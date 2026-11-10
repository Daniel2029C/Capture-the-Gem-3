using System.Collections.Generic;
using CTG.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace CTG.UI
{
    /// <summary>
    /// Menu (Esc) with the key binding settings: click a control, press the new key or mouse button. Escape
    /// cancels a rebind. While the menu is open the game ignores the controls (<see cref="InputBlock.All"/>).
    /// </summary>
    public sealed class KeybindMenuUi : MonoBehaviour
    {
        bool m_Open;
        BindAction? m_Rebinding;
        bool m_WaitForMouseRelease;
        Vector2 m_Scroll;
        GUIStyle m_Title, m_Label, m_Button;
        Texture2D m_White;
        readonly List<BindAction> m_Actions = new();

        public static KeybindMenuUi Create(Transform parent)
        {
            var go = new GameObject("Keybind Menu");
            go.transform.SetParent(parent, false);
            return go.AddComponent<KeybindMenuUi>();
        }

        void Awake() => m_Actions.AddRange(KeyBindings.Actions);

        void OnDestroy() => InputBlock.All = false;

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (m_Rebinding.HasValue)
            {
                CaptureBinding(keyboard);
                return;
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                m_Open = !m_Open;
                InputBlock.All = m_Open;
            }
        }

        void CaptureBinding(Keyboard keyboard)
        {
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                m_Rebinding = null;
                return;
            }

            // The click that started the rebind must be released before a mouse button can be chosen.
            Mouse mouse = Mouse.current;
            if (m_WaitForMouseRelease && mouse != null && !mouse.leftButton.isPressed) m_WaitForMouseRelease = false;

            foreach (KeyControl key in keyboard.allKeys)
            {
                if (key.keyCode == Key.None || key.keyCode == Key.Escape || !key.wasPressedThisFrame) continue;

                KeyBindings.Set(m_Rebinding.Value, Binding.OfKey(key.keyCode));
                m_Rebinding = null;
                return;
            }

            if (mouse == null || m_WaitForMouseRelease) return;

            for (int button = 0; button < 3; button++)
            {
                var control = button == 0 ? mouse.leftButton : button == 1 ? mouse.rightButton : mouse.middleButton;
                if (!control.wasPressedThisFrame) continue;

                KeyBindings.Set(m_Rebinding.Value, Binding.OfMouse(button));
                m_Rebinding = null;
                return;
            }
        }

        void EnsureStyles()
        {
            if (m_White != null) return;

            m_White = Texture2D.whiteTexture;
            m_Title = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            m_Label = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            m_Button = new GUIStyle(GUI.skin.button) { fontSize = 15 };
        }

        void OnGUI()
        {
            if (!m_Open) return;

            EnsureStyles();
            const float width = 520f, height = 560f;
            var panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            Fill(panel, new Color(0.05f, 0.08f, 0.18f, 0.96f));
            Fill(new Rect(panel.x + 3f, panel.y + 3f, panel.width - 6f, panel.height - 6f), new Color(0.12f, 0.18f, 0.38f, 0.96f));

            GUI.Label(new Rect(panel.x + 16f, panel.y + 8f, 300f, 34f), "Key bindings", m_Title);

            var viewport = new Rect(panel.x + 12f, panel.y + 52f, panel.width - 24f, panel.height - 120f);
            var content = new Rect(0f, 0f, viewport.width - 20f, m_Actions.Count * 34f);
            m_Scroll = GUI.BeginScrollView(viewport, m_Scroll, content);
            for (int i = 0; i < m_Actions.Count; i++)
            {
                BindAction action = m_Actions[i];
                float y = i * 34f;
                GUI.Label(new Rect(4f, y, 250f, 30f), KeyBindings.DisplayName(action), m_Label);

                string text = m_Rebinding == action ? "Press a key..." : KeyBindings.Label(action);
                if (GUI.Button(new Rect(260f, y + 2f, 210f, 28f), text, m_Button) && !m_Rebinding.HasValue)
                {
                    m_Rebinding = action;
                    m_WaitForMouseRelease = true;
                }
            }

            GUI.EndScrollView();

            if (GUI.Button(new Rect(panel.x + 16f, panel.yMax - 54f, 180f, 36f), "Reset to defaults", m_Button)) KeyBindings.ResetToDefaults();
            if (GUI.Button(new Rect(panel.xMax - 196f, panel.yMax - 54f, 180f, 36f), "Close (Esc)", m_Button))
            {
                m_Open = false;
                InputBlock.All = false;
                m_Rebinding = null;
            }
        }

        void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, m_White);
            GUI.color = previous;
        }
    }
}
