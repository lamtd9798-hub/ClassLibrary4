#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ClassLibrary4
{
    public sealed class AiTrainingManifestRow
    {
        [JsonPropertyName("sample_hash")]
        public string SampleHash { get; set; } = "";

        [JsonPropertyName("storage_bucket")]
        public string StorageBucket { get; set; } = "";

        [JsonPropertyName("storage_path")]
        public string StoragePath { get; set; } = "";

        [JsonPropertyName("class_code")]
        public string ClassCode { get; set; } = "";

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = "";

        [JsonPropertyName("follow_dn")]
        public bool FollowDn { get; set; }

        [JsonPropertyName("approval_source")]
        public string ApprovalSource { get; set; } = "";

        [JsonPropertyName("positive_votes")]
        public int PositiveVotes { get; set; }

        [JsonPropertyName("hard_negative")]
        public string HardNegative { get; set; } = "";

        [JsonPropertyName("signed_url")]
        public string SignedUrl { get; set; } = "";
    }

    public sealed class AiTrainingExportResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string ExportFolder { get; set; } = "";
        public string ZipPath { get; set; } = "";
        public int SampleCount { get; set; }
        public int ClassCount { get; set; }
        public int Downloaded { get; set; }
        public int Failed { get; set; }
        public Dictionary<string, int> ClassCounts { get; set; } =
            new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);
    }

    public sealed class AiOnnxTrainingExportClient
    {
        private static readonly HttpClient Http =
            new HttpClient
            {
                Timeout =
                    TimeSpan.FromSeconds(60)
            };

        private static readonly JsonSerializerOptions JsonOptions =
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive =
                    true,
                WriteIndented =
                    true
            };

        public string DefaultTrainingRoot
        {
            get
            {
                string documents =
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.MyDocuments);

                if (string.IsNullOrWhiteSpace(
                        documents))
                {
                    documents =
                        Path.GetTempPath();
                }

                return
                    Path.Combine(
                        documents,
                        "TDL_MEP_AI_TRAINING");
            }
        }

        public async Task<AiTrainingExportResult> ExportApprovedDatasetAsync(
            AiCloudConfig config,
            string adminKey,
            string outputRoot = "",
            CancellationToken cancellationToken = default)
        {
            AiTrainingExportResult result =
                new AiTrainingExportResult();

            if (config == null)
            {
                result.Message =
                    "Chưa có cấu hình AI Cloud.";
                return result;
            }

            config.Normalize();

            if (!config.IsConfigured)
            {
                result.Message =
                    "AI Cloud chưa cấu hình đầy đủ.";
                return result;
            }

            if (string.IsNullOrWhiteSpace(
                    adminKey))
            {
                result.Message =
                    "Chưa nhập Admin Key.";
                return result;
            }

            string response =
                await PostAsync(
                    config,
                    new
                    {
                        action =
                            "export",
                        company_code =
                            config.CompanyCode,
                        sync_key =
                            config.CompanySyncKey,
                        admin_key =
                            adminKey
                    },
                    cancellationToken);

            List<AiTrainingManifestRow> rows;

            using (JsonDocument doc =
                JsonDocument.Parse(
                    response))
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
                    result.Message =
                        root.TryGetProperty(
                            "error",
                            out JsonElement errorElement)
                            ? errorElement.GetString()
                            : "Cloud không trả training manifest.";

                    return result;
                }

                if (!root.TryGetProperty(
                        "rows",
                        out JsonElement rowsElement))
                {
                    result.Message =
                        "Training manifest đang trống.";
                    return result;
                }

                rows =
                    JsonSerializer.Deserialize<List<AiTrainingManifestRow>>(
                        rowsElement.GetRawText(),
                        JsonOptions) ??
                    new List<AiTrainingManifestRow>();
            }

            rows =
                rows
                    .Where(
                        x =>
                            x != null &&
                            !string.IsNullOrWhiteSpace(
                                x.SampleHash) &&
                            !string.IsNullOrWhiteSpace(
                                x.ClassCode) &&
                            !string.IsNullOrWhiteSpace(
                                x.SignedUrl))
                    .ToList();

            if (rows.Count == 0)
            {
                result.Message =
                    "Chưa có mẫu APPROVED có CLASS CODE để xuất bộ train.\n\n" +
                    "Hãy duyệt/gán class thêm trong DUYỆT CLOUD.";
                return result;
            }

            string rootFolder =
                string.IsNullOrWhiteSpace(
                    outputRoot)
                    ? DefaultTrainingRoot
                    : outputRoot;

            Directory.CreateDirectory(
                rootFolder);

            string exportFolder =
                Path.Combine(
                    rootFolder,
                    "TRAIN_" +
                    DateTime.Now.ToString(
                        "yyyyMMdd_HHmmss",
                        CultureInfo.InvariantCulture));

            string imagesFolder =
                Path.Combine(
                    exportFolder,
                    "images");

            Directory.CreateDirectory(
                imagesFolder);

            int downloaded =
                0;

            int failed =
                0;

            foreach (AiTrainingManifestRow row
                in rows)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                try
                {
                    string classFolder =
                        Path.Combine(
                            imagesFolder,
                            SanitizePathPart(
                                row.ClassCode));

                    Directory.CreateDirectory(
                        classFolder);

                    string filePath =
                        Path.Combine(
                            classFolder,
                            row.SampleHash +
                            ".png");

                    if (!File.Exists(
                            filePath))
                    {
                        byte[] bytes =
                            await Http.GetByteArrayAsync(
                                row.SignedUrl,
                                cancellationToken);

                        File.WriteAllBytes(
                            filePath,
                            bytes);
                    }

                    downloaded++;
                }
                catch
                {
                    failed++;
                }
            }

            List<AiTrainingManifestRow> downloadedRows =
                rows
                    .Where(
                        row =>
                            File.Exists(
                                Path.Combine(
                                    imagesFolder,
                                    SanitizePathPart(
                                        row.ClassCode),
                                    row.SampleHash +
                                    ".png")))
                    .ToList();

            Dictionary<string, string> displayByClass =
                downloadedRows
                    .GroupBy(
                        x =>
                            x.ClassCode,
                        StringComparer.OrdinalIgnoreCase)
                    .OrderBy(
                        g =>
                            g.Key,
                        StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        g =>
                            g.Key,
                        g =>
                            g.Select(
                                x =>
                                    x.DisplayName)
                             .FirstOrDefault(
                                 x =>
                                     !string.IsNullOrWhiteSpace(
                                         x)) ??
                            g.Key,
                        StringComparer.OrdinalIgnoreCase);

            List<string> orderedClasses =
                displayByClass
                    .Keys
                    .OrderBy(
                        x =>
                            x,
                        StringComparer.OrdinalIgnoreCase)
                    .ToList();

            List<string> labels =
                orderedClasses
                    .Select(
                        code =>
                            code +
                            "|" +
                            displayByClass[code])
                    .ToList();

            File.WriteAllLines(
                Path.Combine(
                    exportFolder,
                    "mep_symbol_labels.txt"),
                labels,
                new UTF8Encoding(
                    true));

            List<object> manifest =
                downloadedRows
                    .Select(
                        row =>
                            new
                            {
                                sample_hash =
                                    row.SampleHash,
                                class_code =
                                    row.ClassCode,
                                display_name =
                                    row.DisplayName,
                                image_path =
                                    Path.Combine(
                                        "images",
                                        SanitizePathPart(
                                            row.ClassCode),
                                        row.SampleHash +
                                        ".png")
                                        .Replace(
                                            '\\',
                                            '/'),
                                follow_dn =
                                    row.FollowDn,
                                approval_source =
                                    row.ApprovalSource,
                                positive_votes =
                                    row.PositiveVotes,
                                hard_negative =
                                    row.HardNegative
                            })
                    .Cast<object>()
                    .ToList();

            File.WriteAllText(
                Path.Combine(
                    exportFolder,
                    "manifest.json"),
                JsonSerializer.Serialize(
                    manifest,
                    JsonOptions),
                Encoding.UTF8);

            Dictionary<string, int> classCounts =
                downloadedRows
                    .GroupBy(
                        x =>
                            x.ClassCode,
                        StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        g =>
                            g.Key,
                        g =>
                            g.Count(),
                        StringComparer.OrdinalIgnoreCase);

            object configJson =
                new
                {
                    version =
                        1,
                    company_code =
                        config.CompanyCode,
                    exported_utc =
                        DateTime.UtcNow.ToString(
                            "O",
                            CultureInfo.InvariantCulture),
                    image_size =
                        224,
                    input_channels =
                        1,
                    model_contract =
                        "float32 NCHW [1,1,224,224] -> logits [1,classes]",
                    sample_count =
                        downloadedRows.Count,
                    class_count =
                        classCounts.Count,
                    classes =
                        orderedClasses.Select(
                            code =>
                                new
                                {
                                    class_code =
                                        code,
                                    display_name =
                                        displayByClass[code],
                                    sample_count =
                                        classCounts[code]
                                })
                };

            File.WriteAllText(
                Path.Combine(
                    exportFolder,
                    "training_config.json"),
                JsonSerializer.Serialize(
                    configJson,
                    JsonOptions),
                Encoding.UTF8);

            File.WriteAllText(
                Path.Combine(
                    exportFolder,
                    "README_TRAINING_PACK.txt"),
                BuildPackReadme(
                    downloadedRows.Count,
                    classCounts),
                Encoding.UTF8);

            string zipPath =
                exportFolder +
                ".zip";

            try
            {
                if (File.Exists(
                        zipPath))
                {
                    File.Delete(
                        zipPath);
                }

                ZipFile.CreateFromDirectory(
                    exportFolder,
                    zipPath,
                    CompressionLevel.Fastest,
                    false);
            }
            catch
            {
                zipPath =
                    "";
            }

            result.Success =
                downloadedRows.Count > 0 &&
                failed == 0;

            result.Message =
                failed == 0
                    ? "Đã xuất bộ train."
                    : "Đã xuất bộ train nhưng có " +
                      failed +
                      " ảnh tải lỗi.";

            result.ExportFolder =
                exportFolder;

            result.ZipPath =
                zipPath;

            result.SampleCount =
                downloadedRows.Count;

            result.ClassCount =
                classCounts.Count;

            result.Downloaded =
                downloaded;

            result.Failed =
                failed;

            result.ClassCounts =
                classCounts;

            return result;
        }

        private async Task<string> PostAsync(
            AiCloudConfig config,
            object body,
            CancellationToken cancellationToken)
        {
            string url =
                config.ProjectUrl.TrimEnd('/') +
                "/functions/v1/ai-training-export";

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
                                "Edge Function ai-training-export lỗi " +
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

        private static string SanitizePathPart(
            string value)
        {
            string source =
                (value ?? "")
                    .Trim();

            StringBuilder sb =
                new StringBuilder();

            foreach (char c
                in source)
            {
                if (char.IsLetterOrDigit(
                        c) ||
                    c == '_' ||
                    c == '-' ||
                    c == '.')
                {
                    sb.Append(
                        c);
                }
                else
                {
                    sb.Append(
                        '_');
                }
            }

            string result =
                sb.ToString()
                    .Trim(
                        '_');

            return
                string.IsNullOrWhiteSpace(
                    result)
                    ? "UNKNOWN"
                    : result;
        }

        private static string BuildPackReadme(
            int sampleCount,
            Dictionary<string, int> classCounts)
        {
            StringBuilder sb =
                new StringBuilder();

            sb.AppendLine(
                "TDL MEP - ONNX TRAINING PACK");

            sb.AppendLine(
                "============================");

            sb.AppendLine();

            sb.AppendLine(
                "Samples: " +
                sampleCount);

            sb.AppendLine(
                "Classes: " +
                (classCounts?.Count ?? 0));

            sb.AppendLine();

            sb.AppendLine(
                "Cấu trúc:");

            sb.AppendLine(
                "  images/<CLASS_CODE>/<SHA256>.png");

            sb.AppendLine(
                "  manifest.json");

            sb.AppendLine(
                "  training_config.json");

            sb.AppendLine(
                "  mep_symbol_labels.txt");

            sb.AppendLine();

            sb.AppendLine(
                "Dùng train_mep_symbol_classifier.py của STEP21F để train.");

            sb.AppendLine();

            sb.AppendLine(
                "Khuyến nghị chưa train model chính thức nếu một class có dưới 20 mẫu.");

            return
                sb.ToString();
        }
    }
}
