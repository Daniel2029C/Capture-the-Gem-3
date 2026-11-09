using CTG.Core;
using CTG.Items;
using CTG.World;
using UnityEngine;

namespace CTG.Gameplay
{
    /// <summary>
    /// Terraria's item-use timing (Player.ItemCheck), reduced to what tools and blocks need:
    /// a use lasts useAnimation ticks; within it an action (tile hit or block placement) can fire whenever
    /// itemTime has run out, after which itemTime is reset to useTime. autoReuse items start a new use
    /// immediately while the button stays held. The hotbar can't be changed mid-use, as in Terraria.
    /// </summary>
    public class ItemUser
    {
        // Terraria Player.tileRangeX / tileRangeY: reach beyond the hitbox, in tiles.
        public const int TileRangeX = 5;
        public const int TileRangeY = 4;

        readonly Inventory m_Inventory;
        readonly TileEditor m_TileEditor;

        /// <summary>Ticks left in the current use (Terraria itemAnimation).</summary>
        public int ItemAnimation { get; private set; }
        /// <summary>Length of the current use, for animating the swing.</summary>
        public int ItemAnimationMax { get; private set; }
        /// <summary>Ticks until the next action is allowed (Terraria itemTime).</summary>
        public int ItemTime { get; private set; }
        public ItemId UsingItem { get; private set; }
        public bool IsUsing => ItemAnimation > 0;

        /// <summary>Extra reach from accessories (e.g. Toolbox +1), added to the item's tileBoost.</summary>
        public int BonusTileReach;

        bool m_ReleasedUse = true;

        public ItemUser(Inventory inventory, TileEditor tileEditor)
        {
            m_Inventory = inventory;
            m_TileEditor = tileEditor;
        }

        public void Tick(int tick, bool useHeld, Vector2Int cursorTile, TerrariaMotor motor)
        {
            if (ItemAnimation > 0) ItemAnimation--;
            if (ItemTime > 0) ItemTime--;

            ItemStack held = m_Inventory.Selected;
            ItemDef def = held.Def;

            if (ItemAnimation == 0 && useHeld && !held.IsEmpty && def.UseStyle != UseStyle.None)
            {
                if (def.AutoReuse || m_ReleasedUse)
                {
                    ItemAnimation = def.UseAnimation;
                    ItemAnimationMax = def.UseAnimation;
                    ItemTime = 0;
                    UsingItem = held.Id;
                }
            }

            m_ReleasedUse = !useHeld;

            if (ItemAnimation <= 0 || ItemTime > 0 || !useHeld) return;
            if (held.Id != UsingItem || held.IsEmpty) return;

            if (!InTileRange(motor, cursorTile, def.TileBoost + BonusTileReach)) return;

            if (def.IsPickaxe)
            {
                if (m_TileEditor.CanMine(cursorTile))
                {
                    m_TileEditor.Hit(cursorTile, def.PickPower, tick, m_Inventory);
                    ItemTime = def.UseTime;
                }
            }
            else if (def.PlacesTile)
            {
                Rect hitbox = new Rect(motor.Position.x, motor.Position.y, motor.Width, motor.Height);
                if (m_TileEditor.Place(cursorTile, def.CreateTile, hitbox))
                {
                    if (def.Consumable) m_Inventory.ConsumeFromSlot(m_Inventory.SelectedSlot);
                    ItemTime = def.UseTime;
                }
            }
        }

        /// <summary>
        /// Terraria's tool reach test, converted to y-up: tileRangeX tiles beyond either side of the hitbox,
        /// tileRangeY tiles above the head, and one tile less below the feet (Terraria's "- 2" on the
        /// bottom bound).
        /// </summary>
        public static bool InTileRange(TerrariaMotor motor, Vector2Int tile, int tileBoost)
        {
            float left = motor.Position.x / TerrariaUnits.PixelsPerTile;
            float right = (motor.Position.x + motor.Width) / TerrariaUnits.PixelsPerTile;
            float bottom = motor.Position.y / TerrariaUnits.PixelsPerTile;
            float top = (motor.Position.y + motor.Height) / TerrariaUnits.PixelsPerTile;

            int rangeX = TileRangeX + tileBoost;
            int rangeY = TileRangeY + tileBoost;

            return left - rangeX <= tile.x &&
                   right + rangeX - 1 >= tile.x &&
                   top + rangeY - 1 >= tile.y &&
                   bottom - rangeY + 1 <= tile.y;
        }

        public bool CanSwitchItems => !IsUsing;
    }
}
