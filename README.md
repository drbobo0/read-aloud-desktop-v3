# Read Aloud Desktop V3

A Windows desktop companion for [Read Aloud: A Text to Speech Voice Reader](https://chromewebstore.google.com/detail/read-aloud-a-text-to-speec/hdhinadidafjejdhmfkjgnolgimiaplp). Select text in a desktop app and press **Alt + V** to read it using your existing Chrome voices. Control reading from a small, draggable dark pill or a Windows tray icon.

V3 adds idle fading, three sizes, a position lock and optional text cleanup. It has its own identity and settings, so earlier desktop bridge versions can remain installed separately. Screenshot reading is not included. This project is a separate companion and requires the original Read Aloud extension.

## Requirements

- Windows 10 or 11 with .NET Framework 4.8. Windows 11 supplies the native rounded outer corners.
- Google Chrome with the original Read Aloud extension installed and enabled in the same profile.
- No paid service, Node.js or Python is required to use the bridge. Node.js and Python are only used for developer checks.

## Start V3

1. Extract the V3 ZIP into a permanent folder, or clone this repository. If you use an earlier desktop bridge, click **Disable** in that bridge first. Keep the original **Read Aloud: A Text to Speech Voice Reader** enabled. Only one desktop bridge should own the shortcuts.
2. Double-click **Install.cmd** in the extracted folder. It builds the helper if needed and registers only V3 for your Windows account; administrator rights and a separate developer SDK are not needed. Keep the folder in its permanent location after installing.
3. Open **chrome://extensions** in Chrome. Enable **Developer mode** if necessary, click **Load unpacked**, and choose the **extension** subfolder inside the V3 folder.
4. Open **Read Aloud Desktop Bridge V3** from Chrome's extensions menu and click **Enable / retry**, then **Test voice**.
5. Select text in a desktop app and press **Alt + V**, then release the keys.

V3 has its own extension identity, preferences, native helper and registration. It starts with Small size, bottom-right placement, fading on, position unlocked and all cleanup options off. V2's saved preferences remain with V2. To return to V2, disable V3 and re-enable V2.

## New controls

**Windows tray icon.** While V3 is enabled and connected to Chrome, a purple speaker appears in the notification area by the clock, possibly inside the **^ hidden-icons menu**. Double-click it to show the compact player. Right-click for show/hide, reading controls, pause/resume, stop and preferences. **Disable V3** stops reading, releases shortcuts and removes the tray icon. Open the Chrome extension and choose **Enable / retry** to start it again. Windows chooses whether new tray icons appear beside the clock or under the arrow.

**Fade when idle.** After five seconds without interaction, the compact pill smoothly fades to 45% opacity. Hover over it to restore full visibility immediately. Showing the player, starting another selection and using controls also brighten it. Expanded controls and open menus stay fully visible. Switch fading off in Preferences or the tray menu to keep the pill opaque.

**Adjustable size.** Choose **Small**, **Medium** or **Large**. Small retains V2's 112 × 28 logical-pixel size; Medium is 140 × 35 and Large is 168 × 42. Windows display scaling is applied separately, with native-resolution fonts and vector icons. Corner placement stays anchored when size changes; a freely dragged position retains its top-left coordinates, subject to monitor bounds.

**Position lock.** Turn on **Lock position** to prevent accidental dragging. Turning it off allows dragging by the Read label, the empty bar or the expanded heading. Placement presets and **Reset to selected corner** still work when locked. Saved positions are kept inside a connected monitor's working area.

**Optional text cleanup.** Each option is off initially and can be selected independently:

- **Skip web addresses** removes http/https and www addresses while retaining Markdown link labels.
- **Skip repeated symbols** removes runs of three or more identical punctuation or symbol characters, such as `***`, `===` and `!!!`.
- **Skip fenced code blocks** omits complete blocks enclosed in triple backticks or tildes. Inline code and incomplete fences are preserved.

Cleanup affects **new selections**, including the test passage. Changing an option does not rewrite a passage already loaded. The passage view shows the same cleaned text that is sent to Read Aloud, so Previous/Next and restarting use that exact text. If cleanup leaves nothing to read, V3 explains that and keeps the previous passage available. Text cleanup is deliberately limited; it does not detect all code or interpret document structure.

Access preferences by right-clicking the pill or tray icon, or by expanding the three dots and clicking **Preferences**. The Chrome popup also has **Shortcuts & preferences**; changes there take effect when you click **Save preferences**. Native menu changes save immediately.

## Reading and placement

| Action | Default shortcut |
| --- | --- |
| Read selected text | Alt + V |
| Pause / resume | Ctrl + Alt + Space |
| Stop | Alt + Shift + V |
| Expand / collapse player | Ctrl + Alt + V |

Customize shortcuts in the Chrome popup. Conflicts reject the change and attempt to restore the previous shortcuts. Preferences that do not change shortcuts leave their registrations in place.

The blue button pauses, resumes or starts the passage again. The three dots expand/collapse controls; Esc collapses. The × hides the pill while the tray icon and shortcuts remain available. Bottom right is the default, with top right as another preset. Dragged positions and preferences are saved across helper and Chrome restarts. Previous/Next and double-clicking a sentence choose a starting sentence; highlighting is not live word tracking.

## When it runs

Chrome and the original Read Aloud extension must stay running in the same profile. If V3 was enabled, it reconnects when Chrome next starts, including after a Windows restart. There is no separate Windows startup service, web server or scheduled task. The helper and tray icon end when the native connection closes, V3 is disabled or Chrome exits. Use only one Chrome profile's desktop helper at a time.

Read Aloud's external interface accepts play, pause, resume and stop requests. It does not report actual completion or playback position. V3's button and tray labels follow the last request made through V3, so they can become out of sync when speech ends naturally or is controlled in Read Aloud itself. Starting a new selection resets that sequence. **Voice / speed** opens Read Aloud's details page; use its Extension options or gear menu to change voices and speed.

## Text and settings

Text is copied only when you press the read shortcut. V3 uses Ctrl+C and attempts to restore your previous clipboard. It keeps passage text in memory while connected; text is not saved to Chrome storage. Only preferences, shortcuts, status and window coordinates are saved. Cleanup is performed locally before the text is sent to the original Read Aloud extension. A cloud voice may send that text to its provider, as determined by Read Aloud's settings.

There is no screenshot capture, OCR, clipboard watcher, webpage access, telemetry or passage history. Text must support Ctrl+C; protected content, elevated apps and scanned images may not work. Clipboard restoration is best effort for unusual formats; clipboard managers may retain the briefly copied selection. Maximum selection size is 500 KB in UTF-8.

## Preview and removal

Double-click **Preview.cmd** to try the player, sizes, fading, locking and tray menus with sample text. The preview does not register shortcuts, copy text, send speech or change saved preferences. Its tray icon is labelled **Preview**; choose **Close preview** from its menu to quit. Hiding the preview leaves its tray controls available.

To remove V3, disable it in Chrome, remove **Read Aloud Desktop Bridge V3**, then run **Uninstall.cmd from this folder**. It removes only V3's helper registration. V1, V2 and the original Read Aloud extension remain intact.

## Building and checks

Double-click **Build.cmd**, or run `powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1`. This uses Windows' .NET Framework C# compiler to build the helper and sample preview. The `runtime.json` file names their versioned executables. Compiled programs and the generated native-host manifest are excluded from source control.

Run these checks from the project folder after building:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Run-PlayerChecks.ps1
python tests/native_checks.py
node tests/extension_checks.cjs
```

The optional `python tests/native_checks.py --windows-hotkeys` tests shortcut conflicts with temporary keys and releases them without touching the clipboard. Local, untracked checksum snapshots can additionally verify that earlier versions are untouched; those snapshots are not needed for the portable checks.

The original Read Aloud project is available at [ken107/read-aloud](https://github.com/ken107/read-aloud). This repository contains the desktop companion and bridge, and does not bundle the original extension or voice providers.
