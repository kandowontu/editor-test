# Famidash Editor - Godot Port

Cross-platform tile and sprite editor for Famidash levels, built with Godot 4.3 + C#.

## Installation & Setup

### Prerequisites

1. **Godot 4.3+ with .NET support**
   - Download from: https://godotengine.org/download/
   - **Important**: Get the **.NET version** (not the standard version)
   - Linux users: Install via package manager or download from website

2. **.NET 8.0 SDK**
   - Download from: https://dotnet.microsoft.com/download/dotnet/8.0
   - Windows: Use the installer
   - Linux: Follow distro-specific instructions (apt, dnf, pacman, etc.)

### Linux Setup

#### Ubuntu/Debian:
```bash
# Install .NET 8.0
sudo apt-get update
sudo apt-get install -y dotnet-sdk-8.0

# Download Godot .NET (or use package manager if available)
wget https://github.com/godotengine/godot/releases/download/4.3-stable/Godot_v4.3-stable_mono_linux_x86_64.zip
unzip Godot_v4.3-stable_mono_linux_x86_64.zip
chmod +x Godot_v4.3-stable_mono_linux_x86_64
```

#### Fedora:
```bash
sudo dnf install dotnet-sdk-8.0
# Then download Godot .NET from godotengine.org
```

#### Arch:
```bash
sudo pacman -S dotnet-sdk
# Then download Godot .NET from godotengine.org or AUR
```

### Windows Setup

1. Install .NET 8.0 SDK from Microsoft
2. Download and install Godot 4.3 .NET version
3. Open the project in Godot Editor

## Running the Editor

1. Open Godot Engine
2. Click "Import" and navigate to `godot-port/project.godot`
3. Click "Import & Edit"
4. Press F5 or click the Play button to run

## Project Structure

```
godot-port/
├── project.godot          # Godot project config
├── scenes/                # Scene files (.tscn)
│   ├── MainEditor.tscn   # Main editor window
│   ├── TilePalette.tscn  # Tile selection palette
│   └── MapCanvas.tscn    # Map editing canvas
├── scripts/               # C# scripts
│   ├── MainEditor.cs     # Main editor logic
│   ├── MapData.cs        # Map data structure
│   ├── TilesetManager.cs # Tileset loading/management
│   └── UndoRedo.cs       # Undo/redo system
├── assets/                # Shared assets (symlinked from parent)
│   ├── tilesets/
│   ├── sprites/
│   └── portals/
└── export/                # Export templates for builds
```

## Building for Distribution

### Linux:
```bash
# From Godot editor: Project > Export > Linux/X11
# Or via command line:
godot --headless --export-release "Linux/X11" famidash-editor.x86_64
```

### Windows:
```bash
# From Godot editor: Project > Export > Windows Desktop
godot --headless --export-release "Windows Desktop" famidash-editor.exe
```

## Features Ported

- [x] Basic tile/sprite editing
- [x] Multi-layer support (tiles + sprites)
- [x] Undo/redo system
- [x] TMX file import/export
- [ ] Preview mode animations
- [ ] Audio playback (FamiStudio integration)
- [ ] Advanced drawing tools (shapes, fill, etc.)

## Development

### Hot Reload
Godot supports C# hot-reload. Make changes to scripts and press F5 to test immediately.

### Debugging
Use Visual Studio Code with the Godot extension, or Visual Studio 2022 with Godot plugin.

## Differences from WPF Version

- **Cross-platform**: Runs on Windows, Linux, and macOS
- **Simpler UI**: Uses Godot's built-in UI system instead of XAML
- **Better performance**: Hardware-accelerated rendering
- **Easier asset management**: Direct PNG support, no embedding needed

## Keeping Both Versions in Sync

1. **Shared Assets**: Use symlinks or junction points to share asset folders
2. **TMX Format**: Both versions use the same TMX file format
3. **Core Logic**: Port C# logic classes to be framework-agnostic
4. **Version Control**: Track both `native-windows/` and `godot-port/` in same repo
