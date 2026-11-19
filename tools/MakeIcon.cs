using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

// Makes HypaceTray.exe's icon from the three product photos in photos\: the Hypace upright, split along its own
// seams into four shell panels (the centre channel splits left from right, the Λ button seams front from rear):
// front-left Ghost, front-right Jet Black, rear-left Crayon Grey, rear-right a sketch traced from the Crayon Grey
// photo (dark lines on white). Every piece is drawn into the same rectangle, so the panels line up.
// The photos, and so the icon, are (c) Lofree (see photos\README.md).
//   MakeIcon.exe <photosDir> <out.ico> [preview.png]      (tools\make-icon.ps1 builds and runs it)
static class MakeIcon
{
    static readonly int[] Sizes = { 256, 64, 48, 40, 32, 24, 20, 16 };
    const int Hi = 1024;                   // tracing resolution
    const int K = Hi / 256;                // tracing distances below are in pixels of the 256 px icon, times K
    const float MouseHeight = 240f / 256;  // of the icon; the width follows the photos (326 x 630)
    const float ShellLuminance = 0.42f;    // shell surfaces 0.48-0.90 (the shaded right half lowest); seams, rims, channel below 0.35
    static readonly Color Ink = Color.FromArgb(0x26, 0x26, 0x26), Paper = Color.FromArgb(0xF7, 0xF6, 0xF2);
    static readonly string[] PanelPhotos = { "Ghost", "JetBlack", "CrayonGrey" };   // panels 0-2; panel 3 is the sketch

    // sketch line width and outline width (0: none), in icon pixels
    static float LineWidth(int s) { return s >= 256 ? 3f : s >= 64 ? 1.6f : s >= 40 ? 1.3f : 1.1f; }
    static float OutlineWidth(int s) { return s > 32 ? 0f : 1f; }

    static int Main(string[] a)
    {
        if (a.Length < 2) { Console.Error.WriteLine("usage: MakeIcon <photosDir> <out.ico> [preview.png]"); return 2; }
        try { return Make(a); }
        catch (Exception e) { Console.Error.WriteLine("MakeIcon: " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); return 1; }
    }

    static int Make(string[] a)
    {
        string dir = a[0];

        // tracing, at Hi
        Pixels hi;
        using (var b = DrawPhoto(dir, "CrayonGrey", Hi)) hi = new Pixels(b);
        bool[] sil, shell, sides;
        Masks(hi, out sil, out shell, out sides);
        var edges = Or(Edge(shell), Edge(sides), Edge(sil));
        float[] edgeDist = Distance(edges), silDist = Distance(Edge(sil));
        int[] panel = SplitPanels(hi, shell);
        var panelHi = new float[4][];
        for (int p = 0; p < 4; p++)
        {
            panelHi[p] = new float[Hi * Hi];
            for (int i = 0; i < Hi * Hi; i++) panelHi[p][i] = panel[i] == p ? 1 : 0;
        }

        var images = new List<Bitmap>();
        foreach (int s in Sizes) images.Add(Render(dir, s, panelHi, edgeDist, silDist, sil));
        WriteIco(a[1], images);

        CheckIco(a[1]);
        Console.WriteLine("wrote " + a[1] + ": " + string.Join(", ", Array.ConvertAll(Sizes, x => x.ToString())) + " px, 4 panels");
        if (a.Length > 2) Preview(images, a[2]);
        foreach (var b in images) b.Dispose();
        return 0;
    }

    // ---------------- drawing ----------------

    static RectangleF Target(int size)
    {
        float h = size * MouseHeight, w = h * 326f / 630f;
        return new RectangleF((size - w) / 2, (size - h) / 2, w, h);
    }

    static Bitmap DrawPhoto(string dir, string name, int size)
    {
        using (var src = new Bitmap(Path.Combine(dir, name + ".png")))
        {
            var b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.DrawImage(src, Target(size), new Rectangle(0, 0, src.Width, src.Height), GraphicsUnit.Pixel);
            }
            return b;
        }
    }

    static Bitmap Render(string dir, int s, float[][] panelHi, float[] edgeDist, float[] silDist, bool[] sil)
    {
        int n = s * s;
        var pieces = new Pixels[4];
        for (int p = 0; p < 3; p++)
            using (var b = DrawPhoto(dir, PanelPhotos[p], s)) pieces[p] = new Pixels(b, s);

        // the sketch: the Crayon Grey silhouette in paper white, the traced lines in ink
        float lineHalf = LineWidth(s) / 2 * Hi / s;
        var lineHi = new float[Hi * Hi];
        for (int i = 0; i < lineHi.Length; i++) lineHi[i] = Clamp01(lineHalf - edgeDist[i] + 0.5f);
        float[] line = Down(lineHi, s);
        Pixels paper;
        using (var b = DrawPhoto(dir, "CrayonGrey", s)) paper = new Pixels(b, s);
        var sketch = new Pixels(s);
        for (int i = 0; i < n; i++)
        {
            float la = line[i], pa = paper.A[i];
            Over(sketch, i, Paper, pa);
            Over(sketch, i, Ink, la);
        }
        pieces[3] = sketch;

        var w = new float[4][];
        for (int p = 0; p < 4; p++) w[p] = Down(panelHi[p], s);
        var outPx = new Pixels(s);
        for (int i = 0; i < n; i++)
        {
            float sum = w[0][i] + w[1][i] + w[2][i] + w[3][i];
            if (sum <= 0) continue;
            float A = 0, R = 0, G = 0, B = 0;
            for (int p = 0; p < 4; p++)
            {
                float wa = w[p][i] / sum * pieces[p].A[i];
                A += wa; R += wa * pieces[p].R[i]; G += wa * pieces[p].G[i]; B += wa * pieces[p].B[i];
            }
            if (A <= 0) continue;
            outPx.A[i] = A; outPx.R[i] = R / A; outPx.G[i] = G / A; outPx.B[i] = B / A;
        }

        // small sizes: a thin dark outline just inside the silhouette, so the light panels keep their shape on a
        // light background
        float o = OutlineWidth(s);
        if (o > 0)
        {
            float oHi = o * Hi / s;
            var ring = new float[Hi * Hi];
            for (int i = 0; i < ring.Length; i++) ring[i] = sil[i] ? Clamp01(oHi - silDist[i] + 0.5f) : 0;
            float[] oc = Down(ring, s);
            for (int i = 0; i < n; i++) Over(outPx, i, Ink, oc[i] * 0.85f);
        }
        return outPx.ToBitmap();
    }

    // colour c at coverage a over pixel i
    static void Over(Pixels p, int i, Color c, float a)
    {
        if (a <= 0) return;
        float A = a + p.A[i] * (1 - a);
        p.R[i] = (c.R / 255f * a + p.R[i] * p.A[i] * (1 - a)) / A;
        p.G[i] = (c.G / 255f * a + p.G[i] * p.A[i] * (1 - a)) / A;
        p.B[i] = (c.B / 255f * a + p.B[i] * p.A[i] * (1 - a)) / A;
        p.A[i] = A;
    }

    // area average from Hi x Hi to s x s
    static float[] Down(float[] hi, int s)
    {
        var o = new float[s * s];
        double f = (double)Hi / s;
        for (int y = 0; y < s; y++)
        {
            double y0 = y * f, y1 = (y + 1) * f;
            for (int x = 0; x < s; x++)
            {
                double x0 = x * f, x1 = (x + 1) * f, sum = 0;
                for (int hy = (int)y0; hy < Math.Min(Hi, (int)Math.Ceiling(y1)); hy++)
                {
                    double wy = Math.Min(y1, hy + 1) - Math.Max(y0, hy);
                    for (int hx = (int)x0; hx < Math.Min(Hi, (int)Math.Ceiling(x1)); hx++)
                        sum += wy * (Math.Min(x1, hx + 1) - Math.Max(x0, hx)) * hi[hy * Hi + hx];
                }
                o[y * s + x] = (float)(sum / (f * f));
            }
        }
        return o;
    }

    static float Clamp01(float v) { return v < 0 ? 0 : v > 1 ? 1 : v; }

    // ---------------- tracing ----------------

    // silhouette; light shell surfaces (small holes such as the logo filled; seams, rims and channel left out);
    // the orange side buttons
    static void Masks(Pixels px, out bool[] sil, out bool[] shell, out bool[] sides)
    {
        int n = Hi * Hi;
        sil = new bool[n]; shell = new bool[n]; sides = new bool[n];
        for (int i = 0; i < n; i++)
        {
            if (px.A[i] < 0.5f) continue;
            sil[i] = true;
            float L = 0.2126f * px.R[i] + 0.7152f * px.G[i] + 0.0722f * px.B[i];
            if (px.R[i] > 0.7f && px.G[i] < 0.55f && px.B[i] < 0.4f) sides[i] = true;
            else if (L > ShellLuminance) shell[i] = true;
        }
        int area = 0;
        foreach (bool v in sil) if (v) area++;
        shell = Big(Morph(Morph(shell, K / 2, false), K / 2, true), area / 40);
        var holes = new bool[n];
        for (int i = 0; i < n; i++) holes[i] = sil[i] && !shell[i];
        holes = Big(holes, area / 150);
        for (int i = 0; i < n; i++) shell[i] = sil[i] && !holes[i] && !sides[i];
        sides = Big(Morph(Morph(Morph(Morph(sides, K / 2 + 1, false), K / 2 + 1, true), K, true), K, false), area / 400);
        sil = Morph(Morph(sil, K, true), K, false);
    }

    // The four panels: the shell shrunk a little and cut along the middle of the channel (the halves touch at the
    // tip), its four largest parts as 0 front-left, 1 front-right, 2 rear-left, 3 rear-right; every other pixel of
    // the mouse joins the nearest panel.
    static int[] SplitPanels(Pixels px, bool[] shell)
    {
        int n = Hi * Hi;
        var cut = Morph(shell, 2 * K, false);
        int x0 = Hi, x1 = 0;
        for (int i = 0; i < n; i++) if (px.A[i] > 0.5f) { x0 = Math.Min(x0, i % Hi); x1 = Math.Max(x1, i % Hi); }
        int mid = (x0 + x1) / 2;
        for (int y = 0; y < Hi; y++) for (int x = mid - K; x <= mid + K; x++) cut[y * Hi + x] = false;

        var label = new int[n];
        for (int i = 0; i < n; i++) label[i] = -1;
        var parts = new List<double[]>();   // area, x, y
        var stack = new Stack<int>();
        for (int s = 0; s < n; s++)
        {
            if (!cut[s] || label[s] != -1) continue;
            int id = parts.Count; double area = 0, sx = 0, sy = 0;
            label[s] = id; stack.Push(s);
            while (stack.Count > 0)
            {
                int i = stack.Pop(), x = i % Hi, y = i / Hi;
                area++; sx += x; sy += y;
                foreach (int j in Neighbours(i))
                    if (j >= 0 && cut[j] && label[j] == -1) { label[j] = id; stack.Push(j); }
            }
            parts.Add(new[] { area, sx / area, sy / area });
        }
        var order = new List<int>();
        for (int i = 0; i < parts.Count; i++) order.Add(i);
        order.Sort((p, q) => parts[q][0].CompareTo(parts[p][0]));
        if (order.Count < 4 || parts[order[3]][0] < parts[order[0]][0] / 4)
            throw new Exception("could not find the four shell panels in the Crayon Grey photo");
        var top = order.GetRange(0, 4);
        double mx = 0, my = 0;
        foreach (int i in top) { mx += parts[i][1] / 4; my += parts[i][2] / 4; }
        var panelOf = new Dictionary<int, int>();
        foreach (int i in top) panelOf[i] = (parts[i][2] < my ? 0 : 2) + (parts[i][1] < mx ? 0 : 1);
        if (new HashSet<int>(panelOf.Values).Count != 4) throw new Exception("the shell parts are not one per corner");

        var panel = new int[n];
        var queue = new Queue<int>();
        for (int i = 0; i < n; i++)
        {
            int p;
            panel[i] = label[i] >= 0 && panelOf.TryGetValue(label[i], out p) ? p : -1;
            if (panel[i] >= 0) queue.Enqueue(i);
        }
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            foreach (int j in Neighbours(i))
                if (j >= 0 && panel[j] == -1 && px.A[j] > 0.02f) { panel[j] = panel[i]; queue.Enqueue(j); }
        }
        return panel;
    }

    static int[] Neighbours(int i)
    {
        int x = i % Hi, y = i / Hi;
        return new[] { x > 0 ? i - 1 : -1, x < Hi - 1 ? i + 1 : -1, y > 0 ? i - Hi : -1, y < Hi - 1 ? i + Hi : -1 };
    }

    // dilate (or erode) with a disc of radius r
    static bool[] Morph(bool[] m, int r, bool dilate)
    {
        var o = new bool[Hi * Hi];
        for (int y = 0; y < Hi; y++)
            for (int x = 0; x < Hi; x++)
            {
                bool acc = !dilate;
                for (int dy = -r; dy <= r && acc != dilate; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (dx * dx + dy * dy > r * r) continue;
                        int xx = x + dx, yy = y + dy;
                        bool v = xx >= 0 && yy >= 0 && xx < Hi && yy < Hi && m[yy * Hi + xx];
                        if (dilate && v) { acc = true; break; }
                        if (!dilate && !v) { acc = false; break; }
                    }
                o[y * Hi + x] = acc;
            }
        return o;
    }

    // connected parts of at least minArea pixels
    static bool[] Big(bool[] m, int minArea)
    {
        var seen = new bool[Hi * Hi];
        var o = new bool[Hi * Hi];
        var stack = new Stack<int>();
        var part = new List<int>();
        for (int s = 0; s < m.Length; s++)
        {
            if (!m[s] || seen[s]) continue;
            part.Clear(); seen[s] = true; stack.Push(s);
            while (stack.Count > 0)
            {
                int i = stack.Pop(); part.Add(i);
                foreach (int j in Neighbours(i)) if (j >= 0 && m[j] && !seen[j]) { seen[j] = true; stack.Push(j); }
            }
            if (part.Count >= minArea) foreach (int i in part) o[i] = true;
        }
        return o;
    }

    static bool[] Edge(bool[] m)
    {
        var o = new bool[Hi * Hi];
        for (int i = 0; i < m.Length; i++)
        {
            if (!m[i]) continue;
            foreach (int j in Neighbours(i)) if (j < 0 || !m[j]) { o[i] = true; break; }
        }
        return o;
    }

    static bool[] Or(params bool[][] ms)
    {
        var o = new bool[Hi * Hi];
        foreach (var m in ms) for (int i = 0; i < o.Length; i++) o[i] |= m[i];
        return o;
    }

    // distance in pixels from each pixel's centre to the nearest set pixel's centre (two-pass chamfer, 1 and √2)
    static float[] Distance(bool[] m)
    {
        const float D = 1f, E = 1.41421356f, Far = 1e9f;
        var d = new float[Hi * Hi];
        for (int i = 0; i < d.Length; i++) d[i] = m[i] ? 0 : Far;
        for (int y = 0; y < Hi; y++)
            for (int x = 0; x < Hi; x++)
            {
                int i = y * Hi + x; float v = d[i];
                if (x > 0) v = Math.Min(v, d[i - 1] + D);
                if (y > 0)
                {
                    v = Math.Min(v, d[i - Hi] + D);
                    if (x > 0) v = Math.Min(v, d[i - Hi - 1] + E);
                    if (x < Hi - 1) v = Math.Min(v, d[i - Hi + 1] + E);
                }
                d[i] = v;
            }
        for (int y = Hi - 1; y >= 0; y--)
            for (int x = Hi - 1; x >= 0; x--)
            {
                int i = y * Hi + x; float v = d[i];
                if (x < Hi - 1) v = Math.Min(v, d[i + 1] + D);
                if (y < Hi - 1)
                {
                    v = Math.Min(v, d[i + Hi] + D);
                    if (x < Hi - 1) v = Math.Min(v, d[i + Hi + 1] + E);
                    if (x > 0) v = Math.Min(v, d[i + Hi - 1] + E);
                }
                d[i] = v;
            }
        return d;
    }

    // ---------------- files ----------------

    // 256 px as PNG, the rest as 32-bit BMP (with the 1-bit mask older readers expect)
    static void WriteIco(string path, List<Bitmap> images)
    {
        var data = new List<byte[]>();
        foreach (var b in images) data.Add(b.Width >= 256 ? Png(b) : Dib(b));
        using (var f = new BinaryWriter(File.Create(path)))
        {
            f.Write((short)0); f.Write((short)1); f.Write((short)images.Count);
            int offset = 6 + 16 * images.Count;
            for (int i = 0; i < images.Count; i++)
            {
                int s = images[i].Width;
                f.Write((byte)(s >= 256 ? 0 : s)); f.Write((byte)(s >= 256 ? 0 : s));
                f.Write((byte)0); f.Write((byte)0);
                f.Write((short)1); f.Write((short)32);
                f.Write(data[i].Length); f.Write(offset);
                offset += data[i].Length;
            }
            foreach (var d in data) f.Write(d);
        }
    }

    // read the file back: one entry per size, each decoding to that size with something drawn in it
    // (System.Drawing.Icon cannot be used: it skips PNG entries)
    static void CheckIco(string path)
    {
        byte[] f = File.ReadAllBytes(path);
        int count = BitConverter.ToInt16(f, 4);
        if (BitConverter.ToInt16(f, 2) != 1 || count != Sizes.Length) throw new Exception("bad icon header");
        for (int e = 0; e < count; e++)
        {
            int s = Sizes[e], at = 6 + 16 * e, len = BitConverter.ToInt32(f, at + 8), off = BitConverter.ToInt32(f, at + 12);
            int dirW = f[at] == 0 ? 256 : f[at];
            if (dirW != s || off + len > f.Length) throw new Exception("bad entry for " + s + " px");
            int opaque = 0;
            if (f[off] == 0x89 && f[off + 1] == (byte)'P')
                using (var ms = new MemoryStream(f, off, len))
                using (var b = new Bitmap(ms))
                {
                    if (b.Width != s || b.Height != s) throw new Exception(s + " px PNG is " + b.Width + "x" + b.Height);
                    for (int y = 0; y < s; y++) for (int x = 0; x < s; x++) if (b.GetPixel(x, y).A > 128) opaque++;
                }
            else
            {
                if (BitConverter.ToInt32(f, off + 4) != s || BitConverter.ToInt32(f, off + 8) != 2 * s || BitConverter.ToInt16(f, off + 14) != 32)
                    throw new Exception("bad " + s + " px bitmap header");
                if (len != 40 + s * s * 4 + (s + 31) / 32 * 4 * s) throw new Exception("bad " + s + " px bitmap length");
                for (int i = 0; i < s * s; i++) if (f[off + 40 + i * 4 + 3] > 128) opaque++;
            }
            if (opaque < s * s / 10) throw new Exception(s + " px image is nearly empty");
        }
    }

    static byte[] Png(Bitmap b)
    {
        using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); return ms.ToArray(); }
    }

    static byte[] Dib(Bitmap b)
    {
        int s = b.Width, maskRow = (s + 31) / 32 * 4;
        var px = new Pixels(b, s);
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write(40); w.Write(s); w.Write(2 * s); w.Write((short)1); w.Write((short)32);
            w.Write(0); w.Write(s * s * 4 + maskRow * s); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            for (int y = s - 1; y >= 0; y--)
                for (int x = 0; x < s; x++)
                {
                    int i = y * s + x;
                    w.Write(Byte(px.B[i])); w.Write(Byte(px.G[i])); w.Write(Byte(px.R[i])); w.Write(Byte(px.A[i]));
                }
            for (int y = s - 1; y >= 0; y--)
            {
                var row = new byte[maskRow];
                for (int x = 0; x < s; x++) if (Byte(px.A[y * s + x]) == 0) row[x / 8] |= (byte)(0x80 >> (x % 8));
                w.Write(row);
            }
            return ms.ToArray();
        }
    }

    static byte Byte(float v) { return (byte)Math.Round(Clamp01(v) * 255); }

    // every size at 1:1 and the small ones at 4x, on a dark and a light background
    static void Preview(List<Bitmap> images, string path)
    {
        int pad = 16, W = pad, H = 256 + 2 * pad;
        foreach (var b in images) W += b.Width + pad;
        int zoomW = pad; foreach (var b in images) if (b.Width <= 32) zoomW += 4 * b.Width + pad;
        using (var sheet = new Bitmap(Math.Max(W, zoomW), 2 * (H + 4 * 32 + pad), PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(sheet))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            for (int t = 0; t < 2; t++)
            {
                int top = t * (H + 4 * 32 + pad);
                using (var bg = new SolidBrush(t == 0 ? Color.FromArgb(0x20, 0x20, 0x20) : Color.FromArgb(0xF3, 0xF3, 0xF3)))
                    g.FillRectangle(bg, 0, top, sheet.Width, H + 4 * 32 + pad);
                int x = pad;
                foreach (var b in images) { g.DrawImage(b, x, top + pad + (256 - b.Height) / 2, b.Width, b.Height); x += b.Width + pad; }
                x = pad;
                foreach (var b in images)
                    if (b.Width <= 32) { g.DrawImage(b, x, top + H, 4 * b.Width, 4 * b.Height); x += 4 * b.Width + pad; }
            }
            sheet.Save(path, ImageFormat.Png);
        }
    }

    // straight (not premultiplied) colour, 0..1
    sealed class Pixels
    {
        public readonly float[] A, R, G, B;
        readonly int size;

        public Pixels(int s) { size = s; A = new float[s * s]; R = new float[s * s]; G = new float[s * s]; B = new float[s * s]; }

        public Pixels(Bitmap b) : this(b, b.Width) { }

        public Pixels(Bitmap b, int s) : this(s)
        {
            var px = new byte[s * s * 4];
            var d = b.LockBits(new Rectangle(0, 0, s, s), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try { for (int y = 0; y < s; y++) Marshal.Copy(d.Scan0 + y * d.Stride, px, y * s * 4, s * 4); }
            finally { b.UnlockBits(d); }
            for (int i = 0; i < s * s; i++)
            {
                A[i] = px[i * 4 + 3] / 255f; R[i] = px[i * 4 + 2] / 255f; G[i] = px[i * 4 + 1] / 255f; B[i] = px[i * 4] / 255f;
            }
        }

        public Bitmap ToBitmap()
        {
            var px = new byte[size * size * 4];
            for (int i = 0; i < size * size; i++)
            {
                px[i * 4] = Byte(B[i]); px[i * 4 + 1] = Byte(G[i]); px[i * 4 + 2] = Byte(R[i]); px[i * 4 + 3] = Byte(A[i]);
            }
            var b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            var d = b.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { for (int y = 0; y < size; y++) Marshal.Copy(px, y * size * 4, d.Scan0 + y * d.Stride, size * 4); }
            finally { b.UnlockBits(d); }
            return b;
        }
    }
}
