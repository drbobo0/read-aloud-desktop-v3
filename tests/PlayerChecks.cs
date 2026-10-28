using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ReadAloudDesktopV3 {
    internal static class PlayerChecks {
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        static void Click(Button button) { typeof(Button).GetMethod("OnClick",BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button,new object[] { EventArgs.Empty }); }
        [STAThread] static int Main() {
            try {
                Application.EnableVisualStyles();
                Require(AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(),new IntPtr(-4)), "The executable must be PerMonitorV2 DPI aware.");
                var context = new BridgeContext(true, false);
                Require(context.Settings.autoHide && context.Settings.autoHideSeconds == 5,"Automatic hiding must default to five seconds.");
                using (var form = new PlayerForm(context)) {
                    context.Player = form;
                    Require(!form.Visible && !form.Expanded, "New player must remain hidden and collapsed.");
                    Require(form.ClientSize == form.CompactPixelSize, "Player must keep its reference size at the display DPI.");
                    Require(form.Font.Unit == GraphicsUnit.Pixel && Math.Abs(form.Font.Size-12*form.RenderDpi/96f) < .05, "Text must use native-resolution pixel fonts.");
                    form.Show(); Application.DoEvents();
                    Require(form.ClientSize == form.CompactPixelSize, "Showing the window must not trigger another scaling pass.");
                    Require(form.CompactPlayback.Width == form.Pixels(22), "The outside button must be rendered at the window DPI.");
                    context.PlaybackSessionId = "current";
                    context.FinishReading("previous");
                    Require(form.Visible,"An old reading must not hide the current pill.");
                    form.SetStatus("Paused.","paused");
                    Require(form.Visible,"Pausing must keep the pill visible.");
                    context.FinishReading("current");
                    Require(!form.Visible && context.Playback == "stopped","Confirmed completion must hide the pill without exiting the helper.");
                    form.Show(); context.PlaybackSessionId = "current";
                    context.Preview = true;
                    context.Command("play","A new selection.",true);
                    context.Preview = false;
                    context.FinishReading("current");
                    Require(form.Visible,"Beginning a new reading must invalidate the older completion token.");
                    context.LoadPassage("Reading continues after the compact pill disappears.",false);
                    context.Playback = "playing";
                    context.Settings.fadeWhenIdle = false;
                    form.Reveal(); DateTime started = DateTime.UtcNow;
                    form.StepFade(started.AddSeconds(4),false);
                    Require(form.Visible,"The default delay must not hide the player before five seconds.");
                    form.StepFade(started.AddSeconds(6),false);
                    Require(!form.Visible && context.Playback == "playing" && context.Passage.Text.StartsWith("Reading continues"),"Automatic hiding must preserve speech, passage and helper state, even with fading off.");
                    context.Settings.autoHideSeconds = 3; form.Show(); form.Reveal(); started = DateTime.UtcNow;
                    form.StepFade(started.AddSeconds(2),false); Require(form.Visible,"Three-second hiding must wait for the selected delay.");
                    form.StepFade(started.AddSeconds(4),false); Require(!form.Visible,"The three-second option must hide the pill.");
                    context.Settings.autoHideSeconds = 5; form.Show(); form.Reveal(); started = DateTime.UtcNow;
                    form.StepFade(started.AddSeconds(6),true); Require(form.Visible,"Hovering must keep controls available.");
                    form.StepFade(started.AddSeconds(10),false); Require(form.Visible,"The delay must restart after interaction.");
                    form.StepFade(started.AddSeconds(12),false); Require(!form.Visible,"The player must hide after the restarted delay expires.");
                    form.Show(); form.SetExpanded(true); form.StepFade(DateTime.UtcNow.AddMinutes(1),false);
                    Require(form.Visible && form.Expanded,"Expanded controls must remain available.");
                    form.SetExpanded(false); context.Settings.autoHide = false;
                    form.StepFade(DateTime.UtcNow.AddMinutes(1),false); Require(form.Visible,"Turning automatic hiding off must keep the player visible.");
                    context.Settings.fadeWhenIdle = true;
                    form.Reveal();
                    for (int i = 0; i < 12; i++) form.StepFade(DateTime.UtcNow.AddSeconds(6),false);
                    Require(Math.Abs(form.Opacity-.45) < .01, "Idle pill must fade to 45 percent after five seconds.");
                    form.StepFade(DateTime.UtcNow.AddSeconds(7),true);
                    Require(form.Opacity == 1, "Hover must immediately restore full visibility.");
                    context.Settings.fadeWhenIdle = false;
                    for (int i = 0; i < 12; i++) form.StepFade(DateTime.UtcNow.AddSeconds(20),false);
                    Require(form.Opacity == 1, "Disabling fade must keep the pill opaque.");
                    context.Settings.fadeWhenIdle = true;
                    form.Hide();
                    Rectangle work = Screen.PrimaryScreen.WorkingArea;
                    Require(form.Right == work.Right-form.Pixels(12) && form.Bottom == work.Bottom-form.Pixels(12), "Player must default to the bottom-right working area.");
                    Require(form.Region == null, "The player must not use the old clipped native window region.");
                    int cornerPreference;
                    Require(DwmGetWindowAttribute(form.Handle,33,out cornerPreference,4) == 0 && cornerPreference == 3, "Use compositor-provided small rounded corners.");
                    using (var bitmap = new Bitmap(form.Width,form.Height)) {
                        form.DrawToBitmap(bitmap,form.ClientRectangle);
                        foreach (Point p in new[] {new Point(0,0),new Point(form.Width-1,0),new Point(0,form.Height-1),new Point(form.Width-1,form.Height-1)}) {
                            Color color = bitmap.GetPixel(p.X,p.Y);
                            Require(color.R < 170 && color.G < 170 && color.B < 170,"The player corners must be dark.");
                        }
                    }
                    form.SetExpanded(true);
                    Require(form.Expanded && form.Height > 300, "Explicit click must expand the controls.");
                    for (int i = 0; i < 3; i++) {
                        context.LoadPassage("A new selection. Another sentence.", false);
                        Require(!form.Expanded && form.Height == form.Pixels(28), "Every new selection must collapse the controls.");
                        form.SetStatus("Text sent to Read Aloud.", "playing");
                        Require(!form.Expanded, "Playback updates must not open the controls.");
                        form.SetExpanded(true);
                    }
                    form.SetExpanded(false);
                    form.SetStatus("Pause sent to Read Aloud.", "paused");
                    Require(!form.Expanded && form.Bottom == work.Bottom-form.Pixels(12), "Pause must keep the compact bar in place.");
                    Require(form.CompactPlayback.Enabled && form.CompactPlayback.AccessibleName == "Resume reading", "Resume must remain available on the outside.");
                    form.SetStatus("Resume sent.","playing");
                    Require(form.CompactPlayback.AccessibleName == "Pause reading" && !form.Expanded, "Pause must remain outside while collapsed.");
                    context.Preview = true; context.Playback = "playing";
                    Click(form.CompactPlayback);
                    Require(context.Playback == "paused" && !form.Expanded, "Outside pause must work without expanding.");
                    Click(form.CompactPlayback);
                    Require(context.Playback == "playing" && !form.Expanded, "Outside resume must work without expanding.");
                    context.Playback = "stopped"; form.SetStatus("Stopped.","stopped");
                    Click(form.CompactPlayback);
                    Require(context.Playback == "playing" && !form.Expanded, "Outside start must replay without expanding.");
                    context.Preview = false;
                    form.SetStatus("Stop sent.","stopped");
                    Require(form.CompactPlayback.AccessibleName == "Start reading again", "Stopped passages need an outside start button.");
                    context.Settings.placement = "top-right"; form.SetShortcuts(context.Settings);
                    Require(form.Top == work.Top+form.Pixels(12) && form.Right == work.Right-form.Pixels(12), "Top-right placement must apply immediately.");
                    form.SetExpanded(true);
                    Require(form.Top == work.Top+form.Pixels(12) && form.Right == work.Right-form.Pixels(12), "Expanded controls must stay anchored at the top right.");
                    Require(form.CompactPlayback.Parent.Dock == DockStyle.Top, "Top-right controls must expand below the compact bar.");
                    context.LoadPassage("A new passage.",false);
                    Require(!form.Expanded && form.Top == work.Top+form.Pixels(12), "New text must preserve the chosen corner.");
                    context.Settings.placement = "bottom-right"; form.SetShortcuts(context.Settings); form.SetExpanded(true);
                    Require(form.Bottom == work.Bottom-form.Pixels(12) && form.CompactPlayback.Parent.Dock == DockStyle.Bottom, "Bottom-right controls must expand upward.");
                    form.SetExpanded(false); context.Preview = true;
                    Point chosen = new Point(work.Left+100,work.Top+90);
                    form.MovePlayerTo(chosen);
                    Require(form.Location == chosen && context.Settings.customPosition.x == chosen.X, "Dragging must move the compact player to the chosen coordinates.");
                    context.Settings.positionLocked = true;
                    form.MovePlayerTo(new Point(chosen.X+80,chosen.Y+80));
                    Require(form.Location == chosen && context.Settings.customPosition.x == chosen.X,"Lock must reject movement and preserve the saved position.");
                    context.Settings.positionLocked = false;
                    context.LoadPassage("Another selection after dragging.",false);
                    Require(form.Location == chosen, "Reading a new selection must preserve the dragged position.");
                    form.SetExpanded(true);
                    Require(work.Contains(form.Bounds), "Expanded controls must stay inside the monitor.");
                    form.SetExpanded(false);
                    Require(form.Location == chosen, "Collapsing must return to the custom position.");
                    using (var restored = new PlayerForm(context)) Require(restored.Location == chosen, "Restoring saved preferences must restore the dragged position.");
                    form.MovePlayerTo(new Point(100000,100000));
                    bool onScreen = false;
                    foreach (Screen screen in Screen.AllScreens) if (screen.WorkingArea.Contains(form.Bounds)) onScreen = true;
                    Require(onScreen, "Off-screen positions must be clamped to a working monitor.");
                    context.Settings.customPosition = null; form.SetShortcuts(context.Settings);
                    Require(form.Right == work.Right-form.Pixels(12) && form.Bottom == work.Bottom-form.Pixels(12), "Reset to corner must clear the dragged position.");
                    foreach (string size in new[] { "medium", "large", "small" }) {
                        context.Settings.playerSize = size; form.SetShortcuts(context.Settings);
                        Require(form.ClientSize == form.CompactPixelSize && form.CompactPlayback.Width == form.CompactPixels(22),"Changing size must scale icons and window together.");
                        Require(form.Right == work.Right-form.Pixels(12) && form.Bottom == work.Bottom-form.Pixels(12),"Resizing must preserve corner anchoring.");
                        form.SetExpanded(true); form.StepFade(DateTime.UtcNow.AddMinutes(1),false);
                        Require(form.Opacity == 1 && work.Contains(form.Bounds),"Expanded controls must remain fully visible and on screen at every size.");
                        form.SetExpanded(false);
                    }
                    context.Preview = true;
                    context.Menu.Refresh();
                    foreach (ToolStripItem item in context.Menu.Control.Items) {
                        if (item.Text == "Lock position") { item.PerformClick(); Require(context.Settings.positionLocked,"Menu lock must apply immediately."); item.PerformClick(); }
                        if (item.Text == "Fade when idle") { item.PerformClick(); Require(!context.Settings.fadeWhenIdle,"Menu fade switch must apply immediately."); }
                        if (item.Text == "Hide pill after inactivity") { item.PerformClick(); Require(context.Settings.autoHide,"The menu must enable automatic hiding."); context.Menu.Refresh(); }
                        if (item.Text == "Hide after") {
                            foreach (ToolStripItem choice in ((ToolStripMenuItem)item).DropDownItems) {
                                choice.PerformClick();
                                Require(context.Settings.autoHideSeconds == (choice.Text == "3 seconds" ? 3 : 5),"Menu delay choices must apply numeric settings immediately.");
                            }
                        }
                    }
                    Console.WriteLine("PASS: automatic hide defaults/3/5/off, continued speech, hover/expanded controls, native " + form.RenderDpi + " DPI, sizes, fading, lock and menu controls");
                    Require(!form.Visible, "The check must not display a window.");
                }
                context.Dispose();
                using (var trayContext = new BridgeContext(false,false)) {
                    Require(trayContext.Tray != null && trayContext.Tray.Visible && trayContext.Tray.Icon != null,"A connected helper must expose a Windows tray icon.");
                    Require(trayContext.Tray.ContextMenuStrip == trayContext.Menu.Control,"Tray and player must share current preferences and playback controls.");
                    trayContext.Dispose();
                    Require(!trayContext.Tray.Visible,"Disabling or closing the helper must remove the tray icon.");
                }
                Console.WriteLine("PASS: reference-size bar, both corners, outside play/pause/resume, free placement, position restore, monitor clamping and corner reset");
                return 0;
            } catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
        }
        [DllImport("user32.dll")] static extern IntPtr GetThreadDpiAwarenessContext();
        [DllImport("user32.dll")] static extern bool AreDpiAwarenessContextsEqual(IntPtr first,IntPtr second);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int size);
    }
}
