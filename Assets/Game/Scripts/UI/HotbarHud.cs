using CTG.Combat;
using CTG.Gameplay;
using CTG.Items;
using UnityEngine;

namespace CTG.UI
{
    /// <summary>
    /// Development HUD: hotbar (top-left, slots 1-0), life and mana (top-right and
    /// under the player), death
    /// countdown, target dummy DPS and a debug readout of movement state.
    /// Drawn with IMGUI so it needs no UI assets; replace with the real UI later.
    /// </summary>
    public class HotbarHud : MonoBehaviour
    {
        const float k_Slot = 52f;
        const float k_Gap = 4f;
        const float k_Margin = 20f;

        PlayerCharacter m_Player;
        TargetDummy m_Dummy;
        GUIStyle m_Big;
        GUIStyle m_Count;
        GUIStyle m_Label;
        Texture2D m_White;

        public bool ShowDebug = true;

        public void Bind(PlayerCharacter player, TargetDummy dummy)
        {
            m_Player = player;
            m_Dummy = dummy;
        }

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
            m_Big = new GUIStyle(GUI.skin.label)
            {
                fontSize = 36, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.35f, 0.35f) },
            };
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

            DrawLife();
            DrawMana();
            DrawOverheadBars();
            DrawDummyStats();
            if (ShowDebug) DrawDebug();
        }

        void DrawLife()
        {
            Health health = m_Player.Health;
            const float width = 260f;
            var bar = new Rect(Screen.width - width - k_Margin, k_Margin + 22f, width, 20f);

            GUI.Label(new Rect(bar.x, k_Margin, width, 22f), $"Life: {health.Current}/{health.Max}", m_Label);
            DrawRect(bar, new Color(0.15f, 0.05f, 0.05f, 0.8f));
            float fill = health.Max > 0 ? health.Current / (float)health.Max : 0f;
            DrawRect(new Rect(bar.x + 2f, bar.y + 2f, (bar.width - 4f) * fill, bar.height - 4f),
                     new Color(0.85f, 0.15f, 0.2f, 0.95f));

            if (health.IsDead)
            {
                float seconds = Mathf.Ceil(health.RespawnTicksLeft / (float)Core.SimUnits.TicksPerSecond);
                GUI.Label(new Rect(0f, Screen.height * 0.35f, Screen.width, 60f), $"You died. Respawning in {seconds:0}", m_Big);
            }
        }

        void DrawMana()
        {
            Mana mana = m_Player.Mana;
            if (mana.Max <= 0) return;

            const float width = 260f;
            const float top = k_Margin + 50f;
            var bar = new Rect(Screen.width - width - k_Margin, top + 22f, width, 20f);

            GUI.Label(new Rect(bar.x, top, width, 22f), $"Mana: {mana.Current}/{mana.Max}", m_Label);
            DrawRect(bar, new Color(0.03f, 0.05f, 0.18f, 0.8f));
            float fill = mana.Max > 0 ? mana.Current / (float)mana.Max : 0f;
            DrawRect(new Rect(bar.x + 2f, bar.y + 2f, (bar.width - 4f) * fill, bar.height - 4f),
                     new Color(0.2f, 0.45f, 1f, 0.95f));
        }

        /// <summary>
        /// Small bars under the player's feet: life (green, hidden while full) and mana (blue, always shown).
        /// Each keeps its own slot so the mana bar doesn't jump when the life bar appears.
        /// </summary>
        void DrawOverheadBars()
        {
            if (m_Player.Health.IsDead) return;

            Camera cam = Camera.main;
            if (cam == null) return;

            const float halfWidthUnits = 1.25f;
            Vector3 feet = m_Player.transform.position;
            Vector3 left = cam.WorldToScreenPoint(feet + new Vector3(-halfWidthUnits, -0.25f, 0f));
            Vector3 right = cam.WorldToScreenPoint(feet + new Vector3(halfWidthUnits, -0.25f, 0f));
            float x = left.x;
            float width = right.x - left.x;
            float y = Screen.height - left.y;
            const float height = 6f;
            const float gap = 2f;

            Health health = m_Player.Health;
            if (health.Current < health.Max)
            {
                DrawSmallBar(new Rect(x, y, width, height), health.Current / (float)health.Max,
                             new Color(0.2f, 0.85f, 0.25f, 1f));
            }

            Mana mana = m_Player.Mana;
            bool hasMana = mana.Max > 0;
            if (hasMana)
            {
                DrawSmallBar(new Rect(x, y + height + gap, width, height), mana.Current / (float)mana.Max,
                             new Color(0.2f, 0.45f, 1f, 1f));
            }

            // Bow charge, below mana, only while drawing: purple while charging, pulsing gold when full.
            ChargeBow bow = m_Player.ItemUser.Bow;
            if (bow.IsCharging && bow.ChargeFraction > 0f)
            {
                Color fillColor = new Color32(170, 80, 230, 200);
                if (bow.ChargeFraction >= 1f)
                {
                    float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 6f);
                    fillColor = new Color(1f, 220f / 255f, 80f / 255f, 220f / 255f) * pulse;
                }

                // Takes the mana bar's slot when the class has no mana.
                var rect = new Rect(x, y + (height + gap) * (hasMana ? 2f : 1f), width, height);
                DrawRect(rect, new Color32(60, 40, 90, 160));
                DrawRect(new Rect(rect.x + 1f, rect.y + 1f, (rect.width - 2f) * bow.ChargeFraction, rect.height - 2f), fillColor);
            }
        }

        void DrawSmallBar(Rect rect, float fill, Color color)
        {
            DrawRect(rect, new Color(0f, 0f, 0f, 0.7f));
            DrawRect(new Rect(rect.x + 1f, rect.y + 1f, (rect.width - 2f) * Mathf.Clamp01(fill), rect.height - 2f), color);
        }

        void DrawDummyStats()
        {
            if (m_Dummy == null) return;

            Camera cam = Camera.main;
            if (cam == null) return;

            Vector3 head = cam.WorldToScreenPoint(m_Dummy.transform.position + new Vector3(0f, 3.4f, 0f));
            string text = $"DPS {m_Dummy.Dps:0.0}   total {m_Dummy.TotalDamage}";
            GUI.Label(new Rect(head.x - 90f, Screen.height - head.y - 20f, 180f, 22f), text, m_Label);
        }

        void DrawDebug()
        {
            PlayerMotor motor = m_Player.Motor;
            string text =
                $"vel {motor.Velocity.x:0.00}, {motor.Velocity.y:0.00} px/tick   grounded {motor.Grounded}\n" +
                $"tile {Mathf.FloorToInt(motor.Position.x / 16f)}, {Mathf.FloorToInt(motor.Position.y / 16f)}   " +
                $"cursor {m_Player.CursorTile.x}, {m_Player.CursorTile.y}\n" +
                "A/D move · Space jump · LMB use · RMB alt use · 1-0 / wheel hotbar · K take a hit · L die";
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
