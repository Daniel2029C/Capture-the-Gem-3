using System.Collections.Generic;
using CTG.Combat;
using CTG.Core;
using CTG.Gameplay;
using CTG.Rendering;
using UnityEngine;

namespace CTG.Economy
{
    /// <summary>
    /// An altar where offerings are cashed in. A player carrying one stands still within reach for 1.75 seconds to cash
    /// in a single offering (then channels again for the next). Damage doesn't interrupt the channel; moving, or being
    /// knocked out of reach, does. A base altar is safe and only works for its own team; the mid altar works for anyone
    /// and pays more.
    /// </summary>
    public sealed class Altar : MonoBehaviour, ITickable
    {
        public const int ChannelTicks = 105;
        /// <summary>Reach around the altar's base centre: half-width and height, px.</summary>
        const float k_ReachHalfWidthPx = 32f;
        const float k_ReachHeightPx = 64f;

        static readonly List<Altar> s_All = new();
        public static IReadOnlyList<Altar> All => s_All;

        public AltarKind Kind { get; private set; }
        /// <summary>The team that can use a base altar; Neutral for the mid altar (anyone).</summary>
        public Team Owner { get; private set; }
        /// <summary>Bottom centre of the altar, px.</summary>
        public Vector2 PositionPx { get; private set; }

        readonly Dictionary<PlayerCharacter, int> m_Progress = new();
        Transform m_Glow;

        public static Altar Create(Transform parent, AltarKind kind, Team owner, Vector2 bottomCenterPx)
        {
            var go = new GameObject(kind == AltarKind.Mid ? "Mid Altar" : $"{owner} Altar");
            go.transform.SetParent(parent, false);
            var altar = go.AddComponent<Altar>();
            altar.Kind = kind;
            altar.Owner = owner;
            altar.PositionPx = bottomCenterPx;
            altar.BuildVisuals();
            s_All.Add(altar);
            SimulationClock.Instance.Register(altar);
            return altar;
        }

        void BuildVisuals()
        {
            Color color = Kind == AltarKind.Mid ? new Color32(190, 110, 255, 255) : Owner == Team.Blue ? new Color32(80, 140, 255, 255) : new Color32(255, 90, 90, 255);
            transform.position = PositionPx / SimUnits.PixelsPerTile;

            // A stepped stone pillar with a glowing top.
            MakePart("Base", new Color32(90, 90, 105, 255), 28, 6, 3f / SimUnits.PixelsPerTile, 5);
            MakePart("Pillar", new Color32(130, 130, 150, 255), 16, 14, 9f / SimUnits.PixelsPerTile, 5);
            Transform top = MakePart("Top", new Color32(150, 150, 170, 255), 22, 4, 18f / SimUnits.PixelsPerTile, 5);
            top.gameObject.name = "Top";
            m_Glow = MakePart("Glow", color, 10, 10, 26f / SimUnits.PixelsPerTile, 6);
        }

        Transform MakePart(string name, Color color, int w, int h, float y, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderSprites.Solid($"altar_{name}_{color}_{w}x{h}", color, w, h);
            sr.sortingOrder = order;
            go.transform.localPosition = new Vector3(0f, y, 0f);
            return go.transform;
        }

        void OnDestroy()
        {
            s_All.Remove(this);
            if (SimulationClock.Instance != null) SimulationClock.Instance.Unregister(this);
        }

        /// <summary>The altar's reach, px.</summary>
        public Rect ReachPx => new(PositionPx.x - k_ReachHalfWidthPx, PositionPx.y - 8f, k_ReachHalfWidthPx * 2f, k_ReachHeightPx);

        public bool CanUse(PlayerCharacter player) =>
            player != null && !player.Health.IsDead
            && (Kind == AltarKind.Mid || player.Team == Owner);

        /// <summary>0..1 channel progress for a player at this altar.</summary>
        public float ProgressFor(PlayerCharacter player) =>
            m_Progress.TryGetValue(player, out int ticks) ? ticks / (float)ChannelTicks : 0f;

        public void Tick(int tick)
        {
            var all = DamageableRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is PlayerCharacter player) TickPlayer(player);
            }
        }

        void TickPlayer(PlayerCharacter player)
        {
            if (!IsChanneling(player))
            {
                m_Progress.Remove(player);
                return;
            }

            m_Progress.TryGetValue(player, out int ticks);
            ticks++;
            if (ticks < ChannelTicks)
            {
                m_Progress[player] = ticks;
                return;
            }

            m_Progress.Remove(player);
            if (player.TakeOffering()) MatchEconomy.Instance?.AwardOffering(player, Kind);
        }

        /// <summary>Alive, allowed to use this altar, carrying an offering, within reach and standing still.</summary>
        bool IsChanneling(PlayerCharacter player)
        {
            if (player.Health.IsDead || player.Offerings <= 0) return false;
            if (Kind == AltarKind.Base && player.Team != Owner) return false;
            if (!ReachPx.Overlaps(player.HitboxPx)) return false;

            PlayerMotor motor = player.Motor;
            var input = player.LastInput;
            bool tryingToMove = input.Left || input.Right || input.Jump;
            return motor.Grounded && !tryingToMove && Mathf.Abs(motor.Velocity.x) < 0.1f;
        }

        void LateUpdate()
        {
            // The glow pulses, and brightens while someone is cashing in.
            float pulse = 1f + 0.15f * Mathf.Sin(Time.time * 3f);
            float channel = m_Progress.Count > 0 ? 1.4f : 1f;
            if (m_Glow != null) m_Glow.localScale = Vector3.one * pulse * channel;
        }
    }
}
