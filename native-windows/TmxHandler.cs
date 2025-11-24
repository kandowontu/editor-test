using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace FamidashEditor
{
    public static class TmxHandler
    {
        public static TmxLevel LoadTmx(string filePath)
        {
            var doc = XDocument.Load(filePath);
            var map = doc.Element("map");
            
            if (map == null)
                throw new Exception("Invalid TMX file: no map element");

            int width = (int?)map.Attribute("width") ?? 0;
            int height = (int?)map.Attribute("height") ?? 0;
            int totalTiles = width * height;
            
            // Read tileset sources
            string? tilesetSource = null;
            string? spritesetSource = null;
            var tilesets = map.Elements("tileset").ToList();
            foreach (var tileset in tilesets)
            {
                int firstgid = (int?)tileset.Attribute("firstgid") ?? 0;
                var imageElem = tileset.Element("image");
                if (imageElem != null)
                {
                    string? source = (string?)imageElem.Attribute("source");
                    if (firstgid == 1) tilesetSource = source;
                    else if (firstgid == 257) spritesetSource = source;
                }
            }
            
            // Initialize separate tiles and sprites arrays with -1 (empty)
            int[] tiles = Enumerable.Repeat(-1, totalTiles).ToArray();
            int[] sprites = Enumerable.Repeat(-1, totalTiles).ToArray();
            
            // Process tile layers separately
            // TMX uses GIDs: 0=empty, 1-256=famidash tileset, 257-512=sprites tileset
            // Convert to editor format: -1=empty, 0-255=famidash tiles, 0-255=sprites
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
                                if (isSpriteLayer)
                                {
                                    // Sprite layer: GID 257-512 → editor index 0-255
                                    if (gid >= 257)
                                        sprites[i] = gid - 257;
                                }
                                else
                                {
                                    // Tile layer: GID 1-256 → editor index 0-255
                                    if (gid >= 1 && gid <= 256)
                                        tiles[i] = gid - 1;
                                }
                            }
                        }
                    }
                    else
                    {
                        throw new Exception("Only CSV encoding is supported");
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
                HasGroundLayer = groundLayer != null
            };
        }

        public static void SaveTmx(string filePath, TmxLevel level)
        {
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
            
            map.Add(editorSettings);

            // Add tilesets
            map.Add(new XElement("tileset",
                new XAttribute("firstgid", 1),
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
                new XAttribute("firstgid", 257),
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

            // Convert tiles to CSV format
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
                            // Editor: -1=empty, 0-255=tiles → TMX: 0=empty, 1-256=tiles
                            if (editorIdx >= 0 && editorIdx < 256)
                            {
                                tileValue = editorIdx + 1;
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
                var csvLines = new System.Text.StringBuilder();
                for (int y = 0; y < level.Height; y++)
                {
                    for (int x = 0; x < level.Width; x++)
                    {
                        int idx = y * level.Width + x;
                        int tileValue = 0; // Default to GID 0 (empty)
                        
                        if (idx < level.Sprites.Length)
                        {
                            int editorIdx = level.Sprites[idx];
                            // Convert editor index to TMX GID
                            // Editor: -1=empty, 0-255=sprites → TMX: 0=empty, 257-512=sprites
                            if (editorIdx >= 0 && editorIdx < 256)
                            {
                                tileValue = editorIdx + 257;
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
        
        // Ground layer properties
        public string? GroundSource { get; set; }
        public double GroundOffsetY { get; set; } = 432;
        public bool GroundRepeatX { get; set; } = true;
        public bool HasGroundLayer { get; set; } = false; // Track if ground existed in loaded file
    }
}
