using System;
using CTG.Combat;
using CTG.UI;
using UnityEngine;

namespace CTG.Gameplay
{
    /// <summary>
    /// The shield around a gem. The gem's owner can't hurt it; enemy players must break it (1000 life, no
    /// regeneration, no repair) before they can pick the gem up. It also drops by itself at the Apex Shift. Only
    /// players can damage it; mobs and other sources are ignored.
    /// </summary>
    public sealed class GemBarrier : IDamageable
    {
        public const int MaxHealth = 1000;

        readonly Gem m_Gem;

        public GemBarrier(Gem gem)
        {
            m_Gem = gem;
            Health = MaxHealth;
            DamageableRegistry.Register(this);
        }

        /// <summary>The gem's team: that team can't damage its own barrier.</summary>
        public Team Team => m_Gem.Team;
        public Rect HitboxPx => m_Gem.HitboxPx;
        public int Health { get; private set; }
        public bool IsUp => Health > 0;
        public bool CanBeHit => IsUp;

        /// <summary>Raised when the barrier goes down: the player who broke it, or null when it fell by timer.</summary>
        public event Action<GemBarrier, PlayerCharacter> Broken;

        public void Unregister() => DamageableRegistry.Unregister(this);

        public void Reset()
        {
            Health = MaxHealth;
            DamageableRegistry.Register(this);
        }

        public int ApplyHit(in HitInfo hit)
        {
            if (!IsUp) return 0;
            if (hit.Source is not PlayerCharacter breaker || breaker.Team == Team) return 0;

            int dealt = Mathf.Min(Health, Mathf.Max(1, hit.Damage));
            Health -= dealt;
            CombatText.Spawn(HitboxPx.center + new Vector2(0f, HitboxPx.height * 0.5f), dealt.ToString(), CombatText.DummyDamaged);
            if (Health <= 0) Broken?.Invoke(this, breaker);
            return dealt;
        }

        /// <summary>The barrier falls without anyone breaking it (the Apex Shift).</summary>
        public void BreakByTimer()
        {
            if (!IsUp) return;

            Health = 0;
            Broken?.Invoke(this, null);
        }
    }
}
