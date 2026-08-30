#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ClassLibrary4
{
    /// <summary>
    /// Single decision entry point. Label and DN are fused independently and only
    /// combined at OverallKind, preventing an accepted device label from hiding a DN review.
    /// </summary>
    public sealed class MepDecisionService
    {
        public MepDecision Decide(MepEvidence evidence)
        {
            evidence ??= new MepEvidence();

            MepDecision result = new MepDecision();
            DecideLabel(evidence, result);
            DecideDn(evidence, result);
            result.OverallKind = Combine(result.LabelKind, result.DnKind);
            return result;
        }

        private static void DecideLabel(MepEvidence evidence, MepDecision result)
        {
            if (evidence.CadDeterministic &&
                !string.IsNullOrWhiteSpace(evidence.CadLabel) &&
                evidence.CadConfidence >= 0.80)
            {
                result.Label = Canonical("CAD", evidence.CadLabel);
                result.LabelConfidence = Math.Max(0.80, evidence.CadConfidence);
                result.LabelKind = MepDecisionKind.AutoAccept;
                result.LabelReason = "CAD deterministic";
                return;
            }

            var candidates = new List<(string Label, double Confidence, string Source, double Weight)>
            {
                (Canonical("ONNX", evidence.OnnxLabel), evidence.OnnxConfidence, "ONNX", 1.00),
                (Canonical("YOLO", evidence.YoloLabel), evidence.YoloConfidence, "YOLO", 1.05),
                (Canonical("HVAC", evidence.HvacLabel), evidence.HvacConfidence, "HVAC", 1.10),
                (Canonical("MEMORY", evidence.MemoryLabel), evidence.MemoryConfidence, "MEMORY", 1.08),
                (Canonical("PROTOTYPE", evidence.PrototypeLabel), evidence.PrototypeConfidence, "PROTOTYPE", 0.95),
                (Canonical("CAD", evidence.CadLabel), evidence.CadConfidence, "CAD", 1.10)
            }
            .Where(x => !string.IsNullOrWhiteSpace(x.Label) && x.Confidence > 0.0)
            .Select(x => (x.Label, Clamp01(x.Confidence), x.Source, x.Weight))
            .ToList();

            if (candidates.Count == 0)
            {
                result.LabelKind = MepDecisionKind.Review;
                result.LabelReason = "No label evidence";
                return;
            }

            var ranked = candidates
                .GroupBy(x => x.Item1, StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    Label = g.Key,
                    Score = g.Sum(x => x.Item2 * x.Item4),
                    MaxConfidence = g.Max(x => x.Item2),
                    Count = g.Count(),
                    Sources = string.Join("+", g.Select(x => x.Item3).Distinct(StringComparer.OrdinalIgnoreCase))
                })
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.MaxConfidence)
                .ToList();

            var best = ranked[0];
            var second = ranked.Count > 1 ? ranked[1] : null;
            double margin = second == null ? best.Score : best.Score - second.Score;

            result.Label = best.Label;
            result.LabelConfidence = Clamp01(best.MaxConfidence);
            result.LabelReason = best.Sources + " score=" + best.Score.ToString("0.000");

            bool safeAccept =
                (best.MaxConfidence >= 0.92 && margin >= 0.12) ||
                (best.Count >= 2 && best.MaxConfidence >= 0.78 && margin >= 0.08);

            result.LabelKind = safeAccept
                ? MepDecisionKind.AutoAccept
                : MepDecisionKind.Review;
        }

        private static void DecideDn(MepEvidence evidence, MepDecision result)
        {
            var candidates = new List<(string Dn, double Confidence, string Source, double Weight)>
            {
                (evidence.TextDn, evidence.TextDnConfidence, "TEXT", 1.15),
                (evidence.GraphDn, evidence.GraphDnConfidence, "GRAPH", 1.10),
                (evidence.GnnDn, evidence.GnnDnConfidence, "GNN", 1.00),
                (evidence.GeometryDn, evidence.GeometryDnConfidence, "GEOMETRY", 0.95)
            }
            .Where(x => !string.IsNullOrWhiteSpace(x.Dn) && x.Confidence > 0.0)
            .Select(x => (NormalizeDn(x.Dn), Clamp01(x.Confidence), x.Source, x.Weight))
            .ToList();

            if (candidates.Count == 0)
            {
                result.DnKind = MepDecisionKind.NotApplicable;
                result.DnReason = "No DN evidence";
                return;
            }

            var ranked = candidates
                .GroupBy(x => x.Item1, StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    Dn = g.Key,
                    Score = g.Sum(x => x.Item2 * x.Item4),
                    MaxConfidence = g.Max(x => x.Item2),
                    Count = g.Count(),
                    Sources = string.Join("+", g.Select(x => x.Item3).Distinct(StringComparer.OrdinalIgnoreCase))
                })
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.MaxConfidence)
                .ToList();

            var best = ranked[0];
            var second = ranked.Count > 1 ? ranked[1] : null;
            double margin = second == null ? best.Score : best.Score - second.Score;

            result.Dn = best.Dn;
            result.DnConfidence = Clamp01(best.MaxConfidence);
            result.DnReason = best.Sources + " score=" + best.Score.ToString("0.000");

            bool accepted =
                (best.Count >= 2 && best.MaxConfidence >= 0.72 && margin >= 0.08) ||
                (best.MaxConfidence >= 0.94 && margin >= 0.12);

            result.DnKind = accepted
                ? MepDecisionKind.AutoAccept
                : MepDecisionKind.Review;
        }

        private static MepDecisionKind Combine(MepDecisionKind labelKind, MepDecisionKind dnKind)
        {
            if (labelKind == MepDecisionKind.Reject || dnKind == MepDecisionKind.Reject)
                return MepDecisionKind.Reject;
            if (labelKind == MepDecisionKind.Review || dnKind == MepDecisionKind.Review)
                return MepDecisionKind.Review;
            if (labelKind == MepDecisionKind.AutoAccept || dnKind == MepDecisionKind.AutoAccept)
                return MepDecisionKind.AutoAccept;
            return MepDecisionKind.NotApplicable;
        }

        private static string Canonical(string engine, string value) =>
            MepCanonicalLabelMap.Canonicalize(engine, value);

        private static string NormalizeDn(string value)
        {
            string s = (value ?? "").Trim().ToUpperInvariant().Replace(" ", "");
            if (s.Length > 0 && char.IsDigit(s[0]))
                s = "DN" + s;
            return s;
        }

        private static double Clamp01(double value) => Math.Max(0.0, Math.Min(1.0, value));
    }
}
