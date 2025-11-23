import React, {useCallback, useState} from 'react'
import SimulatorPanel from '../simulator/SimulatorPanel'
import TilePalette from './components/TilePalette'
import SpritePalette from './components/SpritePalette'
import MapCanvas from './components/MapCanvas'
import { downloadJson, readJsonFile } from './utils/saveLoad'
import { importTiledJson } from '../importers/tiledImporter'
import { LevelData } from '../simulator/simulator'

const DEFAULT_WIDTH = 200
const DEFAULT_HEIGHT = 27

export default function App() {
  const [level, setLevel] = useState<LevelData>({width: DEFAULT_WIDTH, height: DEFAULT_HEIGHT, tiles: new Array(DEFAULT_WIDTH*DEFAULT_HEIGHT).fill(0)})
  const [selectedTile, setSelectedTile] = useState<number>(1)
  const [tilesetImg, setTilesetImg] = React.useState<HTMLImageElement | null>(null)
  const [selectedSprite, setSelectedSprite] = useState<number>(0)
  const [spritesetImg, setSpritesetImg] = React.useState<HTMLImageElement | null>(null)
  const [spriteMode, setSpriteMode] = useState<boolean>(false)
  const [scale, setScale] = useState<number>(2)

  function resizeLevel(newWidth:number, newHeight:number){
    const w = Math.max(1, Math.floor(newWidth))
    const h = Math.max(1, Math.floor(newHeight))
    const newTiles = new Array(w*h).fill(0)
    for(let y=0;y<Math.min(h, level.height); y++){
      for(let x=0;x<Math.min(w, level.width); x++){
        newTiles[y*w + x] = level.tiles[y*level.width + x]
      }
    }
    const newObjects = (level.objects || []).filter((o:any)=> o.x < w && o.y < h)
    setLevel({width: w, height: h, tiles: newTiles, objects: newObjects})
  }

  const handlePaint = useCallback((x:number,y:number,tile:number)=>{
    const idx = y*level.width + x
    const newTiles = level.tiles.slice()
    newTiles[idx] = tile
    setLevel({...level, tiles: newTiles})
  }, [level])

  function saveLevel(){
    downloadJson(level, 'level.json')
  }

  async function loadLevelFromFile(e: React.ChangeEvent<HTMLInputElement>){
    const json = await readJsonFile(e.target.files?.[0])
    if (json) setLevel(json as LevelData)
    e.currentTarget.value = ''
  }

  async function importTiled(e: React.ChangeEvent<HTMLInputElement>){
    const json = await readJsonFile(e.target.files?.[0])
    if (json) {
      const mapped = importTiledJson(json)
      setLevel({width: mapped.width, height: mapped.height, tiles: mapped.tiles})
    }
    e.currentTarget.value = ''
  }

  return (
    <div className="app-root">
      <header className="app-header">famidash - Level Editor (prototype)</header>
      <main className="workspace">
        <section className="left">
          <div style={{display:'flex',flexDirection:'column',gap:12}}>
            <TilePalette selected={selectedTile} onSelect={setSelectedTile} onTilesetLoad={setTilesetImg} scale={scale} />
            <SpritePalette selected={selectedSprite} onSelect={setSelectedSprite} onSpritesetLoad={setSpritesetImg} scale={scale} />
            <div style={{marginTop:8}}>
              <label style={{display:'inline-flex',alignItems:'center',gap:8}}>
                <input type="checkbox" checked={spriteMode} onChange={e=>setSpriteMode(e.target.checked)} />
                <span>Place sprites</span>
              </label>
            </div>

            <div style={{marginTop:8}}>
              <div style={{marginBottom:6}}>Level size</div>
              <div style={{display:'flex',gap:8,alignItems:'center'}}>
                <label>Width (tiles): <input type="number" value={level.width} onChange={e=>resizeLevel(Number(e.target.value||0), level.height)} /></label>
                <label>Height (tiles): <input type="number" min={16} max={57} value={level.height} onChange={e=>resizeLevel(level.width, Math.min(57, Math.max(16, Number(e.target.value||16))))} /></label>
              </div>
            </div>

            <div style={{marginTop:8}}>
              <div style={{marginBottom:6}}>Zoom</div>
              <div style={{display:'flex',gap:8,alignItems:'center'}}>
                <button onClick={()=>setScale(s=>Math.max(1, s-1))}>-</button>
                <div style={{width:48,textAlign:'center'}}>{scale}x</div>
                <button onClick={()=>setScale(s=>Math.min(6, s+1))}>+</button>
              </div>
            </div>
          </div>
          <div style={{marginTop:12}}>
            <div style={{marginBottom:8}}>
              <button onClick={saveLevel}>Save level (JSON)</button>
              <label style={{marginLeft:8}}>
                <input type="file" accept="application/json" onChange={loadLevelFromFile} style={{display:'none'}} />
                <span style={{cursor:'pointer',marginLeft:4}}>Load level</span>
              </label>
            </div>
            <div>
              <label>
                <input type="file" accept="application/json" onChange={importTiled} style={{display:'none'}} />
                <span style={{cursor:'pointer'}}>Import Tiled JSON</span>
              </label>
            </div>
          </div>
        </section>
        <section className="center">
          <div className="map-wrapper">
            <MapCanvas level={level} selectedTile={selectedTile} tileset={tilesetImg} spriteset={spritesetImg} spriteMode={spriteMode} selectedSprite={selectedSprite} scale={scale} onPlaceSprite={(x:number,y:number,sprite:number)=>{
              // add or replace sprite at tile position
              const objs = (level.objects || []).slice()
              // check existing object at same pos
              const idx = objs.findIndex((o:any)=>o.x===x && o.y===y)
              const obj = {x,y,sprite}
              if (idx >= 0) objs[idx] = obj
              else objs.push(obj)
              setLevel({...level, objects: objs})
            }} onPaint={handlePaint} />
          </div>
        </section>
        <section className="right">
          <SimulatorPanel />
        </section>
      </main>
    </div>
  )
}
