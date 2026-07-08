# CLAUDE.md

## Project Overview

LittleSwitcher is a Windows desktop app for enhanced window and virtual desktop switching with global hotkeys. Built with .NET 9 WinForms targeting Windows 10/11.

## Build & Run

```bash
dotnet build LittleSwitcher.csproj
dotnet publish LittleSwitcher.csproj -c Release
```

The exe may be locked if the app is running — close from tray icon first.

## Architecture

- **AppHost.cs** — Hidden background form: tray icon, global hotkey registration, WinEvent hooks. Entry point for all hotkey actions.
- **AppHost.Designer.cs** — Designer code for tray icon and context menu.
- **MainWindow.cs** — Tabbed settings dialog (Settings, App Launcher, Status tabs). Contains `KeyTextBox` for key capture.
- **ZOrderWindowSwitcher.cs** — Reads top-level z-order on demand and selects eligible windows per current desktop/monitor.
- **WindowFilterConfig.cs** — Regex include/exclude config persisted to `%AppData%/LittleSwitcher/window_filters.json`.
- **GlobalHotkey.cs** — Win32 RegisterHotKey/UnregisterHotKey wrapper.
- **HotkeyConfig.cs** — Hotkey config model with JSON persistence to `%AppData%/LittleSwitcher/hotkeys.json`. Also contains `HotkeyBinding` with `KeyToString`.
- **AppLauncherConfig.cs** — App launcher config with JSON persistence to `%AppData%/LittleSwitcher/launcher.json`.
- **VirtualDesktopInterop.cs** — Wrapper around Slions.VirtualDesktop NuGet (COM interop, no native DLL).
- **WindowHelper.cs** — Win32 P/Invoke helpers: focus, taskbar toggle, title bar toggle, cursor positioning.
- **TitleOverlay.cs** — Borderless topmost overlay showing window title for 3s when title bar is hidden.

## Key Patterns

- Shared modifier (Alt/Ctrl/Shift/Win) + individual key per hotkey action.
- Hotkeys stay registered while settings dialog is open — KeyTextBox captures raw key without modifier.
- Config auto-saves on change in App Launcher tab; Settings tab requires explicit Save button.
- Z-order switching does not maintain a linked list; `Alt+W` sends current eligible window to bottom and focuses the top eligible window.
- `_titleBarHiddenWindows` HashSet in AppHost tracks windows with hidden title bars; restored on app exit.
- App launcher polls `Process.MainWindowHandle` every 200ms (up to 5s) before moving window to target desktop.

## Hotkey Mechanism Notes

`RegisterHotKey` intercepts the key combination but not individual modifier key events: the foreground window still receives `WM_SYSKEYDOWN(VK_MENU)` when Alt is first pressed, and `WM_SYSKEYUP(VK_MENU)` when Alt is physically released. Only the combo's keydown is suppressed; keyup events are never intercepted.

This causes menu activation on the newly focused window when switching with an Alt modifier: the new window receives a hardware-repeat Alt keydown after focus transfer, and if Alt is released before any other key, DefWindowProc fires SC_KEYMENU. The current fix (`InjectModifierKeyUp` via `SendInput` in `FocusWindowWithOverlay`) clears the global Alt async key state before `SetForegroundWindow`, which handles the focus-transfer moment but not hardware repeats from prolonged key holds.

The injected Alt keyup itself can also fire SC_KEYMENU on the *old* window: during a desktop switch the old window is still foreground for the whole switch animation, so it receives the injected keyup right after the original Alt keydown with nothing in between — leaving it stuck in menu mode when the user returns. Fix: for Alt/Win modifiers, `SendMaskedKeyUp` sends a masking LControl down/up before the modifier keyup (AutoHotkey's mask-key trick), so no window ever sees an unaccompanied Alt down→up pair.

The *newly focused* window has the mirror problem: it never saw the Alt keydown (swallowed context), and the user's physical Alt release is delivered to it as a bare keyup. Alt-tap-sensitive windows (WinForms MenuStrip apps, RDP clients that sync local modifier state into the session on focus gain — e.g. RDCMan) then behave as if Alt is stuck down. Release order matters: releasing the hotkey's non-modifier key first delivers its (unswallowed) keyup ahead of the Alt keyup, making it non-bare — which is why the bug only shows when Alt is released first. Fix: `ReassertModifierReleaseAfterFocus` re-sends the masked release to the new window right after `FocusWindow`, using `_heldModifierKeys` captured at hotkey-fire time (the async state of the generic VK is already cleared by our own injection by then, so detection uses left/right-specific VKs).

Injection details that matter: SendInput events must carry real scan codes (`MapVirtualKey`) — RDP clients forward keys by scan code and drop `wScan = 0` events — and right-side modifiers plus Win keys need `KEYEVENTF_EXTENDEDKEY`.

**`WH_KEYBOARD_LL` release guard** — injection ordering cannot win the race when the modifier is released the instant the desktop switches: the shell foregrounds the target window within tens of ms and the bare physical keyup lands there before any post-focus re-injection. `ModifierReleaseGuard` (armed at each hotkey fire, in `InjectModifierKeyUp`) eats the modifier's hardware repeats and replaces its physical keyup with the masked scan-coded release, guaranteeing no window ever receives a bare modifier keyup. Replacing (not just eating) keeps the async key state consistent — injected events update it, eaten ones don't. 5s timeout guards against a release missed on the UAC secure desktop (LL hooks don't run there). Hook proc must return within ~300ms; does not require a separate DLL.

## Conventions

- WinForms absolute positioning (not TableLayoutPanel — it caused layout issues).
- All Win32 interop via `DllImport` in the class that uses it, except shared ones in `WindowHelper`.
- Namespace: `LittleSwitcher` for all files.
