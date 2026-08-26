#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
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

    public enum MepDuctFittingType
    {
        Unknown = 0,
        Elbow = 1,       // Co / Cút (90° hoặc 45°)
        Tee = 2,         // Tê rẽ nhánh 90°
        Reducer = 3,     // Giảm / Côn thu
        ShoeTap = 4,     // Gót giày / Trích nhánh xiên (30°-65°)
        Cross = 5,       // Thập (4 ngả)
        EndCap = 6       // Bịt đầu / Cửa gió cuối
    }

    public sealed class MepDuctFitting
    {
        public int Id { get; set; }
        public MepDuctFittingType Type { get; set; } = MepDuctFittingType.Unknown;
        public Point3d Position { get; set; } = Point3d.Origin; // Điểm tâm giao 50/50
        public List<int> ConnectedSegmentIds { get; set; } = new List<int>();
        public int? MainSegmentId { get; set; }
        public int? BranchSegmentId { get; set; }
        public double AngleDegrees { get; set; }
        public string SizeIn { get; set; } = "";
        public string SizeOut { get; set; } = "";
        public string SizeBranch { get; set; } = "";
        public string Description { get; set; } = "";
        public Point3d ReducerStart { get; set; } = Point3d.Origin;
        public Point3d ReducerEnd { get; set; } = Point3d.Origin;
        public bool HasBranchEdgePosition { get; set; }
        public Point3d BranchEdgePosition { get; set; } = Point3d.Origin;
        public double WideWidth { get; set; }
        public double NarrowWidth { get; set; }
    }

    public sealed class MepDuctSegment
    {
        public int Id { get; set; }
        public Point3d Start { get; set; } = Point3d.Origin;
        public Point3d End { get; set; } = Point3d.Origin;
        public Point3d OriginalStart { get; set; } = Point3d.Origin;
        public Point3d OriginalEnd { get; set; } = Point3d.Origin;
        public double LengthMm { get; set; }
        public string Layer { get; set; } = "";
        public string SystemCode { get; set; } = "";
        public string FireRating { get; set; } = "";
        public string Size { get; set; } = "";
        public string Shape { get; set; } = "";
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }
        public double DiameterMm { get; set; }
        public double MeasuredPlanWidth { get; set; } // Chiều rộng đo trực tiếp từ 2 nét vẽ CAD (mm)
        public double Confidence { get; set; }
        public bool HasExplicitSize { get; set; }
        public string Evidence { get; set; } = "";
        public string Representation { get; set; } = "";
        public List<ObjectId> SourceIds { get; set; } = new List<ObjectId>();

        public double MaxDimensionMm
        {
            get
            {
                if (string.Equals(Shape, "ROUND", StringComparison.OrdinalIgnoreCase))
                    return DiameterMm > 0 ? DiameterMm : MeasuredPlanWidth;

                double max = Math.Max(WidthMm, HeightMm);
                if (max <= 0.0 && MeasuredPlanWidth > 0.0)
                    max = MeasuredPlanWidth;

                return max > 0.0 ? max : 200.0;
            }
        }

        public Point3d Center =>
            new Point3d(
                (Start.X + End.X) * 0.5,
                (Start.Y + End.Y) * 0.5,
                (Start.Z + End.Z) * 0.5);

        public double SurfaceAreaM2
        {
            get
            {
                double lengthM = Math.Max(0.0, LengthMm) / 1000.0;
                if (string.Equals(Shape, "RECT", StringComparison.OrdinalIgnoreCase))
                {
                    double w = WidthMm > 0 ? WidthMm : MeasuredPlanWidth;
                    double h = HeightMm > 0 ? HeightMm : (w * 0.5);
                    double perimeterM = 2.0 * (w + h) / 1000.0;
                    return perimeterM * lengthM;
                }
                if (string.Equals(Shape, "ROUND", StringComparison.OrdinalIgnoreCase))
                {
                    double d = DiameterMm > 0 ? DiameterMm : MeasuredPlanWidth;
                    double perimeterM = Math.PI * d / 1000.0;
                    return perimeterM * lengthM;
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

        public double LengthMeters => LengthMm / 1000.0;
    }

    public sealed class MepDuctScanResult
    {
        public List<MepDuctSegment> Segments { get; set; } = new List<MepDuctSegment>();
        public List<MepDuctTakeoffRow> Stats { get; set; } = new List<MepDuctTakeoffRow>();
        public List<MepDuctFitting> Fittings { get; set; } = new List<MepDuctFitting>();

        public int ElbowCount => Fittings?.Count(f => f.Type == MepDuctFittingType.Elbow) ?? 0;
        public int TeeCount => Fittings?.Count(f => f.Type == MepDuctFittingType.Tee) ?? 0;
        public int ReducerCount => Fittings?.Count(f => f.Type == MepDuctFittingType.Reducer) ?? 0;
        public int ShoeTapCount => Fittings?.Count(f => f.Type == MepDuctFittingType.ShoeTap) ?? 0;
        public int CrossCount => Fittings?.Count(f => f.Type == MepDuctFittingType.Cross) ?? 0;

        public int RawCurveCount { get; set; }
        public int DuctSizeTextCount { get; set; }
        public int RectFrameCount { get; set; }
        public int DoubleLinePairCount { get; set; }
        public int SingleCenterlineCount { get; set; }
        public int InheritedSizeCount { get; set; }
        public int RejectedCount { get; set; }
        public int AmbiguousCount { get; set; }
        public int ShaftExcludedCount { get; set; }
        public int OutputSegmentCount => Segments?.Count ?? 0;

        public double TotalLengthMm =>
            Segments?.Sum(x => Math.Max(0.0, x?.LengthMm ?? 0.0)) ?? 0.0;

        public double TotalAreaM2 =>
            Segments?.Sum(x => Math.Max(0.0, x?.SurfaceAreaM2 ?? 0.0)) ?? 0.0;

        public double AverageConfidence =>
            Segments == null || Segments.Count == 0
                ? 0.0
                : Segments.Select(x => x?.Confidence ?? 0.0).DefaultIfEmpty(0.0).Average();
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
    /// Engine AI Nhận Diện Ống Gió & Phụ Kiện HVAC Chuyên Nghiệp:
    /// - Nhận diện hình học 2D chính xác từ 2 nét song song (Double-line), đơn tuyến (Single-line), khung chữ nhật.
    /// - Ràng buộc kích thước theo bề rộng đo thực tế từ bản vẽ (Measured Plan Width) -> Tránh nhận nhầm size.
    /// - Nhận diện toàn diện phụ kiện: Co (Elbow), Tê (Tee), Giảm (Reducer), Gót giày (Shoe tap).
    /// - Lan truyền kích thước thông minh qua đồ thị tôpô cho các đoạn không ghi text kích thước.
    /// - Co gặp đúng đỉnh; nhánh Tê/Gót giày chỉ chạm mép ngoài ống chính.
    /// - Vẽ Polyline với ConstantWidth = Max(W, H) và Độ Trong Suốt 60%.
    /// - Vẽ các ký hiệu/nhãn Phụ Kiện rõ ràng trên layer riêng TDL_AI_DUCT_PHUKIEN.
    /// </summary>
    public sealed class MepDuctSemanticEngine
    {
        public const string OverlayPrefix = "TDL_AI_DUCT_";
        public const string FittingLayerName = "TDL_AI_DUCT_PHUKIEN";
        public const string CheckLayer = "TDL_AI_DUCT_CHECK";

        // Độ trong suốt 60% (Alpha = 255 * (100 - 60) / 100 = 102)
        public const byte DuctTransparencyAlpha = 102;

        private const double MaxTextAngleRadians = Math.PI / 7.2; // 25 deg
        private const double DoubleLineAngleRadians = Math.PI / 25.0; // ~7.2 deg
        private const double MinCurveLength = 50.0;
        private const double MaxTopologyReachMm = 1200.0;
        private const double MaxElbowReachMm = 2400.0;
        private const double MinTrustedConfidence = 0.68;

        private sealed class CurveCandidate
        {
            public int CandidateId { get; set; }
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
            public double RectWidth { get; set; }
            public MepDuctTextSeed Seed { get; set; }
            public double SeedDistance { get; set; } = double.MaxValue;
            public double SeedAngle { get; set; } = double.MaxValue;
            public double SeedScore { get; set; } = double.MaxValue;
            public bool LayerLooksDuct { get; set; }
        }

        private sealed class ShaftMarker
        {
            public Point3d Position { get; set; } = Point3d.Origin;
        }

        private sealed class ShaftExclusionZone
        {
            public double MinX { get; set; }
            public double MinY { get; set; }
            public double MaxX { get; set; }
            public double MaxY { get; set; }

            public double Width => Math.Max(0.0, MaxX - MinX);
            public double Height => Math.Max(0.0, MaxY - MinY);
            public double MaxSide => Math.Max(Width, Height);

            public Point3d Center =>
                new Point3d(
                    (MinX + MaxX) * 0.5,
                    (MinY + MaxY) * 0.5,
                    0.0);

            public bool Contains2D(
                Point3d point,
                double tolerance = 0.0)
            {
                return point.X >= MinX - tolerance &&
                       point.X <= MaxX + tolerance &&
                       point.Y >= MinY - tolerance &&
                       point.Y <= MaxY + tolerance;
            }
        }

        private sealed class OverlayPath
        {
            public MepDuctSegment Template { get; set; }
            public double Width { get; set; }
            public bool Closed { get; set; }
            public List<Point3d> Points { get; set; } = new List<Point3d>();
        }

        private sealed class OverlayVertex
        {
            public Point3d Point { get; set; } = Point3d.Origin;
            // Bulge của đoạn từ vertex hiện tại tới vertex kế tiếp.
            public double Bulge { get; set; }
        }

        private sealed class OverlayGraphNode
        {
            public Point3d Position { get; set; } = Point3d.Origin;
            public int SampleCount { get; set; }
            public List<int> EdgeIds { get; set; } = new List<int>();
        }

        private sealed class OverlayGraphEdge
        {
            public MepDuctSegment Segment { get; set; }
            public int NodeA { get; set; }
            public int NodeB { get; set; }
            public bool Used { get; set; }
        }

        public MepDuctScanResult AnalyzeAndDraw(
            Document doc,
            IEnumerable<ObjectId> selectedIds,
            bool drawOverlay = true,
            bool deleteOldOverlay = true)
        {
            MepDuctScanResult result = new MepDuctScanResult();

            if (doc == null || selectedIds == null)
                return result;

            ObjectId[] ids = selectedIds
                .Where(x => !x.IsNull && x.IsValid && !x.IsErased)
                .Distinct()
                .ToArray();

            if (ids.Length == 0)
                return result;

            Database db = doc.Database;

            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord space = tr.GetObject(
                    db.CurrentSpaceId,
                    drawOverlay ? OpenMode.ForWrite : OpenMode.ForRead) as BlockTableRecord;

                // 0) Tìm vùng ký hiệu trục/shaft để không tô ống gió vào
                // phần gạch chéo của ô trục.
                List<ShaftExclusionZone> shaftZones =
                    ReadShaftExclusionZones(tr, ids);

                // 1) Đọc text kích thước WxH, ØD, EI, Hệ thống
                List<MepDuctTextSeed> seeds = ReadDuctTextSeeds(tr, ids);
                result.DuctSizeTextCount = seeds.Count;

                // 2) Đọc tất cả đối tượng đường nét CAD
                List<CurveCandidate> curves =
                    ReadCurveCandidates(tr, ids, shaftZones);
                result.RawCurveCount = curves.Count;

                // 3) Gán seed cho các đường phù hợp (kết hợp khoảng cách, góc và bề rộng thực tế)
                AttachBestSeeds(curves, seeds, ref result);

                // 4) Xây dựng các đoạn tim tuyến ban đầu (kể cả đoạn chưa có text)
                List<MepDuctSegment> segments = BuildInitialSegments(curves, ref result);

                // Cắt tuyến tại mép ô trục và loại phần nằm trong vùng hatch.
                segments = ExcludeShaftZones(
                    segments,
                    shaftZones,
                    ref result);

                // Loại trùng trước khi dựng topology để fitting không tham chiếu
                // các segment ảo/trùng ID.
                segments = DeduplicateSegments(segments);
                ReindexSegments(segments);

                // 5) Xây dựng Đồ Thị Tôpô & Nhận diện Phụ kiện (Co, Tê, Giảm, Gót Giày)
                List<MepDuctFitting> fittings = DetectFittingsAndBuildTopology(segments);

                // 6) Lan truyền kích thước cho các đoạn CHƯA CÓ TEXT qua đồ thị
                PropagateSizesThroughTopology(segments, fittings, seeds);

                // 7) Chỉnh điểm nối: co gặp đỉnh; Tê/Gót giày dừng tại mép ống chính.
                ApplyFitting5050Adjustment(segments, fittings);

                // 8) Chỉ giữ các đoạn có bằng chứng đủ mạnh. Đây là gate chặn
                // nét thiết bị/phụ kiện bị biến thành mảng tím lớn.
                int beforeTrustGate = segments.Count;
                segments = segments
                    .Where(IsTrustedDuctSegment)
                    .ToList();

                segments = DeduplicateSegments(segments);
                ReindexSegments(segments);

                result.RejectedCount += Math.Max(0, beforeTrustGate - segments.Count);
                result.InheritedSizeCount = segments.Count(x =>
                    x != null &&
                    (string.Equals(x.Representation, "INHERITED", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(x.Representation, "INFERRED_FROM_CAD_WIDTH", StringComparison.OrdinalIgnoreCase)));

                // Dựng lại fitting từ danh sách cuối để ConnectedSegmentIds
                // luôn khớp sau bước lọc/deduplicate/reindex.
                result.Fittings = DetectFittingsAndBuildTopology(segments);

                // Topology cuối có thêm các segment vừa được truyền size.
                // Áp dụng lại để co 90 có khoảng hở/bo cong vẫn gặp đúng đỉnh,
                // còn nhánh tee/gót giày chỉ chạm MÉP ống chính.
                ApplyFitting5050Adjustment(segments, result.Fittings);
                ConnectCollinearSameSizeRuns(segments);

                result.Fittings = DetectFittingsAndBuildTopology(segments);

                result.Segments = segments;
                result.Stats = BuildStats(segments);

                // 9) Vẽ Overlay Polyline với ConstantWidth = Max(W, H) và Độ Trong Suốt 60%
                if (drawOverlay && space != null)
                {
                    if (deleteOldOverlay)
                    {
                        DeleteOldOverlay(tr, space);
                    }

                    DrawOverlay(tr, db, space, result);
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
            MepDuctNearestSizeResult result = new MepDuctNearestSizeResult();

            if (scan?.Segments == null || scan.Segments.Count == 0)
                return result;

            List<Point3d> refs = BuildReferencePoints(point, deviceExtents);
            List<(MepDuctSegment Segment, double Score, double Distance)> ranked =
                new List<(MepDuctSegment, double, double)>();

            foreach (MepDuctSegment segment in scan.Segments)
            {
                if (segment == null || string.IsNullOrWhiteSpace(segment.Size))
                    continue;

                double d = refs.Min(p => DistancePointToSegment2D(p, segment.Start, segment.End));
                if (d > maxDistance)
                    continue;

                double score = d - Math.Max(0.0, Math.Min(1.0, segment.Confidence)) * 260.0;
                ranked.Add((segment, score, d));
            }

            if (ranked.Count == 0)
                return result;

            var ordered = ranked.OrderBy(x => x.Score).ThenBy(x => x.Distance).ToList();
            var best = ordered[0];

            result.Found = true;
            result.Size = best.Segment.Size;
            result.SystemCode = best.Segment.SystemCode;
            result.FireRating = best.Segment.FireRating;
            result.Distance = best.Distance;
            result.Confidence = Math.Max(0.55, Math.Min(0.98, best.Segment.Confidence - Math.Min(0.20, best.Distance / Math.Max(1.0, maxDistance) * 0.20)));

            if (ordered.Count > 1)
            {
                var second = ordered[1];
                bool different = !string.Equals(best.Segment.Size, second.Segment.Size, StringComparison.OrdinalIgnoreCase);
                if (different && second.Score - best.Score < 120.0)
                {
                    result.Ambiguous = true;
                    result.Confidence = Math.Max(0.50, result.Confidence - 0.18);
                }
            }

            result.Evidence = "DUCT nearest: " + result.Size + " | d=" + result.Distance.ToString("0", CultureInfo.InvariantCulture) + "mm";
            return result;
        }

        public static string BuildCompactSummary(
            MepDuctScanResult run,
            int maxRows = 12)
        {
            if (run == null || run.OutputSegmentCount <= 0)
            {
                return "ỐNG GIÓ: chưa có tuyến đủ bằng chứng.";
            }

            List<string> lines = new List<string>
            {
                "ỐNG GIÓ AI (TỰ ĐỘNG NHẬN DIỆN & TÔPÔ)",
                "• Tuyến ống: " + run.OutputSegmentCount +
                " đoạn (Có text: " + (run.OutputSegmentCount - run.InheritedSizeCount) +
                " | Lan truyền: " + run.InheritedSizeCount + ")",
                "• Phụ kiện: Co=" + run.ElbowCount +
                " | Tê=" + run.TeeCount +
                " | Giảm=" + run.ReducerCount +
                " | Gót giày=" + run.ShoeTapCount +
                (run.CrossCount > 0 ? " | Thập=" + run.CrossCount : ""),
                "• Tổng chiều dài: " + (run.TotalLengthMm / 1000.0).ToString("0.00", CultureInfo.InvariantCulture) + " m" +
                " | Diện tích tôn: " + run.TotalAreaM2.ToString("0.00", CultureInfo.InvariantCulture) + " m²" +
                (run.ShaftExcludedCount > 0
                    ? " | Loại khỏi trục: " + run.ShaftExcludedCount
                    : "")
            };

            foreach (MepDuctTakeoffRow row in (run.Stats ?? new List<MepDuctTakeoffRow>()).Take(Math.Max(1, maxRows)))
            {
                lines.Add(
                    (string.IsNullOrWhiteSpace(row.SystemCode) ? "DUCT" : row.SystemCode) +
                    " | " + row.Size +
                    (string.IsNullOrWhiteSpace(row.FireRating) ? "" : " " + row.FireRating) +
                    " | " + row.LengthMeters.ToString("0.00", CultureInfo.InvariantCulture) + " m" +
                    " | " + row.SurfaceAreaM2.ToString("0.00", CultureInfo.InvariantCulture) + " m²");
            }

            if (run.Stats != null && run.Stats.Count > maxRows)
            {
                lines.Add("... +" + (run.Stats.Count - maxRows) + " dòng khác");
            }

            return string.Join(Environment.NewLine, lines);
        }

        // ============================================================
        // 1. ĐỌC DỮ LIỆU ĐỐI TƯỢNG (TEXT + HÌNH HỌC)
        // ============================================================

        private static List<MepDuctTextSeed> ReadDuctTextSeeds(
            Transaction tr,
            ObjectId[] ids)
        {
            List<MepDuctTextSeed> result = new List<MepDuctTextSeed>();

            foreach (ObjectId id in ids)
            {
                Entity ent = SafeOpenEntity(tr, id);
                if (ent == null)
                    continue;

                if (ent is DBText txt)
                {
                    string raw = txt.TextString ?? "";
                    if (!MepDuctSizeParser.TryParse(raw, txt.Layer, out MepDuctSizeInfo size))
                        continue;

                    Point3d pt = txt.Position;
                    try
                    {
                        if (txt.Justify != AttachmentPoint.BaseLeft &&
                            (Math.Abs(txt.AlignmentPoint.X) > 1e-9 || Math.Abs(txt.AlignmentPoint.Y) > 1e-9))
                        {
                            pt = txt.AlignmentPoint;
                        }
                    }
                    catch
                    {
                    }

                    result.Add(new MepDuctTextSeed
                    {
                        Id = id,
                        Position = pt,
                        Rotation = txt.Rotation,
                        RawText = raw,
                        Layer = txt.Layer ?? "",
                        Size = size
                    });

                    continue;
                }

                if (ent is MText mtxt)
                {
                    string raw = mtxt.Text ?? "";
                    if (!MepDuctSizeParser.TryParse(raw, mtxt.Layer, out MepDuctSizeInfo size))
                        continue;

                    result.Add(new MepDuctTextSeed
                    {
                        Id = id,
                        Position = mtxt.Location,
                        Rotation = mtxt.Rotation,
                        RawText = raw,
                        Layer = mtxt.Layer ?? "",
                        Size = size
                    });
                }
            }

            return result;
        }

        private static List<ShaftExclusionZone> ReadShaftExclusionZones(
            Transaction tr,
            ObjectId[] ids)
        {
            List<ShaftMarker> markers = new List<ShaftMarker>();
            List<(ShaftExclusionZone Zone, bool Preferred)> boundaries =
                new List<(ShaftExclusionZone, bool)>();

            foreach (ObjectId id in ids ?? Array.Empty<ObjectId>())
            {
                Entity ent = SafeOpenEntity(tr, id);
                if (ent == null || IsAiDuctOutputLayer(ent.Layer))
                    continue;

                if (ent is DBText text)
                {
                    string raw = text.TextString ?? "";

                    if (MepDuctSizeParser.HasShaftContext(
                            raw + " " + (text.Layer ?? "")))
                    {
                        Point3d position = text.Position;

                        try
                        {
                            if (text.Justify != AttachmentPoint.BaseLeft)
                                position = text.AlignmentPoint;
                        }
                        catch
                        {
                        }

                        markers.Add(new ShaftMarker { Position = position });
                    }

                    continue;
                }

                if (ent is MText mtext)
                {
                    if (MepDuctSizeParser.HasShaftContext(
                            (mtext.Text ?? "") + " " + (mtext.Layer ?? "")))
                    {
                        markers.Add(new ShaftMarker { Position = mtext.Location });
                    }

                    continue;
                }

                bool preferred =
                    ent is Hatch ||
                    MepDuctSizeParser.HasShaftContext(ent.Layer);

                bool eligible =
                    preferred ||
                    (ent is Polyline polyline && polyline.Closed) ||
                    (ent is Polyline2d polyline2d && polyline2d.Closed) ||
                    (ent is Polyline3d polyline3d && polyline3d.Closed);

                if (ent is BlockReference blockReference)
                {
                    string blockName = "";

                    try
                    {
                        blockName = blockReference.Name ?? "";
                    }
                    catch
                    {
                    }

                    if (MepDuctSizeParser.HasShaftContext(
                            blockName + " " + (ent.Layer ?? "")))
                    {
                        eligible = true;
                        preferred = true;
                    }
                }

                if (!eligible ||
                    !TryCreateShaftZone(ent, out ShaftExclusionZone zone))
                {
                    continue;
                }

                boundaries.Add((zone, preferred));
            }

            if (markers.Count == 0 || boundaries.Count == 0)
                return new List<ShaftExclusionZone>();

            List<ShaftExclusionZone> result =
                new List<ShaftExclusionZone>();

            foreach (ShaftMarker marker in markers)
            {
                ShaftExclusionZone best = null;
                double bestScore = double.MaxValue;

                foreach (var boundary in boundaries)
                {
                    ShaftExclusionZone zone = boundary.Zone;
                    double distance = DistancePointToZone2D(marker.Position, zone);
                    double maxSearch = Math.Min(
                        3500.0,
                        Math.Max(1600.0, zone.MaxSide * 0.75 + 500.0));

                    if (distance > maxSearch)
                        continue;

                    double areaPenalty =
                        Math.Sqrt(Math.Max(1.0, zone.Width * zone.Height)) * 0.04;

                    double score =
                        distance +
                        areaPenalty +
                        (boundary.Preferred ? -450.0 : 0.0);

                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = zone;
                    }
                }

                if (best == null)
                    continue;

                bool duplicate = result.Any(existing =>
                    PlanDistance(existing.Center, best.Center) <= 120.0 &&
                    Math.Abs(existing.Width - best.Width) <= 160.0 &&
                    Math.Abs(existing.Height - best.Height) <= 160.0);

                if (!duplicate)
                    result.Add(best);
            }

            return result;
        }

        private static bool TryCreateShaftZone(
            Entity entity,
            out ShaftExclusionZone zone)
        {
            zone = null;

            if (entity == null)
                return false;

            try
            {
                Extents3d extents = entity.GeometricExtents;
                double width = Math.Abs(extents.MaxPoint.X - extents.MinPoint.X);
                double height = Math.Abs(extents.MaxPoint.Y - extents.MinPoint.Y);
                double minSide = Math.Min(width, height);
                double maxSide = Math.Max(width, height);

                // Ô trục trên bản vẽ MEP thường từ vài trăm đến vài nghìn mm.
                // Loại hatch tường/sàn quá lớn để không che mất tuyến thật.
                if (minSide < 120.0 ||
                    maxSide > 8000.0 ||
                    width * height > 36000000.0)
                {
                    return false;
                }

                zone = new ShaftExclusionZone
                {
                    MinX = Math.Min(extents.MinPoint.X, extents.MaxPoint.X),
                    MinY = Math.Min(extents.MinPoint.Y, extents.MaxPoint.Y),
                    MaxX = Math.Max(extents.MinPoint.X, extents.MaxPoint.X),
                    MaxY = Math.Max(extents.MinPoint.Y, extents.MaxPoint.Y)
                };

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static double DistancePointToZone2D(
            Point3d point,
            ShaftExclusionZone zone)
        {
            if (zone == null)
                return double.MaxValue;

            double dx = Math.Max(
                Math.Max(zone.MinX - point.X, 0.0),
                point.X - zone.MaxX);

            double dy = Math.Max(
                Math.Max(zone.MinY - point.Y, 0.0),
                point.Y - zone.MaxY);

            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static bool IsSegmentFullyInsideShaftZone(
            Point3d start,
            Point3d end,
            List<ShaftExclusionZone> zones,
            double tolerance)
        {
            if (zones == null || zones.Count == 0)
                return false;

            Point3d center = MidPoint(start, end);

            return zones.Any(zone =>
                zone != null &&
                zone.Contains2D(start, tolerance) &&
                zone.Contains2D(end, tolerance) &&
                zone.Contains2D(center, tolerance));
        }

        private static List<CurveCandidate> ReadCurveCandidates(
            Transaction tr,
            ObjectId[] ids,
            List<ShaftExclusionZone> shaftZones)
        {
            List<CurveCandidate> result = new List<CurveCandidate>();
            int candidateId = 0;

            foreach (ObjectId id in ids)
            {
                Entity ent = SafeOpenEntity(tr, id);
                if (ent == null || IsAiDuctOutputLayer(ent.Layer))
                    continue;

                if (MepDuctSizeParser.HasShaftContext(ent.Layer))
                    continue;

                bool layerLooksDuct =
                    MepDuctSizeParser.HasDuctContext(ent.Layer);

                // Layer FIRE RATED DUCT vẫn là duct. Chỉ loại PCCC/CTN khi
                // layer không có ngữ cảnh HVAC/DUCT rõ ràng.
                if (!layerLooksDuct &&
                    (MepDuctSizeParser.HasPipeOrFireProtectionContext(ent.Layer) ||
                     MepDuctSizeParser.HasDnPipeText(ent.Layer)))
                {
                    continue;
                }

                if (!(ent is Curve curve))
                    continue;

                if (ent is Arc)
                {
                    // Arc là hình học fitting, không được lấy dây cung Start-End
                    // làm một tuyến thẳng xuyên qua co/cút.
                    continue;
                }

                if (ent is Polyline polyline)
                {
                    if (TryGetClosedRectangleCenterline(
                            polyline,
                            out Point3d centerStart,
                            out Point3d centerEnd,
                            out double rectWidth))
                    {
                        if (IsSegmentFullyInsideShaftZone(
                                centerStart,
                                centerEnd,
                                shaftZones,
                                120.0))
                        {
                            continue;
                        }

                        result.Add(new CurveCandidate
                        {
                            CandidateId = candidateId++,
                            Id = id,
                            Curve = curve,
                            Entity = ent,
                            Layer = ent.Layer ?? "",
                            Start = polyline.StartPoint,
                            End = polyline.EndPoint,
                            Length = centerStart.DistanceTo(centerEnd),
                            ClosedRectangle = true,
                            RectCenterStart = centerStart,
                            RectCenterEnd = centerEnd,
                            RectWidth = rectWidth,
                            LayerLooksDuct = layerLooksDuct
                        });

                        continue;
                    }

                    // Closed polyline không phải khung duct thường là outline
                    // thiết bị/ký hiệu. Không tách nó thành các tuyến ống.
                    if (polyline.Closed || polyline.NumberOfVertices < 2)
                        continue;

                    for (int index = 0;
                        index < polyline.NumberOfVertices - 1;
                        index++)
                    {
                        try
                        {
                            if (polyline.GetSegmentType(index) != SegmentType.Line)
                                continue;

                            LineSegment3d lineSegment =
                                polyline.GetLineSegmentAt(index);

                            Point3d start = lineSegment.StartPoint;
                            Point3d end = lineSegment.EndPoint;
                            double length = start.DistanceTo(end);

                            if (length < MinCurveLength ||
                                IsSegmentFullyInsideShaftZone(
                                    start,
                                    end,
                                    shaftZones,
                                    80.0))
                            {
                                continue;
                            }

                            result.Add(new CurveCandidate
                            {
                                CandidateId = candidateId++,
                                Id = id,
                                Curve = curve,
                                Entity = ent,
                                Layer = ent.Layer ?? "",
                                Start = start,
                                End = end,
                                Length = length,
                                LayerLooksDuct = layerLooksDuct
                            });
                        }
                        catch
                        {
                        }
                    }

                    continue;
                }

                if (!(ent is Line) &&
                    !(ent is Polyline2d) &&
                    !(ent is Polyline3d))
                {
                    continue;
                }

                if ((ent is Polyline2d polyline2d && polyline2d.Closed) ||
                    (ent is Polyline3d polyline3d && polyline3d.Closed))
                {
                    continue;
                }

                double curveLength = GetCurveLength(curve);
                double chordLength = curve.StartPoint.DistanceTo(curve.EndPoint);

                if (curveLength < MinCurveLength ||
                    chordLength < MinCurveLength ||
                    chordLength / Math.Max(curveLength, 1.0) < 0.985)
                {
                    // Không dùng Start-End chord của polyline nhiều đỉnh/cong.
                    continue;
                }

                if (IsSegmentFullyInsideShaftZone(
                        curve.StartPoint,
                        curve.EndPoint,
                        shaftZones,
                        80.0))
                {
                    continue;
                }

                result.Add(new CurveCandidate
                {
                    CandidateId = candidateId++,
                    Id = id,
                    Curve = curve,
                    Entity = ent,
                    Layer = ent.Layer ?? "",
                    Start = curve.StartPoint,
                    End = curve.EndPoint,
                    Length = chordLength,
                    LayerLooksDuct = layerLooksDuct
                });
            }

            return result;
        }

        private static Entity SafeOpenEntity(
            Transaction tr,
            ObjectId id)
        {
            try
            {
                if (id.IsNull || !id.IsValid || id.IsErased)
                    return null;

                Entity ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (ent == null || ent.IsErased)
                    return null;

                return ent;
            }
            catch
            {
                return null;
            }
        }

        // ============================================================
        // 2. GÁN TEXT KÍCH THƯỚC VÀO TUYẾN GẦN VÀ KHỚP BỀ RỘNG
        // ============================================================

        private static void AttachBestSeeds(
            List<CurveCandidate> curves,
            List<MepDuctTextSeed> seeds,
            ref MepDuctScanResult result)
        {
            if (curves == null || seeds == null || seeds.Count == 0)
                return;

            foreach (CurveCandidate curve in curves)
            {
                MepDuctTextSeed best = null;
                double bestScore = double.MaxValue;
                double bestDistance = double.MaxValue;
                double bestAngle = double.MaxValue;

                foreach (MepDuctTextSeed seed in seeds)
                {
                    if (seed?.Size == null)
                        continue;

                    Point3d a = curve.ClosedRectangle ? curve.RectCenterStart : curve.Start;
                    Point3d b = curve.ClosedRectangle ? curve.RectCenterEnd : curve.End;

                    double distance = DistancePointToSegment2D(seed.Position, a, b);
                    double maxDistance = Math.Min(
                        1800.0,
                        Math.Max(
                            450.0,
                            seed.Size.MaxDimensionMm * 0.85 + 250.0));

                    if (distance > maxDistance)
                        continue;

                    // Size trần như "800x400" không có context HVAC chỉ được
                    // gắn vào nét rất gần; tránh hút nhầm kích thước kiến trúc.
                    if (!curve.LayerLooksDuct &&
                        !seed.Size.HasStrongDuctContext &&
                        distance > Math.Min(350.0, seed.Size.MaxDimensionMm * 0.45 + 80.0))
                    {
                        continue;
                    }

                    double angleDiff = curve.ClosedRectangle
                        ? 0.0
                        : ParallelAngleDifference(PlanAngle(a, b), seed.Rotation);

                    if (!curve.ClosedRectangle && angleDiff > MaxTextAngleRadians)
                        continue;

                    // Nếu là khung chữ nhật, kiểm tra bề rộng text có khớp bề rộng hình chữ nhật không
                    if (curve.ClosedRectangle && curve.RectWidth > 0.0)
                    {
                        double expectedW = seed.Size.WidthMm;
                        double expectedH = seed.Size.HeightMm;
                        bool matchW = Math.Abs(expectedW - curve.RectWidth) / Math.Max(1.0, curve.RectWidth) <= 0.25;
                        bool matchH = Math.Abs(expectedH - curve.RectWidth) / Math.Max(1.0, curve.RectWidth) <= 0.25;
                        if (!matchW && !matchH)
                            continue; // Bỏ qua nếu kích thước text không khớp hình học thực tế
                    }

                    double layerBonus = curve.LayerLooksDuct ? 180.0 : 0.0;
                    string systemFromLayer = MepDuctSizeParser.InferSystemCode(curve.Layer);
                    double systemBonus = !string.IsNullOrWhiteSpace(systemFromLayer) &&
                                         !string.IsNullOrWhiteSpace(seed.Size.SystemCode) &&
                                         string.Equals(systemFromLayer, seed.Size.SystemCode, StringComparison.OrdinalIgnoreCase)
                                         ? 120.0
                                         : 0.0;

                    double score = distance + angleDiff * 500.0 - layerBonus - systemBonus;

                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = seed;
                        bestDistance = distance;
                        bestAngle = angleDiff;
                    }
                }

                if (best == null)
                    continue;

                curve.Seed = best;
                curve.SeedDistance = bestDistance;
                curve.SeedAngle = bestAngle;
                curve.SeedScore = bestScore;
            }

            // Một annotation kích thước chỉ được quyền "khởi tạo" một ứng
            // viên hình học tốt nhất. Bản cũ gắn cùng một text 3300x700 cho
            // mọi LINE/POLYLINE nằm trong bán kính lớn, khiến outline AHU,
            // co và thiết bị đều bị tô ConstantWidth thành mảng tím khổng lồ.
            // Các đoạn còn lại phải nhận size qua double-line/topology, không
            // được dùng lại text như một nhãn toàn vùng.
            foreach (IGrouping<ObjectId, CurveCandidate> group in curves
                .Where(x => x?.Seed != null && !x.Seed.Id.IsNull)
                .GroupBy(x => x.Seed.Id))
            {
                CurveCandidate winner = group
                    .OrderBy(x => x.SeedScore)
                    .ThenBy(x => x.SeedDistance)
                    .ThenByDescending(x => x.LayerLooksDuct)
                    .ThenByDescending(x => x.Length)
                    .FirstOrDefault();

                foreach (CurveCandidate candidate in group)
                {
                    if (ReferenceEquals(candidate, winner))
                        continue;

                    candidate.Seed = null;
                    candidate.SeedDistance = double.MaxValue;
                    candidate.SeedAngle = double.MaxValue;
                    candidate.SeedScore = double.MaxValue;
                    result.AmbiguousCount++;
                }
            }
        }

        // ============================================================
        // 3. XÂY DỰNG CÁC ĐOẠN TIM TUYẾN BAN ĐẦU
        // ============================================================

        private static List<MepDuctSegment> BuildInitialSegments(
            List<CurveCandidate> curves,
            ref MepDuctScanResult result)
        {
            List<MepDuctSegment> output = new List<MepDuctSegment>();
            if (curves == null || curves.Count == 0)
                return output;

            HashSet<int> used = new HashSet<int>();
            int segIdCounter = 0;

            // 1) Khung chữ nhật khép kín
            foreach (CurveCandidate curve in curves.Where(c => c != null && c.ClosedRectangle))
            {
                bool trustedUnseededFrame =
                    curve.Seed == null &&
                    curve.LayerLooksDuct &&
                    curve.RectWidth > 0.0 &&
                    curve.RectCenterStart.DistanceTo(curve.RectCenterEnd) >=
                        curve.RectWidth * 1.35;

                if (curve.Seed == null && !trustedUnseededFrame)
                {
                    result.RejectedCount++;
                    continue;
                }

                MepDuctSegment segment = CreateSegmentFromCandidate(
                    segIdCounter++,
                    curve.RectCenterStart,
                    curve.RectCenterEnd,
                    curve.Seed,
                    curve.Layer,
                    curve.RectWidth,
                    curve.Seed?.Size != null ? 0.96 : 0.65,
                    "RECT_FRAME",
                    new[] { curve.Id },
                    "closed rectangle");

                if (segment != null)
                {
                    output.Add(segment);
                    used.Add(curve.CandidateId);
                    result.RectFrameCount++;
                }
            }

            // 2) Ghép cặp đường song song (Double-line)
            List<CurveCandidate> open = curves
                .Where(c => c != null && !c.ClosedRectangle && !used.Contains(c.CandidateId))
                .OrderByDescending(c => c.Length)
                .ToList();

            for (int i = 0; i < open.Count; i++)
            {
                CurveCandidate a = open[i];
                if (a == null || used.Contains(a.CandidateId))
                    continue;

                CurveCandidate bestPair = null;
                double bestPairScore = double.MaxValue;
                double bestSeparation = 0.0;

                for (int j = i + 1; j < open.Count; j++)
                {
                    CurveCandidate b = open[j];
                    if (b == null || used.Contains(b.CandidateId))
                        continue;

                    // Hai cạnh lấy từ cùng một open polyline thường là outline
                    // của thiết bị/co. Closed rectangle hợp lệ đã xử lý ở trên.
                    if (a.Id == b.Id)
                        continue;

                    double lengthRatio =
                        Math.Min(a.Length, b.Length) /
                        Math.Max(1.0, Math.Max(a.Length, b.Length));

                    if (lengthRatio < 0.45)
                        continue;

                    double angle = ParallelAngleDifference(PlanAngle(a.Start, a.End), PlanAngle(b.Start, b.End));
                    if (angle > DoubleLineAngleRadians)
                        continue;

                    double overlap = SegmentOverlapRatio(a.Start, a.End, b.Start, b.End);
                    if (overlap < 0.55)
                        continue;

                    double separation = ParallelSeparation2D(a.Start, a.End, b.Start, b.End);
                    MepDuctTextSeed pairSeed = ChooseBetterSeed(a, b);
                    double maxSeparation =
                        pairSeed?.Size != null
                            ? Math.Min(4500.0, pairSeed.Size.MaxDimensionMm * 1.30 + 180.0)
                            : 2500.0;

                    if (separation < 50.0 || separation > maxSeparation)
                        continue;

                    string systemA = MepDuctSizeParser.InferSystemCode(a.Layer);
                    string systemB = MepDuctSizeParser.InferSystemCode(b.Layer);

                    if (!string.IsNullOrWhiteSpace(systemA) &&
                        !string.IsNullOrWhiteSpace(systemB) &&
                        !string.Equals(systemA, systemB, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (pairSeed == null &&
                        !(a.LayerLooksDuct && b.LayerLooksDuct))
                    {
                        continue;
                    }

                    // Kiểm tra xem khoảng cách 2 đường có khớp với text seed không
                    bool seedMatchesSeparation = true;
                    if (pairSeed?.Size != null)
                    {
                        double expW = pairSeed.Size.WidthMm;
                        double expH = pairSeed.Size.HeightMm;
                        double expD = pairSeed.Size.DiameterMm;
                        bool matchW = Math.Abs(expW - separation) / separation <= 0.25;
                        bool matchH = Math.Abs(expH - separation) / separation <= 0.25;
                        bool matchD = Math.Abs(expD - separation) / separation <= 0.25;
                        if (!matchW && !matchH && !matchD)
                            seedMatchesSeparation = false;
                    }

                    if (!seedMatchesSeparation)
                        continue;

                    double overlapLength =
                        Math.Min(a.Length, b.Length) * overlap;

                    if (pairSeed == null && overlapLength < separation * 1.35)
                        continue;

                    double score =
                        (1.0 - overlap) * 300.0 +
                        angle * 400.0 +
                        (string.Equals(a.Layer, b.Layer, StringComparison.OrdinalIgnoreCase)
                            ? -80.0
                            : 0.0);

                    if (score < bestPairScore)
                    {
                        bestPairScore = score;
                        bestPair = b;
                        bestSeparation = separation;
                    }
                }

                if (bestPair != null)
                {
                    BuildCenterlineBetweenParallelCurves(
                        a.Start, a.End,
                        bestPair.Start, bestPair.End,
                        out Point3d centerStart,
                        out Point3d centerEnd);

                    MepDuctTextSeed seed = ChooseBetterSeed(a, bestPair);

                    MepDuctSegment segment = CreateSegmentFromCandidate(
                        segIdCounter++,
                        centerStart,
                        centerEnd,
                        seed,
                        PreferLayer(a, bestPair),
                        bestSeparation,
                        seed != null ? 0.95 : 0.70,
                        "DOUBLE_LINE",
                        new[] { a.Id, bestPair.Id },
                        "2 parallel boundary lines");

                    if (segment != null)
                    {
                        output.Add(segment);
                        used.Add(a.CandidateId);
                        used.Add(bestPair.CandidateId);
                        result.DoubleLinePairCount++;
                    }

                    continue;
                }

                // 3) Tuyến đơn (Single line): Chỉ nhận nếu có Text Seed kích thước hoặc Layer rõ ràng là ống gió
                bool explicitSingleTrusted =
                    a.Seed?.Size != null &&
                    (a.LayerLooksDuct ||
                     a.Seed.Size.HasStrongDuctContext ||
                     (a.SeedDistance <= 300.0 &&
                      a.Length >= a.Seed.Size.MaxDimensionMm * 1.50));

                if (explicitSingleTrusted || a.LayerLooksDuct)
                {
                    MepDuctSegment single = CreateSegmentFromCandidate(
                        segIdCounter++,
                        a.Start,
                        a.End,
                        explicitSingleTrusted ? a.Seed : null,
                        a.Layer,
                        0.0,
                        explicitSingleTrusted ? 0.90 : 0.40,
                        "CENTERLINE",
                        new[] { a.Id },
                        explicitSingleTrusted
                            ? "centerline with trusted duct seed"
                            : "duct layer without direct size");

                    if (single != null)
                    {
                        output.Add(single);
                        used.Add(a.CandidateId);
                        result.SingleCenterlineCount++;
                    }
                }
            }

            return output;
        }

        private static List<MepDuctSegment> ExcludeShaftZones(
            List<MepDuctSegment> segments,
            List<ShaftExclusionZone> zones,
            ref MepDuctScanResult result)
        {
            if (segments == null || segments.Count == 0 ||
                zones == null || zones.Count == 0)
            {
                return segments ?? new List<MepDuctSegment>();
            }

            List<MepDuctSegment> output =
                new List<MepDuctSegment>();

            foreach (MepDuctSegment segment in segments)
            {
                if (segment == null)
                    continue;

                List<MepDuctSegment> pieces =
                    new List<MepDuctSegment> { segment };

                bool changedByShaft = false;

                foreach (ShaftExclusionZone zone in zones)
                {
                    List<MepDuctSegment> next =
                        new List<MepDuctSegment>();

                    foreach (MepDuctSegment piece in pieces)
                    {
                        next.AddRange(
                            SubtractShaftZoneFromSegment(
                                piece,
                                zone,
                                out bool changed));

                        changedByShaft |= changed;
                    }

                    pieces = next;

                    if (pieces.Count == 0)
                        break;
                }

                if (changedByShaft)
                    result.ShaftExcludedCount++;

                if (pieces.Count == 0)
                {
                    result.RejectedCount++;
                    continue;
                }

                output.AddRange(pieces);
            }

            return output;
        }

        private static List<MepDuctSegment> SubtractShaftZoneFromSegment(
            MepDuctSegment segment,
            ShaftExclusionZone sourceZone,
            out bool changed)
        {
            changed = false;

            if (segment == null || sourceZone == null)
                return new List<MepDuctSegment>();

            double padding = Math.Max(
                30.0,
                Math.Min(120.0, GetOverlayPlanWidth(segment) * 0.06));

            ShaftExclusionZone zone = new ShaftExclusionZone
            {
                MinX = sourceZone.MinX - padding,
                MinY = sourceZone.MinY - padding,
                MaxX = sourceZone.MaxX + padding,
                MaxY = sourceZone.MaxY + padding
            };

            if (!TryGetSegmentInsideZoneInterval(
                    segment.Start,
                    segment.End,
                    zone,
                    out double insideStart,
                    out double insideEnd))
            {
                return new List<MepDuctSegment> { segment };
            }

            if (insideEnd - insideStart <= 1e-6)
                return new List<MepDuctSegment> { segment };

            changed = true;
            List<MepDuctSegment> pieces =
                new List<MepDuctSegment>();

            if (insideStart > 1e-6)
            {
                Point3d outsideEnd = InterpolatePoint(
                    segment.Start,
                    segment.End,
                    insideStart);

                MepDuctSegment before = CloneSegmentPiece(
                    segment,
                    segment.Start,
                    outsideEnd,
                    "clipped before shaft edge");

                if (before != null)
                    pieces.Add(before);
            }

            if (insideEnd < 1.0 - 1e-6)
            {
                Point3d outsideStart = InterpolatePoint(
                    segment.Start,
                    segment.End,
                    insideEnd);

                MepDuctSegment after = CloneSegmentPiece(
                    segment,
                    outsideStart,
                    segment.End,
                    "clipped after shaft edge");

                if (after != null)
                    pieces.Add(after);
            }

            return pieces;
        }

        private static bool TryGetSegmentInsideZoneInterval(
            Point3d start,
            Point3d end,
            ShaftExclusionZone zone,
            out double enter,
            out double exit)
        {
            enter = 0.0;
            exit = 1.0;

            if (zone == null)
                return false;

            double dx = end.X - start.X;
            double dy = end.Y - start.Y;

            if (!ClipParameter(-dx, start.X - zone.MinX, ref enter, ref exit) ||
                !ClipParameter(dx, zone.MaxX - start.X, ref enter, ref exit) ||
                !ClipParameter(-dy, start.Y - zone.MinY, ref enter, ref exit) ||
                !ClipParameter(dy, zone.MaxY - start.Y, ref enter, ref exit))
            {
                return false;
            }

            return exit >= enter && exit >= 0.0 && enter <= 1.0;
        }

        private static bool ClipParameter(
            double p,
            double q,
            ref double enter,
            ref double exit)
        {
            if (Math.Abs(p) <= 1e-12)
                return q >= 0.0;

            double ratio = q / p;

            if (p < 0.0)
            {
                if (ratio > exit)
                    return false;

                if (ratio > enter)
                    enter = ratio;
            }
            else
            {
                if (ratio < enter)
                    return false;

                if (ratio < exit)
                    exit = ratio;
            }

            return true;
        }

        private static Point3d InterpolatePoint(
            Point3d start,
            Point3d end,
            double parameter)
        {
            double t = Math.Max(0.0, Math.Min(1.0, parameter));

            return new Point3d(
                start.X + (end.X - start.X) * t,
                start.Y + (end.Y - start.Y) * t,
                start.Z + (end.Z - start.Z) * t);
        }

        private static MepDuctSegment CloneSegmentPiece(
            MepDuctSegment source,
            Point3d start,
            Point3d end,
            string evidence)
        {
            double length = start.DistanceTo(end);

            if (source == null || length < MinCurveLength)
                return null;

            return new MepDuctSegment
            {
                Id = source.Id,
                Start = start,
                End = end,
                OriginalStart = start,
                OriginalEnd = end,
                LengthMm = length,
                Layer = source.Layer,
                SystemCode = source.SystemCode,
                FireRating = source.FireRating,
                Size = source.Size,
                Shape = source.Shape,
                WidthMm = source.WidthMm,
                HeightMm = source.HeightMm,
                DiameterMm = source.DiameterMm,
                MeasuredPlanWidth = source.MeasuredPlanWidth,
                Confidence = source.Confidence,
                HasExplicitSize = source.HasExplicitSize,
                Evidence = (source.Evidence ?? "") + " | " + (evidence ?? "shaft clip"),
                Representation = source.Representation,
                SourceIds = (source.SourceIds ?? new List<ObjectId>()).ToList()
            };
        }

        private static MepDuctSegment CreateSegmentFromCandidate(
            int id,
            Point3d start,
            Point3d end,
            MepDuctTextSeed seed,
            string layer,
            double measuredWidth,
            double confidence,
            string representation,
            IEnumerable<ObjectId> sourceIds,
            string evidence)
        {
            double length = start.DistanceTo(end);
            if (length < MinCurveLength)
                return null;

            string system = "";
            string fire = "";
            string size = "";
            string shape = "";
            double w = 0.0, h = 0.0, d = 0.0;
            bool explicitSize = false;

            if (seed?.Size != null)
            {
                explicitSize = true;
                size = seed.Size.CanonicalSize;
                shape = seed.Size.Shape;
                w = seed.Size.WidthMm;
                h = seed.Size.HeightMm;
                d = seed.Size.DiameterMm;
                system = !string.IsNullOrWhiteSpace(seed.Size.SystemCode)
                    ? seed.Size.SystemCode
                    : MepDuctSizeParser.InferSystemCode(layer);
                fire = !string.IsNullOrWhiteSpace(seed.Size.FireRating)
                    ? seed.Size.FireRating
                    : MepDuctSizeParser.ParseFireRating(layer);
            }
            else
            {
                system = MepDuctSizeParser.InferSystemCode(layer);
                fire = MepDuctSizeParser.ParseFireRating(layer);
                if (measuredWidth > 0.0)
                {
                    w = measuredWidth;
                }
            }

            return new MepDuctSegment
            {
                Id = id,
                Start = start,
                End = end,
                OriginalStart = start,
                OriginalEnd = end,
                LengthMm = length,
                Layer = layer ?? "",
                SystemCode = system ?? "",
                FireRating = fire ?? "",
                Size = size ?? "",
                Shape = shape ?? "",
                WidthMm = w,
                HeightMm = h,
                DiameterMm = d,
                MeasuredPlanWidth = measuredWidth,
                Confidence = confidence,
                HasExplicitSize = explicitSize,
                Evidence = evidence ?? "",
                Representation = representation ?? "",
                SourceIds = (sourceIds ?? Enumerable.Empty<ObjectId>()).Where(x => !x.IsNull).Distinct().ToList()
            };
        }

        // ============================================================
        // 4. NHẬN DIỆN PHỤ KIỆN (CO, TÊ, GIẢM, GÓT GIÀY) & TÔPÔ
        // ============================================================

        private static List<MepDuctFitting> DetectFittingsAndBuildTopology(List<MepDuctSegment> segments)
        {
            List<MepDuctFitting> fittings = new List<MepDuctFitting>();
            if (segments == null || segments.Count == 0)
                return fittings;

            int fittingIdCounter = 0;
            int count = segments.Count;

            // 1) Tìm CO (Elbow) và GIẢM (Reducer) bằng phân tích tia giao nhau
            for (int i = 0; i < count; i++)
            {
                MepDuctSegment s1 = segments[i];
                if (!IsTopologyCandidate(s1)) continue;

                for (int j = i + 1; j < count; j++)
                {
                    MepDuctSegment s2 = segments[j];
                    if (!IsTopologyCandidate(s2) ||
                        !AreSystemsCompatible(s1, s2))
                    {
                        continue;
                    }

                    double angle = ParallelAngleDifference(PlanAngle(s1.Start, s1.End), PlanAngle(s2.Start, s2.End));

                    // CO: góc bẻ hướng từ 25° đến 155° (chuẩn 90° hoặc 45°)
                    if (angle >= Math.PI / 7.2 &&
                        angle <= Math.PI * 0.5 + 1e-6 &&
                        AreElbowSizesCompatible(s1, s2))
                    {
                        if (TryIntersectRays2D(s1.Start, s1.End, s2.Start, s2.End, out Point3d pInt))
                        {
                            double maxPlanWidth = Math.Max(
                                GetOverlayPlanWidth(s1),
                                GetOverlayPlanWidth(s2));

                            double maxAllowedDist = Math.Min(
                                MaxElbowReachMm,
                                Math.Max(
                                    450.0,
                                    maxPlanWidth * 1.35 + 350.0));

                            double d1 = Math.Min(PlanDistance(s1.Start, pInt), PlanDistance(s1.End, pInt));
                            double d2 = Math.Min(PlanDistance(s2.Start, pInt), PlanDistance(s2.End, pInt));
                            double endpointGap = MinDistanceBetweenEndpoints(s1, s2);

                            if (d1 <= maxAllowedDist &&
                                d2 <= maxAllowedDist &&
                                endpointGap <= maxAllowedDist * 1.55)
                            {
                                double deg = Math.Round(angle * 180.0 / Math.PI);
                                double fittingAngle = (deg >= 70 && deg <= 110) ? 90.0 : ((deg >= 35 && deg <= 55) ? 45.0 : deg);

                                fittings.Add(new MepDuctFitting
                                {
                                    Id = fittingIdCounter++,
                                    Type = MepDuctFittingType.Elbow,
                                    Position = pInt,
                                    ConnectedSegmentIds = { s1.Id, s2.Id },
                                    AngleDegrees = fittingAngle,
                                    SizeIn = s1.Size,
                                    SizeOut = s2.Size,
                                    Description = "Co / Cút " + fittingAngle + "°"
                                });
                            }
                        }
                    }
                    // GIẢM (Reducer) hoặc Nối Thẳng: 2 đoạn gần như thẳng hàng (angle <= 15°)
                    else if (angle <= Math.PI / 12.0)
                    {
                        // Kiểm tra khoảng cách gap giữa 2 đầu mút
                        Point3d p1Near = s1.Start, p2Near = s2.Start;
                        double minGap = double.MaxValue;

                        Point3d[] pts1 = { s1.Start, s1.End };
                        Point3d[] pts2 = { s2.Start, s2.End };

                        foreach (var p1 in pts1)
                        {
                            foreach (var p2 in pts2)
                            {
                                double d = PlanDistance(p1, p2);
                                if (d < minGap)
                                {
                                    minGap = d;
                                    p1Near = p1;
                                    p2Near = p2;
                                }
                            }
                        }

                        // Nếu 2 đầu mút cách nhau trong khoảng chuyển tiếp côn thu (<= 750mm)
                        if (minGap <= 750.0)
                        {
                            Point3d pMid = MidPoint(p1Near, p2Near);

                            bool differentSize = !string.IsNullOrWhiteSpace(s1.Size) &&
                                                 !string.IsNullOrWhiteSpace(s2.Size) &&
                                                 !string.Equals(s1.Size, s2.Size, StringComparison.OrdinalIgnoreCase);

                            bool differentMeasuredWidth = s1.MeasuredPlanWidth > 0 && s2.MeasuredPlanWidth > 0 &&
                                                          Math.Abs(s1.MeasuredPlanWidth - s2.MeasuredPlanWidth) >= 80.0;

                            if (differentSize || differentMeasuredWidth)
                            {
                                double w1 = s1.WidthMm > 0 ? s1.WidthMm : s1.MeasuredPlanWidth;
                                double w2 = s2.WidthMm > 0 ? s2.WidthMm : s2.MeasuredPlanWidth;

                                fittings.Add(new MepDuctFitting
                                {
                                    Id = fittingIdCounter++,
                                    Type = MepDuctFittingType.Reducer,
                                    Position = pMid,
                                    ConnectedSegmentIds = { s1.Id, s2.Id },
                                    AngleDegrees = 180.0,
                                    SizeIn = s1.Size,
                                    SizeOut = s2.Size,
                                    WideWidth = Math.Max(w1, w2),
                                    NarrowWidth = Math.Min(w1, w2),
                                    ReducerStart = p1Near,
                                    ReducerEnd = p2Near,
                                    Description = "Côn thu / Giảm (" + (s1.Size ?? w1.ToString("0")) + " -> " + (s2.Size ?? w2.ToString("0")) + ")"
                                });
                            }
                        }
                    }
                }
            }

            // 2) Tìm TÊ (Tee) và GÓT GIÀY (Shoe Tap) bằng cách chiếu nhánh vào trục chính
            for (int i = 0; i < count; i++)
            {
                MepDuctSegment branch = segments[i];
                if (!IsTopologyCandidate(branch)) continue;

                for (int j = 0; j < count; j++)
                {
                    if (i == j) continue;
                    MepDuctSegment main = segments[j];
                    if (!IsTopologyCandidate(main) ||
                        !AreSystemsCompatible(branch, main))
                    {
                        continue;
                    }

                    // Kiểm tra cả 2 đầu mút của branch xem đầu nào đâm vào main
                    Point3d[] branchEnds = { branch.Start, branch.End };
                    Point3d[] branchOthers = { branch.End, branch.Start };

                    for (int k = 0; k < 2; k++)
                    {
                        Point3d brAt = branchEnds[k];
                        Point3d brOther = branchOthers[k];

                        Point3d pProj = ProjectPointOnLine2D(brAt, main.Start, main.End);
                        double distFromMain = PlanDistance(brAt, pProj);
                        double mainParameter = ProjectionParameter2D(
                            pProj,
                            main.Start,
                            main.End);

                        double mainInteriorMargin = Math.Min(
                            0.20,
                            Math.Max(
                                0.025,
                                GetOverlayPlanWidth(main) /
                                Math.Max(1.0, main.LengthMm) * 0.30));

                        double maxBranchReach = Math.Min(
                            MaxTopologyReachMm,
                            Math.Max(
                                250.0,
                                main.MaxDimensionMm * 0.65 + 200.0));

                        if (distFromMain <= maxBranchReach &&
                            IsPointWithinSegmentBounds(pProj, main.Start, main.End, 150.0) &&
                            mainParameter >= mainInteriorMargin &&
                            mainParameter <= 1.0 - mainInteriorMargin)
                        {
                            double angleToMain = ParallelAngleDifference(PlanAngle(brOther, brAt), PlanAngle(main.Start, main.End));
                            double angleDeg = Math.Round(angleToMain * 180.0 / Math.PI);
                            Point3d branchEdge = ComputeBranchEdgeConnectionPoint(
                                pProj,
                                brOther,
                                main,
                                angleToMain);

                            // Tê 90°
                            if (angleDeg >= 68.0 && angleDeg <= 112.0)
                            {
                                fittings.Add(new MepDuctFitting
                                {
                                    Id = fittingIdCounter++,
                                    Type = MepDuctFittingType.Tee,
                                    Position = pProj,
                                    ConnectedSegmentIds = { main.Id, branch.Id },
                                    MainSegmentId = main.Id,
                                    BranchSegmentId = branch.Id,
                                    HasBranchEdgePosition = true,
                                    BranchEdgePosition = branchEdge,
                                    AngleDegrees = 90.0,
                                    SizeIn = main.Size,
                                    SizeBranch = branch.Size,
                                    Description = "Tê 90° (Nhánh " + branch.Size + ")"
                                });
                            }
                            // Gót Giày (Shoe Tap xiên 30° - 65°)
                            else if (angleDeg >= 28.0 && angleDeg <= 67.0)
                            {
                                fittings.Add(new MepDuctFitting
                                {
                                    Id = fittingIdCounter++,
                                    Type = MepDuctFittingType.ShoeTap,
                                    Position = pProj,
                                    ConnectedSegmentIds = { main.Id, branch.Id },
                                    MainSegmentId = main.Id,
                                    BranchSegmentId = branch.Id,
                                    HasBranchEdgePosition = true,
                                    BranchEdgePosition = branchEdge,
                                    AngleDegrees = angleDeg,
                                    SizeIn = main.Size,
                                    SizeBranch = branch.Size,
                                    Description = "Gót giày / Trích nhánh " + angleDeg + "°"
                                });
                            }
                        }
                    }
                }
            }

            return DeduplicateFittings(fittings);
        }

        private static bool IsTopologyCandidate(MepDuctSegment segment)
        {
            if (segment == null || segment.LengthMm < MinCurveLength)
                return false;

            if (segment.HasExplicitSize ||
                !string.IsNullOrWhiteSpace(segment.Size))
            {
                return segment.Confidence >= 0.60;
            }

            bool ductLayer =
                MepDuctSizeParser.HasDuctContext(segment.Layer) &&
                !MepDuctSizeParser.HasShaftContext(segment.Layer);

            if (!ductLayer)
            {
                return
                    segment.MeasuredPlanWidth > 0.0 &&
                    segment.Confidence >= 0.60 &&
                    (string.Equals(segment.Representation, "DOUBLE_LINE", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(segment.Representation, "RECT_FRAME", StringComparison.OrdinalIgnoreCase));
            }

            if (segment.MeasuredPlanWidth > 0.0)
            {
                return segment.LengthMm >= segment.MeasuredPlanWidth * 1.10;
            }

            return segment.LengthMm >= 500.0;
        }

        private static bool AreElbowSizesCompatible(
            MepDuctSegment first,
            MepDuctSegment second)
        {
            if (first == null || second == null)
                return false;

            if (!string.IsNullOrWhiteSpace(first.Size) &&
                !string.IsNullOrWhiteSpace(second.Size))
            {
                return string.Equals(
                    first.Size,
                    second.Size,
                    StringComparison.OrdinalIgnoreCase);
            }

            bool firstHasWidthEvidence =
                first.MeasuredPlanWidth > 0.0 ||
                first.WidthMm > 0.0 ||
                first.DiameterMm > 0.0;

            bool secondHasWidthEvidence =
                second.MeasuredPlanWidth > 0.0 ||
                second.WidthMm > 0.0 ||
                second.DiameterMm > 0.0;

            if (!firstHasWidthEvidence || !secondHasWidthEvidence)
                return true;

            double firstWidth = GetOverlayPlanWidth(first);
            double secondWidth = GetOverlayPlanWidth(second);

            if (firstWidth > 0.0 && secondWidth > 0.0)
            {
                double tolerance = Math.Max(
                    80.0,
                    Math.Max(firstWidth, secondWidth) * 0.18);

                return Math.Abs(firstWidth - secondWidth) <= tolerance;
            }

            return true;
        }

        private static Point3d ComputeBranchEdgeConnectionPoint(
            Point3d mainCenterIntersection,
            Point3d branchOutsidePoint,
            MepDuctSegment main,
            double branchAngleToMain)
        {
            double vx = branchOutsidePoint.X - mainCenterIntersection.X;
            double vy = branchOutsidePoint.Y - mainCenterIntersection.Y;
            double length = Math.Sqrt(vx * vx + vy * vy);

            if (length <= 1e-9)
                return mainCenterIntersection;

            double mainHalfWidth = Math.Max(
                25.0,
                GetOverlayPlanWidth(main) * 0.5);

            double sinAngle = Math.Abs(Math.Sin(branchAngleToMain));
            double travel = mainHalfWidth / Math.Max(0.35, sinAngle);
            travel = Math.Min(travel, mainHalfWidth * 2.5);

            return new Point3d(
                mainCenterIntersection.X + vx / length * travel,
                mainCenterIntersection.Y + vy / length * travel,
                branchOutsidePoint.Z);
        }

        private static bool AreSystemsCompatible(
            MepDuctSegment first,
            MepDuctSegment second)
        {
            string a = first?.SystemCode ?? "";
            string b = second?.SystemCode ?? "";

            return string.IsNullOrWhiteSpace(a) ||
                   string.IsNullOrWhiteSpace(b) ||
                   string.Equals(a, b, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(a, "DUCT", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(b, "DUCT", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gom cụm (cluster) các fitting trong bán kính ClusterRadius.
        /// Mỗi cụm chỉ giữ lại 1 fitting ưu tiên nhất (Tee &gt; Elbow &gt; Reducer &gt; ShoeTap).
        /// Tránh vẽ chồng đống cross-marker đỏ tại 1 điểm nút.
        /// </summary>
        private static List<MepDuctFitting> DeduplicateFittings(List<MepDuctFitting> raw)
        {
            const double ClusterRadius = 250.0;

            if (raw == null || raw.Count == 0)
                return new List<MepDuctFitting>();

            // Sắp xếp theo độ ưu tiên: Tee (1) > Elbow (2) > Reducer (3) > ShoeTap (4) > others
            var sorted = raw.OrderBy(f => FittingPriority(f.Type)).ToList();

            var kept = new List<MepDuctFitting>();
            var eliminated = new HashSet<int>(); // index trong sorted

            for (int i = 0; i < sorted.Count; i++)
            {
                if (eliminated.Contains(i))
                    continue;

                MepDuctFitting fi = sorted[i];
                kept.Add(fi);

                // Đánh dấu tất cả fitting gần hơn ClusterRadius là duplicate
                for (int j = i + 1; j < sorted.Count; j++)
                {
                    if (eliminated.Contains(j))
                        continue;

                    MepDuctFitting fj = sorted[j];
                    double distance = PlanDistance(fi.Position, fj.Position);
                    bool sharesSegment =
                        fi.ConnectedSegmentIds != null &&
                        fj.ConnectedSegmentIds != null &&
                        fi.ConnectedSegmentIds.Intersect(fj.ConnectedSegmentIds).Any();

                    // Hai ống song song có thể cách nhau <250 mm nhưng không
                    // phải cùng fitting. Chỉ gom khi cùng segment hoặc gần như
                    // trùng đúng một điểm hình học.
                    if (distance < ClusterRadius &&
                        (sharesSegment || distance <= 40.0))
                    {
                        eliminated.Add(j);
                    }
                }
            }

            return kept;
        }

        private static int FittingPriority(MepDuctFittingType type)
        {
            switch (type)
            {
                case MepDuctFittingType.Tee:     return 1;
                case MepDuctFittingType.Elbow:   return 2;
                case MepDuctFittingType.Reducer: return 3;
                case MepDuctFittingType.ShoeTap: return 4;
                case MepDuctFittingType.Cross:   return 5;
                case MepDuctFittingType.EndCap:  return 6;
                default:                         return 7;
            }
        }

        // ============================================================
        // 5. LAN TRUYỀN KÍCH THƯỚC QUA ĐỒ THỊ TÔPÔ
        // ============================================================

        private static int PropagateSizesThroughTopology(
            List<MepDuctSegment> segments,
            List<MepDuctFitting> fittings,
            List<MepDuctTextSeed> seeds)
        {
            if (segments == null)
                return 0;

            Dictionary<int, MepDuctSegment> segMap = segments.ToDictionary(s => s.Id);
            int propagatedCount = 0;

            // Vòng lặp lan truyền cho đến khi hội tụ (tối đa 10 passes)
            for (int pass = 0; pass < 10; pass++)
            {
                bool changed = false;

                // 1) Lan truyền qua các mối nối phụ kiện
                if (fittings != null)
                {
                    foreach (MepDuctFitting f in fittings)
                    {
                        if (f.Type == MepDuctFittingType.Elbow)
                        {
                            if (f.ConnectedSegmentIds.Count >= 2 &&
                                segMap.TryGetValue(f.ConnectedSegmentIds[0], out MepDuctSegment s1) &&
                                segMap.TryGetValue(f.ConnectedSegmentIds[1], out MepDuctSegment s2))
                            {
                                if (TryPropagateBetweenSegments(s1, s2))
                                {
                                    propagatedCount++;
                                    changed = true;
                                }
                                else if (TryPropagateBetweenSegments(s2, s1))
                                {
                                    propagatedCount++;
                                    changed = true;
                                }
                            }
                        }
                        else if (f.Type == MepDuctFittingType.Tee || f.Type == MepDuctFittingType.ShoeTap)
                        {
                            if (f.MainSegmentId.HasValue && f.BranchSegmentId.HasValue &&
                                segMap.TryGetValue(f.MainSegmentId.Value, out MepDuctSegment sMain) &&
                                segMap.TryGetValue(f.BranchSegmentId.Value, out MepDuctSegment sBranch))
                            {
                                // Đồng bộ System và Fire rating cho nhánh
                                if (string.IsNullOrWhiteSpace(sBranch.SystemCode) && !string.IsNullOrWhiteSpace(sMain.SystemCode))
                                    sBranch.SystemCode = sMain.SystemCode;
                                if (string.IsNullOrWhiteSpace(sBranch.FireRating) && !string.IsNullOrWhiteSpace(sMain.FireRating))
                                    sBranch.FireRating = sMain.FireRating;
                            }
                        }
                    }
                }

                // 2) Lan truyền qua các đoạn thẳng hàng gần nhau (Collinear segments)
                for (int i = 0; i < segments.Count; i++)
                {
                    MepDuctSegment s1 = segments[i];
                    if (s1 == null) continue;

                    for (int j = i + 1; j < segments.Count; j++)
                    {
                        MepDuctSegment s2 = segments[j];
                        if (s2 == null) continue;

                        if (!AreSystemsCompatible(s1, s2))
                            continue;

                        double angle = ParallelAngleDifference(PlanAngle(s1.Start, s1.End), PlanAngle(s2.Start, s2.End));
                        if (angle <= Math.PI / 15.0) // Thẳng hàng
                        {
                            double lateralOffset = PerpendicularDistanceToLine2D(
                                s2.Center,
                                s1.Start,
                                s1.End);

                            if (lateralOffset > 80.0)
                                continue;

                            double d = MinDistanceBetweenEndpoints(s1, s2);
                            if (d <= 300.0)
                            {
                                // Chỉ lan truyền nếu bề rộng đo thực tế tương thích
                                if (s1.MeasuredPlanWidth <= 0 || s2.MeasuredPlanWidth <= 0 ||
                                    Math.Abs(s1.MeasuredPlanWidth - s2.MeasuredPlanWidth) <= 70.0)
                                {
                                    if (TryPropagateBetweenSegments(s1, s2))
                                    {
                                        propagatedCount++;
                                        changed = true;
                                    }
                                    else if (TryPropagateBetweenSegments(s2, s1))
                                    {
                                        propagatedCount++;
                                        changed = true;
                                    }
                                }
                            }
                        }
                    }
                }

                if (!changed)
                    break;
            }

            // 3) Gán fallback cho các đoạn có MeasuredPlanWidth nhưng chưa có Text (ví dụ: Wx(W/2))
            foreach (MepDuctSegment s in segments.Where(x =>
                x != null &&
                string.IsNullOrWhiteSpace(x.Size) &&
                x.MeasuredPlanWidth >= 100.0 &&
                MepDuctSizeParser.HasDuctContext(x.Layer) &&
                (string.Equals(x.Representation, "DOUBLE_LINE", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(x.Representation, "RECT_FRAME", StringComparison.OrdinalIgnoreCase)) &&
                x.LengthMm >= x.MeasuredPlanWidth * 1.35))
            {
                double w = s.MeasuredPlanWidth;
                double h = Math.Round(w * 0.5); // Tỷ lệ tiêu chuẩn 2:1 nếu không có chiều cao
                s.WidthMm = w;
                s.HeightMm = h;
                s.Shape = "RECT";
                s.Size = MepDuctSizeParser.FormatMm(w) + "x" + MepDuctSizeParser.FormatMm(h);
                if (string.IsNullOrWhiteSpace(s.SystemCode))
                    s.SystemCode = MepDuctSizeParser.InferSystemCode(s.Layer);
                s.Confidence = Math.Max(0.72, s.Confidence);
                s.Representation = "INFERRED_FROM_CAD_WIDTH";
                propagatedCount++;
            }

            return propagatedCount;
        }

        private static bool TryPropagateBetweenSegments(MepDuctSegment from, MepDuctSegment to)
        {
            if (from == null || to == null)
                return false;

            if (string.IsNullOrWhiteSpace(from.Size) || !string.IsNullOrWhiteSpace(to.Size))
                return false;

            if (!AreSystemsCompatible(from, to))
                return false;

            // Kiểm tra ràng buộc bề rộng đo được: Không gán size lớn vào đoạn nhỏ
            if (to.MeasuredPlanWidth > 0.0)
            {
                double expW = from.WidthMm;
                double expH = from.HeightMm;
                double expD = from.DiameterMm;
                bool matchW = Math.Abs(expW - to.MeasuredPlanWidth) / to.MeasuredPlanWidth <= 0.25;
                bool matchH = Math.Abs(expH - to.MeasuredPlanWidth) / to.MeasuredPlanWidth <= 0.25;
                bool matchD = Math.Abs(expD - to.MeasuredPlanWidth) / to.MeasuredPlanWidth <= 0.25;
                if (!matchW && !matchH && !matchD)
                    return false; // Bề rộng đo không khớp -> Không lan truyền sai!
            }

            to.Size = from.Size;
            to.Shape = from.Shape;
            to.WidthMm = from.WidthMm;
            to.HeightMm = from.HeightMm;
            to.DiameterMm = from.DiameterMm;
            if (string.IsNullOrWhiteSpace(to.SystemCode))
                to.SystemCode = from.SystemCode;
            if (string.IsNullOrWhiteSpace(to.FireRating))
                to.FireRating = from.FireRating;

            to.Confidence = Math.Max(0.80, from.Confidence - 0.05);
            to.Representation = "INHERITED";
            to.Evidence = "Inherited from segment " + from.Id;

            return true;
        }

        // ============================================================
        // 6. QUY TẮC CẮT NỐI PHỤ KIỆN 50/50
        // ============================================================

        private static void ApplyFitting5050Adjustment(
            List<MepDuctSegment> segments,
            List<MepDuctFitting> fittings)
        {
            if (segments == null || fittings == null)
                return;

            Dictionary<int, MepDuctSegment> segMap = segments.ToDictionary(s => s.Id);

            foreach (MepDuctFitting f in fittings)
            {
                // 1) Co (Elbow 50/50): Cả 2 đoạn kéo dài gặp nhau tại tâm đỉnh giao nhau
                if (f.Type == MepDuctFittingType.Elbow)
                {
                    if (f.ConnectedSegmentIds.Count >= 2 &&
                        segMap.TryGetValue(f.ConnectedSegmentIds[0], out MepDuctSegment s1) &&
                        segMap.TryGetValue(f.ConnectedSegmentIds[1], out MepDuctSegment s2))
                    {
                        Point3d pApex = f.Position;

                        TryMoveNearestEndpointTo(
                            s1,
                            pApex,
                            GetSafeElbowMoveDistance(s1));

                        TryMoveNearestEndpointTo(
                            s2,
                            pApex,
                            GetSafeElbowMoveDistance(s2));
                    }
                }
                // 2) Tê và Gót Giày: nhánh chỉ chạm mép ngoài ống chính.
                // Không kéo tim nhánh vào giữa làm overlay đè nửa thân ống lớn.
                else if (f.Type == MepDuctFittingType.Tee || f.Type == MepDuctFittingType.ShoeTap)
                {
                    if (f.BranchSegmentId.HasValue && segMap.TryGetValue(f.BranchSegmentId.Value, out MepDuctSegment sBranch))
                    {
                        Point3d pJunc =
                            f.HasBranchEdgePosition
                                ? f.BranchEdgePosition
                                : f.Position;

                        TryMoveNearestEndpointTo(
                            sBranch,
                            pJunc,
                            GetSafeTopologyMoveDistance(sBranch));
                    }
                }
                // 3) Giảm (Reducer 50/50): Đoạn trước kéo dài 50% tới tâm, đoạn sau kéo dài 50% từ tâm
                else if (f.Type == MepDuctFittingType.Reducer)
                {
                    if (f.ConnectedSegmentIds.Count >= 2 &&
                        segMap.TryGetValue(f.ConnectedSegmentIds[0], out MepDuctSegment s1) &&
                        segMap.TryGetValue(f.ConnectedSegmentIds[1], out MepDuctSegment s2))
                    {
                        Point3d pMid = f.Position;

                        TryMoveNearestEndpointTo(
                            s1,
                            pMid,
                            GetSafeTopologyMoveDistance(s1));

                        TryMoveNearestEndpointTo(
                            s2,
                            pMid,
                            GetSafeTopologyMoveDistance(s2));
                    }
                }
            }
        }

        private static double GetSafeTopologyMoveDistance(
            MepDuctSegment segment)
        {
            return Math.Min(
                MaxTopologyReachMm,
                Math.Max(
                    250.0,
                    (segment?.MaxDimensionMm ?? 0.0) * 0.75 + 250.0));
        }

        private static double GetSafeElbowMoveDistance(
            MepDuctSegment segment)
        {
            return Math.Min(
                MaxElbowReachMm,
                Math.Max(
                    450.0,
                    GetOverlayPlanWidth(segment) * 1.35 + 350.0));
        }

        private static bool TryMoveNearestEndpointTo(
            MepDuctSegment segment,
            Point3d target,
            double maxDistance)
        {
            if (segment == null)
                return false;

            double startDistance = PlanDistance(segment.Start, target);
            double endDistance = PlanDistance(segment.End, target);
            double nearest = Math.Min(startDistance, endDistance);

            if (nearest > Math.Max(1.0, maxDistance))
                return false;

            if (startDistance <= endDistance)
                segment.Start = target;
            else
                segment.End = target;

            segment.LengthMm = segment.Start.DistanceTo(segment.End);
            return true;
        }

        private static void ConnectCollinearSameSizeRuns(
            List<MepDuctSegment> segments)
        {
            if (segments == null || segments.Count < 2)
                return;

            for (int pass = 0; pass < 4; pass++)
            {
                bool changed = false;

                for (int i = 0; i < segments.Count; i++)
                {
                    MepDuctSegment first = segments[i];
                    if (!IsTrustedDuctSegment(first))
                        continue;

                    for (int j = i + 1; j < segments.Count; j++)
                    {
                        MepDuctSegment second = segments[j];

                        if (!IsTrustedDuctSegment(second) ||
                            !HaveSameOverlayStyle(first, second))
                        {
                            continue;
                        }

                        double angle = ParallelAngleDifference(
                            PlanAngle(first.Start, first.End),
                            PlanAngle(second.Start, second.End));

                        if (angle > Math.PI / 45.0) // 4°
                            continue;

                        double width = Math.Max(
                            GetOverlayPlanWidth(first),
                            GetOverlayPlanWidth(second));

                        double lateralTolerance = Math.Max(
                            18.0,
                            Math.Min(45.0, width * 0.04));

                        if (PerpendicularDistanceToLine2D(
                                second.Center,
                                first.Start,
                                first.End) > lateralTolerance)
                        {
                            continue;
                        }

                        Point3d[] firstEnds = { first.Start, first.End };
                        Point3d[] secondEnds = { second.Start, second.End };
                        int firstIndex = 0;
                        int secondIndex = 0;
                        double nearest = double.MaxValue;

                        for (int a = 0; a < 2; a++)
                        {
                            for (int b = 0; b < 2; b++)
                            {
                                double distance = PlanDistance(
                                    firstEnds[a],
                                    secondEnds[b]);

                                if (distance < nearest)
                                {
                                    nearest = distance;
                                    firstIndex = a;
                                    secondIndex = b;
                                }
                            }
                        }

                        double maxGap = Math.Min(
                            500.0,
                            Math.Max(120.0, width * 0.45));

                        if (nearest > maxGap)
                            continue;

                        Point3d target = MidPoint(
                            firstEnds[firstIndex],
                            secondEnds[secondIndex]);

                        if (firstIndex == 0)
                            first.Start = target;
                        else
                            first.End = target;

                        if (secondIndex == 0)
                            second.Start = target;
                        else
                            second.End = target;

                        first.LengthMm = first.Start.DistanceTo(first.End);
                        second.LengthMm = second.Start.DistanceTo(second.End);
                        changed = true;
                    }
                }

                if (!changed)
                    break;
            }
        }

        private static bool HaveSameOverlayStyle(
            MepDuctSegment first,
            MepDuctSegment second)
        {
            if (first == null || second == null)
                return false;

            return string.Equals(
                       BuildOverlayLayerName(first),
                       BuildOverlayLayerName(second),
                       StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(
                       first.Shape ?? "",
                       second.Shape ?? "",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTrustedDuctSegment(MepDuctSegment segment)
        {
            if (segment == null ||
                string.IsNullOrWhiteSpace(segment.Size) ||
                segment.LengthMm < MinCurveLength ||
                segment.Confidence < MinTrustedConfidence)
            {
                return false;
            }

            double planWidth = GetOverlayPlanWidth(segment);

            if (planWidth < 50.0 || planWidth > 5000.0)
                return false;

            bool ductLayer =
                MepDuctSizeParser.HasDuctContext(segment.Layer) &&
                !MepDuctSizeParser.HasShaftContext(segment.Layer);
            bool isInferredFromCadWidth =
                string.Equals(
                    segment.Representation,
                    "INFERRED_FROM_CAD_WIDTH",
                    StringComparison.OrdinalIgnoreCase);

            if (isInferredFromCadWidth)
            {
                return ductLayer &&
                       segment.MeasuredPlanWidth > 0.0 &&
                       segment.LengthMm >= planWidth * 1.35;
            }

            bool isInherited =
                string.Equals(
                    segment.Representation,
                    "INHERITED",
                    StringComparison.OrdinalIgnoreCase);

            if (isInherited &&
                !ductLayer &&
                string.IsNullOrWhiteSpace(segment.SystemCode))
            {
                return false;
            }

            // Một "tuyến" ngắn hơn chính bề rộng của nó thường là outline
            // thiết bị, co hoặc côn. Không tô ConstantWidth lên vùng đó.
            double minimumRunRatio = segment.HasExplicitSize ? 0.75 : 1.00;
            return segment.LengthMm >= planWidth * minimumRunRatio;
        }

        private static double GetOverlayPlanWidth(MepDuctSegment segment)
        {
            if (segment == null)
                return 0.0;

            if (segment.MeasuredPlanWidth > 0.0)
                return segment.MeasuredPlanWidth;

            if (string.Equals(segment.Shape, "ROUND", StringComparison.OrdinalIgnoreCase))
                return segment.DiameterMm;

            if (segment.WidthMm > 0.0)
                return segment.WidthMm;

            return segment.MaxDimensionMm;
        }

        // ============================================================
        // 7. VẼ OVERLAY LÊN AUTOCAD VỚI BỀ RỘNG THỰC TẾ & ĐỘ TRONG SUỐT 60%
        // ============================================================

        private static void DrawOverlay(
            Transaction tr,
            Database db,
            BlockTableRecord space,
            MepDuctScanResult result)
        {
            if (result?.Segments == null)
                return;

            List<MepDuctSegment> trusted = result.Segments
                .Where(IsTrustedDuctSegment)
                .ToList();

            // Các segment cùng system/size/EI được dựng thành một graph nhỏ.
            // Qua node bậc 2 vẽ MỘT wide polyline liên tục; góc 90° được thay
            // bằng cung bo có bán kính theo bề rộng ống thay vì góc vuông sắc.
            foreach (OverlayPath path in BuildOverlayPaths(trusted))
            {
                if (path?.Template == null ||
                    path.Points == null ||
                    path.Points.Count < 2 ||
                    path.Width < 50.0 ||
                    path.Width > 5000.0)
                {
                    continue;
                }

                List<OverlayVertex> overlayVertices =
                    BuildRoundedOverlayVertices(path);

                if (overlayVertices.Count < 2)
                    continue;

                string layerName = BuildOverlayLayerName(path.Template);
                EnsureLayerWithTransparency(
                    tr,
                    db,
                    layerName,
                    GetLayerColor(layerName),
                    DuctTransparencyAlpha);

                Polyline polyline = new Polyline();
                polyline.SetDatabaseDefaults(db);

                for (int index = 0; index < overlayVertices.Count; index++)
                {
                    OverlayVertex vertex = overlayVertices[index];
                    Point3d point = vertex.Point;
                    polyline.AddVertexAt(
                        index,
                        new Point2d(point.X, point.Y),
                        vertex.Bulge,
                        0.0,
                        0.0);
                }

                polyline.Elevation = overlayVertices[0].Point.Z;
                polyline.ConstantWidth = path.Width;
                polyline.Closed = path.Closed && overlayVertices.Count >= 3;
                polyline.Layer = layerName;
                polyline.ColorIndex = 256;

                space.AppendEntity(polyline);
                tr.AddNewlyCreatedDBObject(polyline, true);

                try
                {
                    polyline.Transparency =
                        new Transparency(DuctTransparencyAlpha);
                }
                catch
                {
                }
            }

            List<Point3d> labeledPositions = new List<Point3d>();
            const double MinLabelSpacingMm = 2500.0;

            // Label vẫn lấy theo segment có text trực tiếp để không lặp nhãn
            // trên các đoạn chỉ được truyền size.
            foreach (MepDuctSegment seg in trusted)
            {
                double overlayWidth = GetOverlayPlanWidth(seg);
                string layerName = BuildOverlayLayerName(seg);

                // Label: chỉ đoạn có annotation trực tiếp, đủ dài, không chồng
                bool isDirectAnnotation =
                    seg.HasExplicitSize &&
                    !string.Equals(seg.Representation, "INHERITED", StringComparison.OrdinalIgnoreCase);

                if (isDirectAnnotation && seg.LengthMm >= 1500.0 &&
                    labeledPositions.All(p => p.DistanceTo(seg.Center) > MinLabelSpacingMm))
                {
                    string labelText = BuildOverlayLabel(seg);
                    double textH = Math.Max(100.0, Math.Min(300.0, overlayWidth * 0.28));

                    DBText lbl = new DBText();
                    lbl.SetDatabaseDefaults(db);
                    lbl.TextStyleId = db.Textstyle;
                    lbl.TextString = labelText;
                    lbl.Height = textH;
                    lbl.Layer = layerName;
                    lbl.ColorIndex = 256;
                    lbl.Justify = AttachmentPoint.MiddleCenter;
                    lbl.AlignmentPoint = seg.Center;
                    lbl.Position = seg.Center;

                    double angle = PlanAngle(seg.Start, seg.End);
                    if (angle > Math.PI * 0.5 && angle <= Math.PI * 1.5) angle -= Math.PI;
                    else if (angle < -Math.PI * 0.5 && angle >= -Math.PI * 1.5) angle += Math.PI;
                    lbl.Rotation = angle;

                    space.AppendEntity(lbl);
                    tr.AddNewlyCreatedDBObject(lbl, true);
                    labeledPositions.Add(seg.Center);
                }
            }
        }

        private static List<OverlayVertex> BuildRoundedOverlayVertices(
            OverlayPath path)
        {
            List<OverlayVertex> result =
                new List<OverlayVertex>();

            if (path?.Points == null || path.Points.Count < 2)
                return result;

            List<Point3d> points = new List<Point3d>();

            foreach (Point3d point in path.Points)
            {
                if (points.Count == 0 ||
                    PlanDistance(points[points.Count - 1], point) > 1e-6)
                {
                    points.Add(point);
                }
            }

            if (path.Closed &&
                points.Count > 2 &&
                PlanDistance(points[0], points[points.Count - 1]) <= 1e-6)
            {
                points.RemoveAt(points.Count - 1);
            }

            if (points.Count < 2)
                return result;

            if (points.Count == 2)
            {
                AddOverlayVertex(result, points[0], 0.0);
                AddOverlayVertex(result, points[1], 0.0);
                return result;
            }

            if (path.Closed)
            {
                for (int index = 0; index < points.Count; index++)
                {
                    Point3d previous =
                        points[(index - 1 + points.Count) % points.Count];

                    Point3d corner = points[index];
                    Point3d next = points[(index + 1) % points.Count];

                    if (TryCreateRoundedNinetyCorner(
                            previous,
                            corner,
                            next,
                            path.Width,
                            out Point3d entry,
                            out Point3d exit,
                            out double bulge))
                    {
                        AddOverlayVertex(result, entry, bulge);
                        AddOverlayVertex(result, exit, 0.0);
                    }
                    else
                    {
                        AddOverlayVertex(result, corner, 0.0);
                    }
                }

                return result;
            }

            AddOverlayVertex(result, points[0], 0.0);

            for (int index = 1; index < points.Count - 1; index++)
            {
                Point3d previous = points[index - 1];
                Point3d corner = points[index];
                Point3d next = points[index + 1];

                if (TryCreateRoundedNinetyCorner(
                        previous,
                        corner,
                        next,
                        path.Width,
                        out Point3d entry,
                        out Point3d exit,
                        out double bulge))
                {
                    AddOverlayVertex(result, entry, bulge);
                    AddOverlayVertex(result, exit, 0.0);
                }
                else
                {
                    AddOverlayVertex(result, corner, 0.0);
                }
            }

            AddOverlayVertex(result, points[points.Count - 1], 0.0);
            return result;
        }

        private static bool TryCreateRoundedNinetyCorner(
            Point3d previous,
            Point3d corner,
            Point3d next,
            double ductWidth,
            out Point3d entry,
            out Point3d exit,
            out double bulge)
        {
            entry = corner;
            exit = corner;
            bulge = 0.0;

            double incomingX = corner.X - previous.X;
            double incomingY = corner.Y - previous.Y;
            double outgoingX = next.X - corner.X;
            double outgoingY = next.Y - corner.Y;

            double incomingLength = Math.Sqrt(
                incomingX * incomingX + incomingY * incomingY);

            double outgoingLength = Math.Sqrt(
                outgoingX * outgoingX + outgoingY * outgoingY);

            if (incomingLength <= 1e-6 || outgoingLength <= 1e-6)
                return false;

            double uxIn = incomingX / incomingLength;
            double uyIn = incomingY / incomingLength;
            double uxOut = outgoingX / outgoingLength;
            double uyOut = outgoingY / outgoingLength;

            double dot = Math.Max(
                -1.0,
                Math.Min(1.0, uxIn * uxOut + uyIn * uyOut));

            double turnAngle = Math.Acos(dot);
            double minNinetyAngle = 70.0 * Math.PI / 180.0;
            double maxNinetyAngle = 110.0 * Math.PI / 180.0;

            // Chỉ bo các co gần 90°. Co 45° và các nút khác giữ nguyên.
            if (turnAngle < minNinetyAngle || turnAngle > maxNinetyAngle)
                return false;

            double cross = uxIn * uyOut - uyIn * uxOut;

            if (Math.Abs(cross) <= 1e-6)
                return false;

            double safeWidth = Math.Max(50.0, ductWidth);
            double desiredRadius = Math.Max(
                180.0,
                Math.Min(1400.0, safeWidth * 0.65));

            double tangentFactor = Math.Tan(turnAngle * 0.5);

            if (tangentFactor <= 1e-6)
                return false;

            double desiredSetback = desiredRadius * tangentFactor;
            double availableSetback =
                Math.Min(incomingLength, outgoingLength) * 0.42;

            double setback = Math.Min(desiredSetback, availableSetback);
            double actualRadius = setback / tangentFactor;

            // Không cố bo khi hai chân co quá ngắn: bán kính nhỏ hơn nửa
            // bề rộng sẽ làm mép trong wide polyline tự chồng lên nhau.
            if (actualRadius < Math.Max(90.0, safeWidth * 0.52))
                return false;

            entry = new Point3d(
                corner.X - uxIn * setback,
                corner.Y - uyIn * setback,
                corner.Z);

            exit = new Point3d(
                corner.X + uxOut * setback,
                corner.Y + uyOut * setback,
                corner.Z);

            bulge =
                Math.Sign(cross) *
                Math.Tan(turnAngle * 0.25);

            return Math.Abs(bulge) > 1e-6;
        }

        private static void AddOverlayVertex(
            List<OverlayVertex> vertices,
            Point3d point,
            double bulge)
        {
            if (vertices == null)
                return;

            if (vertices.Count > 0 &&
                PlanDistance(vertices[vertices.Count - 1].Point, point) <= 1e-6)
            {
                vertices[vertices.Count - 1].Point = point;

                if (Math.Abs(bulge) > 1e-9)
                    vertices[vertices.Count - 1].Bulge = bulge;

                return;
            }

            vertices.Add(new OverlayVertex
            {
                Point = point,
                Bulge = bulge
            });
        }

        private static List<OverlayPath> BuildOverlayPaths(
            List<MepDuctSegment> segments)
        {
            List<OverlayPath> result = new List<OverlayPath>();

            if (segments == null || segments.Count == 0)
                return result;

            foreach (IGrouping<string, MepDuctSegment> group in segments
                .Where(x => x != null)
                .GroupBy(
                    BuildOverlayLayerName,
                    StringComparer.OrdinalIgnoreCase))
            {
                List<MepDuctSegment> groupSegments = group.ToList();
                List<double> widths = groupSegments
                    .Select(GetOverlayPlanWidth)
                    .Where(x => x >= 50.0 && x <= 5000.0)
                    .OrderBy(x => x)
                    .ToList();

                if (widths.Count == 0)
                    continue;

                double pathWidth = widths[widths.Count / 2];
                double nodeTolerance = Math.Max(
                    35.0,
                    Math.Min(180.0, pathWidth * 0.18));

                List<OverlayGraphNode> nodes =
                    new List<OverlayGraphNode>();

                List<OverlayGraphEdge> edges =
                    new List<OverlayGraphEdge>();

                foreach (MepDuctSegment segment in groupSegments)
                {
                    int nodeA = FindOrCreateOverlayNode(
                        nodes,
                        segment.Start,
                        nodeTolerance);

                    int nodeB = FindOrCreateOverlayNode(
                        nodes,
                        segment.End,
                        nodeTolerance);

                    if (nodeA == nodeB)
                        continue;

                    int edgeId = edges.Count;
                    edges.Add(new OverlayGraphEdge
                    {
                        Segment = segment,
                        NodeA = nodeA,
                        NodeB = nodeB
                    });

                    nodes[nodeA].EdgeIds.Add(edgeId);
                    nodes[nodeB].EdgeIds.Add(edgeId);
                }

                // Đi từ đầu hở/điểm nhánh trước. Node bậc 2 được đi xuyên
                // qua để ghép thành một polyline duy nhất.
                for (int nodeId = 0; nodeId < nodes.Count; nodeId++)
                {
                    if (nodes[nodeId].EdgeIds.Count == 2)
                        continue;

                    foreach (int edgeId in nodes[nodeId].EdgeIds.ToList())
                    {
                        if (edgeId < 0 || edgeId >= edges.Count || edges[edgeId].Used)
                            continue;

                        OverlayPath path = WalkOverlayPath(
                            nodes,
                            edges,
                            nodeId,
                            edgeId,
                            pathWidth);

                        if (path != null)
                            result.Add(path);
                    }
                }

                // Phần còn lại là loop kín: chọn một cạnh bất kỳ làm điểm đầu.
                for (int edgeId = 0; edgeId < edges.Count; edgeId++)
                {
                    if (edges[edgeId].Used)
                        continue;

                    OverlayPath path = WalkOverlayPath(
                        nodes,
                        edges,
                        edges[edgeId].NodeA,
                        edgeId,
                        pathWidth);

                    if (path != null)
                        result.Add(path);
                }
            }

            return result;
        }

        private static int FindOrCreateOverlayNode(
            List<OverlayGraphNode> nodes,
            Point3d point,
            double tolerance)
        {
            int bestIndex = -1;
            double bestDistance = double.MaxValue;

            for (int index = 0; index < nodes.Count; index++)
            {
                double distance = PlanDistance(nodes[index].Position, point);

                if (distance <= tolerance && distance < bestDistance)
                {
                    bestIndex = index;
                    bestDistance = distance;
                }
            }

            if (bestIndex < 0)
            {
                nodes.Add(new OverlayGraphNode
                {
                    Position = point,
                    SampleCount = 1
                });

                return nodes.Count - 1;
            }

            OverlayGraphNode node = nodes[bestIndex];
            int oldCount = Math.Max(1, node.SampleCount);
            int newCount = oldCount + 1;

            node.Position = new Point3d(
                (node.Position.X * oldCount + point.X) / newCount,
                (node.Position.Y * oldCount + point.Y) / newCount,
                (node.Position.Z * oldCount + point.Z) / newCount);

            node.SampleCount = newCount;
            return bestIndex;
        }

        private static OverlayPath WalkOverlayPath(
            List<OverlayGraphNode> nodes,
            List<OverlayGraphEdge> edges,
            int startNode,
            int startEdge,
            double width)
        {
            if (startNode < 0 || startNode >= nodes.Count ||
                startEdge < 0 || startEdge >= edges.Count)
            {
                return null;
            }

            OverlayPath path = new OverlayPath
            {
                Template = edges[startEdge].Segment,
                Width = width
            };

            int currentNode = startNode;
            int currentEdge = startEdge;
            path.Points.Add(nodes[currentNode].Position);

            while (currentEdge >= 0 && currentEdge < edges.Count)
            {
                OverlayGraphEdge edge = edges[currentEdge];

                if (edge.Used)
                    break;

                edge.Used = true;

                int nextNode =
                    edge.NodeA == currentNode
                        ? edge.NodeB
                        : edge.NodeA;

                if (nextNode < 0 || nextNode >= nodes.Count)
                    break;

                Point3d nextPoint = nodes[nextNode].Position;

                if (path.Points.Count == 0 ||
                    PlanDistance(path.Points[path.Points.Count - 1], nextPoint) > 1e-6)
                {
                    path.Points.Add(nextPoint);
                }

                if (nextNode == startNode)
                {
                    path.Closed = path.Points.Count >= 4;
                    break;
                }

                if (nodes[nextNode].EdgeIds.Count != 2)
                    break;

                int followingEdge = nodes[nextNode].EdgeIds
                    .FirstOrDefault(id =>
                        id >= 0 &&
                        id < edges.Count &&
                        id != currentEdge &&
                        !edges[id].Used);

                // FirstOrDefault trả 0 khi không có; cần kiểm tra lại rõ ràng.
                bool hasFollowing = nodes[nextNode].EdgeIds.Any(id =>
                    id >= 0 &&
                    id < edges.Count &&
                    id != currentEdge &&
                    !edges[id].Used);

                if (!hasFollowing)
                    break;

                currentNode = nextNode;
                currentEdge = followingEdge;
            }

            if (path.Closed &&
                path.Points.Count > 1 &&
                PlanDistance(path.Points[0], path.Points[path.Points.Count - 1]) <= 1e-6)
            {
                path.Points.RemoveAt(path.Points.Count - 1);
            }

            return path.Points.Count >= 2 ? path : null;
        }

        private static void DeleteOldOverlay(
            Transaction tr,
            BlockTableRecord space)
        {
            foreach (ObjectId id in space)
            {
                Entity ent = null;
                try
                {
                    ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                }
                catch
                {
                    continue;
                }

                if (ent == null || ent.IsErased || !IsAiDuctOutputLayer(ent.Layer))
                    continue;

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

        private static bool IsAiDuctOutputLayer(string layer)
        {
            string value = (layer ?? "").Trim();
            return value.StartsWith(OverlayPrefix, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, FittingLayerName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, CheckLayer, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildOverlayLayerName(MepDuctSegment segment)
        {
            string system = string.IsNullOrWhiteSpace(segment?.SystemCode) ? "DUCT" : segment.SystemCode;
            string size = segment?.Size ?? "UNKNOWN";
            string fire = segment?.FireRating ?? "";

            return SanitizeLayerName(
                OverlayPrefix + system + "_" + size + (string.IsNullOrWhiteSpace(fire) ? "" : "_" + fire));
        }

        private static string BuildOverlayLabel(MepDuctSegment segment)
        {
            string text = (string.IsNullOrWhiteSpace(segment?.SystemCode) ? "DUCT" : segment.SystemCode) +
                          " " + (segment?.Size ?? "");

            if (!string.IsNullOrWhiteSpace(segment?.FireRating))
            {
                text += " " + segment.FireRating;
            }

            return text;
        }

        private static string SanitizeLayerName(string value)
        {
            string s = (value ?? "")
                .Trim()
                .Replace("×", "x")
                .Replace("Ø", "DIA")
                .Replace("Φ", "DIA");

            char[] invalid = { '<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', '=', '`' };
            foreach (char c in invalid)
            {
                s = s.Replace(c, '_');
            }

            while (s.Contains("__"))
                s = s.Replace("__", "_");

            return s.Length <= 180 ? s : s.Substring(0, 180);
        }

        private static void EnsureLayerWithTransparency(
            Transaction tr,
            Database db,
            string layerName,
            short aci,
            byte alpha)
        {
            LayerTable table = tr.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;
            if (table == null)
                return;

            if (table.Has(layerName))
            {
                try
                {
                    ObjectId layerId = table[layerName];
                    LayerTableRecord rec = tr.GetObject(layerId, OpenMode.ForWrite) as LayerTableRecord;
                    if (rec != null)
                    {
                        rec.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                            Autodesk.AutoCAD.Colors.ColorMethod.ByAci,
                            aci);
                        rec.Transparency = new Transparency(alpha);
                    }
                }
                catch
                {
                }
                return;
            }

            table.UpgradeOpen();
            LayerTableRecord record = new LayerTableRecord
            {
                Name = layerName,
                Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                    Autodesk.AutoCAD.Colors.ColorMethod.ByAci, aci)
            };

            table.Add(record);
            tr.AddNewlyCreatedDBObject(record, true);
            
            // LƯU Ý QUAN TRỌNG: Phải set Transparency SAU KHI đã add vào database để tránh lỗi eNoDatabase
            try
            {
                record.Transparency = new Transparency(alpha);
            }
            catch { }
        }

        private static short GetLayerColor(string layerName)
        {
            string upper = (layerName ?? "").ToUpperInvariant();

            if (upper.Contains("SA") || upper.Contains("CAP"))
                return 4; // Cyan (Gió cấp)
            if (upper.Contains("RA") || upper.Contains("HOI"))
                return 3; // Green (Gió hồi)
            if (upper.Contains("EA") || upper.Contains("THAI"))
                return 1; // Red / Magenta (Gió thải)
            if (upper.Contains("FA") || upper.Contains("OA") || upper.Contains("TUOI"))
                return 2; // Yellow (Gió tươi)
            if (upper.Contains("SMOKE") || upper.Contains("KHOI") || upper.Contains("SEF"))
                return 6; // Magenta (Hút khói)
            if (upper.Contains("PA") || upper.Contains("TANG"))
                return 30; // Orange (Tăng áp)

            unchecked
            {
                int hash = 17;
                foreach (char c in layerName ?? "")
                {
                    hash = hash * 31 + c;
                }

                short[] colors = { 1, 2, 3, 4, 5, 6, 30, 40, 50, 80, 90, 110, 120, 140, 170, 200, 210, 220 };
                int index = (hash & 0x7FFFFFFF) % colors.Length;
                return colors[index];
            }
        }

        // ============================================================
        // 8. HÌNH HỌC TÍNH TOÁN HỖ TRỢ
        // ============================================================

        private static bool TryIntersectRays2D(
            Point3d p1, Point3d p2,
            Point3d q1, Point3d q2,
            out Point3d intersect)
        {
            intersect = Point3d.Origin;

            double dx1 = p2.X - p1.X;
            double dy1 = p2.Y - p1.Y;
            double dx2 = q2.X - q1.X;
            double dy2 = q2.Y - q1.Y;

            double det = dx1 * dy2 - dy1 * dx2;
            if (Math.Abs(det) < 1e-6)
                return false;

            double t1 = ((q1.X - p1.X) * dy2 - (q1.Y - p1.Y) * dx2) / det;
            intersect = new Point3d(p1.X + t1 * dx1, p1.Y + t1 * dy1, (p2.Z + q2.Z) * 0.5);
            return true;
        }

        private static Point3d ProjectPointOnLine2D(Point3d p, Point3d lineStart, Point3d lineEnd)
        {
            double vx = lineEnd.X - lineStart.X;
            double vy = lineEnd.Y - lineStart.Y;
            double vv = vx * vx + vy * vy;
            if (vv <= 1e-9)
                return lineStart;

            double wx = p.X - lineStart.X;
            double wy = p.Y - lineStart.Y;
            double t = (wx * vx + wy * vy) / vv;

            return new Point3d(lineStart.X + t * vx, lineStart.Y + t * vy, p.Z);
        }

        private static double ProjectionParameter2D(
            Point3d point,
            Point3d lineStart,
            Point3d lineEnd)
        {
            double vx = lineEnd.X - lineStart.X;
            double vy = lineEnd.Y - lineStart.Y;
            double vv = vx * vx + vy * vy;

            if (vv <= 1e-9)
                return 0.0;

            return
                ((point.X - lineStart.X) * vx +
                 (point.Y - lineStart.Y) * vy) /
                vv;
        }

        private static bool IsPointWithinSegmentBounds(Point3d p, Point3d a, Point3d b, double tolerance)
        {
            double minX = Math.Min(a.X, b.X) - tolerance;
            double maxX = Math.Max(a.X, b.X) + tolerance;
            double minY = Math.Min(a.Y, b.Y) - tolerance;
            double maxY = Math.Max(a.Y, b.Y) + tolerance;

            return p.X >= minX && p.X <= maxX && p.Y >= minY && p.Y <= maxY;
        }

        private static double MinDistanceBetweenEndpoints(MepDuctSegment s1, MepDuctSegment s2)
        {
            double d1 = PlanDistance(s1.Start, s2.Start);
            double d2 = PlanDistance(s1.Start, s2.End);
            double d3 = PlanDistance(s1.End, s2.Start);
            double d4 = PlanDistance(s1.End, s2.End);

            return Math.Min(Math.Min(d1, d2), Math.Min(d3, d4));
        }

        private static bool TryGetClosedRectangleCenterline(
            Polyline pl,
            out Point3d start,
            out Point3d end,
            out double width)
        {
            start = Point3d.Origin;
            end = Point3d.Origin;
            width = 0.0;

            if (pl == null || !pl.Closed || pl.NumberOfVertices != 4)
                return false;

            List<Point3d> points = new List<Point3d>();
            for (int i = 0; i < 4; i++)
            {
                points.Add(pl.GetPoint3dAt(i));
            }

            List<(Point3d A, Point3d B, double Length, Point3d Mid)> edges =
                new List<(Point3d, Point3d, double, Point3d)>();

            for (int i = 0; i < 4; i++)
            {
                Point3d a = points[i];
                Point3d b = points[(i + 1) % 4];
                edges.Add((a, b, a.DistanceTo(b), MidPoint(a, b)));
            }

            double max = edges.Max(x => x.Length);
            double min = edges.Min(x => x.Length);

            if (max < 60.0 || min < 20.0 || max / Math.Max(1.0, min) < 1.35)
                return false;

            var shortEdges = edges.OrderBy(x => x.Length).Take(2).ToList();
            if (shortEdges.Count != 2)
                return false;

            double parallel = ParallelAngleDifference(
                PlanAngle(shortEdges[0].A, shortEdges[0].B),
                PlanAngle(shortEdges[1].A, shortEdges[1].B));

            if (parallel > Math.PI / 15.0)
                return false;

            start = shortEdges[0].Mid;
            end = shortEdges[1].Mid;
            width = shortEdges.Average(e => e.Length);

            return start.DistanceTo(end) >= MinCurveLength;
        }

        private static void BuildCenterlineBetweenParallelCurves(
            Point3d aStart, Point3d aEnd,
            Point3d bStart, Point3d bEnd,
            out Point3d centerStart,
            out Point3d centerEnd)
        {
            double direct = aStart.DistanceTo(bStart) + aEnd.DistanceTo(bEnd);
            double reverse = aStart.DistanceTo(bEnd) + aEnd.DistanceTo(bStart);

            if (reverse < direct)
            {
                Point3d tmp = bStart;
                bStart = bEnd;
                bEnd = tmp;
            }

            centerStart = MidPoint(aStart, bStart);
            centerEnd = MidPoint(aEnd, bEnd);
        }

        private static double SegmentOverlapRatio(
            Point3d a1, Point3d a2,
            Point3d b1, Point3d b2)
        {
            Vector2d dir = new Vector2d(a2.X - a1.X, a2.Y - a1.Y);
            if (dir.Length < 1e-6)
                return 0.0;

            dir = dir.GetNormal();

            double a0 = 0.0;
            double aEnd = new Vector2d(a2.X - a1.X, a2.Y - a1.Y).DotProduct(dir);
            double bStart = new Vector2d(b1.X - a1.X, b1.Y - a1.Y).DotProduct(dir);
            double bEnd = new Vector2d(b2.X - a1.X, b2.Y - a1.Y).DotProduct(dir);

            double aMin = Math.Min(a0, aEnd);
            double aMax = Math.Max(a0, aEnd);
            double bMin = Math.Min(bStart, bEnd);
            double bMax = Math.Max(bStart, bEnd);

            double overlap = Math.Max(0.0, Math.Min(aMax, bMax) - Math.Max(aMin, bMin));
            double denom = Math.Max(1.0, Math.Min(aMax - aMin, bMax - bMin));

            return Math.Max(0.0, Math.Min(1.0, overlap / denom));
        }

        private static double ParallelSeparation2D(
            Point3d a1, Point3d a2,
            Point3d b1, Point3d b2)
        {
            Point3d midB = MidPoint(b1, b2);
            return DistancePointToSegment2D(midB, a1, a2);
        }

        private static Point3d MidPoint(Point3d a, Point3d b)
        {
            return new Point3d((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, (a.Z + b.Z) * 0.5);
        }

        private static double PlanAngle(Point3d a, Point3d b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            if (Math.Sqrt(dx * dx + dy * dy) < 1e-9)
                return 0.0;

            return Math.Atan2(dy, dx);
        }

        private static double ParallelAngleDifference(double a, double b)
        {
            double diff = Math.Abs(NormalizeAngle(a) - NormalizeAngle(b));
            while (diff > Math.PI)
                diff -= Math.PI;

            diff = Math.Abs(diff);
            return Math.Min(diff, Math.PI - diff);
        }

        private static double NormalizeAngle(double value)
        {
            while (value < 0.0)
                value += Math.PI * 2.0;
            while (value >= Math.PI * 2.0)
                value -= Math.PI * 2.0;
            return value;
        }

        private static double DistancePointToSegment2D(Point3d p, Point3d a, Point3d b)
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

            Point3d q = new Point3d(a.X + vx * t, a.Y + vy * t, p.Z);
            return PlanDistance(p, q);
        }

        private static double PerpendicularDistanceToLine2D(
            Point3d point,
            Point3d lineStart,
            Point3d lineEnd)
        {
            double vx = lineEnd.X - lineStart.X;
            double vy = lineEnd.Y - lineStart.Y;
            double length = Math.Sqrt(vx * vx + vy * vy);

            if (length <= 1e-9)
                return PlanDistance(point, lineStart);

            double cross =
                Math.Abs(
                    vx * (lineStart.Y - point.Y) -
                    (lineStart.X - point.X) * vy);

            return cross / length;
        }

        private static double PlanDistance(Point3d a, Point3d b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double SegmentCenterDistance(MepDuctSegment a, MepDuctSegment b)
        {
            if (a == null || b == null)
                return double.MaxValue;

            return PlanDistance(a.Center, b.Center);
        }

        private static double GetCurveLength(Curve curve)
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

                return curve.GetDistanceAtParameter(curve.EndParam);
            }
            catch
            {
                try
                {
                    return curve.StartPoint.DistanceTo(curve.EndPoint);
                }
                catch
                {
                    return 0.0;
                }
            }
        }

        private static MepDuctTextSeed ChooseBetterSeed(CurveCandidate a, CurveCandidate b)
        {
            if (a?.Seed == null) return b?.Seed;
            if (b?.Seed == null) return a.Seed;
            return a.SeedDistance <= b.SeedDistance ? a.Seed : b.Seed;
        }

        private static string PreferLayer(CurveCandidate a, CurveCandidate b)
        {
            if (a != null && a.LayerLooksDuct) return a.Layer ?? "";
            if (b != null && b.LayerLooksDuct) return b.Layer ?? "";
            return a?.Layer ?? b?.Layer ?? "";
        }

        private static List<MepDuctSegment> DeduplicateSegments(List<MepDuctSegment> segments)
        {
            List<MepDuctSegment> output = new List<MepDuctSegment>();

            foreach (MepDuctSegment candidate in segments
                .Where(x => x != null)
                .OrderByDescending(x => x.HasExplicitSize)
                .ThenByDescending(x => x.Confidence)
                .ThenByDescending(x => x.LengthMm))
            {
                bool duplicate = output.Any(existing =>
                    AreDuplicateSegments(existing, candidate));

                if (!duplicate)
                    output.Add(candidate);
            }

            return output;
        }

        private static bool AreDuplicateSegments(
            MepDuctSegment first,
            MepDuctSegment second)
        {
            if (first == null || second == null ||
                !AreSystemsCompatible(first, second))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(first.Size) &&
                !string.IsNullOrWhiteSpace(second.Size) &&
                !string.Equals(first.Size, second.Size, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (first.MeasuredPlanWidth > 0.0 &&
                second.MeasuredPlanWidth > 0.0 &&
                Math.Abs(first.MeasuredPlanWidth - second.MeasuredPlanWidth) > 70.0)
            {
                return false;
            }

            const double EndpointTolerance = 80.0;

            bool sameEndpoints =
                (PlanDistance(first.Start, second.Start) <= EndpointTolerance &&
                 PlanDistance(first.End, second.End) <= EndpointTolerance) ||
                (PlanDistance(first.Start, second.End) <= EndpointTolerance &&
                 PlanDistance(first.End, second.Start) <= EndpointTolerance);

            if (sameEndpoints)
                return true;

            double angleDifference = ParallelAngleDifference(
                PlanAngle(first.Start, first.End),
                PlanAngle(second.Start, second.End));

            if (angleDifference > Math.PI / 36.0)
                return false;

            double separation = ParallelSeparation2D(
                first.Start,
                first.End,
                second.Start,
                second.End);

            if (separation > 50.0)
                return false;

            return SegmentOverlapRatio(
                first.Start,
                first.End,
                second.Start,
                second.End) >= 0.85;
        }

        private static void ReindexSegments(List<MepDuctSegment> segments)
        {
            if (segments == null)
                return;

            for (int i = 0; i < segments.Count; i++)
            {
                if (segments[i] != null)
                    segments[i].Id = i;
            }
        }

        private static List<MepDuctTakeoffRow> BuildStats(List<MepDuctSegment> segments)
        {
            if (segments == null)
                return new List<MepDuctTakeoffRow>();

            return segments
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Size))
                .GroupBy(x => new
                {
                    System = x.SystemCode ?? "",
                    Size = x.Size ?? "",
                    Shape = x.Shape ?? "",
                    Fire = x.FireRating ?? ""
                })
                .Select(g => new MepDuctTakeoffRow
                {
                    SystemCode = g.Key.System,
                    Size = g.Key.Size,
                    Shape = g.Key.Shape,
                    FireRating = g.Key.Fire,
                    SegmentCount = g.Count(),
                    LengthMm = g.Sum(x => x.LengthMm),
                    SurfaceAreaM2 = g.Sum(x => x.SurfaceAreaM2),
                    AverageConfidence = g.Select(x => x.Confidence).DefaultIfEmpty(0.0).Average()
                })
                .OrderBy(x => x.SystemCode)
                .ThenByDescending(x => ParseSortDimension(x.Size))
                .ThenBy(x => x.FireRating)
                .ToList();
        }

        private static List<Point3d> BuildReferencePoints(Point3d insertion, Extents3d? extents)
        {
            List<Point3d> points = new List<Point3d> { insertion };
            if (!extents.HasValue)
                return points;

            Extents3d ex = extents.Value;
            double cx = (ex.MinPoint.X + ex.MaxPoint.X) * 0.5;
            double cy = (ex.MinPoint.Y + ex.MaxPoint.Y) * 0.5;
            double cz = (ex.MinPoint.Z + ex.MaxPoint.Z) * 0.5;

            points.Add(new Point3d(cx, cy, cz));
            points.Add(new Point3d(ex.MinPoint.X, cy, cz));
            points.Add(new Point3d(ex.MaxPoint.X, cy, cz));
            points.Add(new Point3d(cx, ex.MinPoint.Y, cz));
            points.Add(new Point3d(cx, ex.MaxPoint.Y, cz));

            return points;
        }

        private static double ParseSortDimension(string size)
        {
            if (!MepDuctSizeParser.TryParse(size, "DUCT", out MepDuctSizeInfo info))
                return 0.0;

            return info.MaxDimensionMm;
        }
    }
}
