#nullable disable
using System;
using System.Collections.Generic;
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
    public sealed class AiGraphCloudSummary
    {
        [JsonPropertyName("total_graphs")]
        public int TotalGraphs { get; set; }

        [JsonPropertyName("total_submissions")]
        public int TotalSubmissions { get; set; }

        [JsonPropertyName("approved")]
        public int Approved { get; set; }

        [JsonPropertyName("approved_admin")]
        public int ApprovedAdmin { get; set; }

        [JsonPropertyName("approved_consensus")]
        public int ApprovedConsensus { get; set; }

        [JsonPropertyName("pending")]
        public int Pending { get; set; }

        [JsonPropertyName("rejected")]
        public int Rejected { get; set; }

        [JsonPropertyName("total_reliable_targets")]
        public int TotalReliableTargets { get; set; }
    }

    public sealed class AiGraphCloudLocalSummary
    {
        public int LocalUniqueGraphs { get; set; }
        public int PendingUpload { get; set; }
        public int CloudApprovedLocal { get; set; }
        public string LastSyncUtc { get; set; } = "";
        public string LastMessage { get; set; } = "";
        public int LastCloudTotal { get; set; }
        public int LastCloudApproved { get; set; }
        public int LastCloudPending { get; set; }
        public int LastCloudRejected { get; set; }
    }

    public sealed class AiGraphCloudSyncResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public int Uploaded { get; set; }
        public int PulledApproved { get; set; }
        public int PendingAfterSync { get; set; }
        public AiGraphCloudSummary CloudSummary { get; set; } =
            new AiGraphCloudSummary();
    }

    public sealed class AiGraphCloudReviewRow
    {
        [JsonPropertyName("graph_hash")]
        public string GraphHash { get; set; } = "";

        [JsonPropertyName("status")]
        public string Status { get; set; } = "PENDING";

        [JsonPropertyName("pipe_count")]
        public int PipeCount { get; set; }

        [JsonPropertyName("explicit_label_count")]
        public int ExplicitLabelCount { get; set; }

        [JsonPropertyName("dn_class_count")]
        public int DnClassCount { get; set; }

        [JsonPropertyName("dn_counts")]
        public Dictionary<string, int> DnCounts { get; set; } =
            new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);

        [JsonPropertyName("voter_count")]
        public int VoterCount { get; set; }

        [JsonPropertyName("review_note")]
        public string ReviewNote { get; set; } = "";

        [JsonPropertyName("updated_at")]
        public string UpdatedAt { get; set; } = "";
    }

    public sealed class AiGraphCloudAdminResult
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = "";

        [JsonPropertyName("error")]
        public string Error { get; set; } = "";
    }

    internal sealed class AiGraphCloudState
    {
        public string CompanyCode { get; set; } = "";
        public List<string> UploadedHashes { get; set; } =
            new List<string>();
        public string LastSyncUtc { get; set; } = "";
        public string LastMessage { get; set; } = "";
        public int LastCloudTotal { get; set; }
        public int LastCloudApproved { get; set; }
        public int LastCloudPending { get; set; }
        public int LastCloudRejected { get; set; }
    }

    internal sealed class AiGraphCloudLocalItem
    {
        public string Hash { get; set; } = "";
        public string SourcePath { get; set; } = "";
        public JsonElement Graph { get; set; }
    }

    /// <summary>
    /// STEP22B2 - Graph Dataset Cloud.
    ///
    /// Cloud payload được privacy-sanitize:
    /// - bỏ tên DWG
    /// - bỏ Handle/ObjectId
    /// - bỏ tên layer thô
    /// - tọa độ chuyển về local origin
    /// - chuẩn hóa hướng endpoint
    /// - sort node + remap neighbor
    ///
    /// SHA256 tính trên canonical graph => chống trùng tốt hơn giữa nhiều máy.
    /// </summary>
    public sealed class AiGraphCloudClient
    {
        private static readonly HttpClient Http =
            new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60)
            };

        private static readonly JsonSerializerOptions JsonOptions =
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = false
            };

        private const int UploadBatchSize = 5;

        public string GraphRoot { get; }
        public string HistoryFolder { get; }
        public string CloudApprovedFolder { get; }
        public string StatePath { get; }

        public AiGraphCloudClient()
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

            GraphRoot =
                Path.Combine(
                    appData,
                    "TDL_MEP",
                    "Graph");

            HistoryFolder =
                Path.Combine(
                    GraphRoot,
                    "History");

            CloudApprovedFolder =
                Path.Combine(
                    GraphRoot,
                    "CloudApproved");

            StatePath =
                Path.Combine(
                    GraphRoot,
                    "graph_cloud_state_v1.json");

            Directory.CreateDirectory(
                HistoryFolder);

            Directory.CreateDirectory(
                CloudApprovedFolder);
        }

        public string GetCanonicalHashForGraphFile(
            string path)
        {
            try
            {
                AiGraphCloudLocalItem item =
                    BuildCanonicalCloudGraph(
                        path);

                return
                    item?.Hash ?? "";
            }
            catch
            {
                return "";
            }
        }

        public AiGraphCloudLocalSummary GetLocalSummary(
            AiCloudConfig config = null)
        {
            AiGraphCloudState state =
                LoadState();

            if (config != null)
            {
                config.Normalize();

                if (!string.Equals(
                        state.CompanyCode,
                        config.CompanyCode,
                        StringComparison.OrdinalIgnoreCase))
                {
                    state =
                        new AiGraphCloudState
                        {
                            CompanyCode =
                                config.CompanyCode
                        };
                }
            }

            List<AiGraphCloudLocalItem> local =
                ScanCanonicalLocalGraphs();

            HashSet<string> uploaded =
                new HashSet<string>(
                    state.UploadedHashes ??
                    new List<string>(),
                    StringComparer.OrdinalIgnoreCase);

            return
                new AiGraphCloudLocalSummary
                {
                    LocalUniqueGraphs =
                        local.Count,

                    PendingUpload =
                        local.Count(
                            x =>
                                !uploaded.Contains(
                                    x.Hash)),

                    CloudApprovedLocal =
                        Directory
                            .GetFiles(
                                CloudApprovedFolder,
                                "*.json",
                                SearchOption.TopDirectoryOnly)
                            .Length,

                    LastSyncUtc =
                        state.LastSyncUtc ?? "",

                    LastMessage =
                        state.LastMessage ?? "",

                    LastCloudTotal =
                        state.LastCloudTotal,

                    LastCloudApproved =
                        state.LastCloudApproved,

                    LastCloudPending =
                        state.LastCloudPending,

                    LastCloudRejected =
                        state.LastCloudRejected
                };
        }

        public async Task<AiGraphCloudSyncResult> SyncAsync(
            AiCloudConfig config,
            CancellationToken cancellationToken = default)
        {
            if (config == null)
            {
                return
                    new AiGraphCloudSyncResult
                    {
                        Success = false,
                        Message =
                            "Chưa có cấu hình AI Cloud."
                    };
            }

            config.Normalize();

            if (!config.IsConfigured)
            {
                return
                    new AiGraphCloudSyncResult
                    {
                        Success = false,
                        Message =
                            "AI Cloud chưa cấu hình đầy đủ."
                    };
            }

            AiGraphCloudState state =
                LoadState();

            if (!string.Equals(
                    state.CompanyCode,
                    config.CompanyCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                state =
                    new AiGraphCloudState
                    {
                        CompanyCode =
                            config.CompanyCode
                    };
            }

            HashSet<string> uploaded =
                new HashSet<string>(
                    state.UploadedHashes ??
                    new List<string>(),
                    StringComparer.OrdinalIgnoreCase);

            List<AiGraphCloudLocalItem> local =
                ScanCanonicalLocalGraphs();

            List<AiGraphCloudLocalItem> pending =
                local
                    .Where(
                        x =>
                            !uploaded.Contains(
                                x.Hash))
                    .ToList();

            int uploadCount =
                0;

            try
            {
                for (int i = 0;
                    i < pending.Count;
                    i += UploadBatchSize)
                {
                    List<AiGraphCloudLocalItem> batch =
                        pending
                            .Skip(i)
                            .Take(UploadBatchSize)
                            .ToList();

                    object[] graphs =
                        batch
                            .Select(
                                x =>
                                    new
                                    {
                                        graph_hash =
                                            x.Hash,
                                        graph =
                                            x.Graph
                                    })
                            .Cast<object>()
                            .ToArray();

                    await PostRpcAsync(
                        config,
                        "ai_graph_upsert_batch",
                        new
                        {
                            p_company_code =
                                config.CompanyCode,
                            p_sync_key =
                                config.CompanySyncKey,
                            p_voter_id =
                                config.VoterId,
                            p_graphs =
                                graphs
                        },
                        cancellationToken);

                    foreach (AiGraphCloudLocalItem item
                        in batch)
                    {
                        uploaded.Add(
                            item.Hash);

                        uploadCount++;
                    }

                    state.UploadedHashes =
                        uploaded
                            .OrderBy(
                                x =>
                                    x,
                                StringComparer.OrdinalIgnoreCase)
                            .ToList();

                    SaveState(
                        state);
                }

                AiGraphCloudSummary cloudSummary =
                    await GetCloudSummaryAsync(
                        config,
                        cancellationToken);

                int pulled =
                    await PullApprovedAsync(
                        config,
                        cancellationToken);

                state.LastSyncUtc =
                    DateTime.UtcNow.ToString(
                        "O",
                        CultureInfo.InvariantCulture);

                state.LastMessage =
                    "SYNC_OK";

                state.LastCloudTotal =
                    cloudSummary.TotalGraphs;

                state.LastCloudApproved =
                    cloudSummary.Approved;

                state.LastCloudPending =
                    cloudSummary.Pending;

                state.LastCloudRejected =
                    cloudSummary.Rejected;

                state.UploadedHashes =
                    uploaded
                        .OrderBy(
                            x =>
                                x,
                            StringComparer.OrdinalIgnoreCase)
                        .ToList();

                SaveState(
                    state);

                AiGraphCloudLocalSummary localAfter =
                    GetLocalSummary(
                        config);

                return
                    new AiGraphCloudSyncResult
                    {
                        Success = true,
                        Message =
                            "Đồng bộ Graph Cloud thành công.",
                        Uploaded =
                            uploadCount,
                        PulledApproved =
                            pulled,
                        PendingAfterSync =
                            localAfter.PendingUpload,
                        CloudSummary =
                            cloudSummary
                    };
            }
            catch (Exception ex)
            {
                state.LastSyncUtc =
                    DateTime.UtcNow.ToString(
                        "O",
                        CultureInfo.InvariantCulture);

                state.LastMessage =
                    ex.GetType().Name +
                    ": " +
                    ex.Message;

                state.UploadedHashes =
                    uploaded
                        .OrderBy(
                            x =>
                                x,
                            StringComparer.OrdinalIgnoreCase)
                        .ToList();

                SaveState(
                    state);

                return
                    new AiGraphCloudSyncResult
                    {
                        Success = false,
                        Message =
                            ex.Message,
                        Uploaded =
                            uploadCount,
                        PendingAfterSync =
                            GetLocalSummary(
                                config)
                                .PendingUpload
                    };
            }
        }

        public async Task<AiGraphCloudSummary> GetCloudSummaryAsync(
            AiCloudConfig config,
            CancellationToken cancellationToken = default)
        {
            string response =
                await PostRpcAsync(
                    config,
                    "ai_graph_summary",
                    new
                    {
                        p_company_code =
                            config.CompanyCode,
                        p_sync_key =
                            config.CompanySyncKey
                    },
                    cancellationToken);

            return
                JsonSerializer.Deserialize<AiGraphCloudSummary>(
                    response,
                    JsonOptions) ??
                new AiGraphCloudSummary();
        }

        public async Task<List<AiGraphCloudReviewRow>> GetReviewRowsAsync(
            AiCloudConfig config,
            string filter,
            int limit = 500,
            CancellationToken cancellationToken = default)
        {
            string response =
                await PostRpcAsync(
                    config,
                    "ai_graph_review_list",
                    new
                    {
                        p_company_code =
                            config.CompanyCode,
                        p_sync_key =
                            config.CompanySyncKey,
                        p_filter =
                            string.IsNullOrWhiteSpace(
                                filter)
                                ? "ALL"
                                : filter,
                        p_limit =
                            Math.Max(
                                20,
                                Math.Min(
                                    1000,
                                    limit))
                    },
                    cancellationToken);

            return
                JsonSerializer.Deserialize<List<AiGraphCloudReviewRow>>(
                    response,
                    JsonOptions) ??
                new List<AiGraphCloudReviewRow>();
        }

        public async Task<AiGraphCloudAdminResult> AdminReviewAsync(
            AiCloudConfig config,
            string adminKey,
            string graphHash,
            string action,
            string note,
            string reviewerId,
            CancellationToken cancellationToken = default)
        {
            string response =
                await PostRpcAsync(
                    config,
                    "ai_graph_admin_review",
                    new
                    {
                        p_company_code =
                            config.CompanyCode,
                        p_sync_key =
                            config.CompanySyncKey,
                        p_admin_key =
                            adminKey ?? "",
                        p_graph_hash =
                            graphHash ?? "",
                        p_action =
                            action ?? "",
                        p_note =
                            note ?? "",
                        p_reviewer_id =
                            reviewerId ?? ""
                    },
                    cancellationToken);

            return
                JsonSerializer.Deserialize<AiGraphCloudAdminResult>(
                    response,
                    JsonOptions) ??
                new AiGraphCloudAdminResult
                {
                    Ok = false,
                    Error =
                        "Cloud không trả kết quả."
                };
        }

        public async Task<int> PullApprovedAsync(
            AiCloudConfig config,
            CancellationToken cancellationToken = default)
        {
            string response =
                await PostRpcAsync(
                    config,
                    "ai_graph_get_approved",
                    new
                    {
                        p_company_code =
                            config.CompanyCode,
                        p_sync_key =
                            config.CompanySyncKey
                    },
                    cancellationToken);

            using (JsonDocument doc =
                JsonDocument.Parse(
                    response))
            {
                if (doc.RootElement.ValueKind !=
                    JsonValueKind.Array)
                {
                    return 0;
                }

                int saved =
                    0;

                foreach (JsonElement row
                    in doc.RootElement.EnumerateArray())
                {
                    if (!row.TryGetProperty(
                            "graph_hash",
                            out JsonElement hashElement))
                    {
                        continue;
                    }

                    if (!row.TryGetProperty(
                            "graph_json",
                            out JsonElement graphElement))
                    {
                        continue;
                    }

                    string hash =
                        (hashElement.GetString() ?? "")
                            .Trim()
                            .ToLowerInvariant();

                    if (!IsSha256(
                            hash))
                    {
                        continue;
                    }

                    string path =
                        Path.Combine(
                            CloudApprovedFolder,
                            hash +
                            ".json");

                    string json =
                        graphElement
                            .GetRawText();

                    bool write =
                        !File.Exists(
                            path);

                    if (!write)
                    {
                        try
                        {
                            write =
                                !string.Equals(
                                    File.ReadAllText(
                                        path,
                                        Encoding.UTF8),
                                    json,
                                    StringComparison.Ordinal);
                        }
                        catch
                        {
                            write =
                                true;
                        }
                    }

                    if (write)
                    {
                        File.WriteAllText(
                            path,
                            json,
                            Encoding.UTF8);

                        saved++;
                    }
                }

                return saved;
            }
        }

        private List<AiGraphCloudLocalItem> ScanCanonicalLocalGraphs()
        {
            Dictionary<string, AiGraphCloudLocalItem> unique =
                new Dictionary<string, AiGraphCloudLocalItem>(
                    StringComparer.OrdinalIgnoreCase);

            List<string> paths =
                Directory
                    .GetFiles(
                        HistoryFolder,
                        "*.json",
                        SearchOption.TopDirectoryOnly)
                    .ToList();

            string lastGraph =
                Path.Combine(
                    GraphRoot,
                    "last_graph.json");

            if (paths.Count == 0 &&
                File.Exists(
                    lastGraph))
            {
                paths.Add(
                    lastGraph);
            }

            foreach (string path
                in paths)
            {
                try
                {
                    AiGraphCloudLocalItem item =
                        BuildCanonicalCloudGraph(
                            path);

                    if (item == null ||
                        !IsSha256(
                            item.Hash))
                    {
                        continue;
                    }

                    if (!unique.ContainsKey(
                            item.Hash))
                    {
                        unique[
                            item.Hash] =
                            item;
                    }
                }
                catch
                {
                }
            }

            return
                unique.Values
                    .OrderBy(
                        x =>
                            x.Hash,
                        StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }

        private AiGraphCloudLocalItem BuildCanonicalCloudGraph(
            string sourcePath)
        {
            using (JsonDocument doc =
                JsonDocument.Parse(
                    File.ReadAllText(
                        sourcePath,
                        Encoding.UTF8)))
            {
                JsonElement root =
                    doc.RootElement;

                if (!root.TryGetProperty(
                        "pipes",
                        out JsonElement pipesElement) ||
                    pipesElement.ValueKind !=
                        JsonValueKind.Array)
                {
                    return null;
                }

                List<CanonicalPipe> raw =
                    new List<CanonicalPipe>();

                int originalIndex =
                    0;

                foreach (JsonElement pipe
                    in pipesElement.EnumerateArray())
                {
                    double[] start =
                        ReadPoint(
                            pipe,
                            "start");

                    double[] end =
                        ReadPoint(
                            pipe,
                            "end");

                    double sx =
                        start[0];

                    double sy =
                        start[1];

                    double ex =
                        end[0];

                    double ey =
                        end[1];

                    if (PointCompare(
                            sx,
                            sy,
                            ex,
                            ey) >
                        0)
                    {
                        double tx =
                            sx;

                        double ty =
                            sy;

                        sx =
                            ex;

                        sy =
                            ey;

                        ex =
                            tx;

                        ey =
                            ty;
                    }

                    List<int> neighbors =
                        ReadIntArray(
                            pipe,
                            "neighbors");

                    raw.Add(
                        new CanonicalPipe
                        {
                            OriginalIndex =
                                originalIndex,
                            StartX =
                                sx,
                            StartY =
                                sy,
                            EndX =
                                ex,
                            EndY =
                                ey,
                            Length =
                                ReadDouble(
                                    pipe,
                                    "length"),
                            Dn =
                                ReadString(
                                    pipe,
                                    "dn"),
                            DnConfidence =
                                ReadDouble(
                                    pipe,
                                    "dn_confidence"),
                            DnSource =
                                ReadString(
                                    pipe,
                                    "dn_source")
                                    .ToUpperInvariant(),
                            AiOverlay =
                                ReadBool(
                                    pipe,
                                    "ai_overlay"),
                            LayerPipe =
                                ReadBool(
                                    pipe,
                                    "layer_pipe") ||
                                LooksLikePipeLayer(
                                    ReadString(
                                        pipe,
                                        "layer")),
                            Neighbors =
                                neighbors
                        });

                    originalIndex++;
                }

                if (raw.Count == 0)
                    return null;

                double originX =
                    raw.Min(
                        p =>
                            Math.Min(
                                p.StartX,
                                p.EndX));

                double originY =
                    raw.Min(
                        p =>
                            Math.Min(
                                p.StartY,
                                p.EndY));

                foreach (CanonicalPipe pipe
                    in raw)
                {
                    pipe.StartX =
                        Quantize(
                            pipe.StartX -
                            originX);

                    pipe.StartY =
                        Quantize(
                            pipe.StartY -
                            originY);

                    pipe.EndX =
                        Quantize(
                            pipe.EndX -
                            originX);

                    pipe.EndY =
                        Quantize(
                            pipe.EndY -
                            originY);

                    pipe.Length =
                        Quantize(
                            pipe.Length);

                    pipe.DnConfidence =
                        Math.Round(
                            Math.Max(
                                0.0,
                                Math.Min(
                                    1.0,
                                    pipe.DnConfidence)),
                            4);
                }

                List<CanonicalPipe> sorted =
                    raw
                        .OrderBy(
                            p =>
                                p.StartX)
                        .ThenBy(
                            p =>
                                p.StartY)
                        .ThenBy(
                            p =>
                                p.EndX)
                        .ThenBy(
                            p =>
                                p.EndY)
                        .ThenBy(
                            p =>
                                p.Length)
                        .ThenBy(
                            p =>
                                p.Dn,
                            StringComparer.OrdinalIgnoreCase)
                        .ThenBy(
                            p =>
                                p.OriginalIndex)
                        .ToList();

                Dictionary<int, int> remap =
                    new Dictionary<int, int>();

                for (int i = 0;
                    i < sorted.Count;
                    i++)
                {
                    remap[
                        sorted[i].OriginalIndex] =
                        i;
                }

                List<object> canonicalPipes =
                    new List<object>();

                for (int i = 0;
                    i < sorted.Count;
                    i++)
                {
                    CanonicalPipe pipe =
                        sorted[i];

                    int[] neighbors =
                        pipe.Neighbors
                            .Where(
                                n =>
                                    remap.ContainsKey(
                                        n))
                            .Select(
                                n =>
                                    remap[n])
                            .Where(
                                n =>
                                    n != i)
                            .Distinct()
                            .OrderBy(
                                n =>
                                    n)
                            .ToArray();

                    canonicalPipes.Add(
                        new
                        {
                            id =
                                i,
                            layer =
                                "",
                            layer_pipe =
                                pipe.LayerPipe,
                            start =
                                new[]
                                {
                                    pipe.StartX,
                                    pipe.StartY
                                },
                            end =
                                new[]
                                {
                                    pipe.EndX,
                                    pipe.EndY
                                },
                            length =
                                pipe.Length,
                            dn =
                                NormalizeDn(
                                    pipe.Dn),
                            dn_confidence =
                                pipe.DnConfidence,
                            dn_source =
                                pipe.DnSource,
                            ai_overlay =
                                pipe.AiOverlay,
                            neighbors =
                                neighbors
                        });
                }

                object canonical =
                    new
                    {
                        version =
                            2,
                        privacy =
                            "SANITIZED_GRAPH_V1",
                        pipes =
                            canonicalPipes
                    };

                string json =
                    JsonSerializer.Serialize(
                        canonical,
                        JsonOptions);

                // Hash cố ý KHÔNG dùng confidence/source/ai-overlay.
                // Cùng geometry + topology + DN labels từ nhiều máy vẫn dedupe,
                // còn server sẽ giữ bản có Ground-truth mạnh hơn.
                object hashCanonical =
                    new
                    {
                        version =
                            2,
                        pipes =
                            sorted.Select(
                                (pipe, i) =>
                                {
                                    int[] neighbors =
                                        pipe.Neighbors
                                            .Where(
                                                n =>
                                                    remap.ContainsKey(
                                                        n))
                                            .Select(
                                                n =>
                                                    remap[n])
                                            .Where(
                                                n =>
                                                    n != i)
                                            .Distinct()
                                            .OrderBy(
                                                n =>
                                                    n)
                                            .ToArray();

                                    return
                                        new
                                        {
                                            id =
                                                i,
                                            layer_pipe =
                                                pipe.LayerPipe,
                                            start =
                                                new[]
                                                {
                                                    pipe.StartX,
                                                    pipe.StartY
                                                },
                                            end =
                                                new[]
                                                {
                                                    pipe.EndX,
                                                    pipe.EndY
                                                },
                                            length =
                                                pipe.Length,
                                            dn =
                                                NormalizeDn(
                                                    pipe.Dn),
                                            neighbors =
                                                neighbors
                                        };
                                })
                            .ToList()
                    };

                string hash =
                    ComputeSha256(
                        JsonSerializer.Serialize(
                            hashCanonical,
                            JsonOptions));

                using (JsonDocument canonicalDoc =
                    JsonDocument.Parse(
                        json))
                {
                    return
                        new AiGraphCloudLocalItem
                        {
                            Hash =
                                hash,
                            SourcePath =
                                sourcePath,
                            Graph =
                                canonicalDoc
                                    .RootElement
                                    .Clone()
                        };
                }
            }
        }

        private AiGraphCloudState LoadState()
        {
            try
            {
                if (!File.Exists(
                        StatePath))
                {
                    return
                        new AiGraphCloudState();
                }

                return
                    JsonSerializer.Deserialize<AiGraphCloudState>(
                        File.ReadAllText(
                            StatePath,
                            Encoding.UTF8),
                        JsonOptions) ??
                    new AiGraphCloudState();
            }
            catch
            {
                return
                    new AiGraphCloudState();
            }
        }

        private void SaveState(
            AiGraphCloudState state)
        {
            try
            {
                Directory.CreateDirectory(
                    GraphRoot);

                File.WriteAllText(
                    StatePath,
                    JsonSerializer.Serialize(
                        state ??
                        new AiGraphCloudState(),
                        new JsonSerializerOptions
                        {
                            WriteIndented =
                                true
                        }),
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
            config.Normalize();

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

                    return
                        responseText;
                }
            }
        }

        private sealed class CanonicalPipe
        {
            public int OriginalIndex { get; set; }
            public double StartX { get; set; }
            public double StartY { get; set; }
            public double EndX { get; set; }
            public double EndY { get; set; }
            public double Length { get; set; }
            public string Dn { get; set; } = "";
            public double DnConfidence { get; set; }
            public string DnSource { get; set; } = "";
            public bool AiOverlay { get; set; }
            public bool LayerPipe { get; set; }
            public List<int> Neighbors { get; set; } =
                new List<int>();
        }

        private static double[] ReadPoint(
            JsonElement element,
            string name)
        {
            if (!element.TryGetProperty(
                    name,
                    out JsonElement value) ||
                value.ValueKind !=
                    JsonValueKind.Array)
            {
                return
                    new[]
                    {
                        0.0,
                        0.0
                    };
            }

            double[] values =
                value
                    .EnumerateArray()
                    .Take(2)
                    .Select(
                        x =>
                            x.ValueKind ==
                                JsonValueKind.Number &&
                            x.TryGetDouble(
                                out double number)
                                ? number
                                : 0.0)
                    .ToArray();

            return
                new[]
                {
                    values.Length > 0
                        ? values[0]
                        : 0.0,
                    values.Length > 1
                        ? values[1]
                        : 0.0
                };
        }

        private static List<int> ReadIntArray(
            JsonElement element,
            string name)
        {
            if (!element.TryGetProperty(
                    name,
                    out JsonElement value) ||
                value.ValueKind !=
                    JsonValueKind.Array)
            {
                return
                    new List<int>();
            }

            List<int> result =
                new List<int>();

            foreach (JsonElement item
                in value.EnumerateArray())
            {
                if (item.ValueKind ==
                        JsonValueKind.Number &&
                    item.TryGetInt32(
                        out int number))
                {
                    result.Add(
                        number);
                }
            }

            return result;
        }

        private static string ReadString(
            JsonElement element,
            string name)
        {
            if (!element.TryGetProperty(
                    name,
                    out JsonElement value))
            {
                return "";
            }

            return
                value.ValueKind ==
                    JsonValueKind.String
                    ? value.GetString() ?? ""
                    : value.ToString();
        }

        private static double ReadDouble(
            JsonElement element,
            string name)
        {
            if (!element.TryGetProperty(
                    name,
                    out JsonElement value))
            {
                return 0.0;
            }

            if (value.ValueKind ==
                    JsonValueKind.Number &&
                value.TryGetDouble(
                    out double number))
            {
                return number;
            }

            double.TryParse(
                value.ToString(),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out number);

            return number;
        }

        private static bool ReadBool(
            JsonElement element,
            string name)
        {
            if (!element.TryGetProperty(
                    name,
                    out JsonElement value))
            {
                return false;
            }

            if (value.ValueKind ==
                JsonValueKind.True)
            {
                return true;
            }

            if (value.ValueKind ==
                JsonValueKind.False)
            {
                return false;
            }

            return
                bool.TryParse(
                    value.ToString(),
                    out bool result) &&
                result;
        }

        private static int PointCompare(
            double ax,
            double ay,
            double bx,
            double by)
        {
            int x =
                ax.CompareTo(
                    bx);

            if (x != 0)
                return x;

            return
                ay.CompareTo(
                    by);
        }

        private static double Quantize(
            double value)
        {
            return
                Math.Round(
                    value,
                    3,
                    MidpointRounding.AwayFromZero);
        }

        private static string NormalizeDn(
            string value)
        {
            return
                (value ?? "")
                    .Trim()
                    .ToUpperInvariant();
        }

        private static bool LooksLikePipeLayer(
            string layer)
        {
            string s =
                (layer ?? "")
                    .ToUpperInvariant();

            string[] tokens =
            {
                "PIPE",
                "PCCC",
                "FIRE",
                "SPRINK",
                "HYDRANT",
                "WATER",
                "CTN",
                "DRAIN",
                "CHW",
                "CWS",
                "HWS",
                "GAS",
                "ONG",
                "ỐNG",
                "PLUMB",
                "MEP"
            };

            return
                tokens.Any(
                    t =>
                        s.Contains(
                            t));
        }

        private static string ComputeSha256(
            string text)
        {
            using (SHA256 sha =
                SHA256.Create())
            {
                byte[] bytes =
                    Encoding.UTF8.GetBytes(
                        text ?? "");

                byte[] digest =
                    sha.ComputeHash(
                        bytes);

                return
                    BitConverter
                        .ToString(
                            digest)
                        .Replace(
                            "-",
                            "")
                        .ToLowerInvariant();
            }
        }

        private static bool IsSha256(
            string value)
        {
            if (string.IsNullOrWhiteSpace(
                    value) ||
                value.Length != 64)
            {
                return false;
            }

            return
                value.All(
                    c =>
                        (c >= '0' &&
                         c <= '9') ||
                        (c >= 'a' &&
                         c <= 'f') ||
                        (c >= 'A' &&
                         c <= 'F'));
        }
    }
}
