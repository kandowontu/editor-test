export function downloadJson(obj:any, filename:string){
  const blob = new Blob([JSON.stringify(obj,null,2)], {type:'application/json'})
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

export function readJsonFile(file: File | undefined | null): Promise<any | null>{
  return new Promise((res)=>{
    if (!file) return res(null)
    const reader = new FileReader()
    reader.onload = ()=>{
      try {
        const txt = String(reader.result)
        res(JSON.parse(txt))
      } catch (e) { res(null) }
    }
    reader.onerror = ()=>res(null)
    reader.readAsText(file)
  })
}
