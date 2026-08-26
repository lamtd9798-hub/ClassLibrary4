#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;

using OpenCvSharp;

namespace ClassLibrary4
{
    /// <summary>
    /// STEP29A.2 - OpenCV Tile / ROI Candidate Generator.
    ///
    /// Mục tiêu:
    /// - Nhận một tile bitmap đã raster hóa từ CAD.
    /// - Dùng Otsu + morphology + Canny + contour để tìm các ROI compact
    ///   có khả năng là ký hiệu MEP.
    /// - Không phân loại nhãn. ROI sau đó được trả về pipeline CAD/Vector/ONNX.
    /// - Không tham chiếu AutoCAD API để engine có thể tái sử dụng cho YOLO ở STEP29B.
    ///
    /// An toàn:
    /// - Chỉ là candidate generator, không tự đếm và không tự học.
    /// - Native OpenCV lỗi => disable riêng engine này, pipeline cũ vẫn chạy.
    /// </summary>
    internal sealed class MepOpenCvTileCandidateEngine : IDisposable
    {
        internal sealed class CandidateRegion
        {
            public int X { get; set; }
            public int Y { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }

            public double Score { get; set; }
            public double AreaRatio { get; set; }
            public double ForegroundDensity { get; set; }
            public double EdgeDensity { get; set; }
            public int LineCount { get; set; }
            public double AspectRatio { get; set; }

            public int Right => X + Width;
            public int Bottom => Y + Height;
        }

        internal sealed class TileAnalysis
        {
            public bool Success { get; set; }
            public int RawContourCount { get; set; }
            public int FilteredRegionCount { get; set; }
            public int FinalRegionCount { get; set; }
            public double ForegroundDensity { get; set; }
            public double EdgeDensity { get; set; }
            public string Message { get; set; } = "";
        }

        internal readonly struct TileStats
        {
            public TileStats(long runs, long candidates, long errors)
            {
                Runs = runs;
                Candidates = candidates;
                Errors = errors;
            }

            public long Runs { get; }
            public long Candidates { get; }
            public long Errors { get; }
        }

        private long _runs;
        private long _candidates;
        private long _errors;
        private bool _disabled;
        private bool _disposed;
        private string _lastError = "";

        public bool IsAvailable =>
            !_disposed &&
            !_disabled;

        public string LastError =>
            _lastError ?? "";

        public TileStats Stats =>
            new TileStats(
                Interlocked.Read(ref _runs),
                Interlocked.Read(ref _candidates),
                Interlocked.Read(ref _errors));

        public bool TryFindCandidates(
            Bitmap source,
            out List<CandidateRegion> candidates,
            out TileAnalysis analysis,
            int maxCandidates = 48)
        {
            candidates = new List<CandidateRegion>();
            analysis = new TileAnalysis();

            if (!IsAvailable)
            {
                analysis.Message =
                    string.IsNullOrWhiteSpace(_lastError)
                        ? "OpenCV tile engine không khả dụng."
                        : _lastError;
                return false;
            }

            if (source == null ||
                source.Width < 32 ||
                source.Height < 32)
            {
                analysis.Message =
                    "Tile quá nhỏ để tìm candidate.";
                return false;
            }

            maxCandidates =
                Math.Max(1, Math.Min(128, maxCandidates));

            Interlocked.Increment(ref _runs);

            try
            {
                using (Mat src = MepOpenCvBitmapBridge.ToMat(source))
                using (Mat gray = new Mat())
                using (Mat blur = new Mat())
                using (Mat binary = new Mat())
                using (Mat closed = new Mat())
                using (Mat grouped = new Mat())
                using (Mat edges = new Mat())
                using (Mat closeKernel =
                    Cv2.GetStructuringElement(
                        MorphShapes.Rect,
                        new OpenCvSharp.Size(3, 3)))
                using (Mat groupKernel =
                    Cv2.GetStructuringElement(
                        MorphShapes.Rect,
                        new OpenCvSharp.Size(5, 5)))
                {
                    if (src.Empty())
                    {
                        analysis.Message = "Tile OpenCV đang rỗng.";
                        return false;
                    }

                    int channels = src.Channels();

                    if (channels == 4)
                    {
                        Cv2.CvtColor(src, gray, ColorConversionCodes.BGRA2GRAY);
                    }
                    else if (channels == 3)
                    {
                        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
                    }
                    else if (channels == 1)
                    {
                        src.CopyTo(gray);
                    }
                    else
                    {
                        analysis.Message =
                            "Tile có channel không hỗ trợ: " + channels;
                        return false;
                    }

                    Cv2.GaussianBlur(
                        gray,
                        blur,
                        new OpenCvSharp.Size(3, 3),
                        0.0);

                    double mean = Cv2.Mean(blur).Val0;

                    ThresholdTypes thresholdMode =
                        mean >= 127.0
                            ? ThresholdTypes.BinaryInv | ThresholdTypes.Otsu
                            : ThresholdTypes.Binary | ThresholdTypes.Otsu;

                    Cv2.Threshold(
                        blur,
                        binary,
                        0.0,
                        255.0,
                        thresholdMode);

                    Cv2.MorphologyEx(
                        binary,
                        closed,
                        MorphTypes.Close,
                        closeKernel,
                        iterations: 1);

                    // Nối các mảnh ký hiệu chỉ cách nhau vài pixel.
                    // Dilate đúng 1 vòng để không làm dính các thiết bị lân cận.
                    Cv2.Dilate(
                        closed,
                        grouped,
                        groupKernel,
                        iterations: 1);

                    Cv2.Canny(
                        blur,
                        edges,
                        50.0,
                        150.0,
                        3,
                        true);

                    long pixelCount =
                        (long)gray.Rows * gray.Cols;

                    if (pixelCount <= 0)
                    {
                        analysis.Message = "Không lấy được kích thước tile.";
                        return false;
                    }

                    analysis.ForegroundDensity =
                        (double)Cv2.CountNonZero(closed) / pixelCount;

                    analysis.EdgeDensity =
                        (double)Cv2.CountNonZero(edges) / pixelCount;

                    OpenCvSharp.Point[][] contours;
                    HierarchyIndex[] hierarchy;

                    using (Mat contourInput = grouped.Clone())
                    {
                        Cv2.FindContours(
                            contourInput,
                            out contours,
                            out hierarchy,
                            RetrievalModes.External,
                            ContourApproximationModes.ApproxSimple);
                    }

                    analysis.RawContourCount =
                        contours?.Length ?? 0;

                    List<CandidateRegion> raw =
                        new List<CandidateRegion>();

                    if (contours != null)
                    {
                        foreach (OpenCvSharp.Point[] contour in contours)
                        {
                            if (contour == null || contour.Length < 2)
                                continue;

                            Rect rect = Cv2.BoundingRect(contour);

                            CandidateRegion region =
                                BuildCandidateRegion(
                                    rect,
                                    closed,
                                    edges,
                                    source.Width,
                                    source.Height);

                            if (region != null)
                                raw.Add(region);
                        }
                    }

                    analysis.FilteredRegionCount = raw.Count;

                    candidates =
                        ApplyCandidateNms(raw, maxCandidates);

                    analysis.FinalRegionCount = candidates.Count;
                    analysis.Success = true;

                    if (candidates.Count > 0)
                    {
                        Interlocked.Add(
                            ref _candidates,
                            candidates.Count);
                    }

                    analysis.Message =
                        "STEP29A.2 ROI=" + candidates.Count +
                        ", contour=" + analysis.RawContourCount +
                        ", edge=" + analysis.EdgeDensity.ToString("0.000") +
                        ".";

                    return true;
                }
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _errors);

                _lastError =
                    ex.GetType().Name + ": " + ex.Message;

                if (ex is DllNotFoundException ||
                    ex is System.IO.FileNotFoundException ||
                    ex is System.IO.FileLoadException ||
                    ex is BadImageFormatException ||
                    ex is EntryPointNotFoundException ||
                    ex is TypeInitializationException)
                {
                    _disabled = true;
                }

                analysis.Message = _lastError;
                return false;
            }
        }

        private static CandidateRegion BuildCandidateRegion(
            Rect inputRect,
            Mat foreground,
            Mat edges,
            int imageWidth,
            int imageHeight)
        {
            Rect rect =
                ExpandRect(
                    inputRect,
                    4,
                    imageWidth,
                    imageHeight);

            if (rect.Width < 7 || rect.Height < 7)
                return null;

            double imageArea =
                Math.Max(1.0, (double)imageWidth * imageHeight);

            double areaRatio =
                (double)rect.Width * rect.Height / imageArea;

            // Candidate symbol không được phủ gần cả tile.
            if (areaRatio < 0.00020 ||
                areaRatio > 0.38)
            {
                return null;
            }

            double minSide = Math.Min(rect.Width, rect.Height);
            double maxSide = Math.Max(rect.Width, rect.Height);
            double aspect =
                maxSide / Math.Max(1.0, minSide);

            // Loại đường ống/đường kiến trúc dài và rất mảnh.
            if (aspect > 11.0 ||
                (aspect > 7.0 && minSide < 14.0))
            {
                return null;
            }

            double foregroundDensity;
            double edgeDensity;
            int lineCount = 0;

            using (Mat fgRoi = new Mat(foreground, rect))
            using (Mat edgeRoi = new Mat(edges, rect))
            {
                double roiPixels =
                    Math.Max(1.0, (double)rect.Width * rect.Height);

                foregroundDensity =
                    Cv2.CountNonZero(fgRoi) / roiPixels;

                edgeDensity =
                    Cv2.CountNonZero(edgeRoi) / roiPixels;

                if (foregroundDensity < 0.006 ||
                    foregroundDensity > 0.82 ||
                    edgeDensity < 0.002 ||
                    edgeDensity > 0.62)
                {
                    return null;
                }

                double minDim =
                    Math.Max(8.0, minSide);

                LineSegmentPoint[] lines =
                    Cv2.HoughLinesP(
                        edgeRoi,
                        1.0,
                        Math.PI / 180.0,
                        12,
                        Math.Max(5.0, minDim * 0.22),
                        Math.Max(2.0, minDim * 0.08));

                lineCount = lines?.Length ?? 0;
            }

            double compactness =
                minSide / Math.Max(1.0, maxSide);

            double edgeScore =
                PeakScore(edgeDensity, 0.035, 0.20);

            double fillScore =
                PeakScore(foregroundDensity, 0.045, 0.42);

            double areaScore =
                PeakScore(areaRatio, 0.0015, 0.080);

            double lineScore =
                Math.Min(1.0, lineCount / 6.0);

            double score =
                compactness * 0.26 +
                edgeScore * 0.25 +
                fillScore * 0.21 +
                areaScore * 0.18 +
                lineScore * 0.10;

            if (score < 0.27)
                return null;

            return new CandidateRegion
            {
                X = rect.X,
                Y = rect.Y,
                Width = rect.Width,
                Height = rect.Height,
                Score = Clamp01(score),
                AreaRatio = areaRatio,
                ForegroundDensity = foregroundDensity,
                EdgeDensity = edgeDensity,
                LineCount = lineCount,
                AspectRatio = aspect
            };
        }

        private static List<CandidateRegion> ApplyCandidateNms(
            List<CandidateRegion> source,
            int maxCandidates)
        {
            List<CandidateRegion> sorted =
                (source ?? new List<CandidateRegion>())
                    .Where(x => x != null)
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.AreaRatio)
                    .ToList();

            List<CandidateRegion> kept =
                new List<CandidateRegion>();

            foreach (CandidateRegion candidate in sorted)
            {
                bool suppressed = false;

                foreach (CandidateRegion existing in kept)
                {
                    double iou = GetIoU(candidate, existing);
                    double containment = GetContainment(candidate, existing);

                    if (iou >= 0.44 ||
                        containment >= 0.82)
                    {
                        suppressed = true;
                        break;
                    }
                }

                if (suppressed)
                    continue;

                kept.Add(candidate);

                if (kept.Count >= maxCandidates)
                    break;
            }

            return kept;
        }

        private static Rect ExpandRect(
            Rect rect,
            int padding,
            int imageWidth,
            int imageHeight)
        {
            int x1 = Math.Max(0, rect.X - padding);
            int y1 = Math.Max(0, rect.Y - padding);
            int x2 = Math.Min(imageWidth, rect.X + rect.Width + padding);
            int y2 = Math.Min(imageHeight, rect.Y + rect.Height + padding);

            return new Rect(
                x1,
                y1,
                Math.Max(1, x2 - x1),
                Math.Max(1, y2 - y1));
        }

        private static double PeakScore(
            double value,
            double lowGood,
            double highGood)
        {
            if (value <= 0.0)
                return 0.0;

            if (value >= lowGood && value <= highGood)
                return 1.0;

            if (value < lowGood)
                return Clamp01(value / Math.Max(1e-9, lowGood));

            double tail =
                1.0 -
                (value - highGood) /
                Math.Max(1e-9, 1.0 - highGood);

            return Clamp01(tail);
        }

        private static double GetIoU(
            CandidateRegion a,
            CandidateRegion b)
        {
            int x1 = Math.Max(a.X, b.X);
            int y1 = Math.Max(a.Y, b.Y);
            int x2 = Math.Min(a.Right, b.Right);
            int y2 = Math.Min(a.Bottom, b.Bottom);

            double iw = Math.Max(0, x2 - x1);
            double ih = Math.Max(0, y2 - y1);
            double intersection = iw * ih;

            if (intersection <= 0.0)
                return 0.0;

            double areaA = Math.Max(1.0, (double)a.Width * a.Height);
            double areaB = Math.Max(1.0, (double)b.Width * b.Height);
            double union = areaA + areaB - intersection;

            return union <= 0.0
                ? 0.0
                : intersection / union;
        }

        private static double GetContainment(
            CandidateRegion a,
            CandidateRegion b)
        {
            int x1 = Math.Max(a.X, b.X);
            int y1 = Math.Max(a.Y, b.Y);
            int x2 = Math.Min(a.Right, b.Right);
            int y2 = Math.Min(a.Bottom, b.Bottom);

            double iw = Math.Max(0, x2 - x1);
            double ih = Math.Max(0, y2 - y1);
            double intersection = iw * ih;

            if (intersection <= 0.0)
                return 0.0;

            double smaller =
                Math.Min(
                    Math.Max(1.0, (double)a.Width * a.Height),
                    Math.Max(1.0, (double)b.Width * b.Height));

            return intersection / smaller;
        }

        private static double Clamp01(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return 0.0;

            if (value < 0.0)
                return 0.0;

            if (value > 1.0)
                return 1.0;

            return value;
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
