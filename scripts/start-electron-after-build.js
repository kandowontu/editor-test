const fs = require('fs')
const { spawn } = require('child_process')
const path = require('path')

const target = path.resolve(__dirname, '..', 'dist-electron', 'main.js')

function waitForFile(file, cb){
  const tick = ()=>{
    if (fs.existsSync(file)) return cb()
    setTimeout(tick, 150)
  }
  tick()
}

console.log('Waiting for Electron main build at', target)
waitForFile(target, ()=>{
  console.log('Found main bundle, launching Electron...')
  const env = Object.assign({}, process.env, { NODE_ENV: 'development' })

  // Try to resolve local electron binary first (node_modules/.bin)
  const tryPaths = []
  if (process.platform === 'win32') {
    tryPaths.push(path.resolve(process.cwd(), 'node_modules', '.bin', 'electron.cmd'))
    tryPaths.push(path.resolve(process.cwd(), 'node_modules', 'electron', 'dist', 'electron', 'electron.exe'))
  } else {
    tryPaths.push(path.resolve(process.cwd(), 'node_modules', '.bin', 'electron'))
  }
  // fallback to global 'electron' on PATH
  tryPaths.push(process.platform === 'win32' ? 'electron.cmd' : 'electron')

  let launched = false
  (function tryNext(i){
    if (i >= tryPaths.length) {
      console.error('Failed to launch Electron: no candidate executable worked')
      return
    }
    const cmd = tryPaths[i]
    console.log('Attempting to spawn Electron from:', cmd)
    try {
      const proc = spawn(cmd, ['.'], {stdio:'inherit', env, shell: false})
      proc.on('error', (err)=>{
        console.error('Spawn error for', cmd, err && err.message)
        // try next candidate
        tryNext(i+1)
      })
      proc.on('exit', code=> process.exit(code))
      launched = true
    } catch (e){
      console.error('Exception spawning', cmd, e && e.message)
      tryNext(i+1)
    }
  })(0)
})
