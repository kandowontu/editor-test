-- Famidash Physics Debug Script for Mesen
-- Outputs detailed frame-by-frame physics data to compare with simulator

local logFile = io.open("famidash_physics_log.txt", "w")
local frameCount = 0
local lastPadFrame = -999

-- Get label addresses using Mesen's debug symbols
local playerXAddr = emu.getLabelAddress("_player_x")
local playerYAddr = emu.getLabelAddress("_player_y")
local playerVelXAddr = emu.getLabelAddress("_player_vel_x")
local playerVelYAddr = emu.getLabelAddress("_player_vel_y")
local playerGravityAddr = emu.getLabelAddress("_player_gravity")
local playerMiniAddr = emu.getLabelAddress("_player_mini")
local gamemodeAddr = emu.getLabelAddress("_gamemode")
local dashingAddr = emu.getLabelAddress("_dashing")

function readInt16(baseAddr, memType)
    local low = emu.read(baseAddr, memType, false)
    local high = emu.read(baseAddr + 1, memType, false)
    local value = low + (high * 256)
    -- Convert to signed
    if value >= 32768 then
        value = value - 65536
    end
    return value
end

function readUInt8(addr, memType)
    return emu.read(addr, memType, false)
end

function logMessage(msg)
    if logFile then
        logFile:write(msg .. "\n")
        logFile:flush()
    end
    emu.log(msg)
end

function frameCallback()
    frameCount = frameCount + 1
    
    -- Read key variables using label addresses (player arrays indexed at [0] for player 1)
    local playerX = readInt16(playerXAddr.address, playerXAddr.memType)
    local playerY = readInt16(playerYAddr.address, playerYAddr.memType)
    local playerVelX = readInt16(playerVelXAddr.address, playerVelXAddr.memType)
    local playerVelY = readInt16(playerVelYAddr.address, playerVelYAddr.memType)
    local playerGravity = readUInt8(playerGravityAddr.address, playerGravityAddr.memType)
    local mini = readUInt8(playerMiniAddr.address, playerMiniAddr.memType)
    local gamemode = readUInt8(gamemodeAddr.address, gamemodeAddr.memType)
    local dashing = readUInt8(dashingAddr.address, dashingAddr.memType)
    
    -- Convert fixed point to pixels for Y
    local playerY_px = math.floor(playerY / 256)
    local playerX_px = math.floor(playerX / 256)
    
    -- Check for pad collision (simplified - just check if velocity was set to pad value)
    -- Yellow pad normal: 0x7C0 = 1984, mini: 0x680 = 1664
    local isPadFrame = false
    if mini == 0 then
        if playerVelY == -0x7C0 or playerVelY == 0x7C0 then
            isPadFrame = true
        end
    else
        if playerVelY == -0x680 or playerVelY == 0x680 then
            isPadFrame = true
        end
    end
    
    -- Log every frame, but highlight pad frames
    if isPadFrame and (frameCount - lastPadFrame) > 5 then
        logMessage(string.format("[PAD_FRAME_%d] ========================================", frameCount))
        logMessage(string.format("  PAD SET velY to 0x%04X (signed: %d)", playerVelY & 0xFFFF, playerVelY))
        lastPadFrame = frameCount
    end
    
    -- Log detailed state
    logMessage(string.format("Frame %d:", frameCount))
    logMessage(string.format("  posY=0x%04X (%dpx), velY=0x%04X (%d)", 
        playerY & 0xFFFF, playerY_px, playerVelY & 0xFFFF, playerVelY))
    logMessage(string.format("  posX=0x%04X (%dpx), velX=0x%04X (%d)", 
        playerX & 0xFFFF, playerX_px, playerVelX & 0xFFFF, playerVelX))
    logMessage(string.format("  gravity=0x%02X, mini=%d, gamemode=%d, dashing=%d", 
        playerGravity, mini, gamemode, dashing))
    logMessage("")
end

emu.addEventCallback(frameCallback, emu.eventType.endFrame)
logMessage("=== Famidash Physics Debug Log Started ===")
logMessage("")
