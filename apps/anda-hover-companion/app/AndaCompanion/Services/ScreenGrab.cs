using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AndaCompanion.Services;

/// <summary>
/// Saves what is really on the primary screen as a PNG (GDI BitBlt). Diagnostic only: it lets the
/// self-test prove a window genuinely appeared on a real Windows desktop, with the real compositor.
/// </summary>
internal static class ScreenGrab
{
    public static bool TrySave(string path, out string detail)
    {
        detail = "";
        if (!OperatingSystem.IsWindows()) { detail = "not Windows"; return false; }
        IntPtr screenDc = IntPtr.Zero, memDc = IntPtr.Zero, bmp = IntPtr.Zero;
        try
        {
            int w = GetSystemMetrics(0), h = GetSystemMetrics(1);
            if (w <= 0 || h <= 0) { detail = "no screen metrics"; return false; }

            screenDc = GetDC(IntPtr.Zero);
            memDc = CreateCompatibleDC(screenDc);
            bmp = CreateCompatibleBitmap(screenDc, w, h);
            IntPtr old = SelectObject(memDc, bmp);
            bool ok = BitBlt(memDc, 0, 0, w, h, screenDc, 0, 0, 0x00CC0020 /*SRCCOPY*/ | 0x40000000 /*CAPTUREBLT*/);
            SelectObject(memDc, old);
            if (!ok) { detail = "BitBlt failed"; return false; }

            var info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(), Width = w, Height = -h, // negative = top-down
                    Planes = 1, BitCount = 32, Compression = 0,
                },
            };
            var pixels = new byte[w * h * 4];
            if (GetDIBits(memDc, bmp, 0, (uint)h, pixels, ref info, 0) == 0) { detail = "GetDIBits failed"; return false; }
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;   // GDI leaves alpha undefined

            using var image = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (var fb = image.Lock())
                for (int y = 0; y < h; y++)
                    Marshal.Copy(pixels, y * w * 4, fb.Address + y * fb.RowBytes, w * 4);
            image.Save(path);
            detail = $"{w}x{h}";
            return true;
        }
        catch (Exception ex) { detail = ex.Message; return false; }
        finally
        {
            if (bmp != IntPtr.Zero) DeleteObject(bmp);
            if (memDc != IntPtr.Zero) DeleteDC(memDc);
            if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount;
        public uint Compression, SizeImage; public int XPelsPerMeter, YPelsPerMeter; public uint ClrUsed, ClrImportant;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo { public BitmapInfoHeader Header; public uint Colors; }

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, byte[] bits, ref BitmapInfo info, uint usage);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}
