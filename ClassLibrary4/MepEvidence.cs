#nullable disable
using System;

namespace ClassLibrary4
{
    public enum MepDecisionKind
    {
        NotApplicable = 0,
        AutoAccept = 1,
        Review = 2,
        Reject = 3
    }

    /// <summary>
    /// One immutable-style envelope for all evidence collected during one MEP scan.
    /// Label evidence and DN evidence intentionally stay independent.
    /// </summary>
    public sealed class MepEvidence
    {
        // Label evidence.
        public string OnnxLabel { get; set; } = "";
        public double OnnxConfidence { get; set; }
        public string YoloLabel { get; set; } = "";
        public double YoloConfidence { get; set; }
        public string CadLabel { get; set; } = "";
        public double CadConfidence { get; set; }
        public bool CadDeterministic { get; set; }
        public string HvacLabel { get; set; } = "";
        public double HvacConfidence { get; set; }
        public string MemoryLabel { get; set; } = "";
        public double MemoryConfidence { get; set; }
        public string PrototypeLabel { get; set; } = "";
        public double PrototypeConfidence { get; set; }

        // DN evidence. GNN is deliberately DN-only.
        public string GeometryDn { get; set; } = "";
        public double GeometryDnConfidence { get; set; }
        public string GraphDn { get; set; } = "";
        public double GraphDnConfidence { get; set; }
        public string GnnDn { get; set; } = "";
        public double GnnDnConfidence { get; set; }
        public string TextDn { get; set; } = "";
        public double TextDnConfidence { get; set; }

        // Context used by policies/taxonomy and audit.
        public string Layer { get; set; } = "";
        public string BlockName { get; set; } = "";
        public string TextHint { get; set; } = "";
        public string EntityType { get; set; } = "";
        public string DrawingKey { get; set; } = "";
    }

    public sealed class MepDecision
    {
        public string Label { get; set; } = "";
        public string Dn { get; set; } = "";

        public MepDecisionKind LabelKind { get; set; } = MepDecisionKind.NotApplicable;
        public MepDecisionKind DnKind { get; set; } = MepDecisionKind.NotApplicable;
        public MepDecisionKind OverallKind { get; set; } = MepDecisionKind.NotApplicable;

        public double LabelConfidence { get; set; }
        public double DnConfidence { get; set; }

        public string LabelReason { get; set; } = "";
        public string DnReason { get; set; } = "";
    }
}
