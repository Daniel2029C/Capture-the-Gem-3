using System.Collections.Generic;
using CTG.Classes;
using CTG.Gameplay;
using CTG.UI;

namespace CTG.Economy
{
    /// <summary>
    /// Hands out rewards for match events and remembers the once-per-match bonuses (first kill, first barrier
    /// broken). Sources call in: <see cref="RecordKill"/> from player deaths, and the altar and barrier systems
    /// (not built yet) call <see cref="AwardOffering"/> and <see cref="AwardBarrierBroken"/>.
    /// </summary>
    public sealed class MatchEconomy
    {
        public static MatchEconomy Instance { get; private set; }

        bool m_FirstKillGiven;
        bool m_FirstBarrierGiven;

        public MatchEconomy() => Instance = this;

        /// <param name="killer">The player who dealt the killing blow.</param>
        /// <param name="victimClass">The class of the player who died.</param>
        /// <param name="assisters">Other players who damaged the victim recently.</param>
        public void RecordKill(PlayerCharacter killer, ClassDef victimClass, IReadOnlyList<PlayerCharacter> assisters)
        {
            if (killer != null && victimClass != null)
            {
                Give(killer, EconomyRules.Kill(victimClass.IsFrontline, false), "Kill");
                if (!m_FirstKillGiven)
                {
                    m_FirstKillGiven = true;
                    Give(killer, EconomyRules.FirstKillBonus, "First kill");
                }
            }

            if (assisters == null || victimClass == null) return;
            foreach (PlayerCharacter assister in assisters)
            {
                if (assister != killer) Give(assister, EconomyRules.Kill(victimClass.IsFrontline, true), "Assist");
            }
        }

        public void AwardOffering(PlayerCharacter player, AltarKind altar) =>
            Give(player, EconomyRules.Offering(altar), altar == AltarKind.Mid ? "Mid offering" : "Offering");

        /// <summary>Call when a gem barrier breaks from damage; only the first break of the match pays the bonus.</summary>
        public void AwardBarrierBroken(PlayerCharacter breaker)
        {
            if (m_FirstBarrierGiven) return;

            m_FirstBarrierGiven = true;
            Give(breaker, EconomyRules.FirstBarrierBonus, "Barrier broken");
        }

        void Give(PlayerCharacter player, Reward reward, string reason)
        {
            if (player == null || reward.IsEmpty) return;

            player.Progress.AddShards(reward.Shards);
            player.Progress.AddExp(reward.Exp);

            string text = reward.Shards > 0 ? $"{reason}  +{reward.Shards} shards  +{reward.Exp} xp" : $"{reason}  +{reward.Exp} xp";
            CombatText.Spawn(player.Motor.Center + new UnityEngine.Vector2(0f, 40f), text, CombatText.Healed);
        }
    }
}
