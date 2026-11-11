using System.Collections.Generic;
using CTG.Core;
using CTG.Rendering;
using CTG.World;
using UnityEngine;

namespace CTG.Combat
{
    /// <summary>A live projectile. Position is the hitbox bottom-left in px (y-up).</summary>
    public sealed class Projectile
    {
        public ProjectileDef Def;
        public IDamageable Owner;
        public Vector2 Position;
        public Vector2 PreviousPosition;
        /// <summary>px per update.</summary>
        public Vector2 Velocity;
        public int Damage;
        public KnockbackScale Knockback;
        /// <summary>Where it was fired from (centre, px) and how fast: a follow-up shot starts there.</summary>
        public Vector2 OriginCenter;
        public Vector2 LaunchVelocity;
        /// <summary>Updates run per tick beyond the first.</summary>
        public int ExtraUpdates;
        public int TimeLeft;
        public int Penetrate;
        /// <summary>Counts updates up to the gravity delay.</summary>
        public int FlightUpdates;
        /// <summary>Travel direction, +1 right / -1 left; used to push targets.</summary>
        public int Direction = 1;
        public bool Active = true;
        public readonly HashSet<IDamageable> HitTargets = new();
        /// <summary>Boomerangs: heading back to the owner.</summary>
        public bool Returning;
        /// <summary>Boomerangs: updates flown outbound.</summary>
        public int OutboundUpdates;
        /// <summary>Updates since spawn.</summary>
        public int Age;
        /// <summary>Boomerangs: the update count at which each target can be hit again.</summary>
        public readonly Dictionary<IDamageable, int> RehitAt = new();

        public Vector2 Size => new(Def.WidthPx, Def.HeightPx);
        public Vector2 Center => Position + Size * 0.5f;
        public Rect HitboxPx => new(Position, Size);
    }

    /// <summary>
    /// Steps every projectile once per tick (plus its extra updates) after the players have acted, and
    /// draws them. Each update: flight (gravity) → move with tile collision (touching a tile ends it) →
    /// hit hostile targets → lifetime. A projectile that ends with an explosion hits everything hostile
    /// in the blast that it hadn't already hit.
    /// </summary>
    public class ProjectileSystem : MonoBehaviour, ITickable
    {
        const int k_ExplosionVisualTicks = 10;

        static ProjectileSystem s_Instance;
        public static ProjectileSystem Instance => s_Instance;

        /// <summary>A projectile ran into a solid tile: the contact point (px) and the tile struck.</summary>
        public event System.Action<Vector2, TileId> TileStruck;

        TileWorld m_World;
        readonly List<Projectile> m_Projectiles = new();
        readonly List<SpriteRenderer> m_ProjectileViews = new();

        /// <summary>Follow-ups waiting for their delay to run out (kept here so they still fire after the parent has ended).</summary>
        struct ScheduledFollowUp
        {
            public Projectile Parent;
            public int TicksLeft;
        }

        readonly List<ScheduledFollowUp> m_Scheduled = new();

        struct Blast
        {
            public Rect RectPx;
            public int TicksLeft;
        }

        readonly List<Blast> m_Blasts = new();
        readonly List<SpriteRenderer> m_BlastViews = new();

        public IReadOnlyList<Projectile> Projectiles => m_Projectiles;

        public void Initialize(TileWorld world)
        {
            m_World = world;
            s_Instance = this;
            SimulationClock.Instance.Register(this);
        }

        void OnDestroy()
        {
            if (s_Instance == this) s_Instance = null;
            if (SimulationClock.Instance != null) SimulationClock.Instance.Unregister(this);
        }

        public Projectile Spawn(ProjectileId id, IDamageable owner, Vector2 centerPx, Vector2 velocity,
                                int damage, KnockbackScale knockback, int extraUpdates)
        {
            ProjectileDef def = ProjectileDefs.Get(id);
            var projectile = new Projectile
            {
                Def = def, Owner = owner, Velocity = velocity, Damage = damage, Knockback = knockback,
                ExtraUpdates = extraUpdates, TimeLeft = def.LifetimeUpdates, Penetrate = def.Penetrate,
                Direction = velocity.x < 0f ? -1 : 1,
            };
            projectile.OriginCenter = centerPx;
            projectile.LaunchVelocity = velocity;
            projectile.Position = centerPx - projectile.Size * 0.5f;
            projectile.PreviousPosition = projectile.Position;
            m_Projectiles.Add(projectile);
            if (def.FollowUp != null && def.FollowUp.Trigger == FollowUpTrigger.AfterDelay)
            {
                m_Scheduled.Add(new ScheduledFollowUp { Parent = projectile, TicksLeft = def.FollowUp.DelayTicks });
            }

            return projectile;
        }

        public void Tick(int tick)
        {
            for (int i = 0; i < m_Projectiles.Count; i++)
            {
                Projectile p = m_Projectiles[i];
                p.PreviousPosition = p.Position;
                for (int update = 0; update <= p.ExtraUpdates && p.Active; update++)
                {
                    Step(p);
                }
            }

            m_Projectiles.RemoveAll(p => !p.Active);

            // Delayed follow-ups launch once their time is up, counted in ticks from when the parent was fired.
            for (int i = m_Scheduled.Count - 1; i >= 0; i--)
            {
                ScheduledFollowUp scheduled = m_Scheduled[i];
                if (--scheduled.TicksLeft > 0)
                {
                    m_Scheduled[i] = scheduled;
                    continue;
                }

                m_Scheduled.RemoveAt(i);
                LaunchFollowUp(scheduled.Parent);
            }

            for (int i = m_Blasts.Count - 1; i >= 0; i--)
            {
                Blast blast = m_Blasts[i];
                blast.TicksLeft--;
                if (blast.TicksLeft <= 0) m_Blasts.RemoveAt(i);
                else m_Blasts[i] = blast;
            }
        }

        void Step(Projectile p)
        {
            ProjectileDef def = p.Def;
            if (def.Boomerang != null)
            {
                StepBoomerang(p);
                return;
            }

            // Flight: straight for a few updates, then constant vertical acceleration up to a cap.
            p.FlightUpdates++;
            if (p.FlightUpdates >= def.GravityDelayUpdates)
            {
                p.FlightUpdates = def.GravityDelayUpdates;
                p.Velocity.y += def.GravityPerUpdate;
            }

            if (def.GravityPerUpdate > 0f && p.Velocity.y > def.MaxRiseSpeed) p.Velocity.y = def.MaxRiseSpeed;
            if (p.Velocity.y < -def.MaxFallSpeed) p.Velocity.y = -def.MaxFallSpeed;

            // Movement: touching a tile ends the projectile where it stopped.
            float x = p.Position.x, y = p.Position.y;
            float vx = p.Velocity.x, vy = p.Velocity.y;
            TileCollision.Result hit = TileCollision.Move(m_World, ref x, ref y, def.WidthPx, def.HeightPx, ref vx, ref vy);
            p.Position = new Vector2(x, y);
            if (hit.HitLeft || hit.HitRight || hit.HitFloor || hit.HitCeiling)
            {
                ReportTileStrike(p);
                End(p);
                return;
            }

            p.Direction = p.Velocity.x < 0f ? -1 : 1;

            HitTargets(p);

            p.TimeLeft--;
            if (p.TimeLeft <= 0 || p.Penetrate == 0) End(p);
        }

        /// <summary>Finds the solid tile just ahead of a projectile that stopped against one and reports it.</summary>
        void ReportTileStrike(Projectile p)
        {
            if (TileStruck == null) return;

            Vector2 heading = p.Velocity.sqrMagnitude > 0.0001f ? p.Velocity.normalized : Vector2.down;
            Vector2 center = p.Center;
            for (float reach = 2f; reach <= 12f; reach += 2f)
            {
                Vector2 probe = center + heading * (Mathf.Max(p.Def.WidthPx, p.Def.HeightPx) * 0.5f + reach);
                int tx = Mathf.FloorToInt(probe.x / SimUnits.PixelsPerTile), ty = Mathf.FloorToInt(probe.y / SimUnits.PixelsPerTile);
                if (!m_World.IsSolid(tx, ty)) continue;

                TileStruck(probe, m_World.Get(tx, ty));
                return;
            }
        }

        /// <summary>
        /// One boomerang update: outbound flight counts up to the turn, hitting a tile or a target turns it
        /// around (reversing its velocity); returning it ignores tiles and steers toward the owner.
        /// </summary>
        void StepBoomerang(Projectile p)
        {
            BoomerangStats b = p.Def.Boomerang;
            p.Age++;

            if (!p.Returning)
            {
                if (++p.OutboundUpdates >= b.OutboundUpdates) p.Returning = true;
            }
            else
            {
                if (p.Owner == null) { End(p); return; }

                Vector2 toOwner = p.Owner.HitboxPx.center - p.Center;
                float distance = toOwner.magnitude;
                if (distance > b.MaxOwnerDistancePx) { End(p); return; }

                Vector2 want = distance > 0f ? toOwner * (b.ReturnMaxSpeed / distance) : Vector2.zero;
                p.Velocity.x = Accelerate(p.Velocity.x, want.x, b.ReturnAccel);
                p.Velocity.y = Accelerate(p.Velocity.y, want.y, b.ReturnAccel);
            }

            if (p.Returning)
            {
                p.Position += p.Velocity;
            }
            else
            {
                float x = p.Position.x, y = p.Position.y;
                float vx = p.Velocity.x, vy = p.Velocity.y;
                TileCollision.Result hit = TileCollision.Move(m_World, ref x, ref y, p.Def.WidthPx, p.Def.HeightPx, ref vx, ref vy);
                p.Position = new Vector2(x, y);
                if (hit.HitLeft || hit.HitRight || hit.HitFloor || hit.HitCeiling)
                {
                    ReportTileStrike(p);
                    p.Velocity = -p.Velocity;
                    p.Returning = true;
                }
            }

            p.Direction = p.Velocity.x < 0f ? -1 : 1;
            HitBoomerangTargets(p, b);

            if (p.Returning && p.Owner != null && p.HitboxPx.Overlaps(p.Owner.HitboxPx))
            {
                End(p);
                return;
            }

            p.TimeLeft--;
            if (p.TimeLeft <= 0) End(p);
        }

        /// <summary>Speed toward a target velocity, gaining double while moving the opposite way.</summary>
        static float Accelerate(float v, float want, float accel)
        {
            if (v < want)
            {
                v += accel;
                if (v < 0f && want > 0f) v += accel;
            }
            else if (v > want)
            {
                v -= accel;
                if (v > 0f && want < 0f) v -= accel;
            }

            return v;
        }

        /// <summary>Hits every hostile target it overlaps (each at most once per cooldown); a hit while outbound turns it back.</summary>
        void HitBoomerangTargets(Projectile p, BoomerangStats b)
        {
            Rect box = p.HitboxPx;
            IReadOnlyList<IDamageable> targets = DamageableRegistry.All;
            for (int i = 0; i < targets.Count; i++)
            {
                IDamageable target = targets[i];
                if (!target.CanBeHit) continue;
                if (p.RehitAt.TryGetValue(target, out int until) && p.Age < until) continue;
                if (p.Owner != null && !DamageableRegistry.AreHostile(p.Owner, target)) continue;
                if (!box.Overlaps(target.HitboxPx)) continue;

                if (target.ApplyHit(new HitInfo
                    {
                        Damage = p.Damage, Knockback = p.Knockback, Direction = p.Direction, Source = p.Owner,
                    }) <= 0) continue;

                p.RehitAt[target] = p.Age + b.RehitCooldownUpdates;
                if (!p.Returning)
                {
                    p.Velocity = -p.Velocity;
                    p.Returning = true;
                }
            }
        }

        void HitTargets(Projectile p)
        {
            Rect box = p.HitboxPx;
            IReadOnlyList<IDamageable> targets = DamageableRegistry.All;
            for (int i = 0; i < targets.Count && p.Penetrate != 0; i++)
            {
                IDamageable target = targets[i];
                if (!target.CanBeHit || p.HitTargets.Contains(target)) continue;
                if (p.Owner != null && !DamageableRegistry.AreHostile(p.Owner, target)) continue;
                if (!box.Overlaps(target.HitboxPx)) continue;

                if (target.ApplyHit(new HitInfo
                    {
                        Damage = p.Damage, Knockback = p.Knockback, Direction = p.Direction, Source = p.Owner,
                    }) <= 0) continue;

                p.HitTargets.Add(target);
                if (p.Def.EndsOnHit && p.TimeLeft > 1) p.TimeLeft = 1;
                if (p.Penetrate > 0) p.Penetrate--;
                if (p.Def.DamageDecayPerHit < 1f) p.Damage = (int)(p.Damage * p.Def.DamageDecayPerHit);
            }
        }

        /// <summary>The parent's follow-up leaves from the firing spot along the same launch, with the parent's current damage.</summary>
        void LaunchFollowUp(Projectile parent)
        {
            FollowUpDef followUp = parent.Def.FollowUp;
            if (followUp == null || parent.Owner == null) return;

            Spawn(followUp.Projectile, parent.Owner, parent.OriginCenter, parent.LaunchVelocity, parent.Damage, parent.Knockback, followUp.ExtraUpdates);
        }

        void End(Projectile p)
        {
            if (!p.Active) return;
            p.Active = false;

            if (p.Def.FollowUp != null && p.Def.FollowUp.Trigger == FollowUpTrigger.WhenEnded) LaunchFollowUp(p);

            int size = p.Def.ExplosionSizePx;
            if (size <= 0) return;

            Vector2 center = p.Center;
            var blast = new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size);
            m_Blasts.Add(new Blast { RectPx = blast, TicksLeft = k_ExplosionVisualTicks });

            IReadOnlyList<IDamageable> targets = DamageableRegistry.All;
            for (int i = 0; i < targets.Count; i++)
            {
                IDamageable target = targets[i];
                if (!target.CanBeHit || p.HitTargets.Contains(target)) continue;
                if (p.Owner != null && !DamageableRegistry.AreHostile(p.Owner, target)) continue;
                if (!blast.Overlaps(target.HitboxPx)) continue;

                p.HitTargets.Add(target);
                target.ApplyHit(new HitInfo
                {
                    Damage = p.Damage, Knockback = p.Knockback, Direction = p.Direction, Source = p.Owner,
                });
            }
        }

        void LateUpdate()
        {
            float alpha = SimulationClock.Instance != null ? SimulationClock.Instance.Alpha : 1f;

            EnsureViews(m_ProjectileViews, m_Projectiles.Count, 12);
            for (int i = 0; i < m_ProjectileViews.Count; i++)
            {
                SpriteRenderer view = m_ProjectileViews[i];
                bool used = i < m_Projectiles.Count;
                view.enabled = used;
                if (!used) continue;

                Projectile p = m_Projectiles[i];
                Vector2 centerPx = Vector2.Lerp(p.PreviousPosition, p.Position, alpha) + p.Size * 0.5f;
                view.transform.position = centerPx / SimUnits.PixelsPerTile;
                if (p.Def.Boomerang != null)
                {
                    // Spins as it flies.
                    view.sprite = PlaceholderSprites.Solid("boomerang", p.Def.Color, 14, 14);
                    view.transform.rotation = Quaternion.Euler(0f, 0f, (p.Age + alpha) * 28f);
                    continue;
                }

                if (p.Def.Round)
                {
                    view.sprite = PlaceholderSprites.Solid($"blob_{p.Def.Id}", p.Def.Color, p.Def.WidthPx, p.Def.HeightPx);
                    view.transform.rotation = Quaternion.identity;
                    continue;
                }

                view.sprite = PlaceholderSprites.Arrow(p.Def.Color);
                float angle = Mathf.Atan2(p.Velocity.y, p.Velocity.x) * Mathf.Rad2Deg;
                view.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            }

            EnsureViews(m_BlastViews, m_Blasts.Count, 13);
            for (int i = 0; i < m_BlastViews.Count; i++)
            {
                SpriteRenderer view = m_BlastViews[i];
                bool used = i < m_Blasts.Count;
                view.enabled = used;
                if (!used) continue;

                Blast blast = m_Blasts[i];
                view.sprite = PlaceholderSprites.Square("blast", Color.white, SimUnits.PixelsPerTile);
                view.color = new Color(1f, 0.55f, 0.15f, 0.6f * blast.TicksLeft / k_ExplosionVisualTicks);
                view.transform.position = blast.RectPx.position / SimUnits.PixelsPerTile;
                view.transform.localScale = new Vector3(blast.RectPx.width, blast.RectPx.height, 1f) / SimUnits.PixelsPerTile;
            }
        }

        void EnsureViews(List<SpriteRenderer> views, int count, int sortingOrder)
        {
            while (views.Count < count)
            {
                var go = new GameObject("Projectile View");
                go.transform.SetParent(transform, false);
                var view = go.AddComponent<SpriteRenderer>();
                view.sortingOrder = sortingOrder;
                views.Add(view);
            }
        }
    }
}
