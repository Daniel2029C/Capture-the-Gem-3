using System;
using CTG.Core;
using UnityEngine;

namespace CTG.Economy
{
    /// <summary>
    /// A player's shards (currency), experience and level. Experience comes from kills, assists, altars, bonuses and a
    /// passive trickle; every 1000 experience is a level, capped at level 10. Level scales life, damage and mining
    /// speed linearly from 1x at level 1 to 1.5x at level 10.
    /// </summary>
    public sealed class PlayerProgress
    {
        public const int MaxLevel = 10;
        public const int ExpPerLevel = 1000;
        /// <summary>Passive experience per second.</summary>
        public const float PassiveExpPerSecond = 4f;
        /// <summary>Extra stat multiplier at the maximum level (0.5 = 1.5x).</summary>
        public const float MaxLevelStatBonus = 0.5f;

        public int Shards { get; private set; }
        public int Exp { get; private set; }

        /// <summary>Raised with the new level each time a level is gained.</summary>
        public event Action<int> LeveledUp;

        float m_PassiveProgress;

        public static int MaxExp => (MaxLevel - 1) * ExpPerLevel;

        public int Level => Mathf.Min(MaxLevel, 1 + Exp / ExpPerLevel);
        public bool AtMaxLevel => Level >= MaxLevel;

        /// <summary>Experience gained inside the current level (0..ExpPerLevel); full at the cap.</summary>
        public int ExpIntoLevel => AtMaxLevel ? ExpPerLevel : Exp % ExpPerLevel;

        /// <summary>Multiplier applied to life, damage and mining speed: 1 at level 1, 1.5 at level 10, linear between.</summary>
        public float LevelScale => 1f + MaxLevelStatBonus * (Level - 1) / (MaxLevel - 1);

        public void Reset()
        {
            Shards = 0;
            Exp = 0;
            m_PassiveProgress = 0f;
        }

        public void AddShards(int amount)
        {
            if (amount > 0) Shards += amount;
        }

        public void AddExp(int amount)
        {
            if (amount <= 0 || AtMaxLevel) return;

            int before = Level;
            Exp = Mathf.Min(MaxExp, Exp + amount);
            for (int level = before + 1; level <= Level; level++) LeveledUp?.Invoke(level);
        }

        /// <summary>Spends shards if there are enough.</summary>
        public bool TrySpend(int amount)
        {
            if (amount < 0 || Shards < amount) return false;

            Shards -= amount;
            return true;
        }

        /// <summary>Passive experience; once per tick while alive.</summary>
        public void Tick()
        {
            m_PassiveProgress += PassiveExpPerSecond / SimUnits.TicksPerSecond;
            if (m_PassiveProgress < 1f) return;

            int whole = (int)m_PassiveProgress;
            m_PassiveProgress -= whole;
            AddExp(whole);
        }
    }
}
