using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

namespace AutoBocTachCAD
{
    public class BocTachCommand
    {
        // Tên lệnh gõ trong CAD sẽ là: DEMTHIETBI
        [CommandMethod("DEMTHIETBI")]
        public void DemVaPhanLoaiThietBi()
        {
            // Kết nối với bản vẽ CAD hiện tại
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // 1. Tạo bộ lọc: Chỉ cho phép quét chọn Block (Thiết bị)
            TypedValue[] tvs = new TypedValue[] { new TypedValue((int)DxfCode.Start, "INSERT") };
            SelectionFilter filter = new SelectionFilter(tvs);

            // 2. Yêu cầu người dùng quét vùng bản vẽ
            PromptSelectionResult psr = ed.GetSelection(filter);
            if (psr.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nBạn đã hủy lệnh quét.");
                return;
            }

            // 3. Khai báo từ điển để lưu Tên thiết bị và Số lượng
            Dictionary<string, int> danhSachThietBi = new Dictionary<string, int>();

            // Bắt đầu đọc dữ liệu bản vẽ
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject so in psr.Value)
                {
                    // Lấy đối tượng Block ra
                    BlockReference blkRef = (BlockReference)tr.GetObject(so.ObjectId, OpenMode.ForRead);
                    string tenThietBi = blkRef.Name;

                    // Xử lý trường hợp Block động (Dynamic Block) để lấy đúng tên gốc
                    if (blkRef.IsDynamicBlock)
                    {
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(blkRef.DynamicBlockTableRecord, OpenMode.ForRead);
                        tenThietBi = btr.Name;
                    }

                    // Đếm cộng dồn
                    if (danhSachThietBi.ContainsKey(tenThietBi))
                        danhSachThietBi[tenThietBi]++;
                    else
                        danhSachThietBi[tenThietBi] = 1;
                }
                tr.Commit();
            }

            // 4. In kết quả tổng hợp ra màn hình CAD
            ed.WriteMessage("\n\n--- KẾT QUẢ BỐC TÁCH THIẾT BỊ ---");
            // Sắp xếp theo tên A-Z cho đẹp
            foreach (var item in danhSachThietBi.OrderBy(x => x.Key))
            {
                ed.WriteMessage($"\n + {item.Key} : {item.Value} bộ");
            }
            ed.WriteMessage("\n----------------------------------\n");
        }
    }
}