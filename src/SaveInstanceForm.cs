using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SuyunCraft
{
    /// <summary>存档隔离管理：每个环境独立保存 mods / config / saves。</summary>
    sealed class SaveInstanceForm : Form
    {
        readonly Config configuration;
        readonly string version;
        readonly Func<bool> gameRunning;
        readonly UiListView list = new UiListView();
        readonly UiListHeader listHeader = new UiListHeader();
        readonly UiTextField nameField = new UiTextField();
        readonly UiCheckBox seed = new UiCheckBox();
        readonly UiLabel status = new UiLabel();
        readonly FlowLayoutPanel actions = new FlowLayoutPanel();
        UiCard nameCard;
        readonly UiButton cancel = new UiButton { Text = "取消复制", Enabled = false };
        readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        bool busy;
        bool scaled;
        public SaveInstance SelectedInstance { get; private set; }

        public SaveInstanceForm(Config cfg, string version, Func<bool> gameRunning)
        {
            configuration = cfg.Snapshot(); this.version = version; this.gameRunning = gameRunning;
            Name = "saveInstanceForm"; Text = "存档隔离管理"; Font = Theme.Ui;
            StartPosition = FormStartPosition.CenterParent; Size = new Size(900, 610); MinimumSize = new Size(800, 560);
            BackColor = Theme.Bg; ForeColor = Theme.Text; DoubleBuffered = true;
            // 缩放完全由 Ui.ScaleForm 负责，关闭 WinForms 自带的字体自适应，避免叠加放大。
            AutoScaleMode = AutoScaleMode.None;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(18, 14, 18, 14),
                BackColor = Theme.Bg
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            Controls.Add(layout);

            // ---------- 说明卡 ----------
            UiCard head = new UiCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
            layout.Controls.Add(head, 0, 0);
            Label headTitle = MkDialogLabel("存档隔离管理", 20, 12, 320);
            headTitle.Font = Theme.TitleBold;
            head.Controls.Add(headTitle);
            Label headSub = MkDialogLabel(
                "一个存档使用一个环境，分别保存 mods、config、saves 及资源包。切换前请退出游戏。\r\n新环境绑定当前版本：" + (version ?? "请先在主页选择版本"),
                20, 36, 760);
            headSub.ForeColor = Theme.Muted;
            headSub.Font = Theme.Caption;
            headSub.Height = 40;
            head.Controls.Add(headSub);

            // ---------- 名称输入 ----------
            nameCard = new UiCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
            layout.Controls.Add(nameCard, 0, 1);
            Label nameCap = MkDialogLabel("环境名称", 20, 14, 120);
            nameCap.ForeColor = Theme.Muted;
            nameCap.Font = Theme.Caption;
            nameCard.Controls.Add(nameCap);
            nameField.SetBounds(150, 10, 560, 32);
            nameField.Inner.Name = "instanceName";
            nameField.Inner.MaxLength = 60;
            nameField.SetPlaceholder("例如：主线存档");
            nameCard.Controls.Add(nameField);
            nameCard.Resize += delegate { LayoutNameRow(); };

            // ---------- 复制选项 ----------
            seed.Text = "复制当前环境的模组和配置（生成独立副本；其他存档不跟随）";
            seed.SetBounds(22, 4, 640, 24);
            layout.Controls.Add(seed, 0, 2);

            // ---------- 操作区 ----------
            actions.Dock = DockStyle.Fill;
            actions.FlowDirection = FlowDirection.LeftToRight;
            actions.WrapContents = false;
            actions.BackColor = Theme.Bg;
            actions.Margin = new Padding(0, 0, 0, 10);
            layout.Controls.Add(actions, 0, 3);
            ActionButton("新建空环境", UiIcon.Plus, () => CreateAsync(null));
            ActionButton("复制导入存档", UiIcon.Package, ImportAsync);
            ActionButton("使用所选环境", UiIcon.Check, () =>
            {
                RequireStopped();
                if (list.SelectedItems.Count == 0) return Task.FromResult(0);
                SelectedInstance = SaveIsolation.Load(configuration.GameDir, ((SaveInstance)list.SelectedItems[0].Tag).Id);
                busy = false; DialogResult = DialogResult.OK; return Task.FromResult(0);
            });
            ActionButton("打开环境目录", UiIcon.Folder, () =>
            {
                if (list.SelectedItems.Count > 0)
                {
                    var item = (SaveInstance)list.SelectedItems[0].Tag;
                    string path = SaveIsolation.GameDirectory(configuration.GameDir, item.Id);
                    if (!Directory.Exists(path)) throw new DirectoryNotFoundException("环境目录不存在。");
                    Process.Start("explorer.exe", CommandLine.Quote(path));
                }
                return Task.FromResult(0);
            });
            ActionButton("重命名", UiIcon.Refresh, () =>
            {
                if (list.SelectedItems.Count == 0) return Task.FromResult(0);
                var item = (SaveInstance)list.SelectedItems[0].Tag;
                if (item.Id.Length == 0 || item.Version == "?") throw new InvalidOperationException("损坏的环境不可重命名。");
                string current = nameField.Text.Trim();
                if (current.Length == 0) current = item.Name;
                using (var dialog = new TextInputForm("重命名存档环境", "新名称", current, 60))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return Task.FromResult(0);
                    SaveIsolation.Rename(configuration.GameDir, item.Id, dialog.Value);
                }
                return RefreshAsync();
            });
            ActionButton("导出备份", UiIcon.Package, ExportSelectedAsync);
            ActionButton("删除环境", UiIcon.Minus, DeleteSelectedAsync);

            // ---------- 环境列表 ----------
            UiCard listCard = new UiCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
            layout.Controls.Add(listCard, 0, 4);
            Label listTitle = MkDialogLabel("已有环境", 20, 10, 300);
            listTitle.Font = Theme.UiBold;
            listCard.Controls.Add(listTitle);

            list.Name = "instanceList"; list.Dock = DockStyle.Fill;
            list.MultiSelect = false; list.HideSelection = false;
            list.BackColor = Theme.Field; list.ForeColor = Theme.Text;
            list.Columns.Add("存档环境", 200);
            list.Columns.Add("绑定版本", 200);
            list.Columns.Add("环境 ID", 320);
            listHeader.Captions.AddRange(new string[] { "存档环境", "绑定版本", "环境 ID" });
            listHeader.Dock = DockStyle.Top;
            listHeader.Height = 28;
            Panel listArea = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(16, 34, 16, 14) };
            listArea.Controls.Add(list);
            listArea.Controls.Add(listHeader);
            listCard.Controls.Add(listArea);
            list.Resize += delegate { SyncColumns(); };

            // ---------- 底部状态 ----------
            status.Name = "instanceStatus"; status.Dock = DockStyle.Fill;
            status.AutoEllipsis = true;
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.ForeColor = Theme.Muted;
            cancel.Dock = DockStyle.Fill;
            cancel.Click += delegate { cancellation.Cancel(); };
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Bg };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            footer.Controls.Add(status, 0, 0);
            footer.Controls.Add(cancel, 1, 0);
            layout.Controls.Add(footer, 0, 5);

            Shown += async delegate { await RunAsync(RefreshAsync); };
            FormClosing += (s, e) => { if (busy) { cancellation.Cancel(); e.Cancel = true; status.Text = "正在取消复制，请稍候…"; } };
            FormClosed += delegate { cancellation.Dispose(); };

            Ui.ScaleForm(this);
            scaled = true;
            LayoutNameRow();
            WindowChrome.Apply(this);
            DarkMode.Apply(list, "DarkMode_ItemsView", "DarkMode_Explorer");
            DarkMode.ApplyScrollBars(list);
        }

        /// <summary>名称输入框随卡片宽度伸展，右侧留出统一内边距。</summary>
        void LayoutNameRow()
        {
            if (!scaled) return;
            int w = Math.Max(Ui.Px(220), nameCard.ClientSize.Width - Ui.Px(170));
            nameField.SetBounds(Ui.Px(150), Ui.Px(10), w, Ui.Px(32));
        }

        static Label MkDialogLabel(string text, int x, int y, int w)
        {
            UiLabel l = new UiLabel();
            l.Text = text; l.SetBounds(x, y, w, 22);
            l.ForeColor = Theme.Text;
            return l;
        }

        /// <summary>列宽铺满列表宽度，表头条与列表保持对齐。</summary>
        void SyncColumns()
        {
            int total = Math.Max(Ui.Px(240), list.ClientSize.Width);
            int w1 = Ui.Px(200), w2 = Ui.Px(200);
            int w3 = Math.Max(Ui.Px(140), total - w1 - w2);
            if (list.Columns.Count >= 3)
            {
                list.Columns[0].Width = w1;
                list.Columns[1].Width = w2;
                list.Columns[2].Width = w3;
            }
            listHeader.Widths.Clear();
            listHeader.Widths.Add(w1);
            listHeader.Widths.Add(w2);
            listHeader.Widths.Add(w3);
            listHeader.Width = total;
            listHeader.Invalidate();
        }

        void RequireStopped()
        {
            if (gameRunning()) throw new InvalidOperationException("请先退出游戏，再创建、导入或切换存档环境。");
        }

        void ActionButton(string text, UiIcon icon, Func<Task> action)
        {
            UiButton button = new UiButton { Name = text, Text = text, Icon = icon };
            // 构造函数里的尺寸用 96 DPI 设计单位，Ui.ScaleForm 会统一放大一次；
            // 此处若再乘 Ui.Px 会被二次放大。
            button.SetBounds(0, 0, 160, 34);
            button.Margin = new Padding(0, 0, 10, 0);
            button.Click += async delegate { await RunAsync(action); };
            actions.Controls.Add(button);
        }

        async Task RunAsync(Func<Task> action)
        {
            if (busy) return;
            busy = true; actions.Enabled = false; list.Enabled = false; nameField.Enabled = false; seed.Enabled = false;
            try { await action(); }
            catch (OperationCanceledException) { status.Text = "已取消；原存档保持不变。请重新打开窗口后重试。"; }
            catch (Exception ex) { if (!IsDisposed) { status.Text = "操作失败，未切换环境。"; MessageBox.Show(this, ex.Message, "存档隔离"); } }
            finally { busy = false; if (!IsDisposed) { actions.Enabled = true; list.Enabled = true; nameField.Enabled = true; seed.Enabled = true; cancel.Enabled = false; } }
        }

        async Task ImportAsync()
        {
            RequireStopped();
            using (var picker = new FolderBrowserDialog { Description = "选择包含 level.dat 的存档文件夹；请先退出使用此存档的游戏。将复制导入，保留原件。" })
            {
                if (version != null)
                {
                    try { string saves = FileSafety.Under(GameDirectories.Resolve(configuration, version), "saves"); if (Directory.Exists(saves)) picker.SelectedPath = saves; }
                    catch (IOException) { }
                }
                if (picker.ShowDialog(this) == DialogResult.OK) await CreateAsync(picker.SelectedPath);
            }
        }

        async Task CreateAsync(string world)
        {
            RequireStopped();
            if (version == null) throw new InvalidOperationException("请先在主页选择已安装版本。");
            string title = nameField.Text.Trim(); bool copy = seed.Checked;
            var progress = new Progress<string>(text => { if (!IsDisposed) status.Text = text; });
            status.Text = "正在创建独立环境…";
            cancel.Enabled = true;
            SelectedInstance = await Task.Run(() => SaveIsolation.Create(configuration, version, title, world, copy, cancellation.Token, progress));
            // Closing while RunAsync is busy is intentionally blocked; select after the copy completes.
            busy = false; DialogResult = DialogResult.OK;
        }

        async Task ExportSelectedAsync()
        {
            if (list.SelectedItems.Count == 0) return;
            var item = (SaveInstance)list.SelectedItems[0].Tag;
            if (item.Version == "?") throw new InvalidOperationException("损坏的环境不可导出。");
            RequireStopped();
            var progress = new Progress<string>(text => { if (!IsDisposed) status.Text = text; });
            status.Text = "正在导出环境…";
            cancel.Enabled = true;
            string zipPath = await Task.Run(() => SaveIsolation.Export(configuration, item.Id, cancellation.Token, progress));
            if (IsDisposed) return;
            status.Text = "已导出：" + zipPath;
            var open = MessageBox.Show(this, "导出完成：\r\n" + zipPath + "\r\n\r\n打开所在文件夹？", "导出备份", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (open == DialogResult.Yes) Process.Start("explorer.exe", "/select," + CommandLine.Quote(zipPath));
        }

        async Task DeleteSelectedAsync()
        {
            if (list.SelectedItems.Count == 0) return;
            var item = (SaveInstance)list.SelectedItems[0].Tag;
            if (item.Id.Length == 0) throw new InvalidOperationException("默认目录不能在这里删除。");
            RequireStopped();
            var confirm = MessageBox.Show(this,
                "将永久删除存档环境「" + item.Name + "」的全部内容（mods、config、saves）。\r\n此操作不可恢复，建议先「导出备份」。确定删除？",
                "删除存档环境", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;
            await Task.Run(() => SaveIsolation.Delete(configuration, item.Id, gameRunning));
            await RefreshAsync();
        }

        async Task RefreshAsync()
        {
            var instances = await Task.Run(() => SaveIsolation.List(configuration.GameDir));
            if (IsDisposed) return;
            list.Items.Clear();
            foreach (var instance in instances) list.Items.Add(new ListViewItem(new[] { instance.Name, instance.Version, instance.Id }) { Tag = instance });
            status.Text = "共 " + instances.Count + " 个环境。导入时请选择匹配的游戏版本与模组组合；每个环境只保留一个存档。";
        }
    }
}
