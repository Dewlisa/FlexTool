# FlexTool
All-in-one RimWorld mod manager, pawn extractor, and performance suite
# FlexTool

**All-in-one RimWorld mod manager, pawn extractor, and performance suite**

A desktop companion app that makes managing RimWorld mods easier than ever. Install mods with one click, move colonists between saves, boost FPS, and access live performance diagnostics — all from a single application.

## ✨ Features

- **One-Click Mod Installation** — Deploy any mod instantly without manual folder management
- **Auto-Load Saves** — Launch RimWorld straight into your favorite save on startup
- **Pawn Extractor (Live)** — Move colonists between saves without editing save files
- **Performance Monitoring** — Real-time FPS, memory, and tick-rate tracking
- **Crash Protection** — Auto-detects freezes and recovers without losing progress
- **Debug Overlay** — Live in-game stats and alerts (toggle from the app)
- **Save Explorer** — Browse and edit pawns across all your saves
- **Relationship Mods** — Keep couples together and track relationship health
- **Speed Controls** — Add 4x and 5x game speed controls

## 📋 Requirements

- **Windows only**
- **.NET 10 Runtime** (free download, link provided in app)
- **Harmony mod** (free, Steam Workshop link in app)
- **RimWorld 1.3, 1.4, 1.5, or 1.6**

## 🚀 Build Instructions

### Prerequisites
- Visual Studio 2022 Community (or later)
- .NET 10 SDK
- .NET Framework 4.7.2 (for mod projects)

### Build Release

```powershell
cd FlexTool
dotnet build -c Release
```

### Output Locations

- **Desktop App:** `FlexTool\bin\Release\net10.0-windows\FlexTool.exe`
- **Mod DLLs:** Located in respective project folders under `bin\Release`

### Package for Distribution

```powershell
# Run the release build, then zip the output:
# FlexTool\bin\Release\net10.0-windows\ + all mod subfolders
```

## 📁 Project Structure

```
FlexTool/
├── FlexTool/                    # Desktop WPF app (.NET 10)
│   ├── MainWindow.xaml.cs
│   ├── RimWorldSaveReader.cs    # Core IPC & mod deployment
│   ├── MainWindow.Mods.cs       # Mod manager UI
│   └── ...
├── FlexTool.AutoLoadMod/        # Auto-load companion DLL (.NET 4.7.2)
├── FlexTool.SpeedMod/           # Speed controls mod
├── FlexTool.CheatsMod/          # Cheat actions mod
├── FlexTool.DebugInfoMod/       # Debug overlay & crash guard
├── FlexTool.FPSOptimizer/       # Performance tuning mod
├── FlexTool.KeepItTogetherMod/  # Relationship health mod
├── FlexTool.TillDeathMod/       # Breakup prevention mod
└── FlexTool.PerfMod/            # Standalone performance mod
```

## 🔧 Key Components

- **RimWorldSaveReader.cs** — Handles save file I/O, pawn extraction, mod deployment, and IPC communication
- **MainWindow.*.cs** — UI pages for Mods, Saves, Dashboard, Analytics, Debug, etc.
- **DebugInfoMod.cs** — In-game overlay, crash guard, dev-log mirroring, live pawn IPC
- **Harmony Patches** — All mods use Harmony for non-invasive game patching



## 🐛 Bug Reports & Features

Found a bug or have a feature request? Open an issue on GitHub or post on Nexus Mods.


## 🙏 Credits

- **Harmony** — For the amazing patching framework
- **RimWorld modding community** — For inspiration and feedback


---

**FlexTool — Manage your mods, manage your colonies, manage your life. 🎮**
