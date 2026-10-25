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
    /// <summary>Save instance manager：Each instance saves mods / config / saves independently.</summary>
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
        readonly UiButton cancel = new UiButton { Text = "Cancelcopy", Enabled = false };
        readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        bool busy;
        bool scaled;
        public SaveInstance SelectedInstance { get; private set; }

        public SaveInstanceForm(Config cfg, string version, Func<bool> gameRunning)
        {
            configuration = cfg.Snapshot(); this.version = version; this.gameRunning = gameRunning;
            Name = "saveInstanceForm"; Text = "Save instance manager"; Font = Theme.Ui;
            StartPosition = FormStartPosition.CenterParent; Size = new Size(900, 610); MinimumSize = new Size(800, 560);
            BackColor = Theme.Bg; ForeColor = Theme.Text; DoubleBuffered = true;
            // Scaling is handled by Ui.ScaleForm; disable WinForms autoscaling to avoid double scaling。
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

            // ---------- Intro card ----------
            UiCard head = new UiCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
            layout.Controls.Add(head, 0, 0);
            Label headTitle = MkDialogLabel("Save instance manager", 20, 12, 320);
            headTitle.Font = Theme.TitleBold;
            head.Controls.Add(headTitle);
            Label headSub = MkDialogLabel(
                "One world per instance, each saving its own mods, config, saves and packs.\r\nExit the game before switching. New instances bind to: " + (version ?? "select a version on Home first"),
                20, 36, 760);
            headSub.ForeColor = Theme.Muted;
            headSub.Font = Theme.Caption;
            headSub.Height = 40;
            head.Controls.Add(headSub);

            // ---------- name input ----------
            nameCard = new UiCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
            layout.Controls.Add(nameCard, 0, 1);
            Label nameCap = MkDialogLabel("Instance name", 20, 14, 120);
            nameCap.ForeColor = Theme.Muted;
            nameCap.Font = Theme.Caption;
            nameCard.Controls.Add(nameCap);
            nameField.SetBounds(150, 10, 560, 32);
            nameField.Inner.Name = "instanceName";
            nameField.Inner.MaxLength = 60;
            nameField.SetPlaceholder("e.g. Main world");
            nameCard.Controls.Add(nameField);
            nameCard.Resize += delegate { LayoutNameRow(); };

            // ---------- copy option ----------
            seed.Text = "Copy mods and config from the current environment (independent copy)";
            seed.SetBounds(22, 4, 640, 24);
            layout.Controls.Add(seed, 0, 2);

            // ---------- action row ----------
            actions.Dock = DockStyle.Fill;
            actions.FlowDirection = FlowDirection.LeftToRight;
            actions.WrapContents = false;
            actions.BackColor = Theme.Bg;
            actions.Margin = new Padding(0, 0, 0, 10);
            layout.Controls.Add(actions, 0, 3);
            ActionButton("New empty instance", UiIcon.Plus, () => CreateAsync(null));
            ActionButton("Import a world", UiIcon.Package, ImportAsync);
            ActionButton("Use selected", UiIcon.Check, () =>
            {
                RequireStopped();
                if (list.SelectedItems.Count == 0) return Task.FromResult(0);
                SelectedInstance = SaveIsolation.Load(configuration.GameDir, ((SaveInstance)list.SelectedItems[0].Tag).Id);
                busy = false; DialogResult = DialogResult.OK; return Task.FromResult(0);
            });
            ActionButton("Open instancedirectory", UiIcon.Folder, () =>
            {
                if (list.SelectedItems.Count > 0)
                {
                    var item = (SaveInstance)list.SelectedItems[0].Tag;
                    string path = SaveIsolation.GameDirectory(configuration.GameDir, item.Id);
                    if (!Directory.Exists(path)) throw new DirectoryNotFoundException("Instance directory does not exist。");
                    Process.Start("explorer.exe", CommandLine.Quote(path));
                }
                return Task.FromResult(0);
            });
            ActionButton("Rename", UiIcon.Refresh, () =>
            {
                if (list.SelectedItems.Count == 0) return Task.FromResult(0);
                var item = (SaveInstance)list.SelectedItems[0].Tag;
                if (item.Id.Length == 0 || item.Version == "?") throw new InvalidOperationException("Corrupted instances cannot be Rename。");
                string current = nameField.Text.Trim();
                if (current.Length == 0) current = item.Name;
                using (var dialog = new TextInputForm("RenameSave instance", "New name", current, 60))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return Task.FromResult(0);
                    SaveIsolation.Rename(configuration.GameDir, item.Id, dialog.Value);
                }
                return RefreshAsync();
            });
            ActionButton("Export backup", UiIcon.Package, ExportSelectedAsync);
            ActionButton("Deleteinstance", UiIcon.Minus, DeleteSelectedAsync);

            // ---------- Instance list ----------
            UiCard listCard = new UiCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
            layout.Controls.Add(listCard, 0, 4);
            Label listTitle = MkDialogLabel("Existing instances", 20, 10, 300);
            listTitle.Font = Theme.UiBold;
            listCard.Controls.Add(listTitle);

            list.Name = "instanceList"; list.Dock = DockStyle.Fill;
            list.MultiSelect = false; list.HideSelection = false;
            list.BackColor = Theme.Field; list.ForeColor = Theme.Text;
            list.Columns.Add("Save instance", 200);
            list.Columns.Add("boundversion", 200);
            list.Columns.Add("Instance ID", 320);
            listHeader.Captions.AddRange(new string[] { "Save instance", "boundversion", "Instance ID" });
            listHeader.Dock = DockStyle.Top;
            listHeader.Height = 28;
            Panel listArea = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(16, 34, 16, 14) };
            listArea.Controls.Add(list);
            listArea.Controls.Add(listHeader);
            listCard.Controls.Add(listArea);
            list.Resize += delegate { SyncColumns(); };

            // ---------- Footer status ----------
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
            FormClosing += (s, e) => { if (busy) { cancellation.Cancel(); e.Cancel = true; status.Text = "Cancelling the copy，please wait…"; } };
            FormClosed += delegate { cancellation.Dispose(); };

            Ui.ScaleForm(this);
            scaled = true;
            LayoutNameRow();
            WindowChrome.Apply(this);
            DarkMode.Apply(list, "DarkMode_ItemsView", "DarkMode_Explorer");
            DarkMode.ApplyScrollBars(list);
        }

        /// <summary>The name field stretches with the card, keeping a uniform right margin。</summary>
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

        /// <summary>Stretch columns to full width, keeping the header aligned。</summary>
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
            if (gameRunning()) throw new InvalidOperationException("Exit the game before creating, importing or switchingSave instance。");
        }

        void ActionButton(string text, UiIcon icon, Func<Task> action)
        {
            UiButton button = new UiButton { Name = text, Text = text, Icon = icon };
            // Sizes in constructors use 96 DPI design units; Ui.ScaleForm scales once.
            // Multiplying by Ui.Px here would double-scale。
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
            catch (OperationCanceledException) { status.Text = "Cancelled; the original save is untouched. Reopen the dialog andretry。"; }
            catch (Exception ex) { if (!IsDisposed) { status.Text = "Operation failed; the instance was not switched。"; MessageBox.Show(this, ex.Message, "Save isolation"); } }
            finally { busy = false; if (!IsDisposed) { actions.Enabled = true; list.Enabled = true; nameField.Enabled = true; seed.Enabled = true; cancel.Enabled = false; } }
        }

        async Task ImportAsync()
        {
            RequireStopped();
            using (var picker = new FolderBrowserDialog { Description = "Pick a world folder with level.dat; exit the game using it. The world is copied, the original kept。" })
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
            if (version == null) throw new InvalidOperationException("Select a version on Home firstInstalledversion。");
            string title = nameField.Text.Trim(); bool copy = seed.Checked;
            var progress = new Progress<string>(text => { if (!IsDisposed) status.Text = text; });
            status.Text = "Creating an isolated instance…";
            cancel.Enabled = true;
            SelectedInstance = await Task.Run(() => SaveIsolation.Create(configuration, version, title, world, copy, cancellation.Token, progress));
            // Closing while RunAsync is busy is intentionally blocked; select after the copy completes.
            busy = false; DialogResult = DialogResult.OK;
        }

        async Task ExportSelectedAsync()
        {
            if (list.SelectedItems.Count == 0) return;
            var item = (SaveInstance)list.SelectedItems[0].Tag;
            if (item.Version == "?") throw new InvalidOperationException("Corrupted instances cannot be exported.");
            RequireStopped();
            var progress = new Progress<string>(text => { if (!IsDisposed) status.Text = text; });
            status.Text = "Exporting instance…";
            cancel.Enabled = true;
            string zipPath = await Task.Run(() => SaveIsolation.Export(configuration, item.Id, cancellation.Token, progress));
            if (IsDisposed) return;
            status.Text = "Exported: " + zipPath;
            var open = MessageBox.Show(this, "exportDone：\r\n" + zipPath + "\r\n\r\nOpen the containing folder？", "Export backup", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (open == DialogResult.Yes) Process.Start("explorer.exe", "/select," + CommandLine.Quote(zipPath));
        }

        async Task DeleteSelectedAsync()
        {
            if (list.SelectedItems.Count == 0) return;
            var item = (SaveInstance)list.SelectedItems[0].Tag;
            if (item.Id.Length == 0) throw new InvalidOperationException("The default directory cannot be deleted hereDelete。");
            RequireStopped();
            var confirm = MessageBox.Show(this,
                "This permanently deletes save instance \x0022" + item.Name + "\x0022 including mods, config and saves.\r\nThis cannot be undone; consider exporting a backup first. Delete?",
                "DeleteSave instance", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
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
            status.Text = "Total " + instances.Count + "  instances; match versions and mod sets when importing; one world per instance。";
        }
    }
}
