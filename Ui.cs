using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WordBackspace;

static class Ui
{
    public static readonly Font Font;
    public static readonly Font FontBold;
    public static readonly Font FontSmall;

    static Ui()
    {
        string family = "Segoe UI";
        try
        {
            if (Array.Exists(FontFamily.Families, f => f.Name == "Segoe UI Variable"))
                family = "Segoe UI Variable";
        }
        catch
        {
        }
        Font = new Font(family, 9f);
        FontBold = new Font(family, 9f, FontStyle.Bold);
        FontSmall = new Font(family, 8f);
        ApplyTheme(false);
    }

    public static bool IsDark;
    public static Color WindowBg { get; private set; }
    public static Color Card { get; private set; }
    public static Color CardBorder { get; private set; }
    public static Color InputBorder { get; private set; }
    public static Color Text { get; private set; }
    public static Color Muted { get; private set; }
    public static Color Accent { get; private set; }
    public static Color AccentHover { get; private set; }
    public static Color AccentPressed { get; private set; }
    public static Color AccentText { get; private set; }
    public static Color Danger { get; private set; }
    public static Color DangerBg { get; private set; }
    public static Color DangerPressed { get; private set; }
    public static Color Thumb { get; private set; }
    public static Color ModeActiveBg { get; private set; }
    public static Color ModeActiveFg { get; private set; }
    public static Color ModeActiveBorder { get; private set; }
    public static Color ModeActiveHover { get; private set; }
    public static Color ModeActivePressed { get; private set; }
    public static Color ModeIdleHover { get; private set; }
    public static Color ModeIdlePressed { get; private set; }

    // Applies the light/dark palette (Win11 system colors). Called at
    // startup and whenever the system theme changes; controls read these
    // live, so a repaint picks up the new values.
    public static void ApplyTheme(bool dark)
    {
        IsDark = dark;
        if (dark)
        {
            WindowBg = Color.FromArgb(0x20, 0x20, 0x20);
            Card = Color.FromArgb(0x2B, 0x2B, 0x2B);
            CardBorder = Color.FromArgb(0x3E, 0x3E, 0x3E);
            InputBorder = Color.FromArgb(0x56, 0x56, 0x56);
            Text = Color.FromArgb(0xF2, 0xF2, 0xF2);
            Muted = Color.FromArgb(0xA8, 0xA8, 0xA8);
            Accent = Color.FromArgb(0x4C, 0xC2, 0xFF);
            AccentHover = Color.FromArgb(0x66, 0xCB, 0xFF);
            AccentPressed = Color.FromArgb(0x38, 0xB6, 0xF5);
            AccentText = Color.FromArgb(0x0E, 0x2A, 0x44);
            Danger = Color.FromArgb(0xFF, 0x99, 0xA4);
            DangerBg = Color.FromArgb(0x45, 0x2A, 0x2E);
            DangerPressed = Color.FromArgb(0x55, 0x34, 0x39);
            Thumb = Color.FromArgb(0x9C, 0x9C, 0x9C);
            ModeActiveBg = Color.FromArgb(0x2E, 0x41, 0x59);
            ModeActiveFg = Color.FromArgb(0xA9, 0xCD, 0xFF);
            ModeActiveBorder = Color.FromArgb(0x4A, 0x6A, 0x96);
            ModeActiveHover = Color.FromArgb(0x35, 0x4B, 0x67);
            ModeActivePressed = Color.FromArgb(0x27, 0x38, 0x50);
            ModeIdleHover = Color.FromArgb(0x38, 0x38, 0x38);
            ModeIdlePressed = Color.FromArgb(0x3F, 0x3F, 0x3F);
        }
        else
        {
            WindowBg = Color.FromArgb(0xF3, 0xF3, 0xF3);
            Card = Color.White;
            CardBorder = Color.FromArgb(0xE5, 0xE5, 0xE5);
            InputBorder = Color.FromArgb(0xC4, 0xC4, 0xC4);
            Text = Color.FromArgb(0x1A, 0x1A, 0x1A);
            Muted = Color.FromArgb(0x61, 0x61, 0x61);
            Accent = Color.FromArgb(0x00, 0x67, 0xC0);
            AccentHover = Color.FromArgb(0x10, 0x6B, 0xC4);
            AccentPressed = Color.FromArgb(0x00, 0x5B, 0xA8);
            AccentText = Color.White;
            Danger = Color.FromArgb(0xC4, 0x2B, 0x1C);
            DangerBg = Color.FromArgb(0xFF, 0xF0, 0xEE);
            DangerPressed = Color.FromArgb(0xFB, 0xDC, 0xD8);
            Thumb = Color.FromArgb(0x66, 0x66, 0x66);
            ModeActiveBg = Color.FromArgb(0xE8, 0xF1, 0xFB);
            ModeActiveFg = Color.FromArgb(0x00, 0x5F, 0xB8);
            ModeActiveBorder = Color.FromArgb(0xBF, 0xDB, 0xF8);
            ModeActiveHover = Color.FromArgb(0xDB, 0xEA, 0xF9);
            ModeActivePressed = Color.FromArgb(0xCE, 0xE1, 0xF6);
            ModeIdleHover = Color.FromArgb(0xF5, 0xF5, 0xF5);
            ModeIdlePressed = Color.FromArgb(0xEF, 0xEF, 0xEF);
        }
    }

    private static string? _iconFont;

    private static string IconFont()
    {
        if (_iconFont != null) return _iconFont;
        string[] candidates = { "Segoe Fluent Icons", "Segoe MDL2 Assets" };
        try
        {
            _iconFont = candidates.FirstOrDefault(f =>
                Array.Exists(FontFamily.Families, x => x.Name == f)) ?? "Segoe UI Symbol";
        }
        catch
        {
            _iconFont = "Segoe UI Symbol";
        }
        return _iconFont;
    }

    public static Bitmap Glyph(char code, int sizePx)
    {
        var bmp = new Bitmap(sizePx, sizePx);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var font = new Font(IconFont(), sizePx * 0.78f);
            using var brush = new SolidBrush(IsDark ? Color.FromArgb(0xCC, 0xCC, 0xCC) : Color.FromArgb(0x44, 0x44, 0x44));
            var sf = new StringFormat(StringFormat.GenericTypographic)
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            g.DrawString(code.ToString(), font, brush, new RectangleF(0, 0, sizePx, sizePx), sf);
        }
        return bmp;
    }

    public static Icon AppIcon()
    {
        // A multi-size .ico built in memory: the title bar, taskbar and
        // notification tray each pick the image closest to what they need,
        // so every surface shows the same crisp icon. The 256px master is
        // stored as PNG, the format Windows expects for the high-DPI image
        // (the shell scales it down for larger taskbar/tray renderings).
        int[] sizes = { 16, 24, 32, 48, 64 };
        const int highRes = 256;
        var images = new System.Collections.Generic.List<byte[]>();
        foreach (int size in sizes)
        {
            using var bmp = RenderBitmap(size);
            images.Add(IconImageBytes(bmp));
        }
        using (var big = RenderBitmap(highRes))
        using (var png = new MemoryStream())
        {
            big.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            images.Add(png.ToArray());
        }

        int count = images.Count;
        int dirSize = 6 + count * 16;
        int total = dirSize;
        foreach (var img in images) total += img.Length;
        var ico = new byte[total];
        Write16(ico, 0, 0);           // reserved
        Write16(ico, 2, 1);           // type: icon
        Write16(ico, 4, (short)count);
        int offset = dirSize;
        for (int i = 0; i < count; i++)
        {
            int size = i < sizes.Length ? sizes[i] : highRes;
            int e = 6 + i * 16;
            ico[e] = (byte)(size == 256 ? 0 : size);     // width (0 means 256)
            ico[e + 1] = (byte)(size == 256 ? 0 : size); // height (0 means 256)
            ico[e + 2] = 0;           // palette size
            ico[e + 3] = 0;           // reserved
            Write16(ico, e + 4, 1);   // planes
            Write16(ico, e + 6, 32);  // bit count
            Write32(ico, e + 8, images[i].Length);
            Write32(ico, e + 12, offset);
            offset += images[i].Length;
        }
        int dataOffset = dirSize;
        foreach (var img in images)
        {
            img.CopyTo(ico, dataOffset);
            dataOffset += img.Length;
        }
        // .NET's "closest size" picker measures distance using the raw
        // directory bytes, where the 256px image is encoded as 0x0, so it
        // can never win the match. Ask for 64 explicitly: that is the
        // largest selectable image, crisp at any DPI without upscaling,
        // and the shell downscales it cleanly for smaller slots.
        using var stream = new MemoryStream(ico);
        return new Icon(stream, 64, 64);
    }

    // The title bar icon: DWM draws the window icon at 16 logical pixels, so
    // at the form's DPI it wants an image of exactly 16 * dpi/96 physical
    // pixels. Handing it that size means no scaling at all - DWM's own
    // downscaling (e.g. from a 64px HICON) is what made the title bar icon
    // look softer than the tray's.
    public static Icon WindowIcon(int dpi)
    {
        int px = Math.Clamp((int)Math.Round(16.0 * dpi / 96f), 16, 64);
        using var bmp = RenderBitmap(px);
        var img = IconImageBytes(bmp);
        var ico = new byte[6 + 16 + img.Length];
        Write16(ico, 2, 1);
        Write16(ico, 4, 1);
        ico[6] = (byte)px;
        ico[7] = (byte)px;
        Write16(ico, 10, 1);
        Write16(ico, 12, 32);
        Write32(ico, 14, img.Length);
        Write32(ico, 18, 22);
        img.CopyTo(ico, 22);
        using var stream = new MemoryStream(ico);
        return new Icon(stream, px, px);
    }

    private static Bitmap RenderBitmap(int size)
    {
        // Rasterize at 4x and shrink with a high-quality filter: the
        // supersampled source keeps edges far cleaner when Windows
        // later downscales the icon for small slots (tray, title bar).
        using var hi = Rasterize(size * 4);
        var low = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(low))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.DrawImage(hi, new Rectangle(0, 0, size, size));
        }
        return low;
    }

    private static Bitmap Rasterize(int px)
    {
        var bmp = new Bitmap(px, px, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        using var glyph = BackspaceGlyph(px);
        // A dark outline just outside the glyph keeps the shape readable
        // on light backgrounds; the white fill covers the inner half of
        // the stroke, so only the outer ring shows. The transparent
        // corners mean the icon no longer reads as a square box.
        float u = px / 32f;
        using var pen = new Pen(Color.FromArgb(38, 42, 58), 4f * u)
        {
            LineJoin = LineJoin.Round,
        };
        g.DrawPath(pen, glyph);
        g.FillPath(Brushes.White, glyph);
        return bmp;
    }

    // The backspace (U+232B) shape: a left arrowhead merged into a rectangle,
    // drawn geometrically so it does not depend on any installed font
    // (missing font glyphs render as empty boxes). The arrowhead edges are
    // exactly 45 degrees, which survive downscaling to small tray sizes
    // far more cleanly than shallow slopes.
    private static GraphicsPath BackspaceGlyph(int size)
    {
        float u = size / 32f;
        var path = new GraphicsPath();
        path.AddPolygon(new[]
        {
            new PointF(11f * u, 10f * u),
            new PointF(27f * u, 10f * u),
            new PointF(27f * u, 22f * u),
            new PointF(11f * u, 22f * u),
            new PointF(5f * u, 16f * u),
        });
        return path;
    }

    // One .ico image: a 40-byte BITMAPINFOHEADER, then the bottom-up 32bpp
    // pixel data, then an all-zero AND mask (the alpha channel is
    // authoritative for 32bpp images).
    private static byte[] IconImageBytes(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        var rect = new Rectangle(0, 0, w, h);
        var bits = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        byte[] xor;
        try
        {
            xor = new byte[w * h * 4];
            System.Runtime.InteropServices.Marshal.Copy(bits.Scan0, xor, 0, xor.Length);
        }
        finally
        {
            bmp.UnlockBits(bits);
        }

        // ICO images are bottom-up; GDI+ hands out top-down rows.
        int rowBytes = w * 4;
        for (int y = 0; y < h / 2; y++)
        {
            int o1 = y * rowBytes, o2 = (h - 1 - y) * rowBytes;
            for (int i = 0; i < rowBytes; i++)
            {
                byte t = xor[o1 + i];
                xor[o1 + i] = xor[o2 + i];
                xor[o2 + i] = t;
            }
        }

        int andBytes = (w + 31) / 32 * 4 * h;
        var outBytes = new byte[40 + xor.Length + andBytes];
        outBytes[0] = 40;                         // biSize
        Write32(outBytes, 4, w);                  // biWidth
        Write32(outBytes, 8, h * 2);              // biHeight (XOR + AND)
        Write16(outBytes, 12, 1);                 // biPlanes
        Write16(outBytes, 14, 32);                // biBitCount
        Write32(outBytes, 20, xor.Length + andBytes); // biSizeImage
        xor.CopyTo(outBytes, 40);
        return outBytes;
    }

    private static void Write16(byte[] b, int o, short v) => BitConverter.TryWriteBytes(b.AsSpan(o), v);
    private static void Write32(byte[] b, int o, int v) => BitConverter.TryWriteBytes(b.AsSpan(o), v);

    public static GraphicsPath RoundRect(Rectangle r, int radius)
        => RoundRect(new RectangleF(r.X, r.Y, r.Width, r.Height), radius);

    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        float d = radius * 2f;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void ApplyRounded(Control c, int radius)
    {
        if (radius <= 0) return;
        c.Region = new Region(RoundRect(new Rectangle(0, 0, c.Width, c.Height), radius));
        EventHandler onResize = (_, _) =>
            c.Region = new Region(RoundRect(new Rectangle(0, 0, c.Width, c.Height), radius));
        c.Resize += onResize;
    }
}