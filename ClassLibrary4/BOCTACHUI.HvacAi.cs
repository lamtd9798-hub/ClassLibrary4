#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

using MessageBox = System.Windows.MessageBox;

namespace ClassLibrary4
{
    /// <summary>
    /// STEP30B-D3 - bridge tích hợp HVAC vào đúng các nút AI hiện tại
    /// mà KHÔNG nhét thêm logic vào BOCTACHUI.xaml.cs.
    ///
    /// XAML chỉ đổi 3 Click handler:
    /// - BtnAiAutoTakeoff_Click -> BtnAiAutoTakeoffHvac_Click
    /// - BtnAiAutoRun_Click     -> BtnAiAutoRunHvac_Click
    /// - BtnAiDeviceOnly_Click  -> BtnAiDeviceOnlyHvac_Click
    /// </summary>
    public partial class BOCTACHUI
    {
        private readonly MepDuctSemanticEngine _aiDuctSemanticEngine =
            new MepDuctSemanticEngine();

        private readonly MepHvacDeviceSemanticEngine _aiHvacDeviceEngine =
            new MepHvacDeviceSemanticEngine();

        private MepDuctScanResult _lastAiDuctRun =
            null;

        private MepHvacDeviceScanResult _lastAiHvacDeviceRun =
            null;

        // ============================================================
        // AI ĐƯỜNG ỐNG = PIPE + DUCT, CHỈ QUÉT 1 LẦN
        // ============================================================

        private void BtnAiAutoTakeoffHvac_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                MepPluginExtensionApp.RegisterResolvers();

                // Status của AI học/ONNX/OpenCV là phần tùy chọn. Một package
                // phụ lỗi không được chặn chức năng bóc PIPE + DUCT bằng CAD.
                try
                {
                    UpdateAiLearningStatusUi();
                }
                catch
                {
                }

                RunAiAutomaticPipeAndDuctTakeoff();
            }
            catch (System.Exception ex)
            {
                try
                {
                    MessageBox.Show(
                        "AI ĐƯỜNG ỐNG + ỐNG GIÓ gặp lỗi nhưng đã được chặn.\n\n" +
                        BuildCompactHvacError(ex),
                        "AI BÓC TÁCH MEP");
                }
                catch
                {
                }
            }
        }

        private void RunAiAutomaticPipeAndDuctTakeoff()
        {
            Document doc =
                Autodesk.AutoCAD.ApplicationServices.Core.Application
                    .DocumentManager
                    .MdiActiveDocument;

            if (doc == null)
                return;

            Editor ed =
                doc.Editor;

            try
            {
                Autodesk.AutoCAD.ApplicationServices.Core.Application
                    .MainWindow
                    .Focus();
            }
            catch
            {
            }

            MessageBoxResult confirm =
                MessageBox.Show(
                    "AI sẽ nhận diện chung ĐƯỜNG ỐNG + ỐNG GIÓ trên cùng một vùng quét.\n\n" +
                    "PIPE:\n" +
                    "• DN100 / D60 / Ø60\n" +
                    "• Output: TDL_AI_PIPE_DN...\n\n" +
                    "DUCT:\n" +
                    "• 800x400 / W800xH400 / Ø300\n" +
                    "• OG CẤP / HỒI / THẢI / HÚT KHÓI / GIÓ TƯƠI\n" +
                    "• EI30 / EI60 / EI90...\n" +
                    "• Nhận centerline, 2 nét song song và khung chữ nhật\n" +
                    "• Output: TDL_AI_DUCT_...\n\n" +
                    "Không sửa hoặc xóa nét gốc.\n\n" +
                    "Tiếp tục?",
                    "AI ĐƯỜNG ỐNG MEP",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

            if (confirm !=
                MessageBoxResult.Yes)
            {
                return;
            }

            PromptSelectionOptions pso =
                new PromptSelectionOptions();

            pso.MessageForAdding =
                "\n[AI MEP] Quét MỘT LẦN vùng có PIPE + DUCT + TEXT DN/WxH: ";

            TypedValue[] tvs =
                new TypedValue[]
                {
                    new TypedValue(
                        (int)DxfCode.Operator,
                        "<OR"),
                    new TypedValue(
                        (int)DxfCode.Start,
                        "LINE"),
                    new TypedValue(
                        (int)DxfCode.Start,
                        "LWPOLYLINE"),
                    new TypedValue(
                        (int)DxfCode.Start,
                        "POLYLINE"),
                    new TypedValue(
                        (int)DxfCode.Start,
                        "ARC"),
                    new TypedValue(
                        (int)DxfCode.Start,
                        "CIRCLE"),
                    new TypedValue(
                        (int)DxfCode.Start,
                        "INSERT"),
                    new TypedValue(
                        (int)DxfCode.Start,
                        "POINT"),
                    new TypedValue(
                        (int)DxfCode.Start,
                        "TEXT"),
                    new TypedValue(
                        (int)DxfCode.Start,
                        "MTEXT"),
                    new TypedValue(
                        (int)DxfCode.Start,
                        "HATCH"),
                    new TypedValue(
                        (int)DxfCode.Operator,
                        "OR>")
                };

            PromptSelectionResult psr =
                ed.GetSelection(
                    pso,
                    new SelectionFilter(
                        tvs));

            if (psr.Status !=
                    PromptStatus.OK ||
                psr.Value == null ||
                psr.Value.Count == 0)
            {
                return;
            }

            SelectionSet workingSelection =
                psr.Value;

            List<string> nonBlockingErrors =
                new List<string>();

            AiPipeTakeoffRunResult pipeRun = null;

            try
            {
                pipeRun =
                    AnalyzeAndDrawAiPipeTakeoff(
                        doc,
                        workingSelection);
            }
            catch (System.Exception ex)
            {
                nonBlockingErrors.Add(
                    "PIPE: " + BuildCompactHvacError(ex));
            }

            _lastAiAutoPipeRun =
                pipeRun;

            MepDuctScanResult ductRun = null;

            try
            {
                ductRun =
                    _aiDuctSemanticEngine
                        .AnalyzeAndDraw(
                            doc,
                            workingSelection
                                .GetObjectIds(),
                            true,
                            true);
            }
            catch (System.Exception ex)
            {
                nonBlockingErrors.Add(
                    "DUCT: " + BuildCompactHvacError(ex));
            }

            _lastAiDuctRun =
                ductRun;

            // Graph hiện vẫn dùng PIPE DN. Duct Graph sẽ nối vào STEP30B-D4.
            try
            {
                BuildMepGraphFromSelection(
                    doc,
                    workingSelection
                        .GetObjectIds(),
                    false);
            }
            catch (System.Exception ex)
            {
                nonBlockingErrors.Add(
                    "GRAPH: " + BuildCompactHvacError(ex));
            }

            int pipeCount =
                pipeRun != null
                    ? pipeRun.OutputSegmentCount
                    : 0;

            int ductCount =
                ductRun != null
                    ? ductRun.OutputSegmentCount
                    : 0;

            if (pipeCount <= 0 &&
                ductCount <= 0)
            {
                MessageBox.Show(
                    "Chưa có tuyến nào đủ bằng chứng để AI chốt.\n\n" +
                    "PIPE cần text DN100 / D60 / Ø60 song song tuyến.\n" +
                    "DUCT cần text 800x400 / W800xH400 hoặc ØD có context ACMV." +
                    (nonBlockingErrors.Count > 0
                        ? "\n\nChi tiết phần bị chặn:\n• " +
                          string.Join("\n• ", nonBlockingErrors)
                        : ""),
                    "AI ĐƯỜNG ỐNG MEP");

                return;
            }

            if (pipeRun != null &&
                pipeRun.OutputSegmentCount > 0)
            {
                bool placePipeTable =
                    ShowAiPipeTakeoffSummaryDialog(
                        pipeRun);

                if (placePipeTable &&
                    pipeRun.Stats.Count > 0)
                {
                    OutputAiPipeTakeoffTable(
                        doc,
                        pipeRun.Stats);
                }
            }

            if (ductRun != null &&
                ductRun.OutputSegmentCount > 0)
            {
                MessageBoxResult ductTable =
                    MessageBox.Show(
                        MepDuctSemanticEngine
                            .BuildCompactSummary(
                                ductRun) +
                        "\n\nYES = đặt bảng thống kê ống gió vào bản vẽ.",
                        "AI ỐNG GIÓ",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                if (ductTable ==
                    MessageBoxResult.Yes)
                {
                    OutputAiDuctTakeoffTable(
                        doc,
                        ductRun.Stats);
                }
            }

            ed.WriteMessage(
                "\n[AI MEP] Xong PIPE=" +
                pipeCount +
                " đoạn | DUCT=" +
                ductCount +
                " đoạn.");

            if (nonBlockingErrors.Count > 0)
            {
                MessageBox.Show(
                    "AI đã hoàn thành các phần còn chạy được.\n\n" +
                    "Các phần lỗi đã được cô lập, không làm mất kết quả còn lại:\n• " +
                    string.Join("\n• ", nonBlockingErrors),
                    "AI BÓC TÁCH MEP",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private static string BuildCompactHvacError(
            System.Exception ex)
        {
            if (ex == null)
                return "Lỗi không xác định.";

            System.Exception root = ex;

            while (root.InnerException != null && root.InnerException != root)
                root = root.InnerException;

            string message = (root.Message ?? "").Trim();

            if (message.Length > 420)
                message = message.Substring(0, 420) + "...";

            return root.GetType().Name +
                   (string.IsNullOrWhiteSpace(message)
                       ? ""
                       : " - " + message);
        }

        // ============================================================
        // AI VAN / THIẾT BỊ = MEP SYMBOL + HVAC SEMANTIC
        // ============================================================

        private void BtnAiDeviceOnlyHvac_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                MepPluginExtensionApp.RegisterResolvers();

                try
                {
                    UpdateAiLearningStatusUi();
                    UpdateOnnxStatusUi();
                }
                catch
                {
                }

                Document doc =
                    Autodesk.AutoCAD.ApplicationServices.Core.Application
                        .DocumentManager
                        .MdiActiveDocument;

                if (doc == null)
                    return;

                Editor ed =
                    doc.Editor;

                bool hasCurrentProjectLegend;

                List<SmartSymbolRule> rules =
                    ResolveAiAutoDeviceRules(
                        doc,
                        out hasCurrentProjectLegend);

                rules =
                    MergeBuiltInHvacRules(
                        rules);

                PromptSelectionOptions pso =
                    new PromptSelectionOptions();

                pso.MessageForAdding =
                    "\n[AI THIẾT BỊ] Quét vùng VAN / THIẾT BỊ / VAN GIÓ / MIỆNG GIÓ: ";

                PromptSelectionResult psr =
                    ed.GetSelection(
                        pso);

                if (psr.Status !=
                        PromptStatus.OK ||
                    psr.Value == null ||
                    psr.Value.Count == 0)
                {
                    return;
                }

                SelectionSet workingSelection =
                    psr.Value;

                // Duct context chạy im lặng để VCD/FD/FSD lấy đúng WxH.
                MepDuctScanResult ductContext =
                    new MepDuctScanResult();

                try
                {
                    ductContext =
                        _aiDuctSemanticEngine
                            .AnalyzeAndDraw(
                                doc,
                                workingSelection
                                    .GetObjectIds(),
                                false,
                                false);
                }
                catch
                {
                    // Duct context chỉ là evidence bổ sung. Exact Block/Vector
                    // và pipeline VAN/TB chính vẫn phải tiếp tục.
                }

                _lastAiDuctRun =
                    ductContext;

                if (rules != null &&
                    rules.Count > 0)
                {
                    ScanSmartValveDeviceStatistics(
                        doc,
                        rules,
                        workingSelection,
                        false,
                        true,
                        hasCurrentProjectLegend);
                }

                MepHvacDeviceScanResult hvac =
                    _aiHvacDeviceEngine
                        .Analyze(
                            doc,
                            workingSelection
                                .GetObjectIds(),
                            ductContext);

                _lastAiHvacDeviceRun =
                    hvac;

                MessageBox.Show(
                    MepHvacDeviceSemanticEngine
                        .BuildCompactSummary(
                            hvac) +
                    "\n\n" +
                    "AI chính vẫn chạy Exact Block → Vector → ONNX/YOLO → Fusion.\n" +
                    "HVAC Semantic là evidence bổ sung từ Block/Layer/Attribute + duct context.",
                    "AI THIẾT BỊ HVAC",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                UpdateAiAutoHubStatusUi();
            }
            catch (System.Exception ex)
            {
                HandleSmartValveFatalSafe(
                    "STEP30B_D3_HVAC_DEVICE",
                    ex);
            }
        }

        // ============================================================
        // AI TỰ ĐỘNG = PIPE + DUCT + VAN/TB + HVAC
        // CHỈ QUÉT 1 LẦN
        // ============================================================

        private void BtnAiAutoRunHvac_Click(
            object sender,
            RoutedEventArgs e)
        {
            RunAiUnifiedPipelineHvac();
        }

        private void RunAiUnifiedPipelineHvac()
        {
            if (_aiAutoPipelineBusy)
                return;

            MepPluginExtensionApp.RegisterResolvers();

            Document doc =
                Autodesk.AutoCAD.ApplicationServices.Core.Application
                    .DocumentManager
                    .MdiActiveDocument;

            if (doc == null)
                return;

            Editor ed =
                doc.Editor;

            List<string> nonBlockingErrors =
                new List<string>();

            try
            {
                Autodesk.AutoCAD.ApplicationServices.Core.Application
                    .MainWindow
                    .Focus();
            }
            catch
            {
            }

            _aiAutoPipelineBusy =
                true;

            try
            {
                if (BtnAiAutoRun != null)
                {
                    BtnAiAutoRun.IsEnabled =
                        false;

                    BtnAiAutoRun.Content =
                        "AI ĐANG PHÂN TÍCH...";
                }

                if (TxtAiAutoStatus != null)
                {
                    TxtAiAutoStatus.Text =
                        "AI ENGINE: ĐANG CHUẨN BỊ MODEL / LEGEND / HVAC...";
                }

                try
                {
                    UpdateOnnxStatusUi();
                    UpdateAiGnnStatusUi();
                }
                catch (System.Exception ex)
                {
                    nonBlockingErrors.Add(
                        "MODEL STATUS: " + BuildCompactHvacError(ex));
                }

                bool hasCurrentProjectLegend = false;

                List<SmartSymbolRule> autoDeviceRules =
                    new List<SmartSymbolRule>();

                try
                {
                    autoDeviceRules =
                        ResolveAiAutoDeviceRules(
                            doc,
                            out hasCurrentProjectLegend);
                }
                catch (System.Exception ex)
                {
                    nonBlockingErrors.Add(
                        "DEVICE RULE: " + BuildCompactHvacError(ex));
                }

                autoDeviceRules =
                    MergeBuiltInHvacRules(
                        autoDeviceRules);

                bool hasDeviceRules =
                    autoDeviceRules != null &&
                    autoDeviceRules.Count > 0;

                PromptSelectionOptions pso =
                    new PromptSelectionOptions();

                pso.MessageForAdding =
                    "\n[AI TỰ ĐỘNG] Quét MỘT LẦN toàn bộ MEP: PIPE + DUCT + VAN/TB + HVAC: ";

                PromptSelectionResult psr =
                    ed.GetSelection(
                        pso);

                if (psr.Status !=
                        PromptStatus.OK ||
                    psr.Value == null ||
                    psr.Value.Count == 0)
                {
                    return;
                }

                SelectionSet workingSelection =
                    psr.Value;

                if (TxtAiAutoStatus != null)
                {
                    TxtAiAutoStatus.Text =
                        "AI ENGINE: 1/4 • PIPE + DUCT...";
                }

                AiPipeTakeoffRunResult pipeRun = null;

                try
                {
                    pipeRun =
                        AnalyzeAndDrawAiPipeTakeoff(
                            doc,
                            workingSelection);
                }
                catch (System.Exception ex)
                {
                    nonBlockingErrors.Add(
                        "PIPE: " + BuildCompactHvacError(ex));
                }

                _lastAiAutoPipeRun =
                    pipeRun;

                MepDuctScanResult ductRun = null;

                try
                {
                    ductRun =
                        _aiDuctSemanticEngine
                            .AnalyzeAndDraw(
                                doc,
                                workingSelection
                                    .GetObjectIds(),
                                true,
                                true);
                }
                catch (System.Exception ex)
                {
                    nonBlockingErrors.Add(
                        "DUCT: " + BuildCompactHvacError(ex));
                }

                _lastAiDuctRun =
                    ductRun;

                if (TxtAiAutoStatus != null)
                {
                    TxtAiAutoStatus.Text =
                        "AI ENGINE: 2/4 • GRAPH / TOPOLOGY...";
                }

                try
                {
                    BuildMepGraphFromSelection(
                        doc,
                        workingSelection
                            .GetObjectIds(),
                        false);
                }
                catch (System.Exception ex)
                {
                    nonBlockingErrors.Add(
                        "GRAPH: " + BuildCompactHvacError(ex));
                }

                if (hasDeviceRules)
                {
                    if (TxtAiAutoStatus != null)
                    {
                        TxtAiAutoStatus.Text =
                            "AI ENGINE: 3/4 • VAN / THIẾT BỊ / YOLO...";
                    }

                    try
                    {
                        ScanSmartValveDeviceStatistics(
                            doc,
                            autoDeviceRules,
                            workingSelection,
                            false,
                            true,
                            hasCurrentProjectLegend);
                    }
                    catch (System.Exception ex)
                    {
                        nonBlockingErrors.Add(
                            "VAN/TB: " + BuildCompactHvacError(ex));
                    }
                }

                if (TxtAiAutoStatus != null)
                {
                    TxtAiAutoStatus.Text =
                        "AI ENGINE: 4/4 • HVAC VAN GIÓ / MIỆNG GIÓ...";
                }

                MepHvacDeviceScanResult hvacRun = null;

                try
                {
                    hvacRun =
                        _aiHvacDeviceEngine
                            .Analyze(
                                doc,
                                workingSelection
                                    .GetObjectIds(),
                                ductRun);
                }
                catch (System.Exception ex)
                {
                    nonBlockingErrors.Add(
                        "HVAC DEVICE: " + BuildCompactHvacError(ex));
                }

                _lastAiHvacDeviceRun =
                    hvacRun;

                _lastAiAutoRunUtc =
                    DateTime.UtcNow;

                try
                {
                    UpdateAiAutoHubStatusUi();
                    UpdateAiGraphStatusUi();
                    UpdateAiLegendStatusUi();
                    UpdateAiLearningStatusUi();
                    UpdateAiDatasetStatusUi();
                }
                catch (System.Exception ex)
                {
                    nonBlockingErrors.Add(
                        "STATUS UI: " + BuildCompactHvacError(ex));
                }

                int pipeCount =
                    pipeRun != null
                        ? pipeRun.OutputSegmentCount
                        : 0;

                int ductCount =
                    ductRun != null
                        ? ductRun.OutputSegmentCount
                        : 0;

                int auditTotal =
                    _lastSmartAuditRows != null
                        ? _lastSmartAuditRows.Count
                        : 0;

                int hvacCount =
                    hvacRun != null
                        ? hvacRun.RecognizedCount
                        : 0;

                int needReview =
                    (_lastSmartAuditRows != null
                        ? _lastSmartAuditRows.Count(
                            r =>
                                r != null &&
                                !string.Equals(
                                    r.Status,
                                    "OK",
                                    StringComparison.OrdinalIgnoreCase))
                        : 0) +
                    (hvacRun != null
                        ? hvacRun.ReviewCount
                        : 0);

                string ductSummary =
                    ductRun != null &&
                    ductRun.OutputSegmentCount > 0
                        ? "\n\n" +
                          MepDuctSemanticEngine
                              .BuildCompactSummary(
                                  ductRun,
                                  8)
                        : "";

                string hvacSummary =
                    hvacRun != null &&
                    hvacRun.RecognizedCount > 0
                        ? "\n\n" +
                          MepHvacDeviceSemanticEngine
                              .BuildCompactSummary(
                                  hvacRun,
                                  8)
                        : "";

                MessageBox.Show(
                    "AI TỰ ĐỘNG ĐÃ CHẠY XONG\n\n" +
                    "PIPE: " +
                    pipeCount +
                    " đoạn\n" +
                    "DUCT: " +
                    ductCount +
                    " đoạn\n" +
                    "Van / thiết bị AI: " +
                    auditTotal +
                    "\n" +
                    "HVAC deterministic: " +
                    hvacCount +
                    "\n" +
                    "Cần kiểm tra: " +
                    needReview +
                    ductSummary +
                    hvacSummary +
                    (nonBlockingErrors.Count > 0
                        ? "\n\nPHẦN ĐÃ CÔ LẬP LỖI:\n• " +
                          string.Join("\n• ", nonBlockingErrors)
                        : ""),
                    "AI MEP");

                if (TxtAiAutoSummary != null)
                {
                    TxtAiAutoSummary.Text =
                        "PIPE " +
                        pipeCount +
                        " • DUCT " +
                        ductCount +
                        " • TB " +
                        auditTotal +
                        " • HVAC " +
                        hvacCount +
                        " • REVIEW " +
                        needReview;
                }

                try
                {
                    RequestAiCloudAutoSyncQuiet(
                        "AUTO_PIPE_DUCT_HVAC_FINISHED",
                        true);
                }
                catch
                {
                }
            }
            catch (System.Exception ex)
            {
                HandleSmartValveFatalSafe(
                    "STEP30B_D3_AUTO_PIPE_DUCT_HVAC",
                    ex);
            }
            finally
            {
                _aiAutoPipelineBusy =
                    false;

                if (BtnAiAutoRun != null)
                {
                    BtnAiAutoRun.IsEnabled =
                        true;

                    BtnAiAutoRun.Content =
                        "AI TỰ ĐỘNG NHẬN DIỆN";
                }
            }
        }

        // ============================================================
        // BUILT-IN HVAC RULES FOR ONNX / YOLO
        // ============================================================

        private List<SmartSymbolRule> MergeBuiltInHvacRules(
            List<SmartSymbolRule> source)
        {
            List<SmartSymbolRule> result =
                CloneSmartRulesForSession(
                    source ??
                    new List<SmartSymbolRule>());

            foreach (MepHvacDeviceDefinition def
                in MepHvacDeviceTaxonomy.All)
            {
                if (def == null ||
                    string.IsNullOrWhiteSpace(
                        def.CanonicalLabel))
                {
                    continue;
                }

                string key =
                    NormalizeOnnxDisplayKey(
                        def.CanonicalLabel);

                bool exists =
                    result.Any(r =>
                        r != null &&
                        string.Equals(
                            NormalizeOnnxDisplayKey(
                                r.DisplayName),
                            key,
                            StringComparison.OrdinalIgnoreCase));

                if (exists)
                    continue;

                result.Add(
                    new SmartSymbolRule
                    {
                        BlockKey =
                            "HVAC_AI_" +
                            NormalizeSmartSymbolKey(
                                def.CanonicalLabel),
                        DisplayName =
                            def.CanonicalLabel,
                        SizeRule =
                            def.FollowDuctSize
                                ? "THEO_DUCT"
                                : "KHONG_SIZE",
                        MatchMode =
                            "AI_LABEL",
                        GeometryFingerprint =
                            "",
                        RasterSignature =
                            ""
                    });
            }

            return result;
        }

        // ============================================================
        // DUCT TABLE
        // ============================================================

        private void OutputAiDuctTakeoffTable(
            Document doc,
            List<MepDuctTakeoffRow> rows)
        {
            if (doc == null ||
                rows == null ||
                rows.Count == 0)
            {
                return;
            }

            Editor ed =
                doc.Editor;

            PromptPointResult ppr =
                ed.GetPoint(
                    "\n[AI DUCT] Chọn điểm đặt bảng thống kê ống gió: ");

            if (ppr.Status !=
                PromptStatus.OK)
            {
                return;
            }

            Database db =
                doc.Database;

            using (doc.LockDocument())
            using (Transaction tr =
                db.TransactionManager.StartTransaction())
            {
                BlockTableRecord space =
                    tr.GetObject(
                        db.CurrentSpaceId,
                        OpenMode.ForWrite)
                    as BlockTableRecord;

                if (space == null)
                    return;

                int rowCount =
                    rows.Count + 3;

                Table table =
                    new Table();

                table.SetDatabaseDefaults(db);
                table.Position =
                    ppr.Value;
                table.SetSize(
                    rowCount,
                    7);

                table.SetRowHeight(260.0);

                table.Columns[0].Width = 500.0;
                table.Columns[1].Width = 1150.0;
                table.Columns[2].Width = 1150.0;
                table.Columns[3].Width = 750.0;
                table.Columns[4].Width = 750.0;
                table.Columns[5].Width = 1000.0;
                table.Columns[6].Width = 1150.0;

                table.Cells[0, 0].TextString =
                    "AI THỐNG KÊ ỐNG GIÓ";

                table.MergeCells(
                    CellRange.Create(
                        table,
                        0,
                        0,
                        0,
                        6));

                string[] headers =
                {
                    "STT",
                    "HỆ",
                    "SIZE",
                    "EI",
                    "SL ĐOẠN",
                    "DÀI (m)",
                    "DIỆN TÍCH (m²)"
                };

                for (int c = 0;
                    c < headers.Length;
                    c++)
                {
                    table.Cells[1, c].TextString =
                        headers[c];
                }

                for (int i = 0;
                    i < rows.Count;
                    i++)
                {
                    MepDuctTakeoffRow row =
                        rows[i];

                    int r =
                        i + 2;

                    table.Cells[r, 0].TextString =
                        (i + 1).ToString(
                            CultureInfo.InvariantCulture);

                    table.Cells[r, 1].TextString =
                        string.IsNullOrWhiteSpace(
                            row.SystemCode)
                            ? "DUCT"
                            : row.SystemCode;

                    table.Cells[r, 2].TextString =
                        row.Size ?? "";

                    table.Cells[r, 3].TextString =
                        row.FireRating ?? "";

                    table.Cells[r, 4].TextString =
                        row.SegmentCount.ToString(
                            CultureInfo.InvariantCulture);

                    table.Cells[r, 5].TextString =
                        row.LengthMeters.ToString(
                            "0.00",
                            CultureInfo.InvariantCulture);

                    table.Cells[r, 6].TextString =
                        row.SurfaceAreaM2.ToString(
                            "0.00",
                            CultureInfo.InvariantCulture);
                }

                int totalRow =
                    rowCount - 1;

                table.Cells[totalRow, 0].TextString =
                    "TỔNG";

                table.MergeCells(
                    CellRange.Create(
                        table,
                        totalRow,
                        0,
                        totalRow,
                        4));

                table.Cells[totalRow, 5].TextString =
                    rows.Sum(x =>
                        x.LengthMeters)
                        .ToString(
                            "0.00",
                            CultureInfo.InvariantCulture);

                table.Cells[totalRow, 6].TextString =
                    rows.Sum(x =>
                        x.SurfaceAreaM2)
                        .ToString(
                            "0.00",
                            CultureInfo.InvariantCulture);

                for (int rr = 0;
                    rr < rowCount;
                    rr++)
                {
                    for (int cc = 0;
                        cc < 7;
                        cc++)
                    {
                        table.Cells[rr, cc].Alignment =
                            CellAlignment.MiddleCenter;

                        table.Cells[rr, cc].TextHeight =
                            90.0;
                    }
                }

                table.Cells[0, 0].TextHeight =
                    130.0;

                space.AppendEntity(
                    table);

                tr.AddNewlyCreatedDBObject(
                    table,
                    true);

                tr.Commit();
            }

            ed.Regen();
        }
    }
}
