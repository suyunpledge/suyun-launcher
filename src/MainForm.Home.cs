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

        // ---------------- 主页 ----------------
        void BuildHome()
        {
            homePage = new UiPage();
            homePage.HeaderTitle = "主页";
            homePage.HeaderSubtitle = "离线账户、版本与内存配置";

            // ---------- 游戏配置卡片 ----------
            cardConfig = new UiCard();
            cardConfig.Shadow = true;
            homePage.Controls.Add(cardConfig);

            Label l1 = MkLabel("玩家名（离线登录）", 20, 16, 220);
            l1.ForeColor = Theme.Muted;
            l1.Font = Theme.Caption;
            cardConfig.Controls.Add(l1);
            txtUser = new UiTextField(); txtUser.Name = "txtUser";
            txtUser.SetBounds(20, 38, 250, 34);
            cardConfig.Controls.Add(txtUser);

            Label l2 = MkLabel("游戏版本", 20, 84, 220);
            l2.ForeColor = Theme.Muted;
            l2.Font = Theme.Caption;
            cardConfig.Controls.Add(l2);
            cmbVersion = new UiComboBox(); cmbVersion.Name = "cmbVersion";
            cmbVersion.SetBounds(20, 106, 250, 34);
            cardConfig.Controls.Add(cmbVersion);
            btnRefreshVer = new UiButton();
            btnRefreshVer.Text = "刷新";
            btnRefreshVer.Icon = UiIcon.Refresh;
            btnRefreshVer.SetBounds(282, 105, 96, 36);
            Theme.StyleButton(btnRefreshVer, null);
            btnRefreshVer.Click += delegate { ScanLocalVersions(); };
            cardConfig.Controls.Add(btnRefreshVer);

            Label l3 = MkLabel("运行内存", 20, 154, 220);
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
            chkAutoMem.Text = "自动分配（推荐）";
            chkAutoMem.SetBounds(20, 216, 170, 24);
            chkAutoMem.CheckedChanged += delegate
            {
                trackMem.Enabled = !chkAutoMem.Checked;
                if (chkAutoMem.Checked) AutoMemory();
            };
            cardConfig.Controls.Add(chkAutoMem);

            chkIsolation = new UiCheckBox();
            chkIsolation.Text = "版本隔离";
            chkIsolation.SetBounds(206, 216, 120, 24);
            ToolTip tt = new ToolTip();
            tt.SetToolTip(chkIsolation, "每个版本使用独立的 mods / config / 存档目录");
            cardConfig.Controls.Add(chkIsolation);

            // ---------- 启动卡片 ----------
            cardLaunch = new UiCard();
            cardLaunch.Shadow = true;
            homePage.Controls.Add(cardLaunch);

            btnLaunch = new UiButton(); btnLaunch.Name = "btnLaunch";
            btnLaunch.Kind = UiButtonKind.Primary;
            btnLaunch.Icon = UiIcon.Play;
            btnLaunch.Text = "启动游戏";
            btnLaunch.Font = Theme.LaunchBold;
            btnLaunch.SetBounds(16, 16, 248, 64);
            btnLaunch.Click += delegate { LaunchGame(); };
            cardLaunch.Controls.Add(btnLaunch);

            Label memCap = MkLabel("已分配内存", 16, 92, 200);
            memCap.ForeColor = Theme.Muted;
            memCap.Font = Theme.Caption;
            cardLaunch.Controls.Add(memCap);

            lblMemBig = MkLabel("4 GB", 16, 110, 200);
            lblMemBig.Font = Theme.LaunchBold;
            lblMemBig.ForeColor = Theme.Accent;
            cardLaunch.Controls.Add(lblMemBig);

            Label javaCap = MkLabel("Java 运行时", 16, 154, 200);
            javaCap.ForeColor = Theme.Muted;
            javaCap.Font = Theme.Caption;
            cardLaunch.Controls.Add(javaCap);

            lblJava = MkLabel("Java：检测中…", 16, 174, 248);
            lblJava.ForeColor = Theme.Text;
            lblJava.Font = Theme.Caption;
            lblJava.AutoEllipsis = true;
            lblJava.Height = 46;
            cardLaunch.Controls.Add(lblJava);

            // ---------- 存档环境卡片 ----------
            cardSave = new UiCard();
            homePage.Controls.Add(cardSave);

            Label saveCap = MkLabel("存档环境", 20, 12, 200);
            saveCap.ForeColor = Theme.Muted;
            saveCap.Font = Theme.Caption;
            cardSave.Controls.Add(saveCap);

            cmbSaveInstance = new UiComboBox { Name = "cmbSaveInstance", DropDownWidth = 650 };
            cmbSaveInstance.SetBounds(20, 34, 480, 34);
            cmbSaveInstance.SelectedIndexChanged += delegate { SaveInstanceChanged(); };
            cardSave.Controls.Add(cmbSaveInstance);

            btnSaveInstances = new UiButton { Name = "saveInstancesButton", Text = "存档隔离管理", Icon = UiIcon.Grid };
            btnSaveInstances.SetBounds(520, 33, 160, 36);
            Theme.StyleButton(btnSaveInstances, null);
            btnSaveInstances.Click += delegate { ManageSaveInstances(); };
            cardSave.Controls.Add(btnSaveInstances);

            // ---------- 日志卡片 ----------
            cardLog = new UiCard();
            homePage.Controls.Add(cardLog);

            Label lg = MkLabel("游戏运行日志", 20, 12, 300);
            lg.Font = Theme.UiBold;
            cardLog.Controls.Add(lg);
            lblLogHint = MkLabel("捕获自 stdout / stderr", 20, 32, 300);
            lblLogHint.ForeColor = Theme.TextDisabled;
            lblLogHint.Font = Theme.Caption;
            cardLog.Controls.Add(lblLogHint);

            btnClearLog = new UiButton();
            btnClearLog.Text = "清空";
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

            int launchW = Ui.Px(280);
            int configW = Math.Max(Ui.Px(360), w - launchW - Ui.Px(20));

            cardConfig.SetBounds(0, Ui.Px(62), configW, Ui.Px(250));
            cardLaunch.SetBounds(w - launchW, Ui.Px(62), launchW, Ui.Px(250));

            cardSave.SetBounds(0, Ui.Px(324), w, Ui.Px(76));
            cmbSaveInstance.Width = Math.Max(Ui.Px(220), w - Ui.Px(220));
            btnSaveInstances.Left = w - Ui.Px(180);

            int logTop = Ui.Px(412);
            cardLog.SetBounds(0, logTop, w, Math.Max(Ui.Px(140), h - logTop));
            btnClearLog.Left = w - Ui.Px(84);
            txtGameLog.SetBounds(Ui.Px(16), Ui.Px(52), w - Ui.Px(32), Math.Max(Ui.Px(80), cardLog.Height - Ui.Px(68)));
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
