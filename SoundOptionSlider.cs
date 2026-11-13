using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NaiwaDesktopPet
{
    // One two/three-position sliding control, usable by dragging, clicking or arrows.
    public sealed class SoundOptionSlider : Control
    {
        readonly string title;
        readonly string[] options;
        readonly Timer timer = new Timer { Interval = 15 };
        readonly float scale;
        int selected;
        float thumb;
        bool dragging;
        public event EventHandler SelectedIndexChanged;
        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                int next = Math.Max(0, Math.Min(options.Length - 1, value));
                if (selected == next) return;
                selected = next;
                if (!dragging) timer.Start();
                Invalidate();
                if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }
        RectangleF Track
        {
            get
            {
                float height = Math.Min(28 * scale, Math.Max(8 * scale, Height - 4 * scale));
                return new RectangleF(88 * scale, (Height - height) / 2, 165 * scale, height);
            }
        }
        internal Rectangle TrackBounds { get { return Rectangle.Round(Track); } }
        public SoundOptionSlider(string label, params string[] choices)
        {
            title = label; options = choices; scale = DisplayQuality.Scale;
            if (choices.Length < 2) throw new ArgumentException("At least two choices are required.");
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            Size = new Size((int)(262 * scale), (int)(42 * scale));
            BackColor = SystemColors.Menu;
            TabStop = true; AccessibleName = label; AccessibleDescription = "拖动滑块选择：" + String.Join("、", choices);
            timer.Tick += (s, e) => { thumb += (selected - thumb) * .35f; if (Math.Abs(thumb - selected) < .01) { thumb = selected; timer.Stop(); } Invalidate(); };
        }
        static GraphicsPath Round(RectangleF r, float radius)
        {
            var path = new GraphicsPath(); float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            TextRenderer.DrawText(g, title, Font, Rectangle.Round(new RectangleF(8 * scale, 0, 76 * scale, Height)), SystemColors.MenuText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            RectangleF track = Track; float part = track.Width / options.Length;
            using (var path = Round(track, 14 * scale))
            using (var fill = new SolidBrush(Color.FromArgb(239, 242, 245)))
            using (var border = new Pen(Color.FromArgb(205, 213, 221)))
            { g.FillPath(fill, path); g.DrawPath(border, path); }
            RectangleF pill = new RectangleF(track.X + thumb * part + 2 * scale, track.Y + 2 * scale, part - 4 * scale, track.Height - 4 * scale);
            using (var path = Round(pill, 11 * scale))
            using (var fill = new SolidBrush(Color.FromArgb(193, 224, 250)))
            using (var border = new Pen(Color.FromArgb(98, 163, 221)))
            { g.FillPath(fill, path); g.DrawPath(border, path); }
            for (int i = 0; i < options.Length; i++)
                TextRenderer.DrawText(g, options[i], Font, Rectangle.Round(new RectangleF(track.X + i * part, track.Y, part, track.Height)),
                    i == selected ? SystemColors.MenuText : Color.FromArgb(92, 103, 115), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (Focused) ControlPaint.DrawFocusRectangle(g, Rectangle.Round(RectangleF.Inflate(track, 2 * scale, 2 * scale)));
        }
        void Pick(float x)
        {
            RectangleF track = Track; float part = track.Width / options.Length;
            thumb = Math.Max(0, Math.Min(options.Length - 1, (x - track.X - part / 2) / part));
            SelectedIndex = (int)Math.Round(thumb); Invalidate();
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Track.Contains(e.Location)) return;
            Focus(); dragging = true; Capture = true; timer.Stop(); Pick(e.X);
        }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging) Pick(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (dragging) { Pick(e.X); dragging = false; Capture = false; timer.Start(); } }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (dragging && !Capture) { dragging = false; timer.Start(); } }
        protected override bool IsInputKey(Keys keyData)
        { Keys key = keyData & Keys.KeyCode; return key == Keys.Left || key == Keys.Right || key == Keys.Home || key == Keys.End || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left) SelectedIndex--;
            else if (e.KeyCode == Keys.Right) SelectedIndex++;
            else if (e.KeyCode == Keys.Home) SelectedIndex = 0;
            else if (e.KeyCode == Keys.End) SelectedIndex = options.Length - 1;
            else return;
            e.Handled = true;
        }
        protected override AccessibleObject CreateAccessibilityInstance() { return new SliderAccessibility(this); }
        sealed class SliderAccessibility : ControlAccessibleObject
        {
            readonly SoundOptionSlider slider;
            public SliderAccessibility(SoundOptionSlider owner) : base(owner) { slider = owner; }
            public override AccessibleRole Role { get { return AccessibleRole.Slider; } }
            public override string Value { get { return slider.options[slider.SelectedIndex]; } set { int i = Array.IndexOf(slider.options, value); if (i >= 0) slider.SelectedIndex = i; } }
        }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }

    // Supply the final aligned location through the native menu placement hook.
    // Moving a visible dropdown from Opened fights its owner's positioning and
    // repaints saved background pixels, especially over accelerated windows.
    public sealed class SoundOptionsMenuItem : ToolStripMenuItem
    {
        public SoundOptionsMenuItem(string text) : base(text) { }

        protected override Point DropDownLocation
        {
            get
            {
                if (Owner == null) return base.DropDownLocation;
                return AdjacentLocation(Owner.Bounds, DropDown.Size, Screen.FromRectangle(Owner.Bounds).WorkingArea);
            }
        }

        internal static Point AdjacentLocation(Rectangle anchor, Size panel, Rectangle work)
        {
            int maxX = Math.Max(work.Left, work.Right - panel.Width);
            int maxY = Math.Max(work.Top, work.Bottom - panel.Height);
            int y = Math.Max(work.Top, Math.Min(maxY, anchor.Top));
            if (anchor.Right + panel.Width <= work.Right) return new Point(anchor.Right, y);
            if (anchor.Left - panel.Width >= work.Left) return new Point(anchor.Left - panel.Width, y);
            int side = work.Right - anchor.Right >= anchor.Left - work.Left ? anchor.Right : anchor.Left - panel.Width;
            return new Point(Math.Max(work.Left, Math.Min(maxX, side)), y);
        }
    }

    // A real cascading menu surface: the parent item owns its lifetime,
    // keyboard navigation, mouse tracking and z-order above the desktop pet.
    public sealed class SoundOptionsPopup : ToolStripDropDown
    {
        readonly Panel content;
        readonly ToolStripControlHost host;
        readonly float scale;
        readonly SoundOptionSlider[] options;
        public SoundOptionSlider Laugh { get; private set; }
        public SoundOptionSlider Rest { get; private set; }
        public SoundOptionSlider Jump { get; private set; }
        public SoundOptionSlider Listen { get; private set; }

        public SoundOptionsPopup(float displayScale, Font menuFont)
        {
            scale = displayScale;
            Font = menuFont;
            Text = "奶蛙音效设置"; AccessibleName = Text;
            AutoSize = false; AutoClose = true; CanOverflow = false;
            Padding = new Padding(1); Margin = Padding.Empty;
            LayoutStyle = ToolStripLayoutStyle.VerticalStackWithOverflow;
            content = new BufferedPanel { Font = menuFont, BackColor = SystemColors.Menu, AccessibleName = Text, AccessibleRole = AccessibleRole.Pane };
            content.Controls.Add(new Label { Text = Text, AutoSize = true, Font = menuFont,
                ForeColor = SystemColors.MenuText, Location = new Point((int)(16 * scale), (int)(9 * scale)) });
            content.Controls.Add(new Label { Text = "拖动滑块，或点击选项切换", AutoSize = true, Font = menuFont,
                ForeColor = SystemColors.GrayText, Location = new Point((int)(16 * scale), (int)(33 * scale)) });
            Laugh = new SoundOptionSlider("捧腹大笑", "关", "开");
            Jump = new SoundOptionSlider("跳一下", "关", "开");
            Rest = new SoundOptionSlider("躺平奶蛙", "关", "男声", "女声");
            Listen = new SoundOptionSlider("沉思奶蛙", "关", "开");
            options = new[] { Laugh, Jump, Rest, Listen };
            foreach (var option in options) { option.Font = menuFont; content.Controls.Add(option); }
            host = new ToolStripControlHost(content) { AutoSize = false, Margin = Padding.Empty, Padding = Padding.Empty };
            Items.Add(host);
            FitHeight((int)Math.Round(210 * scale));
            Closing += (s, e) => {
                // Adjusting a hosted slider is not choosing a leaf menu command.
                if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked && Bounds.Contains(Cursor.Position)) e.Cancel = true;
            };
        }

        public void FitHeight(int height)
        {
            var size = new Size((int)Math.Round(278 * scale) + 2, height);
            if (Size == size && content.Size == new Size(size.Width - 2, size.Height - 2) && host.Size == content.Size) return;
            SuspendLayout(); content.SuspendLayout();
            try
            {
                Size = size;
                content.Size = new Size(Width - 2, Height - 2); host.Size = content.Size;
                int top = (int)Math.Round(52 * scale), bottom = (int)Math.Round(8 * scale);
                int spacing = Math.Max(1, (content.Height - top - bottom) / options.Length), row = 0;
                foreach (var option in options)
                {
                    option.Location = new Point((int)Math.Round(10 * scale), top + row++ * spacing);
                    option.Height = Math.Min((int)Math.Round(42 * scale), spacing);
                }
            }
            finally { content.ResumeLayout(false); ResumeLayout(true); }
        }

        sealed class BufferedPanel : Panel
        {
            public BufferedPanel()
            { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
        }
    }
}
