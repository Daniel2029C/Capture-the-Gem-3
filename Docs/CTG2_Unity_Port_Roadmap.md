# Capture the Gem 2 → Unity: Port Roadmap

Source: `genesisTerraria/CTG2` (tModLoader mod, ~33.5k lines of C# excluding imported/archived code).
Target: this Unity 6 project (`6000.6.7f1`, URP 2D, Input System, Tilemap, Cinemachine, BuildingBlocks framework).

---

## 1. What the tModLoader version actually contains

### 1.1 Gameplay content

| Area | Where in CTG2 | Size |
|---|---|---|
| Classes | `Content/Classes/clientconfig.json` (`classes[]`) + one loadout JSON per class | **19 live classes**: Archer, Ninja, Alchemist, Gladiator, Paladin, Fisherman, Black Mage, Psychic, White Mage, Miner, Fish, Clown, Phoenix, Tiki Priest, Tree, Mutant (Rush/Regen stances), Leech, Astronaut, Rng Man. Loadouts also exist for Beast, Tank, Jungleman and Builder, but those classes aren't in the config. |
| Class stats | loadout JSON: `HealthPoints`, `ManaPoints`, `BonusDefense`, `BonusMoveSpeed`, `BonusRunAcceleration`; config: `respawn-time`, `buffs[]` (permanent vanilla buff IDs), `health-from-kills`, `ability-id`, `upgrades` | 100–200 HP, 1–28 mana |
| Abilities | `Content/Classes/Abilities.cs` (1,932 lines, one big `switch` on `AbilityID`) and `AbilitiesRewrite/` (an unfinished refactor) | 1–3 abilities per class, plus passives |
| Custom items | `Content/Items/*.cs` | ~35 ModItems: Shardstone Pickaxe and its upgraded version, Rancor (charged bow), Zen, Space Splitter (hook), Foliage Tendrils (hook), Amalgamated Hand (Sunfury-style flail), Gladiatorial Javelin and Polearm, Tesla Cannon, Particle Carbine, Hellwing Tome, Staff of Winter, King Fisher, Fisherman's Anchor, Magic Geode, Doll of the Dark Arts, accessories (Blessing of the Dragons, Planetary Exploration Gear, Taboo Artifact, Hexagonal Barrier), and more |
| Vanilla items used | loadout JSONs, by Terraria item ID | ~45 weapons and tools, ~40 accessories, ~45 armor pieces. Many slots are vanity or dyes. |
| Balance overrides | `Overrides/UpdatedWeps.cs` (`GlobalItem.SetDefaults`, `UpdateEquip`), `UpdatedProjectiles.cs`, `GrenadeOverrides.cs`, `UpdatedBuffs.cs`, `UpdatedMounts.cs` | Per-item changes to damage, use time and speed; armor defense and mana nerfs |
| Buffs | `Content/Buffs/*.cs` plus many vanilla `BuffID`s | 18 custom buffs (Haste, Immortality, Rising Sun, HyperRegen, Netted, Transmutated, Glory, Resiliance, Retaliation, …) and about 30 vanilla ones (Swiftness, Invisibility, Weak, Silenced, Regeneration, Cursed, Electrified, Slow, Chilled, Webbed, Chaos State, Potion Sickness, …) |
| NPC-like entities | `TikiTotem.cs`, `BeastMob.cs`, `SlimerClone.cs` | Team-owned summons |
| Blocks | `Overrides/UpdatedBlocks.cs`, `Functionality/ProtectedRegion.cs`, `BlockVariance.cs`, `TealCurrentTile.cs` | Map tiles can't be broken. Only dirt, mud, grass, breakable ice, rope, bubble and tile 127 can be mined, placed or exploded. You can't replace an existing block. Some regions are protected. Players are refilled with 300 dirt (mud for Ninja). |
| Maps | `Content/MapSaves/*.json` | **21 maps**, each a `[row][col]` grid of `{TileType, WallType, TileColor, WallColor, HalfBlock, Slope, LiquidAmount, LiquidType, wires/actuators}`. They import directly into a Unity Tilemap. |
| Match flow | `ServerSide/GameManager.cs`, `Gem.cs`, `GameTeam.cs`, `GameMap.cs` | Class select (30 s pubs / 2 min scrims, ends early when everyone has picked) → 10-minute match → overtime. In overtime a team wins when it is ahead on captures and its own gem isn't held. A team also wins with a 2+ capture lead. Gem hitbox is 6×9 tiles. A firework fires every 2 s while a gem is carried. |
| Competitive layer | `Hooks/Hooks.cs`, `NeatQueueTeamAssignmentSystem.cs`, `ClientSide/BanUI.cs`, `ScrimsData/`, `PvPHub/`, `Reese/`, `Discord/` | Best-of-3 scrims, captain bans (3 min), NeatQueue roster, stats upload, match recording, Discord integration |
| Admin and tooling | `Content/Commands/*` (41 files), `ModAdminUI.cs`, `MapSaveCommand.cs` | Kick, ban, mute, TP, timers, map roll, give, godmode, map capture |
| Networking | `CTG2.cs` `MessageType` enum | **135 custom packet types**, hand-synced |

### 1.2 Class and ability reference (from `Abilities.cs`)

Cooldowns are the values hardcoded in `PostItemCheck` (`SetCooldown*`). **These disagree with `ability-cooldown` in `clientconfig.json`** (for example Ninja is 45 s in code and 10 s in config). Pick one source of truth when you port.

| Class | HP / Mana | Weapons (loadout) | Ability 1 | Ability 2 / 3 / passive |
|---|---|---|---|---|
| Archer | 140 / 1 | Rancor (custom charged bow), Hellfire Arrows | 23 s: empowers shots (see `class1UsedLuminite` handling in `PostItemCheck`) | A2 40 s: Haste for 4 s |
| Ninja | 160 / 15 | Tragic Umbrella, Zen | 45 s: 6 s Invisibility + Swiftness + Weak + Silenced, and temporarily equips Soaring Insignia + Magiluminescence. Taking damage cancels it. | A2 22 s: throws Rope Coil. Passive: standing still for 1 s grants Invisibility. |
| Alchemist | 160 / 1 | Space Splitter (hook), Magic Geode | 25 s (shared): throws Love Potion | A2 (shared 25 s): Foul Potion. A3 46 s: Apprentice Staff T3 shot (15 damage). |
| Gladiator | 140 / 25 | Gladiatorial Javelin, Gladiatorial Polearm | 35 s: 5 s Well Fed II + Withered Armor + Gold imbue + Gel Balloon, followed by a 2 s crash (Slimed, Slow, Withered Armor) | A2 6 s: power shot on the next javelin |
| Paladin | 160 / 11 | Water Bolt, Ghastly Glaive, Sergeant United Shield (+4 defense while blocking) | 38 s: 2.5 s Immortality + Slow + Chilled, and a temporary Cobalt Shield (knockback immunity), then 1 s of buff 160 | — |
| Fisherman | 150 / 1 (+3 def) | Fisherman's Anchor, King Fisher, Atlantic Cod | 6 s: fires a Fish Hook projectile | Passive: +2 Atlantic Cod every 60 s |
| Black Mage | 160 / 28 | Demon Scythe, Doll of the Dark Arts | 42 s: buffs 206, 137 and 320 for 7 s, and +7 mana | — |
| Psychic | 160 / 17 (−2 def, +0.1 accel) | Thunder Zapper, Nebula Arcanum | 40 s: long-duration buffs 178, 196, 117 and 7, plus a Chaos State lockout. While active, each mana use costs 20 HP and healing is blocked. | — |
| White Mage | 160 / 18 | Ice Rod, Staff of Winter, Frost Staff | 30 s: Regeneration on self for 5 s; allies in radius get buff 137 (5 s) and buff 206 (8 s) | — |
| Miner | 160 / 1 | Sticky Bomb, Sticky Grenade | 15 s: digs a 14×7 block area of dirt, mud and grass toward the mouse | A2 6 s: Bouncy Grenade (10 damage), self-Cursed for 0.35 s. Passive: +1 Sticky Bomb every 20 s. |
| Fish | 150 / 1 | Golden Shower (reworked: 35 damage, no mana) | 35 s: buffs 1, 104 and 109 for 2 s | — |
| Clown | 120 / 10 | Amethyst Staff (reworked) | 30 s: after 1 s, swaps positions with the nearest valid target in radius. Self-Electrified and Withered Weapon for 1 s. | Permanently potion-sick |
| Phoenix | 160 / 15 | Daybreak, Flamelash, Sleepy Octopod | 45 s, needs more than 40 HP, costs 40 HP: Nebula Mana + Star in a Bottle for 2 s, Rising Sun for 4 s | — |
| Tiki Priest | 150 / 1 | Staff of Earth, Blowgun | 27 s: spawns a team-owned Tiki Totem NPC (`TikiTotem.cs`) | — |
| Tree | 150 / 18 | Wand of Sparking, Bananarang, Foliage Tendrils (hook) | 32 s: 3 Toxic Clouds that Daze and Ogre-Spit enemies | A2 10 s: detonates your own Bananarangs, Emerald Bolts and Toxic Clouds into Spore Clouds (22 damage) |
| Mutant | 100 / 1 | Rush: Amalgamated Hand. Regen: The Rotted Fork. | 1 s: swaps between the Rush and Regen loadouts. Freezes you for 0.5 s and applies potion sickness for 20 s. | — |
| Leech | 140 / 1 | Cursed Flames, Chain Knife | 40 s: buff 320 for 5 s; hits from projectiles 273 and 304 heal 10% of damage | — |
| Astronaut | 140 / 6 (+8 def) | Tesla Cannon, Particle Carbine, Planetary Exploration Gear | 32 s: Electrosphere Missile (18 damage) | A2 45 s: Magic Power + Electrified for 7 s. Passive: mana regen is locked. |
| Rng Man | 200 / 50 | Flintlock Pistol, Universal Crafting Item | 5 s: gives a random non-banned item stack | Passive: hitting an enemy gives a random item |

Every class also gets: a Shardstone Pickaxe (tier varies), Lesser Healing Potions ×99, Lucky Horseshoe (no fall damage), the Whoopie Cushion (used as the ability-1 trigger item), the Paint Sprayer, and vanity and dye cosmetics.

---

## 2. Decisions to make before porting gameplay

These shape everything else. Changing them later means rewriting what's built on top.

### 2.1 Networking: choose now, build on it from the first gameplay system

CTG2 inherits Terraria's model. Clients simulate themselves and report what happened (hits are resolved in the victim's `OnHurt`, gem pickup is sent by clients as `UpdateIsHeld`), and the server relays it. That model is why the mod needs 135 packet types and is easy to cheat in. A standalone competitive 5v5 game should be **server-authoritative with client-side prediction for the local player**.

- **Recommended: Fish-Net** (free, has built-in client prediction/reconciliation and lag compensation, suits a 10-player dedicated server).
- Alternatives: **Netcode for GameObjects** (first-party, but you'd have to write prediction yourself), or **Photon Fusion** (excellent for competitive play, but paid and hosted by Photon).
- Add the networking package and stand up "two clients see each other move" **before** porting combat. Every later system then gets written as server logic plus replicated state, instead of being retrofitted.

### 2.2 Simulate on a fixed 60 Hz tick in Terraria units

Every number in CTG2 and in Terraria is in **ticks** (1/60 s) and **pixels per tick**, with 16 px per tile.

- Set `Time.fixedDeltaTime = 1/60` and run all gameplay (movement, cooldowns, buffs, projectiles) in a network tick or `FixedUpdate`, not in `Update`. **Today's `PlayerMovementAbility` and `MovementModule` run in `Update` with `Time.deltaTime`, so they need to move.**
- Add one `TerrariaUnits` helper (`PxPerTickToUnitsPerSec(v) = v * 60 / 16`, `TicksToSeconds(t) = t / 60`). Then CTG2 values like `SetCooldown(23)`, `useTime = 20` and `shootSpeed = 13.8f` can be pasted in unchanged.
- 1 tile = 1 Unity unit (the scene grid is already about 1.0). The player is 3 tiles tall (done in the movement rework).

### 2.3 Data-driven content keyed by Terraria IDs

Don't hand-write one class per weapon. Mirror Terraria's own data model:

- `ItemDef` (ScriptableObject) holds `terrariaId`, `damage`, `damageClass`, `useTime`, `useAnimation`, `useStyle`, `knockBack`, `crit`, `mana`, `autoReuse`, `shoot` (→ `ProjectileDef`), `shootSpeed`, `useAmmo`/`ammo`, `pick` power, `createTile`, `defense`, `accessoryEffects[]`, and `holdEffects[]`.
- `ProjectileDef` holds `terrariaId`, `aiStyle` (→ behaviour component), `width`/`height`, `penetrate`, `timeLeft`, gravity, tile collide, `friendly`, and `onHitBuffs[]`.
- `BuffDef` holds `terrariaId`/custom id, `isDebuff`, `statModifiers` (move speed, defense, damage, regen, …), and `flags` (cannot use items, invisible, frozen, …).
- `ClassDef` holds HP, mana, bonuses, loadout (`ItemDef` per slot), permanent buffs, respawn time, `AbilityDef[]` and upgrades.
- **Bake CTG2's `UpdatedWeps.cs`, `UpdatedProjectiles.cs` and `UpdatedBuffs.cs` overrides into these assets**, so the shipped number is the CTG2-balanced number.
- Write editor importers: **class JSON + `clientconfig.json` → `ClassDef`**, and **map JSON → Tilemap**. The data you already have then does most of the work.

### 2.4 Projectile behaviour by Terraria AI style, not per item

Terraria groups projectiles into about 15 `aiStyle`s that matter here: straight/bolt, arrow (gravity after a delay), boomerang (Bananarang), hook, explosive (sticky bomb and grenades, bouncy), spear/shortsword (Ghastly Glaive), flail (Sunfury → Amalgamated Hand), magic missile (mouse-steered), lingering cloud (Toxic/Spore cloud), rope coil, fishing bobber/hook, and laser/beam (Tesla).

Implement one behaviour component per style and parameterize it from `ProjectileDef`. Most of the roughly 60 projectiles become data.

### 2.5 What to do with BuildingBlocks

- **Keep:** `BuildingBlocksCharacter` as the player root, the `StatsModule` (extend `StatType` with Defense, LifeRegen and ManaRegen, or move to a dedicated stats block), the `CharacterAbility` input-binding pattern, `ProjectilePool`, and the GameFeel and HUD pieces.
- **Replace or extend:**
  - Targeting by tag → a `Team` component (Red = 1, Blue = 3, Spectator = 0, as in CTG2), with friendly-fire rules.
  - `DamageInfo` → add crit, damage class, source item and projectile ID (for kill credit and on-hit effects like Leech and Rng Man).
  - `HitReaction` → Terraria-style player knockback plus immunity frames.
- **Remove from the Player:** the stray Jump, Dash and Enemy example abilities (already done), and the single-player pieces (`WinDoor`, `EliminationZone` semantics).

### 2.6 Licensing (important)

The mod could use Terraria's sprites, sounds, item names and IDs because every player owns Terraria. **A standalone Unity game can't ship Re-Logic's assets.** Use Terraria IDs and wiki behaviour as a design reference only. Plan original art and audio for every item, projectile, armor set and tile, or reuse only the custom art CTG2 made itself. Also rename vanilla-derived items in the shipped game.

---

## 3. Mapping tModLoader concepts to Unity

| tModLoader / CTG2 | Unity equivalent |
|---|---|
| `ModPlayer` (`ClassSystem`, `Abilities`, `PlayerManager`, `DashInputPlayer`) | Components on the networked Player prefab: `ClassLoadout`, `AbilityController`, `PlayerMatchState`, `TerrariaMovement` |
| `ModItem` / `GlobalItem.SetDefaults` | `ItemDef` asset (overrides baked in) |
| `ModProjectile` / `GlobalProjectile` | `ProjectileDef` asset + AI-style behaviour component, pooled and server-spawned |
| `ModBuff` / `BuffID` / `Player.AddBuff` | `BuffDef` + `BuffController` (server-owned, replicated list of id and remaining ticks) |
| `GlobalTile.CanKillTile` / `CanPlace` / `CanExplode` | `TileDef` flags (breakable, placeable, explodable) + `WorldRules` + protected-region volumes |
| `ModSystem` `GameManager` (`PostUpdateWorld`) | Server-only `MatchManager` NetworkBehaviour driving a state machine |
| `Main.player[]`, `whoAmI` | Network player registry / connection id |
| `ModPacket` + 135 `MessageType`s | Replicated state (SyncVars), server RPCs for input and intent, client RPCs for cosmetic events. Most of the 135 packets go away. |
| `Player.statLife` / `statLifeMax2` / `ModifyMaxStats` | Server-owned stats with modifiers recomputed each tick from class, equipment and buffs (like Terraria's `ResetEffects` → `UpdateEquips` → `PostUpdateEquips` pipeline) |
| `Main.MouseWorld` | Aim vector sent with the input each tick |
| `SoundEngine.PlaySound(SoundID.*)` | Audio event IDs → your own clips |
| `Mod.GetFileStream("…json")` | Imported ScriptableObjects (or Addressables) |
| Commands (`ModCommand`) | Server console/admin RPC layer |

Things in CTG2 **not** to carry over:
- Client-side hit resolution.
- Clients announcing gem pickups.
- HP clamping inside `CanUseItem` (Psychic).
- Item-swapping in armor slots to fake effects (Ninja and Paladin abilities add and remove accessories). Use buffs that grant the same effect instead.
- The `ResetEffects` comment that reads "THIS METHOD IS BROKEN".

---

## 4. Phased roadmap

Each phase lists what to build and how to know it's done. Sizes are relative: S is days, M is about 1–2 weeks, L is several weeks.

### Phase 0: Foundations (M)
1. Add the networking package (2.1). Set up host/client/dedicated-server boot and a lobby scene.
2. Fixed 60 Hz tick and the `TerrariaUnits` helper (2.2).
3. Networked Player prefab spawned per connection, with a `Team` component.
4. Content folders and empty `ItemDef`, `ProjectileDef`, `BuffDef` and `ClassDef` types (2.3).

**Done when:** two clients and a server build all see each other move, using the existing Terraria-style walk and jump on the fixed tick.

### Phase 1: Finish Terraria player physics (M)
The base walk and jump is in place (`PlayerMovementAbility`). Remaining work:
- Move it to the network tick with prediction and reconciliation.
- **Movement modifiers pipeline:** `moveSpeed`, `runAcceleration`, `maxRunSpeed` and `jumpSpeedBoost` multipliers recomputed each tick from class bonuses (`BonusMoveSpeed`, `BonusRunAcceleration`), accessories and buffs.
- **Extra jumps:** Cloud, Tsunami and Blizzard in a Bottle, each with its own Terraria height and duration.
- **Flight and hover:** Soaring Insignia (infinite flight time while it's equipped), Flying Carpet (hover), and wing-style flight time if needed.
- **Dash:** Shield of Cthulhu (CTG2 binds a dedicated Dash key, see `DashKeybind/`), plus Tiger Climbing Gear wall slide and wall jump.
- **Speed accessories:** Anklet of the Wind, Aglet, Magiluminescence (acceleration), Flurry, Flower and Ice Skates boots, Frog Leg (jump boost and fall), Step Stool.
- Fall damage, which Lucky Horseshoe negates. Every class has it, so you can skip fall damage entirely.
- Auto step-up over 1-tile ledges, half-blocks and slopes.
- **Liquids:** water (slow movement), lava (damage, with Lava Charm and Obsidian Rose).
- Teal Current tile (speed boost) from `TealCurrentTile.cs`.

**Reference:** decompiled `Player.Update` / `Player.HorizontalMovement` / `Player.JumpMovement` (tModLoader source), and wiki pages for each accessory.
**Done when:** each class's mobility loadout feels like CTG2 in a side-by-side test.

### Phase 2: World, tiles, mining and building (L)
1. **Map importer:** CTG2 map JSON → Tilemap layers (solid, wall, liquid) with half-block and slope support. Map Terraria `TileType` IDs to your own `TileDef`s. Only the tile types those 21 maps actually use need defining.
2. **Tile rules** from `UpdatedBlocks.cs`:
   - Only dirt, mud, grass, breakable ice, rope, bubble and tile 127 are breakable.
   - Only those can be placed, and never onto an existing block.
   - Protected regions block placement and explosions.
   - Walls are never exploded.
3. **Pickaxe:** pick power, tile HP and mining speed from `useTime`/`useAnimation`. Each Shardstone tier is a separate `ItemDef`. Toolbox gives extra range.
4. **Block placing:** placement range, cursor/smart-cursor, 300 dirt (mud for Ninja), and the refill timer from `ClassSystem.PostUpdate`.
5. **Explosions** (sticky bombs and grenades) destroy only explodable tiles.
6. **Server-authoritative tile edits:** the client sends an intent, the server validates and applies it, and the change is replicated as a delta. Late joiners get a full snapshot.
7. **Map rotation:** `MapTypes`, queue, random pick, and lava preload.

**Done when:** all 21 maps load, and players can mine and build exactly what CTG2 allows, in sync across clients.

### Phase 3: Combat core (L)
1. **Stats:** max HP and mana from the class, defense from armor + accessories + `BonusDefense`, and the CTG2 armor nerfs (`UpdatedWeps.UpdateEquip`).
2. **Damage pipeline (server):**
   - base damage × class/buff multipliers
   - ±15% variance (Terraria `Main.DamageVar`)
   - crit ×2 (most CTG2 weapons set `crit = 0`)
   - minus defense × 0.5 (Terraria's normal-mode player defense factor)
   - minimum 1
   - **Verify every constant against decompiled `Player.Hurt` / `Main.DamageVar` before tuning.**
3. **Immunity frames** after PvP hits, and **knockback**. Terraria applies a mostly fixed velocity impulse to the victim in the hit direction, cancelled by knockback immunity (Cobalt Shield, Paladin ability). Check the exact values in `Player.Hurt`.
4. **Death and respawn:**
   - Per-class `respawn-time` (2–12 s), spawning at the team base.
   - Drop the gem on death.
   - Kill credit, assists, and damage dealt/taken stats (`PlayerManager`).
   - `health-from-kills` (Gladiator +10).
5. **Regeneration:**
   - Terraria natural life regen (ramps with time since last hit; faster standing still), and `lifeRegen` from buffs, Regeneration and Charm of Myths.
   - Healing potions: Lesser Healing Potion +50 HP, Potion Sickness 60 s, 45 s with Philosopher's Stone or Charm of Myths.
   - Mana regen with delay (Mana Regeneration Band, Mana Flower, Magnet and Arcane Flower).
   - Class rules: Astronaut's locked mana regen, and permanent sickness for Clown and Mutant.
6. **Buff system:** a generic `BuffController`. Port the ~30 vanilla buffs CTG2 uses as `BuffDef`s, then the 18 custom ones. Include CTG2's banned-buff clearing (Ichor, Poisoned) and permanent class buffs reapplied on respawn.

**Done when:** two players can fight with a placeholder weapon and the server-reported damage, knockback, death, respawn and healing match CTG2 numbers.

### Phase 4: Weapons and projectiles (L, ongoing)
1. **Item use loop** like Terraria's `ItemCheck`: `useTime` vs `useAnimation`, `autoReuse` (Feral Claws), mana cost, ammo consumption (Endless Quiver and Musket Pouch), and `useStyle` (Swing, Shoot, Thrust, Hold).
2. **Melee:** swing arcs with hitboxes scaled by `scale` (pickaxes, Mace, Chain Knife), and thrust (Ghastly Glaive, polearm).
3. **AI-style behaviours** (2.4), then the projectiles per class, in this order:
   1. Shared: pickaxe swing, potions.
   2. Mid-complexity class kits: Archer (Rancor charge bar, `ChargeBowBarSystem.cs`), Leech, Fish, Clown, White Mage, Black Mage.
   3. Throwables and explosives: Miner (sticky bombs and grenades, no self-damage per `GrenadeOverrides`).
   4. Complex: Amalgamated Hand (flail, alt-fire variants), Bananarang (boomerang), Tesla Cannon (beam), Hellwing Tome, Zen, Tragic Umbrella, Daybreak, Nebula Arcanum, Thunder Zapper, Staff of Earth, Blowgun, Golden Shower (arc).
4. **On-hit effects:** debuffs from projectiles, lifesteal (Leech), random items (Rng Man), and colour-coded team projectiles (`ColorCodedProjectiles.cs`).
5. **Lag compensation:** the server rewinds the victim's hitbox to the shooter's view time for hit tests. Fish-Net and Fusion provide this.

**Done when:** every weapon in the 19 loadouts hits for its CTG2 damage and plays like it does in-game.

### Phase 5: Grappling hooks (M)
- Port Terraria's hook AI style: fire toward the cursor, travel to max range, latch onto solid tiles, pull the player at pull speed, release on jump or re-press, handle multiple hooks, and detach on damage where applicable.
- Items to port:
  - Space Splitter (Queen Slime Hook clone)
  - Foliage Tendrils (custom chain and texture)
  - Ninja's Rope Coil (places rope tiles)
  - Fisherman's Fish Hook ability projectile
- Hooks interact with the movement prediction, so build them on Phase 1's networked movement.

**Done when:** hooking and pulling are smooth with 100 ms of simulated latency.

### Phase 6: Armor and accessories (M)
- An `EquipmentController` with armor slots (3) and accessory slots (7, `extraAccessory` is on). Recompute modifiers each tick, as in Terraria's `UpdateEquips`.
- Port only the functional pieces from the loadouts. Vanity and dyes are cosmetic, so leave them for skins.
- Custom accessories: Blessing of the Dragons, Planetary Exploration Gear (with its bar UI, `ExplorationGearBarSystem.cs`), Taboo Artifact, Hexagonal Barrier.
- Shield blocking (Sergeant United Shield: +4 defense on right-click).

### Phase 7: Classes and abilities (L)
1. A `ClassDef` importer from the 19 configs and loadout JSONs.
2. **Class select:** UI (`ClassUI.cs`), loadout application on spawn, upgrades (the `upgrades` arrays; most code is commented out, so decide what's in scope), and class bans for scrims.
3. **Ability framework:** an `AbilityDef` with cooldown in ticks, a key slot (Ability 1, 2 or 3, rebindable), activation conditions (not Webbed, alive, active state, Phoenix's HP check), and server-side execution. A client ability-ready cue plays the custom sounds (`AbilityReady.mp3`, …).
4. **Port abilities using the table in 1.2.** Rough order:
   1. Pure buff abilities: Archer A2, Gladiator, Paladin, Black Mage, Fish, Phoenix, Leech, Astronaut A2.
   2. Projectile abilities: Alchemist ×3, Ninja A2, Miner A2, Fisherman, Astronaut A1.
   3. World and target abilities: Miner A1 dig, White Mage team heal, Tree clouds and detonation, Clown swap.
   4. Systemic abilities: Ninja invisibility and passive, Psychic HP-for-mana, Mutant stance swap, Tiki Totem summon, Rng Man random items.
5. Finish the `AbilitiesRewrite/Ability.cs` idea properly: one class per ability instead of the `switch`.

**Done when:** all 19 classes are playable with their full kits.

### Phase 8: Capture the Gem game mode (M)
- `MatchManager` state machine (server): Lobby → ClassSelection (30 s, or 2 min for scrims; ends early when everyone has picked) → Active (10 min) → Overtime → End (stats board) → next round.
- **Gems:**
  - One per base, with a 6×9-tile trigger.
  - Pickup by an enemy (server-detected). Carry with a visual (`GemDrawLayer.cs`) and a firework every 120 ticks.
  - Drop on death, disconnect or spectate (CTG2 resets it to base).
  - Capture by touching your own gem area.
- **Win rules:**
  - A 2+ capture lead wins.
  - At 10 minutes, overtime starts. A team wins when it is ahead and its own gem isn't held.
  - Timer edits (`SetTimerCommand`) can cancel overtime.
- Team enforcement, base spawns, and spectators with spectator TP.
- HUD: scores, gem carrier names, timer, team sizes, damage board (`DamageBoardUI.cs`), and the marked indicator.

**Done when:** a full 5v5 match plays start to finish on a dedicated server.

### Phase 9: Competitive and ops layer (M–L)
- Scrims: NeatQueue roster join → captains → 3-minute ban phase → best of 3 (`Hooks.cs`, `NeatQueueTeamAssignmentSystem.cs`).
- Accounts and auth (replaces `ctg2-auth`, the login commands and `SteamAuth.cs`). Steam is the natural fit for a standalone game.
- Stats tracking and upload (`ScrimsData`, `PvPHub`), match recording (`Reese`), Discord rich presence and bot hooks (`Discord/`).
- Admin tooling: kick, ban, mute, pause, timers, map roll, godmode, give (`Commands/`).
- **Anti-cheat largely comes for free from server authority.** Movement-speed validation and input-rate checks cover most of the rest.

### Phase 10: Content, art and polish (ongoing, parallel)
- Original sprites and animation for the player (3-tile height), items, projectiles, tiles and backgrounds (see 2.6).
- Audio (CTG2's custom sounds are reusable; replace Terraria's `SoundID`s).
- VFX through the existing GameFeel components, music system (`Content/Music`), outlines (`PlayerOutlines.cs`), and settings (`ClientConfig`).

---

## 5. Suggested order of work and first milestones

1. **Milestone A, "Two players on a CTG2 map" (Phase 0, Phase 1 core, Phase 2 import):** networked movement on one imported map (`classic.json`).
2. **Milestone B, "First blood":** Phase 3 and one weapon (Archer's Rancor) and one hook. This proves damage, knockback, respawn and lag compensation.
3. **Milestone C, "Playable CTG":** Phase 8 with 4–5 classes (Archer, Miner, Paladin, White Mage, Ninja). This covers buffs, projectiles, world edits, team heals and invisibility, which exercises most systems.
4. **Milestone D, "Feature parity":** the remaining classes, weapons and accessories (Phases 4–7).
5. **Milestone E, "Competitive":** Phase 9.

## 6. Open questions to settle early

- Which networking stack (2.1)? Everything else waits on it.
- Ability cooldowns: use the code values or the `clientconfig.json` values?
- Which of Beast, Tank, Jungleman and Builder (loadouts without config entries) are meant to ship?
- Do upgrades (Cloud in a Bottle swap, Simple Dash, extra jump) stay in?
- How strictly should you match Terraria's numbers versus retuning for the new engine? I recommend porting exactly first and tuning second, behind data assets.
- Hosting: dedicated servers (and where), plus matchmaking/queue integration outside Discord.
