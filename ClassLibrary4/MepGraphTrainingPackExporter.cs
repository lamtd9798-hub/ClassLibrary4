#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace ClassLibrary4
{
    public sealed class MepGraphTrainingPackResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string ExportFolder { get; set; } = "";
        public string ZipPath { get; set; } = "";
        public int GraphCount { get; set; }
        public int PipeCount { get; set; }
        public int ExplicitLabelCount { get; set; }
        public int DnClassCount { get; set; }
        public Dictionary<string, int> DnCounts { get; set; } =
            new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// STEP22B2 - gom Graph History local + CloudApproved thành training pack cho GNN.
    /// Không train trong AutoCAD.
    /// </summary>
    internal sealed class MepGraphTrainingPackExporter
    {
        public string GraphRoot
        {
            get
            {
                string appData =
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.ApplicationData);

                if (string.IsNullOrWhiteSpace(
                        appData))
                {
                    appData =
                        Path.GetTempPath();
                }

                return
                    Path.Combine(
                        appData,
                        "TDL_MEP",
                        "Graph");
            }
        }

        public string HistoryFolder =>
            Path.Combine(
                GraphRoot,
                "History");

        public string CloudApprovedFolder =>
            Path.Combine(
                GraphRoot,
                "CloudApproved");

        public string DefaultTrainingRoot
        {
            get
            {
                string documents =
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.MyDocuments);

                if (string.IsNullOrWhiteSpace(
                        documents))
                {
                    documents =
                        Path.GetTempPath();
                }

                return
                    Path.Combine(
                        documents,
                        "TDL_MEP_GNN_TRAINING");
            }
        }

        public MepGraphTrainingPackResult Export(
            string outputRoot = "")
        {
            MepGraphTrainingPackResult result =
                new MepGraphTrainingPackResult();

            try
            {
                Directory.CreateDirectory(
                    HistoryFolder);

                Directory.CreateDirectory(
                    CloudApprovedFolder);

                Dictionary<string, string> graphByKey =
                    new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase);

                AiGraphCloudClient canonical =
                    new AiGraphCloudClient();

                foreach (string file
                    in Directory.GetFiles(
                        HistoryFolder,
                        "*.json",
                        SearchOption.TopDirectoryOnly))
                {
                    string key =
                        canonical.GetCanonicalHashForGraphFile(
                            file);

                    if (string.IsNullOrWhiteSpace(
                            key))
                    {
                        key =
                            Path.GetFileNameWithoutExtension(
                                file);
                    }

                    if (!graphByKey.ContainsKey(
                            key))
                    {
                        graphByKey[key] =
                            file;
                    }
                }

                // Approved Cloud ưu tiên hơn local nếu cùng canonical hash.
                foreach (string file
                    in Directory.GetFiles(
                        CloudApprovedFolder,
                        "*.json",
                        SearchOption.TopDirectoryOnly))
                {
                    string key =
                        canonical.GetCanonicalHashForGraphFile(
                            file);

                    if (string.IsNullOrWhiteSpace(
                            key))
                    {
                        key =
                            Path.GetFileNameWithoutExtension(
                                file);
                    }

                    graphByKey[key] =
                        file;
                }

                List<string> graphFiles =
                    graphByKey.Values
                        .OrderBy(
                            x =>
                                x,
                            StringComparer.OrdinalIgnoreCase)
                        .ToList();

                string lastGraph =
                    Path.Combine(
                        GraphRoot,
                        "last_graph.json");

                if (graphFiles.Count == 0 &&
                    File.Exists(
                        lastGraph))
                {
                    graphFiles.Add(
                        lastGraph);
                }

                if (graphFiles.Count == 0)
                {
                    result.Message =
                        "Chưa có Graph History.\n\n" +
                        "Hãy chạy AI GRAPH / TOPOLOGY trên vài vùng bản vẽ trước.";

                    return result;
                }

                string root =
                    string.IsNullOrWhiteSpace(
                        outputRoot)
                        ? DefaultTrainingRoot
                        : outputRoot;

                Directory.CreateDirectory(
                    root);

                string exportFolder =
                    Path.Combine(
                        root,
                        "TRAIN_GNN_" +
                        DateTime.Now.ToString(
                            "yyyyMMdd_HHmmss",
                            CultureInfo.InvariantCulture));

                string graphsFolder =
                    Path.Combine(
                        exportFolder,
                        "graphs");

                Directory.CreateDirectory(
                    graphsFolder);

                Dictionary<string, int> dnCounts =
                    new Dictionary<string, int>(
                        StringComparer.OrdinalIgnoreCase);

                int totalPipes =
                    0;

                int explicitLabels =
                    0;

                int copiedGraphs =
                    0;

                foreach (string source
                    in graphFiles)
                {
                    string fileName =
                        Path.GetFileName(
                            source);

                    string destination =
                        Path.Combine(
                            graphsFolder,
                            fileName);

                    if (!string.Equals(
                            Path.GetFullPath(source),
                            Path.GetFullPath(destination),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(
                            source,
                            destination,
                            true);
                    }

                    copiedGraphs++;

                    try
                    {
                        using (JsonDocument doc =
                            JsonDocument.Parse(
                                File.ReadAllText(
                                    source,
                                    Encoding.UTF8)))
                        {
                            JsonElement rootElement =
                                doc.RootElement;

                            if (!rootElement.TryGetProperty(
                                    "pipes",
                                    out JsonElement pipes) ||
                                pipes.ValueKind !=
                                    JsonValueKind.Array)
                            {
                                continue;
                            }

                            foreach (JsonElement pipe
                                in pipes.EnumerateArray())
                            {
                                totalPipes++;

                                string dn =
                                    GetString(
                                        pipe,
                                        "dn");

                                string sourceName =
                                    GetString(
                                        pipe,
                                        "dn_source");

                                double confidence =
                                    GetDouble(
                                        pipe,
                                        "dn_confidence");

                                bool targetReliable =
                                    !string.IsNullOrWhiteSpace(
                                        dn) &&
                                    confidence >=
                                        0.85 &&
                                    (string.Equals(
                                         sourceName,
                                         "TEXT",
                                         StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(
                                         sourceName,
                                         "AI_LAYER",
                                         StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(
                                         sourceName,
                                         "LAYER",
                                         StringComparison.OrdinalIgnoreCase));

                                if (!targetReliable)
                                    continue;

                                explicitLabels++;

                                if (!dnCounts.ContainsKey(
                                        dn))
                                {
                                    dnCounts[dn] =
                                        0;
                                }

                                dnCounts[dn]++;
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                object summary =
                    new
                    {
                        version =
                            1,
                        exported_utc =
                            DateTime.UtcNow.ToString(
                                "O",
                                CultureInfo.InvariantCulture),
                        graph_count =
                            copiedGraphs,
                        pipe_count =
                            totalPipes,
                        explicit_target_labels =
                            explicitLabels,
                        dn_class_count =
                            dnCounts.Count,
                        dn_counts =
                            dnCounts
                            .OrderBy(
                                x =>
                                    DnNumber(
                                        x.Key))
                            .ToDictionary(
                                x =>
                                    x.Key,
                                x =>
                                    x.Value)
                    };

                File.WriteAllText(
                    Path.Combine(
                        exportFolder,
                        "gnn_dataset_summary.json"),
                    JsonSerializer.Serialize(
                        summary,
                        new JsonSerializerOptions
                        {
                            WriteIndented =
                                true
                        }),
                    Encoding.UTF8);

                File.WriteAllText(
                    Path.Combine(
                        exportFolder,
                        "README_GNN_TRAINING_PACK.txt"),
                    BuildReadme(
                        copiedGraphs,
                        totalPipes,
                        explicitLabels,
                        dnCounts),
                    Encoding.UTF8);

                string zipPath =
                    exportFolder +
                    ".zip";

                try
                {
                    if (File.Exists(
                            zipPath))
                    {
                        File.Delete(
                            zipPath);
                    }

                    ZipFile.CreateFromDirectory(
                        exportFolder,
                        zipPath,
                        CompressionLevel.Fastest,
                        false);
                }
                catch
                {
                    zipPath =
                        "";
                }

                result.Success =
                    copiedGraphs > 0;

                result.Message =
                    explicitLabels > 0
                        ? "Đã xuất GNN training pack."
                        : "Đã xuất graph nhưng chưa có DN label đáng tin để train.";

                result.ExportFolder =
                    exportFolder;

                result.ZipPath =
                    zipPath;

                result.GraphCount =
                    copiedGraphs;

                result.PipeCount =
                    totalPipes;

                result.ExplicitLabelCount =
                    explicitLabels;

                result.DnClassCount =
                    dnCounts.Count;

                result.DnCounts =
                    dnCounts;

                return result;
            }
            catch (Exception ex)
            {
                result.Message =
                    ex.GetType().Name +
                    ": " +
                    ex.Message;

                return result;
            }
        }

        private static string BuildReadme(
            int graphCount,
            int pipeCount,
            int labels,
            Dictionary<string, int> counts)
        {
            StringBuilder sb =
                new StringBuilder();

            sb.AppendLine(
                "TDL MEP - STEP22B GNN TRAINING PACK");

            sb.AppendLine(
                "===================================");

            sb.AppendLine();

            sb.AppendLine(
                "Graphs: " +
                graphCount);

            sb.AppendLine(
                "Pipes: " +
                pipeCount);

            sb.AppendLine(
                "Reliable target labels: " +
                labels);

            sb.AppendLine(
                "DN classes: " +
                counts.Count);

            sb.AppendLine();

            sb.AppendLine(
                "Target labels chỉ lấy DN có nguồn TEXT / AI_LAYER / LAYER và confidence >= 0.85.");

            sb.AppendLine(
                "GRAPH_NEIGHBOR không được dùng làm ground-truth target để tránh AI tự học lại lỗi của chính nó.");

            sb.AppendLine();

            sb.AppendLine(
                "Copy 3 file STEP22B vào folder này:");

            sb.AppendLine(
                "  train_mep_graph_gnn.py");

            sb.AppendLine(
                "  RUN_TRAIN_GNN_PY312.bat");

            sb.AppendLine(
                "  requirements_STEP22B.txt");

            sb.AppendLine();

            sb.AppendLine(
                "Sau đó chạy RUN_TRAIN_GNN_PY312.bat.");

            sb.AppendLine();

            sb.AppendLine(
                "Khuyến nghị tối thiểu để TEST kỹ thuật:");

            sb.AppendLine(
                "  >= 2 DN classes, >= 10 reliable targets/class.");

            sb.AppendLine(
                "Dùng production nên gom nhiều graph từ nhiều dự án/bản vẽ khác nhau.");

            return
                sb.ToString();
        }

        private static string GetString(
            JsonElement element,
            string name)
        {
            if (!element.TryGetProperty(
                    name,
                    out JsonElement value))
            {
                return "";
            }

            return
                value.ValueKind ==
                    JsonValueKind.String
                    ? value.GetString() ?? ""
                    : value.ToString();
        }

        private static double GetDouble(
            JsonElement element,
            string name)
        {
            if (!element.TryGetProperty(
                    name,
                    out JsonElement value))
            {
                return 0.0;
            }

            if (value.ValueKind ==
                    JsonValueKind.Number &&
                value.TryGetDouble(
                    out double number))
            {
                return number;
            }

            double.TryParse(
                value.ToString(),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out number);

            return number;
        }

        private static int DnNumber(
            string value)
        {
            if (string.IsNullOrWhiteSpace(
                    value))
            {
                return int.MaxValue;
            }

            string digits =
                new string(
                    value
                        .Where(
                            char.IsDigit)
                        .ToArray());

            return
                int.TryParse(
                    digits,
                    out int number)
                    ? number
                    : int.MaxValue;
        }
    }
}