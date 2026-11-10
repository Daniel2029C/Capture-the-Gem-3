using System;
using CTG.Combat;
using CTG.Core;
using CTG.Gameplay;
using CTG.Rendering;
using CTG.UI;
using CTG.World;
using UnityEngine;

namespace CTG.Mobs
{
    /// <summary>
    /// The mid-boss, the stalemate breaker. It hovers over the mid platform, 1500 life, immune to knockback. If it
    /// isn't hit for 4 seconds it regenerates rapidly, so one team has to out-damage the other. The player who lands the
    /// last hit is reported through <see cref="Died"/>.
    ///
    /// It has two basic attacks. A fireball volley: three fireballs in a narrow spread at the nearest player in range,
    /// every 2.5 seconds. A shockwave: when a player gets close, a ring winds up for one second and then damages and
    /// knocks back everyone inside it. It doesn't fire volleys while winding up a shockwave.
    /// </summary>
    public sealed class ElderDrakon : MonoBehaviour, IDamageable, ITickable
    {
        public const int MaxHealth = 1500;
        /// <summary>Ticks without being hit before it starts regenerating.</summary>
        public const int RegenDelayTicks = 4 * SimUnits.TicksPerSecond;
        /// <summary>Life regained per second while regenerating (a full heal in 10 s).</summary>
        public const float RegenPerSecond = 150f;

        const float k_Scale = 2.6f;
        const float k_HoverPx = 14f;

        // Fireball volley.
        const int k_VolleyIntervalTicks = 150;
        const int k_VolleyDamage = 20;
        const float k_VolleySpeed = 4.5f;
        const float k_VolleyKnockback = 0.3f;
        const float k_VolleySpreadDegrees = 12f;
        const float k_SightPx = 24f * SimUnits.PixelsPerTile;

        // Shockwave.
        const float k_ShockRadiusPx = 6f * SimUnits.PixelsPerTile;
        const int k_ShockWindupTicks = 60;
        const int k_ShockCooldownTicks = 6 * SimUnits.TicksPerSecond;
        const int k_ShockDamage = 40;
        const float k_ShockKnockback = 2f;

        Vector2 m_Anchor;     // bottom centre it hovers over, px
        float m_Health;
        int m_TicksSinceHit;
        int m_Age;
        int m_HitFlashTicks;
        SpriteRenderer m_Body;
        TileWorld m_World;
        int m_NextVolleyTick = 90;
        int m_ShockWindup;           // ticks left of a running windup, 0 when idle
        int m_NextShockTick;
        SpriteRenderer m_ShockRing;

        public Team Team => Team.Monster;
        public Rect HitboxPx => new(m_Anchor.x - 50f, m_Anchor.y + k_HoverPx, 100f, 62f);
        public bool CanBeHit => Health > 0;
        public int Health => Mathf.CeilToInt(m_Health);
        public bool IsRegenerating => m_TicksSinceHit >= RegenDelayTicks && m_Health < MaxHealth;

        /// <summary>Raised once when it dies: the player who landed the last hit, or null.</summary>
        public event Action<ElderDrakon, PlayerCharacter> Died;

        public static ElderDrakon Spawn(Transform parent, Vector2 bottomCenterPx, TileWorld world)
        {
            var go = new GameObject("Elder Drakon");
            go.transform.SetParent(parent, false);
            var boss = go.AddComponent<ElderDrakon>();
            boss.m_Anchor = bottomCenterPx;
            boss.m_World = world;
            boss.m_Health = MaxHealth;
            boss.m_TicksSinceHit = RegenDelayTicks;
            boss.m_Body = go.AddComponent<SpriteRenderer>();
            boss.m_Body.sprite = MobArt.Drakon(0);
            boss.m_Body.sortingOrder = 9;
            go.transform.localScale = Vector3.one * k_Scale;

            // The shockwave's warning ring, a child that is drawn at world scale regardless of the boss's own scale.
            var ring = new GameObject("Shockwave Ring").AddComponent<SpriteRenderer>();
            ring.sprite = MobArt.Disc();
            ring.sortingOrder = 8;
            ring.enabled = false;
            boss.m_ShockRing = ring;
            DamageableRegistry.Register(boss);
            SimulationClock.Instance.Register(boss);
            return boss;
        }

        void OnDestroy()
        {
            if (m_ShockRing != null) Destroy(m_ShockRing.gameObject);
            DamageableRegistry.Unregister(this);
            if (SimulationClock.Instance != null) SimulationClock.Instance.Unregister(this);
        }

        public int ApplyHit(in HitInfo hit)
        {
            if (m_Health <= 0f) return 0;

            int damage = CombatRules.ComputeDamage(hit.Damage, 0);
            if (damage <= 0) return 0;

            damage = Mathf.Min(damage, Health);
            m_Health -= damage;
            m_TicksSinceHit = 0;
            m_HitFlashTicks = 6;
            CombatText.Spawn(HitboxPx.center + new Vector2(0f, 40f), damage.ToString(), CombatText.DummyDamaged);

            if (m_Health <= 0f)
            {
                m_Health = 0f;
                Died?.Invoke(this, hit.Source as PlayerCharacter);
                Destroy(gameObject);
            }

            return damage;
        }

        /// <summary>Removes it without a kill.</summary>
        public void Despawn()
        {
            m_Health = 0f;
            Destroy(gameObject);
        }

        public void Tick(int tick)
        {
            if (m_Health <= 0f) return;

            m_Age++;
            if (m_HitFlashTicks > 0) m_HitFlashTicks--;
            if (m_TicksSinceHit < RegenDelayTicks) m_TicksSinceHit++;
            else if (m_Health < MaxHealth) m_Health = Mathf.Min(MaxHealth, m_Health + RegenPerSecond / SimUnits.TicksPerSecond);

            if (m_ShockWindup > 0)
            {
                if (--m_ShockWindup == 0) ReleaseShockwave();
                return;
            }

            if (!TryStartShockwave()) TryVolley();
        }

        Vector2 CenterPx => HitboxPx.center;

        PlayerCharacter NearestPlayer(out float distance)
        {
            PlayerCharacter best = null;
            distance = float.MaxValue;
            var all = DamageableRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is not PlayerCharacter player || player.Health.IsDead) continue;

                float d = Vector2.Distance(CenterPx, player.Motor.Center);
                if (d < distance)
                {
                    distance = d;
                    best = player;
                }
            }

            return best;
        }

        /// <summary>A player came close and the shockwave is ready: start the one-second windup.</summary>
        bool TryStartShockwave()
        {
            if (m_Age < m_NextShockTick) return false;

            PlayerCharacter near = NearestPlayer(out float distance);
            if (near == null || distance > k_ShockRadiusPx) return false;

            m_ShockWindup = k_ShockWindupTicks;
            return true;
        }

        void ReleaseShockwave()
        {
            m_NextShockTick = m_Age + k_ShockCooldownTicks;
            var all = DamageableRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is not PlayerCharacter player || !player.CanBeHit) continue;
                if (Vector2.Distance(CenterPx, player.Motor.Center) > k_ShockRadiusPx) continue;

                int direction = player.Motor.Center.x >= CenterPx.x ? 1 : -1;
                player.ApplyHit(new HitInfo { Damage = k_ShockDamage, Knockback = k_ShockKnockback, Direction = direction, Source = this });
            }
        }

        void TryVolley()
        {
            if (m_Age < m_NextVolleyTick || ProjectileSystem.Instance == null) return;

            PlayerCharacter target = NearestPlayer(out float distance);
            if (target == null || distance > k_SightPx || !HasClearLine(CenterPx, target.Motor.Center)) return;

            m_NextVolleyTick = m_Age + k_VolleyIntervalTicks;
            Vector2 aim = (target.Motor.Center - CenterPx).normalized;
            for (int i = -1; i <= 1; i++)
            {
                Vector2 direction = Quaternion.Euler(0f, 0f, i * k_VolleySpreadDegrees) * aim;
                ProjectileSystem.Instance.Spawn(ProjectileId.Fireball, this, CenterPx, direction * k_VolleySpeed, k_VolleyDamage, k_VolleyKnockback, 0);
            }
        }

        /// <summary>No solid tile between the two points (sampled every 8 px).</summary>
        bool HasClearLine(Vector2 from, Vector2 to)
        {
            if (m_World == null) return true;

            int steps = Mathf.CeilToInt(Vector2.Distance(from, to) / 8f);
            for (int i = 1; i < steps; i++)
            {
                Vector2 p = Vector2.Lerp(from, to, i / (float)steps);
                if (m_World.IsSolid(Mathf.FloorToInt(p.x / SimUnits.PixelsPerTile), Mathf.FloorToInt(p.y / SimUnits.PixelsPerTile))) return false;
            }

            return true;
        }

        void LateUpdate()
        {
            float bob = Mathf.Sin(Time.time * 1.6f) * 4f;
            Vector2 centre = HitboxPx.center + new Vector2(0f, bob);
            transform.position = centre / SimUnits.PixelsPerTile;
            m_Body.sprite = MobArt.Drakon((Time.frameCount / 14) % 2);
            Color tint = new Color(1f, 0.62f, 0.45f);
            if (m_HitFlashTicks > 0) tint = new Color(1f, 0.85f, 0.85f);
            else if (IsRegenerating) tint = Color.Lerp(tint, new Color(0.5f, 1f, 0.55f), 0.5f + 0.3f * Mathf.Sin(Time.time * 8f));
            m_Body.color = tint;

            // The warning ring grows to full size and brightens through the windup.
            bool winding = m_ShockWindup > 0;
            m_ShockRing.enabled = winding;
            if (winding)
            {
                float progress = 1f - m_ShockWindup / (float)k_ShockWindupTicks;
                m_ShockRing.transform.position = centre / SimUnits.PixelsPerTile;
                m_ShockRing.transform.localScale = Vector3.one * (k_ShockRadiusPx * 2f / SimUnits.PixelsPerTile) * (0.35f + 0.65f * progress);
                m_ShockRing.color = new Color(1f, 0.45f, 0.2f, 0.15f + 0.35f * progress);
            }
        }
    }
}
