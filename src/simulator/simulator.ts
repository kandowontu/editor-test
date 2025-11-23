/**
 * Simple gameplay simulator stub for famidash.
 * This is NOT an NES emulator. It simulates game objects and physics
 * inside the editor so you can playtest levels inside the editor UI.
 */

export type LevelData = {
  width: number
  height: number
  tiles: number[] // linear tile indices
  objects?: any[]
}

export default class Simulator {
  level: LevelData | null = null
  running = false
  tickInterval = 1000 / 60
  timer: any = null

  loadLevel(level: LevelData) {
    this.level = level
  }

  start() {
    if (!this.level) return
    this.running = true
    this.timer = setInterval(() => this.step(), this.tickInterval)
  }

  stop() {
    this.running = false
    if (this.timer) clearInterval(this.timer)
    this.timer = null
  }

  step() {
    // run one simulation tick
    // TODO: implement player physics, entity updates, collision with tiles
    // For now we just emit a console tick for the editor to hook into
    // The editor UI can call step() manually for deterministic stepping.
    // Example: update positions, run behavior scripts, check win/lose.
    // Placeholder: no-op
  }

  reset() {
    // reset simulator state
    this.stop()
  }
}
