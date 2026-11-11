namespace CTG.Gameplay
{
    /// <summary>
    /// Runtime state a class carries that its abilities and items share (the Archer's arrow charges, for example). A
    /// class describes how to make its kit in <see cref="Classes.ClassDef.CreateKit"/>; the player creates one when the
    /// class is selected, ticks it every tick and resets it on respawn.
    /// </summary>
    public abstract class ClassKit
    {
        /// <summary>Back to the starting state (respawn).</summary>
        public abstract void Reset();

        /// <param name="cooldownRate">How fast cooldowns run (above 1 = faster).</param>
        public virtual void Tick(float cooldownRate = 1f) { }
    }
}
