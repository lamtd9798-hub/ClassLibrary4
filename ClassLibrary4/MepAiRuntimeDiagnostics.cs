#nullable disable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace ClassLibrary4
{
    internal enum MepAiEngineState
    {
        Available = 0,
        MissingModel = 1,
        LoadError = 2
    }

    internal sealed class MepAiEngineDiagnostic
    {
        public string Engine { get; set; } = "";
        public MepAiEngineState State { get; set; }
        public long Runs { get; set; }
        public string ModelPath { get; set; } = "";
        public string LabelsPath { get; set; } = "";
        public string LastError { get; set; } = "";

        public string StateText => State switch
        {
            MepAiEngineState.Available => "AVAILABLE",
            MepAiEngineState.MissingModel => "MISSING MODEL",
            MepAiEngineState.LoadError => "LOAD ERROR",
            _ => "LOAD ERROR"
        };
    }

    /// <summary>
    /// Small cross-engine diagnostics registry. It does not decide whether the whole AI button
    /// is enabled; deterministic CAD/graph paths stay usable when an optional model is missing.
    /// </summary>
    internal static class MepAiRuntimeDiagnostics
    {
        public const string SymbolModel = "mep_symbol_classifier.onnx";
        public const string SymbolLabels = "mep_symbol_labels.txt";
        public const string YoloModel = "mep_symbol_detector.onnx";
        public const string YoloLabels = "mep_symbol_detector_labels.txt";
        public const string GnnModel = "mep_graph_context.onnx";
        public const string GnnLabels = "mep_graph_dn_labels.txt";

        private sealed class Counter
        {
            public long Runs;
            public string LastError = "";
        }

        private static readonly ConcurrentDictionary<string, Counter> Counters =
            new ConcurrentDictionary<string, Counter>(StringComparer.OrdinalIgnoreCase);

        public static void MarkRun(string engine)
        {
            Counter counter = Counters.GetOrAdd(engine ?? "UNKNOWN", _ => new Counter());
            Interlocked.Increment(ref counter.Runs);
        }

        public static void MarkLoadError(string engine, Exception error)
        {
            Counter counter = Counters.GetOrAdd(engine ?? "UNKNOWN", _ => new Counter());
            counter.LastError = error == null
                ? "Unknown load error"
                : error.GetType().Name + ": " + error.Message;
        }

        public static IReadOnlyList<MepAiEngineDiagnostic> Snapshot(string appDataRoot = null)
        {
            string root = string.IsNullOrWhiteSpace(appDataRoot)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TDL_MEP")
                : appDataRoot;

            return new[]
            {
                Build("ONNX", root, SymbolModel, SymbolLabels),
                Build("YOLO", root, YoloModel, YoloLabels),
                Build("GNN", root, GnnModel, GnnLabels)
            };
        }

        public static string BuildCompactStatus(string appDataRoot = null)
        {
            return string.Join(" | ", Snapshot(appDataRoot).Select(x =>
                x.Engine + ": " + x.StateText + " · RUNS: " + x.Runs));
        }

        private static MepAiEngineDiagnostic Build(
            string engine,
            string root,
            string modelName,
            string labelsName)
        {
            string model = FindRecursively(root, modelName);
            string labels = FindRecursively(root, labelsName);
            Counter counter = Counters.GetOrAdd(engine, _ => new Counter());

            MepAiEngineState state;
            if (!string.IsNullOrWhiteSpace(counter.LastError))
                state = MepAiEngineState.LoadError;
            else if (string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(labels))
                state = MepAiEngineState.MissingModel;
            else
                state = MepAiEngineState.Available;

            return new MepAiEngineDiagnostic
            {
                Engine = engine,
                State = state,
                Runs = Interlocked.Read(ref counter.Runs),
                ModelPath = model ?? "",
                LabelsPath = labels ?? "",
                LastError = counter.LastError ?? ""
            };
        }

        private static string FindRecursively(string root, string fileName)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return "";

            try
            {
                string direct = Path.Combine(root, fileName);
                if (File.Exists(direct))
                    return direct;

                return Directory
                    .EnumerateFiles(root, fileName, SearchOption.AllDirectories)
                    .FirstOrDefault() ?? "";
            }
            catch
            {
                return "";
            }
        }
    }
}
