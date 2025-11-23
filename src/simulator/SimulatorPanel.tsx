import React, {useEffect, useRef, useState} from 'react'
import Simulator, {LevelData} from './simulator'

const sim = new Simulator()

export default function SimulatorPanel(){
  const [running,setRunning]=useState(false)
  const [log,setLog]=useState<string[]>([])

  useEffect(()=>{
    return ()=>sim.stop()
  },[])

  function loadSample(){
    const level: LevelData = {width:16,height:14,tiles:new Array(16*14).fill(0)}
    sim.loadLevel(level)
    setLog(l=>[...l,'Loaded sample level'])
  }

  function start(){
    sim.start()
    setRunning(true)
    setLog(l=>[...l,'Simulator started'])
  }

  function stop(){
    sim.stop()
    setRunning(false)
    setLog(l=>[...l,'Simulator stopped'])
  }

  return (
    <div>
      <h3>Simulator</h3>
      <div style={{display:'flex',gap:8}}>
        <button onClick={loadSample}>Load sample</button>
        <button onClick={start} disabled={running}>Start</button>
        <button onClick={stop} disabled={!running}>Stop</button>
      </div>
      <div style={{marginTop:8,background:'#0b0b0f',padding:8,height:200,overflow:'auto'}}>
        {log.map((l,i)=>(<div key={i}>{l}</div>))}
      </div>
    </div>
  )
}
