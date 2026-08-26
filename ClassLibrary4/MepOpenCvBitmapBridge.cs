#nullable disable
using System;
using System.Drawing;
using System.Drawing.Imaging;

using OpenCvSharp;

namespace ClassLibrary4
{
    /// <summary>
    /// Chuyển đổi Bitmap &lt;-&gt; Mat mà không phụ thuộc
    /// OpenCvSharp4.Extensions/System.Drawing.Common 10.x.
    ///
    /// AutoCAD 2025 chạy .NET 8 và đã có System.Drawing.Common 8 trong
    /// Windows Desktop Runtime. Giữ việc chuyển đổi ở đây giúp OpenCV 4.13
    /// chạy trong đúng AssemblyLoadContext của plugin, không ép AutoCAD nạp
    /// một framework assembly phiên bản 10.
    /// </summary>
    internal static class MepOpenCvBitmapBridge
    {
        public static Mat ToMat(Bitmap source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (source.Width <= 0 || source.Height <= 0)
                throw new ArgumentException("Bitmap không có kích thước hợp lệ.", nameof(source));

            Bitmap normalized = null;
            Bitmap working = source;

            try
            {
                int channels;
                PixelFormat lockFormat;

                switch (source.PixelFormat)
                {
                    case PixelFormat.Format24bppRgb:
                        channels = 3;
                        lockFormat = PixelFormat.Format24bppRgb;
                        break;

                    case PixelFormat.Format32bppArgb:
                    case PixelFormat.Format32bppRgb:
                        channels = 4;
                        lockFormat = source.PixelFormat;
                        break;

                    default:
                        // Indexed/PArgb/hi-bit-depth được chuẩn hóa trước để
                        // không đọc sai palette, alpha premultiplied hoặc stride.
                        normalized = CloneAs32BppArgb(source);
                        working = normalized;
                        channels = 4;
                        lockFormat = PixelFormat.Format32bppArgb;
                        break;
                }

                Mat result = new Mat(
                    working.Height,
                    working.Width,
                    MatType.CV_8UC(channels));

                BitmapData data = null;

                try
                {
                    data = working.LockBits(
                        new Rectangle(0, 0, working.Width, working.Height),
                        ImageLockMode.ReadOnly,
                        lockFormat);

                    CopyBitmapRowsToMat(data, result);
                    return result;
                }
                catch
                {
                    result.Dispose();
                    throw;
                }
                finally
                {
                    if (data != null)
                        working.UnlockBits(data);
                }
            }
            finally
            {
                normalized?.Dispose();
            }
        }

        public static Bitmap ToBitmap(Mat source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (source.IsDisposed)
                throw new ObjectDisposedException(nameof(source));

            if (source.Empty() || source.Width <= 0 || source.Height <= 0)
                throw new ArgumentException("Mat đang rỗng.", nameof(source));

            if (source.Dims != 2 || source.Depth() != MatType.CV_8U)
            {
                throw new NotSupportedException(
                    "Chỉ hỗ trợ Mat 2D có depth CV_8U.");
            }

            PixelFormat format;

            switch (source.Channels())
            {
                case 1:
                    format = PixelFormat.Format8bppIndexed;
                    break;
                case 3:
                    format = PixelFormat.Format24bppRgb;
                    break;
                case 4:
                    format = PixelFormat.Format32bppArgb;
                    break;
                default:
                    throw new NotSupportedException(
                        "Số channel OpenCV không hỗ trợ: " + source.Channels() + ".");
            }

            Bitmap result = new Bitmap(source.Width, source.Height, format);

            if (format == PixelFormat.Format8bppIndexed)
            {
                ColorPalette palette = result.Palette;

                for (int i = 0; i < palette.Entries.Length; i++)
                {
                    palette.Entries[i] = Color.FromArgb(i, i, i);
                }

                result.Palette = palette;
            }

            BitmapData data = null;

            try
            {
                data = result.LockBits(
                    new Rectangle(0, 0, result.Width, result.Height),
                    ImageLockMode.WriteOnly,
                    format);

                CopyMatRowsToBitmap(source, data);
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
            finally
            {
                if (data != null)
                    result.UnlockBits(data);
            }
        }

        private static Bitmap CloneAs32BppArgb(Bitmap source)
        {
            Bitmap result = new Bitmap(
                source.Width,
                source.Height,
                PixelFormat.Format32bppArgb);

            using (Graphics graphics = Graphics.FromImage(result))
            {
                graphics.Clear(Color.Transparent);
                graphics.DrawImageUnscaled(source, 0, 0);
            }

            return result;
        }

        private static unsafe void CopyBitmapRowsToMat(
            BitmapData source,
            Mat destination)
        {
            int rowBytes = checked(
                destination.Cols * (int)destination.ElemSize());

            long destinationStep = checked((long)destination.Step());

            if (Math.Abs((long)source.Stride) < rowBytes ||
                destinationStep < rowBytes)
            {
                throw new ArgumentException(
                    "Stride của Bitmap/Mat nhỏ hơn số byte một hàng.");
            }

            byte* sourceBase = (byte*)source.Scan0.ToPointer();
            byte* destinationBase = (byte*)destination.Data.ToPointer();

            for (int row = 0; row < destination.Rows; row++)
            {
                byte* sourceRow = sourceBase + row * source.Stride;
                byte* destinationRow = destinationBase + row * destinationStep;

                Buffer.MemoryCopy(
                    sourceRow,
                    destinationRow,
                    rowBytes,
                    rowBytes);
            }
        }

        private static unsafe void CopyMatRowsToBitmap(
            Mat source,
            BitmapData destination)
        {
            int rowBytes = checked(
                source.Cols * (int)source.ElemSize());

            long sourceStep = checked((long)source.Step());

            if (sourceStep < rowBytes ||
                Math.Abs((long)destination.Stride) < rowBytes)
            {
                throw new ArgumentException(
                    "Stride của Mat/Bitmap nhỏ hơn số byte một hàng.");
            }

            byte* sourceBase = (byte*)source.Data.ToPointer();
            byte* destinationBase = (byte*)destination.Scan0.ToPointer();

            for (int row = 0; row < source.Rows; row++)
            {
                byte* sourceRow = sourceBase + row * sourceStep;
                byte* destinationRow = destinationBase + row * destination.Stride;

                Buffer.MemoryCopy(
                    sourceRow,
                    destinationRow,
                    rowBytes,
                    rowBytes);
            }
        }
    }
}
