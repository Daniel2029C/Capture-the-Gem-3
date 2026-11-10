using System.Collections.Generic;
using CTG.Core;
using CTG.World;
using UnityEngine;

namespace CTG.Rendering
{
    /// <summary>
    /// Small cosmetic particles (dirt chips): a pooled set of tiny squares that fly out, fall under gravity and
    /// fade. Purely visual; runs on frame time and never affects the simulation.
    /// </summary>
    public sealed class ParticleView : MonoBehaviour
    {
        struct Particle
        {
            public Vector2 Position;   // px
            public Vector2 Velocity;   // px/s
            public float Life;
            public float MaxLife;
            public Color Color;
            public float Size;
        }

        const int k_MaxParticles = 600;
        const float k_GravityPxPerSec2 = 700f;

        public static ParticleView Instance { get; private set; }

        readonly List<Particle> m_Particles = new();
        readonly List<SpriteRenderer> m_Views = new();
        Sprite m_Pixel;

        public static ParticleView Create(Transform parent)
        {
            var go = new GameObject("Particles");
            go.transform.SetParent(parent, false);
            return go.AddComponent<ParticleView>();
        }

        void Awake()
        {
            Instance = this;
            m_Pixel = PlaceholderSprites.Square("particle_pixel", Color.white, 1);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>True for the tile kinds that shed dirt.</summary>
        public static bool IsDirt(TileId id) => id == TileId.BlueDirt || id == TileId.RedDirt;

        /// <summary>
        /// A burst of chips. <paramref name="direction"/> biases where they fly (zero = upward);
        /// <paramref name="speed"/> is the typical launch speed in px/s.
        /// </summary>
        public static void Burst(Vector2 positionPx, Color color, int count, Vector2 direction, float speed, float lifeSeconds = 0.5f)
        {
            if (Instance == null) return;

            for (int i = 0; i < count; i++) Instance.Add(positionPx, color, direction, speed, lifeSeconds);
        }

        /// <summary>Dirt chips in the tile's own colour, with slight brightness variation.</summary>
        public static void DirtBurst(Vector2 positionPx, TileId tile, int count, Vector2 direction, float speed)
        {
            if (!IsDirt(tile)) return;

            Burst(positionPx, TileDefs.Get(tile).Color, count, direction, speed);
        }

        void Add(Vector2 positionPx, Color color, Vector2 direction, float speed, float lifeSeconds)
        {
            if (m_Particles.Count >= k_MaxParticles) return;

            Vector2 bias = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.up;
            Vector2 random = Random.insideUnitCircle;
            Vector2 velocity = (bias * 0.7f + random * 0.8f) * speed * Random.Range(0.5f, 1f);
            float shade = Random.Range(0.75f, 1.15f);
            m_Particles.Add(new Particle
            {
                Position = positionPx + random * 3f,
                Velocity = velocity,
                Life = lifeSeconds * Random.Range(0.6f, 1f),
                MaxLife = lifeSeconds,
                Color = new Color(color.r * shade, color.g * shade, color.b * shade, 1f),
                Size = Random.value < 0.4f ? 3f : 2f,
            });
        }

        void Update() => Step(Time.deltaTime);

        /// <summary>Advances every particle by <paramref name="dt"/> seconds and refreshes the sprites.</summary>
        public void Step(float dt)
        {
            for (int i = m_Particles.Count - 1; i >= 0; i--)
            {
                Particle p = m_Particles[i];
                p.Life -= dt;
                if (p.Life <= 0f)
                {
                    m_Particles.RemoveAt(i);
                    continue;
                }

                p.Velocity.y -= k_GravityPxPerSec2 * dt;
                p.Position += p.Velocity * dt;
                m_Particles[i] = p;
            }

            while (m_Views.Count < m_Particles.Count)
            {
                var go = new GameObject("Particle");
                go.transform.SetParent(transform, false);
                var view = go.AddComponent<SpriteRenderer>();
                view.sprite = m_Pixel;
                view.sortingOrder = 9;
                m_Views.Add(view);
            }

            for (int i = 0; i < m_Views.Count; i++)
            {
                SpriteRenderer view = m_Views[i];
                bool used = i < m_Particles.Count;
                view.enabled = used;
                if (!used) continue;

                Particle p = m_Particles[i];
                // The 1 px sprite is pivoted at its corner and is 1/16 unit wide.
                view.transform.position = (p.Position - Vector2.one * (p.Size * 0.5f)) / SimUnits.PixelsPerTile;
                view.transform.localScale = new Vector3(p.Size, p.Size, 1f);
                Color c = p.Color;
                c.a = Mathf.Clamp01(p.Life / (p.MaxLife * 0.4f));
                view.color = c;
            }
        }
    }
}
