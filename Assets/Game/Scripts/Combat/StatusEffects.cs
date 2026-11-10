using System;
using System.Collections.Generic;
using CTG.Core;

namespace CTG.Combat
{
    public enum EffectKind
    {
        Buff,
        Debuff,
    }

    /// <summary>
    /// What an effect does to the player holding it, per tick it is active. Effects are summed into one of these
    /// (<see cref="StatusEffects.Aggregate"/>) and the player applies the result, so an effect only has to say what it
    /// changes; it never needs to know about health, damage or the other effects. Add a field here when a new kind of
    /// change is needed (movement speed, for example) and use it from the system that owns that stat.
    /// </summary>
    [Serializable]
    public struct EffectModifiers
    {
        /// <summary>Multiplier on weapon damage dealt (pickaxe excluded). 1 = unchanged; effects multiply together.</summary>
        public float WeaponDamageMultiplier;
        /// <summary>Fraction of incoming damage removed (0.1 = 10%). Effects add together.</summary>
        public float DamageReduction;
        /// <summary>Extra life regenerated per second. Effects add together.</summary>
        public float RegenPerSecond;
        /// <summary>Life lost per second (burning). Effects add together.</summary>
        public float DrainPerSecond;
        /// <summary>Life per second of drain this cancels out, down to no drain at all. Effects add together.</summary>
        public float DrainReductionPerSecond;
        /// <summary>No life regeneration at all while this is active.</summary>
        public bool BlocksRegen;
        /// <summary>Extra mana regenerated per second. Effects add together.</summary>
        public float ManaRegenPerSecond;
        /// <summary>Mana lost per second (never below zero). Effects add together. Unlike life drain it does not stop regeneration by itself: regeneration and drain net out.</summary>
        public float ManaDrainPerSecond;
        /// <summary>No mana regeneration at all while this is active.</summary>
        public bool BlocksManaRegen;

        public static EffectModifiers None => new() { WeaponDamageMultiplier = 1f };

        /// <summary>Life lost per second after drain reductions.</summary>
        public float NetDrainPerSecond => Math.Max(0f, DrainPerSecond - DrainReductionPerSecond);

        public void Add(in EffectModifiers other)
        {
            WeaponDamageMultiplier *= other.WeaponDamageMultiplier;
            DamageReduction += other.DamageReduction;
            RegenPerSecond += other.RegenPerSecond;
            DrainPerSecond += other.DrainPerSecond;
            DrainReductionPerSecond += other.DrainReductionPerSecond;
            BlocksRegen |= other.BlocksRegen;
            ManaRegenPerSecond += other.ManaRegenPerSecond;
            ManaDrainPerSecond += other.ManaDrainPerSecond;
            BlocksManaRegen |= other.BlocksManaRegen;
        }
    }

    /// <summary>
    /// A kind of status effect: its name, whether it helps or hurts, how long it lasts by default, and what it does
    /// while active. Pure shared data: the time left is tracked per player in <see cref="StatusEffects"/>.
    /// </summary>
    public sealed class StatusEffectDef
    {
        public readonly string Name;
        public readonly EffectKind Kind;
        public readonly int DefaultTicks;
        /// <summary>Removed when the player dies. A false value keeps the effect through death (it still only counts down while alive).</summary>
        public readonly bool ClearsOnDeath;
        public readonly EffectModifiers Modifiers;

        public StatusEffectDef(string name, EffectKind kind, float defaultSeconds, EffectModifiers modifiers, bool clearsOnDeath = true)
        {
            Name = name;
            Kind = kind;
            DefaultTicks = SimUnits.SecondsToTicks(defaultSeconds);
            Modifiers = modifiers;
            ClearsOnDeath = clearsOnDeath;
        }
    }

    /// <summary>Every status effect in the game. To add one, describe it here.</summary>
    public static class StatusEffectDefs
    {
        /// <summary>Lava's burning: drains 4 life per second and stops regeneration.</summary>
        public static readonly StatusEffectDef Burning = new("On Fire!", EffectKind.Debuff, 7f,
            new EffectModifiers { WeaponDamageMultiplier = 1f, DrainPerSecond = 4f, BlocksRegen = true });

        /// <summary>From nectar: +1 life per second, and it cancels 3 of the burn's 4 per second (so burning while
        /// in this drains 1 per second; regeneration itself is still blocked by the burn).</summary>
        public static readonly StatusEffectDef Regeneration = new("Regeneration", EffectKind.Buff, 30f,
            new EffectModifiers { WeaponDamageMultiplier = 1f, RegenPerSecond = 1f, DrainReductionPerSecond = 3f });

        /// <summary>The mid-boss buff: +15% weapon damage and 15% less damage taken for 2 minutes; lost on death.</summary>
        public static readonly StatusEffectDef Apex = new("Apex buff", EffectKind.Buff, 120f,
            new EffectModifiers { WeaponDamageMultiplier = 1.15f, DamageReduction = 0.15f });

        /// <summary>The Endurance spell: 10% less damage taken. Survives death.</summary>
        public static readonly StatusEffectDef Endurance = new("Endurance", EffectKind.Buff, 5f,
            new EffectModifiers { WeaponDamageMultiplier = 1f, DamageReduction = 0.1f }, clearsOnDeath: false);
    }

    /// <summary>One effect on a player and how long it has left.</summary>
    public sealed class ActiveEffect
    {
        public StatusEffectDef Def;
        public int TicksLeft;
    }

    /// <summary>
    /// The status effects on one player. Applying an effect that is already active refreshes it to the longer of the
    /// two times (never shortens). Each tick the owner reads <see cref="Aggregate"/> to learn the combined result, uses
    /// it, then calls <see cref="Tick"/> to count down; an effect is therefore active for exactly the ticks it was given.
    /// </summary>
    public sealed class StatusEffects
    {
        readonly List<ActiveEffect> m_Active = new();

        public IReadOnlyList<ActiveEffect> Active => m_Active;

        public bool Has(StatusEffectDef def) => Find(def) != null;

        public int TicksLeft(StatusEffectDef def) => Find(def)?.TicksLeft ?? 0;

        /// <summary>Applies the effect for its default time.</summary>
        public void Apply(StatusEffectDef def) => Apply(def, def.DefaultTicks);

        /// <summary>Applies the effect for the given time, or extends it to that if it would last longer.</summary>
        public void Apply(StatusEffectDef def, int ticks)
        {
            if (ticks <= 0) return;

            ActiveEffect existing = Find(def);
            if (existing != null) existing.TicksLeft = Math.Max(existing.TicksLeft, ticks);
            else m_Active.Add(new ActiveEffect { Def = def, TicksLeft = ticks });
        }

        public void Remove(StatusEffectDef def) => m_Active.RemoveAll(e => e.Def == def);

        /// <summary>Removes every effect (class selection).</summary>
        public void ClearAll() => m_Active.Clear();

        /// <summary>Removes the effects that do not survive death.</summary>
        public void ClearOnDeath() => m_Active.RemoveAll(e => e.Def.ClearsOnDeath);

        /// <summary>Removes every debuff (a cleanse).</summary>
        public void ClearDebuffs() => m_Active.RemoveAll(e => e.Def.Kind == EffectKind.Debuff);

        /// <summary>The combined modifiers of everything active this tick.</summary>
        public EffectModifiers Aggregate()
        {
            EffectModifiers total = EffectModifiers.None;
            for (int i = 0; i < m_Active.Count; i++) total.Add(m_Active[i].Def.Modifiers);
            return total;
        }

        /// <summary>Counts every effect down one tick and drops the ones that ran out.</summary>
        public void Tick()
        {
            for (int i = m_Active.Count - 1; i >= 0; i--)
            {
                if (--m_Active[i].TicksLeft <= 0) m_Active.RemoveAt(i);
            }
        }

        ActiveEffect Find(StatusEffectDef def)
        {
            for (int i = 0; i < m_Active.Count; i++)
            {
                if (m_Active[i].Def == def) return m_Active[i];
            }

            return null;
        }
    }
}
