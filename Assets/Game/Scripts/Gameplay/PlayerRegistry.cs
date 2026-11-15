using System.Collections.Generic;

namespace CTG.Gameplay
{
    /// <summary>
    /// Every player in the match, with a stable id each, and which one this client controls. Systems that act on
    /// "the players" (the gem objective, match flow, the scoreboard) read it instead of holding a player of their own.
    /// Ids start at 1, are never reused during a match, and are what a network layer will use to name a player.
    /// </summary>
    public static class PlayerRegistry
    {
        static readonly List<PlayerCharacter> s_All = new();
        static int s_NextId = 1;

        /// <summary>All players, in the order they joined.</summary>
        public static IReadOnlyList<PlayerCharacter> All => s_All;

        /// <summary>The player this client controls, or null. It decides what this client may see (teammates' offering orbs).</summary>
        public static PlayerCharacter Local { get; private set; }

        /// <summary>Adds a player and returns its id. <paramref name="isLocal"/> marks the one this client controls.</summary>
        public static int Add(PlayerCharacter player, bool isLocal)
        {
            if (!s_All.Contains(player)) s_All.Add(player);
            if (isLocal) Local = player;
            return s_NextId++;
        }

        public static void Remove(PlayerCharacter player)
        {
            s_All.Remove(player);
            if (Local == player) Local = null;
        }

        public static PlayerCharacter Get(int id)
        {
            for (int i = 0; i < s_All.Count; i++)
            {
                if (s_All[i].Id == id) return s_All[i];
            }

            return null;
        }

        /// <summary>Players on the given team.</summary>
        public static int CountOnTeam(Combat.Team team)
        {
            int count = 0;
            for (int i = 0; i < s_All.Count; i++)
            {
                if (s_All[i].Team == team) count++;
            }

            return count;
        }
    }
}
