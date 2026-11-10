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
        /// <param name="victim">The player who died, still carrying the streak and deaths they had when killed.</param>
        /// <param name="assisters">Other players who damaged the victim recently.</param>
        public void RecordKill(PlayerCharacter killer, PlayerCharacter victim, IReadOnlyList<PlayerCharacter> assisters)
        {
            if (killer != null && victim != null)
            {
                Give(killer, EconomyRules.Kill(victim.KillStreak, victim.DeathStreak, false), "Kill");
                killer.AddKill();
                if (!m_FirstKillGiven)
                {
                    m_FirstKillGiven = true;
                    Give(killer, EconomyRules.FirstKillBonus, "First kill");
                }
            }

            if (assisters == null || victim == null) return;
            foreach (PlayerCharacter assister in assisters)
            {
                if (assister == killer) continue;

                Give(assister, EconomyRules.Kill(victim.KillStreak, victim.DeathStreak, true), "Assist");
                assister.AddAssist();
            }
        }

        /// <summary>The player who lands the last hit on a mob: a small reward and an offering to carry to an altar.</summary>
        public void AwardMobKill(PlayerCharacter killer)
        {
            if (killer == null) return;

            Give(killer, EconomyRules.MobLastHit, "Drakon");
            killer.GrantOffering();
        }

        /// <summary>The mid-boss dies: the last-hitter is rewarded and their whole team gets the Apex buff.</summary>
        public void AwardBossKill(PlayerCharacter killer)
        {
            if (killer == null) return;

            Give(killer, EconomyRules.BossLastHit, "Boss");
            var all = Combat.DamageableRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is PlayerCharacter player && player.Team == killer.Team) player.GrantApexBuff();
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
