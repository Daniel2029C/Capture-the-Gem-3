namespace CTG.Core
{
    /// <summary>
    /// The simulation works in fixed pixel/tick units so tuned gameplay values (CTG2's included) port verbatim:
    /// positions in pixels, velocities in pixels per tick, durations in ticks (60 per second).
    /// One tile is 16 px, and one tile is one Unity world unit for rendering.
    /// Coordinates are y-up (Unity convention).
    /// </summary>
    public static class SimUnits
    {
        public const int TicksPerSecond = 60;
        public const float TickSeconds = 1f / TicksPerSecond;
        public const int PixelsPerTile = 16;

        public static float PixelsToUnits(float px) => px / PixelsPerTile;
        public static float UnitsToPixels(float units) => units * PixelsPerTile;
        public static int PixelToTile(float px) => (int)System.Math.Floor(px / PixelsPerTile);
        public static int SecondsToTicks(float seconds) => (int)System.Math.Round(seconds * TicksPerSecond);
    }
}
