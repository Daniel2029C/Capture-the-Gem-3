using CTG.Economy;
using CTG.Gameplay;
using CTG.Input;
using UnityEngine;

namespace CTG.UI
{
    /// <summary>
    /// The shop panel (IMGUI placeholder): opens with the shop key at base, lists what can be bought with its price,
    /// and shows what the player has already bought. Click an item to buy it; the shop closes on leaving base or
    /// pressing the shop key again.
    /// </summary>
    public sealed class ShopUi : MonoBehaviour
    {
        PlayerCharacter m_Player;
        GUIStyle m_Title, m_Label, m_Small, m_Button;
        Texture2D m_White;
        string m_Message;
        float m_MessageUntil;

        public static ShopUi Create(Transform parent, PlayerCharacter player)
        {
            var go = new GameObject("Shop UI");
            go.transform.SetParent(parent, false);
            var ui = go.AddComponent<ShopUi>();
            ui.m_Player = player;
            return ui;
        }

        void EnsureStyles()
        {
            if (m_White != null) return;

            m_White = Texture2D.whiteTexture;
            m_Title = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            m_Label = new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = Color.white }, wordWrap = true };
            m_Small = new GUIStyle(m_Label) { fontSize = 13, normal = { textColor = new Color(0.8f, 0.85f, 1f) } };
            m_Button = new GUIStyle(GUI.skin.button) { fontSize = 15, alignment = TextAnchor.MiddleLeft, wordWrap = true };
        }

        void OnGUI()
        {
            if (m_Player == null || !m_Player.ShopOpen) return;

            EnsureStyles();

            const float width = 640f, height = 460f;
            var panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f - 40f, width, height);
            Fill(panel, new Color(0.05f, 0.08f, 0.18f, 0.94f));
            Fill(new Rect(panel.x + 3f, panel.y + 3f, panel.width - 6f, panel.height - 6f), new Color(0.12f, 0.18f, 0.38f, 0.94f));

            GUI.Label(new Rect(panel.x + 16f, panel.y + 8f, 300f, 32f), "Shop", m_Title);
            GUI.Label(new Rect(panel.xMax - 270f, panel.y + 12f, 254f, 28f), $"Shards: {m_Player.Progress.Shards}", m_Title);

            float y = panel.y + 52f;
            var items = ShopCatalog.All;
            for (int i = 0; i < items.Count; i++)
            {
                ShopItem item = items[i];
                bool can = Shop.CanBuy(m_Player, item, out string reason);
                var row = new Rect(panel.x + 16f, y, panel.width - 32f, 56f);

                GUI.enabled = can;
                string label = $"{item.Name}  -  {item.Cost} shards\n{item.Description}";
                if (GUI.Button(row, label, m_Button))
                {
                    if (Shop.TryBuy(m_Player, item)) Say($"Bought {item.Name}");
                }

                GUI.enabled = true;
                if (!can) GUI.Label(new Rect(row.xMax - 150f, row.y + 4f, 146f, 20f), reason, m_Small);
                y += 62f;
            }

            // What has been bought so far.
            GUI.Label(new Rect(panel.x + 16f, y + 2f, panel.width - 32f, 24f), "Owned", m_Title);
            y += 34f;
            Purchases purchases = m_Player.Purchases;
            string owned = "";
            foreach (ArtifactId artifact in purchases.Artifacts) owned += $"{ShopCatalog.ArtifactName(artifact)}   ";
            if (owned.Length == 0) owned = "No artifacts yet.   ";
            GUI.Label(new Rect(panel.x + 16f, y, panel.width - 32f, 22f), owned, m_Label);
            y += 24f;

            string spells = "";
            for (int slot = 0; slot < SpellBook.SlotCount; slot++)
            {
                SpellId? spell = purchases.Spells.SlotSpell(slot);
                spells += spell.HasValue ? $"[{KeyBindings.Label(BindAction.Spell1 + slot)}] {ShopCatalog.SpellName(spell.Value)} x{purchases.Spells.Uses(slot)}   " : $"[{KeyBindings.Label(BindAction.Spell1 + slot)}] empty   ";
            }

            GUI.Label(new Rect(panel.x + 16f, y, panel.width - 32f, 22f), spells, m_Label);

            if (Time.unscaledTime < m_MessageUntil)
            {
                GUI.Label(new Rect(panel.x + 16f, panel.yMax - 30f, panel.width - 32f, 22f), m_Message, m_Small);
            }
            else
            {
                GUI.Label(new Rect(panel.x + 16f, panel.yMax - 30f, panel.width - 32f, 22f), $"Press {KeyBindings.Label(BindAction.Shop)} to close. Only available at base.", m_Small);
            }
        }

        void Say(string text)
        {
            m_Message = text;
            m_MessageUntil = Time.unscaledTime + 2f;
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
