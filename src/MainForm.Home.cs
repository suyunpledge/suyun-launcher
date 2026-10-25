using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SuyunCraft
{
    partial class MainForm
    {
        UiCard cardConfig, cardLaunch, cardSave, cardLog;
        Label lblMemBig, lblLogHint;
        UiButton btnClearLog;

        // ---------------- Home ----------------
        void BuildHome()
        {
            homePage = new UiPage();
            homePage.HeaderTitle = "Home";
            homePage.HeaderSubtitle = "Offline account, version and memory settings";

            // ---------- Game settings card ----------
            cardConfig = new UiCard();
            cardConfig.Shadow = true;
            homePage.Controls.Add(cardConfig);

            Label l1 = MkLabel("Player name (offline)", 20, 16, 220);
            l1.ForeColor = Theme.Muted;
            l1.Font = Theme.Caption;
            cardConfig.Controls.Add(l1);
            txtUser = new UiTextField(); txtUser.Name = "txtUser";
            txtUser.SetBounds(20, 38, 250, 34);
            cardConfig.Controls.Add(txtUser);

            Label l2 = MkLabel("gameversion", 20, 84, 220);
            l2.ForeColor = Theme.Muted;
            l2.Font = Theme.Caption;
            cardConfig.Controls.Add(l2);
            cmbVersion = new UiComboBox(); cmbVersion.Name = "cmbVersion";
            cmbVersion.SetBounds(20, 106, 250, 34);
            cardConfig.Controls.Add(cmbVersion);
            btnRefreshVer = new UiButton();
            btnRefreshVer.Text = "Refresh";
            btnRefreshVer.Icon = UiIcon.Refresh;
            btnRefreshVer.SetBounds(282, 105, 96, 36);
            Theme.StyleButton(btnRefreshVer, null);
            btnRefreshVer.Click += delegate { ScanLocalVersions(); };
            cardConfig.Controls.Add(btnRefreshVer);

            Label l3 = MkLabel("Memory", 20, 154, 220);
            l3.ForeColor = Theme.Muted;
            l3.Font = Theme.Caption;
            cardConfig.Controls.Add(l3);
            trackMem = new UiTrackBar();
            trackMem.SetBounds(20, 178, 250, 30);
            trackMem.Minimum = 1; trackMem.Maximum = Math.Max(2, (int)TotalRamGb()); trackMem.TickFrequency = 1;
            trackMem.ValueChanged += delegate
            {
                lblMem.Text = trackMem.Value + " GB";
                lblMemBig.Text = trackMem.Value + " GB";
            };
            cardConfig.Controls.Add(trackMem);

            lblMem = MkLabel("4 GB", 286, 178, 110);
            lblMem.Font = Theme.UiBold;
            lblMem.ForeColor = Theme.Accent;
            cardConfig.Controls.Add(lblMem);

            chkAutoMem = new UiCheckBox();
            chkAutoMem.Text = "Auto (recommended）";
            chkAutoMem.SetBounds(20, 216, 170, 24);
            chkAutoMem.CheckedChanged += delegate
            {
                trackMem.Enabled = !chkAutoMem.Checked;
                if (chkAutoMem.Checked) AutoMemory();
            };
            cardConfig.Controls.Add(chkAutoMem);

            chkIsolation = new UiCheckBox();
            chkIsolation.Text = "versionisolation";
            chkIsolation.SetBounds(206, 216, 120, 24);
            ToolTip tt = new ToolTip();
            tt.SetToolTip(chkIsolation, "Each version uses its own mods / config / savesdirectory");
            cardConfig.Controls.Add(chkIsolation);

            // ---------- Launchcard ----------
            cardLaunch = new UiCard();
            cardLaunch.Shadow = true;
            homePage.Controls.Add(cardLaunch);

            btnLaunch = new UiButton(); btnLaunch.Name = "btnLaunch";
            btnLaunch.Kind = UiButtonKind.Primary;
            btnLaunch.Icon = UiIcon.Play;
            btnLaunch.Text = "Launchgame";
            btnLaunch.Font = Theme.LaunchBold;
            btnLaunch.SetBounds(16, 16, 248, 64);
            btnLaunch.Click += delegate { if (operationBusy) CancelOperation(); else LaunchGame(); };
            cardLaunch.Controls.Add(btnLaunch);

            Label memCap = MkLabel("Allocated memory", 16, 92, 200);
            memCap.ForeColor = Theme.Muted;
            memCap.Font = Theme.Caption;
            cardLaunch.Controls.Add(memCap);

            lblMemBig = MkLabel("4 GB", 16, 110, 200);
            lblMemBig.Font = Theme.LaunchBold;
            lblMemBig.ForeColor = Theme.Accent;
            cardLaunch.Controls.Add(lblMemBig);

            Label javaCap = MkLabel("Java runtime", 16, 154, 200);
            javaCap.ForeColor = Theme.Muted;
            javaCap.Font = Theme.Caption;
            cardLaunch.Controls.Add(javaCap);

            lblJava = MkLabel("Java：detect…", 16, 174, 248);
            lblJava.ForeColor = Theme.Text;
            lblJava.Font = Theme.Caption;
            lblJava.AutoEllipsis = true;
            lblJava.Height = 46;
            cardLaunch.Controls.Add(lblJava);

            // ---------- Save instancecard ----------
            cardSave = new UiCard();
            homePage.Controls.Add(cardSave);

            Label saveCap = MkLabel("Save instance", 20, 12, 200);
            saveCap.ForeColor = Theme.Muted;
            saveCap.Font = Theme.Caption;
            cardSave.Controls.Add(saveCap);

            cmbSaveInstance = new UiComboBox { Name = "cmbSaveInstance", DropDownWidth = 650 };
            cmbSaveInstance.SetBounds(20, 34, 480, 34);
            cmbSaveInstance.SelectedIndexChanged += delegate { SaveInstanceChanged(); };
            cardSave.Controls.Add(cmbSaveInstance);

            btnSaveInstances = new UiButton { Name = "saveInstancesButton", Text = "Save instance manager", Icon = UiIcon.Grid };
            btnSaveInstances.SetBounds(520, 33, 160, 36);
            Theme.StyleButton(btnSaveInstances, null);
            btnSaveInstances.Click += delegate { ManageSaveInstances(); };
            cardSave.Controls.Add(btnSaveInstances);

            // ---------- Log card ----------
            cardLog = new UiCard();
            homePage.Controls.Add(cardLog);

            Label lg = MkLabel("Game log", 20, 12, 300);
            lg.Font = Theme.UiBold;
            cardLog.Controls.Add(lg);
            lblLogHint = MkLabel("captured from stdout / stderr", 20, 32, 300);
            lblLogHint.ForeColor = Theme.TextDisabled;
            lblLogHint.Font = Theme.Caption;
            cardLog.Controls.Add(lblLogHint);

            btnClearLog = new UiButton();
            btnClearLog.Text = "clear";
            btnClearLog.SetBounds(700, 12, 64, 30);            Theme.StyleButton(btnClearLog, null);
            btnClearLog.Click += delegate { txtGameLog.Clear(); };
            cardLog.Controls.Add(btnClearLog);

            txtGameLog = new UiLogView(); txtGameLog.Name = "txtGameLog";
            txtGameLog.SetBounds(16, 52, 850, 216);
            cardLog.Controls.Add(txtGameLog);

            homePage.Resize += delegate { LayoutHome(); };
            LayoutHome();
        }

        void LayoutHome()
        {
            if (suppressLayout) return;
            int w = homePage.ClientSize.Width;
            int h = homePage.ClientSize.Height;
            if (w <= 0 || h <= 0) return;

            int gap = Ui.Px(14);
            int top = Ui.Px(58);
            int rowH = Ui.Px(250);
            int saveH = Ui.Px(74);

            // LaunchCards default to 288 wide; shrink only when the window is too narrow
            int launchW = Ui.Px(288);
            int minConfig = Ui.Px(360);
            if (w - launchW - gap < minConfig)
                launchW = Math.Max(Ui.Px(200), w - gap - minConfig);
            int configW = Math.Max(Ui.Px(300), w - launchW - gap);

            cardConfig.SetBounds(0, top, configW, rowH);
            cardLaunch.SetBounds(w - launchW, top, launchW, rowH);

            int innerW = Math.Max(Ui.Px(90), launchW - Ui.Px(32));
            btnLaunch.SetBounds(Ui.Px(16), Ui.Px(16), innerW, Ui.Px(64));
            lblMemBig.Width = innerW;
            lblJava.Width = innerW;

            int saveTop = top + rowH + gap;
            cardSave.SetBounds(0, saveTop, w, saveH);
            int saveBtnW = Ui.Px(150);
            int saveBtnX = Math.Max(Ui.Px(120), w - Ui.Px(20) - saveBtnW);
            btnSaveInstances.SetBounds(saveBtnX, Ui.Px(33), saveBtnW, Ui.Px(36));
            cmbSaveInstance.SetBounds(Ui.Px(20), Ui.Px(34),
                Math.Max(Ui.Px(160), saveBtnX - Ui.Px(16) - Ui.Px(20)), Ui.Px(34));

            int logTop = saveTop + saveH + gap;
            cardLog.SetBounds(0, logTop, w, Math.Max(Ui.Px(48), h - logTop));
            btnClearLog.Left = Math.Max(Ui.Px(80), w - Ui.Px(80));
            txtGameLog.SetBounds(Ui.Px(16), Ui.Px(52), Math.Max(Ui.Px(80), w - Ui.Px(32)),
                Math.Max(Ui.Px(40), cardLog.Height - Ui.Px(66)));
        }

        static long TotalRamGb()
        {
            return (long)(new Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory / (1024L * 1024L * 1024L));
        }

        void AutoMemory()
        {
            try
            {
                long gb = TotalRamGb();
                int rec = (int)Math.Max(2, Math.Min(8, gb / 2));
                trackMem.Value = Math.Min(trackMem.Maximum, rec);
            }
            catch { }
        }

    }
}
