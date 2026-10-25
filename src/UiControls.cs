using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;

namespace SuyunCraft
{
    /// <summary>按钮视觉类型。</summary>
    public enum UiButtonKind
    {
        Primary,
        Secondary,
        Ghost,
        Danger,
        Success
    }

    /// <summary>轻量补间动画，用于悬停和状态过渡。</summary>
    sealed class UiTween
    {
        readonly System.Windows.Forms.Timer timer;
        float current;
        float target;
        Action<float> apply;

        public UiTween()
        {
            timer = new System.Windows.Forms.Timer();
            timer.Interval = 16;
            timer.Tick += Tick;
        }

        public float Value { get { return current; } }

        public void To(float value, Action<float> onChange)
        {
            apply = onChange;
            target = value;
            if (!timer.Enabled) timer.Start();
        }

        public void Dispose()
        {
            timer.Stop();
            timer.Dispose();
        }

        void Tick(object sender, EventArgs e)
        {
            float step = 0.22f;
            if (current < target) current = Math.Min(target, current + step);
            else if (current > target) current = Math.Max(target, current - step);
            else
            {
                timer.Stop();
                return;
            }
            if (apply != null) apply(current);
        }
    }

    /// <summary>统一的深色按钮：圆角、渐变、悬停反馈与矢量图标。</summary>
    class UiButton : Button
    {
        UiTween hover = new UiTween();
        UiTween press = new UiTween();

        public UiButtonKind Kind = UiButtonKind.Secondary;
        public UiIcon Icon = UiIcon.None;
        public int Radius = 8;

        public UiButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            Font = Theme.UiBold;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.StandardDoubleClick, false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { hover.Dispose(); press.Dispose(); }
            base.Dispose(disposing);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover.To(1f, delegate(float v) { Invalidate(); });
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover.To(0f, delegate(float v) { Invalidate(); });
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs me)
        {
            press.To(1f, delegate(float v) { Invalidate(); });
            base.OnMouseDown(me);
        }

        protected override void OnMouseUp(MouseEventArgs me)
        {
            press.To(0f, delegate(float v) { Invalidate(); });
            base.OnMouseUp(me);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            UiKit.ClearBackground(g, this);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Rectangle r = new Rectangle(0, 0, Width, Height);
            float hv = hover.Value;
            float pv = press.Value;

            Color fill;
            Color stroke;
            Color glowColor = Theme.Accent;
            bool solid = true;

            if (!Enabled)
            {
                fill = Theme.Card;
                stroke = Theme.Border;
                solid = false;
            }
            else if (Kind == UiButtonKind.Primary)
            {
                fill = UiKit.Mix(Theme.Accent, Theme.AccentLight, hv);
                stroke = UiKit.Mix(Theme.AccentDark, Theme.Accent, hv);
                glowColor = Theme.Accent;
            }
            else if (Kind == UiButtonKind.Success)
            {
                fill = UiKit.Mix(Theme.Blue, Theme.BlueLight, hv);
                stroke = UiKit.Mix(Theme.BlueDark, Theme.Blue, hv);
                glowColor = Theme.Blue;
            }
            else if (Kind == UiButtonKind.Danger)
            {
                fill = UiKit.Mix(Theme.Danger, Theme.DangerLight, hv);
                stroke = UiKit.Mix(Theme.DangerDark, Theme.Danger, hv);
                glowColor = Theme.Danger;
            }
            else if (Kind == UiButtonKind.Ghost)
            {
                fill = Parent != null ? Parent.BackColor : Theme.Bg;
                stroke = UiKit.Alpha(fill, 0);
                solid = false;
            }
            else
            {
                fill = UiKit.Mix(Theme.Card, Theme.CardHover, hv);
                stroke = UiKit.Mix(Theme.Border, Theme.BorderStrong, hv);
                glowColor = Theme.CardHover;
            }

            if (solid && Enabled && (Kind == UiButtonKind.Primary || Kind == UiButtonKind.Success || Kind == UiButtonKind.Danger))
            {
                UiKit.Glow(g, r, Ui.Px(Radius), UiKit.Alpha(glowColor, 200), Ui.Px(8 + (int)(hv * 8)));
            }

            if (Kind == UiButtonKind.Ghost)
            {
                if (hv > 0.01f)
                    UiKit.FillRound(g, r, Ui.Px(Radius), UiKit.Alpha(Theme.Text, (int)(16 * hv)));
            }
            else
            {
                Color top = UiKit.Mix(fill, System.Drawing.Color.White, pv * 0.14);
                Color bottom = UiKit.Mix(fill, System.Drawing.Color.Black, 0.10 + pv * 0.08);
                UiKit.FillRoundGradient(g, r, Ui.Px(Radius), top, bottom, true);
                UiKit.StrokeRound(g, r, Ui.Px(Radius), UiKit.Alpha(stroke, Enabled ? 255 : 120), 1f);
            }

            if (Focused && Enabled)
                UiKit.StrokeRound(g, Rectangle.Inflate(r, -2, -2), Ui.Px(Radius - 1), UiKit.Alpha(Theme.FocusRing, 200), 1f);

            Color fore = Enabled ? ForeColor : Theme.Muted;
            if (Kind == UiButtonKind.Primary || Kind == UiButtonKind.Success || Kind == UiButtonKind.Danger)
                fore = System.Drawing.Color.White;

            int iconSize = Math.Min(Ui.Px(18), Height - Ui.Px(10));
            bool hasIcon = Icon != UiIcon.None && iconSize >= Ui.Px(10);
            Size textSize = TextRenderer.MeasureText(Text, Font, new Size(Width, Height),
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            int gap = hasIcon ? Ui.Px(8) : 0;
            int total = (hasIcon ? iconSize : 0) + gap + textSize.Width;
            int startX = Math.Max(0, (Width - total) / 2);
            int cy = (Height - iconSize) / 2;

            if (hasIcon)
            {
                UiKit.DrawIcon(g, Icon, new Rectangle(startX, cy, iconSize, iconSize), fore, 1.8f);
            }
            if (Text.Length > 0)
            {
                Rectangle textRect = new Rectangle(startX + (hasIcon ? iconSize + gap : 0), 0,
                    Math.Max(0, Width - startX - (hasIcon ? iconSize + gap : 0)), Height);
                TextRenderer.DrawText(g, Text, Font, textRect, fore,
                    TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.Left
                    | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            }
        }
    }

    /// <summary>圆角输入框：外层自绘边框与聚焦光晕，内嵌无边框文本框。</summary>
    class UiTextField : Panel
    {
        readonly TextBox inner;
        bool focused;

        public UiTextField()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Theme.Field;
            inner = new TextBox();
            inner.BorderStyle = BorderStyle.None;
            inner.BackColor = Theme.Field;
            inner.ForeColor = Theme.Text;
            inner.Font = Theme.Input;
            inner.Name = "input";
            inner.TextChanged += delegate { OnTextChanged(EventArgs.Empty); };
            inner.GotFocus += delegate { focused = true; Invalidate(); };
            inner.LostFocus += delegate { focused = false; Invalidate(); };
            inner.HandleCreated += delegate { DarkMode.Apply(inner, "DarkMode_Explorer"); };
            Controls.Add(inner);
        }

        public new event EventHandler TextChanged;

        public TextBox Inner { get { return inner; } }

        public override string Text
        {
            get { return inner.Text; }
            set { inner.Text = value; }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            EventHandler handler = TextChanged;
            if (handler != null) handler(this, e);
        }

        public void SetPlaceholder(string text)
        {
            inner.HandleCreated += delegate { CueBanner.Set(inner, text); };
            if (inner.IsHandleCreated) CueBanner.Set(inner, text);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            inner.Enabled = Enabled;
            inner.BackColor = Enabled ? Theme.Field : Theme.FieldDisabled;
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            inner.Focus();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            int pad = Ui.Px(10);
            inner.SetBounds(pad, Ui.Px(6), Math.Max(10, Width - pad * 2), Math.Max(10, Height - Ui.Px(12)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            UiKit.ClearBackground(g, this);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width, Height);
            UiKit.FillRound(g, r, Ui.Px(8), Enabled ? Theme.Field : Theme.FieldDisabled);
            if (focused && Enabled)
            {
                UiKit.Glow(g, r, Ui.Px(8), UiKit.Alpha(Theme.Accent, 150), Ui.Px(6));
                UiKit.StrokeRound(g, r, Ui.Px(8), Theme.Accent, 1.4f);
            }
            else
            {
                UiKit.StrokeRound(g, r, Ui.Px(8), Theme.Border, 1f);
            }
        }
    }

    /// <summary>自绘下拉框：圆角外观与矢量箭头，展开列表仍由系统深色主题渲染。</summary>
    class UiComboBox : ComboBox
    {
        bool focused;

        public UiComboBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            BackColor = Theme.Field;
            ForeColor = Theme.Text;
            Font = Theme.Input;
            DrawMode = DrawMode.Normal;
            HandleCreated += delegate { DarkMode.Apply(this, "DarkMode_CFD"); DarkMode.Apply(this, "DarkMode_Explorer"); };
            GotFocus += delegate { focused = true; Invalidate(); };
            LostFocus += delegate { focused = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            UiKit.ClearBackground(g, this);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width, Height);
            UiKit.FillRound(g, r, Ui.Px(8), Enabled ? Theme.Field : Theme.FieldDisabled);
            if (focused && Enabled)
            {
                UiKit.Glow(g, r, Ui.Px(8), UiKit.Alpha(Theme.Accent, 140), Ui.Px(6));
                UiKit.StrokeRound(g, r, Ui.Px(8), Theme.Accent, 1.4f);
            }
            else UiKit.StrokeRound(g, r, Ui.Px(8), Theme.Border, 1f);

            string text = Text;
            Rectangle textRect = new Rectangle(Ui.Px(10), 0, Math.Max(10, Width - Ui.Px(34)), Height);
            TextRenderer.DrawText(g, text, Font, textRect, Enabled ? Theme.Text : Theme.Muted,
                TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.Left
                | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            int arrow = Ui.Px(16);
            UiKit.DrawIcon(g, UiIcon.ChevronDown, new Rectangle(Width - Ui.Px(28), (Height - arrow) / 2, arrow, arrow),
                Enabled ? Theme.Muted : Theme.TextDisabled, 1.8f);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }
    }

    /// <summary>自绘勾选框。</summary>
    class UiCheckBox : CheckBox
    {
        UiTween tween = new UiTween();
        bool hot;

        public UiCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            Appearance = Appearance.Normal;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaintBackground(PaintEventArgs pe)
        {
            // 用父控件底色清屏：透明背景会让 WinForms 用同一 DC 重绘整棵树，产生错位残影。
            Control parent = Parent;
            pe.Graphics.Clear(parent != null ? parent.BackColor : Theme.Bg);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tween.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hot = true;
            tween.To(1f, delegate(float v) { Invalidate(); });
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hot = false;
            tween.To(0f, delegate(float v) { Invalidate(); });
            base.OnMouseLeave(e);
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            tween.To(Checked ? 1f : 0f, delegate(float v) { Invalidate(); });
            base.OnCheckedChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            UiKit.ClearBackground(g, this);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int box = Ui.Px(18);
            int top = (Height - box) / 2;
            Rectangle mark = new Rectangle(0, top, box, box);
            float mix = tween.Value;
            bool indeterminate = CheckState == CheckState.Indeterminate;

            Color fill = Enabled ? (Checked || indeterminate ? Theme.Accent : Theme.Field) : Theme.FieldDisabled;
            Color stroke = Enabled ? (Checked || indeterminate ? Theme.Accent : Theme.Border) : Theme.Border;

            if (hot && Enabled && !Checked) stroke = Theme.BorderStrong;
            UiKit.FillRound(g, mark, Ui.Px(5), fill);
            UiKit.StrokeRound(g, mark, Ui.Px(5), stroke, 1.2f);

            if (mix > 0.02f)
            {
                Color tick = System.Drawing.Color.White;
                int size = Ui.Px(14);
                Rectangle tr = new Rectangle(mark.X + (box - size) / 2, mark.Y + (box - size) / 2, size, size);
                using (GraphicsPath path = new GraphicsPath())
                {
                    float u = size / 24f;
                    path.AddLines(new PointF[] {
                        new PointF(tr.X + 5 * u, tr.Y + 12.5f * u),
                        new PointF(tr.X + 10 * u, tr.Y + 17 * u),
                        new PointF(tr.X + 19 * u, tr.Y + 7 * u) });
                    using (Pen pen = new Pen(tick, 2.2f * (0.6f + mix * 0.4f)))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        pen.LineJoin = LineJoin.Round;
                        g.DrawPath(pen, path);
                    }
                }
            }

            Rectangle textRect = new Rectangle(box + Ui.Px(9), 0, Math.Max(10, Width - box - Ui.Px(9)), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, Enabled ? ForeColor : Theme.TextDisabled,
                TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.Left
                | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>自绘单选按钮。</summary>
    class UiRadioButton : RadioButton
    {
        UiTween tween = new UiTween();

        public UiRadioButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            Appearance = Appearance.Normal;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaintBackground(PaintEventArgs pe)
        {
            Control parent = Parent;
            pe.Graphics.Clear(parent != null ? parent.BackColor : Theme.Bg);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tween.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            tween.To(Checked ? 1f : 0f, delegate(float v) { Invalidate(); });
            base.OnCheckedChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            UiKit.ClearBackground(g, this);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int box = Ui.Px(18);
            int top = (Height - box) / 2;
            Rectangle mark = new Rectangle(0, top, box, box);
            float mix = tween.Value;

            using (SolidBrush b = new SolidBrush(Enabled ? Theme.Field : Theme.FieldDisabled))
                g.FillEllipse(b, mark);
            using (Pen pen = new Pen(Checked && Enabled ? Theme.Accent : Theme.Border, 1.4f))
                g.DrawEllipse(pen, mark);
            if (mix > 0.02f)
            {
                int d = (int)(Ui.Px(10) * mix);
                using (SolidBrush b = new SolidBrush(Enabled ? Theme.Accent : Theme.TextDisabled))
                    g.FillEllipse(b, mark.X + (box - d) / 2, mark.Y + (box - d) / 2, d, d);
            }

            Rectangle textRect = new Rectangle(box + Ui.Px(9), 0, Math.Max(10, Width - box - Ui.Px(9)), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, Enabled ? ForeColor : Theme.TextDisabled,
                TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.Left
                | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>自绘内存滑块：圆角轨道、已选区间与发光滑块。</summary>
    class UiTrackBar : TrackBar
    {
        public UiTrackBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TickStyle = TickStyle.None;
            BackColor = Theme.Bg;
        }

        protected override void OnPaintBackground(PaintEventArgs pe)
        {
            Control parent = Parent;
            pe.Graphics.Clear(parent != null ? parent.BackColor : Theme.Bg);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            UiKit.ClearBackground(g, this);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int knob = Ui.Px(16);
            int cy = Height / 2;
            int travel = Math.Max(1, Width - knob);
            double ratio = (Maximum > Minimum) ? (double)(Value - Minimum) / (Maximum - Minimum) : 0;
            ratio = Math.Max(0, Math.Min(1, ratio));
            int knobX = (int)Math.Round(travel * ratio);

            Rectangle track = new Rectangle(0, cy - Ui.Px(3), Math.Max(1, Width), Ui.Px(6));
            UiKit.FillRound(g, track, Ui.Px(3), Theme.Field);
            Rectangle filled = new Rectangle(track.X, track.Y, Math.Max(Ui.Px(6), knobX + knob / 2), track.Height);
            UiKit.FillRoundGradient(g, filled, Ui.Px(3), Theme.AccentLight, Theme.Accent, false);

            Rectangle knobRect = new Rectangle(knobX, cy - knob / 2, knob, knob);
            UiKit.Glow(g, knobRect, knob / 2, UiKit.Alpha(Theme.Accent, 180), Ui.Px(7));
            UiKit.FillRound(g, knobRect, knob / 2, System.Drawing.Color.White);
            UiKit.StrokeRound(g, knobRect, knob / 2, UiKit.Alpha(Theme.Accent, 220), 1.6f);
        }
    }

    /// <summary>自绘数字输入：隐藏系统微调按钮，改用自绘箭头。</summary>
    class UiNumeric : NumericUpDown
    {
        bool hotUp;
        bool hotDown;

        public UiNumeric()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BorderStyle = BorderStyle.None;
            BackColor = Theme.Field;
            ForeColor = Theme.Text;
            Font = Theme.Input;
            TextAlign = HorizontalAlignment.Left;
            HideSpinButtons();
            HandleCreated += delegate { HideSpinButtons(); };
        }

        void HideSpinButtons()
        {
            foreach (Control c in Controls)
                if (c.GetType().Name == "UpDownButtons") c.Visible = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            UiKit.ClearBackground(g, this);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width, Height);
            UiKit.FillRound(g, r, Ui.Px(8), Enabled ? Theme.Field : Theme.FieldDisabled);
            UiKit.StrokeRound(g, r, Ui.Px(8), Theme.Border, 1f);
            Rectangle textRect = new Rectangle(Ui.Px(10), 0, Math.Max(10, Width - Ui.Px(40)), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, Enabled ? Theme.Text : Theme.TextDisabled,
                TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.Left
                | TextFormatFlags.SingleLine);

            int size = Ui.Px(12);
            int cx = Width - Ui.Px(16);
            Color arrow = Enabled ? (hotUp || hotDown ? Theme.Text : Theme.Muted) : Theme.TextDisabled;
            UiKit.DrawIcon(g, UiIcon.Plus, new Rectangle(cx - size / 2, Ui.Px(5), size, size), arrow, 1.6f);
            UiKit.DrawIcon(g, UiIcon.Minus, new Rectangle(cx - size / 2, Height - Ui.Px(5) - size, size, size), arrow, 1.6f);
        }

        protected override void OnMouseMove(MouseEventArgs me)
        {
            hotUp = me.Y < Height / 2;
            hotDown = !hotUp;
            Invalidate();
            base.OnMouseMove(me);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hotUp = false; hotDown = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs me)
        {
            if (Enabled && me.X > Width - Ui.Px(30))
            {
                if (me.Y < Height / 2)
                {
                    if (Value < Maximum) Value++;
                }
                else
                {
                    if (Value > Minimum) Value--;
                }
                Invalidate();
                return;
            }
            base.OnMouseDown(me);
        }

        protected override void OnValueChanged(EventArgs e)
        {
            Invalidate();
            base.OnValueChanged(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }
    }

    /// <summary>
    /// 自绘列表表头条。
    /// 原生 ListView 表头在最后一列右侧会露出系统浅色区域，深色主题下极其刺眼，
    /// 因此隐藏原生表头，改用与列宽对齐的自绘表头。
    /// </summary>
    class UiListHeader : Control
    {
        public List<string> Captions = new List<string>();
        public List<int> Widths = new List<int>();

        public UiListHeader()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            Font = Theme.UiBold;
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            Graphics g = pe.Graphics;
            UiKit.ClearBackground(g, this);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int x = 0;
            for (int i = 0; i < Captions.Count; i++)
            {
                int cw = i < Widths.Count ? Widths[i] : 0;
                if (cw <= 0) continue;
                TextRenderer.DrawText(g, Captions[i], Theme.UiBold,
                    new Rectangle(x + Ui.Px(10), 0, Math.Max(10, cw - Ui.Px(16)), Height), Theme.Muted,
                    TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.Left
                    | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                x += cw;
            }
            using (Pen p = new Pen(Theme.Border))
                g.DrawLine(p, 0, Height - 1, Width, Height - 1);
        }
    }

    /// <summary>自绘列表：深色表头、斑马行、悬停高亮与强调选中态。</summary>
    class UiListView : ListView
    {
        int hoverIndex = -1;

        public UiListView()
        {
            OwnerDraw = true;
            View = View.Details;
            FullRowSelect = true;
            HideSelection = false;
            BorderStyle = BorderStyle.None;
            // 隐藏原生表头：最后一列右侧会露出系统浅色区域，深色主题下无法接受。
            HeaderStyle = ColumnHeaderStyle.None;
            BackColor = Theme.Field;
            ForeColor = Theme.Text;
            DoubleBuffered = true;
            DrawColumnHeader += OnHeader;
            DrawItem += OnItem;
        }

        protected override void OnMouseMove(MouseEventArgs me)
        {
            int idx = -1;
            ListViewHitTestInfo hit = HitTest(me.X, me.Y);
            if (hit != null && hit.Item != null) idx = hit.Item.Index;
            if (idx != hoverIndex)
            {
                hoverIndex = idx;
                Invalidate();
            }
            base.OnMouseMove(me);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (hoverIndex != -1) { hoverIndex = -1; Invalidate(); }
            base.OnMouseLeave(e);
        }

        void OnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle r = e.Bounds;
            using (SolidBrush b = new SolidBrush(Theme.Card))
                g.FillRectangle(b, r);
            using (Pen p = new Pen(Theme.Border))
                g.DrawLine(p, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);
            TextRenderer.DrawText(g, e.Header.Text, Theme.UiBold,
                new Rectangle(r.X + Ui.Px(10), r.Y, Math.Max(10, r.Width - Ui.Px(14)), r.Height), Theme.Muted,
                TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.Left
                | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }

        void OnItem(object sender, DrawListViewItemEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle r = e.Bounds;
            bool selected = e.Item.Selected;
            bool hovered = !selected && e.ItemIndex == hoverIndex;
            if (selected)
            {
                using (SolidBrush b = new SolidBrush(UiKit.Alpha(Theme.Accent, 38)))
                    g.FillRectangle(b, r);
                using (Pen p = new Pen(UiKit.Alpha(Theme.Accent, 120), 1f))
                    g.DrawLine(p, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);
            }
            else if (hovered)
            {
                using (SolidBrush b = new SolidBrush(UiKit.Alpha(System.Drawing.Color.White, 10)))
                    g.FillRectangle(b, r);
                using (SolidBrush b = new SolidBrush(UiKit.Alpha(Theme.Accent, 150)))
                    g.FillRectangle(b, r.X, r.Y, Ui.Px(2), r.Height);
            }
            else if (e.ItemIndex % 2 == 1)
            {
                using (SolidBrush b = new SolidBrush(UiKit.Alpha(System.Drawing.Color.White, 4)))
                    g.FillRectangle(b, r);
            }
            e.DrawDefault = false;
            Color fore = e.Item.ForeColor;
            if (fore.IsEmpty) fore = Theme.Text;
            for (int i = 0; i < e.Item.SubItems.Count; i++)
            {
                Rectangle cell = e.Item.SubItems[i].Bounds;
                if (cell.Width <= 0) continue;
                Color cellColor = e.Item.SubItems[i].ForeColor;
                if (cellColor.IsEmpty) cellColor = fore;
                TextRenderer.DrawText(g, e.Item.SubItems[i].Text, e.Item.SubItems[i].Font ?? Font,
                    new Rectangle(cell.X + Ui.Px(10), cell.Y, Math.Max(10, cell.Width - Ui.Px(16)), cell.Height),
                    cellColor,
                    TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.Left
                    | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            }
        }
    }

    /// <summary>
    /// 与父容器底色融为一体的标签。
    /// 不用 Color.Transparent：透明背景会让 WinForms 用同一个 DC 重绘整棵控件树，
    /// 导致侧栏图标等内容被画到错误位置（残影）。这里改为自行清除父容器底色。
    /// </summary>
    class UiLabel : Label
    {
        /// <summary>是否与父容器底色融合；状态栏这类自绘底色的控件需要关闭。</summary>
        public bool BlendBackground = true;

        public UiLabel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaintBackground(PaintEventArgs pe)
        {
            if (!BlendBackground) { base.OnPaintBackground(pe); return; }
            Control parent = Parent;
            pe.Graphics.Clear(parent != null ? parent.BackColor : Theme.Bg);
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            // 双缓冲下 OnPaintBackground 不会被调用，必须在这里铺底色，否则会露出复用缓冲的残影。
            if (BlendBackground) UiKit.ClearBackground(pe.Graphics, this);
            base.OnPaint(pe);
        }
    }

    /// <summary>
    /// 内容页容器：统一绘制背景光晕与页头，并承载入场过渡状态。
    /// 页头必须由页面自己绘制——子控件只能铺平底色，会把背景光晕擦出硬边矩形；
    /// 页面背景保持不透明，透明背景会让 WinForms 用同一个 DC 重绘整棵控件树，产生错位残影。
    /// </summary>
    class UiPage : Panel
    {
        /// <summary>页头主标题。</summary>
        public string HeaderTitle = "";
        /// <summary>页头副标题。</summary>
        public string HeaderSubtitle = "";
        /// <summary>页头入场进度 0~1（1 为完全显示）；用颜色混合模拟淡入，避免引入透明背景。</summary>
        public float HeaderFade = 1f;
        /// <summary>页头纵向位移（像素），入场时从上方滑入。</summary>
        public int HeaderShift = 0;

        public UiPage()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Dock = DockStyle.None;
            BackColor = Theme.Bg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            UiKit.ClearBackground(g, this);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            UiKit.RadialGlow(g, new Point(Width - Ui.Px(90), Ui.Px(20)), Ui.Px(320), UiKit.Alpha(Theme.Accent, 22));
            UiKit.RadialGlow(g, new Point(Ui.Px(40), Height - Ui.Px(10)), Ui.Px(300), UiKit.Alpha(Theme.Blue, 18));
            DrawHeader(g);
        }

        /// <summary>绘制页头标题与副标题（带淡入与位移）。</summary>
        void DrawHeader(Graphics g)
        {
            if (HeaderTitle.Length == 0) return;
            float f = HeaderFade;
            if (f <= 0.002f) return;
            int dy = HeaderShift;
            Color title = UiKit.Mix(Theme.Bg, Theme.Text, f);
            Color sub = UiKit.Mix(Theme.Bg, Theme.Muted, f);
            TextRenderer.DrawText(g, HeaderTitle, Theme.HeadingBold,
                new Rectangle(0, dy, Ui.Px(320), Ui.Px(30)), title,
                TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(g, HeaderSubtitle, Theme.Caption,
                new Rectangle(Ui.Px(1), dy + Ui.Px(24), Ui.Px(480), Ui.Px(26)), sub,
                TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>圆角卡片容器，带细边框与柔和投影。</summary>
    class UiCard : Panel
    {
        int radius = 12;

        public UiCard()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            DoubleBuffered = true;
        }

        public int Radius { get { return radius; } set { radius = value; Invalidate(); } }
        public bool Shadow { get; set; }
        public Color Fill { get { return BackColor; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            UiKit.ClearBackground(g, this);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width, Height);
            int rad = Ui.Px(radius);
            if (Shadow) UiKit.DropShadow(g, r, rad, Color.Black, Ui.Px(10));
            UiKit.FillRound(g, r, rad, BackColor);
            UiKit.StrokeRound(g, r, rad, Theme.Border, 1f);
        }
    }

    /// <summary>侧边栏品牌标识：渐变方形图标 + 中英文名称。</summary>
    class UiBrand : Control
    {
        public UiBrand()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel;
        }

        protected override void OnPaintBackground(PaintEventArgs pe)
        {
            Control parent = Parent;
            pe.Graphics.Clear(parent != null ? parent.BackColor : Theme.Panel);
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            Graphics g = pe.Graphics;
            UiKit.ClearBackground(g, this);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int size = Ui.Px(38);
            int y = Ui.Px(22);
            Rectangle logo = new Rectangle(Ui.Px(18), y, size, size);
            UiKit.Glow(g, logo, Ui.Px(11), UiKit.Alpha(Theme.Accent, 170), Ui.Px(12));
            UiKit.FillRoundGradient(g, logo, Ui.Px(11), Theme.AccentLight, Theme.AccentDark, true);
            UiKit.StrokeRound(g, logo, Ui.Px(11), UiKit.Alpha(System.Drawing.Color.White, 60), 1f);
            int glyph = Ui.Px(20);
            UiKit.DrawIcon(g, UiIcon.Cube, new Rectangle(logo.X + (size - glyph) / 2, logo.Y + (size - glyph) / 2, glyph, glyph), System.Drawing.Color.White, 1.8f);

            int textX = Ui.Px(66);
            int textW = Math.Max(10, Width - textX - Ui.Px(12));
            TextRenderer.DrawText(g, "溯昀启动器", Theme.TitleBold,
                new Rectangle(textX, y - Ui.Px(2), textW, Ui.Px(24)), Theme.Text,
                TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(g, "SUYUNCRAFT", Theme.Caption,
                new Rectangle(textX + Ui.Px(1), y + Ui.Px(21), textW, Ui.Px(16)), Theme.Muted,
                TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>侧边栏导航项：图标 + 文字 + 选中指示条。</summary>
    class UiNavItem : Control
    {
        UiTween hover = new UiTween();
        UiTween activeTween = new UiTween();

        public UiIcon Icon = UiIcon.Home;
        public string Caption = "";
        public bool Active;

        public UiNavItem()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
            Font = Theme.NavBold;
            Cursor = Cursors.Hand;
            Height = 42;
        }

        protected override void OnPaintBackground(PaintEventArgs pe)
        {
            Control parent = Parent;
            pe.Graphics.Clear(parent != null ? parent.BackColor : Theme.Panel);
        }

        /// <summary>切换选中状态并播放过渡动画。</summary>
        public void SetActive(bool value)
        {
            if (Active == value) return;
            Active = value;
            activeTween.To(value ? 1f : 0f, delegate(float v) { Invalidate(); });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { hover.Dispose(); activeTween.Dispose(); }
            base.Dispose(disposing);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover.To(1f, delegate(float v) { Invalidate(); });
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover.To(0f, delegate(float v) { Invalidate(); });
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            Graphics gg = pe.Graphics;
            UiKit.ClearBackground(gg, this);
            gg.SmoothingMode = SmoothingMode.AntiAlias;
            float a = activeTween.Value;
            float h = hover.Value;
            Rectangle r = new Rectangle(0, 0, Width, Height);

            if (a > 0.01f)
            {
                Rectangle pill = new Rectangle(0, Ui.Px(2), Width, Height - Ui.Px(4));
                UiKit.FillRoundGradient(gg, pill, Ui.Px(10),
                    UiKit.Alpha(Theme.Accent, (int)(46 * a)), UiKit.Alpha(Theme.AccentDark, (int)(24 * a)), true);
                UiKit.StrokeRound(gg, pill, Ui.Px(10), UiKit.Alpha(Theme.Accent, (int)(70 * a)), 1f);
            }
            else if (h > 0.01f)
            {
                UiKit.FillRound(gg, new Rectangle(0, Ui.Px(2), Width, Height - Ui.Px(4)), Ui.Px(10),
                    UiKit.Alpha(System.Drawing.Color.White, (int)(14 * h)));
            }

            if (a > 0.01f)
            {
                Rectangle bar = new Rectangle(0, Ui.Px(10), Ui.Px(3), Height - Ui.Px(20));
                UiKit.FillRound(gg, bar, Ui.Px(2), UiKit.Alpha(Theme.Accent, (int)(255 * a)));
            }

            Color fore = UiKit.Mix(Theme.Muted, Theme.Text, Math.Max(h, a));
            if (a > 0.5) fore = System.Drawing.Color.White;

            int iconSize = Ui.Px(18);
            int left = Ui.Px(14);
            UiKit.DrawIcon(gg, Icon, new Rectangle(left, (Height - iconSize) / 2, iconSize, iconSize),
                UiKit.Mix(fore, Theme.Accent, a), 1.8f);

            Rectangle textRect = new Rectangle(left + iconSize + Ui.Px(11), 0,
                Math.Max(10, Width - left - iconSize - Ui.Px(20)), Height);
            TextRenderer.DrawText(gg, Caption, Font, textRect, fore,
                TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.Left
                | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>日志视图：等宽字体、分类着色与自绘滚动条。</summary>
    class UiLogView : Control
    {
        readonly List<string> lines = new List<string>();
        int scrollLine;
        bool autoScroll = true;

        public UiLogView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Field;
            Font = Theme.Console;
            DoubleBuffered = true;
        }

        public int LineCount { get { return lines.Count; } }

        public int TextLength
        {
            get
            {
                int total = 0;
                foreach (string l in lines) total += l.Length + 1;
                return total;
            }
        }

        public override string Text
        {
            get
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < lines.Count; i++)
                {
                    if (i > 0) sb.Append("\r\n");
                    sb.Append(lines[i]);
                }
                return sb.ToString();
            }
            set
            {
                lines.Clear();
                if (!string.IsNullOrEmpty(value))
                {
                    string[] parts = value.Replace("\r\n", "\n").Split('\n');
                    foreach (string p in parts) lines.Add(p);
                }
                autoScroll = true;
                scrollLine = 0;
                Invalidate();
            }
        }

        public void AppendText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            string[] parts = text.Replace("\r\n", "\n").Split('\n');
            foreach (string p in parts) lines.Add(p);
            if (lines.Count > 4000) lines.RemoveRange(0, lines.Count - 4000);
            if (autoScroll) scrollLine = Math.Max(0, lines.Count - VisibleLineCount());
            Invalidate();
        }

        public void Clear()
        {
            lines.Clear();
            scrollLine = 0;
            autoScroll = true;
            Invalidate();
        }

        public void ScrollToEnd()
        {
            scrollLine = Math.Max(0, lines.Count - VisibleLineCount());
            Invalidate();
        }

        int VisibleLineCount()
        {
            return Math.Max(1, (Height - 12) / Math.Max(12, LineHeight()));
        }

        int LineHeight()
        {
            Size measured = TextRenderer.MeasureText("W", Font);
            return Math.Max(12, measured.Height + 2);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            scrollLine -= e.Delta / 120 * 3;
            ClampScroll();
            autoScroll = scrollLine >= lines.Count - VisibleLineCount();
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
        }

        void ClampScroll()
        {
            int max = Math.Max(0, lines.Count - VisibleLineCount());
            if (scrollLine > max) scrollLine = max;
            if (scrollLine < 0) scrollLine = 0;
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            Graphics g = pe.Graphics;
            UiKit.ClearBackground(g, this);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width, Height);
            using (SolidBrush b = new SolidBrush(Theme.Field))
                g.FillRectangle(b, r);

            ClampScroll();
            int lh = LineHeight();
            int count = VisibleLineCount();
            int y = Ui.Px(6);

            for (int i = scrollLine; i < lines.Count && y < Height; i++, y += lh)
            {
                string line = lines[i];
                Color fore = ColorFor(line);
                TextRenderer.DrawText(g, line, Font, new Rectangle(Ui.Px(10), y, Math.Max(10, Width - Ui.Px(28)), lh + 2),
                    fore, TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.NoPadding
                    | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            }

            if (lines.Count == 0)
            {
                TextRenderer.DrawText(g, "暂无输出。启动游戏后这里会显示 stdout / stderr。", Font,
                    new Rectangle(Ui.Px(12), Ui.Px(10), Math.Max(10, Width - Ui.Px(24)), Ui.Px(24)), Theme.TextDisabled,
                    TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.NoPadding);
            }

            if (lines.Count > count)
            {
                int trackH = Height - Ui.Px(12);
                int thumbH = Math.Max(Ui.Px(28), trackH * count / lines.Count);
                int maxScroll = Math.Max(1, lines.Count - count);
                int thumbY = Ui.Px(6) + (trackH - thumbH) * scrollLine / maxScroll;
                Rectangle track = new Rectangle(Width - Ui.Px(11), Ui.Px(6), Ui.Px(5), trackH);
                UiKit.FillRound(g, track, Ui.Px(3), UiKit.Alpha(System.Drawing.Color.White, 12));
                Rectangle thumb = new Rectangle(Width - Ui.Px(11), thumbY, Ui.Px(5), thumbH);
                UiKit.FillRound(g, thumb, Ui.Px(3), UiKit.Alpha(System.Drawing.Color.White, 52));
            }
        }

        static Color ColorFor(string line)
        {
            if (line.StartsWith("[err]")) return Theme.Danger;
            if (line.StartsWith("[out]")) return Theme.LogMuted;
            if (line.StartsWith("[启动器]")) return Theme.Accent;
            if (line.StartsWith("[预检")) return Theme.Warning;
            return Theme.LogText;
        }
    }
}
