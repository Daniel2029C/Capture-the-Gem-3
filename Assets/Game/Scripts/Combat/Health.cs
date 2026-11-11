using System;

namespace CTG.Combat
{
    /// <summary>How a piece of damage interacts with post-hit immunity.</summary>
    [Flags]
    public enum DamageFlags
    {
        /// <summary>An attack: stopped by immunity, and starts immunity.</summary>
        None = 0,
        /// <summary>Lands even while immune.</summary>
        IgnoresImmunity = 1,
        /// <summary>Does not start immunity afterwards.</summary>
        DoesNotStartImmunity = 2,
        /// <summary>Damage from the environment (lava): ignores immunity and does not start it, which stays with attacks.</summary>
        Environmental = IgnoresImmunity | DoesNotStartImmunity,
    }

    /// <summary>
    /// Life: a resource pool that can be damaged, with post-hit immunity, death and the respawn countdown, all advanced
    /// per tick. Draining stops regeneration. Respawn time is a property so class or game state can set it.
    /// </summary>
    public class Health : ResourcePool
    {
        /// <summary>Extra life per second from the area the player is in (the spawn room); set each tick.</summary>
        public float ZoneRegenPerSecond { get; set; }

        /// <summary>Ticks of immunity after taking a hit.</summary>
        public int HitImmunityTicks { get; set; }

        /// <summary>Ticks from death to respawn.</summary>
        public int RespawnTicks { get; set; }

        public int ImmunityTicksLeft { get; private set; }
        public int RespawnTicksLeft { get; private set; }
        public bool IsDead { get; private set; }
        public bool IsImmune => ImmunityTicksLeft > 0;
        /// <summary>Who dealt the damage that killed this, or null (the environment, a drain, no source given).</summary>
        public IDamageable Killer { get; private set; }

        public event Action<int> Damaged;
        /// <summary>Raised once when life runs out; <see cref="Killer"/> is already set.</summary>
        public event Action Died;
        public event Action Respawned;

        public Health(int max, float regenPerSecond, int hitImmunityTicks, int respawnTicks) : base(max, regenPerSecond)
        {
            HitImmunityTicks = hitImmunityTicks;
            RespawnTicks = respawnTicks;
        }

        /// <summary>Applies damage unless dead (or immune, for an attack). Returns the damage dealt.</summary>
        /// <param name="source">Who is responsible, recorded as <see cref="Killer"/> if this kills.</param>
        public int TakeDamage(int amount, DamageFlags flags = DamageFlags.None, IDamageable source = null)
        {
            if (IsDead || amount <= 0) return 0;
            if (IsImmune && (flags & DamageFlags.IgnoresImmunity) == 0) return 0;

            Current -= amount;
            if ((flags & DamageFlags.DoesNotStartImmunity) == 0) ImmunityTicksLeft = HitImmunityTicks;
            Damaged?.Invoke(amount);

            if (Current <= 0) Kill(source);
            return amount;
        }

        /// <summary>Restores life up to the maximum. Returns the amount actually restored.</summary>
        public int Heal(int amount) => IsDead ? 0 : Restore(amount);

        /// <summary>Restores full life and clears immunity (class selection, respawn-like resets).</summary>
        public override void Refill()
        {
            base.Refill();
            ImmunityTicksLeft = 0;
        }

        public void Kill(IDamageable killer = null)
        {
            if (IsDead) return;

            Current = 0;
            IsDead = true;
            ImmunityTicksLeft = 0;
            ResetProgress();
            RespawnTicksLeft = RespawnTicks;
            Killer = killer;
            Died?.Invoke();
        }

        protected override void OnDepleted() => Kill();

        /// <summary>Returns true on the tick the respawn countdown finishes.</summary>
        public bool Tick()
        {
            if (IsDead)
            {
                if (--RespawnTicksLeft > 0) return false;

                IsDead = false;
                Current = Max;
                Killer = null;
                Respawned?.Invoke();
                return true;
            }

            if (ImmunityTicksLeft > 0) ImmunityTicksLeft--;
            TickFlow(ZoneRegenPerSecond, drainBlocksRegen: true);
            return false;
        }
    }
}
