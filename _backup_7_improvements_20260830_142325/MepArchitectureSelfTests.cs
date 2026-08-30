#nullable disable
using System;

namespace ClassLibrary4
{
    /// <summary>
    /// Pure-data regression checks that can run without an AutoCAD document.
    /// Intended for CI/debug command wiring without depending on CAD DB transactions.
    /// </summary>
    internal static class MepArchitectureSelfTests
    {
        public static void RunAll()
        {
            TestLabelAcceptedWhileDnNeedsReview();
            TestDnAgreementAutoAccepts();
            TestCanonicalLabelMapping();
            TestDuctParserDoesNotTreatFireDnAsDuct();
            TestSnapshotFingerprintStable();
        }

        private static void TestLabelAcceptedWhileDnNeedsReview()
        {
            var service = new MepDecisionService();
            MepDecision result = service.Decide(new MepEvidence
            {
                CadLabel = "SPRINKLER",
                CadConfidence = 0.99,
                CadDeterministic = true,
                GeometryDn = "DN50",
                GeometryDnConfidence = 0.61,
                GnnDn = "DN65",
                GnnDnConfidence = 0.60
            });

            Require(result.LabelKind == MepDecisionKind.AutoAccept, "Label must auto-accept.");
            Require(result.DnKind == MepDecisionKind.Review, "Conflicting DN must stay Review.");
            Require(result.OverallKind == MepDecisionKind.Review, "Overall must expose DN review.");
        }

        private static void TestDnAgreementAutoAccepts()
        {
            var service = new MepDecisionService();
            MepDecision result = service.Decide(new MepEvidence
            {
                TextDn = "DN50",
                TextDnConfidence = 0.92,
                GraphDn = "50",
                GraphDnConfidence = 0.84,
                GnnDn = "DN50",
                GnnDnConfidence = 0.81
            });

            Require(result.Dn == "DN50", "DN normalization failed.");
            Require(result.DnKind == MepDecisionKind.AutoAccept, "Agreeing DN evidence must auto-accept.");
        }

        private static void TestCanonicalLabelMapping()
        {
            string value = MepCanonicalLabelMap.Canonicalize("YOLO", "fire_damper");
            Require(string.Equals(value, "FD", StringComparison.Ordinal), "Canonical label mapping failed.");
        }

        private static void TestDuctParserDoesNotTreatFireDnAsDuct()
        {
            bool parsed = MepDuctSizeParser.TryParse("DN100 PCCC", "FF_PIPE", out _);
            Require(!parsed, "PCCC DN text must not be parsed as duct size.");
        }

        private static void TestSnapshotFingerprintStable()
        {
            string a = MepScanSnapshot.BuildStableFingerprint(new[] { "B", "A", "C" });
            string b = MepScanSnapshot.BuildStableFingerprint(new[] { "C", "B", "A" });
            Require(string.Equals(a, b, StringComparison.Ordinal), "Snapshot fingerprint must be order-independent.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException("MEP architecture self-test failed: " + message);
        }
    }
}
