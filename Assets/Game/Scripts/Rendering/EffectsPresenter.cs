using CTG.Core;
using CTG.UI;
using UnityEngine;

namespace CTG.Rendering
{
    /// <summary>
    /// The client's side of <see cref="PresentationEvents"/>: turns the simulation's "show this" requests into floating
    /// combat text and particles. A headless run never attaches one, so the simulation has no view calls.
    /// </summary>
    public static class EffectsPresenter
    {
        /// <summary>Starts drawing this match's text and particle requests. Safe to call once per match.</summary>
        public static void Attach(PresentationEvents events)
        {
            events.FloatingText += (positionPx, text, kind) => CombatText.Spawn(positionPx, text, ColorFor(kind));
            events.DirtBurst += (positionPx, tile, count, direction, speed) => ParticleView.DirtBurst(positionPx, tile, count, direction, speed);
        }

        static Color ColorFor(TextKind kind) => kind switch
        {
            TextKind.PlayerDamage => CombatText.PlayerDamaged,
            TextKind.Heal => CombatText.Healed,
            _ => CombatText.DummyDamaged,
        };
    }
}
