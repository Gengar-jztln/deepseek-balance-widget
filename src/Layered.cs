using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DeepSeekPet
{
    /// <summary>
    /// 分层窗口（WS_EX_LAYERED + UpdateLayeredWindow）绘制支持：
    /// 提供逐像素 Alpha 的透明窗口，透明区域自动鼠标穿透。
    /// </summary>
    internal sealed class LayeredSurface : IDisposable
    {
        private IntPtr _memoryDc = IntPtr.Zero;
        private IntPtr _dib = IntPtr.Zero;
        private IntPtr _oldDib = IntPtr.Zero;
        private IntPtr _bits = IntPtr.Zero;
        private int _width;
        private int _height;
        private byte[] _buffer = new byte[0];

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int CX; public int CY; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RGBQUAD { public byte Blue; public byte Green; public byte Red; public byte Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public BITMAPINFOHEADER Header;
            public RGBQUAD Colors;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr obj);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteDC(IntPtr hdc);

        private const int ULW_ALPHA = 0x00000002;
        private const byte AC_SRC_OVER = 0x00;
        private const byte AC_SRC_ALPHA = 0x01;

        /// <summary>把一张 32bpp ARGB 位图作为整个窗口内容提交到屏幕。</summary>
        public void Present(IntPtr hwnd, Bitmap bitmap, int screenX, int screenY)
        {
            EnsureSize(bitmap.Width, bitmap.Height);

            // GDI+ 的 32bpp ARGB 位图使用直通 Alpha，UpdateLayeredWindow 需要预乘 Alpha。
            BitmapData data = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int count = data.Stride * bitmap.Height;
                if (_buffer.Length < count) _buffer = new byte[count];
                Marshal.Copy(data.Scan0, _buffer, 0, count);
                for (int i = 0; i + 3 < count; i += 4)
                {
                    byte a = _buffer[i + 3];
                    if (a == 255) continue;
                    if (a == 0)
                    {
                        _buffer[i] = 0; _buffer[i + 1] = 0; _buffer[i + 2] = 0;
                        continue;
                    }
                    _buffer[i] = (byte)(_buffer[i] * a / 255);
                    _buffer[i + 1] = (byte)(_buffer[i + 1] * a / 255);
                    _buffer[i + 2] = (byte)(_buffer[i + 2] * a / 255);
                }
                Marshal.Copy(_buffer, 0, _bits, count);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            POINT dst = new POINT();
            dst.X = screenX;
            dst.Y = screenY;
            SIZE size = new SIZE();
            size.CX = bitmap.Width;
            size.CY = bitmap.Height;
            POINT src = new POINT();
            src.X = 0;
            src.Y = 0;
            BLENDFUNCTION blend = new BLENDFUNCTION();
            blend.BlendOp = AC_SRC_OVER;
            blend.BlendFlags = 0;
            blend.SourceConstantAlpha = 255;
            blend.AlphaFormat = AC_SRC_ALPHA;

            UpdateLayeredWindow(hwnd, IntPtr.Zero, ref dst, ref size, _memoryDc, ref src, 0, ref blend, ULW_ALPHA);
        }

        private void EnsureSize(int width, int height)
        {
            if (width == _width && height == _height && _memoryDc != IntPtr.Zero) return;
            Release();

            _width = width;
            _height = height;

            _memoryDc = CreateCompatibleDC(IntPtr.Zero);
            BITMAPINFO info = new BITMAPINFO();
            info.Header.biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER));
            info.Header.biWidth = width;
            info.Header.biHeight = -height;      // 负值：自上而下
            info.Header.biPlanes = 1;
            info.Header.biBitCount = 32;
            info.Header.biCompression = 0;       // BI_RGB
            _dib = CreateDIBSection(_memoryDc, ref info, 0, out _bits, IntPtr.Zero, 0);
            if (_dib != IntPtr.Zero) _oldDib = SelectObject(_memoryDc, _dib);
        }

        private void Release()
        {
            if (_memoryDc != IntPtr.Zero && _oldDib != IntPtr.Zero)
            {
                SelectObject(_memoryDc, _oldDib);
                _oldDib = IntPtr.Zero;
            }
            if (_dib != IntPtr.Zero)
            {
                DeleteObject(_dib);
                _dib = IntPtr.Zero;
            }
            if (_memoryDc != IntPtr.Zero)
            {
                DeleteDC(_memoryDc);
                _memoryDc = IntPtr.Zero;
            }
            _bits = IntPtr.Zero;
            _width = 0;
            _height = 0;
        }

        public void Dispose()
        {
            Release();
            GC.SuppressFinalize(this);
        }
    }
}
