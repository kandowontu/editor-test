-- Memory scanner to find non-zero values in RAM

local frameCount = 0
local scanned = false

function frameCallback()
    frameCount = frameCount + 1
    
    -- Only scan once after a few frames (let game initialize)
    if frameCount == 120 and not scanned then
        scanned = true
        emu.log("=== Scanning RAM for non-zero values ===")
        
        -- Scan main RAM ($0000-$07FF)
        emu.log("\nNon-zero values in $0000-$00FF (zeropage):")
        for addr = 0x0000, 0x00FF do
            local val = emu.read(addr, emu.memType.cpu)
            if val ~= 0 then
                emu.log(string.format("  $%04X = $%02X (%d)", addr, val, val))
            end
        end
        
        emu.log("\nNon-zero values in $0400-$04FF (typical var area):")
        for addr = 0x0400, 0x04FF do
            local val = emu.read(addr, emu.memType.cpu)
            if val ~= 0 then
                emu.log(string.format("  $%04X = $%02X (%d)", addr, val, val))
            end
        end
        
        emu.log("\nNon-zero 16-bit values in $0400-$04FF:")
        for addr = 0x0400, 0x04FE, 2 do
            local low = emu.read(addr, emu.memType.cpu)
            local high = emu.read(addr + 1, emu.memType.cpu)
            if low ~= 0 or high ~= 0 then
                local val = low + (high * 256)
                emu.log(string.format("  $%04X-$%04X = $%04X (%d)", addr, addr+1, val, val))
            end
        end
        
        emu.log("\n=== Scan complete ===")
    end
    
    -- After scan, show live updates of a few likely addresses
    if scanned and frameCount % 60 == 0 then
        emu.log(string.format("\nFrame %d live check:", frameCount))
        -- Check a range around where we expect player data
        for addr = 0x0430, 0x0450, 4 do
            local val = emu.read(addr, emu.memType.cpu)
            local val16_low = emu.read(addr, emu.memType.cpu)
            local val16_high = emu.read(addr + 1, emu.memType.cpu)
            local val16 = val16_low + (val16_high * 256)
            emu.log(string.format("  $%04X: %02X, 16-bit: $%04X (%d)", addr, val, val16, val16))
        end
    end
end

emu.addEventCallback(frameCallback, emu.eventType.endFrame)
emu.log("=== Memory scanner started - will scan at frame 120 ===")
emu.log("Make sure the game is running and the player is visible!")
