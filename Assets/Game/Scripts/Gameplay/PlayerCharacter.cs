using CTG.Classes;
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
        [Tooltip("Maximum mana comes from the class; 0 means no mana.")]
        [SerializeField] float manaRegenPerSecond = 1f;

        [Header("Healing potion")]
        [SerializeField] int potionHeal = 50;
        [Tooltip("Seconds before the potion can be used again.")]
        [SerializeField] float potionCooldownSeconds = 60f;
        [Tooltip("Allow drinking at full life (wastes the potion).")]
        [SerializeField] bool potionUsableAtFullLife;

        [Header("Lava")]
        [SerializeField] int lavaDamage = 80;
        [Tooltip("Ticks between lava hits while staying in it.")]
        [SerializeField] int lavaHitCooldownTicks = 40;
        [Tooltip("Seconds of burning applied by each lava hit.")]
        [SerializeField] float lavaBurnSeconds = 7f;

        [Header("Nectar")]
        [Tooltip("Seconds of the regeneration buff granted (and refreshed to) while standing in nectar.")]
        [SerializeField] float nectarBuffSeconds = 30f;

        [Header("Testing")]
        [SerializeField] int devHurtDamage = 25;
        [SerializeField] float devHurtKnockback = 1f;

        PlayerInput m_Input;
        Vector2 m_SpawnPx;
        TileWorld m_World;
        SmartCursor m_SmartCursor;
        int m_LavaCooldown;
        Team m_BodyTeam = Team.Neutral;
        SpriteRenderer m_Body;
        Transform m_HeldItemPivot;
        SpriteRenderer m_HeldItem;
        Transform m_BowPivot;
        SpriteRenderer m_BowSprite;
        SpriteRenderer m_NockedArrow;

        public PlayerMotor Motor { get; private set; }
        public Health Health { get; private set; }
        public Mana Mana { get; private set; }
        public HealingPotion Potion { get; } = new();
        /// <summary>The class's grappling hooks (ability 1), or null if it has none.</summary>
        public GrapplingHooks Hooks { get; private set; }
        public Inventory Inventory { get; private set; }
        public ItemUser ItemUser { get; private set; }
        public InputFrame LastInput { get; private set; }
        public Vector2Int CursorTile { get; private set; }
        public Team Team { get; set; } = Team.Red;
        public int Defense { get; set; }
        /// <summary>The selected class, or null before one is chosen.</summary>
        public ClassDef Class { get; private set; }

        public Rect HitboxPx => new(Motor.Position.x, Motor.Position.y, Motor.Width, Motor.Height);
        public bool CanBeHit => Health != null && !Health.IsDead && !Health.IsImmune;

        public void Initialize(TileWorld world, TileEditor tileEditor, PlayerInput input, Vector2 spawnPx)
        {
            m_Input = input;
            m_World = world;
            m_SmartCursor = new SmartCursor(world, tileEditor);
            m_SpawnPx = spawnPx;
            Motor = new PlayerMotor(world, HitboxWidthPx, HitboxHeightPx) { Stats = movement };
            Motor.Teleport(spawnPx);
            Health = new Health(maxLife, regenPerSecond, hitImmunityTicks,
                                SimUnits.SecondsToTicks(respawnSeconds));
            Mana = new Mana(0, manaRegenPerSecond);
            ApplyPotionSettings();
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
            if (Motor != null) Motor.Stats = ClassAdjustedMovement();
            if (Health != null)
            {
                if (Class == null) Health.Max = maxLife;
                Health.RegenPerSecond = regenPerSecond;
                Health.HitImmunityTicks = hitImmunityTicks;
                Health.RespawnTicks = SimUnits.SecondsToTicks(respawnSeconds);
            }

            ApplyPotionSettings();

            if (Mana != null)
            {
                Mana.RegenPerSecond = manaRegenPerSecond;
            }
        }

        void ApplyPotionSettings()
        {
            Potion.HealAmount = potionHeal;
            Potion.CooldownTicks = SimUnits.SecondsToTicks(potionCooldownSeconds);
            Potion.UsableAtFullLife = potionUsableAtFullLife;
        }

        MovementStats ClassAdjustedMovement()
        {
            MovementStats stats = movement;
            if (Class == null) return stats;

            stats.MaxRunSpeed *= 1f + Class.BonusMoveSpeed;
            stats.RunAcceleration = (stats.RunAcceleration + Class.BonusRunAcceleration) * (1f + Class.BonusMoveSpeed);
            return stats;
        }

        /// <summary>
        /// Becomes the given class: takes its life, mana, defense and movement bonuses, replaces the
        /// inventory with its loadout and starts with full life and mana.
        /// </summary>
        public void SetClass(ClassDef def)
        {
            Class = def;
            Health.Max = def.MaxLife;
            Health.Refill();
            Mana.Max = def.MaxMana;
            Mana.Refill();
            Defense = def.BonusDefense;
            Motor.Stats = ClassAdjustedMovement();
            Motor.DoubleJump = def.DoubleJump;
            Hooks?.ReleaseAll();
            Hooks = def.Hook != null ? new GrapplingHooks(def.Hook, m_World) : null;
            Potion.Reset();

            ItemUser.Cancel();
            for (int i = 0; i < Inventory.SlotCount; i++) Inventory.Set(i, ItemId.None, 0);
            foreach (LoadoutEntry entry in def.Loadout) Inventory.Set(entry.Slot, entry.Item, entry.Count);
            Inventory.Select(0);
        }

        void BuildVisuals()
        {
            m_Body = gameObject.AddComponent<SpriteRenderer>();
            m_BodyTeam = Team;
            m_Body.sprite = PlaceholderSprites.Player(k_SpriteWidthPx, k_SpriteHeightPx, TeamColors.For(Team));
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

            // Drawn bow: pivots on the body centre and turns to the aim.
            m_BowPivot = new GameObject("Bow").transform;
            m_BowPivot.SetParent(transform, false);
            m_BowPivot.localPosition = new Vector3(0f, HitboxHeightPx * 0.5f / SimUnits.PixelsPerTile, 0f);

            m_BowSprite = new GameObject("Bow Sprite").AddComponent<SpriteRenderer>();
            m_BowSprite.transform.SetParent(m_BowPivot, false);
            m_BowSprite.sortingOrder = 12;
            m_BowSprite.enabled = false;

            m_NockedArrow = new GameObject("Nocked Arrow").AddComponent<SpriteRenderer>();
            m_NockedArrow.transform.SetParent(m_BowPivot, false);
            m_NockedArrow.sortingOrder = 13;
            m_NockedArrow.enabled = false;

            var hookView = new GameObject("Grapple Hooks").AddComponent<GrappleHookView>();
            hookView.transform.SetParent(transform, false);
            hookView.Bind(this);
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

            Potion.Tick();
            if (input.UsePotion)
            {
                int healed = Potion.TryUse(Health);
                if (healed >= 0) CombatText.Spawn(Motor.Center, $"+{healed}", CombatText.Healed);
            }

            if (ItemUser.CanSwitchItems)
            {
                int visible = Inventory.VisibleSlots;
                if (input.HotbarKey >= 0 && input.HotbarKey < visible) Inventory.Select(input.HotbarKey);
                // Scrolling down moves right along the visible hotbar, wrapping at its ends.
                if (input.HotbarScroll != 0)
                {
                    int next = ((Inventory.SelectedSlot - input.HotbarScroll) % visible + visible) % visible;
                    Inventory.Select(next);
                }
            }

            // The cursor tile is the one under the mouse, adjusted by the always-on cursor assist.
            Vector2 cursorPx = input.CursorWorld * SimUnits.PixelsPerTile;
            CursorTile = m_SmartCursor.Resolve(Inventory.Selected.Def, Team, Motor, ItemUser.BonusTileReach,
                                               cursorPx, TileWorld.WorldToTile(input.CursorWorld));
            ItemUser.Tick(tick, input.UseItem, input.UseAlt, CursorTile, cursorPx, Motor);

            // While using an item, the player faces the way it was aimed (a drawn bow follows the cursor).
            if (ItemUser.IsUsing) Motor.Direction = ItemUser.UseDirection;

            // Ability 1: fire a hook toward the cursor.
            if (Hooks != null && input.Ability1) Hooks.TryFire(Motor.Center, cursorPx, Motor.Direction);

            UpdateLiquids();

            // Hooked, the latched hooks pull the player; a fresh jump press lets go.
            Vector2? pull = Hooks?.PullVelocity(Motor.Center);
            if (pull.HasValue)
            {
                if (Motor.TickGrappled(input.Jump, pull.Value)) Hooks.ReleaseAll();
            }
            else
            {
                Motor.Tick(input.Left, input.Right, input.Jump);
            }

            Hooks?.Update(Motor.Center);

            if (ItemUser.Bow.Active) Motor.Direction = ItemUser.UseDirection;

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

            // Being hit by a projectile shakes off hooks that have latched or turned back.
            if (hit.FromProjectile) Hooks?.ReleaseAttached();

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
            Hooks?.ReleaseAll();
            Potion.Reset();
            ItemUser.Cancel();
            Motor.Velocity = Vector2.zero;
            CombatText.Spawn(Motor.Center + new Vector2(0f, 24f), "You died", CombatText.PlayerDamaged);
        }

        /// <summary>Width of the part of the hitbox, centred on it, that counts as touching nectar, px.</summary>
        const float k_NectarProbeWidthPx = 10f;

        /// <summary>
        /// Liquid contact. Lava: while any part of the hitbox is in it the player is submerged (movement changes
        /// in the motor), and each time the lava cooldown allows it deals damage and sets the player burning.
        /// The lava cooldown is separate from post-hit immunity, so lava keeps hurting through it.
        /// Nectar: only the middle of the body counts (a narrow strip, half the hitbox tall); while in it the
        /// player moves slowly, the regeneration buff is refreshed, and burning is put out unless also in lava.
        /// Nectar's movement wins over lava's.
        /// </summary>
        void UpdateLiquids()
        {
            if (m_LavaCooldown > 0) m_LavaCooldown--;

            Rect box = HitboxPx;
            bool inLava = m_World.OverlapsLiquid(LiquidType.Lava, box.x, box.y, box.width, box.height);

            float probeWidth = Mathf.Min(k_NectarProbeWidthPx, box.width);
            float probeHeight = box.height * 0.5f;
            bool inNectar = m_World.OverlapsLiquid(LiquidType.Nectar,
                box.center.x - probeWidth * 0.5f, box.center.y - probeHeight * 0.5f, probeWidth, probeHeight);

            Motor.Liquid = inNectar ? LiquidType.Nectar : inLava ? LiquidType.Lava : LiquidType.None;

            if (inNectar)
            {
                Health.GrantRegenBuff(SimUnits.SecondsToTicks(nectarBuffSeconds));
                if (!inLava) Health.Extinguish();
            }

            if (!inLava || m_LavaCooldown > 0) return;

            int damage = CombatRules.ComputeDamage(lavaDamage, Defense);
            int dealt = Health.TakeEnvironmentalDamage(damage);
            m_LavaCooldown = lavaHitCooldownTicks;
            CombatText.Spawn(Motor.Center, dealt.ToString(), CombatText.PlayerDamaged);

            if (Health.IsDead)
            {
                Die();
                return;
            }

            Health.Ignite(SimUnits.SecondsToTicks(lavaBurnSeconds));
        }

        void Respawn()
        {
            Hooks?.ReleaseAll();
            m_LavaCooldown = 0;
            Motor.Liquid = LiquidType.None;
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
            if (m_BodyTeam != Team)
            {
                m_BodyTeam = Team;
                m_Body.sprite = PlaceholderSprites.Player(k_SpriteWidthPx, k_SpriteHeightPx, TeamColors.For(Team));
            }

            // Burning flickers orange; blink while immune after a hit.
            Color tint = Color.white;
            if (Health.IsBurning) tint = Color.Lerp(Color.white, new Color(1f, 0.5f, 0.15f), 0.55f + 0.25f * Mathf.Sin(Time.time * 18f));
            if (Health.IsImmune && (Time.frameCount / 4) % 2 == 0) tint.a = 0.4f;
            m_Body.color = tint;

            UpdateHeldItem(alpha, alive);
            UpdateBow(alive);
        }

        void UpdateBow(bool alive)
        {
            ChargeBow bow = ItemUser.Bow;
            bool drawn = alive && bow.Active && bow.Item != null;
            m_BowSprite.enabled = drawn;
            m_NockedArrow.enabled = drawn && !bow.Released;
            if (!drawn) return;

            float angle = bow.AimAngle * Mathf.Rad2Deg;
            m_BowPivot.localRotation = Quaternion.Euler(0f, 0f, angle);
            // Facing left, mirror across the aim line so the bow isn't upside down.
            m_BowPivot.localScale = new Vector3(1f, bow.Direction, 1f);

            // Bow 9·√2 px out along the aim; arrow nocked 16·√2 px out, pulled back up to 13 px by charge.
            const float px = 1f / SimUnits.PixelsPerTile;
            m_BowSprite.sprite = PlaceholderSprites.Bow(bow.Item.IconColor);
            m_BowSprite.transform.localPosition = new Vector3(9f * 1.41421356f * px, 0f, 0f);

            ChargeBowStats stats = bow.Item.ChargeBow;
            float pullback = Mathf.Lerp(0f, 13f, Mathf.Min(bow.Charge, stats.MaxCharge) / stats.MaxCharge);
            m_NockedArrow.sprite = PlaceholderSprites.Arrow(ProjectileDefs.Get(bow.Arrow).Color);
            m_NockedArrow.transform.localPosition = new Vector3((16f * 1.41421356f - pullback) * px, 0f, 0f);

            // At full charge the arrow alternates between slightly brighter and slightly dimmer every 200 ticks.
            Color tint = Color.white;
            if (bow.FullyCharged)
            {
                tint = bow.FullChargeTicks % 400 <= 200
                    ? Color.Lerp(tint, Color.white, 0.2f)
                    : Color.Lerp(tint, new Color32(150, 150, 150, 255), 0.2f);
            }

            m_NockedArrow.color = tint;
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
