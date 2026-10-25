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
                lblJava.Text = "Java：" + cfg.JavaPath + "（Auto-detect）";
                txtJava.Text = cfg.JavaPath;
            }
            else lblJava.Text = "Java：not detected; configure in Settings or Download";
        }

        // ---------------- the Settings page ----------------
        UiCard cardGameDir, cardJava, cardMirror, cardLaunchOpt, cardTools;
        UiButton btnGameBrowse, btnGameOpen, btnJavaBrowse, btnJavaDetect;

        void BuildSettings()
        {
            setPage = new UiPage();
            setPage.AutoScroll = true;
            setPage.HeaderTitle = "Settings";
            setPage.HeaderSubtitle = "directory、Runtime, download source and launch arguments";

            // ---------- gamedirectory ----------
            cardGameDir = NewCard(setPage, "gamedirectory", 60, 80);
            txtGameDir = new UiTextField(); txtGameDir.SetBounds(20, 36, 600, 34);
            cardGameDir.Controls.Add(txtGameDir);
            UiButton b1 = new UiButton(); b1.Text = "Browse"; b1.Icon = UiIcon.Folder; b1.SetBounds(630, 35, 96, 36);
            btnGameBrowse = b1;
            b1.Click += delegate
            {
                var d = new FolderBrowserDialog();
                d.Description = "Choose the game directory（.minecraft）";
                try { d.SelectedPath = cfg.GameDir; } catch { }
                if (d.ShowDialog(this) == DialogResult.OK) { txtGameDir.Text = d.SelectedPath; }
            };
            cardGameDir.Controls.Add(b1);
            UiButton b1b = new UiButton(); b1b.Text = "Open directory"; b1b.SetBounds(736, 35, 110, 36);
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

            // ---------- Java runtime ----------
            cardJava = NewCard(setPage, "Java Runtime (javaw.exe path）", 152, 160);
            txtJava = new UiTextField(); txtJava.SetBounds(20, 36, 600, 34);
            cardJava.Controls.Add(txtJava);
            UiButton b2 = new UiButton(); b2.Text = "Browse"; b2.SetBounds(630, 35, 96, 36);
            btnJavaBrowse = b2;
            b2.Click += delegate
            {
                var d = new OpenFileDialog(); d.Filter = "javaw.exe|javaw.exe|java.exe|java.exe";
                try { d.InitialDirectory = Path.GetDirectoryName(cfg.JavaPath); } catch { }
                if (d.ShowDialog(this) == DialogResult.OK) txtJava.Text = d.FileName;
            };
            cardJava.Controls.Add(b2);
            UiButton b2b = new UiButton(); b2b.Text = "Auto-detect"; b2b.Icon = UiIcon.Search; b2b.SetBounds(736, 35, 110, 36);
            btnJavaDetect = b2b;
            ToolTip ttDetect = new ToolTip();
            ttDetect.SetToolTip(b2b, "Scan PATH, the official runtime and downloaded JREs; probe each and use the first match");
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
                    if (detected.Count == 0) { MessageBox.Show("Nothing detected Java。"); return; }
                    var sb = new StringBuilder();
                    for (int i = 0; i < detected.Count; i++) sb.AppendLine((i + 1) + ". " + detected[i] + "  (Java " + result.Value[i] + ")");
                    sb.AppendLine("Using the first match。");
                    MessageBox.Show(sb.ToString(), "detectResult");
                    txtJava.Text = detected[0];
                }
                finally { b2b.Enabled = true; }
            };
            cardJava.Controls.Add(b2b);

            Label l2c = MkLabel("No Java or wrong version？Download Temurin JRE：", 20, 88, 360);
            l2c.ForeColor = Theme.Muted;
            l2c.Font = Theme.Caption;
            cardJava.Controls.Add(l2c);
            UiButton bj8 = new UiButton(); bj8.Text = "Java 8"; bj8.SetBounds(20, 112, 90, 32);
            UiButton bj17 = new UiButton(); bj17.Text = "Java 17"; bj17.SetBounds(118, 112, 90, 32);
            UiButton bj21 = new UiButton(); bj21.Text = "Java 21"; bj21.SetBounds(216, 112, 90, 32);
            UiButton bjSelected = new UiButton(); bjSelected.Text = "Download per version"; bjSelected.SetBounds(314, 112, 120, 32);
            bj8.Click += delegate { DownloadJava(8); };
            bj17.Click += delegate { DownloadJava(17); };
            bj21.Click += delegate { DownloadJava(21); };
            cardJava.Controls.Add(bj8); cardJava.Controls.Add(bj17); cardJava.Controls.Add(bj21);
            ToolTip ttJre = new ToolTip();
            ttJre.SetToolTip(bj8, "Download Temurin JRE 8（1.16 or older)");
            ttJre.SetToolTip(bj17, "Download Temurin JRE 17（1.17 – 1.20.4）");
            ttJre.SetToolTip(bj21, "Download Temurin JRE 21（1.20.5 or newer)");
            ttJre.SetToolTip(bjSelected, "Read the Java requirement of the Home-selected version and download a matching Temurin JRE");
            bjSelected.Click += delegate {
                string selected = SelectedVersion();
                if (selected == null) { MessageBox.Show("Select an installed version on Home firstversion。"); return; }
                try { DownloadJava((int)Json.L(Json.O(LaunchPlanner.LoadMerged(cfg.GameDir, selected), "javaVersion"), "majorVersion", 8)); }
                catch (Exception ex) { MessageBox.Show(ex.Message); }
            };
            cardJava.Controls.Add(bjSelected);

            // ---------- Downloadsource ----------
            cardMirror = NewCard(setPage, "Downloadsource (BMCLAPI recommended in China BMCLAPI）", 324, 82);
            rbOfficial = new UiRadioButton(); rbOfficial.Text = "Official"; rbOfficial.SetBounds(20, 36, 110, 26);
            rbBmcl = new UiRadioButton(); rbBmcl.Text = "BMCLAPI (China CDN)"; rbBmcl.SetBounds(140, 36, 220, 26);
            ToolTip ttMirror = new ToolTip();
            ttMirror.SetToolTip(rbOfficial, "Mojang Official servers; better outside China");
            ttMirror.SetToolTip(rbBmcl, "China mirror; usually faster and more stable domestically");
            cardMirror.Controls.Add(rbOfficial); cardMirror.Controls.Add(rbBmcl);

            // ---------- Launchargumentsand window ----------
            cardLaunchOpt = NewCard(setPage, "Extra JVM arguments（optional, e.g. -Dfile.encoding=UTF-8）", 418, 190);
            txtJvm = new UiTextField(); txtJvm.SetBounds(20, 36, 600, 34);
            cardLaunchOpt.Controls.Add(txtJvm);
            ToolTip ttJvm = new ToolTip();
            ttJvm.SetToolTip(txtJvm, "Appended to the JVM command line; leave empty for defaults. Memory comes from the Home slider -Xmx");

            Label l5 = MkLabel("Game window size", 20, 84, 140);
            l5.ForeColor = Theme.Muted;
            l5.Font = Theme.Caption;
            cardLaunchOpt.Controls.Add(l5);
            numW = new UiNumeric(); numW.SetBounds(20, 106, 92, 32); numW.Minimum = 0; numW.Maximum = 7680;
            Label l5b = MkLabel("×", 120, 110, 16); cardLaunchOpt.Controls.Add(l5b);
            numH = new UiNumeric(); numH.SetBounds(142, 106, 92, 32); numH.Minimum = 0; numH.Maximum = 4320;
            Label l5c = MkLabel("(0 = follow the game default)", 244, 110, 220); l5c.ForeColor = Theme.Muted; l5c.Font = Theme.Caption;
            cardLaunchOpt.Controls.Add(l5c);
            cardLaunchOpt.Controls.Add(numW); cardLaunchOpt.Controls.Add(numH);

            chkCapture = new UiCheckBox();
            chkCapture.Text = "Capture game output for troubleshooting (off is slightly faster）";
            chkCapture.SetBounds(20, 148, 420, 24);
            cardLaunchOpt.Controls.Add(chkCapture);

            // ---------- Maintenance & extensions ----------
            cardTools = NewCard(setPage, "verification, mods and AI integration", 620, 132);
            UiButton bVerify = new UiButton(); bVerify.Text = "Force full verification"; bVerify.Icon = UiIcon.Shield;
            bVerify.SetBounds(20, 36, 160, 36);
            ToolTip ttVerify = new ToolTip();
            ttVerify.SetToolTip(bVerify, "Clear the persistent verify cache; next launch re-hashes every file");
            bVerify.Click += delegate {
                try { VerifyStore.InvalidateAll(); MessageBox.Show("Verify cache cleared; next launch performs a full check。", "Notice"); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "failed"); }
            };
            cardTools.Controls.Add(bVerify);

            var modManager = new UiButton { Name = "modManagerButton", Text = "Third-party mod manager", Icon = UiIcon.Package };
            modManager.SetBounds(190, 36, 170, 36);
            modManager.Click += delegate {
                try {
                    string version = SelectedVersion();
                    if (version == null) throw new InvalidOperationException("Select a version on Home firstInstalledversion。");
                    var snapshot = cfg.Snapshot(); snapshot.VersionIsolation = chkIsolation.Checked;
                    using (var dialog = new ModManagerForm(new ModManagement(snapshot, version), () => gameProc != null && !gameProc.HasExited))
                        dialog.ShowDialog(this);
                } catch (Exception ex) { MessageBox.Show(ex.Message, "modmanager"); }
            };
            cardTools.Controls.Add(modManager);

            UiButton aiInstall = new UiButton { Name = "aiInstallButton", Text = "Install AI mod", Icon = UiIcon.Spark };
            aiInstall.SetBounds(370, 36, 150, 36);
            aiInstall.Click += async delegate {
                if (gameProc != null && !gameProc.HasExited) { MessageBox.Show("Exit the game before updatingmod。"); return; }
                string version = SelectedVersion();
                if (version == null) { MessageBox.Show("Select a Minecraft 1.21.1 Fabric version first."); return; }
                var snapshot = cfg.Snapshot(); snapshot.VersionIsolation = chkIsolation.Checked;
                try {
                    SetBusy(true);
                    string path = await Task.Run(() => AiIntegration.Install(snapshot, version));
                    if (!IsDisposed) MessageBox.Show("AI modinstalled. \nAfter starting the AI bridge, type in game:  /scai ask task.\nConfig: " + path);
                } catch (Exception ex) { if (!IsDisposed) MessageBox.Show(ex.Message, "AI modInstallfailed"); }
                finally { SetBusy(false); }
            };
            cardTools.Controls.Add(aiInstall);

            UiButton aiBridge = new UiButton { Name = "aiBridgeButton", Text = "Launch AI bridge", Icon = UiIcon.Link };
            aiBridge.SetBounds(530, 36, 150, 36);
            aiBridge.Click += delegate {
                try {
                    string version = SelectedVersion();
                    if (version == null) throw new InvalidOperationException("Select a version first.");
                    var snapshot = cfg.Snapshot(); snapshot.VersionIsolation = chkIsolation.Checked;
                    string path = FileSafety.Under(AiIntegration.GameDirectory(snapshot, version), "config/suyuncraft-ai.json");
                    if (!File.Exists(path)) throw new FileNotFoundException("Install the AI mod first.");
                    string script = Path.Combine(AiIntegration.BundleDirectory(), "start-bridge.ps1");
                    Process.Start(new ProcessStartInfo("powershell.exe", CommandLine.Join(new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-ConfigPath", path })) { UseShellExecute = true });
                } catch (Exception ex) { MessageBox.Show(ex.Message, "AI bridge"); }
            };
            cardTools.Controls.Add(aiBridge);

            UiButton bSave = new UiButton(); bSave.Text = "SaveSettings"; bSave.Kind = UiButtonKind.Primary;
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
                lblJava.Text = string.IsNullOrEmpty(cfg.JavaPath) ? "Java：not configured in Settings" : "Java：" + cfg.JavaPath;
                MessageBox.Show("saved.", "Notice", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            cardTools.Controls.Add(bSave);

            lblSetInfo = MkLabel("Notice：Config file:  %APPDATA%\\SuyunCraft\\launcher_config.json", 0, 762, 760);
            lblSetInfo.ForeColor = Theme.TextDisabled;
            lblSetInfo.Font = Theme.Caption;
            setPage.Controls.Add(lblSetInfo);

            setPage.Resize += delegate { LayoutSettings(); };
            LayoutSettings();

            // Load current values
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
            // Content always scrolls vertically; reserve scrollbar width to avoid a horizontal one。
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
                statusBar.Text = "Fetching Java " + major + " Downloadinfo…";
                string architecture = Environment.Is64BitOperatingSystem ? "x64" : "x86";
                string metadataUrl = "https://api.adoptium.net/v3/assets/latest/" + major +
                    "/hotspot?architecture=" + architecture + "&image_type=jre&os=windows&vendor=eclipse";
                string metadata = await Task.Run(() => Net.DownloadString(metadataUrl, Mirrors.Official));
                object[] releases = Json.A(Json.Parse(metadata));
                if (releases == null || releases.Length == 0) throw new InvalidDataException("No JRE for this platform");
                object package = Json.O(Json.O(releases[0], "binary"), "package");
                string checksum = Json.S(package, "checksum", "");
                if (!FileSafety.IsHash(checksum, 64)) throw new InvalidDataException("JRE metadata missing  SHA256");
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
                        if (path == null || JavaUtil.GetMajor(path) != major) throw new InvalidDataException("JRE extracted version mismatch");
                        return path;
                    });
                    if (IsDisposed) return;
                    txtJava.Text = java; cfg.JavaPath = java; cfg.Save(); lblJava.Text = "Java：" + java;
                    statusBar.Text = "Java " + major + " InstallDone";
                    MessageBox.Show("Java Installed to ：" + java, "Done");
                }
                finally { if (File.Exists(archive)) File.Delete(archive); }
            }
            catch (OperationCanceledException) { if (!IsDisposed) statusBar.Text = "Java DownloadCancelled"; }
            catch (Exception ex) { if (!IsDisposed) { statusBar.Text = "Java Downloadfailed"; MessageBox.Show(ex.Message, "Java Downloadfailed"); } }
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
