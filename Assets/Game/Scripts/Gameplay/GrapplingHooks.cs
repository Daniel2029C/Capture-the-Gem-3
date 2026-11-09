using System.Collections.Generic;
using CTG.Core;
using CTG.World;
using UnityEngine;

namespace CTG.Gameplay
{
    /// <summary>
    /// Tuning for a class's grappling hook (Foliage Tendrils): up to <see cref="MaxHooks"/> hooks at once,
    /// each fired toward the cursor, latching onto the first solid tile it touches within
    /// <see cref="RangePx"/>. All latched hooks pull the player toward their average position.
    /// </summary>
    [System.Serializable]
    public sealed class GrappleStats
    {
        /// <summary>Distance from the player beyond which a flying hook turns back, px.</summary>
        public float RangePx = 352f;
        /// <summary>Hooks in flight or attached at once; no new one can be fired past this.</summary>
        public int MaxHooks = 4;
        /// <summary>Flight speed, px/tick.</summary>
        public float ShootSpeed = 11.5f;
        /// <summary>Speed a released or missed hook returns to the player, px/tick.</summary>
        public float RetreatSpeed = 11f;
        /// <summary>Top speed the player is pulled at, px/tick.</summary>
        public float PullSpeed = 11f;
        /// <summary>Side of the hook's square hitbox, px (used for drawing).</summary>
        public float HookSizePx = 18f;
        /// <summary>Side of the square at the hook's centre that must touch a tile to latch, px.</summary>
        public float LatchProbePx = 10f;
        /// <summary>A returning hook disappears once this close to the player, px.</summary>
        public float CatchDistancePx = 24f;
        /// <summary>A hook this far from the player is dropped, px.</summary>
        public float DropDistancePx = 2500f;
    }

    public enum HookState
    {
        /// <summary>Travelling toward the cursor.</summary>
        Flying,
        /// <summary>Returning to the player, after missing, being released from its tile, or going out of range.</summary>
        Retreating,
        /// <summary>Stuck in a tile, pulling the player.</summary>
        Latched,
    }

    /// <summary>One hook. Center is in px (y-up).</summary>
    public sealed class GrappleHook
    {
        public Vector2 Center;
        public Vector2 PreviousCenter;
        public Vector2 Velocity;
        public HookState State;
    }

    /// <summary>
    /// A player's grappling hooks. Per tick the player first asks for <see cref="PullVelocity"/> (from the
    /// hooks latched so far), moves, and then calls <see cref="Update"/> to advance the hooks from the
    /// player's new position. Each hook update: range check or latch test (flying), steering home
    /// (retreating) or checking its tile still exists (latched), then moving.
    /// </summary>
    public sealed class GrapplingHooks
    {
        readonly GrappleStats m_Stats;
        readonly TileWorld m_World;
        readonly List<GrappleHook> m_Hooks = new();

        public GrapplingHooks(GrappleStats stats, TileWorld world)
        {
            m_Stats = stats;
            m_World = world;
        }

        public GrappleStats Stats => m_Stats;
        public IReadOnlyList<GrappleHook> Hooks => m_Hooks;
        public int Count => m_Hooks.Count;

        public bool AnyLatched
        {
            get
            {
                for (int i = 0; i < m_Hooks.Count; i++)
                {
                    if (m_Hooks[i].State == HookState.Latched) return true;
                }

                return false;
            }
        }

        /// <summary>Fires a hook from the player's centre toward a point. False if all hooks are already out.</summary>
        public bool TryFire(Vector2 originPx, Vector2 targetPx, int facing)
        {
            if (m_Hooks.Count >= m_Stats.MaxHooks) return false;

            Vector2 direction = targetPx - originPx;
            direction = direction.sqrMagnitude < 0.0001f ? new Vector2(facing < 0 ? -1f : 1f, 0f) : direction.normalized;

            m_Hooks.Add(new GrappleHook
            {
                Center = originPx, PreviousCenter = originPx, Velocity = direction * m_Stats.ShootSpeed,
                State = HookState.Flying,
            });
            return true;
        }

        /// <summary>
        /// The velocity the latched hooks pull the player at: toward the average of their positions, at most
        /// <see cref="GrappleStats.PullSpeed"/>, slower only when the point is closer than that. Null if none
        /// is latched.
        /// </summary>
        public Vector2? PullVelocity(Vector2 playerCenterPx)
        {
            Vector2 sum = Vector2.zero;
            int latched = 0;
            for (int i = 0; i < m_Hooks.Count; i++)
            {
                if (m_Hooks[i].State != HookState.Latched) continue;

                sum += m_Hooks[i].Center;
                latched++;
            }

            if (latched == 0) return null;

            Vector2 pull = sum / latched - playerCenterPx;
            float length = pull.magnitude;
            if (length > m_Stats.PullSpeed) pull *= m_Stats.PullSpeed / length;
            return pull;
        }

        /// <summary>Removes every hook (jumping off, dying).</summary>
        public void ReleaseAll() => m_Hooks.Clear();

        /// <summary>Removes the hooks that have already latched or turned back; hooks still flying carry on.</summary>
        public void ReleaseAttached() => m_Hooks.RemoveAll(h => h.State != HookState.Flying);

        /// <summary>Advances every hook one tick, from the player's current centre.</summary>
        public void Update(Vector2 playerCenterPx)
        {
            for (int i = m_Hooks.Count - 1; i >= 0; i--)
            {
                GrappleHook hook = m_Hooks[i];
                hook.PreviousCenter = hook.Center;

                Vector2 toPlayer = playerCenterPx - hook.Center;
                float distance = toPlayer.magnitude;
                if (distance > m_Stats.DropDistancePx)
                {
                    m_Hooks.RemoveAt(i);
                    continue;
                }

                switch (hook.State)
                {
                    case HookState.Flying:
                        if (distance > m_Stats.RangePx) hook.State = HookState.Retreating;
                        else TryLatch(hook);
                        break;

                    case HookState.Retreating:
                        if (distance < m_Stats.CatchDistancePx || distance <= 0f)
                        {
                            m_Hooks.RemoveAt(i);
                            continue;
                        }

                        hook.Velocity = toPlayer / distance * m_Stats.RetreatSpeed;
                        break;

                    case HookState.Latched:
                        // Mining the tile it holds lets go.
                        if (!CanLatch(SimUnits.PixelToTile(hook.Center.x), SimUnits.PixelToTile(hook.Center.y)))
                        {
                            hook.State = HookState.Retreating;
                        }

                        break;
                }

                if (hook.State != HookState.Latched) hook.Center += hook.Velocity;
            }
        }

        /// <summary>
        /// Latches onto the first solid tile touching the probe square at the hook's centre (scanning
        /// left to right, top to bottom) and snaps to that tile's centre.
        /// </summary>
        void TryLatch(GrappleHook hook)
        {
            float half = m_Stats.LatchProbePx * 0.5f;
            int x0 = Mathf.FloorToInt((hook.Center.x - half) / SimUnits.PixelsPerTile);
            int x1 = Mathf.CeilToInt((hook.Center.x + half) / SimUnits.PixelsPerTile) - 1;
            int y0 = Mathf.FloorToInt((hook.Center.y - half) / SimUnits.PixelsPerTile);
            int y1 = Mathf.CeilToInt((hook.Center.y + half) / SimUnits.PixelsPerTile) - 1;

            for (int x = x0; x <= x1; x++)
            {
                for (int y = y1; y >= y0; y--)
                {
                    if (!CanLatch(x, y)) continue;

                    hook.State = HookState.Latched;
                    hook.Velocity = Vector2.zero;
                    hook.Center = new Vector2((x + 0.5f) * SimUnits.PixelsPerTile, (y + 0.5f) * SimUnits.PixelsPerTile);
                    return;
                }
            }
        }

        bool CanLatch(int x, int y) => m_World.InBounds(x, y) && m_World.IsSolid(x, y);
    }
}
