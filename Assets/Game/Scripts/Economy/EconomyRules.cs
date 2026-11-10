namespace CTG.Economy
{
    /// <summary>Shards and experience from one event.</summary>
    public readonly struct Reward
    {
        public readonly int Shards;
        public readonly int Exp;

        public Reward(int shards, int exp)
        {
            Shards = shards;
            Exp = exp;
        }

        public bool IsEmpty => Shards == 0 && Exp == 0;
    }

    public enum AltarKind
    {
        Base,
        Mid,
    }

    /// <summary>The reward table. All numbers live here so they are easy to tune.</summary>
    public static class EconomyRules
    {
        /// <summary>Seconds after damaging an enemy during which a death counts as an assist for the damager.</summary>
        public const float AssistWindowSeconds = 10f;

        /// <summary>Shards for a kill before any bounty or penalty.</summary>
        public const int BaseKillShards = 30;
        /// <summary>Extra shards per kill the victim has made since they last died (their kill streak).</summary>
        public const int BountyPerStreakKill = 10;
        /// <summary>The streak stops adding bounty after this many kills (so the bounty tops out at +50).</summary>
        public const int MaxBountyStreak = 5;
        /// <summary>Shards removed for each death the victim has had since their last kill, so a player who keeps dying is never worth more.</summary>
        public const int DeathPenaltyPerDeath = 1;
        public const int MaxDeathPenalty = 5;
        /// <summary>Experience is flat: only shards scale with the bounty.</summary>
        public const int KillExp = 500;
        public const int AssistExp = 125;

        /// <summary>Shards for killing a player with this many kills since their last death and this many deaths since their last kill.</summary>
        public static int KillShards(int victimKillStreak, int victimDeathStreak)
        {
            int bounty = BountyPerStreakKill * System.Math.Min(System.Math.Max(victimKillStreak, 0), MaxBountyStreak);
            int penalty = System.Math.Min(System.Math.Max(victimDeathStreak, 0) * DeathPenaltyPerDeath, MaxDeathPenalty);
            return BaseKillShards + bounty - penalty;
        }

        /// <summary>The reward for killing (or assisting a kill on) a victim in this state. Assists give exp only.</summary>
        public static Reward Kill(int victimKillStreak, int victimDeathStreak, bool assist) =>
            assist ? new Reward(0, AssistExp) : new Reward(KillShards(victimKillStreak, victimDeathStreak), KillExp);

        /// <summary>The first kill of the match, either team: extra for the killer only.</summary>
        public static readonly Reward FirstKillBonus = new(25, 500);

        /// <summary>The last hit on a mob: a small reward on its own, on top of the offering the killer receives.</summary>
        public static readonly Reward MobLastHit = new(5, 50);

        /// <summary>The last hit on the mid-boss: a reward for the last-hitter alone (the team also gets the buff).</summary>
        public static readonly Reward BossLastHit = new(100, 1000);

        /// <summary>One offering cashed in at an altar.</summary>
        public static Reward Offering(AltarKind altar) => altar == AltarKind.Mid ? new Reward(20, 200) : new Reward(10, 100);

        /// <summary>The first gem barrier broken in the match: extra for the breaker only.</summary>
        public static readonly Reward FirstBarrierBonus = new(100, 500);
    }
}
