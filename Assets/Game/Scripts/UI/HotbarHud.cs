using CTG.Combat;
using CTG.Gameplay;
using CTG.Items;
using CTG.Rendering;
using UnityEngine;

namespace CTG.UI
{
    /// <summary>
    /// Development HUD: hotbar and healing potion (bottom centre), life and mana (top-right and
    /// under the player), death countdown, target dummy DPS and a debug readout (top-left).
    /// Drawn with IMGUI so it needs no UI assets; replace with the real UI later.
    /// </summary>
    public class HotbarHud : MonoBehaviour
    {
        const float k_Slot = 52f;
        const float k_Gap = 4f;
        const float k_Margin = 20f;
        const float k_PotionSize = 56f;
        const float k_NameHeight = 22f;

        // Only the slots the class actually uses are shown; the bar stays centred as it grows or shrinks.
        int m_Visible = 1;
        float HotbarWidth => m_Visible * k_Slot + (m_Visible - 1) * k_Gap;
        float HotbarLeft => (Screen.width - HotbarWidth) * 0.5f;
        static float HotbarTop => Screen.height - k_Margin - k_Slot;

        PlayerCharacter m_Player;
        TargetDummy m_Dummy;
        GUIStyle m_Big;
        GUIStyle m_Count;
        GUIStyle m_Label;
        GUIStyle m_Big2;
        GUIStyle m_NameLabel;
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
            m_NameLabel = new GUIStyle(m_Label) { alignment = TextAnchor.LowerCenter };
            m_Big2 = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white },
            };
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

            m_Visible = inventory.VisibleSlots;
            for (int i = 0; i < m_Visible; i++)
            {
                var rect = new Rect(HotbarLeft + i * (k_Slot + k_Gap), HotbarTop, k_Slot, k_Slot);
                bool selected = i == inventory.SelectedSlot;

                DrawRect(rect, selected ? new Color(1f, 0.85f, 0.3f, 0.9f) : new Color(0.1f, 0.15f, 0.35f, 0.75f));
                DrawRect(Inset(rect, 3f), new Color(0.2f, 0.3f, 0.6f, 0.85f));

                ItemStack stack = inventory[i];
                if (!stack.IsEmpty)
                {
                    DrawRect(Inset(rect, 14f), stack.Def.IconColor);
                    if (stack.Count > 1) GUI.Label(Inset(rect, 4f), stack.Count.ToString(), m_Count);
                }

                GUI.Label(new Rect(rect.x + 4f, rect.y + 1f, 40f, 18f), Input.KeyBindings.Label(Input.BindAction.Hotbar1 + i), m_Label);
            }

            ItemStack held = inventory.Selected;
            GUI.Label(new Rect(HotbarLeft, HotbarTop - k_NameHeight - 2f, HotbarWidth, k_NameHeight),
                      held.IsEmpty ? "" : held.Def.Name, m_NameLabel);

            DrawLife();
            DrawMana();
            DrawBuffs();
            DrawOverheadBars();
            DrawSpells();
            DrawLevelBar();
            DrawChannelBar();
            DrawAbilities();
            DrawDummyStats();
            if (ShowDebug) DrawDebug();
        }

        /// <summary>The two spell slots at the bottom centre: icon, uses left, key and cooldown.</summary>
        void DrawSpells()
        {
            const float size = k_PotionSize;
            float gap = 8f;
            float left = (Screen.width - (size * Economy.SpellBook.SlotCount + gap)) * 0.5f;
            float top = HotbarTop - k_NameHeight - 6f - size;

            for (int slot = 0; slot < Economy.SpellBook.SlotCount; slot++)
            {
                var rect = new Rect(left + slot * (size + gap), top, size, size);
                DrawRect(rect, new Color(0.1f, 0.15f, 0.35f, 0.75f));
                DrawRect(Inset(rect, 3f), new Color(0.2f, 0.3f, 0.6f, 0.85f));

                Economy.SpellId? spell = m_Player.Purchases.Spells.SlotSpell(slot);
                if (spell.HasValue)
                {
                    bool ready = m_Player.Purchases.Spells.IsReady(spell.Value);
                    Color previous = GUI.color;
                    GUI.color = ready ? Color.white : new Color(0.45f, 0.45f, 0.45f, 0.8f);
                    if (spell.Value == Economy.SpellId.HealingPotion)
                    {
                        GUI.DrawTexture(Inset(rect, 8f), PlaceholderSprites.Potion().texture, ScaleMode.ScaleToFit);
                    }
                    else
                    {
                        DrawRect(Inset(rect, 14f), new Color(0.95f, 0.8f, 0.3f, 1f));
                    }

                    GUI.color = previous;
                    GUI.Label(Inset(rect, 4f), m_Player.Purchases.Spells.Uses(slot).ToString(), m_Count);
                    if (!ready)
                    {
                        int seconds = Mathf.CeilToInt(m_Player.Purchases.Spells.Cooldown(spell.Value) / (float)Core.SimUnits.TicksPerSecond);
                        GUI.Label(rect, seconds.ToString(), m_Big2);
                    }
                }

                GUI.Label(new Rect(rect.x + 4f, rect.y + 1f, size, 18f), Input.KeyBindings.Label(Input.BindAction.Spell1 + slot), m_Label);
            }
        }

        /// <summary>A progress bar above the player while cashing in an offering at an altar.</summary>
        void DrawChannelBar()
        {
            Camera cam = Camera.main;
            if (cam == null || m_Player.Health.IsDead) return;

            foreach (Economy.Altar altar in Economy.Altar.All)
            {
                float progress = altar.ProgressFor(m_Player);
                if (progress <= 0f) continue;

                Vector3 head = cam.WorldToScreenPoint(m_Player.transform.position + new Vector3(0f, 3.8f, 0f));
                var rect = new Rect(head.x - 40f, Screen.height - head.y - 6f, 80f, 10f);
                DrawRect(rect, new Color(0f, 0f, 0f, 0.7f));
                DrawRect(new Rect(rect.x + 1f, rect.y + 1f, (rect.width - 2f) * progress, rect.height - 2f), new Color(1f, 0.85f, 0.3f, 0.95f));
                GUI.Label(new Rect(rect.x - 20f, rect.y - 22f, 120f, 20f), "Cashing in...", m_Label);
                break;
            }
        }

        /// <summary>Level, experience and shards along the bottom edge of the screen.</summary>
        void DrawLevelBar()
        {
            Economy.PlayerProgress progress = m_Player.Progress;
            const float height = 16f;
            var bar = new Rect(0f, Screen.height - height, Screen.width, height);
            DrawRect(bar, new Color(0f, 0f, 0f, 0.7f));
            float fill = progress.ExpIntoLevel / (float)Economy.PlayerProgress.ExpPerLevel;
            DrawRect(new Rect(bar.x, bar.y + 2f, bar.width * Mathf.Clamp01(fill), bar.height - 4f), new Color(0.55f, 0.35f, 0.95f, 0.95f));

            string exp = progress.AtMaxLevel ? "MAX" : $"{progress.ExpIntoLevel} / {Economy.PlayerProgress.ExpPerLevel} xp";
            GUI.Label(new Rect(8f, bar.y - 24f, 600f, 22f), $"Level {progress.Level}   {exp}   Shards {progress.Shards}   Offerings {m_Player.Offerings}", m_Label);
        }

        /// <summary>Ability slots in a row to the right of the potion: key, name, status and a fill bar.</summary>
        void DrawAbilities()
        {
            const float width = 96f, height = 56f, gap = 6f;
            float left = (Screen.width + k_PotionSize) * 0.5f + 12f;
            float top = HotbarTop - k_NameHeight - 6f - height;

            for (int i = 0; i < AbilitySet.SlotCount; i++)
            {
                Ability ability = m_Player.Abilities[i];
                if (ability == null) continue;

                var rect = new Rect(left, top, width, height);
                left += width + gap;

                DrawRect(rect, ability.IsActive ? new Color(0.55f, 0.45f, 0.15f, 0.85f) : new Color(0.1f, 0.15f, 0.35f, 0.75f));
                DrawRect(Inset(rect, 3f), new Color(0.2f, 0.3f, 0.6f, 0.85f));

                GUI.Label(new Rect(rect.x + 6f, rect.y + 2f, rect.width - 8f, 18f), $"{AbilityKeys.Labels[i]} · {ability.Name}", m_Label);
                GUI.Label(new Rect(rect.x + 6f, rect.y + 20f, rect.width - 8f, 18f), ability.Status, m_Label);

                var bar = new Rect(rect.x + 6f, rect.yMax - 12f, rect.width - 12f, 6f);
                DrawRect(bar, new Color(0f, 0f, 0f, 0.5f));
                DrawRect(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(ability.Fill), bar.height),
                         ability.Fill >= 1f ? new Color(0.5f, 0.95f, 0.5f, 0.95f) : new Color(0.95f, 0.8f, 0.3f, 0.95f));
            }
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

        /// <summary>Active debuffs under the bars, with the seconds left.</summary>
        void DrawBuffs()
        {
            Health health = m_Player.Health;
            float top = k_Margin + 50f + (m_Player.Mana.Max > 0 ? 50f : 0f);
            const float lineHeight = 24f;

            if (health.IsBurning)
            {
                int seconds = Mathf.CeilToInt(health.BurnTicksLeft / (float)Core.SimUnits.TicksPerSecond);
                GUI.Label(new Rect(Screen.width - 260f - k_Margin, top, 260f, 22f), $"On Fire!  {seconds}s", m_Label);
                top += lineHeight;
            }

            if (m_Player.ApexBuffTicksLeft > 0)
            {
                int seconds = Mathf.CeilToInt(m_Player.ApexBuffTicksLeft / (float)Core.SimUnits.TicksPerSecond);
                GUI.Label(new Rect(Screen.width - 260f - k_Margin, top, 260f, 22f), $"Apex buff  {seconds / 60}:{seconds % 60:00}", m_Label);
                top += lineHeight;
            }

            if (health.HasRegenBuff)
            {
                int seconds = Mathf.CeilToInt(health.RegenBuffTicksLeft / (float)Core.SimUnits.TicksPerSecond);
                GUI.Label(new Rect(Screen.width - 260f - k_Margin, top, 260f, 22f), $"Regeneration  {seconds}s", m_Label);
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
                $"{Input.KeyBindings.Label(Input.BindAction.MoveLeft)}/{Input.KeyBindings.Label(Input.BindAction.MoveRight)} move · {Input.KeyBindings.Label(Input.BindAction.Jump)} jump/unhook · " +
                $"{Input.KeyBindings.Label(Input.BindAction.Ability1)}/{Input.KeyBindings.Label(Input.BindAction.Ability2)}/{Input.KeyBindings.Label(Input.BindAction.Ability3)} abilities · " +
                $"{Input.KeyBindings.Label(Input.BindAction.Spell1)}/{Input.KeyBindings.Label(Input.BindAction.Spell2)} spells · {Input.KeyBindings.Label(Input.BindAction.Shop)} shop (at base) · Esc menu / keybinds · wheel hotbar";
            GUI.Label(new Rect(k_Margin, k_Margin, 700f, 66f), text, m_Label);
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
