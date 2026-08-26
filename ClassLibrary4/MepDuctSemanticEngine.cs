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
    /// - Kéo dài phụ kiện 50/50 vào đúng tâm giao điểm đỉnh.
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
        private const double MaxSeedDistanceBase = 1200.0;

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
            public double RectWidth { get; set; }
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

                // 1) Đọc text kích thước WxH, ØD, EI, Hệ thống
                List<MepDuctTextSeed> seeds = ReadDuctTextSeeds(tr, ids);
                result.DuctSizeTextCount = seeds.Count;

                // 2) Đọc tất cả đối tượng đường nét CAD
                List<CurveCandidate> curves = ReadCurveCandidates(tr, ids);
                result.RawCurveCount = curves.Count;

                // 3) Gán seed cho các đường phù hợp (kết hợp khoảng cách, góc và bề rộng thực tế)
                AttachBestSeeds(curves, seeds, ref result);

                // 4) Xây dựng các đoạn tim tuyến ban đầu (kể cả đoạn chưa có text)
                List<MepDuctSegment> segments = BuildInitialSegments(curves, ref result);

                // 5) Xây dựng Đồ Thị Tôpô & Nhận diện Phụ kiện (Co, Tê, Giảm, Gót Giày)
                List<MepDuctFitting> fittings = DetectFittingsAndBuildTopology(segments);
                result.Fittings = fittings;

                // 6) Lan truyền kích thước cho các đoạn CHƯA CÓ TEXT qua đồ thị
                int propagatedCount = PropagateSizesThroughTopology(segments, fittings, seeds);
                result.InheritedSizeCount = propagatedCount;

                // 7) Áp dụng quy tắc cắt nối 50/50 tại phụ kiện (Kéo dài tim tuyến gặp nhau tại tâm giao)
                ApplyFitting5050Adjustment(segments, fittings);

                // 8) Lọc các đoạn hợp lệ có kích thước và chiều dài đạt chuẩn
                segments = segments
                    .Where(x => x != null &&
                                !string.IsNullOrWhiteSpace(x.Size) &&
                                x.LengthMm >= MinCurveLength)
                    .ToList();

                for (int i = 0; i < segments.Count; i++)
                {
                    segments[i].Id = i;
                }

                // Loại bỏ đoạn trùng lặp
                segments = DeduplicateSegments(segments);

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
                " | Diện tích tôn: " + run.TotalAreaM2.ToString("0.00", CultureInfo.InvariantCulture) + " m²"
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

        private static List<CurveCandidate> ReadCurveCandidates(
            Transaction tr,
            ObjectId[] ids)
        {
            List<CurveCandidate> result = new List<CurveCandidate>();

            foreach (ObjectId id in ids)
            {
                Entity ent = SafeOpenEntity(tr, id);
                if (ent == null || IsAiDuctOutputLayer(ent.Layer))
                    continue;

                // STEP30E FIX: Loại trừ ngay các đối tượng thuộc Layer PCCC, Chữa cháy, Cấp thoát nước
                if (MepDuctSizeParser.HasPipeOrFireProtectionContext(ent.Layer) ||
                    MepDuctSizeParser.HasDnPipeText(ent.Layer))
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

                double length = GetCurveLength(curve);
                if (length < MinCurveLength)
                    continue;

                CurveCandidate candidate = new CurveCandidate
                {
                    Id = id,
                    Curve = curve,
                    Entity = ent,
                    Layer = ent.Layer ?? "",
                    Start = curve.StartPoint,
                    End = curve.EndPoint,
                    Length = length,
                    LayerLooksDuct = MepDuctSizeParser.HasDuctContext(ent.Layer)
                };

                if (ent is Polyline pl &&
                    TryGetClosedRectangleCenterline(pl, out Point3d centerStart, out Point3d centerEnd, out double rectWidth))
                {
                    candidate.ClosedRectangle = true;
                    candidate.RectCenterStart = centerStart;
                    candidate.RectCenterEnd = centerEnd;
                    candidate.RectWidth = rectWidth;
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
                    double maxDistance = Math.Max(MaxSeedDistanceBase, seed.Size.MaxDimensionMm * 1.5 + 300.0);

                    if (distance > maxDistance)
                        continue;

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

            HashSet<ObjectId> used = new HashSet<ObjectId>();
            int segIdCounter = 0;

            // 1) Khung chữ nhật khép kín
            foreach (CurveCandidate curve in curves.Where(c => c != null && c.ClosedRectangle))
            {
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
                    used.Add(curve.Id);
                    result.RectFrameCount++;
                }
            }

            // 2) Ghép cặp đường song song (Double-line)
            List<CurveCandidate> open = curves
                .Where(c => c != null && !c.ClosedRectangle && !used.Contains(c.Id))
                .OrderByDescending(c => c.Length)
                .ToList();

            for (int i = 0; i < open.Count; i++)
            {
                CurveCandidate a = open[i];
                if (a == null || used.Contains(a.Id))
                    continue;

                CurveCandidate bestPair = null;
                double bestPairScore = double.MaxValue;
                double bestSeparation = 0.0;

                for (int j = i + 1; j < open.Count; j++)
                {
                    CurveCandidate b = open[j];
                    if (b == null || used.Contains(b.Id))
                        continue;

                    double angle = ParallelAngleDifference(PlanAngle(a.Start, a.End), PlanAngle(b.Start, b.End));
                    if (angle > DoubleLineAngleRadians)
                        continue;

                    double overlap = SegmentOverlapRatio(a.Start, a.End, b.Start, b.End);
                    if (overlap < 0.35)
                        continue;

                    double separation = ParallelSeparation2D(a.Start, a.End, b.Start, b.End);
                    if (separation < 50.0 || separation > 4500.0)
                        continue;

                    // Kiểm tra xem khoảng cách 2 đường có khớp với text seed không
                    bool seedMatchesSeparation = true;
                    if (a.Seed?.Size != null)
                    {
                        double expW = a.Seed.Size.WidthMm;
                        double expH = a.Seed.Size.HeightMm;
                        double expD = a.Seed.Size.DiameterMm;
                        bool matchW = Math.Abs(expW - separation) / separation <= 0.25;
                        bool matchH = Math.Abs(expH - separation) / separation <= 0.25;
                        bool matchD = Math.Abs(expD - separation) / separation <= 0.25;
                        if (!matchW && !matchH && !matchD)
                            seedMatchesSeparation = false;
                    }

                    double score = (1.0 - overlap) * 300.0 + angle * 400.0;
                    if (!seedMatchesSeparation)
                        score += 500.0; // Penalty nếu text lệch nhiều so với bề rộng đo thực tế

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

                    // Nếu seed không khớp với khoảng cách thực tế của 2 đường nét, bỏ seed để topology lan truyền đúng size
                    if (seed?.Size != null)
                    {
                        double expW = seed.Size.WidthMm;
                        double expH = seed.Size.HeightMm;
                        double expD = seed.Size.DiameterMm;
                        bool matchW = Math.Abs(expW - bestSeparation) / bestSeparation <= 0.25;
                        bool matchH = Math.Abs(expH - bestSeparation) / bestSeparation <= 0.25;
                        bool matchD = Math.Abs(expD - bestSeparation) / bestSeparation <= 0.25;
                        if (!matchW && !matchH && !matchD)
                            seed = null; // Huỷ seed sai để lan truyền đúng
                    }

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
                        used.Add(a.Id);
                        used.Add(bestPair.Id);
                        result.DoubleLinePairCount++;
                    }

                    continue;
                }

                // 3) Tuyến đơn (Single line): Chỉ nhận nếu có Text Seed kích thước hoặc Layer rõ ràng là ống gió
                if (a.Seed != null || a.LayerLooksDuct)
                {
                    MepDuctSegment single = CreateSegmentFromCandidate(
                        segIdCounter++,
                        a.Start,
                        a.End,
                        a.Seed,
                        a.Layer,
                        0.0,
                        a.Seed != null ? 0.90 : 0.40,
                        "CENTERLINE",
                        new[] { a.Id },
                        a.LayerLooksDuct ? "duct layer" : "candidate line with duct seed");

                    if (single != null)
                    {
                        output.Add(single);
                        used.Add(a.Id);
                        result.SingleCenterlineCount++;
                    }
                }
            }

            return output;
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
                if (s1 == null) continue;

                for (int j = i + 1; j < count; j++)
                {
                    MepDuctSegment s2 = segments[j];
                    if (s2 == null) continue;

                    double angle = ParallelAngleDifference(PlanAngle(s1.Start, s1.End), PlanAngle(s2.Start, s2.End));

                    // CO: góc bẻ hướng từ 25° đến 155° (chuẩn 90° hoặc 45°)
                    if (angle >= Math.PI / 7.2 && angle <= Math.PI * 0.85)
                    {
                        if (TryIntersectRays2D(s1.Start, s1.End, s2.Start, s2.End, out Point3d pInt))
                        {
                            double maxAllowedDist = Math.Max(s1.MaxDimensionMm, s2.MaxDimensionMm) * 2.2 + 400.0;
                            double d1 = Math.Min(PlanDistance(s1.Start, pInt), PlanDistance(s1.End, pInt));
                            double d2 = Math.Min(PlanDistance(s2.Start, pInt), PlanDistance(s2.End, pInt));

                            if (d1 <= maxAllowedDist && d2 <= maxAllowedDist)
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
                if (branch == null) continue;

                for (int j = 0; j < count; j++)
                {
                    if (i == j) continue;
                    MepDuctSegment main = segments[j];
                    if (main == null) continue;

                    // Kiểm tra cả 2 đầu mút của branch xem đầu nào đâm vào main
                    Point3d[] branchEnds = { branch.Start, branch.End };
                    Point3d[] branchOthers = { branch.End, branch.Start };

                    for (int k = 0; k < 2; k++)
                    {
                        Point3d brAt = branchEnds[k];
                        Point3d brOther = branchOthers[k];

                        Point3d pProj = ProjectPointOnLine2D(brAt, main.Start, main.End);
                        double distFromMain = PlanDistance(brAt, pProj);

                        double maxBranchReach = main.MaxDimensionMm * 1.5 + 250.0;

                        if (distFromMain <= maxBranchReach && IsPointWithinSegmentBounds(pProj, main.Start, main.End, 150.0))
                        {
                            double angleToMain = ParallelAngleDifference(PlanAngle(brOther, brAt), PlanAngle(main.Start, main.End));
                            double angleDeg = Math.Round(angleToMain * 180.0 / Math.PI);

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

        /// <summary>
        /// Gom cụm (cluster) các fitting trong bán kính ClusterRadius.
        /// Mỗi cụm chỉ giữ lại 1 fitting ưu tiên nhất (Tee &gt; Elbow &gt; Reducer &gt; ShoeTap).
        /// Tránh vẽ chồng đống cross-marker đỏ tại 1 điểm nút.
        /// </summary>
        private static List<MepDuctFitting> DeduplicateFittings(List<MepDuctFitting> raw)
        {
            const double ClusterRadius = 350.0; // mm — nếu 2 fitting cách nhau dưới 350mm → cùng 1 nút vật lý

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

                    if (PlanDistance(fi.Position, sorted[j].Position) < ClusterRadius)
                        eliminated.Add(j);
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

                        double angle = ParallelAngleDifference(PlanAngle(s1.Start, s1.End), PlanAngle(s2.Start, s2.End));
                        if (angle <= Math.PI / 15.0) // Thẳng hàng
                        {
                            double d = MinDistanceBetweenEndpoints(s1, s2);
                            if (d <= 500.0)
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
            foreach (MepDuctSegment s in segments.Where(x => string.IsNullOrWhiteSpace(x.Size) && x.MeasuredPlanWidth >= 100.0))
            {
                double w = s.MeasuredPlanWidth;
                double h = Math.Round(w * 0.5); // Tỷ lệ tiêu chuẩn 2:1 nếu không có chiều cao
                s.WidthMm = w;
                s.HeightMm = h;
                s.Shape = "RECT";
                s.Size = MepDuctSizeParser.FormatMm(w) + "x" + MepDuctSizeParser.FormatMm(h);
                if (string.IsNullOrWhiteSpace(s.SystemCode))
                    s.SystemCode = MepDuctSizeParser.InferSystemCode(s.Layer);
                s.Confidence = 0.70;
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

                        // Kéo dài đầu gần nhất của s1 tới pApex
                        if (PlanDistance(s1.Start, pApex) <= PlanDistance(s1.End, pApex))
                            s1.Start = pApex;
                        else
                            s1.End = pApex;

                        // Kéo dài đầu gần nhất của s2 tới pApex
                        if (PlanDistance(s2.Start, pApex) <= PlanDistance(s2.End, pApex))
                            s2.Start = pApex;
                        else
                            s2.End = pApex;

                        s1.LengthMm = s1.Start.DistanceTo(s1.End);
                        s2.LengthMm = s2.Start.DistanceTo(s2.End);
                    }
                }
                // 2) Tê và Gót Giày (50/50): Nhánh rẽ kéo dài đâm trọn vẹn vào tim trục chính
                else if (f.Type == MepDuctFittingType.Tee || f.Type == MepDuctFittingType.ShoeTap)
                {
                    if (f.BranchSegmentId.HasValue && segMap.TryGetValue(f.BranchSegmentId.Value, out MepDuctSegment sBranch))
                    {
                        Point3d pJunc = f.Position;
                        if (PlanDistance(sBranch.Start, pJunc) <= PlanDistance(sBranch.End, pJunc))
                            sBranch.Start = pJunc;
                        else
                            sBranch.End = pJunc;

                        sBranch.LengthMm = sBranch.Start.DistanceTo(sBranch.End);
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

                        if (PlanDistance(s1.Start, pMid) <= PlanDistance(s1.End, pMid))
                            s1.Start = pMid;
                        else
                            s1.End = pMid;

                        if (PlanDistance(s2.Start, pMid) <= PlanDistance(s2.End, pMid))
                            s2.Start = pMid;
                        else
                            s2.End = pMid;

                        s1.LengthMm = s1.Start.DistanceTo(s1.End);
                        s2.LengthMm = s2.Start.DistanceTo(s2.End);
                    }
                }
            }
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

            var labeledPositions = new List<Point3d>();
            const double MinLabelSpacingMm = 2500.0;

            // Vẽ TẤT CẢ segment — không bỏ sót đoạn nào
            foreach (MepDuctSegment seg in result.Segments)
            {
                if (seg == null || seg.LengthMm < MinCurveLength)
                    continue;

                string layerName = BuildOverlayLayerName(seg);
                EnsureLayerWithTransparency(tr, db, layerName, GetLayerColor(layerName), DuctTransparencyAlpha);

                // Polyline ConstantWidth = cạnh lớn nhất ống gió (giống VẼ OG TỰ ĐỘNG)
                Polyline pl = new Polyline();
                pl.SetDatabaseDefaults(db);
                pl.AddVertexAt(0, new Point2d(seg.Start.X, seg.Start.Y), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(seg.End.X, seg.End.Y), 0, 0, 0);
                pl.Elevation = seg.Start.Z;
                pl.ConstantWidth = seg.MaxDimensionMm;
                pl.Layer = layerName;
                pl.ColorIndex = 256;

                space.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                try { pl.Transparency = new Transparency(DuctTransparencyAlpha); } catch { }

                // Label: chỉ đoạn có annotation trực tiếp, đủ dài, không chồng
                bool isDirectAnnotation =
                    seg.HasExplicitSize &&
                    !string.Equals(seg.Representation, "INHERITED", StringComparison.OrdinalIgnoreCase);

                if (isDirectAnnotation && seg.LengthMm >= 1500.0 &&
                    labeledPositions.All(p => p.DistanceTo(seg.Center) > MinLabelSpacingMm))
                {
                    string labelText = BuildOverlayLabel(seg);
                    double textH = Math.Max(100.0, Math.Min(300.0, seg.MaxDimensionMm * 0.28));

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
                int index = Math.Abs(hash) % colors.Length;
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

            if (max < 60.0 || min < 20.0 || max / Math.Max(1.0, min) < 1.10)
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
                .OrderByDescending(x => x.Confidence)
                .ThenByDescending(x => x.LengthMm))
            {
                bool duplicate = output.Any(existing =>
                    string.Equals(existing.Size, candidate.Size, StringComparison.OrdinalIgnoreCase) &&
                    SegmentCenterDistance(existing, candidate) <= 50.0 &&
                    ParallelAngleDifference(
                        PlanAngle(existing.Start, existing.End),
                        PlanAngle(candidate.Start, candidate.End)) <= Math.PI / 36.0);

                if (!duplicate)
                    output.Add(candidate);
            }

            return output;
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