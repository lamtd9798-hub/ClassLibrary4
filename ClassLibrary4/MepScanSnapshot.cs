#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ClassLibrary4
{
    internal enum MepGraphDomain
    {
        Unknown = 0,
        Pipe = 1,
        Duct = 2
    }

    // Snapshot DTO names are intentionally different from the existing
    // MepGraphPipeNode / MepGraphDeviceNode classes in MepGraphEngine.cs.
    internal sealed class MepSnapshotPipeNode
    {
        public string Key { get; set; } = "";
        public string Dn { get; set; } = "";
        public double Confidence { get; set; }
        public string SourceHandle { get; set; } = "";
    }

    internal sealed class MepGraphDuctNode
    {
        public string Key { get; set; } = "";
        public string Size { get; set; } = "";
        public string SystemCode { get; set; } = "";
        public string FireRating { get; set; } = "";
        public double Confidence { get; set; }
        public string SourceHandle { get; set; } = "";
    }

    internal sealed class MepSnapshotDeviceNode
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string Dn { get; set; } = "";
        public MepDecisionKind LabelKind { get; set; }
        public MepDecisionKind DnKind { get; set; }
        public string SourceHandle { get; set; } = "";
    }

    internal sealed class MepGraphDomainEdge
    {
        public string FromKey { get; set; } = "";
        public string ToKey { get; set; } = "";
        public MepGraphDomain Domain { get; set; }
        public double Confidence { get; set; }
    }

    internal sealed class MepTypedGraphSnapshot
    {
        // Existing deterministic pipe graph remains authoritative for pipe/GNN.
        public MepGraphSnapshot PipeGraph { get; set; }
        public List<MepSnapshotPipeNode> Pipes { get; set; } = new List<MepSnapshotPipeNode>();
        public List<MepGraphDuctNode> Ducts { get; set; } = new List<MepGraphDuctNode>();
        public List<MepSnapshotDeviceNode> Devices { get; set; } = new List<MepSnapshotDeviceNode>();
        public List<MepGraphDomainEdge> Edges { get; set; } = new List<MepGraphDomainEdge>();
    }

    internal sealed class MepFusedDeviceSnapshot
    {
        public string CandidateKey { get; set; } = "";
        public string SourceHandle { get; set; } = "";
        public string Label { get; set; } = "";
        public string Dn { get; set; } = "";
        public MepDecisionKind LabelKind { get; set; }
        public MepDecisionKind DnKind { get; set; }
        public MepDecisionKind OverallKind { get; set; }
        public double LabelConfidence { get; set; }
        public double DnConfidence { get; set; }
        public string LabelReason { get; set; } = "";
        public string DnReason { get; set; } = "";
    }

    internal sealed class MepAreaCandidateSnapshot
    {
        public string SourceHandle { get; set; } = "";
        public string Layer { get; set; } = "";
        public string EntityType { get; set; } = "";
        public double AreaM2 { get; set; }
        public bool IsSubtraction { get; set; }
    }

    internal sealed class MepDrawingTextSnapshot
    {
        public string SourceHandle { get; set; } = "";
        public string Text { get; set; } = "";
        public string Layer { get; set; } = "";
        public string SourceType { get; set; } = "";
    }

    /// <summary>
    /// Session snapshot produced from ONE selected CAD region. Fire/HVAC/graph consumers
    /// use this object instead of re-selecting/re-reading the same drawing content.
    /// </summary>
    internal sealed class MepScanSnapshot
    {
        public MepTypedGraphSnapshot Graph { get; set; } = new MepTypedGraphSnapshot();
        public List<MepFusedDeviceSnapshot> FusedDevices { get; set; } = new List<MepFusedDeviceSnapshot>();
        public List<MepAreaCandidateSnapshot> AreaCandidates { get; set; } = new List<MepAreaCandidateSnapshot>();
        public List<MepDrawingTextSnapshot> DrawingTexts { get; set; } = new List<MepDrawingTextSnapshot>();
        public List<string> SystemHints { get; set; } = new List<string>();
        public List<string> SelectedHandles { get; set; } = new List<string>();
        public string DrawingKey { get; set; } = "";
        public string SelectionFingerprint { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public bool IsStale(string drawingKey, string currentSelectionFingerprint)
        {
            if (!string.Equals(DrawingKey ?? "", drawingKey ?? "", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.IsNullOrWhiteSpace(SelectionFingerprint) ||
                string.IsNullOrWhiteSpace(currentSelectionFingerprint))
                return true;

            return !string.Equals(
                SelectionFingerprint,
                currentSelectionFingerprint,
                StringComparison.Ordinal);
        }

        public static string BuildStableFingerprint(IEnumerable<string> tokens)
        {
            // FNV-1a 64 bit: deterministic, cheap, no crypto dependency. Callers should
            // include handle + entity type + layer + geometry/text signature for each selected entity.
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                foreach (string token in (tokens ?? Enumerable.Empty<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .OrderBy(x => x, StringComparer.Ordinal))
                {
                    foreach (char c in token)
                    {
                        hash ^= c;
                        hash *= 1099511628211UL;
                    }
                    hash ^= 0xFF;
                    hash *= 1099511628211UL;
                }
                return hash.ToString("X16");
            }
        }
    }
}
