-- TAS Capture for Mesen (NES famidash)
-- Based on MainWindow.MesenOverlay.cs replay tracer (known working pattern).
-- Uses DIRECT addresses + emu.memType.nesMemory (no getLabelAddress, since
-- some labels aren't exported by every build of the .dbg).
--
-- Output: tas_capture.csv  (in Mesen working directory)
--
-- Addresses (from Famidash.dbg, current build):
--   _player_x        $043D  uint16 (screen-relative, lo|hi)
--   _player_y        $0441  uint16 (screen-relative)
--   _player_vel_x    $0445  int16
--   _player_vel_y    $0449  int16
--   _currplayer_gravity $0070  uint8 (zeropage)
--   _currplayer_mini    $0067  uint8 (zeropage)
--   _gamemode           $007A
--   _cube_data[0]       $007B
--   _scroll_x        $04A6  uint32 world pixel scroll
--   _scroll_y        $04AA  uint16 (lo + hi*240 = linear pixel Y)
--   _scroll_y_subpx  $04AC  uint8
--
-- World coords (matches PathfinderEngine PF coords):
--   px = scroll_x + (player_x >> 8) + 8
--   py = scroll_y_linear + (player_y >> 8) + 8

local logFile = io.open("tas_capture.csv", "w")
local frameCount = 0

if logFile then
    logFile:write("frame,worldX,worldY,X_screen,Y_screen,X_fixed,Y_fixed,VelX_fixed,VelY_fixed,scrollX,scrollY_linear,scrollY_raw,scrollY_subpx,gravity,mini,gamemode,cubedata,onGround,jblocked\n")
    logFile:flush()
end

local function rd8(a)  return emu.read  (a, emu.memType.nesMemory) or 0 end
local function rd16(a) return emu.read16(a, emu.memType.nesMemory) or 0 end
local function rd32(a) return emu.read32(a, emu.memType.nesMemory) or 0 end

local function s16(v)
    if v >= 0x8000 then return v - 0x10000 end
    return v
end

emu.addEventCallback(function()
    local rawX     = rd16(0x043D)
    local rawY     = rd16(0x0441)
    local velX     = s16(rd16(0x0445))
    local velY     = s16(rd16(0x0449))
    local gravity  = rd8 (0x0070)
    local mini     = rd8 (0x0067)
    local gamemode = rd8 (0x007A)
    local cubedata = rd8 (0x007B)
    local scrollX  = rd32(0x04A6)
    local sy_raw   = rd16(0x04AA)
    local sy_lo    = sy_raw & 0xFF
    local sy_hi    = (sy_raw >> 8) & 0xFF
    local scrollY  = sy_lo + sy_hi * 240
    local sy_subpx = rd8 (0x04AC)

    -- Skip non-gameplay frames: scrollX is huge garbage at title/boot
    -- (e.g. 0xFFFFFF82). Only log when we're actually in-level.
    if scrollX < 0 or scrollX >= 524288 then return end

    frameCount = frameCount + 1

    local px_screen = rawX >> 8
    local py_screen = rawY >> 8
    local worldX = scrollX + px_screen + 8
    local worldY = scrollY + py_screen + 8

    local onGround = (cubedata & 0x01) ~= 0 and 1 or 0
    local jblocked = (cubedata & 0x02) ~= 0 and 1 or 0

    if logFile then
        logFile:write(string.format(
            "%d,%d,%d,%d,%d,0x%04X,0x%04X,%d,%d,%d,%d,0x%04X,%d,%d,%d,%d,0x%02X,%d,%d\n",
            frameCount,
            worldX, worldY,
            px_screen, py_screen,
            rawX, rawY,
            velX, velY,
            scrollX, scrollY, sy_raw, sy_subpx,
            gravity, mini, gamemode, cubedata,
            onGround, jblocked))
        logFile:flush()
    end

    emu.drawString(2, 10, string.format("F:%d wX:%d wY:%d", frameCount, worldX, worldY), 0xFFFFFF, 0x40000000)
    emu.drawString(2, 20, string.format("scrX:%d pX:%d VY:%d", scrollX, px_screen, velY),  0xFFFF00, 0x40000000)
    emu.drawString(2, 30, string.format("cd:%02X gm:%d mi:%d gv:%02X", cubedata, gamemode, mini, gravity), 0x00FF00, 0x40000000)
end, emu.eventType.endFrame)

emu.displayMessage("Script", "TAS Capture loaded -> tas_capture.csv")
