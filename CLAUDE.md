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

`RegisterHotKey` intercepts the combo's keydown but nothing else: the old foreground window still sees the modifier's `WM_SYSKEYDOWN`, and whichever window is foreground at release receives the physical keyup. Keyup events are never intercepted.

The failure mode (measured with `KeyLogTool`, see below): after a switch the shell foregrounds the target within ~10ms, while the modifier is often still physically held. A window activated with the modifier physically down has its thread key state synced from the async table as "modifier down"; the user's physical keyup arriving shortly after (~14ms measured) then completes a clean Alt tap and arms menu/mnemonic mode in Alt-tap-sensitive apps (WinForms MenuStrip apps like RDCMan; focus lands on its tree view, the RDP session is not involved) — the app then acts as if Alt is stuck down until Esc. Releases landing ≥~75ms after activation were consistently safe. The *old* window is safe with no help at all: it saw the real Alt keydown and simply never receives the keyup (the Alt+Tab model).

**Disproven by measurement — do not retry**: injecting an intervening "mask" event after the foreground change (dummy vk 0xFF, mstsc-style, delivered 12ms before the physical keyup) does NOT prevent the arming; the arming comes from activation-time state sync, not from the message sequence. The apparent correlation between mstsc's own 0xFF taps and safe switches was spurious (those cases were safe for timing reasons). Also tried and reverted: a `WH_KEYBOARD_LL` guard that eats/replaces modifier events — caused sticky/dead Alt (compounded by the then-broken SendInput below), and an LL hook cannot distinguish hardware repeats from fresh presses without maintaining its own physical-state table (commit c727892, reverted in 53e6057).

Fix (`ReleaseModifierBeforeSwitch` in AppHost): when a hotkey fires while the modifier is physically held (left/right-specific VK check — e.g. right Alt reports as `VK_RMENU`), synchronously inject [LControl down/up mask + keyup for each held side key] *before* `GoToDesktopNumber`/`SetForegroundWindow`, so the async state already reads "up" when the new window is activated and syncs its key state. The Ctrl mask protects the *old* window (which saw the real Alt keydown) from reading the injected keyup as an Alt tap — AutoHotkey's mask-key trick. Injected events carry real scan codes (`MapVirtualKey` + `KEYEVENTF_EXTENDEDKEY` for right-side/Win keys) so RDP clients, which forward by scan code, see them too.

**Combo mode**: clearing the async state breaks `RegisterHotKey` matching for held-modifier chaining (confirmed by testing — the matcher uses the async state). After injecting the release, `EnterComboMode` re-registers all trigger keys as *bare* hotkeys (same actions, recorded via `RegisterBoundHotkey` into `_hotkeyBindings`), and `ModifierReleaseWatcher` — an observe-only `WH_KEYBOARD_LL` hook that never swallows events (none of the stuck-key risk of an intercepting hook), living on its own always-pumping thread so system-wide keystroke delivery never stalls behind a busy UI thread — exits combo mode the instant the modifier is physically released. 10s timeout as a safety valve (LL hooks don't run on the UAC secure desktop). Side effect while combo-holding: non-hotkey keys pressed with the modifier held type plainly instead of acting as Alt+key accelerators.

Why Alt+W never showed the stuck-Alt bug while desktop switching did: the desktop path's foreground change is done by the shell ~9ms after the keydown (long before a human can release), while the Alt+W path's foreground change is our own handler at ~100ms (z-order enumeration) — by then the keyup has usually landed in the old window (paired, safe), and later releases fall past the ~75ms danger window. Geometry, not immunity.

**SendInput cbSize trap**: the `INPUT` union must include `MOUSEINPUT` (its largest member) even when only keyboard input is sent — with only `KEYBDINPUT` in the union, `Marshal.SizeOf<INPUT>()` is 32 instead of 40 on x64 and SendInput silently rejects every call (returns 0). All injections in this app were no-ops from the first fix commit until this was found via KeyLogTool showing no `INJECTED` events — meaning the fire-time release design had never actually been tested before.

Known limitation (Windows semantics, same as holding Alt through Alt+Tab): holding Alt long enough after the switch for hardware repeats makes the new window see real Alt keydowns, so releasing then activates its menu legitimately.

**KeyLogTool/** — diagnostic console app (separate project, excluded from the main build): logs every low-level keyboard event with the `LLKHF_INJECTED` flag plus `EVENT_SYSTEM_FOREGROUND` / `EVENT_OBJECT_FOCUS` changes on one timeline. Run with `dotnet run --project KeyLogTool`.

## Conventions

- WinForms absolute positioning (not TableLayoutPanel — it caused layout issues).
- All Win32 interop via `DllImport` in the class that uses it, except shared ones in `WindowHelper`.
- Namespace: `LittleSwitcher` for all files.
