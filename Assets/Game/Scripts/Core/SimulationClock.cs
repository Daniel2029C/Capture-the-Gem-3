using System.Collections.Generic;
using UnityEngine;

namespace CTG.Core
{
    public interface ITickable
    {
        /// <summary>Advances one fixed 1/60 s simulation step.</summary>
        void Tick(int tick);
    }

    /// <summary>
    /// Fixed 60 Hz simulation loop, independent of frame rate and of Unity's physics timestep.
    /// Rendering interpolates between the last two ticks using <see cref="Alpha"/>. Keeping gameplay on
    /// discrete ticks is what lets Terraria's per-tick numbers port exactly, and is what a networked
    /// simulation will need later.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class SimulationClock : MonoBehaviour
    {
        const float k_MaxFrameTime = 0.25f;

        static SimulationClock s_Instance;
        readonly List<ITickable> m_Tickables = new();
        float m_Accumulator;

        public static SimulationClock Instance => s_Instance;

        /// <summary>Ticks simulated since start.</summary>
        public int CurrentTick { get; private set; }

        /// <summary>0..1 fraction of the way from the previous tick to the next, for interpolation.</summary>
        public float Alpha { get; private set; }

        public static event System.Action BeforeTick;

        void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(this);
                return;
            }

            s_Instance = this;
        }

        void OnDestroy()
        {
            if (s_Instance == this) s_Instance = null;
        }

        public void Register(ITickable tickable)
        {
            if (!m_Tickables.Contains(tickable)) m_Tickables.Add(tickable);
        }

        public void Unregister(ITickable tickable) => m_Tickables.Remove(tickable);

        void Update()
        {
            m_Accumulator += Mathf.Min(Time.deltaTime, k_MaxFrameTime);

            while (m_Accumulator >= TerrariaUnits.TickSeconds)
            {
                m_Accumulator -= TerrariaUnits.TickSeconds;
                BeforeTick?.Invoke();

                for (int i = 0; i < m_Tickables.Count; i++)
                {
                    m_Tickables[i].Tick(CurrentTick);
                }

                CurrentTick++;
            }

            Alpha = m_Accumulator / TerrariaUnits.TickSeconds;
        }
    }
}
