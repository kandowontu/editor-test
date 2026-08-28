# Famidash PR #360 Physics Migration

Date: 2026-08-28

Authoritative NES change: [tfdsoft/famidash PR #360](https://github.com/tfdsoft/famidash/pull/360)

Authoritative merge commit: `4554c178948aaafe34778550f4c0d9c777ad10b8`

## Purpose and invariants

This migration updates the native Windows editor, pathfinder, simulator, standalone PF runner, and bundled one-shot `sim-famidash` source to the coordinate and scrolling model introduced by Famidash PR #360.

The implementation follows these invariants:

1. The NES implementation is authoritative. PF/simulator state is adapted to reproduce NES behavior; the NES source is not adapted to match PF/simulator behavior.
2. Integer and fractional operations preserve the NES operation order, including carry, borrow, independent branch checks, and cap timing.
3. Collision probes use the same linear absolute Y coordinates as the new NES collision code.
4. The one-shot replay build still freezes on the first committed death instead of running the normal death animation/reset sequence.
5. Existing editor projects using the old scroll high/low metadata remain loadable, but newly saved editor config, generated JSON5, and build/test metadata use the new format.

## Executive summary

PR #360 makes `scroll_y` a normal linear pixel coordinate. Before the PR, parts of the engine represented vertical scroll in a PPU-oriented format where each page contained 240 valid lines and the `$F0-$FF` range was skipped. The new code keeps gameplay coordinates linear and converts to PPU format only when sending scroll values to rendering code.

That change affects more than display scrolling. Player screen coordinates, camera targets, collision probes, slope checks, spawn calculations, cap compensation, and the fixed-point ship camera all depend on the representation of `scroll_y`. Carrying only the visible integer result would eventually produce one-pixel PF/Mesen divergences, so the simulator and PF now retain the NES fractional scroll byte and reproduce the exact arithmetic that changes the integer scroll byte.

## Authoritative `sim-famidash` merge

The PR #360 versions of all changed engine/configuration files were copied from the merge commit into the local `sim-famidash` source. This includes:

- all changed linker configurations under `CONFIG`;
- `LEVELS/export_levels.py`;
- the changed NES assembly and headers under `LIB`;
- the changed engine definitions, loading, collision, scrolling, movement, sprite, practice, and reset code under `SAUCE`;
- the changed ball, cube, ship, spider, UFO, and wave mode code;
- game-state and title-screen changes.

A hash comparison against merge commit `4554c178...` confirms every PR-changed engine/configuration file matches exactly except `SAUCE/gamestates/state_game.h`. That file intentionally contains the one-shot replay override described below.

### One-shot death behavior retained

Normal PR #360 behavior:

```c
death_animation();
reset_level();
```

One-shot replay behavior retained in `sim-famidash`:

```c
while (1) { };
```

The condition surrounding that override now uses PR #360's renamed `player_relx[0]`, so the replay freeze occurs at the same committed-death point as the new NES source.

### Malformed wave/snake comparison fixed

The previous local source contained:

```c
if (gamemode == gamemode == GAMEMODE_WAVE || gamemode == GAMEMODE_SNAKE)
```

It is now the authoritative comparison:

```c
if (gamemode == GAMEMODE_WAVE || gamemode == GAMEMODE_SNAKE)
```

The corrected expression is present in source, regenerated cc65 output, Debug build runtime content, and ReleaseBuild runtime content. The malformed expression is absent from those release-chain copies.

## Coordinate representation

### Old model

The old metadata described scroll using high/low PPU-format components. Converting it to a physical pixel coordinate required:

```text
linearY = high * 240 + low
```

The old default was commonly written as high `$02`, low `$EF`, which converts to 719 physical pixels.

### New model

The new metadata stores the physical value directly:

```text
scrollYPosition = $02CF = 719
```

The shared C# constant is now:

```text
NES_DEFAULT_SCROLL_Y_LINEAR = NES_MAX_SCROLL_Y_LINEAR = $02CF
```

No gameplay code converts this value through a 240-line page representation. PPU conversion remains a rendering-boundary responsibility in the NES source.

The compact level header still stores only this field's low byte and `init_rld` fixes its high byte to `$02`. Current metadata is therefore normalized exactly as the ROM loads it: `$02xx`, with the supplied low byte. Normal full values such as `$02CF` are unchanged. A short or malformed value such as `$0030` is interpreted as `$0230`, matching the ROM rather than allowing PF/simulator startup to disagree. Values through `$02FF` are representable initially; normal gameplay scrolling then applies the `$02CF` bottom cap.

### PF/simulator state decomposition

The C# runtime keeps:

- `CameraY_fixed`: integer camera pixels in fixed-point form;
- `ScrollYSubpx`: the NES `scroll_y_subpx` byte;
- player Y in the PF/simulator's world-like representation, equivalent to NES relative player Y plus the integer camera component.

Because the fractional scroll byte is separate, changing `scroll_y_subpx` can require a compensating change in represented player Y even when the physical absolute player position has not changed. The implementation performs that compensation explicitly.

## Spawn and metadata changes

### Current fields

The editor now exposes and generates:

```json5
spawnYPositionHi: 0xB0,
scrollYPosition: 0x02CF,
```

The removed `spawnYPositionLow`, `scrollYPositionHi`, and `scrollYPositionLow` fields are not emitted by Generate JSON5 or Build and Test.

### Backward-compatible loading

Old TMX editor configs and old JSON5 metadata are accepted on load. If the new `scrollYPosition` field is absent but an old high/low pair exists, the loader performs the old `high * 240 + low` conversion once and then encodes the result through the current compact-header rule. The historically used `$02xx` PPU range therefore migrates to the same physical linear position, while any unsupported old high-byte value follows what the current ROM can actually load.

On the next config save, the editor writes `ScrollYPosition` and clears the obsolete split fields. This makes migration one-way without changing the effective camera position of existing projects.

### Spawn position formula

Without a START POS marker, PF and simulator now use:

```text
nesYOffset = (57 - mapHeight + reservedGroundRows) * 16
startY = spawnYPositionHi + scrollYPosition - nesYOffset
```

`spawnYPositionHi` is the integer part of the NES 8.8 relative spawn coordinate. Its removed low byte is fixed to zero.

### Initial camera formula

The new camera calculation is direct:

```text
cameraY = scrollYPosition - nesYOffset
```

It is not pre-clamped. NES `reset_level` installs the header value directly, and the first gameplay `process_y_scroll` call performs source-ordered top/bottom cap compensation. This matters for unusual initial values outside the normal gameplay range. It also removes the previous bottom-relative conversion, including its possible one-pixel difference.

## Minimum and maximum scroll

### Minimum scroll

PR #360 calculates minimum scroll in linear space. PF and simulator now mirror it as:

```text
emptyTopRows = max(0, 57 - mapHeight)
minScrollY = (emptyTopRows * 16) | 8
```

The old code divided the empty area into 15-row PPU pages. That conversion is no longer valid after the linear-scroll rewrite.

### Bottom cap

The bottom cap is linear `$02CF`. The assembly uses a signed result and enters the cap routine when scroll is equal to or greater than `$02CF`, not only after it exceeds the cap.

PF and simulator therefore use `>=` at the bottom cap. On cap, the current fractional scroll byte is folded into represented player Y and then cleared, matching the assembly.

### Top cap

The new assembly adds `scroll_y_subpx` to player relative Y when capping at the top. PF and simulator previously subtracted that byte in several paths. Those signs are now corrected, and dual-player state receives the same compensation.

## Cube/robot/ninja/pogo/football camera behavior

The Y-follow branch still anchors the player between the NES's `$40` and `$A0` relative Y thresholds. Fractional movement is applied in NES order:

1. move player relative Y toward the anchor;
2. add or subtract the fractional amount from `scroll_y_subpx`;
3. propagate carry/borrow into integer `scroll_y`;
4. update the C# represented player Y by the difference between relative movement and integer camera movement;
5. run top or bottom cap compensation.

Football mode now joins the same camera-follow branch as cube, robot, ninja, and pogo, matching PR #360. It no longer falls through to ship-style target scrolling.

## Ship-style camera arithmetic

The authoritative NTSC step is the full fixed-point value:

```text
SHIP_SCROLL_SPEED = $0266
```

It is not a fixed `+2` in one direction and `-3` in the other. The integer amount is selected by the current subpixel carry or borrow.

### Moving toward a larger target

```text
newSub = oldSub + $66
carry = newSub > $FF
integerCameraMove = 2 + carry
```

The player relative coordinate moves by `-$0266`. The C# represented Y is adjusted by:

```text
(integerCameraMove << 8) - $0266
```

### Moving toward a smaller target

```text
newSub = oldSub - $66
borrow = newSub < 0
integerCameraMove = -(2 + borrow)
```

The player relative coordinate moves by `+$0266`. The C# represented Y is adjusted by:

```text
$0266 + (integerCameraMove << 8)
```

### Independent comparisons

The two target comparisons in NES `scroll.h` are independent `if` statements. A first step can cross the target, after which the opposite comparison may also run in the same frame. PF and simulator preserve that order and do not convert it to `if/else`.

## Camera portal targets

PR #360 stores active sprite Y and `target_scroll_y` in linear coordinates. The previous PF/simulator code converted portal targets into and back out of PPU page space. The target is now simply:

```text
targetCameraY = portalWorldY - 90
```

The 90-pixel offset is the PR's new `PORTAL_TO_TOP_DIFF = $5A`; the old source used `$3A` (58 pixels). PF and simulator both use `$5A` directly. The result is intentionally not clamped before `process_y_scroll`; the NES applies its top/bottom cap after attempting the target step.

## Collision and slope probes

The PR replaces the old `add_scroll_y`/byte-wrap path with linear absolute collision coordinates. The C# shared collision code previously reproduced the old one-byte wrap using a `NesByteWrappedCollisionY` helper.

That helper has been removed. Ball, ship, and UFO slope, ceiling, floor, and ejection probes now pass their direct absolute probe Y values to the shared collision map. This affects:

- upward and downward slope probes;
- ceiling tile probes;
- floor tile probes;
- ball ejection in both gravity directions;
- ship/UFO ejection in both gravity directions;
- 66-degree slope solid-region handling where the old page-gap wrap could select a different tile.

Slope state side effects, ejection byte persistence, and NES operation ordering remain unchanged.

## Wave behavior

Three wave-related corrections were made:

1. The malformed C wave/snake comparison was replaced by the authoritative PR expression.
2. Simulator wave Y movement now freezes whenever either NES slope counter is active. `dblocked` no longer bypasses that movement gate; it only controls the later collision response, as in the NES source.
3. Simulator held input is set directly on key-down and cleared directly on key-up. Wave and snake are hold-driven, so they no longer wait for a later UI polling tick before changing vertical direction.

The simulator camera no longer waits for the editor-only `jumpedOnce` flag before running NES Y scrolling. This matters for continuously moving modes such as wave and ship, which must scroll from their first active gameplay frames even without a cube-style jump event.

## Editor and standalone PF integration

The following integration paths use the same new startup data:

- editor spawn/camera overlays;
- simulator construction;
- editor Pathfinder dialog runs;
- deterministic replay reconstruction for simulator playback;
- standalone `pf-test` runs;
- Generate JSON5;
- Tools > Mesen > Build and Test metadata generation.

The standalone runner reads `ScrollYPosition` from current config/metadata and still accepts old split fields as a load-only fallback. Its diagnostics now print the single linear scroll value.

## File-by-file implementation map

### `native-windows/SharedPhysics.cs`

- Defines `$02CF` as the shared default and maximum linear NES scroll.
- Resolves current one-field scroll metadata.
- Retains a load-only converter for legacy high/low metadata using `high * 240 + low`.
- Removes the gameplay-facing byte-wrapped Y collision helper so all callers use linear absolute probes.

### `native-windows/PathfinderEngine.cs`

- Stores `ConfigScrollYPosition` as one linear value.
- Computes spawn and initial camera Y directly from the new scroll coordinate.
- Implements the cube-family and ship-family camera branches as centralized helpers so replay, search, intro, and spider-wait paths cannot carry separate approximations.
- Reproduces the `$0266` ship carry/borrow phase, independent comparisons, cap signs, and cap timing.
- Adds football to cube-family Y following.
- Uses direct portal targets and direct absolute collision Y values.

The cube-family helper preserves this exact source order:

1. optionally perform upward anchor adjustment;
2. always call the top-cap equivalent;
3. recompute represented screen Y and linear scroll;
4. independently test and optionally perform downward anchor adjustment;
5. always call the bottom-cap equivalent.

The main frame path delegates to this helper rather than retaining an inlined duplicate. This is important because changing the source's second comparison into an `else if`, or moving either cap inside a movement branch, changes the fractional phase and can create a later one-pixel divergence.

### `native-windows/SimulatorWindow.xaml.cs`

- Uses the same direct startup and camera formulas as PF.
- Uses the same centralized NES camera arithmetic, including both-player compensation during dual/two-player state.
- Starts Y camera processing without waiting for the editor-only `jumpedOnce` flag.
- Sets wave/snake held input synchronously on key down/up.
- Uses direct portal targets and direct absolute collision probes.
- Receives the new single scroll field through `SetSpawnScrollConfig`.

### `native-windows/WavePhysics_Fresh.partial.cs`

- Restores the NES slope-counter movement gate for wave/snake.
- Keeps `dblocked` in its collision-response role rather than using it to bypass the movement freeze.

### `native-windows/MainWindow.xaml.cs`

- Adds `ScrollYPosition` to saved TMX config while retaining obsolete fields only for migration reads.
- Migrates old split values on load and writes only the new value on save.
- Routes spawn overlays, PF startup, simulator startup, and replay reconstruction through the same linear coordinate.

### `native-windows/MainWindow.BuildAndTest.cs`

- Emits the new `scrollYPosition` metadata for test builds.
- Stops emitting removed split scroll/spawn fields.

### `native-windows/SetOptionsWindow.xaml` and `.xaml.cs`

- Replace the old low-byte scroll control with one `scrollYPosition` control.
- Parse and display the full effective `$02xx` value; startup values through `$02FF` are representable, while gameplay still caps at `$02CF`.
- Generate current JSON5 while accepting legacy fields when older metadata is loaded.

### `pf-test/Program.cs`

- Reads current TMX/JSON5 scroll metadata and passes one linear value to PF.
- Keeps old split fields as a compatibility fallback only.
- Removes the obsolete spawn-low command-line override and reports the effective linear value.

### `sim-famidash`

- Incorporates the PR #360 engine, exporter, linker, and assembly changes.
- Keeps the local one-shot death behavior as the only intentional engine-file deviation from the PR merge.
- Supplies the source copied into Debug and Publish output by the existing project targets.

## Release-chain behavior

`sim-famidash` remains the source copied by the existing MSBuild Debug and Publish targets. No hard-coded alternate sim folder was introduced.

The local ReleaseBuild was republished without deleting its settings/history files. Its bundled `sim-famidash` contains:

- the PR #360 engine source;
- the one-shot death freeze;
- the corrected wave/snake comparison;
- the new exporter.

The one-shot batch file and editor Build and Test flow remain in place. Build and Test still copies the selected TMX to level set D, rewrites its metadata/music selection, invokes `1BigSimExport.bat`, and launches the resulting ROM through the local Mesen runtime.

## Verification performed

### C# builds

The following succeeded with zero errors:

```powershell
dotnet build native-windows\FamidashEditor.csproj --no-restore /p:SelfContained=false /p:UseAppHost=false
dotnet build pf-test\PfTest.csproj --no-restore
dotnet publish native-windows\FamidashEditor.csproj -c Release -o native-windows\ReleaseBuild --no-restore /p:SelfContained=false /p:PublishSingleFile=false
```

Only pre-existing nullable/unused-field warnings remain.

### Source parity

All PR-changed `CONFIG`, `LIB`, `SAUCE`, and exporter files in local `sim-famidash` hash-match merge commit `4554c178...`, except the intentionally customized one-shot `state_game.h`.

### Pathfinder regression

The standalone pathfinder completed Stereo Madness with coin preference enabled:

```text
Result: SUCCESS
Completed in 5104 frames
3/3 coins
```

This validates the rewritten default startup and cube-family camera path across a complete replay. The portal-target constant is source/build verified separately because this level does not exercise a mode-camera target.

### One-shot ROM build

The one-shot batch reached and compiled the updated engine source. It then stopped at the final assembly/link stage because the currently selected local level metadata references `song_carefree_victory_remix`, but that symbol is absent from the currently exported local music album. This is an independent local music-library prerequisite, not a physics or source-compilation failure.

Before attempting another direct one-shot build, either select a song present in the local exported album or install/export the custom song through the editor's music-library flow.

## Expected physics effects

The changes intentionally alter PF/simulator results wherever old PPU-format vertical math leaked into gameplay:

- startup Y and camera Y can shift by one pixel compared with the old conversion;
- ship-style camera integer steps now follow the `$66` carry/borrow phase instead of a constant asymmetric approximation;
- portal camera drift no longer appears when targets cross former 240-line page boundaries;
- top/bottom cap frames and low-byte player Y now match the new assembly;
- collisions near former `$F0-$FF` page gaps use the same absolute tile as NES;
- football receives cube-style Y tracking;
- wave responds immediately to held input and retains NES slope-freeze behavior.

These are compatibility corrections, not search heuristics. They do not remove pathfinder branches or potential input paths. They change the simulated result of a branch only where the old runtime differed from the new NES physics.

## Recommended acceptance tests

For final Mesen acceptance, use levels that exercise each changed area:

1. a normal cube level for startup and cube camera behavior;
2. a ship level with several vertical camera targets and long `$0266` carry cycles;
3. a wave level with slopes and held-input transitions;
4. a level with 66-degree slopes near a camera transition;
5. a football section to confirm Y-follow behavior;
6. a level with custom `scrollYPosition` metadata;
7. an old project containing split scroll fields, followed by save/reopen, to verify migration.

For every replay, compare the first divergent frame rather than downstream death position. PF, simulator, and Mesen should agree on player relative Y, player Y low byte, integer scroll Y, `scroll_y_subpx`, target scroll Y, mode, gravity, and mini state.
