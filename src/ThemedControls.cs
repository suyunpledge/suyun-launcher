using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SuyunCraft
{
    static class DarkMode
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hWnd, string subApp, string subId);

        // Win10 1809+ 生效，旧系统自动忽略
        public static void Apply(Control c, string themeList)
        {
            try { if (c.IsHandleCreated) SetWindowTheme(c.Handle, themeList, null); }
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

    class ThemeProgressBar : Control
    {
        int value;

        public int Value
        {
            get { return value; }
            set { this.value = Math.Max(0, Math.Min(100, value)); Invalidate(); }
        }

        public ThemeProgressBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = System.Drawing.Color.FromArgb(24, 25, 28);
            Size = new System.Drawing.Size(200, 20);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            var bounds = new System.Drawing.Rectangle(0, 0, Width - 1, Height - 1);
            if (value > 0)
            {
                int fillWidth = (int)((long)(Width - 2) * value / 100);
                if (fillWidth > 0)
                    using (var brush = new System.Drawing.SolidBrush(Theme.Blue))
                        g.FillRectangle(brush, 1, 1, fillWidth, Height - 2);
            }
            using (var pen = new System.Drawing.Pen(Theme.Border))
                g.DrawRectangle(pen, bounds);
            string pct = value + "%";
            var size = g.MeasureString(pct, Theme.Ui);
            using (var brush = new System.Drawing.SolidBrush(Theme.Text))
                g.DrawString(pct, Theme.Ui, brush, (Width - size.Width) / 2, (Height - size.Height) / 2);
        }
    }
}
