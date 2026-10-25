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

        // uxtheme Undocumented entry: switches system controls to dark rendering
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
        /// Process-wide dark mode. Must be called before any window is created,
        /// otherwise system scrollbars and dropdowns render light。
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

        // Win10 1809+ takes effect; older systems ignore it
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

        /// <summary>Apply multiple themes (each tried; failures do not affect others)）。</summary>
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

        /// <summary>Lets a window use system dark rendering (scrollbars, dropdowns)）。</summary>
        public static void AllowDark(IntPtr hwnd)
        {
            try { AllowDarkModeForWindow(hwnd, true); } catch { }
        }

        /// <summary>Theme container scrollbars too (AutoScroll panels)）。</summary>
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

    /// <summary>Window chrome: dark title bar, rounded border and shadow。</summary>
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

    /// <summary>Rounded progress bar with gradient fill and percentage。</summary>
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

        /// <summary>DownloadA flowing highlight while active。</summary>
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
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Rectangle bounds = new Rectangle(0, 0, Width, Height);
            int rad = Ui.Px(3);

            // groove
            UiKit.FillRound(g, bounds, rad, Theme.FieldDeep);
            UiKit.Inset(g, bounds, rad, UiKit.Alpha(System.Drawing.Color.Black, 150));
            UiKit.StrokeRound(g, bounds, rad, Theme.Border, 1f);

            if (value > 0)
            {
                int fillWidth = (int)((long)(Width - 2) * value / 100);
                if (fillWidth > 2)
                {
                    Rectangle fill = new Rectangle(1, 1, fillWidth, Math.Max(2, Height - 2));
                    GraphicsState state = g.Save();
                    using (GraphicsPath p = UiKit.RoundPath(fill, rad))
                    using (Region clip = new Region(p))
                        g.SetClip(clip, CombineMode.Intersect);

                    using (LinearGradientBrush b = new LinearGradientBrush(fill, Theme.BlueLight, Theme.BlueDark, 90f))
                        g.FillRectangle(b, fill);

                    // Segmented ticks: XP-bar-like blockiness
                    using (Pen tick = new Pen(UiKit.Alpha(System.Drawing.Color.Black, 62), 1f))
                    {
                        for (int x = fill.X + Ui.Px(7); x < fill.Right; x += Ui.Px(7))
                            g.DrawLine(tick, x, fill.Top, x, fill.Bottom);
                    }
                    using (Pen hi = new Pen(UiKit.Alpha(System.Drawing.Color.White, 76), 1f))
                        g.DrawLine(hi, fill.Left, fill.Top + 0.5f, fill.Right, fill.Top + 0.5f);

                    if (pulse)
                    {
                        int phase = (int)(System.Environment.TickCount % 1200) * 100 / 1200;
                        int band = Math.Max(Ui.Px(14), Width / 5);
                        int x = (int)((long)(Width + band) * phase / 100) - band;
                        Rectangle shine = new Rectangle(x, fill.Top, band, fill.Height);
                        using (LinearGradientBrush lb = new LinearGradientBrush(shine,
                            System.Drawing.Color.FromArgb(0, 255, 255, 255),
                            System.Drawing.Color.FromArgb(80, 255, 255, 255), 0f))
                            g.FillRectangle(lb, shine);
                    }
                    g.Restore(state);
                }
            }

            string pct = value + "%";
            Rectangle textRect = new Rectangle(0, 0, Width, Height);
            TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter
                | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
            if (value > 0)
                UiKit.DrawTextShadow(g, pct, Theme.UiBold, textRect, System.Drawing.Color.White, flags,
                    UiKit.Alpha(System.Drawing.Color.Black, 120), 1);
            else
                TextRenderer.DrawText(g, pct, Theme.UiBold, textRect, Theme.Muted, flags);
        }
    }
}
