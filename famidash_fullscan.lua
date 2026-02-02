-- Full memory scanner - checks all memory types

local frameCount = 0
local scanned = false

function scanMemoryType(memType, name, startAddr, endAddr)
    emu.log(string.format("\n=== Scanning %s ($%04X-$%04X) ===", name, startAddr, endAddr))
    local foundCount = 0
    for addr = startAddr, endAddr do
        local val = emu.read(addr, memType)
        if val ~= 0 and foundCount < 50 then  -- Limit output
            emu.log(string.format("  $%04X = $%02X (%d)", addr, val, val))
            foundCount = foundCount + 1
        end
    end
    if foundCount == 0 then
        emu.log("  (all zeros)")
    elseif foundCount >= 50 then
        emu.log("  (showing first 50 non-zero values)")
    end
end

function frameCallback()
    frameCount = frameCount + 1
    
    if frameCount == 180 and not scanned then
        scanned = true
        emu.log("========================================")
        emu.log("FULL MEMORY SCAN AT FRAME 180")
        emu.log("========================================")
        
        -- Try all memory types
        scanMemoryType(emu.memType.cpu, "CPU Memory", 0x0000, 0x07FF)
        scanMemoryType(emu.memType.workRam, "Work RAM", 0x0000, 0x07FF)
        scanMemoryType(emu.memType.saveRam, "Save RAM", 0x0000, 0x1FFF)
        
        emu.log("\n========================================")
        emu.log("SCAN COMPLETE")
        emu.log("========================================")
    end
end

emu.addEventCallback(frameCallback, emu.eventType.endFrame)
emu.log("=== Full memory scanner started ===")
emu.log("Will scan at frame 180 - MAKE SURE GAME IS PLAYING A LEVEL!")
