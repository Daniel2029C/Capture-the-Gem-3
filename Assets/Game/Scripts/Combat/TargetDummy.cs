using CTG.Core;
using CTG.Rendering;
using CTG.UI;
using UnityEngine;

namespace CTG.Combat
{
    /// <summary>
    /// Testing target: infinite life, never moves, shows every hit as a floating number and keeps a
    /// rolling damage-per-second readout. Neutral team, so anyone can hit it.
    /// </summary>
    public class TargetDummy : MonoBehaviour, IDamageable, ITickable
    {
        const int k_WidthPx = 22;
        const int k_HeightPx = 46;
        const int k_DpsWindowTicks = 3 * SimUnits.TicksPerSecond;

        Vector2 m_PositionPx;
        SpriteRenderer m_Sprite;
        int m_WobbleTicks;
        int m_LastWobbleDirection = 1;
        readonly int[] m_DamagePerTick = new int[k_DpsWindowTicks];
        int m_CurrentTick;

        public Team Team => Team.Neutral;
        public Rect HitboxPx => new(m_PositionPx.x, m_PositionPx.y, k_WidthPx, k_HeightPx);
        public bool CanBeHit => true;

        public long TotalDamage { get; private set; }
        public int LastHit { get; private set; }

        /// <summary>Damage dealt over the last 3 seconds, per second.</summary>
        public float Dps
        {
            get
            {
                int sum = 0;
                for (int i = 0; i < m_DamagePerTick.Length; i++) sum += m_DamagePerTick[i];
                return sum / (float)(k_DpsWindowTicks / SimUnits.TicksPerSecond);
            }
        }

        /// <param name="bottomLeftPx">Hitbox bottom-left corner, px.</param>
        public void Initialize(Vector2 bottomLeftPx)
        {
            m_PositionPx = bottomLeftPx;
            transform.position = new Vector3((bottomLeftPx.x + k_WidthPx * 0.5f) / SimUnits.PixelsPerTile,
                                             bottomLeftPx.y / SimUnits.PixelsPerTile, 0f);

            m_Sprite = gameObject.AddComponent<SpriteRenderer>();
            m_Sprite.sprite = PlaceholderSprites.Dummy(24, 48);
            m_Sprite.sortingOrder = 9;

            DamageableRegistry.Register(this);
            SimulationClock.Instance.Register(this);
        }

        void OnDestroy()
        {
            DamageableRegistry.Unregister(this);
            if (SimulationClock.Instance != null) SimulationClock.Instance.Unregister(this);
        }

        public int ApplyHit(in HitInfo hit)
        {
            int damage = CombatRules.ComputeDamage(hit.Damage, 0);
            if (damage <= 0) return 0;

            TotalDamage += damage;
            LastHit = damage;
            m_DamagePerTick[m_CurrentTick % k_DpsWindowTicks] += damage;
            m_WobbleTicks = 12;
            m_LastWobbleDirection = hit.Direction == 0 ? 1 : hit.Direction;

            CombatText.Spawn(HitboxPx.center, damage.ToString(), CombatText.DummyDamaged);
            return damage;
        }

        public void Tick(int tick)
        {
            m_CurrentTick = tick + 1;
            m_DamagePerTick[m_CurrentTick % k_DpsWindowTicks] = 0;
            if (m_WobbleTicks > 0) m_WobbleTicks--;
        }

        void LateUpdate()
        {
            // Cosmetic sway when hit; the hitbox never moves.
            float sway = m_WobbleTicks > 0 ? Mathf.Sin(m_WobbleTicks * 0.9f) * m_WobbleTicks * 0.8f : 0f;
            transform.rotation = Quaternion.Euler(0f, 0f, -sway * m_LastWobbleDirection);
        }
    }
}
