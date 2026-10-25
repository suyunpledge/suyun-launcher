using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SuyunCraft
{
    /// <summary>Third-party mod managerDialog: import, enable/disable, diagnose mod metadata。</summary>
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
            Text = "Third-party mod manager · " + manager.MinecraftVersion + " · " + manager.Loader;
            Name = "modManagerForm"; Size = new Size(980, 680); MinimumSize = new Size(760, 500);
            StartPosition = FormStartPosition.CenterParent; BackColor = Theme.Bg; ForeColor = Theme.Text;
            Font = Theme.Ui; DoubleBuffered = true;
            // Scaling is handled by Ui.ScaleForm; disable WinForms autoscaling to avoid double scaling。
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

            // ---------- Title card ----------
            UiCard head = new UiCard();
            head.Dock = DockStyle.Fill;
            head.Margin = new Padding(0, 0, 0, 10);
            layout.Controls.Add(head, 0, 0);
            Label headTitle = MkDialogLabel("Third-party mod manager", 20, 12, 420);
            headTitle.Font = Theme.TitleBold;
            head.Controls.Add(headTitle);
            Label headSub = MkDialogLabel(manager.MinecraftVersion + " · " + manager.Loader + "　current instance：" + manager.GameDirectory, 20, 36, 700);
            headSub.ForeColor = Theme.Muted;
            headSub.Font = Theme.Caption;
            headSub.AutoEllipsis = true;
            head.Controls.Add(headSub);

            // ---------- action row ----------
            actions.Dock = DockStyle.Fill;
            actions.FlowDirection = FlowDirection.LeftToRight;
            actions.WrapContents = false;
            actions.BackColor = Theme.Bg;
            actions.Margin = new Padding(0, 0, 0, 10);
            layout.Controls.Add(actions, 0, 1);
            AddAction("Import .jar (multi-select)", UiIcon.Package, ImportAsync);
            AddAction("Enable / disable", UiIcon.Check, ToggleAsync);
            AddAction("Refreshand diagnostics", UiIcon.Refresh, RefreshAsync);
            AddAction("Open the mods directory", UiIcon.Folder, () =>
            {
                System.IO.Directory.CreateDirectory(FileSafety.Under(manager.GameDirectory, "mods"));
                Process.Start("explorer.exe", CommandLine.Quote(manager.ModsDirectory)); return Task.FromResult(0);
            });

            // ---------- modlist ----------
            UiCard listCard = new UiCard();
            listCard.Dock = DockStyle.Fill;
            listCard.Margin = new Padding(0, 0, 0, 10);
            layout.Controls.Add(listCard, 0, 2);
            Label listTitle = MkDialogLabel("Installedmod", 20, 10, 300);
            listTitle.Font = Theme.UiBold;
            listCard.Controls.Add(listTitle);

            list.Name = "modList"; list.Dock = DockStyle.Fill;
            list.MultiSelect = false; list.HideSelection = false;
            list.BackColor = Theme.Field; list.ForeColor = Theme.Text;
            foreach (var column in new[] { "status", "mod ID / Name", "version", "Loader marker", "fileName", "Read the notes" }) list.Columns.Add(column, 120);
            list.DoubleClick += delegate { Task ignored = ToggleAsync(); };

            listHeader.Captions.AddRange(new string[] { "status", "mod ID / Name", "version", "Loader marker", "fileName", "Read the notes" });
            listHeader.Dock = DockStyle.Top;
            listHeader.Height = 28;

            Panel listArea = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(16, 34, 16, 14) };
            listArea.Controls.Add(list);
            listArea.Controls.Add(listHeader);
            listCard.Controls.Add(listArea);
            list.Resize += delegate { SyncColumns(); };

            // ---------- Diagnosticsarea ----------
            Label diagTitle = MkDialogLabel("Compatibility diagnostics (metadata only; Forge / NeoForge / Quilt deps verified by their loaders)）", 2, 4, 700);
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
            // Diagnosticsthe box is an EDIT control; its scrollbar is non-client and must be themed after handle creation。
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

        /// <summary>Stretch columns to full width, keeping the header aligned。</summary>
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
            // Sizes in constructors use 96 DPI design units; Ui.ScaleForm scales once.
            // Multiplying by Ui.Px here would double-scale。
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
            catch (Exception ex) { if (!IsDisposed) MessageBox.Show(this, ex.Message, "modmanager"); }
            finally { busy = false; if (!IsDisposed) { actions.Enabled = true; list.Enabled = true; } }
        }

        void RequireStopped()
        {
            if (gameRunning()) throw new InvalidOperationException("Exit the game launched by SuyunCraft before modifyingmodfile。");
        }

        async Task ImportAsync()
        {
            RequireStopped();
            using (var picker = new OpenFileDialog { Filter = "Minecraft mod (*.jar)|*.jar", Multiselect = true, Title = "Choose downloaded mods; they must match the current Minecraft version and loader" })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                RequireStopped();
                int count = await Task.Run(() => manager.Import(picker.FileNames));
                await RefreshAsync();
                diagnostics.Text = "Imported " + count + "  mods. Review the diagnostics below.\r\n" + diagnostics.Text;
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
                string note = mod.Error.Length > 0 ? mod.Error : mod.Loaders.Count == 0 ? "Metadata unrecognized; the loader will verify" : mod.Id.Length == 0 ? "Loader markers only" : "";
                var row = new ListViewItem(new[] { mod.Enabled ? "Enabled" : "Disabled", Display(mod.Name), Display(mod.Version), string.Join(" / ", mod.Loaders), mod.FileName, Display(note) });
                row.Tag = mod; list.Items.Add(row);
            }
            list.EndUpdate();
            diagnostics.Text = "Enabled " + mods.Count(m => m.Enabled) + " / Total " + mods.Count + "  。\r\n" +
                (issues.Count == 0 ? "No recognizable metadata issues; in-game conflicts still need real testing。" :
                string.Join("\r\n", issues.Take(200).Select(i => (i.BlocksLaunch ? "[blocks launch] " : "[Notice] ") + i.Code + "：" + i.Message)) +
                (issues.Count > 200 ? "\r\nShowing the first 200 rowsDiagnostics。" : ""));
        }
    }
}
