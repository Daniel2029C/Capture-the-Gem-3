using System;
using CTG.World;
using UnityEngine;

namespace CTG.Core
{
    /// <summary>What a floating number or message means; the view decides how it looks (colour, size, font).</summary>
    public enum TextKind
    {
        /// <summary>Damage dealt to something that isn't a player (a dummy, a mob, the barrier).</summary>
        Damage,
        /// <summary>Damage or bad news for a player.</summary>
        PlayerDamage,
        /// <summary>Healing, rewards, level ups.</summary>
        Heal,
    }

    /// <summary>
    /// Things the simulation wants shown but never needs to look at again: floating text and particle bursts. Gameplay
    /// code calls these and moves on; whatever draws them (the client's views) subscribes, and a headless run with no
    /// views simply has no subscribers. One lives on each <see cref="MatchContext"/>.
    /// </summary>
    public sealed class PresentationEvents
    {
        /// <summary>Position in simulation pixels (y-up), the text, and what kind of message it is.</summary>
        public event Action<Vector2, string, TextKind> FloatingText;

        /// <summary>Position in simulation pixels, the tile that was struck, how many chips, which way they fly and how fast (px/s).</summary>
        public event Action<Vector2, TileId, int, Vector2, float> DirtBurst;

        public void ShowText(Vector2 positionPx, string text, TextKind kind) => FloatingText?.Invoke(positionPx, text, kind);

        /// <summary>A burst of dirt chips (nothing happens for tiles that don't shed any). <paramref name="direction"/> zero = upward.</summary>
        public void SprayDirt(Vector2 positionPx, TileId tile, int count, Vector2 direction, float speed)
        {
            if (ShedsDirt(tile)) DirtBurst?.Invoke(positionPx, tile, count, direction, speed);
        }

        /// <summary>True for the tile kinds that shed dirt.</summary>
        public static bool ShedsDirt(TileId id) => id == TileId.BlueDirt || id == TileId.RedDirt;
    }
}
