using System;
using System.Windows;

namespace FamidashEditor
{
    public partial class DebugInfoWindow : Window
    {
        public DebugInfoWindow()
        {
            InitializeComponent();
        }

        // Method to update all debug fields
        public void UpdateDebugInfo(
            int gameMode, bool mini, bool invertedGravity,
            int xPos, int yPos, string xSpeed, int ninjaJumps,
            bool dBlocked, bool hBlocked, bool fBlocked, bool jBlocked,
            bool orbed, bool blackOrbed, int dashing, int robotJumpTime,
            int playerYVel, bool bufferActive, bool freeCam)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (DbgGameModeText != null) DbgGameModeText.Text = $"Game mode: {gameMode}";
                    if (DbgMiniText != null) DbgMiniText.Text = $"Mini: {mini}";
                    if (DbgInvertedGravityText != null) DbgInvertedGravityText.Text = $"Inverted gravity: {invertedGravity}";
                    if (DbgXPosText != null) DbgXPosText.Text = $"X position: {xPos} (0x{xPos:X})";
                    if (DbgYPosText != null) DbgYPosText.Text = $"Y position: {yPos} (0x{yPos:X})";
                    if (DbgXSpeedText != null) DbgXSpeedText.Text = $"X speed: {xSpeed}";
                    if (DbgNinjaJumpsText != null) DbgNinjaJumpsText.Text = $"Ninja jumps: {ninjaJumps}";
                    if (DbgDBlockedText != null) DbgDBlockedText.Text = $"D blocked: {dBlocked}";
                    if (DbgHBlockedText != null) DbgHBlockedText.Text = $"H blocked: {hBlocked}";
                    if (DbgFBlockedText != null) DbgFBlockedText.Text = $"F blocked: {fBlocked}";
                    if (DbgJBlockedText != null) DbgJBlockedText.Text = $"J blocked: {jBlocked}";
                    if (DbgOrbedText != null) DbgOrbedText.Text = $"Orbed: {orbed}";
                    if (DbgBlackOrbedText != null) DbgBlackOrbedText.Text = $"Black orbed: {blackOrbed}";
                    if (DbgDashingText != null) DbgDashingText.Text = $"Dashing: {dashing}";
                    if (DbgRobotJumpTimeText != null) DbgRobotJumpTimeText.Text = $"Robot jump time: {robotJumpTime}";
                    if (DbgPlayerYVelText != null) DbgPlayerYVelText.Text = $"Player Y vel: {playerYVel} (0x{playerYVel:X})";
                    if (DbgBufferActiveText != null) DbgBufferActiveText.Text = $"Buffer active: {bufferActive}";
                    if (DbgFreeCamText != null) DbgFreeCamText.Text = $"FreeCam: {freeCam}";
                });
            }
            catch { }
        }
    }
}
