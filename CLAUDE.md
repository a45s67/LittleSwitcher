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
- **FocusHistory.cs** — Circular linked list per (desktop, monitor) context for window focus tracking.
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
- `_titleBarHiddenWindows` HashSet in AppHost tracks windows with hidden title bars; restored on app exit.
- App launcher polls `Process.MainWindowHandle` every 200ms (up to 5s) before moving window to target desktop.

## Conventions

- WinForms absolute positioning (not TableLayoutPanel — it caused layout issues).
- All Win32 interop via `DllImport` in the class that uses it, except shared ones in `WindowHelper`.
- Namespace: `LittleSwitcher` for all files.
