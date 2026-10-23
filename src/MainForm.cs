using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
        public static readonly System.Drawing.Color Bg = System.Drawing.Color.FromArgb(30, 31, 36);
        public static readonly System.Drawing.Color Panel = System.Drawing.Color.FromArgb(38, 39, 44);
        public static readonly System.Drawing.Color Card = System.Drawing.Color.FromArgb(47, 48, 54);
        public static readonly System.Drawing.Color Border = System.Drawing.Color.FromArgb(60, 62, 68);
        public static readonly System.Drawing.Color Text = System.Drawing.Color.FromArgb(230, 231, 235);
        public static readonly System.Drawing.Color Muted = System.Drawing.Color.FromArgb(140, 143, 150);
        public static readonly System.Drawing.Color Accent = System.Drawing.Color.FromArgb(63, 185, 80);
        public static readonly System.Drawing.Color AccentDark = System.Drawing.Color.FromArgb(43, 140, 56);
        public static readonly System.Drawing.Color Blue = System.Drawing.Color.FromArgb(70, 130, 245);

        public static readonly System.Drawing.Font Ui = new System.Drawing.Font("Microsoft YaHei UI", 9.75f);
        public static readonly System.Drawing.Font UiBold = new System.Drawing.Font("Microsoft YaHei UI", 9.75f, System.Drawing.FontStyle.Bold);
        public static readonly System.Drawing.Font Input = new System.Drawing.Font("Microsoft YaHei UI", 10f);
        public static readonly System.Drawing.Font NavBold = new System.Drawing.Font("Microsoft YaHei UI", 10.5f, System.Drawing.FontStyle.Bold);
        public static readonly System.Drawing.Font TitleBold = new System.Drawing.Font("Microsoft YaHei UI", 12.5f, System.Drawing.FontStyle.Bold);
        public static readonly System.Drawing.Font HeadingBold = new System.Drawing.Font("Microsoft YaHei UI", 13f, System.Drawing.FontStyle.Bold);
        public static readonly System.Drawing.Font LaunchBold = new System.Drawing.Font("Microsoft YaHei UI", 15f, System.Drawing.FontStyle.Bold);
        public static readonly System.Drawing.Font Console = new System.Drawing.Font("Consolas", 9f);

        public static void StyleButton(Button b, System.Drawing.Color? back)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = back.HasValue ? back.Value : Card;
            b.ForeColor = Text;
            b.FlatAppearance.BorderColor = Border;
            b.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(62, 63, 70);
            b.Cursor = Cursors.Hand;
            b.Font = UiBold;
        }
        public static void StyleInput(TextBox t)
        {
            t.BackColor = System.Drawing.Color.FromArgb(24, 25, 28);
            t.ForeColor = Text;
            t.BorderStyle = BorderStyle.FixedSingle;
            t.Font = Input;
        }
        public static void StyleCombo(ComboBox c)
        {
            c.BackColor = System.Drawing.Color.FromArgb(24, 25, 28);
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
        CancellationTokenSource launchCancellation;
        Panel navPane, content;
        Button navHome, navDownload, navSettings;
        Panel navHomeBar, navDownloadBar, navSettingsBar;
        Panel homePage, dlPage, setPage;
        Label statusBar;

        // home
        TextBox txtUser;
        ComboBox cmbVersion;
        Button btnRefreshVer, btnLaunch;
        TrackBar trackMem; Label lblMem, lblJava;
        CheckBox chkAutoMem, chkIsolation;
        TextBox txtGameLog;
        Process gameProc;
        readonly ConcurrentQueue<string> logQueue = new ConcurrentQueue<string>();
        System.Windows.Forms.Timer logTimer;

        // download
        ComboBox cmbFilter; TextBox txtSearch;
        ListView lvVersions;
        CheckBox chkFabric; ComboBox cmbFabric;
        Button btnInstall;
        ThemeProgressBar bar; Label lblDl;
        Button btnCancelDl;
        DownloadManager mgr;
        System.Windows.Forms.Timer dlTimer;
        long lastBytes; DateTime lastSample;
        List<ManifestVer> manifest = new List<ManifestVer>();
        List<string> fabricLoaders;
        HashSet<string> localInstalled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // settings
        TextBox txtGameDir, txtJava, txtJvm;
        RadioButton rbOfficial, rbBmcl;
        CheckBox chkCapture; NumericUpDown numW, numH;
        Label lblSetInfo;

        public MainForm()
        {
            cfg = Config.Load();
            Text = "溯昀启动器 SuyunCraft v" + Program.AppVersion;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new System.Drawing.Size(920, 600);
            MinimumSize = new System.Drawing.Size(880, 560);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui;
            KeyPreview = true;

            BuildLayout();
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
            navPane.Width = 148;
            navPane.BackColor = Theme.Panel;
            Controls.Add(navPane);

            navHome = NavButton("主页", 90, out navHomeBar);
            navDownload = NavButton("下载", 146, out navDownloadBar);
            navSettings = NavButton("设置", 202, out navSettingsBar);
            navPane.Controls.Add(navHome);
            navPane.Controls.Add(navDownload);
            navPane.Controls.Add(navSettings);
            Label ttl2 = new Label();
            ttl2.Text = "溯昀启动器\nSuyunCraft";
            ttl2.ForeColor = Theme.Accent;
            ttl2.Font = Theme.TitleBold;
            ttl2.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            ttl2.SetBounds(0, 10, 148, 60);
            navPane.Controls.Add(ttl2);

            navHome.Click += delegate { SwitchPage("home"); };
            navDownload.Click += delegate { SwitchPage("dl"); };
            navSettings.Click += delegate { SwitchPage("set"); };

            // 内容区
            content = new Panel();
            content.Dock = DockStyle.Fill;
            content.BackColor = Theme.Bg;
            content.Padding = new Padding(18, 14, 18, 8);
            Controls.Add(content);
            content.BringToFront();

            // 状态栏
            statusBar = new Label();
            statusBar.Dock = DockStyle.Bottom;
            statusBar.Height = 26;
            statusBar.BackColor = Theme.Panel;
            statusBar.ForeColor = Theme.Muted;
            statusBar.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            statusBar.Padding = new Padding(8, 0, 0, 0);
            statusBar.Text = "就绪";
            Controls.Add(statusBar);

            BuildHome();
            BuildDownload();
            BuildSettings();

            content.Controls.Add(homePage);
            content.Controls.Add(dlPage);
            content.Controls.Add(setPage);
            SwitchPage("home");
        }

        Button NavButton(string text, int y, out Panel indicator)
        {
            Button b = new Button();
            b.Text = text;
            b.SetBounds(8, y, 132, 42);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = Theme.Panel;
            b.ForeColor = Theme.Text;
            b.Font = Theme.NavBold;
            b.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            b.Padding = new Padding(14, 0, 0, 0);
            b.Cursor = Cursors.Hand;
            indicator = new Panel();
            indicator.Dock = DockStyle.Left;
            indicator.Width = 4;
            indicator.BackColor = Theme.Accent;
            indicator.Visible = false;
            b.Controls.Add(indicator);
            b.MouseEnter += delegate { if (b.BackColor == Theme.Panel) b.BackColor = Theme.Card; };
            b.MouseLeave += delegate { if (b.BackColor == Theme.Card) b.BackColor = Theme.Panel; };
            return b;
        }

        void SwitchPage(string key)
        {
            homePage.Visible = key == "home";
            dlPage.Visible = key == "dl";
            setPage.Visible = key == "set";
            System.Drawing.Color sel = Theme.AccentDark, unsel = Theme.Panel;
            navHome.BackColor = key == "home" ? sel : unsel;
            navDownload.BackColor = key == "dl" ? sel : unsel;
            navSettings.BackColor = key == "set" ? sel : unsel;
            navHome.ForeColor = key == "home" ? System.Drawing.Color.White : Theme.Text;
            navDownload.ForeColor = key == "dl" ? System.Drawing.Color.White : Theme.Text;
            navSettings.ForeColor = key == "set" ? System.Drawing.Color.White : Theme.Text;
            navHomeBar.Visible = key == "home";
            navDownloadBar.Visible = key == "dl";
            navSettingsBar.Visible = key == "set";
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyDarkMode(this);
        }

        static void ApplyDarkMode(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is ListView) DarkMode.Apply(c, "DarkMode_ItemsView");
                else if (c is TextBox) DarkMode.Apply(c, "DarkMode_Explorer");
                else if (c is ComboBox) DarkMode.Apply(c, "DarkMode_CFD");
                if (c.HasChildren) ApplyDarkMode(c);
            }
        }

        Label MkLabel(string text, int x, int y, int w)
        {
            Label l = new Label();
            l.Text = text; l.SetBounds(x, y, w, 22);
            l.ForeColor = Theme.Text; l.BackColor = System.Drawing.Color.Transparent;
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
                            var j = Json.D(Json.Parse(File.ReadAllText(jp, Encoding.UTF8)));
                            v.IsFabric = (Json.S(j, "mainClass", "").IndexOf("fabric", StringComparison.OrdinalIgnoreCase) >= 0) || id.ToLower().Contains("fabric");
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
                cmbVersion.Items.Add((v.IsFabric ? "[F] " : "") + v.Id);
            if (cmbVersion.Items.Count > 0)
            {
                int idx = 0;
                for (int i = 0; i < vers.Count; i++) if (vers[i].Id == cfg.LastVersion) { idx = i; break; }
                cmbVersion.SelectedIndex = idx;
            }
            statusBar.Text = "本地版本 " + vers.Count + " 个 · 游戏目录 " + cfg.GameDir;
        }

        string SelectedVersion()
        {
            if (cmbVersion.SelectedItem == null) return null;
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
            try
            {
                SetBusy(true);
                VerifyStore.Load();   // 持久校验缓存：命中的文件跳过哈希（P2）
                cfg.UserName = user; cfg.MemoryGb = memory; cfg.AutoMemory = chkAutoMem.Checked;
                cfg.VersionIsolation = isolation; cfg.LastVersion = version; cfg.Save();
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
                if (cfg.CaptureGameLog)
                {
                    process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) AppendLog("[out] " + e.Data); };
                    process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) AppendLog("[err] " + e.Data); };
                }
                process.Exited += delegate {
                    try { BeginInvoke((MethodInvoker)delegate {
                        FlushLog(true);
                        statusBar.Text = "游戏已退出（代码 " + process.ExitCode + "）";
                        btnLaunch.Text = "▶  启动游戏";
                        btnLaunch.Enabled = !operationBusy;
                    }); }
                    catch (InvalidOperationException) { }
                };
                launchCancellation.Token.ThrowIfCancellationRequested();
                // A failed start must not leave an unstarted Process in gameProc: HasExited would throw on the next click.
                try { if (!process.Start()) throw new InvalidOperationException("无法创建 Java 进程"); }
                catch { process.Dispose(); throw; }
                if (gameProc != null) gameProc.Dispose();
                gameProc = process;
                btnLaunch.Text = "●  游戏运行中";
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
