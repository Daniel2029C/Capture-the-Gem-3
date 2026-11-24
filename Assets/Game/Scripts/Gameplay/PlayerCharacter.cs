using CTG.Classes;
using CTG.Combat;
using CTG.Core;
using CTG.Economy;
using CTG.Input;
using CTG.Items;
using CTG.World;
using UnityEngine;

namespace CTG.Gameplay
{
    /// <summary>
    /// A player: owns the motor, health, inventory and item use and steps them on the simulation clock. Order per
    /// tick: hotbar selection and item use (including melee hits), then movement, then health (regeneration,
    /// immunity, respawn) and mana. How it looks is <see cref="Rendering.PlayerView"/>'s job; this class never draws.
    /// </summary>
    public class PlayerCharacter : MonoBehaviour, ITickable, IDamageable
    {
        // The character is drawn 3 tiles (48 px) tall. The hitbox is 2 px shorter so 3-tile-tall tunnels can be
        // entered mid-air with a little room to spare.
        public const int HitboxWidthPx = 22;
        public const int HitboxHeightPx = 46;

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

        IInputSource m_Input;
        Vector2 m_SpawnPx;
        TileWorld m_World;
        SmartCursor m_SmartCursor;
        int m_LavaCooldown;

        public PlayerMotor Motor { get; private set; }
        public Health Health { get; private set; }
        public Mana Mana { get; private set; }
        /// <summary>Shards, experience and level.</summary>
        public PlayerProgress Progress { get; } = new();
        /// <summary>Artifacts and spells bought.</summary>
        public Purchases Purchases { get; } = new();
        /// <summary>Multipliers from artifacts, level and active effects, refreshed every tick.</summary>
        public PlayerModifiers Modifiers { get; private set; }
        /// <summary>Stable id from <see cref="PlayerRegistry"/>, unique within the match.</summary>
        public int Id { get; private set; }
        /// <summary>Shown in announcements and on the scoreboard.</summary>
        public string PlayerName { get; set; }
        /// <summary>Whether this client controls this player.</summary>
        public bool IsLocal => MatchContext.LocalPlayer == this;

        /// <summary>Whether the local player may see this player's offering orb: their own, and their teammates'.</summary>
        public bool OfferingVisibleToLocal
        {
            get
            {
                PlayerCharacter local = MatchContext.LocalPlayer;
                return local == null || local == this || local.Team == Team;
            }
        }

        /// <summary>Gems this player has picked up and captured this match, for the end-of-match summary.</summary>
        public int GemPickups { get; private set; }
        public int GemCaptures { get; private set; }
        public void AddGemPickup() => GemPickups++;
        public void AddGemCapture() => GemCaptures++;

        /// <summary>Match statistics, shown on the scoreboard.</summary>
        public int Kills { get; private set; }
        public int Deaths { get; private set; }
        public int Assists { get; private set; }
        /// <summary>Kills since this player last died; raises their bounty.</summary>
        public int KillStreak { get; private set; }
        /// <summary>The shards an enemy would earn for killing this player right now.</summary>
        public int Bounty => EconomyRules.KillShards(KillStreak, DeathStreak);
        /// <summary>Deaths since this player last made a kill; lowers their bounty.</summary>
        public int DeathStreak { get; private set; }
        public void AddKill() { Kills++; KillStreak++; DeathStreak = 0; }
        public void AddAssist() => Assists++;

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
            if (Offerings > 0 && announce) Presentation?.ShowText(Motor.Center + new Vector2(0f, 40f), "Offerings lost", TextKind.PlayerDamage);
            Offerings = 0;
        }

        /// <summary>The shop panel is open (only possible at base).</summary>
        public bool ShopOpen { get; private set; }
        /// <summary>Buffs and debuffs on the player (burning, nectar regeneration, the Apex buff, Endurance).</summary>
        public StatusEffects Effects { get; } = new();
        EffectModifiers m_EffectModifiers = EffectModifiers.None;
        /// <summary>The class's grappling hooks (ability 1), or null if it has none.</summary>
        public GrapplingHooks Hooks { get; private set; }

        TileEditor m_TileEditor;

        /// <summary>
        /// Whether this player may mine the tile: it must be breakable, and none of their own hooks can be latched to it
        /// (you cannot dig out what you are hanging from; other players still can, which drops the hook).
        /// </summary>
        public bool CanMineTile(Vector2Int tile) => m_TileEditor.CanMine(tile) && (Hooks == null || !Hooks.IsLatchedTo(tile));

        /// <summary>Gives the player grappling hooks with this tuning, or takes them away (null). Used by <see cref="GrappleMobility"/>.</summary>
        public void SetGrapple(GrappleMobility stats)
        {
            Hooks?.ReleaseAll();
            Hooks = stats != null ? new GrapplingHooks(stats, m_World) : null;
        }
        /// <summary>Abilities 1-3 (default keys Shift, E, C).</summary>
        public AbilitySet Abilities { get; } = new();
        /// <summary>The state the class's abilities and items share (see <see cref="ClassKit"/>), or null.</summary>
        public ClassKit Kit { get; private set; }
        /// <summary>The Archer's shared ability state, or null for other classes.</summary>
        public ArcherKit Archer => Kit as ArcherKit;
        /// <summary>The player's own spawn room (world px), or null. Standing in it gives extra regeneration.</summary>
        public Rect? HomeRoomPx { get; set; }
        /// <summary>No controls reach the player (match countdown, results): they still stand and fall, but can't act.</summary>
        public bool InputLocked { get; set; }
        public Inventory Inventory { get; private set; }
        public ItemUser ItemUser { get; private set; }
        public InputFrame LastInput { get; private set; }
        public Vector2Int CursorTile { get; private set; }
        public Team Team { get; set; } = Team.Red;
        /// <summary>Flat defense: the class's, plus whatever status effects add.</summary>
        public int Defense => (Class != null ? Class.Stats.Defense : 0) + Effects.Aggregate().Defense;
        /// <summary>The selected class, or null before one is chosen.</summary>
        public ClassDef Class { get; private set; }

        public Rect HitboxPx => new(Motor.Position.x, Motor.Position.y, Motor.Width, Motor.Height);
        public bool CanBeHit => Health != null && !Health.IsDead && !Health.IsImmune && !Effects.Aggregate().Invulnerable;

        /// <param name="isLocal">This client controls this player (the camera, HUD and shop follow it).</param>
        public void Initialize(TileWorld world, TileEditor tileEditor, IInputSource input, Vector2 spawnPx, bool isLocal = false)
        {
            Id = MatchContext.Current.Players.Add(this, isLocal);
            if (string.IsNullOrEmpty(PlayerName)) PlayerName = $"Player {Id}";
            m_Input = input;
            m_World = world;
            m_TileEditor = tileEditor;
            m_SmartCursor = new SmartCursor(world, tileEditor) { CanMineTile = CanMineTile };
            m_SpawnPx = spawnPx;
            Motor = new PlayerMotor(world, HitboxWidthPx, HitboxHeightPx) { Stats = movement };
            Motor.Teleport(spawnPx);
            Health = new Health(maxLife, regenPerSecond, hitImmunityTicks,
                                SimUnits.SecondsToTicks(respawnSeconds));
            Health.Died += OnHealthDied;
            Mana = new Mana(0, manaRegenPerSecond);
            Inventory = new Inventory();
            ItemUser = new ItemUser(Inventory, tileEditor, this) { CanMineTile = CanMineTile };
            Progress.LeveledUp += level => Presentation?.ShowText(Motor.Center + new Vector2(0f, 56f), $"Level {level}!", TextKind.Heal);
            Purchases.Reset();

            MatchContext.Current.Clock.Register(this);
            MatchContext.Current.Damageables.Register(this);
        }

        void OnDestroy()
        {
            MatchContext.Current?.Players.Remove(this);
            MatchContext.Current?.Clock?.Unregister(this);
            MatchContext.Current?.Damageables.Unregister(this);
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

            stats.MaxRunSpeed *= 1f + Class.Stats.MoveSpeedBonus;
            stats.RunAcceleration = (stats.RunAcceleration + Class.Stats.RunAccelerationBonus) * (1f + Class.Stats.MoveSpeedBonus);
            return stats;
        }

        /// <summary>
        /// Becomes the given class: takes its life, mana, defense and movement bonuses, replaces the
        /// inventory with its loadout and starts with full life and mana.
        /// </summary>
        public void SetClass(ClassDef def)
        {
            ClassDef previous = Class;
            Class = def;
            Health.Max = def.Stats.MaxLife;
            Health.Refill();
            Mana.Max = def.Stats.MaxMana;
            Mana.Refill();
            Motor.Stats = ClassAdjustedMovement();
            if (previous != null) foreach (Mobility feature in previous.Mobilities) feature.Remove(this);
            foreach (Mobility feature in def.Mobilities) feature.Apply(this);
            Progress.Reset();
            Purchases.Reset();
            Offerings = 0;
            Effects.ClearAll();
            ShopOpen = false;

            Kit = def.CreateKit?.Invoke();
            ItemUser.Bow.Kit = Archer;
            ItemUser.MiningSpeedMultiplier = def.Stats.MiningSpeedMultiplier;
            for (int i = 0; i < AbilitySet.SlotCount; i++)
            {
                Abilities.Set(i, i < def.Abilities.Count ? AbilityFactory.Create(def.Abilities[i], Kit) : null);
            }

            ItemUser.Cancel();
            for (int i = 0; i < Inventory.SlotCount; i++) Inventory.Set(i, ItemId.None, 0);
            foreach (LoadoutEntry entry in def.Loadout) Inventory.Set(entry.Slot, entry.Item, entry.Count);
            Inventory.Select(0);
            ClassChanged?.Invoke(def);
        }

        /// <summary>Raised after the player takes a class; views use it to switch to that class's art.</summary>
        public event System.Action<ClassDef> ClassChanged;

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
            Kit?.Tick(cooldownRate);
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

            // A pickaxe swing keeps the player facing the way it swings, even while running the other way
            // (the motor turns the player to face their movement, which would otherwise win).
            if (ItemUser.IsUsing && Inventory.Selected.Def.IsPickaxe) Motor.Direction = ItemUser.UseDirection;

            HandleDevInput(input, cursorPx);
            // Effects applied earlier this tick (lava, nectar) count now; they are counted down after life is updated.
            m_EffectModifiers = Effects.Aggregate();
            Health.RegenPerSecond = regenPerSecond + Modifiers.RegenBonusPerSecond + m_EffectModifiers.RegenPerSecond;
            Health.ZoneRegenPerSecond = HomeRoomPx.HasValue && HomeRoomPx.Value.Contains(Motor.Center) ? spawnRoomRegenPerSecond : 0f;
            Health.DrainPerSecond = m_EffectModifiers.NetDrainPerSecond;
            Health.RegenBlocked = m_EffectModifiers.BlocksRegen;
            Health.Tick();
            Effects.Tick();
            if (!Health.IsDead)
            {
                Mana.RegenPerSecond = manaRegenPerSecond + m_EffectModifiers.ManaRegenPerSecond;
                Mana.DrainPerSecond = m_EffectModifiers.ManaDrainPerSecond;
                Mana.RegenBlocked = m_EffectModifiers.BlocksManaRegen;
                Mana.Tick();
            }
        }

        const int k_HealingPotionAmount = 50;
        static readonly int k_SpellCooldownTicks = SimUnits.SecondsToTicks(60f);

        /// <summary>True inside the player's own spawn room (or anywhere on maps without one).</summary>
        public bool InBase => !HomeRoomPx.HasValue || HomeRoomPx.Value.Contains(Motor.Center);

        public void CloseShop() => ShopOpen = false;

        /// <summary>Recomputes the artifact, level and effect multipliers and applies the ones that live on other objects.</summary>
        void RefreshModifiers()
        {
            Modifiers = PlayerModifiers.Compute(Purchases, Progress, Effects.Aggregate());
            ItemUser.WeaponDamageMultiplier = Modifiers.WeaponDamageMultiplier;
            ItemUser.MiningSpeedMultiplier = (Class != null ? Class.Stats.MiningSpeedMultiplier : 1f) * Modifiers.MiningSpeedMultiplier;

            // Life scales with level; gaining max life also gives that much life now.
            int baseLife = Class != null ? Class.Stats.MaxLife : maxLife;
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
                Presentation?.ShowText(Motor.Center, $"+{healed}", TextKind.Heal);
            }
            else
            {
                Effects.Apply(StatusEffectDefs.Endurance);
                Presentation?.ShowText(Motor.Center, "Endurance", TextKind.Heal);
            }
        }

        /// <summary>Kicks up a few dirt chips from the tile underfoot while running on dirt.</summary>
        void EmitWalkDust(int tick)
        {
            if (!Motor.Grounded || Mathf.Abs(Motor.Velocity.x) < 1.2f || tick % 5 != 0) return;

            int tx = Mathf.FloorToInt(Motor.Center.x / SimUnits.PixelsPerTile);
            int ty = Mathf.FloorToInt((Motor.Position.y - 1f) / SimUnits.PixelsPerTile);
            TileId below = m_World.Get(tx, ty);
            if (!PresentationEvents.ShedsDirt(below)) return;

            // Chips spray backwards, away from the direction of travel.
            var feet = new Vector2(Motor.Center.x, Motor.Position.y + 1f);
            Presentation?.SprayDirt(feet, below, 2, new Vector2(-Mathf.Sign(Motor.Velocity.x), 1.2f), 70f);
        }

        public int ApplyHit(in HitInfo hit)
        {
            if (!CanBeHit) return 0;

            float reduction = (Class != null ? Class.Stats.DamageReduction : 0f) + Modifiers.DamageReduction;
            int damage = CombatRules.ComputeDamage(hit.Damage, Defense, reduction);

            // A lethal hit raises Health.Died (with this attacker as the killer) before this returns; OnHealthDied does the rest.
            int dealt = Health.TakeDamage(damage, DamageFlags.None, hit.Source);
            if (dealt <= 0) return 0;

            Presentation?.ShowText(Motor.Center, dealt.ToString(), TextKind.PlayerDamage);
            if (Health.IsDead) return dealt;

            var attacker = hit.Source as PlayerCharacter;
            if (attacker != null && attacker != this && attacker.Team != Team) m_Attackers[attacker] = m_CurrentTick;

            // Movement features react to the hit (a grapple can be knocked loose, for instance).
            if (Class != null) foreach (Mobility feature in Class.Mobilities) feature.OnHit(this, hit);

            if (!hit.Knockback.IsZero) Motor.ApplyKnockback(CombatRules.KnockbackVelocity(hit));
            return dealt;
        }

        readonly System.Collections.Generic.Dictionary<PlayerCharacter, int> m_Attackers = new();
        int m_CurrentTick;

        /// <summary>Reports a death caused by an enemy player to the economy: the killer, and anyone who damaged us recently.</summary>
        void CreditKill(PlayerCharacter killer)
        {
            if (killer != null && killer != this && killer.Team != Team && MatchContext.Current.Economy != null)
            {
                int window = SimUnits.SecondsToTicks(EconomyRules.AssistWindowSeconds);
                var assisters = new System.Collections.Generic.List<PlayerCharacter>();
                foreach (var entry in m_Attackers)
                {
                    if (entry.Key != killer && m_CurrentTick - entry.Value <= window) assisters.Add(entry.Key);
                }

                MatchContext.Current.Economy.RecordKill(killer, this, assisters);
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

        /// <summary>The one place a death is handled, whatever caused it: an attack, lava or burning.</summary>
        void OnHealthDied()
        {
            CreditKill(Health.Killer as PlayerCharacter);
            Die();
        }

        void Die()
        {
            Deaths++;
            DeathStreak++;
            KillStreak = 0;
            ClearOfferings(true);
            Effects.ClearOnDeath();
            if (Class != null) foreach (Mobility feature in Class.Mobilities) feature.OnDeath(this);
            ItemUser.Cancel();
            Motor.Velocity = Vector2.zero;
            Presentation?.ShowText(Motor.Center + new Vector2(0f, 24f), "You died", TextKind.PlayerDamage);
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
                Effects.Apply(StatusEffectDefs.Regeneration, SimUnits.SecondsToTicks(nectarBuffSeconds));
                if (!inLava) Effects.Remove(StatusEffectDefs.Burning);
            }

            if (!inLava || m_LavaCooldown > 0) return;

            int damage = CombatRules.ComputeDamage(lavaDamage, Defense);
            int dealt = Health.TakeDamage(damage, DamageFlags.Environmental);
            m_LavaCooldown = lavaHitCooldownTicks;
            Presentation?.ShowText(Motor.Center, dealt.ToString(), TextKind.PlayerDamage);

            // Dying in lava is handled by OnHealthDied; a dead player does not start burning.
            if (Health.IsDead) return;

            Effects.Apply(StatusEffectDefs.Burning, SimUnits.SecondsToTicks(lavaBurnSeconds));
        }

        void Respawn()
        {
            Hooks?.ReleaseAll();
            m_LavaCooldown = 0;
            Motor.Liquid = LiquidType.None;
            Motor.Teleport(m_SpawnPx);
            ItemUser.Cancel();
            Abilities.Reset();
            Kit?.Reset();
            Motor.RunSpeedScale = 1f;
            Motor.RunAccelerationScale = 1f;
            Mana.Refill();
        }

        /// <summary>Where this player's floating text and particles go; null when no match is running.</summary>
        static PresentationEvents Presentation => MatchContext.Current?.Presentation;
    }
}
