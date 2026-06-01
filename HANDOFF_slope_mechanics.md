# Handoff: Slope Mechanics (PF + SIM + Source-Parity Workflow)

Date: 2026-05-28
Workspace: `c:\Editor Test`

This is the slope-specific recovery handoff for future chat sessions. Use this as the source-of-truth for how slope routines were fixed and how to continue without reintroducing old regressions.

---

## 1) Scope and Goal

Goal: keep PathfinderEngine (PF) and Simulator (SIM) slope behavior in lockstep with Famidash NES source.

Slope work in this repo touches three codepaths:

1. PF runtime/eject path in `native-windows/PathfinderEngine.cs` + `native-windows/SharedPhysics.cs`
2. SIM path in `native-windows/SlopeCollision.partial.cs` + `native-windows/SimulatorWindow.xaml.cs`
3. Wave slope-death checks in both PF and SIM

If any one path drifts, parity fails (usually as 1px Y divergence first, then death mismatch).

---

## 2) Non-Negotiable Invariants

### Invariant A: Slope enum range upper bound must include `COL_SLOPE_LU66_TOP`

C# enum ordering has `COL_SLOPE_LU66_TOP` after `COL_SLOPE_LU66_BOT`, so slope checks must use:

- `collision >= COL_SLOPE_RD45 && collision <= COL_SLOPE_LU66_TOP`

Never use `<= COL_SLOPE_LU66_BOT` for inclusive slope range checks in C# code.

Why: using `_BOT` as upper bound silently excludes `_TOP` and causes intermittent slope miss/eject differences.

Memory anchor: `/memories/repo/famidash-slope-fixes.md`.

### Invariant B: Cube inverted ceiling-slope eject is not ship eject

For cube/robot/ninja/football with flipped gravity on ceiling slopes, PF must match NES sign behavior:

- snap is `+tmp8`, not `+tmp8 - 1`

The extra `-1` belongs to ship ceiling eject patterns, not cube family.

### Invariant C: 66-degree BOT sentinel slope hits must not mutate slope counters/jump flags

If slope result is sentinel (`slopeType == 0` style path), it may still eject but must not apply normal slope side effects:

- do not set `SlopeJumpHigher`
- do not set `SlopeFrames`
- do not set `SlopeWasOnCounter`

### Invariant D: Direction-rejected slope checks must rollback temporary counter writes

When `bg_coll_slope` probe mutates slope state but final direction gate rejects the slope, SIM must restore previous counters.

In `SlopeCollision.partial.cs`, this is documented as "Fix 33" around `bg_coll_D_slopes` / `bg_coll_U_slopes`.

### Invariant E: Wave slope checks are death checks, not generic floor/ceiling nudges

Wave/snake treat slopes as death surfaces through dedicated checks. Keep their slope-range and mini exclusions aligned with PF/SIM shared rules.

---

## 3) Current Slope Routine Map (Where to Edit)

## PF routine anchors

- `native-windows/PathfinderEngine.cs`
  - `PfSlopeCalc(...)`
  - `PfCheckSlopes(...)`
  - `PfCheckSlopesUp(...)`
  - `PfUpdateSlopeCounters_Fresh(...)`
  - `PfSlopeJumpCheck(...)`
  - `CheckSlopePenetrationDeath(...)`
  - Wave eject/death slope checks in wave handling section

- `native-windows/SharedPhysics.cs`
  - `SlopeCalc(...)`
  - `CheckSlopesDown(...)`
  - `CheckSlopesUp(...)`
  - `GetForwardSlopeNudge(...)`
  - `CubeEject(...)`
  - `BallEject(...)`
  - `ShipUfoEject(...)`

## SIM routine anchors

- `native-windows/SlopeCollision.partial.cs`
  - `bg_coll_slope(...)`
  - `bg_coll_D_slopes(...)`
  - `bg_coll_U_slopes(...)`

- `native-windows/SimulatorWindow.xaml.cs`
  - Wave center / R-edge slope death checks (P1 and P2)
  - Slope range and mini-skip logic must mirror PF

---

## 4) What Is Already Fixed (Do Not Re-break)

1. Inclusive slope range checks use `<= COL_SLOPE_LU66_TOP` in PF/SIM slope logic.
2. Cube-family flipped-gravity ceiling-slope eject sign/amount parity fix is in place.
3. 66-degree BOT sentinel-hit side effects are suppressed (no false slope counter/jump mutations).
4. SIM `bg_coll_*_slopes` rollback behavior on direction rejection (Fix 33) is present.
5. Wave slope-death paths use inclusive slope range and mini exclusions consistent with PF logic.

If you suspect one of these regressed, audit before making new changes.

---

## 5) Mandatory Audit Commands Before/After Any Slope Patch

Run these from PowerShell:

```powershell
# 1) Bad upper-bound pattern should be zero
Select-String -Path "c:\Editor Test\native-windows\*.cs","c:\Editor Test\native-windows\*.partial.cs" -Pattern "<=\s*MetatileCollision\.COL_SLOPE_LU66_BOT"

# 2) Confirm inclusive upper bound exists in all target files
Select-String -Path "c:\Editor Test\native-windows\PathfinderEngine.cs","c:\Editor Test\native-windows\SharedPhysics.cs","c:\Editor Test\native-windows\SlopeCollision.partial.cs","c:\Editor Test\native-windows\SimulatorWindow.xaml.cs" -Pattern "COL_SLOPE_LU66_TOP"

# 3) Find all slope-core call sites quickly
Select-String -Path "c:\Editor Test\native-windows\PathfinderEngine.cs","c:\Editor Test\native-windows\SharedPhysics.cs" -Pattern "PfSlopeCalc\(|PfCheckSlopes\(|PfCheckSlopesUp\(|SlopeCalc\(|CheckSlopesDown\(|CheckSlopesUp\(|GetForwardSlopeNudge\(" -AllMatches
```

Expected: command (1) returns zero matches.

---

## 6) Safe Workflow For Any New Slope Change

1. Confirm behavior in Famidash source first:
   - `famidash/SAUCE/functions/collision.h`
   - related movement/eject handlers in SAUCE
2. Touch PF and SIM in the same pass when rule semantics change.
3. Keep edits minimal and local to slope routines; do not mix with portal/orb/camera changes.
4. Re-run audit commands in section 5.
5. Build both projects:
   - `c:\Editor Test\native-windows\FamidashEditor.csproj`
   - `c:\Editor Test\pf-test\PfTest.csproj`
6. Validate with known slope-sensitive levels/scenarios and compare PF vs Mesen/SIM traces.

---

## 7) Slope + Trace Validation Notes

When validating frame alignment:

- Mesen `sim_cursor = N` compares to PF frame `N-1`
- Always inspect `Y_fixed`, low byte, and camera Y (`CamY_fixed`) around slope interactions

For PF CSV traces, include slope columns:

- `SlopeType`
- `LastSlopeType`
- `SlopeFrames`
- `SlopeWasOn`

These are already emitted by the PF frame trace CSV header currently in `PathfinderEngine.cs`.

---

## 8) Common Failure Patterns (Seen in This Project)

1. Enum-range drift: using `_LU66_BOT` as terminal bound in C#.
2. Mixing ship and cube ceiling-eject formulas.
3. Letting sentinel slope probes write lasting slope state.
4. Updating PF slope logic but forgetting SIM mirror logic.
5. Editing slope code while also changing dual/portal flow, making root-cause unclear.

---

## 9) High-Value File Checklist For Future Chat

- `native-windows/PathfinderEngine.cs`
- `native-windows/SharedPhysics.cs`
- `native-windows/SlopeCollision.partial.cs`
- `native-windows/SimulatorWindow.xaml.cs`
- `famidash/SAUCE/functions/collision.h`
- `/memories/repo/famidash-slope-fixes.md`

---

## 10) If A New Slope Bug Appears

Triage order:

1. Re-run section 5 audits (fastest regression detector)
2. Diff PF vs SIM slope routine decisions (`SlopeType/SlopeFrames/SlopeWasOn`)
3. Check whether bug is wave-death path vs generic eject path
4. Verify mini exclusions (`LU45` and 66-top/bot exclusions) are identical on both sides
5. Patch smallest scope possible, then re-run all audits and build

If unsure, prefer adding temporary slope trace logs around `SlopeCalc` and `CheckSlopes*` rather than broad rewrites.

---

## 11) Build Commands

```powershell
cd "c:\Editor Test\pf-test"
dotnet build --no-restore

cd "c:\Editor Test\native-windows"
dotnet build --no-restore
```

If build fails with `MSB3021/MSB3027` file lock, close running editor process and rebuild.

---

End of slope handoff.
