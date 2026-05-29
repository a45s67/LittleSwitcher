# LittleSwitcher

A lightweight Windows application for enhanced window and virtual desktop switching with global hotkeys.

## Features

- **Cycle Windows**: Send the current window to the bottom of z-order and focus the top eligible window on the current virtual desktop and monitor
- **Switch Desktops**: Switch to virtual desktops 1-9
- **Focus Other Monitor**: Focus the top eligible window on another screen (multi-monitor)
- **Last Desktop**: Jump to the last desktop and focus its top eligible window
- **Pin Window**: Pin/unpin current window to all virtual desktops
- **Toggle Taskbar**: Hide/show the Windows taskbar
- **Toggle Title Bar**: Hide window title bar with floating overlay on focus
- **App Launcher**: Configure apps to launch on specific virtual desktops
- **Autorun**: Start with Windows option
- **Configurable Hotkeys**: All shortcuts are customizable via the Settings UI

### Default Hotkeys

| Action | Default Shortcut |
|---|---|
| Cycle Windows | Alt + W |
| Switch Desktop 1-9 | Alt + 1-9 |
| Previous Desktop | Alt + [ |
| Next Desktop | Alt + / |
| Focus Other Monitor | Alt + ] |
| Last Desktop | Alt + \\ |
| Pin Window | Alt + P |
| Toggle Taskbar | Alt + T |
| Toggle Title Bar | Alt + H |

## How It Works

### Z-order Window Switching
- Reads Windows top-level z-order on demand instead of maintaining a managed window list
- Filters windows by current virtual desktop, monitor, visibility, cloaking state, usable rect, and regex include/exclude rules
- Sends the current window to the bottom on cycle, then focuses the current top eligible window
- Regex filters live at `%AppData%/LittleSwitcher/window_filters.json`

### Example Usage
1. Focus the top window on a desktop and monitor
2. Press Alt+W to send it to the bottom
3. LittleSwitcher focuses the next top eligible window in the same desktop+monitor context
4. Edit `%AppData%/LittleSwitcher/window_filters.json` to exclude or include windows by title, class name, or process name

## Installation

1. Download the latest release
2. Run `LittleSwitcher.exe`

## Requirements

- Windows 10/11 with Virtual Desktop support
- .NET 9.0 Runtime (or use self-contained build)

## Building from Source

```bash
dotnet build -c Release
```

Single-file self-contained publish:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## Dependencies

- [Slions.VirtualDesktop](https://github.com/Slion/VirtualDesktop) - Pure C# virtual desktop management via COM interop (fork of Grabacr07/VirtualDesktop)

## Roadmap

- [x] Configurable hotkeys via Settings UI
- [x] Tabbed main window (Settings + Status)
- [x] Move mouse to center of window on focus switch
- [x] Autorun toggle (Start with Windows via registry)
- [x] Pin window to all desktops (toggle hotkey)
- [x] Toggle taskbar visibility (hotkey, restore on exit)
- [x] Toggle window title bar (hotkey, show floating name overlay for 3s on focus switch)
- [x] App Launcher tab (configure apps to launch on specific virtual desktops)

## License

This project is open source.
