// PfTrace — comprehensive physics-trace infrastructure for famidash PF.
//
// Designed to mirror the granularity of Mesen's NES physics-debug Lua log:
// every gamemode movement function, every eject, every slope/collision
// probe, every sprite/orb/pad/portal interaction emits a structured
// key=value line tagged with frame#, player slot, and gamemode.
//
// Usage:
//   PfTrace.Open(levelName);                 // once per replay
//   PfTrace.SetFrameContext(f, cur, gm);    // top of StepFrame
//   PfTrace.Event("ship_movement.in");      // bare tag
//   PfTrace.Event("ship_movement.out", $"X={x} Y={y} Vy={vy}");
//   PfTrace.State("ship_eject.in", ref s);  // full state dump
//   PfTrace.Close();                         // end of replay
//
// All calls are O(1) when Enabled==false (single bool check, no allocs).
//
// Activation: set PfTrace.Enabled=true (caller code) OR set env var
// FAMIDASH_PF_TRACE=1 before constructing the engine.  Frame range can be
// narrowed via FAMIDASH_PF_TRACE_LO / FAMIDASH_PF_TRACE_HI (inclusive).

#nullable enable
using System;
using System.IO;
using System.Text;

namespace FamidashEditor
{
    public static class PfTrace
    {
        // -- Public toggles -------------------------------------------------
        public static bool Enabled;
        public static int FrameLo = 0;
        public static int FrameHi = int.MaxValue;

        // -- Output --------------------------------------------------------
        private static StreamWriter? _w;
        private static string? _path;
        private static readonly object _gate = new();

        // -- Per-frame context (set by host per frame) ---------------------
        private static int _curFrame;
        private static int _curCur;         // player slot (0/1)
        private static int _curGm = -1;     // gamemode
        private static int _speculative;    // >0 = suppress

        // -- Static init: honour env vars ----------------------------------
        static PfTrace()
        {
            try
            {
                if (Environment.GetEnvironmentVariable("FAMIDASH_PF_TRACE") == "1")
                    Enabled = true;
                var lo = Environment.GetEnvironmentVariable("FAMIDASH_PF_TRACE_LO");
                var hi = Environment.GetEnvironmentVariable("FAMIDASH_PF_TRACE_HI");
                if (int.TryParse(lo, out int loV)) FrameLo = loV;
                if (int.TryParse(hi, out int hiV)) FrameHi = hiV;
            }
            catch { }
        }

        public static string Path => _path ?? string.Empty;

        public static void Open(string levelName)
        {
            Close();
            try
            {
                string safe = SanitizeName(levelName);
                string ts = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                _path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    $"famidash_pf_trace_FULL_{safe}_{ts}.log");
                _w = new StreamWriter(_path, append: false) { AutoFlush = false };
                _w.WriteLine($"# PfTrace opened {DateTime.UtcNow:o} level={levelName}");
                _w.WriteLine($"# FrameLo={FrameLo} FrameHi={FrameHi}");
                _w.WriteLine($"# format: f=N cur=C gm=G tag=NAME k=v k=v ...");
                _w.Flush();
            }
            catch { _w = null; }
        }

        public static void Close()
        {
            lock (_gate)
            {
                try { _w?.Flush(); _w?.Dispose(); }
                catch { }
                _w = null;
            }
        }

        public static void Flush()
        {
            lock (_gate) { try { _w?.Flush(); } catch { } }
        }

        public static void SetSpeculative(int depth) => _speculative = depth;

        public static void SetFrameContext(int frame, int cur, int gamemode)
        {
            _curFrame = frame;
            _curCur = cur;
            _curGm = gamemode;
        }

        public static void SetGameMode(int gamemode) => _curGm = gamemode;

        // -- Fast-path check ------------------------------------------------
        private static bool ShouldEmit()
        {
            if (!Enabled) return false;
            if (_w == null) return false;
            if (_speculative > 0) return false;
            if (_curFrame < FrameLo || _curFrame > FrameHi) return false;
            return true;
        }

        // -- Primitives -----------------------------------------------------
        public static void Event(string tag)
        {
            if (!ShouldEmit()) return;
            lock (_gate)
            {
                try { _w!.WriteLine($"f={_curFrame} cur={_curCur} gm={_curGm} tag={tag}"); }
                catch { }
            }
        }

        public static void Event(string tag, string body)
        {
            if (!ShouldEmit()) return;
            lock (_gate)
            {
                try { _w!.WriteLine($"f={_curFrame} cur={_curCur} gm={_curGm} tag={tag} {body}"); }
                catch { }
            }
        }

        // -- Helpers (allocate strings only when emitting) ------------------

        /// Full state snapshot of the player.
        public static void State(string tag,
            int xFx, int yFx, int vxFx, int vyFx,
            int slopeFrames, int slopeWasOn, int slopeType, int lastSlopeType,
            bool input, bool grav, bool mini, int gameMode,
            int camYFx = 0, bool onGround = false, bool dashing = false)
        {
            if (!ShouldEmit()) return;
            int yPxNes = ((yFx - camYFx) >> 8) + (camYFx >> 8);
            string body = $"X={xFx >> 8}.{(xFx & 0xFF):X2} Y={yFx >> 8}.{(yFx & 0xFF):X2} Ypx={yPxNes} " +
                          $"Vx={vxFx} Vy={vyFx} " +
                          $"sFr={slopeFrames} swOn={slopeWasOn} sT={slopeType} lst={lastSlopeType} " +
                          $"inp={(input ? 1 : 0)} grav={(grav ? 1 : 0)} mini={(mini ? 1 : 0)} " +
                          $"gm={gameMode} onG={(onGround ? 1 : 0)} dash={(dashing ? 1 : 0)} " +
                          $"camY={camYFx >> 8}.{(camYFx & 0xFF):X2}";
            Event(tag, body);
        }

        /// Probe result.
        public static void Probe(string tag, int probeX, int probeY, int tileX, int tileY,
            int collisionVal, bool hit, int ejection = 0, int slopeType = 0)
        {
            if (!ShouldEmit()) return;
            Event(tag, $"px={probeX} py={probeY} tx={tileX} ty={tileY} col=0x{collisionVal:X2} hit={(hit ? 1 : 0)} ej={ejection} sT={slopeType}");
        }

        /// Eject delta.
        public static void Eject(string tag, int oldYFx, int newYFx, int oldVy, int newVy,
            int ejectAmount = 0, int slopeType = 0, int sFr = 0, int swOn = 0)
        {
            if (!ShouldEmit()) return;
            Event(tag, $"Yold={oldYFx >> 8}.{(oldYFx & 0xFF):X2} Ynew={newYFx >> 8}.{(newYFx & 0xFF):X2} " +
                       $"Vyold={oldVy} Vynew={newVy} ej={ejectAmount} sT={slopeType} sFr={sFr} swOn={swOn}");
        }

        /// Gravity step.
        public static void Gravity(string tag, int vyPre, int vyPost, int gravity,
            bool falling, bool holding, bool gravFlipped, int tmpFallSpeed = 0, double gravMod = 0)
        {
            if (!ShouldEmit()) return;
            Event(tag, $"Vypre={vyPre} Vypost={vyPost} g={gravity} fall={(falling ? 1 : 0)} hold={(holding ? 1 : 0)} flip={(gravFlipped ? 1 : 0)} tfs={tmpFallSpeed} gMod={gravMod}");
        }

        /// Slope counter mutation.
        public static void SlopeCnt(string tag, int sFrOld, int sFrNew, int swOnOld, int swOnNew,
            int sTOld, int sTNew, int lstOld, int lstNew, int delta = 0)
        {
            if (!ShouldEmit()) return;
            Event(tag, $"sFr={sFrOld}->{sFrNew} swOn={swOnOld}->{swOnNew} sT={sTOld}->{sTNew} lst={lstOld}->{lstNew} dY={delta}");
        }

        /// Slope velocity application.
        public static void SlopeVel(string tag, int vx, int slopeType, int vyPre, int vyPost)
        {
            if (!ShouldEmit()) return;
            Event(tag, $"Vx={vx} sT={slopeType} Vypre={vyPre} Vypost={vyPost}");
        }

        /// Sprite/orb/pad/portal interaction.
        public static void Sprite(string tag, int spriteIdx, int spriteVal, int spriteX, int spriteY,
            int playerX = 0, int playerY = 0, string? extra = null)
        {
            if (!ShouldEmit()) return;
            string body = $"idx={spriteIdx} val=0x{spriteVal:X2} sx={spriteX} sy={spriteY} px={playerX} py={playerY}";
            if (extra != null) body += " " + extra;
            Event(tag, body);
        }

        /// Tile read.
        public static void Tile(string tag, int tileX, int tileY, int collisionVal, int tileId = -1)
        {
            if (!ShouldEmit()) return;
            string body = $"tx={tileX} ty={tileY} col=0x{collisionVal:X2}";
            if (tileId >= 0) body += $" tid={tileId}";
            Event(tag, body);
        }

        /// Camera/scroll change.
        public static void Cam(string tag, int camYFxOld, int camYFxNew, int tgtCamYFxOld, int tgtCamYFxNew, int scrollSub = 0)
        {
            if (!ShouldEmit()) return;
            Event(tag, $"camY={camYFxOld >> 8}.{(camYFxOld & 0xFF):X2}->{camYFxNew >> 8}.{(camYFxNew & 0xFF):X2} " +
                       $"tgt={tgtCamYFxOld >> 8}.{(tgtCamYFxOld & 0xFF):X2}->{tgtCamYFxNew >> 8}.{(tgtCamYFxNew & 0xFF):X2} sub={scrollSub}");
        }

        /// Death notification.
        public static void Death(string tag, string reason, int xFx, int yFx)
        {
            if (!ShouldEmit()) return;
            Event(tag, $"reason={reason} X={xFx >> 8}.{(xFx & 0xFF):X2} Y={yFx >> 8}.{(yFx & 0xFF):X2}");
        }

        /// Conditional branch trace ("if X then ..." outcome).
        public static void Branch(string tag, string condition, bool taken, string? extra = null)
        {
            if (!ShouldEmit()) return;
            string body = $"cond=\"{condition}\" taken={(taken ? 1 : 0)}";
            if (extra != null) body += " " + extra;
            Event(tag, body);
        }

        /// Forward / X-axis collision probe (bg_coll_R analog).
        public static void XColl(string tag, int probeX, int probeY, int leftX, int rightX,
            bool hit, int nudge = 0, int hitSlopeType = 0)
        {
            if (!ShouldEmit()) return;
            Event(tag, $"px={probeX} py={probeY} lx={leftX} rx={rightX} hit={(hit ? 1 : 0)} nudge={nudge} sT={hitSlopeType}");
        }

        /// Generic numeric snapshot (3 values).
        public static void V(string tag, string a, int va, string b, int vb, string c, int vc)
        {
            if (!ShouldEmit()) return;
            Event(tag, $"{a}={va} {b}={vb} {c}={vc}");
        }

        public static void V(string tag, string a, int va, string b, int vb)
        {
            if (!ShouldEmit()) return;
            Event(tag, $"{a}={va} {b}={vb}");
        }

        public static void V(string tag, string a, int va)
        {
            if (!ShouldEmit()) return;
            Event(tag, $"{a}={va}");
        }

        // -- Util -----------------------------------------------------------
        private static string SanitizeName(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "unknown";
            var sb = new StringBuilder(s!.Length);
            foreach (var c in s!)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return sb.ToString();
        }
    }
}
