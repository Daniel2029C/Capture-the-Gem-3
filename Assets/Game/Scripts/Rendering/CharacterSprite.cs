using System.Collections.Generic;
using CTG.Core;
using UnityEngine;

namespace CTG.Rendering
{
    /// <summary>What the character is doing this frame, for picking the pose.</summary>
    public struct CharacterPose
    {
        /// <summary>The class has art and the character should be drawn with it (false = hidden or placeholder).</summary>
        public bool Visible;
        public bool Alive;
        /// <summary>+1 facing right, -1 facing left.</summary>
        public int Facing;
        public bool Grounded;
        /// <summary>Velocity in px per tick, y up.</summary>
        public Vector2 Velocity;
        public bool DoubleJumping;
        public bool FastFalling;
        /// <summary>Running with a speed boost (sprint).</summary>
        public bool Sprinting;
        /// <summary>Just took a hit (post-hit immunity running).</summary>
        public bool Hit;
        public bool Drawing;
        /// <summary>Bow charge 0..1 while drawing; 1 = full draw.</summary>
        public float DrawFraction;
        /// <summary>The arrow has just been loosed (the bow's follow-through).</summary>
        public bool Released;
        /// <summary>Bow aim in world space, radians from +x.</summary>
        public float AimAngle;
        /// <summary>Swinging a pickaxe or placing a block, with the swing's progress 0..1.</summary>
        public bool Swinging;
        public float SwingProgress;
        /// <summary>The swung item's angle this instant, degrees (0 = pointing forward, positive = up); the arm follows it.</summary>
        public float SwingAngle;
        public Color TeamColor;
        /// <summary>Body tint (burning flicker, hit-immunity blink); its alpha also fades the outline.</summary>
        public Color Tint;
    }

    /// <summary>
    /// One sheet image read as cells of the <see cref="CharacterSheet"/> template. Shared by every character using the
    /// same image; needs the image to be readable (the import rules set it).
    /// </summary>
    internal sealed class SheetLayer
    {
        static readonly Dictionary<Texture2D, SheetLayer> s_Cache = new();

        readonly Color32[] m_Pixels;
        readonly int m_Width, m_Height;
        readonly bool[] m_Has = new bool[CharacterSheet.RowCount * CharacterSheet.Columns];
        readonly int[] m_Count = new int[CharacterSheet.RowCount];

        public static SheetLayer For(Texture2D texture)
        {
            if (texture == null) return null;
            if (s_Cache.TryGetValue(texture, out SheetLayer cached)) return cached;
            SheetLayer layer = null;
            if (!texture.isReadable)
                Debug.LogWarning($"Character sheet '{texture.name}' isn't readable. Keep character art under Assets/Game/Art/Characters or Assets/Resources/ClassArt.");
            else
                layer = new SheetLayer(texture);
            s_Cache[texture] = layer;
            return layer;
        }

        SheetLayer(Texture2D texture)
        {
            m_Pixels = texture.GetPixels32();
            m_Width = texture.width;
            m_Height = texture.height;
            for (int row = 0; row < CharacterSheet.RowCount; row++)
            {
                bool run = true;
                for (int col = 0; col < CharacterSheet.Columns; col++)
                {
                    bool has = CellHasPixels(row, col);
                    m_Has[row * CharacterSheet.Columns + col] = has;
                    run &= has;
                    if (run) m_Count[row]++;
                }
            }
        }

        /// <summary>The cell's bottom-left pixel in the image (rows count from the top), or false if the image is too small.</summary>
        bool Origin(int row, int col, out int x0, out int y0)
        {
            x0 = col * CharacterSheet.CellWidth;
            y0 = m_Height - (row + 1) * CharacterSheet.CellHeight;
            return x0 + CharacterSheet.CellWidth <= m_Width && y0 >= 0;
        }

        bool CellHasPixels(int row, int col)
        {
            if (!Origin(row, col, out int x0, out int y0)) return false;
            for (int y = 0; y < CharacterSheet.CellHeight; y++)
                for (int x = 0; x < CharacterSheet.CellWidth; x++)
                    if (m_Pixels[(y0 + y) * m_Width + x0 + x].a > 0) return true;
            return false;
        }

        public bool Has(int row, int col) => m_Has[row * CharacterSheet.Columns + col];
        /// <summary>Frames in the row: cells from the left up to the first empty one.</summary>
        public int Count(int row) => m_Count[row];

        /// <summary>Draws the cell over <paramref name="dst"/> (one cell's pixels), multiplied by <paramref name="tint"/>.</summary>
        public void DrawOver(Color32[] dst, int row, int col, Color32 tint)
        {
            if (!Has(row, col) || !Origin(row, col, out int x0, out int y0)) return;
            int w = CharacterSheet.CellWidth;
            for (int y = 0; y < CharacterSheet.CellHeight; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Color32 s = m_Pixels[(y0 + y) * m_Width + x0 + x];
                    if (s.a == 0) continue;
                    s.r = (byte)(s.r * tint.r / 255); s.g = (byte)(s.g * tint.g / 255); s.b = (byte)(s.b * tint.b / 255);
                    ref Color32 d = ref dst[y * w + x];
                    if (s.a == 255 || d.a == 0) { d = s; continue; }
                    float sa = s.a / 255f, da = d.a / 255f, oa = sa + da * (1f - sa);
                    d = new Color32(
                        (byte)((s.r * sa + d.r * da * (1f - sa)) / oa), (byte)((s.g * sa + d.g * da * (1f - sa)) / oa),
                        (byte)((s.b * sa + d.b * da * (1f - sa)) / oa), (byte)(oa * 255f));
                }
            }
        }
    }

    /// <summary>
    /// Draws a character from a <see cref="ClassSpriteSet"/>: picks the cell for what the character is doing (idle, run,
    /// sprint, jump, fall, double jump, fast fall, landing, drawing, release, swing, hit), stacks the base body, outfit
    /// and team-tinted mask for that cell into one sprite, flips for facing, draws the team outline as a separate layer
    /// behind everything, poses the jointed bow rig so the arms follow the aim up and down, and leaves a death burst
    /// where the character died. Everything is snapped to the pixel grid so the 2 × 2 pixel art stays crisp.
    /// </summary>
    public sealed class CharacterSprite : MonoBehaviour
    {
        const float RunSpeedThreshold = 0.5f;
        // sorting: one outline layer behind every part, the far arm behind the body, the near arm in front,
        // the hands in front of the game's bow (12) and nocked arrow (13)
        const int OutlineOrder = 8, FarArmOrder = 9, BodyOrder = 10, NearArmOrder = 11, HandOrder = 14, DeathOrder = 15;
        const int NoState = -1, RigState = -2;

        enum Mode { Loop, Once, Progress }

        ClassSpriteSet m_Set;
        SheetLayer m_Base, m_Outfit, m_Mask;
        SpriteRenderer m_Body;
        SpriteRenderer m_Outline;

        // animation state
        int m_StateKey = NoState;
        float m_StateTime;
        float m_RunPhase;
        float m_AirTime;
        float m_LandTimeLeft;
        bool m_WasAlive = true;

        // bow rig
        sealed class Part
        {
            public Transform T;
            public SpriteRenderer Sprite;
            public SpriteRenderer Outline;
        }

        Transform m_Rig;
        Part m_NearUpper, m_NearFore, m_FarUpper, m_FarFore, m_GripHand, m_DrawHand;

        readonly Dictionary<(int, int, int, Color32), Sprite> m_Composed = new();
        readonly Dictionary<(Sprite, int, float), Sprite> m_OutlineCache = new();

        public bool HasArt => m_Set != null && m_Outfit != null;

        /// <summary>True while the arm is swinging an item: <see cref="SwingHandWorld"/> is where its hand grips it.</summary>
        public bool SwingRigActive { get; private set; }
        public Vector3 SwingHandWorld { get; private set; }

        public static CharacterSprite Create(Transform parent, int unusedSortingOrder = BodyOrder)
        {
            var go = new GameObject("Character Art");
            go.transform.SetParent(parent, false);
            var art = go.AddComponent<CharacterSprite>();
            art.m_Body = go.AddComponent<SpriteRenderer>();
            art.m_Body.sortingOrder = BodyOrder;
            art.m_Outline = NewRenderer("Team Outline", go.transform, OutlineOrder);
            art.HideAll();
            return art;
        }

        static SpriteRenderer NewRenderer(string name, Transform parent, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = order;
            sr.enabled = false;
            return sr;
        }

        /// <summary>Uses this sprite set, or none (null) so the caller's placeholder is drawn instead.</summary>
        public void SetSpriteSet(ClassSpriteSet set)
        {
            m_Set = set;
            m_Outfit = set != null ? SheetLayer.For(set.Outfit) : null;
            m_Base = set != null ? SheetLayer.For(set.BaseBody) : null;
            m_Mask = set != null ? SheetLayer.For(set.TeamMask) : null;
            ClearCaches();
            m_StateKey = NoState;
            m_StateTime = 0f;
            m_RunPhase = 0f;
            if (!HasArt) HideAll();
        }

        void HideAll()
        {
            if (m_Body != null) m_Body.enabled = false;
            if (m_Outline != null) m_Outline.enabled = false;
            SetRigVisible(false);
        }

        // =========================================================================================================
        // per frame
        // =========================================================================================================

        public void Show(in CharacterPose pose)
        {
            if (!HasArt)
            {
                HideAll();
                return;
            }

            if (m_WasAlive && !pose.Alive && pose.Visible) SpawnDeathBurst(pose.Facing);
            m_WasAlive = pose.Alive;
            if (!pose.Visible || !pose.Alive)
            {
                m_RunPhase = 0f;
                HideAll();
                return;
            }

            SnapToPixelGrid();
            TrackLanding(pose);

            float speed = Mathf.Abs(pose.Velocity.x);
            bool striding = pose.Grounded && speed > RunSpeedThreshold && m_Set.RunCycleDistancePx > 0f;
            if (striding)
                m_RunPhase = Mathf.Repeat(m_RunPhase + speed * SimUnits.TicksPerSecond * Time.deltaTime / m_Set.RunCycleDistancePx, 1f);
            else
                m_RunPhase = 0f;

            // The jointed arms are used for the bow and, when the class has the rig, for swinging an item: the arm turns with
            // the item and the hand stays on its grip (frame-by-frame Swing art is only the fallback without a rig).
            SwingRigActive = false;
            bool swingRig = pose.Swinging && !pose.Hit && HasRig;
            bool useRig = swingRig || ((pose.Drawing || pose.Released) && !pose.Hit && !pose.Swinging && HasRig);
            (int row, int col) cell = useRig ? RigBody(pose) : PickFrame(pose);
            int legs = striding && m_Set.ActionLegsHeightPx > 0 && Count(CharacterSheet.Row.Run) > 0
                       && (useRig || cell.row == (int)CharacterSheet.Row.Swing)
                ? GroundStrideColumn(CharacterSheet.Row.Run)
                : -1;
            Sprite frame = cell.row < 0 ? null : Composed(cell.row, cell.col, pose.TeamColor, legs);
            if (frame == null)
            {
                HideAll();
                return;
            }

            bool flip = pose.Facing < 0;
            Color outlineColor = pose.TeamColor;
            outlineColor.a = m_Set.OutlineOpacity * pose.Tint.a;

            m_Body.sprite = frame;
            m_Body.flipX = flip;
            m_Body.color = pose.Tint;
            m_Body.enabled = true;
            SetOutline(m_Outline, frame, flip, outlineColor);

            if (useRig) PoseRig(pose, outlineColor);
            else SetRigVisible(false);
        }

        /// <summary>Keeps the art on whole pixels while the character moves smoothly underneath.</summary>
        void SnapToPixelGrid()
        {
            Transform parent = transform.parent;
            if (parent == null) return;
            Vector3 p = parent.position;
            float ppu = CharacterSheet.PixelsPerUnit;
            transform.position = new Vector3(Mathf.Round(p.x * ppu) / ppu, Mathf.Round(p.y * ppu) / ppu, p.z);
        }

        void TrackLanding(in CharacterPose pose)
        {
            float dt = Time.deltaTime;
            if (!pose.Grounded)
            {
                m_AirTime += dt;
            }
            else
            {
                bool cameDown = m_AirTime > 0f && m_AirTime >= m_Set.MinAirTimeForLanding;   // just touched down after a real fall
                if (cameDown && Count(CharacterSheet.Row.Land) > 0) m_LandTimeLeft = m_Set.LandSeconds;
                m_AirTime = 0f;
            }

            if (m_LandTimeLeft > 0f) m_LandTimeLeft -= dt;
        }

        // ---- layers ----

        /// <summary>Frames in a row: the outfit decides, so a row the outfit leaves empty falls back to another animation.</summary>
        int Count(CharacterSheet.Row row) => m_Outfit.Count((int)row);

        bool HasCell(int row, int col) => m_Outfit.Has(row, col) || (m_Base != null && m_Base.Has(row, col));

        bool HasRig
        {
            get
            {
                int parts = (int)CharacterSheet.Row.RigParts;
                if (!HasCell((int)CharacterSheet.Row.RigBody, (int)CharacterSheet.RigBodyCell.Ready)) return false;
                for (int c = 0; c <= (int)CharacterSheet.RigPart.StringPinchHand; c++)
                    if (!HasCell(parts, c)) return false;
                return true;
            }
        }

        /// <summary>The cell with every layer stacked (base body, outfit, team mask in the team's colour), cached.</summary>
        Sprite Composed(int row, int col, Color teamColor, int runLegs = -1)
        {
            Color32 team = teamColor;
            team.a = 255;
            var key = (row, col, runLegs, m_Mask != null ? team : default);
            if (m_Composed.TryGetValue(key, out Sprite cached)) return cached;

            Sprite sprite = null;
            if (HasCell(row, col) || (m_Mask != null && m_Mask.Has(row, col)))
            {
                int w = CharacterSheet.CellWidth, h = CharacterSheet.CellHeight;
                var px = new Color32[w * h];
                var white = new Color32(255, 255, 255, 255);
                m_Base?.DrawOver(px, row, col, white);
                m_Outfit.DrawOver(px, row, col, white);
                m_Mask?.DrawOver(px, row, col, team);
                if (runLegs >= 0)
                {
                    int run = (int)CharacterSheet.Row.Run;
                    var runPixels = new Color32[px.Length];
                    m_Base?.DrawOver(runPixels, run, runLegs, white);
                    m_Outfit.DrawOver(runPixels, run, runLegs, white);
                    m_Mask?.DrawOver(runPixels, run, runLegs, team);
                    System.Array.Copy(runPixels, px, w * Mathf.Clamp(m_Set.ActionLegsHeightPx, 0, h));
                }

                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = $"{m_Set.name} {(CharacterSheet.Row)row} {col}",
                };
                tex.SetPixels32(px);
                tex.Apply(false, false);   // stays readable so the outline can be built from it
                Vector2Int pivot = CharacterSheet.PivotPx(row, col);
                sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(pivot.x / (float)w, pivot.y / (float)h), CharacterSheet.PixelsPerUnit);
                sprite.name = tex.name;
            }

            m_Composed[key] = sprite;
            return sprite;
        }

        void ClearCaches()
        {
            foreach (Sprite s in m_Composed.Values) DestroySprite(s);
            foreach (Sprite s in m_OutlineCache.Values) DestroySprite(s);
            m_Composed.Clear();
            m_OutlineCache.Clear();
        }

        static void DestroySprite(Sprite s)
        {
            if (s == null) return;
            Destroy(s.texture);
            Destroy(s);
        }

        // ---- frame picking ----

        CharacterSheet.Row Or(CharacterSheet.Row a, CharacterSheet.Row b) => Count(a) > 0 ? a : b;

        int GroundStrideColumn(CharacterSheet.Row row) => Mathf.FloorToInt(m_RunPhase * Count(row));

        (int, int) PickFrame(in CharacterPose pose)
        {
            ClassSpriteSet s = m_Set;
            CharacterSheet.Row idle = Or(CharacterSheet.Row.Idle, Or(CharacterSheet.Row.Run, CharacterSheet.Row.Draw));

            if (pose.Hit && Count(CharacterSheet.Row.Hit) > 0) return Play(CharacterSheet.Row.Hit, Mode.Once, s.HitFramesPerSecond, 0f);
            if (pose.Swinging && Count(CharacterSheet.Row.Swing) > 0) return Play(CharacterSheet.Row.Swing, Mode.Progress, 0f, pose.SwingProgress);

            if (pose.Drawing && Count(CharacterSheet.Row.Draw) > 0)
            {
                CharacterSheet.Row draw = pose.Grounded ? CharacterSheet.Row.Draw : Or(CharacterSheet.Row.DrawAir, CharacterSheet.Row.Draw);
                return Play(draw, Mode.Progress, 0f, pose.DrawFraction);
            }

            if (pose.Released)
            {
                if (Count(CharacterSheet.Row.Release) > 0) return Play(CharacterSheet.Row.Release, Mode.Once, s.ReleaseFramesPerSecond, 0f);
                if (Count(CharacterSheet.Row.Draw) > 0) return Play(CharacterSheet.Row.Draw, Mode.Progress, 0f, 1f);
            }

            if (m_LandTimeLeft > 0f && pose.Grounded)
            {
                return Play(CharacterSheet.Row.Land, Mode.Once, Count(CharacterSheet.Row.Land) / Mathf.Max(0.01f, s.LandSeconds), 0f);
            }

            if (!pose.Grounded)
            {
                CharacterSheet.Row up = Or(CharacterSheet.Row.JumpUp, idle);
                CharacterSheet.Row fall = Or(CharacterSheet.Row.Fall, up);
                if (pose.DoubleJumping) return Play(Or(CharacterSheet.Row.DoubleJump, up), Mode.Once, s.AirFramesPerSecond, 0f);
                if (pose.FastFalling) return Play(Or(CharacterSheet.Row.FastFall, fall), Mode.Loop, s.AirFramesPerSecond, 0f);
                return Play(pose.Velocity.y > 0f ? up : fall, Mode.Loop, s.AirFramesPerSecond, 0f);
            }

            if (Mathf.Abs(pose.Velocity.x) > RunSpeedThreshold && Count(CharacterSheet.Row.Run) > 0)
            {
                bool sprint = pose.Sprinting && Count(CharacterSheet.Row.Sprint) > 0;
                CharacterSheet.Row row = sprint ? CharacterSheet.Row.Sprint : CharacterSheet.Row.Run;
                if (s.RunCycleDistancePx > 0f)
                    return ((int)row, GroundStrideColumn(row));
                return Play(row, Mode.Loop, sprint ? s.SprintFramesPerSecond : s.RunFramesPerSecond, 0f);
            }

            return Play(idle, Mode.Loop, s.IdleFramesPerSecond, 0f);
        }

        /// <summary>A cell of <paramref name="row"/>: looped or played once by time (restarting when the animation changes), or chosen by progress. Row -1 = nothing to draw.</summary>
        (int, int) Play(CharacterSheet.Row row, Mode mode, float fps, float progress)
        {
            int n = Count(row);
            if (n == 0) return (-1, 0);

            if ((int)row != m_StateKey)
            {
                m_StateKey = (int)row;
                m_StateTime = 0f;
            }
            else
            {
                m_StateTime += Time.deltaTime;
            }

            int col;
            switch (mode)
            {
                case Mode.Progress:
                    col = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(progress) * (n - 1)), 0, n - 1);
                    break;
                case Mode.Once:
                    col = Mathf.Min(n - 1, Mathf.FloorToInt(m_StateTime * Mathf.Max(0.01f, fps)));
                    break;
                default:
                    col = Mathf.FloorToInt(m_StateTime * Mathf.Max(0.01f, fps)) % n;
                    break;
            }

            return ((int)row, col);
        }

        // ---- bow rig ----

        (int, int) RigBody(in CharacterPose pose)
        {
            ClassSpriteSet s = m_Set;
            m_StateKey = RigState;           // a rig pose; the next frame-based animation starts from its first frame
            m_StateTime = 0f;
            int row = (int)CharacterSheet.Row.RigBody;
            int Cell(CharacterSheet.RigBodyCell c, CharacterSheet.RigBodyCell fallback) => HasCell(row, (int)c) ? (int)c : (int)fallback;

            if (!pose.Grounded)
            {
                int rising = Cell(CharacterSheet.RigBodyCell.AirRising, CharacterSheet.RigBodyCell.Ready);
                int falling = HasCell(row, (int)CharacterSheet.RigBodyCell.AirFalling) ? (int)CharacterSheet.RigBodyCell.AirFalling : rising;
                return (row, pose.Velocity.y > 0f ? rising : falling);
            }

            bool braced = pose.Released || pose.DrawFraction >= s.BraceAtCharge;
            return (row, braced ? Cell(CharacterSheet.RigBodyCell.Braced, CharacterSheet.RigBodyCell.Ready) : (int)CharacterSheet.RigBodyCell.Ready);
        }

        void EnsureRig()
        {
            if (m_Rig != null) return;

            m_Rig = new GameObject("Bow Rig").transform;
            m_Rig.SetParent(transform, false);
            m_FarUpper = NewPart("Far Upper Arm", FarArmOrder);
            m_FarFore = NewPart("Far Forearm", FarArmOrder);
            m_NearUpper = NewPart("Near Upper Arm", NearArmOrder);
            m_NearFore = NewPart("Near Forearm", NearArmOrder);
            m_GripHand = NewPart("Bow Hand", HandOrder);
            m_DrawHand = NewPart("Draw Hand", HandOrder);
        }

        Part NewPart(string name, int order)
        {
            var t = new GameObject(name).transform;
            t.SetParent(m_Rig, false);
            var sr = t.gameObject.AddComponent<SpriteRenderer>();
            sr.sortingOrder = order;
            return new Part { T = t, Sprite = sr, Outline = NewRenderer(name + " Outline", t, OutlineOrder) };
        }

        void SetRigVisible(bool visible)
        {
            if (m_Rig != null) m_Rig.gameObject.SetActive(visible);
        }

        Sprite RigPart(CharacterSheet.RigPart part, Color team) => Composed((int)CharacterSheet.Row.RigParts, (int)part, team);

        /// <summary>
        /// Poses the arms in the rig's own space, which always faces right (the rig is mirrored for facing left): the bow
        /// hand goes on the grip, out along the aim from the bow pivot, and the draw hand on the string, pulled back with the
        /// charge (coming from the quiver at the very start). Each arm is a two-bone chain solved to reach its hand.
        /// </summary>
        void PoseRig(in CharacterPose pose, Color outlineColor)
        {
            EnsureRig();
            SetRigVisible(true);
            ClassSpriteSet s = m_Set;
            float ppu = ClassSpriteSet.PixelsPerUnit;
            int facing = pose.Facing < 0 ? -1 : 1;
            m_Rig.localScale = new Vector3(facing, 1f, 1f);
            if (pose.Swinging)
            {
                PoseSwing(pose, outlineColor);
                return;
            }

            float aim = facing > 0 ? pose.AimAngle : Mathf.PI - pose.AimAngle;   // aim as seen in the rig's right-facing space
            var dir = new Vector2(Mathf.Cos(aim), Mathf.Sin(aim));
            float aimDeg = aim * Mathf.Rad2Deg;

            Vector2 pivot = s.BowPivotPx / ppu;
            Vector2 grip = pivot + dir * (s.GripDistancePx / ppu);
            float charge = pose.Released ? 1f : Mathf.Clamp01(pose.DrawFraction);
            Vector2 pinch = pivot + dir * (Mathf.Lerp(s.StringRestPx, s.StringFullPx, charge) / ppu);

            Color team = pose.TeamColor;
            Sprite drawHand = RigPart(CharacterSheet.RigPart.StringPinchHand, team);
            Vector2 drawTarget = pinch;
            if (pose.Released)
            {
                drawHand = RigPart(CharacterSheet.RigPart.ReleaseHand, team) ?? drawHand;
            }
            else if (charge < s.ReachFraction && s.ReachFraction > 0f)
            {
                drawTarget = Vector2.Lerp(s.QuiverPx / ppu, pinch, charge / s.ReachFraction);
                drawHand = RigPart(CharacterSheet.RigPart.ReachingHand, team) ?? drawHand;
            }

            bool farHoldsBow = s.BowArm == RigArm.Far;
            Vector2 nearShoulder = s.NearShoulderPx / ppu, farShoulder = s.FarShoulderPx / ppu;
            float upper = s.UpperArmLengthPx / ppu, fore = s.ForearmLengthPx / ppu;

            PoseArm(m_FarUpper, m_FarFore, RigPart(CharacterSheet.RigPart.FarUpperArm, team), RigPart(CharacterSheet.RigPart.FarForearm, team),
                    farShoulder, farHoldsBow ? grip : drawTarget, upper, fore, farHoldsBow ? s.BowElbowBend : s.DrawElbowBend, pose.Tint, outlineColor);
            PoseArm(m_NearUpper, m_NearFore, RigPart(CharacterSheet.RigPart.NearUpperArm, team), RigPart(CharacterSheet.RigPart.NearForearm, team),
                    nearShoulder, farHoldsBow ? drawTarget : grip, upper, fore, farHoldsBow ? s.DrawElbowBend : s.BowElbowBend, pose.Tint, outlineColor);

            PlacePart(m_GripHand, RigPart(CharacterSheet.RigPart.BowGripHand, team), grip, aimDeg, pose.Tint, outlineColor);
            PlacePart(m_DrawHand, drawHand, drawTarget, aimDeg, pose.Tint, outlineColor);
        }

        /// <summary>
        /// Swinging an item: the near arm turns with the item, reaching out along its angle, and its fist stays on the grip
        /// at every point of the swing (<see cref="SwingHandWorld"/> is where the item's pivot goes). The far arm hangs
        /// relaxed at the side. The rig faces right here; the rig is mirrored for facing left.
        /// </summary>
        void PoseSwing(in CharacterPose pose, Color outlineColor)
        {
            ClassSpriteSet s = m_Set;
            float ppu = ClassSpriteSet.PixelsPerUnit;
            Color team = pose.TeamColor;
            Vector2 nearShoulder = s.NearShoulderPx / ppu, farShoulder = s.FarShoulderPx / ppu;
            float upper = s.UpperArmLengthPx / ppu, fore = s.ForearmLengthPx / ppu;

            float armRad = pose.SwingAngle * Mathf.Deg2Rad;
            Vector2 hand = nearShoulder + new Vector2(Mathf.Cos(armRad), Mathf.Sin(armRad)) * ((upper + fore) * s.SwingReach);
            Vector2 farRest = farShoulder + new Vector2(0.1f, -(upper + fore) * 0.8f);

            PoseArm(m_FarUpper, m_FarFore, RigPart(CharacterSheet.RigPart.FarUpperArm, team), RigPart(CharacterSheet.RigPart.FarForearm, team),
                    farShoulder, farRest, upper, fore, s.DrawElbowBend, pose.Tint, outlineColor);
            PoseArm(m_NearUpper, m_NearFore, RigPart(CharacterSheet.RigPart.NearUpperArm, team), RigPart(CharacterSheet.RigPart.NearForearm, team),
                    nearShoulder, hand, upper, fore, s.BowElbowBend, pose.Tint, outlineColor);

            PlacePart(m_GripHand, RigPart(CharacterSheet.RigPart.BowGripHand, team), hand, pose.SwingAngle, pose.Tint, outlineColor);
            PlacePart(m_DrawHand, RigPart(CharacterSheet.RigPart.StringPinchHand, team), farRest, -90f, pose.Tint, outlineColor);

            SwingRigActive = true;
            SwingHandWorld = m_Rig.TransformPoint(new Vector3(hand.x, hand.y, 0f));
        }

        void PoseArm(Part upperPart, Part forePart, Sprite upperSprite, Sprite foreSprite, Vector2 shoulder, Vector2 target,
                     float upper, float fore, int bend, Color tint, Color outlineColor)
        {
            SolveTwoBone(shoulder, target, upper, fore, bend, out float upperDeg, out Vector2 elbow, out float foreDeg);
            PlacePart(upperPart, upperSprite, shoulder, upperDeg, tint, outlineColor);
            PlacePart(forePart, foreSprite, elbow, foreDeg, tint, outlineColor);
        }

        /// <summary>
        /// Two-bone reach: the angles that put the end of a shoulder→elbow→hand chain on <paramref name="target"/>
        /// (or as close as the bones allow). <paramref name="bend"/> picks which side the elbow goes.
        /// </summary>
        public static void SolveTwoBone(Vector2 shoulder, Vector2 target, float upper, float fore, int bend,
                                        out float upperDeg, out Vector2 elbow, out float foreDeg)
        {
            Vector2 toTarget = target - shoulder;
            float maxReach = upper + fore - 1e-4f, minReach = Mathf.Abs(upper - fore) + 1e-4f;
            float d = Mathf.Clamp(toTarget.magnitude, minReach, maxReach);
            float baseAngle = Mathf.Atan2(toTarget.y, toTarget.x);
            float cos = Mathf.Clamp((upper * upper + d * d - fore * fore) / (2f * upper * d), -1f, 1f);
            float upperAngle = baseAngle - (bend >= 0 ? 1f : -1f) * Mathf.Acos(cos);

            elbow = shoulder + new Vector2(Mathf.Cos(upperAngle), Mathf.Sin(upperAngle)) * upper;
            Vector2 hand = shoulder + new Vector2(Mathf.Cos(baseAngle), Mathf.Sin(baseAngle)) * d;
            Vector2 toHand = hand - elbow;
            upperDeg = upperAngle * Mathf.Rad2Deg;
            foreDeg = Mathf.Atan2(toHand.y, toHand.x) * Mathf.Rad2Deg;
        }

        void PlacePart(Part part, Sprite sprite, Vector2 position, float degrees, Color tint, Color outlineColor)
        {
            bool on = sprite != null;
            part.Sprite.enabled = on;
            if (!on)
            {
                part.Outline.enabled = false;
                return;
            }

            float ppu = CharacterSheet.PixelsPerUnit;
            part.T.localPosition = new Vector2(Mathf.Round(position.x * ppu) / ppu, Mathf.Round(position.y * ppu) / ppu);
            part.T.localRotation = Quaternion.Euler(0f, 0f, degrees);
            part.Sprite.sprite = sprite;
            part.Sprite.color = tint;
            SetOutline(part.Outline, sprite, false, outlineColor);
        }

        // ---- outline ----

        void SetOutline(SpriteRenderer target, Sprite frame, bool flip, Color color)
        {
            Sprite outline = m_Set.OutlineThickness > 0 ? OutlineFor(frame, m_Set.OutlineThickness, m_Set.OutlineAlphaThreshold) : null;
            target.enabled = outline != null;
            if (outline == null) return;

            target.sprite = outline;
            target.flipX = flip;
            target.color = color;
        }

        /// <summary>
        /// A white silhouette of the frame grown by <paramref name="thickness"/> pixels, same pivot and scale, so drawn
        /// behind the character it shows as an outline.
        /// </summary>
        Sprite OutlineFor(Sprite frame, int thickness, float threshold)
        {
            var key = (frame, thickness, threshold);
            if (m_OutlineCache.TryGetValue(key, out Sprite cached)) return cached;

            Texture2D src = frame.texture;
            Rect r = frame.rect;
            int w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
            Color32[] pixels = src.GetPixels32();
            int sx0 = Mathf.RoundToInt(r.x), sy0 = Mathf.RoundToInt(r.y);
            byte cut = (byte)Mathf.Clamp(Mathf.RoundToInt(threshold * 255f), 1, 255);

            var solid = new bool[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++) solid[y * w + x] = pixels[(sy0 + y) * src.width + sx0 + x].a >= cut;
            }

            // square growth keeps the outline on the same blocky grid as the art
            int ow = w + thickness * 2, oh = h + thickness * 2;
            var outPx = new Color32[ow * oh];
            for (int y = 0; y < oh; y++)
            {
                for (int x = 0; x < ow; x++)
                {
                    bool hit = false;
                    for (int dy = -thickness; dy <= thickness && !hit; dy++)
                    {
                        int yy = y - thickness + dy;
                        if (yy < 0 || yy >= h) continue;
                        for (int dx = -thickness; dx <= thickness; dx++)
                        {
                            bool corner = Mathf.Abs(dx) == thickness && Mathf.Abs(dy) == thickness && thickness > 1;
                            if (corner) continue;
                            int xx = x - thickness + dx;
                            if (xx >= 0 && xx < w && solid[yy * w + xx])
                            {
                                hit = true;
                                break;
                            }
                        }
                    }

                    outPx[y * ow + x] = hit ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                }
            }

            var tex = new Texture2D(ow, oh, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = frame.name + " outline",
            };
            tex.SetPixels32(outPx);
            tex.Apply(false, true);

            Vector2 pivotPx = frame.pivot + new Vector2(thickness, thickness);
            Sprite outline = Sprite.Create(tex, new Rect(0, 0, ow, oh), new Vector2(pivotPx.x / ow, pivotPx.y / oh), frame.pixelsPerUnit);
            outline.name = tex.name;
            m_OutlineCache[key] = outline;
            return outline;
        }

        // ---- death ----

        void SpawnDeathBurst(int facing)
        {
            if (m_Set.Death.Length == 0) return;

            var go = new GameObject("Death Burst");
            go.transform.position = transform.position;
            var burst = go.AddComponent<DeathBurst>();
            burst.Play(m_Set.Death, m_Set.DeathFramesPerSecond, facing < 0, DeathOrder);
        }

        void OnDestroy() => ClearCaches();
    }

    /// <summary>Plays a one-shot sequence of frames where it was left (a death burst), then removes itself.</summary>
    public sealed class DeathBurst : MonoBehaviour
    {
        Sprite[] m_Frames;
        float m_Fps;
        float m_Time;
        SpriteRenderer m_Renderer;

        public void Play(Sprite[] frames, float fps, bool flip, int sortingOrder)
        {
            m_Frames = frames;
            m_Fps = Mathf.Max(0.01f, fps);
            m_Renderer = gameObject.AddComponent<SpriteRenderer>();
            m_Renderer.sortingOrder = sortingOrder;
            m_Renderer.flipX = flip;
            m_Renderer.sprite = frames[0];
        }

        void Update()
        {
            m_Time += Time.deltaTime;
            int i = Mathf.FloorToInt(m_Time * m_Fps);
            if (i >= m_Frames.Length)
            {
                Destroy(gameObject);
                return;
            }

            m_Renderer.sprite = m_Frames[i];
        }
    }
}
