# LittleSwitcher

A lightweight Windows application for enhanced window and virtual desktop switching with global hotkeys.

## Features

- **Alt+A**: Add/remove current window to/from management system (toggle)
- **Alt+W**: Cycle through window focus history in current virtual desktop and monitor
- **Alt+1-9**: Switch to virtual desktops 1-9
- **Alt+]**: Focus last focused window on another screen (multi-monitor)
- **Alt+\\**: Focus last focused window in last virtual desktop

## How It Works

### Focus History System
- Uses hash map with key `(virtual_desktop_id, monitor_id)` and value as circular linked list of windows
- Each desktop+monitor combination maintains independent focus history
- Windows must be manually added to management system using Alt+A
- Alt+W cycles through managed windows within current context only

### Example Usage
1. Focus Window 1, press Alt+A to add to management
2. Focus Window 2, press Alt+A to add to management
3. Focus Window 3, press Alt+A to add to management (all on same desktop/monitor)
4. Press Alt+W → focuses Window 1
5. Press Alt+W again → focuses Window 2
6. To remove Window 3 from management: focus it and press Alt+A again

## Installation

1. Download the latest release
2. Extract both files to a folder:
   - `LittleSwitcher.exe`
   - `VirtualDesktopAccessor.dll`
3. Run `LittleSwitcher.exe`

## Requirements

- Windows 10/11 with Virtual Desktop support
- .NET 7.0 Runtime

## Technical Details

- Written in C# using Windows Forms
- Runs as background service with system tray icon
- Manual window management via Alt+A toggle
- Uses Win32 APIs for window management and global hotkeys
- Integrates with VirtualDesktopAccessor.dll for virtual desktop operations

## Building from Source

```bash
cd LittleSwitcher
dotnet publish -c Release
cp VirtualDesktopAccessor.dll bin/Release/net7.0-windows/
```

## Dependencies

- [VirtualDesktopAccessor](https://github.com/Ciantic/VirtualDesktopAccessor) - For virtual desktop management

## Project Structure

```
switcher/
├── README.md                           # Project documentation
├── VirtualDesktopAccessor.dll          # External dependency
│
└── LittleSwitcher/                     # Main project directory
    ├── LittleSwitcher.csproj           # Project configuration
    ├── Program.cs                      # Application entry point
    ├── VirtualDesktopAccessor.dll      # Copy for development
    │
    ├── Core Application:
    ├── Form1.cs                        # Main application form (hidden)
    ├── Form1.Designer.cs              # Windows Forms designer code
    │
    ├── Window Management:
    ├── FocusHistory.cs                 # Hash map + linked list focus tracking
    ├── WindowHelper.cs                 # Win32 window manipulation APIs
    ├── WindowInfo.cs                   # Window information data class
    │
    ├── System Integration:
    ├── GlobalHotkey.cs                 # Global hotkey registration
    ├── VirtualDesktopInterop.cs        # Virtual desktop API wrapper
    │
    └── bin/Release/net7.0-windows/     # Build output
        ├── LittleSwitcher.exe          # ← Main executable
        ├── LittleSwitcher.dll          # Application library
        └── VirtualDesktopAccessor.dll  # Required dependency
```

## License

This project is open source. VirtualDesktopAccessor.dll is distributed under its original license.