#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace ClassLibrary4
{
    public sealed class MepDuctTextSeed
    {
        public ObjectId Id { get; set; } = ObjectId.Null;
        public Point3d Position { get; set; } = Point3d.Origin;
        public double Rotation { get; set; }
        public string RawText { get; set; } = "";
        public string Layer { get; set; } = "";
        public MepDuctSizeInfo Size { get; set; }
    }

    public sealed class MepDuctSegment
    {
        public int Id { get; set; }
        public Point3d Start { get; set; } = Point3d.Origin;
        public Point3d End { get; set; } = Point3d.Origin;
        public double LengthMm { get; set; }
        public string Layer { get; set; } = "";
        public string SystemCode { get; set; } = "";
        public string FireRating { get; set; } = "";
        public string Size { get; set; } = "";
        public string Shape { get; set; } = "";
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }
        public double DiameterMm { get; set; }
        public double Confidence { get; set; }
        public string Evidence { get; set; } = "";
        public string Representation { get; set; } = "";
        public List<ObjectId> SourceIds { get; set; } =
            new List<ObjectId>();

        public Point3d Center =>
            new Point3d(
                (Start.X + End.X) * 0.5,
                (Start.Y + End.Y) * 0.5,
                (Start.Z + End.Z) * 0.5);

        public double SurfaceAreaM2
        {
            get
            {
                double lengthM =
                    Math.Max(
                        0.0,
                        LengthMm) /
                    1000.0;

                if (string.Equals(
                        Shape,
                        "RECT",
                        StringComparison.OrdinalIgnoreCase))
                {
                    double perimeterM =
                        2.0 *
                        (WidthMm + HeightMm) /
                        1000.0;

                    return
                        perimeterM *
                        lengthM;
                }

                if (string.Equals(
                        Shape,
                        "ROUND",
                        StringComparison.OrdinalIgnoreCase))
                {
                    double perimeterM =
                        Math.PI *
                        DiameterMm /
                        1000.0;

                    return
                        perimeterM *
                        lengthM;
                }

                return 0.0;
            }
        }
    }

    public sealed class MepDuctTakeoffRow
    {
        public string SystemCode { get; set; } = "";
        public string Size { get; set; } = "";
        public string Shape { get; set; } = "";
        public string FireRating { get; set; } = "";
        public int SegmentCount { get; set; }
        public double LengthMm { get; set; }
        public double SurfaceAreaM2 { get; set; }
        public double AverageConfidence { get; set; }

        public double LengthMeters =>
            LengthMm / 1000.0;
    }

    public sealed class MepDuctScanResult
    {
        public List<MepDuctSegment> Segments { get; set; } =
            new List<MepDuctSegment>();

        public List<MepDuctTakeoffRow> Stats { get; set; } =
            new List<MepDuctTakeoffRow>();

        public int RawCurveCount { get; set; }
        public int DuctSizeTextCount { get; set; }
        public int RectFrameCount { get; set; }
        public int DoubleLinePairCount { get; set; }
        public int SingleCenterlineCount { get; set; }
        public int RejectedCount { get; set; }
        public int AmbiguousCount { get; set; }
        public int OutputSegmentCount =>
            Segments?.Count ?? 0;

        public double TotalLengthMm =>
            Segments?.Sum(x =>
                Math.Max(
                    0.0,
                    x?.LengthMm ?? 0.0)) ?? 0.0;

        public double TotalAreaM2 =>
            Segments?.Sum(x =>
                Math.Max(
                    0.0,
                    x?.SurfaceAreaM2 ?? 0.0)) ?? 0.0;

        public double AverageConfidence =>
            Segments == null ||
            Segments.Count == 0
                ? 0.0
                : Segments
                    .Select(x =>
                        x?.Confidence ?? 0.0)
                    .DefaultIfEmpty(0.0)
                    .Average();
    }

    public sealed class MepDuctNearestSizeResult
    {
        public bool Found { get; set; }
        public string Size { get; set; } = "";
        public string SystemCode { get; set; } = "";
        public string FireRating { get; set; } = "";
        public double Distance { get; set; } = double.MaxValue;
        public double Confidence { get; set; }
        public bool Ambiguous { get; set; }
        public string Evidence { get; set; } = "";
    }

    /// <summary>
    /// STEP30B-D1 - AI duct semantic engine.
    ///
    /// Chạy trên cùng ObjectId[] mà nút AI ĐƯỜNG ỐNG đã quét:
    /// - đọc WxH / ØD + EI + system text,
    /// - ghép text với tuyến song song,
    /// - gộp 2 đường biên thành 1 centerline,
    /// - nhận closed rectangular polyline,
    /// - thống kê length + m2,
    /// - vẽ overlay TDL_AI_DUCT_* mà không sửa nét gốc.
    /// </summary>
    public sealed class MepDuctSemanticEngine
    {
        public const string OverlayPrefix =
            "TDL_AI_DUCT_";

        public const string CheckLayer =
            "TDL_AI_DUCT_CHECK";

        private const double MaxTextAngleRadians =
            Math.PI / 7.2; // 25 deg

        private const double DoubleLineAngleRadians =
            Math.PI / 30.0; // 6 deg

        private const double MinCurveLength =
            80.0;

        private const double MaxSeedDistanceBase =
            1200.0;

        private const double EndpointConnectTolerance =
            180.0;

        private sealed class CurveCandidate
        {
            public ObjectId Id { get; set; } = ObjectId.Null;
            public Curve Curve { get; set; }
            public Entity Entity { get; set; }
            public string Layer { get; set; } = "";
            public Point3d Start { get; set; } = Point3d.Origin;
            public Point3d End { get; set; } = Point3d.Origin;
            public double Length { get; set; }
            public bool ClosedRectangle { get; set; }
            public Point3d RectCenterStart { get; set; } = Point3d.Origin;
            public Point3d RectCenterEnd { get; set; } = Point3d.Origin;
            public MepDuctTextSeed Seed { get; set; }
            public double SeedDistance { get; set; } = double.MaxValue;
            public double SeedAngle { get; set; } = double.MaxValue;
            public bool LayerLooksDuct { get; set; }
        }

        public MepDuctScanResult AnalyzeAndDraw(
            Document doc,
            IEnumerable<ObjectId> selectedIds,
            bool drawOverlay = true,
            bool deleteOldOverlay = true)
        {
            MepDuctScanResult result =
                new MepDuctScanResult();

            if (doc == null ||
                selectedIds == null)
            {
                return result;
            }

            ObjectId[] ids =
                selectedIds
                    .Where(x =>
                        !x.IsNull &&
                        x.IsValid &&
                        !x.IsErased)
                    .Distinct()
                    .ToArray();

            if (ids.Length == 0)
                return result;

            Database db =
                doc.Database;

            using (DocumentLock docLock =
                doc.LockDocument())
            using (Transaction tr =
                db.TransactionManager.StartTransaction())
            {
                BlockTableRecord space =
                    tr.GetObject(
                        db.CurrentSpaceId,
                        drawOverlay
                            ? OpenMode.ForWrite
                            : OpenMode.ForRead)
                    as BlockTableRecord;

                List<MepDuctTextSeed> seeds =
                    ReadDuctTextSeeds(
                        tr,
                        ids);

                result.DuctSizeTextCount =
                    seeds.Count;

                List<CurveCandidate> curves =
                    ReadCurveCandidates(
                        tr,
                        ids);

                result.RawCurveCount =
                    curves.Count;

                AttachBestSeeds(
                    curves,
                    seeds,
                    ref result);

                List<MepDuctSegment> segments =
                    BuildSegments(
                        curves,
                        ref result);

                PropagateUnambiguousSize(
                    segments);

                segments =
                    segments
                        .Where(x =>
                            x != null &&
                            !string.IsNullOrWhiteSpace(
                                x.Size) &&
                            x.LengthMm >=
                                MinCurveLength)
                        .ToList();

                for (int i = 0;
                    i < segments.Count;
                    i++)
                {
                    segments[i].Id = i;
                }

                result.Segments =
                    segments;

                result.Stats =
                    BuildStats(
                        segments);

                if (drawOverlay &&
                    space != null)
                {
                    if (deleteOldOverlay)
                    {
                        DeleteOldOverlay(
                            tr,
                            space);
                    }

                    DrawOverlay(
                        tr,
                        db,
                        space,
                        result);
                }

                tr.Commit();
            }

            return result;
        }

        public MepDuctNearestSizeResult InferNearestSize(
            Point3d point,
            Extents3d? deviceExtents,
            MepDuctScanResult scan,
            double maxDistance = 1600.0)
        {
            MepDuctNearestSizeResult result =
                new MepDuctNearestSizeResult();

            if (scan?.Segments == null ||
                scan.Segments.Count == 0)
            {
                return result;
            }

            List<Point3d> refs =
                BuildReferencePoints(
                    point,
                    deviceExtents);

            List<(MepDuctSegment Segment, double Score, double Distance)> ranked =
                new List<(MepDuctSegment, double, double)>();

            foreach (MepDuctSegment segment
                in scan.Segments)
            {
                if (segment == null ||
                    string.IsNullOrWhiteSpace(
                        segment.Size))
                {
                    continue;
                }

                double d =
                    refs.Min(p =>
                        DistancePointToSegment2D(
                            p,
                            segment.Start,
                            segment.End));

                if (d > maxDistance)
                    continue;

                double score =
                    d -
                    Math.Max(
                        0.0,
                        Math.Min(
                            1.0,
                            segment.Confidence)) *
                    260.0;

                ranked.Add(
                    (segment,
                     score,
                     d));
            }

            if (ranked.Count == 0)
                return result;

            List<(MepDuctSegment Segment, double Score, double Distance)> ordered =
                ranked
                    .OrderBy(x =>
                        x.Score)
                    .ThenBy(x =>
                        x.Distance)
                    .ToList();

            var best =
                ordered[0];

            result.Found = true;
            result.Size =
                best.Segment.Size;
            result.SystemCode =
                best.Segment.SystemCode;
            result.FireRating =
                best.Segment.FireRating;
            result.Distance =
                best.Distance;
            result.Confidence =
                Math.Max(
                    0.55,
                    Math.Min(
                        0.98,
                        best.Segment.Confidence -
                        Math.Min(
                            0.20,
                            best.Distance /
                            Math.Max(
                                1.0,
                                maxDistance) *
                            0.20)));

            if (ordered.Count > 1)
            {
                var second =
                    ordered[1];

                bool different =
                    !string.Equals(
                        best.Segment.Size,
                        second.Segment.Size,
                        StringComparison.OrdinalIgnoreCase);

                if (different &&
                    second.Score -
                        best.Score <
                        120.0)
                {
                    result.Ambiguous = true;
                    result.Confidence =
                        Math.Max(
                            0.50,
                            result.Confidence -
                            0.18);
                }
            }

            result.Evidence =
                "DUCT nearest: " +
                result.Size +
                " | d=" +
                result.Distance.ToString(
                    "0",
                    CultureInfo.InvariantCulture) +
                "mm";

            return result;
        }

        public static string BuildCompactSummary(
            MepDuctScanResult run,
            int maxRows = 12)
        {
            if (run == null ||
                run.OutputSegmentCount <= 0)
            {
                return
                    "ỐNG GIÓ: chưa có tuyến đủ bằng chứng.";
            }

            List<string> lines =
                new List<string>
                {
                    "ỐNG GIÓ AI",
                    "Đoạn: " +
                    run.OutputSegmentCount +
                    " | Text size: " +
                    run.DuctSizeTextCount +
                    " | 2 nét: " +
                    run.DoubleLinePairCount +
                    " | Khung: " +
                    run.RectFrameCount,
                    "Tổng chiều dài: " +
                    (run.TotalLengthMm / 1000.0)
                        .ToString(
                            "0.00",
                            CultureInfo.InvariantCulture) +
                    " m | Diện tích thẳng: " +
                    run.TotalAreaM2.ToString(
                        "0.00",
                        CultureInfo.InvariantCulture) +
                    " m²"
                };

            foreach (MepDuctTakeoffRow row
                in (run.Stats ??
                    new List<MepDuctTakeoffRow>())
                    .Take(
                        Math.Max(
                            1,
                            maxRows)))
            {
                lines.Add(
                    (string.IsNullOrWhiteSpace(
                        row.SystemCode)
                        ? "DUCT"
                        : row.SystemCode) +
                    " | " +
                    row.Size +
                    (string.IsNullOrWhiteSpace(
                        row.FireRating)
                        ? ""
                        : " " +
                          row.FireRating) +
                    " | " +
                    row.LengthMeters.ToString(
                        "0.00",
                        CultureInfo.InvariantCulture) +
                    " m | " +
                    row.SurfaceAreaM2.ToString(
                        "0.00",
                        CultureInfo.InvariantCulture) +
                    " m²");
            }

            if (run.Stats != null &&
                run.Stats.Count > maxRows)
            {
                lines.Add(
                    "... +" +
                    (run.Stats.Count -
                     maxRows) +
                    " dòng");
            }

            return
                string.Join(
                    Environment.NewLine,
                    lines);
        }

        // ============================================================
        // READ
        // ============================================================

        private static List<MepDuctTextSeed> ReadDuctTextSeeds(
            Transaction tr,
            ObjectId[] ids)
        {
            List<MepDuctTextSeed> result =
                new List<MepDuctTextSeed>();

            foreach (ObjectId id in ids)
            {
                Entity ent =
                    SafeOpenEntity(
                        tr,
                        id);

                if (ent == null)
                    continue;

                if (ent is DBText txt)
                {
                    string raw =
                        txt.TextString ?? "";

                    if (!MepDuctSizeParser.TryParse(
                            raw,
                            txt.Layer,
                            out MepDuctSizeInfo size))
                    {
                        continue;
                    }

                    Point3d pt =
                        txt.Position;

                    try
                    {
                        if (txt.Justify !=
                                AttachmentPoint.BaseLeft &&
                            (Math.Abs(
                                 txt.AlignmentPoint.X) >
                             1e-9 ||
                             Math.Abs(
                                 txt.AlignmentPoint.Y) >
                             1e-9))
                        {
                            pt =
                                txt.AlignmentPoint;
                        }
                    }
                    catch
                    {
                    }

                    result.Add(
                        new MepDuctTextSeed
                        {
                            Id = id,
                            Position = pt,
                            Rotation =
                                txt.Rotation,
                            RawText = raw,
                            Layer =
                                txt.Layer ?? "",
                            Size = size
                        });

                    continue;
                }

                if (ent is MText mtxt)
                {
                    string raw =
                        mtxt.Text ?? "";

                    if (!MepDuctSizeParser.TryParse(
                            raw,
                            mtxt.Layer,
                            out MepDuctSizeInfo size))
                    {
                        continue;
                    }

                    result.Add(
                        new MepDuctTextSeed
                        {
                            Id = id,
                            Position =
                                mtxt.Location,
                            Rotation =
                                mtxt.Rotation,
                            RawText = raw,
                            Layer =
                                mtxt.Layer ?? "",
                            Size = size
                        });
                }
            }

            return result;
        }

        private static List<CurveCandidate> ReadCurveCandidates(
            Transaction tr,
            ObjectId[] ids)
        {
            List<CurveCandidate> result =
                new List<CurveCandidate>();

            foreach (ObjectId id in ids)
            {
                Entity ent =
                    SafeOpenEntity(
                        tr,
                        id);

                if (ent == null ||
                    IsAiDuctOutputLayer(
                        ent.Layer))
                {
                    continue;
                }

                if (!(ent is Curve curve))
                    continue;

                if (!(ent is Line) &&
                    !(ent is Polyline) &&
                    !(ent is Polyline2d) &&
                    !(ent is Polyline3d) &&
                    !(ent is Arc))
                {
                    continue;
                }

                double length =
                    GetCurveLength(
                        curve);

                if (length <
                    MinCurveLength)
                {
                    continue;
                }

                CurveCandidate candidate =
                    new CurveCandidate
                    {
                        Id = id,
                        Curve = curve,
                        Entity = ent,
                        Layer =
                            ent.Layer ?? "",
                        Start =
                            curve.StartPoint,
                        End =
                            curve.EndPoint,
                        Length =
                            length,
                        LayerLooksDuct =
                            MepDuctSizeParser
                                .HasDuctContext(
                                    ent.Layer)
                    };

                if (ent is Polyline pl &&
                    TryGetClosedRectangleCenterline(
                        pl,
                        out Point3d centerStart,
                        out Point3d centerEnd))
                {
                    candidate.ClosedRectangle =
                        true;
                    candidate.RectCenterStart =
                        centerStart;
                    candidate.RectCenterEnd =
                        centerEnd;
                }

                result.Add(candidate);
            }

            return result;
        }

        private static Entity SafeOpenEntity(
            Transaction tr,
            ObjectId id)
        {
            try
            {
                if (id.IsNull ||
                    !id.IsValid ||
                    id.IsErased)
                {
                    return null;
                }

                Entity ent =
                    tr.GetObject(
                        id,
                        OpenMode.ForRead,
                        false) as Entity;

                if (ent == null ||
                    ent.IsErased)
                {
                    return null;
                }

                return ent;
            }
            catch
            {
                return null;
            }
        }

        // ============================================================
        // MAP SIZE TEXT
        // ============================================================

        private static void AttachBestSeeds(
            List<CurveCandidate> curves,
            List<MepDuctTextSeed> seeds,
            ref MepDuctScanResult result)
        {
            if (curves == null ||
                seeds == null ||
                seeds.Count == 0)
            {
                return;
            }

            foreach (CurveCandidate curve
                in curves)
            {
                MepDuctTextSeed best =
                    null;

                double bestScore =
                    double.MaxValue;

                double bestDistance =
                    double.MaxValue;

                double bestAngle =
                    double.MaxValue;

                foreach (MepDuctTextSeed seed
                    in seeds)
                {
                    if (seed?.Size == null)
                        continue;

                    Point3d a =
                        curve.ClosedRectangle
                            ? curve.RectCenterStart
                            : curve.Start;

                    Point3d b =
                        curve.ClosedRectangle
                            ? curve.RectCenterEnd
                            : curve.End;

                    double distance =
                        DistancePointToSegment2D(
                            seed.Position,
                            a,
                            b);

                    double maxDistance =
                        Math.Max(
                            MaxTextAngleDistance(
                                seed.Size),
                            MaxTextAngleDistance(
                                seed.Size) *
                            0.75 +
                            350.0);

                    if (distance >
                        maxDistance)
                    {
                        continue;
                    }

                    double angleDiff =
                        curve.ClosedRectangle
                            ? 0.0
                            : ParallelAngleDifference(
                                PlanAngle(
                                    a,
                                    b),
                                seed.Rotation);

                    if (!curve.ClosedRectangle &&
                        angleDiff >
                            MaxTextAngleRadians)
                    {
                        continue;
                    }

                    double layerBonus =
                        curve.LayerLooksDuct
                            ? 160.0
                            : 0.0;

                    string systemFromLayer =
                        MepDuctSizeParser
                            .InferSystemCode(
                                curve.Layer);

                    double systemBonus =
                        !string.IsNullOrWhiteSpace(
                            systemFromLayer) &&
                        !string.IsNullOrWhiteSpace(
                            seed.Size.SystemCode) &&
                        string.Equals(
                            systemFromLayer,
                            seed.Size.SystemCode,
                            StringComparison.OrdinalIgnoreCase)
                            ? 90.0
                            : 0.0;

                    double score =
                        distance +
                        angleDiff *
                        650.0 -
                        layerBonus -
                        systemBonus;

                    if (score < bestScore)
                    {
                        bestScore =
                            score;
                        best =
                            seed;
                        bestDistance =
                            distance;
                        bestAngle =
                            angleDiff;
                    }
                }

                if (best == null)
                    continue;

                curve.Seed = best;
                curve.SeedDistance =
                    bestDistance;
                curve.SeedAngle =
                    bestAngle;
            }

            // Nếu 1 text gần như hòa cho nhiều tuyến khác hướng/layer,
            // không ép confidence quá cao.
            foreach (MepDuctTextSeed seed
                in seeds)
            {
                List<CurveCandidate> attached =
                    curves
                        .Where(c =>
                            c?.Seed == seed)
                        .ToList();

                if (attached.Count <= 4)
                    continue;

                result.AmbiguousCount +=
                    attached.Count - 4;
            }
        }

        private static double MaxTextAngleDistance(
            MepDuctSizeInfo size)
        {
            if (size == null)
                return MaxTextAngleRadians;

            return
                Math.Max(
                    MaxSeedDistanceBase,
                    size.MaxDimensionMm *
                    1.6 +
                    300.0);
        }

        // ============================================================
        // BUILD SEGMENTS
        // ============================================================

        private static List<MepDuctSegment> BuildSegments(
            List<CurveCandidate> curves,
            ref MepDuctScanResult result)
        {
            List<MepDuctSegment> output =
                new List<MepDuctSegment>();

            if (curves == null ||
                curves.Count == 0)
            {
                return output;
            }

            HashSet<ObjectId> used =
                new HashSet<ObjectId>();

            // 1) Closed rectangular polyline.
            foreach (CurveCandidate curve
                in curves
                    .Where(c =>
                        c != null &&
                        c.ClosedRectangle &&
                        c.Seed?.Size != null))
            {
                MepDuctSegment segment =
                    CreateSegmentFromSeed(
                        curve.RectCenterStart,
                        curve.RectCenterEnd,
                        curve.Seed,
                        curve.Layer,
                        0.96,
                        "RECT_FRAME",
                        new[]
                        {
                            curve.Id
                        },
                        "closed rectangle + WxH");

                if (segment != null)
                {
                    output.Add(segment);
                    used.Add(curve.Id);
                    result.RectFrameCount++;
                }
            }

            // 2) Double-line boundaries.
            List<CurveCandidate> open =
                curves
                    .Where(c =>
                        c != null &&
                        !c.ClosedRectangle &&
                        c.Seed?.Size != null &&
                        !used.Contains(c.Id))
                    .OrderByDescending(c =>
                        c.Length)
                    .ToList();

            for (int i = 0;
                i < open.Count;
                i++)
            {
                CurveCandidate a =
                    open[i];

                if (a == null ||
                    used.Contains(a.Id))
                {
                    continue;
                }

                CurveCandidate best =
                    null;

                double bestPairScore =
                    double.MaxValue;

                for (int j = i + 1;
                    j < open.Count;
                    j++)
                {
                    CurveCandidate b =
                        open[j];

                    if (b == null ||
                        used.Contains(b.Id) ||
                        a.Seed?.Size == null ||
                        b.Seed?.Size == null ||
                        !string.Equals(
                            a.Seed.Size.CanonicalSize,
                            b.Seed.Size.CanonicalSize,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(
                            a.Layer) &&
                        !string.IsNullOrWhiteSpace(
                            b.Layer) &&
                        !string.Equals(
                            a.Layer,
                            b.Layer,
                            StringComparison.OrdinalIgnoreCase) &&
                        !(a.LayerLooksDuct &&
                          b.LayerLooksDuct))
                    {
                        continue;
                    }

                    double angle =
                        ParallelAngleDifference(
                            PlanAngle(
                                a.Start,
                                a.End),
                            PlanAngle(
                                b.Start,
                                b.End));

                    if (angle >
                        DoubleLineAngleRadians)
                    {
                        continue;
                    }

                    double overlap =
                        SegmentOverlapRatio(
                            a.Start,
                            a.End,
                            b.Start,
                            b.End);

                    if (overlap < 0.45)
                        continue;

                    double separation =
                        ParallelSeparation2D(
                            a.Start,
                            a.End,
                            b.Start,
                            b.End);

                    double expected =
                        ExpectedPlanWidth(
                            a.Seed.Size);

                    if (expected <= 1.0)
                        continue;

                    double ratio =
                        Math.Abs(
                            separation -
                            expected) /
                        expected;

                    // Consultant có thể ghi HxW thay cho WxH.
                    // Thử dimension còn lại cho rectangular.
                    if (a.Seed.Size.IsRectangular)
                    {
                        double alt =
                            Math.Min(
                                a.Seed.Size.WidthMm,
                                a.Seed.Size.HeightMm);

                        if (alt > 1.0)
                        {
                            ratio =
                                Math.Min(
                                    ratio,
                                    Math.Abs(
                                        separation -
                                        alt) /
                                    alt);
                        }
                    }

                    if (ratio > 0.38)
                        continue;

                    double score =
                        ratio * 1000.0 +
                        (1.0 - overlap) *
                        250.0 +
                        angle * 600.0;

                    if (score <
                        bestPairScore)
                    {
                        bestPairScore =
                            score;
                        best =
                            b;
                    }
                }

                if (best != null)
                {
                    BuildCenterlineBetweenParallelCurves(
                        a.Start,
                        a.End,
                        best.Start,
                        best.End,
                        out Point3d centerStart,
                        out Point3d centerEnd);

                    MepDuctTextSeed seed =
                        ChooseBetterSeed(
                            a,
                            best);

                    MepDuctSegment segment =
                        CreateSegmentFromSeed(
                            centerStart,
                            centerEnd,
                            seed,
                            PreferLayer(a, best),
                            0.95,
                            "DOUBLE_LINE",
                            new[]
                            {
                                a.Id,
                                best.Id
                            },
                            "2 boundary lines + parallel WxH");

                    if (segment != null)
                    {
                        output.Add(segment);
                        used.Add(a.Id);
                        used.Add(best.Id);
                        result.DoubleLinePairCount++;
                    }

                    continue;
                }

                // 3) Single centerline only if evidence đủ mạnh.
                bool strong =
                    a.LayerLooksDuct ||
                    a.SeedDistance <=
                        Math.Max(
                            300.0,
                            a.Seed.Size.MaxDimensionMm *
                            0.75);

                if (!strong)
                {
                    result.RejectedCount++;
                    continue;
                }

                MepDuctSegment single =
                    CreateSegmentFromSeed(
                        a.Start,
                        a.End,
                        a.Seed,
                        a.Layer,
                        a.LayerLooksDuct
                            ? 0.91
                            : 0.86,
                        "CENTERLINE",
                        new[]
                        {
                            a.Id
                        },
                        a.LayerLooksDuct
                            ? "duct layer + WxH"
                            : "parallel WxH near curve");

                if (single != null)
                {
                    output.Add(single);
                    used.Add(a.Id);
                    result.SingleCenterlineCount++;
                }
            }

            return
                DeduplicateSegments(
                    output);
        }

        private static MepDuctSegment CreateSegmentFromSeed(
            Point3d start,
            Point3d end,
            MepDuctTextSeed seed,
            string layer,
            double confidence,
            string representation,
            IEnumerable<ObjectId> sourceIds,
            string evidence)
        {
            if (seed?.Size == null)
                return null;

            double length =
                start.DistanceTo(end);

            if (length <
                MinCurveLength)
            {
                return null;
            }

            string system =
                !string.IsNullOrWhiteSpace(
                    seed.Size.SystemCode)
                    ? seed.Size.SystemCode
                    : MepDuctSizeParser
                        .InferSystemCode(
                            layer);

            string fireRating =
                !string.IsNullOrWhiteSpace(
                    seed.Size.FireRating)
                    ? seed.Size.FireRating
                    : MepDuctSizeParser
                        .ParseFireRating(
                            layer);

            return
                new MepDuctSegment
                {
                    Start = start,
                    End = end,
                    LengthMm = length,
                    Layer = layer ?? "",
                    SystemCode =
                        system ?? "",
                    FireRating =
                        fireRating ?? "",
                    Size =
                        seed.Size
                            .CanonicalSize,
                    Shape =
                        seed.Size.Shape,
                    WidthMm =
                        seed.Size.WidthMm,
                    HeightMm =
                        seed.Size.HeightMm,
                    DiameterMm =
                        seed.Size.DiameterMm,
                    Confidence =
                        Math.Max(
                            0.0,
                            Math.Min(
                                1.0,
                                confidence)),
                    Evidence =
                        evidence ?? "",
                    Representation =
                        representation ?? "",
                    SourceIds =
                        (sourceIds ??
                         Enumerable.Empty<ObjectId>())
                            .Where(x =>
                                !x.IsNull)
                            .Distinct()
                            .ToList()
                };
        }

        private static MepDuctTextSeed ChooseBetterSeed(
            CurveCandidate a,
            CurveCandidate b)
        {
            if (a?.Seed == null)
                return b?.Seed;

            if (b?.Seed == null)
                return a.Seed;

            return
                a.SeedDistance <=
                    b.SeedDistance
                    ? a.Seed
                    : b.Seed;
        }

        private static string PreferLayer(
            CurveCandidate a,
            CurveCandidate b)
        {
            if (a != null &&
                a.LayerLooksDuct)
            {
                return a.Layer ?? "";
            }

            if (b != null &&
                b.LayerLooksDuct)
            {
                return b.Layer ?? "";
            }

            return
                a?.Layer ??
                b?.Layer ??
                "";
        }

        // Propagate chỉ trong component cùng system/layer khi mọi seed đã biết
        // trong component cùng một size. Không lan qua reducer mơ hồ.
        private static void PropagateUnambiguousSize(
            List<MepDuctSegment> segments)
        {
            // STEP D1 hiện các segment output đã có seed rõ.
            // Method giữ chỗ cho D3 Graph Duct; cố tình không đoán thêm ở đây.
        }

        private static List<MepDuctSegment> DeduplicateSegments(
            List<MepDuctSegment> segments)
        {
            List<MepDuctSegment> output =
                new List<MepDuctSegment>();

            foreach (MepDuctSegment candidate
                in segments
                    .Where(x =>
                        x != null)
                    .OrderByDescending(x =>
                        x.Confidence)
                    .ThenByDescending(x =>
                        x.LengthMm))
            {
                bool duplicate =
                    output.Any(existing =>
                        string.Equals(
                            existing.Size,
                            candidate.Size,
                            StringComparison.OrdinalIgnoreCase) &&
                        SegmentCenterDistance(
                            existing,
                            candidate) <=
                            80.0 &&
                        ParallelAngleDifference(
                            PlanAngle(
                                existing.Start,
                                existing.End),
                            PlanAngle(
                                candidate.Start,
                                candidate.End)) <=
                            Math.PI / 36.0);

                if (!duplicate)
                    output.Add(candidate);
            }

            return output;
        }

        private static List<MepDuctTakeoffRow> BuildStats(
            List<MepDuctSegment> segments)
        {
            if (segments == null)
                return new List<MepDuctTakeoffRow>();

            return
                segments
                    .Where(x =>
                        x != null &&
                        !string.IsNullOrWhiteSpace(
                            x.Size))
                    .GroupBy(
                        x =>
                            new
                            {
                                System =
                                    x.SystemCode ?? "",
                                Size =
                                    x.Size ?? "",
                                Shape =
                                    x.Shape ?? "",
                                Fire =
                                    x.FireRating ?? ""
                            })
                    .Select(g =>
                        new MepDuctTakeoffRow
                        {
                            SystemCode =
                                g.Key.System,
                            Size =
                                g.Key.Size,
                            Shape =
                                g.Key.Shape,
                            FireRating =
                                g.Key.Fire,
                            SegmentCount =
                                g.Count(),
                            LengthMm =
                                g.Sum(x =>
                                    x.LengthMm),
                            SurfaceAreaM2 =
                                g.Sum(x =>
                                    x.SurfaceAreaM2),
                            AverageConfidence =
                                g.Select(x =>
                                        x.Confidence)
                                    .DefaultIfEmpty(0.0)
                                    .Average()
                        })
                    .OrderBy(x =>
                        x.SystemCode)
                    .ThenByDescending(x =>
                        ParseSortDimension(
                            x.Size))
                    .ThenBy(x =>
                        x.FireRating)
                    .ToList();
        }

        // ============================================================
        // DRAW OVERLAY
        // ============================================================

        private static void DrawOverlay(
            Transaction tr,
            Database db,
            BlockTableRecord space,
            MepDuctScanResult result)
        {
            if (result?.Segments == null)
                return;

            foreach (MepDuctSegment segment
                in result.Segments)
            {
                if (segment == null)
                    continue;

                string layerName =
                    BuildOverlayLayerName(
                        segment);

                EnsureLayer(
                    tr,
                    db,
                    layerName,
                    GetLayerColor(
                        layerName));

                Line line =
                    new Line(
                        segment.Start,
                        segment.End);

                line.SetDatabaseDefaults(db);
                line.Layer =
                    layerName;
                line.ColorIndex = 256;

                space.AppendEntity(line);
                tr.AddNewlyCreatedDBObject(
                    line,
                    true);

                if (segment.LengthMm >= 1800.0)
                {
                    DBText label =
                        new DBText();

                    label.SetDatabaseDefaults(db);
                    label.TextStyleId =
                        db.Textstyle;
                    label.TextString =
                        BuildOverlayLabel(
                            segment);
                    label.Height = 95.0;
                    label.Layer =
                        layerName;
                    label.ColorIndex = 256;
                    label.Justify =
                        AttachmentPoint.MiddleCenter;
                    label.AlignmentPoint =
                        segment.Center;
                    label.Position =
                        segment.Center;
                    label.Rotation =
                        PlanAngle(
                            segment.Start,
                            segment.End);

                    space.AppendEntity(label);
                    tr.AddNewlyCreatedDBObject(
                        label,
                        true);
                }
            }
        }

        private static void DeleteOldOverlay(
            Transaction tr,
            BlockTableRecord space)
        {
            foreach (ObjectId id
                in space)
            {
                Entity ent =
                    null;

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

                if (ent == null ||
                    ent.IsErased ||
                    !IsAiDuctOutputLayer(
                        ent.Layer))
                {
                    continue;
                }

                try
                {
                    ent.UpgradeOpen();
                    ent.Erase();
                }
                catch
                {
                }
            }
        }

        private static bool IsAiDuctOutputLayer(
            string layer)
        {
            string value =
                (layer ?? "")
                    .Trim();

            return
                value.StartsWith(
                    OverlayPrefix,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    value,
                    CheckLayer,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildOverlayLayerName(
            MepDuctSegment segment)
        {
            string system =
                string.IsNullOrWhiteSpace(
                    segment?.SystemCode)
                    ? "DUCT"
                    : segment.SystemCode;

            string size =
                segment?.Size ?? "";

            string fire =
                segment?.FireRating ?? "";

            return
                SanitizeLayerName(
                    OverlayPrefix +
                    system +
                    "_" +
                    size +
                    (string.IsNullOrWhiteSpace(
                        fire)
                        ? ""
                        : "_" +
                          fire));
        }

        private static string BuildOverlayLabel(
            MepDuctSegment segment)
        {
            string text =
                (string.IsNullOrWhiteSpace(
                    segment?.SystemCode)
                    ? "DUCT"
                    : segment.SystemCode) +
                " " +
                (segment?.Size ?? "");

            if (!string.IsNullOrWhiteSpace(
                    segment?.FireRating))
            {
                text +=
                    " " +
                    segment.FireRating;
            }

            return text;
        }

        private static string SanitizeLayerName(
            string value)
        {
            string s =
                (value ?? "")
                    .Trim()
                    .Replace("×", "x")
                    .Replace("Ø", "DIA")
                    .Replace("Φ", "DIA");

            char[] invalid =
            {
                '<', '>', '/', '\\',
                '"', ':', ';', '?',
                '*', '|', '=', '`'
            };

            foreach (char c in invalid)
            {
                s =
                    s.Replace(
                        c,
                        '_');
            }

            while (s.Contains("__"))
                s = s.Replace("__", "_");

            return
                s.Length <= 180
                    ? s
                    : s.Substring(
                        0,
                        180);
        }

        private static void EnsureLayer(
            Transaction tr,
            Database db,
            string layerName,
            short aci)
        {
            LayerTable table =
                tr.GetObject(
                    db.LayerTableId,
                    OpenMode.ForRead)
                as LayerTable;

            if (table == null)
                return;

            if (table.Has(layerName))
                return;

            table.UpgradeOpen();

            LayerTableRecord record =
                new LayerTableRecord
                {
                    Name = layerName,
                    Color =
                        Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                            Autodesk.AutoCAD.Colors.ColorMethod.ByAci,
                            aci)
                };

            table.Add(record);

            tr.AddNewlyCreatedDBObject(
                record,
                true);
        }

        private static short GetLayerColor(
            string layerName)
        {
            unchecked
            {
                int hash = 17;

                foreach (char c
                    in layerName ?? "")
                {
                    hash =
                        hash * 31 +
                        c;
                }

                short[] colors =
                {
                    1, 2, 3, 4, 5, 6,
                    30, 40, 50, 80, 90,
                    110, 120, 140, 170,
                    200, 210, 220
                };

                int index =
                    Math.Abs(hash) %
                    colors.Length;

                return
                    colors[index];
            }
        }

        // ============================================================
        // RECT / DOUBLE LINE GEOMETRY
        // ============================================================

        private static bool TryGetClosedRectangleCenterline(
            Polyline pl,
            out Point3d start,
            out Point3d end)
        {
            start =
                Point3d.Origin;

            end =
                Point3d.Origin;

            if (pl == null ||
                !pl.Closed ||
                pl.NumberOfVertices != 4)
            {
                return false;
            }

            List<Point3d> points =
                new List<Point3d>();

            for (int i = 0;
                i < 4;
                i++)
            {
                points.Add(
                    pl.GetPoint3dAt(i));
            }

            List<(Point3d A, Point3d B, double Length, Point3d Mid)> edges =
                new List<(Point3d, Point3d, double, Point3d)>();

            for (int i = 0;
                i < 4;
                i++)
            {
                Point3d a =
                    points[i];

                Point3d b =
                    points[
                        (i + 1) % 4];

                edges.Add(
                    (a,
                     b,
                     a.DistanceTo(b),
                     MidPoint(a, b)));
            }

            double max =
                edges.Max(x =>
                    x.Length);

            double min =
                edges.Min(x =>
                    x.Length);

            if (max < 100.0 ||
                min < 40.0 ||
                max / Math.Max(
                    1.0,
                    min) < 1.15)
            {
                return false;
            }

            // Hai cạnh ngắn là đầu/cuối duct.
            List<(Point3d A, Point3d B, double Length, Point3d Mid)> shortEdges =
                edges
                    .OrderBy(x =>
                        x.Length)
                    .Take(2)
                    .ToList();

            if (shortEdges.Count != 2)
                return false;

            double parallel =
                ParallelAngleDifference(
                    PlanAngle(
                        shortEdges[0].A,
                        shortEdges[0].B),
                    PlanAngle(
                        shortEdges[1].A,
                        shortEdges[1].B));

            if (parallel >
                Math.PI / 18.0)
            {
                return false;
            }

            start =
                shortEdges[0].Mid;

            end =
                shortEdges[1].Mid;

            return
                start.DistanceTo(end) >=
                MinCurveLength;
        }

        private static void BuildCenterlineBetweenParallelCurves(
            Point3d aStart,
            Point3d aEnd,
            Point3d bStart,
            Point3d bEnd,
            out Point3d centerStart,
            out Point3d centerEnd)
        {
            double direct =
                aStart.DistanceTo(
                    bStart) +
                aEnd.DistanceTo(
                    bEnd);

            double reverse =
                aStart.DistanceTo(
                    bEnd) +
                aEnd.DistanceTo(
                    bStart);

            if (reverse < direct)
            {
                Point3d tmp =
                    bStart;

                bStart =
                    bEnd;

                bEnd =
                    tmp;
            }

            centerStart =
                MidPoint(
                    aStart,
                    bStart);

            centerEnd =
                MidPoint(
                    aEnd,
                    bEnd);
        }

        private static double SegmentOverlapRatio(
            Point3d a1,
            Point3d a2,
            Point3d b1,
            Point3d b2)
        {
            Vector2d dir =
                new Vector2d(
                    a2.X - a1.X,
                    a2.Y - a1.Y);

            if (dir.Length < 1e-6)
                return 0.0;

            dir =
                dir.GetNormal();

            double a0 = 0.0;

            double aEnd =
                new Vector2d(
                    a2.X - a1.X,
                    a2.Y - a1.Y)
                    .DotProduct(dir);

            double bStart =
                new Vector2d(
                    b1.X - a1.X,
                    b1.Y - a1.Y)
                    .DotProduct(dir);

            double bEnd =
                new Vector2d(
                    b2.X - a1.X,
                    b2.Y - a1.Y)
                    .DotProduct(dir);

            double aMin =
                Math.Min(
                    a0,
                    aEnd);

            double aMax =
                Math.Max(
                    a0,
                    aEnd);

            double bMin =
                Math.Min(
                    bStart,
                    bEnd);

            double bMax =
                Math.Max(
                    bStart,
                    bEnd);

            double overlap =
                Math.Max(
                    0.0,
                    Math.Min(
                        aMax,
                        bMax) -
                    Math.Max(
                        aMin,
                        bMin));

            double denom =
                Math.Max(
                    1.0,
                    Math.Min(
                        aMax - aMin,
                        bMax - bMin));

            return
                Math.Max(
                    0.0,
                    Math.Min(
                        1.0,
                        overlap /
                        denom));
        }

        private static double ParallelSeparation2D(
            Point3d a1,
            Point3d a2,
            Point3d b1,
            Point3d b2)
        {
            Point3d midB =
                MidPoint(
                    b1,
                    b2);

            return
                DistancePointToSegment2D(
                    midB,
                    a1,
                    a2);
        }

        private static double ExpectedPlanWidth(
            MepDuctSizeInfo size)
        {
            if (size == null)
                return 0.0;

            if (size.IsRectangular)
            {
                // Chuẩn HVAC thường ghi Width x Height.
                return size.WidthMm;
            }

            if (size.IsRound)
                return size.DiameterMm;

            return 0.0;
        }

        // ============================================================
        // MATH
        // ============================================================

        private static Point3d MidPoint(
            Point3d a,
            Point3d b)
        {
            return
                new Point3d(
                    (a.X + b.X) * 0.5,
                    (a.Y + b.Y) * 0.5,
                    (a.Z + b.Z) * 0.5);
        }

        private static double PlanAngle(
            Point3d a,
            Point3d b)
        {
            double dx =
                b.X - a.X;

            double dy =
                b.Y - a.Y;

            if (Math.Sqrt(
                    dx * dx +
                    dy * dy) <
                1e-9)
            {
                return 0.0;
            }

            return
                Math.Atan2(
                    dy,
                    dx);
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
            double value)
        {
            while (value < 0.0)
                value += Math.PI * 2.0;

            while (value >=
                Math.PI * 2.0)
            {
                value -=
                    Math.PI * 2.0;
            }

            return value;
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
            {
                return
                    PlanDistance(
                        p,
                        a);
            }

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

            return
                PlanDistance(
                    p,
                    q);
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

        private static double SegmentCenterDistance(
            MepDuctSegment a,
            MepDuctSegment b)
        {
            if (a == null ||
                b == null)
            {
                return
                    double.MaxValue;
            }

            return
                PlanDistance(
                    a.Center,
                    b.Center);
        }

        private static double GetCurveLength(
            Curve curve)
        {
            if (curve == null)
                return 0.0;

            try
            {
                if (curve is Line line)
                    return line.Length;

                if (curve is Polyline pl)
                    return pl.Length;

                if (curve is Arc arc)
                    return arc.Length;

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
                 ex.MaxPoint.X) *
                0.5;

            double cy =
                (ex.MinPoint.Y +
                 ex.MaxPoint.Y) *
                0.5;

            double cz =
                (ex.MinPoint.Z +
                 ex.MaxPoint.Z) *
                0.5;

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

        private static double ParseSortDimension(
            string size)
        {
            if (!MepDuctSizeParser.TryParse(
                    size,
                    "DUCT",
                    out MepDuctSizeInfo info))
            {
                return 0.0;
            }

            return
                info.MaxDimensionMm;
        }
    }
}