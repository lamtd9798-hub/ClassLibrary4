#nullable disable
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

using OpenCvSharp;

namespace ClassLibrary4
{
    /// <summary>
    /// STEP29A - OpenCV Vision Engine.
    ///
    /// Vai trò:
    /// - KHÔNG thay CAD Native / Vector / STEP28A Hot Cache.
    /// - Chỉ chạy khi prediction ONNX thô chưa đủ chắc.
    /// - Dùng Canny + Contour + HoughLinesP để đánh giá ảnh có cấu trúc MEP hay không.
    /// - Nếu ảnh có cấu trúc đủ tốt, tạo ảnh nhị phân đã làm sạch rồi cho classifier thử lần 2.
    /// - Native OpenCV lỗi => tự disable engine, pipeline cũ vẫn chạy.
    ///
    /// Lớp này cố tình không tham chiếu AutoCAD API.
    /// </summary>
    internal sealed class MepOpenCvVisionEngine : IDisposable
    {
        internal sealed class Analysis
        {
            public bool Success { get; set; }
            public bool RefineRecommended { get; set; }
            public double EdgeDensity { get; set; }
            public double ForegroundDensity { get; set; }
            public int ContourCount { get; set; }
            public int SignificantContourCount { get; set; }
            public int LineCount { get; set; }
            public int OrthogonalLineCount { get; set; }
            public double OrthogonalRatio { get; set; }
            public string StructureHint { get; set; } = "";
            public string Message { get; set; } = "";
        }

        internal readonly struct VisionStats
        {
            public VisionStats(
                long runs,
                long successful,
                long recommended,
                long skipped,
                long errors)
            {
                Runs = runs;
                Successful = successful;
                Recommended = recommended;
                Skipped = skipped;
                Errors = errors;
            }

            public long Runs { get; }
            public long Successful { get; }
            public long Recommended { get; }
            public long Skipped { get; }
            public long Errors { get; }
        }

        private static readonly object NativeResolverGate =
            new object();

        private static bool _nativeResolverAttempted;

        private long _runs;
        private long _successful;
        private long _recommended;
        private long _skipped;
        private long _errors;

        private bool _nativeDisabled;
        private bool _disposed;
        private string _lastError = "";

        public bool IsAvailable =>
            !_disposed &&
            !_nativeDisabled;

        public string LastError =>
            _lastError ?? "";

        public VisionStats Stats =>
            new VisionStats(
                Interlocked.Read(ref _runs),
                Interlocked.Read(ref _successful),
                Interlocked.Read(ref _recommended),
                Interlocked.Read(ref _skipped),
                Interlocked.Read(ref _errors));

        /// <summary>
        /// Probe native runtime đúng 1 lần khi engine được tạo.
        /// Không mở cửa sổ OpenCV, không tạo thread nền.
        /// </summary>
        public bool Probe()
        {
            if (_disposed ||
                _nativeDisabled)
            {
                return false;
            }

            try
            {
                EnsureNativeResolverRegistered();

                using (Mat src =
                    new Mat(
                        new OpenCvSharp.Size(8, 8),
                        MatType.CV_8UC1,
                        Scalar.All(0)))
                using (Mat dst =
                    new Mat())
                {
                    Cv2.Canny(
                        src,
                        dst,
                        50.0,
                        150.0,
                        3,
                        true);
                }

                _lastError = "";
                return true;
            }
            catch (Exception ex)
            {
                DisableIfNativeFailure(ex);
                _lastError =
                    ex.GetType().Name +
                    ": " +
                    ex.Message;

                return false;
            }
        }

        /// <summary>
        /// Phân tích + làm sạch ảnh cho classifier.
        /// processedBitmap do caller Dispose.
        /// </summary>
        public bool TryPreprocessForClassifier(
            Bitmap source,
            out Bitmap processedBitmap,
            out Analysis analysis)
        {
            processedBitmap = null;
            analysis =
                new Analysis();

            if (_disposed ||
                _nativeDisabled)
            {
                analysis.Message =
                    string.IsNullOrWhiteSpace(_lastError)
                        ? "OpenCV engine không khả dụng."
                        : _lastError;
                return false;
            }

            if (source == null ||
                source.Width < 8 ||
                source.Height < 8)
            {
                analysis.Message =
                    "Ảnh quá nhỏ để OpenCV phân tích.";
                return false;
            }

            Interlocked.Increment(
                ref _runs);

            try
            {
                using (Mat src =
                    MepOpenCvBitmapBridge.ToMat(
                        source))
                using (Mat gray =
                    new Mat())
                using (Mat blurred =
                    new Mat())
                using (Mat binary =
                    new Mat())
                using (Mat clean =
                    new Mat())
                using (Mat edges =
                    new Mat())
                using (Mat kernel =
                    Cv2.GetStructuringElement(
                        MorphShapes.Rect,
                        new OpenCvSharp.Size(3, 3)))
                {
                    if (src.Empty())
                    {
                        analysis.Message =
                            "OpenCV nhận ảnh rỗng.";
                        Interlocked.Increment(
                            ref _skipped);
                        return false;
                    }

                    int channels =
                        src.Channels();

                    if (channels == 4)
                    {
                        Cv2.CvtColor(
                            src,
                            gray,
                            ColorConversionCodes.BGRA2GRAY);
                    }
                    else if (channels == 3)
                    {
                        Cv2.CvtColor(
                            src,
                            gray,
                            ColorConversionCodes.BGR2GRAY);
                    }
                    else if (channels == 1)
                    {
                        src.CopyTo(
                            gray);
                    }
                    else
                    {
                        analysis.Message =
                            "OpenCV không hỗ trợ ảnh " +
                            channels +
                            " channel ở STEP29A.";
                        Interlocked.Increment(
                            ref _skipped);
                        return false;
                    }

                    Cv2.GaussianBlur(
                        gray,
                        blurred,
                        new OpenCvSharp.Size(3, 3),
                        0.0);

                    double mean =
                        Cv2.Mean(
                            blurred).Val0;

                    ThresholdTypes thresholdMode =
                        mean >= 127.0
                            ? ThresholdTypes.BinaryInv |
                              ThresholdTypes.Otsu
                            : ThresholdTypes.Binary |
                              ThresholdTypes.Otsu;

                    Cv2.Threshold(
                        blurred,
                        binary,
                        0.0,
                        255.0,
                        thresholdMode);

                    // Close nhẹ để nối các nét CAD bị đứt 1-2 pixel.
                    // Không Open/Erode mạnh vì ký hiệu MEP thường có nét rất mảnh.
                    Cv2.MorphologyEx(
                        binary,
                        clean,
                        MorphTypes.Close,
                        kernel);

                    Cv2.Canny(
                        blurred,
                        edges,
                        50.0,
                        150.0,
                        3,
                        true);

                    long pixelCount =
                        (long)gray.Rows *
                        gray.Cols;

                    if (pixelCount <= 0)
                    {
                        analysis.Message =
                            "OpenCV không lấy được kích thước ảnh.";
                        Interlocked.Increment(
                            ref _skipped);
                        return false;
                    }

                    int edgePixels =
                        Cv2.CountNonZero(
                            edges);

                    int foregroundPixels =
                        Cv2.CountNonZero(
                            clean);

                    analysis.EdgeDensity =
                        (double)edgePixels /
                        pixelCount;

                    analysis.ForegroundDensity =
                        (double)foregroundPixels /
                        pixelCount;

                    OpenCvSharp.Point[][] contours;
                    HierarchyIndex[] hierarchy;

                    using (Mat contourInput =
                        clean.Clone())
                    {
                        Cv2.FindContours(
                            contourInput,
                            out contours,
                            out hierarchy,
                            RetrievalModes.List,
                            ContourApproximationModes.ApproxSimple);
                    }

                    analysis.ContourCount =
                        contours?.Length ?? 0;

                    double minContourArea =
                        Math.Max(
                            4.0,
                            pixelCount * 0.00025);

                    int significantContours = 0;

                    if (contours != null)
                    {
                        foreach (OpenCvSharp.Point[] contour
                            in contours)
                        {
                            if (contour == null ||
                                contour.Length < 2)
                            {
                                continue;
                            }

                            double area =
                                Math.Abs(
                                    Cv2.ContourArea(
                                        contour));

                            Rect rect =
                                Cv2.BoundingRect(
                                    contour);

                            // Nét CAD mảnh có contour-area nhỏ nhưng bounding box vẫn hữu ích.
                            bool meaningfulByBox =
                                rect.Width >= 5 &&
                                rect.Height >= 5;

                            if (area >= minContourArea ||
                                meaningfulByBox)
                            {
                                significantContours++;
                            }
                        }
                    }

                    analysis.SignificantContourCount =
                        significantContours;

                    double minDim =
                        Math.Max(
                            8.0,
                            Math.Min(
                                gray.Cols,
                                gray.Rows));

                    LineSegmentPoint[] lines =
                        Cv2.HoughLinesP(
                            edges,
                            1.0,
                            Math.PI / 180.0,
                            18,
                            minDim * 0.14,
                            Math.Max(
                                2.0,
                                minDim * 0.035));

                    analysis.LineCount =
                        lines?.Length ?? 0;

                    int orthogonal = 0;

                    if (lines != null)
                    {
                        foreach (LineSegmentPoint line
                            in lines)
                        {
                            double dx =
                                line.P2.X -
                                line.P1.X;

                            double dy =
                                line.P2.Y -
                                line.P1.Y;

                            double angle =
                                Math.Abs(
                                    Math.Atan2(
                                        dy,
                                        dx) *
                                    180.0 /
                                    Math.PI);

                            while (angle >= 180.0)
                                angle -= 180.0;

                            double d0 =
                                Math.Min(
                                    angle,
                                    180.0 - angle);

                            double d90 =
                                Math.Abs(
                                    angle - 90.0);

                            if (d0 <= 12.0 ||
                                d90 <= 12.0)
                            {
                                orthogonal++;
                            }
                        }
                    }

                    analysis.OrthogonalLineCount =
                        orthogonal;

                    analysis.OrthogonalRatio =
                        analysis.LineCount <= 0
                            ? 0.0
                            : (double)orthogonal /
                              analysis.LineCount;

                    // Chặn ảnh trắng/rỗng hoặc ảnh đặc kín bất thường.
                    bool densityOk =
                        analysis.ForegroundDensity >= 0.0020 &&
                        analysis.ForegroundDensity <= 0.62 &&
                        analysis.EdgeDensity >= 0.0010 &&
                        analysis.EdgeDensity <= 0.48;

                    bool hasStructure =
                        analysis.SignificantContourCount >= 1 ||
                        analysis.LineCount >= 1;

                    analysis.RefineRecommended =
                        densityOk &&
                        hasStructure;

                    if (analysis.LineCount >= 2 &&
                        analysis.OrthogonalRatio >= 0.72)
                    {
                        analysis.StructureHint =
                            "ORTHOGONAL";
                    }
                    else if (analysis.SignificantContourCount >= 2)
                    {
                        analysis.StructureHint =
                            "CONTOUR";
                    }
                    else if (analysis.ForegroundDensity < 0.012)
                    {
                        analysis.StructureHint =
                            "SPARSE";
                    }
                    else
                    {
                        analysis.StructureHint =
                            "GENERIC";
                    }

                    analysis.Success = true;

                    Interlocked.Increment(
                        ref _successful);

                    if (!analysis.RefineRecommended)
                    {
                        analysis.Message =
                            "Canny/Contour không đủ bằng chứng để chạy ONNX lần 2.";

                        Interlocked.Increment(
                            ref _skipped);

                        return true;
                    }

                    // Classifier cũ quen nền trắng + nét tối.
                    // clean hiện là foreground trắng => đảo lại trước khi trả Bitmap.
                    using (Mat classifierGray =
                        new Mat())
                    {
                        Cv2.BitwiseNot(
                            clean,
                            classifierGray);

                        using (Bitmap rawBitmap =
                            MepOpenCvBitmapBridge.ToBitmap(
                                classifierGray))
                        {
                            processedBitmap =
                                CloneAs32BppArgb(
                                    rawBitmap);
                        }
                    }

                    if (processedBitmap == null)
                    {
                        analysis.RefineRecommended = false;
                        analysis.Message =
                            "OpenCV phân tích được nhưng không tạo được ảnh refine.";
                        Interlocked.Increment(
                            ref _skipped);
                        return true;
                    }

                    Interlocked.Increment(
                        ref _recommended);

                    analysis.Message =
                        "OpenCV refine: edge=" +
                        analysis.EdgeDensity.ToString("0.000") +
                        ", contour=" +
                        analysis.SignificantContourCount +
                        ", line=" +
                        analysis.LineCount +
                        ", ortho=" +
                        analysis.OrthogonalRatio.ToString("0.00") +
                        ".";

                    return true;
                }
            }
            catch (Exception ex)
            {
                Interlocked.Increment(
                    ref _errors);

                _lastError =
                    ex.GetType().Name +
                    ": " +
                    ex.Message;

                DisableIfNativeFailure(
                    ex);

                analysis.Message =
                    _lastError;

                return false;
            }
        }


        private static void EnsureNativeResolverRegistered()
        {
            lock (NativeResolverGate)
            {
                if (_nativeResolverAttempted)
                    return;

                _nativeResolverAttempted = true;

                try
                {
                    Assembly openCvAssembly =
                        typeof(Cv2).Assembly;

                    string openCvDirectory =
                        Path.GetDirectoryName(
                            openCvAssembly.Location) ??
                        "";

                    string pluginDirectory =
                        Path.GetDirectoryName(
                            typeof(MepOpenCvVisionEngine)
                                .Assembly
                                .Location) ??
                        openCvDirectory;

                    string nativeName =
                        "OpenCvSharpExtern.dll";

                    string[] candidates =
                    {
                        Path.Combine(
                            pluginDirectory,
                            nativeName),
                        Path.Combine(
                            openCvDirectory,
                            nativeName),
                        Path.Combine(
                            pluginDirectory,
                            "runtimes",
                            "win-x64",
                            "native",
                            nativeName),
                        Path.Combine(
                            openCvDirectory,
                            "runtimes",
                            "win-x64",
                            "native",
                            nativeName),
                        Path.Combine(
                            pluginDirectory,
                            "dll",
                            "x64",
                            nativeName),
                        Path.Combine(
                            openCvDirectory,
                            "dll",
                            "x64",
                            nativeName)
                    };

                    NativeLibrary.SetDllImportResolver(
                        openCvAssembly,
                        (libraryName, assembly, searchPath) =>
                        {
                            if (string.IsNullOrWhiteSpace(
                                    libraryName) ||
                                !libraryName.StartsWith(
                                    "OpenCvSharpExtern",
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                return IntPtr.Zero;
                            }

                            foreach (string candidate
                                in candidates)
                            {
                                try
                                {
                                    if (!string.IsNullOrWhiteSpace(
                                            candidate) &&
                                        File.Exists(
                                            candidate))
                                    {
                                        return
                                            NativeLibrary.Load(
                                                candidate);
                                    }
                                }
                                catch
                                {
                                }
                            }

                            // Trả zero để .NET tiếp tục cơ chế resolve mặc định
                            // từ runtime package / host nếu không thấy file custom.
                            return IntPtr.Zero;
                        });
                }
                catch (InvalidOperationException)
                {
                    // Resolver đã được host/package khác đăng ký. Giữ nguyên resolver đó.
                }
                catch
                {
                    // Không để resolver phụ làm hỏng pipeline. Probe() sẽ xác nhận thật.
                }
            }
        }

        private static Bitmap CloneAs32BppArgb(
            Bitmap source)
        {
            if (source == null ||
                source.Width <= 0 ||
                source.Height <= 0)
            {
                return null;
            }

            Bitmap result =
                new Bitmap(
                    source.Width,
                    source.Height,
                    PixelFormat.Format32bppArgb);

            using (Graphics g =
                Graphics.FromImage(
                    result))
            {
                g.Clear(
                    Color.White);

                g.DrawImageUnscaled(
                    source,
                    0,
                    0);
            }

            return result;
        }

        private void DisableIfNativeFailure(
            Exception ex)
        {
            if (ex == null)
                return;

            if (ex is DllNotFoundException ||
                ex is FileNotFoundException ||
                ex is FileLoadException ||
                ex is BadImageFormatException ||
                ex is TypeInitializationException ||
                ex is EntryPointNotFoundException)
            {
                _nativeDisabled = true;
            }
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
