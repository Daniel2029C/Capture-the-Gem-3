using UnityEngine;

namespace CTG.Gameplay
{
    /// <summary>Which ability a class slot holds. Add a value here and a case in <see cref="AbilityFactory"/> for a new one.</summary>
    public enum AbilityId
    {
        None = 0,
        ArcherSprint,
        ArcherLightArrow,
        ArcherLuminite,
    }

    /// <summary>What an ability can see and touch while it ticks.</summary>
    public readonly struct AbilityContext
    {
        public readonly PlayerCharacter Player;
        public readonly int Tick;
        /// <summary>How fast cooldowns and recharges run: 1 normally, above 1 with cooldown reductions.</summary>
        public readonly float CooldownRate;

        public AbilityContext(PlayerCharacter player, int tick, float cooldownRate = 1f)
        {
            Player = player;
            Tick = tick;
            CooldownRate = cooldownRate;
        }

        public PlayerMotor Motor => Player.Motor;
    }

    /// <summary>
    /// One ability on a class slot (1-3). It is told each tick whether its key was pressed and whether it is held,
    /// and reports what the HUD needs. Cooldowns, charges and effects live inside the ability; abilities that
    /// share state (the Archer's) share a kit object passed in by the factory.
    /// </summary>
    public abstract class Ability
    {
        public abstract string Name { get; }

        /// <summary>Called every tick the player is alive.</summary>
        public abstract void Tick(in AbilityContext context, bool pressed, bool held);

        /// <summary>Back to the starting state (class change, death).</summary>
        public virtual void Reset() { }

        /// <summary>HUD bar fill, 0..1. For a cooldown, 1 = ready.</summary>
        public abstract float Fill { get; }

        /// <summary>Short HUD text under the name: a cooldown in seconds, a mode, a count.</summary>
        public virtual string Status => "";

        /// <summary>The ability is doing something right now (drawn highlighted).</summary>
        public virtual bool IsActive => false;

        /// <summary>Multiplies top running speed (and acceleration with it) while this ability is in effect.</summary>
        public virtual float RunSpeedScale => 1f;

        /// <summary>Extra multiplier on run acceleration only, on top of <see cref="RunSpeedScale"/>.</summary>
        public virtual float RunAccelerationScale => 1f;
    }

    /// <summary>The up-to-three abilities a player has, in slot order (index 0 = ability 1).</summary>
    public sealed class AbilitySet
    {
        public const int SlotCount = 3;
        readonly Ability[] m_Slots = new Ability[SlotCount];

        public Ability this[int index] => m_Slots[index];

        public void Set(int index, Ability ability) => m_Slots[index] = ability;

        public void Tick(in AbilityContext context, int pressedMask, int heldMask)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                m_Slots[i]?.Tick(context, (pressedMask & (1 << i)) != 0, (heldMask & (1 << i)) != 0);
            }
        }

        public void Reset()
        {
            for (int i = 0; i < SlotCount; i++) m_Slots[i]?.Reset();
        }

        public float RunAccelerationScale
        {
            get
            {
                float scale = 1f;
                for (int i = 0; i < SlotCount; i++)
                {
                    if (m_Slots[i] != null) scale *= m_Slots[i].RunAccelerationScale;
                }

                return scale;
            }
        }

        public float RunSpeedScale
        {
            get
            {
                float scale = 1f;
                for (int i = 0; i < SlotCount; i++)
                {
                    if (m_Slots[i] != null) scale *= m_Slots[i].RunSpeedScale;
                }

                return scale;
            }
        }
    }

    /// <summary>Default keys for the ability slots until per-class binding exists.</summary>
    public static class AbilityKeys
    {
        public static readonly string[] Labels = { "Shift", "E", "C" };
    }

    public static class AbilityFactory
    {
        /// <param name="classKit">The class's shared state, or null for classes without one.</param>
        public static Ability Create(AbilityId id, ClassKit classKit)
        {
            var kit = classKit as ArcherKit;
            switch (id)
            {
                case AbilityId.ArcherSprint: return new SprintAbility(kit != null ? kit.Stats.Sprint : new SprintStats());
                case AbilityId.ArcherLightArrow: return kit != null ? new LightArrowAbility(kit) : null;
                case AbilityId.ArcherLuminite: return kit != null ? new LuminiteAbility(kit) : null;
                default: return null;
            }
        }
    }
}
