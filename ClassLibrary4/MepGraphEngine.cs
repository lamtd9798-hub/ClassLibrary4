#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace ClassLibrary4
{
    // ================================================================
    // STEP30B.1 - GRAPH SCHEMA V2 + TRUE 3D TOPOLOGY
    //
    // Backward compatible với các API cũ:
    //   MepGraphEngine.BuildSnapshot(...)
    //   MepGraphEngine.InferDevicePipeSize(...)
    //   MepGraphEngine.ParseDn(...)
    //   MepGraphEngine.SaveSnapshotToLocalJson(...)
    //
    // Mục tiêu:
    // - Giữ XYZ thật của pipe.
    // - Không nối nhầm 2 pipe chỉ trùng XY nhưng khác cao độ.
    // - Nhận vertical curve / riser block thành pipe node.
    // - Sinh edge metadata cho TEE / ELBOW / REDUCER / RISER.
    // - Lưu Graph JSON schema v2 để trainer GNN V3 dùng đúng dữ liệu 3D.
    // ================================================================

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

        // STEP30B.1
        public string NodeKind { get; set; } = "CURVE";
        public bool IsSynthetic { get; set; }
        public bool IsVertical { get; set; }
        public bool IsRiser { get; set; }
        public double RiserHeight { get; set; }
        public double VerticalRatio { get; set; }
        public string JunctionType { get; set; } = "";

        public List<int> Neighbors { get; } = new List<int>();
    }

    public sealed class MepGraphEdge
    {
        public int From { get; set; } = -1;
        public int To { get; set; } = -1;

        public string Type { get; set; } = "STRAIGHT";
        public double AngleDegrees { get; set; }
        public double LengthRatio { get; set; }
        public double ElevationDelta { get; set; }
        public double EndpointDistance { get; set; }

        public bool SameDn { get; set; }
        public bool DifferentDn { get; set; }
        public bool IsRiser { get; set; }
        public bool IsReducer { get; set; }
        public bool IsTee { get; set; }
        public bool IsElbow { get; set; }
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
        public int SchemaVersion { get; set; } = 2;
        public string SchemaName { get; set; } = "TDL_MEP_GRAPH_V2_3D";

        public string DrawingName { get; set; } = "";
        public DateTime BuiltUtc { get; set; } = DateTime.UtcNow;

        public List<MepGraphPipeNode> Pipes { get; } =
            new List<MepGraphPipeNode>();

        public List<MepGraphEdge> Edges { get; } =
            new List<MepGraphEdge>();

        public List<MepGraphDeviceNode> Devices { get; } =
            new List<MepGraphDeviceNode>();

        public List<MepGraphTextNode> DnTexts { get; } =
            new List<MepGraphTextNode>();

        public int PipeConnectionCount { get; set; }
        public int ExplicitDnCount { get; set; }
        public int InheritedDnCount { get; set; }
        public int DeviceOnPipeCount { get; set; }
        public int AmbiguousDeviceCount { get; set; }
        public Extents3d? SelectionExtents { get; set; }

        public int KnownDnPipeCount =>
            Pipes.Count(p =>
                p != null &&
                !string.IsNullOrWhiteSpace(p.Dn));

        public int UnknownDnPipeCount =>
            Pipes.Count(p =>
                p != null &&
                string.IsNullOrWhiteSpace(p.Dn));

        public int VerticalPipeCount =>
            Pipes.Count(p => p != null && p.IsVertical);

        public int RiserCount =>
            Pipes.Count(p => p != null && p.IsRiser);

        public int TeeEdgeCount =>
            Edges.Count(e => e != null && e.IsTee);

        public int ElbowEdgeCount =>
            Edges.Count(e => e != null && e.IsElbow);

        public int ReducerEdgeCount =>
            Edges.Count(e => e != null && e.IsReducer);

        public int RiserEdgeCount =>
            Edges.Count(e => e != null && e.IsRiser);
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

    public sealed class MepGraphEngine
    {
        // XY tolerance dùng cho bản vẽ MEP plan.
        public const double ConnectionTolerance = 150.0;

        // STEP30B.1: nếu hai đối tượng chỉ gần nhau trong XY nhưng chênh Z lớn
        // thì KHÔNG được coi là connected.
        public const double ElevationConnectionTolerance = 120.0;

        private const double DnTextSearchDistance = 800.0;
        private const double DevicePipeSearchDistance = 1200.0;
        private const double SpatialCell = 1200.0;

        private const double VerticalMinHeight = 100.0;
        private const double VerticalRatioThreshold = 0.70;

        private static readonly Regex DnRegex =
            new Regex(
                @"(?i)(?:DN[ _\-]*|(?:^|[^A-Z0-9_])D\s*|Ø\s*|Φ\s*)(\d{2,3})(?!\d)",
                RegexOptions.Compiled);

        // Metadata của nút "Trục đứng" có thể khác nhau giữa các version cũ.
        // Chỉ parse HEIGHT/H/CAO/RISE/RISER rõ ràng; KHÔNG lấy số tùy tiện.
        private static readonly Regex RiserHeightRegex =
            new Regex(
                @"(?ix)
                  (?:RISER[_\s-]*(?:HEIGHT|H)|
                     HEIGHT|
                     RISE|
                     CAO[_\s-]*(?:DO|ĐỘ)?|
                     CHIEU[_\s-]*CAO|
                     CHIỀU[_\s-]*CAO|
                     \bH)
                  \s*[:=]?\s*
                  (?<v>[+-]?\d+(?:[.,]\d+)?)
                  \s*(?<u>MM|M)?",
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
            MepGraphSnapshot snapshot = new MepGraphSnapshot();

            if (doc == null ||
                selectedIds == null ||
                selectedIds.Length == 0)
            {
                return snapshot;
            }

            snapshot.DrawingName = doc.Name ?? "";
            snapshot.BuiltUtc = DateTime.UtcNow;

            Database db = doc.Database;

            HashSet<ObjectId> ids =
                new HashSet<ObjectId>(
                    selectedIds.Where(x =>
                        !x.IsNull &&
                        x.IsValid &&
                        !x.IsErased));

            using (Transaction tr =
                db.TransactionManager.StartTransaction())
            {
                Extents3d? selectionBounds =
                    BuildSelectionBounds(tr, ids);

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

                List<MepGraphPipeNode> rawPipes =
                    new List<MepGraphPipeNode>();

                foreach (ObjectId id in ids)
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
                        continue;
                    }

                    if (ent == null || ent.IsErased)
                        continue;

                    if (ent is DBText dbText)
                    {
                        AddTextNode(
                            snapshot,
                            id,
                            dbText.TextString ?? "",
                            dbText.Position,
                            dbText.Rotation);

                        continue;
                    }

                    if (ent is MText mText)
                    {
                        AddTextNode(
                            snapshot,
                            id,
                            StripMText(mText.Contents ?? ""),
                            mText.Location,
                            mText.Rotation);

                        continue;
                    }

                    if (ent is BlockReference br)
                    {
                        // STEP30B.1:
                        // Block riser hợp lệ được biến thành synthetic vertical pipe.
                        // Block thường vẫn là Device như pipeline cũ.
                        MepGraphPipeNode riser =
                            TryBuildRiserBlockNode(tr, br);

                        if (riser != null)
                        {
                            rawPipes.Add(riser);
                        }
                        else
                        {
                            snapshot.Devices.Add(
                                BuildBlockDevice(tr, br));
                        }

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

                    Curve curve = ent as Curve;

                    if (curve == null)
                        continue;

                    MepGraphPipeNode node =
                        BuildCurveNode(ent, curve);

                    if (node == null ||
                        node.Length < 5.0)
                    {
                        continue;
                    }

                    rawPipes.Add(node);
                }

                // TRUE 3D adjacency trước khi propagate DN.
                BuildPipeAdjacency3D(
                    rawPipes,
                    tr);

                AttachDnTextSeeds(
                    rawPipes,
                    snapshot.DnTexts,
                    tr);

                PropagateDnConservatively(
                    rawPipes,
                    snapshot);

                HashSet<int> keep =
                    FindPipeLikeClosure(rawPipes);

                Dictionary<int, int> remap =
                    new Dictionary<int, int>();

                for (int i = 0; i < rawPipes.Count; i++)
                {
                    if (!keep.Contains(i))
                        continue;

                    remap[i] = snapshot.Pipes.Count;
                    snapshot.Pipes.Add(rawPipes[i]);
                }

                // Remap neighbor index sau khi lọc non-pipe curve.
                foreach (KeyValuePair<int, int> pair in remap)
                {
                    MepGraphPipeNode pipe =
                        snapshot.Pipes[pair.Value];

                    List<int> mapped =
                        new List<int>();

                    foreach (int oldNeighbor in pipe.Neighbors)
                    {
                        if (remap.TryGetValue(
                                oldNeighbor,
                                out int newNeighbor))
                        {
                            mapped.Add(newNeighbor);
                        }
                    }

                    pipe.Neighbors.Clear();
                    pipe.Neighbors.AddRange(
                        mapped
                            .Where(n => n != pair.Value)
                            .Distinct()
                            .OrderBy(n => n));
                }

                snapshot.PipeConnectionCount =
                    snapshot.Pipes.Sum(
                        p => p.Neighbors.Count) / 2;

                BuildEdgeMetadata(snapshot);
                ClassifyJunctions(snapshot);

                AttachDevicesToGraph(
                    snapshot,
                    tr);

                tr.Commit();
            }

            SaveSnapshotToLocalJson(snapshot);
            return snapshot;
        }

        private static void AddTextNode(
            MepGraphSnapshot snapshot,
            ObjectId id,
            string text,
            Point3d position,
            double rotation)
        {
            string dn = ParseDn(text);

            if (string.IsNullOrWhiteSpace(dn))
                return;

            snapshot.DnTexts.Add(
                new MepGraphTextNode
                {
                    Id = id,
                    Text = text ?? "",
                    Dn = dn,
                    Position = position,
                    Rotation = rotation
                });
        }

        // ============================================================
        // DEVICE -> PIPE DN inference
        // Giữ distance plan 2D vì nhiều bản MEP vẫn vẽ toàn bộ plan tại Z=0.
        // TRUE 3D được áp dụng cho GRAPH CONNECTIVITY, không ép user phải dựng
        // toàn bộ thiết bị ở elevation thật.
        // ============================================================

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
                    string.IsNullOrWhiteSpace(pipe.Dn))
                {
                    continue;
                }

                double distance =
                    refs.Min(p =>
                        DistancePointToSegment2D(
                            p,
                            pipe.Start,
                            pipe.End));

                bool crosses =
                    deviceExtents.HasValue &&
                    ExtentsOverlapExpanded2D(
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
                    pipe.DnConfidence * 180.0;

                if (pipe.IsAiOverlay)
                    score -= 140.0;

                if (pipe.IsRiser)
                    score -= 40.0;

                if (crosses)
                    score -= 320.0;

                int sameDnNeighbors =
                    pipe.Neighbors.Count(n =>
                        n >= 0 &&
                        n < snapshot.Pipes.Count &&
                        string.Equals(
                            snapshot.Pipes[n].Dn,
                            pipe.Dn,
                            StringComparison.OrdinalIgnoreCase));

                score -=
                    Math.Min(
                        160.0,
                        sameDnNeighbors * 45.0);

                score = Math.Max(0.0, score);

                candidates.Add(
                    (pipe.Dn,
                     score,
                     distance,
                     i));
            }

            return RankDnCandidates(candidates);
        }

        private MepGraphDnInference InferDevicePipeSizeWithTransaction(
            MepGraphSnapshot snapshot,
            Transaction tr,
            Point3d position,
            Extents3d? deviceExtents)
        {
            // STEP30B.1:
            // Không phụ thuộc DB Curve tại đây nữa vì riser synthetic dùng
            // BlockReference ObjectId chứ không phải Curve.
            return InferDevicePipeSize(
                snapshot,
                position,
                deviceExtents);
        }

        private static MepGraphDnInference RankDnCandidates(
            List<(string Dn, double Score, double Distance, int Index)> candidates)
        {
            MepGraphDnInference result =
                new MepGraphDnInference();

            if (candidates == null ||
                candidates.Count == 0)
            {
                return result;
            }

            var ranked =
                candidates
                    .GroupBy(
                        x => x.Dn,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(g => new
                    {
                        Dn = g.Key,
                        Best = g.Min(x => x.Score),
                        Distance = g.Min(x => x.Distance),
                        Support = g.Count()
                    })
                    .OrderBy(x => x.Best)
                    .ThenByDescending(x => x.Support)
                    .ToList();

            var best = ranked[0];

            result.Found =
                best.Best <= 900.0;

            if (!result.Found)
                return result;

            result.Dn = best.Dn;
            result.SupportCount = best.Support;
            result.BestDistance = best.Distance;

            result.Confidence =
                Math.Min(
                    0.98,
                    0.70 +
                    Math.Min(
                        0.16,
                        Math.Max(
                            0,
                            best.Support - 1) * 0.05) +
                    Math.Max(
                        0.0,
                        0.10 -
                        best.Best / 9000.0));

            if (ranked.Count > 1 &&
                ranked[1].Best -
                    best.Best < 90.0)
            {
                result.Ambiguous = true;

                result.Confidence =
                    Math.Max(
                        0.50,
                        result.Confidence - 0.18);
            }

            result.Evidence =
                "GRAPH V2: " +
                best.Support.ToString(
                    CultureInfo.InvariantCulture) +
                " support, d=" +
                best.Distance.ToString(
                    "0",
                    CultureInfo.InvariantCulture) +
                "mm";

            return result;
        }

        // ============================================================
        // BUILD PIPE NODES
        // ============================================================

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
                    ParseDn(layer);

                bool ai =
                    layer.StartsWith(
                        "TDL_AI_PIPE_DN",
                        StringComparison.OrdinalIgnoreCase);

                bool pipeLayer =
                    ai ||
                    LooksLikePipeLayer(layer);

                Point3d start =
                    curve.StartPoint;

                Point3d end =
                    curve.EndPoint;

                double dx =
                    end.X - start.X;

                double dy =
                    end.Y - start.Y;

                double dz =
                    end.Z - start.Z;

                double length3d =
                    Math.Sqrt(
                        dx * dx +
                        dy * dy +
                        dz * dz);

                double verticalRatio =
                    length3d <= 1e-6
                        ? 0.0
                        : Math.Abs(dz) / length3d;

                bool vertical =
                    Math.Abs(dz) >=
                        VerticalMinHeight &&
                    verticalRatio >=
                        VerticalRatioThreshold;

                return
                    new MepGraphPipeNode
                    {
                        Id = ent.ObjectId,
                        Handle = SafeHandle(ent.ObjectId),
                        Layer = layer,
                        Start = start,
                        End = end,
                        Center =
                            MidPoint3D(start, end),
                        Extents = ex,
                        Length =
                            GetCurveLength(curve),
                        Dn = dn,
                        DnConfidence =
                            string.IsNullOrWhiteSpace(dn)
                                ? 0.0
                                : (ai
                                    ? 0.995
                                    : 0.90),
                        DnSource =
                            string.IsNullOrWhiteSpace(dn)
                                ? ""
                                : (ai
                                    ? "AI_LAYER"
                                    : "LAYER"),
                        IsAiOverlay = ai,
                        LayerLooksLikePipe =
                            pipeLayer,
                        NodeKind = "CURVE",
                        IsSynthetic = false,
                        IsVertical = vertical,
                        IsRiser = vertical,
                        RiserHeight =
                            vertical
                                ? Math.Abs(dz)
                                : 0.0,
                        VerticalRatio =
                            verticalRatio,
                        JunctionType =
                            vertical
                                ? "RISER"
                                : ""
                    };
            }
            catch
            {
                return null;
            }
        }

        private static MepGraphPipeNode TryBuildRiserBlockNode(
            Transaction tr,
            BlockReference br)
        {
            if (tr == null || br == null)
                return null;

            string blockName =
                GetBlockName(tr, br);

            string layer =
                br.Layer ?? "";

            string metadata =
                BuildBlockMetadataText(
                    tr,
                    br,
                    blockName,
                    layer);

            if (!LooksLikeRiserMetadata(metadata))
                return null;

            if (!TryParseRiserHeight(
                    metadata,
                    out double height))
            {
                // Không biết chiều cao thật => không tạo fake 3D edge.
                return null;
            }

            int direction =
                ParseRiserDirection(metadata);

            Point3d start =
                br.Position;

            Point3d end =
                new Point3d(
                    start.X,
                    start.Y,
                    start.Z +
                    direction * height);

            string dn =
                ParseDn(metadata);

            Extents3d ex =
                BuildExtents3D(start, end);

            return
                new MepGraphPipeNode
                {
                    Id = br.ObjectId,
                    Handle = SafeHandle(br.ObjectId),
                    Layer = layer,
                    Start = start,
                    End = end,
                    Center = MidPoint3D(start, end),
                    Extents = ex,
                    Length = height,
                    Dn = dn,
                    DnConfidence =
                        string.IsNullOrWhiteSpace(dn)
                            ? 0.0
                            : 0.97,
                    DnSource =
                        string.IsNullOrWhiteSpace(dn)
                            ? ""
                            : "RISER_META",
                    IsAiOverlay = false,
                    LayerLooksLikePipe = true,
                    NodeKind = "RISER_BLOCK",
                    IsSynthetic = true,
                    IsVertical = true,
                    IsRiser = true,
                    RiserHeight = height,
                    VerticalRatio = 1.0,
                    JunctionType = "RISER"
                };
        }

        private static string GetBlockName(
            Transaction tr,
            BlockReference br)
        {
            string name = "BLOCK";

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
                    !string.IsNullOrWhiteSpace(def.Name))
                {
                    name = def.Name;
                }
            }
            catch
            {
            }

            return name;
        }

        private static string BuildBlockMetadataText(
            Transaction tr,
            BlockReference br,
            string blockName,
            string layer)
        {
            StringBuilder sb =
                new StringBuilder();

            sb.Append(blockName ?? "");
            sb.Append(' ');
            sb.Append(layer ?? "");

            // Attributes.
            try
            {
                foreach (ObjectId id
                    in br.AttributeCollection)
                {
                    AttributeReference ar =
                        tr.GetObject(
                            id,
                            OpenMode.ForRead,
                            false) as AttributeReference;

                    if (ar == null)
                        continue;

                    sb.Append(' ');
                    sb.Append(ar.Tag ?? "");
                    sb.Append('=');
                    sb.Append(ar.TextString ?? "");
                }
            }
            catch
            {
            }

            // XData. Chỉ lấy text/numeric để đọc metadata của các version cũ.
            try
            {
                using (ResultBuffer rb = br.XData)
                {
                    if (rb != null)
                    {
                        foreach (TypedValue value in rb)
                        {
                            if (value.Value == null)
                                continue;

                            sb.Append(' ');
                            sb.Append(
                                Convert.ToString(
                                    value.Value,
                                    CultureInfo.InvariantCulture));
                        }
                    }
                }
            }
            catch
            {
            }

            return sb.ToString();
        }

        private static bool LooksLikeRiserMetadata(
            string value)
        {
            string s =
                NormalizeAsciiLike(
                    value);

            string[] tokens =
            {
                "RISER",
                "TRUC DUNG",
                "TRUCDUNG",
                "VERTICAL PIPE",
                "VERTICAL_PIPE",
                "TDL MEP RISER",
                "TDL_MEP_RISER"
            };

            return
                tokens.Any(t =>
                    s.Contains(
                        t,
                        StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryParseRiserHeight(
            string metadata,
            out double height)
        {
            height = 0.0;

            if (string.IsNullOrWhiteSpace(metadata))
                return false;

            Match match =
                RiserHeightRegex.Match(metadata);

            if (!match.Success)
                return false;

            string raw =
                match.Groups["v"]
                    .Value
                    .Replace(',', '.');

            if (!double.TryParse(
                    raw,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double value))
            {
                return false;
            }

            value = Math.Abs(value);

            if (value <= 0.001)
                return false;

            string unit =
                match.Groups["u"]
                    .Value
                    .Trim()
                    .ToUpperInvariant();

            // UI trục đứng trước đây nhập theo mét.
            // Nếu không có unit mà giá trị <= 50, coi là mét.
            if (unit == "M" ||
                (string.IsNullOrWhiteSpace(unit) &&
                 value <= 50.0))
            {
                value *= 1000.0;
            }

            if (value < VerticalMinHeight ||
                value > 200000.0)
            {
                return false;
            }

            height = value;
            return true;
        }

        private static int ParseRiserDirection(
            string metadata)
        {
            string s =
                NormalizeAsciiLike(metadata);

            if (s.Contains("DOWN") ||
                s.Contains("XUONG") ||
                s.Contains("HUONG XUONG") ||
                s.Contains("DIRECTION=-1"))
            {
                return -1;
            }

            return 1;
        }

        private static MepGraphDeviceNode BuildBlockDevice(
            Transaction tr,
            BlockReference br)
        {
            string name =
                GetBlockName(tr, br);

            Extents3d? ex = null;

            try
            {
                ex = br.GeometricExtents;
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
                         e.MaxPoint.X) * 0.5,
                        (e.MinPoint.Y +
                         e.MaxPoint.Y) * 0.5,
                        (e.MinPoint.Z +
                         e.MaxPoint.Z) * 0.5);
            }

            return
                new MepGraphDeviceNode
                {
                    Id = br.ObjectId,
                    Handle =
                        SafeHandle(br.ObjectId),
                    Kind = "BLOCK",
                    Name = name,
                    Layer = br.Layer ?? "",
                    Position = pos,
                    Extents = ex
                };
        }

        // ============================================================
        // TRUE 3D TOPOLOGY
        // ============================================================

        private static void BuildPipeAdjacency3D(
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

            for (int i = 0; i < pipes.Count; i++)
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

                        grid[key] = set;
                    }

                    set.Add(i);
                }
            }

            HashSet<long> checkedPairs =
                new HashSet<long>();

            for (int i = 0; i < pipes.Count; i++)
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
                        candidates.UnionWith(set);
                    }
                }

                foreach (int j in candidates)
                {
                    if (j <= i)
                        continue;

                    long pairKey =
                        ((long)i << 32) |
                        (uint)j;

                    if (!checkedPairs.Add(pairKey))
                        continue;

                    if (!ArePipesConnected3D(
                            pipes[i],
                            pipes[j],
                            tr))
                    {
                        continue;
                    }

                    pipes[i].Neighbors.Add(j);
                    pipes[j].Neighbors.Add(i);
                }
            }
        }

        private static bool ArePipesConnected3D(
            MepGraphPipeNode a,
            MepGraphPipeNode b,
            Transaction tr)
        {
            if (a == null || b == null)
                return false;

            if (!ExtentsOverlapExpanded3D(
                    a.Extents,
                    b.Extents,
                    ConnectionTolerance,
                    ElevationConnectionTolerance))
            {
                return false;
            }

            // Endpoint-to-endpoint true 3D.
            if (a.Start.DistanceTo(b.Start) <= ConnectionTolerance ||
                a.Start.DistanceTo(b.End) <= ConnectionTolerance ||
                a.End.DistanceTo(b.Start) <= ConnectionTolerance ||
                a.End.DistanceTo(b.End) <= ConnectionTolerance)
            {
                return true;
            }

            // Synthetic riser hoặc curve bất kỳ:
            // branch endpoint chạm giữa tuyến chính.
            if (DistancePointToSegment3D(
                    a.Start,
                    b.Start,
                    b.End) <= ConnectionTolerance ||
                DistancePointToSegment3D(
                    a.End,
                    b.Start,
                    b.End) <= ConnectionTolerance ||
                DistancePointToSegment3D(
                    b.Start,
                    a.Start,
                    a.End) <= ConnectionTolerance ||
                DistancePointToSegment3D(
                    b.End,
                    a.Start,
                    a.End) <= ConnectionTolerance)
            {
                return true;
            }

            // Với Curve thật, dùng closest point 3D để giữ tương thích Arc/Polyline.
            if (!a.IsSynthetic &&
                !b.IsSynthetic &&
                tr != null)
            {
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

                    if (ca != null &&
                        cb != null)
                    {
                        if (DistancePointToCurve3D(
                                cb,
                                a.Start) <=
                                ConnectionTolerance ||
                            DistancePointToCurve3D(
                                cb,
                                a.End) <=
                                ConnectionTolerance ||
                            DistancePointToCurve3D(
                                ca,
                                b.Start) <=
                                ConnectionTolerance ||
                            DistancePointToCurve3D(
                                ca,
                                b.End) <=
                                ConnectionTolerance)
                        {
                            return true;
                        }
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        private static void BuildEdgeMetadata(
            MepGraphSnapshot snapshot)
        {
            snapshot.Edges.Clear();

            if (snapshot?.Pipes == null)
                return;

            for (int i = 0;
                i < snapshot.Pipes.Count;
                i++)
            {
                MepGraphPipeNode a =
                    snapshot.Pipes[i];

                if (a == null)
                    continue;

                foreach (int j in a.Neighbors)
                {
                    if (j <= i ||
                        j < 0 ||
                        j >= snapshot.Pipes.Count)
                    {
                        continue;
                    }

                    MepGraphPipeNode b =
                        snapshot.Pipes[j];

                    if (b == null)
                        continue;

                    GetAbsAngleFeatures(
                        a,
                        b,
                        out double absCos,
                        out double absSin);

                    double angle =
                        Math.Atan2(
                            Math.Abs(absSin),
                            Math.Abs(absCos)) *
                        180.0 /
                        Math.PI;

                    double lenA =
                        Math.Max(1.0, a.Length);

                    double lenB =
                        Math.Max(1.0, b.Length);

                    double lengthRatio =
                        Math.Min(lenA, lenB) /
                        Math.Max(lenA, lenB);

                    bool aDnReliable =
                        !string.IsNullOrWhiteSpace(a.Dn) &&
                        a.DnConfidence >= 0.70;

                    bool bDnReliable =
                        !string.IsNullOrWhiteSpace(b.Dn) &&
                        b.DnConfidence >= 0.70;

                    bool bothDn =
                        aDnReliable &&
                        bDnReliable;

                    bool sameDn =
                        bothDn &&
                        string.Equals(
                            a.Dn,
                            b.Dn,
                            StringComparison.OrdinalIgnoreCase);

                    bool differentDn =
                        bothDn &&
                        !sameDn;

                    int maxDegree =
                        Math.Max(
                            a.Neighbors?.Count ?? 0,
                            b.Neighbors?.Count ?? 0);

                    bool riser =
                        a.IsRiser ||
                        b.IsRiser;

                    bool reducer =
                        !riser &&
                        differentDn &&
                        absCos >= 0.80;

                    bool tee =
                        !riser &&
                        maxDegree >= 3 &&
                        absSin >= 0.25;

                    bool elbow =
                        !riser &&
                        !tee &&
                        !reducer &&
                        absSin >= 0.25 &&
                        absCos <= 0.97;

                    string type =
                        riser
                            ? "RISER"
                            : reducer
                                ? "REDUCER"
                                : tee
                                    ? "TEE"
                                    : elbow
                                        ? "ELBOW"
                                        : "STRAIGHT";

                    Point3d jointA;
                    Point3d jointB;

                    FindClosestEndpoints(
                        a,
                        b,
                        out jointA,
                        out jointB);

                    snapshot.Edges.Add(
                        new MepGraphEdge
                        {
                            From = i,
                            To = j,
                            Type = type,
                            AngleDegrees = angle,
                            LengthRatio = lengthRatio,
                            ElevationDelta =
                                Math.Abs(
                                    jointA.Z -
                                    jointB.Z),
                            EndpointDistance =
                                jointA.DistanceTo(
                                    jointB),
                            SameDn = sameDn,
                            DifferentDn = differentDn,
                            IsRiser = riser,
                            IsReducer = reducer,
                            IsTee = tee,
                            IsElbow = elbow
                        });
                }
            }
        }

        private static void ClassifyJunctions(
            MepGraphSnapshot snapshot)
        {
            if (snapshot?.Pipes == null)
                return;

            for (int i = 0;
                i < snapshot.Pipes.Count;
                i++)
            {
                MepGraphPipeNode pipe =
                    snapshot.Pipes[i];

                if (pipe == null)
                    continue;

                if (pipe.IsRiser)
                {
                    pipe.JunctionType = "RISER";
                    continue;
                }

                int degree =
                    pipe.Neighbors?.Count ?? 0;

                if (degree >= 4)
                {
                    pipe.JunctionType = "CROSS";
                    continue;
                }

                if (degree >= 3)
                {
                    pipe.JunctionType = "TEE";
                    continue;
                }

                if (degree <= 1)
                {
                    pipe.JunctionType = "END";
                    continue;
                }

                List<MepGraphEdge> incident =
                    snapshot.Edges
                        .Where(e =>
                            e != null &&
                            (e.From == i ||
                             e.To == i))
                        .ToList();

                if (incident.Any(e => e.IsReducer))
                    pipe.JunctionType = "REDUCER";
                else if (incident.Any(e => e.IsElbow))
                    pipe.JunctionType = "ELBOW";
                else
                    pipe.JunctionType = "STRAIGHT";
            }
        }

        // ============================================================
        // DN TEXT + PROPAGATION
        // ============================================================

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

            foreach (MepGraphTextNode text in texts)
            {
                if (text == null ||
                    string.IsNullOrWhiteSpace(text.Dn))
                {
                    continue;
                }

                int best = -1;
                double bestScore = double.MaxValue;

                for (int i = 0;
                    i < pipes.Count;
                    i++)
                {
                    MepGraphPipeNode pipe =
                        pipes[i];

                    if (pipe == null)
                        continue;

                    // DN text trên plan: dùng khoảng cách XY như bản cũ.
                    double distance =
                        DistancePointToSegment2D(
                            text.Position,
                            pipe.Start,
                            pipe.End);

                    if (distance >
                        DnTextSearchDistance)
                    {
                        continue;
                    }

                    double anglePenalty = 0.0;

                    double pipeAngle =
                        GetPipePlanAngle(pipe);

                    if (!double.IsNaN(pipeAngle))
                    {
                        double diff =
                            ParallelAngleDifference(
                                pipeAngle,
                                text.Rotation);

                        if (diff >
                            Math.PI / 6.0)
                        {
                            anglePenalty = 280.0;
                        }
                        else
                        {
                            anglePenalty =
                                diff * 180.0;
                        }
                    }

                    if (pipe.IsVertical)
                    {
                        // Text DN cạnh riser không có hướng line hữu ích.
                        anglePenalty *= 0.25;
                    }

                    double score =
                        distance +
                        anglePenalty;

                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = i;
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
                    !string.IsNullOrWhiteSpace(target.Dn) &&
                    !string.Equals(
                        target.Dn,
                        text.Dn,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                target.Dn = text.Dn;
                target.DnConfidence = 0.98;
                target.DnSource = "TEXT";
                target.LayerLooksLikePipe = true;
            }
        }

        private static void PropagateDnConservatively(
            List<MepGraphPipeNode> pipes,
            MepGraphSnapshot snapshot)
        {
            snapshot.ExplicitDnCount =
                pipes.Count(p =>
                    !string.IsNullOrWhiteSpace(p.Dn) &&
                    (p.DnSource == "TEXT" ||
                     p.DnSource == "AI_LAYER" ||
                     p.DnSource == "LAYER" ||
                     p.DnSource == "RISER_META"));

            int inherited = 0;

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

                    if (!string.IsNullOrWhiteSpace(pipe.Dn))
                        continue;

                    List<MepGraphPipeNode> known =
                        pipe.Neighbors
                            .Where(n =>
                                n >= 0 &&
                                n < pipes.Count)
                            .Select(n => pipes[n])
                            .Where(n =>
                                n != null &&
                                !string.IsNullOrWhiteSpace(n.Dn))
                            .ToList();

                    if (known.Count == 0)
                        continue;

                    List<string> sizes =
                        known
                            .Select(n => n.Dn)
                            .Distinct(
                                StringComparer.OrdinalIgnoreCase)
                            .ToList();

                    // Có nhiều DN ở junction => không đoán.
                    if (sizes.Count != 1)
                        continue;

                    bool sameLayerSupport =
                        known.Any(n =>
                            string.Equals(
                                n.Layer,
                                pipe.Layer,
                                StringComparison.OrdinalIgnoreCase));

                    bool riserSupport =
                        pipe.IsRiser &&
                        known.Any(n =>
                            n.IsRiser ||
                            PlanDistance(
                                n.Center,
                                pipe.Center) <=
                                ConnectionTolerance);

                    if (!pipe.LayerLooksLikePipe &&
                        !sameLayerSupport &&
                        !riserSupport)
                    {
                        continue;
                    }

                    double conf =
                        Math.Max(
                            0.72,
                            known.Max(n =>
                                n.DnConfidence) -
                            0.10);

                    pending.Add(
                        (i,
                         sizes[0],
                         conf));
                }

                if (pending.Count == 0)
                    break;

                foreach (var item in pending)
                {
                    MepGraphPipeNode pipe =
                        pipes[item.Index];

                    if (!string.IsNullOrWhiteSpace(pipe.Dn))
                        continue;

                    pipe.Dn = item.Dn;
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

                if (p == null)
                    continue;

                if (p.LayerLooksLikePipe ||
                    p.IsRiser ||
                    !string.IsNullOrWhiteSpace(p.Dn))
                {
                    keep.Add(i);
                    queue.Enqueue(i);
                }
            }

            int seedCount =
                queue.Count;

            for (int q = 0;
                q < seedCount;
                q++)
            {
                int i = queue.Dequeue();

                MepGraphPipeNode source =
                    pipes[i];

                foreach (int n
                    in source.Neighbors)
                {
                    if (n < 0 ||
                        n >= pipes.Count ||
                        keep.Contains(n))
                    {
                        continue;
                    }

                    MepGraphPipeNode target =
                        pipes[n];

                    if (target == null)
                        continue;

                    if ((string.Equals(
                             source.Layer,
                             target.Layer,
                             StringComparison.OrdinalIgnoreCase) ||
                         target.IsRiser) &&
                        target.Length <= 15000.0)
                    {
                        keep.Add(n);
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
                    snapshot.AmbiguousDeviceCount++;
            }
        }

        // ============================================================
        // GEOMETRY HELPERS
        // ============================================================

        private static Extents3d? BuildSelectionBounds(
            Transaction tr,
            IEnumerable<ObjectId> ids)
        {
            bool has = false;
            Extents3d result =
                default(Extents3d);

            foreach (ObjectId id in ids)
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
                        result = ex;
                        has = true;
                    }
                    else
                    {
                        result.AddExtents(ex);
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

                foreach (ObjectId id in space)
                {
                    if (ids.Contains(id))
                        continue;

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

                    if (ExtentsOverlapExpanded2D(
                            ex,
                            bounds,
                            250.0))
                    {
                        ids.Add(id);
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
                (maxX - minX + 1) *
                (maxY - minY + 1);

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
                    ((minX + maxX) / 2)
                        .ToString(
                            CultureInfo.InvariantCulture) +
                    ":" +
                    ((minY + maxY) / 2)
                        .ToString(
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
                    insertion
                };

            if (!extents.HasValue)
                return points;

            Extents3d ex =
                extents.Value;

            double cx =
                (ex.MinPoint.X +
                 ex.MaxPoint.X) * 0.5;

            double cy =
                (ex.MinPoint.Y +
                 ex.MaxPoint.Y) * 0.5;

            double cz =
                (ex.MinPoint.Z +
                 ex.MaxPoint.Z) * 0.5;

            points.Add(
                new Point3d(
                    cx,
                    cy,
                    cz));

            points.Add(
                new Point3d(
                    ex.MinPoint.X,
                    cy,
                    cz));

            points.Add(
                new Point3d(
                    ex.MaxPoint.X,
                    cy,
                    cz));

            points.Add(
                new Point3d(
                    cx,
                    ex.MinPoint.Y,
                    cz));

            points.Add(
                new Point3d(
                    cx,
                    ex.MaxPoint.Y,
                    cz));

            return points;
        }

        private static double DistancePointToCurve3D(
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
                    cp.DistanceTo(p);
            }
            catch
            {
                try
                {
                    return
                        Math.Min(
                            curve.StartPoint.DistanceTo(p),
                            curve.EndPoint.DistanceTo(p));
                }
                catch
                {
                    return double.MaxValue;
                }
            }
        }

        private static double DistancePointToSegment2D(
            Point3d p,
            Point3d a,
            Point3d b)
        {
            double vx =
                b.X - a.X;

            double vy =
                b.Y - a.Y;

            double wx =
                p.X - a.X;

            double wy =
                p.Y - a.Y;

            double vv =
                vx * vx +
                vy * vy;

            if (vv <= 1e-9)
                return PlanDistance(p, a);

            double t =
                (wx * vx +
                 wy * vy) /
                vv;

            t =
                Math.Max(
                    0.0,
                    Math.Min(
                        1.0,
                        t));

            Point3d q =
                new Point3d(
                    a.X + vx * t,
                    a.Y + vy * t,
                    p.Z);

            return PlanDistance(p, q);
        }

        private static double DistancePointToSegment3D(
            Point3d p,
            Point3d a,
            Point3d b)
        {
            Vector3d ab =
                b - a;

            Vector3d ap =
                p - a;

            double denom =
                ab.DotProduct(ab);

            if (denom <= 1e-12)
                return p.DistanceTo(a);

            double t =
                ap.DotProduct(ab) /
                denom;

            t =
                Math.Max(
                    0.0,
                    Math.Min(
                        1.0,
                        t));

            Point3d q =
                a + ab * t;

            return p.DistanceTo(q);
        }

        private static double GetPipePlanAngle(
            MepGraphPipeNode pipe)
        {
            if (pipe == null)
                return double.NaN;

            double dx =
                pipe.End.X -
                pipe.Start.X;

            double dy =
                pipe.End.Y -
                pipe.Start.Y;

            if (Math.Sqrt(
                    dx * dx +
                    dy * dy) < 1e-6)
            {
                return double.NaN;
            }

            return Math.Atan2(dy, dx);
        }

        private static double ParallelAngleDifference(
            double a,
            double b)
        {
            double diff =
                Math.Abs(
                    NormalizeAngle(a) -
                    NormalizeAngle(b));

            while (diff > Math.PI)
                diff -= Math.PI;

            diff =
                Math.Abs(diff);

            return
                Math.Min(
                    diff,
                    Math.PI - diff);
        }

        private static double NormalizeAngle(
            double a)
        {
            while (a < 0)
                a += Math.PI * 2.0;

            while (a >=
                Math.PI * 2.0)
            {
                a -=
                    Math.PI * 2.0;
            }

            return a;
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
                    return 0.0;
                }
            }
        }

        private static bool ExtentsOverlapExpanded2D(
            Extents3d a,
            Extents3d b,
            double margin)
        {
            return
                a.MinPoint.X <=
                    b.MaxPoint.X + margin &&
                a.MaxPoint.X >=
                    b.MinPoint.X - margin &&
                a.MinPoint.Y <=
                    b.MaxPoint.Y + margin &&
                a.MaxPoint.Y >=
                    b.MinPoint.Y - margin;
        }

        private static bool ExtentsOverlapExpanded3D(
            Extents3d a,
            Extents3d b,
            double xyMargin,
            double zMargin)
        {
            return
                ExtentsOverlapExpanded2D(
                    a,
                    b,
                    xyMargin) &&
                a.MinPoint.Z <=
                    b.MaxPoint.Z + zMargin &&
                a.MaxPoint.Z >=
                    b.MinPoint.Z - zMargin;
        }

        private static double PlanDistance(
            Point3d a,
            Point3d b)
        {
            double dx =
                a.X - b.X;

            double dy =
                a.Y - b.Y;

            return
                Math.Sqrt(
                    dx * dx +
                    dy * dy);
        }

        private static Point3d MidPoint3D(
            Point3d a,
            Point3d b)
        {
            return
                new Point3d(
                    (a.X + b.X) * 0.5,
                    (a.Y + b.Y) * 0.5,
                    (a.Z + b.Z) * 0.5);
        }

        private static Extents3d BuildExtents3D(
            Point3d a,
            Point3d b)
        {
            Point3d min =
                new Point3d(
                    Math.Min(a.X, b.X),
                    Math.Min(a.Y, b.Y),
                    Math.Min(a.Z, b.Z));

            Point3d max =
                new Point3d(
                    Math.Max(a.X, b.X),
                    Math.Max(a.Y, b.Y),
                    Math.Max(a.Z, b.Z));

            // Tránh extents zero-size gây edge case spatial filter.
            const double eps = 1.0;

            if (Math.Abs(max.X - min.X) < eps)
            {
                min =
                    new Point3d(
                        min.X - eps,
                        min.Y,
                        min.Z);

                max =
                    new Point3d(
                        max.X + eps,
                        max.Y,
                        max.Z);
            }

            if (Math.Abs(max.Y - min.Y) < eps)
            {
                min =
                    new Point3d(
                        min.X,
                        min.Y - eps,
                        min.Z);

                max =
                    new Point3d(
                        max.X,
                        max.Y + eps,
                        max.Z);
            }

            return
                new Extents3d(
                    min,
                    max);
        }

        private static void FindClosestEndpoints(
            MepGraphPipeNode a,
            MepGraphPipeNode b,
            out Point3d pointA,
            out Point3d pointB)
        {
            pointA = a.Start;
            pointB = b.Start;
            double best =
                pointA.DistanceTo(pointB);

            Point3d[] aa =
            {
                a.Start,
                a.End
            };

            Point3d[] bb =
            {
                b.Start,
                b.End
            };

            foreach (Point3d pa in aa)
            {
                foreach (Point3d pb in bb)
                {
                    double d =
                        pa.DistanceTo(pb);

                    if (d < best)
                    {
                        best = d;
                        pointA = pa;
                        pointB = pb;
                    }
                }
            }
        }

        private static void GetAbsAngleFeatures(
            MepGraphPipeNode a,
            MepGraphPipeNode b,
            out double absCos,
            out double absSin)
        {
            absCos = 1.0;
            absSin = 0.0;

            if (a == null || b == null)
                return;

            double adx =
                a.End.X -
                a.Start.X;

            double ady =
                a.End.Y -
                a.Start.Y;

            double bdx =
                b.End.X -
                b.Start.X;

            double bdy =
                b.End.Y -
                b.Start.Y;

            double al =
                Math.Sqrt(
                    adx * adx +
                    ady * ady);

            double bl =
                Math.Sqrt(
                    bdx * bdx +
                    bdy * bdy);

            // Vertical riser trong plan có vector XY ~ 0.
            // Khi đó angle plan không có ý nghĩa.
            if (al < 1e-6 ||
                bl < 1e-6)
            {
                return;
            }

            double dot =
                (adx * bdx +
                 ady * bdy) /
                (al * bl);

            double cross =
                (adx * bdy -
                 ady * bdx) /
                (al * bl);

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

            absCos =
                Math.Abs(dot);

            absSin =
                Math.Abs(cross);
        }

        // ============================================================
        // PARSERS
        // ============================================================

        public static string ParseDn(
            string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            string normalized =
                value
                    .Replace("\\P", " ")
                    .Replace("\\p", " ")
                    .Replace("{", " ")
                    .Replace("}", " ");

            Match match =
                DnRegex.Match(normalized);

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
                    StringComparison.OrdinalIgnoreCase) >= 0;

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

            if (nominal.Contains(number))
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
                "MEP",
                "RISER"
            };

            return
                tokens.Any(t =>
                    s.Contains(t));
        }

        private static string StripMText(
            string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            string s =
                value
                    .Replace("\\P", " ")
                    .Replace("\\p", " ");

            s =
                Regex.Replace(
                    s,
                    @"\\[A-Za-z][^;]*;",
                    "");

            return
                s
                    .Replace("{", "")
                    .Replace("}", "");
        }

        private static string NormalizeAsciiLike(
            string value)
        {
            string s =
                (value ?? "")
                    .Trim()
                    .ToUpperInvariant();

            // Đủ cho các keyword tiếng Việt mà riser metadata hay dùng.
            return
                s
                    .Replace('Đ', 'D')
                    .Replace("Ứ", "U")
                    .Replace("Ừ", "U")
                    .Replace("Ử", "U")
                    .Replace("Ữ", "U")
                    .Replace("Ự", "U")
                    .Replace("Ư", "U")
                    .Replace("Ớ", "O")
                    .Replace("Ờ", "O")
                    .Replace("Ở", "O")
                    .Replace("Ỡ", "O")
                    .Replace("Ợ", "O")
                    .Replace("Ơ", "O")
                    .Replace("Á", "A")
                    .Replace("À", "A")
                    .Replace("Ả", "A")
                    .Replace("Ã", "A")
                    .Replace("Ạ", "A")
                    .Replace("Â", "A")
                    .Replace("Ă", "A")
                    .Replace("É", "E")
                    .Replace("È", "E")
                    .Replace("Ẻ", "E")
                    .Replace("Ẽ", "E")
                    .Replace("Ẹ", "E")
                    .Replace("Ê", "E")
                    .Replace("Í", "I")
                    .Replace("Ì", "I")
                    .Replace("Ỉ", "I")
                    .Replace("Ĩ", "I")
                    .Replace("Ị", "I")
                    .Replace("Ó", "O")
                    .Replace("Ò", "O")
                    .Replace("Ỏ", "O")
                    .Replace("Õ", "O")
                    .Replace("Ọ", "O")
                    .Replace("Ô", "O")
                    .Replace("Ú", "U")
                    .Replace("Ù", "U")
                    .Replace("Ủ", "U")
                    .Replace("Ũ", "U")
                    .Replace("Ụ", "U")
                    .Replace("Ý", "Y")
                    .Replace("Ỳ", "Y")
                    .Replace("Ỷ", "Y")
                    .Replace("Ỹ", "Y")
                    .Replace("Ỵ", "Y");
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

        // ============================================================
        // GRAPH JSON V2
        // ============================================================

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

                if (string.IsNullOrWhiteSpace(appData))
                    appData = Path.GetTempPath();

                string folder =
                    Path.Combine(
                        appData,
                        "TDL_MEP",
                        "Graph");

                Directory.CreateDirectory(folder);

                string path =
                    Path.Combine(
                        folder,
                        "last_graph.json");

                object dto =
                    new
                    {
                        version = 2,
                        schema =
                            "TDL_MEP_GRAPH_V2_3D",
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
                                edges =
                                    snapshot.Edges.Count,
                                vertical_pipes =
                                    snapshot.VerticalPipeCount,
                                risers =
                                    snapshot.RiserCount,
                                tee_edges =
                                    snapshot.TeeEdgeCount,
                                elbow_edges =
                                    snapshot.ElbowEdgeCount,
                                reducer_edges =
                                    snapshot.ReducerEdgeCount,
                                riser_edges =
                                    snapshot.RiserEdgeCount,
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
                                        layer_pipe =
                                            p.LayerLooksLikePipe,

                                        start =
                                            new[]
                                            {
                                                p.Start.X,
                                                p.Start.Y,
                                                p.Start.Z
                                            },

                                        end =
                                            new[]
                                            {
                                                p.End.X,
                                                p.End.Y,
                                                p.End.Z
                                            },

                                        center =
                                            new[]
                                            {
                                                p.Center.X,
                                                p.Center.Y,
                                                p.Center.Z
                                            },

                                        elevation_start =
                                            p.Start.Z,
                                        elevation_end =
                                            p.End.Z,

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

                                        node_kind =
                                            p.NodeKind,
                                        synthetic =
                                            p.IsSynthetic,
                                        is_vertical =
                                            p.IsVertical,
                                        is_riser =
                                            p.IsRiser,
                                        riser_height =
                                            p.RiserHeight,
                                        vertical_ratio =
                                            p.VerticalRatio,
                                        junction_type =
                                            p.JunctionType,

                                        neighbors =
                                            p.Neighbors
                                                .Distinct()
                                                .OrderBy(n => n)
                                                .ToArray()
                                    }),

                        edges =
                            snapshot.Edges.Select(
                                e =>
                                    new
                                    {
                                        from =
                                            e.From,
                                        to =
                                            e.To,
                                        type =
                                            e.Type,
                                        angle_deg =
                                            e.AngleDegrees,
                                        length_ratio =
                                            e.LengthRatio,
                                        elevation_delta =
                                            e.ElevationDelta,
                                        endpoint_distance =
                                            e.EndpointDistance,
                                        same_dn =
                                            e.SameDn,
                                        different_dn =
                                            e.DifferentDn,
                                        riser =
                                            e.IsRiser,
                                        reducer =
                                            e.IsReducer,
                                        tee =
                                            e.IsTee,
                                        elbow =
                                            e.IsElbow
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
                                                d.Position.Y,
                                                d.Position.Z
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
                            WriteIndented = true
                        });

                File.WriteAllText(
                    path,
                    json,
                    Encoding.UTF8);

                // STEP30B.1:
                // Local History hash dùng XYZ + topology thật.
                // Không dùng canonical 2D cũ để tránh 2 mạng trùng XY nhưng
                // khác elevation ghi đè lên nhau.
                try
                {
                    string historyFolder =
                        Path.Combine(
                            folder,
                            "History");

                    Directory.CreateDirectory(
                        historyFolder);

                    string structureHash =
                        BuildStructureHashV2(
                            snapshot);

                    if (string.IsNullOrWhiteSpace(
                            structureHash))
                    {
                        structureHash =
                            ComputeSha256(json);
                    }

                    string historyPath =
                        Path.Combine(
                            historyFolder,
                            structureHash +
                            ".json");

                    File.WriteAllText(
                        historyPath,
                        json,
                        Encoding.UTF8);
                }
                catch
                {
                    // History/GNN dataset không được làm fail Graph chính.
                }

                return path;
            }
            catch
            {
                return "";
            }
        }

        private static string BuildStructureHashV2(
            MepGraphSnapshot snapshot)
        {
            if (snapshot?.Pipes == null ||
                snapshot.Pipes.Count == 0)
            {
                return "";
            }

            try
            {
                double originX =
                    snapshot.Pipes.Min(p =>
                        Math.Min(
                            p.Start.X,
                            p.End.X));

                double originY =
                    snapshot.Pipes.Min(p =>
                        Math.Min(
                            p.Start.Y,
                            p.End.Y));

                double originZ =
                    snapshot.Pipes.Min(p =>
                        Math.Min(
                            p.Start.Z,
                            p.End.Z));

                object structure =
                    new
                    {
                        version = 2,
                        schema =
                            "STRUCTURE_3D",
                        pipes =
                            snapshot.Pipes.Select(
                                (p, i) =>
                                    new
                                    {
                                        id = i,
                                        start =
                                            new[]
                                            {
                                                Quantize(
                                                    p.Start.X -
                                                    originX),
                                                Quantize(
                                                    p.Start.Y -
                                                    originY),
                                                Quantize(
                                                    p.Start.Z -
                                                    originZ)
                                            },
                                        end =
                                            new[]
                                            {
                                                Quantize(
                                                    p.End.X -
                                                    originX),
                                                Quantize(
                                                    p.End.Y -
                                                    originY),
                                                Quantize(
                                                    p.End.Z -
                                                    originZ)
                                            },
                                        length =
                                            Quantize(p.Length),
                                        riser =
                                            p.IsRiser,
                                        neighbors =
                                            p.Neighbors
                                                .Distinct()
                                                .OrderBy(n => n)
                                                .ToArray()
                                    })
                                .ToArray()
                    };

                return
                    ComputeSha256(
                        JsonSerializer.Serialize(
                            structure));
            }
            catch
            {
                return "";
            }
        }

        private static string ComputeSha256(
            string text)
        {
            using (SHA256 sha =
                SHA256.Create())
            {
                byte[] digest =
                    sha.ComputeHash(
                        Encoding.UTF8.GetBytes(
                            text ?? ""));

                return
                    BitConverter
                        .ToString(digest)
                        .Replace("-", "")
                        .ToLowerInvariant();
            }
        }

        private static double Quantize(
            double value)
        {
            return
                Math.Round(
                    value,
                    3,
                    MidpointRounding.AwayFromZero);
        }
    }
}