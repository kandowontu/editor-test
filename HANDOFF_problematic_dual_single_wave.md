# Handoff: Problematic.tmx Dual-to-Single Wave 1px Divergence + Missing PF FULL Traces

Session date: May 28, 2026.
Workspace: `c:\Editor Test`.

The user lost the previous handoff (file was wiped to 0 bytes). This is the recreated version with everything I can put down so a new chat can take over cold.

---

## TL;DR — Two Open Threads

1. **`problematic.tmx`** — after the dual section, wave mode has a persistent 1px Y divergence after hitting the single portal. Source-confirmed area: dual→single state copy + scroll/camera ordering. Do NOT patch until source + logs prove the exact ordering.
2. **PF detailed (FULL) traces with the tmx name are no longer generated.** Confirmed root cause: `PfTrace.Open(...)` has **zero callers** in the entire codebase. The static `PfTrace` class still exists in `native-windows/PfTrace.cs` but nothing calls `PfTrace.Open()` to actually open the writer. Even when env var `FAMIDASH_PF_TRACE=1` flips `Enabled=true`, `_w` stays null and `ShouldEmit()` returns false, so no `famidash_pf_trace_FULL_*.log` is ever created. A previous build must have called `PfTrace.Open(LevelName)` from the engine's run-start path; a recent revert removed it. See "Missing PF FULL Traces" section below for fix sketch.

Also note: the per-run **stamped** PF CSV (`famidash_pf_trace_<level>_<ts>.csv`) is no longer produced either — the current `_frameTracePath` in `native-windows/PathfinderEngine.cs` is the OLD static `%TEMP%\famidash_pf_trace.csv` (no level name, no timestamp). The user's most recent stamped CSV `famidash_pf_trace_Problematic_20260528_004400_499.csv` is from an earlier build.

---

## Latest Confirmed Problematic Logs

The most informative pair for the actual divergence is still the older stamped run (these have the full post-dual wave window). The newest Mesen trace from `20260528_034730_255` only covers the early pre-dual portion (one dual change at sim=447).

Use for divergence analysis:

- Mesen
  - `C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace_Problematic_20260528_005701_830.csv` (616 KB)
  - `C:\Users\kando\AppData\Local\Temp\famidash_mesen_physics_debug_Problematic_20260528_005701_830.log` (8.27 MB)
  - `C:\Users\kando\AppData\Local\Temp\famidash_mesen_orb_debug_Problematic_20260528_005701_830.log` (1.84 MB)
- PF (older stamped — likely the LAST run that produced both stamped CSV and FULL log)
  - `C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_Problematic_20260528_004400_499.csv` (325 KB)
  - `C:\Users\kando\AppData\Local\Temp\famidash_pf_orb_debug_Problematic_20260528_004400_499.log` (1.07 MB)
  - `C:\Users\kando\AppData\Local\Temp\famidash_pf_debug_Problematic_20260528_004400.txt` (2.29 MB)
  - `C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_FULL_C__famidash_LEVELS_LEVEL_DATA_lvlset_HUGE_Problematic_tmx_20260528_005628.log` (6.33 MB) — this is the only `*_Problematic_*` FULL trace and confirms FULL traces stopped being generated AFTER May 27 8:56 PM.

Latest temp inventory (newest 20):

```text
Name                                                                                                  Length LastWriteTime
----                                                                                                  ------ -------------
famidash_mesen_trace_Problematic_20260528_034730_255.csv                                              349368 5/27/2026 11:52:49 PM
famidash_mesen_orb_debug_Problematic_20260528_034730_255.log                                          757212 5/27/2026 11:52:49 PM
famidash_mesen_physics_debug_Problematic_20260528_034730_255.log                                     1285838 5/27/2026 11:52:47 PM
famidash_mesen_orb_debug_Problematic_20260528_035243_817.log                                               0 5/27/2026 11:52:43 PM
famidash_mesen_physics_debug_Problematic_20260528_035243_817.log                                           0 5/27/2026 11:52:43 PM
famidash_mesen_trace_Problematic_20260528_035243_817.csv                                                   0 5/27/2026 11:52:43 PM
famidash_mesen_orb_debug_Problematic_20260528_031558_041.log                                         1081220 5/27/2026 11:16:05 PM
famidash_mesen_trace_Problematic_20260528_031558_041.csv                                              308541 5/27/2026 11:16:05 PM
famidash_mesen_physics_debug_Problematic_20260528_031558_041.log                                     2150586 5/27/2026 11:16:03 PM
famidash_mesen_orb_debug_Problematic_20260528_005701_830.log                                         1840822 5/27/2026 8:58:18 PM
famidash_mesen_trace_Problematic_20260528_005701_830.csv                                              616054 5/27/2026 8:58:18 PM
famidash_mesen_physics_debug_Problematic_20260528_005701_830.log                                     8276020 5/27/2026 8:58:16 PM
famidash_pf_orb_debug_Problematic_20260528_004400_499.log                                            1078281 5/27/2026 8:56:34 PM
famidash_pf_debug_Problematic_20260528_004400.txt                                                    2297983 5/27/2026 8:56:34 PM
famidash_pf_trace_FULL_C__famidash_LEVELS_LEVEL_DATA_lvlset_HUGE_Problematic_tmx_20260528_005628.log 6333560 5/27/2026 8:56:34 PM
famidash_pf_trace_Problematic_20260528_004400_499.csv                                                 325074 5/27/2026 8:56:34 PM
famidash_mesen_trace_Problematic_20260527_220656_536.csv                                              521845 5/27/2026 6:09:18 PM
famidash_mesen_orb_debug_Problematic_20260527_220656_536.log                                         1554728 5/27/2026 6:09:18 PM
famidash_mesen_physics_debug_Problematic_20260527_220656_536.log                                     5330466 5/27/2026 6:09:16 PM
```

Latest FULL traces across all levels (proves FULL generation stopped on May 27 8:56 PM):

```text
Name                                                                                                       Length LastWriteTime
----                                                                                                       ------ -------------
famidash_pf_trace_FULL_C__famidash_LEVELS_LEVEL_DATA_lvlset_HUGE_Problematic_tmx_20260528_005628.log      6333560 5/27/2026 8:56:34 PM
famidash_pf_trace_FULL_C__famidash_LEVELS_LEVEL_DATA_lvlset_HUGE_blastprocessing_tmx_20260528_004516.log 13514044 5/27/2026 8:45:28 PM
famidash_pf_trace_FULL_unknown_20260528_004348.log                                                        7399620 5/27/2026 8:43:52 PM
famidash_pf_trace_FULL_C__famidash_LEVELS_LEVEL_DATA_lvlset_HUGE_deadlocked_tmx_20260527_235948.log      13867339 5/27/2026 7:59:55 PM
famidash_pf_trace_FULL_C__famidash_LEVELS_LEVEL_DATA_lvlset_HUGE_bloodbath_tmx_20260527_235811.log       12947605 5/27/2026 7:58:25 PM
famidash_pf_trace_FULL_C__famidash_LEVELS_LEVEL_DATA_lvlset_HUGE_cataclysm_tmx_20260527_234651.log       10715415 5/27/2026 7:47:05 PM
famidash_pf_trace_FULL_C__famidash_LEVELS_LEVEL_DATA_lvlset_HUGE_demonpark_tmx_20260527_225035.log       13736896 5/27/2026 6:51:14 PM
famidash_pf_trace_FULL_C__famidash_LEVELS_LEVEL_DATA_lvlset_HUGE_Problematic_tmx_20260527_220647.log      6312764 5/27/2026 6:06:56 PM
famidash_pf_trace_FULL_C__famidash_LEVELS_LEVEL_DATA_lvlset_HUGE_blastprocessing_tmx_20260527_211213.log 13555005 5/27/2026 5:12:24 PM
famidash_pf_trace_FULL_C__famidash_LEVELS_LEVEL_DATA_lvlset_HUGE_dorabaebasic7_tmx_20260527_210841.log   21564612 5/27/2026 5:08:48 PM
```

Empty Mesen sets (`*_220854_133`, `*_225255_805`, `*_035243_817`) are from an older bug that was already fixed and rolled back; ignore them.

---

## Trace Alignment Rule (critical)

This has caused confusion before — write it on the wall:

- Mesen Lua `sim_cursor` is 1-based.
- Compare Mesen row `sim_cursor=N` to PF frame `N - 1`.
- `a_cur` in the Mesen row corresponds to PF input at frame `N - 1`.
- `a_next` corresponds to PF input at frame `N`.

Repo memory file: `/memories/repo/famidash-mesen-trace-cursor-indexing.md`.

---

## Current Measured Handoff Window (from prior session)

Mode/dual transitions in `famidash_mesen_trace_Problematic_20260528_005701_830.csv`:

```text
 sim frame dual prevDual mode prevMode    x   y raw_y vel_y
 --- ----- ---- -------- ---- --------    -   - ----- -----
 446   445    0        0    1        0 1244 300 26070 -355
 897   896    0        0    6        1 2492 281 21992 -708
1615  1614    1        0    6        6 5061 309 30135 881
1858  1857    0        1    6        6 5862 314 29477 -1065
```

Focused compare around dual→single drop (vs PF stamped CSV `20260528_004400_499`):

```text
 sim frame mDual mMode pMode   mX   pX dx  mY  pY
 --- ----- ----- ----- -----   --   -- --  --  --
1848  1847     1     6     6 5821 5821  0 297 298
1849  1848     1     6     6 5825 5825  0 301 302
1850  1849     1     6     6 5829 5829  0 305 306
1851  1850     1     6     6 5833 5833  0 309 310
1852  1851     1     6     6 5837 5837  0 314 314
1853  1852     1     6     6 5841 5841  0 318 318
1854  1853     1     6     6 5846 5846  0 314 314
1855  1854     1     6     6 5850 5850  0 318 318
1856  1855     1     6     6 5854 5854  0 314 314
1857  1856     1     6     6 5858 5858  0 318 318
1858  1857     0     6     6 5862 5862  0 314 314    <-- single portal hit
1859  1858     0     6     6 5866 5866  0 318 318
1860  1859     0     6     6 5870 5870  0 314 314
1861  1860     0     6     6 5875 5875  0 318 318
1862  1861     0     6     6 5879 5879  0 314 314
1863  1862     0     6     6 5883 5883  0 309 310
1864  1863     0     6     6 5887 5887  0 305 306
1865  1864     0     6     6 5891 5891  0 309 310
1866  1865     0     6     6 5895 5895  0 305 306
1867  1866     0     6     6 5900 5900  0 301 302
1868  1867     0     6     6 5904 5904  0 297 298
```

Observations:

- X is identical through this window.
- Some rows already mismatch Y by 1px BEFORE the dual drop (e.g. sim=1848..1851 show dy=-1: Mesen 297 vs PF 298, etc.).
- Some rows match exactly during the same dual run (sim=1852..1857).
- After single portal at sim=1858, the pattern alternates between dy=0 and dy=-1.
- This rules out "the 1px appears the instant the portal flips dual=0" — the pattern is already present during dual mode and just persists.

Need to dig into: PF frame's raw `Y_fixed`, `Y_lowB`, `CamY_fixed`, `CamY_lowB`, `ScrollYSubpx`, plus Mesen `raw_y`/`scrolly`/`scroll_y_subpx`/`scroll_y_raw` to find which side rounds vs which side carries fractional Y.

Useful refresh command (now that the PF CSV is unstamped, point to the static file or run with FAMIDASH_PF_TRACE if you re-enable FULL):

```powershell
$mes='C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace_Problematic_20260528_005701_830.csv'
$pf ='C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_Problematic_20260528_004400_499.csv'  # or famidash_pf_trace.csv after a new run
$mRows = Get-Content $mes | Select-Object -Skip 1 | ConvertFrom-Csv
$pRows = Import-Csv $pf
$pByFrame = @{}; foreach($p in $pRows){ $pByFrame[[int]$p.frame] = $p }
$rows = @()
foreach($m in $mRows){
  $sim=[int]$m.sim_cursor
  if($sim -lt 1840 -or $sim -gt 1885){ continue }
  $p = $pByFrame[$sim-1]; if($null -eq $p){ continue }
  $raw=[int]$m.raw_y
  $rows += [pscustomobject]@{
    sim=$sim; frame=($sim-1)
    mDual=[int]$m.dual; mMode=[int]$m.gamemode; pMode=[int]$p.mode
    mX=[int]$m.px; pX=[int]$p.X_px; dx=([int]$m.px-[int]$p.X_px)
    mY=[int]$m.py; pY=[int]$p.Y_px; dy=([int]$m.py-[int]$p.Y_px)
    mRawY=('0x{0:X4}' -f $raw); pYfix=$p.Y_fixed; pYlow=$p.Y_lowB
    mRawLow=('0x{0:X2}' -f ($raw -band 255))
    mVel=$m.vel_y; pVel=$p.VelY_fixed
    mGrav=$m.cp_gravity; pGrav=$p.gravFlipped
    mScrollY=$m.scrolly; pCam=$p.CamY_px
    input=$p.input
  }
}
$rows | Format-Table -AutoSize | Out-String -Width 340
```

Note: when comparing the brand-new `famidash_pf_trace.csv` (unstamped), Mesen `px`/`py` are wrapped 32-bit unsigned for negative values; cast via `[int64]` then subtract `4294967296` if `> 2147483647` before subtracting from PF.

---

## Missing PF FULL Traces — Root Cause + Fix Sketch

### What the user observed

"why aren't the detailed PF debug traces with the tmx name generating anymore?"

These are the `%TEMP%\famidash_pf_trace_FULL_<sanitized-tmx-path>_<ts>.log` files. They stopped after `20260528_005628` (May 27 8:56 PM).

### Confirmed cause

`grep` on every `.cs` in the workspace for `PfTrace\.(Open|Enabled|SetFrameContext|Close)` returns **only documentation comments** inside `native-windows/PfTrace.cs` itself. No production code:

- never calls `PfTrace.Open(LevelName)` → `_w` stays null
- never sets `PfTrace.Enabled = true` from caller code
- never calls `PfTrace.SetFrameContext(frame, cur, gm)` → frame context defaults
- never calls `PfTrace.Close()`

So even if a user sets env var `FAMIDASH_PF_TRACE=1` (which does flip `Enabled=true` in the static constructor), `ShouldEmit()` early-returns because `_w == null`. No file is created, nothing is written.

A previous version of `PathfinderEngine.cs` clearly DID call `PfTrace.Open(LevelName)` at engine run-start and `PfTrace.SetFrameContext(...)` per frame — proven by the existence of the older `famidash_pf_trace_FULL_*` files. Those calls were removed by a recent commit/revert (do not git diff per user's instruction, just re-add them).

### Suggested minimal fix

Add the following anchor points, only re-enabling when env var is on so behavior is identical for normal users:

1. **Engine run-start** (top of the public method that performs the main solve / replay loop in `native-windows/PathfinderEngine.cs`). Examples of where this likely lived: just before the main `for` loop in `RunFresh`-style entrypoints, right after `[LEVEL]` / `[RUN_START]` PfLog lines.
   - Call `PfTrace.Open(LevelName)` if `PfTrace.Enabled` is true.

2. **Per-frame**, immediately at the top of `StepFrame(ref SimState s, bool input, out bool endLevel)`:
   - `PfTrace.SetFrameContext(_frameCounter, _dualP2Guard ? 1 : 0, s.GameMode);`
   - `PfTrace.SetSpeculative(_speculativeDepth);`

3. **Engine run-end** (wherever the main run loop exits, plus in any exception/finally):
   - `PfTrace.Close()`.

All `PfTrace.Event` / `PfTrace.State` / `PfTrace.Probe` call sites in `SharedPhysics.cs` are still present and will start writing again the moment `Open()` is called and per-frame context is set.

Do NOT change `PfTrace.cs` itself — it's correct and complete.

### Bonus regression worth flagging

`native-windows/PathfinderEngine.cs` around line 493 currently defines:

```csharp
private readonly string _frameTracePath = System.IO.Path.Combine(
    System.IO.Path.GetTempPath(), "famidash_pf_trace.csv");
```

That's the OLD path — the per-run **stamped** CSV (`famidash_pf_trace_<level>_<ts>.csv`) that previously existed (e.g. `famidash_pf_trace_Problematic_20260528_004400_499.csv`) is no longer produced. If the user wants those back too, mirror the same `<sanitized-level>_<ts>` naming used by `PfTrace`:

```csharp
private string _frameTracePath = System.IO.Path.Combine(
    System.IO.Path.GetTempPath(),
    $"famidash_pf_trace_{SanitizeLogLevelTag(LevelName)}_{PfTraceStamp}.csv");
```

(with `SanitizeLogLevelTag` / `PfTraceStamp` helpers — these existed in an earlier revision and were also rolled back.)

Confirm with the user before doing this second change; they only explicitly asked about the FULL detailed trace.

---

## Problematic Investigation — Source Truth Anchors

### Single Portal Source

`famidash/SAUCE/functions/sprite_loading.h` around line 958:

```c
spcl_sngl_pt:
    if (!activesprites_activated[index]) {
        if (!twoplayer) {
            dual = 0;
            player_y[0] = currplayer_y;
            player_gravity[0] = currplayer_gravity;
            player_vel_y[0] = currplayer_vel_y;
        }
        else { player_gravity[1] = player_gravity[0]; }
        activesprites_activated[index] = 1;
        exitPortalTimer = 10;
    }
    return;
```

`spcl_sngl_pt` copies the CURRENT player's Y/Gravity/VelY into `player_*[0]` immediately during `sprite_collide`, before that current player's movement/collisions for the frame. The current player is P2 at this point.

### P2 Per-frame Order

`famidash/SAUCE/gamestates/state_game.h` around lines 560-680:

1. P1 runs `sprite_collide` → `movement` → `runthecolls()` (with `processXMovement=1`).
2. `do_the_scroll_thing()` runs ONCE after P1 — adjusts both `player0_y` and `player1_y`.
3. P1 state stored back into `player_*[0]`.
4. If `dual`: load P2 from `player_*[1]`.
5. P2 runs `sprite_collide` (this is where `spcl_sngl_pt` may fire and set `dual=0`).
6. P2 runs `movement`.
7. Only `if (dual && ...)` (now FALSE because P2 just hit single): the P2 X sync `currplayer_x = player_x[0]` is **skipped**.
8. P2 sets `processXMovement=0`, runs `runthecolls()`, restores `processXMovement=1`.
9. P2 stored to `player_*[1]`.
10. P1 restored from `player_*[0]` (which was just overwritten by step 5).

Critical: on the single-portal frame, the P2 X sync is skipped because `dual` flipped during step 5. PF must mirror this exactly.

### Scroll Source

`famidash/SAUCE/functions/scroll.h` around lines 58-122:

- `process_y_scroll()` adjusts both `player0_y` and `player1_y` in both cube branch and ship branch.
- `do_the_scroll_thing()` is called exactly once per frame, after P1.
- In dual mode (and wave mode in single), the ship-style branch runs.
- `cap_scroll_y_at_top` / `cap_scroll_y_at_bottom` in `famidash/LIB/asm/nesdash.s` also compensate both players.

---

## Current PF Code Inventory (relevant)

`native-windows/PathfinderEngine.cs`:

- `_dualP2Guard` field around line 395.
- Main `StepFrame` is around lines 9235-9705.
- P2 dual recursion block around line 9507 (`if (s.DualActive && !_dualP2Guard)`).
- Single-portal post-P2 sync at the `[SINGLE_P2_SYNC]` log around line 9685:
  - Currently overwrites P1 with P2's PRE-physics Y/VelY/Gravity.
  - Keeps P1's slope/orb/etc. state.
- Step 7e camera/OOB block is currently inside the shared `StepFrame` body (around line 9305+) and runs for both P1 and P2 unless guarded — VERIFY whether `_dualP2Guard` actually skips it.
- `ApplyPortalsUpTo` around line 12217.
- `ProcessSprites` around line 12381; portal `_dualP2Guard` handling around 12530, 12562.

`native-windows/PfTrace.cs`:

- Status: complete and correct; no caller code anywhere — see "Missing PF FULL Traces" above.

`native-windows/SharedPhysics.cs`:

- Has all `PfTrace.Event(...)` call sites for the major physics sub-routines (`CheckFloor`, `CheckCeiling`, `CheckDeathCollision`, `CheckForwardCollision`, `CheckFloorSpikes`, `GetForwardSlopeNudge`, `SlopeCalc`, `CheckSlopesDown/Up`, `SP.CubeEject`, `SP.BallEject`, `SP.ShipUfoEject`, etc.). These will populate the FULL trace as soon as `PfTrace.Open` is called and per-frame context is set.

---

## Confirmed-Passing Earlier Fixes (DO NOT REVERT)

The user has explicitly verified all of these pass:

1. **Dorabaebasic7** speed portal velocity latch — preserve entry `VelX_fixed` for pre-`x_movement` physics/slope, restore new `speed` for actual X advance. Source: `famidash/SAUCE/functions/sprite_loading.h` + `x_movement.h`.
2. **Cube slope self-correcting blip** — blue gravity pad handler calls `clear_slope_stuff()`; PF clears slope latch/state on blue gravity pad collision, including post-teleport pad rescan path. Source: `famidash/SAUCE/functions/sprite_loading.h`.
3. **Blastprocessing dual wave death** — Famidash runs `do_the_scroll_thing()` once after P1; P2 runs with `processXMovement=0` and no second camera scroll pass; scroll compensation affects both `player0_y` and `player1_y`. User confirmed this passes. NOTE: when last read, PF `StepFrame` still has the Step 7e camera/OOB block visible without an obvious outer `if (!_dualP2Guard)` guard — re-verify whether the guard is actually present elsewhere before assuming the blastprocessing fix is fully in place. If not, the same scroll-double-pass is the prime suspect for the current Problematic divergence too.
4. **Blank Mesen log sets** — earlier attempt to fix via `BeginMesenLogRun()` was reverted by the user because it broke PF logs. Do NOT re-introduce `BeginMesenLogRun` in `MainWindow.MesenOverlay.cs` / `MainWindow.Mesen.cs` / `MainWindow.BuildAndTest.cs`. The repo memory file referencing that fix needs to be marked stale (note: a memory update to flag this as rolled back is pending — do it before storing new repo memories).
5. **P2 path overlay** — `ExportReplayCsv` writes P2 rows; `BuildOverlayLuaScript(includeReplay, drawPathlines)` exists with a no-pathlines sibling; overlay Lua parses `replay2`.

---

## Things Not To Regress

- Speed-portal velocity latch behavior.
- Blue gravity pad slope clearing.
- Portal co-location ordering fixes.
- P2 replay/pathline/no-pathlines Lua generation.
- Do NOT re-introduce `BeginMesenLogRun` (user rolled it back; broke PF logs).
- Do NOT change `PfTrace.cs` itself when re-enabling FULL traces — just add the missing `Open/SetFrameContext/Close` caller hooks.

---

## Suggested Next Investigation Steps

### For "PF FULL traces not generating"

1. Find the engine entrypoint in `native-windows/PathfinderEngine.cs` that runs the main solve loop. Reasonable anchors:
   - `[LEVEL] {LevelName}` PfLog (search `PfLog($"[LEVEL]`).
   - `[RUN_START]` PfLog.
2. Add `if (PfTrace.Enabled) PfTrace.Open(LevelName);` at run-start and `PfTrace.Close();` in the `finally` / end-of-run path.
3. In `StepFrame`, top of function, add:
   ```csharp
   PfTrace.SetFrameContext(_frameCounter, _dualP2Guard ? 1 : 0, s.GameMode);
   PfTrace.SetSpeculative(_speculativeDepth);
   ```
4. Run with `$env:FAMIDASH_PF_TRACE='1'` to confirm files appear in `%TEMP%`.
5. Confirm file naming matches the historical pattern `famidash_pf_trace_FULL_<sanitized-level>_<yyyyMMdd_HHmmss>.log`.

### For "Problematic dual→single 1px Y"

1. Re-pull the focused 1840..1885 window with the helper script above; include `Y_fixed`, `Y_lowB`, `CamY_fixed`, `CamY_lowB`, `ScrollYSubpx`.
2. Search Mesen physics debug around the portal frame:
   ```powershell
   Select-String -Path 'C:\Users\kando\AppData\Local\Temp\famidash_mesen_physics_debug_Problematic_20260528_005701_830.log' `
     -Pattern '1857|1858|sngl|single|portal|scroll|wave|process_y_scroll' -Context 2,2 |
     Select-Object -First 200 | Out-String -Width 280
   ```
3. If PfTrace FULL is back on, grep the new FULL log for `SINGLE_P2_SYNC`, `WAVE_PHYS`, `WAVE_EJECT`, `CAM_FOLLOW`, and scroll deltas around frame 1857.
4. Verify in `PathfinderEngine.cs` whether the Step 7e camera/OOB block is actually guarded by `if (!_dualP2Guard)` — if not, that's likely the cause of the persistent 1px even in single (the blastprocessing fix may have been partial).
5. Verify `[SINGLE_P2_SYNC]` block: source only copies Y/Gravity/VelY into `player_*[0]`; P1's slope/orb/etc. fields remain from `player_*[0]`. PF currently mirrors that. Confirm whether the P2 PRE-physics Y captured (`p2PreY`) is captured AT THE RIGHT MOMENT — specifically, before P2 runs scroll (it shouldn't, because P2 never runs scroll in source).
6. Only patch after the trace + source clearly point to one of: (a) PF doing scroll twice, (b) wrong moment of P2 pre-state capture, (c) X sync semantics when `dual` flips during P2 sprite_collide.

---

## Validation Commands

Build:

```powershell
dotnet build "c:\Editor Test\native-windows\FamidashEditor.csproj" --no-restore /p:SelfContained=false /p:UseAppHost=false
```

Always also use the VS Code `get_errors` tool on any changed files.

Run PF FULL with env var (once Open/SetFrameContext/Close calls are re-added):

```powershell
$env:FAMIDASH_PF_TRACE = '1'
# then trigger the PF run from the editor UI
```

---

## File / Path Inventory

- Workspace: `c:\Editor Test`
- Editor project: `c:\Editor Test\native-windows\FamidashEditor.csproj`
- PF engine: `c:\Editor Test\native-windows\PathfinderEngine.cs`
- PF tracer: `c:\Editor Test\native-windows\PfTrace.cs`
- Shared physics: `c:\Editor Test\native-windows\SharedPhysics.cs`
- Mesen overlay code: `c:\Editor Test\native-windows\MainWindow.MesenOverlay.cs`, `MainWindow.Mesen.cs`, `MainWindow.BuildAndTest.cs`
- Famidash C source: `c:\Editor Test\famidash\SAUCE`
- Famidash ASM: `c:\Editor Test\famidash\LIB\asm`
- Temp logs: `C:\Users\kando\AppData\Local\Temp\famidash_*`
- Old engine snapshots (reference): `c:\Editor Test\old_pfe.cs`, `pfe_prevheld_fix.cs`, `pf_fa67.cs`, `native-windows\PathfinderEngine.cs.githead` (these still have OLD `_frameTracePath = famidash_pf_trace.csv` constant — useful for context, NOT for restoring blindly).

---

## User Preferences / Workflow Constraints

- Preserve exact 1:1 Famidash parity. Confirm behavior from Famidash source AND Mesen/NES logs BEFORE patching.
- Do NOT run `git diff` (user explicitly disallowed during this session).
- Keep patches focused; no formatting passes / no opportunistic refactors.
- Use `replace_string_in_file` (with 3-5 lines of context) or `multi_replace_string_in_file` for edits.
- Don't create markdown docs unless asked. (This handoff was explicitly requested.)
- Use `get_errors` after edits.
- User is running PF/Mesen tests live; don't kick off long builds without confirming.
- Don't commit / push / branch unless asked.

---

## Quick Start For The Next Chat

1. Open `native-windows/PathfinderEngine.cs`, locate engine run entrypoint(s) and `StepFrame`, re-add `PfTrace.Open(LevelName)` + `PfTrace.SetFrameContext(...)` + `PfTrace.Close()` hooks gated by `PfTrace.Enabled`. This restores `famidash_pf_trace_FULL_*` files.
2. Re-run user's Problematic test with `$env:FAMIDASH_PF_TRACE='1'` to get the FULL log for the post-dual wave window.
3. Compare Mesen `sim_cursor=1840..1885` to PF frame `N-1` using the helper script above; include fixed-point Y, low byte, CamY, ScrollYSubpx.
4. Verify whether Step 7e camera/OOB block in `StepFrame` is guarded by `if (!_dualP2Guard)`. If not, that's the most likely cause of the 1px.
5. Patch only after source + trace agree on the exact mechanism.
