using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;

namespace SuyunCraft
{
    /// <summary>Button visual type。</summary>
    public enum UiButtonKind
    {
        Primary,
        Secondary,
        Ghost,
        Danger,
        Success
    }

    /// <summary>Lightweight tween for hover and state transitions。</summary>
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

    /// <summary>Unified dark button: rounded, gradient, hover feedback and vector icons。</summary>
    class UiButton : Button
    {
        UiTween hover = new UiTween();
        UiTween press = new UiTween();

        public UiButtonKind Kind = UiButtonKind.Secondary;
        public UiIcon Icon = UiIcon.None;
        public int Radius = 6;

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
            int rad = Ui.Px(Radius);
            float hv = hover.Value;
            float pv = press.Value;

            Color fill;
            Color stroke;
            Color deep;
            bool solid = true;

            if (!Enabled)
            {
                fill = Theme.Card;
                stroke = Theme.Border;
                deep = Theme.Bg;
                solid = false;
            }
            else if (Kind == UiButtonKind.Primary)
            {
                fill = UiKit.Mix(Theme.Accent, Theme.AccentLight, hv);
                stroke = Theme.AccentDark;
                deep = Theme.AccentDeep;
            }
            else if (Kind == UiButtonKind.Success)
            {
                fill = UiKit.Mix(Theme.Blue, Theme.BlueLight, hv);
                stroke = Theme.BlueDark;
                deep = Theme.BlueDark;
            }
            else if (Kind == UiButtonKind.Danger)
            {
                fill = UiKit.Mix(Theme.Danger, Theme.DangerLight, hv);
                stroke = Theme.DangerDark;
                deep = Theme.DangerDark;
            }
            else if (Kind == UiButtonKind.Ghost)
            {
                fill = Parent != null ? Parent.BackColor : Theme.Bg;
                stroke = UiKit.Alpha(fill, 0);
                deep = stroke;
                solid = false;
            }
            else
            {
                fill = UiKit.Mix(Theme.CardTop, Theme.CardHover, hv);
                stroke = UiKit.Mix(Theme.Border, Theme.BorderStrong, hv);
                deep = Theme.Bg;
            }

            // Pressed: the whole block sinks and bounces back - like pushing a block into the ground
            int sink = pv > 0.45f ? Ui.Px(1) : 0;
            Rectangle body = new Rectangle(r.X, r.Y + sink, r.Width, r.Height - sink);

            if (Kind == UiButtonKind.Ghost)
            {
                if (hv > 0.01f)
                    UiKit.FillRound(g, body, rad, UiKit.Alpha(Theme.Text, (int)(16 * hv)));
            }
            else if (solid)
            {
                // Soft outer glow on hover
                if (Enabled && hv > 0.01f)
                    UiKit.Glow(g, body, rad, UiKit.Alpha(fill, (int)(110 * hv)), Ui.Px(6));

                // A dark base first, then lift the lit bottom edge by 2px; those 2px are the block rim」
                int edge = Math.Max(1, Ui.Px(2));
                UiKit.FillRound(g, body, rad, deep);
                Rectangle face = new Rectangle(body.X, body.Y, body.Width, Math.Max(Ui.Px(6), body.Height - edge));
                Color top = UiKit.Mix(fill, System.Drawing.Color.White, 0.13);
                Color bottom = UiKit.Mix(fill, System.Drawing.Color.Black, 0.06);
                UiKit.FillRoundGradient(g, face, rad, top, bottom, true);
                UiKit.Bevel(g, face, rad, UiKit.Alpha(System.Drawing.Color.White, Enabled ? 52 : 22), UiKit.Alpha(System.Drawing.Color.Black, 46));
                UiKit.StrokeRound(g, body, rad, UiKit.Alpha(stroke, Enabled ? 255 : 120), 1f);
            }
            else
            {
                UiKit.FillRound(g, body, rad, fill);
                UiKit.StrokeRound(g, body, rad, UiKit.Alpha(stroke, Enabled ? 200 : 120), 1f);
            }

            if (Focused && Enabled)
                UiKit.StrokeRound(g, Rectangle.Inflate(body, -2, -2), Math.Max(1, rad - 1), UiKit.Alpha(Theme.FocusRing, 170), 1f);

            Color fore = Enabled ? ForeColor : Theme.Muted;
            bool lightText = Kind == UiButtonKind.Primary || Kind == UiButtonKind.Success || Kind == UiButtonKind.Danger;
            if (lightText) fore = System.Drawing.Color.White;

            int iconSize = Math.Min(Ui.Px(18), Math.Max(Ui.Px(10), body.Height - Ui.Px(12)));
            bool hasIcon = Icon != UiIcon.None && iconSize >= Ui.Px(10);
            Size textSize = TextRenderer.MeasureText(Text, Font, new Size(Width, Height),
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            int gap = hasIcon ? Ui.Px(8) : 0;
            int total = (hasIcon ? iconSize : 0) + gap + textSize.Width;
            int startX = Math.Max(0, (Width - total) / 2);
            int cy = body.Y + (body.Height - iconSize) / 2;

            if (hasIcon)
            {
                if (lightText && Enabled)
                    UiKit.DrawIcon(g, Icon, new Rectangle(startX, cy + 1, iconSize, iconSize),
                        UiKit.Alpha(System.Drawing.Color.Black, 70), 1.8f);
                UiKit.DrawIcon(g, Icon, new Rectangle(startX, cy, iconSize, iconSize), fore, 1.8f);
            }
            if (Text.Length > 0)
            {
                int textX = startX + (hasIcon ? iconSize + gap : 0);
                Rectangle textRect = new Rectangle(textX, body.Y, Math.Max(0, Width - textX), body.Height);
                TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
                if (lightText && Enabled)
                {
                    UiKit.DrawTextShadow(g, Text, Font, textRect, fore, flags,
                        UiKit.Alpha(System.Drawing.Color.Black, 90), 1);
                }
                else
                {
                    TextRenderer.DrawText(g, Text, Font, textRect, fore, flags);
                }
            }
        }
    }

    /// <summary>Rounded text field: custom border, focus glow, borderless inner box。</summary>
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
            int rad = Ui.Px(6);
            UiKit.FillRound(g, r, rad, Enabled ? Theme.Field : Theme.FieldDisabled);
            UiKit.Inset(g, r, rad, UiKit.Alpha(System.Drawing.Color.Black, 110));
            if (focused && Enabled)
            {
                UiKit.Glow(g, r, rad, UiKit.Alpha(Theme.Accent, 150), Ui.Px(5));
                UiKit.StrokeRound(g, r, rad, Theme.Accent, 1.4f);
            }
            else
            {
                UiKit.StrokeRound(g, r, rad, UiKit.Mix(Theme.Border, Theme.BorderStrong, focused ? 1f : 0f), 1f);
            }
        }
    }

    /// <summary>Owner-drawn combo: rounded look and vector arrow; the dropdown list stays system-dark。</summary>
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
            int rad = Ui.Px(6);
            UiKit.FillRound(g, r, rad, Enabled ? Theme.Field : Theme.FieldDisabled);
            UiKit.Inset(g, r, rad, UiKit.Alpha(System.Drawing.Color.Black, 110));
            if (focused && Enabled)
            {
                UiKit.Glow(g, r, rad, UiKit.Alpha(Theme.Accent, 140), Ui.Px(5));
                UiKit.StrokeRound(g, r, rad, Theme.Accent, 1.4f);
            }
            else UiKit.StrokeRound(g, r, rad, Theme.Border, 1f);

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

    /// <summary>Owner-drawn checkbox。</summary>
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
            // Clear with the parent back color: transparency repaints the whole tree on one DC, causing ghosting。
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
            Color stroke = Enabled ? (Checked || indeterminate ? Theme.AccentDark : Theme.Border) : Theme.Border;

            if (hot && Enabled && !Checked) stroke = Theme.BorderStrong;
            int rad = Ui.Px(3);
            if (Checked || indeterminate)
            {
                UiKit.FillRound(g, mark, rad, Theme.AccentDeep);
                Rectangle face = new Rectangle(mark.X, mark.Y, mark.Width, mark.Height - Math.Max(1, Ui.Px(2)));
                UiKit.FillRoundGradient(g, face, rad, Theme.AccentLight, Theme.Accent, true);
                UiKit.Bevel(g, face, rad, UiKit.Alpha(System.Drawing.Color.White, 55), UiKit.Alpha(System.Drawing.Color.Black, 40));
            }
            else
            {
                UiKit.FillRound(g, mark, rad, fill);
                UiKit.Inset(g, mark, rad, UiKit.Alpha(System.Drawing.Color.Black, 120));
            }
            UiKit.StrokeRound(g, mark, rad, stroke, 1.2f);

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

    /// <summary>Owner-drawn radio button。</summary>
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

    /// <summary>Owner-drawn slider: rounded track, selected range, glowing thumb。</summary>
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
            int knobW = Ui.Px(14);
            int knobH = Ui.Px(18);
            int cy = Height / 2;
            int travel = Math.Max(1, Width - knobW);
            double ratio = (Maximum > Minimum) ? (double)(Value - Minimum) / (Maximum - Minimum) : 0;
            ratio = Math.Max(0, Math.Min(1, ratio));
            int knobX = (int)Math.Round(travel * ratio);

            // groovetrack
            int rad = Ui.Px(2);
            Rectangle track = new Rectangle(0, cy - Ui.Px(4), Math.Max(1, Width), Ui.Px(8));
            UiKit.FillRound(g, track, rad, Theme.FieldDeep);
            UiKit.Inset(g, track, rad, UiKit.Alpha(System.Drawing.Color.Black, 130));
            UiKit.StrokeRound(g, track, rad, Theme.Border, 1f);

            // Selected range: grass-green fill + Segmented ticks
            int fillRight = Math.Min(track.Right, knobX + knobW / 2);
            if (fillRight > Ui.Px(4))
            {
                Rectangle filled = new Rectangle(track.X, track.Y, fillRight, track.Height);
                UiKit.FillRoundGradient(g, filled, rad, Theme.AccentLight, Theme.AccentDark, true);
                using (Pen tick = new Pen(UiKit.Alpha(System.Drawing.Color.Black, 55), 1f))
                {
                    for (int x = filled.X + Ui.Px(6); x < filled.Right - Ui.Px(2); x += Ui.Px(7))
                        g.DrawLine(tick, x, filled.Top + Ui.Px(2), x, filled.Bottom - Ui.Px(3));
                }
            }

            // Thumb
            Rectangle knobRect = new Rectangle(knobX, cy - knobH / 2, knobW, knobH);
            UiKit.PixelShadow(g, knobRect, rad, Theme.Shadow, Ui.Px(2));
            UiKit.FillRound(g, knobRect, rad, Theme.AccentDeep);
            Rectangle kface = new Rectangle(knobRect.X, knobRect.Y, knobRect.Width, knobRect.Height - Ui.Px(2));
            UiKit.FillRoundGradient(g, kface, rad,
                System.Drawing.Color.FromArgb(240, 244, 236), System.Drawing.Color.FromArgb(204, 212, 200), true);
            UiKit.Bevel(g, kface, rad, UiKit.Alpha(System.Drawing.Color.White, 170), UiKit.Alpha(System.Drawing.Color.Black, 60));
            using (Pen grip = new Pen(UiKit.Alpha(System.Drawing.Color.Black, 60), 1f))
            {
                g.DrawLine(grip, knobRect.X + Ui.Px(4), cy - Ui.Px(3), knobRect.Right - Ui.Px(5), cy - Ui.Px(3));
                g.DrawLine(grip, knobRect.X + Ui.Px(4), cy + Ui.Px(2), knobRect.Right - Ui.Px(5), cy + Ui.Px(2));
            }
        }
    }

    /// <summary>Owner-drawn numeric input: hidden spin buttons, custom arrows。</summary>
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
            int rad = Ui.Px(6);
            UiKit.FillRound(g, r, rad, Enabled ? Theme.Field : Theme.FieldDisabled);
            UiKit.Inset(g, r, rad, UiKit.Alpha(System.Drawing.Color.Black, 110));
            UiKit.StrokeRound(g, r, rad, Theme.Border, 1f);
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
    /// Owner-drawn list header.
    /// The native ListView header leaves a light gap after the last column, jarring on dark;
    /// so the native header is hidden in favor of an aligned custom one。
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

    /// <summary>Owner-drawn list: dark header, zebra rows, hover highlight, emphatic selection。</summary>
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
            // Hide the native header: its trailing area renders light, unacceptable on dark。
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
                using (SolidBrush b = new SolidBrush(UiKit.Alpha(Theme.Accent, 34)))
                    g.FillRectangle(b, r);
                using (SolidBrush b = new SolidBrush(Theme.Accent))
                    g.FillRectangle(b, r.X, r.Y, Ui.Px(3), r.Height);
                using (Pen p = new Pen(UiKit.Alpha(Theme.Accent, 110), 1f))
                    g.DrawLine(p, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);
            }
            else if (hovered)
            {
                using (SolidBrush b = new SolidBrush(UiKit.Alpha(System.Drawing.Color.White, 9)))
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
    /// A label that blends into its container.
    /// No Color.Transparent: transparency repaints the whole tree on one DC
    /// misplacing sidebar icons (ghosting). We clear with the parent back color instead。
    /// </summary>
    class UiLabel : Label
    {
        /// <summary>Blend with the parent back color; needed by self-painted controls like the status barClose。</summary>
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
            // With double buffering OnPaintBackground is not called; paint the base color here or pooled buffers leave ghosts。
            if (BlendBackground) UiKit.ClearBackground(pe.Graphics, this);
            base.OnPaint(pe);
        }
    }

    /// <summary>
    /// Content page container: paints the ambient glow and page header, carrying the enter-transition state.
    /// The header must be painted by the page itself - children only fill flat colors and would erase the glow;
    /// Pages stay opaque; transparency makes WinForms repaint the whole tree on one DC, causing ghosting。
    /// </summary>
    class UiPage : Panel
    {
        /// <summary>Page title。</summary>
        public string HeaderTitle = "";
        /// <summary>Page subtitle。</summary>
        public string HeaderSubtitle = "";
        /// <summary>Header enter progress 0~1（1 fully visible); fade via color blending instead of transparency。</summary>
        public float HeaderFade = 1f;
        /// <summary>Header vertical offset (px); slides in from above。</summary>
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
            // World block grid: near-invisible texture between cards
            UiKit.BlockGrid(g, new Rectangle(0, 0, Width, Height), Ui.Px(52), UiKit.Alpha(System.Drawing.Color.White, 6));
            UiKit.RadialGlow(g, new Point(Width - Ui.Px(120), Ui.Px(6)), Ui.Px(340), UiKit.Alpha(Theme.Accent, 26));
            UiKit.RadialGlow(g, new Point(Ui.Px(30), Height - Ui.Px(20)), Ui.Px(300), UiKit.Alpha(Theme.Gold, 16));
            DrawHeader(g);
        }

        /// <summary>Draw the page header title and subtitle (fade + offset）。</summary>
        void DrawHeader(Graphics g)
        {
            if (HeaderTitle.Length == 0) return;
            float f = HeaderFade;
            if (f <= 0.002f) return;
            int dy = HeaderShift;
            Color title = UiKit.Mix(Theme.Bg, Theme.Text, f);
            Color sub = UiKit.Mix(Theme.Bg, Theme.Muted, f);
            TextRenderer.DrawText(g, HeaderTitle, Theme.HeadingBold,
                new Rectangle(0, dy, Ui.Px(360), Ui.Px(32)), title,
                TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(g, HeaderSubtitle, Theme.Caption,
                new Rectangle(Ui.Px(1), dy + Ui.Px(28), Ui.Px(520), Ui.Px(24)), sub,
                TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>Rounded card with a hard shadow and lit top edge - like a stone slab。</summary>
    class UiCard : Panel
    {
        int radius = 8;

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
            UiKit.FillRound(g, r, rad, BackColor);
            if (Shadow)
            {
                // Solid dark edge at the card bottom: controls clip at their bounds, so the shadow must be inner
                GraphicsState state = g.Save();
                using (GraphicsPath p = UiKit.RoundPath(r, rad))
                using (Region clip = new Region(p))
                    g.SetClip(clip, CombineMode.Intersect);
                using (SolidBrush b = new SolidBrush(UiKit.Alpha(System.Drawing.Color.Black, 70)))
                    g.FillRectangle(b, r.X, r.Bottom - Ui.Px(3), r.Width, Ui.Px(3));
                g.Restore(state);
            }
            UiKit.Bevel(g, r, rad, Theme.BevelLight, Theme.BevelDark);
            UiKit.StrokeRound(g, r, rad, Theme.Border, 1f);
        }
    }

    /// <summary>Sidebar brand: isometric grass block + Chinese and English names.</summary>
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
            int size = Ui.Px(40);
            int y = Ui.Px(22);
            Rectangle logo = new Rectangle(Ui.Px(18), y, size, size);
            UiKit.IsoBlock(g, logo, true);

            int textX = Ui.Px(70);
            int textW = Math.Max(10, Width - textX - Ui.Px(10));
            TextRenderer.DrawText(g, "SuyunCraft Launcher", Theme.TitleBold,
                new Rectangle(textX, y - Ui.Px(2), textW, Ui.Px(24)), Theme.Text,
                TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(g, "SUYUNCRAFT", Theme.Caption,
                new Rectangle(textX + Ui.Px(1), y + Ui.Px(21), textW, Ui.Px(16)), Theme.Muted,
                TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>Sidebar nav item: icon + text + Selection indicator。</summary>
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

        /// <summary>Toggle the selected state with a transition。</summary>
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
                Rectangle pill = new Rectangle(Ui.Px(6), Ui.Px(2), Width - Ui.Px(6), Height - Ui.Px(4));
                UiKit.FillRoundGradient(gg, pill, Ui.Px(5),
                    UiKit.Alpha(Theme.Accent, (int)(44 * a)), UiKit.Alpha(Theme.AccentDark, (int)(26 * a)), true);
                UiKit.Bevel(gg, pill, Ui.Px(5), UiKit.Alpha(System.Drawing.Color.White, (int)(26 * a)), UiKit.Alpha(System.Drawing.Color.Black, (int)(40 * a)));
                UiKit.StrokeRound(gg, pill, Ui.Px(5), UiKit.Alpha(Theme.Accent, (int)(74 * a)), 1f);
            }
            else if (h > 0.01f)
            {
                UiKit.FillRound(gg, new Rectangle(Ui.Px(6), Ui.Px(2), Width - Ui.Px(6), Height - Ui.Px(4)), Ui.Px(5),
                    UiKit.Alpha(System.Drawing.Color.White, (int)(12 * h)));
            }

            if (a > 0.01f)
            {
                Rectangle bar = new Rectangle(0, Ui.Px(9), Ui.Px(3), Height - Ui.Px(18));
                UiKit.FillRound(gg, bar, Ui.Px(1), UiKit.Alpha(Theme.AccentLight, (int)(255 * a)));
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

    /// <summary>Log view: monospace, categorized colors, custom scrollbar。</summary>
    class UiLogView : Control
    {
        readonly List<string> lines = new List<string>();
        int scrollLine;
        bool autoScroll = true;

        public UiLogView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.FieldDeep;
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
            using (SolidBrush b = new SolidBrush(Theme.FieldDeep))
                g.FillRectangle(b, r);
            UiKit.Inset(g, r, 0, UiKit.Alpha(System.Drawing.Color.Black, 150));

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
                TextRenderer.DrawText(g, "No output yet. Launch the game and it appears here stdout / stderr。", Font,
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
            if (line.StartsWith("[Launch ]")) return Theme.Accent;
            if (line.StartsWith("[preflight")) return Theme.Warning;
            return Theme.LogText;
        }
    }
}
