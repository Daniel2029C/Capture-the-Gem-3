using CTG.Gameplay;
using CTG.Items;
using UnityEngine;

namespace CTG.UI
{
    /// <summary>
    /// Development HUD: Terraria-style hotbar (top-left, slots 1-0) and a debug readout of movement state.
    /// Drawn with IMGUI so it needs no UI assets; replace with the real UI later.
    /// </summary>
    public class HotbarHud : MonoBehaviour
    {
        const float k_Slot = 52f;
        const float k_Gap = 4f;
        const float k_Margin = 20f;

        PlayerCharacter m_Player;
        GUIStyle m_Count;
        GUIStyle m_Label;
        Texture2D m_White;

        public bool ShowDebug = true;

        public void Bind(PlayerCharacter player) => m_Player = player;

        void EnsureStyles()
        {
            if (m_White != null) return;

            m_White = Texture2D.whiteTexture;
            m_Count = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.LowerRight, fontSize = 14, fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
            };
            m_Label = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = Color.white } };
        }

        void OnGUI()
        {
            if (m_Player == null || m_Player.Inventory == null) return;

            EnsureStyles();
            Inventory inventory = m_Player.Inventory;

            for (int i = 0; i < Inventory.HotbarSize; i++)
            {
                var rect = new Rect(k_Margin + i * (k_Slot + k_Gap), k_Margin, k_Slot, k_Slot);
                bool selected = i == inventory.SelectedSlot;

                DrawRect(rect, selected ? new Color(1f, 0.85f, 0.3f, 0.9f) : new Color(0.1f, 0.15f, 0.35f, 0.75f));
                DrawRect(Inset(rect, 3f), new Color(0.2f, 0.3f, 0.6f, 0.85f));

                ItemStack stack = inventory[i];
                if (!stack.IsEmpty)
                {
                    DrawRect(Inset(rect, 14f), stack.Def.IconColor);
                    if (stack.Count > 1) GUI.Label(Inset(rect, 4f), stack.Count.ToString(), m_Count);
                }

                GUI.Label(new Rect(rect.x + 4f, rect.y + 1f, 20f, 18f), ((i + 1) % 10).ToString(), m_Label);
            }

            ItemStack held = inventory.Selected;
            GUI.Label(new Rect(k_Margin, k_Margin + k_Slot + 4f, 400f, 22f), held.IsEmpty ? "" : held.Def.Name, m_Label);

            if (ShowDebug) DrawDebug();
        }

        void DrawDebug()
        {
            TerrariaMotor motor = m_Player.Motor;
            string text =
                $"vel {motor.Velocity.x:0.00}, {motor.Velocity.y:0.00} px/tick   grounded {motor.Grounded}\n" +
                $"tile {Mathf.FloorToInt(motor.Position.x / 16f)}, {Mathf.FloorToInt(motor.Position.y / 16f)}   " +
                $"cursor {m_Player.CursorTile.x}, {m_Player.CursorTile.y}\n" +
                "A/D move · Space jump · LMB use · 1-0 / wheel hotbar";
            GUI.Label(new Rect(k_Margin, Screen.height - 70f, 700f, 66f), text, m_Label);
        }

        static Rect Inset(Rect r, float by) => new(r.x + by, r.y + by, r.width - by * 2f, r.height - by * 2f);

        void DrawRect(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, m_White);
            GUI.color = previous;
        }
    }
}
