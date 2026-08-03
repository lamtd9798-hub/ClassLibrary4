#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.ApplicationServices;

namespace ClassLibrary4
{
    public partial class BOCTACHUI : UserControl
    {
        private Dictionary<string, string> tuDienDoiChieu = new Dictionary<string, string>();
        private Dictionary<ObjectId, Autodesk.AutoCAD.Colors.Color> mauGocCuaBlock = new Dictionary<ObjectId, Autodesk.AutoCAD.Colors.Color>();

        // Các biến phục vụ cho tính năng rải Text tự động
        private bool _isWaitingForPline = false;
        private ObjectId _lastPlineId = ObjectId.Null;
        private string _currentLayerNameForText = "";
        private double _currentPlineWidth = 0;

        public BOCTACHUI()
        {
            InitializeComponent();
            CmbHeThong.SelectedIndex = 0;
        }

        // =========================================================
        // THUẬT TOÁN SẮP XẾP SIZE THÔNG MINH
        // =========================================================
        private void CapNhatVaSapXepDanhSachSize(List<string> rawSizes, string itemToSelect = null)
        {
            var sortedSizes = rawSizes.OrderBy(s =>
            {
                var matches = Regex.Matches(s, @"\d+(\.\d+)?");
                return matches.Count > 0 ? double.Parse(matches[0].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
            }).ThenBy(s =>
            {
                var matches = Regex.Matches(s, @"\d+(\.\d+)?");
                return matches.Count > 1 ? double.Parse(matches[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
            }).ToList();

            LstSizeOng.Items.Clear();
            foreach (var s in sortedSizes) LstSizeOng.Items.Add(s);

            if (itemToSelect != null && LstSizeOng.Items.Contains(itemToSelect))
            {
                LstSizeOng.SelectedItem = itemToSelect;
                LstSizeOng.ScrollIntoView(itemToSelect);
            }
            else if (LstSizeOng.Items.Count > 0) LstSizeOng.SelectedIndex = 0;
        }

        // =========================================================
        // ĐỔI HỆ THỐNG / VẬT LIỆU
        // =========================================================
        private void CmbHeThong_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbVatLieuOng == null || PnlApSuat == null) return;
            string system = (CmbHeThong.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "";
            CmbVatLieuOng.Items.Clear();

            if (system.Contains("HVAC") || system.Contains("SM") || system.Contains("TG"))
            {
                PnlApSuat.Visibility = System.Windows.Visibility.Hidden;
                string[] ductTypes = {
                    "Ống gió hút khói _ SEAF", "Ống gió tươi _ FAF", "Ống gió thải _ EAF",
                    "Ống gió tạo áp _ PAF", "Ống gió bếp _ BEP", "Ống gió lạnh _ CN13",
                    "Ống gió lạnh _ CN20", "Ống gió lạnh _ CN32", "Ống gió lạnh _ CN50"
                };
                foreach (var type in ductTypes) CmbVatLieuOng.Items.Add(new ComboBoxItem { Content = type });
            }
            else
            {
                PnlApSuat.Visibility = System.Windows.Visibility.Visible;
                string[] pipeTypes = {
                    "Ống thép mạ kẽm _ GI", "Ống thép đen _ TĐ", "Ống thép nhúng nóng _ NN",
                    "Ống nhựa HDPE _ HDPE", "Ống PPR _ PPR", "Ống uPVC _ UPVC",
                    "Ống đồng _ CU", "Ống PVC _ PVC", "Ống HDPE xoắn _ HDPEX"
                };
                foreach (var type in pipeTypes) CmbVatLieuOng.Items.Add(new ComboBoxItem { Content = type });
            }

            if (CmbVatLieuOng.Items.Count > 0) CmbVatLieuOng.SelectedIndex = 0;
        }

        private void CmbVatLieuOng_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstSizeOng == null) return;
            List<string> newSizes = new List<string>();

            if (CmbVatLieuOng.SelectedItem is ComboBoxItem selectedItem)
            {
                string material = selectedItem.Content.ToString() ?? "";

                if (material.Contains("Ống gió"))
                {
                    string[] ductSizes = {
                        "150x100", "200x150", "200x200", "250x250",
                        "300x200", "300x300", "400x200", "400x300", "400x400",
                        "500x300", "500x500", "600x300", "600x400", "800x400"
                    };
                    newSizes.AddRange(ductSizes);
                }
                else if (material.Contains("_ CU"))
                {
                    string[] copperSizes = {
                        "6.4 - 9.5", "6.4 - 12.7", "6.4 - 15.9", "9.5 - 12.7", "9.5 - 15.9",
                        "9.5 - 19.1", "9.5 - 22.2", "12.7 - 19.1", "12.7 - 22.2", "12.7 - 25.4",
                        "12.7 - 28.6", "15.9 - 28.6", "15.9 - 31.8", "15.9 - 34.9", "15.9 - 38.1",
                        "19.1 - 31.8", "19.1 - 34.9", "19.1 - 38.1", "19.1 - 41.3", "22.2 - 34.9",
                        "22.2 - 38.1", "22.2 - 41.3", "22.2 - 44.5", "22.2 - 54.0", "25.4 - 54.0"
                    };
                    newSizes.AddRange(copperSizes);
                }
                else if (material.Contains("_ HDPEX"))
                {
                    string[] hdpeXoanSizes = {
                        "D 25/32", "D 30/40", "D 40/50", "D 50/65", "D 65/85",
                        "D 70/90", "D 80/105", "D 90/110", "D 100/130", "D 125/160"
                    };
                    newSizes.AddRange(hdpeXoanSizes);
                }
                else
                {
                    string[] standardSizes = {
                        "DN15", "DN20", "DN25", "DN32", "DN40", "DN50",
                        "DN65", "DN80", "DN100", "DN125", "DN150", "DN200",
                        "DN250", "DN300", "DN350", "DN400"
                    };
                    newSizes.AddRange(standardSizes);
                }
            }
            CapNhatVaSapXepDanhSachSize(newSizes);
        }

        private void TxtCustomSize_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                string newSize = TxtCustomSize.Text.Trim();
                if (!string.IsNullOrEmpty(newSize))
                {
                    List<string> currentSizes = LstSizeOng.Items.Cast<string>().ToList();
                    if (!currentSizes.Contains(newSize)) currentSizes.Add(newSize);

                    CapNhatVaSapXepDanhSachSize(currentSizes, newSize);
                    TxtCustomSize.Text = "";
                }
            }
        }

        private void LstSizeOng_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && LstSizeOng.SelectedItem != null)
                LstSizeOng.Items.Remove(LstSizeOng.SelectedItem);
        }

        // =========================================================
        // SỰ KIỆN NÚT VẼ ỐNG
        // =========================================================
        private void BtnVeOng_Click(object sender, RoutedEventArgs e)
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;

            string sys = (CmbHeThong.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "";
            string mat = (CmbVatLieuOng.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "";
            string size = LstSizeOng.SelectedItem?.ToString() ?? "";

            if (string.IsNullOrEmpty(size))
            {
                MessageBox.Show("Vui lòng chọn Size ống trước khi vẽ!", "Cảnh báo");
                return;
            }

            double plineWidth = 0;
            var matches = Regex.Matches(size, @"\d+(\.\d+)?");
            if (matches.Count > 0)
            {
                plineWidth = matches.Cast<Match>().Max(m => double.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture));
            }

            string GetKyHieu(string input)
            {
                if (input.Contains("_")) return input.Split('_').Last().Trim();
                return input.Trim();
            }

            string layerName = $"{GetKyHieu(sys)}_{GetKyHieu(mat)}_{size}";

            if (PnlApSuat.Visibility == System.Windows.Visibility.Visible)
            {
                if (ChkApSuat.IsChecked == true)
                {
                    string apSuat = (CmbApSuat.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "";
                    layerName += $"_{GetKyHieu(apSuat)}";
                }
                if (ChkNuocThai.IsChecked == true)
                {
                    string nuocThai = (CmbNuocThai.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "";
                    layerName += $"_{GetKyHieu(nuocThai)}";
                }
            }

            layerName = layerName.Replace(" ", "").Replace("/", "-");
            bool isOngGio = sys.Contains("HVAC") || sys.Contains("SM") || sys.Contains("TG");

            using (doc.LockDocument())
            {
                Autodesk.AutoCAD.ApplicationServices.Application.SetSystemVariable("PLINEWID", plineWidth);

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    LayerTableRecord ltr;

                    if (!lt.Has(layerName))
                    {
                        lt.UpgradeOpen();
                        ltr = new LayerTableRecord();
                        ltr.Name = layerName;

                        int hash = Math.Abs(layerName.GetHashCode());
                        short colorIndex = (short)((hash % 254) + 1);
                        if (colorIndex == 0 || colorIndex == 7 || colorIndex == 8) colorIndex = 3;
                        ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, colorIndex);

                        lt.Add(ltr);
                        tr.AddNewlyCreatedDBObject(ltr, true);
                    }
                    else
                    {
                        ltr = (LayerTableRecord)tr.GetObject(lt[layerName], OpenMode.ForWrite);
                    }

                    ltr.UpgradeOpen();
                    if (isOngGio) ltr.Transparency = new Autodesk.AutoCAD.Colors.Transparency(102);
                    else ltr.Transparency = new Autodesk.AutoCAD.Colors.Transparency(255);

                    db.Clayer = lt[layerName];
                    tr.Commit();
                }
            }

            // Ghi nhớ thông tin để chuẩn bị rải Text sau khi vẽ xong
            _currentLayerNameForText = layerName;
            _currentPlineWidth = plineWidth;
            _lastPlineId = ObjectId.Null;

            // Bắt đầu cài máy theo dõi (Hook Events)
            if (!_isWaitingForPline)
            {
                doc.Database.ObjectAppended += Db_ObjectAppended;
                doc.CommandEnded += Doc_CommandEnded;
                doc.CommandCancelled += Doc_CommandCancelled;
                _isWaitingForPline = true;
            }

            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            doc.SendStringToExecute("._PLINE ", true, false, false);
        }

        // =========================================================
        // HỆ THỐNG THEO DÕI VÀ RẢI TEXT TỰ ĐỘNG LÊN MẶT ỐNG
        // =========================================================

        // Đã sửa lại tham số e thành kiểu ObjectEventArgs cho đúng thư viện CAD
        private void Db_ObjectAppended(object sender, ObjectEventArgs e)
        {
            if (_isWaitingForPline && e.DBObject is Polyline)
            {
                _lastPlineId = e.DBObject.Id;
            }
        }

        private void Doc_CommandEnded(object sender, CommandEventArgs e)
        {
            if (_isWaitingForPline && e.GlobalCommandName.ToUpper() == "PLINE")
            {
                CleanupEvents((Document)sender);
                if (_lastPlineId != ObjectId.Null && !_lastPlineId.IsErased)
                    AddTextToPolyline(_lastPlineId, _currentLayerNameForText, _currentPlineWidth);
            }
        }

        private void Doc_CommandCancelled(object sender, CommandEventArgs e)
        {
            if (_isWaitingForPline && e.GlobalCommandName.ToUpper() == "PLINE")
            {
                CleanupEvents((Document)sender);
                if (_lastPlineId != ObjectId.Null && !_lastPlineId.IsErased)
                    AddTextToPolyline(_lastPlineId, _currentLayerNameForText, _currentPlineWidth);
            }
        }

        private void CleanupEvents(Document doc)
        {
            _isWaitingForPline = false;
            doc.Database.ObjectAppended -= Db_ObjectAppended;
            doc.CommandEnded -= Doc_CommandEnded;
            doc.CommandCancelled -= Doc_CommandCancelled;
        }

        private void AddTextToPolyline(ObjectId plineId, string layerName, double width)
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            using (doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Polyline pline = tr.GetObject(plineId, OpenMode.ForRead) as Polyline;
                    if (pline != null)
                    {
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                        // Độ cao chữ bằng 50% kích thước ống (Tối thiểu là 150 để không bị quá nhỏ)
                        double textHeight = Math.Max(width * 0.5, 150);

                        for (int i = 0; i < pline.NumberOfVertices - 1; i++)
                        {
                            if (pline.GetSegmentType(i) == SegmentType.Line)
                            {
                                LineSegment3d segment = pline.GetLineSegmentAt(i);

                                // CHỈ rải Text ở những đoạn ống dài hơn 1000mm
                                if (segment.Length >= 1000)
                                {
                                    Point3d midPt = segment.MidPoint;
                                    Vector3d dir = segment.Direction;
                                    double angle = dir.AngleOnPlane(new Plane());

                                    // Lật ngược chiều chữ nếu ống bị vẽ ngược từ Phải sang Trái, tránh việc thợ đọc bị đau cổ
                                    if (angle > Math.PI / 2 && angle <= 3 * Math.PI / 2)
                                    {
                                        angle -= Math.PI;
                                        dir = dir.Negate();
                                    }

                                    // Đẩy Text nổi lên trên mặt ống
                                    Vector3d upDir = dir.RotateBy(Math.PI / 2, Vector3d.ZAxis);
                                    double offsetDist = (pline.ConstantWidth > 0 ? pline.ConstantWidth / 2.0 : width / 2.0) + (textHeight * 0.2);
                                    Point3d textPt = midPt + upDir * offsetDist;

                                    DBText txt = new DBText();
                                    txt.SetDatabaseDefaults();
                                    txt.TextString = layerName;
                                    txt.Height = textHeight;
                                    txt.Layer = layerName;
                                    txt.Justify = AttachmentPoint.BottomCenter;
                                    txt.AlignmentPoint = textPt;
                                    txt.Rotation = angle;

                                    btr.AppendEntity(txt);
                                    tr.AddNewlyCreatedDBObject(txt, true);
                                }
                            }
                        }
                    }
                    tr.Commit();
                }
            }
        }

        // =========================================================
        // THUẬT TOÁN BỐC TÁCH KHỐI LƯỢNG VÀ THỐNG KÊ (GIỮ NGUYÊN)
        // =========================================================
        private string TuDongPhatHienVaSuaFont(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            char[] tcvn3Signs = { 'µ', '¸', '¶', '·', '¹', '©', '®', '«', '¬', '¢', '§', '£', '¤', '¥', '¦', 'Ç', 'ù' };
            if (input.IndexOfAny(tcvn3Signs) >= 0)
            {
                char[] tcvn3 = { 'µ', '¸', '¶', '·', '¹', '¨', '»', '¾', '¼', '½', 'Æ', '©', 'Ç', 'Ê', 'È', 'É', 'Ë', '®', 'Ì', 'Ð', 'Î', 'Ï', 'Ñ', 'ª', 'Ò', 'Õ', 'Ó', 'Ô', 'Ö', '×', 'Ý', 'Ø', 'Ü', 'Þ', 'ß', 'ã', 'á', 'â', 'ä', '«', 'å', 'è', 'æ', 'ç', 'é', '¬', 'ê', 'í', 'ë', 'ì', 'î', 'ï', 'ó', 'ñ', 'ò', 'ô', '­', 'õ', 'ø', 'ö', '÷', 'ù', 'ú', 'ý', 'û', 'ü', 'þ', '¡', '¢', '§', '£', '¤', '¥', '¦' };
                char[] unicode = { 'à', 'á', 'ả', 'ã', 'ạ', 'ă', 'ằ', 'ắ', 'ẳ', 'ẵ', 'ặ', 'â', 'ầ', 'ấ', 'ẩ', 'ẫ', 'ậ', 'đ', 'è', 'é', 'ẻ', 'ẽ', 'ẹ', 'ê', 'ề', 'ế', 'ể', 'ễ', 'ệ', 'ì', 'í', 'ỉ', 'ĩ', 'ị', 'ò', 'ó', 'ỏ', 'õ', 'ọ', 'ô', 'ồ', 'ố', 'ổ', 'ỗ', 'ộ', 'ơ', 'ờ', 'ớ', 'ở', 'ỡ', 'ợ', 'ù', 'ú', 'ủ', 'ũ', 'ụ', 'ư', 'ừ', 'ứ', 'ử', 'ữ', 'ự', 'ỳ', 'ý', 'ỷ', 'ỹ', 'ỵ', 'Ă', 'Â', 'Đ', 'Ê', 'Ô', 'Ơ', 'Ư' };
                string result = input;
                for (int i = 0; i < tcvn3.Length; i++) result = result.Replace(tcvn3[i], unicode[i]);
                return result;
            }
            return input;
        }

        private string TaoMaDinhDanhBlock(BlockReference blk, Transaction tr)
        {
            if (blk == null) return "";
            string blkName = blk.IsDynamicBlock ? ((BlockTableRecord)tr.GetObject(blk.DynamicBlockTableRecord, OpenMode.ForRead)).Name : blk.Name;
            string key = $"{blkName}_Color{blk.ColorIndex}";
            if (blk.IsDynamicBlock)
            {
                foreach (DynamicBlockReferenceProperty prop in blk.DynamicBlockReferencePropertyCollection)
                {
                    if (prop.PropertyName.StartsWith("Visibility") || prop.PropertyName.StartsWith("Trạng thái") || prop.PropertyName == "Visibility1")
                        key += $"_Vis{prop.Value}";
                }
            }
            return key;
        }

        private void BtnQuetBang_Click(object sender, RoutedEventArgs e)
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            using (doc.LockDocument())
            {
                TypedValue[] tvs = new TypedValue[] {
                    new TypedValue((int)DxfCode.Operator, "<OR"),
                    new TypedValue((int)DxfCode.Start, "INSERT"),
                    new TypedValue((int)DxfCode.Start, "TEXT"),
                    new TypedValue((int)DxfCode.Start, "MTEXT"),
                    new TypedValue((int)DxfCode.Operator, "OR>")
                };
                SelectionFilter filter = new SelectionFilter(tvs);

                PromptSelectionOptions pso = new PromptSelectionOptions();
                pso.MessageForAdding = "\n[Bước 1] Quét chọn BẢNG ĐỐI CHIẾU: ";
                PromptSelectionResult psr = ed.GetSelection(pso, filter);
                if (psr.Status != PromptStatus.OK) return;

                List<BlockReference> blocks = new List<BlockReference>();
                List<Entity> texts = new List<Entity>();

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (SelectedObject so in psr.Value)
                    {
                        Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                        if (ent is BlockReference blk) blocks.Add(blk);
                        else if (ent != null) texts.Add(ent);
                    }

                    tuDienDoiChieu.Clear();

                    foreach (var blk in blocks)
                    {
                        string blkKey = TaoMaDinhDanhBlock(blk, tr);
                        string matchedText = blk.Name;
                        double minDistance = double.MaxValue;

                        foreach (var txt in texts)
                        {
                            double txtY = 0, txtX = 0; string txtVal = "";
                            if (txt is DBText dbTxt) { txtY = dbTxt.Position.Y; txtX = dbTxt.Position.X; txtVal = dbTxt.TextString; }
                            else if (txt is MText mTxt) { txtY = mTxt.Location.Y; txtX = mTxt.Location.X; txtVal = mTxt.Text; }

                            double dist = Math.Sqrt(Math.Pow(blk.Position.X - txtX, 2) + Math.Pow(blk.Position.Y - txtY, 2));
                            if (dist < minDistance)
                            {
                                minDistance = dist;
                                string rawText = txtVal.Replace("\r", " ").Replace("\n", " ").Trim();
                                matchedText = TuDongPhatHienVaSuaFont(rawText);
                            }
                        }
                        tuDienDoiChieu[blkKey] = matchedText;
                    }
                    tr.Commit();
                }
                MessageBox.Show($"Hoàn tất! Đã nhận diện được {tuDienDoiChieu.Count} loại thiết bị.", "Thông Báo");
            }
        }

        private void BtnQuetMatBang_Click(object sender, RoutedEventArgs e)
        {
            if (tuDienDoiChieu.Count == 0)
            {
                MessageBox.Show("Bạn phải bấm Nút số 1 trước nhé!", "Cảnh Báo");
                return;
            }

            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            KhoiPhucMauGoc();
            mauGocCuaBlock.Clear();

            using (doc.LockDocument())
            {
                TypedValue[] tvs = new TypedValue[] { new TypedValue((int)DxfCode.Start, "INSERT") };
                SelectionFilter filter = new SelectionFilter(tvs);

                PromptSelectionOptions pso = new PromptSelectionOptions();
                pso.MessageForAdding = "\n[Bước 2] Quét chọn MẶT BẰNG cần bốc tách: ";
                PromptSelectionResult psr = doc.Editor.GetSelection(pso, filter);
                if (psr.Status != PromptStatus.OK) return;

                Dictionary<string, KetQuaBocTach> danhSachBocTach = new Dictionary<string, KetQuaBocTach>();

                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    foreach (SelectedObject so in psr.Value)
                    {
                        BlockReference blkRef = tr.GetObject(so.ObjectId, OpenMode.ForRead) as BlockReference;
                        if (blkRef == null) continue;

                        string blkKey = TaoMaDinhDanhBlock(blkRef, tr);
                        if (!tuDienDoiChieu.ContainsKey(blkKey)) continue;

                        string tenHienThi = tuDienDoiChieu[blkKey];
                        mauGocCuaBlock[blkRef.ObjectId] = blkRef.Color;

                        if (!danhSachBocTach.ContainsKey(tenHienThi))
                        {
                            danhSachBocTach[tenHienThi] = new KetQuaBocTach { TenHienThi = tenHienThi, SoLuong = 0 };
                        }
                        danhSachBocTach[tenHienThi].SoLuong++;
                        danhSachBocTach[tenHienThi].DanhSachId.Add(blkRef.ObjectId);
                    }
                    tr.Commit();
                }
                DgvKetQua.ItemsSource = danhSachBocTach.Values.OrderBy(x => x.TenHienThi).ToList();
            }
        }

        private void DgvKetQua_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null || mauGocCuaBlock.Count == 0) return;

            if (DgvKetQua.SelectedItem == null)
            {
                KhoiPhucMauGoc();
                return;
            }

            KetQuaBocTach selectedRow = DgvKetQua.SelectedItem as KetQuaBocTach;
            if (selectedRow == null) return;

            using (doc.LockDocument())
            {
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    foreach (var item in mauGocCuaBlock)
                    {
                        ObjectId objId = item.Key;
                        if (objId.IsErased) continue;

                        BlockReference blk = tr.GetObject(objId, OpenMode.ForWrite) as BlockReference;
                        if (blk != null)
                        {
                            blk.ColorIndex = selectedRow.DanhSachId.Contains(objId) ? 1 : 8;
                        }
                    }
                    tr.Commit();
                }
                doc.Editor.Regen();
            }
        }

        private void KhoiPhucMauGoc()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null || mauGocCuaBlock.Count == 0) return;

            using (doc.LockDocument())
            {
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    foreach (var item in mauGocCuaBlock)
                    {
                        ObjectId objId = item.Key;
                        if (objId.IsErased) continue;

                        BlockReference blk = tr.GetObject(objId, OpenMode.ForWrite) as BlockReference;
                        if (blk != null) blk.Color = item.Value;
                    }
                    tr.Commit();
                }
                doc.Editor.Regen();
            }
        }

        private void BtnKhoiPhuc_Click(object sender, RoutedEventArgs e)
        {
            KhoiPhucMauGoc();
            DgvKetQua.SelectedItem = null;
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            KhoiPhucMauGoc();
        }
    }

    public class KetQuaBocTach
    {
        public string TenHienThi { get; set; }
        public int SoLuong { get; set; }
        public List<ObjectId> DanhSachId { get; set; } = new List<ObjectId>();
    }
}