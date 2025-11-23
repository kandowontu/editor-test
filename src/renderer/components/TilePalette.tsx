import React, {useEffect, useRef, useState} from 'react'

type Props = {
  selected: number
  onSelect: (tile:number) => void
  onTilesetLoad?: (img: HTMLImageElement|null) => void
  scale?: number
}

const TILE_PX = 16

export default function TilePalette({selected, onSelect, onTilesetLoad, scale=2}:Props){
  const canvasRef = useRef<HTMLCanvasElement|null>(null)
  const [img, setImg] = useState<HTMLImageElement|null>(null)
  const [cols, setCols] = useState(0)

  useEffect(()=>{
    if (!img || !canvasRef.current) return
    const ctx = canvasRef.current.getContext('2d')!
    const tW = TILE_PX * scale
  const rows = Math.ceil(img.height / TILE_PX)
  const c = Math.ceil(img.width / TILE_PX)
    setCols(c)
    canvasRef.current.width = c * tW
    canvasRef.current.height = rows * tW
    ctx.clearRect(0,0,canvasRef.current.width,canvasRef.current.height)
    for(let y=0;y<rows;y++){
      for(let x=0;x<c;x++){
        const sx = x * TILE_PX
        const sy = y * TILE_PX
        const dx = x * tW
        const dy = y * tW
        ctx.drawImage(img, sx, sy, TILE_PX, TILE_PX, dx, dy, tW, tW)
        ctx.strokeStyle = 'rgba(0,0,0,0.2)'
        ctx.strokeRect(dx,dy,tW,tW)
      }
    }
    // highlight selected (selected is 1-based; convert to 0-based index)
    if (selected != null){
      const selIdx = Math.max(0, selected - 1)
      const sx = (selIdx % c) * tW
      const sy = Math.floor(selIdx / c) * tW
      ctx.strokeStyle = 'yellow'
      ctx.lineWidth = 2
      ctx.strokeRect(sx+1, sy+1, tW-2, tW-2)
    }
  }, [img, selected])

  // expose scale control if needed in future
  useEffect(()=>{
    onTilesetLoad && onTilesetLoad(img)
  }, [img])

  useEffect(()=>{
    // try to auto-load assets from either Electron IPC (if available) or dev server paths
    ;(async ()=>{
      // If running inside Electron with our preload API, prefer that (reads from disk)
      try{
        const win = window as any
        if (win && win.electron && typeof win.electron.readAsset === 'function'){
          const dataUrl = await win.electron.readAsset('famidash.bmp')
          if (dataUrl){
            const auto = new Image()
            auto.onload = ()=>{
              setImg(auto)
              onTilesetLoad && onTilesetLoad(auto)
            }
            auto.src = dataUrl
            return
          }
        }
      }catch(e){/*ignore*/}

      const candidates = ['/src/renderer/assets/famidash.bmp','/famidash.bmp']
      for (const url of candidates){
        try{
          const res = await fetch(url, { method: 'HEAD' })
          if (res.ok){
            const auto = new Image()
            auto.onload = ()=>{
              setImg(auto)
              onTilesetLoad && onTilesetLoad(auto)
            }
            auto.onerror = ()=>{}
            auto.src = url
            return
          }
        }catch(e){
          // try next
        }
      }
    })()
  }, [])

  function handleClick(e:React.MouseEvent){
    if (!canvasRef.current || !img) return
    const rect = canvasRef.current.getBoundingClientRect()
    const x = Math.floor((e.clientX - rect.left) / (TILE_PX * scale))
    const y = Math.floor((e.clientY - rect.top) / (TILE_PX * scale))
    const cx = Math.max(0, Math.min(cols - 1, x))
    const rows = Math.ceil(img.height / TILE_PX)
    const cy = Math.max(0, Math.min(rows - 1, y))
    const idx = cy * cols + cx
    onSelect(idx+1) // use 1-based tile indices in editor (0 = empty)
  }

  function loadFile(f:File|null){
    if (!f) return
    const url = URL.createObjectURL(f)
    const image = new Image()
    image.onload = ()=>{
      setImg(image)
      onTilesetLoad && onTilesetLoad(image)
      URL.revokeObjectURL(url)
    }
    image.src = url
  }

  return (
    <div>
      <h4>Tileset</h4>
      <div style={{marginBottom:8}}>
        <input id="tileset-file" type="file" accept="image/*" onChange={(e)=>loadFile(e.target.files?.[0] ?? null)} />
      </div>
      <canvas ref={canvasRef} style={{display: img ? 'block' : 'none', cursor:'pointer'}} onClick={handleClick} />
      {!img && <div style={{color:'#aaa'}}>Load a tileset image (16x16 tiles). You can pick `famidash.bmp`.</div>}
    </div>
  )
}
