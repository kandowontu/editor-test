const fs = require('fs')
const path = require('path')

const root = path.resolve(__dirname, '..')
const srcAssets = path.join(root, 'src', 'renderer', 'assets')
if (!fs.existsSync(srcAssets)) fs.mkdirSync(srcAssets, { recursive: true })

const files = ['famidash.bmp', 'sprites.png']
for (const f of files){
  const src = path.join(root, f)
  const dest = path.join(srcAssets, f)
  try{
    if (fs.existsSync(src)){
      fs.copyFileSync(src, dest)
      console.log('Copied', src, '->', dest)
    } else {
      // not present at root; skip
    }
  }catch(e){
    console.error('Failed to copy', src, '->', dest, e && e.message)
  }
}
