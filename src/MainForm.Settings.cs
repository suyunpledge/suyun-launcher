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
        void BuildSettings()
        {
            setPage = new Panel();
            setPage.Dock = DockStyle.Fill;
            setPage.AutoScroll = true;
            setPage.BackColor = System.Drawing.Color.Transparent;

            int y = 10;
            Label l1 = MkLabel("游戏目录", 0, y, 200); setPage.Controls.Add(l1); y += 26;
            txtGameDir = new TextBox(); txtGameDir.SetBounds(0, y, 600, 30);
            Theme.StyleInput(txtGameDir); setPage.Controls.Add(txtGameDir);
            Button b1 = new Button(); b1.Text = "浏览"; b1.SetBounds(610, y - 2, 70, 32);
            Theme.StyleButton(b1, null);
            b1.Click += delegate
            {
                var d = new FolderBrowserDialog();
                d.Description = "选择游戏目录（.minecraft）";
                try { d.SelectedPath = cfg.GameDir; } catch { }
                if (d.ShowDialog(this) == DialogResult.OK) { txtGameDir.Text = d.SelectedPath; }
            };
            setPage.Controls.Add(b1);
            Button b1b = new Button(); b1b.Text = "打开目录"; b1b.SetBounds(690, y - 2, 90, 32);
            Theme.StyleButton(b1b, null);
            b1b.Click += delegate
            {
                try
                {
                    if (!Directory.Exists(cfg.GameDir)) Directory.CreateDirectory(cfg.GameDir);
                    Process.Start("explorer.exe", CommandLine.Quote(cfg.GameDir));
                }
                catch (Exception ex) { MessageBox.Show(ex.Message); }
            };
            setPage.Controls.Add(b1b);
            y += 44;

            Label l2 = MkLabel("Java（javaw.exe 路径）", 0, y, 300); setPage.Controls.Add(l2); y += 26;
            txtJava = new TextBox(); txtJava.SetBounds(0, y, 600, 30);
            Theme.StyleInput(txtJava); setPage.Controls.Add(txtJava);
            Button b2 = new Button(); b2.Text = "浏览"; b2.SetBounds(610, y - 2, 70, 32);
            Theme.StyleButton(b2, null);
            b2.Click += delegate
            {
                var d = new OpenFileDialog(); d.Filter = "javaw.exe|javaw.exe|java.exe|java.exe";
                try { d.InitialDirectory = Path.GetDirectoryName(cfg.JavaPath); } catch { }
                if (d.ShowDialog(this) == DialogResult.OK) txtJava.Text = d.FileName;
            };
            setPage.Controls.Add(b2);
            Button b2b = new Button(); b2b.Text = "自动检测"; b2b.SetBounds(690, y - 2, 90, 32);
            Theme.StyleButton(b2b, null);
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
            setPage.Controls.Add(b2b);
            y += 44;

            Label l2c = MkLabel("没有 Java 或版本不匹配？下载 Temurin JRE：", 0, y, 320); setPage.Controls.Add(l2c);
            Button bj8 = new Button(); bj8.Text = "Java 8"; bj8.SetBounds(330, y - 4, 80, 30); Theme.StyleButton(bj8, null);
            Button bj17 = new Button(); bj17.Text = "Java 17"; bj17.SetBounds(418, y - 4, 80, 30); Theme.StyleButton(bj17, null);
            Button bj21 = new Button(); bj21.Text = "Java 21"; bj21.SetBounds(506, y - 4, 80, 30); Theme.StyleButton(bj21, null);
            bj8.Click += delegate { DownloadJava(8); };
            bj17.Click += delegate { DownloadJava(17); };
            bj21.Click += delegate { DownloadJava(21); };
            setPage.Controls.Add(bj8); setPage.Controls.Add(bj17); setPage.Controls.Add(bj21);
            ToolTip ttJre = new ToolTip();
            ttJre.SetToolTip(bj8, "下载 Temurin JRE 8（1.16 及更早版本）");
            ttJre.SetToolTip(bj17, "下载 Temurin JRE 17（1.17 – 1.20.4）");
            ttJre.SetToolTip(bj21, "下载 Temurin JRE 21（1.20.5 及更新版本）");
            var bjSelected = new Button { Text = "按版本下载" };
            bjSelected.SetBounds(590, y - 4, 106, 30); Theme.StyleButton(bjSelected, null);
            ttJre.SetToolTip(bjSelected, "读取主页所选版本的 Java 要求，下载匹配的 Temurin JRE");
            bjSelected.Click += delegate {
                string selected = SelectedVersion();
                if (selected == null) { MessageBox.Show("请先在主页选择已安装的版本。"); return; }
                try { DownloadJava((int)Json.L(Json.O(LaunchPlanner.LoadMerged(cfg.GameDir, selected), "javaVersion"), "majorVersion", 8)); }
                catch (Exception ex) { MessageBox.Show(ex.Message); }
            };
            setPage.Controls.Add(bjSelected);
            y += 38;

            Label l3 = MkLabel("下载源（国内网络建议 BMCLAPI）", 0, y, 300); setPage.Controls.Add(l3); y += 26;
            rbOfficial = new RadioButton(); rbOfficial.Text = "官方源"; rbOfficial.SetBounds(0, y, 90, 24); rbOfficial.ForeColor = Theme.Text;
            rbBmcl = new RadioButton(); rbBmcl.Text = "BMCLAPI（国内加速）"; rbBmcl.SetBounds(100, y, 200, 24); rbBmcl.ForeColor = Theme.Text;
            ToolTip ttMirror = new ToolTip();
            ttMirror.SetToolTip(rbOfficial, "Mojang 官方服务器，海外网络适用");
            ttMirror.SetToolTip(rbBmcl, "国内镜像，下载速度与稳定性通常更好");
            setPage.Controls.Add(rbOfficial); setPage.Controls.Add(rbBmcl); y += 36;

            Label l4 = MkLabel("额外 JVM 参数（可选，如 -Dfile.encoding=UTF-8）", 0, y, 520); setPage.Controls.Add(l4); y += 26;
            txtJvm = new TextBox(); txtJvm.SetBounds(0, y, 600, 30);
            Theme.StyleInput(txtJvm); setPage.Controls.Add(txtJvm);
            ToolTip ttJvm = new ToolTip();
            ttJvm.SetToolTip(txtJvm, "追加到游戏 JVM 命令行，留空使用默认；内存由主页滑块控制，不要在这里写 -Xmx");
            y += 44;

            Label l5 = MkLabel("游戏窗口大小", 0, y, 150); setPage.Controls.Add(l5);
            numW = new NumericUpDown(); numW.SetBounds(160, y - 3, 80, 28); numW.Minimum = 0; numW.Maximum = 7680;
            Label l5b = MkLabel("×", 250, y, 16); setPage.Controls.Add(l5b);
            numH = new NumericUpDown(); numH.SetBounds(272, y - 3, 80, 28); numH.Minimum = 0; numH.Maximum = 4320;
            setPage.Controls.Add(numW); setPage.Controls.Add(numH);
            Label l5c = MkLabel("(0 = 跟随游戏默认)", 360, y, 200); l5c.ForeColor = Theme.Muted; setPage.Controls.Add(l5c);
            y += 40;

            chkCapture = new CheckBox(); chkCapture.Text = "捕获游戏输出日志（排错用，关闭可略微提速）";
            chkCapture.SetBounds(0, y, 420, 24); chkCapture.ForeColor = Theme.Text;
            setPage.Controls.Add(chkCapture); y += 40;

            Button bVerify = new Button(); bVerify.Text = "🔍 强制完整校验";
            bVerify.SetBounds(170, y, 150, 40);
            Theme.StyleButton(bVerify, null);
            ToolTip ttVerify = new ToolTip();
            ttVerify.SetToolTip(bVerify, "清空持久校验缓存，下次启动对全部文件重新做完整哈希校验");
            bVerify.Click += delegate {
                try { VerifyStore.InvalidateAll(); MessageBox.Show("已清空校验缓存，下次启动将全量校验。", "提示"); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "失败"); }
            };
            setPage.Controls.Add(bVerify);
            Button bSave = new Button(); bSave.Text = "保存设置"; bSave.SetBounds(0, y, 150, 40);
            Theme.StyleButton(bSave, Theme.Accent); bSave.ForeColor = System.Drawing.Color.White;
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
            setPage.Controls.Add(bSave);

            lblSetInfo = MkLabel("提示：配置文件在 %APPDATA%\\SuyunCraft\\launcher_config.json", 170, y + 10, 600);
            lblSetInfo.ForeColor = Theme.Muted;
            setPage.Controls.Add(lblSetInfo);

            Button aiInstall = new Button(); aiInstall.Name = "aiInstallButton"; aiInstall.Text = "安装 AI 模组";
            aiInstall.SetBounds(0, y + 58, 150, 36); Theme.StyleButton(aiInstall, Theme.Card);
            aiInstall.Click += async delegate {
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
            setPage.Controls.Add(aiInstall);
            Button aiBridge = new Button(); aiBridge.Name = "aiBridgeButton"; aiBridge.Text = "启动 AI 桥接";
            aiBridge.SetBounds(165, y + 58, 150, 36); Theme.StyleButton(aiBridge, Theme.Card);
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
            setPage.Controls.Add(aiBridge);

            setPage.Resize += delegate {
                int width = Math.Max(360, setPage.ClientSize.Width - 180);
                txtGameDir.Width = width; b1.Left = width + 10; b1b.Left = width + 90;
                txtJava.Width = width; b2.Left = width + 10; b2b.Left = width + 90;
                txtJvm.Width = Math.Min(600, setPage.ClientSize.Width);
                lblSetInfo.Width = Math.Max(200, setPage.ClientSize.Width - 180);
                lblSetInfo.AutoEllipsis = true;
            };

            // 载入当前值
            txtGameDir.Text = cfg.GameDir;
            txtJava.Text = cfg.JavaPath;
            txtJvm.Text = cfg.ExtraJvm;
            if (cfg.Mirror == Mirrors.Official) rbOfficial.Checked = true; else rbBmcl.Checked = true;
            numW.Value = Math.Min(7680, Math.Max(0, cfg.WindowWidth));
            numH.Value = Math.Min(4320, Math.Max(0, cfg.WindowHeight));
            chkCapture.Checked = cfg.CaptureGameLog;
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
