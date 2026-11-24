# Character animation style guide (minimal, chunky pixel style)

Reference only: the look is a small, bold, low-frame-count pixel style like the one in the base game CTG2 was a mod of. Draw everything originally; never reuse its sprites, and keep its name out of code, comments and tooltips.

## Principles
- Few frames, strong poses. Every frame must read at actual size (about 36 x 48 px on screen at 1x).
- Drawn in 2 x 2 pixel blocks (an 18 x 24 grid scaled 2x). One block = one "art pixel". Never use 1 px details.
- Facing right only; the game mirrors for facing left. The silhouette must change between poses.
- Motion is shown by moving big shapes (legs, arms, one block of head or chest bob), not by redrawing detail.
- Body fits 36 x 48 px, feet on a fixed row; anything that sticks out (quiver, cloak) may use the rest of the 48 x 56 cell.

## Sheet format (what the renderer loads)
- One PNG per layer (base body, class outfit, optional team mask), cells 48 x 56 px, 8 per row, one row per animation, a row ends at its first empty cell. All layers share the layout. Imported at 16 px per tile, point filtering.
- Rows, top to bottom: Idle, Run, Sprint, Land, JumpUp, Fall, DoubleJump, FastFall, Draw, DrawAir, Release, Swing, Hit, RigBody, RigParts.
- Template with the grid, 36 x 48 body area, ground line, feet point (24, 2) and arm/hand joint marks: `Docs/art-mockups/character_sheet_template.png`.
- Empty rows fall back to a related one (sprint to run, fall to jump up, double jump to jump up, fast fall to fall, air draw to draw, anything to idle), so animations can be added a few at a time.

## Animations (frames, playback, what to draw)
| Row | Frames | Speed | Draw |
|---|---|---|---|
| Idle | 2 | 6 fps loop | Standing. Frame 2 is 1 block lower (breathing). Head and shoulders move, feet do not. |
| Run | 6 to 8 (14 is the classic count) | 12 fps loop | Leg cycle: contact, down, passing, up for each leg. Torso bobs up 1 block on the passing frames. Arms swing opposite to the legs. Lean forward slightly. |
| Sprint | 0 to 2 | 14 fps loop | Optional. Same cycle, more lean. Empty = Run. |
| JumpUp | 1 | 10 fps | Rising: legs tucked, arms up. One strong pose, no animation. |
| Fall | 1 | 10 fps | Dropping: legs hanging, arms slightly out. Used for any airborne non-rising state. |
| DoubleJump | 1 to 2 | once | Optional: a quick tuck or flip. Empty = JumpUp. |
| FastFall | 1 | 10 fps | Optional: body narrow, arms up. Empty = Fall. |
| Land | 1 | 0.12 s, once | Optional squash: knees bent, 1 block lower. Only after a fall longer than 0.2 s. |
| Hit | 1 | during post-hit immunity | Optional: leaning back, head tilted. |
| Swing | 3 to 4 | follows the swing | Pickaxe and block placing: windup, downswing, follow-through. Progress picks the frame. |
| Death | 2 to 4 | 12 fps, once | A separate burst effect left where the player died; any size, feet at the pivot. |

## The bow (a jointed rig, not drawn frames)
- RigBody row (4 cells, no arms): Ready, Braced (draw under way), AirRising, AirFalling.
- RigParts row (8 cells): near and far upper arm and forearm (pointing right, pivot on the joint at (8, 28)), then bow-grip hand, string-pinch hand, release hand, reaching hand (pointing right, pivot on the hand point (24, 28)).
- The game aims the arms at any angle with two-bone IK. Draw parts as plain straight shapes; snapped to whole pixels, they rotate in 2 x 2 steps.
- Shoulder positions, arm lengths, bow pivot, grip distance, string rest and full-draw distances and the quiver point are tuned on the class's asset (Resources/ClassArt/Archer.asset).

## Minimum to start
Idle 1 frame, Run 6 to 8, JumpUp 1, Fall 1, the 4 rig bodies, 4 arm pieces and 4 hands. Everything else can fall back until drawn.

## Not done by drawing
Team colour: a greyscale team-mask layer (eyes, scarf) tinted at runtime, plus an outline. Hit flicker, burning flicker and facing flip are done in code.
