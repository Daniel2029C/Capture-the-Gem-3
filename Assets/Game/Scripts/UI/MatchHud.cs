using CTG.Combat;
using CTG.Gameplay;
using CTG.Rendering;
using UnityEngine;

namespace CTG.UI
{
    /// <summary>
    /// Top-of-screen match readout: each team's gem carrier and progress toward the enemy base, captures,
    /// the match clock, and the latest announcement. IMGUI placeholder.
    /// </summary>
    public class MatchHud : MonoBehaviour
    {
        GemMatch m_Match;
        GUIStyle m_Text, m_Center, m_Message;

        public static MatchHud Create(Transform parent, GemMatch match)
        {
            var hud = new GameObject("Match HUD").AddComponent<MatchHud>();
            hud.transform.SetParent(parent, false);
            hud.m_Match = match;
            return hud;
        }

        void OnGUI()
        {
            if (m_Match == null) return;

            m_Text ??= new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            m_Center ??= new GUIStyle(m_Text) { fontSize = 20 };
            m_Message ??= new GUIStyle(m_Text) { fontSize = 18 };

            float cx = Screen.width * 0.5f;
            const float panelW = 280f, panelH = 46f, top = 6f;

            DrawTeam(new Rect(cx - 70f - panelW, top, panelW, panelH), m_Match.BlueGem, TeamColors.For(Team.Blue));
            DrawTeam(new Rect(cx + 70f, top, panelW, panelH), m_Match.RedGem, TeamColors.For(Team.Red));

            Fill(new Rect(cx - 64f, top, 38f, 34f), TeamColors.For(Team.Blue) * 0.6f);
            GUI.Label(new Rect(cx - 64f, top, 38f, 34f), m_Match.BlueCaptures.ToString(), m_Center);
            Fill(new Rect(cx + 26f, top, 38f, 34f), TeamColors.For(Team.Red) * 0.6f);
            GUI.Label(new Rect(cx + 26f, top, 38f, 34f), m_Match.RedCaptures.ToString(), m_Center);

            int seconds = m_Match.MatchTicks / 60;
            string clock = m_Match.IsOvertime ? "OT" : $"{seconds / 60}:{seconds % 60:00}";
            GUI.Label(new Rect(cx - 24f, top, 48f, 34f), clock, m_Center);

            if (m_Match.MessageTicksLeft > 0)
            {
                m_Message.normal.textColor = m_Match.MessageColor;
                GUI.Label(new Rect(0f, top + 56f, Screen.width, 30f), m_Match.Message, m_Message);
            }
        }

        void DrawTeam(Rect rect, Gem gem, Color teamColor)
        {
            Fill(rect, new Color(0f, 0f, 0f, 0.55f));
            Rect bar = new(rect.x + 8f, rect.y + 6f, rect.width - 16f, 10f);
            Fill(bar, new Color(1f, 1f, 1f, 0.2f));
            Fill(new Rect(bar.x, bar.y, bar.width * m_Match.Progress(gem), bar.height), Color.white);
            GUI.Label(new Rect(rect.x, rect.y + 18f, rect.width, 24f), m_Match.CarrierText(gem), m_Text);
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
