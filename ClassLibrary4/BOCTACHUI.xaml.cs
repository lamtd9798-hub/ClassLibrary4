#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfComboBoxItem = System.Windows.Controls.ComboBoxItem;
using WpfFrameworkElement = System.Windows.FrameworkElement;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfListBoxItem = System.Windows.Controls.ListBoxItem;
using WpfTabControl = System.Windows.Controls.TabControl;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfUserControl = System.Windows.Controls.UserControl;
using WpfKey = System.Windows.Input.Key;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using WinFormsDialogResult = System.Windows.Forms.DialogResult;

namespace ClassLibrary4
{
    public partial class BOCTACHUI : WpfUserControl
    {
        private readonly Dictionary<ObjectId, Autodesk.AutoCAD.Colors.Color> mauGocCuaBlock =
            new Dictionary<ObjectId, Autodesk.AutoCAD.Colors.Color>();

        private readonly Dictionary<string, short> _userCustomColors =
            new Dictionary<string, short>();

        private const double ManualMinimumLabelSegmentLength = 3000.0;
        private const double MinimumLabelTextHeight = 67.0;
        private const double LabelTextHeightToWidthRatio = 2.0 / 9.0;
        private const double AutomaticLabelScale = 2.2;
        // Độ dày nét cố định cho ống DN và ống đồng (không phụ thuộc size)
        private const double FixedDnPipeDisplayWidth = 50.0;
        private const string LayerChangeBuild = "DL-20260808-12";
        private const string AutoConvertBuild = "AUTO-20260807-10";
        private const string TemplateAutoDrawBuild = "MAU-20260807-02";
        private const double SprinklerCenterSearchDistance = 500.0;
        private const double TemplateLengthToleranceRatio = 0.05;
        private const double TemplateAbsoluteLengthTolerance = 100.0;
        private const double TemplateShapeToleranceRatio = 0.02;
        private const double TemplateMarkerSearchDistance = 750.0;
        private const double TemplateMarkerMatchTolerance = 300.0;
        private const double TemplateConnectionTolerance = 150.0;
        private const double TemplateDuplicateTolerance = 100.0;

        // SHOP thông minh: gom các đầu/điểm giao thành nút rồi mới quyết định phụ kiện.
        private const string ShopSmartBuild = "SHOP-SMART-20260809-26-CLEAN-WARNINGS";
        private const double ShopJointTolerance = 150.0;
        private const double ShopDuplicateNodeTolerance = 100.0;
        private const double ShopStraightAngleToleranceDeg = 12.0;
        private const double ShopElbowAngleToleranceDeg = 18.0;
        private const double ShopSprinklerEndSearchDistance = 500.0;
        // Khoảng hở đầu ống tại nút CÓ phụ kiện — vừa đủ để block không đè nét.
        // Không được lớn quá kẻo dư khúc giữa ống và co/giảm.
        private const double ShopFittingGapMin = 12.0;
        private const double ShopFittingGapRatio = 0.25;
        private const double ShopFittingGapMax = 45.0;

        // Góc lệch tối đa (radian) giữa hướng chữ và hướng ống
        // để coi là "song song". ~18 độ ≈ Math.PI / 10.
        // Chữ vuông góc với ống (nhánh rẽ) sẽ bị loại, tránh
        // lấy nhầm DN của nhánh gán cho ống chính dài.
        private const double MaxParallelAngleRadians = Math.PI / 10.0;

        private static readonly double[][] OutsideDiameterToNominalTable =
            new double[][]
            {
                new double[] { 15, 20, 21, 21.2 },
                new double[] { 20, 25, 26.8, 27 },
                new double[] { 25, 32, 33.5, 34 },
                new double[] { 32, 40, 42, 42.2 },
                new double[] { 40, 48.1, 49, 50 },
                new double[] { 50, 60, 60.3, 63 },
                new double[] { 65, 73, 75, 76, 76.1 },
                new double[] { 80, 88.9, 90 },
                new double[] { 100, 110, 114, 114.3 },
                new double[] { 125, 140, 141.3 },
                new double[] { 150, 160, 168, 168.3 },
                new double[] { 200, 219, 219.1, 220, 225 },
                new double[] { 250, 273, 280 },
                new double[] { 300, 315, 323.9, 324 },
                new double[] { 350, 355, 355.6, 356 },
                new double[] { 400, 400, 406.4 }
            };

        private readonly short[] _brightAciColors = new short[]
        {
            1, 2, 3, 4, 6, 30, 40, 50, 80, 90, 110, 120,
            200, 210, 220, 230
        };

        private readonly ObservableCollection<PipeSizeItem> _pipeSizesFF =
            new ObservableCollection<PipeSizeItem>();

        private readonly ObservableCollection<PipeSizeItem> _pipeSizesACMV =
            new ObservableCollection<PipeSizeItem>();

        private readonly ObservableCollection<PipeSizeItem> _pipeSizesCTN =
            new ObservableCollection<PipeSizeItem>();

        private readonly ObservableCollection<PipeSizeItem> _valveSizesFF =
            new ObservableCollection<PipeSizeItem>();

        private readonly ObservableCollection<PipeSizeItem> _valveSizesACMV =
            new ObservableCollection<PipeSizeItem>();

        private readonly ObservableCollection<PipeSizeItem> _valveSizesCTN =
            new ObservableCollection<PipeSizeItem>();

        private readonly ObservableCollection<PipeSizeItem> _equipSizesFF =
            new ObservableCollection<PipeSizeItem>();

        private readonly ObservableCollection<PipeSizeItem> _equipSizesACMV =
            new ObservableCollection<PipeSizeItem>();

        private readonly ObservableCollection<PipeSizeItem> _equipSizesCTN =
            new ObservableCollection<PipeSizeItem>();

        private PipeUiContext _ctxFF;
        private PipeUiContext _ctxACMV;
        private PipeUiContext _ctxCTN;

        private ValveUiContext _valveCtxFF;
        private ValveUiContext _valveCtxACMV;
        private ValveUiContext _valveCtxCTN;

        private EquipUiContext _equipCtxFF;
        private EquipUiContext _equipCtxACMV;
        private EquipUiContext _equipCtxCTN;

        private bool _isWaitingForPline = false;
        private Document _plineWatcherDocument;
        private ObjectId _lastPlineId = ObjectId.Null;

        private readonly HashSet<ObjectId> _pendingPlineIds =
            new HashSet<ObjectId>();

        private readonly HashSet<ObjectId> _processedPlineIds =
            new HashSet<ObjectId>();

        private string _currentLayerNameForText = "";
        private double _currentPlineWidth = 0;

        public BOCTACHUI()
        {
            InitializeComponent();
            KhoiTaoTatCaTabOng();
            KhoiTaoTatCaTabVan();
            KhoiTaoTatCaTabThietBi();
        }

        private WpfComboBox TimComboBox(string name)
        {
            return FindName(name) as WpfComboBox;
        }

        private WpfListBox TimListBox(string name)
        {
            return FindName(name) as WpfListBox;
        }

        private WpfTextBox TimTextBox(string name)
        {
            return FindName(name) as WpfTextBox;
        }

        private void KhoiTaoTatCaTabOng()
        {
            _ctxFF = new PipeUiContext
            {
                Suffix = "",
                HeThongMacDinh = "Chữa cháy _ FF",
                CmbHeThong = TimComboBox("CmbHeThong"),
                CmbVatLieu = TimComboBox("CmbVatLieuOng"),
                LstVatLieu = TimListBox("LstVatLieuOngBang"),
                TxtVatLieuThem = TimTextBox("TxtVatLieuOngBoSung"),
                LstSize = TimListBox("LstSizeOng"),
                TxtSizeThem = TimTextBox("TxtCustomSize"),
                Sizes = _pipeSizesFF
            };

            _ctxACMV = new PipeUiContext
            {
                Suffix = "ACMV",
                HeThongMacDinh = "ACMV _ ACMV",
                CmbHeThong = TimComboBox("CmbHeThongACMV"),
                CmbVatLieu = TimComboBox("CmbVatLieuOngACMV"),
                LstVatLieu = TimListBox("LstVatLieuOngBangACMV"),
                TxtVatLieuThem = TimTextBox("TxtVatLieuOngBoSungACMV"),
                LstSize = TimListBox("LstSizeOngACMV"),
                TxtSizeThem = TimTextBox("TxtCustomSizeACMV"),
                Sizes = _pipeSizesACMV
            };

            _ctxCTN = new PipeUiContext
            {
                Suffix = "CTN",
                HeThongMacDinh = "CTN _ CTN",
                CmbHeThong = TimComboBox("CmbHeThongCTN"),
                CmbVatLieu = TimComboBox("CmbVatLieuOngCTN"),
                LstVatLieu = TimListBox("LstVatLieuOngBangCTN"),
                TxtVatLieuThem = TimTextBox("TxtVatLieuOngBoSungCTN"),
                LstSize = TimListBox("LstSizeOngCTN"),
                TxtSizeThem = TimTextBox("TxtCustomSizeCTN"),
                Sizes = _pipeSizesCTN
            };

            KhoiTaoContext(_ctxFF);
            KhoiTaoContext(_ctxACMV);
            KhoiTaoContext(_ctxCTN);
        }

        private void KhoiTaoTatCaTabVan()
        {
            _valveCtxFF = new ValveUiContext
            {
                Suffix = "",
                HeThongMacDinh = "Chữa cháy _ FF",
                CmbHeThong = TimComboBox("CmbHeThong"),
                LstLoaiVan = TimListBox("LstLoaiVan"),
                TxtLoaiVanThem = TimTextBox("TxtLoaiVanBoSung"),
                LstSize = TimListBox("LstSizeVan"),
                TxtSizeThem = TimTextBox("TxtCustomSizeVan"),
                Sizes = _valveSizesFF
            };

            _valveCtxACMV = new ValveUiContext
            {
                Suffix = "ACMV",
                HeThongMacDinh = "ACMV _ ACMV",
                CmbHeThong = TimComboBox("CmbHeThongACMV"),
                LstLoaiVan = TimListBox("LstLoaiVanACMV"),
                TxtLoaiVanThem = TimTextBox("TxtLoaiVanBoSungACMV"),
                LstSize = TimListBox("LstSizeVanACMV"),
                TxtSizeThem = TimTextBox("TxtCustomSizeVanACMV"),
                Sizes = _valveSizesACMV
            };

            _valveCtxCTN = new ValveUiContext
            {
                Suffix = "CTN",
                HeThongMacDinh = "CTN _ CTN",
                CmbHeThong = TimComboBox("CmbHeThongCTN"),
                LstLoaiVan = TimListBox("LstLoaiVanCTN"),
                TxtLoaiVanThem = TimTextBox("TxtLoaiVanBoSungCTN"),
                LstSize = TimListBox("LstSizeVanCTN"),
                TxtSizeThem = TimTextBox("TxtCustomSizeVanCTN"),
                Sizes = _valveSizesCTN
            };

            KhoiTaoValveContext(_valveCtxFF);
            KhoiTaoValveContext(_valveCtxACMV);
            KhoiTaoValveContext(_valveCtxCTN);
        }

        private void KhoiTaoTatCaTabThietBi()
        {
            _equipCtxFF = new EquipUiContext
            {
                Suffix = "",
                HeThongMacDinh = "Chữa cháy _ FF",
                CmbHeThong = TimComboBox("CmbHeThong"),
                LstLoai = TimListBox("LstLoaiThietBi"),
                TxtLoaiThem = TimTextBox("TxtLoaiThietBiBoSung"),
                LstSize = TimListBox("LstSizeThietBi"),
                TxtSizeThem = TimTextBox("TxtCustomSizeThietBi"),
                Sizes = _equipSizesFF
            };

            _equipCtxACMV = new EquipUiContext
            {
                Suffix = "ACMV",
                HeThongMacDinh = "ACMV _ ACMV",
                CmbHeThong = TimComboBox("CmbHeThongACMV"),
                LstLoai = TimListBox("LstLoaiThietBiACMV"),
                TxtLoaiThem = TimTextBox("TxtLoaiThietBiBoSungACMV"),
                LstSize = TimListBox("LstSizeThietBiACMV"),
                TxtSizeThem = TimTextBox("TxtCustomSizeThietBiACMV"),
                Sizes = _equipSizesACMV
            };

            _equipCtxCTN = new EquipUiContext
            {
                Suffix = "CTN",
                HeThongMacDinh = "CTN _ CTN",
                CmbHeThong = TimComboBox("CmbHeThongCTN"),
                LstLoai = TimListBox("LstLoaiThietBiCTN"),
                TxtLoaiThem = TimTextBox("TxtLoaiThietBiBoSungCTN"),
                LstSize = TimListBox("LstSizeThietBiCTN"),
                TxtSizeThem = TimTextBox("TxtCustomSizeThietBiCTN"),
                Sizes = _equipSizesCTN
            };

            KhoiTaoEquipContext(_equipCtxFF);
            KhoiTaoEquipContext(_equipCtxACMV);
            KhoiTaoEquipContext(_equipCtxCTN);
        }

        private void KhoiTaoEquipContext(EquipUiContext ctx)
        {
            if (ctx == null)
                return;

            if (ctx.LstLoai != null &&
                ctx.LstLoai.SelectedIndex < 0 &&
                ctx.LstLoai.Items.Count > 0)
            {
                ctx.LstLoai.SelectedIndex = 0;
            }

            if (ctx.LstSize != null)
                ctx.LstSize.ItemsSource = ctx.Sizes;

            CapNhatModelTheoLoaiThietBi(ctx);
            CapNhatHienThiPanelThietBiFF(ctx);
            CapNhatHienThiPanelMayLanhACMV(ctx);
        }

        private void CapNhatModelTheoLoaiThietBi(EquipUiContext ctx)
        {
            if (ctx == null)
                return;

            string loai = GetSelectedEquipTypeName(ctx);
            List<string> models = new List<string>();

            string key = (loai ?? "").Trim().ToUpperInvariant();

            if (ctx.Suffix == "ACMV")
            {
                if (key.Contains("MÁY LẠNH") || key.Contains("MAY LANH"))
                {
                    // Máy lạnh dùng panel 3 cột — không cần list model
                    models.Clear();
                }
                else if (key.Contains("QUẠT") || key.Contains("QUAT"))
                {
                    // Quạt dùng panel 3 cột — không cần list model
                    models.Clear();
                }
                else if (key.Contains("BƠM") || key.Contains("BOM"))
                {
                    models.AddRange(new[]
                    {
                        "Bơm ly tâm",
                        "Bơm trục đứng",
                        "Bơm tăng áp"
                    });
                }
                else
                {
                    models.Add("Model 1");
                }
            }
            else if (ctx.Suffix == "CTN")
            {
                if (key.Contains("ĐỒNG HỒ") || key.Contains("DONG HO"))
                {
                    models.AddRange(new[] { "DN15", "DN20", "DN25", "DN32", "DN40", "DN50" });
                }
                else if (key.Contains("BƠM") || key.Contains("BOM"))
                {
                    models.AddRange(new[] { "Bơm tăng áp", "Bơm tuần hoàn" });
                }
                else if (key.Contains("BỒN") || key.Contains("BON"))
                {
                    models.AddRange(new[] { "Bồn inox 500L", "Bồn inox 1000L", "Bồn composite" });
                }
                else
                {
                    models.Add("Model 1");
                }
            }
            else
            {
                // Chữa cháy FF
                if (key.Contains("ĐẦU PHUN") ||
                    key.Contains("DAU PHUN") ||
                    key.Contains("PHUN"))
                {
                    // Đầu phun dùng panel 3 cột — không cần list model
                    models.Clear();
                }
                else if (key.Contains("BÌNH") ||
                         key.Contains("BINH") ||
                         key.Contains("CC"))
                {
                    models.AddRange(new[]
                    {
                        "ABC 4KG",
                        "ABC 6KG",
                        "ABC 8KG",
                        "ABC 9KG",
                        "CO2 3KG",
                        "CO2 5KG",
                        "ABC TREO 6KG",
                        "ABC TREO 8KG"
                    });
                }
                else
                {
                    models.Add("Model 1");
                }
            }

            CapNhatVaSapXepDanhSachSizeEquip(ctx, models);
            CapNhatHienThiPanelThietBiFF(ctx);
            CapNhatHienThiPanelMayLanhACMV(ctx);
        }

        private void CapNhatHienThiPanelMayLanhACMV(EquipUiContext ctx)
        {
            if (ctx == null || ctx.Suffix != "ACMV")
                return;

            var panelMayLanh =
                FindName("PanelMayLanh") as System.Windows.UIElement;
            var panelQuat =
                FindName("PanelQuat") as System.Windows.UIElement;
            var panelKhac =
                FindName("PanelAcmvKhac") as System.Windows.UIElement;

            if (panelMayLanh == null || panelKhac == null)
                return;

            string loai = GetSelectedEquipTypeName(ctx).ToUpperInvariant();
            bool isMayLanh =
                loai.Contains("MÁY LẠNH") || loai.Contains("MAY LANH");
            bool isQuat =
                loai.Contains("QUẠT") || loai.Contains("QUAT");

            panelMayLanh.Visibility = isMayLanh
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

            if (panelQuat != null)
            {
                panelQuat.Visibility = isQuat
                    ? System.Windows.Visibility.Visible
                    : System.Windows.Visibility.Collapsed;
            }

            // Panel list thường chỉ hiện khi không phải Máy lạnh / Quạt (vd: Bơm)
            panelKhac.Visibility = (!isMayLanh && !isQuat)
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
        }

        private string BuildQuatModelText()
        {
            string loaiQuat = "Gắn mái";
            string luuLuong = "1020";
            string cotAp = "50Pa";

            var lstLoai = TimListBox("LstLoaiQuat");
            var lstLuuLuong = TimListBox("LstLuuLuongQuat");
            var lstCotAp = TimListBox("LstCotApQuat");

            if (lstLoai?.SelectedItem != null)
                loaiQuat = LayNoiDungItem(lstLoai.SelectedItem);

            if (lstLuuLuong?.SelectedItem != null)
                luuLuong = LayNoiDungItem(lstLuuLuong.SelectedItem);

            if (lstCotAp?.SelectedItem != null)
                cotAp = LayNoiDungItem(lstCotAp.SelectedItem);

            // Chuẩn hóa đơn vị hiển thị
            if (!luuLuong.ToUpperInvariant().Contains("M3") &&
                !luuLuong.Contains("m³") &&
                !luuLuong.ToUpperInvariant().Contains("M³"))
            {
                luuLuong = $"{luuLuong} m³/h";
            }

            return $"{loaiQuat} {luuLuong} {cotAp}";
        }

        private void TxtQuatCotThem_KeyDown(
            object sender,
            WpfKeyEventArgs e)
        {
            if (e.Key != WpfKey.Enter)
                return;

            WpfTextBox txt = sender as WpfTextBox;
            if (txt == null)
                return;

            string value = (txt.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value))
                return;

            string listName = null;
            string name = txt.Name ?? "";

            if (name.Contains("LoaiQuat"))
                listName = "LstLoaiQuat";
            else if (name.Contains("LuuLuong"))
                listName = "LstLuuLuongQuat";
            else if (name.Contains("CotAp"))
                listName = "LstCotApQuat";

            if (listName == null)
                return;

            WpfListBox lst = TimListBox(listName);
            if (lst == null)
                return;

            bool existed =
                lst.Items
                    .Cast<object>()
                    .Any(x => LayNoiDungItem(x).Equals(
                        value,
                        StringComparison.OrdinalIgnoreCase));

            if (!existed)
            {
                lst.Items.Add(
                    new WpfListBoxItem
                    {
                        Content = value
                    });
            }

            foreach (object item in lst.Items)
            {
                if (LayNoiDungItem(item).Equals(
                    value,
                    StringComparison.OrdinalIgnoreCase))
                {
                    lst.SelectedItem = item;
                    lst.ScrollIntoView(item);
                    break;
                }
            }

            txt.Text = "";
            e.Handled = true;
        }

        private void LstQuatCot_KeyDown(
            object sender,
            WpfKeyEventArgs e)
        {
            if (e.Key != WpfKey.Delete)
                return;

            WpfListBox lst = sender as WpfListBox;
            if (lst?.SelectedItem == null)
                return;

            if (lst.Items.Count <= 1)
                return;

            object selected = lst.SelectedItem;
            lst.Items.Remove(selected);

            if (lst.Items.Count > 0)
                lst.SelectedIndex = 0;

            e.Handled = true;
        }

        private bool _updatingDonViMayLanh = false;

        private void ChkDonViMayLanh_Checked(
            object sender,
            RoutedEventArgs e)
        {
            if (_updatingDonViMayLanh)
                return;

            _updatingDonViMayLanh = true;

            try
            {
                var chkHP =
                    FindName("ChkDonViHP") as System.Windows.Controls.CheckBox;
                var chkKW =
                    FindName("ChkDonViKW") as System.Windows.Controls.CheckBox;
                var lstHP = TimListBox("LstCongSuatHP");
                var lstKW = TimListBox("LstCongSuatKW");
                var txtHP = TimTextBox("TxtCongSuatHPThem");
                var txtKW = TimTextBox("TxtCongSuatKWThem");

                bool useHP = sender == chkHP;

                if (chkHP != null)
                    chkHP.IsChecked = useHP;
                if (chkKW != null)
                    chkKW.IsChecked = !useHP;

                SetMayLanhCapacityEnabled(lstHP, txtHP, useHP);
                SetMayLanhCapacityEnabled(lstKW, txtKW, !useHP);
            }
            finally
            {
                _updatingDonViMayLanh = false;
            }
        }

        private void ChkDonViMayLanh_Unchecked(
            object sender,
            RoutedEventArgs e)
        {
            if (_updatingDonViMayLanh)
                return;

            // Không cho bỏ tích cả hai — nếu uncheck thì bật cái còn lại
            _updatingDonViMayLanh = true;

            try
            {
                var chkHP =
                    FindName("ChkDonViHP") as System.Windows.Controls.CheckBox;
                var chkKW =
                    FindName("ChkDonViKW") as System.Windows.Controls.CheckBox;

                if (sender == chkHP && chkKW != null)
                {
                    chkKW.IsChecked = true;
                    ChkDonViMayLanh_Checked(chkKW, e);
                }
                else if (sender == chkKW && chkHP != null)
                {
                    chkHP.IsChecked = true;
                    ChkDonViMayLanh_Checked(chkHP, e);
                }
            }
            finally
            {
                _updatingDonViMayLanh = false;
            }
        }

        private void SetMayLanhCapacityEnabled(
            WpfListBox list,
            WpfTextBox textBox,
            bool enabled)
        {
            if (list != null)
            {
                list.IsEnabled = enabled;
                list.Opacity = enabled ? 1.0 : 0.45;
            }

            if (textBox != null)
            {
                textBox.IsEnabled = enabled;
                textBox.Opacity = enabled ? 1.0 : 0.45;
            }
        }

        private string BuildMayLanhModelText()
        {
            string loaiMay = "Cassette";
            string congSuat = "1 HP";

            var lstLoai = TimListBox("LstLoaiMayLanh");
            var lstHP = TimListBox("LstCongSuatHP");
            var lstKW = TimListBox("LstCongSuatKW");
            var chkHP =
                FindName("ChkDonViHP") as System.Windows.Controls.CheckBox;

            if (lstLoai?.SelectedItem != null)
                loaiMay = LayNoiDungItem(lstLoai.SelectedItem);

            bool useHP = chkHP?.IsChecked == true;

            if (useHP)
            {
                if (lstHP?.SelectedItem != null)
                    congSuat = LayNoiDungItem(lstHP.SelectedItem);
            }
            else
            {
                if (lstKW?.SelectedItem != null)
                    congSuat = LayNoiDungItem(lstKW.SelectedItem);
            }

            return $"{loaiMay} {congSuat}";
        }

        private void TxtMayLanhCotThem_KeyDown(
            object sender,
            WpfKeyEventArgs e)
        {
            if (e.Key != WpfKey.Enter)
                return;

            WpfTextBox txt = sender as WpfTextBox;
            if (txt == null)
                return;

            string value = (txt.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value))
                return;

            string listName = null;
            string name = txt.Name ?? "";

            if (name.Contains("LoaiMayLanh"))
                listName = "LstLoaiMayLanh";
            else if (name.Contains("HP"))
                listName = "LstCongSuatHP";
            else if (name.Contains("KW"))
                listName = "LstCongSuatKW";

            if (listName == null)
                return;

            WpfListBox lst = TimListBox(listName);
            if (lst == null)
                return;

            bool existed =
                lst.Items
                    .Cast<object>()
                    .Any(x => LayNoiDungItem(x).Equals(
                        value,
                        StringComparison.OrdinalIgnoreCase));

            if (!existed)
            {
                lst.Items.Add(
                    new WpfListBoxItem
                    {
                        Content = value
                    });
            }

            foreach (object item in lst.Items)
            {
                if (LayNoiDungItem(item).Equals(
                    value,
                    StringComparison.OrdinalIgnoreCase))
                {
                    lst.SelectedItem = item;
                    lst.ScrollIntoView(item);
                    break;
                }
            }

            txt.Text = "";
            e.Handled = true;
        }

        private void LstMayLanhCot_KeyDown(
            object sender,
            WpfKeyEventArgs e)
        {
            if (e.Key != WpfKey.Delete)
                return;

            WpfListBox lst = sender as WpfListBox;
            if (lst?.SelectedItem == null)
                return;

            if (lst.Items.Count <= 1)
                return;

            object selected = lst.SelectedItem;
            lst.Items.Remove(selected);

            if (lst.Items.Count > 0)
                lst.SelectedIndex = 0;

            e.Handled = true;
        }

        private void CapNhatHienThiPanelThietBiFF(EquipUiContext ctx)
        {
            // Chỉ áp dụng cho tab Chữa cháy (không có suffix)
            if (ctx == null || !string.IsNullOrEmpty(ctx.Suffix))
                return;

            var panelBinh = FindName("PanelBinhCC") as System.Windows.UIElement;
            var panelPhun = FindName("PanelDauPhun") as System.Windows.UIElement;

            if (panelBinh == null || panelPhun == null)
                return;

            string loai = GetSelectedEquipTypeName(ctx).ToUpperInvariant();
            bool isDauPhun =
                loai.Contains("ĐẦU PHUN") ||
                loai.Contains("DAU PHUN") ||
                loai.Contains("PHUN");

            panelBinh.Visibility = isDauPhun
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;

            panelPhun.Visibility = isDauPhun
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
        }

        private void TxtDauPhunCotThem_KeyDown(
            object sender,
            WpfKeyEventArgs e)
        {
            if (e.Key != WpfKey.Enter)
                return;

            WpfTextBox txt = sender as WpfTextBox;
            if (txt == null)
                return;

            string value = (txt.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value))
                return;

            string listName = null;
            string name = txt.Name ?? "";

            if (name.Contains("Huong"))
                listName = "LstHuongDauPhun";
            else if (name.Contains("NhietDo") || name.Contains("Nhiet"))
                listName = "LstNhietDoDauPhun";
            else if (name.Contains("KDauPhun") || name == "TxtKDauPhunThem")
                listName = "LstKDauPhun";

            if (listName == null)
                return;

            WpfListBox lst = TimListBox(listName);
            if (lst == null)
                return;

            bool existed =
                lst.Items
                    .Cast<object>()
                    .Any(x => LayNoiDungItem(x).Equals(
                        value,
                        StringComparison.OrdinalIgnoreCase));

            if (!existed)
            {
                lst.Items.Add(
                    new WpfListBoxItem
                    {
                        Content = value
                    });
            }

            foreach (object item in lst.Items)
            {
                if (LayNoiDungItem(item).Equals(
                    value,
                    StringComparison.OrdinalIgnoreCase))
                {
                    lst.SelectedItem = item;
                    lst.ScrollIntoView(item);
                    break;
                }
            }

            txt.Text = "";
            e.Handled = true;
        }

        private void LstDauPhunCot_KeyDown(
            object sender,
            WpfKeyEventArgs e)
        {
            if (e.Key != WpfKey.Delete)
                return;

            WpfListBox lst = sender as WpfListBox;
            if (lst?.SelectedItem == null)
                return;

            // Không xóa hết — giữ ít nhất 1 mục
            if (lst.Items.Count <= 1)
                return;

            object selected = lst.SelectedItem;
            lst.Items.Remove(selected);

            if (lst.Items.Count > 0)
                lst.SelectedIndex = 0;

            e.Handled = true;
        }

        private string BuildDauPhunModelText()
        {
            string huong = "Hướng Lên";
            string k = "K5.6";
            string nhiet = "68°C";

            var lstHuong = TimListBox("LstHuongDauPhun");
            var lstK = TimListBox("LstKDauPhun");
            var lstNhiet = TimListBox("LstNhietDoDauPhun");

            if (lstHuong?.SelectedItem != null)
                huong = LayNoiDungItem(lstHuong.SelectedItem);

            if (lstK?.SelectedItem != null)
                k = LayNoiDungItem(lstK.SelectedItem);

            if (lstNhiet?.SelectedItem != null)
                nhiet = LayNoiDungItem(lstNhiet.SelectedItem);

            // Rút gọn hướng thành mã ngắn giống ký hiệu phổ biến
            string huongCode = huong.Trim().ToUpperInvariant();

            if (huongCode.Contains("LÊN OG") || huongCode.Contains("LEN OG"))
                huongCode = "HL-OG";
            else if (huongCode.Contains("XUỐNG OG") || huongCode.Contains("XUONG OG"))
                huongCode = "HX-OG";
            else if (huongCode.Contains("LÊN") || huongCode.Contains("LEN"))
                huongCode = "HL";
            else if (huongCode.Contains("XUỐNG") || huongCode.Contains("XUONG"))
                huongCode = "HX";
            else if (huongCode.Contains("NGANG"))
                huongCode = "HN";
            else
                huongCode = CleanLayerText(huong);

            // Bỏ ký tự °C nếu có, chỉ giữ số
            string tempNum = nhiet
                .Replace("°C", "")
                .Replace("ĐỘ C", "")
                .Replace("DO C", "")
                .Trim();

            return $"{huongCode} {k} {tempNum}";
        }

        private void CapNhatVaSapXepDanhSachSizeEquip(
            EquipUiContext ctx,
            List<string> rawSizes,
            string itemToSelect = null)
        {
            if (ctx?.Sizes == null)
                return;

            var sortedSizes = rawSizes
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            ctx.Sizes.Clear();

            string prefix = GetEquipLayerPrefix(ctx);

            foreach (var s in sortedSizes)
            {
                string layerName = $"{prefix}_{CleanLayerText(s)}";
                short aci = GetExpectedAciColor(layerName);

                ctx.Sizes.Add(
                    new PipeSizeItem
                    {
                        SizeName = s,
                        AciColor = aci,
                        LayerColorBrush = GetBrushFromAci(aci)
                    });
            }

            if (ctx.LstSize == null)
                return;

            if (itemToSelect != null)
            {
                var target = ctx.Sizes.FirstOrDefault(
                    x => x.SizeName.Equals(
                        itemToSelect,
                        StringComparison.OrdinalIgnoreCase));

                if (target != null)
                {
                    ctx.LstSize.SelectedItem = target;
                    ctx.LstSize.ScrollIntoView(target);
                }
            }
            else if (ctx.Sizes.Count > 0)
            {
                ctx.LstSize.SelectedIndex = 0;
            }
        }

        private EquipUiContext GetEquipContext(object sender)
        {
            if (sender is WpfButton btn &&
                btn.DataContext is PipeSizeItem item)
            {
                if (_equipCtxACMV != null &&
                    _equipCtxACMV.Sizes.Contains(item))
                    return _equipCtxACMV;

                if (_equipCtxCTN != null &&
                    _equipCtxCTN.Sizes.Contains(item))
                    return _equipCtxCTN;

                return _equipCtxFF;
            }

            if (sender is WpfFrameworkElement fe)
            {
                string name = fe.Name ?? "";

                if (name.Contains("ACMV"))
                    return _equipCtxACMV;

                if (name.Contains("CTN"))
                    return _equipCtxCTN;
            }

            WpfTabControl mainTabs =
                FindName("MainSystemTabs") as WpfTabControl;

            if (mainTabs != null)
            {
                if (mainTabs.SelectedIndex == 1)
                    return _equipCtxACMV;

                if (mainTabs.SelectedIndex == 2)
                    return _equipCtxCTN;
            }

            return _equipCtxFF;
        }

        private string GetSelectedEquipTypeName(EquipUiContext ctx)
        {
            if (ctx?.LstLoai != null &&
                ctx.LstLoai.SelectedItem != null)
            {
                return LayNoiDungItem(ctx.LstLoai.SelectedItem);
            }

            if (ctx?.Suffix == "ACMV")
                return "MÁY LẠNH";

            if (ctx?.Suffix == "CTN")
                return "ĐỒNG HỒ";

            return "BÌNH CC";
        }

        private string GetEquipSystemCode(EquipUiContext ctx)
        {
            string sys = LayNoiDungItem(ctx?.CmbHeThong?.SelectedItem);

            if (string.IsNullOrWhiteSpace(sys))
                sys = ctx?.HeThongMacDinh ?? "Chữa cháy _ FF";

            if (sys.Contains("_"))
                return sys.Split('_').Last().Trim();

            return sys.Trim();
        }

        private string GetEquipLayerPrefix(EquipUiContext ctx)
        {
            string systemCode = CleanLayerText(GetEquipSystemCode(ctx));
            string equipType =
                CleanLayerText(GetSelectedEquipTypeName(ctx));

            return $"{systemCode}_{equipType}";
        }

        private void CapNhatMauEquipTheoPrefix(EquipUiContext ctx)
        {
            if (ctx?.Sizes == null || ctx.Sizes.Count == 0)
                return;

            string prefix = GetEquipLayerPrefix(ctx);

            foreach (var item in ctx.Sizes)
            {
                string layerName =
                    $"{prefix}_{CleanLayerText(item.SizeName)}";
                short aci = GetExpectedAciColor(layerName);

                item.AciColor = aci;
                item.LayerColorBrush = GetBrushFromAci(aci);
            }
        }

        private void KhoiTaoValveContext(ValveUiContext ctx)
        {
            if (ctx == null)
                return;

            if (ctx.LstLoaiVan != null &&
                ctx.LstLoaiVan.SelectedIndex < 0 &&
                ctx.LstLoaiVan.Items.Count > 0)
            {
                ctx.LstLoaiVan.SelectedIndex = 0;
            }

            if (ctx.LstSize != null)
                ctx.LstSize.ItemsSource = ctx.Sizes;

            CapNhatSizeVan(ctx);
        }

        private void CapNhatSizeVan(ValveUiContext ctx)
        {
            if (ctx == null)
                return;

            List<string> newSizes = new List<string>();

            string loai = GetSelectedValveTypeName(ctx);
            bool isDamperGroup = false;

            if (ctx.Suffix == "ACMV")
            {
                var chkGio =
                    FindName("ChkNhomVanGioACMV") as System.Windows.Controls.CheckBox;
                isDamperGroup = chkGio?.IsChecked == true;
            }

            bool isDamperSize =
                isDamperGroup ||
                (ctx.Suffix == "ACMV" && LaVanOngGio(loai));

            if (isDamperSize)
            {
                newSizes.AddRange(new[]
                {
                    "100x100",
                    "200x200",
                    "400x200",
                    "450x450",
                    "500x200"
                });
            }
            else
            {
                newSizes.AddRange(new[]
                {
                    "DN15", "DN20", "DN25", "DN32", "DN40", "DN50",
                    "DN65", "DN80", "DN100", "DN125", "DN150", "DN200",
                    "DN250", "DN300", "DN350", "DN400"
                });
            }

            CapNhatVaSapXepDanhSachSizeVan(ctx, newSizes);
        }

        private bool LaVanOngGio(string valveType)
        {
            string t = (valveType ?? "").Trim().ToUpperInvariant();

            return t == "VCD" ||
                   t == "FD" ||
                   t == "MFD" ||
                   t == "PRD" ||
                   t == "LOUVER" ||
                   t.Contains("MG CẤP") ||
                   t.Contains("MG CAP") ||
                   t.Contains("MG THẢI") ||
                   t.Contains("MG THAI") ||
                   t.Contains("LOUVER") ||
                   t.Contains("DAMPER");
        }

        private void CapNhatVaSapXepDanhSachSizeVan(
            ValveUiContext ctx,
            List<string> rawSizes,
            string itemToSelect = null)
        {
            if (ctx?.Sizes == null)
                return;

            var sortedSizes = rawSizes
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s =>
                {
                    var matches = Regex.Matches(s, @"\d+(\.\d+)?");
                    return matches.Count > 0
                        ? double.Parse(
                            matches[0].Value,
                            CultureInfo.InvariantCulture)
                        : 0;
                })
                .ThenBy(s => s)
                .ToList();

            ctx.Sizes.Clear();

            string prefix = GetValveLayerPrefix(ctx);

            foreach (var s in sortedSizes)
            {
                string layerName = $"{prefix}_{s}";
                short aci = GetExpectedAciColor(layerName);

                ctx.Sizes.Add(
                    new PipeSizeItem
                    {
                        SizeName = s,
                        AciColor = aci,
                        LayerColorBrush = GetBrushFromAci(aci)
                    });
            }

            if (ctx.LstSize == null)
                return;

            if (itemToSelect != null)
            {
                var target = ctx.Sizes.FirstOrDefault(
                    x => x.SizeName.Equals(
                        itemToSelect,
                        StringComparison.OrdinalIgnoreCase));

                if (target != null)
                {
                    ctx.LstSize.SelectedItem = target;
                    ctx.LstSize.ScrollIntoView(target);
                }
            }
            else if (ctx.Sizes.Count > 0)
            {
                ctx.LstSize.SelectedIndex = 0;
            }
        }

        private ValveUiContext GetValveContext(object sender)
        {
            if (sender is WpfButton btn &&
                btn.DataContext is PipeSizeItem item)
            {
                if (_valveCtxACMV != null &&
                    _valveCtxACMV.Sizes.Contains(item))
                    return _valveCtxACMV;

                if (_valveCtxCTN != null &&
                    _valveCtxCTN.Sizes.Contains(item))
                    return _valveCtxCTN;

                return _valveCtxFF;
            }

            if (sender is WpfFrameworkElement fe)
            {
                string name = fe.Name ?? "";

                if (name.Contains("ACMV"))
                    return _valveCtxACMV;

                if (name.Contains("CTN"))
                    return _valveCtxCTN;
            }

            WpfTabControl mainTabs =
                FindName("MainSystemTabs") as WpfTabControl;

            if (mainTabs != null)
            {
                if (mainTabs.SelectedIndex == 1)
                    return _valveCtxACMV;

                if (mainTabs.SelectedIndex == 2)
                    return _valveCtxCTN;
            }

            return _valveCtxFF;
        }

        private string GetSelectedValveTypeName(ValveUiContext ctx)
        {
            // ACMV: 2 nhóm Van / Van gió — lấy theo nhóm đang tích
            if (ctx?.Suffix == "ACMV")
            {
                var chkGio =
                    FindName("ChkNhomVanGioACMV") as System.Windows.Controls.CheckBox;

                if (chkGio?.IsChecked == true)
                {
                    var lstGio = TimListBox("LstLoaiVanGioACMV");
                    if (lstGio?.SelectedItem != null)
                        return LayNoiDungItem(lstGio.SelectedItem);

                    return "VCD";
                }
            }

            if (ctx?.LstLoaiVan != null &&
                ctx.LstLoaiVan.SelectedItem != null)
            {
                return LayNoiDungItem(ctx.LstLoaiVan.SelectedItem);
            }

            return "V.CỔNG TN";
        }

        private bool _updatingNhomVanAcmv = false;

        private void ChkNhomVanAcmv_Changed(
            object sender,
            RoutedEventArgs e)
        {
            if (_updatingNhomVanAcmv)
                return;

            _updatingNhomVanAcmv = true;

            try
            {
                var chkVan =
                    FindName("ChkNhomVanACMV") as System.Windows.Controls.CheckBox;
                var chkGio =
                    FindName("ChkNhomVanGioACMV") as System.Windows.Controls.CheckBox;

                bool useGio = false;

                if (sender is System.Windows.Controls.CheckBox cb)
                {
                    if (cb == chkGio)
                        useGio = cb.IsChecked == true;
                    else if (cb == chkVan)
                        useGio = cb.IsChecked != true;
                }

                if (chkVan != null)
                    chkVan.IsChecked = !useGio;
                if (chkGio != null)
                    chkGio.IsChecked = useGio;

                SetNhomVanAcmvEnabled(useVan: !useGio, useGio: useGio);

                if (_valveCtxACMV != null)
                {
                    CapNhatSizeVan(_valveCtxACMV);
                    CapNhatMauVanTheoPrefix(_valveCtxACMV);
                }
            }
            finally
            {
                _updatingNhomVanAcmv = false;
            }
        }

        private void SetNhomVanAcmvEnabled(bool useVan, bool useGio)
        {
            var lstVan = TimListBox("LstLoaiVanACMV");
            var txtVan = TimTextBox("TxtLoaiVanBoSungACMV");
            var lstGio = TimListBox("LstLoaiVanGioACMV");
            var txtGio = TimTextBox("TxtLoaiVanGioBoSungACMV");

            void Apply(WpfListBox list, WpfTextBox text, bool enabled)
            {
                if (list != null)
                {
                    list.IsEnabled = enabled;
                    list.Opacity = enabled ? 1.0 : 0.45;
                }

                if (text != null)
                {
                    text.IsEnabled = enabled;
                    text.Opacity = enabled ? 1.0 : 0.45;
                }
            }

            Apply(lstVan, txtVan, useVan);
            Apply(lstGio, txtGio, useGio);
        }

        private void TxtLoaiVanGioBoSung_KeyDown(
            object sender,
            WpfKeyEventArgs e)
        {
            if (e.Key != WpfKey.Enter)
                return;

            var lst = TimListBox("LstLoaiVanGioACMV");
            var txt = TimTextBox("TxtLoaiVanGioBoSungACMV");

            if (lst == null || txt == null)
                return;

            string newType = (txt.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(newType))
                return;

            bool existed =
                lst.Items
                    .Cast<object>()
                    .Any(x => LayNoiDungItem(x).Equals(
                        newType,
                        StringComparison.OrdinalIgnoreCase));

            if (!existed)
            {
                lst.Items.Add(
                    new WpfListBoxItem
                    {
                        Content = newType.ToUpper()
                    });
            }

            foreach (object item in lst.Items)
            {
                if (LayNoiDungItem(item).Equals(
                    newType,
                    StringComparison.OrdinalIgnoreCase))
                {
                    lst.SelectedItem = item;
                    lst.ScrollIntoView(item);
                    break;
                }
            }

            txt.Text = "";

            if (_valveCtxACMV != null)
            {
                CapNhatSizeVan(_valveCtxACMV);
                CapNhatMauVanTheoPrefix(_valveCtxACMV);
            }

            e.Handled = true;
        }

        private string GetValveSystemCode(ValveUiContext ctx)
        {
            string sys = LayNoiDungItem(ctx?.CmbHeThong?.SelectedItem);

            if (string.IsNullOrWhiteSpace(sys))
                sys = ctx?.HeThongMacDinh ?? "Chữa cháy _ FF";

            if (sys.Contains("_"))
                return sys.Split('_').Last().Trim();

            return sys.Trim();
        }

        private string GetValveLayerPrefix(ValveUiContext ctx)
        {
            string systemCode = CleanLayerText(GetValveSystemCode(ctx));
            string valveType =
                CleanLayerText(GetSelectedValveTypeName(ctx));
            string viTri = GetViTriText(ctx?.Suffix ?? "");

            if (!string.IsNullOrEmpty(viTri))
                return $"{systemCode}_{viTri}_{valveType}";

            return $"{systemCode}_{valveType}";
        }

        private void CapNhatMauVanTheoPrefix(ValveUiContext ctx)
        {
            if (ctx?.Sizes == null || ctx.Sizes.Count == 0)
                return;

            string prefix = GetValveLayerPrefix(ctx);

            foreach (var item in ctx.Sizes)
            {
                string layerName = $"{prefix}_{item.SizeName}";
                short aci = GetExpectedAciColor(layerName);

                item.AciColor = aci;
                item.LayerColorBrush = GetBrushFromAci(aci);
            }
        }

        private void KhoiTaoContext(PipeUiContext ctx)
        {
            if (ctx == null)
                return;

            if (ctx.CmbHeThong != null)
            {
                if (ctx.CmbHeThong.Items.Count == 0)
                {
                    ctx.CmbHeThong.Items.Add(
                        new WpfComboBoxItem
                        {
                            Content = ctx.HeThongMacDinh
                        });
                }

                ctx.CmbHeThong.SelectedIndex = 0;
            }

            if (ctx.LstVatLieu != null)
            {
                if (ctx.LstVatLieu.Items.Count == 0)
                {
                    if (ctx.Suffix == "ACMV")
                    {
                        ThemVatLieuMacDinh(ctx, "TRÁNG KẼM");
                        ThemVatLieuMacDinh(ctx, "HDPE");
                        ThemVatLieuMacDinh(ctx, "THÉP ĐEN");
                        ThemVatLieuMacDinh(ctx, "INOX");
                        ThemVatLieuMacDinh(ctx, "UPVC");
                        ThemVatLieuMacDinh(ctx, "ỐNG ĐỒNG");
                    }
                    else
                    {
                        ThemVatLieuMacDinh(ctx, "TRÁNG KẼM");
                        ThemVatLieuMacDinh(ctx, "HDPE");
                        ThemVatLieuMacDinh(ctx, "THÉP ĐEN");
                        ThemVatLieuMacDinh(ctx, "INOX");
                        ThemVatLieuMacDinh(ctx, "NHÚNG NÓNG");
                    }
                }

                if (ctx.LstVatLieu.SelectedIndex < 0 &&
                    ctx.LstVatLieu.Items.Count > 0)
                {
                    ctx.LstVatLieu.SelectedIndex = 0;
                }
            }

            CapNhatComboVatLieuAn(ctx);

            if (ctx.LstSize != null)
                ctx.LstSize.ItemsSource = ctx.Sizes;

            CapNhatSizeTheoVatLieu(ctx);
        }

        private void ThemVatLieuMacDinh(
            PipeUiContext ctx,
            string tenVatLieu)
        {
            if (ctx?.LstVatLieu == null)
                return;

            ctx.LstVatLieu.Items.Add(
                new WpfListBoxItem
                {
                    Content = tenVatLieu
                });
        }

        private void CapNhatComboVatLieuAn(PipeUiContext ctx)
        {
            if (ctx?.CmbVatLieu == null)
                return;

            string selectedText = GetSelectedPipeMaterialName(ctx);
            ctx.CmbVatLieu.Items.Clear();

            if (ctx.LstVatLieu != null)
            {
                foreach (object item in ctx.LstVatLieu.Items)
                {
                    string text = LayNoiDungItem(item);

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        ctx.CmbVatLieu.Items.Add(
                            new WpfComboBoxItem
                            {
                                Content = text
                            });
                    }
                }
            }

            int index = -1;

            for (int i = 0; i < ctx.CmbVatLieu.Items.Count; i++)
            {
                string text = LayNoiDungItem(ctx.CmbVatLieu.Items[i]);

                if (string.Equals(
                    text,
                    selectedText,
                    StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            if (ctx.CmbVatLieu.Items.Count > 0)
                ctx.CmbVatLieu.SelectedIndex = index >= 0 ? index : 0;
        }

        private PipeUiContext GetContext(object sender)
        {
            if (sender is WpfButton btn &&
                btn.DataContext is PipeSizeItem item)
            {
                if (_ctxACMV != null && _ctxACMV.Sizes.Contains(item))
                    return _ctxACMV;

                if (_ctxCTN != null && _ctxCTN.Sizes.Contains(item))
                    return _ctxCTN;

                return _ctxFF;
            }

            if (sender is WpfFrameworkElement fe)
            {
                string name = fe.Name ?? "";

                if (name.Contains("ACMV"))
                    return _ctxACMV;

                if (name.Contains("CTN"))
                    return _ctxCTN;
            }

            WpfTabControl mainTabs =
                FindName("MainSystemTabs") as WpfTabControl;

            if (mainTabs != null)
            {
                if (mainTabs.SelectedIndex == 1)
                    return _ctxACMV;

                if (mainTabs.SelectedIndex == 2)
                    return _ctxCTN;
            }

            return _ctxFF;
        }

        private string LayNoiDungItem(object item)
        {
            if (item == null)
                return "";

            if (item is WpfComboBoxItem cbi)
                return cbi.Content?.ToString()?.Trim() ?? "";

            if (item is WpfListBoxItem lbi)
                return lbi.Content?.ToString()?.Trim() ?? "";

            return item.ToString()?.Trim() ?? "";
        }

        private string GetSelectedPipeMaterialName(PipeUiContext ctx)
        {
            // ACMV: 2 nhóm Ống / Ống gió — lấy theo nhóm đang tích
            if (ctx?.Suffix == "ACMV")
            {
                var chkGio =
                    FindName("ChkNhomOngGioACMV") as System.Windows.Controls.CheckBox;

                if (chkGio?.IsChecked == true)
                {
                    var lstGio = TimListBox("LstVatLieuOngGioACMV");
                    if (lstGio?.SelectedItem != null)
                        return LayNoiDungItem(lstGio.SelectedItem);

                    return "OG THẢI";
                }
            }

            if (ctx?.LstVatLieu != null &&
                ctx.LstVatLieu.SelectedItem != null)
            {
                return LayNoiDungItem(ctx.LstVatLieu.SelectedItem);
            }

            if (ctx?.CmbVatLieu != null &&
                ctx.CmbVatLieu.SelectedItem != null)
            {
                return LayNoiDungItem(ctx.CmbVatLieu.SelectedItem);
            }

            return "TRÁNG KẼM";
        }

        private bool _updatingNhomOngAcmv = false;

        private void ChkNhomOngAcmv_Changed(
            object sender,
            RoutedEventArgs e)
        {
            if (_updatingNhomOngAcmv)
                return;

            _updatingNhomOngAcmv = true;

            try
            {
                var chkOng =
                    FindName("ChkNhomOngACMV") as System.Windows.Controls.CheckBox;
                var chkGio =
                    FindName("ChkNhomOngGioACMV") as System.Windows.Controls.CheckBox;

                bool useGio = false;

                if (sender is System.Windows.Controls.CheckBox cb)
                {
                    if (cb == chkGio)
                        useGio = cb.IsChecked == true;
                    else if (cb == chkOng)
                        useGio = cb.IsChecked != true;
                }

                if (chkOng != null)
                    chkOng.IsChecked = !useGio;
                if (chkGio != null)
                    chkGio.IsChecked = useGio;

                SetNhomOngAcmvEnabled(useOng: !useGio, useGio: useGio);

                if (_ctxACMV != null)
                    CapNhatSizeTheoVatLieu(_ctxACMV);
            }
            finally
            {
                _updatingNhomOngAcmv = false;
            }
        }

        private void SetNhomOngAcmvEnabled(bool useOng, bool useGio)
        {
            var lstOng = TimListBox("LstVatLieuOngBangACMV");
            var txtOng = TimTextBox("TxtVatLieuOngBoSungACMV");
            var lstGio = TimListBox("LstVatLieuOngGioACMV");
            var txtGio = TimTextBox("TxtVatLieuOngGioBoSungACMV");

            void Apply(WpfListBox list, WpfTextBox text, bool enabled)
            {
                if (list != null)
                {
                    list.IsEnabled = enabled;
                    list.Opacity = enabled ? 1.0 : 0.45;
                }

                if (text != null)
                {
                    text.IsEnabled = enabled;
                    text.Opacity = enabled ? 1.0 : 0.45;
                }
            }

            Apply(lstOng, txtOng, useOng);
            Apply(lstGio, txtGio, useGio);
        }

        private void TxtVatLieuOngGioBoSung_KeyDown(
            object sender,
            WpfKeyEventArgs e)
        {
            if (e.Key != WpfKey.Enter)
                return;

            var lst = TimListBox("LstVatLieuOngGioACMV");
            var txt = TimTextBox("TxtVatLieuOngGioBoSungACMV");

            if (lst == null || txt == null)
                return;

            string newType = (txt.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(newType))
                return;

            bool existed =
                lst.Items
                    .Cast<object>()
                    .Any(x => LayNoiDungItem(x).Equals(
                        newType,
                        StringComparison.OrdinalIgnoreCase));

            if (!existed)
            {
                lst.Items.Add(
                    new WpfListBoxItem
                    {
                        Content = newType.ToUpper()
                    });
            }

            foreach (object item in lst.Items)
            {
                if (LayNoiDungItem(item).Equals(
                    newType,
                    StringComparison.OrdinalIgnoreCase))
                {
                    lst.SelectedItem = item;
                    lst.ScrollIntoView(item);
                    break;
                }
            }

            txt.Text = "";

            if (_ctxACMV != null)
                CapNhatSizeTheoVatLieu(_ctxACMV);

            e.Handled = true;
        }

        private string CleanLayerText(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "";

            string invalid = "<>/\\\":;?*|=,";
            StringBuilder sb = new StringBuilder();

            foreach (char ch in input.Trim())
                sb.Append(invalid.Contains(ch) ? '-' : ch);

            string result = Regex.Replace(
                sb.ToString(),
                @"\s+",
                " ").Trim();

            result = Regex.Replace(
                result,
                @"-+",
                "-").Trim('-');

            return result;
        }

        private string GetSystemCode(PipeUiContext ctx)
        {
            string sys = LayNoiDungItem(ctx?.CmbHeThong?.SelectedItem);

            if (string.IsNullOrWhiteSpace(sys))
                sys = ctx?.HeThongMacDinh ?? "Chữa cháy _ FF";

            if (sys.Contains("_"))
                return sys.Split('_').Last().Trim();

            return sys.Trim();
        }

        private string GetViTriText(string suffix)
        {
            string name = "TxtViTriFF";

            if (suffix == "ACMV")
                name = "TxtViTriACMV";
            else if (suffix == "CTN")
                name = "TxtViTriCTN";

            var txt = TimTextBox(name);
            string viTri = (txt?.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(viTri))
                return "";

            return CleanLayerText(viTri);
        }

        private string GetLayerPrefix(PipeUiContext ctx)
        {
            string systemCode = CleanLayerText(GetSystemCode(ctx));
            string materialName =
                CleanLayerText(GetSelectedPipeMaterialName(ctx));
            string viTri = GetViTriText(ctx?.Suffix ?? "");

            if (!string.IsNullOrEmpty(viTri))
                return $"{systemCode}_{viTri}_{materialName}";

            return $"{systemCode}_{materialName}";
        }

        private bool CheckIsOngGio(PipeUiContext ctx)
        {
            string sys =
                LayNoiDungItem(ctx?.CmbHeThong?.SelectedItem);

            string mat = GetSelectedPipeMaterialName(ctx);

            return sys.Contains("HVAC") ||
                   sys.Contains("SM") ||
                   sys.Contains("TG") ||
                   mat.Contains("ỐNG GIÓ") ||
                   LaOngGio(mat);
        }

        private void BtnColor_Click(
            object sender,
            RoutedEventArgs e)
        {
            WpfButton btn = sender as WpfButton;
            PipeSizeItem item = btn?.DataContext as PipeSizeItem;

            if (item == null)
                return;

            PipeUiContext ctx = GetContext(sender);
            string layerPrefix = GetLayerPrefix(ctx);
            string layerName = $"{layerPrefix}_{item.SizeName}";

            var cd = new Autodesk.AutoCAD.Windows.ColorDialog();

            cd.Color =
                Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                    ColorMethod.ByAci,
                    item.AciColor);

            if (cd.ShowDialog() == WinFormsDialogResult.OK)
            {
                short newAci = cd.Color.ColorIndex;
                _userCustomColors[layerName] = newAci;

                item.AciColor = newAci;
                item.LayerColorBrush = GetBrushFromAci(newAci);

                var doc =
                    Autodesk.AutoCAD.ApplicationServices.Core.Application
                        .DocumentManager
                        .MdiActiveDocument;

                if (doc != null)
                {
                    using (doc.LockDocument())
                    {
                        using (Transaction tr =
                            doc.Database.TransactionManager
                                .StartTransaction())
                        {
                            LayerTable lt =
                                (LayerTable)tr.GetObject(
                                    doc.Database.LayerTableId,
                                    OpenMode.ForRead);

                            if (lt.Has(layerName))
                            {
                                LayerTableRecord ltr =
                                    (LayerTableRecord)tr.GetObject(
                                        lt[layerName],
                                        OpenMode.ForWrite);

                                ltr.Color =
                                    Autodesk.AutoCAD.Colors.Color
                                        .FromColorIndex(
                                            ColorMethod.ByAci,
                                            newAci);
                            }

                            tr.Commit();
                        }
                    }

                    doc.Editor.Regen();
                }
            }
        }

        private short GetExpectedAciColor(string layerName)
        {
            if (_userCustomColors.ContainsKey(layerName))
                return _userCustomColors[layerName];

            return GetBrightAciColor(layerName);
        }

        private short GetBrightAciColor(string layerName)
        {
            unchecked
            {
                uint hash = 2166136261;

                foreach (char character in layerName ?? "")
                {
                    hash ^= character;
                    hash *= 16777619;
                }

                return _brightAciColors[
                    hash % (uint)_brightAciColors.Length];
            }
        }

        private bool IsAciColorTooDark(short aci)
        {
            if (aci <= 0 || aci == 7 || aci == 8)
                return true;

            try
            {
                var acColor =
                    Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                        ColorMethod.ByAci,
                        aci);

                double brightness =
                    (acColor.ColorValue.R * 0.299) +
                    (acColor.ColorValue.G * 0.587) +
                    (acColor.ColorValue.B * 0.114);

                return brightness < 65;
            }
            catch
            {
                return true;
            }
        }

        private System.Windows.Media.SolidColorBrush GetBrushFromAci(
            short aci)
        {
            try
            {
                var acColor =
                    Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                        ColorMethod.ByAci,
                        aci);

                return new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(
                        acColor.ColorValue.R,
                        acColor.ColorValue.G,
                        acColor.ColorValue.B));
            }
            catch
            {
                return new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Colors.Black);
            }
        }

        private void UpdatePrefix_Trigger(
            object sender,
            RoutedEventArgs e)
        {
            CapNhatMauTheoPrefix(GetContext(sender));
        }

        private void CapNhatMauTheoPrefix(PipeUiContext ctx)
        {
            if (ctx?.Sizes == null || ctx.Sizes.Count == 0)
                return;

            string prefix = GetLayerPrefix(ctx);

            foreach (var item in ctx.Sizes)
            {
                string layerName = $"{prefix}_{item.SizeName}";
                short aci = GetExpectedAciColor(layerName);

                item.AciColor = aci;
                item.LayerColorBrush = GetBrushFromAci(aci);
            }
        }

        private class SegmentData
        {
            public Curve Curve { get; set; }
            public string Layer { get; set; }
            public double Width { get; set; }
            public string LabelText { get; set; }
            public double BestScore { get; set; } = double.MaxValue;
            public Curve OriginalParent { get; set; }
        }

        private class TextData
        {
            public Point3d Position { get; set; }
            public double Rotation { get; set; }
            public string TextString { get; set; }
            public string LayerName { get; set; }
            public double Width { get; set; }
        }

        private class TextProjectionData
        {
            public TextData Text { get; set; }
            public double DistanceAlongCurve { get; set; }
            public double MatchScore { get; set; }
        }

        private class SprinklerProjectionData
        {
            public Point3d Center { get; set; }
            public Point3d PointOnCurve { get; set; }
            public double PlanDistance { get; set; }
        }

        private class ShopPipeCandidate
        {
            public Curve Curve { get; set; }
            public string SizeText { get; set; }
            public double Width { get; set; }
            public string LayerName { get; set; }
            public Point3d Start { get; set; }
            public Point3d End { get; set; }
            public Point3d StartLeft { get; set; }
            public Point3d StartRight { get; set; }
            public Point3d EndLeft { get; set; }
            public Point3d EndRight { get; set; }
        }

        private class ShopPipeLeg
        {
            public ShopPipeCandidate Pipe { get; set; }
            public Point3d Start { get; set; }
            public Point3d End { get; set; }
            public Vector3d StartDirectionAway { get; set; }
            public Vector3d EndDirectionAway { get; set; }
            public bool IsStraight { get; set; }
        }

        private class ShopJointArm
        {
            public ShopPipeLeg Leg { get; set; }
            public ShopPipeCandidate Pipe => Leg?.Pipe;
            public Vector3d Direction { get; set; }
            public string SizeText => Pipe?.SizeText ?? "";
            public double Width => Pipe?.Width ?? 0.0;
        }

        private class ShopJointNode
        {
            public Point3d Point { get; set; }
            public List<ShopJointArm> Arms { get; } =
                new List<ShopJointArm>();
        }

        /// <summary>
        /// Nửa chiều dài phụ kiện tại nút — để ống SNAP chạm mặt block.
        /// </summary>
        private class ShopFittingGapInfo
        {
            public Point3d Joint { get; set; }
            public double HalfLength { get; set; }

            // FIX23:
            // Reducer cũ vẫn để Direction = 0, SizeText = "" => hành vi y như code gốc.
            // Co/Tê mới ghi thêm hướng + DN để mỗi ống chỉ rút đúng tới cổng của nó.
            public Vector3d Direction { get; set; }
            public string SizeText { get; set; }
        }

        // Hình học cổng nối của block phụ kiện thư viện.
        // Không dùng Base Point của block để đặt phụ kiện nữa vì nhiều block
        // trong thư viện có Base Point nằm lệch khỏi tâm nối thực tế.
        private class ShopFittingEndpointSample
        {
            public Point3d Point { get; set; }
            public Vector3d OutwardDirection { get; set; }
        }

        private class ShopFittingPortInfo
        {
            public Point3d Center { get; set; }
            public Vector3d OutwardDirection { get; set; }
            public double Width { get; set; }
            public int SampleCount { get; set; }
        }

        private class ShopFittingPlacementInfo
        {
            public Point3d Anchor { get; set; }
            public List<ShopFittingPortInfo> Ports { get; } =
                new List<ShopFittingPortInfo>();
            public int LargePortIndex { get; set; } = -1;
            public int SmallPortIndex { get; set; } = -1;
            public int BranchPortIndex { get; set; } = -1;
        }

        private class TemplateBranchMatchData
        {
            public Curve TargetCurve { get; set; }
            public Matrix3d Transform { get; set; }
            public double Score { get; set; }
            public Point3d TargetCenter { get; set; }
        }

        private void EnsureLayerExists(
            Transaction tr,
            Database db,
            string layerName,
            bool isOngGio)
        {
            LayerTable lt =
                (LayerTable)tr.GetObject(
                    db.LayerTableId,
                    OpenMode.ForRead);

            if (!lt.Has(layerName))
            {
                lt.UpgradeOpen();

                LayerTableRecord ltr = new LayerTableRecord();
                ltr.Name = layerName;

                short colorIndex = GetExpectedAciColor(layerName);

                ltr.Color =
                    Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                        ColorMethod.ByAci,
                        colorIndex);

                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);

                if (isOngGio)
                {
                    ltr.Transparency =
                        new Autodesk.AutoCAD.Colors.Transparency(102);
                }
                else
                {
                    ltr.Transparency =
                        new Autodesk.AutoCAD.Colors.Transparency(255);
                }
            }
            else
            {
                LayerTableRecord ltr =
                    (LayerTableRecord)tr.GetObject(
                        lt[layerName],
                        OpenMode.ForWrite);

                if (!_userCustomColors.ContainsKey(layerName))
                {
                    ltr.Color =
                        Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                            ColorMethod.ByAci,
                            GetBrightAciColor(layerName));
                }
            }
        }

        private double TinhGocLech(
            Curve curve,
            Point3d closestPt,
            double txtRotation)
        {
            try
            {
                Vector3d deriv =
                    curve.GetFirstDerivative(
                        curve.GetParameterAtPoint(closestPt));

                double curveAngle =
                    deriv.AngleOnPlane(new Plane());

                double diff = Math.Abs(curveAngle - txtRotation);

                diff = diff % Math.PI;

                if (diff > Math.PI / 2.0)
                    diff = Math.PI - diff;

                return diff;
            }
            catch
            {
                // Không xác định được hướng → coi như không song song.
                return Math.PI / 2.0;
            }
        }

        /// <summary>
        /// Chữ kích thước chỉ được gán cho ống khi gần như song song
        /// với hướng ống (góc lệch ≤ MaxParallelAngleRadians).
        /// </summary>
        private bool IsTextParallelToCurve(
            Curve curve,
            Point3d closestPt,
            double txtRotation)
        {
            return TinhGocLech(curve, closestPt, txtRotation)
                <= MaxParallelAngleRadians;
        }

        private bool IsTouching(
            Curve c1,
            Curve c2,
            out Point3d touchPt)
        {
            touchPt = Point3d.Origin;

            Point3d[] pts1 = { c1.StartPoint, c1.EndPoint };
            Point3d[] pts2 = { c2.StartPoint, c2.EndPoint };

            foreach (var p1 in pts1)
            {
                foreach (var p2 in pts2)
                {
                    if (p1.DistanceTo(p2) < 1.0)
                    {
                        touchPt = p1;
                        return true;
                    }
                }
            }

            return false;
        }

        private bool IsCollinear(
            Curve c1,
            Curve c2,
            Point3d touchPt)
        {
            try
            {
                Vector3d v1 =
                    c1.GetFirstDerivative(
                        c1.GetParameterAtPoint(touchPt))
                        .GetNormal();

                Vector3d v2 =
                    c2.GetFirstDerivative(
                        c2.GetParameterAtPoint(touchPt))
                        .GetNormal();

                double angle = v1.GetAngleTo(v2);

                return angle < 0.1 ||
                       Math.Abs(angle - Math.PI) < 0.1;
            }
            catch
            {
                return false;
            }
        }

        private void CapNhatVaSapXepDanhSachSize(
            PipeUiContext ctx,
            List<string> rawSizes,
            string itemToSelect = null)
        {
            if (ctx?.Sizes == null)
                return;

            var sortedSizes = rawSizes
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s =>
                {
                    var matches =
                        Regex.Matches(s, @"\d+(\.\d+)?");

                    return matches.Count > 0
                        ? double.Parse(
                            matches[0].Value,
                            CultureInfo.InvariantCulture)
                        : 0;
                })
                .ThenBy(s =>
                {
                    var matches =
                        Regex.Matches(s, @"\d+(\.\d+)?");

                    return matches.Count > 1
                        ? double.Parse(
                            matches[1].Value,
                            CultureInfo.InvariantCulture)
                        : 0;
                })
                .ThenBy(s => s)
                .ToList();

            ctx.Sizes.Clear();

            string prefix = GetLayerPrefix(ctx);

            foreach (var s in sortedSizes)
            {
                string layerName = $"{prefix}_{s}";
                short aci = GetExpectedAciColor(layerName);

                ctx.Sizes.Add(
                    new PipeSizeItem
                    {
                        SizeName = s,
                        AciColor = aci,
                        LayerColorBrush = GetBrushFromAci(aci)
                    });
            }

            if (ctx.LstSize == null)
                return;

            if (itemToSelect != null)
            {
                var target =
                    ctx.Sizes.FirstOrDefault(
                        x => x.SizeName.Equals(
                            itemToSelect,
                            StringComparison.OrdinalIgnoreCase));

                if (target != null)
                {
                    ctx.LstSize.SelectedItem = target;
                    ctx.LstSize.ScrollIntoView(target);
                }
            }
            else if (ctx.Sizes.Count > 0)
            {
                ctx.LstSize.SelectedIndex = 0;
            }
        }

        private bool LaOngGio(string material)
        {
            string m =
                (material ?? "").Trim().ToUpperInvariant();

            return m.Contains("ỐNG GIÓ") ||
                   m.Contains("ONG GIO") ||
                   m.StartsWith("OG ") ||
                   m.StartsWith("OG_") ||
                   m == "OG" ||
                   m.Contains("OG THẢI") ||
                   m.Contains("OG THAI") ||
                   m.Contains("OG HÚT") ||
                   m.Contains("OG HUT") ||
                   m.Contains("OG LẠNH") ||
                   m.Contains("OG LANH") ||
                   m.Contains("OG CẤP") ||
                   m.Contains("OG CAP") ||
                   m.Contains("OG HỒI") ||
                   m.Contains("OG HOI") ||
                   m.Contains("SEAF") ||
                   m.Contains("FAF") ||
                   m.Contains("EAF") ||
                   m.Contains("PAF") ||
                   m.Contains("BEP");
        }

        private bool LaOngGioHutKhoi(string material)
        {
            string m =
                (material ?? "").Trim().ToUpperInvariant();

            return m.Contains("HÚT KHÓI") ||
                   m.Contains("HUT KHOI") ||
                   m.Contains("HÚT KHÓI") ||
                   m.Contains("SMOKE");
        }

        private bool LaOngDong(string material)
        {
            string m =
                (material ?? "").Trim().ToUpperInvariant();

            return m == "ỐNG ĐỒNG" ||
                   m == "ĐỒNG" ||
                   m.Contains("ỐNG ĐỒNG") ||
                   m.EndsWith("_ CU") ||
                   m.Contains(" _ CU");
        }

        private void CapNhatSizeTheoVatLieu(PipeUiContext ctx)
        {
            if (ctx == null)
                return;

            string material = GetSelectedPipeMaterialName(ctx);
            List<string> newSizes = new List<string>();

            bool isOngGioSize = LaOngGio(material);

            if (ctx.Suffix == "ACMV")
            {
                var chkGio =
                    FindName("ChkNhomOngGioACMV") as System.Windows.Controls.CheckBox;
                if (chkGio != null)
                    isOngGioSize = chkGio.IsChecked == true;
            }

            if (isOngGioSize)
            {
                newSizes.AddRange(
                    new[]
                    {
                        "100x100",
                        "200x100",
                        "500x200",
                        "800x300",
                        "800x350"
                    });
            }
            else if (LaOngDong(material))
            {
                newSizes.AddRange(
                    new[]
                    {
                        "6.4 - 9.5", "6.4 - 12.7",
                        "6.4 - 15.9", "9.5 - 12.7",
                        "9.5 - 15.9", "9.5 - 19.1",
                        "9.5 - 22.2", "12.7 - 19.1",
                        "12.7 - 22.2", "12.7 - 25.4",
                        "12.7 - 28.6", "15.9 - 28.6",
                        "15.9 - 31.8", "15.9 - 34.9",
                        "15.9 - 38.1"
                    });
            }
            else
            {
                newSizes.AddRange(
                    new[]
                    {
                        "DN15", "DN20", "DN25",
                        "DN32", "DN40", "DN50",
                        "DN65", "DN80", "DN100",
                        "DN125", "DN150", "DN200",
                        "DN250", "DN300", "DN350",
                        "DN400"
                    });
            }

            CapNhatVaSapXepDanhSachSize(ctx, newSizes);
            CapNhatHienThiPanelOngGioACMV(ctx);
            CapNhatDanhSachCnEiOngGio(ctx);
        }

        private void CapNhatHienThiPanelOngGioACMV(PipeUiContext ctx)
        {
            if (ctx == null || ctx.Suffix != "ACMV")
                return;

            var panelThuong =
                FindName("PanelSizeOngThuongACMV") as System.Windows.UIElement;
            var panelGio =
                FindName("PanelSizeOngGioACMV") as System.Windows.UIElement;

            // Nếu thiếu panel ống gió trong XAML → vẫn cho vẽ bằng list size thường
            if (panelThuong == null && panelGio == null)
                return;

            string material = GetSelectedPipeMaterialName(ctx);
            bool isOngGio = LaOngGio(material);

            // Ưu tiên theo ô tích nhóm Ống gió (ACMV)
            if (ctx.Suffix == "ACMV")
            {
                var chkGio =
                    FindName("ChkNhomOngGioACMV") as System.Windows.Controls.CheckBox;
                if (chkGio != null)
                    isOngGio = chkGio.IsChecked == true;
            }

            if (panelThuong != null)
            {
                // Không có panel gió riêng → luôn hiện panel thường (chứa size WxH)
                if (panelGio == null)
                    panelThuong.Visibility = System.Windows.Visibility.Visible;
                else
                    panelThuong.Visibility = isOngGio
                        ? System.Windows.Visibility.Collapsed
                        : System.Windows.Visibility.Visible;
            }

            if (panelGio != null)
            {
                panelGio.Visibility = isOngGio
                    ? System.Windows.Visibility.Visible
                    : System.Windows.Visibility.Collapsed;
            }

            // Đồng bộ list size ống gió từ ctx.Sizes (có màu Layer)
            if (isOngGio)
            {
                try
                {
                    var lstGio = TimListBox("LstSizeOngGioACMV");
                    if (lstGio != null && ctx.Sizes != null)
                    {
                        string selectedName = null;
                        if (lstGio.SelectedItem is PipeSizeItem selItem)
                            selectedName = selItem.SizeName;
                        else
                            selectedName = LayNoiDungItem(lstGio.SelectedItem);

                        // Chỉ gán ItemsSource — không Clear Items (tránh crash)
                        lstGio.ItemsSource = ctx.Sizes;

                        if (ctx.Sizes.Count > 0)
                        {
                            PipeSizeItem target = null;
                            if (!string.IsNullOrWhiteSpace(selectedName))
                            {
                                foreach (var s in ctx.Sizes)
                                {
                                    if (string.Equals(s.SizeName, selectedName,
                                        StringComparison.OrdinalIgnoreCase))
                                    {
                                        target = s;
                                        break;
                                    }
                                }
                            }
                            object want = target ?? ctx.Sizes[0];
                            if (!ReferenceEquals(lstGio.SelectedItem, want))
                                lstGio.SelectedItem = want;
                        }
                    }

                    CapNhatDanhSachCnEiOngGio(ctx);
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "CapNhatHienThiPanelOngGioACMV: " + ex.Message);
                }
            }
        }

        private void CapNhatDanhSachCnEiOngGio(PipeUiContext ctx)
        {
            if (ctx == null || ctx.Suffix != "ACMV")
                return;

            var lstCnEi = TimListBox("LstCnEiOngGioACMV");
            var txtTitle =
                FindName("TxtTieuDeCnEi") as System.Windows.Controls.TextBlock;

            if (lstCnEi == null)
                return;

            string material = GetSelectedPipeMaterialName(ctx);
            bool isHutKhoi = LaOngGioHutKhoi(material);

            // Hút khói → EI ; các loại ống gió còn lại → CN
            string[] items = isHutKhoi
                ? new[] { "EI30", "EI45", "EI60", "EI90", "EI120", "EI160" }
                : new[]
                {
                    "CN10", "CN13", "CN15", "CN20",
                    "CN25", "CN30", "CN35"
                };

            if (txtTitle != null)
                txtTitle.Text = isHutKhoi ? "EI" : "CN";

            // OG Hút khói thường cần EI → mặc định tích
            // OG Thải / OG Cấp thường không bọc CN → mặc định bỏ tích
            var chk =
                FindName("ChkDungCnEi") as System.Windows.Controls.CheckBox;

            if (chk != null)
            {
                chk.IsChecked = isHutKhoi;
                CapNhatTrangThaiCnEi();
            }

            string selected = LayNoiDungItem(lstCnEi.SelectedItem);
            lstCnEi.Items.Clear();

            foreach (string item in items)
            {
                lstCnEi.Items.Add(
                    new WpfListBoxItem
                    {
                        Content = item
                    });
            }

            int idx = 0;

            for (int i = 0; i < lstCnEi.Items.Count; i++)
            {
                if (LayNoiDungItem(lstCnEi.Items[i]).Equals(
                    selected,
                    StringComparison.OrdinalIgnoreCase))
                {
                    idx = i;
                    break;
                }
            }

            if (lstCnEi.Items.Count > 0)
                lstCnEi.SelectedIndex = idx;
        }

        private string GetSelectedOngGioSize()
        {
            var lstGio = TimListBox("LstSizeOngGioACMV");
            if (lstGio?.SelectedItem is PipeSizeItem psi &&
                !string.IsNullOrWhiteSpace(psi.SizeName))
                return psi.SizeName;

            string size = LayNoiDungItem(lstGio?.SelectedItem);
            if (!string.IsNullOrWhiteSpace(size))
                return size;

            // Fallback list thường
            if (_ctxACMV?.LstSize?.SelectedItem is PipeSizeItem item &&
                !string.IsNullOrWhiteSpace(item.SizeName))
                return item.SizeName;

            var lstThuong = TimListBox("LstSizeOngACMV");
            size = LayNoiDungItem(lstThuong?.SelectedItem);
            return size ?? "";
        }

        private string GetSelectedCnEi()
        {
            var chk =
                FindName("ChkDungCnEi") as System.Windows.Controls.CheckBox;

            // Không tích → không dùng CN/EI
            if (chk?.IsChecked != true)
                return "";

            var lst = TimListBox("LstCnEiOngGioACMV");
            return LayNoiDungItem(lst?.SelectedItem);
        }

        private void ChkDungCnEi_Changed(
            object sender,
            RoutedEventArgs e)
        {
            CapNhatTrangThaiCnEi();
        }

        private void CapNhatTrangThaiCnEi()
        {
            var chk =
                FindName("ChkDungCnEi") as System.Windows.Controls.CheckBox;
            var lst = TimListBox("LstCnEiOngGioACMV");
            var txt = TimTextBox("TxtCnEiOngGioACMVThem");

            bool enabled = chk?.IsChecked == true;

            if (lst != null)
            {
                lst.IsEnabled = enabled;
                lst.Opacity = enabled ? 1.0 : 0.45;
            }

            if (txt != null)
            {
                txt.IsEnabled = enabled;
                txt.Opacity = enabled ? 1.0 : 0.45;
            }
        }

        private string GetSelectedPipeSizeName(PipeUiContext ctx)
        {
            if (ctx == null)
                return "";

            string material = GetSelectedPipeMaterialName(ctx);

            bool isOngGioSelected = LaOngGio(material);

            if (ctx.Suffix == "ACMV")
            {
                var chkGio =
                    FindName("ChkNhomOngGioACMV") as System.Windows.Controls.CheckBox;
                if (chkGio != null)
                    isOngGioSelected = chkGio.IsChecked == true;
            }

            if (ctx.Suffix == "ACMV" && isOngGioSelected)
            {
                string size = GetSelectedOngGioSize();
                string cnEi = GetSelectedCnEi();

                if (string.IsNullOrWhiteSpace(size))
                    return "";

                if (string.IsNullOrWhiteSpace(cnEi))
                    return size;

                return $"{size}_{cnEi}";
            }

            // Ống thường ACMV: size DN + CN (nếu tích)
            string baseSize =
                (ctx.LstSize?.SelectedItem as PipeSizeItem)
                    ?.SizeName ?? "";

            if (ctx.Suffix == "ACMV" && !isOngGioSelected)
            {
                string cnOng = GetSelectedCnOngAcmv();

                if (!string.IsNullOrWhiteSpace(baseSize) &&
                    !string.IsNullOrWhiteSpace(cnOng))
                {
                    return $"{baseSize}_{cnOng}";
                }
            }

            // CTN: size DN + PN (nếu tích)
            if (ctx.Suffix == "CTN")
            {
                string pn = GetSelectedPnCtn();
                if (!string.IsNullOrWhiteSpace(baseSize) &&
                    !string.IsNullOrWhiteSpace(pn))
                {
                    return $"{baseSize}_{pn}";
                }
            }

            return baseSize;
        }

        private string GetSelectedPnCtn()
        {
            var chk =
                FindName("ChkDungPnCTN") as System.Windows.Controls.CheckBox;

            if (chk != null && chk.IsChecked != true)
                return "";

            var lst = TimListBox("LstPnCTN");
            return LayNoiDungItem(lst?.SelectedItem);
        }

        private void ChkDungPnCtn_Changed(
            object sender,
            RoutedEventArgs e)
        {
            CapNhatTrangThaiPnCtn();
        }

        private void CapNhatTrangThaiPnCtn()
        {
            var chk =
                FindName("ChkDungPnCTN") as System.Windows.Controls.CheckBox;
            var lst = TimListBox("LstPnCTN");

            bool enabled = chk == null || chk.IsChecked == true;

            if (lst != null)
            {
                lst.IsEnabled = enabled;
                lst.Opacity = enabled ? 1.0 : 0.45;
            }
        }

        private string GetSelectedCnOngAcmv()
        {
            var chk =
                FindName("ChkDungCnOngACMV") as System.Windows.Controls.CheckBox;

            if (chk?.IsChecked != true)
                return "";

            var lst = TimListBox("LstCnOngACMV");
            return LayNoiDungItem(lst?.SelectedItem);
        }

        private void ChkDungCnOngAcmv_Changed(
            object sender,
            RoutedEventArgs e)
        {
            CapNhatTrangThaiCnOngAcmv();
        }

        private void CapNhatTrangThaiCnOngAcmv()
        {
            var chk =
                FindName("ChkDungCnOngACMV") as System.Windows.Controls.CheckBox;
            var lst = TimListBox("LstCnOngACMV");
            var txt = TimTextBox("TxtCnOngACMVThem");

            bool enabled = chk?.IsChecked == true;

            if (lst != null)
            {
                lst.IsEnabled = enabled;
                lst.Opacity = enabled ? 1.0 : 0.45;
            }

            if (txt != null)
            {
                txt.IsEnabled = enabled;
                txt.Opacity = enabled ? 1.0 : 0.45;
            }
        }

        private void TxtCnOngAcmvThem_KeyDown(
            object sender,
            WpfKeyEventArgs e)
        {
            if (e.Key != WpfKey.Enter)
                return;

            WpfTextBox txt = sender as WpfTextBox;
            if (txt == null)
                return;

            string value = (txt.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value))
                return;

            var lst = TimListBox("LstCnOngACMV");
            if (lst == null)
                return;

            bool existed =
                lst.Items
                    .Cast<object>()
                    .Any(x => LayNoiDungItem(x).Equals(
                        value,
                        StringComparison.OrdinalIgnoreCase));

            if (!existed)
            {
                lst.Items.Add(
                    new WpfListBoxItem
                    {
                        Content = value
                    });
            }

            foreach (object item in lst.Items)
            {
                if (LayNoiDungItem(item).Equals(
                    value,
                    StringComparison.OrdinalIgnoreCase))
                {
                    lst.SelectedItem = item;
                    lst.ScrollIntoView(item);
                    break;
                }
            }

            txt.Text = "";
            e.Handled = true;
        }

        private void LstCnOngAcmv_KeyDown(
            object sender,
            WpfKeyEventArgs e)
        {
            if (e.Key != WpfKey.Delete)
                return;

            WpfListBox lst = sender as WpfListBox;
            if (lst?.SelectedItem == null)
                return;

            if (lst.Items.Count <= 1)
                return;

            lst.Items.Remove(lst.SelectedItem);

            if (lst.Items.Count > 0)
                lst.SelectedIndex = 0;

            e.Handled = true;
        }

        private void TxtOngGioCotThem_KeyDown(
            object sender,
            WpfKeyEventArgs e)
        {
            if (e.Key != WpfKey.Enter)
                return;

            e.Handled = true;

            try
            {
                WpfTextBox txt = sender as WpfTextBox;
                if (txt == null)
                    return;

                string value = (txt.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(value))
                    return;

                string name = txt.Name ?? "";

                if (name.Contains("SizeOngGio"))
                {
                    if (_ctxACMV?.Sizes == null)
                        return;

                    PipeSizeItem target =
                        _ctxACMV.Sizes.FirstOrDefault(
                            x => x.SizeName.Equals(
                                value,
                                StringComparison.OrdinalIgnoreCase));

                    if (target == null)
                    {
                        string layerPrefix = GetLayerPrefix(_ctxACMV);
                        string layerName = $"{layerPrefix}_{value}";
                        short aci = GetExpectedAciColor(layerName);

                        target =
                            new PipeSizeItem
                            {
                                SizeName = value,
                                AciColor = aci,
                                LayerColorBrush = GetBrushFromAci(aci)
                            };

                        InsertPipeSizeItemSorted(_ctxACMV.Sizes, target);
                    }

                    WpfListBox lstSize =
                        TimListBox("LstSizeOngGioACMV");

                    if (lstSize != null)
                        lstSize.SelectedItem = target;

                    txt.Text = "";
                    return;
                }

                if (name.Contains("CnEi"))
                {
                    WpfListBox lst =
                        TimListBox("LstCnEiOngGioACMV");
                    if (lst == null)
                        return;

                    bool existed =
                        lst.Items
                            .Cast<object>()
                            .Any(x => LayNoiDungItem(x).Equals(
                                value,
                                StringComparison.OrdinalIgnoreCase));

                    if (!existed)
                    {
                        lst.Items.Add(
                            new WpfListBoxItem
                            {
                                Content = value
                            });
                    }

                    foreach (object item in lst.Items)
                    {
                        if (LayNoiDungItem(item).Equals(
                            value,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            lst.SelectedItem = item;
                            lst.ScrollIntoView(item);
                            break;
                        }
                    }

                    txt.Text = "";
                }
            }
            catch
            {
                // Tránh bật hộp thoại lỗi trong AutoCAD khi đang xử lý KeyDown.
            }
        }

        private void InsertPipeSizeItemSorted(
            ObservableCollection<PipeSizeItem> collection,
            PipeSizeItem item)
        {
            if (collection == null || item == null)
                return;

            double itemA = GetFirstNumberForSort(item.SizeName);
            double itemB = GetSecondNumberForSort(item.SizeName);

            int insertIndex = collection.Count;
            for (int i = 0; i < collection.Count; i++)
            {
                PipeSizeItem current = collection[i];
                double curA = GetFirstNumberForSort(current?.SizeName);
                double curB = GetSecondNumberForSort(current?.SizeName);

                if (itemA < curA ||
                    (Math.Abs(itemA - curA) < 1e-9 && itemB < curB) ||
                    (Math.Abs(itemA - curA) < 1e-9 &&
                     Math.Abs(itemB - curB) < 1e-9 &&
                     string.Compare(
                         item.SizeName,
                         current?.SizeName,
                         StringComparison.OrdinalIgnoreCase) < 0))
                {
                    insertIndex = i;
                    break;
                }
            }

            collection.Insert(insertIndex, item);
        }

        private static double GetFirstNumberForSort(string text)
        {
            MatchCollection matches =
                Regex.Matches(text ?? "", @"\d+(\.\d+)?");

            if (matches.Count == 0)
                return 0.0;

            if (double.TryParse(
                    matches[0].Value,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out double result))
                return result;

            return 0.0;
        }

        private static double GetSecondNumberForSort(string text)
        {
            MatchCollection matches =
                Regex.Matches(text ?? "", @"\d+(\.\d+)?");

            if (matches.Count < 2)
                return 0.0;

            if (double.TryParse(
                    matches[1].Value,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out double result))
                return result;

            return 0.0;
        }

        private void LstOngGioCot_KeyDown(
            object sender,
            WpfKeyEventArgs e)
        {
            if (e.Key != WpfKey.Delete)
                return;

            WpfListBox lst = sender as WpfListBox;
            if (lst?.SelectedItem == null)
                return;

            if (lst.Items.Count <= 1)
                return;

            object selected = lst.SelectedItem;
            string removed = LayNoiDungItem(selected);

            if (lst.Name == "LstSizeOngGioACMV" && _ctxACMV != null)
            {
                var item = _ctxACMV.Sizes.FirstOrDefault(
                    x => x.SizeName.Equals(
                        removed,
                        StringComparison.OrdinalIgnoreCase));

                if (item != null)
                    _ctxACMV.Sizes.Remove(item);

                if (_ctxACMV.Sizes.Count > 0)
                    lst.SelectedIndex = 0;

                e.Handled = true;
                return;
            }

            lst.Items.Remove(selected);

            if (lst.Items.Count > 0)
                lst.SelectedIndex = 0;

            e.Handled = true;
        }

        private void CmbHeThong_SelectionChanged(
            object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            PipeUiContext ctx = GetContext(sender);
            CapNhatComboVatLieuAn(ctx);
            CapNhatSizeTheoVatLieu(ctx);
            CapNhatMauTheoPrefix(ctx);
        }

        private void CmbVatLieuOng_SelectionChanged(
            object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            PipeUiContext ctx = GetContext(sender);
            CapNhatSizeTheoVatLieu(ctx);
        }

        private void LstVatLieuOngBang_SelectionChanged(
            object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            PipeUiContext ctx = GetContext(sender);
            CapNhatComboVatLieuAn(ctx);
            CapNhatSizeTheoVatLieu(ctx);
        }

        private void TxtVatLieuOngBoSung_KeyDown(
            object sender,
            WpfKeyEventArgs e)
        {
            if (e.Key != WpfKey.Enter)
                return;

            PipeUiContext ctx = GetContext(sender);

            if (ctx?.TxtVatLieuThem == null ||
                ctx.LstVatLieu == null)
            {
                return;
            }

            string newMaterial =
                ctx.TxtVatLieuThem.Text.Trim();

            if (string.IsNullOrWhiteSpace(newMaterial))
                return;

            bool existed =
                ctx.LstVatLieu.Items
                    .Cast<object>()
                    .Any(x => LayNoiDungItem(x).Equals(
                        newMaterial,
                        StringComparison.OrdinalIgnoreCase));

            if (!existed)
            {
                ctx.LstVatLieu.Items.Add(
                    new WpfListBoxItem
                    {
                        Content = newMaterial.ToUpper()
                    });
            }

            foreach (object item in ctx.LstVatLieu.Items)
            {
                if (LayNoiDungItem(item).Equals(
                    newMaterial,
                    StringComparison.OrdinalIgnoreCase))
                {
                    ctx.LstVatLieu.SelectedItem = item;
                    ctx.LstVatLieu.ScrollIntoView(item);
                    break;
                }
            }

            ctx.TxtVatLieuThem.Text = "";
            CapNhatComboVatLieuAn(ctx);
            CapNhatSizeTheoVatLieu(ctx);
            e.Handled = true;
        }

        private void LstVatLieuOngBang_KeyDown(
            object sender,
            WpfKeyEventArgs e)
        {
            if (e.Key != WpfKey.Delete)
                return;

            PipeUiContext ctx = GetContext(sender);
            WpfListBox lst = sender as WpfListBox;

            if (lst == null)
                lst = ctx?.LstVatLieu;

            if (lst == null || lst.SelectedItem == null)
                return;

            if (lst.Items.Count <= 1)
                return;

            object selected = lst.SelectedItem;
            lst.Items.Remove(selected);

            if (lst.Items.Count > 0)
                lst.SelectedIndex = 0;

            CapNhatComboVatLieuAn(ctx);
            CapNhatSizeTheoVatLieu(ctx);
            e.Handled = true;
        }

        private void TxtCustomSize_KeyDown(
            object sender,
            WpfKeyEventArgs e)
        {
            if (e.Key != WpfKey.Enter)
                return;

            PipeUiContext ctx = GetContext(sender);

            if (ctx?.TxtSizeThem == null)
                return;

            string newSize = ctx.TxtSizeThem.Text.Trim();

            if (string.IsNullOrWhiteSpace(newSize))
                return;

            List<string> currentSizes =
                ctx.Sizes.Select(x => x.SizeName).ToList();

            if (!currentSizes.Contains(
                newSize,
                StringComparer.OrdinalIgnoreCase))
            {
                currentSizes.Add(newSize);
            }

            CapNhatVaSapXepDanhSachSize(
                ctx,
                currentSizes,
                newSize);

            ctx.TxtSizeThem.Text = "";
            e.Handled = true;
        }

        private void LstSizeOng_KeyDown(
            object sender,
            WpfKeyEventArgs e)
        {
            if (e.Key != WpfKey.Delete)
                return;

            PipeUiContext ctx = GetContext(sender);

            if (ctx?.LstSize?.SelectedItem is PipeSizeItem selected)
            {
                ctx.Sizes.Remove(selected);
                e.Handled = true;
            }
        }

        private double LayWidthTuSize(string size)
        {
            string s = (size ?? "").Trim();

            // Ống gió (WxH): giữ độ dày theo kích thước như cũ
            if (Regex.IsMatch(
                s,
                @"\d+(\.\d+)?\s*[xX×]\s*\d+(\.\d+)?"))
            {
                var matches =
                    Regex.Matches(s, @"\d+(\.\d+)?");

                if (matches.Count > 0)
                {
                    return matches
                        .Cast<Match>()
                        .Max(m => double.Parse(
                            m.Value,
                            CultureInfo.InvariantCulture));
                }

                return FixedDnPipeDisplayWidth;
            }

            // Ống DN / ống đồng / còn lại: cùng 1 độ dày cố định
            // (tránh DN400 đè chữ vì nét quá dày)
            return FixedDnPipeDisplayWidth;
        }

        private void BtnVeOng_Click(
            object sender,
            RoutedEventArgs e)
        {
            PipeUiContext ctx = GetContext(sender);

            var doc =
                Autodesk.AutoCAD.ApplicationServices.Core.Application
                    .DocumentManager
                    .MdiActiveDocument;

            if (doc == null)
                return;

            var db = doc.Database;

            string size = GetSelectedPipeSizeName(ctx);

            if (string.IsNullOrEmpty(size))
            {
                MessageBox.Show(
                    "Vui lòng chọn Size ống trước khi vẽ!",
                    "Cảnh báo");

                return;
            }

            double plineWidth = LayWidthTuSize(size);
            string layerName =
                $"{GetLayerPrefix(ctx)}_{CleanLayerText(size)}";
            bool isOngGio = CheckIsOngGio(ctx);

            using (doc.LockDocument())
            {
                Autodesk.AutoCAD.ApplicationServices.Core.Application
                    .SetSystemVariable(
                        "PLINEWID",
                        plineWidth);

                Autodesk.AutoCAD.ApplicationServices.Core.Application
                    .SetSystemVariable(
                        "CECOLOR",
                        "BYLAYER");

                using (Transaction tr =
                    db.TransactionManager.StartTransaction())
                {
                    EnsureLayerExists(
                        tr,
                        db,
                        layerName,
                        isOngGio);

                    LayerTable lt =
                        (LayerTable)tr.GetObject(
                            db.LayerTableId,
                            OpenMode.ForRead);

                    db.Clayer = lt[layerName];
                    tr.Commit();
                }
            }

            _currentLayerNameForText = layerName;
            _currentPlineWidth = plineWidth;
            _lastPlineId = ObjectId.Null;
            _pendingPlineIds.Clear();

            StartPlineTextWatcher(doc);

            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();

            doc.SendStringToExecute(
                "._PLINE ",
                true,
                false,
                false);
        }

        private void BtnVeOng2_Click(
            object sender,
            RoutedEventArgs e)
        {
            PipeUiContext ctx = GetContext(sender);

            var doc =
                Autodesk.AutoCAD.ApplicationServices.Core.Application
                    .DocumentManager
                    .MdiActiveDocument;

            if (doc == null)
                return;

            var db = doc.Database;
            var ed = doc.Editor;

            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();

            // Chọn text trên bản vẽ để lấy kích thước
            PromptEntityOptions peo =
                new PromptEntityOptions(
                    "\n[VẼ ỐNG 2] Chọn text chứa kích thước ống " +
                    "(vd: PPR DN20, EAL 200x200 EI60): ")
                {
                    AllowNone = false
                };

            peo.SetRejectMessage("\nChỉ chọn TEXT hoặc MTEXT.");
            peo.AddAllowedClass(typeof(DBText), false);
            peo.AddAllowedClass(typeof(MText), false);

            PromptEntityResult per = ed.GetEntity(peo);

            if (per.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\n[VẼ ỐNG 2] Đã hủy.");
                return;
            }

            string rawText = "";

            using (doc.LockDocument())
            using (Transaction tr =
                db.TransactionManager.StartTransaction())
            {
                Entity ent =
                    tr.GetObject(per.ObjectId, OpenMode.ForRead)
                        as Entity;

                if (ent is DBText dbText)
                    rawText = dbText.TextString ?? "";
                else if (ent is MText mText)
                    rawText = mText.Contents ?? mText.Text ?? "";

                tr.Commit();
            }

            // MText có thể chứa formatting codes
            rawText = Regex.Replace(
                rawText,
                @"\\[A-Za-z][^;]*;",
                " ");
            rawText = Regex.Replace(rawText, @"[{}]", " ");
            rawText = Regex.Replace(rawText, @"\s+", " ").Trim();

            if (string.IsNullOrWhiteSpace(rawText))
            {
                MessageBox.Show(
                    "Không đọc được nội dung text đã chọn!",
                    "Cảnh báo");
                return;
            }

            string size = ExtractSizeFromDrawingText(rawText);

            if (string.IsNullOrWhiteSpace(size))
            {
                MessageBox.Show(
                    $"Không nhận diện được kích thước ống từ text:\n\"{rawText}\"\n\n" +
                    "Ví dụ hợp lệ: PPR DN20, DN50, 200x200, EAL 500x200 EI60",
                    "Cảnh báo");
                return;
            }

            // Ghép CN/EI từ bảng UI nếu đang tích (không lấy từ text bản vẽ)
            size = AppendCnFromUiIfSelected(ctx, size);

            double plineWidth = LayWidthTuSize(size);
            string layerName =
                $"{GetLayerPrefix(ctx)}_{CleanLayerText(size)}";
            bool isOngGio = CheckIsOngGio(ctx) ||
                            size.IndexOf('x') >= 0 ||
                            size.IndexOf('X') >= 0;

            ed.WriteMessage(
                $"\n[VẼ ỐNG 2] Text: \"{rawText}\" → Size: {size}");
            ed.WriteMessage(
                $"\n[VẼ ỐNG 2] Layer: {layerName}");

            using (doc.LockDocument())
            {
                Autodesk.AutoCAD.ApplicationServices.Core.Application
                    .SetSystemVariable("PLINEWID", plineWidth);

                Autodesk.AutoCAD.ApplicationServices.Core.Application
                    .SetSystemVariable("CECOLOR", "BYLAYER");

                using (Transaction tr =
                    db.TransactionManager.StartTransaction())
                {
                    EnsureLayerExists(
                        tr,
                        db,
                        layerName,
                        isOngGio);

                    LayerTable lt =
                        (LayerTable)tr.GetObject(
                            db.LayerTableId,
                            OpenMode.ForRead);

                    db.Clayer = lt[layerName];
                    tr.Commit();
                }
            }

            _currentLayerNameForText = layerName;
            _currentPlineWidth = plineWidth;
            _lastPlineId = ObjectId.Null;
            _pendingPlineIds.Clear();

            StartPlineTextWatcher(doc);

            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();

            doc.SendStringToExecute(
                "._PLINE ",
                true,
                false,
                false);
        }

        /// <summary>
        /// Lấy size từ text bản vẽ: DN20, 200x200, kèm EI/CN nếu có.
        /// </summary>
        private string ExtractSizeFromDrawingText(string sourceText)
        {
            if (string.IsNullOrWhiteSpace(sourceText))
                return "";

            string normalized =
                NormalizePipeLabel(sourceText)
                    .Replace(',', '.')
                    .ToUpperInvariant();

            // DN / D / Ø
            string sizePart = ExtractSizeOnlyText(sourceText);

            // Nếu ExtractSizeOnlyText rỗng, thử DN không khoảng
            if (string.IsNullOrWhiteSpace(sizePart))
            {
                Match dn =
                    Regex.Match(
                        normalized,
                        @"(?<![A-Z0-9])DN\s*(\d{1,4}(?:\.\d+)?)",
                        RegexOptions.IgnoreCase);

                if (dn.Success)
                    sizePart = "DN" + dn.Groups[1].Value;
            }

            // Ống gió: WxH
            if (string.IsNullOrWhiteSpace(sizePart))
            {
                Match rect =
                    Regex.Match(
                        normalized,
                        @"(?<!\d)(\d{2,4})\s*[xX×]\s*(\d{2,4})(?!\d)");

                if (rect.Success)
                    sizePart =
                        rect.Groups[1].Value + "x" +
                        rect.Groups[2].Value;
            }

            if (string.IsNullOrWhiteSpace(sizePart))
                return "";

            // Chỉ lấy kích thước (DN / WxH) — không lấy EI / CN
            sizePart = Regex.Replace(
                sizePart,
                @"\s+",
                "");

            return sizePart;
        }

        /// <summary>
        /// Nếu trên bảng đang tích CN (ống) hoặc CN/EI (ống gió)
        /// thì ghép vào size lấy từ bản vẽ.
        /// </summary>
        private string AppendCnFromUiIfSelected(
            PipeUiContext ctx,
            string size)
        {
            if (ctx == null || string.IsNullOrWhiteSpace(size))
                return size;

            // Đã có CN/EI trong size rồi thì thôi
            string upper = size.ToUpperInvariant();
            if (upper.Contains("_CN") ||
                upper.Contains("_EI") ||
                Regex.IsMatch(upper, @"(^|[^A-Z])CN\d") ||
                Regex.IsMatch(upper, @"(^|[^A-Z])EI\d"))
            {
                return size;
            }

            if (ctx.Suffix != "ACMV")
                return size;

            var chkOngGio =
                FindName("ChkNhomOngGioACMV") as System.Windows.Controls.CheckBox;
            bool isOngGioGroup = chkOngGio?.IsChecked == true;

            string cnPart = "";

            if (isOngGioGroup)
            {
                // Ống gió: dùng ChkDungCnEi + LstCnEiOngGioACMV
                cnPart = GetSelectedCnEi();
            }
            else
            {
                // Ống thường: dùng ChkDungCnOngACMV + LstCnOngACMV
                cnPart = GetSelectedCnOngAcmv();
            }

            if (string.IsNullOrWhiteSpace(cnPart))
                return size;

            return $"{size}_{cnPart.Trim()}";
        }

        private Polyline ConvertCurveToWidePolyline(
            Curve curve,
            string layerName,
            double width)
        {
            if (curve == null)
                return null;

            Polyline pline = new Polyline();
            pline.SetDatabaseDefaults();
            pline.Layer = layerName;
            pline.Color =
                Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                    ColorMethod.ByLayer,
                    256);
            pline.Linetype = "ByLayer";
            pline.LineWeight = LineWeight.ByLayer;

            if (curve is Line line)
            {
                pline.AddVertexAt(
                    0,
                    new Point2d(line.StartPoint.X, line.StartPoint.Y),
                    0, width, width);
                pline.AddVertexAt(
                    1,
                    new Point2d(line.EndPoint.X, line.EndPoint.Y),
                    0, width, width);
            }
            else if (curve is Polyline sourcePline &&
                     sourcePline.NumberOfVertices >= 2)
            {
                for (int i = 0; i < sourcePline.NumberOfVertices; i++)
                {
                    Point2d pt = sourcePline.GetPoint2dAt(i);
                    double bulge = sourcePline.GetBulgeAt(i);
                    pline.AddVertexAt(i, pt, bulge, width, width);
                }

                pline.Closed = sourcePline.Closed;
            }
            else
            {
                // Fallback: lấy 2 điểm đầu-cuối
                try
                {
                    Point3d sp = curve.StartPoint;
                    Point3d ep = curve.EndPoint;

                    if (sp.DistanceTo(ep) < 0.001)
                        return null;

                    pline.AddVertexAt(
                        0,
                        new Point2d(sp.X, sp.Y),
                        0, width, width);
                    pline.AddVertexAt(
                        1,
                        new Point2d(ep.X, ep.Y),
                        0, width, width);
                }
                catch
                {
                    return null;
                }
            }

            pline.ConstantWidth = width;
            return pline;
        }

        private void BtnDoiLayer_Click(
            object sender,
            RoutedEventArgs e)
        {
            PipeUiContext ctx = GetContext(sender);

            var doc =
                Autodesk.AutoCAD.ApplicationServices.Core.Application
                    .DocumentManager
                    .MdiActiveDocument;

            if (doc == null)
                return;

            var db = doc.Database;
            var ed = doc.Editor;

            string size = GetSelectedPipeSizeName(ctx);

            if (string.IsNullOrEmpty(size))
            {
                MessageBox.Show(
                    "Vui lòng chọn Size ống trước khi thực hiện đổi Layer!",
                    "Cảnh báo");

                return;
            }

            double plineWidth = LayWidthTuSize(size);
            string layerName =
                $"{GetLayerPrefix(ctx)}_{CleanLayerText(size)}";
            string shortSizeLabel = GetShortSizeLabel(size);
            bool isOngGio = CheckIsOngGio(ctx);

            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();

            PromptSelectionOptions pso =
                new PromptSelectionOptions();

            pso.MessageForAdding =
                $"\nQuét chọn các đối tượng để chuyển sang Layer [{layerName}]: ";

            PromptSelectionResult psr = ed.GetSelection(pso);

            if (psr.Status != PromptStatus.OK ||
                psr.Value.Count == 0)
            {
                return;
            }

            using (doc.LockDocument())
            {
                using (Transaction tr =
                    db.TransactionManager.StartTransaction())
                {
                    EnsureLayerExists(
                        tr,
                        db,
                        layerName,
                        isOngGio);

                    BlockTableRecord btr =
                        (BlockTableRecord)tr.GetObject(
                            db.CurrentSpaceId,
                            OpenMode.ForWrite);

                    HashSet<ObjectId> selectedIds =
                        new HashSet<ObjectId>(
                            psr.Value
                                .Cast<SelectedObject>()
                                .Where(item =>
                                    item != null &&
                                    item.ObjectId != ObjectId.Null)
                                .Select(item => item.ObjectId));

                    Dictionary<ObjectId, string> selectedCurveLayers =
                        new Dictionary<ObjectId, string>();

                    HashSet<ObjectId> textIdsToUpdate =
                        new HashSet<ObjectId>();

                    // Danh sách polyline kết quả (sau convert) để tạo chữ
                    List<Polyline> resultPolylines =
                        new List<Polyline>();

                    int convertedLineCount = 0;
                    int updatedPlineCount = 0;

                    // --- Bước 1: phân loại + tìm chữ đi kèm ---
                    foreach (ObjectId id in selectedIds)
                    {
                        Entity selectedEntity =
                            tr.GetObject(
                                id,
                                OpenMode.ForRead,
                                false) as Entity;

                        if (selectedEntity is Curve)
                        {
                            selectedCurveLayers[id] =
                                selectedEntity.Layer;
                        }

                        if (selectedEntity is DBText ||
                            selectedEntity is MText)
                        {
                            textIdsToUpdate.Add(id);
                        }
                    }

                    foreach (ObjectId textId in
                        FindAssociatedPipeLabels(
                            tr,
                            selectedCurveLayers))
                    {
                        textIdsToUpdate.Add(textId);
                    }

                    // --- Bước 2: xử lý từng đối tượng đã chọn ---
                    foreach (ObjectId id in selectedIds)
                    {
                        Entity ent =
                            tr.GetObject(
                                id,
                                OpenMode.ForWrite,
                                false) as Entity;

                        if (ent == null)
                            continue;

                        // Text đã chọn → chỉ đổi layer + nội dung
                        if (ent is DBText || ent is MText)
                        {
                            ApplyLayerAndLabel(
                                ent,
                                layerName,
                                plineWidth,
                                shortSizeLabel);
                            continue;
                        }

                        // Polyline có sẵn → set layer + độ dày
                        if (ent is Polyline existingPline)
                        {
                            ApplyLayerAndLabel(
                                existingPline,
                                layerName,
                                plineWidth,
                                shortSizeLabel);

                            resultPolylines.Add(existingPline);
                            updatedPlineCount++;
                            continue;
                        }

                        // Line / Curve khác → chuyển thành Polyline có độ dày
                        if (ent is Curve curve)
                        {
                            Polyline newPline =
                                ConvertCurveToWidePolyline(
                                    curve,
                                    layerName,
                                    plineWidth);

                            if (newPline == null)
                            {
                                // Không convert được → chỉ đổi layer
                                ApplyLayerAndLabel(
                                    ent,
                                    layerName,
                                    plineWidth,
                                    shortSizeLabel);
                                continue;
                            }

                            btr.AppendEntity(newPline);
                            tr.AddNewlyCreatedDBObject(newPline, true);
                            resultPolylines.Add(newPline);

                            // Xóa Line/Curve cũ
                            ent.Erase();
                            convertedLineCount++;
                        }
                        else
                        {
                            ApplyLayerAndLabel(
                                ent,
                                layerName,
                                plineWidth,
                                shortSizeLabel);
                        }
                    }

                    // --- Bước 3: cập nhật chữ đi kèm (nếu có) ---
                    foreach (ObjectId textId in textIdsToUpdate)
                    {
                        if (selectedIds.Contains(textId))
                            continue;

                        Entity textEntity =
                            tr.GetObject(
                                textId,
                                OpenMode.ForWrite,
                                false) as Entity;

                        if (textEntity != null)
                        {
                            ApplyLayerAndLabel(
                                textEntity,
                                layerName,
                                plineWidth,
                                shortSizeLabel);
                        }
                    }

                    // --- Bước 4: luôn tạo chữ DN nếu chưa có chữ đi kèm ---
                    int createdLabelCount = 0;

                    bool hasExistingLabels =
                        textIdsToUpdate.Count > 0;

                    if (!hasExistingLabels)
                    {
                        foreach (Polyline pline in resultPolylines)
                        {
                            if (pline == null ||
                                pline.IsErased ||
                                !pline.ObjectId.IsValid)
                            {
                                continue;
                            }

                            DBText newLabel =
                                CreateAutomaticSizeLabel(
                                    db,
                                    pline,
                                    layerName,
                                    shortSizeLabel,
                                    plineWidth);

                            if (newLabel == null)
                                continue;

                            btr.AppendEntity(newLabel);
                            tr.AddNewlyCreatedDBObject(
                                newLabel,
                                true);

                            newLabel.AdjustAlignment(db);
                            createdLabelCount++;
                        }
                    }

                    tr.Commit();

                    ed.WriteMessage(
                        $"\n[{LayerChangeBuild}] Đã chuyển " +
                        $"{selectedIds.Count} đối tượng " +
                        $"(pline: {updatedPlineCount}, " +
                        $"line→pline: {convertedLineCount}), " +
                        $"cập nhật {textIdsToUpdate.Count} chữ, " +
                        $"tạo mới {createdLabelCount} chữ " +
                        $"→ Layer: {layerName}, Width: {plineWidth}");
                }

                ed.Regen();
            }
        }

        private HashSet<ObjectId> FindAssociatedPipeLabels(
            Transaction tr,
            Dictionary<ObjectId, string> selectedCurveLayers)
        {
            HashSet<ObjectId> result =
                new HashSet<ObjectId>();

            if (tr == null ||
                selectedCurveLayers == null ||
                selectedCurveLayers.Count == 0)
            {
                return result;
            }

            Dictionary<ObjectId, HashSet<ObjectId>>
                selectedCurvesByOwner =
                    new Dictionary<ObjectId, HashSet<ObjectId>>();

            foreach (KeyValuePair<ObjectId, string> pair
                in selectedCurveLayers)
            {
                Curve curve =
                    tr.GetObject(
                        pair.Key,
                        OpenMode.ForRead,
                        false) as Curve;

                if (curve == null)
                    continue;

                if (!selectedCurvesByOwner.TryGetValue(
                    curve.OwnerId,
                    out HashSet<ObjectId> ownerIds))
                {
                    ownerIds = new HashSet<ObjectId>();
                    selectedCurvesByOwner[curve.OwnerId] = ownerIds;
                }

                ownerIds.Add(pair.Key);
            }

            foreach (KeyValuePair<ObjectId, HashSet<ObjectId>>
                ownerPair in selectedCurvesByOwner)
            {
                BlockTableRecord owner =
                    tr.GetObject(
                        ownerPair.Key,
                        OpenMode.ForRead,
                        false) as BlockTableRecord;

                if (owner == null)
                    continue;

                HashSet<string> oldLayers =
                    new HashSet<string>(
                        ownerPair.Value
                            .Where(selectedCurveLayers.ContainsKey)
                            .Select(id => selectedCurveLayers[id]),
                        StringComparer.OrdinalIgnoreCase);

                Dictionary<string, List<Curve>> curvesByLayer =
                    new Dictionary<string, List<Curve>>(
                        StringComparer.OrdinalIgnoreCase);

                List<Curve> allCurves =
                    new List<Curve>();

                List<Entity> labelCandidates =
                    new List<Entity>();

                foreach (ObjectId id in owner)
                {
                    Entity entity =
                        tr.GetObject(
                            id,
                            OpenMode.ForRead,
                            false) as Entity;

                    if (entity == null)
                        continue;

                    Curve curve = entity as Curve;

                    if (curve != null)
                    {
                        allCurves.Add(curve);

                        if (oldLayers.Contains(curve.Layer))
                        {
                            if (!curvesByLayer.TryGetValue(
                                curve.Layer,
                                out List<Curve> layerCurves))
                            {
                                layerCurves = new List<Curve>();
                                curvesByLayer[curve.Layer] = layerCurves;
                            }

                            layerCurves.Add(curve);
                        }

                        continue;
                    }

                    if (entity is DBText || entity is MText)
                        labelCandidates.Add(entity);
                }

                foreach (Entity label in labelCandidates)
                {
                    string oldLabelText = GetLabelText(label);

                    string matchingOldLayer =
                        oldLayers.FirstOrDefault(
                            oldLayer =>
                                AreSamePipeLabel(
                                    oldLabelText,
                                    oldLayer) ||
                                AreSamePipeLabel(
                                    label.Layer,
                                    oldLayer));

                    List<Curve> candidateCurves;

                    if (!string.IsNullOrWhiteSpace(
                            matchingOldLayer) &&
                        curvesByLayer.TryGetValue(
                            matchingOldLayer,
                            out List<Curve> matchingLayerCurves))
                    {
                        candidateCurves = matchingLayerCurves;
                    }
                    else if (
                        LooksLikePipeLabel(oldLabelText) ||
                        LooksLikePipeLabel(label.Layer))
                    {
                        candidateCurves = allCurves;
                    }
                    else
                    {
                        continue;
                    }

                    Point3d labelPoint = GetLabelPoint(label);
                    double labelRotation = GetLabelRotation(label);
                    Curve nearestCurve = null;
                    double nearestDistance = double.MaxValue;
                    double nearestScore = double.MaxValue;

                    foreach (Curve curve in candidateCurves)
                    {
                        try
                        {
                            Point3d closestPoint;

                            try
                            {
                                closestPoint =
                                    curve.GetClosestPointTo(
                                        labelPoint,
                                        Vector3d.ZAxis,
                                        false);
                            }
                            catch
                            {
                                closestPoint =
                                    curve.GetClosestPointTo(
                                        labelPoint,
                                        false);
                            }

                            double deltaX =
                                closestPoint.X - labelPoint.X;

                            double deltaY =
                                closestPoint.Y - labelPoint.Y;

                            double distance =
                                Math.Sqrt(
                                    (deltaX * deltaX) +
                                    (deltaY * deltaY));

                            // Chữ gắn với ống phải gần như song song.
                            if (!IsTextParallelToCurve(
                                curve,
                                closestPoint,
                                labelRotation))
                            {
                                continue;
                            }

                            double angleScore =
                                TinhGocLech(
                                    curve,
                                    closestPoint,
                                    labelRotation) * 2000.0;

                            double score = distance + angleScore;

                            if (score < nearestScore)
                            {
                                nearestScore = score;
                                nearestDistance = distance;
                                nearestCurve = curve;
                            }
                        }
                        catch
                        {
                            // Bỏ qua đường cong không xác định được điểm gần nhất.
                        }
                    }

                    if (nearestCurve == null ||
                        !ownerPair.Value.Contains(
                            nearestCurve.ObjectId))
                    {
                        continue;
                    }

                    if (nearestDistance <=
                        GetMaximumLabelDistance(
                            nearestCurve,
                            label))
                    {
                        result.Add(label.ObjectId);
                    }
                }
            }

            return result;
        }

        private bool LooksLikePipeLabel(string value)
        {
            string normalized = NormalizePipeLabel(value);

            return Regex.IsMatch(
                normalized,
                @"^(FF|ACMV|CTN)_",
                RegexOptions.IgnoreCase);
        }

        private double GetLabelRotation(Entity entity)
        {
            if (entity is DBText dbText)
                return dbText.Rotation;

            if (entity is MText mText)
                return mText.Rotation;

            return 0;
        }

        private bool AreSamePipeLabel(
            string first,
            string second)
        {
            return string.Equals(
                NormalizePipeLabel(first),
                NormalizePipeLabel(second),
                StringComparison.OrdinalIgnoreCase);
        }

        private string NormalizePipeLabel(string value)
        {
            string normalized =
                (value ?? "")
                    .Replace("\r", "")
                    .Replace("\n", "")
                    .Trim();

            return Regex.Replace(
                normalized,
                @"\s+",
                " ");
        }

        private string GetLabelText(Entity entity)
        {
            if (entity is DBText dbText)
                return (dbText.TextString ?? "").Trim();

            if (entity is MText mText)
            {
                return (mText.Text ?? "")
                    .Replace("\r", "")
                    .Replace("\n", "")
                    .Trim();
            }

            return "";
        }

        private Point3d GetLabelPoint(Entity entity)
        {
            if (entity is DBText dbText)
            {
                if (dbText.Justify != AttachmentPoint.BaseLeft &&
                    (dbText.AlignmentPoint.X != 0 ||
                     dbText.AlignmentPoint.Y != 0 ||
                     dbText.AlignmentPoint.Z != 0))
                {
                    return dbText.AlignmentPoint;
                }

                return dbText.Position;
            }

            if (entity is MText mText)
                return mText.Location;

            return Point3d.Origin;
        }

        private double GetMaximumLabelDistance(
            Curve curve,
            Entity label)
        {
            double curveWidth = 0;

            if (curve is Polyline polyline)
            {
                curveWidth = polyline.ConstantWidth;

                for (int index = 0;
                    index < polyline.NumberOfVertices;
                    index++)
                {
                    curveWidth = Math.Max(
                        curveWidth,
                        polyline.GetStartWidthAt(index));

                    curveWidth = Math.Max(
                        curveWidth,
                        polyline.GetEndWidthAt(index));
                }
            }

            double textHeight = 0;

            if (label is DBText dbText)
                textHeight = dbText.Height;
            else if (label is MText mText)
                textHeight = mText.TextHeight;

            return Math.Max(
                1500.0,
                (curveWidth / 2.0) +
                (textHeight * 6.0) +
                200.0);
        }

        private void SetPolylineWidth(Polyline polyline, double width)
        {
            if (polyline == null)
                return;

            polyline.ConstantWidth = width;

            for (int i = 0; i < polyline.NumberOfVertices; i++)
            {
                polyline.SetStartWidthAt(i, width);
                polyline.SetEndWidthAt(i, width);
            }
        }

        private string GetShortSizeLabel(string sizeOrLayer)
        {
            if (string.IsNullOrWhiteSpace(sizeOrLayer))
                return "";

            string source = sizeOrLayer.Trim();
            string sizePart = ExtractSizeOnlyText(source);

            // Fallback: phần sau _ cuối nếu chưa lấy được size
            if (string.IsNullOrWhiteSpace(sizePart) &&
                source.Contains("_"))
            {
                sizePart = source.Split('_').Last().Trim();
            }

            if (string.IsNullOrWhiteSpace(sizePart))
                sizePart = source;

            // Lấy CN / EI nếu có trong chuỗi (vd: DN20_CN25, ..._EI60)
            string upper = source.ToUpperInvariant();
            string cnEi = "";

            Match ei =
                Regex.Match(
                    upper,
                    @"(?<![A-Z0-9])EI\s*(\d{2,3})(?![A-Z0-9])");

            Match cn =
                Regex.Match(
                    upper,
                    @"(?<![A-Z0-9])CN\s*(\d{1,3})(?![A-Z0-9])");

            if (ei.Success)
                cnEi = "EI" + ei.Groups[1].Value;
            else if (cn.Success)
                cnEi = "CN" + cn.Groups[1].Value;

            if (!string.IsNullOrWhiteSpace(cnEi))
            {
                // Tránh lặp nếu sizePart đã chứa CN/EI
                string sizeUpper = sizePart.ToUpperInvariant();
                if (!sizeUpper.Contains(cnEi))
                    return $"{sizePart} {cnEi}";
            }

            return sizePart;
        }

        private void ApplyLayerAndLabel(
            Entity entity,
            string layerName,
            double plineWidth,
            string shortSizeLabel = null)
        {
            if (entity == null)
                return;

            entity.Layer = layerName;

            entity.Color =
                Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                    ColorMethod.ByLayer,
                    256);

            entity.Linetype = "ByLayer";
            entity.LineWeight = LineWeight.ByLayer;

            double labelHeight =
                Math.Max(
                    plineWidth * LabelTextHeightToWidthRatio,
                    MinimumLabelTextHeight);

            if (entity is Polyline polyline)
            {
                SetPolylineWidth(polyline, plineWidth);
            }

            string displayText =
                !string.IsNullOrWhiteSpace(shortSizeLabel)
                    ? shortSizeLabel
                    : GetShortSizeLabel(layerName);

            if (entity is DBText dbText)
            {
                dbText.TextString = displayText;
                dbText.Height = labelHeight;

                if (dbText.Database != null)
                    dbText.AdjustAlignment(dbText.Database);
            }
            else if (entity is MText mText)
            {
                mText.Contents = displayText;
                mText.TextHeight = labelHeight;
            }
        }

        private bool TryGetClosestPointInPlan(
            Curve curve,
            Point3d sourcePoint,
            out Point3d closestPoint,
            out double planDistance)
        {
            closestPoint = Point3d.Origin;
            planDistance = double.MaxValue;

            if (curve == null)
                return false;

            try
            {
                try
                {
                    closestPoint =
                        curve.GetClosestPointTo(
                            sourcePoint,
                            Vector3d.ZAxis,
                            false);
                }
                catch
                {
                    Point3d flatPoint =
                        new Point3d(
                            sourcePoint.X,
                            sourcePoint.Y,
                            curve.StartPoint.Z);

                    closestPoint =
                        curve.GetClosestPointTo(
                            flatPoint,
                            false);
                }

                double deltaX =
                    closestPoint.X - sourcePoint.X;

                double deltaY =
                    closestPoint.Y - sourcePoint.Y;

                planDistance =
                    Math.Sqrt(
                        (deltaX * deltaX) +
                        (deltaY * deltaY));

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void AddUniquePoint(
            List<Point3d> points,
            Point3d point,
            double tolerance = 1.0)
        {
            if (points == null)
                return;

            bool alreadyExists =
                points.Any(
                    existing =>
                    {
                        double deltaX =
                            existing.X - point.X;

                        double deltaY =
                            existing.Y - point.Y;

                        return Math.Sqrt(
                            (deltaX * deltaX) +
                            (deltaY * deltaY)) <= tolerance;
                    });

            if (!alreadyExists)
                points.Add(point);
        }

        private List<Point3d> GetCircularCentersFromBlock(
            Transaction tr,
            BlockReference blockReference)
        {
            List<Point3d> centers =
                new List<Point3d>();

            if (tr == null || blockReference == null)
                return centers;

            try
            {
                BlockTableRecord definition =
                    tr.GetObject(
                        blockReference.BlockTableRecord,
                        OpenMode.ForRead,
                        false) as BlockTableRecord;

                if (definition == null)
                    return centers;

                foreach (ObjectId entityId in definition)
                {
                    Circle circle =
                        tr.GetObject(
                            entityId,
                            OpenMode.ForRead,
                            false) as Circle;

                    if (circle == null)
                        continue;

                    Point3d worldCenter =
                        circle.Center.TransformBy(
                            blockReference.BlockTransform);

                    AddUniquePoint(
                        centers,
                        worldCenter,
                        5.0);
                }
            }
            catch
            {
                // Block không đọc được hình tròn thì bỏ qua nhận diện tâm.
            }

            return centers;
        }

        private Dictionary<Curve, List<SprinklerProjectionData>>
            MapSprinklerCentersToCurves(
                List<Curve> originalCurves,
                List<Point3d> sprinklerCenters)
        {
            Dictionary<Curve, List<SprinklerProjectionData>> result =
                new Dictionary<Curve, List<SprinklerProjectionData>>();

            foreach (Curve curve in originalCurves)
            {
                result[curve] =
                    new List<SprinklerProjectionData>();
            }

            foreach (Point3d center in sprinklerCenters)
            {
                Curve bestCurve = null;
                Point3d bestPoint = Point3d.Origin;
                double bestDistance = double.MaxValue;

                foreach (Curve curve in originalCurves)
                {
                    if (!TryGetClosestPointInPlan(
                        curve,
                        center,
                        out Point3d pointOnCurve,
                        out double planDistance))
                    {
                        continue;
                    }

                    if (planDistance < bestDistance)
                    {
                        bestDistance = planDistance;
                        bestCurve = curve;
                        bestPoint = pointOnCurve;
                    }
                }

                if (bestCurve == null ||
                    bestDistance > SprinklerCenterSearchDistance)
                {
                    continue;
                }

                bool duplicate =
                    result[bestCurve].Any(
                        item =>
                        {
                            double deltaX =
                                item.Center.X - center.X;

                            double deltaY =
                                item.Center.Y - center.Y;

                            return Math.Sqrt(
                                (deltaX * deltaX) +
                                (deltaY * deltaY)) <= 5.0;
                        });

                if (!duplicate)
                {
                    result[bestCurve].Add(
                        new SprinklerProjectionData
                        {
                            Center = center,
                            PointOnCurve = bestPoint,
                            PlanDistance = bestDistance
                        });
                }
            }

            return result;
        }

        private int CreateSprinklerCenterConnections(
            Transaction tr,
            Database db,
            BlockTableRecord targetSpace,
            List<SegmentData> allSegments,
            Dictionary<Curve, List<SprinklerProjectionData>>
                sprinklerProjections,
            bool isOngGio)
        {
            int createdCount = 0;

            if (tr == null ||
                db == null ||
                targetSpace == null ||
                allSegments == null ||
                sprinklerProjections == null)
            {
                return createdCount;
            }

            foreach (
                KeyValuePair<Curve, List<SprinklerProjectionData>>
                    pair in sprinklerProjections)
            {
                List<SegmentData> parentSegments =
                    allSegments
                        .Where(
                            segment =>
                                segment.OriginalParent == pair.Key &&
                                !string.IsNullOrWhiteSpace(
                                    segment.Layer))
                        .ToList();

                if (parentSegments.Count == 0)
                    continue;

                foreach (
                    SprinklerProjectionData projection
                    in pair.Value)
                {
                    if (projection.PlanDistance <= 1.0)
                        continue;

                    SegmentData nearestSegment = null;
                    double nearestDistance = double.MaxValue;

                    foreach (SegmentData segment in parentSegments)
                    {
                        if (!TryGetClosestPointInPlan(
                            segment.Curve,
                            projection.PointOnCurve,
                            out Point3d ignoredPoint,
                            out double planDistance))
                        {
                            continue;
                        }

                        if (planDistance < nearestDistance)
                        {
                            nearestDistance = planDistance;
                            nearestSegment = segment;
                        }
                    }

                    if (nearestSegment == null)
                        continue;

                    EnsureLayerExists(
                        tr,
                        db,
                        nearestSegment.Layer,
                        isOngGio);

                    Polyline connector = new Polyline();
                    connector.SetDatabaseDefaults(db);
                    connector.Layer = nearestSegment.Layer;

                    connector.Color =
                        Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                            ColorMethod.ByLayer,
                            256);

                    connector.Linetype = "ByLayer";
                    connector.LineWeight = LineWeight.ByLayer;
                    connector.ConstantWidth = nearestSegment.Width;

                    connector.AddVertexAt(
                        0,
                        new Point2d(
                            projection.PointOnCurve.X,
                            projection.PointOnCurve.Y),
                        0,
                        nearestSegment.Width,
                        nearestSegment.Width);

                    connector.AddVertexAt(
                        1,
                        new Point2d(
                            projection.Center.X,
                            projection.Center.Y),
                        0,
                        nearestSegment.Width,
                        nearestSegment.Width);

                    targetSpace.AppendEntity(connector);
                    tr.AddNewlyCreatedDBObject(connector, true);
                    createdCount++;
                }
            }

            return createdCount;
        }

        private Dictionary<Curve, List<TextProjectionData>>
            MapTextsToOriginalCurves(
                List<Curve> originalCurves,
                List<TextData> texts)
        {
            Dictionary<Curve, List<TextProjectionData>> result =
                new Dictionary<Curve, List<TextProjectionData>>();

            foreach (Curve curve in originalCurves)
            {
                result[curve] =
                    new List<TextProjectionData>();
            }

            foreach (TextData text in texts)
            {
                Curve bestCurve = null;
                Point3d bestPoint = Point3d.Origin;
                double bestScore = double.MaxValue;

                foreach (Curve curve in originalCurves)
                {
                    if (!TryGetClosestPointInPlan(
                        curve,
                        text.Position,
                        out Point3d closestPoint,
                        out double planDistance))
                    {
                        continue;
                    }

                    if (planDistance > 3500.0)
                        continue;

                    // Bắt buộc chữ phải song song với ống.
                    // Chữ của nhánh vuông góc không được gán cho ống chính.
                    if (!IsTextParallelToCurve(
                        curve,
                        closestPoint,
                        text.Rotation))
                    {
                        continue;
                    }

                    double angleScore =
                        TinhGocLech(
                            curve,
                            closestPoint,
                            text.Rotation) * 5000.0;

                    double score =
                        planDistance + angleScore;

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestCurve = curve;
                        bestPoint = closestPoint;
                    }
                }

                if (bestCurve == null)
                    continue;

                try
                {
                    double distanceAlongCurve =
                        bestCurve.GetDistAtPoint(bestPoint);

                    result[bestCurve].Add(
                        new TextProjectionData
                        {
                            Text = text,
                            DistanceAlongCurve =
                                distanceAlongCurve,
                            MatchScore = bestScore
                        });
                }
                catch
                {
                    // Bỏ qua text không chiếu được lên chiều dài tuyến.
                }
            }

            foreach (Curve curve in originalCurves)
            {
                List<TextProjectionData> ordered =
                    result[curve]
                        .OrderBy(
                            item => item.DistanceAlongCurve)
                        .ThenBy(
                            item => item.MatchScore)
                        .ToList();

                List<TextProjectionData> filtered =
                    new List<TextProjectionData>();

                foreach (
                    TextProjectionData projection in ordered)
                {
                    TextProjectionData duplicate =
                        filtered.LastOrDefault(
                            item =>
                                Math.Abs(
                                    item.DistanceAlongCurve -
                                    projection.DistanceAlongCurve) <
                                10.0 &&
                                string.Equals(
                                    item.Text.LayerName,
                                    projection.Text.LayerName,
                                    StringComparison.OrdinalIgnoreCase));

                    if (duplicate == null)
                    {
                        filtered.Add(projection);
                    }
                    else if (
                        projection.MatchScore <
                        duplicate.MatchScore)
                    {
                        int duplicateIndex =
                            filtered.IndexOf(duplicate);

                        filtered[duplicateIndex] = projection;
                    }
                }

                result[curve] =
                    filtered
                        .OrderBy(
                            item => item.DistanceAlongCurve)
                        .ToList();
            }

            return result;
        }

        private List<double> GetTextSizeTransitionDistances(
            Curve curve,
            Dictionary<Curve, List<TextProjectionData>>
                projectionsByCurve,
            IEnumerable<double> preferredSplitDistances)
        {
            List<double> transitions =
                new List<double>();

            if (curve == null ||
                projectionsByCurve == null ||
                !projectionsByCurve.TryGetValue(
                    curve,
                    out List<TextProjectionData> projections) ||
                projections.Count < 2)
            {
                return transitions;
            }

            List<TextProjectionData> ordered =
                projections
                    .OrderBy(
                        item => item.DistanceAlongCurve)
                    .ToList();

            List<double> preferredDistances =
                (preferredSplitDistances ??
                 Enumerable.Empty<double>())
                    .OrderBy(distance => distance)
                    .ToList();

            double totalLength;

            try
            {
                totalLength =
                    curve.GetDistanceAtParameter(
                        curve.EndParam);
            }
            catch
            {
                return transitions;
            }

            for (int index = 0;
                index < ordered.Count - 1;
                index++)
            {
                TextProjectionData current = ordered[index];
                TextProjectionData next = ordered[index + 1];

                if (string.Equals(
                    current.Text.LayerName,
                    next.Text.LayerName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (next.DistanceAlongCurve -
                    current.DistanceAlongCurve < 20.0)
                {
                    continue;
                }

                double middleDistance =
                    (current.DistanceAlongCurve +
                     next.DistanceAlongCurve) / 2.0;

                List<double> geometryCandidates =
                    preferredDistances
                        .Where(
                            distance =>
                                distance >
                                current.DistanceAlongCurve + 1.0 &&
                                distance <
                                next.DistanceAlongCurve - 1.0)
                        .ToList();

                double transitionDistance =
                    geometryCandidates.Count > 0
                        ? geometryCandidates
                            .OrderBy(
                                distance =>
                                    Math.Abs(
                                        distance -
                                        middleDistance))
                            .First()
                        : middleDistance;

                if (transitionDistance > 1.0 &&
                    transitionDistance < totalLength - 1.0)
                {
                    transitions.Add(transitionDistance);
                }
            }

            return transitions;
        }

        private string ExtractSizeOnlyText(string sourceText)
        {
            string normalized =
                NormalizePipeLabel(sourceText);

            Match nominalSize =
                Regex.Match(
                    normalized,
                    @"(?<![A-Z0-9])(?:DN|D|Ø|Φ)\s*\d{1,4}(?:[\.,]\d+)?(?:\s*MM)?(?![A-Z0-9])",
                    RegexOptions.IgnoreCase);

            if (nominalSize.Success)
            {
                string result =
                    Regex.Replace(
                        nominalSize.Value,
                        @"\s+",
                        "")
                    .ToUpperInvariant();

                return Regex.Replace(
                    result,
                    @"MM$",
                    "",
                    RegexOptions.IgnoreCase);
            }

            Match pairedSize =
                Regex.Match(
                    normalized,
                    @"(?<!\d)\d{1,4}(?:[\.,]\d{1,2})?\s*(?:[/\\xX×-])\s*\d{1,4}(?:[\.,]\d{1,2})?(?!\d)");

            if (pairedSize.Success)
            {
                return Regex.Replace(
                    pairedSize.Value,
                    @"\s+",
                    " ").Trim();
            }

            // Tuyệt đối không lấy một con số đứng riêng.
            // Nhờ vậy các cao độ như FFL +7000, BOP +3500,
            // EL -1200 và số kích thước kiến trúc 3000
            // sẽ không bị hiểu là kích thước ống.
            return "";
        }

        private bool TryParseAutomaticPipeSize(
            PipeUiContext ctx,
            string sourceText,
            out string sizeText,
            out double pipeWidth)
        {
            sizeText = "";
            pipeWidth = 0;

            string normalized =
                NormalizePipeLabel(sourceText)
                    .Replace(',', '.')
                    .ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            // 1. Ký hiệu danh nghĩa: DN20, DN 25, DN50 mm...
            Match dnMatch =
                Regex.Match(
                    normalized,
                    @"(?<![A-Z0-9])DN\s*(?<SIZE>\d{1,4}(?:\.\d+)?)(?:\s*MM)?(?![A-Z0-9])",
                    RegexOptions.IgnoreCase);

            if (dnMatch.Success &&
                TryParsePositiveSize(
                    dnMatch.Groups["SIZE"].Value,
                    2000.0,
                    out double dnSize))
            {
                sizeText =
                    "DN" + FormatSizeNumber(dnSize);

                pipeWidth = dnSize;
                return true;
            }

            // 2. Đường kính ngoài: D60, D63, Ø60, Φ60...
            // Các cỡ thông dụng được đổi về DN.
            // Ví dụ D60 hoặc D63 được hiểu là DN50.
            Match outsideMatch =
                Regex.Match(
                    normalized,
                    @"(?<![A-Z0-9])(?<PREFIX>D|Ø|Φ)\s*(?<SIZE>\d{1,4}(?:\.\d+)?)(?:\s*MM)?(?![A-Z0-9])",
                    RegexOptions.IgnoreCase);

            if (outsideMatch.Success &&
                TryParsePositiveSize(
                    outsideMatch.Groups["SIZE"].Value,
                    2000.0,
                    out double outsideSize))
            {
                if (TryConvertOutsideDiameterToNominal(
                    outsideSize,
                    out double nominalSize))
                {
                    sizeText =
                        "DN" +
                        FormatSizeNumber(nominalSize);

                    pipeWidth = nominalSize;
                }
                else
                {
                    string prefix =
                        outsideMatch.Groups["PREFIX"]
                            .Value
                            .ToUpperInvariant();

                    sizeText =
                        prefix +
                        FormatSizeNumber(outsideSize);

                    pipeWidth = outsideSize;
                }

                return true;
            }

            string material =
                GetSelectedPipeMaterialName(ctx);

            // 3. Chỉ ống đồng mới được nhận cặp đường kính
            // không có ký hiệu DN hoặc D.
            if (LaOngDong(material))
            {
                Match copperMatch =
                    Regex.Match(
                        normalized,
                        @"(?<![\d\.])(?<FIRST>\d{1,2}(?:\.\d{1,2})?)\s*(?:[/\\xX×-])\s*(?<SECOND>\d{1,2}(?:\.\d{1,2})?)(?![\d\.])");

                if (copperMatch.Success &&
                    TryParsePositiveSize(
                        copperMatch.Groups["FIRST"].Value,
                        100.0,
                        out double firstCopperSize) &&
                    TryParsePositiveSize(
                        copperMatch.Groups["SECOND"].Value,
                        100.0,
                        out double secondCopperSize))
                {
                    sizeText =
                        FormatSizeNumber(firstCopperSize) +
                        " - " +
                        FormatSizeNumber(secondCopperSize);

                    pipeWidth =
                        Math.Max(
                            firstCopperSize,
                            secondCopperSize);

                    return true;
                }
            }

            // 4. Chỉ ống gió mới nhận dạng rộng x cao.
            if (LaOngGio(material))
            {
                Match ductMatch =
                    Regex.Match(
                        normalized,
                        @"(?<!\d)(?<WIDTH>\d{2,4})\s*[xX×]\s*(?<HEIGHT>\d{2,4})(?!\d)");

                if (ductMatch.Success &&
                    TryParsePositiveSize(
                        ductMatch.Groups["WIDTH"].Value,
                        9999.0,
                        out double ductWidth) &&
                    TryParsePositiveSize(
                        ductMatch.Groups["HEIGHT"].Value,
                        9999.0,
                        out double ductHeight))
                {
                    sizeText =
                        FormatSizeNumber(ductWidth) +
                        "x" +
                        FormatSizeNumber(ductHeight);

                    pipeWidth =
                        Math.Max(
                            ductWidth,
                            ductHeight);

                    return true;
                }
            }

            // Không còn nhánh lấy "số đầu tiên" như code cũ.
            // Do đó FFL +7000, BOP: FFL +7000,
            // TOP +3500, EL -1200, số trục và kích thước
            // kiến trúc đều bị bỏ qua.
            return false;
        }

        private bool TryParsePositiveSize(
            string value,
            double maximum,
            out double result)
        {
            result = 0;

            if (!double.TryParse(
                (value ?? "").Replace(',', '.'),
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out double parsed))
            {
                return false;
            }

            if (parsed <= 0 || parsed > maximum)
                return false;

            result = parsed;
            return true;
        }

        private string FormatSizeNumber(double value)
        {
            return value.ToString(
                "0.##",
                CultureInfo.InvariantCulture);
        }

        private bool TryConvertOutsideDiameterToNominal(
            double outsideDiameter,
            out double nominalDiameter)
        {
            nominalDiameter = 0;

            // Bảng quy đổi các đường kính ngoài thường gặp.
            // Sai số 0.2 mm cho phép nhận cả D60 và D60.3.
            foreach (double[] row
                in OutsideDiameterToNominalTable)
            {
                for (int index = 1;
                    index < row.Length;
                    index++)
                {
                    if (Math.Abs(
                        outsideDiameter -
                        row[index]) <= 0.2)
                    {
                        nominalDiameter = row[0];
                        return true;
                    }
                }
            }

            return false;
        }

        private DBText CreateAutomaticSizeLabel(
            Database db,
            Polyline pipe,
            string layerName,
            string labelText,
            double pipeWidth)
        {
            if (db == null ||
                pipe == null ||
                pipe.NumberOfVertices < 2 ||
                string.IsNullOrWhiteSpace(layerName) ||
                string.IsNullOrWhiteSpace(labelText))
            {
                return null;
            }

            try
            {
                double totalLength =
                    pipe.GetDistanceAtParameter(
                        pipe.EndParam);

                // Vẽ tự động: đoạn có kích thước dù ngắn
                // vẫn phải hiện chữ.
                if (totalLength <= 0.001)
                    return null;

                Point3d middlePoint =
                    pipe.GetPointAtDist(
                        totalLength / 2.0);

                double middleParameter =
                    pipe.GetParameterAtPoint(
                        middlePoint);

                Vector3d direction =
                    pipe.GetFirstDerivative(
                        middleParameter);

                if (direction.Length < 0.000001)
                    return null;

                direction = direction.GetNormal();

                double textRotation =
                    direction.AngleOnPlane(new Plane());

                if (textRotation > Math.PI / 2.0 &&
                    textRotation <=
                    3.0 * Math.PI / 2.0)
                {
                    textRotation -= Math.PI;
                    direction = direction.Negate();
                }

                Vector3d normal =
                    direction.RotateBy(
                        Math.PI / 2.0,
                        Vector3d.ZAxis);

                double textHeight =
                    Math.Max(
                        pipeWidth *
                        LabelTextHeightToWidthRatio,
                        MinimumLabelTextHeight) *
                    AutomaticLabelScale;

                double offset =
                    (pipeWidth / 2.0) +
                    (textHeight * 0.2);

                Point3d textPoint =
                    middlePoint +
                    (normal * offset);

                DBText sizeLabel = new DBText();
                sizeLabel.SetDatabaseDefaults(db);
                sizeLabel.TextString = labelText;
                sizeLabel.Height = textHeight;
                sizeLabel.Layer = layerName;

                sizeLabel.Color =
                    Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                        ColorMethod.ByLayer,
                        256);

                sizeLabel.Linetype = "ByLayer";
                sizeLabel.LineWeight = LineWeight.ByLayer;
                sizeLabel.Justify =
                    AttachmentPoint.BottomCenter;

                sizeLabel.AlignmentPoint = textPoint;
                sizeLabel.Rotation = textRotation;

                return sizeLabel;
            }
            catch
            {
                return null;
            }
        }

        private bool IsTemplateReferenceCurve(Entity entity)
        {
            return entity is Line ||
                   entity is Polyline ||
                   entity is Polyline2d ||
                   entity is Polyline3d ||
                   entity is Arc;
        }

        private double GetTemplateCurveWidth(Curve curve)
        {
            if (!(curve is Polyline polyline))
                return 0.0;

            double width = polyline.ConstantWidth;

            for (int index = 0;
                index < polyline.NumberOfVertices;
                index++)
            {
                width = Math.Max(
                    width,
                    polyline.GetStartWidthAt(index));

                width = Math.Max(
                    width,
                    polyline.GetEndWidthAt(index));
            }

            return width;
        }

        private bool IsTemplatePipeCurve(Curve curve)
        {
            if (curve == null ||
                !IsTemplateReferenceCurve(curve))
            {
                return false;
            }

            // Ống do công cụ tạo là Polyline có bề rộng thực tế.
            // Không dựa vào tên Layer để tránh nhầm đường tim gốc.
            return GetTemplateCurveWidth(curve) > 0.001;
        }

        private double GetTemplateCurveLength(Curve curve)
        {
            if (curve == null)
                return 0.0;

            try
            {
                return curve.GetDistanceAtParameter(
                    curve.EndParam);
            }
            catch
            {
                try
                {
                    return curve.StartPoint.DistanceTo(
                        curve.EndPoint);
                }
                catch
                {
                    return 0.0;
                }
            }
        }

        private Point3d GetTemplateCurvePoint(
            Curve curve,
            double fraction)
        {
            fraction =
                Math.Max(
                    0.0,
                    Math.Min(1.0, fraction));

            double length =
                GetTemplateCurveLength(curve);

            if (length <= 0.001)
                return curve.StartPoint;

            try
            {
                return curve.GetPointAtDist(
                    length * fraction);
            }
            catch
            {
                return fraction < 0.5
                    ? curve.StartPoint
                    : curve.EndPoint;
            }
        }

        private double GetTemplatePlanDistance(
            Point3d first,
            Point3d second)
        {
            double deltaX = first.X - second.X;
            double deltaY = first.Y - second.Y;

            return Math.Sqrt(
                (deltaX * deltaX) +
                (deltaY * deltaY));
        }

        private double GetTemplateCoverageScore(
            Curve referenceCurve,
            IEnumerable<Curve> templatePipeCurves)
        {
            double score = 0.0;

            foreach (Curve pipeCurve in
                templatePipeCurves ??
                Enumerable.Empty<Curve>())
            {
                double maximumDistance = 0.0;
                bool valid = true;

                foreach (double fraction in
                    new double[] { 0.15, 0.5, 0.85 })
                {
                    Point3d testPoint =
                        GetTemplateCurvePoint(
                            pipeCurve,
                            fraction);

                    if (!TryGetClosestPointInPlan(
                        referenceCurve,
                        testPoint,
                        out Point3d ignoredPoint,
                        out double planDistance))
                    {
                        valid = false;
                        break;
                    }

                    maximumDistance =
                        Math.Max(
                            maximumDistance,
                            planDistance);
                }

                double allowedDistance =
                    Math.Max(
                        250.0,
                        (GetTemplateCurveWidth(
                            pipeCurve) / 2.0) +
                        150.0);

                if (valid &&
                    maximumDistance <= allowedDistance)
                {
                    score +=
                        GetTemplateCurveLength(
                            pipeCurve);
                }
            }

            return score;
        }

        private Curve SelectTemplateReferenceCurve(
            List<Curve> sourceCurves,
            List<Curve> templatePipeCurves)
        {
            List<Curve> candidates =
                sourceCurves != null &&
                sourceCurves.Count > 0
                    ? sourceCurves
                    : templatePipeCurves;

            if (candidates == null ||
                candidates.Count == 0)
            {
                return null;
            }

            return candidates
                .OrderByDescending(
                    curve =>
                        GetTemplateCoverageScore(
                            curve,
                            templatePipeCurves))
                .ThenByDescending(
                    GetTemplateCurveLength)
                .FirstOrDefault();
        }

        private double GetTemplateAxisProjection(
            Point3d point,
            Point3d axisOrigin,
            Vector3d axisDirection)
        {
            Vector3d delta =
                new Vector3d(
                    point.X - axisOrigin.X,
                    point.Y - axisOrigin.Y,
                    0.0);

            return delta.DotProduct(axisDirection);
        }

        private double GetTemplateAxisOffset(
            Point3d point,
            Point3d axisOrigin,
            Vector3d axisNormal)
        {
            Vector3d delta =
                new Vector3d(
                    point.X - axisOrigin.X,
                    point.Y - axisOrigin.Y,
                    0.0);

            return Math.Abs(
                delta.DotProduct(axisNormal));
        }

        private Curve BuildVirtualTemplateCenterline(
            List<Curve> templatePipeCurves)
        {
            List<Curve> validCurves =
                (templatePipeCurves ??
                 new List<Curve>())
                    .Where(
                        curve =>
                            curve != null &&
                            GetTemplateCurveLength(curve) >
                            1.0)
                    .ToList();

            if (validCurves.Count == 0)
                return null;

            double bestSpan = 0.0;
            double bestCoveredLength = 0.0;
            Point3d bestOrigin = Point3d.Origin;
            Vector3d bestDirection = Vector3d.XAxis;
            double bestStart = 0.0;
            double bestEnd = 0.0;

            foreach (Curve seedCurve in validCurves)
            {
                if (!TryGetTemplatePlanDirection(
                    seedCurve,
                    out Vector3d axisDirection))
                {
                    continue;
                }

                Point3d axisOrigin =
                    seedCurve.StartPoint;

                Vector3d axisNormal =
                    new Vector3d(
                        -axisDirection.Y,
                        axisDirection.X,
                        0.0);

                List<double[]> intervals =
                    new List<double[]>();

                foreach (Curve candidateCurve in validCurves)
                {
                    if (!TryGetTemplatePlanDirection(
                        candidateCurve,
                        out Vector3d candidateDirection))
                    {
                        continue;
                    }

                    double parallelScore =
                        Math.Abs(
                            axisDirection.DotProduct(
                                candidateDirection));

                    if (parallelScore < 0.985)
                        continue;

                    double maximumAxisOffset =
                        Math.Max(
                            200.0,
                            (Math.Max(
                                GetTemplateCurveWidth(
                                    seedCurve),
                                GetTemplateCurveWidth(
                                    candidateCurve)) /
                             2.0) +
                            100.0);

                    Point3d candidateStart =
                        candidateCurve.StartPoint;

                    Point3d candidateMiddle =
                        GetTemplateCurvePoint(
                            candidateCurve,
                            0.5);

                    Point3d candidateEnd =
                        candidateCurve.EndPoint;

                    if (GetTemplateAxisOffset(
                            candidateStart,
                            axisOrigin,
                            axisNormal) >
                            maximumAxisOffset ||
                        GetTemplateAxisOffset(
                            candidateMiddle,
                            axisOrigin,
                            axisNormal) >
                            maximumAxisOffset ||
                        GetTemplateAxisOffset(
                            candidateEnd,
                            axisOrigin,
                            axisNormal) >
                            maximumAxisOffset)
                    {
                        continue;
                    }

                    double firstProjection =
                        GetTemplateAxisProjection(
                            candidateStart,
                            axisOrigin,
                            axisDirection);

                    double secondProjection =
                        GetTemplateAxisProjection(
                            candidateEnd,
                            axisOrigin,
                            axisDirection);

                    intervals.Add(
                        new double[]
                        {
                            Math.Min(
                                firstProjection,
                                secondProjection),
                            Math.Max(
                                firstProjection,
                                secondProjection)
                        });
                }

                if (intervals.Count == 0)
                    continue;

                List<double[]> orderedIntervals =
                    intervals
                        .OrderBy(item => item[0])
                        .ThenBy(item => item[1])
                        .ToList();

                double clusterStart =
                    orderedIntervals[0][0];

                double clusterEnd =
                    orderedIntervals[0][1];

                double clusterCoveredLength =
                    orderedIntervals[0][1] -
                    orderedIntervals[0][0];

                Action evaluateCluster = () =>
                {
                    double span =
                        clusterEnd - clusterStart;

                    if (span > bestSpan + 1.0 ||
                        (Math.Abs(span - bestSpan) <=
                            1.0 &&
                         clusterCoveredLength >
                            bestCoveredLength))
                    {
                        bestSpan = span;
                        bestCoveredLength =
                            clusterCoveredLength;
                        bestOrigin = axisOrigin;
                        bestDirection = axisDirection;
                        bestStart = clusterStart;
                        bestEnd = clusterEnd;
                    }
                };

                for (int intervalIndex = 1;
                    intervalIndex <
                    orderedIntervals.Count;
                    intervalIndex++)
                {
                    double[] interval =
                        orderedIntervals[intervalIndex];

                    if (interval[0] <=
                        clusterEnd +
                        TemplateConnectionTolerance)
                    {
                        double uncoveredStart =
                            Math.Max(
                                clusterEnd,
                                interval[0]);

                        if (interval[1] > uncoveredStart)
                        {
                            clusterCoveredLength +=
                                interval[1] -
                                uncoveredStart;
                        }

                        clusterEnd =
                            Math.Max(
                                clusterEnd,
                                interval[1]);
                    }
                    else
                    {
                        evaluateCluster();

                        clusterStart = interval[0];
                        clusterEnd = interval[1];

                        clusterCoveredLength =
                            interval[1] -
                            interval[0];
                    }
                }

                evaluateCluster();
            }

            if (bestSpan <= 1.0)
                return null;

            Point3d virtualStart =
                new Point3d(
                    bestOrigin.X +
                    (bestDirection.X * bestStart),
                    bestOrigin.Y +
                    (bestDirection.Y * bestStart),
                    bestOrigin.Z);

            Point3d virtualEnd =
                new Point3d(
                    bestOrigin.X +
                    (bestDirection.X * bestEnd),
                    bestOrigin.Y +
                    (bestDirection.Y * bestEnd),
                    bestOrigin.Z);

            return new Line(
                virtualStart,
                virtualEnd);
        }

        private Curve SelectUsableTemplateReferenceCurve(
            Curve sourceReferenceCurve,
            Curve virtualReferenceCurve,
            List<Curve> templatePipeCurves)
        {
            if (sourceReferenceCurve == null)
                return virtualReferenceCurve;

            if (virtualReferenceCurve == null)
                return sourceReferenceCurve;

            double sourceCoverage =
                GetTemplateCoverageScore(
                    sourceReferenceCurve,
                    templatePipeCurves);

            double virtualCoverage =
                GetTemplateCoverageScore(
                    virtualReferenceCurve,
                    templatePipeCurves);

            double sourceLength =
                GetTemplateCurveLength(
                    sourceReferenceCurve);

            double virtualLength =
                GetTemplateCurveLength(
                    virtualReferenceCurve);

            double allowedLengthDifference =
                Math.Max(
                    TemplateAbsoluteLengthTolerance,
                    virtualLength *
                    TemplateLengthToleranceRatio);

            bool sourceCurveStillRepresentsWholeBranch =
                sourceCoverage >=
                    virtualCoverage * 0.90 &&
                Math.Abs(
                    sourceLength -
                    virtualLength) <=
                    allowedLengthDifference;

            return sourceCurveStillRepresentsWholeBranch
                ? sourceReferenceCurve
                : virtualReferenceCurve;
        }

        private List<Point3d> CollectTemplateMarkerCenters(
            Transaction tr,
            IEnumerable<Entity> entities)
        {
            List<Point3d> centers =
                new List<Point3d>();

            foreach (Entity entity in
                entities ??
                Enumerable.Empty<Entity>())
            {
                if (entity is Circle circle)
                {
                    AddUniquePoint(
                        centers,
                        circle.Center,
                        5.0);
                }
                else if (entity is BlockReference blockReference)
                {
                    List<Point3d> blockCenters =
                        GetCircularCentersFromBlock(
                            tr,
                            blockReference);

                    if (blockCenters.Count == 0)
                    {
                        AddUniquePoint(
                            centers,
                            blockReference.Position,
                            5.0);
                    }
                    else
                    {
                        foreach (Point3d center
                            in blockCenters)
                        {
                            AddUniquePoint(
                                centers,
                                center,
                                5.0);
                        }
                    }
                }
            }

            return centers;
        }

        private List<Point3d> GetTemplateMarkersNearCurve(
            Curve curve,
            IEnumerable<Point3d> markerCenters)
        {
            List<Point3d> result =
                new List<Point3d>();

            foreach (Point3d center in
                markerCenters ??
                Enumerable.Empty<Point3d>())
            {
                if (TryGetClosestPointInPlan(
                    curve,
                    center,
                    out Point3d ignoredPoint,
                    out double planDistance) &&
                    planDistance <=
                    TemplateMarkerSearchDistance)
                {
                    AddUniquePoint(
                        result,
                        center,
                        5.0);
                }
            }

            return result;
        }

        private bool TryGetTemplatePlanDirection(
            Curve curve,
            out Vector3d direction)
        {
            direction = Vector3d.XAxis;

            if (curve == null)
                return false;

            try
            {
                Point3d startPoint =
                    curve.StartPoint;

                Point3d endPoint =
                    curve.EndPoint;

                Vector3d chord =
                    new Vector3d(
                        endPoint.X - startPoint.X,
                        endPoint.Y - startPoint.Y,
                        0.0);

                if (chord.Length <= 0.001)
                    return false;

                direction = chord.GetNormal();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private int FindTemplateBranchRootIndex(
            Curve branchCurve,
            IEnumerable<Curve> contextCurves)
        {
            if (!TryGetTemplatePlanDirection(
                branchCurve,
                out Vector3d branchDirection))
            {
                return -1;
            }

            Point3d[] endpoints =
            {
                branchCurve.StartPoint,
                branchCurve.EndPoint
            };

            bool[] hasNonCollinearConnection =
            {
                false,
                false
            };

            for (int endpointIndex = 0;
                endpointIndex < endpoints.Length;
                endpointIndex++)
            {
                foreach (Curve otherCurve in
                    contextCurves ??
                    Enumerable.Empty<Curve>())
                {
                    if (otherCurve == null ||
                        ReferenceEquals(
                            branchCurve,
                            otherCurve))
                    {
                        continue;
                    }

                    if (branchCurve.ObjectId != ObjectId.Null &&
                        otherCurve.ObjectId != ObjectId.Null &&
                        branchCurve.ObjectId ==
                        otherCurve.ObjectId)
                    {
                        continue;
                    }

                    if (!TryGetClosestPointInPlan(
                        otherCurve,
                        endpoints[endpointIndex],
                        out Point3d ignoredPoint,
                        out double planDistance) ||
                        planDistance >
                        TemplateConnectionTolerance)
                    {
                        continue;
                    }

                    if (!TryGetTemplatePlanDirection(
                        otherCurve,
                        out Vector3d otherDirection))
                    {
                        continue;
                    }

                    double directionDot =
                        Math.Abs(
                            branchDirection.DotProduct(
                                otherDirection));

                    if (directionDot < 0.94)
                    {
                        hasNonCollinearConnection[
                            endpointIndex] = true;

                        break;
                    }
                }
            }

            if (hasNonCollinearConnection[0] &&
                !hasNonCollinearConnection[1])
            {
                return 0;
            }

            if (!hasNonCollinearConnection[0] &&
                hasNonCollinearConnection[1])
            {
                return 1;
            }

            return -1;
        }

        private bool TryBuildTemplateAlignment(
            Curve sourceCurve,
            Curve targetCurve,
            bool reverseTarget,
            out Matrix3d transform)
        {
            transform = Matrix3d.Identity;

            try
            {
                Point3d sourceStart =
                    sourceCurve.StartPoint;

                Point3d sourceEnd =
                    sourceCurve.EndPoint;

                Point3d targetStart =
                    reverseTarget
                        ? targetCurve.EndPoint
                        : targetCurve.StartPoint;

                Point3d targetEnd =
                    reverseTarget
                        ? targetCurve.StartPoint
                        : targetCurve.EndPoint;

                Vector3d sourceXAxis =
                    new Vector3d(
                        sourceEnd.X - sourceStart.X,
                        sourceEnd.Y - sourceStart.Y,
                        0.0);

                Vector3d targetXAxis =
                    new Vector3d(
                        targetEnd.X - targetStart.X,
                        targetEnd.Y - targetStart.Y,
                        0.0);

                if (sourceXAxis.Length <= 0.001 ||
                    targetXAxis.Length <= 0.001)
                {
                    return false;
                }

                sourceXAxis = sourceXAxis.GetNormal();
                targetXAxis = targetXAxis.GetNormal();

                Vector3d sourceYAxis =
                    Vector3d.ZAxis
                        .CrossProduct(sourceXAxis)
                        .GetNormal();

                Vector3d targetYAxis =
                    Vector3d.ZAxis
                        .CrossProduct(targetXAxis)
                        .GetNormal();

                transform =
                    Matrix3d.AlignCoordinateSystem(
                        sourceStart,
                        sourceXAxis,
                        sourceYAxis,
                        Vector3d.ZAxis,
                        targetStart,
                        targetXAxis,
                        targetYAxis,
                        Vector3d.ZAxis);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryMatchTemplateBranch(
            Curve sourceCurve,
            Curve targetCurve,
            int sourceRootIndex,
            int targetRootIndex,
            List<Point3d> sourceMarkers,
            List<Point3d> targetMarkers,
            out Matrix3d bestTransform,
            out double bestScore)
        {
            bestTransform = Matrix3d.Identity;
            bestScore = double.MaxValue;

            double sourceLength =
                GetTemplateCurveLength(
                    sourceCurve);

            double targetLength =
                GetTemplateCurveLength(
                    targetCurve);

            if (sourceLength <= 0.001 ||
                targetLength <= 0.001)
            {
                return false;
            }

            double lengthTolerance =
                Math.Max(
                    TemplateAbsoluteLengthTolerance,
                    sourceLength *
                    TemplateLengthToleranceRatio);

            if (Math.Abs(
                sourceLength -
                targetLength) > lengthTolerance)
            {
                return false;
            }

            List<bool> reverseOptions =
                new List<bool>();

            if (sourceRootIndex >= 0 &&
                targetRootIndex >= 0)
            {
                reverseOptions.Add(
                    sourceRootIndex !=
                    targetRootIndex);
            }
            else
            {
                reverseOptions.Add(false);
                reverseOptions.Add(true);
            }

            foreach (bool reverseTarget in
                reverseOptions.Distinct())
            {
                if (!TryBuildTemplateAlignment(
                    sourceCurve,
                    targetCurve,
                    reverseTarget,
                    out Matrix3d candidateTransform))
                {
                    continue;
                }

                double shapeErrorTotal = 0.0;
                int shapePointCount = 0;

                for (int index = 0;
                    index <= 8;
                    index++)
                {
                    double fraction =
                        index / 8.0;

                    Point3d sourcePoint =
                        GetTemplateCurvePoint(
                            sourceCurve,
                            fraction);

                    Point3d targetPoint =
                        GetTemplateCurvePoint(
                            targetCurve,
                            reverseTarget
                                ? 1.0 - fraction
                                : fraction);

                    Point3d transformedPoint =
                        sourcePoint.TransformBy(
                            candidateTransform);

                    shapeErrorTotal +=
                        GetTemplatePlanDistance(
                            transformedPoint,
                            targetPoint);

                    shapePointCount++;
                }

                double shapeError =
                    shapePointCount > 0
                        ? shapeErrorTotal /
                          shapePointCount
                        : double.MaxValue;

                double shapeTolerance =
                    Math.Max(
                        80.0,
                        sourceLength *
                        TemplateShapeToleranceRatio);

                if (shapeError > shapeTolerance)
                    continue;

                int matchedMarkerCount = 0;
                double markerErrorTotal = 0.0;

                HashSet<int> usedTargetMarkerIndexes =
                    new HashSet<int>();

                foreach (Point3d sourceMarker in
                    sourceMarkers ??
                    new List<Point3d>())
                {
                    Point3d expectedMarker =
                        sourceMarker.TransformBy(
                            candidateTransform);

                    int bestMarkerIndex = -1;

                    double nearestMarkerDistance =
                        double.MaxValue;

                    for (int markerIndex = 0;
                        markerIndex <
                        (targetMarkers?.Count ?? 0);
                        markerIndex++)
                    {
                        if (usedTargetMarkerIndexes.Contains(
                            markerIndex))
                        {
                            continue;
                        }

                        double markerDistance =
                            GetTemplatePlanDistance(
                                expectedMarker,
                                targetMarkers[markerIndex]);

                        if (markerDistance <
                            nearestMarkerDistance)
                        {
                            nearestMarkerDistance =
                                markerDistance;

                            bestMarkerIndex =
                                markerIndex;
                        }
                    }

                    if (bestMarkerIndex >= 0 &&
                        nearestMarkerDistance <=
                        TemplateMarkerMatchTolerance)
                    {
                        usedTargetMarkerIndexes.Add(
                            bestMarkerIndex);

                        matchedMarkerCount++;
                        markerErrorTotal +=
                            nearestMarkerDistance;
                    }
                }

                int requiredMarkerCount =
                    sourceMarkers != null &&
                    sourceMarkers.Count > 0
                        ? Math.Max(
                            1,
                            (int)Math.Ceiling(
                                sourceMarkers.Count *
                                0.75))
                        : 0;

                if (matchedMarkerCount <
                    requiredMarkerCount)
                {
                    continue;
                }

                double markerError =
                    matchedMarkerCount > 0
                        ? markerErrorTotal /
                          matchedMarkerCount
                        : 0.0;

                double score =
                    shapeError +
                    (markerError * 0.35);

                if (sourceRootIndex < 0 &&
                    targetRootIndex < 0 &&
                    (sourceMarkers == null ||
                     sourceMarkers.Count == 0) &&
                    TryGetTemplatePlanDirection(
                        sourceCurve,
                        out Vector3d sourceDirection) &&
                    TryGetTemplatePlanDirection(
                        targetCurve,
                        out Vector3d targetDirection))
                {
                    if (reverseTarget)
                    {
                        targetDirection =
                            targetDirection.Negate();
                    }

                    score +=
                        sourceDirection.GetAngleTo(
                            targetDirection) * 10.0;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    bestTransform =
                        candidateTransform;
                }
            }

            return bestScore < double.MaxValue;
        }

        private bool HasTemplateOutputAtPlacement(
            Curve probeCurve,
            Matrix3d transform,
            IEnumerable<Curve> existingPipeCurves)
        {
            if (probeCurve == null)
                return false;

            Point3d expectedCenter =
                GetTemplateCurvePoint(
                    probeCurve,
                    0.5)
                .TransformBy(transform);

            double probeLength =
                GetTemplateCurveLength(
                    probeCurve);

            double lengthTolerance =
                Math.Max(
                    TemplateAbsoluteLengthTolerance,
                    probeLength *
                    TemplateLengthToleranceRatio);

            foreach (Curve existingCurve in
                existingPipeCurves ??
                Enumerable.Empty<Curve>())
            {
                if (!string.Equals(
                    existingCurve.Layer,
                    probeCurve.Layer,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (Math.Abs(
                    GetTemplateCurveLength(
                        existingCurve) -
                    probeLength) >
                    lengthTolerance)
                {
                    continue;
                }

                Point3d existingCenter =
                    GetTemplateCurvePoint(
                        existingCurve,
                        0.5);

                if (GetTemplatePlanDistance(
                    expectedCenter,
                    existingCenter) <=
                    TemplateDuplicateTolerance)
                {
                    return true;
                }
            }

            return false;
        }

        private double GetReadableTemplateTextRotation(
            double rotation)
        {
            double fullTurn = Math.PI * 2.0;

            double readableRotation =
                rotation % fullTurn;

            if (readableRotation < 0.0)
                readableRotation += fullTurn;

            if (readableRotation > Math.PI / 2.0 &&
                readableRotation <=
                3.0 * Math.PI / 2.0)
            {
                readableRotation -= Math.PI;
            }

            return readableRotation;
        }

        private void KeepTemplateCopiedTextReadable(
            Entity entity)
        {
            if (entity is DBText dbText)
            {
                dbText.Rotation =
                    GetReadableTemplateTextRotation(
                        dbText.Rotation);
            }
            else if (entity is MText mText)
            {
                mText.Rotation =
                    GetReadableTemplateTextRotation(
                        mText.Rotation);
            }
        }

        private void BtnAutoDrawByTemplate_Click(
            object sender,
            RoutedEventArgs e)
        {
            Document doc =
                Autodesk.AutoCAD.ApplicationServices.Core.Application
                    .DocumentManager
                    .MdiActiveDocument;

            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();

            TypedValue[] filterValues =
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
                        "TEXT"),
                    new TypedValue(
                        (int)DxfCode.Start,
                        "MTEXT"),
                    new TypedValue(
                        (int)DxfCode.Start,
                        "INSERT"),
                    new TypedValue(
                        (int)DxfCode.Start,
                        "CIRCLE"),
                    new TypedValue(
                        (int)DxfCode.Operator,
                        "OR>")
                };

            SelectionFilter selectionFilter =
                new SelectionFilter(filterValues);

            PromptSelectionOptions sampleOptions =
                new PromptSelectionOptions();

            sampleOptions.MessageForAdding =
                "\nBƯỚC 1 - Quét chọn NHÁNH MẪU: " +
                "ống/chữ đã vẽ, các đầu phun và " +
                "đường tim gốc nếu vẫn còn: ";

            PromptSelectionResult sampleResult =
                ed.GetSelection(
                    sampleOptions,
                    selectionFilter);

            if (sampleResult.Status != PromptStatus.OK ||
                sampleResult.Value.Count == 0)
            {
                return;
            }

            PromptSelectionOptions targetOptions =
                new PromptSelectionOptions();

            targetOptions.MessageForAdding =
                "\nBƯỚC 2 - Quét chọn KHU VỰC cần tìm " +
                "và vẽ các nhánh tương tự: ";

            PromptSelectionResult targetResult =
                ed.GetSelection(
                    targetOptions,
                    selectionFilter);

            if (targetResult.Status != PromptStatus.OK ||
                targetResult.Value.Count == 0)
            {
                return;
            }

            Curve virtualReferenceCurve = null;

            try
            {
                using (doc.LockDocument())
                using (Transaction tr =
                    db.TransactionManager.StartTransaction())
                {
                    List<Entity> sampleEntities =
                        sampleResult.Value
                            .GetObjectIds()
                            .Select(
                                id => tr.GetObject(
                                    id,
                                    OpenMode.ForRead,
                                    false) as Entity)
                            .Where(
                                entity => entity != null)
                            .ToList();

                    List<Entity> targetEntities =
                        targetResult.Value
                            .GetObjectIds()
                            .Select(
                                id => tr.GetObject(
                                    id,
                                    OpenMode.ForRead,
                                    false) as Entity)
                            .Where(
                                entity => entity != null)
                            .ToList();

                    List<Curve> samplePipeCurves =
                        sampleEntities
                            .OfType<Curve>()
                            .Where(IsTemplatePipeCurve)
                            .ToList();

                    if (samplePipeCurves.Count == 0)
                    {
                        MessageBox.Show(
                            "Không tìm thấy ống mẫu đã vẽ.\n" +
                            "Hãy chọn cả Polyline ống có bề rộng " +
                            "và Layer kích thước.",
                            "Vẽ ống tự động theo mẫu");

                        return;
                    }

                    HashSet<string> templatePipeLayers =
                        new HashSet<string>(
                            samplePipeCurves.Select(
                                curve => curve.Layer),
                            StringComparer.OrdinalIgnoreCase);

                    List<Entity> templateEntitiesToCopy =
                        sampleEntities
                            .Where(
                                entity =>
                                    (entity is Curve curve &&
                                     samplePipeCurves.Contains(
                                         curve)) ||
                                    ((entity is DBText ||
                                      entity is MText) &&
                                     templatePipeLayers.Contains(
                                         entity.Layer)))
                            .ToList();

                    List<Curve> sampleSourceCurves =
                        sampleEntities
                            .Where(IsTemplateReferenceCurve)
                            .Cast<Curve>()
                            .Where(
                                curve =>
                                    !samplePipeCurves.Contains(
                                        curve))
                            .ToList();

                    Curve sourceReferenceCurve =
                        sampleSourceCurves.Count > 0
                            ? SelectTemplateReferenceCurve(
                                sampleSourceCurves,
                                samplePipeCurves)
                            : null;

                    // Dựng đường tim ảo từ những Polyline ống đã vẽ.
                    // Vì vậy dù chức năng tự động đã xóa LINE gốc,
                    // chức năng vẽ theo mẫu vẫn nhận diện được nhánh mẫu.
                    virtualReferenceCurve =
                        BuildVirtualTemplateCenterline(
                            samplePipeCurves);

                    Curve referenceCurve =
                        SelectUsableTemplateReferenceCurve(
                            sourceReferenceCurve,
                            virtualReferenceCurve,
                            samplePipeCurves);

                    if (referenceCurve == null ||
                        templateEntitiesToCopy.Count == 0)
                    {
                        MessageBox.Show(
                            "Không dựng được đường tim ảo từ " +
                            "các đoạn ống mẫu.\n" +
                            "Hãy quét đủ toàn bộ các đoạn DN " +
                            "trên cùng một nhánh.",
                            "Vẽ ống tự động theo mẫu");

                        return;
                    }

                    List<Curve> sampleContextCurves =
                        sampleEntities
                            .Where(IsTemplateReferenceCurve)
                            .Cast<Curve>()
                            .ToList();

                    List<Point3d> allSampleMarkers =
                        CollectTemplateMarkerCenters(
                            tr,
                            sampleEntities);

                    List<Point3d> sampleMarkers =
                        GetTemplateMarkersNearCurve(
                            referenceCurve,
                            allSampleMarkers);

                    int sourceRootIndex =
                        FindTemplateBranchRootIndex(
                            referenceCurve,
                            sampleContextCurves);

                    List<Curve> targetSourceCurves =
                        targetEntities
                            .Where(IsTemplateReferenceCurve)
                            .Cast<Curve>()
                            .Where(
                                curve =>
                                    !IsTemplatePipeCurve(curve))
                            .ToList();

                    List<Curve> existingTargetPipeCurves =
                        targetEntities
                            .OfType<Curve>()
                            .Where(IsTemplatePipeCurve)
                            .ToList();

                    List<Point3d> allTargetMarkers =
                        CollectTemplateMarkerCenters(
                            tr,
                            targetEntities);

                    if (targetSourceCurves.Count == 0)
                    {
                        MessageBox.Show(
                            "Không tìm thấy đường tim nhánh " +
                            "trong khu vực đã quét.",
                            "Vẽ ống tự động theo mẫu");

                        return;
                    }

                    Curve probeCurve =
                        samplePipeCurves
                            .OrderByDescending(
                                GetTemplateCurveLength)
                            .First();

                    List<TemplateBranchMatchData> matches =
                        new List<TemplateBranchMatchData>();

                    List<Point3d> acceptedTargetCenters =
                        new List<Point3d>();

                    foreach (Curve targetCurve
                        in targetSourceCurves)
                    {
                        if (referenceCurve.ObjectId !=
                                ObjectId.Null &&
                            targetCurve.ObjectId !=
                                ObjectId.Null &&
                            referenceCurve.ObjectId ==
                                targetCurve.ObjectId)
                        {
                            continue;
                        }

                        List<Point3d> targetMarkers =
                            GetTemplateMarkersNearCurve(
                                targetCurve,
                                allTargetMarkers);

                        int targetRootIndex =
                            FindTemplateBranchRootIndex(
                                targetCurve,
                                targetSourceCurves);

                        if (!TryMatchTemplateBranch(
                            referenceCurve,
                            targetCurve,
                            sourceRootIndex,
                            targetRootIndex,
                            sampleMarkers,
                            targetMarkers,
                            out Matrix3d transform,
                            out double score))
                        {
                            continue;
                        }

                        Point3d targetCenter =
                            GetTemplateCurvePoint(
                                referenceCurve,
                                0.5)
                            .TransformBy(transform);

                        if (acceptedTargetCenters.Any(
                            center =>
                                GetTemplatePlanDistance(
                                    center,
                                    targetCenter) <=
                                TemplateDuplicateTolerance))
                        {
                            continue;
                        }

                        if (HasTemplateOutputAtPlacement(
                            probeCurve,
                            transform,
                            existingTargetPipeCurves))
                        {
                            continue;
                        }

                        acceptedTargetCenters.Add(
                            targetCenter);

                        matches.Add(
                            new TemplateBranchMatchData
                            {
                                TargetCurve = targetCurve,
                                Transform = transform,
                                Score = score,
                                TargetCenter = targetCenter
                            });
                    }

                    if (matches.Count == 0)
                    {
                        MessageBox.Show(
                            "Không tìm thấy nhánh nào đủ giống " +
                            "với nhánh mẫu.\n" +
                            "Hãy quét đủ các đoạn ống khác DN " +
                            "và các đầu phun của nhánh mẫu.",
                            "Vẽ ống tự động theo mẫu");

                        return;
                    }

                    BlockTableRecord targetSpace =
                        tr.GetObject(
                            db.CurrentSpaceId,
                            OpenMode.ForWrite)
                            as BlockTableRecord;

                    int copiedEntityCount = 0;
                    int copiedBranchCount = 0;

                    foreach (
                        TemplateBranchMatchData match
                        in matches.OrderBy(
                            item => item.Score))
                    {
                        int copiedOnCurrentBranch = 0;

                        foreach (
                            Entity templateEntity
                            in templateEntitiesToCopy)
                        {
                            Entity copiedEntity =
                                templateEntity.Clone()
                                    as Entity;

                            if (copiedEntity == null)
                                continue;

                            copiedEntity.TransformBy(
                                match.Transform);

                            KeepTemplateCopiedTextReadable(
                                copiedEntity);

                            targetSpace.AppendEntity(
                                copiedEntity);

                            tr.AddNewlyCreatedDBObject(
                                copiedEntity,
                                true);

                            if (copiedEntity
                                is DBText copiedText)
                            {
                                copiedText.AdjustAlignment(db);
                            }

                            copiedEntityCount++;
                            copiedOnCurrentBranch++;
                        }

                        if (copiedOnCurrentBranch > 0)
                            copiedBranchCount++;
                    }

                    tr.Commit();
                    ed.Regen();

                    ed.WriteMessage(
                        $"\n[{TemplateAutoDrawBuild}] Hoàn tất: " +
                        $"đã nhận diện {copiedBranchCount} " +
                        $"nhánh tương tự và sao chép " +
                        $"{copiedEntityCount} đối tượng ống/chữ.");
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(
                    "Không thể vẽ tự động theo mẫu:\n" +
                    ex.Message,
                    "Lỗi");
            }
            finally
            {
                virtualReferenceCurve?.Dispose();
            }
        }

        private void BtnVeOgTuDong_Click(
            object sender,
            RoutedEventArgs e)
        {
            PipeUiContext ctx = _ctxACMV ?? GetContext(sender);
            if (ctx == null)
                ctx = _ctxACMV;

            var doc =
                Autodesk.AutoCAD.ApplicationServices.Core.Application
                    .DocumentManager
                    .MdiActiveDocument;

            if (doc == null)
                return;

            var ed = doc.Editor;
            var db = doc.Database;

            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();

            PromptSelectionOptions pso =
                new PromptSelectionOptions();
            pso.MessageForAdding =
                "\n[VẼ OG TỰ ĐỘNG] Quét chọn vùng có đường ống gió + chữ size (WxH) + tên OG: ";

            TypedValue[] tvs =
                new TypedValue[]
                {
                    new TypedValue((int)DxfCode.Operator, "<OR"),
                    new TypedValue((int)DxfCode.Start, "LINE"),
                    new TypedValue((int)DxfCode.Start, "ARC"),
                    new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                    new TypedValue((int)DxfCode.Start, "POLYLINE"),
                    new TypedValue((int)DxfCode.Start, "TEXT"),
                    new TypedValue((int)DxfCode.Start, "MTEXT"),
                    new TypedValue((int)DxfCode.Operator, "OR>")
                };

            PromptSelectionResult psr =
                ed.GetSelection(pso, new SelectionFilter(tvs));

            if (psr.Status != PromptStatus.OK ||
                psr.Value.Count == 0)
                return;

            var sizeTexts =
                new List<OngGioSizeTextInfo>();
            var protectionTexts =
                new List<OngGioProtectionTextInfo>();
            var ogTypeTexts =
                new List<OngGioTypeTextInfo>();
            var segments =
                new List<OngGioSegmentInfo>();
            var arcs =
                new List<OngGioArcInfo>();
            var candidates =
                new List<OngGioCenterlineCandidate>();

            string layerPrefix = GetOngGioAutoLayerPrefix(ctx);

            using (doc.LockDocument())
            using (Transaction tr =
                db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject so in psr.Value)
                {
                    if (so == null) continue;
                    Entity ent =
                        tr.GetObject(so.ObjectId, OpenMode.ForRead)
                            as Entity;
                    if (ent == null) continue;

                    if (ent is DBText dbText)
                    {
                        string str = (dbText.TextString ?? "")
                            .Replace("\r", " ").Replace("\n", " ").Trim();
                        Point3d pt = dbText.Position;
                        if (dbText.Justify != AttachmentPoint.BaseLeft &&
                            !(dbText.AlignmentPoint.X == 0 &&
                              dbText.AlignmentPoint.Y == 0))
                            pt = dbText.AlignmentPoint;

                        if (TryParseOngGioSize(
                                str,
                                out string sz,
                                out double sideA,
                                out double sideB,
                                out double w,
                                out string ei))
                        {
                            sizeTexts.Add(
                                new OngGioSizeTextInfo
                                {
                                    Pos = pt,
                                    Size = sz,
                                    SideA = sideA,
                                    SideB = sideB,
                                    Width = w,
                                    Ei = ei
                                });
                        }

                        if (TryParseOngGioProtection(str, out string protect))
                        {
                            protectionTexts.Add(
                                new OngGioProtectionTextInfo
                                {
                                    Pos = pt,
                                    Text = protect
                                });
                        }

                        if (TryParseOngGioType(str, out string ogType))
                        {
                            ogTypeTexts.Add(
                                new OngGioTypeTextInfo
                                {
                                    Pos = pt,
                                    Type = ogType
                                });
                        }
                    }
                    else if (ent is MText mText)
                    {
                        string str =
                            LayPlainTextTuMText(mText.Contents);
                        Point3d pt = mText.Location;

                        if (TryParseOngGioSize(
                                str,
                                out string sz,
                                out double sideA,
                                out double sideB,
                                out double w,
                                out string ei))
                        {
                            sizeTexts.Add(
                                new OngGioSizeTextInfo
                                {
                                    Pos = pt,
                                    Size = sz,
                                    SideA = sideA,
                                    SideB = sideB,
                                    Width = w,
                                    Ei = ei
                                });
                        }

                        if (TryParseOngGioProtection(str, out string protect))
                        {
                            protectionTexts.Add(
                                new OngGioProtectionTextInfo
                                {
                                    Pos = pt,
                                    Text = protect
                                });
                        }

                        if (TryParseOngGioType(str, out string ogType))
                        {
                            ogTypeTexts.Add(
                                new OngGioTypeTextInfo
                                {
                                    Pos = pt,
                                    Type = ogType
                                });
                        }
                    }
                    else if (ent is Entity src)
                    {
                        bool hasClosedCandidate =
                            TryCreateOngGioCandidateFromRectangle(
                                src,
                                out OngGioCenterlineCandidate closedCandidate);

                        if (hasClosedCandidate)
                        {
                            candidates.Add(closedCandidate);
                        }

                        if (!hasClosedCandidate)
                            AddOngGioSegmentsFromEntity(src, segments);

                        AddOngGioArcFromEntity(src, arcs);
                    }
                }

                tr.Commit();
            }

            if (sizeTexts.Count == 0)
            {
                MessageBox.Show(
                    "Không tìm thấy chữ kích thước ống gió (dạng 500x200, 800x300...).",
                    "Cảnh báo");
                return;
            }

            candidates.AddRange(
                CreateOngGioCandidatesFromParallelSegments(segments));
            candidates.AddRange(
                CreateReducerCandidatesFromEndSegments(segments));
            List<OngGioCenterlineCandidate> arcCandidates =
                CreateOngGioCandidatesFromConcentricArcs(arcs);
            arcCandidates.AddRange(
                CreateTerminalOngGioCandidatesFromSingleArcs(
                    arcs,
                    arcCandidates,
                    candidates,
                    segments));
            candidates.AddRange(arcCandidates);
            candidates =
                RemoveShortStraightCandidatesNearArcs(
                    candidates,
                    arcCandidates);

            candidates =
                RemoveOrTrimCandidatesAtReducers(candidates);

            candidates =
                RemoveDuplicateOngGioCandidates(candidates)
                    .Where(x => x != null && x.Length > 100.0)
                    .ToList();

            ExtendBranchCandidatesToNearbyMainDucts(candidates);

            candidates =
                candidates
                    .Where(x =>
                        x != null &&
                        !IsOngGioCandidateBlockedByInternalMark(
                            x,
                            segments))
                    .ToList();

            if (candidates.Count == 0)
            {
                MessageBox.Show(
                    "Không nhận diện được khung ống gió hình chữ nhật trong vùng chọn.\n" +
                    "Hãy quét gồm 4 cạnh LINE/POLYLINE hoặc closed polyline của ống gió, kèm chữ size dạng 500x200.",
                    "Cảnh báo");
                return;
            }

            int converted = 0;
            string defaultOg =
                ogTypeTexts.Count > 0
                    ? ogTypeTexts[0].Type
                    : "OG THẢI";

            ResolveOngGioCandidateMetadata(
                candidates,
                sizeTexts,
                protectionTexts,
                ogTypeTexts,
                defaultOg);

            PropagateConnectedStraightMetadataToArcs(candidates);

            candidates =
                MergeConnectedSameLayerCandidates(
                    candidates);

            using (doc.LockDocument())
            using (Transaction tr =
                db.TransactionManager.StartTransaction())
            {
                BlockTableRecord btr =
                    (BlockTableRecord)tr.GetObject(
                        db.CurrentSpaceId, OpenMode.ForWrite);

                foreach (OngGioCenterlineCandidate candidate in candidates)
                {
                    if (candidate == null ||
                        candidate.Length <= 100.0)
                        continue;

                    if (IsOngGioCandidateBlockedByInternalMark(
                            candidate,
                            segments))
                        continue;

                    OngGioSizeTextInfo sizeInfo =
                        candidate.ResolvedSizeInfo ??
                        FindNearestOngGioSizeText(
                            candidate,
                            sizeTexts);

                    if (sizeInfo == null)
                        continue;

                    string size = sizeInfo.Size;
                    double width = sizeInfo.Width;
                    string eiPart =
                        candidate.ResolvedProtection;

                    if (string.IsNullOrWhiteSpace(eiPart))
                        eiPart = sizeInfo.Ei;

                    if (string.IsNullOrWhiteSpace(eiPart))
                    {
                        OngGioProtectionTextInfo protectInfo =
                            FindNearestOngGioProtectionText(
                                candidate,
                                protectionTexts);
                        if (protectInfo != null)
                            eiPart = protectInfo.Text;
                    }

                    if (width <= 0)
                        width = LayWidthTuSize(size);
                    if (width <= 0)
                        width = candidate.DuctWidth;
                    if (width <= 0)
                        width = 100.0;

                    string ogType =
                        string.IsNullOrWhiteSpace(
                            candidate.ResolvedOgType)
                            ? defaultOg
                            : candidate.ResolvedOgType;

                    if (string.IsNullOrWhiteSpace(
                            candidate.ResolvedOgType))
                    {
                        OngGioTypeTextInfo typeInfo =
                            FindNearestOngGioTypeText(
                                candidate,
                                ogTypeTexts);
                        if (typeInfo != null &&
                            !string.IsNullOrWhiteSpace(typeInfo.Type))
                        {
                            ogType = typeInfo.Type;
                        }
                    }

                    string sizeForLayer = size;
                    if (!string.IsNullOrWhiteSpace(eiPart))
                        sizeForLayer = size + "_" + eiPart;

                    string layerName =
                        $"{layerPrefix}_{CleanLayerText(ogType)}_{CleanLayerText(sizeForLayer)}";

                    EnsureLayerExists(tr, db, layerName, true);

                    if (candidate.IsReducer)
                    {
                        converted += DrawReducerCandidate(
                            tr,
                            db,
                            btr,
                            candidate,
                            sizeTexts,
                            protectionTexts,
                            candidates,
                            ogType,
                            layerPrefix);
                    }
                    else
                    {
                        Polyline newPl =
                            CreatePolylineFromOngGioCandidate(candidate);
                        if (newPl == null)
                            continue;

                        newPl.SetDatabaseDefaults(db);
                        newPl.Layer = layerName;
                        newPl.ColorIndex = 256;
                        newPl.ConstantWidth = width;

                        btr.AppendEntity(newPl);
                        tr.AddNewlyCreatedDBObject(newPl, true);

                        if (TryGetOngGioLabelPlacement(
                                candidate,
                                out Point3d labelPoint,
                                out Vector3d labelDirection,
                                out double labelSegmentLength) &&
                            labelSegmentLength >=
                                Math.Max(width * 2.0, 1500.0))
                        {
                            AddOngGioLabel(
                                tr,
                                db,
                                btr,
                                layerName,
                                BuildOngGioDisplayText(size, eiPart),
                                width,
                                labelPoint,
                                labelDirection);
                        }

                        converted++;
                    }
                }

                tr.Commit();
            }

            ed.Regen();
            ed.WriteMessage(
                $"\n[VẼ OG TỰ ĐỘNG] Đã tạo {converted} đường tâm ống gió " +
                $"từ khung chữ nhật. Độ dày = cạnh lớn nhất của size, layer gốc: {layerPrefix}.");
        }

        private class OngGioSizeTextInfo
        {
            public Point3d Pos { get; set; }
            public string Size { get; set; }
            public double SideA { get; set; }
            public double SideB { get; set; }
            public double Width { get; set; }
            public string Ei { get; set; }
        }

        private class OngGioProtectionTextInfo
        {
            public Point3d Pos { get; set; }
            public string Text { get; set; }
        }

        private class OngGioTypeTextInfo
        {
            public Point3d Pos { get; set; }
            public string Type { get; set; }
        }

        private class OngGioSegmentInfo
        {
            public Point3d Start { get; set; }
            public Point3d End { get; set; }
            public Point3d Mid { get; set; }
            public Vector3d Direction { get; set; }
            public double Length { get; set; }
            public string SourceKey { get; set; }
        }

        private class OngGioArcInfo
        {
            public Point3d Center { get; set; }
            public double Radius { get; set; }
            public double StartAngle { get; set; }
            public double EndAngle { get; set; }
            public double IncludedAngle { get; set; }
            public Point3d Start { get; set; }
            public Point3d End { get; set; }
        }

        private class OngGioCenterlineCandidate
        {
            public Point3d Start { get; set; }
            public Point3d End { get; set; }
            public Point3d Mid { get; set; }
            public Vector3d Direction { get; set; }
            public double Length { get; set; }
            public double DuctWidth { get; set; }
            public string Source { get; set; }
            public bool IsArc { get; set; }
            public Point3d ArcCenter { get; set; }
            public double ArcRadius { get; set; }
            public double ArcStartAngle { get; set; }
            public double ArcEndAngle { get; set; }
            public double ArcIncludedAngle { get; set; }
            public bool IsReducer { get; set; }
            public double StartDuctWidth { get; set; }
            public double EndDuctWidth { get; set; }
            public List<Point3d> Vertices { get; set; }
            public OngGioSizeTextInfo ResolvedSizeInfo { get; set; }
            public string ResolvedProtection { get; set; }
            public string ResolvedOgType { get; set; }
        }

        private string GetOngGioAutoLayerPrefix(PipeUiContext ctx)
        {
            string systemCode = CleanLayerText(GetSystemCode(ctx));
            if (string.IsNullOrWhiteSpace(systemCode))
                systemCode = "ACMV";

            string viTri = GetViTriText("ACMV");
            if (!string.IsNullOrWhiteSpace(viTri))
                return $"{systemCode}_{viTri}";

            return systemCode;
        }

        private static string LayPlainTextTuMText(string contents)
        {
            string str = contents ?? "";
            str = str.Replace("\\P", " ");
            str = Regex.Replace(str, @"\\[A-Za-z][^;]*;", "");
            str = Regex.Replace(str, @"[{}]", "");
            str = str.Replace("\r", " ").Replace("\n", " ");

            if (str.Contains(";"))
            {
                string[] parts = str.Split(';');
                str = parts.Length > 0 ? parts[parts.Length - 1] : str;
            }

            return Regex.Replace(str, @"\s+", " ").Trim();
        }

        private bool TryParseOngGioSize(
            string source,
            out string sizeText,
            out double sideA,
            out double sideB,
            out double width,
            out string eiText)
        {
            sizeText = "";
            sideA = 0;
            sideB = 0;
            width = 0;
            eiText = "";
            if (string.IsNullOrWhiteSpace(source))
                return false;

            string n = source.ToUpperInvariant()
                .Replace(',', '.')
                .Replace('×', 'X')
                .Replace('Х', 'X');

            Match m = Regex.Match(
                n,
                @"(?<![A-Z0-9])(?<A>\d{2,4}(?:\.\d+)?)\s*[xX×*/\/\-]\s*(?<B>\d{2,4}(?:\.\d+)?)(?![A-Z0-9])");

            if (!m.Success)
                return false;

            if (!double.TryParse(
                    m.Groups["A"].Value,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out double a) ||
                !double.TryParse(
                    m.Groups["B"].Value,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out double b))
                return false;

            if (a < 50 || b < 50 || a > 5000 || b > 5000)
                return false;

            sizeText =
                FormatSizeNumber(a) + "x" + FormatSizeNumber(b);
            sideA = a;
            sideB = b;
            // Bề rộng nét = cạnh LỚN (900x300 → 900)
            width = Math.Max(a, b);

            Match ei = Regex.Match(
                n,
                @"\b(?<K>EI|CN)\s*(?<E>\d{0,3})\b");
            if (ei.Success)
                eiText =
                    ei.Groups["K"].Value +
                    ei.Groups["E"].Value;

            return true;
        }

        private bool TryParseOngGioProtection(
            string source,
            out string protectionText)
        {
            protectionText = "";

            if (string.IsNullOrWhiteSpace(source))
                return false;

            string n = source.ToUpperInvariant()
                .Replace("\r", " ")
                .Replace("\n", " ");

            Match m = Regex.Match(
                n,
                @"\b(?<K>EI|CN)\s*(?<E>\d{0,3})\b");

            if (!m.Success)
                return false;

            protectionText =
                m.Groups["K"].Value +
                m.Groups["E"].Value;

            return !string.IsNullOrWhiteSpace(protectionText);
        }

        private bool TryCreateOngGioCandidateFromRectangle(
            Entity src,
            out OngGioCenterlineCandidate candidate)
        {
            candidate = null;

            try
            {
                if (src is Polyline pl)
                {
                    if (PolylineHasArcSegments(pl))
                        return false;

                    List<Point3d> pts =
                        GetPolylineVerticesForOngGio(pl);

                    return TryCreateOngGioCandidateFromPointLoop(
                        pts,
                        "RECT-PL",
                        out candidate);
                }
            }
            catch { }

            return false;
        }

        private static bool PolylineHasArcSegments(
            Polyline pl)
        {
            if (pl == null || pl.NumberOfVertices < 2)
                return false;

            int segmentCount =
                pl.Closed
                    ? pl.NumberOfVertices
                    : pl.NumberOfVertices - 1;

            for (int i = 0; i < segmentCount; i++)
            {
                if (Math.Abs(pl.GetBulgeAt(i)) > 1e-9)
                    return true;
            }

            return false;
        }

        private static List<Point3d> GetPolylineVerticesForOngGio(
            Polyline pl)
        {
            var pts = new List<Point3d>();
            if (pl == null)
                return pts;

            for (int i = 0; i < pl.NumberOfVertices; i++)
            {
                Point3d p = pl.GetPoint3dAt(i);
                if (pts.Count == 0 ||
                    pts[pts.Count - 1].DistanceTo(p) > 1e-6)
                {
                    pts.Add(p);
                }
            }

            if (pts.Count > 1 &&
                pts[0].DistanceTo(pts[pts.Count - 1]) < 50.0)
            {
                pts.RemoveAt(pts.Count - 1);
            }

            if (!pl.Closed &&
                pts.Count >= 2 &&
                pts[0].DistanceTo(pts[pts.Count - 1]) >= 50.0)
            {
                pts.Clear();
            }

            return pts;
        }

        private bool TryCreateOngGioCandidateFromPointLoop(
            List<Point3d> pts,
            string source,
            out OngGioCenterlineCandidate candidate)
        {
            candidate = null;

            if (pts == null || pts.Count != 4)
                return false;

            double[] len = new double[4];
            Vector3d[] dir = new Vector3d[4];

            for (int i = 0; i < 4; i++)
            {
                Point3d a = pts[i];
                Point3d b = pts[(i + 1) % 4];
                Vector3d v = b - a;
                len[i] = v.Length;

                if (len[i] < 50.0)
                    return false;

                dir[i] = v.GetNormal();
            }

            double avg02 = (len[0] + len[2]) / 2.0;
            double avg13 = (len[1] + len[3]) / 2.0;

            bool pair02Parallel = AreParallel(dir[0], dir[2], 15.0);
            bool pair13Parallel = AreParallel(dir[1], dir[3], 15.0);

            if (!pair02Parallel && !pair13Parallel)
                return false;

            Point3d start;
            Point3d end;
            double ductWidth;
            double startWidth;
            double endWidth;

            bool use02AsEnds =
                pair02Parallel &&
                (!pair13Parallel || avg02 <= avg13);

            if (use02AsEnds)
            {
                start = MidPoint(pts[0], pts[1]);
                end = MidPoint(pts[2], pts[3]);
                ductWidth = avg02;
                startWidth = len[0];
                endWidth = len[2];
            }
            else
            {
                start = MidPoint(pts[1], pts[2]);
                end = MidPoint(pts[3], pts[0]);
                ductWidth = avg13;
                startWidth = len[1];
                endWidth = len[3];
            }

            bool ok = TryCreateOngGioCenterlineCandidate(
                start,
                end,
                ductWidth,
                source,
                out candidate);

            if (!ok || candidate == null)
                return false;

            candidate.StartDuctWidth = startWidth;
            candidate.EndDuctWidth = endWidth;
            candidate.IsReducer =
                Math.Abs(startWidth - endWidth) >
                Math.Max(80.0, Math.Max(startWidth, endWidth) * 0.12);

            return true;
        }

        private void AddOngGioSegmentsFromEntity(
            Entity src,
            List<OngGioSegmentInfo> segments)
        {
            if (src == null || segments == null)
                return;

            try
            {
                if (src is Arc)
                    return;

                string key = src.ObjectId.IsNull
                    ? Guid.NewGuid().ToString()
                    : src.ObjectId.ToString();

                if (src is Line line)
                {
                    AddOngGioSegment(
                        line.StartPoint,
                        line.EndPoint,
                        key,
                        segments);
                    return;
                }

                if (src is Polyline pl)
                {
                    for (int i = 0; i < pl.NumberOfVertices - 1; i++)
                    {
                        if (Math.Abs(pl.GetBulgeAt(i)) > 1e-9)
                            continue;

                        AddOngGioSegment(
                            pl.GetPoint3dAt(i),
                            pl.GetPoint3dAt(i + 1),
                            key,
                            segments);
                    }

                    if (pl.Closed && pl.NumberOfVertices > 2)
                    {
                        int last = pl.NumberOfVertices - 1;

                        if (Math.Abs(pl.GetBulgeAt(last)) <= 1e-9)
                        {
                            AddOngGioSegment(
                                pl.GetPoint3dAt(last),
                                pl.GetPoint3dAt(0),
                                key,
                                segments);
                        }
                    }

                    return;
                }

                if (src is Curve cv)
                {
                    AddOngGioSegment(
                        cv.StartPoint,
                        cv.EndPoint,
                        key,
                        segments);
                }
            }
            catch { }
        }

        private void AddOngGioArcFromEntity(
            Entity src,
            List<OngGioArcInfo> arcs)
        {
            if (src == null || arcs == null)
                return;

            try
            {
                if (src is Arc arc)
                {
                    AddOngGioArcInfo(
                        arc.Center,
                        arc.Radius,
                        arc.StartAngle,
                        arc.EndAngle,
                        NormalizeSignedAngle(
                            arc.EndAngle - arc.StartAngle),
                        arc.StartPoint,
                        arc.EndPoint,
                        arcs);
                    return;
                }

                if (src is Polyline pl)
                {
                    int segmentCount =
                        pl.Closed
                            ? pl.NumberOfVertices
                            : pl.NumberOfVertices - 1;

                    for (int i = 0; i < segmentCount; i++)
                    {
                        double bulge = pl.GetBulgeAt(i);
                        if (Math.Abs(bulge) <= 1e-9)
                            continue;

                        int next =
                            (i + 1) % pl.NumberOfVertices;

                        TryAddOngGioArcFromBulge(
                            pl.GetPoint3dAt(i),
                            pl.GetPoint3dAt(next),
                            bulge,
                            arcs);
                    }
                }
            }
            catch { }
        }

        private static void TryAddOngGioArcFromBulge(
            Point3d start,
            Point3d end,
            double bulge,
            List<OngGioArcInfo> arcs)
        {
            if (arcs == null || Math.Abs(bulge) <= 1e-9)
                return;

            Vector3d chord = end - start;
            double chordLength =
                Math.Sqrt(
                    chord.X * chord.X +
                    chord.Y * chord.Y);

            if (chordLength < 1e-6)
                return;

            double included = 4.0 * Math.Atan(bulge);
            if (Math.Abs(included) < Math.PI / 6.0 ||
                Math.Abs(included) > Math.PI * 0.75)
                return;

            double radius =
                chordLength * (1.0 + bulge * bulge) /
                (4.0 * Math.Abs(bulge));
            if (radius < 50.0)
                return;

            Point3d chordMiddle = MidPoint(start, end);
            double centerOffset =
                chordLength * (1.0 - bulge * bulge) /
                (4.0 * bulge);
            double normalX = -chord.Y / chordLength;
            double normalY = chord.X / chordLength;
            Point3d center =
                new Point3d(
                    chordMiddle.X + normalX * centerOffset,
                    chordMiddle.Y + normalY * centerOffset,
                    chordMiddle.Z);

            double startAngle =
                Math.Atan2(
                    start.Y - center.Y,
                    start.X - center.X);
            double endAngle = startAngle + included;

            AddOngGioArcInfo(
                center,
                radius,
                startAngle,
                endAngle,
                included,
                start,
                end,
                arcs);
        }

        private static void AddOngGioArcInfo(
            Point3d center,
            double radius,
            double startAngle,
            double endAngle,
            double included,
            Point3d start,
            Point3d end,
            List<OngGioArcInfo> arcs)
        {
            if (arcs == null)
                return;

            if (Math.Abs(included) < Math.PI / 6.0 ||
                Math.Abs(included) > Math.PI * 0.75 ||
                radius < 50.0)
                return;

            arcs.Add(
                new OngGioArcInfo
                {
                    Center = center,
                    Radius = radius,
                    StartAngle = startAngle,
                    EndAngle = endAngle,
                    IncludedAngle = included,
                    Start = start,
                    End = end
                });
        }

        private static void AddOngGioSegment(
            Point3d start,
            Point3d end,
            string sourceKey,
            List<OngGioSegmentInfo> segments)
        {
            Vector3d v = end - start;
            double len = v.Length;

            if (len < 100.0)
                return;

            Vector3d dir = v.GetNormal();

            segments.Add(
                new OngGioSegmentInfo
                {
                    Start = start,
                    End = end,
                    Mid = MidPoint(start, end),
                    Direction = dir,
                    Length = len,
                    SourceKey = sourceKey
                });
        }

        private List<OngGioCenterlineCandidate> CreateOngGioCandidatesFromParallelSegments(
            List<OngGioSegmentInfo> segments)
        {
            var result = new List<OngGioCenterlineCandidate>();

            if (segments == null || segments.Count < 2)
                return result;

            for (int i = 0; i < segments.Count; i++)
            {
                for (int j = i + 1; j < segments.Count; j++)
                {
                    OngGioSegmentInfo a = segments[i];
                    OngGioSegmentInfo b = segments[j];

                    if (a == null || b == null)
                        continue;

                    if (!AreParallel(a.Direction, b.Direction, 10.0))
                        continue;

                    if (!AreSimilarLength(a.Length, b.Length, 0.35))
                        continue;

                    double gap =
                        DistancePointToInfiniteLine(
                            b.Mid,
                            a.Start,
                            a.Direction);

                    if (gap < 50.0 || gap > 5000.0)
                        continue;

                    if (gap > Math.Min(a.Length, b.Length) * 0.75)
                        continue;

                    Vector3d u = a.Direction;

                    double a0 = DotPoint(a.Start, u);
                    double a1 = DotPoint(a.End, u);
                    double b0 = DotPoint(b.Start, u);
                    double b1 = DotPoint(b.End, u);

                    if (a0 > a1)
                        Swap(ref a0, ref a1);
                    if (b0 > b1)
                        Swap(ref b0, ref b1);

                    double startT = Math.Max(a0, b0);
                    double endT = Math.Min(a1, b1);
                    double overlap = endT - startT;

                    if (overlap < Math.Max(200.0, Math.Min(a.Length, b.Length) * 0.35))
                        continue;

                    Point3d aStart = PointOnSegmentProjection(a, startT, u);
                    Point3d aEnd = PointOnSegmentProjection(a, endT, u);
                    Point3d bStart = PointOnSegmentProjection(b, startT, u);
                    Point3d bEnd = PointOnSegmentProjection(b, endT, u);

                    if (aStart.DistanceTo(bStart) < 50.0 ||
                        aEnd.DistanceTo(bEnd) < 50.0)
                        continue;

                    Point3d centerStart = MidPoint(aStart, bStart);
                    Point3d centerEnd = MidPoint(aEnd, bEnd);

                    if (TryCreateOngGioCenterlineCandidate(
                            centerStart,
                            centerEnd,
                            gap,
                            "RECT-LINES",
                            out OngGioCenterlineCandidate candidate))
                    {
                        result.Add(candidate);
                    }
                }
            }

            return result;
        }

        private List<OngGioCenterlineCandidate> CreateReducerCandidatesFromEndSegments(
            List<OngGioSegmentInfo> segments)
        {
            var result = new List<OngGioCenterlineCandidate>();

            if (segments == null || segments.Count < 2)
                return result;

            for (int i = 0; i < segments.Count; i++)
            {
                for (int j = i + 1; j < segments.Count; j++)
                {
                    OngGioSegmentInfo a = segments[i];
                    OngGioSegmentInfo b = segments[j];

                    if (a == null || b == null)
                        continue;

                    if (!AreParallel(a.Direction, b.Direction, 12.0))
                        continue;

                    double maxLen = Math.Max(a.Length, b.Length);
                    double minLen = Math.Min(a.Length, b.Length);

                    if (maxLen < 80.0 || maxLen > 5000.0)
                        continue;

                    if (minLen / maxLen < 0.25)
                        continue;

                    if (Math.Abs(a.Length - b.Length) <=
                        Math.Max(80.0, maxLen * 0.12))
                        continue;

                    Vector3d centerVector = b.Mid - a.Mid;
                    double centerDistance = centerVector.Length;

                    if (centerDistance < 100.0 ||
                        centerDistance > maxLen * 4.0)
                        continue;

                    Vector3d centerDirection =
                        centerVector.GetNormal();

                    double dot =
                        Math.Abs(
                            centerDirection.DotProduct(
                                a.Direction.GetNormal()));

                    if (dot > Math.Sin(Math.PI / 9.0))
                        continue;

                    if (!HasReducerSideConnections(
                            a,
                            b,
                            segments))
                        continue;

                    if (!TryCreateOngGioCenterlineCandidate(
                            a.Mid,
                            b.Mid,
                            (a.Length + b.Length) / 2.0,
                            "REDUCER-END-LINES",
                            out OngGioCenterlineCandidate candidate))
                        continue;

                    candidate.IsReducer = true;
                    candidate.StartDuctWidth = a.Length;
                    candidate.EndDuctWidth = b.Length;

                    result.Add(candidate);
                }
            }

            return result;
        }

        private static bool HasReducerSideConnections(
            OngGioSegmentInfo firstEnd,
            OngGioSegmentInfo secondEnd,
            List<OngGioSegmentInfo> segments)
        {
            if (firstEnd == null ||
                secondEnd == null ||
                segments == null)
                return false;

            double direct =
                firstEnd.Start.DistanceTo(secondEnd.Start) +
                firstEnd.End.DistanceTo(secondEnd.End);
            double crossed =
                firstEnd.Start.DistanceTo(secondEnd.End) +
                firstEnd.End.DistanceTo(secondEnd.Start);

            Point3d firstTarget;
            Point3d secondTarget;

            if (direct <= crossed)
            {
                firstTarget = secondEnd.Start;
                secondTarget = secondEnd.End;
            }
            else
            {
                firstTarget = secondEnd.End;
                secondTarget = secondEnd.Start;
            }

            double endpointTolerance =
                Math.Min(
                    100.0,
                    Math.Max(
                        25.0,
                        Math.Min(firstEnd.Length, secondEnd.Length) * 0.05));

            return
                HasOngGioSideConnectingPoints(
                    firstEnd.Start,
                    firstTarget,
                    firstEnd,
                    secondEnd,
                    segments,
                    endpointTolerance) &&
                HasOngGioSideConnectingPoints(
                    firstEnd.End,
                    secondTarget,
                    firstEnd,
                    secondEnd,
                    segments,
                    endpointTolerance);
        }

        private static bool HasOngGioSideConnectingPoints(
            Point3d firstPoint,
            Point3d secondPoint,
            OngGioSegmentInfo firstEnd,
            OngGioSegmentInfo secondEnd,
            List<OngGioSegmentInfo> segments,
            double endpointTolerance)
        {
            Vector3d expected = secondPoint - firstPoint;
            if (expected.Length < 80.0)
                return false;

            Vector3d expectedDirection = expected.GetNormal();

            foreach (OngGioSegmentInfo side in segments)
            {
                if (side == null ||
                    ReferenceEquals(side, firstEnd) ||
                    ReferenceEquals(side, secondEnd))
                    continue;

                bool sameDirection =
                    side.Start.DistanceTo(firstPoint) <= endpointTolerance &&
                    side.End.DistanceTo(secondPoint) <= endpointTolerance;
                bool reverseDirection =
                    side.End.DistanceTo(firstPoint) <= endpointTolerance &&
                    side.Start.DistanceTo(secondPoint) <= endpointTolerance;

                if (!sameDirection && !reverseDirection)
                    continue;

                double alignment =
                    Math.Abs(
                        side.Direction.GetNormal()
                            .DotProduct(expectedDirection));

                if (alignment >= Math.Cos(Math.PI / 4.0))
                    return true;
            }

            return false;
        }

        private List<OngGioCenterlineCandidate> CreateOngGioCandidatesFromConcentricArcs(
            List<OngGioArcInfo> arcs)
        {
            var result = new List<OngGioCenterlineCandidate>();

            if (arcs == null || arcs.Count == 0)
                return result;

            var used = new HashSet<int>();

            for (int i = 0; i < arcs.Count; i++)
            {
                if (used.Contains(i))
                    continue;

                OngGioArcInfo a = arcs[i];
                int bestIndex = -1;
                double bestScore = double.MaxValue;
                OngGioCenterlineCandidate bestCandidate = null;

                for (int j = i + 1; j < arcs.Count; j++)
                {
                    if (used.Contains(j))
                        continue;

                    OngGioArcInfo b = arcs[j];

                    if (!TryCreateArcCandidateFromPair(
                            a,
                            b,
                            out OngGioCenterlineCandidate pairCandidate,
                            out double score))
                        continue;

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestIndex = j;
                        bestCandidate = pairCandidate;
                    }
                }

                if (bestIndex >= 0 && bestCandidate != null)
                {
                    used.Add(i);
                    used.Add(bestIndex);
                    result.Add(bestCandidate);
                }
                else
                {
                    used.Add(i);
                }
            }

            return result.Where(x => x != null).ToList();
        }

        private bool TryCreateArcCandidateFromPair(
            OngGioArcInfo first,
            OngGioArcInfo second,
            out OngGioCenterlineCandidate candidate,
            out double score)
        {
            candidate = null;
            score = double.MaxValue;

            if (first == null || second == null)
                return false;

            double centerGap =
                first.Center.DistanceTo(second.Center);
            if (centerGap > 80.0)
                return false;

            double firstSweep = Math.Abs(first.IncludedAngle);
            double secondSweep = Math.Abs(second.IncludedAngle);
            double sweepGap = Math.Abs(firstSweep - secondSweep);
            if (sweepGap > Math.PI / 10.0)
                return false;

            double radiusGap =
                Math.Abs(first.Radius - second.Radius);
            if (radiusGap < 50.0 || radiusGap > 5000.0)
                return false;

            double directPairing =
                first.Start.DistanceTo(second.Start) +
                first.End.DistanceTo(second.End);
            double crossedPairing =
                first.Start.DistanceTo(second.End) +
                first.End.DistanceTo(second.Start);
            bool useCrossedPairing = crossedPairing < directPairing;

            Point3d start =
                useCrossedPairing
                    ? MidPoint(first.Start, second.End)
                    : MidPoint(first.Start, second.Start);
            Point3d end =
                useCrossedPairing
                    ? MidPoint(first.End, second.Start)
                    : MidPoint(first.End, second.End);

            double startGap =
                useCrossedPairing
                    ? first.Start.DistanceTo(second.End)
                    : first.Start.DistanceTo(second.Start);
            double endGap =
                useCrossedPairing
                    ? first.End.DistanceTo(second.Start)
                    : first.End.DistanceTo(second.End);

            double endpointTolerance =
                Math.Max(
                    100.0,
                    radiusGap * 0.35 + centerGap);

            if (Math.Abs(startGap - radiusGap) > endpointTolerance ||
                Math.Abs(endGap - radiusGap) > endpointTolerance ||
                Math.Abs(startGap - endGap) > endpointTolerance)
                return false;

            Point3d firstArcMiddle =
                PointAtAngle(
                    first.Center,
                    first.Radius,
                    first.StartAngle + first.IncludedAngle / 2.0);
            Point3d secondArcMiddle =
                PointAtAngle(
                    second.Center,
                    second.Radius,
                    second.StartAngle + second.IncludedAngle / 2.0);
            Point3d middle =
                MidPoint(firstArcMiddle, secondArcMiddle);

            if (start.DistanceTo(middle) < 50.0 ||
                middle.DistanceTo(end) < 50.0)
                return false;

            Point3d center =
                MidPoint(first.Center, second.Center);
            double radius =
                (center.DistanceTo(start) +
                 center.DistanceTo(end)) / 2.0;
            double startAngle =
                Math.Atan2(
                    start.Y - center.Y,
                    start.X - center.X);
            double endAngle =
                Math.Atan2(
                    end.Y - center.Y,
                    end.X - center.X);
            double includedAngle =
                NormalizeSignedAngle(endAngle - startAngle);
            double ductWidth =
                (startGap + endGap) / 2.0;
            Vector3d direction = middle - start;

            if (direction.Length < 1e-9)
                return false;

            candidate = new OngGioCenterlineCandidate
            {
                Start = start,
                End = end,
                Mid = middle,
                Direction = direction.GetNormal(),
                Length =
                    start.DistanceTo(middle) +
                    middle.DistanceTo(end),
                DuctWidth = ductWidth,
                Source = "ELBOW-ARC-PAIR",
                IsArc = true,
                ArcCenter = center,
                ArcRadius = radius,
                ArcStartAngle = startAngle,
                ArcEndAngle = endAngle,
                ArcIncludedAngle = includedAngle,
                Vertices = new List<Point3d>
                {
                    start,
                    middle,
                    end
                }
            };

            double pairingScore =
                Math.Min(directPairing, crossedPairing);
            score =
                centerGap * 5.0 +
                sweepGap * Math.Max(first.Radius, second.Radius) +
                Math.Abs(startGap - endGap) * 2.0 +
                pairingScore * 0.05;

            return true;
        }

        private List<OngGioCenterlineCandidate> CreateTerminalOngGioCandidatesFromSingleArcs(
            List<OngGioArcInfo> arcs,
            List<OngGioCenterlineCandidate> pairedArcCandidates,
            List<OngGioCenterlineCandidate> nearbyCandidates,
            List<OngGioSegmentInfo> sourceSegments)
        {
            var result = new List<OngGioCenterlineCandidate>();

            if (arcs == null ||
                arcs.Count == 0 ||
                nearbyCandidates == null ||
                nearbyCandidates.Count == 0)
                return result;

            foreach (OngGioArcInfo arc in arcs)
            {
                if (arc == null)
                    continue;

                double sweep = Math.Abs(arc.IncludedAngle);
                if (sweep < Math.PI * 55.0 / 180.0 ||
                    sweep > Math.PI * 125.0 / 180.0)
                    continue;

                if (IsOngGioArcRepresentedByPair(
                        arc,
                        pairedArcCandidates))
                    continue;

                OngGioCenterlineCandidate bestCandidate = null;
                double bestScore = double.MaxValue;

                foreach (OngGioCenterlineCandidate neighbor in nearbyCandidates)
                {
                    if (neighbor == null ||
                        neighbor.IsArc ||
                        neighbor.Length < 100.0)
                        continue;

                    foreach (bool useNeighborStart in new[] { true, false })
                    {
                        Point3d neighborPoint =
                            useNeighborStart
                                ? neighbor.Start
                                : neighbor.End;
                        double ductWidth =
                            GetOngGioCandidateEndpointWidth(
                                neighbor,
                                useNeighborStart);

                        if (ductWidth < 80.0 || ductWidth > 5000.0)
                            continue;

                        foreach (double radiusOffset in
                                 new[]
                                 {
                                     -ductWidth / 2.0,
                                     ductWidth / 2.0
                                 })
                        {
                            double centerRadius =
                                arc.Radius + radiusOffset;
                            if (centerRadius < 50.0)
                                continue;

                            Point3d centerStart =
                                PointAtAngle(
                                    arc.Center,
                                    centerRadius,
                                    arc.StartAngle);
                            Point3d centerEnd =
                                PointAtAngle(
                                    arc.Center,
                                    centerRadius,
                                    arc.EndAngle);

                            TryCreateStrictTerminalArcCandidate(
                                arc,
                                neighbor,
                                neighborPoint,
                                ductWidth,
                                centerRadius,
                                centerStart,
                                centerEnd,
                                nearbyCandidates,
                                sourceSegments,
                                ref bestCandidate,
                                ref bestScore);
                        }
                    }
                }

                if (bestCandidate == null)
                    continue;

                bool duplicate =
                    result.Any(x =>
                        x != null &&
                        x.ArcCenter.DistanceTo(
                            bestCandidate.ArcCenter) <= 30.0 &&
                        Math.Abs(
                            x.ArcRadius -
                            bestCandidate.ArcRadius) <= 30.0 &&
                        ((x.Start.DistanceTo(
                              bestCandidate.Start) <= 50.0 &&
                          x.End.DistanceTo(
                              bestCandidate.End) <= 50.0) ||
                         (x.Start.DistanceTo(
                              bestCandidate.End) <= 50.0 &&
                          x.End.DistanceTo(
                              bestCandidate.Start) <= 50.0)));

                if (!duplicate)
                    result.Add(bestCandidate);
            }

            return result;
        }

        private static bool IsOngGioArcRepresentedByPair(
            OngGioArcInfo arc,
            List<OngGioCenterlineCandidate> pairedArcCandidates)
        {
            if (arc == null || pairedArcCandidates == null)
                return false;

            foreach (OngGioCenterlineCandidate pair in pairedArcCandidates)
            {
                if (pair == null || !pair.IsArc || pair.DuctWidth <= 0.0)
                    continue;

                double centerTolerance =
                    Math.Max(30.0, pair.DuctWidth * 0.05);
                if (arc.Center.DistanceTo(pair.ArcCenter) > centerTolerance)
                    continue;

                double sweepGap =
                    Math.Abs(
                        Math.Abs(arc.IncludedAngle) -
                        Math.Abs(pair.ArcIncludedAngle));
                if (sweepGap > Math.PI / 18.0)
                    continue;

                double boundaryOffset =
                    Math.Abs(arc.Radius - pair.ArcRadius);
                double offsetTolerance =
                    Math.Max(30.0, pair.DuctWidth * 0.10);

                if (Math.Abs(
                        boundaryOffset -
                        pair.DuctWidth / 2.0) <= offsetTolerance)
                    return true;
            }

            return false;
        }

        private static void TryCreateStrictTerminalArcCandidate(
            OngGioArcInfo arc,
            OngGioCenterlineCandidate neighbor,
            Point3d neighborPoint,
            double ductWidth,
            double centerRadius,
            Point3d centerStart,
            Point3d centerEnd,
            List<OngGioCenterlineCandidate> nearbyCandidates,
            List<OngGioSegmentInfo> sourceSegments,
            ref OngGioCenterlineCandidate bestCandidate,
            ref double bestScore)
        {
            if (arc == null || neighbor == null)
                return;

            double jointTolerance =
                Math.Max(
                    50.0,
                    Math.Min(250.0, ductWidth * 0.25));

            foreach (bool useArcStart in new[] { true, false })
            {
                Point3d jointPoint =
                    useArcStart ? centerStart : centerEnd;
                Point3d farPoint =
                    useArcStart ? centerEnd : centerStart;
                Point3d jointBoundaryPoint =
                    useArcStart ? arc.Start : arc.End;
                Point3d farBoundaryPoint =
                    useArcStart ? arc.End : arc.Start;
                double jointAngle =
                    useArcStart
                        ? arc.StartAngle
                        : arc.EndAngle;
                double farAngle =
                    useArcStart
                        ? arc.EndAngle
                        : arc.StartAngle;
                Vector3d jointTangent =
                    GetOngGioArcTangent(jointAngle);
                Vector3d farTangent =
                    GetOngGioArcTangent(farAngle);
                double jointDistance =
                    neighborPoint.DistanceTo(jointPoint);

                if (jointDistance > jointTolerance ||
                    !AreParallel(
                        neighbor.Direction,
                        jointTangent,
                        10.0))
                    continue;

                bool hasJointOutline =
                    HasOngGioArcBoundaryTangentContinuation(
                        jointBoundaryPoint,
                        jointTangent,
                        ductWidth,
                        sourceSegments,
                        out double jointOutlineScore);
                double exactJointTolerance =
                    Math.Max(
                        35.0,
                        Math.Min(100.0, ductWidth * 0.10));

                if (!hasJointOutline &&
                    jointDistance > exactJointTolerance)
                    continue;

                if (!HasStrictTerminalArcFarEnd(
                        farPoint,
                        farBoundaryPoint,
                        farTangent,
                        ductWidth,
                        neighbor,
                        nearbyCandidates,
                        sourceSegments,
                        jointTolerance,
                        out double farEndScore))
                    continue;

                double alignment =
                    Math.Abs(
                        neighbor.Direction.GetNormal()
                            .DotProduct(jointTangent));
                double score =
                    jointDistance +
                    farEndScore +
                    (hasJointOutline
                        ? jointOutlineScore * 0.25
                        : 0.0) +
                    (1.0 - alignment) * ductWidth;

                if (score >= bestScore)
                    continue;

                Point3d middle =
                    PointAtAngle(
                        arc.Center,
                        centerRadius,
                        arc.StartAngle +
                        arc.IncludedAngle / 2.0);
                Vector3d direction = middle - centerStart;
                if (direction.Length < 1e-9)
                    continue;

                bestCandidate =
                    new OngGioCenterlineCandidate
                    {
                        Start = centerStart,
                        End = centerEnd,
                        Mid = middle,
                        Direction = direction.GetNormal(),
                        Length =
                            centerStart.DistanceTo(middle) +
                            middle.DistanceTo(centerEnd),
                        DuctWidth = ductWidth,
                        Source = "ELBOW-TERMINAL-STRICT",
                        IsArc = true,
                        ArcCenter = arc.Center,
                        ArcRadius = centerRadius,
                        ArcStartAngle = arc.StartAngle,
                        ArcEndAngle = arc.EndAngle,
                        ArcIncludedAngle = arc.IncludedAngle,
                        Vertices = new List<Point3d>
                        {
                            centerStart,
                            middle,
                            centerEnd
                        }
                    };
                bestScore = score;
            }
        }

        private static bool HasStrictTerminalArcFarEnd(
            Point3d farPoint,
            Point3d farBoundaryPoint,
            Vector3d farTangent,
            double ductWidth,
            OngGioCenterlineCandidate joinedNeighbor,
            List<OngGioCenterlineCandidate> nearbyCandidates,
            List<OngGioSegmentInfo> sourceSegments,
            double tolerance,
            out double score)
        {
            score = double.MaxValue;

            foreach (OngGioCenterlineCandidate candidate in
                     nearbyCandidates ??
                     new List<OngGioCenterlineCandidate>())
            {
                if (candidate == null ||
                    candidate.IsArc ||
                    ReferenceEquals(candidate, joinedNeighbor))
                    continue;

                foreach (bool useStart in new[] { true, false })
                {
                    Point3d endpoint =
                        useStart ? candidate.Start : candidate.End;
                    double endpointWidth =
                        GetOngGioCandidateEndpointWidth(
                            candidate,
                            useStart);

                    if (Math.Abs(endpointWidth - ductWidth) >
                        Math.Max(80.0, ductWidth * 0.20))
                        continue;

                    double distance = endpoint.DistanceTo(farPoint);
                    if (distance > tolerance ||
                        !AreParallel(
                            candidate.Direction,
                            farTangent,
                            18.0))
                        continue;

                    score = distance;
                    return true;
                }
            }

            Vector3d capDirection =
                farTangent.RotateBy(
                    Math.PI / 2.0,
                    Vector3d.ZAxis).GetNormal();
            Point3d expectedFirst =
                farPoint + capDirection * (ductWidth / 2.0);
            Point3d expectedSecond =
                farPoint - capDirection * (ductWidth / 2.0);

            foreach (OngGioSegmentInfo segment in
                     sourceSegments ??
                     new List<OngGioSegmentInfo>())
            {
                if (segment == null ||
                    segment.Length < ductWidth * 0.82 ||
                    segment.Length > ductWidth * 1.18 ||
                    !AreParallel(
                        segment.Direction,
                        capDirection,
                        10.0))
                    continue;

                double direct =
                    segment.Start.DistanceTo(expectedFirst) +
                    segment.End.DistanceTo(expectedSecond);
                double crossed =
                    segment.Start.DistanceTo(expectedSecond) +
                    segment.End.DistanceTo(expectedFirst);
                double endpointError = Math.Min(direct, crossed) / 2.0;

                if (endpointError > tolerance)
                    continue;

                score = endpointError;
                return true;
            }

            if (HasOngGioArcBoundaryTangentContinuation(
                    farBoundaryPoint,
                    farTangent,
                    ductWidth,
                    sourceSegments,
                    out double continuationScore))
            {
                score = continuationScore;
                return true;
            }

            return false;
        }

        private static bool HasOngGioArcBoundaryTangentContinuation(
            Point3d boundaryPoint,
            Vector3d tangent,
            double ductWidth,
            List<OngGioSegmentInfo> sourceSegments,
            out double score)
        {
            score = double.MaxValue;

            if (sourceSegments == null || sourceSegments.Count == 0)
                return false;

            double endpointTolerance =
                Math.Max(
                    35.0,
                    Math.Min(150.0, ductWidth * 0.15));
            double minimumLength =
                Math.Max(80.0, ductWidth * 0.15);

            foreach (OngGioSegmentInfo segment in sourceSegments)
            {
                if (segment == null ||
                    segment.Length < minimumLength ||
                    !AreParallel(
                        segment.Direction,
                        tangent,
                        12.0))
                    continue;

                double endpointDistance =
                    Math.Min(
                        boundaryPoint.DistanceTo(segment.Start),
                        boundaryPoint.DistanceTo(segment.End));

                if (endpointDistance > endpointTolerance)
                    continue;

                if (endpointDistance < score)
                    score = endpointDistance;
            }

            return score < double.MaxValue;
        }

        private static double GetOngGioCandidateEndpointWidth(
            OngGioCenterlineCandidate candidate,
            bool useStart)
        {
            if (candidate == null)
                return 0.0;

            if (candidate.IsReducer)
            {
                double endpointWidth =
                    useStart
                        ? candidate.StartDuctWidth
                        : candidate.EndDuctWidth;
                if (endpointWidth > 0.0)
                    return endpointWidth;
            }

            return candidate.DuctWidth;
        }

        private static Vector3d GetOngGioArcTangent(
            double angle)
        {
            return new Vector3d(
                -Math.Sin(angle),
                Math.Cos(angle),
                0.0).GetNormal();
        }

        private static Point3d PointOnSegmentProjection(
            OngGioSegmentInfo seg,
            double targetT,
            Vector3d direction)
        {
            double startT = DotPoint(seg.Start, direction);
            return seg.Start + direction * (targetT - startT);
        }

        private static bool TryCreateOngGioCenterlineCandidate(
            Point3d start,
            Point3d end,
            double ductWidth,
            string source,
            out OngGioCenterlineCandidate candidate)
        {
            candidate = null;

            Vector3d v = end - start;
            double len = v.Length;

            if (len < 100.0)
                return false;

            candidate =
                new OngGioCenterlineCandidate
                {
                    Start = start,
                    End = end,
                    Mid = MidPoint(start, end),
                    Direction = v.GetNormal(),
                    Length = len,
                    DuctWidth = ductWidth,
                    Source = source
                };

            return true;
        }

        private static List<OngGioCenterlineCandidate> RemoveDuplicateOngGioCandidates(
            List<OngGioCenterlineCandidate> candidates)
        {
            var result = new List<OngGioCenterlineCandidate>();

            foreach (OngGioCenterlineCandidate candidate in
                     (candidates ?? new List<OngGioCenterlineCandidate>())
                     .Where(x => x != null)
                     .OrderByDescending(x => x.Length))
            {
                bool duplicate = false;

                foreach (OngGioCenterlineCandidate existing in result)
                {
                    if (IsDuplicateOngGioCandidate(existing, candidate))
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                    result.Add(candidate);
            }

            return result;
        }

        private static bool IsDuplicateOngGioCandidate(
            OngGioCenterlineCandidate a,
            OngGioCenterlineCandidate b)
        {
            if (a == null || b == null)
                return false;

            if (!AreParallel(a.Direction, b.Direction, 8.0))
                return false;

            double tol = Math.Max(
                120.0,
                Math.Min(
                    Math.Max(a.DuctWidth, 0.0),
                    Math.Max(b.DuctWidth, 0.0)) * 0.35);

            bool sameEnds =
                a.Start.DistanceTo(b.Start) <= tol &&
                a.End.DistanceTo(b.End) <= tol;

            bool reverseEnds =
                a.Start.DistanceTo(b.End) <= tol &&
                a.End.DistanceTo(b.Start) <= tol;

            bool sameMiddle =
                a.Mid.DistanceTo(b.Mid) <= tol &&
                Math.Abs(a.Length - b.Length) <=
                Math.Max(200.0, Math.Min(a.Length, b.Length) * 0.2);

            return sameEnds || reverseEnds || sameMiddle;
        }

        private List<OngGioCenterlineCandidate> RemoveShortStraightCandidatesNearArcs(
            List<OngGioCenterlineCandidate> candidates,
            List<OngGioCenterlineCandidate> arcCandidates)
        {
            if (candidates == null ||
                arcCandidates == null ||
                arcCandidates.Count == 0)
                return candidates ?? new List<OngGioCenterlineCandidate>();

            var result = new List<OngGioCenterlineCandidate>();

            foreach (OngGioCenterlineCandidate candidate in candidates)
            {
                if (candidate == null)
                    continue;

                if (candidate.IsArc || candidate.IsReducer)
                {
                    result.Add(candidate);
                    continue;
                }

                bool nearArc = false;

                foreach (OngGioCenterlineCandidate arc in arcCandidates)
                {
                    if (arc == null || !arc.IsArc)
                        continue;

                    if (IsCandidateInsideArcFitting(
                            candidate,
                            arc))
                    {
                        nearArc = true;
                        break;
                    }
                }

                if (!nearArc)
                    result.Add(candidate);
            }

            return result;
        }

        private static bool IsCandidateInsideArcFitting(
            OngGioCenterlineCandidate candidate,
            OngGioCenterlineCandidate arc)
        {
            if (candidate == null ||
                arc == null ||
                !arc.IsArc)
                return false;

            double maxLength =
                Math.Max(
                    arc.Length * 1.6,
                    arc.DuctWidth * 2.5);
            if (candidate.Length > maxLength)
                return false;

            double tolerance =
                Math.Max(
                    150.0,
                    Math.Max(candidate.DuctWidth, arc.DuctWidth) * 0.65);

            double startDistance =
                DistancePointToOngGioCandidatePath(
                    candidate.Start,
                    arc);
            double endDistance =
                DistancePointToOngGioCandidatePath(
                    candidate.End,
                    arc);

            if (startDistance <= tolerance &&
                endDistance <= tolerance)
                return true;

            if (!candidate.IsReducer)
                return false;

            double middleDistance =
                DistancePointToOngGioCandidatePath(
                    candidate.Mid,
                    arc);

            return
                middleDistance <= tolerance &&
                Math.Min(startDistance, endDistance) <= tolerance &&
                Math.Max(startDistance, endDistance) <= tolerance * 1.5;
        }

        private static double DistancePointToOngGioCandidatePath(
            Point3d point,
            OngGioCenterlineCandidate path)
        {
            if (path == null)
                return double.MaxValue;

            if (path.Vertices != null &&
                path.Vertices.Count >= 2)
            {
                double best = double.MaxValue;

                for (int i = 0; i < path.Vertices.Count - 1; i++)
                {
                    double distance =
                        DistancePointToSegment(
                            point,
                            path.Vertices[i],
                            path.Vertices[i + 1],
                            out double ignored);

                    if (distance < best)
                        best = distance;
                }

                return best;
            }

            return DistancePointToSegment(
                point,
                path.Start,
                path.End,
                out double unused);
        }

        private List<OngGioCenterlineCandidate> RemoveOrTrimCandidatesAtReducers(
            List<OngGioCenterlineCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0)
                return candidates ?? new List<OngGioCenterlineCandidate>();

            List<OngGioCenterlineCandidate> reducers =
                candidates
                    .Where(x => x != null && x.IsReducer)
                    .ToList();

            if (reducers.Count == 0)
                return candidates;

            var result = new List<OngGioCenterlineCandidate>();

            foreach (OngGioCenterlineCandidate candidate in candidates)
            {
                if (candidate == null)
                    continue;

                if (candidate.IsReducer || candidate.IsArc)
                {
                    result.Add(candidate);
                    continue;
                }

                bool remove = false;

                foreach (OngGioCenterlineCandidate reducer in reducers)
                {
                    if (ReferenceEquals(candidate, reducer))
                        continue;

                    if (IsCandidateInsideReducerEnvelope(
                            candidate,
                            reducer))
                    {
                        remove = true;
                        break;
                    }

                    TrimStraightCandidateAtReducerBoundary(
                        candidate,
                        reducer);

                    if (candidate.Length < 100.0)
                    {
                        remove = true;
                        break;
                    }
                }

                if (!remove)
                    result.Add(candidate);
            }

            return result;
        }

        private static bool IsCandidateInsideReducerEnvelope(
            OngGioCenterlineCandidate candidate,
            OngGioCenterlineCandidate reducer)
        {
            if (candidate == null ||
                reducer == null ||
                !reducer.IsReducer)
                return false;

            Vector3d axis = reducer.End - reducer.Start;
            double reducerLength = axis.Length;
            if (reducerLength < 1e-9)
                return false;

            Vector3d direction = axis.GetNormal();
            double startT =
                (candidate.Start - reducer.Start)
                    .DotProduct(direction);
            double endT =
                (candidate.End - reducer.Start)
                    .DotProduct(direction);
            double axialTolerance =
                Math.Max(80.0, reducerLength * 0.08);

            if (startT < -axialTolerance ||
                startT > reducerLength + axialTolerance ||
                endT < -axialTolerance ||
                endT > reducerLength + axialTolerance)
                return false;

            double maxEndWidth =
                Math.Max(
                    reducer.StartDuctWidth,
                    reducer.EndDuctWidth);
            if (maxEndWidth <= 0.0)
                maxEndWidth = reducer.DuctWidth;

            double lateralTolerance =
                Math.Max(120.0, maxEndWidth * 0.60);
            double startLateral =
                DistancePointToInfiniteLine(
                    candidate.Start,
                    reducer.Start,
                    direction);
            double endLateral =
                DistancePointToInfiniteLine(
                    candidate.End,
                    reducer.Start,
                    direction);
            double maxCandidateLength =
                Math.Max(
                    reducerLength * 1.5,
                    maxEndWidth * 1.25);

            return
                startLateral <= lateralTolerance &&
                endLateral <= lateralTolerance &&
                candidate.Length <= maxCandidateLength;
        }

        private static void TrimStraightCandidateAtReducerBoundary(
            OngGioCenterlineCandidate candidate,
            OngGioCenterlineCandidate reducer)
        {
            if (candidate == null ||
                reducer == null ||
                !reducer.IsReducer ||
                candidate.IsArc ||
                candidate.IsReducer ||
                (candidate.Vertices != null &&
                 candidate.Vertices.Count > 2))
                return;

            Vector3d axis = reducer.End - reducer.Start;
            double reducerLength = axis.Length;
            if (reducerLength < 1e-9)
                return;

            Vector3d direction = axis.GetNormal();
            if (!AreParallel(candidate.Direction, direction, 12.0))
                return;

            double lateralTolerance =
                Math.Max(
                    100.0,
                    Math.Min(
                        Math.Max(candidate.DuctWidth, 0.0),
                        Math.Max(reducer.DuctWidth, 0.0)) * 0.35);

            if (DistancePointToInfiniteLine(
                    candidate.Start,
                    reducer.Start,
                    direction) > lateralTolerance ||
                DistancePointToInfiniteLine(
                    candidate.End,
                    reducer.Start,
                    direction) > lateralTolerance)
                return;

            double startT =
                (candidate.Start - reducer.Start)
                    .DotProduct(direction);
            double endT =
                (candidate.End - reducer.Start)
                    .DotProduct(direction);
            double axialTolerance =
                Math.Max(50.0, reducerLength * 0.05);

            bool startInside =
                startT >= -axialTolerance &&
                startT <= reducerLength + axialTolerance;
            bool endInside =
                endT >= -axialTolerance &&
                endT <= reducerLength + axialTolerance;

            if (startInside == endInside)
                return;

            double outsideT = startInside ? endT : startT;
            Point3d boundary =
                outsideT < 0.0
                    ? reducer.Start
                    : reducer.End;

            if (startInside)
                candidate.Start = boundary;
            else
                candidate.End = boundary;

            RefreshOngGioCandidateGeometry(candidate);
        }

        private List<OngGioCenterlineCandidate> MergeMiterStraightCandidates(
            List<OngGioCenterlineCandidate> candidates)
        {
            if (candidates == null || candidates.Count < 2)
                return candidates ?? new List<OngGioCenterlineCandidate>();

            var used = new HashSet<int>();
            var result = new List<OngGioCenterlineCandidate>();

            for (int i = 0; i < candidates.Count; i++)
            {
                if (used.Contains(i))
                    continue;

                OngGioCenterlineCandidate a = candidates[i];
                if (a == null ||
                    a.IsArc ||
                    a.IsReducer ||
                    a.DuctWidth <= 0.0)
                {
                    if (a != null)
                        result.Add(a);
                    continue;
                }

                int bestIndex = -1;
                Point3d jointA = Point3d.Origin;
                Point3d jointB = Point3d.Origin;
                Point3d farA = Point3d.Origin;
                Point3d farB = Point3d.Origin;
                double bestScore = double.MaxValue;

                for (int j = i + 1; j < candidates.Count; j++)
                {
                    if (used.Contains(j))
                        continue;

                    OngGioCenterlineCandidate b = candidates[j];
                    if (b == null ||
                        b.IsArc ||
                        b.IsReducer ||
                        b.DuctWidth <= 0.0)
                        continue;

                    if (Math.Abs(a.DuctWidth - b.DuctWidth) >
                        Math.Max(100.0, Math.Max(a.DuctWidth, b.DuctWidth) * 0.2))
                        continue;

                    if (AreParallel(a.Direction, b.Direction, 18.0))
                        continue;

                    Point3d ja;
                    Point3d jb;
                    Point3d fa;
                    Point3d fb;
                    double d =
                        ClosestEndpointPair(
                            a,
                            b,
                            out ja,
                            out jb,
                            out fa,
                            out fb);

                    double tol =
                        Math.Max(250.0, Math.Max(a.DuctWidth, b.DuctWidth) * 0.75);

                    if (d > tol)
                        continue;

                    double score = d + Math.Abs(a.Length - b.Length) * 0.05;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestIndex = j;
                        jointA = ja;
                        jointB = jb;
                        farA = fa;
                        farB = fb;
                    }
                }

                if (bestIndex < 0)
                {
                    result.Add(a);
                    continue;
                }

                OngGioCenterlineCandidate other = candidates[bestIndex];
                Point3d joint = MidPoint(jointA, jointB);
                var vertices = new List<Point3d> { farA, joint, farB };

                OngGioCenterlineCandidate merged =
                    new OngGioCenterlineCandidate
                    {
                        Start = farA,
                        End = farB,
                        Mid = joint,
                        Direction = (joint - farA).GetNormal(),
                        Length = farA.DistanceTo(joint) + joint.DistanceTo(farB),
                        DuctWidth = (a.DuctWidth + other.DuctWidth) / 2.0,
                        Source = "MITER",
                        Vertices = vertices
                    };

                result.Add(merged);
                used.Add(i);
                used.Add(bestIndex);
            }

            return result;
        }

        private static double ClosestEndpointPair(
            OngGioCenterlineCandidate a,
            OngGioCenterlineCandidate b,
            out Point3d jointA,
            out Point3d jointB,
            out Point3d farA,
            out Point3d farB)
        {
            jointA = a.Start;
            jointB = b.Start;
            farA = a.End;
            farB = b.End;

            double best = a.Start.DistanceTo(b.Start);

            double d = a.Start.DistanceTo(b.End);
            if (d < best)
            {
                best = d;
                jointA = a.Start;
                jointB = b.End;
                farA = a.End;
                farB = b.Start;
            }

            d = a.End.DistanceTo(b.Start);
            if (d < best)
            {
                best = d;
                jointA = a.End;
                jointB = b.Start;
                farA = a.Start;
                farB = b.End;
            }

            d = a.End.DistanceTo(b.End);
            if (d < best)
            {
                best = d;
                jointA = a.End;
                jointB = b.End;
                farA = a.Start;
                farB = b.Start;
            }

            return best;
        }

        private void ExtendBranchCandidatesToNearbyMainDucts(
            List<OngGioCenterlineCandidate> candidates)
        {
            if (candidates == null || candidates.Count < 2)
                return;

            foreach (OngGioCenterlineCandidate branch in candidates)
            {
                if (branch == null ||
                    branch.IsArc ||
                    branch.IsReducer ||
                    branch.DuctWidth <= 0.0)
                    continue;

                foreach (bool useStart in new[] { true, false })
                {
                    Point3d endPoint =
                        useStart ? branch.Start : branch.End;

                    OngGioCenterlineCandidate bestMain = null;
                    Point3d bestPoint = Point3d.Origin;
                    double bestScore = double.MaxValue;

                    foreach (OngGioCenterlineCandidate main in candidates)
                    {
                        if (main == null ||
                            ReferenceEquals(main, branch) ||
                            main.IsArc ||
                            main.IsReducer ||
                            main.DuctWidth <= branch.DuctWidth * 1.15)
                            continue;

                        if (AreParallel(
                                branch.Direction,
                                main.Direction,
                                18.0))
                            continue;

                        double t;
                        double d =
                            DistancePointToSegment(
                                endPoint,
                                main.Start,
                                main.End,
                                out t);

                        if (t < -0.1 || t > 1.1)
                            continue;

                        double tol =
                            Math.Max(
                                300.0,
                                (branch.DuctWidth + main.DuctWidth) * 0.6);

                        if (d > tol)
                            continue;

                        if (d < bestScore)
                        {
                            bestScore = d;
                            bestMain = main;
                            Point3d projected =
                                ProjectPointToSegment(
                                    endPoint,
                                    main.Start,
                                    main.End);
                            Vector3d edgeDirection =
                                endPoint - projected;
                            if (edgeDirection.Length < 1e-9)
                            {
                                edgeDirection =
                                    branch.Direction.RotateBy(
                                        Math.PI / 2.0,
                                        Vector3d.ZAxis);
                            }

                            edgeDirection = edgeDirection.GetNormal();
                            bestPoint =
                                projected +
                                edgeDirection *
                                (main.DuctWidth / 2.0);
                        }
                    }

                    if (bestMain == null)
                        continue;

                    if (useStart)
                        branch.Start = bestPoint;
                    else
                        branch.End = bestPoint;

                    RefreshOngGioCandidateGeometry(branch);
                }
            }
        }

        private bool IsOngGioCandidateBlockedByInternalMark(
            OngGioCenterlineCandidate candidate,
            List<OngGioSegmentInfo> segments)
        {
            if (candidate == null ||
                candidate.IsArc ||
                candidate.IsReducer ||
                segments == null ||
                segments.Count == 0 ||
                candidate.DuctWidth <= 0.0)
                return false;

            int perpendicularInsideCount = 0;
            double halfWidth =
                Math.Max(candidate.DuctWidth / 2.0, 50.0);

            foreach (OngGioSegmentInfo seg in segments)
            {
                if (seg == null || seg.Length < 80.0)
                    continue;

                double t;
                double centerDist =
                    DistancePointToSegment(
                        seg.Mid,
                        candidate.Start,
                        candidate.End,
                        out t);

                if (t < 0.08 || t > 0.92)
                    continue;

                if (centerDist > halfWidth + 80.0)
                    continue;

                double axisDot =
                    Math.Abs(
                        seg.Direction.GetNormal()
                            .DotProduct(candidate.Direction.GetNormal()));

                bool isParallelToDuct = axisDot > Math.Cos(Math.PI / 12.0);
                bool isPerpendicularToDuct = axisDot < Math.Sin(Math.PI / 12.0);

                if (isParallelToDuct)
                    continue;

                if (isPerpendicularToDuct)
                {
                    perpendicularInsideCount++;
                    continue;
                }

                if (seg.Length >= Math.Min(candidate.Length * 0.25, halfWidth * 0.8))
                    return true;
            }

            return perpendicularInsideCount >= 2;
        }

        private OngGioSizeTextInfo FindNearestOngGioSizeText(
            OngGioCenterlineCandidate candidate,
            List<OngGioSizeTextInfo> texts)
        {
            if (candidate == null || texts == null || texts.Count == 0)
                return null;

            OngGioSizeTextInfo best = null;
            double bestScore = double.MaxValue;
            double bestGeometryDifference = double.MaxValue;

            if (candidate.DuctWidth > 0.0)
            {
                foreach (OngGioSizeTextInfo text in texts)
                {
                    if (text == null)
                        continue;

                    double difference =
                        GetOngGioGeometryDifference(
                            text,
                            candidate.DuctWidth);
                    if (difference < bestGeometryDifference)
                        bestGeometryDifference = difference;
                }
            }

            double geometryTolerance =
                Math.Max(
                    20.0,
                    candidate.DuctWidth * 0.02);

            foreach (OngGioSizeTextInfo text in texts)
            {
                if (text == null)
                    continue;

                double geometryDifference =
                    GetOngGioGeometryDifference(
                        text,
                        candidate.DuctWidth);

                if (bestGeometryDifference < double.MaxValue &&
                    geometryDifference >
                        bestGeometryDifference + geometryTolerance)
                    continue;

                double outsidePenalty;
                double dLine =
                    GetOngGioTextDistanceToCandidate(
                        text.Pos,
                        candidate,
                        out outsidePenalty);

                double dMid =
                    text.Pos.DistanceTo(candidate.Mid);
                double widthPenalty =
                    geometryDifference * 8.0;

                double score =
                    dLine +
                    outsidePenalty * 0.5 +
                    dMid * 0.02 +
                    widthPenalty;

                if (score < bestScore)
                {
                    bestScore = score;
                    best = text;
                }
            }

            return best;
        }

        private static double GetOngGioGeometryDifference(
            OngGioSizeTextInfo text,
            double measuredWidth)
        {
            if (text == null || measuredWidth <= 0.0)
                return 0.0;

            double difference = double.MaxValue;

            if (text.SideA > 0.0)
            {
                difference =
                    Math.Min(
                        difference,
                        Math.Abs(text.SideA - measuredWidth));
            }

            if (text.SideB > 0.0)
            {
                difference =
                    Math.Min(
                        difference,
                        Math.Abs(text.SideB - measuredWidth));
            }

            if (difference == double.MaxValue &&
                text.Width > 0.0)
            {
                difference =
                    Math.Abs(text.Width - measuredWidth);
            }

            return difference == double.MaxValue
                ? 0.0
                : difference;
        }

        private static double GetOngGioTextDistanceToCandidate(
            Point3d textPoint,
            OngGioCenterlineCandidate candidate,
            out double outsidePenalty)
        {
            outsidePenalty = 0.0;

            if (candidate == null)
                return double.MaxValue;

            if (candidate.Vertices != null &&
                candidate.Vertices.Count >= 2)
            {
                return DistancePointToOngGioCandidatePath(
                    textPoint,
                    candidate);
            }

            double distance =
                DistancePointToSegment(
                    textPoint,
                    candidate.Start,
                    candidate.End,
                    out double t);

            if (t < 0.0)
                outsidePenalty = -t * candidate.Length;
            else if (t > 1.0)
                outsidePenalty = (t - 1.0) * candidate.Length;

            return distance;
        }

        private OngGioProtectionTextInfo FindNearestOngGioProtectionText(
            OngGioCenterlineCandidate candidate,
            List<OngGioProtectionTextInfo> texts)
        {
            if (candidate == null || texts == null || texts.Count == 0)
                return null;

            OngGioProtectionTextInfo best = null;
            double bestScore = double.MaxValue;

            foreach (OngGioProtectionTextInfo text in texts)
            {
                if (text == null)
                    continue;

                double outsidePenalty;
                double dLine =
                    GetOngGioTextDistanceToCandidate(
                        text.Pos,
                        candidate,
                        out outsidePenalty);

                double score =
                    dLine +
                    outsidePenalty * 0.5 +
                    text.Pos.DistanceTo(candidate.Mid) * 0.05;

                if (score < bestScore)
                {
                    bestScore = score;
                    best = text;
                }
            }

            return best;
        }

        private OngGioTypeTextInfo FindNearestOngGioTypeText(
            OngGioCenterlineCandidate candidate,
            List<OngGioTypeTextInfo> texts)
        {
            if (candidate == null || texts == null || texts.Count == 0)
                return null;

            OngGioTypeTextInfo best = null;
            double bestScore = double.MaxValue;

            foreach (OngGioTypeTextInfo text in texts)
            {
                if (text == null)
                    continue;

                double outsidePenalty;
                double dLine =
                    GetOngGioTextDistanceToCandidate(
                        text.Pos,
                        candidate,
                        out outsidePenalty);

                double score =
                    dLine +
                    outsidePenalty * 0.5 +
                    text.Pos.DistanceTo(candidate.Mid) * 0.05;

                if (score < bestScore)
                {
                    bestScore = score;
                    best = text;
                }
            }

            return best;
        }

        private void ResolveOngGioCandidateMetadata(
            List<OngGioCenterlineCandidate> candidates,
            List<OngGioSizeTextInfo> sizeTexts,
            List<OngGioProtectionTextInfo> protectionTexts,
            List<OngGioTypeTextInfo> typeTexts,
            string defaultOgType)
        {
            if (candidates == null)
                return;

            foreach (OngGioCenterlineCandidate candidate in candidates)
            {
                if (candidate == null || candidate.IsReducer)
                    continue;

                OngGioSizeTextInfo sizeInfo =
                    FindNearestOngGioSizeText(
                        candidate,
                        sizeTexts);
                if (sizeInfo == null)
                    continue;

                candidate.ResolvedSizeInfo = sizeInfo;
                candidate.ResolvedProtection = sizeInfo.Ei;

                if (string.IsNullOrWhiteSpace(
                        candidate.ResolvedProtection))
                {
                    OngGioProtectionTextInfo protection =
                        FindNearestOngGioProtectionText(
                            candidate,
                            protectionTexts);
                    if (protection != null)
                    {
                        candidate.ResolvedProtection =
                            protection.Text;
                    }
                }

                candidate.ResolvedOgType = defaultOgType;
                OngGioTypeTextInfo typeInfo =
                    FindNearestOngGioTypeText(
                        candidate,
                        typeTexts);
                if (typeInfo != null &&
                    !string.IsNullOrWhiteSpace(typeInfo.Type))
                {
                    candidate.ResolvedOgType = typeInfo.Type;
                }
            }
        }

        private static void PropagateConnectedStraightMetadataToArcs(
            List<OngGioCenterlineCandidate> candidates)
        {
            if (candidates == null || candidates.Count < 2)
                return;

            foreach (OngGioCenterlineCandidate arc in candidates)
            {
                if (arc == null || !arc.IsArc || arc.IsReducer)
                    continue;

                double endpointTolerance =
                    Math.Max(
                        100.0,
                        arc.DuctWidth *
                        (string.Equals(
                            arc.Source,
                            "ELBOW-TERMINAL-STRICT",
                            StringComparison.OrdinalIgnoreCase)
                            ? 0.55
                            : 0.25));
                double widthTolerance =
                    Math.Max(30.0, arc.DuctWidth * 0.05);

                List<OngGioCenterlineCandidate> connected =
                    candidates
                        .Where(x =>
                            x != null &&
                            !ReferenceEquals(x, arc) &&
                            !x.IsArc &&
                            !x.IsReducer &&
                            x.ResolvedSizeInfo != null &&
                            GetClosestOngGioEndpointDistance(
                                arc,
                                x) <= endpointTolerance &&
                            Math.Abs(
                                arc.DuctWidth - x.DuctWidth) <=
                                widthTolerance)
                        .ToList();

                if (connected.Count == 0)
                    continue;

                var groups =
                    connected
                        .GroupBy(x =>
                            ((x.ResolvedSizeInfo.Size ?? "") + "|" +
                             (x.ResolvedProtection ?? "") + "|" +
                             (x.ResolvedOgType ?? ""))
                            .ToUpperInvariant())
                        .OrderByDescending(x => x.Count())
                        .ToList();

                if (groups.Count > 1 &&
                    groups[0].Count() == groups[1].Count())
                    continue;

                OngGioCenterlineCandidate source =
                    groups[0]
                        .OrderBy(x =>
                            GetClosestOngGioEndpointDistance(
                                arc,
                                x))
                        .FirstOrDefault();

                if (source == null)
                    continue;

                arc.ResolvedSizeInfo = source.ResolvedSizeInfo;
                arc.ResolvedProtection = source.ResolvedProtection;
                arc.ResolvedOgType = source.ResolvedOgType;
            }
        }

        private static double GetClosestOngGioEndpointDistance(
            OngGioCenterlineCandidate first,
            OngGioCenterlineCandidate second)
        {
            if (first == null || second == null)
                return double.MaxValue;

            return Math.Min(
                Math.Min(
                    first.Start.DistanceTo(second.Start),
                    first.Start.DistanceTo(second.End)),
                Math.Min(
                    first.End.DistanceTo(second.Start),
                    first.End.DistanceTo(second.End)));
        }

        private List<OngGioCenterlineCandidate> MergeConnectedSameLayerCandidates(
            List<OngGioCenterlineCandidate> candidates)
        {
            var remaining =
                (candidates ??
                 new List<OngGioCenterlineCandidate>())
                .Where(x => x != null)
                .ToList();
            var topologyCandidates =
                new List<OngGioCenterlineCandidate>(remaining);
            var result = new List<OngGioCenterlineCandidate>();

            while (remaining.Count > 0)
            {
                OngGioCenterlineCandidate current = remaining[0];
                remaining.RemoveAt(0);

                if (current.IsReducer ||
                    current.ResolvedSizeInfo == null)
                {
                    result.Add(current);
                    continue;
                }

                while (true)
                {
                    int bestIndex = -1;
                    double bestConnectionScore = double.MaxValue;

                    for (int i = 0; i < remaining.Count; i++)
                    {
                        OngGioCenterlineCandidate next = remaining[i];
                        if (!HaveSameResolvedOngGioLayer(
                                current,
                                next))
                            continue;

                        if (!TryGetOngGioEndpointConnectionScore(
                                current,
                                next,
                                topologyCandidates,
                                out double connectionScore))
                            continue;

                        if (connectionScore < bestConnectionScore)
                        {
                            bestConnectionScore = connectionScore;
                            bestIndex = i;
                        }
                    }

                    if (bestIndex < 0)
                        break;

                    OngGioCenterlineCandidate nextCandidate =
                        remaining[bestIndex];
                    if (!TryMergeOngGioCandidatesAtEndpoints(
                            current,
                            nextCandidate,
                            out OngGioCenterlineCandidate merged))
                        break;

                    current = merged;
                    remaining.RemoveAt(bestIndex);
                }

                result.Add(current);
            }

            return result;
        }

        private static bool HaveSameResolvedOngGioLayer(
            OngGioCenterlineCandidate first,
            OngGioCenterlineCandidate second)
        {
            if (first == null || second == null ||
                first.IsReducer || second.IsReducer ||
                first.ResolvedSizeInfo == null ||
                second.ResolvedSizeInfo == null)
                return false;

            if (!string.Equals(
                    first.ResolvedSizeInfo.Size,
                    second.ResolvedSizeInfo.Size,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    first.ResolvedProtection ?? "",
                    second.ResolvedProtection ?? "",
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    first.ResolvedOgType ?? "",
                    second.ResolvedOgType ?? "",
                    StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private static bool TryGetOngGioEndpointConnectionScore(
            OngGioCenterlineCandidate first,
            OngGioCenterlineCandidate second,
            List<OngGioCenterlineCandidate> allCandidates,
            out double score)
        {
            score = double.MaxValue;

            if (first == null || second == null)
                return false;

            bool firstUsesStart;
            bool secondUsesStart;
            Point3d firstPoint;
            Point3d secondPoint;
            double distance =
                GetClosestOngGioEndpointPair(
                    first,
                    second,
                    out firstUsesStart,
                    out secondUsesStart,
                    out firstPoint,
                    out secondPoint);
            double tolerance =
                GetOngGioCandidateMergeTolerance(
                    first,
                    second);

            if (distance > tolerance)
                return false;

            Vector3d firstDirection =
                GetOngGioEndpointPathDirection(
                    first,
                    firstUsesStart);
            Vector3d secondDirection =
                GetOngGioEndpointPathDirection(
                    second,
                    secondUsesStart);

            if (firstDirection.Length < 1e-9 ||
                secondDirection.Length < 1e-9)
                return false;

            bool hasArc = first.IsArc || second.IsArc;
            double angleTolerance = hasArc ? 40.0 : 15.0;
            bool directionsContinue =
                AreParallel(
                    firstDirection,
                    secondDirection,
                    angleTolerance);
            double actualWidth =
                Math.Max(
                    Math.Max(first.DuctWidth, second.DuctWidth),
                    100.0);
            double exactJointTolerance =
                Math.Max(
                    40.0,
                    Math.Min(100.0, actualWidth * 0.08));

            if (!hasArc && directionsContinue)
            {
                double lateralTolerance =
                    Math.Max(
                        40.0,
                        Math.Min(120.0, actualWidth * 0.10));
                double lateralOffset =
                    DistancePointToInfiniteLine(
                        secondPoint,
                        firstPoint,
                        firstDirection.GetNormal());

                if (lateralOffset > lateralTolerance)
                    return false;
            }

            if (!directionsContinue)
            {
                if (distance > exactJointTolerance)
                    return false;

                Point3d joint = MidPoint(firstPoint, secondPoint);
                int jointDegree =
                    CountOngGioCandidatesAtJoint(
                        allCandidates,
                        joint,
                        exactJointTolerance * 1.5);

                if (jointDegree > 2)
                    return false;
            }

            double alignment =
                Math.Abs(
                    firstDirection.GetNormal()
                        .DotProduct(
                            secondDirection.GetNormal()));
            score =
                distance +
                (1.0 - alignment) * actualWidth * 0.25;

            return true;
        }

        private static double GetClosestOngGioEndpointPair(
            OngGioCenterlineCandidate first,
            OngGioCenterlineCandidate second,
            out bool firstUsesStart,
            out bool secondUsesStart,
            out Point3d firstPoint,
            out Point3d secondPoint)
        {
            firstUsesStart = true;
            secondUsesStart = true;
            firstPoint = first.Start;
            secondPoint = second.Start;
            double best = first.Start.DistanceTo(second.Start);

            double distance = first.Start.DistanceTo(second.End);
            if (distance < best)
            {
                best = distance;
                firstUsesStart = true;
                secondUsesStart = false;
                firstPoint = first.Start;
                secondPoint = second.End;
            }

            distance = first.End.DistanceTo(second.Start);
            if (distance < best)
            {
                best = distance;
                firstUsesStart = false;
                secondUsesStart = true;
                firstPoint = first.End;
                secondPoint = second.Start;
            }

            distance = first.End.DistanceTo(second.End);
            if (distance < best)
            {
                best = distance;
                firstUsesStart = false;
                secondUsesStart = false;
                firstPoint = first.End;
                secondPoint = second.End;
            }

            return best;
        }

        private static double GetOngGioCandidateMergeTolerance(
            OngGioCenterlineCandidate first,
            OngGioCenterlineCandidate second)
        {
            double actualWidth =
                Math.Max(
                    Math.Max(
                        first?.DuctWidth ?? 0.0,
                        second?.DuctWidth ?? 0.0),
                    100.0);

            if ((first?.IsArc ?? false) ||
                (second?.IsArc ?? false))
            {
                return Math.Max(150.0, actualWidth * 0.55);
            }

            return Math.Max(100.0, actualWidth * 0.45);
        }

        private static Vector3d GetOngGioEndpointPathDirection(
            OngGioCenterlineCandidate candidate,
            bool useStart)
        {
            List<Point3d> vertices =
                GetOngGioCandidateVertices(candidate);

            if (vertices.Count < 2)
                return Vector3d.XAxis;

            Vector3d direction =
                useStart
                    ? vertices[1] - vertices[0]
                    : vertices[vertices.Count - 2] -
                      vertices[vertices.Count - 1];

            return direction.Length < 1e-9
                ? Vector3d.XAxis
                : direction.GetNormal();
        }

        private static int CountOngGioCandidatesAtJoint(
            List<OngGioCenterlineCandidate> candidates,
            Point3d joint,
            double tolerance)
        {
            if (candidates == null)
                return 0;

            return candidates.Count(x =>
                x != null &&
                !x.IsReducer &&
                (x.Start.DistanceTo(joint) <= tolerance ||
                 x.End.DistanceTo(joint) <= tolerance));
        }

        private static bool TryMergeOngGioCandidatesAtEndpoints(
            OngGioCenterlineCandidate first,
            OngGioCenterlineCandidate second,
            out OngGioCenterlineCandidate merged)
        {
            merged = null;

            List<Point3d> firstVertices =
                GetOngGioCandidateVertices(first);
            List<Point3d> secondVertices =
                GetOngGioCandidateVertices(second);

            if (firstVertices.Count < 2 ||
                secondVertices.Count < 2)
                return false;

            double tolerance =
                GetOngGioCandidateMergeTolerance(
                    first,
                    second);
            double bestDistance = double.MaxValue;
            List<Point3d> bestFirst = null;
            List<Point3d> bestSecond = null;

            for (int reverseFirst = 0; reverseFirst < 2; reverseFirst++)
            {
                for (int reverseSecond = 0; reverseSecond < 2; reverseSecond++)
                {
                    List<Point3d> orientedFirst =
                        new List<Point3d>(firstVertices);
                    List<Point3d> orientedSecond =
                        new List<Point3d>(secondVertices);

                    if (reverseFirst == 1)
                        orientedFirst.Reverse();
                    if (reverseSecond == 1)
                        orientedSecond.Reverse();

                    double distance =
                        orientedFirst[orientedFirst.Count - 1]
                            .DistanceTo(orientedSecond[0]);

                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestFirst = orientedFirst;
                        bestSecond = orientedSecond;
                    }
                }
            }

            if (bestFirst == null ||
                bestSecond == null ||
                bestDistance > tolerance)
                return false;

            Point3d joint =
                MidPoint(
                    bestFirst[bestFirst.Count - 1],
                    bestSecond[0]);
            bestFirst[bestFirst.Count - 1] = joint;
            bestSecond[0] = joint;

            var vertices = new List<Point3d>();
            vertices.AddRange(bestFirst);

            for (int i = 1; i < bestSecond.Count; i++)
            {
                Point3d point = bestSecond[i];
                if (vertices.Count == 0 ||
                    vertices[vertices.Count - 1]
                        .DistanceTo(point) > 1e-6)
                {
                    vertices.Add(point);
                }
            }

            vertices =
                SimplifyOngGioPathVertices(vertices);

            double length =
                GetOngGioPathLength(vertices);
            if (vertices.Count < 2 || length < 100.0)
                return false;

            Vector3d direction =
                vertices[1] - vertices[0];
            if (direction.Length < 1e-9)
                return false;

            double weightedWidth =
                first.DuctWidth * Math.Max(first.Length, 1.0) +
                second.DuctWidth * Math.Max(second.Length, 1.0);
            double totalWeight =
                Math.Max(first.Length, 1.0) +
                Math.Max(second.Length, 1.0);
            bool isArcRun =
                first.IsArc ||
                second.IsArc ||
                vertices.Count > 2;

            merged = new OngGioCenterlineCandidate
            {
                Start = vertices[0],
                End = vertices[vertices.Count - 1],
                Mid = GetPointAtHalfOngGioPath(vertices),
                Direction = direction.GetNormal(),
                Length = length,
                DuctWidth = weightedWidth / totalWeight,
                Source = isArcRun
                    ? "ELBOW-RUN"
                    : "STRAIGHT-RUN",
                IsArc = isArcRun,
                Vertices = vertices,
                ResolvedSizeInfo = first.ResolvedSizeInfo,
                ResolvedProtection = first.ResolvedProtection,
                ResolvedOgType = first.ResolvedOgType
            };

            return true;
        }

        private static List<Point3d> SimplifyOngGioPathVertices(
            List<Point3d> vertices)
        {
            var clean = new List<Point3d>();

            foreach (Point3d point in
                     vertices ?? new List<Point3d>())
            {
                if (clean.Count == 0 ||
                    clean[clean.Count - 1]
                        .DistanceTo(point) > 1e-6)
                {
                    clean.Add(point);
                }
            }

            if (clean.Count < 3)
                return clean;

            var result = new List<Point3d> { clean[0] };

            for (int i = 1; i < clean.Count - 1; i++)
            {
                Point3d previous = result[result.Count - 1];
                Point3d current = clean[i];
                Point3d next = clean[i + 1];
                Vector3d firstDirection = current - previous;
                Vector3d secondDirection = next - current;

                bool isForwardCollinear =
                    firstDirection.Length > 1e-9 &&
                    secondDirection.Length > 1e-9 &&
                    AreParallel(
                        firstDirection,
                        secondDirection,
                        3.0) &&
                    firstDirection.GetNormal()
                        .DotProduct(
                            secondDirection.GetNormal()) > 0.0;

                if (!isForwardCollinear)
                    result.Add(current);
            }

            result.Add(clean[clean.Count - 1]);
            return result;
        }

        private static List<Point3d> GetOngGioCandidateVertices(
            OngGioCenterlineCandidate candidate)
        {
            if (candidate?.Vertices != null &&
                candidate.Vertices.Count >= 2)
            {
                return new List<Point3d>(candidate.Vertices);
            }

            if (candidate == null)
                return new List<Point3d>();

            return new List<Point3d>
            {
                candidate.Start,
                candidate.End
            };
        }

        private static double GetOngGioPathLength(
            List<Point3d> vertices)
        {
            if (vertices == null || vertices.Count < 2)
                return 0.0;

            double length = 0.0;

            for (int i = 0; i < vertices.Count - 1; i++)
            {
                length +=
                    vertices[i].DistanceTo(vertices[i + 1]);
            }

            return length;
        }

        private static Point3d GetPointAtHalfOngGioPath(
            List<Point3d> vertices)
        {
            if (vertices == null || vertices.Count == 0)
                return Point3d.Origin;
            if (vertices.Count == 1)
                return vertices[0];

            double totalLength =
                GetOngGioPathLength(vertices);
            double target = totalLength / 2.0;
            double accumulated = 0.0;

            for (int i = 0; i < vertices.Count - 1; i++)
            {
                Point3d start = vertices[i];
                Point3d end = vertices[i + 1];
                double segmentLength = start.DistanceTo(end);

                if (accumulated + segmentLength >= target &&
                    segmentLength > 1e-9)
                {
                    double ratio =
                        (target - accumulated) /
                        segmentLength;
                    return start + (end - start) * ratio;
                }

                accumulated += segmentLength;
            }

            return vertices[vertices.Count - 1];
        }

        private static bool TryGetOngGioLabelPlacement(
            OngGioCenterlineCandidate candidate,
            out Point3d point,
            out Vector3d direction,
            out double segmentLength)
        {
            point = Point3d.Origin;
            direction = Vector3d.XAxis;
            segmentLength = 0.0;

            if (candidate == null ||
                (candidate.IsArc &&
                 !string.Equals(
                     candidate.Source,
                     "ELBOW-RUN",
                     StringComparison.OrdinalIgnoreCase)))
                return false;

            List<Point3d> vertices =
                GetOngGioCandidateVertices(candidate);

            for (int i = 0; i < vertices.Count - 1; i++)
            {
                Vector3d vector =
                    vertices[i + 1] - vertices[i];
                double length = vector.Length;

                if (length <= segmentLength || length < 1e-9)
                    continue;

                segmentLength = length;
                point = MidPoint(vertices[i], vertices[i + 1]);
                direction = vector.GetNormal();
            }

            return segmentLength > 0.0;
        }

        private int DrawReducerCandidate(
            Transaction tr,
            Database db,
            BlockTableRecord btr,
            OngGioCenterlineCandidate candidate,
            List<OngGioSizeTextInfo> sizeTexts,
            List<OngGioProtectionTextInfo> protectionTexts,
            List<OngGioCenterlineCandidate> allCandidates,
            string ogType,
            string layerPrefix)
        {
            if (candidate == null ||
                sizeTexts == null ||
                sizeTexts.Count == 0)
                return 0;

            Point3d split = candidate.Mid;

            OngGioSizeTextInfo startSize =
                FindBestReducerSizeForWidth(
                    candidate.Start,
                    candidate.StartDuctWidth,
                    sizeTexts);

            OngGioSizeTextInfo endSize =
                FindBestReducerSizeForWidth(
                    candidate.End,
                    candidate.EndDuctWidth,
                    sizeTexts);

            if (startSize == null)
                startSize = FindNearestOngGioSizeText(candidate, sizeTexts);
            if (endSize == null)
                endSize = startSize;

            int count = 0;
            double halfLength = candidate.Length / 2.0;
            Vector3d startDirection =
                FindReducerExtensionDirection(
                    candidate.Start,
                    candidate.Mid,
                    candidate,
                    allCandidates);
            Vector3d endDirection =
                FindReducerExtensionDirection(
                    candidate.End,
                    candidate.Mid,
                    candidate,
                    allCandidates);

            Point3d startEnd =
                candidate.Start + startDirection * halfLength;
            Point3d endEnd =
                candidate.End + endDirection * halfLength;

            count += DrawStraightOngGioPart(
                tr,
                db,
                btr,
                candidate.Start,
                startEnd,
                startDirection,
                startSize,
                FindNearestProtectionTextToPoint(
                    candidate.Start,
                    protectionTexts),
                ogType,
                layerPrefix,
                false);

            count += DrawStraightOngGioPart(
                tr,
                db,
                btr,
                candidate.End,
                endEnd,
                endDirection,
                endSize,
                FindNearestProtectionTextToPoint(
                    candidate.End,
                    protectionTexts),
                ogType,
                layerPrefix,
                false);

            return count;
        }

        private int DrawStraightOngGioPart(
            Transaction tr,
            Database db,
            BlockTableRecord btr,
            Point3d start,
            Point3d end,
            Vector3d direction,
            OngGioSizeTextInfo sizeInfo,
            OngGioProtectionTextInfo protectionInfo,
            string ogType,
            string layerPrefix,
            bool showLabel)
        {
            if (sizeInfo == null)
                return 0;

            Vector3d v = end - start;
            if (v.Length < 100.0)
                return 0;

            string size = sizeInfo.Size;
            string eiPart = sizeInfo.Ei;

            if (string.IsNullOrWhiteSpace(eiPart) &&
                protectionInfo != null)
            {
                eiPart = protectionInfo.Text;
            }

            double width = sizeInfo.Width;
            if (width <= 0.0)
                width = LayWidthTuSize(size);
            if (width <= 0.0)
                width = 100.0;

            string layerName =
                BuildOngGioLayerName(
                    layerPrefix,
                    ogType,
                    size,
                    eiPart);

            EnsureLayerExists(tr, db, layerName, true);

            Polyline pl = new Polyline();
            pl.SetDatabaseDefaults(db);
            pl.AddVertexAt(
                0,
                new Point2d(start.X, start.Y),
                0, 0, 0);
            pl.AddVertexAt(
                1,
                new Point2d(end.X, end.Y),
                0, 0, 0);
            pl.Layer = layerName;
            pl.ColorIndex = 256;
            pl.ConstantWidth = width;

            btr.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);

            if (showLabel)
            {
                AddOngGioLabel(
                    tr,
                    db,
                    btr,
                    layerName,
                    BuildOngGioDisplayText(size, eiPart),
                    width,
                    MidPoint(start, end),
                    v.GetNormal());
            }

            return 1;
        }

        private OngGioSizeTextInfo FindBestReducerSizeForWidth(
            Point3d point,
            double targetWidth,
            List<OngGioSizeTextInfo> texts)
        {
            if (texts == null || texts.Count == 0)
                return null;

            OngGioSizeTextInfo best = null;
            double bestDist = double.MaxValue;
            double bestGeometryDifference = double.MaxValue;

            foreach (OngGioSizeTextInfo text in texts)
            {
                if (text == null)
                    continue;

                double difference =
                    GetOngGioGeometryDifference(
                        text,
                        targetWidth);
                if (difference < bestGeometryDifference)
                    bestGeometryDifference = difference;
            }

            double geometryTolerance =
                Math.Max(20.0, targetWidth * 0.02);

            foreach (OngGioSizeTextInfo text in texts)
            {
                if (text == null)
                    continue;

                double geometryDifference =
                    GetOngGioGeometryDifference(
                        text,
                        targetWidth);

                if (bestGeometryDifference < double.MaxValue &&
                    geometryDifference >
                        bestGeometryDifference + geometryTolerance)
                    continue;

                double widthPenalty =
                    geometryDifference * 8.0;

                double d =
                    point.DistanceTo(text.Pos) * 0.12 +
                    widthPenalty;

                if (d < bestDist)
                {
                    bestDist = d;
                    best = text;
                }
            }

            return best;
        }

        private OngGioProtectionTextInfo FindNearestProtectionTextToPoint(
            Point3d point,
            List<OngGioProtectionTextInfo> texts)
        {
            if (texts == null || texts.Count == 0)
                return null;

            OngGioProtectionTextInfo best = null;
            double bestDist = double.MaxValue;

            foreach (OngGioProtectionTextInfo text in texts)
            {
                if (text == null)
                    continue;

                double d = point.DistanceTo(text.Pos);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = text;
                }
            }

            return best;
        }

        private Vector3d FindReducerExtensionDirection(
            Point3d reducerEnd,
            Point3d reducerMiddle,
            OngGioCenterlineCandidate reducer,
            List<OngGioCenterlineCandidate> allCandidates)
        {
            Vector3d fallback = reducerMiddle - reducerEnd;
            if (fallback.Length < 1e-9)
                fallback = reducer.Direction;
            fallback = fallback.GetNormal();

            if (allCandidates == null)
                return fallback;

            OngGioCenterlineCandidate best = null;
            double bestScore = double.MaxValue;

            foreach (OngGioCenterlineCandidate candidate in allCandidates)
            {
                if (candidate == null ||
                    ReferenceEquals(candidate, reducer) ||
                    candidate.IsArc ||
                    candidate.IsReducer)
                    continue;

                double t;
                double d =
                    DistancePointToSegment(
                        reducerEnd,
                        candidate.Start,
                        candidate.End,
                        out t);

                if (d > Math.Max(500.0, candidate.DuctWidth * 0.75))
                    continue;

                double score =
                    d +
                    (1.0 -
                     Math.Abs(
                         candidate.Direction.GetNormal()
                             .DotProduct(fallback))) * 100.0;

                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            if (best == null)
                return fallback;

            Vector3d dir = best.Direction.GetNormal();
            if (dir.DotProduct(fallback) < 0.0)
                dir = dir.Negate();

            return dir;
        }

        private Polyline CreatePolylineFromOngGioCandidate(
            OngGioCenterlineCandidate candidate)
        {
            if (candidate == null)
                return null;

            Polyline pl = new Polyline();

            if (candidate.Vertices != null &&
                candidate.Vertices.Count >= 2)
            {
                for (int i = 0; i < candidate.Vertices.Count; i++)
                {
                    Point3d p = candidate.Vertices[i];
                    pl.AddVertexAt(
                        i,
                        new Point2d(p.X, p.Y),
                        0, 0, 0);
                }
            }
            else if (candidate.IsArc)
            {
                double bulge =
                    Math.Tan(candidate.ArcIncludedAngle / 4.0);

                pl.AddVertexAt(
                    0,
                    new Point2d(
                        candidate.Start.X,
                        candidate.Start.Y),
                    bulge,
                    0, 0);
                pl.AddVertexAt(
                    1,
                    new Point2d(
                        candidate.End.X,
                        candidate.End.Y),
                    0,
                    0, 0);
            }
            else
            {
                pl.AddVertexAt(
                    0,
                    new Point2d(
                        candidate.Start.X,
                        candidate.Start.Y),
                    0, 0, 0);
                pl.AddVertexAt(
                    1,
                    new Point2d(
                        candidate.End.X,
                        candidate.End.Y),
                    0, 0, 0);
            }

            return pl;
        }

        private void AddOngGioLabel(
            Transaction tr,
            Database db,
            BlockTableRecord btr,
            string layerName,
            string labelText,
            double width,
            Point3d textPt,
            Vector3d direction)
        {
            try
            {
                DBText label = new DBText();
                label.SetDatabaseDefaults(db);
                label.TextStyleId = db.Textstyle;
                label.TextString = labelText;
                label.Height = Math.Max(
                    MinimumLabelTextHeight,
                    width * 0.15);
                label.WidthFactor = 1.0;
                label.Layer = layerName;
                label.ColorIndex = 256;
                label.Rotation =
                    GetReadableRotation(direction);
                label.Justify = AttachmentPoint.MiddleCenter;
                label.AlignmentPoint = textPt;
                label.Position = textPt;

                btr.AppendEntity(label);
                tr.AddNewlyCreatedDBObject(label, true);

                try { label.AdjustAlignment(db); } catch { }
            }
            catch { }
        }

        private string BuildOngGioLayerName(
            string layerPrefix,
            string ogType,
            string size,
            string protection)
        {
            string sizeForLayer = size;
            if (!string.IsNullOrWhiteSpace(protection))
                sizeForLayer = size + "_" + protection;

            return
                $"{layerPrefix}_{CleanLayerText(ogType)}_{CleanLayerText(sizeForLayer)}";
        }

        private static string BuildOngGioDisplayText(
            string size,
            string protection)
        {
            if (string.IsNullOrWhiteSpace(protection))
                return size;

            return (size + " " + protection).Trim();
        }

        private static Point3d PointAtAngle(
            Point3d center,
            double radius,
            double angle)
        {
            return new Point3d(
                center.X + Math.Cos(angle) * radius,
                center.Y + Math.Sin(angle) * radius,
                center.Z);
        }

        private static double NormalizePositiveAngle(double angle)
        {
            while (angle < 0.0)
                angle += Math.PI * 2.0;
            while (angle >= Math.PI * 2.0)
                angle -= Math.PI * 2.0;
            return angle;
        }

        private static double NormalizeSignedAngle(double angle)
        {
            while (angle <= -Math.PI)
                angle += Math.PI * 2.0;
            while (angle > Math.PI)
                angle -= Math.PI * 2.0;
            return angle;
        }

        private static bool AreParallel(
            Vector3d a,
            Vector3d b,
            double angleToleranceDegrees)
        {
            if (a.Length < 1e-9 || b.Length < 1e-9)
                return false;

            Vector3d na = a.GetNormal();
            Vector3d nb = b.GetNormal();
            double dot = Math.Abs(na.DotProduct(nb));
            double cos =
                Math.Cos(
                    angleToleranceDegrees *
                    Math.PI / 180.0);

            return dot >= cos;
        }

        private static bool AreSimilarLength(
            double a,
            double b,
            double toleranceRatio)
        {
            double max = Math.Max(Math.Abs(a), Math.Abs(b));
            if (max < 1e-9)
                return false;

            return Math.Abs(a - b) / max <= toleranceRatio;
        }

        private static double DistancePointToInfiniteLine(
            Point3d p,
            Point3d linePoint,
            Vector3d unitDirection)
        {
            Vector3d v = p - linePoint;
            Vector3d perpendicular =
                v - unitDirection * v.DotProduct(unitDirection);

            return perpendicular.Length;
        }

        private static double DistancePointToSegment(
            Point3d p,
            Point3d a,
            Point3d b,
            out double t)
        {
            Vector3d ab = b - a;
            double len2 = ab.DotProduct(ab);

            if (len2 < 1e-9)
            {
                t = 0.0;
                return p.DistanceTo(a);
            }

            t = ((p - a).DotProduct(ab)) / len2;
            double tc = Math.Max(0.0, Math.Min(1.0, t));
            Point3d closest = a + ab * tc;

            return p.DistanceTo(closest);
        }

        private static Point3d ProjectPointToSegment(
            Point3d p,
            Point3d a,
            Point3d b)
        {
            double t;
            DistancePointToSegment(p, a, b, out t);
            t = Math.Max(0.0, Math.Min(1.0, t));
            Vector3d ab = b - a;
            return a + ab * t;
        }

        private static void RefreshOngGioCandidateGeometry(
            OngGioCenterlineCandidate candidate)
        {
            if (candidate == null)
                return;

            Vector3d v = candidate.End - candidate.Start;
            double len = v.Length;

            if (len < 1e-9)
                return;

            candidate.Length = len;
            candidate.Direction = v.GetNormal();
            candidate.Mid =
                MidPoint(candidate.Start, candidate.End);
        }

        private static Point3d MidPoint(
            Point3d a,
            Point3d b)
        {
            return new Point3d(
                (a.X + b.X) / 2.0,
                (a.Y + b.Y) / 2.0,
                (a.Z + b.Z) / 2.0);
        }

        private static double DotPoint(
            Point3d p,
            Vector3d direction)
        {
            return p.X * direction.X +
                   p.Y * direction.Y +
                   p.Z * direction.Z;
        }

        private static void Swap(
            ref double a,
            ref double b)
        {
            double t = a;
            a = b;
            b = t;
        }

        private static double GetReadableRotation(
            Vector3d direction)
        {
            double angle =
                Math.Atan2(direction.Y, direction.X);

            while (angle < 0.0)
                angle += Math.PI * 2.0;
            while (angle >= Math.PI * 2.0)
                angle -= Math.PI * 2.0;

            if (angle > Math.PI / 2.0 &&
                angle <= Math.PI * 3.0 / 2.0)
            {
                angle -= Math.PI;
            }

            return angle;
        }

        private bool TryParseOngGioType(
            string source,
            out string ogType)
        {
            ogType = "";
            if (string.IsNullOrWhiteSpace(source))
                return false;

            string u = BoDauTiengViet(source.Trim());

            // Khớp các loại OG
            string[][] map =
            {
                new[] { "OG HUT KHOI", "OG HÚT KHÓI" },
                new[] { "HUT KHOI", "OG HÚT KHÓI" },
                new[] { "OG THAI", "OG THẢI" },
                new[] { "OG LANH", "OG LẠNH" },
                new[] { "OG CAP", "OG CẤP" },
                new[] { "OG HOI", "OG HỒI" },
                new[] { "ONG GIO THAI", "OG THẢI" },
                new[] { "ONG GIO", "OG THẢI" },
            };

            foreach (var pair in map)
            {
                if (u.Contains(pair[0]))
                {
                    ogType = pair[1];
                    return true;
                }
            }

            // OG đứng riêng + từ sau
            Match m = Regex.Match(
                u,
                @"\bOG\s*(THAI|HUT|LANH|CAP|HOI|KHOI)?");
            if (m.Success)
            {
                string k = m.Groups[1].Value;
                if (k.Contains("HUT") || k.Contains("KHOI"))
                    ogType = "OG HÚT KHÓI";
                else if (k == "LANH")
                    ogType = "OG LẠNH";
                else if (k == "CAP")
                    ogType = "OG CẤP";
                else if (k == "HOI")
                    ogType = "OG HỒI";
                else
                    ogType = "OG THẢI";
                return true;
            }

            return false;
        }

        private void BtnAutoConvertPipe_Click(
            object sender,
            RoutedEventArgs e)
        {
            PipeUiContext ctx = GetContext(sender);

            var doc =
                Autodesk.AutoCAD.ApplicationServices.Core.Application
                    .DocumentManager
                    .MdiActiveDocument;

            if (doc == null)
                return;

            var ed = doc.Editor;
            var db = doc.Database;

            string layerPrefix =
                GetLayerPrefix(ctx);

            bool isOngGio =
                CheckIsOngGio(ctx);

            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();

            PromptSelectionOptions pso =
                new PromptSelectionOptions();

            pso.MessageForAdding =
                "\nQuét chọn toàn bộ khu vực tuyến ống, " +
                "nhánh rẽ, đầu phun và Text kích thước: ";

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
                        "TEXT"),
                    new TypedValue(
                        (int)DxfCode.Start,
                        "MTEXT"),
                    new TypedValue(
                        (int)DxfCode.Start,
                        "INSERT"),
                    new TypedValue(
                        (int)DxfCode.Start,
                        "CIRCLE"),
                    new TypedValue(
                        (int)DxfCode.Start,
                        "ARC"),
                    new TypedValue(
                        (int)DxfCode.Operator,
                        "OR>")
                };

            SelectionFilter filter =
                new SelectionFilter(tvs);

            PromptSelectionResult psr =
                ed.GetSelection(pso, filter);

            if (psr.Status != PromptStatus.OK ||
                psr.Value.Count == 0)
            {
                return;
            }

            using (doc.LockDocument())
            {
                using (Transaction tr =
                    db.TransactionManager.StartTransaction())
                {
                    BlockTableRecord btr =
                        (BlockTableRecord)tr.GetObject(
                            db.CurrentSpaceId,
                            OpenMode.ForWrite);

                    List<Curve> origCurves =
                        new List<Curve>();

                    List<Entity> intersectEntities =
                        new List<Entity>();

                    List<TextData> allTexts =
                        new List<TextData>();

                    List<Point3d> sprinklerCenters =
                        new List<Point3d>();

                    foreach (SelectedObject so in psr.Value)
                    {
                        Entity ent =
                            tr.GetObject(
                                so.ObjectId,
                                OpenMode.ForRead)
                                as Entity;

                        if (ent is Line ||
                            ent is Polyline ||
                            ent is Polyline2d ||
                            ent is Polyline3d)
                        {
                            origCurves.Add(ent as Curve);
                            intersectEntities.Add(ent);
                        }
                        else if (
                            ent is BlockReference blockReference)
                        {
                            intersectEntities.Add(ent);

                            foreach (
                                Point3d center in
                                GetCircularCentersFromBlock(
                                    tr,
                                    blockReference))
                            {
                                AddUniquePoint(
                                    sprinklerCenters,
                                    center,
                                    5.0);
                            }
                        }
                        else if (ent is Circle circle)
                        {
                            intersectEntities.Add(ent);

                            AddUniquePoint(
                                sprinklerCenters,
                                circle.Center,
                                5.0);
                        }
                        else if (ent is Arc)
                        {
                            intersectEntities.Add(ent);
                        }
                        else if (ent is DBText txt)
                        {
                            Point3d pt =
                                (txt.Justify !=
                                    AttachmentPoint.BaseLeft &&
                                 (txt.AlignmentPoint.X != 0 ||
                                  txt.AlignmentPoint.Y != 0))
                                    ? txt.AlignmentPoint
                                    : txt.Position;

                            string str =
                                txt.TextString
                                    .Replace("\r", "")
                                    .Replace("\n", "")
                                    .Trim();

                            if (TryParseAutomaticPipeSize(
                                ctx,
                                str,
                                out string detectedSize,
                                out double detectedWidth))
                            {
                                allTexts.Add(
                                    new TextData
                                    {
                                        Position = pt,
                                        Rotation = txt.Rotation,
                                        TextString =
                                            detectedSize,
                                        LayerName =
                                            $"{layerPrefix}_" +
                                            $"{CleanLayerText(detectedSize)}",
                                        Width =
                                            detectedWidth
                                    });
                            }
                        }
                        else if (ent is MText mtxt)
                        {
                            string str =
                                mtxt.Text
                                    .Replace("\r", "")
                                    .Replace("\n", "")
                                    .Trim();

                            if (TryParseAutomaticPipeSize(
                                ctx,
                                str,
                                out string detectedSize,
                                out double detectedWidth))
                            {
                                allTexts.Add(
                                    new TextData
                                    {
                                        Position =
                                            mtxt.Location,
                                        Rotation =
                                            mtxt.Rotation,
                                        TextString =
                                            detectedSize,
                                        LayerName =
                                            $"{layerPrefix}_" +
                                            $"{CleanLayerText(detectedSize)}",
                                        Width =
                                            detectedWidth
                                    });
                            }
                        }
                    }

                    if (origCurves.Count == 0 ||
                        allTexts.Count == 0)
                    {
                        MessageBox.Show(
                            "Phải quét trúng ít nhất 1 đường ống " +
                            "và 1 Text kích thước!",
                            "Cảnh báo");

                        return;
                    }

                    Dictionary<Curve, List<TextProjectionData>>
                        textProjectionsByCurve =
                            MapTextsToOriginalCurves(
                                origCurves,
                                allTexts);

                    Dictionary<Curve, List<SprinklerProjectionData>>
                        sprinklerProjectionsByCurve =
                            MapSprinklerCentersToCurves(
                                origCurves,
                                sprinklerCenters);

                    int detectedSprinklerCount =
                        sprinklerProjectionsByCurve
                            .Sum(pair => pair.Value.Count);

                    int sizeTransitionCount = 0;

                    List<SegmentData> allSegments =
                        new List<SegmentData>();

                    Plane xyPlane =
                        new Plane(
                            Point3d.Origin,
                            Vector3d.ZAxis);

                    foreach (Curve mainCurve in origCurves)
                    {
                        Point3dCollection splitPoints =
                            new Point3dCollection();

                        foreach (
                            Entity otherEnt
                            in intersectEntities)
                        {
                            if (mainCurve == otherEnt)
                                continue;

                            try
                            {
                                Point3dCollection pts =
                                    new Point3dCollection();

                                mainCurve.IntersectWith(
                                    otherEnt,
                                    Intersect.OnBothOperands,
                                    xyPlane,
                                    pts,
                                    IntPtr.Zero,
                                    IntPtr.Zero);

                                foreach (Point3d p in pts)
                                {
                                    splitPoints.Add(
                                        mainCurve
                                            .GetClosestPointTo(
                                                p,
                                                false));
                                }
                            }
                            catch
                            {
                            }

                            try
                            {
                                List<Point3d> testPoints =
                                    new List<Point3d>();

                                if (otherEnt
                                    is BlockReference blk)
                                {
                                    testPoints.Add(
                                        blk.Position);
                                }
                                else if (otherEnt
                                    is Circle cir)
                                {
                                    testPoints.Add(
                                        cir.Center);
                                }
                                else if (otherEnt
                                    is Curve oc)
                                {
                                    testPoints.Add(
                                        oc.StartPoint);

                                    testPoints.Add(
                                        oc.EndPoint);
                                }

                                foreach (
                                    Point3d pt in testPoints)
                                {
                                    Point3d flatPt =
                                        new Point3d(
                                            pt.X,
                                            pt.Y,
                                            mainCurve
                                                .StartPoint.Z);

                                    Point3d closest =
                                        mainCurve
                                            .GetClosestPointTo(
                                                flatPt,
                                                false);

                                    if (closest.DistanceTo(
                                        flatPt) < 150.0)
                                    {
                                        splitPoints.Add(
                                            closest);
                                    }
                                }
                            }
                            catch
                            {
                            }
                        }

                        if (mainCurve is Polyline pl)
                        {
                            for (int i = 0;
                                i < pl.NumberOfVertices;
                                i++)
                            {
                                splitPoints.Add(
                                    pl.GetPoint3dAt(i));
                            }
                        }

                        if (sprinklerProjectionsByCurve
                            .TryGetValue(
                                mainCurve,
                                out List<SprinklerProjectionData>
                                    curveSprinklers))
                        {
                            foreach (
                                SprinklerProjectionData sprinkler
                                in curveSprinklers)
                            {
                                splitPoints.Add(
                                    sprinkler.PointOnCurve);
                            }
                        }

                        List<double> validDistances =
                            new List<double>();

                        double totalLength =
                            mainCurve
                                .GetDistanceAtParameter(
                                    mainCurve.EndParam);

                        foreach (Point3d pt in splitPoints)
                        {
                            try
                            {
                                double dist =
                                    mainCurve.GetDistAtPoint(
                                        mainCurve
                                            .GetClosestPointTo(
                                                pt,
                                                false));

                                if (dist > 1.0 &&
                                    dist < totalLength - 1.0)
                                {
                                    if (!validDistances.Any(
                                        d => Math.Abs(
                                            d - dist) < 1.0))
                                    {
                                        validDistances.Add(
                                            dist);
                                    }
                                }
                            }
                            catch
                            {
                            }
                        }

                        List<double> textTransitionDistances =
                            GetTextSizeTransitionDistances(
                                mainCurve,
                                textProjectionsByCurve,
                                validDistances);

                        sizeTransitionCount +=
                            textTransitionDistances.Count;

                        foreach (
                            double transitionDistance
                            in textTransitionDistances)
                        {
                            if (transitionDistance > 1.0 &&
                                transitionDistance <
                                    totalLength - 1.0 &&
                                !validDistances.Any(
                                    d => Math.Abs(
                                        d -
                                        transitionDistance) <
                                        1.0))
                            {
                                validDistances.Add(
                                    transitionDistance);
                            }
                        }

                        validDistances.Sort();

                        Point3dCollection finalSplitPts =
                            new Point3dCollection();

                        foreach (double d in validDistances)
                        {
                            finalSplitPts.Add(
                                mainCurve.GetPointAtDist(d));
                        }

                        if (finalSplitPts.Count > 0)
                        {
                            try
                            {
                                DBObjectCollection splits =
                                    mainCurve.GetSplitCurves(
                                        finalSplitPts);

                                foreach (DBObject obj in splits)
                                {
                                    if (obj is Curve c)
                                    {
                                        allSegments.Add(
                                            new SegmentData
                                            {
                                                Curve = c,
                                                OriginalParent =
                                                    mainCurve
                                            });
                                    }
                                }
                            }
                            catch
                            {
                                allSegments.Add(
                                    new SegmentData
                                    {
                                        Curve =
                                            mainCurve.Clone()
                                                as Curve,
                                        OriginalParent =
                                            mainCurve
                                    });
                            }
                        }
                        else
                        {
                            allSegments.Add(
                                new SegmentData
                                {
                                    Curve =
                                        mainCurve.Clone()
                                            as Curve,
                                    OriginalParent =
                                        mainCurve
                                });
                        }
                    }

                    foreach (
                        KeyValuePair<Curve, List<TextProjectionData>>
                            curveTextPair
                        in textProjectionsByCurve)
                    {
                        foreach (
                            TextProjectionData projection
                            in curveTextPair.Value)
                        {
                            TextData txt = projection.Text;
                            SegmentData bestSeg = null;
                            double minScore = double.MaxValue;

                            foreach (
                                SegmentData seg
                                in allSegments.Where(
                                    item =>
                                        item.OriginalParent ==
                                        curveTextPair.Key))
                            {
                                if (!TryGetClosestPointInPlan(
                                    seg.Curve,
                                    txt.Position,
                                    out Point3d closestPtOnPipe,
                                    out double distToCurve))
                                {
                                    continue;
                                }

                                if (distToCurve > 3500.0)
                                    continue;

                                // Chỉ nhận chữ song song với đoạn ống.
                                if (!IsTextParallelToCurve(
                                    seg.Curve,
                                    closestPtOnPipe,
                                    txt.Rotation))
                                {
                                    continue;
                                }

                                double angleScore =
                                    TinhGocLech(
                                        seg.Curve,
                                        closestPtOnPipe,
                                        txt.Rotation) *
                                    5000.0;

                                double score =
                                    distToCurve +
                                    angleScore;

                                if (score < minScore)
                                {
                                    minScore = score;
                                    bestSeg = seg;
                                }
                            }

                            if (bestSeg != null &&
                                minScore <
                                bestSeg.BestScore)
                            {
                                bestSeg.Layer =
                                    txt.LayerName;

                                bestSeg.Width =
                                    txt.Width;

                                bestSeg.LabelText =
                                    ExtractSizeOnlyText(
                                        txt.TextString);

                                bestSeg.BestScore =
                                    minScore;
                            }
                        }
                    }

                    // Chỉ truyền kích thước cho các đoạn nối tiếp
                    // thẳng hàng. Không truyền qua nhánh vuông góc
                    // vì nhánh chưa có chữ không được lấy DN
                    // của đường ống chính.
                    bool changed;

                    do
                    {
                        changed = false;

                        foreach (
                            var orphan in allSegments.Where(
                                s => s.Layer == null))
                        {
                            foreach (
                                var parent in allSegments.Where(
                                    s => s.Layer != null))
                            {
                                if (IsTouching(
                                        orphan.Curve,
                                        parent.Curve,
                                        out Point3d touchPt) &&
                                    IsCollinear(
                                        orphan.Curve,
                                        parent.Curve,
                                        touchPt))
                                {
                                    orphan.Layer =
                                        parent.Layer;

                                    orphan.Width =
                                        parent.Width;

                                    orphan.LabelText =
                                        parent.LabelText;

                                    changed = true;
                                    break;
                                }
                            }
                        }
                    }
                    while (changed);

                    int sprinklerConnectionCount =
                        CreateSprinklerCenterConnections(
                            tr,
                            db,
                            btr,
                            allSegments,
                            sprinklerProjectionsByCurve,
                            isOngGio);

                    int convertedCount = 0;
                    int generatedSizeTextCount = 0;

                    var groupedByOriginal =
                        allSegments
                            .GroupBy(
                                s => s.OriginalParent)
                            .ToList();

                    foreach (var group in groupedByOriginal)
                    {
                        List<SegmentData> segs =
                            group.ToList();

                        List<Entity> finalEntitiesToAdd =
                            new List<Entity>();

                        bool modified = false;

                        for (int i = 0;
                            i < segs.Count;)
                        {
                            var current = segs[i];

                            if (current.Layer == null)
                            {
                                finalEntitiesToAdd.Add(
                                    current.Curve.Clone()
                                        as Entity);

                                i++;
                            }
                            else
                            {
                                int j = i;

                                List<Curve> mergeList =
                                    new List<Curve>();

                                while (j < segs.Count &&
                                    segs[j].Layer ==
                                    current.Layer)
                                {
                                    mergeList.Add(
                                        segs[j].Curve);

                                    j++;
                                }

                                EnsureLayerExists(
                                    tr,
                                    db,
                                    current.Layer,
                                    isOngGio);

                                Polyline mergedPline =
                                    new Polyline();

                                mergedPline
                                    .SetDatabaseDefaults();

                                mergedPline.Layer =
                                    current.Layer;

                                mergedPline.Color =
                                    Autodesk.AutoCAD.Colors.Color
                                        .FromColorIndex(
                                            ColorMethod.ByLayer,
                                            256);

                                mergedPline.ConstantWidth =
                                    current.Width;

                                List<Point3d> allPts =
                                    new List<Point3d>();

                                foreach (
                                    var c in mergeList)
                                {
                                    if (c is Line l)
                                    {
                                        allPts.Add(
                                            l.StartPoint);

                                        allPts.Add(
                                            l.EndPoint);
                                    }
                                    else if (
                                        c is Polyline p &&
                                        p.NumberOfVertices > 0)
                                    {
                                        allPts.Add(
                                            p.GetPoint3dAt(0));

                                        allPts.Add(
                                            p.GetPoint3dAt(
                                                p.NumberOfVertices -
                                                1));
                                    }
                                }

                                List<Point3d> uniquePts =
                                    new List<Point3d>();

                                foreach (var p in allPts)
                                {
                                    if (!uniquePts.Any(
                                        up =>
                                            up.DistanceTo(p) <
                                            1.0))
                                    {
                                        uniquePts.Add(p);
                                    }
                                }

                                if (uniquePts.Count >= 2)
                                {
                                    Point3d pA =
                                        uniquePts[0];

                                    Point3d pB =
                                        uniquePts[1];

                                    double maxD =
                                        pA.DistanceTo(pB);

                                    for (int a = 0;
                                        a < uniquePts.Count;
                                        a++)
                                    {
                                        for (int b = a + 1;
                                            b < uniquePts.Count;
                                            b++)
                                        {
                                            double d =
                                                uniquePts[a]
                                                    .DistanceTo(
                                                        uniquePts[b]);

                                            if (d > maxD)
                                            {
                                                maxD = d;
                                                pA = uniquePts[a];
                                                pB = uniquePts[b];
                                            }
                                        }
                                    }

                                    double sumLen =
                                        mergeList.Sum(
                                            c =>
                                                c.GetDistanceAtParameter(
                                                    c.EndParam));

                                    if (Math.Abs(
                                        sumLen - maxD) < 5.0)
                                    {
                                        mergedPline.AddVertexAt(
                                            0,
                                            new Point2d(
                                                pA.X,
                                                pA.Y),
                                            0,
                                            current.Width,
                                            current.Width);

                                        mergedPline.AddVertexAt(
                                            1,
                                            new Point2d(
                                                pB.X,
                                                pB.Y),
                                            0,
                                            current.Width,
                                            current.Width);
                                    }
                                    else
                                    {
                                        int vIdx = 0;

                                        foreach (
                                            var c in mergeList)
                                        {
                                            if (c is Line l)
                                            {
                                                if (vIdx == 0)
                                                {
                                                    mergedPline
                                                        .AddVertexAt(
                                                            vIdx++,
                                                            new Point2d(
                                                                l.StartPoint.X,
                                                                l.StartPoint.Y),
                                                            0,
                                                            current.Width,
                                                            current.Width);
                                                }

                                                mergedPline
                                                    .AddVertexAt(
                                                        vIdx++,
                                                        new Point2d(
                                                            l.EndPoint.X,
                                                            l.EndPoint.Y),
                                                        0,
                                                        current.Width,
                                                        current.Width);
                                            }
                                            else if (
                                                c is Polyline p)
                                            {
                                                for (
                                                    int k = 0;
                                                    k <
                                                    p.NumberOfVertices;
                                                    k++)
                                                {
                                                    Point3d pt =
                                                        p.GetPoint3dAt(
                                                            k);

                                                    mergedPline
                                                        .AddVertexAt(
                                                            vIdx++,
                                                            new Point2d(
                                                                pt.X,
                                                                pt.Y),
                                                            0,
                                                            current.Width,
                                                            current.Width);
                                                }
                                            }
                                        }
                                    }
                                }

                                finalEntitiesToAdd.Add(
                                    mergedPline);

                                string generatedLabelText =
                                    !string.IsNullOrWhiteSpace(
                                        current.LabelText)
                                        ? current.LabelText
                                        : ExtractSizeOnlyText(
                                            current.Layer);

                                DBText generatedSizeLabel =
                                    CreateAutomaticSizeLabel(
                                        db,
                                        mergedPline,
                                        current.Layer,
                                        generatedLabelText,
                                        current.Width);

                                if (generatedSizeLabel != null)
                                {
                                    finalEntitiesToAdd.Add(
                                        generatedSizeLabel);

                                    generatedSizeTextCount++;
                                }

                                modified = true;
                                convertedCount++;
                                i = j;
                            }
                        }

                        if (modified)
                        {
                            group.Key.UpgradeOpen();
                            group.Key.Erase();

                            foreach (
                                Entity newEnt
                                in finalEntitiesToAdd)
                            {
                                if (newEnt == null)
                                    continue;

                                btr.AppendEntity(newEnt);

                                tr.AddNewlyCreatedDBObject(
                                    newEnt,
                                    true);

                                if (newEnt
                                    is DBText generatedText)
                                {
                                    generatedText
                                        .AdjustAlignment(db);
                                }
                            }
                        }
                    }

                    tr.Commit();

                    ed.WriteMessage(
                        $"\n[{AutoConvertBuild}] Hoàn tất: " +
                        $"Đã tạo {convertedCount} đoạn ống, " +
                        $"tách {sizeTransitionCount} vị trí " +
                        $"chuyển kích thước, tạo mới " +
                        $"{generatedSizeTextCount} chữ kích thước, " +
                        $"nhận {detectedSprinklerCount} tâm đầu phun " +
                        $"và nối thêm {sprinklerConnectionCount} " +
                        $"đoạn tới tâm.");
                }
            }
        }


        private void BtnVeShopOngChuaChay_Click(
            object sender,
            RoutedEventArgs e)
        {
            PipeUiContext ctx = GetContext(sender) ?? _ctxFF;

            var doc =
                Autodesk.AutoCAD.ApplicationServices.Core.Application
                    .DocumentManager
                    .MdiActiveDocument;

            if (doc == null)
                return;

            var ed = doc.Editor;
            var db = doc.Database;

            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();

            PromptSelectionOptions pso =
                new PromptSelectionOptions();

            pso.MessageForAdding =
                "\n[VẼ SHOP ỐNG] Quét chọn line/polyline tuyến ống và chữ DN: ";

            TypedValue[] tvs =
                new TypedValue[]
                {
                    new TypedValue((int)DxfCode.Operator, "<OR"),
                    new TypedValue((int)DxfCode.Start, "LINE"),
                    new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                    new TypedValue((int)DxfCode.Start, "POLYLINE"),
                    new TypedValue((int)DxfCode.Start, "ARC"),
                    new TypedValue((int)DxfCode.Start, "TEXT"),
                    new TypedValue((int)DxfCode.Start, "MTEXT"),
                    new TypedValue((int)DxfCode.Start, "INSERT"),
                    new TypedValue((int)DxfCode.Operator, "OR>")
                };

            PromptSelectionResult psr =
                ed.GetSelection(
                    pso,
                    new SelectionFilter(tvs));

            if (psr.Status != PromptStatus.OK ||
                psr.Value == null ||
                psr.Value.Count == 0)
                return;

            using (doc.LockDocument())
            using (Transaction tr =
                db.TransactionManager.StartTransaction())
            {
                BlockTableRecord btr =
                    (BlockTableRecord)tr.GetObject(
                        db.CurrentSpaceId,
                        OpenMode.ForWrite);

                List<Curve> curves =
                    new List<Curve>();
                List<ObjectId> sourceCurveIds =
                    new List<ObjectId>();
                List<TextData> texts =
                    new List<TextData>();
                List<Point3d> sprinklerCenters =
                    new List<Point3d>();

                foreach (SelectedObject so in psr.Value)
                {
                    if (so == null || so.ObjectId.IsNull)
                        continue;

                    Entity ent =
                        tr.GetObject(
                            so.ObjectId,
                            OpenMode.ForRead) as Entity;

                    if (ent == null)
                        continue;

                    if ((ent is Line ||
                         ent is Polyline ||
                         ent is Polyline2d ||
                         ent is Polyline3d ||
                         ent is Arc) &&
                        ent is Curve curveEnt)
                    {
                        curves.Add(curveEnt);
                        sourceCurveIds.Add(so.ObjectId);
                    }
                    else if (ent is DBText dbText)
                    {
                        Point3d pt =
                            (dbText.Justify !=
                                AttachmentPoint.BaseLeft &&
                             (dbText.AlignmentPoint.X != 0 ||
                              dbText.AlignmentPoint.Y != 0))
                                ? dbText.AlignmentPoint
                                : dbText.Position;

                        TryAddShopPipeSizeText(
                            ctx,
                            dbText.TextString,
                            pt,
                            dbText.Rotation,
                            texts);
                    }
                    else if (ent is MText mText)
                    {
                        TryAddShopPipeSizeText(
                            ctx,
                            mText.Text,
                            mText.Location,
                            mText.Rotation,
                            texts);
                    }
                    else if (ent is BlockReference blockRef)
                    {
                        // Lấy tâm hình tròn trong block để nhận biết đầu phun.
                        // Chỉ dùng các tâm này cho đầu tuyến hở, nên không ảnh hưởng
                        // các nút co/tê/giảm bình thường.
                        foreach (Point3d center in
                            GetCircularCentersFromBlock(tr, blockRef))
                        {
                            AddUniquePoint(
                                sprinklerCenters,
                                center,
                                5.0);
                        }

                        if (IsLikelyShopSprinklerBlock(tr, blockRef))
                        {
                            AddUniquePoint(
                                sprinklerCenters,
                                blockRef.Position,
                                5.0);
                        }

                        foreach (ObjectId attId
                            in blockRef.AttributeCollection)
                        {
                            AttributeReference att =
                                tr.GetObject(
                                    attId,
                                    OpenMode.ForRead)
                                    as AttributeReference;

                            if (att == null)
                                continue;

                            TryAddShopPipeSizeText(
                                ctx,
                                att.TextString,
                                att.Position,
                                att.Rotation,
                                texts);
                        }
                    }
                }

                if (curves.Count == 0)
                {
                    MessageBox.Show(
                        "Phải quét trúng ít nhất 1 line/polyline tuyến ống.",
                        "VẼ SHOP ỐNG");
                    return;
                }

                Dictionary<Curve, List<TextProjectionData>>
                    textMap =
                        MapTextsToOriginalCurves(
                            curves,
                            texts);

                List<ShopPipeCandidate> shopPipes =
                    new List<ShopPipeCandidate>();

                foreach (Curve curve in curves)
                {
                    string sizeText = "";
                    double width = 0.0;
                    List<TextProjectionData> projections = null;

                    if (textMap.TryGetValue(
                            curve,
                            out projections) &&
                        projections.Count > 0)
                    {
                        List<ShopPipeCandidate> splitCandidates =
                            CreateShopSplitCandidatesFromTexts(
                                curve,
                                projections);

                        if (splitCandidates.Count > 0)
                        {
                            shopPipes.AddRange(splitCandidates);
                            continue;
                        }

                        TextProjectionData best =
                            projections
                                .OrderBy(x => x.MatchScore)
                                .ThenBy(x => x.DistanceAlongCurve)
                                .First();

                        sizeText = best.Text.TextString;
                        width = best.Text.Width;
                    }

                    if (string.IsNullOrWhiteSpace(sizeText) &&
                        TryParseAutomaticPipeSize(
                            ctx,
                            curve.Layer,
                            out string layerSize,
                            out double layerWidth))
                    {
                        sizeText = layerSize;
                        width = layerWidth;
                    }

                    if (string.IsNullOrWhiteSpace(sizeText) ||
                        width <= 0.0)
                        continue;

                    ShopPipeCandidate candidate =
                        CreateShopPipeCandidate(
                            curve,
                            sizeText,
                            width);

                    if (candidate != null)
                        shopPipes.Add(candidate);
                }

                if (shopPipes.Count == 0)
                {
                    MessageBox.Show(
                        "Không nhận được DN nào. Hãy quét thêm chữ DN gần tuyến ống hoặc layer có DN.",
                        "VẼ SHOP ỐNG");
                    return;
                }

                string shopMaterial =
                    InferShopMaterialFromPipes(
                        shopPipes,
                        GetSelectedPipeMaterialName(ctx));

                string shopLibraryPath =
                    FindShopFittingLibraryPath(
                        shopMaterial,
                        db);

                if (string.IsNullOrWhiteSpace(
                        shopLibraryPath))
                {
                    ed.WriteMessage(
                        "\n[SHOP-LIB] Không tìm thấy file thư viện cho vật liệu [" +
                        shopMaterial +
                        "]. SHOP sẽ không tự chèn block thư viện.");
                }
                else
                {
                    ed.WriteMessage(
                        "\n[SHOP-LIB] Đang dùng đúng file: " +
                        Path.GetFileName(
                            shopLibraryPath));
                }

                // 1) Chèn phụ kiện TRƯỚC → đo nửa chiều dài block tại mỗi nút
                // 2) Vẽ ống 2 nét song song + tâm nét đứt, đầu ống CHẠM mặt phụ kiện
                List<ShopFittingGapInfo> fittingGaps =
                    new List<ShopFittingGapInfo>();

                int reducerCount;
                int elbow90Count;
                int elbow45Count;
                int teeCount;
                int sprinklerElbowCount;

                int fittingCount =
                    DrawSmartShopFittings(
                        tr,
                        db,
                        btr,
                        shopPipes,
                        sprinklerCenters,
                        shopLibraryPath,
                        fittingGaps,
                        out reducerCount,
                        out elbow90Count,
                        out elbow45Count,
                        out teeCount,
                        out sprinklerElbowCount);

                int edgeCount = 0;

                foreach (ShopPipeCandidate pipe in shopPipes)
                {
                    EnsureShopLayerExists(
                        tr,
                        db,
                        pipe.LayerName);

                    edgeCount += DrawShopParallelPipeWithGaps(
                        tr,
                        db,
                        btr,
                        pipe,
                        fittingGaps);
                }

                int erasedSourceCount =
                    EraseShopSourceCurves(
                        tr,
                        sourceCurveIds);

                tr.Commit();

                try
                {
                    ed.Regen();
                }
                catch { }

                ed.WriteMessage(
                    $"\n[{ShopSmartBuild}] Đã vẽ {edgeCount} nét (2 song song + tâm nét đứt) cho {shopPipes.Count} tuyến; " +
                    $"phụ kiện thư viện: {fittingCount} (giảm {reducerCount}, co90 {elbow90Count}, " +
                    $"co lơi 45 {elbow45Count}, tê {teeCount}, co90 đầu phun {sprinklerElbowCount}); " +
                    $"xóa {erasedSourceCount} nét gốc.");

                if (fittingCount == 0 &&
                    !string.IsNullOrWhiteSpace(shopLibraryPath))
                {
                    ed.WriteMessage(
                        "\n[SHOP] Không chèn được block phụ kiện từ thư viện. " +
                        "Kiểm tra tên block trong file (CO90/TE/REDUCER + DN) " +
                        "hoặc gửi danh sách tên block để map lại.");
                }
                else if (string.IsNullOrWhiteSpace(shopLibraryPath))
                {
                    ed.WriteMessage(
                        "\n[SHOP] Chưa tìm thấy file thư viện trong thư mục DLL. " +
                        "Cần: Thu vien STEEL.dwg / Thu vien uPVC.dwg / Thu vien PPR.dwg");
                }
            }
        }

        private int DrawSmartShopFittings(
            Transaction tr,
            Database db,
            BlockTableRecord btr,
            List<ShopPipeCandidate> shopPipes,
            List<Point3d> sprinklerCenters,
            string libraryPath,
            List<ShopFittingGapInfo> fittingGaps,
            out int reducerCount,
            out int elbow90Count,
            out int elbow45Count,
            out int teeCount,
            out int sprinklerElbowCount)
        {
            reducerCount = 0;
            elbow90Count = 0;
            elbow45Count = 0;
            teeCount = 0;
            sprinklerElbowCount = 0;

            if (shopPipes == null || shopPipes.Count == 0)
                return 0;

            List<ShopPipeLeg> legs =
                BuildShopPipeLegs(shopPipes);

            List<ShopJointNode> nodes =
                BuildShopJointNodes(legs);

            int total = 0;

            foreach (ShopJointNode node in nodes)
            {
                if (node == null || node.Arms.Count == 0)
                    continue;

                // Đầu hở gần đầu phun: chèn CO 90 ĐI XUỐNG (không dùng hướng ngang).
                if (node.Arms.Count == 1)
                {
                    if (IsShopSprinklerEnd(
                            node.Point,
                            sprinklerCenters))
                    {
                        if (DrawSmartShopSprinklerDropElbow(
                                tr,
                                db,
                                btr,
                                node.Point,
                                node.Arms[0],
                                libraryPath,
                                fittingGaps) > 0)
                        {
                            sprinklerElbowCount++;
                            elbow90Count++;
                            total++;
                        }
                    }

                    continue;
                }

                if (node.Arms.Count == 2)
                {
                    ShopJointArm a = node.Arms[0];
                    ShopJointArm b = node.Arms[1];

                    double includedAngle =
                        GetShopAngleDegrees(
                            a.Direction,
                            b.Direction);

                    double turnAngle =
                        Math.Abs(180.0 - includedAngle);

                    bool differentSize =
                        Math.Abs(a.Width - b.Width) >= 0.5;

                    if (differentSize &&
                        turnAngle <= ShopStraightAngleToleranceDeg)
                    {
                        if (DrawSmartShopReducer(
                                tr,
                                db,
                                btr,
                                node.Point,
                                a,
                                b,
                                libraryPath,
                                fittingGaps) > 0)
                        {
                            reducerCount++;
                            total++;
                        }

                        continue;
                    }

                    if (differentSize)
                    {
                        // Không tự ép một côn giảm vào nút đang đổi hướng.
                        // Trường hợp này cần block co giảm chuyên dụng trong thư viện.
                        continue;
                    }

                    if (Math.Abs(turnAngle - 90.0) <=
                        ShopElbowAngleToleranceDeg)
                    {
                        if (DrawSmartShopElbow(
                                tr,
                                db,
                                btr,
                                node.Point,
                                a,
                                b,
                                libraryPath,
                                90,
                                fittingGaps) > 0)
                        {
                            elbow90Count++;
                            total++;
                        }

                        continue;
                    }

                    if (Math.Abs(turnAngle - 45.0) <=
                        ShopElbowAngleToleranceDeg)
                    {
                        if (DrawSmartShopElbow(
                                tr,
                                db,
                                btr,
                                node.Point,
                                a,
                                b,
                                libraryPath,
                                45,
                                fittingGaps) > 0)
                        {
                            elbow45Count++;
                            total++;
                        }
                    }

                    continue;
                }

                if (node.Arms.Count == 3)
                {
                    if (DrawSmartShopTee(
                            tr,
                            db,
                            btr,
                            node.Point,
                            node.Arms,
                            libraryPath,
                            fittingGaps) > 0)
                    {
                        teeCount++;
                        total++;
                    }

                    continue;
                }

                // 4 nhánh trở lên thường là giao cắt hoặc nút đặc biệt.
                // Không tự chèn để tránh nhận sai cao độ/giao cắt không nối.
            }

            return total;
        }

        private List<ShopPipeLeg> BuildShopPipeLegs(
            IEnumerable<ShopPipeCandidate> shopPipes)
        {
            List<ShopPipeLeg> result =
                new List<ShopPipeLeg>();

            if (shopPipes == null)
                return result;

            foreach (ShopPipeCandidate pipe in shopPipes)
            {
                if (pipe?.Curve == null)
                    continue;

                Curve curve = pipe.Curve;

                if (curve is Line)
                {
                    AddStraightShopLeg(
                        result,
                        pipe,
                        pipe.Start,
                        pipe.End);
                    continue;
                }

                if (curve is Polyline pl &&
                    pl.NumberOfVertices >= 2)
                {
                    for (int i = 0;
                        i < pl.NumberOfVertices - 1;
                        i++)
                    {
                        if (pl.GetSegmentType(i) !=
                            SegmentType.Line)
                        {
                            continue;
                        }

                        Point3d p1 = pl.GetPoint3dAt(i);
                        Point3d p2 = pl.GetPoint3dAt(i + 1);

                        AddStraightShopLeg(
                            result,
                            pipe,
                            p1,
                            p2);
                    }

                    if (pl.Closed &&
                        pl.NumberOfVertices > 2)
                    {
                        Point3d p1 =
                            pl.GetPoint3dAt(
                                pl.NumberOfVertices - 1);
                        Point3d p2 = pl.GetPoint3dAt(0);

                        AddStraightShopLeg(
                            result,
                            pipe,
                            p1,
                            p2);
                    }

                    continue;
                }

                // Arc/Curve khác: vẫn dùng 2 đầu và tiếp tuyến để nhận nút ở đầu,
                // nhưng không dùng phần giữa để suy đoán giao cắt.
                try
                {
                    Vector3d startDir =
                        GetShopCurveDirection(curve, true);
                    Vector3d endDir =
                        GetShopCurveDirection(curve, false);

                    result.Add(
                        new ShopPipeLeg
                        {
                            Pipe = pipe,
                            Start = curve.StartPoint,
                            End = curve.EndPoint,
                            StartDirectionAway = startDir,
                            EndDirectionAway = -endDir,
                            IsStraight = false
                        });
                }
                catch { }
            }

            return result;
        }

        private static void AddStraightShopLeg(
            List<ShopPipeLeg> result,
            ShopPipeCandidate pipe,
            Point3d start,
            Point3d end)
        {
            if (result == null ||
                pipe == null ||
                start.DistanceTo(end) < 1.0)
            {
                return;
            }

            Vector3d dir =
                new Vector3d(
                    end.X - start.X,
                    end.Y - start.Y,
                    0.0);

            if (dir.Length < 1e-9)
                return;

            dir = dir.GetNormal();

            result.Add(
                new ShopPipeLeg
                {
                    Pipe = pipe,
                    Start = start,
                    End = end,
                    StartDirectionAway = dir,
                    EndDirectionAway = -dir,
                    IsStraight = true
                });
        }

        private List<ShopJointNode> BuildShopJointNodes(
            List<ShopPipeLeg> legs)
        {
            List<ShopJointNode> result =
                new List<ShopJointNode>();

            if (legs == null || legs.Count == 0)
                return result;

            List<Point3d> nodePoints =
                new List<Point3d>();

            foreach (ShopPipeLeg leg in legs)
            {
                AddUniquePoint(
                    nodePoints,
                    leg.Start,
                    ShopDuplicateNodeTolerance);
                AddUniquePoint(
                    nodePoints,
                    leg.End,
                    ShopDuplicateNodeTolerance);
            }

            for (int i = 0; i < legs.Count; i++)
            {
                for (int j = i + 1; j < legs.Count; j++)
                {
                    ShopPipeLeg a = legs[i];
                    ShopPipeLeg b = legs[j];

                    if (a == null || b == null)
                        continue;

                    if (a.IsStraight && b.IsStraight &&
                        TryGetShopSegmentIntersection(
                            a,
                            b,
                            out Point3d crossing))
                    {
                        AddUniquePoint(
                            nodePoints,
                            crossing,
                            ShopDuplicateNodeTolerance);
                    }

                    TryRegisterShopEndpointOnLeg(
                        nodePoints,
                        a.Start,
                        b);
                    TryRegisterShopEndpointOnLeg(
                        nodePoints,
                        a.End,
                        b);
                    TryRegisterShopEndpointOnLeg(
                        nodePoints,
                        b.Start,
                        a);
                    TryRegisterShopEndpointOnLeg(
                        nodePoints,
                        b.End,
                        a);
                }
            }

            foreach (Point3d pt in nodePoints)
            {
                ShopJointNode node =
                    new ShopJointNode
                    {
                        Point = pt
                    };

                foreach (ShopPipeLeg leg in legs)
                {
                    AddShopArmsAtNode(
                        node,
                        leg);
                }

                RemoveDuplicateShopArms(node.Arms);

                if (node.Arms.Count > 0)
                    result.Add(node);
            }

            return result;
        }

        private void TryRegisterShopEndpointOnLeg(
            List<Point3d> nodePoints,
            Point3d endpoint,
            ShopPipeLeg targetLeg)
        {
            if (nodePoints == null ||
                targetLeg == null ||
                !targetLeg.IsStraight)
            {
                return;
            }

            if (!TryProjectShopPointToSegment(
                    endpoint,
                    targetLeg.Start,
                    targetLeg.End,
                    out Point3d projected,
                    out double t,
                    out double distance))
            {
                return;
            }

            if (distance > ShopJointTolerance ||
                t <= 0.02 ||
                t >= 0.98)
            {
                return;
            }

            Point3d joint =
                MidPoint(endpoint, projected);

            AddUniquePoint(
                nodePoints,
                joint,
                ShopDuplicateNodeTolerance);
        }

        private void AddShopArmsAtNode(
            ShopJointNode node,
            ShopPipeLeg leg)
        {
            if (node == null || leg == null)
                return;

            double ds =
                PlanDistance(node.Point, leg.Start);
            double de =
                PlanDistance(node.Point, leg.End);

            bool atStart = ds <= ShopJointTolerance;
            bool atEnd = de <= ShopJointTolerance;

            if (atStart)
            {
                AddShopArm(
                    node.Arms,
                    leg,
                    leg.StartDirectionAway);
            }

            if (atEnd)
            {
                AddShopArm(
                    node.Arms,
                    leg,
                    leg.EndDirectionAway);
            }

            if (atStart || atEnd || !leg.IsStraight)
                return;

            if (TryProjectShopPointToSegment(
                    node.Point,
                    leg.Start,
                    leg.End,
                    out Point3d ignored,
                    out double t,
                    out double distance) &&
                distance <= ShopJointTolerance &&
                t > 0.02 &&
                t < 0.98)
            {
                Vector3d dir =
                    new Vector3d(
                        leg.End.X - leg.Start.X,
                        leg.End.Y - leg.Start.Y,
                        0.0);

                if (dir.Length > 1e-9)
                {
                    dir = dir.GetNormal();
                    AddShopArm(node.Arms, leg, dir);
                    AddShopArm(node.Arms, leg, -dir);
                }
            }
        }

        private static void AddShopArm(
            List<ShopJointArm> arms,
            ShopPipeLeg leg,
            Vector3d direction)
        {
            if (arms == null ||
                leg?.Pipe == null ||
                direction.Length < 1e-9)
            {
                return;
            }

            Vector3d dir =
                new Vector3d(
                    direction.X,
                    direction.Y,
                    0.0);

            if (dir.Length < 1e-9)
                return;

            arms.Add(
                new ShopJointArm
                {
                    Leg = leg,
                    Direction = dir.GetNormal()
                });
        }

        private static void RemoveDuplicateShopArms(
            List<ShopJointArm> arms)
        {
            if (arms == null || arms.Count < 2)
                return;

            for (int i = arms.Count - 1; i >= 0; i--)
            {
                ShopJointArm a = arms[i];

                for (int j = 0; j < i; j++)
                {
                    ShopJointArm b = arms[j];

                    if (!string.Equals(
                            a.SizeText,
                            b.SizeText,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    double angle =
                        GetShopAngleDegrees(
                            a.Direction,
                            b.Direction);

                    if (angle <= 3.0)
                    {
                        arms.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        private static bool TryGetShopSegmentIntersection(
            ShopPipeLeg a,
            ShopPipeLeg b,
            out Point3d point)
        {
            point = Point3d.Origin;

            if (a == null || b == null ||
                !a.IsStraight || !b.IsStraight)
                return false;

            double px = a.Start.X;
            double py = a.Start.Y;
            double rx = a.End.X - a.Start.X;
            double ry = a.End.Y - a.Start.Y;
            double qx = b.Start.X;
            double qy = b.Start.Y;
            double sx = b.End.X - b.Start.X;
            double sy = b.End.Y - b.Start.Y;

            double denom = Cross2D(rx, ry, sx, sy);

            if (Math.Abs(denom) < 1e-9)
                return false;

            double qpx = qx - px;
            double qpy = qy - py;

            double t =
                Cross2D(qpx, qpy, sx, sy) / denom;
            double u =
                Cross2D(qpx, qpy, rx, ry) / denom;

            double lenA =
                Math.Sqrt((rx * rx) + (ry * ry));
            double lenB =
                Math.Sqrt((sx * sx) + (sy * sy));

            double extA =
                lenA > 1e-9
                    ? ShopJointTolerance / lenA
                    : 0.0;
            double extB =
                lenB > 1e-9
                    ? ShopJointTolerance / lenB
                    : 0.0;

            if (t < -extA || t > 1.0 + extA ||
                u < -extB || u > 1.0 + extB)
            {
                return false;
            }

            point =
                new Point3d(
                    px + (t * rx),
                    py + (t * ry),
                    (a.Start.Z + b.Start.Z) / 2.0);

            return true;
        }

        private static bool TryProjectShopPointToSegment(
            Point3d point,
            Point3d start,
            Point3d end,
            out Point3d projected,
            out double t,
            out double distance)
        {
            projected = Point3d.Origin;
            t = 0.0;
            distance = double.MaxValue;

            double vx = end.X - start.X;
            double vy = end.Y - start.Y;
            double len2 = (vx * vx) + (vy * vy);

            if (len2 < 1e-9)
                return false;

            t =
                ((point.X - start.X) * vx +
                 (point.Y - start.Y) * vy) / len2;

            double clamped = t;
            if (clamped < 0.0) clamped = 0.0;
            if (clamped > 1.0) clamped = 1.0;

            projected =
                new Point3d(
                    start.X + (clamped * vx),
                    start.Y + (clamped * vy),
                    (start.Z + end.Z) / 2.0);

            distance =
                PlanDistance(point, projected);

            return true;
        }

        private static double Cross2D(
            double ax,
            double ay,
            double bx,
            double by)
        {
            return (ax * by) - (ay * bx);
        }

        private static double PlanDistance(
            Point3d a,
            Point3d b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        private static double GetShopAngleDegrees(
            Vector3d a,
            Vector3d b)
        {
            Vector3d aa =
                new Vector3d(a.X, a.Y, 0.0);
            Vector3d bb =
                new Vector3d(b.X, b.Y, 0.0);

            if (aa.Length < 1e-9 || bb.Length < 1e-9)
                return 0.0;

            aa = aa.GetNormal();
            bb = bb.GetNormal();

            double dot = aa.DotProduct(bb);
            if (dot < -1.0) dot = -1.0;
            if (dot > 1.0) dot = 1.0;

            return Math.Acos(dot) * 180.0 / Math.PI;
        }

        private static double ShopCross(
            Vector3d a,
            Vector3d b)
        {
            return (a.X * b.Y) - (a.Y * b.X);
        }

        /// <summary>
        /// Co 90 cuối nhánh đầu phun: một đầu theo ống, một đầu HƯỚNG XUỐNG.
        /// Không dùng orientation ngang.
        /// </summary>
        private int DrawSmartShopSprinklerDropElbow(
            Transaction tr,
            Database db,
            BlockTableRecord btr,
            Point3d nodePoint,
            ShopJointArm arm,
            string libraryPath,
            List<ShopFittingGapInfo> fittingGaps)
        {
            if (arm == null)
                return 0;

            string layerName =
                "FF_SHOP_CO90_DAUPHUN_" +
                CleanLayerText(arm.SizeText);

            EnsureShopLayerExists(
                tr,
                db,
                layerName);

            // Direction arm = từ node đầu phun đi ngược vào tuyến ống.
            Vector3d intoPipe =
                new Vector3d(
                    arm.Direction.X,
                    arm.Direction.Y,
                    0.0);

            if (intoPipe.Length < 1e-9)
                return 0;

            intoPipe =
                intoPipe.GetNormal();

            // FIX24:
            // Co tại đầu phun là CO ĐI XUỐNG theo Z, KHÔNG phải co 90
            // quay một chân xuống -Y trên mặt bằng.
            // Chỉ xoay đầu nối ngang của block theo hướng tuyến ống.
            double rotation =
                Math.Atan2(
                    intoPipe.Y,
                    intoPipe.X);

            // Tuyệt đối không mirror Y ở co đầu phun.
            // Hướng xuống sẽ do Visibility/Lookup của Dynamic Block quyết định.
            bool mirrorY = false;

            if (!TryInsertShopLibraryFitting(
                    tr,
                    db,
                    btr,
                    libraryPath,
                    "ELBOW90",
                    arm.SizeText,
                    arm.SizeText,
                    nodePoint,
                    rotation,
                    layerName,
                    mirrorY,
                    fittingGaps))
            {
                return 0;
            }

            return 1;
        }

        /// <summary>
        /// FIX24: đổi trạng thái Dynamic Block trên ĐÚNG reference vừa chèn.
        /// Không tìm "block gần nhất" vì gần đầu phun còn có block sprinkler,
        /// dễ chọn nhầm đối tượng.
        /// </summary>
        private bool TrySetShopFittingVisibilityOnReference(
            BlockReference br,
            bool preferDown,
            bool preferHorizontal)
        {
            if (br == null ||
                br.IsErased ||
                !br.IsDynamicBlock)
            {
                return false;
            }

            try
            {
                DynamicBlockReferencePropertyCollection props =
                    br.DynamicBlockReferencePropertyCollection;

                if (props == null)
                    return false;

                bool changed = false;

                foreach (DynamicBlockReferenceProperty prop in props)
                {
                    if (prop == null ||
                        prop.ReadOnly)
                    {
                        continue;
                    }

                    string pname =
                        NormalizeShopKey(
                            prop.PropertyName ?? "");

                    // Chỉ xét property kiểu Visibility / Lookup / State / Hướng.
                    bool possibleState =
                        pname.Contains("VISIBILITY") ||
                        pname.Contains("VISIBLE") ||
                        pname.Contains("STATE") ||
                        pname.Contains("TRANGTHAI") ||
                        pname.Contains("HUONG") ||
                        pname.Contains("ORIENT") ||
                        pname.Contains("TYPE") ||
                        pname.Contains("KIEU") ||
                        pname.Contains("VIEW") ||
                        pname.Contains("LOOKUP") ||
                        pname.Contains("CHIEU") ||
                        pname.Contains("DIRECTION");

                    if (pname.Contains("FLIP") ||
                        pname.Contains("MIRROR"))
                    {
                        continue;
                    }

                    object[] values = null;

                    try
                    {
                        values =
                            prop.GetAllowedValues();
                    }
                    catch
                    {
                        continue;
                    }

                    if (values == null ||
                        values.Length == 0)
                    {
                        continue;
                    }

                    // Nếu property có các giá trị chuỗi rõ nghĩa thì vẫn xét
                    // ngay cả khi tên property chung chung.
                    bool anyNamedState =
                        values.Any(v =>
                        {
                            string k =
                                NormalizeShopKey(
                                    (v ?? "").ToString());

                            return
                                k.Contains("XUONG") ||
                                k.Contains("DOWN") ||
                                k.Contains("DROP") ||
                                k.Contains("VERT") ||
                                k.Contains("NGANG") ||
                                k.Contains("HORIZ") ||
                                k.Contains("PLAN") ||
                                k.Contains("UP") ||
                                k.Contains("LEN");
                        });

                    if (!possibleState &&
                        !anyNamedState)
                    {
                        continue;
                    }

                    object bestValue = null;
                    int bestScore =
                        int.MinValue;

                    foreach (object value
                        in values)
                    {
                        string raw =
                            (value ?? "")
                                .ToString();

                        string k =
                            NormalizeShopKey(
                                raw);

                        int score = 0;

                        bool isDown =
                            k.Contains("HUONGXUONG") ||
                            k.Contains("XUONG") ||
                            k.Contains("DOWN") ||
                            k.Contains("DROP") ||
                            k.Contains("DIXUONG") ||
                            k.Contains("BOTTOM") ||
                            k.Contains("VERTDOWN");

                        bool isUp =
                            k.Contains("HUONGLEN") ||
                            k.Contains("LEN") ||
                            k.Contains("TOP") ||
                            k == "UP" ||
                            k.Contains("VERTUP");

                        bool isHorizontal =
                            k.Contains("NGANG") ||
                            k.Contains("HORIZ") ||
                            k.Contains("PLAN") ||
                            k.Contains("FLAT") ||
                            k.Contains("SIDE");

                        if (preferDown)
                        {
                            if (isDown)
                                score += 1000;

                            if (isHorizontal)
                                score -= 500;

                            if (isUp)
                                score -= 300;
                        }
                        else if (preferHorizontal)
                        {
                            if (isHorizontal)
                                score += 1000;

                            if (isDown ||
                                isUp)
                            {
                                score -= 500;
                            }
                        }

                        if (score >
                            bestScore)
                        {
                            bestScore = score;
                            bestValue = value;
                        }
                    }

                    if (bestValue != null &&
                        bestScore > 0)
                    {
                        try
                        {
                            prop.Value =
                                bestValue;

                            changed = true;

                            var doc =
                                Autodesk.AutoCAD.ApplicationServices.Core.Application
                                    .DocumentManager
                                    .MdiActiveDocument;

                            doc?.Editor.WriteMessage(
                                $"\n[SHOP FIX24] Dynamic state: " +
                                $"{prop.PropertyName} = {bestValue}");
                        }
                        catch
                        {
                        }
                    }
                }

                if (changed)
                {
                    try
                    {
                        br.RecordGraphicsModified(
                            true);
                    }
                    catch
                    {
                    }
                }

                return changed;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Block động co cuối đầu phun: visibility HƯỚNG XUỐNG.
        /// </summary>
        private void TrySetShopElbowDropVisibility(
            Transaction tr,
            BlockTableRecord btr,
            Point3d nearPoint)
        {
            TrySetShopFittingVisibility(
                tr,
                btr,
                nearPoint,
                preferDown: true,
                preferHorizontal: false);
        }

        /// <summary>
        /// Chọn visibility block động: xuống HOẶC ngang (không để hiện cả hai).
        /// </summary>
        private void TrySetShopFittingVisibility(
            Transaction tr,
            BlockTableRecord btr,
            Point3d nearPoint,
            bool preferDown,
            bool preferHorizontal)
        {
            if (tr == null || btr == null)
                return;

            try
            {
                BlockReference best = null;
                double bestDist = double.MaxValue;

                foreach (ObjectId id in btr)
                {
                    BlockReference br =
                        tr.GetObject(
                            id,
                            OpenMode.ForRead,
                            false) as BlockReference;

                    if (br == null || br.IsErased)
                        continue;

                    double d = br.Position.DistanceTo(nearPoint);
                    if (d < bestDist && d <= 400.0)
                    {
                        bestDist = d;
                        best = br;
                    }
                }

                if (best == null)
                    return;

                best =
                    tr.GetObject(
                        best.ObjectId,
                        OpenMode.ForWrite,
                        false) as BlockReference;

                if (best == null || !best.IsDynamicBlock)
                    return;

                DynamicBlockReferencePropertyCollection props =
                    best.DynamicBlockReferencePropertyCollection;

                if (props == null)
                    return;

                foreach (DynamicBlockReferenceProperty prop in props)
                {
                    if (prop == null || prop.ReadOnly)
                        continue;

                    string pname =
                        (prop.PropertyName ?? "").ToUpperInvariant();

                    bool isVis =
                        pname.Contains("VISIBILITY") ||
                        pname.Contains("VISIBLE") ||
                        pname.Contains("STATE") ||
                        pname.Contains("TRANGTHAI") ||
                        pname.Contains("HUONG") ||
                        pname.Contains("ORIENT") ||
                        pname.Contains("TYPE") ||
                        pname.Contains("KIEU") ||
                        pname.Contains("VIEW") ||
                        pname.Contains("LOOKUP") ||
                        pname.Contains("CHIEU") ||
                        pname.Contains("DIRECTION");

                    // Flip parameter: không dùng cho visibility, bỏ qua
                    if (pname.Contains("FLIP") ||
                        pname.Contains("MIRROR"))
                        continue;

                    if (!isVis)
                        continue;

                    object[] values = null;
                    try { values = prop.GetAllowedValues(); }
                    catch { continue; }

                    if (values == null || values.Length == 0)
                        continue;

                    string scoreBest = null;
                    int scoreBestVal = int.MinValue;

                    foreach (object v in values)
                    {
                        string s = (v ?? "").ToString();
                        string k = NormalizeShopKey(s);
                        int score = 0;

                        bool isDown =
                            k.Contains("XUONG") ||
                            k.Contains("DOWN") ||
                            k.Contains("DROP") ||
                            k.Contains("DIXUONG") ||
                            k.Contains("BOTTOM");

                        bool isUp =
                            k.Contains("LEN") ||
                            k.Contains("TOP") ||
                            (k.Contains("UP") &&
                             !k.Contains("SUP") &&
                             !k.Contains("COUP"));

                        bool isHoriz =
                            k.Contains("NGANG") ||
                            k.Contains("HORIZ") ||
                            k.Contains("FLAT") ||
                            k.Contains("PLAN") ||
                            k.Contains("SIDE") ||
                            k.Contains("LEFT") ||
                            k.Contains("RIGHT") ||
                            k == "1" ||
                            k == "A" ||
                            k.Contains("ELB90") ||
                            k.Contains("CO90");

                        if (preferDown)
                        {
                            if (isDown) score += 100;
                            if (isHoriz) score -= 60;
                            if (isUp) score += 5;
                        }
                        else if (preferHorizontal)
                        {
                            if (isHoriz) score += 100;
                            if (isDown) score -= 100;
                            if (isUp) score -= 50;
                            if (!isDown && !isUp) score += 40;
                        }

                        if (score > scoreBestVal)
                        {
                            scoreBestVal = score;
                            scoreBest = s;
                        }
                    }

                    if (scoreBest != null && scoreBestVal > 0)
                    {
                        try
                        {
                            prop.Value = scoreBest;
                        }
                        catch { }
                    }
                }

                try
                {
                    best.RecordGraphicsModified(true);
                }
                catch { }
            }
            catch { }
        }

        private bool IsShopSprinklerEnd(
            Point3d point,
            List<Point3d> sprinklerCenters)
        {
            if (sprinklerCenters == null ||
                sprinklerCenters.Count == 0)
            {
                return false;
            }

            return sprinklerCenters.Any(
                center =>
                    PlanDistance(point, center) <=
                    ShopSprinklerEndSearchDistance);
        }

        private bool IsLikelyShopSprinklerBlock(
            Transaction tr,
            BlockReference br)
        {
            if (tr == null || br == null)
                return false;

            try
            {
                BlockTableRecord def =
                    tr.GetObject(
                        br.BlockTableRecord,
                        OpenMode.ForRead,
                        false) as BlockTableRecord;

                string key =
                    NormalizeShopKey(def?.Name ?? "");

                return key.Contains("SPRINK") ||
                       key.Contains("SPK") ||
                       key.Contains("DAUPHUN") ||
                       key.Contains("PHUN") ||
                       key.Contains("SPRHEAD");
            }
            catch
            {
                return false;
            }
        }

        private int DrawSmartShopReducer(
            Transaction tr,
            Database db,
            BlockTableRecord btr,
            Point3d nodePoint,
            ShopJointArm a,
            ShopJointArm b,
            string libraryPath,
            List<ShopFittingGapInfo> fittingGaps = null)
        {
            if (a == null || b == null)
                return 0;

            ShopJointArm large =
                a.Width >= b.Width ? a : b;
            ShopJointArm small =
                a.Width >= b.Width ? b : a;

            // Quy ước block reducer: +X đi từ đầu lớn sang đầu nhỏ.
            // Vì Direction của arm hướng từ nút đi ra ống, nên +X phải
            // hướng về phía ống nhỏ. Đây là chỗ sửa lỗi DN50 -> DN40
            // nhưng block cũ lại bị quay DN40 -> DN50.
            Vector3d largeToSmall =
                small.Direction;

            double rotation =
                Math.Atan2(
                    largeToSmall.Y,
                    largeToSmall.X);

            string layerName =
                "FF_SHOP_GIAM_" +
                CleanLayerText(large.SizeText) +
                "-" +
                CleanLayerText(small.SizeText);

            EnsureShopLayerExists(tr, db, layerName);

            if (TryInsertShopLibraryFitting(
                    tr,
                    db,
                    btr,
                    libraryPath,
                    "REDUCER",
                    large.SizeText,
                    small.SizeText,
                    nodePoint,
                    rotation,
                    layerName,
                    false,
                    fittingGaps))
            {
                return 1;
            }

            // KHÔNG vẽ outline giả (hộp/hình lạ). Chỉ dùng block thư viện thật.
            return 0;
        }

        private int DrawSmartShopReducerOutline(
            Transaction tr,
            Database db,
            BlockTableRecord btr,
            Point3d nodePoint,
            ShopJointArm large,
            ShopJointArm small,
            string layerName)
        {
            if (large == null || small == null)
                return 0;

            Vector3d axis = small.Direction;
            if (axis.Length < 1e-9)
                return 0;
            axis = axis.GetNormal();

            Vector3d normal = GetPlanNormal(axis);
            double length =
                Math.Max(
                    150.0,
                    Math.Max(large.Width, small.Width) * 1.5);

            Point3d bigCenter =
                nodePoint - axis * (length / 2.0);
            Point3d smallCenter =
                nodePoint + axis * (length / 2.0);

            Point3d bigLeft =
                bigCenter + normal * (large.Width / 2.0);
            Point3d bigRight =
                bigCenter - normal * (large.Width / 2.0);
            Point3d smallLeft =
                smallCenter + normal * (small.Width / 2.0);
            Point3d smallRight =
                smallCenter - normal * (small.Width / 2.0);

            AppendShopLine(tr, db, btr, bigLeft, bigRight, layerName);
            AppendShopLine(tr, db, btr, smallLeft, smallRight, layerName);
            AppendShopLine(tr, db, btr, bigLeft, smallLeft, layerName);
            AppendShopLine(tr, db, btr, bigRight, smallRight, layerName);
            return 1;
        }

        private int DrawSmartShopElbow(
            Transaction tr,
            Database db,
            BlockTableRecord btr,
            Point3d nodePoint,
            ShopJointArm a,
            ShopJointArm b,
            string libraryPath,
            int angle,
            List<ShopFittingGapInfo> fittingGaps = null)
        {
            if (a == null || b == null)
                return 0;

            string fittingType =
                angle == 45 ? "ELBOW45" : "ELBOW90";

            string layerName =
                angle == 45
                    ? "FF_SHOP_COLOI45_" +
                      CleanLayerText(a.SizeText)
                    : "FF_SHOP_CO90_" +
                      CleanLayerText(a.SizeText);

            EnsureShopLayerExists(tr, db, layerName);

            // Quy ước block co: nhánh chuẩn thứ nhất theo +X,
            // nhánh thứ hai nằm phía +Y. Nếu góc thực tế quay phải thì mirror Y.
            Vector3d first = a.Direction;
            Vector3d second = b.Direction;

            double rotation =
                Math.Atan2(first.Y, first.X);
            bool mirrorY =
                ShopCross(first, second) < 0.0;

            if (TryInsertShopLibraryFitting(
                    tr,
                    db,
                    btr,
                    libraryPath,
                    fittingType,
                    a.SizeText,
                    a.SizeText,
                    nodePoint,
                    rotation,
                    layerName,
                    mirrorY,
                    fittingGaps))
            {
                return 1;
            }

            // KHÔNG vẽ outline giả. Chỉ chèn block co thật từ thư viện.
            return 0;
        }

        private int DrawSmartShopElbowOutline(
            Transaction tr,
            Database db,
            BlockTableRecord btr,
            Point3d nodePoint,
            ShopJointArm a,
            ShopJointArm b,
            string layerName,
            int angle)
        {
            if (a == null || b == null)
                return 0;

            double halfA = Math.Max(a.Width, 10.0) / 2.0;
            double halfB = Math.Max(b.Width, 10.0) / 2.0;
            double legLen =
                Math.Max(
                    Math.Max(halfA, halfB) * 1.8,
                    80.0);

            Vector3d da = a.Direction;
            Vector3d dbDir = b.Direction;
            if (da.Length < 1e-9 || dbDir.Length < 1e-9)
                return 0;

            da = da.GetNormal();
            dbDir = dbDir.GetNormal();

            Vector3d na = GetPlanNormal(da);
            Vector3d nb = GetPlanNormal(dbDir);

            // Hai nhánh từ tâm nút đi ra theo hướng arm
            Point3d aEnd = nodePoint + da * legLen;
            Point3d bEnd = nodePoint + dbDir * legLen;

            // Vẽ 2 nét ngoài cho mỗi nhánh (hình co đơn giản)
            AppendShopLine(tr, db, btr, nodePoint + na * halfA, aEnd + na * halfA, layerName);
            AppendShopLine(tr, db, btr, nodePoint - na * halfA, aEnd - na * halfA, layerName);
            AppendShopLine(tr, db, btr, nodePoint + nb * halfB, bEnd + nb * halfB, layerName);
            AppendShopLine(tr, db, btr, nodePoint - nb * halfB, bEnd - nb * halfB, layerName);

            // Nối cung góc ngoài/trong gần đúng
            try
            {
                Vector3d mid = (da + dbDir);
                if (mid.Length > 1e-9)
                {
                    mid = mid.GetNormal();
                    Vector3d nMid = GetPlanNormal(mid);
                    double outer = Math.Max(halfA, halfB);
                    Point3d outerPt = nodePoint + mid * (legLen * 0.35) + nMid * outer;
                    // bỏ qua chi tiết phức tạp — 4 nét song song đã đủ nhận diện
                }
            }
            catch { }

            return 1;
        }

        private int DrawSmartShopTee(
            Transaction tr,
            Database db,
            BlockTableRecord btr,
            Point3d nodePoint,
            List<ShopJointArm> arms,
            string libraryPath,
            List<ShopFittingGapInfo> fittingGaps = null)
        {
            if (arms == null || arms.Count != 3)
                return 0;

            int mainA = 0;
            int mainB = 1;
            double bestOppositeAngle = -1.0;

            for (int i = 0; i < arms.Count; i++)
            {
                for (int j = i + 1; j < arms.Count; j++)
                {
                    double angle =
                        GetShopAngleDegrees(
                            arms[i].Direction,
                            arms[j].Direction);

                    if (angle > bestOppositeAngle)
                    {
                        bestOppositeAngle = angle;
                        mainA = i;
                        mainB = j;
                    }
                }
            }

            if (bestOppositeAngle < 150.0)
                return 0;

            int branchIndex =
                Enumerable.Range(0, 3)
                    .First(i => i != mainA && i != mainB);

            ShopJointArm main1 = arms[mainA];
            ShopJointArm main2 = arms[mainB];
            ShopJointArm branch = arms[branchIndex];

            // Hai đầu đường chính của Tê phải cùng DN.
            // Nếu khác DN thì không được ép thành Tê thường.
            if (Math.Abs(main1.Width - main2.Width) >= 0.5)
                return 0;

            ShopJointArm main =
                main1.Width >= main2.Width
                    ? main1
                    : main2;

            string mainSize =
                main.SizeText;
            string branchSize =
                branch.SizeText;

            string layerName =
                "FF_SHOP_TE_" +
                CleanLayerText(mainSize) +
                "-" +
                CleanLayerText(branchSize);

            EnsureShopLayerExists(tr, db, layerName);

            // Hướng ống thật trên bản vẽ
            Vector3d mainDir = main1.Direction;
            if (mainDir.Length < 1e-9)
                mainDir = main.Direction;
            mainDir = mainDir.GetNormal();

            Vector3d branchDir = branch.Direction;
            if (branchDir.Length > 1e-9)
                branchDir = branchDir.GetNormal();
            else
                branchDir = new Vector3d(-mainDir.Y, mainDir.X, 0.0);

            // FIX23: rotation truyền xuống là HƯỚNG ĐƯỜNG CHÍNH THỰC TẾ.
            // TryInsert sẽ tự đọc 3 port của block và map chúng vào
            // main / -main / branch. Không giả định block gốc nằm theo X hay Y.
            double rotation =
                Math.Atan2(
                    mainDir.Y,
                    mainDir.X);

            bool mirrorY =
                ShopCross(
                    mainDir,
                    branchDir) < 0.0;

            if (TryInsertShopLibraryFitting(
                    tr,
                    db,
                    btr,
                    libraryPath,
                    "TEE",
                    mainSize,
                    branchSize,
                    nodePoint,
                    rotation,
                    layerName,
                    mirrorY,
                    fittingGaps))
            {
                return 1;
            }

            return 0;
        }

        /// <summary>
        /// Tính xoay/mirror cho TÊ thư viện SCREW/WELD
        /// (trục chính block ≈ Y, nhánh ≈ X trong định nghĩa).
        /// </summary>
        private void ComputeShopTeeTransform(
            Vector3d mainDir,
            Vector3d branchDir,
            out double rotation,
            out bool mirrorY)
        {
            // Block SCREW/WELD-TEE: +Y = đường chính, +X = nhánh.
            // Thử 2 chiều through, chọn chiều làm nhánh (+X) khớp branchDir nhất.
            // Không mirror Y (dễ đảo trục chính).
            mainDir = mainDir.GetNormal();
            branchDir = branchDir.GetNormal();
            mirrorY = false;

            Vector3d through = mainDir;
            double bestScore = double.MinValue;
            double bestRot = 0.0;

            for (int i = 0; i < 2; i++)
            {
                // R*(0,1) = (-sinθ, cosθ) = through
                double theta = Math.Atan2(-through.X, through.Y);
                Vector3d xDir = new Vector3d(
                    Math.Cos(theta),
                    Math.Sin(theta),
                    0.0);
                double score = xDir.DotProduct(branchDir);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestRot = theta;
                }
                through = -through;
            }

            rotation = bestRot;
        }

        private int EraseShopSourceCurves(
            Transaction tr,
            IEnumerable<ObjectId> sourceCurveIds)
        {
            if (tr == null || sourceCurveIds == null)
                return 0;

            int count = 0;

            foreach (ObjectId id in sourceCurveIds.Distinct())
            {
                if (id.IsNull || id.IsErased)
                    continue;

                try
                {
                    Entity ent =
                        tr.GetObject(
                            id,
                            OpenMode.ForWrite,
                            false) as Entity;

                    if (ent == null || ent.IsErased)
                        continue;

                    if (ent is Line ||
                        ent is Polyline ||
                        ent is Polyline2d ||
                        ent is Polyline3d ||
                        ent is Arc)
                    {
                        ent.Erase();
                        count++;
                    }
                }
                catch { }
            }

            return count;
        }

        private void TryAddShopPipeSizeText(
            PipeUiContext ctx,
            string rawText,
            Point3d position,
            double rotation,
            List<TextData> texts)
        {
            if (texts == null)
                return;

            string str =
                (rawText ?? "")
                    .Replace("\r", " ")
                    .Replace("\n", " ")
                    .Trim();

            if (!TryParseAutomaticPipeSize(
                    ctx,
                    str,
                    out string detectedSize,
                    out double detectedWidth))
                return;

            texts.Add(
                new TextData
                {
                    Position = position,
                    Rotation = rotation,
                    TextString = detectedSize,
                    LayerName =
                        "FF_SHOP_" +
                        CleanLayerText(detectedSize),
                    Width = detectedWidth
                });
        }

        private ShopPipeCandidate CreateShopPipeCandidate(
            Curve curve,
            string sizeText,
            double width)
        {
            if (curve == null ||
                string.IsNullOrWhiteSpace(sizeText) ||
                width <= 0.0)
                return null;

            try
            {
                Point3d start = curve.StartPoint;
                Point3d end = curve.EndPoint;

                Vector3d startDir =
                    GetShopCurveDirection(
                        curve,
                        true);
                Vector3d endDir =
                    GetShopCurveDirection(
                        curve,
                        false);

                if (startDir.Length < 1e-9 ||
                    endDir.Length < 1e-9)
                    return null;

                Vector3d startNormal =
                    GetPlanNormal(startDir);
                Vector3d endNormal =
                    GetPlanNormal(endDir);

                double half = width / 2.0;

                string cleanSize =
                    CleanLayerText(sizeText);

                return new ShopPipeCandidate
                {
                    Curve = curve,
                    SizeText = sizeText,
                    Width = width,
                    LayerName = "FF_SHOP_" + cleanSize,
                    Start = start,
                    End = end,
                    StartLeft = start + startNormal * half,
                    StartRight = start - startNormal * half,
                    EndLeft = end + endNormal * half,
                    EndRight = end - endNormal * half
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// True nếu curve chỉ là 1 đoạn thẳng (không gãy góc).
        /// </summary>
        private bool IsShopStraightCurve(Curve curve)
        {
            if (curve == null)
                return false;

            if (curve is Line)
                return true;

            if (curve is Polyline pl)
            {
                if (pl.NumberOfVertices < 2)
                    return false;

                Vector3d? firstDir = null;

                for (int i = 0; i < pl.NumberOfVertices - 1; i++)
                {
                    if (pl.GetSegmentType(i) != SegmentType.Line)
                        return false;

                    Point2d a = pl.GetPoint2dAt(i);
                    Point2d b = pl.GetPoint2dAt(i + 1);
                    Vector3d d =
                        new Vector3d(b.X - a.X, b.Y - a.Y, 0.0);

                    if (d.Length < 1e-9)
                        continue;

                    d = d.GetNormal();

                    if (firstDir == null)
                    {
                        firstDir = d;
                        continue;
                    }

                    double ang =
                        GetShopAngleDegrees(firstDir.Value, d);

                    // Lệch > 5° coi là có góc gãy
                    if (ang > 5.0 && ang < 175.0)
                        return false;
                }

                return firstDir != null;
            }

            return false;
        }

        private List<ShopPipeCandidate>
            CreateShopSplitCandidatesFromTexts(
                Curve curve,
                List<TextProjectionData> projections)
        {
            List<ShopPipeCandidate> result =
                new List<ShopPipeCandidate>();

            if (curve == null ||
                projections == null ||
                projections.Count < 2 ||
                !(curve is Line ||
                  curve is Polyline ||
                  curve is Polyline2d ||
                  curve is Polyline3d))
            {
                return result;
            }

            // QUAN TRỌNG: không được cắt polyline có góc thành Line (d1→d2)
            // vì Line là đường CHÉO (chord), phá góc vuông gốc.
            // Chỉ split khi đường nguồn là 1 đoạn thẳng.
            if (!IsShopStraightCurve(curve))
                return result;

            List<TextProjectionData> ordered =
                projections
                    .Where(x =>
                        x != null &&
                        x.Text != null &&
                        x.Text.Width > 0.0)
                    .OrderBy(x => x.DistanceAlongCurve)
                    .ToList();

            if (ordered.Count < 2)
                return result;

            try
            {
                double totalLength =
                    curve.GetDistanceAtParameter(
                        curve.EndParam);

                if (totalLength < 10.0)
                    return result;

                List<double> boundaries =
                    new List<double>();

                boundaries.Add(0.0);

                for (int i = 0; i < ordered.Count - 1; i++)
                {
                    double middle =
                        (ordered[i].DistanceAlongCurve +
                         ordered[i + 1].DistanceAlongCurve) / 2.0;

                    if (middle > 1.0 &&
                        middle < totalLength - 1.0)
                    {
                        boundaries.Add(middle);
                    }
                }

                boundaries.Add(totalLength);

                if (boundaries.Count != ordered.Count + 1)
                    return result;

                for (int i = 0; i < ordered.Count; i++)
                {
                    double d1 = boundaries[i];
                    double d2 = boundaries[i + 1];

                    if (d2 - d1 < 5.0)
                        continue;

                    Point3d p1 =
                        curve.GetPointAtDist(d1);
                    Point3d p2 =
                        curve.GetPointAtDist(d2);

                    if (p1.DistanceTo(p2) < 5.0)
                        continue;

                    Line segment =
                        new Line(p1, p2);

                    ShopPipeCandidate candidate =
                        CreateShopPipeCandidate(
                            segment,
                            ordered[i].Text.TextString,
                            ordered[i].Text.Width);

                    if (candidate != null)
                        result.Add(candidate);
                }
            }
            catch
            {
                result.Clear();
            }

            return result;
        }

        private Vector3d GetShopCurveDirection(
            Curve curve,
            bool atStart)
        {
            try
            {
                double param =
                    atStart
                        ? curve.StartParam
                        : curve.EndParam;

                Vector3d dir =
                    curve.GetFirstDerivative(param);

                if (dir.Length < 1e-9)
                    return Vector3d.XAxis;

                return new Vector3d(
                    dir.X,
                    dir.Y,
                    0.0).GetNormal();
            }
            catch
            {
                try
                {
                    Vector3d dir =
                        curve.EndPoint - curve.StartPoint;

                    if (dir.Length < 1e-9)
                        return Vector3d.XAxis;

                    return new Vector3d(
                        dir.X,
                        dir.Y,
                        0.0).GetNormal();
                }
                catch
                {
                    return Vector3d.XAxis;
                }
            }
        }

        private static Vector3d GetPlanNormal(Vector3d direction)
        {
            Vector3d dir =
                new Vector3d(
                    direction.X,
                    direction.Y,
                    0.0);

            if (dir.Length < 1e-9)
                return Vector3d.YAxis;

            dir = dir.GetNormal();
            return new Vector3d(-dir.Y, dir.X, 0.0).GetNormal();
        }

        private static double ComputeShopFittingGap(double pipeWidth)
        {
            // gap ≈ 1/4 bề rộng ống, kẹp trong [min, max]
            // đủ chỗ cho mặt bích block, không dư khúc dài.
            double g =
                Math.Max(
                    ShopFittingGapMin,
                    pipeWidth * ShopFittingGapRatio);

            if (g > ShopFittingGapMax)
                g = ShopFittingGapMax;

            return g;
        }

        private HashSet<string> BuildShopConnectedEndKeys(
            List<ShopJointNode> nodes)
        {
            HashSet<string> keys =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            if (nodes == null)
                return keys;

            foreach (ShopJointNode node in nodes)
            {
                if (node == null ||
                    node.Arms == null ||
                    node.Arms.Count < 2)
                {
                    continue;
                }

                // CHỈ chừa khe khi thật sự sẽ có phụ kiện.
                // Đoạn thẳng cùng DN (2 nhánh thẳng hàng) KHÔNG cắt.
                if (!ShopNodeNeedsFitting(node))
                    continue;

                keys.Add(ShopPointKey(node.Point));
            }

            return keys;
        }

        /// <summary>
        /// Nút cần phụ kiện (co/tê/giảm) — mới được rút ngắn ống.
        /// </summary>
        private bool ShopNodeNeedsFitting(ShopJointNode node)
        {
            if (node == null || node.Arms == null)
                return false;

            int n = node.Arms.Count;

            // Tê / chữ thập
            if (n >= 3)
                return true;

            if (n != 2)
                return false;

            ShopJointArm a = node.Arms[0];
            ShopJointArm b = node.Arms[1];
            if (a == null || b == null)
                return false;

            double included =
                GetShopAngleDegrees(a.Direction, b.Direction);
            double turn =
                Math.Abs(180.0 - included);

            // Cùng hướng (thẳng): chỉ cần giảm nếu khác DN
            if (turn <= ShopStraightAngleToleranceDeg)
            {
                string sa = CleanLayerText(a.SizeText ?? "");
                string sb = CleanLayerText(b.SizeText ?? "");
                if (string.IsNullOrWhiteSpace(sa) ||
                    string.IsNullOrWhiteSpace(sb))
                    return false;

                // Cùng size → nối thẳng, không cắt ống
                if (string.Equals(
                        sa,
                        sb,
                        StringComparison.OrdinalIgnoreCase))
                    return false;

                // Khác size → có giảm
                return true;
            }

            // Co 45 / co 90
            if (Math.Abs(turn - 90.0) <= ShopElbowAngleToleranceDeg)
                return true;

            if (Math.Abs(turn - 45.0) <= ShopElbowAngleToleranceDeg)
                return true;

            return false;
        }

        private static string ShopPointKey(Point3d p)
        {
            // Làm tròn theo tolerance để so khớp điểm nút
            double t = ShopDuplicateNodeTolerance;
            long x = (long)Math.Round(p.X / t);
            long y = (long)Math.Round(p.Y / t);
            return x.ToString() + ":" + y.ToString();
        }

        private bool IsShopConnectedEnd(
            Point3d point,
            List<ShopFittingGapInfo> fittingGaps)
        {
            return TryFindShopFittingGap(
                point,
                fittingGaps,
                out _,
                out double half) && half > 1.0;
        }

        private double GetShopGapAtPoint(
            Point3d point,
            List<ShopFittingGapInfo> fittingGaps,
            double pipeWidth)
        {
            if (TryFindShopFittingGap(
                    point,
                    fittingGaps,
                    out _,
                    out double half))
            {
                return half;
            }
            return 0;
        }

        /// <summary>
        /// Tìm phụ kiện gần đầu ống (trong bán kính half + tol).
        /// </summary>
        private bool TryFindShopFittingGap(
            Point3d pipeEnd,
            List<ShopFittingGapInfo> fittingGaps,
            out Point3d joint,
            out double half)
        {
            joint = Point3d.Origin;
            half = 0;

            if (fittingGaps == null || fittingGaps.Count == 0)
                return false;

            double bestDist = double.MaxValue;
            ShopFittingGapInfo best = null;

            foreach (ShopFittingGapInfo g in fittingGaps)
            {
                if (g == null || g.HalfLength <= 1.0)
                    continue;

                double d = pipeEnd.DistanceTo(g.Joint);
                // Cho phép đầu ống lệch khỏi nút tới half + 150
                double maxReach =
                    g.HalfLength + Math.Max(150.0, ShopJointTolerance);

                if (d <= maxReach && d < bestDist)
                {
                    bestDist = d;
                    best = g;
                }
            }

            if (best == null)
                return false;

            joint = best.Joint;
            half = best.HalfLength;
            return true;
        }

        private void RecordShopFittingGap(
            List<ShopFittingGapInfo> fittingGaps,
            Point3d joint,
            double half)
        {
            // GIỮ NGUYÊN hành vi cũ cho REDUCER:
            // không hướng, không DN => áp cho hai đầu giảm như trước.
            RecordShopFittingGapDirectional(
                fittingGaps,
                joint,
                half,
                new Vector3d(0.0, 0.0, 0.0),
                "");
        }

        private void RecordShopFittingGapDirectional(
            List<ShopFittingGapInfo> fittingGaps,
            Point3d joint,
            double half,
            Vector3d direction,
            string sizeText)
        {
            if (fittingGaps == null || half <= 1.0)
                return;

            Vector3d dir =
                new Vector3d(
                    direction.X,
                    direction.Y,
                    0.0);

            if (dir.Length > 1e-9)
                dir = dir.GetNormal();

            string sizeKey =
                CleanLayerText(sizeText ?? "");

            for (int i = 0; i < fittingGaps.Count; i++)
            {
                ShopFittingGapInfo oldGap =
                    fittingGaps[i];

                if (oldGap == null ||
                    oldGap.Joint.DistanceTo(joint) >
                        ShopDuplicateNodeTolerance)
                {
                    continue;
                }

                // Gap legacy của REDUCER: chỉ gộp với legacy khác.
                bool oldDirectional =
                    oldGap.Direction.Length > 1e-9 ||
                    !string.IsNullOrWhiteSpace(
                        oldGap.SizeText);

                bool newDirectional =
                    dir.Length > 1e-9 ||
                    !string.IsNullOrWhiteSpace(
                        sizeKey);

                if (oldDirectional != newDirectional)
                    continue;

                if (!newDirectional)
                {
                    if (half > oldGap.HalfLength)
                        oldGap.HalfLength = half;
                    return;
                }

                string oldSize =
                    CleanLayerText(
                        oldGap.SizeText ?? "");

                if (!string.Equals(
                        oldSize,
                        sizeKey,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (oldGap.Direction.Length < 1e-9 ||
                    dir.Length < 1e-9)
                {
                    continue;
                }

                double angle =
                    GetShopAngleDegrees(
                        oldGap.Direction,
                        dir);

                // Cùng chân/cùng hướng mới gộp.
                if (angle > 8.0)
                    continue;

                if (half > oldGap.HalfLength)
                    oldGap.HalfLength = half;

                return;
            }

            fittingGaps.Add(
                new ShopFittingGapInfo
                {
                    Joint = joint,
                    HalfLength = half,
                    Direction = dir,
                    SizeText = sizeText ?? ""
                });
        }

        private bool ShopFittingGapAppliesToPipe(
            ShopFittingGapInfo gap,
            ShopPipeCandidate pipe,
            Vector3d pipeDirection)
        {
            if (gap == null ||
                pipe == null ||
                gap.HalfLength <= 1.0)
            {
                return false;
            }

            // Legacy gap = reducer cũ => GIỮ NGUYÊN, áp như code đang chạy tốt.
            bool directional =
                gap.Direction.Length > 1e-9 ||
                !string.IsNullOrWhiteSpace(
                    gap.SizeText);

            if (!directional)
                return true;

            string gapSize =
                CleanLayerText(
                    gap.SizeText ?? "");
            string pipeSize =
                CleanLayerText(
                    pipe.SizeText ?? "");

            if (!string.IsNullOrWhiteSpace(gapSize) &&
                !string.IsNullOrWhiteSpace(pipeSize) &&
                !string.Equals(
                    gapSize,
                    pipeSize,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            Vector3d pd =
                new Vector3d(
                    pipeDirection.X,
                    pipeDirection.Y,
                    0.0);

            if (gap.Direction.Length < 1e-9 ||
                pd.Length < 1e-9)
            {
                return true;
            }

            pd = pd.GetNormal();

            double angle =
                GetShopAngleDegrees(
                    gap.Direction,
                    pd);

            // Pipe có thể đang duyệt theo chiều ngược lại:
            // so theo TRỤC, không bắt dấu vector.
            double axisError =
                Math.Min(
                    angle,
                    Math.Abs(180.0 - angle));

            return axisError <=
                Math.Max(
                    10.0,
                    ShopStraightAngleToleranceDeg);
        }

        private int DrawShopParallelPipeWithGaps(
            Transaction tr,
            Database db,
            BlockTableRecord btr,
            ShopPipeCandidate pipe,
            List<ShopFittingGapInfo> fittingGaps)
        {
            if (pipe == null || pipe.Curve == null)
                return 0;

            // Line thẳng: rút khe tại đầu VÀ tại phụ kiện nằm giữa đoạn (tê)
            if (pipe.Curve is Line)
            {
                Point3d start = pipe.Start;
                Point3d end = pipe.End;
                Vector3d dir = end - start;
                double len = dir.Length;
                if (len < 1e-6)
                    return 0;
                dir = dir.GetNormal();

                // Danh sách mốc dọc đoạn: 0, joints, len
                List<Tuple<double, double>> cuts =
                    new List<Tuple<double, double>>();
                // Tuple: distanceAlong, halfGap (0 = endpoint no gap)

                cuts.Add(Tuple.Create(0.0, 0.0));

                if (fittingGaps != null)
                {
                    double reach =
                        Math.Max(
                            ShopJointTolerance,
                            Math.Max(pipe.Width * 3.0, 150.0));

                    foreach (ShopFittingGapInfo g in fittingGaps)
                    {
                        if (!ShopFittingGapAppliesToPipe(
                                g,
                                pipe,
                                dir))
                        {
                            continue;
                        }

                        Vector3d toJ = g.Joint - start;
                        double along = toJ.DotProduct(dir);

                        // Cho phép joint hơi ngoài 2 đầu đoạn
                        if (along < -reach || along > len + reach)
                            continue;

                        along = Math.Max(0.0, Math.Min(len, along));
                        Point3d proj = start + dir * along;

                        // Khoảng cách vuông góc tới tâm ống
                        if (PlanDistance(proj, g.Joint) > reach)
                            continue;

                        // Dùng đúng half đã đo — không phóng to (gây hở)
                        cuts.Add(Tuple.Create(along, g.HalfLength));
                    }
                }

                cuts.Add(Tuple.Create(len, 0.0));
                cuts = cuts
                    .OrderBy(c => c.Item1)
                    .ToList();

                // Gộp mốc trùng
                List<Tuple<double, double>> merged =
                    new List<Tuple<double, double>>();
                foreach (var c in cuts)
                {
                    if (merged.Count == 0)
                    {
                        merged.Add(c);
                        continue;
                    }
                    var last = merged[merged.Count - 1];
                    if (Math.Abs(c.Item1 - last.Item1) <= 1.0)
                    {
                        // Giữ half lớn hơn
                        if (c.Item2 > last.Item2)
                            merged[merged.Count - 1] =
                                Tuple.Create(last.Item1, c.Item2);
                    }
                    else
                        merged.Add(c);
                }

                Vector3d normal = GetPlanNormal(dir);
                double half = pipe.Width / 2.0;
                int drawn = 0;

                for (int i = 0; i < merged.Count - 1; i++)
                {
                    double d0 = merged[i].Item1;
                    double gap0 = merged[i].Item2;
                    double d1 = merged[i + 1].Item1;
                    double gap1 = merged[i + 1].Item2;

                    // Rút half tại mỗi đầu mốc (phụ kiện)
                    double a = d0 + gap0;
                    double b = d1 - gap1;
                    if (b - a < 5.0)
                        continue;

                    Point3d p0 = start + dir * a;
                    Point3d p1 = start + dir * b;

                    AppendShopLine(
                        tr, db, btr,
                        p0 + normal * half,
                        p1 + normal * half,
                        pipe.LayerName);
                    AppendShopLine(
                        tr, db, btr,
                        p0 - normal * half,
                        p1 - normal * half,
                        pipe.LayerName);
                    AppendShopCenterline(
                        tr, db, btr, p0, p1, pipe.LayerName);
                    drawn += 3;
                }

                return drawn;
            }

            // Polyline: vẽ từng đoạn + rút khe tại nút phụ kiện
            return DrawShopParallelPipe(
                tr, db, btr, pipe, fittingGaps);
        }

        private int DrawShopParallelPipe(
            Transaction tr,
            Database db,
            BlockTableRecord btr,
            ShopPipeCandidate pipe,
            List<ShopFittingGapInfo> fittingGaps = null)
        {
            if (pipe == null || pipe.Curve == null)
                return 0;

            int count = 0;
            double half = pipe.Width / 2.0;

            count += DrawShopManualParallelCurves(
                tr,
                db,
                btr,
                pipe.Curve,
                pipe,
                half,
                pipe.LayerName,
                fittingGaps);

            if (count > 0)
            {
                count += DrawShopCenterlineForCurve(
                    tr, db, btr, pipe);
                return count;
            }

            // Chỉ fallback Start→End khi nguồn là 1 đoạn thẳng.
            // Polyline gãy góc: CẤM vẽ chéo từ đầu tới cuối.
            if (!IsShopStraightCurve(pipe.Curve))
                return 0;

            AppendShopLine(
                tr,
                db,
                btr,
                pipe.StartLeft,
                pipe.EndLeft,
                pipe.LayerName);

            AppendShopLine(
                tr,
                db,
                btr,
                pipe.StartRight,
                pipe.EndRight,
                pipe.LayerName);

            AppendShopCenterline(
                tr, db, btr,
                pipe.Start, pipe.End,
                pipe.LayerName);

            return 3;
        }

        private int DrawShopManualParallelCurves(
            Transaction tr,
            Database db,
            BlockTableRecord btr,
            Curve curve,
            ShopPipeCandidate pipe,
            double half,
            string layerName,
            List<ShopFittingGapInfo> fittingGaps = null)
        {
            if (curve == null || pipe == null)
                return 0;

            if (curve is Arc arc)
            {
                return DrawShopParallelArcPair(
                    tr,
                    db,
                    btr,
                    arc,
                    half,
                    layerName);
            }

            if (curve is Line)
            {
                AppendShopLine(
                    tr,
                    db,
                    btr,
                    pipe.StartLeft,
                    pipe.EndLeft,
                    layerName);

                AppendShopLine(
                    tr,
                    db,
                    btr,
                    pipe.StartRight,
                    pipe.EndRight,
                    layerName);

                return 2;
            }

            if (curve is Polyline pl &&
                pl.NumberOfVertices >= 2)
            {
                int count = 0;

                // Từng đoạn thẳng polyline — góc vuông giữ nguyên.
                // Rút khe CHỈ tại nút có phụ kiện (co/tê/giảm).
                for (int i = 0; i < pl.NumberOfVertices - 1; i++)
                {
                    if (pl.GetSegmentType(i) != SegmentType.Line)
                        continue;

                    Point2d p1 = pl.GetPoint2dAt(i);
                    Point2d p2 = pl.GetPoint2dAt(i + 1);
                    Vector3d dir =
                        new Vector3d(
                            p2.X - p1.X,
                            p2.Y - p1.Y,
                            0.0);

                    if (dir.Length < 1e-9)
                        continue;

                    dir = dir.GetNormal();
                    Vector3d normal = GetPlanNormal(dir);

                    Point3d a =
                        new Point3d(p1.X, p1.Y, curve.StartPoint.Z);
                    Point3d b =
                        new Point3d(p2.X, p2.Y, curve.StartPoint.Z);

                    double segLen = a.DistanceTo(b);
                    if (segLen < 1e-9)
                        continue;

                    // Cắt tại mọi phụ kiện nằm trên đoạn (kể cả tê giữa đoạn)
                    List<Tuple<double, double>> cuts =
                        new List<Tuple<double, double>>();
                    cuts.Add(Tuple.Create(0.0, 0.0));
                    cuts.Add(Tuple.Create(segLen, 0.0));

                    if (fittingGaps != null)
                    {
                        double reach =
                            Math.Max(
                                ShopJointTolerance,
                                Math.Max(half * 6.0, 150.0));

                        foreach (ShopFittingGapInfo g in fittingGaps)
                        {
                            if (!ShopFittingGapAppliesToPipe(
                                    g,
                                    pipe,
                                    dir))
                            {
                                continue;
                            }

                            Vector3d toJ = g.Joint - a;
                            double along = toJ.DotProduct(dir);
                            if (along < -reach || along > segLen + reach)
                                continue;

                            along = Math.Max(0.0, Math.Min(segLen, along));
                            Point3d proj = a + dir * along;
                            if (PlanDistance(proj, g.Joint) > reach)
                                continue;

                            cuts.Add(Tuple.Create(along, g.HalfLength));
                        }
                    }

                    cuts = cuts.OrderBy(c => c.Item1).ToList();
                    List<Tuple<double, double>> merged =
                        new List<Tuple<double, double>>();
                    foreach (var c in cuts)
                    {
                        if (merged.Count == 0)
                        {
                            merged.Add(c);
                            continue;
                        }
                        var last = merged[merged.Count - 1];
                        if (Math.Abs(c.Item1 - last.Item1) <= 1.0)
                        {
                            if (c.Item2 > last.Item2)
                                merged[merged.Count - 1] =
                                    Tuple.Create(last.Item1, c.Item2);
                        }
                        else
                            merged.Add(c);
                    }

                    for (int k = 0; k < merged.Count - 1; k++)
                    {
                        double d0 = merged[k].Item1;
                        double g0 = merged[k].Item2;
                        double d1 = merged[k + 1].Item1;
                        double g1 = merged[k + 1].Item2;
                        double aa = d0 + g0;
                        double bb = d1 - g1;
                        if (bb - aa < 5.0)
                            continue;

                        Point3d p0 = a + dir * aa;
                        Point3d p1pt = a + dir * bb;

                        AppendShopLine(
                            tr, db, btr,
                            p0 + normal * half,
                            p1pt + normal * half,
                            layerName);
                        AppendShopLine(
                            tr, db, btr,
                            p0 - normal * half,
                            p1pt - normal * half,
                            layerName);
                        count += 2;
                    }
                }

                return count;
            }

            return 0;
        }

        private int DrawShopParallelArcPair(
            Transaction tr,
            Database db,
            BlockTableRecord btr,
            Arc arc,
            double half,
            string layerName)
        {
            int count = 0;

            double r1 = arc.Radius + half;
            double r2 = arc.Radius - half;

            if (r1 > 1.0)
            {
                Arc a1 =
                    new Arc(
                        arc.Center,
                        r1,
                        arc.StartAngle,
                        arc.EndAngle);

                a1.SetDatabaseDefaults(db);
                ApplyShopEntityStyle(a1, layerName);
                btr.AppendEntity(a1);
                tr.AddNewlyCreatedDBObject(a1, true);
                count++;
            }

            if (r2 > 1.0)
            {
                Arc a2 =
                    new Arc(
                        arc.Center,
                        r2,
                        arc.StartAngle,
                        arc.EndAngle);

                a2.SetDatabaseDefaults(db);
                ApplyShopEntityStyle(a2, layerName);
                btr.AppendEntity(a2);
                tr.AddNewlyCreatedDBObject(a2, true);
                count++;
            }

            return count;
        }

        private int DrawShopOffsetCurves(
            Transaction tr,
            Database db,
            BlockTableRecord btr,
            Curve curve,
            double offset,
            string layerName)
        {
            int count = 0;
            DBObjectCollection offsets = null;

            try
            {
                offsets = curve.GetOffsetCurves(offset);

                foreach (DBObject obj in offsets)
                {
                    if (obj is Entity ent)
                    {
                        ent.SetDatabaseDefaults(db);
                        ApplyShopEntityStyle(ent, layerName);

                        btr.AppendEntity(ent);
                        tr.AddNewlyCreatedDBObject(ent, true);
                        count++;
                    }
                    else
                    {
                        obj.Dispose();
                    }
                }
            }
            catch
            {
                if (offsets != null)
                {
                    foreach (DBObject obj in offsets)
                    {
                        try { obj.Dispose(); }
                        catch { }
                    }
                }
            }

            return count;
        }

        private int TryDrawShopReducer(
            Transaction tr,
            Database db,
            BlockTableRecord btr,
            ShopPipeCandidate a,
            ShopPipeCandidate b,
            string libraryPath,
            List<Point3d> reducerJointPoints)
        {
            if (a == null || b == null ||
                Math.Abs(a.Width - b.Width) < 0.5)
                return 0;

            const double jointTolerance = 150.0;

            if (a.End.DistanceTo(b.Start) <= jointTolerance)
            {
                if (!RegisterShopReducerJoint(
                        reducerJointPoints,
                        a.End,
                        b.Start))
                    return 0;

                return DrawShopReducerFromLibraryOrOutline(
                    tr, db, btr,
                    a.EndLeft, a.EndRight,
                    b.StartLeft, b.StartRight,
                    a, b, libraryPath);
            }

            if (a.Start.DistanceTo(b.End) <= jointTolerance)
            {
                if (!RegisterShopReducerJoint(
                        reducerJointPoints,
                        a.Start,
                        b.End))
                    return 0;

                return DrawShopReducerFromLibraryOrOutline(
                    tr, db, btr,
                    a.StartLeft, a.StartRight,
                    b.EndLeft, b.EndRight,
                    a, b, libraryPath);
            }

            if (a.Start.DistanceTo(b.Start) <= jointTolerance)
            {
                if (!RegisterShopReducerJoint(
                        reducerJointPoints,
                        a.Start,
                        b.Start))
                    return 0;

                return DrawShopReducerFromLibraryOrOutline(
                    tr, db, btr,
                    a.StartLeft, a.StartRight,
                    b.StartLeft, b.StartRight,
                    a, b, libraryPath);
            }

            if (a.End.DistanceTo(b.End) <= jointTolerance)
            {
                if (!RegisterShopReducerJoint(
                        reducerJointPoints,
                        a.End,
                        b.End))
                    return 0;

                return DrawShopReducerFromLibraryOrOutline(
                    tr, db, btr,
                    a.EndLeft, a.EndRight,
                    b.EndLeft, b.EndRight,
                    a, b, libraryPath);
            }

            return 0;
        }

        private static bool RegisterShopReducerJoint(
            List<Point3d> reducerJointPoints,
            Point3d a,
            Point3d b)
        {
            if (reducerJointPoints == null)
                return true;

            // Dùng đúng vị trí hình học thay vì key làm tròn 50 mm.
            // Nếu nhiều cặp segment cùng gặp tại một nút, chỉ chèn 1 phụ kiện.
            const double duplicateTolerance = 150.0;

            Point3d mid =
                MidPoint(a, b);

            if (reducerJointPoints.Any(
                    p => p.DistanceTo(mid) <= duplicateTolerance))
            {
                return false;
            }

            reducerJointPoints.Add(mid);
            return true;
        }

        private int DrawShopReducerFromLibraryOrOutline(
            Transaction tr,
            Database db,
            BlockTableRecord btr,
            Point3d aLeft,
            Point3d aRight,
            Point3d bLeft,
            Point3d bRight,
            ShopPipeCandidate a,
            ShopPipeCandidate b,
            string libraryPath)
        {
            string layerName =
                "FF_SHOP_GIAM_" +
                CleanLayerText(a.SizeText) +
                "-" +
                CleanLayerText(b.SizeText);

            EnsureShopLayerExists(tr, db, layerName);

            Point3d centerA =
                MidPoint(aLeft, aRight);
            Point3d centerB =
                MidPoint(bLeft, bRight);
            Point3d insertPoint =
                MidPoint(centerA, centerB);
            Vector3d direction =
                centerB - centerA;

            double rotation =
                direction.Length > 1e-9
                    ? Math.Atan2(direction.Y, direction.X)
                    : 0.0;

            if (TryInsertShopLibraryFitting(
                    tr,
                    db,
                    btr,
                    libraryPath,
                    "REDUCER",
                    a.SizeText,
                    b.SizeText,
                    insertPoint,
                    rotation,
                    layerName))
                return 1;

            return DrawShopReducerLines(
                tr,
                db,
                btr,
                aLeft,
                aRight,
                bLeft,
                bRight,
                a,
                b);
        }

        private int DrawShopReducerLines(
            Transaction tr,
            Database db,
            BlockTableRecord btr,
            Point3d aLeft,
            Point3d aRight,
            Point3d bLeft,
            Point3d bRight,
            ShopPipeCandidate a,
            ShopPipeCandidate b)
        {
            string layerName =
                "FF_SHOP_GIAM_" +
                CleanLayerText(a.SizeText) +
                "-" +
                CleanLayerText(b.SizeText);

            EnsureShopLayerExists(tr, db, layerName);

            double normalPair =
                aLeft.DistanceTo(bLeft) +
                aRight.DistanceTo(bRight);
            double crossPair =
                aLeft.DistanceTo(bRight) +
                aRight.DistanceTo(bLeft);

            if (crossPair < normalPair)
            {
                AppendShopLine(tr, db, btr, aLeft, aRight, layerName);
                AppendShopLine(tr, db, btr, bLeft, bRight, layerName);
                AppendShopLine(tr, db, btr, aLeft, bRight, layerName);
                AppendShopLine(tr, db, btr, aRight, bLeft, layerName);
            }
            else
            {
                AppendShopLine(tr, db, btr, aLeft, aRight, layerName);
                AppendShopLine(tr, db, btr, bLeft, bRight, layerName);
                AppendShopLine(tr, db, btr, aLeft, bLeft, layerName);
                AppendShopLine(tr, db, btr, aRight, bRight, layerName);
            }

            return 1;
        }

        private int DrawShopCenterlineForCurve(
            Transaction tr,
            Database db,
            BlockTableRecord btr,
            ShopPipeCandidate pipe)
        {
            if (pipe == null || pipe.Curve == null)
                return 0;

            if (pipe.Curve is Line)
            {
                AppendShopCenterline(
                    tr, db, btr,
                    pipe.Start, pipe.End,
                    pipe.LayerName);
                return 1;
            }

            if (pipe.Curve is Polyline pl &&
                pl.NumberOfVertices >= 2)
            {
                int count = 0;
                for (int i = 0; i < pl.NumberOfVertices - 1; i++)
                {
                    if (pl.GetSegmentType(i) != SegmentType.Line)
                        continue;

                    Point3d a = pl.GetPoint3dAt(i);
                    Point3d b = pl.GetPoint3dAt(i + 1);
                    if (a.DistanceTo(b) < 1e-6)
                        continue;

                    AppendShopCenterline(
                        tr, db, btr, a, b, pipe.LayerName);
                    count++;
                }
                return count;
            }

            try
            {
                AppendShopCenterline(
                    tr, db, btr,
                    pipe.Start, pipe.End,
                    pipe.LayerName);
                return 1;
            }
            catch
            {
                return 0;
            }
        }

        private void AppendShopCenterline(
            Transaction tr,
            Database db,
            BlockTableRecord btr,
            Point3d start,
            Point3d end,
            string pipeLayerName)
        {
            if (tr == null || db == null || btr == null)
                return;

            if (start.DistanceTo(end) < 1e-6)
                return;

            string centerLayer =
                "FF_SHOP_TAM_" +
                CleanLayerText(
                    (pipeLayerName ?? "")
                        .Replace("FF_SHOP_", ""));

            EnsureShopLayerExists(tr, db, centerLayer);

            string dashName =
                EnsureShopDashedLinetype(tr, db);

            // Gán linetype nét đứt cho layer tâm
            try
            {
                if (!string.IsNullOrEmpty(dashName))
                {
                    LayerTable lt =
                        (LayerTable)tr.GetObject(
                            db.LayerTableId,
                            OpenMode.ForRead);

                    if (lt.Has(centerLayer))
                    {
                        LayerTableRecord ltr =
                            (LayerTableRecord)tr.GetObject(
                                lt[centerLayer],
                                OpenMode.ForWrite);

                        ltr.LinetypeObjectId =
                            ((LinetypeTable)tr.GetObject(
                                db.LinetypeTableId,
                                OpenMode.ForRead))[dashName];
                    }
                }
            }
            catch { }

            Line line = new Line(start, end);
            line.SetDatabaseDefaults(db);
            line.Layer = centerLayer;
            line.Color =
                Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                    ColorMethod.ByAci,
                    8); // xám — đường tâm
            line.LineWeight = LineWeight.LineWeight000;

            if (!string.IsNullOrEmpty(dashName))
            {
                try { line.Linetype = dashName; }
                catch { }
            }

            // Bản vẽ mm: scale đủ lớn để thấy nét đứt rõ
            double segLen = start.DistanceTo(end);
            double ltScale =
                segLen > 5000 ? 200.0 :
                segLen > 1000 ? 100.0 :
                50.0;

            try { line.LinetypeScale = ltScale; }
            catch { }

            btr.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);
        }

        private string EnsureShopDashedLinetype(
            Transaction tr,
            Database db)
        {
            string[] candidates =
            {
                "DASHED", "DASH", "HIDDEN", "CENTER",
                "ACAD_ISO02W100", "ACAD_ISO04W100",
                "ACAD_ISO03W100"
            };

            try
            {
                LinetypeTable ltt =
                    (LinetypeTable)tr.GetObject(
                        db.LinetypeTableId,
                        OpenMode.ForRead);

                foreach (string name in candidates)
                {
                    if (ltt.Has(name))
                        return name;
                }
            }
            catch { }

            // Load từ acad.lin / acadiso.lin nếu chưa có
            string[] linFiles =
            {
                "acad.lin",
                "acadiso.lin"
            };

            foreach (string lin in linFiles)
            {
                foreach (string name in candidates)
                {
                    try
                    {
                        db.LoadLineTypeFile(name, lin);

                        LinetypeTable ltt2 =
                            (LinetypeTable)tr.GetObject(
                                db.LinetypeTableId,
                                OpenMode.ForRead);

                        if (ltt2.Has(name))
                            return name;
                    }
                    catch { }
                }
            }

            return "";
        }

        private void AppendShopLine(
            Transaction tr,
            Database db,
            BlockTableRecord btr,
            Point3d start,
            Point3d end,
            string layerName)
        {
            if (start.DistanceTo(end) < 1.0)
                return;

            Line line = new Line(start, end);
            line.SetDatabaseDefaults(db);
            ApplyShopEntityStyle(line, layerName);

            btr.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);
        }

        private string InferShopMaterialFromPipes(
            List<ShopPipeCandidate> shopPipes,
            string fallbackMaterial)
        {
            if (shopPipes == null || shopPipes.Count == 0)
                return fallbackMaterial ?? "TRÁNG KẼM";

            Dictionary<string, int> votes =
                new Dictionary<string, int>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (ShopPipeCandidate pipe in shopPipes)
            {
                string layer = "";
                try
                {
                    layer = pipe?.Curve?.Layer ?? "";
                }
                catch
                {
                    layer = "";
                }

                if (string.IsNullOrWhiteSpace(layer))
                    layer = pipe?.LayerName ?? "";

                string mat =
                    DetectShopMaterialFromLayerOrText(layer);

                if (string.IsNullOrWhiteSpace(mat))
                    continue;

                if (!votes.ContainsKey(mat))
                    votes[mat] = 0;
                votes[mat]++;
            }

            if (votes.Count > 0)
            {
                return votes
                    .OrderByDescending(x => x.Value)
                    .ThenBy(x => x.Key)
                    .First()
                    .Key;
            }

            return fallbackMaterial ?? "TRÁNG KẼM";
        }

        private string DetectShopMaterialFromLayerOrText(
            string text)
        {
            string u =
                BoDauTiengViet(text ?? "")
                    .ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(u))
                return "";

            if (u.Contains("PPR"))
                return "PPR";
            if (u.Contains("UPVC") ||
                (u.Contains("PVC") && !u.Contains("PPR")))
                return "UPVC";
            if (u.Contains("HDPE"))
                return "HDPE";
            if (u.Contains("INOX") || u.Contains("STAINLESS"))
                return "INOX";
            if (u.Contains("NHUNG") || u.Contains("HOTDIP"))
                return "NHÚNG NÓNG";
            if (u.Contains("THEPDEN") ||
                (u.Contains("THEP") && u.Contains("DEN")) ||
                u.Contains("BLACKSTEEL"))
                return "THÉP ĐEN";
            if (u.Contains("TRANG") ||
                u.Contains("KEM") ||
                u.Contains("GALV") ||
                u.Contains("STEEL"))
                return "TRÁNG KẼM";

            return "";
        }

        private string FindShopFittingLibraryPath(
            string materialName,
            Database db)
        {
            List<string> fileNames =
                GetShopLibraryFileNames(materialName);

            if (fileNames == null || fileNames.Count == 0)
                return "";

            List<string> folders =
                new List<string>();

            try
            {
                string dllFolder =
                    Path.GetDirectoryName(
                        Assembly.GetExecutingAssembly().Location);

                if (!string.IsNullOrWhiteSpace(dllFolder))
                    folders.Add(dllFolder);
            }
            catch { }

            try
            {
                string dwgFolder =
                    Path.GetDirectoryName(db?.Filename ?? "");

                if (!string.IsNullOrWhiteSpace(dwgFolder))
                    folders.Add(dwgFolder);
            }
            catch { }

            try
            {
                string current = Directory.GetCurrentDirectory();
                if (!string.IsNullOrWhiteSpace(current))
                    folders.Add(current);
            }
            catch { }

            // Dùng đúng tên 3 file thư viện hiện tại của người dùng.
            foreach (string folder in folders.Distinct(
                StringComparer.OrdinalIgnoreCase))
            {
                foreach (string fileName in fileNames)
                {
                    try
                    {
                        string path = Path.Combine(folder, fileName);
                        if (File.Exists(path))
                            return path;
                    }
                    catch { }
                }

                // Fallback: quét thư mục theo tên gần đúng (không phân biệt hoa thường)
                try
                {
                    if (!Directory.Exists(folder))
                        continue;

                    string[] existing =
                        Directory.GetFiles(folder, "*.dwg");

                    foreach (string fileName in fileNames)
                    {
                        string target =
                            Path.GetFileName(fileName)
                                .ToLowerInvariant()
                                .Replace(" ", "");

                        foreach (string existingPath in existing)
                        {
                            string name =
                                Path.GetFileName(existingPath)
                                    .ToLowerInvariant()
                                    .Replace(" ", "");

                            if (name == target ||
                                name.Contains(
                                    target.Replace(".dwg", "")))
                            {
                                return existingPath;
                            }
                        }
                    }
                }
                catch { }
            }

            return "";
        }

        private List<string> GetShopLibraryFileNames(string materialName)
        {
            string u =
                BoDauTiengViet(materialName ?? "");

            if (u.Contains("PPR"))
            {
                return new List<string>
                {
                    "Thu vien PPR.dwg",
                    "Thu vien PPR.DWG",
                    "ThuvienPPR.dwg",
                    "PPR.dwg"
                };
            }

            if (u.Contains("UPVC") ||
                u.Contains("PVC") ||
                u.Contains("HDPE"))
            {
                return new List<string>
                {
                    // TÊN MỚI người dùng đang dùng.
                    "Thu vien uPVC.dwg",
                    "Thu vien uPVC.DWG",
                    "Thu vien UPVC.dwg",
                    "ThuvienuPVC.dwg",
                    "uPVC.dwg",
                    "UPVC.dwg"
                };
            }

            if (u.Contains("TRANG") ||
                u.Contains("KEM") ||
                u.Contains("THEP") ||
                u.Contains("NHUNG") ||
                u.Contains("STEEL") ||
                u.Contains("INOX") ||
                u.Contains("DEN"))
            {
                return new List<string>
                {
                    "Thu vien STEEL.dwg",
                    "Thu vien STEEL.DWG",
                    "ThuvienSTEEL.dwg",
                    "STEEL.dwg"
                };
            }

            // Mặc định thử STEEL (thép/tráng kẽm phổ biến cho chữa cháy)
            return new List<string>
            {
                "Thu vien STEEL.dwg",
                "Thu vien STEEL.DWG"
            };
        }

        /// <summary>
        /// Block thư viện SCREW/WELD: tâm hình học = điểm chèn trên đường tâm.
        /// Không offset theo AnalyzeShopFittingPlacement (dễ lệch 1 bên).
        /// </summary>
        /// <summary>
        /// Tâm hình học + nửa chiều dài theo trục dài của block (local).
        /// </summary>
        /// <summary>
        /// Khoảng rút ống tại co/tê = khoảng từ base point tới mép xa nhất
        /// theo phương X hoặc Y (chân co).
        /// </summary>
        private double EstimateShopElbowLegGap(
            Transaction tr,
            ObjectId blockId)
        {
            try
            {
                BlockTableRecord rec =
                    tr.GetObject(blockId, OpenMode.ForRead, false)
                        as BlockTableRecord;
                if (rec == null)
                    return 0;

                Extents3d? ext = null;
                foreach (ObjectId id in rec)
                {
                    Entity ent =
                        tr.GetObject(id, OpenMode.ForRead, false)
                            as Entity;
                    if (ent == null || ent.IsErased)
                        continue;
                    try
                    {
                        Extents3d e = ent.GeometricExtents;
                        if (ext == null) ext = e;
                        else
                        {
                            Extents3d cur = ext.Value;
                            cur.AddExtents(e);
                            ext = cur;
                        }
                    }
                    catch { }
                }

                if (ext == null)
                    return 0;

                Extents3d box = ext.Value;
                // Từ origin (0,0) ra mép — lấy cạnh lớn hơn (chân co)
                double toX =
                    Math.Max(
                        Math.Abs(box.MinPoint.X),
                        Math.Abs(box.MaxPoint.X));
                double toY =
                    Math.Max(
                        Math.Abs(box.MinPoint.Y),
                        Math.Abs(box.MaxPoint.Y));
                // Lấy cạnh NHỎ hơn = chân co/tê thật (max sẽ lấy luôn nhánh dài → hở to)
                double leg = Math.Min(toX, toY);
                if (leg < 1.0)
                    leg = Math.Max(toX, toY);
                // 0.82: ống chạm sát mặt bích, không hở khúc
                return leg > 1.0 ? leg * 0.82 : 0;
            }
            catch
            {
                return 0;
            }
        }

        private bool TryGetShopBlockLocalCenter(
            Transaction tr,
            ObjectId blockId,
            out Point3d localCenter,
            out double halfAlongAxis)
        {
            localCenter = Point3d.Origin;
            halfAlongAxis = 0;

            try
            {
                BlockTableRecord rec =
                    tr.GetObject(blockId, OpenMode.ForRead, false)
                        as BlockTableRecord;

                if (rec == null)
                    return false;

                Extents3d? ext = null;

                foreach (ObjectId id in rec)
                {
                    Entity ent =
                        tr.GetObject(id, OpenMode.ForRead, false)
                            as Entity;

                    if (ent == null || ent.IsErased)
                        continue;

                    try
                    {
                        Extents3d e = ent.GeometricExtents;
                        if (ext == null)
                            ext = e;
                        else
                        {
                            Extents3d cur = ext.Value;
                            cur.AddExtents(e);
                            ext = cur;
                        }
                    }
                    catch { }
                }

                if (ext == null)
                    return false;

                Extents3d box = ext.Value;
                localCenter =
                    new Point3d(
                        (box.MinPoint.X + box.MaxPoint.X) * 0.5,
                        (box.MinPoint.Y + box.MaxPoint.Y) * 0.5,
                        (box.MinPoint.Z + box.MaxPoint.Z) * 0.5);

                double dx = Math.Abs(box.MaxPoint.X - box.MinPoint.X);
                double dy = Math.Abs(box.MaxPoint.Y - box.MinPoint.Y);
                // Trục ống trong block giảm/co thường là X
                double along = dx >= dy ? dx : dy;
                halfAlongAxis = along * 0.5;

                return halfAlongAxis > 1.0;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsShopSimpleCenterBlock(
            string blockName,
            string fittingType)
        {
            string key = NormalizeShopKey(blockName ?? "");
            string typeKey = NormalizeShopKey(fittingType ?? "");

            if (key.StartsWith("SCREW") ||
                key.StartsWith("WELD"))
            {
                return true;
            }

            // Co / giảm / tê chuẩn thư viện user
            if (key.Contains("ELB90") ||
                key.Contains("ELB45") ||
                key.Contains("ELBOW") ||
                (key.Contains("RED") && !key.Contains("THREAD")) ||
                key.Contains("TEE") ||
                key.Contains("GIAM"))
            {
                return true;
            }

            if (IsElbow90Key(typeKey) ||
                IsElbow45Key(typeKey) ||
                IsTeeKey(typeKey) ||
                IsReducerKey(typeKey))
            {
                // Mặc định phụ kiện shop: chèn tâm
                return true;
            }

            return false;
        }

        private bool TryInsertShopLibraryFitting(
            Transaction tr,
            Database db,
            BlockTableRecord btr,
            string libraryPath,
            string fittingType,
            string fromSize,
            string toSize,
            Point3d insertPoint,
            double rotation,
            string layerName,
            bool mirrorY = false,
            List<ShopFittingGapInfo> fittingGaps = null)
        {
            if (string.IsNullOrWhiteSpace(libraryPath) ||
                !File.Exists(libraryPath))
                return false;

            string blockName =
                FindShopFittingBlockName(
                    libraryPath,
                    fittingType,
                    fromSize,
                    toSize);

            if (string.IsNullOrWhiteSpace(blockName))
            {
                try
                {
                    Autodesk.AutoCAD.ApplicationServices.Core.Application
                        .DocumentManager
                        .MdiActiveDocument?
                        .Editor
                        .WriteMessage(
                            $"\n[SHOP {ShopSmartBuild}] KHÔNG TÌM BLOCK " +
                            $"{fittingType} {fromSize}->{toSize}");
                }
                catch { }

                return false;
            }

            ObjectId blockId =
                EnsureExternalBlockImported(
                    tr,
                    db,
                    libraryPath,
                    blockName);

            if (blockId.IsNull)
                return false;

            NormalizeShopImportedBlock(tr, blockId);

            // QUAN TRỌNG: không dùng Base Point của block làm tâm nối.
            // Đọc hình học thật của block, tìm các cổng nối rồi suy ra tâm
            // hình học (virtual intersection) của Co/Tê/Giảm.
            ShopFittingPlacementInfo placement =
                AnalyzeShopFittingPlacement(
                    blockId,
                    fittingType);

            double desiredPrimaryAngle = rotation;
            double finalRotation = rotation;
            double finalMirrorY = mirrorY ? -1.0 : 1.0;
            Point3d basePoint = insertPoint;

            // REDUCER: căn tâm hình học trên đường tâm (thẳng).
            // CO / TÊ: dùng Base Point gốc block tại nút (thường là giao 2 trục tâm).
            //   Không dùng bbox center — sẽ lệch, ống đâm vào thân phụ kiện.
            bool isReducer =
                IsReducerKey(NormalizeShopKey(fittingType ?? "")) ||
                NormalizeShopKey(blockName ?? "").Contains("RED") ||
                NormalizeShopKey(blockName ?? "").Contains("GIAM");

            bool isElbowOrTee =
                IsElbow90Key(NormalizeShopKey(fittingType ?? "")) ||
                IsElbow45Key(NormalizeShopKey(fittingType ?? "")) ||
                IsTeeKey(NormalizeShopKey(fittingType ?? "")) ||
                NormalizeShopKey(blockName ?? "").Contains("ELB") ||
                NormalizeShopKey(blockName ?? "").Contains("TEE") ||
                NormalizeShopKey(blockName ?? "").Contains("CO");

            finalRotation = rotation;
            finalMirrorY = mirrorY ? -1.0 : 1.0;
            basePoint = insertPoint;

            if (isReducer &&
                IsShopSimpleCenterBlock(blockName, fittingType))
            {
                Point3d localCenter;
                double halfAlong;
                if (TryGetShopBlockLocalCenter(
                        tr,
                        blockId,
                        out localCenter,
                        out halfAlong))
                {
                    Vector3d local =
                        new Vector3d(
                            localCenter.X,
                            localCenter.Y * finalMirrorY,
                            localCenter.Z);

                    Vector3d world =
                        local.RotateBy(
                            finalRotation,
                            Vector3d.ZAxis);

                    basePoint = insertPoint - world;

                    RecordShopFittingGap(
                        fittingGaps,
                        insertPoint,
                        halfAlong);
                }
            }
            else if (isElbowOrTee)
            {
                // FIX23 chỉ sửa CO/TÊ.
                // Reducer ở nhánh phía trên GIỮ NGUYÊN hoàn toàn.

                if (placement != null &&
                    placement.Ports != null &&
                    placement.Ports.Count >= 2 &&
                    TryResolveShopFittingTransform(
                        fittingType,
                        placement,
                        desiredPrimaryAngle,
                        mirrorY,
                        out finalRotation,
                        out finalMirrorY))
                {
                    Vector3d localAnchor =
                        new Vector3d(
                            placement.Anchor.X,
                            placement.Anchor.Y * finalMirrorY,
                            placement.Anchor.Z);

                    Vector3d transformedAnchor =
                        localAnchor.RotateBy(
                            finalRotation,
                            Vector3d.ZAxis);

                    // Đưa giao tâm thật của các port vào đúng node mạng ống.
                    basePoint =
                        insertPoint -
                        transformedAnchor;

                }
                else
                {
                    // Fallback đúng code cũ nếu block không phân tích được.
                    basePoint = insertPoint;

                    double legGap =
                        EstimateShopElbowLegGap(
                            tr,
                            blockId);

                    if (legGap < 1.0)
                    {
                        Point3d localCenter;
                        double halfAlong;

                        if (TryGetShopBlockLocalCenter(
                                tr,
                                blockId,
                                out localCenter,
                                out halfAlong))
                        {
                            legGap = halfAlong;
                        }
                    }

                    if (legGap < 1.0)
                    {
                        double dn =
                            Math.Max(
                                ParseShopDnNumber(
                                    NormalizeShopDnToken(
                                        fromSize)),
                                ParseShopDnNumber(
                                    NormalizeShopDnToken(
                                        toSize)));

                        if (dn <= 0)
                            dn = 25;

                        legGap =
                            Math.Max(
                                8.0,
                                dn * 0.40);
                    }

                    RecordShopFittingGap(
                        fittingGaps,
                        insertPoint,
                        legGap);
                }
            }
            else if (placement != null &&
                     placement.Ports.Count > 0 &&
                     TryResolveShopFittingTransform(
                         fittingType,
                         placement,
                         desiredPrimaryAngle,
                         mirrorY,
                         out finalRotation,
                         out finalMirrorY))
            {
                Vector3d localAnchor =
                    new Vector3d(
                        placement.Anchor.X,
                        placement.Anchor.Y * finalMirrorY,
                        placement.Anchor.Z);

                Vector3d transformedAnchor =
                    localAnchor.RotateBy(
                        finalRotation,
                        Vector3d.ZAxis);

                basePoint = insertPoint - transformedAnchor;
            }

            // Chống chèn trùng theo tâm nối / vị trí chèn trên đường tâm.
            foreach (ObjectId existingId in btr)
            {
                BlockReference existing =
                    tr.GetObject(
                        existingId,
                        OpenMode.ForRead,
                        false) as BlockReference;

                if (existing == null ||
                    existing.IsErased ||
                    existing.BlockTableRecord != blockId)
                {
                    continue;
                }

                try
                {
                    if (placement != null &&
                        placement.Ports.Count > 0)
                    {
                        Point3d existingAnchor =
                            placement.Anchor.TransformBy(
                                existing.BlockTransform);

                        if (existingAnchor.DistanceTo(insertPoint) <= 100.0)
                            return true;
                    }
                    else if (existing.Position.DistanceTo(insertPoint) <= 100.0)
                    {
                        return true;
                    }
                }
                catch
                {
                    if (existing.Position.DistanceTo(basePoint) <= 100.0)
                        return true;
                }
            }

            BlockReference br =
                new BlockReference(
                    basePoint,
                    blockId);

            br.SetDatabaseDefaults(db);
            br.Layer = layerName;
            br.Rotation = finalRotation;
            br.ScaleFactors =
                new Scale3d(
                    1.0,
                    finalMirrorY,
                    1.0);
            br.Color =
                Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                    ColorMethod.ByAci,
                    GetShopVisibleAciColor(layerName));
            br.LineWeight = LineWeight.LineWeight000;
            br.Transparency =
                new Autodesk.AutoCAD.Colors.Transparency((byte)255);
            br.Visible = true;

            btr.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);

            // FIX24: riêng co cuối đầu phun, đổi CHÍNH block vừa chèn
            // sang trạng thái "đi xuống". Không dùng mirror mặt bằng.
            bool isSprinklerDropElbow =
                layerName != null &&
                layerName.StartsWith(
                    "FF_SHOP_CO90_DAUPHUN_",
                    StringComparison.OrdinalIgnoreCase);

            if (isSprinklerDropElbow)
            {
                TrySetShopFittingVisibilityOnReference(
                    br,
                    preferDown: true,
                    preferHorizontal: false);
            }

            // FIX23: CO/TÊ rút từng ống đúng tới mặt cổng thật.
            // REDUCER không đi qua đoạn này => giữ nguyên cơ chế đang chạy tốt.
            if (isElbowOrTee &&
                !isSprinklerDropElbow &&
                placement != null &&
                placement.Ports != null &&
                placement.Ports.Count >= 2 &&
                fittingGaps != null)
            {
                string typeKey =
                    NormalizeShopKey(
                        fittingType ?? "");

                for (int portIndex = 0;
                    portIndex < placement.Ports.Count;
                    portIndex++)
                {
                    ShopFittingPortInfo port =
                        placement.Ports[portIndex];

                    if (port == null)
                        continue;

                    Point3d worldPort;

                    try
                    {
                        worldPort =
                            port.Center.TransformBy(
                                br.BlockTransform);
                    }
                    catch
                    {
                        continue;
                    }

                    Vector3d portDir =
                        new Vector3d(
                            worldPort.X - insertPoint.X,
                            worldPort.Y - insertPoint.Y,
                            0.0);

                    double portGap =
                        portDir.Length;

                    if (portGap <= 1.0)
                        continue;

                    portDir =
                        portDir.GetNormal();

                    string portSize =
                        fromSize;

                    if (IsTeeKey(typeKey) &&
                        placement.Ports.Count >= 3)
                    {
                        // AnalyzeShopTeePorts luôn xếp:
                        // 0,1 = main ; 2 = branch.
                        portSize =
                            portIndex == 2
                                ? toSize
                                : fromSize;
                    }

                    // Trừ 0.5 để 2 nét ống chạm/đè cực nhẹ vào mặt phụ kiện,
                    // không tạo khe trắng do regen.
                    RecordShopFittingGapDirectional(
                        fittingGaps,
                        insertPoint,
                        Math.Max(
                            1.0,
                            portGap - 0.5),
                        portDir,
                        portSize);
                }
            }

            // Co đầu phun: chỉ cần rút ống ngang tới thân co.
            // Chân còn lại đi xuống Z nên không tạo gap thứ hai trên mặt bằng.
            if (isSprinklerDropElbow &&
                fittingGaps != null)
            {
                double legGap =
                    EstimateShopElbowLegGap(
                        tr,
                        blockId);

                if (legGap < 1.0)
                {
                    double dn =
                        ParseShopDnNumber(
                            NormalizeShopDnToken(
                                fromSize));

                    if (dn <= 0)
                        dn = 25.0;

                    legGap =
                        Math.Max(
                            8.0,
                            dn * 0.40);
                }

                Vector3d inletDir =
                    new Vector3d(
                        Math.Cos(rotation),
                        Math.Sin(rotation),
                        0.0);

                RecordShopFittingGapDirectional(
                    fittingGaps,
                    insertPoint,
                    Math.Max(
                        1.0,
                        legGap),
                    inletDir,
                    fromSize);
            }

            // Fallback gap:
            // - REDUCER: giữ nguyên logic cũ.
            // - CO/TÊ: chỉ fallback nếu thực sự không có directional port gap.
            if (fittingGaps != null)
            {
                bool already = false;

                foreach (ShopFittingGapInfo g in fittingGaps)
                {
                    if (g == null ||
                        g.Joint.DistanceTo(insertPoint) >
                            ShopDuplicateNodeTolerance)
                    {
                        continue;
                    }

                    if (!isElbowOrTee ||
                        isSprinklerDropElbow ||
                        g.Direction.Length > 1e-9)
                    {
                        already = true;
                        break;
                    }
                }

                if (!already)
                {
                    double half =
                        EstimateShopFittingHalfLength(
                            tr,
                            blockId,
                            placement,
                            fromSize,
                            toSize);

                    RecordShopFittingGap(
                        fittingGaps,
                        insertPoint,
                        half);
                }
            }

            return true;
        }

        private double EstimateShopFittingHalfLength(
            Transaction tr,
            ObjectId blockId,
            ShopFittingPlacementInfo placement,
            string fromSize,
            string toSize)
        {
            // 1) Extents block — ưu tiên cho SCREW/WELD chèn tâm
            //    Giảm/co thường dài theo trục X trong định nghĩa block.
            try
            {
                BlockTableRecord rec =
                    tr.GetObject(blockId, OpenMode.ForRead, false)
                        as BlockTableRecord;
                if (rec != null)
                {
                    Extents3d? ext = null;
                    foreach (ObjectId id in rec)
                    {
                        Entity ent =
                            tr.GetObject(id, OpenMode.ForRead, false)
                                as Entity;
                        if (ent == null || ent.IsErased)
                            continue;
                        try
                        {
                            Extents3d e = ent.GeometricExtents;
                            if (ext == null)
                                ext = e;
                            else
                            {
                                Extents3d cur = ext.Value;
                                cur.AddExtents(e);
                                ext = cur;
                            }
                        }
                        catch { }
                    }
                    if (ext != null)
                    {
                        Extents3d e = ext.Value;
                        double dx = Math.Abs(e.MaxPoint.X - e.MinPoint.X);
                        double dy = Math.Abs(e.MaxPoint.Y - e.MinPoint.Y);
                        // Trục dọc ống = cạnh dài hơn (giảm/co)
                        double along = Math.Max(dx, dy);
                        if (along > 2.0)
                        {
                            // Một chút nhỏ hơn 0.5 để nét ống chạm mặt bích, không hở
                            return along * 0.48;
                        }
                    }
                }
            }
            catch { }

            // 2) Từ cổng nối đã phân tích
            if (placement != null &&
                placement.Ports != null &&
                placement.Ports.Count > 0)
            {
                double maxDist = 0;
                foreach (ShopFittingPortInfo port in placement.Ports)
                {
                    if (port == null)
                        continue;
                    double d =
                        port.Center.DistanceTo(placement.Anchor);
                    if (d > maxDist)
                        maxDist = d;
                }
                if (maxDist > 1.0)
                    return maxDist;
            }

            // 3) Ước lượng theo DN
            double n1 = ParseShopDnNumber(NormalizeShopDnToken(fromSize));
            double n2 = ParseShopDnNumber(NormalizeShopDnToken(toSize));
            double refN = Math.Max(n1, n2);
            if (refN <= 0)
                refN = 25;
            return Math.Max(12.0, Math.Min(80.0, refN * 0.70));
        }

        private ShopFittingPlacementInfo AnalyzeShopFittingPlacement(
            ObjectId blockId,
            string fittingType)
        {
            if (blockId.IsNull)
                return null;

            List<ShopFittingEndpointSample> samples =
                CollectShopFittingEndpointSamples(blockId);

            if (samples.Count < 2)
                return null;

            List<ShopFittingPortInfo> ports =
                BuildShopFittingPorts(samples);

            string key = NormalizeShopKey(fittingType);

            if (IsReducerKey(key))
                return AnalyzeShopReducerPorts(ports);

            if (IsTeeKey(key))
                return AnalyzeShopTeePorts(ports);

            if (IsElbow45Key(key))
                return AnalyzeShopElbowPorts(ports, 135.0);

            if (IsElbow90Key(key))
                return AnalyzeShopElbowPorts(ports, 90.0);

            return null;
        }

        private List<ShopFittingEndpointSample> CollectShopFittingEndpointSamples(
            ObjectId blockId)
        {
            List<ShopFittingEndpointSample> result =
                new List<ShopFittingEndpointSample>();

            DBObjectCollection exploded =
                new DBObjectCollection();

            try
            {
                using (BlockReference temp =
                    new BlockReference(Point3d.Origin, blockId))
                {
                    temp.Explode(exploded);
                }

                CollectShopFittingEndpointSamplesFromObjects(
                    exploded,
                    result,
                    0);
            }
            catch
            {
            }
            finally
            {
                foreach (DBObject obj in exploded)
                {
                    try { obj?.Dispose(); } catch { }
                }
            }

            // Lọc theo topology: góc kín của hình vẽ thường có từ 2 đường
            // cùng chạm một điểm nhưng khác hướng. Đó KHÔNG phải cổng nối.
            // Cổng thật thường là đầu hở: chỉ có một hướng đi vào fitting.
            List<List<ShopFittingEndpointSample>> pointClusters =
                new List<List<ShopFittingEndpointSample>>();

            foreach (ShopFittingEndpointSample sample in result)
            {
                if (sample == null ||
                    sample.OutwardDirection.Length < 1e-9)
                    continue;

                List<ShopFittingEndpointSample> cluster =
                    pointClusters.FirstOrDefault(g =>
                        g.Count > 0 &&
                        g[0].Point.DistanceTo(sample.Point) <= 0.75);

                if (cluster == null)
                {
                    cluster =
                        new List<ShopFittingEndpointSample>();
                    pointClusters.Add(cluster);
                }

                cluster.Add(sample);
            }

            List<ShopFittingEndpointSample> dedup =
                new List<ShopFittingEndpointSample>();

            foreach (List<ShopFittingEndpointSample> cluster in pointClusters)
            {
                if (cluster.Count == 0)
                    continue;

                Vector3d first =
                    cluster[0].OutwardDirection.GetNormal();

                bool hasDifferentDirection =
                    cluster.Skip(1).Any(x =>
                        GetShopAngleDegrees(
                            first,
                            x.OutwardDirection) > 8.0);

                if (hasDifferentDirection)
                {
                    // Đây là giao điểm hình học nội bộ của block.
                    continue;
                }

                Vector3d avg =
                    AverageShopDirection(
                        cluster.Select(x => x.OutwardDirection));

                if (avg.Length < 1e-9)
                    continue;

                dedup.Add(
                    new ShopFittingEndpointSample
                    {
                        Point = new Point3d(
                            cluster.Average(x => x.Point.X),
                            cluster.Average(x => x.Point.Y),
                            0.0),
                        OutwardDirection = avg.GetNormal()
                    });
            }

            return dedup;
        }

        private void CollectShopFittingEndpointSamplesFromObjects(
            DBObjectCollection objects,
            List<ShopFittingEndpointSample> result,
            int depth)
        {
            if (objects == null ||
                result == null ||
                depth > 6)
            {
                return;
            }

            foreach (DBObject obj in objects)
            {
                if (obj is BlockReference nested)
                {
                    DBObjectCollection nestedObjects =
                        new DBObjectCollection();

                    try
                    {
                        nested.Explode(nestedObjects);
                        CollectShopFittingEndpointSamplesFromObjects(
                            nestedObjects,
                            result,
                            depth + 1);
                    }
                    catch { }
                    finally
                    {
                        foreach (DBObject nestedObj in nestedObjects)
                        {
                            try { nestedObj?.Dispose(); } catch { }
                        }
                    }

                    continue;
                }

                if (obj is Line line)
                {
                    AddShopCurveEndSamples(
                        line,
                        result);
                    continue;
                }

                if (obj is Arc arc)
                {
                    AddShopCurveEndSamples(
                        arc,
                        result);
                    continue;
                }

                if (obj is Polyline pl)
                {
                    if (!pl.Closed &&
                        pl.NumberOfVertices >= 2)
                    {
                        AddShopCurveEndSamples(
                            pl,
                            result);
                    }

                    continue;
                }

                if (obj is Polyline2d pl2)
                {
                    if (!pl2.Closed)
                    {
                        AddShopCurveEndSamples(
                            pl2,
                            result);
                    }
                }
            }
        }

        private void AddShopCurveEndSamples(
            Curve curve,
            List<ShopFittingEndpointSample> result)
        {
            if (curve == null || result == null)
                return;

            try
            {
                Point3d start = curve.StartPoint;
                Point3d end = curve.EndPoint;

                if (start.DistanceTo(end) <= 1e-6)
                    return;

                Vector3d startInward =
                    curve.GetFirstDerivative(
                        curve.StartParam);

                Vector3d endInward =
                    -curve.GetFirstDerivative(
                        curve.EndParam);

                if (startInward.Length > 1e-9)
                {
                    result.Add(
                        new ShopFittingEndpointSample
                        {
                            Point = start,
                            OutwardDirection =
                                -startInward.GetNormal()
                        });
                }

                if (endInward.Length > 1e-9)
                {
                    result.Add(
                        new ShopFittingEndpointSample
                        {
                            Point = end,
                            OutwardDirection =
                                -endInward.GetNormal()
                        });
                }
            }
            catch { }
        }

        private List<ShopFittingPortInfo> BuildShopFittingPorts(
            List<ShopFittingEndpointSample> samples)
        {
            List<ShopFittingPortInfo> result =
                new List<ShopFittingPortInfo>();

            if (samples == null || samples.Count == 0)
                return result;

            Point3d center =
                new Point3d(
                    samples.Average(s => s.Point.X),
                    samples.Average(s => s.Point.Y),
                    0.0);

            // Bỏ endpoint mà hướng "outward" lại chĩa sâu vào giữa fitting.
            List<ShopFittingEndpointSample> filtered =
                samples.Where(s =>
                {
                    Vector3d radial = s.Point - center;
                    if (radial.Length < 1e-6)
                        return true;

                    return s.OutwardDirection.GetNormal()
                        .DotProduct(radial.GetNormal()) >= -0.15;
                })
                .ToList();

            if (filtered.Count < 2)
                filtered = samples.ToList();

            const double directionToleranceDeg = 18.0;
            List<List<ShopFittingEndpointSample>> groups =
                new List<List<ShopFittingEndpointSample>>();

            foreach (ShopFittingEndpointSample sample in filtered)
            {
                List<ShopFittingEndpointSample> bestGroup = null;
                double bestAngle = double.MaxValue;

                foreach (List<ShopFittingEndpointSample> group in groups)
                {
                    Vector3d avg =
                        AverageShopDirection(
                            group.Select(x => x.OutwardDirection));

                    if (avg.Length < 1e-9)
                        continue;

                    double angle =
                        GetShopAngleDegrees(
                            avg,
                            sample.OutwardDirection);

                    if (angle <= directionToleranceDeg &&
                        angle < bestAngle)
                    {
                        bestAngle = angle;
                        bestGroup = group;
                    }
                }

                if (bestGroup == null)
                {
                    bestGroup =
                        new List<ShopFittingEndpointSample>();
                    groups.Add(bestGroup);
                }

                bestGroup.Add(sample);
            }

            foreach (List<ShopFittingEndpointSample> group in groups)
            {
                if (group.Count == 0)
                    continue;

                Vector3d outward =
                    AverageShopDirection(
                        group.Select(x => x.OutwardDirection));

                if (outward.Length < 1e-9)
                    continue;

                outward = outward.GetNormal();

                Point3d portCenter =
                    new Point3d(
                        group.Average(x => x.Point.X),
                        group.Average(x => x.Point.Y),
                        0.0);

                Vector3d normal =
                    new Vector3d(
                        -outward.Y,
                        outward.X,
                        0.0);

                List<double> projected =
                    group.Select(x =>
                        (x.Point - portCenter)
                            .DotProduct(normal))
                        .ToList();

                double width = 0.0;
                if (projected.Count >= 2)
                    width = projected.Max() - projected.Min();

                result.Add(
                    new ShopFittingPortInfo
                    {
                        Center = portCenter,
                        OutwardDirection = outward,
                        Width = Math.Abs(width),
                        SampleCount = group.Count
                    });
            }

            return result;
        }

        private Vector3d AverageShopDirection(
            IEnumerable<Vector3d> directions)
        {
            if (directions == null)
                return new Vector3d(0.0, 0.0, 0.0);

            double x = 0.0;
            double y = 0.0;
            int count = 0;

            foreach (Vector3d d in directions)
            {
                if (d.Length < 1e-9)
                    continue;

                Vector3d n = d.GetNormal();
                x += n.X;
                y += n.Y;
                count++;
            }

            if (count == 0)
                return new Vector3d(0.0, 0.0, 0.0);

            Vector3d result =
                new Vector3d(x, y, 0.0);

            return result.Length > 1e-9
                ? result.GetNormal()
                : new Vector3d(0.0, 0.0, 0.0);
        }

        private ShopFittingPlacementInfo AnalyzeShopReducerPorts(
            List<ShopFittingPortInfo> ports)
        {
            if (ports == null || ports.Count < 2)
                return null;

            ShopFittingPortInfo pa = null;
            ShopFittingPortInfo pb = null;
            double bestScore = double.MaxValue;

            for (int i = 0; i < ports.Count; i++)
            {
                for (int j = i + 1; j < ports.Count; j++)
                {
                    double angle =
                        GetShopAngleDegrees(
                            ports[i].OutwardDirection,
                            ports[j].OutwardDirection);

                    double score = Math.Abs(180.0 - angle);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        pa = ports[i];
                        pb = ports[j];
                    }
                }
            }

            if (pa == null || pb == null || bestScore > 30.0)
                return null;

            ShopFittingPlacementInfo info =
                new ShopFittingPlacementInfo();

            info.Ports.Add(pa);
            info.Ports.Add(pb);
            info.Anchor = MidPoint(pa.Center, pb.Center);

            // Đầu nào có bề rộng hình học lớn hơn được coi là đầu DN lớn.
            if (Math.Abs(pa.Width - pb.Width) > 1.0)
            {
                info.LargePortIndex = pa.Width >= pb.Width ? 0 : 1;
                info.SmallPortIndex = pa.Width >= pb.Width ? 1 : 0;
            }
            else
            {
                // Không đủ dữ liệu bề rộng: vẫn giữ thứ tự nhưng transform
                // sẽ ưu tiên đúng trục. Không dùng mẹo cộng PI như bản cũ.
                info.LargePortIndex = 0;
                info.SmallPortIndex = 1;
            }

            return info;
        }

        private ShopFittingPlacementInfo AnalyzeShopElbowPorts(
            List<ShopFittingPortInfo> ports,
            double expectedIncludedAngle)
        {
            if (ports == null || ports.Count < 2)
                return null;

            ShopFittingPortInfo pa = null;
            ShopFittingPortInfo pb = null;
            double bestScore = double.MaxValue;

            for (int i = 0; i < ports.Count; i++)
            {
                for (int j = i + 1; j < ports.Count; j++)
                {
                    double angle =
                        GetShopAngleDegrees(
                            ports[i].OutwardDirection,
                            ports[j].OutwardDirection);

                    double angleError =
                        Math.Abs(expectedIncludedAngle - angle);

                    double distance =
                        ports[i].Center.DistanceTo(ports[j].Center);

                    // Ưu tiên đúng góc, sau đó ưu tiên hai cổng cách xa nhau.
                    double score =
                        angleError * 1000.0 -
                        Math.Min(distance, 100000.0);

                    if (score < bestScore)
                    {
                        bestScore = score;
                        pa = ports[i];
                        pb = ports[j];
                    }
                }
            }

            if (pa == null || pb == null)
                return null;

            double finalAngle =
                GetShopAngleDegrees(
                    pa.OutwardDirection,
                    pb.OutwardDirection);

            if (Math.Abs(finalAngle - expectedIncludedAngle) > 35.0)
                return null;

            Point3d anchor;
            if (!TryIntersectShopAxes(
                    pa.Center,
                    pa.OutwardDirection,
                    pb.Center,
                    pb.OutwardDirection,
                    out anchor))
            {
                anchor = MidPoint(pa.Center, pb.Center);
            }

            ShopFittingPlacementInfo info =
                new ShopFittingPlacementInfo
                {
                    Anchor = anchor
                };

            info.Ports.Add(pa);
            info.Ports.Add(pb);
            return info;
        }

        private ShopFittingPlacementInfo AnalyzeShopTeePorts(
            List<ShopFittingPortInfo> ports)
        {
            if (ports == null || ports.Count < 3)
                return null;

            int mainA = -1;
            int mainB = -1;
            double bestOppositeError = double.MaxValue;

            for (int i = 0; i < ports.Count; i++)
            {
                for (int j = i + 1; j < ports.Count; j++)
                {
                    double angle =
                        GetShopAngleDegrees(
                            ports[i].OutwardDirection,
                            ports[j].OutwardDirection);

                    double error = Math.Abs(180.0 - angle);
                    if (error < bestOppositeError)
                    {
                        bestOppositeError = error;
                        mainA = i;
                        mainB = j;
                    }
                }
            }

            if (mainA < 0 || mainB < 0 || bestOppositeError > 30.0)
                return null;

            int branch = -1;
            double bestBranchError = double.MaxValue;

            for (int i = 0; i < ports.Count; i++)
            {
                if (i == mainA || i == mainB)
                    continue;

                double a90 = Math.Abs(90.0 -
                    GetShopAngleDegrees(
                        ports[mainA].OutwardDirection,
                        ports[i].OutwardDirection));

                double b90 = Math.Abs(90.0 -
                    GetShopAngleDegrees(
                        ports[mainB].OutwardDirection,
                        ports[i].OutwardDirection));

                double error = a90 + b90;
                if (error < bestBranchError)
                {
                    bestBranchError = error;
                    branch = i;
                }
            }

            if (branch < 0 || bestBranchError > 70.0)
                return null;

            Point3d mainMid =
                MidPoint(
                    ports[mainA].Center,
                    ports[mainB].Center);

            Point3d anchor;
            if (!TryIntersectShopAxes(
                    mainMid,
                    ports[mainA].OutwardDirection,
                    ports[branch].Center,
                    ports[branch].OutwardDirection,
                    out anchor))
            {
                anchor = mainMid;
            }

            ShopFittingPlacementInfo info =
                new ShopFittingPlacementInfo
                {
                    Anchor = anchor,
                    BranchPortIndex = 2
                };

            info.Ports.Add(ports[mainA]);
            info.Ports.Add(ports[mainB]);
            info.Ports.Add(ports[branch]);
            return info;
        }

        private bool TryIntersectShopAxes(
            Point3d p1,
            Vector3d d1,
            Point3d p2,
            Vector3d d2,
            out Point3d intersection)
        {
            intersection = MidPoint(p1, p2);

            Vector3d a =
                new Vector3d(d1.X, d1.Y, 0.0);
            Vector3d b =
                new Vector3d(d2.X, d2.Y, 0.0);

            if (a.Length < 1e-9 || b.Length < 1e-9)
                return false;

            double cross = a.X * b.Y - a.Y * b.X;
            if (Math.Abs(cross) < 1e-9)
                return false;

            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;

            double t =
                (dx * b.Y - dy * b.X) / cross;

            intersection =
                new Point3d(
                    p1.X + a.X * t,
                    p1.Y + a.Y * t,
                    0.0);

            return true;
        }

        private bool TryResolveShopFittingTransform(
            string fittingType,
            ShopFittingPlacementInfo info,
            double desiredPrimaryAngle,
            bool desiredMirrorY,
            out double rotation,
            out double mirrorY)
        {
            rotation = desiredPrimaryAngle;
            mirrorY = desiredMirrorY ? -1.0 : 1.0;

            if (info == null || info.Ports.Count == 0)
                return false;

            string key = NormalizeShopKey(fittingType);
            Vector3d desired1 =
                new Vector3d(
                    Math.Cos(desiredPrimaryAngle),
                    Math.Sin(desiredPrimaryAngle),
                    0.0);

            if (IsReducerKey(key))
            {
                if (info.Ports.Count < 2 ||
                    info.LargePortIndex < 0 ||
                    info.SmallPortIndex < 0)
                {
                    return false;
                }

                // Caller truyền desired1 = hướng DN lớn -> DN nhỏ.
                // Vì outward của đầu nhỏ cũng chính là hướng lớn -> nhỏ,
                // căn đầu nhỏ của block theo desired1 là đủ để không đảo reducer.
                Vector3d localSmall =
                    info.Ports[info.SmallPortIndex]
                        .OutwardDirection;

                rotation =
                    desiredPrimaryAngle -
                    Math.Atan2(localSmall.Y, localSmall.X);

                mirrorY = 1.0;
                return true;
            }

            if (IsElbow90Key(key) ||
                IsElbow45Key(key))
            {
                if (info.Ports.Count < 2)
                    return false;

                double included =
                    IsElbow45Key(key) ? 135.0 : 90.0;

                double sign = desiredMirrorY ? -1.0 : 1.0;
                Vector3d desired2 =
                    RotateShopVector(
                        desired1,
                        sign * included);

                return TryMatchShopPortsToDirections(
                    info.Ports.Take(2).ToList(),
                    new List<Vector3d>
                    {
                        desired1,
                        desired2
                    },
                    true,
                    out rotation,
                    out mirrorY);
            }

            if (IsTeeKey(key))
            {
                if (info.Ports.Count < 3)
                    return false;

                Vector3d desiredMain2 = -desired1;
                Vector3d desiredBranch =
                    RotateShopVector(
                        desired1,
                        desiredMirrorY ? -90.0 : 90.0);

                // Ports của info Tê đã được xếp: main1, main2, branch.
                // Cho phép đảo 2 đầu main nhưng không đổi branch.
                List<List<int>> permutations =
                    new List<List<int>>
                    {
                        new List<int> { 0, 1, 2 },
                        new List<int> { 1, 0, 2 }
                    };

                return TryMatchShopPortsToDirections(
                    info.Ports.Take(3).ToList(),
                    new List<Vector3d>
                    {
                        desired1,
                        desiredMain2,
                        desiredBranch
                    },
                    true,
                    out rotation,
                    out mirrorY,
                    permutations);
            }

            return false;
        }

        private bool TryMatchShopPortsToDirections(
            List<ShopFittingPortInfo> ports,
            List<Vector3d> desiredDirections,
            bool allowMirror,
            out double bestRotation,
            out double bestMirrorY,
            List<List<int>> explicitPermutations = null)
        {
            bestRotation = 0.0;
            bestMirrorY = 1.0;

            if (ports == null ||
                desiredDirections == null ||
                ports.Count != desiredDirections.Count ||
                ports.Count == 0)
            {
                return false;
            }

            List<List<int>> permutations =
                explicitPermutations ??
                BuildShopIndexPermutations(ports.Count);

            double bestScore = double.MaxValue;

            foreach (double mirror in
                (allowMirror
                    ? new[] { 1.0, -1.0 }
                    : new[] { 1.0 }))
            {
                foreach (List<int> perm in permutations)
                {
                    if (perm == null || perm.Count != ports.Count)
                        continue;

                    Vector3d local0 =
                        MirrorShopVectorY(
                            ports[perm[0]].OutwardDirection,
                            mirror);

                    Vector3d desired0 =
                        desiredDirections[0].GetNormal();

                    if (local0.Length < 1e-9 ||
                        desired0.Length < 1e-9)
                    {
                        continue;
                    }

                    double candidateRotation =
                        Math.Atan2(desired0.Y, desired0.X) -
                        Math.Atan2(local0.Y, local0.X);

                    double score = 0.0;

                    for (int i = 0; i < ports.Count; i++)
                    {
                        Vector3d local =
                            MirrorShopVectorY(
                                ports[perm[i]].OutwardDirection,
                                mirror);

                        local = local.RotateBy(
                            candidateRotation,
                            Vector3d.ZAxis);

                        score += GetShopAngleDegrees(
                            local,
                            desiredDirections[i]);
                    }

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestRotation = candidateRotation;
                        bestMirrorY = mirror;
                    }
                }
            }

            // Sai tổng quá lớn nghĩa là block được nhận không đúng loại.
            double maxScore = ports.Count * 22.0;
            return bestScore <= maxScore;
        }

        private List<List<int>> BuildShopIndexPermutations(int count)
        {
            List<List<int>> result =
                new List<List<int>>();

            void Recurse(List<int> current, HashSet<int> used)
            {
                if (current.Count == count)
                {
                    result.Add(new List<int>(current));
                    return;
                }

                for (int i = 0; i < count; i++)
                {
                    if (used.Contains(i))
                        continue;

                    used.Add(i);
                    current.Add(i);
                    Recurse(current, used);
                    current.RemoveAt(current.Count - 1);
                    used.Remove(i);
                }
            }

            Recurse(new List<int>(), new HashSet<int>());
            return result;
        }

        private Vector3d MirrorShopVectorY(
            Vector3d vector,
            double mirrorY)
        {
            Vector3d result =
                new Vector3d(
                    vector.X,
                    vector.Y * mirrorY,
                    0.0);

            return result.Length > 1e-9
                ? result.GetNormal()
                : new Vector3d(0.0, 0.0, 0.0);
        }

        private Vector3d RotateShopVector(
            Vector3d vector,
            double degrees)
        {
            if (vector.Length < 1e-9)
                return new Vector3d(0.0, 0.0, 0.0);

            return vector.GetNormal().RotateBy(
                degrees * Math.PI / 180.0,
                Vector3d.ZAxis);
        }

        private int GetShopReducerLargeToSmallLocalSign(
            Transaction tr,
            ObjectId blockId)
        {
            // +1: đầu lớn ở -X, đầu nhỏ ở +X => chiều lớn->nhỏ là +X.
            // -1: đầu lớn ở +X, đầu nhỏ ở -X => chiều lớn->nhỏ là -X.
            // Không đọc chắc chắn được thì giữ +1 để tương thích thư viện cũ.
            if (tr == null || blockId.IsNull)
                return 1;

            try
            {
                BlockTableRecord def =
                    tr.GetObject(
                        blockId,
                        OpenMode.ForRead,
                        false) as BlockTableRecord;

                if (def == null)
                    return 1;

                List<Point2d> samples =
                    new List<Point2d>();

                foreach (ObjectId id in def)
                {
                    Entity ent =
                        tr.GetObject(
                            id,
                            OpenMode.ForRead,
                            false) as Entity;

                    if (ent == null)
                        continue;

                    if (ent is Line line)
                    {
                        samples.Add(
                            new Point2d(
                                line.StartPoint.X,
                                line.StartPoint.Y));
                        samples.Add(
                            new Point2d(
                                line.EndPoint.X,
                                line.EndPoint.Y));
                    }
                    else if (ent is Polyline pl)
                    {
                        for (int i = 0;
                            i < pl.NumberOfVertices;
                            i++)
                        {
                            Point2d pt = pl.GetPoint2dAt(i);
                            samples.Add(pt);
                        }
                    }
                    else if (ent is Arc arc)
                    {
                        samples.Add(
                            new Point2d(
                                arc.StartPoint.X,
                                arc.StartPoint.Y));
                        samples.Add(
                            new Point2d(
                                arc.EndPoint.X,
                                arc.EndPoint.Y));
                    }
                }

                if (samples.Count < 4)
                    return 1;

                double minX = samples.Min(p => p.X);
                double maxX = samples.Max(p => p.X);
                double spanX = maxX - minX;

                if (spanX < 1e-6)
                    return 1;

                double edgeBand =
                    Math.Max(1.0, spanX * 0.15);

                List<Point2d> left =
                    samples.Where(p =>
                        p.X <= minX + edgeBand)
                        .ToList();
                List<Point2d> right =
                    samples.Where(p =>
                        p.X >= maxX - edgeBand)
                        .ToList();

                if (left.Count < 2 || right.Count < 2)
                    return 1;

                double leftSpread =
                    left.Max(p => p.Y) -
                    left.Min(p => p.Y);
                double rightSpread =
                    right.Max(p => p.Y) -
                    right.Min(p => p.Y);

                if (leftSpread > rightSpread * 1.10)
                    return 1;

                if (rightSpread > leftSpread * 1.10)
                    return -1;
            }
            catch { }

            return 1;
        }

        private void NormalizeShopImportedBlock(
            Transaction tr,
            ObjectId blockId)
        {
            if (tr == null || blockId.IsNull)
                return;

            try
            {
                BlockTableRecord blockDef =
                    tr.GetObject(
                        blockId,
                        OpenMode.ForWrite) as BlockTableRecord;

                if (blockDef == null)
                    return;

                foreach (ObjectId id in blockDef)
                {
                    Entity ent =
                        tr.GetObject(
                            id,
                            OpenMode.ForWrite,
                            false) as Entity;

                    if (ent == null)
                        continue;

                    ent.Layer = "0";
                    ent.Color =
                        Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                            ColorMethod.ByBlock,
                            0);
                    ent.LineWeight = LineWeight.LineWeight000;
                    ent.Transparency =
                        new Autodesk.AutoCAD.Colors.Transparency((byte)255);
                    ent.Visible = true;

                    if (ent is Polyline pl)
                        pl.ConstantWidth = 0.0;
                }
            }
            catch { }
        }

        private ObjectId EnsureExternalBlockImported(
            Transaction targetTr,
            Database targetDb,
            string libraryPath,
            string blockName)
        {
            if (targetTr == null ||
                targetDb == null ||
                string.IsNullOrWhiteSpace(libraryPath) ||
                string.IsNullOrWhiteSpace(blockName))
            {
                return ObjectId.Null;
            }

            // QUAN TRỌNG:
            // Không được lấy thẳng block cùng tên đang có trong bản vẽ đích.
            // Thư viện MEP thường có các tên block rất chung; nếu trùng tên với
            // block kiến trúc (cửa, hatch, thiết bị...) thì code cũ có thể chèn
            // nhầm block của bản vẽ hiện tại thay vì block trong file thư viện.
            string safeTargetName =
                BuildShopImportedBlockName(
                    libraryPath,
                    blockName);

            BlockTable targetBt =
                (BlockTable)targetTr.GetObject(
                    targetDb.BlockTableId,
                    OpenMode.ForRead);

            // Chỉ tái sử dụng block đã được chính SHOP import trước đó.
            if (!string.IsNullOrWhiteSpace(safeTargetName) &&
                targetBt.Has(safeTargetName))
            {
                return targetBt[safeTargetName];
            }

            try
            {
                using (Database sourceDb =
                    new Database(false, true))
                {
                    sourceDb.ReadDwgFile(
                        libraryPath,
                        FileOpenMode.OpenForReadAndAllShare,
                        true,
                        "");

                    ObjectId sourceBlockId = ObjectId.Null;

                    using (Transaction sourceTr =
                        sourceDb.TransactionManager
                            .StartTransaction())
                    {
                        BlockTable sourceBt =
                            (BlockTable)sourceTr.GetObject(
                                sourceDb.BlockTableId,
                                OpenMode.ForRead);

                        if (!sourceBt.Has(blockName))
                            return ObjectId.Null;

                        sourceBlockId = sourceBt[blockName];
                        sourceTr.Commit();
                    }

                    ObjectIdCollection ids =
                        new ObjectIdCollection();
                    ids.Add(sourceBlockId);

                    IdMapping mapping =
                        new IdMapping();

                    // MangleName bắt buộc incoming record dùng tên riêng khi
                    // đụng tên với block/layer/style đã có trong bản vẽ đích.
                    // Tuyệt đối không dùng Ignore ở đây vì Ignore sẽ tái dùng
                    // record cùng tên của bản vẽ đích và có thể thành "cửa".
                    sourceDb.WblockCloneObjects(
                        ids,
                        targetDb.BlockTableId,
                        mapping,
                        DuplicateRecordCloning.MangleName,
                        false);

                    ObjectId clonedBlockId =
                        ObjectId.Null;

                    foreach (IdPair pair in mapping)
                    {
                        if (pair.Key == sourceBlockId)
                        {
                            clonedBlockId = pair.Value;
                            break;
                        }
                    }

                    if (clonedBlockId.IsNull ||
                        !clonedBlockId.IsValid)
                    {
                        return ObjectId.Null;
                    }

                    // Đặt tên namespace riêng cho block SHOP để lần chèn kế tiếp
                    // dùng đúng block đã import từ đúng file thư viện.
                    try
                    {
                        BlockTableRecord clonedRecord =
                            targetTr.GetObject(
                                clonedBlockId,
                                OpenMode.ForWrite,
                                false) as BlockTableRecord;

                        if (clonedRecord != null &&
                            !clonedRecord.IsLayout &&
                            !clonedRecord.IsAnonymous &&
                            !string.IsNullOrWhiteSpace(safeTargetName))
                        {
                            targetBt =
                                (BlockTable)targetTr.GetObject(
                                    targetDb.BlockTableId,
                                    OpenMode.ForRead);

                            if (!targetBt.Has(safeTargetName))
                                clonedRecord.Name = safeTargetName;
                        }
                    }
                    catch
                    {
                        // Nếu block động/anonymous không cho đổi tên thì vẫn
                        // dùng ObjectId clone vừa nhận từ IdMapping.
                    }

                    return clonedBlockId;
                }
            }
            catch
            {
                return ObjectId.Null;
            }
        }

        private static string BuildShopImportedBlockName(
            string libraryPath,
            string sourceBlockName)
        {
            string lib =
                Path.GetFileNameWithoutExtension(
                    libraryPath ?? "") ?? "LIB";

            string raw =
                "__SHOPLIB_" +
                lib +
                "_" +
                (sourceBlockName ?? "BLOCK");

            raw =
                BoDauTiengViet(raw)
                    .ToUpperInvariant();

            // Chỉ giữ các ký tự an toàn cho tên SymbolTableRecord.
            raw = Regex.Replace(
                raw,
                @"[^A-Z0-9_\-$]+",
                "_");

            raw = Regex.Replace(
                raw,
                @"_+",
                "_");

            if (raw.Length > 200)
                raw = raw.Substring(0, 200);

            return raw;
        }

        private string FindShopFittingBlockName(
            string libraryPath,
            string fittingType,
            string fromSize,
            string toSize)
        {
            try
            {
                using (Database libDb =
                    new Database(false, true))
                {
                    libDb.ReadDwgFile(
                        libraryPath,
                        FileOpenMode.OpenForReadAndAllShare,
                        true,
                        "");

                    using (Transaction tr =
                        libDb.TransactionManager
                            .StartTransaction())
                    {
                        // Ưu tiên tên block trước. Nếu thư viện đã đặt tên rõ
                        // CO90 / TEE / REDUCER + DN thì đây là cách chắc nhất.
                        string byName =
                            FindShopBlockByName(
                                tr,
                                libDb,
                                fittingType,
                                fromSize,
                                toSize);

                        if (!string.IsNullOrWhiteSpace(byName))
                            return byName;

                        // Chỉ dùng phương pháp đọc "bảng thư viện" theo hàng/cột
                        // làm fallback khi tên block không mang thông tin.
                        string byGrid =
                            FindShopBlockByLibraryGrid(
                                tr,
                                libDb,
                                fittingType,
                                fromSize,
                                toSize);

                        if (!string.IsNullOrWhiteSpace(byGrid))
                            return byGrid;

                        tr.Commit();
                    }
                }
            }
            catch { }

            return "";
        }

        private string FindShopBlockByExactCandidates(
            BlockTable bt,
            string fittingType,
            string fromSize,
            string toSize)
        {
            if (bt == null)
                return "";

            string dn =
                NormalizeShopDnToken(fromSize);
            string dn2 =
                NormalizeShopDnToken(toSize);

            // Số DN thuần: "DN50" -> 50
            double n1 = ParseShopDnNumber(dn);
            double n2 = ParseShopDnNumber(dn2);

            if (n1 <= 0 && n2 <= 0)
                return "";

            // Ống nhỏ (≤50) = ren SCREW; lớn (≥65) = hàn WELD
            // Một số thư viện có cả hai — thử cả hai prefix.
            List<string> prefixes =
                BuildShopConnectionPrefixes(n1, n2);

            List<string> candidates =
                new List<string>();

            string typeKey =
                NormalizeShopKey(fittingType ?? "");

            // ---- CO 90 ----
            if (IsElbow90Key(typeKey) ||
                typeKey == "ELBOW90" ||
                typeKey.Contains("ELBOW90"))
            {
                string sizeTok = dn;
                if (string.IsNullOrWhiteSpace(sizeTok) && n2 > 0)
                    sizeTok = dn2;

                foreach (string p in prefixes)
                {
                    candidates.Add(p + "-ELB90-" + sizeTok);
                    candidates.Add(p + "-ELBOW90-" + sizeTok);
                }
                candidates.Add("ELB90-" + sizeTok);
                candidates.Add("CO90-" + sizeTok);
            }
            // ---- CO 45 ----
            else if (IsElbow45Key(typeKey) ||
                     typeKey == "ELBOW45" ||
                     typeKey.Contains("ELBOW45"))
            {
                string sizeTok = dn;
                if (string.IsNullOrWhiteSpace(sizeTok) && n2 > 0)
                    sizeTok = dn2;

                foreach (string p in prefixes)
                {
                    candidates.Add(p + "-ELB45-" + sizeTok);
                    candidates.Add(p + "-ELBOW45-" + sizeTok);
                }
                candidates.Add("ELB45-" + sizeTok);
                candidates.Add("CO45-" + sizeTok);
                candidates.Add("COLOI45-" + sizeTok);
            }
            // ---- TÊ : SCREW-TEE-DN50x40 / WELD-TEE-DN80x50 ----
            else if (IsTeeKey(typeKey) ||
                     typeKey == "TEE" ||
                     typeKey == "TE")
            {
                // main = lớn hơn, branch = nhỏ hơn (nếu khác size)
                double mainN = n1;
                double branchN = n2 > 0 ? n2 : n1;
                if (n2 > 0 && n1 > 0)
                {
                    mainN = Math.Max(n1, n2);
                    branchN = Math.Min(n1, n2);
                }

                string mainTok = "DN" + FormatShopDnNumber(mainN);
                string branchTok = FormatShopDnNumber(branchN);
                // Dạng thư viện: DN50x40 (không lặp chữ DN ở nhánh)
                string pair = mainTok + "x" + branchTok;
                string pairFull = mainTok + "xDN" + branchTok;
                string same = mainTok + "x" + FormatShopDnNumber(mainN);

                foreach (string p in prefixes)
                {
                    candidates.Add(p + "-TEE-" + pair);
                    candidates.Add(p + "-TEE-" + pairFull);
                    candidates.Add(p + "-TEE-" + same);
                    candidates.Add(p + "-TE-" + pair);
                }
                candidates.Add("TEE-" + pair);
                candidates.Add("TE-" + pair);
            }
            // ---- GIẢM : SCREW-RED-DN50x40 / WELD-RED-DN80x50 ----
            else if (IsReducerKey(typeKey) ||
                     typeKey.Contains("REDUC"))
            {
                if (n1 > 0 && n2 > 0 &&
                    Math.Abs(n1 - n2) > 0.01)
                {
                    double big = Math.Max(n1, n2);
                    double small = Math.Min(n1, n2);
                    string bigTok = "DN" + FormatShopDnNumber(big);
                    string smallTok = FormatShopDnNumber(small);
                    string pair = bigTok + "x" + smallTok;
                    string pairFull = bigTok + "xDN" + smallTok;

                    foreach (string p in prefixes)
                    {
                        candidates.Add(p + "-RED-" + pair);
                        candidates.Add(p + "-RED-" + pairFull);
                        candidates.Add(p + "-REDUCER-" + pair);
                        candidates.Add(p + "-GIAM-" + pair);
                    }
                    candidates.Add("RED-" + pair);
                    candidates.Add("REDUCER-" + pair);
                    candidates.Add("GIAM-" + pair);
                }
            }
            // ---- NẮP / CAP ----
            else if (typeKey.Contains("CAP") ||
                     typeKey.Contains("NAP"))
            {
                string sizeTok = dn;
                if (string.IsNullOrWhiteSpace(sizeTok) && n2 > 0)
                    sizeTok = dn2;

                foreach (string p in prefixes)
                    candidates.Add(p + "-CAP-" + sizeTok);
                candidates.Add("CAP-" + sizeTok);
            }

            // Build name map từ BlockTable
            Dictionary<string, string> map =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);

            Transaction tr =
                bt.Database.TransactionManager.TopTransaction;

            if (tr == null)
                return "";

            foreach (ObjectId id in bt)
            {
                BlockTableRecord record = null;
                try
                {
                    record =
                        tr.GetObject(id, OpenMode.ForRead, false)
                            as BlockTableRecord;
                }
                catch
                {
                    continue;
                }

                if (record == null ||
                    record.IsLayout ||
                    record.IsAnonymous ||
                    record.IsFromExternalReference)
                {
                    continue;
                }

                string name = record.Name ?? "";
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                map[name] = name;
                map[NormalizeShopKey(name)] = name;
            }

            // 1) Khớp đúng tên candidate
            foreach (string cand in candidates)
            {
                if (string.IsNullOrWhiteSpace(cand))
                    continue;

                if (map.TryGetValue(cand, out string hit))
                    return hit;

                string norm = NormalizeShopKey(cand);
                if (map.TryGetValue(norm, out hit))
                    return hit;
            }

            // 2) Khớp lỏng theo Normalize key + đúng DN
            foreach (string cand in candidates)
            {
                string normCand = NormalizeShopKey(cand);
                if (string.IsNullOrWhiteSpace(normCand))
                    continue;

                foreach (KeyValuePair<string, string> kv in map)
                {
                    string normName = NormalizeShopKey(kv.Value);
                    if (normName == normCand)
                        return kv.Value;
                }
            }

            // 3) Fallback: block chứa type + đủ size (SCREWELB90DN50, WELDREDDN80X50...)
            foreach (KeyValuePair<string, string> kv in map)
            {
                string normName = NormalizeShopKey(kv.Value);
                if (!IsShopFittingTypeText(fittingType, kv.Value))
                    continue;

                bool okFrom =
                    string.IsNullOrWhiteSpace(fromSize) ||
                    ShopKeyContainsSize(kv.Value, fromSize);
                bool okTo =
                    string.IsNullOrWhiteSpace(toSize) ||
                    string.Equals(
                        CleanLayerText(fromSize ?? ""),
                        CleanLayerText(toSize ?? ""),
                        StringComparison.OrdinalIgnoreCase) ||
                    ShopKeyContainsSize(kv.Value, toSize);

                if (okFrom && okTo)
                    return kv.Value;
            }

            return "";
        }

        private static List<string> BuildShopConnectionPrefixes(
            double sizeA,
            double sizeB)
        {
            List<string> result = new List<string>();

            double refSize =
                sizeA > 0 && sizeB > 0
                    ? Math.Max(sizeA, sizeB)
                    : (sizeA > 0 ? sizeA : sizeB);

            // ≤50: ưu tiên SCREW; ≥65: ưu tiên WELD; thử cả hai
            if (refSize > 0 && refSize <= 50.0)
            {
                result.Add("SCREW");
                result.Add("WELD");
            }
            else if (refSize >= 65.0)
            {
                result.Add("WELD");
                result.Add("SCREW");
            }
            else
            {
                // DN50–65 vùng chuyển
                result.Add("SCREW");
                result.Add("WELD");
            }

            return result;
        }

        private static double ParseShopDnNumber(string dnToken)
        {
            if (string.IsNullOrWhiteSpace(dnToken))
                return 0;

            Match m =
                Regex.Match(
                    dnToken,
                    @"(\d+(?:[.,]\d+)?)",
                    RegexOptions.IgnoreCase);

            if (!m.Success)
                return 0;

            if (double.TryParse(
                    m.Groups[1].Value.Replace(',', '.'),
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out double v))
            {
                return v;
            }

            return 0;
        }

        private static string FormatShopDnNumber(double n)
        {
            if (n <= 0)
                return "";

            // DN nguyên phổ biến: 15,20,25,32,40,50,65,80,100...
            if (Math.Abs(n - Math.Round(n)) < 0.01)
                return ((int)Math.Round(n)).ToString(
                    CultureInfo.InvariantCulture);

            return n.ToString(
                "0.##",
                CultureInfo.InvariantCulture);
        }

        private static string NormalizeShopDnToken(string sizeText)
        {
            string raw = (sizeText ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(raw))
                return "";

            // "DN50" / "50" / "dn 50" -> "DN50"
            Match m =
                Regex.Match(
                    raw,
                    @"DN\s*(\d{1,4}(?:\.\d+)?)",
                    RegexOptions.IgnoreCase);

            if (m.Success)
                return "DN" + m.Groups[1].Value.Replace(',', '.');

            m = Regex.Match(raw, @"(\d{1,4}(?:\.\d+)?)");
            if (m.Success)
                return "DN" + m.Groups[1].Value.Replace(',', '.');

            return NormalizeShopKey(raw);
        }

        private string FindShopBlockByName(
            Transaction tr,
            Database db,
            string fittingType,
            string fromSize,
            string toSize)
        {
            BlockTable bt =
                (BlockTable)tr.GetObject(
                    db.BlockTableId,
                    OpenMode.ForRead);

            // Ưu tiên tên block đúng chuẩn thư viện user:
            // SCREW-ELB90-DN15, SCREW-ELB45-DN50, SCREW-TEE-DN40...
            string exact =
                FindShopBlockByExactCandidates(
                    bt,
                    fittingType,
                    fromSize,
                    toSize);

            if (!string.IsNullOrWhiteSpace(exact))
                return exact;

            string bestName = "";
            int bestScore = -1;

            foreach (ObjectId id in bt)
            {
                BlockTableRecord record =
                    tr.GetObject(
                        id,
                        OpenMode.ForRead,
                        false) as BlockTableRecord;

                if (record == null ||
                    record.IsLayout ||
                    record.IsAnonymous ||
                    record.IsFromExternalReference)
                {
                    continue;
                }

                string name =
                    record.Name ?? "";

                // Tên block phải thể hiện ĐÚNG loại phụ kiện.
                // Không còn cho phép "chỉ trùng DN" đạt đủ điểm rồi bị chọn.
                // Nếu thư viện dùng tên generic (Block1, A$C...) thì hàm này
                // trả rỗng và chuyển sang dò theo bảng thư viện có kiểm soát.
                if (!IsShopFittingTypeText(
                        fittingType,
                        name))
                {
                    continue;
                }

                int score = 100;

                bool sameSize =
                    string.Equals(
                        CleanLayerText(
                            fromSize ?? ""),
                        CleanLayerText(
                            toSize ?? ""),
                        StringComparison.OrdinalIgnoreCase);

                bool hasFrom =
                    ShopKeyContainsSize(
                        name,
                        fromSize);

                bool hasTo =
                    ShopKeyContainsSize(
                        name,
                        toSize);

                if (hasFrom)
                    score += 30;

                if (sameSize)
                {
                    if (hasFrom)
                        score += 20;
                }
                else if (hasTo)
                {
                    score += 30;
                }

                if (ShopSizeLabelMatches(
                        name,
                        fromSize,
                        toSize))
                {
                    score += 40;
                }

                // Với giảm/tê giảm cần cả hai DN nếu tên block có DN.
                // Nếu tên không có DN thì vẫn cho phép fallback grid xử lý,
                // không tự chèn dựa vào tên loại chung chung.
                bool nameHasAnyDn =
                    Regex.IsMatch(
                        NormalizeShopKey(name),
                        @"(?:DN)?\d{1,4}");

                if (nameHasAnyDn)
                {
                    if (sameSize && !hasFrom)
                        continue;

                    if (!sameSize &&
                        (!hasFrom || !hasTo))
                    {
                        continue;
                    }
                }
                else
                {
                    // Tên chỉ nói loại phụ kiện nhưng không nói size:
                    // không đủ chắc chắn để lấy thẳng theo tên.
                    continue;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestName = name;
                }
            }

            return bestName;
        }

        private string FindShopBlockByLibraryGrid(
            Transaction tr,
            Database db,
            string fittingType,
            string fromSize,
            string toSize)
        {
            BlockTable bt =
                (BlockTable)tr.GetObject(
                    db.BlockTableId,
                    OpenMode.ForRead);

            BlockTableRecord ms =
                (BlockTableRecord)tr.GetObject(
                    bt[BlockTableRecord.ModelSpace],
                    OpenMode.ForRead);

            List<Tuple<string, Point3d>> texts =
                new List<Tuple<string, Point3d>>();

            List<BlockReference> blocks =
                new List<BlockReference>();

            foreach (ObjectId id in ms)
            {
                Entity ent =
                    tr.GetObject(
                        id,
                        OpenMode.ForRead,
                        false) as Entity;

                if (ent is DBText dbText)
                {
                    texts.Add(
                        Tuple.Create(
                            dbText.TextString ?? "",
                            dbText.Position));
                }
                else if (ent is MText mText)
                {
                    texts.Add(
                        Tuple.Create(
                            LayPlainTextTuMText(
                                mText.Contents),
                            mText.Location));
                }
                else if (ent is BlockReference br)
                {
                    // Bỏ xref/layout và các reference không có definition hợp lệ.
                    try
                    {
                        BlockTableRecord def =
                            tr.GetObject(
                                br.BlockTableRecord,
                                OpenMode.ForRead,
                                false) as BlockTableRecord;

                        if (def == null ||
                            def.IsLayout ||
                            def.IsFromExternalReference)
                        {
                            continue;
                        }
                    }
                    catch
                    {
                        continue;
                    }

                    blocks.Add(br);
                }
            }

            if (texts.Count == 0 ||
                blocks.Count == 0)
            {
                return "";
            }

            List<Tuple<string, Point3d>> typeLabels =
                texts.Where(t =>
                    IsShopFittingTypeText(
                        fittingType,
                        t.Item1))
                    .ToList();

            List<Tuple<string, Point3d>> sizeLabels =
                texts.Where(t =>
                    ShopSizeLabelMatches(
                        t.Item1,
                        fromSize,
                        toSize))
                    .ToList();

            if (typeLabels.Count == 0 ||
                sizeLabels.Count == 0)
            {
                return "";
            }

            // Code cũ dùng:
            //   min khoảng cách Y tới BẤT KỲ label loại
            // + min khoảng cách X tới BẤT KỲ label size.
            // Hai label đó có thể thuộc hai bảng khác nhau -> chọn nhầm block.
            //
            // Cách mới: tạo giao điểm thật của từng cặp
            // (cột SIZE.X, hàng TYPE.Y), rồi tìm block gần giao điểm đó nhất.
            BlockReference bestBlock = null;
            double bestDistance =
                double.MaxValue;

            foreach (Tuple<string, Point3d> typeLabel
                in typeLabels)
            {
                foreach (Tuple<string, Point3d> sizeLabel
                    in sizeLabels)
                {
                    Point3d expectedCell =
                        new Point3d(
                            sizeLabel.Item2.X,
                            typeLabel.Item2.Y,
                            0.0);

                    foreach (BlockReference br in blocks)
                    {
                        double dx =
                            br.Position.X -
                            expectedCell.X;

                        double dy =
                            br.Position.Y -
                            expectedCell.Y;

                        double dist =
                            Math.Sqrt(
                                dx * dx +
                                dy * dy);

                        if (dist < bestDistance)
                        {
                            bestDistance = dist;
                            bestBlock = br;
                        }
                    }
                }
            }

            if (bestBlock == null)
                return "";

            // Không "đoán đại": nếu block nằm quá xa ô hàng/cột dự kiến,
            // trả về rỗng để SHOP vẽ fallback bằng line thay vì chèn sai block.
            double gridSpacing =
                EstimateShopLibraryBlockSpacing(
                    blocks);

            double maxAllowedDistance =
                gridSpacing > 1.0
                    ? Math.Max(
                        250.0,
                        gridSpacing * 0.40)
                    : 500.0;

            if (bestDistance >
                maxAllowedDistance)
            {
                return "";
            }

            BlockTableRecord blockDef =
                tr.GetObject(
                    bestBlock.BlockTableRecord,
                    OpenMode.ForRead,
                    false) as BlockTableRecord;

            if (blockDef == null)
                return "";

            // Loại nhanh các block có tên thể hiện rõ là kiến trúc/nội thất.
            // Tên generic vẫn được phép vì nhiều thư viện MEP dùng tên Block1...
            if (IsObviouslyNonShopFittingBlockName(
                    blockDef.Name))
            {
                return "";
            }

            return blockDef.Name ?? "";
        }

        private static double EstimateShopLibraryBlockSpacing(
            List<BlockReference> blocks)
        {
            if (blocks == null ||
                blocks.Count < 2)
            {
                return 0.0;
            }

            List<double> nearestDistances =
                new List<double>();

            for (int i = 0;
                i < blocks.Count;
                i++)
            {
                Point3d a =
                    blocks[i].Position;

                double nearest =
                    double.MaxValue;

                for (int j = 0;
                    j < blocks.Count;
                    j++)
                {
                    if (i == j)
                        continue;

                    Point3d b =
                        blocks[j].Position;

                    double dx = a.X - b.X;
                    double dy = a.Y - b.Y;

                    double d =
                        Math.Sqrt(
                            dx * dx +
                            dy * dy);

                    if (d > 1.0 &&
                        d < nearest)
                    {
                        nearest = d;
                    }
                }

                if (nearest < double.MaxValue)
                    nearestDistances.Add(nearest);
            }

            if (nearestDistances.Count == 0)
                return 0.0;

            nearestDistances.Sort();

            int mid =
                nearestDistances.Count / 2;

            if (nearestDistances.Count % 2 == 1)
                return nearestDistances[mid];

            return
                (nearestDistances[mid - 1] +
                 nearestDistances[mid]) /
                2.0;
        }

        private static bool IsObviouslyNonShopFittingBlockName(
            string blockName)
        {
            string key =
                NormalizeShopKey(
                    blockName ?? "");

            if (string.IsNullOrWhiteSpace(key))
                return false;

            string[] rejectTokens =
            {
                "DOOR",
                "WINDOW",
                "CUA",
                "FURN",
                "FURNITURE",
                "TABLE",
                "CHAIR",
                "BED",
                "WC",
                "SANITARY",
                "HATCH",
                "TITLEBLOCK"
            };

            return rejectTokens.Any(
                token =>
                    key.Contains(token));
        }

        private static bool IsShopFittingTypeText(
            string fittingType,
            string text)
        {
            string typeKey = NormalizeShopKey(fittingType);
            string key = NormalizeShopKey(text);

            if (IsReducerKey(typeKey))
            {
                return key.Contains("REDUC") ||
                       key.Contains("GIAM") ||
                       key.Contains("BAUGIAM") ||
                       key.Contains("CONGIAM") ||
                       key.Contains("CONTHU") ||
                       key.Contains("RED") ||
                       key.Contains("SOCKET") && key.Contains("RED");
            }

            if (IsElbow90Key(typeKey))
            {
                // Thư viện user: SCREW-ELB90-DN15, SCREW-ELB90-DN50...
                return key.Contains("ELB90") ||
                       key.Contains("ELBOW90") ||
                       key.Contains("EL90") ||
                       key.Contains("CO90") ||
                       key.Contains("C90") ||
                       (key.Contains("CO") && key.Contains("90") &&
                        !key.Contains("ELB45") && !key.Contains("45"));
            }

            if (IsElbow45Key(typeKey))
            {
                // Thư viện user: SCREW-ELB45-DN15...
                return key.Contains("ELB45") ||
                       key.Contains("ELBOW45") ||
                       key.Contains("EL45") ||
                       key.Contains("CO45") ||
                       key.Contains("COLOI") ||
                       key.Contains("LOI45") ||
                       key.Contains("C45") ||
                       (key.Contains("CO") && key.Contains("45"));
            }

            if (IsTeeKey(typeKey))
            {
                if (key == "TE" || key == "TEE")
                    return true;
                if (key.Contains("TEE") ||
                    key.Contains("TEGIAM") ||
                    key.Contains("TETHU"))
                    return true;
                // SCREW-TEE-DN50, SCREW-TE-DN50
                if (key.Contains("SCREW") &&
                    (key.Contains("TEE") || key.Contains("TEDN")))
                    return true;
                if (key.Contains("TEDN"))
                    return true;
                if (key.StartsWith("TE") &&
                    (key.Contains("DN") ||
                     System.Text.RegularExpressions.Regex.IsMatch(key, @"^TE\d")))
                    return true;
                return false;
            }

            // Nắp / cap
            if (typeKey.Contains("CAP") || typeKey.Contains("NAP"))
            {
                return key.Contains("CAP") ||
                       key.Contains("NAP") ||
                       key.Contains("ENDCAP");
            }

            return key.Contains(typeKey);
        }

        private bool ShopSizeLabelMatches(
            string text,
            string fromSize,
            string toSize)
        {
            if (!ShopKeyContainsSize(text, fromSize))
                return false;

            string a = CleanLayerText(fromSize ?? "");
            string b = CleanLayerText(toSize ?? "");

            if (string.Equals(
                    a,
                    b,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return ShopKeyContainsSize(text, toSize);
        }

        private static bool ShopKeyContainsSize(
            string text,
            string sizeText)
        {
            string key = NormalizeShopKey(text);
            string sizeKey = NormalizeShopKey(sizeText);

            Match match =
                Regex.Match(
                    sizeKey,
                    @"(?:DN)?(?<N>\d{1,4}(?:\.\d+)?)");

            if (!match.Success)
                return key.Contains(sizeKey);

            string number =
                Regex.Escape(match.Groups["N"].Value);

            return Regex.IsMatch(
                key,
                @"(?<!\d)(?:DN)?" + number + @"(?!\d)",
                RegexOptions.IgnoreCase);
        }

        private static bool IsReducerKey(string key)
        {
            key = NormalizeShopKey(key);
            return key.Contains("REDUC") ||
                   key.Contains("GIAM") ||
                   key.Contains("RED");
        }

        private static bool IsElbow90Key(string key)
        {
            key = NormalizeShopKey(key);
            return key.Contains("ELB90") ||
                   key.Contains("ELBOW90") ||
                   key.Contains("EL90") ||
                   key.Contains("CO90") ||
                   key.Contains("C90");
        }

        private static bool IsElbow45Key(string key)
        {
            key = NormalizeShopKey(key);
            return key.Contains("ELB45") ||
                   key.Contains("ELBOW45") ||
                   key.Contains("EL45") ||
                   key.Contains("CO45") ||
                   key.Contains("COLOI") ||
                   key.Contains("LOI45") ||
                   key.Contains("C45");
        }

        private static bool IsTeeKey(string key)
        {
            key = NormalizeShopKey(key);
            return key == "TEE" ||
                   key == "TE" ||
                   key.Contains("TEE");
        }

        private static bool IsReducerText(string text)
        {
            return IsShopFittingTypeText("REDUCER", text);
        }

        private static string NormalizeShopKey(string text)
        {
            string u =
                BoDauTiengViet(text ?? "")
                    .ToUpperInvariant();

            return Regex.Replace(
                u,
                @"[^A-Z0-9]+",
                "");
        }

        private void EnsureShopLayerExists(
            Transaction tr,
            Database db,
            string layerName)
        {
            EnsureLayerExists(tr, db, layerName, false);

            try
            {
                LayerTable lt =
                    (LayerTable)tr.GetObject(
                        db.LayerTableId,
                        OpenMode.ForRead);

                if (!lt.Has(layerName))
                    return;

                LayerTableRecord ltr =
                    (LayerTableRecord)tr.GetObject(
                        lt[layerName],
                        OpenMode.ForWrite);

                ltr.LineWeight = LineWeight.LineWeight000;
                ltr.Color =
                    Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                        ColorMethod.ByAci,
                        GetShopVisibleAciColor(layerName));
                ltr.Transparency =
                    new Autodesk.AutoCAD.Colors.Transparency((byte)255);
                ltr.IsOff = false;
                ltr.IsFrozen = false;
                ltr.IsLocked = false;
            }
            catch { }
        }

        private static void ApplyShopEntityStyle(
            Entity ent,
            string layerName)
        {
            if (ent == null)
                return;

            ent.Layer = layerName;
            ent.Color =
                Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                    ColorMethod.ByAci,
                    GetShopVisibleAciColor(layerName));
            ent.LineWeight = LineWeight.LineWeight000;
            ent.Transparency =
                new Autodesk.AutoCAD.Colors.Transparency((byte)255);
            ent.Visible = true;

            if (ent is Polyline pl)
                pl.ConstantWidth = 0.0;
        }

        private static short GetShopVisibleAciColor(string layerName)
        {
            string key =
                NormalizeShopKey(layerName ?? "");

            if (key.Contains("DN15"))
                return 2;
            if (key.Contains("DN20"))
                return 3;
            if (key.Contains("DN25"))
                return 4;
            if (key.Contains("DN32"))
                return 5;
            if (key.Contains("DN40"))
                return 3;
            if (key.Contains("DN50"))
                return 1;
            if (key.Contains("DN65"))
                return 6;
            if (key.Contains("DN80"))
                return 4;
            if (key.Contains("DN100"))
                return 2;
            if (key.Contains("DN125"))
                return 5;
            if (key.Contains("DN150"))
                return 1;

            if (key.Contains("GIAM") ||
                key.Contains("REDUC"))
                return 7;

            return 7;
        }

        /// <summary>
        /// Layer do tool tạo luôn bắt đầu bằng mã hệ thống: FF_ / ACMV_ / CTN_
        /// </summary>
        private static bool LaLayerCuaTool(string layerName)
        {
            if (string.IsNullOrWhiteSpace(layerName))
                return false;

            string u = layerName.Trim().ToUpperInvariant();

            return u.StartsWith("FF_") ||
                   u.StartsWith("ACMV_") ||
                   u.StartsWith("CTN_") ||
                   u.StartsWith("CHUA_CHAY_") ||
                   u.StartsWith("CHỮA_CHÁY_");
        }

        /// <summary>
        /// Phân biệt layer ống (vật liệu + DN) với layer van / thiết bị.
        /// </summary>
        private static string BoDauTiengViet(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";

            string u = s.ToUpperInvariant();
            // A
            u = u.Replace("Á", "A").Replace("À", "A").Replace("Ả", "A")
                 .Replace("Ã", "A").Replace("Ạ", "A")
                 .Replace("Ă", "A").Replace("Ắ", "A").Replace("Ằ", "A")
                 .Replace("Ẳ", "A").Replace("Ẵ", "A").Replace("Ặ", "A")
                 .Replace("Â", "A").Replace("Ấ", "A").Replace("Ầ", "A")
                 .Replace("Ẩ", "A").Replace("Ẫ", "A").Replace("Ậ", "A");
            // E
            u = u.Replace("É", "E").Replace("È", "E").Replace("Ẻ", "E")
                 .Replace("Ẽ", "E").Replace("Ẹ", "E")
                 .Replace("Ê", "E").Replace("Ế", "E").Replace("Ề", "E")
                 .Replace("Ể", "E").Replace("Ễ", "E").Replace("Ệ", "E");
            // O
            u = u.Replace("Ó", "O").Replace("Ò", "O").Replace("Ỏ", "O")
                 .Replace("Õ", "O").Replace("Ọ", "O")
                 .Replace("Ô", "O").Replace("Ố", "O").Replace("Ồ", "O")
                 .Replace("Ổ", "O").Replace("Ỗ", "O").Replace("Ộ", "O")
                 .Replace("Ơ", "O").Replace("Ớ", "O").Replace("Ờ", "O")
                 .Replace("Ở", "O").Replace("Ỡ", "O").Replace("Ợ", "O");
            // U
            u = u.Replace("Ú", "U").Replace("Ù", "U").Replace("Ủ", "U")
                 .Replace("Ũ", "U").Replace("Ụ", "U")
                 .Replace("Ư", "U").Replace("Ứ", "U").Replace("Ừ", "U")
                 .Replace("Ử", "U").Replace("Ữ", "U").Replace("Ự", "U");
            // I Y D
            u = u.Replace("Í", "I").Replace("Ì", "I").Replace("Ỉ", "I")
                 .Replace("Ĩ", "I").Replace("Ị", "I")
                 .Replace("Ý", "Y").Replace("Ỳ", "Y").Replace("Ỷ", "Y")
                 .Replace("Ỹ", "Y").Replace("Ỵ", "Y")
                 .Replace("Đ", "D");
            return u;
        }

        private static bool LaLayerOng(string layerName)
        {
            if (string.IsNullOrWhiteSpace(layerName))
                return false;

            // Nếu đã nhận là van/thiết bị → không coi là ống
            if (LaLayerThietBiHoacVan(layerName))
                return false;

            string u = BoDauTiengViet(layerName.Trim());

            string[] vatLieuOng =
            {
                "TRANG KEM", "TRANGKEM", "HDPE", "THEP DEN", "THEPDEN",
                "INOX", "NHUNG NONG", "NHUNGNONG", "UPVC", "ONG DONG",
                "ONGDONG", "OG THAI", "OG HUT", "OG LANH", "OG CAP",
                "OG HOI", "PPR", "PVC", "PEHD", "THEP"
            };

            foreach (var vl in vatLieuOng)
            {
                if (u.Contains(vl))
                    return true;
            }

            // Size ống gió WxH trên layer OG
            if (Regex.IsMatch(u, @"\d+\s*X\s*\d+") &&
                (u.Contains("OG ") || u.Contains("OG_") || u.Contains("_OG")))
                return true;

            // DN + vật liệu ống (không phải van)
            if (Regex.IsMatch(u, @"DN\s*\d+") &&
                (u.Contains("TRANG") || u.Contains("HDPE") ||
                 u.Contains("THEP") || u.Contains("INOX") ||
                 u.Contains("UPVC") || u.Contains("DONG")))
                return true;

            return false;
        }

        private static bool LaLayerThietBiHoacVan(string layerName)
        {
            if (string.IsNullOrWhiteSpace(layerName))
                return false;

            string u = BoDauTiengViet(layerName.Trim());

            string[] keywords =
            {
                // Van (kể cả V.CỔNG sau khi bỏ dấu → V.CONG)
                "VAN", "V.CONG", "VCONG", "V. CONG", "CONG TN", "CONG TC",
                "CONG DIEN", "Y LOC", "YLOC", "KNM",
                "VCD", "MFD", "PRD", "LOUVER", "DAMPER",
                "MG CAP", "MG THAI", "VAN 1C", "VAN1C",
                // Thiết bị FF / ACMV / CTN
                "BINH", "DAU PHUN", "DAUPHUN", "PHUN",
                "MAY LANH", "MAYLANH", "QUAT", "BOM",
                "DONG HO", "DONGHO", "BON NUOC", "BON ",
                "CASSETTE", "GAN TUONG", "AM TRAN", "AP TRAN", "DAN NONG",
                "HL-", "HX-", "HN-", " HL ", " HX ", " HN ",
                "BE NUOC", "BE GOI", "THIET BI"
            };

            foreach (var k in keywords)
            {
                if (u.Contains(k))
                    return true;
            }

            return false;
        }


        // ============================================================
        // THỐNG KÊ PHỤ KIỆN VẼ SHOP CHỮA CHÁY
        // Chỉ đọc BlockReference nằm trên các layer FF_SHOP_* phụ kiện.
        // Không can thiệp vào logic VẼ SHOP / chèn reducer / co / tê.
        // ============================================================

        private class ShopFittingStatRow
        {
            public int STT { get; set; }
            public string Loai { get; set; }
            public string KichThuoc { get; set; }
            public string KieuNoi { get; set; }
            public double SoLuong { get; set; }
            public int LoaiSort { get; set; }
            public double KichThuocSort { get; set; }
        }

        private void BtnThongKePhuKienShop_Click(
            object sender,
            RoutedEventArgs e)
        {
            var doc =
                Autodesk.AutoCAD.ApplicationServices.Core.Application
                    .DocumentManager
                    .MdiActiveDocument;

            if (doc == null)
                return;

            var ed = doc.Editor;
            var db = doc.Database;

            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();

            using (doc.LockDocument())
            {
                PromptSelectionOptions pso =
                    new PromptSelectionOptions();

                pso.MessageForAdding =
                    "\n[THỐNG KÊ PHỤ KIỆN SHOP] " +
                    "Quét chọn khu vực có Co / Tê / Giảm cần thống kê: ";

                SelectionFilter filter =
                    new SelectionFilter(
                        new TypedValue[]
                        {
                            new TypedValue(
                                (int)DxfCode.Start,
                                "INSERT")
                        });

                PromptSelectionResult psr =
                    ed.GetSelection(
                        pso,
                        filter);

                if (psr.Status != PromptStatus.OK ||
                    psr.Value == null ||
                    psr.Value.Count == 0)
                {
                    return;
                }

                Dictionary<string, ShopFittingStatRow> dict =
                    new Dictionary<string, ShopFittingStatRow>(
                        StringComparer.OrdinalIgnoreCase);

                using (Transaction tr =
                    db.TransactionManager.StartTransaction())
                {
                    foreach (SelectedObject so in psr.Value)
                    {
                        if (so == null ||
                            so.ObjectId.IsNull)
                        {
                            continue;
                        }

                        BlockReference br =
                            tr.GetObject(
                                so.ObjectId,
                                OpenMode.ForRead,
                                false) as BlockReference;

                        if (br == null ||
                            br.IsErased)
                        {
                            continue;
                        }

                        string layer =
                            br.Layer ?? "";

                        if (!TryGetShopFittingStatIdentity(
                                tr,
                                br,
                                layer,
                                out string loai,
                                out string kichThuoc,
                                out string kieuNoi,
                                out int loaiSort,
                                out double sizeSort))
                        {
                            continue;
                        }

                        string key =
                            loai + "|" +
                            kichThuoc + "|" +
                            kieuNoi;

                        if (!dict.TryGetValue(
                                key,
                                out ShopFittingStatRow row))
                        {
                            row =
                                new ShopFittingStatRow
                                {
                                    Loai = loai,
                                    KichThuoc = kichThuoc,
                                    KieuNoi = kieuNoi,
                                    SoLuong = 0.0,
                                    LoaiSort = loaiSort,
                                    KichThuocSort = sizeSort
                                };

                            dict[key] = row;
                        }

                        row.SoLuong += 1.0;
                    }

                    tr.Commit();
                }

                if (dict.Count == 0)
                {
                    MessageBox.Show(
                        "Không tìm thấy phụ kiện SHOP trong vùng chọn.\n\n" +
                        "Chỉ đếm block trên các layer:\n" +
                        "FF_SHOP_CO90_...\n" +
                        "FF_SHOP_COLOI45_...\n" +
                        "FF_SHOP_TE_...\n" +
                        "FF_SHOP_GIAM_...",
                        "THỐNG KÊ PHỤ KIỆN SHOP");
                    return;
                }

                List<ShopFittingStatRow> data =
                    dict.Values
                        .OrderBy(x => x.LoaiSort)
                        .ThenByDescending(x => x.KichThuocSort)
                        .ThenBy(x => x.KichThuoc)
                        .ThenBy(x => x.KieuNoi)
                        .ToList();

                for (int i = 0; i < data.Count; i++)
                    data[i].STT = i + 1;

                double tong =
                    data.Sum(x => x.SoLuong);

                ed.WriteMessage(
                    $"\n[THỐNG KÊ PHỤ KIỆN SHOP] " +
                    $"Tìm thấy {FormatSoThongKe(tong)} phụ kiện, " +
                    $"{data.Count} chủng loại/kích thước.");

                XuatBangThongKePhuKienShop(
                    data);
            }
        }

        private bool TryGetShopFittingStatIdentity(
            Transaction tr,
            BlockReference br,
            string layer,
            out string loai,
            out string kichThuoc,
            out string kieuNoi,
            out int loaiSort,
            out double sizeSort)
        {
            loai = "";
            kichThuoc = "";
            kieuNoi = "";
            loaiSort = 999;
            sizeSort = 0.0;

            if (tr == null ||
                br == null)
            {
                return false;
            }

            string layerUpper =
                (layer ?? "")
                    .Trim()
                    .ToUpperInvariant();

            // Chỉ nhận đúng phụ kiện do VẼ SHOP tạo.
            // Thứ tự phải kiểm tra CO90_DAUPHUN trước CO90 thường.
            if (layerUpper.StartsWith(
                    "FF_SHOP_CO90_DAUPHUN_",
                    StringComparison.OrdinalIgnoreCase))
            {
                loai = "CO 90 ĐẦU PHUN";
                loaiSort = 11;
                kichThuoc =
                    ExtractShopStatSingleSize(
                        layer,
                        "FF_SHOP_CO90_DAUPHUN_");
            }
            else if (layerUpper.StartsWith(
                         "FF_SHOP_CO90_",
                         StringComparison.OrdinalIgnoreCase))
            {
                loai = "CO 90";
                loaiSort = 10;
                kichThuoc =
                    ExtractShopStatSingleSize(
                        layer,
                        "FF_SHOP_CO90_");
            }
            else if (layerUpper.StartsWith(
                         "FF_SHOP_COLOI45_",
                         StringComparison.OrdinalIgnoreCase))
            {
                loai = "CO 45 / CO LƠI";
                loaiSort = 20;
                kichThuoc =
                    ExtractShopStatSingleSize(
                        layer,
                        "FF_SHOP_COLOI45_");
            }
            else if (layerUpper.StartsWith(
                         "FF_SHOP_TE_",
                         StringComparison.OrdinalIgnoreCase))
            {
                loai = "TÊ";
                loaiSort = 30;
                kichThuoc =
                    ExtractShopStatPairSize(
                        layer,
                        "FF_SHOP_TE_");
            }
            else if (layerUpper.StartsWith(
                         "FF_SHOP_GIAM_",
                         StringComparison.OrdinalIgnoreCase))
            {
                loai = "GIẢM";
                loaiSort = 40;
                kichThuoc =
                    ExtractShopStatPairSize(
                        layer,
                        "FF_SHOP_GIAM_");
            }
            else
            {
                return false;
            }

            string blockName =
                GetShopStatBlockName(
                    tr,
                    br);

            kieuNoi =
                GetShopStatConnectionType(
                    blockName);

            // Nếu layer cũ thiếu size, lấy lại từ tên block.
            if (string.IsNullOrWhiteSpace(kichThuoc))
            {
                kichThuoc =
                    ExtractShopStatSizeFromBlockName(
                        blockName,
                        loai);
            }

            if (string.IsNullOrWhiteSpace(kichThuoc))
                kichThuoc = "-";

            sizeSort =
                GetShopStatSortSize(
                    kichThuoc);

            return true;
        }

        private string GetShopStatBlockName(
            Transaction tr,
            BlockReference br)
        {
            if (tr == null ||
                br == null)
            {
                return "";
            }

            try
            {
                ObjectId defId =
                    br.BlockTableRecord;

                if (br.IsDynamicBlock &&
                    !br.DynamicBlockTableRecord.IsNull &&
                    br.DynamicBlockTableRecord.IsValid)
                {
                    defId =
                        br.DynamicBlockTableRecord;
                }

                BlockTableRecord rec =
                    tr.GetObject(
                        defId,
                        OpenMode.ForRead,
                        false) as BlockTableRecord;

                return rec?.Name ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static string GetShopStatConnectionType(
            string blockName)
        {
            string key =
                NormalizeShopKey(
                    blockName ?? "");

            if (key.Contains("SCREW"))
                return "SCREW";

            if (key.Contains("WELD"))
                return "WELD";

            if (key.Contains("PPR"))
                return "PPR";

            if (key.Contains("UPVC") ||
                key.Contains("PVC"))
            {
                return "uPVC";
            }

            return "-";
        }

        private string ExtractShopStatSingleSize(
            string layer,
            string prefix)
        {
            if (string.IsNullOrWhiteSpace(layer) ||
                string.IsNullOrWhiteSpace(prefix))
            {
                return "";
            }

            string tail =
                layer.Length >= prefix.Length
                    ? layer.Substring(prefix.Length)
                    : "";

            Match m =
                Regex.Match(
                    tail,
                    @"DN\s*(\d{1,4}(?:[.,]\d+)?)",
                    RegexOptions.IgnoreCase);

            if (!m.Success)
            {
                m =
                    Regex.Match(
                        tail,
                        @"(\d{1,4}(?:[.,]\d+)?)");
            }

            if (!m.Success)
                return "";

            return "DN" +
                m.Groups[1]
                    .Value
                    .Replace(',', '.');
        }

        private string ExtractShopStatPairSize(
            string layer,
            string prefix)
        {
            if (string.IsNullOrWhiteSpace(layer) ||
                string.IsNullOrWhiteSpace(prefix))
            {
                return "";
            }

            string tail =
                layer.Length >= prefix.Length
                    ? layer.Substring(prefix.Length)
                    : "";

            MatchCollection ms =
                Regex.Matches(
                    tail,
                    @"(?:DN\s*)?(\d{1,4}(?:[.,]\d+)?)",
                    RegexOptions.IgnoreCase);

            if (ms.Count < 2)
                return "";

            string a =
                ms[0].Groups[1]
                    .Value
                    .Replace(',', '.');

            string b =
                ms[1].Groups[1]
                    .Value
                    .Replace(',', '.');

            return "DN" + a + "x" + b;
        }

        private string ExtractShopStatSizeFromBlockName(
            string blockName,
            string loai)
        {
            if (string.IsNullOrWhiteSpace(blockName))
                return "";

            string raw =
                blockName
                    .Replace('_', '-')
                    .ToUpperInvariant();

            bool isPair =
                string.Equals(
                    loai,
                    "TÊ",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    loai,
                    "GIẢM",
                    StringComparison.OrdinalIgnoreCase);

            if (isPair)
            {
                Match pair =
                    Regex.Match(
                        raw,
                        @"DN\s*(\d{1,4}(?:[.,]\d+)?)\s*[xX×]\s*(?:DN\s*)?(\d{1,4}(?:[.,]\d+)?)",
                        RegexOptions.IgnoreCase);

                if (pair.Success)
                {
                    return "DN" +
                        pair.Groups[1].Value.Replace(',', '.') +
                        "x" +
                        pair.Groups[2].Value.Replace(',', '.');
                }
            }

            Match one =
                Regex.Match(
                    raw,
                    @"DN\s*(\d{1,4}(?:[.,]\d+)?)",
                    RegexOptions.IgnoreCase);

            if (one.Success)
            {
                return "DN" +
                    one.Groups[1]
                        .Value
                        .Replace(',', '.');
            }

            return "";
        }

        private static double GetShopStatSortSize(
            string size)
        {
            if (string.IsNullOrWhiteSpace(size))
                return 0.0;

            MatchCollection ms =
                Regex.Matches(
                    size,
                    @"\d+(?:[.,]\d+)?");

            double max = 0.0;

            foreach (Match m in ms)
            {
                if (double.TryParse(
                        m.Value.Replace(',', '.'),
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out double value))
                {
                    if (value > max)
                        max = value;
                }
            }

            return max;
        }

        private void XuatBangThongKePhuKienShop(
            List<ShopFittingStatRow> data)
        {
            if (data == null ||
                data.Count == 0)
            {
                return;
            }

            var doc =
                Autodesk.AutoCAD.ApplicationServices.Core.Application
                    .DocumentManager
                    .MdiActiveDocument;

            if (doc == null)
                return;

            var db = doc.Database;
            var ed = doc.Editor;

            PromptPointResult ppr =
                ed.GetPoint(
                    new PromptPointOptions(
                        "\nKích chọn vị trí đặt BẢNG THỐNG KÊ PHỤ KIỆN SHOP: "));

            if (ppr.Status != PromptStatus.OK)
                return;

            using (Transaction tr =
                db.TransactionManager.StartTransaction())
            {
                BlockTableRecord btr =
                    (BlockTableRecord)tr.GetObject(
                        db.CurrentSpaceId,
                        OpenMode.ForWrite);

                Table tb =
                    new Table
                    {
                        TableStyle = db.Tablestyle,
                        Position = ppr.Value
                    };

                tb.Color =
                    Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                        ColorMethod.ByAci,
                        2);

                const int soCot = 5;
                int soDong =
                    data.Count + 3;

                tb.SetSize(
                    soDong,
                    soCot);

                // Tỷ lệ tương thích bản vẽ mm nhưng nhỏ gọn hơn bảng tổng hợp cũ.
                double sf = 4.0;
                double textH = 120.0 * sf;

                for (int r = 0;
                    r < tb.Rows.Count;
                    r++)
                {
                    tb.Rows[r].Height =
                        (r == 0 ? 360.0 : 260.0) * sf;

                    for (int c = 0;
                        c < tb.Columns.Count;
                        c++)
                    {
                        tb.Cells[r, c].TextStyleId =
                            db.Textstyle;

                        tb.Cells[r, c].TextHeight =
                            textH;
                    }
                }

                tb.Columns[0].Width = 700.0 * sf;
                tb.Columns[1].Width = 2500.0 * sf;
                tb.Columns[2].Width = 2200.0 * sf;
                tb.Columns[3].Width = 1800.0 * sf;
                tb.Columns[4].Width = 1800.0 * sf;

                try
                {
                    tb.MergeCells(
                        CellRange.Create(
                            tb,
                            0,
                            0,
                            0,
                            soCot - 1));
                }
                catch
                {
                }

                tb.Cells[0, 0].TextString =
                    "BẢNG THỐNG KÊ PHỤ KIỆN SHOP CHỮA CHÁY";

                tb.Cells[0, 0].Alignment =
                    CellAlignment.MiddleCenter;

                tb.Cells[1, 0].TextString = "STT";
                tb.Cells[1, 1].TextString = "LOẠI PHỤ KIỆN";
                tb.Cells[1, 2].TextString = "KÍCH THƯỚC";
                tb.Cells[1, 3].TextString = "KIỂU NỐI";
                tb.Cells[1, 4].TextString = "SỐ LƯỢNG (cái)";

                for (int c = 0; c < soCot; c++)
                {
                    tb.Cells[1, c].Alignment =
                        CellAlignment.MiddleCenter;
                }

                int rowIndex = 2;

                foreach (ShopFittingStatRow item in data)
                {
                    tb.Cells[rowIndex, 0].TextString =
                        item.STT.ToString();

                    tb.Cells[rowIndex, 1].TextString =
                        item.Loai ?? "";

                    tb.Cells[rowIndex, 2].TextString =
                        item.KichThuoc ?? "";

                    tb.Cells[rowIndex, 3].TextString =
                        item.KieuNoi ?? "";

                    tb.Cells[rowIndex, 4].TextString =
                        FormatSoThongKe(
                            item.SoLuong);

                    tb.Cells[rowIndex, 0].Alignment =
                        CellAlignment.MiddleCenter;

                    tb.Cells[rowIndex, 1].Alignment =
                        CellAlignment.MiddleLeft;

                    tb.Cells[rowIndex, 2].Alignment =
                        CellAlignment.MiddleCenter;

                    tb.Cells[rowIndex, 3].Alignment =
                        CellAlignment.MiddleCenter;

                    tb.Cells[rowIndex, 4].Alignment =
                        CellAlignment.MiddleCenter;

                    rowIndex++;
                }

                try
                {
                    tb.MergeCells(
                        CellRange.Create(
                            tb,
                            rowIndex,
                            0,
                            rowIndex,
                            3));
                }
                catch
                {
                }

                tb.Cells[rowIndex, 0].TextString =
                    "TỔNG";

                tb.Cells[rowIndex, 0].Alignment =
                    CellAlignment.MiddleCenter;

                tb.Cells[rowIndex, 4].TextString =
                    FormatSoThongKe(
                        data.Sum(x => x.SoLuong));

                tb.Cells[rowIndex, 4].Alignment =
                    CellAlignment.MiddleCenter;

                tb.GenerateLayout();

                btr.AppendEntity(tb);
                tr.AddNewlyCreatedDBObject(
                    tb,
                    true);

                tr.Commit();
            }
        }


        private void BtnThongKeOng_Click(
            object sender,
            RoutedEventArgs e)
        {
            var doc =
                Autodesk.AutoCAD.ApplicationServices.Core.Application
                    .DocumentManager
                    .MdiActiveDocument;

            if (doc == null)
                return;

            var ed = doc.Editor;
            var db = doc.Database;

            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();

            using (doc.LockDocument())
            {
                TypedValue[] tvs =
                    new TypedValue[]
                    {
                        new TypedValue(
                            (int)DxfCode.Start,
                            "LWPOLYLINE")
                    };

                SelectionFilter filter =
                    new SelectionFilter(tvs);

                PromptSelectionOptions pso =
                    new PromptSelectionOptions();

                pso.MessageForAdding =
                    "\nQuét chọn khu vực ống cần thống kê: ";

                PromptSelectionResult psr =
                    ed.GetSelection(pso, filter);

                if (psr.Status != PromptStatus.OK)
                    return;

                Dictionary<string, double> dictChieuDai =
                    new Dictionary<string, double>();

                using (Transaction tr =
                    db.TransactionManager.StartTransaction())
                {
                    foreach (SelectedObject so in psr.Value)
                    {
                        Polyline pline =
                            tr.GetObject(
                                so.ObjectId,
                                OpenMode.ForRead)
                                as Polyline;

                        if (pline != null)
                        {
                            // Chỉ thống kê layer do tool tạo
                            if (!LaLayerCuaTool(pline.Layer))
                                continue;

                            if (!dictChieuDai.ContainsKey(
                                pline.Layer))
                            {
                                dictChieuDai[pline.Layer] = 0;
                            }

                            dictChieuDai[pline.Layer] +=
                                pline.Length;
                        }
                    }

                    tr.Commit();
                }

                List<ThongKeOng> danhSachThongKe =
                    new List<ThongKeOng>();

                foreach (var item in dictChieuDai)
                {
                    double kichThuoc = 0;

                    var match =
                        Regex.Match(
                            item.Key,
                            @"\d+(\.\d+)?");

                    if (match.Success)
                    {
                        double.TryParse(
                            match.Value,
                            NumberStyles.Any,
                            CultureInfo.InvariantCulture,
                            out kichThuoc);
                    }

                    danhSachThongKe.Add(
                        new ThongKeOng
                        {
                            TenLayer = item.Key,
                            SoLuong =
                                Math.Round(
                                    item.Value / 1000.0,
                                    2),
                            M2 =
                                Math.Round(
                                    TinhM2OngGioTuLayer(
                                        item.Key,
                                        item.Value / 1000.0),
                                    2),
                            HeThongSort =
                                item.Key.Split('_')[0],
                            KichThuocSort =
                                kichThuoc
                        });
                }

                var danhSachDaSapXep =
                    danhSachThongKe
                        .OrderBy(x => x.HeThongSort)
                        .ThenByDescending(
                            x => x.KichThuocSort)
                        .ToList();

                for (int i = 0;
                    i < danhSachDaSapXep.Count;
                    i++)
                {
                    danhSachDaSapXep[i].STT = i + 1;
                }

                XuatBangRaCad(
                    danhSachDaSapXep,
                    "BẢNG THỐNG KÊ KHỐI LƯỢNG ỐNG",
                    "SỐ LƯỢNG (m)",
                    true);
            }
        }

        private void BtnThongKeThietBiVan_Click(
            object sender,
            RoutedEventArgs e)
        {
            var doc =
                Autodesk.AutoCAD.ApplicationServices.Core.Application
                    .DocumentManager
                    .MdiActiveDocument;

            if (doc == null)
                return;

            var ed = doc.Editor;
            var db = doc.Database;

            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();

            using (doc.LockDocument())
            {
                // Chỉ lấy TEXT + MTEXT (thiết bị / van được đặt bằng text)
                TypedValue[] tvs =
                    new TypedValue[]
                    {
                        new TypedValue(
                            (int)DxfCode.Start,
                            "TEXT,MTEXT")
                    };

                SelectionFilter filter =
                    new SelectionFilter(tvs);

                PromptSelectionOptions pso =
                    new PromptSelectionOptions();

                pso.MessageForAdding =
                    "\nQuét chọn khu vực thiết bị / van cần thống kê: ";

                PromptSelectionResult psr =
                    ed.GetSelection(pso, filter);

                if (psr.Status != PromptStatus.OK)
                    return;

                // Đếm số lượng theo Layer
                Dictionary<string, double> dictSoLuong =
                    new Dictionary<string, double>(
                        StringComparer.OrdinalIgnoreCase);

                using (Transaction tr =
                    db.TransactionManager.StartTransaction())
                {
                    foreach (SelectedObject so in psr.Value)
                    {
                        Entity ent =
                            tr.GetObject(
                                so.ObjectId,
                                OpenMode.ForRead)
                                as Entity;

                        if (ent == null)
                            continue;

                        string layer = ent.Layer ?? "";

                        // 1) Phải là layer tool FF_ / ACMV_ / CTN_
                        if (!LaLayerCuaTool(layer))
                            continue;
                        // 2) Bỏ layer ống (vật liệu + DN / ống gió)
                        if (LaLayerOng(layer))
                            continue;
                        // 3) Còn lại: van + thiết bị (kể cả CTN)

                        if (!dictSoLuong.ContainsKey(layer))
                            dictSoLuong[layer] = 0;

                        dictSoLuong[layer] += 1;
                    }

                    tr.Commit();
                }

                if (dictSoLuong.Count == 0)
                {
                    MessageBox.Show(
                        "Không tìm thấy text thiết bị / van nào trong vùng chọn.",
                        "Thông báo");
                    return;
                }

                List<ThongKeOng> danhSachThongKe =
                    new List<ThongKeOng>();

                foreach (var item in dictSoLuong)
                {
                    string heThong = item.Key;
                    if (heThong.Contains("_"))
                        heThong = heThong.Split('_')[0];

                    double kichThuoc = 0;
                    var match = Regex.Match(item.Key, @"\d+(\.\d+)?");
                    if (match.Success)
                    {
                        double.TryParse(
                            match.Value,
                            NumberStyles.Any,
                            CultureInfo.InvariantCulture,
                            out kichThuoc);
                    }

                    danhSachThongKe.Add(
                        new ThongKeOng
                        {
                            TenLayer = item.Key,
                            SoLuong = item.Value,
                            HeThongSort = heThong,
                            KichThuocSort = kichThuoc
                        });
                }

                // Sắp xếp: Hệ thống → tên layer → kích thước
                var danhSachDaSapXep =
                    danhSachThongKe
                        .OrderBy(x => x.HeThongSort)
                        .ThenBy(x => x.TenLayer)
                        .ThenByDescending(x => x.KichThuocSort)
                        .ToList();

                for (int i = 0; i < danhSachDaSapXep.Count; i++)
                    danhSachDaSapXep[i].STT = i + 1;

                XuatBangRaCad(
                    danhSachDaSapXep,
                    "BẢNG THỐNG KÊ THIẾT BỊ + VAN",
                    "SỐ LƯỢNG (cái)");
            }
        }


        private const string TempFindLayerName = "_TIM_DOI_TUONG_TEMP";

        private class EntityColorBackup
        {
            public ObjectId Id;
            public Autodesk.AutoCAD.Colors.Color Color;
        }

        private void BtnTimDoiTuongThongKe_Click(
            object sender,
            RoutedEventArgs e)
        {
            TimDoiTuongThongKe(false);
        }

        private void BtnTimDoiTuongKhoanhTron_Click(
            object sender,
            RoutedEventArgs e)
        {
            TimDoiTuongThongKe(true);
        }

        private void TimDoiTuongThongKe(bool khoanhTron)
        {
            var doc =
                Autodesk.AutoCAD.ApplicationServices.Core.Application
                    .DocumentManager
                    .MdiActiveDocument;

            if (doc == null)
                return;

            var db = doc.Database;
            var ed = doc.Editor;

            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();

            List<ObjectId> tempLineIds = new List<ObjectId>();
            List<EntityColorBackup> colorBackups = new List<EntityColorBackup>();

            try
            {
                while (true)
                {
                    ed.WriteMessage(
                        khoanhTron
                            ? "\n[TÌM ĐỐI TƯỢNG KHOANH TRÒN] Click vào TÊN LAYER trên bảng (ESC thoát): "
                            : "\n[TÌM ĐỐI TƯỢNG ĐƯỜNG THẲNG] Click vào TÊN LAYER trên bảng (ESC thoát): ");

                    PromptPointOptions ppo =
                        new PromptPointOptions(
                            "\nClick vào ô Tên Layer trên bảng thống kê: ")
                        {
                            AllowNone = true
                        };

                    PromptPointResult ppr = ed.GetPoint(ppo);

                    if (ppr.Status == PromptStatus.None ||
                        ppr.Status == PromptStatus.Cancel)
                    {
                        ed.WriteMessage(
                            khoanhTron
                                ? "\n[TÌM ĐỐI TƯỢNG KHOANH TRÒN] Đã thoát."
                                : "\n[TÌM ĐỐI TƯỢNG ĐƯỜNG THẲNG] Đã thoát.");
                        break;
                    }

                    if (ppr.Status != PromptStatus.OK)
                        continue;

                    // Xóa đường + khôi phục màu lần trước
                    KhoiPhucMauVaXoaDuong(doc, db, tempLineIds, colorBackups);
                    tempLineIds.Clear();
                    colorBackups.Clear();

                    Point3d pick = ppr.Value;
                    string layerName = "";
                    Point3d fromPt = pick;

                    using (doc.LockDocument())
                    using (Transaction tr =
                        db.TransactionManager.StartTransaction())
                    {
                        layerName = TimTenLayerTaiDiem(
                            tr, db, ed, pick, out fromPt);
                        tr.Commit();
                    }

                    layerName = (layerName ?? "").Trim();

                    if (string.IsNullOrWhiteSpace(layerName) ||
                        layerName.Equals("TÊN LAYER", StringComparison.OrdinalIgnoreCase) ||
                        layerName.Equals("STT", StringComparison.OrdinalIgnoreCase) ||
                        layerName.StartsWith("BẢNG THỐNG KÊ", StringComparison.OrdinalIgnoreCase) ||
                        layerName.StartsWith("SỐ LƯỢNG", StringComparison.OrdinalIgnoreCase) ||
                        layerName.Equals("M2", StringComparison.OrdinalIgnoreCase) ||
                        layerName.Equals("TỔNG", StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show(
                            "Không đọc được Tên Layer tại vị trí click.\nHãy click vào chữ tên Layer trong bảng thống kê.",
                            "Cảnh báo");
                        continue;
                    }

                    List<Point3d> targets = new List<Point3d>();
                    HashSet<ObjectId> targetIds = new HashSet<ObjectId>();
                    HashSet<ObjectId> circleIds = new HashSet<ObjectId>();

                    using (doc.LockDocument())
                    using (Transaction tr =
                        db.TransactionManager.StartTransaction())
                    {
                        BlockTableRecord btr =
                            (BlockTableRecord)tr.GetObject(
                                db.CurrentSpaceId, OpenMode.ForRead);

                        List<Polyline> plines = new List<Polyline>();
                        List<Entity> texts = new List<Entity>();

                        foreach (ObjectId id in btr)
                        {
                            Entity o =
                                tr.GetObject(id, OpenMode.ForRead)
                                    as Entity;
                            if (o == null)
                                continue;

                            if (!string.Equals(
                                    o.Layer,
                                    layerName,
                                    StringComparison.OrdinalIgnoreCase))
                                continue;

                            if (o is Table)
                                continue;

                            if (o is Polyline pl)
                                plines.Add(pl);
                            else if (o is DBText || o is MText)
                                texts.Add(o);
                        }

                        if (khoanhTron)
                        {
                            foreach (Polyline pl in plines)
                            {
                                targets.Add(LayDiemGiuaPolyline(pl));
                                targetIds.Add(pl.ObjectId);
                            }

                            foreach (Entity t in texts)
                            {
                                targets.Add(LayDiemDaiDien(t));
                                targetIds.Add(t.ObjectId);
                                circleIds.Add(t.ObjectId);
                            }

                            if (circleIds.Count == 0)
                            {
                                foreach (Polyline pl in plines)
                                    circleIds.Add(pl.ObjectId);
                            }
                        }
                        else if (plines.Count > 0)
                        {
                            foreach (Polyline pl in plines)
                            {
                                targets.Add(LayDiemGiuaPolyline(pl));
                                targetIds.Add(pl.ObjectId);
                            }
                        }
                        else
                        {
                            foreach (Entity t in texts)
                            {
                                targets.Add(LayDiemDaiDien(t));
                                targetIds.Add(t.ObjectId);
                            }
                        }

                        tr.Commit();
                    }

                    if (targets.Count == 0)
                    {
                        MessageBox.Show(
                            $"Không tìm thấy đối tượng nào trên layer:\n{layerName}",
                            "Thông báo");
                        continue;
                    }

                    // Đổi màu: target = vàng (2), còn lại = xám (8)
                    using (doc.LockDocument())
                    using (Transaction tr =
                        db.TransactionManager.StartTransaction())
                    {
                        EnsureTempFindLayer(tr, db);

                        BlockTableRecord btr =
                            (BlockTableRecord)tr.GetObject(
                                db.CurrentSpaceId, OpenMode.ForWrite);

                        Autodesk.AutoCAD.Colors.Color colYellow =
                            Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                                ColorMethod.ByAci, 2);
                        Autodesk.AutoCAD.Colors.Color colGray =
                            Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                                ColorMethod.ByAci, 8);
                        HashSet<ObjectId> blockChildBackupIds =
                            new HashSet<ObjectId>();

                        foreach (ObjectId id in btr)
                        {
                            Entity o =
                                tr.GetObject(id, OpenMode.ForRead)
                                    as Entity;
                            if (o == null || o.IsErased)
                                continue;

                            // Giữ nguyên bảng thống kê + đường chỉ tạm
                            if (o is Table)
                                continue;
                            if (string.Equals(
                                    o.Layer,
                                    TempFindLayerName,
                                    StringComparison.OrdinalIgnoreCase))
                                continue;

                            if (IsEntityOnLockedLayer(tr, db, o))
                                continue;

                            try
                            {
                                o.UpgradeOpen();

                                colorBackups.Add(new EntityColorBackup
                                {
                                    Id = id,
                                    Color = o.Color
                                });

                                if (targetIds.Contains(id))
                                    o.Color = colYellow;
                                else
                                    o.Color = colGray;

                                if (o is BlockReference br)
                                {
                                    DoiMauEntityTrongBlockTamThoi(
                                        tr,
                                        db,
                                        br,
                                        targetIds.Contains(id)
                                            ? colYellow
                                            : colGray,
                                        colorBackups,
                                        blockChildBackupIds,
                                        new HashSet<ObjectId>());
                                }
                            }
                            catch { }
                        }

                        if (khoanhTron)
                        {
                            foreach (ObjectId id in circleIds)
                            {
                                try
                                {
                                    Entity ent =
                                        tr.GetObject(id, OpenMode.ForRead)
                                            as Entity;
                                    Circle circle =
                                        TaoVongTronBaoDoiTuong(ent);

                                    if (circle == null)
                                        continue;

                                    circle.SetDatabaseDefaults(db);
                                    circle.Layer = TempFindLayerName;
                                    circle.Color =
                                        Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                                            ColorMethod.ByAci, 1);
                                    btr.AppendEntity(circle);
                                    tr.AddNewlyCreatedDBObject(circle, true);
                                    tempLineIds.Add(circle.ObjectId);
                                }
                                catch { }
                            }
                        }
                        else
                        {
                            foreach (Point3d toPt in targets)
                            {
                                Line line = new Line(fromPt, toPt);
                                line.SetDatabaseDefaults(db);
                                line.Layer = TempFindLayerName;
                                line.Color =
                                    Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                                        ColorMethod.ByAci, 1);
                                btr.AppendEntity(line);
                                tr.AddNewlyCreatedDBObject(line, true);
                                tempLineIds.Add(line.ObjectId);
                            }
                        }

                        tr.Commit();
                    }

                    ed.Regen();
                    ed.WriteMessage(
                        (khoanhTron
                            ? $"\n[TÌM ĐỐI TƯỢNG KHOANH TRÒN] {layerName} → {targets.Count} đối tượng (vàng + vòng tròn). "
                            : $"\n[TÌM ĐỐI TƯỢNG ĐƯỜNG THẲNG] {layerName} → {targets.Count} đối tượng (vàng). ") +
                        "Click layer khác để tìm tiếp, ESC để khôi phục màu & thoát.");
                }
            }
            catch (System.Exception ex)
            {
                try
                {
                    KhoiPhucMauVaXoaDuong(
                        doc, db, tempLineIds, colorBackups);
                }
                catch { }
                MessageBox.Show(
                    "Lỗi tìm đối tượng:\n" + ex.Message,
                    "Lỗi");
            }
            finally
            {
                KhoiPhucMauVaXoaDuong(doc, db, tempLineIds, colorBackups);
                try { ed.Regen(); } catch { }
            }
        }

        private static void KhoiPhucMauVaXoaDuong(
            Document doc,
            Database db,
            List<ObjectId> tempLineIds,
            List<EntityColorBackup> colorBackups)
        {
            try
            {
                using (doc.LockDocument())
                using (Transaction tr =
                    db.TransactionManager.StartTransaction())
                {
                    if (colorBackups != null)
                    {
                        foreach (var bak in colorBackups)
                        {
                            if (bak.Id.IsNull || bak.Id.IsErased)
                                continue;
                            try
                            {
                                Entity o =
                                    tr.GetObject(bak.Id, OpenMode.ForWrite)
                                        as Entity;
                                if (o != null && bak.Color != null)
                                    o.Color = bak.Color;
                            }
                            catch { }
                        }
                    }

                    if (tempLineIds != null)
                    {
                        foreach (ObjectId id in tempLineIds)
                        {
                            if (id.IsNull || id.IsErased)
                                continue;
                            try
                            {
                                Entity ent =
                                    tr.GetObject(id, OpenMode.ForWrite)
                                        as Entity;
                                if (ent != null)
                                    ent.Erase();
                            }
                            catch { }
                        }
                    }

                    tr.Commit();
                }
            }
            catch { }
        }

        /// <summary>
        /// Tìm bảng gần điểm click và lấy text cột Tên Layer.
        /// </summary>
        private static string TimTenLayerTaiDiem(
            Transaction tr,
            Database db,
            Editor ed,
            Point3d pick,
            out Point3d fromPt)
        {
            fromPt = pick;

            BlockTableRecord btr =
                (BlockTableRecord)tr.GetObject(
                    db.CurrentSpaceId, OpenMode.ForRead);

            Table bestTable = null;
            double bestTableDist = double.MaxValue;

            foreach (ObjectId id in btr)
            {
                Table tb = tr.GetObject(id, OpenMode.ForRead) as Table;
                if (tb == null)
                    continue;

                try
                {
                    Extents3d ext = tb.GeometricExtents;
                    // Mở rộng một chút vùng bao
                    double pad = Math.Max(
                        ext.MaxPoint.X - ext.MinPoint.X,
                        ext.MaxPoint.Y - ext.MinPoint.Y) * 0.05;

                    if (pick.X < ext.MinPoint.X - pad ||
                        pick.X > ext.MaxPoint.X + pad ||
                        pick.Y < ext.MinPoint.Y - pad ||
                        pick.Y > ext.MaxPoint.Y + pad)
                        continue;

                    Point3d mid = new Point3d(
                        (ext.MinPoint.X + ext.MaxPoint.X) / 2.0,
                        (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0,
                        0);
                    double d = pick.DistanceTo(mid);
                    if (d < bestTableDist)
                    {
                        bestTableDist = d;
                        bestTable = tb;
                    }
                }
                catch { }
            }

            if (bestTable == null)
                return "";

            // 1) HitTest
            string name = LayTenLayerTuBang(
                bestTable, pick, ed, out fromPt);
            if (!string.IsNullOrWhiteSpace(name))
                return name;

            // 2) Fallback khoảng cách ô
            return LayTenLayerBangBangKhoangCach(
                bestTable, pick, out fromPt);
        }

        private static Point3d LayDiemGiuaPolyline(Polyline pl)
        {
            try
            {
                if (pl == null || pl.NumberOfVertices < 1)
                    return Point3d.Origin;

                double len = pl.Length;
                if (len > 1e-9)
                    return pl.GetPointAtDist(len / 2.0);

                return pl.GetPoint3dAt(0);
            }
            catch
            {
                try { return pl.GetPoint3dAt(0); }
                catch { return Point3d.Origin; }
            }
        }

        private static Point3d LayDiemDaiDien(Entity o)
        {
            try
            {
                if (o is Polyline pl && pl.NumberOfVertices > 0)
                    return pl.GetPoint3dAt(0);

                if (o is Curve cv)
                    return cv.StartPoint;

                if (o is DBText t)
                    return t.Position;

                if (o is MText mt)
                    return mt.Location;

                if (o is BlockReference br)
                    return br.Position;

                Extents3d ext = o.GeometricExtents;
                return new Point3d(
                    (ext.MinPoint.X + ext.MaxPoint.X) / 2.0,
                    (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0,
                    (ext.MinPoint.Z + ext.MaxPoint.Z) / 2.0);
            }
            catch
            {
                return Point3d.Origin;
            }
        }

        private static void DoiMauEntityTrongBlockTamThoi(
            Transaction tr,
            Database db,
            BlockReference blockRef,
            Autodesk.AutoCAD.Colors.Color color,
            List<EntityColorBackup> colorBackups,
            HashSet<ObjectId> backedUpIds,
            HashSet<ObjectId> visitedBlockRecords)
        {
            if (blockRef == null ||
                blockRef.BlockTableRecord.IsNull ||
                visitedBlockRecords.Contains(blockRef.BlockTableRecord))
                return;

            try
            {
                foreach (ObjectId attId in blockRef.AttributeCollection)
                {
                    AttributeReference att =
                        tr.GetObject(attId, OpenMode.ForRead)
                            as AttributeReference;

                    if (att == null || att.IsErased)
                        continue;

                    if (IsEntityOnLockedLayer(tr, db, att))
                        continue;

                    try
                    {
                        att.UpgradeOpen();

                        if (!backedUpIds.Contains(att.ObjectId))
                        {
                            colorBackups.Add(new EntityColorBackup
                            {
                                Id = att.ObjectId,
                                Color = att.Color
                            });
                            backedUpIds.Add(att.ObjectId);
                        }

                        att.Color = color;
                    }
                    catch { }
                }
            }
            catch { }

            visitedBlockRecords.Add(blockRef.BlockTableRecord);

            try
            {
                BlockTableRecord blockDef =
                    tr.GetObject(
                        blockRef.BlockTableRecord,
                        OpenMode.ForRead) as BlockTableRecord;

                if (blockDef == null)
                    return;

                foreach (ObjectId childId in blockDef)
                {
                    Entity child =
                        tr.GetObject(childId, OpenMode.ForRead)
                            as Entity;
                    if (child == null || child.IsErased)
                        continue;

                    if (IsEntityOnLockedLayer(tr, db, child))
                        continue;

                    try
                    {
                        child.UpgradeOpen();

                        if (!backedUpIds.Contains(child.ObjectId))
                        {
                            colorBackups.Add(new EntityColorBackup
                            {
                                Id = child.ObjectId,
                                Color = child.Color
                            });
                            backedUpIds.Add(child.ObjectId);
                        }

                        child.Color = color;

                        if (child is BlockReference nestedBlock)
                        {
                            DoiMauEntityTrongBlockTamThoi(
                                tr,
                                db,
                                nestedBlock,
                                color,
                                colorBackups,
                                backedUpIds,
                                visitedBlockRecords);
                        }
                    }
                    catch { }
                }
            }
            catch { }
            finally
            {
                visitedBlockRecords.Remove(blockRef.BlockTableRecord);
            }
        }

        private static Circle TaoVongTronBaoDoiTuong(Entity ent)
        {
            if (ent == null)
                return null;

            try
            {
                Extents3d ext = ent.GeometricExtents;

                Point3d center =
                    new Point3d(
                        (ext.MinPoint.X + ext.MaxPoint.X) / 2.0,
                        (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0,
                        (ext.MinPoint.Z + ext.MaxPoint.Z) / 2.0);

                double width =
                    Math.Abs(ext.MaxPoint.X - ext.MinPoint.X);
                double height =
                    Math.Abs(ext.MaxPoint.Y - ext.MinPoint.Y);
                double maxSize = Math.Max(width, height);

                if (maxSize < 1e-6)
                    maxSize = 600.0;

                double radius =
                    Math.Max(maxSize * 1.44, 600.0);

                return new Circle(
                    center,
                    Vector3d.ZAxis,
                    radius);
            }
            catch
            {
                Point3d center = LayDiemDaiDien(ent);
                return new Circle(
                    center,
                    Vector3d.ZAxis,
                    600.0);
            }
        }

        private static string LayTenLayerTuBang(
            Table table,
            Point3d pick,
            Editor ed,
            out Point3d cellCenter)
        {
            cellCenter = pick;

            try
            {
                Vector3d viewDir = Vector3d.ZAxis;
                try
                {
                    using (ViewTableRecord view = ed.GetCurrentView())
                    {
                        viewDir = view.ViewDirection;
                        if (viewDir.Length < 1e-9)
                            viewDir = Vector3d.ZAxis;
                    }
                }
                catch { }

                TableHitTestInfo hit = table.HitTest(pick, viewDir);

                if (hit.Type != TableHitTestType.Cell)
                    hit = table.HitTest(pick, Vector3d.ZAxis);

                if (hit.Type != TableHitTestType.Cell)
                    hit = table.HitTest(pick, new Vector3d(0, 0, 1));

                // Fallback: duyệt từng ô, chọn ô gần điểm pick nhất (cột tên layer = 1)
                if (hit.Type != TableHitTestType.Cell)
                {
                    return LayTenLayerBangBangKhoangCach(
                        table, pick, out cellCenter);
                }

                int row = hit.Row;
                int col = hit.Column;

                string text = (table.Cells[row, col].TextString ?? "").Trim();

                if (col != 1 && table.Columns.Count > 1 && row >= 2)
                {
                    string col1 = (table.Cells[row, 1].TextString ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(col1))
                        text = col1;
                }

                cellCenter = pick;
                return text;
            }
            catch
            {
                return LayTenLayerBangBangKhoangCach(
                    table, pick, out cellCenter);
            }
        }

        private static string LayTenLayerBangBangKhoangCach(
            Table table,
            Point3d pick,
            out Point3d cellCenter)
        {
            cellCenter = pick;
            string best = "";
            double bestDist = double.MaxValue;

            try
            {
                int rows = table.Rows.Count;
                int cols = table.Columns.Count;
                if (cols < 2)
                    return "";

                // Chỉ xét cột 1 (Tên Layer), bỏ hàng 0-1 (tiêu đề)
                for (int r = 2; r < rows; r++)
                {
                    try
                    {
                        Cell cell = table.Cells[r, 1];
                        string text = (cell.TextString ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(text))
                            continue;

                        // Ước lượng tâm ô theo vị trí bảng + cộng dồn width/height
                        double x = table.Position.X;
                        double y = table.Position.Y;

                        for (int c = 0; c < 1; c++)
                            x += table.Columns[c].Width;
                        x += table.Columns[1].Width / 2.0;

                        for (int rr = 0; rr < r; rr++)
                            y -= table.Rows[rr].Height;
                        y -= table.Rows[r].Height / 2.0;

                        Point3d center = new Point3d(x, y, table.Position.Z);
                        double d = pick.DistanceTo(center);

                        if (d < bestDist)
                        {
                            bestDist = d;
                            best = text;
                            cellCenter = center;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return best;
        }

        private static void EnsureTempFindLayer(
            Transaction tr,
            Database db)
        {
            LayerTable lt =
                (LayerTable)tr.GetObject(
                    db.LayerTableId, OpenMode.ForRead);

            if (lt.Has(TempFindLayerName))
            {
                LayerTableRecord existing =
                    (LayerTableRecord)tr.GetObject(
                        lt[TempFindLayerName],
                        OpenMode.ForWrite);

                existing.IsLocked = false;
                existing.IsOff = false;
                existing.IsFrozen = false;
                return;
            }

            lt.UpgradeOpen();
            LayerTableRecord ltr = new LayerTableRecord();
            ltr.Name = TempFindLayerName;
            ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                ColorMethod.ByAci, 1);
            ltr.IsOff = false;
            ltr.IsLocked = false;
            ltr.IsFrozen = false;
            lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
        }

        private static bool IsEntityOnLockedLayer(
            Transaction tr,
            Database db,
            Entity ent)
        {
            if (ent == null)
                return false;

            try
            {
                LayerTable lt =
                    (LayerTable)tr.GetObject(
                        db.LayerTableId,
                        OpenMode.ForRead);

                if (!lt.Has(ent.Layer))
                    return false;

                LayerTableRecord ltr =
                    (LayerTableRecord)tr.GetObject(
                        lt[ent.Layer],
                        OpenMode.ForRead);

                return ltr != null && ltr.IsLocked;
            }
            catch
            {
                return false;
            }
        }

        private static void XoaDuongChiTam(
            Document doc,
            Database db,
            List<ObjectId> ids)
        {
            if (ids == null || ids.Count == 0)
                return;

            try
            {
                using (doc.LockDocument())
                using (Transaction tr =
                    db.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in ids)
                    {
                        if (id.IsNull || id.IsErased)
                            continue;

                        try
                        {
                            Entity ent =
                                tr.GetObject(id, OpenMode.ForWrite)
                                    as Entity;
                            if (ent != null)
                                ent.Erase();
                        }
                        catch { }
                    }

                    tr.Commit();
                }
            }
            catch { }
        }

        private static double TinhM2OngGioTuLayer(
            string layerName,
            double chieuDaiMet)
        {
            if (string.IsNullOrWhiteSpace(layerName) ||
                chieuDaiMet <= 0.0)
                return 0.0;

            Match match =
                Regex.Match(
                    layerName,
                    @"(\d+(?:\.\d+)?)\s*[xX×]\s*(\d+(?:\.\d+)?)");

            if (!match.Success)
                return 0.0;

            double canhA;
            double canhB;

            if (!double.TryParse(
                    match.Groups[1].Value,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out canhA) ||
                !double.TryParse(
                    match.Groups[2].Value,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out canhB))
                return 0.0;

            double chuViMet =
                ((canhA / 1000.0) * 2.0) +
                ((canhB / 1000.0) * 2.0);

            return chieuDaiMet * chuViMet;
        }

        private static string FormatSoThongKe(double value)
        {
            return Math.Round(value, 2)
                .ToString("0.##", CultureInfo.InvariantCulture);
        }

        private void XuatBangRaCad(
            List<ThongKeOng> data,
            string tieuDe = "BẢNG THỐNG KÊ KHỐI LƯỢNG ỐNG",
            string cotSoLuong = "SỐ LƯỢNG (m)",
            bool themCotM2OngGio = false)
        {
            var doc =
                Autodesk.AutoCAD.ApplicationServices.Core.Application
                    .DocumentManager
                    .MdiActiveDocument;

            if (doc == null)
                return;

            var db = doc.Database;
            var ed = doc.Editor;

            PromptPointOptions ppo =
                new PromptPointOptions(
                    "\nKích chọn vị trí đặt Bảng thống kê: ");

            PromptPointResult ppr =
                ed.GetPoint(ppo);

            if (ppr.Status != PromptStatus.OK)
                return;

            using (Transaction tr =
                db.TransactionManager.StartTransaction())
            {
                BlockTableRecord btr =
                    (BlockTableRecord)tr.GetObject(
                        db.CurrentSpaceId,
                        OpenMode.ForWrite);

                Table tb =
                    new Table
                    {
                        TableStyle = db.Tablestyle
                    };

                tb.Color =
                    Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                        ColorMethod.ByAci,
                        2);

                int soCot = themCotM2OngGio ? 4 : 3;
                int soDong = data.Count + 2 +
                    (themCotM2OngGio ? 1 : 0);

                tb.SetSize(soDong, soCot);
                tb.Position = ppr.Value;

                // Cỡ bảng lớn, tỷ lệ cột cân với chiều cao chữ
                double sf = 12.0;
                double textH = 140.0 * sf;   // ~1680

                for (int r = 0;
                    r < tb.Rows.Count;
                    r++)
                {
                    // Hàng tiêu đề cao hơn một chút
                    tb.Rows[r].Height = (r == 0 ? 420.0 : 320.0) * sf;

                    for (int c = 0;
                        c < tb.Columns.Count;
                        c++)
                    {
                        tb.Cells[r, c].TextStyleId = db.Textstyle;
                        tb.Cells[r, c].TextHeight = textH;
                    }
                }

                // STT | LAYER | Số lượng | M2 (chỉ bảng ống gió)
                tb.Columns[0].Width = 900.0 * sf;     // STT
                tb.Columns[1].Width =
                    (themCotM2OngGio ? 4500.0 : 4800.0) * sf;
                tb.Columns[2].Width = 2200.0 * sf;    // Số lượng
                if (themCotM2OngGio)
                    tb.Columns[3].Width = 1800.0 * sf; // M2

                // Gộp hàng tiêu đề theo số cột thực tế
                try
                {
                    tb.MergeCells(
                        CellRange.Create(tb, 0, 0, 0, soCot - 1));
                }
                catch
                {
                    // một số version TableStyle không merge được — bỏ qua
                }

                tb.Cells[0, 0].TextString = tieuDe;
                tb.Cells[0, 0].Alignment =
                    CellAlignment.MiddleCenter;

                tb.Cells[1, 0].TextString = "STT";
                tb.Cells[1, 1].TextString = "TÊN LAYER";
                tb.Cells[1, 2].TextString = cotSoLuong;
                if (themCotM2OngGio)
                    tb.Cells[1, 3].TextString = "M2";

                for (int i = 0; i < soCot; i++)
                {
                    tb.Cells[1, i].Alignment =
                        CellAlignment.MiddleCenter;
                }

                int row = 2;

                foreach (var item in data)
                {
                    tb.Cells[row, 0].TextString =
                        item.STT.ToString();

                    tb.Cells[row, 0].Alignment =
                        CellAlignment.MiddleCenter;

                    tb.Cells[row, 1].TextString =
                        "   " + item.TenLayer;

                    tb.Cells[row, 1].Alignment =
                        CellAlignment.MiddleLeft;

                    tb.Cells[row, 2].TextString =
                        FormatSoThongKe(item.SoLuong);

                    tb.Cells[row, 2].Alignment =
                        CellAlignment.MiddleCenter;

                    if (themCotM2OngGio)
                    {
                        tb.Cells[row, 3].TextString =
                            FormatSoThongKe(item.M2);

                        tb.Cells[row, 3].Alignment =
                            CellAlignment.MiddleCenter;
                    }

                    row++;
                }

                if (themCotM2OngGio)
                {
                    try
                    {
                        tb.MergeCells(
                            CellRange.Create(tb, row, 0, row, 1));
                    }
                    catch
                    {
                    }

                    tb.Cells[row, 0].TextString = "TỔNG";
                    tb.Cells[row, 0].Alignment =
                        CellAlignment.MiddleCenter;

                    tb.Cells[row, 2].TextString =
                        FormatSoThongKe(
                            data.Sum(x => x.SoLuong));

                    tb.Cells[row, 2].Alignment =
                        CellAlignment.MiddleCenter;

                    tb.Cells[row, 3].TextString =
                        FormatSoThongKe(
                            data.Sum(x => x.M2));

                    tb.Cells[row, 3].Alignment =
                        CellAlignment.MiddleCenter;
                }

                tb.GenerateLayout();
                btr.AppendEntity(tb);
                tr.AddNewlyCreatedDBObject(tb, true);
                tr.Commit();
            }
        }

        private void BtnTimDoiTuong_Click(
            object sender,
            RoutedEventArgs e)
        {
            var doc =
                Autodesk.AutoCAD.ApplicationServices.Core.Application
                    .DocumentManager
                    .MdiActiveDocument;

            if (doc == null)
                return;

            var ed = doc.Editor;
            var db = doc.Database;

            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();

            using (doc.LockDocument())
            {
                List<ObjectId> tempLines =
                    new List<ObjectId>();

                try
                {
                    PromptPointOptions ppo =
                        new PromptPointOptions(
                            "\nKích chọn trực tiếp vào dòng " +
                            "tên layer cần tìm trong Bảng: ");

                    PromptPointResult ppr =
                        ed.GetPoint(ppo);

                    if (ppr.Status != PromptStatus.OK)
                        return;

                    Point3d hitPt = ppr.Value;

                    using (Transaction tr =
                        db.TransactionManager.StartTransaction())
                    {
                        BlockTableRecord btr =
                            (BlockTableRecord)tr.GetObject(
                                db.CurrentSpaceId,
                                OpenMode.ForWrite);

                        Table targetTable = null;
                        double minBoxDist = double.MaxValue;

                        foreach (ObjectId id in btr)
                        {
                            if (id.ObjectClass.IsDerivedFrom(
                                RXClass.GetClass(
                                    typeof(Table))))
                            {
                                Table tb =
                                    tr.GetObject(
                                        id,
                                        OpenMode.ForRead)
                                        as Table;

                                if (tb != null)
                                {
                                    double dist =
                                        Math.Abs(
                                            hitPt.X -
                                            tb.Position.X) +
                                        Math.Abs(
                                            hitPt.Y -
                                            tb.Position.Y);

                                    if (dist < minBoxDist)
                                    {
                                        minBoxDist = dist;
                                        targetTable = tb;
                                    }
                                }
                            }
                        }

                        if (targetTable == null)
                        {
                            MessageBox.Show(
                                "Không tìm thấy Bảng thống kê!",
                                "Thông báo");

                            return;
                        }

                        int targetRow = -1;
                        double curY =
                            targetTable.Position.Y;

                        for (int r = 0;
                            r < targetTable.Rows.Count;
                            r++)
                        {
                            double h =
                                targetTable.Rows[r].Height;

                            if (hitPt.Y <= curY &&
                                hitPt.Y >= curY - h)
                            {
                                targetRow = r;
                                break;
                            }

                            curY -= h;
                        }

                        if (targetRow < 2)
                        {
                            MessageBox.Show(
                                "Hãy kích vào dòng dữ liệu " +
                                "bên trong bảng!",
                                "Nhắc nhở");

                            return;
                        }

                        string targetLayer =
                            targetTable
                                .Cells[targetRow, 1]
                                .TextString
                                .Trim();

                        TypedValue[] tvs =
                            new TypedValue[]
                            {
                                new TypedValue(
                                    (int)DxfCode.Start,
                                    "LWPOLYLINE"),
                                new TypedValue(
                                    (int)DxfCode.LayerName,
                                    targetLayer)
                            };

                        PromptSelectionResult psr =
                            ed.SelectAll(
                                new SelectionFilter(tvs));

                        if (psr.Status != PromptStatus.OK ||
                            psr.Value.Count == 0)
                        {
                            MessageBox.Show(
                                $"Không tìm thấy đoạn ống nào " +
                                $"thuộc Layer: {targetLayer}",
                                "Thông báo");

                            return;
                        }

                        foreach (
                            SelectedObject so in psr.Value)
                        {
                            Polyline pline =
                                tr.GetObject(
                                    so.ObjectId,
                                    OpenMode.ForRead)
                                    as Polyline;

                            if (pline != null &&
                                pline.Length > 0)
                            {
                                Line pointerLine =
                                    new Line(
                                        hitPt,
                                        pline.GetPointAtDist(
                                            pline.Length /
                                            2.0))
                                    {
                                        ColorIndex = 1
                                    };

                                tempLines.Add(
                                    btr.AppendEntity(
                                        pointerLine));

                                tr.AddNewlyCreatedDBObject(
                                    pointerLine,
                                    true);
                            }
                        }

                        tr.Commit();
                    }

                    ed.GetPoint(
                        new PromptPointOptions(
                            "\n[Đang chỉ vạch] Bấm ENTER " +
                            "hoặc kích chuột để xóa và kết thúc: ")
                        {
                            AllowNone = true
                        });
                }
                catch
                {
                }
                finally
                {
                    if (tempLines.Count > 0)
                    {
                        try
                        {
                            using (Transaction tr =
                                db.TransactionManager
                                    .StartTransaction())
                            {
                                foreach (
                                    ObjectId id in tempLines)
                                {
                                    if (!id.IsErased &&
                                        id.IsValid)
                                    {
                                        Entity ent =
                                            tr.GetObject(
                                                id,
                                                OpenMode.ForWrite)
                                                as Entity;

                                        ent?.Erase();
                                    }
                                }

                                tr.Commit();
                            }

                            ed.Regen();
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        private void Db_ObjectAppended(
            object sender,
            ObjectEventArgs e)
        {
            if (!_isWaitingForPline ||
                _plineWatcherDocument == null ||
                e == null)
            {
                return;
            }

            Database eventDatabase =
                sender as Database;

            if (eventDatabase != null &&
                eventDatabase !=
                _plineWatcherDocument.Database)
            {
                return;
            }

            Polyline pline =
                e.DBObject as Polyline;

            if (pline == null)
                return;

            if (!string.Equals(
                pline.Layer,
                _currentLayerNameForText,
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _lastPlineId = pline.Id;
            _pendingPlineIds.Add(pline.Id);
        }

        private void StartPlineTextWatcher(Document doc)
        {
            if (doc == null)
                return;

            if (_isWaitingForPline &&
                _plineWatcherDocument == doc)
            {
                return;
            }

            if (_isWaitingForPline &&
                _plineWatcherDocument != null)
            {
                CleanupEvents(
                    _plineWatcherDocument);
            }

            _plineWatcherDocument = doc;

            doc.Database.ObjectAppended +=
                Db_ObjectAppended;

            doc.CommandEnded +=
                Doc_CommandEnded;

            doc.CommandCancelled +=
                Doc_CommandCancelled;

            _isWaitingForPline = true;
        }

        private bool IsPlineCommand(CommandEventArgs e)
        {
            string commandName =
                (e?.GlobalCommandName ?? "")
                    .Trim()
                    .TrimStart('.', '_');

            return string.Equals(
                commandName,
                "PLINE",
                StringComparison.OrdinalIgnoreCase);
        }

        private void Doc_CommandEnded(
            object sender,
            CommandEventArgs e)
        {
            if (_isWaitingForPline &&
                IsPlineCommand(e))
            {
                AddTextToPendingPolylines(
                    sender as Document ??
                    _plineWatcherDocument,
                    true);

                _lastPlineId = ObjectId.Null;
                _pendingPlineIds.Clear();
            }
        }

        private void Doc_CommandCancelled(
            object sender,
            CommandEventArgs e)
        {
            if (_isWaitingForPline &&
                IsPlineCommand(e))
            {
                AddTextToPendingPolylines(
                    sender as Document ??
                    _plineWatcherDocument,
                    false);

                _lastPlineId = ObjectId.Null;
                _pendingPlineIds.Clear();
            }
        }

        private void AddTextToPendingPolylines(
            Document doc,
            bool allowFallback)
        {
            if (doc == null)
                return;

            if (_pendingPlineIds.Count == 0 &&
                _lastPlineId != ObjectId.Null)
            {
                _pendingPlineIds.Add(
                    _lastPlineId);
            }

            List<ObjectId> candidates =
                _pendingPlineIds
                    .Where(
                        id =>
                            id != ObjectId.Null &&
                            id.IsValid &&
                            !id.IsErased)
                    .ToList();

            if (allowFallback &&
                candidates.Count == 0)
            {
                ObjectId newestPlineId =
                    FindNewestUnprocessedPolyline(doc);

                if (newestPlineId != ObjectId.Null)
                    candidates.Add(newestPlineId);
            }

            foreach (ObjectId id in candidates)
            {
                if (_processedPlineIds.Contains(id))
                    continue;

                if (AddTextToPolyline(
                    doc,
                    id,
                    _currentLayerNameForText,
                    _currentPlineWidth))
                {
                    _processedPlineIds.Add(id);
                }
            }
        }

        private ObjectId FindNewestUnprocessedPolyline(
            Document doc)
        {
            ObjectId newestId = ObjectId.Null;
            long newestHandle = long.MinValue;

            try
            {
                using (doc.LockDocument())
                using (Transaction tr =
                    doc.Database.TransactionManager
                        .StartTransaction())
                {
                    BlockTableRecord space =
                        (BlockTableRecord)tr.GetObject(
                            doc.Database.CurrentSpaceId,
                            OpenMode.ForRead);

                    foreach (ObjectId id in space)
                    {
                        if (_processedPlineIds.Contains(id))
                            continue;

                        Polyline pline =
                            tr.GetObject(
                                id,
                                OpenMode.ForRead,
                                false)
                                as Polyline;

                        if (pline == null ||
                            pline.NumberOfVertices < 2)
                        {
                            continue;
                        }

                        if (!string.Equals(
                            pline.Layer,
                            _currentLayerNameForText,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        long handleValue =
                            id.Handle.Value;

                        if (handleValue > newestHandle)
                        {
                            newestHandle = handleValue;
                            newestId = id;
                        }
                    }

                    tr.Commit();
                }
            }
            catch
            {
                return ObjectId.Null;
            }

            return newestId;
        }

        private void CleanupEvents(Document doc)
        {
            _isWaitingForPline = false;
            _lastPlineId = ObjectId.Null;
            _pendingPlineIds.Clear();

            if (doc != null)
            {
                doc.Database.ObjectAppended -=
                    Db_ObjectAppended;

                doc.CommandEnded -=
                    Doc_CommandEnded;

                doc.CommandCancelled -=
                    Doc_CommandCancelled;
            }

            if (_plineWatcherDocument == doc)
                _plineWatcherDocument = null;
        }

        private bool AddTextToPolyline(
            Document doc,
            ObjectId plineId,
            string layerName,
            double width)
        {
            if (doc == null ||
                plineId == ObjectId.Null ||
                !plineId.IsValid ||
                plineId.IsErased)
            {
                return false;
            }

            var db = doc.Database;

            try
            {
                using (doc.LockDocument())
                using (Transaction tr =
                    db.TransactionManager.StartTransaction())
                {
                    Polyline pline =
                        tr.GetObject(
                            plineId,
                            OpenMode.ForRead)
                            as Polyline;

                    if (pline == null)
                        return false;

                    BlockTableRecord owner =
                        (BlockTableRecord)tr.GetObject(
                            pline.OwnerId,
                            OpenMode.ForWrite);

                    double textHeight =
                        Math.Max(
                            width *
                            LabelTextHeightToWidthRatio,
                            MinimumLabelTextHeight);

                    int segmentCount =
                        pline.Closed
                            ? pline.NumberOfVertices
                            : pline.NumberOfVertices - 1;

                    for (int i = 0;
                        i < segmentCount;
                        i++)
                    {
                        if (pline.GetSegmentType(i) !=
                            SegmentType.Line)
                        {
                            continue;
                        }

                        LineSegment3d segment =
                            pline.GetLineSegmentAt(i);

                        double segmentLength =
                            segment.StartPoint.DistanceTo(
                                segment.EndPoint);

                        // Dưới 3m: vẫn hiện chữ, chỉ DN (+ CN/EI nếu có)
                        // Từ 3m trở lên: hiện full layer như cũ
                        string displayText =
                            segmentLength <
                            ManualMinimumLabelSegmentLength
                                ? GetShortSizeLabel(layerName)
                                : layerName;

                        if (string.IsNullOrWhiteSpace(displayText))
                            continue;

                        Point3d midPt =
                            segment.MidPoint;

                        Vector3d dir =
                            segment.Direction;

                        double angle =
                            dir.AngleOnPlane(
                                new Plane());

                        if (angle > Math.PI / 2 &&
                            angle <=
                            3 * Math.PI / 2)
                        {
                            angle -= Math.PI;
                            dir = dir.Negate();
                        }

                        Point3d textPt =
                            midPt +
                            dir.RotateBy(
                                Math.PI / 2,
                                Vector3d.ZAxis) *
                            ((width / 2.0) +
                             (textHeight * 0.2));

                        DBText txt = new DBText();
                        txt.SetDatabaseDefaults(db);
                        txt.TextString = displayText;
                        txt.Height = textHeight;
                        txt.Layer = layerName;
                        txt.ColorIndex = 256;
                        txt.Justify =
                            AttachmentPoint.BottomCenter;

                        txt.AlignmentPoint = textPt;
                        txt.Rotation = angle;

                        owner.AppendEntity(txt);

                        tr.AddNewlyCreatedDBObject(
                            txt,
                            true);
                    }

                    tr.Commit();
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage(
                    $"\nKhông thể tạo chữ theo nét vẽ: " +
                    $"{ex.Message}");

                return false;
            }
        }

        private void UserControl_Unloaded(
            object sender,
            RoutedEventArgs e)
        {
            if (_isWaitingForPline)
                CleanupEvents(_plineWatcherDocument);
        }

        /// <summary>
        /// Tự động thu nhỏ / phóng to toàn bộ giao diện theo chiều rộng palette.
        /// Trên laptop hẹp sẽ scale nhỏ lại (tối thiểu 0.72), trên máy to vẫn gần 1.0.
        /// </summary>
        private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (UiScale == null) return;

            // Chiều rộng thiết kế gốc của palette
            const double baseWidth = 360.0;

            // Giới hạn scale để không quá nhỏ hoặc quá to
            double scale = Math.Max(0.72, Math.Min(1.05, ActualWidth / baseWidth));

            UiScale.ScaleX = scale;
            UiScale.ScaleY = scale;
        }

        // ==================== ĐẶT THIẾT BỊ MẪU ====================

        private void BtnDatThietBiMau_Click(object sender, RoutedEventArgs e)
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Core.Application
                .DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var db = doc.Database;
            var ed = doc.Editor;

            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();

            // 1. Chọn text mẫu
            ed.WriteMessage("\n[ĐẶT THIẾT BỊ MẪU] Chọn TEXT mẫu đã đặt: ");
            var peoText = new PromptEntityOptions("\nChọn Text mẫu: ");
            peoText.SetRejectMessage("\nChỉ chọn TEXT hoặc MTEXT.");
            peoText.AddAllowedClass(typeof(DBText), false);
            peoText.AddAllowedClass(typeof(MText), false);
            var perText = ed.GetEntity(peoText);
            if (perText.Status != PromptStatus.OK) return;

            // 2. Chọn block mẫu
            ed.WriteMessage("\nChọn BLOCK mẫu tương ứng: ");
            var peoBlock = new PromptEntityOptions("\nChọn Block mẫu: ");
            peoBlock.SetRejectMessage("\nChỉ chọn Block Reference.");
            peoBlock.AddAllowedClass(typeof(BlockReference), false);
            var perBlock = ed.GetEntity(peoBlock);
            if (perBlock.Status != PromptStatus.OK) return;

            string sampleTextString = "";
            string sampleLayer = "0";
            double sampleHeight = MinimumLabelTextHeight;
            double sampleRotation = 0;
            Point3d sampleTextPos = Point3d.Origin;
            AttachmentPoint sampleJustify = AttachmentPoint.MiddleCenter;
            bool isMText = false;

            string sampleBlockName = "";
            Point3d sampleBlockPos = Point3d.Origin;
            double sampleBlockRotation = 0;
            Scale3d sampleBlockScale = new Scale3d(1);

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity textEnt = tr.GetObject(perText.ObjectId, OpenMode.ForRead) as Entity;
                BlockReference blk = tr.GetObject(perBlock.ObjectId, OpenMode.ForRead) as BlockReference;

                if (textEnt == null || blk == null)
                {
                    MessageBox.Show("Không đọc được text hoặc block mẫu.", "Lỗi");
                    return;
                }

                sampleBlockName = blk.Name;
                sampleBlockPos = blk.Position;
                sampleBlockRotation = blk.Rotation;
                sampleBlockScale = blk.ScaleFactors;

                if (textEnt is DBText dbText)
                {
                    sampleTextString = dbText.TextString;
                    sampleLayer = dbText.Layer;
                    sampleHeight = dbText.Height;
                    sampleRotation = dbText.Rotation;
                    sampleTextPos = dbText.AlignmentPoint;
                    if (sampleTextPos.IsEqualTo(Point3d.Origin) ||
                        dbText.Justify == AttachmentPoint.BaseLeft)
                        sampleTextPos = dbText.Position;
                    sampleJustify = dbText.Justify;
                }
                else if (textEnt is MText mText)
                {
                    isMText = true;
                    sampleTextString = mText.Contents;
                    sampleLayer = mText.Layer;
                    sampleHeight = mText.TextHeight;
                    sampleRotation = mText.Rotation;
                    sampleTextPos = mText.Location;
                }

                tr.Commit();
            }

            if (string.IsNullOrWhiteSpace(sampleTextString) ||
                string.IsNullOrWhiteSpace(sampleBlockName))
            {
                MessageBox.Show("Text hoặc Block mẫu không hợp lệ.", "Cảnh báo");
                return;
            }

            // Vector offset từ block → text (trong hệ tọa độ thế giới)
            Vector3d offsetWorld = sampleTextPos - sampleBlockPos;

            // 3. Quét chọn vùng
            ed.WriteMessage(
                $"\n[ĐẶT THIẾT BỊ MẪU] Block mẫu: {sampleBlockName} | Text: {sampleTextString}");
            ed.WriteMessage("\nQuét chọn vùng chứa các block cần đặt text (Enter kết thúc): ");

            var pso = new PromptSelectionOptions
            {
                MessageForAdding = "\nChọn các đối tượng trong vùng (hoặc quét cửa sổ): ",
                AllowDuplicates = false
            };

            // Chỉ lấy BlockReference
            var filter = new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Start, "INSERT")
            });

            var psr = ed.GetSelection(pso, filter);
            if (psr.Status != PromptStatus.OK || psr.Value.Count == 0)
            {
                ed.WriteMessage("\nKhông có block nào được chọn.");
                return;
            }

            int placed = 0;
            int skippedSame = 0;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(
                    db.CurrentSpaceId, OpenMode.ForWrite);

                EnsureLayerExists(tr, db, sampleLayer, false);

                foreach (SelectedObject so in psr.Value)
                {
                    if (so == null || so.ObjectId == perBlock.ObjectId)
                    {
                        skippedSame++;
                        continue; // bỏ qua block mẫu
                    }

                    BlockReference other = tr.GetObject(so.ObjectId, OpenMode.ForRead) as BlockReference;
                    if (other == null) continue;

                    // Chỉ lấy block cùng tên
                    if (!string.Equals(other.Name, sampleBlockName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Tính vị trí text mới = vị trí block mới + offset (có xét scale & rotation nếu cần)
                    // Đơn giản: giữ offset theo hệ thế giới (đủ dùng hầu hết trường hợp)
                    Point3d newTextPos = other.Position + offsetWorld;

                    // Nếu block bị xoay / scale khác mẫu thì có thể biến đổi offset
                    // (nâng cao – hiện tại giữ offset cố định cho ổn định)

                    if (isMText)
                    {
                        MText mt = new MText();
                        mt.SetDatabaseDefaults(db);
                        mt.Contents = sampleTextString;
                        mt.TextHeight = sampleHeight;
                        mt.Layer = sampleLayer;
                        mt.ColorIndex = 256;
                        mt.Location = newTextPos;
                        mt.Rotation = sampleRotation;
                        btr.AppendEntity(mt);
                        tr.AddNewlyCreatedDBObject(mt, true);
                    }
                    else
                    {
                        DBText txt = new DBText();
                        txt.SetDatabaseDefaults(db);
                        txt.TextStyleId = db.Textstyle;
                        txt.TextString = sampleTextString;
                        txt.Height = sampleHeight;
                        txt.WidthFactor = 1.0;
                        txt.Layer = sampleLayer;
                        txt.ColorIndex = 256;
                        txt.Justify = sampleJustify;
                        txt.Rotation = sampleRotation;

                        if (sampleJustify == AttachmentPoint.BaseLeft ||
                            sampleJustify == AttachmentPoint.BaseCenter ||
                            sampleJustify == AttachmentPoint.BaseRight ||
                            sampleJustify == AttachmentPoint.BaseAlign ||
                            sampleJustify == AttachmentPoint.BaseFit)
                        {
                            txt.Position = newTextPos;
                        }
                        else
                        {
                            txt.AlignmentPoint = newTextPos;
                            try { txt.AdjustAlignment(db); } catch { }
                        }

                        btr.AppendEntity(txt);
                        tr.AddNewlyCreatedDBObject(txt, true);
                    }

                    placed++;
                }

                tr.Commit();
            }

            ed.Regen();
            ed.WriteMessage(
                $"\n[ĐẶT THIẾT BỊ MẪU] Đã đặt {placed} text trên các block '{sampleBlockName}'." +
                (skippedSame > 0 ? $" (bỏ qua {skippedSame} block mẫu)" : ""));
        }

        // ==================== VAN ====================

        private void LstLoaiVan_SelectionChanged(
            object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            ValveUiContext ctx = GetValveContext(sender);
            CapNhatSizeVan(ctx);
            CapNhatMauVanTheoPrefix(ctx);
        }

        private void TxtLoaiVanBoSung_KeyDown(
            object sender,
            WpfKeyEventArgs e)
        {
            if (e.Key != WpfKey.Enter)
                return;

            ValveUiContext ctx = GetValveContext(sender);

            if (ctx?.TxtLoaiVanThem == null ||
                ctx.LstLoaiVan == null)
            {
                return;
            }

            string newType = ctx.TxtLoaiVanThem.Text.Trim();

            if (string.IsNullOrWhiteSpace(newType))
                return;

            bool existed =
                ctx.LstLoaiVan.Items
                    .Cast<object>()
                    .Any(x => LayNoiDungItem(x).Equals(
                        newType,
                        StringComparison.OrdinalIgnoreCase));

            if (!existed)
            {
                ctx.LstLoaiVan.Items.Add(
                    new WpfListBoxItem
                    {
                        Content = newType.ToUpper()
                    });
            }

            foreach (object item in ctx.LstLoaiVan.Items)
            {
                if (LayNoiDungItem(item).Equals(
                    newType,
                    StringComparison.OrdinalIgnoreCase))
                {
                    ctx.LstLoaiVan.SelectedItem = item;
                    ctx.LstLoaiVan.ScrollIntoView(item);
                    break;
                }
            }

            ctx.TxtLoaiVanThem.Text = "";
            CapNhatMauVanTheoPrefix(ctx);
            e.Handled = true;
        }

        private void LstLoaiVan_KeyDown(
            object sender,
            WpfKeyEventArgs e)
        {
            if (e.Key != WpfKey.Delete)
                return;

            ValveUiContext ctx = GetValveContext(sender);
            WpfListBox lst = sender as WpfListBox;

            // Ưu tiên list đang gửi sự kiện (Van hoặc Van gió)
            if (lst == null)
                lst = ctx?.LstLoaiVan;

            if (lst == null || lst.SelectedItem == null)
                return;

            if (lst.Items.Count <= 1)
                return;

            object selected = lst.SelectedItem;
            lst.Items.Remove(selected);

            if (lst.Items.Count > 0)
                lst.SelectedIndex = 0;

            CapNhatSizeVan(ctx);
            CapNhatMauVanTheoPrefix(ctx);
            e.Handled = true;
        }

        private void TxtCustomSizeVan_KeyDown(
            object sender,
            WpfKeyEventArgs e)
        {
            if (e.Key != WpfKey.Enter)
                return;

            ValveUiContext ctx = GetValveContext(sender);

            if (ctx?.TxtSizeThem == null)
                return;

            string newSize = ctx.TxtSizeThem.Text.Trim();

            if (string.IsNullOrWhiteSpace(newSize))
                return;

            List<string> currentSizes =
                ctx.Sizes.Select(x => x.SizeName).ToList();

            if (!currentSizes.Contains(
                newSize,
                StringComparer.OrdinalIgnoreCase))
            {
                currentSizes.Add(newSize);
            }

            CapNhatVaSapXepDanhSachSizeVan(
                ctx,
                currentSizes,
                newSize);

            ctx.TxtSizeThem.Text = "";
            e.Handled = true;
        }

        private void LstSizeVan_KeyDown(
            object sender,
            WpfKeyEventArgs e)
        {
            if (e.Key != WpfKey.Delete)
                return;

            ValveUiContext ctx = GetValveContext(sender);

            if (ctx?.LstSize?.SelectedItem is PipeSizeItem selected)
            {
                ctx.Sizes.Remove(selected);
                e.Handled = true;
            }
        }

        private void BtnColorVan_Click(
            object sender,
            RoutedEventArgs e)
        {
            WpfButton btn = sender as WpfButton;
            PipeSizeItem item = btn?.DataContext as PipeSizeItem;

            if (item == null)
                return;

            ValveUiContext ctx = GetValveContext(sender);
            string layerPrefix = GetValveLayerPrefix(ctx);
            string layerName = $"{layerPrefix}_{item.SizeName}";

            var cd = new Autodesk.AutoCAD.Windows.ColorDialog();

            cd.Color =
                Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                    ColorMethod.ByAci,
                    item.AciColor);

            if (cd.ShowDialog() == WinFormsDialogResult.OK)
            {
                short newAci = cd.Color.ColorIndex;
                _userCustomColors[layerName] = newAci;

                item.AciColor = newAci;
                item.LayerColorBrush = GetBrushFromAci(newAci);

                var doc =
                    Autodesk.AutoCAD.ApplicationServices.Core.Application
                        .DocumentManager
                        .MdiActiveDocument;

                if (doc != null)
                {
                    using (doc.LockDocument())
                    {
                        using (Transaction tr =
                            doc.Database.TransactionManager
                                .StartTransaction())
                        {
                            LayerTable lt =
                                (LayerTable)tr.GetObject(
                                    doc.Database.LayerTableId,
                                    OpenMode.ForRead);

                            if (lt.Has(layerName))
                            {
                                LayerTableRecord ltr =
                                    (LayerTableRecord)tr.GetObject(
                                        lt[layerName],
                                        OpenMode.ForWrite);

                                ltr.Color =
                                    Autodesk.AutoCAD.Colors.Color
                                        .FromColorIndex(
                                            ColorMethod.ByAci,
                                            newAci);
                            }

                            tr.Commit();
                        }
                    }

                    doc.Editor.Regen();
                }
            }
        }

        private void BtnDatVan_Click(
            object sender,
            RoutedEventArgs e)
        {
            ValveUiContext ctx = GetValveContext(sender);

            var doc =
                Autodesk.AutoCAD.ApplicationServices.Core.Application
                    .DocumentManager
                    .MdiActiveDocument;

            if (doc == null)
                return;

            var db = doc.Database;
            var ed = doc.Editor;

            string size =
                (ctx?.LstSize?.SelectedItem as PipeSizeItem)
                    ?.SizeName ?? "";

            if (string.IsNullOrEmpty(size))
            {
                MessageBox.Show(
                    "Vui lòng chọn kích thước van trước khi đặt!",
                    "Cảnh báo");
                return;
            }

            string valveType = GetSelectedValveTypeName(ctx);
            string layerName =
                $"{GetValveLayerPrefix(ctx)}_{size}";
            string displayText = $"{valveType} {size}";

            // Chiều cao chữ cố định → nét đều, không phụ thuộc size van
            double textHeight = MinimumLabelTextHeight;

            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();

            using (doc.LockDocument())
            {
                using (Transaction trInit =
                    db.TransactionManager.StartTransaction())
                {
                    EnsureLayerExists(
                        trInit,
                        db,
                        layerName,
                        false);

                    LayerTable lt =
                        (LayerTable)trInit.GetObject(
                            db.LayerTableId,
                            OpenMode.ForRead);

                    db.Clayer = lt[layerName];
                    trInit.Commit();
                }
            }

            int placedCount = 0;

            ed.WriteMessage(
                $"\n[ĐẶT VAN] Layer: {layerName} | " +
                $"Bấm chuột để đặt text, ESC để kết thúc.");

            while (true)
            {
                PromptPointOptions ppo =
                    new PromptPointOptions(
                        $"\nChọn vị trí đặt van ({displayText}) " +
                        $"[đã đặt {placedCount}] <ESC kết thúc>: ")
                    {
                        AllowNone = true
                    };

                PromptPointResult ppr = ed.GetPoint(ppo);

                if (ppr.Status != PromptStatus.OK)
                    break;

                Point3d pt = ppr.Value;

                using (doc.LockDocument())
                using (Transaction tr =
                    db.TransactionManager.StartTransaction())
                {
                    BlockTableRecord btr =
                        (BlockTableRecord)tr.GetObject(
                            db.CurrentSpaceId,
                            OpenMode.ForWrite);

                    EnsureLayerExists(
                        tr,
                        db,
                        layerName,
                        false);

                    DBText txt = new DBText();
                    txt.SetDatabaseDefaults(db);
                    txt.TextStyleId = db.Textstyle;   // font/nét thống nhất
                    txt.TextString = displayText;
                    txt.Height = textHeight;
                    txt.WidthFactor = 1.0;
                    txt.Layer = layerName;
                    txt.ColorIndex = 256;
                    txt.Justify = AttachmentPoint.MiddleCenter;
                    txt.AlignmentPoint = pt;
                    txt.Position = pt;

                    btr.AppendEntity(txt);
                    tr.AddNewlyCreatedDBObject(txt, true);
                    txt.AdjustAlignment(db);

                    tr.Commit();
                    placedCount++;
                }

                ed.Regen();
            }

            ed.WriteMessage(
                $"\n[ĐẶT VAN] Đã đặt {placedCount} van " +
                $"trên Layer: {layerName}");
        }

        // ==================== THIẾT BỊ ====================

        private void LstLoaiThietBi_SelectionChanged(
            object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            EquipUiContext ctx = GetEquipContext(sender);
            CapNhatModelTheoLoaiThietBi(ctx);
        }

        private void TxtLoaiThietBiBoSung_KeyDown(
            object sender,
            WpfKeyEventArgs e)
        {
            if (e.Key != WpfKey.Enter)
                return;

            EquipUiContext ctx = GetEquipContext(sender);

            if (ctx?.TxtLoaiThem == null || ctx.LstLoai == null)
                return;

            string newType = ctx.TxtLoaiThem.Text.Trim();

            if (string.IsNullOrWhiteSpace(newType))
                return;

            bool existed =
                ctx.LstLoai.Items
                    .Cast<object>()
                    .Any(x => LayNoiDungItem(x).Equals(
                        newType,
                        StringComparison.OrdinalIgnoreCase));

            if (!existed)
            {
                ctx.LstLoai.Items.Add(
                    new WpfListBoxItem
                    {
                        Content = newType.ToUpper()
                    });
            }

            foreach (object item in ctx.LstLoai.Items)
            {
                if (LayNoiDungItem(item).Equals(
                    newType,
                    StringComparison.OrdinalIgnoreCase))
                {
                    ctx.LstLoai.SelectedItem = item;
                    ctx.LstLoai.ScrollIntoView(item);
                    break;
                }
            }

            ctx.TxtLoaiThem.Text = "";
            CapNhatModelTheoLoaiThietBi(ctx);
            e.Handled = true;
        }

        private void LstLoaiThietBi_KeyDown(
            object sender,
            WpfKeyEventArgs e)
        {
            if (e.Key != WpfKey.Delete)
                return;

            EquipUiContext ctx = GetEquipContext(sender);

            if (ctx?.LstLoai == null ||
                ctx.LstLoai.SelectedItem == null)
            {
                return;
            }

            object selected = ctx.LstLoai.SelectedItem;
            ctx.LstLoai.Items.Remove(selected);

            if (ctx.LstLoai.Items.Count > 0)
                ctx.LstLoai.SelectedIndex = 0;

            CapNhatModelTheoLoaiThietBi(ctx);
            e.Handled = true;
        }

        private void TxtCustomSizeThietBi_KeyDown(
            object sender,
            WpfKeyEventArgs e)
        {
            if (e.Key != WpfKey.Enter)
                return;

            EquipUiContext ctx = GetEquipContext(sender);

            if (ctx?.TxtSizeThem == null)
                return;

            string newSize = ctx.TxtSizeThem.Text.Trim();

            if (string.IsNullOrWhiteSpace(newSize))
                return;

            List<string> currentSizes =
                ctx.Sizes.Select(x => x.SizeName).ToList();

            if (!currentSizes.Contains(
                newSize,
                StringComparer.OrdinalIgnoreCase))
            {
                currentSizes.Add(newSize);
            }

            CapNhatVaSapXepDanhSachSizeEquip(
                ctx,
                currentSizes,
                newSize);

            ctx.TxtSizeThem.Text = "";
            e.Handled = true;
        }

        private void LstSizeThietBi_KeyDown(
            object sender,
            WpfKeyEventArgs e)
        {
            if (e.Key != WpfKey.Delete)
                return;

            EquipUiContext ctx = GetEquipContext(sender);

            if (ctx?.LstSize?.SelectedItem is PipeSizeItem selected)
            {
                ctx.Sizes.Remove(selected);
                e.Handled = true;
            }
        }

        private void BtnColorThietBi_Click(
            object sender,
            RoutedEventArgs e)
        {
            WpfButton btn = sender as WpfButton;
            PipeSizeItem item = btn?.DataContext as PipeSizeItem;

            if (item == null)
                return;

            EquipUiContext ctx = GetEquipContext(sender);
            string layerPrefix = GetEquipLayerPrefix(ctx);
            string layerName =
                $"{layerPrefix}_{CleanLayerText(item.SizeName)}";

            var cd = new Autodesk.AutoCAD.Windows.ColorDialog();

            cd.Color =
                Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                    ColorMethod.ByAci,
                    item.AciColor);

            if (cd.ShowDialog() == WinFormsDialogResult.OK)
            {
                short newAci = cd.Color.ColorIndex;
                _userCustomColors[layerName] = newAci;

                item.AciColor = newAci;
                item.LayerColorBrush = GetBrushFromAci(newAci);

                var doc =
                    Autodesk.AutoCAD.ApplicationServices.Core.Application
                        .DocumentManager
                        .MdiActiveDocument;

                if (doc != null)
                {
                    using (doc.LockDocument())
                    {
                        using (Transaction tr =
                            doc.Database.TransactionManager
                                .StartTransaction())
                        {
                            LayerTable lt =
                                (LayerTable)tr.GetObject(
                                    doc.Database.LayerTableId,
                                    OpenMode.ForRead);

                            if (lt.Has(layerName))
                            {
                                LayerTableRecord ltr =
                                    (LayerTableRecord)tr.GetObject(
                                        lt[layerName],
                                        OpenMode.ForWrite);

                                ltr.Color =
                                    Autodesk.AutoCAD.Colors.Color
                                        .FromColorIndex(
                                            ColorMethod.ByAci,
                                            newAci);
                            }

                            tr.Commit();
                        }
                    }

                    doc.Editor.Regen();
                }
            }
        }

        private void BtnDatThietBi_Click(
            object sender,
            RoutedEventArgs e)
        {
            EquipUiContext ctx = GetEquipContext(sender);

            var doc =
                Autodesk.AutoCAD.ApplicationServices.Core.Application
                    .DocumentManager
                    .MdiActiveDocument;

            if (doc == null)
                return;

            var db = doc.Database;
            var ed = doc.Editor;

            string equipType = GetSelectedEquipTypeName(ctx);
            string loaiKey = (equipType ?? "").ToUpperInvariant();
            bool isDauPhun =
                string.IsNullOrEmpty(ctx?.Suffix) &&
                (loaiKey.Contains("ĐẦU PHUN") ||
                 loaiKey.Contains("DAU PHUN") ||
                 loaiKey.Contains("PHUN"));

            bool isMayLanh =
                ctx?.Suffix == "ACMV" &&
                (loaiKey.Contains("MÁY LẠNH") ||
                 loaiKey.Contains("MAY LANH"));

            bool isQuat =
                ctx?.Suffix == "ACMV" &&
                (loaiKey.Contains("QUẠT") ||
                 loaiKey.Contains("QUAT"));

            string model;

            if (isDauPhun)
            {
                model = BuildDauPhunModelText();
            }
            else if (isMayLanh)
            {
                model = BuildMayLanhModelText();
            }
            else if (isQuat)
            {
                model = BuildQuatModelText();
            }
            else
            {
                model =
                    (ctx?.LstSize?.SelectedItem as PipeSizeItem)
                        ?.SizeName ?? "";
            }

            if (string.IsNullOrEmpty(model))
            {
                MessageBox.Show(
                    "Vui lòng chọn model/kích thước thiết bị trước khi đặt!",
                    "Cảnh báo");
                return;
            }

            string layerName =
                $"{GetEquipLayerPrefix(ctx)}_{CleanLayerText(model)}";
            string displayText = (isDauPhun || isMayLanh || isQuat)
                ? model
                : $"{equipType} {model}";

            // Chiều cao chữ cố định → nét đều
            double textHeight = MinimumLabelTextHeight;

            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();

            using (doc.LockDocument())
            {
                using (Transaction trInit =
                    db.TransactionManager.StartTransaction())
                {
                    EnsureLayerExists(
                        trInit,
                        db,
                        layerName,
                        false);

                    LayerTable lt =
                        (LayerTable)trInit.GetObject(
                            db.LayerTableId,
                            OpenMode.ForRead);

                    db.Clayer = lt[layerName];
                    trInit.Commit();
                }
            }

            int placedCount = 0;

            ed.WriteMessage(
                $"\n[ĐẶT THIẾT BỊ] Layer: {layerName} | " +
                $"Bấm chuột để đặt text, ESC để kết thúc.");

            while (true)
            {
                PromptPointOptions ppo =
                    new PromptPointOptions(
                        $"\nChọn vị trí đặt thiết bị ({displayText}) " +
                        $"[đã đặt {placedCount}] <ESC kết thúc>: ")
                    {
                        AllowNone = true
                    };

                PromptPointResult ppr = ed.GetPoint(ppo);

                if (ppr.Status != PromptStatus.OK)
                    break;

                Point3d pt = ppr.Value;

                using (doc.LockDocument())
                using (Transaction tr =
                    db.TransactionManager.StartTransaction())
                {
                    BlockTableRecord btr =
                        (BlockTableRecord)tr.GetObject(
                            db.CurrentSpaceId,
                            OpenMode.ForWrite);

                    EnsureLayerExists(
                        tr,
                        db,
                        layerName,
                        false);

                    DBText txt = new DBText();
                    txt.SetDatabaseDefaults(db);
                    txt.TextStyleId = db.Textstyle;   // font/nét thống nhất
                    txt.TextString = displayText;
                    txt.Height = textHeight;
                    txt.WidthFactor = 1.0;
                    txt.Layer = layerName;
                    txt.ColorIndex = 256;
                    txt.Justify = AttachmentPoint.MiddleCenter;
                    txt.AlignmentPoint = pt;
                    txt.Position = pt;

                    btr.AppendEntity(txt);
                    tr.AddNewlyCreatedDBObject(txt, true);
                    txt.AdjustAlignment(db);

                    tr.Commit();
                    placedCount++;
                }

                ed.Regen();
            }

            ed.WriteMessage(
                $"\n[ĐẶT THIẾT BỊ] Đã đặt {placedCount} thiết bị " +
                $"trên Layer: {layerName}");
        }

        private class PipeUiContext
        {
            public string Suffix { get; set; }
            public string HeThongMacDinh { get; set; }
            public WpfComboBox CmbHeThong { get; set; }
            public WpfComboBox CmbVatLieu { get; set; }
            public WpfListBox LstVatLieu { get; set; }
            public WpfTextBox TxtVatLieuThem { get; set; }
            public WpfListBox LstSize { get; set; }
            public WpfTextBox TxtSizeThem { get; set; }
            public ObservableCollection<PipeSizeItem> Sizes { get; set; }
        }

        private class ValveUiContext
        {
            public string Suffix { get; set; }
            public string HeThongMacDinh { get; set; }
            public WpfComboBox CmbHeThong { get; set; }
            public WpfListBox LstLoaiVan { get; set; }
            public WpfTextBox TxtLoaiVanThem { get; set; }
            public WpfListBox LstSize { get; set; }
            public WpfTextBox TxtSizeThem { get; set; }
            public ObservableCollection<PipeSizeItem> Sizes { get; set; }
        }

        private class EquipUiContext
        {
            public string Suffix { get; set; }
            public string HeThongMacDinh { get; set; }
            public WpfComboBox CmbHeThong { get; set; }
            public WpfListBox LstLoai { get; set; }
            public WpfTextBox TxtLoaiThem { get; set; }
            public WpfListBox LstSize { get; set; }
            public WpfTextBox TxtSizeThem { get; set; }
            public ObservableCollection<PipeSizeItem> Sizes { get; set; }
        }
    }

    public class PipeSizeItem : INotifyPropertyChanged
    {
        private string _sizeName;

        public string SizeName
        {
            get
            {
                return _sizeName;
            }
            set
            {
                _sizeName = value;
                OnPropertyChanged("SizeName");
            }
        }

        private System.Windows.Media.SolidColorBrush
            _layerColorBrush;

        public System.Windows.Media.SolidColorBrush
            LayerColorBrush
        {
            get
            {
                return _layerColorBrush;
            }
            set
            {
                _layerColorBrush = value;
                OnPropertyChanged("LayerColorBrush");
            }
        }

        public short AciColor { get; set; }

        public event PropertyChangedEventHandler
            PropertyChanged;

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(name));
        }
    }

    public class ThongKeOng
    {
        public int STT { get; set; }
        public string TenLayer { get; set; }
        public double SoLuong { get; set; }
        public double M2 { get; set; }
        public string HeThongSort { get; set; }
        public double KichThuocSort { get; set; }
    }
}