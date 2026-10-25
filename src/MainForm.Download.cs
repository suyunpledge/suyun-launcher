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
        // ---------------- Downloadpage ----------------
        UiCard cardDlToolbar, cardDlList, cardDlAction;
        Label lblDlCount;
        UiListHeader listHeader;

        void BuildDownload()
        {
            dlPage = new UiPage();
            dlPage.HeaderTitle = "Downloadgameversion";
            dlPage.HeaderSubtitle = "Fetch versions from the official manifest; optionally install the Fabric loader";

            // ---------- toolbar ----------
            cardDlToolbar = new UiCard();
            dlPage.Controls.Add(cardDlToolbar);

            cmbFilter = new UiComboBox();
            cmbFilter.SetBounds(20, 14, 120, 34);
            cmbFilter.Items.AddRange(new object[] { "Release", "Release+snapshot", "All" });
            cmbFilter.SelectedIndex = 0;
            cmbFilter.SelectedIndexChanged += delegate { ApplyFilter(); };
            cardDlToolbar.Controls.Add(cmbFilter);

            txtSearch = new UiTextField();
            txtSearch.SetBounds(152, 14, 220, 34);
            txtSearch.SetPlaceholder("Filter by version id");
            txtSearch.TextChanged += delegate { ApplyFilter(); };
            cardDlToolbar.Controls.Add(txtSearch);

            UiButton btnLoad = new UiButton();
            btnLoad.Text = "Fetch version list";
            btnLoad.Icon = UiIcon.Refresh;
            btnLoad.SetBounds(384, 13, 140, 36);
            btnLoad.Click += delegate { LoadManifest(); };
            cardDlToolbar.Controls.Add(btnLoad);

            // ---------- list ----------
            cardDlList = new UiCard();
            dlPage.Controls.Add(cardDlList);

            Label lList = MkLabel("versionmanifest", 20, 12, 220);
            lList.Font = Theme.UiBold;
            cardDlList.Controls.Add(lList);
            lblDlCount = MkLabel("Not loaded yet", 20, 32, 220);
            lblDlCount.ForeColor = Theme.TextDisabled;
            lblDlCount.Font = Theme.Caption;
            cardDlList.Controls.Add(lblDlCount);

            chkFabric = new UiCheckBox();
            chkFabric.Text = "Install Fabric";
            chkFabric.SetBounds(0, 16, 110, 24);
            ToolTip ttFabric = new ToolTip();
            ttFabric.SetToolTip(chkFabric, "When checked, install Fabric Loader for the selected version to load mods from the mods directorymod");
            chkFabric.CheckedChanged += delegate
            {
                cmbFabric.Visible = chkFabric.Checked;
                if (chkFabric.Checked && fabricLoaders == null) LoadFabricLoaders();
                LayoutDownload();
            };
            cardDlList.Controls.Add(chkFabric);

            cmbFabric = new UiComboBox();
            cmbFabric.SetBounds(0, 14, 150, 32);
            cmbFabric.Visible = false;
            cardDlList.Controls.Add(cmbFabric);

            lvVersions = new UiListView();
            lvVersions.SetBounds(16, 88, 700, 240);
            lvVersions.Columns.Add("version", Ui.Px(240));
            lvVersions.Columns.Add("type", Ui.Px(100));
            lvVersions.Columns.Add("Released", Ui.Px(170));
            lvVersions.Columns.Add("status", Ui.Px(120));
            lvVersions.DoubleClick += delegate { InstallSelected(); };
            cardDlList.Controls.Add(lvVersions);

            listHeader = new UiListHeader();
            listHeader.Captions.AddRange(new string[] { "version", "type", "Released", "status" });
            listHeader.SetBounds(16, 58, 700, 30);
            cardDlList.Controls.Add(listHeader);

            // ---------- action row ----------
            cardDlAction = new UiCard();
            dlPage.Controls.Add(cardDlAction);

            btnInstall = new UiButton();
            btnInstall.Text = "Downloadand install the selected version";
            btnInstall.Icon = UiIcon.Download;
            btnInstall.Kind = UiButtonKind.Success;
            btnInstall.SetBounds(20, 16, 230, 42);
            btnInstall.Click += delegate { InstallSelected(); };
            cardDlAction.Controls.Add(btnInstall);

            bar = new ThemeProgressBar();
            bar.SetBounds(266, 20, 380, 18);
            cardDlAction.Controls.Add(bar);

            lblDl = MkLabel("", 266, 44, 460);
            lblDl.ForeColor = Theme.Muted;
            lblDl.Font = Theme.Caption;
            cardDlAction.Controls.Add(lblDl);

            btnCancelDl = new UiButton();
            btnCancelDl.Text = "Cancel";
            btnCancelDl.SetBounds(700, 18, 80, 32);
            btnCancelDl.Enabled = false;
            btnCancelDl.Click += delegate { CancelOperation(); };
            cardDlAction.Controls.Add(btnCancelDl);

            dlTimer = new System.Windows.Forms.Timer();
            dlTimer.Interval = 300;
            dlTimer.Tick += DlTimerTick;

            dlPage.Resize += delegate { LayoutDownload(); };
            LayoutDownload();
        }

        void LayoutDownload()
        {
            if (suppressLayout) return;
            int w = dlPage.ClientSize.Width;
            int h = dlPage.ClientSize.Height;
            if (w <= 0 || h <= 0) return;

            cardDlToolbar.SetBounds(0, Ui.Px(56), w, Ui.Px(62));
            cardDlAction.SetBounds(0, h - Ui.Px(82), w, Ui.Px(68));

            int listTop = Ui.Px(130);
            int listH = Math.Max(Ui.Px(160), h - listTop - Ui.Px(92));
            cardDlList.SetBounds(0, listTop, w, listH);

            int lvX = Ui.Px(16);
            int lvW = Math.Max(Ui.Px(320), w - Ui.Px(32));
            int hdrH = Ui.Px(30);
            listHeader.SetBounds(lvX, Ui.Px(58), lvW, hdrH);
            lvVersions.SetBounds(lvX, Ui.Px(58) + hdrH, lvW, Math.Max(Ui.Px(80), listH - Ui.Px(74) - hdrH));

            // Stretch columns to full width to avoid a system-color gap
            int cVer = Ui.Px(240), cType = Ui.Px(110), cTime = Ui.Px(170);
            int cState = Math.Max(Ui.Px(90), lvW - cVer - cType - cTime);
            if (lvVersions.Columns.Count >= 4)
            {
                lvVersions.Columns[0].Width = cVer;
                lvVersions.Columns[1].Width = cType;
                lvVersions.Columns[2].Width = cTime;
                lvVersions.Columns[3].Width = cState;
            }
            listHeader.Widths.Clear();
            listHeader.Widths.Add(cVer);
            listHeader.Widths.Add(cType);
            listHeader.Widths.Add(cTime);
            listHeader.Widths.Add(cState);
            listHeader.Invalidate();

            chkFabric.Left = w - Ui.Px(150);
            cmbFabric.Left = w - Ui.Px(200);
            cmbFabric.Visible = chkFabric.Checked;
            if (chkFabric.Checked) chkFabric.Left = w - Ui.Px(320);

            btnInstall.SetBounds(Ui.Px(20), Ui.Px(13), Ui.Px(230), Ui.Px(42));
            bar.SetBounds(Ui.Px(266), Ui.Px(18), Math.Max(Ui.Px(120), w - Ui.Px(266) - Ui.Px(120)), Ui.Px(18));
            lblDl.SetBounds(Ui.Px(266), Ui.Px(42), Math.Max(Ui.Px(120), w - Ui.Px(280)), Ui.Px(20));
            btnCancelDl.Left = w - Ui.Px(104);
        }

        void DlTimerTick(object sender, EventArgs e)
        {
            if (mgr == null) return;
            long done = Math.Max(0, Interlocked.Read(ref mgr.DoneBytes) + Interlocked.Read(ref mgr.InflightBytes));
            double speed = 0;
            if (lastSample != default(DateTime))
            {
                double sec = (DateTime.Now - lastSample).TotalSeconds;
                if (sec > 0.2) speed = Math.Max(0, (done - lastBytes) / sec);
            }
            lastBytes = done; lastSample = DateTime.Now;
            if (mgr.TotalBytes > 0)
                bar.Value = (int)Math.Max(0, Math.Min(100, done * 100 / mgr.TotalBytes));
            string cur = mgr.CurrentFile;
            if (cur.Length > 50) cur = cur.Substring(cur.Length - 50);
            lblDl.Text = string.Format("{0} / {1} file  ·  {2:0.#} / {3:0.#} MB  ·  {4:0.#} MB/s  ·  {5}",
                mgr.DoneFiles, mgr.TotalFiles, done / 1024.0 / 1024.0, mgr.TotalBytes / 1024.0 / 1024.0, speed / 1024.0 / 1024.0, cur);
        }

        async void LoadManifest()
        {
            statusBar.Text = "Fetching version manifest… (may take 10-30s on slow networks)";
            var tick = new System.Windows.Forms.Timer();
            int secs = 0;
            tick.Interval = 1000;
            tick.Tick += delegate { secs++; statusBar.Text = "Fetching version manifest… " + secs + " s（racing multiple endpoints，please wait）"; };
            tick.Start();
            try
            {
                var list = await Task.Run(() => Net.GetVersions());
                if (IsDisposed) return;
                tick.Stop(); tick.Dispose();
                manifest = list; ApplyFilter();
                if (Net.LastFromCache)
                {
                    string when = "?";
                    try { when = System.IO.File.GetLastWriteTime(Net.ManifestCachePath).ToString("MM-dd HH:mm"); } catch { }
                    statusBar.Text = "Network unavailable，using local cache（" + list.Count + "  version，updated " + when + "）";
                }
                else statusBar.Text = "versionmanifest updated, total  " + list.Count + "  version";
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                {
                    tick.Stop(); tick.Dispose();
                    statusBar.Text = "Fetch version manifestfailed";
                    MessageBox.Show(ex.Message, "fetchfailed");
                }
            }
        }

        void ApplyFilter()
        {
            lvVersions.Items.Clear();
            string q = txtSearch.Text.Trim().ToLower();
            string f = cmbFilter.SelectedItem != null ? cmbFilter.SelectedItem.ToString() : "Release";
            foreach (ManifestVer v in manifest)
            {
                if (f == "Release" && v.Type != "release") continue;
                if (f == "Release+snapshot" && v.Type != "release" && v.Type != "snapshot") continue;
                if (q.Length > 0 && !v.Id.ToLower().Contains(q)) continue;
                ListViewItem it = new ListViewItem(v.Id);
                string typeStr = v.Type == "release" ? "Release" : v.Type == "snapshot" ? "snapshot" : v.Type;
                it.SubItems.Add(typeStr);
                string t = v.Time;
                if (t.Length >= 10) t = t.Substring(0, 10);
                it.SubItems.Add(t);
                bool installed = localInstalled.Contains(v.Id);
                it.SubItems.Add(installed ? "Installed" : "");
                if (installed) it.ForeColor = Theme.Accent;
                it.Tag = v;
                lvVersions.Items.Add(it);
            }
            if (lblDlCount != null)
                lblDlCount.Text = manifest.Count == 0
                    ? "Not loaded yet"
                    : "show " + lvVersions.Items.Count + " / " + manifest.Count + "  version";
        }

        async void LoadFabricLoaders()
        {
            try
            {
                string response = await Task.Run(() => Net.DownloadString("https://meta.fabricmc.net/v2/versions/loader", cfg.Mirror));
                if (IsDisposed) return;
                fabricLoaders = (Json.A(Json.Parse(response)) ?? new object[0]).Select(item => Json.S(item, "version", ""))
                    .Where(version => version.Length > 0).Take(10).ToList();
                cmbFabric.Items.Clear();
                foreach (string version in fabricLoaders) cmbFabric.Items.Add(version);
                if (cmbFabric.Items.Count > 0) cmbFabric.SelectedIndex = 0;
            }
            catch (Exception ex) {
                if (IsDisposed) return;
                chkFabric.Checked = false; cmbFabric.Visible = false;
                MessageBox.Show("Fabric Loader list fetchfailed：" + ex.Message);
            }
        }

        ManifestVer SelectedManifest()
        {
            if (lvVersions.SelectedItems.Count == 0) return null;
            return lvVersions.SelectedItems[0].Tag as ManifestVer;
        }

        async void InstallSelected()
        {
            if (operationBusy) return;
            ManifestVer selected = SelectedManifest();
            if (selected == null) { MessageBox.Show("Select a version first."); return; }
            if (selected.Type == "old_beta" || selected.Type == "old_alpha") { MessageBox.Show("Legacy Alpha/Beta versions are not supported."); return; }
            if (chkFabric.Checked && cmbFabric.SelectedItem == null) { MessageBox.Show("Wait for the Fabric loader list to finish loading."); return; }
            string loader = chkFabric.Checked ? cmbFabric.SelectedItem.ToString() : null;
            launchCancellation = new CancellationTokenSource();
            var cancellation = launchCancellation.Token;
            try
            {
                SetBusy(true);
                statusBar.Text = "Installing " + selected.Id;
                await Task.Run(() => VerifyStore.Load(), cancellation);
                await InstallOneVersionAsync(selected.Id, selected.Url, selected.Sha1, cancellation);
                string installed = selected.Id;
                if (loader != null)
                {
                    string profileUrl = "https://meta.fabricmc.net/v2/versions/loader/" +
                        Uri.EscapeDataString(selected.Id) + "/" + Uri.EscapeDataString(loader) + "/profile/json";
                    string profile = await Task.Run(() => Net.DownloadString(profileUrl, cfg.Mirror, cancellation), cancellation);
                    var json = Json.D(Json.Parse(profile));
                    installed = FileSafety.Id(Json.S(json, "id", ""));
                    if (Json.S(json, "inheritsFrom", "") != selected.Id) throw new InvalidDataException("Fabric Vanilla inheritance mismatch");
                    string path = FileSafety.VersionFile(cfg.GameDir, installed, ".json");
                    cancellation.ThrowIfCancellationRequested();
                    await Task.Run(() => FileSafety.WriteText(path, profile), cancellation);
                    await EnsureVersionFilesAsync(installed, cancellation);
                }
                cancellation.ThrowIfCancellationRequested();
                if (IsDisposed) return;
                cfg.LastVersion = installed; cfg.Save();
                ScanLocalVersions(); ApplyFilter(); SwitchPage("home");
                statusBar.Text = "InstallDone：" + installed;
                MessageBox.Show("InstallDone，selectversionready to launch.", "Done");
            }
            catch (OperationCanceledException) { if (!IsDisposed) statusBar.Text = "InstallCancelled"; }
            catch (Exception ex) { if (!IsDisposed) { statusBar.Text = "Installfailed"; MessageBox.Show(ex.Message, "Installfailed"); } }
            finally { FinishOperationAsync(); }
        }

        async Task InstallOneVersionAsync(string id, string url, string sha1, CancellationToken cancellation)
        {
            string destination = FileSafety.VersionFile(cfg.GameDir, id, ".json");
            if (url != null)
            {
                await RunDownloadsAsync(new List<DlItem> {
                    new DlItem { Url = url, Dest = destination, Sha1 = sha1, Desc = "versioninfo " + id }
                }, cancellation);
                var json = Json.D(Json.Parse(await Task.Run(() => File.ReadAllText(destination, Encoding.UTF8))));
                if (Json.S(json, "id", "") != id) throw new InvalidDataException("versionID does not match the official manifest");
            }
            await EnsureVersionFilesAsync(id, cancellation);
        }

        async Task EnsureVersionFilesAsync(string id, CancellationToken cancellation)
        {
            string root = cfg.GameDir;
            var download = CreateDownloadCallback();
            var metadata = await Task.Run(() => LaunchPlanner.LoadMerged(root, id), cancellation);
            await VersionFiles.EnsureAsync(root, id, metadata, download, cancellation);
        }

        Func<List<DlItem>, CancellationToken, Task> CreateDownloadCallback()
        {
            // The backend runs off the UI thread; marshal only the download progress adapter back.
            var scheduler = TaskScheduler.FromCurrentSynchronizationContext();
            return (items, token) => Task.Factory.StartNew(() => RunDownloadsAsync(items, token),
                token, TaskCreationOptions.None, scheduler).Unwrap();
        }

        async Task RunDownloadsAsync(List<DlItem> items, CancellationToken cancellation = default(CancellationToken))
        {
            if (IsDisposed) throw new OperationCanceledException();
            var manager = new DownloadManager { Mirror = cfg.Mirror };
            foreach (var item in items) manager.Queue.Enqueue(item);
            mgr = manager; lastBytes = 0; lastSample = default(DateTime); bar.Value = 0;
            bar.Pulse = true;
            btnCancelDl.Enabled = true;
            dlTimer.Start();
            try
            {
                await manager.RunAsync(cancellation);
                if (manager.Error != null) throw manager.Error;
                if (manager.Cancelled) throw new OperationCanceledException();
                if (!IsDisposed) bar.Value = 100;
            }
            finally
            {
                if (!IsDisposed) { dlTimer.Stop(); btnCancelDl.Enabled = operationBusy && launchCancellation != null && !launchCancellation.IsCancellationRequested; bar.Pulse = false; }
                if (mgr == manager) mgr = null;
            }
        }


    }
}
