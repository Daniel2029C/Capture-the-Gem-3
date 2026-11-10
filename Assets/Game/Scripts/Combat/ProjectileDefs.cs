using UnityEngine;

namespace CTG.Combat
{
    public enum ProjectileId
    {
        None = 0,
        /// <summary>Falls after a short delay and explodes when it hits anything or expires.</summary>
        EmberArrow = 1,
        /// <summary>Rises after a short delay (inverted gravity) and stops at the first target.</summary>
        GleamArrow = 2,
        /// <summary>Thrown by the Tree: flies out, turns back, and homes in on its owner.</summary>
        GroveBoomerang = 3,
        /// <summary>Archer's quick shot: fast, straight, short range, no knockback.</summary>
        LightArrow = 4,
        /// <summary>Archer's armed shot: pierces several targets.</summary>
        LuminiteArrow = 5,
        /// <summary>Second shot of a Luminite arrow: launched from the original firing spot once the first arrow ends.</summary>
        LuminiteFollowUp = 6,
        /// <summary>A Drakon's fireball: slow, straight, ends on the first thing it hits.</summary>
        Fireball = 7,
    }

    /// <summary>
    /// Projectile data. Speeds are px per update and lifetimes are counted in updates; a projectile with
    /// extra updates runs several updates per tick, so it moves (and falls) faster in real time.
    /// Velocities are y-up.
    /// </summary>
    /// <summary>
    /// Boomerang flight: straight out for <see cref="OutboundUpdates"/> updates (or until it hits a tile or a
    /// target), then back to the owner, ignoring tiles, accelerating toward them up to a top speed. It ends on
    /// touching the owner, and is dropped if it ever gets 3000 px away.
    /// </summary>
    public sealed class BoomerangStats
    {
        public int OutboundUpdates = 30;
        /// <summary>Top speed on the way back, px/update. Raised from the original 9 for a snappier return.</summary>
        public float ReturnMaxSpeed = 17f;
        /// <summary>Speed gained per update toward the owner (doubled while still moving away from them). Original 0.4.</summary>
        public float ReturnAccel = 1f;
        /// <summary>Updates before the same target can be hit again.</summary>
        public int RehitCooldownUpdates = 10;
        public float MaxOwnerDistancePx = 3000f;
    }

    public sealed class ProjectileDef
    {
        public ProjectileId Id;
        public string Name;
        public int WidthPx = 10;
        public int HeightPx = 10;
        /// <summary>Updates before the projectile expires.</summary>
        public int LifetimeUpdates = 3600;
        /// <summary>Targets it can hit before it's used up. -1 = unlimited.</summary>
        public int Penetrate = 1;
        /// <summary>Updates of straight flight before gravity starts acting.</summary>
        public int GravityDelayUpdates = 15;
        /// <summary>Vertical acceleration per update once gravity acts (y-up: negative falls, positive rises).</summary>
        public float GravityPerUpdate = -0.1f;
        /// <summary>Downward speed cap, px/update.</summary>
        public float MaxFallSpeed = 16f;
        /// <summary>Upward speed cap, px/update. Only enforced for projectiles that rise.</summary>
        public float MaxRiseSpeed = float.PositiveInfinity;
        /// <summary>Hitting a target ends the projectile within the same update (so it explodes on contact).</summary>
        public bool EndsOnHit;
        /// <summary>Drawn as a round blob instead of an arrow.</summary>
        public bool Round;
        /// <summary>Side of the square blast that hits everything around it when the projectile ends. 0 = none.</summary>
        public int ExplosionSizePx;
        public Color Color = Color.white;
        /// <summary>
        /// A projectile launched when this one ends (for any reason): it starts at this one's firing position with this
        /// one's launch velocity, and inherits its current damage. None = no follow-up.
        /// </summary>
        public ProjectileId FollowUp = ProjectileId.None;
        /// <summary>Extra updates per tick for the follow-up.</summary>
        public int FollowUpExtraUpdates;
        /// <summary>Damage is multiplied by this after each target hit (1 = unchanged).</summary>
        public float DamageDecayPerHit = 1f;
        /// <summary>Set for boomerangs: flight is steered by these stats instead of gravity.</summary>
        public BoomerangStats Boomerang;
    }

    public static class ProjectileDefs
    {
        static readonly ProjectileDef s_None = new() { Id = ProjectileId.None, Name = "" };

        // 10×10, unlimited pierce but ends on the first hit, 3600-update lifetime, falls at 0.1/update
        // after 15 updates (cap 16). Explodes in a 64×64 blast however it ends.
        static readonly ProjectileDef s_Ember = new()
        {
            Id = ProjectileId.EmberArrow, Name = "Ember Arrow",
            LifetimeUpdates = 3600, Penetrate = -1, EndsOnHit = true, ExplosionSizePx = 64,
            GravityDelayUpdates = 15, GravityPerUpdate = -0.1f, MaxFallSpeed = 16f,
            Color = new Color32(255, 140, 40, 255),
        };

        // 10×10, hits one target, 1200-update lifetime, rises at 0.1/update after 15 updates
        // (capped at 16 both ways).
        static readonly ProjectileDef s_Gleam = new()
        {
            Id = ProjectileId.GleamArrow, Name = "Gleam Arrow",
            LifetimeUpdates = 1200, Penetrate = 1,
            GravityDelayUpdates = 15, GravityPerUpdate = 0.1f, MaxFallSpeed = 16f, MaxRiseSpeed = 16f,
            Color = new Color32(230, 170, 255, 255),
        };

        // 22×22, never used up (it can hit the same target again on the way back), no gravity.
        static readonly ProjectileDef s_GroveBoomerang = new()
        {
            Id = ProjectileId.GroveBoomerang, Name = "Grove Boomerang",
            WidthPx = 22, HeightPx = 22, LifetimeUpdates = 3600, Penetrate = -1,
            GravityPerUpdate = 0f, Boomerang = new BoomerangStats(),
            Color = new Color32(235, 205, 70, 255),
        };

        // Light arrow: 10×10, hits one target, flies dead straight (no gravity) and expires after 20 updates,
        // about 25 tiles at its launch speed of 22 px/update.
        static readonly ProjectileDef s_Light = new()
        {
            Id = ProjectileId.LightArrow, Name = "Light Arrow",
            LifetimeUpdates = 20, Penetrate = 1, EndsOnHit = true,
            GravityDelayUpdates = 1000, GravityPerUpdate = 0f,
            Color = new Color32(255, 250, 200, 255),
        };

        // Luminite arrow: 10×10, passes through up to 4 targets, 90-update lifetime, gravity after 14 updates, damage x0.96 per
        // hit. When it ends it launches the follow-up shot from where it was fired.
        static readonly ProjectileDef s_Luminite = new()
        {
            Id = ProjectileId.LuminiteArrow, Name = "Luminite Arrow",
            LifetimeUpdates = 90, Penetrate = 4,
            GravityDelayUpdates = 14, GravityPerUpdate = -0.1f, MaxFallSpeed = 16f,
            DamageDecayPerHit = 0.96f,
            FollowUp = ProjectileId.LuminiteFollowUp, FollowUpExtraUpdates = 2,
            Color = new Color32(90, 255, 200, 255),
        };

        // Follow-up shot: same body and flight as the first, run three updates per tick, damage x0.96 per hit.
        static readonly ProjectileDef s_LuminiteFollowUp = new()
        {
            Id = ProjectileId.LuminiteFollowUp, Name = "Luminite Arrow",
            LifetimeUpdates = 90, Penetrate = 4,
            GravityDelayUpdates = 15, GravityPerUpdate = -0.1f, MaxFallSpeed = 16f,
            DamageDecayPerHit = 0.96f,
            Color = new Color32(150, 255, 225, 255),
        };

        // Fireball: 12×12, hits one target, flies straight and expires after 200 updates (about 55 tiles at 4.5 px/update).
        static readonly ProjectileDef s_Fireball = new()
        {
            Id = ProjectileId.Fireball, Name = "Fireball",
            WidthPx = 12, HeightPx = 12, LifetimeUpdates = 200, Penetrate = 1, EndsOnHit = true, Round = true,
            GravityDelayUpdates = 100000, GravityPerUpdate = 0f,
            Color = new Color32(255, 120, 30, 255),
        };

        public static ProjectileDef Get(ProjectileId id) => id switch
        {
            ProjectileId.Fireball => s_Fireball,
            ProjectileId.LightArrow => s_Light,
            ProjectileId.LuminiteArrow => s_Luminite,
            ProjectileId.LuminiteFollowUp => s_LuminiteFollowUp,
            ProjectileId.GroveBoomerang => s_GroveBoomerang,
            ProjectileId.EmberArrow => s_Ember,
            ProjectileId.GleamArrow => s_Gleam,
            _ => s_None,
        };
    }
}
