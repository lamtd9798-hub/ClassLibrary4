#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ClassLibrary4
{
    public sealed class AiDatasetSample
    {
        public string SampleHash { get; set; } = "";
        public string Signature { get; set; } = "";
        public string MatchMode { get; set; } = "BLOCK";
        public string BlockKey { get; set; } = "";
        public string GeometryFingerprint { get; set; } = "";
        public string Label { get; set; } = "";
        public string Decision { get; set; } = "POSITIVE";
        public string HardNegativeLabel { get; set; } = "";
        public bool FollowDn { get; set; }
        public string Source { get; set; } = "";
        public string ImagePath { get; set; } = "";
        public bool CloudSynced { get; set; }
        public string UpdatedUtc { get; set; } = "";
    }

    public sealed class AiDatasetLocalSummary
    {
        public int Total { get; set; }
        public int Pending { get; set; }
        public int Synced { get; set; }
        public int Positive { get; set; }
        public int Negative { get; set; }
        public int ClassCount { get; set; }
    }

    public sealed class AiDatasetCloudSummary
    {
        [JsonPropertyName("total_samples")]
        public int TotalSamples { get; set; }

        [JsonPropertyName("total_votes")]
        public int TotalVotes { get; set; }

        [JsonPropertyName("class_count")]
        public int ClassCount { get; set; }

        [JsonPropertyName("approved")]
        public int Approved { get; set; }

        [JsonPropertyName("pending")]
        public int Pending { get; set; }

        [JsonPropertyName("conflict")]
        public int Conflict { get; set; }
    }

    public sealed class AiDatasetSyncResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public int Uploaded { get; set; }
        public int Duplicate { get; set; }
        public int Failed { get; set; }
        public int PendingAfterSync { get; set; }
        public AiDatasetCloudSummary CloudSummary { get; set; } =
            new AiDatasetCloudSummary();
    }

    public sealed class AiDatasetManager
    {
        private static readonly HttpClient Http =
            new HttpClient
            {
                Timeout =
                    TimeSpan.FromSeconds(45)
            };

        private static readonly JsonSerializerOptions JsonOptions =
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive =
                    true,
                WriteIndented =
                    true
            };

        public string BaseFolder { get; }
        public string ImagesFolder { get; }
        public string IndexPath { get; }

        public AiDatasetManager()
        {
            string appData =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData);

            if (string.IsNullOrWhiteSpace(
                    appData))
            {
                appData =
                    Path.GetTempPath();
            }

            BaseFolder =
                Path.Combine(
                    appData,
                    "TDL_MEP",
                    "AI_Dataset");

            ImagesFolder =
                Path.Combine(
                    BaseFolder,
                    "images");

            IndexPath =
                Path.Combine(
                    BaseFolder,
                    "dataset_index_v1.json");

            Directory.CreateDirectory(
                ImagesFolder);
        }

        public List<AiDatasetSample> LoadSamples()
        {
            try
            {
                if (!File.Exists(
                        IndexPath))
                {
                    return
                        new List<AiDatasetSample>();
                }

                return
                    JsonSerializer.Deserialize<List<AiDatasetSample>>(
                        File.ReadAllText(
                            IndexPath,
                            Encoding.UTF8),
                        JsonOptions) ??
                    new List<AiDatasetSample>();
            }
            catch
            {
                return
                    new List<AiDatasetSample>();
            }
        }

        public AiDatasetLocalSummary GetLocalSummary()
        {
            List<AiDatasetSample> samples =
                LoadSamples();

            return
                new AiDatasetLocalSummary
                {
                    Total =
                        samples.Count,
                    Pending =
                        samples.Count(
                            x =>
                                x != null &&
                                !x.CloudSynced),
                    Synced =
                        samples.Count(
                            x =>
                                x != null &&
                                x.CloudSynced),
                    Positive =
                        samples.Count(
                            x =>
                                x != null &&
                                !string.Equals(
                                    x.Decision,
                                    "NEGATIVE",
                                    StringComparison.OrdinalIgnoreCase)),
                    Negative =
                        samples.Count(
                            x =>
                                x != null &&
                                string.Equals(
                                    x.Decision,
                                    "NEGATIVE",
                                    StringComparison.OrdinalIgnoreCase)),
                    ClassCount =
                        samples
                            .Where(
                                x =>
                                    x != null &&
                                    !string.IsNullOrWhiteSpace(
                                        x.Label) &&
                                    !string.Equals(
                                        x.Decision,
                                        "NEGATIVE",
                                        StringComparison.OrdinalIgnoreCase))
                            .Select(
                                x =>
                                    x.Label.Trim())
                            .Distinct(
                                StringComparer.OrdinalIgnoreCase)
                            .Count()
                };
        }

        public AiDatasetSample CaptureSample(
            Bitmap sourceBitmap,
            string signature,
            string matchMode,
            string blockKey,
            string geometryFingerprint,
            string label,
            bool followDn,
            string decision,
            string source,
            string hardNegativeLabel)
        {
            if (sourceBitmap == null)
                return null;

            string normalizedDecision =
                string.Equals(
                    decision,
                    "NEGATIVE",
                    StringComparison.OrdinalIgnoreCase)
                    ? "NEGATIVE"
                    : "POSITIVE";

            string normalizedLabel =
                (label ?? "")
                    .Trim();

            using (Bitmap normalized =
                NormalizeSymbolBitmap(
                    sourceBitmap,
                    224))
            {
                if (normalized == null)
                    return null;

                byte[] pngBytes =
                    BitmapToPngBytes(
                        normalized);

                if (pngBytes == null ||
                    pngBytes.Length == 0)
                {
                    return null;
                }

                string hash =
                    Sha256Hex(
                        pngBytes);

                if (string.IsNullOrWhiteSpace(
                        hash))
                {
                    return null;
                }

                string imagePath =
                    Path.Combine(
                        ImagesFolder,
                        hash +
                        ".png");

                if (!File.Exists(
                        imagePath))
                {
                    File.WriteAllBytes(
                        imagePath,
                        pngBytes);
                }

                List<AiDatasetSample> samples =
                    LoadSamples();

                AiDatasetSample existing =
                    samples.FirstOrDefault(
                        x =>
                            x != null &&
                            string.Equals(
                                x.SampleHash,
                                hash,
                                StringComparison.OrdinalIgnoreCase));

                bool changed =
                    false;

                if (existing == null)
                {
                    existing =
                        new AiDatasetSample
                        {
                            SampleHash =
                                hash,
                            ImagePath =
                                imagePath
                        };

                    samples.Add(
                        existing);

                    changed =
                        true;
                }

                string oldSignature =
                    existing.Signature ?? "";

                string oldLabel =
                    existing.Label ?? "";

                string oldDecision =
                    existing.Decision ?? "";

                string oldHardNegative =
                    existing.HardNegativeLabel ?? "";

                bool oldFollowDn =
                    existing.FollowDn;

                existing.Signature =
                    signature ?? "";

                existing.MatchMode =
                    string.IsNullOrWhiteSpace(
                        matchMode)
                        ? "BLOCK"
                        : matchMode;

                existing.BlockKey =
                    blockKey ?? "";

                existing.GeometryFingerprint =
                    geometryFingerprint ?? "";

                existing.Label =
                    normalizedDecision ==
                        "POSITIVE"
                        ? normalizedLabel
                        : "";

                existing.Decision =
                    normalizedDecision;

                existing.HardNegativeLabel =
                    (hardNegativeLabel ?? "")
                        .Trim();

                existing.FollowDn =
                    normalizedDecision ==
                        "POSITIVE" &&
                    followDn;

                existing.Source =
                    source ?? "";

                existing.ImagePath =
                    imagePath;

                existing.UpdatedUtc =
                    DateTime.UtcNow.ToString(
                        "O",
                        CultureInfo.InvariantCulture);

                if (!string.Equals(
                        oldSignature,
                        existing.Signature,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        oldLabel,
                        existing.Label,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        oldDecision,
                        existing.Decision,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        oldHardNegative,
                        existing.HardNegativeLabel,
                        StringComparison.OrdinalIgnoreCase) ||
                    oldFollowDn !=
                        existing.FollowDn)
                {
                    changed =
                        true;
                }

                // Nếu user sửa label/decision của chính hình đã từng sync,
                // bắt buộc upload lại vote mới cho cùng sample hash.
                if (changed)
                {
                    existing.CloudSynced =
                        false;
                }

                SaveSamples(
                    samples);

                return
                    existing;
            }
        }

        public bool DeleteSample(
            string sampleHash)
        {
            if (string.IsNullOrWhiteSpace(
                    sampleHash))
            {
                return false;
            }

            List<AiDatasetSample> samples =
                LoadSamples();

            AiDatasetSample sample =
                samples.FirstOrDefault(
                    x =>
                        x != null &&
                        string.Equals(
                            x.SampleHash,
                            sampleHash,
                            StringComparison.OrdinalIgnoreCase));

            if (sample == null)
                return false;

            samples.Remove(
                sample);

            try
            {
                if (!string.IsNullOrWhiteSpace(
                        sample.ImagePath) &&
                    File.Exists(
                        sample.ImagePath))
                {
                    File.Delete(
                        sample.ImagePath);
                }
            }
            catch
            {
            }

            SaveSamples(
                samples);

            return true;
        }

        public async Task<AiDatasetSyncResult> SyncPendingAsync(
            AiCloudConfig config,
            CancellationToken cancellationToken =
                default)
        {
            if (config == null)
            {
                return
                    new AiDatasetSyncResult
                    {
                        Success =
                            false,
                        Message =
                            "Chưa có cấu hình AI Cloud."
                    };
            }

            config.Normalize();

            if (!config.IsConfigured)
            {
                return
                    new AiDatasetSyncResult
                    {
                        Success =
                            false,
                        Message =
                            "AI Cloud chưa được cấu hình đầy đủ."
                    };
            }

            List<AiDatasetSample> samples =
                LoadSamples();

            List<AiDatasetSample> pending =
                samples
                    .Where(
                        x =>
                            x != null &&
                            !x.CloudSynced &&
                            !string.IsNullOrWhiteSpace(
                                x.SampleHash) &&
                            !string.IsNullOrWhiteSpace(
                                x.ImagePath) &&
                            File.Exists(
                                x.ImagePath))
                    .ToList();

            int uploaded =
                0;

            int duplicate =
                0;

            int failed =
                0;

            StringBuilder failureText =
                new StringBuilder();

            foreach (AiDatasetSample sample
                in pending)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                try
                {
                    byte[] bytes =
                        File.ReadAllBytes(
                            sample.ImagePath);

                    string imageBase64 =
                        Convert.ToBase64String(
                            bytes);

                    string responseText =
                        await PostEdgeFunctionAsync(
                            config,
                            new
                            {
                                action =
                                    "ingest",
                                company_code =
                                    config.CompanyCode,
                                sync_key =
                                    config.CompanySyncKey,
                                voter_id =
                                    config.VoterId,
                                sample_hash =
                                    sample.SampleHash,
                                signature =
                                    sample.Signature,
                                match_mode =
                                    sample.MatchMode,
                                block_key =
                                    sample.BlockKey,
                                geometry_fingerprint =
                                    sample.GeometryFingerprint,
                                label =
                                    sample.Label,
                                decision =
                                    sample.Decision,
                                hard_negative_label =
                                    sample.HardNegativeLabel,
                                follow_dn =
                                    sample.FollowDn,
                                source =
                                    sample.Source,
                                client_updated_utc =
                                    sample.UpdatedUtc,
                                image_base64 =
                                    imageBase64
                            },
                            cancellationToken);

                    using (JsonDocument doc =
                        JsonDocument.Parse(
                            responseText))
                    {
                        JsonElement root =
                            doc.RootElement;

                        bool ok =
                            root.TryGetProperty(
                                "ok",
                                out JsonElement okElement) &&
                            okElement.GetBoolean();

                        if (!ok)
                        {
                            failed++;

                            if (root.TryGetProperty(
                                    "error",
                                    out JsonElement errorElement))
                            {
                                failureText.AppendLine(
                                    errorElement.GetString());
                            }

                            continue;
                        }

                        bool wasDuplicate =
                            root.TryGetProperty(
                                "duplicate",
                                out JsonElement duplicateElement) &&
                            duplicateElement.GetBoolean();

                        if (wasDuplicate)
                        {
                            duplicate++;
                        }
                        else
                        {
                            uploaded++;
                        }

                        sample.CloudSynced =
                            true;
                    }
                }
                catch (Exception ex)
                {
                    failed++;

                    if (failureText.Length <
                        1800)
                    {
                        failureText.AppendLine(
                            ex.Message);
                    }
                }
            }

            SaveSamples(
                samples);

            AiDatasetCloudSummary cloudSummary =
                await GetCloudSummaryAsync(
                    config,
                    cancellationToken);

            return
                new AiDatasetSyncResult
                {
                    Success =
                        failed == 0,
                    Message =
                        failed == 0
                            ? "Đồng bộ Dataset thành công."
                            : "Có " +
                              failed +
                              " mẫu chưa upload được.\n" +
                              failureText.ToString(),
                    Uploaded =
                        uploaded,
                    Duplicate =
                        duplicate,
                    Failed =
                        failed,
                    PendingAfterSync =
                        samples.Count(
                            x =>
                                x != null &&
                                !x.CloudSynced),
                    CloudSummary =
                        cloudSummary ??
                        new AiDatasetCloudSummary()
                };
        }

        public async Task<AiDatasetCloudSummary> GetCloudSummaryAsync(
            AiCloudConfig config,
            CancellationToken cancellationToken =
                default)
        {
            if (config == null)
                return new AiDatasetCloudSummary();

            config.Normalize();

            if (!config.IsConfigured)
                return new AiDatasetCloudSummary();

            try
            {
                string responseText =
                    await PostEdgeFunctionAsync(
                        config,
                        new
                        {
                            action =
                                "summary",
                            company_code =
                                config.CompanyCode,
                            sync_key =
                                config.CompanySyncKey,
                            voter_id =
                                config.VoterId
                        },
                        cancellationToken);

                using (JsonDocument doc =
                    JsonDocument.Parse(
                        responseText))
                {
                    JsonElement root =
                        doc.RootElement;

                    if (!root.TryGetProperty(
                            "ok",
                            out JsonElement okElement) ||
                        !okElement.GetBoolean())
                    {
                        return
                            new AiDatasetCloudSummary();
                    }

                    if (!root.TryGetProperty(
                            "summary",
                            out JsonElement summaryElement))
                    {
                        return
                            new AiDatasetCloudSummary();
                    }

                    return
                        JsonSerializer.Deserialize<AiDatasetCloudSummary>(
                            summaryElement.GetRawText(),
                            JsonOptions) ??
                        new AiDatasetCloudSummary();
                }
            }
            catch
            {
                return
                    new AiDatasetCloudSummary();
            }
        }

        private async Task<string> PostEdgeFunctionAsync(
            AiCloudConfig config,
            object body,
            CancellationToken cancellationToken)
        {
            string url =
                config.ProjectUrl.TrimEnd('/') +
                "/functions/v1/ai-dataset-ingest";

            string json =
                JsonSerializer.Serialize(
                    body,
                    JsonOptions);

            using (HttpRequestMessage request =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    url))
            {
                request.Headers.TryAddWithoutValidation(
                    "apikey",
                    config.PublishableKey);

                if (config.PublishableKey.StartsWith(
                        "eyJ",
                        StringComparison.Ordinal))
                {
                    request.Headers.Authorization =
                        new AuthenticationHeaderValue(
                            "Bearer",
                            config.PublishableKey);
                }

                request.Headers.TryAddWithoutValidation(
                    "Accept",
                    "application/json");

                request.Content =
                    new StringContent(
                        json,
                        Encoding.UTF8,
                        "application/json");

                using (HttpResponseMessage response =
                    await Http.SendAsync(
                        request,
                        cancellationToken))
                {
                    string responseText =
                        await response.Content.ReadAsStringAsync(
                            cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        throw
                            new InvalidOperationException(
                                "Edge Function ai-dataset-ingest lỗi " +
                                ((int)response.StatusCode)
                                    .ToString(
                                        CultureInfo.InvariantCulture) +
                                " " +
                                response.ReasonPhrase +
                                "\n" +
                                responseText);
                    }

                    return
                        responseText;
                }
            }
        }

        private void SaveSamples(
            List<AiDatasetSample> samples)
        {
            try
            {
                Directory.CreateDirectory(
                    BaseFolder);

                Directory.CreateDirectory(
                    ImagesFolder);

                File.WriteAllText(
                    IndexPath,
                    JsonSerializer.Serialize(
                        samples ??
                        new List<AiDatasetSample>(),
                        JsonOptions),
                    Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static byte[] BitmapToPngBytes(
            Bitmap bitmap)
        {
            if (bitmap == null)
                return null;

            using (MemoryStream ms =
                new MemoryStream())
            {
                bitmap.Save(
                    ms,
                    ImageFormat.Png);

                return
                    ms.ToArray();
            }
        }

        private static string Sha256Hex(
            byte[] bytes)
        {
            if (bytes == null ||
                bytes.Length == 0)
            {
                return "";
            }

            using (SHA256 sha =
                SHA256.Create())
            {
                byte[] hash =
                    sha.ComputeHash(
                        bytes);

                StringBuilder sb =
                    new StringBuilder(
                        hash.Length * 2);

                foreach (byte b
                    in hash)
                {
                    sb.Append(
                        b.ToString(
                            "x2",
                            CultureInfo.InvariantCulture));
                }

                return
                    sb.ToString();
            }
        }

        private static Bitmap NormalizeSymbolBitmap(
            Bitmap source,
            int outputSize)
        {
            if (source == null ||
                outputSize < 64)
            {
                return null;
            }

            Color background =
                EstimateBackgroundColor(
                    source);

            Rectangle foreground =
                FindForegroundBounds(
                    source,
                    background);

            if (foreground.Width <= 1 ||
                foreground.Height <= 1)
            {
                foreground =
                    new Rectangle(
                        0,
                        0,
                        source.Width,
                        source.Height);
            }

            Bitmap result =
                new Bitmap(
                    outputSize,
                    outputSize,
                    PixelFormat.Format24bppRgb);

            using (Graphics g =
                Graphics.FromImage(
                    result))
            {
                g.Clear(
                    Color.Black);

                g.SmoothingMode =
                    SmoothingMode.HighQuality;

                g.InterpolationMode =
                    InterpolationMode.HighQualityBicubic;

                g.PixelOffsetMode =
                    PixelOffsetMode.HighQuality;

                float maxSide =
                    Math.Max(
                        foreground.Width,
                        foreground.Height);

                float targetSide =
                    outputSize *
                    0.78f;

                float scale =
                    targetSide /
                    Math.Max(
                        1.0f,
                        maxSide);

                float drawWidth =
                    foreground.Width *
                    scale;

                float drawHeight =
                    foreground.Height *
                    scale;

                float dx =
                    (outputSize -
                     drawWidth) *
                    0.5f;

                float dy =
                    (outputSize -
                     drawHeight) *
                    0.5f;

                using (Bitmap mask =
                    CreateWhiteForegroundMask(
                        source,
                        background))
                {
                    g.DrawImage(
                        mask,
                        new RectangleF(
                            dx,
                            dy,
                            drawWidth,
                            drawHeight),
                        foreground,
                        GraphicsUnit.Pixel);
                }
            }

            return
                result;
        }

        private static Bitmap CreateWhiteForegroundMask(
            Bitmap source,
            Color background)
        {
            Bitmap mask =
                new Bitmap(
                    source.Width,
                    source.Height,
                    PixelFormat.Format24bppRgb);

            for (int y = 0;
                y < source.Height;
                y++)
            {
                for (int x = 0;
                    x < source.Width;
                    x++)
                {
                    Color c =
                        source.GetPixel(
                            x,
                            y);

                    int distance =
                        Math.Abs(
                            c.R -
                            background.R) +
                        Math.Abs(
                            c.G -
                            background.G) +
                        Math.Abs(
                            c.B -
                            background.B);

                    bool foreground =
                        distance >=
                            55 ||
                        c.GetBrightness() >=
                            0.72f;

                    mask.SetPixel(
                        x,
                        y,
                        foreground
                            ? Color.White
                            : Color.Black);
                }
            }

            return
                mask;
        }

        private static Rectangle FindForegroundBounds(
            Bitmap source,
            Color background)
        {
            int minX =
                source.Width;

            int minY =
                source.Height;

            int maxX =
                -1;

            int maxY =
                -1;

            for (int y = 0;
                y < source.Height;
                y++)
            {
                for (int x = 0;
                    x < source.Width;
                    x++)
                {
                    Color c =
                        source.GetPixel(
                            x,
                            y);

                    int distance =
                        Math.Abs(
                            c.R -
                            background.R) +
                        Math.Abs(
                            c.G -
                            background.G) +
                        Math.Abs(
                            c.B -
                            background.B);

                    if (distance <
                            55 &&
                        c.GetBrightness() <
                            0.72f)
                    {
                        continue;
                    }

                    minX =
                        Math.Min(
                            minX,
                            x);

                    minY =
                        Math.Min(
                            minY,
                            y);

                    maxX =
                        Math.Max(
                            maxX,
                            x);

                    maxY =
                        Math.Max(
                            maxY,
                            y);
                }
            }

            if (maxX < minX ||
                maxY < minY)
            {
                return
                    Rectangle.Empty;
            }

            return
                Rectangle.FromLTRB(
                    minX,
                    minY,
                    maxX + 1,
                    maxY + 1);
        }

        private static Color EstimateBackgroundColor(
            Bitmap source)
        {
            if (source == null ||
                source.Width == 0 ||
                source.Height == 0)
            {
                return
                    Color.FromArgb(
                        31,
                        42,
                        52);
            }

            List<Color> samples =
                new List<Color>
                {
                    source.GetPixel(
                        0,
                        0),
                    source.GetPixel(
                        source.Width - 1,
                        0),
                    source.GetPixel(
                        0,
                        source.Height - 1),
                    source.GetPixel(
                        source.Width - 1,
                        source.Height - 1)
                };

            return
                Color.FromArgb(
                    (int)samples.Average(
                        c =>
                            c.R),
                    (int)samples.Average(
                        c =>
                            c.G),
                    (int)samples.Average(
                        c =>
                            c.B));
        }
    }
}
