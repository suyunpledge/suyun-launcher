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
        // 底层与应用表面
        public static readonly System.Drawing.Color Bg = System.Drawing.Color.FromArgb(20, 22, 27);
        public static readonly System.Drawing.Color Panel = System.Drawing.Color.FromArgb(27, 30, 37);
        public static readonly System.Drawing.Color Card = System.Drawing.Color.FromArgb(33, 37, 45);
        public static readonly System.Drawing.Color CardHover = System.Drawing.Color.FromArgb(42, 47, 57);
        public static readonly System.Drawing.Color Field = System.Drawing.Color.FromArgb(24, 27, 33);
        public static readonly System.Drawing.Color FieldDisabled = System.Drawing.Color.FromArgb(31, 34, 40);
        public static readonly System.Drawing.Color Border = System.Drawing.Color.FromArgb(45, 50, 60);
        public static readonly System.Drawing.Color BorderStrong = System.Drawing.Color.FromArgb(62, 69, 82);

        // 文本
        public static readonly System.Drawing.Color Text = System.Drawing.Color.FromArgb(233, 237, 244);
        public static readonly System.Drawing.Color Muted = System.Drawing.Color.FromArgb(138, 147, 163);
        public static readonly System.Drawing.Color TextDisabled = System.Drawing.Color.FromArgb(94, 102, 116);
        public static readonly System.Drawing.Color LogText = System.Drawing.Color.FromArgb(201, 209, 217);
        public static readonly System.Drawing.Color LogMuted = System.Drawing.Color.FromArgb(139, 148, 158);

        // 主题色
        public static readonly System.Drawing.Color Accent = System.Drawing.Color.FromArgb(63, 185, 80);
        public static readonly System.Drawing.Color AccentLight = System.Drawing.Color.FromArgb(95, 211, 111);
        public static readonly System.Drawing.Color AccentDark = System.Drawing.Color.FromArgb(46, 139, 59);
        public static readonly System.Drawing.Color Blue = System.Drawing.Color.FromArgb(59, 130, 246);
        public static readonly System.Drawing.Color BlueLight = System.Drawing.Color.FromArgb(96, 165, 250);
        public static readonly System.Drawing.Color BlueDark = System.Drawing.Color.FromArgb(37, 99, 235);
        public static readonly System.Drawing.Color Danger = System.Drawing.Color.FromArgb(229, 83, 75);
        public static readonly System.Drawing.Color DangerLight = System.Drawing.Color.FromArgb(244, 112, 105);
        public static readonly System.Drawing.Color DangerDark = System.Drawing.Color.FromArgb(192, 57, 43);
        public static readonly System.Drawing.Color Warning = System.Drawing.Color.FromArgb(227, 179, 65);
        public static readonly System.Drawing.Color FocusRing = System.Drawing.Color.FromArgb(88, 166, 255);

        public static readonly System.Drawing.Font Ui = new System.Drawing.Font("Microsoft YaHei UI", 9.75f);
        public static readonly System.Drawing.Font UiBold = new System.Drawing.Font("Microsoft YaHei UI", 9.75f, System.Drawing.FontStyle.Bold);
        public static readonly System.Drawing.Font Input = new System.Drawing.Font("Microsoft YaHei UI", 10f);
        public static readonly System.Drawing.Font Caption = new System.Drawing.Font("Microsoft YaHei UI", 8.75f);
        public static readonly System.Drawing.Font NavBold = new System.Drawing.Font("Microsoft YaHei UI", 10.5f, System.Drawing.FontStyle.Bold);
        public static readonly System.Drawing.Font TitleBold = new System.Drawing.Font("Microsoft YaHei UI", 12.5f, System.Drawing.FontStyle.Bold);
        public static readonly System.Drawing.Font HeadingBold = new System.Drawing.Font("Microsoft YaHei UI", 13f, System.Drawing.FontStyle.Bold);
        public static readonly System.Drawing.Font LaunchBold = new System.Drawing.Font("Microsoft YaHei UI", 15f, System.Drawing.FontStyle.Bold);
        public static readonly System.Drawing.Font Console = new System.Drawing.Font("Consolas", 9f);

        /// <summary>把标准按钮升级为自绘按钮；传入底色可推断语义类型。</summary>
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

        // 页面切换过渡
        UiPage activePage;
        UiPage animPrev, animNext;
        Rectangle pageBase;
        System.Windows.Forms.Timer pageAnim;
        DateTime animStart;
        const int PageShift = 20;      // 过渡位移（逻辑像素）
        const int PageFadeMs = 260;    // 过渡时长（毫秒）

        // home
        UiTextField txtUser;
        UiComboBox cmbVersion;
        UiButton btnRefreshVer, btnLaunch;
        UiTrackBar trackMem; Label lblMem, lblJava;
        UiCheckBox chkAutoMem, chkIsolation;
        UiLogView txtGameLog;
        Process gameProc;
        readonly ConcurrentQueue<string> logQueue = new ConcurrentQueue<string>();
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
            Text = "溯昀启动器 SuyunCraft v" + Program.AppVersion;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new System.Drawing.Size(1000, 660);
            MinimumSize = new System.Drawing.Size(940, 600);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui;
            KeyPreview = true;
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
            // 左侧导航
            navPane = new Panel();
            navPane.Dock = DockStyle.Left;
            navPane.Width = 190;
            navPane.BackColor = Theme.Panel;
            navPane.Paint += delegate (object sender, PaintEventArgs e)
            {
                using (Pen p = new Pen(Theme.Border))
                    e.Graphics.DrawLine(p, navPane.Width - 1, 0, navPane.Width - 1, navPane.Height);
            };
            Controls.Add(navPane);

            UiBrand brand = new UiBrand();
            brand.SetBounds(0, 0, 190, 92);
            navPane.Controls.Add(brand);

            navHome = NavItem("主页", UiIcon.Home, 112);
            navDownload = NavItem("下载", UiIcon.Download, 160);
            navSettings = NavItem("设置", UiIcon.Settings, 208);
            navPane.Controls.Add(navHome);
            navPane.Controls.Add(navDownload);
            navPane.Controls.Add(navSettings);

            navHome.Click += delegate { SwitchPage("home"); };
            navDownload.Click += delegate { SwitchPage("dl"); };
            navSettings.Click += delegate { SwitchPage("set"); };

            Label versionBadge = new UiLabel();
            versionBadge.Text = "v" + Program.AppVersion + " · 离线启动";
            versionBadge.ForeColor = Theme.TextDisabled;
            versionBadge.Font = Theme.Caption;
            versionBadge.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            navPane.Controls.Add(versionBadge);
            navPane.Resize += delegate { versionBadge.SetBounds(0, navPane.Height - Ui.Px(48), navPane.Width, Ui.Px(30)); };
            versionBadge.SetBounds(0, navPane.Height - Ui.Px(48), navPane.Width, Ui.Px(30));

            // 内容区
            content = new Panel();
            content.Dock = DockStyle.Fill;
            content.BackColor = Theme.Bg;
            content.Padding = new Padding(26, 20, 26, 12);
            Controls.Add(content);
            content.BringToFront();

            // 状态栏
            statusBar = new UiLabel();
            statusBar.Dock = DockStyle.Bottom;
            statusBar.Height = 28;
            statusBar.BackColor = Theme.Panel;
            statusBar.BlendBackground = false;
            statusBar.ForeColor = Theme.Muted;
            statusBar.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            statusBar.Padding = new Padding(16, 0, 0, 0);
            statusBar.Text = "就绪";
            statusBar.Paint += delegate (object sender, PaintEventArgs e)
            {
                using (Pen p = new Pen(Theme.Border))
                    e.Graphics.DrawLine(p, 0, 0, statusBar.Width, 0);
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
        /// 按当前显示器 DPI 把整棵控件树放大一次。
        /// PerMonitorV2 下 WinForms 不会自动缩放控件边界（运行时会改写 AutoScaleDimensions，
        /// 缩放系数恒为 1），而 GDI 字体已被系统按 DPI 放大，因此这里必须手动缩放一次，
        /// 否则文字会被裁切、控件会互相错位。缩放期间屏蔽布局，避免中途重排留下残影。
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
            ClientSize = new System.Drawing.Size(Ui.Px(1000), Ui.Px(660));
            MinimumSize = new System.Drawing.Size(Ui.Px(940), Ui.Px(600));
            content.Padding = new Padding(Ui.Px(26), Ui.Px(20), Ui.Px(26), Ui.Px(12));
        }

        /// <summary>按内容区可用区域摆放三个页面。</summary>
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
        /// 切换页面，并播放横向推移过渡。
        /// 新旧页面同时可见、互相擦除（wipe），页头同时淡入并轻微下落。
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
                // 首次显示不做过渡。
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
                // 上一次过渡尚未播完：先把旧页归位并隐藏，避免它残留在新页下方。
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

        /// <summary>立即结束过渡，把页面归位。</summary>
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
            // ease-in-out cubic：起步与收尾都慢，中段最快，推移更有质感
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
            // 缩放后强制整窗重绘一次，避免残留旧位置的画面。
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
        /// 窗口被拖到不同 DPI 的显示器时：刷新缩放系数并重新排版。
        /// 框架通常已经按比例放大了控件树，若没有（例如关闭了自动重排）则这里手动补一次。
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

        // 子控件的句柄在父窗口句柄创建之后才陆续生成，必须在各自 HandleCreated 时再套用深色主题。
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
            statusBar.Text = "本地版本 " + vers.Count + " 个 · 游戏目录 " + cfg.GameDir;
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
            homePage.Enabled = !busy;
            setPage.Enabled = !busy;
            btnInstall.Enabled = !busy;
            UpdateInstanceControls();
        }

        async void LaunchGame()
        {
            if (operationBusy) return;
            if (gameProc != null && !gameProc.HasExited) { MessageBox.Show("游戏已在运行中。"); return; }
            string user = txtUser.Text.Trim();
            if (user.Length == 0) user = "Player";
            string version = SelectedVersion();
            if (version == null) { SwitchPage("dl"); MessageBox.Show("请先安装并选择一个版本。"); return; }
            bool isolation = chkIsolation.Checked;
            int memory = trackMem.Value;
            var service = new LaunchPreparationService(download: CreateDownloadCallback());
            launchCancellation = new CancellationTokenSource();
            FileStream environmentLease = null;
            try
            {
                SetBusy(true);
                VerifyStore.Load();   // 持久校验缓存：命中的文件跳过哈希（P2）
                cfg.UserName = user; cfg.MemoryGb = memory; cfg.AutoMemory = chkAutoMem.Checked;
                cfg.VersionIsolation = isolation; cfg.LastVersion = version; cfg.Save();
                environmentLease = GameDirectories.Acquire(cfg, version);
                var progress = new Progress<LaunchProgress>(p => {
                    if (!IsDisposed && operationBusy) statusBar.Text = p.Message;
                });
                var prepared = await service.PrepareAsync(cfg, version, progress, launchCancellation.Token);
                var plan = prepared.Plan;
                if (IsDisposed) return;
                cfg.JavaPath = plan.JavaExe; txtJava.Text = plan.JavaExe; lblJava.Text = "Java：" + plan.JavaExe; cfg.Save();
                foreach (var issue in prepared.Report.Issues) AppendLog("[预检 " + issue.Code + "] " + issue.Message);
                AppendLog("[启动器] 准备耗时 " + prepared.Report.PreparationMilliseconds + " ms（不含游戏初始化）");
                var args = new List<string>(plan.JvmArgs);
                args.Add(plan.MainClass); args.AddRange(plan.GameArgs);
                string arguments = CommandLine.Join(args);
                if (arguments.Length > 30000) throw new InvalidOperationException("启动参数过长，请缩短游戏目录路径或减少重复依赖。");
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
                        statusBar.Text = "游戏已退出（代码 " + process.ExitCode + "）";
                        btnLaunch.Text = "启动游戏";
                        btnLaunch.Icon = UiIcon.Play;
                        btnLaunch.Enabled = !operationBusy;
                        UpdateInstanceControls();
                    }); }
                    catch (InvalidOperationException) { }
                };
                launchCancellation.Token.ThrowIfCancellationRequested();
                // A failed start must not leave an unstarted Process in gameProc: HasExited would throw on the next click.
                try { if (!process.Start()) throw new InvalidOperationException("无法创建 Java 进程"); }
                catch { process.Dispose(); throw; }
                environmentLease = null; // The process exit callback owns the lease from here.
                if (gameProc != null) gameProc.Dispose();
                gameProc = process;
                btnLaunch.Text = "游戏运行中";
                btnLaunch.Icon = UiIcon.Stop;
                btnLaunch.Enabled = false;
                if (cfg.CaptureGameLog) { process.BeginOutputReadLine(); process.BeginErrorReadLine(); }
                statusBar.Text = "游戏已启动（PID " + process.Id + "）· " + version;
                AppendLog("[启动器] 命令: " + plan.CommandText);
                AppendLog("[启动器] 游戏目录: " + plan.WorkingDir);
            }
            catch (OperationCanceledException) { if (!IsDisposed) statusBar.Text = "已取消"; }
            catch (Exception ex) { if (!IsDisposed) { statusBar.Text = "启动失败"; MessageBox.Show(ex.Message, "启动失败"); } }
            finally
            {
                if (environmentLease != null) environmentLease.Dispose();
                try
                {
                    if (service.LastReport != null)
                    {
                        string reportPath = Path.Combine(Config.BaseDir, "logs", "launch-preparation-latest.json");
                        service.LastReport.Save(reportPath);
                        AppendLog("[启动器] 启动准备报告：" + reportPath);
                    }
                }
                catch (Exception ex) { AppendLog("[启动器] 报告保存失败：" + ex.Message); }
                try { VerifyStore.Save(); } catch { }
                launchCancellation.Dispose(); launchCancellation = null;
                SetBusy(false);
            }
        }



        void AppendLog(string line)
        {
            logQueue.Enqueue(line);
        }

        void FlushLog(bool flushAll)
        {
            var lines = new List<string>();
            string line;
            while ((flushAll || lines.Count < 2000) && logQueue.TryDequeue(out line)) lines.Add(line);
            if (lines.Count == 0) return;
            txtGameLog.AppendText(string.Join("\r\n", lines) + "\r\n");
            if (txtGameLog.TextLength > 200000) txtGameLog.Text = txtGameLog.Text.Substring(txtGameLog.TextLength - 100000);
            try
            {
                string logDir = Path.Combine(Config.BaseDir, "logs");
                Directory.CreateDirectory(logDir);
                var sb = new StringBuilder();
                foreach (string l in lines) sb.Append(DateTime.Now.ToString("HH:mm:ss ")).Append(l).Append("\r\n");
                File.AppendAllText(Path.Combine(logDir, "game-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log"), sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }


    }
}
