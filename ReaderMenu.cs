using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ReadAloudDesktopV3 {
    internal sealed class ReaderMenu : IDisposable {
        internal readonly ContextMenuStrip Control;
        readonly BridgeContext bridge;
        readonly ToolStripMenuItem show, hide, playback, stop;
        readonly ToolStripMenuItem small, medium, large, bottom, top, locked, fade, urls, symbols, code;
        readonly ToolStripMenuItem autoHide, hideDelay, hideThree, hideFive;
        internal ReaderMenu(BridgeContext context) {
            bridge = context;
            Control = new ContextMenuStrip { BackColor = Color.FromArgb(38,38,41), ForeColor = Color.FromArgb(239,239,243), Renderer = new ToolStripProfessionalRenderer(new MenuColors()), ShowImageMargin = false, ShowCheckMargin = true };
            show = Add("Show player",delegate { bridge.ShowPlayer(false); });
            Add("Open reading controls",delegate { bridge.ShowPlayer(true); });
            hide = Add("Hide player",delegate { if (bridge.Player != null) bridge.Player.Hide(); });
            Control.Items.Add(new ToolStripSeparator());
            playback = Add("Pause / resume",delegate { bridge.TogglePlayback(); });
            stop = Add("Stop reading",delegate { bridge.Command("stop",null,false); });
            Control.Items.Add(new ToolStripSeparator());
            var size = Add("Player size",null);
            small = Choice(size,"Small","playerSize","small");
            medium = Choice(size,"Medium","playerSize","medium");
            large = Choice(size,"Large","playerSize","large");
            var placement = Add("Move to corner",null);
            bottom = Choice(placement,"Bottom right","placement","bottom-right");
            top = Choice(placement,"Top right","placement","top-right");
            locked = Add("Lock position",delegate { bridge.SetPreference("positionLocked",!bridge.Settings.positionLocked); });
            fade = Add("Fade when idle",delegate { bridge.SetPreference("fadeWhenIdle",!bridge.Settings.fadeWhenIdle); });
            autoHide = Add("Hide pill after inactivity",delegate { bridge.SetPreference("autoHide",!bridge.Settings.autoHide); });
            hideDelay = Add("Hide after",null);
            hideThree = Choice(hideDelay,"3 seconds","autoHideSeconds",3);
            hideFive = Choice(hideDelay,"5 seconds","autoHideSeconds",5);
            var cleanup = Add("Text cleanup",null);
            urls = Toggle(cleanup,"Skip web addresses","cleanupUrls",delegate { return bridge.Settings.cleanupUrls; });
            symbols = Toggle(cleanup,"Skip repeated symbols","cleanupSymbols",delegate { return bridge.Settings.cleanupSymbols; });
            code = Toggle(cleanup,"Skip fenced code blocks","cleanupCode",delegate { return bridge.Settings.cleanupCode; });
            cleanup.DropDownItems.Add(new ToolStripSeparator());
            cleanup.DropDownItems.Add(new ToolStripMenuItem("Applies to new selections") { Enabled = false });
            Control.Items.Add(new ToolStripSeparator());
            Add(bridge.Preview ? "Close preview" : "Disable V3",delegate { if (bridge.Preview) bridge.ExitThread(); else bridge.Command("disable",null,false); });
            Control.Opening += delegate { Refresh(); if (bridge.Player != null) bridge.Player.Reveal(); };
            foreach (ToolStripItem item in Control.Items) {
                var menu = item as ToolStripMenuItem;
                if (menu != null && menu.HasDropDownItems) {
                    menu.DropDown.BackColor = Control.BackColor; menu.DropDown.ForeColor = Control.ForeColor;
                    menu.DropDown.Renderer = Control.Renderer;
                    var dropdown = menu.DropDown as ToolStripDropDownMenu;
                    if (dropdown != null) { dropdown.ShowImageMargin = false; dropdown.ShowCheckMargin = true; }
                }
            }
        }
        ToolStripMenuItem Add(string label,EventHandler action) {
            var item = new ToolStripMenuItem(label,null,action); Control.Items.Add(item); return item;
        }
        ToolStripMenuItem Choice(ToolStripMenuItem parent,string label,string name,object value) {
            var item = new ToolStripMenuItem(label,null,delegate { bridge.SetPreference(name,value); }); parent.DropDownItems.Add(item); return item;
        }
        ToolStripMenuItem Toggle(ToolStripMenuItem parent,string label,string name,Func<bool> current) {
            var item = new ToolStripMenuItem(label,null,delegate { bridge.SetPreference(name,!current()); }); parent.DropDownItems.Add(item); return item;
        }
        internal void Refresh() {
            Settings s = bridge.Settings;
            show.Enabled = bridge.Player == null || !bridge.Player.Visible;
            hide.Enabled = !show.Enabled;
            playback.Enabled = stop.Enabled = bridge.Passage.Starts.Count > 0;
            playback.Text = bridge.Playback == "playing" ? "Pause reading" : bridge.Playback == "paused" ? "Resume reading" : "Start reading again";
            small.Checked = s.playerSize == "small"; medium.Checked = s.playerSize == "medium"; large.Checked = s.playerSize == "large";
            bottom.Checked = s.customPosition == null && s.placement == "bottom-right"; top.Checked = s.customPosition == null && s.placement == "top-right";
            locked.Checked = s.positionLocked; fade.Checked = s.fadeWhenIdle;
            autoHide.Checked = s.autoHide; hideDelay.Enabled = s.autoHide;
            fade.Enabled = !s.autoHide;
            hideThree.Checked = s.autoHideSeconds == 3; hideFive.Checked = s.autoHideSeconds == 5;
            urls.Checked = s.cleanupUrls; symbols.Checked = s.cleanupSymbols; code.Checked = s.cleanupCode;
        }
        public void Dispose() { Control.Dispose(); }
        sealed class MenuColors : ProfessionalColorTable {
            public override Color MenuItemSelected { get { return Color.FromArgb(57,52,88); } }
            public override Color MenuItemBorder { get { return Color.FromArgb(104,100,255); } }
            public override Color MenuBorder { get { return Color.FromArgb(66,66,71); } }
            public override Color ToolStripDropDownBackground { get { return Color.FromArgb(38,38,41); } }
            public override Color ImageMarginGradientBegin { get { return ToolStripDropDownBackground; } }
            public override Color ImageMarginGradientMiddle { get { return ToolStripDropDownBackground; } }
            public override Color ImageMarginGradientEnd { get { return ToolStripDropDownBackground; } }
            public override Color CheckBackground { get { return Color.FromArgb(57,52,88); } }
            public override Color CheckSelectedBackground { get { return CheckBackground; } }
            public override Color CheckPressedBackground { get { return CheckBackground; } }
        }
    }
    internal static class ReaderIcon {
        internal static Icon Create() {
            using (var bitmap = new Bitmap(32,32)) using (Graphics g = Graphics.FromImage(bitmap)) {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
                using (var fill = new SolidBrush(Color.FromArgb(104,100,255)))
                using (var outline = PlayerForm.Rounded(new RectangleF(1,1,30,30),7)) g.FillPath(fill,outline);
                using (var white = new SolidBrush(Color.White)) {
                    g.FillRectangle(white,7,12,4,8);
                    g.FillPolygon(white,new[] {new Point(10,12),new Point(17,7),new Point(17,25),new Point(10,20)});
                }
                using (var pen = new Pen(Color.White,2)) { g.DrawArc(pen,16,10,8,12,-65,130); g.DrawArc(pen,16,6,13,20,-60,120); }
                IntPtr handle = bitmap.GetHicon();
                try { using (Icon borrowed = Icon.FromHandle(handle)) return (Icon)borrowed.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    }
}
