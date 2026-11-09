using CTG.Combat;
using CTG.Core;
using CTG.World;
using UnityEngine;

namespace CTG.Gameplay
{
    /// <summary>
    /// The gem objective. Each team has a gem on a pedestal at its base. A player touches the enemy gem to
    /// pick it up (first to touch wins; only one carrier at a time), carries it to their own gem to capture
    /// it, and drops it by dying. The gem returns to its pedestal on capture. After ten minutes the match goes
    /// to overtime: a team wins once it leads on captures and the enemy isn't carrying its gem, or leads by
    /// more than one capture. Until networking exists this runs on the local client against the local player.
    /// </summary>
    public class GemMatch : MonoBehaviour, ITickable
    {
        public const int RegularTimeTicks = 10 * 60 * SimUnits.TicksPerSecond;
        const int k_MessageTicks = 5 * SimUnits.TicksPerSecond;

        public Gem BlueGem { get; private set; }
        public Gem RedGem { get; private set; }
        public int BlueCaptures { get; private set; }
        public int RedCaptures { get; private set; }
        public int MatchTicks { get; private set; }
        public bool IsOvertime { get; private set; }
        public bool IsOver { get; private set; }
        public Team Winner { get; private set; }
        /// <summary>This player's totals, for the end-of-match summary.</summary>
        public int Pickups { get; private set; }
        public int Captures { get; private set; }
        /// <summary>The latest announcement, shown for a few seconds.</summary>
        public string Message { get; private set; } = "";
        public Color MessageColor { get; private set; } = Color.cyan;
        public int MessageTicksLeft { get; private set; }

        PlayerCharacter m_Player;

        /// <summary>The match clock and objective only run while this is set (the flow sets it when play starts).</summary>
        public bool Running { get; set; }

        public static readonly Color BlueMessage = new Color32(0, 119, 182, 255);
        public static readonly Color RedMessage = new Color32(255, 0, 0, 255);

        public static GemMatch Create(Transform parent, LoadedMap map, PlayerCharacter player)
        {
            if (!map.BlueGem.HasValue || !map.RedGem.HasValue) return null;

            var go = new GameObject("Gem Match");
            go.transform.SetParent(parent, false);
            var match = go.AddComponent<GemMatch>();
            match.m_Player = player;
            match.BlueGem = new Gem(Team.Blue, ToPx(map.BlueGem.Value));
            match.RedGem = new Gem(Team.Red, ToPx(map.RedGem.Value));
            // Registered after the player so this tick's pickups see the player's new position.
            SimulationClock.Instance.Register(match);
            return match;
        }

        static Rect ToPx(RectInt tiles) => new(tiles.x * SimUnits.PixelsPerTile, tiles.y * SimUnits.PixelsPerTile,
                                               tiles.width * SimUnits.PixelsPerTile, tiles.height * SimUnits.PixelsPerTile);

        public Gem GemOf(Team team) => team == Team.Blue ? BlueGem : RedGem;
        public Gem EnemyGemOf(Team team) => team == Team.Blue ? RedGem : BlueGem;

        /// <summary>"At Base", or the carrier's name and life.</summary>
        public string CarrierText(Gem gem) =>
            gem.IsHeld && gem.Holder != null ? $"Player: {gem.Holder.Health.Current}/{gem.Holder.Health.Max}" : "At Base";

        /// <summary>How far a carried gem is toward the enemy base, 0..1 (0 when at its pedestal).</summary>
        public float Progress(Gem gem)
        {
            if (!gem.IsHeld || gem.Holder == null) return 0f;

            float between = Mathf.Abs(RedGem.HitboxPx.x - BlueGem.HitboxPx.x);
            if (between <= 0f) return 0f;

            float holderX = gem.Holder.HitboxPx.x;
            float travelled = gem.Team == Team.Red ? RedGem.HitboxPx.x - holderX : holderX - BlueGem.HitboxPx.x;
            return Mathf.Clamp01(travelled / between);
        }

        public void Tick(int tick)
        {
            if (MessageTicksLeft > 0) MessageTicksLeft--;
            if (!Running || IsOver || m_Player == null || m_Player.Team == Team.Neutral) return;

            MatchTicks++;
            UpdatePlayer(m_Player);

            if (BlueGem.IsCaptured)
            {
                RedCaptures++;
                BlueGem.Reset();
            }
            else if (RedGem.IsCaptured)
            {
                BlueCaptures++;
                RedGem.Reset();
            }

            bool blueWin = BlueCaptures - RedCaptures > 1 || (!BlueGem.IsHeld && BlueCaptures > RedCaptures);
            bool redWin = RedCaptures - BlueCaptures > 1 || (!RedGem.IsHeld && RedCaptures > BlueCaptures);

            if (IsOvertime)
            {
                if (blueWin) End(Team.Blue);
                else if (redWin) End(Team.Red);
            }
            else if (MatchTicks >= RegularTimeTicks)
            {
                IsOvertime = true;
                if (blueWin) End(Team.Blue);
                else if (redWin) End(Team.Red);
                else Announce("Overtime has started! The game will end when a team has more captures and their gem is not possessed.", Color.cyan);
            }
        }

        void UpdatePlayer(PlayerCharacter player)
        {
            Gem enemy = EnemyGemOf(player.Team);
            Gem own = GemOf(player.Team);
            bool alive = !player.Health.IsDead;
            Rect body = player.HitboxPx;
            string enemyName = enemy.Team == Team.Red ? "red" : "blue";
            Color enemyColor = enemy.Team == Team.Red ? RedMessage : BlueMessage;

            // Touching the enemy gem picks it up if nobody is carrying it.
            if (alive && !enemy.IsHeld && body.Overlaps(enemy.HitboxPx))
            {
                enemy.IsHeld = true;
                enemy.Holder = player;
                Pickups++;
                Announce($"Player has picked up the {enemyName} team's gem!", enemyColor);
            }

            if (!enemy.IsHeld || enemy.Holder != player) return;

            // Dying drops it; it is back on its pedestal.
            if (!alive)
            {
                enemy.Reset();
                Announce($"Player has dropped the {enemyName} team's gem!", enemyColor);
                return;
            }

            // Carrying it into the own gem's area captures it.
            if (body.Overlaps(own.HitboxPx))
            {
                enemy.IsCaptured = true;
                enemy.IsHeld = false;
                enemy.Holder = null;
                Captures++;
                Announce($"Player has captured the {enemyName} team's gem!", enemyColor);
            }
        }

        /// <summary>Testing: ends the running match now, in favour of whoever leads on captures (blue on a tie).</summary>
        public void DevEnd()
        {
            if (!Running || IsOver) return;

            End(BlueCaptures >= RedCaptures ? Team.Blue : Team.Red);
        }

        void End(Team winner)
        {
            IsOver = true;
            Winner = winner;
            Announce($"{(winner == Team.Blue ? "Blue" : "Red")} team wins!  Pickups {Pickups}, captures {Captures}", winner == Team.Blue ? BlueMessage : RedMessage);
            MessageTicksLeft = int.MaxValue;
        }

        public void Announce(string text, Color color)
        {
            Message = text;
            MessageColor = color;
            MessageTicksLeft = k_MessageTicks;
        }
    }
}
