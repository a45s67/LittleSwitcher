# LittleSwitcher

A lightweight Windows application for enhanced window and virtual desktop switching with global hotkeys.

## Features

- **Cycle Windows**: Cycle through window focus history in current virtual desktop and monitor
- **Switch Desktops**: Switch to virtual desktops 1-9
- **Focus Other Monitor**: Focus last focused window on another screen (multi-monitor)
- **Last Desktop**: Jump to and focus last focused window in last virtual desktop
- **Toggle Management**: Add/remove current window to/from management system
- **Configurable Hotkeys**: All shortcuts are customizable via the Settings UI (right-click tray icon → Settings)

### Default Hotkeys

| Action | Default Shortcut |
|---|---|
| Toggle Management | Alt + A |
| Cycle Windows | Alt + W |
| Switch Desktop 1-9 | Alt + 1-9 |
| Focus Other Monitor | Alt + ] |
| Last Desktop | Alt + \\ |

## How It Works

### Focus History System
- Uses hash map with key `(virtual_desktop_id, monitor_id)` and value as circular linked list of windows
- Each desktop+monitor combination maintains independent focus history
- Windows must be manually added to management system using the Toggle Management hotkey
- Cycle Windows cycles through managed windows within current context only

### Example Usage
1. Focus Window 1, press Alt+A to add to management
2. Focus Window 2, press Alt+A to add to management
3. Focus Window 3, press Alt+A to add to management (all on same desktop/monitor)
4. Press Alt+W → focuses Window 1
5. Press Alt+W again → focuses Window 2
6. To remove Window 3 from management: focus it and press Alt+A again

## Installation

1. Download the latest release
2. Run `LittleSwitcher.exe`

## Requirements

- Windows 10/11 with Virtual Desktop support
- .NET 8.0 Runtime

## Building from Source

```bash
dotnet build -c Release
```

Single-file self-contained publish:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## Dependencies

- [Slions.VirtualDesktop](https://github.com/Slions/VirtualDesktop) - Pure C# virtual desktop management via COM interop

## Roadmap

- [x] Configurable hotkeys via Settings UI
- [x] Tabbed main window (Settings + Status)
- [ ] Move mouse to center of window on focus switch
- [ ] Autorun toggle (Start with Windows via registry, not a service)
- [ ] Pin window to all desktops (toggle hotkey)
- [ ] Toggle taskbar visibility (hotkey, restore on exit)
- [ ] Toggle window title bar (hotkey, show floating name overlay for 3s on focus switch)
- [ ] App Launcher tab (configure apps to launch on specific virtual desktops, auto-add to managed list)

## License

This project is open source.
