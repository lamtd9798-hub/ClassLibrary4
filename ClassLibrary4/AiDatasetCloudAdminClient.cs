#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ClassLibrary4
{
    public sealed class AiDatasetCloudReviewRow
    {
        [JsonPropertyName("sample_hash")]
        public string SampleHash { get; set; } = "";

        [JsonPropertyName("signature")]
        public string Signature { get; set; } = "";

        [JsonPropertyName("match_mode")]
        public string MatchMode { get; set; } = "BLOCK";

        [JsonPropertyName("winner_label")]
        public string WinnerLabel { get; set; } = "";

        [JsonPropertyName("winner_votes")]
        public int WinnerVotes { get; set; }

        [JsonPropertyName("second_label")]
        public string SecondLabel { get; set; } = "";

        [JsonPropertyName("second_votes")]
        public int SecondVotes { get; set; }

        [JsonPropertyName("negative_votes")]
        public int NegativeVotes { get; set; }

        [JsonPropertyName("voter_count")]
        public int VoterCount { get; set; }

        [JsonPropertyName("hard_negative_label")]
        public string HardNegativeLabel { get; set; } = "";

        [JsonPropertyName("follow_dn")]
        public bool FollowDn { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = "PENDING";

        [JsonPropertyName("final_label")]
        public string FinalLabel { get; set; } = "";

        [JsonPropertyName("class_code")]
        public string ClassCode { get; set; } = "";

        [JsonPropertyName("review_note")]
        public string ReviewNote { get; set; } = "";

        [JsonPropertyName("storage_path")]
        public string StoragePath { get; set; } = "";

        [JsonPropertyName("signed_url")]
        public string SignedUrl { get; set; } = "";

        [JsonPropertyName("updated_at")]
        public string UpdatedAt { get; set; } = "";
    }

    public sealed class AiClassDictionaryItem
    {
        [JsonPropertyName("class_code")]
        public string ClassCode { get; set; } = "";

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = "";

        [JsonPropertyName("aliases")]
        public List<string> Aliases { get; set; } =
            new List<string>();

        [JsonPropertyName("active")]
        public bool Active { get; set; } = true;

        [JsonPropertyName("sample_count")]
        public int SampleCount { get; set; }
    }

    public sealed class AiDatasetAdminActionResult
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = "";

        [JsonPropertyName("error")]
        public string Error { get; set; } = "";
    }

    public sealed class AiDatasetCloudAdminClient
    {
        private static readonly HttpClient Http =
            new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(45)
            };

        private static readonly JsonSerializerOptions JsonOptions =
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = false
            };

        public async Task<List<AiDatasetCloudReviewRow>> GetReviewRowsAsync(
            AiCloudConfig config,
            string filter,
            int limit = 250,
            CancellationToken cancellationToken = default)
        {
            string response =
                await PostAsync(
                    config,
                    new
                    {
                        action = "review_list",
                        company_code = config.CompanyCode,
                        sync_key = config.CompanySyncKey,
                        filter = string.IsNullOrWhiteSpace(filter)
                            ? "ALL"
                            : filter,
                        limit = Math.Max(20, Math.Min(1000, limit))
                    },
                    cancellationToken);

            using (JsonDocument doc = JsonDocument.Parse(response))
            {
                JsonElement root = doc.RootElement;

                if (!GetOk(root))
                {
                    throw new InvalidOperationException(
                        GetError(root));
                }

                if (!root.TryGetProperty(
                        "rows",
                        out JsonElement rowsElement))
                {
                    return new List<AiDatasetCloudReviewRow>();
                }

                return
                    JsonSerializer.Deserialize<List<AiDatasetCloudReviewRow>>(
                        rowsElement.GetRawText(),
                        JsonOptions) ??
                    new List<AiDatasetCloudReviewRow>();
            }
        }

        public async Task<List<AiClassDictionaryItem>> GetClassesAsync(
            AiCloudConfig config,
            CancellationToken cancellationToken = default)
        {
            string response =
                await PostAsync(
                    config,
                    new
                    {
                        action = "class_list",
                        company_code = config.CompanyCode,
                        sync_key = config.CompanySyncKey
                    },
                    cancellationToken);

            using (JsonDocument doc = JsonDocument.Parse(response))
            {
                JsonElement root = doc.RootElement;

                if (!GetOk(root))
                    throw new InvalidOperationException(GetError(root));

                if (!root.TryGetProperty(
                        "classes",
                        out JsonElement classesElement))
                {
                    return new List<AiClassDictionaryItem>();
                }

                return
                    JsonSerializer.Deserialize<List<AiClassDictionaryItem>>(
                        classesElement.GetRawText(),
                        JsonOptions) ??
                    new List<AiClassDictionaryItem>();
            }
        }

        public async Task<AiDatasetAdminActionResult> UpsertClassAsync(
            AiCloudConfig config,
            string adminKey,
            string classCode,
            string displayName,
            IEnumerable<string> aliases,
            bool active,
            CancellationToken cancellationToken = default)
        {
            return await ExecuteActionAsync(
                config,
                new
                {
                    action = "class_upsert",
                    company_code = config.CompanyCode,
                    sync_key = config.CompanySyncKey,
                    admin_key = adminKey,
                    class_code = classCode,
                    display_name = displayName,
                    aliases = aliases ?? Array.Empty<string>(),
                    active = active
                },
                cancellationToken);
        }

        public async Task<AiDatasetAdminActionResult> ApproveAsync(
            AiCloudConfig config,
            string adminKey,
            string sampleHash,
            string finalLabel,
            string classCode,
            bool followDn,
            string note,
            string reviewerId,
            CancellationToken cancellationToken = default)
        {
            return await ExecuteActionAsync(
                config,
                new
                {
                    action = "approve",
                    company_code = config.CompanyCode,
                    sync_key = config.CompanySyncKey,
                    admin_key = adminKey,
                    sample_hash = sampleHash,
                    final_label = finalLabel,
                    class_code = classCode,
                    follow_dn = followDn,
                    note = note ?? "",
                    reviewer_id = reviewerId ?? ""
                },
                cancellationToken);
        }

        public async Task<AiDatasetAdminActionResult> RejectAsync(
            AiCloudConfig config,
            string adminKey,
            string sampleHash,
            string note,
            string reviewerId,
            CancellationToken cancellationToken = default)
        {
            return await ExecuteActionAsync(
                config,
                new
                {
                    action = "reject",
                    company_code = config.CompanyCode,
                    sync_key = config.CompanySyncKey,
                    admin_key = adminKey,
                    sample_hash = sampleHash,
                    note = note ?? "",
                    reviewer_id = reviewerId ?? ""
                },
                cancellationToken);
        }

        public async Task<Bitmap> DownloadPreviewAsync(
            string signedUrl,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(signedUrl))
                return null;

            byte[] bytes =
                await Http.GetByteArrayAsync(
                    signedUrl,
                    cancellationToken);

            using (MemoryStream ms = new MemoryStream(bytes))
            using (Image image = Image.FromStream(ms))
            {
                return new Bitmap(image);
            }
        }

        private async Task<AiDatasetAdminActionResult> ExecuteActionAsync(
            AiCloudConfig config,
            object body,
            CancellationToken cancellationToken)
        {
            string response =
                await PostAsync(
                    config,
                    body,
                    cancellationToken);

            AiDatasetAdminActionResult result =
                JsonSerializer.Deserialize<AiDatasetAdminActionResult>(
                    response,
                    JsonOptions) ??
                new AiDatasetAdminActionResult();

            if (!result.Ok && string.IsNullOrWhiteSpace(result.Error))
            {
                result.Error = "Cloud không xác nhận thao tác.";
            }

            return result;
        }

        private async Task<string> PostAsync(
            AiCloudConfig config,
            object body,
            CancellationToken cancellationToken)
        {
            if (config == null)
                throw new InvalidOperationException("Chưa có cấu hình AI Cloud.");

            config.Normalize();

            if (!config.IsConfigured)
                throw new InvalidOperationException("AI Cloud chưa cấu hình đầy đủ.");

            string url =
                config.ProjectUrl.TrimEnd('/') +
                "/functions/v1/ai-dataset-admin";

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
                        throw new InvalidOperationException(
                            "Edge Function ai-dataset-admin lỗi " +
                            ((int)response.StatusCode).ToString(
                                CultureInfo.InvariantCulture) +
                            " " +
                            response.ReasonPhrase +
                            "\n" +
                            responseText);
                    }

                    return responseText;
                }
            }
        }

        private static bool GetOk(JsonElement root)
        {
            return
                root.TryGetProperty(
                    "ok",
                    out JsonElement okElement) &&
                okElement.ValueKind == JsonValueKind.True;
        }

        private static string GetError(JsonElement root)
        {
            if (root.TryGetProperty(
                    "error",
                    out JsonElement errorElement))
            {
                return errorElement.GetString() ?? "Unknown cloud error.";
            }

            return "Unknown cloud error.";
        }
    }
}
