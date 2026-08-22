#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace ClassLibrary4
{
    public sealed class MepGraphPipeNode
    {
        public ObjectId Id { get; set; } = ObjectId.Null;
        public string Handle { get; set; } = "";
        public string Layer { get; set; } = "";
        public Point3d Start { get; set; } = Point3d.Origin;
        public Point3d End { get; set; } = Point3d.Origin;
        public Point3d Center { get; set; } = Point3d.Origin;
        public Extents3d Extents { get; set; }
        public double Length { get; set; }
        public string Dn { get; set; } = "";
        public double DnConfidence { get; set; }
        public string DnSource { get; set; } = "";
        public bool IsAiOverlay { get; set; }
        public bool LayerLooksLikePipe { get; set; }
        public List<int> Neighbors { get; } = new List<int>();
    }

    public sealed class MepGraphDeviceNode
    {
        public ObjectId Id { get; set; } = ObjectId.Null;
        public string Handle { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Name { get; set; } = "";
        public string Layer { get; set; } = "";
        public Point3d Position { get; set; } = Point3d.Origin;
        public Extents3d? Extents { get; set; }
        public int PipeIndex { get; set; } = -1;
        public string InferredDn { get; set; } = "";
        public double DnConfidence { get; set; }
    }

    public sealed class MepGraphTextNode
    {
        public ObjectId Id { get; set; } = ObjectId.Null;
        public string Text { get; set; } = "";
        public string Dn { get; set; } = "";
        public Point3d Position { get; set; } = Point3d.Origin;
        public double Rotation { get; set; }
    }

    public sealed class MepGraphSnapshot
    {
        public string DrawingName { get; set; } = "";
        public DateTime BuiltUtc { get; set; } = DateTime.UtcNow;
        public List<MepGraphPipeNode> Pipes { get; } = new List<MepGraphPipeNode>();
        public List<MepGraphDeviceNode> Devices { get; } = new List<MepGraphDeviceNode>();
        public List<MepGraphTextNode> DnTexts { get; } = new List<MepGraphTextNode>();
        public int PipeConnectionCount { get; set; }
        public int ExplicitDnCount { get; set; }
        public int InheritedDnCount { get; set; }
        public int DeviceOnPipeCount { get; set; }
        public int AmbiguousDeviceCount { get; set; }
        public Extents3d? SelectionExtents { get; set; }

        public int KnownDnPipeCount =>
            Pipes.Count(p => p != null && !string.IsNullOrWhiteSpace(p.Dn));

        public int UnknownDnPipeCount =>
            Pipes.Count(p => p != null && string.IsNullOrWhiteSpace(p.Dn));
    }

    public sealed class MepGraphDnInference
    {
        public bool Found { get; set; }
        public string Dn { get; set; } = "";
        public double Confidence { get; set; }
        public bool Ambiguous { get; set; }
        public int SupportCount { get; set; }
        public double BestDistance { get; set; } = double.MaxValue;
        public string Evidence { get; set; } = "";
    }

    /// <summary>
    /// STEP22A - Graph/Topology foundation.
    /// Không dùng ảnh. Đọc trực tiếp Entity CAD, nối topology, gắn text DN,
    /// suy DN theo network và tạo context cho thiết bị.
    /// Đây là graph deterministic trước khi train GNN thật ở STEP22B.
    /// </summary>
    public sealed class MepGraphEngine
    {
        public const double ConnectionTolerance = 150.0;
        private const double DnTextSearchDistance = 800.0;
        private const double DevicePipeSearchDistance = 1200.0;
        private const double SpatialCell = 1200.0;

        private static readonly Regex DnRegex =
            new Regex(
                @"(?i)(?:DN[ _\-]*|(?:^|[^A-Z0-9_])D\s*|Ø\s*|Φ\s*)(\d{2,3})(?!\d)",
                RegexOptions.Compiled);

        private static readonly Dictionary<int, int> OutsideDiameterToDn =
            new Dictionary<int, int>
            {
                { 21, 15 }, { 22, 15 },
                { 27, 20 }, { 28, 20 },
                { 34, 25 }, { 35, 25 },
                { 42, 32 }, { 43, 32 },
                { 49, 40 }, { 48, 40 },
                { 60, 50 }, { 61, 50 },
                { 76, 65 }, { 77, 65 },
                { 90, 80 }, { 89, 80 },
                { 114, 100 }, { 115, 100 },
                { 141, 125 }, { 140, 125 },
                { 168, 150 }, { 169, 150 },
                { 219, 200 }, { 220, 200 },
                { 273, 250 }, { 274, 250 },
                { 325, 300 }, { 324, 300 }
            };

        public MepGraphSnapshot BuildSnapshot(
            Document doc,
            ObjectId[] selectedIds,
            bool includeAiPipeOverlays = true)
        {
            MepGraphSnapshot snapshot =
                new MepGraphSnapshot();

            if (doc == null ||
                selectedIds == null ||
                selectedIds.Length == 0)
            {
                return snapshot;
            }

            snapshot.DrawingName =
                doc.Name ?? "";

            snapshot.BuiltUtc =
                DateTime.UtcNow;

            Database db =
                doc.Database;

            HashSet<ObjectId> ids =
                new HashSet<ObjectId>(
                    selectedIds.Where(
                        x =>
                            !x.IsNull &&
                            x.IsValid &&
                            !x.IsErased));

            using (Transaction tr =
                db.TransactionManager.StartTransaction())
            {
                Extents3d? selectionBounds =
                    BuildSelectionBounds(
                        tr,
                        ids);

                snapshot.SelectionExtents =
                    selectionBounds;

                if (includeAiPipeOverlays &&
                    selectionBounds.HasValue)
                {
                    AddAiPipeOverlaysInsideBounds(
                        tr,
                        db,
                        selectionBounds.Value,
                        ids);
                }

                List<MepGraphPipeNode> rawCurves =
                    new List<MepGraphPipeNode>();

                foreach (ObjectId id
                    in ids)
                {
                    if (id.IsNull ||
                        !id.IsValid ||
                        id.IsErased)
                    {
                        continue;
                    }

                    Entity ent = null;

                    try
                    {
                        ent =
                            tr.GetObject(
                                id,
                                OpenMode.ForRead,
                                false) as Entity;
                    }
                    catch
                    {
                        // Object đã bị erase giữa các bước AI => bỏ qua, không làm hỏng graph.
                        continue;
                    }

                    if (ent == null ||
                        ent.IsErased)
                    {
                        continue;
                    }

                    if (ent is DBText dbText)
                    {
                        string text =
                            dbText.TextString ?? "";

                        string dn =
                            ParseDn(
                                text);

                        if (!string.IsNullOrWhiteSpace(
                                dn))
                        {
                            snapshot.DnTexts.Add(
                                new MepGraphTextNode
                                {
                                    Id = id,
                                    Text = text,
                                    Dn = dn,
                                    Position = dbText.Position,
                                    Rotation = dbText.Rotation
                                });
                        }

                        continue;
                    }

                    if (ent is MText mText)
                    {
                        string text =
                            StripMText(
                                mText.Contents ?? "");

                        string dn =
                            ParseDn(
                                text);

                        if (!string.IsNullOrWhiteSpace(
                                dn))
                        {
                            snapshot.DnTexts.Add(
                                new MepGraphTextNode
                                {
                                    Id = id,
                                    Text = text,
                                    Dn = dn,
                                    Position = mText.Location,
                                    Rotation = mText.Rotation
                                });
                        }

                        continue;
                    }

                    if (ent is BlockReference br)
                    {
                        snapshot.Devices.Add(
                            BuildBlockDevice(
                                tr,
                                br));

                        continue;
                    }

                    if (ent is DBPoint point)
                    {
                        snapshot.Devices.Add(
                            new MepGraphDeviceNode
                            {
                                Id = id,
                                Handle = SafeHandle(id),
                                Kind = "POINT",
                                Name = "POINT",
                                Layer = ent.Layer ?? "",
                                Position = point.Position,
                                Extents = null
                            });

                        continue;
                    }

                    Curve curve =
                        ent as Curve;

                    if (curve == null)
                        continue;

                    MepGraphPipeNode curveNode =
                        BuildCurveNode(
                            ent,
                            curve);

                    if (curveNode == null ||
                        curveNode.Length < 5.0)
                    {
                        continue;
                    }

                    rawCurves.Add(
                        curveNode);
                }

                BuildPipeAdjacency(
                    rawCurves,
                    tr);

                AttachDnTextSeeds(
                    rawCurves,
                    snapshot.DnTexts,
                    tr);

                PropagateDnConservatively(
                    rawCurves,
                    snapshot);

                HashSet<int> keep =
                    FindPipeLikeClosure(
                        rawCurves);

                Dictionary<int, int> remap =
                    new Dictionary<int, int>();

                for (int i = 0;
                    i < rawCurves.Count;
                    i++)
                {
                    if (!keep.Contains(
                            i))
                    {
                        continue;
                    }

                    remap[i] =
                        snapshot.Pipes.Count;

                    snapshot.Pipes.Add(
                        rawCurves[i]);
                }

                foreach (KeyValuePair<int, int> pair
                    in remap)
                {
                    MepGraphPipeNode pipe =
                        snapshot.Pipes[pair.Value];

                    List<int> mapped =
                        new List<int>();

                    foreach (int oldNeighbor
                        in pipe.Neighbors)
                    {
                        if (remap.TryGetValue(
                                oldNeighbor,
                                out int newNeighbor))
                        {
                            mapped.Add(
                                newNeighbor);
                        }
                    }

                    pipe.Neighbors.Clear();

                    pipe.Neighbors.AddRange(
                        mapped.Distinct());
                }

                snapshot.PipeConnectionCount =
                    snapshot.Pipes.Sum(
                        p =>
                            p.Neighbors.Count) /
                    2;

                AttachDevicesToGraph(
                    snapshot,
                    tr);

                tr.Commit();
            }

            SaveSnapshotToLocalJson(
                snapshot);

            return snapshot;
        }

        public MepGraphDnInference InferDevicePipeSize(
            MepGraphSnapshot snapshot,
            Point3d position,
            Extents3d? deviceExtents)
        {
            MepGraphDnInference result =
                new MepGraphDnInference();

            if (snapshot == null ||
                snapshot.Pipes.Count == 0)
            {
                return result;
            }

            List<Point3d> refs =
                BuildReferencePoints(
                    position,
                    deviceExtents);

            List<(string Dn, double Score, double Distance, int Index)> candidates =
                new List<(string, double, double, int)>();

            for (int i = 0;
                i < snapshot.Pipes.Count;
                i++)
            {
                MepGraphPipeNode pipe =
                    snapshot.Pipes[i];

                if (pipe == null ||
                    string.IsNullOrWhiteSpace(
                        pipe.Dn))
                {
                    continue;
                }

                double distance =
                    refs.Min(
                        p =>
                            DistancePointToSegment2D(
                                p,
                                pipe.Start,
                                pipe.End));

                bool crosses =
                    deviceExtents.HasValue &&
                    ExtentsOverlapExpanded(
                        pipe.Extents,
                        deviceExtents.Value,
                        120.0);

                if (!crosses &&
                    distance >
                        DevicePipeSearchDistance)
                {
                    continue;
                }

                double score =
                    distance -
                    pipe.DnConfidence *
                    180.0;

                if (pipe.IsAiOverlay)
                {
                    score -=
                        140.0;
                }

                if (crosses)
                {
                    score -=
                        320.0;
                }

                int sameDnNeighbors =
                    pipe.Neighbors.Count(
                        n =>
                            n >= 0 &&
                            n <
                            snapshot.Pipes.Count &&
                            string.Equals(
                                snapshot.Pipes[n].Dn,
                                pipe.Dn,
                                StringComparison.OrdinalIgnoreCase));

                score -=
                    Math.Min(
                        160.0,
                        sameDnNeighbors *
                        45.0);

                score =
                    Math.Max(
                        0.0,
                        score);

                candidates.Add(
                    (pipe.Dn,
                     score,
                     distance,
                     i));
            }

            if (candidates.Count == 0)
                return result;

            var ranked =
                candidates
                    .GroupBy(
                        x =>
                            x.Dn,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(
                        g =>
                            new
                            {
                                Dn = g.Key,
                                Best = g.Min(
                                    x =>
                                        x.Score),
                                Distance = g.Min(
                                    x =>
                                        x.Distance),
                                Support = g.Count()
                            })
                    .OrderBy(
                        x =>
                            x.Best)
                    .ThenByDescending(
                        x =>
                            x.Support)
                    .ToList();

            var best =
                ranked[0];

            result.Found =
                best.Best <=
                900.0;

            if (!result.Found)
                return result;

            result.Dn =
                best.Dn;

            result.SupportCount =
                best.Support;

            result.BestDistance =
                best.Distance;

            result.Confidence =
                Math.Min(
                    0.98,
                    0.70 +
                    Math.Min(
                        0.16,
                        Math.Max(
                            0,
                            best.Support -
                            1) *
                        0.05) +
                    Math.Max(
                        0.0,
                        0.10 -
                        best.Best /
                        9000.0));

            if (ranked.Count > 1 &&
                ranked[1].Best -
                best.Best <
                90.0)
            {
                result.Ambiguous =
                    true;

                result.Confidence =
                    Math.Max(
                        0.50,
                        result.Confidence -
                        0.18);
            }

            result.Evidence =
                "GRAPH: " +
                best.Support.ToString(
                    CultureInfo.InvariantCulture) +
                " support, d=" +
                best.Distance.ToString(
                    "0",
                    CultureInfo.InvariantCulture) +
                "mm";

            return result;
        }

        private static MepGraphPipeNode BuildCurveNode(
            Entity ent,
            Curve curve)
        {
            try
            {
                Extents3d ex =
                    ent.GeometricExtents;

                string layer =
                    ent.Layer ?? "";

                string dn =
                    ParseDn(
                        layer);

                bool ai =
                    layer.StartsWith(
                        "TDL_AI_PIPE_DN",
                        StringComparison.OrdinalIgnoreCase);

                bool pipeLayer =
                    ai ||
                    LooksLikePipeLayer(
                        layer);

                return
                    new MepGraphPipeNode
                    {
                        Id = ent.ObjectId,
                        Handle = SafeHandle(ent.ObjectId),
                        Layer = layer,
                        Start = curve.StartPoint,
                        End = curve.EndPoint,
                        Center =
                            new Point3d(
                                (ex.MinPoint.X +
                                 ex.MaxPoint.X) *
                                0.5,
                                (ex.MinPoint.Y +
                                 ex.MaxPoint.Y) *
                                0.5,
                                0.0),
                        Extents = ex,
                        Length =
                            GetCurveLength(
                                curve),
                        Dn = dn,
                        DnConfidence =
                            string.IsNullOrWhiteSpace(
                                dn)
                                ? 0.0
                                : (ai
                                    ? 0.995
                                    : 0.90),
                        DnSource =
                            string.IsNullOrWhiteSpace(
                                dn)
                                ? ""
                                : (ai
                                    ? "AI_LAYER"
                                    : "LAYER"),
                        IsAiOverlay = ai,
                        LayerLooksLikePipe =
                            pipeLayer
                    };
            }
            catch
            {
                return null;
            }
        }

        private static MepGraphDeviceNode BuildBlockDevice(
            Transaction tr,
            BlockReference br)
        {
            string name =
                "BLOCK";

            try
            {
                ObjectId definitionId =
                    br.IsDynamicBlock
                        ? br.DynamicBlockTableRecord
                        : br.BlockTableRecord;

                BlockTableRecord def =
                    tr.GetObject(
                        definitionId,
                        OpenMode.ForRead,
                        false) as BlockTableRecord;

                if (def != null &&
                    !string.IsNullOrWhiteSpace(
                        def.Name))
                {
                    name =
                        def.Name;
                }
            }
            catch
            {
            }

            Extents3d? ex =
                null;

            try
            {
                ex =
                    br.GeometricExtents;
            }
            catch
            {
            }

            Point3d pos =
                br.Position;

            if (ex.HasValue)
            {
                Extents3d e =
                    ex.Value;

                pos =
                    new Point3d(
                        (e.MinPoint.X +
                         e.MaxPoint.X) *
                        0.5,
                        (e.MinPoint.Y +
                         e.MaxPoint.Y) *
                        0.5,
                        0.0);
            }

            return
                new MepGraphDeviceNode
                {
                    Id = br.ObjectId,
                    Handle = SafeHandle(br.ObjectId),
                    Kind = "BLOCK",
                    Name = name,
                    Layer = br.Layer ?? "",
                    Position = pos,
                    Extents = ex
                };
        }

        private static void AttachDnTextSeeds(
            List<MepGraphPipeNode> pipes,
            List<MepGraphTextNode> texts,
            Transaction tr)
        {
            if (pipes == null ||
                texts == null ||
                pipes.Count == 0 ||
                texts.Count == 0)
            {
                return;
            }

            foreach (MepGraphTextNode text
                in texts)
            {
                if (text == null ||
                    string.IsNullOrWhiteSpace(
                        text.Dn))
                {
                    continue;
                }

                int best =
                    -1;

                double bestScore =
                    double.MaxValue;

                for (int i = 0;
                    i < pipes.Count;
                    i++)
                {
                    MepGraphPipeNode pipe =
                        pipes[i];

                    Curve curve =
                        tr.GetObject(
                            pipe.Id,
                            OpenMode.ForRead,
                            false) as Curve;

                    if (curve == null ||
                        curve.IsErased)
                    {
                        continue;
                    }

                    double distance =
                        DistancePointToCurve(
                            curve,
                            text.Position);

                    if (distance >
                        DnTextSearchDistance)
                    {
                        continue;
                    }

                    double anglePenalty =
                        0.0;

                    double curveAngle =
                        GetCurvePlanAngle(
                            curve);

                    if (!double.IsNaN(
                            curveAngle))
                    {
                        double diff =
                            ParallelAngleDifference(
                                curveAngle,
                                text.Rotation);

                        if (diff >
                            Math.PI / 6.0)
                        {
                            anglePenalty =
                                280.0;
                        }
                        else
                        {
                            anglePenalty =
                                diff *
                                180.0;
                        }
                    }

                    double score =
                        distance +
                        anglePenalty;

                    if (score <
                        bestScore)
                    {
                        bestScore =
                            score;

                        best =
                            i;
                    }
                }

                if (best < 0 ||
                    bestScore > 900.0)
                {
                    continue;
                }

                MepGraphPipeNode target =
                    pipes[best];

                if (target.IsAiOverlay &&
                    !string.IsNullOrWhiteSpace(
                        target.Dn) &&
                    !string.Equals(
                        target.Dn,
                        text.Dn,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                target.Dn =
                    text.Dn;

                target.DnConfidence =
                    0.98;

                target.DnSource =
                    "TEXT";

                target.LayerLooksLikePipe =
                    true;
            }
        }

        private static void PropagateDnConservatively(
            List<MepGraphPipeNode> pipes,
            MepGraphSnapshot snapshot)
        {
            snapshot.ExplicitDnCount =
                pipes.Count(
                    p =>
                        !string.IsNullOrWhiteSpace(
                            p.Dn) &&
                        (p.DnSource == "TEXT" ||
                         p.DnSource == "AI_LAYER" ||
                         p.DnSource == "LAYER"));

            int inherited =
                0;

            for (int pass = 0;
                pass < 6;
                pass++)
            {
                List<(int Index, string Dn, double Confidence)> pending =
                    new List<(int, string, double)>();

                for (int i = 0;
                    i < pipes.Count;
                    i++)
                {
                    MepGraphPipeNode pipe =
                        pipes[i];

                    if (!string.IsNullOrWhiteSpace(
                            pipe.Dn))
                    {
                        continue;
                    }

                    List<MepGraphPipeNode> known =
                        pipe.Neighbors
                            .Where(
                                n =>
                                    n >= 0 &&
                                    n < pipes.Count)
                            .Select(
                                n =>
                                    pipes[n])
                            .Where(
                                n =>
                                    n != null &&
                                    !string.IsNullOrWhiteSpace(
                                        n.Dn))
                            .ToList();

                    if (known.Count == 0)
                        continue;

                    List<string> sizes =
                        known
                            .Select(
                                n =>
                                    n.Dn)
                            .Distinct(
                                StringComparer.OrdinalIgnoreCase)
                            .ToList();

                    if (sizes.Count != 1)
                        continue;

                    bool sameLayerSupport =
                        known.Any(
                            n =>
                                string.Equals(
                                    n.Layer,
                                    pipe.Layer,
                                    StringComparison.OrdinalIgnoreCase));

                    if (!pipe.LayerLooksLikePipe &&
                        !sameLayerSupport)
                    {
                        continue;
                    }

                    double conf =
                        Math.Max(
                            0.72,
                            known.Max(
                                n =>
                                    n.DnConfidence) -
                            0.10);

                    pending.Add(
                        (i,
                         sizes[0],
                         conf));
                }

                if (pending.Count == 0)
                    break;

                foreach (var item
                    in pending)
                {
                    MepGraphPipeNode pipe =
                        pipes[item.Index];

                    if (!string.IsNullOrWhiteSpace(
                            pipe.Dn))
                    {
                        continue;
                    }

                    pipe.Dn =
                        item.Dn;

                    pipe.DnConfidence =
                        item.Confidence;

                    pipe.DnSource =
                        "GRAPH_NEIGHBOR";

                    pipe.LayerLooksLikePipe =
                        true;

                    inherited++;
                }
            }

            snapshot.InheritedDnCount =
                inherited;
        }

        private static HashSet<int> FindPipeLikeClosure(
            List<MepGraphPipeNode> pipes)
        {
            HashSet<int> keep =
                new HashSet<int>();

            Queue<int> queue =
                new Queue<int>();

            for (int i = 0;
                i < pipes.Count;
                i++)
            {
                MepGraphPipeNode p =
                    pipes[i];

                if (p.LayerLooksLikePipe ||
                    !string.IsNullOrWhiteSpace(
                        p.Dn))
                {
                    keep.Add(
                        i);

                    queue.Enqueue(
                        i);
                }
            }

            int seedCount =
                queue.Count;

            for (int q = 0;
                q < seedCount;
                q++)
            {
                int i =
                    queue.Dequeue();

                MepGraphPipeNode source =
                    pipes[i];

                foreach (int n
                    in source.Neighbors)
                {
                    if (n < 0 ||
                        n >= pipes.Count ||
                        keep.Contains(
                            n))
                    {
                        continue;
                    }

                    MepGraphPipeNode target =
                        pipes[n];

                    if (string.Equals(
                            source.Layer,
                            target.Layer,
                            StringComparison.OrdinalIgnoreCase) &&
                        target.Length <=
                            15000.0)
                    {
                        keep.Add(
                            n);
                    }
                }
            }

            return keep;
        }

        private void AttachDevicesToGraph(
            MepGraphSnapshot snapshot,
            Transaction tr)
        {
            foreach (MepGraphDeviceNode device
                in snapshot.Devices)
            {
                MepGraphDnInference inference =
                    InferDevicePipeSizeWithTransaction(
                        snapshot,
                        tr,
                        device.Position,
                        device.Extents);

                if (!inference.Found)
                    continue;

                device.InferredDn =
                    inference.Dn;

                device.DnConfidence =
                    inference.Confidence;

                snapshot.DeviceOnPipeCount++;

                if (inference.Ambiguous)
                {
                    snapshot.AmbiguousDeviceCount++;
                }
            }
        }

        private MepGraphDnInference InferDevicePipeSizeWithTransaction(
            MepGraphSnapshot snapshot,
            Transaction tr,
            Point3d position,
            Extents3d? deviceExtents)
        {
            MepGraphDnInference result =
                new MepGraphDnInference();

            if (snapshot == null ||
                tr == null)
            {
                return result;
            }

            List<Point3d> refs =
                BuildReferencePoints(
                    position,
                    deviceExtents);

            List<(string Dn, double Score, double Distance, int Index)> candidates =
                new List<(string, double, double, int)>();

            for (int i = 0;
                i < snapshot.Pipes.Count;
                i++)
            {
                MepGraphPipeNode pipe =
                    snapshot.Pipes[i];

                if (string.IsNullOrWhiteSpace(
                        pipe.Dn))
                {
                    continue;
                }

                Curve curve =
                    tr.GetObject(
                        pipe.Id,
                        OpenMode.ForRead,
                        false) as Curve;

                if (curve == null ||
                    curve.IsErased)
                {
                    continue;
                }

                double distance =
                    refs.Min(
                        p =>
                            DistancePointToCurve(
                                curve,
                                p));

                bool crosses =
                    deviceExtents.HasValue &&
                    ExtentsOverlapExpanded(
                        pipe.Extents,
                        deviceExtents.Value,
                        120.0);

                if (!crosses &&
                    distance >
                        DevicePipeSearchDistance)
                {
                    continue;
                }

                double score =
                    distance -
                    pipe.DnConfidence *
                    180.0;

                if (pipe.IsAiOverlay)
                {
                    score -=
                        140.0;
                }

                if (crosses)
                {
                    score -=
                        320.0;
                }

                int sameDnNeighbors =
                    pipe.Neighbors.Count(
                        n =>
                            n >= 0 &&
                            n <
                            snapshot.Pipes.Count &&
                            string.Equals(
                                snapshot.Pipes[n].Dn,
                                pipe.Dn,
                                StringComparison.OrdinalIgnoreCase));

                score -=
                    Math.Min(
                        160.0,
                        sameDnNeighbors *
                        45.0);

                score =
                    Math.Max(
                        0.0,
                        score);

                candidates.Add(
                    (pipe.Dn,
                     score,
                     distance,
                     i));
            }

            if (candidates.Count == 0)
                return result;

            var ranked =
                candidates
                    .GroupBy(
                        x =>
                            x.Dn,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(
                        g =>
                            new
                            {
                                Dn = g.Key,
                                Best = g.Min(
                                    x =>
                                        x.Score),
                                Distance = g.Min(
                                    x =>
                                        x.Distance),
                                Support = g.Count()
                            })
                    .OrderBy(
                        x =>
                            x.Best)
                    .ThenByDescending(
                        x =>
                            x.Support)
                    .ToList();

            var best =
                ranked[0];

            result.Found =
                best.Best <=
                900.0;

            if (!result.Found)
                return result;

            result.Dn =
                best.Dn;

            result.SupportCount =
                best.Support;

            result.BestDistance =
                best.Distance;

            result.Confidence =
                Math.Min(
                    0.98,
                    0.70 +
                    Math.Min(
                        0.16,
                        Math.Max(
                            0,
                            best.Support -
                            1) *
                        0.05) +
                    Math.Max(
                        0.0,
                        0.10 -
                        best.Best /
                        9000.0));

            if (ranked.Count > 1 &&
                ranked[1].Best -
                best.Best <
                90.0)
            {
                result.Ambiguous =
                    true;

                result.Confidence =
                    Math.Max(
                        0.50,
                        result.Confidence -
                        0.18);
            }

            result.Evidence =
                "GRAPH: " +
                best.Support.ToString(
                    CultureInfo.InvariantCulture) +
                " support, d=" +
                best.Distance.ToString(
                    "0",
                    CultureInfo.InvariantCulture) +
                "mm";

            return result;
        }

        private static void BuildPipeAdjacency(
            List<MepGraphPipeNode> pipes,
            Transaction tr)
        {
            if (pipes == null ||
                pipes.Count <= 1)
            {
                return;
            }

            Dictionary<string, HashSet<int>> grid =
                new Dictionary<string, HashSet<int>>();

            for (int i = 0;
                i < pipes.Count;
                i++)
            {
                foreach (string key
                    in GridKeysForExtents(
                        pipes[i].Extents,
                        ConnectionTolerance))
                {
                    if (!grid.TryGetValue(
                            key,
                            out HashSet<int> set))
                    {
                        set =
                            new HashSet<int>();

                        grid[key] =
                            set;
                    }

                    set.Add(
                        i);
                }
            }

            HashSet<long> checkedPairs =
                new HashSet<long>();

            for (int i = 0;
                i < pipes.Count;
                i++)
            {
                HashSet<int> candidates =
                    new HashSet<int>();

                foreach (string key
                    in GridKeysForExtents(
                        pipes[i].Extents,
                        ConnectionTolerance))
                {
                    if (grid.TryGetValue(
                            key,
                            out HashSet<int> set))
                    {
                        candidates.UnionWith(
                            set);
                    }
                }

                foreach (int j
                    in candidates)
                {
                    if (j <= i)
                        continue;

                    long pairKey =
                        ((long)i <<
                         32) |
                        (uint)j;

                    if (!checkedPairs.Add(
                            pairKey))
                    {
                        continue;
                    }

                    if (!ArePipesConnected(
                            pipes[i],
                            pipes[j],
                            tr))
                    {
                        continue;
                    }

                    pipes[i].Neighbors.Add(
                        j);

                    pipes[j].Neighbors.Add(
                        i);
                }
            }
        }

        private static bool ArePipesConnected(
            MepGraphPipeNode a,
            MepGraphPipeNode b,
            Transaction tr)
        {
            if (!ExtentsOverlapExpanded(
                    a.Extents,
                    b.Extents,
                    ConnectionTolerance))
            {
                return false;
            }

            if (PlanDistance(
                    a.Start,
                    b.Start) <=
                    ConnectionTolerance ||
                PlanDistance(
                    a.Start,
                    b.End) <=
                    ConnectionTolerance ||
                PlanDistance(
                    a.End,
                    b.Start) <=
                    ConnectionTolerance ||
                PlanDistance(
                    a.End,
                    b.End) <=
                    ConnectionTolerance)
            {
                return true;
            }

            try
            {
                Curve ca =
                    tr.GetObject(
                        a.Id,
                        OpenMode.ForRead,
                        false) as Curve;

                Curve cb =
                    tr.GetObject(
                        b.Id,
                        OpenMode.ForRead,
                        false) as Curve;

                if (ca == null ||
                    cb == null)
                {
                    return false;
                }

                if (DistancePointToCurve(
                        cb,
                        a.Start) <=
                        ConnectionTolerance ||
                    DistancePointToCurve(
                        cb,
                        a.End) <=
                        ConnectionTolerance ||
                    DistancePointToCurve(
                        ca,
                        b.Start) <=
                        ConnectionTolerance ||
                    DistancePointToCurve(
                        ca,
                        b.End) <=
                        ConnectionTolerance)
                {
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static Extents3d? BuildSelectionBounds(
            Transaction tr,
            IEnumerable<ObjectId> ids)
        {
            bool has =
                false;

            Extents3d result =
                default(Extents3d);

            foreach (ObjectId id
                in ids)
            {
                try
                {
                    Entity ent =
                        tr.GetObject(
                            id,
                            OpenMode.ForRead,
                            false) as Entity;

                    if (ent == null ||
                        ent.IsErased)
                    {
                        continue;
                    }

                    Extents3d ex =
                        ent.GeometricExtents;

                    if (!has)
                    {
                        result =
                            ex;

                        has =
                            true;
                    }
                    else
                    {
                        result.AddExtents(
                            ex);
                    }
                }
                catch
                {
                }
            }

            return
                has
                    ? result
                    : (Extents3d?)null;
        }

        private static void AddAiPipeOverlaysInsideBounds(
            Transaction tr,
            Database db,
            Extents3d bounds,
            HashSet<ObjectId> ids)
        {
            try
            {
                BlockTableRecord space =
                    tr.GetObject(
                        db.CurrentSpaceId,
                        OpenMode.ForRead) as BlockTableRecord;

                if (space == null)
                    return;

                foreach (ObjectId id
                    in space)
                {
                    if (ids.Contains(
                            id))
                    {
                        continue;
                    }

                    Entity ent =
                        tr.GetObject(
                            id,
                            OpenMode.ForRead,
                            false) as Entity;

                    Curve curve =
                        ent as Curve;

                    if (curve == null ||
                        ent.IsErased)
                    {
                        continue;
                    }

                    if (!(ent.Layer ?? "")
                        .StartsWith(
                            "TDL_AI_PIPE_DN",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    Extents3d ex;

                    try
                    {
                        ex =
                            ent.GeometricExtents;
                    }
                    catch
                    {
                        continue;
                    }

                    if (ExtentsOverlapExpanded(
                            ex,
                            bounds,
                            250.0))
                    {
                        ids.Add(
                            id);
                    }
                }
            }
            catch
            {
            }
        }

        private static IEnumerable<string> GridKeysForExtents(
            Extents3d ex,
            double margin)
        {
            int minX =
                (int)Math.Floor(
                    (ex.MinPoint.X -
                     margin) /
                    SpatialCell);

            int maxX =
                (int)Math.Floor(
                    (ex.MaxPoint.X +
                     margin) /
                    SpatialCell);

            int minY =
                (int)Math.Floor(
                    (ex.MinPoint.Y -
                     margin) /
                    SpatialCell);

            int maxY =
                (int)Math.Floor(
                    (ex.MaxPoint.Y +
                     margin) /
                    SpatialCell);

            int count =
                (maxX -
                 minX +
                 1) *
                (maxY -
                 minY +
                 1);

            if (count > 160)
            {
                yield return
                    minX.ToString(
                        CultureInfo.InvariantCulture) +
                    ":" +
                    minY.ToString(
                        CultureInfo.InvariantCulture);

                yield return
                    maxX.ToString(
                        CultureInfo.InvariantCulture) +
                    ":" +
                    maxY.ToString(
                        CultureInfo.InvariantCulture);

                yield return
                    ((minX +
                      maxX) /
                     2).ToString(
                        CultureInfo.InvariantCulture) +
                    ":" +
                    ((minY +
                      maxY) /
                     2).ToString(
                        CultureInfo.InvariantCulture);

                yield break;
            }

            for (int x = minX;
                x <= maxX;
                x++)
            {
                for (int y = minY;
                    y <= maxY;
                    y++)
                {
                    yield return
                        x.ToString(
                            CultureInfo.InvariantCulture) +
                        ":" +
                        y.ToString(
                            CultureInfo.InvariantCulture);
                }
            }
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

        private static double DistancePointToCurve(
            Curve curve,
            Point3d p)
        {
            try
            {
                Point3d cp =
                    curve.GetClosestPointTo(
                        p,
                        false);

                return
                    PlanDistance(
                        cp,
                        p);
            }
            catch
            {
                try
                {
                    return
                        Math.Min(
                            PlanDistance(
                                curve.StartPoint,
                                p),
                            PlanDistance(
                                curve.EndPoint,
                                p));
                }
                catch
                {
                    return
                        double.MaxValue;
                }
            }
        }

        private static double GetCurvePlanAngle(
            Curve curve)
        {
            try
            {
                Vector2d v =
                    new Vector2d(
                        curve.EndPoint.X -
                        curve.StartPoint.X,
                        curve.EndPoint.Y -
                        curve.StartPoint.Y);

                if (v.Length < 1e-6)
                    return double.NaN;

                return
                    Math.Atan2(
                        v.Y,
                        v.X);
            }
            catch
            {
                return
                    double.NaN;
            }
        }

        private static double ParallelAngleDifference(
            double a,
            double b)
        {
            double diff =
                Math.Abs(
                    NormalizeAngle(a) -
                    NormalizeAngle(b));

            while (diff >
                Math.PI)
            {
                diff -=
                    Math.PI;
            }

            diff =
                Math.Abs(
                    diff);

            return
                Math.Min(
                    diff,
                    Math.PI -
                    diff);
        }

        private static double NormalizeAngle(
            double a)
        {
            while (a < 0)
                a += Math.PI * 2.0;

            while (a >=
                Math.PI *
                2.0)
            {
                a -=
                    Math.PI *
                    2.0;
            }

            return a;
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

        private static double GetCurveLength(
            Curve curve)
        {
            try
            {
                if (curve is Line l)
                    return l.Length;

                if (curve is Polyline p)
                    return p.Length;

                if (curve is Arc a)
                    return a.Length;

                return
                    curve.GetDistanceAtParameter(
                        curve.EndParam);
            }
            catch
            {
                try
                {
                    return
                        curve.StartPoint.DistanceTo(
                            curve.EndPoint);
                }
                catch
                {
                    return
                        0.0;
                }
            }
        }

        public static string ParseDn(
            string value)
        {
            if (string.IsNullOrWhiteSpace(
                    value))
            {
                return "";
            }

            string normalized =
                value
                    .Replace(
                        "\\P",
                        " ")
                    .Replace(
                        "\\p",
                        " ")
                    .Replace(
                        "{",
                        " ")
                    .Replace(
                        "}",
                        " ");

            Match match =
                DnRegex.Match(
                    normalized);

            if (!match.Success ||
                !int.TryParse(
                    match.Groups[1].Value,
                    out int number))
            {
                return "";
            }

            string prefix =
                match.Value
                    .Trim()
                    .ToUpperInvariant();

            bool directDn =
                prefix.IndexOf(
                    "DN",
                    StringComparison.OrdinalIgnoreCase) >=
                0;

            if (directDn)
            {
                return
                    "DN" +
                    number.ToString(
                        CultureInfo.InvariantCulture);
            }

            if (OutsideDiameterToDn.TryGetValue(
                    number,
                    out int dn))
            {
                return
                    "DN" +
                    dn.ToString(
                        CultureInfo.InvariantCulture);
            }

            int[] nominal =
            {
                15, 20, 25, 32, 40, 50,
                65, 80, 100, 125, 150,
                200, 250, 300
            };

            if (nominal.Contains(
                    number))
            {
                return
                    "DN" +
                    number.ToString(
                        CultureInfo.InvariantCulture);
            }

            return "";
        }

        private static bool LooksLikePipeLayer(
            string layer)
        {
            string s =
                (layer ?? "")
                    .ToUpperInvariant();

            string[] tokens =
            {
                "PIPE",
                "PCCC",
                "FIRE",
                "SPRINK",
                "HYDRANT",
                "WATER",
                "CTN",
                "DRAIN",
                "CHW",
                "CWS",
                "HWS",
                "GAS",
                "ONG",
                "ỐNG",
                "PLUMB",
                "MEP"
            };

            return
                tokens.Any(
                    t =>
                        s.Contains(
                            t));
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

        private static string StripMText(
            string value)
        {
            if (string.IsNullOrWhiteSpace(
                    value))
            {
                return "";
            }

            string s =
                value
                    .Replace(
                        "\\P",
                        " ")
                    .Replace(
                        "\\p",
                        " ");

            s =
                Regex.Replace(
                    s,
                    @"\\[A-Za-z][^;]*;",
                    "");

            s =
                s
                    .Replace(
                        "{",
                        "")
                    .Replace(
                        "}",
                        "");

            return s;
        }

        private static string SafeHandle(
            ObjectId id)
        {
            try
            {
                return
                    id.Handle.ToString();
            }
            catch
            {
                return "";
            }
        }

        public string SaveSnapshotToLocalJson(
            MepGraphSnapshot snapshot)
        {
            if (snapshot == null)
                return "";

            try
            {
                string appData =
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.ApplicationData);

                if (string.IsNullOrWhiteSpace(
                        appData))
                {
                    appData =
                        Path.GetTempPath();
                }

                string folder =
                    Path.Combine(
                        appData,
                        "TDL_MEP",
                        "Graph");

                Directory.CreateDirectory(
                    folder);

                string path =
                    Path.Combine(
                        folder,
                        "last_graph.json");

                object dto =
                    new
                    {
                        version = 1,
                        drawing =
                            snapshot.DrawingName,
                        built_utc =
                            snapshot.BuiltUtc.ToString(
                                "O",
                                CultureInfo.InvariantCulture),
                        summary =
                            new
                            {
                                pipes =
                                    snapshot.Pipes.Count,
                                known_dn =
                                    snapshot.KnownDnPipeCount,
                                unknown_dn =
                                    snapshot.UnknownDnPipeCount,
                                connections =
                                    snapshot.PipeConnectionCount,
                                devices =
                                    snapshot.Devices.Count,
                                device_on_pipe =
                                    snapshot.DeviceOnPipeCount,
                                ambiguous_devices =
                                    snapshot.AmbiguousDeviceCount
                            },
                        pipes =
                            snapshot.Pipes.Select(
                                (p, index) =>
                                    new
                                    {
                                        id = index,
                                        handle =
                                            p.Handle,
                                        layer =
                                            p.Layer,
                                        start =
                                            new[]
                                            {
                                                p.Start.X,
                                                p.Start.Y
                                            },
                                        end =
                                            new[]
                                            {
                                                p.End.X,
                                                p.End.Y
                                            },
                                        length =
                                            p.Length,
                                        dn =
                                            p.Dn,
                                        dn_confidence =
                                            p.DnConfidence,
                                        dn_source =
                                            p.DnSource,
                                        ai_overlay =
                                            p.IsAiOverlay,
                                        neighbors =
                                            p.Neighbors
                                    }),
                        devices =
                            snapshot.Devices.Select(
                                d =>
                                    new
                                    {
                                        handle =
                                            d.Handle,
                                        kind =
                                            d.Kind,
                                        name =
                                            d.Name,
                                        layer =
                                            d.Layer,
                                        position =
                                            new[]
                                            {
                                                d.Position.X,
                                                d.Position.Y
                                            },
                                        inferred_dn =
                                            d.InferredDn,
                                        dn_confidence =
                                            d.DnConfidence
                                    })
                    };

                string json =
                    JsonSerializer.Serialize(
                        dto,
                        new JsonSerializerOptions
                        {
                            WriteIndented =
                                true
                        });

                File.WriteAllText(
                    path,
                    json,
                    Encoding.UTF8);

                // STEP22D FIX:
                // last_graph.json chỉ là snapshot gần nhất. GNN training cần History
                // tích lũy ổn định qua nhiều lần quét. Dùng canonical hash của
                // Graph Cloud để cùng topology/DN không sinh file mới chỉ vì
                // built_utc / tên DWG / Handle thay đổi.
                try
                {
                    string historyFolder =
                        Path.Combine(
                            folder,
                            "History");

                    Directory.CreateDirectory(
                        historyFolder);

                    string canonicalHash =
                        new AiGraphCloudClient()
                            .GetStructureHashForGraphFile(
                                path);

                    if (string.IsNullOrWhiteSpace(
                            canonicalHash))
                    {
                        using (System.Security.Cryptography.SHA256 sha =
                            System.Security.Cryptography.SHA256.Create())
                        {
                            byte[] hashBytes =
                                sha.ComputeHash(
                                    Encoding.UTF8.GetBytes(
                                        json));

                            canonicalHash =
                                BitConverter.ToString(
                                        hashBytes)
                                    .Replace(
                                        "-",
                                        "")
                                    .ToLowerInvariant();
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(
                            canonicalHash))
                    {
                        string historyPath =
                            Path.Combine(
                                historyFolder,
                                canonicalHash +
                                ".json");

                        // Cùng canonical graph thì cập nhật bản mới nhất để
                        // confidence/source sửa tốt hơn vẫn đi vào training.
                        File.WriteAllText(
                            historyPath,
                            json,
                            Encoding.UTF8);
                    }
                }
                catch
                {
                    // History là tầng học bổ sung. Không bao giờ làm fail Graph chính.
                }

                return path;
            }
            catch
            {
                return "";
            }
        }
    }
}