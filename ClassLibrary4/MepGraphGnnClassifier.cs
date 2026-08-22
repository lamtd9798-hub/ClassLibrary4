#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ClassLibrary4
{
    /// <summary>
    /// STEP22D.1 - GNN Context Model V2 (angle/topology + performance).
    ///
    /// ONNX contract:
    ///   input "nodes"      : float32 [1, N, F]
    ///   input "adjacency"  : float32 [1, N, N] - row-normalized + self loop
    ///   output "logits"    : float32 [1, classes]
    ///
    /// Node 0 luôn là target pipe. DN của target bị mask khỏi feature.
    /// Neighbor pipes giữ DN one-hot để model học context/topology.
    /// V2 thêm góc tương đối với target và direct-target adjacency.
    /// </summary>
    internal sealed class MepGraphGnnClassifier : IDisposable
    {
        internal sealed class Prediction
        {
            public bool Success { get; set; }
            public string Dn { get; set; } = "";
            public double Confidence { get; set; }
            public string SecondDn { get; set; } = "";
            public double SecondConfidence { get; set; }
            public double Margin => Confidence - SecondConfidence;
            public int TargetPipeIndex { get; set; } = -1;
            public int ContextNodeCount { get; set; }
            public string Message { get; set; } = "";
        }

        private const int BaseFeatureCount = 13;

        private readonly InferenceSession _session;
        private readonly string _nodesInputName;
        private readonly string _adjacencyInputName;
        private readonly string _outputName;
        private readonly List<string> _labels;
        private readonly int _maxNodes;
        private readonly int _featureCount;
        private bool _disposed;

        public string ModelPath { get; }
        public string LabelsPath { get; }
        public int ClassCount => _labels.Count;
        public int MaxNodes => _maxNodes;
        public int FeatureCount => _featureCount;

        public MepGraphGnnClassifier(
            string modelPath,
            string labelsPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath) ||
                !File.Exists(modelPath))
            {
                throw new FileNotFoundException(
                    "Không tìm thấy GNN model ONNX.",
                    modelPath);
            }

            if (string.IsNullOrWhiteSpace(labelsPath) ||
                !File.Exists(labelsPath))
            {
                throw new FileNotFoundException(
                    "Không tìm thấy mep_graph_dn_labels.txt.",
                    labelsPath);
            }

            ModelPath =
                Path.GetFullPath(
                    modelPath);

            LabelsPath =
                Path.GetFullPath(
                    labelsPath);

            _labels =
                File.ReadAllLines(
                        LabelsPath)
                    .Select(
                        x =>
                            (x ?? "")
                                .Trim())
                    .Where(
                        x =>
                            !string.IsNullOrWhiteSpace(
                                x) &&
                            !x.StartsWith(
                                "#",
                                StringComparison.Ordinal))
                    .Select(
                        x =>
                            x.Contains("|")
                                ? x.Split('|')[0]
                                    .Trim()
                                : x)
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .ToList();

            if (_labels.Count < 2)
            {
                throw new InvalidDataException(
                    "GNN labels cần tối thiểu 2 DN class.");
            }

            SessionOptions options =
                new SessionOptions();

            options.GraphOptimizationLevel =
                GraphOptimizationLevel.ORT_ENABLE_ALL;

            _session =
                new InferenceSession(
                    ModelPath,
                    options);

            string nodesName =
                _session.InputMetadata.Keys
                    .FirstOrDefault(
                        x =>
                            string.Equals(
                                x,
                                "nodes",
                                StringComparison.OrdinalIgnoreCase));

            string adjacencyName =
                _session.InputMetadata.Keys
                    .FirstOrDefault(
                        x =>
                            string.Equals(
                                x,
                                "adjacency",
                                StringComparison.OrdinalIgnoreCase));

            _nodesInputName =
                nodesName ??
                _session.InputMetadata.Keys
                    .FirstOrDefault() ??
                "";

            _adjacencyInputName =
                adjacencyName ??
                _session.InputMetadata.Keys
                    .Skip(1)
                    .FirstOrDefault() ??
                "";

            _outputName =
                _session.OutputMetadata.Keys
                    .FirstOrDefault(
                        x =>
                            string.Equals(
                                x,
                                "logits",
                                StringComparison.OrdinalIgnoreCase)) ??
                _session.OutputMetadata.Keys
                    .FirstOrDefault() ??
                "";

            if (string.IsNullOrWhiteSpace(
                    _nodesInputName) ||
                string.IsNullOrWhiteSpace(
                    _adjacencyInputName) ||
                string.IsNullOrWhiteSpace(
                    _outputName))
            {
                throw new InvalidDataException(
                    "GNN model thiếu nodes / adjacency / logits.");
            }

            int[] nodeDims =
                _session.InputMetadata[
                        _nodesInputName]
                    .Dimensions
                    .ToArray();

            int[] adjacencyDims =
                _session.InputMetadata[
                        _adjacencyInputName]
                    .Dimensions
                    .ToArray();

            if (nodeDims.Length != 3 ||
                adjacencyDims.Length != 3)
            {
                throw new NotSupportedException(
                    "STEP22B cần GNN ONNX input rank 3: nodes [1,N,F], adjacency [1,N,N].");
            }

            _maxNodes =
                nodeDims[1] > 0
                    ? nodeDims[1]
                    : 24;

            _featureCount =
                nodeDims[2] > 0
                    ? nodeDims[2]
                    : BaseFeatureCount +
                      _labels.Count;

            if (_featureCount !=
                BaseFeatureCount +
                _labels.Count)
            {
                throw new InvalidDataException(
                    "Feature count model không khớp labels. " +
                    "Model F=" +
                    _featureCount.ToString(
                        CultureInfo.InvariantCulture) +
                    ", expected=" +
                    (BaseFeatureCount +
                     _labels.Count).ToString(
                        CultureInfo.InvariantCulture) +
                    ".");
            }

            if (adjacencyDims[1] > 0 &&
                adjacencyDims[1] !=
                    _maxNodes)
            {
                throw new InvalidDataException(
                    "Adjacency N không khớp nodes N.");
            }
        }

        public Prediction PredictDn(
            MepGraphSnapshot snapshot,
            Point3d position,
            Extents3d? deviceExtents)
        {
            Prediction result =
                new Prediction();

            if (_disposed)
            {
                result.Message =
                    "GNN classifier đã Dispose.";

                return result;
            }

            if (snapshot == null ||
                snapshot.Pipes.Count == 0)
            {
                result.Message =
                    "Graph chưa có pipe node.";

                return result;
            }

            try
            {
                int targetIndex =
                    FindTargetPipe(
                        snapshot,
                        position,
                        deviceExtents);

                if (targetIndex < 0)
                {
                    result.Message =
                        "Không tìm được target pipe gần thiết bị.";

                    return result;
                }

                List<int> context =
                    BuildEgoContext(
                        snapshot,
                        targetIndex,
                        _maxNodes);

                if (context.Count == 0)
                {
                    result.Message =
                        "GNN context đang trống.";

                    return result;
                }

                // Target luôn ở node 0.
                if (context[0] !=
                    targetIndex)
                {
                    context.Remove(
                        targetIndex);

                    context.Insert(
                        0,
                        targetIndex);
                }

                DenseTensor<float> nodes =
                    BuildNodeTensor(
                        snapshot,
                        context,
                        targetIndex);

                DenseTensor<float> adjacency =
                    BuildAdjacencyTensor(
                        snapshot,
                        context);

                List<NamedOnnxValue> inputs =
                    new List<NamedOnnxValue>
                    {
                        NamedOnnxValue.CreateFromTensor(
                            _nodesInputName,
                            nodes),

                        NamedOnnxValue.CreateFromTensor(
                            _adjacencyInputName,
                            adjacency)
                    };

                using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs =
                    _session.Run(
                        inputs))
                {
                    DisposableNamedOnnxValue output =
                        outputs.FirstOrDefault(
                            x =>
                                string.Equals(
                                    x.Name,
                                    _outputName,
                                    StringComparison.Ordinal)) ??
                        outputs.FirstOrDefault();

                    if (output == null)
                    {
                        result.Message =
                            "GNN model không trả output.";

                        return result;
                    }

                    float[] logits =
                        output
                            .AsTensor<float>()
                            .ToArray();

                    if (logits == null ||
                        logits.Length == 0)
                    {
                        result.Message =
                            "GNN logits trống.";

                        return result;
                    }

                    if (logits.Length !=
                        _labels.Count)
                    {
                        result.Message =
                            "GNN output=" +
                            logits.Length.ToString(
                                CultureInfo.InvariantCulture) +
                            " nhưng labels=" +
                            _labels.Count.ToString(
                                CultureInfo.InvariantCulture) +
                            ".";

                        return result;
                    }

                    double[] probs =
                        Softmax(
                            logits);

                    int[] ordered =
                        Enumerable.Range(
                                0,
                                probs.Length)
                            .OrderByDescending(
                                i =>
                                    probs[i])
                            .ToArray();

                    int best =
                        ordered[0];

                    int second =
                        ordered.Length > 1
                            ? ordered[1]
                            : best;

                    result.Success =
                        true;

                    result.Dn =
                        _labels[best];

                    result.Confidence =
                        probs[best];

                    result.SecondDn =
                        _labels[second];

                    result.SecondConfidence =
                        ordered.Length > 1
                            ? probs[second]
                            : 0.0;

                    result.TargetPipeIndex =
                        targetIndex;

                    result.ContextNodeCount =
                        context.Count;

                    result.Message =
                        "GNN context " +
                        context.Count.ToString(
                            CultureInfo.InvariantCulture) +
                        " nodes";
                }
            }
            catch (Exception ex)
            {
                result.Message =
                    ex.GetType().Name +
                    ": " +
                    ex.Message;
            }

            return result;
        }

        private DenseTensor<float> BuildNodeTensor(
            MepGraphSnapshot snapshot,
            List<int> context,
            int targetIndex)
        {
            DenseTensor<float> tensor =
                new DenseTensor<float>(
                    new[]
                    {
                        1,
                        _maxNodes,
                        _featureCount
                    });

            MepGraphPipeNode targetPipe =
                targetIndex >= 0 &&
                targetIndex < snapshot.Pipes.Count
                    ? snapshot.Pipes[targetIndex]
                    : null;

            for (int local = 0;
                local < context.Count &&
                local < _maxNodes;
                local++)
            {
                int global =
                    context[local];

                if (global < 0 ||
                    global >=
                        snapshot.Pipes.Count)
                {
                    continue;
                }

                MepGraphPipeNode pipe =
                    snapshot.Pipes[global];

                bool isTarget =
                    global ==
                    targetIndex;

                bool directlyConnectedToTarget =
                    !isTarget &&
                    pipe.Neighbors != null &&
                    pipe.Neighbors.Contains(
                        targetIndex);

                float[] features =
                    BuildFeatures(
                        pipe,
                        targetPipe,
                        isTarget,
                        directlyConnectedToTarget);

                for (int f = 0;
                    f < features.Length &&
                    f < _featureCount;
                    f++)
                {
                    tensor[
                        0,
                        local,
                        f] =
                        features[f];
                }
            }

            return tensor;
        }

        /// <summary>
        /// STEP22D.1 GNN V2 feature contract.
        ///
        /// Giữ nguyên 10 feature cũ để không mất thông tin layer/AI/DN,
        /// sau đó thêm 3 feature topology-hình học:
        ///   [10] |cos(theta)| : độ song song với target pipe
        ///   [11] |sin(theta)| : độ vuông góc với target pipe
        ///   [12] directTarget : có nối trực tiếp target hay không
        ///
        /// Dùng trị tuyệt đối cho dot/cross để feature không đổi khi
        /// Start/End của LINE bị đảo chiều trong DWG.
        /// </summary>
        private float[] BuildFeatures(
            MepGraphPipeNode pipe,
            MepGraphPipeNode targetPipe,
            bool isTarget,
            bool directlyConnectedToTarget)
        {
            float[] features =
                new float[
                    _featureCount];

            if (pipe == null)
                return features;

            double dx =
                pipe.End.X -
                pipe.Start.X;

            double dy =
                pipe.End.Y -
                pipe.Start.Y;

            double planar =
                Math.Sqrt(
                    dx * dx +
                    dy * dy);

            if (planar <
                1e-6)
            {
                planar =
                    1.0;
            }

            features[0] =
                Clamp01(
                    Math.Log10(
                        Math.Max(
                            1.0,
                            pipe.Length)) /
                    5.0);

            features[1] =
                (float)Math.Max(
                    -1.0,
                    Math.Min(
                        1.0,
                        dx /
                        planar));

            features[2] =
                (float)Math.Max(
                    -1.0,
                    Math.Min(
                        1.0,
                        dy /
                        planar));

            features[3] =
                (float)Math.Abs(
                    dx /
                    planar);

            features[4] =
                (float)Math.Abs(
                    dy /
                    planar);

            features[5] =
                Clamp01(
                    (pipe.Neighbors?.Count ?? 0) /
                    6.0);

            features[6] =
                pipe.IsAiOverlay
                    ? 1.0f
                    : 0.0f;

            features[7] =
                pipe.LayerLooksLikePipe
                    ? 1.0f
                    : 0.0f;

            features[8] =
                isTarget
                    ? 1.0f
                    : 0.0f;

            bool allowDnFeature =
                !isTarget &&
                !string.IsNullOrWhiteSpace(
                    pipe.Dn) &&
                pipe.DnConfidence >=
                    0.70;

            features[9] =
                allowDnFeature
                    ? (float)Math.Max(
                        0.0,
                        Math.Min(
                            1.0,
                            pipe.DnConfidence))
                    : 0.0f;

            // --------------------------------------------------------
            // V2: góc tương đối với target pipe.
            //
            // Không dùng signed dot/cross trực tiếp vì hướng Start->End
            // của entity CAD có thể bị đảo. |cos| / |sin| ổn định hơn:
            //   |cos| ~ 1, |sin| ~ 0 : song song / thẳng hàng
            //   |cos| ~ 0, |sin| ~ 1 : gần vuông góc
            // --------------------------------------------------------
            if (targetPipe != null)
            {
                double tdx =
                    targetPipe.End.X -
                    targetPipe.Start.X;

                double tdy =
                    targetPipe.End.Y -
                    targetPipe.Start.Y;

                double targetPlanar =
                    Math.Sqrt(
                        tdx * tdx +
                        tdy * tdy);

                if (targetPlanar >=
                    1e-6)
                {
                    double denominator =
                        planar *
                        targetPlanar;

                    double dot =
                        (dx * tdx +
                         dy * tdy) /
                        denominator;

                    double cross =
                        (dx * tdy -
                         dy * tdx) /
                        denominator;

                    dot =
                        Math.Max(
                            -1.0,
                            Math.Min(
                                1.0,
                                dot));

                    cross =
                        Math.Max(
                            -1.0,
                            Math.Min(
                                1.0,
                                cross));

                    features[10] =
                        (float)Math.Abs(
                            dot);

                    features[11] =
                        (float)Math.Abs(
                            cross);
                }
            }

            features[12] =
                directlyConnectedToTarget
                    ? 1.0f
                    : 0.0f;

            if (allowDnFeature)
            {
                int dnIndex =
                    _labels.FindIndex(
                        x =>
                            string.Equals(
                                x,
                                pipe.Dn,
                                StringComparison.OrdinalIgnoreCase));

                if (dnIndex >= 0)
                {
                    features[
                        BaseFeatureCount +
                        dnIndex] =
                        1.0f;
                }
            }

            return features;
        }

        private DenseTensor<float> BuildAdjacencyTensor(
            MepGraphSnapshot snapshot,
            List<int> context)
        {
            DenseTensor<float> tensor =
                new DenseTensor<float>(
                    new[]
                    {
                        1,
                        _maxNodes,
                        _maxNodes
                    });

            Dictionary<int, int> globalToLocal =
                new Dictionary<int, int>();

            int active =
                Math.Min(
                    context.Count,
                    _maxNodes);

            for (int i = 0;
                i < active;
                i++)
            {
                globalToLocal[
                    context[i]] =
                    i;
            }

            // --------------------------------------------------------
            // STEP22D.1:
            // Bỏ float[,] raw trung gian.
            // Ghi trực tiếp adjacency đã row-normalize vào DenseTensor.
            //
            // Graph Engine đã Distinct() danh sách Neighbors, vì vậy
            // degree = số neighbor hợp lệ trong ego-graph + self-loop.
            // --------------------------------------------------------
            for (int i = 0;
                i < active;
                i++)
            {
                int global =
                    context[i];

                if (global < 0 ||
                    global >= snapshot.Pipes.Count)
                {
                    continue;
                }

                MepGraphPipeNode pipe =
                    snapshot.Pipes[global];

                int localNeighborCount =
                    0;

                if (pipe.Neighbors != null)
                {
                    foreach (int neighbor
                        in pipe.Neighbors)
                    {
                        if (globalToLocal.TryGetValue(
                                neighbor,
                                out int localNeighbor) &&
                            localNeighbor != i)
                        {
                            localNeighborCount++;
                        }
                    }
                }

                float weight =
                    1.0f /
                    Math.Max(
                        1,
                        localNeighborCount +
                        1);

                // Self-loop.
                tensor[
                    0,
                    i,
                    i] =
                    weight;

                if (pipe.Neighbors == null)
                    continue;

                foreach (int neighbor
                    in pipe.Neighbors)
                {
                    if (!globalToLocal.TryGetValue(
                            neighbor,
                            out int localNeighbor) ||
                        localNeighbor == i)
                    {
                        continue;
                    }

                    tensor[
                        0,
                        i,
                        localNeighbor] =
                        weight;
                }
            }

            return tensor;
        }

        private static List<int> BuildEgoContext(
            MepGraphSnapshot snapshot,
            int targetIndex,
            int maxNodes)
        {
            List<int> result =
                new List<int>();

            HashSet<int> visited =
                new HashSet<int>();

            Queue<(int Index, int Depth)> queue =
                new Queue<(int, int)>();

            queue.Enqueue(
                (targetIndex,
                 0));

            visited.Add(
                targetIndex);

            while (queue.Count > 0 &&
                result.Count <
                    maxNodes)
            {
                var current =
                    queue.Dequeue();

                result.Add(
                    current.Index);

                if (current.Depth >= 3)
                    continue;

                if (current.Index < 0 ||
                    current.Index >=
                        snapshot.Pipes.Count)
                {
                    continue;
                }

                IEnumerable<int> neighbors =
                    snapshot.Pipes[
                            current.Index]
                        .Neighbors
                        .Where(
                            n =>
                                n >= 0 &&
                                n <
                                snapshot.Pipes.Count)
                        .OrderByDescending(
                            n =>
                                !string.IsNullOrWhiteSpace(
                                    snapshot.Pipes[n].Dn))
                        .ThenByDescending(
                            n =>
                                snapshot.Pipes[n].DnConfidence)
                        .ThenBy(
                            n =>
                                n);

                foreach (int n
                    in neighbors)
                {
                    if (!visited.Add(
                            n))
                    {
                        continue;
                    }

                    queue.Enqueue(
                        (n,
                         current.Depth +
                         1));
                }
            }

            return result;
        }

        private static int FindTargetPipe(
            MepGraphSnapshot snapshot,
            Point3d position,
            Extents3d? deviceExtents)
        {
            List<Point3d> refs =
                BuildReferencePoints(
                    position,
                    deviceExtents);

            if (refs.Count == 0)
                return -1;

            int best =
                -1;

            double bestScore =
                double.MaxValue;

            // Đơn vị hiện tại của pipeline đang theo drawing unit (thường mm).
            // Giữ tương thích ngưỡng cũ 1400 nhưng dùng envelope 1500 để lọc thô.
            const double maxSearchRadius =
                1500.0;

            double queryMinX =
                refs.Min(
                    p =>
                        p.X) -
                maxSearchRadius;

            double queryMaxX =
                refs.Max(
                    p =>
                        p.X) +
                maxSearchRadius;

            double queryMinY =
                refs.Min(
                    p =>
                        p.Y) -
                maxSearchRadius;

            double queryMaxY =
                refs.Max(
                    p =>
                        p.Y) +
                maxSearchRadius;

            // Nếu có extents thiết bị thì mở rộng query envelope theo cả block.
            if (deviceExtents.HasValue)
            {
                Extents3d ex =
                    deviceExtents.Value;

                queryMinX =
                    Math.Min(
                        queryMinX,
                        ex.MinPoint.X -
                        maxSearchRadius);

                queryMaxX =
                    Math.Max(
                        queryMaxX,
                        ex.MaxPoint.X +
                        maxSearchRadius);

                queryMinY =
                    Math.Min(
                        queryMinY,
                        ex.MinPoint.Y -
                        maxSearchRadius);

                queryMaxY =
                    Math.Max(
                        queryMaxY,
                        ex.MaxPoint.Y +
                        maxSearchRadius);
            }

            for (int i = 0;
                i < snapshot.Pipes.Count;
                i++)
            {
                MepGraphPipeNode pipe =
                    snapshot.Pipes[i];

                // ----------------------------------------------------
                // LỌC THÔ BẰNG AABB / ENVELOPE.
                //
                // Không dùng midpoint filter vì một pipe rất dài có thể
                // đi sát thiết bị nhưng midpoint nằm xa > 1500.
                // ----------------------------------------------------
                Extents3d pipeEx =
                    pipe.Extents;

                if (pipeEx.MaxPoint.X <
                        queryMinX ||
                    pipeEx.MinPoint.X >
                        queryMaxX ||
                    pipeEx.MaxPoint.Y <
                        queryMinY ||
                    pipeEx.MinPoint.Y >
                        queryMaxY)
                {
                    continue;
                }

                // Chỉ các pipe lọt envelope mới tính khoảng cách segment.
                // Dùng vòng for thay LINQ Min để giảm delegate/enumerator overhead.
                double distance =
                    double.MaxValue;

                for (int r = 0;
                    r < refs.Count;
                    r++)
                {
                    double d =
                        DistancePointToSegment2D(
                            refs[r],
                            pipe.Start,
                            pipe.End);

                    if (d <
                        distance)
                    {
                        distance =
                            d;

                        if (distance <=
                            1e-6)
                        {
                            break;
                        }
                    }
                }

                bool overlap =
                    deviceExtents.HasValue &&
                    ExtentsOverlapExpanded(
                        pipe.Extents,
                        deviceExtents.Value,
                        120.0);

                double score =
                    distance;

                if (overlap)
                    score -= 300.0;

                score -=
                    pipe.DnConfidence *
                    30.0;

                if (score <
                    bestScore)
                {
                    bestScore =
                        score;

                    best =
                        i;
                }
            }

            if (bestScore >
                1400.0)
            {
                return -1;
            }

            return best;
        }

        private static List<Point3d> BuildReferencePoints(
            Point3d insertion,
            Extents3d? extents)
        {
            List<Point3d> points =
                new List<Point3d>
                {
                    new Point3d(
                        insertion.X,
                        insertion.Y,
                        0.0)
                };

            if (!extents.HasValue)
                return points;

            Extents3d ex =
                extents.Value;

            double cx =
                (ex.MinPoint.X +
                 ex.MaxPoint.X) *
                0.5;

            double cy =
                (ex.MinPoint.Y +
                 ex.MaxPoint.Y) *
                0.5;

            points.Add(
                new Point3d(
                    cx,
                    cy,
                    0.0));

            points.Add(
                new Point3d(
                    ex.MinPoint.X,
                    cy,
                    0.0));

            points.Add(
                new Point3d(
                    ex.MaxPoint.X,
                    cy,
                    0.0));

            points.Add(
                new Point3d(
                    cx,
                    ex.MinPoint.Y,
                    0.0));

            points.Add(
                new Point3d(
                    cx,
                    ex.MaxPoint.Y,
                    0.0));

            return points;
        }

        private static double DistancePointToSegment2D(
            Point3d p,
            Point3d a,
            Point3d b)
        {
            double vx =
                b.X -
                a.X;

            double vy =
                b.Y -
                a.Y;

            double wx =
                p.X -
                a.X;

            double wy =
                p.Y -
                a.Y;

            double vv =
                vx *
                vx +
                vy *
                vy;

            if (vv <=
                1e-9)
            {
                return
                    PlanDistance(
                        p,
                        a);
            }

            double t =
                (wx *
                 vx +
                 wy *
                 vy) /
                vv;

            t =
                Math.Max(
                    0.0,
                    Math.Min(
                        1.0,
                        t));

            Point3d q =
                new Point3d(
                    a.X +
                    vx *
                    t,
                    a.Y +
                    vy *
                    t,
                    0.0);

            return
                PlanDistance(
                    p,
                    q);
        }

        private static bool ExtentsOverlapExpanded(
            Extents3d a,
            Extents3d b,
            double margin)
        {
            return
                a.MinPoint.X <=
                    b.MaxPoint.X +
                    margin &&
                a.MaxPoint.X >=
                    b.MinPoint.X -
                    margin &&
                a.MinPoint.Y <=
                    b.MaxPoint.Y +
                    margin &&
                a.MaxPoint.Y >=
                    b.MinPoint.Y -
                    margin;
        }

        private static double PlanDistance(
            Point3d a,
            Point3d b)
        {
            double dx =
                a.X -
                b.X;

            double dy =
                a.Y -
                b.Y;

            return
                Math.Sqrt(
                    dx * dx +
                    dy * dy);
        }

        private static float Clamp01(
            double value)
        {
            return
                (float)Math.Max(
                    0.0,
                    Math.Min(
                        1.0,
                        value));
        }

        private static double[] Softmax(
            float[] logits)
        {
            double max =
                logits.Max();

            double[] exp =
                new double[
                    logits.Length];

            double sum =
                0.0;

            for (int i = 0;
                i < logits.Length;
                i++)
            {
                exp[i] =
                    Math.Exp(
                        logits[i] -
                        max);

                sum +=
                    exp[i];
            }

            if (sum <=
                0.0)
            {
                sum =
                    1.0;
            }

            for (int i = 0;
                i < exp.Length;
                i++)
            {
                exp[i] /=
                    sum;
            }

            return exp;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed =
                true;

            try
            {
                _session?.Dispose();
            }
            catch
            {
            }
        }
    }
}