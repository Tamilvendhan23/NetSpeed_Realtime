# NetSpeed Monitor

A tiny, native **real-time network speed monitor for Windows 10/11**. It shows live **download (↓)** and **upload (↑)** speeds in a slim, borderless, draggable status bar on your desktop, with all controls in the system tray.

Built with **C# / .NET 8 / Windows Forms**. No telemetry, no network calls, no installer, and no administrator rights required.

---

## Features

- **Live speeds**: download and upload sampled from Windows network interface counters.
- **Speed units**: `Automatic`, `Mbps`, `MB/s`, `Kbps`, `KB/s`, `bps`, `B/s`.
- **Text color**: Green, White or Black.
- **Show / hide** download or upload independently (at least one stays visible).
- **Refresh interval**: 1, 2 or 5 seconds.
- **Network interface**: Automatic (active connection) or a specific adapter.
- **Always on top** toggle and **snap position** (bottom/top, left/right).
- **Lock position**: disables drag & drop so the bar can't be moved by accident (tray menu or Preferences).
- **System tray** menu for every option; double-click the bar to open Preferences.
- **Start with Windows** (per-user, `HKCU`, no admin needed).
- **Single instance**: launching it again shows the running status bar.
- **High-DPI ready**: `PerMonitorV2`; the Preferences window sizes itself from the font, so it is correct at 100%–300% scaling.

---

## Requirements

| | |
| :--- | :--- |
| OS | Windows 10 / 11 (x64) |
| To build | [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) |
| To run the published exe | Nothing (self-contained) |

---

## Build & Run

```cmd
git clone https://github.com/<your-username>/netspeedmonitor.git
cd netspeedmonitor
build.bat
```

Or with PowerShell: `.\build.ps1` (add `-FrameworkDependent` for a smaller build that needs the .NET 8 Desktop Runtime).

The script publishes a self-contained single-file executable to `publish\NetSpeedMonitor.exe`.

Manual command:

```cmd
dotnet publish NetSpeedMonitor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o .\publish
```

For development you can also run `dotnet run` (Windows only).

---

## Usage

| Action | How |
| :--- | :--- |
| Move the bar | Drag it anywhere (position is remembered); turn on **Lock Position** to prevent dragging |
| Open the menu | Right-click the bar or the tray icon |
| Open Preferences | Tray menu → **Preferences…**, or double-click the bar |
| Hide / show the bar | Tray menu, or double-click the tray icon |
| Quit | Tray menu → **Exit** |

Closing the bar only hides it to the tray; use **Exit** to quit.

---

## Settings

Saved automatically to `%LOCALAPPDATA%\NetSpeedMonitor\settings.json`.

| Setting | Default | Description |
| :--- | :--- | :--- |
| `AlwaysOnTop` | `true` | Keep the bar above other windows |
| `DisplayUnit` | `"Automatic"` | `Automatic`, `Mbps`, `MB/s`, `Kbps`, `KB/s`, `bps`, `B/s` |
| `TextColor` | `"Green"` | `Green`, `White` or `Black` |
| `ShowDownload` / `ShowUpload` | `true` | Which readings are shown |
| `RefreshIntervalSeconds` | `1` | `1`, `2` or `5` |
| `SelectedAdapterId` | `""` | Empty / `AUTO` = automatic |
| `StartWithWindows` | `false` | Per-user auto-start |
| `LockPosition` | `false` | Disable dragging the status bar |
| `IsStatusBarVisible` | `true` | Bar shown or hidden to tray |
| `WindowLocationX` / `Y` | `-1` | `-1` = default bottom-right |

---

## Project structure

```
netspeedmonitor/
├── Program.cs               # Entry point, single-instance mutex, global error handling
├── MainForm.cs              # Transparent status bar + system tray menu
├── SettingsForm.cs          # Preferences dialog (auto-sizing, DPI-safe layout)
├── NetworkSpeedTracker.cs   # Adapter selection, speed sampling, unit formatting
├── AppSettings.cs           # settings.json load/save
├── StartupManager.cs        # HKCU Run-key registration
├── app.manifest             # asInvoker, Windows 10/11 compatibility
├── NetSpeedMonitor.csproj
├── build.bat / build.ps1    # Release build scripts
├── .gitignore
├── .gitattributes
└── README.md
```

---

## Uninstall

1. Tray icon → **Exit**.
2. If startup was enabled, uncheck **Start with Windows** in Preferences (or delete the `NetSpeedMonitor` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`).
3. Delete the app folder.
4. Optional: delete `%LOCALAPPDATA%\NetSpeedMonitor\`.

---

## Troubleshooting

- **Moved the exe after enabling startup?** Open Preferences and save once so the new path is registered.
- **Wrong adapter in Automatic mode?** Pick your adapter explicitly in Preferences → Network → Interface.
- **Bar is off-screen after changing monitors?** Tray menu → **Position** → choose a corner.
- **Preferences text/layout looks wrong?** Make sure you are running the latest build; the dialog is auto-sized and should adapt to any Windows scaling.

---

## Roadmap / ideas

- Cache the active adapter and refresh it only on `NetworkChange` events (lower CPU wake-ups).
- Redraw only when the displayed text changes.
- Pause polling while the bar is hidden, the session is locked, or the PC is suspended.

---
