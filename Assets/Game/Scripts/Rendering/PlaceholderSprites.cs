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
                                          SimUnits.PixelsPerTile);
            sprite.name = key;
            s_Cache[key] = sprite;
            return sprite;
        }

        /// <summary>A 16×16 tile with a darker speckle and edge so neighbouring tiles read as a grid.</summary>
        public static Sprite Tile(string key, Color baseColor)
        {
            if (s_Cache.TryGetValue(key, out Sprite cached)) return cached;

            const int size = SimUnits.PixelsPerTile;
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

        /// <summary>Straw target dummy: a post with a round head and a target ring on the body.</summary>
        public static Sprite Dummy(int widthPx, int heightPx)
        {
            string key = $"dummy_{widthPx}x{heightPx}";
            if (s_Cache.TryGetValue(key, out Sprite cached)) return cached;

            Texture2D texture = NewTexture(widthPx, heightPx);
            Color straw = new Color32(214, 180, 100, 255);
            Color wood = new Color32(110, 75, 40, 255);
            Color ring = new Color32(200, 50, 50, 255);
            Color outline = new Color32(40, 30, 20, 255);
            int cx = widthPx / 2;

            for (int y = 0; y < heightPx; y++)
            {
                for (int x = 0; x < widthPx; x++)
                {
                    Color c = Color.clear;
                    bool post = Mathf.Abs(x - cx) <= 1 && y < heightPx / 4;
                    bool body = y >= heightPx / 4 && y < heightPx * 3 / 4 && x >= 2 && x < widthPx - 2;
                    float hx = x - cx + 0.5f, hy = y - heightPx * 7 / 8f;
                    bool head = hx * hx + hy * hy <= (widthPx / 3f) * (widthPx / 3f);

                    if (post) c = wood;
                    if (body || head) c = straw;
                    if (body)
                    {
                        float dx = x - cx + 0.5f, dy = y - heightPx / 2f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        if (d < 2f || (d > 4f && d < 6f)) c = ring;
                        if (x == 2 || x == widthPx - 3 || y == heightPx / 4 || y == heightPx * 3 / 4 - 1) c = outline;
                    }

                    texture.SetPixel(x, y, c);
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

        /// <summary>An arrow pointing along +x, pivoted at its centre: fletching, shaft and a coloured head.</summary>
        public static Sprite Arrow(Color headColor)
        {
            string key = $"arrow_{ColorUtility.ToHtmlStringRGBA(headColor)}";
            if (s_Cache.TryGetValue(key, out Sprite cached)) return cached;

            const int width = 16, height = 5;
            Texture2D texture = NewTexture(width, height);
            Color shaft = new Color32(150, 110, 70, 255);
            Color fletch = new Color32(235, 235, 235, 255);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color c = Color.clear;
                    if (y == 2 && x >= 2 && x < 12) c = shaft;
                    if (x < 3 && (y == 1 || y == 3)) c = fletch;
                    // Triangular head over the last four columns.
                    if (x >= 12 && Mathf.Abs(y - 2) <= 15 - x) c = headColor;
                    texture.SetPixel(x, y, c);
                }
            }

            return Finish(key, texture, new Vector2(0.5f, 0.5f));
        }

        /// <summary>A bow facing +x (limbs curving forward, string behind), pivoted at its grip.</summary>
        public static Sprite Bow(Color woodColor)
        {
            string key = $"bow_{ColorUtility.ToHtmlStringRGB(woodColor)}";
            if (s_Cache.TryGetValue(key, out Sprite cached)) return cached;

            const int width = 8, height = 22;
            Texture2D texture = NewTexture(width, height);
            Color stringColor = new Color32(230, 230, 230, 255);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    texture.SetPixel(x, y, Color.clear);
                }
            }

            for (int y = 0; y < height; y++)
            {
                float t = y / (height - 1f);
                int bulge = Mathf.RoundToInt(Mathf.Sin(t * Mathf.PI) * (width - 2));
                texture.SetPixel(bulge, y, woodColor);
                texture.SetPixel(Mathf.Min(bulge + 1, width - 1), y, woodColor);
                texture.SetPixel(0, y, stringColor);
            }

            return Finish(key, texture, new Vector2(0.5f, 0.5f));
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

            const int size = SimUnits.PixelsPerTile;
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
