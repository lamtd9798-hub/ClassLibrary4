// FIRE-DESIGN-V2-20260829: hồ sơ, tiêu chuẩn, thủy lực, bố trí và báo cáo.
#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace ClassLibrary4
{
    /// <summary>
    /// Luồng thiết kế PCCC V2.
    ///
    /// Các kết quả "gợi ý" và "sơ bộ" dùng để sàng lọc/đấu thầu. Thông số
    /// thủy lực phải được kỹ sư đối chiếu đúng công năng, nhóm nguy hiểm,
    /// hồ sơ chủ đầu tư và bản tiêu chuẩn có bản quyền đang áp dụng cho dự án.
    /// </summary>
    public partial class BOCTACHUI
    {
        private readonly ObservableCollection<FireStandardRow>
            _fireStandardRows = new ObservableCollection<FireStandardRow>();

        private readonly ObservableCollection<FireSystemRecommendationRow>
            _fireSystemRows =
                new ObservableCollection<FireSystemRecommendationRow>();

        private readonly ObservableCollection<FireCalculationResultRow>
            _fireCalculationRows =
                new ObservableCollection<FireCalculationResultRow>();

        private readonly HashSet<string> _fireOwnerDetectedSystemCodes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private bool _fireWorkflowInitialized;
        private bool _fireApplyingPreset;
        private string _fireOwnerDocumentPath = string.Empty;
        private FireCalculationSnapshot _fireLastCalculation;

        private static readonly Dictionary<string, string[]>
            FireOwnerSystemKeywords =
                new Dictionary<string, string[]>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    {
                        "SPRINKLER",
                        new[]
                        {
                            "SPRINKLER", "DAU PHUN", "PHUN NUOC TU DONG",
                            "CHUA CHAY TU DONG BANG NUOC"
                        }
                    },
                    {
                        "INDOOR_HYDRANT",
                        new[]
                        {
                            "HONG NUOC VACH TUONG", "CHUA CHAY VACH TUONG",
                            "HOP VOI CHUA CHAY", "CUON VOI CHUA CHAY"
                        }
                    },
                    {
                        "OUTDOOR_HYDRANT",
                        new[]
                        {
                            "TRU NUOC CHUA CHAY", "CHUA CHAY NGOAI NHA",
                            "FIRE HYDRANT"
                        }
                    },
                    {
                        "FIRE_ALARM",
                        new[]
                        {
                            "BAO CHAY", "DAU BAO KHOI", "DAU BAO NHIET",
                            "FIRE ALARM"
                        }
                    },
                    {
                        "EXTINGUISHER",
                        new[] { "BINH CHUA CHAY", "FIRE EXTINGUISHER" }
                    },
                    {
                        "SMOKE_CONTROL",
                        new[]
                        {
                            "HUT KHOI", "TANG AP", "PRESSURIZATION",
                            "SMOKE EXHAUST"
                        }
                    },
                    {
                        "EMERGENCY",
                        new[]
                        {
                            "DEN SU CO", "DEN THOAT NAN", "DEN EXIT",
                            "EMERGENCY LIGHT", "EXIT LIGHT"
                        }
                    },
                    {
                        "PUMP_TANK",
                        new[]
                        {
                            "BOM CHUA CHAY", "BE NUOC CHUA CHAY",
                            "FIRE PUMP", "FIRE WATER TANK"
                        }
                    },
                    {
                        "FOAM",
                        new[] { "CHUA CHAY BANG BOT", "FOAM", "FOAM SYSTEM" }
                    },
                    {
                        "CLEAN_AGENT",
                        new[]
                        {
                            "FM200", "NOVEC", "INERGEN", "KHÍ CHUA CHAY",
                            "KHI CHUA CHAY", "CLEAN AGENT"
                        }
                    }
                };

        private void FireDesignWorkflowPanel_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (_fireWorkflowInitialized)
                return;

            _fireWorkflowInitialized = true;

            DgFireStandards.ItemsSource = _fireStandardRows;
            DgFireSystems.ItemsSource = _fireSystemRows;
            DgFireCalculationResults.ItemsSource = _fireCalculationRows;

            InitializeFireStandardRows();
            InitializeFireSystemRows();
            EnsureFireCalculationDefaults();
            EnsureFireHydraulicDefaults();
            SyncFireCalculationAreaFromDrawing(force: false);
            UpdateFireWorkflowSummaries();
        }

        private void InitializeFireStandardRows()
        {
            if (_fireStandardRows.Count > 0)
                return;

            _fireStandardRows.Add(
                new FireStandardRow
                {
                    IsSelected = true,
                    Code = "QCVN 06:2022/BXD + SĐ1:2023",
                    Scope = "An toàn cháy cho nhà và công trình",
                    Status = "Hiệu lực · kiểm tra 29/08/2026",
                    SourceUrl =
                        "https://congbao.chinhphu.vn/van-ban/thong-tu-so-09-2023-tt-bxd-40285.htm"
                });

            _fireStandardRows.Add(
                new FireStandardRow
                {
                    IsSelected = true,
                    Code = "TCVN 3890:2023",
                    Scope = "Trang bị, bố trí phương tiện và hệ thống PCCC",
                    Status = "Còn hiệu lực",
                    SourceUrl =
                        "https://tieuchuan.vsqi.gov.vn/tieuchuan/view?sohieu=TCVN+3890%3A2023"
                });

            _fireStandardRows.Add(
                new FireStandardRow
                {
                    IsSelected = true,
                    Code = "TCVN 7336:2021",
                    Scope = "Hệ thống chữa cháy tự động bằng nước, bọt",
                    Status = "Còn hiệu lực",
                    SourceUrl =
                        "https://tieuchuan.vsqi.gov.vn/tieuchuan/view?sohieu=TCVN+7336%3A2021"
                });

            _fireStandardRows.Add(
                new FireStandardRow
                {
                    IsSelected = true,
                    Code = "TCVN 7568-14:2025",
                    Scope = "Thiết kế, lắp đặt hệ thống báo cháy",
                    Status = "Còn hiệu lực · thay TCVN 5738:2021",
                    SourceUrl =
                        "https://tieuchuan.vsqi.gov.vn/tieuchuan/view?sohieu=TCVN+7568-14%3A2025"
                });

            _fireStandardRows.Add(
                new FireStandardRow
                {
                    IsSelected = true,
                    Code = "TCVN 2622:1995",
                    Scope = "Phòng cháy, chống cháy cho nhà và công trình",
                    Status = "Còn hiệu lực",
                    SourceUrl =
                        "https://tieuchuan.vsqi.gov.vn/tieuchuan/view?sohieu=TCVN+2622%3A1995"
                });

            _fireStandardRows.Add(
                new FireStandardRow
                {
                    IsSelected = false,
                    Code = "TCVN 4513:1988",
                    Scope = "Cấp nước bên trong – tiêu chuẩn thiết kế",
                    Status = "Còn hiệu lực",
                    SourceUrl =
                        "https://tieuchuan.vsqi.gov.vn/tieuchuan/view?sohieu=TCVN+4513%3A1988"
                });

            _fireStandardRows.Add(
                new FireStandardRow
                {
                    IsSelected = false,
                    Code = "TCVN 6379:2024",
                    Scope = "Trụ nước chữa cháy – yêu cầu kỹ thuật",
                    Status = "Còn hiệu lực · thay bản 1998",
                    SourceUrl =
                        "https://tieuchuan.vsqi.gov.vn/tieuchuan/view?sohieu=TCVN+6379%3A2024"
                });
        }

        private void InitializeFireSystemRows()
        {
            if (_fireSystemRows.Count > 0)
                return;

            AddFireSystem("EXTINGUISHER", "Bình chữa cháy", true);
            AddFireSystem("FIRE_ALARM", "Báo cháy tự động", false);
            AddFireSystem("SPRINKLER", "Sprinkler tự động", false);
            AddFireSystem("INDOOR_HYDRANT", "Họng nước trong nhà", false);
            AddFireSystem("OUTDOOR_HYDRANT", "Trụ/họng nước ngoài nhà", false);
            AddFireSystem("SMOKE_CONTROL", "Hút khói / tăng áp", false);
            AddFireSystem("EMERGENCY", "Đèn sự cố / chỉ dẫn thoát nạn", true);
            AddFireSystem("PUMP_TANK", "Bơm và bể nước chữa cháy", false);
            AddFireSystem("FOAM", "Chữa cháy bằng bọt", false);
            AddFireSystem("CLEAN_AGENT", "Khí sạch FM200/Novec/Inergen", false);
        }

        private void AddFireSystem(
            string code,
            string displayName,
            bool selected)
        {
            _fireSystemRows.Add(
                new FireSystemRecommendationRow
                {
                    IsSelected = selected,
                    Code = code,
                    SystemName = displayName,
                    Basis = selected
                        ? "Mặc định sàng lọc – cần đối chiếu tiêu chuẩn"
                        : "Chưa chọn"
                });
        }

        private void EnsureFireCalculationDefaults()
        {
            SetFireTextIfEmpty(TxtFireDensity, "5");
            SetFireTextIfEmpty(TxtFireDesignArea, "180");
            SetFireTextIfEmpty(TxtFireSprinklerCoverage, "12");
            SetFireTextIfEmpty(TxtFireKFactor, "80");
            SetFireTextIfEmpty(TxtFireIndoorFlow, "2.5");
            SetFireTextIfEmpty(TxtFireIndoorJetCount, "2");
            SetFireTextIfEmpty(TxtFireOutdoorFlow, "10");
            SetFireTextIfEmpty(TxtFireDuration, "60");
            SetFireTextIfEmpty(TxtFireStaticHead, "0");
            SetFireTextIfEmpty(TxtFireResidualPressure, "2.5");
            SetFireTextIfEmpty(TxtFirePipeLoss, "0");
            SetFireTextIfEmpty(TxtFireSafetyMargin, "10");
            SetFireTextIfEmpty(TxtFireDetectorCoverage, "60");
            SetFireTextIfEmpty(TxtFireExtinguisherCoverage, "100");
            SetFireTextIfEmpty(TxtFireCabinetsPerFloor, "2");
            SetFireTextIfEmpty(TxtFireCallPointsPerFloor, "2");

            if (TxtFirePresetNotice != null)
            {
                TxtFirePresetNotice.Text =
                    "Các số đang hiển thị là mẫu kiểm thử phần mềm. " +
                    "Phải thay bằng thông số tra từ hồ sơ/tiêu chuẩn của dự án.";
            }
        }

        private static void SetFireTextIfEmpty(TextBox textBox, string value)
        {
            if (textBox != null && string.IsNullOrWhiteSpace(textBox.Text))
                textBox.Text = value;
        }

        private void CmbFireHazardPreset_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (_fireApplyingPreset || CmbFireHazardPreset == null)
                return;

            string tag = GetFireComboTag(CmbFireHazardPreset);

            _fireApplyingPreset = true;

            try
            {
                switch (tag)
                {
                    case "TEST_LIGHT":
                        ApplyFireCalculationPreset(2.5, 120, 12, 80, 60);
                        break;

                    case "TEST_ORDINARY":
                        ApplyFireCalculationPreset(5, 180, 12, 80, 60);
                        break;

                    case "TEST_HIGH":
                        ApplyFireCalculationPreset(7.5, 260, 9, 115, 90);
                        break;
                }
            }
            finally
            {
                _fireApplyingPreset = false;
            }

            if (TxtFirePresetNotice != null)
            {
                TxtFirePresetNotice.Text =
                    tag == "MANUAL"
                        ? "Chế độ nhập tay: điền đúng thông số đã tra cho dự án."
                        : "Đã nạp mẫu kiểm thử, không phải bảng tra chính thức. " +
                          "Hãy sửa lại trước khi dùng cho hồ sơ.";
            }
        }

        private void ApplyFireCalculationPreset(
            double density,
            double designArea,
            double sprinklerCoverage,
            double kFactor,
            double durationMinutes)
        {
            TxtFireDensity.Text = FireInvariant(density);
            TxtFireDesignArea.Text = FireInvariant(designArea);
            TxtFireSprinklerCoverage.Text = FireInvariant(sprinklerCoverage);
            TxtFireKFactor.Text = FireInvariant(kFactor);
            TxtFireDuration.Text = FireInvariant(durationMinutes);
        }

        private void BtnFireImportOwnerDocument_Click(
            object sender,
            RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Chọn yêu cầu/chỉ dẫn của chủ đầu tư",
                Filter =
                    "Tài liệu đọc được (*.txt;*.csv;*.md;*.docx)|*.txt;*.csv;*.md;*.docx|" +
                    "Text (*.txt;*.csv;*.md)|*.txt;*.csv;*.md|" +
                    "Word (*.docx)|*.docx|Tất cả file (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                string text = ReadFireOwnerDocument(dialog.FileName);

                if (string.IsNullOrWhiteSpace(text))
                    throw new InvalidDataException("Tài liệu không có nội dung chữ.");

                if (text.Length > 100000)
                    text = text.Substring(0, 100000);

                _fireOwnerDocumentPath = dialog.FileName;
                TxtFireOwnerRequirements.Text = text;

                AnalyzeFireOwnerRequirements();

                SetFireDesignStatus(
                    "Đã đọc hồ sơ chủ đầu tư: " +
                    System.IO.Path.GetFileName(dialog.FileName) + ".",
                    isError: false,
                    isSuccess: true);
            }
            catch (Exception ex)
            {
                SetFireDesignStatus(
                    "Không đọc được tài liệu: " + ex.Message +
                    " File PDF/DOC cũ cần chuyển sang DOCX/TXT hoặc dán nội dung vào ô.",
                    isError: true);
            }
        }

        private void BtnFireAnalyzeOwnerRequirements_Click(
            object sender,
            RoutedEventArgs e)
        {
            AnalyzeFireOwnerRequirements();

            SetFireDesignStatus(
                "Đã phân tích yêu cầu chủ đầu tư và cập nhật các hệ thống nhận được.",
                isError: false,
                isSuccess: _fireOwnerDetectedSystemCodes.Count > 0);
        }

        private void AnalyzeFireOwnerRequirements()
        {
            string source = TxtFireOwnerRequirements?.Text ?? string.Empty;
            string normalized = NormalizeFireDrawingTextForMatch(source);

            _fireOwnerDetectedSystemCodes.Clear();

            foreach (KeyValuePair<string, string[]> mapping in
                     FireOwnerSystemKeywords)
            {
                if (mapping.Value.Any(
                        keyword => ContainsFireKeyword(normalized, keyword)))
                {
                    _fireOwnerDetectedSystemCodes.Add(mapping.Key);
                }
            }

            foreach (FireSystemRecommendationRow system in _fireSystemRows)
            {
                if (!_fireOwnerDetectedSystemCodes.Contains(system.Code))
                    continue;

                system.IsSelected = true;
                system.Basis = "Có từ khóa trong yêu cầu chủ đầu tư";
            }

            UpdateFireStandardsForSelectedSystems();
            DgFireSystems?.Items.Refresh();
            DgFireStandards?.Items.Refresh();

            List<string> measurements =
                Regex.Matches(
                        source,
                        @"(?<![\w])\d+(?:[\.,]\d+)?\s*(?:m³/h|m3/h|m³|m3|l/s|l/min|bar|mpa|m²|m2|min|phút)",
                        RegexOptions.IgnoreCase)
                    .Cast<Match>()
                    .Select(x => x.Value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(10)
                    .ToList();

            List<string> standards =
                Regex.Matches(
                        source,
                        @"\b(?:QCVN|TCVN|NFPA)\s*[0-9]+(?:[-:/.][0-9A-Z]+)*\b",
                        RegexOptions.IgnoreCase)
                    .Cast<Match>()
                    .Select(x => x.Value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(10)
                    .ToList();

            string systemsText =
                _fireOwnerDetectedSystemCodes.Count == 0
                    ? "không nhận được hệ thống rõ ràng"
                    : string.Join(
                        ", ",
                        _fireOwnerDetectedSystemCodes
                            .Select(GetFireSystemDisplayName));

            string summary =
                "Đã phân tích " + source.Length.ToString("N0") +
                " ký tự; hệ thống: " + systemsText + ".";

            if (measurements.Count > 0)
                summary += " Số liệu thấy: " + string.Join(", ", measurements) + ".";

            if (standards.Count > 0)
                summary += " Viện dẫn: " + string.Join(", ", standards) + ".";

            if (TxtFireOwnerAnalysis != null)
                TxtFireOwnerAnalysis.Text = summary;
        }

        private static string ReadFireOwnerDocument(string filePath)
        {
            string extension =
                System.IO.Path.GetExtension(filePath)
                    .ToLowerInvariant();

            if (extension == ".docx")
                return ReadFireOwnerDocx(filePath);

            if (extension == ".txt" ||
                extension == ".csv" ||
                extension == ".md")
            {
                using (var reader =
                       new StreamReader(
                           filePath,
                           Encoding.UTF8,
                           detectEncodingFromByteOrderMarks: true))
                {
                    return reader.ReadToEnd();
                }
            }

            throw new NotSupportedException(
                "V1 chỉ đọc trực tiếp TXT, CSV, MD và DOCX.");
        }

        private static string ReadFireOwnerDocx(string filePath)
        {
            using (FileStream stream = File.OpenRead(filePath))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                ZipArchiveEntry entry =
                    archive.GetEntry("word/document.xml");

                if (entry == null)
                    throw new InvalidDataException("DOCX thiếu word/document.xml.");

                using (Stream documentStream = entry.Open())
                {
                    XDocument document = XDocument.Load(documentStream);
                    XNamespace word =
                        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

                    var lines =
                        document
                            .Descendants(word + "p")
                            .Select(
                                paragraph => string.Concat(
                                    paragraph
                                        .Descendants(word + "t")
                                        .Select(x => x.Value)))
                            .Where(x => !string.IsNullOrWhiteSpace(x));

                    return string.Join(Environment.NewLine, lines);
                }
            }
        }

        private void BtnFireOpenStandardSource_Click(
            object sender,
            RoutedEventArgs e)
        {
            FireStandardRow row =
                DgFireStandards?.SelectedItem as FireStandardRow;

            if (row == null || string.IsNullOrWhiteSpace(row.SourceUrl))
            {
                SetFireDesignStatus(
                    "Hãy chọn một dòng tiêu chuẩn để mở nguồn chính thức.",
                    isError: false);
                return;
            }

            try
            {
                Process.Start(
                    new ProcessStartInfo(row.SourceUrl)
                    {
                        UseShellExecute = true
                    });
            }
            catch (Exception ex)
            {
                SetFireDesignStatus(
                    "Không mở được liên kết tiêu chuẩn: " + ex.Message,
                    isError: true);
            }
        }

        private void BtnFireSuggestSystems_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!_fireWorkflowInitialized)
                FireDesignWorkflowPanel_Loaded(this, new RoutedEventArgs());

            double totalArea = ResolveFireCalculationTotalArea();
            int floors = ResolveFireFloorCount();
            double height = ResolveFireBuildingHeight();
            string use = GetFireComboTag(CmbFireProjectUse);

            bool significantUse =
                !string.IsNullOrWhiteSpace(use) && use != "OTHER";

            SetFireSystemSuggestion(
                "EXTINGUISHER",
                true,
                "Phương tiện ban đầu – đối chiếu TCVN 3890:2023");

            SetFireSystemSuggestion(
                "EMERGENCY",
                significantUse || floors >= 2 || totalArea >= 300,
                "Sàng lọc theo công năng, số tầng và quy mô");

            SetFireSystemSuggestion(
                "FIRE_ALARM",
                significantUse || floors >= 2 || totalArea >= 300,
                "Sàng lọc báo cháy; đối chiếu TCVN 3890 và TCVN 7568-14");

            bool sprinklerSuggested =
                totalArea >= 1000 ||
                height >= 25 ||
                IsFireUse(
                    use,
                    "FACTORY", "WAREHOUSE", "COMMERCIAL", "HOTEL",
                    "HOSPITAL", "MIXED");

            SetFireSystemSuggestion(
                "SPRINKLER",
                sprinklerSuggested,
                "Sàng lọc theo công năng/quy mô; tra bảng TCVN 3890 và 7336");

            bool indoorSuggested =
                totalArea >= 500 ||
                floors >= 2 ||
                height >= 10 ||
                IsFireUse(
                    use,
                    "FACTORY", "WAREHOUSE", "APARTMENT", "COMMERCIAL",
                    "HOTEL", "HOSPITAL", "MIXED");

            SetFireSystemSuggestion(
                "INDOOR_HYDRANT",
                indoorSuggested,
                "Sàng lọc theo công năng, chiều cao và diện tích");

            bool outdoorSuggested =
                totalArea >= 1000 ||
                IsFireUse(use, "FACTORY", "WAREHOUSE");

            SetFireSystemSuggestion(
                "OUTDOOR_HYDRANT",
                outdoorSuggested,
                "Sàng lọc nhu cầu cấp nước ngoài nhà");

            bool hasBasement =
                _fireDetectedFloors.Any(
                    x => NormalizeFireDrawingTextForMatch(x).Contains("HAM"));

            SetFireSystemSuggestion(
                "SMOKE_CONTROL",
                height >= 25 || hasBasement ||
                (floors >= 3 &&
                 IsFireUse(
                     use,
                     "APARTMENT", "COMMERCIAL", "HOTEL", "HOSPITAL",
                     "MIXED")),
                "Sàng lọc hút khói/tăng áp; cần kiểm tra kiến trúc và QCVN 06");

            bool waterSystem =
                IsFireSystemSelected("SPRINKLER") ||
                IsFireSystemSelected("INDOOR_HYDRANT") ||
                IsFireSystemSelected("OUTDOOR_HYDRANT");

            SetFireSystemSuggestion(
                "PUMP_TANK",
                waterSystem,
                "Phục vụ các hệ chữa cháy bằng nước đang chọn");

            foreach (string ownerSystem in _fireOwnerDetectedSystemCodes)
            {
                SetFireSystemSuggestion(
                    ownerSystem,
                    true,
                    "Có từ khóa trong yêu cầu chủ đầu tư");
            }

            UpdateFireStandardsForSelectedSystems();
            DgFireSystems.Items.Refresh();
            DgFireStandards.Items.Refresh();
            UpdateFireWorkflowSummaries();

            SetFireDesignStatus(
                "Đã tạo danh sách hệ thống cần xem xét. Đây là bước sàng lọc, " +
                "không phải kết luận thẩm duyệt; hãy tích/bỏ và kiểm tra căn cứ.",
                isError: false,
                isSuccess: true);
        }

        private void SetFireSystemSuggestion(
            string code,
            bool suggested,
            string basis)
        {
            FireSystemRecommendationRow row =
                _fireSystemRows.FirstOrDefault(
                    x => string.Equals(
                        x.Code,
                        code,
                        StringComparison.OrdinalIgnoreCase));

            if (row == null)
                return;

            bool forcedByOwner =
                _fireOwnerDetectedSystemCodes.Contains(code);

            row.IsSelected = suggested || forcedByOwner;
            row.Basis =
                forcedByOwner
                    ? "Có từ khóa trong yêu cầu chủ đầu tư"
                    : row.IsSelected
                        ? basis
                        : "Chưa có căn cứ sàng lọc – vẫn cần kiểm tra tiêu chuẩn";
        }

        private static bool IsFireUse(
            string actual,
            params string[] candidates)
        {
            return candidates.Any(
                x => string.Equals(
                    actual,
                    x,
                    StringComparison.OrdinalIgnoreCase));
        }

        private void UpdateFireStandardsForSelectedSystems()
        {
            SelectFireStandardContaining("QCVN 06", true);
            SelectFireStandardContaining("TCVN 3890", true);
            SelectFireStandardContaining("TCVN 2622", true);

            if (IsFireSystemSelected("SPRINKLER") ||
                IsFireSystemSelected("PUMP_TANK") ||
                IsFireSystemSelected("FOAM"))
            {
                SelectFireStandardContaining("TCVN 7336", true);
            }

            if (IsFireSystemSelected("FIRE_ALARM"))
                SelectFireStandardContaining("TCVN 7568-14", true);

            if (IsFireSystemSelected("INDOOR_HYDRANT"))
                SelectFireStandardContaining("TCVN 4513", true);

            if (IsFireSystemSelected("OUTDOOR_HYDRANT"))
                SelectFireStandardContaining("TCVN 6379", true);
        }

        private void SelectFireStandardContaining(
            string codePart,
            bool selected)
        {
            FireStandardRow row =
                _fireStandardRows.FirstOrDefault(
                    x => (x.Code ?? string.Empty).IndexOf(
                        codePart,
                        StringComparison.OrdinalIgnoreCase) >= 0);

            if (row != null)
                row.IsSelected = selected;
        }

        private bool IsFireSystemSelected(string code)
        {
            CommitFireDataGridEdits();

            return _fireSystemRows.Any(
                x => x.IsSelected &&
                     string.Equals(
                         x.Code,
                         code,
                         StringComparison.OrdinalIgnoreCase));
        }

        private void CommitFireDataGridEdits()
        {
            DgFireSystems?.CommitEdit(
                DataGridEditingUnit.Cell,
                exitEditingMode: true);
            DgFireSystems?.CommitEdit(
                DataGridEditingUnit.Row,
                exitEditingMode: true);
            DgFireStandards?.CommitEdit(
                DataGridEditingUnit.Cell,
                exitEditingMode: true);
            DgFireStandards?.CommitEdit(
                DataGridEditingUnit.Row,
                exitEditingMode: true);
        }

        private void BtnFireCalculateAll_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!_fireWorkflowInitialized)
                FireDesignWorkflowPanel_Loaded(this, new RoutedEventArgs());

            CommitFireDataGridEdits();
            SyncFireCalculationAreaFromDrawing(force: false);

            List<string> errors = new List<string>();

            double totalArea = ResolveFireCalculationTotalArea();
            int floors = ResolveFireFloorCount();
            double buildingHeight = ResolveFireBuildingHeight();

            if (totalArea <= 0)
                errors.Add("diện tích tính toán");

            if (floors <= 0)
                errors.Add("số tầng");

            bool sprinkler = IsFireSystemSelected("SPRINKLER");
            bool indoorHydrant = IsFireSystemSelected("INDOOR_HYDRANT");
            bool outdoorHydrant = IsFireSystemSelected("OUTDOOR_HYDRANT");
            bool fireAlarm = IsFireSystemSelected("FIRE_ALARM");
            bool extinguisher = IsFireSystemSelected("EXTINGUISHER");

            double density = 0;
            double designArea = 0;
            double sprinklerCoverage = 0;
            double kFactor = 0;

            if (sprinkler)
            {
                RequirePositiveFireNumber(
                    TxtFireDensity,
                    "mật độ phun",
                    errors,
                    out density);
                RequirePositiveFireNumber(
                    TxtFireDesignArea,
                    "diện tích tính sprinkler",
                    errors,
                    out designArea);
                RequirePositiveFireNumber(
                    TxtFireSprinklerCoverage,
                    "diện tích bảo vệ/đầu phun",
                    errors,
                    out sprinklerCoverage);
                RequirePositiveFireNumber(
                    TxtFireKFactor,
                    "K-factor đầu phun",
                    errors,
                    out kFactor);
            }

            double indoorFlow = 0;
            int indoorJets = 0;

            if (indoorHydrant)
            {
                RequirePositiveFireNumber(
                    TxtFireIndoorFlow,
                    "lưu lượng một lăng trong nhà",
                    errors,
                    out indoorFlow);
                RequirePositiveFireInteger(
                    TxtFireIndoorJetCount,
                    "số lăng đồng thời",
                    errors,
                    out indoorJets);
            }

            double outdoorFlow = 0;

            if (outdoorHydrant)
            {
                RequirePositiveFireNumber(
                    TxtFireOutdoorFlow,
                    "lưu lượng chữa cháy ngoài nhà",
                    errors,
                    out outdoorFlow);
            }

            bool hasWaterSystem =
                sprinkler || indoorHydrant || outdoorHydrant;

            double duration = 0;
            double staticHead = 0;
            double residualPressure = 0;
            double pipeLoss = 0;
            double marginPercent = 0;

            if (hasWaterSystem)
            {
                RequirePositiveFireNumber(
                    TxtFireDuration,
                    "thời gian dự trữ nước",
                    errors,
                    out duration);
                RequireNonNegativeFireNumber(
                    TxtFireStaticHead,
                    "cột áp tĩnh",
                    errors,
                    out staticHead);
                RequirePositiveFireNumber(
                    TxtFireResidualPressure,
                    "áp lực yêu cầu tại điểm bất lợi",
                    errors,
                    out residualPressure);
                RequireNonNegativeFireNumber(
                    TxtFirePipeLoss,
                    "tổn thất đường ống/phụ kiện",
                    errors,
                    out pipeLoss);
                RequireNonNegativeFireNumber(
                    TxtFireSafetyMargin,
                    "dự phòng thiết kế",
                    errors,
                    out marginPercent);
            }

            double detectorCoverage = 0;
            if (fireAlarm)
            {
                RequirePositiveFireNumber(
                    TxtFireDetectorCoverage,
                    "diện tích sơ bộ/đầu báo",
                    errors,
                    out detectorCoverage);
            }

            double extinguisherCoverage = 0;
            if (extinguisher)
            {
                RequirePositiveFireNumber(
                    TxtFireExtinguisherCoverage,
                    "diện tích sơ bộ/bình chữa cháy",
                    errors,
                    out extinguisherCoverage);
            }

            double cabinetsPerFloor = 0;
            if (indoorHydrant)
            {
                RequirePositiveFireNumber(
                    TxtFireCabinetsPerFloor,
                    "tủ/họng nước mỗi tầng",
                    errors,
                    out cabinetsPerFloor);
            }

            double callPointsPerFloor = 0;
            if (fireAlarm)
            {
                RequirePositiveFireNumber(
                    TxtFireCallPointsPerFloor,
                    "nút nhấn báo cháy mỗi tầng",
                    errors,
                    out callPointsPerFloor);
            }

            FireHydraulicSettings hydraulicSettings = null;

            if (hasWaterSystem)
            {
                TryGetFireHydraulicSettings(
                    errors,
                    out hydraulicSettings);
            }

            if (errors.Count > 0)
            {
                SetFireDesignStatus(
                    "Chưa tính được. Kiểm tra: " +
                    string.Join(", ", errors.Distinct()) + ".",
                    isError: true);
                return;
            }

            _fireCalculationRows.Clear();

            AddFireResult(
                "Công trình",
                "Tổng diện tích dùng tính",
                totalArea,
                "m²",
                _fireDesignAreas.Count > 0
                    ? "Từ các vùng kín đã quét"
                    : "Nhập tay");
            AddFireResult("Công trình", "Số tầng", floors, "tầng", "");
            AddFireResult(
                "Công trình",
                "Chiều cao nhà",
                buildingHeight,
                "m",
                buildingHeight > 0 ? "Dữ liệu đầu vào" : "Chưa nhập");

            double sprinklerDemandLps = 0;
            int designHeadCount = 0;
            int totalHeadCount = 0;
            int estimatedHeadCount = 0;
            double flowPerHeadLpm = 0;
            double headPressureBar = 0;

            if (sprinkler)
            {
                designHeadCount =
                    (int)Math.Ceiling(designArea / sprinklerCoverage);
                estimatedHeadCount =
                    (int)Math.Ceiling(totalArea / sprinklerCoverage);
                totalHeadCount =
                    _fireActualSprinklerCount > 0
                        ? _fireActualSprinklerCount
                        : estimatedHeadCount;
                flowPerHeadLpm = density * sprinklerCoverage;
                headPressureBar =
                    Math.Pow(flowPerHeadLpm / kFactor, 2.0);
                sprinklerDemandLps = density * designArea / 60.0;

                AddFireResult(
                    "Sprinkler",
                    "Mật độ phun",
                    density,
                    "L/min·m²",
                    "Thông số người dùng nhập/đã tra");
                AddFireResult(
                    "Sprinkler",
                    "Diện tích vùng tính",
                    designArea,
                    "m²",
                    "Thông số người dùng nhập/đã tra");
                AddFireResult(
                    "Sprinkler",
                    "Số đầu trong vùng tính",
                    designHeadCount,
                    "đầu",
                    "Làm tròn lên");
                AddFireResult(
                    "Sprinkler",
                    "Lưu lượng một đầu",
                    flowPerHeadLpm,
                    "L/min",
                    "q = mật độ × diện tích/đầu");
                AddFireResult(
                    "Sprinkler",
                    "Áp lực lý thuyết tại đầu",
                    headPressureBar,
                    "bar",
                    "p = (q/K)²; vẫn phải kiểm tra áp tối thiểu");
                AddFireResult(
                    "Sprinkler",
                    "Lưu lượng vùng tính",
                    sprinklerDemandLps,
                    "L/s",
                    "Q = mật độ × diện tích / 60");
                AddFireResult(
                    "Khối lượng sơ bộ",
                    "Tổng đầu sprinkler",
                    totalHeadCount,
                    "đầu",
                    _fireActualSprinklerCount > 0
                        ? "Số block đầu phun đã bố trí/đếm trên CAD; " +
                          "ước tính diện tích là " + estimatedHeadCount + " đầu"
                        : "Ước tính theo diện tích/đầu; hãy bố trí hoặc đếm " +
                          "block trên CAD để dùng số lượng thực tế");
            }

            double indoorDemandLps =
                indoorHydrant ? indoorFlow * indoorJets : 0;
            double outdoorDemandLps =
                outdoorHydrant ? outdoorFlow : 0;

            if (indoorHydrant)
            {
                AddFireResult(
                    "Họng nước",
                    "Lưu lượng trong nhà đồng thời",
                    indoorDemandLps,
                    "L/s",
                    indoorJets + " lăng × " +
                    FireDisplay(indoorFlow) + " L/s");
            }

            if (outdoorHydrant)
            {
                AddFireResult(
                    "Họng nước",
                    "Lưu lượng ngoài nhà",
                    outdoorDemandLps,
                    "L/s",
                    "Thông số người dùng nhập/đã tra");
            }

            double combinedDemandLps =
                sprinklerDemandLps + indoorDemandLps + outdoorDemandLps;

            FireHydraulicSummary hydraulicSummary =
                hasWaterSystem
                    ? CalculateFireHydraulicSummary(
                        sprinklerDemandLps,
                        indoorDemandLps,
                        outdoorDemandLps,
                        combinedDemandLps,
                        flowPerHeadLpm,
                        hydraulicSettings)
                    : null;

            double automaticFrictionLoss =
                hydraulicSummary?.CriticalPathFrictionLossM ?? 0;

            double marginFactor = 1.0 + marginPercent / 100.0;
            double pumpFlowM3h =
                combinedDemandLps * 3.6 * marginFactor;
            double effectiveResidualPressureBar =
                Math.Max(
                    residualPressure,
                    sprinkler ? headPressureBar : 0);
            double residualHeadMeters =
                effectiveResidualPressureBar * 10.19716213;
            double pumpHeadMeters =
                (staticHead +
                 residualHeadMeters +
                 automaticFrictionLoss +
                 pipeLoss) * marginFactor;
            double waterVolumeM3 =
                combinedDemandLps * duration * 60.0 / 1000.0 *
                marginFactor;

            if (hasWaterSystem)
            {
                AppendFireHydraulicResults(hydraulicSummary);

                AddFireResult(
                    "Nguồn nước",
                    "Tổng lưu lượng đồng thời",
                    combinedDemandLps,
                    "L/s",
                    "Tổng các hệ nước đang tích chọn");
                AddFireResult(
                    "Nguồn nước",
                    "Lưu lượng chọn bơm sơ bộ",
                    pumpFlowM3h,
                    "m³/h",
                    "Đã cộng " + FireDisplay(marginPercent) + "% dự phòng");
                AddFireResult(
                    "Cột áp bơm",
                    "Cột áp tĩnh",
                    staticHead,
                    "mH₂O",
                    "Chênh cao hình học do người dùng nhập");
                AddFireResult(
                    "Cột áp bơm",
                    "Cột áp dư tại điểm bất lợi",
                    residualHeadMeters,
                    "mH₂O",
                    FireDisplay(effectiveResidualPressureBar) +
                    " bar × 10,197; lấy max(giá trị nhập, p đầu phun)");
                AddFireResult(
                    "Cột áp bơm",
                    "Tổn thất ma sát tuyến bất lợi",
                    automaticFrictionLoss,
                    "mH₂O",
                    "Hazen-Williams trên chiều dài tương đương");
                AddFireResult(
                    "Cột áp bơm",
                    "Tổn thất khác",
                    pipeLoss,
                    "mH₂O",
                    "Van đặc biệt, thiết bị, bộ ngăn dòng... nhập thêm");
                AddFireResult(
                    "Nguồn nước",
                    "Cột áp chọn bơm sơ bộ",
                    pumpHeadMeters,
                    "mH₂O",
                    "(H tĩnh + H dư + hf Hazen-Williams + H khác) " +
                    "× hệ số dự phòng");
                AddFireResult(
                    "Nguồn nước",
                    "Dung tích hữu ích bể sơ bộ",
                    waterVolumeM3,
                    "m³",
                    FireDisplay(duration) + " phút, có dự phòng");
            }

            int detectorCount = 0;
            int extinguisherCount = 0;
            int cabinetCount = 0;
            int callPointCount = 0;

            if (fireAlarm)
            {
                detectorCount =
                    (int)Math.Ceiling(totalArea / detectorCoverage);
                callPointCount =
                    (int)Math.Ceiling(floors * callPointsPerFloor);

                AddFireResult(
                    "Khối lượng sơ bộ",
                    "Đầu báo cháy",
                    detectorCount,
                    "đầu",
                    "Theo diện tích sơ bộ; phải bố trí lại theo hình học/trần");
                AddFireResult(
                    "Khối lượng sơ bộ",
                    "Nút nhấn báo cháy",
                    callPointCount,
                    "cái",
                    "Theo số lượng/tầng người dùng nhập");
            }

            if (extinguisher)
            {
                extinguisherCount =
                    (int)Math.Ceiling(totalArea / extinguisherCoverage);

                AddFireResult(
                    "Khối lượng sơ bộ",
                    "Bình chữa cháy",
                    extinguisherCount,
                    "bình",
                    "Chưa phân loại chất cháy và loại bình");
            }

            if (indoorHydrant)
            {
                cabinetCount =
                    (int)Math.Ceiling(floors * cabinetsPerFloor);

                AddFireResult(
                    "Khối lượng sơ bộ",
                    "Tủ/họng nước trong nhà",
                    cabinetCount,
                    "bộ",
                    "Phải kiểm tra bán kính phục vụ trên mặt bằng");
            }

            bool confirmed = ChkFireEngineerConfirmed?.IsChecked == true;

            _fireLastCalculation =
                new FireCalculationSnapshot
                {
                    CalculatedAt = DateTime.Now,
                    TotalAreaM2 = totalArea,
                    SprinklerDemandLps = sprinklerDemandLps,
                    CombinedDemandLps = combinedDemandLps,
                    PumpFlowM3h = pumpFlowM3h,
                    PumpHeadMeters = pumpHeadMeters,
                    WaterVolumeM3 = waterVolumeM3,
                    MainPipeDnMm =
                        hydraulicSummary?.CombinedMain?.DnMm ?? 0,
                    CriticalPathFrictionLossM = automaticFrictionLoss,
                    ActualSprinklerCount =
                        sprinkler ? totalHeadCount : 0,
                    IsEngineerConfirmed = confirmed
                };

            DgFireCalculationResults.Items.Refresh();
            UpdateFireCalculationSummary();
            UpdateFireStandardsForSelectedSystems();
            DgFireStandards.Items.Refresh();

            SetFireDesignStatus(
                confirmed
                    ? "Đã tính xong theo các thông số anh xác nhận. " +
                      "Cần kiểm tra lại mạng node-by-node, vật cản và hồ sơ thẩm duyệt."
                    : "Đã tính bản sơ bộ nhưng CHƯA xác nhận thông số tiêu chuẩn. " +
                      "Không dùng trực tiếp để phát hành hồ sơ.",
                isError: false,
                isSuccess: confirmed);
        }

        private void AddFireResult(
            string group,
            string item,
            double value,
            string unit,
            string note)
        {
            _fireCalculationRows.Add(
                new FireCalculationResultRow
                {
                    Index = _fireCalculationRows.Count + 1,
                    Group = group,
                    Item = item,
                    Value = FireDisplay(value),
                    Unit = unit,
                    Note = note
                });
        }

        private static string FireDisplay(double value)
        {
            if (Math.Abs(value - Math.Round(value)) < 0.0000001)
                return value.ToString("N0", CultureInfo.CurrentCulture);

            return value.ToString("N2", CultureInfo.CurrentCulture);
        }

        private static string FireInvariant(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static void RequirePositiveFireNumber(
            TextBox textBox,
            string fieldName,
            List<string> errors,
            out double value)
        {
            if (!TryParsePositiveFireDesignNumber(textBox?.Text, out value))
                errors.Add(fieldName);
        }

        private static void RequireNonNegativeFireNumber(
            TextBox textBox,
            string fieldName,
            List<string> errors,
            out double value)
        {
            if (!TryParseNonNegativeFireNumber(textBox?.Text, out value))
                errors.Add(fieldName);
        }

        private static void RequirePositiveFireInteger(
            TextBox textBox,
            string fieldName,
            List<string> errors,
            out int value)
        {
            if (!int.TryParse(
                    (textBox?.Text ?? string.Empty).Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value) ||
                value <= 0)
            {
                errors.Add(fieldName);
            }
        }

        private static bool TryParseNonNegativeFireNumber(
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
                   value >= 0.0;
        }

        private double ResolveFireCalculationTotalArea()
        {
            if (TryParsePositiveFireDesignNumber(
                    TxtFireCalcTotalArea?.Text,
                    out double manualArea))
            {
                return manualArea;
            }

            return Math.Max(
                0,
                _fireDesignAreas.Sum(x => x.SignedAreaM2));
        }

        private int ResolveFireFloorCount()
        {
            return int.TryParse(
                       (TxtFireFloorCount?.Text ?? string.Empty).Trim(),
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out int floorCount) &&
                   floorCount > 0
                ? floorCount
                : 0;
        }

        private double ResolveFireBuildingHeight()
        {
            return TryParseNonNegativeFireNumber(
                TxtFireBuildingHeight?.Text,
                out double height)
                ? height
                : 0;
        }

        private void SyncFireCalculationAreaFromDrawing(bool force)
        {
            if (TxtFireCalcTotalArea == null)
                return;

            double drawingArea =
                _fireDesignAreas.Sum(x => x.SignedAreaM2);

            if (drawingArea <= 0)
                return;

            bool alreadyHasValue =
                TryParsePositiveFireDesignNumber(
                    TxtFireCalcTotalArea.Text,
                    out _);

            if (force || !alreadyHasValue)
            {
                TxtFireCalcTotalArea.Text =
                    drawingArea.ToString(
                        "0.##",
                        CultureInfo.InvariantCulture);
            }
        }

        private void UpdateFireCalculationSummary()
        {
            if (TxtFireCalculationSummary == null)
                return;

            if (_fireLastCalculation == null)
            {
                TxtFireCalculationSummary.Text =
                    "Chưa có kết quả tính toán.";
                return;
            }

            FireCalculationSnapshot result = _fireLastCalculation;

            TxtFireCalculationSummary.Text =
                "Q tổng = " + FireDisplay(result.CombinedDemandLps) +
                " L/s · Bơm ≈ " + FireDisplay(result.PumpFlowM3h) +
                " m³/h @ " + FireDisplay(result.PumpHeadMeters) +
                " mH₂O" +
                (result.MainPipeDnMm > 0
                    ? " · Ống chính DN" + result.MainPipeDnMm
                    : string.Empty) +
                (result.ActualSprinklerCount > 0
                    ? " · " + result.ActualSprinklerCount + " đầu phun"
                    : string.Empty) +
                " · Bể hữu ích ≈ " +
                FireDisplay(result.WaterVolumeM3) + " m³" +
                (result.IsEngineerConfirmed
                    ? " · ĐÃ XÁC NHẬN THÔNG SỐ"
                    : " · CHƯA XÁC NHẬN THÔNG SỐ");
        }

        private void UpdateFireWorkflowSummaries()
        {
            if (TxtFireSystemsSummary != null)
            {
                List<string> selected =
                    _fireSystemRows
                        .Where(x => x.IsSelected)
                        .Select(x => x.SystemName)
                        .ToList();

                TxtFireSystemsSummary.Text =
                    selected.Count == 0
                        ? "Chưa chọn hệ thống."
                        : "Đang chọn " + selected.Count + " hệ: " +
                          string.Join(", ", selected.Take(6)) +
                          (selected.Count > 6 ? ", …" : string.Empty);
            }

            UpdateFireCalculationSummary();
        }

        private void BtnFireSaveProject_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!_fireWorkflowInitialized)
                FireDesignWorkflowPanel_Loaded(this, new RoutedEventArgs());

            CommitFireDataGridEdits();

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Lưu dự án tính toán PCCC",
                Filter = "Dự án PCCC (*.pccc.json)|*.pccc.json|JSON (*.json)|*.json",
                FileName = BuildFireSafeFileName(TxtFireProjectName?.Text) +
                           ".pccc.json"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                FireDesignProjectState state = CaptureFireProjectState();
                string json = JsonSerializer.Serialize(
                    state,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                File.WriteAllText(
                    dialog.FileName,
                    json,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

                SetFireDesignStatus(
                    "Đã lưu dự án: " +
                    System.IO.Path.GetFileName(dialog.FileName) + ".",
                    isError: false,
                    isSuccess: true);
            }
            catch (Exception ex)
            {
                SetFireDesignStatus(
                    "Không lưu được dự án: " + ex.Message,
                    isError: true);
            }
        }

        private void BtnFireLoadProject_Click(
            object sender,
            RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Mở dự án tính toán PCCC",
                Filter = "Dự án PCCC (*.pccc.json;*.json)|*.pccc.json;*.json"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                if (!_fireWorkflowInitialized)
                    FireDesignWorkflowPanel_Loaded(this, new RoutedEventArgs());

                string json = File.ReadAllText(dialog.FileName, Encoding.UTF8);
                FireDesignProjectState state =
                    JsonSerializer.Deserialize<FireDesignProjectState>(json);

                if (state == null)
                    throw new InvalidDataException("File dự án không hợp lệ.");

                ApplyFireProjectState(state);

                SetFireDesignStatus(
                    "Đã mở dự án: " +
                    System.IO.Path.GetFileName(dialog.FileName) +
                    ". Hãy bấm TÍNH TOÀN TOÀN BỘ để cập nhật kết quả.",
                    isError: false,
                    isSuccess: true);
            }
            catch (Exception ex)
            {
                SetFireDesignStatus(
                    "Không mở được dự án: " + ex.Message,
                    isError: true);
            }
        }

        private FireDesignProjectState CaptureFireProjectState()
        {
            return new FireDesignProjectState
            {
                Version = 2,
                SavedAt = DateTime.Now,
                ProjectName = TxtFireProjectName?.Text,
                ProjectUseTag = GetFireComboTag(CmbFireProjectUse),
                FloorCount = TxtFireFloorCount?.Text,
                BuildingHeight = TxtFireBuildingHeight?.Text,
                CalculationArea = TxtFireCalcTotalArea?.Text,
                OwnerDocumentPath = _fireOwnerDocumentPath,
                OwnerRequirements = TxtFireOwnerRequirements?.Text,
                HazardPresetTag = GetFireComboTag(CmbFireHazardPreset),
                Density = TxtFireDensity?.Text,
                DesignArea = TxtFireDesignArea?.Text,
                SprinklerCoverage = TxtFireSprinklerCoverage?.Text,
                KFactor = TxtFireKFactor?.Text,
                IndoorFlow = TxtFireIndoorFlow?.Text,
                IndoorJetCount = TxtFireIndoorJetCount?.Text,
                OutdoorFlow = TxtFireOutdoorFlow?.Text,
                Duration = TxtFireDuration?.Text,
                StaticHead = TxtFireStaticHead?.Text,
                ResidualPressure = TxtFireResidualPressure?.Text,
                PipeLoss = TxtFirePipeLoss?.Text,
                SafetyMargin = TxtFireSafetyMargin?.Text,
                LayoutSpacingX = TxtFireLayoutSpacingX?.Text,
                LayoutSpacingY = TxtFireLayoutSpacingY?.Text,
                LayoutWallOffset = TxtFireLayoutWallOffset?.Text,
                LayoutRotation = TxtFireLayoutRotation?.Text,
                SprinklerBlockName = TxtFireSprinklerBlockName?.Text,
                HazenC = TxtFireHazenC?.Text,
                MaxVelocity = TxtFireMaxVelocity?.Text,
                MaxUnitLoss = TxtFireMaxUnitLoss?.Text,
                CriticalPathLength = TxtFireCriticalPathLength?.Text,
                FittingsAllowance = TxtFireFittingsAllowance?.Text,
                HeadsPerBranch = TxtFireHeadsPerBranch?.Text,
                ActualSprinklerCount = _fireActualSprinklerCount,
                DetectorCoverage = TxtFireDetectorCoverage?.Text,
                ExtinguisherCoverage = TxtFireExtinguisherCoverage?.Text,
                CabinetsPerFloor = TxtFireCabinetsPerFloor?.Text,
                CallPointsPerFloor = TxtFireCallPointsPerFloor?.Text,
                EngineerConfirmed =
                    ChkFireEngineerConfirmed?.IsChecked == true,
                SelectedStandardCodes =
                    _fireStandardRows
                        .Where(x => x.IsSelected)
                        .Select(x => x.Code)
                        .ToList(),
                SelectedSystemCodes =
                    _fireSystemRows
                        .Where(x => x.IsSelected)
                        .Select(x => x.Code)
                        .ToList(),
                Areas =
                    _fireDesignAreas
                        .Select(
                            x => new FireDesignAreaState
                            {
                                AreaName = x.AreaName,
                                LayerName = x.LayerName,
                                EntityType = x.EntityType,
                                AreaM2 = x.AreaM2,
                                IsSubtraction = x.IsSubtraction,
                                SourceKey = x.SourceKey
                            })
                        .ToList()
            };
        }

        private void ApplyFireProjectState(FireDesignProjectState state)
        {
            TxtFireProjectName.Text = state.ProjectName ?? string.Empty;
            SelectFireComboByTag(CmbFireProjectUse, state.ProjectUseTag);
            TxtFireFloorCount.Text = state.FloorCount ?? "1";
            TxtFireBuildingHeight.Text = state.BuildingHeight ?? string.Empty;
            TxtFireCalcTotalArea.Text = state.CalculationArea ?? string.Empty;
            TxtFireOwnerRequirements.Text =
                state.OwnerRequirements ?? string.Empty;
            _fireOwnerDocumentPath = state.OwnerDocumentPath ?? string.Empty;

            SelectFireComboByTag(
                CmbFireHazardPreset,
                state.HazardPresetTag ?? "MANUAL");

            SetFireText(TxtFireDensity, state.Density);
            SetFireText(TxtFireDesignArea, state.DesignArea);
            SetFireText(TxtFireSprinklerCoverage, state.SprinklerCoverage);
            SetFireText(TxtFireKFactor, state.KFactor);
            SetFireText(TxtFireIndoorFlow, state.IndoorFlow);
            SetFireText(TxtFireIndoorJetCount, state.IndoorJetCount);
            SetFireText(TxtFireOutdoorFlow, state.OutdoorFlow);
            SetFireText(TxtFireDuration, state.Duration);
            SetFireText(TxtFireStaticHead, state.StaticHead);
            SetFireText(TxtFireResidualPressure, state.ResidualPressure);
            SetFireText(TxtFirePipeLoss, state.PipeLoss);
            SetFireText(TxtFireSafetyMargin, state.SafetyMargin);
            SetFireText(TxtFireLayoutSpacingX, state.LayoutSpacingX);
            SetFireText(TxtFireLayoutSpacingY, state.LayoutSpacingY);
            SetFireText(TxtFireLayoutWallOffset, state.LayoutWallOffset);
            SetFireText(TxtFireLayoutRotation, state.LayoutRotation);
            SetFireText(
                TxtFireSprinklerBlockName,
                state.SprinklerBlockName);
            SetFireText(TxtFireHazenC, state.HazenC);
            SetFireText(TxtFireMaxVelocity, state.MaxVelocity);
            SetFireText(TxtFireMaxUnitLoss, state.MaxUnitLoss);
            SetFireText(
                TxtFireCriticalPathLength,
                state.CriticalPathLength);
            SetFireText(
                TxtFireFittingsAllowance,
                state.FittingsAllowance);
            SetFireText(TxtFireHeadsPerBranch, state.HeadsPerBranch);
            _fireActualSprinklerCount =
                Math.Max(0, state.ActualSprinklerCount);
            SetFireText(TxtFireDetectorCoverage, state.DetectorCoverage);
            SetFireText(
                TxtFireExtinguisherCoverage,
                state.ExtinguisherCoverage);
            SetFireText(TxtFireCabinetsPerFloor, state.CabinetsPerFloor);
            SetFireText(TxtFireCallPointsPerFloor, state.CallPointsPerFloor);

            ChkFireEngineerConfirmed.IsChecked = state.EngineerConfirmed;

            HashSet<string> selectedStandards =
                new HashSet<string>(
                    state.SelectedStandardCodes ?? new List<string>(),
                    StringComparer.OrdinalIgnoreCase);

            foreach (FireStandardRow standard in _fireStandardRows)
                standard.IsSelected = selectedStandards.Contains(standard.Code);

            HashSet<string> selectedSystems =
                new HashSet<string>(
                    state.SelectedSystemCodes ?? new List<string>(),
                    StringComparer.OrdinalIgnoreCase);

            foreach (FireSystemRecommendationRow system in _fireSystemRows)
            {
                system.IsSelected = selectedSystems.Contains(system.Code);
                system.Basis = system.IsSelected
                    ? "Khôi phục từ file dự án"
                    : "Chưa chọn";
            }

            _fireDesignAreas.Clear();
            _fireDesignAreaKeys.Clear();

            foreach (FireDesignAreaState savedArea in
                     state.Areas ?? new List<FireDesignAreaState>())
            {
                string sourceKey =
                    string.IsNullOrWhiteSpace(savedArea.SourceKey)
                        ? "PROJECT_FILE|" + Guid.NewGuid().ToString("N")
                        : savedArea.SourceKey;

                _fireDesignAreas.Add(
                    new FireDesignAreaRow
                    {
                        SourceKey = sourceKey,
                        AreaName = savedArea.AreaName,
                        LayerName = savedArea.LayerName,
                        EntityType = savedArea.EntityType,
                        AreaM2 = Math.Abs(savedArea.AreaM2),
                        IsSubtraction = savedArea.IsSubtraction
                    });
                _fireDesignAreaKeys.Add(sourceKey);
            }

            RefreshFireDesignAreaIndexes();
            UpdateFireDesignAreaSummary();
            AnalyzeFireOwnerRequirements();
            _fireCalculationRows.Clear();
            _fireLastCalculation = null;
            DgFireStandards.Items.Refresh();
            DgFireSystems.Items.Refresh();
            DgFireCalculationResults.Items.Refresh();
            UpdateFireSprinklerLayoutSummary();
            UpdateFireWorkflowSummaries();
        }

        private static void SetFireText(TextBox textBox, string value)
        {
            if (textBox != null && value != null)
                textBox.Text = value;
        }

        private static string GetFireComboTag(ComboBox comboBox)
        {
            return (comboBox?.SelectedItem as ComboBoxItem)
                       ?.Tag
                       ?.ToString() ?? string.Empty;
        }

        private static void SelectFireComboByTag(
            ComboBox comboBox,
            string tag)
        {
            if (comboBox == null)
                return;

            foreach (object item in comboBox.Items)
            {
                if (item is ComboBoxItem comboItem &&
                    string.Equals(
                        comboItem.Tag?.ToString(),
                        tag,
                        StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = comboItem;
                    return;
                }
            }
        }

        private void BtnFireExportReport_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_fireCalculationRows.Count == 0 ||
                _fireLastCalculation == null)
            {
                SetFireDesignStatus(
                    "Hãy bấm TÍNH TOÁN TOÀN BỘ trước khi xuất báo cáo.",
                    isError: true);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Xuất báo cáo tính toán PCCC",
                Filter = "CSV mở bằng Excel (*.csv)|*.csv",
                FileName = BuildFireSafeFileName(TxtFireProjectName?.Text) +
                           "_PCCC.csv"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                var csv = new StringBuilder();

                AppendFireCsvRow(csv, "BÁO CÁO TÍNH TOÁN PCCC V1");
                AppendFireCsvRow(
                    csv,
                    "Trạng thái",
                    _fireLastCalculation.IsEngineerConfirmed
                        ? "ĐÃ XÁC NHẬN THÔNG SỐ ĐẦU VÀO"
                        : "SƠ BỘ - CHƯA XÁC NHẬN THÔNG SỐ");
                AppendFireCsvRow(csv, "Tên dự án", TxtFireProjectName?.Text);
                AppendFireCsvRow(
                    csv,
                    "Công năng",
                    GetFireProjectUseDisplayName(
                        GetFireComboTag(CmbFireProjectUse)));
                AppendFireCsvRow(csv, "Số tầng", TxtFireFloorCount?.Text);
                AppendFireCsvRow(
                    csv,
                    "Chiều cao (m)",
                    TxtFireBuildingHeight?.Text);
                AppendFireCsvRow(
                    csv,
                    "Ngày tính",
                    _fireLastCalculation.CalculatedAt.ToString(
                        "dd/MM/yyyy HH:mm"));

                AppendFireCsvRow(csv);
                AppendFireCsvRow(csv, "TIÊU CHUẨN ĐANG CHỌN");
                AppendFireCsvRow(csv, "Mã", "Phạm vi", "Trạng thái", "Nguồn");

                foreach (FireStandardRow standard in
                         _fireStandardRows.Where(x => x.IsSelected))
                {
                    AppendFireCsvRow(
                        csv,
                        standard.Code,
                        standard.Scope,
                        standard.Status,
                        standard.SourceUrl);
                }

                AppendFireCsvRow(csv);
                AppendFireCsvRow(csv, "HỆ THỐNG ĐANG CHỌN");
                AppendFireCsvRow(csv, "Hệ thống", "Căn cứ sàng lọc");

                foreach (FireSystemRecommendationRow system in
                         _fireSystemRows.Where(x => x.IsSelected))
                {
                    AppendFireCsvRow(csv, system.SystemName, system.Basis);
                }

                AppendFireCsvRow(csv);
                AppendFireCsvRow(csv, "KẾT QUẢ TÍNH TOÁN");
                AppendFireCsvRow(
                    csv,
                    "STT", "Nhóm", "Chỉ tiêu", "Giá trị", "Đơn vị", "Ghi chú");

                foreach (FireCalculationResultRow row in _fireCalculationRows)
                {
                    AppendFireCsvRow(
                        csv,
                        row.Index.ToString(CultureInfo.InvariantCulture),
                        row.Group,
                        row.Item,
                        row.Value,
                        row.Unit,
                        row.Note);
                }

                AppendFireCsvRow(csv);
                AppendFireCsvRow(
                    csv,
                    "LƯU Ý",
                    "Kết quả là tính toán sơ bộ. Kỹ sư chịu trách nhiệm kiểm tra " +
                    "công năng, nhóm nguy hiểm cháy, đồng thời hệ thống, mạng ống " +
                    "thủy lực và bản tiêu chuẩn áp dụng trước khi phát hành.");

                File.WriteAllText(
                    dialog.FileName,
                    csv.ToString(),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

                SetFireDesignStatus(
                    "Đã xuất báo cáo CSV: " +
                    System.IO.Path.GetFileName(dialog.FileName) + ".",
                    isError: false,
                    isSuccess: true);
            }
            catch (Exception ex)
            {
                SetFireDesignStatus(
                    "Không xuất được báo cáo: " + ex.Message,
                    isError: true);
            }
        }

        private static void AppendFireCsvRow(
            StringBuilder builder,
            params string[] values)
        {
            builder.AppendLine(
                string.Join(
                    ",",
                    (values ?? Array.Empty<string>())
                        .Select(EscapeFireCsv)));
        }

        private static string EscapeFireCsv(string value)
        {
            string text = value ?? string.Empty;

            if (text.Contains(',') ||
                text.Contains('"') ||
                text.Contains('\r') ||
                text.Contains('\n'))
            {
                return "\"" + text.Replace("\"", "\"\"") + "\"";
            }

            return text;
        }

        private void BtnFireInsertResultTable_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_fireCalculationRows.Count == 0 ||
                _fireLastCalculation == null)
            {
                SetFireDesignStatus(
                    "Hãy bấm TÍNH TOÁN TOÀN BỘ trước khi chèn bảng.",
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

            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();

            PromptPointResult pointResult =
                doc.Editor.GetPoint(
                    "\nChọn điểm đặt bảng kết quả tính toán PCCC: ");

            if (pointResult.Status != PromptStatus.OK)
                return;

            try
            {
                using (doc.LockDocument())
                using (Transaction transaction =
                       doc.Database.TransactionManager.StartTransaction())
                {
                    Database database = doc.Database;
                    BlockTableRecord space =
                        transaction.GetObject(
                            database.CurrentSpaceId,
                            OpenMode.ForWrite) as BlockTableRecord;

                    if (space == null)
                        throw new InvalidOperationException("Không mở được không gian vẽ.");

                    double scale = ResolveFireTableMillimeterScale(database);
                    int rowCount = _fireCalculationRows.Count + 3;

                    var table = new Table
                    {
                        TableStyle = database.Tablestyle,
                        Position = pointResult.Value
                    };

                    table.SetDatabaseDefaults(database);
                    table.SetSize(rowCount, 5);
                    table.SetRowHeight(6.0 * scale);
                    table.Columns[0].Width = 12.0 * scale;
                    table.Columns[1].Width = 32.0 * scale;
                    table.Columns[2].Width = 62.0 * scale;
                    table.Columns[3].Width = 24.0 * scale;
                    table.Columns[4].Width = 22.0 * scale;

                    table.Cells[0, 0].TextString =
                        "BẢNG TÍNH PCCC V1 - " +
                        (string.IsNullOrWhiteSpace(TxtFireProjectName?.Text)
                            ? "CHƯA ĐẶT TÊN"
                            : TxtFireProjectName.Text.Trim());
                    table.MergeCells(
                        CellRange.Create(table, 0, 0, 0, 4));

                    string status =
                        _fireLastCalculation.IsEngineerConfirmed
                            ? "ĐÃ XÁC NHẬN THÔNG SỐ ĐẦU VÀO"
                            : "SƠ BỘ - CHƯA XÁC NHẬN THÔNG SỐ";

                    table.Cells[1, 0].TextString = status;
                    table.MergeCells(
                        CellRange.Create(table, 1, 0, 1, 4));

                    string[] headers =
                    {
                        "STT", "NHÓM", "CHỈ TIÊU", "GIÁ TRỊ", "ĐƠN VỊ"
                    };

                    for (int column = 0; column < headers.Length; column++)
                        table.Cells[2, column].TextString = headers[column];

                    for (int index = 0;
                         index < _fireCalculationRows.Count;
                         index++)
                    {
                        FireCalculationResultRow row =
                            _fireCalculationRows[index];
                        int tableRow = index + 3;

                        table.Cells[tableRow, 0].TextString =
                            row.Index.ToString(CultureInfo.InvariantCulture);
                        table.Cells[tableRow, 1].TextString = row.Group ?? "";
                        table.Cells[tableRow, 2].TextString = row.Item ?? "";
                        table.Cells[tableRow, 3].TextString = row.Value ?? "";
                        table.Cells[tableRow, 4].TextString = row.Unit ?? "";
                    }

                    for (int row = 0; row < rowCount; row++)
                    {
                        for (int column = 0; column < 5; column++)
                        {
                            table.Cells[row, column].Alignment =
                                CellAlignment.MiddleCenter;
                            table.Cells[row, column].TextHeight = 2.2 * scale;
                        }
                    }

                    table.Cells[0, 0].TextHeight = 3.0 * scale;

                    space.AppendEntity(table);
                    transaction.AddNewlyCreatedDBObject(table, true);
                    transaction.Commit();
                }

                SetFireDesignStatus(
                    "Đã chèn bảng kết quả vào bản vẽ.",
                    isError: false,
                    isSuccess: true);
            }
            catch (Exception ex)
            {
                SetFireDesignStatus(
                    "Không chèn được bảng vào CAD: " + ex.Message,
                    isError: true);
            }
        }

        private static double ResolveFireTableMillimeterScale(Database database)
        {
            switch ((int)database.Insunits)
            {
                case 1: return 1.0 / 25.4;   // inch
                case 2: return 1.0 / 304.8;  // feet
                case 5: return 0.1;          // cm
                case 6: return 0.001;        // m
                default: return 1.0;         // mm hoặc unitless
            }
        }

        private static string BuildFireSafeFileName(string value)
        {
            string name =
                string.IsNullOrWhiteSpace(value)
                    ? "DU_AN"
                    : value.Trim();

            foreach (char invalid in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');

            name = Regex.Replace(name, @"\s+", "_");

            return name.Length <= 80
                ? name
                : name.Substring(0, 80);
        }

        private static string GetFireSystemDisplayName(string code)
        {
            switch ((code ?? string.Empty).ToUpperInvariant())
            {
                case "EXTINGUISHER": return "Bình chữa cháy";
                case "FIRE_ALARM": return "Báo cháy";
                case "SPRINKLER": return "Sprinkler";
                case "INDOOR_HYDRANT": return "Họng nước trong nhà";
                case "OUTDOOR_HYDRANT": return "Trụ nước ngoài nhà";
                case "SMOKE_CONTROL": return "Hút khói/tăng áp";
                case "EMERGENCY": return "Đèn sự cố/thoát nạn";
                case "PUMP_TANK": return "Bơm/bể nước";
                case "FOAM": return "Chữa cháy bọt";
                case "CLEAN_AGENT": return "Khí sạch";
                default: return code ?? string.Empty;
            }
        }
    }

    internal sealed class FireStandardRow
    {
        public bool IsSelected { get; set; }
        public string Code { get; set; }
        public string Scope { get; set; }
        public string Status { get; set; }
        public string SourceUrl { get; set; }
    }

    internal sealed class FireSystemRecommendationRow
    {
        public bool IsSelected { get; set; }
        public string Code { get; set; }
        public string SystemName { get; set; }
        public string Basis { get; set; }
    }

    internal sealed class FireCalculationResultRow
    {
        public int Index { get; set; }
        public string Group { get; set; }
        public string Item { get; set; }
        public string Value { get; set; }
        public string Unit { get; set; }
        public string Note { get; set; }
    }

    internal sealed class FireCalculationSnapshot
    {
        public DateTime CalculatedAt { get; set; }
        public double TotalAreaM2 { get; set; }
        public double SprinklerDemandLps { get; set; }
        public double CombinedDemandLps { get; set; }
        public double PumpFlowM3h { get; set; }
        public double PumpHeadMeters { get; set; }
        public double WaterVolumeM3 { get; set; }
        public int MainPipeDnMm { get; set; }
        public double CriticalPathFrictionLossM { get; set; }
        public int ActualSprinklerCount { get; set; }
        public bool IsEngineerConfirmed { get; set; }
    }

    internal sealed class FireDesignProjectState
    {
        public int Version { get; set; }
        public DateTime SavedAt { get; set; }
        public string ProjectName { get; set; }
        public string ProjectUseTag { get; set; }
        public string FloorCount { get; set; }
        public string BuildingHeight { get; set; }
        public string CalculationArea { get; set; }
        public string OwnerDocumentPath { get; set; }
        public string OwnerRequirements { get; set; }
        public string HazardPresetTag { get; set; }
        public string Density { get; set; }
        public string DesignArea { get; set; }
        public string SprinklerCoverage { get; set; }
        public string KFactor { get; set; }
        public string IndoorFlow { get; set; }
        public string IndoorJetCount { get; set; }
        public string OutdoorFlow { get; set; }
        public string Duration { get; set; }
        public string StaticHead { get; set; }
        public string ResidualPressure { get; set; }
        public string PipeLoss { get; set; }
        public string SafetyMargin { get; set; }
        public string LayoutSpacingX { get; set; }
        public string LayoutSpacingY { get; set; }
        public string LayoutWallOffset { get; set; }
        public string LayoutRotation { get; set; }
        public string SprinklerBlockName { get; set; }
        public string HazenC { get; set; }
        public string MaxVelocity { get; set; }
        public string MaxUnitLoss { get; set; }
        public string CriticalPathLength { get; set; }
        public string FittingsAllowance { get; set; }
        public string HeadsPerBranch { get; set; }
        public int ActualSprinklerCount { get; set; }
        public string DetectorCoverage { get; set; }
        public string ExtinguisherCoverage { get; set; }
        public string CabinetsPerFloor { get; set; }
        public string CallPointsPerFloor { get; set; }
        public bool EngineerConfirmed { get; set; }
        public List<string> SelectedStandardCodes { get; set; }
        public List<string> SelectedSystemCodes { get; set; }
        public List<FireDesignAreaState> Areas { get; set; }
    }

    internal sealed class FireDesignAreaState
    {
        public string AreaName { get; set; }
        public string LayerName { get; set; }
        public string EntityType { get; set; }
        public double AreaM2 { get; set; }
        public bool IsSubtraction { get; set; }
        public string SourceKey { get; set; }
    }
}
