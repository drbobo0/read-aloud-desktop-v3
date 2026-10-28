using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ReadAloudDesktopV3 {
    internal sealed class PlayerForm : Form {
        internal const int CompactWidth = 112, CompactHeight = 28;
        readonly BridgeContext bridge;
        readonly RichTextBox passage = new RichTextBox();
        readonly Label status = new Label(), sentence = new Label(), shortcuts = new Label();
        readonly Button pause, previous, next, restart;
        readonly Panel body;
        readonly CompactBar pill;
        readonly Button position;
        readonly Timer fadeTimer = new Timer { Interval = 40 };
        DateTime lastActivity = DateTime.UtcNow;
        string lastPlayback = "stopped";
        readonly ToolTip tips = new ToolTip();
        readonly Color canvas = Color.FromArgb(28,28,31), surface = Color.FromArgb(38,38,41);
        readonly Color ink = Color.FromArgb(239,239,243), muted = Color.FromArgb(168,168,178), accent = Color.FromArgb(104,100,255);
        bool draggingWindow;
        bool dpiApplied, applyingDpi;
        float renderScale = 1;
        readonly List<LayoutSpec> design = new List<LayoutSpec>();
        List<Font> dpiFonts = new List<Font>();
        internal bool Expanded { get; private set; }
        internal Button CompactPlayback { get { return pill.PlayPause; } }
        internal int RenderDpi { get { return (int)Math.Round(renderScale*96); } }
        internal int Pixels(int value) { return (int)Math.Round(value*renderScale,MidpointRounding.AwayFromZero); }
        float SizeFactor { get { return bridge.Settings.playerSize == "large" ? 1.5f : bridge.Settings.playerSize == "medium" ? 1.25f : 1f; } }
        internal int CompactPixels(int value) { return (int)Math.Round(value*renderScale*SizeFactor,MidpointRounding.AwayFromZero); }
        internal Size CompactPixelSize { get { return new Size(CompactPixels(CompactWidth),CompactPixels(CompactHeight)); } }
        internal PlayerForm(BridgeContext context) {
            bridge = context; Text = "Read Aloud Desktop V3";
            Icon = ReaderIcon.Create();
            Font = new Font("Segoe UI", 9); AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = bridge.Preview;
            MaximizeBox = false; MinimizeBox = false;
            IntPtr dpiWindow = Handle;
            TopMost = true; KeyPreview = true; BackColor = canvas; ForeColor = ink;
            StartPosition = FormStartPosition.Manual; DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            pill = new CompactBar { Dock = DockStyle.Bottom, Height = CompactHeight, BackColor = canvas, ForeColor = ink };
            pill.OptionsButton.Click += delegate { SetExpanded(!Expanded); };
            pill.Caption.MouseDown += DragSurfaceMouseDown;
            pill.MouseDown += DragSurfaceMouseDown;
            pill.PlayPause.Click += delegate { TogglePause(); };
            pill.CloseButton.Click += delegate { HideOrClose(); };
            Controls.Add(pill);
            body = new Panel { Dock = DockStyle.Fill, BackColor = canvas, Padding = new Padding(16,14,16,2) };
            Controls.Add(body); body.BringToFront();
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, BackColor = canvas };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            foreach (float height in new[] {28f,39f,34f,36f,20f}) layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
            body.Controls.Add(layout);
            var heading = new Label { Text = "Reading controls", Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold",12), ForeColor = ink, Cursor = Cursors.SizeAll };
            heading.MouseDown += DragSurfaceMouseDown; layout.Controls.Add(heading,0,0);
            status.Dock = DockStyle.Fill; status.Text = "Select text in a desktop app to begin."; status.ForeColor = muted; status.AutoEllipsis = true;
            status.AccessibleName = "Reader status"; layout.Controls.Add(status,0,1);
            passage.Dock = DockStyle.Fill; passage.ReadOnly = true; passage.DetectUrls = false;
            passage.BorderStyle = BorderStyle.None; passage.Font = new Font("Segoe UI",10);
            passage.ScrollBars = RichTextBoxScrollBars.None;
            passage.BackColor = surface; passage.ForeColor = ink; passage.HideSelection = false; passage.AccessibleName = "Selected passage";
            passage.MouseDoubleClick += delegate(object sender,MouseEventArgs e) { bridge.Passage.Choose(passage.GetCharIndexFromPosition(e.Location)); bridge.Jump(bridge.Passage.Index); };
            layout.Controls.Add(passage,0,2);
            sentence.Dock = DockStyle.Fill; sentence.TextAlign = ContentAlignment.MiddleLeft; sentence.ForeColor = muted; sentence.Font = new Font("Segoe UI",8);
            layout.Controls.Add(sentence,0,3);
            var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = canvas };
            previous = MakeButton("Previous",delegate { bridge.Jump(bridge.Passage.Index-1); });
            pause = MakeButton("Pause",delegate { TogglePause(); }); pause.BackColor = accent; pause.ForeColor = Color.White;
            pause.FlatAppearance.MouseOverBackColor = Color.FromArgb(127,123,255); pause.FlatAppearance.MouseDownBackColor = Color.FromArgb(85,81,226);
            var stop = MakeButton("Stop",delegate { bridge.Command("stop",null,false); });
            next = MakeButton("Next",delegate { bridge.Jump(bridge.Passage.Index+1); }); restart = MakeButton("Restart",delegate { bridge.Jump(0); });
            Button[] playbackButtons = { previous,pause,stop,next,restart };
            int[] playbackWidths = { 65,73,50,50,62 };
            for (int i = 0;i < playbackButtons.Length;i++) { playbackButtons[i].AutoSize = false; playbackButtons[i].MinimumSize = new Size(0,29); playbackButtons[i].Width = playbackWidths[i]; }
            controls.Controls.AddRange(playbackButtons); layout.Controls.Add(controls,0,4);
            var secondary = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = canvas };
            secondary.Controls.Add(MakeButton("Voice / speed…",delegate { bridge.Command("settings",null,false); }));
            position = MakeButton("Preferences",delegate { Reveal(); bridge.Menu.Control.Show(position,new Point(0,position.Height)); });
            secondary.Controls.Add(position);
            secondary.Controls.Add(MakeButton("Hide pill",delegate { HideOrClose(); })); layout.Controls.Add(secondary,0,5);
            shortcuts.Dock = DockStyle.Fill; shortcuts.Font = new Font("Segoe UI",8); shortcuts.ForeColor = muted; layout.Controls.Add(shortcuts,0,6);
            layout.Controls.Add(new Label { Text = "Highlight marks your chosen start, not live speech.", Dock = DockStyle.Fill, Font = new Font("Segoe UI",8), ForeColor = muted },0,7);
            pill.ContextMenuStrip = bridge.Menu.Control;
            tips.SetToolTip(pill.PlayPause,"Pause or resume reading without opening the controls.");
            tips.SetToolTip(pill.OptionsButton,"Expand or collapse reading controls.");
            tips.SetToolTip(pill.CloseButton,"Hide the compact player.");
            tips.SetToolTip(pill.Caption,"Drag to move the player. Your position is remembered.");
            tips.SetToolTip(passage,"Scroll with your mouse wheel. Double-click a sentence to read from there.");
            KeyDown += delegate(object sender,KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { SetExpanded(false); e.Handled = true; } };
            FormClosing += delegate(object sender,FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing && !bridge.Preview) { e.Cancel = true; SetExpanded(false); } };
            CaptureDesign(this);
            AttachActivity(this);
            ApplyDpiLayout();
            SetPassage(); SetShortcuts(bridge.Settings); SetExpanded(false);
            DpiChanged += delegate {
                if (!draggingWindow && !IsDisposed) BeginInvoke((Action)delegate { if (!IsDisposed) SetExpanded(Expanded); });
            };
            fadeTimer.Tick += delegate { StepFade(DateTime.UtcNow, Bounds.Contains(Cursor.Position)); };
            fadeTimer.Start();
        }
        void HideOrClose() { Hide(); }
        void TogglePause() { Reveal(); bridge.TogglePlayback(); }
        void AttachActivity(Control control) {
            control.MouseEnter += delegate { Reveal(); };
            control.MouseDown += delegate { Reveal(); };
            control.KeyDown += delegate { Reveal(); };
            foreach (Control child in control.Controls) AttachActivity(child);
        }
        internal void Reveal() { lastActivity = DateTime.UtcNow; if (Opacity != 1) Opacity = 1; }
        internal void StepFade(DateTime now,bool hovered) {
            if (IsDisposed) return;
            bool awake = Expanded || draggingWindow || hovered || bridge.Menu.Control.Visible;
            if (awake) { lastActivity = now; if (Opacity != 1) Opacity = 1; return; }
            if (!Visible) return;
            double idleSeconds = (now-lastActivity).TotalSeconds;
            if (bridge.Settings.autoHide && idleSeconds >= bridge.Settings.autoHideSeconds) { Hide(); return; }
            if (!bridge.Settings.fadeWhenIdle) { if (Opacity != 1) Opacity = 1; return; }
            double target = idleSeconds >= 5 ? .45 : 1;
            if (Math.Abs(Opacity-target) > .01) Opacity = Math.Max(target,Opacity-.08);
        }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) Reveal(); }
        protected override void Dispose(bool disposing) {
            if (disposing) { fadeTimer.Stop(); fadeTimer.Dispose(); tips.Dispose(); }
            base.Dispose(disposing);
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void OnShown(EventArgs e) { base.OnShown(e); SetExpanded(Expanded); }
        sealed class LayoutSpec {
            internal Control Control; internal Rectangle Bounds; internal Padding Padding, Margin; internal Size Minimum;
            internal string FontName; internal float Points; internal FontStyle Style;
            internal readonly List<float> Rows = new List<float>(), Columns = new List<float>();
        }
        void CaptureDesign(Control control) {
            var item = new LayoutSpec { Control = control, Bounds = control.Bounds, Padding = control.Padding, Margin = control.Margin, Minimum = control.MinimumSize, FontName = control.Font.Name, Points = control.Font.SizeInPoints, Style = control.Font.Style };
            TableLayoutPanel table = control as TableLayoutPanel;
            if (table != null) {
                foreach (RowStyle row in table.RowStyles) item.Rows.Add(row.SizeType == SizeType.Absolute ? row.Height : -1);
                foreach (ColumnStyle column in table.ColumnStyles) item.Columns.Add(column.SizeType == SizeType.Absolute ? column.Width : -1);
            }
            design.Add(item); foreach (Control child in control.Controls) CaptureDesign(child);
        }
        Padding ScaledPadding(Padding p) { return new Padding(Pixels(p.Left),Pixels(p.Top),Pixels(p.Right),Pixels(p.Bottom)); }
        void ApplyDpiLayout() {
            if (applyingDpi || design.Count == 0) return;
            uint dpi = GetDpiForWindow(Handle); if (dpi == 0) dpi = 96;
            float scale = dpi/96f;
            if (dpiApplied && Math.Abs(scale-renderScale) < .001 && Math.Abs(pill.RenderScale-scale*SizeFactor) < .001) return;
            applyingDpi = true; SuspendLayout(); renderScale = scale;
            List<Font> oldFonts = dpiFonts; dpiFonts = new List<Font>();
            pill.RenderScale = scale*SizeFactor;
            pill.PlayPause.RenderScale = pill.RenderScale; pill.OptionsButton.RenderScale = pill.RenderScale; pill.CloseButton.RenderScale = pill.RenderScale;
            foreach (LayoutSpec item in design) {
                Control c = item.Control;
                c.SuspendLayout();
                var font = new Font(item.FontName,item.Points*96f/72f*scale,item.Style,GraphicsUnit.Pixel);
                dpiFonts.Add(font); c.Font = font;
                c.Padding = ScaledPadding(item.Padding); c.Margin = ScaledPadding(item.Margin);
                c.MinimumSize = new Size(Pixels(item.Minimum.Width),Pixels(item.Minimum.Height));
                if (c != this) c.Bounds = new Rectangle(Pixels(item.Bounds.X),Pixels(item.Bounds.Y),Pixels(item.Bounds.Width),Pixels(item.Bounds.Height));
                TableLayoutPanel table = c as TableLayoutPanel;
                if (table != null) {
                    for (int i = 0;i < item.Rows.Count;i++) if (item.Rows[i] >= 0) table.RowStyles[i].Height = (float)Math.Round(item.Rows[i]*scale);
                    for (int i = 0;i < item.Columns.Count;i++) if (item.Columns[i] >= 0) table.ColumnStyles[i].Width = (float)Math.Round(item.Columns[i]*scale);
                }
                DarkButton button = c as DarkButton; if (button != null) button.RenderScale = scale;
                c.ResumeLayout(false);
            }
            var menuFont = new Font("Segoe UI",12*scale,FontStyle.Regular,GraphicsUnit.Pixel); dpiFonts.Add(menuFont);
            pill.ContextMenuStrip.Font = menuFont;
            var captionFont = new Font("Segoe UI",8.5f*96f/72f*pill.RenderScale,FontStyle.Regular,GraphicsUnit.Pixel);
            dpiFonts.Add(captionFont); pill.Caption.Font = captionFont;
            dpiApplied = true; applyingDpi = false; ResumeLayout(true);
            foreach (Font old in oldFonts) old.Dispose();
        }
        protected override CreateParams CreateParams {
            get { CreateParams result = base.CreateParams; result.Style &= ~0x00c40000; return result; }
        }
        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            // Let the compositor round the window; do not reintroduce a clipped GDI region.
            try {
                int enabled = 1, renderingEnabled = 2, roundSmall = 3, borderColor = 66 | (66 << 8) | (71 << 16);
                DwmSetWindowAttribute(Handle,20,ref enabled,4);
                DwmSetWindowAttribute(Handle,2,ref renderingEnabled,4);
                DwmSetWindowAttribute(Handle,33,ref roundSmall,4);
                DwmSetWindowAttribute(Handle,34,ref borderColor,4);
            } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
        }
        void DragSurfaceMouseDown(object sender,MouseEventArgs e) {
            if (e.Button != MouseButtons.Left || bridge.Settings.positionLocked) return;
            Reveal();
            Point before = Location;
            draggingWindow = true;
            try { ReleaseCapture(); SendMessage(Handle,0x00a1,new IntPtr(2),IntPtr.Zero); }
            finally { draggingWindow = false; }
            ApplyDpiLayout();
            Size compact = CompactPixelSize;
            if (Location != before) MovePlayerTo(new Point(Right-compact.Width,pill.Dock == DockStyle.Top ? Top : Bottom-compact.Height));
        }
        internal void MovePlayerTo(Point screenPosition) {
            if (bridge.Settings.positionLocked) return;
            ApplyDpiLayout();
            Size compact = CompactPixelSize;
            Rectangle work = Screen.FromRectangle(new Rectangle(screenPosition,compact)).WorkingArea;
            Point clamped = ClampAnchor(screenPosition,work,compact);
            bridge.Settings.customPosition = new PlayerPosition { x = clamped.X, y = clamped.Y, physical = true };
            SetExpanded(Expanded);
            if (!bridge.Preview) Program.Send(new { type = "position", x = clamped.X, y = clamped.Y, physical = true });
        }
        static Point ClampAnchor(Point anchor,Rectangle work,Size compact) {
            return new Point(Math.Max(work.Left,Math.Min(work.Right-compact.Width,anchor.X)),Math.Max(work.Top,Math.Min(work.Bottom-compact.Height,anchor.Y)));
        }
        internal void SetExpanded(bool expanded) {
            ApplyDpiLayout();
            Expanded = expanded; SuspendLayout(); body.Visible = expanded; pill.Expanded = expanded;
            Reveal();
            Size compact = CompactPixelSize; pill.Height = compact.Height;
            PlayerPosition saved = bridge.Settings.customPosition;
            Rectangle work = saved == null ? Screen.PrimaryScreen.WorkingArea : Screen.FromRectangle(new Rectangle(saved.x,saved.y,compact.Width,compact.Height)).WorkingArea;
            bool atTop = bridge.Settings.placement == "top-right";
            Point anchor = new Point(work.Right-compact.Width-Pixels(12),atTop ? work.Top+Pixels(12) : work.Bottom-compact.Height-Pixels(12));
            if (saved != null) {
                anchor = ClampAnchor(new Point(saved.x,saved.y),work,compact);
                atTop = work.Bottom-anchor.Y-compact.Height >= anchor.Y-work.Top;
            }
            pill.Dock = atTop ? DockStyle.Top : DockStyle.Bottom;
            int availableHeight = atTop ? work.Bottom-anchor.Y : anchor.Y+compact.Height-work.Top;
            ClientSize = expanded ? new Size(Math.Min(Pixels(390),work.Width),Math.Min(Pixels(396),availableHeight)) : compact;
            Location = new Point(Math.Max(work.Left,Math.Min(work.Right-Width,anchor.X+compact.Width-Width)),atTop ? anchor.Y : anchor.Y+compact.Height-Height);
            ResumeLayout(true); UpdateShape(); pill.Invalidate(); Invalidate();
        }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); UpdateShape(); }
        void UpdateShape() {
            // Avoid a clipped native window region: it creates ragged/light fringes when composed.
            Region old = Region; if (old != null) { Region = null; old.Dispose(); }
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Color.FromArgb(66,66,71)))
            using (var outline = Rounded(new RectangleF(.5f,.5f,Width-1,Height-1),Pixels(4))) e.Graphics.DrawPath(pen,outline);
        }
        internal static GraphicsPath Rounded(RectangleF box,float radius) {
            float d = radius*2; var p = new GraphicsPath();
            p.AddArc(box.X,box.Y,d,d,180,90); p.AddArc(box.Right-d,box.Y,d,d,270,90);
            p.AddArc(box.Right-d,box.Bottom-d,d,d,0,90); p.AddArc(box.X,box.Bottom-d,d,d,90,90); p.CloseFigure(); return p;
        }
        Button MakeButton(string text,EventHandler click) {
            int width;
            using (var measureFont = new Font("Segoe UI",12,FontStyle.Regular,GraphicsUnit.Pixel)) width = Math.Max(50,TextRenderer.MeasureText(text,measureFont).Width+14);
            var b = new DarkButton { Text = text, AutoSize = false, Width = width, Height = 30, MinimumSize = new Size(50,29), Padding = new Padding(4,0,4,0), FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, BackColor = surface, ForeColor = ink, Margin = new Padding(0,2,5,2), Cursor = Cursors.Hand, Font = new Font("Segoe UI",9) };
            b.FlatAppearance.BorderSize = 0; b.FlatAppearance.MouseOverBackColor = Color.FromArgb(52,52,56); b.FlatAppearance.MouseDownBackColor = Color.FromArgb(64,64,69); b.Click += click; return b;
        }
        internal void SetPassage() { if (passage.Text != bridge.Passage.Text) passage.Text = bridge.Passage.Text; HighlightStart(); }
        internal void HighlightStart() {
            passage.SelectAll(); passage.SelectionBackColor = surface; passage.SelectionColor = ink;
            if (bridge.Passage.Starts.Count > 0) { passage.Select(bridge.Passage.Start,bridge.Passage.Length); passage.SelectionBackColor = Color.FromArgb(57,52,88); passage.Select(bridge.Passage.Start,0); passage.ScrollToCaret(); } else passage.Select(0,0);
            sentence.Text = bridge.Passage.Starts.Count == 0 ? "Select text in a desktop app to begin." : "Start "+(bridge.Passage.Index+1)+" / "+bridge.Passage.Starts.Count+"  ·  Double-click a sentence to jump";
            previous.Enabled = bridge.Passage.Starts.Count > 0 && bridge.Passage.Index > 0; next.Enabled = bridge.Passage.Index+1 < bridge.Passage.Starts.Count;
            restart.Enabled = bridge.Passage.Starts.Count > 0; pause.Enabled = bridge.Passage.Starts.Count > 0;
            pill.PlayPause.Enabled = bridge.Passage.Starts.Count > 0;
        }
        internal void SetStatus(string message,string playback) {
            if (playback != lastPlayback) Reveal(); lastPlayback = playback;
            status.Text = message; pause.Text = playback == "paused" ? "Resume" : playback == "stopped" ? "Play again" : "Pause"; pill.SetPlayback(playback);
        }
        internal void SetShortcuts(Settings settings) {
            if (settings.customPosition != null && !settings.customPosition.physical) {
                // Older versions saved DPI-virtualized coordinates; migrate them once.
                MovePlayerTo(new Point(Pixels(settings.customPosition.x),Pixels(settings.customPosition.y)));
            }
            shortcuts.Text = "Read  "+settings.read.label+"    ·    Pause  "+settings.pause.label+"\nStop  "+settings.stop.label+"    ·    Controls  "+settings.player.label;
            pill.Caption.Cursor = settings.positionLocked ? Cursors.Default : Cursors.SizeAll;
            tips.SetToolTip(pill.Caption,settings.positionLocked ? "Position locked. Unlock it in Preferences or the tray menu." : "Drag to move the player. Your position is remembered.");
            SetExpanded(Expanded);
        }
        sealed class DarkButton : Button {
            internal float RenderScale = 1;
            bool hover, pressed;
            internal DarkButton() {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer,true);
                MouseEnter += delegate { hover = true; Invalidate(); }; MouseLeave += delegate { hover = false; pressed = false; Invalidate(); };
                MouseDown += delegate { pressed = true; Invalidate(); }; MouseUp += delegate { pressed = false; Invalidate(); };
            }
            protected override void OnPaint(PaintEventArgs e) {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Color fill = Enabled && pressed ? FlatAppearance.MouseDownBackColor : Enabled && hover ? FlatAppearance.MouseOverBackColor : BackColor;
                e.Graphics.Clear(Parent == null ? Color.FromArgb(28,28,31) : Parent.BackColor);
                float radius = 5*RenderScale;
                using (var brush = new SolidBrush(fill)) using (var path = Rounded(new RectangleF(0,0,Width,Height),radius)) e.Graphics.FillPath(brush,path);
                TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,Enabled ? ForeColor : Color.FromArgb(125,125,137),TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                if (Focused && ShowFocusCues) using (var pen = new Pen(Color.FromArgb(130,125,255))) using (var path = Rounded(new RectangleF(1,1,Width-3,Height-3),radius)) e.Graphics.DrawPath(pen,path);
            }
        }
        sealed class DarkMenuColors : ProfessionalColorTable {
            public override Color MenuItemSelected { get { return Color.FromArgb(52,52,56); } }
            public override Color MenuBorder { get { return Color.FromArgb(66,66,71); } }
            public override Color MenuItemBorder { get { return MenuBorder; } }
            public override Color ToolStripDropDownBackground { get { return Color.FromArgb(38,38,41); } }
            public override Color ImageMarginGradientBegin { get { return ToolStripDropDownBackground; } }
            public override Color ImageMarginGradientMiddle { get { return ToolStripDropDownBackground; } }
            public override Color ImageMarginGradientEnd { get { return ToolStripDropDownBackground; } }
        }
        sealed class CompactBar : Panel {
            internal float RenderScale = 1;
            internal bool Expanded;
            internal readonly IconButton PlayPause = new IconButton("pause");
            internal readonly IconButton OptionsButton = new IconButton("more");
            internal readonly IconButton CloseButton = new IconButton("close");
            internal readonly Label Caption = new Label { Text = "Read", TextAlign = ContentAlignment.MiddleLeft, Cursor = Cursors.SizeAll, ForeColor = Color.FromArgb(239,239,243), Font = new Font("Segoe UI",8.5f), AccessibleName = "Drag to move the player" };
            internal CompactBar() {
                DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw,true);
                Controls.AddRange(new Control[] {PlayPause,Caption,OptionsButton,CloseButton});
                PlayPause.AccessibleName = "Pause reading"; OptionsButton.AccessibleName = "Expand or collapse controls"; CloseButton.AccessibleName = "Hide player";
            }
            protected override void OnLayout(LayoutEventArgs e) {
                base.OnLayout(e);
                PlayPause.SetBounds(P(3),P(3),P(22),P(22)); CloseButton.SetBounds(Width-P(24),P(3),P(20),P(22)); OptionsButton.SetBounds(Width-P(46),P(3),P(20),P(22));
                Caption.SetBounds(P(30),P(2),Math.Max(0,Width-P(78)),P(24));
            }
            int P(int value) { return (int)Math.Round(value*RenderScale,MidpointRounding.AwayFromZero); }
            internal void SetPlayback(string state) {
                PlayPause.Glyph = state == "playing" ? "pause" : "play";
                PlayPause.AccessibleName = state == "playing" ? "Pause reading" : state == "paused" ? "Resume reading" : "Start reading again";
                Caption.Text = "Read";
                PlayPause.RenderScale = RenderScale; OptionsButton.RenderScale = RenderScale; CloseButton.RenderScale = RenderScale;
                PlayPause.Invalidate();
            }
            protected override void OnPaint(PaintEventArgs e) {
                base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var border = new Pen(Color.FromArgb(66,66,71))) {
                    if (Expanded) e.Graphics.DrawLine(border,P(10),Dock == DockStyle.Top ? Height-1 : 0,Width-P(10),Dock == DockStyle.Top ? Height-1 : 0);
                    else using (var outline = Rounded(new RectangleF(.5f,.5f,Width-1,Height-1),P(4))) e.Graphics.DrawPath(border,outline);
                }
            }
        }
        sealed class IconButton : Button {
            internal float RenderScale = 1;
            internal string Glyph; bool hover;
            internal IconButton(string glyph) {
                Glyph = glyph; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; TabStop = true; Cursor = Cursors.Hand;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer,true);
                MouseEnter += delegate { hover = true; Invalidate(); }; MouseLeave += delegate { hover = false; Invalidate(); };
            }
            protected override void OnPaint(PaintEventArgs e) {
                Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
                float scale = RenderScale, logicalWidth = Width/scale, logicalHeight = Height/scale;
                g.ScaleTransform(scale,scale);
                Color background = Parent == null ? Color.FromArgb(28,28,31) : Parent.BackColor; g.Clear(background);
                bool playback = Glyph == "play" || Glyph == "pause";
                Color fill = playback ? Enabled ? hover ? Color.FromArgb(127,123,255) : Color.FromArgb(104,100,255) : Color.FromArgb(66,64,91) : hover ? Color.FromArgb(52,52,56) : background;
                using (var brush = new SolidBrush(fill)) using (var shape = Rounded(new RectangleF(0,0,logicalWidth,logicalHeight),5)) g.FillPath(brush,shape);
                Color foreground = playback ? Enabled ? Color.White : Color.FromArgb(150,148,170) : Color.FromArgb(166,166,177);
                using (var brush = new SolidBrush(foreground)) using (var pen = new Pen(foreground,1.6f)) {
                    float x = logicalWidth/2f,y = logicalHeight/2f;
                    if (Glyph == "pause") { g.FillRectangle(brush,x-4,y-5,2.5f,10); g.FillRectangle(brush,x+1.5f,y-5,2.5f,10); }
                    else if (Glyph == "play") g.FillPolygon(brush,new[] {new PointF(x-3,y-5),new PointF(x-3,y+5),new PointF(x+5,y)});
                    else if (Glyph == "more") for (int i = -1;i <= 1;i++) g.FillEllipse(brush,x-1.2f,y+i*4-1.2f,2.4f,2.4f);
                    else { g.DrawLine(pen,x-4,y-4,x+4,y+4); g.DrawLine(pen,x+4,y-4,x-4,y+4); }
                }
                if (Focused && ShowFocusCues) using (var pen = new Pen(Color.FromArgb(151,147,255),1/scale)) using (var shape = Rounded(new RectangleF(1,1,logicalWidth-3,logicalHeight-3),5)) g.DrawPath(pen,shape);
            }
        }
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    }
}
