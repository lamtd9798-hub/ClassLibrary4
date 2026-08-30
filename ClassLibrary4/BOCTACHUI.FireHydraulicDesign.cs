// FIRE-HYDRAULIC-V2-20260829: bố trí đầu phun, chọn DN và tuyến bất lợi.
#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace ClassLibrary4
{
    public partial class BOCTACHUI
    {
        private readonly ObservableCollection<FireSprinklerLayoutRow>
            _fireSprinklerLayoutRows =
                new ObservableCollection<FireSprinklerLayoutRow>();

        private readonly List<ObjectId> _fireLastInsertedSprinklerIds =
            new List<ObjectId>();

        private bool _fireHydraulicUiInitialized;
        private int _fireActualSprinklerCount;

        private static readonly int[] FireNominalDiametersMm =
        {
            20, 25, 32, 40, 50, 65, 80, 100,
            125, 150, 200, 250, 300
        };

        private void FireHydraulicPanel_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (_fireHydraulicUiInitialized)
                return;

            _fireHydraulicUiInitialized = true;
            DgFireSprinklerLayouts.ItemsSource = _fireSprinklerLayoutRows;
            EnsureFireHydraulicDefaults();
            UpdateFireSprinklerLayoutSummary();
        }

        private void EnsureFireHydraulicDefaults()
        {
            SetFireTextIfEmpty(TxtFireLayoutSpacingX, "3.6");
            SetFireTextIfEmpty(TxtFireLayoutSpacingY, "3.6");
            SetFireTextIfEmpty(TxtFireLayoutWallOffset, "1.8");
            SetFireTextIfEmpty(TxtFireLayoutRotation, "0");
            SetFireTextIfEmpty(
                TxtFireSprinklerBlockName,
                "PCCC_SPRINKLER_AUTO");

            SetFireTextIfEmpty(TxtFireHazenC, "120");
            SetFireTextIfEmpty(TxtFireMaxVelocity, "3");
            SetFireTextIfEmpty(TxtFireMaxUnitLoss, "5");
            SetFireTextIfEmpty(TxtFireCriticalPathLength, "100");
            SetFireTextIfEmpty(TxtFireFittingsAllowance, "30");
            SetFireTextIfEmpty(TxtFireHeadsPerBranch, "8");
        }

        private bool TryGetFireHydraulicSettings(
            List<string> errors,
            out FireHydraulicSettings settings)
        {
            int initialErrorCount = errors.Count;
            settings = new FireHydraulicSettings();

            RequirePositiveFireNumber(
                TxtFireHazenC,
                "hệ số Hazen-Williams C",
                errors,
                out double hazenC);
            RequirePositiveFireNumber(
                TxtFireMaxVelocity,
                "vận tốc giới hạn",
                errors,
                out double maxVelocity);
            RequirePositiveFireNumber(
                TxtFireMaxUnitLoss,
                "tổn thất giới hạn trên 100 m",
                errors,
                out double maxUnitLoss);
            RequirePositiveFireNumber(
                TxtFireCriticalPathLength,
                "chiều dài tuyến bất lợi",
                errors,
                out double criticalPathLength);
            RequireNonNegativeFireNumber(
                TxtFireFittingsAllowance,
                "phụ trội chiều dài phụ kiện",
                errors,
                out double fittingsAllowance);
            RequirePositiveFireInteger(
                TxtFireHeadsPerBranch,
                "số đầu phun trên một nhánh",
                errors,
                out int headsPerBranch);

            settings.HazenC = hazenC;
            settings.MaxVelocityMps = maxVelocity;
            settings.MaxLossMetersPer100M = maxUnitLoss;
            settings.CriticalPathLengthM = criticalPathLength;
            settings.FittingsAllowancePercent = fittingsAllowance;
            settings.HeadsPerBranch = headsPerBranch;

            return errors.Count == initialErrorCount;
        }

        private FireHydraulicSummary CalculateFireHydraulicSummary(
            double sprinklerDemandLps,
            double indoorDemandLps,
            double outdoorDemandLps,
            double combinedDemandLps,
            double flowPerHeadLpm,
            FireHydraulicSettings settings)
        {
            var summary = new FireHydraulicSummary
            {
                Settings = settings
            };

            double oneBranchFlowLps =
                flowPerHeadLpm > 0
                    ? flowPerHeadLpm / 60.0 * settings.HeadsPerBranch
                    : 0;

            summary.Branch =
                SelectFirePipeSize(oneBranchFlowLps, settings);
            summary.SprinklerMain =
                SelectFirePipeSize(sprinklerDemandLps, settings);
            summary.IndoorHydrantMain =
                SelectFirePipeSize(indoorDemandLps, settings);
            summary.OutdoorHydrantMain =
                SelectFirePipeSize(outdoorDemandLps, settings);
            summary.CombinedMain =
                SelectFirePipeSize(combinedDemandLps, settings);

            if (combinedDemandLps > 0 && summary.CombinedMain.DnMm > 0)
            {
                double equivalentLength =
                    settings.CriticalPathLengthM *
                    (1.0 + settings.FittingsAllowancePercent / 100.0);

                summary.EquivalentCriticalLengthM = equivalentLength;
                summary.CriticalPathFrictionLossM =
                    CalculateFireHazenWilliamsLoss(
                        combinedDemandLps,
                        equivalentLength,
                        summary.CombinedMain.DnMm,
                        settings.HazenC);
            }

            return summary;
        }

        private static FirePipeSelection SelectFirePipeSize(
            double flowLps,
            FireHydraulicSettings settings)
        {
            if (flowLps <= 0)
                return new FirePipeSelection();

            FirePipeSelection fallback = null;

            foreach (int dn in FireNominalDiametersMm)
            {
                double velocity = CalculateFirePipeVelocity(flowLps, dn);
                double lossPer100 =
                    CalculateFireHazenWilliamsLoss(
                        flowLps,
                        100.0,
                        dn,
                        settings.HazenC);

                var candidate =
                    new FirePipeSelection
                    {
                        DnMm = dn,
                        FlowLps = flowLps,
                        VelocityMps = velocity,
                        LossMetersPer100M = lossPer100,
                        MeetsCriteria =
                            velocity <= settings.MaxVelocityMps &&
                            lossPer100 <= settings.MaxLossMetersPer100M
                    };

                fallback = candidate;

                if (candidate.MeetsCriteria)
                    return candidate;
            }

            return fallback ?? new FirePipeSelection();
        }

        private static double CalculateFirePipeVelocity(
            double flowLps,
            int diameterMm)
        {
            if (flowLps <= 0 || diameterMm <= 0)
                return 0;

            double flowM3s = flowLps / 1000.0;
            double diameterM = diameterMm / 1000.0;
            double area = Math.PI * diameterM * diameterM / 4.0;

            return flowM3s / area;
        }

        private static double CalculateFireHazenWilliamsLoss(
            double flowLps,
            double lengthM,
            int diameterMm,
            double hazenC)
        {
            if (flowLps <= 0 ||
                lengthM <= 0 ||
                diameterMm <= 0 ||
                hazenC <= 0)
            {
                return 0;
            }

            double flowM3s = flowLps / 1000.0;
            double diameterM = diameterMm / 1000.0;

            // Dạng SI: hf = 10.672 L Q^1.852 / (C^1.852 d^4.87)
            return 10.672 * lengthM * Math.Pow(flowM3s, 1.852) /
                   (Math.Pow(hazenC, 1.852) *
                    Math.Pow(diameterM, 4.87));
        }

        private void AppendFireHydraulicResults(
            FireHydraulicSummary summary)
        {
            if (summary == null)
                return;

            AddFirePipeResult("Ống nhánh sprinkler", summary.Branch);
            AddFirePipeResult("Ống chính sprinkler", summary.SprinklerMain);
            AddFirePipeResult(
                "Ống chính họng trong nhà",
                summary.IndoorHydrantMain);
            AddFirePipeResult(
                "Ống chính chữa cháy ngoài nhà",
                summary.OutdoorHydrantMain);
            AddFirePipeResult("Ống chính kết hợp", summary.CombinedMain);

            if (summary.CombinedMain != null &&
                summary.CombinedMain.DnMm > 0)
            {
                AddFireResult(
                    "Tuyến bất lợi",
                    "Chiều dài hình học đã quét/nhập",
                    summary.Settings.CriticalPathLengthM,
                    "m",
                    "Từ bơm đến điểm dùng nước bất lợi");
                AddFireResult(
                    "Tuyến bất lợi",
                    "Chiều dài tương đương",
                    summary.EquivalentCriticalLengthM,
                    "m",
                    "Đã cộng " +
                    FireDisplay(summary.Settings.FittingsAllowancePercent) +
                    "% van/co/tê/phụ kiện");
                AddFireResult(
                    "Tuyến bất lợi",
                    "Tổn thất ma sát Hazen-Williams",
                    summary.CriticalPathFrictionLossM,
                    "mH₂O",
                    "Dùng DN ống chính kết hợp và lưu lượng đồng thời");
            }
        }

        private void AddFirePipeResult(
            string item,
            FirePipeSelection pipe)
        {
            if (pipe == null || pipe.DnMm <= 0 || pipe.FlowLps <= 0)
                return;

            AddFireResult(
                "Chọn DN",
                item,
                pipe.DnMm,
                "DN",
                "v=" + FireDisplay(pipe.VelocityMps) +
                " m/s; hf=" + FireDisplay(pipe.LossMetersPer100M) +
                " m/100m; DN đang dùng xấp xỉ đường kính trong" +
                (pipe.MeetsCriteria
                    ? "; kiểm tra lại ID thực theo vật liệu"
                    : "; VƯỢT GIỚI HẠN – cần DN lớn hơn 300 hoặc nhập ID thực"));
        }

        private void BtnFireReadCriticalPath_Click(
            object sender,
            RoutedEventArgs e)
        {
            Document doc =
                Autodesk.AutoCAD.ApplicationServices.Core.Application
                    .DocumentManager
                    .MdiActiveDocument;

            if (doc == null)
            {
                SetFireDesignStatus(
                    "Không tìm thấy bản vẽ AutoCAD đang mở.",
                    isError: true);
                return;
            }

            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();

            try
            {
                using (doc.LockDocument())
                {
                    var options = new PromptSelectionOptions
                    {
                        MessageForAdding =
                            "\nChọn các đoạn ống trên tuyến từ bơm đến điểm bất lợi: "
                    };

                    TypedValue[] filterValues =
                    {
                        new TypedValue(
                            (int)DxfCode.Start,
                            "LINE,LWPOLYLINE,POLYLINE,ARC,SPLINE")
                    };

                    PromptSelectionResult selection =
                        doc.Editor.GetSelection(
                            options,
                            new SelectionFilter(filterValues));

                    if (selection.Status != PromptStatus.OK ||
                        selection.Value == null ||
                        selection.Value.Count == 0)
                    {
                        SetFireDesignStatus(
                            "Đã hủy hoặc chưa chọn tuyến ống bất lợi.",
                            isError: false);
                        return;
                    }

                    double lengthToMeters =
                        Math.Sqrt(
                            ResolveFireDesignAreaFactor(
                                doc.Database,
                                out string unitDescription,
                                out bool unitAssumed));

                    double totalDrawingLength = 0;
                    int curveCount = 0;

                    using (Transaction transaction =
                           doc.Database.TransactionManager.StartTransaction())
                    {
                        foreach (SelectedObject selected in selection.Value)
                        {
                            if (selected == null || !selected.ObjectId.IsValid)
                                continue;

                            Curve curve =
                                transaction.GetObject(
                                    selected.ObjectId,
                                    OpenMode.ForRead,
                                    false) as Curve;

                            if (curve == null)
                                continue;

                            try
                            {
                                double startDistance =
                                    curve.GetDistanceAtParameter(
                                        curve.StartParam);
                                double endDistance =
                                    curve.GetDistanceAtParameter(
                                        curve.EndParam);
                                double length =
                                    Math.Abs(endDistance - startDistance);

                                if (length > 0)
                                {
                                    totalDrawingLength += length;
                                    curveCount++;
                                }
                            }
                            catch
                            {
                            }
                        }

                        transaction.Commit();
                    }

                    double totalMeters =
                        totalDrawingLength * lengthToMeters;

                    if (totalMeters <= 0)
                    {
                        SetFireDesignStatus(
                            "Không đọc được chiều dài từ các đối tượng đã chọn.",
                            isError: true);
                        return;
                    }

                    TxtFireCriticalPathLength.Text =
                        totalMeters.ToString(
                            "0.##",
                            CultureInfo.InvariantCulture);

                    string status =
                        "Đã đọc tuyến bất lợi gồm " + curveCount +
                        " đoạn, dài " + FireDisplay(totalMeters) +
                        " m. Đơn vị bản vẽ: " + unitDescription + ".";

                    if (unitAssumed)
                        status += " INSUNITS chưa khai báo; đang giả định mm.";

                    SetFireDesignStatus(
                        status,
                        isError: false,
                        isSuccess: true);
                }
            }
            catch (Exception ex)
            {
                SetFireDesignStatus(
                    "Không đọc được tuyến bất lợi: " + ex.Message,
                    isError: true);
            }
        }

        private void BtnFireLayoutSprinklers_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!TryParsePositiveFireDesignNumber(
                    TxtFireLayoutSpacingX?.Text,
                    out double spacingXM) ||
                !TryParsePositiveFireDesignNumber(
                    TxtFireLayoutSpacingY?.Text,
                    out double spacingYM) ||
                !TryParseNonNegativeFireNumber(
                    TxtFireLayoutWallOffset?.Text,
                    out double wallOffsetM) ||
                !TryParseFiniteFireNumber(
                    TxtFireLayoutRotation?.Text,
                    out double rotationDegrees))
            {
                SetFireDesignStatus(
                    "Khoảng cách X/Y, lùi tường và góc xoay chưa hợp lệ.",
                    isError: true);
                return;
            }

            Document doc =
                Autodesk.AutoCAD.ApplicationServices.Core.Application
                    .DocumentManager
                    .MdiActiveDocument;

            if (doc == null)
            {
                SetFireDesignStatus(
                    "Không tìm thấy bản vẽ AutoCAD đang mở.",
                    isError: true);
                return;
            }

            string blockName =
                NormalizeFireSymbolName(
                    TxtFireSprinklerBlockName?.Text,
                    "PCCC_SPRINKLER_AUTO");

            string layerName = "PCCC_SPRINKLER_HEAD_AUTO";

            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();

            try
            {
                using (doc.LockDocument())
                {
                    var options = new PromptSelectionOptions
                    {
                        MessageForAdding =
                            "\nChọn các Polyline kín cần bố trí đầu phun: "
                    };

                    TypedValue[] filterValues =
                    {
                        new TypedValue((int)DxfCode.Start, "LWPOLYLINE")
                    };

                    PromptSelectionResult selection =
                        doc.Editor.GetSelection(
                            options,
                            new SelectionFilter(filterValues));

                    if (selection.Status != PromptStatus.OK ||
                        selection.Value == null ||
                        selection.Value.Count == 0)
                    {
                        SetFireDesignStatus(
                            "Đã hủy hoặc chưa chọn Polyline kín.",
                            isError: false);
                        return;
                    }

                    Database database = doc.Database;
                    double areaFactor =
                        ResolveFireDesignAreaFactor(
                            database,
                            out string unitDescription,
                            out bool unitAssumed);
                    double lengthToMeters = Math.Sqrt(areaFactor);
                    double spacingX = spacingXM / lengthToMeters;
                    double spacingY = spacingYM / lengthToMeters;
                    double wallOffset = wallOffsetM / lengthToMeters;
                    double rotationRadians =
                        rotationDegrees * Math.PI / 180.0;
                    double symbolScale =
                        (0.075 / lengthToMeters);
                    double duplicateTolerance =
                        Math.Min(spacingX, spacingY) * 0.10;

                    int addedTotal = 0;
                    int skippedTotal = 0;
                    int invalidBoundaries = 0;
                    var newRows = new List<FireSprinklerLayoutRow>();

                    using (Transaction transaction =
                           database.TransactionManager.StartTransaction())
                    {
                        ObjectId layerId =
                            GetOrCreateFireLayer(
                                database,
                                transaction,
                                layerName);
                        ObjectId blockId =
                            GetOrCreateFireSprinklerBlock(
                                database,
                                transaction,
                                blockName);

                        BlockTableRecord space =
                            transaction.GetObject(
                                database.CurrentSpaceId,
                                OpenMode.ForWrite) as BlockTableRecord;

                        if (space == null)
                            throw new InvalidOperationException(
                                "Không mở được không gian vẽ hiện tại.");

                        List<Point3d> existingPositions =
                            CollectExistingFireSprinklerPositions(
                                space,
                                transaction,
                                blockName,
                                layerName);

                        foreach (SelectedObject selected in selection.Value)
                        {
                            if (selected == null || !selected.ObjectId.IsValid)
                            {
                                invalidBoundaries++;
                                continue;
                            }

                            Polyline boundary =
                                transaction.GetObject(
                                    selected.ObjectId,
                                    OpenMode.ForRead,
                                    false) as Polyline;

                            if (boundary == null ||
                                !boundary.Closed ||
                                boundary.NumberOfVertices < 3)
                            {
                                invalidBoundaries++;
                                continue;
                            }

                            List<FirePoint2> polygon =
                                SampleFirePolylineBoundary(boundary);

                            if (polygon.Count < 3)
                            {
                                invalidBoundaries++;
                                continue;
                            }

                            List<FirePoint2> positions =
                                GenerateFireSprinklerGrid(
                                    polygon,
                                    spacingX,
                                    spacingY,
                                    wallOffset,
                                    rotationRadians);

                            if (positions.Count > 5000)
                            {
                                throw new InvalidOperationException(
                                    "Một vùng tạo hơn 5.000 đầu phun. " +
                                    "Hãy kiểm tra đơn vị hoặc khoảng cách bố trí.");
                            }

                            int addedForBoundary = 0;

                            foreach (FirePoint2 position in positions)
                            {
                                var point =
                                    new Point3d(
                                        position.X,
                                        position.Y,
                                        boundary.Elevation);

                                bool duplicate =
                                    existingPositions.Any(
                                        existing =>
                                            existing.DistanceTo(point) <=
                                            duplicateTolerance);

                                if (duplicate)
                                {
                                    skippedTotal++;
                                    continue;
                                }

                                var blockReference =
                                    new BlockReference(point, blockId)
                                    {
                                        LayerId = layerId,
                                        Rotation = rotationRadians,
                                        ScaleFactors =
                                            new Scale3d(symbolScale)
                                    };

                                space.AppendEntity(blockReference);
                                transaction.AddNewlyCreatedDBObject(
                                    blockReference,
                                    true);

                                _fireLastInsertedSprinklerIds.Add(
                                    blockReference.ObjectId);
                                existingPositions.Add(point);
                                addedForBoundary++;
                                addedTotal++;
                            }

                            newRows.Add(
                                new FireSprinklerLayoutRow
                                {
                                    Index = 0,
                                    Boundary = "H" + boundary.Handle,
                                    AreaM2 = Math.Abs(boundary.Area) * areaFactor,
                                    AddedHeadCount = addedForBoundary,
                                    CandidateHeadCount = positions.Count,
                                    Spacing =
                                        FireDisplay(spacingXM) + " × " +
                                        FireDisplay(spacingYM) + " m",
                                    RotationDegrees = rotationDegrees,
                                    LayerName = layerName
                                });
                        }

                        transaction.Commit();

                        _fireActualSprinklerCount = existingPositions.Count;
                    }

                    foreach (FireSprinklerLayoutRow row in newRows)
                    {
                        row.Index = _fireSprinklerLayoutRows.Count + 1;
                        _fireSprinklerLayoutRows.Add(row);
                    }

                    DgFireSprinklerLayouts.Items.Refresh();
                    UpdateFireSprinklerLayoutSummary();

                    string status =
                        "Đã bố trí " + addedTotal +
                        " đầu phun trên layer " + layerName +
                        ". Tổng nhận trong không gian vẽ: " +
                        _fireActualSprinklerCount + ".";

                    if (skippedTotal > 0)
                        status += " Bỏ qua " + skippedTotal + " vị trí trùng.";

                    if (invalidBoundaries > 0)
                        status += " Có " + invalidBoundaries + " vùng không hợp lệ.";

                    status += " Đơn vị: " + unitDescription + ".";

                    if (unitAssumed)
                        status += " INSUNITS chưa khai báo; đang giả định mm.";

                    SetFireDesignStatus(
                        status,
                        isError: addedTotal == 0,
                        isSuccess: addedTotal > 0);
                }
            }
            catch (Exception ex)
            {
                SetFireDesignStatus(
                    "Không bố trí được đầu phun: " + ex.Message,
                    isError: true);
            }
        }

        private void BtnFireCountSprinklers_Click(
            object sender,
            RoutedEventArgs e)
        {
            Document doc =
                Autodesk.AutoCAD.ApplicationServices.Core.Application
                    .DocumentManager
                    .MdiActiveDocument;

            if (doc == null)
            {
                SetFireDesignStatus(
                    "Không tìm thấy bản vẽ AutoCAD đang mở.",
                    isError: true);
                return;
            }

            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();

            try
            {
                using (doc.LockDocument())
                {
                    var options = new PromptSelectionOptions
                    {
                        MessageForAdding =
                            "\nQuét chọn các block đầu phun cần đếm: "
                    };

                    TypedValue[] filterValues =
                    {
                        new TypedValue((int)DxfCode.Start, "INSERT")
                    };

                    PromptSelectionResult selection =
                        doc.Editor.GetSelection(
                            options,
                            new SelectionFilter(filterValues));

                    if (selection.Status != PromptStatus.OK ||
                        selection.Value == null)
                    {
                        return;
                    }

                    int count = 0;
                    string configuredBlockName =
                        NormalizeFireSymbolName(
                            TxtFireSprinklerBlockName?.Text,
                            "PCCC_SPRINKLER_AUTO");

                    using (Transaction transaction =
                           doc.Database.TransactionManager.StartTransaction())
                    {
                        foreach (SelectedObject selected in selection.Value)
                        {
                            BlockReference block =
                                selected == null
                                    ? null
                                    : transaction.GetObject(
                                        selected.ObjectId,
                                        OpenMode.ForRead,
                                        false) as BlockReference;

                            if (block == null)
                                continue;

                            if (IsFireSprinklerBlock(
                                    block,
                                    transaction,
                                    configuredBlockName))
                                count++;
                        }

                        transaction.Commit();
                    }

                    _fireActualSprinklerCount = count;
                    UpdateFireSprinklerLayoutSummary();

                    SetFireDesignStatus(
                        count > 0
                            ? "Đã đếm " + count + " block đầu phun."
                            : "Không nhận được block đầu phun trong vùng chọn. " +
                              "Tên block/layer nên chứa SPRINKLER hoặc DAU_PHUN.",
                        isError: count == 0,
                        isSuccess: count > 0);
                }
            }
            catch (Exception ex)
            {
                SetFireDesignStatus(
                    "Không đếm được đầu phun: " + ex.Message,
                    isError: true);
            }
        }

        private void BtnFireClearSprinklerLayoutResults_Click(
            object sender,
            RoutedEventArgs e)
        {
            _fireSprinklerLayoutRows.Clear();
            _fireLastInsertedSprinklerIds.Clear();
            _fireActualSprinklerCount = 0;
            UpdateFireSprinklerLayoutSummary();

            SetFireDesignStatus(
                "Đã xóa danh sách kết quả bố trí; các block trên CAD vẫn được giữ.",
                isError: false);
        }

        private void UpdateFireSprinklerLayoutSummary()
        {
            if (TxtFireSprinklerLayoutSummary == null)
                return;

            int added =
                _fireSprinklerLayoutRows.Sum(x => x.AddedHeadCount);

            TxtFireSprinklerLayoutSummary.Text =
                "Vùng đã xử lý: " + _fireSprinklerLayoutRows.Count +
                " · Đầu vừa tạo: " + added +
                " · Số đầu dùng tính: " + _fireActualSprinklerCount +
                ". Cần kiểm tra vùng che khuất, trần, dầm và khoảng cách thực tế.";
        }

        private static ObjectId GetOrCreateFireLayer(
            Database database,
            Transaction transaction,
            string layerName)
        {
            LayerTable layerTable =
                transaction.GetObject(
                    database.LayerTableId,
                    OpenMode.ForRead) as LayerTable;

            if (layerTable == null)
                throw new InvalidOperationException("Không mở được bảng layer.");

            if (layerTable.Has(layerName))
                return layerTable[layerName];

            layerTable.UpgradeOpen();

            var record = new LayerTableRecord
            {
                Name = layerName,
                Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                    Autodesk.AutoCAD.Colors.ColorMethod.ByAci,
                    1)
            };

            ObjectId id = layerTable.Add(record);
            transaction.AddNewlyCreatedDBObject(record, true);
            return id;
        }

        private static ObjectId GetOrCreateFireSprinklerBlock(
            Database database,
            Transaction transaction,
            string blockName)
        {
            BlockTable blockTable =
                transaction.GetObject(
                    database.BlockTableId,
                    OpenMode.ForRead) as BlockTable;

            if (blockTable == null)
                throw new InvalidOperationException("Không mở được bảng block.");

            if (blockTable.Has(blockName))
                return blockTable[blockName];

            blockTable.UpgradeOpen();

            var definition = new BlockTableRecord
            {
                Name = blockName,
                Origin = Point3d.Origin
            };

            ObjectId definitionId = blockTable.Add(definition);
            transaction.AddNewlyCreatedDBObject(definition, true);

            var circle =
                new Circle(
                    Point3d.Origin,
                    Vector3d.ZAxis,
                    1.0);
            var horizontal =
                new Line(
                    new Point3d(-1.35, 0, 0),
                    new Point3d(1.35, 0, 0));
            var vertical =
                new Line(
                    new Point3d(0, -1.35, 0),
                    new Point3d(0, 1.35, 0));

            definition.AppendEntity(circle);
            definition.AppendEntity(horizontal);
            definition.AppendEntity(vertical);
            transaction.AddNewlyCreatedDBObject(circle, true);
            transaction.AddNewlyCreatedDBObject(horizontal, true);
            transaction.AddNewlyCreatedDBObject(vertical, true);

            return definitionId;
        }

        private static List<Point3d> CollectExistingFireSprinklerPositions(
            BlockTableRecord space,
            Transaction transaction,
            string blockName,
            string layerName)
        {
            var positions = new List<Point3d>();

            foreach (ObjectId id in space)
            {
                BlockReference block =
                    transaction.GetObject(
                        id,
                        OpenMode.ForRead,
                        false) as BlockReference;

                if (block == null)
                    continue;

                string currentName =
                    ResolveFireBlockName(block, transaction);

                if (string.Equals(
                        currentName,
                        blockName,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        block.Layer,
                        layerName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    positions.Add(block.Position);
                }
            }

            return positions;
        }

        private static bool IsFireSprinklerBlock(
            BlockReference block,
            Transaction transaction,
            string configuredBlockName)
        {
            string resolvedName =
                ResolveFireBlockName(block, transaction);
            string name =
                NormalizeFireDrawingTextForMatch(
                    resolvedName);
            string layer =
                NormalizeFireDrawingTextForMatch(block.Layer);

            return string.Equals(
                       resolvedName,
                       configuredBlockName,
                       StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("SPRINKLER") ||
                   name.Contains("DAU PHUN") ||
                   layer.Contains("SPRINKLER") ||
                   layer.Contains("DAU PHUN");
        }

        private static string ResolveFireBlockName(
            BlockReference block,
            Transaction transaction)
        {
            try
            {
                ObjectId definitionId =
                    block.IsDynamicBlock
                        ? block.DynamicBlockTableRecord
                        : block.BlockTableRecord;

                BlockTableRecord definition =
                    transaction.GetObject(
                        definitionId,
                        OpenMode.ForRead,
                        false) as BlockTableRecord;

                return definition?.Name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static List<FirePoint2> SampleFirePolylineBoundary(
            Polyline polyline)
        {
            var points = new List<FirePoint2>();
            int vertexCount = polyline.NumberOfVertices;

            for (int segment = 0; segment < vertexCount; segment++)
            {
                int samples =
                    Math.Abs(polyline.GetBulgeAt(segment)) > 0.000001
                        ? 12
                        : 1;

                for (int sample = 0; sample < samples; sample++)
                {
                    double parameter =
                        segment + sample / (double)samples;

                    try
                    {
                        Point3d point =
                            polyline.GetPointAtParameter(parameter);
                        points.Add(new FirePoint2(point.X, point.Y));
                    }
                    catch
                    {
                        Point2d point = polyline.GetPoint2dAt(segment);
                        points.Add(new FirePoint2(point.X, point.Y));
                        break;
                    }
                }
            }

            return RemoveConsecutiveDuplicateFirePoints(points);
        }

        private static List<FirePoint2> RemoveConsecutiveDuplicateFirePoints(
            List<FirePoint2> source)
        {
            var result = new List<FirePoint2>();

            foreach (FirePoint2 point in source)
            {
                if (result.Count == 0 ||
                    FirePointDistance(result[result.Count - 1], point) >
                    0.000001)
                {
                    result.Add(point);
                }
            }

            return result;
        }

        private static List<FirePoint2> GenerateFireSprinklerGrid(
            List<FirePoint2> polygon,
            double maxSpacingX,
            double maxSpacingY,
            double targetWallOffset,
            double rotationRadians)
        {
            FirePoint2 center =
                new FirePoint2(
                    polygon.Average(x => x.X),
                    polygon.Average(x => x.Y));

            List<FirePoint2> localPolygon =
                polygon
                    .Select(
                        x => RotateFirePoint(
                            x,
                            center,
                            -rotationRadians))
                    .ToList();

            double minX = localPolygon.Min(x => x.X);
            double maxX = localPolygon.Max(x => x.X);
            double minY = localPolygon.Min(x => x.Y);
            double maxY = localPolygon.Max(x => x.Y);

            double estimatedCandidates =
                (Math.Ceiling((maxX - minX) / maxSpacingX) + 2.0) *
                (Math.Ceiling((maxY - minY) / maxSpacingY) + 2.0);

            if (!double.IsFinite(estimatedCandidates) ||
                estimatedCandidates > 100000)
            {
                throw new InvalidOperationException(
                    "Lưới dự kiến quá 100.000 điểm. " +
                    "Hãy kiểm tra đơn vị và khoảng cách X/Y.");
            }

            List<double> xPositions =
                BuildFireGridAxis(
                    minX,
                    maxX,
                    maxSpacingX,
                    targetWallOffset);
            List<double> yPositions =
                BuildFireGridAxis(
                    minY,
                    maxY,
                    maxSpacingY,
                    targetWallOffset);

            var result = new List<FirePoint2>();

            foreach (double x in xPositions)
            {
                foreach (double y in yPositions)
                {
                    var local = new FirePoint2(x, y);

                    if (!IsFirePointInsidePolygon(local, localPolygon))
                        continue;

                    result.Add(
                        RotateFirePoint(
                            local,
                            center,
                            rotationRadians));
                }
            }

            return result;
        }

        private static List<double> BuildFireGridAxis(
            double minimum,
            double maximum,
            double maxSpacing,
            double targetOffset)
        {
            double width = maximum - minimum;

            if (width <= 0 || maxSpacing <= 0)
                return new List<double>();

            double offset =
                Math.Max(
                    0,
                    Math.Min(targetOffset, width / 2.0));

            if (width <= 2.0 * offset + 0.000001)
                return new List<double> { (minimum + maximum) / 2.0 };

            double first = minimum + offset;
            double last = maximum - offset;
            double span = last - first;

            if (span <= 0.000001)
                return new List<double> { (minimum + maximum) / 2.0 };

            int intervalCount =
                Math.Max(1, (int)Math.Ceiling(span / maxSpacing));
            double actualSpacing = span / intervalCount;
            var positions = new List<double>();

            for (int index = 0; index <= intervalCount; index++)
                positions.Add(first + index * actualSpacing);

            return positions;
        }

        private static bool IsFirePointInsidePolygon(
            FirePoint2 point,
            List<FirePoint2> polygon)
        {
            bool inside = false;

            for (int current = 0, previous = polygon.Count - 1;
                 current < polygon.Count;
                 previous = current++)
            {
                FirePoint2 a = polygon[current];
                FirePoint2 b = polygon[previous];

                if (FirePointOnSegment(point, a, b))
                    return true;

                bool intersects =
                    ((a.Y > point.Y) != (b.Y > point.Y)) &&
                    (point.X <
                     (b.X - a.X) * (point.Y - a.Y) /
                     ((b.Y - a.Y) == 0 ? double.Epsilon : (b.Y - a.Y)) +
                     a.X);

                if (intersects)
                    inside = !inside;
            }

            return inside;
        }

        private static bool FirePointOnSegment(
            FirePoint2 point,
            FirePoint2 start,
            FirePoint2 end)
        {
            double cross =
                (point.Y - start.Y) * (end.X - start.X) -
                (point.X - start.X) * (end.Y - start.Y);

            if (Math.Abs(cross) > 0.000001)
                return false;

            double dot =
                (point.X - start.X) * (end.X - start.X) +
                (point.Y - start.Y) * (end.Y - start.Y);

            if (dot < 0)
                return false;

            double squaredLength =
                Math.Pow(end.X - start.X, 2) +
                Math.Pow(end.Y - start.Y, 2);

            return dot <= squaredLength;
        }

        private static FirePoint2 RotateFirePoint(
            FirePoint2 point,
            FirePoint2 center,
            double radians)
        {
            double cosine = Math.Cos(radians);
            double sine = Math.Sin(radians);
            double dx = point.X - center.X;
            double dy = point.Y - center.Y;

            return new FirePoint2(
                center.X + dx * cosine - dy * sine,
                center.Y + dx * sine + dy * cosine);
        }

        private static double FirePointDistance(
            FirePoint2 first,
            FirePoint2 second)
        {
            double dx = first.X - second.X;
            double dy = first.Y - second.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static string NormalizeFireSymbolName(
            string value,
            string fallback)
        {
            string name =
                string.IsNullOrWhiteSpace(value)
                    ? fallback
                    : value.Trim();

            name = Regex.Replace(name, @"[^A-Za-z0-9_$\-]", "_");

            return string.IsNullOrWhiteSpace(name)
                ? fallback
                : name;
        }

        private static bool TryParseFiniteFireNumber(
            string text,
            out double value)
        {
            string normalized =
                (text ?? string.Empty)
                    .Trim()
                    .Replace(" ", string.Empty)
                    .Replace(',', '.');

            return double.TryParse(
                       normalized,
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out value) &&
                   double.IsFinite(value);
        }
    }

    internal sealed class FireHydraulicSettings
    {
        public double HazenC { get; set; }
        public double MaxVelocityMps { get; set; }
        public double MaxLossMetersPer100M { get; set; }
        public double CriticalPathLengthM { get; set; }
        public double FittingsAllowancePercent { get; set; }
        public int HeadsPerBranch { get; set; }
    }

    internal sealed class FirePipeSelection
    {
        public int DnMm { get; set; }
        public double FlowLps { get; set; }
        public double VelocityMps { get; set; }
        public double LossMetersPer100M { get; set; }
        public bool MeetsCriteria { get; set; }
    }

    internal sealed class FireHydraulicSummary
    {
        public FireHydraulicSettings Settings { get; set; }
        public FirePipeSelection Branch { get; set; }
        public FirePipeSelection SprinklerMain { get; set; }
        public FirePipeSelection IndoorHydrantMain { get; set; }
        public FirePipeSelection OutdoorHydrantMain { get; set; }
        public FirePipeSelection CombinedMain { get; set; }
        public double EquivalentCriticalLengthM { get; set; }
        public double CriticalPathFrictionLossM { get; set; }
    }

    internal sealed class FireSprinklerLayoutRow
    {
        public int Index { get; set; }
        public string Boundary { get; set; }
        public double AreaM2 { get; set; }
        public int AddedHeadCount { get; set; }
        public int CandidateHeadCount { get; set; }
        public string Spacing { get; set; }
        public double RotationDegrees { get; set; }
        public string LayerName { get; set; }
    }

    internal readonly struct FirePoint2
    {
        public FirePoint2(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }
        public double Y { get; }
    }
}
