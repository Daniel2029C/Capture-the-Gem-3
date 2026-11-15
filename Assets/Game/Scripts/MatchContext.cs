using CTG.Combat;
using CTG.Core;
using CTG.Economy;
using CTG.Gameplay;

namespace CTG
{
    /// <summary>
    /// The services that belong to one match: the simulation clock, the registries of players and hittable things,
    /// the projectile system and the economy. It is created when the match is built and ended when it is torn
    /// down, so nothing leaks from one match into the next (a scene reload, or a second match in the same process).
    /// Systems reach their services through <see cref="Current"/>; the one that builds the match sets them up.
    /// </summary>
    public sealed class MatchContext
    {
        /// <summary>The match being played, or null when none is built (the editor, or after teardown).</summary>
        public static MatchContext Current { get; private set; }

        /// <summary>The fixed-step simulation loop. Set by whatever builds the match, before anything registers with it.</summary>
        public SimulationClock Clock { get; set; }

        /// <summary>Projectile simulation. Set once the match's projectile system is built.</summary>
        public ProjectileSystem Projectiles { get; set; }

        public MatchEconomy Economy { get; } = new();
        public DamageableRegistry Damageables { get; } = new();
        public PlayerRegistry Players { get; } = new();

        /// <summary>The player this client controls, or null (no match, or none is local).</summary>
        public static PlayerCharacter LocalPlayer => Current?.Players.Local;

        /// <summary>0..1 fraction of the way between simulation ticks for drawing, or 1 when there is no clock.</summary>
        public static float RenderAlpha => Current != null && Current.Clock != null ? Current.Clock.Alpha : 1f;

        /// <summary>Starts a new match, which becomes <see cref="Current"/>.</summary>
        public static MatchContext Begin()
        {
            Current = new MatchContext();
            return Current;
        }

        /// <summary>Ends this match: clears its registries and, if it is the current one, stops being current.</summary>
        public void End()
        {
            Damageables.Clear();
            Players.Clear();
            if (Current == this) Current = null;
        }
    }
}
