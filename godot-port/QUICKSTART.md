# Famidash Editor - Godot Port Quick Start

## Installation (5 Minutes)

### Windows

1. **Install .NET 8.0 SDK**
   - Download: https://dotnet.microsoft.com/download/dotnet/8.0
   - Run installer, use defaults

2. **Install Godot 4.5 .NET**
   - Download: https://godotengine.org/download/windows/
   - **IMPORTANT**: Get the **.NET/Mono** version (not standard)
   - Extract anywhere (e.g., `C:\Godot`)
   - Optional: Add to PATH for command-line use

3. **Open Project**
   ```powershell
   # Navigate to godot-port folder
   cd "C:\Editor Test\godot-port"
   
   # Launch Godot (adjust path if needed)
   & "C:\Godot\Godot_v4.5-stable_mono_win64.exe" --editor
   ```
   
   Or double-click `Godot_v4.5-stable_mono_win64.exe` and import the project

### Linux (Ubuntu/Debian)

```bash
# Install .NET 8.0
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb
sudo apt-get update
sudo apt-get install -y dotnet-sdk-8.0

# Download Godot .NET
cd ~/Downloads
wget https://github.com/godotengine/godot/releases/download/4.5-stable/Godot_v4.5-stable_mono_linux_x86_64.zip
unzip Godot_v4.5-stable_mono_linux_x86_64.zip
chmod +x Godot_v4.5-stable_mono_linux_x86_64

# Move to /opt (optional but recommended)
sudo mv Godot_v4.5-stable_mono_linux_x86_64 /opt/godot
sudo ln -s /opt/godot/Godot_v4.5-stable_mono_linux_x86_64 /usr/local/bin/godot

# Open project
cd "/path/to/Editor Test/godot-port"
godot --editor
```

### Arch Linux

```bash
# Install via AUR or manually
yay -S godot-mono dotnet-sdk

# Or download manually:
sudo pacman -S dotnet-sdk
wget https://github.com/godotengine/godot/releases/download/4.3-stable/Godot_v4.3-stable_mono_linux_x86_64.zip
# ... extract and run
```

### Fedora

```bash
sudo dnf install dotnet-sdk-8.0

# Download Godot .NET manually from godotengine.org
```

## First Run

1. **Import Project**
   - In Godot Project Manager, click "Import"
   - Navigate to `godot-port/project.godot`
   - Click "Import & Edit"

2. **Initial Build**
   - Godot will automatically build the C# project
   - Wait for "Build Successful" message
   - If errors appear, check that .NET 8.0 SDK is installed

3. **Run Editor**
   - Press F5 or click Play button
   - You should see the main editor window

## Linking Assets from Parent Directory

The Godot port can share assets with the WPF version:

### Windows (PowerShell as Admin)
```powershell
cd "C:\Editor Test\godot-port\assets"
New-Item -ItemType Junction -Name "tilesets" -Target "C:\Editor Test\tilesets"
New-Item -ItemType Junction -Name "sprites.png" -Target "C:\Editor Test\sprites.png"
New-Item -ItemType Junction -Name "famidash.bmp" -Target "C:\Editor Test\famidash.bmp"
```

### Linux/macOS
```bash
cd "/path/to/Editor Test/godot-port/assets"
ln -s ../tilesets tilesets
ln -s ../sprites.png sprites.png
ln -s ../famidash.bmp famidash.bmp
```

## Development Workflow

### Hot Reload
- Edit C# files in your favorite editor (VS Code, Rider, Visual Studio)
- Godot automatically recompiles when files change
- Press F5 to test changes

### Debugging
1. **VS Code**: Install "C# Dev Kit" and "godot-tools" extensions
2. **Rider**: Install Godot plugin
3. **Visual Studio**: Install Godot plugin from Extensions

### Building for Release

#### Linux Binary
```bash
# From Godot editor:
# Project > Export > Add... > Linux/X11
# Configure export settings
# Project > Export > Export Project

# Or via command line:
godot --headless --export-release "Linux/X11" ../builds/famidash-editor-linux.x86_64
```

#### Windows Binary
```bash
# From Godot editor:
# Project > Export > Add... > Windows Desktop
# Project > Export > Export Project

# Command line:
godot --headless --export-release "Windows Desktop" ../builds/famidash-editor-windows.exe
```

## Troubleshooting

### "MSBuild not found"
- Reinstall .NET 8.0 SDK
- On Linux, ensure `dotnet` is in PATH: `which dotnet`

### "Cannot load assembly"
- Click "Build" in Godot editor (top-right)
- Check Output panel for errors

### Assets not loading
- Verify asset paths in `TilesetManager.cs`
- Check that files exist in `assets/` folder or parent directory

### Performance issues
- Disable V-Sync: Project Settings > Display > Window > V-Sync Mode = Disabled
- Enable FPS display: Debug > Visible Collision Shapes

## Next Steps

1. **Port TMX import/export** from WPF version
2. **Implement map canvas rendering** with zoom/pan
3. **Add preview mode** with animations
4. **Port advanced tools** (shapes, fill, etc.)
5. **Add FamiStudio integration** for audio

See `README.md` for full feature roadmap.
