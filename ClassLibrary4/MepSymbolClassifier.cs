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
    /// STEP28A - ONNX symbol classifier + Smart Hot Cache / Symbol Cluster.
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
            public bool CacheHit { get; set; }
            public string CacheMode { get; set; } = "";
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

        // ============================================================
        // STEP28A - SMART SYMBOL HOT CACHE + ROTATION-INVARIANT CLUSTER
        // ============================================================
        // Ý tưởng:
        // - NORMALIZE ảnh đúng 1 lần.
        // - Tạo fingerprint 16x16 bất biến theo xoay 0/90/180/270.
        // - EXACT cache dùng cặp hash 128-bit.
        // - CLUSTER cache dùng hash 8x8 + 4-band LSH để tìm ký hiệu gần giống
        //   mà không phải quét toàn bộ cache.
        // - Chỉ prediction rất chắc mới được dùng làm "cluster teacher".
        // - Nếu 2 cluster gần nhau nhưng khác label -> KHÔNG cache-hit, chạy ONNX.
        //
        // Cache chỉ nằm trong RAM của phiên classifier hiện tại.
        // Ground truth / Cloud Memory vẫn do lớp bên ngoài quản lý.
        private const int HotCacheMaxEntries = 4096;
        private const double ClusterTeacherMinConfidence = 0.965;
        private const double ClusterTeacherMinMargin = 0.18;
        private const int ClusterMaxHammingDistance = 3;
        private const double ClusterMaxOccupancyRatioDelta = 0.055;
        private const double ClusterMaxAspectDelta = 0.16;
        private const int ClusterMaxOccupancyCellDelta = 8;

        private readonly object _cacheGate =
            new object();

        private readonly Dictionary<SymbolExactKey, LinkedListNode<SymbolCacheEntry>>
            _exactCache =
                new Dictionary<SymbolExactKey, LinkedListNode<SymbolCacheEntry>>();

        private readonly LinkedList<SymbolCacheEntry> _cacheLru =
            new LinkedList<SymbolCacheEntry>();

        private readonly Dictionary<ushort, HashSet<SymbolCacheEntry>>[]
            _clusterBands =
            {
                new Dictionary<ushort, HashSet<SymbolCacheEntry>>(),
                new Dictionary<ushort, HashSet<SymbolCacheEntry>>(),
                new Dictionary<ushort, HashSet<SymbolCacheEntry>>(),
                new Dictionary<ushort, HashSet<SymbolCacheEntry>>()
            };

        private long _exactCacheHits;
        private long _clusterCacheHits;
        private long _onnxRuns;

        internal readonly struct SymbolCacheStats
        {
            public SymbolCacheStats(
                int count,
                long exactHits,
                long clusterHits,
                long onnxRuns)
            {
                Count = count;
                ExactHits = exactHits;
                ClusterHits = clusterHits;
                OnnxRuns = onnxRuns;
            }

            public int Count { get; }
            public long ExactHits { get; }
            public long ClusterHits { get; }
            public long OnnxRuns { get; }

            public long TotalCacheHits =>
                ExactHits + ClusterHits;

            public double HitRate
            {
                get
                {
                    long total =
                        TotalCacheHits + OnnxRuns;

                    return total <= 0
                        ? 0.0
                        : (double)TotalCacheHits / total;
                }
            }
        }

        private readonly struct SymbolExactKey :
            IEquatable<SymbolExactKey>
        {
            public SymbolExactKey(
                ulong hashA,
                ulong hashB)
            {
                HashA = hashA;
                HashB = hashB;
            }

            public ulong HashA { get; }
            public ulong HashB { get; }

            public bool Equals(
                SymbolExactKey other)
            {
                return
                    HashA == other.HashA &&
                    HashB == other.HashB;
            }

            public override bool Equals(
                object obj)
            {
                return
                    obj is SymbolExactKey other &&
                    Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int a =
                        (int)(HashA ^ (HashA >> 32));

                    int b =
                        (int)(HashB ^ (HashB >> 32));

                    return
                        (a * 397) ^ b;
                }
            }
        }

        private sealed class SymbolFingerprint
        {
            public SymbolExactKey ExactKey { get; set; }
            public ulong ClusterHash { get; set; }
            public int OccupancyCells { get; set; }
            public double OccupancyRatio { get; set; }
            public double CompactAspect { get; set; }
        }

        private sealed class SymbolCacheEntry
        {
            public SymbolFingerprint Fingerprint { get; set; }
            public Prediction Prediction { get; set; }
            public bool ClusterEligible { get; set; }
            public long LastUseTick { get; set; }
            public int HitCount { get; set; }
            public LinkedListNode<SymbolCacheEntry> LruNode { get; set; }
        }

        public string ModelPath { get; }
        public string LabelsPath { get; }
        public int ClassCount => _labels.Count;
        public int InputWidth => _inputWidth;
        public int InputHeight => _inputHeight;
        public int InputChannels => _inputChannels;
        public string InputLayout => _isNchw ? "NCHW" : "NHWC";

        public SymbolCacheStats CacheStats
        {
            get
            {
                lock (_cacheGate)
                {
                    return new SymbolCacheStats(
                        _exactCache.Count,
                        _exactCacheHits,
                        _clusterCacheHits,
                        _onnxRuns);
                }
            }
        }

        public void ClearHotCache()
        {
            lock (_cacheGate)
            {
                _exactCache.Clear();
                _cacheLru.Clear();

                for (int i = 0;
                    i < _clusterBands.Length;
                    i++)
                {
                    _clusterBands[i].Clear();
                }

                _exactCacheHits = 0;
                _clusterCacheHits = 0;
                _onnxRuns = 0;
            }
        }

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
                // STEP28A:
                // Normalize đúng 1 lần. Fingerprint dùng ảnh này để thử cache
                // trước; chỉ cache-miss mới dựng tensor + chạy ONNX.
                using (Bitmap normalized =
                    RenderNormalizedMonochrome(
                        source,
                        _inputWidth,
                        _inputHeight))
                {
                    SymbolFingerprint fingerprint =
                        BuildSymbolFingerprint(
                            normalized);

                    if (TryGetCachedPrediction(
                            fingerprint,
                            out Prediction cached))
                    {
                        return cached;
                    }

                    DenseTensor<float> tensor =
                        BuildInputTensorFromNormalized(
                            normalized);

                    NamedOnnxValue input =
                        NamedOnnxValue.CreateFromTensor(
                            _inputName,
                            tensor);

                    List<NamedOnnxValue> inputs =
                        new List<NamedOnnxValue>
                        {
                            input
                        };

                    lock (_cacheGate)
                    {
                        _onnxRuns++;
                    }

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
                            outputTensor.ToArray();

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

                        // Hot path: tìm Top-2 bằng 1 vòng for.
                        int bestIndex = 0;
                        int secondIndex = -1;
                        double bestProbability =
                            probabilities[0];
                        double secondProbability =
                            double.NegativeInfinity;

                        for (int i = 1;
                            i < probabilities.Length;
                            i++)
                        {
                            double p =
                                probabilities[i];

                            if (p > bestProbability)
                            {
                                secondIndex = bestIndex;
                                secondProbability =
                                    bestProbability;
                                bestIndex = i;
                                bestProbability = p;
                            }
                            else if (p > secondProbability)
                            {
                                secondIndex = i;
                                secondProbability = p;
                            }
                        }

                        result.Success = true;
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
                        result.CacheHit = false;
                        result.CacheMode = "";
                        result.Message = "OK";

                        AddOrUpdateHotCache(
                            fingerprint,
                            result);

                        return result;
                    }
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

        private unsafe DenseTensor<float> BuildInputTensorFromNormalized(
            Bitmap normalized)
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

                        // normalized đã B=G=R.
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

        private unsafe SymbolFingerprint BuildSymbolFingerprint(
            Bitmap normalized)
        {
            const int gridSize = 16;
            const int sampleCountPerAxis = 5;
            const int foregroundLevel = 4;

            byte[] grid =
                new byte[gridSize * gridSize];

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

                for (int gy = 0;
                    gy < gridSize;
                    gy++)
                {
                    int y0 =
                        gy * normalized.Height /
                        gridSize;

                    int y1 =
                        (gy + 1) *
                        normalized.Height /
                        gridSize;

                    if (y1 <= y0)
                        y1 = y0 + 1;

                    for (int gx = 0;
                        gx < gridSize;
                        gx++)
                    {
                        int x0 =
                            gx * normalized.Width /
                            gridSize;

                        int x1 =
                            (gx + 1) *
                            normalized.Width /
                            gridSize;

                        if (x1 <= x0)
                            x1 = x0 + 1;

                        byte maxValue = 0;

                        for (int sy = 0;
                            sy < sampleCountPerAxis;
                            sy++)
                        {
                            int py =
                                y0 +
                                ((sy * 2 + 1) *
                                 Math.Max(1, y1 - y0)) /
                                (sampleCountPerAxis * 2);

                            if (py >= y1)
                                py = y1 - 1;

                            byte* row =
                                GetTopDownRowPointer(
                                    scan0,
                                    stride,
                                    normalized.Height,
                                    py);

                            for (int sx = 0;
                                sx < sampleCountPerAxis;
                                sx++)
                            {
                                int px =
                                    x0 +
                                    ((sx * 2 + 1) *
                                     Math.Max(1, x1 - x0)) /
                                    (sampleCountPerAxis * 2);

                                if (px >= x1)
                                    px = x1 - 1;

                                byte value =
                                    row[px * 3];

                                if (value > maxValue)
                                    maxValue = value;
                            }
                        }

                        grid[
                            gy * gridSize +
                            gx] =
                                (byte)Math.Min(
                                    15,
                                    (maxValue + 8) / 17);
                    }
                }
            }
            finally
            {
                normalized.UnlockBits(
                    data);
            }

            int bestRotation = 0;
            ulong bestHashA = ulong.MaxValue;
            ulong bestHashB = ulong.MaxValue;

            for (int rotation = 0;
                rotation < 4;
                rotation++)
            {
                ComputeRotatedGridHashes(
                    grid,
                    gridSize,
                    rotation,
                    out ulong hashA,
                    out ulong hashB);

                if (hashA < bestHashA ||
                    (hashA == bestHashA &&
                     hashB < bestHashB))
                {
                    bestRotation = rotation;
                    bestHashA = hashA;
                    bestHashB = hashB;
                }
            }

            ulong clusterHash = 0UL;
            int occupancyCells = 0;
            int minX = gridSize;
            int minY = gridSize;
            int maxX = -1;
            int maxY = -1;

            // 16x16 canonical -> 8x8 robust occupancy hash.
            // Một ô 8x8 sáng nếu ít nhất 1 trong 4 ô con có nét.
            for (int cy = 0;
                cy < 8;
                cy++)
            {
                for (int cx = 0;
                    cx < 8;
                    cx++)
                {
                    bool on = false;

                    for (int dy = 0;
                        dy < 2 && !on;
                        dy++)
                    {
                        for (int dx = 0;
                            dx < 2;
                            dx++)
                        {
                            int gx =
                                cx * 2 + dx;
                            int gy =
                                cy * 2 + dy;

                            byte value =
                                GetRotatedGridValue(
                                    grid,
                                    gridSize,
                                    gx,
                                    gy,
                                    bestRotation);

                            if (value >= foregroundLevel)
                            {
                                on = true;
                                break;
                            }
                        }
                    }

                    if (!on)
                        continue;

                    int bit =
                        cy * 8 + cx;

                    clusterHash |=
                        1UL << bit;

                    occupancyCells++;

                    if (cx < minX)
                        minX = cx;
                    if (cx > maxX)
                        maxX = cx;
                    if (cy < minY)
                        minY = cy;
                    if (cy > maxY)
                        maxY = cy;
                }
            }

            double occupancyRatio =
                occupancyCells /
                64.0;

            double compactAspect = 1.0;

            if (maxX >= minX &&
                maxY >= minY)
            {
                double boxWidth =
                    maxX - minX + 1.0;

                double boxHeight =
                    maxY - minY + 1.0;

                double small =
                    Math.Min(
                        boxWidth,
                        boxHeight);

                double large =
                    Math.Max(
                        boxWidth,
                        boxHeight);

                compactAspect =
                    large <= 1e-9
                        ? 1.0
                        : small / large;
            }

            return new SymbolFingerprint
            {
                ExactKey =
                    new SymbolExactKey(
                        bestHashA,
                        bestHashB),
                ClusterHash = clusterHash,
                OccupancyCells = occupancyCells,
                OccupancyRatio = occupancyRatio,
                CompactAspect = compactAspect
            };
        }

        private static void ComputeRotatedGridHashes(
            byte[] grid,
            int size,
            int rotation,
            out ulong hashA,
            out ulong hashB)
        {
            // Hai FNV streams độc lập -> exact key 128-bit.
            ulong a =
                1469598103934665603UL;

            ulong b =
                1099511628211UL ^
                0x9E3779B97F4A7C15UL;

            const ulong primeA =
                1099511628211UL;

            const ulong primeB =
                14029467366897019727UL;

            for (int y = 0;
                y < size;
                y++)
            {
                for (int x = 0;
                    x < size;
                    x++)
                {
                    byte value =
                        GetRotatedGridValue(
                            grid,
                            size,
                            x,
                            y,
                            rotation);

                    a ^=
                        (ulong)(value + 1);

                    a *=
                        primeA;

                    b ^=
                        (ulong)(
                            value +
                            17 +
                            ((x + y * size) & 15));

                    b *=
                        primeB;
                }
            }

            hashA = a;
            hashB = b;
        }

        private static byte GetRotatedGridValue(
            byte[] grid,
            int size,
            int x,
            int y,
            int rotation)
        {
            int sourceX;
            int sourceY;

            switch (rotation & 3)
            {
                case 1:
                    // Output là ảnh xoay 90 độ CW.
                    sourceX = y;
                    sourceY = size - 1 - x;
                    break;

                case 2:
                    sourceX = size - 1 - x;
                    sourceY = size - 1 - y;
                    break;

                case 3:
                    sourceX = size - 1 - y;
                    sourceY = x;
                    break;

                default:
                    sourceX = x;
                    sourceY = y;
                    break;
            }

            return
                grid[
                    sourceY * size +
                    sourceX];
        }

        private bool TryGetCachedPrediction(
            SymbolFingerprint fingerprint,
            out Prediction prediction)
        {
            prediction = null;

            if (fingerprint == null)
                return false;

            lock (_cacheGate)
            {
                if (_exactCache.TryGetValue(
                        fingerprint.ExactKey,
                        out LinkedListNode<SymbolCacheEntry> exactNode))
                {
                    SymbolCacheEntry exactEntry =
                        exactNode.Value;

                    TouchCacheEntry(
                        exactEntry);

                    exactEntry.HitCount++;
                    _exactCacheHits++;

                    prediction =
                        ClonePrediction(
                            exactEntry.Prediction,
                            true,
                            "EXACT");

                    return true;
                }

                HashSet<SymbolCacheEntry> candidates =
                    new HashSet<SymbolCacheEntry>();

                for (int band = 0;
                    band < 4;
                    band++)
                {
                    ushort bandValue =
                        GetClusterBand(
                            fingerprint.ClusterHash,
                            band);

                    if (_clusterBands[band]
                        .TryGetValue(
                            bandValue,
                            out HashSet<SymbolCacheEntry> bucket))
                    {
                        foreach (SymbolCacheEntry candidate
                            in bucket)
                        {
                            candidates.Add(
                                candidate);
                        }
                    }
                }

                SymbolCacheEntry best = null;
                double bestScore =
                    double.MaxValue;

                bool ambiguousDifferentLabel =
                    false;

                foreach (SymbolCacheEntry candidate
                    in candidates)
                {
                    if (candidate == null ||
                        !candidate.ClusterEligible ||
                        candidate.Fingerprint == null)
                    {
                        continue;
                    }

                    int hamming =
                        PopCount64(
                            candidate.Fingerprint.ClusterHash ^
                            fingerprint.ClusterHash);

                    if (hamming >
                        ClusterMaxHammingDistance)
                    {
                        continue;
                    }

                    int occupancyDelta =
                        Math.Abs(
                            candidate.Fingerprint.OccupancyCells -
                            fingerprint.OccupancyCells);

                    if (occupancyDelta >
                        ClusterMaxOccupancyCellDelta)
                    {
                        continue;
                    }

                    double ratioDelta =
                        Math.Abs(
                            candidate.Fingerprint.OccupancyRatio -
                            fingerprint.OccupancyRatio);

                    if (ratioDelta >
                        ClusterMaxOccupancyRatioDelta)
                    {
                        continue;
                    }

                    double aspectDelta =
                        Math.Abs(
                            candidate.Fingerprint.CompactAspect -
                            fingerprint.CompactAspect);

                    if (aspectDelta >
                        ClusterMaxAspectDelta)
                    {
                        continue;
                    }

                    double score =
                        hamming * 10.0 +
                        occupancyDelta * 0.75 +
                        ratioDelta * 85.0 +
                        aspectDelta * 28.0;

                    if (score <
                        bestScore - 1e-9)
                    {
                        best =
                            candidate;

                        bestScore =
                            score;

                        ambiguousDifferentLabel =
                            false;
                    }
                    else if (best != null &&
                             !string.Equals(
                                 best.Prediction.Label,
                                 candidate.Prediction.Label,
                                 StringComparison.OrdinalIgnoreCase) &&
                             score <=
                                bestScore + 3.0)
                    {
                        // Hai family gần như nhau nhưng khác nhãn:
                        // không được "đoán bằng cache", trả về ONNX để giữ độ chính xác.
                        ambiguousDifferentLabel =
                            true;
                    }
                }

                if (best == null ||
                    ambiguousDifferentLabel)
                {
                    return false;
                }

                TouchCacheEntry(
                    best);

                best.HitCount++;
                _clusterCacheHits++;

                prediction =
                    ClonePrediction(
                        best.Prediction,
                        true,
                        "CLUSTER");

                return true;
            }
        }

        private void AddOrUpdateHotCache(
            SymbolFingerprint fingerprint,
            Prediction prediction)
        {
            if (fingerprint == null ||
                prediction == null ||
                !prediction.Success)
            {
                return;
            }

            bool clusterEligible =
                prediction.Confidence >=
                    ClusterTeacherMinConfidence &&
                prediction.Margin >=
                    ClusterTeacherMinMargin;

            Prediction stored =
                ClonePrediction(
                    prediction,
                    false,
                    "");

            lock (_cacheGate)
            {
                if (_exactCache.TryGetValue(
                        fingerprint.ExactKey,
                        out LinkedListNode<SymbolCacheEntry> existingNode))
                {
                    SymbolCacheEntry existing =
                        existingNode.Value;

                    if (existing.ClusterEligible)
                    {
                        RemoveFromClusterBands(
                            existing);
                    }

                    existing.Fingerprint =
                        fingerprint;

                    existing.Prediction =
                        stored;

                    existing.ClusterEligible =
                        clusterEligible;

                    existing.LastUseTick =
                        Environment.TickCount64;

                    if (clusterEligible)
                    {
                        AddToClusterBands(
                            existing);
                    }

                    TouchCacheEntry(
                        existing);

                    return;
                }

                SymbolCacheEntry entry =
                    new SymbolCacheEntry
                    {
                        Fingerprint = fingerprint,
                        Prediction = stored,
                        ClusterEligible = clusterEligible,
                        LastUseTick = Environment.TickCount64,
                        HitCount = 0
                    };

                LinkedListNode<SymbolCacheEntry> node =
                    _cacheLru.AddFirst(
                        entry);

                entry.LruNode =
                    node;

                _exactCache[
                    fingerprint.ExactKey] =
                        node;

                if (clusterEligible)
                {
                    AddToClusterBands(
                        entry);
                }

                TrimHotCacheIfNeeded();
            }
        }

        private void AddToClusterBands(
            SymbolCacheEntry entry)
        {
            for (int band = 0;
                band < 4;
                band++)
            {
                ushort bandValue =
                    GetClusterBand(
                        entry.Fingerprint.ClusterHash,
                        band);

                if (!_clusterBands[band]
                    .TryGetValue(
                        bandValue,
                        out HashSet<SymbolCacheEntry> bucket))
                {
                    bucket =
                        new HashSet<SymbolCacheEntry>();

                    _clusterBands[band][
                        bandValue] =
                            bucket;
                }

                bucket.Add(
                    entry);
            }
        }

        private void RemoveFromClusterBands(
            SymbolCacheEntry entry)
        {
            if (entry == null ||
                entry.Fingerprint == null)
            {
                return;
            }

            for (int band = 0;
                band < 4;
                band++)
            {
                ushort bandValue =
                    GetClusterBand(
                        entry.Fingerprint.ClusterHash,
                        band);

                if (!_clusterBands[band]
                    .TryGetValue(
                        bandValue,
                        out HashSet<SymbolCacheEntry> bucket))
                {
                    continue;
                }

                bucket.Remove(
                    entry);

                if (bucket.Count == 0)
                {
                    _clusterBands[band].Remove(
                        bandValue);
                }
            }
        }

        private void TouchCacheEntry(
            SymbolCacheEntry entry)
        {
            if (entry == null ||
                entry.LruNode == null)
            {
                return;
            }

            entry.LastUseTick =
                Environment.TickCount64;

            if (!ReferenceEquals(
                    _cacheLru.First,
                    entry.LruNode))
            {
                _cacheLru.Remove(
                    entry.LruNode);

                _cacheLru.AddFirst(
                    entry.LruNode);
            }
        }

        private void TrimHotCacheIfNeeded()
        {
            while (_exactCache.Count >
                HotCacheMaxEntries)
            {
                LinkedListNode<SymbolCacheEntry> last =
                    _cacheLru.Last;

                if (last == null)
                    break;

                SymbolCacheEntry entry =
                    last.Value;

                _cacheLru.RemoveLast();

                _exactCache.Remove(
                    entry.Fingerprint.ExactKey);

                if (entry.ClusterEligible)
                {
                    RemoveFromClusterBands(
                        entry);
                }

                entry.LruNode =
                    null;
            }
        }

        private static ushort GetClusterBand(
            ulong hash,
            int band)
        {
            int shift =
                (band & 3) * 16;

            return
                (ushort)(
                    (hash >> shift) &
                    0xFFFFUL);
        }

        private static int PopCount64(
            ulong value)
        {
            // Kernighan: tối đa 64 vòng, không cần thêm dependency.
            int count = 0;

            while (value != 0)
            {
                value &=
                    value - 1;

                count++;
            }

            return count;
        }

        private static Prediction ClonePrediction(
            Prediction source,
            bool cacheHit,
            string cacheMode)
        {
            if (source == null)
                return new Prediction();

            return new Prediction
            {
                Success = source.Success,
                Label = source.Label ?? "",
                Confidence = source.Confidence,
                SecondLabel = source.SecondLabel ?? "",
                SecondConfidence = source.SecondConfidence,
                CacheHit = cacheHit,
                CacheMode = cacheMode ?? "",
                Message = source.Message ?? ""
            };
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

            ClearHotCache();

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