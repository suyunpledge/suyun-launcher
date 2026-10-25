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
        // ---------------- 下载页 ----------------
        UiCard cardDlToolbar, cardDlList, cardDlAction;
        Label lblDlCount;
        UiListHeader listHeader;

        void BuildDownload()
        {
            dlPage = new UiPage();
            dlPage.HeaderTitle = "下载游戏版本";
            dlPage.HeaderSubtitle = "从官方清单获取版本，可同时安装 Fabric 模组加载器";

            // ---------- 工具条 ----------
            cardDlToolbar = new UiCard();
            dlPage.Controls.Add(cardDlToolbar);

            cmbFilter = new UiComboBox();
            cmbFilter.SetBounds(20, 14, 120, 34);
            cmbFilter.Items.AddRange(new object[] { "正式版", "正式版+快照", "全部" });
            cmbFilter.SelectedIndex = 0;
            cmbFilter.SelectedIndexChanged += delegate { ApplyFilter(); };
            cardDlToolbar.Controls.Add(cmbFilter);

            txtSearch = new UiTextField();
            txtSearch.SetBounds(152, 14, 220, 34);
            txtSearch.SetPlaceholder("输入版本号过滤");
            txtSearch.TextChanged += delegate { ApplyFilter(); };
            cardDlToolbar.Controls.Add(txtSearch);

            UiButton btnLoad = new UiButton();
            btnLoad.Text = "获取版本列表";
            btnLoad.Icon = UiIcon.Refresh;
            btnLoad.SetBounds(384, 13, 140, 36);
            btnLoad.Click += delegate { LoadManifest(); };
            cardDlToolbar.Controls.Add(btnLoad);

            // ---------- 列表 ----------
            cardDlList = new UiCard();
            dlPage.Controls.Add(cardDlList);

            Label lList = MkLabel("版本清单", 20, 12, 220);
            lList.Font = Theme.UiBold;
            cardDlList.Controls.Add(lList);
            lblDlCount = MkLabel("尚未加载", 20, 32, 220);
            lblDlCount.ForeColor = Theme.TextDisabled;
            lblDlCount.Font = Theme.Caption;
            cardDlList.Controls.Add(lblDlCount);

            chkFabric = new UiCheckBox();
            chkFabric.Text = "安装 Fabric";
            chkFabric.SetBounds(0, 16, 110, 24);
            ToolTip ttFabric = new ToolTip();
            ttFabric.SetToolTip(chkFabric, "勾选后为所选版本安装 Fabric Loader，用于加载 mods 目录中的模组");
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
            lvVersions.Columns.Add("版本", Ui.Px(240));
            lvVersions.Columns.Add("类型", Ui.Px(100));
            lvVersions.Columns.Add("发布时间", Ui.Px(170));
            lvVersions.Columns.Add("状态", Ui.Px(120));
            lvVersions.DoubleClick += delegate { InstallSelected(); };
            cardDlList.Controls.Add(lvVersions);

            listHeader = new UiListHeader();
            listHeader.Captions.AddRange(new string[] { "版本", "类型", "发布时间", "状态" });
            listHeader.SetBounds(16, 58, 700, 30);
            cardDlList.Controls.Add(listHeader);

            // ---------- 操作区 ----------
            cardDlAction = new UiCard();
            dlPage.Controls.Add(cardDlAction);

            btnInstall = new UiButton();
            btnInstall.Text = "下载并安装所选版本";
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
            btnCancelDl.Text = "取消";
            btnCancelDl.SetBounds(700, 18, 80, 32);
            btnCancelDl.Enabled = false;
            btnCancelDl.Click += delegate { if (mgr != null) mgr.Cancelled = true; };
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

            // 列宽铺满列表，避免最后一列右侧留出系统色空白
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
            lblDl.Text = string.Format("{0} / {1} 文件  ·  {2:0.#} / {3:0.#} MB  ·  {4:0.#} MB/s  ·  {5}",
                mgr.DoneFiles, mgr.TotalFiles, done / 1024.0 / 1024.0, mgr.TotalBytes / 1024.0 / 1024.0, speed / 1024.0 / 1024.0, cur);
        }

        async void LoadManifest()
        {
            statusBar.Text = "正在获取版本清单…（慢网络约需 10-30 秒）";
            var tick = new System.Windows.Forms.Timer();
            int secs = 0;
            tick.Interval = 1000;
            tick.Tick += delegate { secs++; statusBar.Text = "正在获取版本清单… " + secs + " 秒（多通道并发竞速中，请稍候）"; };
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
                    statusBar.Text = "网络不可用，已用本地缓存（" + list.Count + " 个版本，更新于 " + when + "）";
                }
                else statusBar.Text = "版本清单已更新，共 " + list.Count + " 个版本";
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                {
                    tick.Stop(); tick.Dispose();
                    statusBar.Text = "获取版本清单失败";
                    MessageBox.Show(ex.Message, "获取失败");
                }
            }
        }

        void ApplyFilter()
        {
            lvVersions.Items.Clear();
            string q = txtSearch.Text.Trim().ToLower();
            string f = cmbFilter.SelectedItem != null ? cmbFilter.SelectedItem.ToString() : "正式版";
            foreach (ManifestVer v in manifest)
            {
                if (f == "正式版" && v.Type != "release") continue;
                if (f == "正式版+快照" && v.Type != "release" && v.Type != "snapshot") continue;
                if (q.Length > 0 && !v.Id.ToLower().Contains(q)) continue;
                ListViewItem it = new ListViewItem(v.Id);
                string typeStr = v.Type == "release" ? "正式版" : v.Type == "snapshot" ? "快照" : v.Type;
                it.SubItems.Add(typeStr);
                string t = v.Time;
                if (t.Length >= 10) t = t.Substring(0, 10);
                it.SubItems.Add(t);
                bool installed = localInstalled.Contains(v.Id);
                it.SubItems.Add(installed ? "已安装" : "");
                if (installed) it.ForeColor = Theme.Accent;
                it.Tag = v;
                lvVersions.Items.Add(it);
            }
            if (lblDlCount != null)
                lblDlCount.Text = manifest.Count == 0
                    ? "尚未加载"
                    : "显示 " + lvVersions.Items.Count + " / " + manifest.Count + " 个版本";
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
                MessageBox.Show("Fabric 加载器列表获取失败：" + ex.Message);
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
            if (selected == null) { MessageBox.Show("请先选择一个版本。"); return; }
            if (selected.Type == "old_beta" || selected.Type == "old_alpha") { MessageBox.Show("暂不支持远古 Alpha/Beta 版本。"); return; }
            if (chkFabric.Checked && cmbFabric.SelectedItem == null) { MessageBox.Show("请等待 Fabric 加载器列表加载完成。"); return; }
            string loader = chkFabric.Checked ? cmbFabric.SelectedItem.ToString() : null;
            try
            {
                SetBusy(true);
                VerifyStore.Load();
                statusBar.Text = "正在安装 " + selected.Id;
                await InstallOneVersionAsync(selected.Id, selected.Url, selected.Sha1);
                string installed = selected.Id;
                if (loader != null)
                {
                    string profileUrl = "https://meta.fabricmc.net/v2/versions/loader/" +
                        Uri.EscapeDataString(selected.Id) + "/" + Uri.EscapeDataString(loader) + "/profile/json";
                    string profile = await Task.Run(() => Net.DownloadString(profileUrl, cfg.Mirror));
                    var json = Json.D(Json.Parse(profile));
                    installed = FileSafety.Id(Json.S(json, "id", ""));
                    if (Json.S(json, "inheritsFrom", "") != selected.Id) throw new InvalidDataException("Fabric 原版继承不匹配");
                    string path = FileSafety.VersionFile(cfg.GameDir, installed, ".json");
                    await Task.Run(() => FileSafety.WriteText(path, profile));
                    await EnsureVersionFilesAsync(installed);
                }
                if (IsDisposed) return;
                cfg.LastVersion = installed; cfg.Save();
                ScanLocalVersions(); ApplyFilter(); SwitchPage("home");
                statusBar.Text = "安装完成：" + installed;
                MessageBox.Show("安装完成，选择版本即可启动。", "完成");
            }
            catch (OperationCanceledException) { if (!IsDisposed) statusBar.Text = "安装已取消"; }
            catch (Exception ex) { if (!IsDisposed) { statusBar.Text = "安装失败"; MessageBox.Show(ex.Message, "安装失败"); } }
            finally { try { VerifyStore.Save(); } catch { }
                SetBusy(false); }
        }

        async Task InstallOneVersionAsync(string id, string url, string sha1)
        {
            string destination = FileSafety.VersionFile(cfg.GameDir, id, ".json");
            if (url != null)
            {
                await RunDownloadsAsync(new List<DlItem> {
                    new DlItem { Url = url, Dest = destination, Sha1 = sha1, Desc = "版本信息 " + id }
                });
                var json = Json.D(Json.Parse(await Task.Run(() => File.ReadAllText(destination, Encoding.UTF8))));
                if (Json.S(json, "id", "") != id) throw new InvalidDataException("版本信息 ID 与官方清单不符");
            }
            await EnsureVersionFilesAsync(id);
        }

        async Task EnsureVersionFilesAsync(string id)
        {
            string root = cfg.GameDir;
            var download = CreateDownloadCallback();
            var metadata = await Task.Run(() => LaunchPlanner.LoadMerged(root, id));
            await VersionFiles.EnsureAsync(root, id, metadata, download, CancellationToken.None);
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
                if (!IsDisposed) { dlTimer.Stop(); btnCancelDl.Enabled = false; bar.Pulse = false; }
                if (mgr == manager) mgr = null;
            }
        }


    }
}
