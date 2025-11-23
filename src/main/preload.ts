import { contextBridge, ipcRenderer } from 'electron'

contextBridge.exposeInMainWorld('electron', {
  send: (channel: string, data: any) => ipcRenderer.send(channel, data),
  on: (channel: string, cb: (event: any, ...args: any[]) => void) => ipcRenderer.on(channel, cb),
  // read a local asset (returns dataURL string) - works both in dev and packaged app
  readAsset: (filename: string) => ipcRenderer.invoke('read-asset', filename)
})
