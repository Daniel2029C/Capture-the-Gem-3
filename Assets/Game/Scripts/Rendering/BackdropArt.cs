using System.Collections.Generic;
using CTG.Core;
using UnityEngine;

namespace CTG.Rendering
{
    /// <summary>Procedural sprites for the backdrop: cave silhouettes and the vignette.</summary>
    public static class BackdropArt
    {
        static readonly Dictionary<string, Sprite> s_Cache = new();

        const int k_Width = 512;   // px, tiles seamlessly sideways
        const int k_Height = 1088; // px, 68 tiles: a bit more than the default view

        /// <summary>Stalactites hanging from the top and stalagmites rising from the bottom, as one flat colour.</summary>
        public static Sprite Silhouette(string key, int seed, Color32 color, float coverage, int featureSeed)
        {
            if (s_Cache.TryGetValue(key, out Sprite cached)) return cached;

            var texture = new Texture2D(k_Width, k_Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
            };

            var clear = new Color32(0, 0, 0, 0);
            var pixels = new Color32[k_Width * k_Height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

            int[] hangs = Profile(seed, coverage, 0.28f);
            int[] rises = Profile(featureSeed, coverage, 0.2f);
            for (int x = 0; x < k_Width; x++)
            {
                for (int y = 0; y < hangs[x]; y++) pixels[(k_Height - 1 - y) * k_Width + x] = color;
                for (int y = 0; y < rises[x]; y++) pixels[y * k_Width + x] = color;
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return Make(key, texture);
        }

        /// <summary>Column heights of spikes: a wrapped run of narrow triangles of random size, so the strip tiles.</summary>
        static int[] Profile(int seed, float coverage, float maxFraction)
        {
            var heights = new int[k_Width];
            var rng = new System.Random(seed);
            int maxHeight = (int)(k_Height * maxFraction);
            int x = 0;
            while (x < k_Width)
            {
                int half = rng.Next(5, 18);
                int peak = (int)(maxHeight * (0.25f + 0.75f * (float)rng.NextDouble()));
                if (rng.NextDouble() > coverage) peak = rng.Next(0, 12);

                for (int i = -half; i <= half; i++)
                {
                    int column = (x + half + i) % k_Width;
                    int h = Mathf.RoundToInt(peak * (1f - Mathf.Abs(i) / (half + 1f)));
                    if (h > heights[column]) heights[column] = h;
                }

                x += half * 2 + rng.Next(0, 10);
            }

            return heights;
        }

        /// <summary>Transparent in the middle, darkening towards the edges and corners.</summary>
        public static Sprite Vignette()
        {
            const string key = "vignette";
            if (s_Cache.TryGetValue(key, out Sprite cached)) return cached;

            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f; // 0 centre .. 1 corner
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, d)) * 0.55f;
                    texture.SetPixel(x, y, new Color(0.02f, 0.03f, 0.07f, a));
                }
            }

            return Make(key, texture, size); // one unit square, scaled by the view
        }

        static Sprite Make(string key, Texture2D texture, float pixelsPerUnit = SimUnits.PixelsPerTile)
        {
            texture.Apply();
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f),
                                          pixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprite.name = key;
            s_Cache[key] = sprite;
            return sprite;
        }
    }
}
