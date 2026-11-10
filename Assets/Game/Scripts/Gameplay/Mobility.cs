namespace CTG.Gameplay
{
    /// <summary>
    /// A movement feature a class can have: double jump, grappling hooks, fast fall and whatever comes next. A feature
    /// is a piece of data (its tuning) that knows how to switch itself on for a player and off again, so a class is
    /// described by listing features and the player code never has to know which kinds exist. Each variant (a
    /// different double jump, another kind of hook) is its own subclass or the same subclass with different numbers.
    /// A class should list at most one feature of each kind; if it lists two, the later one replaces the earlier.
    /// </summary>
    [System.Serializable]
    public abstract class Mobility
    {
        /// <summary>Gives the player this feature. Called when the class is selected.</summary>
        public abstract void Apply(PlayerCharacter player);

        /// <summary>Takes it away again. Called when the player's class changes.</summary>
        public abstract void Remove(PlayerCharacter player);
    }
}
