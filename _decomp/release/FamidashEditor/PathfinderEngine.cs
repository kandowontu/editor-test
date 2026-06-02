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

		public int AllocatedSlot;
	}

	private struct SimState
	{
		public int X_fixed;

		public int Y_fixed;

		public int VelY_fixed;

		public int VelX_fixed;

		public int GameMode;

		public bool GravFlipped;

		public bool GravFlippedAtFrameStart;

		public bool OrbUseFrameStartGravitySign;

		public bool Mini;

		public bool ShipDbgCeilSlopeHit;

		public bool ShipDbgCeilTileHit;

		public bool ShipDbgCeilSpike;

		public bool ShipDbgFloorSlopeHit;

		public bool ShipDbgFloorTileHit;

		public bool ShipDbgFloorSpike;

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

		public int ScrollYSubpx;

		public int TargetCameraY_fixed;

		public byte ExitPortalTimer;

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

	private bool _cubeHoldJump;

	private int _cubeHoldDelay;

	private int _committedJumpDelay = -1;

	private int _committedRobotHold;

	private Queue<int> _committedNinjaJumps = new Queue<int>();

	private int _ninjaWaitFrames;

	private bool _prevFrameWasGrounded = true;

	private List<BacktrackCheckpoint> _backtrackCheckpoints = new List<BacktrackCheckpoint>();

	private BacktrackCheckpoint? _lastCubeToShipCheckpoint;

	private BacktrackCheckpoint? _shipEntryRecoveryCheckpoint;

	private int _backtrackAttempts;

	private int _totalBacktrackAttempts;

	private int _btOverrideFrame = -1;

	private int _btOverrideStage;

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
	private static List<int>? _p1OrbIndicesThisFrame;

	[ThreadStatic]
	private static bool _p2OrbFlippedOtherGrav;

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

	private readonly int _minScrollYLin;

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

	private int _modeTransitionStabilizeFrames;

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

	private int _btOrigDeathY;

	private int _shipTreeNodesExplored;

	private int _ballEjectTraceCount;

	private int _step1FireCount;

	private int _step2FireCount;

	private int[]? _gravFDeathCounts;

	private int _gravFDeathFrame = -1;

	private int _gravFDeathSamples;

	private const int PF_SLOPE_RISING = 4;

	private static readonly short[] PF_EXIT_SLOPE_BALL_22 = SharedPhysics.EXIT_SLOPE_BALL_22;

	private static readonly short[] PF_EXIT_SLOPE_BALL_66 = SharedPhysics.EXIT_SLOPE_BALL_66;

	private static readonly short[] PF_EXIT_SLOPE_CUBE_22 = SharedPhysics.EXIT_SLOPE_CUBE_22;

	private const int PF_SLOPE_UD = 8;

	public double JumpTimingBias { get; set; } = 0.5;

	public IProgress<int>? Progress { get; set; }

	public bool PreferCoins { get; set; }

	public int CoinsCollected { get; private set; }

	public HashSet<int>? FinalCollectedCoinIndices { get; private set; }

	public HashSet<int>? SkippedPadIndices { get; private set; }

	public bool UseBFS { get; set; }

	public bool Verbose { get; set; }

	public Action<List<(int x, int y)>?, int, int, bool>? OnSpeculativePath { get; set; }

	public int CurrentSpeculativeVizMode { get; private set; } = -1;

	public int CurrentX_px => _currentX_px;

	public string DebugLogPath => string.Empty;

	public string LevelName { get; set; } = "";

	public int? ConfigScrollYHi { get; set; }

	public int? ConfigScrollYLo { get; set; }

	public int? ConfigSpawnYLo { get; set; }

	private int SpawnYSubpx => ConfigSpawnYLo.GetValueOrDefault() & 0xFF;

	public string FrameTracePath => string.Empty;

	public string OrbDebugLogPath => string.Empty;

	public List<(int x, int y)> PathPoints { get; private set; }

	public List<(int x, int y)> Path2Points { get; private set; }

	public List<bool> Inputs { get; private set; }

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

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int NesPlayerY_px(int yFixed, int camYFixed)
	{
		return yFixed >> 8;
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
		int num = (state.X_fixed >> 8) + 1;
		int hitboxW = GetHitboxW(state.Mini);
		int hitboxH = GetHitboxH(state.Mini);
		int num2 = ((state.Mini && !state.GravFlipped) ? SharedPhysics.GetMiniCenterOffsetY(mini: true) : 0);
		int num3 = (state.Y_fixed >> 8) + num2;
		int num4 = num3 + hitboxH;
		int num5 = num + hitboxW;
		foreach (SpriteEntry allSprite in allSprites)
		{
			if (state.ProcessedSprites.Contains(allSprite.Index) || allSprite.HitRight < num)
			{
				continue;
			}
			if (allSprite.AnchorX_px - 16 > num5 + 16)
			{
				break;
			}
			int spriteId = allSprite.SpriteId;
			if (IsOrbSprite(spriteId))
			{
				bool num6 = num5 >= allSprite.HitLeft && allSprite.HitRight >= num;
				bool flag = num4 >= allSprite.HitTop && allSprite.HitBottom >= num3;
				if (num6 && flag)
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
		int num2 = (state.Y_fixed >> 8) + hitboxOffsetY;
		int num3 = num2 + hitboxH;
		foreach (SpriteEntry allSprite in allSprites)
		{
			if (state.ProcessedSprites.Contains(allSprite.Index) || allSprite.HitRight < num)
			{
				continue;
			}
			int num4 = num + 1 + hitboxW;
			if (allSprite.AnchorX_px - 16 > num4 + 16)
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
			int num5 = ((!IsBluePad(spriteId)) ? 1 : 0);
			int num6 = num + num5;
			bool num7 = num6 + hitboxW >= allSprite.HitLeft && allSprite.HitRight >= num6;
			bool flag2 = num3 >= allSprite.HitTop && allSprite.HitBottom >= num2;
			if (num7 && flag2)
			{
				padSpriteIndex = allSprite.Index;
				return spriteId;
			}
		}
		return -1;
	}

	private static bool IsAnyPad(int sid)
	{
		return SharedPhysics.IsAnyPad(sid);
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
		}
	}

	private int ComputeInitCameraY(int startY_px)
	{
		int val = NesMaxCamY_px() << 8;
		int val2 = -(groundRowsToReserve * 16) << 8;
		if (ConfigScrollYHi.HasValue)
		{
			int num = ConfigScrollYHi.Value & 0xFF;
			int num2 = (ConfigScrollYLo.HasValue ? ConfigScrollYLo.Value : 0) & 0xFF;
			int num3 = num * 240 + num2;
			int num4 = 719 - num3;
			int num5 = (mapHeight - 15) * 16;
			int num6 = Math.Max(0, num5 - num4);
			return Math.Max(val2, Math.Min(val, num6 << 8));
		}
		return Math.Max(val2, Math.Min(val, (startY_px << 8) - 30720));
	}

	private int NesMaxCamY_px()
	{
		int val = (mapHeight - 15) * 16;
		int val2 = 719 - _nesCoordOffset;
		return Math.Max(0, Math.Min(val, val2));
	}

	private int NesNtCameraTarget_fixed(int portalWorldY_px)
	{
		int num = portalWorldY_px + _nesCoordOffset - 58;
		if (num < 0)
		{
			return 0;
		}
		if ((num & 0xFF) >= 240)
		{
			num += 16;
		}
		int num2 = (num >> 8) & 0xFF;
		int num3 = num & 0xFF;
		int num4 = num2 * 240 + num3 - _nesCoordOffset;
		int num5 = NesMaxCamY_px();
		if (num4 > num5)
		{
			num4 = num5;
		}
		return Math.Max(0, num4) << 8;
	}

	private void TraceFrameOpen()
	{
	}

	private void TraceFrame(int frame, ref SimState s, bool input, bool alive)
	{
	}

	private void TraceFrameClose()
	{
	}

	internal void OrbDbg(string msg)
	{
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
		int num = 57 - this.mapHeight;
		if (num < 0)
		{
			num = 0;
		}
		int num2 = 0;
		int num3 = num;
		while (num3 >= 15)
		{
			num3 -= 15;
			num2++;
		}
		int num4 = (num3 * 16) | 8;
		_minScrollYLin = num2 * 240 + num4;
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
		for (int num5 = 0; num5 < this.sprites.Length; num5++)
		{
			int num6 = this.sprites[num5];
			if (num6 == -1)
			{
				continue;
			}
			int num7 = num5 % mapWidth;
			int num8 = num5 / mapWidth;
			int num9 = num6 & 0xFF;
			int sid = num9;
			int num10 = -1;
			bool flag = !IsSpeedPortal(num9) && !IsGameModePortal(num9) && !IsGravityPortal(num9) && !IsMiniGrowthPortal(num9);
			if (this.spriteAnchors.TryGetValue(num5, out (int, int) value))
			{
				num10 = value.Item2 * mapWidth + value.Item1;
				if (flag && num10 >= 0 && num10 < this.sprites.Length)
				{
					int num11 = this.sprites[num10];
					if (num11 >= 0 && num11 < 256)
					{
						sid = num11 & 0xFF;
					}
				}
			}
			sid = SharedPhysics.NormalizePortalGeometrySid(sid);
			int val = ((sid >= 0 && sid < sprite_widths.Length) ? sprite_widths[sid] : 16);
			int val2 = ((sid >= 0 && sid < sprite_heights.Length) ? sprite_heights[sid] : 16);
			int num12 = ((sid >= 0 && sid < sprite_x_offset.Length) ? sprite_x_offset[sid] : 0);
			int num13 = ((sid >= 0 && sid < sprite_y_offset.Length) ? sprite_y_offset[sid] : 0);
			int num14 = 0;
			int num15 = 0;
			(int, int) value3;
			if (num10 >= 0 && this.spritePixelOffsets.TryGetValue(num10, out (int, int) value2))
			{
				(num14, num15) = value2;
			}
			else if (this.spritePixelOffsets.TryGetValue(num5, out value3))
			{
				(num14, num15) = value3;
			}
			int num16 = num7 * 16 + num12 + num14;
			int num17 = (num8 - groundRowsToReserve) * 16 + num13 + num15 - 1;
			int hitRight = num16 + Math.Max(1, val);
			int hitBottom = num17 + Math.Max(1, val2);
			int num18;
			if (this.spriteAnchors.TryGetValue(num5, out (int, int) value4))
			{
				(num18, _) = value4;
			}
			else
			{
				num18 = num7;
			}
			int num19 = num18;
			allSprites.Add(new SpriteEntry
			{
				Index = num5,
				SpriteId = num6,
				AnchorX_px = num19 * 16 + 8 + num14,
				AnchorY_px = (num8 - groundRowsToReserve) * 16 + 8 + num15,
				HitLeft = num16,
				HitTop = num17,
				HitRight = hitRight,
				HitBottom = hitBottom
			});
		}
		allSprites.Sort(delegate(SpriteEntry a, SpriteEntry b)
		{
			int num45 = a.AnchorX_px.CompareTo(b.AnchorX_px);
			if (num45 != 0)
			{
				return num45;
			}
			int num46 = ((!IsGravityPortal(a.SpriteId)) ? 1 : 0);
			int value7 = ((!IsGravityPortal(b.SpriteId)) ? 1 : 0);
			return num46.CompareTo(value7);
		});
		_spritesArr = allSprites.ToArray();
		int num20 = _spritesArr.Length;
		int num21 = this.mapWidth;
		Dictionary<int, (int, int)> dictionary = this.spritePixelOffsets;
		int[] array = new int[num20];
		long[] streamKey = new long[num20];
		int[] array2 = new int[num20];
		for (int num22 = 0; num22 < num20; num22++)
		{
			int index = _spritesArr[num22].Index;
			int num23 = index % num21;
			int num24 = index / num21;
			int num25 = num23 * 16;
			if (dictionary.TryGetValue(index, out var value5))
			{
				num25 += value5.Item1;
			}
			array[num22] = num25;
			streamKey[num22] = ((long)num23 << 32) | (uint)num24;
			array2[num22] = num22;
		}
		Array.Sort(array2, (int a, int b) => streamKey[a].CompareTo(streamKey[b]));
		int[] array3 = new int[16];
		int[] array4 = new int[16];
		for (int num26 = 0; num26 < 16; num26++)
		{
			array3[num26] = -1;
			array4[num26] = int.MinValue;
		}
		int num27 = 0;
		int num28 = 0;
		for (int num29 = 0; num29 < num20; num29++)
		{
			_spritesArr[num29].AllocatedSlot = -1;
		}
		int num30 = 15;
		while (num30 >= 0 && num27 < num20)
		{
			int num31 = (array3[num30] = array2[num27++]);
			array4[num30] = array[num31];
			_spritesArr[num31].AllocatedSlot = num30;
			num28++;
			num30--;
		}
		while (num28 > 0 || num27 < num20)
		{
			int num32 = int.MaxValue;
			for (int num33 = 0; num33 < 16; num33++)
			{
				if (array3[num33] >= 0)
				{
					int num34 = array4[num33] + 1;
					if (num34 < num32)
					{
						num32 = num34;
					}
				}
			}
			if (num32 == int.MaxValue)
			{
				break;
			}
			int num35 = num32;
			for (int num36 = 15; num36 >= 0; num36--)
			{
				int num37 = array3[num36];
				if (num37 < 0 || array4[num36] < num35)
				{
					if (num37 >= 0)
					{
						num28--;
					}
					if (num27 >= num20)
					{
						array3[num36] = -1;
						array4[num36] = int.MinValue;
					}
					else
					{
						int num38 = (array3[num36] = array2[num27++]);
						array4[num36] = array[num38];
						_spritesArr[num38].AllocatedSlot = num36;
						num28++;
					}
				}
			}
		}
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
		int num39 = 0;
		for (int num40 = 0; num40 < this.tiles.Length; num40++)
		{
			num39 = num39 * 31 + this.tiles[num40];
		}
		int num41 = 0;
		for (int num42 = 0; num42 < this.sprites.Length; num42++)
		{
			num41 = num41 * 31 + this.sprites[num42];
		}
		int num43 = 0;
		foreach (KeyValuePair<int, (int, int)> item in this.spritePixelOffsets.OrderBy((KeyValuePair<int, (int offsetX, int offsetY)> x) => x.Key))
		{
			num43 = num43 * 31 + item.Key + item.Value.Item1 * 7 + item.Value.Item2 * 13;
		}
		int num44 = 0;
		foreach (KeyValuePair<int, (int, int)> item2 in this.spriteAnchors.OrderBy((KeyValuePair<int, (int anchorTileX, int anchorTileY)> x) => x.Key))
		{
			num44 = num44 * 31 + item2.Key + item2.Value.Item1 * 7 + item2.Value.Item2 * 13;
		}
		string value6 = $"[PF_DIAG] tiles={this.tiles.Length} tileHash=0x{num39:X8} sprites={this.sprites.Length} sprHash=0x{num41:X8} offsets={this.spritePixelOffsets.Count} offHash=0x{num43:X8} anchors={this.spriteAnchors.Count} anchHash=0x{num44:X8} w={mapWidth} h={mapHeight} ground={groundRowsToReserve} maxFall=0x{this.maxFallSpeed:X} spriteEntries={allSprites.Count}";
		_log.WriteLine(value6);
		_log.Flush();
	}

	public void ReplayInputSequence(int startX_px, int startY_px, int startSpeedUiIndex, int startGameMode, bool startGravFlipped, bool startMini, IList<bool> inputs, int preRollFrames, TextWriter output)
	{
		_log = TextWriter.Null;
		_frameCounter = 0;
		_speculativeDepth = 0;
		_dualP2Guard = false;
		_btSkipAllOrbs = false;
		_btSkipSpecificOrbs.Clear();
		_btSkipSpecificPads.Clear();
		_hitOrbHistory.Clear();
		_autoForgivenCoins.Clear();
		_cubeJumpedThisStep = false;
		_step1FireCount = 0;
		_step2FireCount = 0;
		_gravFDeathCounts = null;
		_gravFDeathFrame = 0;
		_lastDeathReason = "";
		_lastDeathX = 0;
		_lastDeathY = 0;
		TraceFrameOpen();
		int num = ComputeInitCameraY(startY_px);
		SimState s = new SimState
		{
			X_fixed = startX_px << 8,
			Y_fixed = ((startY_px << 8) | SpawnYSubpx),
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
		output.WriteLine("tasFrame,gameFrame,x,y,velY,jump,onGround,event");
		int num2 = preRollFrames + inputs.Count;
		for (int i = 0; i < num2; i++)
		{
			bool flag = i >= preRollFrames && inputs[i - preRollFrames];
			int value = i;
			bool endLevel;
			bool flag2 = StepFrame(ref s, flag, out endLevel);
			TraceFrame(i, ref s, flag, flag2);
			int num3 = s.X_fixed >> 8;
			int value2 = s.Y_fixed >> 8;
			int velY_fixed = s.VelY_fixed;
			bool onGround = s.OnGround;
			string value3 = "";
			if (!flag2)
			{
				value3 = $"DEAD:dt{s.DeathType}";
			}
			else if (endLevel)
			{
				value3 = "END";
			}
			if (!flag2 || endLevel || (num3 >= 12200 && num3 <= 12900))
			{
				output.WriteLine($"{i - preRollFrames},{value},{num3},{value2},0x{velY_fixed & 0xFFFF:X4},{flag},{onGround},{value3}");
			}
			_frameCounter++;
			if (!flag2 || endLevel)
			{
				break;
			}
		}
		output.Flush();
		TraceFrameClose();
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
		if (!PreferCoins)
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
				bool flag = false;
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
							flag = true;
							bool num9 = num8 >= num4 - 30;
							if (num9 && !IsBluePad(allSprite.SpriteId))
							{
								list3.Add((allSprite.HitLeft - 100, allSprite.HitRight + 16));
							}
							if (num9)
							{
								list4.Add((allSprite.HitLeft - 150, allSprite.HitLeft, num4 - 20));
							}
						}
					}
				}
				if (flag)
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
						int num10 = ((FinalCollectedCoinIndices != null) ? FinalCollectedCoinIndices.Count : 0);
						_log.WriteLine($"[COIN_RETRY_COMBINED] Result: {num10}/{allCoins.Count} coins");
						if (num10 > num5)
						{
							_log.WriteLine($"[COIN_RETRY_COMBINED] Improved: {num10} vs {num5}");
							inputs2 = new List<bool>(Inputs);
							pathPoints2 = new List<(int, int)>(PathPoints);
							resultMessage2 = ResultMessage;
							num5 = num10;
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
				int num11 = (item3.HitLeft + item3.HitRight) / 2;
				_ = (item3.HitTop + item3.HitBottom) / 2;
				List<(int, int)> list5 = new List<(int, int)>();
				List<(int, int, int)> list6 = new List<(int, int, int)>();
				bool flag2 = false;
				foreach (SpriteEntry allSprite2 in allSprites)
				{
					if (!IsAnyPad(allSprite2.SpriteId))
					{
						continue;
					}
					int num12 = (allSprite2.HitLeft + allSprite2.HitRight) / 2;
					if (num12 > num11 + 32 || num11 - num12 > 200)
					{
						continue;
					}
					int num13 = (allSprite2.HitTop + allSprite2.HitBottom) / 2;
					if (num13 >= num4 - 40)
					{
						flag2 = true;
						_log.WriteLine($"[COIN_RETRY_PAD] coin={item3.Index} pad idx={allSprite2.Index} sid=0x{allSprite2.SpriteId:X2} hit=({allSprite2.HitLeft},{allSprite2.HitTop})-({allSprite2.HitRight},{allSprite2.HitBottom})");
						bool num14 = num13 >= num4 - 30;
						if (num14 && !IsBluePad(allSprite2.SpriteId))
						{
							list5.Add((allSprite2.HitLeft - 100, allSprite2.HitRight + 16));
						}
						if (num14)
						{
							int item = num4 - 20;
							list6.Add((allSprite2.HitLeft - 150, allSprite2.HitLeft, item));
						}
					}
				}
				if (!flag2)
				{
					continue;
				}
				double[] array = new double[3] { jumpTimingBias, 0.0, 1.0 };
				foreach (double num16 in array)
				{
					_log.WriteLine($"[COIN_RETRY] Attempting coin idx={item3.Index} sid=0x{item3.SpriteId:X2} bias={num16:F2} forceWalk={list5.Count} altPenalty={list6.Count}");
					JumpTimingBias = num16;
					_coinForceWalkZones = list5;
					_coinAltitudePenalties = list6;
					_coinGroundBiasZones.Clear();
					_retryMandatoryCoins.Clear();
					RunSingleAttempt(startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped, startMini);
					if (Success)
					{
						int num17 = ((FinalCollectedCoinIndices != null) ? FinalCollectedCoinIndices.Count : 0);
						_log.WriteLine($"[COIN_RETRY] Retry result: {num17}/{allCoins.Count} coins");
						if (num17 > num5)
						{
							_log.WriteLine($"[COIN_RETRY] Improved: {num17} vs {num5}");
							inputs2 = new List<bool>(Inputs);
							pathPoints2 = new List<(int, int)>(PathPoints);
							resultMessage2 = ResultMessage;
							num5 = num17;
							hashSet = ((FinalCollectedCoinIndices != null) ? new HashSet<int>(FinalCollectedCoinIndices) : null);
							break;
						}
					}
					else
					{
						_log.WriteLine($"[COIN_RETRY] Retry for coin {item3.Index} bias={num16:F2} FAILED");
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
			bool flag3 = list7.All(delegate(SpriteEntry c)
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
			if (list7.Count > 0 && flag3)
			{
				_log.WriteLine($"[SHIP_RETRY_SKIP] All {list7.Count} ship coins have high miss counts \ufffd skipping ship retry");
			}
			if (list7.Count > 0 && !flag3 && num5 < allCoins.Count)
			{
				double[] array = new double[2] { jumpTimingBias, 0.3 };
				foreach (double num18 in array)
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
				double[] array = new double[2] { 0.0, 1.0 };
				foreach (double num20 in array)
				{
					if (num5 >= allCoins.Count)
					{
						break;
					}
					if (Math.Abs(num20 - jumpTimingBias) < 0.05)
					{
						continue;
					}
					_log.WriteLine($"[COIN_RETRY_BALL] Attempting {list8.Count} ball-mode forgiven coins bias={num20:F2}");
					JumpTimingBias = num20;
					_coinForceWalkZones.Clear();
					_coinAltitudePenalties.Clear();
					_coinGroundBiasZones.Clear();
					_retryMandatoryCoins.Clear();
					RunSingleAttempt(startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped, startMini);
					if (Success)
					{
						int num21 = ((FinalCollectedCoinIndices != null) ? FinalCollectedCoinIndices.Count : 0);
						_log.WriteLine($"[COIN_RETRY_BALL] Result: {num21}/{allCoins.Count} coins (bias={num20:F2})");
						if (num21 > num5)
						{
							_log.WriteLine($"[COIN_RETRY_BALL] Improved: {num21} vs {num5}");
							inputs2 = new List<bool>(Inputs);
							pathPoints2 = new List<(int, int)>(PathPoints);
							resultMessage2 = ResultMessage;
							num5 = num21;
							hashSet = ((FinalCollectedCoinIndices != null) ? new HashSet<int>(FinalCollectedCoinIndices) : null);
							break;
						}
					}
					else
					{
						_log.WriteLine($"[COIN_RETRY_BALL] Retry bias={num20:F2} FAILED");
						Success = true;
					}
				}
				JumpTimingBias = jumpTimingBias;
			}
			Inputs = inputs2;
			PathPoints = pathPoints2;
			FinalCollectedCoinIndices = hashSet;
			int num22 = hashSet?.Count ?? 0;
			int num23 = allCoins.Count - num22;
			int num24 = resultMessage2.IndexOf('[');
			string value3 = ((num24 > 0) ? resultMessage2.Substring(0, num24).TrimEnd() : resultMessage2);
			ResultMessage = $"{value3} [{num22}/{allCoins.Count} coins]";
			if (num23 > 0)
			{
				ResultMessage += $" ({num23} unreachable)";
			}
		}
		if (!Success)
		{
			double[] array2 = ((!(jumpTimingBias >= 0.5)) ? new double[4] { 0.25, 0.5, 0.75, 1.0 } : new double[4] { 0.75, 0.5, 0.25, 0.0 });
			double[] array = array2;
			foreach (double num25 in array)
			{
				if (!(Math.Abs(num25 - jumpTimingBias) < 0.01))
				{
					JumpTimingBias = num25;
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
	}

	private bool ReplayInputs(List<bool> inputs, int startX_px, int startY_px, int startSpeedUiIndex, int startGameMode, bool startGravFlipped, bool startMini, out List<(int x, int y)> pathPoints)
	{
		pathPoints = new List<(int, int)>();
		int num = ComputeInitCameraY(startY_px);
		SimState s = new SimState
		{
			X_fixed = startX_px << 8,
			Y_fixed = ((startY_px << 8) | SpawnYSubpx),
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
			bool num2 = StepFrame(ref s, inputs[i], out endLevel);
			int num3 = (s.CameraY_fixed >> 8) + (s.Y_fixed - s.CameraY_fixed >> 8);
			pathPoints.Add(((s.X_fixed >> 8) + 8, num3 + 8));
			if (!num2)
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
			Y_fixed = ((startY_px << 8) | SpawnYSubpx),
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
			int gameMode = s.GameMode;
			_cubeJumpedThisStep = false;
			bool endLevel;
			bool flag9 = StepFrame(ref s, flag6, out endLevel);
			if (gameMode == 0 && flag6 && !_cubeJumpedThisStep && s.GameMode == 0 && s.Dashing == 0)
			{
				Inputs[i] = false;
			}
			TraceFrame(i, ref s, Inputs[i], flag9);
			int num12 = (s.CameraY_fixed >> 8) + (s.Y_fixed - s.CameraY_fixed >> 8);
			PathPoints.Add(((s.X_fixed >> 8) + 8, num12 + 8));
			if (s.DualActive)
			{
				if (!_prevDualActiveForPath && Path2Points.Count > 0)
				{
					Path2Points.Add((-1, -1));
				}
				int num13 = (s.CameraY_fixed >> 8) + (s.P2_Y_fixed - s.CameraY_fixed >> 8);
				Path2Points.Add(((s.X_fixed >> 8) + 8, num13 + 8));
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
					bool num24 = num23 >= spriteEntry2.HitLeft && spriteEntry2.HitRight >= num20;
					bool flag10 = num22 >= spriteEntry2.HitTop && spriteEntry2.HitBottom >= num21;
					if (num24 && flag10)
					{
						s.ProcessedSprites.Add(spriteEntry2.Index);
						if (_speculativeDepth == 0)
						{
							_permanentlyCollectedCoins[spriteEntry2.Index] = i;
							_log.WriteLine($"[COIN_COLLECTED] idx={spriteEntry2.Index} sid=0x{spriteEntry2.SpriteId:X2} gm={s.GameMode} playerX={num20} playerY={num21} coinHit=({spriteEntry2.HitLeft},{spriteEntry2.HitTop})-({spriteEntry2.HitRight},{spriteEntry2.HitBottom})");
						}
					}
				}
			}
			if (!flag9)
			{
				if (_speculativeDepth == 0 && !UseBFS)
				{
					_log.WriteLine($"[DEATH_DBG] frame={i} X={s.X_fixed >> 8} Y={s.Y_fixed >> 8} gm={s.GameMode} reason={_lastDeathReason} dX={_lastDeathX} dY={_lastDeathY} totalBT={_totalBacktrackAttempts}");
				}
				SnapshotBestPath();
				bool flag11 = _lastDeathReason == "MISSED_COIN" && _missedCoinIdx >= 0;
				if (flag11 && list == null)
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
				if (flag11)
				{
					SpriteEntry spriteEntry3 = allCoins[_missedCoinIdx];
					if (_retryMandatoryCoins.Contains(spriteEntry3.Index))
					{
						_log.WriteLine($"[MANDATORY_COIN_DEATH] idx={spriteEntry3.Index} sid=0x{spriteEntry3.SpriteId:X2} \ufffd mandatory coin missed, permanent death");
						UseBestPathIfBetter();
						Success = false;
						int num25 = ((PathPoints.Count > 0) ? PathPoints[PathPoints.Count - 1].x : 0);
						int value7 = ((num2 > 0) ? (num25 * 100 / num2) : 0);
						ResultMessage = $"Permanent death at frame {i} (reason: mandatory coin {spriteEntry3.Index} missed) \ufffd best X {num25}px ({value7}%)";
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
					continue;
				}
				if (_missedCoinIdx < 0 || _missedCoinIdx >= allCoins.Count || list == null)
				{
					UseBestPathIfBetter();
					Success = false;
					int num26 = ((PathPoints.Count > 0) ? PathPoints[PathPoints.Count - 1].x : 0);
					int value8 = ((num2 > 0) ? (num26 * 100 / num2) : 0);
					string text = $"Died \ufffd partial path to X={num26}px ({value8}%) reason={_lastDeathReason} dX={_lastDeathX} dY={_lastDeathY}";
					if (PreferCoins && allCoins.Count > 0)
					{
						int num27 = 0;
						foreach (SpriteEntry allCoin2 in allCoins)
						{
							if (s.ProcessedSprites.Contains(allCoin2.Index))
							{
								num27++;
							}
						}
						text += $" [{num27}/{allCoins.Count} coins]";
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
						bool flag12 = hashSet.Contains(allCoin5.Index);
						_log.WriteLine($"[COIN_RESULT] idx={allCoin5.Index} sid=0x{allCoin5.SpriteId:X2} pos=({allCoin5.AnchorX_px},{allCoin5.AnchorY_px}) hit=({allCoin5.HitLeft},{allCoin5.HitTop})-({allCoin5.HitRight},{allCoin5.HitBottom}) {(flag12 ? "COLLECTED" : "MISSED")}");
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
				return;
			}
		}
		SnapshotBestPath();
		UseBestPathIfBetter();
		ExtractSkippedPads(in s);
		Success = false;
		int num28 = ((PathPoints.Count > 0) ? PathPoints[PathPoints.Count - 1].x : 0);
		int value9 = ((num2 > 0) ? (num28 * 100 / num2) : 0);
		ResultMessage = $"Timeout \ufffd partial path to X={num28}px ({value9}%)";
		TraceFrameClose();
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
		int num = ((!PreferCoins) ? (s.Y_fixed & 0xFFFF) : (flag ? ((s.Y_fixed >> 9) & 0xFFF) : ((s.Y_fixed >> 6) & 0xFFFF)));
		int num2 = ((!PreferCoins) ? (s.VelY_fixed & 0xFFFF) : (flag ? ((s.VelY_fixed + 32768 >> 5) & 0xFFF) : ((s.VelY_fixed + 32768 >> 6) & 0x7FF)));
		int velX_fixed = s.VelX_fixed;
		int num3 = (int)((uint)(s.GameMode & 0xF) | ((s.GravFlipped ? 1u : 0u) << 4) | ((s.Mini ? 1u : 0u) << 5) | ((s.OnGround ? 1u : 0u) << 6) | ((s.Orbed ? 1u : 0u) << 7)) | (((velX_fixed switch
		{
			366 => 0, 
			571 => 1, 
			708 => 2, 
			881 => 3, 
			1065 => 4, 
			1310 => 5, 
			_ => 7, 
		}) & 7) << 8);
		int num4 = s.ProcessedSprites.GetBitsHash();
		if (s.RobotJumpTime > 0)
		{
			num4 = num4 * 31 + s.RobotJumpTime;
		}
		if (s.Step2Ejected)
		{
			num4 = 0;
		}
		if (s.DualActive)
		{
			int num5 = (flag ? ((s.P2_Y_fixed >> 11) & 0x1FF) : ((s.P2_Y_fixed >> 9) & 0x1FF));
			int num6 = (flag ? ((s.P2_VelY_fixed + 32768 >> 8) & 0xFF) : ((s.P2_VelY_fixed + 32768 >> 8) & 0xFF));
			num4 = num4 * 397 + num5;
			num4 = num4 * 397 + num6;
			num4 = num4 * 397 + (s.P2_GravFlipped ? 1 : 0);
		}
		return ((long)(num4 & 0x1FFFF) << 38) | ((long)(num3 & 0x7FF) << 27) | ((long)(num2 & 0x7FF) << 16) | (num & 0xFFFF);
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
		try
		{
			int num = ComputeInitCameraY(startY_px);
			SimState s = new SimState
			{
				X_fixed = startX_px << 8,
				Y_fixed = ((startY_px << 8) | SpawnYSubpx),
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
			for (int frame = 0; frame < 28800; frame++)
			{
				if (frontier.Count <= 0)
				{
					break;
				}
				if (CancelRequested)
				{
					break;
				}
				_frameCounter = frame;
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
				int expandCount = frontier.Count * 2;
				if (expandCount > rState.Length)
				{
					int num14 = expandCount;
					rState = new SimState[num14];
					rAlive = new bool[num14];
					rEnd = new bool[num14];
				}
				Parallel.For(0, expandCount, delegate(int k)
				{
					int index6 = k >> 1;
					bool input2 = (k & 1) == 1;
					SimState s6 = frontier[index6].Clone();
					bool endLevel3;
					bool flag11 = StepFrame(ref s6, input2, out endLevel3);
					if (flag11 && !endLevel3)
					{
						bool num157 = s6.RainbowMaxMode > 0 && frontier[index6].RainbowMaxMode == 0;
						bool flag12 = s6.RainbowShadows != null;
						bool endLevel4;
						if (num157)
						{
							int rainbowMaxMode = s6.RainbowMaxMode;
							SimState[] array10 = new SimState[rainbowMaxMode - 1];
							int num158 = 0;
							for (int i = 0; i < rainbowMaxMode && flag11; i++)
							{
								if (i != s6.GameMode)
								{
									SimState s7 = frontier[index6].Clone();
									s7.GameMode = i;
									s7.RainbowMaxMode = rainbowMaxMode;
									s7.RainbowShadows = null;
									if (frontier[index6].GameMode == 6 || frontier[index6].GameMode == 10)
									{
										s7.VelY_fixed = 0;
									}
									if (!StepFrame(ref s7, input2, out endLevel4))
									{
										s7.ProcessedSprites.Return();
										flag11 = false;
										for (int j = 0; j < num158; j++)
										{
											array10[j].ProcessedSprites.Return();
										}
									}
									else
									{
										array10[num158++] = s7;
									}
								}
							}
							if (flag11)
							{
								s6.RainbowShadows = array10;
							}
						}
						else if (flag12)
						{
							SimState[] rainbowShadows = s6.RainbowShadows;
							for (int l = 0; l < rainbowShadows.Length && flag11; l++)
							{
								if (!StepFrame(ref rainbowShadows[l], input2, out endLevel4))
								{
									flag11 = false;
									rainbowShadows[l].ProcessedSprites.Return();
									for (int m = l + 1; m < rainbowShadows.Length; m++)
									{
										rainbowShadows[m].ProcessedSprites.Return();
									}
									s6.RainbowShadows = null;
								}
							}
							if (flag11 && s6.RainbowMaxMode == 0 && s6.RainbowShadows != null)
							{
								for (int n = 0; n < s6.RainbowShadows.Length; n++)
								{
									s6.RainbowShadows[n].ProcessedSprites.Return();
								}
								s6.RainbowShadows = null;
							}
						}
					}
					rState[k] = s6;
					rAlive[k] = flag11;
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
				bool trackDeathTypes = (frame >= 2000 && frame <= 2070) || (frame >= 3550 && frame <= 3850);
				bool trackAscending = frame >= 4467 && frame <= 4470;
				int[] frameDtCounts = (trackDeathTypes ? new int[13] : null);
				int num18 = 0;
				int workerCount = Math.Min(Environment.ProcessorCount, Math.Max(1, expandCount / 256));
				if (workerCount > expandCount)
				{
					workerCount = expandCount;
				}
				if (workerCount < 1)
				{
					workerCount = 1;
				}
				List<SimState>[] partCandState = new List<SimState>[workerCount];
				List<int>[] partCandParent = new List<int>[workerCount];
				List<bool>[] partCandInput = new List<bool>[workerCount];
				List<int>[] partCandCoins = new List<int>[workerCount];
				List<int>[] partCandScore = new List<int>[workerCount];
				int[] partDeathCount = new int[workerCount];
				int[] partStep8bDeathCount = new int[workerCount];
				int[] partGravFDeathCount = new int[workerCount];
				int[][] partGravFDeathTypes = new int[workerCount][];
				int[][] partFrameDtCounts = ((frameDtCounts != null) ? new int[workerCount][] : null);
				int[] partCoinRangeDeaths = new int[workerCount];
				List<string>[] partAscDeathLogs = (trackAscending ? new List<string>[workerCount] : null);
				List<string>[] partGravDeathLogs = ((frame >= 1890) ? new List<string>[workerCount] : null);
				bool[] partHasWin = new bool[workerCount];
				int[] partWinCoins = new int[workerCount];
				int[] partWinParent = new int[workerCount];
				bool[] partWinInput = new bool[workerCount];
				SimState[] partWinState = new SimState[workerCount];
				Parallel.For(0, workerCount, delegate(int wi)
				{
					int num157 = wi * expandCount / workerCount;
					int num158 = (wi + 1) * expandCount / workerCount;
					int capacity = Math.Max(8, num158 - num157);
					List<SimState> list26 = new List<SimState>(capacity);
					List<int> list27 = new List<int>(capacity);
					List<bool> list28 = new List<bool>(capacity);
					List<int> list29 = new List<int>(capacity);
					List<int> list30 = new List<int>(capacity);
					int num159 = 0;
					int num160 = 0;
					int num161 = 0;
					int[] array10 = new int[13];
					int[] array11 = ((frameDtCounts != null) ? new int[13] : null);
					int num162 = 0;
					List<string> list31 = (trackAscending ? new List<string>() : null);
					List<string> list32 = ((frame >= 1890) ? new List<string>(3) : null);
					bool flag11 = false;
					int num163 = 0;
					int num164 = -1;
					bool flag12 = false;
					SimState simState11 = default(SimState);
					for (int i = num157; i < num158; i++)
					{
						int num165 = i >> 1;
						bool flag13 = (i & 1) == 1;
						if (rEnd[i])
						{
							SimState s6 = rState[i];
							int num166 = CountBfsCoins(ref s6);
							if (!flag11 || num166 > num163)
							{
								flag11 = true;
								num163 = num166;
								num164 = num165;
								flag12 = flag13;
								simState11 = s6;
							}
						}
						else if (!rAlive[i])
						{
							int deathType = rState[i].DeathType;
							if (deathType == 9)
							{
								num160++;
							}
							if (trackAscending)
							{
								SimState simState12 = frontier[num165];
								int velY_fixed2 = simState12.VelY_fixed;
								int num167 = simState12.Y_fixed >> 8;
								if (velY_fixed2 < 0 && num167 < 720)
								{
									SimState simState13 = rState[i];
									list31.Add($"[ASC_DEATH] f={frame} inp={flag13} pX={simState12.X_fixed >> 8} pY={num167} pVelY=0x{velY_fixed2:X} dt={simState13.DeathType} cX={simState13.X_fixed >> 8} cY={simState13.Y_fixed >> 8}");
								}
							}
							if (array11 != null && deathType >= 0 && deathType < array11.Length)
							{
								array11[deathType]++;
							}
							if (trackDeathTypes && frame >= 3550 && frontier[num165].Y_fixed >> 8 <= 200)
							{
								num162++;
							}
							SimState simState14 = frontier[num165];
							if (simState14.GravFlipped)
							{
								num161++;
								if (deathType >= 0 && deathType < array10.Length)
								{
									array10[deathType]++;
								}
								if (list32 != null && list32.Count < 3)
								{
									SimState simState15 = rState[i];
									list32.Add($"[GF_DEAD] f={frame} pX={simState14.X_fixed >> 8} pY={simState14.Y_fixed >> 8} pVelY=0x{simState14.VelY_fixed:X} | cX={simState15.X_fixed >> 8} cY={simState15.Y_fixed >> 8} dt={deathType} inp={flag13}");
								}
							}
							rState[i].ReturnAllSpriteResources();
							num159++;
						}
						else
						{
							SimState s7 = rState[i];
							int num168 = CountBfsCoins(ref s7);
							int item2 = BfsScore(ref s7, num168);
							list26.Add(s7);
							list27.Add(num165);
							list28.Add(flag13);
							list29.Add(num168);
							list30.Add(item2);
						}
					}
					partCandState[wi] = list26;
					partCandParent[wi] = list27;
					partCandInput[wi] = list28;
					partCandCoins[wi] = list29;
					partCandScore[wi] = list30;
					partDeathCount[wi] = num159;
					partStep8bDeathCount[wi] = num160;
					partGravFDeathCount[wi] = num161;
					partGravFDeathTypes[wi] = array10;
					if (partFrameDtCounts != null)
					{
						partFrameDtCounts[wi] = array11;
					}
					partCoinRangeDeaths[wi] = num162;
					if (partAscDeathLogs != null)
					{
						partAscDeathLogs[wi] = list31;
					}
					if (partGravDeathLogs != null)
					{
						partGravDeathLogs[wi] = list32;
					}
					partHasWin[wi] = flag11;
					partWinCoins[wi] = num163;
					partWinParent[wi] = num164;
					partWinInput[wi] = flag12;
					partWinState[wi] = simState11;
				});
				int num19 = 0;
				for (int num20 = 0; num20 < workerCount; num20++)
				{
					if (partHasWin[num20])
					{
						int num21 = partWinCoins[num20];
						if (num5 < 0 || num21 > num7)
						{
							num5 = frame;
							num6 = partWinParent[num20];
							item = partWinInput[num20];
							num7 = num21;
							simState = partWinState[num20];
							_log.WriteLine($"[BFS] Level complete at frame {frame}! coins={num21} X\ufffd{simState.X_fixed >> 8}px");
						}
					}
					List<SimState> list7 = partCandState[num20];
					List<int> list8 = partCandParent[num20];
					List<bool> list9 = partCandInput[num20];
					List<int> list10 = partCandCoins[num20];
					List<int> list11 = partCandScore[num20];
					for (int num22 = 0; num22 < list7.Count; num22++)
					{
						list3.Add(list7[num22]);
						list4.Add(list8[num22]);
						list5.Add(list9[num22]);
						list6.Add(list10[num22]);
						candScore.Add(list11[num22]);
					}
					num15 += partDeathCount[num20];
					num16 += partStep8bDeathCount[num20];
					num17 += partGravFDeathCount[num20];
					num18 += partCoinRangeDeaths[num20];
					int[] array2 = partGravFDeathTypes[num20];
					for (int num23 = 0; num23 < array.Length; num23++)
					{
						array[num23] += array2[num23];
					}
					if (frameDtCounts != null && partFrameDtCounts != null)
					{
						int[] array3 = partFrameDtCounts[num20];
						for (int num24 = 0; num24 < frameDtCounts.Length; num24++)
						{
							frameDtCounts[num24] += array3[num24];
						}
					}
					if (partAscDeathLogs != null)
					{
						List<string> list12 = partAscDeathLogs[num20];
						for (int num25 = 0; num25 < list12.Count; num25++)
						{
							Console.Error.WriteLine(list12[num25]);
						}
					}
					if (partGravDeathLogs == null || num19 >= 3)
					{
						continue;
					}
					List<string> list13 = partGravDeathLogs[num20];
					for (int num26 = 0; num26 < list13.Count; num26++)
					{
						if (num19 >= 3)
						{
							break;
						}
						_log.WriteLine(list13[num26]);
						num19++;
					}
				}
				if (num17 > 0)
				{
					if (_gravFDeathCounts == null)
					{
						_gravFDeathCounts = new int[13];
					}
					for (int num27 = 0; num27 < array.Length && num27 < _gravFDeathCounts.Length; num27++)
					{
						_gravFDeathCounts[num27] += array[num27];
					}
					_gravFDeathFrame = frame;
				}
				if (num16 > 0)
				{
					Console.Error.WriteLine($"[S8B] f={frame} step8b={num16} total={num15} front={frontier.Count} cand={list3.Count} mode={frontier[0].GameMode}");
				}
				if (list3.Count > 0 && list3[0].DualActive)
				{
					int num28 = list3[0].X_fixed >> 8;
					bool flag = num28 >= 3500 && num28 <= 3560;
					bool flag2 = num28 >= 3100 && num28 < 3500 && frame % 10 == 0;
					if (flag || flag2)
					{
						int num29 = 9999;
						int num30 = -1;
						int num31 = 9999;
						int num32 = -1;
						int num33 = 0;
						int num34 = 0;
						int num35 = 0;
						int num36 = 0;
						int num37 = 0;
						int num38 = 0;
						foreach (SimState item3 in list3)
						{
							int num39 = item3.Y_fixed >> 8;
							int num40 = item3.P2_Y_fixed >> 8;
							if (num39 < num29)
							{
								num29 = num39;
							}
							if (num39 > num30)
							{
								num30 = num39;
							}
							if (num40 < num31)
							{
								num31 = num40;
							}
							if (num40 > num32)
							{
								num32 = num40;
							}
							bool num41 = num39 >= 441 && num39 <= 470;
							bool flag3 = num40 >= 357;
							if (num41)
							{
								num33++;
							}
							if (flag3)
							{
								num34++;
							}
							if (num41 && flag3)
							{
								num35++;
							}
							bool num42 = num39 == 401;
							bool flag4 = num40 == 401;
							if (num42)
							{
								num36++;
							}
							if (flag4)
							{
								num37++;
							}
							if (num42 && flag4)
							{
								num38++;
							}
						}
						Console.Error.WriteLine($"[DUAL_Y] f={frame} X={num28} cands={list3.Count} P1:[{num29},{num30}] p1s={num33} on401={num36} P2:[{num31},{num32}] p2s={num34} on401={num37} both={num35} both401={num38}");
						if (flag)
						{
							foreach (SimState item4 in list3)
							{
								int num43 = item4.Y_fixed >> 8;
								int num44 = item4.P2_Y_fixed >> 8;
								int velY_fixed = item4.VelY_fixed;
								int p2_VelY_fixed = item4.P2_VelY_fixed;
								if ((num43 >= 355 && num43 <= 420) || (num44 >= 355 && num44 <= 420))
								{
									Console.Error.WriteLine($"  [ST] X={num28} P1Y={num43} P1V={velY_fixed} P2Y={num44} P2V={p2_VelY_fixed}");
								}
							}
						}
					}
				}
				if (Verbose && frontier.Count <= 10 && frontier.Count > 0 && frontier[0].GameMode == 1 && list3.Count > 0)
				{
					_log.WriteLine($"[SHIP_DIAG] f={frame} front={frontier.Count} cands={list3.Count} deaths={num15}");
					for (int num45 = 0; num45 < Math.Min(list3.Count, 20); num45++)
					{
						SimState s2 = list3[num45];
						long value = BfsQuantizeKey(ref s2);
						_log.WriteLine($"  cand[{num45}] inp={list5[num45]} Y={s2.Y_fixed >> 8} Yfx=0x{s2.Y_fixed:X} VelY=0x{s2.VelY_fixed:X} X={s2.X_fixed >> 8} key=0x{value:X16} score={candScore[num45]} grav={s2.GravFlipped} mode={s2.GameMode}");
					}
					for (int num46 = 0; num46 < Math.Min(frontier.Count, 10); num46++)
					{
						SimState simState2 = frontier[num46];
						_log.WriteLine($"  parent[{num46}] Y={simState2.Y_fixed >> 8} Yfx=0x{simState2.Y_fixed:X} VelY=0x{simState2.VelY_fixed:X} X={simState2.X_fixed >> 8}");
					}
				}
				if (list3.Count == 0)
				{
					_log.WriteLine($"[BFS] ALL DEAD at frame {frame} (X~{num3}px pct={num13}% expanded={frontier.Count * 2} deaths={num15})");
					Console.Error.WriteLine($"[BFS] Frontier before expansion: {frontier.Count} states");
					_log.WriteLine($"[BFS] Frontier before expansion: {frontier.Count} states");
					for (int num47 = 0; num47 < frontier.Count; num47++)
					{
						SimState simState3 = frontier[num47];
						Console.Error.WriteLine($"  [{num47}] X={simState3.X_fixed >> 8} Y={simState3.Y_fixed >> 8} VelY=0x{simState3.VelY_fixed:X} grav={simState3.GravFlipped} mini={simState3.Mini}");
						_log.WriteLine($"  [{num47}] X={simState3.X_fixed >> 8} Y={simState3.Y_fixed >> 8} VelY=0x{simState3.VelY_fixed:X} grav={simState3.GravFlipped} mini={simState3.Mini}");
					}
					int[] array4 = new int[13];
					for (int num48 = 0; num48 < expandCount; num48++)
					{
						if (!rAlive[num48] && !rEnd[num48])
						{
							array4[rState[num48].DeathType]++;
						}
					}
					string[] array5 = new string[13]
					{
						"UNK", "CEIL_SPIKE", "EJECT", "CENTER", "BALL_PROBE", "BALL_VELZERO", "BALL_EJECT", "FLOOR_SPIKE", "FWD", "DEATH_COLL",
						"BOUNDS", "BALL_EJECT_CEIL", "BALL_EJECT_FLOOR"
					};
					List<string> list14 = new List<string>();
					for (int num49 = 0; num49 < array4.Length; num49++)
					{
						if (array4[num49] > 0)
						{
							list14.Add($"{array5[num49]}={array4[num49]}");
						}
					}
					_log.WriteLine("[BFS] Death types: " + string.Join(" ", list14));
					Console.Error.WriteLine("[BFS] Death types: " + string.Join(" ", list14));
					int num50 = 0;
					for (int num51 = 0; num51 < expandCount; num51++)
					{
						if (num50 >= 5)
						{
							break;
						}
						if (!rAlive[num51] && !rEnd[num51])
						{
							SimState simState4 = rState[num51];
							int index = num51 >> 1;
							SimState simState5 = frontier[index];
							_log.WriteLine($"[BFS_DEAD] parent X=0x{simState5.X_fixed:X} Y=0x{simState5.Y_fixed:X} VelY=0x{simState5.VelY_fixed:X} grav={simState5.GravFlipped} mini={simState5.Mini} dual={simState5.DualActive} P2_Y=0x{simState5.P2_Y_fixed:X} P2_grav={simState5.P2_GravFlipped} | child X=0x{simState4.X_fixed:X} Y=0x{simState4.Y_fixed:X} VelY=0x{simState4.VelY_fixed:X} grav={simState4.GravFlipped} mini={simState4.Mini} dt={simState4.DeathType} inp={(num51 & 1) == 1}");
							Console.Error.WriteLine($"[BFS_DEAD] parent Y={simState5.Y_fixed >> 8} VelY=0x{simState5.VelY_fixed:X} grav={simState5.GravFlipped} | child Y={simState4.Y_fixed >> 8} VelY=0x{simState4.VelY_fixed:X} dt={array5[simState4.DeathType]} inp={(num51 & 1) == 1}");
							num50++;
						}
					}
					if (frontier.Count > 0)
					{
						int num52 = int.MaxValue;
						int num53 = int.MinValue;
						int num54 = int.MaxValue;
						int num55 = int.MinValue;
						int num56 = int.MaxValue;
						int num57 = int.MinValue;
						int num58 = int.MaxValue;
						int num59 = int.MinValue;
						foreach (SimState item5 in frontier)
						{
							int num60 = item5.Y_fixed >> 8;
							int num61 = item5.X_fixed >> 8;
							if (num60 < num52)
							{
								num52 = num60;
							}
							if (num60 > num53)
							{
								num53 = num60;
							}
							if (num61 < num56)
							{
								num56 = num61;
							}
							if (num61 > num57)
							{
								num57 = num61;
							}
							if (item5.VelY_fixed < num54)
							{
								num54 = item5.VelY_fixed;
							}
							if (item5.VelY_fixed > num55)
							{
								num55 = item5.VelY_fixed;
							}
							if (item5.DualActive)
							{
								int num62 = item5.P2_Y_fixed >> 8;
								if (num62 < num58)
								{
									num58 = num62;
								}
								if (num62 > num59)
								{
									num59 = num62;
								}
							}
						}
						string value2 = (frontier[0].DualActive ? $" P2_Y=[{num58}..{num59}]" : "");
						_log.WriteLine($"[BFS] Last frontier: size={frontier.Count} X=[{num56}..{num57}] Y=[{num52}..{num53}] mode={frontier[0].GameMode} grav={frontier[0].GravFlipped} mini={frontier[0].Mini} VelY=[0x{num54:X}..0x{num55:X}]{value2}");
						Console.Error.WriteLine($"[BFS] Last frontier: size={frontier.Count} X=[{num56}..{num57}] Y=[{num52}..{num53}] mode={frontier[0].GameMode} grav={frontier[0].GravFlipped} mini={frontier[0].Mini} VelY=[0x{num54:X}..0x{num55:X}]{value2}");
					}
					_log.Flush();
					break;
				}
				if (num5 >= 0)
				{
					bool flag5 = false;
					for (int num63 = 0; num63 < list6.Count; num63++)
					{
						if (list6[num63] > num7)
						{
							flag5 = true;
							break;
						}
					}
					if (!flag5 && PreferCoins && allCoins.Count > 0 && list3.Count > 0)
					{
						int num64 = 0;
						for (int num65 = 0; num65 < list3.Count; num65++)
						{
							int num66 = list3[num65].X_fixed >> 8;
							if (num66 > num64)
							{
								num64 = num66;
							}
						}
						foreach (SpriteEntry allCoin in allCoins)
						{
							if (allCoin.HitRight > num64 && !simState.ProcessedSprites.Contains(allCoin.Index))
							{
								flag5 = true;
								break;
							}
						}
					}
					if (!flag5)
					{
						_log.WriteLine($"[BFS] Optimal \ufffd winning path at frame {num5} with {num7} coins, no better candidates");
						break;
					}
				}
				dictionary.Clear();
				for (int num67 = 0; num67 < list3.Count; num67++)
				{
					SimState s3 = list3[num67];
					long key = BfsQuantizeKey(ref s3);
					if (!dictionary.TryGetValue(key, out var value3) || candScore[num67] < candScore[value3])
					{
						dictionary[key] = num67;
					}
				}
				if (list3.Count > 0 && list3.Count != dictionary.Count)
				{
					Console.Error.WriteLine($"[DEDUP] f={frame} cand={list3.Count} deduped={dictionary.Count} (lost {list3.Count - dictionary.Count})");
				}
				if (frame >= 4400 && frame <= 4600 && dictionary.Count > 0)
				{
					foreach (SimState item6 in list3)
					{
						int num68 = item6.X_fixed >> 8;
						int value4 = item6.Y_fixed >> 8;
						if (num68 >= 12300 && num68 <= 12700)
						{
							Console.Error.WriteLine($"[TRACE] f={frame} X={num68} Y={value4} VelY=0x{item6.VelY_fixed:X}");
						}
					}
					for (int num69 = 0; num69 < expandCount; num69++)
					{
						if (!rAlive[num69] && !rEnd[num69])
						{
							int index2 = num69 >> 1;
							SimState simState6 = frontier[index2];
							int num70 = simState6.X_fixed >> 8;
							if (num70 >= 12300 && num70 <= 12700)
							{
								SimState simState7 = rState[num69];
								Console.Error.WriteLine($"[TDEAD] f={frame} pX={num70} pY={simState6.Y_fixed >> 8} pVelY=0x{simState6.VelY_fixed:X} dt={simState7.DeathType} inp={(num69 & 1) == 1}");
							}
						}
					}
				}
				int num71 = 0;
				int num72 = 0;
				for (int num73 = 0; num73 < list3.Count; num73++)
				{
					if (list3[num73].GravFlipped)
					{
						num71++;
					}
				}
				foreach (int value14 in dictionary.Values)
				{
					if (list3[value14].GravFlipped)
					{
						num72++;
					}
				}
				int num74 = 0;
				foreach (SimState item7 in frontier)
				{
					if (item7.GravFlipped)
					{
						num74++;
					}
				}
				if (num74 > 0 || num71 > 0)
				{
					int num75 = int.MaxValue;
					int num76 = int.MinValue;
					for (int num77 = 0; num77 < list3.Count; num77++)
					{
						if (list3[num77].GravFlipped)
						{
							int num78 = list3[num77].Y_fixed >> 8;
							if (num78 < num75)
							{
								num75 = num78;
							}
							if (num78 > num76)
							{
								num76 = num78;
							}
						}
					}
					string text = "";
					if (num17 > 0)
					{
						string[] array6 = new string[13]
						{
							"UNK", "CEIL", "EJT", "CTR", "BPR", "BVZ", "BEJ", "FLR", "FWD", "DCL",
							"BND", "OOT", "OOB"
						};
						for (int num79 = 0; num79 < array.Length; num79++)
						{
							if (array[num79] > 0)
							{
								text += $" {((num79 < array6.Length) ? array6[num79] : $"d{num79}")}={array[num79]}";
							}
						}
					}
					_log.WriteLine($"[GF_PIPE] f={frame} frontGF={num74} gfDied={num17}{text} candGF={num71} dedupGF={num72} gfY=[{((num75 == int.MaxValue) ? "N/A" : num75.ToString())}..{((num76 == int.MinValue) ? "N/A" : num76.ToString())}]");
				}
				List<int> list15 = new List<int>(dictionary.Values);
				list15.Sort((int a, int b) => candScore[a].CompareTo(candScore[b]));
				List<SimState> list16 = new List<SimState>();
				List<int> list17 = new List<int>();
				List<bool> list18 = new List<bool>();
				int num80 = (PreferCoins ? 120000 : 1073741823);
				int num81 = Math.Min(PreferCoins ? (num80 * 3 / 4) : list15.Count, list15.Count);
				for (int num82 = 0; num82 < num81; num82++)
				{
					if (list16.Count >= num80)
					{
						break;
					}
					int index3 = list15[num82];
					list16.Add(list3[index3]);
					list17.Add(list4[index3]);
					list18.Add(list5[index3]);
				}
				if (!PreferCoins && list15.Count > num81)
				{
					for (int num83 = num81; num83 < list15.Count; num83++)
					{
						if (list16.Count >= num80)
						{
							break;
						}
						int index4 = list15[num83];
						list16.Add(list3[index4]);
						list17.Add(list4[index4]);
						list18.Add(list5[index4]);
					}
				}
				else if (list15.Count > num81)
				{
					int num84 = 16;
					Dictionary<int, int> dictionary2 = new Dictionary<int, int>();
					foreach (SimState item8 in list16)
					{
						int key2 = (item8.Y_fixed >> 8) / num84;
						dictionary2.TryGetValue(key2, out var value5);
						dictionary2[key2] = value5 + 1;
					}
					int num85 = list16.Count / Math.Max(1, dictionary2.Count);
					int num86 = 0;
					int num87 = 0;
					foreach (SimState item9 in list16)
					{
						if (item9.GravFlipped)
						{
							num87++;
						}
						else
						{
							num86++;
						}
					}
					bool flag6 = Math.Min(num86, num87) < list16.Count / 20;
					Dictionary<int, int> dictionary3 = new Dictionary<int, int>();
					foreach (SimState item10 in list16)
					{
						dictionary3.TryGetValue(item10.GameMode, out var value6);
						dictionary3[item10.GameMode] = value6 + 1;
					}
					int count = dictionary3.Count;
					bool flag7 = false;
					int num88 = -1;
					if (count > 1)
					{
						int num89 = int.MaxValue;
						foreach (KeyValuePair<int, int> item11 in dictionary3)
						{
							if (item11.Value < num89)
							{
								num89 = item11.Value;
								num88 = item11.Key;
							}
						}
						flag7 = num89 < list16.Count / 10;
					}
					for (int num90 = num81; num90 < list15.Count; num90++)
					{
						if (list16.Count >= num80)
						{
							break;
						}
						int index5 = list15[num90];
						int key3 = (list3[index5].Y_fixed >> 8) / num84;
						dictionary2.TryGetValue(key3, out var value7);
						bool num91 = value7 < num85 + 1;
						bool flag8 = flag6 && ((list3[index5].GravFlipped && num87 < num86) || (!list3[index5].GravFlipped && num86 < num87));
						bool flag9 = flag7 && list3[index5].GameMode == num88;
						if (num91 || flag8 || flag9)
						{
							list16.Add(list3[index5]);
							list17.Add(list4[index5]);
							list18.Add(list5[index5]);
							dictionary2[key3] = value7 + 1;
							if (list3[index5].GravFlipped)
							{
								num87++;
							}
							else
							{
								num86++;
							}
							flag6 = Math.Min(num86, num87) < list16.Count / 20;
							dictionary3.TryGetValue(list3[index5].GameMode, out var value8);
							dictionary3[list3[index5].GameMode] = value8 + 1;
							if (list3[index5].GameMode == num88)
							{
								flag7 = value8 + 1 < list16.Count / 10;
							}
						}
					}
				}
				int num92 = 0;
				foreach (SimState item12 in list16)
				{
					if (item12.GravFlipped)
					{
						num92++;
					}
				}
				int num93 = 0;
				foreach (int value15 in dictionary.Values)
				{
					if (list3[value15].GravFlipped)
					{
						num93++;
					}
				}
				if (num93 > 0 && num92 != num93)
				{
					_log.WriteLine($"[GF_SELECT] f={frame} dedupGF={num93} selectedGF={num92} totalNext={list16.Count}");
				}
				HashSet<object> hashSet = new HashSet<object>(list16.Count);
				foreach (SimState item13 in list16)
				{
					hashSet.Add(item13.ProcessedSprites);
				}
				for (int num94 = 0; num94 < list3.Count; num94++)
				{
					if (!hashSet.Contains(list3[num94].ProcessedSprites))
					{
						list3[num94].ReturnAllSpriteResources();
					}
				}
				list.Add(list17.ToArray());
				list2.Add(list18.ToArray());
				for (int num95 = 0; num95 < list16.Count; num95++)
				{
					int num96 = list16[num95].X_fixed >> 8;
					if (num96 > num10)
					{
						num8 = list.Count - 1;
						num9 = num95;
						num10 = num96;
					}
				}
				foreach (SimState item14 in frontier)
				{
					item14.ReturnAllSpriteResources();
				}
				frontier = list16;
				bool flag10 = frontier.Count > 0 && frontier[0].DualActive;
				if (frame % 100 == 0 || frontier.Count < 100 || (frame >= 700 && frame <= 810) || flag10 || (frame >= 2000 && frame <= 2070) || (frame >= 3550 && frame <= 3850))
				{
					int num97 = int.MaxValue;
					int num98 = int.MinValue;
					int num99 = 0;
					int num100 = 0;
					foreach (SimState item15 in frontier)
					{
						int num101 = item15.Y_fixed >> 8;
						if (num101 < num97)
						{
							num97 = num101;
						}
						if (num101 > num98)
						{
							num98 = num101;
						}
						if (item15.GravFlipped)
						{
							num100++;
						}
						else
						{
							num99++;
						}
					}
					double value9 = stopwatch.Elapsed.TotalMilliseconds / (double)Math.Max(1, frame + 1);
					string value10 = "";
					if (flag10)
					{
						int num102 = int.MaxValue;
						int num103 = int.MinValue;
						foreach (SimState item16 in frontier)
						{
							int num104 = item16.P2_Y_fixed >> 8;
							if (num104 < num102)
							{
								num102 = num104;
							}
							if (num104 > num103)
							{
								num103 = num104;
							}
						}
						value10 = $" DUAL P2_Y=[{num102}..{num103}]";
					}
					int[] array7 = new int[12];
					foreach (SimState item17 in frontier)
					{
						if (item17.GameMode >= 0 && item17.GameMode < 12)
						{
							array7[item17.GameMode]++;
						}
					}
					string text2 = "";
					for (int num105 = 0; num105 < 12; num105++)
					{
						if (array7[num105] > 0)
						{
							text2 += $" m{num105}={array7[num105]}";
						}
					}
					if (Verbose)
					{
						_log.WriteLine($"[BFS] f={frame} front={frontier.Count} dedup={dictionary.Count} deaths={num15} Y=[{num97}..{num98}] mode={frontier[0].GameMode} gravN={num99} gravF={num100} X~{num3}px pct={num13}% ms/f={value9:F1}{value10} modes:{text2}");
					}
					if (Verbose && frame >= 3550 && frame <= 3850 && frame % 10 == 0)
					{
						int num106 = 0;
						int num107 = 0;
						int num108 = 0;
						int num109 = 0;
						int num110 = 0;
						int num111 = 0;
						int num112 = 0;
						foreach (SimState item18 in frontier)
						{
							int num113 = item18.Y_fixed >> 8;
							if (num113 < 135)
							{
								num106++;
							}
							else if (num113 <= 150)
							{
								num107++;
							}
							else if (num113 <= 167)
							{
								num108++;
								num112++;
							}
							else if (num113 <= 200)
							{
								num109++;
							}
							else if (num113 <= 250)
							{
								num110++;
							}
							else
							{
								num111++;
							}
						}
						_log.WriteLine($"[BFS_COIN_YHIST] f={frame} X~{num3}px <135={num106} 135-150={num107} 151-167={num108} 168-200={num109} 201-250={num110} 251+={num111} coinHitY={num112}");
					}
					if (Verbose && (frame == 700 || frame == 710 || frame == 720 || frame == 800 || frame == 900 || frame == 1500 || frame == 2020 || frame == 2035 || frame == 2040))
					{
						int num114 = 0;
						int num115 = int.MaxValue;
						int num116 = int.MinValue;
						int num117 = int.MaxValue;
						int num118 = int.MinValue;
						for (int num119 = 0; num119 < frontier.Count; num119++)
						{
							SimState simState8 = frontier[num119];
							int num120 = simState8.Y_fixed >> 8;
							if (simState8.Step2Ever)
							{
								num114++;
								if (num120 < num117)
								{
									num117 = num120;
								}
								if (num120 > num118)
								{
									num118 = num120;
								}
							}
							else
							{
								if (num120 < num115)
								{
									num115 = num120;
								}
								if (num120 > num116)
								{
									num116 = num120;
								}
							}
						}
						_log.WriteLine($"[BFS_S2] f={frame} Step2Ever={num114}/{frontier.Count} noS2_Y=[{((num115 == int.MaxValue) ? "N/A" : num115.ToString())}..{((num116 == int.MinValue) ? "N/A" : num116.ToString())}] s2_Y=[{((num117 == int.MaxValue) ? "N/A" : num117.ToString())}..{((num118 == int.MinValue) ? "N/A" : num118.ToString())}]");
					}
					if (Verbose && frame >= 2020 && frame <= 2042 && frame % 2 == 0)
					{
						int num121 = 0;
						int num122 = 0;
						int num123 = 0;
						int num124 = 0;
						int num125 = 0;
						int num126 = 0;
						int num127 = 0;
						int num128 = 0;
						for (int num129 = 0; num129 < frontier.Count; num129++)
						{
							int num130 = frontier[num129].Y_fixed >> 8;
							if (num130 < 288)
							{
								num121++;
							}
							else if (num130 < 304)
							{
								num122++;
							}
							else if (num130 < 320)
							{
								num123++;
							}
							else if (num130 < 336)
							{
								num124++;
								if (frontier[num129].GravFlipped)
								{
									num128++;
								}
								else
								{
									num127++;
								}
							}
							else if (num130 < 352)
							{
								num125++;
							}
							else
							{
								num126++;
							}
						}
						_log.WriteLine($"[BFS_YHIST] f={frame} <288={num121} 288-303={num122} 304-319={num123} 320-335={num124}(gN={num127},gF={num128}) 336-351={num125} 352+={num126}");
						if (num124 > 0)
						{
							int num131 = 0;
							int num132 = 0;
							int num133 = int.MaxValue;
							int num134 = int.MinValue;
							for (int num135 = 0; num135 < frontier.Count; num135++)
							{
								int num136 = frontier[num135].Y_fixed >> 8;
								if (num136 >= 320 && num136 < 336)
								{
									int num137 = frontier[num135].X_fixed >> 8;
									if (num137 < num133)
									{
										num133 = num137;
									}
									if (num137 > num134)
									{
										num134 = num137;
									}
									if (num137 >= num3 - 200)
									{
										num131++;
									}
									else
									{
										num132++;
									}
								}
							}
							_log.WriteLine($"[BFS_GAPX] f={frame} count={num124} X=[{num133}..{num134}] near(within200)={num131} far={num132}");
						}
					}
					if (Verbose)
					{
						_log.Flush();
					}
					if (Verbose && frame >= 703 && frame <= 810)
					{
						long num138 = 0L;
						for (int num139 = 0; num139 < frontier.Count; num139++)
						{
							SimState simState9 = frontier[num139];
							num138 = num138 * 31 + simState9.X_fixed + (long)simState9.Y_fixed * 7L + (long)simState9.VelY_fixed * 13L + (simState9.GravFlipped ? 1 : 0) + (long)simState9.GameMode * 37L;
						}
						string value11 = $"[BFS_HASH] f={frame} frontHash=0x{num138:X16} first=(X=0x{frontier[0].X_fixed:X},Y=0x{frontier[0].Y_fixed:X},VelY=0x{frontier[0].VelY_fixed:X},mode={frontier[0].GameMode}) last=(X=0x{frontier[frontier.Count - 1].X_fixed:X},Y=0x{frontier[frontier.Count - 1].Y_fixed:X},VelY=0x{frontier[frontier.Count - 1].VelY_fixed:X},mode={frontier[frontier.Count - 1].GameMode})";
						_log.WriteLine(value11);
						_log.Flush();
					}
					if (Verbose && frameDtCounts != null && num15 > 0)
					{
						string[] array8 = new string[13]
						{
							"UNK", "CEIL_SPIKE", "EJECT", "CENTER", "BALL_PROBE", "BALL_VELZERO", "BALL_EJECT", "FLOOR_SPIKE", "FWD", "DEATH_COLL",
							"BOUNDS", "BALL_EJECT_CEIL", "BALL_EJECT_FLOOR"
						};
						List<string> list19 = new List<string>();
						for (int num140 = 0; num140 < array8.Length && num140 < frameDtCounts.Length; num140++)
						{
							if (frameDtCounts[num140] > 0)
							{
								list19.Add($"{array8[num140]}={frameDtCounts[num140]}");
							}
						}
						string value12 = ((frame >= 3550 && frame <= 3850) ? $" lowYDeaths={num18}" : "");
						_log.WriteLine($"[BFS_DT] f={frame} DEATH_TYPES: {string.Join(" ", list19)}{value12}");
						_log.WriteLine($"[BFS_DT] f={frame} DEATH_TYPES: {string.Join(" ", list19)}");
					}
					if (_gravFDeathCounts != null && _gravFDeathFrame == frame)
					{
						string[] array9 = new string[13]
						{
							"UNK", "CEIL_SPIKE", "EJECT", "CENTER", "BALL_PROBE", "BALL_VELZERO", "BALL_EJECT", "FLOOR_SPIKE", "FWD", "DEATH_COLL",
							"BOUNDS", "OOB_TOP", "OOB_BOT"
						};
						List<string> list20 = new List<string>();
						int num141 = 0;
						for (int num142 = 0; num142 < _gravFDeathCounts.Length; num142++)
						{
							if (_gravFDeathCounts[num142] > 0)
							{
								list20.Add($"{array9[num142]}={_gravFDeathCounts[num142]}");
								num141 += _gravFDeathCounts[num142];
							}
						}
						_log.WriteLine($"[GRAVF_SUMMARY] f={frame} total={num141} {string.Join(" ", list20)}");
						_gravFDeathCounts = null;
						_gravFDeathSamples = 0;
					}
				}
				if (OnSpeculativePath == null || frontier.Count <= 0 || frame % 5 != 0)
				{
					continue;
				}
				int num143 = Math.Min(8, frontier.Count);
				int num144 = Math.Max(1, frontier.Count / num143);
				_speculativeDepth++;
				for (int num145 = 0; num145 < frontier.Count && num145 / num144 < num143; num145 += num144)
				{
					SimState simState10 = frontier[num145];
					int hitboxW = GetHitboxW(simState10.Mini);
					SimState s4 = simState10.Clone();
					List<(int, int)> list21 = new List<(int, int)>();
					int arg = 0;
					for (int num146 = 0; num146 < 30; num146++)
					{
						int num147 = s4.X_fixed >> 8;
						int num148 = s4.Y_fixed >> 8;
						list21.Add((num147 + hitboxW / 2, num148 + 8));
						if (!StepFrame(ref s4, input: false, out var endLevel) || endLevel)
						{
							break;
						}
						arg = num146 + 1;
					}
					s4.ReturnAllSpriteResources();
					if (list21.Count >= 2)
					{
						OnSpeculativePath(list21, 0, arg, arg4: false);
					}
					SimState s5 = simState10.Clone();
					List<(int, int)> list22 = new List<(int, int)>();
					int arg2 = 0;
					for (int num149 = 0; num149 < 30; num149++)
					{
						int num150 = s5.X_fixed >> 8;
						int num151 = s5.Y_fixed >> 8;
						list22.Add((num150 + hitboxW / 2, num151 + 8));
						bool input = num149 == 0;
						if (!StepFrame(ref s5, input, out var endLevel2) || endLevel2)
						{
							break;
						}
						arg2 = num149 + 1;
					}
					s5.ReturnAllSpriteResources();
					if (list22.Count >= 2)
					{
						OnSpeculativePath(list22, 0, arg2, arg4: true);
					}
				}
				_speculativeDepth--;
			}
			_speculativeDepth = 0;
			if (num5 >= 0)
			{
				List<bool> list23 = new List<bool>();
				int num152 = num6;
				for (int num153 = num5 - 1; num153 >= 0; num153--)
				{
					list23.Add(list2[num153][num152]);
					num152 = list[num153][num152];
				}
				list23.Reverse();
				list23.Add(item);
				_log.WriteLine($"[BFS] Replaying winning path ({list23.Count} frames)...");
				ReplayBfsPath(list23, startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped, startMini);
				int count2 = allCoins.Count;
				if (PreferCoins && count2 > 0 && FinalCollectedCoinIndices != null && FinalCollectedCoinIndices.Count < count2)
				{
					List<bool> list24 = TryCoinBeamSplice(list23, startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped, startMini);
					if (list24 != null)
					{
						list23 = list24;
						ReplayBfsPath(list23, startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped, startMini);
					}
				}
				double totalSeconds = stopwatch.Elapsed.TotalSeconds;
				string text3 = $"Completed in {list23.Count} frames ({PathPoints.Count} path points)";
				if (PreferCoins && count2 > 0)
				{
					text3 += $" [{FinalCollectedCoinIndices?.Count ?? num7}/{count2} coins]";
				}
				text3 = (ResultMessage = text3 + $" [{totalSeconds:F1}s BFS]");
				Success = true;
				_log.WriteLine("[BFS] " + text3);
			}
			else
			{
				_speculativeDepth = 0;
				if (num9 >= 0 && list.Count > 0)
				{
					List<bool> list25 = new List<bool>();
					int num154 = num9;
					for (int num155 = num8; num155 >= 0; num155--)
					{
						list25.Add(list2[num155][num154]);
						num154 = list[num155][num154];
					}
					list25.Reverse();
					ReplayBfsPath(list25, startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped, startMini);
				}
				int num156 = ((PathPoints.Count > 0) ? PathPoints[PathPoints.Count - 1].x : 0);
				int value13 = ((num2 > 0) ? (num156 * 100 / num2) : 0);
				ResultMessage = $"BFS failed ~ best path to X={num156}px ({value13}%)";
				Success = false;
				_log.WriteLine("[BFS] " + ResultMessage);
				_log.WriteLine($"[BFS_DIAG] Step1 fires={_step1FireCount}, Step2 fires={_step2FireCount}");
			}
		}
		catch (Exception ex)
		{
			_speculativeDepth = 0;
			Exception ex2 = ex;
			while (ex2 is AggregateException ex3 && ex3.InnerExceptions.Count > 0)
			{
				ex2 = ex3.InnerExceptions[0];
			}
			_log.WriteLine("[BFS] EXCEPTION: " + ex2.GetType().Name + ": " + ex2.Message);
			_log.WriteLine(ex2.StackTrace);
			ResultMessage = "BFS crashed: " + ex2.GetType().Name + ": " + ex2.Message;
			Success = false;
		}
		stopwatch.Stop();
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
			Y_fixed = ((startY_px << 8) | SpawnYSubpx),
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
			int gameMode = s.GameMode;
			_cubeJumpedThisStep = false;
			bool endLevel;
			bool flag2 = StepFrame(ref s, flag, out endLevel);
			if (gameMode == 0 && flag && !_cubeJumpedThisStep && s.GameMode == 0 && s.Dashing == 0)
			{
				Inputs[i] = false;
			}
			TraceFrame(i, ref s, Inputs[i], flag2);
			_ = s.Mini;
			int num2 = (s.CameraY_fixed >> 8) + (s.Y_fixed - s.CameraY_fixed >> 8);
			PathPoints.Add(((s.X_fixed >> 8) + 8, num2 + 8));
			if (s.DualActive)
			{
				if (!_prevDualActiveForPath && Path2Points.Count > 0)
				{
					Path2Points.Add((-1, -1));
				}
				_ = s.P2_Mini;
				int num3 = (s.CameraY_fixed >> 8) + (s.P2_Y_fixed - s.CameraY_fixed >> 8);
				Path2Points.Add(((s.X_fixed >> 8) + 8, num3 + 8));
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

	private List<bool>? TryCoinBeamSplice(List<bool> originalInputs, int startX_px, int startY_px, int startSpeedUiIndex, int startGameMode, bool startGravFlipped, bool startMini)
	{
		List<SpriteEntry> list = new List<SpriteEntry>();
		HashSet<int> hashSet = FinalCollectedCoinIndices ?? new HashSet<int>();
		foreach (SpriteEntry allCoin in allCoins)
		{
			if (!hashSet.Contains(allCoin.Index))
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
			Y_fixed = ((startY_px << 8) | SpawnYSubpx),
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
				if (!StepFrame(ref s2, originalInputs[i], out var endLevel) || endLevel)
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
								int num9 = k;
								for (int num10 = list2.Count - 1; num10 >= 0; num10--)
								{
									list7.Add(list2[num10].Item2[num9]);
									num9 = list2[num10].Item1[num9];
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
							int num11 = s3.X_fixed >> 8;
							int num12 = s3.Y_fixed >> 8;
							int item2;
							if (s3.ProcessedSprites.Contains(item3.Index))
							{
								item2 = -1000000;
							}
							else
							{
								int num13 = Math.Abs(num12 - num2);
								int num14 = num11 - num;
								item2 = ((num14 <= 100) ? (num13 * 10) : (500000 + num14));
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
				for (int num15 = 0; num15 < array2.Length; num15++)
				{
					int index = array[num15];
					list8.Add(list4[index]);
					array2[num15] = list5[index];
					array3[num15] = list6[index];
				}
				for (int num16 = array2.Length; num16 < array.Length; num16++)
				{
					list4[array[num16]].ReturnAllSpriteResources();
				}
				list2.Add((array2, array3));
				list3 = list8;
				if (list3[0].ProcessedSprites.Contains(item3.Index))
				{
					int num17 = list3[0].X_fixed >> 8;
					if (num17 > num4)
					{
						num8 = 0;
						_log.WriteLine($"[COIN_SPLICE] Beam collected coin at bf={j} X={num17} Y={list3[0].Y_fixed >> 8}");
						break;
					}
				}
				if (j % 20 != 0)
				{
					continue;
				}
				int num18 = int.MaxValue;
				int num19 = int.MinValue;
				int num20 = 0;
				foreach (SimState item7 in list3)
				{
					int num21 = item7.Y_fixed >> 8;
					if (num21 < num18)
					{
						num18 = num21;
					}
					if (num21 > num19)
					{
						num19 = num21;
					}
					if (item7.ProcessedSprites.Contains(item3.Index))
					{
						num20++;
					}
				}
				_log.WriteLine($"[COIN_SPLICE] bf={j} beam={list3.Count} Y=[{num18}..{num19}] collected={num20} X~{list3[0].X_fixed >> 8}");
			}
			_speculativeDepth = 0;
			if (num8 < 0)
			{
				_log.WriteLine($"[COIN_SPLICE] Beam failed to collect coin idx={item3.Index}");
				foreach (SimState item8 in list3)
				{
					item8.ReturnAllSpriteResources();
				}
				continue;
			}
			List<bool> list9 = new List<bool>();
			int num22 = num8;
			for (int num23 = list2.Count - 1; num23 >= 0; num23--)
			{
				list9.Add(list2[num23].Item2[num22]);
				num22 = list2[num23].Item1[num22];
			}
			list9.Reverse();
			SimState startState = list3[num8];
			int num24 = num5 + list9.Count;
			_log.WriteLine($"[COIN_SPLICE] Trying tail with original inputs from f={num24} (total={originalInputs.Count})");
			bool flag = false;
			SimState s4 = startState.Clone();
			_speculativeDepth = 1;
			bool flag2 = false;
			for (int num25 = num24; num25 < originalInputs.Count; num25++)
			{
				_frameCounter = num25;
				bool endLevel3;
				bool flag3 = StepFrame(ref s4, originalInputs[num25], out endLevel3);
				if (endLevel3)
				{
					flag2 = true;
					break;
				}
				if (!flag3)
				{
					_log.WriteLine($"[COIN_SPLICE] Tail died at f={num25} (df={num25 - num24}) X={s4.X_fixed >> 8} Y={s4.Y_fixed >> 8} dt={s4.DeathType}");
					break;
				}
			}
			_speculativeDepth = 0;
			if (flag2)
			{
				flag = true;
				_log.WriteLine("[COIN_SPLICE] Tail reached level end! Splicing...");
			}
			s4.ReturnAllSpriteResources();
			if (!flag)
			{
				_log.WriteLine($"[COIN_SPLICE] Trying survival beam from f={num24}...");
				List<bool> list10 = RunSurvivalBeam(startState, num24, originalInputs.Count + 3000);
				if (list10 != null)
				{
					_log.WriteLine($"[COIN_SPLICE] Survival beam succeeded! len={list10.Count}");
					List<bool> list11 = new List<bool>(num5 + list9.Count + list10.Count);
					for (int num26 = 0; num26 < num5; num26++)
					{
						list11.Add(originalInputs[num26]);
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
			if (!flag)
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
		for (int j = spliceFrame + beamInputs.Count; j < original.Count; j++)
		{
			list.Add(original[j]);
		}
		return list;
	}

	private List<bool>? RunSurvivalBeam(SimState startState, int startFrame, int maxFrames)
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
						{
							foreach (SimState item3 in list3)
							{
								item3.ReturnAllSpriteResources();
							}
							return list6;
						}
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
				if (value >= 3 && _btDeathFrame - backtrackCheckpoint.Frame < 200)
				{
					_backtrackCheckpoints.RemoveAt(num);
					continue;
				}
			}
			backtrackCheckpoint.RetryStage++;
			int num5;
			if (backtrackCheckpoint.GameMode == 1 || backtrackCheckpoint.GameMode == 3)
			{
				if (flag)
				{
					int num4 = _btDeathFrame - backtrackCheckpoint.Frame;
					num5 = ((num4 < 150) ? 2 : ((num4 < 300) ? 4 : ((num4 >= 700) ? 4 : 23)));
				}
				else
				{
					int num6 = _btDeathFrame - backtrackCheckpoint.Frame;
					num5 = ((num6 <= 60) ? 23 : ((num6 > 240) ? 4 : 8));
				}
			}
			else if (backtrackCheckpoint.GameMode == 2 || backtrackCheckpoint.GameMode == 5 || backtrackCheckpoint.GameMode == 7)
			{
				num5 = 23;
			}
			else
			{
				int num7 = _btDeathFrame - backtrackCheckpoint.Frame;
				int num8 = backtrackCheckpoint.State.Y_fixed >> 8;
				if (_btOrigDeathReason == "FWD_DEATH" && Math.Abs(num8 - _btOrigDeathY) < 12 && num7 > 30)
				{
					num5 = 1;
				}
				else if (!flag)
				{
					num5 = ((num7 <= 60) ? 23 : ((num7 > 240) ? 4 : 8));
				}
				else
				{
					SpriteEntry spriteEntry = allCoins[_missedCoinIdx];
					int num9 = (spriteEntry.HitTop + spriteEntry.HitBottom) / 2;
					num5 = ((num8 - num9 > 40 && num7 <= 90) ? 4 : ((num7 > 60) ? 23 : 23));
				}
			}
			if (backtrackCheckpoint.RetryStage > num5)
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
					int num5 = ((num3 - num4 >= 0) ? 1 : (-1));
					int num6 = Math.Max(6, btOverrideDistFromDeath / 2);
					switch (btOverrideStage)
					{
					case 1:
						_shipCorridorBias = num5 * 60;
						break;
					case 2:
						if (num5 > 0)
						{
							_shipForceReleaseFrames = num6;
						}
						else
						{
							_shipForceHoldFrames = num6;
						}
						_shipCorridorBias = num5 * 120;
						break;
					case 3:
						_shipCorridorBias = num5 * 160;
						break;
					case 4:
						if (num5 > 0)
						{
							_shipForceReleaseFrames = num6 * 2;
						}
						else
						{
							_shipForceHoldFrames = num6 * 2;
						}
						_shipCorridorBias = num5 * 200;
						break;
					case 5:
						_shipCorridorBias = -num5 * 60;
						break;
					case 6:
						if (num5 > 0)
						{
							_shipForceHoldFrames = num6;
						}
						else
						{
							_shipForceReleaseFrames = num6;
						}
						_shipCorridorBias = -num5 * 120;
						break;
					case 7:
						if (num5 > 0)
						{
							_shipForceReleaseFrames = num6 * 3;
						}
						else
						{
							_shipForceHoldFrames = num6 * 3;
						}
						_shipCorridorBias = num5 * 250;
						break;
					case 8:
						_shipCorridorBias = num5 * 300;
						break;
					case 9:
						if (num5 > 0)
						{
							_shipForceReleaseFirstFrames = num6;
							_shipForceHoldFrames = num6;
						}
						else
						{
							_shipForceHoldFrames = num6;
							_shipForceReleaseFrames = num6;
						}
						_shipCorridorBias = num5 * 100;
						break;
					case 10:
						if (num5 > 0)
						{
							_shipForceReleaseFirstFrames = num6 * 2;
							_shipForceHoldFrames = num6;
						}
						else
						{
							_shipForceHoldFrames = num6 * 2;
							_shipForceReleaseFrames = num6;
						}
						_shipCorridorBias = num5 * 150;
						break;
					case 11:
						if (num5 > 0)
						{
							_shipForceReleaseFirstFrames = num6 * 2;
							_shipForceHoldFrames = num6 * 2;
						}
						else
						{
							_shipForceHoldFrames = num6 * 2;
							_shipForceReleaseFrames = num6 * 2;
						}
						_shipCorridorBias = num5 * 200;
						break;
					case 12:
						if (num5 > 0)
						{
							_shipForceReleaseFirstFrames = num6 * 3;
							_shipForceHoldFrames = num6;
						}
						else
						{
							_shipForceHoldFrames = num6 * 3;
							_shipForceReleaseFrames = num6;
						}
						_shipCorridorBias = num5 * 250;
						break;
					case 13:
						if (num5 > 0)
						{
							_shipForceReleaseFirstFrames = num6 * 3;
							_shipForceHoldFrames = num6 * 3;
						}
						else
						{
							_shipForceHoldFrames = num6 * 3;
							_shipForceReleaseFrames = num6 * 3;
						}
						_shipCorridorBias = num5 * 300;
						break;
					case 14:
						if (num5 > 0)
						{
							_shipForceReleaseFirstFrames = num6;
							_shipForceHoldFrames = num6 * 2;
						}
						else
						{
							_shipForceHoldFrames = num6;
							_shipForceReleaseFrames = num6 * 2;
						}
						_shipCorridorBias = num5 * 80;
						break;
					case 15:
						if (num5 > 0)
						{
							_shipForceReleaseFirstFrames = num6 * 4;
							_shipForceHoldFrames = num6 * 2;
						}
						else
						{
							_shipForceHoldFrames = num6 * 4;
							_shipForceReleaseFrames = num6 * 2;
						}
						_shipCorridorBias = num5 * 350;
						break;
					case 16:
						if (num5 > 0)
						{
							_shipForceHoldFrames = num6 * 2;
							_shipForceReleaseFrames = num6 * 2;
						}
						else
						{
							_shipForceReleaseFirstFrames = num6 * 2;
							_shipForceHoldFrames = num6 * 2;
						}
						_shipCorridorBias = -num5 * 200;
						break;
					}
				}
				else
				{
					int num7 = Math.Max(6, btOverrideDistFromDeath / 2);
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
						_shipForceHoldFrames = num7;
						break;
					case 6:
						_shipCorridorBias = 120;
						_shipForceReleaseFrames = num7;
						break;
					case 7:
						_shipCorridorBias = -200;
						_shipForceHoldFrames = num7 * 2;
						break;
					case 8:
						_shipCorridorBias = 200;
						_shipForceReleaseFrames = num7 * 2;
						break;
					case 9:
						_shipForceReleaseFirstFrames = num7;
						_shipForceHoldFrames = num7;
						_shipCorridorBias = -150;
						break;
					case 10:
						_shipForceHoldFrames = num7;
						_shipForceReleaseFrames = num7;
						_shipCorridorBias = 150;
						break;
					case 11:
						_shipCorridorBias = -300;
						_shipForceHoldFrames = num7 * 3;
						break;
					case 12:
						_shipCorridorBias = 300;
						_shipForceReleaseFrames = num7 * 3;
						break;
					}
				}
				return DecideShipInput(state);
			}
			if (state.GameMode == 2)
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
				return false;
			case 2:
				return true;
			case 3:
			{
				double jumpTimingBias = ((JumpTimingBias < 0.5) ? 1.0 : 0.0);
				JumpTimingBias = jumpTimingBias;
				break;
			}
			case 4:
				return true;
			case 5:
				JumpTimingBias = 1.0 - JumpTimingBias;
				return true;
			case 6:
				JumpTimingBias = 1.0 - JumpTimingBias;
				_btSuppressJumpUntilAirborne = true;
				return false;
			case 7:
				_committedJumpDelay = 5;
				return false;
			case 8:
				_committedJumpDelay = 10;
				return false;
			case 9:
				_committedJumpDelay = 15;
				return false;
			case 10:
				_committedJumpDelay = 20;
				return false;
			case 11:
				_btForceJumpFramesRemaining = 30;
				return true;
			case 12:
				_btForceJumpFramesRemaining = 50;
				return true;
			case 13:
				_btSkipAllOrbs = true;
				break;
			case 14:
				_btSkipAllOrbs = true;
				JumpTimingBias = 1.0 - JumpTimingBias;
				break;
			case 15:
				if (_hitOrbHistory.Count > 0)
				{
					int item = _hitOrbHistory[_hitOrbHistory.Count - 1];
					_btSkipSpecificOrbs.Add(item);
				}
				break;
			case 16:
			{
				for (int i = Math.Max(0, _hitOrbHistory.Count - 2); i < _hitOrbHistory.Count; i++)
				{
					_btSkipSpecificOrbs.Add(_hitOrbHistory[i]);
				}
				break;
			}
			case 17:
				_committedJumpDelay = 1;
				return false;
			case 18:
				_committedJumpDelay = 2;
				return false;
			case 19:
				_committedJumpDelay = 3;
				return false;
			case 20:
				JumpTimingBias = 0.25;
				break;
			case 21:
				JumpTimingBias = 0.75;
				break;
			case 22:
				JumpTimingBias = 0.0;
				_btSuppressJumpUntilAirborne = true;
				return false;
			case 23:
				JumpTimingBias = 1.0;
				_btSuppressJumpUntilAirborne = true;
				return false;
			}
		}
		if (_coinInputScript.Count > 0 && _speculativeDepth == 0)
		{
			return _coinInputScript.Dequeue();
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
		if (!flag && !_btSuppressJumpUntilAirborne && ScanForOrbOverlap(in state, out var orbSpriteIndex) >= 0)
		{
			if (_coinAltitudePenalties.Count > 0 && !state.GravFlipped && state.VelY_fixed < -100 && _speculativeDepth == 0)
			{
				return false;
			}
			if (_btSkipAllOrbs)
			{
				state.ProcessedSprites.Add(orbSpriteIndex);
				return false;
			}
			if (_btSkipSpecificOrbs.Contains(orbSpriteIndex))
			{
				state.ProcessedSprites.Add(orbSpriteIndex);
				return false;
			}
			if (_speculativeDepth < 3)
			{
				_speculativeDepth++;
				int finalX_px;
				int num8 = SimulateForwardWithJumpAt(state, 0, out finalX_px, holdAfterLanding: false, -1, null, singleJumpOnly: true);
				SimState s = state.Clone();
				s.ProcessedSprites.Add(orbSpriteIndex);
				ClearPendingOrbs(ref s);
				int finalX_px2;
				int num9 = SimulateForwardWithJumpAt(s, -1, out finalX_px2, holdAfterLanding: false, -1, null, singleJumpOnly: true);
				if (num8 >= 90 && num9 >= 90 && finalX_px == finalX_px2)
				{
					int horizonOverride = 270;
					int finalX_px3;
					int num10 = SimulateForwardWithJumpAt(state, 0, out finalX_px3, holdAfterLanding: false, horizonOverride, null, singleJumpOnly: true);
					int finalX_px4;
					int num11 = SimulateForwardWithJumpAt(s, -1, out finalX_px4, holdAfterLanding: false, horizonOverride, null, singleJumpOnly: true);
					finalX_px = finalX_px3;
					finalX_px2 = finalX_px4;
					num8 = num10;
					num9 = num11;
				}
				_speculativeDepth--;
				if (finalX_px2 > finalX_px || (finalX_px2 == finalX_px && num9 > num8))
				{
					state.ProcessedSprites.Add(orbSpriteIndex);
					return false;
				}
				_hitOrbHistory.Add(orbSpriteIndex);
				return true;
			}
		}
		if (_btSuppressJumpUntilAirborne)
		{
			if (state.VelY_fixed != 0)
			{
				_btSuppressJumpUntilAirborne = false;
			}
			else
			{
				if (!QuickDangerCheck(state))
				{
					return false;
				}
				_btSuppressJumpUntilAirborne = false;
			}
		}
		if (_btForceJumpFramesRemaining > 0 && _speculativeDepth == 0 && state.VelY_fixed == 0 && state.OnGround)
		{
			SimState s2 = state.Clone();
			_speculativeDepth++;
			bool endLevel;
			bool flag2 = StepFrame(ref s2, input: true, out endLevel);
			if (flag2)
			{
				flag2 = StepFrame(ref s2, input: false, out endLevel);
			}
			if (flag2)
			{
				flag2 = StepFrame(ref s2, input: false, out endLevel);
			}
			_speculativeDepth--;
			_btForceJumpFramesRemaining--;
			if (flag2)
			{
				return true;
			}
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
				return true;
			}
			return false;
		}
		if (_committedJumpDelay == 0)
		{
			_committedJumpDelay = -1;
			return true;
		}
		_cubeHoldJump = false;
		if (_speculativeDepth >= 3)
		{
			return false;
		}
		if (_coinForceWalkZones.Count > 0)
		{
			int num12 = state.X_fixed >> 8;
			foreach (var (num13, num14) in _coinForceWalkZones)
			{
				if (num12 >= num13 && num12 <= num14)
				{
					if (state.OnGround && state.VelY_fixed == 0)
					{
						return false;
					}
					if (!state.OnGround && ((state.GravMul > 0 && state.VelY_fixed > 0) || (state.GravMul < 0 && state.VelY_fixed < 0)))
					{
						return false;
					}
				}
			}
		}
		bool endLevel2;
		if (_coinGroundBiasZones.Count > 0 && state.OnGround && state.VelY_fixed == 0 && state.GameMode == 0 && _speculativeDepth == 0)
		{
			int num15 = state.X_fixed >> 8;
			foreach (var (num16, num17) in _coinGroundBiasZones)
			{
				if (num15 < num16 || num15 > num17)
				{
					continue;
				}
				bool flag3 = false;
				_speculativeDepth++;
				SimState s3 = state.Clone();
				for (int j = 0; j < 3; j++)
				{
					if (!StepFrame(ref s3, input: false, out endLevel2))
					{
						flag3 = true;
						break;
					}
					if (!s3.OnGround)
					{
						break;
					}
				}
				_speculativeDepth--;
				if (!flag3)
				{
					return false;
				}
				break;
			}
		}
		if (PreferCoins && allCoins.Count > 0 && state.VelY_fixed == 0 && state.GameMode == 0 && _speculativeDepth == 0 && _coinInputScript.Count == 0)
		{
			int num18 = state.X_fixed >> 8;
			int num19 = state.Y_fixed >> 8;
			for (int k = _nextCoinCheckIdx; k < allCoins.Count; k++)
			{
				SpriteEntry spriteEntry2 = allCoins[k];
				if (state.ProcessedSprites.Contains(spriteEntry2.Index) || _forgivenCoins.Contains(spriteEntry2.Index))
				{
					continue;
				}
				int num20 = (spriteEntry2.HitTop + spriteEntry2.HitBottom) / 2;
				int num21 = spriteEntry2.HitLeft - num18;
				if (num21 < 0)
				{
					continue;
				}
				if (num21 > 800)
				{
					break;
				}
				if (num19 - num20 < 30 || num21 < 100 || _cubeBeamSearchAttemptedCoinIdx == spriteEntry2.Index)
				{
					continue;
				}
				_cubeBeamSearchAttemptedCoinIdx = spriteEntry2.Index;
				int num22 = num21 / 3 + 60;
				_speculativeDepth++;
				List<(SimState, List<bool>)> list = new List<(SimState, List<bool>)>();
				list.Add((state.Clone(), new List<bool>()));
				bool flag4 = false;
				List<bool> list2 = new List<bool>();
				for (int l = 0; l < num22; l++)
				{
					if (list.Count <= 0)
					{
						break;
					}
					List<(SimState, List<bool>, int)> list3 = new List<(SimState, List<bool>, int)>();
					foreach (var item6 in list)
					{
						SimState item2 = item6.Item1;
						List<bool> item3 = item6.Item2;
						bool flag5 = item2.PendingOrbIndex >= 0;
						bool flag6 = !item2.OnGround && item2.VelY_fixed != 0 && CubeWillLandThisFrame(item2);
						int num23 = (flag5 ? 1 : ((!((item2.OnGround && item2.VelY_fixed == 0) || flag6)) ? 1 : 2));
						for (int m = 0; m < num23; m++)
						{
							bool flag7 = flag5 || (num23 != 1 && m == 1);
							SimState s4 = item2.Clone();
							if (!StepFrame(ref s4, flag7, out endLevel2))
							{
								continue;
							}
							List<bool> list4 = new List<bool>(item3) { flag7 };
							int num24 = (s4.X_fixed >> 8) + 1;
							int hitboxW = GetHitboxW(s4.Mini);
							int hitboxH = GetHitboxH(s4.Mini);
							int hitboxOffsetY = GetHitboxOffsetY(s4.GameMode, s4.Mini, s4.GravFlipped);
							int num25 = (s4.Y_fixed >> 8) + hitboxOffsetY;
							if (num24 + hitboxW >= spriteEntry2.HitLeft && spriteEntry2.HitRight >= num24 && num25 + hitboxH >= spriteEntry2.HitTop && spriteEntry2.HitBottom >= num25)
							{
								bool flag8 = true;
								SimState s5 = s4.Clone();
								for (int n = 0; n < 30; n++)
								{
									bool input = s5.OnGround && s5.VelY_fixed == 0;
									if (!StepFrame(ref s5, input, out endLevel2))
									{
										flag8 = false;
										break;
									}
								}
								if (flag8)
								{
									flag4 = true;
									list2 = list4;
									break;
								}
							}
							int num26 = s4.X_fixed >> 8;
							if (num26 <= spriteEntry2.HitRight + 16)
							{
								int num27 = Math.Abs((s4.Y_fixed >> 8) - num20);
								int num28 = Math.Max(0, spriteEntry2.HitLeft - num26);
								int item4 = num27 + num28 / 3;
								list3.Add((s4, list4, item4));
							}
						}
						if (flag4)
						{
							break;
						}
					}
					if (flag4)
					{
						break;
					}
					list3.Sort(((SimState st, List<bool> inputs, int coinDist) a, (SimState st, List<bool> inputs, int coinDist) b) => a.coinDist.CompareTo(b.coinDist));
					list.Clear();
					int num29 = Math.Min(512, list3.Count);
					for (int num30 = 0; num30 < num29; num30++)
					{
						list.Add((list3[num30].Item1, list3[num30].Item2));
					}
				}
				_speculativeDepth--;
				if (!flag4)
				{
					break;
				}
				_coinInputScript.Clear();
				_coinInputScriptCoinIdx = spriteEntry2.Index;
				for (int num31 = 1; num31 < list2.Count; num31++)
				{
					_coinInputScript.Enqueue(list2[num31]);
				}
				return list2[0];
			}
		}
		if (PreferCoins && allCoins.Count > 0 && state.OnGround && state.VelY_fixed == 0 && state.GameMode == 0 && _speculativeDepth == 0)
		{
			int num32 = state.X_fixed >> 8;
			for (int num33 = _nextCoinCheckIdx; num33 < allCoins.Count; num33++)
			{
				SpriteEntry spriteEntry3 = allCoins[num33];
				if (state.ProcessedSprites.Contains(spriteEntry3.Index) || _forgivenCoins.Contains(spriteEntry3.Index))
				{
					continue;
				}
				if (spriteEntry3.HitLeft > num32 + 500)
				{
					break;
				}
				if (spriteEntry3.HitRight < num32)
				{
					continue;
				}
				_speculativeDepth++;
				SimState s6 = state.Clone();
				bool flag9 = false;
				int num34 = 0;
				int num35 = -1;
				SimState s7 = state.Clone();
				bool flag10 = false;
				int num36 = 0;
				bool result = false;
				List<bool> list5 = new List<bool>();
				int num37 = (mapHeight - groundRowsToReserve) * 16 - 15;
				for (int num38 = 0; num38 < 200; num38++)
				{
					bool flag11;
					if (s7.PendingOrbIndex >= 0)
					{
						flag11 = true;
					}
					else if (s7.OnGround && s7.VelY_fixed == 0 && s7.Y_fixed >> 8 >= num37 - 5)
					{
						SimState s8 = s7.Clone();
						flag11 = !StepFrame(ref s8, input: false, out endLevel2);
					}
					else
					{
						flag11 = false;
					}
					list5.Add(flag11);
					if (num38 == 0)
					{
						result = flag11;
					}
					if (!StepFrame(ref s7, flag11, out var endLevel3))
					{
						break;
					}
					num36 = num38 + 1;
					if (endLevel3)
					{
						num36 = 120;
						break;
					}
					if (!flag10)
					{
						int num39 = (s7.X_fixed >> 8) + 1;
						int hitboxW2 = GetHitboxW(s7.Mini);
						int hitboxH2 = GetHitboxH(s7.Mini);
						int hitboxOffsetY2 = GetHitboxOffsetY(s7.GameMode, s7.Mini, s7.GravFlipped);
						int num40 = (s7.Y_fixed >> 8) + hitboxOffsetY2;
						int num41 = num40 + hitboxH2;
						bool num42 = num39 + hitboxW2 >= spriteEntry3.HitLeft && spriteEntry3.HitRight >= num39;
						bool flag12 = num41 >= spriteEntry3.HitTop && spriteEntry3.HitBottom >= num40;
						if (num42 && flag12)
						{
							flag10 = true;
						}
					}
				}
				if (flag10 && num36 >= 30)
				{
					_speculativeDepth--;
					_coinInputScript.Clear();
					_coinInputScriptCoinIdx = spriteEntry3.Index;
					for (int num43 = 1; num43 < list5.Count; num43++)
					{
						_coinInputScript.Enqueue(list5[num43]);
					}
					return result;
				}
				List<bool> list6 = null;
				bool flag13 = false;
				List<bool> list7 = new List<bool>();
				bool flag14 = false;
				for (int num44 = 0; num44 < 120; num44++)
				{
					if (s6.PendingOrbIndex >= 0)
					{
						flag14 = true;
					}
					bool flag15 = (s6.VelY_fixed == 0 && s6.OnGround) || s6.PendingOrbIndex >= 0;
					list7.Add(flag15);
					if (!StepFrame(ref s6, flag15, out var endLevel4))
					{
						break;
					}
					num34 = num44 + 1;
					if (endLevel4)
					{
						num34 = 120;
						break;
					}
					if (!flag9)
					{
						int num45 = (s6.X_fixed >> 8) + 1;
						int hitboxW3 = GetHitboxW(s6.Mini);
						int hitboxH3 = GetHitboxH(s6.Mini);
						int hitboxOffsetY3 = GetHitboxOffsetY(s6.GameMode, s6.Mini, s6.GravFlipped);
						int num46 = (s6.Y_fixed >> 8) + hitboxOffsetY3;
						int num47 = num46 + hitboxH3;
						bool num48 = num45 + hitboxW3 >= spriteEntry3.HitLeft && spriteEntry3.HitRight >= num45;
						bool flag16 = num47 >= spriteEntry3.HitTop && spriteEntry3.HitBottom >= num46;
						if (num48 && flag16)
						{
							flag9 = true;
							num35 = num44;
						}
					}
				}
				if (flag9)
				{
					list6 = list7;
					flag13 = flag14;
				}
				if (!flag9)
				{
					s6 = state.Clone();
					num34 = 0;
					bool flag17 = false;
					List<bool> list8 = new List<bool>();
					bool flag18 = false;
					for (int num49 = 0; num49 < 120; num49++)
					{
						if (s6.PendingOrbIndex >= 0)
						{
							flag18 = true;
						}
						bool flag19 = false;
						if (!flag17 && s6.VelY_fixed == 0 && s6.OnGround)
						{
							flag19 = true;
							flag17 = true;
						}
						else if (s6.PendingOrbIndex >= 0)
						{
							flag19 = true;
						}
						list8.Add(flag19);
						if (!StepFrame(ref s6, flag19, out var endLevel5))
						{
							break;
						}
						num34 = num49 + 1;
						if (endLevel5)
						{
							num34 = 120;
							break;
						}
						if (!flag9)
						{
							int num50 = (s6.X_fixed >> 8) + 1;
							int hitboxW4 = GetHitboxW(s6.Mini);
							int hitboxH4 = GetHitboxH(s6.Mini);
							int hitboxOffsetY4 = GetHitboxOffsetY(s6.GameMode, s6.Mini, s6.GravFlipped);
							int num51 = (s6.Y_fixed >> 8) + hitboxOffsetY4;
							int num52 = num51 + hitboxH4;
							bool num53 = num50 + hitboxW4 >= spriteEntry3.HitLeft && spriteEntry3.HitRight >= num50;
							bool flag20 = num52 >= spriteEntry3.HitTop && spriteEntry3.HitBottom >= num51;
							if (num53 && flag20)
							{
								flag9 = true;
								num35 = num49;
							}
						}
					}
					if (flag9)
					{
						list6 = list8;
						flag13 = flag18;
					}
				}
				_speculativeDepth--;
				if (!flag9 || num34 < num35 + 10)
				{
					break;
				}
				if (list6 != null && list6.Count > 1 && !flag13)
				{
					_coinInputScript.Clear();
					_coinInputScriptCoinIdx = spriteEntry3.Index;
					for (int num54 = 1; num54 < list6.Count; num54++)
					{
						_coinInputScript.Enqueue(list6[num54]);
					}
				}
				return true;
			}
		}
		if (PreferCoins && allCoins.Count > 0 && state.OnGround && state.VelY_fixed == 0 && state.GameMode == 0 && _speculativeDepth == 0)
		{
			int num55 = state.X_fixed >> 8;
			int num56 = state.Y_fixed >> 8;
			for (int num57 = _nextCoinCheckIdx; num57 < allCoins.Count; num57++)
			{
				SpriteEntry spriteEntry4 = allCoins[num57];
				if (state.ProcessedSprites.Contains(spriteEntry4.Index) || _forgivenCoins.Contains(spriteEntry4.Index))
				{
					continue;
				}
				if (spriteEntry4.HitLeft > num55 + 300)
				{
					break;
				}
				if (spriteEntry4.HitRight < num55)
				{
					continue;
				}
				if ((spriteEntry4.HitTop + spriteEntry4.HitBottom) / 2 <= num56)
				{
					break;
				}
				_speculativeDepth++;
				SimState s9 = state.Clone();
				bool flag21 = false;
				int num58 = 0;
				int num59 = num56;
				int num60 = num56;
				for (int num61 = 0; num61 < 120; num61++)
				{
					bool input2 = s9.PendingOrbIndex >= 0;
					if (!StepFrame(ref s9, input2, out var endLevel6))
					{
						break;
					}
					num58 = num61 + 1;
					int num62 = s9.Y_fixed >> 8;
					_ = s9.X_fixed >> 8;
					if (num62 < num59)
					{
						num59 = num62;
					}
					if (num62 > num60)
					{
						num60 = num62;
					}
					if (endLevel6)
					{
						num58 = 120;
						break;
					}
					if (!flag21)
					{
						int num63 = (s9.X_fixed >> 8) + 1;
						int hitboxW5 = GetHitboxW(s9.Mini);
						int hitboxH5 = GetHitboxH(s9.Mini);
						int hitboxOffsetY5 = GetHitboxOffsetY(s9.GameMode, s9.Mini, s9.GravFlipped);
						int num64 = (s9.Y_fixed >> 8) + hitboxOffsetY5;
						int num65 = num64 + hitboxH5;
						bool num66 = num63 + hitboxW5 >= spriteEntry4.HitLeft && spriteEntry4.HitRight >= num63;
						bool flag22 = num65 >= spriteEntry4.HitTop && spriteEntry4.HitBottom >= num64;
						if (num66 && flag22)
						{
							flag21 = true;
						}
					}
				}
				_speculativeDepth--;
				if (!flag21 || num58 < 30)
				{
					break;
				}
				return false;
			}
		}
		int num67 = state.Y_fixed >> 8;
		int num68 = (mapHeight - groundRowsToReserve) * 16 - 15;
		bool flag23 = num67 <= num68 - 16;
		int num69 = (flag23 ? 600 : 120);
		int num70 = (flag23 ? 128 : 256);
		List<(SimState, bool, int, int, int, bool)> list9 = new List<(SimState, bool, int, int, int, bool)>();
		int bestF0JumpSurv = -1;
		int bestF0JumpX = int.MinValue;
		bool bestF0JumpHoldPattern = false;
		int bestF0JumpMinY = int.MaxValue;
		int bestF0WalkSurv = -1;
		int bestF0WalkX = int.MinValue;
		int bestF0WalkMinY = int.MaxValue;
		list9.Add((state.Clone(), true, 0, 0, state.Y_fixed >> 8, false));
		list9.Add((state.Clone(), false, 0, 0, state.Y_fixed >> 8, false));
		_speculativeDepth++;
		for (int num71 = 0; num71 < num69; num71++)
		{
			if (list9.Count <= 0)
			{
				break;
			}
			List<(SimState, bool, int, int, int, bool)> list10 = new List<(SimState, bool, int, int, int, bool)>();
			foreach (var item7 in list9)
			{
				(SimState, bool, int, int, int, bool) current2 = item7;
				int num72 = -1;
				if (current2.Item1.PendingOrbIndex >= 0)
				{
					num72 = current2.Item1.PendingOrbIndex;
				}
				else
				{
					ScanForOrbOverlap(in current2.Item1, out var orbSpriteIndex2);
					if (orbSpriteIndex2 >= 0 && !current2.Item1.ProcessedSprites.Contains(orbSpriteIndex2))
					{
						num72 = orbSpriteIndex2;
					}
				}
				int num73 = ((num72 < 0) ? 1 : 2);
				for (int num74 = 0; num74 < num73; num74++)
				{
					SimState s10;
					if (num74 == 0)
					{
						(s10, _, _, _, _, _) = current2;
					}
					else
					{
						SimState item5 = current2.Item1;
						s10 = item5.Clone();
						s10.ProcessedSprites.Add(num72);
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
					bool flag24 = current2.Item6 || num74 == 1;
					if (s10.VelY_fixed == 0 && s10.OnGround && (s10.GameMode == 0 || s10.GameMode == 2))
					{
						int num75 = current2.Item4 + 1;
						SimState s11 = s10.Clone();
						bool endLevel7;
						bool flag25 = StepFrame(ref s11, input: true, out endLevel7);
						bool flag26 = num71 == 0 || current2.Item2;
						int num76 = current2.Item3 + 1;
						int num77 = s11.Y_fixed >> 8;
						if (endLevel7)
						{
							RecordTerminal(flag26, num69, s11.X_fixed >> 8, num76, num75, num77, flag24);
						}
						else if (flag25)
						{
							list10.Add((s11, flag26, num76, num75, num77, flag24));
						}
						else
						{
							RecordTerminal(flag26, num71, s11.X_fixed >> 8, num76, num75, num77, flag24);
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
						bool endLevel8;
						bool flag27 = StepFrame(ref s12, input3, out endLevel8);
						bool flag28 = num71 != 0 && current2.Item2;
						int num78 = s12.Y_fixed >> 8;
						if (endLevel8)
						{
							RecordTerminal(flag28, num69, s12.X_fixed >> 8, current2.Item3, num75, num78, flag24);
						}
						else if (flag27)
						{
							list10.Add((s12, flag28, current2.Item3, num75, num78, flag24));
						}
						else
						{
							RecordTerminal(flag28, num71, s12.X_fixed >> 8, current2.Item3, num75, num78, flag24);
						}
						continue;
					}
					if (s10.GameMode == 0 && CubeWillLandThisFrame(s10))
					{
						int num79 = current2.Item4 + 1;
						for (int num80 = 0; num80 < 2; num80++)
						{
							bool flag29 = num80 == 0;
							if (s10.PendingOrbIndex >= 0 && !_btSkipSpecificOrbs.Contains(s10.PendingOrbIndex))
							{
								flag29 = true;
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
							bool endLevel9;
							bool flag30 = StepFrame(ref s13, flag29, out endLevel9);
							bool flag31 = ((num71 == 0) ? flag29 : current2.Item2);
							int num81 = ((num80 == 0) ? (current2.Item3 + 1) : current2.Item3);
							int num82 = s13.Y_fixed >> 8;
							if (endLevel9)
							{
								RecordTerminal(flag31, num69, s13.X_fixed >> 8, num81, num79, num82, flag24);
							}
							else if (flag30)
							{
								list10.Add((s13, flag31, num81, num79, num82, flag24));
							}
							else
							{
								RecordTerminal(flag31, num71, s13.X_fixed >> 8, num81, num79, num82, flag24);
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
					bool endLevel10;
					bool flag32 = StepFrame(ref s14, input4, out endLevel10);
					int num83 = s14.Y_fixed >> 8;
					if (endLevel10)
					{
						RecordTerminal(current2.Item2, num69, s14.X_fixed >> 8, current2.Item3, current2.Item4, num83, flag24);
					}
					else if (flag32)
					{
						list10.Add((s14, current2.Item2, current2.Item3, current2.Item4, num83, flag24));
					}
					else
					{
						RecordTerminal(current2.Item2, num71, s14.X_fixed >> 8, current2.Item3, current2.Item4, num83, flag24);
					}
				}
			}
			list9 = list10;
			if (list9.Count <= num70)
			{
				continue;
			}
			Dictionary<(int, int, bool, bool, int, bool, int, int, bool), (SimState, bool, int, int, int, bool)> dictionary = new Dictionary<(int, int, bool, bool, int, bool, int, int, bool), (SimState, bool, int, int, int, bool)>();
			foreach (var item8 in list9)
			{
				(int, int, bool, bool, int, bool, int, int, bool) key = (item8.Item1.Y_fixed, item8.Item1.VelY_fixed, item8.Item1.OnGround, item8.Item1.GravFlipped, item8.Item1.GameMode, item8.Item2, item8.Item1.DualActive ? item8.Item1.P2_Y_fixed : 0, item8.Item1.DualActive ? item8.Item1.P2_VelY_fixed : 0, item8.Item1.DualActive && item8.Item1.P2_GravFlipped);
				if (!dictionary.ContainsKey(key) || item8.Item3 > dictionary[key].Item3)
				{
					dictionary[key] = item8;
				}
			}
			list9 = dictionary.Values.ToList();
			if (list9.Count <= num70)
			{
				continue;
			}
			List<(SimState, bool, int, int, int, bool)> list11 = (from tuple5 in list9
				where tuple5.j0
				orderby tuple5.s.Y_fixed
				select tuple5).ToList();
			List<(SimState, bool, int, int, int, bool)> list12 = (from tuple5 in list9
				where !tuple5.j0
				orderby tuple5.s.Y_fixed
				select tuple5).ToList();
			int num84 = num70 / 2;
			if (list11.Count > num84)
			{
				List<(SimState, bool, int, int, int, bool)> list13 = new List<(SimState, bool, int, int, int, bool)>(num84);
				for (int num85 = 0; num85 < num84; num85++)
				{
					list13.Add(list11[(int)((long)num85 * (long)list11.Count / num84)]);
				}
				list11 = list13;
			}
			if (list12.Count > num84)
			{
				List<(SimState, bool, int, int, int, bool)> list14 = new List<(SimState, bool, int, int, int, bool)>(num84);
				for (int num86 = 0; num86 < num84; num86++)
				{
					list14.Add(list12[(int)((long)num86 * (long)list12.Count / num84)]);
				}
				list12 = list14;
			}
			list9 = list11.Concat(list12).ToList();
		}
		_speculativeDepth--;
		foreach (var item9 in list9)
		{
			RecordTerminal(item9.Item2, num69, item9.Item1.X_fixed >> 8, item9.Item3, item9.Item4, item9.Item5, item9.Item6);
		}
		if (_speculativeDepth == 0 && OnSpeculativePath != null)
		{
			_speculativeDepth++;
			List<(int, int)> list15 = new List<(int, int)>();
			int finalX_px5;
			int arg = SimulateForwardWithJumpAt(state, 0, out finalX_px5, holdAfterLanding: false, -1, list15);
			OnSpeculativePath(list15, 0, arg, arg4: false);
			List<(int, int)> list16 = new List<(int, int)>();
			int arg2 = SimulateForwardWithJumpAt(state, 0, out finalX_px5, holdAfterLanding: true, -1, list16);
			OnSpeculativePath(list16, 0, arg2, arg4: true);
			List<(int, int)> list17 = new List<(int, int)>();
			int arg3 = SimulateForwardWithJumpAt(state, -1, holdAfterLanding: false, list17);
			OnSpeculativePath(list17, -1, arg3, arg4: false);
			List<(int, int)> list18 = new List<(int, int)>();
			int arg4 = SimulateForwardWithJumpAt(state, 1, out finalX_px5, holdAfterLanding: false, -1, list18);
			OnSpeculativePath(list18, 1, arg4, arg4: false);
			_speculativeDepth--;
		}
		int num87 = 0;
		if (bestF0JumpSurv >= num69 && bestF0WalkSurv >= 0 && bestF0JumpMinY != int.MaxValue && bestF0WalkMinY != int.MaxValue)
		{
			num87 = (bestF0WalkMinY - bestF0JumpMinY) / 16 * 8;
			if (num87 < 0)
			{
				num87 = 0;
			}
		}
		if (PreferCoins && num87 > 0)
		{
			int num88 = state.X_fixed >> 8;
			int num89 = state.Y_fixed >> 8;
			for (int num90 = _nextCoinCheckIdx; num90 < allCoins.Count; num90++)
			{
				SpriteEntry spriteEntry5 = allCoins[num90];
				if (state.ProcessedSprites.Contains(spriteEntry5.Index) || _forgivenCoins.Contains(spriteEntry5.Index))
				{
					continue;
				}
				if (spriteEntry5.HitLeft > num88 + 1500)
				{
					break;
				}
				if (spriteEntry5.HitRight >= num88)
				{
					if ((spriteEntry5.HitTop + spriteEntry5.HitBottom) / 2 > num89)
					{
						num87 = 0;
					}
					break;
				}
			}
		}
		int num91 = 0;
		int num92 = 0;
		int num93 = -1;
		if (PreferCoins && allCoins.Count > 0 && bestF0JumpMinY != int.MaxValue && bestF0WalkMinY != int.MaxValue && bestF0JumpSurv > 0 && bestF0WalkSurv > 0)
		{
			int num94 = state.X_fixed >> 8;
			for (int num95 = _nextCoinCheckIdx; num95 < allCoins.Count; num95++)
			{
				SpriteEntry spriteEntry6 = allCoins[num95];
				if (state.ProcessedSprites.Contains(spriteEntry6.Index) || _forgivenCoins.Contains(spriteEntry6.Index))
				{
					continue;
				}
				if (spriteEntry6.HitLeft > num94 + 1500)
				{
					break;
				}
				if (spriteEntry6.HitRight >= num94)
				{
					num93 = (spriteEntry6.HitTop + spriteEntry6.HitBottom) / 2;
					int num96 = Math.Max(0, spriteEntry6.HitLeft - num94);
					double num97 = Math.Max(0.1, 1.0 - (double)num96 / 1500.0);
					int num98;
					int num99;
					if (num93 < state.Y_fixed >> 8)
					{
						num98 = ((bestF0JumpMinY > num93) ? (bestF0JumpMinY - num93) : 0);
						num99 = ((bestF0WalkMinY > num93) ? (bestF0WalkMinY - num93) : 0);
					}
					else
					{
						num98 = Math.Abs(bestF0JumpMinY - num93);
						num99 = Math.Abs(bestF0WalkMinY - num93);
					}
					int num100 = num99 - num98;
					int num101 = (int)((double)Math.Abs(num100) * num97);
					int num102 = ((num100 > 0) ? bestF0JumpSurv : bestF0WalkSurv);
					if (num102 < 30)
					{
						num101 = num101 * num102 / 30;
					}
					num101 = Math.Min(num101, num69);
					if (num100 > 0)
					{
						num91 = num101;
					}
					else if (num100 < 0)
					{
						num92 = num101;
					}
					break;
				}
			}
		}
		bool flag33 = bestF0WalkMinY != int.MaxValue && bestF0JumpMinY != int.MaxValue && bestF0WalkMinY > bestF0JumpMinY;
		int num103 = ((bestF0JumpHoldPattern && bestF0JumpSurv >= num69 && !flag33) ? 5 : 0);
		int num104 = 0;
		if (_coinAltitudePenalties.Count > 0)
		{
			int num105 = state.X_fixed >> 8;
			int num106 = state.Y_fixed >> 8;
			foreach (var (num107, num108, num109) in _coinAltitudePenalties)
			{
				if (num105 >= num107 && num105 <= num108)
				{
					num104 = 30;
					if (num106 < num109)
					{
						num104 = Math.Max(num104, num109 - num106);
					}
					break;
				}
			}
		}
		if (num104 > 0 && num87 > 0)
		{
			num87 = 0;
		}
		if (num104 > 0 && num91 > 0)
		{
			num91 = 0;
		}
		int num110 = bestF0JumpSurv + num103 + num87 + num91 - num104;
		int num111 = bestF0WalkSurv + num92;
		if (JumpTimingBias < 0.45)
		{
			int num112 = (int)((0.5 - JumpTimingBias) * 6.0 + 0.5);
			num110 += num112;
		}
		else if (JumpTimingBias > 0.55)
		{
			int num113 = (int)((JumpTimingBias - 0.5) * 6.0 + 0.5);
			num111 += num113;
		}
		if (num110 <= num111 && (num111 > num110 || (flag23 && bestF0JumpSurv >= num69 && bestF0WalkSurv >= num69 && bestF0JumpMinY > bestF0WalkMinY) || (flag23 && bestF0JumpSurv >= num69 && bestF0WalkSurv >= num69) || (bestF0JumpSurv >= num69 && bestF0WalkSurv >= num69 && bestF0WalkMinY > bestF0JumpMinY) || (num93 >= 0 && bestF0JumpX >= bestF0WalkX) || bestF0JumpX < bestF0WalkX || 1 == 0))
		{
			return false;
		}
		return true;
		void RecordTerminal(bool flag34, int survFrames, int xPx, int lj, int tl, int minY, bool orbSkipped = false)
		{
			if (!orbSkipped)
			{
				if (flag34)
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
			if (num < num2)
			{
				return !flag;
			}
			return flag;
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
		Dictionary<long, int> shipCorridorCache = new Dictionary<long, int>(128);
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
				bool num9 = StepFrame(ref s3, input: true, out endLevel3);
				int num10 = ((s3.Mini && !s3.GravFlipped) ? 4 : 0);
				list.Add(((s3.X_fixed >> 8) + 8, (s3.Y_fixed >> 8) + num10 + 8));
				if (!num9 || endLevel3)
				{
					break;
				}
			}
			for (int k = 0; k < 20; k++)
			{
				bool endLevel4;
				bool num11 = StepFrame(ref s4, input: false, out endLevel4);
				int num12 = ((s4.Mini && !s4.GravFlipped) ? 4 : 0);
				list2.Add(((s4.X_fixed >> 8) + 8, (s4.Y_fixed >> 8) + num12 + 8));
				if (!num11 || endLevel4)
				{
					break;
				}
			}
			_speculativeDepth--;
			CurrentSpeculativeVizMode = 1;
			OnSpeculativePath(list, 0, num5, arg4: true);
			CurrentSpeculativeVizMode = 1;
			OnSpeculativePath(list2, 1, num6, arg4: false);
		}
		int num13 = 600;
		bool flag4 = false;
		if (num >= 0 && flag && num3 <= 1200)
		{
			int num14 = CorridorCenter(ref state);
			int num15 = Math.Abs(num - num14);
			if (num15 > 40)
			{
				num13 = Math.Min(1200, Math.Max(600, num15 * 10));
				flag4 = num3 > 600;
			}
		}
		int num16 = (flag4 ? 60 : 30);
		bool endLevel5;
		if (num >= 0 && flag && _coinInputScript.Count == 0 && num3 >= 20 && num3 <= num13 && (_beamSearchAttemptedCoinIdx != num2 || num3 <= _beamSearchLastDistX - num16))
		{
			int num17 = (flag4 ? 128 : 512);
			int num18 = (flag4 ? Math.Max(450, num3 / 2 + 60 + 120) : Math.Min(450, num3 * 3 / 4 + 60 + 120));
			_speculativeDepth++;
			List<(SimState, List<bool>, int)> list3 = new List<(SimState, List<bool>, int)>();
			list3.Add((state.Clone(), new List<bool>(), -1));
			bool flag5 = false;
			List<bool> list4 = new List<bool>();
			int num19 = 0;
			for (int l = 0; l < num18; l++)
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
						bool flag6 = m == 1;
						SimState s5 = item.Clone();
						if (!StepFrame(ref s5, flag6, out endLevel5))
						{
							continue;
						}
						List<bool> list6 = new List<bool>(item2) { flag6 };
						int num20 = item3;
						if (num20 < 0)
						{
							int num21 = (s5.X_fixed >> 8) + 1;
							int hitboxW = GetHitboxW(s5.Mini);
							int hitboxH = GetHitboxH(s5.Mini);
							int hitboxOffsetY = GetHitboxOffsetY(s5.GameMode, s5.Mini, s5.GravFlipped);
							int num22 = (s5.Y_fixed >> 8) + hitboxOffsetY;
							if (num21 + hitboxW >= spriteEntry.HitLeft && spriteEntry.HitRight >= num21 && num22 + hitboxH >= spriteEntry.HitTop && spriteEntry.HitBottom >= num22)
							{
								num20 = 0;
							}
						}
						if (num20 >= 0)
						{
							num20++;
							if (num20 >= 120)
							{
								flag5 = true;
								list4 = list6;
								num19 = num20;
								break;
							}
							if (num20 > num19)
							{
								num19 = num20;
								list4 = new List<bool>(list6);
							}
							int num23 = s5.Y_fixed >> 8;
							int num24 = CorridorCenter(ref s5);
							int num25 = Math.Abs(num23 - num24);
							int num26 = Math.Abs(s5.VelY_fixed) >> 6;
							int item4 = -10000 + num25 * 3 + num26;
							list5.Add((s5, list6, num20, item4));
							continue;
						}
						int num27 = s5.X_fixed >> 8;
						if (num27 <= spriteEntry.HitRight + 4)
						{
							int num28 = s5.Y_fixed >> 8;
							int num29 = Math.Abs(num28 - num);
							int num30 = Math.Max(0, spriteEntry.HitLeft - num27);
							int num31 = ((num30 > 120) ? (num30 + num29) : ((num30 > 50) ? (num30 / 2 + num29 * 2) : ((num30 <= 15) ? (num29 * 6 + num30 / 4) : (num30 / 4 + num29 * 4))));
							int velY_fixed = s5.VelY_fixed;
							bool flag7 = num - num28 > 0 == velY_fixed * s5.GravMul > 0;
							if (flag7 && num29 > 4)
							{
								int val = Math.Abs(velY_fixed) >> 7;
								num31 -= Math.Min(num29 / 2, val);
							}
							bool num32 = ((s5.GravMul > 0) ? (num28 > num + 20) : (num28 < num - 20));
							bool flag8 = !flag7 && Math.Abs(velY_fixed) > 512;
							if (num32 && flag8)
							{
								num31 += 200;
							}
							list5.Add((s5, list6, num20, num31));
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
				if (num19 >= 120 && list5.Count == 0)
				{
					flag5 = true;
					break;
				}
				int num33 = num17 / 4;
				int val2 = num17 - num33;
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
				int num34 = Math.Min(num17, list7.Count);
				for (int num35 = 0; num35 < num34; num35++)
				{
					list3.Add((list7[num35].Item1, list7[num35].Item2, list7[num35].Item3));
				}
				int num36 = num17 - list3.Count;
				if (num36 <= 0 || list8.Count <= 0)
				{
					continue;
				}
				list8.Sort(((SimState st, List<bool> inputs, int collected, int score) a, (SimState st, List<bool> inputs, int collected, int score) b) => a.score.CompareTo(b.score));
				int num37 = Math.Min(Math.Min(val2, num36), list8.Count);
				HashSet<int> hashSet = new HashSet<int>();
				for (int num38 = 0; num38 < num37; num38++)
				{
					list3.Add((list8[num38].Item1, list8[num38].Item2, list8[num38].Item3));
					hashSet.Add(num38);
				}
				num36 = num17 - list3.Count;
				if (num36 <= 0)
				{
					continue;
				}
				SimState ss = list8[0].Item1;
				int num39 = CorridorCenter(ref ss);
				List<(int, int)> list9 = new List<(int, int)>();
				for (int num40 = 0; num40 < list8.Count; num40++)
				{
					if (!hashSet.Contains(num40))
					{
						(SimState, List<bool>, int, int) tuple = list8[num40];
						int num41 = Math.Abs((tuple.Item1.Y_fixed >> 8) - num39);
						int num42 = Math.Abs(tuple.Item1.VelY_fixed) >> 6;
						list9.Add((num40, num41 * 2 + num42));
					}
				}
				list9.Sort(((int origIdx, int safeScore) a, (int origIdx, int safeScore) b) => a.safeScore.CompareTo(b.safeScore));
				int num43 = Math.Min(num36, list9.Count);
				for (int num44 = 0; num44 < num43; num44++)
				{
					(SimState, List<bool>, int, int) tuple2 = list8[list9[num44].Item1];
					list3.Add((tuple2.Item1, tuple2.Item2, tuple2.Item3));
				}
			}
			if (!flag5 && num19 >= 15)
			{
				flag5 = true;
			}
			_speculativeDepth--;
			_beamSearchAttemptedCoinIdx = num2;
			_beamSearchLastDistX = num3;
			if (flag5 && list4.Count > 0)
			{
				_coinInputScript.Clear();
				_coinInputScriptCoinIdx = num2;
				for (int num45 = 1; num45 < list4.Count; num45++)
				{
					_coinInputScript.Enqueue(list4[num45]);
				}
				return list4[0];
			}
			if (list3.Count == 0 && num19 == 0)
			{
				_speculativeDepth++;
				int val3 = CorridorCenter(ref state);
				int num46 = Math.Min(600, num3 + 60 + 120);
				int num47 = Math.Min(val3, num);
				int num48 = Math.Max(val3, num);
				list3.Clear();
				list3.Add((state.Clone(), new List<bool>(), -1));
				flag5 = false;
				list4.Clear();
				num19 = 0;
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
							bool flag9 = num50 == 1;
							SimState s6 = item5.Clone();
							if (!StepFrame(ref s6, flag9, out endLevel5))
							{
								continue;
							}
							List<bool> list11 = new List<bool>(item6) { flag9 };
							int num51 = item7;
							if (num51 < 0)
							{
								int num52 = (s6.X_fixed >> 8) + 1;
								int hitboxW2 = GetHitboxW(s6.Mini);
								int hitboxH2 = GetHitboxH(s6.Mini);
								int hitboxOffsetY2 = GetHitboxOffsetY(s6.GameMode, s6.Mini, s6.GravFlipped);
								int num53 = (s6.Y_fixed >> 8) + hitboxOffsetY2;
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
									flag5 = true;
									list4 = list11;
									num19 = num51;
									break;
								}
								if (num51 > num19)
								{
									num19 = num51;
									list4 = new List<bool>(list11);
								}
								int num54 = s6.Y_fixed >> 8;
								int num55 = CorridorCenter(ref s6);
								int num56 = Math.Abs(num54 - num55);
								int num57 = Math.Abs(s6.VelY_fixed) >> 6;
								item8 = -10000 + num56 * 3 + num57;
							}
							else
							{
								if (s6.X_fixed >> 8 > spriteEntry.HitRight + 32)
								{
									continue;
								}
								int num58 = s6.Y_fixed >> 8;
								int num59 = ((num58 < num47) ? (num47 - num58) : ((num58 > num48) ? (num58 - num48) : 0));
								int num60 = Math.Abs(s6.VelY_fixed) >> 7;
								item8 = num59 * 3 + num60;
							}
							list10.Add((s6, list11, num51, item8));
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
					if (num19 >= 120 && list10.Count == 0)
					{
						flag5 = true;
						break;
					}
					list10.Sort(((SimState st, List<bool> inputs, int collected, int score) a, (SimState st, List<bool> inputs, int collected, int score) b) => a.score.CompareTo(b.score));
					list3.Clear();
					int num61 = Math.Min(1024, list10.Count);
					for (int num62 = 0; num62 < num61; num62++)
					{
						list3.Add((list10[num62].Item1, list10[num62].Item2, list10[num62].Item3));
					}
				}
				if (!flag5 && num19 >= 15)
				{
					flag5 = true;
				}
				_speculativeDepth--;
				if (flag5 && list4.Count > 0)
				{
					_coinInputScript.Clear();
					_coinInputScriptCoinIdx = num2;
					for (int num63 = 1; num63 < list4.Count; num63++)
					{
						_coinInputScript.Enqueue(list4[num63]);
					}
					return list4[0];
				}
			}
		}
		if (num5 != num6)
		{
			bool flag10 = false;
			if (num >= 0 && flag && num3 <= 1200)
			{
				int num64 = CorridorCenter(ref state);
				flag10 = Math.Abs(num - num64) > 40;
			}
			int num65 = (flag10 ? 5 : 10);
			if (!(num >= 0 && flag) || Math.Min(num5, num6) < num65 || !(num3 <= 600 || flag10))
			{
				return num5 > num6;
			}
			_speculativeDepth++;
			bool flag11 = false;
			SimState s7 = state.Clone();
			StepFrame(ref s7, input: true, out endLevel5);
			for (int num66 = 1; num66 < 120; num66++)
			{
				int num67 = s7.Y_fixed >> 8;
				int velY_fixed2 = s7.VelY_fixed;
				int num68 = ((s7.GravMul > 0) ? (num67 - num) : (num - num67));
				int num69 = -(velY_fixed2 * s7.GravMul);
				bool input = num68 - num69 > 0;
				if (!StepFrame(ref s7, input, out endLevel5))
				{
					break;
				}
				int num70 = (s7.X_fixed >> 8) + 1;
				int hitboxW3 = GetHitboxW(s7.Mini);
				int hitboxH3 = GetHitboxH(s7.Mini);
				int hitboxOffsetY3 = GetHitboxOffsetY(s7.GameMode, s7.Mini, s7.GravFlipped);
				int num71 = (s7.Y_fixed >> 8) + hitboxOffsetY3;
				if (num70 + hitboxW3 >= spriteEntry.HitLeft && spriteEntry.HitRight >= num70 && num71 + hitboxH3 >= spriteEntry.HitTop && spriteEntry.HitBottom >= num71)
				{
					flag11 = true;
					break;
				}
			}
			bool flag12 = false;
			SimState s8 = state.Clone();
			StepFrame(ref s8, input: false, out endLevel5);
			for (int num72 = 1; num72 < 120; num72++)
			{
				int num73 = s8.Y_fixed >> 8;
				int velY_fixed3 = s8.VelY_fixed;
				int num74 = ((s8.GravMul > 0) ? (num73 - num) : (num - num73));
				int num75 = -(velY_fixed3 * s8.GravMul);
				bool input2 = num74 - num75 > 0;
				if (!StepFrame(ref s8, input2, out endLevel5))
				{
					break;
				}
				int num76 = (s8.X_fixed >> 8) + 1;
				int hitboxW4 = GetHitboxW(s8.Mini);
				int hitboxH4 = GetHitboxH(s8.Mini);
				int hitboxOffsetY4 = GetHitboxOffsetY(s8.GameMode, s8.Mini, s8.GravFlipped);
				int num77 = (s8.Y_fixed >> 8) + hitboxOffsetY4;
				if (num76 + hitboxW4 >= spriteEntry.HitLeft && spriteEntry.HitRight >= num76 && num77 + hitboxH4 >= spriteEntry.HitTop && spriteEntry.HitBottom >= num77)
				{
					flag12 = true;
					break;
				}
			}
			_speculativeDepth--;
			int num78 = (flag10 ? 1 : 10);
			if (flag11 && !flag12 && num5 >= num78)
			{
				return true;
			}
			if (flag12 && !flag11 && num6 >= num78)
			{
				return false;
			}
			int num79 = _shipCoinAggressiveThreshold;
			if (flag10)
			{
				num79 = ((num3 > 400) ? Math.Max(num79, 4) : ((num3 <= 200) ? Math.Max(num79, 20) : Math.Max(num79, 10)));
			}
			else if (num3 <= 600)
			{
				num79 = ((num3 > 300) ? Math.Max(num79, 4) : ((num3 > 200) ? Math.Max(num79, 6) : ((num3 <= 100) ? Math.Max(num79, 20) : Math.Max(num79, 10))));
			}
			if (Math.Abs(num5 - num6) > num79)
			{
				return num5 > num6;
			}
		}
		int num80 = (int)((JumpTimingBias - 0.5) * 16.0);
		foreach (KeyValuePair<int, int> item12 in _coinCollectThenLoseCount)
		{
			if (!_forgivenCoins.Contains(item12.Key))
			{
				num80 -= 3 * item12.Value;
			}
		}
		int num90;
		if (num >= 0)
		{
			int num81 = CorridorCenter(ref state) + _shipCorridorBias + num80;
			if (Math.Abs(num - num81) > 40 && num3 <= 2000)
			{
				int num82 = CorridorCenter(ref state, 2, num) + _shipCorridorBias + num80;
				int num83 = CorridorCenter(ref state, 0) + _shipCorridorBias + num80;
				int num84 = Math.Abs(num - num82);
				int num85 = Math.Abs(num - num83);
				bool flag13 = num84 < num85 - 20;
				int num86 = (flag13 ? num82 : num83);
				int num87 = Math.Abs(num - num86);
				int num88 = ((num87 > 40 && num87 <= 120 && num3 <= 2000) ? Math.Min(2000, Math.Max(400, num87 * 16)) : 400);
				if (num3 > num88)
				{
					int num89 = 80;
					num90 = num86 + (num - num86) * num89 / 100;
				}
				else if (num3 > 400)
				{
					int num91 = num88 - 400;
					int num92 = ((!flag13) ? ((num91 > 0) ? (80 + (num88 - num3) * 10 / num91) : 90) : ((num91 > 0) ? (70 + (num88 - num3) * 25 / num91) : 95));
					num90 = num86 + (num - num86) * num92 / 100;
				}
				else if (num3 > 300)
				{
					int num93 = (flag13 ? 90 : 85);
					num90 = num86 + (num - num86) * num93 / 100;
				}
				else if (num3 > 200)
				{
					int num94 = (flag13 ? 95 : 90);
					num90 = num86 + (num - num86) * num94 / 100;
				}
				else if (num3 > 120)
				{
					int num95 = (flag13 ? 100 : 95);
					num90 = num86 + (num - num86) * num95 / 100;
				}
				else
				{
					num90 = num;
				}
			}
			else
			{
				num90 = num;
			}
		}
		else
		{
			num90 = CorridorCenter(ref state) + _shipCorridorBias + num80;
		}
		int num96 = state.Y_fixed >> 8;
		int velY_fixed4 = state.VelY_fixed;
		int num97 = ((state.GravMul > 0) ? (num96 - num90) : (num90 - num96));
		int num98 = -(velY_fixed4 * state.GravMul);
		int num99 = ((num >= 0) ? 1 : 2);
		return num97 - num98 * num99 > 0;
		int CorridorCenter(ref SimState reference, int lookAhead = 15, int overrideY = -1)
		{
			int num100 = reference.X_fixed >> 8;
			int num101 = ((overrideY >= 0) ? overrideY : (reference.Y_fixed >> 8));
			int num102 = (reference.Mini ? 1 : 0);
			int num103 = ((overrideY >= 0) ? 1 : 0);
			long key = (long)((uint)(num100 & 0xFFFF) | ((ulong)(uint)(num101 & 0xFFFF) << 16) | ((ulong)(uint)(lookAhead & 0xFF) << 32) | ((ulong)(uint)num102 << 40) | ((ulong)(uint)num103 << 41));
			if (shipCorridorCache.TryGetValue(key, out var value))
			{
				return value;
			}
			int num104 = ((num103 != 0) ? FindCorridorCenter(ref reference, lookAhead, overrideY) : ((lookAhead == 15) ? FindCorridorCenter(ref reference) : FindCorridorCenter(ref reference, lookAhead)));
			shipCorridorCache[key] = num104;
			return num104;
		}
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
					if (((s4.GravMul > 0) ? (num20 - num19) : (num19 - num20)) < 0)
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
					var (num12, _, num13, num14) = GetCollisionBounds(tileCollision);
					if (num13 > num12)
					{
						int num15 = num11 * 16 + num14;
						if (num15 > num5)
						{
							num5 = num15;
						}
						break;
					}
					if (IsDeathCollision(tileCollision))
					{
						int num16 = num11 * 16 + 16;
						if (num16 > num5)
						{
							num5 = num16;
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
				var (num17, num18, num19, _) = GetCollisionBounds(tileCollision2);
				if (num19 > num17)
				{
					int num20 = j * 16 + num18;
					if (num20 < num6)
					{
						num6 = num20;
					}
					break;
				}
				if (IsDeathCollision(tileCollision2))
				{
					int num21 = j * 16;
					if (num21 < num6)
					{
						num6 = num21;
					}
					break;
				}
			}
			if (i <= num9)
			{
				continue;
			}
			int num22 = Math.Max(0, num7 - 2);
			int num23 = Math.Min(mapHeight - groundRowsToReserve - 1, num8 + 2);
			for (int k = num22; k <= num23; k++)
			{
				MetatileCollision tileCollision3 = GetTileCollision(i, k);
				if (tileCollision3 == MetatileCollision.COL_NONE)
				{
					continue;
				}
				bool flag = false;
				bool flag2 = false;
				var (num24, num25, num26, num27) = GetCollisionBounds(tileCollision3);
				int num28;
				int num29;
				if (num26 > num24)
				{
					flag = true;
					num28 = k * 16 + num25;
					num29 = k * 16 + num27;
				}
				else
				{
					if (!IsDeathCollision(tileCollision3))
					{
						continue;
					}
					flag2 = true;
					num28 = k * 16;
					num29 = k * 16 + 16;
				}
				if (k >= num7 && k <= num8)
				{
					int num30 = num28 - num5;
					int num31 = num6 - num29;
					if (num30 >= hitboxH && (num30 >= num31 || num31 < hitboxH))
					{
						if (num28 < num6)
						{
							num6 = num28;
						}
					}
					else if (num29 > num5)
					{
						num5 = num29;
					}
				}
				else if (k < num7)
				{
					if (flag && num29 > num5)
					{
						num5 = num29;
					}
					if (flag2 && num29 > num5)
					{
						num5 = num29;
					}
				}
				else if (k > num8)
				{
					if (flag && num28 < num6)
					{
						num6 = num28;
					}
					if (flag2 && num28 < num6)
					{
						num6 = num28;
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
				input = num3 - num4 * 2 > 0;
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
			bool num4 = StepFrame(ref s, flag3, out endLevel);
			int num5 = ((s.Mini && !s.GravFlipped) ? 4 : 0);
			pathPoints?.Add(((s.X_fixed >> 8) + 8, (s.Y_fixed >> 8) + num5 + 8));
			if (s.VelY_fixed == 0)
			{
				int num6 = s.Y_fixed >> 8;
				if (num6 < num3)
				{
					num3 = num6;
				}
			}
			if (!num4)
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
			bool endLevel;
			bool num2 = StepFrame(ref s, flag5, out endLevel);
			int num3 = ((s.Mini && !s.GravFlipped) ? 4 : 0);
			pathPoints?.Add(((s.X_fixed >> 8) + 8, (s.Y_fixed >> 8) + num3 + 8));
			if (!num2)
			{
				int num4 = ((!flag3 && !flag4 && s.GravFlipped != gravFlipped) ? 90 : 0);
				return i + num4;
			}
			if (endLevel)
			{
				return 90;
			}
		}
		int num5 = ((!flag3 && !flag4 && s.GravFlipped != gravFlipped) ? 90 : 0);
		return 90 + num5;
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
		if ((s.GravFlipped ? (num - num2) : (num2 - num)) > 16)
		{
			return true;
		}
		return false;
	}

	private bool StepFrame(ref SimState s, bool input, out bool endLevel)
	{
		endLevel = false;
		s.Step2Ejected = false;
		s.ShipDbgCeilSlopeHit = false;
		s.ShipDbgCeilTileHit = false;
		s.ShipDbgCeilSpike = false;
		s.ShipDbgFloorSlopeHit = false;
		s.ShipDbgFloorTileHit = false;
		s.ShipDbgFloorSpike = false;
		ClearPendingOrbs(ref s);
		int x_fixed = s.X_fixed;
		int num = x_fixed >> 8;
		bool flag = input && !s.PrevInputHeld;
		int gameMode = s.GameMode;
		int velX_fixed = s.VelX_fixed;
		int value = (s.CameraY_fixed >> 8) + (s.Y_fixed - s.CameraY_fixed >> 8);
		OrbDbg($"ENTER mode={s.GameMode} mini={s.Mini} gravF={s.GravFlipped} dashing={s.Dashing} X_fixed=0x{s.X_fixed:X} Y_fixed=0x{s.Y_fixed:X} VelY_fixed=0x{s.VelY_fixed:X} oldX_px={num} playerY_px={value} CamY_px={s.CameraY_fixed >> 8} input={input} prevHeld={s.PrevInputHeld} pressEdge={flag} orbed={s.Orbed} jblock={s.JBlocked} fblock={s.FBlocked} hblock={s.HBlocked} orbHistCount={_hitOrbHistory.Count}");
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
		OrbDbg($"POST_PROCESS pendSlots=[{s.PendingOrbIndex}/0x{((s.PendingOrbSpriteId >= 0) ? s.PendingOrbSpriteId : 0):X2},{s.PendingOrbExtra1Index}/0x{((s.PendingOrbExtra1SpriteId >= 0) ? s.PendingOrbExtra1SpriteId : 0):X2},{s.PendingOrbExtra2Index}/0x{((s.PendingOrbExtra2SpriteId >= 0) ? s.PendingOrbExtra2SpriteId : 0):X2}] input={input} pressEdge={flag}");
		bool flag2 = flag;
		if (s.PendingOrbIndex >= 0 && !flag2)
		{
			OrbDbg($"CLEAR reason=no_input_pressed pendSlots cleared without activation gateMode=press input={input} pressEdge={flag}");
		}
		if (s.PendingOrbIndex >= 0 && flag2)
		{
			int num2 = num + 1;
			int hitboxW = GetHitboxW(s.Mini);
			int hitboxH = GetHitboxH(s.Mini);
			int miniCenterOffsetY = SharedPhysics.GetMiniCenterOffsetY(s.Mini);
			int num3 = (s.Y_fixed >> 8) + miniCenterOffsetY;
			int num4 = num3 + hitboxH;
			int num5 = num2 + hitboxW;
			for (int i = 0; i < 3; i++)
			{
				int num6;
				int num7;
				switch (i)
				{
				case 0:
					num6 = s.PendingOrbIndex;
					num7 = s.PendingOrbSpriteId;
					break;
				case 1:
					num6 = s.PendingOrbExtra1Index;
					num7 = s.PendingOrbExtra1SpriteId;
					break;
				default:
					num6 = s.PendingOrbExtra2Index;
					num7 = s.PendingOrbExtra2SpriteId;
					break;
				}
				if (num6 < 0)
				{
					continue;
				}
				if ((num7 == 123 || num7 == 124) && !flag)
				{
					OrbDbg($"SKIP kind=ACT slot={i} sid=0x{num7:X2} idx={num6} reason=multi_held_no_press_edge");
					continue;
				}
				int velY_fixed = s.VelY_fixed;
				bool gravFlipped = s.GravFlipped;
				OrbDbg($"ACT slot={i} sid=0x{num7:X2} idx={num6} gravF_before={gravFlipped} mini={s.Mini} velY_before=0x{velY_fixed:X} press={flag} held={input}");
				if (_speculativeDepth == 0)
				{
					_hitOrbHistory.Add(num6);
				}
				if (IsDashOrb(num7))
				{
					ApplyDashOrb(ref s, num7);
					s.ProcessedSprites.Add(num6);
					if (!_dualP2Guard)
					{
						_p1OrbIndicesThisFrame.Add(num6);
					}
				}
				else if (IsSpiderOrb(num7))
				{
					bool goUp = num7 == 84;
					ApplySpiderTeleport(ref s, goUp);
					s.Orbed = true;
					s.ProcessedSprites.Add(num6);
					if (!_dualP2Guard)
					{
						_p1OrbIndicesThisFrame.Add(num6);
					}
				}
				else
				{
					ApplyOrbSprite(ref s, num7);
					if (IsBlackOrb(num7) && s.GameMode == 5)
					{
						s.BlackOrbed = true;
					}
					if (num7 != 123 && num7 != 124)
					{
						s.ProcessedSprites.Add(num6);
						if (!_dualP2Guard)
						{
							_p1OrbIndicesThisFrame.Add(num6);
						}
					}
				}
				bool flag3 = num7 == 123 || num7 == 124;
				OrbDbg($"ACT_DONE slot={i} sid=0x{num7:X2} idx={num6} velY_after=0x{s.VelY_fixed:X} gravF_after={s.GravFlipped} dashing={s.Dashing} multiOrb={flag3}");
				if (flag3)
				{
					continue;
				}
				for (int j = SpriteLowerBound(num - 64); j < _spritesArr.Length; j++)
				{
					ref SpriteEntry reference = ref _spritesArr[j];
					if (reference.AnchorX_px - 16 > num5 + 16)
					{
						break;
					}
					if (reference.SpriteId != num7 || s.ProcessedSprites.Contains(reference.Index))
					{
						continue;
					}
					bool num8 = num5 >= reference.HitLeft && reference.HitRight >= num2;
					bool flag4 = num4 >= reference.HitTop && reference.HitBottom >= num3;
					if (num8 && flag4)
					{
						s.ProcessedSprites.Add(reference.Index);
						if (!_dualP2Guard)
						{
							_p1OrbIndicesThisFrame.Add(reference.Index);
						}
						OrbDbg($"SWEEP slot={i} sid=0x{num7:X2} swept_idx={reference.Index} sprBox=({reference.HitLeft},{reference.HitTop})-({reference.HitRight},{reference.HitBottom}) plBox=({num2},{num3})-({num5},{num4})");
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
		int x_fixed2 = s.X_fixed + s.VelX_fixed;
		if (!_dualP2Guard)
		{
			CheckGravityModTriggersAtNewX(ref s);
		}
		if (s.GameMode == 0)
		{
			PfSlopeDiag(ref s, "cube/pre-grav");
			CubeGravity(ref s);
			PfSlopeDiag(ref s, "cube/post-grav");
			int value2 = s.Y_fixed >> 8;
			int velY_fixed2 = s.VelY_fixed;
			bool died = false;
			CubeEject(ref s, input, out died);
			PfSlopeDiag(ref s, "cube/post-eject");
			if (died)
			{
				Console.Error.WriteLine($"[EJECT_DEATH_DBG] frame={_frameCounter} X={s.X_fixed >> 8} Y_before={value2} Y_after={s.Y_fixed >> 8} VelY_before=0x{velY_fixed2:X} VelY_after=0x{s.VelY_fixed:X}");
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
				}
			}
			PfUpdateSlopeCounters_Fresh(ref s);
			PfSlopeDiag(ref s, "cube/post-fresh");
		}
		else if (s.GameMode == 1)
		{
			ShipGravityAndThrust(ref s, input, out var died2);
			if (died2)
			{
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
			BallGravityStep(ref s);
			bool died4 = false;
			BallEject(ref s, input, out died4);
			if (died4)
			{
				s.DeathType = (byte)((s.DeathType >= 11) ? s.DeathType : 6);
				return false;
			}
			if (input && !orbHitThisFrame && !s.Orbed && s.BallFlipCooldown == 0 && s.VelY_fixed == 0)
			{
				s.GravFlipped = !s.GravFlipped;
				s.GravMul = ((!s.GravFlipped) ? 1 : (-1));
				s.VelY_fixed = BallSwitchVel(s.Mini) * s.GravMul;
				s.OnGround = false;
				s.BallFlipCooldown = 1;
				s.BallInputBuffer = 0;
				s.BallCooldownFrames = 0;
			}
			if (s.BallFlipCooldown != 0 && !input)
			{
				s.BallFlipCooldown = 0;
			}
			PfUpdateSlopeCounters_Fresh(ref s);
		}
		else if (s.GameMode == 3)
		{
			UfoGravityStep(ref s);
			ShipEject(ref s, input, out var died5);
			if (died5)
			{
				if (_speculativeDepth == 0)
				{
					_lastDeathReason = "EJECT_DEATH";
					_lastDeathX = s.X_fixed >> 8;
					_lastDeathY = s.Y_fixed >> 8;
				}
				s.DeathType = 2;
				return false;
			}
			if (flag && !orbHitThisFrame && gameMode == 3)
			{
				int velY_fixed3 = UfoJumpVel(s.Mini) * -s.GravMul;
				s.VelY_fixed = velY_fixed3;
			}
			PfUpdateSlopeCounters_Fresh(ref s);
		}
		else if (s.GameMode == 4)
		{
			CubeGravity(ref s);
			bool died6 = false;
			CubeEject(ref s, input, out died6);
			if (died6)
			{
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
			}
			else if (s.RobotJumpTime > 0)
			{
				s.RobotJumpTime--;
				if (input && !s.Orbed)
				{
					s.VelY_fixed = -688 * s.GravMul;
				}
				else
				{
					s.RobotJumpTime = 0;
				}
			}
			PfUpdateSlopeCounters_Fresh(ref s);
		}
		else if (s.GameMode == 6)
		{
			int num9 = velX_fixed;
			int num10 = (s.Mini ? (num9 << 1) : num9);
			if (s.GravFlipped)
			{
				num10 = -num10;
			}
			s.VelY_fixed = num10;
			s.WasZeroedByCollision = false;
			if (input)
			{
				s.VelY_fixed = -s.VelY_fixed;
			}
			if ((s.SlopeFrames | s.SlopeWasOnCounter) == 0)
			{
				s.Y_fixed += s.VelY_fixed;
			}
			else
			{
				s.VelY_fixed = 0;
			}
			bool died7 = false;
			WaveEject(ref s, input, out died7);
			if (died7)
			{
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
				s.DeathType = 3;
				return false;
			}
			int num11 = s.X_fixed >> 8;
			int num12 = s.Y_fixed >> 8;
			int num13 = (s.Mini ? 4 : 0);
			int num14 = num11 + 4 - 1;
			int num15 = num12 + num13 + 4;
			int tileX = num14 / 16;
			int tileY = num15 / 16;
			MetatileCollision tileCollision = GetTileCollision(tileX, tileY);
			if (tileCollision >= MetatileCollision.COL_SLOPE_RD45 && tileCollision <= MetatileCollision.COL_SLOPE_LU66_TOP && (s.Mini || tileCollision != MetatileCollision.COL_SLOPE_LU45) && (!s.Mini || (tileCollision != MetatileCollision.COL_SLOPE_LU66_TOP && tileCollision != MetatileCollision.COL_SLOPE_LU66_BOT)) && PfSlopeCalc(num14, num15, tileCollision).hit)
			{
				s.DeathType = 3;
				return false;
			}
			if ((s.SlopeWasOnCounter | s.SlopeFrames) == 0)
			{
				int num16 = num11 + 8;
				int num17 = num12 + num13 + 4;
				int tileX2 = num16 / 16;
				int tileY2 = num17 / 16;
				MetatileCollision tileCollision2 = GetTileCollision(tileX2, tileY2);
				if (tileCollision2 >= MetatileCollision.COL_SLOPE_RD45 && tileCollision2 <= MetatileCollision.COL_SLOPE_LU66_TOP && (s.Mini || tileCollision2 != MetatileCollision.COL_SLOPE_LU45) && (!s.Mini || (tileCollision2 != MetatileCollision.COL_SLOPE_LU66_TOP && tileCollision2 != MetatileCollision.COL_SLOPE_LU66_BOT)) && PfSlopeCalc(num16, num17, tileCollision2).hit)
				{
					s.SlopeFrames = 1;
					s.SlopeWasOnCounter = 3;
					if (!s.Dblocked)
					{
						s.DeathType = 3;
						return false;
					}
				}
			}
		}
		else if (s.GameMode == 5)
		{
			SpiderGravityStep(ref s);
			int num18 = ((!s.GravFlipped) ? 1 : (-2));
			int offsetY = (s.Y_fixed >> 8) + num18;
			SpiderEject(ref s, offsetY, input, out var died8);
			if (died8)
			{
				s.DeathType = 2;
				return false;
			}
			PfUpdateSlopeCounters_Fresh(ref s);
			s.OnGround = s.VelY_fixed == 0;
			bool flag6 = s.VelY_fixed == 0 && (!s.Orbed || s.BlackOrbed);
			if (input && flag6)
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
			bool died9 = false;
			ShipEject(ref s, input, out died9);
			if (died9)
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
				int hitboxW2 = GetHitboxW(s.Mini);
				int hitboxH2 = GetHitboxH(s.Mini);
				int miniCenterOffsetY2 = SharedPhysics.GetMiniCenterOffsetY(s.Mini);
				int collX = s.X_fixed >> 8;
				int collY = (s.Y_fixed >> 8) + miniCenterOffsetY2 - 1;
				var (flag7, num19, _, _) = CheckCeiling(collX, collY, hitboxW2, hitboxH2);
				if (flag7 && s.VelY_fixed < 0)
				{
					int num20 = num19 - miniCenterOffsetY2 - 1;
					s.Y_fixed = (num20 << 8) | (s.Y_fixed & 0xFF);
					s.VelY_fixed = 0;
					s.OnGround = true;
					s.WasZeroedByCollision = true;
				}
			}
			bool died10 = false;
			CubeEject(ref s, input, out died10);
			if (died10)
			{
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
			}
			(ref s);
		}
		else if (s.GameMode == 9)
		{
			SwingGravityStep(ref s);
			bool died11 = false;
			BallVelocityZeroing(ref s, out died11);
			if (died11)
			{
				s.DeathType = (byte)((s.DeathType >= 11) ? s.DeathType : 6);
				return false;
			}
			int velY_fixed4 = s.VelY_fixed;
			bool died12 = false;
			BallEject(ref s, input, out died12);
			if (died12)
			{
				s.DeathType = (byte)((s.DeathType >= 11) ? s.DeathType : 6);
				return false;
			}
			if (s.OnGround && s.VelY_fixed == 0 && !orbHitThisFrame)
			{
				int num21 = -velY_fixed4 / 3 * 2;
				int num22 = (s.Mini ? SharedPhysics.PadOrbHeights_Mini[1][7] : SharedPhysics.PadOrbHeights[1][7]);
				if (s.GravFlipped)
				{
					if (num21 < num22)
					{
						num21 = num22;
					}
				}
				else
				{
					int num23 = -num22;
					if (num21 > num23)
					{
						num21 = num23;
					}
				}
				s.VelY_fixed = num21;
				s.OnGround = false;
			}
			PfUpdateSlopeCounters_Fresh(ref s);
			if (flag && !orbHitThisFrame && !s.Orbed)
			{
				int num24 = (s.Mini ? SharedPhysics.PadOrbHeights_Mini[6][7] : SharedPhysics.PadOrbHeights[6][7]);
				s.VelY_fixed = (s.GravFlipped ? num24 : (-num24));
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
		if (_dualP2Guard)
		{
			s.X_fixed = x_fixed2;
		}
		if (CheckFloorSpikes(ref s))
		{
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
			bool num25 = _frameCounter >= 4467 && _frameCounter <= 4470 && s.VelY_fixed < 0 && s.Y_fixed >> 8 < 720;
			bool flag8 = CheckForwardCollision(ref s);
			if (num25)
			{
				int value3 = NesPlayerY_px(s.Y_fixed, s.CameraY_fixed);
				Console.Error.WriteLine($"[FWD_DBG] f={_frameCounter} X={s.X_fixed >> 8} Y={s.Y_fixed >> 8} nesY={value3} VelY=0x{s.VelY_fixed:X} fwd={flag8}");
			}
			if (flag8)
			{
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
				var (num26, num27) = SharedPhysics.GetForwardSlopeNudge(in _collisionMap, s.X_fixed >> 8, s.Y_fixed >> 8, hbW, hbH, hbOffY, s.GameMode, s.Mini, s.GravFlipped);
				if (num26 != 0)
				{
					s.Y_fixed += num26 << 8;
				}
				if (num27 != 0)
				{
					s.SlopeType = num27;
					s.LastSlopeType = num27;
					if ((s.GameMode == 0 || s.GameMode == 4 || s.GameMode == 8) && input)
					{
						s.SlopeJumpHigher = true;
					}
					else
					{
						s.SlopeFrames = 1;
						s.SlopeWasOnCounter = 3;
					}
				}
			}
		}
		if (!s.DualActive && (s.GameMode == 0 || s.GameMode == 4 || s.GameMode == 8 || s.GameMode == 9 || s.NoCamLockForced))
		{
			if (s.ExitPortalTimer != 0)
			{
				s.ExitPortalTimer--;
			}
			int num28 = s.Y_fixed - s.CameraY_fixed;
			int num29 = (s.CameraY_fixed >> 8) + _nesCoordOffset;
			int num30 = _minScrollYLin - _nesCoordOffset << 8;
			int num31 = NesMaxCamY_px() << 8;
			if (num28 < 16384)
			{
				if (num29 > _minScrollYLin || (num29 == _minScrollYLin && s.ScrollYSubpx != 0))
				{
					int num32 = 16384 - num28;
					if (s.ExitPortalTimer != 0)
					{
						int num33 = 11 - s.ExitPortalTimer;
						if (num32 >> 8 >= num33)
						{
							num32 = num33 << 8;
						}
					}
					int num34 = num32 & 0xFF;
					int num35 = (num32 >> 8) & 0xFF;
					int num36 = s.ScrollYSubpx - num34;
					int num37 = 0;
					if (num36 < 0)
					{
						num36 += 256;
						num37 = 1;
					}
					s.ScrollYSubpx = num36;
					int num38 = -(num35 + num37 << 8);
					int num39 = num32 + num38;
					s.CameraY_fixed += num38;
					s.Y_fixed += num39;
					if (s.CameraY_fixed < num30)
					{
						int num40 = num30 - s.CameraY_fixed;
						s.Y_fixed -= num40;
						s.Y_fixed -= s.ScrollYSubpx;
						s.ScrollYSubpx = 0;
						s.CameraY_fixed = num30;
					}
				}
			}
			else if (num28 >> 8 >= 160 && num29 < 719)
			{
				int num41 = num28 - 40960;
				if (s.ExitPortalTimer != 0)
				{
					int num42 = 11 - s.ExitPortalTimer;
					if (num41 >> 8 >= num42)
					{
						num41 = num42 << 8;
					}
				}
				int num43 = num41 & 0xFF;
				int num44 = (num41 >> 8) & 0xFF;
				int num45 = s.ScrollYSubpx + num43;
				int num46 = 0;
				if (num45 > 255)
				{
					num45 -= 256;
					num46 = 1;
				}
				s.ScrollYSubpx = num45;
				int num47 = num44 + num46 << 8;
				int num48 = -num41 + num47;
				s.CameraY_fixed += num47;
				s.Y_fixed += num48;
				if (s.CameraY_fixed > num31)
				{
					s.Y_fixed += s.ScrollYSubpx;
					s.ScrollYSubpx = 0;
					s.CameraY_fixed = num31;
				}
			}
		}
		else
		{
			int num49 = s.CameraY_fixed >> 8;
			int num50 = s.TargetCameraY_fixed >> 8;
			if (num50 > num49)
			{
				s.CameraY_fixed += 512;
			}
			else if (num50 < num49)
			{
				s.CameraY_fixed -= 768;
				s.Y_fixed -= 256;
			}
			int num51 = NesMaxCamY_px() << 8;
			int num52 = -(groundRowsToReserve * 16) << 8;
			if (s.CameraY_fixed < num52)
			{
				s.Y_fixed -= s.ScrollYSubpx;
				s.CameraY_fixed = num52;
				s.ScrollYSubpx = 0;
			}
			if (s.CameraY_fixed >= num51)
			{
				s.Y_fixed += s.ScrollYSubpx;
				s.CameraY_fixed = num51;
				s.ScrollYSubpx = 0;
			}
		}
		int num53 = s.Y_fixed - s.CameraY_fixed;
		if (!s.WrapMode)
		{
			if (num53 < 1536)
			{
				if (_speculativeDepth == 0)
				{
					_lastDeathReason = "OOB_TOP";
					_lastDeathX = s.X_fixed >> 8;
					_lastDeathY = s.Y_fixed >> 8;
				}
				s.DeathType = 11;
				return false;
			}
			if (num53 > 63744)
			{
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
		else if (num53 < 1536)
		{
			s.Y_fixed = s.CameraY_fixed + 63744;
		}
		else if (num53 > 63744)
		{
			s.Y_fixed = s.CameraY_fixed + 1536;
		}
		if (!_dualP2Guard)
		{
			CheckGravityModTriggersAtNewX(ref s);
		}
		if (CheckDeathCollision(ref s))
		{
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
			if (_speculativeDepth == 0)
			{
				_lastDeathReason = "SLOPE_DEATH";
				_lastDeathX = s.X_fixed >> 8;
				_lastDeathY = s.Y_fixed >> 8;
			}
			s.DeathType = 9;
			return false;
		}
		int x_fixed3 = s.X_fixed;
		s.X_fixed = x_fixed2;
		if (s.X_fixed != x_fixed3)
		{
			if (CheckDeathCollision(ref s))
			{
				if (_speculativeDepth == 0)
				{
					_lastDeathReason = "DEATH_COLL_NEWX";
					_lastDeathX = s.X_fixed >> 8;
					_lastDeathY = s.Y_fixed >> 8;
				}
				s.DeathType = 9;
				return false;
			}
			if (CheckSlopePenetrationDeath(ref s))
			{
				if (_speculativeDepth == 0)
				{
					_lastDeathReason = "SLOPE_DEATH_NEWX";
					_lastDeathX = s.X_fixed >> 8;
					_lastDeathY = s.Y_fixed >> 8;
				}
				s.DeathType = 9;
				return false;
			}
		}
		if (s.OnGround && s.VelY_fixed == 0 && s.GameMode != 1 && s.GameMode != 3 && s.GameMode != 5 && s.GameMode != 6 && s.GameMode != 7 && s.GameMode != 10 && !VerifyGroundSupport(ref s))
		{
			s.OnGround = false;
			s.WasZeroedByCollision = false;
		}
		int num54 = s.Y_fixed >> 8;
		int num55 = (mapHeight - groundRowsToReserve) * 16;
		int num56 = -(groundRowsToReserve * 16);
		if (num54 < num56 || num54 >= num55 + 16)
		{
			if (_speculativeDepth == 0)
			{
				_lastDeathReason = "BOUNDS_DEATH";
				_lastDeathX = s.X_fixed >> 8;
				_lastDeathY = num54;
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
			int num57 = s.VelY_fixed;
			bool flag9 = s.GravFlipped;
			int gravMul = s.GravMul;
			bool flag10 = s.Mini;
			bool wasZeroedByCollision = s.WasZeroedByCollision;
			bool onGround = s.OnGround;
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
			s.P2_Mini = flag10;
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
			bool flag11 = false;
			if (s.GameMode == 2 && ballCooldownFrames > 0)
			{
				flag11 = true;
			}
			bool input2 = input && !flag11;
			int y_fixed2 = s.Y_fixed;
			int velY_fixed5 = s.VelY_fixed;
			bool gravFlipped2 = s.GravFlipped;
			foreach (int item in _p1OrbIndicesThisFrame)
			{
				s.ProcessedSprites.Remove(item);
			}
			_p2OrbFlippedOtherGrav = false;
			_dualP2Guard = true;
			bool endLevel2;
			bool flag12 = StepFrame(ref s, input2, out endLevel2);
			_dualP2Guard = false;
			foreach (int item2 in _p1OrbIndicesThisFrame)
			{
				s.ProcessedSprites.Add(item2);
			}
			if (_p2OrbFlippedOtherGrav)
			{
				flag9 = !flag9;
				gravMul = ((!flag9) ? 1 : (-1));
				num57 >>= 1;
				_p2OrbFlippedOtherGrav = false;
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
			if (s.P2_Mini != flag10)
			{
				flag10 = s.P2_Mini;
			}
			if (!s.DualActive)
			{
				s.X_fixed = x_fixed2;
				s.Y_fixed = y_fixed2;
				s.VelY_fixed = velY_fixed5;
				s.GravFlipped = gravFlipped2;
				s.GravMul = ((!gravFlipped2) ? 1 : (-1));
				s.Mini = flag10;
				s.WasZeroedByCollision = wasZeroedByCollision;
				s.OnGround = onGround;
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
				if (endLevel2)
				{
					endLevel = true;
					return true;
				}
				if (!flag12)
				{
					return false;
				}
				return true;
			}
			s.X_fixed = x_fixed2;
			s.Y_fixed = y_fixed;
			s.VelY_fixed = num57;
			s.GravFlipped = flag9;
			s.GravMul = gravMul;
			s.Mini = flag10;
			s.WasZeroedByCollision = wasZeroedByCollision;
			s.OnGround = onGround;
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
			if (!flag12)
			{
				return false;
			}
		}
		return true;
	}

	private bool CubeWillLandThisFrame(SimState state)
	{
		int num = GetGravity(state.Mini) * state.GravMul;
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
		int num = SharedPhysics.GetCubeGravity(s.Mini);
		int num2 = maxFallSpeed;
		if (s.GravFlipped)
		{
			num = -num;
			num2 = -num2;
		}
		int clampMaxY = Math.Max(0, mapHeight * 16 - 16) << 8;
		SharedPhysics.CommonGravityRoutine(ref s.VelY_fixed, ref s.Y_fixed, num, num2, s.GravFlipped ? 255 : 0, s.Dashing, s.GravityMod, 1.0, isFullSpeed: true, s.VelX_fixed, clampMaxY);
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
			var (flag, num, _, _) = CheckCeiling(collX, collY, cubeHitboxW, cubeHitboxH);
			if (flag)
			{
				int num2 = num - hitboxOffsetY;
				s.Y_fixed = (num2 << 8) | (s.Y_fixed & 0xFF);
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
				s.Y_fixed = (num4 << 8) | (s.Y_fixed & 0xFF);
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
		int num = BallGravity(s.Mini);
		int num2 = BallMaxFallSpeed(s.Mini);
		if (s.GravFlipped)
		{
			num = -num;
			num2 = -num2;
		}
		int clampMaxY = Math.Max(0, mapHeight * 16 - 16) << 8;
		SharedPhysics.CommonGravityRoutine(ref s.VelY_fixed, ref s.Y_fixed, num, num2, s.GravFlipped ? 255 : 0, s.Dashing, s.GravityMod, 1.0, isFullSpeed: true, s.VelX_fixed, clampMaxY);
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
				var (flag, num, _, _) = CheckCeiling(collX, collY, hitboxW, hitboxH);
				if (flag)
				{
					int num2 = num - hitboxOffsetY;
					s.Y_fixed = (num2 << 8) | (s.Y_fixed & 0xFF);
					s.VelY_fixed = 0;
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
				s.Y_fixed = (num6 << 8) | (s.Y_fixed & 0xFF);
				s.VelY_fixed = 0;
			}
		}
	}

	private void BallEject(ref SimState s, bool input, out bool died)
	{
		died = false;
		s.OnGround = false;
		SharedPhysics.EjectResult ejectResult = SharedPhysics.BallEject(in _collisionMap, s.X_fixed, s.Y_fixed, s.VelY_fixed, s.VelX_fixed, s.GravFlipped, s.Mini, s.GameMode, input, s.SlopeWasOnCounter, s.SlopeFrames, s.SlopeType, s.SlopeJumpHigher, s.LastSlopeType, s.CameraY_fixed);
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
		int playerX_px = s.X_fixed >> 8;
		int playerY_px = s.Y_fixed >> 8;
		int hitboxW = GetHitboxW(s.Mini);
		int hitboxH = GetHitboxH(s.Mini);
		int hitboxOffsetY = GetHitboxOffsetY(2, s.Mini, s.GravFlipped);
		return SharedPhysics.BallGroundedProbeSpikeDeath(in _collisionMap, playerX_px, playerY_px, hitboxW, hitboxH, hitboxOffsetY, s.GravFlipped);
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
		if (!isOverrideFrame && !_btSuppressJumpUntilAirborne && ScanForOrbOverlap(in state, out var orbSpriteIndex) >= 0)
		{
			SimState s = state.Clone();
			bool endLevel;
			bool flag = StepFrame(ref s, input: true, out endLevel);
			if (endLevel)
			{
				return true;
			}
			int num = 0;
			if (flag)
			{
				for (int i = -1; i < 15; i++)
				{
					int num2 = 1 + SimulateForwardWithJumpAt(s, i);
					if (num2 > num)
					{
						num = num2;
					}
				}
			}
			SimState s2 = state.Clone();
			bool endLevel2;
			bool num3 = StepFrame(ref s2, input: false, out endLevel2);
			s2.ProcessedSprites.Add(orbSpriteIndex);
			int num4 = 0;
			if (num3)
			{
				num4 = 1 + Math.Max(SimulateForwardWithJumpAt(s2, -1), SimulateForwardWithJumpAt(s2, 0));
			}
			if (num >= num4)
			{
				return true;
			}
			state.ProcessedSprites.Add(orbSpriteIndex);
			return false;
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
		if (!isOverrideFrame && !_btSuppressJumpUntilAirborne && ScanForOrbOverlap(in state, out var orbSpriteIndex) >= 0)
		{
			SimState s = state.Clone();
			bool endLevel;
			bool flag = StepFrame(ref s, input: true, out endLevel);
			if (endLevel)
			{
				return true;
			}
			int num = 0;
			if (flag)
			{
				for (int i = -1; i < 15; i++)
				{
					int num2 = 1 + SimulateForwardWithJumpAt(s, i);
					if (num2 > num)
					{
						num = num2;
					}
				}
			}
			SimState s2 = state.Clone();
			bool endLevel2;
			bool num3 = StepFrame(ref s2, input: false, out endLevel2);
			s2.ProcessedSprites.Add(orbSpriteIndex);
			int num4 = 0;
			if (num3)
			{
				num4 = 1 + Math.Max(SimulateForwardWithJumpAt(s2, -1), SimulateForwardWithJumpAt(s2, 0));
			}
			if (num >= num4)
			{
				return true;
			}
			state.ProcessedSprites.Add(orbSpriteIndex);
			return false;
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
		if (num6 > num5 && num6 >= 2)
		{
			_committedJumpDelay = -1;
			return true;
		}
		return false;
	}

	private bool DecidePogoInput(SimState state, bool isOverrideFrame)
	{
		if (!isOverrideFrame && !_btSuppressJumpUntilAirborne && ScanForOrbOverlap(in state, out var orbSpriteIndex) >= 0)
		{
			SimState s = state.Clone();
			bool endLevel;
			bool flag = StepFrame(ref s, input: true, out endLevel);
			if (endLevel)
			{
				return true;
			}
			int num = 0;
			if (flag)
			{
				for (int i = -1; i < 15; i++)
				{
					int num2 = 1 + SimulateForwardWithJumpAt(s, i);
					if (num2 > num)
					{
						num = num2;
					}
				}
			}
			SimState s2 = state.Clone();
			bool endLevel2;
			bool num3 = StepFrame(ref s2, input: false, out endLevel2);
			s2.ProcessedSprites.Add(orbSpriteIndex);
			int num4 = 0;
			if (num3)
			{
				num4 = 1 + Math.Max(SimulateForwardWithJumpAt(s2, -1), SimulateForwardWithJumpAt(s2, 0));
			}
			if (num >= num4)
			{
				return true;
			}
			state.ProcessedSprites.Add(orbSpriteIndex);
			return false;
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
		if (num6 > num5 && num6 >= 2)
		{
			_committedJumpDelay = num7;
			return num7 == 0;
		}
		return false;
	}

	private bool DecideBallInput(SimState state, bool isOverrideFrame)
	{
		if (!isOverrideFrame && !_btSuppressJumpUntilAirborne && ScanForOrbOverlap(in state, out var orbSpriteIndex) >= 0)
		{
			SimState s = state.Clone();
			bool endLevel;
			bool flag = StepFrame(ref s, input: true, out endLevel);
			if (endLevel)
			{
				return true;
			}
			int num = 0;
			if (flag)
			{
				int num2 = Math.Min(15, 90);
				for (int i = -1; i < num2; i++)
				{
					int num3 = 1 + SimulateForwardWithJumpAt(s, i);
					if (num3 > num)
					{
						num = num3;
					}
				}
			}
			SimState s2 = state.Clone();
			bool endLevel2;
			bool flag2 = StepFrame(ref s2, input: false, out endLevel2);
			s2.ProcessedSprites.Add(orbSpriteIndex);
			int num4 = 0;
			if (endLevel2)
			{
				num4 = 90;
			}
			else if (flag2)
			{
				int val = 1 + SimulateForwardWithJumpAt(s2, -1);
				int val2 = 1 + SimulateForwardWithJumpAt(s2, 0);
				num4 = Math.Max(val, val2);
			}
			if (num >= num4)
			{
				return true;
			}
			state.ProcessedSprites.Add(orbSpriteIndex);
			return false;
		}
		if (_committedJumpDelay > 0)
		{
			_committedJumpDelay--;
			if (_committedJumpDelay == 0)
			{
				_committedJumpDelay = -1;
				return true;
			}
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
		int num5 = SimulateForwardWithJumpAt(state, -1, holdAfterLanding: false, list);
		_speculativeDepth--;
		OnSpeculativePath?.Invoke(list, -1, num5, arg4: false);
		int finalX_px = state.X_fixed >> 8;
		_speculativeDepth++;
		SimulateForwardWithJumpAt(state, -1, out finalX_px);
		_speculativeDepth--;
		int num6 = num5;
		int num7 = finalX_px;
		List<(int, int, int)> list2 = new List<(int, int, int)>();
		_speculativeDepth++;
		for (int j = 0; j < 35; j++)
		{
			List<(int, int)> list3 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
			int finalX_px2 = state.X_fixed >> 8;
			int num8 = SimulateForwardWithJumpAt(state, j, out finalX_px2, holdAfterLanding: false, -1, list3);
			OnSpeculativePath?.Invoke(list3, j, num8, arg4: false);
			if (num8 > num6)
			{
				num6 = num8;
			}
			if (finalX_px2 > num7)
			{
				num7 = finalX_px2;
			}
			if (finalX_px2 > finalX_px || num8 > num5)
			{
				list2.Add((j, num8, finalX_px2));
			}
		}
		_speculativeDepth--;
		_speculativeDepth++;
		for (int k = 0; k < 35; k++)
		{
			List<(int, int)> list4 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
			int finalX_px3 = state.X_fixed >> 8;
			int num9 = SimulateForwardWithJumpAt(state, k, out finalX_px3, holdAfterLanding: false, -1, list4, singleJumpOnly: true);
			OnSpeculativePath?.Invoke(list4, k, num9, arg4: false);
			if (num9 > num6)
			{
				num6 = num9;
			}
			if (finalX_px3 > num7)
			{
				num7 = finalX_px3;
			}
			if (finalX_px3 > finalX_px || num9 > num5)
			{
				list2.Add((k, num9, finalX_px3));
			}
		}
		_speculativeDepth--;
		if (list2.Count == 0)
		{
			if (num5 >= 90)
			{
				_speculativeDepth++;
				int finalX_px4;
				int num10 = SimulateForwardWithJumpAt(state, -1, out finalX_px4, holdAfterLanding: false, 180);
				_speculativeDepth--;
				if (num10 < 180)
				{
					_speculativeDepth++;
					for (int l = 0; l < 35; l++)
					{
						List<(int, int)> list5 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
						int finalX_px5;
						int num11 = SimulateForwardWithJumpAt(state, l, out finalX_px5, holdAfterLanding: false, 180, list5);
						OnSpeculativePath?.Invoke(list5, l, num11, arg4: false);
						if (finalX_px5 > finalX_px4 || num11 > num10)
						{
							list2.Add((l, num11, finalX_px5));
							if (num11 > num6)
							{
								num6 = num11;
							}
							if (finalX_px5 > num7)
							{
								num7 = finalX_px5;
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
			int num12 = state.X_fixed >> 8;
			for (int m = _nextCoinCheckIdx; m < allCoins.Count; m++)
			{
				SpriteEntry spriteEntry = allCoins[m];
				if (state.ProcessedSprites.Contains(spriteEntry.Index) || _forgivenCoins.Contains(spriteEntry.Index))
				{
					continue;
				}
				if (spriteEntry.HitLeft > num12 + 800)
				{
					break;
				}
				if (spriteEntry.HitRight < num12)
				{
					continue;
				}
				List<(int, int, int)> list6 = new List<(int, int, int)>();
				Dictionary<int, (int survival, int xProgress)> viableLookup = new Dictionary<int, (int, int)>();
				foreach (var (key, item, item2) in list2)
				{
					viableLookup[key] = (item, item2);
				}
				int num13 = 200 * state.VelX_fixed / 256;
				int num14 = (spriteEntry.HitLeft + spriteEntry.HitRight) / 2 - num12;
				int num15 = Math.Max(35, 200);
				if (num14 <= num13)
				{
					_speculativeDepth++;
					int num16 = int.MaxValue;
					int num17 = int.MaxValue;
					int value = -1;
					int num18 = -1;
					int num19 = 0;
					int num20 = 0;
					int value2 = 0;
					int value3 = 0;
					int value4 = -1;
					int num21 = 0;
					for (int n = 0; n < num15; n++)
					{
						SimState s3 = state.Clone();
						bool flag3 = false;
						bool flag4 = false;
						int num22 = 0;
						int num23 = -1;
						for (int num24 = 0; num24 < 200; num24++)
						{
							bool flag5 = false;
							if (!flag4)
							{
								if (num24 == n)
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
								num22 = num24;
								if (num24 > num21)
								{
									num21 = num24;
									value2 = s3.X_fixed >> 8;
									value3 = s3.Y_fixed >> 8;
									value4 = n;
								}
								if (flag3 && num24 - num23 < 10)
								{
									flag3 = false;
								}
								break;
							}
							if (endLevel3)
							{
								num22 = 200;
								break;
							}
							int num25 = (s3.X_fixed >> 8) + 1;
							int hitboxW = GetHitboxW(s3.Mini);
							int hitboxH = GetHitboxH(s3.Mini);
							int hitboxOffsetY = GetHitboxOffsetY(s3.GameMode, s3.Mini, s3.GravFlipped);
							int num26 = (s3.Y_fixed >> 8) + hitboxOffsetY;
							if (!flag3 && num25 + hitboxW >= spriteEntry.HitLeft && spriteEntry.HitRight >= num25 && num26 + hitboxH >= spriteEntry.HitTop && spriteEntry.HitBottom >= num26)
							{
								flag3 = true;
								num23 = num24;
							}
							int val3 = Math.Max(spriteEntry.HitLeft - (num25 + hitboxW), num25 - spriteEntry.HitRight);
							int val4 = Math.Max(spriteEntry.HitTop - (num26 + hitboxH), num26 - spriteEntry.HitBottom);
							int num27 = Math.Max(val3, 0) + Math.Max(val4, 0);
							long num28 = (long)num16 + (long)num17;
							if (num27 < num28 || (num27 == num28 && num24 < num18))
							{
								num16 = Math.Max(val3, 0);
								num17 = Math.Max(val4, 0);
								value = n;
								num18 = num24;
							}
							if (num24 + 1 > num20)
							{
								num20 = num24 + 1;
							}
							num22 = num24 + 1;
						}
						if (!flag3 && num22 < 200)
						{
							num19++;
						}
						if (flag3)
						{
							if (viableLookup.TryGetValue(n, out (int, int) value5))
							{
								list6.Add((n, value5.Item1, value5.Item2));
							}
							else
							{
								list6.Add((n, num22, s3.X_fixed >> 8));
							}
						}
					}
					_speculativeDepth--;
					if (list6.Count == 0 && num14 <= num13)
					{
						_log.WriteLine($"[BALL_COIN_MISS_DETAIL] idx={spriteEntry.Index} playerX={num12} playerY={state.Y_fixed >> 8} gravFlip={state.GravFlipped} coinHit=({spriteEntry.HitLeft},{spriteEntry.HitTop})-({spriteEntry.HitRight},{spriteEntry.HitBottom}) closestXDist={num16} closestYDist={num17} closestDelay={value} closestFrame={num18} diedCount={num19}/{num15} maxFrame={num20} deathX={value2} deathY={value3} deathDelay={value4}");
					}
				}
				if (list6.Count > 0)
				{
					int value6 = list6.Count<(int, int, int)>(((int delay, int survival, int xProgress) cd) => viableLookup.ContainsKey(cd.delay));
					_log.WriteLine($"[BALL_COIN_FOUND] idx={spriteEntry.Index} {list6.Count}/{num15} delays collect coin ({value6} viable) at playerX={num12} playerY={state.Y_fixed >> 8} gravFlip={state.GravFlipped} delays=[{string.Join(",", list6.Select<(int, int, int), int>(((int delay, int survival, int xProgress) cd) => cd.delay).Take(10))}]");
					list2 = list6;
				}
				if (list6.Count == 0)
				{
					_speculativeDepth++;
					SimState s4 = state.Clone();
					bool flag6 = false;
					for (int num29 = 0; num29 < 200; num29++)
					{
						if (!StepFrame(ref s4, input: false, out var endLevel4))
						{
							break;
						}
						if (endLevel4)
						{
							break;
						}
						int num30 = (s4.X_fixed >> 8) + 1;
						int hitboxW2 = GetHitboxW(s4.Mini);
						int hitboxH2 = GetHitboxH(s4.Mini);
						int hitboxOffsetY2 = GetHitboxOffsetY(s4.GameMode, s4.Mini, s4.GravFlipped);
						int num31 = (s4.Y_fixed >> 8) + hitboxOffsetY2;
						if (num30 + hitboxW2 >= spriteEntry.HitLeft && spriteEntry.HitRight >= num30 && num31 + hitboxH2 >= spriteEntry.HitTop && spriteEntry.HitBottom >= num31)
						{
							flag6 = true;
							break;
						}
					}
					_speculativeDepth--;
					if (flag6)
					{
						return false;
					}
				}
				if (list6.Count != 0 || spriteEntry.HitLeft <= num12 + 20)
				{
					break;
				}
				int num32 = (spriteEntry.HitTop + spriteEntry.HitBottom) / 2;
				int num33 = state.Y_fixed >> 8;
				bool num34 = num32 < num33;
				bool flag7 = !state.GravFlipped;
				if (num34 && flag7)
				{
					int num35 = (spriteEntry.HitLeft + spriteEntry.HitRight) / 2;
					int num36 = (num35 - num12) * 256 / Math.Max(state.VelX_fixed, 1);
					if (num5 >= num36)
					{
						_log.WriteLine($"[BALL_COIN_APPROACH] idx={spriteEntry.Index} suppress flip, walk forward playerX={num12} coinX={num35} dist={num35 - num12} noPressFrames={num5}");
						return false;
					}
					_speculativeDepth++;
					int num37 = SimulateForwardWithJumpAt(state, 0);
					_speculativeDepth--;
					if (num37 >= num36)
					{
						_log.WriteLine($"[BALL_COIN_APPROACH] idx={spriteEntry.Index} FLIP to ceiling (floor dies at {num5}, ceiling survives {num37}, need {num36}) playerX={num12}");
						return true;
					}
					if (num37 >= num5)
					{
						_log.WriteLine($"[BALL_COIN_APPROACH] idx={spriteEntry.Index} FLIP to ceiling (floor dies at {num5}, ceiling dies at {num37}, need {num36} \ufffd ceiling at least as good) playerX={num12}");
						return true;
					}
					_log.WriteLine($"[BALL_COIN_APPROACH] idx={spriteEntry.Index} suppress flip (ceiling dies at {num37} < floor {num5}, need {num36}) playerX={num12}");
					return false;
				}
				bool num38 = num32 > num33;
				bool gravFlipped = state.GravFlipped;
				if (!(num38 && gravFlipped))
				{
					break;
				}
				int num39 = (spriteEntry.HitLeft + spriteEntry.HitRight) / 2;
				int num40 = (num39 - num12) * 256 / Math.Max(state.VelX_fixed, 1);
				if (num5 < num40)
				{
					break;
				}
				_log.WriteLine($"[BALL_COIN_APPROACH] idx={spriteEntry.Index} suppress flip (ceiling), walk forward playerX={num12} coinX={num39} dist={num39 - num12} noPressFrames={num5}");
				return false;
			}
		}
		int xThreshold = num7 - 8;
		List<(int, int, int)> list7 = list2.Where<(int, int, int)>(((int delay, int survival, int xProgress) d) => d.xProgress >= xThreshold).ToList();
		if (list7.Count == 0)
		{
			list7 = list2;
		}
		int value7 = (int)(JumpTimingBias * (double)(list7.Count - 1));
		value7 = Math.Clamp(value7, 0, list7.Count - 1);
		int item3 = list7[value7].Item1;
		if (item3 > 0)
		{
			_committedJumpDelay = item3;
			return false;
		}
		return true;
	}

	private bool DecideUfoInput(SimState state, bool isOverrideFrame)
	{
		if (!isOverrideFrame && !_btSuppressJumpUntilAirborne && ScanForOrbOverlap(in state, out var orbSpriteIndex) >= 0)
		{
			SimState s = state.Clone();
			bool endLevel;
			bool flag = StepFrame(ref s, input: true, out endLevel);
			if (endLevel)
			{
				return true;
			}
			int num = 0;
			if (flag)
			{
				int num2 = Math.Min(15, 90);
				for (int i = -1; i < num2; i++)
				{
					int num3 = 1 + SimulateForwardWithJumpAt(s, i);
					if (num3 > num)
					{
						num = num3;
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
			int num4 = 0;
			if (flag2)
			{
				int num5 = Math.Min(15, 90);
				for (int j = -1; j < num5; j++)
				{
					int num6 = 1 + SimulateForwardWithJumpAt(s2, j);
					if (num6 > num4)
					{
						num4 = num6;
					}
				}
			}
			return num >= num4;
		}
		if (_speculativeDepth >= 3)
		{
			return false;
		}
		_speculativeDepth++;
		List<(int, int)> list = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
		int num7 = SimulateForwardWithJumpAt(state, -1, holdAfterLanding: false, list);
		OnSpeculativePath?.Invoke(list, -1, num7, arg4: false);
		_speculativeDepth--;
		int num8 = num7;
		int num9 = -1;
		_speculativeDepth++;
		int num10 = Math.Min(15, 90);
		for (int k = 0; k < num10; k++)
		{
			List<(int, int)> list2 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
			int num11 = 1 + SimulateForwardWithJumpAt(state, k, holdAfterLanding: false, list2);
			OnSpeculativePath?.Invoke(list2, k, num11, arg4: false);
			if (num11 > num8)
			{
				num8 = num11;
				num9 = k;
			}
		}
		_speculativeDepth--;
		if (num9 < 0)
		{
			return false;
		}
		int num12 = (int)((double)num9 * JumpTimingBias + 0.5);
		if (_committedJumpDelay < 0)
		{
			_committedJumpDelay = num12;
			if (num12 == 0)
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
		if (!isOverrideFrame && !_btSuppressJumpUntilAirborne && ScanForOrbOverlap(in state, out var orbSpriteIndex) >= 0)
		{
			SimState s = state.Clone();
			bool endLevel;
			bool flag = StepFrame(ref s, input: true, out endLevel);
			if (endLevel)
			{
				return true;
			}
			int num = 0;
			if (flag)
			{
				num = 1 + SimulateRobotForward(s, 0);
			}
			SimState s2 = state.Clone();
			s2.ProcessedSprites.Add(orbSpriteIndex);
			bool endLevel2;
			bool flag2 = StepFrame(ref s2, input: false, out endLevel2);
			if (endLevel2)
			{
				return false;
			}
			int num2 = 0;
			if (flag2)
			{
				num2 = 1 + SimulateRobotForward(s2, 0);
			}
			return num >= num2;
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
		int num3 = SimulateRobotForward(state, 0, list);
		_speculativeDepth--;
		OnSpeculativePath?.Invoke(list, -1, num3, arg4: false);
		int num4 = num3;
		int num5 = 0;
		_speculativeDepth++;
		int[] array = new int[8] { 1, 3, 5, 8, 11, 14, 17, 19 };
		foreach (int num6 in array)
		{
			List<(int, int)> list2 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
			int num7 = SimulateRobotForward(state, num6, list2);
			OnSpeculativePath?.Invoke(list2, num6, num7, arg4: true);
			if (num7 > num4)
			{
				num4 = num7;
				num5 = num6;
			}
		}
		_speculativeDepth--;
		if (num5 == 0)
		{
			return false;
		}
		if (JumpTimingBias < 0.45)
		{
			_speculativeDepth++;
			for (int j = 1; j < num5; j++)
			{
				if (SimulateRobotForward(state, j) >= num4 - 2)
				{
					num5 = j;
					break;
				}
			}
			_speculativeDepth--;
		}
		else if (JumpTimingBias > 0.55)
		{
			_speculativeDepth++;
			for (int num8 = 19; num8 > num5; num8--)
			{
				if (SimulateRobotForward(state, num8) >= num4 - 2)
				{
					num5 = num8;
					break;
				}
			}
			_speculativeDepth--;
		}
		_committedRobotHold = num5 - 1;
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
		if (!isOverrideFrame && !_btSuppressJumpUntilAirborne && ScanForOrbOverlap(in state, out var orbSpriteIndex) >= 0)
		{
			SimState s = state.Clone();
			bool endLevel;
			bool flag = StepFrame(ref s, input: true, out endLevel);
			if (endLevel)
			{
				return true;
			}
			int num = 0;
			if (flag)
			{
				num = 1 + SimulateNinjaForward(s, null);
			}
			SimState s2 = state.Clone();
			s2.ProcessedSprites.Add(orbSpriteIndex);
			bool endLevel2;
			bool flag2 = StepFrame(ref s2, input: false, out endLevel2);
			if (endLevel2)
			{
				return false;
			}
			int num2 = 0;
			if (flag2)
			{
				num2 = 1 + SimulateNinjaForward(s2, null);
			}
			return num >= num2;
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
		bool flag3 = state.VelY_fixed == 0;
		if (!flag3 && state.NinjaJumps <= 0)
		{
			return false;
		}
		if (_speculativeDepth >= 3)
		{
			return false;
		}
		_speculativeDepth++;
		List<(int, int)> list = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
		int num3 = SimulateNinjaForward(state, null, list);
		_speculativeDepth--;
		OnSpeculativePath?.Invoke(list, -1, num3, arg4: false);
		int num4 = num3;
		int[] array = null;
		int num5 = (flag3 ? 3 : state.NinjaJumps);
		_speculativeDepth++;
		List<(int, int)> list2 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
		int num6 = SimulateNinjaForward(state, new int[0], list2);
		OnSpeculativePath?.Invoke(list2, 0, num6, arg4: true);
		if (num6 > num4)
		{
			num4 = num6;
			array = new int[0];
		}
		if (num5 >= 2)
		{
			int[] array2 = new int[5] { 3, 6, 10, 15, 20 };
			foreach (int num7 in array2)
			{
				List<(int, int)> list3 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
				int num8 = SimulateNinjaForward(state, new int[1] { num7 }, list3);
				OnSpeculativePath?.Invoke(list3, num7, num8, arg4: true);
				if (num8 > num4)
				{
					num4 = num8;
					array = new int[1] { num7 };
				}
			}
		}
		if (num5 >= 3)
		{
			int[] array3 = new int[4] { 3, 6, 10, 15 };
			for (int j = 0; j < array3.Length; j++)
			{
				for (int k = 0; k < array3.Length; k++)
				{
					int num9 = array3[j];
					int num10 = array3[k];
					List<(int, int)> list4 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
					int num11 = SimulateNinjaForward(state, new int[2] { num9, num10 }, list4);
					OnSpeculativePath?.Invoke(list4, num9 * 100 + num10, num11, arg4: true);
					if (num11 > num4)
					{
						num4 = num11;
						array = new int[2] { num9, num10 };
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
		if (!isOverrideFrame && !_btSuppressJumpUntilAirborne && ScanForOrbOverlap(in state, out var orbSpriteIndex) >= 0)
		{
			SimState s = state.Clone();
			bool endLevel;
			bool flag = StepFrame(ref s, input: true, out endLevel);
			if (endLevel)
			{
				return true;
			}
			int num = (flag ? (1 + SimulateWaveForward(s, hold: true)) : 0);
			SimState s2 = state.Clone();
			s2.ProcessedSprites.Add(orbSpriteIndex);
			bool endLevel2;
			bool flag2 = StepFrame(ref s2, input: false, out endLevel2);
			if (endLevel2)
			{
				return false;
			}
			int num2 = (flag2 ? (1 + SimulateWaveForward(s2, hold: false)) : 0);
			return num >= num2;
		}
		if (_speculativeDepth >= 3)
		{
			return false;
		}
		_speculativeDepth++;
		List<(int, int)> list = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
		int num3 = SimulateWaveForward(state, hold: true, list);
		List<(int, int)> list2 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
		int num4 = SimulateWaveForward(state, hold: false, list2);
		_speculativeDepth--;
		CurrentSpeculativeVizMode = 6;
		OnSpeculativePath?.Invoke(list, 0, num3, arg4: true);
		CurrentSpeculativeVizMode = 6;
		OnSpeculativePath?.Invoke(list2, -1, num4, arg4: false);
		if (num3 > num4 + 3)
		{
			return true;
		}
		if (num4 > num3 + 3)
		{
			return false;
		}
		int num5 = state.Y_fixed >> 8;
		int num6 = (state.X_fixed >> 8) + 8;
		int num7 = 0;
		for (int i = 1; i <= 80; i++)
		{
			int num8 = num5 - i;
			if (num8 < 0)
			{
				num7 = i;
				break;
			}
			int tileX = num6 / 16;
			int tileY = num8 / 16;
			MetatileCollision tileCollision = GetTileCollision(tileX, tileY);
			if (tileCollision != MetatileCollision.COL_NONE && !SharedPhysics.IsDeathCollision(tileCollision))
			{
				num7 = i;
				break;
			}
			if (i == 80)
			{
				num7 = 80;
			}
		}
		int num9 = 0;
		for (int j = 1; j <= 80; j++)
		{
			int num10 = num5 + 16 + j;
			if (num10 >= mapHeight * 16)
			{
				num9 = j;
				break;
			}
			int tileX2 = num6 / 16;
			int tileY2 = num10 / 16;
			MetatileCollision tileCollision2 = GetTileCollision(tileX2, tileY2);
			if (tileCollision2 != MetatileCollision.COL_NONE && !SharedPhysics.IsDeathCollision(tileCollision2))
			{
				num9 = j;
				break;
			}
			if (j == 80)
			{
				num9 = 80;
			}
		}
		double num11 = 0.5 + (JumpTimingBias - 0.5) * 0.6;
		int num12 = (int)((double)(num7 + num9) * (1.0 - num11));
		int num13 = (int)((double)(num7 + num9) * num11);
		bool flag3;
		if (num7 < num12 - 2)
		{
			flag3 = false;
		}
		else
		{
			if (num9 >= num13 - 2)
			{
				if (num3 > num4)
				{
					return true;
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
		int num4 = ((!s.Mini) ? 4 : 0);
		int num5 = num2 + num4;
		if (s.VelY_fixed >= 0)
		{
			int num6 = num3;
			int num7 = (s.Mini ? 4 : 0);
			int num8 = num5 + 8 - 2 + num7;
			int num9 = 8;
			for (int i = 0; i < 2; i++)
			{
				int num10 = num6 + i * num9;
				int tileX = num10 / 16;
				int tileY = num8 / 16;
				MetatileCollision tileCollision = GetTileCollision(tileX, tileY);
				if (tileCollision < MetatileCollision.COL_SLOPE_RD45 || tileCollision > MetatileCollision.COL_SLOPE_LU66_TOP || (!s.Mini && tileCollision == MetatileCollision.COL_SLOPE_LU45) || (s.Mini && (tileCollision == MetatileCollision.COL_SLOPE_LU66_TOP || tileCollision == MetatileCollision.COL_SLOPE_LU66_BOT)))
				{
					continue;
				}
				var (flag, num11, num12) = PfSlopeCalc(num10, num8, tileCollision);
				if (!flag)
				{
					continue;
				}
				s.SlopeFrames = 1;
				s.SlopeWasOnCounter = 3;
				bool flag2 = (num12 & 4) != 0;
				if ((i == 0 && flag2) || (i == 1 && !flag2))
				{
					continue;
				}
				if (s.Dblocked)
				{
					if (num11 > 0)
					{
						int num13 = s.Y_fixed >> 8;
						num13 -= num11;
						s.Y_fixed = (num13 << 8) | (s.Y_fixed & 0xFF);
					}
					s.VelY_fixed = 0;
					s.WasZeroedByCollision = true;
				}
				else
				{
					died = true;
				}
				return;
			}
		}
		else
		{
			int num14 = num3;
			int num15 = 4;
			int num16 = num5 + num15 + (s.Mini ? 1 : 2);
			int num17 = 8;
			for (int j = 0; j < 2; j++)
			{
				int num18 = num14 + j * num17;
				int tileX2 = num18 / 16;
				int tileY2 = num16 / 16;
				MetatileCollision tileCollision2 = GetTileCollision(tileX2, tileY2);
				if (tileCollision2 < MetatileCollision.COL_SLOPE_RD45 || tileCollision2 > MetatileCollision.COL_SLOPE_LU66_TOP || (!s.Mini && tileCollision2 == MetatileCollision.COL_SLOPE_LU45) || (s.Mini && (tileCollision2 == MetatileCollision.COL_SLOPE_LU66_TOP || tileCollision2 == MetatileCollision.COL_SLOPE_LU66_BOT)))
				{
					continue;
				}
				var (flag3, num19, num20) = PfSlopeCalc(num18, num16, tileCollision2);
				if (!flag3)
				{
					continue;
				}
				s.SlopeFrames = 1;
				s.SlopeWasOnCounter = 3;
				bool flag4 = (num20 & 4) != 0;
				if (!(j == 0 && flag4) && (j != 1 || flag4))
				{
					if (s.Dblocked)
					{
						int num21 = s.Y_fixed >> 8;
						num21 -= num19;
						s.Y_fixed = (num21 << 8) | (s.Y_fixed & 0xFF);
						s.VelY_fixed = 0;
						s.WasZeroedByCollision = true;
					}
					else
					{
						died = true;
					}
					return;
				}
			}
		}
		if (s.VelY_fixed < 0)
		{
			int num22 = num3 + 4;
			int num23 = (s.Mini ? 4 : 0);
			int num24 = num5 + num23 - 1;
			var (flag5, _, flag6, metatileCollision) = CheckCeiling(num22, num24, 8, 8);
			if (!flag6)
			{
				int num25 = num24 + 1;
				for (int k = 0; k < 3; k++)
				{
					int num26 = k switch
					{
						1 => num22 + 4, 
						0 => num22, 
						_ => num22 + 8, 
					};
					int tileX3 = num26 / 16;
					int tileY3 = num25 / 16;
					MetatileCollision tileCollision3 = GetTileCollision(tileX3, tileY3);
					if (tileCollision3 != MetatileCollision.COL_NONE)
					{
						int localX = (num26 % 16 + 16) % 16;
						int localY = (num25 % 16 + 16) % 16;
						if (MetatileCollisionTable.TileKillsAtPixel(tileCollision3, localX, localY))
						{
							flag6 = true;
							break;
						}
					}
				}
			}
			if (!flag6)
			{
				int num27 = num3 + 8;
				int num28 = num24 + 1;
				int tileX4 = num27 / 16;
				int tileY4 = num28 / 16;
				MetatileCollision tileCollision4 = GetTileCollision(tileX4, tileY4);
				int localX2 = (num27 % 16 + 16) % 16;
				int localY2 = (num28 % 16 + 16) % 16;
				if (tileCollision4 == MetatileCollision.COL_DEATH_TOP || tileCollision4 == MetatileCollision.COL_DEATH_BOTTOM)
				{
					if (MetatileCollisionTable.TileKillsAtPixel(tileCollision4, localX2, localY2))
					{
						flag6 = true;
					}
				}
				else if (tileCollision4 == MetatileCollision.COL_FLOOR_CEIL)
				{
					s.Dblocked = true;
					flag5 = true;
					metatileCollision = tileCollision4;
				}
				else if (tileCollision4 == MetatileCollision.COL_ALL || tileCollision4 == MetatileCollision.COL_NO_SIDE || (tileCollision4 != MetatileCollision.COL_NONE && TileOccupiesPixel(tileCollision4, localX2, localY2)))
				{
					flag5 = true;
					metatileCollision = tileCollision4;
				}
			}
			if (flag6)
			{
				died = true;
			}
			else if (flag5)
			{
				int tileX5 = num22 / 16;
				int tileY5 = (num24 - 1) / 16;
				if (GetTileCollision(tileX5, tileY5) == MetatileCollision.COL_FLOOR_CEIL)
				{
					s.Dblocked = true;
				}
				if (s.Dblocked)
				{
					int num29 = ((num24 + 1) % 16 + 16) % 16;
					int num30 = (sbyte)(byte)(((metatileCollision == MetatileCollision.COL_NO_SIDE || metatileCollision == MetatileCollision.COL_ALL || metatileCollision == MetatileCollision.COL_FLOOR_CEIL) ? 240 : 248) | num29);
					int num31 = (s.Y_fixed >> 8) - num30;
					s.Y_fixed = (num31 << 8) | (s.Y_fixed & 0xFF);
					s.VelY_fixed = 0;
					s.WasZeroedByCollision = true;
				}
				else
				{
					died = true;
				}
			}
		}
		else
		{
			if (s.VelY_fixed < 0)
			{
				return;
			}
			int num32 = num3 + 4;
			int num33 = (s.Mini ? 4 : 0);
			int num34 = num5 + num33;
			int[] array = new int[3]
			{
				num32,
				num32 + 4,
				num3 + 8
			};
			bool flag7 = false;
			bool flag8 = false;
			MetatileCollision metatileCollision2 = MetatileCollision.COL_NONE;
			bool flag9 = false;
			bool flag10 = false;
			int num35 = num34 + 8;
			foreach (int num36 in array)
			{
				int tileX6 = num36 / 16;
				int tileY6 = num35 / 16;
				MetatileCollision tileCollision5 = GetTileCollision(tileX6, tileY6);
				if (tileCollision5 == MetatileCollision.COL_DEATH_TOP || tileCollision5 == MetatileCollision.COL_DEATH_BOTTOM)
				{
					int localX3 = (num36 % 16 + 16) % 16;
					int localY3 = (num35 % 16 + 16) % 16;
					if (MetatileCollisionTable.TileKillsAtPixel(tileCollision5, localX3, localY3))
					{
						flag9 = true;
						break;
					}
				}
				var (flag11, num37, flag12, _, metatileCollision3) = SharedPhysics.CheckFloorDetailed(in _collisionMap, num36, num34, 0, 8, s.VelY_fixed);
				if (flag12)
				{
					flag10 = true;
				}
				else if (flag11)
				{
					flag7 = true;
					metatileCollision2 = metatileCollision3;
					flag10 = false;
					break;
				}
			}
			if (flag9 || (!flag7 && flag10))
			{
				flag8 = true;
			}
			if (!flag8)
			{
				for (int m = 0; m < 3; m++)
				{
					int num38 = array[m];
					int tileX7 = num38 / 16;
					int tileY7 = num35 / 16;
					MetatileCollision tileCollision6 = GetTileCollision(tileX7, tileY7);
					if (tileCollision6 != MetatileCollision.COL_NONE)
					{
						int localX4 = (num38 % 16 + 16) % 16;
						int localY4 = (num35 % 16 + 16) % 16;
						if (MetatileCollisionTable.TileKillsAtPixel(tileCollision6, localX4, localY4))
						{
							flag8 = true;
							break;
						}
					}
				}
			}
			if (flag8)
			{
				died = true;
			}
			else if (flag7)
			{
				if (metatileCollision2 == MetatileCollision.COL_FLOOR_CEIL)
				{
					s.Dblocked = true;
				}
				if (s.Dblocked)
				{
					int num39 = ((num34 + 8) % 16 + 16) % 16;
					int num40 = (s.Y_fixed >> 8) - num39;
					s.Y_fixed = (num40 << 8) | (s.Y_fixed & 0xFF);
					s.VelY_fixed = 0;
					s.WasZeroedByCollision = true;
				}
				else
				{
					died = true;
				}
			}
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
				s.Y_fixed = ((s.Y_fixed >> 8) - num4 << 8) | (s.Y_fixed & 0xFF);
				s.VelY_fixed = 0;
				s.WasZeroedByCollision = true;
				s.SlopeFrames = 1;
				s.SlopeWasOnCounter = 3;
				s.SlopeType = slopeType;
				return;
			}
			var (flag2, num5) = BgCollD_Spider(playerX_px, num3, width, num, useEjectProbes: true);
			if (flag2)
			{
				s.Y_fixed = ((s.Y_fixed >> 8) - num5 << 8) | (s.Y_fixed & 0xFF);
				s.VelY_fixed = 0;
				s.WasZeroedByCollision = true;
			}
			else
			{
				s.WasZeroedByCollision = false;
			}
		}
		else
		{
			var (flag3, num6) = BgCollU_Spider(playerX_px, num3, width, num, useEjectProbes: true);
			if (flag3)
			{
				s.Y_fixed = (num3 + num6 << 8) | (s.Y_fixed & 0xFF);
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
		s.Y_fixed = (num << 8) | (s.Y_fixed & 0xFF);
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
		s.Y_fixed = (num3 << 8) | (s.Y_fixed & 0xFF);
	}

	private (bool hit, int ejectAmount) BgCollD_Spider(int playerX_px, int playerY_px, int width, int height, bool useEjectProbes = false)
	{
		int num = playerY_px + height;
		int num2 = num / 16 + _collisionMap.GroundRowsToReserve;
		if (num2 >= _collisionMap.MapHeight)
		{
			int num3 = (mapHeight - groundRowsToReserve) * 16;
			return (hit: true, ejectAmount: num - num3);
		}
		if (num2 < 0)
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
		for (int i = 0; i < array2.Length; i++)
		{
			int num4 = array2[i] / 16;
			if (num4 < 0 || num4 >= _collisionMap.MapWidth)
			{
				continue;
			}
			int num5 = num2 * _collisionMap.MapWidth + num4;
			if (num5 < 0 || num5 >= _collisionMap.Tiles.Length)
			{
				continue;
			}
			MetatileCollision collision = MetatileCollisionTable.GetCollision((byte)SharedPhysics.MapTileForCollision(_collisionMap.Tiles[num5]));
			if (IsSolidForSpider(collision))
			{
				int item = SharedPhysics.GetCollisionBounds(collision).top;
				int num6 = (num2 - _collisionMap.GroundRowsToReserve) * 16 + item;
				if (num >= num6)
				{
					return (hit: true, ejectAmount: num - num6);
				}
			}
		}
		return (hit: false, ejectAmount: 0);
	}

	private (bool hit, int ejectAmount) BgCollU_Spider(int playerX_px, int playerY_px, int width, int height, bool useEjectProbes = false)
	{
		int num = playerY_px / 16 + _collisionMap.GroundRowsToReserve;
		if (num < 0)
		{
			return (hit: true, ejectAmount: -playerY_px);
		}
		if (num >= _collisionMap.MapHeight)
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
		for (int i = 0; i < array2.Length; i++)
		{
			int num2 = array2[i] / 16;
			if (num2 < 0 || num2 >= _collisionMap.MapWidth)
			{
				continue;
			}
			int num3 = num * _collisionMap.MapWidth + num2;
			if (num3 >= 0 && num3 < _collisionMap.Tiles.Length)
			{
				MetatileCollision collision = MetatileCollisionTable.GetCollision((byte)SharedPhysics.MapTileForCollision(_collisionMap.Tiles[num3]));
				if (IsSolidForSpider(collision))
				{
					int item = SharedPhysics.GetCollisionBounds(collision).bottom;
					int num4 = (num - _collisionMap.GroundRowsToReserve) * 16 + item;
					return (hit: true, ejectAmount: num4 - playerY_px);
				}
			}
		}
		return (hit: false, ejectAmount: 0);
	}

	private static bool IsSolidForSpider(MetatileCollision col)
	{
		if (col != MetatileCollision.COL_NONE)
		{
			return !SharedPhysics.IsDeathCollision(col);
		}
		return false;
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
		int gravMul = s.GravMul;
		bool flag = ((gravMul > 0) ? (s.VelY_fixed > 0) : (s.VelY_fixed < 0));
		int num = ((holding && flag) ? (ShipGravityHoldFall(s.Mini) * gravMul) : (holding ? (ShipGravityBase(s.Mini) * gravMul) : (flag ? (ShipGravity(s.Mini) * gravMul) : (ShipGravityAfterHold(s.Mini) * gravMul))));
		if (s.GravFlipped ^ holding)
		{
			num = -num;
		}
		int tmpfallspeed = 17475;
		int clampMaxY = Math.Max(0, mapHeight * 16 - 16) << 8;
		SharedPhysics.CommonGravityRoutine(ref s.VelY_fixed, ref s.Y_fixed, num, tmpfallspeed, s.GravFlipped ? 255 : 0, s.Dashing, s.GravityMod, 1.0, isFullSpeed: true, s.VelX_fixed, clampMaxY);
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
		SharedPhysics.EjectResult ejectResult = SharedPhysics.ShipUfoEject(in _collisionMap, s.X_fixed, s.Y_fixed, s.VelY_fixed, s.VelX_fixed, s.GravFlipped, s.Mini, s.GameMode, input, s.SlopeWasOnCounter, s.SlopeFrames, s.SlopeType, s.SlopeJumpHigher, s.LastSlopeType, s.CameraY_fixed);
		s.Y_fixed = ejectResult.NewY_fixed;
		s.VelY_fixed = ejectResult.NewVelY_fixed;
		s.SlopeType = ejectResult.SlopeType;
		s.SlopeFrames = ejectResult.SlopeFrames;
		s.SlopeWasOnCounter = ejectResult.SlopeWasOnCounter;
		s.SlopeJumpHigher = ejectResult.SlopeJumpHigher;
		s.LastSlopeType = ejectResult.LastSlopeType;
		s.ShipDbgCeilSlopeHit = ejectResult.DebugCeilSlopeHit;
		s.ShipDbgCeilTileHit = ejectResult.DebugCeilTileHit;
		s.ShipDbgCeilSpike = ejectResult.DebugCeilSpike;
		s.ShipDbgFloorSlopeHit = ejectResult.DebugFloorSlopeHit;
		s.ShipDbgFloorTileHit = ejectResult.DebugFloorTileHit;
		s.ShipDbgFloorSpike = ejectResult.DebugFloorSpike;
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
			else
			{
				switch (spriteId)
				{
				case 221:
				case 237:
					if (!num14.HasValue || allSprite.AnchorX_px > num14.Value)
					{
						num13 = spriteId;
						num14 = allSprite.AnchorX_px;
					}
					break;
				case 142:
				case 158:
					if (!num16.HasValue || allSprite.AnchorX_px > num16.Value)
					{
						num15 = spriteId;
						num16 = allSprite.AnchorX_px;
					}
					break;
				}
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
		s.GravFlippedAtFrameStart = s.GravFlipped;
		s.OrbUseFrameStartGravitySign = false;
		Span<int> span = stackalloc int[16];
		int num = 0;
		int num2 = NesPlayerY_px(s.Y_fixed, s.CameraY_fixed);
		int num3;
		int num4;
		if (s.GameMode != 6)
		{
			num3 = ((s.GameMode == 10) ? 1 : 0);
			if (num3 == 0)
			{
				num4 = GetHitboxW(s.Mini);
				goto IL_005e;
			}
		}
		else
		{
			num3 = 1;
		}
		num4 = 8;
		goto IL_005e;
		IL_005e:
		int num5 = num4;
		int num6 = ((num3 != 0) ? 8 : GetHitboxH(s.Mini));
		int num7 = ((num3 != 0) ? 4 : GetHitboxOffsetY(s.GameMode, s.Mini, s.GravFlipped));
		int num8 = num2 + num7;
		int num9 = num8 + num6;
		int num10 = currentX_px + 1;
		int num11 = num10 + num5;
		bool dualActive = s.DualActive;
		SpriteEntry[] spritesArr = _spritesArr;
		int num12 = spritesArr.Length;
		int num13 = SpriteLowerBound(currentX_px - 64);
		for (int i = num13; i < num12; i++)
		{
			ref SpriteEntry reference = ref spritesArr[i];
			if (s.ProcessedSprites.Contains(reference.Index) || reference.HitRight < currentX_px)
			{
				continue;
			}
			if (reference.AnchorX_px - 16 > num11 + 16)
			{
				break;
			}
			int spriteId = reference.SpriteId;
			if (spriteId != 34 && spriteId != 35)
			{
				continue;
			}
			bool num14 = num11 >= reference.HitLeft && reference.HitRight >= num10;
			bool flag = num9 >= reference.HitTop && reference.HitBottom >= num8;
			if (num14 && flag)
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
				}
				else if (spriteId == 35 && s.DualActive)
				{
					s.DualActive = false;
					s.ExitPortalTimer = 10;
				}
				s.ProcessedSprites.Add(reference.Index);
			}
		}
		int num15 = -1;
		int num16 = -1;
		for (int j = num13; j < num12; j++)
		{
			ref SpriteEntry reference2 = ref spritesArr[j];
			if (reference2.HitRight < currentX_px)
			{
				continue;
			}
			if (reference2.AnchorX_px - 16 > num11 + 16)
			{
				break;
			}
			if (!IsGravityPortal(reference2.SpriteId) || s.ProcessedSprites.Contains(reference2.Index))
			{
				continue;
			}
			bool num17 = num11 >= reference2.HitLeft && reference2.HitRight >= num10;
			bool flag2 = num9 >= reference2.HitTop && reference2.HitBottom >= num8;
			if (!(num17 && flag2))
			{
				continue;
			}
			if (num15 < 0)
			{
				num15 = j;
				num16 = j;
				continue;
			}
			if (j < num15)
			{
				num15 = j;
			}
			if (j > num16)
			{
				num16 = j;
			}
		}
		if (num15 >= 0)
		{
			int num18 = num16 - num15 + 1;
			Span<int> span2 = ((num18 > 8) ? ((Span<int>)new int[num18]) : stackalloc int[num18]);
			Span<int> span3 = span2;
			int num19 = 0;
			for (int k = num15; k <= num16; k++)
			{
				ref SpriteEntry reference3 = ref spritesArr[k];
				if (!IsGravityPortal(reference3.SpriteId) || s.ProcessedSprites.Contains(reference3.Index))
				{
					continue;
				}
				bool num20 = num11 >= reference3.HitLeft && reference3.HitRight >= num10;
				bool flag3 = num9 >= reference3.HitTop && reference3.HitBottom >= num8;
				if (!(num20 && flag3))
				{
					continue;
				}
				for (int l = num13; l < num12; l++)
				{
					ref SpriteEntry reference4 = ref spritesArr[l];
					if (reference4.HitRight < currentX_px)
					{
						continue;
					}
					if (reference4.AnchorX_px - 16 > num11 + 16)
					{
						break;
					}
					if (reference4.AnchorX_px != reference3.AnchorX_px || s.ProcessedSprites.Contains(reference4.Index))
					{
						continue;
					}
					int spriteId2 = reference4.SpriteId;
					if (IsYellowOrb(spriteId2) || IsYellowOrbBigger(spriteId2) || IsYellowOrbSmaller(spriteId2) || IsPinkOrb(spriteId2) || IsRedOrb(spriteId2) || IsBlackOrb(spriteId2))
					{
						bool num21 = num11 >= reference4.HitLeft && reference4.HitRight >= num10;
						bool flag4 = num9 >= reference4.HitTop && reference4.HitBottom >= num8;
						if (num21 && flag4)
						{
							s.OrbUseFrameStartGravitySign = true;
							break;
						}
					}
				}
				span3[num19++] = k;
			}
			for (int m = 1; m < num19; m++)
			{
				int num22 = span3[m];
				int index = spritesArr[num22].Index;
				int num23 = m - 1;
				while (num23 >= 0 && spritesArr[span3[num23]].Index > index)
				{
					span3[num23 + 1] = span3[num23];
					num23--;
				}
				span3[num23 + 1] = num22;
			}
			for (int n = 0; n < num19; n++)
			{
				ref SpriteEntry reference5 = ref spritesArr[span3[n]];
				int spriteId3 = reference5.SpriteId;
				if (ApplyPortalSprite(ref s, spriteId3))
				{
					s.ProcessedSprites.Add(reference5.Index);
				}
				if (num < span.Length)
				{
					span[num++] = reference5.Index;
				}
			}
		}
		int num24 = -1;
		int num25 = -1;
		for (int num26 = num13; num26 < num12; num26++)
		{
			ref SpriteEntry reference6 = ref spritesArr[num26];
			if (reference6.HitRight < currentX_px)
			{
				continue;
			}
			if (reference6.AnchorX_px - 16 > num11 + 16)
			{
				break;
			}
			if (!IsSpeedPortal(reference6.SpriteId) || s.ProcessedSprites.Contains(reference6.Index))
			{
				continue;
			}
			bool num27 = num11 >= reference6.HitLeft && reference6.HitRight >= num10;
			bool flag5 = num9 >= reference6.HitTop && reference6.HitBottom >= num8;
			if (!(num27 && flag5))
			{
				continue;
			}
			if (num24 < 0)
			{
				num24 = num26;
				num25 = num26;
				continue;
			}
			if (num26 < num24)
			{
				num24 = num26;
			}
			if (num26 > num25)
			{
				num25 = num26;
			}
		}
		if (num24 >= 0)
		{
			int num28 = num25 - num24 + 1;
			Span<int> span2 = ((num28 > 8) ? ((Span<int>)new int[num28]) : stackalloc int[num28]);
			Span<int> span4 = span2;
			int num29 = 0;
			for (int num30 = num24; num30 <= num25; num30++)
			{
				ref SpriteEntry reference7 = ref spritesArr[num30];
				if (IsSpeedPortal(reference7.SpriteId) && !s.ProcessedSprites.Contains(reference7.Index))
				{
					bool num31 = num11 >= reference7.HitLeft && reference7.HitRight >= num10;
					bool flag6 = num9 >= reference7.HitTop && reference7.HitBottom >= num8;
					if (num31 && flag6)
					{
						span4[num29++] = num30;
					}
				}
			}
			for (int num32 = 1; num32 < num29; num32++)
			{
				int num33 = span4[num32];
				int allocatedSlot = spritesArr[num33].AllocatedSlot;
				int num34 = ((allocatedSlot < 0) ? int.MaxValue : allocatedSlot);
				int num35;
				for (num35 = num32 - 1; num35 >= 0; num35--)
				{
					int num36 = span4[num35];
					int allocatedSlot2 = spritesArr[num36].AllocatedSlot;
					if (((allocatedSlot2 < 0) ? int.MaxValue : allocatedSlot2) <= num34)
					{
						break;
					}
					span4[num35 + 1] = span4[num35];
				}
				span4[num35 + 1] = num33;
			}
			for (int num37 = 0; num37 < num29; num37++)
			{
				int num38 = SpriteIdToSpeedFixed(spritesArr[span4[num37]].SpriteId);
				if (num38 > 0)
				{
					s.VelX_fixed = num38;
				}
			}
		}
		for (int num39 = num13; num39 < num12; num39++)
		{
			ref SpriteEntry reference8 = ref spritesArr[num39];
			if (s.ProcessedSprites.Contains(reference8.Index) || reference8.HitRight < currentX_px)
			{
				continue;
			}
			if (reference8.AnchorX_px - 16 > num11 + 16)
			{
				break;
			}
			int spriteId4 = reference8.SpriteId;
			if (IsSpeedPortal(spriteId4) || spriteId4 == 34 || spriteId4 == 35)
			{
				continue;
			}
			switch (spriteId4)
			{
			case 221:
			case 237:
				if (num11 >= reference8.HitLeft && reference8.HitRight >= num10)
				{
					s.NoCamLockForced = spriteId4 == 221;
					s.ProcessedSprites.Add(reference8.Index);
				}
				continue;
			case 142:
			case 158:
			{
				bool num41 = num11 >= reference8.HitLeft && reference8.HitRight >= num10;
				bool flag8 = num9 >= reference8.HitTop && reference8.HitBottom >= num8;
				if (num41 && flag8)
				{
					s.WrapMode = spriteId4 == 142;
					s.ProcessedSprites.Add(reference8.Index);
				}
				continue;
			}
			case 100:
			case 126:
			{
				if (_dualP2Guard)
				{
					break;
				}
				bool num40 = num11 >= reference8.HitLeft && reference8.HitRight >= num10;
				bool flag7 = num9 >= reference8.HitTop && reference8.HitBottom >= num8;
				if (!(num40 && flag7))
				{
					continue;
				}
				if (s.RainbowMaxMode == 0)
				{
					if (s.GameMode == 6 || s.GameMode == 10)
					{
						s.VelY_fixed = 0;
					}
					s.RainbowMaxMode = ((spriteId4 == 100) ? 8 : 12);
				}
				s.RobotJumpTime = 0;
				if (s.GameMode != 0 && s.GameMode != 4 && s.GameMode != 8 && s.GameMode != 9 && !dualActive)
				{
					int portalWorldY_px = reference8.AnchorY_px - 8;
					s.TargetCameraY_fixed = NesNtCameraTarget_fixed(portalWorldY_px);
				}
				s.ProcessedSprites.Add(reference8.Index);
				continue;
			}
			}
			if (IsGameModePortal(spriteId4) && _dualP2Guard)
			{
				continue;
			}
			if (IsGameModePortal(spriteId4) || IsGravityPortal(spriteId4) || IsMiniGrowthPortal(spriteId4) || IsEndLevel(spriteId4))
			{
				if (IsGravityPortal(spriteId4))
				{
					bool flag9 = false;
					for (int num42 = 0; num42 < num; num42++)
					{
						if (span[num42] == reference8.Index)
						{
							flag9 = true;
							break;
						}
					}
					if (flag9)
					{
						continue;
					}
				}
				bool flag10 = num11 >= reference8.HitLeft && reference8.HitRight >= num10;
				bool flag11 = num9 >= reference8.HitTop && reference8.HitBottom >= num8;
				bool flag13;
				if (IsEndLevel(spriteId4))
				{
					int num43 = s.CameraY_fixed >> 8;
					int num44 = num43 + 240;
					int num45 = reference8.AnchorY_px - 8;
					bool flag12 = num45 + 16 >= num43 && num45 < num44;
					flag13 = flag10 && flag12;
				}
				else
				{
					flag13 = flag10 && flag11;
				}
				if (flag13)
				{
					bool flag14 = ApplyPortalSprite(ref s, spriteId4);
					if (flag14)
					{
						s.ProcessedSprites.Add(reference8.Index);
					}
					if (IsGameModePortal(spriteId4) && flag14 && s.GameMode != 0 && s.GameMode != 4 && s.GameMode != 8 && s.GameMode != 9 && !dualActive)
					{
						int portalWorldY_px2 = reference8.AnchorY_px - 8;
						s.TargetCameraY_fixed = NesNtCameraTarget_fixed(portalWorldY_px2);
					}
					if (IsEndLevel(spriteId4))
					{
						return true;
					}
				}
				continue;
			}
			if (spriteId4 >= 95 && spriteId4 <= 99)
			{
				bool num46 = num11 >= reference8.HitLeft && reference8.HitRight >= num10;
				bool flag15 = num9 >= reference8.HitTop && reference8.HitBottom >= num8;
				if (num46 && flag15)
				{
					double gravityMod = 1.0;
					switch (spriteId4)
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
					s.GravityMod = gravityMod;
					s.ProcessedSprites.Add(reference8.Index);
				}
				continue;
			}
			if (spriteId4 >= 112 && spriteId4 <= 116)
			{
				continue;
			}
			bool flag17;
			int num49;
			if (IsYellowPad(spriteId4) || IsPinkPad(spriteId4) || IsRedPad(spriteId4) || IsBluePad(spriteId4) || IsGreenPad(spriteId4))
			{
				int num47 = currentX_px + 1;
				bool num48 = num47 + num5 >= reference8.HitLeft && reference8.HitRight >= num47;
				bool flag16 = num9 >= reference8.HitTop && reference8.HitBottom >= num8;
				if (!(num48 && flag16))
				{
					continue;
				}
				flag17 = true;
				if (IsBluePad(spriteId4))
				{
					if (spriteId4 != 13)
					{
						num49 = ((spriteId4 == 253) ? 1 : 0);
						if (num49 == 0)
						{
							goto IL_0e2b;
						}
					}
					else
					{
						num49 = 1;
					}
					if (s.GravFlipped)
					{
						flag17 = false;
					}
					goto IL_0e2b;
				}
				goto IL_0e38;
			}
			if (IsOrbSprite(spriteId4))
			{
				if (s.Dashing != 0)
				{
					OrbDbg($"SKIP kind=PEND reason=dashing sid=0x{spriteId4:X2} idx={reference8.Index} dashing={s.Dashing}");
					continue;
				}
				if (s.ProcessedSprites.Contains(reference8.Index))
				{
					OrbDbg($"SKIP kind=PEND reason=processed sid=0x{spriteId4:X2} idx={reference8.Index}");
				}
				int miniCenterOffsetY = SharedPhysics.GetMiniCenterOffsetY(s.Mini);
				int num50 = num2 + miniCenterOffsetY;
				int num51 = num50 + num6;
				bool flag18 = num11 >= reference8.HitLeft && reference8.HitRight >= num10;
				bool flag19 = num51 >= reference8.HitTop && reference8.HitBottom >= num50;
				OrbDbg($"CHECK sid=0x{spriteId4:X2} idx={reference8.Index} anchor=({reference8.AnchorX_px},{reference8.AnchorY_px}) sprBox=({reference8.HitLeft},{reference8.HitTop})-({reference8.HitRight},{reference8.HitBottom}) plBox=({num10},{num50})-({num11},{num51}) playerY_px={num2} hbH={num6} hbW={num5} mini={s.Mini} gravF={s.GravFlipped} orbOffY={miniCenterOffsetY} xO={flag18} yO={flag19} processed={s.ProcessedSprites.Contains(reference8.Index)} pendSlots=[{s.PendingOrbIndex},{s.PendingOrbExtra1Index},{s.PendingOrbExtra2Index}]");
				if (flag18 && flag19)
				{
					AddPendingOrb(ref s, reference8.Index, spriteId4);
					OrbDbg($"PEND sid=0x{spriteId4:X2} idx={reference8.Index} pendSlots=[{s.PendingOrbIndex},{s.PendingOrbExtra1Index},{s.PendingOrbExtra2Index}]");
				}
				continue;
			}
			if (IsDashOrb(spriteId4))
			{
				if (s.Dashing == 0)
				{
					int miniCenterOffsetY2 = SharedPhysics.GetMiniCenterOffsetY(s.Mini);
					int num52 = num2 + miniCenterOffsetY2;
					int num53 = num52 + num6;
					bool num54 = num11 >= reference8.HitLeft && reference8.HitRight >= num10;
					bool flag20 = num53 >= reference8.HitTop && reference8.HitBottom >= num52;
					if (num54 && flag20)
					{
						AddPendingOrb(ref s, reference8.Index, spriteId4);
					}
				}
				continue;
			}
			if (IsSpiderOrb(spriteId4) || IsSpiderPad(spriteId4))
			{
				int miniCenterOffsetY3 = SharedPhysics.GetMiniCenterOffsetY(s.Mini);
				int num55 = num2 + miniCenterOffsetY3;
				int num56 = num55 + num6;
				bool num57 = num11 >= reference8.HitLeft && reference8.HitRight >= num10;
				bool flag21 = num56 >= reference8.HitTop && reference8.HitBottom >= num55;
				if (num57 && flag21)
				{
					if (IsSpiderOrb(spriteId4))
					{
						AddPendingOrb(ref s, reference8.Index, spriteId4);
						continue;
					}
					bool goUp = spriteId4 == 86;
					ApplySpiderTeleport(ref s, goUp);
					orbHitThisFrame = true;
				}
				continue;
			}
			switch (spriteId4)
			{
			case 249:
			{
				bool num58 = num11 >= reference8.HitLeft && reference8.HitRight >= num10;
				bool flag22 = num9 >= reference8.HitTop && reference8.HitBottom >= num8;
				if (num58 && flag22 && s.Dashing != 0)
				{
					s.Dashing = 0;
					s.Orbed = true;
					s.VelY_fixed = 0;
				}
				continue;
			}
			case 247:
			{
				bool num60 = num11 >= reference8.HitLeft && reference8.HitRight >= num10;
				bool flag24 = num9 >= reference8.HitTop && reference8.HitBottom >= num8;
				if (num60 && flag24)
				{
					s.JBlocked = true;
				}
				continue;
			}
			case 246:
			{
				bool num62 = num11 >= reference8.HitLeft && reference8.HitRight >= num10;
				bool flag26 = num9 >= reference8.HitTop && reference8.HitBottom >= num8;
				if (num62 && flag26)
				{
					s.FBlocked = true;
				}
				continue;
			}
			case 248:
			{
				bool num59 = num11 >= reference8.HitLeft && reference8.HitRight >= num10;
				bool flag23 = num9 >= reference8.HitTop && reference8.HitBottom >= num8;
				if (num59 && flag23)
				{
					s.HBlocked = true;
				}
				continue;
			}
			case 250:
			{
				bool num61 = num11 >= reference8.HitLeft && reference8.HitRight >= num10;
				bool flag25 = num9 >= reference8.HitTop && reference8.HitBottom >= num8;
				if (num61 && flag25)
				{
					s.Dblocked = true;
				}
				continue;
			}
			}
			if (IsTeleportPortalEntrance(spriteId4))
			{
				bool num63 = num11 >= reference8.HitLeft && reference8.HitRight >= num10;
				bool flag27 = num9 >= reference8.HitTop && reference8.HitBottom >= num8;
				if (!(num63 && flag27))
				{
					continue;
				}
				ApplyTeleportPortal(ref s, reference8, spriteId4, currentX_px);
				s.ProcessedSprites.Add(reference8.Index);
				num2 = NesPlayerY_px(s.Y_fixed, s.CameraY_fixed);
				num8 = num2 + num7;
				num9 = num8 + num6;
				for (int num64 = num13; num64 < num12; num64++)
				{
					ref SpriteEntry reference9 = ref spritesArr[num64];
					if (reference9.HitRight < currentX_px)
					{
						continue;
					}
					if (reference9.AnchorX_px - 16 > num11 + 16)
					{
						break;
					}
					int spriteId5 = reference9.SpriteId;
					if (IsYellowPad(spriteId5) || IsPinkPad(spriteId5) || IsRedPad(spriteId5) || IsBluePad(spriteId5) || IsGreenPad(spriteId5))
					{
						if (IsBluePad(spriteId5))
						{
							bool flag28 = spriteId5 == 13 || spriteId5 == 253;
							if ((flag28 && s.GravFlipped) || (!flag28 && !s.GravFlipped) || s.ProcessedSprites.Contains(reference9.Index))
							{
								continue;
							}
						}
						int num65 = currentX_px + 1;
						bool num66 = num65 + num5 >= reference9.HitLeft && reference9.HitRight >= num65;
						bool flag29 = num9 >= reference9.HitTop && reference9.HitBottom >= num8;
						if (num66 && flag29)
						{
							ApplyPadSprite(ref s, spriteId5);
							orbHitThisFrame = true;
							if (IsBluePad(spriteId5) && !s.DualActive)
							{
								s.ProcessedSprites.Add(reference9.Index);
							}
						}
					}
					else if (IsSpiderPad(spriteId5))
					{
						int miniCenterOffsetY4 = SharedPhysics.GetMiniCenterOffsetY(s.Mini);
						int num67 = num2 + miniCenterOffsetY4;
						int num68 = num67 + num6;
						bool num69 = num11 >= reference9.HitLeft && reference9.HitRight >= num10;
						bool flag30 = num68 >= reference9.HitTop && reference9.HitBottom >= num67;
						if (num69 && flag30)
						{
							bool goUp2 = spriteId5 == 86;
							ApplySpiderTeleport(ref s, goUp2);
							orbHitThisFrame = true;
						}
					}
				}
			}
			else if (IsCoinSprite(spriteId4) || IsMiniCoinSprite(spriteId4))
			{
				bool num70 = num11 >= reference8.HitLeft && reference8.HitRight >= num10;
				bool flag31 = num9 >= reference8.HitTop && reference8.HitBottom >= num8;
				if (num70 && flag31)
				{
					s.ProcessedSprites.Add(reference8.Index);
				}
			}
			continue;
			IL_0e2b:
			if (num49 == 0 && !s.GravFlipped)
			{
				flag17 = false;
			}
			goto IL_0e38;
			IL_0e38:
			if (flag17)
			{
				ApplyPadSprite(ref s, spriteId4);
				orbHitThisFrame = true;
			}
			if (IsBluePad(spriteId4) && !s.DualActive)
			{
				s.ProcessedSprites.Add(reference8.Index);
			}
		}
		return false;
	}

	private void CheckGravityPortalsPostY(ref SimState s, int prevX_px)
	{
		int num = s.Y_fixed >> 8;
		int hitboxW = GetHitboxW(s.Mini);
		int num2 = (s.X_fixed >> 8) + 1 + hitboxW / 2 - 7;
		int num3 = num2 + 14;
		int num4 = num;
		if (s.Mini && !s.GravFlipped)
		{
			num4 += 9;
		}
		int num5 = num4 + 14;
		for (int i = SpriteLowerBound(prevX_px - 64); i < _spritesArr.Length; i++)
		{
			ref SpriteEntry reference = ref _spritesArr[i];
			if (s.ProcessedSprites.Contains(reference.Index) || reference.HitRight <= prevX_px)
			{
				continue;
			}
			if (reference.AnchorX_px - 16 > num3 + 16)
			{
				break;
			}
			int spriteId = reference.SpriteId;
			if (IsGravityPortal(spriteId))
			{
				bool num6 = num3 >= reference.HitLeft && reference.HitRight >= num2;
				bool flag = num5 >= reference.HitTop && reference.HitBottom >= num4;
				if (num6 && flag && ApplyPortalSprite(ref s, spriteId))
				{
					s.ProcessedSprites.Add(reference.Index);
				}
			}
		}
	}

	private void CheckGameModePortalsAtNewX(ref SimState s)
	{
		int num = s.X_fixed >> 8;
		int num2;
		int num3;
		if (s.GameMode != 6)
		{
			num2 = ((s.GameMode == 10) ? 1 : 0);
			if (num2 == 0)
			{
				num3 = (s.Mini ? 8 : 15);
				goto IL_0032;
			}
		}
		else
		{
			num2 = 1;
		}
		num3 = 8;
		goto IL_0032;
		IL_0032:
		int num4 = num3;
		int num5 = ((num2 != 0) ? 8 : (s.Mini ? 7 : 15));
		int num6 = ((num2 != 0) ? 4 : SharedPhysics.GetHitboxOffsetY(s.GameMode, s.Mini, s.GravFlipped));
		int num7 = num + 1;
		int num8 = num7 + num4 - 1;
		int num9 = (s.Y_fixed >> 8) + num6;
		int num10 = num9 + num5 - 1;
		for (int i = SpriteLowerBound(num - 64); i < _spritesArr.Length; i++)
		{
			ref SpriteEntry reference = ref _spritesArr[i];
			if (s.ProcessedSprites.Contains(reference.Index) || reference.HitRight <= num7)
			{
				continue;
			}
			if (reference.AnchorX_px - 16 > num8 + 16)
			{
				break;
			}
			int spriteId = reference.SpriteId;
			if (!IsGameModePortal(spriteId) && !IsGravityPortal(spriteId))
			{
				continue;
			}
			bool num11 = num8 + 1 >= reference.HitLeft && reference.HitRight >= num7;
			bool flag = num10 + 1 >= reference.HitTop && reference.HitBottom >= num9;
			if (num11 && flag)
			{
				bool flag2 = ApplyPortalSprite(ref s, spriteId);
				if (flag2)
				{
					s.ProcessedSprites.Add(reference.Index);
				}
				if (IsGameModePortal(spriteId) && flag2 && s.GameMode != 0 && s.GameMode != 4 && s.GameMode != 8 && s.GameMode != 9 && !s.DualActive)
				{
					int portalWorldY_px = reference.AnchorY_px - 8;
					s.TargetCameraY_fixed = NesNtCameraTarget_fixed(portalWorldY_px);
				}
				break;
			}
		}
	}

	private void CheckSpeedPortalsAtNewX(ref SimState s)
	{
		int num = s.X_fixed + 12288;
		for (int i = SpriteLowerBound((s.X_fixed >> 8) - 64); i < _spritesArr.Length; i++)
		{
			ref SpriteEntry reference = ref _spritesArr[i];
			if (s.ProcessedSprites.Contains(reference.Index))
			{
				continue;
			}
			int spriteId = reference.SpriteId;
			if (IsSpeedPortal(spriteId) && reference.AnchorX_px << 8 <= num)
			{
				int num2 = SpriteIdToSpeedFixed(spriteId);
				if (num2 > 0)
				{
					s.VelX_fixed = num2;
				}
				s.ProcessedSprites.Add(reference.Index);
				break;
			}
		}
	}

	private void CheckGravityModTriggersAtNewX(ref SimState s)
	{
		int num = s.X_fixed + 5888;
		for (int i = SpriteLowerBound((s.X_fixed >> 8) - 64); i < _spritesArr.Length; i++)
		{
			ref SpriteEntry reference = ref _spritesArr[i];
			if (s.ProcessedSprites.Contains(reference.Index))
			{
				continue;
			}
			int spriteId = reference.SpriteId;
			if (spriteId >= 112 && spriteId <= 116 && reference.AnchorX_px << 8 <= num)
			{
				double gravityMod = 1.0;
				switch (spriteId)
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
				s.GravityMod = gravityMod;
				s.ProcessedSprites.Add(reference.Index);
			}
		}
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
				_ = _speculativeDepth;
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
				s.GameMode = num;
				if (num == 0 || num == 4)
				{
					s.ExitPortalTimer = 10;
				}
				s.RainbowMaxMode = 0;
				bool flag = gameMode == 6 || gameMode == 7;
				switch (num)
				{
				case 1:
				case 2:
				case 3:
					s.VelY_fixed >>= 1;
					break;
				case 4:
					if (flag)
					{
						s.VelY_fixed >>= 1;
					}
					break;
				case 0:
				case 5:
				case 8:
				case 9:
				case 10:
					if (flag)
					{
						s.VelY_fixed = 0;
					}
					break;
				}
				s.WasZeroedByCollision = false;
				s.SlopeWasOnCounter = 0;
				s.SlopeFrames = 0;
				s.SlopeType = 0;
				s.LastSlopeType = 0;
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
			if (num5 > 0)
			{
				s.VelX_fixed = num5;
			}
			return true;
		}
		if (IsGravityPortal(sid))
		{
			bool flag2 = IsReverseGravity(sid);
			if (flag2 && !s.GravFlipped)
			{
				s.GravFlipped = true;
				s.GravMul = -1;
				s.VelY_fixed >>= 1;
				s.WasZeroedByCollision = false;
				return true;
			}
			if (!flag2 && s.GravFlipped)
			{
				s.GravFlipped = false;
				s.GravMul = 1;
				s.VelY_fixed >>= 1;
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
		int num = ((s.OrbUseFrameStartGravitySign ? s.GravFlippedAtFrameStart : s.GravFlipped) ? 1 : (-1));
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
					s.P2_VelY_fixed >>= 1;
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
					s.P2_VelY_fixed >>= 1;
				}
			}
			int cpTableIdx = 4 | (s.Mini ? 2 : 0) | (s.GravFlipped ? 1 : 0);
			int tableOffset = ((sid == 124) ? 1 : 0);
			s.VelY_fixed = SharedPhysics.SpriteGamemodeYAdjust(cpTableIdx, s.GameMode, tableOffset);
			s.OnGround = false;
		}
		else if (IsWhiteOrb(sid))
		{
			s.VelY_fixed = 0;
		}
	}

	private void ApplyDashOrb(ref SimState s, int sid)
	{
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
	}

	private void SnapCameraToPlayerY(ref SimState s)
	{
		int num = -(groundRowsToReserve * 16) << 8;
		int num2 = NesMaxCamY_px() << 8;
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
			s.Y_fixed = (num3 << 8) | (s.Y_fixed & 0xFF);
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

	private (bool hit, int ceilingBottomY, bool spikeDeath, MetatileCollision hitCollision) CheckCeiling(int collX, int collY, int collW, int collH)
	{
		return SharedPhysics.CheckCeiling(in _collisionMap, collX, collY, collW, collH);
	}

	private bool CheckCenterPointDeath(ref SimState s)
	{
		int playerX_px = s.X_fixed >> 8;
		int playerY_px = s.Y_fixed >> 8;
		return SharedPhysics.CheckCenterPointDeath(in _collisionMap, playerX_px, playerY_px, GetHitboxW(s.Mini), GetHitboxH(s.Mini), GetHitboxOffsetY(s.GameMode, s.Mini, s.GravFlipped));
	}

	private bool PointKillsPlayer(int px, int py)
	{
		return SharedPhysics.PointKillsPlayer(in _collisionMap, px, py);
	}

	private bool CheckFloorSpikes(ref SimState s)
	{
		int num = s.X_fixed >> 8;
		int num2 = NesPlayerY_px(s.Y_fixed, s.CameraY_fixed);
		int hbW;
		int hbH;
		if (s.GameMode == 6 || s.GameMode == 10)
		{
			hbW = 8;
			hbH = 8;
		}
		else
		{
			hbW = GetHitboxW(s.Mini);
			hbH = GetHitboxH(s.Mini);
		}
		int deathX;
		int deathY;
		string cornerName;
		int dbg_tid;
		int dbg_mappedTid;
		MetatileCollision dbg_col;
		int dbg_localX;
		int dbg_localY;
		bool num3 = SharedPhysics.CheckFloorSpikes(in _collisionMap, num, num2, hbW, hbH, s.Mini, out deathX, out deathY, out cornerName, out dbg_tid, out dbg_mappedTid, out dbg_col, out dbg_localX, out dbg_localY);
		if (num3)
		{
			Console.Error.WriteLine($"[DBG_SPIKE] pX={num} pY={num2} grav={s.GravFlipped} dualP2={_dualP2Guard} corner={cornerName} pt=({deathX},{deathY}) tid=0x{dbg_tid:X2} col={dbg_col} local=({dbg_localX},{dbg_localY})");
		}
		return num3;
	}

	private bool CheckDeathCollision(ref SimState s)
	{
		int playerX_px = s.X_fixed >> 8;
		int playerY_px = NesPlayerY_px(s.Y_fixed, s.CameraY_fixed);
		int hbW;
		int hbH;
		int hbOffY;
		if (s.GameMode == 6 || s.GameMode == 10)
		{
			hbW = 8;
			hbH = 8;
			hbOffY = (s.Mini ? 4 : 0);
		}
		else
		{
			hbW = GetHitboxW(s.Mini);
			hbH = GetHitboxH(s.Mini);
			hbOffY = GetHitboxOffsetY(s.GameMode, s.Mini, s.GravFlipped);
		}
		return SharedPhysics.CheckDeathCollision(in _collisionMap, playerX_px, playerY_px, hbW, hbH, hbOffY, s.GameMode, s.Dblocked);
	}

	private bool CheckSlopePenetrationDeath(ref SimState s)
	{
		int hitboxW = GetHitboxW(s.Mini);
		int hitboxH = GetHitboxH(s.Mini);
		int hitboxOffsetY = GetHitboxOffsetY(s.GameMode, s.Mini, s.GravFlipped);
		int num = (s.X_fixed >> 8) + (hitboxW >> 1) - 1;
		int num2 = NesPlayerY_px(s.Y_fixed, s.CameraY_fixed) + hitboxH / 2 + hitboxOffsetY;
		int tileX = num / 16;
		int tileY = num2 / 16;
		MetatileCollision tileCollision = GetTileCollision(tileX, tileY);
		if (tileCollision >= MetatileCollision.COL_SLOPE_RD45 && tileCollision <= MetatileCollision.COL_SLOPE_LU66_TOP && PfSlopeCalc(num, num2, tileCollision).hit)
		{
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
		int num2 = NesPlayerY_px(s.Y_fixed, s.CameraY_fixed);
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
				MetatileCollision collision = MetatileCollisionTable.GetCollision((byte)MapTileForCollision(_collisionMap.Tiles[num10 * _collisionMap.MapWidth + num8]));
				if (collision == MetatileCollision.COL_FLOOR_CEIL || collision == MetatileCollision.COL_NO_SIDE)
				{
					int num11 = num8 * 16;
					int num12 = num9 * 16;
					int localX = Math.Max(0, Math.Min(15, num5 - num11));
					int localY = Math.Max(0, Math.Min(15, num7 - num12));
					if (SharedPhysics.TileOccupiesPixel(collision, localX, localY))
					{
						flag = true;
					}
				}
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

	private void PfSlopeDiag(ref SimState s, string tag)
	{
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
