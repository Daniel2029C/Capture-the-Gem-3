using CTG.Core;
using CTG.Input;
using CTG.Items;
using CTG.Rendering;
using CTG.World;
using UnityEngine;

namespace CTG.Gameplay
{
    /// <summary>
    /// The local player: owns the Terraria motor, inventory and item use, steps them on the simulation
    /// clock, and draws the result interpolated between ticks. Order per tick follows Terraria's
    /// Player.Update: hotbar selection and item use, then movement.
    /// </summary>
    public class PlayerCharacter : MonoBehaviour, ITickable
    {
        // The sprite is 3 tiles (48 px) tall. The hitbox is 2 px shorter so 3-tile-tall tunnels can be
        // entered mid-air, the way Terraria's 42 px hitbox fits its 48 px gaps with room to spare.
        public const int HitboxWidthPx = 22;
        public const int HitboxHeightPx = 46;
        const int k_SpriteWidthPx = 24;
        const int k_SpriteHeightPx = 48;

        [SerializeField] MovementStats movement = MovementStats.TerrariaDefault;

        PlayerInput m_Input;
        SpriteRenderer m_Body;
        Transform m_HeldItemPivot;
        SpriteRenderer m_HeldItem;

        public TerrariaMotor Motor { get; private set; }
        public Inventory Inventory { get; private set; }
        public ItemUser ItemUser { get; private set; }
        public InputFrame LastInput { get; private set; }
        public Vector2Int CursorTile { get; private set; }

        public void Initialize(TileWorld world, TileEditor tileEditor, PlayerInput input, Vector2 spawnPx)
        {
            m_Input = input;
            Motor = new TerrariaMotor(world, HitboxWidthPx, HitboxHeightPx) { Stats = movement };
            Motor.Teleport(spawnPx);
            Inventory = new Inventory();
            ItemUser = new ItemUser(Inventory, tileEditor);

            BuildVisuals();
            SimulationClock.Instance.Register(this);
        }

        void OnDestroy()
        {
            if (SimulationClock.Instance != null) SimulationClock.Instance.Unregister(this);
        }

        void OnValidate()
        {
            if (Motor != null) Motor.Stats = movement;
        }

        void BuildVisuals()
        {
            m_Body = gameObject.AddComponent<SpriteRenderer>();
            m_Body.sprite = PlaceholderSprites.Player(k_SpriteWidthPx, k_SpriteHeightPx);
            m_Body.sortingOrder = 10;

            var pivot = new GameObject("Held Item");
            m_HeldItemPivot = pivot.transform;
            m_HeldItemPivot.SetParent(transform, false);
            m_HeldItemPivot.localPosition = new Vector3(0f, 1.6f, 0f);

            var item = new GameObject("Sprite");
            item.transform.SetParent(m_HeldItemPivot, false);
            m_HeldItem = item.AddComponent<SpriteRenderer>();
            m_HeldItem.sortingOrder = 11;
            m_HeldItem.enabled = false;
        }

        public void Tick(int tick)
        {
            InputFrame input = m_Input.Consume();
            LastInput = input;
            CursorTile = TileWorld.WorldToTile(input.CursorWorld);

            if (ItemUser.CanSwitchItems)
            {
                if (input.HotbarKey >= 0) Inventory.Select(input.HotbarKey);
                // Scrolling down moves right along the hotbar, as in Terraria.
                if (input.HotbarScroll != 0) Inventory.Select(Inventory.SelectedSlot - input.HotbarScroll);
            }

            ItemUser.Tick(tick, input.UseItem, CursorTile, Motor);

            // While swinging, Terraria turns the player toward the cursor.
            if (ItemUser.IsUsing)
            {
                Motor.Direction = input.CursorWorld.x * TerrariaUnits.PixelsPerTile >= Motor.Center.x ? 1 : -1;
            }

            Motor.Tick(input.Left, input.Right, input.Jump);
        }

        void LateUpdate()
        {
            if (Motor == null) return;

            float alpha = SimulationClock.Instance != null ? SimulationClock.Instance.Alpha : 1f;
            Vector2 px = Vector2.Lerp(Motor.PreviousPosition, Motor.Position, alpha);
            transform.position = new Vector3((px.x + Motor.Width * 0.5f) / TerrariaUnits.PixelsPerTile,
                                             px.y / TerrariaUnits.PixelsPerTile, 0f);

            m_Body.flipX = Motor.Direction < 0;
            UpdateHeldItem(alpha);
        }

        void UpdateHeldItem(float alpha)
        {
            ItemDef def = ItemDefs.Get(ItemUser.UsingItem);
            bool swinging = ItemUser.IsUsing && def.UseStyle == UseStyle.Swing;
            m_HeldItem.enabled = swinging;
            if (!swinging) return;

            m_HeldItem.sprite = def.IsPickaxe
                ? PlaceholderSprites.Pickaxe(def.IconColor)
                : PlaceholderSprites.Square($"icon_{def.Id}", def.IconColor, 8);

            // Terraria's swing sweeps from overhead to the front over the use animation.
            float progress = 1f - (ItemUser.ItemAnimation - alpha) / Mathf.Max(1, ItemUser.ItemAnimationMax);
            // The sprite points up-right at 0°, so +45° is straight up and -100° is down in front.
            float angle = Mathf.Lerp(45f, -100f, Mathf.Clamp01(progress));
            int direction = Motor.Direction;
            m_HeldItemPivot.localScale = new Vector3(direction, 1f, 1f);
            m_HeldItemPivot.localRotation = Quaternion.Euler(0f, 0f, angle * direction);
        }
    }
}
