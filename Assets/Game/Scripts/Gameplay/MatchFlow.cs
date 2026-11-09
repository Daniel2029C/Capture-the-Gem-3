using System;
using CTG.Combat;
using CTG.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CTG.Gameplay
{
    public enum MatchPhase
    {
        /// <summary>Waiting for a class; the simulation is paused.</summary>
        ClassSelect,
        /// <summary>Held in the spawn room while the clock counts down.</summary>
        Countdown,
        Playing,
        /// <summary>A team won; the world is frozen under the results.</summary>
        Ended,
    }

    /// <summary>What a finished match reports to whoever is listening (the results screen now, the main menu later).</summary>
    public struct MatchResult
    {
        public Team Winner;
        public int BlueCaptures;
        public int RedCaptures;
        public int MatchTicks;
        public bool WentToOvertime;
        public int Pickups;
        public int Captures;
    }

    /// <summary>
    /// The stages of a match: class select → countdown → playing → ended. It owns when the objective starts
    /// counting and when everything stops, and reports the outcome through <see cref="Finished"/>. There is no main menu
    /// yet, so "play again" reloads the scene; a menu would subscribe to <see cref="Finished"/> and replace
    /// <see cref="PlayAgain"/> with a return to it.
    /// </summary>
    public class MatchFlow : MonoBehaviour, ITickable
    {
        public const int CountdownTicks = 5 * SimUnits.TicksPerSecond;

        SimulationClock m_Clock;
        PlayerCharacter m_Player;
        GemMatch m_Match;

        public MatchPhase Phase { get; private set; } = MatchPhase.ClassSelect;
        public int CountdownTicksLeft { get; private set; }
        public MatchResult? Result { get; private set; }

        /// <summary>Raised once when the match ends.</summary>
        public event Action<MatchResult> Finished;

        /// <param name="match">The gem objective, or null on a map without gems (the match then never ends on its own).</param>
        public static MatchFlow Create(Transform parent, SimulationClock clock, PlayerCharacter player, GemMatch match)
        {
            var flow = new GameObject("Match Flow").AddComponent<MatchFlow>();
            flow.transform.SetParent(parent, false);
            flow.m_Clock = clock;
            flow.m_Player = player;
            flow.m_Match = match;
            clock.Register(flow);
            return flow;
        }

        /// <summary>The player picked a class: start the countdown.</summary>
        public void ClassChosen()
        {
            if (Phase != MatchPhase.ClassSelect) return;

            Phase = MatchPhase.Countdown;
            CountdownTicksLeft = CountdownTicks;
            m_Player.InputLocked = true;
            m_Clock.enabled = true;
        }

        public void Tick(int tick)
        {
            if (Phase != MatchPhase.Countdown) return;

            if (--CountdownTicksLeft > 0) return;

            Phase = MatchPhase.Playing;
            m_Player.InputLocked = false;
            if (m_Match != null)
            {
                m_Match.Running = true;
                m_Match.Announce("The match is starting!", Color.cyan);
            }
        }

        void Update()
        {
            if (Phase != MatchPhase.Playing || m_Match == null || !m_Match.IsOver) return;

            Phase = MatchPhase.Ended;
            Result = new MatchResult
            {
                Winner = m_Match.Winner, BlueCaptures = m_Match.BlueCaptures, RedCaptures = m_Match.RedCaptures,
                MatchTicks = m_Match.MatchTicks, WentToOvertime = m_Match.IsOvertime,
                Pickups = m_Match.Pickups, Captures = m_Match.Captures,
            };

            // Freeze the world under the results.
            m_Clock.enabled = false;
            Finished?.Invoke(Result.Value);
        }

        /// <summary>Starts a fresh match by reloading the scene (everything is rebuilt from the map).</summary>
        public void PlayAgain() => SceneManager.LoadScene(SceneManager.GetActiveScene().name);

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
