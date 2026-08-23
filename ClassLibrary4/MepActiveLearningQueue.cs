#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ClassLibrary4
{
    /// <summary>
    /// STEP29E - ACTIVE LEARNING + UNKNOWN MINING + HARD-NEGATIVE QUEUE.
    ///
    /// Pure-data service: không phụ thuộc AutoCAD/WPF/OpenCV.
    /// Nhiệm vụ:
    /// - Xếp hạng đúng các mẫu đáng hỏi user nhất.
    /// - Nhận diện OPEN-SET/UNKNOWN thay vì ép một class yếu.
    /// - Ưu tiên model disagreement / low-margin / conflict / hard-negative.
    /// - Giảm duplicate review theo signature, nhưng KHÔNG suppress khi cùng
    ///   signature lại sinh ra nhiều label khác nhau (đó là conflict quan trọng).
    /// - Chỉ xếp hạng/gợi ý; KHÔNG tự đổi label và KHÔNG tự học vào dataset.
    /// </summary>
    internal sealed class MepActiveLearningCandidate
    {
        public string CandidateKey { get; set; } = "";
        public string Signature { get; set; } = "";
        public string Status { get; set; } = "";
        public string Label { get; set; } = "";
        public string OriginalLabel { get; set; } = "";
        public string AlternativeLabel { get; set; } = "";
        public string Source { get; set; } = "";
        public string MatchMode { get; set; } = "";

        public double Confidence { get; set; }
        public double AlternativeConfidence { get; set; }
        public double VisualMargin { get; set; }

        public string ContextDecisionCode { get; set; } = "";
        public string PatternDecisionCode { get; set; } = "";
        public double PatternConfidence { get; set; }

        public string FusionDecisionCode { get; set; } = "";
        public double FusionScore { get; set; }
        public double FusionAlternativeScore { get; set; }
        public double FusionGateThreshold { get; set; }

        public bool UserEdited { get; set; }
    }

    internal sealed class MepActiveLearningDecision
    {
        public string CandidateKey { get; set; } = "";
        public string Kind { get; set; } = "";
        public string PriorityBand { get; set; } = "";
        public double PriorityScore { get; set; }
        public string Reason { get; set; } = "";

        public bool IsUnknown { get; set; }
        public bool IsHardNegativeCandidate { get; set; }
        public bool IsModelDisagreement { get; set; }
        public bool IsHighPriority { get; set; }
        public bool ReviewRecommended { get; set; }
        public bool IsRepresentative { get; set; } = true;
        public int DuplicateCount { get; set; } = 1;
    }

    internal sealed class MepActiveLearningQueueSummary
    {
        public int TotalCandidates { get; set; }
        public int Queued { get; set; }
        public int HighPriority { get; set; }
        public int Unknown { get; set; }
        public int HardNegativeCandidates { get; set; }
        public int ModelDisagreements { get; set; }
        public int DuplicateSuppressed { get; set; }
        public string UpdatedUtc { get; set; } = "";
        public List<MepActiveLearningDecision> Decisions { get; set; } =
            new List<MepActiveLearningDecision>();
    }

    internal sealed class MepActiveLearningQueue
    {
        private const double HighPriorityThreshold = 75.0;
        private const double QueueThreshold = 35.0;

        private readonly JsonSerializerOptions _jsonOptions =
            new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

        public string BaseFolder { get; }
        public string LatestQueuePath { get; }

        public MepActiveLearningQueue()
        {
            string appData =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData);

            if (string.IsNullOrWhiteSpace(appData))
                appData = Path.GetTempPath();

            BaseFolder =
                Path.Combine(
                    appData,
                    "TDL_MEP",
                    "AI_ActiveLearning");

            LatestQueuePath =
                Path.Combine(
                    BaseFolder,
                    "latest_active_learning_queue.json");

            try
            {
                Directory.CreateDirectory(BaseFolder);
            }
            catch
            {
            }
        }

        public MepActiveLearningQueueSummary BuildQueue(
            IEnumerable<MepActiveLearningCandidate> candidates,
            bool persistLatest = true)
        {
            List<MepActiveLearningCandidate> input =
                (candidates ?? Enumerable.Empty<MepActiveLearningCandidate>())
                    .Where(x => x != null)
                    .ToList();

            MepActiveLearningQueueSummary summary =
                new MepActiveLearningQueueSummary
                {
                    TotalCandidates = input.Count,
                    UpdatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                };

            Dictionary<string, int> labelFrequency =
                input
                    .Select(x => NormalizeLabel(x.Label))
                    .Where(x => !string.IsNullOrWhiteSpace(x) && !IsUnknownLabel(x))
                    .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            List<MepActiveLearningDecision> decisions =
                input
                    .Select(x => Evaluate(x, labelFrequency))
                    .ToList();

            ApplyDuplicateSuppression(
                input,
                decisions);

            decisions =
                decisions
                    .OrderByDescending(x => x.IsRepresentative)
                    .ThenByDescending(x => x.PriorityScore)
                    .ThenBy(x => x.CandidateKey, StringComparer.OrdinalIgnoreCase)
                    .ToList();

            summary.Decisions = decisions;
            summary.Queued = decisions.Count(x => x.ReviewRecommended && x.IsRepresentative);
            summary.HighPriority = decisions.Count(x => x.IsHighPriority && x.IsRepresentative);
            summary.Unknown = decisions.Count(x => x.IsUnknown && x.IsRepresentative);
            summary.HardNegativeCandidates = decisions.Count(x => x.IsHardNegativeCandidate && x.IsRepresentative);
            summary.ModelDisagreements = decisions.Count(x => x.IsModelDisagreement && x.IsRepresentative);
            summary.DuplicateSuppressed = decisions.Count(x => !x.IsRepresentative);

            if (persistLatest)
                SaveLatest(summary);

            return summary;
        }

        private static MepActiveLearningDecision Evaluate(
            MepActiveLearningCandidate candidate,
            Dictionary<string, int> labelFrequency)
        {
            string status = Normalize(candidate.Status);
            string label = NormalizeLabel(candidate.Label);
            string original = NormalizeLabel(candidate.OriginalLabel);
            string alternative = NormalizeLabel(candidate.AlternativeLabel);
            string fusionDecision = Normalize(candidate.FusionDecisionCode);
            string contextDecision = Normalize(candidate.ContextDecisionCode);

            double confidence = Clamp01(candidate.Confidence);
            double altConfidence = Clamp01(candidate.AlternativeConfidence);
            double fusionScore = Clamp01(candidate.FusionScore);
            double fusionAlt = Clamp01(candidate.FusionAlternativeScore);
            double gate = Clamp01(candidate.FusionGateThreshold);

            double margin = candidate.VisualMargin;
            if (margin <= 0.0 && confidence > 0.0 && altConfidence > 0.0)
                margin = confidence - altConfidence;
            if (margin <= 0.0 && fusionScore > 0.0 && fusionAlt > 0.0)
                margin = fusionScore - fusionAlt;
            margin = Math.Max(-1.0, Math.Min(1.0, margin));

            bool statusReview = IsReviewStatus(status);
            bool missing =
                status == "MISSING" ||
                string.IsNullOrWhiteSpace(label) ||
                IsUnknownLabel(label);

            bool modelDisagreement =
                !string.IsNullOrWhiteSpace(alternative) &&
                !IsUnknownLabel(alternative) &&
                !string.Equals(label, alternative, StringComparison.OrdinalIgnoreCase);

            bool fusionReview =
                status == "FUSION_REVIEW" ||
                fusionDecision == "REVIEW";

            bool contextConflict =
                status == "CONTEXT_CONFLICT" ||
                contextDecision == "CONFLICT";

            bool correctedHardNegative =
                candidate.UserEdited &&
                !string.IsNullOrWhiteSpace(original) &&
                !IsUnknownLabel(original) &&
                !string.IsNullOrWhiteSpace(label) &&
                !IsUnknownLabel(label) &&
                !string.Equals(original, label, StringComparison.OrdinalIgnoreCase);

            bool ambiguousHardNegative =
                modelDisagreement &&
                Math.Abs(margin) <= 0.10 &&
                IsProbabilisticSource(candidate.Source);

            bool hardNegative =
                correctedHardNegative ||
                ambiguousHardNegative;

            double effectiveScore =
                fusionScore > 0.0
                    ? fusionScore
                    : confidence;

            double effectiveGate =
                gate > 0.0
                    ? gate
                    : 0.90;

            bool openSetUnknown =
                missing ||
                (fusionReview &&
                 effectiveScore > 0.0 &&
                 effectiveScore < Math.Max(0.48, effectiveGate - 0.14) &&
                 Math.Abs(margin) <= 0.10) ||
                (IsProbabilisticSource(candidate.Source) &&
                 confidence > 0.0 &&
                 confidence < 0.42 &&
                 Math.Abs(margin) <= 0.08);

            double score = 0.0;
            List<string> reasons = new List<string>();

            if (openSetUnknown)
            {
                score += 46.0;
                reasons.Add("UNKNOWN/open-set");
            }

            if (fusionReview)
            {
                score += 34.0;
                reasons.Add("Fusion REVIEW");
            }

            if (contextConflict)
            {
                score += 30.0;
                reasons.Add("Context conflict");
            }

            if (status == "DN_CHECK" || status == "NO_DN")
            {
                score += 18.0;
                reasons.Add("DN chưa chắc");
            }

            if (hardNegative)
            {
                score += correctedHardNegative ? 42.0 : 20.0;
                reasons.Add(correctedHardNegative ? "hard-negative user sửa" : "hard-negative candidate");
            }

            if (modelDisagreement)
            {
                score += 13.0;
                reasons.Add("model disagreement");
            }

            double absMargin = Math.Abs(margin);
            if (absMargin > 0.0 && absMargin < 0.08)
            {
                score += 22.0;
                reasons.Add("margin rất thấp");
            }
            else if (absMargin >= 0.08 && absMargin < 0.15)
            {
                score += 13.0;
                reasons.Add("margin thấp");
            }

            if (effectiveScore > 0.0 && effectiveScore < 0.55)
            {
                score += 18.0;
                reasons.Add("confidence thấp");
            }
            else if (effectiveScore >= 0.55 && effectiveScore < 0.75)
            {
                score += 9.0;
                reasons.Add("confidence trung bình");
            }

            if (gate > 0.0 && effectiveScore > 0.0)
            {
                double gateGap = gate - effectiveScore;
                if (gateGap > 0.0 && gateGap <= 0.08)
                {
                    score += 8.0;
                    reasons.Add("sát ngưỡng gate");
                }
            }

            if (!string.IsNullOrWhiteSpace(label) &&
                labelFrequency != null &&
                labelFrequency.TryGetValue(label, out int frequency) &&
                frequency <= 2)
            {
                score += 6.0;
                reasons.Add("class hiếm trong vùng");
            }

            if (candidate.UserEdited)
            {
                score += 5.0;
                reasons.Add("đã có human feedback");
            }

            // Deterministic OK không nên bị Active Learning kéo lên chỉ vì class hiếm.
            if (!statusReview &&
                !fusionReview &&
                !contextConflict &&
                !hardNegative &&
                !modelDisagreement &&
                IsDeterministicSource(candidate.Source, candidate.MatchMode))
            {
                score = Math.Min(score, 12.0);
            }

            score = Math.Max(0.0, Math.Min(100.0, score));

            bool reviewRecommended =
                statusReview ||
                openSetUnknown ||
                hardNegative ||
                score >= QueueThreshold;

            string kind;
            if (openSetUnknown)
                kind = "UNKNOWN";
            else if (correctedHardNegative)
                kind = "HARD_NEG";
            else if (fusionReview || contextConflict)
                kind = "CONFLICT";
            else if (modelDisagreement)
                kind = "DISAGREE";
            else if (status == "DN_CHECK" || status == "NO_DN")
                kind = "DN";
            else if (reviewRecommended)
                kind = "LOW_MARGIN";
            else
                kind = "OK";

            string band =
                score >= HighPriorityThreshold
                    ? "P1"
                    : score >= 55.0
                        ? "P2"
                        : score >= QueueThreshold
                            ? "P3"
                            : "P4";

            return new MepActiveLearningDecision
            {
                CandidateKey = candidate.CandidateKey ?? "",
                Kind = kind,
                PriorityBand = band,
                PriorityScore = score,
                Reason = reasons.Count == 0 ? "Không cần ưu tiên học." : string.Join(" + ", reasons),
                IsUnknown = openSetUnknown,
                IsHardNegativeCandidate = hardNegative,
                IsModelDisagreement = modelDisagreement,
                IsHighPriority = score >= HighPriorityThreshold,
                ReviewRecommended = reviewRecommended,
                IsRepresentative = true,
                DuplicateCount = 1
            };
        }

        private static void ApplyDuplicateSuppression(
            List<MepActiveLearningCandidate> candidates,
            List<MepActiveLearningDecision> decisions)
        {
            if (candidates == null || decisions == null || candidates.Count != decisions.Count)
                return;

            Dictionary<string, List<int>> groups =
                new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < candidates.Count; i++)
            {
                string signature = Normalize(candidates[i]?.Signature);
                if (string.IsNullOrWhiteSpace(signature))
                    continue;

                if (!groups.TryGetValue(signature, out List<int> indices))
                {
                    indices = new List<int>();
                    groups[signature] = indices;
                }

                indices.Add(i);
            }

            foreach (KeyValuePair<string, List<int>> pair in groups)
            {
                List<int> indices = pair.Value;
                if (indices == null || indices.Count <= 1)
                    continue;

                int distinctLabels =
                    indices
                        .Select(i => NormalizeLabel(candidates[i]?.Label))
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count();

                // Cùng signature nhưng AI ra nhiều label khác nhau là conflict thật,
                // không suppress để user thấy hết vấn đề.
                if (distinctLabels > 1)
                {
                    foreach (int i in indices)
                    {
                        decisions[i].PriorityScore = Math.Min(100.0, decisions[i].PriorityScore + 12.0);
                        decisions[i].PriorityBand = decisions[i].PriorityScore >= 75.0 ? "P1" : decisions[i].PriorityBand;
                        decisions[i].IsHighPriority = decisions[i].PriorityScore >= 75.0;
                        decisions[i].Reason = AppendReason(decisions[i].Reason, "cùng mẫu nhưng khác nhãn");
                    }

                    continue;
                }

                int representative =
                    indices
                        .OrderByDescending(i => decisions[i].PriorityScore)
                        .ThenBy(i => candidates[i]?.CandidateKey, StringComparer.OrdinalIgnoreCase)
                        .First();

                foreach (int i in indices)
                {
                    decisions[i].DuplicateCount = indices.Count;

                    if (i == representative)
                    {
                        decisions[i].IsRepresentative = true;
                        decisions[i].Reason = AppendReason(
                            decisions[i].Reason,
                            "đại diện 1/" + indices.Count.ToString(CultureInfo.InvariantCulture));
                        continue;
                    }

                    decisions[i].IsRepresentative = false;
                    decisions[i].PriorityScore = Math.Max(0.0, decisions[i].PriorityScore - 55.0);
                    decisions[i].PriorityBand = "P4";
                    decisions[i].IsHighPriority = false;
                    decisions[i].ReviewRecommended = false;
                    decisions[i].Reason = AppendReason(
                        decisions[i].Reason,
                        "duplicate - ưu tiên instance đại diện");
                }
            }
        }

        private void SaveLatest(
            MepActiveLearningQueueSummary summary)
        {
            try
            {
                Directory.CreateDirectory(BaseFolder);
                File.WriteAllText(
                    LatestQueuePath,
                    JsonSerializer.Serialize(summary, _jsonOptions));
            }
            catch
            {
                // Active Learning log không bao giờ được làm hỏng scan AutoCAD.
            }
        }

        private static string AppendReason(string current, string extra)
        {
            if (string.IsNullOrWhiteSpace(current))
                return extra ?? "";
            if (string.IsNullOrWhiteSpace(extra))
                return current;
            return current + " + " + extra;
        }

        private static bool IsReviewStatus(string status)
        {
            string s = Normalize(status);
            return
                s == "MISSING" ||
                s == "CONTEXT_CONFLICT" ||
                s == "FUSION_REVIEW" ||
                s == "DN_CHECK" ||
                s == "NO_DN";
        }

        private static bool IsProbabilisticSource(string source)
        {
            string s = Normalize(source);
            return
                s.Contains("ONNX") ||
                s.Contains("YOLO") ||
                s.Contains("VISION") ||
                s.Contains("OPENCV") ||
                s.Contains("RASTER") ||
                s.Contains("AI");
        }

        private static bool IsDeterministicSource(string source, string matchMode)
        {
            string s = Normalize(source);
            string m = Normalize(matchMode);

            return
                s.Contains("EXACT") ||
                s.Contains("POINT") ||
                s.Contains("VECTOR") ||
                m == "BLOCK" ||
                m == "GEOMETRY";
        }

        private static bool IsUnknownLabel(string label)
        {
            string s = NormalizeLabel(label);
            return
                string.IsNullOrWhiteSpace(s) ||
                s == "CHƯA NHẬN DIỆN" ||
                s == "UNKNOWN" ||
                s == "UNKN";
        }

        private static string NormalizeLabel(string value)
        {
            return (value ?? "").Trim().ToUpperInvariant();
        }

        private static string Normalize(string value)
        {
            return (value ?? "").Trim().ToUpperInvariant();
        }

        private static double Clamp01(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return 0.0;
            if (value < 0.0)
                return 0.0;
            if (value > 1.0)
                return 1.0;
            return value;
        }
    }
}