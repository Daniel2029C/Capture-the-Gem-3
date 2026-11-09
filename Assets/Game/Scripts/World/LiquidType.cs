namespace CTG.World
{
    /// <summary>The liquid filling a tile. A tile holds at most one, and only when it has no block.</summary>
    public enum LiquidType : byte
    {
        None = 0,
        /// <summary>Burns: damage over time and the burning debuff.</summary>
        Lava = 1,
        /// <summary>Slows movement and grants a timed regeneration buff.</summary>
        Nectar = 2,
    }
}
