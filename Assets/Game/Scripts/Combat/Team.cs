namespace CTG.Combat
{
    /// <summary>Teams: 0 = neutral (hittable by everyone), 1 = red, 2 = blue, 3 = monsters.</summary>
    public enum Team
    {
        Neutral = 0,
        Red = 1,
        Blue = 2,
        /// <summary>Hostile creatures (mobs): hostile to every player, never to each other.</summary>
        Monster = 3,
    }

    /// <summary>Which teams may hurt which. Pure rules, with no presentation.</summary>
    public static class Teams
    {
        /// <summary>Neutral things (the target dummy) are hostile to everyone; otherwise different teams are hostile, so monsters are never hostile to each other.</summary>
        public static bool AreHostile(Team a, Team b)
        {
            if (a == Team.Neutral || b == Team.Neutral) return true;
            return a != b;
        }
    }
}
