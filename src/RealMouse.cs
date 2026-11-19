using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace HypaceTray
{
    // A cut-out product photo (transparent background), kept as floating-point pixels so it can be drawn at any
    // size and level without decoding it again.
    public sealed class MousePhoto
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        internal readonly float[] R, G, B, A;          // straight colour 0..1, alpha 0..1
        internal readonly double MeanLuminance;
        internal readonly double MinX, MaxX, MinY, MaxY; // the silhouette after the tilt, relative to the photo centre

        public MousePhoto(Bitmap cutout)
        {
            int w = cutout.Width, h = cutout.Height, n = w * h;
            Width = w;
            Height = h;
            byte[] px = RealMouse.ReadPixels(cutout);
            R = new float[n]; G = new float[n]; B = new float[n]; A = new float[n];
            double lum = 0, weight = 0;
            for (int i = 0; i < n; i++)
            {
                A[i] = px[i * 4 + 3] / 255f;
                R[i] = px[i * 4 + 2] / 255f;
                G[i] = px[i * 4 + 1] / 255f;
                B[i] = px[i * 4] / 255f;
                lum += RealMouse.Luminance(R[i], G[i], B[i]) * A[i];
                weight += A[i];
            }
            MeanLuminance = weight > 0 ? lum / weight : 0.5;

            double t = RealMouse.Tilt * Math.PI / 180, cs = Math.Cos(t), sn = Math.Sin(t), cx = w / 2.0, cy = h / 2.0;
            MinX = MinY = double.MaxValue;
            MaxX = MaxY = double.MinValue;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (A[y * w + x] < 0.5f) continue;
                    for (int c = 0; c < 4; c++)
                    {
                        double dx = x + (c & 1) - cx, dy = y + (c >> 1) - cy;
                        double rx = dx * cs - dy * sn, ry = dx * sn + dy * cs;
                        MinX = Math.Min(MinX, rx); MaxX = Math.Max(MaxX, rx);
                        MinY = Math.Min(MinY, ry); MaxY = Math.Max(MaxY, ry);
                    }
                }
            if (MaxX < MinX) { MinX = MinY = -1; MaxX = MaxY = 1; }
        }
    }

    // The realistic mouse: Lofree's top-down product photo (cut out, built in: Photos), tilted like the drawn
    // mouse and shaded by the level in one of three ways (LevelDisplay).
    public static class RealMouse
    {
        internal const float Tilt = MouseIconPainter.Tilt;   // degrees clockwise, front up and to the right
        const int Samples = 16;                               // per pixel and axis when shrinking the photo

        // The photo tilted 45 degrees clockwise and scaled so it touches all four sides of the size x size square,
        // each pixel averaged from Samples x Samples bilinear samples. The level line is horizontal, from the
        // mouse's lowest point up; below it the charge, above it the drained part, shaded by display:
        //   GreyWhite (B)  charge as photographed, drained part grey-white
        //   SeeThrough (C) charge as photographed, drained part at 30% opacity
        //   LevelColour (A) charge in the level colour (blue while charging) with the photo's light and shade,
        //                   strongly coloured parts (the orange side buttons) kept; drained part grey-white
        // Asleep: no colour anywhere. Unknown: all drained.
        public static Bitmap Paint(MousePhoto photo, int level, IconState state, LevelDisplay display, int size)
        {
            level = Math.Max(0, Math.Min(100, level));
            double t = Tilt * Math.PI / 180, cs = Math.Cos(t), sn = Math.Sin(t), cx = photo.Width / 2.0, cy = photo.Height / 2.0;
            double rw = photo.MaxX - photo.MinX, rh = photo.MaxY - photo.MinY, s = size / Math.Max(rw, rh);
            double offX = (size - rw * s) / 2, offY = (size - rh * s) / 2;
            double bottom = offY + rh * s, line = bottom - rh * s * level / 100.0;
            bool asleep = state == IconState.Asleep, unknown = state == IconState.Unknown;
            Color lc = state == IconState.Charging ? BatteryPalette.Charging : BatteryPalette.ForLevel(level);
            double lr = lc.R / 255.0, lg = lc.G / 255.0, lb = lc.B / 255.0;
            int W = photo.Width, H = photo.Height;
            var outPx = new byte[size * size * 4];

            for (int oy = 0; oy < size; oy++)
            {
                double cov = unknown ? 0 : Clamp01(oy + 1 - line);   // share of this row that is charge
                for (int ox = 0; ox < size; ox++)
                {
                    double sr = 0, sg = 0, sb = 0, sa = 0;
                    for (int j = 0; j < Samples; j++)
                        for (int i = 0; i < Samples; i++)
                        {
                            double qx = ox + (i + 0.5) / Samples, qy = oy + (j + 0.5) / Samples;
                            double rx = (qx - offX) / s + photo.MinX, ry = (qy - offY) / s + photo.MinY;
                            double px = rx * cs + ry * sn + cx - 0.5, py = -rx * sn + ry * cs + cy - 0.5;
                            int x0 = (int)Math.Floor(px), y0 = (int)Math.Floor(py);
                            double fx = px - x0, fy = py - y0;
                            for (int k = 0; k < 4; k++)
                            {
                                int xx = x0 + (k & 1), yy = y0 + (k >> 1);
                                if (xx < 0 || yy < 0 || xx >= W || yy >= H) continue;
                                int si = yy * W + xx;
                                double a = photo.A[si] * ((k & 1) == 1 ? fx : 1 - fx) * ((k >> 1) == 1 ? fy : 1 - fy);
                                sr += photo.R[si] * a; sg += photo.G[si] * a; sb += photo.B[si] * a; sa += a;
                            }
                        }
                    double alpha = sa / (Samples * Samples);
                    if (alpha <= 0) continue;
                    double r = sr / sa, g = sg / sa, b = sb / sa, L = Luminance(r, g, b);

                    // the charge
                    double cr = r, cg = g, cb = b;
                    if (display == LevelDisplay.LevelColour)
                    {
                        double shade = Math.Max(0.45, Math.Min(1.25, 1 + 1.3 * (L - photo.MeanLuminance)));
                        double sat = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
                        double keep = Clamp01((sat - 0.25) / 0.2);
                        cr = keep * r + (1 - keep) * Clamp01(lr * shade);
                        cg = keep * g + (1 - keep) * Clamp01(lg * shade);
                        cb = keep * b + (1 - keep) * Clamp01(lb * shade);
                    }
                    // the drained part
                    double dr, dg, db, da = alpha;
                    if (display == LevelDisplay.SeeThrough) { dr = r; dg = g; db = b; da = alpha * 0.3; }
                    else { double gw = 0.60 + 0.35 * L; dr = dg = db = gw; }

                    double pa = cov * alpha + (1 - cov) * da;
                    if (pa <= 0) continue;
                    double outR = (cov * alpha * cr + (1 - cov) * da * dr) / pa;
                    double outG = (cov * alpha * cg + (1 - cov) * da * dg) / pa;
                    double outB = (cov * alpha * cb + (1 - cov) * da * db) / pa;
                    if (asleep) outR = outG = outB = Luminance(outR, outG, outB);

                    int d = (oy * size + ox) * 4;
                    outPx[d] = ToByte(outB); outPx[d + 1] = ToByte(outG); outPx[d + 2] = ToByte(outR); outPx[d + 3] = ToByte(pa);
                }
            }
            return WritePixels(outPx, size, size);
        }

        internal static double Luminance(double r, double g, double b) { return 0.2126 * r + 0.7152 * g + 0.0722 * b; }
        static double Clamp01(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
        static byte ToByte(double v) { return (byte)Math.Round(Clamp01(v) * 255); }

        // BGRA bytes, rows packed (width * 4)
        internal static byte[] ReadPixels(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            var px = new byte[w * h * 4];
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try { for (int y = 0; y < h; y++) Marshal.Copy(data.Scan0 + y * data.Stride, px, y * w * 4, w * 4); }
            finally { bmp.UnlockBits(data); }
            return px;
        }

        static Bitmap WritePixels(byte[] px, int w, int h)
        {
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { for (int y = 0; y < h; y++) Marshal.Copy(px, y * w * 4, data.Scan0 + y * data.Stride, w * 4); }
            finally { bmp.UnlockBits(data); }
            return bmp;
        }
    }
}
