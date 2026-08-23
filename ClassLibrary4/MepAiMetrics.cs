#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace ClassLibrary4
{
    /// <summary>
    /// STEP29C - AI METRICS
    /// Pure-data metrics layer. Không phụ thuộc AutoCAD/WPF/OpenCV.
    /// Mục tiêu: đo đúng hệ thống đang tự động hóa được bao nhiêu,
    /// class nào hay bị user sửa, engine nào đang tạo nhiều REVIEW.
    ///
    /// Lưu ý: đây là operational metrics / verified-by-user proxy,
    /// KHÔNG tự gọi là precision/recall chuẩn nếu chưa có ground-truth dataset.
    /// </summary>
    internal sealed class MepAiMetricCandidate
    {
        public string CandidateKey { get; set; } = "";
        public string Status { get; set; } = "";
        public string Label { get; set; } = "";
        public string AlternativeLabel { get; set; } = "";
        public string Source { get; set; } = "";
        public string MatchMode { get; set; } = "";
        public double Confidence { get; set; } = 0.0;
        public bool UserEdited { get; set; } = false;

        // STEP29D - unified fusion trace.
        public string FusionDecisionCode { get; set; } = "";
        public double FusionScore { get; set; } = 0.0;
        public double FusionGateThreshold { get; set; } = 0.0;
    }

    internal sealed class MepAiScanDiagnostics
    {
        public int ContextEvaluated { get; set; }
        public int ContextSwitched { get; set; }
        public int ContextConflict { get; set; }

        public int SpatialQueries { get; set; }
        public int DbscanClusters { get; set; }
        public int DbscanNoise { get; set; }

        public int PatternTeachers { get; set; }
        public int PatternEvaluated { get; set; }
        public int PatternAutoKeep { get; set; }
        public int PatternAutoSwitch { get; set; }
        public int PatternSuggested { get; set; }

        public int OpenCvAnalyzed { get; set; }
        public int OpenCvRefined { get; set; }
        public int OpenCvKeptRaw { get; set; }
        public int OpenCvSkipped { get; set; }

        public int OpenCvTileRuns { get; set; }
        public int OpenCvTileRegions { get; set; }
        public int OpenCvTileRescues { get; set; }
        public int OpenCvTileFallbacks { get; set; }

        public int WorldTileRuns { get; set; }
        public int WorldTileRegions { get; set; }
        public int WorldTileRescues { get; set; }
        public int WorldTileDenseSkips { get; set; }

        public int YoloTileRuns { get; set; }
        public int YoloRawDetections { get; set; }
        public int YoloAccepted { get; set; }
        public int YoloRejected { get; set; }

        // STEP29D - unified fusion diagnostics.
        public int FusionEvaluated { get; set; }
        public int FusionAutoAccepted { get; set; }
        public int FusionKept { get; set; }
        public int FusionSwitched { get; set; }
        public int FusionReview { get; set; }

        public int NmsSuppressed { get; set; }
    }

    internal sealed class MepAiClassMetric
    {
        public string Label { get; set; } = "";
        public int CandidateCount { get; set; }
        public int AutoAcceptedBeforeReview { get; set; }
        public int ReviewBeforeApply { get; set; }
        public int UnknownBeforeApply { get; set; }
        public int UserOverrideCount { get; set; }
        public int LabelChangedCount { get; set; }
        public double AverageConfidence { get; set; }

        public double OverrideRate
        {
            get
            {
                int denominator = Math.Max(1, ReviewBeforeApply);
                return UserOverrideCount * 1.0 / denominator;
            }
        }
    }

    internal sealed class MepAiMetricsSnapshot
    {
        public string SessionId { get; set; } = "";
        public string PipelineName { get; set; } = "";
        public string DrawingKey { get; set; } = "";
        public string StartedUtc { get; set; } = "";
        public string CompletedUtc { get; set; } = "";
        public long DurationMs { get; set; }

        public int TotalBeforeReview { get; set; }
        public int AutoAcceptedBeforeReview { get; set; }
        public int ReviewBeforeApply { get; set; }
        public int UnknownBeforeApply { get; set; }
        public int ContextConflictBeforeApply { get; set; }
        public int DnCheckBeforeApply { get; set; }

        public int TotalAfterApply { get; set; }
        public int FinalOk { get; set; }
        public int FinalNeedsReview { get; set; }
        public int UserOverrideCount { get; set; }
        public int LabelChangedCount { get; set; }
        public int UnknownResolvedByUser { get; set; }

        public double AutoAcceptRate { get; set; }
        public double ReviewRate { get; set; }
        public double OverrideRateAmongReviewed { get; set; }
        public double FinalResolvedRate { get; set; }
        public double AverageConfidence { get; set; }

        public Dictionary<string, int> SourceCounts { get; set; } =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public List<MepAiClassMetric> Classes { get; set; } =
            new List<MepAiClassMetric>();

        public MepAiScanDiagnostics Diagnostics { get; set; } =
            new MepAiScanDiagnostics();
    }

    internal sealed class MepAiMetricsStore
    {
        private readonly JsonSerializerOptions _jsonOptions =
            new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            };

        // STEP29D: cache file metric trong RAM để dynamic gate không đọc lại
        // scan_metrics_v1.jsonl cho từng class trong cùng một phiên quét.
        private readonly object _historyGate = new object();
        private List<MepAiMetricsSnapshot> _recentSnapshotCache = null;
        private DateTime _recentSnapshotCacheWriteUtc = DateTime.MinValue;

        public string BaseFolder { get; }
        public string JsonLinesPath { get; }
        public string LatestPath { get; }

        public MepAiMetricsStore()
        {
            string appData =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData);

            if (string.IsNullOrWhiteSpace(appData))
            {
                appData = Path.GetTempPath();
            }

            BaseFolder =
                Path.Combine(
                    appData,
                    "TDL_MEP",
                    "AI_Metrics");

            JsonLinesPath =
                Path.Combine(
                    BaseFolder,
                    "scan_metrics_v1.jsonl");

            LatestPath =
                Path.Combine(
                    BaseFolder,
                    "latest_scan_metrics.json");

            Directory.CreateDirectory(BaseFolder);
        }

        // STEP29D - dùng metric lịch sử để điều chỉnh gate RẤT NHẸ.
        // Chỉ history đã có review thật mới ảnh hưởng threshold.
        public MepAiDecisionFusionEngine.GateHistory BuildGateHistory(
            string label,
            int maxSnapshots = 40)
        {
            MepAiDecisionFusionEngine.GateHistory history =
                new MepAiDecisionFusionEngine.GateHistory();

            string normalized =
                (label ?? "").Trim();

            if (string.IsNullOrWhiteSpace(normalized))
                return history;

            try
            {
                List<MepAiMetricsSnapshot> snapshots =
                    LoadRecentSnapshots(
                        Math.Max(5, maxSnapshots));

                int reviewed = 0;
                int overrides = 0;

                foreach (MepAiMetricsSnapshot snapshot in snapshots)
                {
                    if (snapshot?.Classes == null)
                        continue;

                    foreach (MepAiClassMetric metric in snapshot.Classes)
                    {
                        if (metric == null ||
                            !string.Equals(
                                (metric.Label ?? "").Trim(),
                                normalized,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        reviewed +=
                            Math.Max(
                                0,
                                metric.ReviewBeforeApply);

                        overrides +=
                            Math.Max(
                                0,
                                metric.UserOverrideCount);
                    }
                }

                history.ReviewedCount = reviewed;
                history.OverrideCount = overrides;
                history.OverrideRate =
                    reviewed <= 0
                        ? 0.0
                        : overrides * 1.0 / reviewed;

                double rate = history.OverrideRate;
                double adjustment = 0.0;

                if (reviewed >= 6)
                {
                    if (rate >= 0.30)
                        adjustment = 0.080;
                    else if (rate >= 0.18)
                        adjustment = 0.050;
                    else if (rate >= 0.08)
                        adjustment = 0.025;
                    else if (rate <= 0.02 && reviewed >= 20)
                        adjustment = -0.015;
                }

                history.ThresholdAdjustment = adjustment;
                history.Reason =
                    reviewed < 6
                        ? "Chưa đủ lịch sử review để đổi gate."
                        : "Gate điều chỉnh theo user override lịch sử.";
            }
            catch
            {
                // Metrics/history tuyệt đối không được làm hỏng scan.
            }

            return history;
        }

        private List<MepAiMetricsSnapshot> LoadRecentSnapshots(
            int maxSnapshots)
        {
            int take = Math.Max(1, maxSnapshots);

            try
            {
                if (!File.Exists(JsonLinesPath))
                    return new List<MepAiMetricsSnapshot>();

                DateTime writeUtc =
                    File.GetLastWriteTimeUtc(
                        JsonLinesPath);

                lock (_historyGate)
                {
                    if (_recentSnapshotCache != null &&
                        _recentSnapshotCacheWriteUtc == writeUtc)
                    {
                        return _recentSnapshotCache
                            .Take(take)
                            .ToList();
                    }
                }

                string[] lines =
                    File.ReadAllLines(
                        JsonLinesPath,
                        Encoding.UTF8);

                List<MepAiMetricsSnapshot> parsed =
                    new List<MepAiMetricsSnapshot>();

                // Cache tối đa 80 phiên gần nhất; BuildGateHistory mặc định dùng 40.
                int cacheLimit = Math.Max(80, take);

                for (int i = lines.Length - 1;
                    i >= 0 && parsed.Count < cacheLimit;
                    i--)
                {
                    string line =
                        lines[i]?.Trim();

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    try
                    {
                        MepAiMetricsSnapshot snapshot =
                            JsonSerializer.Deserialize<MepAiMetricsSnapshot>(
                                line,
                                _jsonOptions);

                        if (snapshot != null)
                            parsed.Add(snapshot);
                    }
                    catch
                    {
                    }
                }

                lock (_historyGate)
                {
                    _recentSnapshotCache = parsed;
                    _recentSnapshotCacheWriteUtc = writeUtc;
                }

                return parsed
                    .Take(take)
                    .ToList();
            }
            catch
            {
                return new List<MepAiMetricsSnapshot>();
            }
        }

        public void Append(MepAiMetricsSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            try
            {
                Directory.CreateDirectory(BaseFolder);

                string json =
                    JsonSerializer.Serialize(
                        snapshot,
                        _jsonOptions);

                File.AppendAllText(
                    JsonLinesPath,
                    json + Environment.NewLine,
                    Encoding.UTF8);

                File.WriteAllText(
                    LatestPath,
                    JsonSerializer.Serialize(
                        snapshot,
                        new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        }),
                    Encoding.UTF8);

                TrimJsonLinesIfNeeded(1000);

                lock (_historyGate)
                {
                    _recentSnapshotCache = null;
                    _recentSnapshotCacheWriteUtc = DateTime.MinValue;
                }
            }
            catch
            {
                // Metrics tuyệt đối không được làm hỏng pipeline CAD chính.
            }
        }

        private void TrimJsonLinesIfNeeded(int maxLines)
        {
            try
            {
                if (!File.Exists(JsonLinesPath))
                    return;

                string[] lines =
                    File.ReadAllLines(
                        JsonLinesPath,
                        Encoding.UTF8);

                if (lines.Length <= maxLines)
                    return;

                string[] tail =
                    lines
                        .Skip(lines.Length - maxLines)
                        .ToArray();

                File.WriteAllLines(
                    JsonLinesPath,
                    tail,
                    Encoding.UTF8);
            }
            catch
            {
            }
        }

        public static string BuildCompactSummary(MepAiMetricsSnapshot snapshot)
        {
            if (snapshot == null)
                return "AI METRIC: chưa có dữ liệu.";

            return
                "AI METRIC: AUTO " +
                (snapshot.AutoAcceptRate * 100.0).ToString(
                    "0.0",
                    CultureInfo.InvariantCulture) +
                "% | REVIEW " +
                (snapshot.ReviewRate * 100.0).ToString(
                    "0.0",
                    CultureInfo.InvariantCulture) +
                "% | USER SỬA " +
                snapshot.UserOverrideCount.ToString(
                    CultureInfo.InvariantCulture) +
                " | GIẢI QUYẾT CUỐI " +
                (snapshot.FinalResolvedRate * 100.0).ToString(
                    "0.0",
                    CultureInfo.InvariantCulture) +
                "% | " +
                snapshot.DurationMs.ToString(
                    CultureInfo.InvariantCulture) +
                " ms" +
                Environment.NewLine +
                "FUSION: AUTO " +
                (snapshot.Diagnostics?.FusionAutoAccepted ?? 0).ToString(
                    CultureInfo.InvariantCulture) +
                " | KEEP " +
                (snapshot.Diagnostics?.FusionKept ?? 0).ToString(
                    CultureInfo.InvariantCulture) +
                " | SWITCH " +
                (snapshot.Diagnostics?.FusionSwitched ?? 0).ToString(
                    CultureInfo.InvariantCulture) +
                " | REVIEW " +
                (snapshot.Diagnostics?.FusionReview ?? 0).ToString(
                    CultureInfo.InvariantCulture);
        }
    }
}