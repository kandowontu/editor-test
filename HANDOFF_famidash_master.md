# Famidash Master Handoff

Date: 2026-05-31
Workspace: `c:\Editor Test`

This is the best-effort master handoff for a fresh chat working on Famidash parity, debugging, or maintenance in this workspace. It is meant to be practical, comprehensive, and current as of the end of this session.

Current user-reported status: **"perfection. its all fixed."**

That means a new chat should treat the current codebase as the baseline, preserve behavior, and only change it when a new divergence is confirmed by source + logs.

---

## 1. Standing Directive

The standing rule in this repo is simple:

- PF/sim must be **1:1 with Famidash NES/Mesen**.
- Famidash source in `famidash/SAUCE` is the source of truth.
- If NES behavior looks weird, PF should still match it.
- Do not stop to ask which parity issue to fix next during parity work.
- If a divergence is clear and the source is clear, patch it, validate it, and continue.
- Preserve unrelated user changes in the worktree.

Repo memory anchor:
- `/memories/repo/pf-famidash-parity-directive.md`

---

## 2. Projects and Important Paths

Workspace:
- `c:\Editor Test`

Main projects:
- `c:\Editor Test\native-windows\FamidashEditor.csproj`
- `c:\Editor Test\pf-test\PfTest.csproj`

Main code files:
- `c:\Editor Test\native-windows\PathfinderEngine.cs`
- `c:\Editor Test\native-windows\SharedPhysics.cs`
- `c:\Editor Test\native-windows\SlopeCollision.partial.cs`
- `c:\Editor Test\native-windows\SimulatorWindow.xaml.cs`
- `c:\Editor Test\native-windows\MainWindow.xaml.cs`
- `c:\Editor Test\native-windows\MainWindow.MesenOverlay.cs`
- `c:\Editor Test\native-windows\MainWindow.PathfinderCompare.cs`
- `c:\Editor Test\native-windows\PfTrace.cs`

Famidash source of truth:
- `c:\Editor Test\famidash\SAUCE`
- `c:\Editor Test\famidash\LIB\asm`

Useful existing handoffs in workspace:
- `c:\Editor Test\HANDOFF_problematic_dual_single_wave.md`
- `c:\Editor Test\HANDOFF_slope_mechanics.md`
- `c:\Editor Test\HANDOFF_famidash_master.md` (this file)

---

## 3. Build and Validation Commands

Main editor build:

```powershell
dotnet build "c:\Editor Test\native-windows\FamidashEditor.csproj" --no-restore /p:SelfContained=false /p:UseAppHost=false
```

PF harness build:

```powershell
cd "c:\Editor Test\pf-test"
dotnet build --no-restore
```

Single-level PF harness run pattern:

```powershell
cd "c:\Editor Test\pf-test"
dotnet run -c Release --no-build -- "c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\theoryofeverything.tmx"
```

Parallel regression harness:
- `c:\Editor Test\pf-test\run-parallel.ps1`

Important validation habits:
- Always run `get_errors` on edited files.
- Prefer behavior-scoped validation over broad exploration.
- Use latest **non-empty** temp logs.
- If build fails with `MSB3021` / `MSB3027`, close the running editor process and rebuild.

---

## 4. Core Truths About This Codebase

### 4.1 Source before patch

Before changing PF behavior, verify the exact rule in Famidash source:

High-value source files:
- `famidash/SAUCE/gamestates/state_game.h`
- `famidash/SAUCE/functions/scroll.h`
- `famidash/SAUCE/functions/x_movement.h`
- `famidash/SAUCE/functions/sprite_loading.h`
- `famidash/SAUCE/functions/collision.h`
- `famidash/SAUCE/gamemodes/gamemode_cube.h`
- `famidash/SAUCE/gamemodes/gamemode_ship.h`
- `famidash/SAUCE/gamemodes/gamemode_ufo.h`
- `famidash/SAUCE/gamemodes/gamemode_ball.h`
- `famidash/SAUCE/gamemodes/gamemode_wave.h`
- `famidash/LIB/asm/nesdash.s`

### 4.2 Mesen is the arbiter of runtime truth

When PF and source interpretation disagree, the actual Mesen trace is the tiebreaker.

### 4.3 Preserve quirks

Do not “improve”:
- camera oddities
- scroll quirks
- portal ordering weirdness
- odd slope/eject formulas
- off-by-one looking behavior

If NES does it, PF should do it.

---

## 5. Trace and Logging Conventions

### 5.1 Temp artifacts

Main temp output family lives in `%TEMP%`, usually:
- `C:\Users\kando\AppData\Local\Temp`

Main artifact types:
- `famidash_pf_debug_<level>_<ts>.txt`
- `famidash_pf_orb_debug_<level>_<ts>.log`
- `famidash_pf_trace_<level>_<ts>.csv`
- `famidash_pf_trace_FULL_<level>_<ts>.log`
- `famidash_mesen_trace_<level>_<ts>.csv`
- `famidash_mesen_orb_debug_<level>_<ts>.log`
- `famidash_mesen_physics_debug_<level>_<ts>.log`

### 5.2 Cursor alignment rule

This is critical and easy to get wrong:

- Mesen Lua replay is **1-indexed**.
- Mesen `sim_cursor = N` compares to PF `frame = N - 1`.
- `a_cur` corresponds to PF input at frame `N - 1`.
- `a_next` corresponds to PF input at frame `N`.

Repo memory anchor:
- `/memories/repo/famidash-mesen-trace-cursor-indexing.md`

### 5.3 Coordinate anti-patterns

Do not compare the wrong coordinate spaces.

Bad comparison:
- PF `Y_fixed.low` vs NES `raw_y.low`

Good comparison:
- PF `(Y_fixed - CamY_fixed).low` vs NES `raw_y.low`

Also:
- `Y_px` may show cosmetic ±1 drift due to rasterization or display conversion.
- Real physics comparison should prefer fixed-point state.

Repo memory anchor:
- `/memories/repo/pf-nes-parity-validation.md`

### 5.4 FULL trace status

PF FULL trace and stamped PF logs were restored. Current expectation is that the engine emits:

- `pf_debug`
- `pf_orb_debug`
- `pf_trace`
- `pf_trace_FULL`

Repo memory anchor:
- `/memories/repo/pf-full-trace-restoration-sharedphysics.md`

Also note a prior bug:
- constructor-time logging could stamp a generic/no-level debug path before `LevelName` was assigned.
- current path stamping is intended to be lazy/per-run so logs carry the level name.

Repo memory anchor:
- `/memories/repo/pf-log-levelname-cache-bug.md`

---

## 6. Current High-Level Architecture

### 6.1 PathfinderEngine.cs

This is the main PF runtime.

Important responsibilities:
- stepping one frame of physics
- mode-specific movement
- sprite/portal/orb/pad handling
- trace emission
- dual recursion / P2 handling
- replay/path export
- camera/scroll integration

Most important local anchors:
- `StepFrame(...)`
- `ProcessSprites(...)`
- mode-specific movement helpers (`ShipGravityAndThrust`, etc.)
- dual recursion via `_dualP2Guard`
- trace open/write/close logic

### 6.2 SharedPhysics.cs

This is where most reusable collision/eject logic lives.

Important responsibilities:
- floor / ceiling collision
- death checks
- slopes
- cube/ball/ship/ufo eject logic
- forward slope nudges
- shared tracing hooks

If parity breaks in collision/eject logic, the fix is often here.

### 6.3 SIM mirror path

SIM logic is split across:
- `SlopeCollision.partial.cs`
- `SimulatorWindow.xaml.cs`
- other simulator-specific files

When a rule changes semantically, PF and SIM usually both need the same parity fix.

---

## 7. Critical Invariants by System

## 7.1 Speed portals

Rule:
- speed portals write global `speed`
- they do **not** immediately update `currplayer_vel_x`
- pre-`x_movement` physics still uses frame-entry `VelX`
- later X advance uses the updated speed

PF rule:
- keep pre-`x_movement` physics/eject/slope velocity on entry `VelX_fixed`
- compute final X advance using the new speed

Repo memory anchor:
- `/memories/repo/famidash-speed-portal-velx-latch.md`

## 7.2 Wave sprite-collide hitbox

Wave mode is special in sprite collision:
- `hbW = 8`
- `hbH = 8`
- `hbOffY = 4`

This must be wave-only.

Repo memory anchor:
- `/memories/repo/pf-wave-sprite-collide-hitbox.md`

## 7.3 Slopes

Slope rules are fragile.

Absolute invariants:
- use inclusive slope range upper bound `COL_SLOPE_LU66_TOP`
- do not accidentally use `COL_SLOPE_LU66_BOT` as the inclusive upper bound in C#
- cube-family flipped-gravity ceiling-slope eject is **not** ship-style eject
- 66-degree sentinel slope hits must not mutate normal slope state
- SIM direction-rejected slope probes must rollback temporary state writes
- wave slope checks are death checks, not ordinary floor nudges

See dedicated handoff:
- `c:\Editor Test\HANDOFF_slope_mechanics.md`

Repo memory anchor:
- `/memories/repo/famidash-slope-fixes.md`

## 7.4 Orb ordering

When multiple overlapping orbs are active simultaneously:
- PF behavior is effectively “last wins” by scan order
- SIM had to be fixed to match that

Repo memory anchor:
- `/memories/repo/sim-pf-orb-ordering.md`

## 7.5 Blue gravity pad slope clearing

Blue gravity pads must clear slope latch/state to match `clear_slope_stuff()` semantics.

Historical regression signature:
- cube/robot slope blips or self-correcting Y weirdness right after blue pads

## 7.6 Dual and single portals

Portal ordering matters, especially when co-located with gamemode portals.

Important truths:
- active sprite slot order matters
- dual/single + gamemode portals can overlap and must apply in the same order as NES
- single portal behavior during P2 processing is especially sensitive
- P2 state capture and restore order matter

Repo memory anchors:
- `/memories/repo/pf-dual-portal-order-target.md`
- `/memories/repo/pf-gamemode-portal-order.md`
- `/memories/repo/pf-gamemode-portal-reset-parity.md`
- `/memories/repo/sngl-portal-no-target.md`

## 7.7 Camera and scroll

Important truths:
- `process_y_scroll()` and its subpixel behavior matter
- nametable-target distortion matters
- bottom cap strictness matters
- scroll subpixel and low-byte borrow behavior matter
- in dual, scroll behavior affecting both players matters

Repo memory anchors:
- `/memories/repo/camera-nametable-distortion.md`
- `/memories/repo/famidash-nt-camera-target.md`
- `/memories/repo/famidash-process-y-scroll-subpx.md`
- `/memories/repo/famidash-process-y-scroll-clamp-gate.md`
- `/memories/repo/pf-camera-bottom-cap-subpx.md`
- `/memories/repo/pf-cap-scroll-y-bottom-strict.md`
- `/memories/repo/pf-scroll-subpx-borrow.md`

---

## 8. Major Historical Fixes Already Landed

This section is not exhaustive, but these are important because future chats must avoid regressing them.

1. Speed portal VelX latch parity.
2. Blue gravity pad slope-state clear parity.
3. Orb overlap ordering parity.
4. Slope range upper-bound parity.
5. Sentinel slope side-effect suppression.
6. Wave sprite-collide hitbox parity.
7. Dual/P2 portal ordering parity.
8. FULL PF trace restoration.
9. Stamped PF log path restoration.
10. TheoryOfEverything end-section mini-ship parity fix in ship/UFO eject handling.

### 8.1 Latest TOE fix

Latest relevant resolved issue:
- `theoryofeverything` diverged late in mini-ship.
- root cause was in the ship/UFO eject path.
- PF’s ship/UFO-specific branch was not matching NES `ufo_ship_eject()` semantics.
- source truth: `gamemode_ship.h` does `common_gravity_routine()` then `ufo_ship_eject()`.
- `ufo_ship_eject()` always does `bg_coll_U()` then `bg_coll_D()` with raw eject semantics.

Current intended parity rule for ship/UFO path:
- always perform U then D checks in the ship/UFO eject path
- preserve low byte when changing high-byte world Y
- use raw `eject_U/eject_D` semantics instead of geometry-snap shortcuts
- do not gate that special path incorrectly by gravity branch ordering

Repo memory anchors:
- `/memories/repo/toe-mini-ship-common-gravity-collision-branch.md`
- `/memories/repo/famidash-pf-shipufoeject-slopetype-propagation.md`
- `/memories/repo/toe-mini-ufo-ceiling-eject-mismatch.md`

### 8.2 Historical Problematic wave/dual notes

There is also a dedicated historical handoff for the `problematic.tmx` dual-to-single wave work:
- `c:\Editor Test\HANDOFF_problematic_dual_single_wave.md`

That file contains the earlier trace windows, source anchors, and investigation history.

---

## 9. Debugging Workflow For New Chats

Use this order:

1. Identify the first real divergence in logs.
2. Verify frame alignment first.
3. Compare fixed-point state, not just display pixels.
4. Read the exact Famidash source routine for that behavior.
5. Find the owning PF abstraction.
6. Make the smallest possible edit.
7. Validate immediately.
8. Only then expand scope if the hypothesis was wrong.

### 9.1 What to compare first

For most divergences, compare:
- `X_fixed`
- `Y_fixed`
- `VelY_fixed`
- mode / gravity / mini
- camera Y fixed and low byte
- slope state (`SlopeType`, `LastSlopeType`, `SlopeFrames`, `SlopeWasOnCounter`)
- collision hit flags
- orb / portal events

### 9.2 Known anti-patterns

Do not:
- compare the wrong coordinate spaces
- trust `Y_px` alone for root cause
- assume input timing is wrong before state alignment is proven wrong
- apply “cleanup” refactors in parity code
- mix slope, portal, and camera changes in the same patch unless the source proves they are one mechanism

---

## 10. SIM-Specific Guidance

SIM is not just a viewer. It has mirrored collision behavior that also needs parity.

When PF slope/collision logic changes, audit SIM mirrors in:
- `native-windows/SlopeCollision.partial.cs`
- `native-windows/SimulatorWindow.xaml.cs`

Common SIM regression areas:
- slope upper bound range
- wave slope death checks
- orb activation ordering
- rollback of temporary slope state on rejected probes

---

## 11. Logging, Overlay, and Replay Support

Important capabilities currently expected to work:

- PF stamped trace/debug/orb/FULL logs
- Mesen overlay Lua generation
- replay CSV export
- P2 replay rows/pathline support during dual
- compare logic using `sim_cursor - 1`

Files to inspect when logging/overlay behavior regresses:
- `native-windows/MainWindow.MesenOverlay.cs`
- `native-windows/MainWindow.Mesen.cs`
- `native-windows/MainWindow.BuildAndTest.cs`
- `native-windows/MainWindow.xaml.cs`
- `native-windows/MainWindow.PathfinderCompare.cs`
- `native-windows/PfTrace.cs`
- `native-windows/PathfinderEngine.cs`

---

## 12. Existing Workspace Handoffs

These are worth reading before reopening old investigations:

- `c:\Editor Test\HANDOFF_problematic_dual_single_wave.md`
- `c:\Editor Test\HANDOFF_slope_mechanics.md`
- `c:\Editor Test\HANDOFF_famidash_master.md`

---

## 13. Repo Memory Index By Theme

This section is effectively the extended knowledge base for future chats.

### 13.1 Core parity / workflow

- `/memories/repo/famidash-parity-master-handoff.md`
- `/memories/repo/pf-famidash-parity-directive.md`
- `/memories/repo/pf-nes-parity-validation.md`
- `/memories/repo/famidash-mesen-trace-cursor-indexing.md`
- `/memories/repo/pf-full-trace-restoration-sharedphysics.md`
- `/memories/repo/pf-log-levelname-cache-bug.md`

### 13.2 Camera / scroll / subpixel

- `/memories/repo/camera-nametable-distortion.md`
- `/memories/repo/famidash-nt-camera-target.md`
- `/memories/repo/famidash-process-y-scroll-clamp-gate.md`
- `/memories/repo/famidash-process-y-scroll-subpx.md`
- `/memories/repo/pf-cam-cap-bot-subpx-loss.md`
- `/memories/repo/pf-camera-bottom-cap-subpx.md`
- `/memories/repo/pf-cap-scroll-y-bottom-strict.md`
- `/memories/repo/pf-scroll-cap-bottom-strict.md`
- `/memories/repo/pf-scroll-subpx-borrow.md`

### 13.3 Slopes / eject / collision

- `/memories/repo/famidash-slope-fixes.md`
- `/memories/repo/sharedphysics-slope-eject-low-byte.md`
- `/memories/repo/ship-spurious-slope-detection.md`
- `/memories/repo/ship-mini-block-ejectD.md`
- `/memories/repo/famidash-pf-shipufoeject-slopetype-propagation.md`
- `/memories/repo/famidash-ufo-slope-vel-order.md`
- `/memories/repo/pf-checkceiling-col-bottom.md`
- `/memories/repo/pf-cube-eject-gravf-spike-discard.md`
- `/memories/repo/famidash-cube-floor-eject-divergence.md`
- `/memories/repo/famidash-balleject-oldx.md`
- `/memories/repo/pf-ball-eject-diag-logging.md`
- `/memories/repo/ball-eject-formula-port-failure.md`
- `/memories/repo/ball-eject-gravf-d-gate.md`
- `/memories/repo/ball-eject-screen-vs-world-y.md`
- `/memories/repo/ball-eject-tmp8-partial.md`
- `/memories/repo/pf-exit-slope-ntsc-bit.md`
- `/memories/repo/pf-nes-probe-y-formula.md`
- `/memories/repo/pf-nes-player-y-borrow-bug.md`
- `/memories/repo/nes-y-probe-borrow.md`
- `/memories/repo/stereomadness-cube-centerprobe-y-borrow.md`
- `/memories/repo/forward-slope-nudge-wedge-gate.md`
- `/memories/repo/famidash-ball-forward-slope-nudge-reenable.md`
- `/memories/repo/sim-slope-type-side-effect.md`

### 13.4 Portals / dual / gamemode / gravity

- `/memories/repo/pf-dual-portal-order-target.md`
- `/memories/repo/pf-gamemode-portal-order.md`
- `/memories/repo/pf-gamemode-portal-reset-parity.md`
- `/memories/repo/sngl-portal-no-target.md`
- `/memories/repo/famidash-pf-ufo-portal-divergence.md`
- `/memories/repo/pf-ufo-portal-transition-no-jump.md`
- `/memories/repo/pf-gravity-portal-iteration-order.md`
- `/memories/repo/pf-gravity-portal-per-frame-skip.md`
- `/memories/repo/pf-gravity-trigger-lookahead.md`
- `/memories/repo/exit-portal-timer-cube-refresh.md`
- `/memories/repo/silentcircles-cube-portal-divergence-fix.md`
- `/memories/repo/silentcircles-wave-portal-entry-velx-order.md`

### 13.5 Orbs / pads / sprite interactions

- `/memories/repo/orb-multi-tile-fix.md`
- `/memories/repo/sim-pf-orb-ordering.md`
- `/memories/repo/pf-orb-airpress-latch-gate.md`
- `/memories/repo/pf-orb-formula-DO-NOT-CHANGE.md`
- `/memories/repo/pf-orb-prescan-gravity-sign.md`
- `/memories/repo/blue-pad-activation-gate.md`
- `/memories/repo/blue-pad-stacked-double-flip.md`
- `/memories/repo/pf-blue-pad-generic2-probe.md`

### 13.6 Wave / ship / robot / special modes

- `/memories/repo/pf-wave-sprite-collide-hitbox.md`
- `/memories/repo/famidash-wave-generic-height-quirk.md`
- `/memories/repo/wave-eject-col-death-family.md`
- `/memories/repo/wave-eject-spike-classes-checkceiling-floor.md`
- `/memories/repo/wave-generic-y-line41-update.md`
- `/memories/repo/wave-mode-bg-coll-death-spike.md`
- `/memories/repo/wave-coll-d-quirk-probe3.md`
- `/memories/repo/wave-coll-u-quirk-probe3.md`
- `/memories/repo/famidash-robot-physics-order.md`
- `/memories/repo/famidash-ball-held-vs-press-flip.md`
- `/memories/repo/toe-mini-ship-common-gravity-collision-branch.md`
- `/memories/repo/toe-mini-ufo-ceiling-eject-mismatch.md`
- `/memories/repo/thechallenge-ship-cam-drift.md`
- `/memories/repo/leveleasy-ship-ceiling-eject-divergence.md`
- `/memories/repo/shardscapes-airpresslatch-bfs-blocker.md`

### 13.7 Specific level investigations / residual drift

- `/memories/repo/famidash-pf-residual-1px-drift.md`
- `/memories/repo/famidash-pf-subpx-drift-slope-trigger.md`
- `/memories/repo/darkparadise-divergence-findings.md`
- `/memories/repo/db6-ball-flip-subpx-divergence.md`
- `/memories/repo/cataclysm-cube-f108-phantom-ceiling.md`
- `/memories/repo/cataclysm-pf-mt-rate-mismatch.md`
- `/memories/repo/pf-nes-f2398-floor-1px.md`
- `/memories/repo/pf-spawn-fix-followup-divergences.md`
- `/memories/repo/pf-spawn-settle-fix.md`
- `/memories/repo/pf-spawn-y-fix-was-wrong.md`
- `/memories/repo/pf-spawn-y-formula-fix.md`

### 13.8 Misc engine / trace / asset notes

- `/memories/repo/famidash-ntsc-table-constants.md`
- `/memories/repo/famidash-sprite-pyoff-anchor.md`
- `/memories/repo/pf-nes-sprite-gamemode-adjust-heights.md`
- `/memories/repo/pf-trigger-sprite-anchor-shift.md`
- `/memories/repo/pf-freecam-trigger-x-only.md`
- `/memories/repo/pf-sprite-collide-y-decomp.md`
- `/memories/repo/sharedphysics-tile-id-mapping-bug.md`
- `/memories/repo/mesen-trace-attempt-segmentation.md`
- `/memories/repo/mesen2wide-bundle-source-path.md`
- `/memories/repo/mesen2wide-wsedge-runtime-read.md`
- `/memories/repo/nes-sprite-slot-allocation.md`
- `/memories/repo/sim-pf-duplicate-camera-follow-desync.md`
- `/memories/repo/sim-pf-portal-hitbox-image-expansion.md`
- `/memories/repo/sim-teleport-cpy-low-preserve.md`
- `/memories/repo/everyend-bfs-rewind-reexpand.md`
- `/memories/repo/everyend-lua-cursor-bug.md`
- `/memories/repo/col-top-center-spike-hitbox.md`

### 13.9 Non-Famidash or adjacent project memories present in repo scope

These exist but are not the core of current Famidash parity work:
- `/memories/repo/sf2-aftercatabath-ship-death-fix.md`
- `/memories/repo/sf2-cp1252-encoding.md`
- `/memories/repo/sf2-dos-8dot3-filename-collision.md`
- `/memories/repo/sf2-mariochip-softlock.md`
- `/memories/repo/sf2-path-bank-alignment.md`
- `/memories/repo/sf2-paths3-expansion-constraint.md`
- `/memories/repo/sf2-title-gsu-hang.md`
- `/memories/repo/sf2-titledemo-crossbank-gosub.md`
- `/memories/repo/sf2-xbank-gosub-trigger.md`
- `/memories/repo/pf-release-binary-port-2026.md`
- `/memories/repo/dll-full-replace-decompiler-tuple-fix.md`

---

## 14. Practical Instructions For A Fresh Chat

If starting cold:

1. Read this file.
2. Read `HANDOFF_slope_mechanics.md`.
3. Read `HANDOFF_problematic_dual_single_wave.md` if touching historical dual/wave work.
4. Read `/memories/repo/famidash-parity-master-handoff.md` and `/memories/repo/pf-famidash-parity-directive.md`.
5. Before editing, identify the exact owning source routine in `famidash/SAUCE`.
6. Validate alignment first, then patch the smallest owning PF/SIM routine.

---

## 15. Final Summary

As of this handoff:
- the user reports everything important is fixed
- the repo contains a large amount of parity knowledge in repo memory files
- the current code should be treated as the working baseline
- future chats should focus on **source-confirmed, minimal, local** parity maintenance rather than broad rewrites

If a future regression appears, assume the source is right, the logs are right, and the first task is to find the smallest behavior-controlling branch that drifted.
# Famidash Master Handoff

Date: 2026-05-31
Workspace: `c:\Editor Test`

This is the master cold-start handoff for a new chat session working on Famidash PF/SIM parity.

Current status at the end of this session:
- User confirmed the latest parity work is fixed.
- TheoryOfEverything late mini-ship divergence is fixed.
- PF stamped logs and FULL traces are restored.
- Slope mechanics handoff exists separately and should be treated as authoritative for slope work.

This document is the broad overview. Use it first, then drill into the specialized handoffs and repo memory notes.

---

## 1. Standing Directive

The goal is strict 1:1 parity with Famidash NES/Mesen behavior.

Rules:
- Treat `famidash/SAUCE` and NES/Mesen traces as source of truth.
- Reproduce quirks exactly, even when the behavior looks wrong.
- Do not ask the user which parity issue to fix next. Investigate, patch, validate, continue.
- Do not revert unrelated dirty-worktree changes.
- Keep fixes narrow and source-confirmed.

Primary memory note:
- `/memories/repo/pf-famidash-parity-directive.md`

---

## 2. Workspace Map

Main paths:
- Workspace root: `c:\Editor Test`
- Main WPF/editor project: `c:\Editor Test\native-windows\FamidashEditor.csproj`
- Main engine: `c:\Editor Test\native-windows\PathfinderEngine.cs`
- Shared physics helpers: `c:\Editor Test\native-windows\SharedPhysics.cs`
- SIM path: `c:\Editor Test\native-windows\SimulatorWindow.xaml.cs`
- SIM slope path: `c:\Editor Test\native-windows\SlopeCollision.partial.cs`
- PF harness: `c:\Editor Test\pf-test\PfTest.csproj`
- Famidash source: `c:\Editor Test\famidash\SAUCE`
- Famidash ASM: `c:\Editor Test\famidash\LIB\asm`
- Temp logs: `%TEMP%`, usually `C:\Users\kando\AppData\Local\Temp`

High-value source files:
- `famidash/SAUCE/gamestates/state_game.h`
- `famidash/SAUCE/functions/scroll.h`
- `famidash/SAUCE/functions/x_movement.h`
- `famidash/SAUCE/functions/sprite_loading.h`
- `famidash/SAUCE/functions/collision.h`
- `famidash/SAUCE/gamemodes/gamemode_cube.h`
- `famidash/SAUCE/gamemodes/gamemode_ship.h`
- `famidash/SAUCE/gamemodes/gamemode_ufo.h`
- `famidash/SAUCE/gamemodes/gamemode_ball.h`
- `famidash/SAUCE/gamemodes/gamemode_wave.h`
- `famidash/LIB/asm/nesdash.s`

---

## 3. Build and Validation Workflow

Main build:
```powershell
dotnet build "c:\Editor Test\native-windows\FamidashEditor.csproj" --no-restore /p:SelfContained=false /p:UseAppHost=false
```

PF harness build:
```powershell
cd "c:\Editor Test\pf-test"
dotnet build --no-restore
```

Single-level PF harness run pattern:
```powershell
cd "c:\Editor Test\pf-test"
dotnet run --configuration Release -- "c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\<level>.tmx"
```

Notes:
- Always run `get_errors` after edits.
- If build fails with `MSB3021` or `MSB3027`, the editor DLL is locked by a running process. Close the process and rebuild.
- For diagnostics, non-stripped/debug-oriented runs are easier because some logging is behind debug flags.

---

## 4. Trace and Log System

Current PF artifacts:
- `famidash_pf_debug_<level>_<ts>.txt`
- `famidash_pf_orb_debug_<level>_<ts>.log`
- `famidash_pf_trace_<level>_<ts>.csv`
- `famidash_pf_trace_FULL_<level>_<ts>.log`

Current Mesen artifacts:
- `famidash_mesen_trace_<level>_<ts>.csv`
- `famidash_mesen_orb_debug_<level>_<ts>.log`
- `famidash_mesen_physics_debug_<level>_<ts>.log`

PF FULL trace status:
- Restored by wiring SharedPhysics FULL callbacks through the engine.
- SharedPhysics FULL hooks cover at least: `CheckFloor`, `CheckFloorDetailed`, `CheckCeiling`, `CheckCenterPointDeath`, `CheckDeathCollision`, `CheckFloorSpikes`, `CheckForwardCollision`, `GetForwardSlopeNudge`, `SlopeCalc`, `CheckSlopesDown`, `CheckSlopesUp`, `CubeEject`, `BallEject`, `ShipUfoEject`.

Relevant memory notes:
- `/memories/repo/pf-full-trace-restoration-sharedphysics.md`
- `/memories/repo/pf-log-levelname-cache-bug.md`

PF trace environment switch:
```powershell
$env:FAMIDASH_PF_TRACE = '1'
```

---

## 5. Critical Trace Alignment Rules

This is the most important comparison rule in the repo:
- Mesen Lua replay cursor is 1-based.
- Compare Mesen `sim_cursor=N` to PF frame `N-1`.
- `a_cur` on the Mesen row is PF input for frame `N-1`.
- `a_next` is PF input for frame `N`.

Do not use any other alignment basis until disproven by direct state matching.

Memory note:
- `/memories/repo/famidash-mesen-trace-cursor-indexing.md`

Also:
- Do not compare PF world-space low byte directly to NES screen-space low byte.
- The physically meaningful compare is PF `(Y_fixed - CamY_fixed)` versus NES raw screen-space/player trace fields.
- Display-only `Y_px` differences can mislead. Full fixed-point comparisons are more trustworthy.

Memory note:
- `/memories/repo/pf-nes-parity-validation.md`

---

## 6. Core Parity Rules by Subsystem

### 6.1 Speed portals
- In source, speed portals update `speed`, not immediate `currplayer_vel_x`.
- `x_movement_coll()` and slope/physics that occur before `x_movement()` still use frame-entry `VelX`.
- `x_movement()` later reloads `currplayer_vel_x` from speed tables and uses the new speed for X advance.
- PF must preserve this split exactly.

Memory note:
- `/memories/repo/famidash-speed-portal-velx-latch.md`

### 6.2 Blue gravity pad and slope clearing
- Blue gravity pad behavior must clear slope state exactly like source `clear_slope_stuff()`.
- This includes post-teleport pad rescan paths when a pad actually dispatches.

Related memory notes:
- `/memories/repo/blue-pad-activation-gate.md`
- `/memories/repo/blue-pad-stacked-double-flip.md`
- `/memories/repo/pf-blue-pad-generic2-probe.md`

### 6.3 Portals and dual mode
- Overlapping dual/single/gamemode portal order matters.
- Active-sprite slot order in source determines the actual behavior.
- Dual P2 recursion in PF uses `_dualP2Guard` and must not double-run camera/scroll paths.
- `state_game.h` is the authority for P1/P2 order, store/restore, and when scroll runs.
- `scroll.h` is the authority for the “scroll once after P1, apply both-player Y compensation” rule.

Key memories:
- `/memories/repo/pf-dual-portal-order-target.md`
- `/memories/repo/pf-gamemode-portal-order.md`
- `/memories/repo/pf-gamemode-portal-reset-parity.md`
- `/memories/repo/pf-ufo-portal-transition-no-jump.md`
- `/memories/repo/sngl-portal-no-target.md`

### 6.4 Camera and scroll
- `process_y_scroll()` and scroll/cap logic preserve weird NES subpixel/cap behavior.
- Camera target and nametable distortion quirks are real and must be mirrored.
- Patches that only change displayed Y without checking `Y_fixed - CamY_fixed` are suspect.

Key memories:
- `/memories/repo/camera-nametable-distortion.md`
- `/memories/repo/famidash-nt-camera-target.md`
- `/memories/repo/famidash-process-y-scroll-subpx.md`
- `/memories/repo/famidash-process-y-scroll-clamp-gate.md`
- `/memories/repo/pf-camera-bottom-cap-subpx.md`
- `/memories/repo/pf-cap-scroll-y-bottom-strict.md`
- `/memories/repo/pf-scroll-cap-bottom-strict.md`
- `/memories/repo/pf-scroll-subpx-borrow.md`

### 6.5 Wave mode
- Wave has its own sprite-collide hitbox in source.
- Wave and snake slope/death behavior must be treated specially.
- Wave collision quirks are not interchangeable with cube/ship routines.

Known wave rule:
- In `ProcessSprites`, wave mode must use width 8, height 8, offsetY 4.

Key memories:
- `/memories/repo/pf-wave-sprite-collide-hitbox.md`
- `/memories/repo/famidash-wave-generic-height-quirk.md`
- `/memories/repo/wave-eject-col-death-family.md`
- `/memories/repo/wave-mode-bg-coll-death-spike.md`
- `/memories/repo/wave-eject-spike-classes-checkceiling-floor.md`
- `/memories/repo/wave-coll-d-quirk-probe3.md`
- `/memories/repo/wave-coll-u-quirk-probe3.md`
- `/memories/repo/wave-generic-y-line41-update.md`

### 6.6 Orbs and pads
- Orb ordering matters. When multiple overlapping orbs are possible, use source ordering, not “first match” assumptions.
- Do not conflate activation order, pending-orb latch behavior, and post-activation movement.

Key memories:
- `/memories/repo/sim-pf-orb-ordering.md`
- `/memories/repo/orb-multi-tile-fix.md`
- `/memories/repo/pf-orb-airpress-latch-gate.md`
- `/memories/repo/pf-orb-formula-DO-NOT-CHANGE.md`
- `/memories/repo/pf-orb-prescan-gravity-sign.md`

### 6.7 Slopes
- Inclusive slope range upper bound in C# must include `COL_SLOPE_LU66_TOP`.
- Cube-family flipped ceiling-slope eject is not ship eject.
- Sentinel 66-degree slope hits must not write normal slope side effects.
- Direction-rejected slope checks must roll back temporary slope-state writes.
- Wave slope paths are death checks, not generic slope nudges.

Authoritative specialized handoff:
- `c:\Editor Test\HANDOFF_slope_mechanics.md`

Primary memory note:
- `/memories/repo/famidash-slope-fixes.md`

Additional slope-related memories:
- `/memories/repo/sharedphysics-slope-eject-low-byte.md`
- `/memories/repo/sim-slope-type-side-effect.md`
- `/memories/repo/famidash-ball-forward-slope-nudge-reenable.md`
- `/memories/repo/famidash-ufo-slope-vel-order.md`
- `/memories/repo/forward-slope-nudge-wedge-gate.md`
- `/memories/repo/pf-exit-slope-ntsc-bit.md`
- `/memories/repo/ship-spurious-slope-detection.md`

### 6.8 Ship and UFO
- Source `gamemode_ship.h` flow is `common_gravity_routine()` followed by `ufo_ship_eject()`.
- `ufo_ship_eject()` always runs `bg_coll_U()` and then `bg_coll_D()`; this is not optional based on gravity direction.
- Ship/UFO eject preserves low byte and applies raw high-byte ejection semantics.
- Ceiling/floor ordering and slope-type propagation can cause long cascaded divergence if wrong by one frame.

Recent major ship/UFO fixes and notes:
- `ShipUfoEject` slope-type propagation bug fixed earlier.
- TOE late mini-ship divergence fixed in this session by tightening the ship/UFO path to source-parity `U then D` eject semantics.
- Historical memory notes around TOE still describe the investigation phase; use them as background, but the user confirmed the issue is now fixed.

Relevant memories:
- `/memories/repo/famidash-pf-shipufoeject-slopetype-propagation.md`
- `/memories/repo/toe-mini-ship-common-gravity-collision-branch.md`
- `/memories/repo/toe-mini-ufo-ceiling-eject-mismatch.md`
- `/memories/repo/leveleasy-ship-ceiling-eject-divergence.md`
- `/memories/repo/thechallenge-ship-cam-drift.md`
- `/memories/repo/ship-mini-block-ejectD.md`

### 6.9 Ball, robot, cube
- Ball eject and cube eject routines have their own source-specific ordering and screen/world-space quirks.
- Robot and ball issues often masquerade as generic camera/subpixel problems but are usually wrong routine ordering, wrong low-byte handling, or wrong table selection.

Relevant memories:
- `/memories/repo/famidash-cube-floor-eject-divergence.md`
- `/memories/repo/famidash-balleject-oldx.md`
- `/memories/repo/ball-eject-formula-port-failure.md`
- `/memories/repo/ball-eject-gravf-d-gate.md`
- `/memories/repo/ball-eject-screen-vs-world-y.md`
- `/memories/repo/ball-eject-tmp8-partial.md`
- `/memories/repo/famidash-ball-held-vs-press-flip.md`
- `/memories/repo/famidash-robot-physics-order.md`

---

## 7. Important Resolved Issues

These are high-signal fixes already learned in this repo:
- Dorabaebasic7 speed-portal velocity latch parity.
- Blue gravity pad slope-clear parity.
- Wave sprite-collide hitbox parity.
- PF FULL trace restoration and stamped PF log restoration.
- Ship/UFO slope-type propagation parity.
- TheoryOfEverything late mini-ship end-section divergence fix.
- Numerous camera/subpixel/cap/borrow corrections in PF.

When a future divergence looks similar, search repo memory before changing code.

---

## 8. Specialized Handoffs Already in Workspace

Use these as deep dives:
- `c:\Editor Test\HANDOFF_slope_mechanics.md`
- `c:\Editor Test\HANDOFF_problematic_dual_single_wave.md`

The master handoff is broad; these are deeper on their respective topics.

---

## 9. Fresh-Chat Workflow

When a new chat starts on parity work:
1. Read this file first.
2. Read `/memories/repo/pf-famidash-parity-directive.md`.
3. Read `/memories/repo/famidash-mesen-trace-cursor-indexing.md`.
4. List latest non-empty temp logs for the affected level.
5. Compare Mesen `sim_cursor` to PF `frame = sim_cursor - 1`.
6. Identify the first persistent state break, not the final death.
7. Read Famidash source for the exact controlling routine.
8. Patch the smallest controlling code path.
9. Build and re-run the narrowest validation possible.
10. Update or create a memory note only after the behavior is confirmed.

Do not start with broad rewrites.

---

## 10. Common Anti-Patterns

Avoid these:
- Comparing PF `Y_fixed.low` to NES raw screen low byte directly.
- Treating display `Y_px` mismatches as proof of physics mismatch.
- Patching around final death instead of finding the first persistent break.
- Editing PF without checking SIM when the rule is shared.
- Mixing slope, portal, and camera changes in one blind patch.
- Using enum upper bounds from NES source literally when C# enum order differs.
- Assuming ship/UFO/cube share identical eject rules.
- Reverting working parity fixes because a new divergence appears nearby.

---

## 11. Repo Memory Index

This is the current repo-memory inventory available to future chats. Treat it as the extended appendix to this handoff.

- `/memories/repo/ball-eject-formula-port-failure.md`
- `/memories/repo/ball-eject-gravf-d-gate.md`
- `/memories/repo/ball-eject-screen-vs-world-y.md`
- `/memories/repo/ball-eject-tmp8-partial.md`
- `/memories/repo/blue-pad-activation-gate.md`
- `/memories/repo/blue-pad-stacked-double-flip.md`
- `/memories/repo/camera-nametable-distortion.md`
- `/memories/repo/cataclysm-cube-f108-phantom-ceiling.md`
- `/memories/repo/cataclysm-pf-mt-rate-mismatch.md`
- `/memories/repo/col-top-center-spike-hitbox.md`
- `/memories/repo/darkparadise-divergence-findings.md`
- `/memories/repo/db6-ball-flip-subpx-divergence.md`
- `/memories/repo/dll-full-replace-decompiler-tuple-fix.md`
- `/memories/repo/everyend-bfs-rewind-reexpand.md`
- `/memories/repo/everyend-lua-cursor-bug.md`
- `/memories/repo/exit-portal-timer-cube-refresh.md`
- `/memories/repo/famidash-ball-forward-slope-nudge-reenable.md`
- `/memories/repo/famidash-ball-held-vs-press-flip.md`
- `/memories/repo/famidash-balleject-oldx.md`
- `/memories/repo/famidash-cube-floor-eject-divergence.md`
- `/memories/repo/famidash-mesen-trace-cursor-indexing.md`
- `/memories/repo/famidash-nt-camera-target.md`
- `/memories/repo/famidash-ntsc-table-constants.md`
- `/memories/repo/famidash-parity-master-handoff.md`
- `/memories/repo/famidash-pf-residual-1px-drift.md`
- `/memories/repo/famidash-pf-shipufoeject-slopetype-propagation.md`
- `/memories/repo/famidash-pf-subpx-drift-slope-trigger.md`
- `/memories/repo/famidash-pf-ufo-portal-divergence.md`
- `/memories/repo/famidash-process-y-scroll-clamp-gate.md`
- `/memories/repo/famidash-process-y-scroll-subpx.md`
- `/memories/repo/famidash-robot-physics-order.md`
- `/memories/repo/famidash-ship-portal-1px-snap.md`
- `/memories/repo/famidash-slope-fixes.md`
- `/memories/repo/famidash-speed-portal-velx-latch.md`
- `/memories/repo/famidash-sprite-pyoff-anchor.md`
- `/memories/repo/famidash-ufo-slope-vel-order.md`
- `/memories/repo/famidash-wave-generic-height-quirk.md`
- `/memories/repo/forward-slope-nudge-wedge-gate.md`
- `/memories/repo/leveleasy-ship-ceiling-eject-divergence.md`
- `/memories/repo/mesen-trace-attempt-segmentation.md`
- `/memories/repo/mesen2wide-bundle-source-path.md`
- `/memories/repo/mesen2wide-wsedge-runtime-read.md`
- `/memories/repo/nes-sprite-slot-allocation.md`
- `/memories/repo/nes-y-probe-borrow.md`
- `/memories/repo/orb-multi-tile-fix.md`
- `/memories/repo/pf-ball-eject-diag-logging.md`
- `/memories/repo/pf-blue-pad-generic2-probe.md`
- `/memories/repo/pf-cam-cap-bot-subpx-loss.md`
- `/memories/repo/pf-camera-bottom-cap-subpx.md`
- `/memories/repo/pf-camera-option-a-refactor.md`
- `/memories/repo/pf-cap-scroll-y-bottom-strict.md`
- `/memories/repo/pf-checkceiling-col-bottom.md`
- `/memories/repo/pf-cube-eject-gravf-spike-discard.md`
- `/memories/repo/pf-dual-portal-order-target.md`
- `/memories/repo/pf-exit-slope-ntsc-bit.md`
- `/memories/repo/pf-famidash-parity-directive.md`
- `/memories/repo/pf-freecam-trigger-x-only.md`
- `/memories/repo/pf-full-trace-restoration-sharedphysics.md`
- `/memories/repo/pf-gamemode-portal-order.md`
- `/memories/repo/pf-gamemode-portal-reset-parity.md`
- `/memories/repo/pf-gravity-portal-iteration-order.md`
- `/memories/repo/pf-gravity-portal-per-frame-skip.md`
- `/memories/repo/pf-gravity-trigger-lookahead.md`
- `/memories/repo/pf-log-levelname-cache-bug.md`
- `/memories/repo/pf-nes-f2398-floor-1px.md`
- `/memories/repo/pf-nes-intro-freeze-prestep.md`
- `/memories/repo/pf-nes-parity-validation.md`
- `/memories/repo/pf-nes-player-y-borrow-bug.md`
- `/memories/repo/pf-nes-probe-y-formula.md`
- `/memories/repo/pf-nes-sprite-gamemode-adjust-heights.md`
- `/memories/repo/pf-orb-airpress-latch-gate.md`
- `/memories/repo/pf-orb-formula-DO-NOT-CHANGE.md`
- `/memories/repo/pf-orb-prescan-gravity-sign.md`
- `/memories/repo/pf-release-binary-port-2026.md`
- `/memories/repo/pf-scroll-cap-bottom-strict.md`
- `/memories/repo/pf-scroll-subpx-borrow.md`
- `/memories/repo/pf-spawn-fix-followup-divergences.md`
- `/memories/repo/pf-spawn-settle-fix.md`
- `/memories/repo/pf-spawn-y-fix-was-wrong.md`
- `/memories/repo/pf-spawn-y-formula-fix.md`
- `/memories/repo/pf-sprite-collide-y-decomp.md`
- `/memories/repo/pf-trigger-sprite-anchor-shift.md`
- `/memories/repo/pf-ufo-portal-transition-no-jump.md`
- `/memories/repo/pf-wave-sprite-collide-hitbox.md`
- `/memories/repo/pf-x-timing-do-not-shift.md`
- `/memories/repo/sf2-aftercatabath-ship-death-fix.md`
- `/memories/repo/sf2-cp1252-encoding.md`
- `/memories/repo/sf2-dos-8dot3-filename-collision.md`
- `/memories/repo/sf2-mariochip-softlock.md`
- `/memories/repo/sf2-path-bank-alignment.md`
- `/memories/repo/sf2-paths3-expansion-constraint.md`
- `/memories/repo/sf2-title-gsu-hang.md`
- `/memories/repo/sf2-titledemo-crossbank-gosub.md`
- `/memories/repo/sf2-xbank-gosub-trigger.md`
- `/memories/repo/shardscapes-airpresslatch-bfs-blocker.md`
- `/memories/repo/sharedphysics-slope-eject-low-byte.md`
- `/memories/repo/sharedphysics-tile-id-mapping-bug.md`
- `/memories/repo/ship-mini-block-ejectD.md`
- `/memories/repo/ship-spurious-slope-detection.md`
- `/memories/repo/silentcircles-cube-portal-divergence-fix.md`
- `/memories/repo/silentcircles-wave-portal-entry-velx-order.md`
- `/memories/repo/sim-pf-duplicate-camera-follow-desync.md`
- `/memories/repo/sim-pf-orb-ordering.md`
- `/memories/repo/sim-pf-portal-hitbox-image-expansion.md`
- `/memories/repo/sim-slope-type-side-effect.md`
- `/memories/repo/sim-teleport-cpy-low-preserve.md`
- `/memories/repo/sngl-portal-no-target.md`
- `/memories/repo/stereomadness-cube-centerprobe-y-borrow.md`
- `/memories/repo/thechallenge-ship-cam-drift.md`
- `/memories/repo/toe-mini-ship-common-gravity-collision-branch.md`
- `/memories/repo/toe-mini-ufo-ceiling-eject-mismatch.md`
- `/memories/repo/wave-coll-d-quirk-probe3.md`
- `/memories/repo/wave-coll-u-quirk-probe3.md`
- `/memories/repo/wave-eject-col-death-family.md`
- `/memories/repo/wave-eject-spike-classes-checkceiling-floor.md`
- `/memories/repo/wave-generic-y-line41-update.md`
- `/memories/repo/wave-mode-bg-coll-death-spike.md`

---

## 12. Final Operating Principle

When in doubt:
- identify the first persistent divergence,
- read the exact Famidash source routine that controls it,
- patch the smallest controlling codepath,
- validate with traces,
- preserve all known weird NES behavior.

That mindset is the real handoff.
