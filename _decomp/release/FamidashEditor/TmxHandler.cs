using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace FamidashEditor;

public static class TmxHandler
{
	private const int TilesFirstGid = 1;

	private const int SpriteFirstGid = 257;

	private const int TilesCount = 256;

	private static bool IsInteractiveSprite(int sid)
	{
		if (sid < 0 || sid > 255)
		{
			return false;
		}
		int[] array = new int[256]
		{
			52, 52, 52, 52, 52, 18, 18, 255, 40, 40,
			3, 18, 3, 3, 3, 255, 14, 14, 14, 14,
			36, 36, 36, 52, 52, 52, 255, 255, 255, 255,
			255, 18, 36, 36, 52, 52, 52, 3, 3, 18,
			18, 18, 254, 254, 254, 254, 254, 254, 254, 254,
			254, 254, 254, 254, 254, 254, 254, 254, 254, 254,
			254, 254, 254, 254, 254, 254, 254, 254, 18, 18,
			18, 40, 40, 254, 254, 52, 18, 18, 48, 255,
			18, 18, 3, 3, 18, 18, 3, 3, 52, 16,
			255, 18, 18, 18, 18, 52, 52, 52, 52, 52,
			52, 2, 16, 255, 16, 255, 52, 52, 52, 32,
			8, 255, 255, 255, 255, 255, 255, 16, 255, 16,
			255, 18, 18, 18, 18, 255, 255, 255, 253, 253,
			253, 253, 253, 253, 253, 253, 253, 253, 253, 253,
			253, 0, 255, 253, 253, 253, 253, 253, 253, 253,
			253, 253, 253, 253, 253, 253, 253, 0, 255, 253,
			253, 253, 253, 253, 253, 253, 253, 253, 253, 253,
			253, 253, 253, 0, 253, 252, 252, 252, 252, 252,
			252, 252, 252, 252, 252, 252, 252, 252, 252, 252,
			252, 252, 253, 253, 253, 253, 253, 253, 253, 253,
			253, 253, 253, 253, 253, 0, 0, 253, 253, 253,
			253, 253, 253, 253, 253, 253, 253, 253, 253, 253,
			253, 255, 255, 0, 253, 253, 253, 253, 253, 253,
			253, 253, 253, 253, 253, 253, 253, 255, 0, 0,
			255, 255, 255, 255, 255, 255, 16, 16, 16, 16,
			31, 16, 16, 3, 3, 0
		};
		if (IsTriggerSprite(sid))
		{
			return true;
		}
		return array[sid] < 252;
	}

	private static bool IsTriggerSprite(int spriteIdx)
	{
		if (spriteIdx < 0)
		{
			return false;
		}
		switch (spriteIdx)
		{
		default:
			if (spriteIdx != 125 && spriteIdx != 127 && (spriteIdx < 128 || spriteIdx > 239))
			{
				if (spriteIdx >= 240)
				{
					return spriteIdx <= 245;
				}
				return false;
			}
			break;
		case 15:
		case 111:
		case 112:
		case 113:
		case 114:
		case 115:
		case 116:
			break;
		}
		return true;
	}

	public static TmxLevel LoadTmx(string filePath, bool useLegacyTriggerOffset = false)
	{
		XElement xElement = XDocument.Load(filePath).Element("map");
		if (xElement == null)
		{
			throw new Exception("Invalid TMX file: no map element");
		}
		int width = ((int?)xElement.Attribute("width")).GetValueOrDefault();
		int valueOrDefault = ((int?)xElement.Attribute("height")).GetValueOrDefault();
		int num = width * valueOrDefault;
		new List<string>();
		string text = null;
		string text2 = null;
		List<XElement> list = xElement.Elements("tileset").ToList();
		List<int> list2 = new List<int>();
		List<int> list3 = new List<int>();
		foreach (XElement item14 in list)
		{
			int valueOrDefault2 = ((int?)item14.Attribute("firstgid")).GetValueOrDefault();
			string a = (string?)item14.Attribute("name");
			XElement xElement2 = item14.Element("image");
			if (xElement2 == null)
			{
				continue;
			}
			string text3 = (string?)xElement2.Attribute("source");
			if (string.Equals(a, "famidash", StringComparison.OrdinalIgnoreCase))
			{
				list3.Add(valueOrDefault2);
				if (text == null)
				{
					text = text3;
				}
			}
			else
			{
				list2.Add(valueOrDefault2);
				if (text2 == null)
				{
					text2 = text3;
				}
			}
		}
		if (list3.Count == 0)
		{
			list3.Add(1);
		}
		if (list2.Count == 0)
		{
			list2.Add(257);
		}
		int[] array = Enumerable.Repeat(-1, num).ToArray();
		int[] sprites = Enumerable.Repeat(-1, num).ToArray();
		List<string> list4 = new List<string>();
		List<(int, int, int, int, int, bool)> list5 = new List<(int, int, int, int, int, bool)>();
		foreach (XElement item15 in xElement.Elements("layer").ToList())
		{
			((string?)item15.Attribute("name"))?.Equals("SP", StringComparison.OrdinalIgnoreCase);
			XElement xElement3 = item15.Element("data");
			if (xElement3 == null)
			{
				continue;
			}
			if ((string?)xElement3.Attribute("encoding") == "csv")
			{
				int[] array2 = (from s in xElement3.Value.Trim().Split(new char[3] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
					select int.Parse(s.Trim())).ToArray();
				for (int num2 = 0; num2 < Math.Min(array2.Length, num); num2++)
				{
					int num3 = array2[num2];
					if (num3 <= 0)
					{
						continue;
					}
					bool flag = false;
					foreach (int item16 in list2)
					{
						if (num3 < item16 || num3 >= item16 + 256)
						{
							continue;
						}
						int num4 = num3 - item16;
						int num5 = num2 / width;
						int num6 = num2 % width;
						int item = num6;
						int item2 = num5;
						bool flag2 = !useLegacyTriggerOffset && IsTriggerSprite(num4);
						if (flag2)
						{
							num6 -= 10;
							if (num6 < 0)
							{
								num6 = 0;
							}
						}
						list5.Add((num4, num6, num5, item, item2, flag2));
						flag = true;
						break;
					}
					if (flag)
					{
						continue;
					}
					foreach (int item17 in list3)
					{
						if (num3 >= item17 && num3 < item17 + 256)
						{
							array[num2] = num3 - item17;
							flag = true;
							break;
						}
					}
				}
				continue;
			}
			throw new Exception("Only CSV encoding is supported");
		}
		List<(int, int, int, int, int, bool)> list6 = list5.Where<(int, int, int, int, int, bool)>(((int spriteIdx, int x, int y, int originalX, int originalY, bool isTrigger) s) => !s.isTrigger).ToList();
		List<(int, int, int, int, int, bool)> list7 = list5.Where<(int, int, int, int, int, bool)>(((int spriteIdx, int x, int y, int originalX, int originalY, bool isTrigger) s) => s.isTrigger).ToList();
		list6.Sort(delegate((int spriteIdx, int x, int y, int originalX, int originalY, bool isTrigger) tuple, (int spriteIdx, int x, int y, int originalX, int originalY, bool isTrigger) b)
		{
			int num21 = tuple.y.CompareTo(b.y);
			return (num21 != 0) ? num21 : tuple.x.CompareTo(b.x);
		});
		list7.Sort(delegate((int spriteIdx, int x, int y, int originalX, int originalY, bool isTrigger) tuple, (int spriteIdx, int x, int y, int originalX, int originalY, bool isTrigger) b)
		{
			int num21 = tuple.x.CompareTo(b.x);
			return (num21 != 0) ? num21 : tuple.y.CompareTo(b.y);
		});
		foreach (var item18 in list6)
		{
			int item3 = item18.Item1;
			int item4 = item18.Item2;
			int item5 = item18.Item3;
			int item6 = item18.Item4;
			int item7 = item18.Item5;
			int num7 = item5 * width + item4;
			if (num7 < 0 || num7 >= num)
			{
				continue;
			}
			if (sprites[num7] != -1)
			{
				int num8 = sprites[num7];
				bool num9 = IsInteractiveSprite(num8);
				bool flag3 = IsInteractiveSprite(item3);
				if (num9 && !flag3)
				{
					list4.Add($"NORMAL Sprite 0x{item3:X2} (deco) at TMX({item6},{item7}) SKIPPED — would overwrite interactive sprite 0x{num8:X2} at ({item4},{item5})");
					continue;
				}
				list4.Add($"NORMAL Sprite 0x{item3:X2} at TMX({item6},{item7}) OVERWRITES existing sprite 0x{num8:X2} at ({item4},{item5})");
			}
			sprites[num7] = item3;
		}
		int maxPlayableRow = Math.Max(0, valueOrDefault - 2);
		HashSet<int> pushVisited = new HashSet<int>();
		Func<int, int, int, bool, int> PlaceTriggerWithPush = null;
		PlaceTriggerWithPush = delegate(int col, int row, int spriteId, bool preferUpFirst)
		{
			int num21 = Math.Max(0, Math.Min(maxPlayableRow, row));
			int num22 = num21 * width + col;
			if (sprites[num22] == -1)
			{
				sprites[num22] = spriteId;
				return num21;
			}
			if (!IsTriggerSprite(sprites[num22]))
			{
				for (int i = 0; i <= maxPlayableRow; i++)
				{
					if (preferUpFirst)
					{
						int num23 = row - i;
						if (num23 >= 0 && num23 <= maxPlayableRow)
						{
							int num24 = num23 * width + col;
							if (sprites[num24] == -1)
							{
								sprites[num24] = spriteId;
								return num23;
							}
						}
						if (i != 0)
						{
							int num25 = row + i;
							if (num25 >= 0 && num25 <= maxPlayableRow)
							{
								int num26 = num25 * width + col;
								if (sprites[num26] == -1)
								{
									sprites[num26] = spriteId;
									return num25;
								}
							}
						}
					}
					else
					{
						int num27 = row + i;
						if (num27 >= 0 && num27 <= maxPlayableRow)
						{
							int num28 = num27 * width + col;
							if (sprites[num28] == -1)
							{
								sprites[num28] = spriteId;
								return num27;
							}
						}
						if (i != 0)
						{
							int num29 = row - i;
							if (num29 >= 0 && num29 <= maxPlayableRow)
							{
								int num30 = num29 * width + col;
								if (sprites[num30] == -1)
								{
									sprites[num30] = spriteId;
									return num29;
								}
							}
						}
					}
				}
				return -1;
			}
			int item13 = (col << 16) | num21;
			if (pushVisited.Contains(item13))
			{
				return -1;
			}
			pushVisited.Add(item13);
			int arg2 = sprites[num22];
			if (preferUpFirst)
			{
				for (int j = 1; j <= maxPlayableRow; j++)
				{
					int num31 = num21 - j;
					if (num31 >= 0 && PlaceTriggerWithPush(col, num31, arg2, preferUpFirst) >= 0)
					{
						sprites[num22] = spriteId;
						pushVisited.Remove(item13);
						return num21;
					}
					int num32 = num21 + j;
					if (num32 <= maxPlayableRow && PlaceTriggerWithPush(col, num32, arg2, preferUpFirst) >= 0)
					{
						sprites[num22] = spriteId;
						pushVisited.Remove(item13);
						return num21;
					}
				}
			}
			else
			{
				for (int k = 1; k <= maxPlayableRow; k++)
				{
					int num33 = num21 + k;
					if (num33 <= maxPlayableRow && PlaceTriggerWithPush(col, num33, arg2, preferUpFirst) >= 0)
					{
						sprites[num22] = spriteId;
						pushVisited.Remove(item13);
						return num21;
					}
					int num34 = num21 - k;
					if (num34 >= 0 && PlaceTriggerWithPush(col, num34, arg2, preferUpFirst) >= 0)
					{
						sprites[num22] = spriteId;
						pushVisited.Remove(item13);
						return num21;
					}
				}
			}
			return -1;
		};
		foreach (var item19 in list7)
		{
			int item8 = item19.Item1;
			int item9 = item19.Item2;
			int item10 = item19.Item3;
			int item11 = item19.Item4;
			int item12 = item19.Item5;
			int num10 = Math.Max(0, Math.Min(width - 1, item9));
			int num11 = -1;
			bool arg = false;
			try
			{
				int num12 = Math.Max(0, valueOrDefault - 1 - 8);
				int num13 = Math.Max(0, valueOrDefault - 2);
				if (item10 >= num12 && item10 <= num13)
				{
					arg = true;
				}
			}
			catch
			{
				arg = false;
			}
			pushVisited.Clear();
			num11 = PlaceTriggerWithPush(num10, item10, item8, arg);
			if (num11 >= 0)
			{
				if (num11 != item10)
				{
					list4.Add($"TRIGGER Sprite 0x{item8:X2} at TMX({item11},{item12}) → Placed at ({num10},{num11})");
				}
				continue;
			}
			bool flag4 = false;
			for (int num14 = 0; num14 <= maxPlayableRow; num14++)
			{
				int num15 = num14 * width + num10;
				if (sprites[num15] == -1)
				{
					sprites[num15] = item8;
					list4.Add($"TRIGGER Sprite 0x{item8:X2} at TMX({item11},{item12}) → Placed at ({num10},{num14}) [fallback]");
					flag4 = true;
					break;
				}
			}
			if (flag4)
			{
				continue;
			}
			bool flag5 = false;
			for (int num16 = 1; num16 <= 2; num16++)
			{
				if (flag5)
				{
					break;
				}
				int[] array3 = new int[2]
				{
					num10 + num16,
					num10 - num16
				};
				foreach (int num18 in array3)
				{
					if (num18 < 0 || num18 >= width)
					{
						continue;
					}
					for (int num19 = 0; num19 <= maxPlayableRow; num19++)
					{
						int num20 = num19 * width + num18;
						if (sprites[num20] == -1)
						{
							sprites[num20] = item8;
							list4.Add($"TRIGGER Sprite 0x{item8:X2} at TMX({item11},{item12}) → Placed at ({num18},{num19}) [adjacent col fallback]");
							flag5 = true;
							break;
						}
					}
					if (flag5)
					{
						break;
					}
				}
			}
			if (!flag5)
			{
				list4.Add($"TRIGGER Sprite 0x{item8:X2} at TMX({item11},{item12}) → DROPPED (column {num10} full, no adjacent space)");
			}
		}
		XElement xElement4 = xElement.Elements("imagelayer").FirstOrDefault(delegate(XElement l)
		{
			string? text4 = (string?)l.Attribute("name");
			return (text4 != null && text4.Contains("parallax", StringComparison.OrdinalIgnoreCase)) || (((string?)l.Attribute("name"))?.Contains("Image Layer 1") ?? false);
		});
		string parallaxSource = null;
		double parallaxX = 1.0;
		double parallaxY = 1.0;
		bool parallaxRepeatX = false;
		bool parallaxRepeatY = false;
		if (xElement4 != null)
		{
			parallaxX = ((double?)xElement4.Attribute("parallaxx")) ?? 1.0;
			parallaxY = ((double?)xElement4.Attribute("parallaxy")) ?? 1.0;
			parallaxRepeatX = (int?)xElement4.Attribute("repeatx") == 1;
			parallaxRepeatY = (int?)xElement4.Attribute("repeaty") == 1;
			XElement xElement5 = xElement4.Element("image");
			if (xElement5 != null)
			{
				parallaxSource = (string?)xElement5.Attribute("source");
			}
		}
		XElement xElement6 = xElement.Elements("imagelayer").FirstOrDefault(delegate(XElement l)
		{
			string? text4 = (string?)l.Attribute("name");
			return (text4 != null && text4.Contains("ground", StringComparison.OrdinalIgnoreCase)) || (((string?)l.Attribute("name"))?.Contains("Image Layer 2") ?? false);
		});
		string groundSource = null;
		double groundOffsetY = 0.0;
		bool groundRepeatX = false;
		if (xElement6 != null)
		{
			groundOffsetY = ((double?)xElement6.Attribute("offsety")).GetValueOrDefault();
			groundRepeatX = (int?)xElement6.Attribute("repeatx") == 1;
			XElement xElement7 = xElement6.Element("image");
			if (xElement7 != null)
			{
				groundSource = (string?)xElement7.Attribute("source");
			}
		}
		XElement xElement8 = xElement.Element("editorsettings");
		bool hasEditorSettings = xElement8 != null;
		int chunkWidth = 16;
		int chunkHeight = valueOrDefault;
		string exportTarget = null;
		string exportFormat = "csv";
		if (xElement8 != null)
		{
			XElement xElement9 = xElement8.Element("chunksize");
			if (xElement9 != null)
			{
				chunkWidth = ((int?)xElement9.Attribute("width")) ?? 16;
				chunkHeight = ((int?)xElement9.Attribute("height")) ?? valueOrDefault;
			}
			XElement xElement10 = xElement8.Element("export");
			if (xElement10 != null)
			{
				exportTarget = (string?)xElement10.Attribute("target");
				exportFormat = ((string?)xElement10.Attribute("format")) ?? "csv";
			}
		}
		return new TmxLevel
		{
			Width = width,
			Height = valueOrDefault,
			Tiles = array,
			Sprites = sprites,
			TilesetSource = text,
			SpritesetSource = text2,
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
			HasParallaxLayer = (xElement4 != null),
			GroundSource = groundSource,
			GroundOffsetY = groundOffsetY,
			GroundRepeatX = groundRepeatX,
			HasGroundLayer = (xElement6 != null),
			LoadCollisionMessages = ((list4.Count > 0) ? string.Join("\n", list4) : null),
			DecoSet = (xElement8?.Element("decoset")?.Attribute("name")?.Value ?? "deco1")
		};
	}

	public static string? SaveTmx(string filePath, TmxLevel level, bool useLegacyTriggerOffset = false)
	{
		List<string> list = new List<string>();
		XElement xElement = new XElement("map", new XAttribute("version", "1.10"), new XAttribute("tiledversion", "1.11.2"), new XAttribute("orientation", "orthogonal"), new XAttribute("renderorder", "right-down"), new XAttribute("width", level.Width), new XAttribute("height", level.Height), new XAttribute("tilewidth", 16), new XAttribute("tileheight", 16), new XAttribute("infinite", 0), new XAttribute("nextlayerid", 5), new XAttribute("nextobjectid", 1));
		XElement xElement2 = new XElement("editorsettings");
		xElement2.Add(new XElement("chunksize", new XAttribute("width", level.ChunkWidth), new XAttribute("height", level.ChunkHeight)));
		xElement2.Add(new XElement("export", new XAttribute("target", level.ExportTarget ?? "export.csv"), new XAttribute("format", level.ExportFormat)));
		if (!string.IsNullOrEmpty(level.DecoSet))
		{
			xElement2.Add(new XElement("decoset", new XAttribute("name", level.DecoSet)));
		}
		xElement.Add(xElement2);
		xElement.Add(new XElement("tileset", new XAttribute("firstgid", 1), new XAttribute("name", "famidash"), new XAttribute("tilewidth", 16), new XAttribute("tileheight", 16), new XAttribute("tilecount", 256), new XAttribute("columns", 16), new XElement("image", new XAttribute("source", level.TilesetSource ?? "famidash.bmp"), new XAttribute("width", 256), new XAttribute("height", 256))));
		xElement.Add(new XElement("tileset", new XAttribute("firstgid", 257), new XAttribute("name", "sprites"), new XAttribute("tilewidth", 16), new XAttribute("tileheight", 16), new XAttribute("tilecount", 256), new XAttribute("columns", 16), new XElement("image", new XAttribute("source", level.SpritesetSource ?? "sprites.png"), new XAttribute("width", 256), new XAttribute("height", 256))));
		if (level.HasParallaxLayer && !string.IsNullOrEmpty(level.ParallaxSource))
		{
			XElement xElement3 = new XElement("imagelayer", new XAttribute("id", 3), new XAttribute("name", "Image Layer 1"), new XAttribute("parallaxx", level.ParallaxX), new XAttribute("parallaxy", level.ParallaxY));
			if (level.ParallaxRepeatX)
			{
				xElement3.Add(new XAttribute("repeatx", 1));
			}
			if (level.ParallaxRepeatY)
			{
				xElement3.Add(new XAttribute("repeaty", 1));
			}
			xElement3.Add(new XElement("image", new XAttribute("source", level.ParallaxSource), new XAttribute("width", 144), new XAttribute("height", 72)));
			xElement.Add(xElement3);
		}
		if (level.HasGroundLayer && !string.IsNullOrEmpty(level.GroundSource))
		{
			XElement xElement4 = new XElement("imagelayer", new XAttribute("id", 4), new XAttribute("name", "Image Layer 2"), new XAttribute("offsetx", 0), new XAttribute("offsety", level.GroundOffsetY));
			if (level.GroundRepeatX)
			{
				xElement4.Add(new XAttribute("repeatx", 1));
			}
			xElement4.Add(new XElement("image", new XAttribute("source", level.GroundSource), new XAttribute("width", 64), new XAttribute("height", 128)));
			xElement.Add(xElement4);
		}
		XElement xElement5 = new XElement("layer", new XAttribute("id", 1), new XAttribute("width", level.Width), new XAttribute("height", level.Height));
		if (level.Tiles != null && level.Tiles.Length != 0)
		{
			StringBuilder stringBuilder = new StringBuilder();
			for (int i = 0; i < level.Height; i++)
			{
				for (int j = 0; j < level.Width; j++)
				{
					int num = i * level.Width + j;
					int value = 0;
					if (num < level.Tiles.Length)
					{
						int num2 = level.Tiles[num];
						if (num2 >= 0 && num2 < 256)
						{
							value = num2 + 1;
						}
					}
					stringBuilder.Append(value);
					if (j < level.Width - 1)
					{
						stringBuilder.Append(',');
					}
				}
				if (i < level.Height - 1)
				{
					stringBuilder.AppendLine(",");
				}
			}
			xElement5.Add(new XElement("data", new XAttribute("encoding", "csv"), "\n" + stringBuilder.ToString() + "\n"));
		}
		xElement.Add(xElement5);
		XElement xElement6 = new XElement("layer", new XAttribute("id", 2), new XAttribute("name", "SP"), new XAttribute("width", level.Width), new XAttribute("height", level.Height));
		if (level.Sprites != null && level.Sprites.Length != 0)
		{
			int[] array = new int[level.Sprites.Length];
			Array.Fill(array, -1);
			for (int k = 0; k < level.Height; k++)
			{
				for (int l = 0; l < level.Width; l++)
				{
					int num3 = k * level.Width + l;
					if (num3 >= level.Sprites.Length)
					{
						continue;
					}
					int num4 = level.Sprites[num3];
					if (num4 < 0)
					{
						continue;
					}
					int num5 = l;
					int value2 = l;
					int value3 = k;
					if (!useLegacyTriggerOffset && IsTriggerSprite(num4))
					{
						num5 += 10;
						if (num5 >= level.Width)
						{
							continue;
						}
					}
					int num6 = k * level.Width + num5;
					if (num6 < 0 || num6 >= array.Length)
					{
						continue;
					}
					if (array[num6] != -1)
					{
						if (!useLegacyTriggerOffset && IsTriggerSprite(num4))
						{
							int num7 = k;
							bool flag = false;
							for (int m = 1; m < level.Height; m++)
							{
								int num8 = k + m;
								if (num8 < level.Height)
								{
									int num9 = num8 * level.Width + num5;
									if (array[num9] == -1)
									{
										num7 = num8;
										flag = true;
										break;
									}
								}
								num8 = k - m;
								if (num8 >= 0)
								{
									int num10 = num8 * level.Width + num5;
									if (array[num10] == -1)
									{
										num7 = num8;
										flag = true;
										break;
									}
								}
							}
							if (!flag)
							{
								list.Add($"TRIGGER Sprite 0x{num4:X2} at ({value2},{value3}) shifted to ({num5},{k}) collides, no free vertical slot found - sprite dropped");
								continue;
							}
							list.Add($"TRIGGER Sprite 0x{num4:X2} at ({value2},{value3}) shifted to ({num5},{k}) collides, saved to ({num5},{num7})");
							num6 = num7 * level.Width + num5;
						}
						else
						{
							int value4 = array[num6];
							list.Add($"NORMAL Sprite 0x{num4:X2} at ({value2},{value3}) OVERWRITES existing sprite 0x{value4:X2} at ({num5},{k})");
						}
					}
					array[num6] = num4;
				}
			}
			StringBuilder stringBuilder2 = new StringBuilder();
			for (int n = 0; n < level.Height; n++)
			{
				for (int num11 = 0; num11 < level.Width; num11++)
				{
					int num12 = n * level.Width + num11;
					int value5 = 0;
					if (num12 < array.Length)
					{
						int num13 = array[num12];
						if (num13 >= 0 && num13 < 256)
						{
							value5 = num13 + 257;
						}
					}
					stringBuilder2.Append(value5);
					if (num11 < level.Width - 1)
					{
						stringBuilder2.Append(',');
					}
				}
				if (n < level.Height - 1)
				{
					stringBuilder2.AppendLine(",");
				}
			}
			xElement6.Add(new XElement("data", new XAttribute("encoding", "csv"), "\n" + stringBuilder2.ToString() + "\n"));
		}
		xElement.Add(xElement6);
		XDocument xDocument = new XDocument(xElement);
		XmlWriterSettings settings = new XmlWriterSettings
		{
			Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
			Indent = true,
			IndentChars = " ",
			NewLineChars = "\n",
			NewLineHandling = NewLineHandling.Replace,
			OmitXmlDeclaration = true
		};
		using (FileStream stream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
		{
			using StreamWriter streamWriter = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			streamWriter.Write("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
			streamWriter.Flush();
			using XmlWriter writer = XmlWriter.Create(streamWriter, settings);
			xDocument.Save(writer);
		}
		if (list.Count <= 0)
		{
			return null;
		}
		return string.Join("\n", list);
	}
}
