# Refactor roadmap

Refactors are not needed up front. Each one below is worth doing only when its trigger is met. After you add assets or features, Claude re-checks this list and says in a line or two if a trigger is now met (which refactor, why now, rough size). Nothing starts unless you ask.

Last updated 2026-11-23. Claude's own copy lives in its memory (`refactor-roadmap.md`); keep the two in step.

## Done

- **Player registry and `MatchContext`:** no more singletons for the clock, projectiles, economy or the registries of players and hittable things.
- **Presentation events:** the simulation raises text and dirt-burst events on `MatchContext.Presentation`; `EffectsPresenter` draws them.
- **`PlayerView` split:** `PlayerCharacter` (about 578 lines) no longer draws or imports Rendering/UI. `PlayerView` draws the body, held item, bow, offering orb and hooks.
- **`PickaxeSnap.cs` deleted:** it was unused (`SmartCursor` replaced it).
- **Class art system:** `ClassSpriteSet` plus a layered character sheet template (48 x 56 cells, 16 pixels per tile, point filtering), composed by `CharacterSprite` with a team outline and mask. The archer currently ships a single idle frame, so every state falls back to idle; the bow and swing rigs stay dormant until real art exists.

## Open, in the order to do them

Trigger in brackets.

1. **Finish decoupling the sim from views** [before assembly definitions or any server/headless work, or when mob, altar, projectile or dummy art is replaced]
   Still draw themselves: `Drakon`, `ElderDrakon` (sprites, bars, hitbox size from `MobArt`), `MobDirector` (spawn marker, boss bar and countdown in IMGUI), `Altar`, `TargetDummy`, `ProjectileSystem` (sprite renderers for projectiles and blasts). Same pattern as `PlayerView`: a view reads public state, and hitbox sizes move into data. `GameBootstrap` stays as the client composition root.

2. **Assembly definitions** [after item 1, when starting the headless simulation or C++ server mirroring]
   Plan: `CTG.Sim` (Core, World, Combat, Items, Economy, Classes, Mobs, Gameplay, Input interfaces), `CTG.Client` (Rendering, UI, GameBootstrap) and `CTG.Editor`. Do it as its own step; watch the `KeyBindings` statics in Input.

3. **Art lookup by id** [when the first real item, projectile, mob, tile or altar art arrives]
   `PlaceholderSprites` builds sprites from ad-hoc string keys (Pickaxe, Bow, Arrow, Potion, Ward, Dummy, Solid...). Replace with an id to sprite lookup (`ItemId`, `ProjectileId`, mob kind) with the placeholder as fallback, so real art drops in without code edits. Characters already have this through `ClassSpriteSet`.

4. **Data-driven items** [around the 4th or 5th new item kind, or when items are authored outside code]
   `ItemDef` is one class with fields for every kind (swing, bow, throw, tile), and `ItemId` is an enum with switches. Move to per-use-style data (ScriptableObject or JSON the server can also read).

5. **Economy and abilities as definitions** [3rd new artifact, spell or ability, or a second class kit]
   `SpellId` and `ArtifactId` are enums with name/behaviour switches (`ShopCatalog.SpellName`, `HotbarHud` checking `SpellId.HealingPotion`, a `SpellBook` cooldown array sized by the enum, `PlayerCharacter.CastSpell`). `AbilityFactory` is a switch with an `ArcherKit` downcast, and passive class effects are ad hoc. Make them defs with effects so the shop, HUD and modifiers read the def.

6. **One UI layer** [any new menu or HUD screen]
   All UI is IMGUI (`OnGUI`) with hand-placed rects in about ten files, including `MobDirector`, `BarrierView`, `MatchHud`, `HotbarHud`, `ScoreboardUi` and `CombatText`. Move to UI Toolkit or uGUI before the HUD grows.

7. **Central balance data** [first balance pass, or when the server needs the same numbers]
   Tuning is consts and serialized fields spread over classes. Known mismatch against the original mod's config: ability cooldowns in code disagree with `ability-cooldown` in its `clientconfig.json`; pick one source of truth.

8. **EditMode tests for the simulation rules** [before server work, or after the first rule regression]
   Everything is currently checked with ad-hoc Unity MCP scripts. First targets: `CombatRules` (damage, knockback), economy kill and assist shards, `SimulationClock` ordering, `MatchContext` teardown, map JSON round trip.

9. **Projectile abstractions** [when adding a new motion type such as homing, or a new end effect]
   Queued earlier: movement as a strategy (straight/gravity, boomerang), the hit rules grouped into one struct, end effects as a list with triggers, and one time unit (lifetimes and gravity delay in ticks or seconds, replacing `ExtraUpdates` with real speed plus sub-stepping). The time unit change was recommended first.

10. **Small leftovers** [whenever touching the file]
    - `PlayerCharacter` still holds the dev-key events (`DevEndMatchRequested`, `DevSkipOvertimeRequested`), spell and progression glue, and the `Presentation` accessor.
    - The swing-arm code (`PoseSwing`, `SwingReach`, `SwingAngle`) in `CharacterSprite` is dormant until the archer has rig art.
    - A per-frame head offset layer for animation is an art-system idea, not a refactor.

## Rules for every step

- One refactor per step: compile, play-test, update this file; never bundle with feature work.
- After entering Play Mode wait about 10 to 15 seconds before polling; leave Play Mode before refreshing scripts.
- Keep the base game's name out of code, comments, tooltips and logs.
- Commit only when asked.
