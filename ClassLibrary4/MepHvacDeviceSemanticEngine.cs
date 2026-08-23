#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace ClassLibrary4
{
    public sealed class MepHvacDeviceCandidate
    {
        public ObjectId Id { get; set; } = ObjectId.Null;
        public string Handle { get; set; } = "";
        public string BlockName { get; set; } = "";
        public string Layer { get; set; } = "";
        public Point3d Position { get; set; } = Point3d.Origin;
        public Extents3d? Extents { get; set; }
        public string Label { get; set; } = "";
        public string Group { get; set; } = "";
        public string Size { get; set; } = "";
        public string SystemCode { get; set; } = "";
        public string FireRating { get; set; } = "";
        public double Confidence { get; set; }
        public bool NeedsReview { get; set; }
        public string Evidence { get; set; } = "";
    }

    public sealed class MepHvacDeviceStatRow
    {
        public string Label { get; set; } = "";
        public string Size { get; set; } = "";
        public string SystemCode { get; set; } = "";
        public string FireRating { get; set; } = "";
        public int Quantity { get; set; }
        public double AverageConfidence { get; set; }
    }

    public sealed class MepHvacDeviceScanResult
    {
        public List<MepHvacDeviceCandidate> Candidates { get; set; } =
            new List<MepHvacDeviceCandidate>();

        public List<MepHvacDeviceStatRow> Stats { get; set; } =
            new List<MepHvacDeviceStatRow>();

        public int BlockCount { get; set; }
        public int RecognizedCount =>
            Candidates?.Count ?? 0;

        public int ReviewCount =>
            Candidates?.Count(x =>
                x != null &&
                x.NeedsReview) ?? 0;
    }

    /// <summary>
    /// STEP30B-D2:
    /// Heuristic HVAC semantic fallback cho block có tên/layer/attribute rõ.
    /// Đây KHÔNG thay YOLO/ONNX. Nó bổ sung deterministic evidence:
    /// VCD/FD/FSD/OBD/miệng gió... có tên CAD rõ thì nhận ngay.
    /// </summary>
    public sealed class MepHvacDeviceSemanticEngine
    {
        public MepHvacDeviceScanResult Analyze(
            Document doc,
            IEnumerable<ObjectId> selectedIds,
            MepDuctScanResult ductContext = null)
        {
            MepHvacDeviceScanResult result =
                new MepHvacDeviceScanResult();

            if (doc == null ||
                selectedIds == null)
            {
                return result;
            }

            ObjectId[] ids =
                selectedIds
                    .Where(x =>
                        !x.IsNull &&
                        x.IsValid &&
                        !x.IsErased)
                    .Distinct()
                    .ToArray();

            if (ids.Length == 0)
                return result;

            Database db =
                doc.Database;

            MepDuctSemanticEngine ductEngine =
                new MepDuctSemanticEngine();

            using (DocumentLock docLock =
                doc.LockDocument())
            using (Transaction tr =
                db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id
                    in ids)
                {
                    BlockReference br =
                        null;

                    try
                    {
                        br =
                            tr.GetObject(
                                id,
                                OpenMode.ForRead,
                                false) as BlockReference;
                    }
                    catch
                    {
                    }

                    if (br == null ||
                        br.IsErased)
                    {
                        continue;
                    }

                    result.BlockCount++;

                    string blockName =
                        GetBlockName(
                            tr,
                            br);

                    string attributes =
                        GetAttributeText(
                            tr,
                            br);

                    string layer =
                        br.Layer ?? "";

                    if (!MepHvacDeviceTaxonomy.TryResolve(
                            blockName + " " +
                            attributes,
                            blockName,
                            layer,
                            attributes,
                            out MepHvacDeviceMatch match))
                    {
                        continue;
                    }

                    Extents3d? extents =
                        null;

                    try
                    {
                        extents =
                            br.GeometricExtents;
                    }
                    catch
                    {
                    }

                    Point3d position =
                        br.Position;

                    if (extents.HasValue)
                    {
                        Extents3d ex =
                            extents.Value;

                        position =
                            new Point3d(
                                (ex.MinPoint.X +
                                 ex.MaxPoint.X) *
                                0.5,
                                (ex.MinPoint.Y +
                                 ex.MaxPoint.Y) *
                                0.5,
                                (ex.MinPoint.Z +
                                 ex.MaxPoint.Z) *
                                0.5);
                    }

                    string size = "";
                    string system = "";
                    string fireRating = "";
                    double confidence =
                        match.Confidence;
                    bool review = false;
                    string evidence =
                        match.Evidence;

                    // Size ghi ngay trong block/attribute có ưu tiên cao.
                    if (MepDuctSizeParser.TryParse(
                            blockName + " " +
                            attributes,
                            layer,
                            out MepDuctSizeInfo localSize))
                    {
                        size =
                            localSize.CanonicalSize;
                        system =
                            localSize.SystemCode;
                        fireRating =
                            localSize.FireRating;

                        confidence =
                            Math.Min(
                                0.995,
                                confidence +
                                0.03);

                        evidence +=
                            " + size trực tiếp";
                    }

                    // Damper/VAV/CAV/silencer thường lấy size theo duct.
                    if (match.FollowDuctSize &&
                        string.IsNullOrWhiteSpace(
                            size) &&
                        ductContext != null)
                    {
                        MepDuctNearestSizeResult near =
                            ductEngine.InferNearestSize(
                                position,
                                extents,
                                ductContext);

                        if (near.Found)
                        {
                            size =
                                near.Size;
                            system =
                                near.SystemCode;
                            fireRating =
                                near.FireRating;

                            confidence =
                                Math.Min(
                                    0.995,
                                    confidence * 0.78 +
                                    near.Confidence * 0.22);

                            evidence +=
                                " + " +
                                near.Evidence;

                            if (near.Ambiguous)
                                review = true;
                        }
                        else
                        {
                            review = true;
                            evidence +=
                                " + chưa thấy duct size gần";
                        }
                    }

                    // Fire damper nếu layer/text có EI thì giữ EI.
                    if (string.IsNullOrWhiteSpace(
                            fireRating))
                    {
                        fireRating =
                            MepDuctSizeParser
                                .ParseFireRating(
                                    blockName +
                                    " " +
                                    attributes +
                                    " " +
                                    layer);
                    }

                    if (string.IsNullOrWhiteSpace(
                            system))
                    {
                        system =
                            MepDuctSizeParser
                                .InferSystemCode(
                                    blockName +
                                    " " +
                                    attributes +
                                    " " +
                                    layer);
                    }

                    if (confidence < 0.72)
                        review = true;

                    result.Candidates.Add(
                        new MepHvacDeviceCandidate
                        {
                            Id = id,
                            Handle =
                                SafeHandle(id),
                            BlockName =
                                blockName,
                            Layer =
                                layer,
                            Position =
                                position,
                            Extents =
                                extents,
                            Label =
                                match.CanonicalLabel,
                            Group =
                                match.Group,
                            Size =
                                size,
                            SystemCode =
                                system,
                            FireRating =
                                fireRating,
                            Confidence =
                                confidence,
                            NeedsReview =
                                review,
                            Evidence =
                                evidence
                        });
                }

                tr.Commit();
            }

            result.Stats =
                BuildStats(
                    result.Candidates);

            return result;
        }

        public static string BuildCompactSummary(
            MepHvacDeviceScanResult run,
            int maxRows = 14)
        {
            if (run == null ||
                run.RecognizedCount <= 0)
            {
                return
                    "HVAC THIẾT BỊ: chưa nhận được block có evidence rõ.";
            }

            List<string> lines =
                new List<string>
                {
                    "HVAC THIẾT BỊ / VAN GIÓ",
                    "Nhận: " +
                    run.RecognizedCount +
                    " | Cần kiểm tra: " +
                    run.ReviewCount
                };

            foreach (MepHvacDeviceStatRow row
                in (run.Stats ??
                    new List<MepHvacDeviceStatRow>())
                    .Take(
                        Math.Max(
                            1,
                            maxRows)))
            {
                lines.Add(
                    row.Label +
                    (string.IsNullOrWhiteSpace(
                        row.Size)
                        ? ""
                        : " " +
                          row.Size) +
                    (string.IsNullOrWhiteSpace(
                        row.FireRating)
                        ? ""
                        : " " +
                          row.FireRating) +
                    " = " +
                    row.Quantity);
            }

            if (run.Stats != null &&
                run.Stats.Count > maxRows)
            {
                lines.Add(
                    "... +" +
                    (run.Stats.Count -
                     maxRows) +
                    " dòng");
            }

            return
                string.Join(
                    Environment.NewLine,
                    lines);
        }

        private static List<MepHvacDeviceStatRow> BuildStats(
            List<MepHvacDeviceCandidate> candidates)
        {
            if (candidates == null)
                return new List<MepHvacDeviceStatRow>();

            return
                candidates
                    .Where(x =>
                        x != null &&
                        !string.IsNullOrWhiteSpace(
                            x.Label))
                    .GroupBy(x =>
                        new
                        {
                            Label =
                                x.Label ?? "",
                            Size =
                                x.Size ?? "",
                            System =
                                x.SystemCode ?? "",
                            Fire =
                                x.FireRating ?? ""
                        })
                    .Select(g =>
                        new MepHvacDeviceStatRow
                        {
                            Label =
                                g.Key.Label,
                            Size =
                                g.Key.Size,
                            SystemCode =
                                g.Key.System,
                            FireRating =
                                g.Key.Fire,
                            Quantity =
                                g.Count(),
                            AverageConfidence =
                                g.Select(x =>
                                        x.Confidence)
                                    .DefaultIfEmpty(0.0)
                                    .Average()
                        })
                    .OrderBy(x =>
                        x.Label)
                    .ThenBy(x =>
                        x.Size)
                    .ToList();
        }

        private static string GetBlockName(
            Transaction tr,
            BlockReference br)
        {
            string name =
                "BLOCK";

            try
            {
                ObjectId definitionId =
                    br.IsDynamicBlock
                        ? br.DynamicBlockTableRecord
                        : br.BlockTableRecord;

                BlockTableRecord def =
                    tr.GetObject(
                        definitionId,
                        OpenMode.ForRead,
                        false) as BlockTableRecord;

                if (def != null &&
                    !string.IsNullOrWhiteSpace(
                        def.Name))
                {
                    name =
                        def.Name;
                }
            }
            catch
            {
            }

            return name;
        }

        private static string GetAttributeText(
            Transaction tr,
            BlockReference br)
        {
            StringBuilder sb =
                new StringBuilder();

            try
            {
                foreach (ObjectId id
                    in br.AttributeCollection)
                {
                    AttributeReference ar =
                        tr.GetObject(
                            id,
                            OpenMode.ForRead,
                            false) as AttributeReference;

                    if (ar == null)
                        continue;

                    if (!string.IsNullOrWhiteSpace(
                            ar.Tag))
                    {
                        sb.Append(
                            ar.Tag);
                        sb.Append('=');
                    }

                    sb.Append(
                        ar.TextString ?? "");
                    sb.Append(' ');
                }
            }
            catch
            {
            }

            return
                sb.ToString()
                    .Trim();
        }

        private static string SafeHandle(
            ObjectId id)
        {
            try
            {
                return
                    id.Handle.ToString();
            }
            catch
            {
                return "";
            }
        }
    }
}
