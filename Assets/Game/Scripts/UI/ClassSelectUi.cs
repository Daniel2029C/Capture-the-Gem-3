using System;
using CTG.Classes;
using UnityEngine;

namespace CTG.UI
{
    /// <summary>
    /// Testing screen shown at start: one button per class. Choosing one hands it to the callback and
    /// closes the screen. The simulation stays paused by the caller until then.
    /// </summary>
    public class ClassSelectUi : MonoBehaviour
    {
        Action<ClassDef> m_OnSelected;
        GUIStyle m_Title;
        GUIStyle m_Button;
        GUIStyle m_Small;

        public void Bind(Action<ClassDef> onSelected) => m_OnSelected = onSelected;

        void OnGUI()
        {
            if (m_OnSelected == null) return;

            m_Title ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 32, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white },
            };
            m_Button ??= new GUIStyle(GUI.skin.button) { fontSize = 22, fontStyle = FontStyle.Bold };
            m_Small ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 14, alignment = TextAnchor.UpperCenter, wordWrap = true,
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f) },
            };

            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previous;

            var classes = ClassDefs.All;
            const float cardWidth = 220f, cardHeight = 110f, gap = 16f;
            float total = classes.Count * cardWidth + (classes.Count - 1) * gap;
            float x = (Screen.width - total) * 0.5f;
            float y = Screen.height * 0.3f;

            GUI.Label(new Rect(0f, y - 70f, Screen.width, 50f), "Choose a class", m_Title);

            for (int i = 0; i < classes.Count; i++)
            {
                ClassDef def = classes[i];
                var rect = new Rect(x + i * (cardWidth + gap), y, cardWidth, cardHeight);
                string mana = def.MaxMana > 0 ? $"   Mana {def.MaxMana}" : "";
                if (GUI.Button(new Rect(rect.x, rect.y, rect.width, 50f), def.Name, m_Button))
                {
                    Action<ClassDef> callback = m_OnSelected;
                    m_OnSelected = null;
                    callback(def);
                    return;
                }

                GUI.Label(new Rect(rect.x, rect.y + 56f, rect.width, 54f),
                          $"Life {def.MaxLife}{mana}\n{def.Description}", m_Small);
            }
        }
    }
}
