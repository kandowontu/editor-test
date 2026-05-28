using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace FamidashEditor;

public class PathfinderEngine
{
	private class BacktrackCheckpoint
	{
		public SimState State;

		public int Frame;

		public bool HoldJumpState;

		public int HoldDelayState;

		public int CommittedDelayState;

		public int PathPointCount;

		public int InputCount;

		public int RetryStage;

		public double UsedBias;

		public int ShipBias;

		public int GameMode;

		public int ShipForceHold;

		public int ShipForceRelease;

		public int ShipCommitFrames;

		public bool ShipCommitHold;

		public int ForceJumpRemaining;

		public bool SkipAllOrbs;

		public HashSet<int> SkipSpecificOrbs = new HashSet<int>();

		public HashSet<int> SkipSpecificPads = new HashSet<int>();

		public bool PrevFrameWasGrounded;

		public int NextCoinCheckIdx;
	}

	private struct SpriteEntry
	{
		public int Index;

		public int SpriteId;

		public int AnchorX_px;

		public int AnchorY_px;

		public int HitLeft;

		public int HitTop;

		public int HitRight;

		public int HitBottom;
	}

	private struct SimState
	{
		public int X_fixed;

		public int Y_fixed;

		public int VelY_fixed;

		public int VelX_fixed;

		public int GameMode;

		public bool GravFlipped;

		public bool Mini;

		public int GravMul;

		public double GravityMod;

		public bool WasZeroedByCollision;

		public bool OnGround;

		public int BallFlipCooldown;

		public int BallInputBuffer;

		public int BallCooldownFrames;

		public int RobotJumpTime;

		public int NinjaJumps;

		public SpriteSet ProcessedSprites;

		public int PendingOrbIndex;

		public int PendingOrbSpriteId;

		public int PendingOrbExtra1Index;

		public int PendingOrbExtra1SpriteId;

		public int PendingOrbExtra2Index;

		public int PendingOrbExtra2SpriteId;

		public int Dashing;

		public bool Orbed;

		public bool BlackOrbed;

		public bool PrevInputHeld;

		public bool JBlocked;

		public bool FBlocked;

		public bool HBlocked;

		public bool Dblocked;

		public bool Step2Ejected;

		public bool Step2Ever;

		public int SlopeWasOnCounter;

		public int SlopeFrames;

		public int SlopeType;

		public bool SlopeJumpHigher;

		public int LastSlopeType;

		public byte DeathType;

		public int CameraY_fixed;

		public int TargetCameraY_fixed;

		public bool NoCamLockForced;

		public bool WrapMode;

		public int RainbowMaxMode;

		public SimState[]? RainbowShadows;

		public bool DualActive;

		public int P2_Y_fixed;

		public int P2_VelY_fixed;

		public bool P2_GravFlipped;

		public int P2_GravMul;

		public bool P2_Mini;

		public bool P2_WasZeroedByCollision;

		public bool P2_OnGround;

		public int P2_BallFlipCooldown;

		public int P2_BallInputBuffer;

		public int P2_BallCooldownFrames;

		public int P2_RobotJumpTime;

		public int P2_NinjaJumps;

		public int P2_SlopeWasOnCounter;

		public int P2_SlopeFrames;

		public int P2_SlopeType;

		public int P2_LastSlopeType;

		public bool P2_Orbed;

		public bool P2_BlackOrbed;

		public bool P2_PrevInputHeld;

		public int P2_Dashing;

		public bool P2_JBlocked;

		public bool P2_FBlocked;

		public bool P2_HBlocked;

		public bool P2_Dblocked;

		public int P2_PendingOrbIndex;

		public int P2_PendingOrbSpriteId;

		public int P2_PendingOrbExtra1Index;

		public int P2_PendingOrbExtra1SpriteId;

		public int P2_PendingOrbExtra2Index;

		public int P2_PendingOrbExtra2SpriteId;

		public SimState Clone()
		{
			SimState result = this;
			result.ProcessedSprites = ProcessedSprites.Clone();
			if (RainbowShadows != null)
			{
				result.RainbowShadows = new SimState[RainbowShadows.Length];
				for (int i = 0; i < RainbowShadows.Length; i++)
				{
					result.RainbowShadows[i] = RainbowShadows[i];
					result.RainbowShadows[i].ProcessedSprites = RainbowShadows[i].ProcessedSprites.Clone();
				}
			}
			return result;
		}

		public void ReturnAllSpriteResources()
		{
			ProcessedSprites.Return();
			if (RainbowShadows != null)
			{
				for (int i = 0; i < RainbowShadows.Length; i++)
				{
					RainbowShadows[i].ProcessedSprites.Return();
				}
				RainbowShadows = null;
			}
		}
	}

	private sealed class SpriteSet
	{
		internal readonly ulong[] Bits;

		private readonly int _logicalLen;

		private readonly int[] _map;

		private readonly bool _rented;

		public int Count
		{
			get
			{
				int num = 0;
				for (int i = 0; i < _logicalLen; i++)
				{
					num += BitOperations.PopCount(Bits[i]);
				}
				return num;
			}
		}

		public SpriteSet(int[] compactMap, int compactCount)
		{
			_map = compactMap;
			_logicalLen = Math.Max(1, compactCount + 63 >> 6);
			Bits = new ulong[_logicalLen];
			_rented = false;
		}

		private SpriteSet(ulong[] srcBits, int logicalLen, int[] compactMap)
		{
			_map = compactMap;
			_logicalLen = logicalLen;
			Bits = ArrayPool<ulong>.Shared.Rent(logicalLen);
			_rented = true;
			Buffer.BlockCopy(srcBits, 0, Bits, 0, logicalLen * 8);
			if (Bits.Length > logicalLen)
			{
				Array.Clear(Bits, logicalLen, Bits.Length - logicalLen);
			}
		}

		public void Add(int tileIndex)
		{
			int num = _map[tileIndex];
			Bits[num >> 6] |= (ulong)(1L << num);
		}

		public bool Contains(int tileIndex)
		{
			if ((uint)tileIndex >= (uint)_map.Length)
			{
				return false;
			}
			int num = _map[tileIndex];
			if (num < 0)
			{
				return false;
			}
			return (Bits[num >> 6] & (ulong)(1L << num)) != 0;
		}

		public void Remove(int tileIndex)
		{
			int num = _map[tileIndex];
			if (num >= 0)
			{
				Bits[num >> 6] &= (ulong)(~(1L << num));
			}
		}

		public SpriteSet Clone()
		{
			return new SpriteSet(Bits, _logicalLen, _map);
		}

		public void Return()
		{
			if (_rented)
			{
				ArrayPool<ulong>.Shared.Return(Bits);
			}
		}

		public int GetBitsHash()
		{
			int num = 0;
			for (int i = 0; i < _logicalLen; i++)
			{
				ulong num2 = Bits[i];
				num = (num * 397) ^ (int)num2 ^ (int)(num2 >> 32);
			}
			return num;
		}
	}

	private const int TILE = 16;

	private const int NES_H = 15;

	private const int SCREEN_H_PX = 240;

	private const int SHIP_SCROLL_SPEED_DOWN_FIXED = 768;

	private const int SHIP_SCROLL_SPEED_UP_FIXED = 512;

	private const int PORTAL_TO_TOP_DIFF_PX = 58;

	private const int LOOKAHEAD_HORIZON = 90;

	private const int MAX_SPECULATIVE_DEPTH = 3;

	private const int CORRIDOR_LOOK_AHEAD_TILES = 15;

	private const int SHIP_LOOKAHEAD_HORIZON = 30;

	private const int SHIP_TREE_DEPTH = 20;

	private const int SHIP_TREE_MAX_NODES = 16000;

	private const int MAX_FRAMES = 28800;

	private const int MAX_BACKTRACK_ATTEMPTS = 500;

	private const int MAX_TOTAL_BACKTRACK_ATTEMPTS = 5000;

	private const int MAX_TOTAL_ITERATIONS = 288000;

	private const int MAX_CHECKPOINT_DEPTH = 200;

	private const int MIN_CHECKPOINT_SPACING = 4;

	private const int ELEV_THRESHOLD = 12;

	public volatile bool CancelRequested;

	private TextWriter _log = TextWriter.Null;

	private volatile int _currentX_px;

	private const int CUBE_HITBOX_W = 15;

	private const int CUBE_HITBOX_H = 15;

	private const int MINI_CUBE_HITBOX_W = 8;

	private const int MINI_CUBE_HITBOX_H = 7;

	private const int ROBOT_JUMP_VEL = -688;

	private const int ROBOT_JUMP_TIME = 19;

	private const int NINJA_MAX_JUMPS = 3;

	private const int BALL_INPUT_BUFFER_FRAMES = 8;

	private bool _cubeJumpedThisStep;

	private bool _cubeHoldJump = false;

	private int _cubeHoldDelay = 0;

	private int _committedJumpDelay = -1;

	private int _committedRobotHold = 0;

	private Queue<int> _committedNinjaJumps = new Queue<int>();

	private int _ninjaWaitFrames = 0;

	private bool _prevFrameWasGrounded = true;

	private List<BacktrackCheckpoint> _backtrackCheckpoints = new List<BacktrackCheckpoint>();

	private BacktrackCheckpoint? _lastCubeToShipCheckpoint;

	private BacktrackCheckpoint? _shipEntryRecoveryCheckpoint;

	private int _backtrackAttempts;

	private int _totalBacktrackAttempts;

	private int _btOverrideFrame = -1;

	private int _btOverrideStage = 0;

	private int _btOverrideDistFromDeath;

	private bool _backtrackActive;

	private bool _btSuppressJumpUntilAirborne;

	private bool _btSkipAllOrbs;

	private HashSet<int> _btSkipSpecificOrbs = new HashSet<int>();

	private List<int> _hitOrbHistory = new List<int>();

	private HashSet<int> _btSkipSpecificPads = new HashSet<int>();

	private List<int> _hitPadHistory = new List<int>();

	private int _btForceJumpFramesRemaining;

	private int _btDeathFrame;

	private int _shipCorridorBias;

	private int _shipForceReleaseFirstFrames;

	private int _shipForceHoldFrames;

	private int _shipForceReleaseFrames;

	private int _shipCommitFrames;

	private bool _shipCommitHold;

	private int _cubeGroundedWalkFrames;

	private int _ballGroundedWalkFrames;

	private int _lastSpecMinLandY;

	private int _bestPathHighWaterX;

	private List<(int x, int y)> _bestPathPoints = new List<(int, int)>();

	private List<(int x, int y)> _bestPath2Points = new List<(int, int)>();

	private List<bool> _bestInputs = new List<bool>();

	private bool _prevDualActiveForPath;

	private Stopwatch? _backtrackTimer;

	private const int MAX_BACKTRACK_SECONDS = 60;

	private const int MAX_BACKTRACK_SECONDS_COIN = 120;

	private int _speculativeDepth;

	private int _frameCounter;

	[ThreadStatic]
	private static bool _dualP2Guard;

	[ThreadStatic]
	private static int? _dualP2FrameEntryVelXOverride;

	[ThreadStatic]
	private static List<int>? _p1OrbIndicesThisFrame;

	[ThreadStatic]
	private static bool _p2OrbFlippedOtherGrav;

	private string pfDebugLogPath = string.Empty;

	private string pfOrbDebugLogPath = string.Empty;

	private static readonly string[] _orbLogTagPrefixes = new string[9] { "[ORB", "[DASH_ORB", "[DECIDE_ORB", "[PAD", "[BLUE_PAD", "[GREEN_PAD", "[YELLOW_PAD", "[GRAV_PAD", "[ORB_TRACK" };

	private string _frameTracePath = Path.Combine(Path.GetTempPath(), "famidash_pf_trace.csv");

	private StreamWriter? _traceWriter;

	private readonly int[] tiles;

	private readonly int[] sprites;

	private readonly Dictionary<int, (int anchorTileX, int anchorTileY)> spriteAnchors;

	private readonly Dictionary<int, (int offsetX, int offsetY)> spritePixelOffsets;

	private readonly int mapWidth;

	private readonly int mapHeight;

	private readonly int groundRowsToReserve;

	private readonly int maxFallSpeed;

	private readonly SharedPhysics.CollisionMap _collisionMap;

	private readonly int _nesCoordOffset;

	private readonly List<SpriteEntry> allSprites;

	private SpriteEntry[] _spritesArr;

	private readonly List<SpriteEntry> allCoins;

	private readonly List<(int X, int Y)> _modePortalPositions;

	private int _nextCoinCheckIdx;

	private readonly HashSet<int> _forgivenCoins = new HashSet<int>();

	private readonly Dictionary<int, int> _coinMissRetryCount = new Dictionary<int, int>();

	private const int MAX_COIN_MISS_RETRIES = 5;

	private readonly HashSet<int> _autoForgivenCoins = new HashSet<int>();

	private readonly Dictionary<int, int> _coinCollectThenLoseCount = new Dictionary<int, int>();

	private const int MAX_COIN_COLLECT_LOSE = 5;

	private readonly Dictionary<int, int> _forgivenCoinGameModes = new Dictionary<int, int>();

	private readonly Dictionary<int, int> _permanentlyCollectedCoins = new Dictionary<int, int>();

	private int _missedCoinIdx = -1;

	private List<(int startX, int endX, int ceilingY)> _coinAltitudePenalties = new List<(int, int, int)>();

	private List<(int startX, int endX)> _coinForceWalkZones = new List<(int, int)>();

	private List<(int startX, int endX)> _coinGroundBiasZones = new List<(int, int)>();

	private HashSet<int> _retryMandatoryCoins = new HashSet<int>();

	private int _shipCoinAggressiveThreshold = 3;

	private int _modeTransitionStabilizeFrames = 0;

	private Queue<bool> _coinInputScript = new Queue<bool>();

	private int _coinInputScriptCoinIdx = -1;

	private int _beamSearchAttemptedCoinIdx = -1;

	private int _beamSearchLastDistX = int.MaxValue;

	private int _cubeBeamSearchAttemptedCoinIdx = -1;

	private readonly Dictionary<int, int> _crossCorrForgivenCoins = new Dictionary<int, int>();

	private string _lastDeathReason = "";

	private int _lastDeathX;

	private int _lastDeathY;

	private static readonly int[] sprite_widths = SharedPhysics.sprite_widths;

	private static readonly int[] sprite_heights = SharedPhysics.sprite_heights;

	private static readonly int[] sprite_x_offset = SharedPhysics.sprite_x_offset;

	private static readonly int[] sprite_y_offset = SharedPhysics.sprite_y_offset;

	private int[] _spriteCompactMap;

	private int _spriteCompactCount;

	private const int BFS_MAX_FRONTIER = 120000;

	private string _btOrigDeathReason = "";

	private int _btOrigDeathY = 0;

	private int _shipTreeNodesExplored;

	private int _ballEjectTraceCount = 0;

	private int _step1FireCount = 0;

	private int _step2FireCount = 0;

	private int[]? _gravFDeathCounts;

	private int _gravFDeathFrame = -1;

	private int _gravFDeathSamples = 0;

	private const int PF_SLOPE_RISING = 4;

	private static readonly short[] PF_EXIT_SLOPE_BALL_22 = SharedPhysics.EXIT_SLOPE_BALL_22;

	private static readonly short[] PF_EXIT_SLOPE_BALL_66 = SharedPhysics.EXIT_SLOPE_BALL_66;

	private static readonly short[] PF_EXIT_SLOPE_CUBE_22 = SharedPhysics.EXIT_SLOPE_CUBE_22;

	private const int PF_SLOPE_UD = 8;

	public double JumpTimingBias { get; set; } = 0.5;

	public IProgress<int>? Progress { get; set; }

	public bool PreferCoins { get; set; } = false;

	public int CoinsCollected { get; private set; } = 0;

	public HashSet<int>? FinalCollectedCoinIndices { get; private set; }

	public HashSet<int>? SkippedPadIndices { get; private set; }

	public bool UseBFS { get; set; } = false;

	public bool Verbose { get; set; } = false;

	public Action<List<(int x, int y)>?, int, int, bool>? OnSpeculativePath { get; set; }

	public int CurrentSpeculativeVizMode { get; private set; } = -1;

	public int CurrentX_px => _currentX_px;

	public string DebugLogPath => pfDebugLogPath;

	public string OrbDebugLogPath => pfOrbDebugLogPath;

	public string LevelName { get; set; } = "";

	public int? ConfigScrollYHi { get; set; }

	public int? ConfigScrollYLo { get; set; }

	public int? ConfigSpawnYLo { get; set; }

	public string FrameTracePath => _frameTracePath;

	public List<(int x, int y)> PathPoints { get; private set; } = null;

	public List<(int x, int y)> Path2Points { get; private set; } = null;

	public List<bool> Inputs { get; private set; } = null;

	public bool Success { get; private set; }

	public string ResultMessage { get; private set; } = "";

	public int NesYOffset => _nesCoordOffset;

	public List<List<(int x, int y)>> AttemptedPaths { get; private set; } = new List<List<(int, int)>>();

	private static int SpeedUiIndexToFixed(int uiIndex)
	{
		return SharedPhysics.SpeedUiIndexToFixed(uiIndex);
	}

	private static int SpriteIdToSpeedFixed(int sid)
	{
		return SharedPhysics.SpriteIdToSpeedFixed(sid);
	}

	private static int SpriteIdToGameMode(int sid)
	{
		return SharedPhysics.SpriteIdToGameMode(sid);
	}

	private static int GetGravity(bool mini)
	{
		return SharedPhysics.GetCubeGravity(mini);
	}

	private static int GetJumpVel(bool mini)
	{
		return SharedPhysics.GetCubeJumpVel(mini);
	}

	private static int GetHitboxW(bool mini)
	{
		return SharedPhysics.GetCubeHitboxW(mini);
	}

	private static int GetHitboxH(bool mini)
	{
		return SharedPhysics.GetCubeHitboxH(mini);
	}

	private static int GetHitboxOffsetY(int gameMode, bool mini, bool gravFlipped)
	{
		return SharedPhysics.GetHitboxOffsetY(gameMode, mini, gravFlipped);
	}

	private static int BallGravity(bool mini)
	{
		return SharedPhysics.BallGravity(mini);
	}

	private static int BallSwitchVel(bool mini)
	{
		return SharedPhysics.BallSwitchVel(mini);
	}

	private static int BallMaxFallSpeed(bool mini)
	{
		return SharedPhysics.BallMaxFallSpeed(mini);
	}

	private static int ShipGravityBase(bool mini)
	{
		return SharedPhysics.ShipGravityBase(mini);
	}

	private static int ShipGravityAfterHold(bool mini)
	{
		return SharedPhysics.ShipGravityAfterHold(mini);
	}

	private static int ShipGravityHoldFall(bool mini)
	{
		return SharedPhysics.ShipGravityHoldFall(mini);
	}

	private static int ShipGravity(bool mini)
	{
		return SharedPhysics.ShipGravity(mini);
	}

	private static int ShipMaxFallSpeed(bool mini)
	{
		return SharedPhysics.ShipMaxFallSpeed(mini);
	}

	private static int ShipMaxFallSpeedHold(bool mini)
	{
		return SharedPhysics.ShipMaxFallSpeedHold(mini);
	}

	private static int UfoGravity(bool mini)
	{
		return SharedPhysics.UfoGravity(mini);
	}

	private static int UfoJumpVel(bool mini)
	{
		return SharedPhysics.UfoJumpVel(mini);
	}

	private static int UfoMaxFallSpeed(bool mini)
	{
		return SharedPhysics.UfoMaxFallSpeed(mini);
	}

	private static bool IsSpeedPortal(int sid)
	{
		return SharedPhysics.IsSpeedPortal(sid);
	}

	private static bool IsGameModePortal(int sid)
	{
		return SharedPhysics.IsGameModePortal(sid);
	}

	private static bool IsGravityPortal(int sid)
	{
		return SharedPhysics.IsGravityPortal(sid);
	}

	private static bool IsReverseGravity(int sid)
	{
		return SharedPhysics.IsReverseGravity(sid);
	}

	private static bool IsMiniGrowthPortal(int sid)
	{
		return SharedPhysics.IsMiniGrowthPortal(sid);
	}

	private static bool IsEndLevel(int sid)
	{
		return SharedPhysics.IsEndLevel(sid);
	}

	private static bool IsYellowPad(int sid)
	{
		return SharedPhysics.IsYellowPad(sid);
	}

	private static bool IsPinkPad(int sid)
	{
		return SharedPhysics.IsPinkPad(sid);
	}

	private static bool IsRedPad(int sid)
	{
		return SharedPhysics.IsRedPad(sid);
	}

	private static bool IsBluePad(int sid)
	{
		return SharedPhysics.IsBluePad(sid);
	}

	private void ExtractSkippedPads(in SimState finalState)
	{
		SkippedPadIndices = new HashSet<int>();
		foreach (SpriteEntry allSprite in allSprites)
		{
			if (finalState.ProcessedSprites.Contains(allSprite.Index) && IsAnyPad(allSprite.SpriteId))
			{
				SkippedPadIndices.Add(allSprite.Index);
				PfLog($"[SKIPPED_PAD] idx={allSprite.Index} sid=0x{allSprite.SpriteId:X2}");
			}
		}
	}

	private static bool IsGreenPad(int sid)
	{
		return SharedPhysics.IsGreenPad(sid);
	}

	private static bool IsYellowOrb(int sid)
	{
		return SharedPhysics.IsYellowOrb(sid);
	}

	private static bool IsYellowOrbBigger(int sid)
	{
		return SharedPhysics.IsYellowOrbBigger(sid);
	}

	private static bool IsYellowOrbSmaller(int sid)
	{
		return SharedPhysics.IsYellowOrbSmaller(sid);
	}

	private static bool IsPinkOrb(int sid)
	{
		return SharedPhysics.IsPinkOrb(sid);
	}

	private static bool IsRedOrb(int sid)
	{
		return SharedPhysics.IsRedOrb(sid);
	}

	private static bool IsBlueOrb(int sid)
	{
		return SharedPhysics.IsBlueOrb(sid);
	}

	private static bool IsGreenOrb(int sid)
	{
		return SharedPhysics.IsGreenOrb(sid);
	}

	private static bool IsBlackOrb(int sid)
	{
		return SharedPhysics.IsBlackOrb(sid);
	}

	private static bool IsWhiteOrb(int sid)
	{
		return SharedPhysics.IsWhiteOrb(sid);
	}

	private static bool IsVelocityOrb(int sid)
	{
		return SharedPhysics.IsVelocityOrb(sid);
	}

	private static bool IsGravityOrb(int sid)
	{
		return SharedPhysics.IsGravityOrb(sid);
	}

	private static bool IsOrbSprite(int sid)
	{
		return SharedPhysics.IsOrbSprite(sid);
	}

	private static int GetPadOrbModeCol(int gameMode)
	{
		return SharedPhysics.GetPadOrbModeCol(gameMode);
	}

	private static int GetPadOrbVel(int row, bool mini, int gameMode)
	{
		return SharedPhysics.GetPadOrbVel(row, mini, gameMode);
	}

	private int ScanForOrbOverlap(in SimState state, out int orbSpriteIndex)
	{
		orbSpriteIndex = -1;
		int num = state.X_fixed >> 8;
		int num2 = num + 1;
		int hitboxW = GetHitboxW(state.Mini);
		int hitboxH = GetHitboxH(state.Mini);
		int num3 = ((state.Mini && !state.GravFlipped) ? SharedPhysics.GetMiniCenterOffsetY(mini: true) : 0);
		int num4 = state.Y_fixed >> 8;
		int num5 = num4 + num3;
		int num6 = num5 + hitboxH;
		int num7 = num2 + hitboxW;
		foreach (SpriteEntry allSprite in allSprites)
		{
			if (state.ProcessedSprites.Contains(allSprite.Index) || allSprite.HitRight < num2)
			{
				continue;
			}
			if (allSprite.AnchorX_px - 16 > num7 + 16)
			{
				break;
			}
			int spriteId = allSprite.SpriteId;
			if (IsOrbSprite(spriteId))
			{
				bool flag = num7 >= allSprite.HitLeft && allSprite.HitRight >= num2;
				bool flag2 = num6 >= allSprite.HitTop && allSprite.HitBottom >= num5;
				if (flag && flag2)
				{
					orbSpriteIndex = allSprite.Index;
					return spriteId;
				}
			}
		}
		return -1;
	}

	private int ScanForPadOverlap(in SimState state, out int padSpriteIndex)
	{
		padSpriteIndex = -1;
		int num = state.X_fixed >> 8;
		int hitboxW = GetHitboxW(state.Mini);
		int hitboxH = GetHitboxH(state.Mini);
		int hitboxOffsetY = GetHitboxOffsetY(state.GameMode, state.Mini, state.GravFlipped);
		int num2 = state.Y_fixed >> 8;
		int num3 = num2 + hitboxOffsetY;
		int num4 = num3 + hitboxH;
		foreach (SpriteEntry allSprite in allSprites)
		{
			if (state.ProcessedSprites.Contains(allSprite.Index) || allSprite.HitRight < num)
			{
				continue;
			}
			int num5 = num + 1;
			int num6 = num5 + hitboxW;
			if (allSprite.AnchorX_px - 16 > num6 + 16)
			{
				break;
			}
			int spriteId = allSprite.SpriteId;
			if (!IsYellowPad(spriteId) && !IsPinkPad(spriteId) && !IsRedPad(spriteId) && !IsBluePad(spriteId) && !IsGreenPad(spriteId))
			{
				continue;
			}
			if (IsBluePad(spriteId))
			{
				bool flag = spriteId == 13 || spriteId == 253;
				if ((flag && state.GravFlipped) || (!flag && !state.GravFlipped))
				{
					continue;
				}
			}
			int num7 = ((!IsBluePad(spriteId)) ? 1 : 0);
			int num8 = num + num7;
			int num9 = num8 + hitboxW;
			bool flag2 = num9 >= allSprite.HitLeft && allSprite.HitRight >= num8;
			bool flag3 = num4 >= allSprite.HitTop && allSprite.HitBottom >= num3;
			if (!(flag2 && flag3))
			{
				continue;
			}
			padSpriteIndex = allSprite.Index;
			return spriteId;
		}
		return -1;
	}

	private static bool IsAnyPad(int sid)
	{
		return SharedPhysics.IsAnyPad(sid);
	}

	private void EnsureDebugPathsStamped()
	{
		if (pfDebugLogPath.Length != 0)
		{
			return;
		}
		try
		{
			string value = SanitizeLevelTag(LevelName ?? "");
			string value2 = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
			string tempPath = Path.GetTempPath();
			pfDebugLogPath = Path.Combine(tempPath, $"famidash_pf_debug_{value}_{value2}.txt");
			pfOrbDebugLogPath = Path.Combine(tempPath, $"famidash_pf_orb_debug_{value}_{value2}.log");
		}
		catch
		{
			string text = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
			string tempPath2 = Path.GetTempPath();
			pfDebugLogPath = Path.Combine(tempPath2, "famidash_pf_debug_unknown_" + text + ".txt");
			pfOrbDebugLogPath = Path.Combine(tempPath2, "famidash_pf_orb_debug_unknown_" + text + ".log");
		}
	}

	private static bool MatchesOrbTag(string msg)
	{
		for (int i = 0; i < _orbLogTagPrefixes.Length; i++)
		{
			if (msg.StartsWith(_orbLogTagPrefixes[i], StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	private void PfLog(string msg)
	{
		if (_speculativeDepth > 0)
		{
			return;
		}
		try
		{
			EnsureDebugPathsStamped();
			string text = $"[PF f={_frameCounter}] {msg}";
			File.AppendAllText(pfDebugLogPath, text + Environment.NewLine);
			if (MatchesOrbTag(msg))
			{
				File.AppendAllText(pfOrbDebugLogPath, text + Environment.NewLine);
			}
		}
		catch
		{
		}
	}

	private int ComputeInitCameraY(int startY_px)
	{
		int val = Math.Max(0, (mapHeight - 15) * 16) << 8;
		int val2 = -(groundRowsToReserve * 16) << 8;
		if (ConfigScrollYHi.HasValue)
		{
			int num = ConfigScrollYHi.Value & 0xFF;
			int num2 = (ConfigScrollYLo.HasValue ? ConfigScrollYLo.Value : 0) & 0xFF;
			int num3 = num * 240 + num2;
			int num4 = 719;
			int num5 = num4 - num3;
			int num6 = (mapHeight - 15) * 16;
			int num7 = Math.Max(0, num6 - num5);
			return Math.Max(val2, Math.Min(val, num7 << 8));
		}
		return Math.Max(val2, Math.Min(val, (startY_px << 8) - 30720));
	}

	private void ApplyNesIntroFreezePrestep(ref SimState s)
	{
		if ((s.GameMode == 0 || s.GameMode == 4) && !s.DualActive)
		{
			int num = SharedPhysics.GetCubeGravity(s.Mini);
			if (s.GravFlipped)
			{
				num = -num;
			}
			s.VelY_fixed += num;
			s.Y_fixed += s.VelY_fixed;
			s.X_fixed += s.VelX_fixed;
			PfLog($"[INTRO_PRESTEP] X=0x{s.X_fixed:X4} Y=0x{s.Y_fixed:X4} VelY=0x{s.VelY_fixed:X4} gravity=0x{num:X4} mode={s.GameMode}");
		}
	}

	private int NesNtCameraTarget_fixed(int portalWorldY_px)
	{
		int num = portalWorldY_px - 58;
		int num2 = num + _nesCoordOffset;
		if (num2 < 256)
		{
			return Math.Max(0, num << 8);
		}
		if ((num2 & 0xFF) >= 240)
		{
			num2 += 16;
		}
		int num3 = num2 >> 8;
		int num4 = num2 & 0xFF;
		int num5 = num3 * 240 + num4;
		int num6 = num5 - _nesCoordOffset;
		return Math.Max(0, num6 << 8);
	}

	private static string SanitizeLevelTag(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return "unknown";
		}
		StringBuilder stringBuilder = new StringBuilder(name.Length);
		foreach (char c in name)
		{
			if (char.IsLetterOrDigit(c))
			{
				stringBuilder.Append(c);
			}
			else
			{
				stringBuilder.Append('_');
			}
		}
		return stringBuilder.ToString();
	}

	private void TraceFrameOpen()
	{
		pfDebugLogPath = string.Empty;
		pfOrbDebugLogPath = string.Empty;
		EnsureDebugPathsStamped();
		try
		{
			string value = SanitizeLevelTag(LevelName ?? "");
			string value2 = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
			string tempPath = Path.GetTempPath();
			_frameTracePath = Path.Combine(tempPath, $"famidash_pf_trace_{value}_{value2}.csv");
		}
		catch
		{
		}
		try
		{
			File.WriteAllText(pfOrbDebugLogPath, $"# pf orb debug {DateTime.UtcNow:o} level={LevelName}{Environment.NewLine}");
		}
		catch
		{
		}
		try
		{
			_traceWriter = new StreamWriter(_frameTracePath, append: false);
			_traceWriter.WriteLine("frame,X_fixed,Y_fixed,VelY_fixed,input,alive,X_px,Y_px,onGround,CamY_px,TgtCamY_px");
		}
		catch
		{
			_traceWriter = null;
		}
		try
		{
			if (PfTrace.Enabled)
			{
				PfTrace.Open(LevelName ?? "unknown");
			}
		}
		catch
		{
		}
	}

	private void TraceFrame(int frame, ref SimState s, bool input, bool alive)
	{
		if (_traceWriter == null || _speculativeDepth > 0)
		{
			return;
		}
		try
		{
			int value = (s.X_fixed >> 8) + 8;
			int value2 = ((!s.DualActive && (s.GameMode == 0 || s.GameMode == 4 || s.GameMode == 8 || s.GameMode == 9 || s.NoCamLockForced)) ? ((s.CameraY_fixed >> 8) + (s.Y_fixed - s.CameraY_fixed >> 8) + 8) : ((s.Y_fixed >> 8) + 8));
			_traceWriter.WriteLine($"{frame},0x{s.X_fixed:X},0x{s.Y_fixed:X},0x{s.VelY_fixed:X},{(input ? 1 : 0)},{(alive ? 1 : 0)},{value},{value2},{(s.OnGround ? 1 : 0)},{s.CameraY_fixed >> 8},{s.TargetCameraY_fixed >> 8}");
		}
		catch
		{
		}
	}

	private void TraceFrameClose()
	{
		try
		{
			_traceWriter?.Flush();
			_traceWriter?.Dispose();
			_traceWriter = null;
		}
		catch
		{
		}
		try
		{
			PfTrace.Close();
		}
		catch
		{
		}
	}

	private static bool IsCoinSprite(int sid)
	{
		return SharedPhysics.IsCoinSprite(sid);
	}

	private static bool IsMiniCoinSprite(int sid)
	{
		return SharedPhysics.IsMiniCoinSprite(sid);
	}

	private static bool IsDashOrb(int sid)
	{
		return SharedPhysics.IsDashOrb(sid);
	}

	private static bool IsGravityDashOrb(int sid)
	{
		return SharedPhysics.IsGravityDashOrb(sid);
	}

	private static int DashOrbMode(int sid)
	{
		return SharedPhysics.DashOrbMode(sid);
	}

	private static bool IsSpiderOrb(int sid)
	{
		return SharedPhysics.IsSpiderOrb(sid);
	}

	private static bool IsSpiderPad(int sid)
	{
		return SharedPhysics.IsSpiderPad(sid);
	}

	private static bool IsTeleportPortalEntrance(int sid)
	{
		return SharedPhysics.IsTeleportPortalEntrance(sid);
	}

	private static bool IsTeleportPortalExit(int sid)
	{
		return SharedPhysics.IsTeleportPortalExit(sid);
	}

	private static bool IsVerticalTeleportEntrance(int sid)
	{
		return SharedPhysics.IsVerticalTeleportEntrance(sid);
	}

	private static bool IsBottomRowTeleportExit(int sid)
	{
		return SharedPhysics.IsBottomRowTeleportExit(sid);
	}

	private SpriteSet NewSpriteSet()
	{
		return new SpriteSet(_spriteCompactMap, _spriteCompactCount);
	}

	public string ExportReplayCsv()
	{
		if (PathPoints == null || Inputs == null)
		{
			return string.Empty;
		}
		int num = Math.Min(PathPoints.Count, Inputs.Count);
		if (num == 0)
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder(num * 16);
		stringBuilder.Append("nes_y_offset,").Append(_nesCoordOffset).Append('\n');
		stringBuilder.Append("frame,x,y,a\n");
		for (int i = 0; i < num; i++)
		{
			(int, int) tuple = PathPoints[i];
			stringBuilder.Append(i).Append(',').Append(tuple.Item1)
				.Append(',')
				.Append(tuple.Item2)
				.Append(',')
				.Append(Inputs[i] ? 1 : 0)
				.Append('\n');
		}
		return stringBuilder.ToString();
	}

	public PathfinderEngine(int[] tiles, int[] sprites, Dictionary<int, (int anchorTileX, int anchorTileY)> spriteAnchors, int mapWidth, int mapHeight, bool hasGroundLayer, int groundTileRows, int maxFallSpeed = 6, Dictionary<int, (int offsetX, int offsetY)>? spritePixelOffsets = null)
	{
		this.tiles = (tiles ?? Array.Empty<int>()).Select((int t) => (t >= 0) ? t : 0).ToArray();
		this.sprites = sprites ?? Array.Empty<int>();
		this.spriteAnchors = spriteAnchors ?? new Dictionary<int, (int, int)>();
		this.spritePixelOffsets = spritePixelOffsets ?? new Dictionary<int, (int, int)>();
		this.mapWidth = mapWidth;
		this.mapHeight = mapHeight;
		groundRowsToReserve = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
		_nesCoordOffset = (57 - this.mapHeight + groundRowsToReserve) * 16;
		_collisionMap = new SharedPhysics.CollisionMap(this.tiles, this.mapWidth, this.mapHeight, groundRowsToReserve);
		if (maxFallSpeed >= 256)
		{
			this.maxFallSpeed = maxFallSpeed;
		}
		else
		{
			this.maxFallSpeed = maxFallSpeed << 8;
		}
		allSprites = new List<SpriteEntry>();
		for (int num = 0; num < this.sprites.Length; num++)
		{
			int num2 = this.sprites[num];
			if (num2 == -1)
			{
				continue;
			}
			int num3 = num % mapWidth;
			int num4 = num / mapWidth;
			int num5 = num2 & 0xFF;
			int num6 = -1;
			if (this.spriteAnchors.TryGetValue(num, out (int, int) value))
			{
				num6 = value.Item2 * mapWidth + value.Item1;
				if (num6 >= 0 && num6 < this.sprites.Length)
				{
					int num7 = this.sprites[num6];
					if (num7 >= 0 && num7 < 256)
					{
						num5 = num7 & 0xFF;
					}
				}
			}
			int val = ((num5 >= 0 && num5 < sprite_widths.Length) ? sprite_widths[num5] : 16);
			int val2 = ((num5 >= 0 && num5 < sprite_heights.Length) ? sprite_heights[num5] : 16);
			int num8 = ((num5 >= 0 && num5 < sprite_x_offset.Length) ? sprite_x_offset[num5] : 0);
			int num9 = ((num5 >= 0 && num5 < sprite_y_offset.Length) ? sprite_y_offset[num5] : 0);
			int num10 = 0;
			int num11 = 0;
			(int, int) value3;
			if (num6 >= 0 && this.spritePixelOffsets.TryGetValue(num6, out (int, int) value2))
			{
				(num10, num11) = value2;
			}
			else if (this.spritePixelOffsets.TryGetValue(num, out value3))
			{
				(num10, num11) = value3;
			}
			int num12 = num3 * 16 + num8 + num10;
			int num13 = (num4 - groundRowsToReserve) * 16 + num9 + num11 - 1;
			int hitRight = num12 + Math.Max(1, val);
			int hitBottom = num13 + Math.Max(1, val2);
			int num14;
			if (this.spriteAnchors.TryGetValue(num, out (int, int) value4))
			{
				(num14, _) = value4;
			}
			else
			{
				num14 = num3;
			}
			int num15 = num14;
			allSprites.Add(new SpriteEntry
			{
				Index = num,
				SpriteId = num2,
				AnchorX_px = num15 * 16 + 8,
				AnchorY_px = (num4 - groundRowsToReserve) * 16 + 8,
				HitLeft = num12,
				HitTop = num13,
				HitRight = hitRight,
				HitBottom = hitBottom
			});
		}
		allSprites.Sort(delegate(SpriteEntry a, SpriteEntry b)
		{
			int num22 = a.AnchorX_px.CompareTo(b.AnchorX_px);
			if (num22 != 0)
			{
				return num22;
			}
			int num23 = ((!IsGravityPortal(a.SpriteId)) ? 1 : 0);
			int value5 = ((!IsGravityPortal(b.SpriteId)) ? 1 : 0);
			return num23.CompareTo(value5);
		});
		_spritesArr = allSprites.ToArray();
		_modePortalPositions = new List<(int, int)>();
		allCoins = allSprites.Where((SpriteEntry sp) => IsCoinSprite(sp.SpriteId) || IsMiniCoinSprite(sp.SpriteId)).ToList();
		_spriteCompactMap = new int[this.sprites.Length];
		Array.Fill(_spriteCompactMap, -1);
		_spriteCompactCount = 0;
		foreach (SpriteEntry allSprite in allSprites)
		{
			_spriteCompactMap[allSprite.Index] = _spriteCompactCount++;
		}
		PathPoints = new List<(int, int)>();
		Path2Points = new List<(int, int)>();
		Inputs = new List<bool>();
		int num16 = 0;
		for (int num17 = 0; num17 < this.tiles.Length; num17++)
		{
			num16 = num16 * 31 + this.tiles[num17];
		}
		int num18 = 0;
		for (int num19 = 0; num19 < this.sprites.Length; num19++)
		{
			num18 = num18 * 31 + this.sprites[num19];
		}
		int num20 = 0;
		foreach (KeyValuePair<int, (int, int)> item in this.spritePixelOffsets.OrderBy((KeyValuePair<int, (int offsetX, int offsetY)> x) => x.Key))
		{
			num20 = num20 * 31 + item.Key + item.Value.Item1 * 7 + item.Value.Item2 * 13;
		}
		int num21 = 0;
		foreach (KeyValuePair<int, (int, int)> item2 in this.spriteAnchors.OrderBy((KeyValuePair<int, (int anchorTileX, int anchorTileY)> x) => x.Key))
		{
			num21 = num21 * 31 + item2.Key + item2.Value.Item1 * 7 + item2.Value.Item2 * 13;
		}
		string text = $"[PF_DIAG] tiles={this.tiles.Length} tileHash=0x{num16:X8} sprites={this.sprites.Length} sprHash=0x{num18:X8} offsets={this.spritePixelOffsets.Count} offHash=0x{num20:X8} anchors={this.spriteAnchors.Count} anchHash=0x{num21:X8} w={mapWidth} h={mapHeight} ground={groundRowsToReserve} maxFall=0x{this.maxFallSpeed:X} spriteEntries={allSprites.Count}";
		_log.WriteLine(text);
		_log.Flush();
		try
		{
			EnsureDebugPathsStamped();
			File.AppendAllText(pfDebugLogPath, text + Environment.NewLine);
		}
		catch
		{
		}
	}

	public void Run(int startX_px, int startY_px, int startSpeedUiIndex, int startGameMode, bool startGravFlipped, bool startMini)
	{
		_log = (Verbose ? Console.Error : TextWriter.Null);
		for (int i = 0; i < allCoins.Count; i++)
		{
			_log.WriteLine($"[COIN_INFO] coin#{i} idx={allCoins[i].Index} sid=0x{allCoins[i].SpriteId:X2} pos=({allCoins[i].AnchorX_px},{allCoins[i].AnchorY_px}) hit=({allCoins[i].HitLeft},{allCoins[i].HitTop})-({allCoins[i].HitRight},{allCoins[i].HitBottom})");
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		double jumpTimingBias = JumpTimingBias;
		_autoForgivenCoins.Clear();
		bool flag = false;
		RunBFS(startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped, startMini);
		if (!Success && Math.Abs(JumpTimingBias - 0.5) >= 0.05)
		{
			_log.WriteLine($"[BFS_RETRY] BFS failed with bias={JumpTimingBias:F2}, retrying with neutral bias...");
			double jumpTimingBias2 = JumpTimingBias;
			JumpTimingBias = 0.5;
			RunBFS(startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped, startMini);
			JumpTimingBias = jumpTimingBias2;
		}
		if (Success || UseBFS)
		{
			stopwatch.Stop();
			if (!string.IsNullOrEmpty(ResultMessage))
			{
				ResultMessage += $" [{stopwatch.Elapsed.TotalSeconds:F1}s]";
			}
			return;
		}
		List<bool> inputs = ((Inputs != null) ? new List<bool>(Inputs) : null);
		List<(int, int)> pathPoints = ((PathPoints != null) ? new List<(int, int)>(PathPoints) : null);
		string resultMessage = ResultMessage;
		int num = ((PathPoints != null && PathPoints.Count > 0) ? PathPoints[PathPoints.Count - 1].x : 0);
		int num2 = mapWidth * 16;
		int value = ((num2 > 0) ? (num * 100 / num2) : 0);
		_log.WriteLine($"[BFS?HEURISTIC] BFS reached {value}%, trying heuristic...");
		RunSingleAttempt(startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped, startMini);
		if (!Success)
		{
			int num3 = ((PathPoints != null && PathPoints.Count > 0) ? PathPoints[PathPoints.Count - 1].x : 0);
			if (num > num3)
			{
				Inputs = inputs;
				PathPoints = pathPoints;
				ResultMessage = resultMessage;
				_log.WriteLine($"[BFS?HEURISTIC] Heuristic worse ({num3}px vs BFS {num}px), keeping BFS result");
			}
		}
		_coinForceWalkZones.Clear();
		_coinAltitudePenalties.Clear();
		if (Success && PreferCoins && _forgivenCoins.Count > 0 && allCoins.Count > 0)
		{
			int num4 = (mapHeight - groundRowsToReserve) * 16 - 15;
			List<bool> inputs2 = new List<bool>(Inputs);
			List<(int, int)> pathPoints2 = new List<(int, int)>(PathPoints);
			string resultMessage2 = ResultMessage;
			int num5 = ((FinalCollectedCoinIndices != null) ? FinalCollectedCoinIndices.Count : 0);
			HashSet<int> hashSet = ((FinalCollectedCoinIndices != null) ? new HashSet<int>(FinalCollectedCoinIndices) : null);
			List<SpriteEntry> list = allCoins.Where((SpriteEntry c) => _forgivenCoins.Contains(c.Index)).ToList();
			int value4;
			List<SpriteEntry> list2 = list.Where((SpriteEntry c) => _forgivenCoinGameModes.TryGetValue(c.Index, out value4) && value4 == 0).ToList();
			if (list2.Count > 1)
			{
				List<(int, int)> list3 = new List<(int, int)>();
				List<(int, int, int)> list4 = new List<(int, int, int)>();
				bool flag2 = false;
				foreach (SpriteEntry item2 in list2)
				{
					int num6 = (item2.HitLeft + item2.HitRight) / 2;
					foreach (SpriteEntry allSprite in allSprites)
					{
						if (!IsAnyPad(allSprite.SpriteId))
						{
							continue;
						}
						int num7 = (allSprite.HitLeft + allSprite.HitRight) / 2;
						if (num7 > num6 + 32 || num6 - num7 > 200)
						{
							continue;
						}
						int num8 = (allSprite.HitTop + allSprite.HitBottom) / 2;
						if (num8 >= num4 - 40)
						{
							flag2 = true;
							bool flag3 = num8 >= num4 - 30;
							if (flag3 && !IsBluePad(allSprite.SpriteId))
							{
								list3.Add((allSprite.HitLeft - 100, allSprite.HitRight + 16));
							}
							if (flag3)
							{
								list4.Add((allSprite.HitLeft - 150, allSprite.HitLeft, num4 - 20));
							}
						}
					}
				}
				if (flag2)
				{
					_log.WriteLine($"[COIN_RETRY_COMBINED] Attempting {list2.Count} cube-mode forgiven coins, forceWalk={list3.Count} altPenalty={list4.Count}");
					JumpTimingBias = jumpTimingBias;
					_coinForceWalkZones = list3;
					_coinAltitudePenalties = list4;
					_coinGroundBiasZones.Clear();
					_retryMandatoryCoins.Clear();
					RunSingleAttempt(startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped, startMini);
					if (Success)
					{
						int num9 = ((FinalCollectedCoinIndices != null) ? FinalCollectedCoinIndices.Count : 0);
						_log.WriteLine($"[COIN_RETRY_COMBINED] Result: {num9}/{allCoins.Count} coins");
						if (num9 > num5)
						{
							_log.WriteLine($"[COIN_RETRY_COMBINED] Improved: {num9} vs {num5}");
							inputs2 = new List<bool>(Inputs);
							pathPoints2 = new List<(int, int)>(PathPoints);
							resultMessage2 = ResultMessage;
							num5 = num9;
							hashSet = ((FinalCollectedCoinIndices != null) ? new HashSet<int>(FinalCollectedCoinIndices) : null);
						}
					}
					else
					{
						Success = true;
					}
					JumpTimingBias = jumpTimingBias;
					_coinForceWalkZones.Clear();
					_coinAltitudePenalties.Clear();
					_coinGroundBiasZones.Clear();
					_retryMandatoryCoins.Clear();
				}
			}
			foreach (SpriteEntry item3 in list)
			{
				if (num5 >= allCoins.Count)
				{
					break;
				}
				if (_forgivenCoinGameModes.TryGetValue(item3.Index, out var value2) && (value2 == 1 || value2 == 3))
				{
					_log.WriteLine($"[COIN_RETRY_SKIP] coin idx={item3.Index} sid=0x{item3.SpriteId:X2} gameMode={value2} \ufffd skip non-cube retry");
					continue;
				}
				int num10 = (item3.HitLeft + item3.HitRight) / 2;
				int num11 = (item3.HitTop + item3.HitBottom) / 2;
				List<(int, int)> list5 = new List<(int, int)>();
				List<(int, int, int)> list6 = new List<(int, int, int)>();
				bool flag4 = false;
				foreach (SpriteEntry allSprite2 in allSprites)
				{
					if (!IsAnyPad(allSprite2.SpriteId))
					{
						continue;
					}
					int num12 = (allSprite2.HitLeft + allSprite2.HitRight) / 2;
					if (num12 > num10 + 32 || num10 - num12 > 200)
					{
						continue;
					}
					int num13 = (allSprite2.HitTop + allSprite2.HitBottom) / 2;
					if (num13 >= num4 - 40)
					{
						flag4 = true;
						_log.WriteLine($"[COIN_RETRY_PAD] coin={item3.Index} pad idx={allSprite2.Index} sid=0x{allSprite2.SpriteId:X2} hit=({allSprite2.HitLeft},{allSprite2.HitTop})-({allSprite2.HitRight},{allSprite2.HitBottom})");
						bool flag5 = num13 >= num4 - 30;
						if (flag5 && !IsBluePad(allSprite2.SpriteId))
						{
							list5.Add((allSprite2.HitLeft - 100, allSprite2.HitRight + 16));
						}
						if (flag5)
						{
							int item = num4 - 20;
							list6.Add((allSprite2.HitLeft - 150, allSprite2.HitLeft, item));
						}
					}
				}
				if (!flag4)
				{
					continue;
				}
				double[] array = new double[3] { jumpTimingBias, 0.0, 1.0 };
				double[] array2 = array;
				foreach (double num15 in array2)
				{
					_log.WriteLine($"[COIN_RETRY] Attempting coin idx={item3.Index} sid=0x{item3.SpriteId:X2} bias={num15:F2} forceWalk={list5.Count} altPenalty={list6.Count}");
					JumpTimingBias = num15;
					_coinForceWalkZones = list5;
					_coinAltitudePenalties = list6;
					_coinGroundBiasZones.Clear();
					_retryMandatoryCoins.Clear();
					RunSingleAttempt(startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped, startMini);
					if (Success)
					{
						int num16 = ((FinalCollectedCoinIndices != null) ? FinalCollectedCoinIndices.Count : 0);
						_log.WriteLine($"[COIN_RETRY] Retry result: {num16}/{allCoins.Count} coins");
						if (num16 > num5)
						{
							_log.WriteLine($"[COIN_RETRY] Improved: {num16} vs {num5}");
							inputs2 = new List<bool>(Inputs);
							pathPoints2 = new List<(int, int)>(PathPoints);
							resultMessage2 = ResultMessage;
							num5 = num16;
							hashSet = ((FinalCollectedCoinIndices != null) ? new HashSet<int>(FinalCollectedCoinIndices) : null);
							break;
						}
					}
					else
					{
						_log.WriteLine($"[COIN_RETRY] Retry for coin {item3.Index} bias={num15:F2} FAILED");
						Success = true;
					}
				}
				JumpTimingBias = jumpTimingBias;
				_coinForceWalkZones.Clear();
				_coinAltitudePenalties.Clear();
				_coinGroundBiasZones.Clear();
				_retryMandatoryCoins.Clear();
			}
			List<SpriteEntry> list7 = list.Where((SpriteEntry c) => _forgivenCoinGameModes.TryGetValue(c.Index, out value4) && value4 == 1).ToList();
			bool flag6 = list7.All(delegate(SpriteEntry c)
			{
				if (_coinCollectThenLoseCount.TryGetValue(c.Index, out value4) && value4 > 0)
				{
					return false;
				}
				if (_autoForgivenCoins.Contains(c.Index))
				{
					return true;
				}
				_coinMissRetryCount.TryGetValue(c.Index, out var value5);
				return value5 >= 3;
			});
			if (list7.Count > 0 && flag6)
			{
				_log.WriteLine($"[SHIP_RETRY_SKIP] All {list7.Count} ship coins have high miss counts \ufffd skipping ship retry");
			}
			if (list7.Count > 0 && !flag6 && num5 < allCoins.Count)
			{
				double[] array3 = new double[2] { jumpTimingBias, 0.3 };
				double[] array4 = array3;
				foreach (double num18 in array4)
				{
					if (num5 >= allCoins.Count)
					{
						break;
					}
					_log.WriteLine($"[COIN_RETRY_SHIP] Attempting {list7.Count} ship-mode forgiven coins bias={num18:F2} with aggressive threshold");
					JumpTimingBias = num18;
					_coinForceWalkZones.Clear();
					_coinAltitudePenalties.Clear();
					_coinGroundBiasZones.Clear();
					_retryMandatoryCoins.Clear();
					_shipCoinAggressiveThreshold = 20;
					RunSingleAttempt(startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped, startMini);
					if (Success)
					{
						int num19 = ((FinalCollectedCoinIndices != null) ? FinalCollectedCoinIndices.Count : 0);
						_log.WriteLine($"[COIN_RETRY_SHIP] Result: {num19}/{allCoins.Count} coins (bias={num18:F2})");
						if (num19 > num5)
						{
							_log.WriteLine($"[COIN_RETRY_SHIP] Improved: {num19} vs {num5}");
							inputs2 = new List<bool>(Inputs);
							pathPoints2 = new List<(int, int)>(PathPoints);
							resultMessage2 = ResultMessage;
							num5 = num19;
							hashSet = ((FinalCollectedCoinIndices != null) ? new HashSet<int>(FinalCollectedCoinIndices) : null);
							break;
						}
					}
					else
					{
						_log.WriteLine($"[COIN_RETRY_SHIP] Retry bias={num18:F2} FAILED");
						Success = true;
					}
				}
				JumpTimingBias = jumpTimingBias;
			}
			List<SpriteEntry> list8 = list.Where((SpriteEntry c) => _forgivenCoinGameModes.TryGetValue(c.Index, out value4) && value4 == 2).ToList();
			if (list8.Count > 0 && num5 < allCoins.Count)
			{
				double[] array5 = new double[2] { 0.0, 1.0 };
				double[] array6 = array5;
				foreach (double num21 in array6)
				{
					if (num5 >= allCoins.Count)
					{
						break;
					}
					if (Math.Abs(num21 - jumpTimingBias) < 0.05)
					{
						continue;
					}
					_log.WriteLine($"[COIN_RETRY_BALL] Attempting {list8.Count} ball-mode forgiven coins bias={num21:F2}");
					JumpTimingBias = num21;
					_coinForceWalkZones.Clear();
					_coinAltitudePenalties.Clear();
					_coinGroundBiasZones.Clear();
					_retryMandatoryCoins.Clear();
					RunSingleAttempt(startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped, startMini);
					if (Success)
					{
						int num22 = ((FinalCollectedCoinIndices != null) ? FinalCollectedCoinIndices.Count : 0);
						_log.WriteLine($"[COIN_RETRY_BALL] Result: {num22}/{allCoins.Count} coins (bias={num21:F2})");
						if (num22 > num5)
						{
							_log.WriteLine($"[COIN_RETRY_BALL] Improved: {num22} vs {num5}");
							inputs2 = new List<bool>(Inputs);
							pathPoints2 = new List<(int, int)>(PathPoints);
							resultMessage2 = ResultMessage;
							num5 = num22;
							hashSet = ((FinalCollectedCoinIndices != null) ? new HashSet<int>(FinalCollectedCoinIndices) : null);
							break;
						}
					}
					else
					{
						_log.WriteLine($"[COIN_RETRY_BALL] Retry bias={num21:F2} FAILED");
						Success = true;
					}
				}
				JumpTimingBias = jumpTimingBias;
			}
			Inputs = inputs2;
			PathPoints = pathPoints2;
			FinalCollectedCoinIndices = hashSet;
			int num23 = hashSet?.Count ?? 0;
			int num24 = allCoins.Count - num23;
			int num25 = resultMessage2.IndexOf('[');
			string value3 = ((num25 > 0) ? resultMessage2.Substring(0, num25).TrimEnd() : resultMessage2);
			ResultMessage = $"{value3} [{num23}/{allCoins.Count} coins]";
			if (num24 > 0)
			{
				ResultMessage += $" ({num24} unreachable)";
			}
		}
		if (!Success)
		{
			double[] array7 = ((!(jumpTimingBias >= 0.5)) ? new double[4] { 0.25, 0.5, 0.75, 1.0 } : new double[4] { 0.75, 0.5, 0.25, 0.0 });
			double[] array8 = array7;
			foreach (double num27 in array8)
			{
				if (!(Math.Abs(num27 - jumpTimingBias) < 0.01))
				{
					PfLog($"[RETRY] BFS + primary bias failed, retrying with bias {num27:F2}");
					JumpTimingBias = num27;
					RunSingleAttempt(startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped, startMini);
					if (Success)
					{
						break;
					}
				}
			}
		}
		JumpTimingBias = jumpTimingBias;
		stopwatch.Stop();
		double totalSeconds = stopwatch.Elapsed.TotalSeconds;
		ResultMessage += $" [{totalSeconds:F1}s]";
		PfLog($"[TIMING] generation took {totalSeconds:F3}s");
	}

	private bool ReplayInputs(List<bool> inputs, int startX_px, int startY_px, int startSpeedUiIndex, int startGameMode, bool startGravFlipped, bool startMini, out List<(int x, int y)> pathPoints)
	{
		pathPoints = new List<(int, int)>();
		int num = ComputeInitCameraY(startY_px);
		SimState s = new SimState
		{
			X_fixed = startX_px << 8,
			Y_fixed = startY_px << 8,
			VelX_fixed = SpeedUiIndexToFixed(startSpeedUiIndex),
			VelY_fixed = 0,
			GameMode = startGameMode,
			GravFlipped = startGravFlipped,
			Mini = startMini,
			GravMul = ((!startGravFlipped) ? 1 : (-1)),
			GravityMod = 1.0,
			WasZeroedByCollision = true,
			OnGround = true,
			ProcessedSprites = NewSpriteSet(),
			PendingOrbIndex = -1,
			PendingOrbSpriteId = -1,
			PendingOrbExtra1Index = -1,
			PendingOrbExtra1SpriteId = -1,
			PendingOrbExtra2Index = -1,
			PendingOrbExtra2SpriteId = -1,
			NinjaJumps = ((startGameMode == 8) ? 3 : 0),
			CameraY_fixed = num,
			TargetCameraY_fixed = num
		};
		ApplyPortalsUpTo(ref s, startX_px);
		ApplyNesIntroFreezePrestep(ref s);
		for (int i = 0; i < inputs.Count; i++)
		{
			bool endLevel;
			bool flag = StepFrame(ref s, inputs[i], out endLevel);
			int num2 = ((s.Mini && !s.GravFlipped) ? 4 : 0);
			pathPoints.Add(((s.X_fixed >> 8) + 8, (s.Y_fixed >> 8) + num2 + 8));
			if (!flag)
			{
				return false;
			}
			if (endLevel)
			{
				return true;
			}
		}
		return false;
	}

	public void ReplayInputSequence(int startX_px, int startY_px, int startSpeedUiIndex, int startGameMode, bool startGravFlipped, bool startMini, IReadOnlyList<bool> inputSequence, int preRollFrames = 0, TextWriter? output = null)
	{
		_log = (Verbose ? Console.Error : TextWriter.Null);
		if (output == null)
		{
			output = TextWriter.Null;
		}
		int num = ComputeInitCameraY(startY_px);
		SimState s = new SimState
		{
			X_fixed = startX_px << 8,
			Y_fixed = startY_px << 8,
			VelX_fixed = SpeedUiIndexToFixed(startSpeedUiIndex),
			VelY_fixed = 0,
			GameMode = startGameMode,
			GravFlipped = startGravFlipped,
			Mini = startMini,
			GravMul = ((!startGravFlipped) ? 1 : (-1)),
			GravityMod = 1.0,
			WasZeroedByCollision = true,
			OnGround = true,
			ProcessedSprites = NewSpriteSet(),
			PendingOrbIndex = -1,
			PendingOrbSpriteId = -1,
			PendingOrbExtra1Index = -1,
			PendingOrbExtra1SpriteId = -1,
			PendingOrbExtra2Index = -1,
			PendingOrbExtra2SpriteId = -1,
			NinjaJumps = ((startGameMode == 8) ? 3 : 0),
			CameraY_fixed = num,
			TargetCameraY_fixed = num
		};
		ApplyPortalsUpTo(ref s, startX_px);
		ApplyNesIntroFreezePrestep(ref s);
		PathPoints = new List<(int, int)>();
		Path2Points = new List<(int, int)>();
		Inputs = new List<bool>();
		Success = false;
		ResultMessage = string.Empty;
		_prevDualActiveForPath = false;
		_cubeHoldJump = false;
		_speculativeDepth = 0;
		_frameCounter = 0;
		int num2 = Math.Max(0, Math.Min(preRollFrames, inputSequence.Count));
		int value = 0;
		bool flag = false;
		bool flag2 = false;
		TraceFrameOpen();
		try
		{
			for (int i = num2; i < inputSequence.Count; i++)
			{
				int num3 = (_frameCounter = i - num2);
				bool flag3 = inputSequence[i];
				Inputs.Add(flag3);
				int gameMode = s.GameMode;
				_cubeJumpedThisStep = false;
				bool endLevel;
				bool flag4 = StepFrame(ref s, flag3, out endLevel);
				if (gameMode == 0 && flag3 && !_cubeJumpedThisStep && s.GameMode == 0 && s.Dashing == 0)
				{
					Inputs[Inputs.Count - 1] = false;
				}
				TraceFrame(num3, ref s, Inputs[Inputs.Count - 1], flag4);
				int num4 = (s.Mini ? 4 : 0);
				PathPoints.Add(((s.X_fixed >> 8) + 8, (s.Y_fixed >> 8) + num4 + 8));
				if (s.DualActive)
				{
					if (!_prevDualActiveForPath && Path2Points.Count > 0)
					{
						Path2Points.Add((-1, -1));
					}
					int num5 = (s.P2_Mini ? 4 : 0);
					Path2Points.Add(((s.X_fixed >> 8) + 8, (s.P2_Y_fixed >> 8) + num5 + 8));
				}
				_prevDualActiveForPath = s.DualActive;
				value = num3 + 1;
				if (endLevel)
				{
					flag = true;
					Success = true;
					ResultMessage = $"TAS replay completed in {value} frames";
					break;
				}
				if (!flag4)
				{
					flag2 = true;
					Success = false;
					ResultMessage = $"TAS replay died at frame {num3} (input line {i})";
					break;
				}
			}
		}
		finally
		{
			TraceFrameClose();
		}
		if (!flag && !flag2)
		{
			ResultMessage = $"TAS replay exhausted {value} frames without ending level";
		}
		output.WriteLine("Result: " + (Success ? "SUCCESS" : "FAILED"));
		output.WriteLine("Message: " + ResultMessage);
		output.WriteLine($"Path points: {PathPoints.Count}");
		output.WriteLine("Trace: " + FrameTracePath);
	}

	public void OptimizeClicks(int mode, int startX_px, int startY_px, int startSpeedUiIndex, int startGameMode, bool startGravFlipped, bool startMini)
	{
		if (!Success || Inputs == null || Inputs.Count == 0)
		{
			return;
		}
		List<bool> list = new List<bool>(Inputs);
		int num = CountClicks(list);
		List<(int, int)> pathPoints;
		switch (mode)
		{
		case 1:
		{
			List<bool> list5 = new List<bool>(list);
			bool flag2 = true;
			while (flag2)
			{
				flag2 = false;
				for (int num6 = list5.Count - 1; num6 >= 0; num6--)
				{
					if (list5[num6])
					{
						list5[num6] = false;
						if (ReplayInputs(list5, startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped, startMini, out List<(int, int)> _))
						{
							flag2 = true;
						}
						else
						{
							list5[num6] = true;
						}
					}
				}
			}
			int num7 = CountClicks(list5);
			if (num7 < num)
			{
				Inputs = list5;
				ReplayInputs(Inputs, startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped, startMini, out List<(int, int)> pathPoints5);
				PathPoints = pathPoints5;
				ResultMessage += $" [optimized: {num}?{num7} clicks]";
			}
			break;
		}
		case 2:
		{
			List<bool> list4 = new List<bool>(list);
			for (int l = 0; l < list4.Count; l++)
			{
				if (!list4[l])
				{
					list4[l] = true;
					if (!ReplayInputs(list4, startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped, startMini, out pathPoints))
					{
						list4[l] = false;
					}
				}
			}
			int num5 = CountClicks(list4);
			if (num5 > num)
			{
				Inputs = list4;
				ReplayInputs(Inputs, startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped, startMini, out List<(int, int)> pathPoints3);
				PathPoints = pathPoints3;
				ResultMessage += $" [swag: {num}?{num5} clicks]";
			}
			break;
		}
		case 3:
		{
			List<bool> list2 = new List<bool>(list);
			bool flag = true;
			while (flag)
			{
				flag = false;
				for (int num2 = list2.Count - 1; num2 >= 0; num2--)
				{
					if (list2[num2])
					{
						list2[num2] = false;
						if (ReplayInputs(list2, startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped, startMini, out pathPoints))
						{
							flag = true;
						}
						else
						{
							list2[num2] = true;
						}
					}
				}
			}
			int i = 0;
			while (i < list2.Count)
			{
				if (!list2[i])
				{
					i++;
					continue;
				}
				int num3 = i;
				for (; i < list2.Count && list2[i]; i++)
				{
				}
				int num4 = i;
				if (num4 - num3 <= 1)
				{
					continue;
				}
				List<bool> list3 = new List<bool>();
				for (int j = num3 + 1; j < num4; j++)
				{
					list3.Add(list2[j]);
					list2[j] = false;
				}
				if (!ReplayInputs(list2, startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped, startMini, out pathPoints))
				{
					for (int k = num3 + 1; k < num4; k++)
					{
						list2[k] = list3[k - num3 - 1];
					}
				}
			}
			int value = CountClicks(list2);
			Inputs = list2;
			ReplayInputs(Inputs, startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped, startMini, out List<(int, int)> pathPoints2);
			PathPoints = pathPoints2;
			ResultMessage += $" [essential: {num}?{value} clicks]";
			break;
		}
		}
	}

	private static int CountClicks(List<bool> inputs)
	{
		int num = 0;
		bool flag = false;
		foreach (bool input in inputs)
		{
			if (input && !flag)
			{
				num++;
			}
			flag = input;
		}
		return num;
	}

	private void RunSingleAttempt(int startX_px, int startY_px, int startSpeedUiIndex, int startGameMode, bool startGravFlipped, bool startMini)
	{
		int num = ComputeInitCameraY(startY_px);
		SimState s = new SimState
		{
			X_fixed = startX_px << 8,
			Y_fixed = startY_px << 8,
			VelX_fixed = SpeedUiIndexToFixed(startSpeedUiIndex),
			VelY_fixed = 0,
			GameMode = startGameMode,
			GravFlipped = startGravFlipped,
			Mini = startMini,
			GravMul = ((!startGravFlipped) ? 1 : (-1)),
			GravityMod = 1.0,
			WasZeroedByCollision = true,
			OnGround = true,
			ProcessedSprites = NewSpriteSet(),
			PendingOrbIndex = -1,
			PendingOrbSpriteId = -1,
			PendingOrbExtra1Index = -1,
			PendingOrbExtra1SpriteId = -1,
			PendingOrbExtra2Index = -1,
			PendingOrbExtra2SpriteId = -1,
			NinjaJumps = ((startGameMode == 8) ? 3 : 0),
			CameraY_fixed = num,
			TargetCameraY_fixed = num
		};
		ApplyPortalsUpTo(ref s, startX_px);
		ApplyNesIntroFreezePrestep(ref s);
		PathPoints.Clear();
		Path2Points.Clear();
		Inputs.Clear();
		_prevDualActiveForPath = false;
		_cubeHoldJump = false;
		_cubeHoldDelay = 0;
		_committedJumpDelay = -1;
		_committedRobotHold = 0;
		_committedNinjaJumps.Clear();
		_ninjaWaitFrames = 0;
		_backtrackCheckpoints = new List<BacktrackCheckpoint>();
		_lastCubeToShipCheckpoint = null;
		_shipEntryRecoveryCheckpoint = null;
		_backtrackAttempts = 0;
		_totalBacktrackAttempts = 0;
		_btOverrideFrame = -1;
		_btOverrideDistFromDeath = 0;
		_backtrackActive = false;
		_btSuppressJumpUntilAirborne = false;
		_btSkipAllOrbs = false;
		_btSkipSpecificOrbs.Clear();
		_hitOrbHistory.Clear();
		_btSkipSpecificPads.Clear();
		_hitPadHistory.Clear();
		_btDeathFrame = 0;
		_shipCorridorBias = 0;
		_shipCoinAggressiveThreshold = 3;
		_modeTransitionStabilizeFrames = 0;
		_shipForceReleaseFirstFrames = 0;
		_shipForceHoldFrames = 0;
		_shipForceReleaseFrames = 0;
		_shipCommitFrames = 0;
		_shipCommitHold = false;
		_cubeGroundedWalkFrames = 0;
		_ballGroundedWalkFrames = 0;
		_bestPathHighWaterX = startX_px;
		_bestPathPoints.Clear();
		_bestPath2Points.Clear();
		_bestInputs.Clear();
		_nextCoinCheckIdx = 0;
		_forgivenCoins.Clear();
		_forgivenCoinGameModes.Clear();
		_coinMissRetryCount.Clear();
		_coinCollectThenLoseCount.Clear();
		_permanentlyCollectedCoins.Clear();
		_missedCoinIdx = -1;
		_coinInputScript.Clear();
		_coinInputScriptCoinIdx = -1;
		_beamSearchAttemptedCoinIdx = -1;
		_beamSearchLastDistX = int.MaxValue;
		_cubeBeamSearchAttemptedCoinIdx = -1;
		_crossCorrForgivenCoins.Clear();
		PreForgiveCrossCorridorCoins(startGameMode);
		_frameCounter = 0;
		_speculativeDepth = 0;
		TraceFrameOpen();
		PfLog("[LEVEL] " + LevelName);
		PfLog($"[RUN_START] startX={startX_px} startY={startY_px} speed={startSpeedUiIndex} mode={startGameMode} gravFlipped={startGravFlipped} mini={startMini}");
		PfLog($"[RUN_STATE] X_fixed=0x{s.X_fixed:X4} Y_fixed=0x{s.Y_fixed:X4} VelX=0x{s.VelX_fixed:X4} VelY=0x{s.VelY_fixed:X4} gravMul={s.GravMul} gravMod={s.GravityMod:F3} onGround={s.OnGround}");
		PfLog($"[RUN_MAP] mapWidth={mapWidth} mapHeight={mapHeight} groundRowsToReserve={groundRowsToReserve} maxFallSpeed=0x{maxFallSpeed:X4} sprites={allSprites.Count}");
		foreach (SpriteEntry allSprite in allSprites)
		{
			int spriteId = allSprite.SpriteId;
			if (IsGravityPortal(spriteId) || IsGameModePortal(spriteId) || IsEndLevel(spriteId))
			{
				PfLog($"[PORTAL_INV] sid=0x{spriteId:X2} idx={allSprite.Index} anchorX={allSprite.AnchorX_px} box=({allSprite.HitLeft},{allSprite.HitTop})-({allSprite.HitRight},{allSprite.HitBottom})");
			}
		}
		int num2 = mapWidth * 16;
		int num3 = startX_px;
		int num4 = Math.Max(1, 144);
		int num5 = 0;
		SimState simState = default(SimState);
		int num6 = 0;
		int num7 = 0;
		int num8 = 0;
		int nextCoinCheckIdx = 0;
		List<BacktrackCheckpoint> list = null;
		for (int i = 0; i < 28800; i++)
		{
			num5++;
			if (CancelRequested)
			{
				SnapshotBestPath();
				UseBestPathIfBetter();
				Success = false;
				int num9 = ((PathPoints.Count > 0) ? PathPoints[PathPoints.Count - 1].x : 0);
				int value = ((num2 > 0) ? (num9 * 100 / num2) : 0);
				ResultMessage = $"Cancelled \ufffd partial path to X={num9}px ({value}%, {PathPoints.Count} points)";
				TraceFrameClose();
				return;
			}
			if (num5 > 288000)
			{
				SnapshotBestPath();
				UseBestPathIfBetter();
				Success = false;
				int num10 = ((PathPoints.Count > 0) ? PathPoints[PathPoints.Count - 1].x : 0);
				int value2 = ((num2 > 0) ? (num10 * 100 / num2) : 0);
				ResultMessage = $"Aborted \ufffd partial path to X={num10}px ({value2}%) exceeded {288000} iterations";
				TraceFrameClose();
				PfLog($"[ABORT] total iterations {num5} exceeded cap");
				return;
			}
			int num11 = s.X_fixed >> 8;
			if (num11 > num3)
			{
				num3 = num11;
			}
			if (i % num4 == 0 && num2 > 0)
			{
				Progress?.Report(Math.Min(99, num3 * 100 / num2));
			}
			_currentX_px = s.X_fixed >> 8;
			_frameCounter = i;
			PfLog($"[STEP_START] playerX_fixed=0x{s.X_fixed:X4} ({s.X_fixed >> 8}px), playerY_fixed=0x{s.Y_fixed:X4} ({s.Y_fixed >> 8}px), playerVelY_fixed=0x{s.VelY_fixed:X4}, mode={s.GameMode}, gravFlipped={s.GravFlipped}, onGround={s.OnGround}, wasZeroed={s.WasZeroedByCollision}, mini={s.Mini}");
			bool flag = s.VelY_fixed == 0 && s.OnGround;
			bool flag2 = !flag && s.GameMode == 0 && CubeWillLandThisFrame(s);
			bool cubeHoldJump = _cubeHoldJump;
			int cubeHoldDelay = _cubeHoldDelay;
			int committedJumpDelay = _committedJumpDelay;
			int count = PathPoints.Count;
			int count2 = Inputs.Count;
			bool flag3 = s.GameMode == 1;
			SimState simState2 = default(SimState);
			bool flag4 = flag || flag3 || flag2;
			if (flag4)
			{
				simState2 = s.Clone();
			}
			bool flag5 = _btOverrideFrame == i;
			bool flag6 = DecideInput(s);
			Inputs.Add(flag6);
			_prevFrameWasGrounded = flag;
			if (s.GameMode == 0 && flag && !flag6 && _committedJumpDelay < 0)
			{
				_cubeGroundedWalkFrames++;
			}
			else if (s.GameMode == 0)
			{
				_cubeGroundedWalkFrames = 0;
			}
			if (s.GameMode == 2 && flag && !flag6 && _committedJumpDelay < 0)
			{
				_ballGroundedWalkFrames++;
			}
			else if (s.GameMode == 2)
			{
				_ballGroundedWalkFrames = 0;
			}
			if (_backtrackActive && i > _btDeathFrame)
			{
				_backtrackActive = false;
				_shipCorridorBias = 0;
				_shipCoinAggressiveThreshold = 3;
				_shipForceHoldFrames = 0;
				_shipForceReleaseFrames = 0;
				_shipCommitFrames = 0;
				_btSkipAllOrbs = false;
				_btSkipSpecificOrbs.Clear();
				_hitOrbHistory.Clear();
				_btSkipSpecificPads.Clear();
				_hitPadHistory.Clear();
				_backtrackAttempts = 0;
				_backtrackTimer = null;
				PfLog($"[BACKTRACK_DONE] advanced past death frame {_btDeathFrame}, resuming normal checkpointing (attempts={_backtrackAttempts}/{500}) coinRetry={_missedCoinIdx >= 0}");
			}
			bool flag7 = (flag || flag2) && (flag6 || _committedJumpDelay > 0);
			if (!flag7 && flag2)
			{
				flag7 = true;
			}
			bool flag8 = s.GameMode == 3;
			if (!flag7 && (flag3 || flag8) && i % 12 == 0)
			{
				flag7 = true;
			}
			if (!flag7 && s.GameMode == 0 && _cubeGroundedWalkFrames > 0 && _cubeGroundedWalkFrames % 24 == 0)
			{
				flag7 = true;
			}
			if (flag7 && _backtrackCheckpoints.Count > 0)
			{
				int frame = _backtrackCheckpoints[_backtrackCheckpoints.Count - 1].Frame;
				if (i - frame < 4)
				{
					flag7 = false;
				}
			}
			if (flag7 && !flag5 && !_backtrackActive)
			{
				if (_backtrackCheckpoints.Count >= 200)
				{
					_backtrackCheckpoints.RemoveAt(0);
				}
				SimState state = (flag4 ? simState2 : s.Clone());
				_backtrackCheckpoints.Add(new BacktrackCheckpoint
				{
					State = state,
					Frame = i,
					HoldJumpState = cubeHoldJump,
					HoldDelayState = cubeHoldDelay,
					CommittedDelayState = committedJumpDelay,
					PathPointCount = count,
					InputCount = count2,
					RetryStage = 0,
					UsedBias = JumpTimingBias,
					ShipBias = _shipCorridorBias,
					GameMode = state.GameMode,
					ShipForceHold = _shipForceHoldFrames,
					ShipForceRelease = _shipForceReleaseFrames,
					ShipCommitFrames = _shipCommitFrames,
					ShipCommitHold = _shipCommitHold,
					ForceJumpRemaining = _btForceJumpFramesRemaining,
					SkipAllOrbs = _btSkipAllOrbs,
					SkipSpecificOrbs = new HashSet<int>(_btSkipSpecificOrbs),
					SkipSpecificPads = new HashSet<int>(_btSkipSpecificPads),
					PrevFrameWasGrounded = _prevFrameWasGrounded,
					NextCoinCheckIdx = _nextCoinCheckIdx
				});
			}
			PfLog($"[DECIDE] input={flag6}");
			int gameMode = s.GameMode;
			_cubeJumpedThisStep = false;
			bool endLevel;
			bool flag9 = StepFrame(ref s, flag6, out endLevel);
			if (gameMode == 0 && flag6 && !_cubeJumpedThisStep && s.GameMode == 0 && s.Dashing == 0)
			{
				Inputs[i] = false;
				PfLog($"[FIX23] Corrected Inputs[{i}] True->False (cube input=True but no jump fired)");
			}
			TraceFrame(i, ref s, Inputs[i], flag9);
			int num12 = (s.Mini ? 4 : 0);
			PathPoints.Add(((s.X_fixed >> 8) + 8, (s.Y_fixed >> 8) + num12 + 8));
			if (s.DualActive)
			{
				if (!_prevDualActiveForPath && Path2Points.Count > 0)
				{
					Path2Points.Add((-1, -1));
				}
				int num13 = (s.P2_Mini ? 4 : 0);
				Path2Points.Add(((s.X_fixed >> 8) + 8, (s.P2_Y_fixed >> 8) + num13 + 8));
			}
			_prevDualActiveForPath = s.DualActive;
			if (flag9 && PreferCoins && _speculativeDepth == 0 && _nextCoinCheckIdx < allCoins.Count)
			{
				int num14 = s.X_fixed >> 8;
				for (int j = _nextCoinCheckIdx; j < allCoins.Count; j++)
				{
					SpriteEntry spriteEntry = allCoins[j];
					if (spriteEntry.HitLeft > num14 + 16)
					{
						break;
					}
					if (s.ProcessedSprites.Contains(spriteEntry.Index) || _forgivenCoins.Contains(spriteEntry.Index))
					{
						if (j == _nextCoinCheckIdx)
						{
							_nextCoinCheckIdx++;
						}
						continue;
					}
					if (num14 < spriteEntry.HitRight)
					{
						break;
					}
					_coinMissRetryCount.TryGetValue(spriteEntry.Index, out var value3);
					value3++;
					_coinMissRetryCount[spriteEntry.Index] = value3;
					int num15 = ((s.GameMode == 1 || s.GameMode == 3) ? 4 : 5);
					if (value3 > num15)
					{
						_forgivenCoins.Add(spriteEntry.Index);
						_forgivenCoinGameModes[spriteEntry.Index] = s.GameMode;
						_autoForgivenCoins.Add(spriteEntry.Index);
						if (j == _nextCoinCheckIdx)
						{
							_nextCoinCheckIdx++;
						}
						_log.WriteLine($"[COIN_AUTO_FORGIVEN] idx={spriteEntry.Index} gm={s.GameMode} missCount={value3} playerX={s.X_fixed >> 8}");
						PfLog($"[COIN_AUTO_FORGIVEN] idx={spriteEntry.Index} sid=0x{spriteEntry.SpriteId:X2} missCount={value3} \ufffd auto-forgiven after {5} retries");
						continue;
					}
					flag9 = false;
					_lastDeathReason = "MISSED_COIN";
					_lastDeathX = spriteEntry.HitLeft;
					_lastDeathY = spriteEntry.HitTop;
					_missedCoinIdx = j;
					if (s.GameMode == 1 || s.GameMode == 3)
					{
						int num16 = s.Y_fixed >> 8;
						int num17 = (spriteEntry.HitTop + spriteEntry.HitBottom) / 2;
						int value4 = num16 - num17;
						_log.WriteLine($"[SHIP_COIN_MISS] idx={spriteEntry.Index} playerY={num16} coinY={num17} yOff={value4} missCount={value3}");
					}
					if (s.GameMode == 2)
					{
						int num18 = s.Y_fixed >> 8;
						int num19 = (spriteEntry.HitTop + spriteEntry.HitBottom) / 2;
						int value5 = (spriteEntry.HitLeft + spriteEntry.HitRight) / 2;
						int value6 = num18 - num19;
						_log.WriteLine($"[BALL_COIN_MISS] idx={spriteEntry.Index} playerX={s.X_fixed >> 8} playerY={num18} coinX={value5} coinY={num19} yOff={value6} onGround={s.OnGround} gravFlip={s.GravFlipped} missCount={value3}");
					}
					PfLog($"[MISSED_COIN] idx={spriteEntry.Index} sid=0x{spriteEntry.SpriteId:X2} hitbox=({spriteEntry.HitLeft},{spriteEntry.HitTop})-({spriteEntry.HitRight},{spriteEntry.HitBottom}) playerX={num14} retryCount={value3}");
					break;
				}
			}
			if (flag9 && _nextCoinCheckIdx < allCoins.Count)
			{
				int num20 = (s.X_fixed >> 8) + 1;
				int hitboxW = GetHitboxW(s.Mini);
				int hitboxH = GetHitboxH(s.Mini);
				int hitboxOffsetY = GetHitboxOffsetY(s.GameMode, s.Mini, s.GravFlipped);
				int num21 = (s.Y_fixed >> 8) + hitboxOffsetY;
				int num22 = num21 + hitboxH;
				int num23 = num20 + hitboxW;
				for (int k = _nextCoinCheckIdx; k < allCoins.Count; k++)
				{
					SpriteEntry spriteEntry2 = allCoins[k];
					if (spriteEntry2.HitLeft > num23 + 16)
					{
						break;
					}
					if (s.ProcessedSprites.Contains(spriteEntry2.Index) || _forgivenCoins.Contains(spriteEntry2.Index))
					{
						continue;
					}
					bool flag10 = num23 >= spriteEntry2.HitLeft && spriteEntry2.HitRight >= num20;
					bool flag11 = num22 >= spriteEntry2.HitTop && spriteEntry2.HitBottom >= num21;
					if (flag10 && flag11)
					{
						s.ProcessedSprites.Add(spriteEntry2.Index);
						if (_speculativeDepth == 0)
						{
							_permanentlyCollectedCoins[spriteEntry2.Index] = i;
							_log.WriteLine($"[COIN_COLLECTED] idx={spriteEntry2.Index} sid=0x{spriteEntry2.SpriteId:X2} gm={s.GameMode} playerX={num20} playerY={num21} coinHit=({spriteEntry2.HitLeft},{spriteEntry2.HitTop})-({spriteEntry2.HitRight},{spriteEntry2.HitBottom})");
						}
						PfLog($"[COIN_COLLECTED] idx={spriteEntry2.Index} sid=0x{spriteEntry2.SpriteId:X2} hitbox=({spriteEntry2.HitLeft},{spriteEntry2.HitTop})-({spriteEntry2.HitRight},{spriteEntry2.HitBottom}) player=({num20},{num21})-({num23},{num22})");
					}
				}
			}
			if (!flag9)
			{
				int value7 = ((num2 > 0) ? ((s.X_fixed >> 8) * 100 / num2) : 0);
				PfLog($"[DEATH] frame={i} X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px pct={value7}% reason={_lastDeathReason} dX={_lastDeathX} dY={_lastDeathY} VelY=0x{s.VelY_fixed:X4} gravFlipped={s.GravFlipped}");
				if (_speculativeDepth == 0 && !UseBFS)
				{
					_log.WriteLine($"[DEATH_DBG] frame={i} X={s.X_fixed >> 8} Y={s.Y_fixed >> 8} gm={s.GameMode} reason={_lastDeathReason} dX={_lastDeathX} dY={_lastDeathY} totalBT={_totalBacktrackAttempts}");
				}
				SnapshotBestPath();
				bool flag12 = _lastDeathReason == "MISSED_COIN" && _missedCoinIdx >= 0;
				if (flag12 && list == null)
				{
					simState = s.Clone();
					num6 = i;
					num7 = Inputs.Count;
					num8 = PathPoints.Count;
					nextCoinCheckIdx = _nextCoinCheckIdx;
					list = _backtrackCheckpoints.Select((BacktrackCheckpoint cp) => new BacktrackCheckpoint
					{
						Frame = cp.Frame,
						State = cp.State.Clone(),
						HoldJumpState = cp.HoldJumpState,
						HoldDelayState = cp.HoldDelayState,
						CommittedDelayState = cp.CommittedDelayState,
						PathPointCount = cp.PathPointCount,
						InputCount = cp.InputCount,
						RetryStage = cp.RetryStage,
						UsedBias = cp.UsedBias,
						ShipBias = cp.ShipBias,
						GameMode = cp.GameMode,
						ShipForceHold = cp.ShipForceHold,
						ShipForceRelease = cp.ShipForceRelease,
						ShipCommitFrames = cp.ShipCommitFrames,
						ShipCommitHold = cp.ShipCommitHold,
						ForceJumpRemaining = cp.ForceJumpRemaining,
						SkipAllOrbs = cp.SkipAllOrbs,
						SkipSpecificOrbs = ((cp.SkipSpecificOrbs != null) ? new HashSet<int>(cp.SkipSpecificOrbs) : new HashSet<int>()),
						SkipSpecificPads = ((cp.SkipSpecificPads != null) ? new HashSet<int>(cp.SkipSpecificPads) : new HashSet<int>()),
						PrevFrameWasGrounded = cp.PrevFrameWasGrounded
					}).ToList();
				}
				if (TryBacktrack(ref s, ref i))
				{
					continue;
				}
				if (flag12)
				{
					SpriteEntry spriteEntry3 = allCoins[_missedCoinIdx];
					if (_retryMandatoryCoins.Contains(spriteEntry3.Index))
					{
						_log.WriteLine($"[MANDATORY_COIN_DEATH] idx={spriteEntry3.Index} sid=0x{spriteEntry3.SpriteId:X2} \ufffd mandatory coin missed, permanent death");
						UseBestPathIfBetter();
						Success = false;
						int num24 = ((PathPoints.Count > 0) ? PathPoints[PathPoints.Count - 1].x : 0);
						int value8 = ((num2 > 0) ? (num24 * 100 / num2) : 0);
						ResultMessage = $"Permanent death at frame {i} (reason: mandatory coin {spriteEntry3.Index} missed) \ufffd best X {num24}px ({value8}%)";
						_log.WriteLine($"[PERM_DEATH] frame={i} X={s.X_fixed >> 8} Y={s.Y_fixed >> 8} reason=MANDATORY_COIN_{spriteEntry3.Index}");
						TraceFrameClose();
						return;
					}
					_forgivenCoins.Add(spriteEntry3.Index);
					_forgivenCoinGameModes[spriteEntry3.Index] = simState.GameMode;
					_coinInputScript.Clear();
					_coinInputScriptCoinIdx = -1;
					s = simState;
					i = num6;
					_nextCoinCheckIdx = nextCoinCheckIdx;
					if (Inputs.Count > num7)
					{
						Inputs.RemoveRange(num7, Inputs.Count - num7);
					}
					if (PathPoints.Count > num8)
					{
						PathPoints.RemoveRange(num8, PathPoints.Count - num8);
					}
					_backtrackCheckpoints = list;
					_backtrackActive = false;
					_backtrackAttempts = 0;
					_btOverrideFrame = -1;
					_btOverrideStage = 0;
					_btSuppressJumpUntilAirborne = false;
					_btSkipAllOrbs = false;
					_btSkipSpecificOrbs.Clear();
					_btSkipSpecificPads.Clear();
					_btForceJumpFramesRemaining = 0;
					_shipCorridorBias = 0;
					_shipCoinAggressiveThreshold = 3;
					_shipForceHoldFrames = 0;
					_shipForceReleaseFrames = 0;
					_shipCommitFrames = 0;
					_missedCoinIdx = -1;
					foreach (SpriteEntry allCoin in allCoins)
					{
						SpriteSet processedSprites = s.ProcessedSprites;
						if (processedSprites != null && processedSprites.Contains(allCoin.Index) && !_permanentlyCollectedCoins.ContainsKey(allCoin.Index))
						{
							_permanentlyCollectedCoins[allCoin.Index] = num6;
						}
					}
					PfLog($"[COIN_FORGIVEN] idx={spriteEntry3.Index} sid=0x{spriteEntry3.SpriteId:X2} hitbox=({spriteEntry3.HitLeft},{spriteEntry3.HitTop})-({spriteEntry3.HitRight},{spriteEntry3.HitBottom}) forgiven={_forgivenCoins.Count}");
					continue;
				}
				if (_missedCoinIdx < 0 || _missedCoinIdx >= allCoins.Count || list == null)
				{
					UseBestPathIfBetter();
					Success = false;
					int num25 = ((PathPoints.Count > 0) ? PathPoints[PathPoints.Count - 1].x : 0);
					int value9 = ((num2 > 0) ? (num25 * 100 / num2) : 0);
					string text = $"Died \ufffd partial path to X={num25}px ({value9}%) reason={_lastDeathReason} dX={_lastDeathX} dY={_lastDeathY}";
					if (PreferCoins && allCoins.Count > 0)
					{
						int num26 = 0;
						foreach (SpriteEntry allCoin2 in allCoins)
						{
							if (s.ProcessedSprites.Contains(allCoin2.Index))
							{
								num26++;
							}
						}
						text += $" [{num26}/{allCoins.Count} coins]";
						if (_forgivenCoins.Count > 0)
						{
							text += $" ({_forgivenCoins.Count} unreachable)";
						}
					}
					ResultMessage = text;
					TraceFrameClose();
					return;
				}
				SpriteEntry spriteEntry4 = allCoins[_missedCoinIdx];
				_forgivenCoins.Add(spriteEntry4.Index);
				_forgivenCoinGameModes[spriteEntry4.Index] = simState.GameMode;
				_coinInputScript.Clear();
				_coinInputScriptCoinIdx = -1;
				s = simState;
				i = num6;
				_nextCoinCheckIdx = nextCoinCheckIdx;
				if (Inputs.Count > num7)
				{
					Inputs.RemoveRange(num7, Inputs.Count - num7);
				}
				if (PathPoints.Count > num8)
				{
					PathPoints.RemoveRange(num8, PathPoints.Count - num8);
				}
				_backtrackCheckpoints = list;
				_backtrackActive = false;
				_backtrackAttempts = 0;
				_btOverrideFrame = -1;
				_btOverrideStage = 0;
				_btSuppressJumpUntilAirborne = false;
				_btSkipAllOrbs = false;
				_btSkipSpecificOrbs.Clear();
				_btSkipSpecificPads.Clear();
				_btForceJumpFramesRemaining = 0;
				_shipCorridorBias = 0;
				_shipCoinAggressiveThreshold = 3;
				_shipForceHoldFrames = 0;
				_shipForceReleaseFrames = 0;
				_shipCommitFrames = 0;
				_missedCoinIdx = -1;
				foreach (SpriteEntry allCoin3 in allCoins)
				{
					SpriteSet processedSprites2 = s.ProcessedSprites;
					if (processedSprites2 != null && processedSprites2.Contains(allCoin3.Index) && !_permanentlyCollectedCoins.ContainsKey(allCoin3.Index))
					{
						_permanentlyCollectedCoins[allCoin3.Index] = num6;
					}
				}
				PfLog($"[COIN_FORGIVEN_ALT] idx={spriteEntry4.Index} sid=0x{spriteEntry4.SpriteId:X2} \ufffd coin retry caused death elsewhere, forgiving");
			}
			else
			{
				if (!endLevel)
				{
					continue;
				}
				Success = true;
				string text2 = $"Completed in {i} frames ({PathPoints.Count} path points)";
				if (PreferCoins && allCoins.Count > 0)
				{
					HashSet<int> hashSet = new HashSet<int>();
					foreach (SpriteEntry allCoin4 in allCoins)
					{
						if (s.ProcessedSprites.Contains(allCoin4.Index))
						{
							hashSet.Add(allCoin4.Index);
						}
					}
					FinalCollectedCoinIndices = hashSet;
					_log.WriteLine($"[RUN_COMPLETE] coins={hashSet.Count}/{allCoins.Count} forgiven={_forgivenCoins.Count} collected=[{string.Join(",", hashSet)}]");
					foreach (SpriteEntry allCoin5 in allCoins)
					{
						bool flag13 = hashSet.Contains(allCoin5.Index);
						_log.WriteLine($"[COIN_RESULT] idx={allCoin5.Index} sid=0x{allCoin5.SpriteId:X2} pos=({allCoin5.AnchorX_px},{allCoin5.AnchorY_px}) hit=({allCoin5.HitLeft},{allCoin5.HitTop})-({allCoin5.HitRight},{allCoin5.HitBottom}) {(flag13 ? "COLLECTED" : "MISSED")}");
					}
					text2 += $" [{hashSet.Count}/{allCoins.Count} coins]";
					if (_forgivenCoins.Count > 0)
					{
						text2 += $" ({_forgivenCoins.Count} unreachable)";
					}
				}
				ResultMessage = text2;
				ExtractSkippedPads(in s);
				TraceFrameClose();
				PfLog($"[END_LEVEL] frame={i} skippedPads={SkippedPadIndices?.Count ?? 0}");
				return;
			}
		}
		SnapshotBestPath();
		UseBestPathIfBetter();
		ExtractSkippedPads(in s);
		Success = false;
		int num27 = ((PathPoints.Count > 0) ? PathPoints[PathPoints.Count - 1].x : 0);
		int value10 = ((num2 > 0) ? (num27 * 100 / num2) : 0);
		ResultMessage = $"Timeout \ufffd partial path to X={num27}px ({value10}%)";
		TraceFrameClose();
		PfLog("[TIMEOUT]");
	}

	private int SpriteLowerBound(int targetX_px)
	{
		int num = 0;
		int num2 = _spritesArr.Length;
		while (num < num2)
		{
			int num3 = num + num2 >> 1;
			if (_spritesArr[num3].AnchorX_px < targetX_px)
			{
				num = num3 + 1;
			}
			else
			{
				num2 = num3;
			}
		}
		return num;
	}

	private long BfsQuantizeKey(ref SimState s)
	{
		bool flag = s.GameMode == 1 || s.GameMode == 3 || s.GameMode == 6 || s.GameMode == 7 || s.GameMode == 10;
		int num = (flag ? ((s.Y_fixed >> 9) & 0xFFF) : ((s.Y_fixed >> 6) & 0xFFFF));
		int num2 = (flag ? ((s.VelY_fixed + 32768 >> 5) & 0xFFF) : ((s.VelY_fixed + 32768 >> 6) & 0x7FF));
		int velX_fixed = s.VelX_fixed;
		if (1 == 0)
		{
		}
		int num3 = velX_fixed switch
		{
			366 => 0, 
			571 => 1, 
			708 => 2, 
			881 => 3, 
			1065 => 4, 
			1310 => 5, 
			_ => 7, 
		};
		if (1 == 0)
		{
		}
		int num4 = num3;
		int num5 = (s.GameMode & 0xF) | ((s.GravFlipped ? 1 : 0) << 4) | ((s.Mini ? 1 : 0) << 5) | ((s.OnGround ? 1 : 0) << 6) | ((s.Orbed ? 1 : 0) << 7) | ((num4 & 7) << 8);
		int num6 = s.ProcessedSprites.GetBitsHash();
		if (s.RobotJumpTime > 0)
		{
			num6 = num6 * 31 + s.RobotJumpTime;
		}
		if (s.Step2Ejected)
		{
			num6 = 0;
		}
		if (s.DualActive)
		{
			int num7 = (flag ? ((s.P2_Y_fixed >> 11) & 0x1FF) : ((s.P2_Y_fixed >> 9) & 0x1FF));
			int num8 = (flag ? ((s.P2_VelY_fixed + 32768 >> 8) & 0xFF) : ((s.P2_VelY_fixed + 32768 >> 8) & 0xFF));
			num6 = num6 * 397 + num7;
			num6 = num6 * 397 + num8;
			num6 = num6 * 397 + (s.P2_GravFlipped ? 1 : 0);
		}
		return ((long)(num6 & 0x1FFFF) << 38) | ((long)(num5 & 0x7FF) << 27) | ((long)(num2 & 0x7FF) << 16) | (num & 0xFFFF);
	}

	private int BfsScore(ref SimState s, int coinsCollected)
	{
		int num = -coinsCollected * 1000000;
		int num2 = 0;
		if (JumpTimingBias < 0.45)
		{
			num2 = (s.Y_fixed >> 8) / 4;
		}
		else if (JumpTimingBias > 0.55)
		{
			num2 = -(s.Y_fixed >> 8) / 4;
		}
		int num3 = 0;
		int num4 = 0;
		return num + num2 + num3 + num4;
	}

	private void RunBFS(int startX_px, int startY_px, int startSpeedUiIndex, int startGameMode, bool startGravFlipped, bool startMini)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		if (Verbose)
		{
			_log.WriteLine($"[BFS] Starting exhaustive BFS exploration (frontier cap={120000})");
		}
		if (Verbose)
		{
			_log.WriteLine($"[BFS_PARAMS] startX={startX_px} startY={startY_px} speed={startSpeedUiIndex} mode={startGameMode} gravFlip={startGravFlipped} mini={startMini} bias={JumpTimingBias} coins={PreferCoins} useBfs={UseBFS}");
		}
		_step1FireCount = 0;
		_step2FireCount = 0;
		BfsLog($"Starting BFS (frontier cap={120000})");
		BfsLog($"PARAMS startX={startX_px} startY={startY_px} speed={startSpeedUiIndex} mode={startGameMode} gravFlip={startGravFlipped} mini={startMini} bias={JumpTimingBias} coins={PreferCoins} useBfs={UseBFS}");
		try
		{
			int num = ComputeInitCameraY(startY_px);
			SimState s = new SimState
			{
				X_fixed = startX_px << 8,
				Y_fixed = startY_px << 8,
				VelX_fixed = SpeedUiIndexToFixed(startSpeedUiIndex),
				VelY_fixed = 0,
				GameMode = startGameMode,
				GravFlipped = startGravFlipped,
				Mini = startMini,
				GravMul = ((!startGravFlipped) ? 1 : (-1)),
				GravityMod = 1.0,
				WasZeroedByCollision = true,
				OnGround = true,
				ProcessedSprites = NewSpriteSet(),
				PendingOrbIndex = -1,
				PendingOrbSpriteId = -1,
				PendingOrbExtra1Index = -1,
				PendingOrbExtra1SpriteId = -1,
				PendingOrbExtra2Index = -1,
				PendingOrbExtra2SpriteId = -1,
				NinjaJumps = ((startGameMode == 8) ? 3 : 0),
				CameraY_fixed = num,
				TargetCameraY_fixed = num
			};
			ApplyPortalsUpTo(ref s, startX_px);
			ApplyNesIntroFreezePrestep(ref s);
			_btSkipAllOrbs = false;
			_btSkipSpecificOrbs.Clear();
			_btSkipSpecificPads.Clear();
			_speculativeDepth = 1;
			int num2 = mapWidth * 16;
			int num3 = startX_px;
			int num4 = -1;
			List<SimState> frontier = new List<SimState> { s };
			List<int[]> list = new List<int[]>();
			List<bool[]> list2 = new List<bool[]>();
			int num5 = -1;
			int num6 = -1;
			int num7 = -1;
			bool item = false;
			SimState simState = default(SimState);
			int num8 = -1;
			int num9 = -1;
			int num10 = startX_px;
			int num11 = 240000;
			SimState[] rState = new SimState[num11];
			bool[] rAlive = new bool[num11];
			bool[] rEnd = new bool[num11];
			List<SimState> list3 = new List<SimState>(num11);
			List<int> list4 = new List<int>(num11);
			List<bool> list5 = new List<bool>(num11);
			List<int> list6 = new List<int>(num11);
			List<int> candScore = new List<int>(num11);
			Dictionary<long, int> dictionary = new Dictionary<long, int>(num11);
			for (int i = 0; i < 28800; i++)
			{
				if (frontier.Count <= 0)
				{
					break;
				}
				if (CancelRequested)
				{
					break;
				}
				_frameCounter = i;
				int num12 = frontier[0].X_fixed >> 8;
				if (num12 > num3)
				{
					num3 = num12;
				}
				_currentX_px = num3;
				int num13 = ((num2 > 0) ? (num3 * 100 / num2) : 0);
				if (num13 != num4 && Progress != null)
				{
					Progress.Report(num13);
					num4 = num13;
				}
				int num14 = frontier.Count * 2;
				Parallel.For(0, num14, delegate(int k)
				{
					int index6 = k >> 1;
					bool input2 = (k & 1) == 1;
					SimState s8 = frontier[index6].Clone();
					bool endLevel3;
					bool flag17 = StepFrame(ref s8, input2, out endLevel3);
					if (flag17 && !endLevel3)
					{
						bool flag18 = s8.RainbowMaxMode > 0 && frontier[index6].RainbowMaxMode == 0;
						bool flag19 = s8.RainbowShadows != null;
						bool endLevel4;
						if (flag18)
						{
							int rainbowMaxMode = s8.RainbowMaxMode;
							SimState[] array14 = new SimState[rainbowMaxMode - 1];
							int num162 = 0;
							for (int j = 0; j < rainbowMaxMode && flag17; j++)
							{
								if (j != s8.GameMode)
								{
									SimState s9 = frontier[index6].Clone();
									s9.GameMode = j;
									s9.RainbowMaxMode = rainbowMaxMode;
									s9.RainbowShadows = null;
									if (frontier[index6].GameMode == 6 || frontier[index6].GameMode == 10)
									{
										s9.VelY_fixed = 0;
									}
									if (!StepFrame(ref s9, input2, out endLevel4))
									{
										s9.ProcessedSprites.Return();
										flag17 = false;
										for (int l = 0; l < num162; l++)
										{
											array14[l].ProcessedSprites.Return();
										}
									}
									else
									{
										array14[num162++] = s9;
									}
								}
							}
							if (flag17)
							{
								s8.RainbowShadows = array14;
							}
						}
						else if (flag19)
						{
							SimState[] rainbowShadows = s8.RainbowShadows;
							for (int m = 0; m < rainbowShadows.Length && flag17; m++)
							{
								if (!StepFrame(ref rainbowShadows[m], input2, out endLevel4))
								{
									flag17 = false;
									rainbowShadows[m].ProcessedSprites.Return();
									for (int n = m + 1; n < rainbowShadows.Length; n++)
									{
										rainbowShadows[n].ProcessedSprites.Return();
									}
									s8.RainbowShadows = null;
								}
							}
							if (flag17 && s8.RainbowMaxMode == 0 && s8.RainbowShadows != null)
							{
								for (int num163 = 0; num163 < s8.RainbowShadows.Length; num163++)
								{
									s8.RainbowShadows[num163].ProcessedSprites.Return();
								}
								s8.RainbowShadows = null;
							}
						}
					}
					rState[k] = s8;
					rAlive[k] = flag17;
					rEnd[k] = endLevel3;
				});
				list3.Clear();
				list4.Clear();
				list5.Clear();
				list6.Clear();
				candScore.Clear();
				int num15 = 0;
				int num16 = 0;
				int num17 = 0;
				int[] array = new int[13];
				bool flag = (i >= 2000 && i <= 2070) || (i >= 3550 && i <= 3850);
				int[] array2 = (flag ? new int[13] : null);
				int num18 = 0;
				for (int num19 = 0; num19 < num14; num19++)
				{
					int num20 = num19 >> 1;
					bool flag2 = (num19 & 1) == 1;
					if (rEnd[num19])
					{
						SimState s2 = rState[num19];
						int num21 = CountBfsCoins(ref s2);
						if (num5 < 0 || num21 > num7)
						{
							num5 = i;
							num6 = num20;
							item = flag2;
							num7 = num21;
							simState = s2;
							_log.WriteLine($"[BFS] Level complete at frame {i}! coins={num21} X\ufffd{s2.X_fixed >> 8}px");
						}
					}
					else if (!rAlive[num19])
					{
						if (rState[num19].DeathType == 9)
						{
							num16++;
						}
						if (array2 != null)
						{
							array2[rState[num19].DeathType]++;
						}
						if (flag && i >= 3550)
						{
							int index = num19 >> 1;
							int num22 = frontier[index].Y_fixed >> 8;
							if (num22 <= 200)
							{
								num18++;
							}
						}
						int index2 = num19 >> 1;
						SimState simState2 = frontier[index2];
						if (simState2.GravFlipped)
						{
							num17++;
							byte deathType = rState[num19].DeathType;
							if (deathType >= 0 && deathType < array.Length)
							{
								array[deathType]++;
							}
							if (_gravFDeathCounts == null)
							{
								_gravFDeathCounts = new int[13];
							}
							if (deathType < _gravFDeathCounts.Length)
							{
								_gravFDeathCounts[deathType]++;
							}
							_gravFDeathFrame = i;
							if (num17 <= 3 && i >= 1890)
							{
								SimState simState3 = rState[num19];
								_log.WriteLine($"[GF_DEAD] f={i} pX={simState2.X_fixed >> 8} pY={simState2.Y_fixed >> 8} pVelY=0x{simState2.VelY_fixed:X} | cX={simState3.X_fixed >> 8} cY={simState3.Y_fixed >> 8} dt={deathType} inp={(num19 & 1) == 1}");
							}
						}
						rState[num19].ReturnAllSpriteResources();
						num15++;
					}
					else
					{
						SimState s3 = rState[num19];
						int num23 = CountBfsCoins(ref s3);
						int item2 = BfsScore(ref s3, num23);
						list3.Add(s3);
						list4.Add(num20);
						list5.Add(flag2);
						list6.Add(num23);
						candScore.Add(item2);
					}
				}
				if (num16 > 0)
				{
					Console.Error.WriteLine($"[S8B] f={i} step8b={num16} total={num15} front={frontier.Count} cand={list3.Count} mode={frontier[0].GameMode}");
				}
				if (list3.Count > 0 && list3[0].DualActive)
				{
					int num24 = list3[0].X_fixed >> 8;
					bool flag3 = num24 >= 3500 && num24 <= 3560;
					bool flag4 = num24 >= 3100 && num24 < 3500 && i % 10 == 0;
					if (flag3 || flag4)
					{
						int num25 = 9999;
						int num26 = -1;
						int num27 = 9999;
						int num28 = -1;
						int num29 = 0;
						int num30 = 0;
						int num31 = 0;
						int num32 = 0;
						int num33 = 0;
						int num34 = 0;
						foreach (SimState item3 in list3)
						{
							int num35 = item3.Y_fixed >> 8;
							int num36 = item3.P2_Y_fixed >> 8;
							if (num35 < num25)
							{
								num25 = num35;
							}
							if (num35 > num26)
							{
								num26 = num35;
							}
							if (num36 < num27)
							{
								num27 = num36;
							}
							if (num36 > num28)
							{
								num28 = num36;
							}
							bool flag5 = num35 >= 441 && num35 <= 470;
							bool flag6 = num36 >= 357;
							if (flag5)
							{
								num29++;
							}
							if (flag6)
							{
								num30++;
							}
							if (flag5 && flag6)
							{
								num31++;
							}
							bool flag7 = num35 == 401;
							bool flag8 = num36 == 401;
							if (flag7)
							{
								num32++;
							}
							if (flag8)
							{
								num33++;
							}
							if (flag7 && flag8)
							{
								num34++;
							}
						}
						Console.Error.WriteLine($"[DUAL_Y] f={i} X={num24} cands={list3.Count} P1:[{num25},{num26}] p1s={num29} on401={num32} P2:[{num27},{num28}] p2s={num30} on401={num33} both={num31} both401={num34}");
						if (flag3)
						{
							foreach (SimState item4 in list3)
							{
								int num37 = item4.Y_fixed >> 8;
								int num38 = item4.P2_Y_fixed >> 8;
								int velY_fixed = item4.VelY_fixed;
								int p2_VelY_fixed = item4.P2_VelY_fixed;
								if ((num37 >= 355 && num37 <= 420) || (num38 >= 355 && num38 <= 420))
								{
									Console.Error.WriteLine($"  [ST] X={num24} P1Y={num37} P1V={velY_fixed} P2Y={num38} P2V={p2_VelY_fixed}");
								}
							}
						}
					}
				}
				if (Verbose && frontier.Count <= 10 && frontier.Count > 0 && frontier[0].GameMode == 1 && list3.Count > 0)
				{
					_log.WriteLine($"[SHIP_DIAG] f={i} front={frontier.Count} cands={list3.Count} deaths={num15}");
					for (int num39 = 0; num39 < Math.Min(list3.Count, 20); num39++)
					{
						SimState s4 = list3[num39];
						long value = BfsQuantizeKey(ref s4);
						_log.WriteLine($"  cand[{num39}] inp={list5[num39]} Y={s4.Y_fixed >> 8} Yfx=0x{s4.Y_fixed:X} VelY=0x{s4.VelY_fixed:X} X={s4.X_fixed >> 8} key=0x{value:X16} score={candScore[num39]} grav={s4.GravFlipped} mode={s4.GameMode}");
					}
					for (int num40 = 0; num40 < Math.Min(frontier.Count, 10); num40++)
					{
						SimState simState4 = frontier[num40];
						_log.WriteLine($"  parent[{num40}] Y={simState4.Y_fixed >> 8} Yfx=0x{simState4.Y_fixed:X} VelY=0x{simState4.VelY_fixed:X} X={simState4.X_fixed >> 8}");
					}
				}
				if (i >= 1930 && i <= 2050 && i % 10 == 0)
				{
					int[] array3 = new int[5] { 10310, 11414, 10314, 11418, 11432 };
					int[] array4 = new int[array3.Length];
					int num41 = 0;
					int num42 = 0;
					int num43 = 0;
					foreach (SimState item5 in list3)
					{
						for (int num44 = 0; num44 < array3.Length; num44++)
						{
							if (item5.ProcessedSprites.Contains(array3[num44]))
							{
								array4[num44]++;
							}
						}
						if (item5.ProcessedSprites.Contains(10319))
						{
							num41++;
						}
						if (item5.ProcessedSprites.Contains(10332))
						{
							num42++;
						}
						if (item5.ProcessedSprites.Contains(9768))
						{
							num43++;
						}
					}
					Console.Error.WriteLine($"[ORB_TRACK] f={i} cands={list3.Count} green392={array4[0]} green394={array4[1]} green396={array4[2]} green398={array4[3]} green412={array4[4]} red401={num41} blue414={num42} gravPortal401={num43}");
				}
				if (i >= 1990 && i <= 2040 && i % 5 == 0)
				{
					int[] array5 = new int[9];
					int[] array6 = new int[9];
					foreach (SimState item6 in list3)
					{
						int num45 = item6.Y_fixed >> 8;
						int num46 = ((num45 >= 240) ? ((num45 >= 352) ? 8 : ((num45 - 240) / 16 + 1)) : 0);
						if (item6.GravFlipped)
						{
							array6[num46]++;
						}
						else
						{
							array5[num46]++;
						}
					}
					Console.Error.WriteLine($"[Y_HIST] f={i} cands={list3.Count} N:<240={array5[0]} 240={array5[1]} 256={array5[2]} 272={array5[3]} 288={array5[4]} 304={array5[5]} 320={array5[6]} 336={array5[7]} 352+={array5[8]} R:<240={array6[0]} 240={array6[1]} 256={array6[2]} 272={array6[3]} 288={array6[4]} 304={array6[5]} 320={array6[6]} 336={array6[7]} 352+={array6[8]}");
				}
				if (list3.Count == 0)
				{
					_log.WriteLine($"[BFS] ALL DEAD at frame {i} (X~{num3}px pct={num13}% expanded={frontier.Count * 2} deaths={num15})");
					BfsLog($"ALL DEAD at frame {i} (X~{num3}px pct={num13}% expanded={frontier.Count * 2} deaths={num15})");
					int[] array7 = new int[13];
					for (int num47 = 0; num47 < num14; num47++)
					{
						if (!rAlive[num47] && !rEnd[num47])
						{
							array7[rState[num47].DeathType]++;
						}
					}
					string[] array8 = new string[13]
					{
						"UNK", "CEIL_SPIKE", "EJECT", "CENTER", "BALL_PROBE", "BALL_VELZERO", "BALL_EJECT", "FLOOR_SPIKE", "FWD", "DEATH_COLL",
						"BOUNDS", "BALL_EJECT_CEIL", "BALL_EJECT_FLOOR"
					};
					List<string> list7 = new List<string>();
					for (int num48 = 0; num48 < array7.Length; num48++)
					{
						if (array7[num48] > 0)
						{
							list7.Add($"{array8[num48]}={array7[num48]}");
						}
					}
					_log.WriteLine("[BFS] Death types: " + string.Join(" ", list7));
					Console.Error.WriteLine("[BFS] Death types: " + string.Join(" ", list7));
					PfLog("[BFS_ALLDEAD] Death types: " + string.Join(" ", list7));
					int num49 = 0;
					for (int num50 = 0; num50 < num14; num50++)
					{
						if (num49 >= 5)
						{
							break;
						}
						if (!rAlive[num50] && !rEnd[num50])
						{
							SimState simState5 = rState[num50];
							int index3 = num50 >> 1;
							SimState simState6 = frontier[index3];
							_log.WriteLine($"[BFS_DEAD] parent X=0x{simState6.X_fixed:X} Y=0x{simState6.Y_fixed:X} VelY=0x{simState6.VelY_fixed:X} grav={simState6.GravFlipped} mini={simState6.Mini} dual={simState6.DualActive} P2_Y=0x{simState6.P2_Y_fixed:X} P2_grav={simState6.P2_GravFlipped} | child X=0x{simState5.X_fixed:X} Y=0x{simState5.Y_fixed:X} VelY=0x{simState5.VelY_fixed:X} grav={simState5.GravFlipped} mini={simState5.Mini} dt={simState5.DeathType} inp={(num50 & 1) == 1}");
							Console.Error.WriteLine($"[BFS_DEAD] parent Y={simState6.Y_fixed >> 8} VelY=0x{simState6.VelY_fixed:X} grav={simState6.GravFlipped} | child Y={simState5.Y_fixed >> 8} VelY=0x{simState5.VelY_fixed:X} dt={array8[simState5.DeathType]} inp={(num50 & 1) == 1}");
							PfLog($"[BFS_DEAD] parent X=0x{simState6.X_fixed:X} Y=0x{simState6.Y_fixed:X} VelY=0x{simState6.VelY_fixed:X} grav={simState6.GravFlipped} dual={simState6.DualActive} P2_Y=0x{simState6.P2_Y_fixed:X} P2_VelY=0x{simState6.P2_VelY_fixed:X} | child X=0x{simState5.X_fixed:X} Y=0x{simState5.Y_fixed:X} VelY=0x{simState5.VelY_fixed:X} grav={simState5.GravFlipped} dt={simState5.DeathType} inp={(num50 & 1) == 1}");
							num49++;
						}
					}
					if (frontier.Count > 0)
					{
						int num51 = int.MaxValue;
						int num52 = int.MinValue;
						int num53 = int.MaxValue;
						int num54 = int.MinValue;
						int num55 = int.MaxValue;
						int num56 = int.MinValue;
						int num57 = int.MaxValue;
						int num58 = int.MinValue;
						foreach (SimState item7 in frontier)
						{
							int num59 = item7.Y_fixed >> 8;
							int num60 = item7.X_fixed >> 8;
							if (num59 < num51)
							{
								num51 = num59;
							}
							if (num59 > num52)
							{
								num52 = num59;
							}
							if (num60 < num55)
							{
								num55 = num60;
							}
							if (num60 > num56)
							{
								num56 = num60;
							}
							if (item7.VelY_fixed < num53)
							{
								num53 = item7.VelY_fixed;
							}
							if (item7.VelY_fixed > num54)
							{
								num54 = item7.VelY_fixed;
							}
							if (item7.DualActive)
							{
								int num61 = item7.P2_Y_fixed >> 8;
								if (num61 < num57)
								{
									num57 = num61;
								}
								if (num61 > num58)
								{
									num58 = num61;
								}
							}
						}
						string value2 = (frontier[0].DualActive ? $" P2_Y=[{num57}..{num58}]" : "");
						_log.WriteLine($"[BFS] Last frontier: size={frontier.Count} X=[{num55}..{num56}] Y=[{num51}..{num52}] mode={frontier[0].GameMode} grav={frontier[0].GravFlipped} mini={frontier[0].Mini} VelY=[0x{num53:X}..0x{num54:X}]{value2}");
						Console.Error.WriteLine($"[BFS] Last frontier: size={frontier.Count} X=[{num55}..{num56}] Y=[{num51}..{num52}] mode={frontier[0].GameMode} grav={frontier[0].GravFlipped} mini={frontier[0].Mini} VelY=[0x{num53:X}..0x{num54:X}]{value2}");
						PfLog($"[BFS_ALLDEAD] Last frontier: size={frontier.Count} X=[{num55}..{num56}] Y=[{num51}..{num52}] mode={frontier[0].GameMode} grav={frontier[0].GravFlipped} mini={frontier[0].Mini} dual={frontier[0].DualActive} VelY=[0x{num53:X}..0x{num54:X}]{value2}");
					}
					_log.Flush();
					break;
				}
				if (num5 >= 0)
				{
					bool flag9 = false;
					for (int num62 = 0; num62 < list6.Count; num62++)
					{
						if (list6[num62] > num7)
						{
							flag9 = true;
							break;
						}
					}
					if (!flag9 && PreferCoins && allCoins.Count > 0 && list3.Count > 0)
					{
						int num63 = 0;
						for (int num64 = 0; num64 < list3.Count; num64++)
						{
							int num65 = list3[num64].X_fixed >> 8;
							if (num65 > num63)
							{
								num63 = num65;
							}
						}
						foreach (SpriteEntry allCoin in allCoins)
						{
							if (allCoin.HitRight > num63 && !simState.ProcessedSprites.Contains(allCoin.Index))
							{
								flag9 = true;
								break;
							}
						}
					}
					if (!flag9)
					{
						_log.WriteLine($"[BFS] Optimal \ufffd winning path at frame {num5} with {num7} coins, no better candidates");
						break;
					}
				}
				dictionary.Clear();
				for (int num66 = 0; num66 < list3.Count; num66++)
				{
					SimState s5 = list3[num66];
					long key = BfsQuantizeKey(ref s5);
					if (!dictionary.TryGetValue(key, out var value3) || candScore[num66] < candScore[value3])
					{
						dictionary[key] = num66;
					}
				}
				int num67 = 0;
				int num68 = 0;
				for (int num69 = 0; num69 < list3.Count; num69++)
				{
					if (list3[num69].GravFlipped)
					{
						num67++;
					}
				}
				foreach (int value12 in dictionary.Values)
				{
					if (list3[value12].GravFlipped)
					{
						num68++;
					}
				}
				int num70 = 0;
				foreach (SimState item8 in frontier)
				{
					if (item8.GravFlipped)
					{
						num70++;
					}
				}
				if (num70 > 0 || num67 > 0)
				{
					int num71 = int.MaxValue;
					int num72 = int.MinValue;
					for (int num73 = 0; num73 < list3.Count; num73++)
					{
						if (list3[num73].GravFlipped)
						{
							int num74 = list3[num73].Y_fixed >> 8;
							if (num74 < num71)
							{
								num71 = num74;
							}
							if (num74 > num72)
							{
								num72 = num74;
							}
						}
					}
					string text = "";
					if (num17 > 0)
					{
						string[] array9 = new string[13]
						{
							"UNK", "CEIL", "EJT", "CTR", "BPR", "BVZ", "BEJ", "FLR", "FWD", "DCL",
							"BND", "OOT", "OOB"
						};
						for (int num75 = 0; num75 < array.Length; num75++)
						{
							if (array[num75] > 0)
							{
								text += $" {((num75 < array9.Length) ? array9[num75] : $"d{num75}")}={array[num75]}";
							}
						}
					}
					_log.WriteLine($"[GF_PIPE] f={i} frontGF={num70} gfDied={num17}{text} candGF={num67} dedupGF={num68} gfY=[{((num71 == int.MaxValue) ? "N/A" : num71.ToString())}..{((num72 == int.MinValue) ? "N/A" : num72.ToString())}]");
				}
				List<int> list8 = new List<int>(dictionary.Values);
				list8.Sort((int a, int b) => candScore[a].CompareTo(candScore[b]));
				List<SimState> list9 = new List<SimState>();
				List<int> list10 = new List<int>();
				List<bool> list11 = new List<bool>();
				int num76 = 120000;
				int val = num76 * 3 / 4;
				int num77 = Math.Min(val, list8.Count);
				for (int num78 = 0; num78 < num77; num78++)
				{
					if (list9.Count >= num76)
					{
						break;
					}
					int index4 = list8[num78];
					list9.Add(list3[index4]);
					list10.Add(list4[index4]);
					list11.Add(list5[index4]);
				}
				if (list8.Count > num77)
				{
					int num79 = 16;
					Dictionary<int, int> dictionary2 = new Dictionary<int, int>();
					foreach (SimState item9 in list9)
					{
						int key2 = (item9.Y_fixed >> 8) / num79;
						dictionary2.TryGetValue(key2, out var value4);
						dictionary2[key2] = value4 + 1;
					}
					int num80 = list9.Count / Math.Max(1, dictionary2.Count);
					int num81 = 0;
					int num82 = 0;
					foreach (SimState item10 in list9)
					{
						if (item10.GravFlipped)
						{
							num82++;
						}
						else
						{
							num81++;
						}
					}
					int num83 = Math.Min(num81, num82);
					bool flag10 = num83 < list9.Count / 20;
					Dictionary<int, int> dictionary3 = new Dictionary<int, int>();
					foreach (SimState item11 in list9)
					{
						dictionary3.TryGetValue(item11.GameMode, out var value5);
						dictionary3[item11.GameMode] = value5 + 1;
					}
					int count = dictionary3.Count;
					bool flag11 = false;
					int num84 = -1;
					if (count > 1)
					{
						int num85 = int.MaxValue;
						foreach (KeyValuePair<int, int> item12 in dictionary3)
						{
							if (item12.Value < num85)
							{
								num85 = item12.Value;
								num84 = item12.Key;
							}
						}
						flag11 = num85 < list9.Count / 10;
					}
					for (int num86 = num77; num86 < list8.Count; num86++)
					{
						if (list9.Count >= num76)
						{
							break;
						}
						int index5 = list8[num86];
						int key3 = (list3[index5].Y_fixed >> 8) / num79;
						dictionary2.TryGetValue(key3, out var value6);
						bool flag12 = value6 < num80 + 1;
						bool flag13 = flag10 && ((list3[index5].GravFlipped && num82 < num81) || (!list3[index5].GravFlipped && num81 < num82));
						bool flag14 = flag11 && list3[index5].GameMode == num84;
						if (flag12 || flag13 || flag14)
						{
							list9.Add(list3[index5]);
							list10.Add(list4[index5]);
							list11.Add(list5[index5]);
							dictionary2[key3] = value6 + 1;
							if (list3[index5].GravFlipped)
							{
								num82++;
							}
							else
							{
								num81++;
							}
							num83 = Math.Min(num81, num82);
							flag10 = num83 < list9.Count / 20;
							dictionary3.TryGetValue(list3[index5].GameMode, out var value7);
							dictionary3[list3[index5].GameMode] = value7 + 1;
							if (list3[index5].GameMode == num84)
							{
								int num87 = value7 + 1;
								flag11 = num87 < list9.Count / 10;
							}
						}
					}
				}
				int num88 = 0;
				foreach (SimState item13 in list9)
				{
					if (item13.GravFlipped)
					{
						num88++;
					}
				}
				int num89 = 0;
				foreach (int value13 in dictionary.Values)
				{
					if (list3[value13].GravFlipped)
					{
						num89++;
					}
				}
				if (num89 > 0 && num88 != num89)
				{
					_log.WriteLine($"[GF_SELECT] f={i} dedupGF={num89} selectedGF={num88} totalNext={list9.Count}");
				}
				HashSet<object> hashSet = new HashSet<object>(list9.Count);
				foreach (SimState item14 in list9)
				{
					hashSet.Add(item14.ProcessedSprites);
				}
				for (int num90 = 0; num90 < list3.Count; num90++)
				{
					if (!hashSet.Contains(list3[num90].ProcessedSprites))
					{
						list3[num90].ReturnAllSpriteResources();
					}
				}
				list.Add(list10.ToArray());
				list2.Add(list11.ToArray());
				if (list9.Count > 0)
				{
					int num91 = list9[0].X_fixed >> 8;
					bool flag15 = num91 > 29300 && num91 < 29600;
					if (num91 > 28000 && num91 < 30000 && (i % 50 == 0 || (flag15 && i % 5 == 0)))
					{
						int num92 = 0;
						int num93 = 0;
						int num94 = 0;
						int num95 = 0;
						int num96 = int.MaxValue;
						int num97 = int.MinValue;
						foreach (SimState item15 in list9)
						{
							int num98 = item15.Y_fixed >> 8;
							if (num98 > 260)
							{
								num92++;
							}
							else
							{
								num93++;
							}
							if (num98 >= 281 && num98 <= 296)
							{
								num94++;
							}
							if (item15.PendingOrbSpriteId == 40)
							{
								num95++;
							}
							if (num98 < num96)
							{
								num96 = num98;
							}
							if (num98 > num97)
							{
								num97 = num98;
							}
						}
						int gameMode = list9[0].GameMode;
						Console.Error.WriteLine($"[BFS_FRON45] f={i} X={num91} sz={list9.Count} Y=[{num96}..{num97}] yHigh={num93} yLow={num92} gap={num94} yOrb={num95} mode={gameMode}");
					}
				}
				for (int num99 = 0; num99 < list9.Count; num99++)
				{
					int num100 = list9[num99].X_fixed >> 8;
					if (num100 > num10)
					{
						num8 = list.Count - 1;
						num9 = num99;
						num10 = num100;
					}
				}
				foreach (SimState item16 in frontier)
				{
					SimState simState7 = item16;
					simState7.ReturnAllSpriteResources();
				}
				frontier = list9;
				bool flag16 = frontier.Count > 0 && frontier[0].DualActive;
				if (i % 100 == 0 || frontier.Count < 100 || (i >= 700 && i <= 810) || flag16 || (i >= 2000 && i <= 2070) || (i >= 3550 && i <= 3850))
				{
					int num101 = int.MaxValue;
					int num102 = int.MinValue;
					int num103 = 0;
					int num104 = 0;
					foreach (SimState item17 in frontier)
					{
						int num105 = item17.Y_fixed >> 8;
						if (num105 < num101)
						{
							num101 = num105;
						}
						if (num105 > num102)
						{
							num102 = num105;
						}
						if (item17.GravFlipped)
						{
							num104++;
						}
						else
						{
							num103++;
						}
					}
					double value8 = stopwatch.Elapsed.TotalMilliseconds / (double)Math.Max(1, i + 1);
					string value9 = "";
					if (flag16)
					{
						int num106 = int.MaxValue;
						int num107 = int.MinValue;
						foreach (SimState item18 in frontier)
						{
							int num108 = item18.P2_Y_fixed >> 8;
							if (num108 < num106)
							{
								num106 = num108;
							}
							if (num108 > num107)
							{
								num107 = num108;
							}
						}
						value9 = $" DUAL P2_Y=[{num106}..{num107}]";
					}
					int[] array10 = new int[12];
					foreach (SimState item19 in frontier)
					{
						if (item19.GameMode >= 0 && item19.GameMode < 12)
						{
							array10[item19.GameMode]++;
						}
					}
					string text2 = "";
					for (int num109 = 0; num109 < 12; num109++)
					{
						if (array10[num109] > 0)
						{
							text2 += $" m{num109}={array10[num109]}";
						}
					}
					if (Verbose)
					{
						_log.WriteLine($"[BFS] f={i} front={frontier.Count} dedup={dictionary.Count} deaths={num15} Y=[{num101}..{num102}] mode={frontier[0].GameMode} gravN={num103} gravF={num104} X~{num3}px pct={num13}% ms/f={value8:F1}{value9} modes:{text2}");
					}
					if (Verbose && i >= 3550 && i <= 3850 && i % 10 == 0)
					{
						int num110 = 0;
						int num111 = 0;
						int num112 = 0;
						int num113 = 0;
						int num114 = 0;
						int num115 = 0;
						int num116 = 0;
						foreach (SimState item20 in frontier)
						{
							int num117 = item20.Y_fixed >> 8;
							if (num117 < 135)
							{
								num110++;
							}
							else if (num117 <= 150)
							{
								num111++;
							}
							else if (num117 <= 167)
							{
								num112++;
								num116++;
							}
							else if (num117 <= 200)
							{
								num113++;
							}
							else if (num117 <= 250)
							{
								num114++;
							}
							else
							{
								num115++;
							}
						}
						_log.WriteLine($"[BFS_COIN_YHIST] f={i} X~{num3}px <135={num110} 135-150={num111} 151-167={num112} 168-200={num113} 201-250={num114} 251+={num115} coinHitY={num116}");
					}
					if (Verbose && (i == 700 || i == 710 || i == 720 || i == 800 || i == 900 || i == 1500 || i == 2020 || i == 2035 || i == 2040))
					{
						int num118 = 0;
						int num119 = int.MaxValue;
						int num120 = int.MinValue;
						int num121 = int.MaxValue;
						int num122 = int.MinValue;
						for (int num123 = 0; num123 < frontier.Count; num123++)
						{
							SimState simState8 = frontier[num123];
							int num124 = simState8.Y_fixed >> 8;
							if (simState8.Step2Ever)
							{
								num118++;
								if (num124 < num121)
								{
									num121 = num124;
								}
								if (num124 > num122)
								{
									num122 = num124;
								}
							}
							else
							{
								if (num124 < num119)
								{
									num119 = num124;
								}
								if (num124 > num120)
								{
									num120 = num124;
								}
							}
						}
						_log.WriteLine($"[BFS_S2] f={i} Step2Ever={num118}/{frontier.Count} noS2_Y=[{((num119 == int.MaxValue) ? "N/A" : num119.ToString())}..{((num120 == int.MinValue) ? "N/A" : num120.ToString())}] s2_Y=[{((num121 == int.MaxValue) ? "N/A" : num121.ToString())}..{((num122 == int.MinValue) ? "N/A" : num122.ToString())}]");
					}
					if (Verbose && i >= 2020 && i <= 2042 && i % 2 == 0)
					{
						int num125 = 0;
						int num126 = 0;
						int num127 = 0;
						int num128 = 0;
						int num129 = 0;
						int num130 = 0;
						int num131 = 0;
						int num132 = 0;
						for (int num133 = 0; num133 < frontier.Count; num133++)
						{
							int num134 = frontier[num133].Y_fixed >> 8;
							if (num134 < 288)
							{
								num125++;
							}
							else if (num134 < 304)
							{
								num126++;
							}
							else if (num134 < 320)
							{
								num127++;
							}
							else if (num134 < 336)
							{
								num128++;
								if (frontier[num133].GravFlipped)
								{
									num132++;
								}
								else
								{
									num131++;
								}
							}
							else if (num134 < 352)
							{
								num129++;
							}
							else
							{
								num130++;
							}
						}
						_log.WriteLine($"[BFS_YHIST] f={i} <288={num125} 288-303={num126} 304-319={num127} 320-335={num128}(gN={num131},gF={num132}) 336-351={num129} 352+={num130}");
						if (num128 > 0)
						{
							int num135 = 0;
							int num136 = 0;
							int num137 = int.MaxValue;
							int num138 = int.MinValue;
							for (int num139 = 0; num139 < frontier.Count; num139++)
							{
								int num140 = frontier[num139].Y_fixed >> 8;
								if (num140 >= 320 && num140 < 336)
								{
									int num141 = frontier[num139].X_fixed >> 8;
									if (num141 < num137)
									{
										num137 = num141;
									}
									if (num141 > num138)
									{
										num138 = num141;
									}
									if (num141 >= num3 - 200)
									{
										num135++;
									}
									else
									{
										num136++;
									}
								}
							}
							_log.WriteLine($"[BFS_GAPX] f={i} count={num128} X=[{num137}..{num138}] near(within200)={num135} far={num136}");
						}
					}
					if (Verbose)
					{
						_log.Flush();
					}
					if (Verbose && i >= 703 && i <= 810)
					{
						long num142 = 0L;
						for (int num143 = 0; num143 < frontier.Count; num143++)
						{
							SimState simState9 = frontier[num143];
							num142 = num142 * 31 + simState9.X_fixed + (long)simState9.Y_fixed * 7L + (long)simState9.VelY_fixed * 13L + (simState9.GravFlipped ? 1 : 0) + (long)simState9.GameMode * 37L;
						}
						string text3 = $"[BFS_HASH] f={i} frontHash=0x{num142:X16} first=(X=0x{frontier[0].X_fixed:X},Y=0x{frontier[0].Y_fixed:X},VelY=0x{frontier[0].VelY_fixed:X},mode={frontier[0].GameMode}) last=(X=0x{frontier[frontier.Count - 1].X_fixed:X},Y=0x{frontier[frontier.Count - 1].Y_fixed:X},VelY=0x{frontier[frontier.Count - 1].VelY_fixed:X},mode={frontier[frontier.Count - 1].GameMode})";
						_log.WriteLine(text3);
						_log.Flush();
						BfsLog(text3);
					}
					if (Verbose && array2 != null && num15 > 0)
					{
						string[] array11 = new string[13]
						{
							"UNK", "CEIL_SPIKE", "EJECT", "CENTER", "BALL_PROBE", "BALL_VELZERO", "BALL_EJECT", "FLOOR_SPIKE", "FWD", "DEATH_COLL",
							"BOUNDS", "BALL_EJECT_CEIL", "BALL_EJECT_FLOOR"
						};
						List<string> list12 = new List<string>();
						for (int num144 = 0; num144 < array11.Length && num144 < array2.Length; num144++)
						{
							if (array2[num144] > 0)
							{
								list12.Add($"{array11[num144]}={array2[num144]}");
							}
						}
						string value10 = ((i >= 3550 && i <= 3850) ? $" lowYDeaths={num18}" : "");
						_log.WriteLine($"[BFS_DT] f={i} DEATH_TYPES: {string.Join(" ", list12)}{value10}");
						_log.WriteLine($"[BFS_DT] f={i} DEATH_TYPES: {string.Join(" ", list12)}");
					}
					BfsLog($"f={i} front={frontier.Count} dedup={dictionary.Count} deaths={num15} Y=[{num101}..{num102}] mode={frontier[0].GameMode} gravN={num103} gravF={num104} X~{num3}px pct={num13}% ms/f={value8:F1}{value9}");
					if (array2 != null && num15 > 0)
					{
						string[] array12 = new string[13]
						{
							"UNK", "CEIL_SPIKE", "EJECT", "CENTER", "BALL_PROBE", "BALL_VELZERO", "BALL_EJECT", "FLOOR_SPIKE", "FWD", "DEATH_COLL",
							"BOUNDS", "BALL_EJECT_CEIL", "BALL_EJECT_FLOOR"
						};
						List<string> list13 = new List<string>();
						for (int num145 = 0; num145 < array2.Length; num145++)
						{
							if (array2[num145] > 0)
							{
								list13.Add($"{array12[num145]}={array2[num145]}");
							}
						}
						BfsLog($"f={i} DEATH_TYPES: {string.Join(" ", list13)}");
					}
					if (_gravFDeathCounts != null && _gravFDeathFrame == i)
					{
						string[] array13 = new string[13]
						{
							"UNK", "CEIL_SPIKE", "EJECT", "CENTER", "BALL_PROBE", "BALL_VELZERO", "BALL_EJECT", "FLOOR_SPIKE", "FWD", "DEATH_COLL",
							"BOUNDS", "OOB_TOP", "OOB_BOT"
						};
						List<string> list14 = new List<string>();
						int num146 = 0;
						for (int num147 = 0; num147 < _gravFDeathCounts.Length; num147++)
						{
							if (_gravFDeathCounts[num147] > 0)
							{
								list14.Add($"{array13[num147]}={_gravFDeathCounts[num147]}");
								num146 += _gravFDeathCounts[num147];
							}
						}
						_log.WriteLine($"[GRAVF_SUMMARY] f={i} total={num146} {string.Join(" ", list14)}");
						_gravFDeathCounts = null;
						_gravFDeathSamples = 0;
					}
				}
				if (OnSpeculativePath == null || frontier.Count <= 0 || i % 5 != 0)
				{
					continue;
				}
				int num148 = Math.Min(8, frontier.Count);
				int num149 = Math.Max(1, frontier.Count / num148);
				_speculativeDepth++;
				for (int num150 = 0; num150 < frontier.Count && num150 / num149 < num148; num150 += num149)
				{
					SimState simState10 = frontier[num150];
					int hitboxW = GetHitboxW(simState10.Mini);
					SimState s6 = simState10.Clone();
					List<(int, int)> list15 = new List<(int, int)>();
					int arg = 0;
					for (int num151 = 0; num151 < 30; num151++)
					{
						int num152 = s6.X_fixed >> 8;
						int num153 = s6.Y_fixed >> 8;
						list15.Add((num152 + hitboxW / 2, num153 + 8));
						if (!StepFrame(ref s6, input: false, out var endLevel) || endLevel)
						{
							break;
						}
						arg = num151 + 1;
					}
					s6.ReturnAllSpriteResources();
					if (list15.Count >= 2)
					{
						OnSpeculativePath(list15, 0, arg, arg4: false);
					}
					SimState s7 = simState10.Clone();
					List<(int, int)> list16 = new List<(int, int)>();
					int arg2 = 0;
					for (int num154 = 0; num154 < 30; num154++)
					{
						int num155 = s7.X_fixed >> 8;
						int num156 = s7.Y_fixed >> 8;
						list16.Add((num155 + hitboxW / 2, num156 + 8));
						bool input = num154 == 0;
						if (!StepFrame(ref s7, input, out var endLevel2) || endLevel2)
						{
							break;
						}
						arg2 = num154 + 1;
					}
					s7.ReturnAllSpriteResources();
					if (list16.Count >= 2)
					{
						OnSpeculativePath(list16, 0, arg2, arg4: true);
					}
				}
				_speculativeDepth--;
			}
			_speculativeDepth = 0;
			if (num5 >= 0)
			{
				List<bool> list17 = new List<bool>();
				int num157 = num6;
				for (int num158 = num5 - 1; num158 >= 0; num158--)
				{
					list17.Add(list2[num158][num157]);
					num157 = list[num158][num157];
				}
				list17.Reverse();
				list17.Add(item);
				_log.WriteLine($"[BFS] Replaying winning path ({list17.Count} frames)...");
				ReplayBfsPath(list17, startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped, startMini);
				int count2 = allCoins.Count;
				if (PreferCoins && count2 > 0 && FinalCollectedCoinIndices != null && FinalCollectedCoinIndices.Count < count2)
				{
					List<bool> list18 = TryCoinBeamSplice(list17, startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped, startMini);
					if (list18 != null)
					{
						list17 = list18;
						ReplayBfsPath(list17, startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped, startMini);
					}
				}
				double totalSeconds = stopwatch.Elapsed.TotalSeconds;
				string text4 = $"Completed in {list17.Count} frames ({PathPoints.Count} path points)";
				if (PreferCoins && count2 > 0)
				{
					text4 += $" [{FinalCollectedCoinIndices?.Count ?? num7}/{count2} coins]";
				}
				text4 = (ResultMessage = text4 + $" [{totalSeconds:F1}s BFS]");
				Success = true;
				_log.WriteLine("[BFS] " + text4);
			}
			else
			{
				_speculativeDepth = 0;
				if (num9 >= 0 && list.Count > 0)
				{
					List<bool> list19 = new List<bool>();
					int num159 = num9;
					for (int num160 = num8; num160 >= 0; num160--)
					{
						list19.Add(list2[num160][num159]);
						num159 = list[num160][num159];
					}
					list19.Reverse();
					ReplayBfsPath(list19, startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped, startMini);
				}
				int num161 = ((PathPoints.Count > 0) ? PathPoints[PathPoints.Count - 1].x : 0);
				int value11 = ((num2 > 0) ? (num161 * 100 / num2) : 0);
				ResultMessage = $"BFS failed ~ best path to X={num161}px ({value11}%)";
				Success = false;
				_log.WriteLine("[BFS] " + ResultMessage);
				_log.WriteLine($"[BFS_DIAG] Step1 fires={_step1FireCount}, Step2 fires={_step2FireCount}");
				BfsLog("RESULT: " + ResultMessage);
				BfsLog($"DIAG: Step1 fires={_step1FireCount}, Step2 fires={_step2FireCount}");
			}
		}
		catch (Exception ex)
		{
			_speculativeDepth = 0;
			_log.WriteLine("[BFS] EXCEPTION: " + ex.GetType().Name + ": " + ex.Message);
			_log.WriteLine(ex.StackTrace);
			ResultMessage = "BFS crashed: " + ex.GetType().Name;
			Success = false;
			try
			{
				EnsureDebugPathsStamped();
				File.AppendAllText(pfDebugLogPath, $"[BFS] CRASHED: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n");
			}
			catch
			{
			}
		}
		stopwatch.Stop();
		void BfsLog(string msg)
		{
			try
			{
				EnsureDebugPathsStamped();
				File.AppendAllText(pfDebugLogPath, "[BFS] " + msg + Environment.NewLine);
			}
			catch
			{
			}
		}
	}

	private int CountBfsCoins(ref SimState s)
	{
		if (allCoins.Count == 0)
		{
			return 0;
		}
		int num = 0;
		foreach (SpriteEntry allCoin in allCoins)
		{
			if (s.ProcessedSprites.Contains(allCoin.Index))
			{
				num++;
			}
		}
		return num;
	}

	private void ReplayBfsPath(List<bool> inputSequence, int startX_px, int startY_px, int startSpeedUiIndex, int startGameMode, bool startGravFlipped, bool startMini)
	{
		int num = ComputeInitCameraY(startY_px);
		SimState s = new SimState
		{
			X_fixed = startX_px << 8,
			Y_fixed = startY_px << 8,
			VelX_fixed = SpeedUiIndexToFixed(startSpeedUiIndex),
			VelY_fixed = 0,
			GameMode = startGameMode,
			GravFlipped = startGravFlipped,
			Mini = startMini,
			GravMul = ((!startGravFlipped) ? 1 : (-1)),
			GravityMod = 1.0,
			WasZeroedByCollision = true,
			OnGround = true,
			ProcessedSprites = NewSpriteSet(),
			PendingOrbIndex = -1,
			PendingOrbSpriteId = -1,
			PendingOrbExtra1Index = -1,
			PendingOrbExtra1SpriteId = -1,
			PendingOrbExtra2Index = -1,
			PendingOrbExtra2SpriteId = -1,
			NinjaJumps = ((startGameMode == 8) ? 3 : 0),
			CameraY_fixed = num,
			TargetCameraY_fixed = num
		};
		ApplyPortalsUpTo(ref s, startX_px);
		ApplyNesIntroFreezePrestep(ref s);
		PathPoints.Clear();
		Path2Points.Clear();
		Inputs.Clear();
		_prevDualActiveForPath = false;
		_speculativeDepth = 0;
		_frameCounter = 0;
		TraceFrameOpen();
		for (int i = 0; i < inputSequence.Count; i++)
		{
			_frameCounter = i;
			bool flag = inputSequence[i];
			Inputs.Add(flag);
			PfLog($"[REPLAY f={i}] X=0x{s.X_fixed:X} ({s.X_fixed >> 8}px) Y=0x{s.Y_fixed:X} ({s.Y_fixed >> 8}px) velY=0x{s.VelY_fixed:X} mode={s.GameMode} grav={(s.GravFlipped ? "FF" : "00")} mini={(s.Mini ? 1 : 0)} inp={(flag ? 1 : 0)}");
			int gameMode = s.GameMode;
			_cubeJumpedThisStep = false;
			bool endLevel;
			bool flag2 = StepFrame(ref s, flag, out endLevel);
			if (gameMode == 0 && flag && !_cubeJumpedThisStep && s.GameMode == 0 && s.Dashing == 0)
			{
				Inputs[i] = false;
			}
			TraceFrame(i, ref s, Inputs[i], flag2);
			int num2 = (s.Mini ? 4 : 0);
			PathPoints.Add(((s.X_fixed >> 8) + 8, (s.Y_fixed >> 8) + num2 + 8));
			if (s.DualActive)
			{
				if (!_prevDualActiveForPath && Path2Points.Count > 0)
				{
					Path2Points.Add((-1, -1));
				}
				int num3 = (s.P2_Mini ? 4 : 0);
				Path2Points.Add(((s.X_fixed >> 8) + 8, (s.P2_Y_fixed >> 8) + num3 + 8));
			}
			_prevDualActiveForPath = s.DualActive;
			if (endLevel || !flag2)
			{
				break;
			}
		}
		if (PreferCoins && allCoins.Count > 0)
		{
			HashSet<int> hashSet = new HashSet<int>();
			foreach (SpriteEntry allCoin in allCoins)
			{
				if (s.ProcessedSprites.Contains(allCoin.Index))
				{
					hashSet.Add(allCoin.Index);
				}
			}
			FinalCollectedCoinIndices = hashSet;
			_log.WriteLine($"[COIN_STATUS] collected_count={hashSet.Count} total={allCoins.Count} collected_indices=[{string.Join(",", hashSet)}]");
			foreach (SpriteEntry allCoin2 in allCoins)
			{
				string value = (hashSet.Contains(allCoin2.Index) ? "YES" : "NO");
				_log.WriteLine($"[COIN_RESULT] idx={allCoin2.Index} status={value}");
			}
		}
		ExtractSkippedPads(in s);
		TraceFrameClose();
	}

	private List<bool> TryCoinBeamSplice(List<bool> originalInputs, int startX_px, int startY_px, int startSpeedUiIndex, int startGameMode, bool startGravFlipped, bool startMini)
	{
		List<SpriteEntry> list = new List<SpriteEntry>();
		foreach (SpriteEntry allCoin in allCoins)
		{
			if (!FinalCollectedCoinIndices.Contains(allCoin.Index))
			{
				list.Add(allCoin);
			}
		}
		if (list.Count == 0)
		{
			return null;
		}
		_log.WriteLine($"[COIN_SPLICE] Attempting beam splice for {list.Count} missed coin(s)");
		SimState s = new SimState
		{
			X_fixed = startX_px << 8,
			Y_fixed = startY_px << 8,
			VelX_fixed = SpeedUiIndexToFixed(startSpeedUiIndex),
			VelY_fixed = 0,
			GameMode = startGameMode,
			GravFlipped = startGravFlipped,
			Mini = startMini,
			GravMul = ((!startGravFlipped) ? 1 : (-1)),
			GravityMod = 1.0,
			WasZeroedByCollision = true,
			OnGround = true,
			ProcessedSprites = NewSpriteSet(),
			PendingOrbIndex = -1,
			PendingOrbSpriteId = -1,
			PendingOrbExtra1Index = -1,
			PendingOrbExtra1SpriteId = -1,
			PendingOrbExtra2Index = -1,
			PendingOrbExtra2SpriteId = -1,
			NinjaJumps = ((startGameMode == 8) ? 3 : 0),
			CameraY_fixed = ComputeInitCameraY(startY_px),
			TargetCameraY_fixed = ComputeInitCameraY(startY_px)
		};
		ApplyPortalsUpTo(ref s, startX_px);
		ApplyNesIntroFreezePrestep(ref s);
		foreach (SpriteEntry item3 in list)
		{
			int num = (item3.HitLeft + item3.HitRight) / 2;
			int num2 = (item3.HitTop + item3.HitBottom) / 2;
			int num3 = num - 2000;
			int num4 = item3.HitRight + 100;
			SimState s2 = s.Clone();
			int num5 = -1;
			SimState item = default(SimState);
			_speculativeDepth = 1;
			for (int i = 0; i < originalInputs.Count; i++)
			{
				_frameCounter = i;
				int num6 = s2.X_fixed >> 8;
				if (num6 >= num3 && num5 < 0)
				{
					if (s2.GameMode != 1 && s2.GameMode != 3 && s2.GameMode != 6 && s2.GameMode != 7 && s2.GameMode != 10)
					{
						_log.WriteLine($"[COIN_SPLICE] Coin idx={item3.Index} at X={num}: mode={s2.GameMode} is not continuous-Y, skipping");
					}
					else
					{
						num5 = i;
						item = s2.Clone();
						_log.WriteLine($"[COIN_SPLICE] Coin idx={item3.Index}: splice at f={i} X={num6} Y={s2.Y_fixed >> 8} mode={s2.GameMode}");
					}
					break;
				}
				bool endLevel;
				bool flag = StepFrame(ref s2, originalInputs[i], out endLevel);
				if (!flag || endLevel)
				{
					break;
				}
			}
			_speculativeDepth = 0;
			if (num5 < 0)
			{
				continue;
			}
			int val = 8192;
			int num7 = 800;
			List<(int[], bool[])> list2 = new List<(int[], bool[])>();
			List<SimState> list3 = new List<SimState> { item };
			int num8 = -1;
			int num9 = -1;
			for (int j = 0; j < num7; j++)
			{
				if (list3.Count <= 0)
				{
					break;
				}
				_frameCounter = num5 + j;
				_speculativeDepth = 1;
				List<SimState> list4 = new List<SimState>();
				List<int> list5 = new List<int>();
				List<bool> list6 = new List<bool>();
				List<int> nextScores = new List<int>();
				for (int k = 0; k < list3.Count; k++)
				{
					for (int l = 0; l <= 1; l++)
					{
						SimState s3 = list3[k].Clone();
						if (!StepFrame(ref s3, l == 1, out var endLevel2))
						{
							s3.ReturnAllSpriteResources();
						}
						else if (endLevel2)
						{
							if (s3.ProcessedSprites.Contains(item3.Index))
							{
								List<bool> list7 = new List<bool> { l == 1 };
								int num10 = k;
								for (int num11 = list2.Count - 1; num11 >= 0; num11--)
								{
									list7.Add(list2[num11].Item2[num10]);
									num10 = list2[num11].Item1[num10];
								}
								list7.Reverse();
								List<bool> result = SpliceInputs(originalInputs, num5, list7);
								_log.WriteLine($"[COIN_SPLICE] Beam reached level end with coin! splice_f={num5} beam_len={list7.Count}");
								_speculativeDepth = 0;
								foreach (SimState item4 in list3)
								{
									item4.ReturnAllSpriteResources();
								}
								foreach (SimState item5 in list4)
								{
									item5.ReturnAllSpriteResources();
								}
								return result;
							}
							s3.ReturnAllSpriteResources();
						}
						else
						{
							int num12 = s3.X_fixed >> 8;
							int num13 = s3.Y_fixed >> 8;
							int item2;
							if (s3.ProcessedSprites.Contains(item3.Index))
							{
								item2 = -1000000;
							}
							else
							{
								int num14 = Math.Abs(num13 - num2);
								int num15 = num12 - num;
								item2 = ((num15 <= 100) ? (num14 * 10) : (500000 + num15));
							}
							list4.Add(s3);
							list5.Add(k);
							list6.Add(l == 1);
							nextScores.Add(item2);
						}
					}
				}
				foreach (SimState item6 in list3)
				{
					item6.ReturnAllSpriteResources();
				}
				if (list4.Count == 0)
				{
					break;
				}
				int[] array = Enumerable.Range(0, list4.Count).ToArray();
				Array.Sort(array, (int a, int b) => nextScores[a].CompareTo(nextScores[b]));
				List<SimState> list8 = new List<SimState>(Math.Min(val, array.Length));
				int[] array2 = new int[Math.Min(val, array.Length)];
				bool[] array3 = new bool[Math.Min(val, array.Length)];
				for (int num16 = 0; num16 < array2.Length; num16++)
				{
					int index = array[num16];
					list8.Add(list4[index]);
					array2[num16] = list5[index];
					array3[num16] = list6[index];
				}
				for (int num17 = array2.Length; num17 < array.Length; num17++)
				{
					list4[array[num17]].ReturnAllSpriteResources();
				}
				list2.Add((array2, array3));
				list3 = list8;
				if (list3[0].ProcessedSprites.Contains(item3.Index))
				{
					int num18 = list3[0].X_fixed >> 8;
					if (num18 > num4)
					{
						num9 = 0;
						num8 = j;
						_log.WriteLine($"[COIN_SPLICE] Beam collected coin at bf={j} X={num18} Y={list3[0].Y_fixed >> 8}");
						break;
					}
				}
				if (j % 20 != 0)
				{
					continue;
				}
				int num19 = int.MaxValue;
				int num20 = int.MinValue;
				int num21 = 0;
				foreach (SimState item7 in list3)
				{
					int num22 = item7.Y_fixed >> 8;
					if (num22 < num19)
					{
						num19 = num22;
					}
					if (num22 > num20)
					{
						num20 = num22;
					}
					if (item7.ProcessedSprites.Contains(item3.Index))
					{
						num21++;
					}
				}
				_log.WriteLine($"[COIN_SPLICE] bf={j} beam={list3.Count} Y=[{num19}..{num20}] collected={num21} X~{list3[0].X_fixed >> 8}");
			}
			_speculativeDepth = 0;
			if (num9 < 0)
			{
				_log.WriteLine($"[COIN_SPLICE] Beam failed to collect coin idx={item3.Index}");
				foreach (SimState item8 in list3)
				{
					item8.ReturnAllSpriteResources();
				}
				continue;
			}
			List<bool> list9 = new List<bool>();
			int num23 = num9;
			for (int num24 = list2.Count - 1; num24 >= 0; num24--)
			{
				list9.Add(list2[num24].Item2[num23]);
				num23 = list2[num24].Item1[num23];
			}
			list9.Reverse();
			SimState startState = list3[num9];
			int num25 = num5 + list9.Count;
			_log.WriteLine($"[COIN_SPLICE] Trying tail with original inputs from f={num25} (total={originalInputs.Count})");
			bool flag2 = false;
			SimState s4 = startState.Clone();
			_speculativeDepth = 1;
			bool flag3 = false;
			for (int num26 = num25; num26 < originalInputs.Count; num26++)
			{
				_frameCounter = num26;
				bool endLevel3;
				bool flag4 = StepFrame(ref s4, originalInputs[num26], out endLevel3);
				if (endLevel3)
				{
					flag3 = true;
					break;
				}
				if (!flag4)
				{
					_log.WriteLine($"[COIN_SPLICE] Tail died at f={num26} (df={num26 - num25}) X={s4.X_fixed >> 8} Y={s4.Y_fixed >> 8} dt={s4.DeathType}");
					break;
				}
			}
			_speculativeDepth = 0;
			if (flag3)
			{
				flag2 = true;
				_log.WriteLine("[COIN_SPLICE] Tail reached level end! Splicing...");
			}
			s4.ReturnAllSpriteResources();
			if (!flag2)
			{
				_log.WriteLine($"[COIN_SPLICE] Trying survival beam from f={num25}...");
				List<bool> list10 = RunSurvivalBeam(startState, num25, originalInputs.Count + 3000);
				if (list10 != null)
				{
					_log.WriteLine($"[COIN_SPLICE] Survival beam succeeded! len={list10.Count}");
					List<bool> list11 = new List<bool>(num5 + list9.Count + list10.Count);
					for (int num27 = 0; num27 < num5; num27++)
					{
						list11.Add(originalInputs[num27]);
					}
					list11.AddRange(list9);
					list11.AddRange(list10);
					foreach (SimState item9 in list3)
					{
						item9.ReturnAllSpriteResources();
					}
					return list11;
				}
				_log.WriteLine("[COIN_SPLICE] Survival beam also failed");
			}
			foreach (SimState item10 in list3)
			{
				item10.ReturnAllSpriteResources();
			}
			if (!flag2)
			{
				continue;
			}
			return SpliceInputs(originalInputs, num5, list9);
		}
		return null;
	}

	private List<bool> SpliceInputs(List<bool> original, int spliceFrame, List<bool> beamInputs)
	{
		List<bool> list = new List<bool>(original.Count);
		for (int i = 0; i < spliceFrame; i++)
		{
			list.Add(original[i]);
		}
		list.AddRange(beamInputs);
		int num = spliceFrame + beamInputs.Count;
		for (int j = num; j < original.Count; j++)
		{
			list.Add(original[j]);
		}
		return list;
	}

	private List<bool> RunSurvivalBeam(SimState startState, int startFrame, int maxFrames)
	{
		int val = 2048;
		List<SimState> list = new List<SimState> { startState.Clone() };
		List<(int[], bool[])> list2 = new List<(int[], bool[])>();
		for (int i = 0; i < maxFrames; i++)
		{
			if (list.Count <= 0)
			{
				break;
			}
			_frameCounter = startFrame + i;
			_speculativeDepth = 1;
			List<SimState> list3 = new List<SimState>();
			List<int> list4 = new List<int>();
			List<bool> list5 = new List<bool>();
			List<int> nextScores = new List<int>();
			for (int j = 0; j < list.Count; j++)
			{
				for (int k = 0; k <= 1; k++)
				{
					SimState s = list[j].Clone();
					if (!StepFrame(ref s, k == 1, out var endLevel))
					{
						s.ReturnAllSpriteResources();
						continue;
					}
					if (endLevel)
					{
						List<bool> list6 = new List<bool> { k == 1 };
						int num = j;
						for (int num2 = list2.Count - 1; num2 >= 0; num2--)
						{
							list6.Add(list2[num2].Item2[num]);
							num = list2[num2].Item1[num];
						}
						list6.Reverse();
						_speculativeDepth = 0;
						foreach (SimState item2 in list)
						{
							item2.ReturnAllSpriteResources();
						}
						foreach (SimState item3 in list3)
						{
							item3.ReturnAllSpriteResources();
						}
						return list6;
					}
					int item = -(s.X_fixed >> 8);
					list3.Add(s);
					list4.Add(j);
					list5.Add(k == 1);
					nextScores.Add(item);
				}
			}
			foreach (SimState item4 in list)
			{
				item4.ReturnAllSpriteResources();
			}
			if (list3.Count == 0)
			{
				break;
			}
			int[] array = Enumerable.Range(0, list3.Count).ToArray();
			Array.Sort(array, (int a, int b) => nextScores[a].CompareTo(nextScores[b]));
			int num3 = Math.Min(val, array.Length);
			List<SimState> list7 = new List<SimState>(num3);
			int[] array2 = new int[num3];
			bool[] array3 = new bool[num3];
			for (int num4 = 0; num4 < num3; num4++)
			{
				int index = array[num4];
				list7.Add(list3[index]);
				array2[num4] = list4[index];
				array3[num4] = list5[index];
			}
			for (int num5 = num3; num5 < array.Length; num5++)
			{
				list3[array[num5]].ReturnAllSpriteResources();
			}
			list2.Add((array2, array3));
			list = list7;
		}
		foreach (SimState item5 in list)
		{
			item5.ReturnAllSpriteResources();
		}
		_speculativeDepth = 0;
		return null;
	}

	private void SnapshotBestPath()
	{
		if (_speculativeDepth <= 0 && PathPoints.Count != 0)
		{
			(int, int) tuple = PathPoints[PathPoints.Count - 1];
			if (tuple.Item1 > _bestPathHighWaterX)
			{
				_bestPathHighWaterX = tuple.Item1;
				_bestPathPoints = new List<(int, int)>(PathPoints);
				_bestPath2Points = new List<(int, int)>(Path2Points);
				_bestInputs = new List<bool>(Inputs);
			}
		}
	}

	private void UseBestPathIfBetter()
	{
		if (_bestPathPoints.Count != 0)
		{
			int num = ((PathPoints.Count > 0) ? PathPoints[PathPoints.Count - 1].x : 0);
			if (_bestPathHighWaterX > num)
			{
				PathPoints.Clear();
				PathPoints.AddRange(_bestPathPoints);
				Path2Points.Clear();
				Path2Points.AddRange(_bestPath2Points);
				Inputs.Clear();
				Inputs.AddRange(_bestInputs);
			}
		}
	}

	private bool TryBacktrack(ref SimState state, ref int frame)
	{
		if (!_backtrackActive)
		{
			_btDeathFrame = frame;
			_btOrigDeathReason = _lastDeathReason;
			_btOrigDeathY = _lastDeathY;
			_backtrackTimer = Stopwatch.StartNew();
		}
		_backtrackActive = true;
		int gameMode = state.GameMode;
		bool flag = _missedCoinIdx >= 0 && _lastDeathReason == "MISSED_COIN";
		while (_backtrackCheckpoints.Count > 0 && _backtrackAttempts < (flag ? 1500 : 500) && _totalBacktrackAttempts < 5000 && (_backtrackTimer == null || _backtrackTimer.Elapsed.TotalSeconds < (double)(flag ? 120 : 60)))
		{
			int num = _backtrackCheckpoints.Count - 1;
			BacktrackCheckpoint backtrackCheckpoint = _backtrackCheckpoints[num];
			if (backtrackCheckpoint.GameMode != gameMode)
			{
				int num2 = _btDeathFrame - backtrackCheckpoint.Frame;
				int num3 = (flag ? 2000 : 500);
				if (num2 > num3)
				{
					_backtrackCheckpoints.RemoveAt(num);
					continue;
				}
				if (flag && backtrackCheckpoint.RetryStage == 0)
				{
					_log.WriteLine($"[CROSSMODE_BT] cpMode={backtrackCheckpoint.GameMode} deathMode={gameMode} frameDist={num2} attempts={_backtrackAttempts}");
				}
			}
			if (flag && backtrackCheckpoint.GameMode == gameMode && (gameMode == 1 || gameMode == 3))
			{
				_coinMissRetryCount.TryGetValue(allCoins[_missedCoinIdx].Index, out var value);
				if (value >= 3)
				{
					int num4 = _btDeathFrame - backtrackCheckpoint.Frame;
					if (num4 < 200)
					{
						_backtrackCheckpoints.RemoveAt(num);
						continue;
					}
				}
			}
			backtrackCheckpoint.RetryStage++;
			int num6;
			if (backtrackCheckpoint.GameMode == 1 || backtrackCheckpoint.GameMode == 3)
			{
				if (flag)
				{
					int num5 = _btDeathFrame - backtrackCheckpoint.Frame;
					num6 = ((num5 < 150) ? 2 : ((num5 < 300) ? 4 : ((num5 >= 700) ? 4 : 23)));
				}
				else
				{
					int num7 = _btDeathFrame - backtrackCheckpoint.Frame;
					num6 = ((num7 <= 60) ? 23 : ((num7 > 240) ? 4 : 8));
				}
			}
			else if (backtrackCheckpoint.GameMode == 2 || backtrackCheckpoint.GameMode == 5 || backtrackCheckpoint.GameMode == 7)
			{
				num6 = 23;
			}
			else
			{
				int num8 = _btDeathFrame - backtrackCheckpoint.Frame;
				int num9 = backtrackCheckpoint.State.Y_fixed >> 8;
				if (_btOrigDeathReason == "FWD_DEATH" && Math.Abs(num9 - _btOrigDeathY) < 12 && num8 > 30)
				{
					num6 = 1;
				}
				else if (!flag)
				{
					num6 = ((num8 <= 60) ? 23 : ((num8 > 240) ? 4 : 8));
				}
				else
				{
					SpriteEntry spriteEntry = allCoins[_missedCoinIdx];
					int num10 = (spriteEntry.HitTop + spriteEntry.HitBottom) / 2;
					num6 = ((num9 - num10 > 40 && num8 <= 90) ? 4 : ((num8 > 60) ? 23 : 23));
				}
			}
			if (backtrackCheckpoint.RetryStage > num6)
			{
				_backtrackCheckpoints.RemoveAt(num);
				continue;
			}
			state = backtrackCheckpoint.State.Clone();
			if (PreferCoins && _permanentlyCollectedCoins.Count > 0)
			{
				List<int> list = new List<int>();
				foreach (KeyValuePair<int, int> permanentlyCollectedCoin in _permanentlyCollectedCoins)
				{
					if (permanentlyCollectedCoin.Value >= backtrackCheckpoint.Frame)
					{
						list.Add(permanentlyCollectedCoin.Key);
					}
				}
				foreach (int item in list)
				{
					_permanentlyCollectedCoins.Remove(item);
					_coinCollectThenLoseCount.TryGetValue(item, out var value2);
					value2++;
					_coinCollectThenLoseCount[item] = value2;
					if (value2 < 5 || _forgivenCoins.Contains(item))
					{
						continue;
					}
					_forgivenCoins.Add(item);
					_autoForgivenCoins.Add(item);
					for (int i = 0; i < allCoins.Count; i++)
					{
						if (allCoins[i].Index == item)
						{
							_forgivenCoinGameModes[item] = state.GameMode;
							break;
						}
					}
					_log.WriteLine($"[COIN_COLLECT_LOSE_FORGIVEN] idx={item} gm={state.GameMode} loseCount={value2}");
					PfLog($"[COIN_COLLECT_LOSE_FORGIVEN] idx={item} loseCount={value2} \ufffd collected {5}x but path always dies");
				}
				if (gameMode == 1 && _shipEntryRecoveryCheckpoint != null)
				{
					bool flag2 = false;
					foreach (int item2 in list)
					{
						if (!_forgivenCoins.Contains(item2) && _coinCollectThenLoseCount.TryGetValue(item2, out var value3) && value3 > 0 && _shipEntryRecoveryCheckpoint.Frame < backtrackCheckpoint.Frame - 50)
						{
							flag2 = true;
							break;
						}
					}
					if (flag2)
					{
						BacktrackCheckpoint shipEntryRecoveryCheckpoint = _shipEntryRecoveryCheckpoint;
						BacktrackCheckpoint backtrackCheckpoint2 = new BacktrackCheckpoint
						{
							Frame = shipEntryRecoveryCheckpoint.Frame,
							State = shipEntryRecoveryCheckpoint.State.Clone(),
							HoldJumpState = shipEntryRecoveryCheckpoint.HoldJumpState,
							HoldDelayState = shipEntryRecoveryCheckpoint.HoldDelayState,
							CommittedDelayState = shipEntryRecoveryCheckpoint.CommittedDelayState,
							PathPointCount = shipEntryRecoveryCheckpoint.PathPointCount,
							InputCount = shipEntryRecoveryCheckpoint.InputCount,
							RetryStage = 0,
							UsedBias = shipEntryRecoveryCheckpoint.UsedBias,
							ShipBias = shipEntryRecoveryCheckpoint.ShipBias,
							GameMode = shipEntryRecoveryCheckpoint.GameMode,
							ShipForceHold = shipEntryRecoveryCheckpoint.ShipForceHold,
							ShipForceRelease = shipEntryRecoveryCheckpoint.ShipForceRelease,
							ShipCommitFrames = shipEntryRecoveryCheckpoint.ShipCommitFrames,
							ShipCommitHold = shipEntryRecoveryCheckpoint.ShipCommitHold,
							ForceJumpRemaining = shipEntryRecoveryCheckpoint.ForceJumpRemaining,
							SkipAllOrbs = shipEntryRecoveryCheckpoint.SkipAllOrbs,
							SkipSpecificOrbs = new HashSet<int>(shipEntryRecoveryCheckpoint.SkipSpecificOrbs ?? new HashSet<int>()),
							SkipSpecificPads = new HashSet<int>(shipEntryRecoveryCheckpoint.SkipSpecificPads ?? new HashSet<int>()),
							PrevFrameWasGrounded = shipEntryRecoveryCheckpoint.PrevFrameWasGrounded,
							NextCoinCheckIdx = shipEntryRecoveryCheckpoint.NextCoinCheckIdx
						};
						_backtrackCheckpoints.Clear();
						_backtrackCheckpoints.Add(backtrackCheckpoint2);
						_backtrackAttempts = 0;
						_log.WriteLine($"[COIN_DEEP_RECOVERY] injecting ship entry frame={backtrackCheckpoint2.Frame} mode={backtrackCheckpoint2.GameMode}");
						return TryBacktrack(ref state, ref frame);
					}
				}
				foreach (KeyValuePair<int, int> permanentlyCollectedCoin2 in _permanentlyCollectedCoins)
				{
					state.ProcessedSprites.Add(permanentlyCollectedCoin2.Key);
				}
			}
			_cubeHoldJump = backtrackCheckpoint.HoldJumpState;
			_cubeHoldDelay = backtrackCheckpoint.HoldDelayState;
			_committedJumpDelay = backtrackCheckpoint.CommittedDelayState;
			_committedRobotHold = 0;
			_committedNinjaJumps.Clear();
			_ninjaWaitFrames = 0;
			JumpTimingBias = backtrackCheckpoint.UsedBias;
			_shipCorridorBias = backtrackCheckpoint.ShipBias;
			_shipForceHoldFrames = backtrackCheckpoint.ShipForceHold;
			_shipForceReleaseFrames = backtrackCheckpoint.ShipForceRelease;
			_shipCommitFrames = backtrackCheckpoint.ShipCommitFrames;
			_shipCommitHold = backtrackCheckpoint.ShipCommitHold;
			_btForceJumpFramesRemaining = backtrackCheckpoint.ForceJumpRemaining;
			_btSkipAllOrbs = backtrackCheckpoint.SkipAllOrbs;
			_btSkipSpecificOrbs = new HashSet<int>(backtrackCheckpoint.SkipSpecificOrbs ?? new HashSet<int>());
			_btSkipSpecificPads = new HashSet<int>(backtrackCheckpoint.SkipSpecificPads ?? new HashSet<int>());
			_prevFrameWasGrounded = backtrackCheckpoint.PrevFrameWasGrounded;
			_nextCoinCheckIdx = backtrackCheckpoint.NextCoinCheckIdx;
			_coinInputScript.Clear();
			_coinInputScriptCoinIdx = -1;
			_beamSearchAttemptedCoinIdx = -1;
			_beamSearchLastDistX = int.MaxValue;
			if (PathPoints.Count > backtrackCheckpoint.PathPointCount && AttemptedPaths.Count < 200)
			{
				List<(int, int)> range = PathPoints.GetRange(backtrackCheckpoint.PathPointCount, PathPoints.Count - backtrackCheckpoint.PathPointCount);
				AttemptedPaths.Add(range);
			}
			if (PathPoints.Count > backtrackCheckpoint.PathPointCount)
			{
				PathPoints.RemoveRange(backtrackCheckpoint.PathPointCount, PathPoints.Count - backtrackCheckpoint.PathPointCount);
			}
			if (Inputs.Count > backtrackCheckpoint.InputCount)
			{
				Inputs.RemoveRange(backtrackCheckpoint.InputCount, Inputs.Count - backtrackCheckpoint.InputCount);
			}
			while (_backtrackCheckpoints.Count > num + 1)
			{
				_backtrackCheckpoints.RemoveAt(_backtrackCheckpoints.Count - 1);
			}
			_btOverrideFrame = backtrackCheckpoint.Frame;
			_btOverrideStage = backtrackCheckpoint.RetryStage;
			_btOverrideDistFromDeath = _btDeathFrame - backtrackCheckpoint.Frame;
			_btSuppressJumpUntilAirborne = false;
			frame = backtrackCheckpoint.Frame - 1;
			_backtrackAttempts++;
			_totalBacktrackAttempts++;
			_frameCounter = backtrackCheckpoint.Frame;
			PfLog($"[BACKTRACK] attempt={_backtrackAttempts}/{500} total={_totalBacktrackAttempts}/{5000} rewind to frame={backtrackCheckpoint.Frame} stage={backtrackCheckpoint.RetryStage} remaining_checkpoints={_backtrackCheckpoints.Count} holdWas={backtrackCheckpoint.HoldJumpState}");
			return true;
		}
		if (_lastCubeToShipCheckpoint != null && flag)
		{
			BacktrackCheckpoint lastCubeToShipCheckpoint = _lastCubeToShipCheckpoint;
			_lastCubeToShipCheckpoint = null;
			lastCubeToShipCheckpoint.RetryStage = 0;
			_backtrackCheckpoints.Clear();
			_backtrackCheckpoints.Add(lastCubeToShipCheckpoint);
			_backtrackAttempts = 0;
			_log.WriteLine($"[CROSSMODE_INJECT] frame={lastCubeToShipCheckpoint.Frame} mode={lastCubeToShipCheckpoint.GameMode} attempts={_backtrackAttempts}");
			return TryBacktrack(ref state, ref frame);
		}
		_backtrackActive = false;
		return false;
	}

	private bool DecideInput(SimState state)
	{
		bool flag = _btOverrideFrame == _frameCounter && _speculativeDepth == 0;
		if (flag)
		{
			int btOverrideStage = _btOverrideStage;
			_btOverrideFrame = -1;
			if (state.GameMode == 1)
			{
				int btOverrideDistFromDeath = _btOverrideDistFromDeath;
				int num = Math.Max(4, btOverrideDistFromDeath / 3);
				int num2 = Math.Max(24, Math.Min(72, btOverrideDistFromDeath));
				_shipForceReleaseFirstFrames = 0;
				_shipForceHoldFrames = 0;
				_shipForceReleaseFrames = 0;
				if (_missedCoinIdx >= 0 && _btOrigDeathReason == "MISSED_COIN" && _missedCoinIdx < allCoins.Count)
				{
					_shipCoinAggressiveThreshold = 20;
					SpriteEntry spriteEntry = allCoins[_missedCoinIdx];
					int num3 = (spriteEntry.HitTop + spriteEntry.HitBottom) / 2;
					int num4 = FindCorridorCenter(ref state);
					int num5 = num3 - num4;
					int num6 = ((num5 >= 0) ? 1 : (-1));
					int num7 = Math.Max(6, btOverrideDistFromDeath / 2);
					switch (btOverrideStage)
					{
					case 1:
						_shipCorridorBias = num6 * 60;
						break;
					case 2:
						if (num6 > 0)
						{
							_shipForceReleaseFrames = num7;
						}
						else
						{
							_shipForceHoldFrames = num7;
						}
						_shipCorridorBias = num6 * 120;
						break;
					case 3:
						_shipCorridorBias = num6 * 160;
						break;
					case 4:
						if (num6 > 0)
						{
							_shipForceReleaseFrames = num7 * 2;
						}
						else
						{
							_shipForceHoldFrames = num7 * 2;
						}
						_shipCorridorBias = num6 * 200;
						break;
					case 5:
						_shipCorridorBias = -num6 * 60;
						break;
					case 6:
						if (num6 > 0)
						{
							_shipForceHoldFrames = num7;
						}
						else
						{
							_shipForceReleaseFrames = num7;
						}
						_shipCorridorBias = -num6 * 120;
						break;
					case 7:
						if (num6 > 0)
						{
							_shipForceReleaseFrames = num7 * 3;
						}
						else
						{
							_shipForceHoldFrames = num7 * 3;
						}
						_shipCorridorBias = num6 * 250;
						break;
					case 8:
						_shipCorridorBias = num6 * 300;
						break;
					case 9:
						if (num6 > 0)
						{
							_shipForceReleaseFirstFrames = num7;
							_shipForceHoldFrames = num7;
						}
						else
						{
							_shipForceHoldFrames = num7;
							_shipForceReleaseFrames = num7;
						}
						_shipCorridorBias = num6 * 100;
						break;
					case 10:
						if (num6 > 0)
						{
							_shipForceReleaseFirstFrames = num7 * 2;
							_shipForceHoldFrames = num7;
						}
						else
						{
							_shipForceHoldFrames = num7 * 2;
							_shipForceReleaseFrames = num7;
						}
						_shipCorridorBias = num6 * 150;
						break;
					case 11:
						if (num6 > 0)
						{
							_shipForceReleaseFirstFrames = num7 * 2;
							_shipForceHoldFrames = num7 * 2;
						}
						else
						{
							_shipForceHoldFrames = num7 * 2;
							_shipForceReleaseFrames = num7 * 2;
						}
						_shipCorridorBias = num6 * 200;
						break;
					case 12:
						if (num6 > 0)
						{
							_shipForceReleaseFirstFrames = num7 * 3;
							_shipForceHoldFrames = num7;
						}
						else
						{
							_shipForceHoldFrames = num7 * 3;
							_shipForceReleaseFrames = num7;
						}
						_shipCorridorBias = num6 * 250;
						break;
					case 13:
						if (num6 > 0)
						{
							_shipForceReleaseFirstFrames = num7 * 3;
							_shipForceHoldFrames = num7 * 3;
						}
						else
						{
							_shipForceHoldFrames = num7 * 3;
							_shipForceReleaseFrames = num7 * 3;
						}
						_shipCorridorBias = num6 * 300;
						break;
					case 14:
						if (num6 > 0)
						{
							_shipForceReleaseFirstFrames = num7;
							_shipForceHoldFrames = num7 * 2;
						}
						else
						{
							_shipForceHoldFrames = num7;
							_shipForceReleaseFrames = num7 * 2;
						}
						_shipCorridorBias = num6 * 80;
						break;
					case 15:
						if (num6 > 0)
						{
							_shipForceReleaseFirstFrames = num7 * 4;
							_shipForceHoldFrames = num7 * 2;
						}
						else
						{
							_shipForceHoldFrames = num7 * 4;
							_shipForceReleaseFrames = num7 * 2;
						}
						_shipCorridorBias = num6 * 350;
						break;
					case 16:
						if (num6 > 0)
						{
							_shipForceHoldFrames = num7 * 2;
							_shipForceReleaseFrames = num7 * 2;
						}
						else
						{
							_shipForceReleaseFirstFrames = num7 * 2;
							_shipForceHoldFrames = num7 * 2;
						}
						_shipCorridorBias = -num6 * 200;
						break;
					}
				}
				else
				{
					int num8 = Math.Max(6, btOverrideDistFromDeath / 2);
					switch (btOverrideStage)
					{
					case 1:
						_shipCorridorBias = -num2;
						break;
					case 2:
						_shipCorridorBias = num2;
						break;
					case 3:
						_shipForceHoldFrames = num;
						break;
					case 4:
						_shipForceReleaseFrames = num;
						break;
					case 5:
						_shipCorridorBias = -120;
						_shipForceHoldFrames = num8;
						break;
					case 6:
						_shipCorridorBias = 120;
						_shipForceReleaseFrames = num8;
						break;
					case 7:
						_shipCorridorBias = -200;
						_shipForceHoldFrames = num8 * 2;
						break;
					case 8:
						_shipCorridorBias = 200;
						_shipForceReleaseFrames = num8 * 2;
						break;
					case 9:
						_shipForceReleaseFirstFrames = num8;
						_shipForceHoldFrames = num8;
						_shipCorridorBias = -150;
						break;
					case 10:
						_shipForceHoldFrames = num8;
						_shipForceReleaseFrames = num8;
						_shipCorridorBias = 150;
						break;
					case 11:
						_shipCorridorBias = -300;
						_shipForceHoldFrames = num8 * 3;
						break;
					case 12:
						_shipCorridorBias = 300;
						_shipForceReleaseFrames = num8 * 3;
						break;
					}
				}
				PfLog($"[BACKTRACK_OVERRIDE_SHIP] stage={btOverrideStage} dist={btOverrideDistFromDeath}: bias={_shipCorridorBias} forceRelFirst={_shipForceReleaseFirstFrames} forceHold={_shipForceHoldFrames} forceRel={_shipForceReleaseFrames} coinRetry={_missedCoinIdx >= 0}");
				return DecideShipInput(state);
			}
			if (state.GameMode == 2)
			{
				PfLog($"[BACKTRACK_OVERRIDE_BALL] stage={btOverrideStage}");
				switch (btOverrideStage)
				{
				case 1:
					_btSuppressJumpUntilAirborne = true;
					return false;
				case 2:
					return true;
				case 3:
					JumpTimingBias = 1.0 - JumpTimingBias;
					break;
				case 4:
					JumpTimingBias = 1.0 - JumpTimingBias;
					_btSuppressJumpUntilAirborne = true;
					return false;
				case 5:
					JumpTimingBias = 1.0 - JumpTimingBias;
					return true;
				case 6:
					_committedJumpDelay = 5;
					return false;
				case 7:
					_committedJumpDelay = 10;
					return false;
				case 8:
					_committedJumpDelay = 15;
					return false;
				case 9:
					_committedJumpDelay = 20;
					return false;
				case 10:
					_committedJumpDelay = 25;
					return false;
				}
				return DecideBallInput(state, isOverrideFrame: true);
			}
			if (state.GameMode == 5)
			{
				switch (btOverrideStage)
				{
				case 1:
					_btSuppressJumpUntilAirborne = true;
					return false;
				case 2:
					return true;
				case 3:
					JumpTimingBias = 1.0 - JumpTimingBias;
					break;
				case 4:
					JumpTimingBias = 1.0 - JumpTimingBias;
					_btSuppressJumpUntilAirborne = true;
					return false;
				case 5:
					JumpTimingBias = 1.0 - JumpTimingBias;
					return true;
				case 6:
					_committedJumpDelay = 5;
					return false;
				case 7:
					_committedJumpDelay = 10;
					return false;
				case 8:
					_committedJumpDelay = 15;
					return false;
				case 9:
					_committedJumpDelay = 20;
					return false;
				case 10:
					_committedJumpDelay = 25;
					return false;
				}
				return DecideSpiderInput(state, isOverrideFrame: true);
			}
			if (state.GameMode == 7)
			{
				switch (btOverrideStage)
				{
				case 1:
					_btSuppressJumpUntilAirborne = true;
					return false;
				case 2:
					return true;
				case 3:
					JumpTimingBias = 1.0 - JumpTimingBias;
					break;
				case 4:
					JumpTimingBias = 1.0 - JumpTimingBias;
					_btSuppressJumpUntilAirborne = true;
					return false;
				case 5:
					JumpTimingBias = 1.0 - JumpTimingBias;
					return true;
				case 6:
					_committedJumpDelay = 5;
					return false;
				case 7:
					_committedJumpDelay = 10;
					return false;
				case 8:
					_committedJumpDelay = 15;
					return false;
				case 9:
					_committedJumpDelay = 20;
					return false;
				case 10:
					_committedJumpDelay = 25;
					return false;
				}
				return DecideSwingInput(state, isOverrideFrame: true);
			}
			switch (btOverrideStage)
			{
			case 1:
				_cubeHoldJump = false;
				_cubeHoldDelay = 0;
				_btSuppressJumpUntilAirborne = true;
				PfLog("[BACKTRACK_OVERRIDE] stage=1: forcing no-jump (suppress until airborne)");
				return false;
			case 2:
				PfLog("[BACKTRACK_OVERRIDE] stage=2: force jump now (alternate)");
				return true;
			case 3:
			{
				double jumpTimingBias = ((JumpTimingBias < 0.5) ? 1.0 : 0.0);
				JumpTimingBias = jumpTimingBias;
				PfLog($"[BACKTRACK_OVERRIDE] stage=3: flipped bias to {JumpTimingBias:F2}");
				break;
			}
			case 4:
				PfLog("[BACKTRACK_OVERRIDE] stage=4: force jump now");
				return true;
			case 5:
				JumpTimingBias = 1.0 - JumpTimingBias;
				PfLog($"[BACKTRACK_OVERRIDE] stage=5: force jump + flipped bias to {JumpTimingBias:F2}");
				return true;
			case 6:
				JumpTimingBias = 1.0 - JumpTimingBias;
				_btSuppressJumpUntilAirborne = true;
				PfLog($"[BACKTRACK_OVERRIDE] stage=6: suppress + flipped bias to {JumpTimingBias:F2}");
				return false;
			case 7:
				_committedJumpDelay = 5;
				PfLog("[BACKTRACK_OVERRIDE] stage=7: committed delay=5");
				return false;
			case 8:
				_committedJumpDelay = 10;
				PfLog("[BACKTRACK_OVERRIDE] stage=8: committed delay=10");
				return false;
			case 9:
				_committedJumpDelay = 15;
				PfLog("[BACKTRACK_OVERRIDE] stage=9: committed delay=15");
				return false;
			case 10:
				_committedJumpDelay = 20;
				PfLog("[BACKTRACK_OVERRIDE] stage=10: committed delay=20");
				return false;
			case 11:
				_btForceJumpFramesRemaining = 30;
				PfLog("[BACKTRACK_OVERRIDE] stage=11: force-jump window=8 landings");
				return true;
			case 12:
				_btForceJumpFramesRemaining = 50;
				PfLog("[BACKTRACK_OVERRIDE] stage=12: force-jump window=16 landings");
				return true;
			case 13:
				_btSkipAllOrbs = true;
				PfLog("[BACKTRACK_OVERRIDE] stage=13: skip all orbs mode enabled");
				break;
			case 14:
				_btSkipAllOrbs = true;
				JumpTimingBias = 1.0 - JumpTimingBias;
				PfLog($"[BACKTRACK_OVERRIDE] stage=14: skip all orbs + flipped bias to {JumpTimingBias:F2}");
				break;
			case 15:
				if (_hitOrbHistory.Count > 0)
				{
					int num9 = _hitOrbHistory[_hitOrbHistory.Count - 1];
					_btSkipSpecificOrbs.Add(num9);
					PfLog($"[BACKTRACK_OVERRIDE] stage=15: skip last orb idx={num9} (history={_hitOrbHistory.Count})");
				}
				break;
			case 16:
			{
				for (int i = Math.Max(0, _hitOrbHistory.Count - 2); i < _hitOrbHistory.Count; i++)
				{
					_btSkipSpecificOrbs.Add(_hitOrbHistory[i]);
				}
				PfLog($"[BACKTRACK_OVERRIDE] stage=16: skip last 2 orbs (specific={_btSkipSpecificOrbs.Count})");
				break;
			}
			case 17:
				_committedJumpDelay = 1;
				PfLog("[BACKTRACK_OVERRIDE] stage=17: committed delay=1");
				return false;
			case 18:
				_committedJumpDelay = 2;
				PfLog("[BACKTRACK_OVERRIDE] stage=18: committed delay=2");
				return false;
			case 19:
				_committedJumpDelay = 3;
				PfLog("[BACKTRACK_OVERRIDE] stage=19: committed delay=3");
				return false;
			case 20:
				JumpTimingBias = 0.25;
				PfLog("[BACKTRACK_OVERRIDE] stage=20: bias=0.25");
				break;
			case 21:
				JumpTimingBias = 0.75;
				PfLog("[BACKTRACK_OVERRIDE] stage=21: bias=0.75");
				break;
			case 22:
				JumpTimingBias = 0.0;
				_btSuppressJumpUntilAirborne = true;
				PfLog("[BACKTRACK_OVERRIDE] stage=22: bias=0.0 + suppress");
				return false;
			case 23:
				JumpTimingBias = 1.0;
				_btSuppressJumpUntilAirborne = true;
				PfLog("[BACKTRACK_OVERRIDE] stage=23: bias=1.0 + suppress");
				return false;
			}
		}
		if (_coinInputScript.Count > 0 && _speculativeDepth == 0)
		{
			bool flag2 = _coinInputScript.Dequeue();
			PfLog($"[COIN_SCRIPT] frame={_frameCounter} remaining={_coinInputScript.Count} input={flag2} gameMode={state.GameMode}");
			return flag2;
		}
		if (state.Dashing != 0)
		{
			return DecideDashHold(state);
		}
		if (state.GameMode == 1)
		{
			return DecideWithBiasFallback(state, flag);
		}
		if (state.GameMode == 2)
		{
			return DecideBallInput(state, flag);
		}
		if (state.GameMode == 3)
		{
			return DecideWithBiasFallback(state, flag);
		}
		if (state.GameMode == 4)
		{
			return DecideWithBiasFallback(state, flag);
		}
		if (state.GameMode == 5)
		{
			return DecideSpiderInput(state, flag);
		}
		if (state.GameMode == 6)
		{
			return DecideWithBiasFallback(state, flag);
		}
		if (state.GameMode == 7)
		{
			return DecideSwingInput(state, flag);
		}
		if (state.GameMode == 8)
		{
			return DecideNinjaInput(state, flag);
		}
		if (state.GameMode == 9)
		{
			return DecidePogoInput(state, flag);
		}
		if (state.GameMode != 0)
		{
			return false;
		}
		if (!flag && !_btSuppressJumpUntilAirborne)
		{
			int orbSpriteIndex;
			int num10 = ScanForOrbOverlap(in state, out orbSpriteIndex);
			if (num10 >= 0)
			{
				if (_coinAltitudePenalties.Count > 0 && !state.GravFlipped && state.VelY_fixed < -100 && _speculativeDepth == 0)
				{
					PfLog($"[ORB_DEFER_PEAK] deferring orb 0x{num10:X2} idx={orbSpriteIndex} velY=0x{state.VelY_fixed:X4} Y={state.Y_fixed >> 8} - waiting for peak");
					return false;
				}
				if (_btSkipAllOrbs)
				{
					state.ProcessedSprites.Add(orbSpriteIndex);
					PfLog($"[ORB_SKIP_CUBE_BT] Force-skipping orb 0x{num10:X2} (btSkipAllOrbs mode)");
					return false;
				}
				if (_btSkipSpecificOrbs.Contains(orbSpriteIndex))
				{
					state.ProcessedSprites.Add(orbSpriteIndex);
					PfLog($"[ORB_SKIP_CUBE_SPECIFIC] Force-skipping orb 0x{num10:X2} idx={orbSpriteIndex} (targeted skip)");
					return false;
				}
				if (_speculativeDepth < 3)
				{
					_speculativeDepth++;
					int finalX_px;
					int num11 = SimulateForwardWithJumpAt(state, 0, out finalX_px, holdAfterLanding: false, -1, null, singleJumpOnly: true);
					SimState s = state.Clone();
					s.ProcessedSprites.Add(orbSpriteIndex);
					ClearPendingOrbs(ref s);
					int finalX_px2;
					int num12 = SimulateForwardWithJumpAt(s, -1, out finalX_px2, holdAfterLanding: false, -1, null, singleJumpOnly: true);
					if (num11 >= 90 && num12 >= 90 && finalX_px == finalX_px2)
					{
						int num13 = 270;
						int finalX_px3;
						int num14 = SimulateForwardWithJumpAt(state, 0, out finalX_px3, holdAfterLanding: false, num13, null, singleJumpOnly: true);
						int finalX_px4;
						int num15 = SimulateForwardWithJumpAt(s, -1, out finalX_px4, holdAfterLanding: false, num13, null, singleJumpOnly: true);
						PfLog($"[ORB_DECIDE_CUBE_EXT] extended horizon={num13}: hitX={finalX_px3} skipX={finalX_px4} hitSurv={num14} skipSurv={num15}");
						finalX_px = finalX_px3;
						finalX_px2 = finalX_px4;
						num11 = num14;
						num12 = num15;
					}
					_speculativeDepth--;
					PfLog($"[ORB_DECIDE_CUBE] sid=0x{num10:X2} idx={orbSpriteIndex} hitX={finalX_px} skipX={finalX_px2} hitSurv={num11} skipSurv={num12} pending={state.PendingOrbIndex}");
					if (finalX_px2 > finalX_px || (finalX_px2 == finalX_px && num12 > num11))
					{
						state.ProcessedSprites.Add(orbSpriteIndex);
						PfLog($"[ORB_SKIP_CUBE] Skipping orb 0x{num10:X2} \ufffd skipX={finalX_px2} vs hitX={finalX_px}");
						return false;
					}
					PfLog($"[ORB_HIT_CUBE] Hitting orb 0x{num10:X2} \ufffd hitX={finalX_px} vs skipX={finalX_px2}");
					_hitOrbHistory.Add(orbSpriteIndex);
					return true;
				}
			}
		}
		if (_btSuppressJumpUntilAirborne)
		{
			if (state.VelY_fixed != 0)
			{
				_btSuppressJumpUntilAirborne = false;
				PfLog("[BACKTRACK_OVERRIDE] no-jump suppression cleared (now airborne)");
			}
			else
			{
				if (!QuickDangerCheck(state))
				{
					PfLog("[BACKTRACK_OVERRIDE] no-jump suppression active (grounded)");
					return false;
				}
				_btSuppressJumpUntilAirborne = false;
				PfLog("[BACKTRACK_OVERRIDE] no-jump suppression OVERRIDDEN \ufffd danger ahead, allowing jump");
			}
		}
		bool endLevel;
		if (_btForceJumpFramesRemaining > 0 && _speculativeDepth == 0 && state.VelY_fixed == 0 && state.OnGround)
		{
			SimState s2 = state.Clone();
			_speculativeDepth++;
			bool flag3 = StepFrame(ref s2, input: true, out endLevel);
			if (flag3)
			{
				flag3 = StepFrame(ref s2, input: false, out endLevel);
			}
			if (flag3)
			{
				flag3 = StepFrame(ref s2, input: false, out endLevel);
			}
			_speculativeDepth--;
			_btForceJumpFramesRemaining--;
			if (flag3)
			{
				PfLog($"[BACKTRACK_FORCE_JUMP] forcing grounded jump, remaining={_btForceJumpFramesRemaining}");
				return true;
			}
			PfLog($"[BACKTRACK_FORCE_JUMP] jump unsafe, walking instead, remaining={_btForceJumpFramesRemaining}");
			return false;
		}
		if (state.Orbed)
		{
			return false;
		}
		if (state.VelY_fixed != 0)
		{
			if (_committedJumpDelay > 0)
			{
				_committedJumpDelay--;
			}
			if (state.GameMode != 0 || !CubeWillLandThisFrame(state))
			{
				return false;
			}
		}
		if (_committedJumpDelay > 0)
		{
			_committedJumpDelay--;
			if (_committedJumpDelay == 0)
			{
				_committedJumpDelay = -1;
				PfLog("[DECIDE_CUBE] committed delay fired \ufffd jumping now");
				return true;
			}
			PfLog($"[DECIDE_CUBE] committed delay countdown: {_committedJumpDelay} remaining");
			return false;
		}
		if (_committedJumpDelay == 0)
		{
			_committedJumpDelay = -1;
			PfLog("[DECIDE_CUBE] committed delay fired (was pending from airborne) \ufffd jumping now");
			return true;
		}
		_cubeHoldJump = false;
		if (_speculativeDepth >= 3)
		{
			return false;
		}
		if (_coinForceWalkZones.Count > 0)
		{
			int num16 = state.X_fixed >> 8;
			foreach (var (num17, num18) in _coinForceWalkZones)
			{
				if (num16 >= num17 && num16 <= num18)
				{
					if (state.OnGround && state.VelY_fixed == 0)
					{
						PfLog($"[FORCE_WALK] Player at X={num16} in force-walk zone ({num17},{num18}) \ufffd forcing WALK");
						return false;
					}
					if (!state.OnGround && ((state.GravMul > 0 && state.VelY_fixed > 0) || (state.GravMul < 0 && state.VelY_fixed < 0)))
					{
						PfLog($"[FORCE_WALK] Player at X={num16} Y={state.Y_fixed >> 8} FALLING in force-walk zone \ufffd suppressing jump buffer");
						return false;
					}
				}
			}
		}
		if (_coinGroundBiasZones.Count > 0 && state.OnGround && state.VelY_fixed == 0 && state.GameMode == 0 && _speculativeDepth == 0)
		{
			int num19 = state.X_fixed >> 8;
			foreach (var (num20, num21) in _coinGroundBiasZones)
			{
				if (num19 < num20 || num19 > num21)
				{
					continue;
				}
				bool flag4 = false;
				_speculativeDepth++;
				SimState s3 = state.Clone();
				for (int j = 0; j < 3; j++)
				{
					if (!StepFrame(ref s3, input: false, out endLevel))
					{
						flag4 = true;
						break;
					}
					if (!s3.OnGround)
					{
						break;
					}
				}
				_speculativeDepth--;
				if (!flag4)
				{
					PfLog($"[GROUND_BIAS] X={num19} in bias zone ? WALK (safe)");
					return false;
				}
				break;
			}
		}
		if (PreferCoins && allCoins.Count > 0 && state.VelY_fixed == 0 && state.GameMode == 0 && _speculativeDepth == 0 && _coinInputScript.Count == 0)
		{
			int num22 = state.X_fixed >> 8;
			int num23 = state.Y_fixed >> 8;
			for (int k = _nextCoinCheckIdx; k < allCoins.Count; k++)
			{
				SpriteEntry spriteEntry2 = allCoins[k];
				if (state.ProcessedSprites.Contains(spriteEntry2.Index) || _forgivenCoins.Contains(spriteEntry2.Index))
				{
					continue;
				}
				int num24 = (spriteEntry2.HitTop + spriteEntry2.HitBottom) / 2;
				int num25 = spriteEntry2.HitLeft - num22;
				if (num25 < 0)
				{
					continue;
				}
				if (num25 > 800)
				{
					break;
				}
				if (num23 - num24 < 30 || num25 < 100 || _cubeBeamSearchAttemptedCoinIdx == spriteEntry2.Index)
				{
					continue;
				}
				_cubeBeamSearchAttemptedCoinIdx = spriteEntry2.Index;
				int num26 = num25 / 3 + 60;
				_speculativeDepth++;
				List<(SimState, List<bool>)> list = new List<(SimState, List<bool>)>();
				list.Add((state.Clone(), new List<bool>()));
				bool flag5 = false;
				List<bool> list2 = new List<bool>();
				for (int l = 0; l < num26; l++)
				{
					if (list.Count <= 0)
					{
						break;
					}
					List<(SimState, List<bool>, int)> list3 = new List<(SimState, List<bool>, int)>();
					foreach (var item5 in list)
					{
						SimState item = item5.Item1;
						List<bool> item2 = item5.Item2;
						bool flag6 = item.PendingOrbIndex >= 0;
						bool flag7 = !item.OnGround && item.VelY_fixed != 0 && CubeWillLandThisFrame(item);
						int num27 = (flag6 ? 1 : ((!((item.OnGround && item.VelY_fixed == 0) || flag7)) ? 1 : 2));
						for (int m = 0; m < num27; m++)
						{
							bool flag8 = flag6 || (num27 != 1 && m == 1);
							SimState s4 = item.Clone();
							if (!StepFrame(ref s4, flag8, out endLevel))
							{
								continue;
							}
							List<bool> list4 = new List<bool>(item2);
							list4.Add(flag8);
							List<bool> list5 = list4;
							int num28 = (s4.X_fixed >> 8) + 1;
							int hitboxW = GetHitboxW(s4.Mini);
							int hitboxH = GetHitboxH(s4.Mini);
							int hitboxOffsetY = GetHitboxOffsetY(s4.GameMode, s4.Mini, s4.GravFlipped);
							int num29 = (s4.Y_fixed >> 8) + hitboxOffsetY;
							if (num28 + hitboxW >= spriteEntry2.HitLeft && spriteEntry2.HitRight >= num28 && num29 + hitboxH >= spriteEntry2.HitTop && spriteEntry2.HitBottom >= num29)
							{
								bool flag9 = true;
								SimState s5 = s4.Clone();
								for (int n = 0; n < 30; n++)
								{
									bool input = s5.OnGround && s5.VelY_fixed == 0;
									if (!StepFrame(ref s5, input, out endLevel))
									{
										flag9 = false;
										break;
									}
								}
								if (flag9)
								{
									flag5 = true;
									list2 = list5;
									break;
								}
							}
							int num30 = s4.X_fixed >> 8;
							if (num30 <= spriteEntry2.HitRight + 16)
							{
								int num31 = s4.Y_fixed >> 8;
								int num32 = Math.Abs(num31 - num24);
								int num33 = Math.Max(0, spriteEntry2.HitLeft - num30);
								int item3 = num32 + num33 / 3;
								list3.Add((s4, list5, item3));
							}
						}
						if (flag5)
						{
							break;
						}
					}
					if (flag5)
					{
						break;
					}
					list3.Sort(((SimState st, List<bool> inputs, int coinDist) a, (SimState st, List<bool> inputs, int coinDist) b) => a.coinDist.CompareTo(b.coinDist));
					list.Clear();
					int num34 = Math.Min(512, list3.Count);
					for (int num35 = 0; num35 < num34; num35++)
					{
						list.Add((list3[num35].Item1, list3[num35].Item2));
					}
				}
				_speculativeDepth--;
				if (flag5)
				{
					_coinInputScript.Clear();
					_coinInputScriptCoinIdx = spriteEntry2.Index;
					for (int num36 = 1; num36 < list2.Count; num36++)
					{
						_coinInputScript.Enqueue(list2[num36]);
					}
					return list2[0];
				}
				break;
			}
		}
		if (PreferCoins && allCoins.Count > 0 && state.OnGround && state.VelY_fixed == 0 && state.GameMode == 0 && _speculativeDepth == 0)
		{
			int num37 = state.X_fixed >> 8;
			for (int nextCoinCheckIdx = _nextCoinCheckIdx; nextCoinCheckIdx < allCoins.Count; nextCoinCheckIdx++)
			{
				SpriteEntry spriteEntry3 = allCoins[nextCoinCheckIdx];
				if (state.ProcessedSprites.Contains(spriteEntry3.Index) || _forgivenCoins.Contains(spriteEntry3.Index))
				{
					continue;
				}
				if (spriteEntry3.HitLeft > num37 + 500)
				{
					break;
				}
				if (spriteEntry3.HitRight < num37)
				{
					continue;
				}
				_speculativeDepth++;
				SimState s6 = state.Clone();
				bool flag10 = false;
				int num38 = 0;
				int num39 = -1;
				SimState s7 = state.Clone();
				bool flag11 = false;
				int num40 = 0;
				int num41 = -1;
				bool flag12 = false;
				List<bool> list6 = new List<bool>();
				int num42 = (mapHeight - groundRowsToReserve) * 16 - 15;
				for (int num43 = 0; num43 < 200; num43++)
				{
					bool flag13;
					if (s7.PendingOrbIndex >= 0)
					{
						flag13 = true;
					}
					else if (s7.OnGround && s7.VelY_fixed == 0 && s7.Y_fixed >> 8 >= num42 - 5)
					{
						SimState s8 = s7.Clone();
						bool flag14 = StepFrame(ref s8, input: false, out endLevel);
						flag13 = !flag14;
					}
					else
					{
						flag13 = false;
					}
					list6.Add(flag13);
					if (num43 == 0)
					{
						flag12 = flag13;
					}
					if (!StepFrame(ref s7, flag13, out var endLevel2))
					{
						break;
					}
					num40 = num43 + 1;
					if (endLevel2)
					{
						num40 = 120;
						break;
					}
					if (!flag11)
					{
						int num44 = (s7.X_fixed >> 8) + 1;
						int hitboxW2 = GetHitboxW(s7.Mini);
						int hitboxH2 = GetHitboxH(s7.Mini);
						int hitboxOffsetY2 = GetHitboxOffsetY(s7.GameMode, s7.Mini, s7.GravFlipped);
						int num45 = (s7.Y_fixed >> 8) + hitboxOffsetY2;
						int num46 = num45 + hitboxH2;
						int num47 = num44 + hitboxW2;
						bool flag15 = num47 >= spriteEntry3.HitLeft && spriteEntry3.HitRight >= num44;
						bool flag16 = num46 >= spriteEntry3.HitTop && spriteEntry3.HitBottom >= num45;
						if (flag15 && flag16)
						{
							flag11 = true;
							num41 = num43;
						}
					}
				}
				if (flag11 && num40 >= 30)
				{
					_speculativeDepth--;
					_coinInputScript.Clear();
					_coinInputScriptCoinIdx = spriteEntry3.Index;
					for (int num48 = 1; num48 < list6.Count; num48++)
					{
						_coinInputScript.Enqueue(list6[num48]);
					}
					PfLog($"[COIN_WALK_S0] Forcing {(flag12 ? "JUMP" : "WALK")}: walk?pad?coin idx={spriteEntry3.Index} sid=0x{spriteEntry3.SpriteId:X2} at ({spriteEntry3.HitLeft},{spriteEntry3.HitTop})-({spriteEntry3.HitRight},{spriteEntry3.HitBottom}) walkSurv={num40}");
					return flag12;
				}
				List<bool> list7 = null;
				bool flag17 = false;
				List<bool> list8 = new List<bool>();
				bool flag18 = false;
				for (int num49 = 0; num49 < 120; num49++)
				{
					if (s6.PendingOrbIndex >= 0)
					{
						flag18 = true;
					}
					bool flag19 = (s6.VelY_fixed == 0 && s6.OnGround) || s6.PendingOrbIndex >= 0;
					list8.Add(flag19);
					if (!StepFrame(ref s6, flag19, out var endLevel3))
					{
						break;
					}
					num38 = num49 + 1;
					if (endLevel3)
					{
						num38 = 120;
						break;
					}
					if (!flag10)
					{
						int num50 = (s6.X_fixed >> 8) + 1;
						int hitboxW3 = GetHitboxW(s6.Mini);
						int hitboxH3 = GetHitboxH(s6.Mini);
						int hitboxOffsetY3 = GetHitboxOffsetY(s6.GameMode, s6.Mini, s6.GravFlipped);
						int num51 = (s6.Y_fixed >> 8) + hitboxOffsetY3;
						int num52 = num51 + hitboxH3;
						int num53 = num50 + hitboxW3;
						bool flag20 = num53 >= spriteEntry3.HitLeft && spriteEntry3.HitRight >= num50;
						bool flag21 = num52 >= spriteEntry3.HitTop && spriteEntry3.HitBottom >= num51;
						if (flag20 && flag21)
						{
							flag10 = true;
							num39 = num49;
						}
					}
				}
				if (flag10)
				{
					list7 = list8;
					flag17 = flag18;
				}
				if (!flag10)
				{
					s6 = state.Clone();
					num38 = 0;
					bool flag22 = false;
					List<bool> list9 = new List<bool>();
					bool flag23 = false;
					for (int num54 = 0; num54 < 120; num54++)
					{
						if (s6.PendingOrbIndex >= 0)
						{
							flag23 = true;
						}
						bool flag24 = false;
						if (!flag22 && s6.VelY_fixed == 0 && s6.OnGround)
						{
							flag24 = true;
							flag22 = true;
						}
						else if (s6.PendingOrbIndex >= 0)
						{
							flag24 = true;
						}
						list9.Add(flag24);
						if (!StepFrame(ref s6, flag24, out var endLevel4))
						{
							break;
						}
						num38 = num54 + 1;
						if (endLevel4)
						{
							num38 = 120;
							break;
						}
						if (!flag10)
						{
							int num55 = (s6.X_fixed >> 8) + 1;
							int hitboxW4 = GetHitboxW(s6.Mini);
							int hitboxH4 = GetHitboxH(s6.Mini);
							int hitboxOffsetY4 = GetHitboxOffsetY(s6.GameMode, s6.Mini, s6.GravFlipped);
							int num56 = (s6.Y_fixed >> 8) + hitboxOffsetY4;
							int num57 = num56 + hitboxH4;
							int num58 = num55 + hitboxW4;
							bool flag25 = num58 >= spriteEntry3.HitLeft && spriteEntry3.HitRight >= num55;
							bool flag26 = num57 >= spriteEntry3.HitTop && spriteEntry3.HitBottom >= num56;
							if (flag25 && flag26)
							{
								flag10 = true;
								num39 = num54;
							}
						}
					}
					if (flag10)
					{
						list7 = list9;
						flag17 = flag23;
					}
				}
				_speculativeDepth--;
				if (!flag10 || num38 < num39 + 10)
				{
					break;
				}
				if (list7 != null && list7.Count > 1 && !flag17)
				{
					_coinInputScript.Clear();
					_coinInputScriptCoinIdx = spriteEntry3.Index;
					for (int num59 = 1; num59 < list7.Count; num59++)
					{
						_coinInputScript.Enqueue(list7[num59]);
					}
				}
				PfLog($"[COIN_JUMP] Forcing jump to collect coin idx={spriteEntry3.Index} sid=0x{spriteEntry3.SpriteId:X2} at ({spriteEntry3.HitLeft},{spriteEntry3.HitTop})-({spriteEntry3.HitRight},{spriteEntry3.HitBottom}) jumpSurv={num38} scriptLen={_coinInputScript.Count}");
				return true;
			}
		}
		if (PreferCoins && allCoins.Count > 0 && state.OnGround && state.VelY_fixed == 0 && state.GameMode == 0 && _speculativeDepth == 0)
		{
			int num60 = state.X_fixed >> 8;
			int num61 = state.Y_fixed >> 8;
			for (int nextCoinCheckIdx2 = _nextCoinCheckIdx; nextCoinCheckIdx2 < allCoins.Count; nextCoinCheckIdx2++)
			{
				SpriteEntry spriteEntry4 = allCoins[nextCoinCheckIdx2];
				if (state.ProcessedSprites.Contains(spriteEntry4.Index) || _forgivenCoins.Contains(spriteEntry4.Index))
				{
					continue;
				}
				if (spriteEntry4.HitLeft > num60 + 300)
				{
					break;
				}
				if (spriteEntry4.HitRight < num60)
				{
					continue;
				}
				int num62 = (spriteEntry4.HitTop + spriteEntry4.HitBottom) / 2;
				if (num62 <= num61)
				{
					break;
				}
				_speculativeDepth++;
				SimState s9 = state.Clone();
				bool flag27 = false;
				int num63 = 0;
				int value = -1;
				int num64 = num61;
				int num65 = num61;
				int num66 = num60;
				int num67 = num61;
				for (int num68 = 0; num68 < 120; num68++)
				{
					bool input2 = s9.PendingOrbIndex >= 0;
					if (!StepFrame(ref s9, input2, out var endLevel5))
					{
						break;
					}
					num63 = num68 + 1;
					int num69 = s9.Y_fixed >> 8;
					int num70 = s9.X_fixed >> 8;
					num66 = num70;
					num67 = num69;
					if (num69 < num64)
					{
						num64 = num69;
					}
					if (num69 > num65)
					{
						num65 = num69;
					}
					if (endLevel5)
					{
						num63 = 120;
						break;
					}
					if (!flag27)
					{
						int num71 = (s9.X_fixed >> 8) + 1;
						int hitboxW5 = GetHitboxW(s9.Mini);
						int hitboxH5 = GetHitboxH(s9.Mini);
						int hitboxOffsetY5 = GetHitboxOffsetY(s9.GameMode, s9.Mini, s9.GravFlipped);
						int num72 = (s9.Y_fixed >> 8) + hitboxOffsetY5;
						int num73 = num72 + hitboxH5;
						int num74 = num71 + hitboxW5;
						bool flag28 = num74 >= spriteEntry4.HitLeft && spriteEntry4.HitRight >= num71;
						bool flag29 = num73 >= spriteEntry4.HitTop && spriteEntry4.HitBottom >= num72;
						if (flag28 && flag29)
						{
							flag27 = true;
							value = num68;
						}
					}
				}
				_speculativeDepth--;
				PfLog($"[COIN_WALK_DBG] idx={spriteEntry4.Index} playerXY=({num60},{num61}) coinXY=({spriteEntry4.HitLeft},{spriteEntry4.HitTop})-({spriteEntry4.HitRight},{spriteEntry4.HitBottom}) coinCenterY={num62} collected={flag27} collFrame={value} walkSurv={num63}");
				if (flag27 && num63 >= 30)
				{
					PfLog($"[COIN_WALK] Forcing WALK to descend toward coin idx={spriteEntry4.Index} sid=0x{spriteEntry4.SpriteId:X2} at ({spriteEntry4.HitLeft},{spriteEntry4.HitTop})-({spriteEntry4.HitRight},{spriteEntry4.HitBottom}) walkSurv={num63}");
					return false;
				}
				break;
			}
		}
		int num75 = state.Y_fixed >> 8;
		int num76 = (mapHeight - groundRowsToReserve) * 16;
		int num77 = num76 - 15;
		bool flag30 = num75 <= num77 - 16;
		int num78 = (flag30 ? 600 : 120);
		int num79 = (flag30 ? 128 : 256);
		if (flag30)
		{
			PfLog($"[DECIDE_CUBE] ELEVATED BFS: horizon={num78} maxAlive={num79} Y={num75} floorY={num76}");
		}
		List<(SimState, bool, int, int, int, bool)> list10 = new List<(SimState, bool, int, int, int, bool)>();
		int bestF0JumpSurv = -1;
		int bestF0JumpX = int.MinValue;
		bool bestF0JumpHoldPattern = false;
		int bestF0JumpMinY = int.MaxValue;
		int bestF0WalkSurv = -1;
		int bestF0WalkX = int.MinValue;
		int bestF0WalkMinY = int.MaxValue;
		list10.Add((state.Clone(), true, 0, 0, state.Y_fixed >> 8, false));
		list10.Add((state.Clone(), false, 0, 0, state.Y_fixed >> 8, false));
		_speculativeDepth++;
		for (int num80 = 0; num80 < num78; num80++)
		{
			if (list10.Count <= 0)
			{
				break;
			}
			List<(SimState, bool, int, int, int, bool)> list11 = new List<(SimState, bool, int, int, int, bool)>();
			foreach (var item6 in list10)
			{
				(SimState, bool, int, int, int, bool) current2 = item6;
				int num81 = -1;
				if (current2.Item1.PendingOrbIndex >= 0)
				{
					num81 = current2.Item1.PendingOrbIndex;
				}
				else
				{
					ScanForOrbOverlap(in current2.Item1, out var orbSpriteIndex2);
					if (orbSpriteIndex2 >= 0 && !current2.Item1.ProcessedSprites.Contains(orbSpriteIndex2))
					{
						num81 = orbSpriteIndex2;
					}
				}
				int num82 = ((num81 < 0) ? 1 : 2);
				for (int num83 = 0; num83 < num82; num83++)
				{
					SimState s10;
					if (num83 == 0)
					{
						(s10, _, _, _, _, _) = current2;
					}
					else
					{
						SimState item4 = current2.Item1;
						s10 = item4.Clone();
						s10.ProcessedSprites.Add(num81);
						if (s10.PendingOrbExtra1Index >= 0)
						{
							s10.ProcessedSprites.Add(s10.PendingOrbExtra1Index);
						}
						if (s10.PendingOrbExtra2Index >= 0)
						{
							s10.ProcessedSprites.Add(s10.PendingOrbExtra2Index);
						}
						ClearPendingOrbs(ref s10);
					}
					bool flag31 = current2.Item6 || num83 == 1;
					if (s10.VelY_fixed == 0 && s10.OnGround && (s10.GameMode == 0 || s10.GameMode == 2))
					{
						int num84 = current2.Item4 + 1;
						SimState s11 = s10.Clone();
						bool endLevel6;
						bool flag32 = StepFrame(ref s11, input: true, out endLevel6);
						bool flag33 = num80 == 0 || current2.Item2;
						int num85 = current2.Item3 + 1;
						int num86 = s11.Y_fixed >> 8;
						if (endLevel6)
						{
							RecordTerminal(flag33, num78, s11.X_fixed >> 8, num85, num84, num86, flag31);
						}
						else if (flag32)
						{
							list11.Add((s11, flag33, num85, num84, num86, flag31));
						}
						else
						{
							RecordTerminal(flag33, num80, s11.X_fixed >> 8, num85, num84, num86, flag31);
						}
						SimState s12 = s10.Clone();
						bool input3 = s12.PendingOrbIndex >= 0 && !_btSkipSpecificOrbs.Contains(s12.PendingOrbIndex);
						if (s12.PendingOrbIndex >= 0 && _btSkipSpecificOrbs.Contains(s12.PendingOrbIndex))
						{
							s12.ProcessedSprites.Add(s12.PendingOrbIndex);
							if (s12.PendingOrbExtra1Index >= 0)
							{
								s12.ProcessedSprites.Add(s12.PendingOrbExtra1Index);
							}
							if (s12.PendingOrbExtra2Index >= 0)
							{
								s12.ProcessedSprites.Add(s12.PendingOrbExtra2Index);
							}
							ClearPendingOrbs(ref s12);
						}
						bool endLevel7;
						bool flag34 = StepFrame(ref s12, input3, out endLevel7);
						bool flag35 = num80 != 0 && current2.Item2;
						int num87 = s12.Y_fixed >> 8;
						if (endLevel7)
						{
							RecordTerminal(flag35, num78, s12.X_fixed >> 8, current2.Item3, num84, num87, flag31);
						}
						else if (flag34)
						{
							list11.Add((s12, flag35, current2.Item3, num84, num87, flag31));
						}
						else
						{
							RecordTerminal(flag35, num80, s12.X_fixed >> 8, current2.Item3, num84, num87, flag31);
						}
						continue;
					}
					if (s10.GameMode == 0 && CubeWillLandThisFrame(s10))
					{
						int num88 = current2.Item4 + 1;
						for (int num89 = 0; num89 < 2; num89++)
						{
							bool flag36 = num89 == 0;
							if (s10.PendingOrbIndex >= 0 && !_btSkipSpecificOrbs.Contains(s10.PendingOrbIndex))
							{
								flag36 = true;
							}
							SimState s13 = s10.Clone();
							if (s13.PendingOrbIndex >= 0 && _btSkipSpecificOrbs.Contains(s13.PendingOrbIndex))
							{
								s13.ProcessedSprites.Add(s13.PendingOrbIndex);
								if (s13.PendingOrbExtra1Index >= 0)
								{
									s13.ProcessedSprites.Add(s13.PendingOrbExtra1Index);
								}
								if (s13.PendingOrbExtra2Index >= 0)
								{
									s13.ProcessedSprites.Add(s13.PendingOrbExtra2Index);
								}
								ClearPendingOrbs(ref s13);
							}
							bool endLevel8;
							bool flag37 = StepFrame(ref s13, flag36, out endLevel8);
							bool flag38 = ((num80 == 0) ? flag36 : current2.Item2);
							int num90 = ((num89 == 0) ? (current2.Item3 + 1) : current2.Item3);
							int num91 = s13.Y_fixed >> 8;
							if (endLevel8)
							{
								RecordTerminal(flag38, num78, s13.X_fixed >> 8, num90, num88, num91, flag31);
							}
							else if (flag37)
							{
								list11.Add((s13, flag38, num90, num88, num91, flag31));
							}
							else
							{
								RecordTerminal(flag38, num80, s13.X_fixed >> 8, num90, num88, num91, flag31);
							}
						}
						continue;
					}
					SimState s14 = s10.Clone();
					bool input4 = s14.PendingOrbIndex >= 0 && !_btSkipSpecificOrbs.Contains(s14.PendingOrbIndex);
					if (s14.PendingOrbIndex >= 0 && _btSkipSpecificOrbs.Contains(s14.PendingOrbIndex))
					{
						s14.ProcessedSprites.Add(s14.PendingOrbIndex);
						if (s14.PendingOrbExtra1Index >= 0)
						{
							s14.ProcessedSprites.Add(s14.PendingOrbExtra1Index);
						}
						if (s14.PendingOrbExtra2Index >= 0)
						{
							s14.ProcessedSprites.Add(s14.PendingOrbExtra2Index);
						}
						ClearPendingOrbs(ref s14);
					}
					bool endLevel9;
					bool flag39 = StepFrame(ref s14, input4, out endLevel9);
					int num92 = s14.Y_fixed >> 8;
					if (endLevel9)
					{
						RecordTerminal(current2.Item2, num78, s14.X_fixed >> 8, current2.Item3, current2.Item4, num92, flag31);
					}
					else if (flag39)
					{
						list11.Add((s14, current2.Item2, current2.Item3, current2.Item4, num92, flag31));
					}
					else
					{
						RecordTerminal(current2.Item2, num80, s14.X_fixed >> 8, current2.Item3, current2.Item4, num92, flag31);
					}
				}
			}
			list10 = list11;
			if (list10.Count <= num79)
			{
				continue;
			}
			Dictionary<(int, int, bool, bool, int, bool, int, int, bool), (SimState, bool, int, int, int, bool)> dictionary = new Dictionary<(int, int, bool, bool, int, bool, int, int, bool), (SimState, bool, int, int, int, bool)>();
			foreach (var item7 in list10)
			{
				(int, int, bool, bool, int, bool, int, int, bool) key = (item7.Item1.Y_fixed, item7.Item1.VelY_fixed, item7.Item1.OnGround, item7.Item1.GravFlipped, item7.Item1.GameMode, item7.Item2, item7.Item1.DualActive ? item7.Item1.P2_Y_fixed : 0, item7.Item1.DualActive ? item7.Item1.P2_VelY_fixed : 0, item7.Item1.DualActive && item7.Item1.P2_GravFlipped);
				if (!dictionary.ContainsKey(key) || item7.Item3 > dictionary[key].Item3)
				{
					dictionary[key] = item7;
				}
			}
			list10 = dictionary.Values.ToList();
			if (list10.Count <= num79)
			{
				continue;
			}
			List<(SimState, bool, int, int, int, bool)> list12 = (from tuple5 in list10
				where tuple5.j0
				orderby tuple5.s.Y_fixed
				select tuple5).ToList();
			List<(SimState, bool, int, int, int, bool)> list13 = (from tuple5 in list10
				where !tuple5.j0
				orderby tuple5.s.Y_fixed
				select tuple5).ToList();
			int num93 = num79 / 2;
			if (list12.Count > num93)
			{
				List<(SimState, bool, int, int, int, bool)> list14 = new List<(SimState, bool, int, int, int, bool)>(num93);
				for (int num94 = 0; num94 < num93; num94++)
				{
					list14.Add(list12[(int)((long)num94 * (long)list12.Count / num93)]);
				}
				list12 = list14;
			}
			if (list13.Count > num93)
			{
				List<(SimState, bool, int, int, int, bool)> list15 = new List<(SimState, bool, int, int, int, bool)>(num93);
				for (int num95 = 0; num95 < num93; num95++)
				{
					list15.Add(list13[(int)((long)num95 * (long)list13.Count / num93)]);
				}
				list13 = list15;
			}
			list10 = list12.Concat(list13).ToList();
		}
		_speculativeDepth--;
		foreach (var item8 in list10)
		{
			RecordTerminal(item8.Item2, num78, item8.Item1.X_fixed >> 8, item8.Item3, item8.Item4, item8.Item5, item8.Item6);
		}
		if (_speculativeDepth == 0 && OnSpeculativePath != null)
		{
			int finalX_px5 = _speculativeDepth++;
			List<(int, int)> list16 = new List<(int, int)>();
			int arg = SimulateForwardWithJumpAt(state, 0, out finalX_px5, holdAfterLanding: false, -1, list16);
			OnSpeculativePath(list16, 0, arg, arg4: false);
			List<(int, int)> list17 = new List<(int, int)>();
			int arg2 = SimulateForwardWithJumpAt(state, 0, out finalX_px5, holdAfterLanding: true, -1, list17);
			OnSpeculativePath(list17, 0, arg2, arg4: true);
			List<(int, int)> list18 = new List<(int, int)>();
			int arg3 = SimulateForwardWithJumpAt(state, -1, holdAfterLanding: false, list18);
			OnSpeculativePath(list18, -1, arg3, arg4: false);
			List<(int, int)> list19 = new List<(int, int)>();
			int arg4 = SimulateForwardWithJumpAt(state, 1, out finalX_px5, holdAfterLanding: false, -1, list19);
			OnSpeculativePath(list19, 1, arg4, arg4: false);
			_speculativeDepth--;
		}
		int num96 = 0;
		if (bestF0JumpSurv >= num78 && bestF0WalkSurv >= 0 && bestF0JumpMinY != int.MaxValue && bestF0WalkMinY != int.MaxValue)
		{
			int num97 = bestF0WalkMinY - bestF0JumpMinY;
			num96 = num97 / 16 * 8;
			if (num96 < 0)
			{
				num96 = 0;
			}
		}
		if (PreferCoins && num96 > 0)
		{
			int num98 = state.X_fixed >> 8;
			int num99 = state.Y_fixed >> 8;
			for (int nextCoinCheckIdx3 = _nextCoinCheckIdx; nextCoinCheckIdx3 < allCoins.Count; nextCoinCheckIdx3++)
			{
				SpriteEntry spriteEntry5 = allCoins[nextCoinCheckIdx3];
				if (state.ProcessedSprites.Contains(spriteEntry5.Index) || _forgivenCoins.Contains(spriteEntry5.Index))
				{
					continue;
				}
				if (spriteEntry5.HitLeft > num98 + 1500)
				{
					break;
				}
				if (spriteEntry5.HitRight >= num98)
				{
					int num100 = (spriteEntry5.HitTop + spriteEntry5.HitBottom) / 2;
					if (num100 > num99)
					{
						num96 = 0;
					}
					break;
				}
			}
		}
		int num101 = 0;
		int num102 = 0;
		int num103 = -1;
		if (PreferCoins && allCoins.Count > 0 && bestF0JumpMinY != int.MaxValue && bestF0WalkMinY != int.MaxValue && bestF0JumpSurv > 0 && bestF0WalkSurv > 0)
		{
			int num104 = state.X_fixed >> 8;
			for (int nextCoinCheckIdx4 = _nextCoinCheckIdx; nextCoinCheckIdx4 < allCoins.Count; nextCoinCheckIdx4++)
			{
				SpriteEntry spriteEntry6 = allCoins[nextCoinCheckIdx4];
				if (state.ProcessedSprites.Contains(spriteEntry6.Index) || _forgivenCoins.Contains(spriteEntry6.Index))
				{
					continue;
				}
				if (spriteEntry6.HitLeft > num104 + 1500)
				{
					break;
				}
				if (spriteEntry6.HitRight >= num104)
				{
					num103 = (spriteEntry6.HitTop + spriteEntry6.HitBottom) / 2;
					int num105 = Math.Max(0, spriteEntry6.HitLeft - num104);
					double num106 = Math.Max(0.1, 1.0 - (double)num105 / 1500.0);
					int num107;
					int num108;
					if (num103 < state.Y_fixed >> 8)
					{
						num107 = ((bestF0JumpMinY > num103) ? (bestF0JumpMinY - num103) : 0);
						num108 = ((bestF0WalkMinY > num103) ? (bestF0WalkMinY - num103) : 0);
					}
					else
					{
						num107 = Math.Abs(bestF0JumpMinY - num103);
						num108 = Math.Abs(bestF0WalkMinY - num103);
					}
					int num109 = num108 - num107;
					int num110 = (int)((double)Math.Abs(num109) * num106);
					int num111 = ((num109 > 0) ? bestF0JumpSurv : bestF0WalkSurv);
					if (num111 < 30)
					{
						num110 = num110 * num111 / 30;
					}
					num110 = Math.Min(num110, num78);
					if (num109 > 0)
					{
						num101 = num110;
					}
					else if (num109 < 0)
					{
						num102 = num110;
					}
					break;
				}
			}
		}
		bool flag40 = bestF0WalkMinY != int.MaxValue && bestF0JumpMinY != int.MaxValue && bestF0WalkMinY > bestF0JumpMinY;
		int num112 = ((bestF0JumpHoldPattern && bestF0JumpSurv >= num78 && !flag40) ? 5 : 0);
		int num113 = 0;
		if (_coinAltitudePenalties.Count > 0)
		{
			int num114 = state.X_fixed >> 8;
			int num115 = state.Y_fixed >> 8;
			foreach (var (num116, num117, num118) in _coinAltitudePenalties)
			{
				if (num114 < num116 || num114 > num117)
				{
					continue;
				}
				num113 = 30;
				if (num115 < num118)
				{
					num113 = Math.Max(num113, num118 - num115);
				}
				break;
			}
		}
		if (num113 > 0 && num96 > 0)
		{
			num96 = 0;
		}
		if (num113 > 0 && num101 > 0)
		{
			num101 = 0;
		}
		int num119 = bestF0JumpSurv + num112 + num96 + num101 - num113;
		int num120 = bestF0WalkSurv + num102;
		if (JumpTimingBias < 0.45)
		{
			int num121 = (int)((0.5 - JumpTimingBias) * 6.0 + 0.5);
			num119 += num121;
		}
		else if (JumpTimingBias > 0.55)
		{
			int num122 = (int)((JumpTimingBias - 0.5) * 6.0 + 0.5);
			num120 += num122;
		}
		string text = "";
		bool flag41;
		if (num119 > num120)
		{
			flag41 = true;
			text = "JSCORE";
		}
		else if (num120 > num119)
		{
			flag41 = false;
			text = "WSCORE";
		}
		else if (flag30 && bestF0JumpSurv >= num78 && bestF0WalkSurv >= num78 && bestF0JumpMinY > bestF0WalkMinY)
		{
			flag41 = false;
			text = "ELEV_ALT";
		}
		else if (flag30 && bestF0JumpSurv >= num78 && bestF0WalkSurv >= num78)
		{
			flag41 = false;
			text = "ELEV_GEN";
		}
		else if (bestF0JumpSurv >= num78 && bestF0WalkSurv >= num78 && bestF0WalkMinY > bestF0JumpMinY)
		{
			flag41 = false;
			text = "FLOOR_TB";
		}
		else if (num103 >= 0 && bestF0JumpX >= bestF0WalkX)
		{
			flag41 = false;
			text = "COIN_WALK";
		}
		else if (bestF0JumpX >= bestF0WalkX)
		{
			flag41 = true;
			text = "DEFJ";
		}
		else
		{
			flag41 = false;
			text = "DEFW";
		}
		if (_speculativeDepth == 0)
		{
			PfLog($"[CUBE_DBG] X={state.X_fixed >> 8} Y={state.Y_fixed >> 8} jS={bestF0JumpSurv} wS={bestF0WalkSurv} jSc={num119} wSc={num120} hB={num112} jMY={bestF0JumpMinY} wMY={bestF0WalkMinY} tb={text}");
		}
		PfLog($"[DECIDE_CUBE] BFS: jumpSurv={bestF0JumpSurv} jumpX={bestF0JumpX} holdPat={bestF0JumpHoldPattern} jumpMinY={bestF0JumpMinY}, walkSurv={bestF0WalkSurv} walkX={bestF0WalkX} walkMinY={bestF0WalkMinY} elevBonus={num96} coinBonusJ={num101} coinBonusW={num102} ? {(flag41 ? "JUMP" : "WALK")}");
		if (!flag41)
		{
			return false;
		}
		return true;
		void RecordTerminal(bool flag42, int survFrames, int xPx, int lj, int tl, int minY, bool orbSkipped = false)
		{
			if (!orbSkipped)
			{
				if (flag42)
				{
					if (survFrames > bestF0JumpSurv || (survFrames == bestF0JumpSurv && xPx > bestF0JumpX))
					{
						bestF0JumpSurv = survFrames;
						bestF0JumpX = xPx;
						bestF0JumpHoldPattern = tl > 0 && lj == tl;
						bestF0JumpMinY = minY;
					}
				}
				else if (survFrames > bestF0WalkSurv || (survFrames == bestF0WalkSurv && xPx > bestF0WalkX))
				{
					bestF0WalkSurv = survFrames;
					bestF0WalkX = xPx;
					bestF0WalkMinY = minY;
				}
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void PreForgiveCrossCorridorCoins(int startGameMode)
	{
		if (!PreferCoins || allCoins.Count == 0)
		{
			return;
		}
		List<(int, int)> list = new List<(int, int)>();
		list.Add((0, startGameMode));
		foreach (SpriteEntry allSprite in allSprites)
		{
			if (IsGameModePortal(allSprite.SpriteId))
			{
				int num = SpriteIdToGameMode(allSprite.SpriteId);
				if (num >= 0)
				{
					list.Add((allSprite.AnchorX_px, num));
				}
			}
		}
		foreach (SpriteEntry allCoin in allCoins)
		{
			int hitLeft = allCoin.HitLeft;
			int num2 = startGameMode;
			int num3 = startGameMode;
			int num4 = 0;
			for (int i = 0; i < list.Count && list[i].Item1 <= hitLeft; i++)
			{
				num2 = num3;
				num3 = list[i].Item2;
				num4 = list[i].Item1;
			}
			int num5 = hitLeft - num4;
			if (num3 != 1 && num2 == 1 && num5 <= 2000)
			{
				_forgivenCoins.Add(allCoin.Index);
				_crossCorrForgivenCoins[allCoin.Index] = num3;
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void UnforgiveCrossCorridorCoins(int newMode)
	{
		if (_crossCorrForgivenCoins.Count == 0 || _speculativeDepth > 0)
		{
			return;
		}
		List<int> list = new List<int>();
		foreach (KeyValuePair<int, int> crossCorrForgivenCoin in _crossCorrForgivenCoins)
		{
			if (crossCorrForgivenCoin.Value == newMode)
			{
				_forgivenCoins.Remove(crossCorrForgivenCoin.Key);
				list.Add(crossCorrForgivenCoin.Key);
			}
		}
		foreach (int item in list)
		{
			_crossCorrForgivenCoins.Remove(item);
		}
	}

	private bool DecideWithBiasFallback(SimState state, bool isOverrideFrame)
	{
		bool flag = DecideModeInput(state, isOverrideFrame);
		if (state.GameMode == 4)
		{
			return flag;
		}
		if (_speculativeDepth > 0)
		{
			return flag;
		}
		double jumpTimingBias = JumpTimingBias;
		if (Math.Abs(jumpTimingBias - 0.5) < 0.05)
		{
			return flag;
		}
		_speculativeDepth++;
		SimState s = state.Clone();
		bool endLevel;
		bool flag2 = StepFrame(ref s, flag, out endLevel);
		if (endLevel)
		{
			_speculativeDepth--;
			return flag;
		}
		int num = 0;
		if (flag2)
		{
			for (int i = 0; i < 3; i++)
			{
				bool endLevel2;
				bool flag3 = StepFrame(ref s, flag, out endLevel2);
				if (endLevel2)
				{
					num = 4;
					break;
				}
				if (!flag3)
				{
					break;
				}
				num++;
			}
		}
		SimState s2 = state.Clone();
		bool endLevel3;
		bool flag4 = StepFrame(ref s2, !flag, out endLevel3);
		if (endLevel3)
		{
			_speculativeDepth--;
			return !flag;
		}
		int num2 = 0;
		if (flag4)
		{
			for (int j = 0; j < 3; j++)
			{
				bool endLevel4;
				bool flag5 = StepFrame(ref s2, !flag, out endLevel4);
				if (endLevel4)
				{
					num2 = 4;
					break;
				}
				if (!flag5)
				{
					break;
				}
				num2++;
			}
		}
		_speculativeDepth--;
		if (num > 1 || num2 > 1)
		{
			return (num >= num2) ? flag : (!flag);
		}
		JumpTimingBias = 0.5;
		bool result = DecideModeInput(state, isOverrideFrame);
		JumpTimingBias = jumpTimingBias;
		return result;
	}

	private bool DecideModeInput(SimState state, bool isOverrideFrame)
	{
		return state.GameMode switch
		{
			1 => DecideShipInput(state), 
			3 => DecideUfoInput(state, isOverrideFrame), 
			4 => DecideRobotInput(state, isOverrideFrame), 
			5 => DecideSpiderInput(state, isOverrideFrame), 
			6 => DecideWaveInput(state, isOverrideFrame), 
			7 => DecideSwingInput(state, isOverrideFrame), 
			8 => DecideNinjaInput(state, isOverrideFrame), 
			9 => DecidePogoInput(state, isOverrideFrame), 
			_ => false, 
		};
	}

	private bool DecideShipInput(SimState state)
	{
		if (_shipForceReleaseFirstFrames > 0)
		{
			_shipForceReleaseFirstFrames--;
			return false;
		}
		if (_shipForceHoldFrames > 0)
		{
			_shipForceHoldFrames--;
			return true;
		}
		if (_shipForceReleaseFrames > 0)
		{
			_shipForceReleaseFrames--;
			return false;
		}
		int num = -1;
		int num2 = -1;
		int num3 = 0;
		SpriteEntry spriteEntry = default(SpriteEntry);
		bool flag = false;
		if (_speculativeDepth == 0 && _modeTransitionStabilizeFrames > 0)
		{
			_modeTransitionStabilizeFrames--;
		}
		if (PreferCoins && allCoins.Count > 0 && _modeTransitionStabilizeFrames == 0)
		{
			int num4 = state.X_fixed >> 8;
			for (int i = _nextCoinCheckIdx; i < allCoins.Count; i++)
			{
				SpriteEntry spriteEntry2 = allCoins[i];
				if (state.ProcessedSprites.Contains(spriteEntry2.Index) || _forgivenCoins.Contains(spriteEntry2.Index))
				{
					continue;
				}
				if (spriteEntry2.HitLeft > num4 + 2000)
				{
					break;
				}
				if (spriteEntry2.HitRight >= num4)
				{
					num = (spriteEntry2.HitTop + spriteEntry2.HitBottom) / 2;
					num2 = spriteEntry2.Index;
					num3 = spriteEntry2.HitLeft - num4;
					if (num3 < 0)
					{
						num3 = 0;
					}
					spriteEntry = spriteEntry2;
					flag = true;
					break;
				}
			}
		}
		_speculativeDepth++;
		SimState s = state.Clone();
		SimState s2 = state.Clone();
		bool endLevel;
		bool flag2 = StepFrame(ref s, input: true, out endLevel);
		bool endLevel2;
		bool flag3 = StepFrame(ref s2, input: false, out endLevel2);
		_speculativeDepth--;
		if (endLevel)
		{
			return true;
		}
		if (endLevel2)
		{
			return false;
		}
		if (flag2 && !flag3)
		{
			return true;
		}
		if (!flag2 && flag3)
		{
			return false;
		}
		if (!flag2 && !flag3)
		{
			return false;
		}
		_shipTreeNodesExplored = 0;
		int num5 = 1 + ShipTreeSearch(s, 19);
		_shipTreeNodesExplored = 0;
		int num6 = 1 + ShipTreeSearch(s2, 19);
		if (_speculativeDepth == 0 && OnSpeculativePath != null)
		{
			SimState s3 = state.Clone();
			SimState s4 = state.Clone();
			List<(int, int)> list = new List<(int, int)>();
			List<(int, int)> list2 = new List<(int, int)>();
			int num7 = ((s3.Mini && !s3.GravFlipped) ? 4 : 0);
			list.Add(((s3.X_fixed >> 8) + 8, (s3.Y_fixed >> 8) + num7 + 8));
			int num8 = ((s4.Mini && !s4.GravFlipped) ? 4 : 0);
			list2.Add(((s4.X_fixed >> 8) + 8, (s4.Y_fixed >> 8) + num8 + 8));
			_speculativeDepth++;
			for (int j = 0; j < 20; j++)
			{
				bool endLevel3;
				bool flag4 = StepFrame(ref s3, input: true, out endLevel3);
				int num9 = ((s3.Mini && !s3.GravFlipped) ? 4 : 0);
				list.Add(((s3.X_fixed >> 8) + 8, (s3.Y_fixed >> 8) + num9 + 8));
				if (!flag4 || endLevel3)
				{
					break;
				}
			}
			for (int k = 0; k < 20; k++)
			{
				bool endLevel4;
				bool flag5 = StepFrame(ref s4, input: false, out endLevel4);
				int num10 = ((s4.Mini && !s4.GravFlipped) ? 4 : 0);
				list2.Add(((s4.X_fixed >> 8) + 8, (s4.Y_fixed >> 8) + num10 + 8));
				if (!flag5 || endLevel4)
				{
					break;
				}
			}
			_speculativeDepth--;
			OnSpeculativePath(list, 0, num5, arg4: true);
			OnSpeculativePath(list2, 1, num6, arg4: false);
		}
		int num11 = 600;
		bool flag6 = false;
		if (num >= 0 && flag && num3 <= 1200)
		{
			int num12 = FindCorridorCenter(ref state);
			int num13 = Math.Abs(num - num12);
			if (num13 > 40)
			{
				num11 = Math.Min(1200, Math.Max(600, num13 * 10));
				flag6 = num3 > 600;
			}
		}
		int num14 = (flag6 ? 60 : 30);
		bool endLevel5;
		if (num >= 0 && flag && _coinInputScript.Count == 0 && num3 >= 20 && num3 <= num11 && (_beamSearchAttemptedCoinIdx != num2 || num3 <= _beamSearchLastDistX - num14))
		{
			int num15 = (flag6 ? 128 : 512);
			int num16 = (flag6 ? Math.Max(450, num3 / 2 + 60 + 120) : Math.Min(450, num3 * 3 / 4 + 60 + 120));
			_speculativeDepth++;
			List<(SimState, List<bool>, int)> list3 = new List<(SimState, List<bool>, int)>();
			list3.Add((state.Clone(), new List<bool>(), -1));
			bool flag7 = false;
			List<bool> list4 = new List<bool>();
			int num17 = 0;
			for (int l = 0; l < num16; l++)
			{
				if (list3.Count <= 0)
				{
					break;
				}
				List<(SimState, List<bool>, int, int)> list5 = new List<(SimState, List<bool>, int, int)>();
				foreach (var item9 in list3)
				{
					SimState item = item9.Item1;
					List<bool> item2 = item9.Item2;
					int item3 = item9.Item3;
					for (int m = 0; m <= 1; m++)
					{
						bool flag8 = m == 1;
						SimState s5 = item.Clone();
						if (!StepFrame(ref s5, flag8, out endLevel5))
						{
							continue;
						}
						List<bool> list6 = new List<bool>(item2) { flag8 };
						int num18 = item3;
						if (num18 < 0)
						{
							int num19 = (s5.X_fixed >> 8) + 1;
							int hitboxW = GetHitboxW(s5.Mini);
							int hitboxH = GetHitboxH(s5.Mini);
							int hitboxOffsetY = GetHitboxOffsetY(s5.GameMode, s5.Mini, s5.GravFlipped);
							int num20 = (s5.Y_fixed >> 8) + hitboxOffsetY;
							if (num19 + hitboxW >= spriteEntry.HitLeft && spriteEntry.HitRight >= num19 && num20 + hitboxH >= spriteEntry.HitTop && spriteEntry.HitBottom >= num20)
							{
								num18 = 0;
							}
						}
						if (num18 >= 0)
						{
							num18++;
							if (num18 >= 120)
							{
								flag7 = true;
								list4 = list6;
								num17 = num18;
								break;
							}
							if (num18 > num17)
							{
								num17 = num18;
								list4 = new List<bool>(list6);
							}
							int num21 = s5.Y_fixed >> 8;
							int num22 = FindCorridorCenter(ref s5);
							int num23 = Math.Abs(num21 - num22);
							int num24 = Math.Abs(s5.VelY_fixed) >> 6;
							int item4 = -10000 + num23 * 3 + num24;
							list5.Add((s5, list6, num18, item4));
							continue;
						}
						int num25 = s5.X_fixed >> 8;
						if (num25 <= spriteEntry.HitRight + 4)
						{
							int num26 = s5.Y_fixed >> 8;
							int num27 = Math.Abs(num26 - num);
							int num28 = Math.Max(0, spriteEntry.HitLeft - num25);
							int num29 = ((num28 > 120) ? (num28 + num27) : ((num28 > 50) ? (num28 / 2 + num27 * 2) : ((num28 <= 15) ? (num27 * 6 + num28 / 4) : (num28 / 4 + num27 * 4))));
							int velY_fixed = s5.VelY_fixed;
							int num30 = num - num26;
							bool flag9 = num30 > 0 == velY_fixed * s5.GravMul > 0;
							if (flag9 && num27 > 4)
							{
								int val = Math.Abs(velY_fixed) >> 7;
								num29 -= Math.Min(num27 / 2, val);
							}
							bool flag10 = ((s5.GravMul > 0) ? (num26 > num + 20) : (num26 < num - 20));
							bool flag11 = !flag9 && Math.Abs(velY_fixed) > 512;
							if (flag10 && flag11)
							{
								num29 += 200;
							}
							list5.Add((s5, list6, num18, num29));
						}
					}
					if (flag7)
					{
						break;
					}
				}
				if (flag7)
				{
					break;
				}
				if (num17 >= 120 && list5.Count == 0)
				{
					flag7 = true;
					break;
				}
				int num31 = num15 / 4;
				int val2 = num15 - num31;
				List<(SimState, List<bool>, int, int)> list7 = new List<(SimState, List<bool>, int, int)>();
				List<(SimState, List<bool>, int, int)> list8 = new List<(SimState, List<bool>, int, int)>();
				foreach (var item10 in list5)
				{
					if (item10.Item3 >= 0)
					{
						list7.Add(item10);
					}
					else
					{
						list8.Add(item10);
					}
				}
				list3.Clear();
				list7.Sort(((SimState st, List<bool> inputs, int collected, int score) a, (SimState st, List<bool> inputs, int collected, int score) b) => a.score.CompareTo(b.score));
				int num32 = Math.Min(num15, list7.Count);
				for (int num33 = 0; num33 < num32; num33++)
				{
					list3.Add((list7[num33].Item1, list7[num33].Item2, list7[num33].Item3));
				}
				int num34 = num15 - list3.Count;
				if (num34 <= 0 || list8.Count <= 0)
				{
					continue;
				}
				list8.Sort(((SimState st, List<bool> inputs, int collected, int score) a, (SimState st, List<bool> inputs, int collected, int score) b) => a.score.CompareTo(b.score));
				int num35 = Math.Min(Math.Min(val2, num34), list8.Count);
				HashSet<int> hashSet = new HashSet<int>();
				for (int num36 = 0; num36 < num35; num36++)
				{
					list3.Add((list8[num36].Item1, list8[num36].Item2, list8[num36].Item3));
					hashSet.Add(num36);
				}
				num34 = num15 - list3.Count;
				if (num34 <= 0)
				{
					continue;
				}
				SimState s6 = list8[0].Item1;
				int num37 = FindCorridorCenter(ref s6);
				List<(int, int)> list9 = new List<(int, int)>();
				for (int num38 = 0; num38 < list8.Count; num38++)
				{
					if (!hashSet.Contains(num38))
					{
						(SimState, List<bool>, int, int) tuple = list8[num38];
						int num39 = tuple.Item1.Y_fixed >> 8;
						int num40 = Math.Abs(num39 - num37);
						int num41 = Math.Abs(tuple.Item1.VelY_fixed) >> 6;
						list9.Add((num38, num40 * 2 + num41));
					}
				}
				list9.Sort(((int origIdx, int safeScore) a, (int origIdx, int safeScore) b) => a.safeScore.CompareTo(b.safeScore));
				int num42 = Math.Min(num34, list9.Count);
				for (int num43 = 0; num43 < num42; num43++)
				{
					(SimState, List<bool>, int, int) tuple2 = list8[list9[num43].Item1];
					list3.Add((tuple2.Item1, tuple2.Item2, tuple2.Item3));
				}
			}
			if (!flag7 && num17 >= 15)
			{
				flag7 = true;
				PfLog($"[SHIP_COIN_BEAM_PARTIAL] Accepting partial recovery={num17} for coin idx={spriteEntry.Index} distX={num3}");
			}
			_speculativeDepth--;
			_beamSearchAttemptedCoinIdx = num2;
			_beamSearchLastDistX = num3;
			if (flag7 && list4.Count > 0)
			{
				_coinInputScript.Clear();
				_coinInputScriptCoinIdx = num2;
				for (int num44 = 1; num44 < list4.Count; num44++)
				{
					_coinInputScript.Enqueue(list4[num44]);
				}
				PfLog($"[SHIP_COIN_BEAM] Found trajectory for coin idx={spriteEntry.Index}, scriptLen={list4.Count}, distX={num3}, recovery={num17}");
				return list4[0];
			}
			PfLog($"[SHIP_COIN_BEAM_FAIL] coin idx={spriteEntry.Index} distX={num3} horizon={num16} beamEnd={list3.Count} bestRecovery={num17} found={flag7} scriptLen={list4.Count} lastDeath={_lastDeathReason} lastDeathX={_lastDeathX} lastDeathY={_lastDeathY}");
			if (list3.Count == 0 && num17 == 0)
			{
				_speculativeDepth++;
				int num45 = FindCorridorCenter(ref state);
				int num46 = Math.Min(600, num3 + 60 + 120);
				int num47 = Math.Min(num45, num);
				int num48 = Math.Max(num45, num);
				PfLog($"[SHIP_COIN_BEAM_SURVIVAL_RETRY] coin idx={spriteEntry.Index} distX={num3} corridorY={num45} coinY={num} segY=[{num47},{num48}] horizon={num46}");
				list3.Clear();
				list3.Add((state.Clone(), new List<bool>(), -1));
				flag7 = false;
				list4.Clear();
				num17 = 0;
				for (int num49 = 0; num49 < num46; num49++)
				{
					if (list3.Count <= 0)
					{
						break;
					}
					List<(SimState, List<bool>, int, int)> list10 = new List<(SimState, List<bool>, int, int)>();
					foreach (var item11 in list3)
					{
						SimState item5 = item11.Item1;
						List<bool> item6 = item11.Item2;
						int item7 = item11.Item3;
						for (int num50 = 0; num50 <= 1; num50++)
						{
							bool flag12 = num50 == 1;
							SimState s7 = item5.Clone();
							if (!StepFrame(ref s7, flag12, out endLevel5))
							{
								continue;
							}
							List<bool> list11 = new List<bool>(item6) { flag12 };
							int num51 = item7;
							if (num51 < 0)
							{
								int num52 = (s7.X_fixed >> 8) + 1;
								int hitboxW2 = GetHitboxW(s7.Mini);
								int hitboxH2 = GetHitboxH(s7.Mini);
								int hitboxOffsetY2 = GetHitboxOffsetY(s7.GameMode, s7.Mini, s7.GravFlipped);
								int num53 = (s7.Y_fixed >> 8) + hitboxOffsetY2;
								if (num52 + hitboxW2 >= spriteEntry.HitLeft && spriteEntry.HitRight >= num52 && num53 + hitboxH2 >= spriteEntry.HitTop && spriteEntry.HitBottom >= num53)
								{
									num51 = 0;
								}
							}
							int item8;
							if (num51 >= 0)
							{
								num51++;
								if (num51 >= 120)
								{
									flag7 = true;
									list4 = list11;
									num17 = num51;
									break;
								}
								if (num51 > num17)
								{
									num17 = num51;
									list4 = new List<bool>(list11);
								}
								int num54 = s7.Y_fixed >> 8;
								int num55 = FindCorridorCenter(ref s7);
								int num56 = Math.Abs(num54 - num55);
								int num57 = Math.Abs(s7.VelY_fixed) >> 6;
								item8 = -10000 + num56 * 3 + num57;
							}
							else
							{
								int num58 = s7.X_fixed >> 8;
								if (num58 > spriteEntry.HitRight + 32)
								{
									continue;
								}
								int num59 = s7.Y_fixed >> 8;
								int num60 = ((num59 < num47) ? (num47 - num59) : ((num59 > num48) ? (num59 - num48) : 0));
								int num61 = Math.Abs(s7.VelY_fixed) >> 7;
								item8 = num60 * 3 + num61;
							}
							list10.Add((s7, list11, num51, item8));
						}
						if (flag7)
						{
							break;
						}
					}
					if (flag7)
					{
						break;
					}
					if (num17 >= 120 && list10.Count == 0)
					{
						flag7 = true;
						break;
					}
					list10.Sort(((SimState st, List<bool> inputs, int collected, int score) a, (SimState st, List<bool> inputs, int collected, int score) b) => a.score.CompareTo(b.score));
					list3.Clear();
					int num62 = Math.Min(1024, list10.Count);
					for (int num63 = 0; num63 < num62; num63++)
					{
						list3.Add((list10[num63].Item1, list10[num63].Item2, list10[num63].Item3));
					}
				}
				if (!flag7 && num17 >= 15)
				{
					flag7 = true;
					PfLog($"[SHIP_COIN_BEAM_SURVIVAL_PARTIAL] recovery={num17} coin idx={spriteEntry.Index}");
				}
				_speculativeDepth--;
				if (flag7 && list4.Count > 0)
				{
					_coinInputScript.Clear();
					_coinInputScriptCoinIdx = num2;
					for (int num64 = 1; num64 < list4.Count; num64++)
					{
						_coinInputScript.Enqueue(list4[num64]);
					}
					PfLog($"[SHIP_COIN_BEAM_SURVIVAL_OK] coin idx={spriteEntry.Index} scriptLen={list4.Count} distX={num3} recovery={num17}");
					return list4[0];
				}
				PfLog($"[SHIP_COIN_BEAM_SURVIVAL_FAIL] coin idx={spriteEntry.Index} distX={num3} horizon={num46} beamEnd={list3.Count} bestRecovery={num17} lastDeath={_lastDeathReason} lastDeathX={_lastDeathX} lastDeathY={_lastDeathY}");
			}
		}
		if (num5 != num6)
		{
			int num65 = 0;
			bool flag13 = false;
			if (num >= 0 && flag && num3 <= 1200)
			{
				int num66 = FindCorridorCenter(ref state);
				num65 = Math.Abs(num - num66);
				flag13 = num65 > 40;
			}
			int num67 = (flag13 ? 5 : 10);
			if (!(num >= 0 && flag) || Math.Min(num5, num6) < num67 || !(num3 <= 600 || flag13))
			{
				return num5 > num6;
			}
			_speculativeDepth++;
			bool flag14 = false;
			int value = -1;
			SimState s8 = state.Clone();
			StepFrame(ref s8, input: true, out endLevel5);
			for (int num68 = 1; num68 < 120; num68++)
			{
				int num69 = s8.Y_fixed >> 8;
				int velY_fixed2 = s8.VelY_fixed;
				int num70 = ((s8.GravMul > 0) ? (num69 - num) : (num - num69));
				int num71 = -(velY_fixed2 * s8.GravMul);
				bool input = num70 - num71 > 0;
				if (!StepFrame(ref s8, input, out endLevel5))
				{
					value = num68;
					break;
				}
				int num72 = (s8.X_fixed >> 8) + 1;
				int hitboxW3 = GetHitboxW(s8.Mini);
				int hitboxH3 = GetHitboxH(s8.Mini);
				int hitboxOffsetY3 = GetHitboxOffsetY(s8.GameMode, s8.Mini, s8.GravFlipped);
				int num73 = (s8.Y_fixed >> 8) + hitboxOffsetY3;
				if (num72 + hitboxW3 >= spriteEntry.HitLeft && spriteEntry.HitRight >= num72 && num73 + hitboxH3 >= spriteEntry.HitTop && spriteEntry.HitBottom >= num73)
				{
					flag14 = true;
					break;
				}
			}
			bool flag15 = false;
			int value2 = -1;
			SimState s9 = state.Clone();
			StepFrame(ref s9, input: false, out endLevel5);
			for (int num74 = 1; num74 < 120; num74++)
			{
				int num75 = s9.Y_fixed >> 8;
				int velY_fixed3 = s9.VelY_fixed;
				int num76 = ((s9.GravMul > 0) ? (num75 - num) : (num - num75));
				int num77 = -(velY_fixed3 * s9.GravMul);
				bool input2 = num76 - num77 > 0;
				if (!StepFrame(ref s9, input2, out endLevel5))
				{
					value2 = num74;
					break;
				}
				int num78 = (s9.X_fixed >> 8) + 1;
				int hitboxW4 = GetHitboxW(s9.Mini);
				int hitboxH4 = GetHitboxH(s9.Mini);
				int hitboxOffsetY4 = GetHitboxOffsetY(s9.GameMode, s9.Mini, s9.GravFlipped);
				int num79 = (s9.Y_fixed >> 8) + hitboxOffsetY4;
				if (num78 + hitboxW4 >= spriteEntry.HitLeft && spriteEntry.HitRight >= num78 && num79 + hitboxH4 >= spriteEntry.HitTop && spriteEntry.HitBottom >= num79)
				{
					flag15 = true;
					break;
				}
			}
			_speculativeDepth--;
			if (num3 < 400)
			{
				PfLog($"[SHIP_COIN_SIM] idx={spriteEntry.Index} playerX={state.X_fixed >> 8} distX={num3} holdCol={flag14} holdDied={value} relCol={flag15} relDied={value2} survH={num5} survR={num6} threshold={_shipCoinAggressiveThreshold}");
			}
			int num80 = (flag13 ? 1 : 10);
			if (flag14 && !flag15 && num5 >= num80)
			{
				PfLog($"[SHIP_COIN] Hold collects coin idx={spriteEntry.Index} \ufffd forcing hold (survH={num5} survR={num6})");
				return true;
			}
			if (flag15 && !flag14 && num6 >= num80)
			{
				PfLog($"[SHIP_COIN] Release collects coin idx={spriteEntry.Index} \ufffd forcing release (survH={num5} survR={num6})");
				return false;
			}
			int num81 = _shipCoinAggressiveThreshold;
			if (flag13)
			{
				num81 = ((num3 > 400) ? Math.Max(num81, 4) : ((num3 <= 200) ? Math.Max(num81, 20) : Math.Max(num81, 10)));
			}
			else if (num3 <= 600)
			{
				num81 = ((num3 > 300) ? Math.Max(num81, 4) : ((num3 > 200) ? Math.Max(num81, 6) : ((num3 <= 100) ? Math.Max(num81, 20) : Math.Max(num81, 10))));
			}
			if (Math.Abs(num5 - num6) > num81)
			{
				return num5 > num6;
			}
		}
		int num82 = (int)((JumpTimingBias - 0.5) * 16.0);
		foreach (KeyValuePair<int, int> item12 in _coinCollectThenLoseCount)
		{
			if (!_forgivenCoins.Contains(item12.Key))
			{
				num82 -= 3 * item12.Value;
			}
		}
		bool flag16 = false;
		int num93;
		if (num >= 0)
		{
			int num83 = FindCorridorCenter(ref state) + _shipCorridorBias + num82;
			int num84 = Math.Abs(num - num83);
			if (num84 > 40 && num3 <= 2000)
			{
				int num85 = FindCorridorCenter(ref state, 2, num) + _shipCorridorBias + num82;
				int num86 = FindCorridorCenter(ref state, 0) + _shipCorridorBias + num82;
				int num87 = Math.Abs(num - num85);
				int num88 = Math.Abs(num - num86);
				bool flag17 = num87 < num88 - 20;
				int num89 = (flag17 ? num85 : num86);
				int num90 = Math.Abs(num - num89);
				int num91 = ((num90 > 40 && num90 <= 120 && num3 <= 2000) ? Math.Min(2000, Math.Max(400, num90 * 16)) : 400);
				if (num3 > num91)
				{
					int num92 = 80;
					num93 = num89 + (num - num89) * num92 / 100;
				}
				else if (num3 > 400)
				{
					int num94 = num91 - 400;
					int num95 = ((!flag17) ? ((num94 > 0) ? (80 + (num91 - num3) * 10 / num94) : 90) : ((num94 > 0) ? (70 + (num91 - num3) * 25 / num94) : 95));
					num93 = num89 + (num - num89) * num95 / 100;
				}
				else if (num3 > 300)
				{
					int num96 = (flag17 ? 90 : 85);
					num93 = num89 + (num - num89) * num96 / 100;
				}
				else if (num3 > 200)
				{
					int num97 = (flag17 ? 95 : 90);
					num93 = num89 + (num - num89) * num97 / 100;
				}
				else if (num3 > 120)
				{
					int num98 = (flag17 ? 100 : 95);
					num93 = num89 + (num - num89) * num98 / 100;
				}
				else
				{
					num93 = num;
				}
			}
			else
			{
				num93 = num;
			}
		}
		else
		{
			num93 = FindCorridorCenter(ref state) + _shipCorridorBias + num82;
		}
		int num99 = state.Y_fixed >> 8;
		int velY_fixed4 = state.VelY_fixed;
		int num100 = ((state.GravMul > 0) ? (num99 - num93) : (num93 - num99));
		int num101 = -(velY_fixed4 * state.GravMul);
		int num102 = ((num >= 0) ? 1 : 2);
		int num103 = num100 - num101 * num102;
		return num103 > 0;
	}

	private int ShipTreeSearch(SimState state, int depthRemaining)
	{
		if (depthRemaining <= 0 || _shipTreeNodesExplored >= 16000)
		{
			return 0;
		}
		_shipTreeNodesExplored++;
		_speculativeDepth++;
		SimState s = state.Clone();
		bool endLevel;
		bool flag = StepFrame(ref s, input: true, out endLevel);
		int num = 0;
		if (endLevel)
		{
			num = depthRemaining;
		}
		else if (flag)
		{
			num = 1 + ShipTreeSearch(s, depthRemaining - 1);
		}
		s.ReturnAllSpriteResources();
		if (num >= depthRemaining)
		{
			_speculativeDepth--;
			return num;
		}
		_shipTreeNodesExplored++;
		SimState s2 = state.Clone();
		bool endLevel2;
		bool flag2 = StepFrame(ref s2, input: false, out endLevel2);
		int val = 0;
		if (endLevel2)
		{
			val = depthRemaining;
		}
		else if (flag2)
		{
			val = 1 + ShipTreeSearch(s2, depthRemaining - 1);
		}
		s2.ReturnAllSpriteResources();
		_speculativeDepth--;
		return Math.Max(num, val);
	}

	private int ShipCoinRecovery(SimState postCollect, int gravMul)
	{
		int val = 0;
		SimState s = postCollect.Clone();
		int num = 0;
		bool flag = true;
		bool endLevel;
		for (int i = 0; i < 6 && flag; i++)
		{
			bool input = gravMul > 0;
			flag = StepFrame(ref s, input, out endLevel);
			if (flag)
			{
				num++;
			}
		}
		if (flag)
		{
			int num2 = FindCorridorCenter(ref s);
			for (int j = 0; j < 114 && flag; j++)
			{
				if (j > 0 && (j & 3) == 0)
				{
					num2 = FindCorridorCenter(ref s);
				}
				int num3 = s.Y_fixed >> 8;
				int velY_fixed = s.VelY_fixed;
				int num4 = ((s.GravMul > 0) ? (num3 - num2) : (num2 - num3));
				int num5 = -(velY_fixed * s.GravMul);
				bool input2 = num4 - num5 * 2 > 0;
				flag = StepFrame(ref s, input2, out endLevel);
				if (flag)
				{
					num++;
				}
			}
		}
		val = Math.Max(val, num);
		SimState s2 = postCollect.Clone();
		int num6 = 0;
		bool flag2 = true;
		int num7 = FindCorridorCenter(ref s2);
		for (int k = 0; k < 120 && flag2; k++)
		{
			if (k > 0 && (k & 3) == 0)
			{
				num7 = FindCorridorCenter(ref s2);
			}
			int num8 = s2.Y_fixed >> 8;
			int velY_fixed2 = s2.VelY_fixed;
			int num9 = ((s2.GravMul > 0) ? (num8 - num7) : (num7 - num8));
			int num10 = -(velY_fixed2 * s2.GravMul);
			bool input3 = num9 - num10 * 2 > 0;
			flag2 = StepFrame(ref s2, input3, out endLevel);
			if (flag2)
			{
				num6++;
			}
		}
		val = Math.Max(val, num6);
		SimState s3 = postCollect.Clone();
		int num11 = 0;
		bool flag3 = true;
		for (int l = 0; l < 12 && flag3; l++)
		{
			bool input4 = gravMul > 0;
			flag3 = StepFrame(ref s3, input4, out endLevel);
			if (flag3)
			{
				num11++;
			}
		}
		if (flag3)
		{
			int num12 = FindCorridorCenter(ref s3);
			for (int m = 0; m < 108 && flag3; m++)
			{
				if (m > 0 && (m & 3) == 0)
				{
					num12 = FindCorridorCenter(ref s3);
				}
				int num13 = s3.Y_fixed >> 8;
				int velY_fixed3 = s3.VelY_fixed;
				int num14 = ((s3.GravMul > 0) ? (num13 - num12) : (num12 - num13));
				int num15 = -(velY_fixed3 * s3.GravMul);
				bool input5 = num14 - num15 * 2 > 0;
				flag3 = StepFrame(ref s3, input5, out endLevel);
				if (flag3)
				{
					num11++;
				}
			}
		}
		val = Math.Max(val, num11);
		SimState s4 = postCollect.Clone();
		int num16 = 0;
		bool flag4 = true;
		for (int n = 0; n < 120 && flag4; n++)
		{
			SimState s5 = s4.Clone();
			SimState s6 = s4.Clone();
			bool flag5 = StepFrame(ref s5, input: true, out endLevel);
			bool flag6 = StepFrame(ref s6, input: false, out endLevel);
			if (!flag5 && !flag6)
			{
				break;
			}
			if (!flag5)
			{
				s4 = s6;
				num16++;
				continue;
			}
			if (!flag6)
			{
				s4 = s5;
				num16++;
				continue;
			}
			_shipTreeNodesExplored = 0;
			int num17 = 1 + ShipTreeSearch(s5, Math.Min(6, 120 - n - 1));
			_shipTreeNodesExplored = 0;
			int num18 = 1 + ShipTreeSearch(s6, Math.Min(6, 120 - n - 1));
			if (num17 >= num18)
			{
				s4 = s5;
				if (num17 == num18)
				{
					int num19 = FindCorridorCenter(ref s4);
					int num20 = s4.Y_fixed >> 8;
					int num21 = ((s4.GravMul > 0) ? (num20 - num19) : (num19 - num20));
					if (num21 < 0)
					{
						s4 = s6;
					}
				}
			}
			else
			{
				s4 = s6;
			}
			num16++;
		}
		return Math.Max(val, num16);
	}

	private int FindCorridorCenter(ref SimState s)
	{
		return FindCorridorCenter(ref s, 15, -1);
	}

	private int FindCorridorCenter(ref SimState s, int lookAhead)
	{
		return FindCorridorCenter(ref s, lookAhead, -1);
	}

	private int FindCorridorCenter(ref SimState s, int lookAhead, int overrideY)
	{
		int num = s.X_fixed >> 8;
		int num2 = ((overrideY >= 0) ? overrideY : (s.Y_fixed >> 8));
		int hitboxW = GetHitboxW(s.Mini);
		int hitboxH = GetHitboxH(s.Mini);
		int num3 = num + hitboxW / 2;
		int num4 = (mapHeight - groundRowsToReserve) * 16;
		int num5 = 0;
		int num6 = num4;
		int num7 = num2 / 16;
		int num8 = (num2 + hitboxH - 1) / 16;
		int num9 = num3 / 16;
		int num10 = Math.Min(num9 + lookAhead, mapWidth - 1);
		for (int i = num9; i <= num10; i++)
		{
			for (int num11 = num7 - 1; num11 >= 0; num11--)
			{
				MetatileCollision tileCollision = GetTileCollision(i, num11);
				if (tileCollision != MetatileCollision.COL_NONE)
				{
					var (num12, num13, num14, num15) = GetCollisionBounds(tileCollision);
					if (num14 > num12)
					{
						int num16 = num11 * 16 + num15;
						if (num16 > num5)
						{
							num5 = num16;
						}
						break;
					}
					if (IsDeathCollision(tileCollision))
					{
						int num17 = num11 * 16 + 16;
						if (num17 > num5)
						{
							num5 = num17;
						}
						break;
					}
				}
			}
			for (int j = num8 + 1; j < mapHeight - groundRowsToReserve; j++)
			{
				MetatileCollision tileCollision2 = GetTileCollision(i, j);
				if (tileCollision2 == MetatileCollision.COL_NONE)
				{
					continue;
				}
				var (num18, num19, num20, num21) = GetCollisionBounds(tileCollision2);
				if (num20 > num18)
				{
					int num22 = j * 16 + num19;
					if (num22 < num6)
					{
						num6 = num22;
					}
					break;
				}
				if (IsDeathCollision(tileCollision2))
				{
					int num23 = j * 16;
					if (num23 < num6)
					{
						num6 = num23;
					}
					break;
				}
			}
			if (i <= num9)
			{
				continue;
			}
			int num24 = Math.Max(0, num7 - 2);
			int num25 = Math.Min(mapHeight - groundRowsToReserve - 1, num8 + 2);
			for (int k = num24; k <= num25; k++)
			{
				MetatileCollision tileCollision3 = GetTileCollision(i, k);
				if (tileCollision3 == MetatileCollision.COL_NONE)
				{
					continue;
				}
				bool flag = false;
				bool flag2 = false;
				var (num26, num27, num28, num29) = GetCollisionBounds(tileCollision3);
				int num30;
				int num31;
				if (num28 > num26)
				{
					flag = true;
					num30 = k * 16 + num27;
					num31 = k * 16 + num29;
				}
				else
				{
					if (!IsDeathCollision(tileCollision3))
					{
						continue;
					}
					flag2 = true;
					num30 = k * 16;
					num31 = k * 16 + 16;
				}
				if (k >= num7 && k <= num8)
				{
					int num32 = num30 - num5;
					int num33 = num6 - num31;
					if (num32 >= hitboxH && (num32 >= num33 || num33 < hitboxH))
					{
						if (num30 < num6)
						{
							num6 = num30;
						}
					}
					else if (num31 > num5)
					{
						num5 = num31;
					}
				}
				else if (k < num7)
				{
					if (flag && num31 > num5)
					{
						num5 = num31;
					}
					if (flag2 && num31 > num5)
					{
						num5 = num31;
					}
				}
				else if (k > num8)
				{
					if (flag && num30 < num6)
					{
						num6 = num30;
					}
					if (flag2 && num30 < num6)
					{
						num6 = num30;
					}
				}
			}
		}
		return (num5 + num6) / 2 - hitboxH / 2;
	}

	private static bool IsDeathCollision(MetatileCollision col)
	{
		return SharedPhysics.IsDeathCollision(col);
	}

	private int GreedyShipLookahead(SimState start, int horizon)
	{
		SimState s = start.Clone();
		for (int i = 0; i < horizon; i++)
		{
			int num = FindCorridorCenter(ref s) + _shipCorridorBias;
			SimState s2 = s.Clone();
			SimState s3 = s.Clone();
			bool endLevel;
			bool flag = StepFrame(ref s2, input: true, out endLevel);
			bool endLevel2;
			bool flag2 = StepFrame(ref s3, input: false, out endLevel2);
			if (!flag && !flag2)
			{
				return i;
			}
			if (endLevel)
			{
				return horizon;
			}
			if (endLevel2)
			{
				return horizon;
			}
			bool input;
			if (flag && !flag2)
			{
				input = true;
			}
			else if (!flag && flag2)
			{
				input = false;
			}
			else
			{
				int num2 = s.Y_fixed >> 8;
				int velY_fixed = s.VelY_fixed;
				int num3 = ((s.GravMul > 0) ? (num2 - num) : (num - num2));
				int num4 = -(velY_fixed * s.GravMul);
				int num5 = num3 - num4 * 2;
				input = num5 > 0;
			}
			if (!StepFrame(ref s, input, out var endLevel3))
			{
				return i;
			}
			if (endLevel3)
			{
				return horizon;
			}
		}
		return horizon;
	}

	private (int bestSurv, int bestX) EvaluatePathBFS(SimState startState, int horizon, int maxAlive)
	{
		int num = 0;
		int num2 = startState.X_fixed >> 8;
		List<SimState> list = new List<SimState>();
		list.Add(startState.Clone());
		for (int i = 0; i < horizon; i++)
		{
			if (list.Count <= 0)
			{
				break;
			}
			List<SimState> list2 = new List<SimState>();
			foreach (SimState item in list)
			{
				if (item.VelY_fixed == 0 && item.OnGround && (item.GameMode == 0 || item.GameMode == 2))
				{
					for (int j = 0; j < 2; j++)
					{
						SimState s = item.Clone();
						bool input = j == 0;
						if (s.PendingOrbIndex >= 0 && !_btSkipSpecificOrbs.Contains(s.PendingOrbIndex))
						{
							input = true;
						}
						else if (s.PendingOrbIndex >= 0 && _btSkipSpecificOrbs.Contains(s.PendingOrbIndex))
						{
							s.ProcessedSprites.Add(s.PendingOrbIndex);
							if (s.PendingOrbExtra1Index >= 0)
							{
								s.ProcessedSprites.Add(s.PendingOrbExtra1Index);
							}
							if (s.PendingOrbExtra2Index >= 0)
							{
								s.ProcessedSprites.Add(s.PendingOrbExtra2Index);
							}
							ClearPendingOrbs(ref s);
						}
						bool endLevel;
						bool flag = StepFrame(ref s, input, out endLevel);
						if (endLevel)
						{
							num = horizon;
							num2 = Math.Max(num2, s.X_fixed >> 8);
							continue;
						}
						if (flag)
						{
							list2.Add(s);
							continue;
						}
						int num3 = s.X_fixed >> 8;
						if (i > num || (i == num && num3 > num2))
						{
							num = i;
							num2 = num3;
						}
					}
					continue;
				}
				if (item.GameMode == 0 && CubeWillLandThisFrame(item))
				{
					for (int k = 0; k < 2; k++)
					{
						SimState s2 = item.Clone();
						bool input2 = k == 0;
						if (s2.PendingOrbIndex >= 0 && !_btSkipSpecificOrbs.Contains(s2.PendingOrbIndex))
						{
							input2 = true;
						}
						else if (s2.PendingOrbIndex >= 0 && _btSkipSpecificOrbs.Contains(s2.PendingOrbIndex))
						{
							s2.ProcessedSprites.Add(s2.PendingOrbIndex);
							if (s2.PendingOrbExtra1Index >= 0)
							{
								s2.ProcessedSprites.Add(s2.PendingOrbExtra1Index);
							}
							if (s2.PendingOrbExtra2Index >= 0)
							{
								s2.ProcessedSprites.Add(s2.PendingOrbExtra2Index);
							}
							ClearPendingOrbs(ref s2);
						}
						bool endLevel2;
						bool flag2 = StepFrame(ref s2, input2, out endLevel2);
						if (endLevel2)
						{
							num = horizon;
							num2 = Math.Max(num2, s2.X_fixed >> 8);
							continue;
						}
						if (flag2)
						{
							list2.Add(s2);
							continue;
						}
						int num4 = s2.X_fixed >> 8;
						if (i > num || (i == num && num4 > num2))
						{
							num = i;
							num2 = num4;
						}
					}
					continue;
				}
				SimState s3 = item.Clone();
				bool input3 = s3.PendingOrbIndex >= 0 && !_btSkipSpecificOrbs.Contains(s3.PendingOrbIndex);
				if (s3.PendingOrbIndex >= 0 && _btSkipSpecificOrbs.Contains(s3.PendingOrbIndex))
				{
					s3.ProcessedSprites.Add(s3.PendingOrbIndex);
					if (s3.PendingOrbExtra1Index >= 0)
					{
						s3.ProcessedSprites.Add(s3.PendingOrbExtra1Index);
					}
					if (s3.PendingOrbExtra2Index >= 0)
					{
						s3.ProcessedSprites.Add(s3.PendingOrbExtra2Index);
					}
					ClearPendingOrbs(ref s3);
				}
				bool endLevel3;
				bool flag3 = StepFrame(ref s3, input3, out endLevel3);
				if (endLevel3)
				{
					num = horizon;
					num2 = Math.Max(num2, s3.X_fixed >> 8);
					continue;
				}
				if (flag3)
				{
					list2.Add(s3);
					continue;
				}
				int num5 = s3.X_fixed >> 8;
				if (i > num || (i == num && num5 > num2))
				{
					num = i;
					num2 = num5;
				}
			}
			list = list2;
			if (list.Count > maxAlive)
			{
				Dictionary<(int, int, bool, bool, int, int, int, bool), SimState> dictionary = new Dictionary<(int, int, bool, bool, int, int, int, bool), SimState>();
				foreach (SimState item2 in list)
				{
					(int, int, bool, bool, int, int, int, bool) key = (item2.Y_fixed, item2.VelY_fixed, item2.OnGround, item2.GravFlipped, item2.GameMode, item2.DualActive ? item2.P2_Y_fixed : 0, item2.DualActive ? item2.P2_VelY_fixed : 0, item2.DualActive && item2.P2_GravFlipped);
					if (!dictionary.ContainsKey(key))
					{
						dictionary[key] = item2;
					}
				}
				list = dictionary.Values.ToList();
				if (list.Count > maxAlive)
				{
					list = list.OrderBy((SimState nd) => nd.Y_fixed).ToList();
					List<SimState> list3 = new List<SimState>(maxAlive);
					for (int num6 = 0; num6 < maxAlive; num6++)
					{
						list3.Add(list[(int)((long)num6 * (long)list.Count / maxAlive)]);
					}
					list = list3;
				}
			}
			if (num >= horizon)
			{
				break;
			}
		}
		return (bestSurv: num, bestX: num2);
	}

	private int SimulateForwardWithJumpAt(SimState state, int jumpFrame, bool holdAfterLanding = false, List<(int x, int y)>? pathPoints = null, bool singleJumpOnly = false)
	{
		string deathReason;
		int deathX;
		int deathY;
		return SimulateForwardWithJumpAt(state, jumpFrame, out deathReason, out deathX, out deathY, logTrajectory: false, holdAfterLanding, pathPoints, singleJumpOnly);
	}

	private int SimulateForwardWithJumpAt(SimState state, int jumpFrame, out int finalX_px, bool holdAfterLanding = false, int horizonOverride = -1, List<(int x, int y)>? pathPoints = null, bool singleJumpOnly = false)
	{
		finalX_px = state.X_fixed >> 8;
		SimState s = state.Clone();
		int num = ((s.Mini && !s.GravFlipped) ? 4 : 0);
		pathPoints?.Add(((s.X_fixed >> 8) + 8, (s.Y_fixed >> 8) + num + 8));
		bool flag = jumpFrame < 0;
		bool flag2 = jumpFrame >= 0 && !singleJumpOnly;
		int num2 = ((horizonOverride > 0) ? horizonOverride : 90);
		int num3 = int.MaxValue;
		if (s.VelY_fixed == 0)
		{
			num3 = s.Y_fixed >> 8;
		}
		for (int i = 0; i < num2; i++)
		{
			bool flag3 = false;
			if (!flag)
			{
				flag3 = i >= jumpFrame && s.VelY_fixed == 0;
				if (!flag3 && i >= jumpFrame && jumpFrame >= 0 && s.PendingOrbIndex >= 0 && !_btSkipSpecificOrbs.Contains(s.PendingOrbIndex))
				{
					flag3 = true;
				}
				if (flag3)
				{
					flag = true;
				}
			}
			else if (flag2 && (s.GameMode == 0 || s.GameMode == 2 || s.GameMode == 4) && s.VelY_fixed == 0 && s.OnGround)
			{
				flag3 = holdAfterLanding || QuickDangerCheck(s);
			}
			else if (flag2 && s.GameMode == 4 && s.RobotJumpTime > 0)
			{
				flag3 = holdAfterLanding;
			}
			else if (flag2 && s.GameMode == 6)
			{
				flag3 = holdAfterLanding;
			}
			else if (flag2 && s.GameMode == 7 && !s.Orbed)
			{
				flag3 = QuickDangerCheck(s);
			}
			if (flag2 && flag && !flag3 && s.PendingOrbIndex >= 0)
			{
				if (_btSkipSpecificOrbs.Contains(s.PendingOrbIndex))
				{
					s.ProcessedSprites.Add(s.PendingOrbIndex);
					if (s.PendingOrbExtra1Index >= 0)
					{
						s.ProcessedSprites.Add(s.PendingOrbExtra1Index);
					}
					if (s.PendingOrbExtra2Index >= 0)
					{
						s.ProcessedSprites.Add(s.PendingOrbExtra2Index);
					}
					ClearPendingOrbs(ref s);
				}
				else
				{
					flag3 = true;
				}
			}
			bool endLevel;
			bool flag4 = StepFrame(ref s, flag3, out endLevel);
			int num4 = ((s.Mini && !s.GravFlipped) ? 4 : 0);
			pathPoints?.Add(((s.X_fixed >> 8) + 8, (s.Y_fixed >> 8) + num4 + 8));
			if (s.VelY_fixed == 0)
			{
				int num5 = s.Y_fixed >> 8;
				if (num5 < num3)
				{
					num3 = num5;
				}
			}
			if (!flag4)
			{
				finalX_px = s.X_fixed >> 8;
				_lastSpecMinLandY = num3;
				return i;
			}
			if (endLevel)
			{
				finalX_px = s.X_fixed >> 8;
				_lastSpecMinLandY = num3;
				return num2;
			}
		}
		finalX_px = s.X_fixed >> 8;
		_lastSpecMinLandY = num3;
		return num2;
	}

	private int SimulateForwardWithJumpAt(SimState state, int jumpFrame, out string deathReason, out int deathX, out int deathY, bool logTrajectory = false, bool holdAfterLanding = false, List<(int x, int y)>? pathPoints = null, bool singleJumpOnly = false)
	{
		deathReason = "";
		deathX = 0;
		deathY = 0;
		SimState s = state.Clone();
		int num = ((s.Mini && !s.GravFlipped) ? 4 : 0);
		pathPoints?.Add(((s.X_fixed >> 8) + 8, (s.Y_fixed >> 8) + num + 8));
		bool flag = jumpFrame < 0;
		bool flag2 = jumpFrame >= 0 && !singleJumpOnly;
		bool gravFlipped = s.GravFlipped;
		bool flag3 = s.GameMode == 2;
		bool flag4 = s.GameMode == 5;
		StringBuilder stringBuilder = (logTrajectory ? new StringBuilder() : null);
		for (int i = 0; i < 90; i++)
		{
			bool flag5 = false;
			if (!flag)
			{
				flag5 = i >= jumpFrame && s.VelY_fixed == 0;
				if (!flag5 && i >= jumpFrame && jumpFrame >= 0 && s.PendingOrbIndex >= 0 && !_btSkipSpecificOrbs.Contains(s.PendingOrbIndex))
				{
					flag5 = true;
				}
				if (flag5)
				{
					flag = true;
				}
			}
			else if (flag2 && s.GameMode == 0 && s.VelY_fixed == 0 && s.OnGround)
			{
				flag5 = holdAfterLanding || QuickDangerCheck(s);
			}
			else if (flag2 && s.GameMode == 2 && s.VelY_fixed == 0 && s.OnGround)
			{
				flag5 = holdAfterLanding || QuickDangerCheck(s);
			}
			else if (flag2 && s.GameMode == 4 && s.VelY_fixed == 0 && s.OnGround)
			{
				flag5 = holdAfterLanding || QuickDangerCheck(s);
			}
			else if (flag2 && s.GameMode == 4 && s.RobotJumpTime > 0)
			{
				flag5 = holdAfterLanding;
			}
			else if (flag2 && s.GameMode == 6)
			{
				flag5 = holdAfterLanding;
			}
			else if (flag2 && s.GameMode == 7 && !s.Orbed)
			{
				flag5 = QuickDangerCheck(s);
			}
			if (flag2 && flag && !flag5 && s.PendingOrbIndex >= 0)
			{
				if (_btSkipSpecificOrbs.Contains(s.PendingOrbIndex))
				{
					s.ProcessedSprites.Add(s.PendingOrbIndex);
					if (s.PendingOrbExtra1Index >= 0)
					{
						s.ProcessedSprites.Add(s.PendingOrbExtra1Index);
					}
					if (s.PendingOrbExtra2Index >= 0)
					{
						s.ProcessedSprites.Add(s.PendingOrbExtra2Index);
					}
					ClearPendingOrbs(ref s);
				}
				else
				{
					flag5 = true;
				}
			}
			if (stringBuilder != null)
			{
				StringBuilder stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder3 = stringBuilder2;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(23, 7, stringBuilder2);
				handler.AppendLiteral("f");
				handler.AppendFormatted(i);
				handler.AppendLiteral(":X=");
				handler.AppendFormatted(s.X_fixed >> 8);
				handler.AppendLiteral(",Y=");
				handler.AppendFormatted(s.Y_fixed >> 8);
				handler.AppendLiteral(",V=0x");
				handler.AppendFormatted(s.VelY_fixed, "X");
				handler.AppendLiteral(",G=");
				handler.AppendFormatted(s.OnGround);
				handler.AppendLiteral(",gf=");
				handler.AppendFormatted(s.GravFlipped);
				handler.AppendLiteral(",i=");
				handler.AppendFormatted(flag5);
				handler.AppendLiteral(" ");
				stringBuilder3.Append(ref handler);
			}
			bool endLevel;
			bool flag6 = StepFrame(ref s, flag5, out endLevel);
			int num2 = ((s.Mini && !s.GravFlipped) ? 4 : 0);
			pathPoints?.Add(((s.X_fixed >> 8) + 8, (s.Y_fixed >> 8) + num2 + 8));
			if (!flag6)
			{
				deathReason = _lastDeathReason;
				deathX = _lastDeathX;
				deathY = _lastDeathY;
				if (stringBuilder != null)
				{
					StringBuilder stringBuilder2 = stringBuilder;
					StringBuilder stringBuilder4 = stringBuilder2;
					StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(10, 2, stringBuilder2);
					handler.AppendLiteral("DEAD:X=");
					handler.AppendFormatted(s.X_fixed >> 8);
					handler.AppendLiteral(",Y=");
					handler.AppendFormatted(s.Y_fixed >> 8);
					stringBuilder4.Append(ref handler);
					PfLog($"[SPEC_TRAJ] {stringBuilder}");
				}
				int num3 = ((!flag3 && !flag4 && s.GravFlipped != gravFlipped) ? 90 : 0);
				return i + num3;
			}
			if (endLevel)
			{
				return 90;
			}
		}
		int num4 = ((!flag3 && !flag4 && s.GravFlipped != gravFlipped) ? 90 : 0);
		return 90 + num4;
	}

	private bool QuickDangerCheck(SimState state)
	{
		SimState s = state.Clone();
		int num = s.Y_fixed >> 8;
		for (int i = 0; i < 15; i++)
		{
			if (!StepFrame(ref s, input: false, out var _))
			{
				return true;
			}
		}
		int num2 = s.Y_fixed >> 8;
		int num3 = (s.GravFlipped ? (num - num2) : (num2 - num));
		if (num3 > 16)
		{
			return true;
		}
		return false;
	}

	private bool StepFrame(ref SimState s, bool input, out bool endLevel)
	{
		endLevel = false;
		PfTrace.SetFrameContext(_frameCounter, _dualP2Guard ? 1 : 0, s.GameMode);
		PfTrace.SetSpeculative(_speculativeDepth);
		s.Step2Ejected = false;
		ClearPendingOrbs(ref s);
		int x_fixed = s.X_fixed;
		int num = x_fixed >> 8;
		CurrentSpeculativeVizMode = s.GameMode;
		int num2 = ((_dualP2Guard && _dualP2FrameEntryVelXOverride.HasValue) ? _dualP2FrameEntryVelXOverride.Value : s.VelX_fixed);
		bool flag = input && !s.PrevInputHeld;
		if (s.Dashing != 0 && !input)
		{
			s.VelY_fixed = 0;
			s.Dashing = 0;
		}
		if (s.Orbed && !input)
		{
			s.Orbed = false;
		}
		bool orbHitThisFrame = false;
		endLevel = ProcessSprites(ref s, num, out orbHitThisFrame);
		if (endLevel)
		{
			return true;
		}
		if (!_dualP2Guard)
		{
			if (_p1OrbIndicesThisFrame == null)
			{
				_p1OrbIndicesThisFrame = new List<int>();
			}
			_p1OrbIndicesThisFrame.Clear();
		}
		if (s.PendingOrbIndex >= 0 && input)
		{
			int num3 = num + 1;
			int hitboxW = GetHitboxW(s.Mini);
			int hitboxH = GetHitboxH(s.Mini);
			int miniCenterOffsetY = SharedPhysics.GetMiniCenterOffsetY(s.Mini);
			int num4 = (s.Y_fixed >> 8) + miniCenterOffsetY;
			int num5 = num4 + hitboxH;
			int num6 = num3 + hitboxW;
			for (int i = 0; i < 3; i++)
			{
				int num7;
				int num8;
				switch (i)
				{
				case 0:
					num7 = s.PendingOrbIndex;
					num8 = s.PendingOrbSpriteId;
					break;
				case 1:
					num7 = s.PendingOrbExtra1Index;
					num8 = s.PendingOrbExtra1SpriteId;
					break;
				default:
					num7 = s.PendingOrbExtra2Index;
					num8 = s.PendingOrbExtra2SpriteId;
					break;
				}
				if (num7 < 0)
				{
					continue;
				}
				PfLog($"[ORB_ACTIVATE] slot={i} sid=0x{num8:X2} idx={num7} gravFlipped={s.GravFlipped} mini={s.Mini}");
				if (_speculativeDepth == 0)
				{
					_hitOrbHistory.Add(num7);
				}
				if (IsDashOrb(num8))
				{
					ApplyDashOrb(ref s, num8);
					s.ProcessedSprites.Add(num7);
					if (!_dualP2Guard)
					{
						_p1OrbIndicesThisFrame.Add(num7);
					}
				}
				else if (IsSpiderOrb(num8))
				{
					bool goUp = num8 == 84;
					ApplySpiderTeleport(ref s, goUp);
					s.Orbed = true;
					s.ProcessedSprites.Add(num7);
					if (!_dualP2Guard)
					{
						_p1OrbIndicesThisFrame.Add(num7);
					}
				}
				else
				{
					ApplyOrbSprite(ref s, num8);
					if (IsBlackOrb(num8) && s.GameMode == 5)
					{
						s.BlackOrbed = true;
					}
					if (num8 != 123 && num8 != 124)
					{
						s.ProcessedSprites.Add(num7);
						if (!_dualP2Guard)
						{
							_p1OrbIndicesThisFrame.Add(num7);
						}
					}
				}
				if (num8 == 123 || num8 == 124)
				{
					continue;
				}
				int num9 = SpriteLowerBound(num - 64);
				for (int j = num9; j < _spritesArr.Length; j++)
				{
					ref SpriteEntry reference = ref _spritesArr[j];
					if (reference.AnchorX_px - 16 > num6 + 16)
					{
						break;
					}
					if (reference.SpriteId != num8 || s.ProcessedSprites.Contains(reference.Index))
					{
						continue;
					}
					bool flag2 = num6 >= reference.HitLeft && reference.HitRight >= num3;
					bool flag3 = num5 >= reference.HitTop && reference.HitBottom >= num4;
					if (flag2 && flag3)
					{
						s.ProcessedSprites.Add(reference.Index);
						if (!_dualP2Guard)
						{
							_p1OrbIndicesThisFrame.Add(reference.Index);
						}
					}
				}
			}
			ClearPendingOrbs(ref s);
			orbHitThisFrame = true;
			if (_speculativeDepth == 0)
			{
				_cubeJumpedThisStep = true;
			}
		}
		int num10 = s.X_fixed + s.VelX_fixed;
		if ((num >= 6300 && num <= 6400) || (num >= 6850 && num <= 6900))
		{
			PfLog($"[X_TRACK] oldX=0x{x_fixed:X} ({num}px) velX=0x{s.VelX_fixed:X} newX=0x{num10:X} ({num10 >> 8}px)");
		}
		PfLog($"[PHYSICS] Mode={s.GameMode}, gravity={(s.GravFlipped ? 255 : 0):X2}, mini={(s.Mini ? 1 : 0)}, table_idx={(s.Mini ? 4 : 0)}");
		if (s.GameMode == 1 && s.Mini)
		{
			int value = s.X_fixed >> 8;
			int value2 = s.Y_fixed >> 8;
			PfLog($"[MINISHIP_Y] X={value} Y={value2} velY=0x{s.VelY_fixed & 0xFFFF:X4}");
		}
		if (s.GameMode == 0)
		{
			CubeGravity(ref s);
			if (s.GravFlipped)
			{
				int hitboxW2 = GetHitboxW(s.Mini);
				int hitboxH2 = GetHitboxH(s.Mini);
				int miniCenterOffsetY2 = SharedPhysics.GetMiniCenterOffsetY(s.Mini);
				int collX = s.X_fixed >> 8;
				int collY = (s.Y_fixed >> 8) + miniCenterOffsetY2 - 1;
				var (flag4, num11, _) = CheckCeiling(collX, collY, hitboxW2, hitboxH2);
				if (flag4 && s.VelY_fixed < 0)
				{
					int num12 = num11 - miniCenterOffsetY2 - 1;
					s.Y_fixed = num12 << 8;
					s.VelY_fixed = 0;
					s.OnGround = true;
					s.WasZeroedByCollision = true;
				}
			}
			bool died = false;
			CubeEject(ref s, input, out died);
			if (died)
			{
				PfLog($"[EJECT_DEATH] X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
				if (_speculativeDepth == 0)
				{
					_lastDeathReason = "EJECT_DEATH";
					_lastDeathX = s.X_fixed >> 8;
					_lastDeathY = s.Y_fixed >> 8;
				}
				s.DeathType = 2;
				return false;
			}
			if (CheckCenterPointDeath(ref s))
			{
				PfLog($"[CENTER_DEATH] X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px (post-eject)");
				if (_speculativeDepth == 0)
				{
					_lastDeathReason = "CENTER_DEATH";
					_lastDeathX = s.X_fixed >> 8;
					_lastDeathY = s.Y_fixed >> 8;
				}
				s.DeathType = 3;
				return false;
			}
			if (s.VelY_fixed == 0)
			{
				bool flag5 = false;
				if (input && !s.JBlocked && !s.FBlocked && !s.Orbed)
				{
					flag5 = true;
				}
				else if (flag && (s.JBlocked || s.FBlocked))
				{
					flag5 = true;
				}
				if (flag5)
				{
					s.VelY_fixed = GetJumpVel(s.Mini) * s.GravMul;
					s.OnGround = false;
					if (_speculativeDepth == 0)
					{
						_cubeJumpedThisStep = true;
					}
					PfSlopeJumpCheck(ref s);
					PfLog($"[JUMP] VelY=0x{s.VelY_fixed:X4} gravMul={s.GravMul} mini={s.Mini} jblocked={s.JBlocked}");
				}
			}
			PfUpdateSlopeCounters_Fresh(ref s);
		}
		else if (s.GameMode == 1)
		{
			ShipGravityAndThrust(ref s, input, out var died2);
			if (died2)
			{
				PfLog($"[GRAV_CEIL_SPIKE_DEATH] ship X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
				if (_speculativeDepth == 0)
				{
					_lastDeathReason = "GRAV_CEIL_SPIKE_DEATH";
					_lastDeathX = s.X_fixed >> 8;
					_lastDeathY = s.Y_fixed >> 8;
				}
				s.DeathType = 2;
				return false;
			}
			ShipEject(ref s, input, out var died3);
			if (died3)
			{
				PfLog($"[EJECT_DEATH] ship X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
				if (_speculativeDepth == 0)
				{
					_lastDeathReason = "EJECT_DEATH";
					_lastDeathX = s.X_fixed >> 8;
					_lastDeathY = s.Y_fixed >> 8;
				}
				s.DeathType = 2;
				return false;
			}
			PfUpdateSlopeCounters_Fresh(ref s);
		}
		else if (s.GameMode == 2)
		{
			if (!s.GravFlipped)
			{
				int hitboxW3 = GetHitboxW(s.Mini);
				int hitboxH3 = GetHitboxH(s.Mini);
				int hitboxOffsetY = GetHitboxOffsetY(2, s.Mini, s.GravFlipped);
				int collY2 = (s.Y_fixed >> 8) + hitboxOffsetY + hitboxH3;
				if (CheckFloor(s.X_fixed >> 8, collY2, hitboxW3, 2).spikeDeath)
				{
					PfLog($"[BALL_GROUNDED_SPIKE_DEATH] X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
					_lastDeathReason = "BALL_GROUNDED_SPIKE_DEATH";
					_lastDeathX = s.X_fixed >> 8;
					_lastDeathY = s.Y_fixed >> 8;
					s.DeathType = (byte)((s.DeathType >= 11) ? s.DeathType : 6);
					return false;
				}
			}
			if (s.OnGround && !BallIsGrounded(ref s) && s.SlopeWasOnCounter == 0)
			{
				s.OnGround = false;
			}
			bool flag6 = false;
			if (input && !orbHitThisFrame && s.BallFlipCooldown == 0)
			{
				if (BallIsGrounded(ref s) || s.OnGround)
				{
					flag6 = true;
				}
				else
				{
					s.BallInputBuffer = 8;
				}
			}
			else if (s.BallInputBuffer > 0 && !orbHitThisFrame && s.BallFlipCooldown == 0 && (BallIsGrounded(ref s) || s.OnGround))
			{
				flag6 = true;
			}
			if (flag6)
			{
				s.GravFlipped = !s.GravFlipped;
				s.GravMul = ((!s.GravFlipped) ? 1 : (-1));
				s.VelY_fixed = BallSwitchVel(s.Mini) * s.GravMul;
				s.OnGround = false;
				s.BallFlipCooldown = 1;
				s.BallInputBuffer = 0;
				s.BallCooldownFrames = 2;
				PfLog($"[BALL_FLIP] gravFlipped={s.GravFlipped} gravMul={s.GravMul} VelY=0x{s.VelY_fixed:X4} mini={s.Mini}");
			}
			else if (s.BallInputBuffer > 0)
			{
				s.BallInputBuffer--;
			}
			if (s.BallFlipCooldown != 0 && !input && s.BallInputBuffer == 0)
			{
				s.BallFlipCooldown = 0;
			}
			BallGravityStep(ref s);
			if (s.BallCooldownFrames > 0)
			{
				s.BallCooldownFrames--;
			}
			else
			{
				bool died4 = false;
				BallVelocityZeroing(ref s, out died4);
				if (died4)
				{
					PfLog($"[BALL_VELZERO_DEATH] X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
					_lastDeathReason = "BALL_VELZERO_DEATH";
					_lastDeathX = s.X_fixed >> 8;
					_lastDeathY = s.Y_fixed >> 8;
					s.DeathType = (byte)((s.DeathType >= 11) ? s.DeathType : 6);
					return false;
				}
				bool died5 = false;
				BallEject(ref s, input, out died5);
				if (died5)
				{
					PfLog($"[BALL_EJECT_DEATH] X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
					_lastDeathReason = "BALL_EJECT_DEATH";
					_lastDeathX = s.X_fixed >> 8;
					_lastDeathY = s.Y_fixed >> 8;
					s.DeathType = (byte)((s.DeathType >= 11) ? s.DeathType : 6);
					return false;
				}
				PfUpdateSlopeCounters_Fresh(ref s);
			}
		}
		else if (s.GameMode == 3)
		{
			UfoGravityStep(ref s);
			int hitboxW4 = GetHitboxW(s.Mini);
			int hitboxH4 = GetHitboxH(s.Mini);
			int hitboxOffsetY2 = GetHitboxOffsetY(s.GameMode, s.Mini, s.GravFlipped);
			int collX2 = s.X_fixed >> 8;
			int collY3 = (s.Y_fixed >> 8) + hitboxOffsetY2 - 1;
			var (flag7, num13, _) = CheckCeiling(collX2, collY3, hitboxW4, hitboxH4);
			if (flag7 && s.VelY_fixed < 0)
			{
				s.Y_fixed = num13 - hitboxOffsetY2 << 8;
				s.VelY_fixed = 0;
			}
			ShipEject(ref s, input, out var died6);
			if (died6)
			{
				PfLog($"[EJECT_DEATH] ufo X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
				if (_speculativeDepth == 0)
				{
					_lastDeathReason = "EJECT_DEATH";
					_lastDeathX = s.X_fixed >> 8;
					_lastDeathY = s.Y_fixed >> 8;
				}
				s.DeathType = 2;
				return false;
			}
			PfUpdateSlopeCounters_Fresh(ref s);
			if (flag && !orbHitThisFrame)
			{
				int velY_fixed = UfoJumpVel(s.Mini) * -s.GravMul;
				s.VelY_fixed = velY_fixed;
				PfLog($"[UFO_JUMP] VelY=0x{s.VelY_fixed:X4} gravMul={s.GravMul} mini={s.Mini}");
			}
		}
		else if (s.GameMode == 4)
		{
			if (s.RobotJumpTime > 0 && !s.Orbed)
			{
				s.RobotJumpTime--;
				if (input)
				{
					s.VelY_fixed = -688 * s.GravMul;
					PfLog($"[ROBOT_HOLD] VelY=0x{s.VelY_fixed:X4} time={s.RobotJumpTime}");
				}
				else
				{
					s.RobotJumpTime = 0;
					PfLog("[ROBOT_RELEASE] jump cancelled");
				}
			}
			CubeGravity(ref s);
			if (s.GravFlipped)
			{
				int hitboxW5 = GetHitboxW(s.Mini);
				int hitboxH5 = GetHitboxH(s.Mini);
				int miniCenterOffsetY3 = SharedPhysics.GetMiniCenterOffsetY(s.Mini);
				int collX3 = s.X_fixed >> 8;
				int collY4 = (s.Y_fixed >> 8) + miniCenterOffsetY3 - 1;
				var (flag8, num14, _) = CheckCeiling(collX3, collY4, hitboxW5, hitboxH5);
				if (flag8 && s.VelY_fixed < 0)
				{
					int num15 = num14 - miniCenterOffsetY3 - 1;
					s.Y_fixed = num15 << 8;
					s.VelY_fixed = 0;
					s.OnGround = true;
					s.WasZeroedByCollision = true;
				}
			}
			bool died7 = false;
			CubeEject(ref s, input, out died7);
			if (died7)
			{
				PfLog($"[EJECT_DEATH] robot X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
				if (_speculativeDepth == 0)
				{
					_lastDeathReason = "EJECT_DEATH";
					_lastDeathX = s.X_fixed >> 8;
					_lastDeathY = s.Y_fixed >> 8;
				}
				s.DeathType = 2;
				return false;
			}
			if (CheckCenterPointDeath(ref s))
			{
				PfLog($"[CENTER_DEATH] robot X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
				if (_speculativeDepth == 0)
				{
					_lastDeathReason = "CENTER_DEATH";
					_lastDeathX = s.X_fixed >> 8;
					_lastDeathY = s.Y_fixed >> 8;
				}
				s.DeathType = 3;
				return false;
			}
			if (flag && s.VelY_fixed == 0 && !s.Orbed)
			{
				s.VelY_fixed = -688 * s.GravMul;
				s.RobotJumpTime = 19;
				s.OnGround = false;
				PfSlopeJumpCheck(ref s);
				PfLog($"[ROBOT_JUMP] VelY=0x{s.VelY_fixed:X4} time={s.RobotJumpTime} gravMul={s.GravMul}");
			}
			PfUpdateSlopeCounters_Fresh(ref s);
		}
		else if (s.GameMode == 6)
		{
			PfLog($"[WAVE_PHYS] START X={s.X_fixed >> 8} Y={s.Y_fixed >> 8} velY=0x{s.VelY_fixed:X4} wasZeroed={s.WasZeroedByCollision} mini={s.Mini} grav={s.GravFlipped}");
			int num16 = (s.Mini ? (num2 << 1) : num2);
			if (s.GravFlipped)
			{
				num16 = -num16;
			}
			if (!s.WasZeroedByCollision)
			{
				s.VelY_fixed = num16;
			}
			s.WasZeroedByCollision = false;
			if (input)
			{
				s.VelY_fixed = -s.VelY_fixed;
			}
			PfLog($"[WAVE_PHYS] postCalc velY=0x{s.VelY_fixed:X4} hold={input}");
			s.Y_fixed += s.VelY_fixed;
			PfLog($"[WAVE_PHYS] postMove Y={s.Y_fixed >> 8} Y_fixed=0x{s.Y_fixed:X4}");
			bool died8 = false;
			WaveEject(ref s, input, out died8);
			if (died8)
			{
				PfLog($"[WAVE_EJECT_DEATH] X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px vel=0x{s.VelY_fixed:X4}");
				if (_speculativeDepth == 0)
				{
					_lastDeathReason = "WAVE_EJECT_DEATH";
					_lastDeathX = s.X_fixed >> 8;
					_lastDeathY = s.Y_fixed >> 8;
				}
				s.DeathType = 2;
				return false;
			}
			PfUpdateSlopeCounters_Fresh(ref s);
			if (CheckDeathCollision(ref s))
			{
				PfLog($"[CENTER_DEATH] wave X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
				_lastDeathReason = "CENTER_DEATH";
				_lastDeathX = s.X_fixed >> 8;
				_lastDeathY = s.Y_fixed >> 8;
				s.DeathType = 3;
				return false;
			}
			int num17 = s.X_fixed >> 8;
			int num18 = s.Y_fixed >> 8;
			int num19 = num17 + 4 - 1;
			int num20 = num18 + 4;
			int num21 = num19 / 16;
			int num22 = num20 / 16;
			MetatileCollision tileCollision = GetTileCollision(num21, num22);
			if (tileCollision >= MetatileCollision.COL_SLOPE_RD45 && tileCollision <= MetatileCollision.COL_SLOPE_LU66_TOP && (s.Mini || tileCollision != MetatileCollision.COL_SLOPE_LU45) && (!s.Mini || (tileCollision != MetatileCollision.COL_SLOPE_LU66_TOP && tileCollision != MetatileCollision.COL_SLOPE_LU66_BOT)) && PfSlopeCalc(num19, num20, tileCollision).hit)
			{
				PfLog($"[WAVE_DEATH] center slope X={num17} Y={num18} tile=({num21},{num22}) col={tileCollision}");
				_lastDeathReason = "WAVE_CENTER_SLOPE";
				_lastDeathX = num17;
				_lastDeathY = num18;
				s.DeathType = 3;
				return false;
			}
			if ((s.SlopeWasOnCounter | s.SlopeFrames) == 0)
			{
				int num23 = num17 + 8;
				int num24 = num18 + 4;
				int num25 = num23 / 16;
				int num26 = num24 / 16;
				MetatileCollision tileCollision2 = GetTileCollision(num25, num26);
				if (tileCollision2 >= MetatileCollision.COL_SLOPE_RD45 && tileCollision2 <= MetatileCollision.COL_SLOPE_LU66_TOP && (s.Mini || tileCollision2 != MetatileCollision.COL_SLOPE_LU45) && (!s.Mini || (tileCollision2 != MetatileCollision.COL_SLOPE_LU66_TOP && tileCollision2 != MetatileCollision.COL_SLOPE_LU66_BOT)) && PfSlopeCalc(num23, num24, tileCollision2).hit)
				{
					PfLog($"[WAVE_DEATH] R-edge slope X={num17} Y={num18} tile=({num25},{num26}) col={tileCollision2}");
					_lastDeathReason = "WAVE_REDGE_SLOPE";
					_lastDeathX = num17;
					_lastDeathY = num18;
					s.DeathType = 3;
					return false;
				}
			}
		}
		else if (s.GameMode == 5)
		{
			SpiderGravityStep(ref s);
			int num27 = ((!s.GravFlipped) ? 1 : (-2));
			int offsetY = (s.Y_fixed >> 8) + num27;
			SpiderEject(ref s, offsetY, input, out var died9);
			if (died9)
			{
				s.DeathType = 2;
				return false;
			}
			PfUpdateSlopeCounters_Fresh(ref s);
			s.OnGround = s.VelY_fixed == 0;
			bool flag9 = s.VelY_fixed == 0 && (!s.Orbed || s.BlackOrbed);
			if (input && flag9)
			{
				if (!s.GravFlipped)
				{
					s.GravFlipped = true;
					s.GravMul = -1;
					SpiderScanUp(ref s);
					s.VelY_fixed = 0;
				}
				else
				{
					s.GravFlipped = false;
					s.GravMul = 1;
					SpiderScanDown(ref s);
					s.VelY_fixed = 0;
				}
				s.BlackOrbed = false;
				s.Orbed = true;
				SnapCameraToPlayerY(ref s);
			}
			else if (!input)
			{
				s.BlackOrbed = false;
				s.Orbed = false;
			}
			if (CheckDeathCollision(ref s))
			{
				s.DeathType = 3;
				return false;
			}
		}
		else if (s.GameMode == 7)
		{
			SwingGravityStep(ref s);
			bool died10 = false;
			ShipEject(ref s, input, out died10);
			if (died10)
			{
				s.DeathType = 6;
				return false;
			}
			PfUpdateSlopeCounters_Fresh(ref s);
			if (input && !s.PrevInputHeld && !s.Orbed)
			{
				s.GravFlipped = !s.GravFlipped;
				s.GravMul = ((!s.GravFlipped) ? 1 : (-1));
			}
			s.Orbed = false;
			if (CheckDeathCollision(ref s))
			{
				s.DeathType = 3;
				return false;
			}
		}
		else if (s.GameMode == 8)
		{
			if (s.OnGround && !flag)
			{
				s.NinjaJumps = 3;
			}
			CubeGravity(ref s);
			if (s.GravFlipped)
			{
				int hitboxW6 = GetHitboxW(s.Mini);
				int hitboxH6 = GetHitboxH(s.Mini);
				int miniCenterOffsetY4 = SharedPhysics.GetMiniCenterOffsetY(s.Mini);
				int collX4 = s.X_fixed >> 8;
				int collY5 = (s.Y_fixed >> 8) + miniCenterOffsetY4 - 1;
				var (flag10, num28, _) = CheckCeiling(collX4, collY5, hitboxW6, hitboxH6);
				if (flag10 && s.VelY_fixed < 0)
				{
					int num29 = num28 - miniCenterOffsetY4 - 1;
					s.Y_fixed = num29 << 8;
					s.VelY_fixed = 0;
					s.OnGround = true;
					s.WasZeroedByCollision = true;
				}
			}
			bool died11 = false;
			CubeEject(ref s, input, out died11);
			if (died11)
			{
				PfLog($"[EJECT_DEATH] ninja X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
				if (_speculativeDepth == 0)
				{
					_lastDeathReason = "EJECT_DEATH";
					_lastDeathX = s.X_fixed >> 8;
					_lastDeathY = s.Y_fixed >> 8;
				}
				s.DeathType = 2;
				return false;
			}
			if (CheckCenterPointDeath(ref s))
			{
				PfLog($"[CENTER_DEATH] ninja X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
				if (_speculativeDepth == 0)
				{
					_lastDeathReason = "CENTER_DEATH";
					_lastDeathX = s.X_fixed >> 8;
					_lastDeathY = s.Y_fixed >> 8;
				}
				s.DeathType = 3;
				return false;
			}
			if (flag && s.NinjaJumps > 0 && !orbHitThisFrame && !s.HBlocked && s.Dashing == 0)
			{
				s.VelY_fixed = GetJumpVel(s.Mini) * s.GravMul;
				s.NinjaJumps--;
				s.OnGround = false;
				PfSlopeJumpCheck(ref s);
				PfLog($"[NINJA_JUMP] VelY=0x{s.VelY_fixed:X4} jumpsLeft={s.NinjaJumps} gravMul={s.GravMul} mini={s.Mini}");
			}
			PfUpdateSlopeCounters_Fresh(ref s);
		}
		else if (s.GameMode == 9)
		{
			SwingGravityStep(ref s);
			bool died12 = false;
			BallVelocityZeroing(ref s, out died12);
			if (died12)
			{
				s.DeathType = (byte)((s.DeathType >= 11) ? s.DeathType : 6);
				return false;
			}
			int velY_fixed2 = s.VelY_fixed;
			bool onGround = s.OnGround;
			bool died13 = false;
			BallEject(ref s, input, out died13);
			if (died13)
			{
				s.DeathType = (byte)((s.DeathType >= 11) ? s.DeathType : 6);
				return false;
			}
			if (s.OnGround && s.VelY_fixed == 0 && !orbHitThisFrame)
			{
				int num30 = -velY_fixed2 / 3 * 2;
				int num31 = (s.Mini ? SharedPhysics.PadOrbHeights_Mini[1][7] : SharedPhysics.PadOrbHeights[1][7]);
				if (s.GravFlipped)
				{
					if (num30 < num31)
					{
						num30 = num31;
					}
				}
				else
				{
					int num32 = -num31;
					if (num30 > num32)
					{
						num30 = num32;
					}
				}
				s.VelY_fixed = num30;
				s.OnGround = false;
			}
			PfUpdateSlopeCounters_Fresh(ref s);
			if (flag && !orbHitThisFrame && !s.Orbed)
			{
				int num33 = (s.Mini ? SharedPhysics.PadOrbHeights_Mini[6][7] : SharedPhysics.PadOrbHeights[6][7]);
				s.VelY_fixed = (s.GravFlipped ? num33 : (-num33));
				s.Orbed = true;
			}
			else if (!input)
			{
				s.Orbed = false;
			}
			if (CheckDeathCollision(ref s))
			{
				s.DeathType = 3;
				return false;
			}
		}
		if (_dualP2Guard && s.DualActive)
		{
			s.X_fixed = num10;
		}
		if (CheckFloorSpikes(ref s))
		{
			PfLog($"[FLOOR_SPIKE] X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
			if (_speculativeDepth == 0)
			{
				_lastDeathReason = "FLOOR_SPIKE";
				_lastDeathX = s.X_fixed >> 8;
				_lastDeathY = s.Y_fixed >> 8;
			}
			s.DeathType = 7;
			return false;
		}
		if (s.GameMode == 0 || s.GameMode == 1 || s.GameMode == 2 || s.GameMode == 3 || s.GameMode == 4 || s.GameMode == 5 || s.GameMode == 6 || s.GameMode == 7 || s.GameMode == 8 || s.GameMode == 9 || s.GameMode == 10)
		{
			if (CheckForwardCollision(ref s))
			{
				PfLog($"[FWD_DEATH] X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
				if (_speculativeDepth == 0)
				{
					_lastDeathReason = "FWD_DEATH";
					_lastDeathX = s.X_fixed >> 8;
					_lastDeathY = s.Y_fixed >> 8;
				}
				s.DeathType = 8;
				return false;
			}
			if (s.SlopeWasOnCounter == 0 && s.GameMode != 6 && s.GameMode != 10)
			{
				int hbW = ((s.GameMode == 6 || s.GameMode == 10) ? 8 : GetHitboxW(s.Mini));
				int hbH = ((s.GameMode == 6 || s.GameMode == 10) ? 8 : GetHitboxH(s.Mini));
				int hbOffY = ((s.GameMode != 6 && s.GameMode != 10) ? GetHitboxOffsetY(s.GameMode, s.Mini, s.GravFlipped) : 0);
				int item = SharedPhysics.GetForwardSlopeNudge(in _collisionMap, s.X_fixed >> 8, s.Y_fixed >> 8, hbW, hbH, hbOffY, s.GameMode, s.Mini, s.GravFlipped).nudge;
				if (item != 0)
				{
					s.Y_fixed += item << 8;
				}
			}
		}
		if (!_dualP2Guard)
		{
			if (!s.DualActive && (s.GameMode == 0 || s.GameMode == 4 || s.GameMode == 8 || s.GameMode == 9 || s.NoCamLockForced))
			{
				int num34 = -(groundRowsToReserve * 16) << 8;
				int num35 = s.Y_fixed - s.CameraY_fixed;
				if (num35 < 16384)
				{
					int num36 = 16384 - num35;
					s.CameraY_fixed -= num36;
					if (s.CameraY_fixed < num34)
					{
						s.CameraY_fixed = num34;
					}
				}
				else if (num35 >> 8 >= 160)
				{
					int num37 = num35 - 40960;
					int num38 = Math.Max(0, (mapHeight - 15) * 16) << 8;
					s.CameraY_fixed += num37;
					if (s.CameraY_fixed > num38)
					{
						s.CameraY_fixed = num38;
					}
				}
			}
			else
			{
				if (s.TargetCameraY_fixed > s.CameraY_fixed)
				{
					s.CameraY_fixed += 512;
					if (s.CameraY_fixed > s.TargetCameraY_fixed)
					{
						s.CameraY_fixed = s.TargetCameraY_fixed;
					}
				}
				else if (s.TargetCameraY_fixed < s.CameraY_fixed)
				{
					s.CameraY_fixed -= 768;
					if (s.CameraY_fixed < s.TargetCameraY_fixed)
					{
						s.CameraY_fixed = s.TargetCameraY_fixed;
					}
					s.Y_fixed -= 256;
				}
				int num39 = Math.Max(0, (mapHeight - 15) * 16) << 8;
				int num40 = -(groundRowsToReserve * 16) << 8;
				if (s.CameraY_fixed < num40)
				{
					s.CameraY_fixed = num40;
				}
				if (s.CameraY_fixed > num39)
				{
					s.CameraY_fixed = num39;
				}
			}
		}
		int num41 = s.Y_fixed - s.CameraY_fixed;
		if (!s.WrapMode)
		{
			if (num41 < 1536)
			{
				PfLog($"[OOB_TOP] screenRelY=0x{num41:X4} camY={s.CameraY_fixed >> 8}");
				if (_speculativeDepth == 0)
				{
					_lastDeathReason = "OOB_TOP";
					_lastDeathX = s.X_fixed >> 8;
					_lastDeathY = s.Y_fixed >> 8;
				}
				s.DeathType = 11;
				return false;
			}
			if (num41 > 63744)
			{
				PfLog($"[OOB_BOTTOM] screenRelY=0x{num41:X4} camY={s.CameraY_fixed >> 8}");
				if (_speculativeDepth == 0)
				{
					_lastDeathReason = "OOB_BOTTOM";
					_lastDeathX = s.X_fixed >> 8;
					_lastDeathY = s.Y_fixed >> 8;
				}
				s.DeathType = 11;
				return false;
			}
		}
		else if (num41 < 1536)
		{
			s.Y_fixed = s.CameraY_fixed + 63744;
		}
		else if (num41 > 63744)
		{
			s.Y_fixed = s.CameraY_fixed + 1536;
		}
		if (!_dualP2Guard)
		{
			CheckGravityModTriggersAtNewX(ref s);
		}
		if (CheckDeathCollision(ref s))
		{
			PfLog($"[DEATH_COLL] X(old)={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
			if (_speculativeDepth == 0)
			{
				_lastDeathReason = "DEATH_COLL";
				_lastDeathX = s.X_fixed >> 8;
				_lastDeathY = s.Y_fixed >> 8;
			}
			s.DeathType = 9;
			return false;
		}
		if (CheckSlopePenetrationDeath(ref s))
		{
			PfLog($"[SLOPE_PENETRATION_DEATH] X(old)={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
			if (_speculativeDepth == 0)
			{
				_lastDeathReason = "SLOPE_DEATH";
				_lastDeathX = s.X_fixed >> 8;
				_lastDeathY = s.Y_fixed >> 8;
			}
			s.DeathType = 9;
			return false;
		}
		s.X_fixed = num10;
		if (s.OnGround && s.VelY_fixed == 0 && s.GameMode != 1 && s.GameMode != 3 && s.GameMode != 5 && s.GameMode != 6 && s.GameMode != 7 && s.GameMode != 10 && !VerifyGroundSupport(ref s))
		{
			s.OnGround = false;
			s.WasZeroedByCollision = false;
		}
		int num42 = s.Y_fixed >> 8;
		int num43 = (mapHeight - groundRowsToReserve) * 16;
		int num44 = -(groundRowsToReserve * 16);
		if (num42 < num44 || num42 >= num43 + 16)
		{
			PfLog($"[BOUNDS_DEATH] Y={num42}px worldBottom={num43}");
			if (_speculativeDepth == 0)
			{
				_lastDeathReason = "BOUNDS_DEATH";
				_lastDeathX = s.X_fixed >> 8;
				_lastDeathY = num42;
			}
			s.DeathType = 10;
			return false;
		}
		s.JBlocked = false;
		s.FBlocked = false;
		s.HBlocked = false;
		s.Dblocked = false;
		s.PrevInputHeld = input;
		if (s.DualActive && !_dualP2Guard)
		{
			int y_fixed = s.Y_fixed;
			int num45 = s.VelY_fixed;
			bool flag11 = s.GravFlipped;
			int gravMul = s.GravMul;
			bool flag12 = s.Mini;
			bool wasZeroedByCollision = s.WasZeroedByCollision;
			bool onGround2 = s.OnGround;
			int ballFlipCooldown = s.BallFlipCooldown;
			int ballInputBuffer = s.BallInputBuffer;
			int ballCooldownFrames = s.BallCooldownFrames;
			int robotJumpTime = s.RobotJumpTime;
			int slopeWasOnCounter = s.SlopeWasOnCounter;
			int slopeFrames = s.SlopeFrames;
			int slopeType = s.SlopeType;
			bool orbed = s.Orbed;
			bool blackOrbed = s.BlackOrbed;
			bool prevInputHeld = s.PrevInputHeld;
			int dashing = s.Dashing;
			bool jBlocked = s.JBlocked;
			bool fBlocked = s.FBlocked;
			bool hBlocked = s.HBlocked;
			int ninjaJumps = s.NinjaJumps;
			int pendingOrbIndex = s.PendingOrbIndex;
			int pendingOrbSpriteId = s.PendingOrbSpriteId;
			s.P2_Mini = flag12;
			s.X_fixed = x_fixed;
			s.Y_fixed = s.P2_Y_fixed;
			s.VelY_fixed = s.P2_VelY_fixed;
			s.GravFlipped = s.P2_GravFlipped;
			s.GravMul = s.P2_GravMul;
			s.Mini = s.P2_Mini;
			s.WasZeroedByCollision = s.P2_WasZeroedByCollision;
			s.OnGround = s.P2_OnGround;
			s.BallFlipCooldown = s.P2_BallFlipCooldown;
			s.BallInputBuffer = s.P2_BallInputBuffer;
			s.BallCooldownFrames = s.P2_BallCooldownFrames;
			s.RobotJumpTime = s.P2_RobotJumpTime;
			s.SlopeWasOnCounter = s.P2_SlopeWasOnCounter;
			s.SlopeFrames = s.P2_SlopeFrames;
			s.SlopeType = s.P2_SlopeType;
			s.Orbed = s.P2_Orbed;
			s.BlackOrbed = s.P2_BlackOrbed;
			s.PrevInputHeld = s.P2_PrevInputHeld;
			s.Dashing = s.P2_Dashing;
			s.JBlocked = s.P2_JBlocked;
			s.FBlocked = s.P2_FBlocked;
			s.HBlocked = s.P2_HBlocked;
			s.Dblocked = s.P2_Dblocked;
			s.NinjaJumps = s.P2_NinjaJumps;
			s.PendingOrbIndex = s.P2_PendingOrbIndex;
			s.PendingOrbSpriteId = s.P2_PendingOrbSpriteId;
			s.PendingOrbExtra1Index = s.P2_PendingOrbExtra1Index;
			s.PendingOrbExtra1SpriteId = s.P2_PendingOrbExtra1SpriteId;
			s.PendingOrbExtra2Index = s.P2_PendingOrbExtra2Index;
			s.PendingOrbExtra2SpriteId = s.P2_PendingOrbExtra2SpriteId;
			bool flag13 = false;
			if (s.GameMode == 2 && ballCooldownFrames > 0)
			{
				flag13 = true;
			}
			bool input2 = input && !flag13;
			int y_fixed2 = s.Y_fixed;
			int velY_fixed3 = s.VelY_fixed;
			bool gravFlipped = s.GravFlipped;
			foreach (int item2 in _p1OrbIndicesThisFrame)
			{
				s.ProcessedSprites.Remove(item2);
			}
			_p2OrbFlippedOtherGrav = false;
			int? dualP2FrameEntryVelXOverride = _dualP2FrameEntryVelXOverride;
			_dualP2FrameEntryVelXOverride = num2;
			_dualP2Guard = true;
			bool endLevel2 = false;
			bool flag14;
			try
			{
				flag14 = StepFrame(ref s, input2, out endLevel2);
			}
			finally
			{
				_dualP2Guard = false;
				_dualP2FrameEntryVelXOverride = dualP2FrameEntryVelXOverride;
			}
			foreach (int item3 in _p1OrbIndicesThisFrame)
			{
				s.ProcessedSprites.Add(item3);
			}
			if (_p2OrbFlippedOtherGrav)
			{
				flag11 = !flag11;
				gravMul = ((!flag11) ? 1 : (-1));
				num45 = NesSignedHalf(num45);
				_p2OrbFlippedOtherGrav = false;
				PfLog($"[DUAL_CAP_CHECK] P2 orb flipped P1 grav→{flag11} velY→0x{num45:X4}");
			}
			s.P2_Y_fixed = s.Y_fixed;
			s.P2_VelY_fixed = s.VelY_fixed;
			s.P2_GravFlipped = s.GravFlipped;
			s.P2_GravMul = s.GravMul;
			s.P2_Mini = s.Mini;
			s.P2_WasZeroedByCollision = s.WasZeroedByCollision;
			s.P2_OnGround = s.OnGround;
			s.P2_BallFlipCooldown = s.BallFlipCooldown;
			s.P2_BallInputBuffer = s.BallInputBuffer;
			s.P2_BallCooldownFrames = s.BallCooldownFrames;
			s.P2_RobotJumpTime = s.RobotJumpTime;
			s.P2_SlopeWasOnCounter = s.SlopeWasOnCounter;
			s.P2_SlopeFrames = s.SlopeFrames;
			s.P2_SlopeType = s.SlopeType;
			s.P2_Orbed = s.Orbed;
			s.P2_BlackOrbed = s.BlackOrbed;
			s.P2_PrevInputHeld = s.PrevInputHeld;
			s.P2_Dashing = s.Dashing;
			s.P2_JBlocked = s.JBlocked;
			s.P2_FBlocked = s.FBlocked;
			s.P2_HBlocked = s.HBlocked;
			s.P2_Dblocked = s.Dblocked;
			s.P2_NinjaJumps = s.NinjaJumps;
			s.P2_PendingOrbIndex = s.PendingOrbIndex;
			s.P2_PendingOrbSpriteId = s.PendingOrbSpriteId;
			s.P2_PendingOrbExtra1Index = s.PendingOrbExtra1Index;
			s.P2_PendingOrbExtra1SpriteId = s.PendingOrbExtra1SpriteId;
			s.P2_PendingOrbExtra2Index = s.PendingOrbExtra2Index;
			s.P2_PendingOrbExtra2SpriteId = s.PendingOrbExtra2SpriteId;
			if (s.P2_Mini != flag12)
			{
				flag12 = s.P2_Mini;
			}
			if (!s.DualActive)
			{
				s.X_fixed = num10;
				s.Y_fixed = y_fixed2;
				s.VelY_fixed = velY_fixed3;
				s.GravFlipped = gravFlipped;
				s.GravMul = ((!gravFlipped) ? 1 : (-1));
				s.Mini = flag12;
				s.WasZeroedByCollision = wasZeroedByCollision;
				s.OnGround = onGround2;
				s.BallFlipCooldown = ballFlipCooldown;
				s.BallInputBuffer = ballInputBuffer;
				s.BallCooldownFrames = ballCooldownFrames;
				s.RobotJumpTime = robotJumpTime;
				s.SlopeWasOnCounter = slopeWasOnCounter;
				s.SlopeFrames = slopeFrames;
				s.SlopeType = slopeType;
				s.Orbed = orbed;
				s.BlackOrbed = blackOrbed;
				s.PrevInputHeld = prevInputHeld;
				s.Dashing = dashing;
				s.JBlocked = jBlocked;
				s.FBlocked = fBlocked;
				s.HBlocked = hBlocked;
				s.PendingOrbIndex = pendingOrbIndex;
				s.PendingOrbSpriteId = pendingOrbSpriteId;
				PfLog($"[SINGLE_P2_SYNC] P2 hit single portal — P1 gets P2 state: Y={s.Y_fixed >> 8} VelY=0x{s.VelY_fixed:X4} GravFlipped={s.GravFlipped}");
				if (endLevel2)
				{
					endLevel = true;
					return true;
				}
				if (!flag14)
				{
					return false;
				}
				return true;
			}
			s.X_fixed = num10;
			s.Y_fixed = y_fixed;
			s.VelY_fixed = num45;
			s.GravFlipped = flag11;
			s.GravMul = gravMul;
			s.Mini = flag12;
			s.WasZeroedByCollision = wasZeroedByCollision;
			s.OnGround = onGround2;
			s.BallFlipCooldown = ballFlipCooldown;
			s.BallInputBuffer = ballInputBuffer;
			s.BallCooldownFrames = ballCooldownFrames;
			s.RobotJumpTime = robotJumpTime;
			s.SlopeWasOnCounter = slopeWasOnCounter;
			s.SlopeFrames = slopeFrames;
			s.SlopeType = slopeType;
			s.Orbed = orbed;
			s.BlackOrbed = blackOrbed;
			s.PrevInputHeld = prevInputHeld;
			s.Dashing = dashing;
			s.JBlocked = jBlocked;
			s.FBlocked = fBlocked;
			s.HBlocked = hBlocked;
			s.NinjaJumps = ninjaJumps;
			s.PendingOrbIndex = pendingOrbIndex;
			s.PendingOrbSpriteId = pendingOrbSpriteId;
			if (endLevel2)
			{
				endLevel = true;
				return true;
			}
			if (!flag14)
			{
				PfLog($"[DUAL_P2_DEATH] X={s.X_fixed >> 8}px P2_Y={s.P2_Y_fixed >> 8}px deathType={s.DeathType}");
				return false;
			}
		}
		return true;
	}

	private bool CubeWillLandThisFrame(SimState state)
	{
		int gravity = GetGravity(state.Mini);
		int num = gravity * state.GravMul;
		if (state.GravMul > 0 && state.VelY_fixed > maxFallSpeed)
		{
			num = -num;
		}
		else if (state.GravMul < 0 && state.VelY_fixed < -maxFallSpeed)
		{
			num = -num;
		}
		num = (int)((double)num * state.GravityMod);
		int num2 = state.VelY_fixed + num;
		if (!state.GravFlipped)
		{
			if (num2 < 0)
			{
				return false;
			}
			int num3 = state.Y_fixed + num2;
			int hitboxW = GetHitboxW(state.Mini);
			int hitboxH = GetHitboxH(state.Mini);
			int hitboxOffsetY = GetHitboxOffsetY(state.GameMode, state.Mini, state.GravFlipped);
			int collY = (num3 >> 8) + hitboxOffsetY;
			return CheckFloor(state.X_fixed >> 8, collY, hitboxW, hitboxH).hit;
		}
		if (num2 > 0)
		{
			return false;
		}
		int num4 = state.Y_fixed + num2;
		int hitboxW2 = GetHitboxW(state.Mini);
		int hitboxH2 = GetHitboxH(state.Mini);
		int hitboxOffsetY2 = GetHitboxOffsetY(state.GameMode, state.Mini, state.GravFlipped);
		int collY2 = (num4 >> 8) + hitboxOffsetY2;
		return CheckCeiling(state.X_fixed >> 8, collY2, hitboxW2, hitboxH2).hit;
	}

	private void CubeGravity(ref SimState s)
	{
		int velY_fixed = s.VelY_fixed;
		int y_fixed = s.Y_fixed;
		int num = SharedPhysics.GetCubeGravity(s.Mini);
		int num2 = maxFallSpeed;
		if (s.GravFlipped)
		{
			num = -num;
			num2 = -num2;
		}
		int clampMaxY = Math.Max(0, mapHeight * 16 - 16) << 8;
		SharedPhysics.CommonGravityRoutine(ref s.VelY_fixed, ref s.Y_fixed, num, num2, s.GravFlipped ? 255 : 0, s.Dashing, s.GravityMod, 1.0, isFullSpeed: true, s.VelX_fixed, clampMaxY);
		PfLog($"[CUBE_GRAV] velY: 0x{velY_fixed:X4} -> 0x{s.VelY_fixed:X4}, posY: 0x{y_fixed:X4} ({y_fixed >> 8}px) -> 0x{s.Y_fixed:X4} ({s.Y_fixed >> 8}px)");
	}

	private void CubeEject(ref SimState s, bool input, out bool died)
	{
		died = false;
		SharedPhysics.EjectResult ejectResult = SharedPhysics.CubeEject(in _collisionMap, s.X_fixed, s.Y_fixed, s.VelY_fixed, s.VelX_fixed, s.GravFlipped, s.Mini, s.GameMode, input, s.SlopeWasOnCounter, s.SlopeFrames, s.SlopeType, s.SlopeJumpHigher, s.LastSlopeType, s.CameraY_fixed);
		s.Y_fixed = ejectResult.NewY_fixed;
		s.VelY_fixed = ejectResult.NewVelY_fixed;
		s.OnGround = ejectResult.OnGround;
		s.WasZeroedByCollision = ejectResult.WasZeroed;
		s.SlopeType = ejectResult.SlopeType;
		s.SlopeFrames = ejectResult.SlopeFrames;
		s.SlopeWasOnCounter = ejectResult.SlopeWasOnCounter;
		s.SlopeJumpHigher = ejectResult.SlopeJumpHigher;
		s.LastSlopeType = ejectResult.LastSlopeType;
		if (ejectResult.Died)
		{
			died = true;
			s.DeathType = 6;
		}
		if ((s.GameMode != 0 && s.GameMode != 4 && s.GameMode != 8 && s.GameMode != 11) || (!s.HBlocked && !s.FBlocked))
		{
			return;
		}
		if (s.HBlocked && ejectResult.WasZeroed)
		{
			if (_speculativeDepth == 0)
			{
				_step1FireCount++;
			}
			if (!s.GravFlipped)
			{
				s.VelY_fixed = -1;
			}
			else
			{
				s.VelY_fixed = 1;
			}
		}
		int collX = s.X_fixed >> 8;
		int cubeHitboxW = SharedPhysics.GetCubeHitboxW(s.Mini);
		int cubeHitboxH = SharedPhysics.GetCubeHitboxH(s.Mini);
		int hitboxOffsetY = SharedPhysics.GetHitboxOffsetY(s.GameMode, s.Mini, s.GravFlipped);
		if (!s.GravFlipped)
		{
			if ((short)(s.VelY_fixed & 0xFFFF) >= 0)
			{
				return;
			}
			int collY = (s.Y_fixed >> 8) + hitboxOffsetY;
			var (flag, num, _) = CheckCeiling(collX, collY, cubeHitboxW, cubeHitboxH);
			if (flag)
			{
				int num2 = num - hitboxOffsetY;
				s.Y_fixed = num2 << 8;
				s.VelY_fixed = (s.HBlocked ? 1 : 0);
				s.OnGround = !s.HBlocked;
				s.WasZeroedByCollision = !s.HBlocked;
				s.Orbed = false;
				if (s.FBlocked)
				{
					s.GravFlipped = true;
					s.GravMul = -1;
				}
				s.Step2Ejected = true;
				s.Step2Ever = true;
				if (_speculativeDepth == 0)
				{
					_step2FireCount++;
				}
			}
		}
		else
		{
			if ((short)(s.VelY_fixed & 0xFFFF) < 0)
			{
				return;
			}
			int collY2 = (s.Y_fixed >> 8) + hitboxOffsetY;
			var (flag2, num3, _) = CheckFloor(collX, collY2, cubeHitboxW, cubeHitboxH);
			if (flag2)
			{
				int num4 = num3 - cubeHitboxH - hitboxOffsetY;
				s.Y_fixed = num4 << 8;
				s.VelY_fixed = (s.HBlocked ? (-1) : 0);
				s.OnGround = !s.HBlocked;
				s.WasZeroedByCollision = !s.HBlocked;
				s.Orbed = false;
				if (s.FBlocked)
				{
					s.GravFlipped = false;
					s.GravMul = 1;
				}
				s.Step2Ejected = true;
				s.Step2Ever = true;
				if (_speculativeDepth == 0)
				{
					_step2FireCount++;
				}
			}
		}
	}

	private void BallGravityStep(ref SimState s)
	{
		int velY_fixed = s.VelY_fixed;
		int y_fixed = s.Y_fixed;
		int num = BallGravity(s.Mini);
		int num2 = BallMaxFallSpeed(s.Mini);
		if (s.GravFlipped)
		{
			num = -num;
			num2 = -num2;
		}
		int clampMaxY = Math.Max(0, mapHeight * 16 - 16) << 8;
		SharedPhysics.CommonGravityRoutine(ref s.VelY_fixed, ref s.Y_fixed, num, num2, s.GravFlipped ? 255 : 0, s.Dashing, s.GravityMod, 1.0, isFullSpeed: true, s.VelX_fixed, clampMaxY);
		PfLog($"[BALL_GRAV] velY: 0x{velY_fixed:X4} -> 0x{s.VelY_fixed:X4}, posY: 0x{y_fixed:X4} ({y_fixed >> 8}px) -> 0x{s.Y_fixed:X4} ({s.Y_fixed >> 8}px)");
	}

	private void BallVelocityZeroing(ref SimState s, out bool died)
	{
		died = false;
		int hitboxW = GetHitboxW(s.Mini);
		int hitboxH = GetHitboxH(s.Mini);
		int hitboxOffsetY = GetHitboxOffsetY(2, s.Mini, s.GravFlipped);
		int collX = s.X_fixed >> 8;
		if (!s.GravFlipped)
		{
			if (s.VelY_fixed < 0)
			{
				int collY = (s.Y_fixed >> 8) + hitboxOffsetY - 1;
				var (flag, num, _) = CheckCeiling(collX, collY, hitboxW, hitboxH);
				if (flag)
				{
					int num2 = num - hitboxOffsetY;
					s.Y_fixed = num2 << 8;
					s.VelY_fixed = 0;
					PfLog($"[BALL_VEL_ZERO] ceiling zeroed upward velocity, snapped Y to {num2}");
				}
			}
		}
		else if (s.VelY_fixed > 0)
		{
			int num3 = (s.Y_fixed >> 8) + hitboxOffsetY + hitboxH;
			int num4 = 2;
			(bool hit, int surfaceY, bool spikeDeath) tuple2 = CheckFloor(collX, num3 - num4, hitboxW, num4);
			var (flag2, num5, _) = tuple2;
			if (tuple2.spikeDeath)
			{
				died = true;
			}
			else if (flag2)
			{
				int num6 = num5 - hitboxH - hitboxOffsetY;
				s.Y_fixed = num6 << 8;
				s.VelY_fixed = 0;
				PfLog($"[BALL_VEL_ZERO] floor zeroed downward velocity, snapped Y to {num6}");
			}
		}
	}

	private void BallEject(ref SimState s, bool input, out bool died)
	{
		died = false;
		s.OnGround = false;
		SharedPhysics.EjectResult ejectResult = SharedPhysics.BallEject(in _collisionMap, s.X_fixed, s.Y_fixed, s.VelY_fixed, s.VelX_fixed, s.GravFlipped, s.Mini, s.GameMode, input, s.SlopeWasOnCounter, s.SlopeFrames, s.SlopeType, s.SlopeJumpHigher, s.LastSlopeType);
		s.Y_fixed = ejectResult.NewY_fixed;
		s.VelY_fixed = ejectResult.NewVelY_fixed;
		s.OnGround = ejectResult.OnGround;
		s.SlopeType = ejectResult.SlopeType;
		s.SlopeFrames = ejectResult.SlopeFrames;
		s.SlopeWasOnCounter = ejectResult.SlopeWasOnCounter;
		s.SlopeJumpHigher = ejectResult.SlopeJumpHigher;
		s.LastSlopeType = ejectResult.LastSlopeType;
		if (ejectResult.Died)
		{
			died = true;
			s.DeathType = 6;
		}
	}

	private bool BallGroundedProbeSpikeDeath(ref SimState s)
	{
		int num = s.X_fixed >> 8;
		int num2 = s.Y_fixed >> 8;
		int hitboxW = GetHitboxW(s.Mini);
		int hitboxH = GetHitboxH(s.Mini);
		int hitboxOffsetY = GetHitboxOffsetY(2, s.Mini, s.GravFlipped);
		bool flag = SharedPhysics.BallGroundedProbeSpikeDeath(in _collisionMap, num, num2, hitboxW, hitboxH, hitboxOffsetY, s.GravFlipped);
		if (flag)
		{
			PfLog($"[BALL_GROUND_SPIKE] detected at X={num} Y={num2}");
		}
		return flag;
	}

	private bool BallIsGrounded(ref SimState s)
	{
		return SharedPhysics.BallIsGrounded(in _collisionMap, s.X_fixed >> 8, s.Y_fixed >> 8, GetHitboxW(s.Mini), GetHitboxH(s.Mini), GetHitboxOffsetY(2, s.Mini, s.GravFlipped), s.GravFlipped);
	}

	private bool DecideDashHold(SimState state)
	{
		if (_speculativeDepth >= 3)
		{
			return true;
		}
		_speculativeDepth++;
		int num = -1;
		int num2 = 0;
		for (int i = 0; i <= 40; i++)
		{
			SimState s = state.Clone();
			bool flag = true;
			int num3 = 0;
			bool flag2 = false;
			for (int j = 0; j < i && flag; j++)
			{
				flag = StepFrame(ref s, input: true, out var endLevel);
				if (endLevel)
				{
					num3 = 90;
					break;
				}
				if (flag)
				{
					num3++;
				}
				if (s.Dashing == 0)
				{
					flag2 = true;
					break;
				}
			}
			if (flag && num3 < 90 && s.Dashing != 0)
			{
				flag = StepFrame(ref s, input: false, out var endLevel2);
				if (endLevel2)
				{
					num3 = 90;
				}
				else if (flag)
				{
					num3++;
				}
			}
			if (flag && num3 < 90)
			{
				int num4 = 90 - num3;
				for (int k = 0; k < num4 && flag; k++)
				{
					bool input = DecideInput(s);
					flag = StepFrame(ref s, input, out var endLevel3);
					if (endLevel3)
					{
						num3 = 90;
						break;
					}
					if (flag)
					{
						num3++;
					}
				}
			}
			if (num3 > num)
			{
				num = num3;
				num2 = i;
			}
			if (num >= 90 || flag2)
			{
				break;
			}
		}
		_speculativeDepth--;
		return num2 > 0;
	}

	private bool DecideSpiderInput(SimState state, bool isOverrideFrame)
	{
		if (!isOverrideFrame && !_btSuppressJumpUntilAirborne)
		{
			int orbSpriteIndex;
			int num = ScanForOrbOverlap(in state, out orbSpriteIndex);
			if (num >= 0)
			{
				SimState s = state.Clone();
				bool endLevel;
				bool flag = StepFrame(ref s, input: true, out endLevel);
				if (endLevel)
				{
					return true;
				}
				int num2 = 0;
				if (flag)
				{
					for (int i = -1; i < 15; i++)
					{
						int num3 = 1 + SimulateForwardWithJumpAt(s, i);
						if (num3 > num2)
						{
							num2 = num3;
						}
					}
				}
				SimState s2 = state.Clone();
				bool endLevel2;
				bool flag2 = StepFrame(ref s2, input: false, out endLevel2);
				s2.ProcessedSprites.Add(orbSpriteIndex);
				int num4 = 0;
				if (flag2)
				{
					num4 = 1 + Math.Max(SimulateForwardWithJumpAt(s2, -1), SimulateForwardWithJumpAt(s2, 0));
				}
				if (num2 >= num4)
				{
					return true;
				}
				state.ProcessedSprites.Add(orbSpriteIndex);
				return false;
			}
		}
		if (state.VelY_fixed != 0 || state.Orbed)
		{
			return false;
		}
		if (_speculativeDepth >= 3)
		{
			return false;
		}
		_speculativeDepth++;
		List<(int, int)> list = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
		int num5 = SimulateForwardWithJumpAt(state, -1, holdAfterLanding: false, list);
		OnSpeculativePath?.Invoke(list, -1, num5, arg4: false);
		int num6 = 0;
		for (int j = 0; j < 20; j++)
		{
			List<(int, int)> list2 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
			int num7 = SimulateForwardWithJumpAt(state, j, holdAfterLanding: false, list2);
			OnSpeculativePath?.Invoke(list2, j, num7, arg4: false);
			if (num7 > num6)
			{
				num6 = num7;
			}
		}
		_speculativeDepth--;
		PfLog($"[DECIDE_SPIDER] noPress={num5} bestPress={num6}");
		if (num5 >= 90)
		{
			return false;
		}
		int num8 = Math.Max(0, num5 - 3);
		if (num6 > num5 + num8 && num6 >= 2)
		{
			_committedJumpDelay = -1;
			return true;
		}
		return false;
	}

	private bool DecideSwingInput(SimState state, bool isOverrideFrame)
	{
		if (!isOverrideFrame && !_btSuppressJumpUntilAirborne)
		{
			int orbSpriteIndex;
			int num = ScanForOrbOverlap(in state, out orbSpriteIndex);
			if (num >= 0)
			{
				SimState s = state.Clone();
				bool endLevel;
				bool flag = StepFrame(ref s, input: true, out endLevel);
				if (endLevel)
				{
					return true;
				}
				int num2 = 0;
				if (flag)
				{
					for (int i = -1; i < 15; i++)
					{
						int num3 = 1 + SimulateForwardWithJumpAt(s, i);
						if (num3 > num2)
						{
							num2 = num3;
						}
					}
				}
				SimState s2 = state.Clone();
				bool endLevel2;
				bool flag2 = StepFrame(ref s2, input: false, out endLevel2);
				s2.ProcessedSprites.Add(orbSpriteIndex);
				int num4 = 0;
				if (flag2)
				{
					num4 = 1 + Math.Max(SimulateForwardWithJumpAt(s2, -1), SimulateForwardWithJumpAt(s2, 0));
				}
				if (num2 >= num4)
				{
					return true;
				}
				state.ProcessedSprites.Add(orbSpriteIndex);
				return false;
			}
		}
		if (state.Orbed)
		{
			return false;
		}
		if (_speculativeDepth >= 3)
		{
			return false;
		}
		_speculativeDepth++;
		List<(int, int)> list = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
		int num5 = SimulateForwardWithJumpAt(state, -1, holdAfterLanding: false, list);
		OnSpeculativePath?.Invoke(list, -1, num5, arg4: false);
		int num6 = 0;
		for (int j = 0; j < 25; j++)
		{
			List<(int, int)> list2 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
			int num7 = SimulateForwardWithJumpAt(state, j, holdAfterLanding: false, list2);
			OnSpeculativePath?.Invoke(list2, j, num7, arg4: false);
			if (num7 > num6)
			{
				num6 = num7;
			}
		}
		_speculativeDepth--;
		PfLog($"[DECIDE_SWING] noPress={num5} bestPress={num6}");
		if (num6 > num5 && num6 >= 2)
		{
			_committedJumpDelay = -1;
			return true;
		}
		return false;
	}

	private bool DecidePogoInput(SimState state, bool isOverrideFrame)
	{
		if (!isOverrideFrame && !_btSuppressJumpUntilAirborne)
		{
			int orbSpriteIndex;
			int num = ScanForOrbOverlap(in state, out orbSpriteIndex);
			if (num >= 0)
			{
				SimState s = state.Clone();
				bool endLevel;
				bool flag = StepFrame(ref s, input: true, out endLevel);
				if (endLevel)
				{
					return true;
				}
				int num2 = 0;
				if (flag)
				{
					for (int i = -1; i < 15; i++)
					{
						int num3 = 1 + SimulateForwardWithJumpAt(s, i);
						if (num3 > num2)
						{
							num2 = num3;
						}
					}
				}
				SimState s2 = state.Clone();
				bool endLevel2;
				bool flag2 = StepFrame(ref s2, input: false, out endLevel2);
				s2.ProcessedSprites.Add(orbSpriteIndex);
				int num4 = 0;
				if (flag2)
				{
					num4 = 1 + Math.Max(SimulateForwardWithJumpAt(s2, -1), SimulateForwardWithJumpAt(s2, 0));
				}
				if (num2 >= num4)
				{
					return true;
				}
				state.ProcessedSprites.Add(orbSpriteIndex);
				return false;
			}
		}
		if (state.Orbed)
		{
			return false;
		}
		if (_speculativeDepth >= 3)
		{
			return false;
		}
		_speculativeDepth++;
		List<(int, int)> list = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
		int num5 = SimulateForwardWithJumpAt(state, -1, holdAfterLanding: false, list);
		OnSpeculativePath?.Invoke(list, -1, num5, arg4: false);
		int num6 = 0;
		int num7 = -1;
		for (int j = 0; j < 20; j++)
		{
			List<(int, int)> list2 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
			int num8 = SimulateForwardWithJumpAt(state, j, holdAfterLanding: false, list2);
			OnSpeculativePath?.Invoke(list2, j, num8, arg4: false);
			if (num8 > num6)
			{
				num6 = num8;
				num7 = j;
			}
		}
		_speculativeDepth--;
		PfLog($"[DECIDE_POGO] noPress={num5} bestPress={num6} bestDelay={num7}");
		if (num6 > num5 && num6 >= 2)
		{
			_committedJumpDelay = num7;
			return num7 == 0;
		}
		return false;
	}

	private bool DecideBallInput(SimState state, bool isOverrideFrame)
	{
		if (!isOverrideFrame && !_btSuppressJumpUntilAirborne)
		{
			int orbSpriteIndex;
			int num = ScanForOrbOverlap(in state, out orbSpriteIndex);
			if (num >= 0)
			{
				SimState s = state.Clone();
				bool endLevel;
				bool flag = StepFrame(ref s, input: true, out endLevel);
				if (endLevel)
				{
					return true;
				}
				int num2 = 0;
				if (flag)
				{
					int num3 = Math.Min(15, 90);
					for (int i = -1; i < num3; i++)
					{
						int num4 = 1 + SimulateForwardWithJumpAt(s, i);
						if (num4 > num2)
						{
							num2 = num4;
						}
					}
				}
				SimState s2 = state.Clone();
				bool endLevel2;
				bool flag2 = StepFrame(ref s2, input: false, out endLevel2);
				s2.ProcessedSprites.Add(orbSpriteIndex);
				int num5 = 0;
				if (endLevel2)
				{
					num5 = 90;
				}
				else if (flag2)
				{
					int val = 1 + SimulateForwardWithJumpAt(s2, -1);
					int val2 = 1 + SimulateForwardWithJumpAt(s2, 0);
					num5 = Math.Max(val, val2);
				}
				PfLog($"[DECIDE_ORB_BALL] sid=0x{num:X2} orbSurv={num2} skipSurv={num5}");
				if (num2 >= num5)
				{
					return true;
				}
				state.ProcessedSprites.Add(orbSpriteIndex);
				return false;
			}
		}
		if (_committedJumpDelay > 0)
		{
			_committedJumpDelay--;
			if (_committedJumpDelay == 0)
			{
				_committedJumpDelay = -1;
				PfLog("[DECIDE_BALL] committed delay fired \ufffd flipping now");
				return true;
			}
			PfLog($"[DECIDE_BALL] committed delay countdown: {_committedJumpDelay} remaining");
			return false;
		}
		if (state.VelY_fixed != 0 || !state.OnGround)
		{
			return false;
		}
		if (_speculativeDepth >= 3)
		{
			return false;
		}
		_speculativeDepth++;
		List<(int, int)> list = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
		int num6 = SimulateForwardWithJumpAt(state, -1, holdAfterLanding: false, list);
		_speculativeDepth--;
		OnSpeculativePath?.Invoke(list, -1, num6, arg4: false);
		PfLog($"[DECIDE_BALL] noPressFrames={num6}, testing flips (walkFrames={_ballGroundedWalkFrames})");
		int finalX_px = state.X_fixed >> 8;
		_speculativeDepth++;
		SimulateForwardWithJumpAt(state, -1, out finalX_px);
		_speculativeDepth--;
		int num7 = num6;
		int num8 = finalX_px;
		List<(int, int, int)> list2 = new List<(int, int, int)>();
		_speculativeDepth++;
		for (int j = 0; j < 35; j++)
		{
			List<(int, int)> list3 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
			int finalX_px2 = state.X_fixed >> 8;
			int num9 = SimulateForwardWithJumpAt(state, j, out finalX_px2, holdAfterLanding: false, -1, list3);
			OnSpeculativePath?.Invoke(list3, j, num9, arg4: false);
			if (num9 > num7)
			{
				num7 = num9;
			}
			if (finalX_px2 > num8)
			{
				num8 = finalX_px2;
			}
			if (finalX_px2 > finalX_px || num9 > num6)
			{
				list2.Add((j, num9, finalX_px2));
			}
		}
		_speculativeDepth--;
		_speculativeDepth++;
		for (int k = 0; k < 35; k++)
		{
			List<(int, int)> list4 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
			int finalX_px3 = state.X_fixed >> 8;
			int num10 = SimulateForwardWithJumpAt(state, k, out finalX_px3, holdAfterLanding: false, -1, list4, singleJumpOnly: true);
			OnSpeculativePath?.Invoke(list4, k, num10, arg4: false);
			if (num10 > num7)
			{
				num7 = num10;
			}
			if (finalX_px3 > num8)
			{
				num8 = finalX_px3;
			}
			if (finalX_px3 > finalX_px || num10 > num6)
			{
				list2.Add((k, num10, finalX_px3));
			}
		}
		_speculativeDepth--;
		if (list2.Count == 0)
		{
			PfLog("[DECIDE_BALL] no viable flip delays found");
			if (num6 >= 90)
			{
				_speculativeDepth++;
				int finalX_px4;
				int num11 = SimulateForwardWithJumpAt(state, -1, out finalX_px4, holdAfterLanding: false, 180);
				_speculativeDepth--;
				if (num11 < 180)
				{
					PfLog($"[DECIDE_BALL] Phase 2 extended: no-press dies at extSurv={num11} extX={finalX_px4}, re-evaluating flips");
					_speculativeDepth++;
					for (int l = 0; l < 35; l++)
					{
						List<(int, int)> list5 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
						int finalX_px5;
						int num12 = SimulateForwardWithJumpAt(state, l, out finalX_px5, holdAfterLanding: false, 180, list5);
						OnSpeculativePath?.Invoke(list5, l, num12, arg4: false);
						if (finalX_px5 > finalX_px4 || num12 > num11)
						{
							list2.Add((l, num12, finalX_px5));
							if (num12 > num7)
							{
								num7 = num12;
							}
							if (finalX_px5 > num8)
							{
								num8 = finalX_px5;
							}
						}
					}
					_speculativeDepth--;
				}
			}
			if (list2.Count == 0)
			{
				return false;
			}
		}
		if (PreferCoins && allCoins.Count > 0 && list2.Count > 0)
		{
			int num13 = state.X_fixed >> 8;
			for (int m = _nextCoinCheckIdx; m < allCoins.Count; m++)
			{
				SpriteEntry spriteEntry = allCoins[m];
				if (state.ProcessedSprites.Contains(spriteEntry.Index) || _forgivenCoins.Contains(spriteEntry.Index))
				{
					continue;
				}
				if (spriteEntry.HitLeft > num13 + 800)
				{
					break;
				}
				if (spriteEntry.HitRight < num13)
				{
					continue;
				}
				List<(int, int, int)> list6 = new List<(int, int, int)>();
				Dictionary<int, (int survival, int xProgress)> viableLookup = new Dictionary<int, (int, int)>();
				foreach (var (key, item, item2) in list2)
				{
					viableLookup[key] = (item, item2);
				}
				int num14 = 200 * state.VelX_fixed / 256;
				int num15 = (spriteEntry.HitLeft + spriteEntry.HitRight) / 2;
				int num16 = num15 - num13;
				int num17 = Math.Max(35, 200);
				if (num16 <= num14)
				{
					_speculativeDepth++;
					int num18 = int.MaxValue;
					int num19 = int.MaxValue;
					int value = -1;
					int num20 = -1;
					int num21 = 0;
					int num22 = 0;
					int value2 = 0;
					int value3 = 0;
					int value4 = -1;
					int num23 = 0;
					for (int n = 0; n < num17; n++)
					{
						SimState s3 = state.Clone();
						bool flag3 = false;
						bool flag4 = false;
						int num24 = 0;
						int num25 = -1;
						for (int num26 = 0; num26 < 200; num26++)
						{
							bool flag5 = false;
							if (!flag4)
							{
								if (num26 == n)
								{
									flag5 = true;
									flag4 = true;
								}
							}
							else if (s3.VelY_fixed == 0 && s3.OnGround)
							{
								flag5 = QuickDangerCheck(s3);
							}
							if (flag4 && !flag5 && s3.PendingOrbIndex >= 0)
							{
								flag5 = true;
							}
							if (!StepFrame(ref s3, flag5, out var endLevel3))
							{
								num24 = num26;
								if (num26 > num23)
								{
									num23 = num26;
									value2 = s3.X_fixed >> 8;
									value3 = s3.Y_fixed >> 8;
									value4 = n;
								}
								if (flag3 && num26 - num25 < 10)
								{
									flag3 = false;
								}
								break;
							}
							if (endLevel3)
							{
								num24 = 200;
								break;
							}
							int num27 = (s3.X_fixed >> 8) + 1;
							int hitboxW = GetHitboxW(s3.Mini);
							int hitboxH = GetHitboxH(s3.Mini);
							int hitboxOffsetY = GetHitboxOffsetY(s3.GameMode, s3.Mini, s3.GravFlipped);
							int num28 = (s3.Y_fixed >> 8) + hitboxOffsetY;
							if (!flag3 && num27 + hitboxW >= spriteEntry.HitLeft && spriteEntry.HitRight >= num27 && num28 + hitboxH >= spriteEntry.HitTop && spriteEntry.HitBottom >= num28)
							{
								flag3 = true;
								num25 = num26;
							}
							int val3 = Math.Max(spriteEntry.HitLeft - (num27 + hitboxW), num27 - spriteEntry.HitRight);
							int val4 = Math.Max(spriteEntry.HitTop - (num28 + hitboxH), num28 - spriteEntry.HitBottom);
							int num29 = Math.Max(val3, 0) + Math.Max(val4, 0);
							long num30 = (long)num18 + (long)num19;
							if (num29 < num30 || (num29 == num30 && num26 < num20))
							{
								num18 = Math.Max(val3, 0);
								num19 = Math.Max(val4, 0);
								value = n;
								num20 = num26;
							}
							if (num26 + 1 > num22)
							{
								num22 = num26 + 1;
							}
							num24 = num26 + 1;
						}
						if (!flag3 && num24 < 200)
						{
							num21++;
						}
						if (flag3)
						{
							if (viableLookup.TryGetValue(n, out (int, int) value5))
							{
								list6.Add((n, value5.Item1, value5.Item2));
							}
							else
							{
								list6.Add((n, num24, s3.X_fixed >> 8));
							}
						}
					}
					_speculativeDepth--;
					if (list6.Count == 0 && num16 <= num14)
					{
						_log.WriteLine($"[BALL_COIN_MISS_DETAIL] idx={spriteEntry.Index} playerX={num13} playerY={state.Y_fixed >> 8} gravFlip={state.GravFlipped} coinHit=({spriteEntry.HitLeft},{spriteEntry.HitTop})-({spriteEntry.HitRight},{spriteEntry.HitBottom}) closestXDist={num18} closestYDist={num19} closestDelay={value} closestFrame={num20} diedCount={num21}/{num17} maxFrame={num22} deathX={value2} deathY={value3} deathDelay={value4}");
					}
				}
				if (list6.Count > 0)
				{
					int value6 = list6.Count<(int, int, int)>(((int delay, int survival, int xProgress) cd) => viableLookup.ContainsKey(cd.delay));
					PfLog($"[BALL_COIN] {list6.Count}/{num17} delays collect coin idx={spriteEntry.Index} ({value6} also viable) \ufffd preferring coin delays");
					_log.WriteLine($"[BALL_COIN_FOUND] idx={spriteEntry.Index} {list6.Count}/{num17} delays collect coin ({value6} viable) at playerX={num13} playerY={state.Y_fixed >> 8} gravFlip={state.GravFlipped} delays=[{string.Join(",", list6.Select<(int, int, int), int>(((int delay, int survival, int xProgress) cd) => cd.delay).Take(10))}]");
					list2 = list6;
				}
				if (list6.Count == 0)
				{
					_speculativeDepth++;
					SimState s4 = state.Clone();
					bool flag6 = false;
					for (int num31 = 0; num31 < 200; num31++)
					{
						if (!StepFrame(ref s4, input: false, out var endLevel4))
						{
							break;
						}
						if (endLevel4)
						{
							break;
						}
						int num32 = (s4.X_fixed >> 8) + 1;
						int hitboxW2 = GetHitboxW(s4.Mini);
						int hitboxH2 = GetHitboxH(s4.Mini);
						int hitboxOffsetY2 = GetHitboxOffsetY(s4.GameMode, s4.Mini, s4.GravFlipped);
						int num33 = (s4.Y_fixed >> 8) + hitboxOffsetY2;
						if (num32 + hitboxW2 >= spriteEntry.HitLeft && spriteEntry.HitRight >= num32 && num33 + hitboxH2 >= spriteEntry.HitTop && spriteEntry.HitBottom >= num33)
						{
							flag6 = true;
							break;
						}
					}
					_speculativeDepth--;
					if (flag6)
					{
						PfLog($"[BALL_COIN] No-press collects coin idx={spriteEntry.Index}, no flip delays do \ufffd returning false");
						return false;
					}
				}
				if (list6.Count != 0 || spriteEntry.HitLeft <= num13 + 20)
				{
					break;
				}
				int num34 = (spriteEntry.HitTop + spriteEntry.HitBottom) / 2;
				int num35 = state.Y_fixed >> 8;
				bool flag7 = num34 < num35;
				bool flag8 = !state.GravFlipped;
				if (flag7 && flag8)
				{
					int num36 = (spriteEntry.HitLeft + spriteEntry.HitRight) / 2;
					int num37 = (num36 - num13) * 256 / Math.Max(state.VelX_fixed, 1);
					if (num6 >= num37)
					{
						_log.WriteLine($"[BALL_COIN_APPROACH] idx={spriteEntry.Index} suppress flip, walk forward playerX={num13} coinX={num36} dist={num36 - num13} noPressFrames={num6}");
						return false;
					}
					_speculativeDepth++;
					int num38 = SimulateForwardWithJumpAt(state, 0);
					_speculativeDepth--;
					if (num38 >= num37)
					{
						_log.WriteLine($"[BALL_COIN_APPROACH] idx={spriteEntry.Index} FLIP to ceiling (floor dies at {num6}, ceiling survives {num38}, need {num37}) playerX={num13}");
						return true;
					}
					if (num38 >= num6)
					{
						_log.WriteLine($"[BALL_COIN_APPROACH] idx={spriteEntry.Index} FLIP to ceiling (floor dies at {num6}, ceiling dies at {num38}, need {num37} \ufffd ceiling at least as good) playerX={num13}");
						return true;
					}
					_log.WriteLine($"[BALL_COIN_APPROACH] idx={spriteEntry.Index} suppress flip (ceiling dies at {num38} < floor {num6}, need {num37}) playerX={num13}");
					return false;
				}
				bool flag9 = num34 > num35;
				bool gravFlipped = state.GravFlipped;
				if (flag9 && gravFlipped)
				{
					int num39 = (spriteEntry.HitLeft + spriteEntry.HitRight) / 2;
					int num40 = (num39 - num13) * 256 / Math.Max(state.VelX_fixed, 1);
					if (num6 >= num40)
					{
						_log.WriteLine($"[BALL_COIN_APPROACH] idx={spriteEntry.Index} suppress flip (ceiling), walk forward playerX={num13} coinX={num39} dist={num39 - num13} noPressFrames={num6}");
						return false;
					}
					break;
				}
				break;
			}
		}
		int xThreshold = num8 - 8;
		List<(int, int, int)> list7 = list2.Where<(int, int, int)>(((int delay, int survival, int xProgress) d) => d.xProgress >= xThreshold).ToList();
		if (list7.Count == 0)
		{
			list7 = list2;
		}
		int value7 = (int)(JumpTimingBias * (double)(list7.Count - 1));
		value7 = Math.Clamp(value7, 0, list7.Count - 1);
		int item3 = list7[value7].Item1;
		PfLog($"[DECIDE_BALL] viable={list2.Count} best=[{string.Join(",", list7.Select<(int, int, int), string>(((int delay, int survival, int xProgress) d) => $"d{d.delay}:s{d.survival}:x{d.xProgress}"))}] bestSurvival={num7} bestX={num8} picking delay={item3}");
		if (item3 > 0)
		{
			_committedJumpDelay = item3;
			return false;
		}
		return true;
	}

	private bool DecideUfoInput(SimState state, bool isOverrideFrame)
	{
		if (!isOverrideFrame && !_btSuppressJumpUntilAirborne)
		{
			int orbSpriteIndex;
			int num = ScanForOrbOverlap(in state, out orbSpriteIndex);
			if (num >= 0)
			{
				SimState s = state.Clone();
				bool endLevel;
				bool flag = StepFrame(ref s, input: true, out endLevel);
				if (endLevel)
				{
					return true;
				}
				int num2 = 0;
				if (flag)
				{
					int num3 = Math.Min(15, 90);
					for (int i = -1; i < num3; i++)
					{
						int num4 = 1 + SimulateForwardWithJumpAt(s, i);
						if (num4 > num2)
						{
							num2 = num4;
						}
					}
				}
				SimState s2 = state.Clone();
				s2.ProcessedSprites.Add(orbSpriteIndex);
				bool endLevel2;
				bool flag2 = StepFrame(ref s2, input: false, out endLevel2);
				if (endLevel2)
				{
					return false;
				}
				int num5 = 0;
				if (flag2)
				{
					int num6 = Math.Min(15, 90);
					for (int j = -1; j < num6; j++)
					{
						int num7 = 1 + SimulateForwardWithJumpAt(s2, j);
						if (num7 > num5)
						{
							num5 = num7;
						}
					}
				}
				bool flag3 = num2 >= num5;
				PfLog($"[DECIDE_UFO_ORB] orbSurv={num2} skipSurv={num5} ? {(flag3 ? "ACTIVATE" : "SKIP")}");
				return flag3;
			}
		}
		if (_speculativeDepth >= 3)
		{
			return false;
		}
		_speculativeDepth++;
		List<(int, int)> list = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
		int num8 = SimulateForwardWithJumpAt(state, -1, holdAfterLanding: false, list);
		OnSpeculativePath?.Invoke(list, -1, num8, arg4: false);
		_speculativeDepth--;
		int num9 = num8;
		int num10 = -1;
		_speculativeDepth++;
		int num11 = Math.Min(15, 90);
		for (int k = 0; k < num11; k++)
		{
			List<(int, int)> list2 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
			int num12 = 1 + SimulateForwardWithJumpAt(state, k, holdAfterLanding: false, list2);
			OnSpeculativePath?.Invoke(list2, k, num12, arg4: false);
			if (num12 > num9)
			{
				num9 = num12;
				num10 = k;
			}
		}
		_speculativeDepth--;
		if (num10 < 0)
		{
			return false;
		}
		int num13 = (int)((double)num10 * JumpTimingBias + 0.5);
		if (_committedJumpDelay < 0)
		{
			_committedJumpDelay = num13;
			if (num13 == 0)
			{
				return true;
			}
			return false;
		}
		if (_committedJumpDelay == 0)
		{
			_committedJumpDelay = -1;
			return true;
		}
		_committedJumpDelay--;
		return false;
	}

	private bool DecideRobotInput(SimState state, bool isOverrideFrame)
	{
		if (!isOverrideFrame && !_btSuppressJumpUntilAirborne)
		{
			int orbSpriteIndex;
			int num = ScanForOrbOverlap(in state, out orbSpriteIndex);
			if (num >= 0)
			{
				SimState s = state.Clone();
				bool endLevel;
				bool flag = StepFrame(ref s, input: true, out endLevel);
				if (endLevel)
				{
					return true;
				}
				int num2 = 0;
				if (flag)
				{
					num2 = 1 + SimulateRobotForward(s, 0);
				}
				SimState s2 = state.Clone();
				s2.ProcessedSprites.Add(orbSpriteIndex);
				bool endLevel2;
				bool flag2 = StepFrame(ref s2, input: false, out endLevel2);
				if (endLevel2)
				{
					return false;
				}
				int num3 = 0;
				if (flag2)
				{
					num3 = 1 + SimulateRobotForward(s2, 0);
				}
				bool flag3 = num2 >= num3;
				PfLog($"[DECIDE_ROBOT_ORB] orbSurv={num2} skipSurv={num3} → {(flag3 ? "ACTIVATE" : "SKIP")}");
				return flag3;
			}
		}
		if (_committedRobotHold > 0)
		{
			_committedRobotHold--;
			return true;
		}
		if (state.VelY_fixed != 0)
		{
			return false;
		}
		if (_speculativeDepth >= 3)
		{
			return false;
		}
		_speculativeDepth++;
		List<(int, int)> list = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
		int num4 = SimulateRobotForward(state, 0, list);
		_speculativeDepth--;
		OnSpeculativePath?.Invoke(list, -1, num4, arg4: false);
		int num5 = num4;
		int num6 = 0;
		_speculativeDepth++;
		int[] array = new int[8] { 1, 3, 5, 8, 11, 14, 17, 19 };
		foreach (int num7 in array)
		{
			List<(int, int)> list2 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
			int num8 = SimulateRobotForward(state, num7, list2);
			OnSpeculativePath?.Invoke(list2, num7, num8, arg4: true);
			if (num8 > num5)
			{
				num5 = num8;
				num6 = num7;
			}
		}
		_speculativeDepth--;
		if (num6 == 0)
		{
			return false;
		}
		if (JumpTimingBias < 0.45)
		{
			_speculativeDepth++;
			for (int j = 1; j < num6; j++)
			{
				int num9 = SimulateRobotForward(state, j);
				if (num9 >= num5 - 2)
				{
					num6 = j;
					break;
				}
			}
			_speculativeDepth--;
		}
		else if (JumpTimingBias > 0.55)
		{
			_speculativeDepth++;
			for (int num10 = 19; num10 > num6; num10--)
			{
				int num11 = SimulateRobotForward(state, num10);
				if (num11 >= num5 - 2)
				{
					num6 = num10;
					break;
				}
			}
			_speculativeDepth--;
		}
		_committedRobotHold = num6 - 1;
		PfLog($"[ROBOT_DECIDE] noJumpSurv={num4} bestHold={num6} bestSurv={num5} bias={JumpTimingBias}");
		return true;
	}

	private int SimulateRobotForward(SimState state, int holdFrames, List<(int x, int y)>? pathPoints = null)
	{
		SimState s = state.Clone();
		int num = ((s.Mini && !s.GravFlipped) ? 4 : 0);
		pathPoints?.Add(((s.X_fixed >> 8) + 8, (s.Y_fixed >> 8) + num + 8));
		for (int i = 0; i < 90; i++)
		{
			bool flag = (holdFrames > 0 && i < holdFrames) || (i >= holdFrames && s.VelY_fixed == 0 && s.OnGround && QuickDangerCheck(s));
			if (!flag && s.PendingOrbIndex >= 0 && !_btSkipSpecificOrbs.Contains(s.PendingOrbIndex))
			{
				flag = true;
			}
			if (!StepFrame(ref s, flag, out var endLevel))
			{
				return i;
			}
			if (endLevel)
			{
				return 90;
			}
			int num2 = ((s.Mini && !s.GravFlipped) ? 4 : 0);
			pathPoints?.Add(((s.X_fixed >> 8) + 8, (s.Y_fixed >> 8) + num2 + 8));
		}
		return 90;
	}

	private bool DecideNinjaInput(SimState state, bool isOverrideFrame)
	{
		if (!isOverrideFrame && !_btSuppressJumpUntilAirborne)
		{
			int orbSpriteIndex;
			int num = ScanForOrbOverlap(in state, out orbSpriteIndex);
			if (num >= 0)
			{
				SimState s = state.Clone();
				bool endLevel;
				bool flag = StepFrame(ref s, input: true, out endLevel);
				if (endLevel)
				{
					return true;
				}
				int num2 = 0;
				if (flag)
				{
					num2 = 1 + SimulateNinjaForward(s, null);
				}
				SimState s2 = state.Clone();
				s2.ProcessedSprites.Add(orbSpriteIndex);
				bool endLevel2;
				bool flag2 = StepFrame(ref s2, input: false, out endLevel2);
				if (endLevel2)
				{
					return false;
				}
				int num3 = 0;
				if (flag2)
				{
					num3 = 1 + SimulateNinjaForward(s2, null);
				}
				bool flag3 = num2 >= num3;
				PfLog($"[DECIDE_NINJA_ORB] orbSurv={num2} skipSurv={num3} → {(flag3 ? "ACTIVATE" : "SKIP")}");
				return flag3;
			}
		}
		if (_committedNinjaJumps.Count > 0)
		{
			if (_ninjaWaitFrames > 0)
			{
				_ninjaWaitFrames--;
				return false;
			}
			_ninjaWaitFrames = _committedNinjaJumps.Dequeue();
			return true;
		}
		bool flag4 = state.VelY_fixed == 0;
		if (!flag4 && state.NinjaJumps <= 0)
		{
			return false;
		}
		if (_speculativeDepth >= 3)
		{
			return false;
		}
		_speculativeDepth++;
		List<(int, int)> list = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
		int num4 = SimulateNinjaForward(state, null, list);
		_speculativeDepth--;
		OnSpeculativePath?.Invoke(list, -1, num4, arg4: false);
		int num5 = num4;
		int[] array = null;
		int num6 = (flag4 ? 3 : state.NinjaJumps);
		_speculativeDepth++;
		List<(int, int)> list2 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
		int num7 = SimulateNinjaForward(state, new int[0], list2);
		OnSpeculativePath?.Invoke(list2, 0, num7, arg4: true);
		if (num7 > num5)
		{
			num5 = num7;
			array = new int[0];
		}
		if (num6 >= 2)
		{
			int[] array2 = new int[5] { 3, 6, 10, 15, 20 };
			foreach (int num8 in array2)
			{
				List<(int, int)> list3 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
				int num9 = SimulateNinjaForward(state, new int[1] { num8 }, list3);
				OnSpeculativePath?.Invoke(list3, num8, num9, arg4: true);
				if (num9 > num5)
				{
					num5 = num9;
					array = new int[1] { num8 };
				}
			}
		}
		if (num6 >= 3)
		{
			int[] array3 = new int[4] { 3, 6, 10, 15 };
			for (int j = 0; j < array3.Length; j++)
			{
				for (int k = 0; k < array3.Length; k++)
				{
					int num10 = array3[j];
					int num11 = array3[k];
					List<(int, int)> list4 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
					int num12 = SimulateNinjaForward(state, new int[2] { num10, num11 }, list4);
					OnSpeculativePath?.Invoke(list4, num10 * 100 + num11, num12, arg4: true);
					if (num12 > num5)
					{
						num5 = num12;
						array = new int[2] { num10, num11 };
					}
				}
			}
		}
		_speculativeDepth--;
		if (array == null)
		{
			return false;
		}
		_committedNinjaJumps.Clear();
		_ninjaWaitFrames = 0;
		for (int l = 0; l < array.Length; l++)
		{
			_committedNinjaJumps.Enqueue(array[l]);
		}
		PfLog($"[NINJA_DECIDE] noJumpSurv={num4} bestSurv={num5} plan=[{string.Join(",", array)}] jumpsAvail={num6}");
		return true;
	}

	private int SimulateNinjaForward(SimState state, int[]? jumpDelays, List<(int x, int y)>? pathPoints = null)
	{
		SimState s = state.Clone();
		int num = ((s.Mini && !s.GravFlipped) ? 4 : 0);
		pathPoints?.Add(((s.X_fixed >> 8) + 8, (s.Y_fixed >> 8) + num + 8));
		int num2 = -1;
		int num3 = 0;
		int num4 = 0;
		if (jumpDelays != null && jumpDelays.Length != 0)
		{
			num4 = jumpDelays[0];
			num2 = num4;
			num3 = 1;
		}
		bool flag = jumpDelays == null;
		int num5 = 0;
		for (int i = 0; i < 90; i++)
		{
			bool flag2 = false;
			if (!flag)
			{
				if (i == 0)
				{
					flag2 = true;
					num5++;
				}
				else if (num2 >= 0 && i == num2)
				{
					flag2 = true;
					num5++;
					if (jumpDelays != null && num3 < jumpDelays.Length)
					{
						num4 += jumpDelays[num3];
						num2 = num4;
						num3++;
					}
					else
					{
						num2 = -1;
					}
				}
			}
			if (!flag2 && s.VelY_fixed == 0 && s.OnGround && (flag || num5 > 0))
			{
				flag2 = QuickDangerCheck(s);
			}
			if (!flag2 && s.PendingOrbIndex >= 0 && !_btSkipSpecificOrbs.Contains(s.PendingOrbIndex))
			{
				flag2 = true;
			}
			if (!StepFrame(ref s, flag2, out var endLevel))
			{
				return i;
			}
			if (endLevel)
			{
				return 90;
			}
			int num6 = ((s.Mini && !s.GravFlipped) ? 4 : 0);
			pathPoints?.Add(((s.X_fixed >> 8) + 8, (s.Y_fixed >> 8) + num6 + 8));
		}
		return 90;
	}

	private bool DecideWaveInput(SimState state, bool isOverrideFrame)
	{
		if (!isOverrideFrame && !_btSuppressJumpUntilAirborne)
		{
			int orbSpriteIndex;
			int num = ScanForOrbOverlap(in state, out orbSpriteIndex);
			if (num >= 0)
			{
				SimState s = state.Clone();
				bool endLevel;
				bool flag = StepFrame(ref s, input: true, out endLevel);
				if (endLevel)
				{
					return true;
				}
				int num2 = (flag ? (1 + SimulateWaveForward(s, hold: true)) : 0);
				SimState s2 = state.Clone();
				s2.ProcessedSprites.Add(orbSpriteIndex);
				bool endLevel2;
				bool flag2 = StepFrame(ref s2, input: false, out endLevel2);
				if (endLevel2)
				{
					return false;
				}
				int num3 = (flag2 ? (1 + SimulateWaveForward(s2, hold: false)) : 0);
				return num2 >= num3;
			}
		}
		if (_speculativeDepth >= 3)
		{
			return false;
		}
		_speculativeDepth++;
		List<(int, int)> list = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
		int num4 = SimulateWaveForward(state, hold: true, list);
		List<(int, int)> list2 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
		int num5 = SimulateWaveForward(state, hold: false, list2);
		_speculativeDepth--;
		OnSpeculativePath?.Invoke(list, 0, num4, arg4: true);
		OnSpeculativePath?.Invoke(list2, -1, num5, arg4: false);
		if (num4 > num5 + 3)
		{
			return true;
		}
		if (num5 > num4 + 3)
		{
			return false;
		}
		int num6 = state.Y_fixed >> 8;
		int num7 = state.X_fixed >> 8;
		int num8 = num7 + 8;
		int num9 = 0;
		for (int i = 1; i <= 80; i++)
		{
			int num10 = num6 - i;
			if (num10 < 0)
			{
				num9 = i;
				break;
			}
			int tileX = num8 / 16;
			int tileY = num10 / 16;
			MetatileCollision tileCollision = GetTileCollision(tileX, tileY);
			if (tileCollision != MetatileCollision.COL_NONE && !SharedPhysics.IsDeathCollision(tileCollision))
			{
				num9 = i;
				break;
			}
			if (i == 80)
			{
				num9 = 80;
			}
		}
		int num11 = 0;
		for (int j = 1; j <= 80; j++)
		{
			int num12 = num6 + 16 + j;
			if (num12 >= mapHeight * 16)
			{
				num11 = j;
				break;
			}
			int tileX2 = num8 / 16;
			int tileY2 = num12 / 16;
			MetatileCollision tileCollision2 = GetTileCollision(tileX2, tileY2);
			if (tileCollision2 != MetatileCollision.COL_NONE && !SharedPhysics.IsDeathCollision(tileCollision2))
			{
				num11 = j;
				break;
			}
			if (j == 80)
			{
				num11 = 80;
			}
		}
		double num13 = 0.5 + (JumpTimingBias - 0.5) * 0.6;
		int num14 = (int)((double)(num9 + num11) * (1.0 - num13));
		int num15 = (int)((double)(num9 + num11) * num13);
		bool flag3;
		if (num9 < num14 - 2)
		{
			flag3 = false;
		}
		else
		{
			if (num11 >= num15 - 2)
			{
				if (num4 > num5)
				{
					return true;
				}
				if (num5 > num4)
				{
					return false;
				}
				return false;
			}
			flag3 = true;
		}
		bool flag4 = !state.GravFlipped;
		return flag3 == flag4;
	}

	private int SimulateWaveForward(SimState state, bool hold, List<(int x, int y)>? pathPoints = null)
	{
		SimState s = state.Clone();
		int num = ((s.Mini && !s.GravFlipped) ? 4 : 0);
		pathPoints?.Add(((s.X_fixed >> 8) + 8, (s.Y_fixed >> 8) + num + 8));
		for (int i = 0; i < 90; i++)
		{
			bool flag = hold;
			if (!flag && s.PendingOrbIndex >= 0 && !_btSkipSpecificOrbs.Contains(s.PendingOrbIndex))
			{
				flag = true;
			}
			if (!StepFrame(ref s, flag, out var endLevel))
			{
				return i;
			}
			if (endLevel)
			{
				return 90;
			}
			int num2 = ((s.Mini && !s.GravFlipped) ? 4 : 0);
			pathPoints?.Add(((s.X_fixed >> 8) + 8, (s.Y_fixed >> 8) + num2 + 8));
		}
		return 90;
	}

	private void WaveEject(ref SimState s, bool input, out bool died)
	{
		died = false;
		int num = s.X_fixed >> 8;
		int num2 = s.Y_fixed >> 8;
		int num3 = num + 4;
		int num4 = ((s.VelY_fixed < 0) ? 2 : (-2));
		int num5 = num2 + num4;
		PfLog($"[WAVE_EJECT] Generic=({num3},{num5}) {8}x{8} velY=0x{s.VelY_fixed:X4} yAdj={num4}");
		if (s.VelY_fixed >= 0)
		{
			int num6 = num + 4;
			int num7 = num2 + -2;
			int num8 = (s.Mini ? 4 : 0);
			int num9 = num7 + 8 - 2 + num8;
			int num10 = 8;
			for (int i = 0; i < 2; i++)
			{
				int num11 = num6 + i * num10;
				int num12 = num11 / 16;
				int num13 = num9 / 16;
				MetatileCollision tileCollision = GetTileCollision(num12, num13);
				if (tileCollision < MetatileCollision.COL_SLOPE_RD45 || tileCollision > MetatileCollision.COL_SLOPE_LU66_TOP || (!s.Mini && tileCollision == MetatileCollision.COL_SLOPE_LU45) || (s.Mini && (tileCollision == MetatileCollision.COL_SLOPE_LU66_TOP || tileCollision == MetatileCollision.COL_SLOPE_LU66_BOT)))
				{
					continue;
				}
				var (flag, num14, num15) = PfSlopeCalc(num11, num9, tileCollision);
				if (!flag)
				{
					continue;
				}
				bool flag2 = (num15 & 4) != 0;
				if ((i == 0 && flag2) || (i == 1 && !flag2))
				{
					continue;
				}
				if (s.Dblocked)
				{
					if (num14 > 0)
					{
						int num16 = s.Y_fixed >> 8;
						num16 -= num14;
						s.Y_fixed = num16 << 8;
					}
					s.VelY_fixed = 0;
					s.WasZeroedByCollision = true;
					PfLog($"[WAVE_EJECT] D slope eject dblocked Y={s.Y_fixed >> 8}");
				}
				else
				{
					PfLog($"[WAVE_DEATH] D slope death X={num} Y={num2} slopeTile=({num12},{num13}) col={tileCollision} probe={i}");
					died = true;
				}
				return;
			}
		}
		else
		{
			int num17 = num + 4;
			int num18 = num2 + 2;
			int num19 = 4;
			int num20 = num18 + num19 + (s.Mini ? 1 : 2);
			int num21 = 8;
			for (int j = 0; j < 2; j++)
			{
				int num22 = num17 + j * num21;
				int num23 = num22 / 16;
				int num24 = num20 / 16;
				MetatileCollision tileCollision2 = GetTileCollision(num23, num24);
				if (tileCollision2 < MetatileCollision.COL_SLOPE_RD45 || tileCollision2 > MetatileCollision.COL_SLOPE_LU66_TOP || (!s.Mini && tileCollision2 == MetatileCollision.COL_SLOPE_LU45) || (s.Mini && (tileCollision2 == MetatileCollision.COL_SLOPE_LU66_TOP || tileCollision2 == MetatileCollision.COL_SLOPE_LU66_BOT)))
				{
					continue;
				}
				var (flag3, num25, num26) = PfSlopeCalc(num22, num20, tileCollision2);
				if (!flag3)
				{
					continue;
				}
				bool flag4 = (num26 & 4) != 0;
				if (!(j == 0 && flag4) && (j != 1 || flag4))
				{
					if (s.Dblocked)
					{
						int num27 = s.Y_fixed >> 8;
						num27 -= num25;
						s.Y_fixed = num27 << 8;
						s.VelY_fixed = 0;
						s.WasZeroedByCollision = true;
						PfLog($"[WAVE_EJECT] U slope eject dblocked Y={s.Y_fixed >> 8}");
					}
					else
					{
						PfLog($"[WAVE_DEATH] U slope death X={num} Y={num2} slopeTile=({num23},{num24}) col={tileCollision2} probe={j}");
						died = true;
					}
					return;
				}
			}
		}
		if (s.VelY_fixed < 0)
		{
			(bool hit, int ceilingBottomY, bool spikeDeath) tuple3 = CheckCeiling(num3, num5, 8, 8);
			var (flag5, num28, _) = tuple3;
			if (tuple3.spikeDeath)
			{
				PfLog($"[WAVE_DEATH] ceiling spike X={num} Y={num2}");
				died = true;
			}
			else if (flag5)
			{
				int tileX = num3 / 16;
				int tileY = (num5 - 1) / 16;
				MetatileCollision tileCollision3 = GetTileCollision(tileX, tileY);
				if (tileCollision3 == MetatileCollision.COL_FLOOR_CEIL)
				{
					s.Dblocked = true;
				}
				PfLog($"[WAVE_EJECT] coll_U hit tile={tileCollision3} dblocked={s.Dblocked} ceilBotY={num28}");
				if (s.Dblocked)
				{
					int num29 = num28 + 1 - num4;
					s.Y_fixed = num29 << 8;
					s.VelY_fixed = 0;
					s.WasZeroedByCollision = true;
					PfLog($"[WAVE_EJECT] UP eject Y={s.Y_fixed >> 8}");
				}
				else
				{
					PfLog($"[WAVE_DEATH] UP non-walkable tile={tileCollision3} X={num} Y={num2} probe=({num3},{num5 - 1})");
					died = true;
				}
			}
		}
		else if (s.VelY_fixed > 0)
		{
			(bool hit, int surfaceY, bool spikeDeath) tuple5 = CheckFloor(num3, num5, 8, 8);
			var (flag6, num30, _) = tuple5;
			if (tuple5.spikeDeath)
			{
				PfLog($"[WAVE_DEATH] floor spike X={num} Y={num2}");
				died = true;
			}
			else if (flag6)
			{
				int tileX2 = num3 / 16;
				int tileY2 = (num5 + 8) / 16;
				MetatileCollision tileCollision4 = GetTileCollision(tileX2, tileY2);
				if (tileCollision4 == MetatileCollision.COL_FLOOR_CEIL)
				{
					s.Dblocked = true;
				}
				PfLog($"[WAVE_EJECT] coll_D hit tile={tileCollision4} dblocked={s.Dblocked} floorTopY={num30}");
				if (s.Dblocked)
				{
					int num31 = num30 - 8 - num4;
					s.Y_fixed = num31 << 8;
					s.VelY_fixed = 0;
					s.WasZeroedByCollision = true;
					PfLog($"[WAVE_EJECT] DOWN eject Y={s.Y_fixed >> 8}");
				}
				else
				{
					PfLog($"[WAVE_DEATH] DOWN non-walkable tile={tileCollision4} X={num} Y={num2} probe=({num3},{num5 + 8})");
					died = true;
				}
			}
		}
		else
		{
			PfLog("[WAVE_EJECT] velY==0 skip collision");
		}
	}

	private void SpiderGravityStep(ref SimState s)
	{
		int num = 75;
		int num2 = 1536;
		if (s.GravFlipped)
		{
			num = -num;
			num2 = -num2;
		}
		int clampMaxY = Math.Max(0, mapHeight * 16 - 16) << 8;
		SharedPhysics.CommonGravityRoutine(ref s.VelY_fixed, ref s.Y_fixed, num, num2, s.GravFlipped ? 255 : 0, s.Dashing, s.GravityMod, 1.0, isFullSpeed: true, s.VelX_fixed, clampMaxY);
	}

	private void SpiderEject(ref SimState s, int offsetY, bool input, out bool died)
	{
		died = false;
		int width = (s.Mini ? 8 : 15);
		int num = (s.Mini ? 7 : 15);
		int num2 = (s.Mini ? (16 - num >> 1) : 0);
		int playerX_px = s.X_fixed >> 8;
		int num3 = offsetY + num2;
		PfUpdateSlopeCounters(ref s);
		if (!s.GravFlipped)
		{
			var (flag, num4, slopeType) = PfCheckSlopes(ref s, input);
			if (flag)
			{
				if (num4 > 0)
				{
					s.Y_fixed = (s.Y_fixed >> 8) - num4 << 8;
				}
				s.VelY_fixed = 0;
				s.WasZeroedByCollision = true;
				s.SlopeFrames = 1;
				s.SlopeWasOnCounter = 3;
				s.SlopeType = slopeType;
			}
			else
			{
				var (flag2, num5) = BgCollD_Spider(playerX_px, num3, width, num, useEjectProbes: true);
				if (flag2)
				{
					s.Y_fixed = (s.Y_fixed >> 8) - num5 << 8;
					s.VelY_fixed = 0;
					s.WasZeroedByCollision = true;
				}
				else
				{
					s.WasZeroedByCollision = false;
				}
			}
		}
		else
		{
			var (flag3, num6) = BgCollU_Spider(playerX_px, num3, width, num, useEjectProbes: true);
			if (flag3)
			{
				s.Y_fixed = num3 + num6 << 8;
				s.VelY_fixed = 0;
				s.WasZeroedByCollision = true;
			}
			else
			{
				s.WasZeroedByCollision = false;
			}
		}
	}

	private void SpiderScanUp(ref SimState s)
	{
		int width = (s.Mini ? 8 : 15);
		int height = (s.Mini ? 8 : 15);
		int playerX_px = s.X_fixed >> 8;
		int num = s.Y_fixed >> 8;
		for (int i = 0; i < 200; i++)
		{
			num -= 8;
			if (num <= -(groundRowsToReserve * 16))
			{
				num = 0;
				break;
			}
			var (flag, num2) = BgCollU_Spider(playerX_px, num, width, height);
			if (flag)
			{
				num += num2;
				break;
			}
		}
		s.Y_fixed = num << 8;
	}

	private void SpiderScanDown(ref SimState s)
	{
		int width = (s.Mini ? 8 : 15);
		int num = (s.Mini ? 7 : 15);
		int num2 = (s.Mini ? (16 - num >> 1) : 0);
		int playerX_px = s.X_fixed >> 8;
		int num3 = s.Y_fixed >> 8;
		int num4 = (mapHeight - groundRowsToReserve) * 16 - num;
		for (int i = 0; i < 200; i++)
		{
			num3 += 8;
			if (num3 >= num4)
			{
				num3 = num4;
				break;
			}
			var (flag, num5) = BgCollD_Spider(playerX_px, num3 + num2, width, num);
			if (flag)
			{
				num3 -= num5;
				break;
			}
		}
		s.Y_fixed = num3 << 8;
	}

	private (bool hit, int ejectAmount) BgCollD_Spider(int playerX_px, int playerY_px, int width, int height, bool useEjectProbes = false)
	{
		int num = playerY_px + height;
		int num2 = num / 16;
		int num3 = num2 + _collisionMap.GroundRowsToReserve;
		if (num3 >= _collisionMap.MapHeight)
		{
			int num4 = (mapHeight - groundRowsToReserve) * 16;
			return (hit: true, ejectAmount: num - num4);
		}
		if (num3 < 0)
		{
			return (hit: false, ejectAmount: 0);
		}
		int[] array = ((!useEjectProbes) ? new int[2]
		{
			playerX_px + 3,
			playerX_px + width - 3
		} : new int[3]
		{
			playerX_px,
			playerX_px + (width >> 1),
			playerX_px + width
		});
		int[] array2 = array;
		foreach (int num5 in array2)
		{
			int num6 = num5 / 16;
			if (num6 < 0 || num6 >= _collisionMap.MapWidth)
			{
				continue;
			}
			int num7 = num3 * _collisionMap.MapWidth + num6;
			if (num7 < 0 || num7 >= _collisionMap.Tiles.Length)
			{
				continue;
			}
			MetatileCollision collision = MetatileCollisionTable.GetCollision((byte)SharedPhysics.MapTileForCollision(_collisionMap.Tiles[num7]));
			if (IsSolidForSpider(collision))
			{
				int item = SharedPhysics.GetCollisionBounds(collision).top;
				int num8 = (num3 - _collisionMap.GroundRowsToReserve) * 16;
				int num9 = num8 + item;
				if (num >= num9)
				{
					return (hit: true, ejectAmount: num - num9);
				}
			}
		}
		return (hit: false, ejectAmount: 0);
	}

	private (bool hit, int ejectAmount) BgCollU_Spider(int playerX_px, int playerY_px, int width, int height, bool useEjectProbes = false)
	{
		int num = playerY_px / 16;
		int num2 = num + _collisionMap.GroundRowsToReserve;
		if (num2 < 0)
		{
			return (hit: true, ejectAmount: -playerY_px);
		}
		if (num2 >= _collisionMap.MapHeight)
		{
			return (hit: false, ejectAmount: 0);
		}
		int[] array = ((!useEjectProbes) ? new int[2]
		{
			playerX_px + 3,
			playerX_px + width - 3
		} : new int[3]
		{
			playerX_px,
			playerX_px + (width >> 1),
			playerX_px + width
		});
		int[] array2 = array;
		foreach (int num3 in array2)
		{
			int num4 = num3 / 16;
			if (num4 < 0 || num4 >= _collisionMap.MapWidth)
			{
				continue;
			}
			int num5 = num2 * _collisionMap.MapWidth + num4;
			if (num5 >= 0 && num5 < _collisionMap.Tiles.Length)
			{
				MetatileCollision collision = MetatileCollisionTable.GetCollision((byte)SharedPhysics.MapTileForCollision(_collisionMap.Tiles[num5]));
				if (IsSolidForSpider(collision))
				{
					int item = SharedPhysics.GetCollisionBounds(collision).bottom;
					int num6 = (num2 - _collisionMap.GroundRowsToReserve) * 16;
					int num7 = num6 + item;
					return (hit: true, ejectAmount: num7 - playerY_px);
				}
			}
		}
		return (hit: false, ejectAmount: 0);
	}

	private static bool IsSolidForSpider(MetatileCollision col)
	{
		return col != MetatileCollision.COL_NONE && !SharedPhysics.IsDeathCollision(col);
	}

	private void SwingGravityStep(ref SimState s)
	{
		int num = (s.Mini ? 56 : 50);
		int num2 = 1072;
		if (s.GravFlipped)
		{
			num = -num;
			num2 = -num2;
		}
		int clampMaxY = Math.Max(0, mapHeight * 16 - 16) << 8;
		SharedPhysics.CommonGravityRoutine(ref s.VelY_fixed, ref s.Y_fixed, num, num2, s.GravFlipped ? 255 : 0, s.Dashing, s.GravityMod, 1.0, isFullSpeed: true, s.VelX_fixed, clampMaxY);
	}

	private void UfoGravityStep(ref SimState s)
	{
		int num = UfoGravity(s.Mini);
		int num2 = UfoMaxFallSpeed(s.Mini);
		if (s.GravFlipped)
		{
			num = -num;
			num2 = -num2;
		}
		SharedPhysics.CommonGravityRoutine(ref s.VelY_fixed, ref s.Y_fixed, num, num2, s.GravFlipped ? 255 : 0, s.Dashing, s.GravityMod, 1.0, isFullSpeed: true, s.VelX_fixed, int.MaxValue);
	}

	private void ShipGravityAndThrust(ref SimState s, bool holding, out bool died)
	{
		died = false;
		bool flag = (s.GravFlipped ? (s.VelY_fixed < 0) : (s.VelY_fixed > 0));
		int num = ((holding && flag) ? ShipGravityHoldFall(s.Mini) : (holding ? ShipGravityBase(s.Mini) : (flag ? ShipGravity(s.Mini) : ShipGravityAfterHold(s.Mini))));
		int num2 = num * ((!s.GravFlipped) ? 1 : (-1));
		if (s.GravFlipped ^ holding)
		{
			num2 = -num2;
		}
		int tmpfallspeed = 17475;
		int clampMaxY = Math.Max(0, mapHeight * 16 - 16) << 8;
		SharedPhysics.CommonGravityRoutine(ref s.VelY_fixed, ref s.Y_fixed, num2, tmpfallspeed, s.GravFlipped ? 255 : 0, s.Dashing, s.GravityMod, 1.0, isFullSpeed: true, s.VelX_fixed, clampMaxY);
		if (!s.GravFlipped)
		{
			if (s.VelY_fixed < -1091)
			{
				s.VelY_fixed = -1091;
			}
			if (s.VelY_fixed > 873)
			{
				s.VelY_fixed = 873;
			}
		}
		else
		{
			if (s.VelY_fixed < -873)
			{
				s.VelY_fixed = -873;
			}
			if (s.VelY_fixed > 1091)
			{
				s.VelY_fixed = 1091;
			}
		}
	}

	private void ShipEject(ref SimState s, bool input, out bool died)
	{
		died = false;
		SharedPhysics.EjectResult ejectResult = SharedPhysics.ShipUfoEject(in _collisionMap, s.X_fixed, s.Y_fixed, s.VelY_fixed, s.VelX_fixed, s.GravFlipped, s.Mini, s.GameMode, input, s.SlopeWasOnCounter, s.SlopeFrames, s.SlopeType, s.SlopeJumpHigher, s.LastSlopeType);
		s.Y_fixed = ejectResult.NewY_fixed;
		s.VelY_fixed = ejectResult.NewVelY_fixed;
		s.SlopeType = ejectResult.SlopeType;
		s.SlopeFrames = ejectResult.SlopeFrames;
		s.SlopeWasOnCounter = ejectResult.SlopeWasOnCounter;
		s.SlopeJumpHigher = ejectResult.SlopeJumpHigher;
		s.LastSlopeType = ejectResult.LastSlopeType;
		if (ejectResult.Died)
		{
			died = true;
		}
	}

	private bool VerifyGroundSupport(ref SimState s)
	{
		return SharedPhysics.VerifyGroundSupport(in _collisionMap, s.X_fixed >> 8, s.Y_fixed >> 8, s.GameMode, s.Mini, s.GravFlipped);
	}

	private void ApplyPortalsUpTo(ref SimState s, int targetX_px)
	{
		int? num = null;
		int? num2 = null;
		int? num3 = null;
		int? num4 = null;
		int? num5 = null;
		int? num6 = null;
		int? num7 = null;
		int? num8 = null;
		int? num9 = null;
		int? num10 = null;
		int? num11 = null;
		int? num12 = null;
		int? num13 = null;
		int? num14 = null;
		int? num15 = null;
		int? num16 = null;
		foreach (SpriteEntry allSprite in allSprites)
		{
			if (allSprite.AnchorX_px > targetX_px)
			{
				break;
			}
			int spriteId = allSprite.SpriteId;
			if (IsGameModePortal(spriteId))
			{
				if (!num2.HasValue || allSprite.AnchorX_px > num2.Value)
				{
					num = spriteId;
					num2 = allSprite.AnchorX_px;
				}
			}
			else if (IsMiniGrowthPortal(spriteId))
			{
				if (!num4.HasValue || allSprite.AnchorX_px > num4.Value)
				{
					num3 = spriteId;
					num4 = allSprite.AnchorX_px;
				}
			}
			else if (IsGravityPortal(spriteId))
			{
				if (!num6.HasValue || allSprite.AnchorX_px > num6.Value)
				{
					num5 = spriteId;
					num6 = allSprite.AnchorX_px;
				}
			}
			else if (IsSpeedPortal(spriteId))
			{
				if (allSprite.AnchorX_px < targetX_px && (!num8.HasValue || allSprite.AnchorX_px > num8.Value))
				{
					num7 = spriteId;
					num8 = allSprite.AnchorX_px;
				}
			}
			else if (spriteId == 34 || spriteId == 35)
			{
				if (!num10.HasValue || allSprite.AnchorX_px > num10.Value)
				{
					num9 = spriteId;
					num10 = allSprite.AnchorX_px;
				}
			}
			else if ((spriteId >= 95 && spriteId <= 99) || (spriteId >= 112 && spriteId <= 116))
			{
				if (!num12.HasValue || allSprite.AnchorX_px >= num12.Value)
				{
					num11 = spriteId;
					num12 = allSprite.AnchorX_px;
				}
			}
			else if (spriteId == 221 || spriteId == 237)
			{
				if (!num14.HasValue || allSprite.AnchorX_px > num14.Value)
				{
					num13 = spriteId;
					num14 = allSprite.AnchorX_px;
				}
			}
			else if ((spriteId == 142 || spriteId == 158) && (!num16.HasValue || allSprite.AnchorX_px > num16.Value))
			{
				num15 = spriteId;
				num16 = allSprite.AnchorX_px;
			}
			if (!IsSpeedPortal(spriteId) || allSprite.AnchorX_px < targetX_px)
			{
				s.ProcessedSprites.Add(allSprite.Index);
			}
		}
		if (num.HasValue)
		{
			int num17 = SpriteIdToGameMode(num.Value);
			if (num17 >= 0)
			{
				s.GameMode = num17;
				if (num17 == 8)
				{
					s.NinjaJumps = 3;
				}
			}
		}
		if (num3.HasValue)
		{
			s.Mini = num3.Value == 24;
		}
		if (num5.HasValue)
		{
			s.GravMul = ((!(s.GravFlipped = IsReverseGravity(num5.Value))) ? 1 : (-1));
		}
		if (num7.HasValue)
		{
			int num18 = SpriteIdToSpeedFixed(num7.Value);
			if (num18 > 0)
			{
				s.VelX_fixed = num18;
			}
		}
		if (num11.HasValue)
		{
			int value = num11.Value;
			double gravityMod = 1.0;
			if (value >= 112 && value <= 116)
			{
				switch (value)
				{
				case 112:
					gravityMod = 1.0 / 3.0;
					break;
				case 113:
					gravityMod = 0.5;
					break;
				case 114:
					gravityMod = 2.0 / 3.0;
					break;
				case 115:
					gravityMod = 2.0;
					break;
				case 116:
					gravityMod = 1.0;
					break;
				}
			}
			else
			{
				switch (value)
				{
				case 95:
					gravityMod = 1.0 / 3.0;
					break;
				case 96:
					gravityMod = 0.5;
					break;
				case 97:
					gravityMod = 2.0 / 3.0;
					break;
				case 98:
					gravityMod = 2.0;
					break;
				case 99:
					gravityMod = 1.0;
					break;
				}
			}
			s.GravityMod = gravityMod;
		}
		if (num9.HasValue)
		{
			if (num9.Value == 34)
			{
				s.DualActive = true;
				s.P2_Y_fixed = s.Y_fixed;
				s.P2_VelY_fixed = 0;
				s.P2_GravFlipped = !s.GravFlipped;
				s.P2_GravMul = -s.GravMul;
				s.P2_Mini = s.Mini;
				s.P2_WasZeroedByCollision = true;
				s.P2_OnGround = true;
				s.P2_PendingOrbIndex = -1;
				s.P2_PendingOrbSpriteId = -1;
				s.P2_PendingOrbExtra1Index = -1;
				s.P2_PendingOrbExtra1SpriteId = -1;
				s.P2_PendingOrbExtra2Index = -1;
				s.P2_PendingOrbExtra2SpriteId = -1;
			}
			else
			{
				s.DualActive = false;
			}
		}
		if (num13.HasValue)
		{
			s.NoCamLockForced = num13.Value == 221;
		}
		if (num15.HasValue)
		{
			s.WrapMode = num15.Value == 142;
		}
	}

	private static void AddPendingOrb(ref SimState s, int index, int spriteId)
	{
		if (s.PendingOrbIndex < 0)
		{
			s.PendingOrbIndex = index;
			s.PendingOrbSpriteId = spriteId;
		}
		else if (s.PendingOrbExtra1Index < 0)
		{
			s.PendingOrbExtra1Index = index;
			s.PendingOrbExtra1SpriteId = spriteId;
		}
		else if (s.PendingOrbExtra2Index < 0)
		{
			s.PendingOrbExtra2Index = index;
			s.PendingOrbExtra2SpriteId = spriteId;
		}
	}

	private static void ClearPendingOrbs(ref SimState s)
	{
		s.PendingOrbIndex = -1;
		s.PendingOrbSpriteId = -1;
		s.PendingOrbExtra1Index = -1;
		s.PendingOrbExtra1SpriteId = -1;
		s.PendingOrbExtra2Index = -1;
		s.PendingOrbExtra2SpriteId = -1;
	}

	private bool ProcessSprites(ref SimState s, int currentX_px, out bool orbHitThisFrame)
	{
		orbHitThisFrame = false;
		int num = (s.CameraY_fixed >> 8) + (s.Y_fixed - s.CameraY_fixed >> 8);
		int hitboxW = GetHitboxW(s.Mini);
		int hitboxH = GetHitboxH(s.Mini);
		int hitboxOffsetY = GetHitboxOffsetY(s.GameMode, s.Mini, s.GravFlipped);
		int num2 = num + hitboxOffsetY;
		int num3 = num2 + hitboxH;
		int num4 = currentX_px + 1;
		int num5 = num4 + hitboxW;
		SpriteEntry[] spritesArr = _spritesArr;
		int num6 = spritesArr.Length;
		int num7 = SpriteLowerBound(currentX_px - 64);
		for (int i = num7; i < num6; i++)
		{
			ref SpriteEntry reference = ref spritesArr[i];
			if (s.ProcessedSprites.Contains(reference.Index) || reference.HitRight < currentX_px)
			{
				continue;
			}
			if (reference.AnchorX_px - 16 > num5 + 16)
			{
				break;
			}
			int spriteId = reference.SpriteId;
			if (spriteId != 34 && spriteId != 35)
			{
				continue;
			}
			bool flag = num5 >= reference.HitLeft && reference.HitRight >= num4;
			bool flag2 = num3 >= reference.HitTop && reference.HitBottom >= num2;
			if (flag && flag2)
			{
				if (spriteId == 34 && !s.DualActive)
				{
					s.DualActive = true;
					s.TargetCameraY_fixed = NesNtCameraTarget_fixed(reference.AnchorY_px - 8);
					s.P2_Y_fixed = s.Y_fixed;
					s.P2_VelY_fixed = -s.VelY_fixed;
					s.P2_GravFlipped = !s.GravFlipped;
					s.P2_GravMul = -s.GravMul;
					s.P2_Mini = s.Mini;
					s.P2_WasZeroedByCollision = false;
					s.P2_OnGround = false;
					s.P2_BallFlipCooldown = 0;
					s.P2_BallInputBuffer = 0;
					s.P2_BallCooldownFrames = 0;
					s.P2_RobotJumpTime = 0;
					s.P2_SlopeWasOnCounter = 0;
					s.P2_SlopeFrames = 0;
					s.P2_SlopeType = 0;
					s.P2_Orbed = false;
					s.P2_BlackOrbed = false;
					s.P2_PrevInputHeld = s.PrevInputHeld;
					s.P2_Dashing = 0;
					s.P2_JBlocked = false;
					s.P2_FBlocked = false;
					s.P2_HBlocked = false;
					s.P2_Dblocked = false;
					s.P2_NinjaJumps = 0;
					s.P2_PendingOrbIndex = -1;
					s.P2_PendingOrbSpriteId = -1;
					s.P2_PendingOrbExtra2Index = -1;
					s.P2_PendingOrbExtra2SpriteId = -1;
					s.P2_PendingOrbExtra1Index = -1;
					s.P2_PendingOrbExtra1SpriteId = -1;
					PfLog($"[DUAL_ACTIVATE] idx={reference.Index} P2_Y={s.P2_Y_fixed >> 8} P2_VelY=0x{s.P2_VelY_fixed:X4} P2_GravFlipped={s.P2_GravFlipped}");
				}
				else if (spriteId == 35 && s.DualActive)
				{
					s.DualActive = false;
					s.TargetCameraY_fixed = NesNtCameraTarget_fixed(reference.AnchorY_px - 8);
					PfLog($"[SINGLE_ACTIVATE] idx={reference.Index} targetCamY={s.TargetCameraY_fixed >> 8}");
				}
				s.ProcessedSprites.Add(reference.Index);
			}
		}
		for (int j = num7; j < num6; j++)
		{
			ref SpriteEntry reference2 = ref spritesArr[j];
			if (s.ProcessedSprites.Contains(reference2.Index) || reference2.HitRight < currentX_px)
			{
				continue;
			}
			if (reference2.AnchorX_px - 16 > num5 + 16)
			{
				break;
			}
			int spriteId2 = reference2.SpriteId;
			if (IsSpeedPortal(spriteId2))
			{
				bool flag3 = num5 >= reference2.HitLeft && reference2.HitRight >= num4;
				bool flag4 = num3 >= reference2.HitTop && reference2.HitBottom >= num2;
				if (flag3 && flag4)
				{
					int num8 = SpriteIdToSpeedFixed(spriteId2);
					PfLog($"[PORTAL_SPEED] sid=0x{spriteId2:X2} idx={reference2.Index} VelX: 0x{s.VelX_fixed:X4} -> 0x{num8:X4}");
					if (num8 > 0)
					{
						s.VelX_fixed = num8;
					}
					s.ProcessedSprites.Add(reference2.Index);
				}
			}
			else
			{
				if (spriteId2 == 34 || spriteId2 == 35)
				{
					continue;
				}
				if (spriteId2 == 221 || spriteId2 == 237)
				{
					bool flag5 = num5 >= reference2.HitLeft && reference2.HitRight >= num4;
					bool flag6 = num3 >= reference2.HitTop && reference2.HitBottom >= num2;
					if (flag5 && flag6)
					{
						s.NoCamLockForced = spriteId2 == 221;
						s.ProcessedSprites.Add(reference2.Index);
					}
				}
				else if (spriteId2 == 142 || spriteId2 == 158)
				{
					bool flag7 = num5 >= reference2.HitLeft && reference2.HitRight >= num4;
					bool flag8 = num3 >= reference2.HitTop && reference2.HitBottom >= num2;
					if (flag7 && flag8)
					{
						s.WrapMode = spriteId2 == 142;
						s.ProcessedSprites.Add(reference2.Index);
					}
				}
				else if ((spriteId2 == 100 || spriteId2 == 126) && !_dualP2Guard)
				{
					bool flag9 = num5 >= reference2.HitLeft && reference2.HitRight >= num4;
					bool flag10 = num3 >= reference2.HitTop && reference2.HitBottom >= num2;
					if (!(flag9 && flag10))
					{
						continue;
					}
					if (s.RainbowMaxMode == 0)
					{
						if (s.GameMode == 6 || s.GameMode == 10)
						{
							s.VelY_fixed = 0;
						}
						s.RainbowMaxMode = ((spriteId2 == 100) ? 8 : 12);
						PfLog($"[RAINBOW_PORTAL] sid=0x{spriteId2:X2} idx={reference2.Index} maxMode={s.RainbowMaxMode} entryMode={s.GameMode}");
					}
					s.RobotJumpTime = 0;
					if (s.GameMode != 0 && s.GameMode != 4 && s.GameMode != 8 && s.GameMode != 9 && !s.DualActive)
					{
						int portalWorldY_px = reference2.AnchorY_px - 8;
						s.TargetCameraY_fixed = NesNtCameraTarget_fixed(portalWorldY_px);
					}
					s.ProcessedSprites.Add(reference2.Index);
				}
				else
				{
					if (IsGameModePortal(spriteId2) && _dualP2Guard)
					{
						continue;
					}
					if (IsGameModePortal(spriteId2) || IsGravityPortal(spriteId2) || IsMiniGrowthPortal(spriteId2) || IsEndLevel(spriteId2))
					{
						bool flag11 = num5 >= reference2.HitLeft && reference2.HitRight >= num4;
						bool flag12 = num3 >= reference2.HitTop && reference2.HitBottom >= num2;
						bool flag14;
						if (IsEndLevel(spriteId2))
						{
							int num9 = s.CameraY_fixed >> 8;
							int num10 = num9 + 240;
							int num11 = reference2.AnchorY_px - 8;
							bool flag13 = num11 + 16 >= num9 && num11 < num10;
							flag14 = flag11 && flag13;
						}
						else
						{
							flag14 = flag11 && flag12;
						}
						if (IsGravityPortal(spriteId2) || IsEndLevel(spriteId2))
						{
							PfLog($"[SPRITE_CHECK] sid=0x{spriteId2:X2} idx={reference2.Index} playerBox=({num4},{num2})-({num5},{num3}) spriteBox=({reference2.HitLeft},{reference2.HitTop})-({reference2.HitRight},{reference2.HitBottom}) xOvlp={flag11} yOvlp={flag12} hit={flag14}");
						}
						if (flag14)
						{
							int gameMode = s.GameMode;
							bool flag15 = ApplyPortalSprite(ref s, spriteId2);
							PfLog($"[SPRITE_HIT] sid=0x{spriteId2:X2} idx={reference2.Index} applied={flag15} gravFlipped={s.GravFlipped} gravMul={s.GravMul}");
							if (flag15)
							{
								s.ProcessedSprites.Add(reference2.Index);
							}
							if (IsGameModePortal(spriteId2) && flag15 && s.GameMode != 0 && s.GameMode != 4 && s.GameMode != 8 && s.GameMode != 9 && !s.DualActive)
							{
								int num12 = reference2.AnchorY_px - 8;
								s.TargetCameraY_fixed = NesNtCameraTarget_fixed(num12);
								PfLog($"[MODE_PORTAL_TARGET_OLDX] sid=0x{spriteId2:X2} idx={reference2.Index} AnchorY_px={reference2.AnchorY_px} portalWorldY_px={num12} resultPF_px={s.TargetCameraY_fixed >> 8} mode={s.GameMode}");
							}
							if (IsEndLevel(spriteId2))
							{
								return true;
							}
						}
					}
					else if (spriteId2 >= 95 && spriteId2 <= 99)
					{
						bool flag16 = num5 >= reference2.HitLeft && reference2.HitRight >= num4;
						bool flag17 = num3 >= reference2.HitTop && reference2.HitBottom >= num2;
						if (flag16 && flag17)
						{
							double num13 = 1.0;
							switch (spriteId2)
							{
							case 95:
								num13 = 1.0 / 3.0;
								break;
							case 96:
								num13 = 0.5;
								break;
							case 97:
								num13 = 2.0 / 3.0;
								break;
							case 98:
								num13 = 2.0;
								break;
							case 99:
								num13 = 1.0;
								break;
							}
							s.GravityMod = num13;
							PfLog($"[GRAV_MOD_PORTAL] sid=0x{spriteId2:X2} idx={reference2.Index} multiplier={num13:F3}x");
							s.ProcessedSprites.Add(reference2.Index);
						}
					}
					else
					{
						if (spriteId2 >= 112 && spriteId2 <= 116)
						{
							continue;
						}
						if (IsYellowPad(spriteId2) || IsPinkPad(spriteId2) || IsRedPad(spriteId2) || IsBluePad(spriteId2) || IsGreenPad(spriteId2))
						{
							if (IsBluePad(spriteId2))
							{
								bool flag18 = spriteId2 == 13 || spriteId2 == 253;
								if ((flag18 && s.GravFlipped) || (!flag18 && !s.GravFlipped))
								{
									continue;
								}
							}
							int num14 = ((!IsBluePad(spriteId2)) ? 1 : 0);
							int num15 = currentX_px + num14;
							int num16 = num15 + hitboxW;
							bool flag19 = num16 >= reference2.HitLeft && reference2.HitRight >= num15;
							bool flag20 = num3 >= reference2.HitTop && reference2.HitBottom >= num2;
							if (IsYellowPad(spriteId2) && _frameCounter >= 2378 && _frameCounter <= 2383)
							{
								PfLog($"[YPAD_CHECK] f={_frameCounter} idx={reference2.Index} sid=0x{spriteId2:X2} player=({num15},{num2})-({num16},{num3}) sprite=({reference2.HitLeft},{reference2.HitTop})-({reference2.HitRight},{reference2.HitBottom}) xOvlp={flag19} yOvlp={flag20} gravFlipped={s.GravFlipped} mode={s.GameMode}");
							}
							if (IsBluePad(spriteId2) && flag19)
							{
								PfLog($"[BPAD_PF] idx={reference2.Index} sid=0x{spriteId2:X2} pad=({num15},{num2})-({num16},{num3}) spr=({reference2.HitLeft},{reference2.HitTop})-({reference2.HitRight},{reference2.HitBottom}) xO={flag19} yO={flag20}");
							}
							if (flag19 && flag20)
							{
								ApplyPadSprite(ref s, spriteId2);
								orbHitThisFrame = true;
							}
							continue;
						}
						if (IsOrbSprite(spriteId2))
						{
							if (s.Dashing == 0)
							{
								int miniCenterOffsetY = SharedPhysics.GetMiniCenterOffsetY(s.Mini);
								int num17 = num + miniCenterOffsetY;
								int num18 = num17 + hitboxH;
								bool flag21 = num5 >= reference2.HitLeft && reference2.HitRight >= num4;
								bool flag22 = num18 >= reference2.HitTop && reference2.HitBottom >= num17;
								if (flag21 && flag22)
								{
									AddPendingOrb(ref s, reference2.Index, spriteId2);
									PfLog($"[ORB_PENDING] sid=0x{spriteId2:X2} idx={reference2.Index} playerBox=({num4},{num17})-({num5},{num18}) spriteBox=({reference2.HitLeft},{reference2.HitTop})-({reference2.HitRight},{reference2.HitBottom})");
								}
							}
							continue;
						}
						if (IsDashOrb(spriteId2))
						{
							if (s.Dashing == 0)
							{
								int miniCenterOffsetY2 = SharedPhysics.GetMiniCenterOffsetY(s.Mini);
								int num19 = num + miniCenterOffsetY2;
								int num20 = num19 + hitboxH;
								bool flag23 = num5 >= reference2.HitLeft && reference2.HitRight >= num4;
								bool flag24 = num20 >= reference2.HitTop && reference2.HitBottom >= num19;
								if (flag23 && flag24)
								{
									AddPendingOrb(ref s, reference2.Index, spriteId2);
									PfLog($"[DASH_ORB_PENDING] sid=0x{spriteId2:X2} idx={reference2.Index}");
								}
							}
							continue;
						}
						if (IsSpiderOrb(spriteId2) || IsSpiderPad(spriteId2))
						{
							int miniCenterOffsetY3 = SharedPhysics.GetMiniCenterOffsetY(s.Mini);
							int num21 = num + miniCenterOffsetY3;
							int num22 = num21 + hitboxH;
							bool flag25 = num5 >= reference2.HitLeft && reference2.HitRight >= num4;
							bool flag26 = num22 >= reference2.HitTop && reference2.HitBottom >= num21;
							if (flag25 && flag26)
							{
								if (IsSpiderOrb(spriteId2))
								{
									AddPendingOrb(ref s, reference2.Index, spriteId2);
									continue;
								}
								bool goUp = spriteId2 == 86;
								ApplySpiderTeleport(ref s, goUp);
								orbHitThisFrame = true;
							}
							continue;
						}
						switch (spriteId2)
						{
						case 249:
						{
							bool flag27 = num5 >= reference2.HitLeft && reference2.HitRight >= num4;
							bool flag28 = num3 >= reference2.HitTop && reference2.HitBottom >= num2;
							if (flag27 && flag28 && s.Dashing != 0)
							{
								s.Dashing = 0;
								s.Orbed = true;
								s.VelY_fixed = 0;
								PfLog($"[S_BLOCK] Stopped dash at idx={reference2.Index}");
							}
							continue;
						}
						case 247:
						{
							bool flag31 = num5 >= reference2.HitLeft && reference2.HitRight >= num4;
							bool flag32 = num3 >= reference2.HitTop && reference2.HitBottom >= num2;
							if (flag31 && flag32)
							{
								s.JBlocked = true;
								PfLog($"[J_BLOCK] Set jblocked at idx={reference2.Index}");
							}
							continue;
						}
						case 246:
						{
							bool flag35 = num5 >= reference2.HitLeft && reference2.HitRight >= num4;
							bool flag36 = num3 >= reference2.HitTop && reference2.HitBottom >= num2;
							if (flag35 && flag36)
							{
								s.FBlocked = true;
								PfLog($"[F_BLOCK] Set fblocked at idx={reference2.Index}");
							}
							continue;
						}
						case 248:
						{
							bool flag29 = num5 >= reference2.HitLeft && reference2.HitRight >= num4;
							bool flag30 = num3 >= reference2.HitTop && reference2.HitBottom >= num2;
							if (flag29 && flag30)
							{
								s.HBlocked = true;
								PfLog($"[H_BLOCK] Set hblocked at idx={reference2.Index}");
							}
							continue;
						}
						case 250:
						{
							bool flag33 = num5 >= reference2.HitLeft && reference2.HitRight >= num4;
							bool flag34 = num3 >= reference2.HitTop && reference2.HitBottom >= num2;
							if (flag33 && flag34)
							{
								s.Dblocked = true;
								PfLog($"[D_BLOCK] Set dblocked at idx={reference2.Index}");
							}
							continue;
						}
						}
						if (IsTeleportPortalEntrance(spriteId2))
						{
							bool flag37 = num5 >= reference2.HitLeft && reference2.HitRight >= num4;
							bool flag38 = num3 >= reference2.HitTop && reference2.HitBottom >= num2;
							if (!(flag37 && flag38))
							{
								continue;
							}
							PfLog($"[TELEPORT_ENTRANCE] sid=0x{spriteId2:X2} idx={reference2.Index} playerBox=({num4},{num2})-({num5},{num3}) spriteBox=({reference2.HitLeft},{reference2.HitTop})-({reference2.HitRight},{reference2.HitBottom})");
							ApplyTeleportPortal(ref s, reference2, spriteId2, currentX_px);
							s.ProcessedSprites.Add(reference2.Index);
							num = s.Y_fixed >> 8;
							num2 = num + hitboxOffsetY;
							num3 = num2 + hitboxH;
							for (int k = num7; k < num6; k++)
							{
								ref SpriteEntry reference3 = ref spritesArr[k];
								if (reference3.HitRight < currentX_px)
								{
									continue;
								}
								if (reference3.AnchorX_px - 16 > num5 + 16)
								{
									break;
								}
								int spriteId3 = reference3.SpriteId;
								if (IsYellowPad(spriteId3) || IsPinkPad(spriteId3) || IsRedPad(spriteId3) || IsBluePad(spriteId3) || IsGreenPad(spriteId3))
								{
									if (IsBluePad(spriteId3))
									{
										bool flag39 = spriteId3 == 13 || spriteId3 == 253;
										if ((flag39 && s.GravFlipped) || (!flag39 && !s.GravFlipped))
										{
											continue;
										}
									}
									int num23 = ((!IsBluePad(spriteId3)) ? 1 : 0);
									int num24 = currentX_px + num23;
									int num25 = num24 + hitboxW;
									bool flag40 = num25 >= reference3.HitLeft && reference3.HitRight >= num24;
									bool flag41 = num3 >= reference3.HitTop && reference3.HitBottom >= num2;
									if (flag40 && flag41)
									{
										PfLog($"[PAD_POST_TELEPORT] sid=0x{spriteId3:X2} idx={reference3.Index} pad=({num24},{num2})-({num25},{num3}) spr=({reference3.HitLeft},{reference3.HitTop})-({reference3.HitRight},{reference3.HitBottom})");
										ApplyPadSprite(ref s, spriteId3);
										orbHitThisFrame = true;
									}
								}
								else if (IsSpiderPad(spriteId3))
								{
									int miniCenterOffsetY4 = SharedPhysics.GetMiniCenterOffsetY(s.Mini);
									int num26 = num + miniCenterOffsetY4;
									int num27 = num26 + hitboxH;
									bool flag42 = num5 >= reference3.HitLeft && reference3.HitRight >= num4;
									bool flag43 = num27 >= reference3.HitTop && reference3.HitBottom >= num26;
									if (flag42 && flag43)
									{
										bool goUp2 = spriteId3 == 86;
										ApplySpiderTeleport(ref s, goUp2);
										orbHitThisFrame = true;
									}
								}
							}
						}
						else if (IsCoinSprite(spriteId2) || IsMiniCoinSprite(spriteId2))
						{
							bool flag44 = num5 >= reference2.HitLeft && reference2.HitRight >= num4;
							bool flag45 = num3 >= reference2.HitTop && reference2.HitBottom >= num2;
							if (flag44 && flag45)
							{
								s.ProcessedSprites.Add(reference2.Index);
							}
						}
					}
				}
			}
		}
		return false;
	}

	private void CheckGravityPortalsPostY(ref SimState s, int prevX_px)
	{
		int num = s.Y_fixed >> 8;
		int hitboxW = GetHitboxW(s.Mini);
		int num2 = (s.X_fixed >> 8) + 1 + hitboxW / 2;
		int num3 = num2 - 7;
		int num4 = num3 + 14;
		int num5 = num;
		if (s.Mini && !s.GravFlipped)
		{
			num5 += 9;
		}
		int num6 = num5 + 14;
		int num7 = SpriteLowerBound(prevX_px - 64);
		for (int i = num7; i < _spritesArr.Length; i++)
		{
			ref SpriteEntry reference = ref _spritesArr[i];
			if (s.ProcessedSprites.Contains(reference.Index) || reference.HitRight <= prevX_px)
			{
				continue;
			}
			if (reference.AnchorX_px - 16 > num4 + 16)
			{
				break;
			}
			int spriteId = reference.SpriteId;
			if (!IsGravityPortal(spriteId))
			{
				continue;
			}
			bool flag = num4 >= reference.HitLeft && reference.HitRight >= num3;
			bool flag2 = num6 >= reference.HitTop && reference.HitBottom >= num5;
			PfLog($"[GRAV_POSTY_CHECK] sid=0x{spriteId:X2} idx={reference.Index} player14x14=({num3},{num5})-({num4},{num6}) spriteBox=({reference.HitLeft},{reference.HitTop})-({reference.HitRight},{reference.HitBottom}) xOvlp={flag} yOvlp={flag2}");
			if (flag && flag2)
			{
				bool flag3 = ApplyPortalSprite(ref s, spriteId);
				PfLog($"[GRAV_POSTY_HIT] sid=0x{spriteId:X2} applied={flag3} gravFlipped={s.GravFlipped} gravMul={s.GravMul} VelY=0x{s.VelY_fixed:X4}");
				if (flag3)
				{
					s.ProcessedSprites.Add(reference.Index);
				}
			}
		}
	}

	private void CheckGameModePortalsAtNewX(ref SimState s)
	{
		int num = s.X_fixed >> 8;
		int num2 = (s.Mini ? 8 : 15);
		int num3 = (s.Mini ? 7 : 15);
		int num4 = num + 1;
		int num5 = num4 + num2 - 1;
		int num6 = (s.Y_fixed >> 8) + SharedPhysics.GetHitboxOffsetY(s.GameMode, s.Mini, s.GravFlipped);
		int num7 = num6 + num3 - 1;
		int num8 = SpriteLowerBound(num - 64);
		for (int i = num8; i < _spritesArr.Length; i++)
		{
			ref SpriteEntry reference = ref _spritesArr[i];
			if (s.ProcessedSprites.Contains(reference.Index) || reference.HitRight <= num4)
			{
				continue;
			}
			if (reference.AnchorX_px - 16 > num5 + 16)
			{
				break;
			}
			int spriteId = reference.SpriteId;
			if (!IsGameModePortal(spriteId) && !IsGravityPortal(spriteId))
			{
				continue;
			}
			bool flag = num5 + 1 >= reference.HitLeft && reference.HitRight >= num4;
			bool flag2 = num7 + 1 >= reference.HitTop && reference.HitBottom >= num6;
			bool flag3 = flag && flag2;
			if (IsGameModePortal(spriteId))
			{
				PfLog($"[GAMEMODE_NEWX_CHECK] sid=0x{spriteId:X2} idx={reference.Index} playerBox=({num4},{num6})-({num5},{num7}) spriteBox=({reference.HitLeft},{reference.HitTop})-({reference.HitRight},{reference.HitBottom}) xOvlp={flag} yOvlp={flag2} hit={flag3}");
			}
			else
			{
				PfLog($"[GRAV_NEWX_CHECK] sid=0x{spriteId:X2} idx={reference.Index} playerBox=({num4},{num6})-({num5},{num7}) spriteBox=({reference.HitLeft},{reference.HitTop})-({reference.HitRight},{reference.HitBottom}) xOvlp={flag} yOvlp={flag2} hit={flag3}");
			}
			if (flag3)
			{
				bool flag4 = ApplyPortalSprite(ref s, spriteId);
				if (IsGameModePortal(spriteId))
				{
					PfLog($"[GAMEMODE_NEWX_HIT] sid=0x{spriteId:X2} applied={flag4} mode={s.GameMode} VelY=0x{s.VelY_fixed:X4}");
				}
				else
				{
					PfLog($"[GRAV_NEWX_HIT] sid=0x{spriteId:X2} applied={flag4} gravFlipped={s.GravFlipped} gravMul={s.GravMul} VelY=0x{s.VelY_fixed:X4}");
				}
				if (flag4)
				{
					s.ProcessedSprites.Add(reference.Index);
				}
				if (IsGameModePortal(spriteId) && flag4 && s.GameMode != 0 && s.GameMode != 4 && s.GameMode != 8 && s.GameMode != 9 && !s.DualActive)
				{
					int num9 = reference.AnchorY_px - 8;
					s.TargetCameraY_fixed = NesNtCameraTarget_fixed(num9);
					PfLog($"[MODE_PORTAL_TARGET] sid=0x{spriteId:X2} idx={reference.Index} AnchorY_px={reference.AnchorY_px} portalWorldY_px={num9} resultPF_px={s.TargetCameraY_fixed >> 8} mode={s.GameMode}");
				}
				break;
			}
		}
	}

	private void CheckSpeedPortalsAtNewX(ref SimState s)
	{
		int num = s.X_fixed + 12288;
		int num2 = SpriteLowerBound((s.X_fixed >> 8) - 64);
		for (int i = num2; i < _spritesArr.Length; i++)
		{
			ref SpriteEntry reference = ref _spritesArr[i];
			if (s.ProcessedSprites.Contains(reference.Index))
			{
				continue;
			}
			int spriteId = reference.SpriteId;
			if (!IsSpeedPortal(spriteId))
			{
				continue;
			}
			int num3 = reference.AnchorX_px << 8;
			if (num3 <= num)
			{
				int num4 = SpriteIdToSpeedFixed(spriteId);
				PfLog($"[PORTAL_SPEED] sid=0x{spriteId:X2} VelX: 0x{s.VelX_fixed:X4} -> 0x{num4:X4}");
				if (num4 > 0)
				{
					s.VelX_fixed = num4;
				}
				s.ProcessedSprites.Add(reference.Index);
				break;
			}
		}
	}

	private void CheckGravityModTriggersAtNewX(ref SimState s)
	{
		int num = s.X_fixed + 12288;
		int num2 = SpriteLowerBound((s.X_fixed >> 8) - 64);
		for (int i = num2; i < _spritesArr.Length; i++)
		{
			ref SpriteEntry reference = ref _spritesArr[i];
			if (s.ProcessedSprites.Contains(reference.Index))
			{
				continue;
			}
			int spriteId = reference.SpriteId;
			if (spriteId < 112 || spriteId > 116)
			{
				continue;
			}
			int num3 = reference.AnchorX_px << 8;
			if (num3 <= num)
			{
				double num4 = 1.0;
				switch (spriteId)
				{
				case 112:
					num4 = 1.0 / 3.0;
					break;
				case 113:
					num4 = 0.5;
					break;
				case 114:
					num4 = 2.0 / 3.0;
					break;
				case 115:
					num4 = 2.0;
					break;
				case 116:
					num4 = 1.0;
					break;
				}
				s.GravityMod = num4;
				PfLog($"[GRAV_MOD_TRIG] sid=0x{spriteId:X2} idx={reference.Index} multiplier={num4:F3}x");
				s.ProcessedSprites.Add(reference.Index);
			}
		}
	}

	private static bool IsWaveOrSnakeMode(int mode)
	{
		return mode == 6 || mode == 10;
	}

	private static int NesSignedHalf(int value)
	{
		return value >> 1;
	}

	private static string ApplyGameModePortalVelocityRule(ref SimState s, int sid, int prevMode, int newMode)
	{
		bool flag = IsWaveOrSnakeMode(prevMode);
		switch (sid)
		{
		case 1:
		case 2:
		case 3:
			if (prevMode != newMode)
			{
				s.VelY_fixed = NesSignedHalf(s.VelY_fixed);
				return "halved";
			}
			break;
		case 4:
			if (flag)
			{
				s.VelY_fixed = NesSignedHalf(s.VelY_fixed);
				return "halved-wave-snake";
			}
			break;
		case 0:
		case 23:
		case 75:
		case 88:
		case 106:
			if (flag)
			{
				s.VelY_fixed = 0;
				return "zeroed-wave-snake";
			}
			break;
		}
		return "preserved";
	}

	private bool ApplyPortalSprite(ref SimState s, int sid)
	{
		if (IsEndLevel(sid))
		{
			return true;
		}
		if (IsGameModePortal(sid))
		{
			int num = SpriteIdToGameMode(sid);
			if (num >= 0 && num != s.GameMode)
			{
				if (_speculativeDepth == 0 && false)
				{
					_log.WriteLine($"[MODE_TRANSITION] {s.GameMode}->{num} Y={s.Y_fixed >> 8} VelY=0x{s.VelY_fixed:X} X={s.X_fixed >> 8} GravMul={s.GravMul} GravFlip={s.GravFlipped} Processed={s.ProcessedSprites.Count}");
				}
				if ((s.GameMode == 0 || s.GameMode == 2) && (num == 1 || num == 3) && _backtrackCheckpoints != null)
				{
					for (int num2 = _backtrackCheckpoints.Count - 1; num2 >= 0; num2--)
					{
						if (_backtrackCheckpoints[num2].GameMode == s.GameMode)
						{
							BacktrackCheckpoint backtrackCheckpoint = _backtrackCheckpoints[num2];
							_lastCubeToShipCheckpoint = new BacktrackCheckpoint
							{
								Frame = backtrackCheckpoint.Frame,
								State = backtrackCheckpoint.State.Clone(),
								HoldJumpState = backtrackCheckpoint.HoldJumpState,
								HoldDelayState = backtrackCheckpoint.HoldDelayState,
								CommittedDelayState = backtrackCheckpoint.CommittedDelayState,
								PathPointCount = backtrackCheckpoint.PathPointCount,
								InputCount = backtrackCheckpoint.InputCount,
								RetryStage = 0,
								UsedBias = backtrackCheckpoint.UsedBias,
								ShipBias = backtrackCheckpoint.ShipBias,
								GameMode = backtrackCheckpoint.GameMode,
								ShipForceHold = backtrackCheckpoint.ShipForceHold,
								ShipForceRelease = backtrackCheckpoint.ShipForceRelease,
								ShipCommitFrames = backtrackCheckpoint.ShipCommitFrames,
								ShipCommitHold = backtrackCheckpoint.ShipCommitHold,
								ForceJumpRemaining = backtrackCheckpoint.ForceJumpRemaining,
								SkipAllOrbs = backtrackCheckpoint.SkipAllOrbs,
								SkipSpecificOrbs = new HashSet<int>(backtrackCheckpoint.SkipSpecificOrbs ?? new HashSet<int>()),
								SkipSpecificPads = new HashSet<int>(backtrackCheckpoint.SkipSpecificPads ?? new HashSet<int>()),
								PrevFrameWasGrounded = backtrackCheckpoint.PrevFrameWasGrounded,
								NextCoinCheckIdx = backtrackCheckpoint.NextCoinCheckIdx
							};
							_shipEntryRecoveryCheckpoint = new BacktrackCheckpoint
							{
								Frame = backtrackCheckpoint.Frame,
								State = backtrackCheckpoint.State.Clone(),
								HoldJumpState = backtrackCheckpoint.HoldJumpState,
								HoldDelayState = backtrackCheckpoint.HoldDelayState,
								CommittedDelayState = backtrackCheckpoint.CommittedDelayState,
								PathPointCount = backtrackCheckpoint.PathPointCount,
								InputCount = backtrackCheckpoint.InputCount,
								RetryStage = 0,
								UsedBias = backtrackCheckpoint.UsedBias,
								ShipBias = backtrackCheckpoint.ShipBias,
								GameMode = backtrackCheckpoint.GameMode,
								ShipForceHold = backtrackCheckpoint.ShipForceHold,
								ShipForceRelease = backtrackCheckpoint.ShipForceRelease,
								ShipCommitFrames = backtrackCheckpoint.ShipCommitFrames,
								ShipCommitHold = backtrackCheckpoint.ShipCommitHold,
								ForceJumpRemaining = backtrackCheckpoint.ForceJumpRemaining,
								SkipAllOrbs = backtrackCheckpoint.SkipAllOrbs,
								SkipSpecificOrbs = new HashSet<int>(backtrackCheckpoint.SkipSpecificOrbs ?? new HashSet<int>()),
								SkipSpecificPads = new HashSet<int>(backtrackCheckpoint.SkipSpecificPads ?? new HashSet<int>()),
								PrevFrameWasGrounded = backtrackCheckpoint.PrevFrameWasGrounded,
								NextCoinCheckIdx = backtrackCheckpoint.NextCoinCheckIdx
							};
							break;
						}
					}
				}
				int gameMode = s.GameMode;
				int velY_fixed = s.VelY_fixed;
				s.GameMode = num;
				s.RainbowMaxMode = 0;
				string value = ApplyGameModePortalVelocityRule(ref s, sid, gameMode, num);
				PfLog($"[PORTAL_GAMEMODE] sid=0x{sid:X2} mode {gameMode} -> {num} VelY {value}: 0x{velY_fixed:X4} -> 0x{s.VelY_fixed:X4}");
				s.WasZeroedByCollision = false;
				s.BallInputBuffer = 0;
				switch (num)
				{
				case 8:
					s.NinjaJumps = 3;
					s.RobotJumpTime = 0;
					if (_speculativeDepth == 0)
					{
						_committedNinjaJumps.Clear();
						_ninjaWaitFrames = 0;
					}
					break;
				case 4:
					s.RobotJumpTime = 0;
					break;
				}
				if (_speculativeDepth == 0 && (num == 1 || num == 3))
				{
					int num3 = ((gameMode == 2) ? 200 : 0);
					if (num3 > 0 && PreferCoins && allCoins.Count > 0)
					{
						int num4 = s.X_fixed >> 8;
						for (int i = _nextCoinCheckIdx; i < allCoins.Count; i++)
						{
							SpriteEntry spriteEntry = allCoins[i];
							if (!s.ProcessedSprites.Contains(spriteEntry.Index) && !_forgivenCoins.Contains(spriteEntry.Index))
							{
								if (spriteEntry.HitLeft > num4 + 600)
								{
									break;
								}
								if (spriteEntry.HitRight >= num4)
								{
									num3 = 20;
									break;
								}
							}
						}
					}
					_modeTransitionStabilizeFrames = num3;
					_log.WriteLine($"[STAB_SET] prevMode={gameMode} mode={num} stab={_modeTransitionStabilizeFrames}");
				}
				UnforgiveCrossCorridorCoins(num);
			}
			return true;
		}
		if (IsSpeedPortal(sid))
		{
			int num5 = SpriteIdToSpeedFixed(sid);
			PfLog($"[PORTAL_SPEED] sid=0x{sid:X2} VelX: 0x{s.VelX_fixed:X4} -> 0x{num5:X4}");
			if (num5 > 0)
			{
				s.VelX_fixed = num5;
			}
			return true;
		}
		if (IsGravityPortal(sid))
		{
			bool flag = IsReverseGravity(sid);
			PfLog($"[PORTAL_GRAV_EVAL] sid=0x{sid:X2} isReverse={flag} gravFlipped={s.GravFlipped}");
			if (flag && !s.GravFlipped)
			{
				int num6 = NesSignedHalf(s.VelY_fixed);
				s.GravFlipped = true;
				s.GravMul = -1;
				PfLog($"[PORTAL_GRAV_FLIP] REVERSED! VelY halved: 0x{s.VelY_fixed:X4} -> 0x{num6:X4}");
				s.VelY_fixed = num6;
				s.WasZeroedByCollision = false;
				return true;
			}
			if (!flag && s.GravFlipped)
			{
				int num7 = NesSignedHalf(s.VelY_fixed);
				s.GravFlipped = false;
				s.GravMul = 1;
				PfLog($"[PORTAL_GRAV_FLIP] NORMAL! VelY halved: 0x{s.VelY_fixed:X4} -> 0x{num7:X4}");
				s.VelY_fixed = num7;
				s.WasZeroedByCollision = false;
				return true;
			}
			return false;
		}
		if (IsMiniGrowthPortal(sid))
		{
			s.Mini = sid == 24;
			return true;
		}
		return true;
	}

	private void ApplyPadSprite(ref SimState s, int sid)
	{
		PfLog($"[PAD] sid=0x{sid:X2} gravFlipped={s.GravFlipped} mode={s.GameMode}");
		int num = (s.GravFlipped ? 1 : (-1));
		if (IsYellowPad(sid))
		{
			s.VelY_fixed = GetPadOrbVel(1, s.Mini, s.GameMode) * num;
			s.OnGround = false;
		}
		else if (IsPinkPad(sid))
		{
			s.VelY_fixed = GetPadOrbVel(3, s.Mini, s.GameMode) * num;
			s.OnGround = false;
		}
		else if (IsRedPad(sid))
		{
			s.VelY_fixed = GetPadOrbVel(8, s.Mini, s.GameMode) * num;
			s.OnGround = false;
		}
		else if (IsBluePad(sid))
		{
			s.GravMul = ((!(s.GravFlipped = sid == 13 || sid == 253)) ? 1 : (-1));
			int num2 = SharedPhysics.BluePadVel(s.Mini);
			s.VelY_fixed = (s.GravFlipped ? (-num2) : num2);
			s.OnGround = false;
		}
		else if (IsGreenPad(sid))
		{
			s.GravFlipped = !s.GravFlipped;
			s.GravMul = ((!s.GravFlipped) ? 1 : (-1));
			int num3 = (s.GravFlipped ? 1 : (-1));
			s.VelY_fixed = GetPadOrbVel(0, s.Mini, s.GameMode) * num3;
			s.OnGround = false;
		}
	}

	private void ApplyOrbSprite(ref SimState s, int sid)
	{
		PfLog($"[ORB_ACTIVATE] sid=0x{sid:X2} gravFlipped={s.GravFlipped} mini={s.Mini}");
		int num = (s.GravFlipped ? 1 : (-1));
		if (IsYellowOrb(sid))
		{
			s.VelY_fixed = GetPadOrbVel(0, s.Mini, s.GameMode) * num;
			s.OnGround = false;
		}
		else if (IsYellowOrbBigger(sid))
		{
			s.VelY_fixed = GetPadOrbVel(5, s.Mini, s.GameMode) * num;
			s.OnGround = false;
		}
		else if (IsYellowOrbSmaller(sid))
		{
			s.VelY_fixed = GetPadOrbVel(7, s.Mini, s.GameMode) * num;
			s.OnGround = false;
		}
		else if (IsPinkOrb(sid))
		{
			s.VelY_fixed = GetPadOrbVel(2, s.Mini, s.GameMode) * num;
			s.OnGround = false;
		}
		else if (IsRedOrb(sid))
		{
			s.VelY_fixed = GetPadOrbVel(4, s.Mini, s.GameMode) * num;
			s.OnGround = false;
		}
		else if (IsBlackOrb(sid))
		{
			s.VelY_fixed = GetPadOrbVel(6, s.Mini, s.GameMode) * num;
			s.OnGround = false;
		}
		else if (IsBlueOrb(sid))
		{
			s.GravFlipped = !s.GravFlipped;
			s.GravMul = ((!s.GravFlipped) ? 1 : (-1));
			if (s.DualActive)
			{
				if (_dualP2Guard)
				{
					_p2OrbFlippedOtherGrav = true;
				}
				else
				{
					s.P2_GravFlipped = !s.P2_GravFlipped;
					s.P2_GravMul = ((!s.P2_GravFlipped) ? 1 : (-1));
					s.P2_VelY_fixed = NesSignedHalf(s.P2_VelY_fixed);
					PfLog($"[DUAL_CAP_CHECK] P1 blue orb flipped P2 grav→{s.P2_GravFlipped} velY→0x{s.P2_VelY_fixed:X4}");
				}
			}
			int num2 = -SharedPhysics.BlueOrbVel(s.Mini, s.GameMode);
			if (!s.GravFlipped)
			{
				num2 = -num2;
			}
			s.VelY_fixed = num2;
			s.OnGround = false;
		}
		else if (IsGreenOrb(sid))
		{
			s.GravFlipped = !s.GravFlipped;
			s.GravMul = ((!s.GravFlipped) ? 1 : (-1));
			if (s.DualActive)
			{
				if (_dualP2Guard)
				{
					_p2OrbFlippedOtherGrav = true;
				}
				else
				{
					s.P2_GravFlipped = !s.P2_GravFlipped;
					s.P2_GravMul = ((!s.P2_GravFlipped) ? 1 : (-1));
					s.P2_VelY_fixed = NesSignedHalf(s.P2_VelY_fixed);
					PfLog($"[DUAL_CAP_CHECK] P1 green orb flipped P2 grav→{s.P2_GravFlipped} velY→0x{s.P2_VelY_fixed:X4}");
				}
			}
			int num3 = (s.GravFlipped ? 1 : (-1));
			s.VelY_fixed = GetPadOrbVel(0, s.Mini, s.GameMode) * num3;
			s.OnGround = false;
		}
		else if (IsWhiteOrb(sid))
		{
			s.VelY_fixed = 0;
		}
	}

	private void ApplyDashOrb(ref SimState s, int sid)
	{
		PfLog($"[DASH_ORB_ACTIVATE] sid=0x{sid:X2} mode={DashOrbMode(sid)} gravFlip={IsGravityDashOrb(sid)}");
		if (IsGravityDashOrb(sid) && s.Dashing == 0)
		{
			s.GravFlipped = !s.GravFlipped;
			s.GravMul = ((!s.GravFlipped) ? 1 : (-1));
		}
		switch (s.Dashing = DashOrbMode(sid))
		{
		case 1:
			s.VelY_fixed = 0;
			break;
		case 2:
			s.VelY_fixed = -s.VelX_fixed;
			break;
		case 3:
			s.VelY_fixed = s.VelX_fixed;
			break;
		case 4:
			s.VelY_fixed = s.VelX_fixed * 4;
			break;
		case 5:
			s.VelY_fixed = -s.VelX_fixed * 4;
			break;
		}
		s.OnGround = false;
	}

	private void ApplySpiderTeleport(ref SimState s, bool goUp)
	{
		PfLog($"[SPIDER_TELEPORT] goUp={goUp} from Y={s.Y_fixed >> 8}");
		int playerX_px = (s.X_fixed >> 8) + 1;
		int num = s.Y_fixed >> 8;
		int hitboxW = GetHitboxW(s.Mini);
		int hitboxH = GetHitboxH(s.Mini);
		int num2 = (s.Mini ? (16 - hitboxH >> 1) : 0);
		if (goUp)
		{
			var (flag, num3) = BgCollD_Spider(playerX_px, num + num2, hitboxW, hitboxH);
			if (flag)
			{
				s.Y_fixed -= num3 << 8;
			}
			s.VelY_fixed = 0;
			s.GravFlipped = true;
			s.GravMul = -1;
			SpiderScanUp(ref s);
			s.VelY_fixed = 0;
		}
		else
		{
			var (flag2, num4) = BgCollU_Spider(playerX_px, num, hitboxW, hitboxH);
			if (flag2)
			{
				s.Y_fixed += num4 + 1 << 8;
			}
			s.VelY_fixed = 0;
			s.GravFlipped = false;
			s.GravMul = 1;
			SpiderScanDown(ref s);
			s.VelY_fixed = 0;
		}
		s.Orbed = true;
		s.SlopeFrames = 0;
		s.SlopeWasOnCounter = 0;
		SnapCameraToPlayerY(ref s);
		PfLog($"[SPIDER_TELEPORT] result Y={s.Y_fixed >> 8} gravFlipped={s.GravFlipped} camY={s.CameraY_fixed >> 8}");
	}

	private void SnapCameraToPlayerY(ref SimState s)
	{
		int num = -(groundRowsToReserve * 16) << 8;
		int num2 = Math.Max(0, (mapHeight - 15) * 16) << 8;
		int num3 = s.Y_fixed - s.CameraY_fixed;
		if (num3 < 16384)
		{
			s.CameraY_fixed -= 16384 - num3;
			if (s.CameraY_fixed < num)
			{
				s.CameraY_fixed = num;
			}
		}
		else if (num3 >> 8 >= 160)
		{
			s.CameraY_fixed += num3 - 40960;
			if (s.CameraY_fixed > num2)
			{
				s.CameraY_fixed = num2;
			}
		}
	}

	private void ApplyTeleportPortal(ref SimState s, SpriteEntry entrance, int entranceSid, int currentX_px)
	{
		bool flag = IsVerticalTeleportEntrance(entranceSid);
		int num = currentX_px - 128;
		int num2 = currentX_px + 128;
		int num3 = -1;
		bool flag2 = false;
		foreach (SpriteEntry allSprite in allSprites)
		{
			int spriteId = allSprite.SpriteId;
			if (!IsTeleportPortalExit(spriteId))
			{
				continue;
			}
			bool flag3 = false;
			if (flag && spriteId == 79)
			{
				flag3 = true;
			}
			else if (!flag && (IsBottomRowTeleportExit(spriteId) || spriteId == 105 || spriteId == 120))
			{
				flag3 = true;
			}
			if (flag3)
			{
				int anchorX_px = allSprite.AnchorX_px;
				if (anchorX_px >= num && anchorX_px <= num2)
				{
					num3 = ((!flag) ? (allSprite.HitTop + 1) : (allSprite.HitTop + 16 + 1));
					flag2 = true;
				}
			}
		}
		if (flag2)
		{
			s.Y_fixed = num3 << 8;
			PfLog($"[TELEPORT_PORTAL] Teleported to Y={num3}");
		}
	}

	private MetatileCollision GetTileCollision(int tileX, int tileY)
	{
		return SharedPhysics.GetTileCollision(in _collisionMap, tileX, tileY);
	}

	private static (int left, int top, int right, int bottom) GetCollisionBounds(MetatileCollision col)
	{
		return SharedPhysics.GetCollisionBounds(col);
	}

	private static bool ProvidesFloorAtColumn(MetatileCollision col, int localX)
	{
		return SharedPhysics.ProvidesFloorAtColumn(col, localX);
	}

	private static bool ProvidesFloorAtColumn(MetatileCollision col, int localX, out int topOffsetPx)
	{
		return SharedPhysics.ProvidesFloorAtColumn(col, localX, out topOffsetPx);
	}

	private static bool IsSlopeTile(MetatileCollision col)
	{
		return SharedPhysics.IsSlopeTile(col);
	}

	private static bool IsMiniBlockType(MetatileCollision col)
	{
		return SharedPhysics.IsMiniBlockType(col);
	}

	private static bool IsMiniBlockFloorHit(MetatileCollision col, int localX, int localY)
	{
		return SharedPhysics.IsMiniBlockFloorHit(col, localX, localY);
	}

	private static int GetMiniBlockFloorSurface(MetatileCollision col)
	{
		return SharedPhysics.GetMiniBlockFloorSurface(col);
	}

	private static bool TileOccupiesPixel(MetatileCollision col, int localX, int localY)
	{
		return SharedPhysics.TileOccupiesPixel(col, localX, localY);
	}

	private static int MapTileForCollision(int tid)
	{
		return SharedPhysics.MapTileForCollision(tid);
	}

	private (bool hit, int surfaceY, bool spikeDeath) CheckFloor(int collX, int collY, int collW, int collH)
	{
		return SharedPhysics.CheckFloor(in _collisionMap, collX, collY, collW, collH);
	}

	private (bool hit, int ceilingBottomY, bool spikeDeath) CheckCeiling(int collX, int collY, int collW, int collH)
	{
		var (item, item2, item3, _) = SharedPhysics.CheckCeiling(in _collisionMap, collX, collY, collW, collH);
		return (hit: item, ceilingBottomY: item2, spikeDeath: item3);
	}

	private bool CheckCenterPointDeath(ref SimState s)
	{
		int playerX_px = s.X_fixed >> 8;
		int playerY_px = s.Y_fixed >> 8;
		return SharedPhysics.CheckCenterPointDeath(in _collisionMap, playerX_px, playerY_px, GetHitboxW(s.Mini), GetHitboxH(s.Mini), GetHitboxOffsetY(s.GameMode, s.Mini, s.GravFlipped));
	}

	private bool PointKillsPlayer(int px, int py, out int dbg_tid, out int dbg_mappedTid, out MetatileCollision dbg_col, out int dbg_localX, out int dbg_localY)
	{
		return SharedPhysics.PointKillsPlayer(in _collisionMap, px, py, out dbg_tid, out dbg_mappedTid, out dbg_col, out dbg_localX, out dbg_localY);
	}

	private bool CheckFloorSpikes(ref SimState s)
	{
		int num = s.X_fixed >> 8;
		int num2 = s.Y_fixed >> 8;
		int hitboxW = GetHitboxW(s.Mini);
		int hitboxH = GetHitboxH(s.Mini);
		int deathX;
		int deathY;
		string cornerName;
		int dbg_tid;
		int dbg_mappedTid;
		MetatileCollision dbg_col;
		int dbg_localX;
		int dbg_localY;
		bool flag = SharedPhysics.CheckFloorSpikes(in _collisionMap, num, num2, hitboxW, hitboxH, s.Mini, out deathX, out deathY, out cornerName, out dbg_tid, out dbg_mappedTid, out dbg_col, out dbg_localX, out dbg_localY);
		if (flag)
		{
			PfLog($"[FLOOR_SPIKE_DEATH] corner {cornerName} ({deathX},{deathY}) tid=0x{dbg_tid:X2} mapped=0x{dbg_mappedTid:X2} col={dbg_col} localXY=({dbg_localX},{dbg_localY})");
			_lastDeathReason = $"FLOOR_SPIKE:{cornerName}({deathX},{deathY})";
			_lastDeathX = num;
			_lastDeathY = num2;
			Console.Error.WriteLine($"[DBG_SPIKE] pX={num} pY={num2} grav={s.GravFlipped} corner={cornerName} pt=({deathX},{deathY}) tid=0x{dbg_tid:X2} col={dbg_col} local=({dbg_localX},{dbg_localY})");
		}
		return flag;
	}

	private bool CheckDeathCollision(ref SimState s)
	{
		int num = s.X_fixed >> 8;
		int num2 = s.Y_fixed >> 8;
		int hitboxW = GetHitboxW(s.Mini);
		int hitboxH = GetHitboxH(s.Mini);
		int hitboxOffsetY = GetHitboxOffsetY(s.GameMode, s.Mini, s.GravFlipped);
		bool flag = SharedPhysics.CheckDeathCollision(in _collisionMap, num, num2, hitboxW, hitboxH, hitboxOffsetY, s.GameMode);
		if (flag)
		{
			int num3 = num + (hitboxW >> 1) - 1;
			int num4 = num2 + hitboxH / 2 + hitboxOffsetY;
			int num5 = num3 / 16;
			int num6 = num4 / 16;
			int num7 = num6 + _collisionMap.GroundRowsToReserve;
			int num8 = 0;
			if (num5 >= 0 && num5 < _collisionMap.MapWidth && num7 >= 0 && num7 < _collisionMap.MapHeight)
			{
				int num9 = num7 * _collisionMap.MapWidth + num5;
				if (num9 >= 0 && num9 < _collisionMap.Tiles.Length)
				{
					num8 = _collisionMap.Tiles[num9];
				}
			}
			MetatileCollision collision = MetatileCollisionTable.GetCollision((byte)MapTileForCollision(num8));
			PfLog($"[DEATH_POINT] center ({num3},{num4}) tile=({num5},{num7}) tid=0x{num8:X2} col={collision}");
			if (_speculativeDepth == 0)
			{
				_lastDeathReason = $"DEATH_COLL:tid=0x{num8:X2}col={collision}";
				_lastDeathX = num;
				_lastDeathY = num2;
			}
		}
		return flag;
	}

	private bool CheckSlopePenetrationDeath(ref SimState s)
	{
		int hitboxW = GetHitboxW(s.Mini);
		int hitboxH = GetHitboxH(s.Mini);
		int hitboxOffsetY = GetHitboxOffsetY(s.GameMode, s.Mini, s.GravFlipped);
		int num = (s.X_fixed >> 8) + (hitboxW >> 1) - 1;
		int num2 = (s.Y_fixed >> 8) + hitboxH / 2 + hitboxOffsetY;
		int tileX = num / 16;
		int tileY = num2 / 16;
		MetatileCollision tileCollision = GetTileCollision(tileX, tileY);
		if (tileCollision >= MetatileCollision.COL_SLOPE_RD45 && tileCollision <= MetatileCollision.COL_SLOPE_LU66_TOP && PfSlopeCalc(num, num2, tileCollision).hit)
		{
			PfLog($"[SLOPE_DEATH] center ({num},{num2}) inside slope {tileCollision}");
			return true;
		}
		return false;
	}

	private bool CheckForwardCollision(ref SimState s)
	{
		if ((s.SlopeWasOnCounter | s.SlopeFrames) != 0)
		{
			return false;
		}
		int num = s.X_fixed >> 8;
		int num2 = s.Y_fixed >> 8;
		int num3;
		int num4;
		int hbOffY;
		if (s.GameMode == 6 || s.GameMode == 10)
		{
			num3 = 8;
			num4 = 8;
			hbOffY = 0;
		}
		else
		{
			num3 = GetHitboxW(s.Mini);
			num4 = GetHitboxH(s.Mini);
			hbOffY = GetHitboxOffsetY(s.GameMode, s.Mini, s.GravFlipped);
		}
		bool flag = SharedPhysics.CheckForwardCollision(in _collisionMap, num, num2, num3, num4, hbOffY, s.GameMode, s.Mini, s.GravFlipped, skipSlopeCheck: true);
		if (!flag && _speculativeDepth > 0)
		{
			int num5 = num + num3;
			int num7;
			if (s.Mini)
			{
				int num6 = 16 - num4 >> 1;
				num7 = num2 + num6 + (num4 >> 1);
				if (s.GameMode == 0 || s.GameMode == 4 || s.GameMode == 8)
				{
					num7 += (s.GravFlipped ? 3 : (-2));
				}
			}
			else
			{
				num7 = num2 + (num4 >> 1);
			}
			int num8 = num5 / 16;
			int num9 = num7 / 16;
			int num10 = num9 + _collisionMap.GroundRowsToReserve;
			if (num8 >= 0 && num8 < _collisionMap.MapWidth && num10 >= 0 && num10 < _collisionMap.MapHeight)
			{
				int tid = _collisionMap.Tiles[num10 * _collisionMap.MapWidth + num8];
				int num11 = MapTileForCollision(tid);
				MetatileCollision collision = MetatileCollisionTable.GetCollision((byte)num11);
				if (collision == MetatileCollision.COL_FLOOR_CEIL || collision == MetatileCollision.COL_NO_SIDE)
				{
					int num12 = num8 * 16;
					int num13 = num9 * 16;
					int localX = Math.Max(0, Math.Min(15, num5 - num12));
					int localY = Math.Max(0, Math.Min(15, num7 - num13));
					if (SharedPhysics.TileOccupiesPixel(collision, localX, localY))
					{
						flag = true;
					}
				}
			}
		}
		if (flag && _frameCounter >= 2020 && _frameCounter <= 2042 && num2 >= 288 && num2 <= 350)
		{
			int num14 = num + num3;
			int num15 = (s.Mini ? (num2 + (16 - num4 >> 1) + (num4 >> 1)) : (num2 + (num4 >> 1)));
			int num16 = num14 / 16;
			int num17 = num15 / 16;
			int num18 = num17 + _collisionMap.GroundRowsToReserve;
			int num19 = -1;
			if (num16 >= 0 && num16 < _collisionMap.MapWidth && num18 >= 0 && num18 < _collisionMap.MapHeight)
			{
				int num20 = num18 * _collisionMap.MapWidth + num16;
				if (num20 >= 0 && num20 < _collisionMap.Tiles.Length)
				{
					num19 = _collisionMap.Tiles[num20];
				}
			}
			int num21 = MapTileForCollision(num19);
			MetatileCollision collision2 = MetatileCollisionTable.GetCollision((byte)num21);
			Console.Error.WriteLine($"[FWD_GAP] f={_frameCounter} X={num} Y={num2} rightEdge={num14} centerY={num15} tile=({num16},{num17}) arrY={num18} tid=0x{num19:X2} mapped=0x{num21:X2} col={collision2} mode={s.GameMode} mini={s.Mini} grav={s.GravFlipped}");
		}
		if (_frameCounter >= 2020 && _frameCounter <= 2042 && !flag && num2 >= 300 && num2 <= 335)
		{
			Console.Error.WriteLine($"[FWD_SURV] f={_frameCounter} X={num} Y={num2} mode={s.GameMode}");
		}
		if (num >= 16380 && num <= 16400 && s.GameMode == 1 && s.Mini)
		{
			int num22 = num + num3;
			int num24;
			if (s.Mini)
			{
				int num23 = 16 - num4 >> 1;
				num24 = num2 + num23 + (num4 >> 1);
			}
			else
			{
				num24 = num2 + (num4 >> 1);
			}
			int num25 = num22 / 16;
			int num26 = num24 / 16;
			int num27 = num26 + _collisionMap.GroundRowsToReserve;
			int value = -1;
			if (num25 >= 0 && num25 < _collisionMap.MapWidth && num27 >= 0 && num27 < _collisionMap.MapHeight)
			{
				int num28 = num27 * _collisionMap.MapWidth + num25;
				if (num28 >= 0 && num28 < _collisionMap.Tiles.Length)
				{
					value = _collisionMap.Tiles[num28];
				}
			}
			PfLog($"[FWD_DIAG] X={num} Y={num2} rightEdge={num22} centerY={num24} tile=({num25},{num26}) tid=0x{value:X2} result={flag} slopeSkip={s.SlopeWasOnCounter}|{s.SlopeFrames}");
		}
		if (flag)
		{
			int num29 = num + num3;
			int num31;
			if (s.Mini)
			{
				int num30 = 16 - num4 >> 1;
				num31 = num2 + num30 + (num4 >> 1);
				if (s.GameMode == 0 || s.GameMode == 4 || s.GameMode == 8 || s.GameMode == 11)
				{
					num31 += (s.GravFlipped ? 3 : (-2));
				}
			}
			else
			{
				num31 = num2 + (num4 >> 1);
			}
			int num32 = num29 / 16;
			int num33 = num31 / 16;
			int num34 = num33 + _collisionMap.GroundRowsToReserve;
			int num35 = 0;
			if (num32 >= 0 && num32 < _collisionMap.MapWidth && num34 >= 0 && num34 < _collisionMap.MapHeight)
			{
				int num36 = num34 * _collisionMap.MapWidth + num32;
				if (num36 >= 0 && num36 < _collisionMap.Tiles.Length)
				{
					num35 = _collisionMap.Tiles[num36];
				}
			}
			MetatileCollision collision3 = MetatileCollisionTable.GetCollision((byte)MapTileForCollision(num35));
			int num37 = num32 * 16;
			int num38 = num33 * 16;
			int num39 = Math.Max(0, Math.Min(15, num29 - num37));
			int num40 = Math.Max(0, Math.Min(15, num31 - num38));
			if (TileOccupiesPixel(collision3, num39, num40))
			{
				PfLog($"[FWD_COLL] probe=({num29},{num31}) tile=({num32},{num33}) tid=0x{num35:X2} col={collision3} oldX={num} newX={s.X_fixed + s.VelX_fixed >> 8}");
			}
			else
			{
				PfLog($"[FWD_SPIKE] probe=({num29},{num31}) tile=({num32},{num33}) tid=0x{num35:X2} col={collision3} local=({num39},{num40})");
			}
		}
		return flag;
	}

	private static (bool hit, int ejection, int slopeType) PfSlopeCalc(int temp_x, int temp_y, MetatileCollision collision)
	{
		return SharedPhysics.SlopeCalc(temp_x, temp_y, collision);
	}

	private static void PfUpdateSlopeCounters(ref SimState s)
	{
		SharedPhysics.UpdateSlopeCounters(ref s.SlopeWasOnCounter, ref s.SlopeType, ref s.VelY_fixed, ref s.Y_fixed, s.GameMode, s.GravFlipped, s.Mini, ref s.LastSlopeType);
	}

	private static void PfUpdateSlopeCounters_Fresh(ref SimState s)
	{
		SharedPhysics.UpdateSlopeCountersFresh(ref s.SlopeFrames, s.SlopeType, ref s.VelY_fixed, s.VelX_fixed);
	}

	private static void PfApplySlopeVelocity(ref SimState s, int slopeType)
	{
		SharedPhysics.ApplySlopeVelocity(ref s.VelY_fixed, slopeType, s.VelX_fixed);
	}

	private static void PfSlopeJumpCheck(ref SimState s)
	{
		SharedPhysics.SlopeJumpCheck(ref s.VelY_fixed, ref s.SlopeJumpHigher, s.SlopeType, s.Mini, s.GravFlipped);
	}

	private (bool hit, int ejection, int slopeType) PfCheckSlopes(ref SimState s, bool input)
	{
		int num = s.X_fixed >> 8;
		int hitboxW = GetHitboxW(s.Mini);
		int hitboxH = GetHitboxH(s.Mini);
		int num2 = (s.Mini ? (16 - hitboxH >> 1) : 0);
		int checkBaseY = (s.Y_fixed >> 8) + num2 + hitboxH - 2;
		return SharedPhysics.CheckSlopesDown(in _collisionMap, num, num, checkBaseY, hitboxW, input, s.GameMode, s.GravFlipped, s.VelX_fixed, ref s.LastSlopeType, ref s.SlopeJumpHigher);
	}

	private (bool hit, int ejection, int slopeType) PfCheckSlopesUp(ref SimState s, bool input)
	{
		int num = s.X_fixed >> 8;
		int hitboxW = GetHitboxW(s.Mini);
		int hitboxH = GetHitboxH(s.Mini);
		int num2 = (s.Mini ? (16 - hitboxH >> 1) : 0);
		int checkBaseY = (s.Y_fixed >> 8) + num2 + (s.Mini ? 1 : 2) + ((s.GameMode == 1) ? 1 : 0);
		return SharedPhysics.CheckSlopesUp(in _collisionMap, num, num, checkBaseY, hitboxW, input, s.GameMode, s.GravFlipped, s.VelX_fixed, ref s.LastSlopeType, ref s.SlopeJumpHigher);
	}
}
