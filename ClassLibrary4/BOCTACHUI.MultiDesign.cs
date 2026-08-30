#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClassLibrary4
{
    /// <summary>
    /// Multi-system design workspace hosted inside tab 7 THIẾT KẾ.
    /// PCCC keeps the existing UI/logic. ACMV, CTN and Electrical are
    /// source-integrated preliminary design calculators for tender/QS use.
    /// All outputs are preliminary and must be checked against project data,
    /// manufacturer data and applicable standards by the responsible engineer.
    /// </summary>
    public partial class BOCTACHUI
    {
        private bool _multiDesignUiInitialized;
        private readonly Dictionary<string, Button> _designNavButtons =
            new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, UIElement> _designPanels =
            new Dictionary<string, UIElement>(StringComparer.OrdinalIgnoreCase);

        private TextBox _hvacArea;
        private TextBox _hvacPeople;
        private TextBox _hvacLoadDensity;
        private TextBox _hvacPeopleLoad;
        private TextBox _hvacDiversity;
        private TextBox _hvacDeltaT;
        private TextBox _hvacFreshAir;
        private TextBox _hvacVelocity;
        private TextBlock _hvacResult;

        private TextBox _ctnArea;
        private TextBox _ctnPeople;
        private TextBox _ctnWaterRate;
        private TextBox _ctnPeakFactor;
        private TextBox _ctnUseHours;
        private TextBox _ctnVelocity;
        private TextBox _ctnReserveDays;
        private TextBox _ctnRoofArea;
        private TextBox _ctnRainfall;
        private TextBlock _ctnResult;

        private TextBox _elecArea;
        private TextBox _elecLoadDensity;
        private TextBox _elecDiversity;
        private TextBox _elecPowerFactor;
        private TextBox _elecVoltage;
        private TextBox _elecReserve;
        private ComboBox _elecPhase;
        private TextBlock _elecResult;

        private static readonly Brush DesignFireBrush =
            new SolidColorBrush(Color.FromRgb(183, 28, 28));
        private static readonly Brush DesignHvacBrush =
            new SolidColorBrush(Color.FromRgb(0, 121, 140));
        private static readonly Brush DesignCtnBrush =
            new SolidColorBrush(Color.FromRgb(21, 101, 192));
        private static readonly Brush DesignElecBrush =
            new SolidColorBrush(Color.FromRgb(245, 124, 0));
        private static readonly Brush DesignInactiveBrush =
            new SolidColorBrush(Color.FromRgb(224, 224, 224));

        /// <summary>
        /// Called once from FireDesignTab_Loaded. It wraps the existing PCCC
        /// content with a 4-system navigator without changing the original XAML.
        /// </summary>
        private void InitializeMultiSystemDesignUi()
        {
            if (_multiDesignUiInitialized || MainSystemTabs == null)
                return;

            TabItem designTab = null;
            foreach (object item in MainSystemTabs.Items)
            {
                if (item is TabItem tab &&
                    (tab.Header?.ToString() ?? string.Empty)
                        .StartsWith("7. THIẾT KẾ", StringComparison.OrdinalIgnoreCase))
                {
                    designTab = tab;
                    break;
                }
            }

            if (designTab == null || !(designTab.Content is UIElement originalFireContent))
                return;

            _multiDesignUiInitialized = true;

            designTab.Content = null;

            var host = new Grid();
            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var navBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(190, 190, 190)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background = Brushes.White,
                Padding = new Thickness(5, 5, 5, 5)
            };
            var nav = new UniformGrid { Columns = 4, Rows = 1 };
            navBorder.Child = nav;
            AddDesignNavButton(nav, "PCCC", "PCCC", DesignFireBrush);
            AddDesignNavButton(nav, "ACMV", "ACMV", DesignHvacBrush);
            AddDesignNavButton(nav, "CTN", "CTN", DesignCtnBrush);
            AddDesignNavButton(nav, "ĐIỆN", "DIEN", DesignElecBrush);
            Grid.SetRow(navBorder, 0);
            host.Children.Add(navBorder);

            Grid.SetRow(originalFireContent, 1);
            host.Children.Add(originalFireContent);
            _designPanels["PCCC"] = originalFireContent;

            UIElement hvac = BuildHvacDesignPanel();
            UIElement ctn = BuildCtnDesignPanel();
            UIElement elec = BuildElectricalDesignPanel();
            AddDesignPanel(host, "ACMV", hvac);
            AddDesignPanel(host, "CTN", ctn);
            AddDesignPanel(host, "DIEN", elec);

            designTab.Content = host;
            ShowDesignSystem("PCCC");
        }

        private void AddDesignPanel(Grid host, string key, UIElement panel)
        {
            panel.Visibility = Visibility.Collapsed;
            Grid.SetRow(panel, 1);
            host.Children.Add(panel);
            _designPanels[key] = panel;
        }

        private void AddDesignNavButton(UniformGrid nav, string caption, string key, Brush activeBrush)
        {
            var button = new Button
            {
                Content = caption,
                Height = 31,
                Margin = new Thickness(2),
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = key,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(170, 170, 170))
            };
            button.Click += (_, __) => ShowDesignSystem(key);
            _designNavButtons[key] = button;
            nav.Children.Add(button);
        }

        private void ShowDesignSystem(string key)
        {
            foreach (var pair in _designPanels)
                pair.Value.Visibility = pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            foreach (var pair in _designNavButtons)
            {
                bool selected = pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase);
                pair.Value.Foreground = selected ? Brushes.White : Brushes.Black;
                pair.Value.Background = selected
                    ? GetDesignActiveBrush(pair.Key)
                    : DesignInactiveBrush;
            }
        }

        private static Brush GetDesignActiveBrush(string key)
        {
            if (key.Equals("PCCC", StringComparison.OrdinalIgnoreCase)) return DesignFireBrush;
            if (key.Equals("ACMV", StringComparison.OrdinalIgnoreCase)) return DesignHvacBrush;
            if (key.Equals("CTN", StringComparison.OrdinalIgnoreCase)) return DesignCtnBrush;
            return DesignElecBrush;
        }

        private ScrollViewer BuildHvacDesignPanel()
        {
            var root = CreateSystemPanelRoot(
                "THIẾT KẾ ACMV",
                "TẢI LẠNH · LƯU LƯỢNG GIÓ · GIÓ TƯƠI · KÍCH THƯỚC ỐNG GIÓ",
                DesignHvacBrush);
            var body = (StackPanel)root.Content;

            AddSectionTitle(body, "1. DỮ LIỆU THIẾT KẾ");
            body.Children.Add(CreateInputRow("Diện tích điều hòa (m²)", "500", out _hvacArea));
            body.Children.Add(CreateInputRow("Số người", "50", out _hvacPeople));
            body.Children.Add(CreateInputRow("Suất tải lạnh nền (W/m²)", "120", out _hvacLoadDensity));
            body.Children.Add(CreateInputRow("Tải người + ẩn (W/người)", "120", out _hvacPeopleLoad));
            body.Children.Add(CreateInputRow("Hệ số đồng thời (%)", "90", out _hvacDiversity));
            body.Children.Add(CreateInputRow("Chênh nhiệt gió cấp/phòng (°C)", "10", out _hvacDeltaT));
            body.Children.Add(CreateInputRow("Gió tươi (L/s.người)", "10", out _hvacFreshAir));
            body.Children.Add(CreateInputRow("Vận tốc ống gió chính (m/s)", "6", out _hvacVelocity));

            body.Children.Add(CreateAreaTransferButton(_hvacArea));
            body.Children.Add(CreateCalculateButton("TÍNH ACMV SƠ BỘ", DesignHvacBrush, CalculateHvacDesign));
            _hvacResult = CreateResultBox();
            body.Children.Add(_hvacResult);
            body.Children.Add(CreateEngineeringWarning(
                "Kết quả ACMV là sơ bộ phục vụ concept/đấu thầu. Cần kiểm tra tải qua vỏ, tải tươi, thiết bị, ESP, độ ồn và catalogue thực tế."));
            return root;
        }

        private ScrollViewer BuildCtnDesignPanel()
        {
            var root = CreateSystemPanelRoot(
                "THIẾT KẾ CTN",
                "NHU CẦU NƯỚC · BỂ · BƠM · ỐNG CẤP · NƯỚC THẢI · NƯỚC MƯA",
                DesignCtnBrush);
            var body = (StackPanel)root.Content;

            AddSectionTitle(body, "1. DỮ LIỆU THIẾT KẾ");
            body.Children.Add(CreateInputRow("Diện tích công trình (m²)", "1000", out _ctnArea));
            body.Children.Add(CreateInputRow("Số người sử dụng", "100", out _ctnPeople));
            body.Children.Add(CreateInputRow("Nhu cầu nước (L/người.ngày)", "100", out _ctnWaterRate));
            body.Children.Add(CreateInputRow("Hệ số giờ cực đại", "2.0", out _ctnPeakFactor));
            body.Children.Add(CreateInputRow("Số giờ sử dụng/ngày", "12", out _ctnUseHours));
            body.Children.Add(CreateInputRow("Vận tốc ống cấp mục tiêu (m/s)", "1.5", out _ctnVelocity));
            body.Children.Add(CreateInputRow("Số ngày dự trữ bể", "1.0", out _ctnReserveDays));
            body.Children.Add(CreateInputRow("Diện tích mái thu nước (m²)", "1000", out _ctnRoofArea));
            body.Children.Add(CreateInputRow("Cường độ mưa thiết kế (mm/h)", "180", out _ctnRainfall));

            body.Children.Add(CreateAreaTransferButton(_ctnArea));
            body.Children.Add(CreateCalculateButton("TÍNH CTN SƠ BỘ", DesignCtnBrush, CalculateCtnDesign));
            _ctnResult = CreateResultBox();
            body.Children.Add(_ctnResult);
            body.Children.Add(CreateEngineeringWarning(
                "Kết quả CTN là sơ bộ. Cần đối chiếu công năng, số thiết bị vệ sinh, áp lực nguồn, cao độ, độ dốc, cường độ mưa địa phương và tiêu chuẩn dự án."));
            return root;
        }

        private ScrollViewer BuildElectricalDesignPanel()
        {
            var root = CreateSystemPanelRoot(
                "THIẾT KẾ ĐIỆN",
                "PHỤ TẢI · DÒNG TÍNH TOÁN · CB · CÁP · MÁY BIẾN ÁP",
                DesignElecBrush);
            var body = (StackPanel)root.Content;

            AddSectionTitle(body, "1. DỮ LIỆU THIẾT KẾ");
            body.Children.Add(CreateInputRow("Diện tích sử dụng (m²)", "1000", out _elecArea));
            body.Children.Add(CreateInputRow("Mật độ phụ tải (W/m²)", "80", out _elecLoadDensity));
            body.Children.Add(CreateInputRow("Hệ số nhu cầu/đồng thời (%)", "80", out _elecDiversity));
            body.Children.Add(CreateInputRow("Hệ số công suất cosφ", "0.90", out _elecPowerFactor));
            body.Children.Add(CreateInputRow("Điện áp dây/pha (V)", "380", out _elecVoltage));
            body.Children.Add(CreateInputRow("Dự phòng công suất (%)", "20", out _elecReserve));

            var phaseRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            phaseRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            phaseRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(135) });
            phaseRow.Children.Add(new TextBlock
            {
                Text = "Hệ nguồn",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 10
            });
            _elecPhase = new ComboBox { Height = 25, SelectedIndex = 0 };
            _elecPhase.Items.Add(new ComboBoxItem { Content = "3 pha", Tag = "3" });
            _elecPhase.Items.Add(new ComboBoxItem { Content = "1 pha", Tag = "1" });
            Grid.SetColumn(_elecPhase, 1);
            phaseRow.Children.Add(_elecPhase);
            body.Children.Add(phaseRow);

            body.Children.Add(CreateAreaTransferButton(_elecArea));
            body.Children.Add(CreateCalculateButton("TÍNH ĐIỆN SƠ BỘ", DesignElecBrush, CalculateElectricalDesign));
            _elecResult = CreateResultBox();
            body.Children.Add(_elecResult);
            body.Children.Add(CreateEngineeringWarning(
                "CB/cáp/MBA chỉ là gợi ý concept. Phải kiểm tra phương pháp lắp đặt, nhiệt độ, sụt áp, ngắn mạch, chọn lọc bảo vệ, hệ số hiệu chỉnh và tiêu chuẩn điện áp dụng."));
            return root;
        }

        private static ScrollViewer CreateSystemPanelRoot(string title, string subtitle, Brush brush)
        {
            var body = new StackPanel { Margin = new Thickness(8) };
            body.Children.Add(new Border
            {
                Background = brush,
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 8),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = title,
                            Foreground = Brushes.White,
                            FontWeight = FontWeights.Bold,
                            FontSize = 15,
                            HorizontalAlignment = HorizontalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = subtitle,
                            Foreground = Brushes.White,
                            Opacity = 0.88,
                            FontSize = 9,
                            Margin = new Thickness(0, 3, 0, 0),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }
            });

            return new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                CanContentScroll = false,
                Content = body
            };
        }

        private static void AddSectionTitle(Panel panel, string text)
        {
            panel.Children.Add(new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(55, 55, 55)),
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 8)
            });
        }

        private static Grid CreateInputRow(string label, string defaultValue, out TextBox textBox)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(135) });
            grid.Children.Add(new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 8, 0)
            });
            textBox = new TextBox
            {
                Text = defaultValue,
                Height = 25,
                Padding = new Thickness(4, 2, 4, 2),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(textBox, 1);
            grid.Children.Add(textBox);
            return grid;
        }

        private Button CreateAreaTransferButton(TextBox target)
        {
            var button = new Button
            {
                Content = "LẤY DIỆN TÍCH ĐÃ QUÉT TỪ PCCC",
                Height = 29,
                Margin = new Thickness(0, 3, 0, 6),
                FontWeight = FontWeights.Bold,
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = new SolidColorBrush(Color.FromRgb(238, 238, 238))
            };
            button.Click += (_, __) =>
            {
                double area = _fireDesignAreas?.Sum(x => x.SignedAreaM2) ?? 0.0;
                if (area <= 0)
                {
                    MessageBox.Show(
                        "Chưa có diện tích đã quét. Vào PCCC > mục 2 quét vùng kín trước, rồi quay lại hệ này.",
                        "THIẾT KẾ MEP",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }
                target.Text = area.ToString("0.##", CultureInfo.CurrentCulture);
            };
            return button;
        }

        private static Button CreateCalculateButton(string text, Brush brush, RoutedEventHandler handler)
        {
            var button = new Button
            {
                Content = text,
                Height = 34,
                Margin = new Thickness(0, 2, 0, 8),
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = brush,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            button.Click += handler;
            return button;
        }

        private static TextBlock CreateResultBox()
        {
            return new TextBlock
            {
                Text = "Chưa tính toán.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Background = Brushes.White,
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 8)
            };
        }

        private static Border CreateEngineeringWarning(string text)
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(255, 248, 225)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(255, 193, 7)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(7),
                Margin = new Thickness(0, 0, 0, 10),
                Child = new TextBlock
                {
                    Text = "LƯU Ý KỸ THUẬT: " + text,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromRgb(92, 69, 0))
                }
            };
        }

        private void CalculateHvacDesign(object sender, RoutedEventArgs e)
        {
            if (!TryReadPositive(_hvacArea, "Diện tích", out double area) ||
                !TryReadNonNegative(_hvacPeople, "Số người", out double people) ||
                !TryReadPositive(_hvacLoadDensity, "Suất tải", out double loadDensity) ||
                !TryReadNonNegative(_hvacPeopleLoad, "Tải người", out double peopleLoad) ||
                !TryReadPositive(_hvacDiversity, "Hệ số đồng thời", out double diversityPct) ||
                !TryReadPositive(_hvacDeltaT, "Chênh nhiệt", out double deltaT) ||
                !TryReadNonNegative(_hvacFreshAir, "Gió tươi", out double freshLpsPerson) ||
                !TryReadPositive(_hvacVelocity, "Vận tốc ống gió", out double velocity))
                return;

            double baseKw = area * loadDensity / 1000.0;
            double peopleKw = people * peopleLoad / 1000.0;
            double totalKw = (baseKw + peopleKw) * diversityPct / 100.0;
            double tr = totalKw / 3.517;
            double supplyM3h = totalKw <= 0 ? 0 : totalKw * 3600.0 / (1.20 * 1.005 * deltaT);
            double freshM3h = people * freshLpsPerson * 3.6;
            double designM3h = Math.Max(supplyM3h, freshM3h);
            double ductAreaM2 = designM3h / 3600.0 / velocity;
            double ductW = Math.Sqrt(Math.Max(ductAreaM2, 0.000001) * 2.0) * 1000.0;
            double ductH = ductW / 2.0;
            int ductWRounded = RoundToStep(ductW, 50, 100);
            int ductHRounded = RoundToStep(ductH, 50, 100);

            _hvacResult.Text =
                $"TẢI LẠNH SƠ BỘ: {totalKw:0.0} kW ≈ {tr:0.0} TR\n" +
                $"- Tải nền: {baseKw:0.0} kW | Tải người: {peopleKw:0.0} kW\n" +
                $"- Lưu lượng gió cấp theo ΔT: {supplyM3h:0} m³/h\n" +
                $"- Gió tươi tối thiểu theo đầu người: {freshM3h:0} m³/h\n" +
                $"- Lưu lượng dùng sơ bộ để sizing: {designM3h:0} m³/h\n" +
                $"- Ống gió chính gợi ý @ {velocity:0.0} m/s: khoảng {ductWRounded} x {ductHRounded} mm (AR≈2:1)\n" +
                "- Bước tiếp theo: chia zone/AHU-FCU, kiểm tra ESP, diffuser/grille và cân bằng gió.";
        }

        private void CalculateCtnDesign(object sender, RoutedEventArgs e)
        {
            if (!TryReadPositive(_ctnArea, "Diện tích", out double area) ||
                !TryReadPositive(_ctnPeople, "Số người", out double people) ||
                !TryReadPositive(_ctnWaterRate, "Nhu cầu nước", out double waterRate) ||
                !TryReadPositive(_ctnPeakFactor, "Hệ số cực đại", out double peakFactor) ||
                !TryReadPositive(_ctnUseHours, "Giờ sử dụng", out double useHours) ||
                !TryReadPositive(_ctnVelocity, "Vận tốc ống cấp", out double velocity) ||
                !TryReadPositive(_ctnReserveDays, "Ngày dự trữ", out double reserveDays) ||
                !TryReadNonNegative(_ctnRoofArea, "Diện tích mái", out double roofArea) ||
                !TryReadNonNegative(_ctnRainfall, "Cường độ mưa", out double rainfall))
                return;

            double dailyM3 = people * waterRate / 1000.0;
            double peakM3h = dailyM3 / Math.Max(useHours, 1.0) * peakFactor;
            double peakLs = peakM3h / 3.6;
            double pipeDiameterMm = Math.Sqrt(4.0 * (peakLs / 1000.0) / (Math.PI * velocity)) * 1000.0;
            int supplyDn = NextStandardDn(pipeDiameterMm);
            double tankM3 = dailyM3 * reserveDays;
            double sewageM3 = dailyM3 * 0.90;
            double rainLs = roofArea * rainfall / 3600.0;
            double rainDiameterMm = rainLs > 0
                ? Math.Sqrt(4.0 * (rainLs / 1000.0) / (Math.PI * 1.5)) * 1000.0
                : 0.0;
            int rainDn = rainLs > 0 ? NextStandardDn(Math.Max(50, rainDiameterMm)) : 0;

            _ctnResult.Text =
                $"NHU CẦU NƯỚC: {dailyM3:0.00} m³/ngày\n" +
                $"- Lưu lượng giờ cực đại: {peakM3h:0.00} m³/h ≈ {peakLs:0.00} L/s\n" +
                $"- Ống cấp chính sơ bộ @ {velocity:0.0} m/s: đường kính tính {pipeDiameterMm:0} mm → gợi ý DN{supplyDn}\n" +
                $"- Dung tích bể hữu ích sơ bộ: {tankM3:0.0} m³ ({reserveDays:0.##} ngày)\n" +
                $"- Nước thải sinh hoạt ước tính: {sewageM3:0.00} m³/ngày\n" +
                $"- Nước mưa mái: {rainLs:0.00} L/s" + (rainDn > 0 ? $" → ống gom tương đương tối thiểu khoảng DN{rainDn}" : string.Empty) + "\n" +
                $"- Diện tích tham chiếu: {area:0} m². Cần tách zone/tầng và kiểm tra thiết bị vệ sinh thực tế.";
        }

        private void CalculateElectricalDesign(object sender, RoutedEventArgs e)
        {
            if (!TryReadPositive(_elecArea, "Diện tích", out double area) ||
                !TryReadPositive(_elecLoadDensity, "Mật độ phụ tải", out double density) ||
                !TryReadPositive(_elecDiversity, "Hệ số nhu cầu", out double diversityPct) ||
                !TryReadPositive(_elecPowerFactor, "cosφ", out double pf) ||
                !TryReadPositive(_elecVoltage, "Điện áp", out double voltage) ||
                !TryReadNonNegative(_elecReserve, "Dự phòng", out double reservePct))
                return;

            if (pf > 1.0)
            {
                MessageBox.Show("cosφ phải ≤ 1.0.", "THIẾT KẾ ĐIỆN", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool threePhase = ((_elecPhase.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "3") == "3";
            double installedKw = area * density / 1000.0;
            double demandKw = installedKw * diversityPct / 100.0;
            double designKw = demandKw * (1.0 + reservePct / 100.0);
            double kva = designKw / Math.Max(pf, 0.01);
            double currentA = threePhase
                ? designKw * 1000.0 / (Math.Sqrt(3.0) * voltage * pf)
                : designKw * 1000.0 / (voltage * pf);
            int breakerA = NextBreaker(currentA * 1.10);
            string cable = SuggestCopperCable(currentA);
            int transformer = NextTransformerKva(kva * 1.10);

            _elecResult.Text =
                $"PHỤ TẢI LẮP ĐẶT SƠ BỘ: {installedKw:0.0} kW\n" +
                $"- Phụ tải nhu cầu: {demandKw:0.0} kW\n" +
                $"- Công suất thiết kế có dự phòng: {designKw:0.0} kW ≈ {kva:0.0} kVA\n" +
                $"- Dòng tính toán ({(threePhase ? "3 pha" : "1 pha")}): {currentA:0.0} A\n" +
                $"- CB tổng concept: khoảng {breakerA} A\n" +
                $"- Cáp đồng concept: {cable}\n" +
                $"- MBA concept gần nhất: {transformer} kVA\n" +
                "- Bắt buộc kiểm tra sụt áp, ngắn mạch, chọn lọc, phương pháp lắp đặt và hệ số hiệu chỉnh trước khi chốt.";
        }

        private static bool TryReadPositive(TextBox box, string name, out double value)
        {
            if (!TryReadNumber(box, out value) || value <= 0)
            {
                MessageBox.Show(name + " phải là số > 0.", "THIẾT KẾ MEP", MessageBoxButton.OK, MessageBoxImage.Warning);
                box?.Focus();
                return false;
            }
            return true;
        }

        private static bool TryReadNonNegative(TextBox box, string name, out double value)
        {
            if (!TryReadNumber(box, out value) || value < 0)
            {
                MessageBox.Show(name + " phải là số ≥ 0.", "THIẾT KẾ MEP", MessageBoxButton.OK, MessageBoxImage.Warning);
                box?.Focus();
                return false;
            }
            return true;
        }

        private static bool TryReadNumber(TextBox box, out double value)
        {
            value = 0;
            string raw = (box?.Text ?? string.Empty).Trim();
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
                return true;
            raw = raw.Replace(',', '.');
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static int RoundToStep(double value, int step, int min)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return min;
            int rounded = (int)Math.Ceiling(value / step) * step;
            return Math.Max(min, rounded);
        }

        private static int NextStandardDn(double requiredMm)
        {
            int[] dns = { 15, 20, 25, 32, 40, 50, 65, 80, 100, 125, 150, 200, 250, 300, 350, 400 };
            foreach (int dn in dns)
                if (dn >= requiredMm) return dn;
            return 400;
        }

        private static int NextBreaker(double requiredA)
        {
            int[] amps = { 6, 10, 16, 20, 25, 32, 40, 50, 63, 80, 100, 125, 160, 200, 250, 315, 400, 500, 630, 800, 1000, 1250, 1600 };
            foreach (int a in amps)
                if (a >= requiredA) return a;
            return 1600;
        }

        private static int NextTransformerKva(double requiredKva)
        {
            int[] kva = { 30, 50, 75, 100, 160, 250, 320, 400, 500, 630, 750, 1000, 1250, 1600, 2000, 2500 };
            foreach (int v in kva)
                if (v >= requiredKva) return v;
            return 2500;
        }

        private static string SuggestCopperCable(double currentA)
        {
            if (currentA <= 20) return "Cu 2.5 mm²";
            if (currentA <= 28) return "Cu 4 mm²";
            if (currentA <= 36) return "Cu 6 mm²";
            if (currentA <= 50) return "Cu 10 mm²";
            if (currentA <= 68) return "Cu 16 mm²";
            if (currentA <= 89) return "Cu 25 mm²";
            if (currentA <= 110) return "Cu 35 mm²";
            if (currentA <= 134) return "Cu 50 mm²";
            if (currentA <= 171) return "Cu 70 mm²";
            if (currentA <= 207) return "Cu 95 mm²";
            if (currentA <= 239) return "Cu 120 mm²";
            if (currentA <= 275) return "Cu 150 mm²";
            if (currentA <= 314) return "Cu 185 mm²";
            if (currentA <= 360) return "Cu 240 mm²";
            if (currentA <= 520) return "2 x Cu 185 mm² song song/pha (concept)";
            if (currentA <= 650) return "2 x Cu 240 mm² song song/pha (concept)";
            return "Cần thiết kế nhiều tuyến song song hoặc busduct; kiểm tra ampacity thực tế.";
        }
    }
}
