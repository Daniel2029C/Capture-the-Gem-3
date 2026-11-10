using System.Collections.Generic;
using UnityEngine;

namespace CTG.Rendering
{
    /// <summary>Procedural sprites for mobs.</summary>
    public static class MobArt
    {
        static readonly Dictionary<string, Sprite> s_Cache = new();

        public const int DrakonWidthPx = 44, DrakonHeightPx = 30;

        /// <summary>A white filled circle (64 px), tinted and scaled for area telegraphs.</summary>
        public static Sprite Disc()
        {
            const string key = "disc";
            if (s_Cache.TryGetValue(key, out Sprite cached)) return cached;

            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size * 0.5f, size * 0.5f)) / (size * 0.5f);
                    float a = d > 1f ? 0f : d > 0.9f ? 1f : 0.55f; // brighter rim
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }

            texture.Apply();
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            sprite.name = key;
            s_Cache[key] = sprite;
            return sprite;
        }

        /// <summary>A small flying dragon facing right: body, head with an eye, tail and two wings (frame 0 up, frame 1 down).</summary>
        public static Sprite Drakon(int frame)
        {
            string key = $"drakon_{frame}";
            if (s_Cache.TryGetValue(key, out Sprite cached)) return cached;

            var texture = new Texture2D(DrakonWidthPx, DrakonHeightPx, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var clear = new Color32(0, 0, 0, 0);
            var pixels = new Color32[DrakonWidthPx * DrakonHeightPx];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

            var body = new Color32(150, 60, 130, 255);
            var belly = new Color32(230, 170, 90, 255);
            var wing = new Color32(110, 40, 100, 255);
            var wingEdge = new Color32(200, 90, 160, 255);

            void Set(int x, int y, Color32 c)
            {
                if (x >= 0 && x < DrakonWidthPx && y >= 0 && y < DrakonHeightPx) pixels[y * DrakonWidthPx + x] = c;
            }

            // Body: an ellipse in the middle.
            for (int y = 8; y < 22; y++)
            {
                for (int x = 10; x < 34; x++)
                {
                    float dx = (x - 22f) / 12f, dy = (y - 15f) / 7f;
                    if (dx * dx + dy * dy <= 1f) Set(x, y, y < 13 ? belly : body);
                }
            }

            // Head and snout to the right, tail to the left.
            for (int y = 13; y < 20; y++) for (int x = 33; x < 42; x++) Set(x, y, body);
            for (int x = 38; x < 43; x++) Set(x, 14, belly);
            Set(37, 18, new Color32(255, 230, 80, 255));
            for (int x = 1; x < 11; x++) Set(x, 14 + (x < 6 ? 2 : 0) - (x / 4), body);
            for (int x = 1; x < 6; x++) Set(x, 15 + (x < 6 ? 2 : 0) - (x / 4), body);

            // Wings: up for frame 0, down for frame 1.
            for (int i = 0; i < 12; i++)
            {
                int baseX = 14 + i;
                int reach = Mathf.Max(0, 12 - Mathf.Abs(i - 5) * 2);
                for (int k = 0; k < reach; k++)
                {
                    int y = frame == 0 ? 21 + k : 8 - k;
                    Set(baseX, y, k == reach - 1 ? wingEdge : wing);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, DrakonWidthPx, DrakonHeightPx), new Vector2(0.5f, 0.5f), 16f);
            sprite.name = key;
            s_Cache[key] = sprite;
            return sprite;
        }
    }
}
