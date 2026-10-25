using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SuyunCraft
{
    static class DarkMode
    {
        public enum PreferredAppMode
        {
            Default = 0,
            AllowDark = 1,
            ForceDark = 2,
            AllowLight = 3,
            ForceLight = 4
        }

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hWnd, string subApp, string subId);

        // uxtheme 未公开入口：把系统控件（滚动条、下拉列表等）切到深色渲染
        [DllImport("uxtheme.dll", EntryPoint = "#133")]
        static extern PreferredAppMode SetPreferredAppMode(PreferredAppMode mode);

        [DllImport("uxtheme.dll", EntryPoint = "#135")]
        static extern bool AllowDarkModeForWindow(IntPtr hWnd, bool allow);

        [DllImport("uxtheme.dll", EntryPoint = "#132")]
        static extern bool IsHighContrast();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        const uint GW_CHILD = 5;
        const uint GW_HWNDNEXT = 2;

        /// <summary>
        /// 进程级深色模式。必须在创建任何窗口之前调用，
        /// 否则系统滚动条、下拉列表等仍会按浅色主题绘制。
        /// </summary>
        public static void EnableForProcess()
        {
            try
            {
                if (IsHighContrast()) return;
                SetPreferredAppMode(PreferredAppMode.ForceDark);
            }
            catch { }
        }

        // Win10 1809+ 生效，旧系统自动忽略
        public static void Apply(Control c, string themeList)
        {
            try
            {
                if (!c.IsHandleCreated) return;
                SetWindowTheme(c.Handle, themeList, null);
                AllowDarkModeForWindow(c.Handle, true);
            }
            catch { }
        }

        /// <summary>同时套用多个主题（依次尝试，失败不影响其它控件）。</summary>
        public static void Apply(Control c, params string[] themes)
        {
            if (themes == null) return;
            foreach (string theme in themes)
            {
                if (theme == null) continue;
                try
                {
                    if (!c.IsHandleCreated) continue;
                    SetWindowTheme(c.Handle, theme, null);
                    AllowDarkModeForWindow(c.Handle, true);
                }
                catch { }
            }
        }

        /// <summary>允许指定窗口使用系统深色渲染（滚动条、下拉列表等）。</summary>
        public static void AllowDark(IntPtr hwnd)
        {
            try { AllowDarkModeForWindow(hwnd, true); } catch { }
        }

        /// <summary>把容器自带的滚动条也套上深色主题（AutoScroll 面板）。</summary>
        public static void ApplyScrollBars(Control c)
        {
            try
            {
                if (!c.IsHandleCreated) return;
                IntPtr child = GetWindow(c.Handle, GW_CHILD);
                while (child != IntPtr.Zero)
                {
                    System.Text.StringBuilder name = new System.Text.StringBuilder(64);
                    GetClassName(child, name, name.Capacity);
                    if (name.ToString().IndexOf("ScrollBar", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        SetWindowTheme(child, "DarkMode_Explorer", null);
                        AllowDarkModeForWindow(child, true);
                    }
                    child = GetWindow(child, GW_HWNDNEXT);
                }
            }
            catch { }
        }
    }

    static class CueBanner
    {
        const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        public static void Set(TextBox box, string text)
        {
            try { SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, text); } catch { }
        }
    }

    /// <summary>窗口外观：深色标题栏、圆角边框与阴影。</summary>
    static class WindowChrome
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWCP_ROUND = 2;

        public static void Apply(Form form)
        {
            try
            {
                IntPtr handle = form.Handle;
                try { DarkMode.AllowDark(handle); } catch { }
                int dark = 1;
                if (DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)) != 0)
                    DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref dark, sizeof(int));
                int corner = DWMWCP_ROUND;
                DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
            }
            catch { }
        }
    }

    /// <summary>圆角进度条，带渐变填充与百分比。</summary>
    class ThemeProgressBar : Control
    {
        int value;
        bool pulse;
        System.Windows.Forms.Timer anim;

        public int Value
        {
            get { return value; }
            set { this.value = Math.Max(0, Math.Min(100, value)); Invalidate(); }
        }

        /// <summary>下载进行中时显示流动的高光。</summary>
        public bool Pulse
        {
            get { return pulse; }
            set
            {
                pulse = value;
                if (value && anim == null)
                {
                    anim = new System.Windows.Forms.Timer();
                    anim.Interval = 33;
                    anim.Tick += delegate { Invalidate(); };
                }
                if (anim != null) { if (value) anim.Start(); else anim.Stop(); }
                Invalidate();
            }
        }

        public ThemeProgressBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Field;
            Size = new System.Drawing.Size(200, 20);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && anim != null) { anim.Stop(); anim.Dispose(); anim = null; }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            UiKit.ClearBackground(g, this);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Width, Height);
            UiKit.FillRound(g, bounds, Height / 2, Theme.Field);
            UiKit.StrokeRound(g, bounds, Height / 2, Theme.Border, 1f);

            if (value > 0)
            {
                int fillWidth = (int)((long)(Width - 2) * value / 100);
                if (fillWidth > 2)
                {
                    Rectangle fill = new Rectangle(1, 1, fillWidth, Height - 2);
                    UiKit.FillRoundGradient(g, fill, Height / 2, Theme.BlueLight, Theme.Blue, false);
                    if (pulse)
                    {
                        int phase = (int)(System.Environment.TickCount % 1400) / 1400;
                        int band = Math.Max(18, Width / 6);
                        int x = (int)((Width + band) * phase) - band;
                        Rectangle shine = new Rectangle(x, 1, band, Height - 2);
                        using (GraphicsPath p = UiKit.RoundPath(fill, Height / 2))
                        {
                            g.SetClip(p);
                            using (LinearGradientBrush b = new LinearGradientBrush(
                                new Rectangle(shine.X, shine.Y, Math.Max(1, shine.Width), Math.Max(1, shine.Height)),
                                Color.FromArgb(0, 255, 255, 255), Color.FromArgb(90, 255, 255, 255), 0f))
                                g.FillRectangle(b, shine);
                            g.ResetClip();
                        }
                    }
                }
            }

            string pct = value + "%";
            Size size = TextRenderer.MeasureText(pct, Theme.UiBold);
            Rectangle textRect = new Rectangle(0, 0, Width, Height);
            TextRenderer.DrawText(g, pct, Theme.UiBold, textRect, value > 0 ? System.Drawing.Color.White : Theme.Muted,
                TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine);
        }
    }
}
