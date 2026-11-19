using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace HypaceTray
{
    public enum IconState { Normal, Charging, Asleep, Unknown }

    public static class TrayIconState
    {
        // shownLevel < 0 means never read. A missing receiver shows "--" rather than a stale grey level,
        // so "receiver unplugged" never looks like "mouse asleep".
        public static IconState For(int shownLevel, bool online, bool receiverFound, bool charging)
        {
            if (shownLevel < 0 || !receiverFound) return IconState.Unknown;
            if (!online) return IconState.Asleep;
            return charging ? IconState.Charging : IconState.Normal;
        }
    }

    // The tray shows two icons side by side, each a full square: the percentage on the left (NumberIconPainter)
    // and the mouse on the right (MouseIconPainter). Windows gives every tray icon one fixed square, so splitting
    // them is what lets both be large.

    // Left icon: the percentage, as large as the square allows.
    public static class NumberIconPainter
    {
        static readonly Color DarkText = Color.FromArgb(0xF5, 0xF5, 0xF5);
        static readonly Color LightText = Color.FromArgb(0x1C, 0x1C, 0x1C);

        public static Bitmap Paint(int level, IconState state, bool lightTaskbar, int size)
        {
            level = Math.Max(0, Math.Min(100, level));
            int S = size;
            float k = S / 20f;
            bool unknown = state == IconState.Unknown;
            Color color = TextColor(level, state, lightTaskbar);
            // digits up to 14 of 20 tall (taller than the taskbar's own "60%"); "100" is width-limited
            var box = unknown ? new RectangleF(4 * k, 3 * k, 12 * k, 14 * k) : new RectangleF(0.5f * k, 3 * k, 19 * k, 14 * k);

            var bmp = new Bitmap(S, S, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            using (var glyph = IconText.TextPath(unknown ? "--" : level.ToString(), box))
            using (var brush = new SolidBrush(color))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.FillPath(brush, glyph);
            }
            return bmp;
        }

        // Near-white / near-black by taskbar theme; the level colour at 20% and below; grey when asleep or unknown.
        internal static Color TextColor(int level, IconState state, bool lightTaskbar)
        {
            if (level <= 20 && state != IconState.Asleep && state != IconState.Unknown)
                return lightTaskbar ? BatteryPalette.Darkened(BatteryPalette.ForLevel(level), 0.5) : BatteryPalette.ForLevel(level);
            return LineColor(state, lightTaskbar);
        }

        // The neutral line colour shared by the digits and the mouse outline: near-white / near-black by taskbar
        // theme, grey when asleep or unknown.
        internal static Color LineColor(IconState state, bool lightTaskbar)
        {
            if (state == IconState.Asleep || state == IconState.Unknown)
                return lightTaskbar ? Color.FromArgb(0x6E, 0x6E, 0x6E) : Color.FromArgb(0xA0, 0xA0, 0xA0);
            return lightTaskbar ? LightText : DarkText;
        }
    }

    // The merged block drawn on the taskbar beside the tray: the percentage, then the mouse, one icon tall.
    // It is not limited to a square, so every number gets full height, "100" included.
    public static class BadgePainter
    {
        // The block's mouse is drawn 20% larger than a tray icon (24 px at 20; the taskbar is far taller than the
        // icons), so its black lines, kept at the tray icon's weight, leave more room for the colour inside.
        internal const float MouseScale = 1.2f;

        public static Bitmap Paint(int level, IconState state, bool lightTaskbar, int size)
        {
            return Paint(level, state, lightTaskbar, size, MouseLook.Drawn);
        }

        public static Bitmap Paint(int level, IconState state, bool lightTaskbar, int size, MouseLook look)
        {
            level = Math.Max(0, Math.Min(100, level));
            int S = size, M = (int)Math.Round(S * MouseScale);
            float k = S / 20f, pad = 1 * k, gap = 3 * k, dy = (M - S) / 2f;
            bool unknown = state == IconState.Unknown;
            // digits are 14 of 20 tall and as wide as they need, centred on the block's (the mouse's) height;
            // "--" keeps the tray icon's width so it stays a dash
            var box = unknown ? new RectangleF(0, 3 * k + dy, 12 * k, 14 * k) : new RectangleF(0, 3 * k + dy, 60 * k, 14 * k);

            using (var glyph = IconText.TextPath(unknown ? "--" : level.ToString(), box))
            using (var mouse = look.PaintMouse(level, state, lightTaskbar, M, MouseIconPainter.LineWidth * k))
            {
                var r = glyph.GetBounds();
                using (var m = new Matrix()) { m.Translate(pad - r.X, 0); glyph.Transform(m); }
                int numW = (int)Math.Ceiling(pad + r.Width + gap);

                var bmp = new Bitmap(numW + M, M, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                using (var brush = new SolidBrush(NumberIconPainter.TextColor(level, state, lightTaskbar)))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.FillPath(brush, glyph);
                }
                for (int y = 0; y < M; y++)
                    for (int x = 0; x < M; x++)
                        bmp.SetPixel(numW + x, y, mouse.GetPixel(x, y));
                // a layered window lets clicks fall through fully transparent pixels: make each one barely there
                // (alpha 1 is invisible) so hovering and clicking work across the whole block
                for (int y = 0; y < M; y++)
                    for (int x = 0; x < bmp.Width; x++)
                        if (bmp.GetPixel(x, y).A == 0) bmp.SetPixel(x, y, Color.FromArgb(1, 0, 0, 0));
                return bmp;
            }
        }
    }

    // Right icon: the Lofree Hypace seen from above, tilted 45 degrees so its long axis runs along the square's
    // diagonal: the largest it can be at its real proportions (about 15% longer than upright). Translucent shell for
    // the drained part, coloured fill rising from the bottom, drawn by black lines: wheel well with a grey wheel,
    // Λ button seams, full-length centre channel; orange side buttons.
    // The shape is designed on a 20-unit grid (front up), then rotated, scaled and centred into the canvas.
    public static class MouseIconPainter
    {
        internal const float Tilt = 45f;   // degrees clockwise: front points up and to the right
        const float Cx = 10.5f, BodyW = 12f, Left = Cx - BodyW / 2f, Right = Cx + BodyW / 2f;
        static readonly RectangleF Body = new RectangleF(Left, 0, BodyW, 20);
        // the Λ button seams: apex at the channel, meeting the sides halfway back (deeper than the real mouse's 42%,
        // as the user asked, so the Λ reads at icon size); the side buttons sit just behind where the left seam ends
        const float SeamApex = 5.4f, SeamSide = 10f;
        static readonly RectangleF[] SideButtons =
        {
            new RectangleF(Left - 1.3f, SeamSide - 1.5f, 1.3f, 2f),
            new RectangleF(Left - 1.3f, SeamSide + 1.1f, 1.3f, 2f),
        };

        static readonly Color AsleepFill = Color.FromArgb(0x78, 0x78, 0x78);
        static readonly Color SideButton = Color.FromArgb(0xFF, 0x5A, 0x1F);
        static readonly Color WheelOnDark = Color.FromArgb(0xB4, 0xB4, 0xB4);
        static readonly Color WheelOnLight = Color.FromArgb(0x8A, 0x8A, 0x8A);

        public static Bitmap Paint(int level, IconState state, bool lightTaskbar, int size)
        {
            return Paint(level, state, lightTaskbar, size, Tilt);
        }

        internal static Bitmap Paint(int level, IconState state, bool lightTaskbar, int size, float tilt)
        {
            return Paint(level, state, lightTaskbar, size, tilt, LineWidth * size / 20f);
        }

        // Width of the mouse's black lines at a 20 px icon. A dark line on a bright fill looks thinner than a light
        // line of the same width on the dark taskbar; 2 px black on the fill carries the same visual weight
        // (width x lightness contrast) as the taskbar's own 1.5 px white strokes (IconText.TaskbarStroke).
        internal const float LineWidth = 2f;

        // linePx: width of the black lines in pixels
        internal static Bitmap Paint(int level, IconState state, bool lightTaskbar, int size, float tilt, float linePx)
        {
            level = Math.Max(0, Math.Min(100, level));
            int S = size;
            bool asleep = state == IconState.Asleep, unknown = state == IconState.Unknown;
            Color track = lightTaskbar ? Color.FromArgb(46, 0, 0, 0) : Color.FromArgb(asleep ? 50 : 70, 255, 255, 255);
            Color fill = state == IconState.Charging ? BatteryPalette.Charging : asleep ? AsleepFill : BatteryPalette.ForLevel(level);

            var bmp = new Bitmap(S, S, PixelFormat.Format32bppArgb);
            using (var xf = Placement(S, tilt))
            {
                float s = (float)Math.Sqrt(xf.Elements[0] * xf.Elements[0] + xf.Elements[1] * xf.Elements[1]);
                float line = linePx / s;   // in design units
                using (var shell = Shell(Body))
                using (var under = Shell(Inset(Body, line / 2)))
                {
                    shell.Transform(xf);
                    under.Transform(xf);
                    var bb = shell.GetBounds();
                    int fillTop = unknown ? S : Math.Max(0, Math.Min(S, RoundInt(bb.Bottom - bb.Height * level / 100.0)));
                    var filled = new Rectangle(0, fillTop, S, S - fillTop);
                    var empty = new Rectangle(0, 0, S, fillTop);

                    // translucent shell for the drained part, the level colour below. They stop halfway under the black
                    // outline, so the outline's outer edge is the outline alone and its inner edge blends into them.
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        using (var b = new SolidBrush(track)) { g.SetClip(empty); g.FillPath(b, under); }
                        using (var b = new SolidBrush(fill)) { g.SetClip(filled); g.FillPath(b, under); }
                        g.ResetClip();
                    }
                }

                DrawLines(bmp, xf, s, line);

                using (var g = Graphics.FromImage(bmp))
                using (var wheel = Rects(new RectangleF(10, 2.1f, 1, 2.6f)))
                using (var side = Rects(SideButtons))
                using (var wb = new SolidBrush(lightTaskbar ? WheelOnLight : WheelOnDark))
                using (var sb = new SolidBrush(asleep || unknown ? AsleepFill : SideButton))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    wheel.Transform(xf);
                    side.Transform(xf);
                    g.FillPath(wb, wheel);
                    g.FillPath(sb, side);
                }
            }
            return bmp;
        }

        // Rotate the design about its centre, then scale and centre it so the mouse (with its side buttons)
        // spans the whole canvas along its larger extent (both extents at 45 degrees).
        static Matrix Placement(int S, float tilt)
        {
            RectangleF b;
            using (var probe = Shell(Body))
            using (var side = Rects(SideButtons))
            using (var rot = new Matrix())
            {
                probe.AddPath(side, false);
                rot.RotateAt(tilt, new PointF(Cx, 10));
                probe.Transform(rot);
                b = probe.GetBounds();
            }
            float s = S / Math.Max(b.Width, b.Height);
            var m = new Matrix();
            m.Translate(S / 2f, S / 2f);
            m.Scale(s, s);
            m.Translate(-(b.X + b.Width / 2f), -(b.Y + b.Height / 2f));
            m.RotateAt(tilt, new PointF(Cx, 10));
            return m;
        }

        // Top-view outline: a superellipse (n = 2.5) matches the Hypace's outline measured from the product photo.
        static GraphicsPath Shell(RectangleF box)
        {
            const int N = 240;
            const double n = 2.5;
            var pts = new PointF[N];
            for (int i = 0; i < N; i++)
            {
                double t = 2 * Math.PI * i / N, c = Math.Cos(t), s = Math.Sin(t);
                double x = Math.Sign(c) * Math.Pow(Math.Abs(c), 2 / n), y = Math.Sign(s) * Math.Pow(Math.Abs(s), 2 / n);
                pts[i] = new PointF((float)(box.X + box.Width * (0.5 + 0.5 * x)), (float)(box.Y + box.Height * (0.5 + 0.5 * y)));
            }
            var p = new GraphicsPath();
            p.AddPolygon(pts);
            return p;
        }

        static RectangleF Inset(RectangleF r, float d) { return new RectangleF(r.X + d, r.Y + d, r.Width - 2 * d, r.Height - 2 * d); }

        static GraphicsPath Rects(params RectangleF[] rects)
        {
            var p = new GraphicsPath();
            foreach (var r in rects) p.AddRectangle(r);
            return p;
        }

        // Paints the black lines, all line wide (design units): the outline round the whole shell, hairline at the
        // tip, wheel well, centre channel (on into the outline at the tail) and the Λ button seams (apex forward,
        // at the channel). Drawn as anti-aliased masks (the inner lines kept inside the shell)
        // and laid over the shell in black.
        static void DrawLines(Bitmap bmp, Matrix xf, float scale, float line)
        {
            int S = bmp.Width;
            float channelEnd = Body.Bottom;   // runs on into the outline at the tail
            using (var mask = new Bitmap(S, S, PixelFormat.Format32bppArgb))
            using (var inside = new Bitmap(S, S, PixelFormat.Format32bppArgb))
            using (var outline = new Bitmap(S, S, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(outline))
                using (var ring = new GraphicsPath(FillMode.Alternate))
                using (var outer = Shell(Body))
                using (var inner = Shell(Inset(Body, line)))
                using (var white = new SolidBrush(Color.White))
                {
                    ring.AddPath(outer, false);
                    ring.AddPath(inner, false);
                    ring.Transform(xf);
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.FillPath(white, ring);
                }
                using (var g = Graphics.FromImage(mask))
                using (var holes = Rects(new RectangleF(Cx - line / 2, 0, line, 1.6f), new RectangleF(9, 1.6f, 3, 3.6f),
                                         new RectangleF(Cx - line / 2, 5.2f, line, channelEnd - 5.2f)))
                using (var seams = new GraphicsPath())
                using (var white = new SolidBrush(Color.White))
                using (var pen = new Pen(Color.White, line * scale))
                {
                    seams.StartFigure();
                    seams.AddLine(Cx - 0.8f, SeamApex, Left - 0.4f, SeamSide);
                    seams.StartFigure();
                    seams.AddLine(Cx + 0.8f, SeamApex, Right + 0.4f, SeamSide);
                    holes.Transform(xf);
                    seams.Transform(xf);
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.FillPath(white, holes);
                    g.DrawPath(pen, seams);
                }
                using (var g = Graphics.FromImage(inside))
                using (var shell = Shell(Body))
                using (var white = new SolidBrush(Color.White))
                {
                    shell.Transform(xf);
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.FillPath(white, shell);
                }
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        double lines = mask.GetPixel(x, y).A / 255.0 * inside.GetPixel(x, y).A / 255.0;
                        double la = 1 - (1 - lines) * (1 - outline.GetPixel(x, y).A / 255.0);
                        if (la == 0) continue;
                        // black over what is there
                        Color c = bmp.GetPixel(x, y);
                        double ca = c.A / 255.0, outA = la + ca * (1 - la), keep = ca * (1 - la) / outA;
                        bmp.SetPixel(x, y, Color.FromArgb(RoundInt(outA * 255), RoundInt(c.R * keep), RoundInt(c.G * keep), RoundInt(c.B * keep)));
                    }
            }
        }

        static int RoundInt(double v) { return (int)Math.Round(v, MidpointRounding.AwayFromZero); }
    }

    // Digit outlines for the icons.
    public static class IconText
    {
        // Text outline scaled as large as fits in the box, centred.
        internal static GraphicsPath TextPath(string text, RectangleF box)
        {
            var p = new GraphicsPath();
            using (var family = PickFont())
            {
                // never Bold: for the light weights GDI+ offers a much heavier Bold (2.4 px stems instead of 1.4)
                var style = family.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular : FontStyle.Bold;
                p.AddString(text, family, (int)style, 100f, PointF.Empty, StringFormat.GenericTypographic);
            }
            var r = p.GetBounds();
            float scale = Math.Min(box.Width / r.Width, box.Height / r.Height);
            using (var m = new Matrix())
            {
                m.Translate(box.X + box.Width / 2f, box.Y + box.Height / 2f);
                m.Scale(scale, scale);
                m.Translate(-(r.X + r.Width / 2f), -(r.Y + r.Height / 2f));
                p.Transform(m);
            }
            return p;
        }

        // Stroke width of the Windows 11 taskbar's own icons and text (中, 拼, network, volume, "60%", the clock),
        // measured on the user's taskbar: 1.5 px at a 20 px icon. Our digits and the mouse rim match it.
        internal const float TaskbarStroke = 1.5f;

        // Condensed digits are the tallest for a given width; the Light weight gives 1.4 px stems at 14 of 20 px,
        // the closest to TaskbarStroke (SemiLight 1.6, Regular 1.8, SemiBold 2.1).
        internal static FontFamily PickFont()
        {
            return PickFont("Bahnschrift Light Condensed", "Bahnschrift SemiLight Condensed", "Bahnschrift Condensed", "Bahnschrift", "Segoe UI");
        }

        // First installed family from the list, else the system sans-serif.
        internal static FontFamily PickFont(params string[] candidates)
        {
            foreach (var name in candidates)
            {
                try { return new FontFamily(name); }
                catch (ArgumentException) { }
            }
            return new FontFamily(GenericFontFamilies.SansSerif);
        }
    }

    // Fill colour per battery level: green → yellow-green → yellow → orange → red, interpolated in OKLab
    // so every percent shifts the colour a little without muddy midpoints.
    public static class BatteryPalette
    {
        public static readonly Color Charging = Color.FromArgb(0x0A, 0x84, 0xFF);
        public static readonly Color Ink = Color.FromArgb(0x12, 0x12, 0x12);   // digits drawn over the fill

        static readonly int[] StopLevels = { 100, 70, 45, 25, 10 };
        static readonly Color[] StopColors =
        {
            Color.FromArgb(0x30, 0xD1, 0x58),
            Color.FromArgb(0xA6, 0xD8, 0x3A),
            Color.FromArgb(0xFF, 0xD6, 0x0A),
            Color.FromArgb(0xFF, 0x9F, 0x0A),
            Color.FromArgb(0xFF, 0x45, 0x3A),
        };

        public static Color ForLevel(int level)
        {
            level = Math.Max(0, Math.Min(100, level));
            if (level >= StopLevels[0]) return StopColors[0];
            for (int i = 1; i < StopLevels.Length; i++)
            {
                if (level < StopLevels[i]) continue;
                if (level == StopLevels[i]) return StopColors[i];
                double t = (double)(level - StopLevels[i]) / (StopLevels[i - 1] - StopLevels[i]);
                double[] lo = ToLab(StopColors[i]), hi = ToLab(StopColors[i - 1]);
                return FromLab(lo[0] + (hi[0] - lo[0]) * t, lo[1] + (hi[1] - lo[1]) * t, lo[2] + (hi[2] - lo[2]) * t);
            }
            return StopColors[StopColors.Length - 1];
        }

        // Same hue, OKLab lightness capped at maxL: keeps orange/red digits readable on a light taskbar.
        internal static Color Darkened(Color c, double maxL)
        {
            double[] lab = ToLab(c);
            return lab[0] <= maxL ? c : FromLab(maxL, lab[1], lab[2]);
        }

        // ---- sRGB <-> OKLab (Björn Ottosson) ----
        static double ToLinear(int c) { double v = c / 255.0; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        static int ToSrgb(double v)
        {
            v = v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(Math.Max(v, 0), 1 / 2.4) - 0.055;
            return Math.Max(0, Math.Min(255, (int)Math.Round(v * 255)));
        }

        static double[] ToLab(Color c)
        {
            double r = ToLinear(c.R), g = ToLinear(c.G), b = ToLinear(c.B);
            double l = Math.Pow(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b, 1 / 3.0);
            double m = Math.Pow(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b, 1 / 3.0);
            double s = Math.Pow(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b, 1 / 3.0);
            return new[]
            {
                0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
                1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
                0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s,
            };
        }

        static Color FromLab(double L, double a, double b)
        {
            double l = Math.Pow(L + 0.3963377774 * a + 0.2158037573 * b, 3);
            double m = Math.Pow(L - 0.1055613458 * a - 0.0638541728 * b, 3);
            double s = Math.Pow(L - 0.0894841775 * a - 1.2914855480 * b, 3);
            return Color.FromArgb(
                ToSrgb(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s),
                ToSrgb(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s),
                ToSrgb(-0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s));
        }
    }
}
