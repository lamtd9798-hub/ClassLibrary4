#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

using OpenCvSharp;

namespace ClassLibrary4
{
    /// <summary>
    /// STEP29F.2/29F.3 - few-shot prototype descriptor + hierarchy-aware matcher.
    /// Descriptor 16x16 silhouette, canonical rotation 0/90/180/270.
    /// Không cần retrain ONNX/YOLO; dùng ngay sau 1 lần xác nhận.
    /// </summary>
    internal static class MepPrototypeMatcher
    {
        private const int GridSize = 16;

        internal sealed class Evidence
        {
            public string Label { get; set; } = "";
            public bool FollowDn { get; set; }
            public double Similarity { get; set; }
            public double RunnerUpSimilarity { get; set; }
            public double Margin => Similarity - RunnerUpSimilarity;
            public int SupportCount { get; set; }
            public string Scope { get; set; } = "PROJECT";
            public string Reason { get; set; } = "";

            public bool Success =>
                !string.IsNullOrWhiteSpace(Label) &&
                Similarity >=
                    MepMemoryPromotionPolicy.RequiredPrototypeSimilarity(
                        Scope);
        }

        public static float[] BuildDescriptor(
            Bitmap source)
        {
            if (source == null ||
                source.Width < 4 ||
                source.Height < 4)
            {
                return Array.Empty<float>();
            }

            try
            {
                return BuildDescriptorOpenCv(source);
            }
            catch
            {
                // Prototype chỉ là lớp bổ sung; native OpenCV lỗi thì bỏ qua.
                return Array.Empty<float>();
            }
        }

        public static Evidence Match(
            Bitmap source,
            IEnumerable<MepPrototypeRecord> prototypes,
            string layerName,
            string matchMode,
            string blockKey,
            string geometryFingerprint)
        {
            float[] descriptor = BuildDescriptor(source);

            if (descriptor.Length == 0)
                return new Evidence();

            return MatchDescriptor(
                descriptor,
                prototypes,
                layerName,
                matchMode,
                blockKey,
                geometryFingerprint);
        }

        public static Evidence MatchDescriptor(
            float[] descriptor,
            IEnumerable<MepPrototypeRecord> prototypes,
            string layerName,
            string matchMode,
            string blockKey,
            string geometryFingerprint)
        {
            Evidence result = new Evidence();

            if (descriptor == null ||
                descriptor.Length == 0 ||
                prototypes == null)
            {
                return result;
            }

            List<LabelBucket> buckets =
                new List<LabelBucket>();

            foreach (MepPrototypeRecord prototype in prototypes)
            {
                if (prototype == null ||
                    string.IsNullOrWhiteSpace(prototype.Label) ||
                    prototype.Descriptor == null ||
                    prototype.Descriptor.Length != descriptor.Length)
                {
                    continue;
                }

                double cosine =
                    MepPrototypeMemoryStore.Cosine(
                        descriptor,
                        prototype.Descriptor);

                double metadataBonus =
                    BuildMetadataBonus(
                        prototype,
                        layerName,
                        matchMode,
                        blockKey,
                        geometryFingerprint);

                double adjusted =
                    Math.Max(
                        0.0,
                        Math.Min(
                            1.0,
                            cosine + metadataBonus));

                string key =
                    prototype.Label.Trim() + "|" +
                    (prototype.FollowDn ? "1" : "0");

                LabelBucket bucket =
                    buckets.FirstOrDefault(x =>
                        string.Equals(
                            x.Key,
                            key,
                            StringComparison.OrdinalIgnoreCase));

                if (bucket == null)
                {
                    bucket = new LabelBucket
                    {
                        Key = key,
                        Label = prototype.Label.Trim(),
                        FollowDn = prototype.FollowDn
                    };

                    buckets.Add(bucket);
                }

                if (adjusted > bucket.BestSimilarity)
                {
                    bucket.BestSimilarity = adjusted;
                    bucket.BestScope =
                        MepMemoryPromotionPolicy.NormalizeScope(
                            GetPrototypeScope(prototype));
                }

                double supportThreshold =
                    Math.Max(
                        0.88,
                        MepMemoryPromotionPolicy.RequiredPrototypeSimilarity(
                            GetPrototypeScope(prototype)) - 0.02);

                if (adjusted >= supportThreshold)
                {
                    bucket.SupportCount +=
                        Math.Max(
                            1,
                            prototype.Confirmations);
                }
            }

            if (buckets.Count == 0)
                return result;

            List<LabelBucket> ordered =
                buckets
                    .OrderByDescending(x => x.BestSimilarity)
                    .ThenByDescending(x => x.SupportCount)
                    .ToList();

            LabelBucket best = ordered[0];
            double runner =
                ordered.Count > 1
                    ? ordered[1].BestSimilarity
                    : 0.0;

            double margin = best.BestSimilarity - runner;

            string bestScope =
                MepMemoryPromotionPolicy.NormalizeScope(
                    best.BestScope);

            double requiredSimilarity =
                MepMemoryPromotionPolicy.RequiredPrototypeSimilarity(
                    bestScope);

            double requiredMargin =
                MepMemoryPromotionPolicy.RequiredPrototypeMargin(
                    bestScope);

            // Scope càng rộng càng phải chắc hơn. GLOBAL không được dùng cùng
            // threshold với prototype của chính project.
            if (best.BestSimilarity < requiredSimilarity ||
                (best.BestSimilarity < 0.965 &&
                 margin < requiredMargin))
            {
                return new Evidence
                {
                    Scope = bestScope,
                    Similarity = best.BestSimilarity,
                    RunnerUpSimilarity = runner,
                    SupportCount = best.SupportCount,
                    Reason =
                        "PROTOTYPE " +
                        bestScope +
                        " chưa đủ chắc | sim=" +
                        best.BestSimilarity.ToString(
                            "0.000",
                            CultureInfo.InvariantCulture) +
                        " | gate=" +
                        requiredSimilarity.ToString(
                            "0.000",
                            CultureInfo.InvariantCulture) +
                        " | margin=" +
                        margin.ToString(
                            "0.000",
                            CultureInfo.InvariantCulture)
                };
            }

            result.Label = best.Label;
            result.FollowDn = best.FollowDn;
            result.Similarity = best.BestSimilarity;
            result.RunnerUpSimilarity = runner;
            result.SupportCount = Math.Max(1, best.SupportCount);
            result.Scope = bestScope;
            result.Reason =
                "PROTOTYPE " +
                bestScope +
                " FEW-SHOT | sim=" +
                best.BestSimilarity.ToString(
                    "0.000",
                    CultureInfo.InvariantCulture) +
                " | margin=" +
                margin.ToString(
                    "0.000",
                    CultureInfo.InvariantCulture) +
                " | support=" +
                result.SupportCount.ToString(
                    CultureInfo.InvariantCulture);

            return result;
        }

        private static float[] BuildDescriptorOpenCv(
            Bitmap source)
        {
            using (Mat src = MepOpenCvBitmapBridge.ToMat(source))
            using (Mat gray = new Mat())
            using (Mat binary = new Mat())
            using (Mat resized = new Mat())
            {
                if (src.Empty())
                    return Array.Empty<float>();

                if (src.Channels() == 4)
                {
                    Cv2.CvtColor(
                        src,
                        gray,
                        ColorConversionCodes.BGRA2GRAY);
                }
                else if (src.Channels() == 3)
                {
                    Cv2.CvtColor(
                        src,
                        gray,
                        ColorConversionCodes.BGR2GRAY);
                }
                else if (src.Channels() == 1)
                {
                    src.CopyTo(gray);
                }
                else
                {
                    return Array.Empty<float>();
                }

                double mean = Cv2.Mean(gray).Val0;

                ThresholdTypes thresholdMode =
                    mean >= 127.0
                        ? ThresholdTypes.BinaryInv | ThresholdTypes.Otsu
                        : ThresholdTypes.Binary | ThresholdTypes.Otsu;

                Cv2.Threshold(
                    gray,
                    binary,
                    0.0,
                    255.0,
                    thresholdMode);

                OpenCvSharp.Rect bounds =
                    Cv2.BoundingRect(binary);

                if (bounds.Width <= 1 ||
                    bounds.Height <= 1)
                {
                    bounds = new OpenCvSharp.Rect(
                        0,
                        0,
                        binary.Cols,
                        binary.Rows);
                }

                // Padding nhẹ để crop không cắt sát nét ngoài.
                int padX = Math.Max(1, bounds.Width / 16);
                int padY = Math.Max(1, bounds.Height / 16);

                int x = Math.Max(0, bounds.X - padX);
                int y = Math.Max(0, bounds.Y - padY);
                int right = Math.Min(binary.Cols, bounds.X + bounds.Width + padX);
                int bottom = Math.Min(binary.Rows, bounds.Y + bounds.Height + padY);

                OpenCvSharp.Rect padded =
                    new OpenCvSharp.Rect(
                        x,
                        y,
                        Math.Max(1, right - x),
                        Math.Max(1, bottom - y));

                using (Mat roi = new Mat(binary, padded))
                {
                    Cv2.Resize(
                        roi,
                        resized,
                        new OpenCvSharp.Size(GridSize, GridSize),
                        0.0,
                        0.0,
                        InterpolationFlags.Area);
                }

                float[] baseDescriptor =
                    new float[GridSize * GridSize];

                for (int row = 0; row < GridSize; row++)
                {
                    for (int col = 0; col < GridSize; col++)
                    {
                        baseDescriptor[
                            row * GridSize + col] =
                            resized.At<byte>(row, col) / 255.0f;
                    }
                }

                float[][] rotations =
                    new[]
                    {
                        baseDescriptor,
                        Rotate90(baseDescriptor),
                        Rotate180(baseDescriptor),
                        Rotate270(baseDescriptor)
                    };

                float[] canonical = rotations[0];

                for (int i = 1; i < rotations.Length; i++)
                {
                    if (LexicographicallyLess(
                            rotations[i],
                            canonical))
                    {
                        canonical = rotations[i];
                    }
                }

                return Normalize(canonical);
            }
        }

        private static double BuildMetadataBonus(
            MepPrototypeRecord prototype,
            string layerName,
            string matchMode,
            string blockKey,
            string geometryFingerprint)
        {
            double bonus = 0.0;

            if (!string.IsNullOrWhiteSpace(geometryFingerprint) &&
                string.Equals(
                    NormalizeKey(prototype.GeometryFingerprint),
                    NormalizeKey(geometryFingerprint),
                    StringComparison.OrdinalIgnoreCase))
            {
                bonus += 0.045;
            }

            if (!string.IsNullOrWhiteSpace(blockKey) &&
                string.Equals(
                    NormalizeKey(prototype.BlockKey),
                    NormalizeKey(blockKey),
                    StringComparison.OrdinalIgnoreCase))
            {
                bonus += 0.030;
            }

            if (!string.IsNullOrWhiteSpace(layerName) &&
                string.Equals(
                    NormalizeKey(prototype.LayerName),
                    NormalizeKey(layerName),
                    StringComparison.OrdinalIgnoreCase))
            {
                bonus += 0.012;
            }

            if (!string.IsNullOrWhiteSpace(matchMode) &&
                string.Equals(
                    NormalizeKey(prototype.MatchMode),
                    NormalizeKey(matchMode),
                    StringComparison.OrdinalIgnoreCase))
            {
                bonus += 0.008;
            }

            string scope =
                MepMemoryPromotionPolicy.NormalizeScope(
                    GetPrototypeScope(prototype));

            // Local prototype được ưu tiên nhẹ khi similarity gần bằng nhau.
            if (scope == "PROJECT")
                bonus += 0.006;
            else if (scope == "COMPANY")
                bonus += 0.002;

            return Math.Min(0.071, bonus);
        }

        private static float[] Rotate90(float[] source)
        {
            float[] result = new float[source.Length];

            for (int y = 0; y < GridSize; y++)
            {
                for (int x = 0; x < GridSize; x++)
                {
                    result[x * GridSize + (GridSize - 1 - y)] =
                        source[y * GridSize + x];
                }
            }

            return result;
        }

        private static float[] Rotate180(float[] source)
        {
            return Rotate90(Rotate90(source));
        }

        private static float[] Rotate270(float[] source)
        {
            return Rotate90(Rotate180(source));
        }

        private static bool LexicographicallyLess(
            float[] a,
            float[] b)
        {
            int count = Math.Min(a?.Length ?? 0, b?.Length ?? 0);

            for (int i = 0; i < count; i++)
            {
                int av = (int)Math.Round(a[i] * 255.0f);
                int bv = (int)Math.Round(b[i] * 255.0f);

                if (av < bv)
                    return true;

                if (av > bv)
                    return false;
            }

            return (a?.Length ?? 0) < (b?.Length ?? 0);
        }

        private static float[] Normalize(
            float[] descriptor)
        {
            if (descriptor == null || descriptor.Length == 0)
                return Array.Empty<float>();

            double sumSquares = 0.0;

            for (int i = 0; i < descriptor.Length; i++)
            {
                double v = descriptor[i];
                sumSquares += v * v;
            }

            double norm = Math.Sqrt(sumSquares);
            if (norm < 1e-8)
                return Array.Empty<float>();

            float[] result = new float[descriptor.Length];

            for (int i = 0; i < descriptor.Length; i++)
                result[i] = (float)(descriptor[i] / norm);

            return result;
        }

        // STEP29F.3 COMPAT: đọc Scope qua reflection để vẫn compile nếu
        // Visual Studio còn giữ metadata cũ của MepPrototypeRecord trong IntelliSense.
        // Với file STEP29F.3 mới, property Scope vẫn được dùng bình thường.
        private static string GetPrototypeScope(MepPrototypeRecord prototype)
        {
            if (prototype == null)
                return "PROJECT";

            try
            {
                System.Reflection.PropertyInfo property =
                    typeof(MepPrototypeRecord).GetProperty(
                        "Scope",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic);

                object raw = property != null
                    ? property.GetValue(prototype)
                    : null;

                return MepMemoryPromotionPolicy.NormalizeScope(
                    raw != null ? raw.ToString() : "PROJECT");
            }
            catch
            {
                return "PROJECT";
            }
        }

        private static string NormalizeKey(string value)
        {
            return (value ?? "")
                .Trim()
                .Replace('/', '\\')
                .ToUpperInvariant();
        }

        private sealed class LabelBucket
        {
            public string Key { get; set; } = "";
            public string Label { get; set; } = "";
            public bool FollowDn { get; set; }
            public double BestSimilarity { get; set; }
            public int SupportCount { get; set; }
            public string BestScope { get; set; } = "PROJECT";
        }
    }
}
