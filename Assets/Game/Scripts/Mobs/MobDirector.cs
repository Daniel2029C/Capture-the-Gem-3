using CTG.Combat;
using CTG.Core;
using CTG.Economy;
using CTG.Gameplay;
using CTG.Rendering;
using CTG.World;
using UnityEngine;

namespace CTG.Mobs
{
    /// <summary>
    /// Runs the Drakon spawns. Each side (blue and red) has one Drakon on the open ground between its gem area and
    /// mid. The first pair spawns together at 0:45; after that each side's Drakon returns 45 seconds after the one on
    /// that same side dies, so keeping an enemy-side Drakon alive denies the enemy the offering but not their own. Every
    /// spawn is telegraphed 5 seconds ahead with a marker and countdown. At the Apex Shift spawning stops and any
    /// Drakon left is removed.
    /// </summary>
    public sealed class MobDirector : MonoBehaviour, ITickable
    {
        public const int FirstSpawnTicks = 45 * SimUnits.TicksPerSecond;
        public const int RespawnTicks = 45 * SimUnits.TicksPerSecond;
        public const int TelegraphTicks = 5 * SimUnits.TicksPerSecond;
        /// <summary>The mid-boss returns this long after it was killed.</summary>
        public const int BossRespawnTicks = 5 * 60 * SimUnits.TicksPerSecond;

        sealed class Slot
        {
            public Team Side;
            public RectInt Area;
            public Drakon Mob;
            public int SpawnAt;          // match tick the next Drakon appears
            public bool Telegraphing;
            public Vector2 SpawnPx;
            public Transform Marker;
        }

        readonly Slot[] m_Slots = new Slot[2];
        LoadedMap m_Map;
        GemMatch m_Match;
        bool m_Ended;
        GUIStyle m_Label;
        ElderDrakon m_Boss;
        int m_BossRespawnAt = -1;
        /// <summary>The mid-boss is alive.</summary>
        public ElderDrakon Boss => m_Boss;

        /// <summary>Match ticks until a side's next Drakon, or -1 if one is alive or spawning has ended.</summary>
        public int TicksUntilSpawn(Team side)
        {
            Slot slot = side == Team.Blue ? m_Slots[0] : m_Slots[1];
            return slot.Mob != null || m_Ended ? -1 : Mathf.Max(0, slot.SpawnAt - m_Match.MatchTicks);
        }

        public bool IsAlive(Team side) => (side == Team.Blue ? m_Slots[0] : m_Slots[1]).Mob != null;

        public static MobDirector Create(Transform parent, LoadedMap map, GemMatch match)
        {
            var go = new GameObject("Mob Director");
            go.transform.SetParent(parent, false);
            var director = go.AddComponent<MobDirector>();
            director.m_Map = map;
            director.m_Match = match;
            Team[] sides = { Team.Blue, Team.Red };
            for (int i = 0; i < 2; i++)
            {
                director.m_Slots[i] = new Slot
                {
                    Side = sides[i], Area = MapLayout.MobSpawnArea(map, sides[i]), SpawnAt = FirstSpawnTicks,
                    Marker = director.MakeMarker(sides[i]),
                };
            }

            match.ApexShift += director.OnApexShift;
            SimulationClock.Instance.Register(director);
            return director;
        }

        Transform MakeMarker(Team side)
        {
            var go = new GameObject($"Spawn Marker {side}");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderSprites.Solid("spawn_marker", new Color32(255, 150, 60, 255), 24, 24);
            sr.sortingOrder = 5;
            go.SetActive(false);
            return go.transform;
        }

        void OnDestroy()
        {
            if (m_Match != null) m_Match.ApexShift -= OnApexShift;
            if (SimulationClock.Instance != null) SimulationClock.Instance.Unregister(this);
        }

        void OnApexShift()
        {
            m_Ended = true;
            foreach (Slot slot in m_Slots)
            {
                slot.Marker.gameObject.SetActive(false);
                slot.Telegraphing = false;
                if (slot.Mob != null) slot.Mob.Despawn();
                slot.Mob = null;
            }

            SpawnBoss();

            // Offerings are worthless after the shift: every player's are wiped.
            var all = DamageableRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is PlayerCharacter player) player.ClearOfferings(true);
            }
        }

        /// <summary>The mid-boss appears on the mid platform at the Apex Shift.</summary>
        void SpawnBoss()
        {
            Vector2Int floor = MapLayout.BossFloorTile(m_Map);
            var bottomCentre = new Vector2((floor.x + 0.5f) * SimUnits.PixelsPerTile, (floor.y + 1) * SimUnits.PixelsPerTile);
            m_Boss = ElderDrakon.Spawn(transform, bottomCentre, m_Map.World);
            m_Boss.Died += (_, killer) =>
            {
                m_Boss = null;
                m_BossRespawnAt = m_Match.MatchTicks + BossRespawnTicks;
                if (killer == null) return;

                MatchEconomy.Instance?.AwardBossKill(killer);
                bool blue = killer.Team == Team.Blue;
                m_Match.Announce($"Player has slain the Elder Drakon! The {(blue ? "blue" : "red")} team gains the Apex buff!", blue ? GemMatch.BlueMessage : GemMatch.RedMessage);
            };
            m_Match.Announce("The Elder Drakon has awoken at mid!", Color.cyan);
        }

        public void Tick(int tick)
        {
            // After the Apex Shift the only thing left to run is the boss coming back.
            if (m_Ended && m_Boss == null && m_BossRespawnAt >= 0 && m_Match.Running && !m_Match.IsOver && m_Match.MatchTicks >= m_BossRespawnAt)
            {
                m_BossRespawnAt = -1;
                SpawnBoss();
            }

            if (m_Ended || !m_Match.Running || m_Match.IsOver) return;

            int now = m_Match.MatchTicks;
            foreach (Slot slot in m_Slots)
            {
                if (slot.Mob != null) continue;

                if (!slot.Telegraphing && now >= slot.SpawnAt - TelegraphTicks)
                {
                    slot.SpawnPx = PickSpawnPoint(slot.Area);
                    slot.Telegraphing = true;
                    slot.Marker.gameObject.SetActive(true);
                    slot.Marker.position = slot.SpawnPx / SimUnits.PixelsPerTile;
                }

                if (slot.Telegraphing && now >= slot.SpawnAt) SpawnMob(slot);
            }
        }

        void SpawnMob(Slot slot)
        {
            slot.Telegraphing = false;
            slot.Marker.gameObject.SetActive(false);

            Rect home = new(slot.Area.x * SimUnits.PixelsPerTile, slot.Area.y * SimUnits.PixelsPerTile,
                            slot.Area.width * SimUnits.PixelsPerTile, slot.Area.height * SimUnits.PixelsPerTile);
            Drakon mob = Drakon.Spawn(transform, m_Map.World, slot.SpawnPx, home);
            slot.Mob = mob;
            mob.Died += (_, killer) => OnMobDied(slot, killer);
        }

        void OnMobDied(Slot slot, PlayerCharacter killer)
        {
            slot.Mob = null;
            if (m_Ended) return;

            // The next Drakon on this side arrives 45 seconds after this one died.
            slot.SpawnAt = m_Match.MatchTicks + RespawnTicks;
            MatchEconomy.Instance?.AwardMobKill(killer);

            // Everyone is told which team took the offering (not where it is), so carriers can be tracked.
            if (killer != null)
            {
                bool blue = killer.Team == Team.Blue;
                m_Match.Announce($"Player has taken a Drakon's offering for the {(blue ? "blue" : "red")} team!", blue ? GemMatch.BlueMessage : GemMatch.RedMessage);
            }
        }

        /// <summary>A random open spot in the area that has room for the Drakon; the area's centre if none is found.</summary>
        Vector2 PickSpawnPoint(RectInt area)
        {
            TileWorld world = m_Map.World;
            for (int attempt = 0; attempt < 200; attempt++)
            {
                int x = Random.Range(area.xMin + 2, area.xMax - 2);
                int y = Random.Range(area.yMin + 2, area.yMax - 2);
                if (IsOpen(world, x, y)) return new Vector2((x + 0.5f) * SimUnits.PixelsPerTile, (y + 0.5f) * SimUnits.PixelsPerTile);
            }

            return new Vector2(area.center.x * SimUnits.PixelsPerTile, area.center.y * SimUnits.PixelsPerTile);
        }

        static bool IsOpen(TileWorld world, int tx, int ty)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (world.IsSolid(tx + dx, ty + dy) || world.GetLiquid(tx + dx, ty + dy) != LiquidType.None) return false;
                }
            }

            return true;
        }

        /// <summary>A wide life bar at the top of the screen while the boss is alive.</summary>
        void DrawBossBar()
        {
            if (m_Boss == null) return;

            const float width = 520f, height = 22f;
            var bar = new Rect((Screen.width - width) * 0.5f, 56f, width, height);
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            float fraction = m_Boss.Health / (float)ElderDrakon.MaxHealth;
            GUI.color = m_Boss.IsRegenerating ? new Color(0.3f, 0.9f, 0.4f, 0.95f) : new Color(0.85f, 0.2f, 0.25f, 0.95f);
            GUI.DrawTexture(new Rect(bar.x + 2f, bar.y + 2f, (bar.width - 4f) * fraction, bar.height - 4f), Texture2D.whiteTexture);
            GUI.color = previous;

            if (m_Label == null)
            {
                m_Label = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(1f, 0.75f, 0.35f) },
                };
            }

            string state = m_Boss.IsRegenerating ? "  (regenerating)" : "";
            GUI.Label(new Rect(bar.x, bar.y - 26f, bar.width, 24f), $"Elder Drakon  {m_Boss.Health}/{ElderDrakon.MaxHealth}{state}", m_Label);
        }

        void Update()
        {
            foreach (Slot slot in m_Slots)
            {
                if (!slot.Marker.gameObject.activeSelf) continue;

                float pulse = 0.8f + 0.3f * Mathf.Sin(Time.time * 7f);
                slot.Marker.localScale = Vector3.one * pulse;
            }
        }

        void OnGUI()
        {
            if (m_Match == null) return;

            DrawBossBar();
            if (m_Ended) return;

            Camera cam = Camera.main;
            if (cam == null) return;

            if (m_Label == null)
            {
                m_Label = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(1f, 0.75f, 0.35f) },
                };
            }

            foreach (Slot slot in m_Slots)
            {
                if (!slot.Telegraphing) continue;

                int seconds = Mathf.CeilToInt((slot.SpawnAt - m_Match.MatchTicks) / (float)SimUnits.TicksPerSecond);
                Vector3 screen = cam.WorldToScreenPoint(slot.SpawnPx / SimUnits.PixelsPerTile + new Vector2(0f, 1.5f));
                GUI.Label(new Rect(screen.x - 70f, Screen.height - screen.y - 12f, 140f, 24f), $"Drakon in {seconds}", m_Label);
            }
        }
    }
}
