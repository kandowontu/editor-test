using System;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        private const byte TELEPORT_PORTAL_VERTICAL_ENTER = 0x4E;
        private const byte TELEPORT_PORTAL_SQUARE_ENTER = 0x59;
        private const byte TELEPORT_PORTAL_HORIZONTAL_ENTER_1 = 0x66;
        private const byte TELEPORT_PORTAL_HORIZONTAL_ENTER_2 = 0x68;
        private const byte TELEPORT_PORTAL_INVISIBLE_ENTER_1 = 0x75;
        private const byte TELEPORT_PORTAL_INVISIBLE_ENTER_2 = 0x77;

        /// <summary>
        /// NES teleport entrances read the shared teleport_output byte. Exit
        /// records publish that byte earlier in the universal slot-order pass;
        /// entrances never search the map for a matching exit.
        /// </summary>
        private void CheckTeleportPortals()
        {
            try
            {
                int playerX_px = (playerX_fixed >> 8) + 1;
                int playerY_px = (playerY_fixed >> 8) + GetMiniSpriteOffsetY();
                int hitboxW = currplayer_mini != 0 ? 8 : 15;
                int hitboxH = currplayer_mini != 0 ? 7 : 15;
                int playerRight_px = playerX_px + hitboxW - 1;
                int playerBottom_px = playerY_px + hitboxH - 1;

                for (int scan = 0; scan < SimulatorInteractionSpriteCount; scan++)
                {
                    int idx = SimulatorInteractionSpriteIndex(scan);
                    int sid = SimulatorInteractionSpriteId(idx);
                    bool isEntrance = sid == TELEPORT_PORTAL_VERTICAL_ENTER ||
                        sid == TELEPORT_PORTAL_SQUARE_ENTER ||
                        sid == TELEPORT_PORTAL_HORIZONTAL_ENTER_1 ||
                        sid == TELEPORT_PORTAL_HORIZONTAL_ENTER_2 ||
                        sid == TELEPORT_PORTAL_INVISIBLE_ENTER_1 ||
                        sid == TELEPORT_PORTAL_INVISIBLE_ENTER_2;
                    if (!isEntrance)
                        continue;

                    if (!SpriteIntersectsPlayer(
                        idx, sid,
                        playerX_px, playerRight_px,
                        playerY_px, playerBottom_px))
                        continue;

                    if (sid == TELEPORT_PORTAL_SQUARE_ENTER)
                    {
                        bool freshPress = System.Threading.Interlocked.CompareExchange(
                            ref keyXPressedCount, 0, 0) > 0;
                        bool squareGate = freshPress || orbBufferActive[currplayer];
                        if (!squareGate)
                            continue;

                        playerVelY_fixed = 0;
                        orbed[currplayer] = true;
                        orbBufferActive[currplayer] = false;
                        ballInputBufferCountdown[currplayer] = 0;
                    }

                    int destinationScreenY_px = simulatorTeleportOutputY_px & 0xFF;
                    playerY_fixed =
                        cameraY_fixed + (destinationScreenY_px << 8) + (playerY_fixed & 0xFF);
                    AppendSimDebug(
                        $"[TELEPORT_PORTAL] NES slot idx={idx} sid=0x{sid:X2} output=0x{simulatorTeleportOutputY_px & 0xFF:X2} screenY={destinationScreenY_px} worldY={playerY_fixed >> 8}");
                    break;
                }
            }
            catch (Exception ex)
            {
                AppendSimDebug($"[TELEPORT_PORTAL] ERROR: {ex.Message}");
            }
        }

        // Retained for legacy camera-mode bookkeeping. Physics mode uses the
        // active NES slot table and permits an entrance to fire while colliding.
        private System.Collections.Generic.HashSet<int> processedTeleportPortals =
            new System.Collections.Generic.HashSet<int>();
    }
}
