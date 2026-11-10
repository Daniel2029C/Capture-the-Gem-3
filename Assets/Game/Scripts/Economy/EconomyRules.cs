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

        /// <summary>Killing a non-frontline player (the Archer): 50 shards and 500 exp; an assist is 125 exp.</summary>
        public static Reward Kill(bool victimIsFrontline, bool assist)
        {
            if (victimIsFrontline) return assist ? new Reward(0, 75) : new Reward(25, 250);
            return assist ? new Reward(0, 125) : new Reward(50, 500);
        }

        /// <summary>The first kill of the match, either team: extra for the killer only.</summary>
        public static readonly Reward FirstKillBonus = new(25, 500);

        /// <summary>One offering cashed in at an altar.</summary>
        public static Reward Offering(AltarKind altar) => altar == AltarKind.Mid ? new Reward(20, 200) : new Reward(10, 100);

        /// <summary>The first gem barrier broken in the match: extra for the breaker only.</summary>
        public static readonly Reward FirstBarrierBonus = new(100, 500);
    }
}
