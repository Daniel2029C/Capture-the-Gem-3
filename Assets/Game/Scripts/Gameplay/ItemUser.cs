using System.Collections.Generic;
using CTG.Combat;
using CTG.Core;
using CTG.Items;
using CTG.World;
using UnityEngine;

namespace CTG.Gameplay
{
    /// <summary>
    /// Item-use timing, reduced to what tools and blocks need:
    /// a use lasts useAnimation ticks; within it an action (tile hit or block placement) can fire whenever
    /// itemTime has run out, after which itemTime is reset to useTime. autoReuse items start a new use
    /// immediately while the button stays held. The hotbar can't be changed mid-use.
    /// Damaging items also hit with the swung item for the whole use, each target at most once per use,
    /// and the swing direction is locked toward the cursor when the use starts.
    /// Charge bows bypass this timing and run their own draw state (<see cref="ChargeBow"/>).
    /// </summary>
    public class ItemUser
    {
        // Tool reach beyond the hitbox, in tiles.
        public const int TileRangeX = 5;
        public const int TileRangeY = 4;

        readonly Inventory m_Inventory;
        readonly TileEditor m_TileEditor;
        readonly IDamageable m_Owner;
        readonly HashSet<IDamageable> m_HitThisUse = new();

        /// <summary>Ticks left in the current use.</summary>
        public int ItemAnimation { get; private set; }
        /// <summary>Length of the current use, for animating the swing.</summary>
        public int ItemAnimationMax { get; private set; }
        /// <summary>Ticks until the next action is allowed.</summary>
        public int ItemTime { get; private set; }
        public ItemId UsingItem { get; private set; }
        public bool IsUsing => ItemAnimation > 0 || Bow.Active;
        public ChargeBow Bow { get; }
        /// <summary>Facing locked for the current use: +1 right, -1 left.</summary>
        public int UseDirection { get; private set; } = 1;

        /// <summary>Extra reach from accessories (e.g. Toolbox +1), added to the item's tileBoost.</summary>
        public int BonusTileReach;

        bool m_ReleasedUse = true;

        public ItemUser(Inventory inventory, TileEditor tileEditor, IDamageable owner)
        {
            m_Inventory = inventory;
            m_TileEditor = tileEditor;
            m_Owner = owner;
            Bow = new ChargeBow(owner);
        }

        /// <summary>Stops any use in progress (death, stun).</summary>
        public void Cancel()
        {
            ItemAnimation = 0;
            ItemTime = 0;
            m_HitThisUse.Clear();
            Bow.Cancel();
        }

        /// <param name="useHeld">Primary use button (left click).</param>
        /// <param name="altHeld">Secondary use button (right click).</param>
        public void Tick(int tick, bool useHeld, bool altHeld, Vector2Int cursorTile, Vector2 cursorPx, PlayerMotor motor)
        {
            if (ItemAnimation > 0) ItemAnimation--;
            if (ItemTime > 0) ItemTime--;

            ItemStack held = m_Inventory.Selected;
            ItemDef def = held.Def;

            if (!held.IsEmpty && def.UseStyle == UseStyle.ChargeBow)
            {
                Bow.Tick(tick, def, useHeld, altHeld, cursorPx, motor);
                if (Bow.Active)
                {
                    UsingItem = held.Id;
                    UseDirection = Bow.Direction;
                }

                m_ReleasedUse = !useHeld;
                return;
            }

            if (ItemAnimation == 0 && useHeld && !held.IsEmpty && def.UseStyle != UseStyle.None)
            {
                if (def.AutoReuse || m_ReleasedUse)
                {
                    ItemAnimation = def.UseAnimation;
                    ItemAnimationMax = def.UseAnimation;
                    ItemTime = 0;
                    UsingItem = held.Id;
                    UseDirection = cursorPx.x >= motor.Center.x ? 1 : -1;
                    m_HitThisUse.Clear();
                }
            }

            m_ReleasedUse = !useHeld;

            if (ItemAnimation > 0) TickMelee(motor);

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

        void TickMelee(PlayerMotor motor)
        {
            ItemDef def = ItemDefs.Get(UsingItem);
            if (!def.DealsDamage || def.UseStyle != UseStyle.Swing) return;

            var ownerBox = new Rect(motor.Position.x, motor.Position.y, motor.Width, motor.Height);
            float angle = MeleeSwing.Angle(ItemAnimation, ItemAnimationMax);
            Rect swing = MeleeSwing.Hitbox(ownerBox, UseDirection, angle, def.SwingLengthPx);

            IReadOnlyList<IDamageable> targets = DamageableRegistry.All;
            for (int i = 0; i < targets.Count; i++)
            {
                IDamageable target = targets[i];
                if (!target.CanBeHit || m_HitThisUse.Contains(target)) continue;
                if (!DamageableRegistry.AreHostile(m_Owner, target)) continue;
                if (!swing.Overlaps(target.HitboxPx)) continue;

                m_HitThisUse.Add(target);
                target.ApplyHit(new HitInfo
                {
                    Damage = def.Damage, Knockback = def.Knockback, Direction = UseDirection, Source = m_Owner,
                });
            }
        }

        /// <summary>
        /// Tool reach test (y-up): tileRangeX tiles beyond either side of the hitbox,
        /// tileRangeY tiles above the head, and one tile less below the feet.
        /// </summary>
        public static bool InTileRange(PlayerMotor motor, Vector2Int tile, int tileBoost)
        {
            float left = motor.Position.x / SimUnits.PixelsPerTile;
            float right = (motor.Position.x + motor.Width) / SimUnits.PixelsPerTile;
            float bottom = motor.Position.y / SimUnits.PixelsPerTile;
            float top = (motor.Position.y + motor.Height) / SimUnits.PixelsPerTile;

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
