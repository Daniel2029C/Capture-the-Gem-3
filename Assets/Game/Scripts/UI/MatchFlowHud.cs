using CTG.Combat;
using CTG.Gameplay;
using CTG.Rendering;
using UnityEngine;

namespace CTG.UI
{
    /// <summary>The countdown before play and the results screen after it. IMGUI placeholder.</summary>
    public class MatchFlowHud : MonoBehaviour
    {
        MatchFlow m_Flow;
        GUIStyle m_Huge, m_Title, m_Line, m_Button;

        public static MatchFlowHud Create(Transform parent, MatchFlow flow)
        {
            var hud = new GameObject("Match Flow HUD").AddComponent<MatchFlowHud>();
            hud.transform.SetParent(parent, false);
            hud.m_Flow = flow;
            return hud;
        }

        void OnGUI()
        {
            if (m_Flow == null) return;

            m_Huge ??= new GUIStyle(GUI.skin.label) { fontSize = 96, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            m_Title ??= new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            m_Line ??= new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            m_Button ??= new GUIStyle(GUI.skin.button) { fontSize = 22, fontStyle = FontStyle.Bold };

            switch (m_Flow.Phase)
            {
                case MatchPhase.Countdown: DrawCountdown(); break;
                case MatchPhase.Ended: DrawResults(m_Flow.Result.Value); break;
            }
        }

        void DrawCountdown()
        {
            int seconds = Mathf.CeilToInt(m_Flow.CountdownTicksLeft / 60f);
            float cy = Screen.height * 0.3f;
            GUI.Label(new Rect(0f, cy - 50f, Screen.width, 40f), "Match starts in", m_Line);
            GUI.Label(new Rect(0f, cy, Screen.width, 120f), seconds.ToString(), m_Huge);
        }

        void DrawResults(MatchResult r)
        {
            Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.6f));

            float w = 460f, h = 330f;
            var panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            Fill(panel, new Color(0.05f, 0.06f, 0.1f, 0.92f));

            bool blue = r.Winner == Team.Blue;
            m_Title.normal.textColor = TeamColors.For(r.Winner);
            GUI.Label(new Rect(panel.x, panel.y + 16f, w, 56f), blue ? "BLUE TEAM WINS" : "RED TEAM WINS", m_Title);

            int seconds = r.MatchTicks / 60;
            string time = $"{seconds / 60}:{seconds % 60:00}" + (r.WentToOvertime ? "  (overtime)" : "");
            GUI.Label(new Rect(panel.x, panel.y + 84f, w, 30f), $"Captures   Blue {r.BlueCaptures}  -  Red {r.RedCaptures}", m_Line);
            GUI.Label(new Rect(panel.x, panel.y + 116f, w, 30f), $"Match time   {time}", m_Line);
            GUI.Label(new Rect(panel.x, panel.y + 148f, w, 30f), $"Your pickups {r.Pickups}   Your captures {r.Captures}", m_Line);

            Event e = Event.current;
            bool again = GUI.Button(new Rect(panel.x + 40f, panel.y + 220f, 170f, 52f), "Play again", m_Button);
            bool quit = GUI.Button(new Rect(panel.x + w - 210f, panel.y + 220f, 170f, 52f), "Quit", m_Button);
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.R)) again = true;

            if (again) m_Flow.PlayAgain();
            else if (quit) m_Flow.Quit();
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
