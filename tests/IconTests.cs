// Minimal test runner for the tray icons (no test framework needed; built by build.ps1).
using System;
using System.Collections.Generic;
using System.Drawing;

namespace HypaceTray.Tests
{
    static class IconTests
    {
        static readonly List<KeyValuePair<string, Action>> tests = new List<KeyValuePair<string, Action>>();
        static void Test(string name, Action body) { tests.Add(new KeyValuePair<string, Action>(name, body)); }

        static void Check(bool cond, string msg) { if (!cond) throw new Exception(msg); }
        static Color Hex(string h) { return ColorTranslator.FromHtml(h); }
        static bool SameRgb(Color a, Color b) { return a.R == b.R && a.G == b.G && a.B == b.B; }
        static bool NearRgb(Color a, Color b, int tol) { return Math.Abs(a.R - b.R) <= tol && Math.Abs(a.G - b.G) <= tol && Math.Abs(a.B - b.B) <= tol; }
        static string Rgb(Color c) { return string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B); }
        static void CheckRgb(Color actual, string expected, string what)
        {
            Check(SameRgb(actual, Hex(expected)), what + ": expected " + expected + " got " + Rgb(actual));
        }

        // bounding box of everything drawn (A > 0): x0, y0, x1, y1
        static int[] Bounds(Bitmap b)
        {
            int x0 = b.Width, y0 = b.Height, x1 = -1, y1 = -1;
            for (int x = 0; x < b.Width; x++)
                for (int y = 0; y < b.Height; y++)
                    if (b.GetPixel(x, y).A > 0) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
            return new[] { x0, y0, x1, y1 };
        }

        static bool Any(Bitmap b, Func<Color, bool> pred, int yFrom, int yTo)
        {
            for (int y = Math.Max(0, yFrom); y <= Math.Min(b.Height - 1, yTo); y++)
                for (int x = 0; x < b.Width; x++)
                    if (pred(b.GetPixel(x, y))) return true;
            return false;
        }

        // row (in from..to) where column x is most see-through: where a seam crosses it
        // light a pixel gives off over a black background: low for a dark line and for a see-through gap alike
        static int Lit(Color c) { return Math.Max(c.R, Math.Max(c.G, c.B)) * c.A / 255; }

        static int MinLitRow(Bitmap b, int x, int from, int to)
        {
            int best = from;
            for (int y = from; y <= to; y++) if (Lit(b.GetPixel(x, y)) < Lit(b.GetPixel(x, best))) best = y;
            return best;
        }

        // rows holding clearly inked pixels (alpha >= 128); 1.4 px strokes seldom produce fully opaque pixels
        static int InkRows(Bitmap b)
        {
            int rows = 0;
            for (int y = 0; y < b.Height; y++)
            {
                bool any = false;
                for (int x = 0; x < b.Width && !any; x++) any = b.GetPixel(x, y).A >= 128;
                if (any) rows++;
            }
            return rows;
        }

        static double Luminance(Color c)
        {
            Func<int, double> lin = v => { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); };
            return 0.2126 * lin(c.R) + 0.7152 * lin(c.G) + 0.0722 * lin(c.B);
        }

        static double Contrast(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        static void CheckSameImage(Bitmap a, Bitmap b, string what)
        {
            using (a) using (b)
                for (int x = 0; x < a.Width; x++)
                    for (int y = 0; y < a.Height; y++)
                        Check(a.GetPixel(x, y) == b.GetPixel(x, y), what + " differ at " + x + "," + y);
        }

        static readonly int[] Sizes = { 16, 20, 24, 32 };

        static readonly Color TaskbarDark = Color.FromArgb(0x1F, 0x1F, 0x1F);   // the user's dark taskbar, measured
        static readonly Color TaskbarLight = Color.FromArgb(0xEE, 0xEE, 0xEE);  // Windows 11 light taskbar

        static double[] Vec(Color c) { return new double[] { c.R, c.G, c.B }; }

        // the icon over a solid taskbar colour at a fractional point (bilinear), as the eye sees it
        static double[] ColourOver(Bitmap b, Color bg, double x, double y)
        {
            int x0 = (int)Math.Floor(x - 0.5), y0 = (int)Math.Floor(y - 0.5);
            double fx = x - 0.5 - x0, fy = y - 0.5 - y0;
            var sum = new double[3];
            for (int j = 0; j < 2; j++)
                for (int i = 0; i < 2; i++)
                {
                    int xx = Math.Max(0, Math.Min(b.Width - 1, x0 + i)), yy = Math.Max(0, Math.Min(b.Height - 1, y0 + j));
                    Color c = b.GetPixel(xx, yy);
                    double a = c.A / 255.0, wt = (i == 0 ? 1 - fx : fx) * (j == 0 ? 1 - fy : fy);
                    sum[0] += (c.R * a + bg.R * (1 - a)) * wt;
                    sum[1] += (c.G * a + bg.G * (1 - a)) * wt;
                    sum[2] += (c.B * a + bg.B * (1 - a)) * wt;
                }
            return sum;
        }

        // how far p has gone from colour a towards colour b (0..1)
        static double Toward(double[] p, double[] a, double[] b)
        {
            double num = 0, den = 0;
            for (int i = 0; i < 3; i++) { num += (p[i] - a[i]) * (b[i] - a[i]); den += (b[i] - a[i]) * (b[i] - a[i]); }
            return den == 0 ? 0 : Math.Max(0, Math.Min(1, num / den));
        }

        // Width of the black line along the mouse's right edge, crossed at right angles 2 px behind the canvas centre
        // (clear of the seams): the ink towards black summed across it, against the colour inside on its inner side
        // and the taskbar on its outer side. No line at all gives 0.
        static double EdgeLineWidth(Bitmap b, Color bg)
        {
            double c = b.Width / 2.0, x0 = c - 0.7071 * 2, y0 = c + 0.7071 * 2, step = 0.05, tDark = 0, darkest = double.MaxValue;
            for (double t = 3; t <= 9; t += step)
            {
                var p = ColourOver(b, bg, x0 + 0.7071 * t, y0 + 0.7071 * t);
                double l = 0.2126 * p[0] + 0.7152 * p[1] + 0.0722 * p[2];
                if (l < darkest) { darkest = l; tDark = t; }
            }
            double[] inside = ColourOver(b, bg, x0 + 0.7071 * (tDark - 2.4), y0 + 0.7071 * (tDark - 2.4)), outside = Vec(bg);
            double w = 0;
            for (double t = tDark - 2; t <= tDark + 3; t += step)
                w += Toward(ColourOver(b, bg, x0 + 0.7071 * t, y0 + 0.7071 * t), t < tDark ? inside : outside, new double[3]) * step;
            return w;
        }

        // Width of the black line crossed through the canvas centre along (nx, ny), over the taskbar colour: its ink
        // summed across it, against the fill on both sides. Anti-aliasing spreads a slanted line but keeps its area,
        // so this is the line's width whatever its angle.
        static double DipWidth(Bitmap b, Color bg, double nx, double ny)
        {
            double c = b.Width / 2.0, step = 0.05, w = 0;
            double[] side1 = ColourOver(b, bg, c + nx * 3, c + ny * 3), side2 = ColourOver(b, bg, c - nx * 3, c - ny * 3);
            var fill = new double[3];
            for (int i = 0; i < 3; i++) fill[i] = (side1[i] + side2[i]) / 2;
            for (double t = -2.5; t <= 2.5; t += step)
                w += Toward(ColourOver(b, bg, c + nx * t, c + ny * t), fill, new double[3]) * step;
            return w;
        }

        // Stroke width of vertical stems between columns x0..x1: the ink (alpha) summed across each short horizontal
        // run, median over all runs. The Windows 11 taskbar icons and text measure about 1.5 px this way at 125%.
        static double StemWidth(Bitmap b, int x0, int x1)
        {
            var runs = new List<double>();
            for (int y = 0; y < b.Height; y++)
            {
                double sum = 0; int len = 0;
                for (int x = x0; x <= x1 + 1; x++)
                {
                    double c = x <= x1 ? b.GetPixel(x, y).A / 255.0 : 0;
                    if (c > 0.15) { sum += c; len++; }
                    else { if (len > 0 && len <= 5) runs.Add(sum); sum = 0; len = 0; }
                }
            }
            runs.Sort();
            return runs.Count == 0 ? 0 : runs[runs.Count / 2];
        }

        // A stand-in for a Lofree product photo (the real ones are never part of the repo): flat (246,246,246)
        // background; a grey 320 x 620 ellipse mouse, its front (top 80 px) blue, an orange side button on its left,
        // and a background-coloured patch enclosed inside the mouse.
        static Bitmap SyntheticPhoto()
        {
            var b = new Bitmap(1000, 1000, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            using (var bg = new SolidBrush(Color.FromArgb(246, 246, 246)))
            using (var grey = new SolidBrush(Color.FromArgb(120, 120, 120)))
            using (var blue = new SolidBrush(Color.FromArgb(40, 90, 200)))
            using (var orange = new SolidBrush(Color.FromArgb(255, 90, 31)))
            {
                g.FillRectangle(bg, 0, 0, 1000, 1000);
                g.FillEllipse(grey, 340, 205, 320, 620);
                g.SetClip(new Rectangle(0, 0, 1000, 285));
                g.FillEllipse(blue, 340, 205, 320, 620);
                g.ResetClip();
                g.FillRectangle(orange, 330, 430, 16, 51);
                g.FillRectangle(bg, 480, 600, 41, 41);
            }
            return b;
        }

        static MousePhoto syntheticMouse;
        static MousePhoto SyntheticMouse()
        {
            if (syntheticMouse == null)
                using (var photo = SyntheticPhoto())
                using (var cut = RealMouse.Cutout(photo))
                    syntheticMouse = new MousePhoto(cut);
            return syntheticMouse;
        }

        // rows that have a pixel matching, from the top
        static List<int> RowsWith(Bitmap b, Func<Color, bool> pred)
        {
            var rows = new List<int>();
            for (int y = 0; y < b.Height; y++)
                for (int x = 0; x < b.Width; x++)
                    if (pred(b.GetPixel(x, y))) { rows.Add(y); break; }
            return rows;
        }

        static List<Color> PixelsInRows(Bitmap b, IEnumerable<int> rows, Func<Color, bool> pred)
        {
            var list = new List<Color>();
            foreach (int y in rows)
                for (int x = 0; x < b.Width; x++)
                    if (pred(b.GetPixel(x, y))) list.Add(b.GetPixel(x, y));
            return list;
        }

        static int Spread(Color c) { return Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B)); }

        // pixels at x >= x0, y >= y0 that match
        static int CountIn(Bitmap b, int x0, int y0, Func<Color, bool> pred)
        {
            int n = 0;
            for (int y = Math.Max(0, y0); y < b.Height; y++)
                for (int x = Math.Max(0, x0); x < b.Width; x++)
                    if (pred(b.GetPixel(x, y))) n++;
            return n;
        }

        static int Main()
        {
            // ---------- palette ----------
            Test("PaletteAnchors", () =>
            {
                CheckRgb(BatteryPalette.ForLevel(100), "#30D158", "100");
                CheckRgb(BatteryPalette.ForLevel(70), "#A6D83A", "70");
                CheckRgb(BatteryPalette.ForLevel(45), "#FFD60A", "45");
                CheckRgb(BatteryPalette.ForLevel(25), "#FF9F0A", "25");
                CheckRgb(BatteryPalette.ForLevel(10), "#FF453A", "10");
                CheckRgb(BatteryPalette.ForLevel(0), "#FF453A", "0");
            });

            Test("PaletteSmooth", () =>
            {
                for (int l = 11; l <= 100; l++)
                {
                    Color a = BatteryPalette.ForLevel(l - 1), b = BatteryPalette.ForLevel(l);
                    int d = Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
                    Check(d <= 12, "step " + (l - 1) + "->" + l + " jumps " + d);
                }
                Color c85 = BatteryPalette.ForLevel(85);
                Check(!SameRgb(c85, BatteryPalette.ForLevel(70)) && !SameRgb(c85, BatteryPalette.ForLevel(100)), "85 must be an in-between colour, got " + Rgb(c85));
            });

            // ---------- number icon (left): the percentage, as large as the square allows ----------
            Test("NumberWhiteOnDarkTaskbar", () =>
            {
                using (var b = NumberIconPainter.Paint(62, IconState.Normal, false, 20))
                    Check(Any(b, p => p.A == 255 && SameRgb(p, Hex("#F5F5F5")), 0, 19), "no white digit pixel");
            });

            Test("NumberDarkOnLightTaskbar", () =>
            {
                using (var b = NumberIconPainter.Paint(62, IconState.Normal, true, 20))
                {
                    Check(Any(b, p => p.A == 255 && SameRgb(p, Hex("#1C1C1C")), 0, 19), "no dark digit pixel");
                    Check(!Any(b, p => p.A > 200 && p.R > 200 && p.G > 200 && p.B > 200, 0, 19), "light digit pixel on a light taskbar");
                }
            });

            Test("NumberIsLarge", () =>
            {
                // bigger than the system's own "60%" text (about 11 px at 125%)
                using (var b = NumberIconPainter.Paint(88, IconState.Normal, false, 20))
                    Check(InkRows(b) >= 12, "two digits only " + InkRows(b) + " px tall");
                using (var b = NumberIconPainter.Paint(100, IconState.Normal, false, 20))
                {
                    var r = Bounds(b);
                    Check(r[2] - r[0] + 1 >= 16, "100 only " + (r[2] - r[0] + 1) + " px wide");
                    Check(InkRows(b) >= 9, "100 only " + InkRows(b) + " px tall");
                }
            });

            Test("NumberFitsAtAllSizes", () =>
            {
                foreach (int s in Sizes)
                    foreach (int lvl in new[] { 7, 88, 100 })
                        using (var b = NumberIconPainter.Paint(lvl, IconState.Normal, false, s))
                        {
                            var r = Bounds(b);
                            Check(r[0] >= 0 && r[2] <= s - 1 && r[2] >= r[0], s + "px " + lvl + ": nothing drawn or clipped");
                            Check(Math.Abs(r[0] + r[2] - (s - 1)) <= 2 && Math.Abs(r[1] + r[3] - (s - 1)) <= 2, s + "px " + lvl + ": not centred");
                        }
            });

            Test("LowNumberTakesLevelColor", () =>
            {
                Color want = BatteryPalette.ForLevel(15);
                using (var b = NumberIconPainter.Paint(15, IconState.Normal, false, 20))
                    Check(Any(b, p => p.A == 255 && SameRgb(p, want), 0, 19), "no digit pixel in level colour " + Rgb(want));
            });

            Test("LowNumberReadableOnLightTaskbar", () =>
            {
                Color behind = Hex("#EEEEEE");
                foreach (int lvl in new[] { 20, 15, 10, 5 })
                    using (var b = NumberIconPainter.Paint(lvl, IconState.Normal, true, 20))
                    {
                        int seen = 0;
                        for (int x = 0; x < 20; x++)
                            for (int y = 0; y < 20; y++)
                            {
                                Color p = b.GetPixel(x, y);
                                if (p.A != 255) continue;
                                seen++;
                                Check(Contrast(p, behind) >= 3.0, lvl + "%: digit " + Rgb(p) + " contrast " + Contrast(p, behind).ToString("0.00"));
                            }
                        Check(seen > 0, lvl + "%: no opaque digit pixels");
                    }
            });

            Test("NumberAsleepIsGray", () =>
            {
                using (var b = NumberIconPainter.Paint(62, IconState.Asleep, false, 20))
                {
                    Check(Any(b, p => p.A == 255 && SameRgb(p, Hex("#A0A0A0")), 0, 19), "no grey digit pixel while asleep");
                    Check(!Any(b, p => p.A > 200 && p.R > 230 && p.G > 230 && p.B > 230, 0, 19), "white digit pixel while asleep");
                }
            });

            Test("NumberUnknownShowsDashes", () =>
            {
                using (var b = NumberIconPainter.Paint(62, IconState.Unknown, false, 20))
                {
                    Check(Any(b, p => p.A >= 128 && SameRgb(p, Hex("#A0A0A0")), 0, 19), "no grey dash pixel");
                    Check(!Any(b, p => p.A == 255 && SameRgb(p, Hex("#F5F5F5")), 0, 19), "white digits while unknown");
                }
            });

            Test("NumberClampsOutOfRange", () =>
            {
                CheckSameImage(NumberIconPainter.Paint(255, IconState.Normal, false, 20), NumberIconPainter.Paint(100, IconState.Normal, false, 20), "255 vs 100");
                CheckSameImage(NumberIconPainter.Paint(-5, IconState.Normal, false, 20), NumberIconPainter.Paint(0, IconState.Normal, false, 20), "-5 vs 0");
            });

            // ---------- mouse icon (right): the Hypace seen from above, tilted so it can be drawn larger ----------
            Test("MouseFillsCanvas", () =>
            {
                foreach (int s in Sizes)
                    using (var b = MouseIconPainter.Paint(50, IconState.Normal, false, s))
                    {
                        var r = Bounds(b);
                        int w = r[2] - r[0] + 1, h = r[3] - r[1] + 1;
                        Check(Math.Max(w, h) >= s - 2, s + "px: mouse spans only " + w + "x" + h);
                        Check(Math.Abs(r[0] + r[2] - (s - 1)) <= 2 && Math.Abs(r[1] + r[3] - (s - 1)) <= 2, s + "px: mouse not centred " + r[0] + "," + r[1] + ".." + r[2] + "," + r[3]);
                    }
            });

            Test("MouseLiesAlongDiagonal", () =>
            {
                // tilted 45 degrees the mouse is as large as the square allows: it reaches all four edges
                foreach (int s in Sizes)
                    using (var b = MouseIconPainter.Paint(100, IconState.Normal, false, s))
                    {
                        var r = Bounds(b);
                        int w = r[2] - r[0] + 1, h = r[3] - r[1] + 1;
                        Check(w >= s && h >= s, s + "px: mouse spans only " + w + "x" + h + ", should touch every edge");
                    }
            });

            Test("MouseIsTiltedFrontUpRight", () =>
            {
                // the wheel (front) sits up and to the right of the tail
                using (var b = MouseIconPainter.Paint(100, IconState.Normal, false, 20))
                {
                    var r = Bounds(b);
                    double topX = 0, topN = 0, botX = 0, botN = 0;
                    for (int x = 0; x < 20; x++)
                    {
                        for (int y = r[1]; y < r[1] + 4; y++) if (b.GetPixel(x, y).A > 128) { topX += x; topN++; }
                        for (int y = r[3] - 3; y <= r[3]; y++) if (b.GetPixel(x, y).A > 128) { botX += x; botN++; }
                    }
                    Check(topN > 0 && botN > 0 && topX / topN - botX / botN >= 3, "top centre " + (topX / topN).ToString("0.0") + " vs bottom " + (botX / botN).ToString("0.0"));
                }
            });

            Test("MouseFillRisesFromBottom", () =>
            {
                Color fill = BatteryPalette.ForLevel(50);
                using (var b = MouseIconPainter.Paint(50, IconState.Normal, false, 20))
                {
                    var r = Bounds(b);
                    int h = r[3] - r[1];
                    Check(Any(b, p => p.A == 255 && SameRgb(p, fill), r[1] + h * 3 / 4, r[1] + h * 3 / 4), "no fill in the lower part");
                    Check(!Any(b, p => p.A > 0 && SameRgb(p, fill), r[1], r[1] + h / 4), "fill colour in the upper part at 50%");
                }
            });

            Test("MouseChargingIsBlue", () =>
            {
                using (var b = MouseIconPainter.Paint(50, IconState.Charging, false, 20))
                {
                    var r = Bounds(b);
                    Check(Any(b, p => p.A == 255 && SameRgb(p, BatteryPalette.Charging), r[1] + (r[3] - r[1]) * 3 / 4, r[3]), "no blue fill while charging");
                }
            });

            Test("MouseAsleepIsGray", () =>
            {
                using (var b = MouseIconPainter.Paint(50, IconState.Asleep, false, 20))
                {
                    var r = Bounds(b);
                    Check(Any(b, p => p.A == 255 && p.R == p.G && p.G == p.B && p.R > 60, r[1] + (r[3] - r[1]) * 3 / 4, r[3]), "no grey fill while asleep");
                    Check(!Any(b, p => p.A > 0 && (p.R != p.G || p.G != p.B), 0, 19), "coloured pixel while asleep (side buttons should be grey too)");
                }
            });

            Test("MouseUnknownHasNoFill", () =>
            {
                Color level = BatteryPalette.ForLevel(50);
                using (var b = MouseIconPainter.Paint(50, IconState.Unknown, false, 20))
                {
                    Check(!Any(b, p => p.A > 0 && SameRgb(p, level), 0, 19), "level-coloured pixel while unknown");
                    Check(Bounds(b)[2] >= 0, "mouse outline missing while unknown");
                }
            });

            Test("MouseSideButtonsOrange", () =>
            {
                using (var b = MouseIconPainter.Paint(50, IconState.Normal, false, 20))
                    Check(Any(b, p => p.A > 100 && NearRgb(p, Hex("#FF5A1F"), 6), 0, 19), "no orange side button");
            });

            Test("MouseHasNoLightOutline", () =>
            {
                // the user wants the mouse drawn in black lines only: no white edge
                foreach (int level in new[] { 0, 50, 100 })
                    using (var b = MouseIconPainter.Paint(level, IconState.Normal, false, 20))
                        Check(!Any(b, p => p.A >= 128 && p.R >= 200 && p.G >= 200 && p.B >= 200, 0, 19), level + "%: white outline");
            });

            Test("MouseOutlinedInBlack", () =>
            {
                // the outer edge is a black line as wide as the inner lines (2 px): measured crossing the right side
                // behind the seams, from the fill out to the taskbar, on both taskbar colours and at any level
                foreach (bool light in new[] { false, true })
                    foreach (int level in new[] { 0, 50, 100 })
                        using (var b = MouseIconPainter.Paint(level, IconState.Normal, light, 20))
                        {
                            double w = EdgeLineWidth(b, light ? TaskbarLight : TaskbarDark);
                            Check(w >= 1.75 && w <= 2.25, (light ? "light" : "dark") + " taskbar, " + level + "%: outer line is " + w.ToString("0.00") + " px");
                        }
            });

            Test("MouseLinesLookAsThickAsTaskbar", () =>
            {
                // A dark line on a bright fill looks thinner than a light line of the same width on the dark taskbar.
                // Equal visual weight (width x lightness contrast) with the taskbar's 1.5 px white strokes needs a
                // 2 px black line on the mouse's fill. Measured across the centre line of the real 20 px, 45° icon.
                using (var b = MouseIconPainter.Paint(100, IconState.Normal, false, 20))
                {
                    double w = DipWidth(b, TaskbarDark, 0.7071, 0.7071);
                    Check(w >= 1.75 && w <= 2.25, "centre line is " + w.ToString("0.00") + " px");
                }
            });

            Test("MouseWheelShows", () =>
            {
                // a grey wheel in the black well: neutral grey pixels near the front, up to the wheel colour (its
                // anti-aliased edges blend into the black around it)
                using (var b = MouseIconPainter.Paint(100, IconState.Normal, false, 20))
                    Check(Any(b, p => p.A > 150 && p.R == p.G && p.G == p.B && p.R >= 60 && p.R <= 0xB4 + 2, 0, 10), "no grey wheel near the front");
                using (var b = MouseIconPainter.Paint(100, IconState.Normal, true, 20))
                {
                    Check(Any(b, p => p.A > 150 && p.R == p.G && p.G == p.B && p.R >= 40 && p.R <= 0x8A + 2, 0, 10), "no wheel on a light taskbar");
                    Check(!Any(b, p => p.A > 150 && p.R == p.G && p.G == p.B && p.R > 0x8A + 2, 0, 10), "wheel should be the darker grey on a light taskbar");
                }
            });

            Test("MouseChannelShows", () =>
            {
                // the centre channel runs the length of the shell: the middle row dips to dark between the edges
                using (var b = MouseIconPainter.Paint(100, IconState.Normal, false, 20))
                {
                    var r = Bounds(b);
                    int y = (r[1] + r[3]) / 2, first = -1, last = -1, minLit = 255;
                    for (int x = 0; x < 20; x++) if (b.GetPixel(x, y).A == 255) { if (first < 0) first = x; last = x; }
                    for (int x = first + 1; x < last; x++) minLit = Math.Min(minLit, Lit(b.GetPixel(x, y)));
                    Check(minLit < 120, "middle row has no dark channel (darkest " + minLit + ")");
                }
            });

            Test("MouseLinesAreBlack", () =>
            {
                // the lines are painted black, not cut through to the taskbar: on a light taskbar they stay black
                using (var b = MouseIconPainter.Paint(100, IconState.Normal, true, 20))
                {
                    double darkest = 1;
                    for (double t = -2; t <= 2; t += 0.05)
                    {
                        var p = ColourOver(b, TaskbarLight, 10 + 0.7071 * t, 10 + 0.7071 * t);
                        darkest = Math.Min(darkest, (0.2126 * p[0] + 0.7152 * p[1] + 0.0722 * p[2]) / 255);
                    }
                    Check(darkest < 0.1, "centre line on a light taskbar is only as dark as " + darkest.ToString("0.00"));
                }
            });

            Test("MouseSeamsPointForward", () =>
            {
                // drawn upright (no tilt) the button seams form a Λ: highest next to the centre slot, lower at the sides.
                // 40 px so the seam slope spans several pixel rows.
                // thin (1 px) lines so the outline and the wheel well leave room to see the seams' slope
                using (var b = MouseIconPainter.Paint(100, IconState.Normal, false, 40, 0f, 1f))
                {
                    // row 30 crosses the rear shell behind the side buttons: outline at both edges, the dark centre
                    // channel in the middle
                    int first = -1, last = -1;
                    for (int x = 0; x < 40; x++) if (b.GetPixel(x, 30).A == 255) { if (first < 0) first = x; last = x; }
                    int slot = first + 4;
                    for (int x = first + 4; x <= last - 4; x++) if (Lit(b.GetPixel(x, 30)) < Lit(b.GetPixel(slot, 30))) slot = x;
                    int innerL = MinLitRow(b, slot - 4, 6, 20), outerL = MinLitRow(b, first + 4, 6, 20);
                    int innerR = MinLitRow(b, slot + 4, 6, 20), outerR = MinLitRow(b, last - 4, 6, 20);
                    Check(outerL > innerL, "left seam: row " + innerL + " near the centre vs " + outerL + " at the side");
                    Check(outerR > innerR, "right seam: row " + innerR + " near the centre vs " + outerR + " at the side");
                }
            });

            Test("MouseSeamsSweepBack", () =>
            {
                // the user wants a deeper Λ: the seams run back steeply, about 0.8 rows back per column outwards
                // (was 0.46). Upright, 80 px, thin lines; measured between 2 units out from the channel and 2 units in
                // from the edge, taken on a rear-shell row behind the side buttons.
                using (var b = MouseIconPainter.Paint(100, IconState.Normal, false, 80, 0f, 1f))
                {
                    int row = 64, first = -1, last = -1;
                    for (int x = 0; x < 80; x++) if (b.GetPixel(x, row).A == 255) { if (first < 0) first = x; last = x; }
                    int slot = first + 8;
                    for (int x = first + 8; x <= last - 8; x++) if (Lit(b.GetPixel(x, row)) < Lit(b.GetPixel(slot, row))) slot = x;
                    int innerL = slot - 8, outerL = first + 8, innerR = slot + 9, outerR = last - 8;
                    double slopeL = (MinLitRow(b, outerL, 12, 56) - MinLitRow(b, innerL, 12, 56)) / (double)(innerL - outerL);
                    double slopeR = (MinLitRow(b, outerR, 12, 56) - MinLitRow(b, innerR, 12, 56)) / (double)(outerR - innerR);
                    Check(slopeL >= 0.65 && slopeR >= 0.65, "seams slope back only " + slopeL.ToString("0.00") + " / " + slopeR.ToString("0.00") + " rows per column");
                }
            });

            Test("MouseCentreLineReachesTail", () =>
            {
                // the centre line runs on into the outline at the tail, no break of colour between them
                using (var b = MouseIconPainter.Paint(100, IconState.Normal, false, 40, 0f))
                {
                    int row = 24, first = -1, last = -1;
                    for (int x = 0; x < 40; x++) if (b.GetPixel(x, row).A == 255) { if (first < 0) first = x; last = x; }
                    // middle of the dark channel on that row
                    int x0 = -1, x1 = -1;
                    for (int x = first + 6; x <= last - 6; x++) if (Lit(b.GetPixel(x, row)) < 40) { if (x0 < 0) x0 = x; x1 = x; }
                    Check(x0 >= 0, "no channel on row " + row);
                    int cx = (x0 + x1) / 2, brightest = 0, at = -1;
                    for (int y = row; y < 40 && b.GetPixel(cx, y).A > 0; y++)
                        if (Lit(b.GetPixel(cx, y)) > brightest) { brightest = Lit(b.GetPixel(cx, y)); at = y; }
                    Check(brightest < 60, "colour between the centre line and the tail outline at row " + at + " (lit " + brightest + ")");
                }
            });

            Test("MouseClampsOutOfRange", () =>
            {
                CheckSameImage(MouseIconPainter.Paint(255, IconState.Normal, false, 20), MouseIconPainter.Paint(100, IconState.Normal, false, 20), "255 vs 100");
                CheckSameImage(MouseIconPainter.Paint(-5, IconState.Normal, false, 20), MouseIconPainter.Paint(0, IconState.Normal, false, 20), "-5 vs 0");
            });

            // ---------- state mapping / text helpers ----------
            Test("StateMapping", () =>
            {
                Check(TrayIconState.For(80, true, true, false) == IconState.Normal, "online -> Normal");
                Check(TrayIconState.For(80, true, true, true) == IconState.Charging, "charging -> Charging");
                Check(TrayIconState.For(80, false, true, false) == IconState.Asleep, "receiver ok, mouse offline -> Asleep");
                Check(TrayIconState.For(-1, false, true, false) == IconState.Unknown, "never read -> Unknown");
            });

            Test("MissingReceiverShowsUnknown", () =>
            {
                Check(TrayIconState.For(80, false, false, false) == IconState.Unknown, "receiver unplugged with a saved level must show '--'");
            });

            Test("DigitFontIsCondensed", () =>
            {
                bool installed = false;
                using (var fonts = new System.Drawing.Text.InstalledFontCollection())
                    foreach (var f in fonts.Families) installed |= f.Name == "Bahnschrift Light Condensed";
                if (!installed) return;
                using (var f = IconText.PickFont())
                    Check(f.Name == "Bahnschrift Light Condensed", "digit font is " + f.Name);
            });

            Test("DigitStrokeMatchesTaskbar", () =>
            {
                // the taskbar's own icons and text (中, 拼, network, volume, "60%", the clock) have 1.5 px strokes at 20 px
                foreach (int level in new[] { 88, 100 })
                    using (var b = BadgePainter.Paint(level, IconState.Normal, false, 20))
                    {
                        double w = StemWidth(b, 0, b.Width - 21);
                        Check(w >= 1.3 && w <= 1.6, level + ": digit stems are " + w.ToString("0.00") + " px");
                    }
            });

            Test("FontFallbackSkipsMissingFonts", () =>
            {
                using (var f = IconText.PickFont("NoSuchFont_HypaceTray", "Segoe UI"))
                    Check(f.Name == "Segoe UI", "expected Segoe UI, got " + f.Name);
                using (var f = IconText.PickFont("NoSuchFont_A", "NoSuchFont_B"))
                    Check(f != null && f.Name.Length > 0, "all candidates missing must still give a usable font");
            });

            Test("EmptyTextIsSafe", () =>
            {
                using (var p = IconText.TextPath("", new RectangleF(1, 1, 10, 8)))
                    Check(p.PointCount == 0, "empty text should give an empty path");
            });

            // ---------- merged taskbar block: number + mouse in one piece ----------
            Test("BadgeMouseIsLarger", () =>
            {
                // the block's mouse is 20% larger than a tray icon (24 px at 20) and sets the block's height
                foreach (int s in Sizes)
                    using (var b = BadgePainter.Paint(62, IconState.Normal, false, s))
                    {
                        int m = (int)Math.Round(s * 1.2);
                        Check(b.Height == m && b.Width > m, s + "px: badge is " + b.Width + "x" + b.Height + ", want " + m + " tall");
                    }
                using (var b = BadgePainter.Paint(62, IconState.Normal, false, 20))
                using (var mouse = b.Clone(new Rectangle(b.Width - 24, 0, 24, 24), b.PixelFormat))
                {
                    var r = Bounds(mouse);
                    Check(r[2] - r[0] + 1 >= 23 && r[3] - r[1] + 1 >= 23, "mouse spans " + (r[2] - r[0] + 1) + "x" + (r[3] - r[1] + 1) + " of 24");
                }
            });

            Test("BadgeMouseLinesKeepTaskbarWeight", () =>
            {
                // enlarging the mouse does not thicken its lines: still 2 px, like the tray icon's
                using (var b = BadgePainter.Paint(100, IconState.Normal, false, 20))
                using (var mouse = b.Clone(new Rectangle(b.Width - b.Height, 0, b.Height, b.Height), b.PixelFormat))
                {
                    double centre = DipWidth(mouse, TaskbarDark, 0.7071, 0.7071), edge = EdgeLineWidth(mouse, TaskbarDark);
                    Check(centre >= 1.75 && centre <= 2.25, "centre line is " + centre.ToString("0.00") + " px");
                    Check(edge >= 1.75 && edge <= 2.25, "outline is " + edge.ToString("0.00") + " px");
                }
            });

            Test("BadgeMouseOnTheRight", () =>
            {
                // the right-hand square is the mouse drawn at the block's size with the tray icon's line weight
                using (var b = BadgePainter.Paint(62, IconState.Normal, false, 20))
                using (var m = MouseIconPainter.Paint(62, IconState.Normal, false, 24, MouseIconPainter.Tilt, MouseIconPainter.LineWidth))
                    for (int x = 0; x < 24; x++)
                        for (int y = 0; y < 24; y++)
                        {
                            Color want = m.GetPixel(x, y), got = b.GetPixel(b.Width - 24 + x, y);
                            Check(want.A == 0 ? got.A <= 1 : got == want, "mouse pixel " + x + "," + y + " differs");
                        }
            });

            Test("BadgeNumberStaysPut", () =>
            {
                // the digits keep their size (14 of 20 px) and stay centred in the block, which is centred on the
                // taskbar, so they sit exactly where they did before the mouse grew
                foreach (int level in new[] { 7, 88, 100 })
                    using (var b = BadgePainter.Paint(level, IconState.Normal, false, 20))
                    using (var num = b.Clone(new Rectangle(0, 0, b.Width - b.Height, b.Height), b.PixelFormat))
                    {
                        int top = -1, bottom = -1;
                        for (int y = 0; y < num.Height; y++)
                            for (int x = 0; x < num.Width; x++)
                                if (num.GetPixel(x, y).A >= 128) { if (top < 0) top = y; bottom = y; }
                        Check(bottom - top + 1 >= 13 && bottom - top + 1 <= 15, level + ": number is " + (bottom - top + 1) + " rows tall");
                        Check(Math.Abs((top + bottom) / 2.0 - (b.Height - 1) / 2.0) <= 1, level + ": number rows " + top + ".." + bottom + " in a " + b.Height + " px block");
                    }
            });

            Test("BadgeNumberLeftOfMouse", () =>
            {
                // everything inked left of the mouse square is the number, in the text colour, clear of the mouse
                Color text = Hex("#F5F5F5");
                using (var b = BadgePainter.Paint(62, IconState.Normal, false, 20))
                {
                    int mouseLeft = b.Width - b.Height, inkRight = -1;
                    string other = null;
                    for (int x = 0; x < mouseLeft; x++)
                        for (int y = 0; y < b.Height; y++)
                        {
                            var c = b.GetPixel(x, y);
                            if (c.A < 128) continue;
                            inkRight = Math.Max(inkRight, x);
                            if (other == null && !NearRgb(c, text, 3)) other = Rgb(c) + " at " + x + "," + y;
                        }
                    Check(inkRight >= 0, "no number");
                    Check(other == null, "not the number's colour left of the mouse: " + other);
                    Check(inkRight < mouseLeft - 1, "number reaches x=" + inkRight + ", mouse starts at x=" + mouseLeft);
                }
            });

            Test("BadgeNumbersFullHeight", () =>
            {
                // with room to the side every number is drawn full height (14 of 20 px), "100" included
                foreach (int level in new[] { 7, 88, 100 })
                    using (var b = BadgePainter.Paint(level, IconState.Normal, false, 20))
                    using (var num = b.Clone(new Rectangle(0, 0, b.Width - b.Height, b.Height), b.PixelFormat))
                        Check(InkRows(num) >= 13, level + ": number is only " + InkRows(num) + " rows tall");
            });

            Test("BadgeWidthFollowsDigits", () =>
            {
                using (var a = BadgePainter.Paint(7, IconState.Normal, false, 20))
                using (var b = BadgePainter.Paint(88, IconState.Normal, false, 20))
                using (var c = BadgePainter.Paint(100, IconState.Normal, false, 20))
                    Check(a.Width < b.Width && b.Width < c.Width, "widths 7/88/100: " + a.Width + "/" + b.Width + "/" + c.Width);
            });

            Test("BadgeTakesClicksEverywhere", () =>
            {
                // a layered window lets clicks fall through fully transparent pixels: leave none
                using (var b = BadgePainter.Paint(88, IconState.Normal, false, 20))
                    for (int x = 0; x < b.Width; x++)
                        for (int y = 0; y < b.Height; y++)
                            Check(b.GetPixel(x, y).A >= 1, "see-through pixel at " + x + "," + y);
            });

            Test("BadgeOnLightTaskbar", () =>
            {
                using (var b = BadgePainter.Paint(62, IconState.Normal, true, 20))
                {
                    Check(Any(b, c => c.A == 255 && SameRgb(c, Hex("#1C1C1C")), 0, 19), "no dark number on a light taskbar");
                    Check(!Any(b, c => c.A == 255 && SameRgb(c, Hex("#F5F5F5")), 0, 19), "light number on a light taskbar");
                }
            });

            Test("BadgeLowNumberTakesLevelColor", () =>
            {
                Color want = BatteryPalette.ForLevel(15);
                using (var b = BadgePainter.Paint(15, IconState.Normal, false, 20))
                using (var num = b.Clone(new Rectangle(0, 0, b.Width - b.Height, b.Height), b.PixelFormat))
                    Check(Any(num, c => c.A == 255 && SameRgb(c, want), 0, num.Height - 1), "15 should be drawn in " + Rgb(want));
            });

            Test("BadgeUnknownShowsDashes", () =>
            {
                using (var b = BadgePainter.Paint(0, IconState.Unknown, false, 20))
                using (var num = b.Clone(new Rectangle(0, 0, b.Width - b.Height, b.Height), b.PixelFormat))
                {
                    Check(Any(num, c => c.A >= 128 && SameRgb(c, Hex("#A0A0A0")), 0, num.Height - 1), "no grey dashes");
                    Check(InkRows(num) <= 4, "dashes are " + InkRows(num) + " rows tall");
                }
            });

            Test("BadgeSitsLeftOfTray", () =>
            {
                // flush against the tray area, vertically centred, in taskbar coordinates (the badge is its child)
                Point p;
                Check(BadgeLayout.TryPlace(new Rectangle(0, 1380, 2560, 60), new Rectangle(2011, 1380, 549, 60), new Size(45, 20), out p), "not placed");
                Check(p == new Point(2011 - 45, 20), "placed at " + p);
                Check(BadgeLayout.TryPlace(new Rectangle(100, 0, 1900, 48), new Rectangle(1500, 0, 500, 48), new Size(40, 20), out p), "not placed");
                Check(p == new Point(1500 - 100 - 40, 14), "placed at " + p);
            });

            Test("BadgeCentresOnTraySlot", () =>
            {
                // over our placeholder tray icon, right of ^: centred on the slot, in taskbar coordinates
                var taskbar = new Rectangle(0, 1380, 2560, 60);
                Point p;
                Check(BadgeLayout.TryPlaceOnSlot(taskbar, new Rectangle(2090, 1380, 40, 60), new Size(38, 20), out p), "not placed");
                Check(p == new Point(2091, 20), "38 wide placed at " + p);
                Check(BadgeLayout.TryPlaceOnSlot(taskbar, new Rectangle(2090, 1380, 40, 60), new Size(44, 20), out p), "not placed");
                Check(p == new Point(2088, 20), "44 wide (100%) placed at " + p);
                Check(BadgeLayout.TryPlaceOnSlot(new Rectangle(100, 0, 1900, 48), new Rectangle(1500, 0, 32, 48), new Size(30, 20), out p), "not placed");
                Check(p == new Point(1401, 14), "offset taskbar placed at " + p);
            });

            Test("BadgeSlotMustBeOnTaskbar", () =>
            {
                var taskbar = new Rectangle(0, 1380, 2560, 60);
                Point p;
                Check(!BadgeLayout.TryPlaceOnSlot(taskbar, new Rectangle(2090, 1200, 40, 40), new Size(38, 20), out p), "slot in the ^ flyout, above the taskbar");
                Check(!BadgeLayout.TryPlaceOnSlot(taskbar, Rectangle.Empty, new Size(38, 20), out p), "no slot");
                Check(!BadgeLayout.TryPlaceOnSlot(new Rectangle(0, 0, 60, 1440), new Rectangle(10, 1000, 40, 40), new Size(38, 20), out p), "vertical taskbar");
            });

            Test("TrayIconInternalsAvailable", () =>
            {
                // the slot of our tray icon is looked up with its window and ID, private fields of NotifyIcon
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                Check(typeof(System.Windows.Forms.NotifyIcon).GetField("window", flags) != null, "NotifyIcon.window missing");
                Check(typeof(System.Windows.Forms.NotifyIcon).GetField("id", flags) != null, "NotifyIcon.id missing");
            });

            Test("BadgeNeedsRoomOnHorizontalTaskbar", () =>
            {
                Point p;
                Check(!BadgeLayout.TryPlace(new Rectangle(0, 0, 60, 1440), new Rectangle(0, 1000, 60, 440), new Size(45, 20), out p), "vertical taskbar");
                Check(!BadgeLayout.TryPlace(new Rectangle(0, 1380, 2560, 60), Rectangle.Empty, new Size(45, 20), out p), "no tray area");
                Check(!BadgeLayout.TryPlace(new Rectangle(0, 1380, 2560, 60), new Rectangle(30, 1380, 549, 60), new Size(45, 20), out p), "no room left of the tray");
            });

            // ---------- UI text: English and Chinese ----------
            Test("MenuTextInBothLanguages", () =>
            {
                UiText en = UiText.For(Language.English), zh = UiText.For(Language.Chinese);
                Check(en.Refresh == "Refresh now" && zh.Refresh == "立即刷新", "refresh: " + en.Refresh + " / " + zh.Refresh);
                Check(en.AutoStart == "Start with Windows" && zh.AutoStart == "开机自启动", "start with Windows: " + en.AutoStart + " / " + zh.AutoStart);
                Check(en.WebDriver == "Open the official web driver" && zh.WebDriver == "打开官方网页驱动", "web driver: " + en.WebDriver + " / " + zh.WebDriver);
                Check(en.LanguageMenu == "Language" && zh.LanguageMenu == "语言", "language: " + en.LanguageMenu + " / " + zh.LanguageMenu);
                Check(en.Exit == "Exit" && zh.Exit == "退出", "exit: " + en.Exit + " / " + zh.Exit);
                Check(en.Connecting == "Connecting…" && zh.Connecting == "正在连接…", "connecting");
            });

            Test("LanguageDefaultsToEnglish", () =>
            {
                Check(UiText.ParseLanguage(null) == Language.English, "nothing saved");
                Check(UiText.ParseLanguage("") == Language.English, "empty saved value");
                Check(UiText.ParseLanguage("nonsense") == Language.English, "unknown saved value");
            });

            Test("SavedLanguageWins", () =>
            {
                Check(UiText.ParseLanguage("en") == Language.English, "en");
                Check(UiText.ParseLanguage(" ZH ") == Language.Chinese, "zh");
                Check(UiText.FormatLanguage(Language.English) == "en" && UiText.FormatLanguage(Language.Chinese) == "zh", "format");
            });

            Test("SettingsFileKeepsOtherKeys", () =>
            {
                var s = SettingsText.Parse("iconSize=Large\r\n\r\n language = en \n");
                Check(s.Count == 2 && s["language"] == "en" && s["iconSize"] == "Large", "parse");
                s["language"] = "zh";
                var again = SettingsText.Parse(SettingsText.Format(s));
                Check(again.Count == 2 && again["language"] == "zh" && again["iconSize"] == "Large", "round trip");
                Check(SettingsText.Parse("").Count == 0 && SettingsText.Parse(null).Count == 0, "empty");
            });

            Test("StatusTextChinese", () =>
            {
                var t = UiText.For(Language.Chinese);
                DateTime seen = new DateTime(2026, 11, 19, 18, 52, 0), now = seen.AddMinutes(1);
                string status, tip;
                t.Describe(85, true, true, false, 4050, seen, now, out status, out tip);
                Check(status == "电量 85%  4.05V" && tip == "Hypace 85%\n更新于 18:52", "online: " + status + " | " + tip);
                t.Describe(48, true, true, true, 0, seen, now, out status, out tip);
                Check(status == "电量 48%（充电中）" && tip == "Hypace 48% 充电中\n更新于 18:52", "charging: " + status + " | " + tip);
                t.Describe(85, false, true, false, 0, seen, now.AddDays(1), out status, out tip);
                Check(status == "鼠标休眠/离线，上次 85%（11/19 18:52）" && tip == "Hypace 休眠中 · 上次 85%\n11/19 18:52", "asleep: " + status + " | " + tip);
                t.Describe(85, false, false, false, 0, seen, now, out status, out tip);
                Check(status == "未找到接收器，上次 85%" && tip == "Hypace：未找到接收器\n上次 85%", "receiver gone: " + status + " | " + tip);
                t.Describe(-1, false, true, false, 0, DateTime.MinValue, now, out status, out tip);
                Check(status == "鼠标未在线（晃动一下鼠标唤醒）" && tip == "Hypace：鼠标未在线（晃动一下鼠标唤醒）", "never online: " + status + " | " + tip);
                t.Describe(-1, false, false, false, 0, DateTime.MinValue, now, out status, out tip);
                Check(status == "未找到 Hypace 接收器" && tip == "Hypace：未找到 Hypace 接收器", "no receiver: " + status + " | " + tip);
            });

            Test("StatusTextEnglish", () =>
            {
                var t = UiText.For(Language.English);
                DateTime seen = new DateTime(2026, 11, 19, 18, 52, 0), now = seen.AddMinutes(1);
                string status, tip;
                t.Describe(85, true, true, false, 4050, seen, now, out status, out tip);
                Check(status == "Battery 85%  4.05V" && tip == "Hypace 85%\nUpdated 18:52", "online: " + status + " | " + tip);
                t.Describe(48, true, true, true, 0, seen, now, out status, out tip);
                Check(status == "Battery 48% (charging)" && tip == "Hypace 48% charging\nUpdated 18:52", "charging: " + status + " | " + tip);
                t.Describe(85, false, true, false, 0, seen, now.AddDays(1), out status, out tip);
                Check(status == "Mouse asleep or offline, last 85% (11/19 18:52)" && tip == "Hypace asleep · last 85%\n11/19 18:52", "asleep: " + status + " | " + tip);
                t.Describe(85, false, false, false, 0, seen, now, out status, out tip);
                Check(status == "Receiver not found, last 85%" && tip == "Hypace: receiver not found\nlast 85%", "receiver gone: " + status + " | " + tip);
                t.Describe(-1, false, true, false, 0, DateTime.MinValue, now, out status, out tip);
                Check(status == "Mouse not online (move it to wake it up)" && tip == "Hypace: mouse not online (move it to wake it up)", "never online: " + status + " | " + tip);
                t.Describe(-1, false, false, false, 0, DateTime.MinValue, now, out status, out tip);
                Check(status == "Hypace receiver not found" && tip == "Hypace: receiver not found", "no receiver: " + status + " | " + tip);
            });

            Test("AlertTextInBothLanguages", () =>
            {
                UiText en = UiText.For(Language.English), zh = UiText.For(Language.Chinese);
                Check(zh.LowTitle == "Hypace 电量低" && zh.LowText(18) == "剩余 18%。", "zh low: " + zh.LowTitle + " " + zh.LowText(18));
                Check(zh.CriticalTitle == "Hypace 电量严重不足" && zh.CriticalText(9) == "剩余 9%，请尽快充电。", "zh critical");
                Check(en.LowTitle == "Hypace battery low" && en.LowText(18) == "18% left.", "en low: " + en.LowTitle + " " + en.LowText(18));
                Check(en.CriticalTitle == "Hypace battery critically low" && en.CriticalText(9) == "9% left, please charge it soon.", "en critical");
            });

            Test("TipsFitTheTrayLimit", () =>
            {
                // NotifyIcon.Text takes at most 63 characters
                var seen = new DateTime(2026, 11, 19, 18, 52, 0);
                foreach (Language l in new[] { Language.English, Language.Chinese })
                    foreach (int lvl in new[] { -1, 100 })
                        foreach (bool on in new[] { true, false })
                            foreach (bool rx in new[] { true, false })
                                foreach (bool ch in new[] { true, false })
                                {
                                    string status, tip;
                                    UiText.For(l).Describe(lvl, on, rx, ch, 4110, seen, seen.AddDays(2), out status, out tip);
                                    Check(tip.Length <= 63, l + " tip has " + tip.Length + " characters: " + tip);
                                }
            });

            // ---------- charging bolt ----------
            Func<Color, bool> boltWhite = p => p.A >= 200 && p.R >= 240 && p.G >= 240 && p.B >= 240;
            Func<Color, bool> boltDark = p => p.A >= 200 && NearRgb(p, Hex("#1C1C1C"), 3);
            var boltLook = new MouseLook(null, LevelDisplay.GreyWhite, true);

            Test("BoltCutsAGapAndDraws", () =>
            {
                using (var b = new Bitmap(96, 96, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(b)) g.Clear(Color.FromArgb(255, 128, 128, 128));
                    ChargingBolt.Draw(b, false);
                    var poly = ChargingBolt.Polygon(96);
                    float h = poly[4].Y - poly[0].Y, left = poly[0].X - 0.38f * h, top = poly[0].Y, gap = 1.2f * 96 / 24;
                    Color inside = b.GetPixel((int)(left + 0.38f * h), (int)(top + 0.48f * h));
                    Check(inside.A == 255 && inside.R >= 250 && inside.G >= 250 && inside.B >= 250, "bolt not white inside: " + Rgb(inside) + " a" + inside.A);
                    Check(b.GetPixel(2, 2) == Color.FromArgb(255, 128, 128, 128), "far pixel changed");
                    Color gapPx = b.GetPixel((int)(left + 0.08f * h - 0.6f * gap), (int)(top + 0.56f * h));
                    Check(gapPx.A < 128, "no gap left of the bolt: alpha " + gapPx.A);
                }
            });

            Test("BoltShowsWhileCharging", () =>
            {
                using (var with = boltLook.PaintMouse(50, IconState.Charging, false, 24, 2.4f))
                using (var without = MouseLook.Drawn.PaintMouse(50, IconState.Charging, false, 24, 2.4f))
                {
                    int n = CountIn(with, 16, 12, boltWhite), n0 = CountIn(without, 16, 12, boltWhite);
                    Check(n >= 10 && n0 == 0, "white bolt pixels " + n + ", without the bolt " + n0);
                }
            });

            Test("BoltIsDarkOnLightTaskbar", () =>
            {
                using (var with = boltLook.PaintMouse(50, IconState.Charging, true, 24, 2.4f))
                using (var without = MouseLook.Drawn.PaintMouse(50, IconState.Charging, true, 24, 2.4f))
                {
                    int n = CountIn(with, 16, 12, boltDark), n0 = CountIn(without, 16, 12, boltDark);
                    Check(n - n0 >= 10, "dark bolt pixels " + n + ", without the bolt " + n0);
                    Check(CountIn(with, 16, 12, boltWhite) == 0, "white bolt on a light taskbar");
                }
            });

            Test("BoltOnlyWhileCharging", () =>
            {
                foreach (var st in new[] { IconState.Normal, IconState.Asleep, IconState.Unknown })
                    foreach (bool light in new[] { false, true })
                        using (var with = boltLook.PaintMouse(50, st, light, 24, 2.4f))
                        using (var without = MouseLook.Drawn.PaintMouse(50, st, light, 24, 2.4f))
                            CheckSameImage(with, without, st + (light ? " light" : " dark"));
            });

            Test("BadgeShowsTheBolt", () =>
            {
                using (var with = BadgePainter.Paint(50, IconState.Charging, false, 20, boltLook))
                using (var plain = BadgePainter.Paint(50, IconState.Charging, false, 20))
                {
                    int n = CountIn(with, with.Width - 9, 12, boltWhite), n0 = CountIn(plain, plain.Width - 9, 12, boltWhite);
                    Check(n >= 10 && n0 == 0, "white bolt pixels in the block " + n + ", default block " + n0);
                }
            });

            Test("BadgeDefaultLookUnchanged", () =>
            {
                foreach (int lvl in new[] { 0, 50, 100 })
                    foreach (IconState st in Enum.GetValues(typeof(IconState)))
                        using (var a = BadgePainter.Paint(lvl, st, false, 20))
                        using (var b = BadgePainter.Paint(lvl, st, false, 20, MouseLook.Drawn))
                            CheckSameImage(a, b, lvl + "% " + st);
            });

            Test("BadgeWithoutNumberIsTheMouse", () =>
            {
                foreach (int lvl in new[] { 0, 12, 62, 100 })
                    foreach (IconState st in Enum.GetValues(typeof(IconState)))
                        foreach (bool light in new[] { false, true })
                            using (var block = BadgePainter.Paint(lvl, st, light, 20, boltLook, false))
                            using (var mouse = boltLook.PaintMouse(lvl, st, light, 24, 2f))
                            {
                                string what = lvl + "% " + st + (light ? " light" : " dark");
                                Check(block.Width == 24 && block.Height == 24, what + ": block " + block.Width + "x" + block.Height);
                                // the same mouse; empty pixels stay barely there (alpha 1) so the block still takes clicks
                                for (int x = 0; x < 24; x++)
                                    for (int y = 0; y < 24; y++)
                                    {
                                        Color p = block.GetPixel(x, y), q = mouse.GetPixel(x, y);
                                        Check(q.A == 0 ? p.A == 1 : p == q, what + " differs at " + x + "," + y);
                                    }
                            }
            });

            Test("BadgeShowsNumberByDefault", () =>
            {
                foreach (IconState st in Enum.GetValues(typeof(IconState)))
                    using (var shown = BadgePainter.Paint(62, st, false, 20, boltLook, true))
                    using (var dflt = BadgePainter.Paint(62, st, false, 20, boltLook))
                    {
                        Check(shown.Width > 24 && shown.Width == dflt.Width, st + ": widths " + shown.Width + " / " + dflt.Width);
                        CheckSameImage(shown, dflt, st.ToString());
                    }
            });

            Test("LookPaintsTrayMouseToo", () =>
            {
                using (var m = boltLook.PaintMouse(50, IconState.Charging, false, 20, 2f))
                {
                    Check(m.Width == 20 && m.Height == 20, "size " + m.Width + "x" + m.Height);
                    int n = CountIn(m, 13, 10, boltWhite);
                    Check(n >= 6, "white bolt pixels in the 20 px tray mouse " + n);
                }
            });

            // ---------- realistic mouse (synthetic photo) ----------
            Func<Color, bool> opaque = p => p.A == 255, visible = p => p.A > 0;

            Test("CutoutFindsTheMouse", () =>
            {
                using (var photo = SyntheticPhoto())
                using (var cut = RealMouse.Cutout(photo))
                {
                    Check(Math.Abs(cut.Width - 331) <= 3 && Math.Abs(cut.Height - 621) <= 3, "cut-out " + cut.Width + "x" + cut.Height);
                    Check(cut.GetPixel(0, 0).A == 0 && cut.GetPixel(cut.Width - 1, cut.Height - 1).A == 0, "corners not transparent");
                    Color mid = cut.GetPixel(cut.Width / 2 + 5, cut.Height / 2);
                    Check(mid.A == 255 && NearRgb(mid, Color.FromArgb(120, 120, 120), 15), "middle " + Rgb(mid) + " a" + mid.A);
                }
            });

            Test("CutoutKeepsEnclosedLightParts", () =>
            {
                using (var photo = SyntheticPhoto())
                using (var cut = RealMouse.Cutout(photo))
                {
                    Color patch = cut.GetPixel(500 - 330, 620 - 205);
                    Check(patch.A == 255, "enclosed light patch was cut away");
                }
            });

            Test("LooksLikeMouseChecksSize", () =>
            {
                Check(RealMouse.LooksLikeMouse(331, 621), "the mouse");
                Check(RealMouse.LooksLikeMouse(326, 633), "Crayon Grey's size");
                Check(!RealMouse.LooksLikeMouse(1000, 1000), "whole photo (background not found)");
                Check(!RealMouse.LooksLikeMouse(120, 90), "small blob");
                Check(!RealMouse.LooksLikeMouse(600, 620), "too wide");
            });

            Test("RealMouseFillsTheSquare", () =>
            {
                foreach (int s in new[] { 20, 24, 32 })
                    using (var m = RealMouse.Paint(SyntheticMouse(), 100, IconState.Normal, LevelDisplay.GreyWhite, s))
                    {
                        var r = Bounds(m);
                        Check(m.Width == s && m.Height == s, s + "px: size " + m.Width + "x" + m.Height);
                        Check(r[0] <= 1 && r[1] <= 1 && r[2] >= s - 2 && r[3] >= s - 2, s + "px: spans " + r[0] + "," + r[1] + ".." + r[2] + "," + r[3]);
                    }
            });

            Test("RealMouseFrontPointsUpRight", () =>
            {
                using (var m = RealMouse.Paint(SyntheticMouse(), 100, IconState.Normal, LevelDisplay.GreyWhite, 24))
                {
                    double bx = 0, by = 0, bn = 0, ax = 0, ay = 0, an = 0;
                    for (int y = 0; y < 24; y++)
                        for (int x = 0; x < 24; x++)
                        {
                            Color p = m.GetPixel(x, y);
                            if (p.A < 128) continue;
                            ax += x; ay += y; an++;
                            if (p.B > p.R + 40 && p.B > p.G + 40) { bx += x; by += y; bn++; }
                        }
                    Check(bn > 0, "no blue front");
                    Check(bx / bn >= ax / an + 2 && by / bn <= ay / an - 2, "front at " + (bx / bn).ToString("0.0") + "," + (by / bn).ToString("0.0") + ", mouse centre " + (ax / an).ToString("0.0") + "," + (ay / an).ToString("0.0"));
                }
            });

            Test("GreyWhiteDrainsTheTop", () =>
            {
                using (var m = RealMouse.Paint(SyntheticMouse(), 50, IconState.Normal, LevelDisplay.GreyWhite, 24))
                {
                    var rows = RowsWith(m, opaque);
                    foreach (var p in PixelsInRows(m, rows.GetRange(rows.Count - 3, 3), opaque))
                        Check(Math.Max(p.R, Math.Max(p.G, p.B)) <= 150, "charged bottom pixel " + Rgb(p));
                    foreach (var p in PixelsInRows(m, rows.GetRange(0, 3), opaque))
                        Check(Math.Min(p.R, Math.Min(p.G, p.B)) >= 175 && Spread(p) <= 6, "drained top pixel " + Rgb(p));
                }
            });

            Test("SeeThroughDrainsTheTop", () =>
            {
                using (var m = RealMouse.Paint(SyntheticMouse(), 50, IconState.Normal, LevelDisplay.SeeThrough, 24))
                {
                    var rows = RowsWith(m, visible);
                    int bottomMax = 0, topMax = 0;
                    foreach (var p in PixelsInRows(m, rows.GetRange(rows.Count - 3, 3), visible)) bottomMax = Math.Max(bottomMax, p.A);
                    foreach (var p in PixelsInRows(m, rows.GetRange(0, 3), visible)) topMax = Math.Max(topMax, p.A);
                    Check(bottomMax >= 250 && topMax <= 90, "bottom alpha " + bottomMax + ", top alpha " + topMax);
                }
            });

            Test("LevelColourTintsTheCharge", () =>
            {
                using (var m = RealMouse.Paint(SyntheticMouse(), 85, IconState.Normal, LevelDisplay.LevelColour, 24))
                {
                    var rows = RowsWith(m, opaque);
                    foreach (var p in PixelsInRows(m, rows.GetRange(rows.Count - 3, 3), opaque))
                        Check(p.G > p.R && p.G > p.B, "85% charge not green: " + Rgb(p));
                }
                using (var m = RealMouse.Paint(SyntheticMouse(), 85, IconState.Charging, LevelDisplay.LevelColour, 24))
                {
                    var rows = RowsWith(m, opaque);
                    foreach (var p in PixelsInRows(m, rows.GetRange(rows.Count - 3, 3), opaque))
                        Check(p.B > p.R && p.B > p.G, "charging not blue: " + Rgb(p));
                }
            });

            Test("UnknownShowsNoCharge", () =>
            {
                using (var m = RealMouse.Paint(SyntheticMouse(), 80, IconState.Unknown, LevelDisplay.GreyWhite, 24))
                    for (int y = 0; y < 24; y++)
                        for (int x = 0; x < 24; x++)
                        {
                            Color p = m.GetPixel(x, y);
                            Check(p.A < 255 || Math.Min(p.R, Math.Min(p.G, p.B)) >= 150, "charged-looking pixel " + Rgb(p) + " at " + x + "," + y);
                        }
            });

            Test("AsleepHasNoColour", () =>
            {
                using (var m = RealMouse.Paint(SyntheticMouse(), 100, IconState.Asleep, LevelDisplay.GreyWhite, 24))
                    for (int y = 0; y < 24; y++)
                        for (int x = 0; x < 24; x++)
                        {
                            Color p = m.GetPixel(x, y);
                            Check(p.A == 0 || Spread(p) <= 6, "colour while asleep " + Rgb(p) + " at " + x + "," + y);
                        }
            });

            Test("MoreLevelMoreCharge", () =>
            {
                int last = -1;
                foreach (int lvl in new[] { 20, 50, 90 })
                    using (var m = RealMouse.Paint(SyntheticMouse(), lvl, IconState.Normal, LevelDisplay.GreyWhite, 24))
                    {
                        int n = CountIn(m, 0, 0, p => p.A == 255 && Math.Max(p.R, Math.Max(p.G, p.B)) <= 150);
                        Check(n > last, lvl + "%: " + n + " charged pixels, not more than " + last);
                        last = n;
                    }
            });

            Test("LookUsesThePhoto", () =>
            {
                using (var a = new MouseLook(SyntheticMouse(), LevelDisplay.GreyWhite, false).PaintMouse(60, IconState.Normal, false, 24, 2.4f))
                using (var b = RealMouse.Paint(SyntheticMouse(), 60, IconState.Normal, LevelDisplay.GreyWhite, 24))
                    CheckSameImage(a, b, "look vs RealMouse");
            });

            // ---------- photo download and cache ----------
            Test("PhotoUrls", () =>
            {
                Check(PhotoStore.Url(Colourway.CrayonGrey) == "https://www.lofree.co/cdn/shop/files/Hypace_CrayonGrey_PI01.webp?width=1000", PhotoStore.Url(Colourway.CrayonGrey));
                Check(PhotoStore.Url(Colourway.JetBlack) == "https://www.lofree.co/cdn/shop/files/Hypace_JetBlack_PI01.webp?width=1000", PhotoStore.Url(Colourway.JetBlack));
                Check(PhotoStore.Url(Colourway.Ghost) == "https://www.lofree.co/cdn/shop/files/Hypace_Ghost_PI01.webp?width=1000", PhotoStore.Url(Colourway.Ghost));
            });

            Func<string> tempDir = () => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hypace-test-" + Guid.NewGuid().ToString("N"));

            Test("PhotoCacheRoundTrip", () =>
            {
                string dir = tempDir();
                try
                {
                    var store = new PhotoStore(dir);
                    using (var photo = SyntheticPhoto())
                    using (var cut = RealMouse.Cutout(photo))
                        store.Save(Colourway.CrayonGrey, cut);
                    string file = System.IO.Path.Combine(dir, "CrayonGrey.png");
                    Check(store.PathFor(Colourway.CrayonGrey) == file && System.IO.File.Exists(file), "not saved as " + file);
                    var p = store.TryLoad(Colourway.CrayonGrey);
                    Check(p != null && Math.Abs(p.Width - 331) <= 3 && Math.Abs(p.Height - 621) <= 3, "loaded " + (p == null ? "nothing" : p.Width + "x" + p.Height));
                }
                finally { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true); }
            });

            Test("BadPhotoCacheIsIgnored", () =>
            {
                string dir = tempDir();
                try
                {
                    var store = new PhotoStore(dir);
                    Check(store.TryLoad(Colourway.CrayonGrey) == null, "missing file");
                    System.IO.Directory.CreateDirectory(dir);
                    System.IO.File.WriteAllText(store.PathFor(Colourway.JetBlack), "not a png");
                    Check(store.TryLoad(Colourway.JetBlack) == null, "text file");
                    using (var small = new Bitmap(50, 50)) small.Save(store.PathFor(Colourway.Ghost), System.Drawing.Imaging.ImageFormat.Png);
                    Check(store.TryLoad(Colourway.Ghost) == null, "50x50 picture");
                }
                finally { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true); }
            });

            // ---------- mouse style settings and menu texts ----------
            Test("MouseSettingsDefaults", () =>
            {
                var m = MouseSettings.Parse(SettingsText.Parse(""));
                Check(m.Realistic && m.Colourway == Colourway.Ghost && m.Display == LevelDisplay.SeeThrough && m.ChargingBolt,
                      "defaults: realistic " + m.Realistic + ", " + m.Colourway + ", " + m.Display + ", bolt " + m.ChargingBolt);
                var old = MouseSettings.Parse(SettingsText.Parse("iconSize=Large\nlanguage=en"));   // a settings file from before the mouse styles
                Check(old.Realistic && old.Colourway == Colourway.Ghost && old.Display == LevelDisplay.SeeThrough && old.ChargingBolt, "older settings file");
                Check(m.ShowNumber && old.ShowNumber, "the number shows by default");
            });

            Test("MouseSettingsRoundTrip", () =>
            {
                foreach (bool real in new[] { false, true })
                    foreach (Colourway c in Enum.GetValues(typeof(Colourway)))
                        foreach (LevelDisplay d in Enum.GetValues(typeof(LevelDisplay)))
                            foreach (bool bolt in new[] { false, true })
                            {
                                var s = SettingsText.Parse("language=en");
                                new MouseSettings { Realistic = real, Colourway = c, Display = d, ChargingBolt = bolt }.WriteTo(s);
                                var back = MouseSettings.Parse(SettingsText.Parse(SettingsText.Format(s)));
                                Check(back.Realistic == real && back.Colourway == c && back.Display == d && back.ChargingBolt == bolt,
                                      real + " " + c + " " + d + " " + bolt + " came back as " + back.Realistic + " " + back.Colourway + " " + back.Display + " " + back.ChargingBolt);
                                Check(s["language"] == "en", "language setting lost");
                            }
                var one = SettingsText.Parse("");
                new MouseSettings { Realistic = true, Colourway = Colourway.JetBlack, Display = LevelDisplay.SeeThrough, ChargingBolt = false }.WriteTo(one);
                Check(one["mouseStyle"] == "realistic" && one["colourway"] == "JetBlack" && one["levelDisplay"] == "seethrough" && one["chargingBolt"] == "off", SettingsText.Format(one));
                var two = SettingsText.Parse("");
                new MouseSettings { Realistic = false, Colourway = Colourway.Ghost, Display = LevelDisplay.LevelColour, ChargingBolt = true }.WriteTo(two);
                Check(two["mouseStyle"] == "drawn" && two["colourway"] == "Ghost" && two["levelDisplay"] == "levelcolour" && two["chargingBolt"] == "on", SettingsText.Format(two));
                var three = SettingsText.Parse("");
                new MouseSettings { Realistic = false, Colourway = Colourway.CrayonGrey, Display = LevelDisplay.GreyWhite, ChargingBolt = true }.WriteTo(three);
                Check(three["colourway"] == "CrayonGrey" && three["levelDisplay"] == "greywhite", SettingsText.Format(three));
            });

            Test("NumberSettingRoundTrip", () =>
            {
                foreach (bool show in new[] { false, true })
                {
                    var s = SettingsText.Parse("language=zh");
                    new MouseSettings { ShowNumber = show }.WriteTo(s);
                    Check(s["showNumber"] == (show ? "on" : "off"), "written as " + s["showNumber"]);
                    Check(MouseSettings.Parse(SettingsText.Parse(SettingsText.Format(s))).ShowNumber == show, "came back wrong for " + show);
                }
                Check(MouseSettings.Parse(SettingsText.Parse("showNumber=maybe")).ShowNumber, "unknown value hides the number");
                Check(!MouseSettings.Parse(SettingsText.Parse("showNumber= OFF ")).ShowNumber, "OFF with spaces");
            });

            Test("NumberMenuText", () =>
            {
                Check(UiText.For(Language.English).ShowNumber == "Show battery percentage", UiText.For(Language.English).ShowNumber);
                Check(UiText.For(Language.Chinese).ShowNumber == "显示电量数字", UiText.For(Language.Chinese).ShowNumber);
            });

            Test("MouseSettingsUnknownValues", () =>
            {
                var m = MouseSettings.Parse(SettingsText.Parse("mouseStyle=x\ncolourway=Pink\nlevelDisplay=?\nchargingBolt=maybe\ncolourway=1"));
                Check(m.Realistic && m.Colourway == Colourway.Ghost && m.Display == LevelDisplay.SeeThrough && m.ChargingBolt,
                      "unknown values: realistic " + m.Realistic + ", " + m.Colourway + ", " + m.Display + ", bolt " + m.ChargingBolt);
            });

            Test("MouseMenuTextInBothLanguages", () =>
            {
                UiText en = UiText.For(Language.English), zh = UiText.For(Language.Chinese);
                Check(zh.MouseStyle == "鼠标样式" && zh.Drawn == "线条" && zh.Realistic == "仿真", "zh style: " + zh.MouseStyle + " " + zh.Drawn + " " + zh.Realistic);
                Check(zh.GreyWhite == "没电部分变灰白" && zh.SeeThrough == "没电部分变透明" && zh.LevelColour == "电量部分用电量颜色", "zh displays");
                Check(zh.ChargingBolt == "充电时显示闪电" && zh.Downloading == "（下载中…）" && zh.DownloadFailed == "（下载失败，稍后重试）", "zh bolt and download");
                Check(en.MouseStyle == "Mouse style" && en.Drawn == "Drawn" && en.Realistic == "Realistic", "en style: " + en.MouseStyle + " " + en.Drawn + " " + en.Realistic);
                Check(en.GreyWhite == "Drained part turns grey-white" && en.SeeThrough == "Drained part turns see-through" && en.LevelColour == "Charged part in level colours", "en displays");
                Check(en.ChargingBolt == "Show charging bolt" && en.Downloading == " (downloading…)" && en.DownloadFailed == " (download failed, will retry)", "en bolt and download");
            });

            int failed = 0;
            foreach (var t in tests)
            {
                try { t.Value(); Console.WriteLine("PASS  " + t.Key); }
                catch (Exception e) { failed++; Console.WriteLine("FAIL  " + t.Key + "  " + e.Message); }
            }
            Console.WriteLine(failed == 0 ? "PASS (" + tests.Count + " tests)" : "FAILED " + failed + "/" + tests.Count);
            return failed == 0 ? 0 : 1;
        }
    }
}
