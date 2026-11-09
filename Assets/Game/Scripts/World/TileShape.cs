namespace CTG.World
{
    /// <summary>
    /// Shape of a tile. Players collide with the real shape (see PlayerCollision); projectiles, hooks and building
    /// treat every shaped tile as a full block.
    /// The names say which sides stay solid.
    /// </summary>
    public enum TileShape : byte
    {
        Full = 0,
        /// <summary>Only the bottom half.</summary>
        Half,
        /// <summary>Bottom and left sides solid: a triangle with its corner at the bottom-left.</summary>
        SlopeDownLeft,
        /// <summary>Bottom and right sides solid.</summary>
        SlopeDownRight,
        /// <summary>Top and left sides solid.</summary>
        SlopeUpLeft,
        /// <summary>Top and right sides solid.</summary>
        SlopeUpRight,
    }

    public static class TileShapes
    {
        /// <summary>True if pixel (x, y) of a 16×16 tile (y up from the bottom) is part of the shape.</summary>
        public static bool Covers(TileShape shape, int x, int y) => shape switch
        {
            TileShape.Half => y < 8,
            TileShape.SlopeDownLeft => x + y <= 15,
            TileShape.SlopeDownRight => y <= x,
            TileShape.SlopeUpLeft => y >= x,
            TileShape.SlopeUpRight => x + y >= 15,
            _ => true,
        };
    }
}
