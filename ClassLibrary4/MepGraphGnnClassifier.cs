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
    /// STEP30A - GNN V3 EDGE-AWARE + SELECTIVE GRAPH SAMPLING.
    ///
    /// Backward compatible:
    /// - V2 model: nodes [1,N,F], adjacency [1,N,N] -> logits
    /// - V3 model: nodes [1,N,F], adjacency [1,N,N],
    ///             edge_attr [1,N,N,E], optional node_mask [1,N] -> logits
    ///
    /// V3 không thay Graph deterministic. Nó chỉ là evidence bổ sung cho DN fusion.
    /// Target pipe luôn ở node 0 và DN của target bị mask khỏi node/edge features
    /// để tránh label leakage khi train.
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
            public int ContextEdgeCount { get; set; }
            public bool EdgeAware { get; set; }
            public string ModelContract { get; set; } = "";
            public string Message { get; set; } = "";
        }

        private const int V2BaseFeatureCount = 13;
        private const int V3BaseFeatureCount = 16;
        private const int V3ExpectedEdgeFeatureCount = 12;
        private const int V2MaxDepth = 3;
        private const int V3MaxDepth = 5;

        private readonly InferenceSession _session;
        private readonly string _nodesInputName;
        private readonly string _adjacencyInputName;
        private readonly string _edgeAttrInputName;
        private readonly string _nodeMaskInputName;
        private readonly string _outputName;
        private readonly List<string> _labels;
        private readonly int _maxNodes;
        private readonly int _featureCount;
        private readonly int _edgeFeatureCount;
        private readonly bool _edgeAware;
        private bool _disposed;

        public string ModelPath { get; }
        public string LabelsPath { get; }
        public int ClassCount => _labels.Count;
        public int MaxNodes => _maxNodes;
        public int FeatureCount => _featureCount;
        public int EdgeFeatureCount => _edgeFeatureCount;
        public bool IsEdgeAware => _edgeAware;
        public string ModelContract => _edgeAware ? "V3_EDGE_AWARE" : "V2_ADJACENCY";

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

            ModelPath = Path.GetFullPath(modelPath);
            LabelsPath = Path.GetFullPath(labelsPath);

            _labels = File
                .ReadAllLines(LabelsPath)
                .Select(x => (x ?? "").Trim())
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x) &&
                    !x.StartsWith("#", StringComparison.Ordinal))
                .Select(x =>
                    x.Contains("|")
                        ? x.Split('|')[0].Trim()
                        : x)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (_labels.Count < 2)
            {
                throw new InvalidDataException(
                    "GNN labels cần tối thiểu 2 DN class.");
            }

            SessionOptions options = new SessionOptions();
            options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

            _session = new InferenceSession(ModelPath, options);

            _nodesInputName = FindInputName(
                _session,
                new[] { "nodes", "node_features", "x" },
                0);

            _adjacencyInputName = FindInputName(
                _session,
                new[] { "adjacency", "adj", "a" },
                1);

            _edgeAttrInputName = FindOptionalInputName(
                _session,
                new[] { "edge_attr", "edge_features", "edge_feature" });

            _nodeMaskInputName = FindOptionalInputName(
                _session,
                new[] { "node_mask", "mask" });

            _outputName = _session.OutputMetadata.Keys
                .FirstOrDefault(x =>
                    string.Equals(
                        x,
                        "logits",
                        StringComparison.OrdinalIgnoreCase)) ??
                _session.OutputMetadata.Keys.FirstOrDefault() ??
                "";

            if (string.IsNullOrWhiteSpace(_nodesInputName) ||
                string.IsNullOrWhiteSpace(_adjacencyInputName) ||
                string.IsNullOrWhiteSpace(_outputName))
            {
                throw new InvalidDataException(
                    "GNN model thiếu nodes / adjacency / logits.");
            }

            int[] nodeDims = _session.InputMetadata[_nodesInputName]
                .Dimensions
                .ToArray();

            int[] adjacencyDims = _session.InputMetadata[_adjacencyInputName]
                .Dimensions
                .ToArray();

            if (nodeDims.Length != 3 ||
                adjacencyDims.Length != 3)
            {
                throw new NotSupportedException(
                    "GNN cần nodes [1,N,F] và adjacency [1,N,N].");
            }

            _maxNodes = nodeDims[1] > 0
                ? nodeDims[1]
                : 24;

            _featureCount = nodeDims[2] > 0
                ? nodeDims[2]
                : V2BaseFeatureCount + _labels.Count;

            if (adjacencyDims[1] > 0 &&
                adjacencyDims[1] != _maxNodes)
            {
                throw new InvalidDataException(
                    "Adjacency N không khớp nodes N.");
            }

            if (adjacencyDims[2] > 0 &&
                adjacencyDims[2] != _maxNodes)
            {
                throw new InvalidDataException(
                    "Adjacency N×N không khớp nodes N.");
            }

            _edgeAware = !string.IsNullOrWhiteSpace(_edgeAttrInputName);

            if (_edgeAware)
            {
                int[] edgeDims = _session.InputMetadata[_edgeAttrInputName]
                    .Dimensions
                    .ToArray();

                if (edgeDims.Length != 4)
                {
                    throw new NotSupportedException(
                        "GNN V3 edge_attr phải có rank 4: [1,N,N,E].");
                }

                if (edgeDims[1] > 0 && edgeDims[1] != _maxNodes)
                {
                    throw new InvalidDataException(
                        "GNN V3 edge_attr N không khớp nodes N.");
                }

                if (edgeDims[2] > 0 && edgeDims[2] != _maxNodes)
                {
                    throw new InvalidDataException(
                        "GNN V3 edge_attr N×N không khớp nodes N.");
                }

                _edgeFeatureCount = edgeDims[3] > 0
                    ? edgeDims[3]
                    : V3ExpectedEdgeFeatureCount;

                int expectedNodeFeatureCount =
                    V3BaseFeatureCount + _labels.Count;

                if (_featureCount != expectedNodeFeatureCount)
                {
                    throw new InvalidDataException(
                        "GNN V3 node feature count không khớp labels. Model F=" +
                        _featureCount.ToString(CultureInfo.InvariantCulture) +
                        ", expected=" +
                        expectedNodeFeatureCount.ToString(CultureInfo.InvariantCulture) +
                        ".");
                }

                if (_edgeFeatureCount != V3ExpectedEdgeFeatureCount)
                {
                    throw new InvalidDataException(
                        "GNN V3 edge feature count phải là " +
                        V3ExpectedEdgeFeatureCount.ToString(CultureInfo.InvariantCulture) +
                        ", model hiện tại E=" +
                        _edgeFeatureCount.ToString(CultureInfo.InvariantCulture) +
                        ".");
                }

                if (!string.IsNullOrWhiteSpace(_nodeMaskInputName))
                {
                    int[] maskDims = _session.InputMetadata[_nodeMaskInputName]
                        .Dimensions
                        .ToArray();

                    if (maskDims.Length != 2)
                    {
                        throw new NotSupportedException(
                            "GNN V3 node_mask phải có rank 2: [1,N].");
                    }

                    if (maskDims[1] > 0 && maskDims[1] != _maxNodes)
                    {
                        throw new InvalidDataException(
                            "GNN V3 node_mask N không khớp nodes N.");
                    }
                }
            }
            else
            {
                _edgeFeatureCount = 0;

                int expectedV2 =
                    V2BaseFeatureCount + _labels.Count;

                if (_featureCount != expectedV2)
                {
                    throw new InvalidDataException(
                        "GNN V2 feature count không khớp labels. Model F=" +
                        _featureCount.ToString(CultureInfo.InvariantCulture) +
                        ", expected=" +
                        expectedV2.ToString(CultureInfo.InvariantCulture) +
                        ".");
                }
            }
        }

        public Prediction PredictDn(
            MepGraphSnapshot snapshot,
            Point3d position,
            Extents3d? deviceExtents)
        {
            Prediction result = new Prediction
            {
                EdgeAware = _edgeAware,
                ModelContract = ModelContract
            };

            if (_disposed)
            {
                result.Message = "GNN classifier đã Dispose.";
                return result;
            }

            if (snapshot == null ||
                snapshot.Pipes == null ||
                snapshot.Pipes.Count == 0)
            {
                result.Message = "Graph chưa có pipe node.";
                return result;
            }

            try
            {
                int targetIndex = FindTargetPipe(
                    snapshot,
                    position,
                    deviceExtents);

                if (targetIndex < 0)
                {
                    result.Message = "Không tìm được target pipe gần thiết bị.";
                    return result;
                }

                List<int> context = _edgeAware
                    ? BuildSelectiveContext(
                        snapshot,
                        targetIndex,
                        _maxNodes)
                    : BuildEgoContext(
                        snapshot,
                        targetIndex,
                        _maxNodes);

                if (context == null || context.Count == 0)
                {
                    result.Message = "GNN context đang trống.";
                    return result;
                }

                EnsureTargetAtZero(context, targetIndex);

                DenseTensor<float> nodes = _edgeAware
                    ? BuildV3NodeTensor(
                        snapshot,
                        context,
                        targetIndex)
                    : BuildV2NodeTensor(
                        snapshot,
                        context,
                        targetIndex);

                DenseTensor<float> adjacency = BuildAdjacencyTensor(
                    snapshot,
                    context,
                    out int edgeCount);

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

                if (_edgeAware)
                {
                    DenseTensor<float> edgeAttr = BuildEdgeAttrTensor(
                        snapshot,
                        context,
                        targetIndex);

                    inputs.Add(
                        NamedOnnxValue.CreateFromTensor(
                            _edgeAttrInputName,
                            edgeAttr));

                    if (!string.IsNullOrWhiteSpace(_nodeMaskInputName))
                    {
                        DenseTensor<float> nodeMask = BuildNodeMaskTensor(context);

                        inputs.Add(
                            NamedOnnxValue.CreateFromTensor(
                                _nodeMaskInputName,
                                nodeMask));
                    }
                }

                using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs =
                    _session.Run(inputs))
                {
                    DisposableNamedOnnxValue output = outputs
                        .FirstOrDefault(x =>
                            string.Equals(
                                x.Name,
                                _outputName,
                                StringComparison.Ordinal)) ??
                        outputs.FirstOrDefault();

                    if (output == null)
                    {
                        result.Message = "GNN model không trả output.";
                        return result;
                    }

                    float[] logits = output
                        .AsTensor<float>()
                        .ToArray();

                    if (logits == null || logits.Length == 0)
                    {
                        result.Message = "GNN logits trống.";
                        return result;
                    }

                    if (logits.Length != _labels.Count)
                    {
                        result.Message =
                            "GNN output=" +
                            logits.Length.ToString(CultureInfo.InvariantCulture) +
                            " nhưng labels=" +
                            _labels.Count.ToString(CultureInfo.InvariantCulture) +
                            ".";

                        return result;
                    }

                    double[] probs = Softmax(logits);

                    int best = 0;
                    int second = -1;
                    double bestP = probs[0];
                    double secondP = double.NegativeInfinity;

                    for (int i = 1; i < probs.Length; i++)
                    {
                        double p = probs[i];

                        if (p > bestP)
                        {
                            second = best;
                            secondP = bestP;
                            best = i;
                            bestP = p;
                        }
                        else if (p > secondP)
                        {
                            second = i;
                            secondP = p;
                        }
                    }

                    result.Success = true;
                    result.Dn = _labels[best];
                    result.Confidence = bestP;
                    result.SecondDn = second >= 0 ? _labels[second] : "";
                    result.SecondConfidence = second >= 0 ? secondP : 0.0;
                    result.TargetPipeIndex = targetIndex;
                    result.ContextNodeCount = Math.Min(context.Count, _maxNodes);
                    result.ContextEdgeCount = edgeCount;
                    result.Message = _edgeAware
                        ? "GNN V3 edge-aware: " +
                          result.ContextNodeCount.ToString(CultureInfo.InvariantCulture) +
                          " nodes / " +
                          edgeCount.ToString(CultureInfo.InvariantCulture) +
                          " directed edges"
                        : "GNN V2 context " +
                          result.ContextNodeCount.ToString(CultureInfo.InvariantCulture) +
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

        private DenseTensor<float> BuildV2NodeTensor(
            MepGraphSnapshot snapshot,
            List<int> context,
            int targetIndex)
        {
            DenseTensor<float> tensor =
                new DenseTensor<float>(
                    new[] { 1, _maxNodes, _featureCount });

            MepGraphPipeNode targetPipe = GetPipe(snapshot, targetIndex);
            int active = Math.Min(context.Count, _maxNodes);

            for (int local = 0; local < active; local++)
            {
                int global = context[local];
                MepGraphPipeNode pipe = GetPipe(snapshot, global);

                if (pipe == null)
                    continue;

                bool isTarget = global == targetIndex;
                bool directTarget =
                    !isTarget &&
                    pipe.Neighbors != null &&
                    pipe.Neighbors.Contains(targetIndex);

                float[] features = BuildV2Features(
                    pipe,
                    targetPipe,
                    isTarget,
                    directTarget);

                for (int f = 0; f < features.Length && f < _featureCount; f++)
                {
                    tensor[0, local, f] = features[f];
                }
            }

            return tensor;
        }

        private float[] BuildV2Features(
            MepGraphPipeNode pipe,
            MepGraphPipeNode targetPipe,
            bool isTarget,
            bool directlyConnectedToTarget)
        {
            float[] features = new float[_featureCount];

            if (pipe == null)
                return features;

            double dx = pipe.End.X - pipe.Start.X;
            double dy = pipe.End.Y - pipe.Start.Y;
            double planar = Math.Sqrt(dx * dx + dy * dy);

            if (planar < 1e-6)
                planar = 1.0;

            features[0] = Clamp01(
                Math.Log10(Math.Max(1.0, pipe.Length)) / 5.0);
            features[1] = ClampSigned(dx / planar);
            features[2] = ClampSigned(dy / planar);
            features[3] = (float)Math.Abs(dx / planar);
            features[4] = (float)Math.Abs(dy / planar);
            features[5] = Clamp01((pipe.Neighbors?.Count ?? 0) / 6.0);
            features[6] = pipe.IsAiOverlay ? 1.0f : 0.0f;
            features[7] = pipe.LayerLooksLikePipe ? 1.0f : 0.0f;
            features[8] = isTarget ? 1.0f : 0.0f;

            bool allowDnFeature =
                !isTarget &&
                !string.IsNullOrWhiteSpace(pipe.Dn) &&
                pipe.DnConfidence >= 0.70;

            features[9] = allowDnFeature
                ? Clamp01(pipe.DnConfidence)
                : 0.0f;

            FillRelativeAngleFeatures(
                pipe,
                targetPipe,
                features,
                10,
                11);

            features[12] = directlyConnectedToTarget ? 1.0f : 0.0f;

            if (allowDnFeature)
            {
                int dnIndex = FindDnLabelIndex(pipe.Dn);

                if (dnIndex >= 0)
                {
                    features[V2BaseFeatureCount + dnIndex] = 1.0f;
                }
            }

            return features;
        }

        private DenseTensor<float> BuildV3NodeTensor(
            MepGraphSnapshot snapshot,
            List<int> context,
            int targetIndex)
        {
            DenseTensor<float> tensor =
                new DenseTensor<float>(
                    new[] { 1, _maxNodes, _featureCount });

            MepGraphPipeNode targetPipe = GetPipe(snapshot, targetIndex);
            int active = Math.Min(context.Count, _maxNodes);

            for (int local = 0; local < active; local++)
            {
                int global = context[local];
                MepGraphPipeNode pipe = GetPipe(snapshot, global);

                if (pipe == null)
                    continue;

                bool isTarget = global == targetIndex;
                bool directTarget =
                    !isTarget &&
                    pipe.Neighbors != null &&
                    pipe.Neighbors.Contains(targetIndex);

                float[] features = BuildV3Features(
                    pipe,
                    targetPipe,
                    isTarget,
                    directTarget);

                for (int f = 0; f < features.Length && f < _featureCount; f++)
                {
                    tensor[0, local, f] = features[f];
                }
            }

            return tensor;
        }

        /// <summary>
        /// V3 NODE FEATURE CONTRACT (16 base + DN one-hot):
        ///  0 logLength
        ///  1 dirX
        ///  2 dirY
        ///  3 absDirX
        ///  4 absDirY
        ///  5 degreeNorm
        ///  6 aiOverlay
        ///  7 pipeLayer
        ///  8 isTarget
        ///  9 reliableDnConfidence (target masked)
        /// 10 absCosToTarget
        /// 11 absSinToTarget
        /// 12 directTarget
        /// 13 verticalRatio
        /// 14 reliableDnKnown (target masked)
        /// 15 junctionFlag (degree >= 3)
        /// 16.. DN one-hot (target masked)
        /// </summary>
        private float[] BuildV3Features(
            MepGraphPipeNode pipe,
            MepGraphPipeNode targetPipe,
            bool isTarget,
            bool directlyConnectedToTarget)
        {
            float[] features = new float[_featureCount];

            if (pipe == null)
                return features;

            double dx = pipe.End.X - pipe.Start.X;
            double dy = pipe.End.Y - pipe.Start.Y;
            double dz = pipe.End.Z - pipe.Start.Z;
            double planar = Math.Sqrt(dx * dx + dy * dy);
            double length3d = Math.Sqrt(dx * dx + dy * dy + dz * dz);

            if (planar < 1e-6)
                planar = 1.0;

            if (length3d < 1e-6)
                length3d = Math.Max(1.0, pipe.Length);

            int degree = pipe.Neighbors?.Count ?? 0;

            features[0] = Clamp01(
                Math.Log10(Math.Max(1.0, pipe.Length)) / 5.0);
            features[1] = ClampSigned(dx / planar);
            features[2] = ClampSigned(dy / planar);
            features[3] = (float)Math.Abs(dx / planar);
            features[4] = (float)Math.Abs(dy / planar);
            features[5] = Clamp01(degree / 6.0);
            features[6] = pipe.IsAiOverlay ? 1.0f : 0.0f;
            features[7] = pipe.LayerLooksLikePipe ? 1.0f : 0.0f;
            features[8] = isTarget ? 1.0f : 0.0f;

            bool allowDnFeature =
                !isTarget &&
                !string.IsNullOrWhiteSpace(pipe.Dn) &&
                pipe.DnConfidence >= 0.70;

            features[9] = allowDnFeature
                ? Clamp01(pipe.DnConfidence)
                : 0.0f;

            FillRelativeAngleFeatures(
                pipe,
                targetPipe,
                features,
                10,
                11);

            features[12] = directlyConnectedToTarget ? 1.0f : 0.0f;
            features[13] = Clamp01(Math.Abs(dz) / Math.Max(1.0, length3d));
            features[14] = allowDnFeature ? 1.0f : 0.0f;
            features[15] = degree >= 3 ? 1.0f : 0.0f;

            if (allowDnFeature)
            {
                int dnIndex = FindDnLabelIndex(pipe.Dn);

                if (dnIndex >= 0)
                {
                    features[V3BaseFeatureCount + dnIndex] = 1.0f;
                }
            }

            return features;
        }

        private DenseTensor<float> BuildAdjacencyTensor(
            MepGraphSnapshot snapshot,
            List<int> context,
            out int directedEdgeCount)
        {
            DenseTensor<float> tensor =
                new DenseTensor<float>(
                    new[] { 1, _maxNodes, _maxNodes });

            Dictionary<int, int> globalToLocal = BuildGlobalToLocal(context);
            int active = Math.Min(context.Count, _maxNodes);
            directedEdgeCount = 0;

            for (int i = 0; i < active; i++)
            {
                int global = context[i];
                MepGraphPipeNode pipe = GetPipe(snapshot, global);

                if (pipe == null)
                    continue;

                List<int> localNeighbors = new List<int>();

                if (pipe.Neighbors != null)
                {
                    foreach (int neighbor in pipe.Neighbors)
                    {
                        if (globalToLocal.TryGetValue(
                                neighbor,
                                out int localNeighbor) &&
                            localNeighbor != i)
                        {
                            localNeighbors.Add(localNeighbor);
                        }
                    }
                }

                localNeighbors = localNeighbors.Distinct().ToList();

                float weight = 1.0f /
                    Math.Max(1, localNeighbors.Count + 1);

                // Self-loop retained for both V2 and V3.
                tensor[0, i, i] = weight;

                foreach (int localNeighbor in localNeighbors)
                {
                    tensor[0, i, localNeighbor] = weight;
                    directedEdgeCount++;
                }
            }

            return tensor;
        }

        /// <summary>
        /// V3 EDGE FEATURE CONTRACT (12):
        ///  0 absCos
        ///  1 absSin
        ///  2 lengthRatio(min/max)
        ///  3 endpointProximity
        ///  4 sameDn (target DN masked)
        ///  5 differentDn (target DN masked)
        ///  6 minDnConfidence (target DN masked)
        ///  7 junctionDegreeNorm
        ///  8 teeLike
        ///  9 elbowLike
        /// 10 reducerLike
        /// 11 sameLayer
        /// </summary>
        private DenseTensor<float> BuildEdgeAttrTensor(
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
                        _maxNodes,
                        _edgeFeatureCount
                    });

            Dictionary<int, int> globalToLocal = BuildGlobalToLocal(context);
            int active = Math.Min(context.Count, _maxNodes);

            for (int localA = 0; localA < active; localA++)
            {
                int globalA = context[localA];
                MepGraphPipeNode a = GetPipe(snapshot, globalA);

                if (a == null || a.Neighbors == null)
                    continue;

                foreach (int globalB in a.Neighbors)
                {
                    if (!globalToLocal.TryGetValue(globalB, out int localB) ||
                        localB == localA)
                    {
                        continue;
                    }

                    MepGraphPipeNode b = GetPipe(snapshot, globalB);

                    if (b == null)
                        continue;

                    float[] edge = BuildEdgeFeatures(
                        a,
                        b,
                        globalA == targetIndex,
                        globalB == targetIndex);

                    for (int e = 0; e < edge.Length && e < _edgeFeatureCount; e++)
                    {
                        tensor[0, localA, localB, e] = edge[e];
                    }
                }
            }

            return tensor;
        }

        private float[] BuildEdgeFeatures(
            MepGraphPipeNode a,
            MepGraphPipeNode b,
            bool aIsTarget,
            bool bIsTarget)
        {
            float[] f = new float[_edgeFeatureCount];

            if (a == null || b == null)
                return f;

            double adx = a.End.X - a.Start.X;
            double ady = a.End.Y - a.Start.Y;
            double bdx = b.End.X - b.Start.X;
            double bdy = b.End.Y - b.Start.Y;

            double al = Math.Sqrt(adx * adx + ady * ady);
            double bl = Math.Sqrt(bdx * bdx + bdy * bdy);

            if (al < 1e-6)
                al = 1.0;

            if (bl < 1e-6)
                bl = 1.0;

            double dot = (adx * bdx + ady * bdy) / (al * bl);
            double cross = (adx * bdy - ady * bdx) / (al * bl);

            dot = Math.Max(-1.0, Math.Min(1.0, dot));
            cross = Math.Max(-1.0, Math.Min(1.0, cross));

            double absCos = Math.Abs(dot);
            double absSin = Math.Abs(cross);

            double lengthA = Math.Max(1.0, a.Length);
            double lengthB = Math.Max(1.0, b.Length);
            double lengthRatio = Math.Min(lengthA, lengthB) /
                Math.Max(lengthA, lengthB);

            double endpointDistance = MinEndpointDistance2D(a, b);
            double endpointProximity = 1.0 -
                Math.Min(1.0, endpointDistance / 300.0);

            bool aDnReliable =
                !aIsTarget &&
                !string.IsNullOrWhiteSpace(a.Dn) &&
                a.DnConfidence >= 0.70;

            bool bDnReliable =
                !bIsTarget &&
                !string.IsNullOrWhiteSpace(b.Dn) &&
                b.DnConfidence >= 0.70;

            bool bothDn = aDnReliable && bDnReliable;
            bool sameDn =
                bothDn &&
                string.Equals(
                    a.Dn,
                    b.Dn,
                    StringComparison.OrdinalIgnoreCase);

            bool differentDn = bothDn && !sameDn;
            double minDnConfidence = bothDn
                ? Math.Min(a.DnConfidence, b.DnConfidence)
                : 0.0;

            int maxDegree = Math.Max(
                a.Neighbors?.Count ?? 0,
                b.Neighbors?.Count ?? 0);

            bool teeLike =
                maxDegree >= 3 &&
                absSin >= 0.35;

            bool elbowLike =
                maxDegree <= 2 &&
                absSin >= 0.25 &&
                absCos <= 0.97;

            bool reducerLike =
                differentDn &&
                absCos >= 0.80;

            bool sameLayer =
                !string.IsNullOrWhiteSpace(a.Layer) &&
                !string.IsNullOrWhiteSpace(b.Layer) &&
                string.Equals(
                    a.Layer,
                    b.Layer,
                    StringComparison.OrdinalIgnoreCase);

            f[0] = Clamp01(absCos);
            f[1] = Clamp01(absSin);
            f[2] = Clamp01(lengthRatio);
            f[3] = Clamp01(endpointProximity);
            f[4] = sameDn ? 1.0f : 0.0f;
            f[5] = differentDn ? 1.0f : 0.0f;
            f[6] = Clamp01(minDnConfidence);
            f[7] = Clamp01(maxDegree / 6.0);
            f[8] = teeLike ? 1.0f : 0.0f;
            f[9] = elbowLike ? 1.0f : 0.0f;
            f[10] = reducerLike ? 1.0f : 0.0f;
            f[11] = sameLayer ? 1.0f : 0.0f;

            return f;
        }

        private DenseTensor<float> BuildNodeMaskTensor(
            List<int> context)
        {
            DenseTensor<float> mask =
                new DenseTensor<float>(
                    new[] { 1, _maxNodes });

            int active = Math.Min(context?.Count ?? 0, _maxNodes);

            for (int i = 0; i < active; i++)
            {
                mask[0, i] = 1.0f;
            }

            return mask;
        }

        /// <summary>
        /// STEP30A SELECTIVE SAMPLING.
        /// Không tăng N mù quáng. Mỗi hop ưu tiên neighbor có thông tin:
        /// - DN seed đáng tin
        /// - junction degree cao
        /// - đổi DN / reducer evidence
        /// - góc rẽ có ý nghĩa
        /// - cùng layer với nhánh đang duyệt
        /// Context luôn connected vì chỉ expand từ node đã được chọn.
        /// </summary>
        private static List<int> BuildSelectiveContext(
            MepGraphSnapshot snapshot,
            int targetIndex,
            int maxNodes)
        {
            List<int> result = new List<int>();
            HashSet<int> visited = new HashSet<int>();
            Queue<(int Index, int Depth)> queue =
                new Queue<(int, int)>();

            queue.Enqueue((targetIndex, 0));
            visited.Add(targetIndex);

            while (queue.Count > 0 && result.Count < maxNodes)
            {
                var current = queue.Dequeue();

                if (current.Index < 0 ||
                    current.Index >= snapshot.Pipes.Count)
                {
                    continue;
                }

                result.Add(current.Index);

                if (current.Depth >= V3MaxDepth ||
                    result.Count >= maxNodes)
                {
                    continue;
                }

                MepGraphPipeNode currentPipe = snapshot.Pipes[current.Index];

                IEnumerable<int> ordered = (currentPipe.Neighbors ?? new List<int>())
                    .Where(n =>
                        n >= 0 &&
                        n < snapshot.Pipes.Count &&
                        !visited.Contains(n))
                    .OrderByDescending(n =>
                        NeighborInformationScore(
                            snapshot,
                            current.Index,
                            n,
                            targetIndex))
                    .ThenBy(n => n);

                foreach (int neighbor in ordered)
                {
                    if (!visited.Add(neighbor))
                        continue;

                    queue.Enqueue((neighbor, current.Depth + 1));
                }
            }

            return result;
        }

        private static double NeighborInformationScore(
            MepGraphSnapshot snapshot,
            int currentIndex,
            int candidateIndex,
            int targetIndex)
        {
            MepGraphPipeNode current = GetPipe(snapshot, currentIndex);
            MepGraphPipeNode candidate = GetPipe(snapshot, candidateIndex);

            if (candidate == null)
                return double.MinValue;

            double score = 0.0;

            bool reliableDn =
                candidateIndex != targetIndex &&
                !string.IsNullOrWhiteSpace(candidate.Dn) &&
                candidate.DnConfidence >= 0.70;

            if (reliableDn)
            {
                score += 70.0 + candidate.DnConfidence * 35.0;
            }

            int degree = candidate.Neighbors?.Count ?? 0;

            if (degree >= 3)
                score += 32.0;
            else if (degree == 2)
                score += 10.0;

            if (current != null)
            {
                if (!string.IsNullOrWhiteSpace(current.Layer) &&
                    string.Equals(
                        current.Layer,
                        candidate.Layer,
                        StringComparison.OrdinalIgnoreCase))
                {
                    score += 9.0;
                }

                if (!string.IsNullOrWhiteSpace(current.Dn) &&
                    !string.IsNullOrWhiteSpace(candidate.Dn) &&
                    currentIndex != targetIndex &&
                    candidateIndex != targetIndex &&
                    current.DnConfidence >= 0.70 &&
                    candidate.DnConfidence >= 0.70 &&
                    !string.Equals(
                        current.Dn,
                        candidate.Dn,
                        StringComparison.OrdinalIgnoreCase))
                {
                    score += 28.0;
                }

                GetAbsAngleFeatures(
                    current,
                    candidate,
                    out double absCos,
                    out double absSin);

                if (absSin >= 0.35)
                    score += 14.0;

                if (absCos >= 0.90)
                    score += 5.0;
            }

            double dz = Math.Abs(candidate.End.Z - candidate.Start.Z);
            double len = Math.Max(1.0, candidate.Start.DistanceTo(candidate.End));

            if (dz / len >= 0.70)
                score += 18.0;

            // Đoạn rất dài thường ít thông tin hơn junction/seed gần target.
            score -= Math.Min(12.0, candidate.Length / 20000.0 * 12.0);

            return score;
        }

        private static List<int> BuildEgoContext(
            MepGraphSnapshot snapshot,
            int targetIndex,
            int maxNodes)
        {
            List<int> result = new List<int>();
            HashSet<int> visited = new HashSet<int>();
            Queue<(int Index, int Depth)> queue =
                new Queue<(int, int)>();

            queue.Enqueue((targetIndex, 0));
            visited.Add(targetIndex);

            while (queue.Count > 0 && result.Count < maxNodes)
            {
                var current = queue.Dequeue();
                result.Add(current.Index);

                if (current.Depth >= V2MaxDepth)
                    continue;

                if (current.Index < 0 ||
                    current.Index >= snapshot.Pipes.Count)
                {
                    continue;
                }

                IEnumerable<int> neighbors = snapshot.Pipes[current.Index]
                    .Neighbors
                    .Where(n =>
                        n >= 0 &&
                        n < snapshot.Pipes.Count)
                    .OrderByDescending(n =>
                        !string.IsNullOrWhiteSpace(snapshot.Pipes[n].Dn))
                    .ThenByDescending(n => snapshot.Pipes[n].DnConfidence)
                    .ThenBy(n => n);

                foreach (int n in neighbors)
                {
                    if (!visited.Add(n))
                        continue;

                    queue.Enqueue((n, current.Depth + 1));
                }
            }

            return result;
        }

        private static int FindTargetPipe(
            MepGraphSnapshot snapshot,
            Point3d position,
            Extents3d? deviceExtents)
        {
            List<Point3d> refs = BuildReferencePoints(position, deviceExtents);

            if (refs.Count == 0)
                return -1;

            int best = -1;
            double bestScore = double.MaxValue;
            const double maxSearchRadius = 1500.0;

            double queryMinX = refs.Min(p => p.X) - maxSearchRadius;
            double queryMaxX = refs.Max(p => p.X) + maxSearchRadius;
            double queryMinY = refs.Min(p => p.Y) - maxSearchRadius;
            double queryMaxY = refs.Max(p => p.Y) + maxSearchRadius;

            if (deviceExtents.HasValue)
            {
                Extents3d ex = deviceExtents.Value;
                queryMinX = Math.Min(queryMinX, ex.MinPoint.X - maxSearchRadius);
                queryMaxX = Math.Max(queryMaxX, ex.MaxPoint.X + maxSearchRadius);
                queryMinY = Math.Min(queryMinY, ex.MinPoint.Y - maxSearchRadius);
                queryMaxY = Math.Max(queryMaxY, ex.MaxPoint.Y + maxSearchRadius);
            }

            for (int i = 0; i < snapshot.Pipes.Count; i++)
            {
                MepGraphPipeNode pipe = snapshot.Pipes[i];

                if (pipe == null)
                    continue;

                Extents3d pipeEx = pipe.Extents;

                if (pipeEx.MaxPoint.X < queryMinX ||
                    pipeEx.MinPoint.X > queryMaxX ||
                    pipeEx.MaxPoint.Y < queryMinY ||
                    pipeEx.MinPoint.Y > queryMaxY)
                {
                    continue;
                }

                double distance = double.MaxValue;

                for (int r = 0; r < refs.Count; r++)
                {
                    double d = DistancePointToSegment2D(
                        refs[r],
                        pipe.Start,
                        pipe.End);

                    if (d < distance)
                    {
                        distance = d;

                        if (distance <= 1e-6)
                            break;
                    }
                }

                bool overlap =
                    deviceExtents.HasValue &&
                    ExtentsOverlapExpanded(
                        pipe.Extents,
                        deviceExtents.Value,
                        120.0);

                double score = distance;

                if (overlap)
                    score -= 300.0;

                score -= pipe.DnConfidence * 30.0;

                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            return bestScore > 1400.0
                ? -1
                : best;
        }

        private static Dictionary<int, int> BuildGlobalToLocal(
            List<int> context)
        {
            Dictionary<int, int> map = new Dictionary<int, int>();
            int active = Math.Min(context?.Count ?? 0, int.MaxValue);

            for (int i = 0; i < active; i++)
            {
                map[context[i]] = i;
            }

            return map;
        }

        private static void EnsureTargetAtZero(
            List<int> context,
            int targetIndex)
        {
            if (context == null || context.Count == 0)
                return;

            if (context[0] == targetIndex)
                return;

            context.Remove(targetIndex);
            context.Insert(0, targetIndex);
        }

        private int FindDnLabelIndex(string dn)
        {
            if (string.IsNullOrWhiteSpace(dn))
                return -1;

            return _labels.FindIndex(x =>
                string.Equals(
                    x,
                    dn,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static void FillRelativeAngleFeatures(
            MepGraphPipeNode pipe,
            MepGraphPipeNode targetPipe,
            float[] features,
            int cosIndex,
            int sinIndex)
        {
            if (pipe == null ||
                targetPipe == null ||
                features == null ||
                cosIndex < 0 ||
                sinIndex < 0 ||
                cosIndex >= features.Length ||
                sinIndex >= features.Length)
            {
                return;
            }

            GetAbsAngleFeatures(
                pipe,
                targetPipe,
                out double absCos,
                out double absSin);

            features[cosIndex] = Clamp01(absCos);
            features[sinIndex] = Clamp01(absSin);
        }

        private static void GetAbsAngleFeatures(
            MepGraphPipeNode a,
            MepGraphPipeNode b,
            out double absCos,
            out double absSin)
        {
            absCos = 0.0;
            absSin = 0.0;

            if (a == null || b == null)
                return;

            double adx = a.End.X - a.Start.X;
            double ady = a.End.Y - a.Start.Y;
            double bdx = b.End.X - b.Start.X;
            double bdy = b.End.Y - b.Start.Y;

            double al = Math.Sqrt(adx * adx + ady * ady);
            double bl = Math.Sqrt(bdx * bdx + bdy * bdy);

            if (al < 1e-6 || bl < 1e-6)
                return;

            double dot = (adx * bdx + ady * bdy) / (al * bl);
            double cross = (adx * bdy - ady * bdx) / (al * bl);

            dot = Math.Max(-1.0, Math.Min(1.0, dot));
            cross = Math.Max(-1.0, Math.Min(1.0, cross));

            absCos = Math.Abs(dot);
            absSin = Math.Abs(cross);
        }

        private static double MinEndpointDistance2D(
            MepGraphPipeNode a,
            MepGraphPipeNode b)
        {
            return Math.Min(
                Math.Min(
                    PlanDistance(a.Start, b.Start),
                    PlanDistance(a.Start, b.End)),
                Math.Min(
                    PlanDistance(a.End, b.Start),
                    PlanDistance(a.End, b.End)));
        }

        private static MepGraphPipeNode GetPipe(
            MepGraphSnapshot snapshot,
            int index)
        {
            if (snapshot == null ||
                snapshot.Pipes == null ||
                index < 0 ||
                index >= snapshot.Pipes.Count)
            {
                return null;
            }

            return snapshot.Pipes[index];
        }

        private static List<Point3d> BuildReferencePoints(
            Point3d insertion,
            Extents3d? extents)
        {
            List<Point3d> points = new List<Point3d>
            {
                new Point3d(insertion.X, insertion.Y, 0.0)
            };

            if (!extents.HasValue)
                return points;

            Extents3d ex = extents.Value;
            double cx = (ex.MinPoint.X + ex.MaxPoint.X) * 0.5;
            double cy = (ex.MinPoint.Y + ex.MaxPoint.Y) * 0.5;

            points.Add(new Point3d(cx, cy, 0.0));
            points.Add(new Point3d(ex.MinPoint.X, cy, 0.0));
            points.Add(new Point3d(ex.MaxPoint.X, cy, 0.0));
            points.Add(new Point3d(cx, ex.MinPoint.Y, 0.0));
            points.Add(new Point3d(cx, ex.MaxPoint.Y, 0.0));

            return points;
        }

        private static double DistancePointToSegment2D(
            Point3d p,
            Point3d a,
            Point3d b)
        {
            double vx = b.X - a.X;
            double vy = b.Y - a.Y;
            double wx = p.X - a.X;
            double wy = p.Y - a.Y;
            double vv = vx * vx + vy * vy;

            if (vv <= 1e-9)
                return PlanDistance(p, a);

            double t = (wx * vx + wy * vy) / vv;
            t = Math.Max(0.0, Math.Min(1.0, t));

            Point3d q = new Point3d(
                a.X + vx * t,
                a.Y + vy * t,
                0.0);

            return PlanDistance(p, q);
        }

        private static bool ExtentsOverlapExpanded(
            Extents3d a,
            Extents3d b,
            double margin)
        {
            return
                a.MinPoint.X <= b.MaxPoint.X + margin &&
                a.MaxPoint.X >= b.MinPoint.X - margin &&
                a.MinPoint.Y <= b.MaxPoint.Y + margin &&
                a.MaxPoint.Y >= b.MinPoint.Y - margin;
        }

        private static double PlanDistance(
            Point3d a,
            Point3d b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static float Clamp01(double value)
        {
            return (float)Math.Max(0.0, Math.Min(1.0, value));
        }

        private static float ClampSigned(double value)
        {
            return (float)Math.Max(-1.0, Math.Min(1.0, value));
        }

        private static double[] Softmax(float[] logits)
        {
            double max = logits.Max();
            double[] exp = new double[logits.Length];
            double sum = 0.0;

            for (int i = 0; i < logits.Length; i++)
            {
                exp[i] = Math.Exp(logits[i] - max);
                sum += exp[i];
            }

            if (sum <= 0.0)
                sum = 1.0;

            for (int i = 0; i < exp.Length; i++)
            {
                exp[i] /= sum;
            }

            return exp;
        }

        private static string FindInputName(
            InferenceSession session,
            IEnumerable<string> preferredNames,
            int fallbackIndex)
        {
            string found = FindOptionalInputName(session, preferredNames);

            if (!string.IsNullOrWhiteSpace(found))
                return found;

            return session.InputMetadata.Keys
                .Skip(Math.Max(0, fallbackIndex))
                .FirstOrDefault() ??
                "";
        }

        private static string FindOptionalInputName(
            InferenceSession session,
            IEnumerable<string> preferredNames)
        {
            if (session == null || preferredNames == null)
                return "";

            foreach (string preferred in preferredNames)
            {
                string found = session.InputMetadata.Keys
                    .FirstOrDefault(x =>
                        string.Equals(
                            x,
                            preferred,
                            StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(found))
                    return found;
            }

            return "";
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

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