/**
 * Tiled JSON importer (stub)
 * Accepts Tiled exported JSON (or TMX converted to JSON) and converts to
 * a simplified famidash-friendly level object.
 */

export type FamidashLevel = {
  width: number
  height: number
  tiles: number[]
  meta?: any
}

export function importTiledJson(tiledJson: any): FamidashLevel {
  // This is a minimal, opinionated mapper. Extend to support multiple layers,
  // auto-tiles, properties, object layers, CHR mapping, etc.
  const layer = tiledJson.layers && tiledJson.layers.find((l: any) => l.type === 'tilelayer')
  const width = tiledJson.width || (layer && layer.width) || 16
  const height = tiledJson.height || (layer && layer.height) || 14
  const tiles = (layer && layer.data) ? layer.data.map((v: any) => (v === 0 ? 0 : v - 1)) : new Array(width * height).fill(0)
  return {width, height, tiles, meta: {source: 'tiled-json'}}
}
