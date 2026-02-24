-- Famidash NES Frame Capture for Mesen
-- Based on famidash_debug.lua (known working pattern)
-- Records per-frame physics state for comparison with PathfinderEngine trace

local logFile = io.open("famidash_nes_capture.csv", "w")
local frameCount = 0

if logFile then
    logFile:write("frame,X_fixed,Y_fixed,VelY_fixed,VelX_fixed,X_px,Y_px,gravity,mini,gamemode,onGround\n")
    logFile:flush()
end

-- Use the SAME labels as the working famidash_debug.lua / sprite hitbox lua
local playerxaddr = emu.getLabelAddress("_player_x")
local playeryaddr = emu.getLabelAddress("_player_y")
local playervelxaddr = emu.getLabelAddress("_player_vel_x")
local playervelyaddr = emu.getLabelAddress("_player_vel_y")
local playergravityaddr = emu.getLabelAddress("_player_gravity")
local playerminiaddr = emu.getLabelAddress("_player_mini")
local gamemodeaddr = emu.getLabelAddress("_gamemode")
local gamestateaddr = emu.getLabelAddress("_gameState")
local cubedataaddr = emu.getLabelAddress("_cube_data")

function readInt16(addr, memType)
    local low = emu.read(addr, memType, false)
    local high = emu.read(addr + 1, memType, false)
    local value = low + (high * 256)
    if value >= 32768 then
        value = value - 65536
    end
    return value
end

function readUInt16(addr, memType)
    local low = emu.read(addr, memType, false)
    local high = emu.read(addr + 1, memType, false)
    return low + (high * 256)
end

function readUInt8(addr, memType)
    return emu.read(addr, memType, false)
end

function logMessage(msg)
    if logFile then
        logFile:write(msg .. "\n")
        logFile:flush()
    end
end

function frameCallback()
    -- Only log during gameplay (STATE_GAME = 2)
    local gamestate = readUInt8(gamestateaddr.address, gamestateaddr.memType)
    if gamestate ~= 0x02 then
        return
    end

    frameCount = frameCount + 1

    -- Read player 1 state (index 0 in the player arrays)
    local playerX = readUInt16(playerxaddr.address, playerxaddr.memType)
    local playerY = readUInt16(playeryaddr.address, playeryaddr.memType)
    local playerVelX = readInt16(playervelxaddr.address, playervelxaddr.memType)
    local playerVelY = readInt16(playervelyaddr.address, playervelyaddr.memType)
    local gravity = readUInt8(playergravityaddr.address, playergravityaddr.memType)
    local mini = readUInt8(playerminiaddr.address, playerminiaddr.memType)
    local gamemode = readUInt8(gamemodeaddr.address, gamemodeaddr.memType)
    local cubedata = readUInt8(cubedataaddr.address, cubedataaddr.memType)

    local playerX_px = math.floor(playerX / 256)
    local playerY_px = math.floor(playerY / 256)
    local onGround = (playerVelY == 0) and 1 or 0

    -- CSV line matching PF trace format
    logMessage(string.format(
        "%d,0x%04X,0x%04X,0x%04X,0x%04X,%d,%d,%d,%d,%d,%d",
        frameCount,
        playerX & 0xFFFF,
        playerY & 0xFFFF,
        playerVelY & 0xFFFF,
        playerVelX & 0xFFFF,
        playerX_px,
        playerY_px,
        gravity, mini, gamemode, onGround
    ))

    -- Draw on screen so we know it's working
    emu.drawString(2, 10, string.format("F:%d X:%d Y:%d VY:%d", frameCount, playerX_px, playerY_px, playerVelY), 0xFFFFFF, 0x40000000)
    emu.drawString(2, 20, string.format("0x%04X 0x%04X cd:%02X", playerX, playerY, cubedata), 0xFFFF00, 0x40000000)
end

emu.addEventCallback(frameCallback, emu.eventType.endFrame)
emu.displayMessage("Script", "NES Capture loaded - logging to famidash_nes_capture.csv")
