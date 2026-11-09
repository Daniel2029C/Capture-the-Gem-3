using CTG.Combat;
using CTG.Core;
using CTG.Input;
using CTG.Items;
using CTG.Rendering;
using CTG.UI;
using CTG.World;
using UnityEngine;

namespace CTG.Gameplay
{
    /// <summary>
    /// The local player: owns the motor, health, inventory and item use, steps them on the simulation
    /// clock, and draws the result interpolated between ticks. Order per tick: hotbar selection and item
    /// use (including melee hits), then movement, then health (regeneration, immunity, respawn) and mana.
    /// </summary>
    public class PlayerCharacter : MonoBehaviour, ITickable, IDamageable
    {
        // The sprite is 3 tiles (48 px) tall. The hitbox is 2 px shorter so 3-tile-tall tunnels can be
        // entered mid-air with a little room to spare.
        public const int HitboxWidthPx = 22;
        public const int HitboxHeightPx = 46;
        const int k_SpriteWidthPx = 24;
        const int k_SpriteHeightPx = 48;

        [SerializeField] MovementStats movement = MovementStats.Default;

        [Header("Life")]
        [SerializeField] int maxLife = 100;
        [SerializeField] float regenPerSecond = 1f;
        [Tooltip("Ticks of immunity after being hit, so overlapping attacks can't all land in the same instant.")]
        [SerializeField] int hitImmunityTicks = 8;
        [Tooltip("Seconds from death to respawn. Will be driven by class and game state later.")]
        [SerializeField] float respawnSeconds = 3f;

        [Header("Mana")]
        [SerializeField] int maxMana = 20;
        [SerializeField] float manaRegenPerSecond = 1f;

        [Header("Testing")]
        [SerializeField] int devHurtDamage = 25;
        [SerializeField] float devHurtKnockback = 1f;

        PlayerInput m_Input;
        Vector2 m_SpawnPx;
        SpriteRenderer m_Body;
        Transform m_HeldItemPivot;
        SpriteRenderer m_HeldItem;

        public PlayerMotor Motor { get; private set; }
        public Health Health { get; private set; }
        public Mana Mana { get; private set; }
        public Inventory Inventory { get; private set; }
        public ItemUser ItemUser { get; private set; }
        public InputFrame LastInput { get; private set; }
        public Vector2Int CursorTile { get; private set; }
        public Team Team { get; set; } = Team.Red;
        public int Defense { get; set; }

        public Rect HitboxPx => new(Motor.Position.x, Motor.Position.y, Motor.Width, Motor.Height);
        public bool CanBeHit => Health != null && !Health.IsDead && !Health.IsImmune;

        public void Initialize(TileWorld world, TileEditor tileEditor, PlayerInput input, Vector2 spawnPx)
        {
            m_Input = input;
            m_SpawnPx = spawnPx;
            Motor = new PlayerMotor(world, HitboxWidthPx, HitboxHeightPx) { Stats = movement };
            Motor.Teleport(spawnPx);
            Health = new Health(maxLife, regenPerSecond, hitImmunityTicks,
                                SimUnits.SecondsToTicks(respawnSeconds));
            Mana = new Mana(maxMana, manaRegenPerSecond);
            Inventory = new Inventory();
            ItemUser = new ItemUser(Inventory, tileEditor, this);

            BuildVisuals();
            SimulationClock.Instance.Register(this);
            DamageableRegistry.Register(this);
        }

        void OnDestroy()
        {
            if (SimulationClock.Instance != null) SimulationClock.Instance.Unregister(this);
            DamageableRegistry.Unregister(this);
        }

        void OnValidate()
        {
            if (Motor != null) Motor.Stats = movement;
            if (Health != null)
            {
                Health.Max = maxLife;
                Health.RegenPerSecond = regenPerSecond;
                Health.HitImmunityTicks = hitImmunityTicks;
                Health.RespawnTicks = SimUnits.SecondsToTicks(respawnSeconds);
            }

            if (Mana != null)
            {
                Mana.Max = maxMana;
                Mana.RegenPerSecond = manaRegenPerSecond;
            }
        }

        void BuildVisuals()
        {
            m_Body = gameObject.AddComponent<SpriteRenderer>();
            m_Body.sprite = PlaceholderSprites.Player(k_SpriteWidthPx, k_SpriteHeightPx);
            m_Body.sortingOrder = 10;

            var pivot = new GameObject("Held Item");
            m_HeldItemPivot = pivot.transform;
            m_HeldItemPivot.SetParent(transform, false);
            m_HeldItemPivot.localPosition = MeleeSwing.HandOffsetPx / SimUnits.PixelsPerTile;

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

            if (Health.IsDead)
            {
                if (Health.Tick()) Respawn();
                return;
            }

            if (ItemUser.CanSwitchItems)
            {
                if (input.HotbarKey >= 0) Inventory.Select(input.HotbarKey);
                // Scrolling down moves right along the hotbar.
                if (input.HotbarScroll != 0) Inventory.Select(Inventory.SelectedSlot - input.HotbarScroll);
            }

            Vector2 cursorPx = input.CursorWorld * SimUnits.PixelsPerTile;
            ItemUser.Tick(tick, input.UseItem, CursorTile, cursorPx, Motor);

            // While swinging, the player faces the way the swing was aimed.
            if (ItemUser.IsUsing) Motor.Direction = ItemUser.UseDirection;

            Motor.Tick(input.Left, input.Right, input.Jump);

            HandleDevInput(input, cursorPx);
            Health.Tick();
            if (!Health.IsDead) Mana.Tick();
        }

        public int ApplyHit(in HitInfo hit)
        {
            if (!CanBeHit) return 0;

            int damage = CombatRules.ComputeDamage(hit.Damage, Defense);
            int dealt = Health.TakeDamage(damage);
            if (dealt <= 0) return 0;

            CombatText.Spawn(Motor.Center, dealt.ToString(), CombatText.PlayerDamaged);

            if (Health.IsDead)
            {
                Die();
            }
            else if (hit.Knockback > 0f)
            {
                Motor.ApplyKnockback(CombatRules.KnockbackVelocity(hit.Knockback, hit.Direction));
            }

            return dealt;
        }

        void HandleDevInput(InputFrame input, Vector2 cursorPx)
        {
            if (input.DevKill)
            {
                Health.Kill();
                Die();
                return;
            }

            if (input.DevHurt)
            {
                // Pushed away from the cursor, as if hit by something standing there.
                int direction = cursorPx.x <= Motor.Center.x ? 1 : -1;
                ApplyHit(new HitInfo { Damage = devHurtDamage, Knockback = devHurtKnockback, Direction = direction });
            }
        }

        void Die()
        {
            ItemUser.Cancel();
            Motor.Velocity = Vector2.zero;
            CombatText.Spawn(Motor.Center + new Vector2(0f, 24f), "You died", CombatText.PlayerDamaged);
        }

        void Respawn()
        {
            Motor.Teleport(m_SpawnPx);
            ItemUser.Cancel();
            Mana.Refill();
        }

        void LateUpdate()
        {
            if (Motor == null) return;

            float alpha = SimulationClock.Instance != null ? SimulationClock.Instance.Alpha : 1f;
            Vector2 px = Motor.GetVisualPosition(alpha);
            transform.position = new Vector3((px.x + Motor.Width * 0.5f) / SimUnits.PixelsPerTile,
                                             px.y / SimUnits.PixelsPerTile, 0f);

            bool alive = !Health.IsDead;
            m_Body.enabled = alive;
            m_Body.flipX = Motor.Direction < 0;
            // Blink while immune after a hit.
            m_Body.color = Health.IsImmune && (Time.frameCount / 4) % 2 == 0 ? new Color(1f, 1f, 1f, 0.4f) : Color.white;

            UpdateHeldItem(alpha, alive);
        }

        void UpdateHeldItem(float alpha, bool alive)
        {
            ItemDef def = ItemDefs.Get(ItemUser.UsingItem);
            bool swinging = alive && ItemUser.IsUsing && def.UseStyle == UseStyle.Swing;
            m_HeldItem.enabled = swinging;
            if (!swinging) return;

            if (def.IsPickaxe)
            {
                m_HeldItem.sprite = PlaceholderSprites.Pickaxe(def.IconColor);
                // The placeholder sprite is 16 px; scale it to the item's real reach so the visual matches hits.
                m_HeldItem.transform.localScale = Vector3.one * (def.SwingLengthPx / SimUnits.PixelsPerTile);
            }
            else
            {
                m_HeldItem.sprite = PlaceholderSprites.Square($"icon_{def.Id}", def.IconColor, 8);
                m_HeldItem.transform.localScale = Vector3.one;
            }

            float angle = MeleeSwing.Angle(ItemUser.ItemAnimation - alpha, ItemUser.ItemAnimationMax);
            int direction = ItemUser.UseDirection;
            m_HeldItemPivot.localScale = new Vector3(direction, 1f, 1f);
            m_HeldItemPivot.localRotation = Quaternion.Euler(0f, 0f, angle * direction);
        }
    }
}
