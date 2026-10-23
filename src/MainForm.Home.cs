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
        // ---------------- 主页 ----------------
        void BuildHome()
        {
            homePage = new Panel();
            homePage.Dock = DockStyle.Fill;
            homePage.BackColor = System.Drawing.Color.Transparent;

            // 顶部卡片：账号 + 版本
            Panel card = new Panel();
            card.SetBounds(0, 4, 560, 210);
            card.BackColor = Theme.Card;
            homePage.Controls.Add(card);

            Label l1 = MkLabel("玩家名（离线登录）", 20, 16, 200);
            card.Controls.Add(l1);
            txtUser = new TextBox(); txtUser.Name = "txtUser";
            txtUser.SetBounds(20, 42, 240, 30);
            Theme.StyleInput(txtUser);
            card.Controls.Add(txtUser);

            Label l2 = MkLabel("游戏版本", 20, 86, 200);
            card.Controls.Add(l2);
            cmbVersion = new ComboBox(); cmbVersion.Name = "cmbVersion";
            cmbVersion.SetBounds(20, 112, 240, 30);
            Theme.StyleCombo(cmbVersion);
            card.Controls.Add(cmbVersion);
            btnRefreshVer = new Button();
            btnRefreshVer.Text = "刷新";
            btnRefreshVer.SetBounds(270, 110, 70, 32);
            Theme.StyleButton(btnRefreshVer, null);
            btnRefreshVer.Click += delegate { ScanLocalVersions(); };
            card.Controls.Add(btnRefreshVer);

            Label l3 = MkLabel("运行内存", 20, 152, 200);
            card.Controls.Add(l3);
            trackMem = new TrackBar();
            trackMem.SetBounds(20, 176, 240, 34);
            trackMem.Minimum = 1; trackMem.Maximum = Math.Max(2, (int)TotalRamGb()); trackMem.TickFrequency = 1;
            trackMem.ValueChanged += delegate { lblMem.Text = trackMem.Value + " GB"; };
            card.Controls.Add(trackMem);

            chkAutoMem = new CheckBox();
            chkAutoMem.Text = "自动（推荐）";
            chkAutoMem.SetBounds(280, 46, 120, 24);
            chkAutoMem.ForeColor = Theme.Text;
            chkAutoMem.CheckedChanged += delegate
            {
                trackMem.Enabled = !chkAutoMem.Checked;
                if (chkAutoMem.Checked) AutoMemory();
            };
            card.Controls.Add(chkAutoMem);

            chkIsolation = new CheckBox();
            chkIsolation.Text = "版本隔离";
            chkIsolation.SetBounds(280, 152, 130, 24);
            chkIsolation.ForeColor = Theme.Text;
            ToolTip tt = new ToolTip();
            tt.SetToolTip(chkIsolation, "每个版本使用独立的 mods / config / 存档目录");
            card.Controls.Add(chkIsolation);

            lblMem = MkLabel("4 GB", 280, 176, 100);
            card.Controls.Add(lblMem);

            // 启动按钮
            btnLaunch = new Button(); btnLaunch.Name = "btnLaunch";
            btnLaunch.Text = "▶  启动游戏";
            btnLaunch.SetBounds(600, 30, 250, 84);
            Theme.StyleButton(btnLaunch, Theme.Accent);
            btnLaunch.ForeColor = System.Drawing.Color.White;
            btnLaunch.Font = Theme.LaunchBold;
            btnLaunch.Click += delegate { LaunchGame(); };
            homePage.Controls.Add(btnLaunch);

            lblJava = MkLabel("Java：检测中…", 600, 130, 280);
            lblJava.ForeColor = Theme.Muted;
            lblJava.AutoEllipsis = true;
            homePage.Controls.Add(lblJava);

            // 游戏日志
            Label lg = MkLabel("游戏运行日志（捕获自 stdout / stderr）", 0, 224, 500);
            homePage.Controls.Add(lg);
            Button btnClearLog = new Button();
            btnClearLog.Text = "清空";
            btnClearLog.SetBounds(770, 218, 70, 26);
            Theme.StyleButton(btnClearLog, null);
            btnClearLog.Click += delegate { txtGameLog.Clear(); };
            homePage.Controls.Add(btnClearLog);
            txtGameLog = new TextBox(); txtGameLog.Name = "txtGameLog";
            txtGameLog.Multiline = true; txtGameLog.ReadOnly = true; txtGameLog.ScrollBars = ScrollBars.Vertical;
            txtGameLog.SetBounds(0, 248, 850, 260);
            txtGameLog.BackColor = System.Drawing.Color.FromArgb(20, 21, 24);
            txtGameLog.ForeColor = System.Drawing.Color.FromArgb(190, 200, 170);
            txtGameLog.Font = Theme.Console;
            txtGameLog.BorderStyle = BorderStyle.FixedSingle;
            homePage.Controls.Add(txtGameLog);

            homePage.Resize += delegate
            {
                card.Width = Math.Max(410, homePage.Width - 270);
                btnLaunch.Left = homePage.Width - 250; btnLaunch.Width = 230;
                lblJava.Left = homePage.Width - 250; lblJava.Width = 230; lblJava.Height = 80;
                lblJava.AutoEllipsis = true;
                txtGameLog.Width = homePage.Width - 20;
                txtGameLog.Height = homePage.Height - 270;
                btnClearLog.Left = txtGameLog.Right - 80;
            };
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
