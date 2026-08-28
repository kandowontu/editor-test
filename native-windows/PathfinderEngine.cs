#nullable enable
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FamidashEditor;

/// <summary>
/// Authoritative post-step state produced by PathfinderEngine's NES-accurate
/// replay.  The native simulator consumes these frames when playing a calculated
/// path instead of re-running its separate physics implementation.
/// </summary>
public sealed class PathfinderReplayFrame
{
	public int FrameIndex { get; init; }
	public bool InputHeld { get; init; }
	public sbyte HorizontalDirection { get; init; }
	public int XFixed { get; init; }
	public int YFixed { get; init; }
	public int VelXFixed { get; init; }
	public int VelYFixed { get; init; }
	public int GlobalSpeedFixed { get; init; }
	public int ScrollXPx { get; init; }
	public int CurrXScrollStopFixed { get; init; }
	public int TargetXScrollStopFixed { get; init; }
	public int CameraYFixed { get; init; }
	public int TargetCameraYFixed { get; init; }
	public int ScrollYSubpx { get; init; }
	public int GameMode { get; init; }
	public bool Mini { get; init; }
	public bool GravityFlipped { get; init; }
	public bool WasZeroedByCollision { get; init; }
	public bool OnGround { get; init; }
	public double GravityMultiplier { get; init; }
	public int Dashing { get; init; }
	public int NinjaJumps { get; init; }
	public int RobotJumpTime { get; init; }
	public int SlopeWasOnCounter { get; init; }
	public int SlopeFrames { get; init; }
	public int SlopeType { get; init; }
	public int LastSlopeType { get; init; }
	public byte InvincibleCounter { get; init; }
	public bool NoCamLockForced { get; init; }
	public bool WrapMode { get; init; }
	public bool SlowMode { get; init; }
	public bool PlayerInvisible { get; init; }
	public int ForcedTrails { get; init; }
	public byte ExitPortalTimer { get; init; }
	public bool DualActive { get; init; }
	public int P2YFixed { get; init; }
	public int P2VelXFixed { get; init; }
	public int P2VelYFixed { get; init; }
	public bool P2Mini { get; init; }
	public bool P2GravityFlipped { get; init; }
	public bool P2WasZeroedByCollision { get; init; }
	public bool P2OnGround { get; init; }
	public int P2Dashing { get; init; }
	public int P2NinjaJumps { get; init; }
	public int P2RobotJumpTime { get; init; }
	public int P2SlopeWasOnCounter { get; init; }
	public int P2SlopeFrames { get; init; }
	public int P2SlopeType { get; init; }
	public int P2LastSlopeType { get; init; }
	public bool Alive { get; init; }
	public bool EndLevel { get; init; }
	public byte DeathType { get; init; }
	public int DeathX { get; init; } = -1;
	public int DeathY { get; init; } = -1;
	public int[] NewlyCollectedCoinIndices { get; init; } = Array.Empty<int>();
	public int BackgroundColorTriggerIndex { get; init; } = -1;
	public int BackgroundColorTriggerSpriteId { get; init; } = -1;
	public int ObjectColorTriggerIndex { get; init; } = -1;
	public int ObjectColorTriggerSpriteId { get; init; } = -1;
	public int GroundColorTriggerIndex { get; init; } = -1;
	public int GroundColorTriggerSpriteId { get; init; } = -1;
}

public class PathfinderEngine
{
	/// <summary>
	/// A tiny exact-length pool for the fixed NES runtime arrays copied by every
	/// search branch. ArrayPool may return a larger array, while several parity
	/// loops intentionally use Length; this pool therefore guarantees that the
	/// observable array length remains exactly 16 or 3.
	/// </summary>
	private sealed class ExactArrayPool<T>
	{
		private readonly ConcurrentBag<T[]> _items = new();
		private readonly int _length;
		private readonly int _maxRetained;
		private int _retained;

		public ExactArrayPool(int length, int maxRetained)
		{
			_length = length;
			_maxRetained = maxRetained;
		}

		public T[] Rent(bool clear)
		{
			if (!_items.TryTake(out T[]? array))
				array = new T[_length];
			else
				Interlocked.Decrement(ref _retained);
			if (clear)
				Array.Clear(array, 0, array.Length);
			return array;
		}

		public T[] RentCopy(T[] source)
		{
			T[] copy = Rent(clear: false);
			Array.Copy(source, 0, copy, 0, _length);
			return copy;
		}

		public void Return(T[]? array)
		{
			if (array == null || array.Length != _length)
				return;
			if (Interlocked.Increment(ref _retained) <= _maxRetained)
			{
				_items.Add(array);
				return;
			}
			Interlocked.Decrement(ref _retained);
		}
	}

	// A coin BFS state owns four int[16], two bool[16], and two int[3] arrays.
	// Retain enough returned bundles to feed the next 120k-state generation;
	// otherwise later frames resume allocating hundreds of thousands of tiny
	// arrays. Every child still owns an independent mutable copy.
	private static readonly ExactArrayPool<int> s_nesInt16Pool = new(16, 1_200_000);
	private static readonly ExactArrayPool<bool> s_nesBool16Pool = new(16, 600_000);
	private static readonly ExactArrayPool<int> s_coinInt3Pool = new(3, 600_000);

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

		public int ProcessKey;

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

		// PF keeps X in world coordinates. Platformer mode cannot derive the
		// horizontal camera from world X because the player can stop and walk
		// left, so preserve the NES scroll state explicitly.
		public int ScrollX_px;

		public int CurrXScrollStop_fixed;

		public int TargetXScrollStop_fixed;

		// Search provenance only. Platformer BFS includes a coarse form of this in
		// its search key so bounded pruning cannot erase a committed retreat route;
		// it is not part of NES physics/runtime identity.
		public sbyte SearchDirectionUsed;

		// Consecutive platformer movement in SearchDirectionUsed. This is also
		// search provenance, used only to keep committed retreat routes alive when
		// the bounded frontier is grouped into coarse spatial buckets.
		public byte SearchDirectionRun;

		public int Y_fixed;

		public int VelY_fixed;

		public int VelX_fixed;

		// NES speed is a global selector loaded by x_movement after each
		// player's Y movement. It is distinct from the saved per-player X
		// velocity, which is observable for one frame after a P2 speed hit.
		public int GlobalSpeed_fixed;

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

		// NES cube_data bit 2 (value $04): a robot press stays queued while
		// airborne and is consumed when robot eject reaches a surface.
		public bool RobotJumpRequested;

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

		public bool UfoOrbed;

		public bool BlackOrbed;

		public bool AirPressLatch;

		public bool PrevInputHeld;

		public bool JBlocked;

		public bool FBlocked;

		public bool HBlocked;

		public bool Dblocked;

		public bool Step2Ejected;

		public bool Step2Ever;

		// Search-only provenance for a bounded set of spider trajectories that
		// would otherwise disappear behind a large tied-score frontier.
		public bool BfsSpiderReserved;

		// Search-only jump/input timing preference. This never participates in NES
		// physics or state deduplication: physically equivalent states remain
		// equivalent, while the lower-cost history is retained. OpportunityAge is
		// the length of the current local decision window or continuous-control
		// deviation. LocalCost is deliberately not accumulated across unrelated
		// actions, so the requested bias resumes immediately after a forced segment.
		public int BfsTimingLocalCost;

		public ushort BfsTimingOpportunityAge;

		public byte BfsTimingTrackedModePlusOne;

		public bool BfsTimingOpportunityOpen;

		public int SlopeWasOnCounter;

		public int SlopeFrames;

		public int SlopeType;

		public bool SlopeJumpHigher;

		public int LastSlopeType;

		// Persistent NES collision globals. Spider orb/pad handlers intentionally
		// consume the last values written by bg_coll_U/bg_coll_D.
		public byte EjectU;

		public byte EjectD;

		// NES reset_level sets this to 8. While non-zero, runthecolls skips
		// x_movement_coll and bg_coll_death, then decrements it after P1.
		public byte InvincibleCounter;

		public byte DeathType;

		public int CameraY_fixed;

		public int ScrollYSubpx;

		public int TargetCameraY_fixed;

		public byte ExitPortalTimer;

		public bool NoCamLockForced;

		public bool WrapMode;

		public bool SlowMode;

		public bool PlayerInvisible;

		public int ForcedTrails;

		public int RainbowMaxMode;

		// BFS-only lifetime of a universal random-portal section.  The NES may
		// place several copies of the deterministic exit portal at the same X so
		// each random mode can reach it.  Keep the source and the exact exit
		// cluster separate from ProcessedSprites: different copies are distinct
		// NES sprites, but are the same universal-section exit.
		public bool RainbowUniversalActive;

		public int RainbowSourcePortalX_px;

		public int RainbowExitPortalX_px;

		public int RainbowExitTargetModePlusOne;

		public int RainbowExitPortalProcessKeyPlusOne;

		public int RainbowExitPlayerX_fixed;

		public int RainbowExitPlayerY_fixed;

		public int RainbowExitVelX_fixed;

		public int RainbowExitVelY_fixed;

		public int RainbowExitStateFlags;

		public int RainbowExitDashing;

		// BFS-only random-portal bookkeeping.  A forced value is encoded as
		// mode+1 so the default zero-initialized state means "canonical mode 0".
		// RainbowPortalModeCount is an output event from the current StepFrame.
		public int RainbowForcedModePlusOne;

		public int RainbowPortalModeCount;

		public bool RainbowBranchEnded;

		// Search diagnostics carried only by a rejected universal candidate.
		// Mode is stored as mode+1 so a normal/live state remains zero-initialized.
		public int RainbowFailureModePlusOne;

		public int RainbowFailureX_px;

		public int RainbowFailureY_px;

		public SimState[]? RainbowShadows;

		public bool DualActive;

		public int P2_Y_fixed;

		public int P2_VelX_fixed;

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

		public bool P2_RobotJumpRequested;

		public int P2_NinjaJumps;

		public int P2_SlopeWasOnCounter;

		public int P2_SlopeFrames;

		public int P2_SlopeType;

		public int P2_LastSlopeType;

		public bool P2_Orbed;

		public bool P2_UfoOrbed;

		public bool P2_BlackOrbed;

		public bool P2_AirPressLatch;

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

		// Transient snapshot made by the NES single-player portal handler while
		// currplayer is P2.  The handler copies P2's state to player[0] at its
		// exact sprite slot, before the remainder of P2's movement runs.
		public bool P2_SingleExitCaptured;

		public int P2_SingleExitY_fixed;

		public int P2_SingleExitVelY_fixed;

		public bool P2_SingleExitGravFlipped;

		public int P2_SingleExitGravMul;

		// NES 16-slot sprite table (runtime, per-frame updated)
		public int[] NesSlots;       // [16] index into _nesSpritesArr, or -1
		public bool[] NesSlotDead;   // [16] true if sprite type was changed to $FF
		public bool[] NesSlotActive; // [16] activesprites_active, set by check_spr_objects
		public int[] NesSlotWorldY;  // [16] mutable activesprites_y world byte
		public int[] NesSlotRealX;   // [16] cached activesprites_realx screen byte
		public int[] NesSlotRealY;   // [16] cached activesprites_realy screen byte
		public int NesSprDataPtr;    // next index in _nesStreamOrder
		public int TeleportOutputY_px;
		public int[] CoinTimer;      // coin1/coin2/coin3 shared animation timers
		public int[] CoinSpeed;      // coin1/coin2/coin3 shared 8.8 speeds
		public bool CoinAnimating;

		// Visual side effects consumed by the exact NES slot pass this frame.
		// These do not affect search-state equivalence; canonical simulator
		// playback uses them to reproduce background/object/ground colors.
		public int ReplayBackgroundColorTriggerIndex;
		public int ReplayBackgroundColorTriggerSpriteId;
		public int ReplayObjectColorTriggerIndex;
		public int ReplayObjectColorTriggerSpriteId;
		public int ReplayGroundColorTriggerIndex;
		public int ReplayGroundColorTriggerSpriteId;

		public SimState CloneBranch()
		{
			SimState result = this;
			result.RainbowShadows = null;
			result.ProcessedSprites = ProcessedSprites.Clone();
			if (NesSlots != null)
			{
				result.NesSlots = s_nesInt16Pool.RentCopy(NesSlots);
				result.NesSlotDead = s_nesBool16Pool.RentCopy(NesSlotDead);
				result.NesSlotActive = s_nesBool16Pool.RentCopy(NesSlotActive);
				result.NesSlotWorldY = s_nesInt16Pool.RentCopy(NesSlotWorldY);
				result.NesSlotRealX = s_nesInt16Pool.RentCopy(NesSlotRealX);
				result.NesSlotRealY = s_nesInt16Pool.RentCopy(NesSlotRealY);
			}
			if (CoinTimer != null) result.CoinTimer = s_coinInt3Pool.RentCopy(CoinTimer);
			if (CoinSpeed != null) result.CoinSpeed = s_coinInt3Pool.RentCopy(CoinSpeed);
			return result;
		}

		public SimState Clone()
		{
			SimState result = CloneBranch();
			if (RainbowShadows != null)
			{
				result.RainbowShadows = new SimState[RainbowShadows.Length];
				for (int i = 0; i < RainbowShadows.Length; i++)
				{
					result.RainbowShadows[i] = RainbowShadows[i].CloneBranch();
				}
			}
			return result;
		}

		public void ReturnAllSpriteResources()
		{
			ProcessedSprites.Return();
			if (NesSlots != null)
			{
				s_nesInt16Pool.Return(NesSlots);
				s_nesBool16Pool.Return(NesSlotDead);
				s_nesBool16Pool.Return(NesSlotActive);
				s_nesInt16Pool.Return(NesSlotWorldY);
				s_nesInt16Pool.Return(NesSlotRealX);
				s_nesInt16Pool.Return(NesSlotRealY);
				NesSlots = null!;
				NesSlotDead = null!;
				NesSlotActive = null!;
				NesSlotWorldY = null!;
				NesSlotRealX = null!;
				NesSlotRealY = null!;
			}
			if (CoinTimer != null)
			{
				s_coinInt3Pool.Return(CoinTimer);
				CoinTimer = null!;
			}
			if (CoinSpeed != null)
			{
				s_coinInt3Pool.Return(CoinSpeed);
				CoinSpeed = null!;
			}
			if (RainbowShadows != null)
			{
				for (int i = 0; i < RainbowShadows.Length; i++)
				{
					RainbowShadows[i].ReturnAllSpriteResources();
				}
				RainbowShadows = null;
			}
		}
	}

	private readonly record struct BfsDedupKey(
		long Physics,
		int XFixed,
		int ScrollXPx,
		int CurrXScrollStopFixed,
		int TargetXScrollStopFixed,
		int CameraYFixed,
		int TargetCameraYFixed,
		int ScrollYSubpx,
		int NesSprDataPtr,
		int SearchRoute,
		long RuntimeHash);

	private readonly record struct BfsTransitionPortal(
		int X,
		int Y,
		int ProcessKey,
		int SpriteId,
		int TargetMode,
		int HitCenterY);

	private readonly record struct BfsRainbowPortal(
		int X,
		int ProcessKey,
		int ModeCount);

	private sealed class BfsRouteArchive
	{
		public int Frame;
		public int SourceX;
		public int SplitY;
		public bool IsInteriorBand;
		public int TargetX;
		public int TargetY;
		public List<SimState> States = new();
		public int[] Parents = Array.Empty<int>();
		public bool[] Inputs = Array.Empty<bool>();
		public sbyte[] Directions = Array.Empty<sbyte>();

		public void ReturnResources()
		{
			foreach (SimState state in States)
				state.ReturnAllSpriteResources();
			States.Clear();
		}
	}

	private sealed class SpriteSet
	{
		internal readonly ulong[] Bits;

		private readonly int _logicalLen;

		private readonly int[] _map;

		private readonly bool _rented;

		private int _bitsHash;

		private bool _bitsHashValid;

		private long _bitsHash64;

		private bool _bitsHash64Valid;

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
			_bitsHash = 0;
			_bitsHashValid = true;
			_bitsHash64 = 0;
			_bitsHash64Valid = false;
		}

		private SpriteSet(ulong[] srcBits, int logicalLen, int[] compactMap,
			int bitsHash, bool bitsHashValid, long bitsHash64, bool bitsHash64Valid)
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
			_bitsHash = bitsHash;
			_bitsHashValid = bitsHashValid;
			_bitsHash64 = bitsHash64;
			_bitsHash64Valid = bitsHash64Valid;
		}

		public void Add(int tileIndex)
		{
			int num = _map[tileIndex];
			ulong mask = (ulong)(1L << num);
			int word = num >> 6;
			if ((Bits[word] & mask) == 0)
			{
				Bits[word] |= mask;
				_bitsHashValid = false;
				_bitsHash64Valid = false;
			}
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
				ulong mask = (ulong)(1L << num);
				int word = num >> 6;
				if ((Bits[word] & mask) != 0)
				{
					Bits[word] &= ~mask;
					_bitsHashValid = false;
					_bitsHash64Valid = false;
				}
			}
		}

		public SpriteSet Clone()
		{
			return new SpriteSet(Bits, _logicalLen, _map, _bitsHash, _bitsHashValid,
				_bitsHash64, _bitsHash64Valid);
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
			if (_bitsHashValid)
			{
				return _bitsHash;
			}
			int num = 0;
			for (int i = 0; i < _logicalLen; i++)
			{
				ulong num2 = Bits[i];
				num = (num * 397) ^ (int)num2 ^ (int)(num2 >> 32);
			}
			_bitsHash = num;
			_bitsHashValid = true;
			return _bitsHash;
		}

		public long GetBitsHash64()
		{
			if (_bitsHash64Valid)
			{
				return _bitsHash64;
			}
			ulong hash = 14695981039346656037UL;
			for (int i = 0; i < _logicalLen; i++)
			{
				hash ^= Bits[i];
				hash *= 1099511628211UL;
			}
			_bitsHash64 = unchecked((long)hash);
			_bitsHash64Valid = true;
			return _bitsHash64;
		}

	}

	private const int TILE = 16;

	private const int NES_H = 15;

	private const int SCREEN_H_PX = 240;

	private const int SHIP_SCROLL_SPEED_FIXED = 0x0266;

	private const int PORTAL_TO_TOP_DIFF_PX = 0x5A;

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

	private sealed class TeeTextWriter : TextWriter
	{
		private readonly TextWriter[] _writers;

		public override Encoding Encoding => Encoding.UTF8;

		public TeeTextWriter(params TextWriter[] writers)
		{
			_writers = writers.Where(static w => w != null).ToArray();
		}

		public override void WriteLine(string? value)
		{
			for (int i = 0; i < _writers.Length; i++)
			{
				_writers[i].WriteLine(value);
			}
		}

		public override void Flush()
		{
			for (int i = 0; i < _writers.Length; i++)
			{
				_writers[i].Flush();
			}
		}
	}

	private sealed class PrefixTextWriter : TextWriter
	{
		private readonly TextWriter _inner;
		private readonly Func<string> _prefixFactory;

		public override Encoding Encoding => _inner.Encoding;

		public PrefixTextWriter(TextWriter inner, Func<string> prefixFactory)
		{
			_inner = inner;
			_prefixFactory = prefixFactory;
		}

		public override void WriteLine(string? value)
		{
			_inner.WriteLine(_prefixFactory() + (value ?? string.Empty));
		}

		public override void Flush()
		{
			_inner.Flush();
		}
	}

	private TextWriter _baseLog = TextWriter.Null;
	private TextWriter _log = TextWriter.Null;

#if !DISABLE_DEBUG_LOGGING
	private string _pfDebugLogPath = string.Empty;
	private string _frameTracePath = string.Empty;
	private string _orbDebugLogPath = string.Empty;
	private string _fullTracePath = string.Empty;
	private StreamWriter? _traceWriter;
	private StreamWriter? _pfDebugWriter;
	private StreamWriter? _orbDebugWriter;
	private StreamWriter? _fullTraceWriter;
	private bool _artifactWritersOpen;
#endif

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

	private bool _stepFrameInputHeld;

	private bool _bfsSearchActive;

	private int _frameCounter;

	[ThreadStatic]
	private static bool _dualP2Guard;

	// NES process_x_scroll decrements high_byte(player1_x) by the scroll delta during
	// player 0's frame (scroll.h), so player 1's sprite_collide runs at a screen X that
	// lags player 0 by tmp1 px (its own x_movement only catches up afterward). The P2
	// sub-step's sprite collision must use the post-P1-movement scroll (the same scroll
	// CheckSprObjects used to cache NesSlotRealX) so the player box and sprite boxes agree.
	[ThreadStatic]
	private static bool _dualP2ScrollXValid;

	[ThreadStatic]
	private static int _dualP2ScrollX_px;

	[ThreadStatic]
	private static bool _dualActivatedThisProcessSprites;

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

	private readonly List<BfsTransitionPortal> _bfsTransitionPortals;

	private readonly List<BfsRainbowPortal> _bfsRainbowPortals;

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

	// Exact NES-exported sprite records, kept separate from the editor's
	// collision-resolved sprite grid.
	private SpriteEntry[] _nesSpritesArr;
	// NES sprite data stream: indices into _nesSpritesArr, already in exporter column-major order.
	private int[] _nesStreamOrder;
	// World X for each _nesSpritesArr entry (used for NES off-screen-left check)
	private int[] _nesSpriteWorldX;

	private const int NES_DECO = 0xFE;
	private const int NES_COLR = 0xFD;
	private const int NES_OUTL = 0xFC;
	private const int NES_SPBH = 0xFF;
	private const int NES_PLAYER_SCREEN_X_PX = 0x50;

	// NES sprite_heights table — determines DECO/COLR/OUTL/SPBH behavior for slot management
	private static readonly int[] _nesSprH = {
		0x34, 0x34, 0x34, 0x34, 0x34, 0x12, 0x12, 0xFF, // 00-07
		0x28, 0x28, 0x03, 0x12, 0x03, 0x03, 0x03, 0xFF, // 08-0F
		0x0E, 0x0E, 0x0E, 0x0E, 0x24, 0x24, 0x24, 0x34, // 10-17
		0x34, 0x34, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x12, // 18-1F
		0x24, 0x24, 0x34, 0x34, 0x34, 0x03, 0x03, 0x12, // 20-27
		0x12, 0x12, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, // 28-2F
		0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, // 30-37
		0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, // 38-3F
		0xFE, 0xFE, 0xFE, 0xFE, 0x12, 0x12, 0x12, 0x28, // 40-47
		0x28, 0xFE, 0xFE, 0x34, 0x12, 0x12, 0x30, 0xFF, // 48-4F
		0x12, 0x12, 0x03, 0x03, 0x12, 0x12, 0x03, 0x03, // 50-57
		0x34, 0x10, 0xFF, 0x12, 0x12, 0x12, 0x12, 0x34, // 58-5F
		0x34, 0x34, 0x34, 0x34, 0x34, 0x02, 0x10, 0xFF, // 60-67
		0x10, 0xFF, 0x34, 0x34, 0x34, 0x20, 0x08, 0xFF, // 68-6F
		0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x10, 0xFF, 0x10, // 70-77
		0xFF, 0x12, 0x12, 0x12, 0x12, 0xFF, 0xFF, 0xFF, // 78-7F
		0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, // 80-87
		0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0x00, 0xFF, 0xFD, // 88-8F
		0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, // 90-97
		0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0x00, 0xFF, 0xFD, // 98-9F
		0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, // A0-A7
		0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0x00, 0xFD, 0xFC, // A8-AF
		0xFC, 0xFC, 0xFC, 0xFC, 0xFC, 0xFC, 0xFC, 0xFC, // B0-B7
		0xFC, 0xFC, 0xFC, 0xFC, 0xFC, 0xFC, 0xFC, 0xFC, // B8-BF
		0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, // C0-C7
		0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0x00, 0x00, 0xFD, // C8-CF
		0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, // D0-D7
		0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFF, 0xFF, 0xFF, // D8-DF
		0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, // E0-E7
		0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFF, 0xFF, 0xFF, // E8-EF
		0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x10, 0x10, // F0-F7
		0x10, 0x10, 0x1F, 0x10, 0x10, 0x03, 0x03, 0x00, // F8-FF
	};

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

	/// <summary>
	/// Run a narrow exact-state beam for a short time before exhaustive BFS. Only
	/// a canonically replayed completion is accepted; every failure falls through
	/// to the full search, so enabling this cannot remove a BFS-reachable path.
	/// </summary>
	public bool UseFastSearch { get; set; } = true;

	// Standalone-runner diagnostic: stop after the bounded exact pass instead of
	// starting exhaustive BFS. The editor never enables this.
	public bool FastSearchOnly { get; set; }

	/// <summary>
	/// Mirrors the NES force_platformer level flag. Platformer is not a new
	/// game mode: it replaces automatic X movement with left/neutral/right
	/// input while retaining the active cube/ship/etc. vertical mechanics.
	/// </summary>
	public bool ForcePlatformer { get; set; }

	// Standalone-runner diagnostic checkpoint. The normal editor never sets this.
	public IReadOnlyList<bool>? DebugBfsPrefixInputs { get; set; }

	public bool Verbose { get; set; }

	/// <summary>
	/// Controls all Pathfinder diagnostic output, including console diagnostics
	/// and the frame/physics/interaction artifacts written to %TEMP%.
	/// </summary>
	public bool EnableLogging { get; set; } = true;

	public Action<List<(int x, int y)>?, int, int, bool>? OnSpeculativePath { get; set; }

	public int CurrentSpeculativeVizMode { get; private set; } = -1;


	public int CurrentX_px => _currentX_px;

	public string DebugLogPath
	{
		get
		{
#if !DISABLE_DEBUG_LOGGING
			return _pfDebugLogPath;
#else
			return string.Empty;
#endif
		}
	}

	public string LevelName { get; set; } = "";


	public int? ConfigScrollYPosition { get; set; }

	public bool UseNesSpawnScrollDefaults { get; set; }

	// The compact PR #360 header no longer has a spawn-Y fractional byte.
	private int SpawnYSubpx => 0;

	private int InitialXFixed(int startX_px)
	{
		// reset_level.h uses $1110 for a real platformer start. START POS is an
		// editor diagnostic override and remains integer-aligned.
		return (startX_px << 8) |
			(ForcePlatformer && UseNesSpawnScrollDefaults ? 0x10 : 0);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int GetScrollX_px(in SimState s)
	{
		return ForcePlatformer
			? s.ScrollX_px
			: Math.Max(0, (s.X_fixed >> 8) - NES_PLAYER_SCREEN_X_PX);
	}

	private void InitializeHorizontalState(ref SimState s)
	{
		s.ScrollX_px = 0;
		s.CurrXScrollStop_fixed = 0x5000;
		s.TargetXScrollStop_fixed = 0x5000;
		s.SearchDirectionUsed = 0;
		s.SearchDirectionRun = 0;
	}

	private void ProcessPlatformerXScroll(ref SimState s)
	{
		if (!ForcePlatformer || _dualP2Guard)
			return;

		if (s.CurrXScrollStop_fixed < s.TargetXScrollStop_fixed)
			s.CurrXScrollStop_fixed += 0x200;
		else if (s.CurrXScrollStop_fixed > s.TargetXScrollStop_fixed)
			s.CurrXScrollStop_fixed -= 0x200;

		int screenXFixed = s.X_fixed - (s.ScrollX_px << 8);
		if (screenXFixed > s.CurrXScrollStop_fixed)
		{
			int delta = (screenXFixed - s.CurrXScrollStop_fixed) >> 8;
			s.ScrollX_px += delta;
		}
		else if (screenXFixed < 0x0200)
		{
			int delta = (screenXFixed + 0x0200) >> 8;
			s.ScrollX_px -= delta;
		}
	}

	private int ResolvePlatformerHorizontal(ref SimState s, sbyte direction,
		int movementSpeedFixed, out bool lethal)
	{
		lethal = false;
		direction = direction < 0 ? (sbyte)-1 : direction > 0 ? (sbyte)1 : (sbyte)0;
		s.SearchDirectionRun = direction != 0
			? (byte)(direction == s.SearchDirectionUsed
				? Math.Min(31, s.SearchDirectionRun + 1)
				: 1)
			: (byte)0;
		s.SearchDirectionUsed = direction;

		int hitboxW = (s.GameMode == 6 || s.GameMode == 10)
			? 8 : GetHitboxW(s.Mini);
		int hitboxH = (s.GameMode == 6 || s.GameMode == 10)
			? 8 : GetHitboxH(s.Mini);
		int playerX = s.X_fixed >> 8;
		int playerY = NesPlayerBgCollisionY_px(s.Y_fixed, s.CameraY_fixed);
		bool slopeActive = (s.SlopeWasOnCounter | s.SlopeFrames) != 0;

		// x_movement_coll performs one right probe before x_movement performs
		// both probes. Its ordinary wall result only zeroes the old velocity;
		// x_movement immediately reloads the global speed.
		if (s.InvincibleCounter == 0)
		{
			var pre = SharedPhysics.CheckPlatformerSideCollision(in _collisionMap,
				playerX, playerY, hitboxW, hitboxH, s.GameMode, s.Mini,
				s.GravFlipped, movingRight: true, slopeActive, s.Dblocked,
				s.SlopeType);
			lethal |= pre.lethal;
			if (pre.nudge != 0) s.Y_fixed += pre.nudge << 8;
			if (pre.slopeType != 0) s.SlopeType = pre.slopeType;
		}

		playerY = NesPlayerBgCollisionY_px(s.Y_fixed, s.CameraY_fixed);
		var right = SharedPhysics.CheckPlatformerSideCollision(in _collisionMap,
			playerX, playerY, hitboxW, hitboxH, s.GameMode, s.Mini,
			s.GravFlipped, movingRight: true, slopeActive, s.Dblocked,
			s.SlopeType);
		lethal |= right.lethal;
		if (right.nudge != 0) s.Y_fixed += right.nudge << 8;
		if (right.slopeType != 0) s.SlopeType = right.slopeType;

		playerY = NesPlayerBgCollisionY_px(s.Y_fixed, s.CameraY_fixed);
		var left = SharedPhysics.CheckPlatformerSideCollision(in _collisionMap,
			playerX, playerY, hitboxW, hitboxH, s.GameMode, s.Mini,
			s.GravFlipped, movingRight: false, slopeActive, s.Dblocked,
			s.SlopeType);
		lethal |= left.lethal;
		if (left.nudge != 0) s.Y_fixed += left.nudge << 8;
		if (left.slopeType != 0) s.SlopeType = left.slopeType;

		int cameraXFixed = s.ScrollX_px << 8;
		int oldScreenXFixed = s.X_fixed - cameraXFixed;
		int result = s.X_fixed;
		bool moved = false;
		if (direction > 0 && !right.blocked)
		{
			result += movementSpeedFixed;
			moved = true;
		}
		else if (direction < 0 && !left.blocked && oldScreenXFixed > 0x1200)
		{
			result -= movementSpeedFixed;
			moved = true;
		}

		if (direction > 0 && right.blocked)
		{
			int screenHigh = (s.X_fixed - (s.ScrollX_px << 8)) >> 8;
			int worldLow = (screenHigh + (s.ScrollX_px & 0xFF)) & 0xFF;
			int correction = ((worldLow + 4) & 7) - 4 + (s.Mini ? 1 : 0);
			result -= correction << 8;
		}
		else if (direction < 0 && left.blocked)
		{
			int screenHigh = (s.X_fixed - (s.ScrollX_px << 8)) >> 8;
			int worldLow = (screenHigh + (s.ScrollX_px & 0xFF)) & 0xFF;
			int correction = ((worldLow + 4) & 7) - 4;
			result -= correction << 8;
		}

		// currplayer_x is screen-relative on the NES. PF stores world X, so the
		// anti-wrap guard and its replacement value must be evaluated in screen
		// space and then translated back. Applying $F000 directly to world X made
		// every platformer wrap at the first 240 pixels.
		int resultScreenXFixed = result - cameraXFixed;
		if (resultScreenXFixed > 0xF000)
		{
			resultScreenXFixed = oldScreenXFixed >= 0xF000 ? 0xF000 : 0;
			result = cameraXFixed + resultScreenXFixed;
			moved = false;
		}
		s.VelX_fixed = moved ? movementSpeedFixed : 0;
		return result;
	}

	public string FrameTracePath
	{
		get
		{
#if !DISABLE_DEBUG_LOGGING
			return _frameTracePath;
#else
			return string.Empty;
#endif
		}
	}

	public string OrbDebugLogPath
	{
		get
		{
#if !DISABLE_DEBUG_LOGGING
			return _orbDebugLogPath;
#else
			return string.Empty;
#endif
		}
	}

	public List<(int x, int y)> PathPoints { get; private set; }

	public List<(int x, int y)> Path2Points { get; private set; }

	public List<bool> Inputs { get; private set; }

	/// <summary>-1 = left, 0 = neutral, +1 = right. Parallel to Inputs.</summary>
	public List<sbyte> HorizontalInputs { get; private set; }

	/// <summary>
	/// Exact post-step states for the final replay. These are deliberately
	/// separate from PathPoints, which only contain rounded display positions.
	/// </summary>
	public List<PathfinderReplayFrame> ReplayFrames { get; private set; } = new();

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
		return SharedPhysics.NesPlayerY_px(yFixed, camYFixed);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int NesPlayerBgCollisionY_px(int yFixed, int camYFixed)
	{
		return SharedPhysics.NesPlayerBgCollisionY_px(yFixed, camYFixed);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void WrapNesPlayerScreenY(ref SimState s)
	{
		// currplayer_y is a 16-bit screen-space fixed-point value on NES. Most
		// states never approach either end, but an opening spider boundary can be
		// allowed through state_game's X <= $20 death suppression. Its following
		// upward movement must then wrap $01.xx -> $FE.xx before process_y_scroll.
		int screenY_fixed = s.Y_fixed - s.CameraY_fixed;
		s.Y_fixed = s.CameraY_fixed + (screenY_fixed & 0xFFFF);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void ApplyNesPlayerYHighSubtract(ref SimState s, byte amount)
	{
		int screenY_fixed = s.Y_fixed - s.CameraY_fixed;
		int rawScreenY = screenY_fixed & 0xFFFF;
		int high = unchecked((byte)((rawScreenY >> 8) - amount));
		s.Y_fixed = s.CameraY_fixed + (high << 8) + (rawScreenY & 0xFF);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int NesSaturatingOffset(int coordinate, int signedOffset)
	{
		return Math.Clamp((coordinate & 0xFF) + signedOffset, 0, 0xFF);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool NesAxisOverlaps(int first, int firstSize, int second, int secondSize)
	{
		first &= 0xFF;
		second &= 0xFF;
		firstSize &= 0xFF;
		secondSize &= 0xFF;

		int firstEnd = first + firstSize;
		if (firstEnd <= 0xFF && firstEnd < second)
			return false;

		int secondEnd = second + secondSize;
		if (secondEnd <= 0xFF && secondEnd < first)
			return false;

		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool NesAxisOverlapsPositive(int first, int firstSize, int second, int secondSize)
	{
		first &= 0xFF;
		second &= 0xFF;
		firstSize &= 0xFF;
		secondSize &= 0xFF;

		int firstEnd = first + firstSize;
		if (firstEnd <= 0xFF && firstEnd <= second)
			return false;

		int secondEnd = second + secondSize;
		if (secondEnd <= 0xFF && secondEnd <= first)
			return false;

		return true;
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

	private static int NesGravityPortalHalfVelocity(int velY)
	{
		// NES/cc65 lowers this portal halve to an arithmetic shift in the
		// replayed path.  Odd negative velocities round down: -725 -> -363,
		// then ball gravity adds +71 to produce the Mesen-observed -292.
		return velY >> 1;
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

	private static bool SetsNesUfoOrbed(int sid)
	{
		return sid switch
		{
			0x05 or 0x06 or 0x0B or 0x1F or 0x27 or 0x28 or 0x29 or 0x44 or
			0x45 or 0x46 or 0x4C or 0x4D or 0x50 or 0x51 or
			0x5B or 0x5C or 0x5D or 0x5E or 0x7B or 0x7C => true,
			_ => false
		};
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

	private int GetNesFirstFrameSpeed(in SimState s)
	{
		// InitNesSlots deliberately leaves every active flag clear until the
		// first check_spr_objects pass. Probe a private copy of the slot table so
		// startup portals are visible here without advancing/replacing records in
		// the real simulation (Chromatic Expedition's speed portal at X=0).
		SimState probe = s;
		probe.NesSlots = (int[])s.NesSlots.Clone();
		probe.NesSlotDead = (bool[])s.NesSlotDead.Clone();
		probe.NesSlotActive = (bool[])s.NesSlotActive.Clone();
		probe.NesSlotWorldY = (int[])s.NesSlotWorldY.Clone();
		probe.NesSlotRealX = (int[])s.NesSlotRealX.Clone();
		probe.NesSlotRealY = (int[])s.NesSlotRealY.Clone();
		CheckSprObjects(ref probe);

		int effectiveSpeed = probe.GlobalSpeed_fixed > 0 ? probe.GlobalSpeed_fixed : probe.VelX_fixed;
		int playerY = SharedPhysics.NesPlayerScreenY_px(probe.Y_fixed, probe.CameraY_fixed);
		bool wave = probe.GameMode == 6;
		int hitboxW = wave ? 8 : GetHitboxW(probe.Mini);
		int hitboxH = wave ? 8 : GetHitboxH(probe.Mini);
		int plTop = playerY + (wave ? 4 : GetHitboxOffsetY(probe.GameMode, probe.Mini, probe.GravFlipped));
		int currentX = probe.X_fixed >> 8;
		int scrollX = GetScrollX_px(in probe);
		int plLeft = currentX - scrollX + 1;

		// sprite_collide visits the live slots in ascending slot order. Every
		// overlapping speed portal writes the shared speed selector, so the last
		// one in that order owns the first x_movement advance.
		for (int slot = 0; slot < 16; slot++)
		{
			int sprIdx = probe.NesSlots[slot];
			if (sprIdx < 0 || probe.NesSlotDead[slot] || !probe.NesSlotActive[slot])
				continue;
			int sid = _nesSpritesArr[sprIdx].SpriteId & 0xFF;
			if (!IsSpeedPortal(sid))
				continue;
			int sprLeft = NesSaturatingOffset(probe.NesSlotRealX[slot],
				sid < sprite_x_offset.Length ? sprite_x_offset[sid] : 0);
			int sprTop = NesSaturatingOffset(probe.NesSlotRealY[slot],
				sid < sprite_y_offset.Length ? sprite_y_offset[sid] : 0);
			int sprWidth = sid < sprite_widths.Length ? sprite_widths[sid] : 0;
			int sprHeight = sid < sprite_heights.Length ? sprite_heights[sid] : 0;
			if (NesAxisOverlaps(plLeft, hitboxW, sprLeft, sprWidth) &&
				NesAxisOverlaps(plTop, hitboxH, sprTop, sprHeight))
			{
				int speed = SpriteIdToSpeedFixed(sid);
				if (speed > 0)
					effectiveSpeed = speed;
			}
		}
		return effectiveSpeed;
	}

	private void ApplyNesIntroFreezePrestep(ref SimState s)
	{
		// Fresh non-practice starts receive eight protected gameplay ticks.
		s.InvincibleCounter = 8;
		if (s.DualActive)
		{
			return;
		}
		if (s.GameMode == 0 || s.GameMode == 4)
		{
			int num = SharedPhysics.GetCubeGravity(s.Mini);
			if (s.GravFlipped)
			{
				num = -num;
			}
			s.VelY_fixed += num;
			s.Y_fixed += s.VelY_fixed;
			if (!ForcePlatformer)
				s.X_fixed += s.VelX_fixed;
			ApplyNesCubeRobotYScroll(ref s);
			// The verified cube/robot intro-freeze pre-step is a complete
			// everything_else tick, including the NES counter decrement.
			s.InvincibleCounter--;
		}
		else if (s.GameMode == 5)
		{
			// The hidden final reset tick is not cube/robot-only.  A level that
			// starts as spider runs spider_movement once before the first recorded
			// sprite_collide pass.  This is observable when a pad is at X=0: NES
			// first applies one +gravity/X tick, then activates the pad on the next
			// tick.  Starting sprite processing immediately makes the pad affect Y
			// one frame early and corrupts every later camera subpixel carry.
			SpiderGravityStep(ref s);
			if (!ForcePlatformer)
				s.X_fixed += s.VelX_fixed;
			s.InvincibleCounter--;
		}
		else if (s.GameMode == 3 && !ForcePlatformer)
		{
			// The NES first recorded UFO tick advances X at the reset/default
			// speed (0x02C4), then uses the level's configured speed thereafter.
			// PF applies the configured start portal before its first tick, so keep
			// the logical frame count unchanged and restore the missing X phase.
			// Chromatic Expedition proves this directly: NES X is PF X + 0x89
			// for speed 0x023B, which changes the first RD45 probe by one pixel.
			int nesResetSpeed = SharedPhysics.SpeedUiIndexToFixed(1);
			int firstFrameSpeed = GetNesFirstFrameSpeed(in s);
			s.X_fixed += nesResetSpeed - firstFrameSpeed;
		}
		else if (s.GameMode == 6 || s.GameMode == 10)
		{
			// The first protected wave/snake tick starts with currplayer_vel_x=0,
			// then x_movement loads the configured speed and advances X. Keep this
			// hidden tick so the first searchable state has the NES X/Y phase.
			ProcessSpritesNesOrder(ref s, s.X_fixed >> 8, inputHeld: false,
				pressEdge: false, queuedPressAtFrameStart: false, out _, out _);

			s.VelX_fixed = 0;
			WaveEject(ref s, input: false, out _);
			int movementVelX = s.GlobalSpeed_fixed;
			if (!ForcePlatformer)
			{
				s.X_fixed += movementVelX;
				s.VelX_fixed = movementVelX;
			}
			else
			{
				s.VelX_fixed = 0;
			}
			s.InvincibleCounter--;
		}
	}

	private void ApplyNesCubeRobotYScroll(ref SimState s)
	{
		int minCameraY_fixed = (_minScrollYLin - _nesCoordOffset) << 8;
		int maxCameraY_fixed = NesMaxCamY_px() << 8;
		int screenY_fixed = NesNoCamTrackingScreenY_fixed(in s);
		int scrollY = (s.CameraY_fixed >> 8) + _nesCoordOffset;

		if (screenY_fixed < 0x4000 &&
			(scrollY > _minScrollYLin ||
			 (scrollY == _minScrollYLin && s.ScrollYSubpx != 0)))
		{
			int movement_fixed = 0x4000 - screenY_fixed;
			if (s.ExitPortalTimer != 0)
			{
				int maxStepPx = 11 - s.ExitPortalTimer;
				if ((movement_fixed >> 8) >= maxStepPx)
					movement_fixed = maxStepPx << 8;
			}
			int sub = s.ScrollYSubpx - (movement_fixed & 0xFF);
			int borrow = sub < 0 ? 1 : 0;
			s.ScrollYSubpx = sub & 0xFF;
			int cameraMove_fixed = -(((movement_fixed >> 8) + borrow) << 8);
			s.CameraY_fixed += cameraMove_fixed;
			s.Y_fixed += movement_fixed + cameraMove_fixed;
		}

		// scroll.h calls both cap routines unconditionally at their respective
		// positions, even when the anchor movement branch did not run.
		if (s.CameraY_fixed < minCameraY_fixed)
		{
			s.Y_fixed += s.ScrollYSubpx;
			s.ScrollYSubpx = 0;
			s.CameraY_fixed = minCameraY_fixed;
		}

		screenY_fixed = NesNoCamTrackingScreenY_fixed(in s);
		scrollY = (s.CameraY_fixed >> 8) + _nesCoordOffset;
		if (scrollY < SharedPhysics.NES_MAX_SCROLL_Y_LINEAR &&
			(screenY_fixed >> 8) >= 0xA0)
		{
			int movement_fixed = screenY_fixed - 0xA000;
			if (s.ExitPortalTimer != 0)
			{
				int maxStepPx = 11 - s.ExitPortalTimer;
				if ((movement_fixed >> 8) >= maxStepPx)
					movement_fixed = maxStepPx << 8;
			}
			int sub = s.ScrollYSubpx + (movement_fixed & 0xFF);
			int carry = sub > 0xFF ? 1 : 0;
			s.ScrollYSubpx = sub & 0xFF;
			int cameraMove_fixed = ((movement_fixed >> 8) + carry) << 8;
			s.CameraY_fixed += cameraMove_fixed;
			s.Y_fixed += -movement_fixed + cameraMove_fixed;
		}

		if (s.CameraY_fixed >= maxCameraY_fixed)
		{
			s.Y_fixed += s.ScrollYSubpx;
			s.ScrollYSubpx = 0;
			s.CameraY_fixed = maxCameraY_fixed;
		}
	}

	private void ApplyNesShipStyleYScroll(ref SimState s)
	{
		int cameraY_px = s.CameraY_fixed >> 8;
		int targetY_px = s.TargetCameraY_fixed >> 8;
		if (targetY_px > cameraY_px)
		{
			int sub = s.ScrollYSubpx + (SHIP_SCROLL_SPEED_FIXED & 0xFF);
			int carry = sub > 0xFF ? 1 : 0;
			s.ScrollYSubpx = sub & 0xFF;
			int cameraMove_fixed = ((SHIP_SCROLL_SPEED_FIXED >> 8) + carry) << 8;
			s.CameraY_fixed += cameraMove_fixed;
			int representedYMove_fixed = cameraMove_fixed - SHIP_SCROLL_SPEED_FIXED;
			s.Y_fixed += representedYMove_fixed;
			if (s.DualActive)
				s.P2_Y_fixed += representedYMove_fixed;
		}

		// These are deliberately independent comparisons in scroll.h. A fractional
		// upward step can cross the target and take the reverse branch immediately.
		if (targetY_px < (s.CameraY_fixed >> 8))
		{
			int sub = s.ScrollYSubpx - (SHIP_SCROLL_SPEED_FIXED & 0xFF);
			int borrow = sub < 0 ? 1 : 0;
			s.ScrollYSubpx = sub & 0xFF;
			int cameraMove_fixed = -(((SHIP_SCROLL_SPEED_FIXED >> 8) + borrow) << 8);
			s.CameraY_fixed += cameraMove_fixed;
			int representedYMove_fixed = SHIP_SCROLL_SPEED_FIXED + cameraMove_fixed;
			s.Y_fixed += representedYMove_fixed;
			if (s.DualActive)
				s.P2_Y_fixed += representedYMove_fixed;
		}

		int minCameraY_fixed = (_minScrollYLin - _nesCoordOffset) << 8;
		int maxCameraY_fixed = NesMaxCamY_px() << 8;
		if (s.CameraY_fixed < minCameraY_fixed)
		{
			s.Y_fixed += s.ScrollYSubpx;
			if (s.DualActive)
				s.P2_Y_fixed += s.ScrollYSubpx;
			s.CameraY_fixed = minCameraY_fixed;
			s.ScrollYSubpx = 0;
		}
		if (s.CameraY_fixed >= maxCameraY_fixed)
		{
			s.Y_fixed += s.ScrollYSubpx;
			if (s.DualActive)
				s.P2_Y_fixed += s.ScrollYSubpx;
			s.CameraY_fixed = maxCameraY_fixed;
			s.ScrollYSubpx = 0;
		}
	}

	private static int NesNoCamTrackingScreenY_fixed(in SimState s)
	{
		return s.Y_fixed - s.CameraY_fixed;
	}

	private void ApplyNesProcessYScrollDuringSpiderWait(ref SimState s)
	{
		if (_dualP2Guard)
		{
			return;
		}
		if (!s.DualActive && (s.GameMode == 0 || s.GameMode == 4 || s.GameMode == 8 || s.GameMode == 9 || s.GameMode == 11 || s.NoCamLockForced))
		{
			if (s.ExitPortalTimer != 0)
			{
				s.ExitPortalTimer--;
			}
			ApplyNesCubeRobotYScroll(ref s);
			return;
		}
		ApplyNesShipStyleYScroll(ref s);
	}

	private int ComputeInitCameraY(int startY_px)
	{
		int val2 = (_minScrollYLin - _nesCoordOffset) << 8;
		if (UseNesSpawnScrollDefaults || ConfigScrollYPosition.HasValue)
		{
			int scrollY = SharedPhysics.ResolveNesInitialScroll(ConfigScrollYPosition);
			// reset_level assigns the header value directly. Do not pre-cap it;
			// process_y_scroll performs the source-ordered cap on the gameplay tick.
			return (scrollY - _nesCoordOffset) << 8;
		}
		int val = NesMaxCamY_px() << 8;
		return Math.Max(val2, Math.Min(val, (startY_px << 8) - 30720));
	}

	private int NesMaxCamY_px()
	{
		int val = (mapHeight - 15) * 16;
		int val2 = SharedPhysics.NES_MAX_SCROLL_Y_LINEAR - _nesCoordOffset;
		return Math.Max(0, Math.Min(val, val2));
	}

	private int NesNtCameraTarget_fixed(int portalWorldY_px)
	{
		// activeSpriteY and target_scroll_y are both linear after PR #360.
		return (portalWorldY_px - PORTAL_TO_TOP_DIFF_PX) << 8;
	}

#if !DISABLE_DEBUG_LOGGING
	private static string SanitizeTraceTag(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "unknown";
		}
		StringBuilder stringBuilder = new StringBuilder(value.Length);
		foreach (char c in value)
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
		string text = stringBuilder.ToString().Trim('_');
		return (text.Length != 0) ? text : "unknown";
	}

	private void EnsureArtifactWritersOpen()
	{
		if (!EnableLogging)
		{
			_log = TextWriter.Null;
			SharedPhysics.FullTraceLog = null;
			return;
		}
		if (_artifactWritersOpen)
		{
			return;
		}
		string tempPath = Path.GetTempPath();
		string text = SanitizeTraceTag(LevelName);
		string text2 = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
		string text3 = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
		_pfDebugLogPath = Path.Combine(tempPath, $"famidash_pf_debug_{text}_{text2}.txt");
		_frameTracePath = Path.Combine(tempPath, $"famidash_pf_trace_{text}_{text2}.csv");
		_orbDebugLogPath = Path.Combine(tempPath, $"famidash_pf_orb_debug_{text}_{text2}.log");
		_fullTracePath = Path.Combine(tempPath, $"famidash_pf_trace_FULL_{text}_{text3}.log");
		UTF8Encoding uTF8Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
		try
		{
			_pfDebugWriter = new StreamWriter(_pfDebugLogPath, append: false, uTF8Encoding)
			{
				AutoFlush = true
			};
		}
		catch
		{
			_pfDebugWriter = null;
		}
		try
		{
			_orbDebugWriter = new StreamWriter(_orbDebugLogPath, append: false, uTF8Encoding)
			{
				AutoFlush = true
			};
			_orbDebugWriter.WriteLine($"# pf orb debug {DateTime.UtcNow:o} level={LevelName}");
		}
		catch
		{
			_orbDebugWriter = null;
		}
		try
		{
			_fullTraceWriter = new StreamWriter(_fullTracePath, append: false, uTF8Encoding)
			{
				AutoFlush = true
			};
			_fullTraceWriter.WriteLine($"# PfTrace opened {DateTime.UtcNow:o} level={LevelName}");
			_fullTraceWriter.WriteLine("# FrameLo=0 FrameHi=2147483647");
			_fullTraceWriter.WriteLine("# format: f=N cur=C gm=G tag=NAME k=v k=v ...");
		}
		catch
		{
			_fullTraceWriter = null;
		}
		List<TextWriter> list = new List<TextWriter>();
		if (!ReferenceEquals(_baseLog, TextWriter.Null))
		{
			list.Add(_baseLog);
		}
		if (_pfDebugWriter != null)
		{
			list.Add(new PrefixTextWriter(_pfDebugWriter, () => $"[PF f={_frameCounter}] "));
		}
		_log = (list.Count switch
		{
			0 => TextWriter.Null,
			1 => list[0],
			_ => new TeeTextWriter(list.ToArray())
		});
		SharedPhysics.FullTraceLog = delegate(string msg)
		{
			if (_fullTraceWriter == null || _speculativeDepth > 0)
			{
				return;
			}
			try
			{
				_fullTraceWriter.WriteLine($"f={_frameCounter} {msg}");
			}
			catch
			{
			}
		};
		_artifactWritersOpen = true;
	}

	private void CloseArtifactWriters()
	{
		try
		{
			_log.Flush();
		}
		catch
		{
		}
		_log = _baseLog;
		SharedPhysics.FullTraceLog = null;
		try
		{
			_pfDebugWriter?.Flush();
			_pfDebugWriter?.Dispose();
		}
		catch
		{
		}
		try
		{
			_orbDebugWriter?.Flush();
			_orbDebugWriter?.Dispose();
		}
		catch
		{
		}
		try
		{
			_fullTraceWriter?.Flush();
			_fullTraceWriter?.Dispose();
		}
		catch
		{
		}
		_pfDebugWriter = null;
		_orbDebugWriter = null;
		_fullTraceWriter = null;
		_artifactWritersOpen = false;
	}
#endif

	private void TraceFrameOpen()
	{
		#if !DISABLE_DEBUG_LOGGING
		if (!EnableLogging)
		{
			_log = TextWriter.Null;
			SharedPhysics.FullTraceLog = null;
			return;
		}
		EnsureArtifactWritersOpen();
		try
		{
			_traceWriter = new StreamWriter(_frameTracePath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
			{
				AutoFlush = true
			};
			_traceWriter.WriteLine("frame,X_fixed,Y_fixed,VelY_fixed,input,alive,X_px,Y_px,onGround,CamY_px,TgtCamY_px,mode,gravFlipped,shipCeilSlopeHit,shipCeilTileHit,shipCeilSpike,shipFloorSlopeHit,shipFloorTileHit,shipFloorSpike,CamY_fixed,Y_lowB,CamY_lowB,ScrollYSubpx,mini,VelX_fixed,SlopeType,LastSlopeType,SlopeFrames,SlopeWasOn");
		}
		catch
		{
			_traceWriter = null;
		}
		#endif
	}

	private void TraceFrame(int frame, ref SimState s, bool input, bool alive)
	{
		#if !DISABLE_DEBUG_LOGGING
		if (_traceWriter == null || _speculativeDepth > 0)
		{
			return;
		}
		try
		{
			int num = (s.X_fixed >> 8) + 8;
			int num2 = NesPlayerY_px(s.Y_fixed, s.CameraY_fixed) + 8;
			_traceWriter.WriteLine($"{frame},0x{s.X_fixed:X},0x{s.Y_fixed:X},0x{s.VelY_fixed:X},{(input ? 1 : 0)},{(alive ? 1 : 0)},{num},{num2},{(s.OnGround ? 1 : 0)},{s.CameraY_fixed >> 8},{s.TargetCameraY_fixed >> 8},{s.GameMode},{(s.GravFlipped ? 1 : 0)},{(s.ShipDbgCeilSlopeHit ? 1 : 0)},{(s.ShipDbgCeilTileHit ? 1 : 0)},{(s.ShipDbgCeilSpike ? 1 : 0)},{(s.ShipDbgFloorSlopeHit ? 1 : 0)},{(s.ShipDbgFloorTileHit ? 1 : 0)},{(s.ShipDbgFloorSpike ? 1 : 0)},0x{s.CameraY_fixed:X},{s.Y_fixed & 0xFF},{s.CameraY_fixed & 0xFF},{s.ScrollYSubpx},{(s.Mini ? 1 : 0)},0x{s.VelX_fixed:X},{s.SlopeType},{s.LastSlopeType},{s.SlopeFrames},{s.SlopeWasOnCounter}");
			if (s.DualActive)
			{
				int p2Xpx = (s.X_fixed >> 8) + 8;
				int p2Ypx = NesPlayerY_px(s.P2_Y_fixed, s.CameraY_fixed) + 8;
				_traceWriter.WriteLine($"p2,{frame},0x{s.P2_Y_fixed:X},0x{s.P2_VelY_fixed:X},{(input ? 1 : 0)},{(alive ? 1 : 0)},{p2Xpx},{p2Ypx},{(s.P2_OnGround ? 1 : 0)},{s.CameraY_fixed >> 8},{s.TargetCameraY_fixed >> 8},{s.GameMode},{(s.P2_GravFlipped ? 1 : 0)},0,0,0,0,0,0,0x{s.CameraY_fixed:X},{s.P2_Y_fixed & 0xFF},{s.CameraY_fixed & 0xFF},{s.ScrollYSubpx},{(s.P2_Mini ? 1 : 0)},0x{s.P2_VelX_fixed:X},{s.P2_SlopeType},{s.P2_LastSlopeType},{s.P2_SlopeFrames},{s.P2_SlopeWasOnCounter}");
			}
		}
		catch
		{
		}
		#endif
	}

	private void TraceFrameClose()
	{
		#if !DISABLE_DEBUG_LOGGING
		try
		{
			_traceWriter?.Flush();
			_traceWriter?.Dispose();
		}
		catch
		{
		}
		_traceWriter = null;
		CloseArtifactWriters();
		#endif
	}

	internal void OrbDbg(string msg)
	{
		#if !DISABLE_DEBUG_LOGGING
		if (_orbDebugWriter == null || _speculativeDepth > 0)
		{
			return;
		}
		try
		{
			_orbDebugWriter.WriteLine($"[PF f={_frameCounter}] {msg}");
		}
		catch
		{
		}
		#endif
	}

	private static bool IsCoinSprite(int sid)
	{
		return SharedPhysics.IsCoinSprite(sid);
	}

	private static int GetNesCoinKind(int sid)
	{
		return sid switch
		{
			0x07 or 0x1C => 0,
			0x1A or 0x1D => 1,
			0x1B or 0x1E => 2,
			_ => -1
		};
	}

	private static bool IsRegularNesCoin(int sid)
	{
		return sid == 0x07 || sid == 0x1A || sid == 0x1B;
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
		return sid == 0x59 || SharedPhysics.IsTeleportPortalEntrance(sid);
	}

	private static bool IsTeleportPortalExit(int sid)
	{
		return sid == 0x5A || SharedPhysics.IsTeleportPortalExit(sid);
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
		stringBuilder.Append("platformer,").Append(ForcePlatformer ? 1 : 0).Append('\n');
		stringBuilder.Append("frame,x,y,a,left,right\n");
		for (int i = 0; i < num; i++)
		{
			(int, int) tuple = PathPoints[i];
			sbyte direction = i < HorizontalInputs.Count ? HorizontalInputs[i] : (sbyte)0;
			stringBuilder.Append(i).Append(',').Append(tuple.Item1)
				.Append(',')
				.Append(tuple.Item2)
				.Append(',')
				.Append(Inputs[i] ? 1 : 0)
				.Append(',')
				.Append(direction < 0 ? 1 : 0)
				.Append(',')
				.Append(direction > 0 ? 1 : 0)
				.Append('\n');
		}
		if (Path2Points != null && Path2Points.Count > 0)
		{
			foreach (var p2 in Path2Points)
			{
				stringBuilder.Append("p2,").Append(p2.x).Append(',').Append(p2.y).Append('\n');
			}
		}
		return stringBuilder.ToString();
	}

	/// <summary>
	/// Replays an already-known input route through the authoritative PF/NES
	/// runtime. This is used to upgrade inputs-only legacy .pfdat data before
	/// native-simulator playback; it performs no path search or pruning.
	/// </summary>
	public void ReplayKnownPath(IReadOnlyList<bool> inputs,
		IReadOnlyList<sbyte>? horizontalDirections, int startX_px, int startY_px,
		int startSpeedUiIndex, int startGameMode, bool startGravFlipped,
		bool startMini)
	{
		var inputCopy = inputs != null
			? new List<bool>(inputs)
			: new List<bool>();
		IReadOnlyList<sbyte>? directionCopy =
			horizontalDirections != null
				? new List<sbyte>(horizontalDirections)
				: null;
		ReplayBfsPath(inputCopy, directionCopy, startX_px, startY_px,
			startSpeedUiIndex, startGameMode, startGravFlipped, startMini);
	}

	public PathfinderEngine(int[] tiles, int[] sprites, Dictionary<int, (int anchorTileX, int anchorTileY)> spriteAnchors, int mapWidth, int mapHeight, bool hasGroundLayer, int groundTileRows, int maxFallSpeed = 6, Dictionary<int, (int offsetX, int offsetY)>? spritePixelOffsets = null, int[]? nesSpriteLayer = null, NesSpriteRecord[]? nesSpriteRecords = null)
	{
		this.tiles = (tiles ?? Array.Empty<int>()).Select((int t) => (t >= 0) ? t : 0).ToArray();
		this.sprites = sprites ?? Array.Empty<int>();
		this.spriteAnchors = spriteAnchors ?? new Dictionary<int, (int, int)>();
		this.spritePixelOffsets = spritePixelOffsets ?? new Dictionary<int, (int, int)>();
		this.mapWidth = mapWidth;
		this.mapHeight = mapHeight;
		groundRowsToReserve = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
		_nesCoordOffset = (57 - this.mapHeight + groundRowsToReserve) * 16;
		int emptyTopRows = Math.Max(0, 57 - this.mapHeight);
		_minScrollYLin = (emptyTopRows * 16) | 8;
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
		for (int i = 0; i < this.sprites.Length; i++)
		{
			int num5 = this.sprites[i];
			if (num5 == -1)
			{
				continue;
			}
			int num6 = i % mapWidth;
			int num7 = i / mapWidth;
			int num8 = num5 & 0xFF;
			int sid = num8;
			int num9 = -1;
			bool flag = !IsSpeedPortal(num8) && !IsGameModePortal(num8) && !IsGravityPortal(num8) && !IsMiniGrowthPortal(num8);
			if (this.spriteAnchors.TryGetValue(i, out (int, int) value))
			{
				num9 = value.Item2 * mapWidth + value.Item1;
				if (flag && num9 >= 0 && num9 < this.sprites.Length)
				{
					int num10 = this.sprites[num9];
					if (num10 >= 0 && num10 < 256)
					{
						sid = num10 & 0xFF;
					}
				}
			}
			sid = SharedPhysics.NormalizePortalGeometrySid(sid);
			int val = ((sid >= 0 && sid < sprite_widths.Length) ? sprite_widths[sid] : 16);
			int val2 = ((sid >= 0 && sid < sprite_heights.Length) ? sprite_heights[sid] : 16);
			int num11 = ((sid >= 0 && sid < sprite_x_offset.Length) ? sprite_x_offset[sid] : 0);
			int num12 = ((sid >= 0 && sid < sprite_y_offset.Length) ? sprite_y_offset[sid] : 0);
			int num13 = 0;
			int num14 = 0;
			(int, int) value3;
			if (num9 >= 0 && this.spritePixelOffsets.TryGetValue(num9, out (int, int) value2))
			{
				(num13, num14) = value2;
			}
			else if (this.spritePixelOffsets.TryGetValue(i, out value3))
			{
				(num13, num14) = value3;
			}
			int num15 = num6 * 16 + num11 + num13;
			int num16 = (num7 - groundRowsToReserve) * 16 + num12 + num14 - 1;
			int hitRight = num15 + Math.Max(1, val);
			int hitBottom = num16 + Math.Max(1, val2);
			int num17;
			if (this.spriteAnchors.TryGetValue(i, out (int, int) value4))
			{
				(num17, _) = value4;
			}
			else
			{
				num17 = num6;
			}
			int num18 = num17;
			allSprites.Add(new SpriteEntry
			{
				Index = i,
				ProcessKey = i,
				SpriteId = num5,
				AnchorX_px = num18 * 16 + 8 + num13,
				AnchorY_px = (num7 - groundRowsToReserve) * 16 + 8 + num14,
				HitLeft = num15,
				HitTop = num16,
				HitRight = hitRight,
				HitBottom = hitBottom
			});
		}
		allSprites.Sort(delegate(SpriteEntry a, SpriteEntry b)
		{
			int num39 = a.AnchorX_px.CompareTo(b.AnchorX_px);
			if (num39 != 0)
			{
				return num39;
			}
			int num40 = ((!IsGravityPortal(a.SpriteId)) ? 1 : 0);
			int value7 = ((!IsGravityPortal(b.SpriteId)) ? 1 : 0);
			return num40.CompareTo(value7);
		});
		_spritesArr = allSprites.ToArray();
		var nesSprites = new List<SpriteEntry>();
		var nesWorldX = new List<int>();
		void AddNesSprite(int rawSid, int baseX, int baseY, int rawOrdinal)
		{
			int sid = SharedPhysics.NormalizePortalGeometrySid(rawSid & 0xFF);
			int width = sid >= 0 && sid < sprite_widths.Length ? sprite_widths[sid] : 16;
			int height = sid >= 0 && sid < sprite_heights.Length ? sprite_heights[sid] : 16;
			int xOffset = sid >= 0 && sid < sprite_x_offset.Length ? sprite_x_offset[sid] : 0;
			int yOffset = sid >= 0 && sid < sprite_y_offset.Length ? sprite_y_offset[sid] : 0;
			int hitLeft = baseX + xOffset;
			int hitTop = baseY + yOffset - 1;
			int processKey = this.sprites.Length + rawOrdinal;
			nesSprites.Add(new SpriteEntry
			{
				Index = processKey,
				ProcessKey = processKey,
				SpriteId = rawSid,
				AnchorX_px = baseX + 8,
				AnchorY_px = baseY + 8,
				HitLeft = hitLeft,
				HitTop = hitTop,
				HitRight = hitLeft + Math.Max(1, width),
				HitBottom = hitTop + Math.Max(1, height),
				AllocatedSlot = -1
			});
			nesWorldX.Add(baseX);
		}

		if (nesSpriteRecords != null)
		{
			for (int rawOrdinal = 0; rawOrdinal < nesSpriteRecords.Length; rawOrdinal++)
			{
				NesSpriteRecord record = nesSpriteRecords[rawOrdinal];
				AddNesSprite(
					record.SpriteId,
					record.X,
					record.Y - _nesCoordOffset,
					rawOrdinal);
			}
		}
		else
		{
			int[] rawNesLayer = nesSpriteLayer != null && nesSpriteLayer.Length == this.sprites.Length
				? nesSpriteLayer
				: this.sprites;
			for (int column = 0; column < this.mapWidth; column++)
			{
				for (int row = 0; row < this.mapHeight; row++)
				{
					int sourceIndex = row * this.mapWidth + column;
					if ((uint)sourceIndex >= (uint)rawNesLayer.Length)
						continue;
					int rawSid = rawNesLayer[sourceIndex];
					if (rawSid < 0)
						continue;

					int pixelOffsetX = 0;
					int pixelOffsetY = 0;
					if (this.spritePixelOffsets.TryGetValue(sourceIndex, out var rawOffset))
						(pixelOffsetX, pixelOffsetY) = rawOffset;
					AddNesSprite(
						rawSid,
						column * 16 + pixelOffsetX,
						(row - groundRowsToReserve) * 16 + pixelOffsetY,
						nesSprites.Count);
				}
			}
		}
		_nesSpritesArr = nesSprites.ToArray();
		_nesStreamOrder = Enumerable.Range(0, _nesSpritesArr.Length).ToArray();
		_nesSpriteWorldX = nesWorldX.ToArray();
		int nesSpriteCount = _nesSpritesArr.Length;
		_bfsTransitionPortals = new List<BfsTransitionPortal>();
		_bfsRainbowPortals = new List<BfsRainbowPortal>();
		foreach (SpriteEntry portal in _nesSpritesArr)
		{
			if (portal.SpriteId == 100 || portal.SpriteId == 126)
			{
				_bfsRainbowPortals.Add(new BfsRainbowPortal(
					portal.AnchorX_px, portal.ProcessKey,
					portal.SpriteId == 100 ? 8 : 12));
			}
			else if (IsGameModePortal(portal.SpriteId))
			{
				int targetMode = SpriteIdToGameMode(portal.SpriteId);
				_bfsTransitionPortals.Add(new BfsTransitionPortal(
					portal.AnchorX_px, portal.AnchorY_px, portal.ProcessKey,
					portal.SpriteId, targetMode,
					(portal.HitTop + portal.HitBottom) / 2));
			}
			else if (IsMiniGrowthPortal(portal.SpriteId))
			{
				_bfsTransitionPortals.Add(new BfsTransitionPortal(
					portal.AnchorX_px, portal.AnchorY_px, portal.ProcessKey,
					portal.SpriteId, -1,
					(portal.HitTop + portal.HitBottom) / 2));
			}
		}
		_bfsTransitionPortals.Sort((a, b) =>
		{
			int byX = a.X.CompareTo(b.X);
			return byX != 0 ? byX : a.ProcessKey.CompareTo(b.ProcessKey);
		});
		_bfsRainbowPortals.Sort((a, b) =>
		{
			int byX = a.X.CompareTo(b.X);
			return byX != 0 ? byX : a.ProcessKey.CompareTo(b.ProcessKey);
		});
		allCoins = _nesSpritesArr.Where((SpriteEntry sp) => IsCoinSprite(sp.SpriteId) || IsMiniCoinSprite(sp.SpriteId)).ToList();
		_spriteCompactMap = new int[this.sprites.Length + nesSpriteCount];
		Array.Fill(_spriteCompactMap, -1);
		_spriteCompactCount = 0;
		foreach (SpriteEntry allSprite in allSprites)
		{
			if (_spriteCompactMap[allSprite.ProcessKey] < 0)
				_spriteCompactMap[allSprite.ProcessKey] = _spriteCompactCount++;
		}
		foreach (SpriteEntry nesSprite in _nesSpritesArr)
		{
			_spriteCompactMap[nesSprite.ProcessKey] = _spriteCompactCount++;
		}
		PathPoints = new List<(int, int)>();
		Path2Points = new List<(int, int)>();
		Inputs = new List<bool>();
		HorizontalInputs = new List<sbyte>();
		int num34 = 0;
		for (int n = 0; n < this.tiles.Length; n++)
		{
			num34 = num34 * 31 + this.tiles[n];
		}
		int num35 = 0;
		for (int num36 = 0; num36 < this.sprites.Length; num36++)
		{
			num35 = num35 * 31 + this.sprites[num36];
		}
		int num37 = 0;
		foreach (KeyValuePair<int, (int, int)> item in this.spritePixelOffsets.OrderBy((KeyValuePair<int, (int offsetX, int offsetY)> x) => x.Key))
		{
			num37 = num37 * 31 + item.Key + item.Value.Item1 * 7 + item.Value.Item2 * 13;
		}
		int num38 = 0;
		foreach (KeyValuePair<int, (int, int)> item2 in this.spriteAnchors.OrderBy((KeyValuePair<int, (int anchorTileX, int anchorTileY)> x) => x.Key))
		{
			num38 = num38 * 31 + item2.Key + item2.Value.Item1 * 7 + item2.Value.Item2 * 13;
		}
		string value6 = $"[PF_DIAG] tiles={this.tiles.Length} tileHash=0x{num34:X8} sprites={this.sprites.Length} sprHash=0x{num35:X8} offsets={this.spritePixelOffsets.Count} offHash=0x{num37:X8} anchors={this.spriteAnchors.Count} anchHash=0x{num38:X8} w={mapWidth} h={mapHeight} ground={groundRowsToReserve} maxFall=0x{this.maxFallSpeed:X} spriteEntries={allSprites.Count} nesSpriteRecords={nesSpriteCount}";
		_log.WriteLine(value6);
		// A stream record has no permanent slot. The NES assigns and replaces
		// live slots dynamically as check_spr_objects walks slots 15 down to 0.
		for (int dbgI = 0; dbgI < nesSpriteCount; dbgI++)
		{
			if (IsSpeedPortal(_nesSpritesArr[dbgI].SpriteId))
			{
				_log.WriteLine($"[SPEED_PORTAL_STREAM] stream={dbgI} idx={_nesSpritesArr[dbgI].Index} sid={_nesSpritesArr[dbgI].SpriteId} anchor=({_nesSpritesArr[dbgI].AnchorX_px},{_nesSpritesArr[dbgI].AnchorY_px})");
			}
		}
		_log.Flush();
	}

	private void InitNesSlots(ref SimState s)
	{
		s.NesSlots = s_nesInt16Pool.Rent(clear: true);
		s.NesSlotDead = s_nesBool16Pool.Rent(clear: true);
		s.NesSlotActive = s_nesBool16Pool.Rent(clear: true);
		s.NesSlotWorldY = s_nesInt16Pool.Rent(clear: true);
		s.NesSlotRealX = s_nesInt16Pool.Rent(clear: true);
		s.NesSlotRealY = s_nesInt16Pool.Rent(clear: true);
		s.CoinTimer = s_coinInt3Pool.Rent(clear: true);
		s.CoinSpeed = s_coinInt3Pool.Rent(clear: true);
		s.CoinAnimating = false;
		for (int i = 0; i < 16; i++) s.NesSlots[i] = -1;
		s.NesSprDataPtr = 0;
		s.TeleportOutputY_px = 0;
		int slot = 15;
		while (slot >= 0 && s.NesSprDataPtr < _nesStreamOrder.Length)
		{
			s.NesSlots[slot] = _nesStreamOrder[s.NesSprDataPtr];
			s.NesSlotWorldY[slot] = _nesSpritesArr[s.NesSlots[slot]].AnchorY_px - 8;
			s.NesSlotRealX[slot] = _nesSpriteWorldX[s.NesSlots[slot]] & 0xFF;
			s.NesSlotRealY[slot] = s.NesSlotWorldY[slot] & 0xFF;
			s.NesSlotDead[slot] = false;
			s.NesSlotActive[slot] = false;
			s.NesSprDataPtr++;
			slot--;
		}
	}

	private void NesLoadNextSprite(ref SimState s, int slot)
	{
		if (s.NesSprDataPtr < _nesStreamOrder.Length)
		{
			s.NesSlots[slot] = _nesStreamOrder[s.NesSprDataPtr++];
			s.NesSlotWorldY[slot] = _nesSpritesArr[s.NesSlots[slot]].AnchorY_px - 8;
			s.NesSlotRealX[slot] = _nesSpriteWorldX[s.NesSlots[slot]] & 0xFF;
			s.NesSlotRealY[slot] = s.NesSlotWorldY[slot] & 0xFF;
			s.NesSlotDead[slot] = false;
			s.NesSlotActive[slot] = false;
		}
		else
		{
			s.NesSlots[slot] = -1;
			s.NesSlotWorldY[slot] = 0;
			s.NesSlotRealX[slot] = 0;
			s.NesSlotRealY[slot] = 0;
			s.NesSlotDead[slot] = false;
			s.NesSlotActive[slot] = false;
		}
	}

	private void CheckSprObjects(ref SimState s)
	{
		// In normal autoscroll play the NES holds currplayer_x at $50 once
		// scrolling begins. check_spr_objects compares sprite world X against
		// that scroll value, so derive the same scroll from PF's world X.
		int scrollX_px = GetScrollX_px(in s);
		int scrollY_px = s.CameraY_fixed >> 8;
		for (int slot = 15; slot >= 0; slot--)
		{
			int sprIdx = s.NesSlots[slot];
			if (sprIdx < 0)
			{
				NesLoadNextSprite(ref s, slot);
				continue;
			}
			if (s.NesSlotDead[slot])
			{
				NesLoadNextSprite(ref s, slot);
				continue;
			}
			int relX = _nesSpriteWorldX[sprIdx] - scrollX_px;
			s.NesSlotRealX[slot] = relX & 0xFF;
			if (relX < 0)
			{
				if (IsRegularNesCoin(_nesSpritesArr[sprIdx].SpriteId & 0xFF))
					s.CoinAnimating = false;
				NesLoadNextSprite(ref s, slot);
				continue;
			}
			if (relX >= 256)
			{
				s.NesSlotActive[slot] = false;
				continue;
			}

			// nesdash.s deliberately clears carry before SBC, so the vertical
			// visibility test is rawY - scrollY - 1.
			int rawY = s.NesSlotWorldY[slot];
			int relY = rawY - scrollY_px - 1;
			s.NesSlotRealY[slot] = relY & 0xFF;
			bool visible = relY >= 0 && relY < 256;
			if (!visible && s.CoinAnimating &&
				IsRegularNesCoin(_nesSpritesArr[sprIdx].SpriteId & 0xFF))
				visible = true;
			s.NesSlotActive[slot] = visible;
		}
	}

	public void ReplayInputSequence(int startX_px, int startY_px, int startSpeedUiIndex, int startGameMode, bool startGravFlipped, bool startMini, IList<bool> inputs, int preRollFrames, TextWriter output)
	{
		_baseLog = TextWriter.Null;
		_log = _baseLog;
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
		SimState simState = default(SimState);
		simState.X_fixed = InitialXFixed(startX_px);
		simState.Y_fixed = (startY_px << 8) | SpawnYSubpx;
		simState.VelX_fixed = SpeedUiIndexToFixed(startSpeedUiIndex);
		simState.GlobalSpeed_fixed = simState.VelX_fixed;
		simState.VelY_fixed = 0;
		simState.GameMode = startGameMode;
		simState.GravFlipped = startGravFlipped;
		simState.Mini = startMini;
		simState.GravMul = ((!startGravFlipped) ? 1 : (-1));
		simState.GravityMod = 1.0;
		simState.WasZeroedByCollision = true;
		simState.OnGround = true;
		simState.ProcessedSprites = NewSpriteSet();
		simState.PendingOrbIndex = -1;
		simState.PendingOrbSpriteId = -1;
		simState.PendingOrbExtra1Index = -1;
		simState.PendingOrbExtra1SpriteId = -1;
		simState.PendingOrbExtra2Index = -1;
		simState.PendingOrbExtra2SpriteId = -1;
		simState.NinjaJumps = ((startGameMode == 8) ? 3 : 0);
		simState.CameraY_fixed = num;
		simState.TargetCameraY_fixed = num;
		InitializeHorizontalState(ref simState);
		SimState s = simState;
		ApplyPortalsUpTo(ref s, startX_px);
		InitNesSlots(ref s);
		ApplyNesIntroFreezePrestep(ref s);
		CheckSprObjects(ref s);
		output.WriteLine("tasFrame,gameFrame,x,y,velY,jump,onGround,mode,mini,gravity,event");
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
			if (!flag2 || endLevel || i % 60 == 0 ||
				(num3 >= 2200 && num3 <= 2650) ||
				(num3 >= 12200 && num3 <= 12900))
			{
				output.WriteLine($"{i - preRollFrames},{value},{num3},{value2}," +
					$"0x{velY_fixed & 0xFFFF:X4},{flag},{onGround}," +
					$"{s.GameMode},{s.Mini},{s.GravFlipped},{value3}");
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
		_baseLog = (EnableLogging && Verbose ? Console.Error : TextWriter.Null);
		_log = _baseLog;
		ReplayFrames.Clear();
		for (int i = 0; i < allCoins.Count; i++)
		{
			_log.WriteLine($"[COIN_INFO] coin#{i} idx={allCoins[i].Index} sid=0x{allCoins[i].SpriteId:X2} pos=({allCoins[i].AnchorX_px},{allCoins[i].AnchorY_px}) hit=({allCoins[i].HitLeft},{allCoins[i].HitTop})-({allCoins[i].HitRight},{allCoins[i].HitBottom})");
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		double jumpTimingBias = JumpTimingBias;
		_autoForgivenCoins.Clear();

		// First run a very narrow beam through the exact BFS transition engine. This
		// often finds an easy route without paying for the full frontier. It is safe
		// to prune here because every incomplete result falls through to the original
		// exhaustive BFS with its normal frontier and route-recovery behavior.
		bool canUseFastSearch = UseFastSearch &&
			(DebugBfsPrefixInputs == null || DebugBfsPrefixInputs.Count == 0);
		if (canUseFastSearch)
		{
			int fastBudgetMs = GetFastSearchBudgetMs();
			int fastFrontierCap = GetFastSearchFrontierCap();
			long fastDeadline = Stopwatch.GetTimestamp() + Math.Max(1L,
				(long)(Stopwatch.Frequency * (fastBudgetMs / 1000.0)));
			_log.WriteLine($"[FAST] Parallel exact-state beam, budget={fastBudgetMs}ms " +
				$"frontier={fastFrontierCap}");
			RunBFS(startX_px, startY_px, startSpeedUiIndex, startGameMode,
				startGravFlipped, startMini, fastFrontierCap, fastDeadline);

			bool collectedEveryCoin = !PreferCoins || allCoins.Count == 0 ||
				FinalCollectedCoinIndices?.Count == allCoins.Count;
			if (Success && collectedEveryCoin && Inputs.Count > 0)
			{
				stopwatch.Stop();
				_log.WriteLine("[FAST] Canonical replay completed; full BFS not required");
				return;
			}
			else if (Success && !collectedEveryCoin)
			{
				_log.WriteLine($"[FAST] Completed with " +
					$"{FinalCollectedCoinIndices?.Count ?? 0}/{allCoins.Count} coins; " +
					"starting BFS for the full coin objective");
			}
			else
			{
				_log.WriteLine("[FAST] No complete beam replay within budget; starting full BFS");
			}
			if (FastSearchOnly)
			{
				stopwatch.Stop();
				if (!collectedEveryCoin)
				{
					Success = false;
					ResultMessage = $"Fast exact search collected " +
						$"{FinalCollectedCoinIndices?.Count ?? 0}/{allCoins.Count} coins";
				}
				return;
			}

			// Never expose a partial bounded result as the BFS result.
			Success = false;
			FinalCollectedCoinIndices = null;
			ReplayFrames.Clear();
			_autoForgivenCoins.Clear();
		}
		RunBFS(startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped, startMini);
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
		List<bool>? inputs = ((Inputs != null) ? new List<bool>(Inputs) : null);
		List<(int, int)>? pathPoints = ((PathPoints != null) ? new List<(int, int)>(PathPoints) : null);
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
				if (inputs != null) Inputs = inputs;
				if (pathPoints != null) PathPoints = pathPoints;
				ResultMessage = resultMessage;
				_log.WriteLine($"[BFS?HEURISTIC] Heuristic worse ({num3}px vs BFS {num}px), keeping BFS result");
			}
		}
		_coinForceWalkZones.Clear();
		_coinAltitudePenalties.Clear();
		if (Success && PreferCoins && _forgivenCoins.Count > 0 && allCoins.Count > 0)
		{
			int num4 = (mapHeight - groundRowsToReserve) * 16 - 15;
			List<bool> inputs2 = new List<bool>(Inputs ?? new List<bool>());
			List<(int, int)> pathPoints2 = new List<(int, int)>(PathPoints ?? new List<(int, int)>());
			string resultMessage2 = ResultMessage;
			int num5 = ((FinalCollectedCoinIndices != null) ? FinalCollectedCoinIndices.Count : 0);
			HashSet<int>? hashSet = ((FinalCollectedCoinIndices != null) ? new HashSet<int>(FinalCollectedCoinIndices) : null);
			List<SpriteEntry> list = allCoins.Where((SpriteEntry c) => _forgivenCoins.Contains(c.Index)).ToList();
			int value8;
			List<SpriteEntry> list2 = list.Where((SpriteEntry c) => _forgivenCoinGameModes.TryGetValue(c.Index, out value8) && value8 == 0).ToList();
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
							inputs2 = new List<bool>(Inputs ?? new List<bool>());
							pathPoints2 = new List<(int, int)>(PathPoints ?? new List<(int, int)>());
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
				foreach (double num15 in array)
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
							inputs2 = new List<bool>(Inputs ?? new List<bool>());
							pathPoints2 = new List<(int, int)>(PathPoints ?? new List<(int, int)>());
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
			int value7;
			List<SpriteEntry> list7 = list.Where((SpriteEntry c) => _forgivenCoinGameModes.TryGetValue(c.Index, out value7) && value7 == 1).ToList();
			bool flag3 = list7.All(delegate(SpriteEntry c)
			{
				if (_coinCollectThenLoseCount.TryGetValue(c.Index, out var value5) && value5 > 0)
				{
					return false;
				}
				if (_autoForgivenCoins.Contains(c.Index))
				{
					return true;
				}
				_coinMissRetryCount.TryGetValue(c.Index, out var value6);
				return value6 >= 3;
			});
			if (list7.Count > 0 && flag3)
			{
				_log.WriteLine($"[SHIP_RETRY_SKIP] All {list7.Count} ship coins have high miss counts \ufffd skipping ship retry");
			}
			if (list7.Count > 0 && !flag3 && num5 < allCoins.Count)
			{
				double[] array = new double[2] { jumpTimingBias, 0.3 };
				foreach (double num17 in array)
				{
					if (num5 >= allCoins.Count)
					{
						break;
					}
					_log.WriteLine($"[COIN_RETRY_SHIP] Attempting {list7.Count} ship-mode forgiven coins bias={num17:F2} with aggressive threshold");
					JumpTimingBias = num17;
					_coinForceWalkZones.Clear();
					_coinAltitudePenalties.Clear();
					_coinGroundBiasZones.Clear();
					_retryMandatoryCoins.Clear();
					_shipCoinAggressiveThreshold = 20;
					RunSingleAttempt(startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped, startMini);
					if (Success)
					{
						int num18 = ((FinalCollectedCoinIndices != null) ? FinalCollectedCoinIndices.Count : 0);
						_log.WriteLine($"[COIN_RETRY_SHIP] Result: {num18}/{allCoins.Count} coins (bias={num17:F2})");
						if (num18 > num5)
						{
							_log.WriteLine($"[COIN_RETRY_SHIP] Improved: {num18} vs {num5}");
							inputs2 = new List<bool>(Inputs ?? new List<bool>());
							pathPoints2 = new List<(int, int)>(PathPoints ?? new List<(int, int)>());
							resultMessage2 = ResultMessage;
							num5 = num18;
							hashSet = ((FinalCollectedCoinIndices != null) ? new HashSet<int>(FinalCollectedCoinIndices) : null);
							break;
						}
					}
					else
					{
						_log.WriteLine($"[COIN_RETRY_SHIP] Retry bias={num17:F2} FAILED");
						Success = true;
					}
				}
				JumpTimingBias = jumpTimingBias;
			}
			int value4;
			List<SpriteEntry> list8 = list.Where((SpriteEntry c) => _forgivenCoinGameModes.TryGetValue(c.Index, out value4) && value4 == 2).ToList();
			if (list8.Count > 0 && num5 < allCoins.Count)
			{
				double[] array = new double[2] { 0.0, 1.0 };
				foreach (double num19 in array)
				{
					if (num5 >= allCoins.Count)
					{
						break;
					}
					if (Math.Abs(num19 - jumpTimingBias) < 0.05)
					{
						continue;
					}
					_log.WriteLine($"[COIN_RETRY_BALL] Attempting {list8.Count} ball-mode forgiven coins bias={num19:F2}");
					JumpTimingBias = num19;
					_coinForceWalkZones.Clear();
					_coinAltitudePenalties.Clear();
					_coinGroundBiasZones.Clear();
					_retryMandatoryCoins.Clear();
					RunSingleAttempt(startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped, startMini);
					if (Success)
					{
						int num20 = ((FinalCollectedCoinIndices != null) ? FinalCollectedCoinIndices.Count : 0);
						_log.WriteLine($"[COIN_RETRY_BALL] Result: {num20}/{allCoins.Count} coins (bias={num19:F2})");
						if (num20 > num5)
						{
							_log.WriteLine($"[COIN_RETRY_BALL] Improved: {num20} vs {num5}");
							inputs2 = new List<bool>(Inputs ?? new List<bool>());
							pathPoints2 = new List<(int, int)>(PathPoints ?? new List<(int, int)>());
							resultMessage2 = ResultMessage;
							num5 = num20;
							hashSet = ((FinalCollectedCoinIndices != null) ? new HashSet<int>(FinalCollectedCoinIndices) : null);
							break;
						}
					}
					else
					{
						_log.WriteLine($"[COIN_RETRY_BALL] Retry bias={num19:F2} FAILED");
						Success = true;
					}
				}
				JumpTimingBias = jumpTimingBias;
			}
			Inputs = inputs2;
			PathPoints = pathPoints2;
			FinalCollectedCoinIndices = hashSet;
			int num21 = hashSet?.Count ?? 0;
			int num22 = allCoins.Count - num21;
			int num23 = resultMessage2.IndexOf('[');
			string value3 = ((num23 > 0) ? resultMessage2.Substring(0, num23).TrimEnd() : resultMessage2);
			ResultMessage = $"{value3} [{num21}/{allCoins.Count} coins]";
			if (num22 > 0)
			{
				ResultMessage += $" ({num22} unreachable)";
			}
		}
		if (!Success)
		{
			double[] array2 = ((!(jumpTimingBias >= 0.5)) ? new double[4] { 0.25, 0.5, 0.75, 1.0 } : new double[4] { 0.75, 0.5, 0.25, 0.0 });
			double[] array = array2;
			foreach (double num24 in array)
			{
				if (!(Math.Abs(num24 - jumpTimingBias) < 0.01))
				{
					JumpTimingBias = num24;
					RunSingleAttempt(startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped, startMini);
					if (Success)
					{
						break;
					}
				}
			}
		}
		JumpTimingBias = jumpTimingBias;
		// RunSingleAttempt can backtrack and replace its route several times.
		// Replaying the selected final inputs once here publishes a clean,
		// contiguous canonical state stream for native-simulator playback.
		if (Inputs.Count > 0)
		{
			var finalInputs = new List<bool>(Inputs);
			IReadOnlyList<sbyte>? finalDirections =
				HorizontalInputs.Count == Inputs.Count
					? new List<sbyte>(HorizontalInputs)
					: null;
			ReplayBfsPath(finalInputs, finalDirections, startX_px, startY_px,
				startSpeedUiIndex, startGameMode, startGravFlipped, startMini);
		}
		stopwatch.Stop();
		double totalSeconds = stopwatch.Elapsed.TotalSeconds;
		ResultMessage += $" [{totalSeconds:F1}s]";
	}

	private bool ReplayInputs(List<bool> inputs, int startX_px, int startY_px, int startSpeedUiIndex, int startGameMode, bool startGravFlipped, bool startMini, out List<(int x, int y)> pathPoints)
	{
		pathPoints = new List<(int, int)>();
		int num = ComputeInitCameraY(startY_px);
		SimState simState = default(SimState);
		simState.X_fixed = InitialXFixed(startX_px);
		simState.Y_fixed = (startY_px << 8) | SpawnYSubpx;
		simState.VelX_fixed = SpeedUiIndexToFixed(startSpeedUiIndex);
		simState.GlobalSpeed_fixed = simState.VelX_fixed;
		simState.VelY_fixed = 0;
		simState.GameMode = startGameMode;
		simState.GravFlipped = startGravFlipped;
		simState.Mini = startMini;
		simState.GravMul = ((!startGravFlipped) ? 1 : (-1));
		simState.GravityMod = 1.0;
		simState.WasZeroedByCollision = true;
		simState.OnGround = true;
		simState.ProcessedSprites = NewSpriteSet();
		simState.PendingOrbIndex = -1;
		simState.PendingOrbSpriteId = -1;
		simState.PendingOrbExtra1Index = -1;
		simState.PendingOrbExtra1SpriteId = -1;
		simState.PendingOrbExtra2Index = -1;
		simState.PendingOrbExtra2SpriteId = -1;
		simState.NinjaJumps = ((startGameMode == 8) ? 3 : 0);
		simState.CameraY_fixed = num;
		simState.TargetCameraY_fixed = num;
		InitializeHorizontalState(ref simState);
		SimState s = simState;
		ApplyPortalsUpTo(ref s, startX_px);
		InitNesSlots(ref s);
		ApplyNesIntroFreezePrestep(ref s);
		CheckSprObjects(ref s);
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

	private static int GetFastSearchBudgetMs()
	{
		if (int.TryParse(Environment.GetEnvironmentVariable(
			"FAMIDASH_FAST_SEARCH_MS"), out int configured))
		{
			return Math.Clamp(configured, 250, 60000);
		}
		return 15000;
	}

	private static int GetFastSearchFrontierCap()
	{
		if (int.TryParse(Environment.GetEnvironmentVariable(
			"FAMIDASH_FAST_FRONTIER"), out int configured))
		{
			return Math.Clamp(configured, 4, 32768);
		}
		return 256;
	}

	private void RunSingleAttempt(int startX_px, int startY_px, int startSpeedUiIndex, int startGameMode, bool startGravFlipped, bool startMini)
	{
		int num = ComputeInitCameraY(startY_px);
		SimState simState = default(SimState);
		simState.X_fixed = InitialXFixed(startX_px);
		simState.Y_fixed = (startY_px << 8) | SpawnYSubpx;
		simState.VelX_fixed = SpeedUiIndexToFixed(startSpeedUiIndex);
		simState.GlobalSpeed_fixed = simState.VelX_fixed;
		simState.VelY_fixed = 0;
		simState.GameMode = startGameMode;
		simState.GravFlipped = startGravFlipped;
		simState.Mini = startMini;
		simState.GravMul = ((!startGravFlipped) ? 1 : (-1));
		simState.GravityMod = 1.0;
		simState.WasZeroedByCollision = true;
		simState.OnGround = true;
		simState.ProcessedSprites = NewSpriteSet();
		simState.PendingOrbIndex = -1;
		simState.PendingOrbSpriteId = -1;
		simState.PendingOrbExtra1Index = -1;
		simState.PendingOrbExtra1SpriteId = -1;
		simState.PendingOrbExtra2Index = -1;
		simState.PendingOrbExtra2SpriteId = -1;
		simState.NinjaJumps = ((startGameMode == 8) ? 3 : 0);
		simState.CameraY_fixed = num;
		simState.TargetCameraY_fixed = num;
		InitializeHorizontalState(ref simState);
		SimState s = simState;
		ApplyPortalsUpTo(ref s, startX_px);
		InitNesSlots(ref s);
		ApplyNesIntroFreezePrestep(ref s);
		CheckSprObjects(ref s);
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
		SimState simState2 = default(SimState);
		int num6 = 0;
		int num7 = 0;
		int num8 = 0;
		int nextCoinCheckIdx = 0;
		List<BacktrackCheckpoint>? list = null;
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
			SimState simState3 = default(SimState);
			bool flag4 = flag || flag3 || flag2;
			if (flag4)
			{
				simState3 = s.Clone();
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
			if (flag7 && _backtrackCheckpoints != null && _backtrackCheckpoints.Count > 0)
			{
				int frame = _backtrackCheckpoints[_backtrackCheckpoints.Count - 1].Frame;
				if (i - frame < 4)
				{
					flag7 = false;
				}
			}
			if (flag7 && !flag5 && !_backtrackActive)
			{
				if (_backtrackCheckpoints != null && _backtrackCheckpoints.Count >= 200)
				{
					_backtrackCheckpoints.RemoveAt(0);
				}
				if (_backtrackCheckpoints == null)
				{
					_backtrackCheckpoints = new List<BacktrackCheckpoint>();
				}
				SimState state = (flag4 ? simState3 : s.Clone());
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
					simState2 = s.Clone();
					num6 = i;
					num7 = Inputs.Count;
					num8 = PathPoints.Count;
					nextCoinCheckIdx = _nextCoinCheckIdx;
					list = (_backtrackCheckpoints ?? new List<BacktrackCheckpoint>()).Select((BacktrackCheckpoint cp) => new BacktrackCheckpoint
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
					_forgivenCoinGameModes[spriteEntry3.Index] = simState2.GameMode;
					_coinInputScript.Clear();
					_coinInputScriptCoinIdx = -1;
					s = simState2;
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
					_backtrackCheckpoints = list ?? new List<BacktrackCheckpoint>();
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
						SpriteSet? processedSprites = s.ProcessedSprites;
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
				_forgivenCoinGameModes[spriteEntry4.Index] = simState2.GameMode;
				_coinInputScript.Clear();
				_coinInputScriptCoinIdx = -1;
				s = simState2;
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
				_backtrackCheckpoints = list ?? new List<BacktrackCheckpoint>();
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
					SpriteSet? processedSprites2 = s.ProcessedSprites;
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
		bool exactDiagonalPhase = s.GameMode == 6 || s.GameMode == 10;
		// Wave/snake motion can traverse diagonal corridors at exact fixed-point
		// phases. Two-pixel coin-BFS buckets can merge distinct future paths, so
		// retain those phases; this only preserves paths and never removes one.
		bool preserveExactPhase = !PreferCoins || exactDiagonalPhase;
		int num = (preserveExactPhase ? (s.Y_fixed & 0xFFFF) : (flag ? ((s.Y_fixed >> 9) & 0xFFF) : ((s.Y_fixed >> 6) & 0xFFFF)));
		int num2 = (preserveExactPhase ? (s.VelY_fixed & 0xFFFF) : (flag ? ((s.VelY_fixed + 32768 >> 5) & 0xFFF) : ((s.VelY_fixed + 32768 >> 6) & 0x7FF)));
		int velX_fixed = s.VelX_fixed;
		int num3 = (s.GameMode & 0xF) | (int)((s.GravFlipped ? 1u : 0u) << 4) | (int)((s.Mini ? 1u : 0u) << 5) | (int)((s.OnGround ? 1u : 0u) << 6) | (int)((s.Orbed ? 1u : 0u) << 7) | (((velX_fixed switch
		{
			366 => 0, 
			571 => 1, 
			708 => 2, 
			881 => 3, 
			1065 => 4, 
			1310 => 5, 
			_ => 7, 
		}) & 7) << 8) | (int)((s.PrevInputHeld ? 1u : 0u) << 11) | (int)((s.BallFlipCooldown != 0 ? 1u : 0u) << 12);
		int num4 = s.ProcessedSprites.GetBitsHash();
		// The packed velocity field below has eleven bits. Preserve its omitted
		// high bits in the latent-state hash so distinct ship/wave/snake phases
		// cannot collapse merely because of the final packing operation.
		num4 = num4 * 397 + (num2 >> 11);
		num4 = num4 * 397 + s.GlobalSpeed_fixed;
		if (s.RobotJumpTime > 0)
			num4 = num4 * 31 + s.RobotJumpTime;
		num4 = num4 * 397 + (s.RobotJumpRequested ? 1 : 0);
		num4 = num4 * 397 + (s.AirPressLatch ? 1 : 0);
		num4 = num4 * 397 + (s.UfoOrbed ? 1 : 0);
		num4 = num4 * 397 + s.Dashing;
		if (s.Step2Ejected)
			num4 = 0;
		num4 = num4 * 397 + s.EjectU;
		num4 = num4 * 397 + s.EjectD;
		num4 = num4 * 397 + s.InvincibleCounter;
		num4 = num4 * 397 + s.SlopeWasOnCounter;
		num4 = num4 * 397 + s.SlopeFrames;
		num4 = num4 * 397 + s.SlopeType;
		num4 = num4 * 397 + s.LastSlopeType;
		num4 = num4 * 397 + (s.SlopeJumpHigher ? 1 : 0);
		num4 = num4 * 397 + s.P2_VelX_fixed;
		num4 = num4 * 397 + s.P2_SlopeWasOnCounter;
		num4 = num4 * 397 + s.P2_SlopeFrames;
		num4 = num4 * 397 + s.P2_SlopeType;
		num4 = num4 * 397 + s.P2_LastSlopeType;
		num4 = num4 * 397 + (s.P2_RobotJumpRequested ? 1 : 0);
		if (s.DualActive)
		{
			int num5 = (flag ? ((s.P2_Y_fixed >> 11) & 0x1FF) : ((s.P2_Y_fixed >> 9) & 0x1FF));
			int num6 = (flag ? ((s.P2_VelY_fixed + 32768 >> 8) & 0xFF) : ((s.P2_VelY_fixed + 32768 >> 8) & 0xFF));
			num4 = num4 * 397 + num5;
			num4 = num4 * 397 + num6;
			num4 = num4 * 397 + (s.P2_GravFlipped ? 1 : 0);
			num4 = num4 * 397 + (s.P2_PrevInputHeld ? 1 : 0);
			num4 = num4 * 397 + (s.P2_AirPressLatch ? 1 : 0);
			num4 = num4 * 397 + (s.P2_UfoOrbed ? 1 : 0);
			num4 = num4 * 397 + ((s.P2_BallFlipCooldown != 0) ? 1 : 0);
			num4 = num4 * 397 + s.P2_Dashing;
		}
		return ((long)(num4 & 0xFFFFFF) << 40) |
			((long)(num3 & 0x1FFF) << 27) |
			((long)(num2 & 0x7FF) << 16) |
			(long)(num & 0xFFFF);
	}

	private bool TryGetUpcomingBfsTransitionPortal(ref SimState s, int horizonPx,
		out BfsTransitionPortal portal)
	{
		int playerX = s.X_fixed >> 8;
		int lo = 0;
		int hi = _bfsTransitionPortals.Count;
		while (lo < hi)
		{
			int mid = (lo + hi) >> 1;
			if (_bfsTransitionPortals[mid].X < playerX)
				lo = mid + 1;
			else
				hi = mid;
		}

		for (int i = lo; i < _bfsTransitionPortals.Count; i++)
		{
			BfsTransitionPortal candidate = _bfsTransitionPortals[i];
			if (candidate.X - playerX > horizonPx)
				break;
			if (s.ProcessedSprites.Contains(candidate.ProcessKey))
				continue;

			bool changesState;
			if (candidate.TargetMode >= 0)
			{
				changesState = s.GameMode != candidate.TargetMode;
			}
			else
			{
				bool targetMini = candidate.SpriteId == 24;
				changesState = s.Mini != targetMini;
			}
			if (changesState)
			{
				portal = candidate;
				return true;
			}
		}

		portal = default;
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void BfsHashMix(ref ulong hash, int value)
	{
		hash ^= unchecked((uint)value);
		hash *= 1099511628211UL;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void BfsHashMix(ref ulong hash, long value)
	{
		BfsHashMix(ref hash, unchecked((int)value));
		BfsHashMix(ref hash, unchecked((int)(value >> 32)));
	}

	private long BfsRuntimeHash(ref SimState s)
	{
		ulong hash = 14695981039346656037UL;
		BfsHashMix(ref hash, s.ProcessedSprites.GetBitsHash64());
		BfsHashMix(ref hash, s.GlobalSpeed_fixed);
		BfsHashMix(ref hash, s.GameMode);
		BfsHashMix(ref hash, s.GravFlipped ? 1 : 0);
		BfsHashMix(ref hash, s.Mini ? 1 : 0);
		BfsHashMix(ref hash, s.OnGround ? 1 : 0);
		BfsHashMix(ref hash, s.PrevInputHeld ? 1 : 0);
		BfsHashMix(ref hash, s.WasZeroedByCollision ? 1 : 0);
		BfsHashMix(ref hash, BitConverter.DoubleToInt64Bits(s.GravityMod));
		BfsHashMix(ref hash, s.BallFlipCooldown);
		BfsHashMix(ref hash, s.BallInputBuffer);
		BfsHashMix(ref hash, s.BallCooldownFrames);
		BfsHashMix(ref hash, s.RobotJumpTime);
		BfsHashMix(ref hash, s.RobotJumpRequested ? 1 : 0);
		BfsHashMix(ref hash, s.NinjaJumps);
		BfsHashMix(ref hash, s.PendingOrbIndex);
		BfsHashMix(ref hash, s.PendingOrbSpriteId);
		BfsHashMix(ref hash, s.PendingOrbExtra1Index);
		BfsHashMix(ref hash, s.PendingOrbExtra1SpriteId);
		BfsHashMix(ref hash, s.PendingOrbExtra2Index);
		BfsHashMix(ref hash, s.PendingOrbExtra2SpriteId);
		BfsHashMix(ref hash, s.Dashing);
		BfsHashMix(ref hash, s.Orbed ? 1 : 0);
		BfsHashMix(ref hash, s.UfoOrbed ? 1 : 0);
		BfsHashMix(ref hash, s.BlackOrbed ? 1 : 0);
		BfsHashMix(ref hash, s.AirPressLatch ? 1 : 0);
		BfsHashMix(ref hash, s.JBlocked ? 1 : 0);
		BfsHashMix(ref hash, s.FBlocked ? 1 : 0);
		BfsHashMix(ref hash, s.HBlocked ? 1 : 0);
		BfsHashMix(ref hash, s.Dblocked ? 1 : 0);
		BfsHashMix(ref hash, s.Step2Ejected ? 1 : 0);
		BfsHashMix(ref hash, s.SlopeWasOnCounter);
		BfsHashMix(ref hash, s.SlopeFrames);
		BfsHashMix(ref hash, s.SlopeType);
		BfsHashMix(ref hash, s.SlopeJumpHigher ? 1 : 0);
		BfsHashMix(ref hash, s.LastSlopeType);
		BfsHashMix(ref hash, s.EjectU);
		BfsHashMix(ref hash, s.EjectD);
		BfsHashMix(ref hash, s.InvincibleCounter);
		BfsHashMix(ref hash, s.ExitPortalTimer);
		BfsHashMix(ref hash, s.NoCamLockForced ? 1 : 0);
		BfsHashMix(ref hash, s.WrapMode ? 1 : 0);
		BfsHashMix(ref hash, s.RainbowMaxMode);
		BfsHashMix(ref hash, s.RainbowUniversalActive ? 1 : 0);
		BfsHashMix(ref hash, s.RainbowSourcePortalX_px);
		BfsHashMix(ref hash, s.RainbowExitPortalX_px);
		BfsHashMix(ref hash, s.RainbowExitTargetModePlusOne);
		BfsHashMix(ref hash, s.RainbowExitPortalProcessKeyPlusOne);
		BfsHashMix(ref hash, s.RainbowExitPlayerX_fixed);
		BfsHashMix(ref hash, s.RainbowExitPlayerY_fixed);
		BfsHashMix(ref hash, s.RainbowExitVelX_fixed);
		BfsHashMix(ref hash, s.RainbowExitVelY_fixed);
		BfsHashMix(ref hash, s.RainbowExitStateFlags);
		BfsHashMix(ref hash, s.RainbowExitDashing);
		BfsHashMix(ref hash, s.RainbowBranchEnded ? 1 : 0);
		BfsHashMix(ref hash, s.DualActive ? 1 : 0);

		if (s.DualActive)
		{
			BfsHashMix(ref hash, s.P2_Y_fixed);
			BfsHashMix(ref hash, s.P2_VelX_fixed);
			BfsHashMix(ref hash, s.P2_VelY_fixed);
			BfsHashMix(ref hash, s.P2_GravFlipped ? 1 : 0);
			BfsHashMix(ref hash, s.P2_Mini ? 1 : 0);
			BfsHashMix(ref hash, s.P2_WasZeroedByCollision ? 1 : 0);
			BfsHashMix(ref hash, s.P2_OnGround ? 1 : 0);
			BfsHashMix(ref hash, s.P2_BallFlipCooldown);
			BfsHashMix(ref hash, s.P2_BallInputBuffer);
			BfsHashMix(ref hash, s.P2_BallCooldownFrames);
			BfsHashMix(ref hash, s.P2_RobotJumpTime);
			BfsHashMix(ref hash, s.P2_RobotJumpRequested ? 1 : 0);
			BfsHashMix(ref hash, s.P2_NinjaJumps);
			BfsHashMix(ref hash, s.P2_SlopeWasOnCounter);
			BfsHashMix(ref hash, s.P2_SlopeFrames);
			BfsHashMix(ref hash, s.P2_SlopeType);
			BfsHashMix(ref hash, s.P2_LastSlopeType);
			BfsHashMix(ref hash, s.P2_Orbed ? 1 : 0);
			BfsHashMix(ref hash, s.P2_UfoOrbed ? 1 : 0);
			BfsHashMix(ref hash, s.P2_BlackOrbed ? 1 : 0);
			BfsHashMix(ref hash, s.P2_AirPressLatch ? 1 : 0);
			BfsHashMix(ref hash, s.P2_PrevInputHeld ? 1 : 0);
			BfsHashMix(ref hash, s.P2_Dashing);
			BfsHashMix(ref hash, s.P2_JBlocked ? 1 : 0);
			BfsHashMix(ref hash, s.P2_FBlocked ? 1 : 0);
			BfsHashMix(ref hash, s.P2_HBlocked ? 1 : 0);
			BfsHashMix(ref hash, s.P2_Dblocked ? 1 : 0);
			BfsHashMix(ref hash, s.P2_PendingOrbIndex);
			BfsHashMix(ref hash, s.P2_PendingOrbSpriteId);
			BfsHashMix(ref hash, s.P2_PendingOrbExtra1Index);
			BfsHashMix(ref hash, s.P2_PendingOrbExtra1SpriteId);
			BfsHashMix(ref hash, s.P2_PendingOrbExtra2Index);
			BfsHashMix(ref hash, s.P2_PendingOrbExtra2SpriteId);
			BfsHashMix(ref hash, s.P2_SingleExitCaptured ? 1 : 0);
			BfsHashMix(ref hash, s.P2_SingleExitY_fixed);
			BfsHashMix(ref hash, s.P2_SingleExitVelY_fixed);
			BfsHashMix(ref hash, s.P2_SingleExitGravFlipped ? 1 : 0);
		}

		BfsHashMix(ref hash, s.TeleportOutputY_px);
		if (s.NesSlots != null)
		{
			for (int i = 0; i < s.NesSlots.Length; i++)
			{
				BfsHashMix(ref hash, s.NesSlots[i]);
				BfsHashMix(ref hash, (s.NesSlotDead[i] ? 1 : 0) | (s.NesSlotActive[i] ? 2 : 0));
				BfsHashMix(ref hash, s.NesSlotWorldY[i]);
				BfsHashMix(ref hash, s.NesSlotRealX[i]);
				BfsHashMix(ref hash, s.NesSlotRealY[i]);
			}
		}
		if (s.CoinTimer != null)
		{
			for (int i = 0; i < s.CoinTimer.Length; i++)
			{
				BfsHashMix(ref hash, s.CoinTimer[i]);
				BfsHashMix(ref hash, s.CoinSpeed[i]);
			}
		}
		BfsHashMix(ref hash, s.CoinAnimating ? 1 : 0);
		if (s.RainbowShadows != null)
		{
			// Every random-mode outcome is future-observable.  Hash the complete
			// ordered ensemble using the same physical/runtime identity as the main
			// BFS key so dedup cannot discard a different set of surviving modes.
			BfsHashMix(ref hash, s.RainbowShadows.Length);
			for (int i = 0; i < s.RainbowShadows.Length; i++)
			{
				SimState shadow = s.RainbowShadows[i];
				BfsHashMix(ref hash, BfsQuantizeKey(ref shadow));
				BfsHashMix(ref hash, shadow.X_fixed);
				BfsHashMix(ref hash, shadow.ScrollX_px);
				BfsHashMix(ref hash, shadow.CurrXScrollStop_fixed);
				BfsHashMix(ref hash, shadow.TargetXScrollStop_fixed);
				BfsHashMix(ref hash, shadow.CameraY_fixed);
				BfsHashMix(ref hash, shadow.TargetCameraY_fixed);
				BfsHashMix(ref hash, shadow.ScrollYSubpx);
				BfsHashMix(ref hash, shadow.NesSprDataPtr);
				BfsHashMix(ref hash, BfsRuntimeHash(ref shadow));
			}
		}
		return unchecked((long)hash);
	}

	private BfsDedupKey BuildBfsDedupKey(ref SimState s)
	{
		int searchRoute = ForcePlatformer ? BfsPlatformerDirectionClass(in s) : 0;
		return new BfsDedupKey(
			BfsQuantizeKey(ref s),
			s.X_fixed,
			s.ScrollX_px,
			s.CurrXScrollStop_fixed,
			s.TargetXScrollStop_fixed,
			s.CameraY_fixed,
			s.TargetCameraY_fixed,
			s.ScrollYSubpx,
			s.NesSprDataPtr,
			searchRoute,
			BfsRuntimeHash(ref s));
	}

	private void CollapseEquivalentRainbowBranches(List<SimState> branches)
	{
		if (branches.Count <= 1)
			return;

		// Random outcomes are a set of possible current states, not permanent
		// identities. Once two outcomes have the same complete BFS state, every
		// future common input affects them identically. Keeping both only multiplies
		// work at every later frame and at every later random portal.
		Dictionary<BfsDedupKey, int> seen = new(branches.Count);
		int write = 0;
		for (int read = 0; read < branches.Count; read++)
		{
			SimState branch = branches[read];
			BfsDedupKey key = BuildBfsDedupKey(ref branch);
			if (seen.TryGetValue(key, out int existingIndex))
			{
				SimState existing = branches[existingIndex];
				KeepWorseBfsTimingHistory(ref existing, in branch);
				branches[existingIndex] = existing;
				branch.ReturnAllSpriteResources();
				continue;
			}
			seen[key] = write;
			branches[write++] = branch;
		}
		if (write < branches.Count)
			branches.RemoveRange(write, branches.Count - write);
		if (write == 1)
		{
			SimState merged = branches[0];
			ClearRainbowUniversalState(ref merged);
			branches[0] = merged;
		}
	}

	private static void ClearRainbowUniversalState(ref SimState state)
	{
		state.RainbowMaxMode = 0;
		state.RainbowUniversalActive = false;
		state.RainbowSourcePortalX_px = 0;
		state.RainbowExitPortalX_px = 0;
		state.RainbowExitTargetModePlusOne = 0;
		state.RainbowExitPortalProcessKeyPlusOne = 0;
		state.RainbowExitPlayerX_fixed = 0;
		state.RainbowExitPlayerY_fixed = 0;
		state.RainbowExitVelX_fixed = 0;
		state.RainbowExitVelY_fixed = 0;
		state.RainbowExitStateFlags = 0;
		state.RainbowExitDashing = 0;
	}

	private static bool RainbowIntArraysEqual(int[]? a, int[]? b)
	{
		if (ReferenceEquals(a, b)) return true;
		if (a == null || b == null || a.Length != b.Length) return false;
		for (int i = 0; i < a.Length; i++)
		{
			if (a[i] != b[i]) return false;
		}
		return true;
	}

	private static bool RainbowBoolArraysEqual(bool[]? a, bool[]? b)
	{
		if (ReferenceEquals(a, b)) return true;
		if (a == null || b == null || a.Length != b.Length) return false;
		for (int i = 0; i < a.Length; i++)
		{
			if (a[i] != b[i]) return false;
		}
		return true;
	}

	private static bool BfsRainbowExitStatesMatch(in SimState a,
		in SimState b)
	{
		// This is deliberately exact.  A shared pixel with a different velocity,
		// latch, camera, or live NES slot is not convergence; it can diverge on the
		// very next frame.  Only permanent ProcessedSprites history is considered
		// separately, where records wholly behind the player are no longer visible.
		if (a.X_fixed != b.X_fixed || a.Y_fixed != b.Y_fixed ||
			a.VelY_fixed != b.VelY_fixed || a.VelX_fixed != b.VelX_fixed ||
			a.GlobalSpeed_fixed != b.GlobalSpeed_fixed ||
			a.GameMode != b.GameMode || a.GravFlipped != b.GravFlipped ||
			a.GravFlippedAtFrameStart != b.GravFlippedAtFrameStart ||
			a.OrbUseFrameStartGravitySign != b.OrbUseFrameStartGravitySign ||
			a.Mini != b.Mini || a.GravMul != b.GravMul ||
			BitConverter.DoubleToInt64Bits(a.GravityMod) !=
				BitConverter.DoubleToInt64Bits(b.GravityMod) ||
			a.WasZeroedByCollision != b.WasZeroedByCollision ||
			a.OnGround != b.OnGround ||
			a.BallFlipCooldown != b.BallFlipCooldown ||
			a.BallInputBuffer != b.BallInputBuffer ||
			a.BallCooldownFrames != b.BallCooldownFrames ||
			a.RobotJumpTime != b.RobotJumpTime ||
			a.RobotJumpRequested != b.RobotJumpRequested ||
			a.NinjaJumps != b.NinjaJumps ||
			a.PendingOrbIndex != b.PendingOrbIndex ||
			a.PendingOrbSpriteId != b.PendingOrbSpriteId ||
			a.PendingOrbExtra1Index != b.PendingOrbExtra1Index ||
			a.PendingOrbExtra1SpriteId != b.PendingOrbExtra1SpriteId ||
			a.PendingOrbExtra2Index != b.PendingOrbExtra2Index ||
			a.PendingOrbExtra2SpriteId != b.PendingOrbExtra2SpriteId ||
			a.Dashing != b.Dashing || a.Orbed != b.Orbed ||
			a.UfoOrbed != b.UfoOrbed || a.BlackOrbed != b.BlackOrbed ||
			a.AirPressLatch != b.AirPressLatch ||
			a.PrevInputHeld != b.PrevInputHeld ||
			a.JBlocked != b.JBlocked || a.FBlocked != b.FBlocked ||
			a.HBlocked != b.HBlocked || a.Dblocked != b.Dblocked ||
			a.Step2Ejected != b.Step2Ejected || a.Step2Ever != b.Step2Ever ||
			a.SlopeWasOnCounter != b.SlopeWasOnCounter ||
			a.SlopeFrames != b.SlopeFrames || a.SlopeType != b.SlopeType ||
			a.SlopeJumpHigher != b.SlopeJumpHigher ||
			a.LastSlopeType != b.LastSlopeType || a.EjectU != b.EjectU ||
			a.EjectD != b.EjectD || a.InvincibleCounter != b.InvincibleCounter ||
			a.CameraY_fixed != b.CameraY_fixed ||
			a.ScrollYSubpx != b.ScrollYSubpx ||
			a.TargetCameraY_fixed != b.TargetCameraY_fixed ||
			a.ExitPortalTimer != b.ExitPortalTimer ||
			a.NoCamLockForced != b.NoCamLockForced || a.WrapMode != b.WrapMode ||
			a.RainbowBranchEnded != b.RainbowBranchEnded ||
			a.DualActive != b.DualActive ||
			a.TeleportOutputY_px != b.TeleportOutputY_px ||
			a.NesSprDataPtr != b.NesSprDataPtr ||
			a.CoinAnimating != b.CoinAnimating)
		{
			return false;
		}

		if (a.DualActive &&
			(a.P2_Y_fixed != b.P2_Y_fixed ||
			a.P2_VelX_fixed != b.P2_VelX_fixed ||
			a.P2_VelY_fixed != b.P2_VelY_fixed ||
			a.P2_GravFlipped != b.P2_GravFlipped ||
			a.P2_GravMul != b.P2_GravMul || a.P2_Mini != b.P2_Mini ||
			a.P2_WasZeroedByCollision != b.P2_WasZeroedByCollision ||
			a.P2_OnGround != b.P2_OnGround ||
			a.P2_BallFlipCooldown != b.P2_BallFlipCooldown ||
			a.P2_BallInputBuffer != b.P2_BallInputBuffer ||
			a.P2_BallCooldownFrames != b.P2_BallCooldownFrames ||
			a.P2_RobotJumpTime != b.P2_RobotJumpTime ||
			a.P2_RobotJumpRequested != b.P2_RobotJumpRequested ||
			a.P2_NinjaJumps != b.P2_NinjaJumps ||
			a.P2_SlopeWasOnCounter != b.P2_SlopeWasOnCounter ||
			a.P2_SlopeFrames != b.P2_SlopeFrames ||
			a.P2_SlopeType != b.P2_SlopeType ||
			a.P2_LastSlopeType != b.P2_LastSlopeType ||
			a.P2_Orbed != b.P2_Orbed || a.P2_UfoOrbed != b.P2_UfoOrbed ||
			a.P2_BlackOrbed != b.P2_BlackOrbed ||
			a.P2_AirPressLatch != b.P2_AirPressLatch ||
			a.P2_PrevInputHeld != b.P2_PrevInputHeld ||
			a.P2_Dashing != b.P2_Dashing ||
			a.P2_JBlocked != b.P2_JBlocked || a.P2_FBlocked != b.P2_FBlocked ||
			a.P2_HBlocked != b.P2_HBlocked || a.P2_Dblocked != b.P2_Dblocked ||
			a.P2_PendingOrbIndex != b.P2_PendingOrbIndex ||
			a.P2_PendingOrbSpriteId != b.P2_PendingOrbSpriteId ||
			a.P2_PendingOrbExtra1Index != b.P2_PendingOrbExtra1Index ||
			a.P2_PendingOrbExtra1SpriteId != b.P2_PendingOrbExtra1SpriteId ||
			a.P2_PendingOrbExtra2Index != b.P2_PendingOrbExtra2Index ||
			a.P2_PendingOrbExtra2SpriteId != b.P2_PendingOrbExtra2SpriteId ||
			a.P2_SingleExitCaptured != b.P2_SingleExitCaptured ||
			a.P2_SingleExitY_fixed != b.P2_SingleExitY_fixed ||
			a.P2_SingleExitVelY_fixed != b.P2_SingleExitVelY_fixed ||
			a.P2_SingleExitGravFlipped != b.P2_SingleExitGravFlipped ||
			a.P2_SingleExitGravMul != b.P2_SingleExitGravMul))
		{
			return false;
		}

		return RainbowIntArraysEqual(a.NesSlots, b.NesSlots) &&
			RainbowBoolArraysEqual(a.NesSlotDead, b.NesSlotDead) &&
			RainbowBoolArraysEqual(a.NesSlotActive, b.NesSlotActive) &&
			RainbowIntArraysEqual(a.NesSlotWorldY, b.NesSlotWorldY) &&
			RainbowIntArraysEqual(a.NesSlotRealX, b.NesSlotRealX) &&
			RainbowIntArraysEqual(a.NesSlotRealY, b.NesSlotRealY) &&
			RainbowIntArraysEqual(a.CoinTimer, b.CoinTimer) &&
			RainbowIntArraysEqual(a.CoinSpeed, b.CoinSpeed);
	}

	private bool BfsRainbowFutureHistoryMatches(in SimState a, in SimState b)
	{
		// Different modes intentionally leave different activated records behind.
		// Those bits are historical once exact player/camera state has converged;
		// sprites ahead cannot have been touched yet. Coins remain observable search
		// state, so coin preference still requires identical collection history.
		if (PreferCoins)
		{
			foreach (SpriteEntry coin in allCoins)
			{
				if (a.ProcessedSprites.Contains(coin.Index) !=
					b.ProcessedSprites.Contains(coin.Index))
				{
					return false;
				}
			}
		}
		return true;
	}

	private static bool TryGetCommonRainbowExit(List<SimState> branches,
		out int exitX, out int targetModePlusOne)
	{
		exitX = 0;
		targetModePlusOne = 0;
		if (branches.Count <= 1) return false;

		SimState first = branches[0];
		if (!first.RainbowUniversalActive ||
			first.RainbowExitTargetModePlusOne == 0 ||
			first.RainbowExitPortalX_px <= first.RainbowSourcePortalX_px)
		{
			return false;
		}
		exitX = first.RainbowExitPortalX_px;
		targetModePlusOne = first.RainbowExitTargetModePlusOne;
		for (int i = 1; i < branches.Count; i++)
		{
			SimState branch = branches[i];
			if (!branch.RainbowUniversalActive ||
				branch.RainbowSourcePortalX_px != first.RainbowSourcePortalX_px ||
				branch.RainbowExitPortalX_px != exitX ||
				branch.RainbowExitTargetModePlusOne != targetModePlusOne)
			{
				return false;
			}
		}
		return true;
	}

	private static bool BfsRainbowExitRecordsMatch(in SimState a,
		in SimState b)
	{
		return a.RainbowExitPortalX_px == b.RainbowExitPortalX_px &&
			a.RainbowExitTargetModePlusOne == b.RainbowExitTargetModePlusOne &&
			a.RainbowExitPortalProcessKeyPlusOne ==
				b.RainbowExitPortalProcessKeyPlusOne &&
			a.RainbowExitPlayerX_fixed == b.RainbowExitPlayerX_fixed &&
			a.RainbowExitPlayerY_fixed == b.RainbowExitPlayerY_fixed &&
			a.RainbowExitVelX_fixed == b.RainbowExitVelX_fixed &&
			a.RainbowExitVelY_fixed == b.RainbowExitVelY_fixed &&
			a.RainbowExitStateFlags == b.RainbowExitStateFlags &&
			a.RainbowExitDashing == b.RainbowExitDashing;
	}

	private static bool BfsRainbowExitRecordsCompatible(
		List<SimState> branches)
	{
		int firstExited = -1;
		for (int i = 0; i < branches.Count; i++)
		{
			if (branches[i].RainbowExitTargetModePlusOne == 0) continue;
			if (firstExited < 0)
			{
				firstExited = i;
				continue;
			}
			SimState first = branches[firstExited];
			SimState branch = branches[i];
			if (!BfsRainbowExitRecordsMatch(in first, in branch)) return false;
		}
		return true;
	}

	private static bool BfsRainbowCurrentCoreStatesMatch(in SimState a,
		in SimState b)
	{
		// A random mode's collision/ejection/slope bytes remain live after the
		// deterministic exit portal.  Pyrophoric's ship outcome demonstrated that
		// matching X/Y/velocity for a frame is not enough: a stale byte changed the
		// following collision snap by one pixel and the later cube jump diverged.
		// Collapse only after every future-observable NES/PF state field agrees.
		return BfsRainbowExitStatesMatch(in a, in b);
	}

	private bool TryCollapseConvergedRainbowBranches(List<SimState> branches)
	{
		if (!TryGetCommonRainbowExit(branches, out int exitX, out _)) return false;

		SimState representative = branches[0];
		if ((representative.X_fixed >> 8) <= exitX + 32) return false;
		for (int i = 1; i < branches.Count; i++)
		{
			SimState branch = branches[i];
			if (!BfsRainbowCurrentCoreStatesMatch(in representative, in branch) ||
				!BfsRainbowFutureHistoryMatches(in representative, in branch))
			{
				return false;
			}
			KeepWorseBfsTimingHistory(ref representative, in branch);
		}

		for (int i = 1; i < branches.Count; i++)
			branches[i].ReturnAllSpriteResources();
		branches.RemoveRange(1, branches.Count - 1);
		ClearRainbowUniversalState(ref representative);
		branches[0] = representative;
		return true;
	}

	private bool TryGetNextBfsRainbowPortal(ref SimState s,
		out BfsRainbowPortal portal)
	{
		int playerX = s.X_fixed >> 8;
		int lo = 0;
		int hi = _bfsRainbowPortals.Count;
		while (lo < hi)
		{
			int mid = (lo + hi) >> 1;
			if (_bfsRainbowPortals[mid].X < playerX)
				lo = mid + 1;
			else
				hi = mid;
		}
		for (int i = lo; i < _bfsRainbowPortals.Count; i++)
		{
			BfsRainbowPortal candidate = _bfsRainbowPortals[i];
			if (s.ProcessedSprites.Contains(candidate.ProcessKey))
				continue;
			portal = candidate;
			return true;
		}
		portal = default;
		return false;
	}

	private int BfsRainbowSearchOutcomeCount(List<SimState> candidates,
		List<int> candidateIndexes, out int nextPortalX)
	{
		// Keep the rainbow-specific beam local to the random-mode segment.  Applying
		// its much smaller frontier cap from the beginning of a level can discard the
		// only prefix that reaches the portal (especially on long levels such as
		// ExtraordinaryExcitement).  One screen is too little preparation for paths
		// that must line up several modes; 1024 px still gives the universal selector
		// hundreds of frames to diversify before activation without pruning the rest
		// of the level.
		const int RainbowPreparationHorizonPx = 1024;
		nextPortalX = -1;
		int outcomes = 0;
		foreach (int candidateIndex in candidateIndexes)
		{
			SimState candidate = candidates[candidateIndex];
			if (candidate.RainbowShadows != null)
				outcomes = Math.Max(outcomes, 1 + candidate.RainbowShadows.Length);
		}
		if (outcomes > 0 || candidateIndexes.Count == 0)
			return outcomes;

		// Shortly before the first random portal, reduce and diversify the prefix
		// frontier as well. Otherwise 120k prefixes each become an eight/twelve-state
		// deep clone on the activation frame, exhausting time and memory before the
		// universal section can make meaningful progress.
		int sampleCount = Math.Min(128, candidateIndexes.Count);
		for (int sample = 0; sample < sampleCount; sample++)
		{
			int pos = (int)((long)sample * candidateIndexes.Count / sampleCount);
			SimState candidate = candidates[candidateIndexes[pos]];
			if (!TryGetNextBfsRainbowPortal(ref candidate, out BfsRainbowPortal portal))
				continue;
			int distanceToPortal = portal.X - (candidate.X_fixed >> 8);
			if (distanceToPortal > RainbowPreparationHorizonPx)
				continue;
			outcomes = Math.Max(outcomes, portal.ModeCount);
			if (nextPortalX < 0 || portal.X < nextPortalX)
				nextPortalX = portal.X;
		}
		return outcomes;
	}

	private static int BfsRainbowFrontierCap(int outcomeCount)
	{
		if (int.TryParse(Environment.GetEnvironmentVariable(
			"FAMIDASH_RAINBOW_CAP"), out int diagnosticCap))
		{
			return Math.Clamp(diagnosticCap, 1024, 65536);
		}
		// Bound work by simulated mode states, rather than by ensemble count.
		// Expansion tests both inputs, so 65,536 retained branch states produces
		// at most about 131k StepFrame calls per BFS frame.
		const int BranchStateBudget = 65536;
		return Math.Clamp(BranchStateBudget / Math.Max(1, outcomeCount), 1024, 8192);
	}

	private long BfsRainbowLocalPhase(ref SimState s)
	{
		ulong hash = 14695981039346656037UL;
		int screenY = SharedPhysics.NesPlayerScreenY_px(
			s.Y_fixed, s.CameraY_fixed);
		int motion = (s.GravFlipped ? 1 : 0) |
			(s.Mini ? 2 : 0) |
			(s.OnGround ? 4 : 0) |
			(s.PrevInputHeld ? 8 : 0) |
			(s.AirPressLatch ? 16 : 0) |
			(s.Orbed ? 32 : 0) |
			(s.UfoOrbed ? 64 : 0) |
			(s.BlackOrbed ? 128 : 0);
		BfsHashMix(ref hash, s.GameMode);
		BfsHashMix(ref hash, screenY >> 2);
		BfsHashMix(ref hash, s.VelY_fixed >> 5);
		BfsHashMix(ref hash, s.X_fixed & 0x1FF);
		BfsHashMix(ref hash, s.VelX_fixed);
		BfsHashMix(ref hash, motion);
		BfsHashMix(ref hash, s.Dashing);
		BfsHashMix(ref hash, s.RobotJumpTime);
		BfsHashMix(ref hash, s.NinjaJumps);
		BfsHashMix(ref hash, s.BallFlipCooldown | (s.BallInputBuffer << 8));
		BfsHashMix(ref hash, s.SlopeType | (s.LastSlopeType << 8));
		BfsHashMix(ref hash, Math.Min(s.SlopeFrames, 15) |
			(Math.Min(s.SlopeWasOnCounter, 15) << 4));
		BfsHashMix(ref hash, s.EjectU | (s.EjectD << 8));
		BfsHashMix(ref hash, s.PendingOrbIndex);
		BfsHashMix(ref hash, s.PendingOrbSpriteId);
		BfsHashMix(ref hash, s.CameraY_fixed >> 10);
		BfsHashMix(ref hash, s.TargetCameraY_fixed >> 10);
		BfsHashMix(ref hash, s.ScrollYSubpx);
		BfsHashMix(ref hash, s.NesSprDataPtr >> 2);
		if (s.GameMode == 6 || s.GameMode == 10)
		{
			// Wave and snake corridors can depend on exact diagonal subpixel phase.
			BfsHashMix(ref hash, s.Y_fixed & 0xFF);
			BfsHashMix(ref hash, s.VelY_fixed & 0xFF);
		}
		return unchecked((long)hash);
	}

	private static int BfsRainbowBranchCount(ref SimState s)
	{
		return 1 + (s.RainbowShadows?.Length ?? 0);
	}

	private long BfsRainbowBranchPhase(ref SimState candidate, int branch)
	{
		if (branch == 0)
		{
			SimState main = candidate;
			main.RainbowShadows = null;
			return BfsRainbowLocalPhase(ref main);
		}
		SimState shadow = candidate.RainbowShadows![branch - 1];
		return BfsRainbowLocalPhase(ref shadow);
	}

	private static int BfsRainbowBranchPortalDistance(ref SimState branch,
		in BfsTransitionPortal portal)
	{
		int wave = branch.GameMode == 6 || branch.GameMode == 10 ? 1 : 0;
		int hitboxH = wave != 0 ? 8 : GetHitboxH(branch.Mini);
		int hitboxOffsetY = wave != 0
			? 0
			: GetHitboxOffsetY(branch.GameMode, branch.Mini,
				branch.GravFlipped);
		int playerCenterY = (branch.Y_fixed >> 8) + hitboxOffsetY +
			hitboxH / 2;
		return Math.Abs(playerCenterY - portal.HitCenterY);
	}

	private static bool BfsRainbowBranchNeedsSingleTransition(
		ref SimState branch, in BfsTransitionPortal portal)
	{
		if (branch.RainbowBranchEnded ||
			branch.ProcessedSprites.Contains(portal.ProcessKey))
		{
			return false;
		}
		if (portal.TargetMode >= 0)
			return branch.GameMode != portal.TargetMode;
		bool targetMini = portal.SpriteId == 24;
		return branch.Mini != targetMini;
	}

	private static bool BfsRainbowSameTransitionCluster(
		in BfsTransitionPortal a, in BfsTransitionPortal b)
	{
		return a.X == b.X && a.TargetMode == b.TargetMode &&
			(a.TargetMode >= 0 || a.SpriteId == b.SpriteId);
	}

	private static bool BfsRainbowBranchReachedTransitionCluster(
		ref SimState branch, List<BfsTransitionPortal> portals,
		int xBlockStart, int xBlockEnd, in BfsTransitionPortal cluster)
	{
		if (branch.RainbowBranchEnded) return true;
		if (cluster.TargetMode >= 0 &&
			branch.RainbowExitPortalX_px == cluster.X &&
			branch.RainbowExitTargetModePlusOne == cluster.TargetMode + 1)
		{
			return true;
		}
		for (int i = xBlockStart; i < xBlockEnd; i++)
		{
			BfsTransitionPortal portal = portals[i];
			if (!BfsRainbowSameTransitionCluster(in portal, in cluster)) continue;
			if (branch.ProcessedSprites.Contains(portal.ProcessKey)) return true;
		}
		return false;
	}

	private static bool BfsRainbowBranchReachedExactTransition(
		ref SimState branch, in BfsTransitionPortal portal)
	{
		if (branch.RainbowBranchEnded) return true;
		if (branch.RainbowExitPortalProcessKeyPlusOne == portal.ProcessKey + 1)
			return true;
		return branch.ProcessedSprites.Contains(portal.ProcessKey);
	}

	private static int BfsRainbowBranchTransitionClusterDistance(
		ref SimState branch, List<BfsTransitionPortal> portals,
		int xBlockStart, int xBlockEnd, in BfsTransitionPortal cluster)
	{
		int best = int.MaxValue;
		for (int i = xBlockStart; i < xBlockEnd; i++)
		{
			BfsTransitionPortal portal = portals[i];
			if (!BfsRainbowSameTransitionCluster(in portal, in cluster)) continue;
			best = Math.Min(best,
				BfsRainbowBranchPortalDistance(ref branch, in portal));
		}
		return best == int.MaxValue ? 0 : best;
	}

	private static bool TryGetCommonRainbowExit(ref SimState candidate,
		out int exitX, out int targetModePlusOne)
	{
		exitX = 0;
		targetModePlusOne = 0;
		if (candidate.RainbowShadows == null ||
			!candidate.RainbowUniversalActive ||
			candidate.RainbowExitTargetModePlusOne == 0 ||
			candidate.RainbowExitPortalX_px <= candidate.RainbowSourcePortalX_px)
		{
			return false;
		}
		exitX = candidate.RainbowExitPortalX_px;
		targetModePlusOne = candidate.RainbowExitTargetModePlusOne;
		for (int i = 0; i < candidate.RainbowShadows.Length; i++)
		{
			SimState shadow = candidate.RainbowShadows[i];
			if (!shadow.RainbowUniversalActive ||
				shadow.RainbowSourcePortalX_px != candidate.RainbowSourcePortalX_px ||
				shadow.RainbowExitPortalX_px != exitX ||
				shadow.RainbowExitTargetModePlusOne != targetModePlusOne)
			{
				return false;
			}
		}
		return true;
	}

	private static int BfsRainbowPostExitConvergenceCost(ref SimState candidate)
	{
		int minX = candidate.X_fixed;
		int maxX = minX;
		int minY = candidate.Y_fixed;
		int maxY = minY;
		int minVelY = candidate.VelY_fixed;
		int maxVelY = minVelY;
		int minCameraY = candidate.CameraY_fixed;
		int maxCameraY = minCameraY;
		int minTargetY = candidate.TargetCameraY_fixed;
		int maxTargetY = minTargetY;
		int stateMismatch = 0;
		SimState reference = candidate;
		reference.RainbowShadows = null;
		SimState[] shadows = candidate.RainbowShadows!;
		for (int i = 0; i < shadows.Length; i++)
		{
			SimState branch = shadows[i];
			minX = Math.Min(minX, branch.X_fixed);
			maxX = Math.Max(maxX, branch.X_fixed);
			minY = Math.Min(minY, branch.Y_fixed);
			maxY = Math.Max(maxY, branch.Y_fixed);
			minVelY = Math.Min(minVelY, branch.VelY_fixed);
			maxVelY = Math.Max(maxVelY, branch.VelY_fixed);
			minCameraY = Math.Min(minCameraY, branch.CameraY_fixed);
			maxCameraY = Math.Max(maxCameraY, branch.CameraY_fixed);
			minTargetY = Math.Min(minTargetY, branch.TargetCameraY_fixed);
			maxTargetY = Math.Max(maxTargetY, branch.TargetCameraY_fixed);
			if (branch.GameMode != reference.GameMode ||
				branch.GravFlipped != reference.GravFlipped ||
				branch.Mini != reference.Mini ||
				branch.OnGround != reference.OnGround ||
				branch.PrevInputHeld != reference.PrevInputHeld ||
				branch.AirPressLatch != reference.AirPressLatch ||
				branch.Dashing != reference.Dashing)
			{
				stateMismatch++;
			}
		}
		long rawCost = (long)(maxY - minY) * 256L +
			(long)(maxVelY - minVelY) * 16L +
			(long)(maxX - minX) * 256L +
			(long)(maxCameraY - minCameraY) / 16L +
			(long)(maxTargetY - minTargetY) / 16L +
			(long)stateMismatch * 100_000_000L;
		return (int)Math.Min(int.MaxValue - 1L, rawCost);
	}

	private bool TryGetBfsRainbowConvergenceCost(ref SimState candidate,
		int horizonPx, out int cost)
	{
		cost = int.MaxValue;
		if (candidate.RainbowShadows == null)
			return false;
		SimState[] shadows = candidate.RainbowShadows;
		if (TryGetCommonRainbowExit(ref candidate, out _, out _))
		{
			cost = BfsRainbowPostExitConvergenceCost(ref candidate);
			return true;
		}

		int playerX = candidate.X_fixed >> 8;
		int sourceX = candidate.RainbowUniversalActive
			? candidate.RainbowSourcePortalX_px
			: playerX;
		int lo = 0;
		int hi = _bfsTransitionPortals.Count;
		while (lo < hi)
		{
			int mid = (lo + hi) >> 1;
			if (_bfsTransitionPortals[mid].X <= sourceX)
				lo = mid + 1;
			else
				hi = mid;
		}
		for (int xBlockStart = lo;
			xBlockStart < _bfsTransitionPortals.Count;)
		{
			int xBlockEnd = xBlockStart + 1;
			int portalX = _bfsTransitionPortals[xBlockStart].X;
			while (xBlockEnd < _bfsTransitionPortals.Count &&
				_bfsTransitionPortals[xBlockEnd].X == portalX)
			{
				xBlockEnd++;
			}
			if (portalX - playerX > horizonPx)
				break;

			for (int clusterIndex = xBlockStart;
				clusterIndex < xBlockEnd; clusterIndex++)
			{
				BfsTransitionPortal cluster =
					_bfsTransitionPortals[clusterIndex];
				bool duplicateIdentity = false;
				for (int prior = xBlockStart; prior < clusterIndex; prior++)
				{
					BfsTransitionPortal priorPortal =
						_bfsTransitionPortals[prior];
					if (BfsRainbowSameTransitionCluster(
						in priorPortal, in cluster))
					{
						duplicateIdentity = true;
						break;
					}
				}
				if (duplicateIdentity) continue;
				int clusterCopyCount = 0;
				for (int copy = xBlockStart; copy < xBlockEnd; copy++)
				{
					BfsTransitionPortal copyPortal =
						_bfsTransitionPortals[copy];
					if (BfsRainbowSameTransitionCluster(
						in copyPortal, in cluster))
					{
						clusterCopyCount++;
					}
				}

				SimState main = candidate;
				main.RainbowShadows = null;
				if (clusterCopyCount == 1)
				{
					int maxDistance = 0;
					int totalDistance = 0;
					int neededBranches = 0;
					if (portalX >= playerX &&
						BfsRainbowBranchNeedsSingleTransition(ref main, in cluster))
					{
						int distance = BfsRainbowBranchPortalDistance(
							ref main, in cluster);
						maxDistance = distance;
						totalDistance = distance;
						neededBranches = 1;
					}
					for (int i = 0; i < shadows.Length; i++)
					{
						SimState shadow = shadows[i];
						if (portalX < playerX ||
							!BfsRainbowBranchNeedsSingleTransition(ref shadow, in cluster))
						{
							continue;
						}
						int distance = BfsRainbowBranchPortalDistance(
							ref shadow, in cluster);
						maxDistance = Math.Max(maxDistance, distance);
						totalDistance += distance;
						neededBranches++;
					}
					if (neededBranches == 0) continue;
					cost = maxDistance * 4096 + totalDistance;
					return true;
				}

				// Vertically duplicated copies provide coverage for different random
				// modes. Before every branch has crossed the cluster, guide each mode
				// toward its nearest reachable copy. Exact shared-state convergence is
				// enforced in the post-exit segment below, after all are ships/balls/etc.
				int maxClusterDistance = 0;
				int totalClusterDistance = 0;
				int clusterNeededBranches = 0;
				if (!BfsRainbowBranchReachedTransitionCluster(ref main,
					_bfsTransitionPortals, xBlockStart, xBlockEnd, in cluster))
				{
					int distance = BfsRainbowBranchTransitionClusterDistance(
						ref main, _bfsTransitionPortals, xBlockStart,
						xBlockEnd, in cluster);
					maxClusterDistance = distance;
					totalClusterDistance = distance;
					clusterNeededBranches = 1;
				}
				for (int i = 0; i < shadows.Length; i++)
				{
					SimState shadow = shadows[i];
					if (BfsRainbowBranchReachedTransitionCluster(ref shadow,
						_bfsTransitionPortals, xBlockStart, xBlockEnd, in cluster))
					{
						continue;
					}
					int distance = BfsRainbowBranchTransitionClusterDistance(
						ref shadow, _bfsTransitionPortals, xBlockStart,
						xBlockEnd, in cluster);
					maxClusterDistance = Math.Max(maxClusterDistance, distance);
					totalClusterDistance += distance;
					clusterNeededBranches++;
				}
				if (clusterNeededBranches == 0) continue;
				cost = maxClusterDistance * 4096 + totalClusterDistance;
				return true;
			}
			xBlockStart = xBlockEnd;
		}
		return false;
	}

	private long BfsRainbowEnsemblePhase(ref SimState candidate)
	{
		ulong hash = 14695981039346656037UL;
		int branchCount = BfsRainbowBranchCount(ref candidate);
		BfsHashMix(ref hash, branchCount);
		SimState main = candidate;
		main.RainbowShadows = null;
		BfsHashMix(ref hash, BfsRainbowLocalPhase(ref main));
		if (candidate.RainbowShadows != null)
		{
			for (int i = 0; i < candidate.RainbowShadows.Length; i++)
			{
				SimState shadow = candidate.RainbowShadows[i];
				BfsHashMix(ref hash, BfsRainbowLocalPhase(ref shadow));
			}
		}
		return unchecked((long)hash);
	}

	private void BfsRainbowRecordLocalPhases(ref SimState candidate,
		HashSet<(int Branch, long Phase)> seen)
	{
		SimState main = candidate;
		main.RainbowShadows = null;
		seen.Add((0, BfsRainbowLocalPhase(ref main)));
		if (candidate.RainbowShadows != null)
		{
			for (int i = 0; i < candidate.RainbowShadows.Length; i++)
			{
				SimState shadow = candidate.RainbowShadows[i];
				seen.Add((i + 1, BfsRainbowLocalPhase(ref shadow)));
			}
		}
	}

	private List<int> OrderBfsRainbowCandidates(List<SimState> candidates,
		List<int> scoreOrderedIndexes, int frontierCap)
	{
		int keepCount = Math.Min(frontierCap, scoreOrderedIndexes.Count);
		if (keepCount >= scoreOrderedIndexes.Count)
			return scoreOrderedIndexes;

		List<int> selectionOrder = scoreOrderedIndexes;
		const int RainbowConvergenceHorizonPx = 1536;
		int[] convergenceCost = new int[candidates.Count];
		Array.Fill(convergenceCost, int.MaxValue);
		bool[] postExitConvergence = new bool[candidates.Count];
		bool hasConvergenceTarget = false;
		bool hasPostExitConvergence = false;
		for (int rank = 0; rank < scoreOrderedIndexes.Count; rank++)
		{
			int candidateIndex = scoreOrderedIndexes[rank];
			SimState candidate = candidates[candidateIndex];
			if (!TryGetBfsRainbowConvergenceCost(ref candidate,
				RainbowConvergenceHorizonPx, out int candidateCost))
			{
				continue;
			}
			convergenceCost[candidateIndex] = candidateCost;
			hasConvergenceTarget = true;
			if (TryGetCommonRainbowExit(ref candidate, out _, out _))
			{
				postExitConvergence[candidateIndex] = true;
				hasPostExitConvergence = true;
			}
		}
		if (hasConvergenceTarget)
		{
			int[] originalRank = new int[candidates.Count];
			for (int rank = 0; rank < scoreOrderedIndexes.Count; rank++)
				originalRank[scoreOrderedIndexes[rank]] = rank;
			selectionOrder = new List<int>(scoreOrderedIndexes);
			selectionOrder.Sort((a, b) =>
			{
				if (hasPostExitConvergence &&
					postExitConvergence[a] != postExitConvergence[b])
				{
					return postExitConvergence[b].CompareTo(postExitConvergence[a]);
				}
				int byCost = convergenceCost[a].CompareTo(convergenceCost[b]);
				return byCost != 0 ? byCost : originalRank[a].CompareTo(originalRank[b]);
			});
		}

		List<int> ordered = new(scoreOrderedIndexes.Count);
		bool[] selected = new bool[candidates.Count];
		void Select(int candidateIndex)
		{
			if (selected[candidateIndex]) return;
			selected[candidateIndex] = true;
			ordered.Add(candidateIndex);
		}

		// A normal 0.5-bias BFS intentionally gives most same-coin candidates an
		// equal score. Keep a small stable prefix, then let every possible mode take
		// turns introducing a trajectory phase. The previous "novel in any mode"
		// pass could spend its entire diversity budget on an easy mode while every
		// useful phase of the limiting mode was discarded.
		int scoreQuota = Math.Max(1, keepCount / 8);
		for (int i = 0; i < scoreQuota; i++)
			Select(scoreOrderedIndexes[i]);

		// Convergence is a reserved slice, not the whole ordering. It is needed to
		// retain ship/ball/etc. entries into a common exit portal, but making every
		// slot locally portal-greedy erases jump setups needed just after the exit.
		if (hasConvergenceTarget)
		{
			int convergenceTarget = hasPostExitConvergence
				? keepCount / 2
				: Math.Min(keepCount, ordered.Count + keepCount / 4);
			for (int i = 0;
				i < selectionOrder.Count && ordered.Count < convergenceTarget;
				i++)
			{
				Select(selectionOrder[i]);
			}
		}

		HashSet<(int Branch, long Phase)> localPhases = new();
		foreach (int candidateIndex in ordered)
		{
			SimState candidate = candidates[candidateIndex];
			BfsRainbowRecordLocalPhases(ref candidate, localPhases);
		}
		int localPhaseTarget = keepCount * 3 / 4;
		int maxBranchCount = 1;
		foreach (int candidateIndex in scoreOrderedIndexes)
		{
			SimState candidate = candidates[candidateIndex];
			maxBranchCount = Math.Max(maxBranchCount,
				BfsRainbowBranchCount(ref candidate));
		}
		int[] branchCursors = new int[maxBranchCount];
		bool addedInRound = true;
		while (ordered.Count < localPhaseTarget && addedInRound)
		{
			addedInRound = false;
			for (int branch = 0;
				branch < maxBranchCount && ordered.Count < localPhaseTarget;
				branch++)
			{
				while (branchCursors[branch] < scoreOrderedIndexes.Count)
				{
					int candidateIndex = scoreOrderedIndexes[branchCursors[branch]++];
					if (selected[candidateIndex]) continue;
					SimState candidate = candidates[candidateIndex];
					if (BfsRainbowBranchCount(ref candidate) <= branch) continue;
					long phase = BfsRainbowBranchPhase(ref candidate, branch);
					if (localPhases.Contains((branch, phase))) continue;
					Select(candidateIndex);
					BfsRainbowRecordLocalPhases(ref candidate, localPhases);
					addedInRound = true;
					break;
				}
			}
		}

		HashSet<long> ensemblePhases = new();
		foreach (int candidateIndex in ordered)
		{
			SimState candidate = candidates[candidateIndex];
			ensemblePhases.Add(BfsRainbowEnsemblePhase(ref candidate));
		}
		foreach (int candidateIndex in scoreOrderedIndexes)
		{
			if (ordered.Count >= keepCount) break;
			if (selected[candidateIndex]) continue;
			SimState candidate = candidates[candidateIndex];
			if (!ensemblePhases.Add(BfsRainbowEnsemblePhase(ref candidate))) continue;
			Select(candidateIndex);
		}
		foreach (int candidateIndex in scoreOrderedIndexes)
		{
			if (ordered.Count >= keepCount) break;
			Select(candidateIndex);
		}
		// Keep the rejected indexes available to the ordinary resource cleanup.
		foreach (int candidateIndex in scoreOrderedIndexes)
		{
			if (!selected[candidateIndex]) ordered.Add(candidateIndex);
		}
		return ordered;
	}

	private bool StepBfsRainbowBranch(ref SimState stepped, in SimState original,
		bool input, sbyte horizontalDirection, List<SimState> output, out byte deathType,
		out int failureModePlusOne, out int failureX_px, out int failureY_px)
	{
		deathType = 0;
		failureModePlusOne = 0;
		failureX_px = 0;
		failureY_px = 0;
		if (stepped.RainbowBranchEnded)
		{
			output.Add(stepped);
			return true;
		}

		AdvanceBfsTimingPreference(ref stepped, in original, input);
		bool alive = StepFrame(ref stepped, input, horizontalDirection, out bool ended);
		int portalModeCount = stepped.RainbowPortalModeCount;
		stepped.RainbowPortalModeCount = 0;
		stepped.RainbowForcedModePlusOne = 0;
		stepped.RainbowBranchEnded = ended;
		output.Add(stepped);
		if (!alive)
		{
			deathType = stepped.DeathType;
			failureModePlusOne = stepped.GameMode + 1;
			failureX_px = stepped.X_fixed >> 8;
			failureY_px = stepped.Y_fixed >> 8;
			return false;
		}

		// A later random portal re-randomizes independently.  Expand the branch
		// again from its exact pre-frame state, preserving every history rather
		// than assuming all prior modes have converged.
		for (int mode = 1; mode < portalModeCount; mode++)
		{
			SimState alternate = original.CloneBranch();
			alternate.RainbowForcedModePlusOne = mode + 1;
			AdvanceBfsTimingPreference(ref alternate, in original, input);
			bool alternateAlive = StepFrame(ref alternate, input, horizontalDirection,
				out bool alternateEnded);
			int alternatePortalModeCount = alternate.RainbowPortalModeCount;
			alternate.RainbowPortalModeCount = 0;
			alternate.RainbowForcedModePlusOne = 0;
			alternate.RainbowBranchEnded = alternateEnded;
			output.Add(alternate);
			if (!alternateAlive || alternatePortalModeCount != portalModeCount)
			{
				deathType = alternate.DeathType;
				failureModePlusOne = alternate.GameMode + 1;
				failureX_px = alternate.X_fixed >> 8;
				failureY_px = alternate.Y_fixed >> 8;
				return false;
			}
		}
		return true;
	}

	private bool StepBfsRainbowEnsemble(ref SimState candidate, in SimState parent,
		bool input, sbyte horizontalDirection, out bool endLevel)
	{
		SimState[] candidateShadows = candidate.RainbowShadows ?? Array.Empty<SimState>();
		SimState[] parentShadows = parent.RainbowShadows ?? Array.Empty<SimState>();
		SimState main = candidate;
		main.RainbowShadows = null;
		List<SimState> branches = new(1 + candidateShadows.Length + 11);
		bool alive = StepBfsRainbowBranch(ref main, in parent, input,
			horizontalDirection, branches,
			out byte universalDeathType, out int failureModePlusOne,
			out int failureX_px, out int failureY_px);

		int shadowIndex = 0;
		for (; shadowIndex < candidateShadows.Length && alive; shadowIndex++)
		{
			SimState shadow = candidateShadows[shadowIndex];
			SimState original = parentShadows[shadowIndex];
			alive = StepBfsRainbowBranch(ref shadow, in original, input,
				horizontalDirection, branches,
				out universalDeathType, out failureModePlusOne,
				out failureX_px, out failureY_px);
		}
		// Keep ownership of unstepped branch resources on a failed candidate so
		// the ordinary BFS rejection path can return everything exactly once.
		for (; shadowIndex < candidateShadows.Length; shadowIndex++)
		{
			branches.Add(candidateShadows[shadowIndex]);
		}

		if (alive)
		{
			if (!TryCollapseConvergedRainbowBranches(branches))
			{
				CollapseEquivalentRainbowBranches(branches);
			}
		}
		candidate = branches[0];
		if (branches.Count > 1)
		{
			candidate.RainbowShadows = new SimState[branches.Count - 1];
			for (int i = 1; i < branches.Count; i++)
				candidate.RainbowShadows[i - 1] = branches[i];
		}
		else
		{
			candidate.RainbowShadows = null;
		}
		if (!alive)
		{
			candidate.DeathType = universalDeathType;
			candidate.RainbowFailureModePlusOne = failureModePlusOne;
			candidate.RainbowFailureX_px = failureX_px;
			candidate.RainbowFailureY_px = failureY_px;
			endLevel = false;
			return false;
		}

		endLevel = candidate.RainbowBranchEnded;
		if (candidate.RainbowShadows != null)
		{
			for (int i = 0; i < candidate.RainbowShadows.Length; i++)
				endLevel &= candidate.RainbowShadows[i].RainbowBranchEnded;
		}
		return true;
	}

	private static bool BfsTimingHasPendingInteraction(in SimState s)
	{
		return s.PendingOrbIndex >= 0 || s.PendingOrbExtra1Index >= 0 ||
			s.PendingOrbExtra2Index >= 0 ||
			(s.DualActive && (s.P2_PendingOrbIndex >= 0 ||
				s.P2_PendingOrbExtra1Index >= 0 ||
				s.P2_PendingOrbExtra2Index >= 0));
	}

	private static bool BfsTimingIsContinuousMode(int gameMode)
	{
		return gameMode == 1 || gameMode == 6 || gameMode == 10;
	}

	private static bool BfsTimingNeedsFreshPress(int gameMode)
	{
		return gameMode == 2 || gameMode == 3 || gameMode == 5 ||
			gameMode == 7 || gameMode == 8 || gameMode == 9;
	}

	private static bool BfsTimingActionAvailable(in SimState s)
	{
		if (s.Dashing != 0 || (s.DualActive && s.P2_Dashing != 0))
			return false;
		if (BfsTimingHasPendingInteraction(in s))
			return true;

		bool p1Grounded = s.OnGround && s.VelY_fixed == 0;
		bool p2Grounded = s.DualActive && s.P2_OnGround &&
			s.P2_VelY_fixed == 0;
		bool eitherReleased = !s.PrevInputHeld ||
			(s.DualActive && !s.P2_PrevInputHeld);
		return s.GameMode switch
		{
			0 => p1Grounded || p2Grounded,
			1 => true,
			2 => ((p1Grounded && s.BallFlipCooldown == 0) ||
				(p2Grounded && s.P2_BallFlipCooldown == 0)) &&
				eitherReleased,
			3 => eitherReleased,
			4 => p1Grounded || p2Grounded,
			5 => (p1Grounded || p2Grounded) && eitherReleased && !s.Orbed,
			6 => true,
			7 => eitherReleased && !s.Orbed,
			8 => eitherReleased &&
				(p1Grounded || s.NinjaJumps > 0 || p2Grounded ||
					(s.DualActive && s.P2_NinjaJumps > 0)),
			9 => eitherReleased && !s.Orbed,
			10 => true,
			_ => false
		};
	}

	private static bool BfsTimingDidAction(in SimState s, bool input)
	{
		bool p1FreshPress = input && !s.PrevInputHeld;
		bool p2FreshPress = input && s.DualActive && !s.P2_PrevInputHeld;
		bool freshPress = p1FreshPress || p2FreshPress;
		if (BfsTimingHasPendingInteraction(in s) && freshPress)
			return true;

		bool p1Grounded = s.OnGround && s.VelY_fixed == 0;
		bool p2Grounded = s.DualActive && s.P2_OnGround &&
			s.P2_VelY_fixed == 0;
		if (BfsTimingIsContinuousMode(s.GameMode))
		{
			return input != s.PrevInputHeld ||
				(s.DualActive && input != s.P2_PrevInputHeld);
		}
		return s.GameMode switch
		{
			0 => input && (p1Grounded || p2Grounded),
			2 => freshPress &&
				((p1Grounded && s.BallFlipCooldown == 0) ||
				 (p2Grounded && s.P2_BallFlipCooldown == 0)),
			3 => freshPress,
			4 => input && (p1Grounded || p2Grounded),
			5 => freshPress && (p1Grounded || p2Grounded),
			7 => freshPress,
			8 => freshPress &&
				(p1Grounded || s.NinjaJumps > 0 || p2Grounded ||
					(s.DualActive && s.P2_NinjaJumps > 0)),
			9 => freshPress,
			_ => false
		};
	}

	private int BfsTimingWeight()
	{
		// Positive means earlier is preferred; negative means later is preferred.
		// The five UI choices therefore remain ordered while 0.5 is truly neutral.
		double bias = Math.Clamp(JumpTimingBias, 0.0, 1.0);
		return (int)Math.Round((0.5 - bias) * 200.0);
	}

	private void AdvanceBfsTimingPreference(ref SimState stepped,
		in SimState parent, bool input)
	{
		int weight = BfsTimingWeight();
		if (weight == 0)
		{
			stepped.BfsTimingLocalCost = 0;
			stepped.BfsTimingOpportunityAge = 0;
			stepped.BfsTimingTrackedModePlusOne = 0;
			stepped.BfsTimingOpportunityOpen = false;
			return;
		}

		int trackedMode = parent.GameMode + 1;
		bool sameWindowMode = parent.BfsTimingTrackedModePlusOne == trackedMode;
		int age = sameWindowMode ? parent.BfsTimingOpportunityAge : 0;
		stepped.BfsTimingLocalCost = 0;
		stepped.BfsTimingTrackedModePlusOne = (byte)trackedMode;

		// Ship, wave, and snake have continuous held controls rather than discrete
		// jump windows. "Early" means establish the held control sooner; "late"
		// means remain released longer. Charge only frames that depart from that
		// preference, so a necessary correction lasts as briefly as survival allows
		// instead of rewarding rapid press/release toggling.
		if (BfsTimingIsContinuousMode(parent.GameMode))
		{
			bool preferredInput = weight > 0;
			bool deviating = input != preferredInput;
			if (deviating)
			{
				stepped.BfsTimingOpportunityAge = (ushort)Math.Min(
					ushort.MaxValue, age + 1);
				stepped.BfsTimingLocalCost = Math.Min(250000,
					Math.Abs(weight) * stepped.BfsTimingOpportunityAge);
			}
			else
			{
				stepped.BfsTimingOpportunityAge = 0;
			}
			stepped.BfsTimingOpportunityOpen = false;
			return;
		}

		// Tap modes cannot reactivate while the button is still held from the prior
		// mode/action. For an early bias, that stale hold is itself a local delay:
		// prefer the route that releases and becomes action-ready sooner. A late bias
		// does not penalize the lockout because it is already deferring the next tap.
		bool staleHeld = parent.PrevInputHeld ||
			(parent.DualActive && parent.P2_PrevInputHeld);
		if (weight > 0 && BfsTimingNeedsFreshPress(parent.GameMode) &&
			staleHeld && !BfsTimingHasPendingInteraction(in parent))
		{
			stepped.BfsTimingOpportunityAge = (ushort)Math.Min(
				ushort.MaxValue, age + 1);
			stepped.BfsTimingLocalCost = Math.Min(250000,
				weight * stepped.BfsTimingOpportunityAge);
			stepped.BfsTimingOpportunityOpen = false;
			return;
		}

		bool available = BfsTimingActionAvailable(in parent);
		bool action = available && BfsTimingDidAction(in parent, input);
		if (action)
		{
			long localCost = (long)weight * age;
			stepped.BfsTimingLocalCost = (int)Math.Clamp(localCost,
				-250000L, 250000L);
			stepped.BfsTimingOpportunityAge = 0;
			stepped.BfsTimingOpportunityOpen = false;
			return;
		}

		if (!available)
		{
			stepped.BfsTimingOpportunityAge = 0;
			stepped.BfsTimingOpportunityOpen = false;
			return;
		}

		stepped.BfsTimingOpportunityAge = (ushort)Math.Min(ushort.MaxValue,
			age + 1);
		stepped.BfsTimingOpportunityOpen = true;
	}

	private int BfsTimingPreferenceScore(ref SimState s)
	{
		long score = s.BfsTimingLocalCost;
		if (s.BfsTimingOpportunityOpen)
			score += (long)BfsTimingWeight() * s.BfsTimingOpportunityAge;
		return (int)Math.Clamp(score, -250000L, 250000L);
	}

	private void KeepWorseBfsTimingHistory(ref SimState destination,
		in SimState other)
	{
		SimState destinationCopy = destination;
		SimState otherCopy = other;
		if (BfsTimingPreferenceScore(ref otherCopy) <=
			BfsTimingPreferenceScore(ref destinationCopy))
		{
			return;
		}
		destination.BfsTimingLocalCost = other.BfsTimingLocalCost;
		destination.BfsTimingOpportunityAge = other.BfsTimingOpportunityAge;
		destination.BfsTimingTrackedModePlusOne =
			other.BfsTimingTrackedModePlusOne;
		destination.BfsTimingOpportunityOpen = other.BfsTimingOpportunityOpen;
	}

	private int BfsScore(ref SimState s, int coinsCollected)
	{
		int score = BfsScoreBranch(ref s, coinsCollected);
		if (s.RainbowShadows != null)
		{
			for (int i = 0; i < s.RainbowShadows.Length; i++)
			{
				SimState shadow = s.RainbowShadows[i];
				score = Math.Max(score, BfsScoreBranch(ref shadow, coinsCollected));
			}
		}
		return score;
	}

	private int BfsScoreBranch(ref SimState s, int coinsCollected)
	{
		// Coins remain lexicographically primary. A platformer has no automatic
		// forward motion, so give forward progress a soft score while the separate
		// X/Y diversity reserve keeps local backtracking routes alive. Timing bias
		// is deliberately local in platformer mode: its ordinary +/-250000 range
		// can otherwise outweigh nearly an entire screen of forward progress and
		// strand the beam at the first wall long after the useful timing window.
		int progressScore = ForcePlatformer ? -((s.X_fixed >> 8) * 64) : 0;
		int timingScore = BfsTimingPreferenceScore(ref s);
		if (ForcePlatformer)
			timingScore = Math.Clamp(timingScore, -1024, 1024);
		return -coinsCollected * 1000000 + progressScore +
			timingScore;
	}

	private int BfsDiversityBucket(ref SimState s, int yBucketSize)
	{
		int yBucket = (s.Y_fixed >> 8) / yBucketSize;
		if (!ForcePlatformer)
			return yBucket;
		int xBucket = (s.X_fixed >> 8) / 32;
		// A left branch needs several consecutive frames to leave its current
		// 32-pixel X bucket. If direction is omitted, the higher-scoring right or
		// neutral state wins this bucket every frame and the left run never forms.
		// Direction and a logarithmic commitment class are search provenance only.
		// The commitment class prevents a state that just tapped left from replacing
		// every state that has walked left for many frames but remains in the same
		// coarse X/Y bucket.
		int directionClass = BfsPlatformerDirectionClass(in s);
		return (xBucket << 20) ^ ((yBucket & 0xFFFF) << 4) ^ directionClass;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int BfsPlatformerDirectionClass(in SimState s)
	{
		int commitmentClass = s.SearchDirectionRun >= 16 ? 4
			: s.SearchDirectionRun >= 8 ? 3
			: s.SearchDirectionRun >= 4 ? 2
			: s.SearchDirectionRun >= 2 ? 1
			: 0;
		return (s.SearchDirectionUsed + 1) * 5 + commitmentClass;
	}

	private static (int Mode, int ScreenY, int VelY, int Motion,
		int Slope, int Horizontal, int Camera, int Ejection) BfsSpiderTrajectoryPhase(
		ref SimState s)
	{
		int mode = (s.Mini ? 2 : 0) | (s.GravFlipped ? 1 : 0);
		int screenY = SharedPhysics.NesPlayerScreenY_px(
			s.Y_fixed, s.CameraY_fixed) >> 1;
		int velY = s.VelY_fixed >> 6;
		int motion = (s.OnGround ? 1 : 0) |
			(s.PrevInputHeld ? 2 : 0) |
			(s.AirPressLatch ? 4 : 0) |
			(s.Orbed ? 8 : 0) |
			(s.BlackOrbed ? 16 : 0);
		int slope = (s.SlopeType & 0xFF) |
			((s.LastSlopeType & 0xFF) << 8) |
			((Math.Min(s.SlopeFrames, 7) & 0x07) << 16) |
			((Math.Min(s.SlopeWasOnCounter, 7) & 0x07) << 19);
		int horizontal = (s.VelX_fixed & 0xFFFF) |
			((s.GlobalSpeed_fixed & 0xFFFF) << 16);
		int camera = (s.CameraY_fixed & 0xFF) |
			((s.ScrollYSubpx & 0xFF) << 8) |
			(((s.TargetCameraY_fixed >> 8) & 0x3FF) << 16);
		// Spider orb/pad handlers consume these persistent NES globals. Two states
		// with the same visible trajectory but different stale eject bytes can land
		// on opposite sides of a later hazard.
		int ejection = s.EjectU | (s.EjectD << 8);
		return (mode, screenY, velY, motion, slope, horizontal, camera, ejection);
	}

	private static bool TryFindBfsVerticalRouteSplit(List<SimState> candidates,
		List<int> candidateIndexes, out int splitY, out int upperCount, out int lowerCount)
	{
		splitY = 0;
		upperCount = 0;
		lowerCount = 0;
		if (candidateIndexes.Count < 2048)
			return false;

		SortedSet<int> occupiedBuckets = new();
		int minY = int.MaxValue;
		int maxY = int.MinValue;
		foreach (int candidateIndex in candidateIndexes)
		{
			int candidateY = candidates[candidateIndex].Y_fixed >> 8;
			occupiedBuckets.Add(candidateY >> 4);
			minY = Math.Min(minY, candidateY);
			maxY = Math.Max(maxY, candidateY);
		}

		int previousBucket = int.MinValue;
		int bestLowerBucket = 0;
		int bestUpperBucket = 0;
		int largestGap = 0;
		foreach (int bucket in occupiedBuckets)
		{
			if (previousBucket != int.MinValue && bucket - previousBucket > largestGap)
			{
				largestGap = bucket - previousBucket;
				bestLowerBucket = previousBucket;
				bestUpperBucket = bucket;
			}
			previousBucket = bucket;
		}

		if (largestGap >= 3)
		{
			// Prefer a real empty corridor gap when one is visible.
			splitY = ((bestLowerBucket << 4) + 15 + (bestUpperBucket << 4)) >> 1;
		}
		else
		{
			// At a fork, valid jump arcs can briefly bridge the empty rows even though
			// the routes are already too far apart to recover from one another.
			if (maxY - minY < 160)
				return false;
			splitY = (minY + maxY) >> 1;
		}
		foreach (int candidateIndex in candidateIndexes)
		{
			if ((candidates[candidateIndex].Y_fixed >> 8) <= splitY)
				upperCount++;
			else
				lowerCount++;
		}
		return upperCount >= 512 && lowerCount >= 512;
	}

	private static string BfsRainbowDiagnosticState(string label,
		in SimState state)
	{
		return $"[RAINBOW_STATE] {label} x={state.X_fixed} y={state.Y_fixed} " +
			$"vy={state.VelY_fixed} vx={state.VelX_fixed} gm={state.GameMode} " +
			$"grav={(state.GravFlipped ? 1 : 0)} mini={(state.Mini ? 1 : 0)} " +
			$"ground={(state.OnGround ? 1 : 0)} prev={(state.PrevInputHeld ? 1 : 0)} " +
			$"latch={(state.AirPressLatch ? 1 : 0)} dash={state.Dashing} " +
			$"cam={state.CameraY_fixed} target={state.TargetCameraY_fixed} " +
			$"scroll={state.ScrollYSubpx} timer={state.ExitPortalTimer} " +
			$"ptr={state.NesSprDataPtr} exitX={state.RainbowExitPortalX_px} " +
			$"exitMode={state.RainbowExitTargetModePlusOne}";
	}

	private void RunBFS(int startX_px, int startY_px, int startSpeedUiIndex,
		int startGameMode, bool startGravFlipped, bool startMini,
		int? frontierCapOverride = null, long deadlineTimestamp = 0)
	{
		const int BaseCoinFrontierCap = 120000;
		int coinFrontierCap = int.TryParse(
			Environment.GetEnvironmentVariable("FAMIDASH_BFS_CAP"), out int diagnosticCap)
			? Math.Max(BaseCoinFrontierCap, diagnosticCap)
			: BaseCoinFrontierCap;
		const int BasePlatformerFrontierCap = 8192;
		int platformerFrontierCap = int.TryParse(
			Environment.GetEnvironmentVariable("FAMIDASH_PLATFORMER_BFS_CAP"),
			out int platformerDiagnosticCap)
			? Math.Clamp(platformerDiagnosticCap, 64, 131072)
			: BasePlatformerFrontierCap;
		bool routeDiagnostics = Environment.GetEnvironmentVariable(
			"FAMIDASH_ROUTE_DIAG") == "1";
		int rainbowEndRejectLogged = 0;
		int diagnosticSpiderOrbKey = routeDiagnostics
			? _nesSpritesArr.FirstOrDefault(sprite =>
				sprite.SpriteId == 85 && sprite.AnchorX_px >= 10000 &&
				sprite.AnchorX_px <= 11000).ProcessKey
			: -1;
		Stopwatch stopwatch = Stopwatch.StartNew();
		long fastDeadlineExtensionTicks = deadlineTimestamp != 0
			? Math.Max(1L, deadlineTimestamp - Stopwatch.GetTimestamp())
			: 0L;
		bool fastDeadlineExtended = false;
		BfsRouteArchive? routeArchive = null;
		bool routeBacktrackAttempted = false;
		if (Verbose)
		{
			int reportedFrontierCap = ForcePlatformer
				? platformerFrontierCap
				: (PreferCoins ? coinFrontierCap : int.MaxValue);
			if (frontierCapOverride.HasValue)
				reportedFrontierCap = Math.Min(reportedFrontierCap,
					frontierCapOverride.Value);
			string searchKind = frontierCapOverride.HasValue
				? "fast parallel beam"
				: "exhaustive BFS";
			_log.WriteLine($"[BFS] Starting {searchKind} exploration " +
				$"(frontier cap={reportedFrontierCap})");
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
			SimState simState = default(SimState);
			simState.X_fixed = InitialXFixed(startX_px);
			simState.Y_fixed = (startY_px << 8) | SpawnYSubpx;
			simState.VelX_fixed = SpeedUiIndexToFixed(startSpeedUiIndex);
			simState.GlobalSpeed_fixed = simState.VelX_fixed;
			simState.VelY_fixed = 0;
			simState.GameMode = startGameMode;
			simState.GravFlipped = startGravFlipped;
			simState.Mini = startMini;
			simState.GravMul = ((!startGravFlipped) ? 1 : (-1));
			simState.GravityMod = 1.0;
			simState.WasZeroedByCollision = true;
			simState.OnGround = true;
			simState.ProcessedSprites = NewSpriteSet();
			simState.PendingOrbIndex = -1;
			simState.PendingOrbSpriteId = -1;
			simState.PendingOrbExtra1Index = -1;
			simState.PendingOrbExtra1SpriteId = -1;
			simState.PendingOrbExtra2Index = -1;
			simState.PendingOrbExtra2SpriteId = -1;
			simState.NinjaJumps = ((startGameMode == 8) ? 3 : 0);
			simState.CameraY_fixed = num;
			simState.TargetCameraY_fixed = num;
			InitializeHorizontalState(ref simState);
			SimState s = simState;
			ApplyPortalsUpTo(ref s, startX_px);
			InitNesSlots(ref s);
			ApplyNesIntroFreezePrestep(ref s);
			CheckSprObjects(ref s);
			_btSkipSpecificOrbs.Clear();
			_btSkipSpecificPads.Clear();
			_speculativeDepth = 1;
			_bfsSearchActive = true;
			int num2 = mapWidth * 16;
			int num3 = startX_px;
			int num4 = -1;
			List<SimState> frontier = new List<SimState> { s };
			List<int[]> list = new List<int[]>();
			List<bool[]> list2 = new List<bool[]>();
			List<sbyte[]> directionHistory = new List<sbyte[]>();
			int num5 = -1;
			int num6 = -1;
			int num7 = -1;
			int bestWinningTimingScore = int.MaxValue;
			bool item = false;
			SimState simState2 = default(SimState);
			int num8 = -1;
			int num9 = -1;
			int num10 = startX_px;
			List<bool>? primaryFailedInputs = null;
			List<sbyte>? primaryFailedDirections = null;
			int primaryFailedX = startX_px;
			int num11 = 240000;
			SimState[] rState = new SimState[num11];
			bool[] rAlive = new bool[num11];
			bool[] rEnd = new bool[num11];
			List<SimState> list3 = new List<SimState>(num11);
			List<int> list4 = new List<int>(num11);
			List<bool> list5 = new List<bool>(num11);
			List<int> list6 = new List<int>(num11);
			List<int> candScore = new List<int>(num11);
			// Horizontal phase, camera/scroll phase, and the exact 16-slot NES runtime
			// table are all future-observable.  In particular, equal player physics can
			// have different upcoming interactions when sprite streaming has assigned
			// different records to a slot.  Keep those phases in the dedup key so the
			// BFS cannot discard a valid branch merely because player Y/velocity match.
			Dictionary<BfsDedupKey, int> dictionary = new Dictionary<BfsDedupKey, int>(num11);
			// Auto-scroll makes a state from an earlier frame unreachable again, but a
			// platformer can idle or walk out and back indefinitely. Remember every
			// exact state that was actually selected for expansion. Reaching that same
			// complete state later cannot expose a new action: all six actions were
			// already expanded when it was first selected. This removes loops without
			// merging different positions, sprite histories, camera phases, or physics.
			HashSet<BfsDedupKey>? platformerVisited = ForcePlatformer
				? new HashSet<BfsDedupKey>(num11)
				: null;
			int bfsStartFrame = 0;
			int lastRainbowOutcomeCount = -1;
			if (DebugBfsPrefixInputs is { Count: > 0 } prefixInputs)
			{
				for (int prefixFrame = 0; prefixFrame < prefixInputs.Count; prefixFrame++)
				{
					_frameCounter = prefixFrame;
					if (!StepFrame(ref s, prefixInputs[prefixFrame], out bool prefixEnded) ||
						prefixEnded)
					{
						throw new InvalidOperationException(
							$"BFS diagnostic prefix stopped at frame {prefixFrame}.");
					}
					list.Add(new[] { 0 });
					list2.Add(new[] { prefixInputs[prefixFrame] });
					directionHistory.Add(new[] { (sbyte)1 });
				}
				frontier[0] = s;
				bfsStartFrame = prefixInputs.Count;
				num3 = s.X_fixed >> 8;
				num10 = num3;
			}
			if (platformerVisited != null)
			{
				SimState initialVisitedState = frontier[0];
				platformerVisited.Add(BuildBfsDedupKey(ref initialVisitedState));
			}
			for (int frame = bfsStartFrame; frame < 28800; frame++)
			{
				if (deadlineTimestamp != 0)
				{
					long deadlineNow = Stopwatch.GetTimestamp();
					if (deadlineNow >= deadlineTimestamp)
					{
						int deadlineProgress = num2 > 0 ? num3 * 100 / num2 : 0;
						// Live preview and diagnostic callbacks are useful but consume part of
						// the same wall-clock budget as search. If a bounded run is demonstrably
						// progressing through the latter half of a level, grant one additional
						// interval rather than discarding a nearly complete route (Fingerdash).
						if (!fastDeadlineExtended && deadlineProgress >= 60)
						{
							fastDeadlineExtended = true;
							deadlineTimestamp = deadlineNow + fastDeadlineExtensionTicks;
							_log.WriteLine($"[FAST] Extending progressing beam at frame {frame}, " +
								$"X={num3}px, progress={deadlineProgress}%, frontier={frontier.Count}");
						}
						else
						{
							_log.WriteLine($"[FAST] Beam deadline reached at frame {frame}, " +
								$"X={num3}px, progress={deadlineProgress}%, frontier={frontier.Count}");
							break;
						}
					}
				}
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
					// A failed bounded pass restarts the exhaustive progress at zero.
					// Keep that short speculative phase quiet rather than making the UI
					// appear to move backwards.
					if (!frontierCapOverride.HasValue)
						Progress.Report(num13);
					num4 = num13;
				}
				int actionCount = ForcePlatformer ? 6 : 2;
				int expandCount = frontier.Count * actionCount;
				if (expandCount > rState.Length)
				{
					int num14 = expandCount;
					rState = new SimState[num14];
					rAlive = new bool[num14];
					rEnd = new bool[num14];
				}
				Action<int> expandCandidate = delegate(int k)
				{
					int action = k % actionCount;
					int index6 = k / actionCount;
					bool input2 = (action & 1) == 1;
					sbyte horizontalDirection = ForcePlatformer
						? (sbyte)((action >> 1) - 1)
						: (sbyte)0;
					SimState parentState = frontier[index6];
					SimState s8 = parentState.Clone();
					bool endLevel3;
					bool flag14;
					if (parentState.RainbowShadows != null)
					{
						flag14 = StepBfsRainbowEnsemble(ref s8, in parentState,
							input2, horizontalDirection, out endLevel3);
					}
					else
					{
						AdvanceBfsTimingPreference(ref s8, in parentState, input2);
						flag14 = StepFrame(ref s8, input2, horizontalDirection,
							out endLevel3);
						int rainbowModeCount = s8.RainbowPortalModeCount;
						s8.RainbowPortalModeCount = 0;
						s8.RainbowForcedModePlusOne = 0;
						s8.RainbowBranchEnded = endLevel3;
						if (flag14 && rainbowModeCount > 0)
						{
							SimState[] rainbowShadows = new SimState[rainbowModeCount - 1];
							int created = 0;
							for (int mode = 1; mode < rainbowModeCount && flag14; mode++)
							{
								SimState alternate = parentState.CloneBranch();
								alternate.RainbowForcedModePlusOne = mode + 1;
								AdvanceBfsTimingPreference(ref alternate,
									in parentState, input2);
								bool alternateAlive = StepFrame(ref alternate, input2,
									horizontalDirection,
									out bool alternateEnded);
								int alternateModeCount = alternate.RainbowPortalModeCount;
								alternate.RainbowPortalModeCount = 0;
								alternate.RainbowForcedModePlusOne = 0;
								alternate.RainbowBranchEnded = alternateEnded;
								if (!alternateAlive || alternateModeCount != rainbowModeCount)
								{
									s8.RainbowFailureModePlusOne = alternate.GameMode + 1;
									s8.RainbowFailureX_px = alternate.X_fixed >> 8;
									s8.RainbowFailureY_px = alternate.Y_fixed >> 8;
									s8.DeathType = alternate.DeathType;
									alternate.ReturnAllSpriteResources();
									flag14 = false;
									for (int i = 0; i < created; i++)
										rainbowShadows[i].ReturnAllSpriteResources();
								}
								else
								{
									rainbowShadows[created++] = alternate;
								}
							}
							if (flag14)
							{
								s8.RainbowShadows = rainbowShadows;
								endLevel3 = s8.RainbowBranchEnded;
								for (int i = 0; i < rainbowShadows.Length; i++)
									endLevel3 &= rainbowShadows[i].RainbowBranchEnded;
							}
						}
					}
					rState[k] = s8;
					rAlive[k] = flag14;
					rEnd[k] = endLevel3;
				};
				// Task scheduling costs more than the simulation for a narrow beam.
				// Keep small frontiers on this worker; large BFS layers still fan out.
				if (expandCount < 256)
				{
					for (int k = 0; k < expandCount; k++)
						expandCandidate(k);
				}
				else
				{
					Parallel.For(0, expandCount, expandCandidate);
				}
				list3.Clear();
				list4.Clear();
				list5.Clear();
				list6.Clear();
				candScore.Clear();
				int num15 = 0;
				int num16 = 0;
				int num17 = 0;
				int[] array = new int[13];
				bool trackDeathTypes = Verbose && ((frame >= 2000 && frame <= 2070) ||
					(frame >= 3550 && frame <= 3850));
				bool trackAscending = Verbose && frame >= 4467 && frame <= 4470;
				int[]? frameDtCounts = (trackDeathTypes ? new int[13] : null);
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
				int[][]? partFrameDtCounts = ((frameDtCounts != null) ? new int[workerCount][] : null);
				int[] partCoinRangeDeaths = new int[workerCount];
				List<string>[]? partAscDeathLogs = (trackAscending ? new List<string>[workerCount] : null);
				List<string>[]? partGravDeathLogs = ((Verbose && frame >= 1890) ? new List<string>[workerCount] : null);
				bool[] partHasWin = new bool[workerCount];
				int[] partWinCoins = new int[workerCount];
				int[] partWinTimingScore = new int[workerCount];
				int[] partWinParent = new int[workerCount];
				bool[] partWinInput = new bool[workerCount];
				SimState[] partWinState = new SimState[workerCount];
				Action<int> processPartition = delegate(int wi)
				{
					int num152 = wi * expandCount / workerCount;
					int num153 = (wi + 1) * expandCount / workerCount;
					int capacity = Math.Max(8, num153 - num152);
					List<SimState> list26 = new List<SimState>(capacity);
					List<int> list27 = new List<int>(capacity);
					List<bool> list28 = new List<bool>(capacity);
					List<int> list29 = new List<int>(capacity);
					List<int> list30 = new List<int>(capacity);
					int num154 = 0;
					int num155 = 0;
					int num156 = 0;
					int[] array10 = new int[13];
					int[]? array11 = ((frameDtCounts != null) ? new int[13] : null);
					int num157 = 0;
					List<string>? list31 = (trackAscending ? new List<string>() : null);
					List<string>? list32 = ((Verbose && frame >= 1890) ? new List<string>(3) : null);
					bool flag11 = false;
					int num158 = 0;
					int bestWorkerTimingScore = int.MaxValue;
					int num159 = -1;
					bool flag12 = false;
					SimState simState12 = default(SimState);
					for (int num160 = num152; num160 < num153; num160++)
					{
						int workerAction = num160 % actionCount;
						int num161 = num160 / actionCount;
						bool flag13 = (workerAction & 1) == 1;
						if (rEnd[num160])
						{
							SimState s6 = rState[num160];
							// Crossing the deterministic exit in every random mode is not
							// enough by itself.  A route that reaches level-end while those
							// outcomes still have distinct states never satisfied the common
							// exit-position contract and must not be reported as a solution.
							if (s6.RainbowShadows != null &&
								TryGetCommonRainbowExit(ref s6, out _, out _))
							{
								SimState[] unconvergedShadows = s6.RainbowShadows!;
								if (routeDiagnostics && System.Threading.Interlocked.CompareExchange(
									ref rainbowEndRejectLogged, 1, 0) == 0)
								{
									Console.Error.WriteLine(
										$"[RAINBOW_UNCONVERGED_END] f={frame} branches={1 + unconvergedShadows.Length}");
									Console.Error.WriteLine(BfsRainbowDiagnosticState("main", in s6));
									for (int branchIndex = 0;
										branchIndex < unconvergedShadows.Length; branchIndex++)
									{
										SimState branch = unconvergedShadows[branchIndex];
										Console.Error.WriteLine(BfsRainbowDiagnosticState(
											$"s{branchIndex + 1}", in branch));
									}
								}
								s6.ReturnAllSpriteResources();
								num154++;
								continue;
							}
							int num162 = CountBfsCoins(ref s6);
							int timingScore = BfsTimingPreferenceScore(ref s6);
							if (!flag11 || num162 > num158 ||
								(num162 == num158 &&
								 timingScore < bestWorkerTimingScore))
							{
								if (flag11)
									simState12.ReturnAllSpriteResources();
								flag11 = true;
								num158 = num162;
								bestWorkerTimingScore = timingScore;
								num159 = num161;
								flag12 = flag13;
								simState12 = s6;
							}
							else
							{
								s6.ReturnAllSpriteResources();
							}
						}
						else if (!rAlive[num160])
						{
							int deathType = rState[num160].DeathType;
							if (deathType == 9)
							{
								num155++;
							}
							if (trackAscending)
							{
								SimState simState13 = frontier[num161];
								int velY_fixed2 = simState13.VelY_fixed;
								int num163 = simState13.Y_fixed >> 8;
								if (velY_fixed2 < 0 && num163 < 720)
								{
									SimState simState14 = rState[num160];
									list31?.Add($"[ASC_DEATH] f={frame} inp={flag13} pX={simState13.X_fixed >> 8} pY={num163} pVelY=0x{velY_fixed2:X} dt={simState14.DeathType} cX={simState14.X_fixed >> 8} cY={simState14.Y_fixed >> 8}");
								}
							}
							if (array11 != null && deathType >= 0 && deathType < array11.Length)
							{
								array11[deathType]++;
							}
							if (trackDeathTypes && frame >= 3550 && frontier[num161].Y_fixed >> 8 <= 200)
							{
								num157++;
							}
							SimState simState15 = frontier[num161];
							if (simState15.GravFlipped)
							{
								num156++;
								if (deathType >= 0 && deathType < array10.Length)
								{
									array10[deathType]++;
								}
								if (list32 != null && list32.Count < 3)
								{
									SimState simState16 = rState[num160];
									list32.Add($"[GF_DEAD] f={frame} pX={simState15.X_fixed >> 8} pY={simState15.Y_fixed >> 8} pVelY=0x{simState15.VelY_fixed:X} | cX={simState16.X_fixed >> 8} cY={simState16.Y_fixed >> 8} dt={deathType} inp={flag13}");
								}
							}
							rState[num160].ReturnAllSpriteResources();
							num154++;
						}
						else
						{
							SimState s7 = rState[num160];
							int num164 = CountBfsCoins(ref s7);
							int item2 = BfsScore(ref s7, num164);
							list26.Add(s7);
							list27.Add(num161);
							list28.Add(flag13);
							list29.Add(num164);
							list30.Add(item2);
						}
					}
					partCandState[wi] = list26;
					partCandParent[wi] = list27;
					partCandInput[wi] = list28;
					partCandCoins[wi] = list29;
					partCandScore[wi] = list30;
					partDeathCount[wi] = num154;
					partStep8bDeathCount[wi] = num155;
					partGravFDeathCount[wi] = num156;
					partGravFDeathTypes[wi] = array10;
					if (partFrameDtCounts != null)
					{
						partFrameDtCounts[wi] = array11 ?? new int[13];
					}
					partCoinRangeDeaths[wi] = num157;
					if (partAscDeathLogs != null)
					{
						partAscDeathLogs[wi] = list31 ?? new List<string>();
					}
					if (partGravDeathLogs != null)
					{
						partGravDeathLogs[wi] = list32 ?? new List<string>();
					}
					partHasWin[wi] = flag11;
					partWinCoins[wi] = num158;
					partWinTimingScore[wi] = bestWorkerTimingScore;
					partWinParent[wi] = num159;
					partWinInput[wi] = flag12;
					partWinState[wi] = simState12;
				};
				if (workerCount == 1)
					processPartition(0);
				else
					Parallel.For(0, workerCount, processPartition);
				int num19 = 0;
				for (int i = 0; i < workerCount; i++)
				{
					if (partHasWin[i])
					{
						int num20 = partWinCoins[i];
						int timingScore = partWinTimingScore[i];
						if (num5 < 0 || num20 > num7 ||
							(num20 == num7 &&
							 timingScore < bestWinningTimingScore))
						{
							if (num5 >= 0)
								simState2.ReturnAllSpriteResources();
							num5 = frame;
							num6 = partWinParent[i];
							item = partWinInput[i];
							num7 = num20;
							bestWinningTimingScore = timingScore;
							simState2 = partWinState[i];
							_log.WriteLine($"[BFS] Level complete at frame {frame}! coins={num20} X\ufffd{simState2.X_fixed >> 8}px");
						}
						else
						{
							partWinState[i].ReturnAllSpriteResources();
						}
					}
					List<SimState> list7 = partCandState[i];
					List<int> list8 = partCandParent[i];
					List<bool> list9 = partCandInput[i];
					List<int> list10 = partCandCoins[i];
					List<int> list11 = partCandScore[i];
					for (int j = 0; j < list7.Count; j++)
					{
						list3.Add(list7[j]);
						list4.Add(list8[j]);
						list5.Add(list9[j]);
						list6.Add(list10[j]);
						candScore.Add(list11[j]);
					}
					num15 += partDeathCount[i];
					num16 += partStep8bDeathCount[i];
					num17 += partGravFDeathCount[i];
					num18 += partCoinRangeDeaths[i];
					int[] array2 = partGravFDeathTypes[i];
					for (int l = 0; l < array.Length; l++)
					{
						array[l] += array2[l];
					}
					if (frameDtCounts != null && partFrameDtCounts != null)
					{
						int[] array3 = partFrameDtCounts[i];
						for (int m = 0; m < frameDtCounts.Length; m++)
						{
							frameDtCounts[m] += array3[m];
						}
					}
					if (partAscDeathLogs != null)
					{
						List<string> list12 = partAscDeathLogs[i];
						for (int n = 0; n < list12.Count; n++)
						{
							Console.Error.WriteLine(list12[n]);
						}
					}
					if (partGravDeathLogs == null || num19 >= 3)
					{
						continue;
					}
					List<string> list13 = partGravDeathLogs[i];
					for (int num21 = 0; num21 < list13.Count; num21++)
					{
						if (num19 >= 3)
						{
							break;
						}
						_log.WriteLine(list13[num21]);
						num19++;
					}
				}
				if (num17 > 0)
				{
					if (_gravFDeathCounts == null)
					{
						_gravFDeathCounts = new int[13];
					}
					for (int num22 = 0; num22 < array.Length && num22 < _gravFDeathCounts.Length; num22++)
					{
						_gravFDeathCounts[num22] += array[num22];
					}
					_gravFDeathFrame = frame;
				}
				if (Verbose && num16 > 0)
				{
					Console.Error.WriteLine($"[S8B] f={frame} step8b={num16} total={num15} front={frontier.Count} cand={list3.Count} mode={frontier[0].GameMode}");
				}
				if (Verbose && list3.Count > 0 && list3[0].DualActive)
				{
					int num23 = list3[0].X_fixed >> 8;
					bool flag = num23 >= 3500 && num23 <= 3560;
					bool flag2 = num23 >= 3100 && num23 < 3500 && frame % 10 == 0;
					if (flag || flag2)
					{
						int num24 = 9999;
						int num25 = -1;
						int num26 = 9999;
						int num27 = -1;
						int num28 = 0;
						int num29 = 0;
						int num30 = 0;
						int num31 = 0;
						int num32 = 0;
						int num33 = 0;
						foreach (SimState item3 in list3)
						{
							int num34 = item3.Y_fixed >> 8;
							int num35 = item3.P2_Y_fixed >> 8;
							if (num34 < num24)
							{
								num24 = num34;
							}
							if (num34 > num25)
							{
								num25 = num34;
							}
							if (num35 < num26)
							{
								num26 = num35;
							}
							if (num35 > num27)
							{
								num27 = num35;
							}
							bool num36 = num34 >= 441 && num34 <= 470;
							bool flag3 = num35 >= 357;
							if (num36)
							{
								num28++;
							}
							if (flag3)
							{
								num29++;
							}
							if (num36 && flag3)
							{
								num30++;
							}
							bool num37 = num34 == 401;
							bool flag4 = num35 == 401;
							if (num37)
							{
								num31++;
							}
							if (flag4)
							{
								num32++;
							}
							if (num37 && flag4)
							{
								num33++;
							}
						}
						Console.Error.WriteLine($"[DUAL_Y] f={frame} X={num23} cands={list3.Count} P1:[{num24},{num25}] p1s={num28} on401={num31} P2:[{num26},{num27}] p2s={num29} on401={num32} both={num30} both401={num33}");
						if (flag)
						{
							foreach (SimState item4 in list3)
							{
								int num38 = item4.Y_fixed >> 8;
								int num39 = item4.P2_Y_fixed >> 8;
								int velY_fixed = item4.VelY_fixed;
								int p2_VelY_fixed = item4.P2_VelY_fixed;
								if ((num38 >= 355 && num38 <= 420) || (num39 >= 355 && num39 <= 420))
								{
									Console.Error.WriteLine($"  [ST] X={num23} P1Y={num38} P1V={velY_fixed} P2Y={num39} P2V={p2_VelY_fixed}");
								}
							}
						}
					}
				}
				if (Verbose && frontier.Count <= 10 && frontier.Count > 0 && frontier[0].GameMode == 1 && list3.Count > 0)
				{
					_log.WriteLine($"[SHIP_DIAG] f={frame} front={frontier.Count} cands={list3.Count} deaths={num15}");
					for (int num40 = 0; num40 < Math.Min(list3.Count, 20); num40++)
					{
						SimState s2 = list3[num40];
						long value = BfsQuantizeKey(ref s2);
						_log.WriteLine($"  cand[{num40}] inp={list5[num40]} Y={s2.Y_fixed >> 8} Yfx=0x{s2.Y_fixed:X} VelY=0x{s2.VelY_fixed:X} X={s2.X_fixed >> 8} key=0x{value:X16} score={candScore[num40]} grav={s2.GravFlipped} mode={s2.GameMode}");
					}
					for (int num41 = 0; num41 < Math.Min(frontier.Count, 10); num41++)
					{
						SimState simState3 = frontier[num41];
						_log.WriteLine($"  parent[{num41}] Y={simState3.Y_fixed >> 8} Yfx=0x{simState3.Y_fixed:X} VelY=0x{simState3.VelY_fixed:X} X={simState3.X_fixed >> 8}");
					}
				}
				if (list3.Count == 0 && num5 < 0 && routeArchive != null &&
					!routeBacktrackAttempted)
				{
					// Preserve the primary search result before rewinding. A route fallback
					// is exploratory and must never replace a farther failed path with a
					// worse one (Windy Landscape exposed this as 78% becoming 21%).
					if (num9 >= 0 && list.Count > 0)
					{
						List<bool> savedInputs = new();
						List<sbyte> savedDirections = new();
						int savedIndex = num9;
						for (int savedFrame = num8; savedFrame >= 0; savedFrame--)
						{
							savedInputs.Add(list2[savedFrame][savedIndex]);
							savedDirections.Add(directionHistory[savedFrame][savedIndex]);
							savedIndex = list[savedFrame][savedIndex];
						}
						savedInputs.Reverse();
						savedDirections.Reverse();
						primaryFailedInputs = savedInputs;
						primaryFailedDirections = savedDirections;
						primaryFailedX = num10;
					}
					routeBacktrackAttempted = true;
					foreach (SimState state in frontier)
						state.ReturnAllSpriteResources();

					if (list.Count > routeArchive.Frame)
						list.RemoveRange(routeArchive.Frame, list.Count - routeArchive.Frame);
					if (list2.Count > routeArchive.Frame)
						list2.RemoveRange(routeArchive.Frame, list2.Count - routeArchive.Frame);
					if (directionHistory.Count > routeArchive.Frame)
						directionHistory.RemoveRange(routeArchive.Frame,
							directionHistory.Count - routeArchive.Frame);
					list.Add(routeArchive.Parents);
					list2.Add(routeArchive.Inputs);
					directionHistory.Add(routeArchive.Directions);
					frontier = routeArchive.States;
					int restoredFrame = routeArchive.Frame;
					int restoredSplitY = routeArchive.SplitY;
					routeArchive = null;

					num3 = frontier.Max(state => state.X_fixed >> 8);
					num4 = -1;
					num8 = list.Count - 1;
					num9 = 0;
					num10 = frontier[0].X_fixed >> 8;
					for (int restoredIndex = 1; restoredIndex < frontier.Count; restoredIndex++)
					{
						int restoredX = frontier[restoredIndex].X_fixed >> 8;
						if (restoredX > num10)
						{
							num9 = restoredIndex;
							num10 = restoredX;
						}
					}
					_log.WriteLine($"[BFS_BACKTRACK] dead end at frame {frame}; " +
						$"restoring {frontier.Count} fork states from frame " +
						$"{restoredFrame} (splitY={restoredSplitY})");
					if (routeDiagnostics)
					{
						Console.Error.WriteLine($"[BFS_BACKTRACK] dead={frame} " +
							$"restore={restoredFrame} states={frontier.Count} " +
							$"splitY={restoredSplitY}");
					}
					frame = restoredFrame;
					continue;
				}
				if (list3.Count == 0)
				{
					_log.WriteLine($"[BFS] ALL DEAD at frame {frame} (X~{num3}px pct={num13}% expanded={frontier.Count * 2} deaths={num15})");
					Console.Error.WriteLine($"[BFS] Frontier before expansion: {frontier.Count} states");
					_log.WriteLine($"[BFS] Frontier before expansion: {frontier.Count} states");
					int frontierSampleCount = Math.Min(frontier.Count, 16);
					for (int num42 = 0; num42 < frontierSampleCount; num42++)
					{
						SimState simState4 = frontier[num42];
						Console.Error.WriteLine($"  [{num42}] X={simState4.X_fixed >> 8} Y={simState4.Y_fixed >> 8} VelY=0x{simState4.VelY_fixed:X} grav={simState4.GravFlipped} mini={simState4.Mini}");
						_log.WriteLine($"  [{num42}] X={simState4.X_fixed >> 8} Y={simState4.Y_fixed >> 8} VelY=0x{simState4.VelY_fixed:X} grav={simState4.GravFlipped} mini={simState4.Mini}");
					}
					if (frontier.Count > frontierSampleCount)
					{
						string omitted = $"  ... {frontier.Count - frontierSampleCount} additional states omitted";
						Console.Error.WriteLine(omitted);
						_log.WriteLine(omitted);
					}
					int[] array4 = new int[13];
					int[] rainbowModeDeaths = new int[12];
					for (int num43 = 0; num43 < expandCount; num43++)
					{
						if (!rAlive[num43] && !rEnd[num43])
						{
							array4[rState[num43].DeathType]++;
							int failedMode = rState[num43].RainbowFailureModePlusOne - 1;
							if ((uint)failedMode < (uint)rainbowModeDeaths.Length)
								rainbowModeDeaths[failedMode]++;
						}
					}
					string[] array5 = new string[13]
					{
						"UNK", "CEIL_SPIKE", "EJECT", "CENTER", "BALL_PROBE", "BALL_VELZERO", "BALL_EJECT", "FLOOR_SPIKE", "FWD", "DEATH_COLL",
						"BOUNDS", "BALL_EJECT_CEIL", "BALL_EJECT_FLOOR"
					};
					List<string> list14 = new List<string>();
					for (int num44 = 0; num44 < array4.Length; num44++)
					{
						if (array4[num44] > 0)
						{
							list14.Add($"{array5[num44]}={array4[num44]}");
						}
					}
					_log.WriteLine("[BFS] Death types: " + string.Join(" ", list14));
					Console.Error.WriteLine("[BFS] Death types: " + string.Join(" ", list14));
					List<string> rainbowDeaths = new List<string>();
					for (int failedMode = 0; failedMode < rainbowModeDeaths.Length;
						failedMode++)
					{
						if (rainbowModeDeaths[failedMode] > 0)
							rainbowDeaths.Add($"m{failedMode}={rainbowModeDeaths[failedMode]}");
					}
					if (rainbowDeaths.Count > 0)
					{
						string rainbowDeathText = "[BFS] Rainbow failing modes: " +
							string.Join(" ", rainbowDeaths);
						_log.WriteLine(rainbowDeathText);
						Console.Error.WriteLine(rainbowDeathText);
					}
					int num45 = 0;
					for (int num46 = 0; num46 < expandCount; num46++)
					{
						if (num45 >= 5)
						{
							break;
						}
						if (!rAlive[num46] && !rEnd[num46])
						{
							SimState simState5 = rState[num46];
							int index = num46 / actionCount;
							bool diagnosticInput = ((num46 % actionCount) & 1) == 1;
							SimState simState6 = frontier[index];
							_log.WriteLine($"[BFS_DEAD] parent X=0x{simState6.X_fixed:X} Y=0x{simState6.Y_fixed:X} VelY=0x{simState6.VelY_fixed:X} grav={simState6.GravFlipped} mini={simState6.Mini} dual={simState6.DualActive} P2_Y=0x{simState6.P2_Y_fixed:X} P2_grav={simState6.P2_GravFlipped} | child X=0x{simState5.X_fixed:X} Y=0x{simState5.Y_fixed:X} VelY=0x{simState5.VelY_fixed:X} grav={simState5.GravFlipped} mini={simState5.Mini} dt={simState5.DeathType} inp={diagnosticInput}");
							string rainbowFailure = simState5.RainbowFailureModePlusOne > 0
								? $" rainbowMode={simState5.RainbowFailureModePlusOne - 1}" +
								  $" rainbowXY=({simState5.RainbowFailureX_px},{simState5.RainbowFailureY_px})"
								: "";
							Console.Error.WriteLine($"[BFS_DEAD] parent Y={simState6.Y_fixed >> 8} VelY=0x{simState6.VelY_fixed:X} grav={simState6.GravFlipped} | child Y={simState5.Y_fixed >> 8} VelY=0x{simState5.VelY_fixed:X} dt={array5[simState5.DeathType]} inp={diagnosticInput}{rainbowFailure}");
							num45++;
						}
					}
					if (frontier.Count > 0)
					{
						int num47 = int.MaxValue;
						int num48 = int.MinValue;
						int num49 = int.MaxValue;
						int num50 = int.MinValue;
						int num51 = int.MaxValue;
						int num52 = int.MinValue;
						int num53 = int.MaxValue;
						int num54 = int.MinValue;
						foreach (SimState item5 in frontier)
						{
							int num55 = item5.Y_fixed >> 8;
							int num56 = item5.X_fixed >> 8;
							if (num55 < num47)
							{
								num47 = num55;
							}
							if (num55 > num48)
							{
								num48 = num55;
							}
							if (num56 < num51)
							{
								num51 = num56;
							}
							if (num56 > num52)
							{
								num52 = num56;
							}
							if (item5.VelY_fixed < num49)
							{
								num49 = item5.VelY_fixed;
							}
							if (item5.VelY_fixed > num50)
							{
								num50 = item5.VelY_fixed;
							}
							if (item5.DualActive)
							{
								int num57 = item5.P2_Y_fixed >> 8;
								if (num57 < num53)
								{
									num53 = num57;
								}
								if (num57 > num54)
								{
									num54 = num57;
								}
							}
						}
						string value2 = (frontier[0].DualActive ? $" P2_Y=[{num53}..{num54}]" : "");
						_log.WriteLine($"[BFS] Last frontier: size={frontier.Count} X=[{num51}..{num52}] Y=[{num47}..{num48}] mode={frontier[0].GameMode} grav={frontier[0].GravFlipped} mini={frontier[0].Mini} VelY=[0x{num49:X}..0x{num50:X}]{value2}");
						Console.Error.WriteLine($"[BFS] Last frontier: size={frontier.Count} X=[{num51}..{num52}] Y=[{num47}..{num48}] mode={frontier[0].GameMode} grav={frontier[0].GravFlipped} mini={frontier[0].Mini} VelY=[0x{num49:X}..0x{num50:X}]{value2}");
					}
					_log.Flush();
					break;
				}
				if (num5 >= 0)
				{
					bool flag5 = false;
					for (int num58 = 0; num58 < list6.Count; num58++)
					{
						if (list6[num58] > num7)
						{
							flag5 = true;
							break;
						}
					}
					if (!flag5 && PreferCoins && allCoins.Count > 0 && list3.Count > 0)
					{
						int num59 = 0;
						for (int num60 = 0; num60 < list3.Count; num60++)
						{
							int num61 = list3[num60].X_fixed >> 8;
							if (num61 > num59)
							{
								num59 = num61;
							}
						}
						foreach (SpriteEntry allCoin in allCoins)
						{
							if (allCoin.HitRight > num59 && simState2.ProcessedSprites != null && !simState2.ProcessedSprites.Contains(allCoin.Index))
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
				for (int num62 = 0; num62 < list3.Count; num62++)
				{
					SimState s3 = list3[num62];
					BfsDedupKey key = BuildBfsDedupKey(ref s3);
					if (platformerVisited != null && platformerVisited.Contains(key))
						continue;
					if (!dictionary.TryGetValue(key, out var value3) ||
						candScore[num62] < candScore[value3] ||
						(candScore[num62] == candScore[value3] &&
						 list3[value3].BfsSpiderReserved && !s3.BfsSpiderReserved))
					{
						dictionary[key] = num62;
					}
				}
				if (Verbose && list3.Count > 0 && list3.Count != dictionary.Count)
				{
					Console.Error.WriteLine($"[DEDUP] f={frame} cand={list3.Count} deduped={dictionary.Count} (lost {list3.Count - dictionary.Count})");
				}
				int diagnosticCandidateOrbCount = 0;
				int diagnosticDedupOrbCount = 0;
				int diagnosticRawOrbCount = 0;
				int diagnosticRawTeleportCount = 0;
				int diagnosticAliveTeleportCount = 0;
				int diagnosticCandidateTeleportCount = 0;
				int diagnosticDedupTeleportCount = 0;
				int diagnosticX = list3.Count > 0 ? list3[0].X_fixed >> 8 : -1;
				if (routeDiagnostics && diagnosticSpiderOrbKey >= 0 &&
					diagnosticX >= 10380 && diagnosticX <= 10440)
				{
					for (int resultIndex = 0; resultIndex < expandCount; resultIndex++)
					{
						SimState resultState = rState[resultIndex];
						if (resultState.ProcessedSprites.Contains(diagnosticSpiderOrbKey))
							diagnosticRawOrbCount++;
						if (resultState.GameMode == 5 && !resultState.GravFlipped &&
							(resultState.Y_fixed >> 8) >= 360)
						{
							diagnosticRawTeleportCount++;
							if (rAlive[resultIndex])
								diagnosticAliveTeleportCount++;
						}
					}
					diagnosticCandidateOrbCount = list3.Count(state =>
						state.ProcessedSprites.Contains(diagnosticSpiderOrbKey));
					diagnosticDedupOrbCount = dictionary.Values.Count(candidateIndex =>
						list3[candidateIndex].ProcessedSprites.Contains(diagnosticSpiderOrbKey));
					diagnosticCandidateTeleportCount = list3.Count(state =>
						state.GameMode == 5 && !state.GravFlipped &&
						(state.Y_fixed >> 8) >= 360);
					diagnosticDedupTeleportCount = dictionary.Values.Count(candidateIndex =>
						list3[candidateIndex].GameMode == 5 &&
						!list3[candidateIndex].GravFlipped &&
						(list3[candidateIndex].Y_fixed >> 8) >= 360);
				}
				if (Verbose && frame >= 4400 && frame <= 4600 && dictionary.Count > 0)
				{
					foreach (SimState item6 in list3)
					{
						int num63 = item6.X_fixed >> 8;
						int value4 = item6.Y_fixed >> 8;
						if (num63 >= 12300 && num63 <= 12700)
						{
							Console.Error.WriteLine($"[TRACE] f={frame} X={num63} Y={value4} VelY=0x{item6.VelY_fixed:X}");
						}
					}
					for (int num64 = 0; num64 < expandCount; num64++)
					{
						if (!rAlive[num64] && !rEnd[num64])
						{
							int index2 = num64 / actionCount;
							SimState simState7 = frontier[index2];
							int num65 = simState7.X_fixed >> 8;
							if (num65 >= 12300 && num65 <= 12700)
							{
								SimState simState8 = rState[num64];
								Console.Error.WriteLine($"[TDEAD] f={frame} pX={num65} pY={simState7.Y_fixed >> 8} pVelY=0x{simState7.VelY_fixed:X} dt={simState8.DeathType} inp={((num64 % actionCount) & 1) == 1}");
							}
						}
					}
				}
				if (Verbose)
				{
					int num66 = 0;
					int num67 = 0;
					for (int num68 = 0; num68 < list3.Count; num68++)
					{
						if (list3[num68].GravFlipped)
						{
							num66++;
						}
					}
					foreach (int value14 in dictionary.Values)
					{
						if (list3[value14].GravFlipped)
						{
							num67++;
						}
					}
					int num69 = 0;
					foreach (SimState item7 in frontier)
					{
						if (item7.GravFlipped)
						{
							num69++;
						}
					}
					if (num69 > 0 || num66 > 0)
					{
						int num70 = int.MaxValue;
						int num71 = int.MinValue;
						for (int num72 = 0; num72 < list3.Count; num72++)
						{
							if (list3[num72].GravFlipped)
							{
								int num73 = list3[num72].Y_fixed >> 8;
								if (num73 < num70)
								{
									num70 = num73;
								}
								if (num73 > num71)
								{
									num71 = num73;
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
							for (int num74 = 0; num74 < array.Length; num74++)
							{
								if (array[num74] > 0)
								{
									text += $" {((num74 < array6.Length) ? array6[num74] : $"d{num74}")}={array[num74]}";
								}
							}
						}
						_log.WriteLine($"[GF_PIPE] f={frame} frontGF={num69} gfDied={num17}{text} candGF={num66} dedupGF={num67} gfY=[{((num70 == int.MaxValue) ? "N/A" : num70.ToString())}..{((num71 == int.MinValue) ? "N/A" : num71.ToString())}]");
					}
				}
				// Additive seeds are one-generation candidates. Their children rejoin
				// the ordinary beam, matching the selector that produced the known-good
				// Windy Landscape and Heliopolis paths.
				List<int> list15 = new List<int>(dictionary.Count);
				List<int> inheritedSpiderReserveIndexes = new List<int>();
				foreach (int candidateIndex in dictionary.Values)
				{
					SimState candidate = list3[candidateIndex];
					if (candidate.RainbowShadows == null &&
						candidate.BfsSpiderReserved && candidate.GameMode == 5)
					{
						inheritedSpiderReserveIndexes.Add(candidateIndex);
					}
					else
					{
						if (candidate.BfsSpiderReserved)
						{
							candidate.BfsSpiderReserved = false;
							list3[candidateIndex] = candidate;
						}
						list15.Add(candidateIndex);
					}
				}
				list15.Sort((int a, int b) =>
				{
					return candScore[a].CompareTo(candScore[b]);
				});
				inheritedSpiderReserveIndexes.Sort((int a, int b) =>
					candScore[a].CompareTo(candScore[b]));
				int nextRainbowPortalX;
				int rainbowOutcomeCount = BfsRainbowSearchOutcomeCount(
					list3, list15, out nextRainbowPortalX);
				bool rainbowSelectiveSearch = rainbowOutcomeCount > 0;
				int rainbowFrontierCap = rainbowSelectiveSearch
					? BfsRainbowFrontierCap(rainbowOutcomeCount)
					: int.MaxValue;
				if (rainbowSelectiveSearch)
				{
					list15 = OrderBfsRainbowCandidates(
						list3, list15, rainbowFrontierCap);
				}
				if (Verbose && rainbowSelectiveSearch &&
					(frame % 30 == 0 || rainbowOutcomeCount != lastRainbowOutcomeCount))
				{
					string portalText = nextRainbowPortalX >= 0
						? nextRainbowPortalX.ToString()
						: "active";
					_log.WriteLine($"[RAINBOW_SELECT] f={frame} outcomes={rainbowOutcomeCount} " +
						$"cap={rainbowFrontierCap} candidates={list15.Count} portalX={portalText}");
				}
				lastRainbowOutcomeCount = rainbowOutcomeCount;
				// A recovered corridor uses the same safe cap as the primary search.
				// Expanding a retry to hundreds of thousands of states made a known-dead
				// route dramatically slower without exposing an additional input branch.
				int num75 = rainbowSelectiveSearch
					? rainbowFrontierCap
					: (ForcePlatformer ? platformerFrontierCap
						: (PreferCoins ? coinFrontierCap : int.MaxValue));
				if (frontierCapOverride.HasValue)
					num75 = Math.Min(num75, frontierCapOverride.Value);
				int selectedCapacity = Math.Min(dictionary.Count, num75);
				List<SimState> list16 = new List<SimState>(selectedCapacity);
				List<int> list17 = new List<int>(selectedCapacity);
				List<bool> list18 = new List<bool>(selectedCapacity);
				HashSet<int>? recoverySelectedIndexes = routeBacktrackAttempted
					? new HashSet<int>()
					: null;
				// Preserve the established beam ordering exactly. Long-lived route recovery
				// is handled by the independent archive below, not by changing which ordinary
				// candidates this selector retains.
				// A cap is an upper bound, not a target.  If every deduplicated state
				// fits, retain every one; the score/diversity split is needed only when
				// actual truncation is unavoidable.
				int scorePrefixCount = rainbowSelectiveSearch
					? Math.Min(num75, list15.Count)
					: ((!PreferCoins && !ForcePlatformer) || list15.Count <= num75
						? list15.Count
						: (ForcePlatformer ? num75 / 2 : num75 * 3 / 4));
				int num76 = Math.Min(scorePrefixCount, list15.Count);
				for (int num77 = 0; num77 < num76; num77++)
				{
					if (list16.Count >= num75)
					{
						break;
					}
					int index3 = list15[num77];
					list16.Add(list3[index3]);
					list17.Add(list4[index3]);
					list18.Add(list5[index3]);
					recoverySelectedIndexes?.Add(index3);
				}
				if (!PreferCoins && !ForcePlatformer && list15.Count > num76)
				{
					for (int num78 = num76; num78 < list15.Count; num78++)
					{
						if (list16.Count >= num75)
						{
							break;
						}
						int index4 = list15[num78];
						list16.Add(list3[index4]);
						list17.Add(list4[index4]);
						list18.Add(list5[index4]);
						recoverySelectedIndexes?.Add(index4);
					}
				}
				else if (!rainbowSelectiveSearch && list15.Count > num76)
				{
					int num79 = 16;
					Dictionary<int, int> dictionary2 = new Dictionary<int, int>();
					foreach (SimState item8 in list16)
					{
						SimState bucketState = item8;
						int key2 = BfsDiversityBucket(ref bucketState, num79);
						dictionary2.TryGetValue(key2, out var value5);
						dictionary2[key2] = value5 + 1;
					}
					int num80 = list16.Count / Math.Max(1, dictionary2.Count);
					int num81 = 0;
					int num82 = 0;
					foreach (SimState item9 in list16)
					{
						if (item9.GravFlipped)
						{
							num82++;
						}
						else
						{
							num81++;
						}
					}
					bool flag6 = Math.Min(num81, num82) < list16.Count / 20;
					// Size portals create future-distinct branches even when game mode is
					// unchanged.  Keep mini and grown states in separate diversity classes
					// so a newly grown route cannot be pruned merely because the much larger
					// mini population already represents the same mode (Sunshine).
					Dictionary<int, int> dictionary3 = new Dictionary<int, int>();
					foreach (SimState item10 in list16)
					{
						int sizeModeClass = (item10.GameMode << 1) | (item10.Mini ? 1 : 0);
						dictionary3.TryGetValue(sizeModeClass, out var value6);
						dictionary3[sizeModeClass] = value6 + 1;
					}
					// A vertically separated portal route can be less attractive than a safe
					// bypass until long after the bypass is no longer recoverable. Keep a
					// small set of the best unselected approach phases for each upcoming
					// mode/size transition. These are additions above the ordinary frontier
					// cap, so portal awareness never evicts an existing candidate.
					const int TransitionApproachHorizonPx = 2048;
					// A route-archive retry exists specifically because the ordinary tied-score
					// beam already discarded a needed corridor.  Preserve more approach phases
					// during that retry, additively, until it reaches the transition portal.
					// The ordinary pass keeps a bounded but broad phase sample; recovery gets
					// extra room because it is already isolated to a failed corridor.
					int transitionSeedsPerPortal = routeBacktrackAttempted ? 2048 : 128;
					// One representative per vertical/velocity/input phase prevents all of
					// the reserved slots from collapsing onto the same attractive but
					// ultimately invalid arc. These states are additions above the normal
					// cap, so this can only preserve paths.
					Dictionary<(int Portal, int Phase), (int Index, int Distance)>
						transitionPhaseBest = new();
					for (int seedPos = num76; seedPos < list15.Count; seedPos++)
					{
						int seedIndex = list15[seedPos];
						SimState seedState = list3[seedIndex];
						if (!TryGetUpcomingBfsTransitionPortal(ref seedState,
							TransitionApproachHorizonPx, out BfsTransitionPortal portal))
						{
							continue;
						}
						int playerCenterY = (seedState.Y_fixed >> 8) + 8;
						int distance = Math.Abs(playerCenterY - portal.Y);
						int yPhase = SharedPhysics.NesPlayerScreenY_px(
							seedState.Y_fixed, seedState.CameraY_fixed) >> 3;
						int velocityPhase = (seedState.VelY_fixed + 32768) >> 7;
						int phase = (yPhase & 0x3F) |
							((velocityPhase & 0x1FF) << 6) |
							((seedState.OnGround ? 1 : 0) << 15) |
							((seedState.PrevInputHeld ? 1 : 0) << 16);
						var phaseKey = (portal.ProcessKey, phase);
						if (!transitionPhaseBest.TryGetValue(phaseKey, out var prior) ||
							distance < prior.Distance)
						{
							transitionPhaseBest[phaseKey] = (seedIndex, distance);
						}
					}
					Dictionary<int, PriorityQueue<(int Index, int Distance), int>>
						transitionQueues = new();
					foreach (var phaseCandidate in transitionPhaseBest)
					{
						int portalKey = phaseCandidate.Key.Portal;
						if (!transitionQueues.TryGetValue(portalKey, out var queue))
						{
							queue = new PriorityQueue<(int, int), int>();
							transitionQueues[portalKey] = queue;
						}
						var candidate = phaseCandidate.Value;
						queue.Enqueue(candidate, -candidate.Distance);
						if (queue.Count > transitionSeedsPerPortal)
							queue.Dequeue();
					}
					HashSet<int> seededTransitionCandidates = new HashSet<int>();
					foreach (var portalQueue in transitionQueues)
					{
						int portalKey = portalQueue.Key;
						var queue = portalQueue.Value;
						foreach (var queued in queue.UnorderedItems)
						{
							int seedIndex = queued.Element.Index;
							if (!seededTransitionCandidates.Add(seedIndex))
								continue;
						SimState seedState = list3[seedIndex];
						list16.Add(seedState);
							list17.Add(list4[seedIndex]);
							list18.Add(list5[seedIndex]);
							recoverySelectedIndexes?.Add(seedIndex);
							int seedYBucket = BfsDiversityBucket(ref seedState, num79);
							dictionary2.TryGetValue(seedYBucket, out int seedYCount);
							dictionary2[seedYBucket] = seedYCount + 1;
							int seedClass = (seedState.GameMode << 1) | (seedState.Mini ? 1 : 0);
							dictionary3.TryGetValue(seedClass, out int seedClassCount);
							dictionary3[seedClass] = seedClassCount + 1;
							if (seedState.GravFlipped) num82++; else num81++;
						}
					}
					if (Verbose && seededTransitionCandidates.Count > 0 && frame % 20 == 0)
					{
						_log.WriteLine($"[TRANSITION_SELECT] f={frame} portals={transitionQueues.Count} " +
							$"added={seededTransitionCandidates.Count} ordinaryCap={num75}");
					}
					// A class can be completely absent from the score-selected prefix. Seed
					// its best remaining candidate before the normal fill so the portal's
					// output remains searchable on the following frame. These few seeds are
					// added above the ordinary cap; existing diversity slots are not removed.
					HashSet<int> seededSizeModeCandidates = new HashSet<int>();
					for (int seedPos = num76; seedPos < list15.Count; seedPos++)
					{
						int seedIndex = list15[seedPos];
						SimState seedState = list3[seedIndex];
						int seedClass = (seedState.GameMode << 1) | (seedState.Mini ? 1 : 0);
						if (dictionary3.ContainsKey(seedClass))
						{
							continue;
						}
						list16.Add(seedState);
						list17.Add(list4[seedIndex]);
						list18.Add(list5[seedIndex]);
						recoverySelectedIndexes?.Add(seedIndex);
						seededSizeModeCandidates.Add(seedIndex);
						dictionary3[seedClass] = 1;
						int seedYBucket = BfsDiversityBucket(ref seedState, num79);
						dictionary2.TryGetValue(seedYBucket, out int seedYCount);
						dictionary2[seedYBucket] = seedYCount + 1;
						if (seedState.GravFlipped)
						{
							num82++;
						}
						else
						{
							num81++;
						}
					}
					// Autoscroll levels can safely rank nearly every retained state by
					// forward score because X cannot retreat. Platformer rooms are different:
					// a wall can require moving back several columns and climbing a route whose
					// local score is temporarily worse. Fill the platformer diversity half in
					// rounds across the full occupied 32x16-pixel span. This keeps neutral/left
					// descendants alive without spending every diversity slot at either the
					// earliest segment or the furthest wall.
					if (ForcePlatformer)
					{
						HashSet<int> alreadySelected = new();
						for (int selectedPos = 0; selectedPos < num76; selectedPos++)
							alreadySelected.Add(list15[selectedPos]);
						alreadySelected.UnionWith(seededTransitionCandidates);
						alreadySelected.UnionWith(seededSizeModeCandidates);

						SortedDictionary<int, List<int>> segmentCandidates = new();
						for (int candidatePos = num76; candidatePos < list15.Count; candidatePos++)
						{
							int candidateIndex = list15[candidatePos];
							if (alreadySelected.Contains(candidateIndex))
								continue;
							SimState segmentState = list3[candidateIndex];
							int segmentKey = BfsDiversityBucket(ref segmentState, num79);
							if (!segmentCandidates.TryGetValue(segmentKey, out List<int>? bucket))
							{
								bucket = new List<int>();
								segmentCandidates.Add(segmentKey, bucket);
							}
							bucket.Add(candidateIndex);
						}

						int platformerDiversityLimit = Math.Min(list3.Count,
							num75 + seededTransitionCandidates.Count +
							seededSizeModeCandidates.Count);
						List<List<int>> orderedSegmentBuckets =
							segmentCandidates.Values.ToList();
						for (int segmentRound = 0;
							list16.Count < platformerDiversityLimit;
							segmentRound++)
						{
							List<List<int>> eligibleBuckets = orderedSegmentBuckets
								.Where(bucket => segmentRound < bucket.Count)
								.ToList();
							if (eligibleBuckets.Count == 0)
								break;
							int slotsRemaining = platformerDiversityLimit - list16.Count;
							int bucketsToTake = Math.Min(slotsRemaining, eligibleBuckets.Count);
							for (int sample = 0; sample < bucketsToTake; sample++)
							{
								// Midpoint sampling is deterministic and covers the entire sorted
								// spatial range even when there are more buckets than slots.
								int bucketPosition = (int)(((long)(sample * 2 + 1) *
									eligibleBuckets.Count) / (bucketsToTake * 2L));
								List<int> bucket = eligibleBuckets[bucketPosition];
								int candidateIndex = bucket[segmentRound];
								SimState segmentState = list3[candidateIndex];
								list16.Add(segmentState);
								list17.Add(list4[candidateIndex]);
								list18.Add(list5[candidateIndex]);
								recoverySelectedIndexes?.Add(candidateIndex);
								alreadySelected.Add(candidateIndex);

								int segmentKey = BfsDiversityBucket(ref segmentState, num79);
								dictionary2.TryGetValue(segmentKey, out int segmentCount);
								dictionary2[segmentKey] = segmentCount + 1;
								int segmentClass = (segmentState.GameMode << 1) |
									(segmentState.Mini ? 1 : 0);
								dictionary3.TryGetValue(segmentClass, out int segmentClassCount);
								dictionary3[segmentClass] = segmentClassCount + 1;
								if (segmentState.GravFlipped) num82++; else num81++;
							}
						}
					}
					flag6 = Math.Min(num81, num82) < list16.Count / 20;
					int diversityLimit = Math.Min(list3.Count,
						num75 + seededSizeModeCandidates.Count +
						seededTransitionCandidates.Count);
					int count = dictionary3.Count;
					bool flag7 = false;
					int num83 = -1;
					if (count > 1)
					{
						int num84 = int.MaxValue;
						foreach (KeyValuePair<int, int> item11 in dictionary3)
						{
							if (item11.Value < num84)
							{
								num84 = item11.Value;
								num83 = item11.Key;
							}
						}
						flag7 = num84 < list16.Count / 10;
					}
					for (int num85 = num76; num85 < list15.Count; num85++)
					{
						if (list16.Count >= diversityLimit)
						{
							break;
						}
						int index5 = list15[num85];
						if (seededSizeModeCandidates.Contains(index5) ||
							seededTransitionCandidates.Contains(index5))
						{
							continue;
						}
						SimState diversityState = list3[index5];
						int key3 = BfsDiversityBucket(ref diversityState, num79);
						dictionary2.TryGetValue(key3, out var value7);
						bool num86 = value7 < num80 + 1;
						bool flag8 = flag6 && ((list3[index5].GravFlipped && num82 < num81) || (!list3[index5].GravFlipped && num81 < num82));
						int sizeModeClass = (list3[index5].GameMode << 1) |
							(list3[index5].Mini ? 1 : 0);
						bool flag9 = flag7 && sizeModeClass == num83;
						if (num86 || flag8 || flag9)
						{
							list16.Add(list3[index5]);
							list17.Add(list4[index5]);
							list18.Add(list5[index5]);
							recoverySelectedIndexes?.Add(index5);
							dictionary2[key3] = value7 + 1;
							if (list3[index5].GravFlipped)
							{
								num82++;
							}
							else
							{
								num81++;
							}
							flag6 = Math.Min(num81, num82) < list16.Count / 20;
							dictionary3.TryGetValue(sizeModeClass, out var value8);
							dictionary3[sizeModeClass] = value8 + 1;
							if (sizeModeClass == num83)
							{
								flag7 = value8 + 1 < list16.Count / 10;
							}
						}
					}
				}
				// A narrow fast beam can prefer a safe trajectory until it is too late
				// to reach an upcoming coin. Temporarily widen only the approach segment,
				// retaining phase-distinct candidates without enlarging the rest of the
				// level. Keep a stable 256-state total around coins. The ordinary beam
				// contributes broadly useful survivors and the remainder is reserved for
				// coin phases; allowing the combined set to grow beyond that changed later
				// phase competition and regressed Base After Base.
				const int FastCoinApproachHorizonPx = 2048;
				int fastCoinReserve = frontierCapOverride.HasValue && PreferCoins
					? Math.Max(0, 256 - frontierCapOverride.Value)
					: 0;
				int fastCoinSeedsAdded = 0;
				if (fastCoinReserve > 0 && list15.Count > num76)
				{
					HashSet<SpriteSet> alreadySelected = new(list16.Select(
						state => state.ProcessedSprites));
					var phaseBest = new Dictionary<(int Coin, int Mode, int ScreenY,
						int VelY, int Motion, int XPhase, int Route),
						(int Index, int Distance)>();
					for (int candidatePos = num76; candidatePos < list15.Count; candidatePos++)
					{
						int candidateIndex = list15[candidatePos];
						SimState candidate = list3[candidateIndex];
						if (alreadySelected.Contains(candidate.ProcessedSprites))
							continue;
						int playerX = candidate.X_fixed >> 8;
						SpriteEntry? targetCoin = null;
						int targetDx = int.MaxValue;
						foreach (SpriteEntry coin in allCoins)
						{
							if (candidate.ProcessedSprites.Contains(coin.Index))
								continue;
							int dx = coin.HitLeft - playerX;
							if (dx < -16 || dx > FastCoinApproachHorizonPx || dx >= targetDx)
								continue;
							targetCoin = coin;
							targetDx = dx;
						}
						if (targetCoin == null)
							continue;
						SpriteEntry selectedCoin = targetCoin.Value;

						int modePhase = (candidate.GameMode << 2) |
							(candidate.GravFlipped ? 2 : 0) | (candidate.Mini ? 1 : 0);
						int screenY = SharedPhysics.NesPlayerScreenY_px(
							candidate.Y_fixed, candidate.CameraY_fixed) >> 2;
						int velocityPhase = (candidate.VelY_fixed + 32768) >> 5;
						int motionPhase = (candidate.OnGround ? 2 : 0) |
							(candidate.PrevInputHeld ? 1 : 0);
						int routePhase = ForcePlatformer
							? BfsPlatformerDirectionClass(in candidate)
							: 0;
						var phaseKey = (selectedCoin.Index, modePhase, screenY,
							velocityPhase, motionPhase, playerX & 0x0F, routePhase);
						int targetY = (selectedCoin.HitTop + selectedCoin.HitBottom) / 2;
						int distance = Math.Abs((candidate.Y_fixed >> 8) + 8 - targetY);
						if (!phaseBest.TryGetValue(phaseKey, out var prior) ||
							distance < prior.Distance)
						{
							phaseBest[phaseKey] = (candidateIndex, distance);
						}
					}

					foreach (var phase in phaseBest.Values
						.OrderBy(value => value.Distance)
						.ThenBy(value => candScore[value.Index]))
					{
						if (fastCoinSeedsAdded >= fastCoinReserve)
							break;
						int seedIndex = phase.Index;
						SimState seed = list3[seedIndex];
						if (!alreadySelected.Add(seed.ProcessedSprites))
							continue;
						list16.Add(seed);
						list17.Add(list4[seedIndex]);
						list18.Add(list5[seedIndex]);
						recoverySelectedIndexes?.Add(seedIndex);
						fastCoinSeedsAdded++;
					}
					if (Verbose && fastCoinSeedsAdded > 0 && frame % 20 == 0)
					{
						_log.WriteLine($"[FAST_COIN_SELECT] f={frame} added={fastCoinSeedsAdded} " +
							$"base={num75} phases={phaseBest.Count}");
					}
				}
				// The recovery pass has already paid to restore a separate corridor and
				// is allowed a larger frontier. The diversity predicates above can stop
				// accepting candidates while tens of thousands of recovery slots remain
				// unused. Fill only those unused slots in the existing score order; this
				// retains every state already chosen by the selector and cannot remove a
				// trajectory from the fallback search.
				if (routeBacktrackAttempted && list16.Count < num75 &&
					recoverySelectedIndexes != null)
				{
					for (int fillPos = num76;
						fillPos < list15.Count && list16.Count < num75;
						fillPos++)
					{
						int fillIndex = list15[fillPos];
						if (!recoverySelectedIndexes.Add(fillIndex))
							continue;
						list16.Add(list3[fillIndex]);
						list17.Add(list4[fillIndex]);
						list18.Add(list5[fillIndex]);
					}
				}
				// Spider orb/pad destinations depend on persistent eject bytes that are
				// invisible in the ordinary score. Keep a bounded, phase-distinct sample
				// as additions above the normal beam, and carry only those spider samples
				// forward while mode 5 remains active. This cannot displace an ordinary
				// candidate or affect other game modes.
				const int MaxSpiderPhaseReserve = 8192;
				var selectedSpiderPhases = new HashSet<(int Mode, int ScreenY, int VelY,
					int Motion, int Slope, int Horizontal, int Camera, int Ejection)>();
				foreach (SimState selectedState in list16)
				{
					if (selectedState.RainbowShadows != null ||
						selectedState.GameMode != 5)
						continue;
					SimState phaseState = selectedState;
					selectedSpiderPhases.Add(BfsSpiderTrajectoryPhase(ref phaseState));
				}
				int spiderPhaseSeedsAdded = 0;
				if (PreferCoins && list15.Count > num76)
				{
					for (int seedPos = num76;
						seedPos < list15.Count && spiderPhaseSeedsAdded < MaxSpiderPhaseReserve;
						seedPos++)
					{
						int seedIndex = list15[seedPos];
						SimState seedState = list3[seedIndex];
						if (seedState.RainbowShadows != null ||
							seedState.GameMode != 5 ||
							!selectedSpiderPhases.Add(BfsSpiderTrajectoryPhase(ref seedState)))
						{
							continue;
						}
						seedState.BfsSpiderReserved = true;
						list16.Add(seedState);
						list17.Add(list4[seedIndex]);
						list18.Add(list5[seedIndex]);
						spiderPhaseSeedsAdded++;
					}
				}
				int inheritedSpiderReserveAdded = 0;
				foreach (int reserveIndex in inheritedSpiderReserveIndexes)
				{
					if (inheritedSpiderReserveAdded >= MaxSpiderPhaseReserve)
						break;
					SimState reserveState = list3[reserveIndex];
					if (!selectedSpiderPhases.Add(BfsSpiderTrajectoryPhase(ref reserveState)))
						continue;
					list16.Add(reserveState);
					list17.Add(list4[reserveIndex]);
					list18.Add(list5[reserveIndex]);
					inheritedSpiderReserveAdded++;
				}
				// Bias is a preference, never a hard input restriction. When the ordinary
				// coin beam is capped, retain a small additive sample of timing-distinct
				// states from outside its selected prefix. These states cannot displace the
				// requested-bias path. If that path is unsafe, they carry the nearest local
				// delay/advance through the obstacle and are rescored under the requested
				// bias immediately afterward.
				const int MaxTimingFallbackReserve = 4096;
				int timingFallbackAdded = 0;
				if (PreferCoins && Math.Abs(JumpTimingBias - 0.5) >= 0.01 &&
					list15.Count > num76)
				{
					HashSet<SpriteSet> alreadySelected = new(list16.Count);
					var timingPhases = new HashSet<(int Mode, int Gravity,
						int Input, int Age, int ScreenY, int VelY, int Cost)>();
					foreach (SimState selectedState in list16)
					{
						alreadySelected.Add(selectedState.ProcessedSprites);
						timingPhases.Add((selectedState.GameMode,
							selectedState.GravFlipped ? 1 : 0,
							selectedState.PrevInputHeld ? 1 : 0,
							Math.Min(31, selectedState.BfsTimingOpportunityAge >> 1),
							SharedPhysics.NesPlayerScreenY_px(selectedState.Y_fixed,
								selectedState.CameraY_fixed) >> 3,
							selectedState.VelY_fixed >> 7,
							selectedState.BfsTimingLocalCost >> 12));
					}
					for (int seedPos = num76; seedPos < list15.Count &&
						timingFallbackAdded < MaxTimingFallbackReserve; seedPos++)
					{
						int seedIndex = list15[seedPos];
						SimState seedState = list3[seedIndex];
						if (alreadySelected.Contains(seedState.ProcessedSprites))
							continue;
						var phase = (seedState.GameMode,
							seedState.GravFlipped ? 1 : 0,
							seedState.PrevInputHeld ? 1 : 0,
							Math.Min(31, seedState.BfsTimingOpportunityAge >> 1),
							SharedPhysics.NesPlayerScreenY_px(seedState.Y_fixed,
								seedState.CameraY_fixed) >> 3,
							seedState.VelY_fixed >> 7,
							seedState.BfsTimingLocalCost >> 12);
						if (!timingPhases.Add(phase))
							continue;
						alreadySelected.Add(seedState.ProcessedSprites);
						list16.Add(seedState);
						list17.Add(list4[seedIndex]);
						list18.Add(list5[seedIndex]);
						timingFallbackAdded++;
					}
				}
				if (Verbose && timingFallbackAdded > 0 && frame % 30 == 0)
				{
					_log.WriteLine($"[TIMING_FALLBACK] f={frame} " +
						$"bias={JumpTimingBias:F2} added={timingFallbackAdded}");
				}
				if (routeDiagnostics && diagnosticSpiderOrbKey >= 0 &&
					diagnosticX >= 10380 && diagnosticX <= 10440)
				{
					int selectedOrbCount = list16.Count(state =>
						state.ProcessedSprites.Contains(diagnosticSpiderOrbKey));
					int selectedTeleportCount = list16.Count(state =>
						state.GameMode == 5 && !state.GravFlipped &&
						(state.Y_fixed >> 8) >= 360);
					int readyParents = frontier.Count(state => state.GameMode == 5 &&
						state.GravFlipped && (state.Y_fixed >> 8) >= 345 &&
						(state.Y_fixed >> 8) <= 355);
					int readyReleasedParents = frontier.Count(state => state.GameMode == 5 &&
						state.GravFlipped && (state.Y_fixed >> 8) >= 345 &&
						(state.Y_fixed >> 8) <= 355 && !state.PrevInputHeld);
					int readyLatchedParents = frontier.Count(state => state.GameMode == 5 &&
						state.GravFlipped && (state.Y_fixed >> 8) >= 345 &&
						(state.Y_fixed >> 8) <= 355 && state.AirPressLatch);
					string ejectPhases = string.Join(",", frontier
						.Where(state => state.GameMode == 5 && state.GravFlipped &&
							(state.Y_fixed >> 8) >= 345 && (state.Y_fixed >> 8) <= 355)
						.GroupBy(state => state.EjectU)
						.OrderByDescending(group => group.Count()).Take(8)
						.Select(group => $"{group.Key:X2}:{group.Count()}"));
					string activationPhases = diagnosticX >= 10405 && diagnosticX <= 10412
						? string.Join(",", frontier
							.Where(state => state.GameMode == 5 && state.GravFlipped &&
								(state.Y_fixed >> 8) >= 348 && (state.Y_fixed >> 8) <= 354)
							.GroupBy(state => (Y: state.Y_fixed >> 8, state.PrevInputHeld,
								state.AirPressLatch, state.EjectU))
							.OrderByDescending(group => group.Count()).Take(16)
							.Select(group => $"{group.Key.Y}:p{(group.Key.PrevInputHeld ? 1 : 0)}" +
								$"l{(group.Key.AirPressLatch ? 1 : 0)}e{group.Key.EjectU:X2}:" +
								$"{group.Count()}"))
						: "";
					int minY = list16.Count > 0 ? list16.Min(state => state.Y_fixed >> 8) : -1;
					int maxY = list16.Count > 0 ? list16.Max(state => state.Y_fixed >> 8) : -1;
					Console.Error.WriteLine($"[SPIDER_ORB_SEARCH] f={frame} x={diagnosticX} " +
						$"orb={diagnosticCandidateOrbCount}/{diagnosticDedupOrbCount}/{selectedOrbCount} " +
						$"raw={diagnosticRawOrbCount}/{diagnosticRawTeleportCount} " +
						$"tele={diagnosticAliveTeleportCount}/{diagnosticCandidateTeleportCount}/" +
						$"{diagnosticDedupTeleportCount}/{selectedTeleportCount} " +
						$"counts={list3.Count}/{dictionary.Count}/{list16.Count} " +
						$"ready={readyParents}/{readyReleasedParents}/{readyLatchedParents} " +
						$"ejU=[{ejectPhases}] phase=[{activationPhases}] y={minY}..{maxY} " +
						$"seed={spiderPhaseSeedsAdded} inherited={inheritedSpiderReserveAdded}");
				}
				if (routeDiagnostics && routeBacktrackAttempted && frame % 30 == 0)
				{
					Console.Error.WriteLine($"[ROUTE_RECOVERY] f={frame} " +
						$"x={list16[0].X_fixed >> 8} candidates={list15.Count} " +
						$"selected={list16.Count} cap={num75}");
				}
				// When a capped search splits into separate vertical corridors, preserve
				// the non-selected upper corridor. Restoring every corridor together simply
				// lets the score choose the same lower dead end a second time.
				const int MaxRouteArchiveStates = 60000;
				if (PreferCoins && !routeBacktrackAttempted &&
					list15.Count > BaseCoinFrontierCap &&
					TryFindBfsVerticalRouteSplit(list3, list15, out int routeSplitY,
						out int upperRouteCount, out int lowerRouteCount))
				{
					SimState routeProbe = list3[list15[0]];
					if (TryGetUpcomingBfsTransitionPortal(ref routeProbe, 2048,
						out BfsTransitionPortal routePortal))
					{
						int routeSourceX = routeProbe.X_fixed >> 8;
						List<int> forkRouteIndexes = new(Math.Min(upperRouteCount,
							MaxRouteArchiveStates));
						foreach (int candidateIndex in list15)
						{
							if ((list3[candidateIndex].Y_fixed >> 8) <= routeSplitY)
								forkRouteIndexes.Add(candidateIndex);
						}
						bool isInteriorBand = false;
						int archiveSplitY = routeSplitY;

						// Early jump arcs can look like separate routes even though they merge
						// immediately. Prefer a materially later split when one remains visible;
						// that snapshot is much closer to the actual corridor commitment and
						// avoids replaying hundreds of frames of the same locally preferred path.
						bool materiallyLaterSplit = routeArchive == null ||
							routeSourceX >= routeArchive.SourceX + 256;
						// Do not keep sliding the only fallback checkpoint into the
						// final approach. A beam can discard the viable setup several
						// jumps before the visible portal; rewinding to a snapshot taken
						// after that commitment simply repeats the same dead corridor.
						const int RouteCommitmentWindowPx = 768;
						bool beforeCommitmentWindow =
							routeSourceX <= routePortal.X - RouteCommitmentWindowPx;
						if (forkRouteIndexes.Count >= 2048 && materiallyLaterSplit &&
							beforeCommitmentWindow)
						{
							BfsRouteArchive replacement = new()
							{
								Frame = list.Count,
								SourceX = routeSourceX,
								SplitY = archiveSplitY,
								IsInteriorBand = isInteriorBand,
								TargetX = routePortal.X,
								TargetY = routePortal.Y
							};
							// A straight even sample can retain tens of thousands of runtime-distinct
							// states that nevertheless share the same physical arc.  Seed the archive
							// with every distinct approach phase first, then use the remaining capacity
							// for the ordinary even sample.  This keeps rare jump-off/slope phases needed
							// to enter Dastardly's ship portal without changing the normal beam.
							List<int> archiveIndexes = new(Math.Min(MaxRouteArchiveStates,
								forkRouteIndexes.Count));
							HashSet<int> archivedCandidateIndexes = new();
							HashSet<(int X, int ScreenY, int VelY, int Motion, int Slope)>
								archivedPhases = new();
							foreach (int candidateIndex in forkRouteIndexes)
							{
								SimState phaseState = list3[candidateIndex];
								int motion = (phaseState.OnGround ? 1 : 0) |
									(phaseState.PrevInputHeld ? 2 : 0) |
									(phaseState.AirPressLatch ? 4 : 0) |
									(phaseState.GravFlipped ? 8 : 0);
								int slope = (phaseState.SlopeType & 0xFF) |
									((phaseState.LastSlopeType & 0xFF) << 8) |
									((Math.Min(phaseState.SlopeFrames, 7) & 0x07) << 16) |
									((Math.Min(phaseState.SlopeWasOnCounter, 7) & 0x07) << 19);
								var phase = (
									phaseState.X_fixed >> 7,
									SharedPhysics.NesPlayerScreenY_px(phaseState.Y_fixed,
										phaseState.CameraY_fixed) >> 1,
									phaseState.VelY_fixed >> 5,
									motion,
									slope);
								if (!archivedPhases.Add(phase))
									continue;
								archiveIndexes.Add(candidateIndex);
								archivedCandidateIndexes.Add(candidateIndex);
								if (archiveIndexes.Count >= MaxRouteArchiveStates)
									break;
							}
							int desiredArchiveCount = Math.Min(MaxRouteArchiveStates,
								forkRouteIndexes.Count);
							for (int archivePos = 0;
								archiveIndexes.Count < desiredArchiveCount &&
								archivePos < desiredArchiveCount;
								archivePos++)
							{
								int routePos = (int)((long)archivePos *
									forkRouteIndexes.Count / desiredArchiveCount);
								int candidateIndex = forkRouteIndexes[routePos];
								if (archivedCandidateIndexes.Add(candidateIndex))
									archiveIndexes.Add(candidateIndex);
							}
							int archiveCount = archiveIndexes.Count;
							int[] archiveParents = new int[archiveCount];
							bool[] archiveInputs = new bool[archiveCount];
							sbyte[] archiveDirections = new sbyte[archiveCount];
							for (int archivePos = 0; archivePos < archiveCount; archivePos++)
							{
								int candidateIndex = archiveIndexes[archivePos];
								SimState archivedState = list3[candidateIndex].Clone();
								replacement.States.Add(archivedState);
								archiveParents[archivePos] = list4[candidateIndex];
								archiveInputs[archivePos] = list5[candidateIndex];
								archiveDirections[archivePos] =
									list3[candidateIndex].SearchDirectionUsed;
							}
							replacement.Parents = archiveParents;
							replacement.Inputs = archiveInputs;
							replacement.Directions = archiveDirections;
							routeArchive?.ReturnResources();
							routeArchive = replacement;
							if (routeDiagnostics)
							{
								int[] routeYs = list15.Select(candidateIndex =>
									list3[candidateIndex].Y_fixed >> 8).OrderBy(y => y).ToArray();
								Console.Error.WriteLine($"[ROUTE_ARCHIVE] f={frame} " +
									$"x={routeProbe.X_fixed >> 8} splitY={routeSplitY} " +
									$"archiveSplitY={archiveSplitY} interior={(isInteriorBand ? 1 : 0)} " +
									$"upper={upperRouteCount} lower={lowerRouteCount} " +
									$"saved={archiveCount} target=({routePortal.X},{routePortal.Y}) " +
									$"ys={routeYs[0]}/{routeYs[routeYs.Length / 4]}/" +
									$"{routeYs[routeYs.Length / 2]}/{routeYs[routeYs.Length * 3 / 4]}/" +
									$"{routeYs[^1]}");
							}
							if (Verbose)
							{
								_log.WriteLine($"[ROUTE_ARCHIVE] f={frame} splitY={routeSplitY} " +
									$"upper={upperRouteCount} lower={lowerRouteCount} " +
									$"saved={archiveCount} targetX={routePortal.X}");
							}
						}
					}
				}
				if (Verbose)
				{
					int num87 = 0;
					foreach (SimState item12 in list16)
					{
						if (item12.GravFlipped)
						{
							num87++;
						}
					}
					int num88 = 0;
					foreach (int value15 in dictionary.Values)
					{
						if (list3[value15].GravFlipped)
						{
							num88++;
						}
					}
					if (num88 > 0 && num87 != num88)
					{
						_log.WriteLine($"[GF_SELECT] f={frame} dedupGF={num88} selectedGF={num87} totalNext={list16.Count}");
					}
				}
				if (platformerVisited != null)
				{
					foreach (SimState selectedPlatformerState in list16)
					{
						SimState visitedState = selectedPlatformerState;
						platformerVisited.Add(BuildBfsDedupKey(ref visitedState));
					}
				}
				if (list16.Count != list3.Count)
				{
					HashSet<SpriteSet> hashSet = new HashSet<SpriteSet>(list16.Count);
					foreach (SimState item13 in list16)
					{
						hashSet.Add(item13.ProcessedSprites);
					}
					for (int num89 = 0; num89 < list3.Count; num89++)
					{
						if (!hashSet.Contains(list3[num89].ProcessedSprites))
						{
							list3[num89].ReturnAllSpriteResources();
						}
					}
				}
				list.Add(list17.ToArray());
				list2.Add(list18.ToArray());
				directionHistory.Add(list16.Select(state =>
					state.SearchDirectionUsed).ToArray());
				for (int num90 = 0; num90 < list16.Count; num90++)
				{
					int num91 = list16[num90].X_fixed >> 8;
					if (num91 > num10)
					{
						num8 = list.Count - 1;
						num9 = num90;
						num10 = num91;
					}
				}
				foreach (SimState item14 in frontier)
				{
					item14.ReturnAllSpriteResources();
				}
				frontier = list16;
				if (routeDiagnostics && ForcePlatformer && frame % 100 == 0)
				{
					int leftCount = 0;
					int neutralCount = 0;
					int rightCount = 0;
					int committedLeft = 0;
					int minPlatformerX = int.MaxValue;
					int maxPlatformerX = int.MinValue;
					foreach (SimState platformerState in frontier)
					{
						int platformerX = platformerState.X_fixed >> 8;
						minPlatformerX = Math.Min(minPlatformerX, platformerX);
						maxPlatformerX = Math.Max(maxPlatformerX, platformerX);
						if (platformerState.SearchDirectionUsed < 0)
						{
							leftCount++;
							if (platformerState.SearchDirectionRun >= 8)
								committedLeft++;
						}
						else if (platformerState.SearchDirectionUsed > 0)
							rightCount++;
						else
							neutralCount++;
					}
					Console.Error.WriteLine($"[PLATFORMER_FRONTIER] f={frame} " +
						$"states={frontier.Count} left={leftCount} left8={committedLeft} " +
						$"neutral={neutralCount} right={rightCount} " +
						$"x={minPlatformerX}..{maxPlatformerX}");
				}
				bool flag10 = frontier.Count > 0 && frontier[0].DualActive;
				if (frame % 100 == 0 || frontier.Count < 100 || (frame >= 700 && frame <= 810) || flag10 ||
					(frame >= 2000 && frame <= 2070) || (frame >= 3550 && frame <= 3850))
				{
					int num92 = int.MaxValue;
					int num93 = int.MinValue;
					int frontierMinX = int.MaxValue;
					int frontierMaxX = int.MinValue;
					int num94 = 0;
					int num95 = 0;
					foreach (SimState item15 in frontier)
					{
						int num96 = item15.Y_fixed >> 8;
						int frontierX = item15.X_fixed >> 8;
						frontierMinX = Math.Min(frontierMinX, frontierX);
						frontierMaxX = Math.Max(frontierMaxX, frontierX);
						if (num96 < num92)
						{
							num92 = num96;
						}
						if (num96 > num93)
						{
							num93 = num96;
						}
						if (item15.GravFlipped)
						{
							num95++;
						}
						else
						{
							num94++;
						}
					}
					double value9 = stopwatch.Elapsed.TotalMilliseconds / (double)Math.Max(1, frame + 1);
					string value10 = "";
					if (flag10)
					{
						int num97 = int.MaxValue;
						int num98 = int.MinValue;
						foreach (SimState item16 in frontier)
						{
							int num99 = item16.P2_Y_fixed >> 8;
							if (num99 < num97)
							{
								num97 = num99;
							}
							if (num99 > num98)
							{
								num98 = num99;
							}
						}
						value10 = $" DUAL P2_Y=[{num97}..{num98}]";
					}
					int rainbowEnsembles = 0;
					int maxRainbowBranches = 1;
					foreach (SimState rainbowState in frontier)
					{
						if (rainbowState.RainbowShadows == null)
							continue;
						rainbowEnsembles++;
						maxRainbowBranches = Math.Max(maxRainbowBranches,
							1 + rainbowState.RainbowShadows.Length);
					}
					if (rainbowEnsembles > 0)
					{
						value10 += $" RAINBOW={rainbowEnsembles}x{maxRainbowBranches}";
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
					for (int num100 = 0; num100 < 12; num100++)
					{
						if (array7[num100] > 0)
						{
							text2 += $" m{num100}={array7[num100]}";
						}
					}
					if (Verbose)
					{
						_log.WriteLine($"[BFS] f={frame} front={frontier.Count} dedup={dictionary.Count} deaths={num15} Y=[{num92}..{num93}] mode={frontier[0].GameMode} gravN={num94} gravF={num95} X=[{frontierMinX}..{frontierMaxX}] best={num3}px pct={num13}% ms/f={value9:F1}{value10} modes:{text2}");
					}
					if (Verbose && frame >= 3550 && frame <= 3850 && frame % 10 == 0)
					{
						int num101 = 0;
						int num102 = 0;
						int num103 = 0;
						int num104 = 0;
						int num105 = 0;
						int num106 = 0;
						int num107 = 0;
						foreach (SimState item18 in frontier)
						{
							int num108 = item18.Y_fixed >> 8;
							if (num108 < 135)
							{
								num101++;
							}
							else if (num108 <= 150)
							{
								num102++;
							}
							else if (num108 <= 167)
							{
								num103++;
								num107++;
							}
							else if (num108 <= 200)
							{
								num104++;
							}
							else if (num108 <= 250)
							{
								num105++;
							}
							else
							{
								num106++;
							}
						}
						_log.WriteLine($"[BFS_COIN_YHIST] f={frame} X~{num3}px <135={num101} 135-150={num102} 151-167={num103} 168-200={num104} 201-250={num105} 251+={num106} coinHitY={num107}");
					}
					if (Verbose && (frame == 700 || frame == 710 || frame == 720 || frame == 800 || frame == 900 || frame == 1500 || frame == 2020 || frame == 2035 || frame == 2040))
					{
						int num109 = 0;
						int num110 = int.MaxValue;
						int num111 = int.MinValue;
						int num112 = int.MaxValue;
						int num113 = int.MinValue;
						for (int num114 = 0; num114 < frontier.Count; num114++)
						{
							SimState simState9 = frontier[num114];
							int num115 = simState9.Y_fixed >> 8;
							if (simState9.Step2Ever)
							{
								num109++;
								if (num115 < num112)
								{
									num112 = num115;
								}
								if (num115 > num113)
								{
									num113 = num115;
								}
							}
							else
							{
								if (num115 < num110)
								{
									num110 = num115;
								}
								if (num115 > num111)
								{
									num111 = num115;
								}
							}
						}
						_log.WriteLine($"[BFS_S2] f={frame} Step2Ever={num109}/{frontier.Count} noS2_Y=[{((num110 == int.MaxValue) ? "N/A" : num110.ToString())}..{((num111 == int.MinValue) ? "N/A" : num111.ToString())}] s2_Y=[{((num112 == int.MaxValue) ? "N/A" : num112.ToString())}..{((num113 == int.MinValue) ? "N/A" : num113.ToString())}]");
					}
					if (Verbose && frame >= 2020 && frame <= 2042 && frame % 2 == 0)
					{
						int num116 = 0;
						int num117 = 0;
						int num118 = 0;
						int num119 = 0;
						int num120 = 0;
						int num121 = 0;
						int num122 = 0;
						int num123 = 0;
						for (int num124 = 0; num124 < frontier.Count; num124++)
						{
							int num125 = frontier[num124].Y_fixed >> 8;
							if (num125 < 288)
							{
								num116++;
							}
							else if (num125 < 304)
							{
								num117++;
							}
							else if (num125 < 320)
							{
								num118++;
							}
							else if (num125 < 336)
							{
								num119++;
								if (frontier[num124].GravFlipped)
								{
									num123++;
								}
								else
								{
									num122++;
								}
							}
							else if (num125 < 352)
							{
								num120++;
							}
							else
							{
								num121++;
							}
						}
						_log.WriteLine($"[BFS_YHIST] f={frame} <288={num116} 288-303={num117} 304-319={num118} 320-335={num119}(gN={num122},gF={num123}) 336-351={num120} 352+={num121}");
						if (num119 > 0)
						{
							int num126 = 0;
							int num127 = 0;
							int num128 = int.MaxValue;
							int num129 = int.MinValue;
							for (int num130 = 0; num130 < frontier.Count; num130++)
							{
								int num131 = frontier[num130].Y_fixed >> 8;
								if (num131 >= 320 && num131 < 336)
								{
									int num132 = frontier[num130].X_fixed >> 8;
									if (num132 < num128)
									{
										num128 = num132;
									}
									if (num132 > num129)
									{
										num129 = num132;
									}
									if (num132 >= num3 - 200)
									{
										num126++;
									}
									else
									{
										num127++;
									}
								}
							}
							_log.WriteLine($"[BFS_GAPX] f={frame} count={num119} X=[{num128}..{num129}] near(within200)={num126} far={num127}");
						}
					}
					if (Verbose)
					{
						_log.Flush();
					}
					if (Verbose && frame >= 703 && frame <= 810)
					{
						long num133 = 0L;
						for (int num134 = 0; num134 < frontier.Count; num134++)
						{
							SimState simState10 = frontier[num134];
							num133 = num133 * 31 + simState10.X_fixed + (long)simState10.Y_fixed * 7L + (long)simState10.VelY_fixed * 13L + (simState10.GravFlipped ? 1 : 0) + (long)simState10.GameMode * 37L;
						}
						string value11 = $"[BFS_HASH] f={frame} frontHash=0x{num133:X16} first=(X=0x{frontier[0].X_fixed:X},Y=0x{frontier[0].Y_fixed:X},VelY=0x{frontier[0].VelY_fixed:X},mode={frontier[0].GameMode}) last=(X=0x{frontier[frontier.Count - 1].X_fixed:X},Y=0x{frontier[frontier.Count - 1].Y_fixed:X},VelY=0x{frontier[frontier.Count - 1].VelY_fixed:X},mode={frontier[frontier.Count - 1].GameMode})";
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
						for (int num135 = 0; num135 < array8.Length && num135 < frameDtCounts.Length; num135++)
						{
							if (frameDtCounts[num135] > 0)
							{
								list19.Add($"{array8[num135]}={frameDtCounts[num135]}");
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
						int num136 = 0;
						for (int num137 = 0; num137 < _gravFDeathCounts.Length; num137++)
						{
							if (_gravFDeathCounts[num137] > 0)
							{
								list20.Add($"{array9[num137]}={_gravFDeathCounts[num137]}");
								num136 += _gravFDeathCounts[num137];
							}
						}
						_log.WriteLine($"[GRAVF_SUMMARY] f={frame} total={num136} {string.Join(" ", list20)}");
						_gravFDeathCounts = null;
						_gravFDeathSamples = 0;
					}
				}
				if (OnSpeculativePath == null || frontier.Count <= 0 || frame % 5 != 0)
				{
					continue;
				}
				int num138 = Math.Min(8, frontier.Count);
				int num139 = Math.Max(1, frontier.Count / num138);
				_speculativeDepth++;
				for (int num140 = 0; num140 < frontier.Count && num140 / num139 < num138; num140 += num139)
				{
					SimState simState11 = frontier[num140];
					int hitboxW = GetHitboxW(simState11.Mini);
					SimState s4 = simState11.Clone();
					List<(int, int)> list21 = new List<(int, int)>();
					int arg = 0;
					for (int num141 = 0; num141 < 30; num141++)
					{
						int num142 = s4.X_fixed >> 8;
						int num143 = s4.Y_fixed >> 8;
						list21.Add((num142 + hitboxW / 2, num143 + 8));
						// Project each platformer branch in the direction it is actually
						// exploring. The old convenience overload always forced right, which
						// made live search appear to have no left-moving branches.
						sbyte projectedDirection = ForcePlatformer
							? simState11.SearchDirectionUsed
							: (sbyte)0;
						if (!StepFrame(ref s4, input: false, projectedDirection,
							out var endLevel) || endLevel)
						{
							break;
						}
						arg = num141 + 1;
					}
					s4.ReturnAllSpriteResources();
					if (list21.Count >= 2)
					{
						OnSpeculativePath(list21, 0, arg, arg4: false);
					}
					SimState s5 = simState11.Clone();
					List<(int, int)> list22 = new List<(int, int)>();
					int arg2 = 0;
					for (int num144 = 0; num144 < 30; num144++)
					{
						int num145 = s5.X_fixed >> 8;
						int num146 = s5.Y_fixed >> 8;
						list22.Add((num145 + hitboxW / 2, num146 + 8));
						bool input = num144 == 0;
						if (!StepFrame(ref s5, input, out var endLevel2) || endLevel2)
						{
							break;
						}
						arg2 = num144 + 1;
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
				List<sbyte> winningDirections = new List<sbyte>();
				int num147 = num6;
				for (int num148 = num5 - 1; num148 >= 0; num148--)
				{
					list23.Add(list2[num148][num147]);
					winningDirections.Add(directionHistory[num148][num147]);
					num147 = list[num148][num147];
				}
				list23.Reverse();
				winningDirections.Reverse();
				list23.Add(item);
				winningDirections.Add(simState2.SearchDirectionUsed);
				_log.WriteLine($"[BFS] Replaying winning path ({list23.Count} frames)...");
				bool canonicalReplayCompleted = ReplayBfsPath(list23, winningDirections, startX_px, startY_px,
					startSpeedUiIndex, startGameMode, startGravFlipped, startMini);
				int count2 = allCoins.Count;
				// Coin splicing is intentionally reserved for the exhaustive pass. It can
				// explore well beyond the fast pass's deadline and defeat its bounded cost.
				if (!frontierCapOverride.HasValue && canonicalReplayCompleted && !ForcePlatformer &&
					PreferCoins && count2 > 0 && FinalCollectedCoinIndices != null &&
					FinalCollectedCoinIndices.Count < count2)
				{
					List<bool>? list24 = TryCoinBeamSplice(list23, startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped, startMini);
					if (list24 != null)
					{
						list23 = list24;
						canonicalReplayCompleted = ReplayBfsPath(list23, startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped, startMini);
					}
				}
				double totalSeconds = stopwatch.Elapsed.TotalSeconds;
				if (!canonicalReplayCompleted)
				{
					ResultMessage = $"BFS candidate failed its canonical replay [{totalSeconds:F1}s BFS]";
					Success = false;
					_log.WriteLine("[BFS] " + ResultMessage);
				}
				else
				{
					string text3 = $"Completed in {list23.Count} frames ({PathPoints.Count} path points)";
					if (PreferCoins && count2 > 0)
					{
						text3 += $" [{FinalCollectedCoinIndices?.Count ?? num7}/{count2} coins]";
					}
					string searchLabel = frontierCapOverride.HasValue ? "fast exact" : "BFS";
					text3 = (ResultMessage = text3 + $" [{totalSeconds:F1}s {searchLabel}]");
					Success = true;
					_log.WriteLine("[BFS] " + text3);
				}
			}
			else
			{
				_speculativeDepth = 0;
				List<bool>? bestFailedInputs = null;
				List<sbyte>? bestFailedDirections = null;
				if (num9 >= 0 && list.Count > 0)
				{
					bestFailedInputs = new List<bool>();
					bestFailedDirections = new List<sbyte>();
					int num149 = num9;
					for (int num150 = num8; num150 >= 0; num150--)
					{
						bestFailedInputs.Add(list2[num150][num149]);
						bestFailedDirections.Add(directionHistory[num150][num149]);
						num149 = list[num150][num149];
					}
					bestFailedInputs.Reverse();
					bestFailedDirections.Reverse();
				}
				if (primaryFailedInputs != null && primaryFailedX > num10)
				{
					bestFailedInputs = primaryFailedInputs;
					bestFailedDirections = primaryFailedDirections;
				}
				if (bestFailedInputs != null)
					ReplayBfsPath(bestFailedInputs, bestFailedDirections, startX_px, startY_px,
						startSpeedUiIndex, startGameMode, startGravFlipped, startMini);
				int num151 = ((PathPoints.Count > 0) ? PathPoints[PathPoints.Count - 1].x : 0);
				int value13 = ((num2 > 0) ? (num151 * 100 / num2) : 0);
				ResultMessage = $"BFS failed ~ best path to X={num151}px ({value13}%)";
				Success = false;
				_log.WriteLine("[BFS] " + ResultMessage);
				_log.WriteLine($"[BFS_DIAG] Step1 fires={_step1FireCount}, Step2 fires={_step2FireCount}");
			}
			foreach (SimState state in frontier)
				state.ReturnAllSpriteResources();
			if (num5 >= 0)
				simState2.ReturnAllSpriteResources();
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
		routeArchive?.ReturnResources();
		_bfsSearchActive = false;
		stopwatch.Stop();
	}

	private int CountBfsCoins(ref SimState s)
	{
		if (allCoins.Count == 0)
		{
			return 0;
		}
		int minimum = CountBfsCoinsForBranch(ref s);
		if (s.RainbowShadows != null)
		{
			for (int i = 0; i < s.RainbowShadows.Length; i++)
			{
				SimState shadow = s.RainbowShadows[i];
				minimum = Math.Min(minimum, CountBfsCoinsForBranch(ref shadow));
			}
		}
		return minimum;
	}

	private int CountBfsCoinsForBranch(ref SimState s)
	{
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

	private PathfinderReplayFrame CaptureReplayFrame(int frameIndex, bool input,
		sbyte horizontalDirection, in SimState s, bool alive, bool endLevel,
		HashSet<int> replayCollectedCoins)
	{
		List<int>? newlyCollected = null;
		foreach (SpriteEntry coin in allCoins)
		{
			if (s.ProcessedSprites.Contains(coin.Index) &&
				replayCollectedCoins.Add(coin.Index))
			{
				(newlyCollected ??= new List<int>()).Add(coin.Index);
			}
		}

		return new PathfinderReplayFrame
		{
			FrameIndex = frameIndex,
			InputHeld = input,
			HorizontalDirection = horizontalDirection,
			XFixed = s.X_fixed,
			YFixed = s.Y_fixed,
			VelXFixed = s.VelX_fixed,
			VelYFixed = s.VelY_fixed,
			GlobalSpeedFixed = s.GlobalSpeed_fixed,
			ScrollXPx = GetScrollX_px(in s),
			CurrXScrollStopFixed = s.CurrXScrollStop_fixed,
			TargetXScrollStopFixed = s.TargetXScrollStop_fixed,
			CameraYFixed = s.CameraY_fixed,
			TargetCameraYFixed = s.TargetCameraY_fixed,
			ScrollYSubpx = s.ScrollYSubpx,
			GameMode = s.GameMode,
			Mini = s.Mini,
			GravityFlipped = s.GravFlipped,
			WasZeroedByCollision = s.WasZeroedByCollision,
			OnGround = s.OnGround,
			GravityMultiplier = s.GravityMod,
			Dashing = s.Dashing,
			NinjaJumps = s.NinjaJumps,
			RobotJumpTime = s.RobotJumpTime,
			SlopeWasOnCounter = s.SlopeWasOnCounter,
			SlopeFrames = s.SlopeFrames,
			SlopeType = s.SlopeType,
			LastSlopeType = s.LastSlopeType,
			InvincibleCounter = s.InvincibleCounter,
			NoCamLockForced = s.NoCamLockForced,
			WrapMode = s.WrapMode,
			SlowMode = s.SlowMode,
			PlayerInvisible = s.PlayerInvisible,
			ForcedTrails = s.ForcedTrails,
			ExitPortalTimer = s.ExitPortalTimer,
			DualActive = s.DualActive,
			P2YFixed = s.P2_Y_fixed,
			P2VelXFixed = s.P2_VelX_fixed,
			P2VelYFixed = s.P2_VelY_fixed,
			P2Mini = s.P2_Mini,
			P2GravityFlipped = s.P2_GravFlipped,
			P2WasZeroedByCollision = s.P2_WasZeroedByCollision,
			P2OnGround = s.P2_OnGround,
			P2Dashing = s.P2_Dashing,
			P2NinjaJumps = s.P2_NinjaJumps,
			P2RobotJumpTime = s.P2_RobotJumpTime,
			P2SlopeWasOnCounter = s.P2_SlopeWasOnCounter,
			P2SlopeFrames = s.P2_SlopeFrames,
			P2SlopeType = s.P2_SlopeType,
			P2LastSlopeType = s.P2_LastSlopeType,
			Alive = alive,
			EndLevel = endLevel,
			DeathType = s.DeathType,
			DeathX = alive ? -1 : _lastDeathX,
			DeathY = alive ? -1 : _lastDeathY,
			NewlyCollectedCoinIndices =
				newlyCollected?.ToArray() ?? Array.Empty<int>(),
			BackgroundColorTriggerIndex = s.ReplayBackgroundColorTriggerIndex,
			BackgroundColorTriggerSpriteId = s.ReplayBackgroundColorTriggerSpriteId,
			ObjectColorTriggerIndex = s.ReplayObjectColorTriggerIndex,
			ObjectColorTriggerSpriteId = s.ReplayObjectColorTriggerSpriteId,
			GroundColorTriggerIndex = s.ReplayGroundColorTriggerIndex,
			GroundColorTriggerSpriteId = s.ReplayGroundColorTriggerSpriteId
		};
	}

	private bool ReplayBfsPath(List<bool> inputSequence, int startX_px, int startY_px,
		int startSpeedUiIndex, int startGameMode, bool startGravFlipped, bool startMini)
	{
		return ReplayBfsPath(inputSequence, null, startX_px, startY_px,
			startSpeedUiIndex, startGameMode, startGravFlipped, startMini);
	}

	private bool ReplayBfsPath(List<bool> inputSequence,
		IReadOnlyList<sbyte>? horizontalSequence, int startX_px, int startY_px,
		int startSpeedUiIndex, int startGameMode, bool startGravFlipped, bool startMini)
	{
		int num = ComputeInitCameraY(startY_px);
		SimState simState = default(SimState);
		simState.X_fixed = InitialXFixed(startX_px);
		simState.Y_fixed = (startY_px << 8) | SpawnYSubpx;
		simState.VelX_fixed = SpeedUiIndexToFixed(startSpeedUiIndex);
		simState.GlobalSpeed_fixed = simState.VelX_fixed;
		simState.VelY_fixed = 0;
		simState.GameMode = startGameMode;
		simState.GravFlipped = startGravFlipped;
		simState.Mini = startMini;
		simState.GravMul = ((!startGravFlipped) ? 1 : (-1));
		simState.GravityMod = 1.0;
		simState.WasZeroedByCollision = true;
		simState.OnGround = true;
		simState.ProcessedSprites = NewSpriteSet();
		simState.PendingOrbIndex = -1;
		simState.PendingOrbSpriteId = -1;
		simState.PendingOrbExtra1Index = -1;
		simState.PendingOrbExtra1SpriteId = -1;
		simState.PendingOrbExtra2Index = -1;
		simState.PendingOrbExtra2SpriteId = -1;
		simState.NinjaJumps = ((startGameMode == 8) ? 3 : 0);
		simState.CameraY_fixed = num;
		simState.TargetCameraY_fixed = num;
		InitializeHorizontalState(ref simState);
		SimState s = simState;
		ApplyPortalsUpTo(ref s, startX_px);
		InitNesSlots(ref s);
		ApplyNesIntroFreezePrestep(ref s);
		CheckSprObjects(ref s);
		PathPoints.Clear();
		Path2Points.Clear();
		Inputs.Clear();
		HorizontalInputs.Clear();
		ReplayFrames.Clear();
		var replayCollectedCoins = new HashSet<int>();
		_prevDualActiveForPath = false;
		_speculativeDepth = 0;
		_frameCounter = 0;
		TraceFrameOpen();
		bool replayCompleted = false;
		for (int i = 0; i < inputSequence.Count; i++)
		{
			_frameCounter = i;
			bool flag = inputSequence[i];
			sbyte direction = horizontalSequence != null && i < horizontalSequence.Count
				? horizontalSequence[i]
				: (ForcePlatformer ? (sbyte)1 : (sbyte)0);
			Inputs.Add(flag);
			HorizontalInputs.Add(direction);
			int gameMode = s.GameMode;
			_cubeJumpedThisStep = false;
			bool endLevel;
			bool flag2 = StepFrame(ref s, flag, direction, out endLevel);
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
			ReplayFrames.Add(CaptureReplayFrame(i, flag, direction, in s, flag2,
				endLevel, replayCollectedCoins));
			if (endLevel)
				replayCompleted = flag2;
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
		s.ReturnAllSpriteResources();
		return replayCompleted;
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
		SimState simState = default(SimState);
		simState.X_fixed = InitialXFixed(startX_px);
		simState.Y_fixed = (startY_px << 8) | SpawnYSubpx;
		simState.VelX_fixed = SpeedUiIndexToFixed(startSpeedUiIndex);
		simState.GlobalSpeed_fixed = simState.VelX_fixed;
		simState.VelY_fixed = 0;
		simState.GameMode = startGameMode;
		simState.GravFlipped = startGravFlipped;
		simState.Mini = startMini;
		simState.GravMul = ((!startGravFlipped) ? 1 : (-1));
		simState.GravityMod = 1.0;
		simState.WasZeroedByCollision = true;
		simState.OnGround = true;
		simState.ProcessedSprites = NewSpriteSet();
		simState.PendingOrbIndex = -1;
		simState.PendingOrbSpriteId = -1;
		simState.PendingOrbExtra1Index = -1;
		simState.PendingOrbExtra1SpriteId = -1;
		simState.PendingOrbExtra2Index = -1;
		simState.PendingOrbExtra2SpriteId = -1;
		simState.NinjaJumps = ((startGameMode == 8) ? 3 : 0);
		simState.CameraY_fixed = ComputeInitCameraY(startY_px);
		simState.TargetCameraY_fixed = ComputeInitCameraY(startY_px);
		InitializeHorizontalState(ref simState);
		SimState s = simState;
		ApplyPortalsUpTo(ref s, startX_px);
		InitNesSlots(ref s);
		ApplyNesIntroFreezePrestep(ref s);
		CheckSprObjects(ref s);
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
				for (int m = 0; m < array2.Length; m++)
				{
					int index = array[m];
					list8.Add(list4[index]);
					array2[m] = list5[index];
					array3[m] = list6[index];
				}
				for (int n = array2.Length; n < array.Length; n++)
				{
					list4[array[n]].ReturnAllSpriteResources();
				}
				list2.Add((array2, array3));
				list3 = list8;
				if (list3[0].ProcessedSprites.Contains(item3.Index))
				{
					int num15 = list3[0].X_fixed >> 8;
					if (num15 > num4)
					{
						num8 = 0;
						_log.WriteLine($"[COIN_SPLICE] Beam collected coin at bf={j} X={num15} Y={list3[0].Y_fixed >> 8}");
						break;
					}
				}
				if (j % 20 != 0)
				{
					continue;
				}
				int num16 = int.MaxValue;
				int num17 = int.MinValue;
				int num18 = 0;
				foreach (SimState item7 in list3)
				{
					int num19 = item7.Y_fixed >> 8;
					if (num19 < num16)
					{
						num16 = num19;
					}
					if (num19 > num17)
					{
						num17 = num19;
					}
					if (item7.ProcessedSprites.Contains(item3.Index))
					{
						num18++;
					}
				}
				_log.WriteLine($"[COIN_SPLICE] bf={j} beam={list3.Count} Y=[{num16}..{num17}] collected={num18} X~{list3[0].X_fixed >> 8}");
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
			int num20 = num8;
			for (int num21 = list2.Count - 1; num21 >= 0; num21--)
			{
				list9.Add(list2[num21].Item2[num20]);
				num20 = list2[num21].Item1[num20];
			}
			list9.Reverse();
			SimState startState = list3[num8];
			int num22 = num5 + list9.Count;
			_log.WriteLine($"[COIN_SPLICE] Trying tail with original inputs from f={num22} (total={originalInputs.Count})");
			bool flag = false;
			SimState s4 = startState.Clone();
			_speculativeDepth = 1;
			bool flag2 = false;
			for (int num23 = num22; num23 < originalInputs.Count; num23++)
			{
				_frameCounter = num23;
				bool endLevel3;
				bool flag3 = StepFrame(ref s4, originalInputs[num23], out endLevel3);
				if (endLevel3)
				{
					flag2 = true;
					break;
				}
				if (!flag3)
				{
					_log.WriteLine($"[COIN_SPLICE] Tail died at f={num23} (df={num23 - num22}) X={s4.X_fixed >> 8} Y={s4.Y_fixed >> 8} dt={s4.DeathType}");
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
				_log.WriteLine($"[COIN_SPLICE] Trying survival beam from f={num22}...");
				List<bool>? list10 = RunSurvivalBeam(startState, num22, originalInputs.Count + 3000);
				if (list10 != null)
				{
					_log.WriteLine($"[COIN_SPLICE] Survival beam succeeded! len={list10.Count}");
					List<bool> list11 = new List<bool>(num5 + list9.Count + list10.Count);
					for (int num24 = 0; num24 < num5; num24++)
					{
						list11.Add(originalInputs[num24]);
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
			for (int l = 0; l < num3; l++)
			{
				int index = array[l];
				list7.Add(list3[index]);
				array2[l] = list4[index];
				array3[l] = list5[index];
			}
			for (int m = num3; m < array.Length; m++)
			{
				list3[array[m]].ReturnAllSpriteResources();
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
		while (_backtrackCheckpoints.Count > 0 &&
			_backtrackAttempts < (flag ? 1500 : 500) &&
			_totalBacktrackAttempts < 5000 &&
			(_backtrackTimer == null ||
			 _backtrackTimer.Elapsed.TotalSeconds < (double)(flag ? 120 : 60)))
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
				for (int k = 0; k < 3; k++)
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
			for (int l = _nextCoinCheckIdx; l < allCoins.Count; l++)
			{
				SpriteEntry spriteEntry2 = allCoins[l];
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
				for (int m = 0; m < num22; m++)
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
						for (int num24 = 0; num24 < num23; num24++)
						{
							bool flag7 = flag5 || (num23 != 1 && num24 == 1);
							SimState s4 = item2.Clone();
							if (!StepFrame(ref s4, flag7, out endLevel2))
							{
								continue;
							}
							List<bool> list4 = new List<bool>(item3) { flag7 };
							int num25 = (s4.X_fixed >> 8) + 1;
							int hitboxW = GetHitboxW(s4.Mini);
							int hitboxH = GetHitboxH(s4.Mini);
							int hitboxOffsetY = GetHitboxOffsetY(s4.GameMode, s4.Mini, s4.GravFlipped);
							int num26 = (s4.Y_fixed >> 8) + hitboxOffsetY;
							if (num25 + hitboxW >= spriteEntry2.HitLeft && spriteEntry2.HitRight >= num25 && num26 + hitboxH >= spriteEntry2.HitTop && spriteEntry2.HitBottom >= num26)
							{
								bool flag8 = true;
								SimState s5 = s4.Clone();
								for (int num27 = 0; num27 < 30; num27++)
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
							int num28 = s4.X_fixed >> 8;
							if (num28 <= spriteEntry2.HitRight + 16)
							{
								int num29 = Math.Abs((s4.Y_fixed >> 8) - num20);
								int num30 = Math.Max(0, spriteEntry2.HitLeft - num28);
								int item4 = num29 + num30 / 3;
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
					int num31 = Math.Min(512, list3.Count);
					for (int num32 = 0; num32 < num31; num32++)
					{
						list.Add((list3[num32].Item1, list3[num32].Item2));
					}
				}
				_speculativeDepth--;
				if (!flag4)
				{
					break;
				}
				_coinInputScript.Clear();
				_coinInputScriptCoinIdx = spriteEntry2.Index;
				for (int num33 = 1; num33 < list2.Count; num33++)
				{
					_coinInputScript.Enqueue(list2[num33]);
				}
				return list2[0];
			}
		}
		if (PreferCoins && allCoins.Count > 0 && state.OnGround && state.VelY_fixed == 0 && state.GameMode == 0 && _speculativeDepth == 0)
		{
			int num34 = state.X_fixed >> 8;
			for (int num35 = _nextCoinCheckIdx; num35 < allCoins.Count; num35++)
			{
				SpriteEntry spriteEntry3 = allCoins[num35];
				if (state.ProcessedSprites.Contains(spriteEntry3.Index) || _forgivenCoins.Contains(spriteEntry3.Index))
				{
					continue;
				}
				if (spriteEntry3.HitLeft > num34 + 500)
				{
					break;
				}
				if (spriteEntry3.HitRight < num34)
				{
					continue;
				}
				_speculativeDepth++;
				SimState s6 = state.Clone();
				bool flag9 = false;
				int num36 = 0;
				int num37 = -1;
				SimState s7 = state.Clone();
				bool flag10 = false;
				int num38 = 0;
				bool result = false;
				List<bool> list5 = new List<bool>();
				int num39 = (mapHeight - groundRowsToReserve) * 16 - 15;
				for (int num40 = 0; num40 < 200; num40++)
				{
					bool flag11;
					if (s7.PendingOrbIndex >= 0)
					{
						flag11 = true;
					}
					else if (s7.OnGround && s7.VelY_fixed == 0 && s7.Y_fixed >> 8 >= num39 - 5)
					{
						SimState s8 = s7.Clone();
						flag11 = !StepFrame(ref s8, input: false, out endLevel2);
					}
					else
					{
						flag11 = false;
					}
					list5.Add(flag11);
					if (num40 == 0)
					{
						result = flag11;
					}
					if (!StepFrame(ref s7, flag11, out var endLevel3))
					{
						break;
					}
					num38 = num40 + 1;
					if (endLevel3)
					{
						num38 = 120;
						break;
					}
					if (!flag10)
					{
						int num41 = (s7.X_fixed >> 8) + 1;
						int hitboxW2 = GetHitboxW(s7.Mini);
						int hitboxH2 = GetHitboxH(s7.Mini);
						int hitboxOffsetY2 = GetHitboxOffsetY(s7.GameMode, s7.Mini, s7.GravFlipped);
						int num42 = (s7.Y_fixed >> 8) + hitboxOffsetY2;
						int num43 = num42 + hitboxH2;
						bool num44 = num41 + hitboxW2 >= spriteEntry3.HitLeft && spriteEntry3.HitRight >= num41;
						bool flag12 = num43 >= spriteEntry3.HitTop && spriteEntry3.HitBottom >= num42;
						if (num44 && flag12)
						{
							flag10 = true;
						}
					}
				}
				if (flag10 && num38 >= 30)
				{
					_speculativeDepth--;
					_coinInputScript.Clear();
					_coinInputScriptCoinIdx = spriteEntry3.Index;
					for (int num45 = 1; num45 < list5.Count; num45++)
					{
						_coinInputScript.Enqueue(list5[num45]);
					}
					return result;
				}
				List<bool>? list6 = null;
				bool flag13 = false;
				List<bool> list7 = new List<bool>();
				bool flag14 = false;
				for (int num46 = 0; num46 < 120; num46++)
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
					num36 = num46 + 1;
					if (endLevel4)
					{
						num36 = 120;
						break;
					}
					if (!flag9)
					{
						int num47 = (s6.X_fixed >> 8) + 1;
						int hitboxW3 = GetHitboxW(s6.Mini);
						int hitboxH3 = GetHitboxH(s6.Mini);
						int hitboxOffsetY3 = GetHitboxOffsetY(s6.GameMode, s6.Mini, s6.GravFlipped);
						int num48 = (s6.Y_fixed >> 8) + hitboxOffsetY3;
						int num49 = num48 + hitboxH3;
						bool num50 = num47 + hitboxW3 >= spriteEntry3.HitLeft && spriteEntry3.HitRight >= num47;
						bool flag16 = num49 >= spriteEntry3.HitTop && spriteEntry3.HitBottom >= num48;
						if (num50 && flag16)
						{
							flag9 = true;
							num37 = num46;
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
					num36 = 0;
					bool flag17 = false;
					List<bool> list8 = new List<bool>();
					bool flag18 = false;
					for (int num51 = 0; num51 < 120; num51++)
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
						num36 = num51 + 1;
						if (endLevel5)
						{
							num36 = 120;
							break;
						}
						if (!flag9)
						{
							int num52 = (s6.X_fixed >> 8) + 1;
							int hitboxW4 = GetHitboxW(s6.Mini);
							int hitboxH4 = GetHitboxH(s6.Mini);
							int hitboxOffsetY4 = GetHitboxOffsetY(s6.GameMode, s6.Mini, s6.GravFlipped);
							int num53 = (s6.Y_fixed >> 8) + hitboxOffsetY4;
							int num54 = num53 + hitboxH4;
							bool num55 = num52 + hitboxW4 >= spriteEntry3.HitLeft && spriteEntry3.HitRight >= num52;
							bool flag20 = num54 >= spriteEntry3.HitTop && spriteEntry3.HitBottom >= num53;
							if (num55 && flag20)
							{
								flag9 = true;
								num37 = num51;
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
				if (!flag9 || num36 < num37 + 10)
				{
					break;
				}
				if (list6 != null && list6.Count > 1 && !flag13)
				{
					_coinInputScript.Clear();
					_coinInputScriptCoinIdx = spriteEntry3.Index;
					for (int num56 = 1; num56 < list6.Count; num56++)
					{
						_coinInputScript.Enqueue(list6[num56]);
					}
				}
				return true;
			}
		}
		if (PreferCoins && allCoins.Count > 0 && state.OnGround && state.VelY_fixed == 0 && state.GameMode == 0 && _speculativeDepth == 0)
		{
			int num57 = state.X_fixed >> 8;
			int num58 = state.Y_fixed >> 8;
			for (int num59 = _nextCoinCheckIdx; num59 < allCoins.Count; num59++)
			{
				SpriteEntry spriteEntry4 = allCoins[num59];
				if (state.ProcessedSprites.Contains(spriteEntry4.Index) || _forgivenCoins.Contains(spriteEntry4.Index))
				{
					continue;
				}
				if (spriteEntry4.HitLeft > num57 + 300)
				{
					break;
				}
				if (spriteEntry4.HitRight < num57)
				{
					continue;
				}
				if ((spriteEntry4.HitTop + spriteEntry4.HitBottom) / 2 <= num58)
				{
					break;
				}
				_speculativeDepth++;
				SimState s9 = state.Clone();
				bool flag21 = false;
				int num60 = 0;
				int num61 = num58;
				int num62 = num58;
				for (int num63 = 0; num63 < 120; num63++)
				{
					bool input2 = s9.PendingOrbIndex >= 0;
					if (!StepFrame(ref s9, input2, out var endLevel6))
					{
						break;
					}
					num60 = num63 + 1;
					int num64 = s9.Y_fixed >> 8;
					_ = s9.X_fixed >> 8;
					if (num64 < num61)
					{
						num61 = num64;
					}
					if (num64 > num62)
					{
						num62 = num64;
					}
					if (endLevel6)
					{
						num60 = 120;
						break;
					}
					if (!flag21)
					{
						int num65 = (s9.X_fixed >> 8) + 1;
						int hitboxW5 = GetHitboxW(s9.Mini);
						int hitboxH5 = GetHitboxH(s9.Mini);
						int hitboxOffsetY5 = GetHitboxOffsetY(s9.GameMode, s9.Mini, s9.GravFlipped);
						int num66 = (s9.Y_fixed >> 8) + hitboxOffsetY5;
						int num67 = num66 + hitboxH5;
						bool num68 = num65 + hitboxW5 >= spriteEntry4.HitLeft && spriteEntry4.HitRight >= num65;
						bool flag22 = num67 >= spriteEntry4.HitTop && spriteEntry4.HitBottom >= num66;
						if (num68 && flag22)
						{
							flag21 = true;
						}
					}
				}
				_speculativeDepth--;
				if (!flag21 || num60 < 30)
				{
					break;
				}
				return false;
			}
		}
		int num69 = state.Y_fixed >> 8;
		int num70 = (mapHeight - groundRowsToReserve) * 16 - 15;
		bool flag23 = num69 <= num70 - 16;
		int num71 = (flag23 ? 600 : 120);
		int num72 = (flag23 ? 128 : 256);
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
		for (int num73 = 0; num73 < num71; num73++)
		{
			if (list9.Count <= 0)
			{
				break;
			}
			List<(SimState, bool, int, int, int, bool)> list10 = new List<(SimState, bool, int, int, int, bool)>();
			foreach (var item7 in list9)
			{
				(SimState, bool, int, int, int, bool) current2 = item7;
				int num74 = -1;
				if (current2.Item1.PendingOrbIndex >= 0)
				{
					num74 = current2.Item1.PendingOrbIndex;
				}
				else
				{
					ScanForOrbOverlap(in current2.Item1, out var orbSpriteIndex2);
					if (orbSpriteIndex2 >= 0 && !current2.Item1.ProcessedSprites.Contains(orbSpriteIndex2))
					{
						num74 = orbSpriteIndex2;
					}
				}
				int num75 = ((num74 < 0) ? 1 : 2);
				for (int num76 = 0; num76 < num75; num76++)
				{
					SimState s10;
					if (num76 == 0)
					{
						(s10, _, _, _, _, _) = current2;
					}
					else
					{
						SimState item5 = current2.Item1;
						s10 = item5.Clone();
						s10.ProcessedSprites.Add(num74);
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
					bool flag24 = current2.Item6 || num76 == 1;
					if (s10.VelY_fixed == 0 && s10.OnGround && (s10.GameMode == 0 || s10.GameMode == 2))
					{
						int num77 = current2.Item4 + 1;
						SimState s11 = s10.Clone();
						bool endLevel7;
						bool flag25 = StepFrame(ref s11, input: true, out endLevel7);
						bool flag26 = num73 == 0 || current2.Item2;
						int num78 = current2.Item3 + 1;
						int num79 = s11.Y_fixed >> 8;
						if (endLevel7)
						{
							RecordTerminal(flag26, num71, s11.X_fixed >> 8, num78, num77, num79, flag24);
						}
						else if (flag25)
						{
							list10.Add((s11, flag26, num78, num77, num79, flag24));
						}
						else
						{
							RecordTerminal(flag26, num73, s11.X_fixed >> 8, num78, num77, num79, flag24);
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
						bool flag28 = num73 != 0 && current2.Item2;
						int num80 = s12.Y_fixed >> 8;
						if (endLevel8)
						{
							RecordTerminal(flag28, num71, s12.X_fixed >> 8, current2.Item3, num77, num80, flag24);
						}
						else if (flag27)
						{
							list10.Add((s12, flag28, current2.Item3, num77, num80, flag24));
						}
						else
						{
							RecordTerminal(flag28, num73, s12.X_fixed >> 8, current2.Item3, num77, num80, flag24);
						}
						continue;
					}
					if (s10.GameMode == 0 && CubeWillLandThisFrame(s10))
					{
						int num81 = current2.Item4 + 1;
						for (int num82 = 0; num82 < 2; num82++)
						{
							bool flag29 = num82 == 0;
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
							bool flag31 = ((num73 == 0) ? flag29 : current2.Item2);
							int num83 = ((num82 == 0) ? (current2.Item3 + 1) : current2.Item3);
							int num84 = s13.Y_fixed >> 8;
							if (endLevel9)
							{
								RecordTerminal(flag31, num71, s13.X_fixed >> 8, num83, num81, num84, flag24);
							}
							else if (flag30)
							{
								list10.Add((s13, flag31, num83, num81, num84, flag24));
							}
							else
							{
								RecordTerminal(flag31, num73, s13.X_fixed >> 8, num83, num81, num84, flag24);
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
					int num85 = s14.Y_fixed >> 8;
					if (endLevel10)
					{
						RecordTerminal(current2.Item2, num71, s14.X_fixed >> 8, current2.Item3, current2.Item4, num85, flag24);
					}
					else if (flag32)
					{
						list10.Add((s14, current2.Item2, current2.Item3, current2.Item4, num85, flag24));
					}
					else
					{
						RecordTerminal(current2.Item2, num73, s14.X_fixed >> 8, current2.Item3, current2.Item4, num85, flag24);
					}
				}
			}
			list9 = list10;
			if (list9.Count <= num72)
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
			if (list9.Count <= num72)
			{
				continue;
			}
			List<(SimState, bool, int, int, int, bool)> list11 = (from n in list9
				where n.Item2
				orderby n.Item1.Y_fixed
				select n).ToList();
			List<(SimState, bool, int, int, int, bool)> list12 = (from n in list9
				where !n.Item2
				orderby n.Item1.Y_fixed
				select n).ToList();
			int num86 = num72 / 2;
			if (list11.Count > num86)
			{
				List<(SimState, bool, int, int, int, bool)> list13 = new List<(SimState, bool, int, int, int, bool)>(num86);
				for (int num87 = 0; num87 < num86; num87++)
				{
					list13.Add(list11[(int)((long)num87 * (long)list11.Count / num86)]);
				}
				list11 = list13;
			}
			if (list12.Count > num86)
			{
				List<(SimState, bool, int, int, int, bool)> list14 = new List<(SimState, bool, int, int, int, bool)>(num86);
				for (int num88 = 0; num88 < num86; num88++)
				{
					list14.Add(list12[(int)((long)num88 * (long)list12.Count / num86)]);
				}
				list12 = list14;
			}
			list9 = list11.Concat(list12).ToList();
		}
		_speculativeDepth--;
		foreach (var item9 in list9)
		{
			RecordTerminal(item9.Item2, num71, item9.Item1.X_fixed >> 8, item9.Item3, item9.Item4, item9.Item5, item9.Item6);
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
		int num89 = 0;
		if (bestF0JumpSurv >= num71 && bestF0WalkSurv >= 0 && bestF0JumpMinY != int.MaxValue && bestF0WalkMinY != int.MaxValue)
		{
			num89 = (bestF0WalkMinY - bestF0JumpMinY) / 16 * 8;
			if (num89 < 0)
			{
				num89 = 0;
			}
		}
		if (PreferCoins && num89 > 0)
		{
			int num90 = state.X_fixed >> 8;
			int num91 = state.Y_fixed >> 8;
			for (int num92 = _nextCoinCheckIdx; num92 < allCoins.Count; num92++)
			{
				SpriteEntry spriteEntry5 = allCoins[num92];
				if (state.ProcessedSprites.Contains(spriteEntry5.Index) || _forgivenCoins.Contains(spriteEntry5.Index))
				{
					continue;
				}
				if (spriteEntry5.HitLeft > num90 + 1500)
				{
					break;
				}
				if (spriteEntry5.HitRight >= num90)
				{
					if ((spriteEntry5.HitTop + spriteEntry5.HitBottom) / 2 > num91)
					{
						num89 = 0;
					}
					break;
				}
			}
		}
		int num93 = 0;
		int num94 = 0;
		int num95 = -1;
		if (PreferCoins && allCoins.Count > 0 && bestF0JumpMinY != int.MaxValue && bestF0WalkMinY != int.MaxValue && bestF0JumpSurv > 0 && bestF0WalkSurv > 0)
		{
			int num96 = state.X_fixed >> 8;
			for (int num97 = _nextCoinCheckIdx; num97 < allCoins.Count; num97++)
			{
				SpriteEntry spriteEntry6 = allCoins[num97];
				if (state.ProcessedSprites.Contains(spriteEntry6.Index) || _forgivenCoins.Contains(spriteEntry6.Index))
				{
					continue;
				}
				if (spriteEntry6.HitLeft > num96 + 1500)
				{
					break;
				}
				if (spriteEntry6.HitRight >= num96)
				{
					num95 = (spriteEntry6.HitTop + spriteEntry6.HitBottom) / 2;
					int num98 = Math.Max(0, spriteEntry6.HitLeft - num96);
					double num99 = Math.Max(0.1, 1.0 - (double)num98 / 1500.0);
					int num100;
					int num101;
					if (num95 < state.Y_fixed >> 8)
					{
						num100 = ((bestF0JumpMinY > num95) ? (bestF0JumpMinY - num95) : 0);
						num101 = ((bestF0WalkMinY > num95) ? (bestF0WalkMinY - num95) : 0);
					}
					else
					{
						num100 = Math.Abs(bestF0JumpMinY - num95);
						num101 = Math.Abs(bestF0WalkMinY - num95);
					}
					int num102 = num101 - num100;
					int num103 = (int)((double)Math.Abs(num102) * num99);
					int num104 = ((num102 > 0) ? bestF0JumpSurv : bestF0WalkSurv);
					if (num104 < 30)
					{
						num103 = num103 * num104 / 30;
					}
					num103 = Math.Min(num103, num71);
					if (num102 > 0)
					{
						num93 = num103;
					}
					else if (num102 < 0)
					{
						num94 = num103;
					}
					break;
				}
			}
		}
		bool flag33 = bestF0WalkMinY != int.MaxValue && bestF0JumpMinY != int.MaxValue && bestF0WalkMinY > bestF0JumpMinY;
		int num105 = ((bestF0JumpHoldPattern && bestF0JumpSurv >= num71 && !flag33) ? 5 : 0);
		int num106 = 0;
		if (_coinAltitudePenalties.Count > 0)
		{
			int num107 = state.X_fixed >> 8;
			int num108 = state.Y_fixed >> 8;
			foreach (var (num109, num110, num111) in _coinAltitudePenalties)
			{
				if (num107 >= num109 && num107 <= num110)
				{
					num106 = 30;
					if (num108 < num111)
					{
						num106 = Math.Max(num106, num111 - num108);
					}
					break;
				}
			}
		}
		if (num106 > 0 && num89 > 0)
		{
			num89 = 0;
		}
		if (num106 > 0 && num93 > 0)
		{
			num93 = 0;
		}
		int num112 = bestF0JumpSurv + num105 + num89 + num93 - num106;
		int num113 = bestF0WalkSurv + num94;
		if (JumpTimingBias < 0.45)
		{
			int num114 = (int)((0.5 - JumpTimingBias) * 6.0 + 0.5);
			num112 += num114;
		}
		else if (JumpTimingBias > 0.55)
		{
			int num115 = (int)((JumpTimingBias - 0.5) * 6.0 + 0.5);
			num113 += num115;
		}
		if (num112 <= num113 && (num113 > num112 || (flag23 && bestF0JumpSurv >= num71 && bestF0WalkSurv >= num71 && bestF0JumpMinY > bestF0WalkMinY) || (flag23 && bestF0JumpSurv >= num71 && bestF0WalkSurv >= num71) || (bestF0JumpSurv >= num71 && bestF0WalkSurv >= num71 && bestF0WalkMinY > bestF0JumpMinY) || (num95 >= 0 && bestF0JumpX >= bestF0WalkX) || bestF0JumpX < bestF0WalkX || 1 == 0))
		{
			return false;
		}
		return true;
		void RecordTerminal(bool j0, int survFrames, int xPx, int lj, int tl, int minY, bool orbSkipped = false)
		{
			if (!orbSkipped)
			{
				if (j0)
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
				for (int n = 0; n < num34; n++)
				{
					list3.Add((list7[n].Item1, list7[n].Item2, list7[n].Item3));
				}
				int num35 = num17 - list3.Count;
				if (num35 <= 0 || list8.Count <= 0)
				{
					continue;
				}
				list8.Sort(((SimState st, List<bool> inputs, int collected, int score) a, (SimState st, List<bool> inputs, int collected, int score) b) => a.score.CompareTo(b.score));
				int num36 = Math.Min(Math.Min(val2, num35), list8.Count);
				HashSet<int> hashSet = new HashSet<int>();
				for (int num37 = 0; num37 < num36; num37++)
				{
					list3.Add((list8[num37].Item1, list8[num37].Item2, list8[num37].Item3));
					hashSet.Add(num37);
				}
				num35 = num17 - list3.Count;
				if (num35 <= 0)
				{
					continue;
				}
				SimState ss2 = list8[0].Item1;
				int num38 = CorridorCenter(ref ss2);
				List<(int, int)> list9 = new List<(int, int)>();
				for (int num39 = 0; num39 < list8.Count; num39++)
				{
					if (!hashSet.Contains(num39))
					{
						(SimState, List<bool>, int, int) tuple = list8[num39];
						int num40 = Math.Abs((tuple.Item1.Y_fixed >> 8) - num38);
						int num41 = Math.Abs(tuple.Item1.VelY_fixed) >> 6;
						list9.Add((num39, num40 * 2 + num41));
					}
				}
				list9.Sort(((int origIdx, int safeScore) a, (int origIdx, int safeScore) b) => a.safeScore.CompareTo(b.safeScore));
				int num42 = Math.Min(num35, list9.Count);
				for (int num43 = 0; num43 < num42; num43++)
				{
					(SimState, List<bool>, int, int) tuple2 = list8[list9[num43].Item1];
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
				for (int num44 = 1; num44 < list4.Count; num44++)
				{
					_coinInputScript.Enqueue(list4[num44]);
				}
				return list4[0];
			}
			if (list3.Count == 0 && num19 == 0)
			{
				_speculativeDepth++;
				int val3 = CorridorCenter(ref state);
				int num45 = Math.Min(600, num3 + 60 + 120);
				int num46 = Math.Min(val3, num);
				int num47 = Math.Max(val3, num);
				list3.Clear();
				list3.Add((state.Clone(), new List<bool>(), -1));
				flag5 = false;
				list4.Clear();
				num19 = 0;
				for (int num48 = 0; num48 < num45; num48++)
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
						for (int num49 = 0; num49 <= 1; num49++)
						{
							bool flag9 = num49 == 1;
							SimState s6 = item5.Clone();
							if (!StepFrame(ref s6, flag9, out endLevel5))
							{
								continue;
							}
							List<bool> list11 = new List<bool>(item6) { flag9 };
							int num50 = item7;
							if (num50 < 0)
							{
								int num51 = (s6.X_fixed >> 8) + 1;
								int hitboxW2 = GetHitboxW(s6.Mini);
								int hitboxH2 = GetHitboxH(s6.Mini);
								int hitboxOffsetY2 = GetHitboxOffsetY(s6.GameMode, s6.Mini, s6.GravFlipped);
								int num52 = (s6.Y_fixed >> 8) + hitboxOffsetY2;
								if (num51 + hitboxW2 >= spriteEntry.HitLeft && spriteEntry.HitRight >= num51 && num52 + hitboxH2 >= spriteEntry.HitTop && spriteEntry.HitBottom >= num52)
								{
									num50 = 0;
								}
							}
							int item8;
							if (num50 >= 0)
							{
								num50++;
								if (num50 >= 120)
								{
									flag5 = true;
									list4 = list11;
									num19 = num50;
									break;
								}
								if (num50 > num19)
								{
									num19 = num50;
									list4 = new List<bool>(list11);
								}
								int num53 = s6.Y_fixed >> 8;
								int num54 = CorridorCenter(ref s6);
								int num55 = Math.Abs(num53 - num54);
								int num56 = Math.Abs(s6.VelY_fixed) >> 6;
								item8 = -10000 + num55 * 3 + num56;
							}
							else
							{
								if (s6.X_fixed >> 8 > spriteEntry.HitRight + 32)
								{
									continue;
								}
								int num57 = s6.Y_fixed >> 8;
								int num58 = ((num57 < num46) ? (num46 - num57) : ((num57 > num47) ? (num57 - num47) : 0));
								int num59 = Math.Abs(s6.VelY_fixed) >> 7;
								item8 = num58 * 3 + num59;
							}
							list10.Add((s6, list11, num50, item8));
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
					int num60 = Math.Min(1024, list10.Count);
					for (int num61 = 0; num61 < num60; num61++)
					{
						list3.Add((list10[num61].Item1, list10[num61].Item2, list10[num61].Item3));
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
					for (int num62 = 1; num62 < list4.Count; num62++)
					{
						_coinInputScript.Enqueue(list4[num62]);
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
				int num63 = CorridorCenter(ref state);
				flag10 = Math.Abs(num - num63) > 40;
			}
			int num64 = (flag10 ? 5 : 10);
			if (!(num >= 0 && flag) || Math.Min(num5, num6) < num64 || !(num3 <= 600 || flag10))
			{
				return num5 > num6;
			}
			_speculativeDepth++;
			bool flag11 = false;
			SimState s7 = state.Clone();
			StepFrame(ref s7, input: true, out endLevel5);
			for (int num65 = 1; num65 < 120; num65++)
			{
				int num66 = s7.Y_fixed >> 8;
				int velY_fixed2 = s7.VelY_fixed;
				int num67 = ((s7.GravMul > 0) ? (num66 - num) : (num - num66));
				int num68 = -(velY_fixed2 * s7.GravMul);
				bool input = num67 - num68 > 0;
				if (!StepFrame(ref s7, input, out endLevel5))
				{
					break;
				}
				int num69 = (s7.X_fixed >> 8) + 1;
				int hitboxW3 = GetHitboxW(s7.Mini);
				int hitboxH3 = GetHitboxH(s7.Mini);
				int hitboxOffsetY3 = GetHitboxOffsetY(s7.GameMode, s7.Mini, s7.GravFlipped);
				int num70 = (s7.Y_fixed >> 8) + hitboxOffsetY3;
				if (num69 + hitboxW3 >= spriteEntry.HitLeft && spriteEntry.HitRight >= num69 && num70 + hitboxH3 >= spriteEntry.HitTop && spriteEntry.HitBottom >= num70)
				{
					flag11 = true;
					break;
				}
			}
			bool flag12 = false;
			SimState s8 = state.Clone();
			StepFrame(ref s8, input: false, out endLevel5);
			for (int num71 = 1; num71 < 120; num71++)
			{
				int num72 = s8.Y_fixed >> 8;
				int velY_fixed3 = s8.VelY_fixed;
				int num73 = ((s8.GravMul > 0) ? (num72 - num) : (num - num72));
				int num74 = -(velY_fixed3 * s8.GravMul);
				bool input2 = num73 - num74 > 0;
				if (!StepFrame(ref s8, input2, out endLevel5))
				{
					break;
				}
				int num75 = (s8.X_fixed >> 8) + 1;
				int hitboxW4 = GetHitboxW(s8.Mini);
				int hitboxH4 = GetHitboxH(s8.Mini);
				int hitboxOffsetY4 = GetHitboxOffsetY(s8.GameMode, s8.Mini, s8.GravFlipped);
				int num76 = (s8.Y_fixed >> 8) + hitboxOffsetY4;
				if (num75 + hitboxW4 >= spriteEntry.HitLeft && spriteEntry.HitRight >= num75 && num76 + hitboxH4 >= spriteEntry.HitTop && spriteEntry.HitBottom >= num76)
				{
					flag12 = true;
					break;
				}
			}
			_speculativeDepth--;
			int num77 = (flag10 ? 1 : 10);
			if (flag11 && !flag12 && num5 >= num77)
			{
				return true;
			}
			if (flag12 && !flag11 && num6 >= num77)
			{
				return false;
			}
			int num78 = _shipCoinAggressiveThreshold;
			if (flag10)
			{
				num78 = ((num3 > 400) ? Math.Max(num78, 4) : ((num3 <= 200) ? Math.Max(num78, 20) : Math.Max(num78, 10)));
			}
			else if (num3 <= 600)
			{
				num78 = ((num3 > 300) ? Math.Max(num78, 4) : ((num3 > 200) ? Math.Max(num78, 6) : ((num3 <= 100) ? Math.Max(num78, 20) : Math.Max(num78, 10))));
			}
			if (Math.Abs(num5 - num6) > num78)
			{
				return num5 > num6;
			}
		}
		int num79 = (int)((JumpTimingBias - 0.5) * 16.0);
		foreach (KeyValuePair<int, int> item12 in _coinCollectThenLoseCount)
		{
			if (!_forgivenCoins.Contains(item12.Key))
			{
				num79 -= 3 * item12.Value;
			}
		}
		int num89;
		if (num >= 0)
		{
			int num80 = CorridorCenter(ref state) + _shipCorridorBias + num79;
			if (Math.Abs(num - num80) > 40 && num3 <= 2000)
			{
				int num81 = CorridorCenter(ref state, 2, num) + _shipCorridorBias + num79;
				int num82 = CorridorCenter(ref state, 0) + _shipCorridorBias + num79;
				int num83 = Math.Abs(num - num81);
				int num84 = Math.Abs(num - num82);
				bool flag13 = num83 < num84 - 20;
				int num85 = (flag13 ? num81 : num82);
				int num86 = Math.Abs(num - num85);
				int num87 = ((num86 > 40 && num86 <= 120 && num3 <= 2000) ? Math.Min(2000, Math.Max(400, num86 * 16)) : 400);
				if (num3 > num87)
				{
					int num88 = 80;
					num89 = num85 + (num - num85) * num88 / 100;
				}
				else if (num3 > 400)
				{
					int num90 = num87 - 400;
					int num91 = ((!flag13) ? ((num90 > 0) ? (80 + (num87 - num3) * 10 / num90) : 90) : ((num90 > 0) ? (70 + (num87 - num3) * 25 / num90) : 95));
					num89 = num85 + (num - num85) * num91 / 100;
				}
				else if (num3 > 300)
				{
					int num92 = (flag13 ? 90 : 85);
					num89 = num85 + (num - num85) * num92 / 100;
				}
				else if (num3 > 200)
				{
					int num93 = (flag13 ? 95 : 90);
					num89 = num85 + (num - num85) * num93 / 100;
				}
				else if (num3 > 120)
				{
					int num94 = (flag13 ? 100 : 95);
					num89 = num85 + (num - num85) * num94 / 100;
				}
				else
				{
					num89 = num;
				}
			}
			else
			{
				num89 = num;
			}
		}
		else
		{
			num89 = CorridorCenter(ref state) + _shipCorridorBias + num79;
		}
		int num95 = state.Y_fixed >> 8;
		int velY_fixed4 = state.VelY_fixed;
		int num96 = ((state.GravMul > 0) ? (num95 - num89) : (num89 - num95));
		int num97 = -(velY_fixed4 * state.GravMul);
		int num98 = ((num >= 0) ? 1 : 2);
		return num96 - num97 * num98 > 0;
		int CorridorCenter(ref SimState ss, int lookAhead = 15, int overrideY = -1)
		{
			int num99 = ss.X_fixed >> 8;
			int num100 = ((overrideY >= 0) ? overrideY : (ss.Y_fixed >> 8));
			int num101 = (ss.Mini ? 1 : 0);
			int num102 = ((overrideY >= 0) ? 1 : 0);
			long key = (long)((uint)(num99 & 0xFFFF) | ((ulong)(uint)(num100 & 0xFFFF) << 16) | ((ulong)(uint)(lookAhead & 0xFF) << 32) | ((ulong)(uint)num101 << 40) | ((ulong)(uint)num102 << 41));
			if (shipCorridorCache.TryGetValue(key, out var value))
			{
				return value;
			}
			int num103 = ((num102 != 0) ? FindCorridorCenter(ref ss, lookAhead, overrideY) : ((lookAhead == 15) ? FindCorridorCenter(ref ss) : FindCorridorCenter(ref ss, lookAhead)));
			shipCorridorCache[key] = num103;
			return num103;
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
				if (tileCollision != 0)
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
					for (int l = 0; l < maxAlive; l++)
					{
						list3.Add(list[(int)((long)l * (long)list.Count / maxAlive)]);
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
		return StepFrame(ref s, input, ForcePlatformer ? (sbyte)1 : (sbyte)0,
			out endLevel);
	}

	private bool StepFrame(ref SimState s, bool input, sbyte horizontalDirection,
		out bool endLevel)
	{
		_stepFrameInputHeld = input;
		endLevel = false;
		s.RainbowPortalModeCount = 0;
		// A PF frame is one replay/sim_cursor tick, not one rendered NES frame.
		// Slow mode inserts rendered frames on which X does not move; the Mesen
		// replay clock deliberately holds the same input and cursor across them.
		// Therefore those waits must not consume a PF input or publish a PF row.
		SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=frame.in X={s.X_fixed >> 8}.{(s.X_fixed & 0xFF):X2} Y={s.Y_fixed >> 8}.{(s.Y_fixed & 0xFF):X2} Ypx={NesPlayerY_px(s.Y_fixed, s.CameraY_fixed)} Vx={s.VelX_fixed} Vy={s.VelY_fixed} sFr={s.SlopeFrames} swOn={s.SlopeWasOnCounter} sT={s.SlopeType} lst={s.LastSlopeType} inp={(input ? 1 : 0)} grav={(s.GravFlipped ? 1 : 0)} mini={(s.Mini ? 1 : 0)} gm={s.GameMode} onG={(s.OnGround ? 1 : 0)} dash={s.Dashing} inv={s.InvincibleCounter} camY={s.CameraY_fixed >> 8}.{(s.CameraY_fixed & 0xFF):X2}");
		SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=StepFrame.in input={(input ? 1 : 0)} horizontal={horizontalDirection} prevHeld={(s.PrevInputHeld ? 1 : 0)} dash={s.Dashing} orbed={(s.Orbed ? 1 : 0)} dual={(s.DualActive ? 1 : 0)}");
		s.Step2Ejected = false;
		s.ShipDbgCeilSlopeHit = false;
		s.ShipDbgCeilTileHit = false;
		s.ShipDbgCeilSpike = false;
		s.ShipDbgFloorSlopeHit = false;
		s.ShipDbgFloorTileHit = false;
		s.ShipDbgFloorSpike = false;
		if (!_dualP2Guard)
		{
			s.ReplayBackgroundColorTriggerIndex = -1;
			s.ReplayBackgroundColorTriggerSpriteId = -1;
			s.ReplayObjectColorTriggerIndex = -1;
			s.ReplayObjectColorTriggerSpriteId = -1;
			s.ReplayGroundColorTriggerIndex = -1;
			s.ReplayGroundColorTriggerSpriteId = -1;
		}
		ClearPendingOrbs(ref s);
		int x_fixed = s.X_fixed;
		int num = x_fixed >> 8;
		int gameModeAtFrameStart = s.GameMode;
		bool flag = input && !s.PrevInputHeld;
		bool queuedPressAtFrameStart = s.AirPressLatch;
		// NES state_game runs decrement_was_on_slope before cube-data latching
		// and sprite_collide. The old mode/gravity must therefore own any
		// slope-exit impulse when a portal changes mode later in this frame.
		PfUpdateSlopeCountersPreGravity(ref s);
		if (!input)
		{
			s.AirPressLatch = false;
			// x_movement stores cube_data &= 1 on every released-input frame.
			// RobotJumpRequested represents cube_data bit $04, so it must not
			// survive a release or leak into a later robot segment (Future Funk).
			s.RobotJumpRequested = false;
		}
		else if (flag && s.GameMode != 1 && s.GameMode != 3 &&
			(s.VelY_fixed != 0 || s.GameMode == 4))
		{
			s.AirPressLatch = true;
		}
		int velX_fixed = s.VelX_fixed;
		int value = NesPlayerY_px(s.Y_fixed, s.CameraY_fixed);
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
		bool skullDeathThisFrame = false;
		SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=ProcessSprites.in X={num} Y={NesPlayerY_px(s.Y_fixed, s.CameraY_fixed)} mode={s.GameMode}");
		if (!_dualP2Guard)
		{
			if (_p1OrbIndicesThisFrame == null)
				_p1OrbIndicesThisFrame = new List<int>();
			_p1OrbIndicesThisFrame.Clear();
		}
		int _dbgVelXBefore = s.VelX_fixed;
		int _dbgYBeforePS = s.Y_fixed;
		endLevel = ProcessSpritesNesOrder(ref s, num, input, flag, queuedPressAtFrameStart,
			out orbHitThisFrame, out skullDeathThisFrame);
		// NES tests orbed[] after sprite_collide and after cube_eject. Preserve the
		// sprite-pass latch explicitly: collision/ejection bookkeeping must never
		// make a held input auto-jump after a spider orb/pad teleport.
		bool orbedAtMovementStart = s.Orbed;
		if (s.Y_fixed != _dbgYBeforePS)
		{
			SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=ProcessSprites.Ychanged oldY=0x{_dbgYBeforePS:X} newY=0x{s.Y_fixed:X} delta={s.Y_fixed - _dbgYBeforePS}");
		}
		if (s.VelX_fixed != _dbgVelXBefore)
		{
			SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=ProcessSprites.velXChanged oldVelX={_dbgVelXBefore} newVelX={s.VelX_fixed}");
		}
		// NES execution order: sprite_collide → movement (Y) → x_movement_coll → x_movement.
		// Y movement (wave, slope counters) always uses the OLD currplayer_vel_x because
		// x_movement (which reloads it from the speed table) runs AFTER Y movement.
		// velX_fixed was captured at frame start (line 8252) and must NOT be recaptured.
		if (endLevel)
		{
			return true;
		}
		// x_movement reloads currplayer_vel_x from the global speed selector
		// after gamemode Y movement. A P2 speed portal therefore changes this
		// frame's P2 X advance and the next frame's P1 speed without changing
		// either player's wave Y velocity early.
		int movementVelX_fixed = s.GlobalSpeed_fixed > 0 ? s.GlobalSpeed_fixed : s.VelX_fixed;
		int x_fixed2 = s.X_fixed + movementVelX_fixed;
		if (s.GameMode == 0)
		{
			PfSlopeDiag(ref s, "cube/pre-grav");
			CubeGravity(ref s);
			PfSlopeDiag(ref s, "cube/post-grav");
			bool died = false;
			CubeEject(ref s, input, out died);
			PfSlopeDiag(ref s, "cube/post-eject");
			if (died && (x_fixed2 >> 8) > 0x20)
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
			if (s.VelY_fixed == 0)
			{
				// NES cube_movement clears cube_data bit 1 as soon as a cube is
				// grounded, before testing the held-input/orbed jump gate.  A
				// spider pad may preserve that bit through sprite_collide, but it
				// must not survive this movement and activate an orb next frame.
				bool queuedPressBeforeGroundClear = s.AirPressLatch;
				s.AirPressLatch = false;
				SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=CubeJumpGate input={(input ? 1 : 0)} orbedNow={(s.Orbed ? 1 : 0)} orbedAfterSprites={(orbedAtMovementStart ? 1 : 0)} queuedBeforeClear={(queuedPressBeforeGroundClear ? 1 : 0)} j={(s.JBlocked ? 1 : 0)} f={(s.FBlocked ? 1 : 0)} dash={s.Dashing}");
				bool flag5 = false;
				// NES cube_movement's entire grounded cube jump branch is gated by
				// dashing == 0.  In particular, colliding with terrain while a dash
				// orb is active must not turn a held input into an ordinary cube jump.
				if (s.Dashing == 0 && input && !s.JBlocked && !s.FBlocked && !orbedAtMovementStart)
				{
					flag5 = true;
				}
				else if (s.Dashing == 0 && flag && (s.JBlocked || s.FBlocked))
				{
					flag5 = true;
				}
				if (flag5)
				{
					s.VelY_fixed = GetJumpVel(s.Mini) * s.GravMul;
					s.OnGround = false;
					s.AirPressLatch = false;
					if (_speculativeDepth == 0)
					{
						_cubeJumpedThisStep = true;
					}
					PfSlopeJumpCheck(ref s);
				}
			}
			PfUpdateSlopeCounters_Fresh(ref s, velX_fixed);
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
			PfUpdateSlopeCounters_Fresh(ref s, velX_fixed);
		}
		else if (s.GameMode == 2)
		{
			int _dbgYBeforeGrav = s.Y_fixed;
			int _dbgVelBeforeGrav = s.VelY_fixed;
			BallGravityStep(ref s);
			SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=BallGravDbg Ybefore=0x{_dbgYBeforeGrav:X} Vbefore={_dbgVelBeforeGrav} Yafter=0x{s.Y_fixed:X} Vafter={s.VelY_fixed} mini={s.Mini} grav={s.GravFlipped} gMod={s.GravityMod} mapH={mapHeight}");
			bool died4 = false;
			BallEject(ref s, input, out died4);
			if (died4)
			{
				s.DeathType = (byte)((s.DeathType >= 11) ? s.DeathType : 6);
				return false;
			}
			bool ballHeldForFlip = input;
			if (s.VelY_fixed == 0 && ballHeldForFlip)
			{
				SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=BallSwitchCheck vel=0 inp={input} orbHit={orbHitThisFrame} orbed={s.Orbed} cooldown={s.BallFlipCooldown} grav={s.GravFlipped} Y=0x{s.Y_fixed:X}");
			}
			// NES ball_movement gates the manual switch on `orbed`, not
			// `orbhitonthisframe`. Merely overlapping an orb must not consume a
			// grounded ball press when the orb itself was not activated.
			if (ballHeldForFlip && !s.Orbed && s.BallFlipCooldown == 0 && s.VelY_fixed == 0)
			{
				SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=BallSwitchFire grav={s.GravFlipped}->{ !s.GravFlipped} vel={BallSwitchVel(s.Mini) * ((!s.GravFlipped) ? -1 : 1)}");
				s.GravFlipped = !s.GravFlipped;
				s.GravMul = ((!s.GravFlipped) ? 1 : (-1));
				s.VelY_fixed = BallSwitchVel(s.Mini) * s.GravMul;
				s.OnGround = false;
				s.BallFlipCooldown = 1;
				s.BallInputBuffer = 0;
				s.BallCooldownFrames = 0;
				s.AirPressLatch = false;
			}
			if (s.BallFlipCooldown != 0 && !input)
			{
				s.BallFlipCooldown = 0;
			}
			PfUpdateSlopeCounters_Fresh(ref s, velX_fixed);
			s.UfoOrbed = false;
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
			if (flag && !s.UfoOrbed)
			{
				int velY_fixed3 = UfoJumpVel(s.Mini) * -s.GravMul;
				s.VelY_fixed = velY_fixed3;
			}
			s.UfoOrbed = false;
			PfUpdateSlopeCounters_Fresh(ref s, velX_fixed);
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
			// cube_movement sets cube_data's robot-request bit after sprite_collide,
			// so a fresh press on the exact wave->robot portal frame is queued too.
			// The bit persists while airborne and is consumed only after eject has
			// put the robot on a surface.
			if (flag)
				s.RobotJumpRequested = true;
			// NES robot takeoff is blocked by an overlapping H block and by an
			// active dash. The request bit remains queued when either gate rejects
			// the landing frame (Eon, PF 5238 / Mesen cursor 5239).
			if (s.RobotJumpRequested && input && s.VelY_fixed == 0 &&
				!s.HBlocked && s.Dashing == 0 &&
				!orbedAtMovementStart && !s.Orbed)
			{
				s.VelY_fixed = -688 * s.GravMul;
				s.RobotJumpTime = 19;
				s.OnGround = false;
				s.RobotJumpRequested = false;
				// The NES robot takeoff path stores cube_data &= 1.  Bit $02 is
				// the queued airborne-orb press represented by AirPressLatch, so
				// it must be consumed with the jump instead of surviving until a
				// later orb (EndorphinRush/XX).
				s.AirPressLatch = false;
			}
			else if (s.RobotJumpTime > 0 && !s.HBlocked)
			{
				s.RobotJumpRequested = false;
				s.RobotJumpTime--;
				// gamemode_cube.h allows held continuation without a J block,
				// or a fresh press when a J block is active.
				if ((input && !s.JBlocked && !s.Orbed) ||
					(flag && s.JBlocked && !s.Orbed))
				{
					s.VelY_fixed = -688 * s.GravMul;
				}
				else
				{
					s.RobotJumpTime = 0;
				}
			}
			PfUpdateSlopeCounters_Fresh(ref s, velX_fixed);
		}
		else if (s.GameMode == 6 || s.GameMode == 10)
		{
			int num9 = velX_fixed;
			s.WasZeroedByCollision = false;
			// wave_movement snapshots dashing before snake's synthetic gravity
			// dash controller runs.  A fresh snake press therefore executes case 0
			// with zero velocity on its activation frame; case 1 begins next frame.
			int dashMode = s.Dashing;
			switch (dashMode)
			{
			case 0:
				int num10 = (s.Mini ? (num9 << 1) : num9);
				if (s.GravFlipped)
				{
					num10 = -num10;
				}
				s.VelY_fixed = num10;
				if (s.GameMode == 6)
				{
					if (input)
						s.VelY_fixed = -s.VelY_fixed;
				}
				else if (input && flag)
				{
					// Snake routes a fresh held press through
					// DASH_GRAVITY_ORB in sprite_gamemode_controller_check.
					ApplyDashOrb(ref s, 0x46);
					s.AirPressLatch = false;
				}
				if ((s.SlopeFrames | s.SlopeWasOnCounter) == 0)
				{
					s.Y_fixed += s.VelY_fixed;
				}
				else
				{
					s.VelY_fixed = 0;
				}
				break;
			case 1:
				// Horizontal dash: NES sets vel_y to 1 and returns without Y motion.
				s.VelY_fixed = 1;
				break;
			case 2:
				s.VelY_fixed = -num9;
				s.Y_fixed += s.VelY_fixed;
				break;
			case 3:
				s.VelY_fixed = num9;
				s.Y_fixed += s.VelY_fixed;
				break;
			case 4:
				s.VelY_fixed = num9;
				s.Y_fixed -= s.VelY_fixed;
				break;
			case 5:
				s.VelY_fixed = num9;
				s.Y_fixed += s.VelY_fixed;
				break;
			}
			bool died7 = false;
			WaveEject(ref s, input, out died7);
			// state_game checks cube_data only after x_movement and explicitly
			// clears both players' death bits while player X is <= $20.  Wave eject
			// still performs its movement/collision work during those opening ticks,
			// but it cannot commit the death yet (Denouement starts inside slopes).
			if (died7 && (x_fixed2 >> 8) > 0x20)
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
			PfUpdateSlopeCounters_Fresh(ref s, velX_fixed);
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
			if (s.InvincibleCounter == 0 && tileCollision >= MetatileCollision.COL_SLOPE_RD45 && tileCollision <= MetatileCollision.COL_SLOPE_LU66_TOP && (s.Mini || tileCollision != MetatileCollision.COL_SLOPE_LU45) && (!s.Mini || (tileCollision != MetatileCollision.COL_SLOPE_LU66_TOP && tileCollision != MetatileCollision.COL_SLOPE_LU66_BOT)) && PfSlopeCalc(num14, num15, tileCollision).hit)
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
				if (s.InvincibleCounter == 0 && tileCollision2 >= MetatileCollision.COL_SLOPE_RD45 && tileCollision2 <= MetatileCollision.COL_SLOPE_LU66_TOP && (s.Mini || tileCollision2 != MetatileCollision.COL_SLOPE_LU45) && (!s.Mini || (tileCollision2 != MetatileCollision.COL_SLOPE_LU66_TOP && tileCollision2 != MetatileCollision.COL_SLOPE_LU66_BOT)) && PfSlopeCalc(num16, num17, tileCollision2).hit)
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
			s.OnGround = s.VelY_fixed == 0;
			bool canSpiderTeleport = s.VelY_fixed == 0 && !s.Orbed && s.Dashing == 0;
			// NES spider_movement() tests cube_data&2 after spider_eject(), while
			// x_movement clears that bit only later on a released input.  Preserve
			// the frame-start queued press for this movement gate so a press held
			// through the landing frame can still teleport even if A is released
			// on that same frame.
			bool spiderTeleportGate = flag || s.AirPressLatch || queuedPressAtFrameStart || (input && s.BlackOrbed);
			if (canSpiderTeleport && spiderTeleportGate)
			{
				if (!s.GravFlipped)
				{
					s.GravFlipped = true;
					s.GravMul = -1;
					if (SpiderScanUp(ref s))
					{
						s.DeathType = 12;
						// spider_movement always executes this after spider_up_wait,
						// even when the wait exited through its boundary-death guard.
						ApplyNesPlayerYHighSubtract(ref s, s.EjectU);
					}
					s.VelY_fixed = 0;
				}
				else
				{
					s.GravFlipped = false;
					s.GravMul = 1;
					if (SpiderScanDown(ref s))
					{
						s.DeathType = 12;
						ApplyNesPlayerYHighSubtract(ref s, s.EjectD);
					}
					s.VelY_fixed = 0;
				}
				s.BlackOrbed = false;
				s.AirPressLatch = false;
			}
			else if (!input)
			{
				s.BlackOrbed = false;
			}
			// NES consumes currplayer_slope_frames in x_movement_coll(), after
			// spider_movement() has already run its grounded teleport gate. If we
			// apply slope velocity before this gate, a slope landing that zeroed
			// velocity can wrongly become airborne and miss the same-frame flip.
			PfUpdateSlopeCounters_Fresh(ref s, velX_fixed);
			s.OnGround = s.VelY_fixed == 0;
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
			// NES gamemode_ball.h handles GAMEMODE_SWING through ball_eject(),
			// not ship/ufo eject.  Swing still uses swing gravity and a
			// press-to-flip after ejection, but its ceiling/floor probes must
			// match ball_eject ordering and offsets exactly.
			BallEject(ref s, input, out died9);
			if (died9)
			{
				s.DeathType = 6;
				return false;
			}
			PfUpdateSlopeCounters_Fresh(ref s, velX_fixed);
			if (flag && !s.UfoOrbed && !s.Orbed)
			{
				bool gravityBeforeFlip = s.GravFlipped;
				s.GravFlipped = !s.GravFlipped;
				s.GravMul = ((!s.GravFlipped) ? 1 : (-1));
				// gamemode_ball calls bg_coll_floor_spikes immediately after the
				// swing flip. Generic.y still has the one-pixel ball_eject offset
				// selected using the PRE-flip gravity; x_movement_coll's later raw-Y
				// spike pass is not equivalent at tile-half boundaries.
				if (CheckFloorSpikes(ref s, gravityBeforeFlip ? -1 : 1))
				{
					s.DeathType = 7;
					if (_speculativeDepth == 0)
					{
						_lastDeathReason = "SWING_FLIP_SPIKE";
						_lastDeathX = s.X_fixed >> 8;
						_lastDeathY = s.Y_fixed >> 8;
					}
					return false;
				}
			}
			s.Orbed = false;
			s.UfoOrbed = false;
			if (CheckDeathCollision(ref s))
			{
				s.DeathType = 3;
				return false;
			}
		}
		else if (s.GameMode == 8)
		{
			CubeGravity(ref s);
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
			// Current gamemode_cube.h permits a held-input buffered jump for a
			// grounded Ninja. Air jumps still require a fresh press and consume one
			// of the three Ninja jumps.
			bool ninjaGrounded = s.VelY_fixed == 0;
			if (ninjaGrounded)
			{
				s.NinjaJumps = 3;
			}
			bool ninjaJump = false;
			if (input && !s.JBlocked && !s.FBlocked && ninjaGrounded)
			{
				ninjaJump = !s.Orbed;
			}
			else if (flag && (s.JBlocked || s.FBlocked ||
				(s.NinjaJumps > 0 && !orbHitThisFrame && !s.Orbed)))
			{
				ninjaJump = true;
				s.NinjaJumps = (s.NinjaJumps - 1) & 0xFF;
				orbHitThisFrame = true;
			}
			if (ninjaJump)
			{
				s.VelY_fixed = GetJumpVel(s.Mini) * s.GravMul;
				s.OnGround = false;
				s.AirPressLatch = false;
				PfSlopeJumpCheck(ref s);
			}
			PfUpdateSlopeCounters_Fresh(ref s, velX_fixed);
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
			PfUpdateSlopeCounters_Fresh(ref s, velX_fixed);
			if (flag && !orbHitThisFrame && !s.Orbed)
			{
				int num24 = (s.Mini ? SharedPhysics.PadOrbHeights_Mini[6][7] : SharedPhysics.PadOrbHeights[6][7]);
				s.VelY_fixed = (s.GravFlipped ? num24 : (-num24));
				// Pogo's synthetic black-orb press stores cube_data bit $02 but
				// does not set orbed[].  AirPressLatch already represents that
				// cube-data bit for this fresh airborne press.  Setting Orbed here
				// wrongly suppresses a held cube jump if a cube portal is reached
				// before the button is released (CarefreeVictory).
			}
			else if (!input)
			{
				s.Orbed = false;
			}
			s.UfoOrbed = false;
			if (CheckDeathCollision(ref s))
			{
				s.DeathType = 3;
				return false;
			}
		}
		// x_movement has now run. Autoscroll always advances right; platformer
		// performs its second R/L probe and chooses left/neutral/right here.
		bool platformerSideDeath = false;
		if (ForcePlatformer && !_dualP2Guard)
		{
			x_fixed2 = ResolvePlatformerHorizontal(ref s, horizontalDirection,
				movementVelX_fixed, out platformerSideDeath);
		}
		else
		{
			s.VelX_fixed = movementVelX_fixed;
		}
		if (_dualP2Guard)
		{
			s.X_fixed = x_fixed2;
		}
		// SKULL_ORB sets cube_data's death bit during sprite_collide. The main
		// game loop observes that bit only after movement has completed, and clears
		// it instead of killing while player 1 is still within the opening $20 px.
		if (skullDeathThisFrame)
		{
			s.X_fixed = x_fixed2;
			if ((x_fixed2 >> 8) > 0x20)
			{
				if (_speculativeDepth == 0)
				{
					_lastDeathReason = "SKULL_ORB";
					_lastDeathX = s.X_fixed >> 8;
					_lastDeathY = s.Y_fixed >> 8;
				}
				s.DeathType = 9;
				return false;
			}
		}
		// spider_up_wait/spider_down_wait set cube_data's death bit as soon as
		// their screen-byte guard reaches <= $07 or >= $F8.  That bit is tested
		// only after x_movement, and the level-start X guard clears it through
		// $20 just like every other NES collision death.
		if (s.DeathType == 12)
		{
			s.X_fixed = x_fixed2;
			if ((x_fixed2 >> 8) > 0x20)
			{
				if (_speculativeDepth == 0)
				{
					_lastDeathReason = "SPIDER_SCAN_BOUNDARY";
					_lastDeathX = s.X_fixed >> 8;
					_lastDeathY = s.Y_fixed >> 8;
				}
				return false;
			}
			s.DeathType = 0;
		}
		if (platformerSideDeath)
		{
			s.X_fixed = x_fixed2;
			if ((x_fixed2 >> 8) > 0x20)
			{
				if (_speculativeDepth == 0)
				{
					_lastDeathReason = "PLATFORMER_SIDE_DEATH";
					_lastDeathX = s.X_fixed >> 8;
					_lastDeathY = s.Y_fixed >> 8;
				}
				s.DeathType = 8;
				return false;
			}
		}
		if (s.InvincibleCounter == 0 && CheckFloorSpikes(ref s))
		{
			// NES x_movement_coll only sets cube_data here. runthecolls still
			// calls x_movement before the frame is reported as dead.
			s.X_fixed = x_fixed2;
			if (_speculativeDepth == 0)
			{
				_lastDeathReason = "FLOOR_SPIKE";
				_lastDeathX = s.X_fixed >> 8;
				_lastDeathY = s.Y_fixed >> 8;
			}
			s.DeathType = 7;
			return false;
		}
		if (!ForcePlatformer && s.InvincibleCounter == 0 && (s.GameMode == 0 || s.GameMode == 1 || s.GameMode == 2 || s.GameMode == 3 || s.GameMode == 4 || s.GameMode == 5 || s.GameMode == 6 || s.GameMode == 7 || s.GameMode == 8 || s.GameMode == 9 || s.GameMode == 10))
		{
			bool flag8 = CheckForwardCollision(ref s);
			if (flag8)
			{
				// bg_coll_R marks cube_data, then NES still executes x_movement.
				s.X_fixed = x_fixed2;
				if (_speculativeDepth == 0)
				{
					_lastDeathReason = "FWD_DEATH";
					_lastDeathX = s.X_fixed >> 8;
					_lastDeathY = s.Y_fixed >> 8;
				}
				s.DeathType = 8;
				return false;
			}
			if ((s.SlopeWasOnCounter | s.SlopeFrames) == 0 && s.GameMode != 6 && s.GameMode != 10)
			{
				int hbW = ((s.GameMode == 6 || s.GameMode == 10) ? 8 : GetHitboxW(s.Mini));
				int hbH = ((s.GameMode == 6 || s.GameMode == 10) ? 8 : GetHitboxH(s.Mini));
				int hbOffY = ((s.GameMode != 6 && s.GameMode != 10) ? GetHitboxOffsetY(s.GameMode, s.Mini, s.GravFlipped) : 0);
				var (num26, num27) = SharedPhysics.GetForwardSlopeNudge(in _collisionMap, s.X_fixed >> 8, NesPlayerBgCollisionY_px(s.Y_fixed, s.CameraY_fixed), hbW, hbH, hbOffY, s.GameMode, s.Mini, s.GravFlipped, s.SlopeType);
				if (num26 != 0)
				{
					s.Y_fixed += num26 << 8;
				}
				if (num27 != 0)
				{
					s.SlopeType = num27;
					// NES's forward bg_coll_R slope probe writes current slope
					// state, but does not promote it to last_slope_type. A later
					// directional slope rejection must still restore the previous
					// value (often zero), as seen in Blast Processing.
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
		if (!_dualP2Guard)
		{
			if (!s.DualActive &&
				(s.GameMode == 0 || s.GameMode == 4 || s.GameMode == 8 ||
				 s.GameMode == 9 || s.GameMode == 11 || s.NoCamLockForced))
			{
				if (s.ExitPortalTimer != 0)
				{
					s.ExitPortalTimer--;
				}
				ApplyNesCubeRobotYScroll(ref s);
			}
			else
			{
				ApplyNesShipStyleYScroll(ref s);
			}
		}
		int num53 = s.Y_fixed - s.CameraY_fixed;
		if (!s.WrapMode)
		{
			if (num53 < 1536)
			{
				s.X_fixed = x_fixed2;
				if ((x_fixed2 >> 8) <= 0x20)
				{
					// state_game clears cube_data's death bit during the protected
					// opening distance. Keep the post-scan position so collision can
					// engage normally once the player reaches screen X $10.
					s.DeathType = 0;
				}
				else
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
			}
			if (num53 > 63744)
			{
				s.X_fixed = x_fixed2;
				if ((x_fixed2 >> 8) <= 0x20)
				{
					s.DeathType = 0;
				}
				else
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
		}
		else if (num53 < 1536)
		{
			s.Y_fixed = s.CameraY_fixed + 63744;
		}
		else if (num53 > 63744)
		{
			s.Y_fixed = s.CameraY_fixed + 1536;
		}
		if (CheckDeathCollision(ref s))
		{
			// bg_coll_death probes Generic.x from before x_movement, but the
			// persisted player position has already advanced for this frame.
			s.X_fixed = x_fixed2;
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
			s.X_fixed = x_fixed2;
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
		if (!ForcePlatformer && s.X_fixed != x_fixed3)
		{
			if (CheckDeathCollision(ref s, polluteSlopeState: false))
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
			// P2 does not run x_movement, but the NES synchronizes currplayer_x to
			// P1's already-advanced X before x_movement_coll. That routine reloads
			// Generic.x, so P2's later bg_coll_death slope probe uses the synced X.
			// The ordinary shared death helper intentionally excludes slopes; apply
			// the missing slope part here, only for that dual-P2 synchronization.
			if (_dualP2Guard && CheckSlopePenetrationDeath(ref s))
			{
				if (_speculativeDepth == 0)
				{
					_lastDeathReason = "SLOPE_DEATH_NEWX_P2";
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
		// NES decrements the global counter once after P1 runthecolls and before
		// P2. The recursive P2 step must observe the newly decremented value.
		if (!_dualP2Guard && s.InvincibleCounter > 0)
		{
			s.InvincibleCounter--;
		}
		s.PrevInputHeld = input;
		// NES runs check_spr_objects once, after P1 movement/scroll and before
		// switching to P2. P2 reuses that exact 16-slot ordering/active state.
		if (!_dualP2Guard)
		{
			ProcessPlatformerXScroll(ref s);
			CheckSprObjects(ref s);
			// Capture the post-P1-movement scroll. NES decremented player1_x by this
			// frame's scroll delta before P2's sprite_collide, so the P2 collision box
			// must be positioned against this same scroll (not P2's own pre-move X).
			_dualP2ScrollX_px = GetScrollX_px(in s);
		}
		if (s.DualActive && !_dualP2Guard)
		{
			// spcl_dual_pt does not initialize player_vel_x[1]; dormant P2 speed
			// survives single sections and is synchronized after each P2 step.
			int p1VelX_fixed = s.VelX_fixed;
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
			bool robotJumpRequested = s.RobotJumpRequested;
			int slopeWasOnCounter = s.SlopeWasOnCounter;
			int slopeFrames = s.SlopeFrames;
			int slopeType = s.SlopeType;
			int lastSlopeType = s.LastSlopeType;
			bool orbed = s.Orbed;
			bool ufoOrbed = s.UfoOrbed;
			bool blackOrbed = s.BlackOrbed;
			bool airPressLatch = s.AirPressLatch;
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
			s.RobotJumpRequested = s.P2_RobotJumpRequested;
			s.SlopeWasOnCounter = s.P2_SlopeWasOnCounter;
			s.SlopeFrames = s.P2_SlopeFrames;
			s.SlopeType = s.P2_SlopeType;
			s.LastSlopeType = s.P2_LastSlopeType;
			s.Orbed = s.P2_Orbed;
			s.UfoOrbed = s.P2_UfoOrbed;
			s.BlackOrbed = s.P2_BlackOrbed;
			s.AirPressLatch = s.P2_AirPressLatch;
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
			// spcl_dual_pt does not initialize player_vel_x[1]. The first
			// dual uses zero; later dual sections reuse the dormant saved speed.
			s.VelX_fixed = s.P2_VelX_fixed;
			bool flag11 = false;
			if (s.GameMode == 2 && ballCooldownFrames > 0)
			{
				flag11 = true;
			}
			bool input2 = input && !flag11;
			int y_fixed2 = s.Y_fixed;
			int velY_fixed5 = s.VelY_fixed;
			bool gravFlipped2 = s.GravFlipped;
			foreach (int item in _p1OrbIndicesThisFrame ?? Enumerable.Empty<int>())
			{
				s.ProcessedSprites.Remove(item);
			}
			_p2OrbFlippedOtherGrav = false;
			s.P2_SingleExitCaptured = false;
			_dualP2Guard = true;
			_dualP2ScrollXValid = true;
			bool endLevel2;
			bool flag12 = StepFrame(ref s, input2, out endLevel2);
			_dualP2ScrollXValid = false;
			_dualP2Guard = false;
			s.VelX_fixed = p1VelX_fixed;
			foreach (int item2 in _p1OrbIndicesThisFrame ?? Enumerable.Empty<int>())
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
			// NES synchronizes P2 X velocity to P1 after P2 movement, before
			// saving player_vel_x[1].
			s.P2_VelX_fixed = p1VelX_fixed;
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
			s.P2_RobotJumpRequested = s.RobotJumpRequested;
			s.P2_SlopeWasOnCounter = s.SlopeWasOnCounter;
			s.P2_SlopeFrames = s.SlopeFrames;
			s.P2_SlopeType = s.SlopeType;
			s.P2_LastSlopeType = s.LastSlopeType;
			s.P2_Orbed = s.Orbed;
			s.P2_UfoOrbed = s.UfoOrbed;
			s.P2_BlackOrbed = s.BlackOrbed;
			s.P2_AirPressLatch = s.AirPressLatch;
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
				// spcl_sngl_pt copies currplayer_y/gravity/vel_y into
				// player[0] immediately at the portal's sprite slot.  Later P2
				// movement must not be included, while earlier same-pass portals
				// (for example wave -> swing) must be included.
				s.Y_fixed = s.P2_SingleExitCaptured ? s.P2_SingleExitY_fixed : y_fixed2;
				s.VelY_fixed = s.P2_SingleExitCaptured ? s.P2_SingleExitVelY_fixed : velY_fixed5;
				s.GravFlipped = s.P2_SingleExitCaptured ? s.P2_SingleExitGravFlipped : gravFlipped2;
				s.GravMul = s.P2_SingleExitCaptured ? s.P2_SingleExitGravMul : ((!gravFlipped2) ? 1 : (-1));
				s.P2_SingleExitCaptured = false;
				s.Mini = flag10;
				s.WasZeroedByCollision = wasZeroedByCollision;
				s.OnGround = onGround;
				s.BallFlipCooldown = ballFlipCooldown;
				s.BallInputBuffer = ballInputBuffer;
				s.BallCooldownFrames = ballCooldownFrames;
				s.RobotJumpTime = robotJumpTime;
				s.RobotJumpRequested = robotJumpRequested;
				s.SlopeWasOnCounter = slopeWasOnCounter;
				s.SlopeFrames = slopeFrames;
				s.SlopeType = slopeType;
				s.LastSlopeType = lastSlopeType;
				s.Orbed = orbed;
				s.UfoOrbed = ufoOrbed;
				s.BlackOrbed = blackOrbed;
				s.AirPressLatch = airPressLatch;
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
			s.RobotJumpRequested = robotJumpRequested;
			s.SlopeWasOnCounter = slopeWasOnCounter;
			s.SlopeFrames = slopeFrames;
			s.SlopeType = slopeType;
			s.LastSlopeType = lastSlopeType;
			s.Orbed = orbed;
			s.UfoOrbed = ufoOrbed;
			s.BlackOrbed = blackOrbed;
			s.AirPressLatch = airPressLatch;
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
		num = SharedPhysics.ApplyNesGravityModifier(num, state.GravityMod);
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
		WrapNesPlayerScreenY(ref s);
	}

	private void CubeEject(ref SimState s, bool input, out bool died)
	{
		died = false;
		SharedPhysics.EjectResult ejectResult = SharedPhysics.CubeEject(in _collisionMap, s.X_fixed, s.Y_fixed, s.VelY_fixed, s.VelX_fixed, s.GravFlipped, s.Mini, s.GameMode, input, s.SlopeWasOnCounter, s.SlopeFrames, s.SlopeType, s.SlopeJumpHigher, s.LastSlopeType, s.CameraY_fixed, updateSlopeCounters: false, hBlocked: s.HBlocked, fBlocked: s.FBlocked);
		s.Y_fixed = ejectResult.NewY_fixed;
		s.VelY_fixed = ejectResult.NewVelY_fixed;
		s.OnGround = ejectResult.OnGround;
		s.WasZeroedByCollision = ejectResult.WasZeroed;
		s.SlopeType = ejectResult.SlopeType;
		s.SlopeFrames = ejectResult.SlopeFrames;
		s.SlopeWasOnCounter = ejectResult.SlopeWasOnCounter;
		s.SlopeJumpHigher = ejectResult.SlopeJumpHigher;
		s.LastSlopeType = ejectResult.LastSlopeType;
		s.GravFlipped = ejectResult.NewGravFlipped;
		s.GravMul = s.GravFlipped ? -1 : 1;
		if (ejectResult.EjectUWritten)
		{
			s.EjectU = ejectResult.EjectU;
		}
		if (ejectResult.EjectDWritten)
		{
			s.EjectD = ejectResult.EjectD;
		}
		if (ejectResult.Died)
		{
			died = true;
			s.DeathType = 6;
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
		SharedPhysics.EjectResult ejectResult = SharedPhysics.BallEject(in _collisionMap, s.X_fixed, s.Y_fixed, s.VelY_fixed, s.VelX_fixed, s.GravFlipped, s.Mini, s.GameMode, input, s.SlopeWasOnCounter, s.SlopeFrames, s.SlopeType, s.SlopeJumpHigher, s.LastSlopeType, s.CameraY_fixed, updateSlopeCounters: false);
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
		int playerY_px = NesPlayerY_px(s.Y_fixed, s.CameraY_fixed);
		int hitboxW = GetHitboxW(s.Mini);
		int hitboxH = GetHitboxH(s.Mini);
		int hitboxOffsetY = GetHitboxOffsetY(2, s.Mini, s.GravFlipped);
		return SharedPhysics.BallGroundedProbeSpikeDeath(in _collisionMap, playerX_px, playerY_px, hitboxW, hitboxH, hitboxOffsetY, s.GravFlipped);
	}

	private bool BallIsGrounded(ref SimState s)
	{
		return SharedPhysics.BallIsGrounded(in _collisionMap, s.X_fixed >> 8, NesPlayerY_px(s.Y_fixed, s.CameraY_fixed), GetHitboxW(s.Mini), GetHitboxH(s.Mini), GetHitboxOffsetY(2, s.Mini, s.GravFlipped), s.GravFlipped);
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
		List<(int, int)>? list = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
		int num5 = SimulateForwardWithJumpAt(state, -1, holdAfterLanding: false, list);
		OnSpeculativePath?.Invoke(list, -1, num5, arg4: false);
		int num6 = 0;
		for (int j = 0; j < 20; j++)
		{
			List<(int, int)>? list2 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
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
		List<(int, int)>? list = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
		int num5 = SimulateForwardWithJumpAt(state, -1, holdAfterLanding: false, list);
		OnSpeculativePath?.Invoke(list, -1, num5, arg4: false);
		int num6 = 0;
		for (int j = 0; j < 25; j++)
		{
			List<(int, int)>? list2 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
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
		List<(int, int)>? list = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
		int num5 = SimulateForwardWithJumpAt(state, -1, holdAfterLanding: false, list);
		OnSpeculativePath?.Invoke(list, -1, num5, arg4: false);
		int num6 = 0;
		int num7 = -1;
		for (int j = 0; j < 20; j++)
		{
			List<(int, int)>? list2 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
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
		List<(int, int)>? list = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
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
			List<(int, int)>? list3 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
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
			List<(int, int)>? list4 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
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
						List<(int, int)>? list5 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
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
		List<(int, int)>? list = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
		int num7 = SimulateForwardWithJumpAt(state, -1, holdAfterLanding: false, list);
		OnSpeculativePath?.Invoke(list, -1, num7, arg4: false);
		_speculativeDepth--;
		int num8 = num7;
		int num9 = -1;
		_speculativeDepth++;
		int num10 = Math.Min(15, 90);
		for (int k = 0; k < num10; k++)
		{
			List<(int, int)>? list2 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
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
		List<(int, int)>? list = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
		int num3 = SimulateRobotForward(state, 0, list);
		_speculativeDepth--;
		OnSpeculativePath?.Invoke(list, -1, num3, arg4: false);
		int num4 = num3;
		int num5 = 0;
		_speculativeDepth++;
		int[] array = new int[8] { 1, 3, 5, 8, 11, 14, 17, 19 };
		foreach (int num6 in array)
		{
			List<(int, int)>? list2 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
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
		List<(int, int)>? list = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
		int num3 = SimulateNinjaForward(state, null, list);
		_speculativeDepth--;
		OnSpeculativePath?.Invoke(list, -1, num3, arg4: false);
		int num4 = num3;
		int[]? array = null;
		int num5 = (flag3 ? 3 : state.NinjaJumps);
		_speculativeDepth++;
		List<(int, int)>? list2 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
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
				List<(int, int)>? list3 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
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
					List<(int, int)>? list4 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
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
		List<(int, int)>? list = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
		int num3 = SimulateWaveForward(state, hold: true, list);
		List<(int, int)>? list2 = ((_speculativeDepth == 1 && OnSpeculativePath != null) ? new List<(int, int)>() : null);
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
			if (tileCollision != 0 && !SharedPhysics.IsDeathCollision(tileCollision))
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
			if (tileCollision2 != 0 && !SharedPhysics.IsDeathCollision(tileCollision2))
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
		SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=WaveEject.in X={s.X_fixed >> 8} Y={s.Y_fixed >> 8} Vy={s.VelY_fixed} input={(input ? 1 : 0)} sFr={s.SlopeFrames} swOn={s.SlopeWasOnCounter} sT={s.SlopeType}");
		int waveYPre = s.Y_fixed;
		int waveVyPre = s.VelY_fixed;
		int num = s.X_fixed >> 8;
		int num2 = s.Y_fixed >> 8;
		int num3 = num + 4;
		int num4 = ((!s.Mini) ? 4 : 0);
		int num5 = num2 + num4;
		// sprite_collide assigns the 8x8 box only to GAMEMODE_WAVE.  Snake
		// deliberately receives the cube dimensions, and wave_movement changes
		// only Generic.x/y before calling wave_eject.  Therefore every U/D
		// slope and ordinary probe below must retain 15x15 (8x7 when mini) for
		// snake instead of sharing wave's 8x8 box.
		int genericWidth = s.GameMode == 10 ? (s.Mini ? 8 : 15) : 8;
		int genericHeight = s.GameMode == 10 ? (s.Mini ? 7 : 15) : 8;
		int genericCenterOffset = (16 - genericHeight) >> 1;
		// bg_coll_U/bg_coll_D skip their slope pass until currplayer_x reaches
		// $10. Ordinary ceiling/floor probes still run below.
		if (s.VelY_fixed >= 0 && num >= 0x10)
		{
			int num6 = num3;
			int num7 = (s.Mini ? genericCenterOffset : 0);
			int num8 = num5 + genericHeight - 2 + num7;
			int num9 = genericWidth;
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
				// bg_coll_slope reaches col_end only when it produced a non-zero
				// slope type. A 66-degree solid-half hit returns before these
				// counter and input-dependent ejection side effects.
				if (num12 != 0)
				{
					s.SlopeType = num12;
					s.SlopeFrames = 1;
					s.SlopeWasOnCounter = 3;
					num11 = PfAdjustWaveSlopeEjection(num11, num12, input, s.GravFlipped);
				}
				int downProbeSlopeType = num12 != 0 ? num12 : s.SlopeType;
				bool flag2 = (downProbeSlopeType & 4) != 0;
				if ((i == 0 && flag2) || (i == 1 && !flag2))
				{
					s.SlopeType = s.LastSlopeType;
					continue;
				}
				if ((s.LastSlopeType & 4) != 0 && (s.SlopeType & 4) == 0 &&
					s.LastSlopeType != 0 && s.SlopeType != 0)
				{
					s.SlopeType = s.LastSlopeType;
					num11 = s.VelX_fixed >> 8;
				}
				if (s.SlopeType != 0)
					s.LastSlopeType = s.SlopeType;
				s.EjectD = unchecked((byte)num11);
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
				SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=WaveEject.out Yold={waveYPre >> 8}.{(waveYPre & 0xFF):X2} Ynew={s.Y_fixed >> 8}.{(s.Y_fixed & 0xFF):X2} Vyold={waveVyPre} Vynew={s.VelY_fixed} died={(died ? 1 : 0)} sFr={s.SlopeFrames} swOn={s.SlopeWasOnCounter} sT={s.SlopeType}");
				return;
			}
		}
		else
		{
			int num14 = num3;
			int num15 = genericCenterOffset;
			int num16 = num5 + num15 + (s.Mini ? 1 : 2);
			int num17 = genericWidth;
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
				if (num20 != 0)
				{
					s.SlopeType = num20;
					s.SlopeFrames = 1;
					s.SlopeWasOnCounter = 3;
					num19 = PfAdjustWaveSlopeEjection(num19, num20, input, s.GravFlipped);
				}
				int upProbeSlopeType = num20 != 0 ? num20 : s.SlopeType;
				bool flag4 = (upProbeSlopeType & 4) != 0;
				if (!(j == 0 && flag4) && (j != 1 || flag4))
				{
					if ((s.LastSlopeType & 4) != 0 && (s.SlopeType & 4) == 0 &&
						s.LastSlopeType != 0 && s.SlopeType != 0)
					{
						s.SlopeType = s.LastSlopeType;
						num19 = s.VelX_fixed >> 8;
					}
					if (s.SlopeType != 0)
						s.LastSlopeType = s.SlopeType;
					s.EjectU = unchecked((byte)(-num19));
					if (s.Dblocked)
					{
						int num21 = s.Y_fixed >> 8;
						// bg_coll_return_slope_U stores eject_U = -tmp8, then
						// wave_eject executes Y -= eject_U.
						num21 += num19;
						s.Y_fixed = (num21 << 8) | (s.Y_fixed & 0xFF);
						s.VelY_fixed = 0;
						s.WasZeroedByCollision = true;
					}
					else
					{
						died = true;
					}
					SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=WaveEject.out Yold={waveYPre >> 8}.{(waveYPre & 0xFF):X2} Ynew={s.Y_fixed >> 8}.{(s.Y_fixed & 0xFF):X2} Vyold={waveVyPre} Vynew={s.VelY_fixed} died={(died ? 1 : 0)} sFr={s.SlopeFrames} swOn={s.SlopeWasOnCounter} sT={s.SlopeType}");
					return;
				}
				s.SlopeType = s.LastSlopeType;
			}
		}
		if (s.VelY_fixed < 0)
		{
			int num22 = num3 + 4;
			int num23 = (s.Mini ? genericCenterOffset : 0);
			int num24 = num5 + num23 - 1;
			// bg_coll_U's tile phase starts four pixels inside Generic.x, then
			// adds width/2 and finally uses Generic.x+width.  For wave this is
			// x+8,x+12,x+12; snake's retained 15px box is x+8,x+15,x+19.
			int ceilingProbeY = num24 + 1;
			int[] ceilingProbeXs = { num22, num22 + (genericWidth >> 1), num3 + genericWidth };
			bool flag5 = false;
			bool flag6 = false;
			int waveEjectU = 0;
			MetatileCollision metatileCollision = MetatileCollision.COL_NONE;
			foreach (int ceilingProbeX in ceilingProbeXs)
			{
				var ceiling = SharedPhysics.CheckCeilingReturnU(in _collisionMap,
					num, ceilingProbeX, ceilingProbeY, 0);
				if (ceiling.spikeDeath)
				{
					flag6 = true;
					break;
				}
				if (ceiling.hit)
				{
					flag5 = true;
					waveEjectU = ceiling.ejectU;
					metatileCollision = ceiling.hitCollision;
					if (metatileCollision == MetatileCollision.COL_FLOOR_CEIL)
						s.Dblocked = true;
					break;
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
					int num30 = (sbyte)(byte)waveEjectU;
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
			int num33 = (s.Mini ? genericCenterOffset : 0);
			int num34 = num5 + num33;
			int[] array = new int[3]
			{
				num32,
				num32 + (genericWidth >> 1),
				num3 + genericWidth
			};
			bool flag7 = false;
			bool flag8 = false;
			MetatileCollision metatileCollision2 = MetatileCollision.COL_NONE;
			bool flag9 = false;
			bool flag10 = false;
			int num35 = num34 + genericHeight;
			foreach (int num36 in array)
			{
				int tileX6 = num36 / 16;
				int tileY6 = num35 / 16;
				MetatileCollision tileCollision5 = GetTileCollision(tileX6, tileY6);
				int localX3 = (num36 % 16 + 16) % 16;
				int localY3 = (num35 % 16 + 16) % 16;
				if (MetatileCollisionTable.TileKillsAtPixel(tileCollision5, localX3, localY3))
				{
					flag9 = true;
					break;
				}
				var (flag11, num37, flag12, _, metatileCollision3) = SharedPhysics.CheckFloorDetailed(in _collisionMap, num36, num34, 0, genericHeight, s.VelY_fixed);
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
					if (tileCollision6 != 0)
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
		int currplayerScreenX_px = playerX_px - GetScrollX_px(in s);
		int num3 = offsetY + num2;
		if (!s.GravFlipped)
		{
			var (flag, num4, slopeType) = PfCheckSlopes(ref s, input, offsetY);
			if (flag)
			{
				s.EjectD = unchecked((byte)num4);
				s.Y_fixed = ((s.Y_fixed >> 8) - num4 << 8) | (s.Y_fixed & 0xFF);
				s.VelY_fixed = 0;
				s.WasZeroedByCollision = true;
				// CheckSlopesDown owns the NES col_end side effects. In particular,
				// a 66-degree solid-half hit retains the prior slope type but returns
				// before arming a fresh slope frame.
				return;
			}
			int velYBeforeEject = s.VelY_fixed;
			// NES bg_coll_D always checks slopes, but its ordinary bottom probes
			// only run while velocity is downward/non-negative.
			var (flag2, num5) = velYBeforeEject >= 0
				? BgCollD_Spider(playerX_px, num3, width, num, currplayerScreenX_px, useEjectProbes: true)
				: (false, 0);
			if (flag2)
			{
				s.EjectD = unchecked((byte)num5);
				s.Y_fixed = ((s.Y_fixed >> 8) - num5 << 8) | (s.Y_fixed & 0xFF);
				s.VelY_fixed = 0;
				s.WasZeroedByCollision = true;
				// The NES ordinary side probe restores the saved slope type while
				// leaving the slope frame/counter intact.
				if (s.SlopeFrames != 0)
					s.SlopeType = s.LastSlopeType;
			}
			else
			{
				s.WasZeroedByCollision = false;
			}
			if (!flag2 && velYBeforeEject >= 0 && CheckEjectDeathSideEffect(playerX_px, num3, width, num, true))
				died = true;
		}
		else
		{
			var (slopeHitUp, slopeEjectUp, slopeTypeUp) = PfCheckSlopesUp(ref s, input, offsetY);
			if (slopeHitUp)
			{
				s.EjectU = unchecked((byte)(-slopeEjectUp));
				// NES spider_eject calls bg_coll_U with Generic.y = high_byte(y)-2
				// and applies high_byte(currplayer_y) -= eject_U + 1.  Shared
				// slope-U returns tmp8 as a positive ejection, so this is +tmp8-1.
				s.Y_fixed = (((s.Y_fixed >> 8) + slopeEjectUp - 1) << 8) | (s.Y_fixed & 0xFF);
				s.VelY_fixed = 0;
				s.WasZeroedByCollision = true;
				// CheckSlopesUp likewise owns fresh-frame/counter writes; do not
				// promote a retained type after a 66-degree solid-half return.
				return;
			}
			int velYBeforeEject = s.VelY_fixed;
			// NES bg_coll_U always checks slopes, but its ordinary top probes
			// only run while velocity is upward/negative.
			var (flag3, num6) = velYBeforeEject < 0
				? BgCollU_Spider(playerX_px, num3, width, num, currplayerScreenX_px, useEjectProbes: true)
				: (false, 0);
			if (flag3)
			{
				// The helper returns the resulting move-down amount. NES applies
				// -(eject_U + 1), so preserve the corresponding persistent byte.
				s.EjectU = unchecked((byte)(-num6 - 1));
				// NES spider_eject probes bg_coll_U with Generic.y offset/mini
				// centering, but applies eject_U + 1 back to currplayer_y itself.
				// The mini probe offset is detection-only and must not remain in
				// the final player coordinate.
				s.Y_fixed = (num3 + num6 - num2 << 8) | (s.Y_fixed & 0xFF);
				s.VelY_fixed = 0;
				s.WasZeroedByCollision = true;
				if (s.SlopeFrames != 0)
					s.SlopeType = s.LastSlopeType;
			}
			else
			{
				s.WasZeroedByCollision = false;
			}
			if (!flag3 && velYBeforeEject < 0 && CheckEjectDeathSideEffect(playerX_px, num3, width, num, false))
				died = true;
		}
	}

	private bool SpiderScanUp(ref SimState s, int playerXBias = 0)
	{
		// A spider orb/pad runs during sprite_collide.  That routine assigns the
		// 8x8 box only to wave; snake deliberately receives the cube dimensions.
		bool wave = s.GameMode == 6;
		int width = wave ? 8 : (s.Mini ? 8 : 15);
		int height = wave ? 8 : (s.Mini ? 7 : 15);
		int hitboxOffsetY = (s.Mini ? (16 - height >> 1) : 0);
		int currplayerWorldX_px = s.X_fixed >> 8;
		int playerX_px = currplayerWorldX_px + playerXBias;
		int currplayerScreenX_px = playerX_px - GetScrollX_px(in s);
		int screenY_fixed = s.Y_fixed - s.CameraY_fixed;
		SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=SpiderScanUp.in X={playerX_px} Ywld={s.Y_fixed >> 8} Yscr={screenY_fixed >> 8} w={width} h={height} offY={hitboxOffsetY} camY={s.CameraY_fixed >> 8}");
		for (int i = 0; i < 200; i++)
		{
			screenY_fixed = ((((screenY_fixed >> 8) - 8) << 8) | (screenY_fixed & 0xFF));
			s.Y_fixed = s.CameraY_fixed + screenY_fixed;
			ApplyNesProcessYScrollDuringSpiderWait(ref s);
			screenY_fixed = s.Y_fixed - s.CameraY_fixed;
			int screenY_px = screenY_fixed >> 8;
			int worldY_px = s.Y_fixed >> 8;
			if (screenY_px <= 0x07)
			{
				SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=SpiderScanUp.top i={i} Yscr={screenY_px}");
				return true;
			}
			UpdateSpiderScanUpEjectSideEffect(ref s, playerX_px,
				worldY_px + hitboxOffsetY, width, currplayerScreenX_px);
			var (flag, num2) = BgCollU_Spider(playerX_px, worldY_px + hitboxOffsetY,
				width, height, currplayerScreenX_px);
			SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=SpiderScanUp.step i={i} Ywld={worldY_px} Yscr={screenY_px} probeY={worldY_px + hitboxOffsetY} hit={(flag ? 1 : 0)} ej={num2}");
			if (flag)
			{
				s.EjectU = unchecked((byte)(-num2));
				screenY_fixed = ((((screenY_fixed >> 8) + num2) << 8) | (screenY_fixed & 0xFF));
				s.Y_fixed = s.CameraY_fixed + screenY_fixed;
				break;
			}
		}
		SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=SpiderScanUp.out Ywld={s.Y_fixed >> 8}");
		return false;
	}

	private bool SpiderScanDown(ref SimState s, int playerXBias = 0)
	{
		// See SpiderScanUp: sprite_collide uses the wave box only for wave.
		bool wave = s.GameMode == 6;
		int width = wave ? 8 : (s.Mini ? 8 : 15);
		int num = wave ? 8 : (s.Mini ? 7 : 15);
		int num2 = (s.Mini ? (16 - num >> 1) : 0);
		int currplayerWorldX_px = s.X_fixed >> 8;
		int playerX_px = currplayerWorldX_px + playerXBias;
		int currplayerScreenX_px = playerX_px - GetScrollX_px(in s);
		int screenY_fixed = s.Y_fixed - s.CameraY_fixed;
		SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=SpiderScanDown.in X={playerX_px} Ywld={s.Y_fixed >> 8} Yscr={screenY_fixed >> 8} w={width} h={num} offY={num2} camY={s.CameraY_fixed >> 8}");
		for (int i = 0; i < 200; i++)
		{
			screenY_fixed = ((((screenY_fixed >> 8) + 8) << 8) | (screenY_fixed & 0xFF));
			s.Y_fixed = s.CameraY_fixed + screenY_fixed;
			ApplyNesProcessYScrollDuringSpiderWait(ref s);
			screenY_fixed = s.Y_fixed - s.CameraY_fixed;
			int screenY_px = screenY_fixed >> 8;
			int worldY_px = s.Y_fixed >> 8;
			if (screenY_px >= 0xF8)
			{
				SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=SpiderScanDown.bottom i={i} Yscr={screenY_px}");
				return true;
			}
			UpdateSpiderScanDownEjectSideEffect(ref s, playerX_px,
				worldY_px + num2, width, num, currplayerScreenX_px);
			var (flag, num5) = BgCollD_Spider(playerX_px, worldY_px + num2,
				width, num, currplayerScreenX_px);
			SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=SpiderScanDown.step i={i} Ywld={worldY_px} Yscr={screenY_px} probeY={worldY_px + num2 + num} hit={(flag ? 1 : 0)} ej={num5}");
			if (flag)
			{
				s.EjectD = unchecked((byte)num5);
				screenY_fixed = ((((screenY_fixed >> 8) - num5) << 8) | (screenY_fixed & 0xFF));
				s.Y_fixed = s.CameraY_fixed + screenY_fixed;
				break;
			}
		}
		SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=SpiderScanDown.out Ywld={s.Y_fixed >> 8}");
		return false;
	}

	private void UpdateSpiderScanUpEjectSideEffect(ref SimState s, int playerX_px,
		int playerY_px, int width, int currplayerScreenX_px)
	{
		int tileRow = playerY_px / 16 + _collisionMap.GroundRowsToReserve;
		if (tileRow < 0 || tileRow >= _collisionMap.MapHeight)
			return;

		int[] probeXs = { playerX_px + 3, playerX_px + width - 3 };
		foreach (int probeX in probeXs)
		{
			int tileCol = probeX / 16;
			if (tileCol < 0 || tileCol >= _collisionMap.MapWidth)
				continue;
			int tileIndex = tileRow * _collisionMap.MapWidth + tileCol;
			MetatileCollision collision = MetatileCollisionTable.GetCollision(
				(byte)SharedPhysics.MapTileForCollision(_collisionMap.Tiles[tileIndex]));
			if (collision == MetatileCollision.COL_NONE)
				continue;

			// bg_coll_return_U writes eject_U for every non-empty collision ID,
			// even when a half-block/death/slope check ultimately returns false.
			bool fullSolid = collision == MetatileCollision.COL_NO_SIDE ||
				collision == MetatileCollision.COL_FLOOR_CEIL ||
				(collision == MetatileCollision.COL_ALL && currplayerScreenX_px >= 0x10);
			int tmp8 = (playerY_px + _nesCoordOffset) & 0x0F;
			s.EjectU = (byte)((fullSolid ? 0xF0 : 0xF8) | tmp8);
			if (IsSolidForSpider(collision, currplayerScreenX_px, probeX, playerY_px))
				return;
		}
	}

	private void UpdateSpiderScanDownEjectSideEffect(ref SimState s, int playerX_px,
		int playerY_px, int width, int height, int currplayerScreenX_px)
	{
		int probeY = playerY_px + height;
		int tileRow = probeY / 16 + _collisionMap.GroundRowsToReserve;
		if (tileRow < 0 || tileRow >= _collisionMap.MapHeight)
			return;

		int[] probeXs = { playerX_px + 3, playerX_px + width - 3 };
		foreach (int probeX in probeXs)
		{
			int tileCol = probeX / 16;
			if (tileCol < 0 || tileCol >= _collisionMap.MapWidth)
				continue;
			int tileIndex = tileRow * _collisionMap.MapWidth + tileCol;
			MetatileCollision collision = MetatileCollisionTable.GetCollision(
				(byte)SharedPhysics.MapTileForCollision(_collisionMap.Tiles[tileIndex]));
			if (collision == MetatileCollision.COL_NONE)
				continue;

			// bg_coll_return_D likewise publishes tmp8 before returning its boolean.
			s.EjectD = (byte)((probeY + _nesCoordOffset) & 0x0F);
			if (IsSolidForSpider(collision, currplayerScreenX_px, probeX, probeY))
				return;
		}
	}

	private (bool hit, int ejectAmount) BgCollD_Spider(int playerX_px, int playerY_px, int width,
		int height, int currplayerScreenX_px, bool useEjectProbes = false)
	{
		int num = playerY_px + height;
		int num2 = num / 16 + _collisionMap.GroundRowsToReserve;
		if (num2 >= _collisionMap.MapHeight)
		{
			// The streamed NES collision map exposes its reserved bottom boundary as
			// terrain. Heliopolis' spider_down_wait lands on that boundary one row
			// beyond the TMX payload; treating it as empty runs the scan to $F8 and
			// falsely sets the spider death bit.
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
			if (IsSolidForSpider(collision, currplayerScreenX_px, array2[i], num))
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

	private (bool hit, int ejectAmount) BgCollU_Spider(int playerX_px, int playerY_px, int width,
		int height, int currplayerScreenX_px, bool useEjectProbes = false)
	{
		// NES bg_coll_U's solid-top path probes Generic.y + 1. spider_eject
		// supplies the three edge/center probes but still receives that +1 Y
		// bias; using Generic.y directly grounds inverted spider one frame early.
		int collisionProbeY = playerY_px + (useEjectProbes ? 1 : 0);
		int num = collisionProbeY / 16 + _collisionMap.GroundRowsToReserve;
		if (num < 0)
		{
			// Mirror the reserved top boundary used by the NES collision map.
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
				if (IsSolidForSpider(collision, currplayerScreenX_px, array2[i], collisionProbeY))
				{
					if (!useEjectProbes)
					{
						return (hit: true, ejectAmount: SharedPhysics.NesSpiderScanUpEject(
							collision, playerY_px, _nesCoordOffset));
					}
					int item = SharedPhysics.GetCollisionBounds(collision).bottom;
					int num4 = (num - _collisionMap.GroundRowsToReserve) * 16 + item;
					return (hit: true, ejectAmount: num4 - playerY_px);
				}
			}
		}
		return (hit: false, ejectAmount: 0);
	}

	private bool CheckEjectDeathSideEffect(int playerX_px, int playerY_px, int width, int height, bool isBottom)
	{
		// Ordinary bg_coll_U probes Generic.y + 1 (wave/snake are the only
		// zero-bias modes, and they never call spider_eject).  The death-tile
		// routine runs even though a death tile does not count as solid eject.
		// Using Generic.y here misses COL_DEATH_TOP exactly at a tile boundary.
		int probeY = isBottom ? (playerY_px + height) : (playerY_px + 1);
		int tileRow = probeY / 16 + _collisionMap.GroundRowsToReserve;
		if (tileRow < 0 || tileRow >= _collisionMap.MapHeight) return false;

		int localY = probeY & 0x0F;
		int[] probeXs = { playerX_px, playerX_px + (width >> 1), playerX_px + width };

		foreach (int probeX in probeXs)
		{
			int tileCol = probeX / 16;
			if (tileCol < 0 || tileCol >= _collisionMap.MapWidth) continue;

			int tileIdx = tileRow * _collisionMap.MapWidth + tileCol;
			if (tileIdx < 0 || tileIdx >= _collisionMap.Tiles.Length) continue;

			var collision = MetatileCollisionTable.GetCollision(
				(byte)SharedPhysics.MapTileForCollision(_collisionMap.Tiles[tileIdx]));

			int localX = probeX & 0x0F;
			if (collision == MetatileCollision.COL_DEATH_BOTTOM && localY > 0x0A && localX >= 0x05 && localX <= 0x07)
				return true;
			if (collision == MetatileCollision.COL_DEATH_TOP && localY < 0x06 && localX >= 0x05 && localX <= 0x07)
				return true;
		}

		return false;
	}

	// Replicates NES bg_coll_return_D/U chain: bg_coll_U_D_checks || bg_coll_mini_blocks || bg_coll_top_bottom_slabs
	private static bool IsSolidForSpider(MetatileCollision col, int currplayerScreenX,
		int probeX, int probeY)
	{
		if (col == MetatileCollision.COL_NONE || SharedPhysics.IsDeathCollision(col) || SharedPhysics.IsSlopeTile(col))
			return false;

		// bg_coll_U_D_checks. COL_ALL has an NES left-edge exception based on
		// high_byte(currplayer_x), not on the world-space probe coordinate.
		switch (col)
		{
			case MetatileCollision.COL_NO_SIDE:
			case MetatileCollision.COL_FLOOR_CEIL:
				return true;
			case MetatileCollision.COL_ALL:
				return currplayerScreenX >= 0x10;
		}

		int localX = probeX & 0x0F;
		int localY = probeY & 0x0F;

		// bg_coll_mini_blocks returns immediately in the same left-edge zone
		// (except COL_FLOOR_CEIL, already handled above). Slabs are evaluated
		// afterward and intentionally remain active there.
		if (currplayerScreenX >= 0x10)
		{
			switch (col)
			{
				case MetatileCollision.COL_UP_LEFT:
					return localY < 8 && localX < 8;
				case MetatileCollision.COL_UP_RIGHT:
					return localY < 8 && localX >= 8;
				case MetatileCollision.COL_DOWN_LEFT:
				case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
					return localY >= 8 && localX < 8;
				case MetatileCollision.COL_DOWN_RIGHT:
				case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
					return localY >= 8 && localX >= 8;
				case MetatileCollision.COL_BOTTOM_LEFT_SPIKE:
				case MetatileCollision.COL_BOTTOM_RIGHT_SPIKE:
				case MetatileCollision.COL_BOTTOM_CENTER_SPIKE:
				case MetatileCollision.COL_BOTTOM_SPIKES:
					return localY >= 8;
				case MetatileCollision.COL_TOP_CENTER_SPIKE:
					return localY < 8;
				case MetatileCollision.COL_LEFT:
					return localX < 8;
				case MetatileCollision.COL_RIGHT:
					return localX >= 8;
				case MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT:
					return (localX < 8 && localY < 8) || (localX >= 8 && localY >= 8);
				case MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT:
					return (localX < 8 && localY >= 8) || (localX >= 8 && localY < 8);
				case MetatileCollision.COL_TOP_RIGHT_STAIRS:
					return !(localY >= 8 && localX < 8);
				case MetatileCollision.COL_TOP_LEFT_STAIRS:
					return !(localY >= 8 && localX >= 8);
				case MetatileCollision.COL_BOTTOM_RIGHT_STAIRS:
					return !(localY < 8 && localX < 8);
				case MetatileCollision.COL_BOTTOM_LEFT_STAIRS:
					return !(localY < 8 && localX >= 8);
			}
		}

		// bg_coll_top_bottom_slabs
		switch (col)
		{
			case MetatileCollision.COL_BOTTOM:
				return localY >= 8;
			case MetatileCollision.COL_TOP:
				return localY < 8;
		}

		// Not matched by any NES check in the spider chain → not solid
		return false;
	}

	private void SwingGravityStep(ref SimState s)
	{
		int num = (s.Mini ? 56 : 50);
		// Current NES 60 fps SWING_MAX_FALLSPEED table (indices 4 and 6).
		int num2 = s.Mini ? 0x352 : 0x300;
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
		int num = ((holding && flag) ? ShipGravityHoldFall(s.Mini) : (holding ? ShipGravityBase(s.Mini) : (flag ? ShipGravity(s.Mini) : ShipGravityAfterHold(s.Mini)))) * gravMul;
		if (s.GravFlipped ^ holding)
		{
			num = -num;
		}
		int tmpfallspeed = 17475;
		int clampMaxY = Math.Max(0, mapHeight * 16 - 16) << 8;
		int _dbgVelBefore = s.VelY_fixed;
		int _dbgPosBefore = s.Y_fixed;
		SharedPhysics.CommonGravityRoutine(ref s.VelY_fixed, ref s.Y_fixed, num, tmpfallspeed, s.GravFlipped ? 255 : 0, s.Dashing, s.GravityMod, 1.0, isFullSpeed: true, s.VelX_fixed, clampMaxY);
		SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=ShipGravDbg hold={holding} fall={flag} gMul={gravMul} gFlip={s.GravFlipped} mini={s.Mini} accel={num} dash={s.Dashing} gMod={s.GravityMod} VelBefore={_dbgVelBefore} VelAfter={s.VelY_fixed} PosBefore=0x{_dbgPosBefore:X} PosAfter=0x{s.Y_fixed:X}");
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
		SharedPhysics.EjectResult ejectResult = SharedPhysics.ShipUfoEject(in _collisionMap, s.X_fixed, s.Y_fixed, s.VelY_fixed, s.VelX_fixed, s.GravFlipped, s.Mini, s.GameMode, input, s.SlopeWasOnCounter, s.SlopeFrames, s.SlopeType, s.SlopeJumpHigher, s.LastSlopeType, s.CameraY_fixed, updateSlopeCounters: false);
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
		return SharedPhysics.VerifyGroundSupport(in _collisionMap, s.X_fixed >> 8, NesPlayerY_px(s.Y_fixed, s.CameraY_fixed), s.GameMode, s.Mini, s.GravFlipped);
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

	private bool ProcessSpritesNesOrder(ref SimState s, int currentX_px, bool inputHeld, bool pressEdge,
		bool queuedPressAtFrameStart, out bool orbHitThisFrame, out bool skullDeathThisFrame)
	{
		orbHitThisFrame = false;
		skullDeathThisFrame = false;
		_dualActivatedThisProcessSprites = false;
		s.GravFlippedAtFrameStart = s.GravFlipped;
		s.OrbUseFrameStartGravitySign = false;
		int cameraY_px = s.CameraY_fixed >> 8;
		// NES sprite collision uses high_byte(currplayer_y), including its fixed
		// screen-space fractional bias relative to PF's camera-relative Y.
		int playerY_px = SharedPhysics.NesPlayerScreenY_px(s.Y_fixed, s.CameraY_fixed);
		// sprite_collide uses WAVE_WIDTH/HEIGHT only for GAMEMODE_WAVE.  Snake
		// uses the cube sprite box even though x_movement later uses the wave box
		// for background collision.
		bool isWave = s.GameMode == 6;
		int hitboxW = isWave ? 8 : GetHitboxW(s.Mini);
		int hitboxH = isWave ? 8 : GetHitboxH(s.Mini);
		int hitboxOffY = isWave ? 4 : GetHitboxOffsetY(s.GameMode, s.Mini, s.GravFlipped);
		int plTop = playerY_px + hitboxOffY;
		int plBot = plTop + hitboxH;
		// In the P2 (player 1) sub-step, use the post-P1-movement scroll captured before
		// this pass. NES scrolled the screen (decrementing player1_x) during player 0's
		// frame, so player 1's collision box lags by the scroll delta. Using P2's own
		// pre-move X here would position the box ~tmp1 px too far right and trip
		// boundary-straddling sprites (e.g. the single portal) one frame early.
		int scrollX_px = (_dualP2Guard && _dualP2ScrollXValid)
			? _dualP2ScrollX_px
			: GetScrollX_px(in s);
		int plLeft = currentX_px - scrollX_px + 1;
		int plRight = plLeft + hitboxW;
		bool dualActive = s.DualActive;
		int miniCenterOffY = SharedPhysics.GetMiniCenterOffsetY(s.Mini);
		// sprite_collide centers the 8px wave box inside the 16px player cell.
		// Using only the mini-cube offset made wave orbs/pads collide four pixels
		// too early vertically (Off to Mars' spider orb is the edge case).
		int orbPlTop = playerY_px + (s.GameMode == 6 ? 4 : miniCenterOffY);
		int orbPlBot = orbPlTop + hitboxH;

		for (int slot = 0; slot < 16; slot++)
		{
			int sprIdx = s.NesSlots[slot];
			// Per-slot iteration trace: lets us see exactly which slots PF visits,
			// which sids they hold, and whether they're active. Logs EMPTY/DEAD/INACTIVE
			// rows too so a missing handler fire is always attributable.
			int _diagSid = (sprIdx >= 0) ? (_nesSpritesArr[sprIdx].SpriteId & 0xFF) : -1;
			int _diagWorldX = (sprIdx >= 0) ? _nesSpriteWorldX[sprIdx] : 0;
			SharedPhysics.FullTraceLog?.Invoke(
				$"cur=0 gm={s.GameMode} tag=NesSlotIter slot={slot} sprIdx={sprIdx} sid={_diagSid} active={(s.NesSlotActive[slot] ? 1 : 0)} dead={(s.NesSlotDead[slot] ? 1 : 0)} relX={s.NesSlotRealX[slot]} relY={s.NesSlotRealY[slot]} wY={s.NesSlotWorldY[slot]} wX={_diagWorldX}");
			if (sprIdx < 0 || s.NesSlotDead[slot] || !s.NesSlotActive[slot]) continue;
			ref SpriteEntry spr = ref _nesSpritesArr[sprIdx];
			int processKey = spr.ProcessKey;
			int sid = spr.SpriteId & 0xFF;
			int nesH = (sid < _nesSprH.Length) ? _nesSprH[sid] : 0;

			if (nesH == NES_DECO) continue;
			if (nesH == NES_COLR)
			{
				if (SharedPhysics.IsBackgroundColorTrigger(sid))
				{
					s.ReplayBackgroundColorTriggerIndex = sprIdx;
					s.ReplayBackgroundColorTriggerSpriteId = sid;
				}
				else if (SharedPhysics.IsGroundColorTrigger(sid))
				{
					s.ReplayGroundColorTriggerIndex = sprIdx;
					s.ReplayGroundColorTriggerSpriteId = sid;
				}
				s.NesSlotDead[slot] = true;
				continue;
			}
			if (nesH == NES_OUTL)
			{
				if (SharedPhysics.IsObjectColorTrigger(sid))
				{
					s.ReplayObjectColorTriggerIndex = sprIdx;
					s.ReplayObjectColorTriggerSpriteId = sid;
				}
				s.NesSlotDead[slot] = true;
				continue;
			}
			if (nesH == 0) continue;

			if (nesH == NES_SPBH)
			{
				// The ROM stores coin animation in the live slot Y byte, with
				// one shared timer/speed pair per coin number. Both regular and
				// already-collected coin records execute this animation.
				int coinKind = GetNesCoinKind(sid);
				if (coinKind >= 0 && s.CoinTimer[coinKind] != 0)
				{
					int yLow = ((s.NesSlotWorldY[slot] & 0xFF) -
						((s.CoinSpeed[coinKind] >> 8) & 0xFF)) & 0xFF;
					s.NesSlotWorldY[slot] =
						(s.NesSlotWorldY[slot] & ~0xFF) | yLow;
					s.CoinSpeed[coinKind] =
						(s.CoinSpeed[coinKind] - 0x40) & 0xFFFF;
					s.CoinTimer[coinKind] =
						(s.CoinTimer[coinKind] + 1) & 0xFF;
					if (s.CoinTimer[coinKind] == 40)
					{
						s.NesSlotDead[slot] = true;
						s.CoinAnimating = false;
						continue;
					}
				}

				// Exit records continuously publish the shared teleport_output
				// byte in slot order. Entrances consume whatever value has been
				// published by an earlier active exit (possibly a prior frame).
				if (IsTeleportPortalExit(sid))
				{
					int relY = s.NesSlotRealY[slot];
					s.TeleportOutputY_px = sid == 0x5A ? relY : relY + 16;
					continue;
				}

				// level_end is unconditional once its slot is active.
				if (IsEndLevel(sid))
				{
					ApplyPortalSprite(ref s, sid);
					s.ProcessedSprites.Add(processKey);
					return true;
				}

				// Coins are the SPBH records that continue into ordinary
				// collision handling. All other recognized triggers below run
				// immediately and kill their slot, exactly like the ROM macro.
				bool persistentCoin = sid == 0x07 || sid == 0x1A || sid == 0x1B ||
					(sid >= 0x1C && sid <= 0x1E);
				if (!persistentCoin)
				{
					switch (sid)
					{
						case 0x70: s.GravityMod = 1.0 / 3.0; break;
						case 0x71: s.GravityMod = 0.5; break;
						case 0x72: s.GravityMod = 2.0 / 3.0; break;
						case 0x73: s.GravityMod = 2.0; break;
						case 0x74: s.GravityMod = 1.0; break;
						case 0x8E: s.WrapMode = true; break;
						case 0x9E: s.WrapMode = false; break;
						case 0xDD: s.NoCamLockForced = true; break;
						case 0xED: s.NoCamLockForced = false; break;
						case 0xDE:
							s.TargetXScrollStop_fixed =
								(s.NesSlotRealY[slot] & 0xF0) << 8;
							break;
						case 0x6F: s.PlayerInvisible = true; break;
						case 0x7F: s.PlayerInvisible = false; break;
						case 0x7D:
						case 0xDF:
						case 0xEE:
						case 0xEF:
						case 0xF0:
						case 0xF1:
							break;
						case 0xF2: s.ForcedTrails = 2; break;
						case 0xF3: s.ForcedTrails = 0; break;
						case 0xF4:
							s.SlowMode = true;
							break;
						case 0xF5:
							s.SlowMode = false;
							break;
						default:
							// Unrecognized SPBH records remain resident and
							// return zero collision height.
							continue;
					}
					s.NesSlotDead[slot] = true;
					continue;
				}
			}

			int sprWidth = sid < sprite_widths.Length ? sprite_widths[sid] : 0;
			int sprHeight = sid < sprite_heights.Length ? sprite_heights[sid] : 0;
			int sprLeft = NesSaturatingOffset(s.NesSlotRealX[slot],
				sid < sprite_x_offset.Length ? sprite_x_offset[sid] : 0);
			int sprTop = NesSaturatingOffset(s.NesSlotRealY[slot],
				sid < sprite_y_offset.Length ? sprite_y_offset[sid] : 0);
			bool xOv = NesAxisOverlaps(plLeft, hitboxW, sprLeft, sprWidth);

			// Full coins are SPBH records: sprite_load_special_behavior() returns
			// a raw 16x16 collision box after running the mutable slot animation.
			// Mini coins are normal table-sized sprites (8x8,+4,+4 on NES), then
			// spcl_minicoi kills the live slot immediately on collision.
			int nesCoinKind = GetNesCoinKind(sid);
			if (nesCoinKind >= 0)
			{
				int coinLeft = s.NesSlotRealX[slot];
				int coinTop = s.NesSlotRealY[slot];
				bool coinXOv = NesAxisOverlaps(plLeft, hitboxW, coinLeft, 0x10);
				bool coinYOv = NesAxisOverlaps(plTop, hitboxH, coinTop, 0x10);
				if (coinXOv && coinYOv && !s.ProcessedSprites.Contains(processKey))
				{
					s.ProcessedSprites.Add(processKey);
					if (s.CoinTimer[nesCoinKind] == 0)
					{
						s.CoinTimer[nesCoinKind] = 1;
						s.CoinSpeed[nesCoinKind] = 0x0200;
						s.CoinAnimating = true;
					}
				}
				continue;
			}
			if (IsMiniCoinSprite(sid))
			{
				bool coinYOv = NesAxisOverlaps(plTop, hitboxH, sprTop, sprHeight);
				if (xOv && coinYOv && !s.ProcessedSprites.Contains(processKey))
				{
					s.ProcessedSprites.Add(processKey);
					s.NesSlotDead[slot] = true;
				}
				continue;
			}

			// Speed portals
			if (IsSpeedPortal(sid))
			{
				bool yOv = NesAxisOverlaps(plTop, hitboxH, sprTop, sprHeight);
				if (xOv && yOv)
				{
					int spd = SpriteIdToSpeedFixed(sid);
					if (spd > 0)
					{
						SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm=-1 tag=NesSlotSpeed sid={sid} idx={spr.Index} slot={slot} oldGlobalSpeed={s.GlobalSpeed_fixed} newGlobalSpeed={spd}");
						s.GlobalSpeed_fixed = spd;
					}
				}
				// NES spcl_spd_* only writes global speed. It never increments
				// activesprites_activated, so overlapping speed portals re-fire
				// every frame and the last colliding active slot wins.
				continue;
			}

			bool processedSprite = s.ProcessedSprites.Contains(processKey);
			// Most activated handlers remain eligible for the other player during
			// dual and in forced-platformer play. The normal green pad (0x65) is
			// globally single-use, however; only GREEN_ORB_MULTI (0x7C) is
			// intentionally reusable.
			bool nesDualAllowsActivatedReplay = s.DualActive && !IsGreenPad(sid);
			bool nesPlatformerAllowsActivatedReplay = ForcePlatformer && !IsGreenPad(sid);
			// spcl_cube/spcl_robot never set activesprites_activated. Their handlers
			// therefore remain live on every overlap and continuously refresh the
			// exit-portal camera timer.
			bool persistentCubeRobotPortal = sid == 0 || sid == 4;
			if (processedSprite && !nesDualAllowsActivatedReplay &&
				!nesPlatformerAllowsActivatedReplay && !persistentCubeRobotPortal) continue;

			// Dual/single portals
			if (sid == 34 || sid == 35)
			{
				bool yOv = NesAxisOverlaps(plTop, hitboxH, sprTop, sprHeight);
				SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=DualSinglePortal f={_frameCounter} sid={sid} slot={slot} p2={(_dualP2Guard ? 1 : 0)} gravF={(s.GravFlipped ? 1 : 0)} dualActive={(s.DualActive ? 1 : 0)} plLeft={plLeft} plTop={plTop} hbW={hitboxW} hbH={hitboxH} sprLeft={sprLeft} sprTop={sprTop} sprW={sprWidth} sprH={sprHeight} xOv={(xOv ? 1 : 0)} yOv={(yOv ? 1 : 0)}");
				if (xOv && yOv)
				{
					// spcl_dual_pt gates on this portal's activation byte, not on
					// the global dual flag. A second dual portal while already dual
					// must reset P2 and the camera target exactly once.
					if (sid == 34 && !processedSprite)
					{
						s.DualActive = true;
						_dualActivatedThisProcessSprites = true;
						s.TargetCameraY_fixed = NesNtCameraTarget_fixed(spr.AnchorY_px - 8);
						s.P2_Y_fixed = s.Y_fixed;
						s.P2_VelY_fixed = -s.VelY_fixed;
						s.P2_GravFlipped = !s.GravFlipped;
						s.P2_GravMul = -s.GravMul;
						s.P2_Mini = s.Mini;
						// NES spcl_dual_pt preserves dormant P2 X velocity,
						// slope state, and mode-specific per-player flags.
						s.P2_PrevInputHeld = s.PrevInputHeld;
						s.P2_PendingOrbIndex = -1;
						s.P2_PendingOrbSpriteId = -1;
						s.P2_PendingOrbExtra2Index = -1;
						s.P2_PendingOrbExtra2SpriteId = -1;
						s.P2_PendingOrbExtra1Index = -1;
						s.P2_PendingOrbExtra1SpriteId = -1;
					}
					else if (sid == 35 && !processedSprite)
					{
						if (_dualP2Guard)
						{
							s.P2_SingleExitCaptured = true;
							s.P2_SingleExitY_fixed = s.Y_fixed;
							s.P2_SingleExitVelY_fixed = s.VelY_fixed;
							s.P2_SingleExitGravFlipped = s.GravFlipped;
							s.P2_SingleExitGravMul = s.GravMul;
						}
						s.DualActive = false;
						s.ExitPortalTimer = 10;
					}
					s.ProcessedSprites.Add(processKey);
				}
				continue;
			}

			// NoCamLock triggers (SPBH, X-overlap only)
			if (sid == 221 || sid == 237)
			{
				if (xOv)
				{
					s.NoCamLockForced = sid == 221;
					s.ProcessedSprites.Add(processKey);
				}
				continue;
			}

			// Wrap mode triggers (SPBH)
			if (sid == 142 || sid == 158)
			{
				bool yOv = NesAxisOverlaps(plTop, hitboxH, sprTop, sprHeight);
				if (xOv && yOv)
				{
					s.WrapMode = sid == 142;
					s.ProcessedSprites.Add(processKey);
				}
				continue;
			}

			// RainbowMax / random mode
			if (sid == 100 || sid == 126)
			{
				if (!_dualP2Guard)
				{
					bool yOv = NesAxisOverlaps(plTop, hitboxH, sprTop, sprHeight);
					if (xOv && yOv)
					{
						ApplyRainbowPortal(ref s, sid, spr.AnchorX_px,
							spr.AnchorY_px - 8);
						s.ProcessedSprites.Add(processKey);
					}
				}
				continue;
			}

			// Gravity mod triggers (0x5F-0x63)
			if (sid >= 95 && sid <= 99)
			{
				bool yOv = NesAxisOverlaps(plTop, hitboxH, sprTop, sprHeight);
				if (xOv && yOv)
				{
					double gmod = 1.0;
					switch (sid) { case 95: gmod = 1.0 / 3.0; break; case 96: gmod = 0.5; break; case 97: gmod = 2.0 / 3.0; break; case 98: gmod = 2.0; break; case 99: gmod = 1.0; break; }
					s.GravityMod = gmod;
					s.ProcessedSprites.Add(processKey);
				}
				continue;
			}

			// Camera visual triggers — skip
			if (sid >= 112 && sid <= 116) continue;

			// End level
			if (IsEndLevel(sid))
			{
				int camTop = s.CameraY_fixed >> 8;
				int camBot = camTop + 240;
				int sprY = spr.AnchorY_px - 8;
				bool camVisible = sprY + 16 >= camTop && sprY < camBot;
				if (xOv && camVisible)
				{
					ApplyPortalSprite(ref s, sid);
					s.ProcessedSprites.Add(processKey);
					return true;
				}
				continue;
			}

			// Gamemode portals
			if (IsGameModePortal(sid))
			{
				// NES runs sprite_collide for both players and gamemode is global.
				// If only P2 overlaps a portal, its new mode applies immediately to
				// P2 physics and remains active when control returns to P1.
				bool yOv = NesAxisOverlaps(plTop, hitboxH, sprTop, sprHeight);
				if (xOv && yOv)
				{
					// spcl_cube/spcl_robot write exitPortalTimer=10 on every
					// collision, even when the player is already in that mode and
					// the portal was activated on a prior frame. Use the live NES
					// slot overlap here; a post-movement world-space approximation
					// drops the final overlap frame and advances the camera ramp.
					if (sid == 0 || sid == 4)
						s.ExitPortalTimer = 10;
					bool applied = ApplyPortalSprite(ref s, sid, spr.AnchorX_px);
					if (applied)
					{
						// Unlike all other game-mode portals, the NES cube and robot
						// handlers deliberately remain unactivated and can run again.
						if (sid != 0 && sid != 4)
							s.ProcessedSprites.Add(processKey);
						// gamemode_stuff tests the live dual flag. A dual portal in an
						// earlier slot therefore suppresses this later portal's target
						// write during the same sprite_collide pass.
						if (s.GameMode != 0 && s.GameMode != 4 && s.GameMode != 8 && s.GameMode != 9 && !s.DualActive)
							s.TargetCameraY_fixed = NesNtCameraTarget_fixed(spr.AnchorY_px - 8);
						RecordRainbowExit(ref s, sid, spr.AnchorX_px, processKey);
					}
				}
				continue;
			}

			// Gravity portals
			if (IsGravityPortal(sid))
			{
				// NES sprite_collide() uses the same inclusive edge-touch
				// check_collision() path for gravity portals as for normal
				// portals.  Requiring positive X overlap delays edge contacts
				// by one frame (Fingerdash gravity-up portal at sim 1231).
				bool xOvGravity = NesAxisOverlaps(plLeft, hitboxW, sprLeft, sprWidth);
				bool yOv = NesAxisOverlaps(plTop, hitboxH, sprTop, sprHeight);
				if (xOvGravity || yOv)
				{
					SharedPhysics.FullTraceLog?.Invoke(
						$"cur=0 gm={s.GameMode} tag=NesSlotGravCheck sid={sid} idx={spr.Index} slot={slot} pl=({plLeft},{plTop},{hitboxW}x{hitboxH}) spr=({sprLeft},{sprTop},{sprWidth}x{sprHeight}) xOv={(xOvGravity ? 1 : 0)} yOv={(yOv ? 1 : 0)} cam={s.CameraY_fixed >> 8} yFixed=0x{s.Y_fixed:X}");
				}
				if (xOvGravity && yOv)
				{
					int velocityBeforePortal = s.VelY_fixed;
					bool gravityBeforePortal = s.GravFlipped;
					SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=NesSlotGravPortal sid={sid} idx={spr.Index} slot={slot} gravBefore={s.GravFlipped} velBefore={s.VelY_fixed}");
					if (ApplyPortalSprite(ref s, sid))
					{
						// spcl_gvity_portal_common halves currplayer_vel_y in this
						// exact slot, before a later gamemode portal can run. Keep the
						// conversion explicit here so no portal-order path can retain
						// the pre-flip ship velocity (Hexagon Force, sim 2471).
						if (s.GravFlipped != gravityBeforePortal)
							s.VelY_fixed = NesGravityPortalHalfVelocity(velocityBeforePortal);
						s.ProcessedSprites.Add(processKey);
					}
				}
				continue;
			}

			// Mini/Growth portals
			if (IsMiniGrowthPortal(sid))
			{
				bool yOv = NesAxisOverlaps(plTop, hitboxH, sprTop, sprHeight);
				if (xOv && yOv)
				{
					if (ApplyPortalSprite(ref s, sid))
						s.ProcessedSprites.Add(processKey);
				}
				continue;
			}

			// Pads
			if (IsYellowPad(sid) || IsPinkPad(sid) || IsRedPad(sid) || IsBluePad(sid) || IsGreenPad(sid))
			{
				bool yOv = NesAxisOverlaps(plTop, hitboxH, sprTop, sprHeight);
				if (xOv && yOv)
				{
					bool shouldApply = true;
					if (IsBluePad(sid))
					{
						// spcl_gvdn_pd/spcl_gvup_pd clear the live slope
						// globals before testing whether gravity already matches.
						// A no-op blue pad contact therefore still cancels a
						// pending slope frame (Sunshine's inverted mini robot).
						ClearSlopeStuff(ref s);
						bool isGravDown = (sid == 13 || sid == 253);
						if (isGravDown && s.GravFlipped) shouldApply = false;
						if (!isGravDown && !s.GravFlipped) shouldApply = false;
					}
					if (shouldApply)
					{
						ApplyPadSprite(ref s, sid);
						orbHitThisFrame = true;
					}
					// NES increments activesprites_activated for both blue and green
					// pads. The shared lookup suppresses them on later single-player
					// frames; dual mode deliberately permits the other player's pass.
					if (IsBluePad(sid) || IsGreenPad(sid))
						s.ProcessedSprites.Add(processKey);
				}
				continue;
			}

			// Death/skull orb. Unlike ordinary orbs it is never consumed: colliding
			// while activating it only sets cube_data bit $01. The buffered branch
			// uses cube_data bit $02 (AirPressLatch), exactly like spcl_skl_orb.
			if (sid == 0x79)
			{
				bool xOvO = NesAxisOverlaps(plLeft, hitboxW, sprLeft, sprWidth);
				bool yOvO = NesAxisOverlaps(orbPlTop, hitboxH, sprTop, sprHeight);
				if (xOvO && yOvO)
				{
					// The source's explicit mode test includes cube, ball, robot,
					// spider, swing, ninja, pogo, snake, and later modes. Ship, UFO,
					// and wave use only a fresh press.
					bool usesBufferedHold = s.GameMode != 1 && s.GameMode != 3 &&
						s.GameMode != 6 && s.AirPressLatch;
					if (usesBufferedHold ? inputHeld : pressEdge)
					{
						skullDeathThisFrame = true;
						OrbDbg($"NES_SLOT_SKULL_ACT slot={slot} idx={spr.Index} " +
							$"press={pressEdge} hold={inputHeld} latch={s.AirPressLatch} gm={s.GameMode}");
					}
				}
				continue;
			}

			// Spider pads
			if (IsSpiderPad(sid))
			{
				bool xOvO = NesAxisOverlaps(plLeft, hitboxW, sprLeft, sprWidth);
				bool yOvO = NesAxisOverlaps(orbPlTop, hitboxH, sprTop, sprHeight);
				if (xOvO && yOvO)
				{
					bool goUp = sid == 86;
					ApplySpiderTeleport(ref s, goUp);
					// spider_*_wait rewrites Generic.y on every 8-pixel scan
					// step.  sprite_collide does not restore it before visiting
					// the remaining slots, so those slots collide against the
					// teleport destination rather than the frame-start player Y.
					// The final handler eject changes currplayer_y only; recover
					// the Generic.y left by the scan from that persistent eject.
					plTop = (SharedPhysics.NesPlayerScreenY_px(s.Y_fixed, s.CameraY_fixed) +
						(goUp ? s.EjectU : s.EjectD)) & 0xFF;
					plBot = plTop + hitboxH;
					orbPlTop = plTop;
					orbPlBot = orbPlTop + hitboxH;
					orbHitThisFrame = true;
				}
				continue;
			}

			// Orbs (yellow, pink, red, black, blue, green, white, skull + multi-orbs)
			if (IsOrbSprite(sid))
			{
				bool xOvO = NesAxisOverlaps(plLeft, hitboxW, sprLeft, sprWidth);
				bool yOvO = NesAxisOverlaps(orbPlTop, hitboxH, sprTop, sprHeight);
				if (xOvO && yOvO)
				{
					if (SetsNesUfoOrbed(sid))
						s.UfoOrbed = true;
					if (s.Dashing != 0)
						continue;
					if (s.GameMode == 2 && inputHeld && !IsWhiteOrb(sid))
						s.BallFlipCooldown = 1;
					bool isCubeGroup = s.GameMode == 0 || s.GameMode == 2 || s.GameMode == 4 || s.GameMode == 5 || s.GameMode == 7 || s.GameMode == 8 || s.GameMode == 9 || s.GameMode >= 10;
					bool orbGate;
					if (isCubeGroup)
						orbGate = inputHeld && (pressEdge || s.AirPressLatch);
					else
						orbGate = pressEdge;
					bool isMultiOrb = sid == 123 || sid == 124;
					bool suppressActivatedBlueGreen =
						!isMultiOrb && s.ProcessedSprites.Contains(processKey) &&
						(IsBlueOrb(sid) || IsGreenOrb(sid));
					OrbDbg($"NES_SLOT_ORB slot={slot} sid=0x{sid:X2} idx={spr.Index} gate={orbGate} press={pressEdge} hold={inputHeld} orbed={s.Orbed} latch={s.AirPressLatch} cubeGrp={isCubeGroup} gm={s.GameMode}");
					if (orbGate)
					{
						if (_speculativeDepth == 0)
							_hitOrbHistory.Add(spr.Index);
						// sprite_gamemode_main sets orbed for robot before applying
						// every regular orb effect. This suppresses the robot's own
						// press-triggered jump later in the same movement pass.
						if (s.GameMode == 4)
							s.Orbed = true;
						// The NES still dispatches an activated orb during dual.  Only
						// the blue/green handlers guard their gravity/velocity effect
						// with activesprites_activated[index].
						if (!suppressActivatedBlueGreen)
							ApplyOrbSprite(ref s, sid);
						if (IsBlackOrb(sid) && s.GameMode == 5) s.BlackOrbed = true;
						if (!isMultiOrb)
						{
							s.ProcessedSprites.Add(processKey);
							if (!_dualP2Guard && _p1OrbIndicesThisFrame != null)
								_p1OrbIndicesThisFrame.Add(processKey);
						}
						s.AirPressLatch = false;
						orbHitThisFrame = true;
						if (_speculativeDepth == 0) _cubeJumpedThisStep = true;
						OrbDbg($"NES_SLOT_ORB_ACT slot={slot} sid=0x{sid:X2} idx={spr.Index} velY_after=0x{s.VelY_fixed:X} gravF_after={s.GravFlipped}");
					}
				}
				continue;
			}

			// Dash orbs
			if (IsDashOrb(sid))
			{
				bool xOvO = NesAxisOverlaps(plLeft, hitboxW, sprLeft, sprWidth);
				bool yOvO = NesAxisOverlaps(orbPlTop, hitboxH, sprTop, sprHeight);
				if (xOvO && yOvO)
				{
					if (SetsNesUfoOrbed(sid))
						s.UfoOrbed = true;
					if (s.Dashing != 0)
						continue;
					if (s.GameMode == 2 && inputHeld)
						s.BallFlipCooldown = 1;
					bool isCubeGroup = s.GameMode == 0 || s.GameMode == 2 || s.GameMode == 4 || s.GameMode == 5 || s.GameMode == 7 || s.GameMode == 8 || s.GameMode == 9 || s.GameMode >= 10;
					bool orbGate = isCubeGroup ? (inputHeld && (pressEdge || s.AirPressLatch)) : pressEdge;
					if (orbGate)
					{
						if (_speculativeDepth == 0) _hitOrbHistory.Add(spr.Index);
						if (s.GameMode == 4)
							s.Orbed = true;
						ApplyDashOrb(ref s, sid);
						s.ProcessedSprites.Add(processKey);
						if (!_dualP2Guard && _p1OrbIndicesThisFrame != null)
							_p1OrbIndicesThisFrame.Add(processKey);
						s.AirPressLatch = false;
						orbHitThisFrame = true;
						if (_speculativeDepth == 0) _cubeJumpedThisStep = true;
					}
				}
				continue;
			}

			// Spider orbs
			if (IsSpiderOrb(sid))
			{
				bool xOvO = NesAxisOverlaps(plLeft, hitboxW, sprLeft, sprWidth);
				bool yOvO = NesAxisOverlaps(orbPlTop, hitboxH, sprTop, sprHeight);
				if (xOvO && yOvO)
				{
					// NES tests cube_data&2 before x_movement clears it on release.
					// Spider-orb handlers do this directly for every game mode; unlike
					// ordinary orb dispatch, ship/UFO/wave are not press-only here. A
					// queued airborne press therefore remains valid until x_movement
					// consumes it, even when A is no longer newly pressed.
					bool orbGate = pressEdge || queuedPressAtFrameStart || s.AirPressLatch;
					if (orbGate)
					{
						if (_speculativeDepth == 0) _hitOrbHistory.Add(spr.Index);
						bool goUp = sid == 84;
						ApplySpiderTeleport(ref s, goUp);
						// The NES scan leaves Generic.y at its last probe Y for all
						// later sprite slots in this same ordered pass.
						plTop = (SharedPhysics.NesPlayerScreenY_px(s.Y_fixed, s.CameraY_fixed) +
							(goUp ? s.EjectU : s.EjectD)) & 0xFF;
						plBot = plTop + hitboxH;
						orbPlTop = plTop;
						orbPlBot = orbPlTop + hitboxH;
						s.Orbed = true;
						s.ProcessedSprites.Add(processKey);
						if (!_dualP2Guard && _p1OrbIndicesThisFrame != null)
							_p1OrbIndicesThisFrame.Add(processKey);
						s.AirPressLatch = false;
						orbHitThisFrame = true;
						if (_speculativeDepth == 0) _cubeJumpedThisStep = true;
					}
				}
				continue;
			}

			// Teleport portals
			if (IsTeleportPortalEntrance(sid))
			{
				bool yOv = NesAxisOverlaps(plTop, hitboxH, sprTop, sprHeight);
				if (xOv && yOv)
				{
					if (sid == 0x59)
					{
						// spcl_tlpt_sq requires controller press or cube_data bit 1.
						// It clears that bit, zeroes velocity, and sets orbed before
						// writing the shared teleport output byte to currplayer_y.
						bool squareGate = pressEdge || queuedPressAtFrameStart || s.AirPressLatch;
						if (!squareGate)
							continue;
						s.VelY_fixed = 0;
						s.Orbed = true;
						s.AirPressLatch = false;
					}
					ApplyTeleportPortal(ref s, spr, sid, currentX_px);
					// sprite_collide initializes Generic.y once before entering its
					// slot loop. Teleporting currplayer_y does not rebuild Generic,
					// so every later slot this frame still tests the pre-teleport box.
				}
				continue;
			}

			// S-block (dash stop)
			if (sid == 249)
			{
				bool yOv = NesAxisOverlaps(plTop, hitboxH, sprTop, sprHeight);
				if (xOv && yOv && s.Dashing != 0) { s.Dashing = 0; s.Orbed = true; s.VelY_fixed = 0; }
				continue;
			}

			// J/F/H/D blocks
			if (sid == 247)
			{
				bool yOv = NesAxisOverlaps(plTop, hitboxH, sprTop, sprHeight);
				if (xOv && yOv)
				{
					s.JBlocked = true;
					// NES spcl_j_block sets both jblocked and orbed; the orbed
					// latch is what prevents a held cube jump from firing on the
					// same landing frame.
					s.Orbed = true;
					SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=NesSlotJBlock sid={sid} idx={spr.Index} slot={slot} pl=({plLeft},{plTop},{hitboxW}x{hitboxH}) spr=({sprLeft},{sprTop},{sprWidth}x{sprHeight})");
				}
				continue;
			}
			if (sid == 246)
			{
				bool yOv = NesAxisOverlaps(plTop, hitboxH, sprTop, sprHeight);
				if (xOv && yOv)
				{
					s.FBlocked = true;
					SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=NesSlotFBlock sid={sid} idx={spr.Index} slot={slot} pl=({plLeft},{plTop},{hitboxW}x{hitboxH}) spr=({sprLeft},{sprTop},{sprWidth}x{sprHeight})");
				}
				continue;
			}
			if (sid == 248)
			{
				bool yOv = NesAxisOverlaps(plTop, hitboxH, sprTop, sprHeight);
				if (xOv && yOv)
				{
					s.HBlocked = true;
					SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=NesSlotHBlock sid={sid} idx={spr.Index} slot={slot} pl=({plLeft},{plTop},{hitboxW}x{hitboxH}) spr=({sprLeft},{sprTop},{sprWidth}x{sprHeight})");
				}
				continue;
			}
			if (sid == 250) { bool yOv = NesAxisOverlaps(plTop, hitboxH, sprTop, sprHeight); if (xOv && yOv) s.Dblocked = true; continue; }
		}

		return false;
	}

	private bool ProcessSprites(ref SimState s, int currentX_px, bool inputHeld, out bool orbHitThisFrame)
	{
		orbHitThisFrame = false;
		_dualActivatedThisProcessSprites = false;
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
					_dualActivatedThisProcessSprites = true;
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
					s.P2_UfoOrbed = false;
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
				bool skipDueToBlueOrb = false;
				for (int bp = num13; bp < num12; bp++)
				{
					ref SpriteEntry bRef = ref spritesArr[bp];
					if (bRef.HitRight < currentX_px) continue;
					if (bRef.AnchorX_px - 16 > num11 + 16) break;
					if (s.ProcessedSprites.Contains(bRef.Index)) continue;
					if (IsBlueOrb(bRef.SpriteId))
					{
						bool xOv = num11 >= bRef.HitLeft && bRef.HitRight >= num10;
						bool yOv = num9 >= bRef.HitTop && bRef.HitBottom >= num8;
						SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=GravPortalBlueOrbCheck portalSid={spriteId3} portalIdx={reference5.Index} blueOrbIdx={bRef.Index} xOv={xOv} yOv={yOv} blueIdxLower={bRef.Index < reference5.Index}");
						if (xOv && yOv)
						{
							skipDueToBlueOrb = true;
							break;
						}
					}
				}
				SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=GravPortalProcess sid={spriteId3} idx={reference5.Index} slot={reference5.AllocatedSlot} anchor=({reference5.AnchorX_px},{reference5.AnchorY_px}) skip={skipDueToBlueOrb} gravBefore={s.GravFlipped} velBefore={s.VelY_fixed}");
				if (!skipDueToBlueOrb)
				{
					if (ApplyPortalSprite(ref s, spriteId3))
					{
						s.ProcessedSprites.Add(reference5.Index);
					}
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
			SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm=-1 tag=SpeedPortalScan.check sid={reference6.SpriteId} idx={reference6.Index} anchor=({reference6.AnchorX_px},{reference6.AnchorY_px}) hit=({reference6.HitLeft},{reference6.HitTop})-({reference6.HitRight},{reference6.HitBottom}) player=({num10},{num8})-({num11},{num9}) xO={num27} yO={flag5}");
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
			// NES sprite_collide only processes sprites whose activesprites_active=1.
			// Sprites near the NES Y-page boundary can be considered offscreen even
			// when their HitTop/HitBottom overlaps the player in PF world coords.
			// When multiple speed portals overlap with DIFFERENT speeds, the marginal
			// one may be offscreen in NES. Use max Y-overlap as proxy: the portal most
			// centered on the player is most likely active in NES.
			if (num29 > 1)
			{
				int bestIdx = -1;
				int bestOverlap = -1;
				for (int oi = 0; oi < num29; oi++)
				{
					ref SpriteEntry oe = ref spritesArr[span4[oi]];
					int overlapTop = Math.Max(num8, oe.HitTop);
					int overlapBot = Math.Min(num9, oe.HitBottom);
					int yOverlap = overlapBot - overlapTop;
					if (yOverlap > bestOverlap)
					{
						bestOverlap = yOverlap;
						bestIdx = oi;
					}
				}
				if (bestIdx >= 0)
				{
					int bestSpeed = SpriteIdToSpeedFixed(spritesArr[span4[bestIdx]].SpriteId);
					if (bestSpeed > 0)
					{
						SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm=-1 tag=SpeedPortalScan.apply sid={spritesArr[span4[bestIdx]].SpriteId} idx={spritesArr[span4[bestIdx]].Index} slot={spritesArr[span4[bestIdx]].AllocatedSlot} oldVelX={s.VelX_fixed} newVelX={bestSpeed} bestOverlap={bestOverlap}/{num29}");
						s.VelX_fixed = bestSpeed;
					}
				}
			}
			else
			{
				for (int num37 = 0; num37 < num29; num37++)
				{
					int num38 = SpriteIdToSpeedFixed(spritesArr[span4[num37]].SpriteId);
					if (num38 > 0)
					{
						SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm=-1 tag=SpeedPortalScan.apply sid={spritesArr[span4[num37]].SpriteId} idx={spritesArr[span4[num37]].Index} slot={spritesArr[span4[num37]].AllocatedSlot} oldVelX={s.VelX_fixed} newVelX={num38}");
						s.VelX_fixed = num38;
					}
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
				ApplyRainbowPortal(ref s, spriteId4, reference8.AnchorX_px,
					reference8.AnchorY_px - 8);
				s.ProcessedSprites.Add(reference8.Index);
				continue;
			}
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
					bool flag14 = ApplyPortalSprite(ref s, spriteId4,
						reference8.AnchorX_px);
					if (flag14)
					{
						s.ProcessedSprites.Add(reference8.Index);
					}
					if (IsGameModePortal(spriteId4) && flag14 && s.GameMode != 0 && s.GameMode != 4 && s.GameMode != 8 && s.GameMode != 9 && !dualActive)
					{
						int portalWorldY_px2 = reference8.AnchorY_px - 8;
						s.TargetCameraY_fixed = NesNtCameraTarget_fixed(portalWorldY_px2);
					}
					if (IsGameModePortal(spriteId4) && flag14)
						RecordRainbowExit(ref s, spriteId4,
							reference8.AnchorX_px, reference8.ProcessKey);
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
				if (IsBluePad(spriteId4))
				{
					// NES clears slope state even when this pad's gravity
					// gate is already satisfied and no launch is applied.
					ClearSlopeStuff(ref s);
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
					// NES sprite_gamemode_main (sprite_loading.h:466-468): merely TOUCHING
					// an orb routed via spcl_orb_cmn while holding A in BALL mode sets
					// ball_switched=1 (blocks the manual gravity switch until A release),
					// even if the orb never activates (no press). White orbs use the
					// separate spcl_wht_orb handler with no such side effect.
					// PF's BallFlipCooldown is the ball_switched analog (identical
					// release-reset semantics at the ball switch check).
					if (s.GameMode == 2 && inputHeld && !IsWhiteOrb(spriteId4) && !s.ProcessedSprites.Contains(reference8.Index))
					{
						s.BallFlipCooldown = 1;
					}
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
						// NES: dash orbs are cases inside sprite_gamemode_main, so mere
						// touch with A held in BALL mode also sets ball_switched=1.
						if (s.GameMode == 2 && inputHeld && !s.ProcessedSprites.Contains(reference8.Index))
						{
							s.BallFlipCooldown = 1;
						}
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
					s.Orbed = true;
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
				// NES teleport portals do NOT set activesprites_activated[index].
				// They fire every frame the player overlaps them. Do NOT add to
				// ProcessedSprites — the overlap check is the only gate.
				bool num63 = num11 >= reference8.HitLeft && reference8.HitRight >= num10;
				bool flag27 = num9 >= reference8.HitTop && reference8.HitBottom >= num8;
				if (!(num63 && flag27))
				{
					continue;
				}
				ApplyTeleportPortal(ref s, reference8, spriteId4, currentX_px);
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
						bool activatedPadIsSuppressed =
							s.ProcessedSprites.Contains(reference9.Index) &&
							(IsGreenPad(spriteId5) ||
							 (IsBluePad(spriteId5) && !s.DualActive));
						if (activatedPadIsSuppressed)
						{
							continue;
						}
						int num65 = currentX_px + 1;
						bool num66 = num65 + num5 >= reference9.HitLeft && reference9.HitRight >= num65;
						bool flag29 = num9 >= reference9.HitTop && reference9.HitBottom >= num8;
						if (num66 && flag29)
						{
							bool shouldApplyNestedPad = true;
							if (IsBluePad(spriteId5))
							{
								ClearSlopeStuff(ref s);
								bool gravityDownPad = spriteId5 == 13 || spriteId5 == 253;
								shouldApplyNestedPad = !((gravityDownPad && s.GravFlipped) ||
									(!gravityDownPad && !s.GravFlipped));
							}
							if (shouldApplyNestedPad)
							{
								ApplyPadSprite(ref s, spriteId5);
								orbHitThisFrame = true;
							}
							if (IsBluePad(spriteId5) || IsGreenPad(spriteId5))
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
		if (_dualActivatedThisProcessSprites && s.DualActive)
	{
		s.P2_VelY_fixed = -s.VelY_fixed;
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
				bool flag2 = ApplyPortalSprite(ref s, spriteId,
					reference.AnchorX_px);
				if (flag2)
				{
					s.ProcessedSprites.Add(reference.Index);
				}
				if (IsGameModePortal(spriteId) && flag2 && s.GameMode != 0 && s.GameMode != 4 && s.GameMode != 8 && s.GameMode != 9 && !s.DualActive)
				{
					int portalWorldY_px = reference.AnchorY_px - 8;
					s.TargetCameraY_fixed = NesNtCameraTarget_fixed(portalWorldY_px);
				}
				if (IsGameModePortal(spriteId) && flag2)
					RecordRainbowExit(ref s, spriteId,
						reference.AnchorX_px, reference.ProcessKey);
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

	private void ApplyRainbowPortal(ref SimState s, int sid, int portalWorldX_px,
		int portalWorldY_px)
	{
		int modeCount = sid == 100 ? 8 : 12;
		int oldMode = s.GameMode;
		if (oldMode == 6 || oldMode == 10)
			s.VelY_fixed = 0;

		int selectedMode = s.RainbowForcedModePlusOne > 0
			? s.RainbowForcedModePlusOne - 1
			: 0;
		if ((uint)selectedMode >= (uint)modeCount)
			selectedMode = 0;
		s.RainbowForcedModePlusOne = 0;
		s.GameMode = selectedMode;
		s.RainbowMaxMode = modeCount;
		s.RainbowPortalModeCount = modeCount;
		s.RainbowUniversalActive = true;
		s.RainbowSourcePortalX_px = portalWorldX_px;
		s.RainbowExitPortalX_px = 0;
		s.RainbowExitTargetModePlusOne = 0;
		s.RainbowExitPortalProcessKeyPlusOne = 0;
		s.RainbowExitPlayerX_fixed = 0;
		s.RainbowExitPlayerY_fixed = 0;
		s.RainbowExitVelX_fixed = 0;
		s.RainbowExitVelY_fixed = 0;
		s.RainbowExitStateFlags = 0;
		s.RainbowExitDashing = 0;

		// NES gamemode_stuff clears robotjumpframe for both players and always
		// retargets the camera in single-player, independent of the selected mode.
		s.RobotJumpTime = 0;
		s.P2_RobotJumpTime = 0;
		if (!s.DualActive)
			s.TargetCameraY_fixed = NesNtCameraTarget_fixed(portalWorldY_px);
	}

	private static void RecordRainbowExit(ref SimState s, int sid,
		int portalWorldX_px, int portalProcessKey)
	{
		int targetMode = SpriteIdToGameMode(sid);
		if (targetMode < 0 || !s.RainbowUniversalActive ||
			s.RainbowExitTargetModePlusOne != 0 ||
			portalWorldX_px <= s.RainbowSourcePortalX_px)
		{
			return;
		}
		s.RainbowExitPortalX_px = portalWorldX_px;
		s.RainbowExitTargetModePlusOne = targetMode + 1;
		s.RainbowExitPortalProcessKeyPlusOne = portalProcessKey + 1;
		s.RainbowExitPlayerX_fixed = s.X_fixed;
		s.RainbowExitPlayerY_fixed = s.Y_fixed;
		s.RainbowExitVelX_fixed = s.VelX_fixed;
		s.RainbowExitVelY_fixed = s.VelY_fixed;
		s.RainbowExitStateFlags = (s.GravFlipped ? 1 : 0) |
			(s.Mini ? 2 : 0) | (s.OnGround ? 4 : 0) |
			(s.PrevInputHeld ? 8 : 0) | (s.AirPressLatch ? 16 : 0);
		s.RainbowExitDashing = s.Dashing;
	}

	private bool ApplyPortalSprite(ref SimState s, int sid,
		int portalWorldX_px = int.MinValue)
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
				bool prevWaveSnake = gameMode == 6 || gameMode == 10;
				// NES sprite_loading.h gamemode portal velocity rules:
				//   ship/ball/UFO halve on mode change;
				//   robot halves only when leaving wave/snake;
				//   cube/spider/swing/ninja/pogo zero only when leaving wave/snake;
				//   wave/snake/football leave velocity unchanged.
				// NES/cc65 compiles signed /= 2 here as ASR, so odd negative
				// velocities round down: -545 / 2 becomes -273.
				switch (num)
				{
				case 1:
				case 2:
				case 3:
					s.VelY_fixed >>= 1;
					break;
				case 4:
					if (prevWaveSnake)
					{
						s.VelY_fixed >>= 1;
					}
					break;
				case 0:
				case 5:
				case 7:
				case 8:
				case 9:
					if (prevWaveSnake)
					{
						s.VelY_fixed = 0;
					}
					break;
				}
				switch (num)
				{
				case 8:
					if (_speculativeDepth == 0)
					{
						_committedNinjaJumps.Clear();
						_ninjaWaitFrames = 0;
					}
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
				s.GlobalSpeed_fixed = num5;
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
				s.VelY_fixed = NesGravityPortalHalfVelocity(s.VelY_fixed);
				s.RobotJumpTime = 0;
				s.WasZeroedByCollision = false;
				return true;
			}
			if (!flag2 && s.GravFlipped)
			{
				s.GravFlipped = false;
				s.GravMul = 1;
				s.VelY_fixed = NesGravityPortalHalfVelocity(s.VelY_fixed);
				s.RobotJumpTime = 0;
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

	private static void ClearSlopeStuff(ref SimState s)
	{
		s.SlopeWasOnCounter = 0;
		s.SlopeFrames = 0;
		s.SlopeType = 0;
		s.LastSlopeType = 0;
	}

	private void ApplyPadSprite(ref SimState s, int sid)
	{
		ClearSlopeStuff(ref s);
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
		ClearSlopeStuff(ref s);
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
		int dashVelX = s.VelX_fixed;
		bool controllerOnlyMode = s.GameMode == 1 || s.GameMode == 3 || s.GameMode == 6;
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
			s.VelY_fixed = controllerOnlyMode ? dashVelX : dashVelX * 4;
			if (controllerOnlyMode)
				s.VelX_fixed = 0;
			break;
		case 5:
			s.VelY_fixed = controllerOnlyMode ? -dashVelX : -dashVelX * 4;
			if (controllerOnlyMode)
				s.VelX_fixed = 0;
			break;
		}
		s.OnGround = false;
	}

	private void ApplySpiderTeleport(ref SimState s, bool goUp)
	{
		// sprite_collide leaves Generic.x at high_byte(currplayer_x) + 1.
		// Spider orb/pad handlers call spider_*_wait without restoring it, unlike
		// ordinary spider movement, so only these teleport scans inherit +1 X.
		int playerX_px = s.X_fixed >> 8;
		int num = s.Y_fixed >> 8;
		int hitboxW = GetHitboxW(s.Mini);
		int hitboxH = GetHitboxH(s.Mini);
		int num2 = (s.Mini ? (16 - hitboxH >> 1) : 0);
		SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=ApplySpiderTeleport.in goUp={(goUp ? 1 : 0)} X={playerX_px} Ywld={num} hbW={hitboxW} hbH={hitboxH} offY={num2} grav={(s.GravFlipped ? 1 : 0)}");
		if (goUp)
		{
			// spcl_sporbup/spcl_sppadup unconditionally consume the persistent
			// eject_D global; they do not run a fresh floor collision here.
			SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=ApplySpiderTeleport.preEjD raw=0x{s.EjectD:X2}");
			s.Y_fixed = (((s.Y_fixed >> 8) - s.EjectD) << 8) | (s.Y_fixed & 0xFF);
			s.VelY_fixed = 0;
			s.GravFlipped = true;
			s.GravMul = -1;
			if (SpiderScanUp(ref s, playerXBias: 1))
			{
				s.DeathType = 12;
				ApplyNesPlayerYHighSubtract(ref s, s.EjectU);
			}
			s.VelY_fixed = 0;
		}
		else if (num >= 0x10)
		{
			// The down handlers likewise use the stale byte from the last
			// bg_coll_U write. Preserve 8-bit addition before signed subtraction.
			byte ejectPlusOne = unchecked((byte)(s.EjectU + 1));
			int signedEjectPlusOne = (sbyte)ejectPlusOne;
			SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=ApplySpiderTeleport.preEjU raw=0x{s.EjectU:X2} plus1=0x{ejectPlusOne:X2} signed={signedEjectPlusOne}");
			s.Y_fixed = (((s.Y_fixed >> 8) - signedEjectPlusOne) << 8) | (s.Y_fixed & 0xFF);
			s.VelY_fixed = 0;
			s.GravFlipped = false;
			s.GravMul = 1;
			if (SpiderScanDown(ref s, playerXBias: 1))
			{
				s.DeathType = 12;
				ApplyNesPlayerYHighSubtract(ref s, s.EjectD);
			}
			s.VelY_fixed = 0;
		}
		s.Orbed = true;
		s.SlopeFrames = 0;
		s.SlopeWasOnCounter = 0;
	}

	private void SnapCameraToPlayerY(ref SimState s)
	{
		int num = (_minScrollYLin - _nesCoordOffset) << 8;
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
		int destinationScreenY_px = s.TeleportOutputY_px & 0xFF;
		// NES writes this byte to currplayer_y, which is screen-space. PF
		// stores world-space Y, so preserve the subpixel byte and solve for the
		// world coordinate that gives the same NES screen-space high byte.
		s.Y_fixed = s.CameraY_fixed + (destinationScreenY_px << 8) + (s.Y_fixed & 0xFF);
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
		// state_game.runthecolls skips bg_coll_death during spawn protection.
		if (s.InvincibleCounter != 0)
		{
			return false;
		}
		int playerX_px = s.X_fixed >> 8;
		int playerY_px = NesPlayerY_px(s.Y_fixed, s.CameraY_fixed);
		return SharedPhysics.CheckCenterPointDeath(in _collisionMap, playerX_px, playerY_px, GetHitboxW(s.Mini), GetHitboxH(s.Mini), GetHitboxOffsetY(s.GameMode, s.Mini, s.GravFlipped));
	}

	private bool PointKillsPlayer(int px, int py)
	{
		return SharedPhysics.PointKillsPlayer(in _collisionMap, px, py);
	}

	private bool CheckFloorSpikes(ref SimState s, int playerYBias = 0)
	{
		int num = s.X_fixed >> 8;
		int num2 = NesPlayerY_px(s.Y_fixed, s.CameraY_fixed) + playerYBias;
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
		return num3;
	}

	private bool CheckDeathCollision(ref SimState s, bool polluteSlopeState = true)
	{
		if (s.InvincibleCounter != 0)
		{
			return false;
		}
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
		// NES bg_coll_death (collision.h:1045) probes player center and runs the
		// bg_coll_U_D_checks || ... || bg_coll_slope chain. For slope tiles that
		// fall through to bg_coll_slope, the jump-table assignment in bg_coll_slope
		// sets currplayer_slope_type as a side effect (even when geometry misses,
		// as long as currplayer_was_on_slope_counter != 0). This pollution is what
		// later flips the decrement_was_on_slope exit-velocity check from "rising"
		// (RU22, applies +80 boost) to "falling" (LU22, no boost). Without this,
		// PF carries the stale ceiling-slope type into the next frame's decrement.
		int probeX = playerX_px + (hbW >> 1) - 1;
		int probeY = playerY_px + hbH / 2 + hbOffY;
		// 6502 level coordinates use arithmetic/floor tile division. C# integer
		// division truncates toward zero, which incorrectly maps Y -1..-15 to row
		// zero and can re-arm a ceiling slope that the NES probe has already left.
		int probeTileX = probeX >= 0 ? probeX / 16 : (probeX - 15) / 16;
		int probeTileY = probeY >= 0 ? probeY / 16 : (probeY - 15) / 16;
		MetatileCollision probeCol = GetTileCollision(probeTileX, probeTileY);
		// polluteSlopeState is false for the extra post-x-movement probe: NES runs
		// bg_coll_death exactly once per frame at the pre-move Generic.x, so only
		// that call may replicate the bg_coll_slope side effects.
		// Wave/snake center probes do run bg_coll_slope, but the NES does not
		// publish that temporary result into the persistent slope counters. Doing
		// so pins a dual wave in place on harmless LU45 geometry (Blast Processing).
		bool mayPollutePersistentSlope = s.GameMode != 6 && s.GameMode != 10;
		if (polluteSlopeState && mayPollutePersistentSlope &&
			probeCol >= MetatileCollision.COL_SLOPE_RD45 && probeCol <= MetatileCollision.COL_SLOPE_LU66_TOP)
		{
			var (slpHit, _, polluteSlope) = PfSlopeCalc(probeX, probeY, probeCol);
			if (slpHit)
			{
				s.SlopeType = polluteSlope;
				s.SlopeFrames = 1;
				s.SlopeWasOnCounter = 3;
				if (polluteSlope != 0) s.LastSlopeType = polluteSlope;
			}
			else if (s.SlopeWasOnCounter != 0 && polluteSlope != 0)
			{
				// NES bg_coll_slope col_end miss path: cpsT keeps jump-table value when cpswOn != 0
				s.SlopeType = polluteSlope;
			}
		}
		return SharedPhysics.CheckDeathCollision(in _collisionMap, playerX_px,
			playerY_px, hbW, hbH, hbOffY, s.GameMode, s.Dblocked,
			ForcePlatformer);
	}

	private bool CheckSlopePenetrationDeath(ref SimState s)
	{
		if (s.InvincibleCounter != 0)
		{
			return false;
		}
		int hitboxW;
		int hitboxH;
		int hitboxOffsetY;
		if (s.GameMode == 6 || s.GameMode == 10)
		{
			// x_movement leaves Generic at the fixed wave/snake dimensions;
			// bg_coll_death still applies the mini centering adjustment.
			hitboxW = 8;
			hitboxH = 8;
			hitboxOffsetY = s.Mini ? 4 : 0;
		}
		else
		{
			hitboxW = GetHitboxW(s.Mini);
			hitboxH = GetHitboxH(s.Mini);
			hitboxOffsetY = GetHitboxOffsetY(s.GameMode, s.Mini, s.GravFlipped);
		}
		int num = (s.X_fixed >> 8) + (hitboxW >> 1) - 1;
		int num2 = NesPlayerY_px(s.Y_fixed, s.CameraY_fixed) + hitboxH / 2 + hitboxOffsetY;
		// Match add_scroll_y/bg_collision_sub at the editor's negative world-Y
		// boundary. C# integer division truncates toward zero, which sampled row 0
		// instead of row -1 and created a phantom slope death above the level.
		int tileX = num >= 0 ? num / 16 : (num - 15) / 16;
		int tileY = num2 >= 0 ? num2 / 16 : (num2 - 15) / 16;
		MetatileCollision tileCollision = GetTileCollision(tileX, tileY);
		if ((s.GameMode == 6 || s.GameMode == 10) &&
			((!s.Mini && tileCollision == MetatileCollision.COL_SLOPE_LU45) ||
			 (s.Mini && (tileCollision == MetatileCollision.COL_SLOPE_LU66_TOP ||
			             tileCollision == MetatileCollision.COL_SLOPE_LU66_BOT))))
		{
			// NES bg_coll_slope deliberately ignores these shapes for wave/snake:
			// normal-size ignores LU45, while mini ignores both LU66 halves.
			return false;
		}
		if (tileCollision >= MetatileCollision.COL_SLOPE_RD45 && tileCollision <= MetatileCollision.COL_SLOPE_LU66_TOP && PfSlopeCalc(num, num2, tileCollision).hit)
		{
			return true;
		}
		return false;
	}

	private bool CheckForwardCollision(ref SimState s)
	{
		// This helper models bg_coll_R inside x_movement_coll, which is gated by
		// the same NES startup counter as the floor-spike pass.
		if (s.InvincibleCounter != 0)
		{
			return false;
		}
		if ((s.SlopeWasOnCounter | s.SlopeFrames) != 0)
		{
			return false;
		}
		int num = s.X_fixed >> 8;
		int num2 = NesPlayerBgCollisionY_px(s.Y_fixed, s.CameraY_fixed);
		int num3;
		int num4;
		int hbOffY;
		if (s.GameMode == 6)
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
		return SharedPhysics.CheckForwardCollision(in _collisionMap, num, num2, num3, num4, hbOffY, s.GameMode, s.Mini, s.GravFlipped, skipSlopeCheck: true, dblocked: s.Dblocked);
	}

	private static (bool hit, int ejection, int slopeType) PfSlopeCalc(int temp_x, int temp_y, MetatileCollision collision)
	{
		return SharedPhysics.SlopeCalc(temp_x, temp_y, collision);
	}

	private static int PfAdjustWaveSlopeEjection(int ejection, int slopeType,
		bool inputHeld, bool gravFlipped)
	{
		// bg_coll_slope's a_check_lookup side effect for non-cube modes.
		int lookupIndex = ((slopeType & 4) != 0 ? 4 : 0) |
			((slopeType & 8) != 0 ? 2 : 0) | (gravFlipped ? 1 : 0);
		bool aCheck = lookupIndex == 0 || lookupIndex == 3 ||
			lookupIndex == 4 || lookupIndex == 7;
		return (aCheck ? inputHeld : !inputHeld) ? 4 : ejection;
	}

	private static void PfUpdateSlopeCounters(ref SimState s)
	{
		SharedPhysics.UpdateSlopeCounters(ref s.SlopeWasOnCounter, ref s.SlopeType, ref s.VelY_fixed, ref s.Y_fixed, s.GameMode, s.GravFlipped, s.Mini, ref s.LastSlopeType);
	}

	private static void PfUpdateSlopeCountersPreGravity(ref SimState s)
	{
		SharedPhysics.UpdateSlopeCounters(ref s.SlopeWasOnCounter, ref s.SlopeType, ref s.VelY_fixed, ref s.Y_fixed, s.GameMode, s.GravFlipped, s.Mini, ref s.LastSlopeType, applyPosition: false);
	}

	private void PfSlopeDiag(ref SimState s, string tag)
	{
	}

	private static void PfUpdateSlopeCounters_Fresh(ref SimState s, int velX_fixed)
	{
		// NES runthecolls() skips x_movement_coll() while the fresh-start
		// invincibility counter is nonzero.  slope_frames is consumed only by
		// x_movement_coll(), so protected slope contacts must remain pending.
		if (s.InvincibleCounter != 0)
		{
			return;
		}
		SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=UpdateSlopeCountersFresh.in sFr={s.SlopeFrames} sT={s.SlopeType} Vy={s.VelY_fixed} Vx={velX_fixed}");
		SharedPhysics.UpdateSlopeCountersFresh(ref s.SlopeFrames, s.SlopeType, ref s.VelY_fixed, velX_fixed);
		SharedPhysics.FullTraceLog?.Invoke($"cur=0 gm={s.GameMode} tag=UpdateSlopeCountersFresh.out sFr={s.SlopeFrames} sT={s.SlopeType} Vy={s.VelY_fixed} Vx={velX_fixed}");
	}

	private static void PfApplySlopeVelocity(ref SimState s, int slopeType)
	{
		SharedPhysics.ApplySlopeVelocity(ref s.VelY_fixed, slopeType, s.VelX_fixed);
	}

	private static void PfSlopeJumpCheck(ref SimState s)
	{
		SharedPhysics.SlopeJumpCheck(ref s.VelY_fixed, ref s.SlopeJumpHigher, s.SlopeType, s.Mini, s.GravFlipped);
	}

	private (bool hit, int ejection, int slopeType) PfCheckSlopes(ref SimState s, bool input, int? genericY_px = null)
	{
		int num = s.X_fixed >> 8;
		int hitboxW = GetHitboxW(s.Mini);
		int hitboxH = GetHitboxH(s.Mini);
		int num2 = (s.Mini ? (16 - hitboxH >> 1) : 0);
		int checkBaseY = (genericY_px ?? NesPlayerY_px(s.Y_fixed, s.CameraY_fixed)) + num2 + hitboxH - 2;
		var (hit, ejection, slopeType, processedSlopeTile) = SharedPhysics.CheckSlopesDown(in _collisionMap, num, num, checkBaseY, hitboxW, input, s.GameMode, s.GravFlipped, s.VelX_fixed, s.SlopeType, ref s.LastSlopeType, ref s.SlopeJumpHigher, ref s.SlopeFrames, ref s.SlopeWasOnCounter);
		// bg_coll_slope writes the live slope type even when its directional
		// filter rejects the geometric hit. Spider movement consumes that value
		// later in the same frame from x_movement_coll.
		if (processedSlopeTile)
			s.SlopeType = slopeType;
		return (hit, ejection, slopeType);
	}

	private (bool hit, int ejection, int slopeType) PfCheckSlopesUp(ref SimState s, bool input, int? genericY_px = null)
	{
		int num = s.X_fixed >> 8;
		int hitboxW = GetHitboxW(s.Mini);
		int hitboxH = GetHitboxH(s.Mini);
		int num2 = (s.Mini ? (16 - hitboxH >> 1) : 0);
		int checkBaseY = (genericY_px ?? NesPlayerY_px(s.Y_fixed, s.CameraY_fixed)) + num2 + (s.Mini ? 1 : 2) + ((s.GameMode == 1) ? 1 : 0);
		var (hit, ejection, slopeType, processedSlopeTile) = SharedPhysics.CheckSlopesUp(in _collisionMap, num, num, checkBaseY, hitboxW, input, s.GameMode, s.GravFlipped, s.VelX_fixed, s.SlopeType, ref s.LastSlopeType, ref s.SlopeJumpHigher, ref s.SlopeFrames, ref s.SlopeWasOnCounter);
		if (processedSlopeTile)
			s.SlopeType = slopeType;
		return (hit, ejection, slopeType);
	}
}

