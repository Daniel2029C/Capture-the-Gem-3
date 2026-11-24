using CTG.Classes;
using UnityEngine;

namespace CTG.Rendering
{
    public enum RigArm { Far, Near }

    /// <summary>
    /// The shared frame template every character sheet is drawn on. A sheet is one image: rows of 48 × 56 cells, one
    /// row per animation (left to right, the row ends at its first empty cell), eight cells per row. The base body,
    /// every class outfit and every team mask use the same layout, so the same cell always lines up across layers.
    ///
    /// Art is drawn in 2 × 2 pixel blocks at 16 pixels per tile: the body fits a 36 × 48 area (2.25 × 3 tiles) with the
    /// feet on <see cref="FeetPx"/>, leaving room in the cell for hoods, cloak tails and quivers. Characters face right.
    /// </summary>
    public static class CharacterSheet
    {
        public const int CellWidth = 48, CellHeight = 56, Columns = 8;
        public const float PixelsPerUnit = 16f;

        /// <summary>Rows, top to bottom.</summary>
        public enum Row
        {
            Idle, Run, Sprint, Land, JumpUp, Fall, DoubleJump, FastFall,
            Draw, DrawAir, Release, Swing, Hit,
            /// <summary>Bow rig bodies without arms: <see cref="RigBodyCell"/>.</summary>
            RigBody,
            /// <summary>Bow rig arm pieces and hands: <see cref="RigPart"/>.</summary>
            RigParts,
        }

        public const int RowCount = (int)Row.RigParts + 1;
        public const int Width = CellWidth * Columns, Height = CellHeight * RowCount;

        public enum RigBodyCell { Ready, Braced, AirRising, AirFalling }
        public enum RigPart { NearUpperArm, NearForearm, FarUpperArm, FarForearm, BowGripHand, StringPinchHand, ReleaseHand, ReachingHand }

        /// <summary>Where the feet go in every body cell (pixels from the cell's bottom-left): the frame's pivot.</summary>
        public static readonly Vector2Int FeetPx = new(24, 2);
        /// <summary>Arm pieces are drawn pointing right with the joint they turn around (shoulder, elbow) here.</summary>
        public static readonly Vector2Int JointPx = new(8, 28);
        /// <summary>Hands are drawn pointing right with the point that sits on the bow or string here.</summary>
        public static readonly Vector2Int HandPx = new(24, 28);

        public static Vector2Int PivotPx(int row, int col)
        {
            if (row != (int)Row.RigParts) return FeetPx;
            return col <= (int)RigPart.FarForearm ? JointPx : HandPx;
        }
    }

    /// <summary>
    /// A class's character art, built from layers on the <see cref="CharacterSheet"/> template: the shared base body
    /// (Resources/ClassArt/BaseBody), this class's outfit drawn over it, and an optional team mask (greyscale, tinted in
    /// the team's colour) over both. Put the asset at Resources/ClassArt/&lt;ClassId&gt; (e.g. Resources/ClassArt/Archer)
    /// and the class uses it; a class without one keeps the placeholder body.
    ///
    /// Rows may be left empty: the character falls back to a related animation (sprint to run, fall to jump, airborne
    /// draw to draw, and so on, ending at idle), so frames can be added a few at a time. Images under
    /// Assets/Game/Art/Characters or Assets/Resources/ClassArt get the right import settings automatically.
    /// </summary>
    [CreateAssetMenu(menuName = "CTG/Class Sprite Set", fileName = "ClassSpriteSet")]
    public sealed class ClassSpriteSet : ScriptableObject
    {
        public const string ResourceFolder = "ClassArt";
        public const string BaseBodyName = "BaseBody";
        /// <summary>Character art's pixels per unit (one unit = one tile): one source pixel is one game pixel.</summary>
        public const float PixelsPerUnit = CharacterSheet.PixelsPerUnit;

        [Header("Layers (sheets on the shared template)")]
        [Tooltip("This class's outfit, drawn over the base body.")]
        public Texture2D Outfit;
        [Tooltip("Optional greyscale areas tinted in the team's colour (a scarf, an eye slit), drawn over the outfit.")]
        public Texture2D TeamMask;
        [Tooltip("Draw the shared base body (Resources/ClassArt/BaseBody) under the outfit. Off = the outfit is the whole character.")]
        public bool UseBaseBody = true;

        [Header("Bow rig (rows RigBody and RigParts): aims up and down")]
        [Tooltip("Which arm holds the bow; the other draws the string.")]
        public RigArm BowArm = RigArm.Far;
        [Tooltip("Shoulder positions in pixels from the feet, facing right.")]
        public Vector2 NearShoulderPx = new(2f, 32f);
        public Vector2 FarShoulderPx = new(-2f, 33f);
        [Tooltip("Shoulder to elbow and elbow to hand, in pixels.")]
        public float UpperArmLengthPx = 8f;
        public float ForearmLengthPx = 8f;
        [Tooltip("Which way each elbow bends: 1 = elbow below the line from shoulder to hand, -1 = above.")]
        public int BowElbowBend = 1;
        public int DrawElbowBend = -1;

        [Tooltip("Where the bow turns around, in pixels from the feet. The game's bow is moved here too while this class has art. Default = the hitbox centre (23 px up).")]
        public Vector2 BowPivotPx = new(0f, 23f);
        [Tooltip("Distance from the bow pivot to the grip along the aim, pixels (the game draws its bow 9·√2 px out = 12.7).")]
        public float GripDistancePx = 12.73f;
        [Tooltip("String hand distance from the bow pivot along the aim: at rest (no charge) and at full draw.")]
        public float StringRestPx = 12f;
        public float StringFullPx = 1f;
        [Tooltip("Where the draw hand reaches to at the start of a draw (the quiver), pixels from the feet.")]
        public Vector2 QuiverPx = new(-6f, 38f);
        [Tooltip("How far the swinging arm reaches as a fraction of its full length (1 = fully straight). The fist holds the item here.")]
        [Range(0.5f, 1f)] public float SwingReach = 0.85f;
        [Tooltip("Charge below which the draw hand is still coming back from the quiver.")]
        [Range(0f, 0.5f)] public float ReachFraction = 0.12f;
        [Tooltip("Charge at which the grounded body switches from Ready to Braced.")]
        [Range(0f, 1f)] public float BraceAtCharge = 0.35f;

        [Header("Death")]
        [Tooltip("Death burst, played once where the character died as a separate effect (any size, pivot on the feet). Empty = none.")]
        public Sprite[] Death = System.Array.Empty<Sprite>();

        [Header("Timing (frames per second)")]
        public float IdleFramesPerSecond = 6f;
        public float RunFramesPerSecond = 12f;
        [Tooltip("Horizontal travel for one ground stride cycle, in simulation pixels. Zero uses frame timing.")]
        [Min(0f)] public float RunCycleDistancePx;
        [Tooltip("Height of the leg strip from the bottom of a sheet cell. Zero uses whole poses for item actions.")]
        [Range(0, CharacterSheet.CellHeight)] public int ActionLegsHeightPx;
        public float SprintFramesPerSecond = 14f;
        public float AirFramesPerSecond = 10f;
        public float ReleaseFramesPerSecond = 16f;
        public float HitFramesPerSecond = 12f;
        public float DeathFramesPerSecond = 12f;
        [Tooltip("How long the landing pose shows.")]
        public float LandSeconds = 0.12f;
        [Tooltip("Only falls longer than this (seconds in the air) show the landing pose, so small steps don't.")]
        public float MinAirTimeForLanding = 0.2f;

        [Header("Team outline")]
        [Tooltip("Outline thickness in pixels (2 = one art block).")]
        [Range(0, 8)] public int OutlineThickness = 2;
        [Tooltip("How opaque a pixel must be to count as part of the character when building the outline.")]
        [Range(0.05f, 1f)] public float OutlineAlphaThreshold = 0.5f;
        [Tooltip("Outline opacity.")]
        [Range(0f, 1f)] public float OutlineOpacity = 1f;

        public bool HasFrames => Outfit != null;

        /// <summary>The base body under the outfit, or null when this class doesn't use one (or none exists yet).</summary>
        public Texture2D BaseBody => UseBaseBody ? Resources.Load<Texture2D>($"{ResourceFolder}/{BaseBodyName}") : null;

        /// <summary>The class's sprite set, or null if it has none yet.</summary>
        public static ClassSpriteSet For(ClassId id) => Resources.Load<ClassSpriteSet>($"{ResourceFolder}/{id}");
    }
}
