local consoleType = emu.getState()["consoleType"]
if consoleType ~= "Nes" then
  emu.displayMessage("Script", "This script only works on the NES.")
  return
end

-- Collision hitbox definitions (matching collision.h)
local collisionHitboxes = {
  [0x07] = {{x=0, y=0, w=16, h=16}}, -- COL_ALL
  [0x06] = {{x=0, y=8, w=16, h=8}}, -- COL_BOTTOM
  [0x05] = {{x=0, y=0, w=16, h=8}}, -- COL_TOP
  [0x24] = {{x=0, y=0, w=8, h=16}}, -- COL_LEFT
  [0x25] = {{x=8, y=0, w=8, h=16}}, -- COL_RIGHT
  
  -- Quarters
  [0x20] = {{x=0, y=0, w=8, h=8}}, -- COL_UP_LEFT
  [0x21] = {{x=8, y=0, w=8, h=8}}, -- COL_UP_RIGHT
  [0x22] = {{x=0, y=8, w=8, h=8}}, -- COL_DOWN_LEFT
  [0x23] = {{x=8, y=8, w=8, h=8}}, -- COL_DOWN_RIGHT
  
  -- Diagonals
  [0x26] = {{x=0, y=0, w=8, h=8}, {x=8, y=8, w=8, h=8}}, -- COL_TOP_LEFT_BOTTOM_RIGHT
  [0x27] = {{x=8, y=0, w=8, h=8}, {x=0, y=8, w=8, h=8}}, -- COL_TOP_RIGHT_BOTTOM_LEFT
  
  -- Stairs
  [0x39] = {{x=0, y=0, w=16, h=8}, {x=8, y=8, w=8, h=8}}, -- COL_TOP_RIGHT_STAIRS
  [0x3a] = {{x=0, y=0, w=16, h=8}, {x=0, y=8, w=8, h=8}}, -- COL_TOP_LEFT_STAIRS
  [0x37] = {{x=0, y=8, w=16, h=8}, {x=8, y=0, w=8, h=8}}, -- COL_BOTTOM_RIGHT_STAIRS
  [0x38] = {{x=0, y=8, w=16, h=8}, {x=0, y=0, w=8, h=8}}, -- COL_BOTTOM_LEFT_STAIRS
  
  -- Death zones (spikes)
  [0x08] = {{x=4, y=4, w=8, h=8}}, -- COL_DEATH
  [0x02] = {{x=0, y=6, w=6, h=3}}, -- COL_DEATH_LEFT
  [0x01] = {{x=10, y=6, w=6, h=3}}, -- COL_DEATH_RIGHT
  [0x03] = {{x=5, y=0, w=3, h=6}}, -- COL_DEATH_TOP
  [0x04] = {{x=5, y=10, w=3, h=6}}, -- COL_DEATH_BOTTOM
  
  -- Corner deaths
  [0x36] = {{x=0, y=6, w=6, h=3}, {x=5, y=10, w=3, h=6}}, -- COL_DEATH_BOTTOM_LEFT
  [0x35] = {{x=10, y=6, w=6, h=3}, {x=5, y=10, w=3, h=6}}, -- COL_DEATH_BOTTOM_RIGHT
  [0x34] = {{x=0, y=6, w=6, h=3}, {x=5, y=0, w=3, h=6}}, -- COL_DEATH_TOP_LEFT
  [0x33] = {{x=10, y=6, w=6, h=3}, {x=5, y=0, w=3, h=6}}, -- COL_DEATH_TOP_RIGHT
  
  -- Single spikes
  [0x28] = {{x=2, y=8, w=4, h=8}}, -- COL_DOWN_LEFT_SPIKE
  [0x29] = {{x=10, y=8, w=3, h=8}}, -- COL_DOWN_RIGHT_SPIKE
  [0x2a] = {{x=2, y=8, w=4, h=8}, {x=10, y=8, w=4, h=8}}, -- COL_DOWN_BOTH_SPIKES
  
  [0x30] = {{x=2, y=0, w=4, h=8}}, -- COL_UP_LEFT_SPIKE
  [0x31] = {{x=10, y=0, w=3, h=8}}, -- COL_UP_RIGHT_SPIKE
  [0x32] = {{x=2, y=0, w=4, h=8}, {x=10, y=0, w=4, h=8}}, -- COL_UP_BOTH_SPIKES
  
  [0x2d] = {{x=2, y=0, w=4, h=8}}, -- COL_BOTTOM_LEFT_SPIKE
  [0x2e] = {{x=10, y=0, w=3, h=8}}, -- COL_BOTTOM_RIGHT_SPIKE
  [0x2f] = {{x=2, y=0, w=4, h=8}, {x=10, y=0, w=4, h=8}}, -- COL_BOTTOM_SPIKES
  
  [0x3b] = {{x=5, y=10, w=3, h=6}}, -- COL_TOP_SPIKES
  
  -- Spike blocks
  [0x2b] = {{x=2, y=0, w=4, h=8}, {x=0, y=8, w=8, h=8}}, -- COL_LEFT_SPIKE_BLOCK
  [0x2c] = {{x=10, y=0, w=3, h=8}, {x=8, y=8, w=8, h=8}}, -- COL_RIGHT_SPIKE_BLOCK
  
  [0x09] = {{x=0, y=0, w=16, h=16}}, -- COL_FLOOR_CEIL
}

function Main()

  gamestateaddr = emu.getLabelAddress("_gameState")

  gamestate = emu.read(gamestateaddr.address, gamestateaddr.memType, false)

  if gamestate ~= 0x02 then
    return
  end


  dualaddr = emu.getLabelAddress("_dual")
  dual = emu.read(dualaddr.address, dualaddr.memType, false)

  playerxaddr = emu.getLabelAddress("_player_x")
  playeryaddr = emu.getLabelAddress("_player_y")

  genericaddr = emu.getLabelAddress("_Generic")
  
  playerminiaddr = emu.getLabelAddress("_player_mini")

  gamemodeaddr = emu.getLabelAddress("_player_mini")

  player_x = emu.read(playerxaddr.address + 1, playerxaddr.memType, false)
  player_y = emu.read(playeryaddr.address + 1, playeryaddr.memType, false)
  
  player_mini = emu.read(playerminiaddr.address, playerminiaddr.memType, false)

  gamemode = emu.read(gamemodeaddr.address, gamemodeaddr.memType, false)

  player_width = emu.read(genericaddr.address + 2, genericaddr.memType, false)
  player_height = emu.read(genericaddr.address + 3, genericaddr.memType, false)

  emu.drawRectangle(player_x, player_y + ((0x10 - player_height) >> 1), player_width, player_height, 0xffff00, false)

  if dual == 1 then
    player_x = emu.read(playerxaddr.address + 3, playerxaddr.memType, false)
    player_y = emu.read(playeryaddr.address + 3, playeryaddr.memType, false)

    emu.drawRectangle(player_x, player_y + ((0x10 - player_height) >> 1), player_width, player_height, 0xffff00, false)
  end 

  actives = emu.getLabelAddress("_activesprites_active")
  
  types = emu.getLabelAddress("_activesprites_type")

  widths = emu.getLabelAddress("_sprite_widths")
  heights = emu.getLabelAddress("_sprite_heights")
  
  offsetsx = emu.getLabelAddress("_sprite_x_offset")
  offsetsy = emu.getLabelAddress("_sprite_y_offset")

  xs = emu.getLabelAddress("_activesprites_realx")
  ys = emu.getLabelAddress("_activesprites_realy")

  scrollxtable = emu.getLabelAddress("_scroll_x")
  scrollytable = emu.getLabelAddress("_scroll_y")

  for i=0,15 do
    sprActive = emu.read(actives.address + i, actives.memType, false)
    if sprActive > 0 then
      sprType = emu.read(types.address + i, types.memType, false)

      width = emu.read(widths.address + sprType, widths.memType, false)
      height = emu.read(heights.address + sprType, heights.memType, false)

      x = emu.read(xs.address + i, xs.memType, false)
      xoffset = emu.read(offsetsx.address + sprType, offsetsx.memType, true)

      y = emu.read(ys.address + i, ys.memType, false)
      yoffset = emu.read(offsetsy.address + sprType, offsetsy.memType, true)

      if height < 0xfc then 
        emu.drawRectangle(x + xoffset, y + yoffset, width, height, 0x7f0000ff, true)
      else
        emu.drawRectangle(x + xoffset, y + yoffset, 16, 16, 0x7fffffff, true)
      end
    end
  end
end
emu.addEventCallback(Main, emu.eventType.startFrame)
