#nullable enable
using System;
using System.Collections.Generic;
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
                VoterId = Guid.NewGuid().ToString("N");
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

        // STEP27A:
        // 1) FileGate bảo vệ config/state/pending queue khi nhiều luồng cùng đọc/ghi.
        // 2) SyncGate ngăn hai lượt SyncAsync chạy chồng nhau.
        private static readonly object FileGate = new object();
        private static readonly SemaphoreSlim SyncGate = new SemaphoreSlim(1, 1);

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
                appData = Path.GetTempPath();

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
            lock (FileGate)
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
        }

        public void SaveConfig(AiCloudConfig? config)
        {
            if (config == null)
                return;

            config.Normalize();

            lock (FileGate)
            {
                try
                {
                    Directory.CreateDirectory(BaseFolder);

                    WriteAllTextAtomic(
                        ConfigPath,
                        JsonSerializer.Serialize(
                            config,
                            JsonOptions));
                }
                catch
                {
                }
            }
        }

        public AiCloudLocalState LoadState()
        {
            lock (FileGate)
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
        }

        public void SaveState(AiCloudLocalState? state)
        {
            lock (FileGate)
            {
                try
                {
                    Directory.CreateDirectory(BaseFolder);

                    WriteAllTextAtomic(
                        StatePath,
                        JsonSerializer.Serialize(
                            state ?? new AiCloudLocalState(),
                            JsonOptions));
                }
                catch
                {
                }
            }
        }

        public int GetPendingCount()
        {
            lock (FileGate)
            {
                return LoadPendingVotesNoLock().Count;
            }
        }

        public void EnqueueVote(AiCloudVote? vote)
        {
            if (vote == null ||
                string.IsNullOrWhiteSpace(vote.Signature))
            {
                return;
            }

            vote.Signature = vote.Signature.Trim();
            vote.MatchMode =
                string.IsNullOrWhiteSpace(vote.MatchMode)
                    ? "BLOCK"
                    : vote.MatchMode.Trim();

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
                    : vote.ClientUpdatedUtc.Trim();

            lock (FileGate)
            {
                List<AiCloudVote> votes =
                    LoadPendingVotesNoLock();

                // Mỗi voter chỉ giữ bản pending mới nhất của 1 signature/scope.
                votes.RemoveAll(
                    x =>
                        string.Equals(
                            x.Signature,
                            vote.Signature,
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(
                            x.Scope,
                            vote.Scope,
                            StringComparison.OrdinalIgnoreCase));

                votes.Add(vote);
                SavePendingVotesNoLock(votes);
            }
        }

        public async Task<Tuple<bool, string>> TestConnectionAsync(
            AiCloudConfig? config,
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
                        cancellationToken)
                    .ConfigureAwait(false);

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
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                return Tuple.Create(
                    false,
                    "Đã hủy kiểm tra kết nối AI Cloud.");
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
            await SyncGate
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            try
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

                List<AiCloudVote> pendingSnapshot;

                lock (FileGate)
                {
                    pendingSnapshot =
                        LoadPendingVotesNoLock();
                }

                int uploaded = 0;

                try
                {
                    if (pendingSnapshot.Count > 0)
                    {
                        await PostRpcAsync(
                                config,
                                "ai_upsert_votes_batch",
                                new
                                {
                                    p_company_code = config.CompanyCode,
                                    p_sync_key = config.CompanySyncKey,
                                    p_voter_id = config.VoterId,
                                    p_votes = pendingSnapshot
                                },
                                cancellationToken)
                            .ConfigureAwait(false);

                        uploaded = pendingSnapshot.Count;

                        // QUAN TRỌNG:
                        // Không được SavePendingVotes(empty) như bản cũ.
                        // Trong lúc request đang chạy, user có thể vừa sửa/học thêm ký hiệu.
                        // Chỉ xóa đúng những vote đã upload; vote mới phát sinh phải được giữ lại.
                        RemoveUploadedSnapshot(
                            pendingSnapshot);
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
                                cancellationToken)
                            .ConfigureAwait(false);

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
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    return new AiCloudSyncResult
                    {
                        Success = false,
                        Message = "Đã hủy đồng bộ AI Cloud.",
                        Uploaded = uploaded,
                        PendingAfterSync = GetPendingCount()
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
            finally
            {
                SyncGate.Release();
            }
        }

        private List<AiCloudVote> LoadPendingVotesNoLock()
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

        private void SavePendingVotesNoLock(
            List<AiCloudVote>? votes)
        {
            try
            {
                Directory.CreateDirectory(BaseFolder);

                WriteAllTextAtomic(
                    PendingQueuePath,
                    JsonSerializer.Serialize(
                        votes ?? new List<AiCloudVote>(),
                        JsonOptions));
            }
            catch
            {
            }
        }

        private void RemoveUploadedSnapshot(
            IReadOnlyCollection<AiCloudVote> uploadedSnapshot)
        {
            if (uploadedSnapshot.Count == 0)
                return;

            HashSet<string> uploadedKeys =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (AiCloudVote vote
                in uploadedSnapshot)
            {
                uploadedKeys.Add(
                    BuildVoteVersionKey(
                        vote));
            }

            lock (FileGate)
            {
                List<AiCloudVote> current =
                    LoadPendingVotesNoLock();

                current.RemoveAll(
                    vote =>
                        uploadedKeys.Contains(
                            BuildVoteVersionKey(
                                vote)));

                SavePendingVotesNoLock(
                    current);
            }
        }

        private static string BuildVoteVersionKey(
            AiCloudVote vote)
        {
            return
                (vote.Scope ?? "").Trim() +
                "\u001F" +
                (vote.Signature ?? "").Trim() +
                "\u001F" +
                (vote.ClientUpdatedUtc ?? "").Trim();
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

            using HttpRequestMessage request =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    url);

            request.Headers.TryAddWithoutValidation(
                "apikey",
                config.PublishableKey);

            // Legacy anon key là JWT nên có thể dùng Bearer.
            // Publishable key sb_publishable_* chỉ gửi ở header apikey.
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

            using HttpResponseMessage response =
                await Http
                    .SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken)
                    .ConfigureAwait(false);

            string responseText =
                await response.Content
                    .ReadAsStringAsync(
                        cancellationToken)
                    .ConfigureAwait(false);

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
                    (response.ReasonPhrase ?? "") +
                    "\n" +
                    responseText);
            }

            return responseText;
        }

        private static void WriteAllTextAtomic(
            string path,
            string content)
        {
            string? folder =
                Path.GetDirectoryName(path);

            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

            string tempPath =
                path +
                ".tmp." +
                Guid.NewGuid().ToString("N");

            try
            {
                File.WriteAllText(
                    tempPath,
                    content ?? "",
                    new UTF8Encoding(false));

                File.Move(
                    tempPath,
                    path,
                    true);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }
    }
}