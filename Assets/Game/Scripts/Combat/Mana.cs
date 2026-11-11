namespace CTG.Combat
{
    /// <summary>
    /// Mana: a resource pool that can be spent. Regeneration and drain net out (a drain does not stop regeneration by
    /// itself; a "blocks regeneration" effect does).
    /// </summary>
    public class Mana : ResourcePool
    {
        public Mana(int max, float regenPerSecond) : base(max, regenPerSecond) { }

        public bool TrySpend(int amount)
        {
            if (amount < 0 || Current < amount) return false;

            Current -= amount;
            return true;
        }

        public void Tick() => TickFlow(0f, drainBlocksRegen: false);
    }
}
