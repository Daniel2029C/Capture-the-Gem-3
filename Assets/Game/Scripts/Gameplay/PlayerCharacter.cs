using CTG.Classes;
using CTG.Combat;
using CTG.Core;
using CTG.Economy;
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
        [Tooltip("Extra life per second while inside the home spawn room.")]
        [SerializeField] float spawnRoomRegenPerSecond = 5f;
        [Tooltip("Ticks of immunity after being hit, so overlapping attacks can't all land in the same instant.")]
        [SerializeField] int hitImmunityTicks = 8;
        [Tooltip("Seconds from death to respawn. Will be driven by class and game state later.")]
        [SerializeField] float respawnSeconds = 3f;

        [Header("Mana")]
        [Tooltip("Maximum mana comes from the class; 0 means no mana.")]
        [SerializeField] float manaRegenPerSecond = 1f;

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
        SpriteRenderer m_OfferingMarker;

        public PlayerMotor Motor { get; private set; }
        public Health Health { get; private set; }
        public Mana Mana { get; private set; }
        /// <summary>Shards, experience and level.</summary>
        public PlayerProgress Progress { get; } = new();
        /// <summary>Artifacts and spells bought.</summary>
        public Purchases Purchases { get; } = new();
        /// <summary>Multipliers from artifacts, level and active effects, refreshed every tick.</summary>
        public PlayerModifiers Modifiers { get; private set; }
        /// <summary>The player this client controls (the first one initialised). Used to decide what this client may see.</summary>
        public static PlayerCharacter LocalPlayer { get; private set; }

        /// <summary>Whether the local player may see this player's offering orb: their own, and their teammates'.</summary>
        public bool OfferingVisibleToLocal => LocalPlayer == null || LocalPlayer == this || LocalPlayer.Team == Team;

        /// <summary>Offerings carried: soulbound, lost on death, cashed in at an altar.</summary>
        public int Offerings { get; private set; }
        public void GrantOffering() => Offerings++;

        public bool TakeOffering()
        {
            if (Offerings <= 0) return false;

            Offerings--;
            return true;
        }

        /// <summary>Loses every offering (death, or the Apex Shift wipe).</summary>
        public void ClearOfferings(bool announce)
        {
            if (Offerings > 0 && announce) CombatText.Spawn(Motor.Center + new Vector2(0f, 40f), "Offerings lost", CombatText.PlayerDamaged);
            Offerings = 0;
        }

        /// <summary>The shop panel is open (only possible at base).</summary>
        public bool ShopOpen { get; private set; }
        /// <summary>Ticks left of the Apex buff from the mid-boss (+15% damage, 15% damage reduction); lost on death.</summary>
        public int ApexBuffTicksLeft { get; private set; }
        public static readonly int ApexBuffTicks = SimUnits.SecondsToTicks(120f);
        public void GrantApexBuff() => ApexBuffTicksLeft = ApexBuffTicks;

        /// <summary>Ticks left of the Endurance spell's damage reduction.</summary>
        public int EnduranceTicksLeft { get; private set; }
        /// <summary>The class's grappling hooks (ability 1), or null if it has none.</summary>
        public GrapplingHooks Hooks { get; private set; }
        /// <summary>Abilities 1-3 (default keys Shift, E, C).</summary>
        public AbilitySet Abilities { get; } = new();
        /// <summary>The Archer's shared ability state, or null for other classes.</summary>
        public ArcherKit Archer { get; private set; }
        /// <summary>The player's own spawn room (world px), or null. Standing in it gives extra regeneration.</summary>
        public Rect? HomeRoomPx { get; set; }
        /// <summary>No controls reach the player (match countdown, results): they still stand and fall, but can't act.</summary>
        public bool InputLocked { get; set; }
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
            if (LocalPlayer == null) LocalPlayer = this;
            m_Input = input;
            m_World = world;
            m_SmartCursor = new SmartCursor(world, tileEditor);
            m_SpawnPx = spawnPx;
            Motor = new PlayerMotor(world, HitboxWidthPx, HitboxHeightPx) { Stats = movement };
            Motor.Teleport(spawnPx);
            Health = new Health(maxLife, regenPerSecond, hitImmunityTicks,
                                SimUnits.SecondsToTicks(respawnSeconds));
            Mana = new Mana(0, manaRegenPerSecond);
            Inventory = new Inventory();
            ItemUser = new ItemUser(Inventory, tileEditor, this);
            Progress.LeveledUp += level => CombatText.Spawn(Motor.Center + new Vector2(0f, 56f), $"Level {level}!", CombatText.Healed);
            Purchases.Reset();

            BuildVisuals();
            SimulationClock.Instance.Register(this);
            DamageableRegistry.Register(this);
        }

        void OnDestroy()
        {
            if (LocalPlayer == this) LocalPlayer = null;
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

            if (Mana != null)
            {
                Mana.RegenPerSecond = manaRegenPerSecond;
            }
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
            Motor.FastFall = def.FastFall;
            Hooks?.ReleaseAll();
            Hooks = def.Hook != null ? new GrapplingHooks(def.Hook, m_World) : null;
            Progress.Reset();
            Purchases.Reset();
            Offerings = 0;
            ApexBuffTicksLeft = 0;
            EnduranceTicksLeft = 0;
            ShopOpen = false;

            bool archerKit = System.Array.Exists(def.Abilities, id => id >= AbilityId.ArcherSprint && id <= AbilityId.ArcherLuminite);
            Archer = archerKit ? new ArcherKit() : null;
            ItemUser.Bow.Kit = Archer;
            ItemUser.MiningSpeedMultiplier = def.MiningSpeedMultiplier;
            for (int i = 0; i < AbilitySet.SlotCount; i++)
            {
                Abilities.Set(i, i < def.Abilities.Length ? AbilityFactory.Create(def.Abilities[i], Archer) : null);
            }

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

            // A glowing orb above the head while carrying an offering, so enemies can see who to hunt.
            m_OfferingMarker = new GameObject("Offering Marker").AddComponent<SpriteRenderer>();
            m_OfferingMarker.transform.SetParent(transform, false);
            m_OfferingMarker.transform.localPosition = new Vector3(0f, (k_SpriteHeightPx + 10f) / SimUnits.PixelsPerTile, 0f);
            m_OfferingMarker.sprite = PlaceholderSprites.Solid("offering_orb", new Color32(255, 215, 90, 255), 8, 8);
            m_OfferingMarker.sortingOrder = 14;
            m_OfferingMarker.enabled = false;

            var hookView = new GameObject("Grapple Hooks").AddComponent<GrappleHookView>();
            hookView.transform.SetParent(transform, false);
            hookView.Bind(this);
        }

        public void Tick(int tick)
        {
            InputFrame input = m_Input.Consume();
            if (InputLocked || InputBlock.All) input = new InputFrame { CursorWorld = input.CursorWorld, HotbarKey = -1 };
            if (ShopOpen || InputBlock.Actions)
            {
                // The shop is a menu: clicks and keys don't act in the world, but the player can still move.
                input.UseItem = false;
                input.UseAlt = false;
                input.HookFire = false;
                input.AbilityPressed = 0;
                input.AbilityHeld = 0;
                input.SpellPressed = 0;
            }

            m_CurrentTick = tick;
            LastInput = input;
            CursorTile = TileWorld.WorldToTile(input.CursorWorld);

            if (Health.IsDead)
            {
                if (Health.Tick()) Respawn();
                return;
            }

            Progress.Tick();
            Purchases.Spells.Tick();
            if (EnduranceTicksLeft > 0) EnduranceTicksLeft--;
            if (ApexBuffTicksLeft > 0) ApexBuffTicksLeft--;
            RefreshModifiers();

            if (input.ShopPressed) ShopOpen = !ShopOpen && InBase;
            if (ShopOpen && !InBase) ShopOpen = false;
            for (int i = 0; i < SpellBook.SlotCount; i++)
            {
                if ((input.SpellPressed & (1 << i)) != 0) CastSpell(i);
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

            // Abilities act before the item so a mode change applies to this tick's shot.
            float cooldownRate = 1f / Modifiers.AbilityCooldownScale;
            Archer?.Tick(cooldownRate);
            Abilities.Tick(new AbilityContext(this, tick, cooldownRate), input.AbilityPressed, input.AbilityHeld);
            Motor.RunSpeedScale = Abilities.RunSpeedScale * (ItemUser.Bow.IsChargingLuminite && Archer != null ? Archer.Stats.LuminiteDrawRunScale : 1f);
            Motor.RunAccelerationScale = Abilities.RunAccelerationScale;

            // The cursor tile is the one under the mouse, adjusted by the always-on cursor assist.
            Vector2 cursorPx = input.CursorWorld * SimUnits.PixelsPerTile;
            CursorTile = m_SmartCursor.Resolve(Inventory.Selected.Def, Team, Motor, ItemUser.BonusTileReach,
                                               cursorPx, TileWorld.WorldToTile(input.CursorWorld));
            ItemUser.Tick(tick, input.UseItem, input.UseAlt, CursorTile, cursorPx, Motor);

            // While using an item, the player faces the way it was aimed (a drawn bow follows the cursor).
            if (ItemUser.IsUsing) Motor.Direction = ItemUser.UseDirection;

            // Fire a hook toward the cursor.
            if (Hooks != null && input.HookFire) Hooks.TryFire(Motor.Center, cursorPx, Motor.Direction);

            Motor.DropThrough = input.Down;
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
            EmitWalkDust(tick);

            if (ItemUser.Bow.Active) Motor.Direction = ItemUser.UseDirection;

            HandleDevInput(input, cursorPx);
            Health.RegenPerSecond = regenPerSecond + Modifiers.RegenBonusPerSecond;
            Health.ZoneRegenPerSecond = HomeRoomPx.HasValue && HomeRoomPx.Value.Contains(Motor.Center) ? spawnRoomRegenPerSecond : 0f;
            Health.Tick();
            if (!Health.IsDead) Mana.Tick();
        }

        const int k_HealingPotionAmount = 50;
        static readonly int k_SpellCooldownTicks = SimUnits.SecondsToTicks(60f);
        static readonly int k_EnduranceTicks = SimUnits.SecondsToTicks(5f);

        /// <summary>True inside the player's own spawn room (or anywhere on maps without one).</summary>
        public bool InBase => !HomeRoomPx.HasValue || HomeRoomPx.Value.Contains(Motor.Center);

        public void CloseShop() => ShopOpen = false;

        /// <summary>Recomputes the artifact, level and effect multipliers and applies the ones that live on other objects.</summary>
        void RefreshModifiers()
        {
            Modifiers = PlayerModifiers.Compute(Purchases, Progress, EnduranceTicksLeft > 0, ApexBuffTicksLeft > 0);
            ItemUser.WeaponDamageMultiplier = Modifiers.WeaponDamageMultiplier;
            ItemUser.MiningSpeedMultiplier = (Class != null ? Class.MiningSpeedMultiplier : 1f) * Modifiers.MiningSpeedMultiplier;

            // Life scales with level; gaining max life also gives that much life now.
            int baseLife = Class != null ? Class.MaxLife : maxLife;
            int scaledLife = Mathf.RoundToInt(baseLife * Modifiers.LifeMultiplier);
            if (scaledLife != Health.Max)
            {
                int gained = scaledLife - Health.Max;
                Health.Max = scaledLife;
                if (gained > 0 && !Health.IsDead) Health.Heal(gained);
                else if (Health.Current > scaledLife) Health.Heal(0);
            }
        }

        void CastSpell(int slot)
        {
            SpellId? spell = Purchases.Spells.SlotSpell(slot);
            if (!spell.HasValue) return;

            // Drinking at full life would waste the potion, so it does nothing.
            if (spell.Value == SpellId.HealingPotion && Health.Current >= Health.Max) return;

            int cooldown = Mathf.RoundToInt(k_SpellCooldownTicks * Modifiers.SpellCooldownScale);
            if (!Purchases.Spells.TryConsume(slot, cooldown, out SpellId cast)) return;

            if (cast == SpellId.HealingPotion)
            {
                int healed = Health.Heal(k_HealingPotionAmount);
                CombatText.Spawn(Motor.Center, $"+{healed}", CombatText.Healed);
            }
            else
            {
                EnduranceTicksLeft = k_EnduranceTicks;
                CombatText.Spawn(Motor.Center, "Endurance", CombatText.Healed);
            }
        }

        /// <summary>Kicks up a few dirt chips from the tile underfoot while running on dirt.</summary>
        void EmitWalkDust(int tick)
        {
            if (!Motor.Grounded || Mathf.Abs(Motor.Velocity.x) < 1.2f || tick % 5 != 0) return;

            int tx = Mathf.FloorToInt(Motor.Center.x / SimUnits.PixelsPerTile);
            int ty = Mathf.FloorToInt((Motor.Position.y - 1f) / SimUnits.PixelsPerTile);
            TileId below = m_World.Get(tx, ty);
            if (!ParticleView.IsDirt(below)) return;

            // Chips spray backwards, away from the direction of travel.
            var feet = new Vector2(Motor.Center.x, Motor.Position.y + 1f);
            ParticleView.DirtBurst(feet, below, 2, new Vector2(-Mathf.Sign(Motor.Velocity.x), 1.2f), 70f);
        }

        public int ApplyHit(in HitInfo hit)
        {
            if (!CanBeHit) return 0;

            int damage = CombatRules.ComputeDamage(hit.Damage, Defense);
            if (Modifiers.DamageReduction > 0f) damage = Mathf.Max(1, Mathf.RoundToInt(damage * (1f - Modifiers.DamageReduction)));
            int dealt = Health.TakeDamage(damage);
            if (dealt <= 0) return 0;

            var attacker = hit.Source as PlayerCharacter;
            if (attacker != null && attacker != this && attacker.Team != Team) m_Attackers[attacker] = m_CurrentTick;

            // Being hit by a projectile shakes off hooks that have latched or turned back.
            if (hit.FromProjectile) Hooks?.ReleaseAttached();

            CombatText.Spawn(Motor.Center, dealt.ToString(), CombatText.PlayerDamaged);

            if (Health.IsDead)
            {
                CreditKill(attacker);
                Die();
            }
            else if (hit.Knockback > 0f)
            {
                Motor.ApplyKnockback(CombatRules.KnockbackVelocity(hit.Knockback, hit.Direction));
            }

            return dealt;
        }

        readonly System.Collections.Generic.Dictionary<PlayerCharacter, int> m_Attackers = new();
        int m_CurrentTick;

        /// <summary>Reports a death caused by an enemy player to the economy: the killer, and anyone who damaged us recently.</summary>
        void CreditKill(PlayerCharacter killer)
        {
            if (killer != null && killer != this && killer.Team != Team && MatchEconomy.Instance != null)
            {
                int window = SimUnits.SecondsToTicks(EconomyRules.AssistWindowSeconds);
                var assisters = new System.Collections.Generic.List<PlayerCharacter>();
                foreach (var entry in m_Attackers)
                {
                    if (entry.Key != killer && m_CurrentTick - entry.Value <= window) assisters.Add(entry.Key);
                }

                MatchEconomy.Instance.RecordKill(killer, Class, assisters);
            }

            m_Attackers.Clear();
        }

        /// <summary>Testing: raised when the end-match key is pressed (K); the bootstrap hooks it to the match.</summary>
        public event System.Action DevEndMatchRequested;
        /// <summary>Testing: raised when the skip-to-overtime key is pressed; the bootstrap hooks it to the match.</summary>
        public event System.Action DevSkipOvertimeRequested;

        void HandleDevInput(InputFrame input, Vector2 cursorPx)
        {
            if (input.DevEndMatch) DevEndMatchRequested?.Invoke();
            if (input.DevSkipOvertime) DevSkipOvertimeRequested?.Invoke();
        }

        void Die()
        {
            ClearOfferings(true);
            ApexBuffTicksLeft = 0;
            Hooks?.ReleaseAll();
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
            Abilities.Reset();
            Archer?.Reset();
            Motor.RunSpeedScale = 1f;
            Motor.RunAccelerationScale = 1f;
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
            // Enemies can't see the orb: only the carrier and their team can.
            m_OfferingMarker.enabled = alive && Offerings > 0 && OfferingVisibleToLocal;
            if (m_OfferingMarker.enabled)
            {
                float pulse = 1f + 0.2f * Mathf.Sin(Time.time * 6f);
                m_OfferingMarker.transform.localScale = Vector3.one * pulse * (1f + 0.25f * (Mathf.Min(Offerings, 4) - 1));
            }
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
