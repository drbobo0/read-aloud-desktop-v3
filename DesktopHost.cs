using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ReadAloudDesktopV3 {
    internal static class Program {
        static readonly Stream Output = Console.OpenStandardOutput();
        static readonly object OutputLock = new object();
        static BridgeContext Context;
        internal static bool ProtocolTest;
        internal static void RunPreview() { Main(new[] { "--preview" }); }
        [STAThread] static void Main(string[] args) {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ProtocolTest = Array.IndexOf(args, "--protocol-test") >= 0;
            bool preview = Array.IndexOf(args, "--preview") >= 0;
            Context = new BridgeContext(ProtocolTest, preview);
            if (preview) { Application.Run(Context); return; }
            Send(new { type = "hello", version = 3, test = ProtocolTest });
            new Thread(ReadInput) { IsBackground = true }.Start();
            Application.Run(Context);
        }
        static void ReadInput() {
            try {
                Stream input = Console.OpenStandardInput();
                while (true) {
                    byte[] header = Exact(input, 4);
                    if (header == null) break;
                    uint count = BitConverter.ToUInt32(header, 0);
                    if (count == 0 || count > 1048576) break;
                    byte[] body = Exact(input, (int)count);
                    if (body == null) break;
                    var message = new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(body)) as Dictionary<string, object>;
                    if (message != null) Context.Dispatch(message);
                }
            } catch { }
            Context.Shutdown(); // Dispose the tray icon and release shortcuts on pipe closure.
        }
        static byte[] Exact(Stream input, int count) {
            var bytes = new byte[count];
            for (int offset = 0; offset < count;) {
                int read = input.Read(bytes, offset, count - offset);
                if (read == 0) return null;
                offset += read;
            }
            return bytes;
        }
        internal static void Send(object message) {
            if (Context != null && Context.Preview) return;
            lock (OutputLock) {
                byte[] bytes = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(message));
                Output.Write(BitConverter.GetBytes(bytes.Length), 0, 4);
                Output.Write(bytes, 0, bytes.Length); Output.Flush();
            }
        }
    }

    internal static class PreviewProgram {
        [STAThread] static void Main() { Program.RunPreview(); }
    }

    internal sealed class BridgeContext : ApplicationContext {
        readonly Control dispatcher = new Control();
        readonly ShortcutWindow shortcuts;
        internal readonly Passage Passage = new Passage();
        internal Settings Settings = new Settings();
        internal PlayerForm Player;
        internal readonly ReaderMenu Menu;
        internal readonly NotifyIcon Tray;
        readonly System.Drawing.Icon trayIcon;
        internal bool Preview;
        readonly bool test;
        bool disposed;
        internal string Playback = "stopped";
        internal string StatusMessage = "Select text in a desktop app to begin.";
        internal BridgeContext(bool protocolTest, bool preview) {
            test = protocolTest; Preview = preview;
            IntPtr handle = dispatcher.Handle;
            shortcuts = new ShortcutWindow(this, test);
            Menu = new ReaderMenu(this);
            if (!test) {
                trayIcon = ReaderIcon.Create();
                Tray = new NotifyIcon { Icon = trayIcon, Text = preview ? "Read Aloud Desktop V3 — Preview" : "Read Aloud Desktop V3", ContextMenuStrip = Menu.Control, Visible = true };
                Tray.DoubleClick += delegate { ShowPlayer(false); };
            }
            if (preview) {
                LoadPassage("Your reading, at your pace.\n\nSelect text in any desktop app and press Alt + V. Read Aloud keeps the voice you already use.\n\nPause when you need a moment. Use Previous and Next to choose a sentence, or double-click a sentence to start there.", true);
                Playback = "playing";
                Player.SetStatus("Preview only — no text is sent or spoken.", Playback);
                Player.FormClosed += delegate { ExitThread(); };
            }
        }
        internal void Dispatch(Dictionary<string, object> message) {
            dispatcher.BeginInvoke((Action)delegate {
                try { Receive(message); }
                catch (Exception error) { Error(error.Message); }
            });
        }
        void Receive(Dictionary<string, object> m) {
            string type = Get(m, "type");
            if (type == "ping") Program.Send(new { type = "pong" });
            else if (type == "configure") {
                Settings candidate = null;
                string error;
                try {
                    candidate = new JavaScriptSerializer().ConvertToType<Settings>(m["settings"]);
                    error = candidate == null ? "Choose valid shortcuts for all four actions." : candidate.Validate();
                } catch { error = "Choose valid shortcuts for all four actions."; }
                if (error == null) error = shortcuts.Apply(candidate);
                if (error == null) {
                    Settings = candidate;
                    if (Player != null) Player.SetShortcuts(Settings);
                }
                Program.Send(new { type = "configured", requestId = Get(m, "requestId"), ok = error == null,
                    message = error ?? "Desktop shortcuts are ready.", settings = Settings, active = shortcuts.Active });
            } else if (type == "session") {
                LoadPassage(Get(m, "text"), m.ContainsKey("show") && (bool)m["show"]);
            } else if (type == "status") {
                Playback = Get(m, "playback");
                StatusMessage = Get(m, "message");
                if (Player != null) Player.SetStatus(StatusMessage, Playback);
                if (Tray != null) Tray.Text = (Preview ? "V3 Preview — " : "Read Aloud V3 — ") + (Playback == "playing" ? "Play requested" : Playback == "paused" ? "Pause requested" : "Ready");
            } else if (type == "player") ShowPlayer(true);
            else if (type == "restart") Jump(0);
            else if (test && type == "testMove") {
                Passage.Move(Convert.ToInt32(m["delta"]));
                Program.Send(new { type = "passage", index = Passage.Index, tail = Passage.Tail, count = Passage.Starts.Count });
            } else if (type == "clear") { Passage.Load(""); if (Player != null) Player.SetPassage(); }
        }
        internal void LoadPassage(string text, bool show) {
            if (Encoding.UTF8.GetByteCount(text) > 500000) { Error("Select a shorter passage (under 500 KB)."); return; }
            Passage.Load(text);
            if (Player != null) { Player.SetPassage(); Player.SetExpanded(false); }
            if (show && !test) ShowPlayer();
        }
        internal void ShowPlayer() { ShowPlayer(false); }
        internal void ShowPlayer(bool expanded) {
            if (test) return;
            if (Player == null || Player.IsDisposed) Player = new PlayerForm(this);
            Player.SetPassage(); Player.SetShortcuts(Settings);
            Player.SetStatus(StatusMessage, Playback);
            Player.SetExpanded(expanded);
            Player.Show();
        }
        internal void TogglePlayer() { if (Player != null && Player.Visible) Player.SetExpanded(!Player.Expanded); else ShowPlayer(true); }
        internal void TogglePlayback() {
            if (Playback == "stopped") Jump(Passage.Index); else Command("togglePause",null,false);
        }
        internal void SetPreference(string name,object value) {
            if (Preview) {
                var serializer = new JavaScriptSerializer();
                var values = serializer.Deserialize<Dictionary<string,object>>(serializer.Serialize(Settings));
                values[name] = value;
                if (name == "placement") values["customPosition"] = null;
                Settings candidate = serializer.ConvertToType<Settings>(values);
                string error = candidate.Validate();
                if (error != null) { Error(error); return; }
                Settings = candidate;
                if (Player != null) Player.SetShortcuts(Settings);
            } else Program.Send(new { type = "preference", name = name, value = value });
        }
        internal void Shutdown() {
            try { dispatcher.BeginInvoke((Action)ExitThread); } catch (InvalidOperationException) { }
        }
        protected override void Dispose(bool disposing) {
            if (disposing && !disposed) {
                disposed = true;
                if (Tray != null) { Tray.Visible = false; Tray.Dispose(); }
                if (Player != null && !Player.IsDisposed) Player.Dispose();
                Menu.Dispose(); if (trayIcon != null) trayIcon.Dispose();
                shortcuts.Release(); dispatcher.Dispose();
            }
            base.Dispose(disposing);
        }
        protected override void ExitThreadCore() { Dispose(); base.ExitThreadCore(); }
        internal void Jump(int index) {
            if (Passage.Starts.Count == 0) { Error("Read a passage first: select text and press " + Settings.read.label + "."); return; }
            Passage.Index = Math.Max(0, Math.Min(Passage.Starts.Count - 1, index));
            if (Player != null) Player.HighlightStart();
            Command("play", Passage.Tail, false);
        }
        internal void Command(string method, string text, bool newSession) {
            if (Preview) {
                if (method == "setPlacement" && (text == "bottom-right" || text == "top-right")) { Settings.placement = text; Settings.customPosition = null; Player.SetShortcuts(Settings); }
                if (method == "togglePause") Playback = Playback == "paused" ? "playing" : "paused";
                else if (method == "stop") Playback = "stopped";
                else if (method == "play") Playback = "playing";
                Player.SetStatus("Preview only — no text is sent or spoken.", Playback);
            } else Program.Send(new { type = "command", method = method, text = text, newSession = newSession });
        }
        internal void Error(string text) {
            StatusMessage = text;
            if (Player != null) Player.SetStatus(text, Playback);
            Program.Send(new { type = "error", message = text });
        }
        internal static string Get(Dictionary<string, object> message, string key) {
            object value; return message.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : "";
        }
    }

    internal sealed class ShortcutWindow : NativeWindow {
        readonly BridgeContext context;
        readonly bool test;
        Settings current;
        internal bool Active;
        bool capturing;
        readonly string[] names = { "Read selection", "Pause / resume", "Stop", "Show / hide player" };
        internal ShortcutWindow(BridgeContext bridge, bool protocolTest) { context = bridge; test = protocolTest; CreateHandle(new CreateParams()); }
        internal string Apply(Settings candidate) {
            if (test) { current = candidate; Active = true; return null; }
            if (Active && current != null) {
                bool same = true;
                Shortcut[] before = current.Shortcuts(), after = candidate.Shortcuts();
                for (int i = 0; i < before.Length; i++) if (before[i].key != after[i].key || before[i].modifiers != after[i].modifiers) same = false;
                if (same) { current = candidate; return null; }
            }
            Settings previous = current;
            UnregisterAll();
            string error = Register(candidate);
            if (error != null) {
                UnregisterAll();
                Active = previous != null && Register(previous) == null;
                if (!Active) UnregisterAll();
                current = Active ? previous : null;
                return error + (Active ? " Your previous shortcuts are still active." : " Disable V2's desktop shortcuts if they are still running, then retry.");
            }
            current = candidate; Active = true; return null;
        }
        string Register(Settings settings) {
            var items = settings.Shortcuts();
            for (int i = 0; i < items.Length; i++) {
                if (!RegisterHotKey(Handle, i + 1, items[i].modifiers | 0x4000, items[i].key)) {
                    int code = Marshal.GetLastWin32Error();
                    return names[i] + " (" + items[i].label + ") " + (code == 1409 ? "is already in use." : "could not be registered (Windows code " + code + ").");
                }
            }
            return null;
        }
        void UnregisterAll() { for (int i = 1; i <= 4; i++) UnregisterHotKey(Handle, i); Active = false; }
        internal void Release() { UnregisterAll(); DestroyHandle(); }
        protected override void WndProc(ref Message m) {
            if (m.Msg == 0x0312 && current != null && !capturing) {
                int id = m.WParam.ToInt32();
                if (id == 1) CaptureSelection();
                else if (id == 2) context.Command("togglePause", null, false);
                else if (id == 3) context.Command("stop", null, false);
                else if (id == 4) context.TogglePlayer();
            }
            base.WndProc(ref m);
        }
        void CaptureSelection() {
            capturing = true;
            IDataObject previous = null;
            bool changed = false;
            uint copiedSequence = 0;
            string selected = null, failure = null;
            try {
                DateTime deadline = DateTime.UtcNow.AddSeconds(2);
                while (Pressed(0x10) || Pressed(0x11) || Pressed(0x12) || Pressed((int)current.read.key)) {
                    if (DateTime.UtcNow > deadline) throw new Exception("Release the shortcut keys, then try again.");
                    Thread.Sleep(10);
                }
                previous = Clipboard.GetDataObject();
                DataObject saved = new DataObject();
                if (previous != null) foreach (string format in previous.GetFormats(false)) {
                    object value = previous.GetData(format, false);
                    if (value != null) saved.SetData(format, false, value);
                }
                previous = saved;
                uint before = GetClipboardSequenceNumber();
                INPUT[] keys = { Key(0x11, false), Key(0x43, false), Key(0x43, true), Key(0x11, true) };
                if (SendInput((uint)keys.Length, keys, Marshal.SizeOf(typeof(INPUT))) != keys.Length)
                    throw new Exception("Windows could not copy the selection. Try a regular desktop app.");
                deadline = DateTime.UtcNow.AddMilliseconds(1500);
                while (DateTime.UtcNow < deadline) {
                    uint sequence = GetClipboardSequenceNumber();
                    if (sequence != before) {
                        changed = true; copiedSequence = sequence;
                        try { if (Clipboard.ContainsText()) selected = Clipboard.GetText(); if (selected != null) break; }
                        catch (ExternalException) { }
                    }
                    Thread.Sleep(20);
                }
                if (String.IsNullOrWhiteSpace(selected)) throw new Exception("No text was copied. Highlight text that supports Ctrl+C and try again.");
                if (Encoding.UTF8.GetByteCount(selected) > 500000) throw new Exception("Select a shorter passage (under 500 KB).");
            } catch (Exception error) { failure = error.Message; }
            finally {
                if (changed && GetClipboardSequenceNumber() == copiedSequence) {
                    try { Clipboard.SetDataObject(previous, true); }
                    catch { Program.Send(new { type = "warning", message = "The previous clipboard could not be restored." }); }
                }
                capturing = false;
            }
            if (failure != null) context.Error(failure);
            else context.Command("play", selected, true);
        }
        static bool Pressed(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }
        static INPUT Key(ushort key, bool up) { INPUT input = new INPUT(); input.type = 1; input.data.keyboard.wVk = key; input.data.keyboard.dwFlags = up ? 2u : 0; return input; }
        [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion data; }
        [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public KEYBDINPUT keyboard; [FieldOffset(0)] public MOUSEINPUT mouse; }
        [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public UIntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public UIntPtr dwExtraInfo; }
        [DllImport("user32.dll", SetLastError=true)] static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll", SetLastError=true)] static extern uint SendInput(uint count, INPUT[] input, int size);
    }
}
