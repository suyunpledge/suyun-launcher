using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace SuyunCraft
{
    /// <summary>
    /// 高 DPI 缩放：布局与自绘细节统一按显示器 DPI 放大，避免文字被裁切。
    /// 字体由 GDI 按系统 DPI 自动放大，因此这里只需保证控件坐标同比例缩放。
    /// </summary>
    static class Ui
    {
        static float scale = 1f;

        /// <summary>当前缩放系数（96 DPI 时为 1）。</summary>
        public static float Scale
        {
            get { return scale; }
            set { if (value > 0.05f && value < 8f) scale = value; }
        }

        /// <summary>把设计像素换算为实际设备像素。</summary>
        public static int Px(int value)
        {
            return (int)Math.Round(value * scale);
        }

        /// <summary>把设计像素换算为实际设备像素（浮点）。</summary>
        public static float Px(float value)
        {
            return (float)Math.Round(value * scale);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern uint GetDpiForSystem();

        /// <summary>按系统 DPI 初始化缩放系数；重复调用安全。</summary>
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
        /// 把运行时创建的窗体（对话框）按 DPI 放大一次。
        /// 窗体内部仍按 96 DPI 设计，调用后控件边界与字号才会匹配。
        /// </summary>
        public static void ScaleForm(System.Windows.Forms.Form form)
        {
            float k = scale;
            if (k <= 1.001f) return;
            System.Drawing.Size min = form.MinimumSize;
            form.Scale(new System.Drawing.SizeF(k, k));
            // Scale 本身已经放大过 MinimumSize，这里按原始值重算一次，避免二次放大把窗口撑大。
            form.MinimumSize = new System.Drawing.Size(
                (int)Math.Round(min.Width * k),
                (int)Math.Round(min.Height * k));
        }
    }

    /// <summary>界面使用的矢量图标，全部由 GDI+ 绘制，避免额外资源依赖。</summary>
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
    /// 绘图工具箱：圆角、渐变、柔光阴影与矢量图标。
    /// 所有方法都要求调用方自行管理 GraphicsPath 的释放。
    /// </summary>
    static class UiKit
    {
        public const int Radius = 10;

        /// <summary>生成圆角矩形路径。</summary>
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

        /// <summary>圆角填充。</summary>
        public static void FillRound(Graphics g, Rectangle r, int radius, Color color)
        {
            using (GraphicsPath p = RoundPath(r, radius))
            using (SolidBrush b = new SolidBrush(color))
                g.FillPath(b, p);
        }

        /// <summary>圆角线性渐变填充。</summary>
        public static void FillRoundGradient(Graphics g, Rectangle r, int radius, Color top, Color bottom, bool vertical)
        {
            using (GraphicsPath p = RoundPath(r, radius))
            using (LinearGradientBrush b = new LinearGradientBrush(r, top, bottom,
                vertical ? 90f : 0f))
                g.FillPath(b, p);
        }

        /// <summary>圆角描边。</summary>
        public static void StrokeRound(Graphics g, Rectangle r, int radius, Color color, float width)
        {
            Rectangle inset = new Rectangle(r.X, r.Y, r.Width, r.Height);
            using (GraphicsPath p = RoundPath(inset, radius))
            using (Pen pen = new Pen(color, width))
                g.DrawPath(pen, p);
        }

        /// <summary>柔和外发光，用于主按钮和聚焦卡片。</summary>
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

        /// <summary>卡片下方的投影。</summary>
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

        /// <summary>径向柔光，用于背景光斑。</summary>
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
        /// 清空控件背景。
        /// 开启双缓冲后 WinForms 只调用 OnPaint、不再调用 OnPaintBackground，
        /// 而缓冲位图是池化复用的（不会自动清空），因此自绘控件必须在 OnPaint 里先铺底色，
        /// 否则会露出上一个使用该缓冲的控件的残影。
        /// </summary>
        public static void ClearBackground(Graphics g, System.Windows.Forms.Control owner)
        {
            System.Windows.Forms.Control parent = owner != null ? owner.Parent : null;
            g.Clear(parent != null ? parent.BackColor : Theme.Bg);
        }

        /// <summary>把两个颜色按比例混合。</summary>
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

        /// <summary>调整颜色透明度。</summary>
        public static Color Alpha(Color c, int alpha)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), c.R, c.G, c.B);
        }

        /// <summary>
        /// 绘制矢量图标。图标绘制在 rect 的正方形内并居中。
        /// </summary>
        public static void DrawIcon(Graphics g, UiIcon icon, Rectangle rect, Color color, float weight)
        {
            if (icon == UiIcon.None) return;
            int side = Math.Min(rect.Width, rect.Height);
            if (side <= 4) return;
            int x = rect.X + (rect.Width - side) / 2;
            int y = rect.Y + (rect.Height - side) / 2;
            float u = side / 24f; // 以 24 为基准的设计网格

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
