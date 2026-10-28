using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ReadAloudDesktopV3 {
    public class Shortcut {
        public uint modifiers;
        public uint key;
        public string label;
        public Shortcut() { }
        public Shortcut(uint mods, uint virtualKey, string display) { modifiers = mods; key = virtualKey; label = display; }
    }
    public class PlayerPosition { public int x; public int y; public bool physical; }
    public class Settings {
        public Shortcut read = new Shortcut(1, 86, "Alt + V");
        public Shortcut pause = new Shortcut(3, 32, "Ctrl + Alt + Space");
        public Shortcut stop = new Shortcut(5, 86, "Alt + Shift + V");
        public Shortcut player = new Shortcut(3, 86, "Ctrl + Alt + V");
        public bool showOnRead = true;
        public bool notifications = true;
        public string placement = "bottom-right";
        public string playerSize = "small";
        public bool fadeWhenIdle = true;
        public bool autoHide = true;
        public int autoHideSeconds = 5;
        public bool positionLocked = false;
        public bool cleanupUrls = false;
        public bool cleanupSymbols = false;
        public bool cleanupCode = false;
        public PlayerPosition customPosition = null;
        public Shortcut[] Shortcuts() { return new[] { read, pause, stop, player }; }
        public string Validate() {
            if (autoHideSeconds != 3 && autoHideSeconds != 5) return "Choose 3 or 5 seconds for automatic hiding.";
            if (playerSize != "small" && playerSize != "medium" && playerSize != "large") return "Choose Small, Medium or Large for the player.";
            if (placement != "bottom-right" && placement != "top-right") return "Choose bottom right or top right for the player.";
            if (customPosition != null && (Math.Abs((long)customPosition.x) > 100000 || Math.Abs((long)customPosition.y) > 100000)) return "Choose a valid player position.";
            var seen = new HashSet<string>();
            foreach (var item in Shortcuts()) {
                if (item == null || (item.modifiers & ~7u) != 0 || (item.modifiers & 3) == 0)
                    return "Each shortcut needs Ctrl or Alt, with optional Shift.";
                bool validKey = item.key == 32 || (item.key >= 48 && item.key <= 57)
                    || (item.key >= 65 && item.key <= 90) || (item.key >= 112 && item.key <= 123);
                if (!validKey) return "Use a letter, number, F1–F12, or Space for each shortcut.";
                if (!seen.Add(item.modifiers + ":" + item.key)) return "Choose a different shortcut for each action.";
                item.label = Label(item);
            }
            return null;
        }
        static string Label(Shortcut shortcut) {
            var parts = new List<string>();
            if ((shortcut.modifiers & 2) != 0) parts.Add("Ctrl");
            if ((shortcut.modifiers & 1) != 0) parts.Add("Alt");
            if ((shortcut.modifiers & 4) != 0) parts.Add("Shift");
            parts.Add(shortcut.key == 32 ? "Space" : shortcut.key >= 112 ? "F" + (shortcut.key - 111) : ((char)shortcut.key).ToString());
            return String.Join(" + ", parts);
        }
    }
    internal sealed class Passage {
        internal string Text = "";
        internal readonly List<int> Starts = new List<int>();
        internal int Index;
        internal void Load(string text) {
            Text = text ?? ""; Starts.Clear(); Index = 0;
            foreach (Match match in Regex.Matches(Text, @"\S[\s\S]*?(?:[.!?。！？؟]+[""'”’»]*(?=\s|$)|$)"))
                if (match.Length > 0) Starts.Add(match.Index);
            if (Starts.Count == 0 && Text.Length > 0) Starts.Add(0);
        }
        internal void Move(int delta) { Index = Math.Max(0, Math.Min(Starts.Count - 1, Index + delta)); }
        internal void Choose(int charIndex) {
            for (int i = Starts.Count - 1; i >= 0; i--) if (Starts[i] <= charIndex) { Index = i; return; }
            Index = 0;
        }
        internal string Tail { get { return Starts.Count == 0 ? "" : Text.Substring(Starts[Index]); } }
        internal int Start { get { return Starts.Count == 0 ? 0 : Starts[Index]; } }
        internal int Length { get { return Starts.Count == 0 ? 0 : (Index + 1 < Starts.Count ? Starts[Index + 1] : Text.Length) - Start; } }
    }
}
