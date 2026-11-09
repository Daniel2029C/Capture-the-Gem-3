using System.Collections.Generic;
using CTG.Core;
using UnityEngine;

namespace CTG.Rendering
{
    public enum FringeKind
    {
        /// <summary>Moss and grass blades rising from the top of earth.</summary>
        Tuft,
        /// <summary>Roots hanging from the underside of earth.</summary>
        Root,
        /// <summary>Soft light above the surface of lava.</summary>
        LavaGlow,
        /// <summary>Soft light above the surface of nectar.</summary>
        NectarGlow,
    }

    /// <summary>
    /// Decoration drawn in the empty tiles next to terrain: it breaks up the grid without touching collision.
    /// Every sprite is one full tile (16×16); tufts and glows sit at the bottom of the tile, roots at the top.
    /// </summary>
    public static class FringeArt
    {
        public const int Variants = 4;

        const int k_Size = SimUnits.PixelsPerTile;
        static readonly Dictionary<string, Sprite> s_Cache = new();

        public static Sprite Get(FringeKind kind, int variant)
        {
            string key = $"fringe_{kind}_{variant}";
            if (s_Cache.TryGetValue(key, out Sprite cached)) return cached;

            var pixels = new Color[k_Size * k_Size];
            var random = new System.Random(variant * 977 + (int)kind * 4099 + 5);
            switch (kind)
            {
                case FringeKind.Tuft: DrawTuft(pixels, random); break;
                case FringeKind.Root: DrawRoots(pixels, random); break;
                case FringeKind.LavaGlow: DrawGlow(pixels, random, new Color(1f, 0.42f, 0.1f), 0.5f); break;
                default: DrawGlow(pixels, random, new Color(1f, 0.72f, 0.25f), 0.4f); break;
            }

            var texture = new Texture2D(k_Size, k_Size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels(pixels);
            texture.Apply();
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, k_Size, k_Size), Vector2.zero, SimUnits.PixelsPerTile);
            sprite.name = key;
            s_Cache[key] = sprite;
            return sprite;
        }

        static void Put(Color[] pixels, int x, int y, Color c)
        {
            if (x < 0 || x >= k_Size || y < 0 || y >= k_Size) return;
            pixels[y * k_Size + x] = c;
        }

        static void DrawTuft(Color[] pixels, System.Random random)
        {
            Color baseMoss = new Color(0.12f, 0.24f, 0.2f);
            Color tipMoss = new Color(0.3f, 0.55f, 0.4f);

            // A thin moss cap along the ground.
            for (int x = 0; x < k_Size; x++)
            {
                Put(pixels, x, 0, baseMoss);
                if (random.NextDouble() < 0.7) Put(pixels, x, 1, baseMoss * 1.1f);
            }

            // Blades with a slight lean.
            for (int x = 0; x < k_Size; x++)
            {
                if (random.NextDouble() > 0.5) continue;

                int height = random.Next(3, 10);
                float lean = (float)(random.NextDouble() - 0.5) * 0.5f;
                for (int y = 1; y < height; y++)
                {
                    float t = y / (float)height;
                    Color c = Color.Lerp(baseMoss, tipMoss, t);
                    Put(pixels, x + Mathf.RoundToInt(lean * y), y, c);
                }
            }
        }

        static void DrawRoots(Color[] pixels, System.Random random)
        {
            Color rootDark = new Color(0.2f, 0.16f, 0.15f);
            Color rootLight = new Color(0.36f, 0.28f, 0.24f);

            int top = k_Size - 1;
            for (int x = 0; x < k_Size; x++)
            {
                Put(pixels, x, top, rootDark);
            }

            for (int x = 0; x < k_Size; x++)
            {
                if (random.NextDouble() > 0.35) continue;

                int length = random.Next(3, 14);
                float sway = (float)(random.NextDouble() - 0.5) * 0.6f;
                for (int d = 1; d <= length; d++)
                {
                    float t = d / (float)length;
                    Put(pixels, x + Mathf.RoundToInt(sway * d), top - d, Color.Lerp(rootDark, rootLight, t * 0.7f));
                }
            }
        }

        static void DrawGlow(Color[] pixels, System.Random random, Color colour, float strength)
        {
            for (int y = 0; y < k_Size; y++)
            {
                float fall = Mathf.Pow(Mathf.Max(0f, 1f - y / 13f), 1.8f);
                for (int x = 0; x < k_Size; x++)
                {
                    float flicker = 0.85f + (float)random.NextDouble() * 0.3f;
                    Color c = colour;
                    c.a = strength * fall * flicker;
                    Put(pixels, x, y, c);
                }
            }
        }
    }
}
