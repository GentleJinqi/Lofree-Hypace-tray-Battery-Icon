using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace HypaceTray
{
    // The Hypace's three colourways, named as Lofree names them.
    public enum Colourway { CrayonGrey, JetBlack, Ghost }

    // How the realistic mouse shows the level: B (default), C, A.
    public enum LevelDisplay { GreyWhite, SeeThrough, LevelColour }

    // What the mouse looks like: the drawn mouse (no photo) or a product photo, and whether a bolt marks charging.
    public sealed class MouseLook
    {
        public static readonly MouseLook Drawn = new MouseLook(null, LevelDisplay.GreyWhite, false);

        public MousePhoto Photo { get; private set; }
        public LevelDisplay Display { get; private set; }
        public bool Bolt { get; private set; }

        public MouseLook(MousePhoto photo, LevelDisplay display, bool bolt)
        {
            Photo = photo;
            Display = display;
            Bolt = bolt;
        }

        // The mouse square, size x size. linePx: width of the drawn mouse's black lines.
        public Bitmap PaintMouse(int level, IconState state, bool lightTaskbar, int size, float linePx)
        {
            Bitmap m = Photo != null ? RealMouse.Paint(Photo, level, state, Display, size)
                                     : MouseIconPainter.Paint(level, state, lightTaskbar, size, MouseIconPainter.Tilt, linePx);
            if (Bolt && state == IconState.Charging) ChargingBolt.Draw(m, lightTaskbar);
            return m;
        }
    }

    // The mouse choices kept in settings.txt: mouseStyle=drawn|realistic, colourway=CrayonGrey|JetBlack|Ghost,
    // levelDisplay=greywhite|seethrough|levelcolour, chargingBolt=on|off. Unknown values fall back to the defaults:
    // the realistic Ghost, drained part see-through, bolt on.
    public sealed class MouseSettings
    {
        public bool Realistic = true;
        public Colourway Colourway = Colourway.Ghost;
        public LevelDisplay Display = LevelDisplay.SeeThrough;
        public bool ChargingBolt = true;

        static readonly string[] DisplayNames = { "greywhite", "seethrough", "levelcolour" };   // LevelDisplay order

        public static MouseSettings Parse(IDictionary<string, string> s)
        {
            var m = new MouseSettings();
            m.Realistic = Value(s, "mouseStyle") != "drawn";
            string c = Value(s, "colourway");
            foreach (Colourway cw in Enum.GetValues(typeof(Colourway)))
                if (cw.ToString().ToLowerInvariant() == c) m.Colourway = cw;
            int d = Array.IndexOf(DisplayNames, Value(s, "levelDisplay"));
            if (d >= 0) m.Display = (LevelDisplay)d;
            m.ChargingBolt = Value(s, "chargingBolt") != "off";
            return m;
        }

        public void WriteTo(IDictionary<string, string> s)
        {
            s["mouseStyle"] = Realistic ? "realistic" : "drawn";
            s["colourway"] = Colourway.ToString();
            s["levelDisplay"] = DisplayNames[(int)Display];
            s["chargingBolt"] = ChargingBolt ? "on" : "off";
        }

        static string Value(IDictionary<string, string> s, string key)
        {
            string v;
            return s.TryGetValue(key, out v) && v != null ? v.Trim().ToLowerInvariant() : "";
        }
    }

    // While charging: a small bolt in the bottom-right of the mouse square, like the badge on the taskbar's globe
    // icon. The mouse is cut away in a thin ring round the bolt (as Windows does round its battery bolt), so the bolt
    // reads on any mouse colour; white on a dark taskbar, near-black on a light one.
    public static class ChargingBolt
    {
        static readonly PointF[] Unit =
        {
            new PointF(0.38f, 0f), new PointF(0.66f, 0f), new PointF(0.45f, 0.40f), new PointF(0.68f, 0.40f),
            new PointF(0.24f, 1f), new PointF(0.34f, 0.56f), new PointF(0.08f, 0.56f),
        };
        static readonly Color OnDark = Color.White;
        static readonly Color OnLight = Color.FromArgb(0x1C, 0x1C, 0x1C);

        // The bolt in pixels for a size x size mouse square: 11/24 of it tall, 0.4/24 in from the right and bottom.
        public static PointF[] Polygon(int size)
        {
            float k = size / 24f, h = 11f * k, left = size - 0.68f * h - 0.4f * k, top = size - h - 0.4f * k;
            var pts = new PointF[Unit.Length];
            for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(left + Unit[i].X * h, top + Unit[i].Y * h);
            return pts;
        }

        public static void Draw(Bitmap mouse, bool lightTaskbar)
        {
            int W = mouse.Width, H = mouse.Height;
            float gap = 1.2f * W / 24f;
            PointF[] pts = Polygon(W);
            using (var mask = new Bitmap(W, H, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(mask))
                using (var path = new GraphicsPath())
                using (var white = new SolidBrush(Color.White))
                using (var pen = new Pen(Color.White, 2 * gap) { LineJoin = LineJoin.Round })
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    path.AddPolygon(pts);
                    g.FillPath(white, path);
                    g.DrawPath(pen, path);
                }
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int cov = mask.GetPixel(x, y).A;
                        if (cov == 0) continue;
                        Color p = mouse.GetPixel(x, y);
                        mouse.SetPixel(x, y, Color.FromArgb((int)Math.Round(p.A * (255 - cov) / 255.0), p.R, p.G, p.B));
                    }
            }
            using (var g = Graphics.FromImage(mouse))
            using (var brush = new SolidBrush(lightTaskbar ? OnLight : OnDark))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.FillPolygon(brush, pts);
            }
        }
    }
}
