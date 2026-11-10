using CTG.Combat;
using UnityEngine;

namespace CTG.Gameplay
{
    /// <summary>
    /// One team's gem. It stays on its pedestal for the whole match: an enemy who touches its hitbox
    /// becomes the carrier (a copy rides above their head) and the gem counts as held until the carrier
    /// captures it or dies. Hitbox is 6×9 tiles.
    /// </summary>
    public sealed class Gem
    {
        public const int WidthTiles = 6;
        public const int HeightTiles = 9;

        /// <summary>The team that owns this gem; the other team steals it.</summary>
        public Team Team { get; }
        /// <summary>The pedestal hitbox, world px.</summary>
        public Rect HitboxPx { get; }
        /// <summary>The shield that must be broken before this gem can be picked up.</summary>
        public GemBarrier Barrier { get; set; }
        public bool IsHeld { get; set; }
        public PlayerCharacter Holder { get; set; }
        /// <summary>Set when the carrier reaches the owner's gem; the match consumes it and resets the gem.</summary>
        public bool IsCaptured { get; set; }

        public Gem(Team team, Rect hitboxPx)
        {
            Team = team;
            HitboxPx = hitboxPx;
        }

        public void Reset()
        {
            IsHeld = false;
            IsCaptured = false;
            Holder = null;
        }
    }
}
