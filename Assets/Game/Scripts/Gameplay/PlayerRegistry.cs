using System.Collections.Generic;

namespace CTG.Gameplay
{
    /// <summary>
    /// Every player in the match (one registry per match, see <see cref="MatchContext"/>), with a stable id each, and which one this client controls. Systems that act on
    /// "the players" (the gem objective, match flow, the scoreboard) read it instead of holding a player of their own.
    /// Ids start at 1, are never reused during a match, and are what a network layer will use to name a player.
    /// </summary>
    public sealed class PlayerRegistry
    {
        readonly List<PlayerCharacter> m_All = new();
        int m_NextId = 1;

        /// <summary>All players, in the order they joined.</summary>
        public IReadOnlyList<PlayerCharacter> All => m_All;

        /// <summary>The player this client controls, or null. It decides what this client may see (teammates' offering orbs).</summary>
        public PlayerCharacter Local { get; private set; }

        public void Clear()
        {
            m_All.Clear();
            Local = null;
        }

        /// <summary>Adds a player and returns its id. <paramref name="isLocal"/> marks the one this client controls.</summary>
        public int Add(PlayerCharacter player, bool isLocal)
        {
            if (!m_All.Contains(player)) m_All.Add(player);
            if (isLocal) Local = player;
            return m_NextId++;
        }

        public void Remove(PlayerCharacter player)
        {
            m_All.Remove(player);
            if (Local == player) Local = null;
        }

        public PlayerCharacter Get(int id)
        {
            for (int i = 0; i < m_All.Count; i++)
            {
                if (m_All[i].Id == id) return m_All[i];
            }

            return null;
        }

        /// <summary>Players on the given team.</summary>
        public int CountOnTeam(Combat.Team team)
        {
            int count = 0;
            for (int i = 0; i < m_All.Count; i++)
            {
                if (m_All[i].Team == team) count++;
            }

            return count;
        }
    }
}
