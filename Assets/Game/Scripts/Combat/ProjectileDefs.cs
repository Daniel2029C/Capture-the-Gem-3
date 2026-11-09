using UnityEngine;

namespace CTG.Combat
{
    public enum ProjectileId
    {
        None = 0,
        /// <summary>Falls after a short delay and explodes when it hits anything or expires.</summary>
        HellfireArrow = 1,
        /// <summary>Rises after a short delay (inverted gravity) and stops at the first target.</summary>
        ShimmerArrow = 2,
        /// <summary>Thrown by the Tree: flies out, turns back, and homes in on its owner.</summary>
        GroveBoomerang = 3,
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
        /// <summary>Side of the square blast that hits everything around it when the projectile ends. 0 = none.</summary>
        public int ExplosionSizePx;
        public Color Color = Color.white;
        /// <summary>Set for boomerangs: flight is steered by these stats instead of gravity.</summary>
        public BoomerangStats Boomerang;
    }

    public static class ProjectileDefs
    {
        static readonly ProjectileDef s_None = new() { Id = ProjectileId.None, Name = "" };

        // 10×10, unlimited pierce but ends on the first hit, 3600-update lifetime, falls at 0.1/update
        // after 15 updates (cap 16). Explodes in a 64×64 blast however it ends.
        static readonly ProjectileDef s_Hellfire = new()
        {
            Id = ProjectileId.HellfireArrow, Name = "Hellfire Arrow",
            LifetimeUpdates = 3600, Penetrate = -1, EndsOnHit = true, ExplosionSizePx = 64,
            GravityDelayUpdates = 15, GravityPerUpdate = -0.1f, MaxFallSpeed = 16f,
            Color = new Color32(255, 140, 40, 255),
        };

        // 10×10, hits one target, 1200-update lifetime, rises at 0.1/update after 15 updates
        // (capped at 16 both ways).
        static readonly ProjectileDef s_Shimmer = new()
        {
            Id = ProjectileId.ShimmerArrow, Name = "Shimmer Arrow",
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

        public static ProjectileDef Get(ProjectileId id) => id switch
        {
            ProjectileId.GroveBoomerang => s_GroveBoomerang,
            ProjectileId.HellfireArrow => s_Hellfire,
            ProjectileId.ShimmerArrow => s_Shimmer,
            _ => s_None,
        };
    }
}
