using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SuyunCraft
{
    /// <summary>第三方模组管理对话框：导入、启停、诊断模组元数据。</summary>
    sealed class ModManagerForm : Form
    {
        readonly ModManagement manager;
        readonly Func<bool> gameRunning;
        readonly UiListView list = new UiListView();
        readonly UiListHeader listHeader = new UiListHeader();
        readonly TextBox diagnostics = new TextBox();
        readonly FlowLayoutPanel actions = new FlowLayoutPanel();
        bool busy;
        static string Display(string value) { return value.Length > 200 ? value.Substring(0, 200) + "…" : value; }

        public ModManagerForm(ModManagement manager, Func<bool> gameRunning)
        {
            this.manager = manager; this.gameRunning = gameRunning;
            Text = "第三方模组管理 · " + manager.MinecraftVersion + " · " + manager.Loader;
            Name = "modManagerForm"; Size = new Size(980, 680); MinimumSize = new Size(760, 500);
            StartPosition = FormStartPosition.CenterParent; BackColor = Theme.Bg; ForeColor = Theme.Text;
            Font = Theme.Ui; DoubleBuffered = true;
            // 缩放完全由 Ui.ScaleForm 负责，关闭 WinForms 自带的字体自适应，避免叠加放大。
            AutoScaleMode = AutoScaleMode.None;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 5,
                ColumnCount = 1,
                Padding = new Padding(18, 14, 18, 14),
                BackColor = Theme.Bg
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
            Controls.Add(layout);

            // ---------- 标题卡 ----------
            UiCard head = new UiCard();
            head.Dock = DockStyle.Fill;
            head.Margin = new Padding(0, 0, 0, 10);
            layout.Controls.Add(head, 0, 0);
            Label headTitle = MkDialogLabel("第三方模组管理", 20, 12, 420);
            headTitle.Font = Theme.TitleBold;
            head.Controls.Add(headTitle);
            Label headSub = MkDialogLabel(manager.MinecraftVersion + " · " + manager.Loader + "　当前实例：" + manager.GameDirectory, 20, 36, 700);
            headSub.ForeColor = Theme.Muted;
            headSub.Font = Theme.Caption;
            headSub.AutoEllipsis = true;
            head.Controls.Add(headSub);

            // ---------- 操作区 ----------
            actions.Dock = DockStyle.Fill;
            actions.FlowDirection = FlowDirection.LeftToRight;
            actions.WrapContents = false;
            actions.BackColor = Theme.Bg;
            actions.Margin = new Padding(0, 0, 0, 10);
            layout.Controls.Add(actions, 0, 1);
            AddAction("导入 .jar（多选）", UiIcon.Package, ImportAsync);
            AddAction("启用 / 停用", UiIcon.Check, ToggleAsync);
            AddAction("刷新与诊断", UiIcon.Refresh, RefreshAsync);
            AddAction("打开 mods 目录", UiIcon.Folder, () =>
            {
                System.IO.Directory.CreateDirectory(FileSafety.Under(manager.GameDirectory, "mods"));
                Process.Start("explorer.exe", CommandLine.Quote(manager.ModsDirectory)); return Task.FromResult(0);
            });

            // ---------- 模组列表 ----------
            UiCard listCard = new UiCard();
            listCard.Dock = DockStyle.Fill;
            listCard.Margin = new Padding(0, 0, 0, 10);
            layout.Controls.Add(listCard, 0, 2);
            Label listTitle = MkDialogLabel("已安装模组", 20, 10, 300);
            listTitle.Font = Theme.UiBold;
            listCard.Controls.Add(listTitle);

            list.Name = "modList"; list.Dock = DockStyle.Fill;
            list.MultiSelect = false; list.HideSelection = false;
            list.BackColor = Theme.Field; list.ForeColor = Theme.Text;
            foreach (var column in new[] { "状态", "模组 ID / 名称", "版本", "加载器标记", "文件名", "读取说明" }) list.Columns.Add(column, 120);
            list.DoubleClick += delegate { Task ignored = ToggleAsync(); };

            listHeader.Captions.AddRange(new string[] { "状态", "模组 ID / 名称", "版本", "加载器标记", "文件名", "读取说明" });
            listHeader.Dock = DockStyle.Top;
            listHeader.Height = 28;

            Panel listArea = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(16, 34, 16, 14) };
            listArea.Controls.Add(list);
            listArea.Controls.Add(listHeader);
            listCard.Controls.Add(listArea);
            list.Resize += delegate { SyncColumns(); };

            // ---------- 诊断区 ----------
            Label diagTitle = MkDialogLabel("兼容性诊断（只读元数据；Forge / NeoForge / Quilt 依赖由对应加载器验证）", 2, 4, 700);
            diagTitle.ForeColor = Theme.Muted;
            diagTitle.Font = Theme.Caption;
            layout.Controls.Add(diagTitle, 0, 3);

            diagnostics.Name = "modDiagnostics"; diagnostics.Dock = DockStyle.Fill;
            diagnostics.Multiline = true; diagnostics.ReadOnly = true;
            diagnostics.ScrollBars = ScrollBars.Vertical;
            diagnostics.BorderStyle = BorderStyle.None;
            diagnostics.BackColor = Theme.Field;
            diagnostics.ForeColor = Theme.LogText;
            diagnostics.Font = Theme.Console;
            layout.Controls.Add(diagnostics, 0, 4);

            Shown += async delegate { await RunAsync(RefreshAsync); };
            FormClosing += (s, e) => { if (busy) e.Cancel = true; };

            Ui.ScaleForm(this);
            WindowChrome.Apply(this);
            DarkMode.Apply(list, "DarkMode_ItemsView", "DarkMode_Explorer");
            DarkMode.ApplyScrollBars(list);
            // 诊断框是 EDIT 控件，滚动条属于非客户区，必须在句柄创建后再允许深色渲染。
            Action themeDiagnostics = delegate
            {
                DarkMode.Apply(diagnostics, "DarkMode_Explorer");
                DarkMode.AllowDark(diagnostics.Handle);
                DarkMode.ApplyScrollBars(diagnostics);
            };
            diagnostics.HandleCreated += delegate { themeDiagnostics(); };
            if (diagnostics.IsHandleCreated) themeDiagnostics();
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
            int[] fixedWidths = { Ui.Px(74), Ui.Px(190), Ui.Px(110), Ui.Px(130), Ui.Px(230) };
            int used = 0;
            for (int i = 0; i < fixedWidths.Length; i++) used += fixedWidths[i];
            int last = Math.Max(Ui.Px(120), total - used);
            listHeader.Widths.Clear();
            for (int i = 0; i < list.Columns.Count; i++)
            {
                int cw = i < fixedWidths.Length ? fixedWidths[i] : last;
                if (i == list.Columns.Count - 1) cw = Math.Max(Ui.Px(120), total - used);
                list.Columns[i].Width = cw;
                listHeader.Widths.Add(cw);
            }
            listHeader.Width = total;
            listHeader.Invalidate();
        }

        void AddAction(string title, UiIcon icon, Func<Task> action)
        {
            UiButton button = new UiButton { Text = title, Icon = icon };
            // 构造函数里的尺寸用 96 DPI 设计单位，Ui.ScaleForm 会统一放大一次；
            // 此处若再乘 Ui.Px 会被二次放大。
            button.SetBounds(0, 0, 168, 34);
            button.Margin = new Padding(0, 0, 10, 0);
            button.Click += async delegate { await RunAsync(action); };
            actions.Controls.Add(button);
        }

        async Task RunAsync(Func<Task> action)
        {
            if (busy) return;
            busy = true; actions.Enabled = false; list.Enabled = false;
            try { await action(); }
            catch (Exception ex) { if (!IsDisposed) MessageBox.Show(this, ex.Message, "模组管理"); }
            finally { busy = false; if (!IsDisposed) { actions.Enabled = true; list.Enabled = true; } }
        }

        void RequireStopped()
        {
            if (gameRunning()) throw new InvalidOperationException("请先退出由启动器启动的游戏，再修改模组文件。");
        }

        async Task ImportAsync()
        {
            RequireStopped();
            using (var picker = new OpenFileDialog { Filter = "Minecraft 模组 (*.jar)|*.jar", Multiselect = true, Title = "选择下载好的模组；须匹配当前 Minecraft 版本及加载器" })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                RequireStopped();
                int count = await Task.Run(() => manager.Import(picker.FileNames));
                await RefreshAsync();
                diagnostics.Text = "已导入 " + count + " 个模组。请检查下方诊断。\r\n" + diagnostics.Text;
            }
        }

        async Task ToggleAsync()
        {
            RequireStopped();
            if (list.SelectedItems.Count == 0) return;
            var item = (ModFileInfo)list.SelectedItems[0].Tag;
            await Task.Run(() => manager.SetEnabled(item.FileName, !item.Enabled));
            await RefreshAsync();
        }

        async Task RefreshAsync()
        {
            var mods = await Task.Run(() => manager.List());
            var issues = await Task.Run(() => manager.Diagnose());
            if (IsDisposed) return;
            list.BeginUpdate(); list.Items.Clear();
            foreach (var mod in mods)
            {
                string note = mod.Error.Length > 0 ? mod.Error : mod.Loaders.Count == 0 ? "元数据未识别，需加载器验证" : mod.Id.Length == 0 ? "仅识别加载器标记" : "";
                var row = new ListViewItem(new[] { mod.Enabled ? "启用" : "停用", Display(mod.Name), Display(mod.Version), string.Join(" / ", mod.Loaders), mod.FileName, Display(note) });
                row.Tag = mod; list.Items.Add(row);
            }
            list.EndUpdate();
            diagnostics.Text = "启用 " + mods.Count(m => m.Enabled) + " / 共 " + mods.Count + " 个。\r\n" +
                (issues.Count == 0 ? "未发现可识别的元数据问题；游戏内冲突仍需实际验证。" :
                string.Join("\r\n", issues.Take(200).Select(i => (i.BlocksLaunch ? "[阻止启动] " : "[提示] ") + i.Code + "：" + i.Message)) +
                (issues.Count > 200 ? "\r\n仅显示前 200 条诊断。" : ""));
        }
    }
}
