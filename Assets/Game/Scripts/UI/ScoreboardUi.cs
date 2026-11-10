using System.Collections.Generic;
using CTG.Combat;
using CTG.Economy;
using CTG.Gameplay;
using CTG.Input;
using UnityEngine;

namespace CTG.UI
{
    /// <summary>
    /// The scoreboard (IMGUI placeholder), shown while the scoreboard key is held: both teams, with each player's
    /// class, level, kills, deaths, assists and bounty (the shards an enemy earns for killing them right now).
    /// A bounty above the base kill value is highlighted gold, so fed players stand out as targets.
    /// </summary>
    public sealed class ScoreboardUi : MonoBehaviour
    {
        const float k_Width = 700f;
        const float k_RowHeight = 26f;

        GUIStyle m_Title, m_Cell, m_Head;
        readonly List<PlayerCharacter> m_Players = new();

        public static ScoreboardUi Create(Transform parent)
        {
            var go = new GameObject("Scoreboard UI");
            go.transform.SetParent(parent, false);
            return go.AddComponent<ScoreboardUi>();
        }

        void EnsureStyles()
        {
            if (m_Cell != null) return;

            m_Title = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            m_Cell = new GUIStyle(GUI.skin.label) { fontSize = 15, normal = { textColor = Color.white } };
            m_Head = new GUIStyle(m_Cell) { fontSize = 12, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.75f, 0.8f, 0.95f) } };
        }

        void OnGUI()
        {
            if (!KeyBindings.IsPressed(BindAction.Scoreboard)) return;

            m_Players.Clear();
            var all = DamageableRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is PlayerCharacter player) m_Players.Add(player);
            }

            if (m_Players.Count == 0) return;
            EnsureStyles();

            // The local player's team first.
            Team own = PlayerCharacter.LocalPlayer != null ? PlayerCharacter.LocalPlayer.Team : Team.Blue;
            Team other = own == Team.Blue ? Team.Red : Team.Blue;

            int rows = CountTeam(own) + CountTeam(other);
            float height = 56f + 2f * (k_RowHeight + 14f) + rows * k_RowHeight;
            var panel = new Rect((Screen.width - k_Width) * 0.5f, 70f, k_Width, height);
            Fill(panel, new Color(0.04f, 0.05f, 0.09f, 0.92f));

            float y = panel.y + 10f;
            y = DrawTeam(panel, y, own);
            DrawTeam(panel, y + 8f, other);
        }

        int CountTeam(Team team)
        {
            int n = 0;
            foreach (PlayerCharacter p in m_Players) if (p.Team == team) n++;
            return n;
        }

        float DrawTeam(Rect panel, float y, Team team)
        {
            Color color = TeamColors.For(team);
            Fill(new Rect(panel.x, y, panel.width, 24f), new Color(color.r, color.g, color.b, 0.55f));
            GUI.Label(new Rect(panel.x + 10f, y - 1f, 200f, 24f), team == Team.Blue ? "Blue team" : "Red team", m_Title);
            DrawColumns(panel, y, m_Head, null, "CLASS", "LV", "K", "D", "A", "BOUNTY");
            y += 26f;

            var members = new List<PlayerCharacter>();
            foreach (PlayerCharacter p in m_Players) if (p.Team == team) members.Add(p);
            members.Sort((a, b) => b.Kills != a.Kills ? b.Kills.CompareTo(a.Kills) : a.Deaths.CompareTo(b.Deaths));

            foreach (PlayerCharacter p in members)
            {
                bool local = p == PlayerCharacter.LocalPlayer;
                if (local) Fill(new Rect(panel.x, y, panel.width, k_RowHeight), new Color(1f, 1f, 1f, 0.1f));

                string name = local ? "You" : $"Player {m_Players.IndexOf(p) + 1}";
                DrawColumns(panel, y, m_Cell, name, p.Class != null ? p.Class.Name : "-", p.Progress.Level.ToString(),
                            p.Kills.ToString(), p.Deaths.ToString(), p.Assists.ToString(), null);

                // Bounty: gold when the player is worth more than a plain kill, dimmer when worth less.
                int bounty = p.Bounty;
                Color bountyColor = bounty > EconomyRules.BaseKillShards ? new Color(1f, 0.82f, 0.25f)
                                  : bounty < EconomyRules.BaseKillShards ? new Color(0.65f, 0.65f, 0.7f) : Color.white;
                var style = new GUIStyle(m_Cell) { fontStyle = bounty > EconomyRules.BaseKillShards ? FontStyle.Bold : FontStyle.Normal, normal = { textColor = bountyColor } };
                GUI.Label(new Rect(ColumnX(panel, 6), y, 90f, k_RowHeight), bounty.ToString(), style);
                y += k_RowHeight;
            }

            return y;
        }

        static readonly float[] s_ColumnOffsets = { 12f, 190f, 330f, 380f, 440f, 500f, 580f };

        static float ColumnX(Rect panel, int column) => panel.x + s_ColumnOffsets[column];

        void DrawColumns(Rect panel, float y, GUIStyle style, params string[] cells)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i] == null) continue;

                float x = ColumnX(panel, i);
                // The title row's header text sits right of the team name.
                if (style == m_Head) GUI.Label(new Rect(x, y + 2f, 120f, 22f), cells[i], style);
                else GUI.Label(new Rect(x, y, 170f, k_RowHeight), cells[i], style);
            }
        }

        static void Fill(Rect rect, Color color)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
