// FIRE-DESIGN-STEP2-20260829: đọc chữ, block và gợi ý dữ liệu công trình.
#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace ClassLibrary4
{
    public partial class BOCTACHUI
    {
        private readonly ObservableCollection<FireDrawingTextRow>
            _fireDrawingTextRows =
                new ObservableCollection<FireDrawingTextRow>();

        private readonly HashSet<string> _fireDetectedFloors =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _fireDetectedRooms =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, int> _fireUseScores =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private bool _fireDrawingReaderInitialized;
        private string _fireSuggestedUseTag = string.Empty;
        private int _fireLastRawTextCount;

        private static readonly string[][] FireRoomKeywords =
        {
            new[] { "PHONG MAY BOM", "Phòng máy bơm" },
            new[] { "PHONG BOM", "Phòng bơm" },
            new[] { "PHONG DIEN", "Phòng điện" },
            new[] { "TRAM DIEN", "Trạm điện" },
            new[] { "PHONG MAY PHAT", "Phòng máy phát" },
            new[] { "PHONG RAC", "Phòng rác" },
            new[] { "PHONG KY THUAT", "Phòng kỹ thuật" },
            new[] { "CAU THANG", "Cầu thang" },
            new[] { "HANH LANG", "Hành lang" },
            new[] { "SANH", "Sảnh" },
            new[] { "KHO", "Kho" },
            new[] { "XUONG", "Xưởng" },
            new[] { "SAN XUAT", "Khu sản xuất" },
            new[] { "VAN PHONG", "Văn phòng" },
            new[] { "OFFICE", "Văn phòng" },
            new[] { "CAN HO", "Căn hộ" },
            new[] { "PHONG NGU", "Phòng ngủ" },
            new[] { "BEP", "Bếp" },
            new[] { "NHA VE SINH", "Nhà vệ sinh" },
            new[] { "WC", "WC" },
            new[] { "LOP HOC", "Lớp học" },
            new[] { "PHONG HOC", "Phòng học" },
            new[] { "PHONG KHAM", "Phòng khám" },
            new[] { "SIEU THI", "Siêu thị" },
            new[] { "SHOP", "Cửa hàng" },
            new[] { "BAI XE", "Bãi xe" },
            new[] { "GARAGE", "Bãi xe" }
        };

        private void FireDrawingReaderPanel_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (_fireDrawingReaderInitialized)
                return;

            _fireDrawingReaderInitialized = true;
            DgFireDrawingTexts.ItemsSource = _fireDrawingTextRows;
            UpdateFireDrawingReaderSummary();
        }

        private void BtnFireScanDrawingTexts_Click(
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

            if (!_fireDrawingReaderInitialized)
            {
                FireDrawingReaderPanel_Loaded(
                    this,
                    new RoutedEventArgs());
            }

            Editor ed = doc.Editor;
            Database db = doc.Database;

            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();

            try
            {
                using (doc.LockDocument())
                {
                    PromptSelectionOptions options =
                        new PromptSelectionOptions
                        {
                            MessageForAdding =
                                "\nQuét vùng có TEXT, MTEXT hoặc Block cần đọc: "
                        };

                    TypedValue[] values =
                    {
                        new TypedValue(
                            (int)DxfCode.Start,
                            "TEXT,MTEXT,INSERT,MULTILEADER")
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
                            "Đã hủy quét hoặc vùng chọn không có chữ/block.",
                            isError: false);
                        return;
                    }

                    var candidates =
                        new List<FireDrawingTextCandidate>();

                    int rawTextCount = 0;

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
                                continue;
                            }

                            if (entity == null)
                                continue;

                            CollectFireDrawingTextsFromEntity(
                                entity,
                                transaction,
                                entity.Layer,
                                depth: 0,
                                blockPath: new HashSet<ObjectId>(),
                                candidates,
                                ref rawTextCount);
                        }

                        transaction.Commit();
                    }

                    ApplyFireDrawingTextCandidates(
                        candidates,
                        rawTextCount);

                    string status =
                        "Đã đọc " + rawTextCount +
                        " chuỗi và nhận " + _fireDrawingTextRows.Count +
                        " thông tin liên quan đến thiết kế PCCC.";

                    if (_fireDrawingTextRows.Count == 0)
                    {
                        status +=
                            " Hãy quét rộng hơn hoặc kiểm tra chữ có nằm trong XREF/block hay không.";
                    }

                    SetFireDesignStatus(
                        status,
                        isError: _fireDrawingTextRows.Count == 0,
                        isSuccess: _fireDrawingTextRows.Count > 0);

                    ed.WriteMessage("\n" + status);
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                SetFireDesignStatus(
                    "AutoCAD không thể đọc thông tin vùng chọn: " + ex.Message,
                    isError: true);
            }
            catch (Exception ex)
            {
                SetFireDesignStatus(
                    "Không thể phân tích chữ trên bản vẽ: " + ex.Message,
                    isError: true);
            }
        }

        private void BtnFireApplyDrawingSuggestions_Click(
            object sender,
            RoutedEventArgs e)
        {
            bool applied = false;

            if (!string.IsNullOrWhiteSpace(_fireSuggestedUseTag))
            {
                foreach (object item in CmbFireProjectUse.Items)
                {
                    if (item is ComboBoxItem comboItem &&
                        string.Equals(
                            comboItem.Tag?.ToString(),
                            _fireSuggestedUseTag,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        CmbFireProjectUse.SelectedItem = comboItem;
                        applied = true;
                        break;
                    }
                }
            }

            if (_fireDetectedFloors.Count > 0)
            {
                TxtFireFloorCount.Text =
                    _fireDetectedFloors.Count
                        .ToString(CultureInfo.InvariantCulture);
                applied = true;
            }

            if (!applied)
            {
                SetFireDesignStatus(
                    "Chưa có gợi ý đủ tin cậy để áp dụng. Hãy quét thêm chữ trên bản vẽ.",
                    isError: false);
                return;
            }

            SetFireDesignStatus(
                "Đã điền công năng và số tầng theo kết quả nhận dạng. " +
                "Anh cần kiểm tra lại trước khi dùng để tra tiêu chuẩn.",
                isError: false,
                isSuccess: true);
        }

        private void BtnFireClearDrawingTexts_Click(
            object sender,
            RoutedEventArgs e)
        {
            ClearFireDrawingReaderResults();
            SetFireDesignStatus(
                "Đã xóa kết quả đọc chữ trên bản vẽ.",
                isError: false);
        }

        private void CollectFireDrawingTextsFromEntity(
            Entity entity,
            Transaction transaction,
            string inheritedLayer,
            int depth,
            HashSet<ObjectId> blockPath,
            List<FireDrawingTextCandidate> candidates,
            ref int rawTextCount)
        {
            if (entity == null || depth > 5)
                return;

            string layerName =
                ResolveFireDrawingTextLayer(
                    entity.Layer,
                    inheritedLayer);

            if (entity is AttributeDefinition)
                return;

            if (entity is DBText dbText)
            {
                AddFireDrawingTextCandidate(
                    dbText.TextString,
                    layerName,
                    "TEXT",
                    candidates,
                    ref rawTextCount);
                return;
            }

            if (entity is MText mText)
            {
                AddFireDrawingTextCandidate(
                    mText.Text,
                    layerName,
                    "MTEXT",
                    candidates,
                    ref rawTextCount);
                return;
            }

            if (entity is MLeader leader)
            {
                try
                {
                    MText leaderText = leader.MText;

                    if (leaderText != null)
                    {
                        AddFireDrawingTextCandidate(
                            leaderText.Text,
                            layerName,
                            "MULTILEADER",
                            candidates,
                            ref rawTextCount);
                    }
                }
                catch
                {
                }

                return;
            }

            if (!(entity is BlockReference blockReference))
                return;

            try
            {
                foreach (ObjectId attributeId in
                    blockReference.AttributeCollection)
                {
                    if (attributeId.IsNull ||
                        !attributeId.IsValid ||
                        attributeId.IsErased)
                    {
                        continue;
                    }

                    AttributeReference attribute =
                        transaction.GetObject(
                            attributeId,
                            OpenMode.ForRead,
                            false) as AttributeReference;

                    if (attribute == null)
                        continue;

                    AddFireDrawingTextCandidate(
                        attribute.TextString,
                        ResolveFireDrawingTextLayer(
                            attribute.Layer,
                            layerName),
                        "BLOCK ATTRIBUTE",
                        candidates,
                        ref rawTextCount);
                }
            }
            catch
            {
            }

            ObjectId definitionId =
                blockReference.BlockTableRecord;

            if (definitionId.IsNull ||
                !definitionId.IsValid ||
                !blockPath.Add(definitionId))
            {
                return;
            }

            try
            {
                BlockTableRecord definition =
                    transaction.GetObject(
                        definitionId,
                        OpenMode.ForRead,
                        false) as BlockTableRecord;

                if (definition == null)
                    return;

                foreach (ObjectId childId in definition)
                {
                    if (childId.IsNull ||
                        !childId.IsValid ||
                        childId.IsErased)
                    {
                        continue;
                    }

                    Entity child = null;

                    try
                    {
                        child = transaction.GetObject(
                            childId,
                            OpenMode.ForRead,
                            false) as Entity;
                    }
                    catch
                    {
                        continue;
                    }

                    if (child == null)
                        continue;

                    CollectFireDrawingTextsFromEntity(
                        child,
                        transaction,
                        layerName,
                        depth + 1,
                        blockPath,
                        candidates,
                        ref rawTextCount);
                }
            }
            finally
            {
                blockPath.Remove(definitionId);
            }
        }

        private void AddFireDrawingTextCandidate(
            string source,
            string layerName,
            string sourceType,
            List<FireDrawingTextCandidate> candidates,
            ref int rawTextCount)
        {
            string text = CleanFireDrawingSourceText(source);

            if (string.IsNullOrWhiteSpace(text))
                return;

            rawTextCount++;

            if (!TryClassifyFireDrawingText(
                    text,
                    out string categoryCode,
                    out string categoryName,
                    out string normalizedValue,
                    out string suggestedUseTag,
                    out int suggestedUseScore))
            {
                return;
            }

            candidates.Add(
                new FireDrawingTextCandidate
                {
                    CategoryCode = categoryCode,
                    CategoryName = categoryName,
                    Content = text,
                    NormalizedValue = normalizedValue,
                    LayerName =
                        string.IsNullOrWhiteSpace(layerName)
                            ? "0"
                            : layerName,
                    SourceType = sourceType,
                    SuggestedUseTag = suggestedUseTag,
                    SuggestedUseScore = suggestedUseScore
                });
        }

        private void ApplyFireDrawingTextCandidates(
            List<FireDrawingTextCandidate> candidates,
            int rawTextCount)
        {
            ClearFireDrawingReaderResults();
            _fireLastRawTextCount = rawTextCount;

            var unique =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (FireDrawingTextCandidate candidate in candidates)
            {
                string key =
                    candidate.CategoryCode + "|" +
                    candidate.NormalizedValue + "|" +
                    candidate.LayerName;

                if (!unique.Add(key))
                    continue;

                _fireDrawingTextRows.Add(
                    new FireDrawingTextRow
                    {
                        Index = _fireDrawingTextRows.Count + 1,
                        CategoryCode = candidate.CategoryCode,
                        CategoryName = candidate.CategoryName,
                        Content = candidate.Content,
                        NormalizedValue = candidate.NormalizedValue,
                        LayerName = candidate.LayerName,
                        SourceType = candidate.SourceType
                    });

                if (candidate.CategoryCode == "FLOOR")
                    _fireDetectedFloors.Add(candidate.NormalizedValue);

                if (candidate.CategoryCode == "ROOM")
                    _fireDetectedRooms.Add(candidate.NormalizedValue);

                if (!string.IsNullOrWhiteSpace(candidate.SuggestedUseTag))
                {
                    _fireUseScores.TryGetValue(
                        candidate.SuggestedUseTag,
                        out int currentScore);

                    _fireUseScores[candidate.SuggestedUseTag] =
                        currentScore +
                        Math.Max(1, candidate.SuggestedUseScore);
                }
            }

            _fireSuggestedUseTag =
                _fireUseScores
                    .OrderByDescending(x => x.Value)
                    .ThenBy(x => x.Key)
                    .Select(x => x.Key)
                    .FirstOrDefault() ?? string.Empty;

            DgFireDrawingTexts.Items.Refresh();
            UpdateFireDrawingReaderSummary();
        }

        private void ClearFireDrawingReaderResults()
        {
            _fireDrawingTextRows.Clear();
            _fireDetectedFloors.Clear();
            _fireDetectedRooms.Clear();
            _fireUseScores.Clear();
            _fireSuggestedUseTag = string.Empty;
            _fireLastRawTextCount = 0;
            UpdateFireDrawingReaderSummary();
        }

        private void UpdateFireDrawingReaderSummary()
        {
            if (TxtFireTextScanSummary == null ||
                TxtFireDetectedFloors == null ||
                TxtFireDetectedRooms == null ||
                TxtFireSuggestedUse == null)
            {
                return;
            }

            TxtFireTextScanSummary.Text =
                _fireLastRawTextCount <= 0
                    ? "Chưa quét thông tin bản vẽ."
                    : "Đã đọc " + _fireLastRawTextCount +
                      " chuỗi; nhận " + _fireDrawingTextRows.Count +
                      " thông tin liên quan.";

            TxtFireDetectedFloors.Text =
                _fireDetectedFloors.Count == 0
                    ? "Tầng nhận được: —"
                    : "Tầng nhận được (" + _fireDetectedFloors.Count + "): " +
                      JoinLimitedFireDrawingValues(
                          _fireDetectedFloors,
                          maxItems: 6);

            TxtFireDetectedRooms.Text =
                _fireDetectedRooms.Count == 0
                    ? "Phòng/khu vực: —"
                    : "Phòng/khu vực (" + _fireDetectedRooms.Count + "): " +
                      JoinLimitedFireDrawingValues(
                          _fireDetectedRooms,
                          maxItems: 8);

            TxtFireSuggestedUse.Text =
                string.IsNullOrWhiteSpace(_fireSuggestedUseTag)
                    ? "Công năng gợi ý: —"
                    : "Công năng gợi ý: " +
                      GetFireProjectUseDisplayName(
                          _fireSuggestedUseTag);
        }

        private static bool TryClassifyFireDrawingText(
            string text,
            out string categoryCode,
            out string categoryName,
            out string normalizedValue,
            out string suggestedUseTag,
            out int suggestedUseScore)
        {
            categoryCode = string.Empty;
            categoryName = string.Empty;
            normalizedValue = string.Empty;
            suggestedUseTag = string.Empty;
            suggestedUseScore = 0;

            string normalized =
                NormalizeFireDrawingTextForMatch(text);

            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            suggestedUseTag =
                DetectFireProjectUseTag(normalized);

            Match floorMatch = Regex.Match(
                normalized,
                @"\b(?:MAT\s+BANG\s+)?(?:TANG|LAU)\s+(?<F>HAM\s*(?:B?\d+)?|TRET|MAI|KY\s*THUAT|LUNG|\d{1,2})\b",
                RegexOptions.IgnoreCase);

            if (!floorMatch.Success)
            {
                floorMatch = Regex.Match(
                    normalized,
                    @"\b(?<F>HAM\s+B?\d+|SAN\s+MAI)\b",
                    RegexOptions.IgnoreCase);
            }

            if (floorMatch.Success)
            {
                categoryCode = "FLOOR";
                categoryName = "Tầng";
                normalizedValue =
                    FormatFireFloorName(
                        floorMatch.Groups["F"].Value);
                suggestedUseScore =
                    string.IsNullOrWhiteSpace(suggestedUseTag) ? 0 : 1;
                return true;
            }

            bool isHeightOrElevation =
                Regex.IsMatch(
                    normalized,
                    @"\b(CHIEU\s+CAO|CAO\s+DO|COT|COTE|FFL)\b") ||
                Regex.IsMatch(
                    normalized,
                    @"\bH\s*=\s*[+\-]?\d+(?:[\.,]\d+)?\s*(?:M|MM)?\b") ||
                Regex.IsMatch(
                    normalized,
                    @"^[+\-]?\d+[\.,]\d{3}\s*$");

            if (isHeightOrElevation)
            {
                categoryCode = "LEVEL";
                categoryName = "Cao độ";
                normalizedValue = normalized;
                suggestedUseScore =
                    string.IsNullOrWhiteSpace(suggestedUseTag) ? 0 : 1;
                return true;
            }

            foreach (string[] keyword in FireRoomKeywords)
            {
                if (!ContainsFireKeyword(normalized, keyword[0]))
                    continue;

                categoryCode = "ROOM";
                categoryName = "Phòng/KV";
                normalizedValue =
                    string.IsNullOrWhiteSpace(text)
                        ? keyword[1]
                        : LimitFireDrawingText(text, 80);
                suggestedUseScore =
                    string.IsNullOrWhiteSpace(suggestedUseTag) ? 0 : 1;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(suggestedUseTag))
            {
                categoryCode = "USE";
                categoryName = "Công năng";
                normalizedValue =
                    GetFireProjectUseDisplayName(suggestedUseTag);
                suggestedUseScore = 3;
                return true;
            }

            return false;
        }

        private static string DetectFireProjectUseTag(string normalized)
        {
            if (ContainsFireKeyword(normalized, "NHA XUONG") ||
                ContainsFireKeyword(normalized, "XUONG") ||
                ContainsFireKeyword(normalized, "SAN XUAT") ||
                ContainsFireKeyword(normalized, "GIA CONG"))
            {
                return "FACTORY";
            }

            if (ContainsFireKeyword(normalized, "NHA KHO") ||
                ContainsFireKeyword(normalized, "KHO") ||
                ContainsFireKeyword(normalized, "STORAGE"))
            {
                return "WAREHOUSE";
            }

            if (ContainsFireKeyword(normalized, "CHUNG CU") ||
                ContainsFireKeyword(normalized, "CAN HO"))
            {
                return "APARTMENT";
            }

            if (ContainsFireKeyword(normalized, "KHACH SAN") ||
                ContainsFireKeyword(normalized, "HOTEL") ||
                ContainsFireKeyword(normalized, "PHONG NGU"))
            {
                return "HOTEL";
            }

            if (ContainsFireKeyword(normalized, "BENH VIEN") ||
                ContainsFireKeyword(normalized, "PHONG KHAM"))
            {
                return "HOSPITAL";
            }

            if (ContainsFireKeyword(normalized, "TRUONG HOC") ||
                ContainsFireKeyword(normalized, "LOP HOC") ||
                ContainsFireKeyword(normalized, "PHONG HOC"))
            {
                return "SCHOOL";
            }

            if (ContainsFireKeyword(normalized, "THUONG MAI") ||
                ContainsFireKeyword(normalized, "SIEU THI") ||
                ContainsFireKeyword(normalized, "SHOP") ||
                ContainsFireKeyword(normalized, "CUA HANG"))
            {
                return "COMMERCIAL";
            }

            if (ContainsFireKeyword(normalized, "VAN PHONG") ||
                ContainsFireKeyword(normalized, "OFFICE"))
            {
                return "OFFICE";
            }

            if (ContainsFireKeyword(normalized, "HON HOP") ||
                ContainsFireKeyword(normalized, "MIXED USE"))
            {
                return "MIXED";
            }

            return string.Empty;
        }

        private static string GetFireProjectUseDisplayName(string tag)
        {
            switch ((tag ?? string.Empty).ToUpperInvariant())
            {
                case "FACTORY": return "Nhà xưởng";
                case "WAREHOUSE": return "Nhà kho";
                case "OFFICE": return "Văn phòng";
                case "APARTMENT": return "Chung cư";
                case "COMMERCIAL": return "Thương mại / dịch vụ";
                case "HOTEL": return "Khách sạn";
                case "HOSPITAL": return "Bệnh viện";
                case "SCHOOL": return "Trường học";
                case "MIXED": return "Công năng hỗn hợp";
                default: return "Khác";
            }
        }

        private static string FormatFireFloorName(string value)
        {
            string normalized =
                NormalizeFireDrawingTextForMatch(value);

            if (Regex.IsMatch(normalized, @"^\d{1,2}$"))
                return "Tầng " + normalized;

            if (normalized.StartsWith("HAM", StringComparison.Ordinal))
            {
                string number =
                    Regex.Match(normalized, @"\d+").Value;

                return string.IsNullOrWhiteSpace(number)
                    ? "Tầng hầm"
                    : "Hầm " + number;
            }

            if (normalized.Contains("TRET")) return "Tầng trệt";
            if (normalized.Contains("MAI")) return "Tầng mái";
            if (normalized.Contains("KY THUAT")) return "Tầng kỹ thuật";
            if (normalized.Contains("LUNG")) return "Tầng lửng";

            return LimitFireDrawingText(value, 50);
        }

        private static string CleanFireDrawingSourceText(string source)
        {
            string text = source ?? string.Empty;
            text = text.Replace("\\P", " ");
            text = Regex.Replace(text, @"\\[A-Za-z][^;]*;", " ");
            text = Regex.Replace(text, @"[{}]", " ");
            text = text.Replace("\r", " ").Replace("\n", " ");
            text = Regex.Replace(text, @"\s+", " ").Trim();
            return LimitFireDrawingText(text, 180);
        }

        private static string NormalizeFireDrawingTextForMatch(string source)
        {
            string text =
                CleanFireDrawingSourceText(source)
                    .Normalize(NormalizationForm.FormD);

            var builder = new StringBuilder(text.Length);

            foreach (char character in text)
            {
                UnicodeCategory category =
                    CharUnicodeInfo.GetUnicodeCategory(character);

                if (category != UnicodeCategory.NonSpacingMark)
                    builder.Append(character);
            }

            string normalized =
                builder
                    .ToString()
                    .Normalize(NormalizationForm.FormC)
                    .Replace('Đ', 'D')
                    .Replace('đ', 'd')
                    .ToUpperInvariant();

            normalized = Regex.Replace(
                normalized,
                @"[^A-Z0-9+\-=\.,/ ]+",
                " ");

            return Regex.Replace(
                    normalized,
                    @"\s+",
                    " ")
                .Trim();
        }

        private static bool ContainsFireKeyword(
            string normalizedSource,
            string normalizedKeyword)
        {
            string source = " " + normalizedSource + " ";
            string keyword = " " + normalizedKeyword + " ";

            return source.IndexOf(
                    keyword,
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ResolveFireDrawingTextLayer(
            string entityLayer,
            string inheritedLayer)
        {
            if (!string.IsNullOrWhiteSpace(entityLayer) &&
                !string.Equals(
                    entityLayer,
                    "0",
                    StringComparison.OrdinalIgnoreCase))
            {
                return entityLayer;
            }

            return string.IsNullOrWhiteSpace(inheritedLayer)
                ? "0"
                : inheritedLayer;
        }

        private static string LimitFireDrawingText(
            string value,
            int maximumLength)
        {
            string text = value ?? string.Empty;

            if (text.Length <= maximumLength)
                return text;

            return text.Substring(0, maximumLength - 1) + "…";
        }

        private static string JoinLimitedFireDrawingValues(
            IEnumerable<string> values,
            int maxItems)
        {
            List<string> items =
                values
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .OrderBy(x => x)
                    .ToList();

            string joined =
                string.Join(
                    ", ",
                    items.Take(maxItems));

            if (items.Count > maxItems)
                joined += ", …";

            return joined;
        }
    }

    internal sealed class FireDrawingTextCandidate
    {
        public string CategoryCode { get; set; }
        public string CategoryName { get; set; }
        public string Content { get; set; }
        public string NormalizedValue { get; set; }
        public string LayerName { get; set; }
        public string SourceType { get; set; }
        public string SuggestedUseTag { get; set; }
        public int SuggestedUseScore { get; set; }
    }

    internal sealed class FireDrawingTextRow
    {
        public int Index { get; set; }
        public string CategoryCode { get; set; }
        public string CategoryName { get; set; }
        public string Content { get; set; }
        public string NormalizedValue { get; set; }
        public string LayerName { get; set; }
        public string SourceType { get; set; }
    }
}
