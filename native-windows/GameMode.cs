using System;

namespace FamidashEditor
{
    /// <summary>
    /// Game mode enumeration matching famidash.h definitions
    /// </summary>
    public enum GameMode
    {
        Cube = 0x00,
        Ship = 0x01,
        Ball = 0x02,
        Ufo = 0x03,
        Robot = 0x04,
        Spider = 0x05,
        Wave = 0x06,
        Swing = 0x07,      // Swingcopter
        Ninja = 0x08
    }
}
