-- Simple test script to check if memory reading works at all

local frameCount = 0

function frameCallback()
    frameCount = frameCount + 1
    
    -- Try different memory types
    local val_cpu = emu.read(0x043D, emu.memType.cpu)
    local val_cpuDebug = emu.read(0x043D, emu.memType.cpuDebug)
    local val_workRam = emu.read(0x043D, emu.memType.workRam)
    
    -- Try reading from zeropage
    local val_zp = emu.read(0x007A, emu.memType.cpu)
    
    -- Try reading multiple consecutive bytes
    local bytes = {}
    for i = 0, 7 do
        bytes[i] = emu.read(0x043D + i, emu.memType.cpu)
    end
    
    if frameCount % 60 == 0 then
        emu.log(string.format("Frame %d:", frameCount))
        emu.log(string.format("  0x043D cpu=%02X debug=%02X workRam=%02X", val_cpu, val_cpuDebug, val_workRam))
        emu.log(string.format("  0x007A (gamemode) = %02X", val_zp))
        emu.log(string.format("  0x043D-0x0444: %02X %02X %02X %02X %02X %02X %02X %02X", 
            bytes[0], bytes[1], bytes[2], bytes[3], bytes[4], bytes[5], bytes[6], bytes[7]))
        emu.log("")
    end
end

emu.addEventCallback(frameCallback, emu.eventType.endFrame)
emu.log("=== Test script started - will log every 60 frames ===")
