#nullable disable
using System;
using System.Globalization;

namespace ClassLibrary4
{
    /// <summary>
    /// STEP29D - UNIFIED AI DECISION FUSION + DYNAMIC CONFIDENCE GATE.
    ///
    /// Pure-data engine. Không phụ thuộc AutoCAD/WPF/OpenCV/ONNX.
    /// Mọi nguồn AI/CAD chỉ đưa evidence vào đây; engine trả 1 trong:
    /// AUTO_ACCEPT / KEEP / SWITCH / REVIEW.
    ///
    /// Nguyên tắc an toàn:
    /// - Exact CAD / vector deterministic không bị AI xác suất ghi đè.
    /// - Không SWITCH khi class mới thay đổi semantics THEO_ONG.
    /// - Context/Pattern chỉ là evidence bổ sung, không tự quyết định một mình.
    /// - Threshold có thể tăng/giảm rất nhẹ theo lịch sử user override.
    /// </summary>
    internal static class MepAiDecisionFusionEngine
    {
        internal sealed class GateHistory
        {
            public int ReviewedCount { get; set; }
            public int OverrideCount { get; set; }
            public double OverrideRate { get; set; }
            public double ThresholdAdjustment { get; set; }
            public string Reason { get; set; } = "";
        }

        internal sealed class Input
        {
            public string Source { get; set; } = "";
            public string ExistingStatus { get; set; } = "";

            public string CurrentLabel { get; set; } = "";
            public string AlternativeLabel { get; set; } = "";

            public double CurrentVisualConfidence { get; set; }
            public double AlternativeVisualConfidence { get; set; }

            public bool HasLegend { get; set; }
            public bool CurrentInLegend { get; set; }
            public bool AlternativeInLegend { get; set; }

            // STEP28B.2 evidence KHÔNG gồm visual/legend để tránh double-count.
            public double CurrentContextContribution { get; set; }
            public double AlternativeContextContribution { get; set; }
            public string ContextDecisionCode { get; set; } = "";

            public string PatternSuggestedLabel { get; set; } = "";
            public double PatternConfidence { get; set; }
            public int PatternSupportCount { get; set; }

            public bool IsDeterministicCad { get; set; }
            public bool IsMissingOrUnknown { get; set; }
            public bool ExistingContextConflict { get; set; }
            public bool ExistingNonSymbolReview { get; set; }

            // false nếu alternative có SizeRule THEO_ONG khác current.
            public bool AllowAlternativeSwitch { get; set; } = true;

            public GateHistory History { get; set; } = new GateHistory();
        }

        internal sealed class Decision
        {
            public bool Evaluated { get; set; }
            public string DecisionCode { get; set; } = "REVIEW";
            public string SelectedLabel { get; set; } = "";
            public string AlternativeLabel { get; set; } = "";

            public double SelectedScore { get; set; }
            public double AlternativeScore { get; set; }
            public double ScoreMargin { get; set; }
            public double GateThreshold { get; set; }
            public double RequiredMargin { get; set; }

            public int SelectedSupportCount { get; set; }
            public int AlternativeSupportCount { get; set; }
            public bool Switched { get; set; }
            public bool AutoAccepted { get; set; }
            public string Reason { get; set; } = "";
        }

        public static Decision Evaluate(Input input)
        {
            Decision result = new Decision();

            if (input == null)
                return result;

            string current = Normalize(input.CurrentLabel);
            string alternative = Normalize(input.AlternativeLabel);

            result.Evaluated = true;
            result.SelectedLabel = current;
            result.AlternativeLabel = alternative;

            if (input.IsMissingOrUnknown || string.IsNullOrWhiteSpace(current))
            {
                result.DecisionCode = "REVIEW";
                result.Reason = "Chưa có nhãn đủ tin cậy; giữ trong hàng đợi kiểm tra.";
                return result;
            }

            // Exact CAD / Vector fingerprint là deterministic evidence.
            if (input.IsDeterministicCad)
            {
                result.DecisionCode = input.ExistingNonSymbolReview ? "KEEP" : "AUTO_ACCEPT";
                result.AutoAccepted = !input.ExistingNonSymbolReview;
                result.SelectedScore = Math.Max(0.995, Clamp01(input.CurrentVisualConfidence));
                result.GateThreshold = 0.0;
                result.RequiredMargin = 0.0;
                result.ScoreMargin = 1.0;
                result.SelectedSupportCount = 4;
                result.Reason = input.ExistingNonSymbolReview
                    ? "Nhãn được CAD/vector xác nhận; chỉ còn review DN/kích thước."
                    : "Exact CAD/vector deterministic được ưu tiên cao nhất.";
                return result;
            }

            double threshold = BuildBaseThreshold(input.Source);
            double requiredMargin = BuildRequiredMargin(input.Source);

            GateHistory history = input.History ?? new GateHistory();
            threshold += Clamp(history.ThresholdAdjustment, -0.015, 0.080);

            if (input.ExistingContextConflict)
            {
                // Conflict cũ chỉ được tự resolve khi evidence mới thật sự mạnh.
                threshold += 0.020;
                requiredMargin += 0.015;
            }

            threshold = Clamp(threshold, 0.82, 0.98);
            requiredMargin = Clamp(requiredMargin, 0.05, 0.16);

            double currentScore = BuildCandidateScore(
                current,
                Clamp01(input.CurrentVisualConfidence),
                input.HasLegend,
                input.CurrentInLegend,
                input.CurrentContextContribution,
                input.PatternSuggestedLabel,
                input.PatternConfidence,
                input.PatternSupportCount);

            double alternativeScore = string.IsNullOrWhiteSpace(alternative)
                ? 0.0
                : BuildCandidateScore(
                    alternative,
                    Clamp01(input.AlternativeVisualConfidence),
                    input.HasLegend,
                    input.AlternativeInLegend,
                    input.AlternativeContextContribution,
                    input.PatternSuggestedLabel,
                    input.PatternConfidence,
                    input.PatternSupportCount);

            int currentSupport = CountSupports(
                true,
                input,
                current,
                alternative);

            int alternativeSupport = string.IsNullOrWhiteSpace(alternative)
                ? 0
                : CountSupports(
                    false,
                    input,
                    alternative,
                    current);

            result.SelectedScore = currentScore;
            result.AlternativeScore = alternativeScore;
            result.ScoreMargin = Math.Abs(currentScore - alternativeScore);
            result.GateThreshold = threshold;
            result.RequiredMargin = requiredMargin;
            result.SelectedSupportCount = currentSupport;
            result.AlternativeSupportCount = alternativeSupport;

            bool alternativeWins =
                !string.IsNullOrWhiteSpace(alternative) &&
                alternativeScore > currentScore;

            if (alternativeWins)
            {
                double delta = alternativeScore - currentScore;

                bool canSwitch =
                    input.AllowAlternativeSwitch &&
                    alternativeScore >= threshold + 0.020 &&
                    delta >= Math.Max(0.060, requiredMargin) &&
                    alternativeSupport >= 2;

                if (canSwitch)
                {
                    result.DecisionCode = "SWITCH";
                    result.SelectedLabel = alternative;
                    result.AlternativeLabel = current;
                    result.SelectedScore = alternativeScore;
                    result.AlternativeScore = currentScore;
                    result.ScoreMargin = delta;
                    result.SelectedSupportCount = alternativeSupport;
                    result.AlternativeSupportCount = currentSupport;
                    result.Switched = true;
                    result.AutoAccepted = true;
                    result.Reason = BuildReason(
                        "Fusion đổi nhãn vì alternative vượt gate",
                        result,
                        history);
                    return result;
                }

                result.DecisionCode = "REVIEW";
                result.Reason = BuildReason(
                    input.AllowAlternativeSwitch
                        ? "Alternative đang mạnh hơn nhưng chưa đủ điều kiện SWITCH"
                        : "Alternative mạnh hơn nhưng khác semantics THEO_ONG nên không tự SWITCH",
                    result,
                    history);
                return result;
            }

            double currentDelta = currentScore - alternativeScore;
            bool hasAlternative = !string.IsNullOrWhiteSpace(alternative);

            bool strongCurrent =
                currentScore >= threshold &&
                (!hasAlternative || currentDelta >= requiredMargin);

            if (input.ExistingContextConflict)
            {
                strongCurrent =
                    strongCurrent &&
                    currentSupport >= 2 &&
                    currentScore >= threshold + 0.020;
            }

            if (strongCurrent)
            {
                result.DecisionCode = input.ExistingNonSymbolReview ? "KEEP" : "AUTO_ACCEPT";
                result.AutoAccepted = !input.ExistingNonSymbolReview;
                result.Reason = BuildReason(
                    input.ExistingNonSymbolReview
                        ? "Nhãn qua Fusion; giữ review DN/kích thước hiện có"
                        : "Fusion vượt dynamic gate",
                    result,
                    history);
                return result;
            }

            // Nếu user đã phải review DN/NO_DN thì không tạo thêm status symbol khác;
            // giữ nhãn nhưng vẫn để review hiện tại làm hàng rào an toàn.
            if (input.ExistingNonSymbolReview &&
                currentScore >= threshold - 0.050)
            {
                result.DecisionCode = "KEEP";
                result.Reason = BuildReason(
                    "Giữ nhãn hiện tại; review DN/kích thước vẫn còn hiệu lực",
                    result,
                    history);
                return result;
            }

            result.DecisionCode = "REVIEW";
            result.Reason = BuildReason(
                "Fusion chưa vượt dynamic gate",
                result,
                history);
            return result;
        }

        private static double BuildCandidateScore(
            string label,
            double visualConfidence,
            bool hasLegend,
            bool inLegend,
            double contextContribution,
            string patternSuggestedLabel,
            double patternConfidence,
            int patternSupportCount)
        {
            // Visual confidence giữ nguyên thang 0..1 để dynamic gate có ý nghĩa
            // trực tiếp. Context/pattern chỉ boost/penalty nhỏ quanh confidence gốc.
            double score = visualConfidence;

            if (hasLegend)
            {
                score += inLegend ? 0.055 : -0.010;
            }

            // STEP28B contribution đã có scale nhỏ; clamp để không một context
            // bất thường nào có thể áp đảo visual.
            score += Clamp(contextContribution, -0.060, 0.160);

            if (!string.IsNullOrWhiteSpace(patternSuggestedLabel) &&
                string.Equals(
                    Normalize(patternSuggestedLabel),
                    Normalize(label),
                    StringComparison.OrdinalIgnoreCase))
            {
                double pattern = Clamp01(patternConfidence);
                double supportBonus = Math.Min(0.025, Math.Max(0, patternSupportCount) * 0.005);
                score += pattern * 0.060 + supportBonus;
            }

            return Clamp01(score);
        }

        private static int CountSupports(
            bool candidateIsCurrent,
            Input input,
            string candidateLabel,
            string otherLabel)
        {
            int count = 0;

            double candidateVisual = candidateIsCurrent
                ? Clamp01(input.CurrentVisualConfidence)
                : Clamp01(input.AlternativeVisualConfidence);

            double otherVisual = candidateIsCurrent
                ? Clamp01(input.AlternativeVisualConfidence)
                : Clamp01(input.CurrentVisualConfidence);

            if (candidateVisual >= otherVisual + 0.10)
                count++;

            bool candidateLegend = candidateIsCurrent
                ? input.CurrentInLegend
                : input.AlternativeInLegend;

            bool otherLegend = candidateIsCurrent
                ? input.AlternativeInLegend
                : input.CurrentInLegend;

            if (input.HasLegend && candidateLegend && !otherLegend)
                count++;

            double candidateContext = candidateIsCurrent
                ? input.CurrentContextContribution
                : input.AlternativeContextContribution;

            double otherContext = candidateIsCurrent
                ? input.AlternativeContextContribution
                : input.CurrentContextContribution;

            if (candidateContext >= otherContext + 0.025)
                count++;

            if (!string.IsNullOrWhiteSpace(input.PatternSuggestedLabel) &&
                string.Equals(
                    Normalize(input.PatternSuggestedLabel),
                    Normalize(candidateLabel),
                    StringComparison.OrdinalIgnoreCase) &&
                Clamp01(input.PatternConfidence) >= 0.75 &&
                input.PatternSupportCount >= 2)
            {
                count++;
            }

            return count;
        }

        private static double BuildBaseThreshold(string source)
        {
            string value = (source ?? "").ToUpperInvariant();

            if (value.Contains("YOLO"))
                return 0.87;

            if (value.Contains("ONNX"))
                return 0.90;

            if (value.Contains("VISION"))
                return 0.93;

            if (value.Contains("PATTERN"))
                return 0.90;

            return 0.91;
        }

        private static double BuildRequiredMargin(string source)
        {
            string value = (source ?? "").ToUpperInvariant();

            if (value.Contains("YOLO"))
                return 0.065;

            if (value.Contains("ONNX"))
                return 0.075;

            if (value.Contains("VISION"))
                return 0.100;

            return 0.080;
        }

        private static string BuildReason(
            string prefix,
            Decision decision,
            GateHistory history)
        {
            string historyText = "";

            if (history != null && history.ReviewedCount > 0)
            {
                historyText =
                    " | history review=" +
                    history.ReviewedCount.ToString(CultureInfo.InvariantCulture) +
                    ", override=" +
                    (history.OverrideRate * 100.0).ToString("0.0", CultureInfo.InvariantCulture) +
                    "%";
            }

            return
                prefix +
                " | score=" + decision.SelectedScore.ToString("0.000", CultureInfo.InvariantCulture) +
                " vs " + decision.AlternativeScore.ToString("0.000", CultureInfo.InvariantCulture) +
                " | gate=" + decision.GateThreshold.ToString("0.000", CultureInfo.InvariantCulture) +
                " | margin=" + decision.ScoreMargin.ToString("0.000", CultureInfo.InvariantCulture) +
                historyText +
                ".";
        }

        private static string Normalize(string value)
        {
            return (value ?? "").Trim();
        }

        private static double Clamp01(double value)
        {
            return Clamp(value, 0.0, 1.0);
        }

        private static double Clamp(double value, double min, double max)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return min;

            if (value < min)
                return min;

            if (value > max)
                return max;

            return value;
        }
    }
}
