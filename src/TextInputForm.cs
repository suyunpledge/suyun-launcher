using System;
using System.Drawing;
using System.Windows.Forms;

namespace SuyunCraft
{
    /// <summary>单行文本输入对话框（重命名等轻量场景）。</summary>
    sealed class TextInputForm : Form
    {
        readonly UiTextField input = new UiTextField();
        public string Value { get { return input.Text; } }

        public TextInputForm(string title, string caption, string initial, int maxLength)
        {
            Text = title; Font = Theme.Ui;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(420, 150);
            BackColor = Theme.Bg; ForeColor = Theme.Text;

            var cap = new UiLabel { Text = caption, Dock = DockStyle.Top, Height = 28, Padding = new Padding(18, 12, 0, 0) };
            cap.ForeColor = Theme.Muted;
            Controls.Add(cap);

            input.SetBounds(18, 46, 384, 32);
            input.Inner.MaxLength = Math.Max(1, maxLength);
            input.Text = initial ?? "";
            input.Inner.SelectAll();
            Controls.Add(input);

            var ok = new UiButton { Text = "确定", Dock = DockStyle.None };
            ok.SetBounds(214, 96, 88, 34);
            ok.Click += delegate { DialogResult = DialogResult.OK; };
            Controls.Add(ok);

            var cancel = new UiButton { Text = "取消" };
            cancel.SetBounds(312, 96, 88, 34);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
            Controls.Add(cancel);

            AcceptButton = ok; CancelButton = cancel;
            Shown += delegate { input.Inner.Focus(); };
            Ui.ScaleForm(this);
            WindowChrome.Apply(this);
        }
    }
}
