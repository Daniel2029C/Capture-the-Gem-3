using System.Collections.Generic;
using CTG.Core;
using UnityEngine;

namespace CTG.UI
{
    /// <summary>
    /// Floating combat numbers: spawned at a world position, drift upward and fade over about a second.
    /// Drawn with IMGUI so they need no font assets; replace with the real UI later.
    /// </summary>
    public class CombatText : MonoBehaviour
    {
        public static readonly Color DummyDamaged = new Color32(255, 160, 70, 255);
        public static readonly Color PlayerDamaged = new Color32(255, 70, 80, 255);
        public static readonly Color Healed = new Color32(100, 255, 100, 255);

        const float k_Lifetime = 1f;
        const float k_RisePerSecond = 1.5f; // world units
        const int k_MaxEntries = 64;

        struct Entry
        {
            public Vector2 WorldPos;
            public string Text;
            public Color Color;
            public float Age;
            public float DriftX;
        }

        static CombatText s_Instance;
        readonly List<Entry> m_Entries = new();
        GUIStyle m_Style;
        GUIStyle m_Shadow;

        void Awake() => s_Instance = this;

        void OnDestroy()
        {
            if (s_Instance == this) s_Instance = null;
        }

        /// <param name="positionPx">Simulation pixels (y-up).</param>
        public static void Spawn(Vector2 positionPx, string text, Color color)
        {
            if (s_Instance == null) return;
            if (s_Instance.m_Entries.Count >= k_MaxEntries) s_Instance.m_Entries.RemoveAt(0);

            s_Instance.m_Entries.Add(new Entry
            {
                WorldPos = positionPx / SimUnits.PixelsPerTile + new Vector2(0f, 1.5f),
                Text = text,
                Color = color,
                DriftX = Random.Range(-0.5f, 0.5f), // cosmetic only
            });
        }

        void Update()
        {
            for (int i = m_Entries.Count - 1; i >= 0; i--)
            {
                Entry e = m_Entries[i];
                e.Age += Time.deltaTime;
                if (e.Age >= k_Lifetime)
                {
                    m_Entries.RemoveAt(i);
                    continue;
                }

                e.WorldPos += new Vector2(e.DriftX, k_RisePerSecond) * Time.deltaTime;
                m_Entries[i] = e;
            }
        }

        void OnGUI()
        {
            Camera cam = Camera.main;
            if (cam == null || m_Entries.Count == 0) return;

            if (m_Style == null)
            {
                m_Style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                };
                m_Shadow = new GUIStyle(m_Style) { normal = { textColor = Color.black } };
            }

            foreach (Entry e in m_Entries)
            {
                Vector3 screen = cam.WorldToScreenPoint(e.WorldPos);
                var rect = new Rect(screen.x - 60f, Screen.height - screen.y - 15f, 120f, 30f);
                float alpha = 1f - Mathf.Clamp01((e.Age - k_Lifetime * 0.5f) / (k_Lifetime * 0.5f));

                m_Shadow.normal.textColor = new Color(0f, 0f, 0f, alpha * 0.8f);
                GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), e.Text, m_Shadow);
                Color c = e.Color;
                c.a = alpha;
                m_Style.normal.textColor = c;
                GUI.Label(rect, e.Text, m_Style);
            }
        }
    }
}
