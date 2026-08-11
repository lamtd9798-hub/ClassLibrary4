using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;

namespace ClassLibrary4
{
    public class BocTachCommand
    {
        private static PaletteSet? ps = null;
        private static BOCTACHUI? ui = null;

        // Tên lệnh gọi bảng trong AutoCAD
        [CommandMethod("HIENBANG")]
        public void HienThiBang()
        {
            // LICENSE-ONLINE-20260811-02
            // Luôn kiểm tra trước khi tạo hoặc hiện Palette.
            // Nếu người dùng đóng cửa sổ kích hoạt hoặc key không hợp lệ,
            // kết thúc lệnh ngay và không cho mở bảng công cụ.
            if (!OnlineLicenseManager.EnsureActivated())
                return;

            if (ps == null)
            {
                // Khởi tạo khung Palette
                ps = new PaletteSet("Bốc Tách Khối Lượng");
                ps.Size = new System.Drawing.Size(320, 500);
                ps.Dock = DockSides.Left;

                // Gọi giao diện WPF. Constructor BOCTACHUI có lớp bảo vệ
                // thứ hai nhưng không gọi mạng lại trong cùng phiên CAD.
                ui = new BOCTACHUI();

                // Nhúng giao diện vào bảng Palette
                ps.AddVisual("Giao Diện Bốc Tách", ui);
            }

            // Chỉ chạy tới đây sau khi bản quyền hợp lệ.
            ps.Visible = true;
        }
    }
}