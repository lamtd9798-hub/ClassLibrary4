// FIX-CS0104-20260829: khóa rõ Region của AutoCAD và Brushes của WPF.
#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

using AcadRegion = Autodesk.AutoCAD.DatabaseServices.Region;
using WpfBrushes = System.Windows.Media.Brushes;

namespace ClassLibrary4
{
    /// <summary>
    /// THIẾT KẾ PCCC - BƯỚC 1.
    ///
    /// Phần này chỉ đọc và chuẩn hóa dữ liệu hình học từ bản vẽ.
    /// Nó chưa tự kết luận yêu cầu PCCC hoặc thay thế việc kiểm tra của kỹ sư.
    /// Tách thành partial file để những lần nâng cấp module thiết kế không làm
    /// BOCTACHUI.xaml.cs hiện tại lớn thêm và dễ phát sinh xung đột.
    /// </summary>
    public partial class BOCTACHUI
    {
        private readonly ObservableCollection<FireDesignAreaRow>
            _fireDesignAreas = new ObservableCollection<FireDesignAreaRow>();

        private readonly HashSet<string> _fireDesignAreaKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private bool _fireDesignUiInitialized;

        private void FireDesignTab_Loaded(object sender, RoutedEventArgs e)
        {
            if (_fireDesignUiInitialized)
                return;

            _fireDesignUiInitialized = true;
            InitializeMultiSystemDesignUi();
            DgFireAreas.ItemsSource = _fireDesignAreas;
            UpdateFireDesignAreaSummary();
        }

        private void BtnFireReadAreas_Click(object sender, RoutedEventArgs e)
        {
            Document doc =
                Autodesk.AutoCAD.ApplicationServices.Core.Application
                    .DocumentManager
                    .MdiActiveDocument;

            if (doc == null)
            {
                ShowFireDesignMessage(
                    "Không tìm thấy bản vẽ AutoCAD đang mở.",
                    MessageBoxImage.Warning);
                return;
            }

            if (!_fireDesignUiInitialized)
                FireDesignTab_Loaded(this, new RoutedEventArgs());

            Editor ed = doc.Editor;
            Database db = doc.Database;

            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();

            int addedCount = 0;
            int duplicateCount = 0;
            int invalidCount = 0;
            bool subtract = IsFireDesignSubtractMode();

            try
            {
                using (doc.LockDocument())
                {
                    PromptSelectionOptions options =
                        new PromptSelectionOptions
                        {
                            MessageForAdding =
                                "\nQuét chọn Polyline kín, Hatch, Region hoặc Circle " +
                                "dùng để tính diện tích: "
                        };

                    TypedValue[] values =
                    {
                        new TypedValue(
                            (int)DxfCode.Start,
                            "LWPOLYLINE,POLYLINE,HATCH,REGION,CIRCLE")
                    };

                    PromptSelectionResult selection =
                        ed.GetSelection(
                            options,
                            new SelectionFilter(values));

                    if (selection.Status != PromptStatus.OK ||
                        selection.Value == null ||
                        selection.Value.Count == 0)
                    {
                        SetFireDesignStatus(
                            "Đã hủy quét hoặc chưa chọn được vùng nào.",
                            isError: false);
                        return;
                    }

                    double areaToSquareMeters =
                        ResolveFireDesignAreaFactor(
                            db,
                            out string unitDescription,
                            out bool unitWasAssumed);

                    using (Transaction transaction =
                        db.TransactionManager.StartTransaction())
                    {
                        foreach (SelectedObject selected in selection.Value)
                        {
                            if (selected == null ||
                                selected.ObjectId.IsNull ||
                                !selected.ObjectId.IsValid ||
                                selected.ObjectId.IsErased)
                            {
                                invalidCount++;
                                continue;
                            }

                            Entity entity = null;

                            try
                            {
                                entity = transaction.GetObject(
                                    selected.ObjectId,
                                    OpenMode.ForRead,
                                    false) as Entity;
                            }
                            catch
                            {
                                invalidCount++;
                                continue;
                            }

                            if (entity == null ||
                                !TryGetFireDesignPlanArea(
                                    entity,
                                    out double drawingArea,
                                    out string entityType))
                            {
                                invalidCount++;
                                continue;
                            }

                            double areaM2 =
                                Math.Abs(drawingArea) * areaToSquareMeters;

                            if (double.IsNaN(areaM2) ||
                                double.IsInfinity(areaM2) ||
                                areaM2 <= 0.000001)
                            {
                                invalidCount++;
                                continue;
                            }

                            string sourceKey =
                                BuildFireDesignAreaKey(
                                    db,
                                    entity);

                            if (!_fireDesignAreaKeys.Add(sourceKey))
                            {
                                duplicateCount++;
                                continue;
                            }

                            _fireDesignAreas.Add(
                                new FireDesignAreaRow
                                {
                                    SourceKey = sourceKey,
                                    AreaName =
                                        "Vùng " +
                                        (_fireDesignAreas.Count + 1)
                                            .ToString(CultureInfo.InvariantCulture),
                                    LayerName =
                                        string.IsNullOrWhiteSpace(entity.Layer)
                                            ? "0"
                                            : entity.Layer,
                                    EntityType = entityType,
                                    AreaM2 = areaM2,
                                    IsSubtraction = subtract
                                });

                            addedCount++;
                        }

                        transaction.Commit();
                    }

                    RefreshFireDesignAreaIndexes();
                    UpdateFireDesignAreaSummary();

                    string action = subtract ? "trừ" : "cộng";
                    string status =
                        "Đã " + action + " " + addedCount +
                        " vùng hợp lệ. Đơn vị: " + unitDescription + ".";

                    if (duplicateCount > 0)
                    {
                        status +=
                            " Bỏ qua " + duplicateCount +
                            " vùng đã có trong bảng.";
                    }

                    if (invalidCount > 0)
                    {
                        status +=
                            " Có " + invalidCount +
                            " đối tượng không kín hoặc không đọc được diện tích.";
                    }

                    if (unitWasAssumed)
                    {
                        status +=
                            " Bản vẽ chưa khai báo INSUNITS; phần mềm đang tạm " +
                            "coi đơn vị là mm. Hãy kiểm tra lại mục Đơn vị bản vẽ.";
                    }

                    SetFireDesignStatus(
                        status,
                        isError: addedCount == 0);

                    ed.WriteMessage("\n" + status);
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                SetFireDesignStatus(
                    "AutoCAD không thể đọc vùng đã chọn: " + ex.Message,
                    isError: true);
            }
            catch (Exception ex)
            {
                SetFireDesignStatus(
                    "Không thể tính diện tích: " + ex.Message,
                    isError: true);
            }
        }

        private void BtnFireDeleteSelectedAreas_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (DgFireAreas == null ||
                DgFireAreas.SelectedItems.Count == 0)
            {
                SetFireDesignStatus(
                    "Hãy chọn một hoặc nhiều dòng cần xóa.",
                    isError: false);
                return;
            }

            List<FireDesignAreaRow> selectedRows =
                DgFireAreas.SelectedItems
                    .OfType<FireDesignAreaRow>()
                    .ToList();

            foreach (FireDesignAreaRow row in selectedRows)
            {
                _fireDesignAreas.Remove(row);
                _fireDesignAreaKeys.Remove(row.SourceKey ?? string.Empty);
            }

            RefreshFireDesignAreaIndexes();
            UpdateFireDesignAreaSummary();
            SetFireDesignStatus(
                "Đã xóa " + selectedRows.Count + " vùng khỏi bảng.",
                isError: false);
        }

        private void BtnFireClearAreas_Click(object sender, RoutedEventArgs e)
        {
            _fireDesignAreas.Clear();
            _fireDesignAreaKeys.Clear();
            UpdateFireDesignAreaSummary();
            SetFireDesignStatus(
                "Đã xóa toàn bộ kết quả đọc mặt bằng.",
                isError: false);
        }

        private void BtnFireValidateInputs_Click(
            object sender,
            RoutedEventArgs e)
        {
            List<string> missing = new List<string>();

            if (string.IsNullOrWhiteSpace(TxtFireProjectName.Text))
                missing.Add("tên dự án");

            string projectUse =
                (CmbFireProjectUse.SelectedItem as ComboBoxItem)
                    ?.Tag
                    ?.ToString();

            if (string.IsNullOrWhiteSpace(projectUse))
                missing.Add("công năng chính");

            if (!int.TryParse(
                    (TxtFireFloorCount.Text ?? string.Empty).Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int floorCount) ||
                floorCount <= 0)
            {
                missing.Add("số tầng hợp lệ");
            }

            if (!TryParsePositiveFireDesignNumber(
                    TxtFireBuildingHeight.Text,
                    out _))
            {
                missing.Add("chiều cao nhà");
            }

            double totalArea =
                _fireDesignAreas.Sum(x => x.SignedAreaM2);

            if (_fireDesignAreas.Count == 0 || totalArea <= 0.0)
                missing.Add("diện tích mặt bằng");

            if (missing.Count > 0)
            {
                SetFireDesignStatus(
                    "Chưa đủ dữ liệu: " +
                    string.Join(", ", missing) +
                    ". Hãy bổ sung trước khi sang bước tra yêu cầu tiêu chuẩn.",
                    isError: true);
                return;
            }

            SetFireDesignStatus(
                "Dữ liệu cơ bản đã hợp lệ: " +
                floorCount + " tầng, tổng diện tích " +
                totalArea.ToString("N2", CultureInfo.CurrentCulture) +
                " m². Bước tiếp theo sẽ đọc hồ sơ chủ đầu tư và xác định " +
                "bộ tiêu chuẩn áp dụng; đây chưa phải kết luận thiết kế PCCC.",
                isError: false,
                isSuccess: true);
        }

        private bool IsFireDesignSubtractMode()
        {
            string tag =
                (CmbFireAreaOperation.SelectedItem as ComboBoxItem)
                    ?.Tag
                    ?.ToString();

            return string.Equals(
                tag,
                "SUBTRACT",
                StringComparison.OrdinalIgnoreCase);
        }

        private double ResolveFireDesignAreaFactor(
            Database database,
            out string unitDescription,
            out bool unitWasAssumed)
        {
            unitWasAssumed = false;

            string selectedUnit =
                (CmbFireDrawingUnit.SelectedItem as ComboBoxItem)
                    ?.Tag
                    ?.ToString() ?? "AUTO";

            double lengthToMeters;

            switch (selectedUnit.ToUpperInvariant())
            {
                case "M":
                    unitDescription = "m";
                    lengthToMeters = 1.0;
                    break;

                case "CM":
                    unitDescription = "cm";
                    lengthToMeters = 0.01;
                    break;

                case "MM":
                    unitDescription = "mm";
                    lengthToMeters = 0.001;
                    break;

                default:
                    // Giá trị INSUNITS thường dùng của AutoCAD:
                    // 0 Unitless, 1 Inches, 2 Feet, 4 Millimeters,
                    // 5 Centimeters, 6 Meters.
                    switch ((int)database.Insunits)
                    {
                        case 1:
                            unitDescription = "inch (INSUNITS)";
                            lengthToMeters = 0.0254;
                            break;

                        case 2:
                            unitDescription = "feet (INSUNITS)";
                            lengthToMeters = 0.3048;
                            break;

                        case 5:
                            unitDescription = "cm (INSUNITS)";
                            lengthToMeters = 0.01;
                            break;

                        case 6:
                            unitDescription = "m (INSUNITS)";
                            lengthToMeters = 1.0;
                            break;

                        case 4:
                            unitDescription = "mm (INSUNITS)";
                            lengthToMeters = 0.001;
                            break;

                        default:
                            unitDescription = "mm (tạm giả định)";
                            lengthToMeters = 0.001;
                            unitWasAssumed = true;
                            break;
                    }
                    break;
            }

            return lengthToMeters * lengthToMeters;
        }

        private static bool TryGetFireDesignPlanArea(
            Entity entity,
            out double drawingArea,
            out string entityType)
        {
            drawingArea = 0.0;
            entityType = string.Empty;

            try
            {
                if (entity is Polyline polyline)
                {
                    if (!polyline.Closed)
                        return false;

                    drawingArea = polyline.Area;
                    entityType = "Polyline kín";
                    return Math.Abs(drawingArea) > 0.0;
                }

                if (entity is Polyline2d polyline2d)
                {
                    if (!polyline2d.Closed)
                        return false;

                    drawingArea = polyline2d.Area;
                    entityType = "Polyline 2D kín";
                    return Math.Abs(drawingArea) > 0.0;
                }

                if (entity is Hatch hatch)
                {
                    drawingArea = hatch.Area;
                    entityType = "Hatch";
                    return Math.Abs(drawingArea) > 0.0;
                }

                if (entity is AcadRegion region)
                {
                    drawingArea = region.Area;
                    entityType = "Region";
                    return Math.Abs(drawingArea) > 0.0;
                }

                if (entity is Circle circle)
                {
                    drawingArea = Math.PI * circle.Radius * circle.Radius;
                    entityType = "Circle";
                    return Math.Abs(drawingArea) > 0.0;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static string BuildFireDesignAreaKey(
            Database database,
            Entity entity)
        {
            string drawing =
                string.IsNullOrWhiteSpace(database.Filename)
                    ? "UNSAVED_DRAWING"
                    : database.Filename;

            return drawing + "|" + entity.Handle;
        }

        private void RefreshFireDesignAreaIndexes()
        {
            for (int i = 0; i < _fireDesignAreas.Count; i++)
                _fireDesignAreas[i].Index = i + 1;

            DgFireAreas?.Items.Refresh();
        }

        private void UpdateFireDesignAreaSummary()
        {
            if (TxtFireAreaCount == null || TxtFireTotalArea == null)
                return;

            double total =
                _fireDesignAreas.Sum(x => x.SignedAreaM2);

            TxtFireAreaCount.Text =
                _fireDesignAreas.Count
                    .ToString(CultureInfo.CurrentCulture);

            TxtFireTotalArea.Text =
                total.ToString("N2", CultureInfo.CurrentCulture) + " m²";

            // Đồng bộ vùng đã quét sang phần tính toán tổng hợp. Nếu chưa có
            // vùng, người dùng vẫn có thể nhập diện tích thủ công ở bước 6.
            if (TxtFireCalcTotalArea != null && total > 0.0)
            {
                TxtFireCalcTotalArea.Text =
                    total.ToString("0.##", CultureInfo.InvariantCulture);
            }
        }

        private void SetFireDesignStatus(
            string message,
            bool isError,
            bool isSuccess = false)
        {
            if (TxtFireDesignStatus == null)
                return;

            TxtFireDesignStatus.Text = message ?? string.Empty;
            TxtFireDesignStatus.Foreground =
                isError
                    ? WpfBrushes.Firebrick
                    : isSuccess
                        ? WpfBrushes.DarkGreen
                        : WpfBrushes.DimGray;
        }

        private static bool TryParsePositiveFireDesignNumber(
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
                   value > 0.0;
        }

        private static void ShowFireDesignMessage(
            string message,
            MessageBoxImage image)
        {
            System.Windows.MessageBox.Show(
                message,
                "THIẾT KẾ PCCC",
                MessageBoxButton.OK,
                image);
        }
    }

    internal sealed class FireDesignAreaRow
    {
        public int Index { get; set; }

        public string AreaName { get; set; }

        public string LayerName { get; set; }

        public string EntityType { get; set; }

        public double AreaM2 { get; set; }

        public bool IsSubtraction { get; set; }

        public string SourceKey { get; set; }

        public double SignedAreaM2 =>
            IsSubtraction ? -AreaM2 : AreaM2;
    }
}

