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
    partial class MainForm
    {
        async void DetectJavaAsync()
        {
            var list = await Task.Run(() => JavaUtil.Detect());
            if (IsDisposed) return;
            if (list.Count > 0)
            {
                cfg.JavaPath = list[0];
                lblJava.Text = "Java：" + cfg.JavaPath + "（自动检测）";
                txtJava.Text = cfg.JavaPath;
            }
            else lblJava.Text = "Java：未检测到，请到「设置」配置或下载";
        }

        // ---------------- 设置页 ----------------
        UiCard cardGameDir, cardJava, cardMirror, cardLaunchOpt, cardTools;
        UiButton btnGameBrowse, btnGameOpen, btnJavaBrowse, btnJavaDetect;

        void BuildSettings()
        {
            setPage = new UiPage();
            setPage.AutoScroll = true;
            setPage.HeaderTitle = "设置";
            setPage.HeaderSubtitle = "目录、运行时、下载源与启动参数";

            // ---------- 游戏目录 ----------
            cardGameDir = NewCard(setPage, "游戏目录", 60, 80);
            txtGameDir = new UiTextField(); txtGameDir.SetBounds(20, 36, 600, 34);
            cardGameDir.Controls.Add(txtGameDir);
            UiButton b1 = new UiButton(); b1.Text = "浏览"; b1.Icon = UiIcon.Folder; b1.SetBounds(630, 35, 96, 36);
            btnGameBrowse = b1;
            b1.Click += delegate
            {
                var d = new FolderBrowserDialog();
                d.Description = "选择游戏目录（.minecraft）";
                try { d.SelectedPath = cfg.GameDir; } catch { }
                if (d.ShowDialog(this) == DialogResult.OK) { txtGameDir.Text = d.SelectedPath; }
            };
            cardGameDir.Controls.Add(b1);
            UiButton b1b = new UiButton(); b1b.Text = "打开目录"; b1b.SetBounds(736, 35, 110, 36);
            btnGameOpen = b1b;
            b1b.Click += delegate
            {
                try
                {
                    if (!Directory.Exists(cfg.GameDir)) Directory.CreateDirectory(cfg.GameDir);
                    Process.Start("explorer.exe", CommandLine.Quote(cfg.GameDir));
                }
                catch (Exception ex) { MessageBox.Show(ex.Message); }
            };
            cardGameDir.Controls.Add(b1b);

            // ---------- Java 运行时 ----------
            cardJava = NewCard(setPage, "Java 运行时（javaw.exe 路径）", 152, 160);
            txtJava = new UiTextField(); txtJava.SetBounds(20, 36, 600, 34);
            cardJava.Controls.Add(txtJava);
            UiButton b2 = new UiButton(); b2.Text = "浏览"; b2.SetBounds(630, 35, 96, 36);
            btnJavaBrowse = b2;
            b2.Click += delegate
            {
                var d = new OpenFileDialog(); d.Filter = "javaw.exe|javaw.exe|java.exe|java.exe";
                try { d.InitialDirectory = Path.GetDirectoryName(cfg.JavaPath); } catch { }
                if (d.ShowDialog(this) == DialogResult.OK) txtJava.Text = d.FileName;
            };
            cardJava.Controls.Add(b2);
            UiButton b2b = new UiButton(); b2b.Text = "自动检测"; b2b.Icon = UiIcon.Search; b2b.SetBounds(736, 35, 110, 36);
            btnJavaDetect = b2b;
            ToolTip ttDetect = new ToolTip();
            ttDetect.SetToolTip(b2b, "扫描 PATH、官方 runtime 与已下载 JRE，逐一探测版本后选用第一个");
            b2b.Click += async delegate
            {
                b2b.Enabled = false;
                try
                {
                    var result = await Task.Run(() =>
                    {
                        var found = JavaUtil.Detect();
                        var majors = new List<int>();
                        foreach (string p in found) majors.Add(JavaUtil.GetMajor(p));
                        return new KeyValuePair<List<string>, List<int>>(found, majors);
                    });
                    var detected = result.Key;
                    if (detected.Count == 0) { MessageBox.Show("没有检测到任何 Java。"); return; }
                    var sb = new StringBuilder();
                    for (int i = 0; i < detected.Count; i++) sb.AppendLine((i + 1) + ". " + detected[i] + "  (Java " + result.Value[i] + ")");
                    sb.AppendLine("已选用第 1 个。");
                    MessageBox.Show(sb.ToString(), "检测结果");
                    txtJava.Text = detected[0];
                }
                finally { b2b.Enabled = true; }
            };
            cardJava.Controls.Add(b2b);

            Label l2c = MkLabel("没有 Java 或版本不匹配？下载 Temurin JRE：", 20, 88, 360);
            l2c.ForeColor = Theme.Muted;
            l2c.Font = Theme.Caption;
            cardJava.Controls.Add(l2c);
            UiButton bj8 = new UiButton(); bj8.Text = "Java 8"; bj8.SetBounds(20, 112, 90, 32);
            UiButton bj17 = new UiButton(); bj17.Text = "Java 17"; bj17.SetBounds(118, 112, 90, 32);
            UiButton bj21 = new UiButton(); bj21.Text = "Java 21"; bj21.SetBounds(216, 112, 90, 32);
            UiButton bjSelected = new UiButton(); bjSelected.Text = "按版本下载"; bjSelected.SetBounds(314, 112, 120, 32);
            bj8.Click += delegate { DownloadJava(8); };
            bj17.Click += delegate { DownloadJava(17); };
            bj21.Click += delegate { DownloadJava(21); };
            cardJava.Controls.Add(bj8); cardJava.Controls.Add(bj17); cardJava.Controls.Add(bj21);
            ToolTip ttJre = new ToolTip();
            ttJre.SetToolTip(bj8, "下载 Temurin JRE 8（1.16 及更早版本）");
            ttJre.SetToolTip(bj17, "下载 Temurin JRE 17（1.17 – 1.20.4）");
            ttJre.SetToolTip(bj21, "下载 Temurin JRE 21（1.20.5 及更新版本）");
            ttJre.SetToolTip(bjSelected, "读取主页所选版本的 Java 要求，下载匹配的 Temurin JRE");
            bjSelected.Click += delegate {
                string selected = SelectedVersion();
                if (selected == null) { MessageBox.Show("请先在主页选择已安装的版本。"); return; }
                try { DownloadJava((int)Json.L(Json.O(LaunchPlanner.LoadMerged(cfg.GameDir, selected), "javaVersion"), "majorVersion", 8)); }
                catch (Exception ex) { MessageBox.Show(ex.Message); }
            };
            cardJava.Controls.Add(bjSelected);

            // ---------- 下载源 ----------
            cardMirror = NewCard(setPage, "下载源（国内网络建议 BMCLAPI）", 324, 82);
            rbOfficial = new UiRadioButton(); rbOfficial.Text = "官方源"; rbOfficial.SetBounds(20, 36, 110, 26);
            rbBmcl = new UiRadioButton(); rbBmcl.Text = "BMCLAPI（国内加速）"; rbBmcl.SetBounds(140, 36, 220, 26);
            ToolTip ttMirror = new ToolTip();
            ttMirror.SetToolTip(rbOfficial, "Mojang 官方服务器，海外网络适用");
            ttMirror.SetToolTip(rbBmcl, "国内镜像，下载速度与稳定性通常更好");
            cardMirror.Controls.Add(rbOfficial); cardMirror.Controls.Add(rbBmcl);

            // ---------- 启动参数与窗口 ----------
            cardLaunchOpt = NewCard(setPage, "额外 JVM 参数（可选，如 -Dfile.encoding=UTF-8）", 418, 190);
            txtJvm = new UiTextField(); txtJvm.SetBounds(20, 36, 600, 34);
            cardLaunchOpt.Controls.Add(txtJvm);
            ToolTip ttJvm = new ToolTip();
            ttJvm.SetToolTip(txtJvm, "追加到游戏 JVM 命令行，留空使用默认；内存由主页滑块控制，不要在这里写 -Xmx");

            Label l5 = MkLabel("游戏窗口大小", 20, 84, 140);
            l5.ForeColor = Theme.Muted;
            l5.Font = Theme.Caption;
            cardLaunchOpt.Controls.Add(l5);
            numW = new UiNumeric(); numW.SetBounds(20, 106, 92, 32); numW.Minimum = 0; numW.Maximum = 7680;
            Label l5b = MkLabel("×", 120, 110, 16); cardLaunchOpt.Controls.Add(l5b);
            numH = new UiNumeric(); numH.SetBounds(142, 106, 92, 32); numH.Minimum = 0; numH.Maximum = 4320;
            Label l5c = MkLabel("(0 = 跟随游戏默认)", 244, 110, 220); l5c.ForeColor = Theme.Muted; l5c.Font = Theme.Caption;
            cardLaunchOpt.Controls.Add(l5c);
            cardLaunchOpt.Controls.Add(numW); cardLaunchOpt.Controls.Add(numH);

            chkCapture = new UiCheckBox();
            chkCapture.Text = "捕获游戏输出日志（排错用，关闭可略微提速）";
            chkCapture.SetBounds(20, 148, 420, 24);
            cardLaunchOpt.Controls.Add(chkCapture);

            // ---------- 维护与扩展 ----------
            cardTools = NewCard(setPage, "校验、模组与 AI 集成", 620, 132);
            UiButton bVerify = new UiButton(); bVerify.Text = "强制完整校验"; bVerify.Icon = UiIcon.Shield;
            bVerify.SetBounds(20, 36, 160, 36);
            ToolTip ttVerify = new ToolTip();
            ttVerify.SetToolTip(bVerify, "清空持久校验缓存，下次启动对全部文件重新做完整哈希校验");
            bVerify.Click += delegate {
                try { VerifyStore.InvalidateAll(); MessageBox.Show("已清空校验缓存，下次启动将全量校验。", "提示"); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "失败"); }
            };
            cardTools.Controls.Add(bVerify);

            var modManager = new UiButton { Name = "modManagerButton", Text = "第三方模组管理", Icon = UiIcon.Package };
            modManager.SetBounds(190, 36, 170, 36);
            modManager.Click += delegate {
                try {
                    string version = SelectedVersion();
                    if (version == null) throw new InvalidOperationException("请先在主页选择已安装版本。");
                    var snapshot = cfg.Snapshot(); snapshot.VersionIsolation = chkIsolation.Checked;
                    using (var dialog = new ModManagerForm(new ModManagement(snapshot, version), () => gameProc != null && !gameProc.HasExited))
                        dialog.ShowDialog(this);
                } catch (Exception ex) { MessageBox.Show(ex.Message, "模组管理"); }
            };
            cardTools.Controls.Add(modManager);

            UiButton aiInstall = new UiButton { Name = "aiInstallButton", Text = "安装 AI 模组", Icon = UiIcon.Spark };
            aiInstall.SetBounds(370, 36, 150, 36);
            aiInstall.Click += async delegate {
                if (gameProc != null && !gameProc.HasExited) { MessageBox.Show("请先退出游戏，再更新模组。"); return; }
                string version = SelectedVersion();
                if (version == null) { MessageBox.Show("请先选择 Minecraft 1.21.1 Fabric 版本。"); return; }
                var snapshot = cfg.Snapshot(); snapshot.VersionIsolation = chkIsolation.Checked;
                try {
                    SetBusy(true);
                    string path = await Task.Run(() => AiIntegration.Install(snapshot, version));
                    if (!IsDisposed) MessageBox.Show("AI 模组已安装。\n点击「启动 AI 桥接」后，进游戏输入 /scai ask 任务。\n配置：" + path);
                } catch (Exception ex) { if (!IsDisposed) MessageBox.Show(ex.Message, "AI 模组安装失败"); }
                finally { SetBusy(false); }
            };
            cardTools.Controls.Add(aiInstall);

            UiButton aiBridge = new UiButton { Name = "aiBridgeButton", Text = "启动 AI 桥接", Icon = UiIcon.Link };
            aiBridge.SetBounds(530, 36, 150, 36);
            aiBridge.Click += delegate {
                try {
                    string version = SelectedVersion();
                    if (version == null) throw new InvalidOperationException("请先选择版本。");
                    var snapshot = cfg.Snapshot(); snapshot.VersionIsolation = chkIsolation.Checked;
                    string path = FileSafety.Under(AiIntegration.GameDirectory(snapshot, version), "config/suyuncraft-ai.json");
                    if (!File.Exists(path)) throw new FileNotFoundException("请先安装 AI 模组。");
                    string script = Path.Combine(AiIntegration.BundleDirectory(), "start-bridge.ps1");
                    Process.Start(new ProcessStartInfo("powershell.exe", CommandLine.Join(new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-ConfigPath", path })) { UseShellExecute = true });
                } catch (Exception ex) { MessageBox.Show(ex.Message, "AI 桥接"); }
            };
            cardTools.Controls.Add(aiBridge);

            UiButton bSave = new UiButton(); bSave.Text = "保存设置"; bSave.Kind = UiButtonKind.Primary;
            bSave.SetBounds(20, 84, 160, 38);
            bSave.Click += delegate
            {
                cfg.GameDir = txtGameDir.Text.Trim().Length > 0 ? txtGameDir.Text.Trim() : cfg.GameDir;
                cfg.JavaPath = txtJava.Text.Trim();
                cfg.Mirror = rbBmcl.Checked ? Mirrors.Bmclapi : Mirrors.Official;
                cfg.ExtraJvm = txtJvm.Text.Trim();
                cfg.WindowWidth = (int)numW.Value; cfg.WindowHeight = (int)numH.Value;
                cfg.CaptureGameLog = chkCapture.Checked;
                cfg.Save();
                Net.ClearManifestCache();
                ScanLocalVersions();
                lblJava.Text = string.IsNullOrEmpty(cfg.JavaPath) ? "Java：未设置" : "Java：" + cfg.JavaPath;
                MessageBox.Show("已保存。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            cardTools.Controls.Add(bSave);

            lblSetInfo = MkLabel("提示：配置文件在 %APPDATA%\\SuyunCraft\\launcher_config.json", 0, 762, 760);
            lblSetInfo.ForeColor = Theme.TextDisabled;
            lblSetInfo.Font = Theme.Caption;
            setPage.Controls.Add(lblSetInfo);

            setPage.Resize += delegate { LayoutSettings(); };
            LayoutSettings();

            // 载入当前值
            txtGameDir.Text = cfg.GameDir;
            txtJava.Text = cfg.JavaPath;
            txtJvm.Text = cfg.ExtraJvm;
            if (cfg.Mirror == Mirrors.Official) rbOfficial.Checked = true; else rbBmcl.Checked = true;
            numW.Value = Math.Min(7680, Math.Max(0, cfg.WindowWidth));
            numH.Value = Math.Min(4320, Math.Max(0, cfg.WindowHeight));
            chkCapture.Checked = cfg.CaptureGameLog;
        }

        UiCard NewCard(Panel host, string caption, int top, int height)
        {
            UiCard card = new UiCard();
            card.SetBounds(0, top, 600, height);
            Label l = MkLabel(caption, 20, 12, 460);
            l.ForeColor = Theme.Muted;
            l.Font = Theme.Caption;
            card.Controls.Add(l);
            host.Controls.Add(card);
            return card;
        }

        void LayoutSettings()
        {
            if (suppressLayout) return;
            // 页面内容总是需要纵向滚动，先给滚动条预留宽度，避免再冒出横向滚动条。
            int w = setPage.ClientSize.Width - Ui.Px(SystemInformation.VerticalScrollBarWidth) - Ui.Px(2);
            if (w <= 0) return;
            int[] tops = { Ui.Px(60), Ui.Px(152), Ui.Px(324), Ui.Px(418), Ui.Px(620) };
            UiCard[] cards = { cardGameDir, cardJava, cardMirror, cardLaunchOpt, cardTools };
            for (int i = 0; i < cards.Length; i++)
            {
                cards[i].SetBounds(0, tops[i], w, cards[i].Height);
            }
            int right = w - Ui.Px(20);
            int fieldW = Math.Max(Ui.Px(240), right - Ui.Px(240));
            txtGameDir.Width = fieldW;
            txtJava.Width = fieldW;
            txtJvm.Width = Math.Max(Ui.Px(240), w - Ui.Px(40));
            lblSetInfo.Width = Math.Max(Ui.Px(200), w - Ui.Px(20));

            btnJavaDetect.Left = right - btnJavaDetect.Width;
            btnJavaBrowse.Left = btnJavaDetect.Left - Ui.Px(10) - btnJavaBrowse.Width;
            btnGameOpen.Left = right - btnGameOpen.Width;
            btnGameBrowse.Left = btnGameOpen.Left - Ui.Px(10) - btnGameBrowse.Width;
        }

        async void DownloadJava(int major)
        {
            if (operationBusy) return;
            try
            {
                SetBusy(true);
                statusBar.Text = "正在获取 Java " + major + " 下载信息…";
                string architecture = Environment.Is64BitOperatingSystem ? "x64" : "x86";
                string metadataUrl = "https://api.adoptium.net/v3/assets/latest/" + major +
                    "/hotspot?architecture=" + architecture + "&image_type=jre&os=windows&vendor=eclipse";
                string metadata = await Task.Run(() => Net.DownloadString(metadataUrl, Mirrors.Official));
                object[] releases = Json.A(Json.Parse(metadata));
                if (releases == null || releases.Length == 0) throw new InvalidDataException("该平台没有对应 JRE");
                object package = Json.O(Json.O(releases[0], "binary"), "package");
                string checksum = Json.S(package, "checksum", "");
                if (!FileSafety.IsHash(checksum, 64)) throw new InvalidDataException("JRE 元数据缺少 SHA256");
                string javaRoot = FileSafety.Under(Config.BaseDir, "java");
                Directory.CreateDirectory(javaRoot);
                string name = "temurin-" + major + "-" + Guid.NewGuid().ToString("N");
                string destination = FileSafety.Under(javaRoot, name);
                string archive = FileSafety.Under(javaRoot, name + ".zip");
                try
                {
                    await RunDownloadsAsync(new List<DlItem> {
                        new DlItem { Url = Json.S(package, "link", ""), Dest = archive, Sha256 = checksum,
                            Size = Json.L(package, "size", 0), Desc = "Temurin Java " + major }
                    });
                    string java = await Task.Run(() => {
                        FileSafety.ExtractZip(archive, destination);
                        string path = FindFile(destination, "javaw.exe");
                        if (path == null || JavaUtil.GetMajor(path) != major) throw new InvalidDataException("JRE 解压后的版本不匹配");
                        return path;
                    });
                    if (IsDisposed) return;
                    txtJava.Text = java; cfg.JavaPath = java; cfg.Save(); lblJava.Text = "Java：" + java;
                    statusBar.Text = "Java " + major + " 安装完成";
                    MessageBox.Show("Java 已安装到：" + java, "完成");
                }
                finally { if (File.Exists(archive)) File.Delete(archive); }
            }
            catch (OperationCanceledException) { if (!IsDisposed) statusBar.Text = "Java 下载已取消"; }
            catch (Exception ex) { if (!IsDisposed) { statusBar.Text = "Java 下载失败"; MessageBox.Show(ex.Message, "Java 下载失败"); } }
            finally { SetBusy(false); }
        }

        static string FindFile(string root, string name)
        {
            try
            {
                string direct = Path.Combine(root, "bin", name);
                if (File.Exists(direct)) return direct;
                string[] files = Directory.GetFiles(root, name, SearchOption.AllDirectories);
                return files.Length > 0 ? files[0] : null;
            }
            catch { return null; }
        }
    }
}
