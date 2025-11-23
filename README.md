# famidash-editor (prototype)

This repository contains a minimal scaffold for a standalone level editor for famidash.

What you get
- Electron + Vite + React (renderer) scaffold
- A simple in-editor gameplay simulator stub (not an NES emulator) at `src/simulator`
- A Tiled JSON importer stub at `src/importers/tiledImporter.ts`

Quick start (Windows / PowerShell)

1. Install dependencies

```powershell
npm install
```

2. Start the dev environment (renderer served by Vite; main compiled by tsc)

```powershell
npm run dev
```

Notes & next steps
- The simulator in `src/simulator` is a small TypeScript stub that you can extend
  to mirror famidash gameplay (player physics, collisions, entity behaviors).
- The Tiled importer maps a single tile layer into a simple famidash format.
- If you want the main process in TypeScript to run directly without a watch build,
  we can switch to ts-node or use a more advanced electron + Vite integration.
