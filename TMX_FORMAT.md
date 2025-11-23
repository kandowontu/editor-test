# TMX Format Support for Famidash Editor

The Famidash Editor now supports loading and saving levels in the Tiled Map Editor (.TMX) format, compatible with Tiled 1.11.2.

## Features

### Loading TMX Files
- **File Format**: XML-based Tiled Map files (.tmx)
- **Tile Data**: CSV-encoded tile layers
- **Map Dimensions**: Automatically reads width and height
- **Parallax Layers**: Reads parallax image layer settings (parallaxX, parallaxY, repeat modes)
- **Ground Layers**: Reads ground image layer settings (offsetY, repeat modes)

### Saving TMX Files
- **Standard Format**: Saves in Tiled 1.11.2 compatible format
- **CSV Encoding**: Tile data saved in comma-separated format
- **Tilesets**: Automatically includes Famidash and Sprites tilesets
  - `famidash` tileset (firstgid=1): 256 tiles, 16x16 columns
  - `sprites` tileset (firstgid=257): 256 tiles, 16x16 columns
- **Image Layers**: Includes parallax and ground layers with proper attributes

## Usage

### Loading a TMX File
1. Click the **Load** button
2. Select "Tiled Map (TMX)" from the file type filter
3. Browse to your TMX file (e.g., from `c:\famidash-new\levels\level data\lvlset_HUGE\`)
4. The level will be loaded with tile data and dimensions

### Saving a TMX File
1. Click the **Save** button
2. Select "Tiled Map (TMX)" from the file type filter
3. Enter a filename with `.tmx` extension
4. The level will be saved in Tiled-compatible format

### File Type Filters
The Load/Save dialogs support multiple formats:
- **Tiled Map (TMX)** - *.tmx (recommended for Tiled compatibility)
- **JSON level** - *.json (simple JSON format)
- **All files** - *.*

## TMX Structure

The saved TMX files include:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<map version="1.10" tiledversion="1.11.2" 
     orientation="orthogonal" renderorder="right-down" 
     width="[map width]" height="[map height]" 
     tilewidth="16" tileheight="16" infinite="0">
  
  <!-- Famidash Tileset -->
  <tileset firstgid="1" name="famidash" ...>
    <image source="../../../GRAPHICS/famidash Red.bmp" />
  </tileset>
  
  <!-- Sprites Tileset -->
  <tileset firstgid="257" name="sprites" ...>
    <image source="../../../GRAPHICS/sprites.png" />
  </tileset>
  
  <!-- Parallax Background Layer -->
  <imagelayer id="3" name="Image Layer 1" 
              parallaxx="0.9" parallaxy="0.9" 
              repeatx="1" repeaty="1">
    <image source="../../../GRAPHICS/Old/parallax Red.bmp" />
  </imagelayer>
  
  <!-- Ground Layer -->
  <imagelayer id="4" name="Image Layer 2" 
              offsetx="0" offsety="432" repeatx="1">
    <image source="../../../GRAPHICS/ground Red.bmp" />
  </imagelayer>
  
  <!-- Main Tile Layer -->
  <layer id="1" width="[width]" height="[height]">
    <data encoding="csv">
      [tile data as CSV]
    </data>
  </layer>
</map>
```

## Default Values

When saving, the following defaults are used:

### Parallax Layer
- **Source**: `../../../GRAPHICS/Old/parallax Red.bmp`
- **Parallax X**: 0.9
- **Parallax Y**: 0.9
- **Repeat X**: true
- **Repeat Y**: true
- **Dimensions**: 144x72

### Ground Layer
- **Source**: `../../../GRAPHICS/ground Red.bmp`
- **Offset Y**: 432 (pixels below map area)
- **Repeat X**: true
- **Dimensions**: 64x128

## Compatibility

### Tiled Map Editor
The saved TMX files can be opened directly in Tiled Map Editor 1.11.2 or later. The files use:
- Standard XML structure
- CSV encoding for tile data
- Orthogonal orientation
- Right-down render order

### Famidash Levels
Example TMX files compatible with this format can be found in:
```
c:\famidash-new\levels\level data\lvlset_HUGE\
```

Files include:
- aftercatabath.tmx
- aftermath.tmx
- aprettyeasylevel.tmx
- backontrack.tmx
- baseafterbase.tmx
- ...and many more

## Technical Notes

### Tile IDs
- Tile IDs in TMX are 1-based (0 = empty)
- Famidash tiles: GID 1-256
- Sprite tiles: GID 257-512

### CSV Format
Tiles are saved with proper line breaks every row for readability:
```csv
1,1,1,110,112,112,...
1,1,1,110,112,112,...
```

### Image Paths
Image paths in the TMX file use relative paths pointing to the GRAPHICS folder:
- `../../../GRAPHICS/famidash Red.bmp`
- `../../../GRAPHICS/sprites.png`
- `../../../GRAPHICS/Old/parallax Red.bmp`
- `../../../GRAPHICS/ground Red.bmp`

Adjust these paths as needed based on your project structure.

## Future Enhancements

Potential improvements for TMX support:
- [ ] Custom parallax/ground image selection in UI
- [ ] Object layer support for game entities
- [ ] Multiple tile layers
- [ ] Tile properties and metadata
- [ ] Auto-detect and apply loaded parallax/ground images
- [ ] Export to other formats (base64, zlib compressed)
