#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
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

            return baseSize;
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

            WpfTextBox txt = sender as WpfTextBox;
            if (txt == null)
                return;

            string value = (txt.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value))
                return;

            string name = txt.Name ?? "";
            string listName = null;

            if (name.Contains("SizeOngGio"))
                listName = "LstSizeOngGioACMV";
            else if (name.Contains("CnEi"))
                listName = "LstCnEiOngGioACMV";

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

                // Nếu là size ống gió → đồng bộ vào ctx.Sizes
                if (listName == "LstSizeOngGioACMV" && _ctxACMV != null)
                {
                    List<string> sizes =
                        _ctxACMV.Sizes
                            .Select(x => x.SizeName)
                            .ToList();

                    if (!sizes.Contains(
                        value,
                        StringComparer.OrdinalIgnoreCase))
                    {
                        sizes.Add(value);
                        CapNhatVaSapXepDanhSachSize(_ctxACMV, sizes, value);
                    }
                }
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
            lst.Items.Remove(selected);

            if (lst.Name == "LstSizeOngGioACMV" && _ctxACMV != null)
            {
                var item = _ctxACMV.Sizes.FirstOrDefault(
                    x => x.SizeName.Equals(
                        removed,
                        StringComparison.OrdinalIgnoreCase));

                if (item != null)
                    _ctxACMV.Sizes.Remove(item);
            }

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
        private static bool LaLayerOng(string layerName)
        {
            if (string.IsNullOrWhiteSpace(layerName))
                return false;

            string u = layerName.Trim().ToUpperInvariant()
                .Replace("Á", "A").Replace("À", "A").Replace("Ả", "A")
                .Replace("Ã", "A").Replace("Ạ", "A")
                .Replace("É", "E").Replace("È", "E").Replace("Ẻ", "E")
                .Replace("Ẽ", "E").Replace("Ẹ", "E")
                .Replace("Ó", "O").Replace("Ò", "O").Replace("Ỏ", "O")
                .Replace("Õ", "O").Replace("Ọ", "O")
                .Replace("Ú", "U").Replace("Ù", "U").Replace("Ủ", "U")
                .Replace("Ũ", "U").Replace("Ụ", "U")
                .Replace("Í", "I").Replace("Ì", "I").Replace("Ỉ", "I")
                .Replace("Ĩ", "I").Replace("Ị", "I")
                .Replace("Ý", "Y").Replace("Ỳ", "Y").Replace("Ỷ", "Y")
                .Replace("Ỹ", "Y").Replace("Ỵ", "Y")
                .Replace("Đ", "D");

            // Từ khóa vật liệu ống
            string[] vatLieuOng =
            {
                "TRANG KEM", "TRANGKEM", "HDPE", "THEP DEN", "THEPDEN",
                "INOX", "NHUNG NONG", "NHUNGNONG", "UPVC", "ONG DONG",
                "ONGDONG", "OG THAI", "OG HUT", "OG LANH", "OG CAP",
                "PPR", "PVC", "PEHD", "THEP"
            };

            bool coVatLieu = false;
            foreach (var vl in vatLieuOng)
            {
                if (u.Contains(vl))
                {
                    coVatLieu = true;
                    break;
                }
            }

            // Có DN / size ống điển hình
            bool coSizeOng =
                Regex.IsMatch(u, @"DN\s*\d+") ||
                Regex.IsMatch(u, @"D\d{2,}") ||
                Regex.IsMatch(u, @"\d+\s*X\s*\d+"); // ống gió WxH

            return coVatLieu || (coSizeOng && !LaLayerThietBiHoacVan(layerName));
        }

        private static bool LaLayerThietBiHoacVan(string layerName)
        {
            if (string.IsNullOrWhiteSpace(layerName))
                return false;

            string u = layerName.Trim().ToUpperInvariant()
                .Replace("Á", "A").Replace("À", "A").Replace("Ả", "A")
                .Replace("Ã", "A").Replace("Ạ", "A")
                .Replace("É", "E").Replace("È", "E").Replace("Ẻ", "E")
                .Replace("Ẽ", "E").Replace("Ẹ", "E")
                .Replace("Ó", "O").Replace("Ò", "O").Replace("Ỏ", "O")
                .Replace("Õ", "O").Replace("Ọ", "O")
                .Replace("Ú", "U").Replace("Ù", "U").Replace("Ủ", "U")
                .Replace("Ũ", "U").Replace("Ụ", "U")
                .Replace("Í", "I").Replace("Ì", "I").Replace("Ỉ", "I")
                .Replace("Ĩ", "I").Replace("Ị", "I")
                .Replace("Đ", "D");

            string[] keywords =
            {
                // Van
                "VAN", "V.CONG", "VCONG", "Y LOC", "YLOC", "KNM",
                "VCD", "MFD", "PRD", "LOUVER", "DAMPER", "MG CAP", "MG THAI",
                // Thiết bị
                "BINH", "DAU PHUN", "DAUPHUN", "PHUN",
                "MAY LANH", "MAYLANH", "QUAT", "BOM",
                "DONG HO", "DONGHO", "BON",
                "CASSETTE", "GAN TUONG", "AM TRAN", "AP TRAN", "DAN NONG",
                "HL-", "HX-", "HN-", " HL ", " HX ", " HN "
            };

            foreach (var k in keywords)
            {
                if (u.Contains(k))
                    return true;
            }

            return false;
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

                XuatBangRaCad(danhSachDaSapXep);
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

                        // Chỉ text trên layer do tool tạo VÀ là van / thiết bị
                        // (bỏ chữ size ống trên layer TRÁNG KẼM_DN..., HDPE_DN...)
                        if (!LaLayerCuaTool(layer))
                            continue;
                        if (!LaLayerThietBiHoacVan(layer))
                            continue;

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

        private void BtnTimDoiTuongThongKe_Click(
            object sender,
            RoutedEventArgs e)
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

            try
            {
                while (true)
                {
                    ed.WriteMessage(
                        "\n[TÌM ĐỐI TƯỢNG] Click vào TÊN LAYER trên bảng (ESC thoát): ");

                    // Không dùng GetEntity (dễ "Nothing Selected") — lấy điểm click
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
                        ed.WriteMessage("\n[TÌM ĐỐI TƯỢNG] Đã thoát.");
                        break;
                    }

                    if (ppr.Status != PromptStatus.OK)
                        continue;

                    // Có click mới → xóa đường chỉ lần trước (giữ đường đến lúc này)
                    XoaDuongChiTam(doc, db, tempLineIds);
                    tempLineIds.Clear();
                    ed.Regen();

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
                        layerName.StartsWith("SỐ LƯỢNG", StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show(
                            "Không đọc được Tên Layer tại vị trí click.\nHãy click vào chữ tên Layer trong bảng thống kê.",
                            "Cảnh báo");
                        continue;
                    }

                    List<Point3d> targets = new List<Point3d>();

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

                        if (plines.Count > 0)
                        {
                            foreach (Polyline pl in plines)
                                targets.Add(LayDiemGiuaPolyline(pl));
                        }
                        else
                        {
                            foreach (Entity t in texts)
                                targets.Add(LayDiemDaiDien(t));
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

                    using (doc.LockDocument())
                    using (Transaction tr =
                        db.TransactionManager.StartTransaction())
                    {
                        EnsureTempFindLayer(tr, db);

                        BlockTableRecord btr =
                            (BlockTableRecord)tr.GetObject(
                                db.CurrentSpaceId, OpenMode.ForWrite);

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

                        tr.Commit();
                    }

                    ed.Regen();
                    ed.WriteMessage(
                        $"\n[TÌM ĐỐI TƯỢNG] {layerName} → {targets.Count} đường chỉ. " +
                        "Click layer khác để tìm tiếp, ESC để thoát.");
                }
            }
            catch (System.Exception ex)
            {
                try { XoaDuongChiTam(doc, db, tempLineIds); } catch { }
                MessageBox.Show(
                    "Lỗi tìm đối tượng:\n" + ex.Message,
                    "Lỗi");
            }
            finally
            {
                XoaDuongChiTam(doc, db, tempLineIds);
                try { ed.Regen(); } catch { }
            }
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
                return;

            lt.UpgradeOpen();
            LayerTableRecord ltr = new LayerTableRecord();
            ltr.Name = TempFindLayerName;
            ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                ColorMethod.ByAci, 1);
            ltr.IsOff = false;
            lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
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

        private void XuatBangRaCad(
            List<ThongKeOng> data,
            string tieuDe = "BẢNG THỐNG KÊ KHỐI LƯỢNG ỐNG",
            string cotSoLuong = "SỐ LƯỢNG (m)")
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

                tb.SetSize(data.Count + 2, 3);
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

                // STT | LAYER (giảm còn ~2/3 bề rộng trước) | Số lượng
                tb.Columns[0].Width = 900.0 * sf;     // STT
                tb.Columns[1].Width = 4800.0 * sf;    // LAYER = 2/3 của 7200
                tb.Columns[2].Width = 2200.0 * sf;    // Số lượng

                // Gộp hàng tiêu đề 3 cột
                try
                {
                    tb.MergeCells(
                        CellRange.Create(tb, 0, 0, 0, 2));
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

                for (int i = 0; i < 3; i++)
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
                        item.SoLuong.ToString();

                    tb.Cells[row, 2].Alignment =
                        CellAlignment.MiddleCenter;

                    row++;
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
        public string HeThongSort { get; set; }
        public double KichThuocSort { get; set; }
    }
}