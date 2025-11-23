import React, {useEffect, useRef, useState} from 'react'

type Props = {
  selected: number
  onSelect: (sprite:number) => void
  onSpritesetLoad?: (img: HTMLImageElement|null) => void
  scale?: number
}

const SPRITE_PX = 16

// allow local scale control if needed later

export default function SpritePalette({selected, onSelect, onSpritesetLoad, scale=2}:Props){
  const canvasRef = useRef<HTMLCanvasElement|null>(null)
  const [img, setImg] = useState<HTMLImageElement|null>(null)
  const [cols, setCols] = useState(0)

  useEffect(()=>{
    ;(async ()=>{
      try{
        const win = window as any
        if (win && win.electron && typeof win.electron.readAsset === 'function'){
          const dataUrl = await win.electron.readAsset('sprites.png')
          if (dataUrl){
            const auto = new Image()
            auto.onload = ()=>{
              setImg(auto)
              onSpritesetLoad && onSpritesetLoad(auto)
            }
            auto.src = dataUrl
            return
          }
        }
      }catch(e){/*ignore*/}

      const candidates = ['/src/renderer/assets/sprites.png','/sprites.png']
      for (const url of candidates){
        try{
          const res = await fetch(url, { method: 'HEAD' })
          if (res.ok){
            const auto = new Image()
            auto.onload = ()=>{
              setImg(auto)
              onSpritesetLoad && onSpritesetLoad(auto)
            }
            auto.onerror = ()=>{}
            auto.src = url
            return
          }
        }catch(e){/*ignore*/}
      }
    })()
  }, [])

  useEffect(()=>{
    if (!img || !canvasRef.current) return
  const ctx = canvasRef.current.getContext('2d')!
  const tW = SPRITE_PX * scale
    const rows = Math.ceil(img.height / SPRITE_PX)
    const c = Math.ceil(img.width / SPRITE_PX)
    setCols(c)
    canvasRef.current.width = c * tW
    canvasRef.current.height = rows * tW
    ctx.clearRect(0,0,canvasRef.current.width,canvasRef.current.height)
    for(let y=0;y<rows;y++){
      for(let x=0;x<c;x++){
        const sx = x * SPRITE_PX
        const sy = y * SPRITE_PX
        const dx = x * tW
        const dy = y * tW
        ctx.drawImage(img, sx, sy, SPRITE_PX, SPRITE_PX, dx, dy, tW, tW)
        ctx.strokeStyle = 'rgba(0,0,0,0.2)'
        ctx.strokeRect(dx,dy,tW,tW)
      }
    }
    // highlight selected (selected is 1-based index)
    if (selected != null){
      const selIdx = Math.max(0, selected - 1)
      const sx = (selIdx % c) * tW
      const sy = Math.floor(selIdx / c) * tW
      ctx.strokeStyle = 'lime'
      ctx.lineWidth = 2
      ctx.strokeRect(sx+1, sy+1, tW-2, tW-2)
    }
  }, [img, selected])

  function handleClick(e:React.MouseEvent){
    if (!canvasRef.current || !img) return
    const rect = canvasRef.current.getBoundingClientRect()
    const x = Math.floor((e.clientX - rect.left) / (SPRITE_PX * scale))
    const y = Math.floor((e.clientY - rect.top) / (SPRITE_PX * scale))
    const cx = Math.max(0, Math.min(cols - 1, x))
    const rows = Math.ceil(img.height / SPRITE_PX)
    const cy = Math.max(0, Math.min(rows - 1, y))
    const idx = cy * cols + cx
    onSelect(idx+1)
  }

  function loadFile(f:File|null){
    if (!f) return
    const url = URL.createObjectURL(f)
    const image = new Image()
    image.onload = ()=>{
      setImg(image)
      onSpritesetLoad && onSpritesetLoad(image)
      URL.revokeObjectURL(url)
    }
    image.src = url
  }

  return (
    <div style={{marginTop:12}}>
      <h4>Sprites</h4>
      <div style={{marginBottom:8}}>
        <input id="spriteset-file" type="file" accept="image/*" onChange={(e)=>loadFile(e.target.files?.[0] ?? null)} />
      </div>
      <canvas ref={canvasRef} style={{display: img ? 'block' : 'none', cursor:'pointer'}} onClick={handleClick} />
      {!img && <div style={{color:'#aaa'}}>Load a sprites image (16x16 sprites). You can pick `sprites.png`.</div>}
    </div>
  )
}
