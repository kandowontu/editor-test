using System;
using System.IO;
using System.Text;

namespace FamidashEditor;

public static class PfTrace
{
	public static bool Enabled;

	public static int FrameLo;

	public static int FrameHi;

	private static StreamWriter? _w;

	private static string? _path;

	private static readonly object _gate;

	private static int _curFrame;

	private static int _curCur;

	private static int _curGm;

	private static int _speculative;

	public static string Path => _path ?? string.Empty;

	static PfTrace()
	{
		FrameLo = 0;
		FrameHi = int.MaxValue;
		_gate = new object();
		_curGm = -1;
		try
		{
			if (Environment.GetEnvironmentVariable("FAMIDASH_PF_TRACE") == "1")
			{
				Enabled = true;
			}
			string? environmentVariable = Environment.GetEnvironmentVariable("FAMIDASH_PF_TRACE_LO");
			string environmentVariable2 = Environment.GetEnvironmentVariable("FAMIDASH_PF_TRACE_HI");
			if (int.TryParse(environmentVariable, out var result))
			{
				FrameLo = result;
			}
			if (int.TryParse(environmentVariable2, out var result2))
			{
				FrameHi = result2;
			}
		}
		catch
		{
		}
	}

	public static void Open(string levelName)
	{
		Close();
		try
		{
			string value = SanitizeName(levelName);
			string value2 = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
			_path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"famidash_pf_trace_FULL_{value}_{value2}.log");
			_w = new StreamWriter(_path, append: false)
			{
				AutoFlush = false
			};
			_w.WriteLine($"# PfTrace opened {DateTime.UtcNow:o} level={levelName}");
			_w.WriteLine($"# FrameLo={FrameLo} FrameHi={FrameHi}");
			_w.WriteLine("# format: f=N cur=C gm=G tag=NAME k=v k=v ...");
			_w.Flush();
		}
		catch
		{
			_w = null;
		}
	}

	public static void Close()
	{
		lock (_gate)
		{
			try
			{
				_w?.Flush();
				_w?.Dispose();
			}
			catch
			{
			}
			_w = null;
		}
	}

	public static void Flush()
	{
		lock (_gate)
		{
			try
			{
				_w?.Flush();
			}
			catch
			{
			}
		}
	}

	public static void SetSpeculative(int depth)
	{
		_speculative = depth;
	}

	public static void SetFrameContext(int frame, int cur, int gamemode)
	{
		_curFrame = frame;
		_curCur = cur;
		_curGm = gamemode;
	}

	public static void SetGameMode(int gamemode)
	{
		_curGm = gamemode;
	}

	private static bool ShouldEmit()
	{
		if (!Enabled)
		{
			return false;
		}
		if (_w == null)
		{
			return false;
		}
		if (_speculative > 0)
		{
			return false;
		}
		if (_curFrame < FrameLo || _curFrame > FrameHi)
		{
			return false;
		}
		return true;
	}

	public static void Event(string tag)
	{
		if (!ShouldEmit())
		{
			return;
		}
		lock (_gate)
		{
			try
			{
				_w.WriteLine($"f={_curFrame} cur={_curCur} gm={_curGm} tag={tag}");
			}
			catch
			{
			}
		}
	}

	public static void Event(string tag, string body)
	{
		if (!ShouldEmit())
		{
			return;
		}
		lock (_gate)
		{
			try
			{
				_w.WriteLine($"f={_curFrame} cur={_curCur} gm={_curGm} tag={tag} {body}");
			}
			catch
			{
			}
		}
	}

	public static void State(string tag, int xFx, int yFx, int vxFx, int vyFx, int slopeFrames, int slopeWasOn, int slopeType, int lastSlopeType, bool input, bool grav, bool mini, int gameMode, int camYFx = 0, bool onGround = false, bool dashing = false)
	{
		if (ShouldEmit())
		{
			int value = (yFx - camYFx >> 8) + (camYFx >> 8);
			string body = $"X={xFx >> 8}.{xFx & 0xFF:X2} Y={yFx >> 8}.{yFx & 0xFF:X2} Ypx={value} Vx={vxFx} Vy={vyFx} sFr={slopeFrames} swOn={slopeWasOn} sT={slopeType} lst={lastSlopeType} inp={input} grav={grav} mini={mini} gm={gameMode} onG={onGround} dash={dashing} camY={camYFx >> 8}.{camYFx & 0xFF:X2}";
			Event(tag, body);
		}
	}

	public static void Probe(string tag, int probeX, int probeY, int tileX, int tileY, int collisionVal, bool hit, int ejection = 0, int slopeType = 0)
	{
		if (ShouldEmit())
		{
			Event(tag, $"px={probeX} py={probeY} tx={tileX} ty={tileY} col=0x{collisionVal:X2} hit={hit} ej={ejection} sT={slopeType}");
		}
	}

	public static void Eject(string tag, int oldYFx, int newYFx, int oldVy, int newVy, int ejectAmount = 0, int slopeType = 0, int sFr = 0, int swOn = 0)
	{
		if (ShouldEmit())
		{
			Event(tag, $"Yold={oldYFx >> 8}.{oldYFx & 0xFF:X2} Ynew={newYFx >> 8}.{newYFx & 0xFF:X2} Vyold={oldVy} Vynew={newVy} ej={ejectAmount} sT={slopeType} sFr={sFr} swOn={swOn}");
		}
	}

	public static void Gravity(string tag, int vyPre, int vyPost, int gravity, bool falling, bool holding, bool gravFlipped, int tmpFallSpeed = 0, double gravMod = 0.0)
	{
		if (ShouldEmit())
		{
			Event(tag, $"Vypre={vyPre} Vypost={vyPost} g={gravity} fall={falling} hold={holding} flip={gravFlipped} tfs={tmpFallSpeed} gMod={gravMod}");
		}
	}

	public static void SlopeCnt(string tag, int sFrOld, int sFrNew, int swOnOld, int swOnNew, int sTOld, int sTNew, int lstOld, int lstNew, int delta = 0)
	{
		if (ShouldEmit())
		{
			Event(tag, $"sFr={sFrOld}->{sFrNew} swOn={swOnOld}->{swOnNew} sT={sTOld}->{sTNew} lst={lstOld}->{lstNew} dY={delta}");
		}
	}

	public static void SlopeVel(string tag, int vx, int slopeType, int vyPre, int vyPost)
	{
		if (ShouldEmit())
		{
			Event(tag, $"Vx={vx} sT={slopeType} Vypre={vyPre} Vypost={vyPost}");
		}
	}

	public static void Sprite(string tag, int spriteIdx, int spriteVal, int spriteX, int spriteY, int playerX = 0, int playerY = 0, string? extra = null)
	{
		if (ShouldEmit())
		{
			string text = $"idx={spriteIdx} val=0x{spriteVal:X2} sx={spriteX} sy={spriteY} px={playerX} py={playerY}";
			if (extra != null)
			{
				text = text + " " + extra;
			}
			Event(tag, text);
		}
	}

	public static void Tile(string tag, int tileX, int tileY, int collisionVal, int tileId = -1)
	{
		if (ShouldEmit())
		{
			string text = $"tx={tileX} ty={tileY} col=0x{collisionVal:X2}";
			if (tileId >= 0)
			{
				text += $" tid={tileId}";
			}
			Event(tag, text);
		}
	}

	public static void Cam(string tag, int camYFxOld, int camYFxNew, int tgtCamYFxOld, int tgtCamYFxNew, int scrollSub = 0)
	{
		if (ShouldEmit())
		{
			Event(tag, $"camY={camYFxOld >> 8}.{camYFxOld & 0xFF:X2}->{camYFxNew >> 8}.{camYFxNew & 0xFF:X2} tgt={tgtCamYFxOld >> 8}.{tgtCamYFxOld & 0xFF:X2}->{tgtCamYFxNew >> 8}.{tgtCamYFxNew & 0xFF:X2} sub={scrollSub}");
		}
	}

	public static void Death(string tag, string reason, int xFx, int yFx)
	{
		if (ShouldEmit())
		{
			Event(tag, $"reason={reason} X={xFx >> 8}.{xFx & 0xFF:X2} Y={yFx >> 8}.{yFx & 0xFF:X2}");
		}
	}

	public static void Branch(string tag, string condition, bool taken, string? extra = null)
	{
		if (ShouldEmit())
		{
			string text = $"cond=\"{condition}\" taken={taken}";
			if (extra != null)
			{
				text = text + " " + extra;
			}
			Event(tag, text);
		}
	}

	public static void XColl(string tag, int probeX, int probeY, int leftX, int rightX, bool hit, int nudge = 0, int hitSlopeType = 0)
	{
		if (ShouldEmit())
		{
			Event(tag, $"px={probeX} py={probeY} lx={leftX} rx={rightX} hit={hit} nudge={nudge} sT={hitSlopeType}");
		}
	}

	public static void V(string tag, string a, int va, string b, int vb, string c, int vc)
	{
		if (ShouldEmit())
		{
			Event(tag, $"{a}={va} {b}={vb} {c}={vc}");
		}
	}

	public static void V(string tag, string a, int va, string b, int vb)
	{
		if (ShouldEmit())
		{
			Event(tag, $"{a}={va} {b}={vb}");
		}
	}

	public static void V(string tag, string a, int va)
	{
		if (ShouldEmit())
		{
			Event(tag, $"{a}={va}");
		}
	}

	private static string SanitizeName(string? s)
	{
		if (string.IsNullOrWhiteSpace(s))
		{
			return "unknown";
		}
		StringBuilder stringBuilder = new StringBuilder(s.Length);
		foreach (char c in s)
		{
			stringBuilder.Append((char.IsLetterOrDigit(c) || c == '-' || c == '_') ? c : '_');
		}
		return stringBuilder.ToString();
	}
}
