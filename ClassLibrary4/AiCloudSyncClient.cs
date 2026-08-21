#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    public sealed class AiCloudConfig
    {
        public string ProjectUrl { get; set; } = "";
        public string PublishableKey { get; set; } = "";
        public string CompanyCode { get; set; } = "";
        public string CompanySyncKey { get; set; } = "";
        public string VoterId { get; set; } = "";

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(ProjectUrl) &&
            !string.IsNullOrWhiteSpace(PublishableKey) &&
            !string.IsNullOrWhiteSpace(CompanyCode) &&
            !string.IsNullOrWhiteSpace(CompanySyncKey) &&
            !string.IsNullOrWhiteSpace(VoterId);

        public void Normalize()
        {
            ProjectUrl = (ProjectUrl ?? "").Trim().TrimEnd('/');
            PublishableKey = (PublishableKey ?? "").Trim();
            CompanyCode = (CompanyCode ?? "").Trim().ToUpperInvariant();
            CompanySyncKey = (CompanySyncKey ?? "").Trim();
            VoterId = (VoterId ?? "").Trim();

            if (string.IsNullOrWhiteSpace(VoterId))
            {
                VoterId = Guid.NewGuid().ToString("N");
            }
        }
    }

    public sealed class AiCloudVote
    {
        [JsonPropertyName("signature")]
        public string Signature { get; set; } = "";

        [JsonPropertyName("match_mode")]
        public string MatchMode { get; set; } = "BLOCK";

        [JsonPropertyName("block_key")]
        public string BlockKey { get; set; } = "";

        [JsonPropertyName("geometry_fingerprint")]
        public string GeometryFingerprint { get; set; } = "";

        [JsonPropertyName("label")]
        public string Label { get; set; } = "";

        [JsonPropertyName("decision")]
        public string Decision { get; set; } = "POSITIVE";

        [JsonPropertyName("follow_dn")]
        public bool FollowDn { get; set; }

        [JsonPropertyName("scope")]
        public string Scope { get; set; } = "GLOBAL";

        [JsonPropertyName("source")]
        public string Source { get; set; } = "";

        [JsonPropertyName("client_updated_utc")]
        public string ClientUpdatedUtc { get; set; } = "";
    }

    public sealed class AiCloudConsensusRow
    {
        [JsonPropertyName("company_code")]
        public string CompanyCode { get; set; } = "";

        [JsonPropertyName("scope")]
        public string Scope { get; set; } = "GLOBAL";

        [JsonPropertyName("signature")]
        public string Signature { get; set; } = "";

        [JsonPropertyName("match_mode")]
        public string MatchMode { get; set; } = "BLOCK";

        [JsonPropertyName("block_key")]
        public string BlockKey { get; set; } = "";

        [JsonPropertyName("geometry_fingerprint")]
        public string GeometryFingerprint { get; set; } = "";

        [JsonPropertyName("label")]
        public string Label { get; set; } = "";

        [JsonPropertyName("follow_dn")]
        public bool FollowDn { get; set; }

        [JsonPropertyName("positive_votes")]
        public int PositiveVotes { get; set; }

        [JsonPropertyName("negative_votes")]
        public int NegativeVotes { get; set; }

        [JsonPropertyName("voter_count")]
        public int VoterCount { get; set; }

        [JsonPropertyName("last_event_at")]
        public string LastEventAt { get; set; } = "";
    }

    public sealed class AiCloudLocalState
    {
        public string LastSyncUtc { get; set; } = "";
        public int LastUploaded { get; set; }
        public int LastCloudGroups { get; set; }
        public int LastApproved { get; set; }
        public int LastPending { get; set; }
        public int LastConflict { get; set; }
        public string LastMessage { get; set; } = "";
    }

    public sealed class AiCloudSyncResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public int Uploaded { get; set; }
        public int PendingAfterSync { get; set; }
        public List<AiCloudConsensusRow> ConsensusRows { get; set; } =
            new List<AiCloudConsensusRow>();
    }

    public sealed class AiCloudSyncClient
    {
        private static readonly HttpClient Http =
            new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

        private static readonly JsonSerializerOptions JsonOptions =
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };

        public string BaseFolder { get; }
        public string ConfigPath { get; }
        public string PendingQueuePath { get; }
        public string StatePath { get; }

        public AiCloudSyncClient()
        {
            string appData =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData);

            if (string.IsNullOrWhiteSpace(appData))
            {
                appData = Path.GetTempPath();
            }

            BaseFolder = Path.Combine(
                appData,
                "TDL_MEP",
                "AI_Cloud");

            Directory.CreateDirectory(BaseFolder);

            ConfigPath = Path.Combine(
                BaseFolder,
                "ai_cloud_config_v1.json");

            PendingQueuePath = Path.Combine(
                BaseFolder,
                "ai_cloud_pending_votes_v1.json");

            StatePath = Path.Combine(
                BaseFolder,
                "ai_cloud_state_v1.json");
        }

        public AiCloudConfig LoadConfig()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    AiCloudConfig fresh = new AiCloudConfig();
                    fresh.Normalize();
                    return fresh;
                }

                AiCloudConfig config =
                    JsonSerializer.Deserialize<AiCloudConfig>(
                        File.ReadAllText(
                            ConfigPath,
                            Encoding.UTF8),
                        JsonOptions) ??
                    new AiCloudConfig();

                config.Normalize();
                return config;
            }
            catch
            {
                AiCloudConfig fallback = new AiCloudConfig();
                fallback.Normalize();
                return fallback;
            }
        }

        public void SaveConfig(AiCloudConfig config)
        {
            if (config == null)
                return;

            config.Normalize();
            Directory.CreateDirectory(BaseFolder);

            File.WriteAllText(
                ConfigPath,
                JsonSerializer.Serialize(
                    config,
                    JsonOptions),
                Encoding.UTF8);
        }

        public AiCloudLocalState LoadState()
        {
            try
            {
                if (!File.Exists(StatePath))
                    return new AiCloudLocalState();

                return
                    JsonSerializer.Deserialize<AiCloudLocalState>(
                        File.ReadAllText(
                            StatePath,
                            Encoding.UTF8),
                        JsonOptions) ??
                    new AiCloudLocalState();
            }
            catch
            {
                return new AiCloudLocalState();
            }
        }

        public void SaveState(AiCloudLocalState state)
        {
            try
            {
                Directory.CreateDirectory(BaseFolder);

                File.WriteAllText(
                    StatePath,
                    JsonSerializer.Serialize(
                        state ?? new AiCloudLocalState(),
                        JsonOptions),
                    Encoding.UTF8);
            }
            catch
            {
            }
        }

        public int GetPendingCount()
        {
            return LoadPendingVotes().Count;
        }

        public void EnqueueVote(AiCloudVote vote)
        {
            if (vote == null ||
                string.IsNullOrWhiteSpace(vote.Signature))
            {
                return;
            }

            vote.Signature = vote.Signature.Trim();
            vote.Decision =
                string.Equals(
                    vote.Decision,
                    "NEGATIVE",
                    StringComparison.OrdinalIgnoreCase)
                    ? "NEGATIVE"
                    : "POSITIVE";

            vote.Scope =
                string.IsNullOrWhiteSpace(vote.Scope)
                    ? "GLOBAL"
                    : vote.Scope.Trim();

            vote.ClientUpdatedUtc =
                string.IsNullOrWhiteSpace(vote.ClientUpdatedUtc)
                    ? DateTime.UtcNow.ToString(
                        "O",
                        CultureInfo.InvariantCulture)
                    : vote.ClientUpdatedUtc;

            List<AiCloudVote> votes = LoadPendingVotes();

            // Mỗi voter chỉ có 1 phiếu hiện tại/signature.
            // User sửa lại thì pending cũ bị thay thế, không spam confirmations.
            votes.RemoveAll(
                x =>
                    x != null &&
                    string.Equals(
                        x.Signature,
                        vote.Signature,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        x.Scope,
                        vote.Scope,
                        StringComparison.OrdinalIgnoreCase));

            votes.Add(vote);
            SavePendingVotes(votes);
        }

        public async Task<Tuple<bool, string>> TestConnectionAsync(
            AiCloudConfig config,
            CancellationToken cancellationToken = default)
        {
            if (config == null)
            {
                return Tuple.Create(
                    false,
                    "Chưa có cấu hình AI Cloud.");
            }

            config.Normalize();

            if (!config.IsConfigured)
            {
                return Tuple.Create(
                    false,
                    "Thiếu Project URL / Publishable Key / Company Code / Company Sync Key.");
            }

            try
            {
                string response =
                    await PostRpcAsync(
                        config,
                        "ai_ping",
                        new
                        {
                            p_company_code = config.CompanyCode,
                            p_sync_key = config.CompanySyncKey
                        },
                        cancellationToken);

                bool ok =
                    response
                        .Trim()
                        .Equals(
                            "true",
                            StringComparison.OrdinalIgnoreCase);

                return Tuple.Create(
                    ok,
                    ok
                        ? "Kết nối AI Cloud thành công."
                        : "Cloud trả về FALSE. Kiểm tra Company Code / Sync Key.");
            }
            catch (Exception ex)
            {
                return Tuple.Create(
                    false,
                    ex.Message);
            }
        }

        public async Task<AiCloudSyncResult> SyncAsync(
            CancellationToken cancellationToken = default)
        {
            AiCloudConfig config = LoadConfig();

            if (!config.IsConfigured)
            {
                return new AiCloudSyncResult
                {
                    Success = false,
                    Message = "AI Cloud chưa được cấu hình.",
                    PendingAfterSync = GetPendingCount()
                };
            }

            List<AiCloudVote> pending = LoadPendingVotes();
            int uploaded = 0;

            try
            {
                if (pending.Count > 0)
                {
                    await PostRpcAsync(
                        config,
                        "ai_upsert_votes_batch",
                        new
                        {
                            p_company_code = config.CompanyCode,
                            p_sync_key = config.CompanySyncKey,
                            p_voter_id = config.VoterId,
                            p_votes = pending
                        },
                        cancellationToken);

                    uploaded = pending.Count;
                    SavePendingVotes(new List<AiCloudVote>());
                }

                string consensusJson =
                    await PostRpcAsync(
                        config,
                        "ai_get_consensus",
                        new
                        {
                            p_company_code = config.CompanyCode,
                            p_sync_key = config.CompanySyncKey
                        },
                        cancellationToken);

                List<AiCloudConsensusRow> rows =
                    JsonSerializer.Deserialize<List<AiCloudConsensusRow>>(
                        consensusJson,
                        JsonOptions) ??
                    new List<AiCloudConsensusRow>();

                return new AiCloudSyncResult
                {
                    Success = true,
                    Message = "Đồng bộ AI Cloud thành công.",
                    Uploaded = uploaded,
                    PendingAfterSync = GetPendingCount(),
                    ConsensusRows = rows
                };
            }
            catch (Exception ex)
            {
                return new AiCloudSyncResult
                {
                    Success = false,
                    Message = ex.Message,
                    Uploaded = uploaded,
                    PendingAfterSync = GetPendingCount()
                };
            }
        }

        private List<AiCloudVote> LoadPendingVotes()
        {
            try
            {
                if (!File.Exists(PendingQueuePath))
                    return new List<AiCloudVote>();

                return
                    JsonSerializer.Deserialize<List<AiCloudVote>>(
                        File.ReadAllText(
                            PendingQueuePath,
                            Encoding.UTF8),
                        JsonOptions) ??
                    new List<AiCloudVote>();
            }
            catch
            {
                return new List<AiCloudVote>();
            }
        }

        private void SavePendingVotes(List<AiCloudVote> votes)
        {
            try
            {
                Directory.CreateDirectory(BaseFolder);

                File.WriteAllText(
                    PendingQueuePath,
                    JsonSerializer.Serialize(
                        votes ?? new List<AiCloudVote>(),
                        JsonOptions),
                    Encoding.UTF8);
            }
            catch
            {
            }
        }

        private async Task<string> PostRpcAsync(
            AiCloudConfig config,
            string rpcName,
            object body,
            CancellationToken cancellationToken)
        {
            string url =
                config.ProjectUrl.TrimEnd('/') +
                "/rest/v1/rpc/" +
                rpcName;

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

                // Legacy anon key là JWT nên có thể dùng Bearer.
                // Publishable key mới sb_publishable_* KHÔNG gửi làm Bearer.
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
                            "Supabase RPC " +
                            rpcName +
                            " lỗi " +
                            ((int)response.StatusCode)
                                .ToString(
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
    }
}
