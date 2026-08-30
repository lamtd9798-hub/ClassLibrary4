#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace ClassLibrary4
{
    /// <summary>
    /// Runtime glue for the unified MEP architecture.
    /// Keeps the current CAD selection as a reusable scan snapshot so PCCC/HVAC
    /// can consume one scan instead of asking for the same drawing region again.
    /// </summary>
    internal static class MepScanSessionStore
    {
        private static readonly object Gate = new object();
        private static ObjectId[] _selectedIds = Array.Empty<ObjectId>();
        private static MepScanSnapshot _snapshot;

        public static MepScanSnapshot Current
        {
            get
            {
                lock (Gate)
                    return _snapshot;
            }
        }

        public static void CaptureSelection(
            Document document,
            IEnumerable<ObjectId> objectIds,
            IEnumerable<string> systemHints = null)
        {
            if (document == null || objectIds == null)
                return;

            ObjectId[] ids = objectIds
                .Where(x => !x.IsNull && x.IsValid && !x.IsErased)
                .Distinct()
                .ToArray();

            if (ids.Length == 0)
                return;

            List<string> handles = ids
                .Select(x =>
                {
                    try { return x.Handle.ToString(); }
                    catch { return ""; }
                })
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            string drawingKey = GetDrawingKey(document);

            var snapshot = new MepScanSnapshot
            {
                DrawingKey = drawingKey,
                SelectedHandles = handles,
                SelectionFingerprint = MepScanSnapshot.BuildStableFingerprint(handles),
                CreatedAt = DateTime.UtcNow,
                SystemHints = (systemHints ?? Array.Empty<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };

            lock (Gate)
            {
                _selectedIds = ids;
                _snapshot = snapshot;
            }
        }

        public static void AttachPipeGraph(MepGraphSnapshot graph)
        {
            if (graph == null)
                return;

            lock (Gate)
            {
                if (_snapshot == null)
                    return;

                if (_snapshot.Graph == null)
                    _snapshot.Graph = new MepTypedGraphSnapshot();

                _snapshot.Graph.PipeGraph = graph;
            }
        }

        public static bool TryGetFreshSelection(
            Document document,
            out SelectionSet selection,
            int maxAgeMinutes = 30)
        {
            selection = null;

            if (document == null)
                return false;

            ObjectId[] ids;
            MepScanSnapshot snapshot;

            lock (Gate)
            {
                ids = _selectedIds?.ToArray() ?? Array.Empty<ObjectId>();
                snapshot = _snapshot;
            }

            if (snapshot == null || ids.Length == 0)
                return false;

            string drawingKey = GetDrawingKey(document);

            if (!string.Equals(
                    snapshot.DrawingKey ?? "",
                    drawingKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (maxAgeMinutes > 0 &&
                DateTime.UtcNow - snapshot.CreatedAt > TimeSpan.FromMinutes(maxAgeMinutes))
            {
                return false;
            }

            ObjectId[] valid = ids
                .Where(x => !x.IsNull && x.IsValid && !x.IsErased)
                .ToArray();

            if (valid.Length == 0)
                return false;

            try
            {
                selection = SelectionSet.FromObjectIds(valid);
                return selection != null && selection.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsCurrentSnapshotStale(Document document)
        {
            MepScanSnapshot snapshot = Current;
            if (snapshot == null || document == null)
                return true;

            return !string.Equals(
                snapshot.DrawingKey ?? "",
                GetDrawingKey(document),
                StringComparison.OrdinalIgnoreCase);
        }

        public static void Clear()
        {
            lock (Gate)
            {
                _selectedIds = Array.Empty<ObjectId>();
                _snapshot = null;
            }
        }

        private static string GetDrawingKey(Document document)
        {
            if (document == null)
                return "";

            try
            {
                string file = document.Database?.Filename ?? "";
                if (!string.IsNullOrWhiteSpace(file))
                    return file.Trim();
            }
            catch { }

            try
            {
                return (document.Name ?? "").Trim();
            }
            catch
            {
                return "";
            }
        }
    }

    /// <summary>
    /// One public-internal decision entry for new adapters.
    /// Label and DN remain independent inside MepDecisionService.
    /// </summary>
    internal static class MepDecisionRuntime
    {
        private static readonly MepDecisionService Service = new MepDecisionService();

        public static MepDecision Decide(MepEvidence evidence)
        {
            return Service.Decide(evidence ?? new MepEvidence());
        }
    }

    internal static class MepRuntimeIntegrationSelfTests
    {
        public static void RunAll()
        {
            TestDecisionFacadePreservesIndependentDnReview();
            TestDiagnosticsHaveExactPhysicalModelNames();
        }

        private static void TestDecisionFacadePreservesIndependentDnReview()
        {
            MepDecision decision = MepDecisionRuntime.Decide(new MepEvidence
            {
                CadLabel = "SPRINKLER",
                CadConfidence = 0.99,
                CadDeterministic = true,
                GraphDn = "DN50",
                GraphDnConfidence = 0.61,
                GnnDn = "DN65",
                GnnDnConfidence = 0.60
            });

            Require(
                decision.LabelKind == MepDecisionKind.AutoAccept &&
                decision.DnKind == MepDecisionKind.Review &&
                decision.OverallKind == MepDecisionKind.Review,
                "Label auto-accept must not hide DN review.");
        }

        private static void TestDiagnosticsHaveExactPhysicalModelNames()
        {
            Require(
                MepAiRuntimeDiagnostics.SymbolModel == "mep_symbol_classifier.onnx" &&
                MepAiRuntimeDiagnostics.SymbolLabels == "mep_symbol_labels.txt" &&
                MepAiRuntimeDiagnostics.YoloModel == "mep_symbol_detector.onnx" &&
                MepAiRuntimeDiagnostics.YoloLabels == "mep_symbol_detector_labels.txt" &&
                MepAiRuntimeDiagnostics.GnnModel == "mep_graph_context.onnx" &&
                MepAiRuntimeDiagnostics.GnnLabels == "mep_graph_dn_labels.txt",
                "Physical model/label names changed unexpectedly.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(
                    "MEP runtime integration self-test failed: " + message);
        }
    }
}
