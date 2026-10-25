using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SuyunCraft
{
    static class Theme
    {
        // Base and surface: night-world palette - deep moss background + Layered stone panel
        public static readonly System.Drawing.Color Bg = System.Drawing.Color.FromArgb(16, 20, 17);
        public static readonly System.Drawing.Color Panel = System.Drawing.Color.FromArgb(22, 27, 23);
        public static readonly System.Drawing.Color PanelDeep = System.Drawing.Color.FromArgb(18, 23, 19);
        public static readonly System.Drawing.Color Card = System.Drawing.Color.FromArgb(30, 36, 31);
        public static readonly System.Drawing.Color CardTop = System.Drawing.Color.FromArgb(35, 42, 36);
        public static readonly System.Drawing.Color CardHover = System.Drawing.Color.FromArgb(41, 49, 42);
        public static readonly System.Drawing.Color Field = System.Drawing.Color.FromArgb(18, 23, 19);
        public static readonly System.Drawing.Color FieldDeep = System.Drawing.Color.FromArgb(12, 16, 13);
        public static readonly System.Drawing.Color FieldDisabled = System.Drawing.Color.FromArgb(26, 31, 27);
        public static readonly System.Drawing.Color Border = System.Drawing.Color.FromArgb(47, 56, 48);
        public static readonly System.Drawing.Color BorderStrong = System.Drawing.Color.FromArgb(68, 80, 69);

        // Block depth: lit top edge, shaded bottom (Minecraft GUI bevel）
        public static readonly System.Drawing.Color BevelLight = System.Drawing.Color.FromArgb(30, 255, 255, 255);
        public static readonly System.Drawing.Color BevelDark = System.Drawing.Color.FromArgb(78, 0, 0, 0);
        public static readonly System.Drawing.Color Shadow = System.Drawing.Color.FromArgb(96, 0, 0, 0);

        // text
        public static readonly System.Drawing.Color Text = System.Drawing.Color.FromArgb(238, 242, 234);
        public static readonly System.Drawing.Color Muted = System.Drawing.Color.FromArgb(150, 160, 148);
        public static readonly System.Drawing.Color TextDisabled = System.Drawing.Color.FromArgb(100, 110, 100);
        public static readonly System.Drawing.Color LogText = System.Drawing.Color.FromArgb(198, 208, 196);
        public static readonly System.Drawing.Color LogMuted = System.Drawing.Color.FromArgb(134, 146, 134);

        // Primary: grass-block green
        public static readonly System.Drawing.Color Accent = System.Drawing.Color.FromArgb(92, 184, 62);
        public static readonly System.Drawing.Color AccentLight = System.Drawing.Color.FromArgb(126, 216, 92);
        public static readonly System.Drawing.Color AccentDark = System.Drawing.Color.FromArgb(52, 118, 34);
        public static readonly System.Drawing.Color AccentDeep = System.Drawing.Color.FromArgb(30, 70, 22);

        // Secondary: torch gold (notices / highlighted data）
        public static readonly System.Drawing.Color Gold = System.Drawing.Color.FromArgb(232, 176, 64);
        public static readonly System.Drawing.Color GoldLight = System.Drawing.Color.FromArgb(248, 205, 108);
        public static readonly System.Drawing.Color GoldDark = System.Drawing.Color.FromArgb(158, 114, 30);

        // Cyan diamond (download / progress)）
        public static readonly System.Drawing.Color Blue = System.Drawing.Color.FromArgb(46, 160, 152);
        public static readonly System.Drawing.Color BlueLight = System.Drawing.Color.FromArgb(78, 196, 186);
        public static readonly System.Drawing.Color BlueDark = System.Drawing.Color.FromArgb(26, 106, 100);

        // Dirt (brand block sides)）
        public static readonly System.Drawing.Color DirtLight = System.Drawing.Color.FromArgb(122, 90, 60);
        public static readonly System.Drawing.Color DirtDark = System.Drawing.Color.FromArgb(92, 67, 45);

        public static readonly System.Drawing.Color Danger = System.Drawing.Color.FromArgb(212, 74, 62);
        public static readonly System.Drawing.Color DangerLight = System.Drawing.Color.FromArgb(236, 108, 94);
        public static readonly System.Drawing.Color DangerDark = System.Drawing.Color.FromArgb(148, 44, 34);
        public static readonly System.Drawing.Color Warning = System.Drawing.Color.FromArgb(232, 176, 64);
        public static readonly System.Drawing.Color FocusRing = System.Drawing.Color.FromArgb(126, 216, 92);

        public static readonly System.Drawing.Font Ui = new System.Drawing.Font("Microsoft YaHei UI", 9.75f);
        public static readonly System.Drawing.Font UiBold = new System.Drawing.Font("Microsoft YaHei UI", 9.75f, System.Drawing.FontStyle.Bold);
        public static readonly System.Drawing.Font Input = new System.Drawing.Font("Microsoft YaHei UI", 10f);
        public static readonly System.Drawing.Font Caption = new System.Drawing.Font("Microsoft YaHei UI", 8.75f);
        public static readonly System.Drawing.Font NavBold = new System.Drawing.Font("Microsoft YaHei UI", 10.5f, System.Drawing.FontStyle.Bold);
        public static readonly System.Drawing.Font TitleBold = new System.Drawing.Font("Microsoft YaHei UI", 12.5f, System.Drawing.FontStyle.Bold);
        public static readonly System.Drawing.Font HeadingBold = new System.Drawing.Font("Microsoft YaHei UI", 14.5f, System.Drawing.FontStyle.Bold);
        public static readonly System.Drawing.Font LaunchBold = new System.Drawing.Font("Microsoft YaHei UI", 15.5f, System.Drawing.FontStyle.Bold);
        public static readonly System.Drawing.Font StatBold = new System.Drawing.Font("Microsoft YaHei UI", 13f, System.Drawing.FontStyle.Bold);
        public static readonly System.Drawing.Font Console = new System.Drawing.Font("Consolas", 9f);

        /// <summary>Upgrade standard buttons to owner-drawn; the back color implies the semantic type.</summary>
        public static void StyleButton(Button b, System.Drawing.Color? back)
        {
            UiButton ui = b as UiButton;
            if (ui != null)
            {
                if (back.HasValue)
                {
                    if (back.Value == Accent) ui.Kind = UiButtonKind.Primary;
                    else if (back.Value == Blue) ui.Kind = UiButtonKind.Success;
                    else if (back.Value == Danger) ui.Kind = UiButtonKind.Danger;
                    else if (back.Value == Card) ui.Kind = UiButtonKind.Secondary;
                }
                ui.ForeColor = Text;
                return;
            }
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = back.HasValue ? back.Value : Card;
            b.ForeColor = Text;
            b.FlatAppearance.BorderColor = Border;
            b.FlatAppearance.MouseOverBackColor = CardHover;
            b.Cursor = Cursors.Hand;
            b.Font = UiBold;
        }

        public static void StyleInput(TextBox t)
        {
            t.BackColor = Field;
            t.ForeColor = Text;
            t.BorderStyle = BorderStyle.None;
            t.Font = Input;
        }

        public static void StyleCombo(ComboBox c)
        {
            UiComboBox ui = c as UiComboBox;
            if (ui != null) return;
            c.BackColor = Field;
            c.ForeColor = Text;
            c.FlatStyle = FlatStyle.Flat;
            c.Font = Input;
            c.DropDownStyle = ComboBoxStyle.DropDownList;
        }
    }

    partial class MainForm : Form
    {
        Config cfg;
        bool operationBusy;
        bool suppressLayout;
        CancellationTokenSource launchCancellation;
        Panel navPane, content;
        UiNavItem navHome, navDownload, navSettings;
        UiPage homePage, dlPage, setPage;
        UiLabel statusBar;

        // Page transition
        UiPage activePage;
        UiPage animPrev, animNext;
        Rectangle pageBase;
        System.Windows.Forms.Timer pageAnim;
        DateTime animStart;
        const int PageShift = 20;      // Transition offset (logical px）
        const int PageFadeMs = 260;    // Transition duration (ms）

        // home
        UiTextField txtUser;
        UiComboBox cmbVersion;
        UiButton btnRefreshVer, btnLaunch;
        UiTrackBar trackMem; Label lblMem, lblJava;
        UiCheckBox chkAutoMem, chkIsolation;
        UiLogView txtGameLog;
        Process gameProc;
        readonly GameLogBuffer logQueue = new GameLogBuffer();
        System.Windows.Forms.Timer logTimer;

        // download
        UiComboBox cmbFilter; UiTextField txtSearch;
        UiListView lvVersions;
        UiCheckBox chkFabric; UiComboBox cmbFabric;
        UiButton btnInstall;
        ThemeProgressBar bar; Label lblDl;
        UiButton btnCancelDl;
        DownloadManager mgr;
        System.Windows.Forms.Timer dlTimer;
        long lastBytes; DateTime lastSample;
        List<ManifestVer> manifest = new List<ManifestVer>();
        List<string> fabricLoaders;
        HashSet<string> localInstalled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // settings
        UiTextField txtGameDir, txtJava, txtJvm;
        UiRadioButton rbOfficial, rbBmcl;
        UiCheckBox chkCapture; UiNumeric numW, numH;
        Label lblSetInfo;

        public MainForm()
        {
            Ui.InitScale();
            cfg = Config.Load();
            Text = "SuyunCraft Launcher SuyunCraft v" + Program.AppVersion;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new System.Drawing.Size(1000, 660);
            MinimumSize = new System.Drawing.Size(940, 600);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui;
            KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode == Keys.Escape && operationBusy) { CancelOperation(); e.Handled = true; e.SuppressKeyPress = true; }
            };
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;

            BuildLayout();
            ApplyDpiScale();
            LayoutPages();
            LayoutHome();
            LayoutDownload();
            LayoutSettings();
            AcceptButton = btnLaunch;
            logTimer = new System.Windows.Forms.Timer();
            logTimer.Interval = 250;
            logTimer.Tick += delegate { FlushLog(false); };
            logTimer.Start();
            LoadState();
            ScanLocalVersions();
            FormClosed += delegate {
                if (launchCancellation != null) launchCancellation.Cancel();
                if (mgr != null) mgr.Cancelled = true;
                dlTimer.Dispose();
                logTimer.Stop();
                FlushLog(true);
                logTimer.Dispose();
                cfg.UserName = txtUser.Text.Trim(); cfg.MemoryGb = trackMem.Value;
                cfg.AutoMemory = chkAutoMem.Checked; cfg.VersionIsolation = chkIsolation.Checked;
                cfg.Save();
            };
        }

        void BuildLayout()
        {
            // left nav
            navPane = new Panel();
            navPane.Dock = DockStyle.Left;
            navPane.Width = 190;
            navPane.BackColor = Theme.Panel;
            navPane.Paint += delegate (object sender, PaintEventArgs e)
            {
                using (Pen p = new Pen(Theme.Border))
                    e.Graphics.DrawLine(p, navPane.Width - 1, 0, navPane.Width - 1, navPane.Height);
                using (Pen p = new Pen(UiKit.Alpha(System.Drawing.Color.White, 7)))
                    e.Graphics.DrawLine(p, navPane.Width - 2, 0, navPane.Width - 2, navPane.Height);
            };
            Controls.Add(navPane);

            UiBrand brand = new UiBrand();
            brand.SetBounds(0, 0, 190, 92);
            navPane.Controls.Add(brand);

            navHome = NavItem("Home", UiIcon.Home, 112);
            navDownload = NavItem("Download", UiIcon.Download, 160);
            navSettings = NavItem("Settings", UiIcon.Settings, 208);
            navPane.Controls.Add(navHome);
            navPane.Controls.Add(navDownload);
            navPane.Controls.Add(navSettings);

            navHome.Click += delegate { SwitchPage("home"); };
            navDownload.Click += delegate { SwitchPage("dl"); };
            navSettings.Click += delegate { SwitchPage("set"); };

            Label versionBadge = new UiLabel();
            versionBadge.Text = "v" + Program.AppVersion + " · offlineLaunch";
            versionBadge.ForeColor = Theme.TextDisabled;
            versionBadge.Font = Theme.Caption;
            versionBadge.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            navPane.Controls.Add(versionBadge);
            navPane.Resize += delegate { versionBadge.SetBounds(0, navPane.Height - Ui.Px(48), navPane.Width, Ui.Px(30)); };
            versionBadge.SetBounds(0, navPane.Height - Ui.Px(48), navPane.Width, Ui.Px(30));

            // content area
            content = new Panel();
            content.Dock = DockStyle.Fill;
            content.BackColor = Theme.Bg;
            content.Padding = new Padding(26, 20, 26, 12);
            Controls.Add(content);
            content.BringToFront();

            // status bar
            statusBar = new UiLabel();
            statusBar.Dock = DockStyle.Bottom;
            statusBar.Height = 28;
            statusBar.BackColor = Theme.Panel;
            statusBar.BlendBackground = false;
            statusBar.ForeColor = Theme.Muted;
            statusBar.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            statusBar.Padding = new Padding(26, 0, 0, 0);
            statusBar.Text = "Ready";
            statusBar.Paint += delegate (object sender, PaintEventArgs e)
            {
                using (Pen p = new Pen(Theme.Border))
                    e.Graphics.DrawLine(p, 0, 0, statusBar.Width, 0);
                // Left status cube: like a small lit grass block
                Rectangle dot = new Rectangle(13, (statusBar.Height - 6) / 2, 6, 6);
                UiKit.FillRound(e.Graphics, dot, 1, Theme.Accent);
                UiKit.Bevel(e.Graphics, dot, 1, UiKit.Alpha(System.Drawing.Color.White, 90), UiKit.Alpha(System.Drawing.Color.Black, 60));
            };
            Controls.Add(statusBar);

            BuildHome();
            BuildDownload();
            BuildSettings();

            content.Controls.Add(homePage);
            content.Controls.Add(dlPage);
            content.Controls.Add(setPage);
            content.Resize += delegate { LayoutPages(); };
            SwitchPage("home");
        }

        /// <summary>
        /// Scale the whole control tree once for the current monitor DPI.
        /// PerMonitorV2 WinForms does not rescale control bounds (AutoScaleDimensions is rewritten at runtime
        /// and the factor is pinned to 1), while GDI fonts are already DPI-scaled - so scale manually once,
        /// or text clips and controls misalign. Suspend layout while scaling to avoid mid-way artifacts.
        /// </summary>
        void ApplyDpiScale()
        {
            float k = Ui.Scale;
            if (k > 1.001f)
            {
                suppressLayout = true;
                try { Scale(new System.Drawing.SizeF(k, k)); }
                finally { suppressLayout = false; }
            }

            // The design size 1000x660 becomes 1250x825 at 125%,
            // overshooting small screens; the launch button would leave the screen.
            // Clamp to the working area once so the whole window stays visible.
            int wantW = Ui.Px(1000);
            int wantH = Ui.Px(660);
            try
            {
                System.Drawing.Rectangle work = Screen.PrimaryScreen.WorkingArea;
                wantW = Math.Min(wantW, Math.Max(Ui.Px(560), work.Width - Ui.Px(16)));
                wantH = Math.Min(wantH, Math.Max(Ui.Px(420), work.Height - Ui.Px(44)));
            }
            catch { }

            // Relax MinimumSize first, otherwise ClientSize gets clamped by the old value
            MinimumSize = new System.Drawing.Size(Ui.Px(480), Ui.Px(360));
            ClientSize = new System.Drawing.Size(wantW, wantH);
            int frameW = Math.Max(0, Width - ClientSize.Width);
            int frameH = Math.Max(0, Height - ClientSize.Height);
            MinimumSize = new System.Drawing.Size(
                Math.Min(Ui.Px(940), ClientSize.Width) + frameW,
                Math.Min(Ui.Px(600), ClientSize.Height) + frameH);
            content.Padding = new Padding(Ui.Px(24), Ui.Px(18), Ui.Px(24), Ui.Px(10));
        }

        /// <summary>Layout the three pages within the content area。</summary>
        void LayoutPages()
        {
            if (suppressLayout) return;
            Rectangle area = content.DisplayRectangle;
            if (area.Width <= 0 || area.Height <= 0) return;
            StopPageAnim();
            pageBase = area;
            homePage.SetBounds(area.X, area.Y, area.Width, area.Height);
            dlPage.SetBounds(area.X, area.Y, area.Width, area.Height);
            setPage.SetBounds(area.X, area.Y, area.Width, area.Height);
        }

        UiNavItem NavItem(string text, UiIcon icon, int y)
        {
            UiNavItem item = new UiNavItem();
            item.Caption = text;
            item.Icon = icon;
            item.SetBounds(14, y, 162, 42);
            return item;
        }

        /// <summary>
        /// Switch the page with a horizontal wipe transition.
        /// Old and new pages stay visible, wiping each other; the header fades in with a slight drop.
        /// </summary>
        void SwitchPage(string key)
        {
            UiPage target = key == "home" ? homePage : (key == "dl" ? dlPage : setPage);
            navHome.SetActive(key == "home");
            navDownload.SetActive(key == "dl");
            navSettings.SetActive(key == "set");
            if (target == activePage) return;

            UiPage prev = activePage;
            activePage = target;
            target.Visible = true;
            target.BringToFront();
            target.HeaderFade = 0f;
            target.HeaderShift = -Ui.Px(8);

            if (prev == null)
            {
                // No transition on first show。
                target.HeaderFade = 1f;
                target.HeaderShift = 0;
                return;
            }
            StartPageAnim(prev, target);
        }

        void StartPageAnim(UiPage prev, UiPage next)
        {
            if (animPrev != null)
            {
                // Previous transition still running: settle and hide the old page first。
                animPrev.Visible = false;
                animPrev.SetBounds(pageBase.X, pageBase.Y, pageBase.Width, pageBase.Height);
                animPrev.HeaderFade = 1f;
                animPrev.HeaderShift = 0;
                animPrev = null;
            }
            if (pageAnim == null)
            {
                pageAnim = new System.Windows.Forms.Timer();
                pageAnim.Interval = 16;
                pageAnim.Tick += delegate { StepPageAnim(); };
            }
            animPrev = prev;
            animNext = next;
            animStart = DateTime.UtcNow;
            pageAnim.Start();
        }

        /// <summary>Finish the transition immediately and snap pages into place。</summary>
        void StopPageAnim()
        {
            if (pageAnim != null) pageAnim.Stop();
            if (animPrev != null)
            {
                animPrev.Visible = false;
                animPrev.SetBounds(pageBase.X, pageBase.Y, pageBase.Width, pageBase.Height);
            }
            if (animNext != null)
            {
                animNext.SetBounds(pageBase.X, pageBase.Y, pageBase.Width, pageBase.Height);
                animNext.HeaderFade = 1f;
                animNext.HeaderShift = 0;
            }
            animPrev = null;
            animNext = null;
        }

        void StepPageAnim()
        {
            double ms = (DateTime.UtcNow - animStart).TotalMilliseconds;
            float t = (float)(ms / PageFadeMs);
            bool done = t >= 1f;
            if (done) t = 1f;
            // ease-in-out cubic：Slow start and end, fast middle - feels more physical
            float e = t < 0.5f
                ? 4f * t * t * t
                : 1f - (float)Math.Pow(-2f * t + 2f, 3) / 2f;
            int shift = Ui.Px((int)(PageShift * (1f - e)));

            if (animNext != null)
            {
                animNext.SetBounds(pageBase.X + shift, pageBase.Y, pageBase.Width, pageBase.Height);
                animNext.HeaderFade = e;
                animNext.HeaderShift = (int)(Ui.Px(8) * (e - 1f));
            }
            if (animPrev != null)
                animPrev.SetBounds(pageBase.X - shift, pageBase.Y, pageBase.Width, pageBase.Height);

            if (done) StopPageAnim();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Force a full repaint after scaling to avoid stale pixels。
            Invalidate(true);
            Refresh();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyDarkMode(this);
            WindowChrome.Apply(this);
        }

        /// <summary>
        /// When dragged to another monitor: refresh the scale factor and re-layout.
        /// The framework usually scales the tree; if not (e.g. autoscaling disabled) we scale manually once.
        /// </summary>
        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            float oldScale = Ui.Scale;
            float newScale = e.DeviceDpiNew / 96f;
            if (newScale < 0.05f || newScale > 8f) return;
            if (Math.Abs(newScale - oldScale) < 0.01f) return;
            bool frameworkScaled = navPane != null && navPane.Width >= Ui.Px(190) * 0.9f;
            Ui.Scale = newScale;
            if (!frameworkScaled && oldScale > 0.05f)
            {
                float ratio = newScale / oldScale;
                suppressLayout = true;
                try { Scale(new System.Drawing.SizeF(ratio, ratio)); }
                finally { suppressLayout = false; }
                content.Padding = new Padding(Ui.Px(26), Ui.Px(20), Ui.Px(26), Ui.Px(12));
            }
            LayoutPages();
            LayoutHome();
            LayoutDownload();
            LayoutSettings();
            Invalidate(true);
        }

        static void ApplyDarkMode(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is ListView) ThemeControl(c, "DarkMode_ItemsView", "DarkMode_Explorer");
                else if (c is TextBox) ThemeControl(c, "DarkMode_Explorer");
                else if (c is ComboBox) ThemeControl(c, "DarkMode_CFD", "DarkMode_Explorer");
                ScrollableControl scroll = c as ScrollableControl;
                if (scroll != null && scroll.AutoScroll) ThemeControl(c, "DarkMode_Explorer");
                if (c.HasChildren) ApplyDarkMode(c);
            }
        }

        // Child handles appear after the parent handle; apply the dark theme in each HandleCreated。
        static void ThemeControl(Control c, params string[] themes)
        {
            Action apply = delegate
            {
                foreach (string t in themes) if (t != null) DarkMode.Apply(c, t);
                DarkMode.ApplyScrollBars(c);
            };
            c.HandleCreated += delegate { apply(); };
            if (c.IsHandleCreated) apply();
        }

        Label MkLabel(string text, int x, int y, int w)
        {
            UiLabel l = new UiLabel();
            l.Text = text; l.SetBounds(x, y, w, 22);
            l.ForeColor = Theme.Text;
            return l;
        }

        void LoadState()
        {
            txtUser.Text = cfg.UserName;
            chkAutoMem.Checked = cfg.AutoMemory;
            chkIsolation.Checked = cfg.VersionIsolation;
            trackMem.Value = Math.Max(1, Math.Min(trackMem.Maximum, cfg.MemoryGb));
            lblMem.Text = trackMem.Value + " GB";
            if (cfg.AutoMemory) AutoMemory();
            if (string.IsNullOrEmpty(cfg.JavaPath)) DetectJavaAsync();
            else lblJava.Text = "Java：" + cfg.JavaPath;
            txtJava.Text = cfg.JavaPath;
        }


        public void ScanLocalVersions()
        {
            List<LocalVer> vers = new List<LocalVer>();
            var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string vd = Path.Combine(cfg.GameDir, "versions");
                if (Directory.Exists(vd))
                {
                    foreach (string d in Directory.GetDirectories(vd))
                    {
                        string id = Path.GetFileName(d);
                        installed.Add(id);
                        string jp = Path.Combine(d, id + ".json");
                        if (!File.Exists(jp)) continue;
                        LocalVer v = new LocalVer();
                        v.Id = id;
                        try
                        {
                            var j = LaunchPlanner.LoadMerged(cfg.GameDir, id);
                            v.Loader = new MinecraftLoaderDetector().Detect(j);
                            v.IsFabric = v.Loader == "fabric";
                        }
                        catch { }
                        vers.Add(v);
                    }
                }
            }
            catch { }
            localInstalled = installed;
            vers.Sort((a, b) => string.CompareOrdinal(b.Id, a.Id));
            cmbVersion.Items.Clear();
            foreach (LocalVer v in vers)
                cmbVersion.Items.Add(v);
            if (cmbVersion.Items.Count > 0)
            {
                int idx = 0;
                for (int i = 0; i < vers.Count; i++) if (vers[i].Id == cfg.LastVersion) { idx = i; break; }
                cmbVersion.SelectedIndex = idx;
            }
            statusBar.Text = "localversion " + vers.Count + "   · gamedirectory " + cfg.GameDir;
            RefreshSaveInstances();
        }

        string SelectedVersion()
        {
            if (cmbVersion.SelectedItem == null) return null;
            var local = cmbVersion.SelectedItem as LocalVer;
            if (local != null) return local.Id;
            string s = cmbVersion.SelectedItem.ToString();
            if (s.StartsWith("[F] ")) s = s.Substring("[F] ".Length);
            return s;
        }

        void SetBusy(bool busy)
        {
            operationBusy = busy;
            if (IsDisposed) return;
            foreach (Control control in homePage.Controls) control.Enabled = !busy || control.Contains(btnLaunch);
            setPage.Enabled = !busy;
            btnInstall.Enabled = !busy;
            bool running = gameProc != null && !gameProc.HasExited;
            btnLaunch.Enabled = busy ? launchCancellation != null && !launchCancellation.IsCancellationRequested : !running;
            btnLaunch.Text = busy ? "Cancelaction" : running ? "Game running" : "Launchgame";
            btnLaunch.Icon = busy || running ? UiIcon.Stop : UiIcon.Play;
            btnCancelDl.Enabled = busy && launchCancellation != null && !launchCancellation.IsCancellationRequested;
            UpdateInstanceControls();
        }

        void CancelOperation()
        {
            if (launchCancellation != null) launchCancellation.Cancel();
            if (mgr != null) mgr.Cancelled = true;
            if (!operationBusy) return;
            btnCancelDl.Enabled = false;
            btnLaunch.Enabled = false;
            statusBar.Text = "Cancelling; wait for the current file operation to finish…";
        }

        async void LaunchGame()
        {
            if (operationBusy) return;
            if (gameProc != null && !gameProc.HasExited) { MessageBox.Show("The game is already running."); return; }
            string user = txtUser.Text.Trim();
            if (user.Length == 0) user = "Player";
            string version = SelectedVersion();
            if (version == null) { SwitchPage("dl"); MessageBox.Show("Install and select a version firstversion。"); return; }
            bool isolation = chkIsolation.Checked;
            int memory = trackMem.Value;
            var service = new LaunchPreparationService(download: CreateDownloadCallback());
            launchCancellation = new CancellationTokenSource();
            FileStream environmentLease = null;
            try
            {
                SetBusy(true);
                cfg.UserName = user; cfg.MemoryGb = memory; cfg.AutoMemory = chkAutoMem.Checked;
                cfg.VersionIsolation = isolation; cfg.LastVersion = version; cfg.Save();
                statusBar.Text = "Load the verify cache and check instance locks…";
                var snapshot = cfg.Snapshot();
                environmentLease = await Task.Run(() => {
                    VerifyStore.Load();
                    launchCancellation.Token.ThrowIfCancellationRequested();
                    return GameDirectories.Acquire(snapshot, version);
                });
                var progress = new Progress<LaunchProgress>(p => {
                    if (!IsDisposed && operationBusy && launchCancellation != null && !launchCancellation.IsCancellationRequested) statusBar.Text = p.Message;
                });
                var prepared = await service.PrepareAsync(cfg, version, progress, launchCancellation.Token);
                var plan = prepared.Plan;
                if (IsDisposed) return;
                cfg.JavaPath = plan.JavaExe; txtJava.Text = plan.JavaExe; lblJava.Text = "Java：" + plan.JavaExe; cfg.Save();
                foreach (var issue in prepared.Report.Issues) AppendLog("[preflight " + issue.Code + "] " + issue.Message);
                AppendLog("[Launch ] preparation took " + prepared.Report.PreparationMilliseconds + " ms（game init excluded）");
                var args = new List<string>(plan.JvmArgs);
                args.Add(plan.MainClass); args.AddRange(plan.GameArgs);
                string arguments = CommandLine.Join(args);
                if (arguments.Length > 30000) throw new InvalidOperationException("LaunchArguments too long; shorten the game directory or reduce duplicate dependencies.");
                var info = new ProcessStartInfo(plan.JavaExe, arguments) {
                    WorkingDirectory = plan.WorkingDir, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardError = cfg.CaptureGameLog, RedirectStandardOutput = cfg.CaptureGameLog
                };
                var process = new Process { StartInfo = info, EnableRaisingEvents = true };
                var runningLease = environmentLease;
                if (cfg.CaptureGameLog)
                {
                    process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) AppendLog("[out] " + e.Data); };
                    process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) AppendLog("[err] " + e.Data); };
                }
                process.Exited += delegate {
                    runningLease.Dispose();
                    try { BeginInvoke((MethodInvoker)delegate {
                        FlushLog(true);
                        statusBar.Text = "Game exited (code " + process.ExitCode + "）";
                        btnLaunch.Text = "Launchgame";
                        btnLaunch.Icon = UiIcon.Play;
                        btnLaunch.Enabled = !operationBusy;
                        UpdateInstanceControls();
                    }); }
                    catch (InvalidOperationException) { }
                };
                launchCancellation.Token.ThrowIfCancellationRequested();
                // A failed start must not leave an unstarted Process in gameProc: HasExited would throw on the next click.
                try { if (!process.Start()) throw new InvalidOperationException("Failed to create the Java process"); }
                catch { process.Dispose(); throw; }
                environmentLease = null; // The process exit callback owns the lease from here.
                if (gameProc != null) gameProc.Dispose();
                gameProc = process;
                btnLaunch.Text = "Game running";
                btnLaunch.Icon = UiIcon.Stop;
                btnLaunch.Enabled = false;
                if (cfg.CaptureGameLog) { process.BeginOutputReadLine(); process.BeginErrorReadLine(); }
                statusBar.Text = "gameLaunched (PID  " + process.Id + "）· " + version;
                AppendLog("[Launch ] Command: " + plan.CommandText);
                AppendLog("[Launch ] gamedirectory: " + plan.WorkingDir);
            }
            catch (OperationCanceledException) { if (!IsDisposed) statusBar.Text = "Cancelled"; }
            catch (Exception ex) { if (!IsDisposed) { statusBar.Text = "Launchfailed"; MessageBox.Show(ex.Message, "Launchfailed"); } }
            finally
            {
                if (environmentLease != null) environmentLease.Dispose();
                FinishOperationAsync(service.LastReport);
            }
        }

        async void FinishOperationAsync(LaunchReport report = null)
        {
            try
            {
                await Task.Run(() => {
                    if (report != null)
                    {
                        try { report.Save(Path.Combine(Config.BaseDir, "logs", "launch-preparation-latest.json")); }
                        catch (Exception ex) { AppendLog("[Launch ] Failed to save report: " + ex.Message); }
                    }
                    VerifyStore.Save();
                });
            }
            catch (Exception ex) { AppendLog("[Launch ] cacheSavefailed：" + ex.Message); }
            finally
            {
                if (launchCancellation != null) { launchCancellation.Dispose(); launchCancellation = null; }
                SetBusy(false);
            }
        }


        void AppendLog(string line)
        {
            logQueue.Append(line);
        }

        void FlushLog(bool flushAll)
        {
            string batch = logQueue.Drain(flushAll);
            if (batch.Length == 0) return;
            txtGameLog.AppendText(batch);
            if (txtGameLog.TextLength > 200000) txtGameLog.Text = txtGameLog.Text.Substring(txtGameLog.TextLength - 100000);
            try
            {
                string logDir = Path.Combine(Config.BaseDir, "logs");
                Directory.CreateDirectory(logDir);
                string path = Path.Combine(logDir, "game-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                if (File.Exists(path) && new FileInfo(path).Length > 10L * 1024 * 1024)
                {
                    string previous = path + ".previous";
                    if (File.Exists(previous)) File.Delete(previous);
                    File.Move(path, previous);
                }
                File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss ") + batch, Encoding.UTF8);
            }
            catch { }
        }


    }
}
