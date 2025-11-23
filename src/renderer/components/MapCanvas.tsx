import React, {useEffect, useRef} from 'react'
import { LevelData } from '../../simulator/simulator'

type Props = {
  level: LevelData
  selectedTile: number
  tileset?: HTMLImageElement | null
  spriteset?: HTMLImageElement | null
  spriteMode?: boolean
  selectedSprite?: number
  onPlaceSprite?: (x:number,y:number,sprite:number)=>void
  onPaint: (x:number,y:number,tile:number) => void
  scale?: number
}

const TILE_PX = 16
const SCALE = 2

export default function MapCanvas({level, selectedTile, tileset, spriteset, spriteMode, selectedSprite, onPlaceSprite, onPaint, scale=2}:Props){
  const canvasRef = useRef<HTMLCanvasElement|null>(null)

  useEffect(()=>{
    const c = canvasRef.current
    if (!c) return
  const ctx = c.getContext('2d')!
  const w = level.width * TILE_PX * scale
  const h = level.height * TILE_PX * scale
  c.width = w
  c.height = h
  ctx.fillStyle = '#0b0b0f'
  ctx.fillRect(0,0,w,h)
    // draw tiles
    if (tileset){
      const cols = Math.floor(tileset.width / TILE_PX)
      for(let y=0;y<level.height;y++){
        for(let x=0;x<level.width;x++){
          const t = level.tiles[y*level.width + x]
          if (t > 0){
            const idx = t-1
            const sx = (idx % cols) * TILE_PX
            const sy = Math.floor(idx / cols) * TILE_PX
            ctx.drawImage(tileset, sx, sy, TILE_PX, TILE_PX, x * TILE_PX * scale, y * TILE_PX * scale, TILE_PX * scale, TILE_PX * scale)
          }
        }
      }
    } else {
      // simple colored tiles if no tileset
      for(let y=0;y<level.height;y++){
        for(let x=0;x<level.width;x++){
          const t = level.tiles[y*level.width + x]
          if (t > 0){
            ctx.fillStyle = `hsl(${(t*37)%360} 60% 50%)`
            ctx.fillRect(x*TILE_PX*scale, y*TILE_PX*scale, TILE_PX*scale, TILE_PX*scale)
          }
        }
      }
    }
    // draw objects (sprites) on top of tiles
    if (spriteset && (level.objects && level.objects.length > 0)){
      const sCols = Math.floor(spriteset.width / TILE_PX)
      for(const obj of level.objects){
        const sx = ((obj.sprite-1) % sCols) * TILE_PX
        const sy = Math.floor((obj.sprite-1) / sCols) * TILE_PX
        ctx.drawImage(spriteset, sx, sy, TILE_PX, TILE_PX, obj.x * TILE_PX * scale, obj.y * TILE_PX * scale, TILE_PX * scale, TILE_PX * scale)
      }
    }

    // grid lines
    ctx.strokeStyle = 'rgba(0,0,0,0.35)'
    for(let x=0;x<=level.width;x++){
      ctx.beginPath()
      ctx.moveTo(x*TILE_PX*scale,0)
      ctx.lineTo(x*TILE_PX*scale,h)
      ctx.stroke()
    }
    for(let y=0;y<=level.height;y++){
      ctx.beginPath()
      ctx.moveTo(0,y*TILE_PX*scale)
      ctx.lineTo(w,y*TILE_PX*scale)
      ctx.stroke()
    }
  }, [level, tileset, spriteset])

  // no separate effect needed; sprites drawn in main effect

  function handleClick(e:React.MouseEvent){
    const c = canvasRef.current
    if (!c) return
    const rect = c.getBoundingClientRect()
    const x = Math.floor((e.clientX - rect.left) / (TILE_PX * scale))
    const y = Math.floor((e.clientY - rect.top) / (TILE_PX * scale))
    if (spriteMode && selectedSprite && selectedSprite > 0){
      // place sprite object
      onPlaceSprite && onPlaceSprite(x,y,selectedSprite)
    } else {
      onPaint(x,y,selectedTile)
    }
  }

  return (
    <div>
      <canvas ref={canvasRef} style={{border:'1px solid #222',cursor:'crosshair'}} onClick={handleClick} />
    </div>
  )
}
