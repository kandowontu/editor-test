using System;
using System.Text.RegularExpressions;

namespace FamidashEditor;

public static class MetatileCollisionTable
{
	private static readonly MetatileCollision[] table;

	public static bool TileKillsAtPixel(MetatileCollision col, int localX, int localY)
	{
		return col switch
		{
			MetatileCollision.COL_DEATH => InRange(localY, 4, 11) && InRange(localX, 4, 8), 
			MetatileCollision.COL_DEATH_TOP => localY < 6 && InRange(localX, 3, 9), 
			MetatileCollision.COL_DEATH_BOTTOM => localY > 10 && InRange(localX, 3, 9), 
			MetatileCollision.COL_DEATH_LEFT => localX < 6 && InRange(localY, 3, 9), 
			MetatileCollision.COL_DEATH_RIGHT => localX >= 10 && InRange(localY, 3, 9), 
			MetatileCollision.COL_DEATH_BOTTOM_LEFT => (localX < 6 && InRange(localY, 3, 9)) || (localY > 10 && InRange(localX, 3, 9)), 
			MetatileCollision.COL_DEATH_BOTTOM_RIGHT => (localX >= 10 && InRange(localY, 3, 9)) || (localY > 10 && InRange(localX, 3, 9)), 
			MetatileCollision.COL_DEATH_TOP_LEFT => (localX < 6 && InRange(localY, 3, 9)) || (localY < 6 && InRange(localX, 3, 9)), 
			MetatileCollision.COL_DEATH_TOP_RIGHT => (localX >= 10 && InRange(localY, 3, 9)) || (localY < 6 && InRange(localX, 3, 9)), 
			MetatileCollision.COL_DEATH_TOP_RIGHT_LEFT => (localY < 6 && InRange(localX, 3, 9)) || (localX >= 10 && InRange(localY, 3, 9)) || (localX < 6 && InRange(localY, 3, 9)), 
			MetatileCollision.COL_DEATH_TOP_BOTTOM => (localY < 6 && InRange(localX, 3, 9)) || (localY > 10 && InRange(localX, 3, 9)), 
			MetatileCollision.COL_DEATH_LEFT_RIGHT => (localX < 6 && InRange(localY, 3, 9)) || (localX >= 10 && InRange(localY, 3, 9)), 
			MetatileCollision.COL_DEATH_TOP_LEFT_BOTTOM => (localY < 6 && InRange(localX, 3, 9)) || (localX < 6 && InRange(localY, 3, 9)) || (localY > 10 && InRange(localX, 3, 9)), 
			MetatileCollision.COL_UP_LEFT_SPIKE => localY < 8 && InRange(localX, 2, 5), 
			MetatileCollision.COL_UP_RIGHT_SPIKE => localY < 8 && InRange(localX, 10, 12), 
			MetatileCollision.COL_UP_BOTH_SPIKES => localY < 8 && InRange(localX & 7, 2, 5), 
			MetatileCollision.COL_DOWN_LEFT_SPIKE => localY >= 8 && InRange(localX, 2, 5), 
			MetatileCollision.COL_DOWN_RIGHT_SPIKE => localY >= 8 && InRange(localX, 10, 12), 
			MetatileCollision.COL_DOWN_BOTH_SPIKES => localY >= 8 && InRange(localX & 7, 2, 5), 
			MetatileCollision.COL_TOP_CENTER_SPIKE => localY > 10 && InRange(localX, 3, 9), 
			MetatileCollision.COL_BOTTOM_CENTER_SPIKE => localY < 8 && InRange(localX, 7, 10), 
			MetatileCollision.COL_LEFT_SPIKE_BLOCK => localY < 8 && InRange(localX, 2, 5), 
			MetatileCollision.COL_RIGHT_SPIKE_BLOCK => localY < 8 && InRange(localX, 10, 12), 
			MetatileCollision.COL_BOTTOM_LEFT_SPIKE => localY < 8 && InRange(localX, 2, 5), 
			MetatileCollision.COL_BOTTOM_RIGHT_SPIKE => localY < 8 && InRange(localX, 10, 12), 
			MetatileCollision.COL_BOTTOM_SPIKES => localY < 8 && InRange(localX & 7, 2, 5), 
			_ => false, 
		};
		static bool InRange(int val, int min, int max)
		{
			return val >= min && val <= max;
		}
	}

	public static bool TileKillsAtSideProbe(MetatileCollision col, int localX, int localY)
	{
		if (TileKillsAtPixel(col, localX, localY))
		{
			return true;
		}
		int num = localY & 0xF;
		int num2 = num & 7;
		int num3 = localX & 0xF;
		switch (col)
		{
		case MetatileCollision.COL_BOTTOM:
		case MetatileCollision.COL_BOTTOM_CENTER_SPIKE:
		case MetatileCollision.COL_BOTTOM_LEFT_SPIKE:
		case MetatileCollision.COL_BOTTOM_RIGHT_SPIKE:
		case MetatileCollision.COL_BOTTOM_SPIKES:
			return num2 != num;
		case MetatileCollision.COL_TOP:
		case MetatileCollision.COL_TOP_CENTER_SPIKE:
			return num2 == num;
		case MetatileCollision.COL_DOWN_LEFT:
		case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
			return num >= 8 && num3 < 8;
		case MetatileCollision.COL_DOWN_RIGHT:
		case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
			return num >= 8 && num3 >= 8;
		case MetatileCollision.COL_LEFT:
			return num3 < 8;
		case MetatileCollision.COL_RIGHT:
			return num3 >= 8;
		case MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT:
			return (num3 < 8) ? (num < 8) : (num >= 8);
		case MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT:
			return (num3 < 8) ? (num >= 8) : (num < 8);
		case MetatileCollision.COL_TOP_RIGHT_STAIRS:
			return num < 8 || num3 >= 8;
		case MetatileCollision.COL_TOP_LEFT_STAIRS:
			return num < 8 || num3 < 8;
		case MetatileCollision.COL_BOTTOM_RIGHT_STAIRS:
			return num >= 8 || num3 >= 8;
		case MetatileCollision.COL_BOTTOM_LEFT_STAIRS:
			return num >= 8 || num3 < 8;
		default:
			return false;
		}
	}

	public static bool TileBlocksAtSideProbe(MetatileCollision col, int localX, int localY)
	{
		int num = localY & 0xF;
		int num2 = num & 7;
		int num3 = localX & 0xF;
		switch (col)
		{
		case MetatileCollision.COL_BOTTOM:
			return num2 != num;
		case MetatileCollision.COL_TOP:
			return num2 == num;
		default:
			switch (col)
			{
			case MetatileCollision.COL_UP_LEFT:
				return num < 8 && num3 < 8;
			case MetatileCollision.COL_UP_RIGHT:
				return num < 8 && num3 >= 8;
			case MetatileCollision.COL_DOWN_LEFT:
			case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
				return num >= 8 && num3 < 8;
			case MetatileCollision.COL_DOWN_RIGHT:
			case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
				return num >= 8 && num3 >= 8;
			case MetatileCollision.COL_BOTTOM_CENTER_SPIKE:
			case MetatileCollision.COL_BOTTOM_LEFT_SPIKE:
			case MetatileCollision.COL_BOTTOM_RIGHT_SPIKE:
			case MetatileCollision.COL_BOTTOM_SPIKES:
				return num2 != num;
			case MetatileCollision.COL_TOP_CENTER_SPIKE:
				return num2 == num;
			case MetatileCollision.COL_LEFT:
				return num3 < 8;
			case MetatileCollision.COL_RIGHT:
				return num3 >= 8;
			case MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT:
				return (num3 < 8) ? (num < 8) : (num >= 8);
			case MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT:
				return (num3 < 8) ? (num >= 8) : (num < 8);
			case MetatileCollision.COL_TOP_RIGHT_STAIRS:
				return num < 8 || num3 >= 8;
			case MetatileCollision.COL_TOP_LEFT_STAIRS:
				return num < 8 || num3 < 8;
			case MetatileCollision.COL_BOTTOM_RIGHT_STAIRS:
				return num >= 8 || num3 >= 8;
			case MetatileCollision.COL_BOTTOM_LEFT_STAIRS:
				return num >= 8 || num3 < 8;
			default:
				return false;
			}
		}
	}

	static MetatileCollisionTable()
	{
		table = new MetatileCollision[256];
		for (int i = 0; i < table.Length; i++)
		{
			table[i] = MetatileCollision.COL_NONE;
		}
		string text = "\r\nCOL_NONE\r\nCOL_FLOOR_CEIL\r\nCOL_FLOOR_CEIL\r\nCOL_BOTTOM\r\nCOL_DEATH_TOP\r\nCOL_FLOOR_CEIL\r\nCOL_FLOOR_CEIL\r\nCOL_NONE\r\nCOL_DEATH_BOTTOM\r\nCOL_DEATH_BOTTOM\r\nCOL_DEATH_TOP\r\nCOL_DEATH_TOP\r\nCOL_DEATH_BOTTOM\r\nCOL_DEATH_TOP\r\nCOL_DEATH_LEFT\r\nCOL_DEATH_RIGHT\r\n\r\nCOL_ALL\r\nCOL_DEATH\r\nCOL_DEATH_BOTTOM\r\nCOL_DEATH_BOTTOM\r\nCOL_DEATH_TOP\r\nCOL_TOP_CENTER_SPIKE\r\nCOL_ALL\r\nCOL_DEATH_TOP\r\nCOL_DEATH_BOTTOM\r\nCOL_TOP\r\nCOL_DEATH\r\nCOL_DEATH\r\nCOL_DEATH\r\nCOL_DEATH_LEFT\r\nCOL_DEATH_TOP\r\nCOL_DEATH_RIGHT\r\n\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_NONE\r\n\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_NONE\r\nCOL_NONE\r\nCOL_NONE\r\nCOL_TOP_CENTER_SPIKE\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_DEATH_BOTTOM\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\n\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_NONE\r\n\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_TOP\r\nCOL_TOP\r\nCOL_TOP\r\nCOL_TOP\r\nCOL_BOTTOM\r\nCOL_BOTTOM\r\nCOL_BOTTOM\r\nCOL_DEATH\r\nCOL_DEATH_BOTTOM\r\nCOL_DEATH_TOP\r\nCOL_DEATH_RIGHT\r\nCOL_DEATH_LEFT\r\n\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_NONE\r\n\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_ALL\r\nCOL_DOWN_RIGHT_SPIKE\r\nCOL_DEATH_BOTTOM\r\nCOL_DOWN_LEFT_SPIKE\r\nCOL_DEATH_RIGHT\r\nCOL_NONE\r\nCOL_DEATH_LEFT\r\nCOL_UP_RIGHT_SPIKE\r\nCOL_DEATH_TOP\r\nCOL_UP_LEFT_SPIKE\r\nCOL_DEATH\r\nCOL_ALL\r\nCOL_DEATH_BOTTOM\r\n\r\nCOL_NONE;$80\r\nCOL_NONE\r\nCOL_DEATH\r\nCOL_DEATH\r\nCOL_DEATH_BOTTOM\r\nCOL_DEATH_TOP\r\nCOL_DEATH_BOTTOM\r\nCOL_DEATH_TOP\r\nCOL_FLOOR_CEIL\r\nCOL_FLOOR_CEIL\r\nCOL_RIGHT\r\nCOL_LEFT\r\nCOL_RIGHT\r\nCOL_LEFT\r\nCOL_NONE\r\nCOL_NO_SIDE\r\n\r\nCOL_SLOPE_RD45\r\nCOL_SLOPE_LD45\r\nCOL_SLOPE_RU45\r\nCOL_SLOPE_LU45\r\nCOL_SLOPE_RD22_RIGHT\r\nCOL_SLOPE_RD22_LEFT\r\nCOL_SLOPE_LD22_RIGHT\r\nCOL_SLOPE_LD22_LEFT\r\nCOL_SLOPE_RU22_RIGHT\r\nCOL_SLOPE_RU22_LEFT\r\nCOL_SLOPE_LU22_RIGHT\r\nCOL_SLOPE_LU22_LEFT\r\nCOL_SLOPE_RD66_TOP\r\nCOL_SLOPE_RD66_BOT\r\nCOL_SLOPE_LD66_BOT\r\nCOL_SLOPE_LD66_TOP\r\n\r\nCOL_SLOPE_RU66_TOP\r\nCOL_SLOPE_RU66_BOT\r\nCOL_SLOPE_LU66_BOT\r\nCOL_SLOPE_LU66_TOP\r\nCOL_NO_SIDE\r\nCOL_NO_SIDE\r\nCOL_NO_SIDE\r\nCOL_NO_SIDE\r\nCOL_LEFT_SPIKE_BLOCK\r\nCOL_RIGHT_SPIKE_BLOCK\r\nCOL_BOTTOM_LEFT_SPIKE\r\nCOL_BOTTOM_RIGHT_SPIKE\r\nCOL_BOTTOM_SPIKES\r\nCOL_DOWN_LEFT_SPIKE\r\nCOL_DOWN_RIGHT_SPIKE\r\nCOL_DOWN_BOTH_SPIKES\r\n\r\nCOL_UP_LEFT\r\nCOL_UP_RIGHT\r\nCOL_DOWN_LEFT\r\nCOL_DOWN_RIGHT\r\nCOL_TOP\r\nCOL_BOTTOM\r\nCOL_LEFT\r\nCOL_RIGHT\r\nCOL_TOP_LEFT_STAIRS\r\nCOL_TOP_RIGHT_STAIRS\r\nCOL_BOTTOM_LEFT_STAIRS\r\nCOL_BOTTOM_RIGHT_STAIRS\r\nCOL_TOP_LEFT_BOTTOM_RIGHT\r\nCOL_TOP_RIGHT_BOTTOM_LEFT\r\nCOL_UP_LEFT_SPIKE\r\nCOL_UP_RIGHT_SPIKE\r\n\r\nCOL_UP_BOTH_SPIKES\r\nCOL_DEATH_TOP_RIGHT\r\nCOL_DEATH_TOP_LEFT\r\nCOL_DEATH_BOTTOM_RIGHT\r\nCOL_DEATH_BOTTOM_LEFT\r\nCOL_NONE\r\nCOL_NONE\r\nCOL_BOTTOM_CENTER_SPIKE\r\nCOL_BOTTOM_CENTER_SPIKE\r\nCOL_TOP\r\nCOL_BOTTOM\r\nCOL_NONE\r\nCOL_NONE\r\nCOL_NONE\r\nCOL_NONE\r\nCOL_NONE\r\n\r\nCOL_NONE\r\nCOL_NONE\r\nCOL_NONE\r\nCOL_ALL\r\nCOL_NONE\r\nCOL_NONE\r\nCOL_NONE\r\nCOL_NONE\r\nCOL_NONE\r\nCOL_DOWN_RIGHT_SPIKE\r\nCOL_DOWN_LEFT_SPIKE\r\nCOL_UP_LEFT_SPIKE\r\nCOL_UP_RIGHT_SPIKE\r\nCOL_LEFT\r\nCOL_RIGHT\r\nCOL_UP_RIGHT\r\n\r\nCOL_RIGHT\r\nCOL_TOP\r\nCOL_TOP\r\nCOL_UP_LEFT\r\nCOL_TOP\r\nCOL_RIGHT\r\nCOL_BOTTOM\r\nCOL_TOP\r\nCOL_NONE\r\nCOL_NONE\r\nCOL_NONE\r\nCOL_NONE\r\nCOL_NONE\r\nCOL_NONE\r\nCOL_NONE\r\nCOL_NONE\r\n\r\nCOL_DOWN_RIGHT_SPIKE\r\nCOL_DOWN_LEFT_SPIKE\r\nCOL_UP_RIGHT_SPIKE\r\nCOL_UP_LEFT_SPIKE\r\nCOL_DOWN_RIGHT_SPIKE\r\nCOL_DOWN_LEFT_SPIKE\r\nCOL_DEATH\r\nCOL_RIGHT\r\nCOL_LEFT\r\nCOL_UP_RIGHT_SPIKE\r\nCOL_UP_LEFT_SPIKE\r\nCOL_DEATH\r\nCOL_ALL\r\nCOL_LEFT\r\nCOL_DOWN_RIGHT\r\nCOL_DOWN_LEFT\r\n";
		Regex regex = new Regex("COL_[A-Z0-9_]+");
		string[] array = text.Split(new char[2] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
		int num = 0;
		string[] array2 = array;
		foreach (string text2 in array2)
		{
			if (num >= table.Length)
			{
				break;
			}
			string input = text2.Trim();
			Match match = regex.Match(input);
			if (match.Success && Enum.TryParse<MetatileCollision>(match.Value, out var result))
			{
				table[num] = result;
			}
			num++;
		}
	}

	public static MetatileCollision GetCollision(byte index)
	{
		return table[index];
	}
}
