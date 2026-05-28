namespace FamidashEditor;

public static class GameModePhysics
{
	public const int ROBOT_JUMP_TIME = 19;

	public const int SHIP_GRAVITY_BASE = 42;

	public const int MINI_SHIP_GRAVITY_BASE = 49;

	public const int SHIP_GRAVITY_AFTER_HOLD = 50;

	public const int MINI_SHIP_GRAVITY_AFTER_HOLD = 59;

	public const int SHIP_GRAVITY_HOLD_FALL = 52;

	public const int MINI_SHIP_GRAVITY_HOLD_FALL = 62;

	public static int GetTableIdx(bool gravityUp, bool mini)
	{
		return (gravityUp ? 1 : 0) | (mini ? 4 : 0);
	}

	public static int JUMP_VEL(int table_idx)
	{
		bool mini = (table_idx & 4) != 0;
		return SharedPhysics.GetCubeJumpVel(mini);
	}

	public static int CUBE_MAX_FALLSPEED(int table_idx, int configuredMaxFallSpeed)
	{
		return configuredMaxFallSpeed;
	}

	public static int CUBE_GRAVITY(int table_idx)
	{
		bool mini = (table_idx & 4) != 0;
		return SharedPhysics.GetCubeGravity(mini);
	}

	public static int ROBOT_JUMP_VEL(int table_idx)
	{
		return -688;
	}

	public static int SHIP_GRAVITY(int table_idx)
	{
		int num = 4 + (table_idx & 3);
		int[] array = new int[8] { 48, -48, 57, -57, 34, -34, 39, -39 };
		return array[num];
	}

	public static int SHIP_MAX_FALLSPEED(int table_idx)
	{
		int num = 4 + (table_idx & 3);
		int[] array = new int[8] { 873, -873, 1027, -1027, 727, -727, 855, -855 };
		return array[num];
	}

	public static int SHIP_MAX_FALLSPEED_HOLD(int table_idx)
	{
		int num = 4 + (table_idx & 3);
		int[] array = new int[8] { 1091, -1091, 1283, -1283, 909, -909, 1069, -1069 };
		return array[num];
	}

	public static int BALL_GRAVITY(int table_idx)
	{
		bool mini = (table_idx & 4) != 0;
		return SharedPhysics.BallGravity(mini);
	}

	public static int BALL_MAX_FALLSPEED(int table_idx)
	{
		bool mini = (table_idx & 4) != 0;
		return SharedPhysics.BallMaxFallSpeed(mini);
	}

	public static int BALL_SWITCH_VEL(int table_idx)
	{
		bool mini = (table_idx & 4) != 0;
		bool flag = (table_idx & 1) != 0;
		int num = SharedPhysics.BallSwitchVel(mini);
		return flag ? num : (-num);
	}

	public static int UFO_JUMP_VEL(int table_idx)
	{
		bool mini = (table_idx & 4) != 0;
		return -SharedPhysics.UfoJumpVel(mini);
	}

	public static int UFO_GRAVITY(int table_idx)
	{
		bool mini = (table_idx & 4) != 0;
		return SharedPhysics.UfoGravity(mini);
	}

	public static int UFO_MAX_FALLSPEED(int table_idx)
	{
		bool mini = (table_idx & 4) != 0;
		return SharedPhysics.UfoMaxFallSpeed(mini);
	}

	public static int SPIDER_GRAVITY(int table_idx)
	{
		return 75;
	}

	public static int SPIDER_MAX_FALLSPEED(int table_idx)
	{
		return 1536;
	}

	public static int SWING_GRAVITY(int table_idx)
	{
		return ((table_idx & 4) != 0) ? 56 : 50;
	}

	public static int SWING_MAX_FALLSPEED(int table_idx)
	{
		return 1072;
	}
}
