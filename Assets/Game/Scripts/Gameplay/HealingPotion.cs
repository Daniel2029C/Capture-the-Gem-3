using CTG.Combat;
using CTG.Core;

namespace CTG.Gameplay
{
    /// <summary>
    /// The healing potion: restores life instantly, then can't be used again until the cooldown ends.
    /// Not an inventory item; every class has it on its own key and HUD icon.
    /// </summary>
    public sealed class HealingPotion
    {
        /// <summary>Life restored per use.</summary>
        public int HealAmount { get; set; } = 50;
        /// <summary>Ticks before the next use.</summary>
        public int CooldownTicks { get; set; } = 60 * SimUnits.TicksPerSecond;
        /// <summary>When false, drinking at full life does nothing and doesn't start the cooldown.</summary>
        public bool UsableAtFullLife { get; set; }

        public int CooldownLeft { get; private set; }
        public bool Ready => CooldownLeft <= 0;

        /// <summary>Clears the cooldown (death, class change).</summary>
        public void Reset() => CooldownLeft = 0;

        public void Tick()
        {
            if (CooldownLeft > 0) CooldownLeft--;
        }

        /// <summary>Returns the life actually restored, or -1 if the potion couldn't be used.</summary>
        public int TryUse(Health health)
        {
            if (!Ready || health.IsDead) return -1;
            if (!UsableAtFullLife && health.Current >= health.Max) return -1;

            CooldownLeft = CooldownTicks;
            return health.Heal(HealAmount);
        }
    }
}
