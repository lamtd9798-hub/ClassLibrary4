#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace ClassLibrary4
{
    /// <summary>
    /// STEP29B - YOLO object detector for MEP symbols.
    ///
    /// Supported ONNX layouts:
    /// - YOLOv8 / YOLO11 raw head: [1, C, N] or [1, N, C]
    ///   where C = 4 + classCount (or 5 + classCount for objectness exports).
    /// - End-to-end/NMS export: [1, N, 6] or [1, 6, N]
    ///   with [x1,y1,x2,y2,score,classId].
    ///
    /// The detector is AutoCAD-independent. It receives a Bitmap and returns
    /// bounding boxes in the SOURCE bitmap coordinate system.
    /// </summary>
    internal sealed class MepYoloSymbolDetector : IDisposable
    {
        internal sealed class Detection
        {
            public string Label { get; set; } = "";
            public double Confidence { get; set; }
            public string SecondLabel { get; set; } = "";
            public double SecondConfidence { get; set; }
            public int ClassId { get; set; } = -1;
            public int X { get; set; }
            public int Y { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }

            public int Right => X + Width;
            public int Bottom => Y + Height;
            public double Margin => Confidence - SecondConfidence;
        }

        internal sealed class DetectionResult
        {
            public bool Success { get; set; }
            public List<Detection> Detections { get; set; } = new List<Detection>();
            public int RawCandidateCount { get; set; }
            public int NmsSuppressedCount { get; set; }
            public string OutputShape { get; set; } = "";
            public string Message { get; set; } = "";
        }

        internal readonly struct DetectorStats
        {
            public DetectorStats(long runs, long detections, long errors)
            {
                Runs = runs;
                Detections = detections;
                Errors = errors;
            }

            public long Runs { get; }
            public long Detections { get; }
            public long Errors { get; }
        }

        private readonly InferenceSession _session;
        private readonly string _inputName;
        private readonly string _outputName;
        private readonly List<string> _labels;
        private readonly int _inputWidth;
        private readonly int _inputHeight;
        private readonly int _inputChannels;
        private readonly bool _isNchw;

        private long _runs;
        private long _detections;
        private long _errors;
        private bool _disposed;

        public string ModelPath { get; }
        public string LabelsPath { get; }
        public int ClassCount => _labels.Count;
        public int InputWidth => _inputWidth;
        public int InputHeight => _inputHeight;
        public string InputLayout => _isNchw ? "NCHW" : "NHWC";

        public DetectorStats Stats =>
            new DetectorStats(
                Interlocked.Read(ref _runs),
                Interlocked.Read(ref _detections),
                Interlocked.Read(ref _errors));

        public MepYoloSymbolDetector(string modelPath, string labelsPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
                throw new FileNotFoundException("Không tìm thấy YOLO detector model.", modelPath);

            if (string.IsNullOrWhiteSpace(labelsPath) || !File.Exists(labelsPath))
                throw new FileNotFoundException("Không tìm thấy YOLO detector labels.", labelsPath);

            ModelPath = Path.GetFullPath(modelPath);
            LabelsPath = Path.GetFullPath(labelsPath);

            _labels = File
                .ReadAllLines(LabelsPath)
                .Select(x => (x ?? "").Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith("#", StringComparison.Ordinal))
                .ToList();

            if (_labels.Count == 0)
                throw new InvalidDataException("YOLO labels đang trống.");

            SessionOptions options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };

            _session = new InferenceSession(ModelPath, options);

            _inputName = _session.InputMetadata.Keys.FirstOrDefault() ?? "";
            _outputName = _session.OutputMetadata.Keys.FirstOrDefault() ?? "";

            if (string.IsNullOrWhiteSpace(_inputName) || string.IsNullOrWhiteSpace(_outputName))
                throw new InvalidDataException("YOLO ONNX không có input/output hợp lệ.");

            NodeMetadata metadata = _session.InputMetadata[_inputName];
            int[] dims = metadata.Dimensions?.ToArray() ?? Array.Empty<int>();

            if (dims.Length != 4)
                throw new NotSupportedException("YOLO detector cần input ảnh rank 4.");

            _isNchw = IsChannelDimension(dims[1]) || !IsChannelDimension(dims[3]);

            int channels = _isNchw ? dims[1] : dims[3];
            int height = _isNchw ? dims[2] : dims[1];
            int width = _isNchw ? dims[3] : dims[2];

            _inputChannels = channels > 0 ? channels : 3;
            _inputHeight = height > 0 ? height : 640;
            _inputWidth = width > 0 ? width : 640;

            if (_inputChannels != 3)
                throw new NotSupportedException("STEP29B hỗ trợ YOLO RGB 3-channel. C=" + _inputChannels + ".");

            if (_inputWidth < 64 || _inputHeight < 64)
                throw new NotSupportedException("Kích thước input YOLO không hợp lệ.");
        }

        public DetectionResult Detect(
            Bitmap source,
            double confidenceThreshold = 0.30,
            double iouThreshold = 0.45,
            int maxDetections = 80)
        {
            DetectionResult result = new DetectionResult();

            if (_disposed)
            {
                result.Message = "YOLO detector đã Dispose.";
                return result;
            }

            if (source == null || source.Width < 16 || source.Height < 16)
            {
                result.Message = "Ảnh YOLO không hợp lệ.";
                return result;
            }

            confidenceThreshold = Clamp(confidenceThreshold, 0.05, 0.95);
            iouThreshold = Clamp(iouThreshold, 0.10, 0.90);
            maxDetections = Math.Max(1, Math.Min(300, maxDetections));

            Interlocked.Increment(ref _runs);

            try
            {
                DenseTensor<float> tensor = BuildLetterboxTensor(
                    source,
                    out double scale,
                    out double padX,
                    out double padY);

                NamedOnnxValue input =
                    NamedOnnxValue.CreateFromTensor(_inputName, tensor);

                List<NamedOnnxValue> inputs =
                    new List<NamedOnnxValue>
                    {
                        input
                    };

                using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs =
                    _session.Run(inputs);

                DisposableNamedOnnxValue output = outputs
                    .FirstOrDefault(x => string.Equals(x.Name, _outputName, StringComparison.Ordinal)) ??
                    outputs.FirstOrDefault();

                if (output == null)
                {
                    result.Message = "YOLO model không trả output.";
                    return result;
                }

                Tensor<float> outputTensor = output.AsTensor<float>();
                int[] dims = outputTensor.Dimensions.ToArray();
                float[] raw = outputTensor.ToArray();

                result.OutputShape = "[" + string.Join(",", dims) + "]";

                if (raw == null || raw.Length == 0)
                {
                    result.Message = "YOLO output đang trống.";
                    return result;
                }

                List<Detection> parsed = ParseOutput(
                    raw,
                    dims,
                    source.Width,
                    source.Height,
                    scale,
                    padX,
                    padY,
                    confidenceThreshold);

                result.RawCandidateCount = parsed.Count;

                List<Detection> nms = ApplyClassAwareNms(
                    parsed,
                    iouThreshold,
                    maxDetections,
                    out int suppressed);

                result.NmsSuppressedCount = suppressed;
                result.Detections = nms;
                result.Success = true;

                if (nms.Count > 0)
                    Interlocked.Add(ref _detections, nms.Count);

                result.Message =
                    "STEP29B YOLO raw=" + parsed.Count +
                    ", keep=" + nms.Count +
                    ", nms=" + suppressed +
                    ", output=" + result.OutputShape + ".";

                return result;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _errors);
                result.Message = ex.GetType().Name + ": " + ex.Message;
                return result;
            }
        }

        private DenseTensor<float> BuildLetterboxTensor(
            Bitmap source,
            out double scale,
            out double padX,
            out double padY)
        {
            scale = Math.Min(
                (double)_inputWidth / Math.Max(1, source.Width),
                (double)_inputHeight / Math.Max(1, source.Height));

            int resizedWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
            int resizedHeight = Math.Max(1, (int)Math.Round(source.Height * scale));

            resizedWidth = Math.Min(_inputWidth, resizedWidth);
            resizedHeight = Math.Min(_inputHeight, resizedHeight);

            int left = (_inputWidth - resizedWidth) / 2;
            int top = (_inputHeight - resizedHeight) / 2;

            padX = left;
            padY = top;

            using Mat src = BitmapConverter.ToMat(source);
            using Mat rgb = new Mat();
            using Mat resized = new Mat();
            using Mat canvas = new Mat(
                _inputHeight,
                _inputWidth,
                MatType.CV_8UC3,
                new Scalar(114, 114, 114));

            if (src.Channels() == 4)
                Cv2.CvtColor(src, rgb, ColorConversionCodes.BGRA2RGB);
            else if (src.Channels() == 3)
                Cv2.CvtColor(src, rgb, ColorConversionCodes.BGR2RGB);
            else if (src.Channels() == 1)
                Cv2.CvtColor(src, rgb, ColorConversionCodes.GRAY2RGB);
            else
                throw new NotSupportedException("YOLO source channel=" + src.Channels() + " không hỗ trợ.");

            Cv2.Resize(
                rgb,
                resized,
                new OpenCvSharp.Size(resizedWidth, resizedHeight),
                0.0,
                0.0,
                InterpolationFlags.Linear);

            using (Mat roi = new Mat(canvas, new Rect(left, top, resizedWidth, resizedHeight)))
            {
                resized.CopyTo(roi);
            }

            int byteCount = _inputWidth * _inputHeight * 3;
            byte[] bytes = new byte[byteCount];

            if (!canvas.IsContinuous())
            {
                using Mat clone = canvas.Clone();
                Marshal.Copy(clone.Data, bytes, 0, byteCount);
            }
            else
            {
                Marshal.Copy(canvas.Data, bytes, 0, byteCount);
            }

            DenseTensor<float> tensor = _isNchw
                ? new DenseTensor<float>(new[] { 1, 3, _inputHeight, _inputWidth })
                : new DenseTensor<float>(new[] { 1, _inputHeight, _inputWidth, 3 });

            if (_isNchw)
            {
                int plane = _inputWidth * _inputHeight;

                for (int y = 0; y < _inputHeight; y++)
                {
                    int row = y * _inputWidth;

                    for (int x = 0; x < _inputWidth; x++)
                    {
                        int pixel = row + x;
                        int srcIndex = pixel * 3;

                        tensor[0, 0, y, x] = bytes[srcIndex] / 255.0f;
                        tensor[0, 1, y, x] = bytes[srcIndex + 1] / 255.0f;
                        tensor[0, 2, y, x] = bytes[srcIndex + 2] / 255.0f;
                    }
                }
            }
            else
            {
                for (int y = 0; y < _inputHeight; y++)
                {
                    int row = y * _inputWidth;

                    for (int x = 0; x < _inputWidth; x++)
                    {
                        int srcIndex = (row + x) * 3;

                        tensor[0, y, x, 0] = bytes[srcIndex] / 255.0f;
                        tensor[0, y, x, 1] = bytes[srcIndex + 1] / 255.0f;
                        tensor[0, y, x, 2] = bytes[srcIndex + 2] / 255.0f;
                    }
                }
            }

            return tensor;
        }

        private List<Detection> ParseOutput(
            float[] raw,
            int[] dims,
            int sourceWidth,
            int sourceHeight,
            double scale,
            double padX,
            double padY,
            double confidenceThreshold)
        {
            if (dims == null || dims.Length < 2)
                throw new NotSupportedException("YOLO output rank không hỗ trợ.");

            // Remove leading batch=1 dimensions for common exports.
            int[] shape = dims.Where((d, i) => !(i == 0 && d == 1)).ToArray();

            if (shape.Length != 2)
                throw new NotSupportedException(
                    "STEP29B hiện hỗ trợ YOLO output 2D sau batch. Shape=[" +
                    string.Join(",", dims) + "].");

            int a = shape[0];
            int b = shape[1];

            // End-to-end NMS export [N,6] / [6,N].
            if (a == 6 || b == 6)
            {
                bool endToEndFeaturesFirst = a == 6;
                int rows = endToEndFeaturesFirst ? b : a;

                return ParseEndToEnd6(
                    raw,
                    rows,
                    endToEndFeaturesFirst,
                    sourceWidth,
                    sourceHeight,
                    scale,
                    padX,
                    padY,
                    confidenceThreshold);
            }

            int expectedNoObjectness = _labels.Count + 4;
            int expectedObjectness = _labels.Count + 5;

            bool featuresFirst;
            int featureCount;
            int candidateCount;

            if (a == expectedNoObjectness || a == expectedObjectness)
            {
                featuresFirst = true;
                featureCount = a;
                candidateCount = b;
            }
            else if (b == expectedNoObjectness || b == expectedObjectness)
            {
                featuresFirst = false;
                featureCount = b;
                candidateCount = a;
            }
            else
            {
                // Fallback cho export có dynamic/metadata hơi khác: dimension nhỏ
                // được xem là feature dimension nếu đủ chứa 4 tọa độ + classes.
                bool aLooksLikeFeatures = a >= 5 && a <= 1024 && a < b;
                bool bLooksLikeFeatures = b >= 5 && b <= 1024 && b < a;

                if (aLooksLikeFeatures)
                {
                    featuresFirst = true;
                    featureCount = a;
                    candidateCount = b;
                }
                else if (bLooksLikeFeatures)
                {
                    featuresFirst = false;
                    featureCount = b;
                    candidateCount = a;
                }
                else
                {
                    throw new NotSupportedException(
                        "Không nhận được feature dimension YOLO. Shape=[" +
                        string.Join(",", dims) + "] labels=" + _labels.Count + ".");
                }
            }

            bool hasObjectness = featureCount == expectedObjectness;
            int classStart = hasObjectness ? 5 : 4;
            int availableClasses = featureCount - classStart;
            int classCount = Math.Min(_labels.Count, availableClasses);

            if (classCount <= 0)
                throw new InvalidDataException("YOLO output không chứa class score.");

            float Read(int candidate, int feature)
            {
                if (featuresFirst)
                    return raw[feature * candidateCount + candidate];

                return raw[candidate * featureCount + feature];
            }

            List<Detection> detections = new List<Detection>();

            for (int i = 0; i < candidateCount; i++)
            {
                double cx = Read(i, 0);
                double cy = Read(i, 1);
                double w = Read(i, 2);
                double h = Read(i, 3);

                bool normalizedBox =
                    Math.Abs(cx) <= 2.0 &&
                    Math.Abs(cy) <= 2.0 &&
                    Math.Abs(w) <= 2.0 &&
                    Math.Abs(h) <= 2.0;

                if (normalizedBox)
                {
                    cx *= _inputWidth;
                    cy *= _inputHeight;
                    w *= _inputWidth;
                    h *= _inputHeight;
                }

                if (w <= 0.0 || h <= 0.0)
                    continue;

                double objectness = hasObjectness
                    ? ToProbability(Read(i, 4))
                    : 1.0;

                int bestClass = -1;
                int secondClass = -1;
                double best = 0.0;
                double second = 0.0;

                for (int c = 0; c < classCount; c++)
                {
                    double classProbability = ToProbability(Read(i, classStart + c));
                    double score = objectness * classProbability;

                    if (score > best)
                    {
                        second = best;
                        secondClass = bestClass;
                        best = score;
                        bestClass = c;
                    }
                    else if (score > second)
                    {
                        second = score;
                        secondClass = c;
                    }
                }

                if (bestClass < 0 || best < confidenceThreshold)
                    continue;

                double x1 = cx - w * 0.5;
                double y1 = cy - h * 0.5;
                double x2 = cx + w * 0.5;
                double y2 = cy + h * 0.5;

                Detection detection = MapToSource(
                    x1, y1, x2, y2,
                    bestClass,
                    best,
                    secondClass,
                    second,
                    sourceWidth,
                    sourceHeight,
                    scale,
                    padX,
                    padY);

                if (detection != null)
                    detections.Add(detection);
            }

            return detections;
        }

        private List<Detection> ParseEndToEnd6(
            float[] raw,
            int rowCount,
            bool featuresFirst,
            int sourceWidth,
            int sourceHeight,
            double scale,
            double padX,
            double padY,
            double confidenceThreshold)
        {
            float Read(int row, int feature)
            {
                if (featuresFirst)
                    return raw[feature * rowCount + row];

                return raw[row * 6 + feature];
            }

            List<Detection> detections = new List<Detection>();

            for (int i = 0; i < rowCount; i++)
            {
                double x1 = Read(i, 0);
                double y1 = Read(i, 1);
                double x2 = Read(i, 2);
                double y2 = Read(i, 3);
                double score = ToProbability(Read(i, 4));
                int classId = (int)Math.Round(Read(i, 5));

                if (score < confidenceThreshold || classId < 0 || classId >= _labels.Count)
                    continue;

                bool normalizedBox =
                    Math.Abs(x1) <= 2.0 &&
                    Math.Abs(y1) <= 2.0 &&
                    Math.Abs(x2) <= 2.0 &&
                    Math.Abs(y2) <= 2.0;

                if (normalizedBox)
                {
                    x1 *= _inputWidth;
                    x2 *= _inputWidth;
                    y1 *= _inputHeight;
                    y2 *= _inputHeight;
                }

                Detection detection = MapToSource(
                    x1, y1, x2, y2,
                    classId,
                    score,
                    -1,
                    0.0,
                    sourceWidth,
                    sourceHeight,
                    scale,
                    padX,
                    padY);

                if (detection != null)
                    detections.Add(detection);
            }

            return detections;
        }

        private Detection MapToSource(
            double x1,
            double y1,
            double x2,
            double y2,
            int classId,
            double score,
            int secondClassId,
            double secondScore,
            int sourceWidth,
            int sourceHeight,
            double scale,
            double padX,
            double padY)
        {
            if (scale <= 1e-9)
                return null;

            double sx1 = (x1 - padX) / scale;
            double sy1 = (y1 - padY) / scale;
            double sx2 = (x2 - padX) / scale;
            double sy2 = (y2 - padY) / scale;

            sx1 = Clamp(sx1, 0.0, sourceWidth - 1.0);
            sy1 = Clamp(sy1, 0.0, sourceHeight - 1.0);
            sx2 = Clamp(sx2, 0.0, sourceWidth - 1.0);
            sy2 = Clamp(sy2, 0.0, sourceHeight - 1.0);

            if (sx2 <= sx1 || sy2 <= sy1)
                return null;

            int ix = Math.Max(0, (int)Math.Floor(sx1));
            int iy = Math.Max(0, (int)Math.Floor(sy1));
            int ir = Math.Min(sourceWidth, (int)Math.Ceiling(sx2));
            int ib = Math.Min(sourceHeight, (int)Math.Ceiling(sy2));

            int width = ir - ix;
            int height = ib - iy;

            if (width < 3 || height < 3)
                return null;

            return new Detection
            {
                Label = _labels[classId],
                Confidence = Clamp(score, 0.0, 1.0),
                SecondLabel =
                    secondClassId >= 0 && secondClassId < _labels.Count
                        ? _labels[secondClassId]
                        : "",
                SecondConfidence = Clamp(secondScore, 0.0, 1.0),
                ClassId = classId,
                X = ix,
                Y = iy,
                Width = width,
                Height = height
            };
        }

        private static List<Detection> ApplyClassAwareNms(
            List<Detection> source,
            double iouThreshold,
            int maxDetections,
            out int suppressed)
        {
            suppressed = 0;

            List<Detection> ordered = (source ?? new List<Detection>())
                .Where(x => x != null)
                .OrderByDescending(x => x.Confidence)
                .ToList();

            List<Detection> keep = new List<Detection>();

            foreach (Detection candidate in ordered)
            {
                bool reject = false;

                foreach (Detection existing in keep)
                {
                    if (existing.ClassId != candidate.ClassId)
                        continue;

                    if (IoU(existing, candidate) >= iouThreshold)
                    {
                        reject = true;
                        suppressed++;
                        break;
                    }
                }

                if (!reject)
                    keep.Add(candidate);

                if (keep.Count >= maxDetections)
                    break;
            }

            return keep;
        }

        private static double IoU(Detection a, Detection b)
        {
            double ix1 = Math.Max(a.X, b.X);
            double iy1 = Math.Max(a.Y, b.Y);
            double ix2 = Math.Min(a.Right, b.Right);
            double iy2 = Math.Min(a.Bottom, b.Bottom);

            double iw = Math.Max(0.0, ix2 - ix1);
            double ih = Math.Max(0.0, iy2 - iy1);
            double intersection = iw * ih;

            double areaA = Math.Max(0.0, a.Width) * Math.Max(0.0, a.Height);
            double areaB = Math.Max(0.0, b.Width) * Math.Max(0.0, b.Height);
            double union = areaA + areaB - intersection;

            return union <= 1e-9 ? 0.0 : intersection / union;
        }

        private static bool IsChannelDimension(int value)
        {
            return value == 1 || value == 3 || value == 4;
        }

        private static double ToProbability(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return 0.0;

            if (value >= 0.0 && value <= 1.0)
                return value;

            // Some custom exports expose logits.
            if (value >= 35.0)
                return 1.0;

            if (value <= -35.0)
                return 0.0;

            return 1.0 / (1.0 + Math.Exp(-value));
        }

        private static double Clamp(double value, double min, double max)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return min;

            if (value < min)
                return min;

            if (value > max)
                return max;

            return value;
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
