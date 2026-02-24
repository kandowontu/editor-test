using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace FamidashEditor
{
    public static class TmxHandler
    {
        // TMX GID base values — change these if your TMX uses a different firstgid for sprites/tiles
        private const int TilesFirstGid = 1;    // TMX GID for first famidash tile
        private const int SpriteFirstGid = 257; // TMX GID for first sprite tile
        private const int TilesCount = 256;     // Number of tiles/sprites per tileset

        /// <summary>
        /// Returns true for sprite IDs that have real collision geometry
        /// (pads, orbs, portals, coins, etc.).  Used during TMX loading to
        /// prevent deco/color/outline sprites from overwriting interactive
        /// sprites that happen to share a tile position.
        /// </summary>
        private static bool IsInteractiveSprite(int sid)
        {
            if (sid < 0 || sid > 0xFF) return false;
            // Deco (height=0xFE): 0x2A-0x43, 0x49-0x4A
            // COLR (height=0xFD): 0x80-0x8D,0x8F-0x9D,0x9F-0xAD,0xAE-0xAF, 0xC0-0xEF range
            // OUTL (height=0xFC): 0xAF-0xBF
            // SPBH (height=0xFF): sub-parts / hidden / trigger positions
            // Anything that is NOT in one of these categories is interactive.
            // Use the same height table sentinel values as SimulatorWindow.
            int[] heights = {
                0x34,0x34,0x34,0x34,0x34,0x12,0x12,0xFF, // 00-07
                0x28,0x28,0x03,0x12,0x03,0x03,0x03,0xFF, // 08-0F
                0x0e,0x0e,0x0e,0x0e,0x24,0x24,0x24,0x34, // 10-17
                0x34,0x34,0xFF,0xFF,0xFF,0xFF,0xFF,0x12, // 18-1F
                0x24,0x24,0x34,0x34,0x34,0x03,0x03,0x12, // 20-27
                0x12,0x12,0xFE,0xFE,0xFE,0xFE,0xFE,0xFE, // 28-2F
                0xFE,0xFE,0xFE,0xFE,0xFE,0xFE,0xFE,0xFE, // 30-37
                0xFE,0xFE,0xFE,0xFE,0xFE,0xFE,0xFE,0xFE, // 38-3F
                0xFE,0xFE,0xFE,0xFE,0x12,0x12,0x12,0x28, // 40-47
                0x28,0xFE,0xFE,0x34,0x12,0x12,0x30,0xFF, // 48-4F
                0x12,0x12,0x03,0x03,0x12,0x12,0x03,0x03, // 50-57
                0x34,0x10,0xFF,0x12,0x12,0x12,0x12,0x34, // 58-5F
                0x34,0x34,0x34,0x34,0x34,0x02,0x10,0xFF, // 60-67
                0x10,0xFF,0x34,0x34,0x34,0x20,0x08,0xFF, // 68-6F
                0xFF,0xFF,0xFF,0xFF,0xFF,0x10,0xFF,0x10, // 70-77
                0xFF,0x12,0x12,0x12,0x12,0xFF,0xFF,0xFF, // 78-7F
                0xFD,0xFD,0xFD,0xFD,0xFD,0xFD,0xFD,0xFD, // 80-87
                0xFD,0xFD,0xFD,0xFD,0xFD,0x00,0xFF,0xFD, // 88-8F
                0xFD,0xFD,0xFD,0xFD,0xFD,0xFD,0xFD,0xFD, // 90-97
                0xFD,0xFD,0xFD,0xFD,0xFD,0x00,0xFF,0xFD, // 98-9F
                0xFD,0xFD,0xFD,0xFD,0xFD,0xFD,0xFD,0xFD, // A0-A7
                0xFD,0xFD,0xFD,0xFD,0xFD,0x00,0xFD,0xFC, // A8-AF
                0xFC,0xFC,0xFC,0xFC,0xFC,0xFC,0xFC,0xFC, // B0-B7
                0xFC,0xFC,0xFC,0xFC,0xFC,0xFC,0xFC,0xFC, // B8-BF
                0xFD,0xFD,0xFD,0xFD,0xFD,0xFD,0xFD,0xFD, // C0-C7
                0xFD,0xFD,0xFD,0xFD,0xFD,0x00,0x00,0xFD, // C8-CF
                0xFD,0xFD,0xFD,0xFD,0xFD,0xFD,0xFD,0xFD, // D0-D7
                0xFD,0xFD,0xFD,0xFD,0xFD,0xFF,0xFF,0x00, // D8-DF
                0xFD,0xFD,0xFD,0xFD,0xFD,0xFD,0xFD,0xFD, // E0-E7
                0xFD,0xFD,0xFD,0xFD,0xFD,0xFF,0x00,0x00, // E8-EF
                0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0x10,0x10, // F0-F7
                0x10,0x10,0x1F,0x10,0x10,0x03,0x03,0x00  // F8-FF
            };
            int h = heights[sid];
            // Interactive = height < 0xFC (real collision geometry)
            return h < 0xFC;
        }

        // Check if a sprite index is a trigger sprite that needs position offsetting
        private static bool IsTriggerSprite(int spriteIdx)
        {
            if (spriteIdx < 0) return false;
            
            return spriteIdx == 0x0F ||
                   spriteIdx == 0x6F ||
                   (spriteIdx >= 0x70 && spriteIdx <= 0x74) ||
                   spriteIdx == 0x7D ||
                   spriteIdx == 0x7F ||
                   (spriteIdx >= 0x80 && spriteIdx <= 0xEF) ||
                   (spriteIdx >= 0xF0 && spriteIdx <= 0xF5);
        }
        
        public static TmxLevel LoadTmx(string filePath, bool useLegacyTriggerOffset = false)
        {
            var doc = XDocument.Load(filePath);
            var map = doc.Element("map");
            
            if (map == null)
                throw new Exception("Invalid TMX file: no map element");

            int width = (int?)map.Attribute("width") ?? 0;
            int height = (int?)map.Attribute("height") ?? 0;
            int totalTiles = width * height;
            
            // Track collision warnings
            var collisionWarnings = new System.Collections.Generic.List<string>();
            
            // Read tileset sources
            string? tilesetSource = null;
            string? spritesetSource = null;
            var tilesets = map.Elements("tileset").ToList();

            // Collect all tileset firstgid values that should be treated as sprite tilesets.
            // TMX files sometimes include the same sprite image as multiple tilesets
            // (e.g. firstgid=257 and firstgid=513). Treat any tileset with firstgid >= SpriteFirstGid
            // as a sprite tileset so that GIDs from those ranges are converted correctly.
            var spriteFirstGids = new System.Collections.Generic.List<int>();

            foreach (var tileset in tilesets)
            {
                int firstgid = (int?)tileset.Attribute("firstgid") ?? 0;
                var imageElem = tileset.Element("image");
                if (imageElem != null)
                {
                    string? source = (string?)imageElem.Attribute("source");
                    if (firstgid == TilesFirstGid) tilesetSource = source;
                    // Prefer to remember the primary spriteset source (first encountered)
                    if (firstgid >= SpriteFirstGid)
                    {
                        spriteFirstGids.Add(firstgid);
                        if (spritesetSource == null) spritesetSource = source;
                    }
                }
            }

            // Ensure there is at least the configured SpriteFirstGid in the list so
            // old TMX files without extra tileset declarations still convert correctly.
            if (!spriteFirstGids.Contains(SpriteFirstGid))
                spriteFirstGids.Insert(0, SpriteFirstGid);
            
            // Initialize separate tiles and sprites arrays with -1 (empty)
            int[] tiles = Enumerable.Repeat(-1, totalTiles).ToArray();
            int[] sprites = Enumerable.Repeat(-1, totalTiles).ToArray();
            
            // Track sprite collisions during loading
            var collisionMessages = new System.Collections.Generic.List<string>();
            
            
            // First pass: collect all sprites with their intended positions (after trigger shift)
            var spritesToPlace = new System.Collections.Generic.List<(int spriteIdx, int x, int y, int originalX, int originalY, bool isTrigger)>();
            
            // Process tile layers separately
            // TMX uses GIDs: 0=empty, TilesFirstGid..TilesFirstGid+TilesCount-1 = tileset, SpriteFirstGid..SpriteFirstGid+TilesCount-1 = sprites tileset
            // Convert to editor format: -1=empty, 0..TilesCount-1 = famidash tiles/sprites
            var layers = map.Elements("layer").ToList();
            
            foreach (var layer in layers)
            {
                string? layerName = (string?)layer.Attribute("name");
                bool isSpriteLayer = layerName?.Equals("SP", StringComparison.OrdinalIgnoreCase) == true;
                
                var dataElement = layer.Element("data");
                if (dataElement != null)
                {
                    string? encoding = (string?)dataElement.Attribute("encoding");
                    if (encoding == "csv")
                    {
                        string csvData = dataElement.Value.Trim();
                        var layerTiles = csvData.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                               .Select(s => int.Parse(s.Trim()))
                                               .ToArray();
                        
                        for (int i = 0; i < Math.Min(layerTiles.Length, totalTiles); i++)
                        {
                            int gid = layerTiles[i];
                            if (gid > 0)
                            {
                                // Prefer interpreting any GID in the sprite tileset range as a sprite,
                                // regardless of the layer name. This allows maps that embed sprites
                                // directly into the main tile layer to be imported correctly.
                                // Check against all known sprite tileset firstgid values
                                bool handled = false;
                                foreach (var spriteFg in spriteFirstGids)
                                {
                                    if (gid >= spriteFg && gid < spriteFg + TilesCount)
                                    {
                                        int spriteIdx = gid - spriteFg;

                                        // Calculate position in grid
                                        int y = i / width;
                                        int x = i % width;
                                        int originalX = x;
                                        int originalY = y;

                                        // Trigger sprites are stored 10 tiles to the right in TMX,
                                        // but displayed 10 tiles to the left in editor (unless legacy mode enabled)
                                        bool isTrigger = !useLegacyTriggerOffset && IsTriggerSprite(spriteIdx);
                                        if (isTrigger)
                                        {
                                            x -= 10; // Shift left
                                            if (x < 0) x = 0; // Clamp to left boundary instead of skipping
                                        }

                                        // Add to list for collision resolution (allow multiple sprites at same TMX index)
                                        spritesToPlace.Add((spriteIdx, x, y, originalX, originalY, isTrigger));
                                        handled = true;
                                        break;
                                    }
                                }

                                // If no sprite-firstgid matched, treat as a tile if it falls in the tile range
                                if (!handled && gid >= TilesFirstGid && gid < TilesFirstGid + TilesCount)
                                {
                                    // Tile layer: GID TilesFirstGid..TilesFirstGid+TilesCount-1 → editor index 0..TilesCount-1
                                    tiles[i] = gid - TilesFirstGid;
                                }
                                // else: GID is outside recognized ranges; ignore
                            }
                        }
                    }
                    else
                    {
                        throw new Exception("Only CSV encoding is supported");
                    }
                }
            }
            
            // Separate normal sprites and trigger sprites
            var normalSprites = spritesToPlace.Where(s => !s.isTrigger).ToList();
            var triggerSprites = spritesToPlace.Where(s => s.isTrigger).ToList();
            
            // Sort both lists by position (top-to-bottom, left-to-right)
            normalSprites.Sort((a, b) =>
            {
                int cmp = a.y.CompareTo(b.y);
                if (cmp != 0) return cmp;
                return a.x.CompareTo(b.x);
            });
            
            // Place triggers left-to-right to avoid cascading overlaps from right-to-left processing.
            triggerSprites.Sort((a, b) =>
            {
                int cmp = a.x.CompareTo(b.x);
                if (cmp != 0) return cmp;
                return a.y.CompareTo(b.y);
            });
            
            // First pass: place all normal sprites.
            // If a non-interactive sprite (deco/color/outline) would overwrite
            // an interactive sprite (pad/orb/portal/coin), skip the overwrite
            // so interactive sprites always take priority.
            foreach (var (spriteIdx, x, y, originalX, originalY, isTrigger) in normalSprites)
            {
                int newIdx = y * width + x;
                
                if (newIdx >= 0 && newIdx < totalTiles)
                {
                    if (sprites[newIdx] != -1)
                    {
                        int existingSprite = sprites[newIdx];
                        bool existingIsInteractive = IsInteractiveSprite(existingSprite);
                        bool newIsInteractive = IsInteractiveSprite(spriteIdx);

                        // Don't let a non-interactive sprite overwrite an interactive one
                        if (existingIsInteractive && !newIsInteractive)
                        {
                            collisionMessages.Add($"NORMAL Sprite 0x{spriteIdx:X2} (deco) at TMX({originalX},{originalY}) SKIPPED — would overwrite interactive sprite 0x{existingSprite:X2} at ({x},{y})");
                            continue;
                        }

                        collisionMessages.Add($"NORMAL Sprite 0x{spriteIdx:X2} at TMX({originalX},{originalY}) OVERWRITES existing sprite 0x{existingSprite:X2} at ({x},{y})");
                    }
                    
                    sprites[newIdx] = spriteIdx;
                }
            }
            
            // Second pass: place trigger sprites and resolve collisions
            // Second pass: place trigger sprites and resolve collisions
            // Determine playable rows: last row (height-1) is ground level and must not be used.
            int maxPlayableRow = Math.Max(0, height - 2);

            // Helper: attempt to place a trigger in `col` at `row`. If occupied by another trigger,
            // recursively push that trigger down or up (depending on preference) to make room.
            // Returns placed row or -1.
            System.Collections.Generic.HashSet<int> pushVisited = new System.Collections.Generic.HashSet<int>();
            System.Func<int,int,int,bool,int> PlaceTriggerWithPush = null!;
            PlaceTriggerWithPush = (int col, int row, int spriteId, bool preferUpFirst) =>
            {
                // Clamp row into playable range (do not allow placement on ground row)
                int r = Math.Max(0, Math.Min(maxPlayableRow, row));

                int idx = r * width + col;
                // If empty, place directly
                if (sprites[idx] == -1)
                {
                    sprites[idx] = spriteId;
                    return r;
                }

                // If occupied by non-trigger, try to find nearest empty row for the current sprite (no replacement)
                int occupant = sprites[idx];
                if (!IsTriggerSprite(occupant))
                {
                    // Search outward from row: prefer direction based on preferUpFirst
                    for (int d = 0; d <= maxPlayableRow; d++)
                    {
                        if (preferUpFirst)
                        {
                            int up = row - d;
                            if (up >= 0 && up <= maxPlayableRow)
                            {
                                int uidx = up * width + col;
                                if (sprites[uidx] == -1)
                                {
                                    sprites[uidx] = spriteId; return up;
                                }
                            }
                            if (d == 0) continue;
                            int down = row + d;
                            if (down >= 0 && down <= maxPlayableRow)
                            {
                                int didx = down * width + col;
                                if (sprites[didx] == -1)
                                {
                                    sprites[didx] = spriteId; return down;
                                }
                            }
                        }
                        else
                        {
                            int down = row + d;
                            if (down >= 0 && down <= maxPlayableRow)
                            {
                                int didx = down * width + col;
                                if (sprites[didx] == -1)
                                {
                                    sprites[didx] = spriteId; return down;
                                }
                            }
                            if (d == 0) continue;
                            int up = row - d;
                            if (up >= 0 && up <= maxPlayableRow)
                            {
                                int uidx = up * width + col;
                                if (sprites[uidx] == -1)
                                {
                                    sprites[uidx] = spriteId; return up;
                                }
                            }
                        }
                    }
                    return -1;
                }

                // Occupied by another trigger: attempt to push it with preference.
                int visitKey = (col << 16) | r;
                if (pushVisited.Contains(visitKey)) return -1;
                pushVisited.Add(visitKey);

                int pushedSprite = sprites[idx];
                // Try pushing in preferred order: either up-first or down-first, but never beyond maxPlayableRow.
                if (preferUpFirst)
                {
                    for (int d = 1; d <= maxPlayableRow; d++)
                    {
                        int upRow = r - d;
                        if (upRow >= 0)
                        {
                            int placed = PlaceTriggerWithPush(col, upRow, pushedSprite, preferUpFirst);
                            if (placed >= 0)
                            {
                                sprites[idx] = spriteId;
                                pushVisited.Remove(visitKey);
                                return r;
                            }
                        }
                        int downRow = r + d;
                        if (downRow <= maxPlayableRow)
                        {
                            int placed = PlaceTriggerWithPush(col, downRow, pushedSprite, preferUpFirst);
                            if (placed >= 0)
                            {
                                sprites[idx] = spriteId;
                                pushVisited.Remove(visitKey);
                                return r;
                            }
                        }
                    }
                }
                else
                {
                    for (int d = 1; d <= maxPlayableRow; d++)
                    {
                        int downRow = r + d;
                        if (downRow <= maxPlayableRow)
                        {
                            int placed = PlaceTriggerWithPush(col, downRow, pushedSprite, preferUpFirst);
                            if (placed >= 0)
                            {
                                sprites[idx] = spriteId;
                                pushVisited.Remove(visitKey);
                                return r;
                            }
                        }
                        int upRow = r - d;
                        if (upRow >= 0)
                        {
                            int placed = PlaceTriggerWithPush(col, upRow, pushedSprite, preferUpFirst);
                            if (placed >= 0)
                            {
                                sprites[idx] = spriteId;
                                pushVisited.Remove(visitKey);
                                return r;
                            }
                        }
                    }
                }

                // Do NOT remove visitKey on failure — acts as memoization to prevent
                // factorial-time re-exploration of cells that already proved unpushable.
                // pushVisited.Remove(visitKey);  // intentionally removed to fix O(H!) freeze
                return -1;
            };

            foreach (var (spriteIdx, x, y, originalX, originalY, isTrigger) in triggerSprites)
            {
                int col = Math.Max(0, Math.Min(width - 1, x));
                int placed = -1;

                // Determine if this trigger is within the bottom 8 gameplay rows (from bottom up, excluding ground row)
                bool preferUpFirst = false;
                try
                {
                    int bottomGameplayStart = Math.Max(0, height - 1 - 8); // inclusive
                    int bottomGameplayEnd = Math.Max(0, height - 2); // inclusive
                    if (y >= bottomGameplayStart && y <= bottomGameplayEnd) preferUpFirst = true;
                }
                catch { preferUpFirst = false; }

                pushVisited.Clear();
                placed = PlaceTriggerWithPush(col, y, spriteIdx, preferUpFirst);
                if (placed >= 0)
                {
                    if (placed != y) collisionMessages.Add($"TRIGGER Sprite 0x{spriteIdx:X2} at TMX({originalX},{originalY}) → Placed at ({col},{placed})");
                }
                else
                {
                    // As a last resort, scan the column for any empty slot top-to-bottom but only within playable rows
                    bool found = false;
                    for (int r = 0; r <= maxPlayableRow; r++)
                    {
                        int tidx = r * width + col;
                        if (sprites[tidx] == -1)
                        {
                            sprites[tidx] = spriteIdx; collisionMessages.Add($"TRIGGER Sprite 0x{spriteIdx:X2} at TMX({originalX},{originalY}) → Placed at ({col},{r}) [fallback]"); found = true; break;
                        }
                    }
                    if (!found)
                    {
                        // Could not place in the target column. Do NOT scatter triggers
                        // to arbitrary map positions — placing an end-level trigger (0x0F)
                        // at (0,0) would make the pathfinder think the level ends immediately.
                        // Instead, try the adjacent column (col ± 1) as a last resort.
                        bool found2 = false;
                        for (int dc = 1; dc <= 2 && !found2; dc++)
                        {
                            foreach (int tryCol in new[] { col + dc, col - dc })
                            {
                                if (tryCol < 0 || tryCol >= width) continue;
                                for (int r = 0; r <= maxPlayableRow; r++)
                                {
                                    int tidx = r * width + tryCol;
                                    if (sprites[tidx] == -1)
                                    {
                                        sprites[tidx] = spriteIdx;
                                        collisionMessages.Add($"TRIGGER Sprite 0x{spriteIdx:X2} at TMX({originalX},{originalY}) → Placed at ({tryCol},{r}) [adjacent col fallback]");
                                        found2 = true; break;
                                    }
                                }
                                if (found2) break;
                            }
                        }
                        if (!found2)
                        {
                            // Drop the trigger entirely rather than placing at an unrelated position
                            collisionMessages.Add($"TRIGGER Sprite 0x{spriteIdx:X2} at TMX({originalX},{originalY}) → DROPPED (column {col} full, no adjacent space)");
                        }
                    }
                }
            }

            // Extract parallax layer info
            var parallaxLayer = map.Elements("imagelayer")
                                   .FirstOrDefault(l => ((string?)l.Attribute("name"))?.Contains("parallax", StringComparison.OrdinalIgnoreCase) == true 
                                                     || ((string?)l.Attribute("name"))?.Contains("Image Layer 1") == true);
            string? parallaxSource = null;
            double parallaxX = 1.0;
            double parallaxY = 1.0;
            bool parallaxRepeatX = false;
            bool parallaxRepeatY = false;
            
            if (parallaxLayer != null)
            {
                parallaxX = (double?)parallaxLayer.Attribute("parallaxx") ?? 1.0;
                parallaxY = (double?)parallaxLayer.Attribute("parallaxy") ?? 1.0;
                parallaxRepeatX = ((int?)parallaxLayer.Attribute("repeatx") ?? 0) == 1;
                parallaxRepeatY = ((int?)parallaxLayer.Attribute("repeaty") ?? 0) == 1;
                
                var imageElem = parallaxLayer.Element("image");
                if (imageElem != null)
                {
                    parallaxSource = (string?)imageElem.Attribute("source");
                }
            }

            // Extract ground layer info
            var groundLayer = map.Elements("imagelayer")
                                 .FirstOrDefault(l => ((string?)l.Attribute("name"))?.Contains("ground", StringComparison.OrdinalIgnoreCase) == true
                                                   || ((string?)l.Attribute("name"))?.Contains("Image Layer 2") == true);
            string? groundSource = null;
            double groundOffsetY = 0;
            bool groundRepeatX = false;
            
            if (groundLayer != null)
            {
                groundOffsetY = (double?)groundLayer.Attribute("offsety") ?? 0;
                groundRepeatX = ((int?)groundLayer.Attribute("repeatx") ?? 0) == 1;
                
                var imageElem = groundLayer.Element("image");
                if (imageElem != null)
                {
                    groundSource = (string?)imageElem.Attribute("source");
                }
            }

            // Extract editor settings
            var editorSettings = map.Element("editorsettings");
            bool hasEditorSettings = editorSettings != null;
            int chunkWidth = 16;
            int chunkHeight = height; // Default to map height
            string? exportTarget = null;
            string exportFormat = "csv";
            
            if (editorSettings != null)
            {
                var chunkSize = editorSettings.Element("chunksize");
                if (chunkSize != null)
                {
                    chunkWidth = (int?)chunkSize.Attribute("width") ?? 16;
                    chunkHeight = (int?)chunkSize.Attribute("height") ?? height;
                }
                
                var export = editorSettings.Element("export");
                if (export != null)
                {
                    exportTarget = (string?)export.Attribute("target");
                    exportFormat = (string?)export.Attribute("format") ?? "csv";
                }

                // Optional deco set stored in editor settings
                // deco set element handled when constructing return object below
            }

            return new TmxLevel
            {
                Width = width,
                Height = height,
                Tiles = tiles,
                Sprites = sprites,
                TilesetSource = tilesetSource,
                SpritesetSource = spritesetSource,
                HasEditorSettings = hasEditorSettings,
                ChunkWidth = chunkWidth,
                ChunkHeight = chunkHeight,
                ExportTarget = exportTarget,
                ExportFormat = exportFormat,
                ParallaxSource = parallaxSource,
                ParallaxX = parallaxX,
                ParallaxY = parallaxY,
                ParallaxRepeatX = parallaxRepeatX,
                ParallaxRepeatY = parallaxRepeatY,
                HasParallaxLayer = parallaxLayer != null,
                GroundSource = groundSource,
                GroundOffsetY = groundOffsetY,
                GroundRepeatX = groundRepeatX,
                HasGroundLayer = groundLayer != null,
                LoadCollisionMessages = collisionMessages.Count > 0 ? string.Join("\n", collisionMessages) : null,
                // read deco set if present
                DecoSet = editorSettings?.Element("decoset")?.Attribute("name")?.Value ?? "deco1"
            };
        }

        public static string? SaveTmx(string filePath, TmxLevel level, bool useLegacyTriggerOffset = false)
        {
            // Track sprite collisions during saving
            var collisionMessages = new System.Collections.Generic.List<string>();
            
            // Create the XML structure
            var map = new XElement("map",
                new XAttribute("version", "1.10"),
                new XAttribute("tiledversion", "1.11.2"),
                new XAttribute("orientation", "orthogonal"),
                new XAttribute("renderorder", "right-down"),
                new XAttribute("width", level.Width),
                new XAttribute("height", level.Height),
                new XAttribute("tilewidth", 16),
                new XAttribute("tileheight", 16),
                new XAttribute("infinite", 0),
                new XAttribute("nextlayerid", 5),
                new XAttribute("nextobjectid", 1)
            );

            // Add editor settings first (right after map element)
            var editorSettings = new XElement("editorsettings");
            
            // Chunksize element
            editorSettings.Add(new XElement("chunksize",
                new XAttribute("width", level.ChunkWidth),
                new XAttribute("height", level.ChunkHeight)
            ));
            
            // Export element
            editorSettings.Add(new XElement("export",
                new XAttribute("target", level.ExportTarget ?? "export.csv"),
                new XAttribute("format", level.ExportFormat)
            ));

            // Deco set element (editor-level preview selection)
            if (!string.IsNullOrEmpty(level.DecoSet))
            {
                editorSettings.Add(new XElement("decoset", new XAttribute("name", level.DecoSet)));
            }
            
            map.Add(editorSettings);

            // Add tilesets
            map.Add(new XElement("tileset",
                new XAttribute("firstgid", TilesFirstGid),
                new XAttribute("name", "famidash"),
                new XAttribute("tilewidth", 16),
                new XAttribute("tileheight", 16),
                new XAttribute("tilecount", 256),
                new XAttribute("columns", 16),
                new XElement("image",
                    new XAttribute("source", level.TilesetSource ?? "famidash.bmp"),
                    new XAttribute("width", 256),
                    new XAttribute("height", 256)
                )
            ));

            map.Add(new XElement("tileset",
                new XAttribute("firstgid", SpriteFirstGid),
                new XAttribute("name", "sprites"),
                new XAttribute("tilewidth", 16),
                new XAttribute("tileheight", 16),
                new XAttribute("tilecount", 256),
                new XAttribute("columns", 16),
                new XElement("image",
                    new XAttribute("source", level.SpritesetSource ?? "sprites.png"),
                    new XAttribute("width", 256),
                    new XAttribute("height", 256)
                )
            ));

            // Only add parallax image layer if it existed in the loaded file
            if (level.HasParallaxLayer && !string.IsNullOrEmpty(level.ParallaxSource))
            {
                var parallaxLayer = new XElement("imagelayer",
                    new XAttribute("id", 3),
                    new XAttribute("name", "Image Layer 1"),
                    new XAttribute("parallaxx", level.ParallaxX),
                    new XAttribute("parallaxy", level.ParallaxY)
                );
                
                if (level.ParallaxRepeatX)
                    parallaxLayer.Add(new XAttribute("repeatx", 1));
                if (level.ParallaxRepeatY)
                    parallaxLayer.Add(new XAttribute("repeaty", 1));
                
                parallaxLayer.Add(new XElement("image",
                    new XAttribute("source", level.ParallaxSource),
                    new XAttribute("width", 144),
                    new XAttribute("height", 72)
                ));
                
                map.Add(parallaxLayer);
            }

            // Only add ground image layer if it existed in the loaded file
            if (level.HasGroundLayer && !string.IsNullOrEmpty(level.GroundSource))
            {
                var groundLayer = new XElement("imagelayer",
                    new XAttribute("id", 4),
                    new XAttribute("name", "Image Layer 2"),
                    new XAttribute("offsetx", 0),
                    new XAttribute("offsety", level.GroundOffsetY)
                );
                
                if (level.GroundRepeatX)
                    groundLayer.Add(new XAttribute("repeatx", 1));
                
                groundLayer.Add(new XElement("image",
                    new XAttribute("source", level.GroundSource),
                    new XAttribute("width", 64),
                    new XAttribute("height", 128)
                ));
                
                map.Add(groundLayer);
            }

            // Add main tile layer (tiles 0-255)
            var tileLayer = new XElement("layer",
                new XAttribute("id", 1),
                new XAttribute("width", level.Width),
                new XAttribute("height", level.Height)
            );

                // Convert tiles to CSV format using TilesFirstGid and TilesCount constants
            if (level.Tiles != null && level.Tiles.Length > 0)
            {
                var csvLines = new System.Text.StringBuilder();
                for (int y = 0; y < level.Height; y++)
                {
                    for (int x = 0; x < level.Width; x++)
                    {
                        int idx = y * level.Width + x;
                        int tileValue = 0; // Default to GID 0 (empty)
                        
                        if (idx < level.Tiles.Length)
                        {
                            int editorIdx = level.Tiles[idx];
                                // Convert editor index to TMX GID
                                // Editor: -1=empty, 0..TilesCount-1=tiles → TMX: 0=empty, TilesFirstGid..TilesFirstGid+TilesCount-1=tiles
                            if (editorIdx >= 0 && editorIdx < TilesCount)
                            {
                                    tileValue = editorIdx + TilesFirstGid;
                            }
                        }
                        
                        csvLines.Append(tileValue);
                        if (x < level.Width - 1)
                            csvLines.Append(',');
                    }
                    if (y < level.Height - 1)
                        csvLines.AppendLine(",");
                }

                tileLayer.Add(new XElement("data",
                    new XAttribute("encoding", "csv"),
                    "\n" + csvLines.ToString() + "\n"
                ));
            }

            map.Add(tileLayer);

            // Add sprite layer
            var spriteLayer = new XElement("layer",
                new XAttribute("id", 2),
                new XAttribute("name", "SP"),
                new XAttribute("width", level.Width),
                new XAttribute("height", level.Height)
            );

            if (level.Sprites != null && level.Sprites.Length > 0)
            {
                // First, create a temporary array with trigger sprites shifted right by 10 tiles
                int[] spritesToSave = new int[level.Sprites.Length];
                Array.Fill(spritesToSave, -1); // Initialize with empty
                
                for (int y = 0; y < level.Height; y++)
                {
                    for (int x = 0; x < level.Width; x++)
                    {
                        int idx = y * level.Width + x;
                        if (idx >= level.Sprites.Length) continue;
                        
                        int spriteIdx = level.Sprites[idx];
                        if (spriteIdx >= 0)
                        {
                            int saveX = x;
                            int originalX = x;
                            int originalY = y;
                            
                            // Trigger sprites are displayed 10 tiles left in editor,
                            // but need to be saved 10 tiles right in TMX (unless legacy mode enabled)
                            if (!useLegacyTriggerOffset && IsTriggerSprite(spriteIdx))
                            {
                                saveX += 10; // Shift right for saving
                                if (saveX >= level.Width) continue; // Skip if out of bounds
                            }
                            
                            int saveIdx = y * level.Width + saveX;
                            
                            // Check for collision
                            if (saveIdx >= 0 && saveIdx < spritesToSave.Length)
                            {
                                if (spritesToSave[saveIdx] != -1)
                                {
                                    // Only move TRIGGER sprites when there's a collision
                                    // Normal sprites overwrite (have priority)
                                    bool isTrigger = !useLegacyTriggerOffset && IsTriggerSprite(spriteIdx);
                                    
                                    if (isTrigger)
                                    {
                                        // Collision detected for trigger - find nearest vertical neighbor
                                        int finalY = y;
                                        bool foundSlot = false;
                                        
                                        // Search up and down alternately
                                        for (int offset = 1; offset < level.Height; offset++)
                                        {
                                            // Try below first
                                            int testY = y + offset;
                                            if (testY < level.Height)
                                            {
                                                int testIdx = testY * level.Width + saveX;
                                                if (spritesToSave[testIdx] == -1)
                                                {
                                                    finalY = testY;
                                                    foundSlot = true;
                                                    break;
                                                }
                                            }
                                            
                                            // Try above
                                            testY = y - offset;
                                            if (testY >= 0)
                                            {
                                                int testIdx = testY * level.Width + saveX;
                                                if (spritesToSave[testIdx] == -1)
                                                {
                                                    finalY = testY;
                                                    foundSlot = true;
                                                    break;
                                                }
                                            }
                                        }
                                        
                                        if (foundSlot)
                                        {
                                            collisionMessages.Add($"TRIGGER Sprite 0x{spriteIdx:X2} at ({originalX},{originalY}) shifted to ({saveX},{y}) collides, saved to ({saveX},{finalY})");
                                            saveIdx = finalY * level.Width + saveX;
                                        }
                                        else
                                        {
                                            collisionMessages.Add($"TRIGGER Sprite 0x{spriteIdx:X2} at ({originalX},{originalY}) shifted to ({saveX},{y}) collides, no free vertical slot found - sprite dropped");
                                            continue; // Skip this sprite
                                        }
                                    }
                                    else
                                    {
                                        // NORMAL sprite overwrites existing sprite at this position
                                        int existingSprite = spritesToSave[saveIdx];
                                        collisionMessages.Add($"NORMAL Sprite 0x{spriteIdx:X2} at ({originalX},{originalY}) OVERWRITES existing sprite 0x{existingSprite:X2} at ({saveX},{y})");
                                    }
                                }
                                
                                spritesToSave[saveIdx] = spriteIdx;
                            }
                        }
                    }
                }
                
                // Now write the shifted sprites to CSV
                var csvLines = new System.Text.StringBuilder();
                for (int y = 0; y < level.Height; y++)
                {
                    for (int x = 0; x < level.Width; x++)
                    {
                        int idx = y * level.Width + x;
                        int tileValue = 0; // Default to GID 0 (empty)
                        
                        if (idx < spritesToSave.Length)
                        {
                            int editorIdx = spritesToSave[idx];
                            // Convert editor index to TMX GID
                            // Editor: -1=empty, 0..TilesCount-1=sprites → TMX: 0=empty, SpriteFirstGid..SpriteFirstGid+TilesCount-1=sprites
                            if (editorIdx >= 0 && editorIdx < TilesCount)
                            {
                                tileValue = editorIdx + SpriteFirstGid;
                            }
                        }
                        
                        csvLines.Append(tileValue);
                        if (x < level.Width - 1)
                            csvLines.Append(',');
                    }
                    if (y < level.Height - 1)
                        csvLines.AppendLine(",");
                }

                spriteLayer.Add(new XElement("data",
                    new XAttribute("encoding", "csv"),
                    "\n" + csvLines.ToString() + "\n"
                ));
            }

            map.Add(spriteLayer);

            // Save the document
            var doc = new XDocument(map);
            
            // Save settings for precise XML formatting
            var settings = new System.Xml.XmlWriterSettings
            {
                Encoding = new System.Text.UTF8Encoding(false), // No BOM
                Indent = true,
                IndentChars = " ", // Single space per indent
                NewLineChars = "\n", // Unix line endings
                NewLineHandling = System.Xml.NewLineHandling.Replace,
                OmitXmlDeclaration = true // We'll write it manually
            };
            
            using (var stream = new System.IO.FileStream(filePath, System.IO.FileMode.Create, System.IO.FileAccess.Write))
            using (var streamWriter = new System.IO.StreamWriter(stream, new System.Text.UTF8Encoding(false))) // No BOM
            {
                // Manually write the XML declaration with uppercase UTF-8
                streamWriter.Write("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
                streamWriter.Flush();
                
                // Write the rest of the document
                using (var xmlWriter = System.Xml.XmlWriter.Create(streamWriter, settings))
                {
                    doc.Save(xmlWriter);
                }
            }
            
            // Return collision messages if any
            return collisionMessages.Count > 0 ? string.Join("\n", collisionMessages) : null;
        }
    }

    public class TmxLevel
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int[]? Tiles { get; set; }
        public int[]? Sprites { get; set; }  // Separate sprites array
        
        // Tileset sources (preserve from loaded file)
        public string? TilesetSource { get; set; } = "famidash.bmp";
        public string? SpritesetSource { get; set; } = "sprites.png";
        
        // Editor settings (preserve from loaded file)
        public bool HasEditorSettings { get; set; } = false;
        public int ChunkWidth { get; set; } = 16;
        public int ChunkHeight { get; set; } = 27;
        public string? ExportTarget { get; set; }
        public string ExportFormat { get; set; } = "csv";
        
        // Parallax layer properties
        public string? ParallaxSource { get; set; }
        public double ParallaxX { get; set; } = 0.9;
        public double ParallaxY { get; set; } = 0.9;
        public bool ParallaxRepeatX { get; set; } = true;
        public bool ParallaxRepeatY { get; set; } = true;
        public bool HasParallaxLayer { get; set; } = false; // Track if parallax existed in loaded file

        // Decoration/spriteset selection for preview rendering
        public string? DecoSet { get; set; } = "deco1";
        
        // Ground layer properties
        public string? GroundSource { get; set; }
        public double GroundOffsetY { get; set; } = 432;
        public bool GroundRepeatX { get; set; } = true;
        public bool HasGroundLayer { get; set; } = false; // Track if ground existed in loaded file
        
        // Collision messages from loading/saving
        public string? LoadCollisionMessages { get; set; }
        public string? SaveCollisionMessages { get; set; }
    }
}
