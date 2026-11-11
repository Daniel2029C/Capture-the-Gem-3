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
    /// A Drakon: 300 life, flies slowly around its home area and shoots a fireball (15 damage) at the nearest player
    /// in range. It is hostile to every player and can be hurt by any of them. The player who lands the last hit
    /// is reported through <see cref="Died"/>.
    /// </summary>
    public sealed class Drakon : MonoBehaviour, IDamageable, ITickable
    {
        /// <summary>Life of a Drakon on its own side; the mid ones have more.</summary>
        public const int MaxHealth = 300;
        public const int MidMaxHealth = 400;
        public const int FireballDamage = 15;

        const float k_SpeedPx = 0.7f;
        const float k_FireballSpeed = 4.5f;
        const float k_FireballKnockback = 0.4f;
        const float k_SightPx = 20f * SimUnits.PixelsPerTile;
        const int k_FireIntervalTicks = 110;
        const int k_FirstShotDelayTicks = 60;
        /// <summary>Knockback shoves it this fraction of what a player would be pushed, fading by this factor each tick.</summary>
        const float k_KnockbackScale = 0.6f;
        const float k_KnockbackDecay = 0.88f;
        /// <summary>Ticks added before its next shot when a hit shoves it.</summary>
        const int k_StaggerTicks = 20;

        TileWorld m_World;
        Rect m_HomePx;
        Vector2 m_Center;
        Vector2 m_Waypoint;
        int m_NextWaypointTick;
        int m_NextShotTick;
        int m_Age;
        int m_HitFlashTicks;
        Vector2 m_Knock;
        SpriteRenderer m_Body;
        Transform m_BarFill;
        Transform m_Bar;

        public Team Team => Team.Monster;
        public Rect HitboxPx => new(m_Center.x - MobArt.DrakonWidthPx * 0.5f + 4f, m_Center.y - MobArt.DrakonHeightPx * 0.5f + 4f,
                                    MobArt.DrakonWidthPx - 8f, MobArt.DrakonHeightPx - 8f);
        public bool CanBeHit => Health > 0;
        MobLife m_Life;
        public int Health => m_Life.Current;
        public int MaxLife => m_Life.Max;
        public Vector2 CenterPx => m_Center;

        /// <summary>Raised once when it dies: the player who landed the last hit, or null.</summary>
        public event Action<Drakon, PlayerCharacter> Died;

        public static Drakon Spawn(Transform parent, TileWorld world, Vector2 centerPx, Rect homePx, int maxLife = MaxHealth)
        {
            var go = new GameObject("Drakon");
            go.transform.SetParent(parent, false);
            var drakon = go.AddComponent<Drakon>();
            drakon.m_World = world;
            drakon.m_HomePx = homePx;
            drakon.m_Center = centerPx;
            drakon.m_Life = new MobLife(maxLife);
            drakon.m_Waypoint = centerPx;
            drakon.m_NextShotTick = k_FirstShotDelayTicks;
            drakon.BuildVisuals();
            DamageableRegistry.Register(drakon);
            SimulationClock.Instance.Register(drakon);
            return drakon;
        }

        void BuildVisuals()
        {
            m_Body = gameObject.AddComponent<SpriteRenderer>();
            m_Body.sprite = MobArt.Drakon(0);
            m_Body.sortingOrder = 9;

            m_Bar = new GameObject("Health Bar").transform;
            m_Bar.SetParent(transform, false);
            m_Bar.localPosition = new Vector3(0f, 1.3f, 0f);
            var back = new GameObject("Back").AddComponent<SpriteRenderer>();
            back.transform.SetParent(m_Bar, false);
            back.sprite = PlaceholderSprites.Solid("mob_bar_back", new Color32(0, 0, 0, 190), 32, 4);
            back.sortingOrder = 15;
            var fill = new GameObject("Fill").AddComponent<SpriteRenderer>();
            fill.transform.SetParent(m_Bar, false);
            fill.sprite = PlaceholderSprites.Solid("mob_bar_fill", new Color32(220, 60, 60, 255), 30, 2);
            fill.sortingOrder = 16;
            m_BarFill = fill.transform;
        }

        void OnDestroy()
        {
            DamageableRegistry.Unregister(this);
            if (SimulationClock.Instance != null) SimulationClock.Instance.Unregister(this);
        }

        public int ApplyHit(in HitInfo hit)
        {
            int damage = m_Life.TakeHit(hit);
            if (damage <= 0) return 0;

            m_HitFlashTicks = 6;
            if (!hit.Knockback.IsZero)
            {
                // Shoved the way the hit was travelling, then it recovers; the push also rattles its aim for a moment.
                HitInfo push = hit;
                if (push.Direction == 0) push.Direction = 1;
                m_Knock = CombatRules.KnockbackVelocity(push) * k_KnockbackScale;
                m_NextShotTick = Mathf.Max(m_NextShotTick, m_Age + k_StaggerTicks);
            }

            CombatText.Spawn(m_Center + new Vector2(0f, 20f), damage.ToString(), CombatText.DummyDamaged);

            if (Health <= 0)
            {
                Died?.Invoke(this, hit.Source as PlayerCharacter);
                Destroy(gameObject);
            }

            return damage;
        }

        /// <summary>Removes it without a kill (the Apex Shift).</summary>
        public void Despawn()
        {
            m_Life.Kill();
            Destroy(gameObject);
        }

        public void Tick(int tick)
        {
            if (Health <= 0) return;

            m_Age++;
            if (m_HitFlashTicks > 0) m_HitFlashTicks--;

            Wander();
            TryShoot();
        }

        void Wander()
        {
            if (m_Age >= m_NextWaypointTick || (m_Waypoint - m_Center).sqrMagnitude < 36f) PickWaypoint();

            Vector2 toward = m_Waypoint - m_Center;
            Vector2 step = toward.normalized * k_SpeedPx;
            // A slow bob so it doesn't glide perfectly flat.
            step.y += Mathf.Sin(m_Age * 0.08f) * 0.15f;

            Vector2 next = m_Center + step + m_Knock;
            if (CanOccupy(next))
            {
                m_Center = next;
            }
            else
            {
                // Shoved into a wall: the push ends there, and it carries on flying normally.
                m_Knock = Vector2.zero;
                next = m_Center + step;
                if (CanOccupy(next)) m_Center = next;
                else PickWaypoint();
            }

            m_Knock *= k_KnockbackDecay;
            if (m_Knock.sqrMagnitude < 0.01f) m_Knock = Vector2.zero;
        }

        void PickWaypoint()
        {
            m_NextWaypointTick = m_Age + UnityEngine.Random.Range(150, 360);
            for (int attempt = 0; attempt < 12; attempt++)
            {
                var candidate = new Vector2(UnityEngine.Random.Range(m_HomePx.xMin, m_HomePx.xMax),
                                            UnityEngine.Random.Range(m_HomePx.yMin, m_HomePx.yMax));
                if (CanOccupy(candidate))
                {
                    m_Waypoint = candidate;
                    return;
                }
            }

            m_Waypoint = m_Center;
        }

        bool CanOccupy(Vector2 center)
        {
            float w = MobArt.DrakonWidthPx - 8f, h = MobArt.DrakonHeightPx - 8f;
            return !m_World.AnySolidInPixelRect(center.x - w * 0.5f, center.y - h * 0.5f, w, h);
        }

        void TryShoot()
        {
            if (m_Age < m_NextShotTick || ProjectileSystem.Instance == null) return;

            PlayerCharacter target = NearestTarget(out float distance);
            if (target == null || distance > k_SightPx) return;

            Vector2 aim = target.Motor.Center - m_Center;
            if (!HasClearLine(m_Center, target.Motor.Center)) return;

            m_NextShotTick = m_Age + k_FireIntervalTicks;
            ProjectileSystem.Instance.Spawn(ProjectileId.Fireball, this, m_Center, aim.normalized * k_FireballSpeed, FireballDamage, k_FireballKnockback, 0);
        }

        PlayerCharacter NearestTarget(out float distance)
        {
            PlayerCharacter best = null;
            distance = float.MaxValue;
            var all = DamageableRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is not PlayerCharacter player || !player.CanBeHit) continue;

                float d = Vector2.Distance(m_Center, player.Motor.Center);
                if (d < distance)
                {
                    distance = d;
                    best = player;
                }
            }

            return best;
        }

        /// <summary>No solid tile between the two points (sampled every 8 px).</summary>
        bool HasClearLine(Vector2 from, Vector2 to)
        {
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
            transform.position = m_Center / SimUnits.PixelsPerTile;
            m_Body.sprite = MobArt.Drakon((Time.frameCount / 10) % 2);

            // Face the way it is heading.
            float heading = m_Waypoint.x - m_Center.x;
            if (Mathf.Abs(heading) > 4f) m_Body.flipX = heading < 0f;

            m_Body.color = m_HitFlashTicks > 0 ? new Color(1f, 0.6f, 0.6f) : Color.white;
            float fraction = Mathf.Clamp01(Health / (float)MaxLife);
            m_BarFill.localScale = new Vector3(fraction, 1f, 1f);
            m_BarFill.localPosition = new Vector3(-(1f - fraction) * 30f * 0.5f / SimUnits.PixelsPerTile, 0f, 0f);
        }
    }
}
