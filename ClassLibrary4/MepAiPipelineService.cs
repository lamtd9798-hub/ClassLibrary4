#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ClassLibrary4
{
    /// <summary>
    /// STEP29C - AI CORE SERVICE (phase 1)
    ///
    /// Tách session lifecycle + metric decision khỏi BOCTACHUI.xaml.cs.
    /// Chưa di chuyển toàn bộ AutoCAD DB/Transaction code trong một lần để
    /// tránh regression. Các STEP sau sẽ tiếp tục chuyển Fusion/Review/Pipeline
    /// vào service độc lập trên nền interface này.
    /// </summary>
    internal sealed class MepAiPipelineService
    {
        private readonly MepAiMetricsStore _metricsStore =
            new MepAiMetricsStore();

        private readonly Stopwatch _stopwatch =
            new Stopwatch();

        private string _sessionId = "";
        private string _pipelineName = "";
        private string _drawingKey = "";
        private DateTime _startedUtc = DateTime.MinValue;

        private List<MepAiMetricCandidate> _preReview =
            new List<MepAiMetricCandidate>();

        public MepAiMetricsSnapshot LastSnapshot { get; private set; }

        public void BeginScan(
            string drawingKey,
            string pipelineName)
        {
            _sessionId = Guid.NewGuid().ToString("N");
            _drawingKey = drawingKey ?? "";
            _pipelineName = string.IsNullOrWhiteSpace(pipelineName)
                ? "SMART_MEP"
                : pipelineName.Trim();
            _startedUtc = DateTime.UtcNow;
            _preReview = new List<MepAiMetricCandidate>();
            LastSnapshot = null;

            _stopwatch.Restart();
        }

        // STEP29D - dynamic confidence gate lấy lịch sử override theo class.
        public MepAiDecisionFusionEngine.GateHistory GetDynamicGateHistory(
            string label)
        {
            return _metricsStore.BuildGateHistory(label);
        }

        public void CapturePreReview(
            IEnumerable<MepAiMetricCandidate> candidates)
        {
            _preReview = CloneCandidates(candidates);
        }

        public MepAiMetricsSnapshot CompleteScan(
            IEnumerable<MepAiMetricCandidate> finalCandidates,
            MepAiScanDiagnostics diagnostics)
        {
            _stopwatch.Stop();

            List<MepAiMetricCandidate> before =
                _preReview ?? new List<MepAiMetricCandidate>();

            List<MepAiMetricCandidate> after =
                CloneCandidates(finalCandidates);

            Dictionary<string, MepAiMetricCandidate> beforeByKey =
                before
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.CandidateKey))
                    .GroupBy(x => x.CandidateKey, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            int autoAcceptedBefore =
                before.Count(x => IsAutoAcceptedStatus(x?.Status));

            int reviewBefore =
                before.Count(x => IsReviewStatus(x?.Status));

            int unknownBefore =
                before.Count(x => IsUnknown(x));

            int contextConflictBefore =
                before.Count(x =>
                    string.Equals(
                        x?.Status,
                        "CONTEXT_CONFLICT",
                        StringComparison.OrdinalIgnoreCase));

            int dnCheckBefore =
                before.Count(x =>
                    string.Equals(x?.Status, "DN_CHECK", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(x?.Status, "NO_DN", StringComparison.OrdinalIgnoreCase));

            int userOverrideCount = 0;
            int labelChangedCount = 0;
            int unknownResolvedByUser = 0;

            foreach (MepAiMetricCandidate current in after)
            {
                if (current == null)
                    continue;

                MepAiMetricCandidate initial = null;

                if (!string.IsNullOrWhiteSpace(current.CandidateKey))
                {
                    beforeByKey.TryGetValue(
                        current.CandidateKey,
                        out initial);
                }

                bool edited = current.UserEdited;
                bool labelChanged =
                    initial != null &&
                    !string.Equals(
                        NormalizeLabel(initial.Label),
                        NormalizeLabel(current.Label),
                        StringComparison.OrdinalIgnoreCase);

                if (edited || labelChanged)
                {
                    userOverrideCount++;
                }

                if (labelChanged)
                {
                    labelChangedCount++;
                }

                if (initial != null &&
                    IsUnknown(initial) &&
                    !IsUnknown(current) &&
                    (edited || labelChanged))
                {
                    unknownResolvedByUser++;
                }
            }

            int finalOk =
                after.Count(x => IsAutoAcceptedStatus(x?.Status));

            int finalNeedsReview =
                after.Count(x => IsReviewStatus(x?.Status));

            double averageConfidence =
                after
                    .Where(x => x != null && x.Confidence > 0.0)
                    .Select(x => x.Confidence)
                    .DefaultIfEmpty(0.0)
                    .Average();

            MepAiMetricsSnapshot snapshot =
                new MepAiMetricsSnapshot
                {
                    SessionId =
                        string.IsNullOrWhiteSpace(_sessionId)
                            ? Guid.NewGuid().ToString("N")
                            : _sessionId,
                    PipelineName = _pipelineName,
                    DrawingKey = _drawingKey,
                    StartedUtc =
                        (_startedUtc == DateTime.MinValue
                            ? DateTime.UtcNow
                            : _startedUtc).ToString("O"),
                    CompletedUtc = DateTime.UtcNow.ToString("O"),
                    DurationMs = _stopwatch.ElapsedMilliseconds,
                    TotalBeforeReview = before.Count,
                    AutoAcceptedBeforeReview = autoAcceptedBefore,
                    ReviewBeforeApply = reviewBefore,
                    UnknownBeforeApply = unknownBefore,
                    ContextConflictBeforeApply = contextConflictBefore,
                    DnCheckBeforeApply = dnCheckBefore,
                    TotalAfterApply = after.Count,
                    FinalOk = finalOk,
                    FinalNeedsReview = finalNeedsReview,
                    UserOverrideCount = userOverrideCount,
                    LabelChangedCount = labelChangedCount,
                    UnknownResolvedByUser = unknownResolvedByUser,
                    AutoAcceptRate = Ratio(autoAcceptedBefore, before.Count),
                    ReviewRate = Ratio(reviewBefore, before.Count),
                    OverrideRateAmongReviewed = Ratio(userOverrideCount, reviewBefore),
                    FinalResolvedRate = Ratio(finalOk, after.Count),
                    AverageConfidence = averageConfidence,
                    Diagnostics = diagnostics ?? new MepAiScanDiagnostics(),
                    SourceCounts = BuildSourceCounts(after),
                    Classes = BuildClassMetrics(before, after, beforeByKey)
                };

            LastSnapshot = snapshot;
            _metricsStore.Append(snapshot);

            return snapshot;
        }

        public string GetLastCompactSummary()
        {
            return MepAiMetricsStore.BuildCompactSummary(LastSnapshot);
        }

        public static bool IsReviewStatus(string status)
        {
            return
                string.Equals(status, "MISSING", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "CONTEXT_CONFLICT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "FUSION_REVIEW", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "DN_CHECK", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "NO_DN", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAutoAcceptedStatus(string status)
        {
            return string.Equals(
                status,
                "OK",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUnknown(MepAiMetricCandidate candidate)
        {
            if (candidate == null)
                return true;

            string label = NormalizeLabel(candidate.Label);

            return
                string.Equals(
                    candidate.Status,
                    "MISSING",
                    StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(label) ||
                string.Equals(
                    label,
                    "CHƯA NHẬN DIỆN",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeLabel(string label)
        {
            return (label ?? "").Trim();
        }

        private static double Ratio(int numerator, int denominator)
        {
            return denominator <= 0
                ? 0.0
                : numerator * 1.0 / denominator;
        }

        private static List<MepAiMetricCandidate> CloneCandidates(
            IEnumerable<MepAiMetricCandidate> candidates)
        {
            if (candidates == null)
                return new List<MepAiMetricCandidate>();

            return candidates
                .Where(x => x != null)
                .Select(x => new MepAiMetricCandidate
                {
                    CandidateKey = x.CandidateKey ?? "",
                    Status = x.Status ?? "",
                    Label = x.Label ?? "",
                    AlternativeLabel = x.AlternativeLabel ?? "",
                    Source = x.Source ?? "",
                    MatchMode = x.MatchMode ?? "",
                    Confidence = x.Confidence,
                    UserEdited = x.UserEdited,
                    FusionDecisionCode = x.FusionDecisionCode ?? "",
                    FusionScore = x.FusionScore,
                    FusionGateThreshold = x.FusionGateThreshold
                })
                .ToList();
        }

        private static Dictionary<string, int> BuildSourceCounts(
            IEnumerable<MepAiMetricCandidate> candidates)
        {
            Dictionary<string, int> result =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            if (candidates == null)
                return result;

            foreach (MepAiMetricCandidate candidate in candidates)
            {
                if (candidate == null)
                    continue;

                string source =
                    string.IsNullOrWhiteSpace(candidate.Source)
                        ? "UNKNOWN"
                        : candidate.Source.Trim();

                if (!result.ContainsKey(source))
                {
                    result[source] = 0;
                }

                result[source]++;
            }

            return result;
        }

        private static List<MepAiClassMetric> BuildClassMetrics(
            List<MepAiMetricCandidate> before,
            List<MepAiMetricCandidate> after,
            Dictionary<string, MepAiMetricCandidate> beforeByKey)
        {
            HashSet<string> labels =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (MepAiMetricCandidate item in before.Concat(after))
            {
                if (item == null)
                    continue;

                string label = NormalizeLabel(item.Label);

                if (!string.IsNullOrWhiteSpace(label) &&
                    !string.Equals(label, "CHƯA NHẬN DIỆN", StringComparison.OrdinalIgnoreCase))
                {
                    labels.Add(label);
                }
            }

            List<MepAiClassMetric> result =
                new List<MepAiClassMetric>();

            foreach (string label in labels.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                List<MepAiMetricCandidate> beforeClass =
                    before
                        .Where(x =>
                            x != null &&
                            string.Equals(
                                NormalizeLabel(x.Label),
                                label,
                                StringComparison.OrdinalIgnoreCase))
                        .ToList();

                int userOverrides = 0;
                int labelChanged = 0;

                // STEP29D: override-rate phải gắn với NHÃN AI BAN ĐẦU,
                // không phải nhãn sau khi user đã sửa. Đây là dữ liệu dùng
                // để dynamic gate biết class nào hay dự đoán sai.
                Dictionary<string, MepAiMetricCandidate> afterByKey =
                    after
                        .Where(x =>
                            x != null &&
                            !string.IsNullOrWhiteSpace(x.CandidateKey))
                        .GroupBy(
                            x => x.CandidateKey,
                            StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            g => g.Key,
                            g => g.First(),
                            StringComparer.OrdinalIgnoreCase);

                foreach (MepAiMetricCandidate initial in beforeClass)
                {
                    if (initial == null ||
                        string.IsNullOrWhiteSpace(initial.CandidateKey))
                    {
                        continue;
                    }

                    if (!afterByKey.TryGetValue(
                            initial.CandidateKey,
                            out MepAiMetricCandidate current) ||
                        current == null)
                    {
                        continue;
                    }

                    bool changed =
                        !string.Equals(
                            NormalizeLabel(initial.Label),
                            NormalizeLabel(current.Label),
                            StringComparison.OrdinalIgnoreCase);

                    if (current.UserEdited || changed)
                    {
                        userOverrides++;
                    }

                    if (changed)
                    {
                        labelChanged++;
                    }
                }

                result.Add(
                    new MepAiClassMetric
                    {
                        Label = label,
                        CandidateCount = beforeClass.Count,
                        AutoAcceptedBeforeReview =
                            beforeClass.Count(x => IsAutoAcceptedStatus(x.Status)),
                        ReviewBeforeApply =
                            beforeClass.Count(x => IsReviewStatus(x.Status)),
                        UnknownBeforeApply =
                            beforeClass.Count(IsUnknown),
                        UserOverrideCount = userOverrides,
                        LabelChangedCount = labelChanged,
                        AverageConfidence =
                            beforeClass
                                .Where(x => x.Confidence > 0.0)
                                .Select(x => x.Confidence)
                                .DefaultIfEmpty(0.0)
                                .Average()
                    });
            }

            return result;
        }
    }
}