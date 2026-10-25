using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SuyunCraft
{
    /// <summary>
    /// High-DPI: layout and custom drawing scale with the monitor to avoid clipped text.
    /// GDI scales fonts automatically; only control coordinates need scaling。
    /// </summary>
    static class Ui
    {
        static float scale = 1f;

        /// <summary>Current scale factor (1 at 96 DPI: 1）。</summary>
        public static float Scale
        {
            get { return scale; }
            set { if (value > 0.05f && value < 8f) scale = value; }
        }

        /// <summary>Convert design px to device px。</summary>
        public static int Px(int value)
        {
            return (int)Math.Round(value * scale);
        }

        /// <summary>Convert design px to device px (float）。</summary>
        public static float Px(float value)
        {
            return (float)Math.Round(value * scale);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern uint GetDpiForSystem();

        /// <summary>Initialize the scale factor from system DPI; idempotent。</summary>
        public static void InitScale()
        {
            try
            {
                uint dpi = GetDpiForSystem();
                if (dpi >= 96) Scale = dpi / 96f;
            }
            catch { }
        }

        /// <summary>
        /// Scale runtime-created forms (dialogs) once for DPI.
        /// Forms are designed at 96 DPI; bounds and fonts match after this call。
        /// </summary>
        public static void ScaleForm(System.Windows.Forms.Form form)
        {
            float k = scale;
            if (k <= 1.001f) return;
            System.Drawing.Size min = form.MinimumSize;
            form.Scale(new System.Drawing.SizeF(k, k));
            // Scale MinimumSize was already scaled; recompute from the raw value to avoid double scaling。
            form.MinimumSize = new System.Drawing.Size(
                (int)Math.Round(min.Width * k),
                (int)Math.Round(min.Height * k));
        }
    }

    /// <summary>Vector icons used by the UI, all drawn by GDI+ drawing; no extra asset dependencies.</summary>
    public enum UiIcon
    {
        None = 0,
        Home,
        Download,
        Settings,
        Play,
        Stop,
        Refresh,
        Check,
        ChevronDown,
        Folder,
        Search,
        Package,
        Cube,
        User,
        Spark,
        Shield,
        Clock,
        Plus,
        Minus,
        Grid,
        Bug,
        Link
    }

    /// <summary>
    /// Drawing kit: rounded rects, gradients, soft shadows and vector icons.
    /// Callers own GraphicsPath disposal。
    /// </summary>
    static class UiKit
    {
        public const int Radius = 10;

        /// <summary>Build a rounded-rect path。</summary>
        public static GraphicsPath RoundPath(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            int d = radius * 2;
            if (r.Width <= d || r.Height <= d)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.X + r.Width - d, r.Y, d, d, 270, 90);
            path.AddArc(r.X + r.Width - d, r.Y + r.Height - d, d, d, 0, 90);
            path.AddArc(r.X, r.Y + r.Height - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>Rounded fill。</summary>
        public static void FillRound(Graphics g, Rectangle r, int radius, Color color)
        {
            using (GraphicsPath p = RoundPath(r, radius))
            using (SolidBrush b = new SolidBrush(color))
                g.FillPath(b, p);
        }

        /// <summary>Rounded linear-gradient fill。</summary>
        public static void FillRoundGradient(Graphics g, Rectangle r, int radius, Color top, Color bottom, bool vertical)
        {
            using (GraphicsPath p = RoundPath(r, radius))
            using (LinearGradientBrush b = new LinearGradientBrush(r, top, bottom,
                vertical ? 90f : 0f))
                g.FillPath(b, p);
        }

        /// <summary>Rounded stroke。</summary>
        public static void StrokeRound(Graphics g, Rectangle r, int radius, Color color, float width)
        {
            Rectangle inset = new Rectangle(r.X, r.Y, r.Width, r.Height);
            using (GraphicsPath p = RoundPath(inset, radius))
            using (Pen pen = new Pen(color, width))
                g.DrawPath(pen, p);
        }

        /// <summary>Soft outer glow for primary buttons and focused cards。</summary>
        public static void Glow(Graphics g, Rectangle r, int radius, Color color, int spread)
        {
            if (spread <= 0) return;
            for (int i = spread; i >= 1; i--)
            {
                Rectangle box = Rectangle.Inflate(r, i, i);
                int alpha = (int)(color.A * 0.16 / spread * (spread - i + 1));
                if (alpha <= 0) continue;
                using (GraphicsPath p = RoundPath(box, radius + i))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, color)))
                    g.FillPath(b, p);
            }
        }

        /// <summary>Shadow under cards。</summary>
        public static void DropShadow(Graphics g, Rectangle r, int radius, Color color, int spread)
        {
            if (spread <= 0) return;
            for (int i = spread; i >= 1; i--)
            {
                Rectangle box = new Rectangle(r.X, r.Y + i, r.Width, r.Height);
                int alpha = (int)(color.A * 0.10 / spread * (spread - i + 1));
                if (alpha <= 0) continue;
                using (GraphicsPath p = RoundPath(box, radius + i / 2))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, color)))
                    g.FillPath(b, p);
            }
        }

        /// <summary>Radial glow for background spots。</summary>
        public static void RadialGlow(Graphics g, Point center, int radius, Color color)
        {
            if (radius <= 0) return;
            Rectangle box = new Rectangle(center.X - radius, center.Y - radius, radius * 2, radius * 2);
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddEllipse(box);
                using (PathGradientBrush b = new PathGradientBrush(p))
                {
                    b.CenterPoint = new PointF(center.X, center.Y);
                    b.CenterColor = color;
                    b.SurroundColors = new Color[] { Color.FromArgb(0, color) };
                    g.FillPath(b, p);
                }
            }
        }

        /// <summary>
        /// Block bevel: a lit line along the top and a dark line along the bottom inside a rounded rect.
        /// This is the core Minecraft GUI language - lit faces up, shaded faces down,
        /// giving flat fills instant slab-like depth。
        /// </summary>
        public static void Bevel(Graphics g, Rectangle r, int radius, Color light, Color dark)
        {
            if (r.Width < 6 || r.Height < 6) return;
            GraphicsState state = g.Save();
            using (GraphicsPath p = RoundPath(r, radius))
            using (Region clip = new Region(p))
            {
                g.SetClip(clip, CombineMode.Intersect);
                using (Pen pen = new Pen(light, 1f))
                    g.DrawLine(pen, r.Left + 1, r.Top + 0.5f, r.Right - 1, r.Top + 0.5f);
                using (Pen pen = new Pen(dark, 1f))
                    g.DrawLine(pen, r.Left + 1, r.Bottom - 1.5f, r.Right - 1, r.Bottom - 1.5f);
            }
            g.Restore(state);
        }

        /// <summary>Inset: a dark line on top for sunken surfaces like inputs and tracks。</summary>
        public static void Inset(Graphics g, Rectangle r, int radius, Color dark)
        {
            if (r.Width < 6 || r.Height < 6) return;
            GraphicsState state = g.Save();
            using (GraphicsPath p = RoundPath(r, radius))
            using (Region clip = new Region(p))
            {
                g.SetClip(clip, CombineMode.Intersect);
                using (Pen pen = new Pen(dark, 1f))
                    g.DrawLine(pen, r.Left + 1, r.Top + 0.5f, r.Right - 1, r.Top + 0.5f);
            }
            g.Restore(state);
        }

        /// <summary>Hard shadow: block-like, crisp edges, more pixel than gaussian。</summary>
        public static void PixelShadow(Graphics g, Rectangle r, int radius, Color color, int offset)
        {
            if (offset <= 0) return;
            Rectangle box = new Rectangle(r.X, r.Y + offset, r.Width, r.Height);
            using (GraphicsPath p = RoundPath(box, radius))
            using (SolidBrush b = new SolidBrush(color))
                g.FillPath(b, p);
        }

        /// <summary>
        /// Background block grid: world-like seams, near invisible, texture without noise。
        /// </summary>
        public static void BlockGrid(Graphics g, Rectangle area, int step, Color color)
        {
            if (step < 8) return;
            using (Pen pen = new Pen(color, 1f))
            {
                for (int x = area.Left + step; x < area.Right; x += step)
                    g.DrawLine(pen, x, area.Top, x, area.Bottom);
                for (int y = area.Top + step; y < area.Bottom; y += step)
                    g.DrawLine(pen, area.Left, y, area.Right, y);
            }
        }

        /// <summary>Outlined text that stays readable on saturated backgrounds。</summary>
        public static void DrawTextShadow(Graphics g, string text, Font font, Rectangle rect, Color color,
            TextFormatFlags flags, Color shadow, int offset)
        {
            Rectangle shifted = new Rectangle(rect.X, rect.Y + offset, rect.Width, rect.Height);
            TextRenderer.DrawText(g, text, font, shifted, shadow, flags);
            TextRenderer.DrawText(g, text, font, rect, color, flags);
        }

        /// <summary>
        /// Isometric grass block: grass top, lit left dirt, shaded right dirt, grass overlay.
        /// Used by the brand and empty states; the most direct Minecraft signal in the UI。
        /// </summary>
        public static void IsoBlock(Graphics g, Rectangle box, bool glow)
        {
            int s = Math.Min(box.Width, box.Height);
            if (s < 10) return;
            float x = box.X + (box.Width - s) / 2f;
            float y = box.Y + (box.Height - s) / 2f;
            float cx = x + s / 2f;

            PointF a = new PointF(cx, y);
            PointF b = new PointF(x + s, y + s * 0.275f);
            PointF c = new PointF(cx, y + s * 0.55f);
            PointF d = new PointF(x, y + s * 0.275f);
            PointF e = new PointF(x, y + s * 0.775f);
            PointF f = new PointF(cx, y + s);
            PointF gp = new PointF(x + s, y + s * 0.775f);

            if (glow) Glow(g, Rectangle.Round(new RectangleF(x, y, s, s)), (int)(s * 0.3f), Alpha(Theme.Accent, 190), (int)(s * 0.34f));

            // Left (lit dirt）
            using (SolidBrush b1 = new SolidBrush(Theme.DirtLight))
                g.FillPolygon(b1, new PointF[] { d, c, f, e });
            // Right (shaded dirt）
            using (SolidBrush b2 = new SolidBrush(Theme.DirtDark))
                g.FillPolygon(b2, new PointF[] { c, b, gp, f });
            // Top (grass, lit above shaded below）
            RectangleF topBox = new RectangleF(x, y, s, s * 0.55f);
            using (LinearGradientBrush tb = new LinearGradientBrush(topBox, Theme.AccentLight, Theme.Accent, 90f))
                g.FillPolygon(tb, new PointF[] { a, b, c, d });
            // Grass overlay: a grass strip along both top edges
            using (SolidBrush gb = new SolidBrush(Theme.Accent))
            {
                g.FillPolygon(gb, new PointF[] { d, c, new PointF(c.X, c.Y + s * 0.10f), new PointF(d.X, d.Y + s * 0.10f) });
                g.FillPolygon(gb, new PointF[] { c, b, new PointF(b.X, b.Y + s * 0.10f), new PointF(c.X, c.Y + s * 0.10f) });
            }
            // outline
            using (Pen pen = new Pen(Alpha(Color.Black, 130), Math.Max(1f, s / 34f)))
            {
                pen.LineJoin = LineJoin.Round;
                g.DrawPolygon(pen, new PointF[] { a, b, c, d });
                g.DrawLine(pen, d, e);
                g.DrawLine(pen, e, f);
                g.DrawLine(pen, f, gp);
                g.DrawLine(pen, gp, b);
                g.DrawLine(pen, c, f);
            }
        }

        /// <summary>
        /// Clear the control background.
        /// With double buffering WinForms calls only OnPaint, never OnPaintBackground,
        /// and pooled buffers are not auto-cleared, so custom controls must fill a base color in OnPaint
        /// or ghosts from the previous buffer user appear。
        /// </summary>
        public static void ClearBackground(Graphics g, System.Windows.Forms.Control owner)
        {
            System.Windows.Forms.Control parent = owner != null ? owner.Parent : null;
            g.Clear(parent != null ? parent.BackColor : Theme.Bg);
        }

        /// <summary>Blend two colors by ratio。</summary>
        public static Color Mix(Color a, Color b, double t)
        {
            if (t <= 0) return a;
            if (t >= 1) return b;
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        /// <summary>Adjust color alpha。</summary>
        public static Color Alpha(Color c, int alpha)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), c.R, c.G, c.B);
        }

        /// <summary>
        /// Draw a vector icon centered in the square of rect。
        /// </summary>
        public static void DrawIcon(Graphics g, UiIcon icon, Rectangle rect, Color color, float weight)
        {
            if (icon == UiIcon.None) return;
            int side = Math.Min(rect.Width, rect.Height);
            if (side <= 4) return;
            int x = rect.X + (rect.Width - side) / 2;
            int y = rect.Y + (rect.Height - side) / 2;
            float u = side / 24f; // Design grid based on 24

            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(color, weight))
            using (SolidBrush brush = new SolidBrush(color))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                switch (icon)
                {
                    case UiIcon.Home:
                        g.DrawLines(pen, new PointF[] {
                            new PointF(x + 3 * u, y + 11.5f * u),
                            new PointF(x + 12 * u, y + 4 * u),
                            new PointF(x + 21 * u, y + 11.5f * u) });
                        g.DrawLines(pen, new PointF[] {
                            new PointF(x + 5.5f * u, y + 10.4f * u),
                            new PointF(x + 5.5f * u, y + 19.5f * u),
                            new PointF(x + 18.5f * u, y + 19.5f * u),
                            new PointF(x + 18.5f * u, y + 10.4f * u) });
                        break;
                    case UiIcon.Download:
                        g.DrawLines(pen, new PointF[] {
                            new PointF(x + 12 * u, y + 3.5f * u),
                            new PointF(x + 12 * u, y + 14.5f * u) });
                        g.DrawLines(pen, new PointF[] {
                            new PointF(x + 7 * u, y + 10 * u),
                            new PointF(x + 12 * u, y + 15 * u),
                            new PointF(x + 17 * u, y + 10 * u) });
                        g.DrawLines(pen, new PointF[] {
                            new PointF(x + 4.5f * u, y + 17.5f * u),
                            new PointF(x + 4.5f * u, y + 20.5f * u),
                            new PointF(x + 19.5f * u, y + 20.5f * u),
                            new PointF(x + 19.5f * u, y + 17.5f * u) });
                        break;
                    case UiIcon.Settings:
                        {
                            using (GraphicsPath p = new GraphicsPath())
                            {
                                for (int i = 0; i < 8; i++)
                                {
                                    double a = i * Math.PI / 4.0;
                                    float cx = x + 12 * u, cy = y + 12 * u;
                                    float r1 = 8.2f * u, r2 = 10.4f * u;
                                    p.AddLine(
                                        new PointF(cx + (float)Math.Cos(a - 0.16) * r1, cy + (float)Math.Sin(a - 0.16) * r1),
                                        new PointF(cx + (float)Math.Cos(a) * r2, cy + (float)Math.Sin(a) * r2));
                                    p.AddLine(
                                        new PointF(cx + (float)Math.Cos(a) * r2, cy + (float)Math.Sin(a) * r2),
                                        new PointF(cx + (float)Math.Cos(a + 0.16) * r1, cy + (float)Math.Sin(a + 0.16) * r1));
                                }
                                p.CloseFigure();
                                g.DrawPath(pen, p);
                            }
                            g.DrawEllipse(pen, x + 8.2f * u, y + 8.2f * u, 7.6f * u, 7.6f * u);
                            break;
                        }
                    case UiIcon.Play:
                        g.FillPolygon(brush, new PointF[] {
                            new PointF(x + 7.5f * u, y + 5 * u),
                            new PointF(x + 18.5f * u, y + 12 * u),
                            new PointF(x + 7.5f * u, y + 19 * u) });
                        break;
                    case UiIcon.Stop:
                        g.FillEllipse(brush, x + 6.5f * u, y + 6.5f * u, 11 * u, 11 * u);
                        break;
                    case UiIcon.Refresh:
                        g.DrawArc(pen, x + 4.5f * u, y + 4.5f * u, 15 * u, 15 * u, 40f, 275f);
                        g.FillPolygon(brush, new PointF[] {
                            new PointF(x + 17.5f * u, y + 3.5f * u),
                            new PointF(x + 20.5f * u, y + 10 * u),
                            new PointF(x + 13.5f * u, y + 8.5f * u) });
                        break;
                    case UiIcon.Check:
                        g.DrawLines(pen, new PointF[] {
                            new PointF(x + 5.5f * u, y + 12.5f * u),
                            new PointF(x + 10 * u, y + 17 * u),
                            new PointF(x + 18.5f * u, y + 7 * u) });
                        break;
                    case UiIcon.ChevronDown:
                        g.DrawLines(pen, new PointF[] {
                            new PointF(x + 6 * u, y + 9.5f * u),
                            new PointF(x + 12 * u, y + 15 * u),
                            new PointF(x + 18 * u, y + 9.5f * u) });
                        break;
                    case UiIcon.Folder:
                        g.DrawLines(pen, new PointF[] {
                            new PointF(x + 3.5f * u, y + 18.5f * u),
                            new PointF(x + 3.5f * u, y + 6 * u),
                            new PointF(x + 9.5f * u, y + 6 * u),
                            new PointF(x + 11.5f * u, y + 8.5f * u),
                            new PointF(x + 20.5f * u, y + 8.5f * u),
                            new PointF(x + 20.5f * u, y + 18.5f * u),
                            new PointF(x + 3.5f * u, y + 18.5f * u) });
                        break;
                    case UiIcon.Search:
                        g.DrawEllipse(pen, x + 4.5f * u, y + 4.5f * u, 11 * u, 11 * u);
                        g.DrawLines(pen, new PointF[] {
                            new PointF(x + 14.5f * u, y + 14.5f * u),
                            new PointF(x + 19.5f * u, y + 19.5f * u) });
                        break;
                    case UiIcon.Package:
                    case UiIcon.Cube:
                        g.DrawLines(pen, new PointF[] {
                            new PointF(x + 12 * u, y + 3.5f * u),
                            new PointF(x + 20 * u, y + 8 * u),
                            new PointF(x + 20 * u, y + 16.5f * u),
                            new PointF(x + 12 * u, y + 21 * u),
                            new PointF(x + 4 * u, y + 16.5f * u),
                            new PointF(x + 4 * u, y + 8 * u),
                            new PointF(x + 12 * u, y + 3.5f * u) });
                        g.DrawLines(pen, new PointF[] {
                            new PointF(x + 4 * u, y + 8 * u),
                            new PointF(x + 12 * u, y + 12.5f * u),
                            new PointF(x + 20 * u, y + 8 * u) });
                        break;
                    case UiIcon.User:
                        g.DrawEllipse(pen, x + 8 * u, y + 4 * u, 8 * u, 8 * u);
                        g.DrawArc(pen, x + 4 * u, y + 12.5f * u, 16 * u, 12 * u, 180f, 180f);
                        break;
                    case UiIcon.Spark:
                        {
                            using (GraphicsPath p = new GraphicsPath())
                            {
                                p.AddPolygon(new PointF[] {
                                    new PointF(x + 12 * u, y + 2.5f * u),
                                    new PointF(x + 14.2f * u, y + 9.8f * u),
                                    new PointF(x + 21.5f * u, y + 12 * u),
                                    new PointF(x + 14.2f * u, y + 14.2f * u),
                                    new PointF(x + 12 * u, y + 21.5f * u),
                                    new PointF(x + 9.8f * u, y + 14.2f * u),
                                    new PointF(x + 2.5f * u, y + 12 * u),
                                    new PointF(x + 9.8f * u, y + 9.8f * u) });
                                g.FillPath(brush, p);
                            }
                            break;
                        }
                    case UiIcon.Shield:
                        g.DrawLines(pen, new PointF[] {
                            new PointF(x + 12 * u, y + 3 * u),
                            new PointF(x + 19.5f * u, y + 6.5f * u),
                            new PointF(x + 19.5f * u, y + 12 * u),
                            new PointF(x + 12 * u, y + 20.5f * u),
                            new PointF(x + 4.5f * u, y + 12 * u),
                            new PointF(x + 4.5f * u, y + 6.5f * u),
                            new PointF(x + 12 * u, y + 3 * u) });
                        break;
                    case UiIcon.Clock:
                        g.DrawEllipse(pen, x + 4 * u, y + 4 * u, 16 * u, 16 * u);
                        g.DrawLines(pen, new PointF[] {
                            new PointF(x + 12 * u, y + 8 * u),
                            new PointF(x + 12 * u, y + 12.5f * u),
                            new PointF(x + 15.5f * u, y + 14.5f * u) });
                        break;
                    case UiIcon.Plus:
                        g.DrawLines(pen, new PointF[] {
                            new PointF(x + 12 * u, y + 5.5f * u),
                            new PointF(x + 12 * u, y + 18.5f * u) });
                        g.DrawLines(pen, new PointF[] {
                            new PointF(x + 5.5f * u, y + 12 * u),
                            new PointF(x + 18.5f * u, y + 12 * u) });
                        break;
                    case UiIcon.Minus:
                        g.DrawLines(pen, new PointF[] {
                            new PointF(x + 5.5f * u, y + 12 * u),
                            new PointF(x + 18.5f * u, y + 12 * u) });
                        break;
                    case UiIcon.Grid:
                        for (int i = 0; i < 4; i++)
                        {
                            float gx = x + (4.5f + (i % 2) * 8 * u);
                            float gy = y + (4.5f + (i / 2) * 8 * u);
                            g.DrawRectangle(pen, gx, gy, 6 * u, 6 * u);
                        }
                        break;
                    case UiIcon.Bug:
                        g.DrawEllipse(pen, x + 7 * u, y + 6.5f * u, 10 * u, 13 * u);
                        g.DrawLines(pen, new PointF[] {
                            new PointF(x + 7 * u, y + 10 * u), new PointF(x + 3.5f * u, y + 8 * u) });
                        g.DrawLines(pen, new PointF[] {
                            new PointF(x + 17 * u, y + 10 * u), new PointF(x + 20.5f * u, y + 8 * u) });
                        g.DrawLines(pen, new PointF[] {
                            new PointF(x + 7 * u, y + 15 * u), new PointF(x + 3.5f * u, y + 16.5f * u) });
                        g.DrawLines(pen, new PointF[] {
                            new PointF(x + 17 * u, y + 15 * u), new PointF(x + 20.5f * u, y + 16.5f * u) });
                        break;
                    case UiIcon.Link:
                        g.DrawArc(pen, x + 3 * u, y + 8 * u, 10 * u, 8 * u, 90f, 180f);
                        g.DrawArc(pen, x + 11 * u, y + 8 * u, 10 * u, 8 * u, 270f, 180f);
                        g.DrawLines(pen, new PointF[] {
                            new PointF(x + 9 * u, y + 12 * u), new PointF(x + 15 * u, y + 12 * u) });
                        break;
                }
            }
            g.SmoothingMode = old;
        }
    }
}
