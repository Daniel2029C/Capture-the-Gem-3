using System.Collections.Generic;
using CTG.Core;
using UnityEngine;

namespace CTG.Rendering
{
    /// <summary>
    /// Generates simple pixel-art placeholder sprites at runtime, so the build carries no template or
    /// third-party art. Every sprite is 16 pixels per unit with point filtering, matching the tile grid.
    /// </summary>
    public static class PlaceholderSprites
    {
        static readonly Dictionary<string, Sprite> s_Cache = new();

        static Texture2D NewTexture(int width, int height)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
        }

        static Sprite Finish(string key, Texture2D texture, Vector2 pivot)
        {
            texture.Apply();
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), pivot,
                                          TerrariaUnits.PixelsPerTile);
            sprite.name = key;
            s_Cache[key] = sprite;
            return sprite;
        }

        /// <summary>A 16×16 tile with a darker speckle and edge so neighbouring tiles read as a grid.</summary>
        public static Sprite Tile(string key, Color baseColor)
        {
            if (s_Cache.TryGetValue(key, out Sprite cached)) return cached;

            const int size = TerrariaUnits.PixelsPerTile;
            Texture2D texture = NewTexture(size, size);
            var random = new System.Random(key.GetHashCode());

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float shade = 0.9f + (float)random.NextDouble() * 0.15f;
                    if (x == 0 || y == 0) shade *= 0.8f;
                    Color c = baseColor * shade;
                    c.a = 1f;
                    texture.SetPixel(x, y, c);
                }
            }

            return Finish(key, texture, Vector2.zero);
        }

        /// <summary>Player body: a coloured rectangle with a head and an eye showing the facing direction.</summary>
        public static Sprite Player(int widthPx, int heightPx)
        {
            string key = $"player_{widthPx}x{heightPx}";
            if (s_Cache.TryGetValue(key, out Sprite cached)) return cached;

            Texture2D texture = NewTexture(widthPx, heightPx);
            Color body = new Color32(70, 110, 200, 255);
            Color legs = new Color32(50, 60, 110, 255);
            Color skin = new Color32(235, 190, 150, 255);
            Color outline = new Color32(20, 20, 30, 255);
            int headHeight = heightPx / 4;
            int legHeight = heightPx * 3 / 8;

            for (int y = 0; y < heightPx; y++)
            {
                for (int x = 0; x < widthPx; x++)
                {
                    bool edge = x == 0 || y == 0 || x == widthPx - 1 || y == heightPx - 1;
                    Color c = y >= heightPx - headHeight ? skin : y < legHeight ? legs : body;
                    texture.SetPixel(x, y, edge ? outline : c);
                }
            }

            // Eye on the right side; the renderer flips the sprite for left-facing.
            int eyeY = heightPx - headHeight / 2;
            for (int dy = -1; dy <= 0; dy++)
            {
                for (int dx = 0; dx < 2; dx++)
                {
                    texture.SetPixel(widthPx - 5 + dx, eyeY + dy, outline);
                }
            }

            return Finish(key, texture, new Vector2(0.5f, 0f));
        }

        /// <summary>A held tool or block icon, pivoted at the grip (bottom-left).</summary>
        public static Sprite Pickaxe(Color headColor)
        {
            string key = $"pickaxe_{ColorUtility.ToHtmlStringRGB(headColor)}";
            if (s_Cache.TryGetValue(key, out Sprite cached)) return cached;

            const int size = 16;
            Texture2D texture = NewTexture(size, size);
            Color clear = Color.clear;
            Color handle = new Color32(120, 80, 40, 255);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    texture.SetPixel(x, y, clear);
                }
            }

            // Diagonal handle from the grip to the head.
            for (int i = 0; i < 12; i++)
            {
                texture.SetPixel(i, i, handle);
                texture.SetPixel(i + 1, i, handle);
            }

            // Curved head across the top-right.
            for (int i = 0; i < 9; i++)
            {
                texture.SetPixel(7 + i, 15 - i / 3, headColor);
                texture.SetPixel(15 - i / 3, 7 + i, headColor);
                texture.SetPixel(8 + i, 14 - i / 3, headColor);
            }

            return Finish(key, texture, Vector2.zero);
        }

        public static Sprite Square(string key, Color color, int sizePx)
        {
            if (s_Cache.TryGetValue(key, out Sprite cached)) return cached;

            Texture2D texture = NewTexture(sizePx, sizePx);
            for (int y = 0; y < sizePx; y++)
            {
                for (int x = 0; x < sizePx; x++)
                {
                    texture.SetPixel(x, y, color);
                }
            }

            return Finish(key, texture, Vector2.zero);
        }

        /// <summary>A 16×16 hollow square, for the tile cursor.</summary>
        public static Sprite Outline(string key, Color color)
        {
            if (s_Cache.TryGetValue(key, out Sprite cached)) return cached;

            const int size = TerrariaUnits.PixelsPerTile;
            Texture2D texture = NewTexture(size, size);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool edge = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                    texture.SetPixel(x, y, edge ? color : Color.clear);
                }
            }

            return Finish(key, texture, Vector2.zero);
        }
    }
}
