using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;

namespace ClassLibrary4
{
    public class BocTachCommand
    {
        static PaletteSet? ps = null;
        static BOCTACHUI? ui = null;

        // Tên lệnh gọi bảng trong AutoCAD
        [CommandMethod("HIENBANG")]
        public void HienThiBang()
        {
            if (ps == null)
            {
                // Khởi tạo khung Palette
                ps = new PaletteSet("Bốc Tách Khối Lượng");
                ps.Size = new System.Drawing.Size(320, 500);
                ps.Dock = DockSides.Left; // Neo bảng vào lề trái

                // Gọi giao diện WPF
                ui = new BOCTACHUI();

                // Nhúng giao diện vào bảng Palette
                ps.AddVisual("Giao Diện Bốc Tách", ui);
            }
            // Hiển thị Palette lên màn hình
            ps.Visible = true;
        }
    }
}