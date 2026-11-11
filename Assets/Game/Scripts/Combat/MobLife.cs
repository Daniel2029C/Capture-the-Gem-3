namespace CTG.Combat
{
    /// <summary>
    /// The life of a mob: a resource pool that takes plain hits (no defense, no immunity) and can regenerate. Mobs
    /// decide for themselves what a hit does besides losing life (flash, knockback, dying).
    /// </summary>
    public sealed class MobLife : ResourcePool
    {
        public MobLife(int max, float regenPerSecond = 0f) : base(max, regenPerSecond) { }

        public bool IsDead => Current <= 0;

        /// <summary>
        /// Takes the damage of a hit, never more than what is left. Returns the damage actually dealt: 0 if already dead
        /// or the hit does nothing.
        /// </summary>
        public int TakeHit(in HitInfo hit)
        {
            if (IsDead) return 0;

            int damage = CombatRules.ComputeDamage(hit.Damage, 0);
            if (damage <= 0) return 0;

            damage = System.Math.Min(damage, Current);
            Current -= damage;
            return damage;
        }

        public void Kill() => Current = 0;

        /// <summary>Regenerates for one tick (if it has any regeneration and it is not blocked).</summary>
        public void Tick() => TickFlow(0f, drainBlocksRegen: false);
    }
}
