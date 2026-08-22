#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ClassLibrary4
{
    /// <summary>
    /// STEP21A/B - ONNX symbol classifier for AutoCAD MEP symbols.
    ///
    /// Model contract:
    /// - Classification model, one image input.
    /// - Input: float32 NCHW [1,C,H,W] or NHWC [1,H,W,C]. C = 1 or 3.
    /// - Output: float tensor [1,classes] or [classes], logits or probabilities.
    /// - Labels: one class name per line, in exact output-index order.
    ///
    /// The class is intentionally isolated from AutoCAD types so ONNX inference
    /// never touches AutoCAD transactions/DBObjects directly.
    /// </summary>
    internal sealed class MepSymbolClassifier : IDisposable
    {
        internal sealed class Prediction
        {
            public bool Success { get; set; }
            public string Label { get; set; } = "";
            public double Confidence { get; set; }
            public string SecondLabel { get; set; } = "";
            public double SecondConfidence { get; set; }
            public double Margin => Confidence - SecondConfidence;
            public string Message { get; set; } = "";
        }

        private readonly InferenceSession _session;
        private readonly string _inputName;
        private readonly string _outputName;
        private readonly List<string> _labels;
        private readonly int _inputWidth;
        private readonly int _inputHeight;
        private readonly int _inputChannels;
        private readonly bool _isNchw;
        private bool _disposed;

        // STEP24-SYMBOL-PERF:
        // LUT 0..255 cho phép chuyển cường độ CAD -> grayscale mà không gọi
        // Math.Pow cho từng pixel trong vòng lặp nóng. Khởi tạo đúng 1 lần.
        private static readonly byte[] MonochromeLookup =
            BuildMonochromeLookup();

        public string ModelPath { get; }
        public string LabelsPath { get; }
        public int ClassCount => _labels.Count;
        public int InputWidth => _inputWidth;
        public int InputHeight => _inputHeight;
        public int InputChannels => _inputChannels;
        public string InputLayout => _isNchw ? "NCHW" : "NHWC";

        public MepSymbolClassifier(
            string modelPath,
            string labelsPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath) ||
                !File.Exists(modelPath))
            {
                throw new FileNotFoundException(
                    "Không tìm thấy model ONNX.",
                    modelPath);
            }

            if (string.IsNullOrWhiteSpace(labelsPath) ||
                !File.Exists(labelsPath))
            {
                throw new FileNotFoundException(
                    "Không tìm thấy file labels của model ONNX.",
                    labelsPath);
            }

            ModelPath = Path.GetFullPath(modelPath);
            LabelsPath = Path.GetFullPath(labelsPath);

            _labels = File
                .ReadAllLines(
                    LabelsPath)
                .Select(
                    x => (x ?? "").Trim())
                .Where(
                    x =>
                        !string.IsNullOrWhiteSpace(x) &&
                        !x.StartsWith("#", StringComparison.Ordinal))
                .ToList();

            if (_labels.Count == 0)
            {
                throw new InvalidDataException(
                    "File labels đang trống. Mỗi dòng phải là một class name theo đúng thứ tự output của model.");
            }

            TryPreloadOnnxNativeRuntime(
                Path.GetDirectoryName(ModelPath));

            SessionOptions options =
                new SessionOptions();

            options.GraphOptimizationLevel =
                GraphOptimizationLevel.ORT_ENABLE_ALL;

            // CPU-first: ổn định trên mọi máy công ty/nhà.
            // Không ép CUDA/DirectML để tránh phụ thuộc driver/native package.
            _session =
                new InferenceSession(
                    ModelPath,
                    options);

            _inputName =
                _session.InputMetadata.Keys.FirstOrDefault() ?? "";

            _outputName =
                _session.OutputMetadata.Keys.FirstOrDefault() ?? "";

            if (string.IsNullOrWhiteSpace(_inputName) ||
                string.IsNullOrWhiteSpace(_outputName))
            {
                throw new InvalidDataException(
                    "Model ONNX không có input/output tensor hợp lệ.");
            }

            NodeMetadata inputMetadata =
                _session.InputMetadata[_inputName];

            int[] dims =
                inputMetadata.Dimensions?.ToArray() ??
                Array.Empty<int>();

            if (dims.Length != 4)
            {
                throw new NotSupportedException(
                    "STEP21 hiện hỗ trợ model classification ảnh 4D: NCHW hoặc NHWC. " +
                    "Input hiện tại có rank=" + dims.Length.ToString(CultureInfo.InvariantCulture) + ".");
            }

            // NCHW if channel dimension is obviously at index 1.
            bool nchw =
                IsChannelDimension(dims[1]) ||
                !IsChannelDimension(dims[3]);

            _isNchw = nchw;

            int channels =
                nchw
                    ? dims[1]
                    : dims[3];

            int height =
                nchw
                    ? dims[2]
                    : dims[1];

            int width =
                nchw
                    ? dims[3]
                    : dims[2];

            _inputChannels =
                channels > 0
                    ? channels
                    : 3;

            _inputHeight =
                height > 0
                    ? height
                    : 224;

            _inputWidth =
                width > 0
                    ? width
                    : 224;

            if (_inputChannels != 1 &&
                _inputChannels != 3)
            {
                throw new NotSupportedException(
                    "STEP21 hỗ trợ model 1-channel hoặc 3-channel. Model hiện tại có C=" +
                    _inputChannels.ToString(CultureInfo.InvariantCulture) + ".");
            }
        }

        public Prediction Predict(
            Bitmap source)
        {
            Prediction result =
                new Prediction();

            if (_disposed)
            {
                result.Message =
                    "ONNX classifier đã Dispose.";
                return result;
            }

            if (source == null)
            {
                result.Message =
                    "Ảnh ký hiệu đang null.";
                return result;
            }

            try
            {
                DenseTensor<float> tensor =
                    BuildInputTensor(
                        source);

                NamedOnnxValue input =
                    NamedOnnxValue.CreateFromTensor(
                        _inputName,
                        tensor);

                List<NamedOnnxValue> inputs =
                    new List<NamedOnnxValue>
                    {
                        input
                    };

                using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs =
                    _session.Run(inputs))
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
                            "Model ONNX không trả output.";
                        return result;
                    }

                    Tensor<float> outputTensor =
                        output.AsTensor<float>();

                    float[] raw =
                        outputTensor
                            .ToArray();

                    if (raw == null ||
                        raw.Length == 0)
                    {
                        result.Message =
                            "Output tensor của model đang trống.";
                        return result;
                    }

                    if (raw.Length !=
                        _labels.Count)
                    {
                        result.Message =
                            "Số class output (" +
                            raw.Length.ToString(CultureInfo.InvariantCulture) +
                            ") không khớp số labels (" +
                            _labels.Count.ToString(CultureInfo.InvariantCulture) +
                            ").";
                        return result;
                    }

                    double[] probabilities =
                        ConvertToProbabilities(
                            raw);

                    if (probabilities.Length == 0)
                    {
                        result.Message =
                            "Không lấy được class từ model.";
                        return result;
                    }

                    // Hot path: tìm Top-2 bằng 1 vòng for, không LINQ/OrderBy.
                    int bestIndex = 0;
                    int secondIndex = -1;
                    double bestProbability = probabilities[0];
                    double secondProbability = double.NegativeInfinity;

                    for (int i = 1; i < probabilities.Length; i++)
                    {
                        double p = probabilities[i];

                        if (p > bestProbability)
                        {
                            secondIndex = bestIndex;
                            secondProbability = bestProbability;
                            bestIndex = i;
                            bestProbability = p;
                        }
                        else if (p > secondProbability)
                        {
                            secondIndex = i;
                            secondProbability = p;
                        }
                    }

                    result.Success =
                        true;

                    result.Label =
                        _labels[bestIndex];

                    result.Confidence =
                        probabilities[bestIndex];

                    result.SecondLabel =
                        secondIndex >= 0
                            ? _labels[secondIndex]
                            : "";

                    result.SecondConfidence =
                        secondIndex >= 0
                            ? probabilities[secondIndex]
                            : 0.0;

                    result.Message =
                        "OK";

                    return result;
                }
            }
            catch (Exception ex)
            {
                result.Message =
                    ex.GetType().Name +
                    ": " +
                    ex.Message;

                return result;
            }
        }

        private unsafe DenseTensor<float> BuildInputTensor(
            Bitmap source)
        {
            using (Bitmap normalized =
                RenderNormalizedMonochrome(
                    source,
                    _inputWidth,
                    _inputHeight))
            {
                int[] dimensions =
                    _isNchw
                        ? new int[]
                        {
                            1,
                            _inputChannels,
                            _inputHeight,
                            _inputWidth
                        }
                        : new int[]
                        {
                            1,
                            _inputHeight,
                            _inputWidth,
                            _inputChannels
                        };

                DenseTensor<float> tensor =
                    new DenseTensor<float>(
                        dimensions);

                Rectangle rect =
                    new Rectangle(
                        0,
                        0,
                        normalized.Width,
                        normalized.Height);

                BitmapData data =
                    normalized.LockBits(
                        rect,
                        ImageLockMode.ReadOnly,
                        PixelFormat.Format24bppRgb);

                try
                {
                    byte* scan0 =
                        (byte*)data.Scan0.ToPointer();

                    int stride =
                        data.Stride;

                    Span<float> destination =
                        tensor.Buffer.Span;

                    int planeSize =
                        _inputWidth *
                        _inputHeight;

                    for (int y = 0;
                        y < _inputHeight;
                        y++)
                    {
                        byte* row =
                            GetTopDownRowPointer(
                                scan0,
                                stride,
                                _inputHeight,
                                y);

                        int pixelRowBase =
                            y * _inputWidth;

                        for (int x = 0;
                            x < _inputWidth;
                            x++)
                        {
                            int pixelOffset =
                                x * 3;

                            // RenderNormalizedMonochrome đã đảm bảo B=G=R.
                            // Đọc trực tiếp một channel, không cần Max() lại.
                            float signal =
                                row[pixelOffset] /
                                255.0f;

                            int pixelIndex =
                                pixelRowBase + x;

                            if (_isNchw)
                            {
                                if (_inputChannels == 1)
                                {
                                    destination[pixelIndex] =
                                        signal;
                                }
                                else
                                {
                                    destination[pixelIndex] =
                                        signal;

                                    destination[
                                        planeSize +
                                        pixelIndex] =
                                        signal;

                                    destination[
                                        planeSize * 2 +
                                        pixelIndex] =
                                        signal;
                                }
                            }
                            else
                            {
                                int baseIndex =
                                    pixelIndex *
                                    _inputChannels;

                                destination[baseIndex] =
                                    signal;

                                if (_inputChannels == 3)
                                {
                                    destination[baseIndex + 1] =
                                        signal;

                                    destination[baseIndex + 2] =
                                        signal;
                                }
                            }
                        }
                    }
                }
                finally
                {
                    normalized.UnlockBits(
                        data);
                }

                return tensor;
            }
        }

        private static unsafe Bitmap RenderNormalizedMonochrome(
            Bitmap source,
            int width,
            int height)
        {
            Bitmap canvas =
                new Bitmap(
                    Math.Max(16, width),
                    Math.Max(16, height),
                    PixelFormat.Format24bppRgb);

            using (Graphics g =
                Graphics.FromImage(canvas))
            {
                g.Clear(
                    Color.Black);

                g.InterpolationMode =
                    InterpolationMode.HighQualityBicubic;

                g.PixelOffsetMode =
                    PixelOffsetMode.HighQuality;

                g.SmoothingMode =
                    SmoothingMode.HighQuality;

                double scale =
                    Math.Min(
                        (double)canvas.Width /
                            Math.Max(1, source.Width),
                        (double)canvas.Height /
                            Math.Max(1, source.Height));

                int drawWidth =
                    Math.Max(
                        1,
                        (int)Math.Round(
                            source.Width * scale));

                int drawHeight =
                    Math.Max(
                        1,
                        (int)Math.Round(
                            source.Height * scale));

                int x =
                    (canvas.Width - drawWidth) /
                    2;

                int y =
                    (canvas.Height - drawHeight) /
                    2;

                g.DrawImage(
                    source,
                    new Rectangle(
                        x,
                        y,
                        drawWidth,
                        drawHeight));
            }

            // STEP24-SYMBOL-PERF:
            // Không Marshal.Copy sang byte[] managed nữa. Đọc/ghi trực tiếp vùng
            // LockBits và dùng LUT để tránh Math.Pow trên từng pixel.
            Rectangle rect =
                new Rectangle(
                    0,
                    0,
                    canvas.Width,
                    canvas.Height);

            BitmapData data =
                canvas.LockBits(
                    rect,
                    ImageLockMode.ReadWrite,
                    PixelFormat.Format24bppRgb);

            try
            {
                byte* scan0 =
                    (byte*)data.Scan0.ToPointer();

                int stride =
                    data.Stride;

                for (int yy = 0;
                    yy < canvas.Height;
                    yy++)
                {
                    byte* row =
                        GetTopDownRowPointer(
                            scan0,
                            stride,
                            canvas.Height,
                            yy);

                    for (int xx = 0;
                        xx < canvas.Width;
                        xx++)
                    {
                        int i =
                            xx * 3;

                        byte b = row[i + 0];
                        byte g = row[i + 1];
                        byte r = row[i + 2];

                        byte max = b;
                        if (g > max)
                            max = g;
                        if (r > max)
                            max = r;

                        byte gray =
                            MonochromeLookup[max];

                        row[i + 0] = gray;
                        row[i + 1] = gray;
                        row[i + 2] = gray;
                    }
                }
            }
            finally
            {
                canvas.UnlockBits(
                    data);
            }

            return canvas;
        }

        private static byte[] BuildMonochromeLookup()
        {
            byte[] lookup =
                new byte[256];

            for (int i = 0; i < lookup.Length; i++)
            {
                double normalized =
                    (i - 55.0) /
                    200.0;

                if (normalized < 0.0)
                    normalized = 0.0;
                else if (normalized > 1.0)
                    normalized = 1.0;

                // Giữ đúng contrast curve của STEP21.
                normalized =
                    Math.Pow(
                        normalized,
                        0.75);

                lookup[i] =
                    (byte)Math.Round(
                        normalized *
                        255.0);
            }

            return lookup;
        }

        private static unsafe byte* GetTopDownRowPointer(
            byte* scan0,
            int stride,
            int height,
            int y)
        {
            if (stride >= 0)
            {
                return
                    scan0 +
                    y * stride;
            }

            // Một số Bitmap có bottom-up layout. Chuẩn hóa về y=0 là hàng trên
            // để tensor không vô tình bị lật dọc.
            return
                scan0 +
                (height - 1 - y) * stride;
        }

        private static double[] ConvertToProbabilities(
            float[] raw)
        {
            if (raw == null ||
                raw.Length == 0)
            {
                return
                    Array.Empty<double>();
            }

            double[] values =
                new double[raw.Length];

            double sum = 0.0;
            double max = double.NegativeInfinity;
            bool allInProbabilityRange = true;

            // Một vòng duy nhất: copy float->double + sum + max + range check.
            for (int i = 0; i < raw.Length; i++)
            {
                double value =
                    raw[i];

                values[i] =
                    value;

                sum +=
                    value;

                if (value > max)
                    max = value;

                if (value < -1e-6 ||
                    value > 1.000001)
                {
                    allInProbabilityRange =
                        false;
                }
            }

            bool looksLikeProbability =
                allInProbabilityRange &&
                sum >= 0.90 &&
                sum <= 1.10;

            if (looksLikeProbability)
            {
                if (sum <= 1e-12)
                    return values;

                double inverseSum =
                    1.0 / sum;

                for (int i = 0;
                    i < values.Length;
                    i++)
                {
                    double value =
                        values[i];

                    values[i] =
                        (value > 0.0
                            ? value
                            : 0.0) *
                        inverseSum;
                }

                return values;
            }

            // Treat as logits and apply numerically stable softmax.
            double[] exp =
                new double[values.Length];

            double expSum = 0.0;

            for (int i = 0;
                i < values.Length;
                i++)
            {
                double diff =
                    values[i] - max;

                if (diff < -80.0)
                    diff = -80.0;
                else if (diff > 80.0)
                    diff = 80.0;

                double value =
                    Math.Exp(diff);

                exp[i] =
                    value;

                expSum +=
                    value;
            }

            if (expSum <= 1e-12)
            {
                Array.Clear(
                    exp,
                    0,
                    exp.Length);

                return exp;
            }

            double inverseExpSum =
                1.0 / expSum;

            for (int i = 0;
                i < exp.Length;
                i++)
            {
                exp[i] *=
                    inverseExpSum;
            }

            return exp;
        }

        private static bool IsChannelDimension(
            int value)
        {
            return
                value == 1 ||
                value == 3;
        }

        private static void TryPreloadOnnxNativeRuntime(
            string modelDirectory)
        {
            // AutoCAD's AssemblyLoadContext can be more restrictive than a normal
            // desktop EXE. Preloading onnxruntime.dll helps if NuGet leaves the native
            // DLL inside runtimes\win-x64\native instead of the plugin root.
            try
            {
                List<string> roots =
                    new List<string>();

                if (!string.IsNullOrWhiteSpace(
                        modelDirectory))
                {
                    roots.Add(
                        modelDirectory);
                }

                string assemblyDirectory =
                    Path.GetDirectoryName(
                        typeof(MepSymbolClassifier)
                            .Assembly
                            .Location) ??
                    "";

                if (!string.IsNullOrWhiteSpace(
                        assemblyDirectory))
                {
                    roots.Add(
                        assemblyDirectory);
                }

                foreach (string root
                    in roots.Distinct(
                        StringComparer.OrdinalIgnoreCase))
                {
                    string[] candidates =
                    {
                        Path.Combine(
                            root,
                            "onnxruntime.dll"),
                        Path.Combine(
                            root,
                            "runtimes",
                            "win-x64",
                            "native",
                            "onnxruntime.dll")
                    };

                    foreach (string candidate
                        in candidates)
                    {
                        if (!File.Exists(candidate))
                            continue;

                        try
                        {
                            NativeLibrary.Load(
                                candidate);
                            return;
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
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