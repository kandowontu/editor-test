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
            
            // Find the main tile layer (first tilelayer with data)
            var tileLayer = map.Elements("layer").FirstOrDefault();
            int[]? tiles = null;
            
            if (tileLayer != null)
            {
                var dataElement = tileLayer.Element("data");
                if (dataElement != null)
                {
                    string? encoding = (string?)dataElement.Attribute("encoding");
                    if (encoding == "csv")
                    {
                        string csvData = dataElement.Value.Trim();
                        tiles = csvData.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                      .Select(s => int.Parse(s.Trim()))
                                      .ToArray();
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

            return new TmxLevel
            {
                Width = width,
                Height = height,
                Tiles = tiles,
                ParallaxSource = parallaxSource,
                ParallaxX = parallaxX,
                ParallaxY = parallaxY,
                ParallaxRepeatX = parallaxRepeatX,
                ParallaxRepeatY = parallaxRepeatY,
                GroundSource = groundSource,
                GroundOffsetY = groundOffsetY,
                GroundRepeatX = groundRepeatX
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

            // Add tilesets
            map.Add(new XElement("tileset",
                new XAttribute("firstgid", 1),
                new XAttribute("name", "famidash"),
                new XAttribute("tilewidth", 16),
                new XAttribute("tileheight", 16),
                new XAttribute("tilecount", 256),
                new XAttribute("columns", 16),
                new XElement("image",
                    new XAttribute("source", "../../../GRAPHICS/famidash Red.bmp"),
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
                    new XAttribute("source", "../../../GRAPHICS/sprites.png"),
                    new XAttribute("width", 256),
                    new XAttribute("height", 256)
                )
            ));

            // Add parallax image layer if source is specified
            if (!string.IsNullOrEmpty(level.ParallaxSource))
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

            // Add ground image layer if source is specified
            if (!string.IsNullOrEmpty(level.GroundSource))
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

            // Add main tile layer
            var tileLayer = new XElement("layer",
                new XAttribute("id", 1),
                new XAttribute("width", level.Width),
                new XAttribute("height", level.Height)
            );

            // Convert tiles to CSV format
            if (level.Tiles != null && level.Tiles.Length > 0)
            {
                // Format as CSV with proper line breaks every row
                var csvLines = new System.Text.StringBuilder();
                for (int y = 0; y < level.Height; y++)
                {
                    for (int x = 0; x < level.Width; x++)
                    {
                        int idx = y * level.Width + x;
                        if (idx < level.Tiles.Length)
                        {
                            csvLines.Append(level.Tiles[idx]);
                        }
                        else
                        {
                            csvLines.Append(0);
                        }
                        
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

            // Save the document
            var doc = new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                map
            );
            
            doc.Save(filePath);
        }
    }

    public class TmxLevel
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int[]? Tiles { get; set; }
        
        // Parallax layer properties
        public string? ParallaxSource { get; set; }
        public double ParallaxX { get; set; } = 0.9;
        public double ParallaxY { get; set; } = 0.9;
        public bool ParallaxRepeatX { get; set; } = true;
        public bool ParallaxRepeatY { get; set; } = true;
        
        // Ground layer properties
        public string? GroundSource { get; set; }
        public double GroundOffsetY { get; set; } = 432;
        public bool GroundRepeatX { get; set; } = true;
    }
}
