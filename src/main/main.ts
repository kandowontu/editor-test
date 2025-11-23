import { app, BrowserWindow, ipcMain } from 'electron'
import * as path from 'path'
import * as fs from 'fs'

function createWindow() {
  console.log('createWindow: NODE_ENV=', process.env.NODE_ENV, ' __dirname=', __dirname)
  const win = new BrowserWindow({
    width: 1200,
    height: 800,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js')
    }
  })

  win.webContents.on('did-finish-load', () => {
    console.log('Renderer finished load, url=', win.webContents.getURL())
  })

  win.webContents.on('did-fail-load', (event, errorCode, errorDescription, validatedURL, isMainFrame) => {
    console.error('did-fail-load', {errorCode, errorDescription, validatedURL, isMainFrame})
  })

  if (process.env.NODE_ENV === 'development') {
    const url = 'http://localhost:5173'
    console.log('Loading dev url:', url)
    win.loadURL(url).catch(err=>console.error('loadURL error', err))
    // open devtools for debugging
    win.webContents.openDevTools({mode: 'undocked'})
  } else {
    const file = path.join(__dirname, '..', '..', 'dist-renderer', 'index.html')
    console.log('Loading prod file:', file)
    win.loadFile(file).catch(err=>console.error('loadFile error', err))
  }
}

app.whenReady().then(() => {
  createWindow()

  // expose a handler to read local image assets and return them as data URLs
  ipcMain.handle('read-asset', async (event, filename: string) => {
    try {
      // prefer project working dir first
      const candidate1 = path.resolve(process.cwd(), filename)
      const candidate2 = path.resolve(process.cwd(), 'src', 'renderer', 'assets', filename)
      const candidate3 = path.resolve(__dirname, '..', '..', filename)
      const candidate4 = path.resolve(__dirname, '..', 'renderer', 'assets', filename)
      const candidates = [candidate1, candidate2, candidate3, candidate4]
      let found: string | null = null
      for (const c of candidates){
        if (fs.existsSync(c)) { found = c; break }
      }
      if (!found) return null
      const buf = fs.readFileSync(found)
      // simple mime sniff by extension
      const ext = path.extname(found).toLowerCase()
      let mime = 'application/octet-stream'
      if (ext === '.png') mime = 'image/png'
      else if (ext === '.bmp') mime = 'image/bmp'
      else if (ext === '.jpg' || ext === '.jpeg') mime = 'image/jpeg'
      const dataUrl = `data:${mime};base64,${buf.toString('base64')}`
      return dataUrl
    } catch (e) {
      console.error('read-asset error', e)
      return null
    }
  })

  app.on('activate', function () {
    if (BrowserWindow.getAllWindows().length === 0) createWindow()
  })
})

app.on('window-all-closed', function () {
  if (process.platform !== 'darwin') app.quit()
})
