using System.Collections.Generic;
using CTG.Core;
using CTG.World;
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

        /// <summary>Bit set in a tile's neighbour mask when the neighbour on that side is the same kind of tile.</summary>
        public const int SameLeft = 1, SameRight = 2, SameBelow = 4, SameAbove = 8;
        /// <summary>Same, for the four diagonal neighbours (used for the inside corners).</summary>
        public const int SameBelowLeft = 16, SameBelowRight = 32, SameAboveLeft = 64, SameAboveRight = 128;

        /// <summary>
        /// A 16×16 tile with a faint speckle. Only sides that border a different kind of tile get a darker
        /// edge, and inside corners get a dot, so a patch of the same tile reads as one surface instead of a
        /// grid. <paramref name="sameMask"/> says which neighbours are the same kind; <paramref name="variant"/>
        /// picks one of a few speckle patterns so the texture doesn't visibly repeat.
        /// </summary>
        public static Sprite Tile(string key, Color baseColor, TileShape shape, int sameMask, int variant)
        {
            key = $"{key}_{shape}_{sameMask}_{variant}";
            if (s_Cache.TryGetValue(key, out Sprite cached)) return cached;

            const int size = SimUnits.PixelsPerTile;
            Texture2D texture = NewTexture(size, size);
            var random = new System.Random(unchecked(variant * 7919 + (int)shape * 104729 + 13));

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool covered = TileShapes.Covers(shape, x, y);
                    float shade = 0.94f + (float)random.NextDouble() * 0.1f;
                    if (covered && IsEdge(shape, sameMask, x, y)) shade *= 0.78f;
                    Color c = baseColor * shade;
                    c.a = covered ? 1f : 0f;
                    texture.SetPixel(x, y, c);
                }
            }

            return Finish(key, texture, Vector2.zero);
        }

        /// <summary>Is this pixel on a border with empty space or a different tile (drawn darker)?</summary>
        static bool IsEdge(TileShape shape, int mask, int x, int y)
        {
            const int last = SimUnits.PixelsPerTile - 1;

            // Borders with neighbouring tiles of a different kind.
            if (x == 0 && (mask & SameLeft) == 0) return true;
            if (x == last && (mask & SameRight) == 0) return true;
            if (y == 0 && (mask & SameBelow) == 0) return true;
            if (y == last && (mask & SameAbove) == 0) return true;

            // The tile's own cut edges (slopes and half blocks).
            if (shape != TileShape.Full)
            {
                if (x > 0 && !TileShapes.Covers(shape, x - 1, y)) return true;
                if (x < last && !TileShapes.Covers(shape, x + 1, y)) return true;
                if (y > 0 && !TileShapes.Covers(shape, x, y - 1)) return true;
                if (y < last && !TileShapes.Covers(shape, x, y + 1)) return true;
            }

            // Inside corners: both neighbours on the sides are the same, the diagonal one is not.
            if (x == 0 && y == 0 && (mask & SameLeft) != 0 && (mask & SameBelow) != 0 && (mask & SameBelowLeft) == 0) return true;
            if (x == last && y == 0 && (mask & SameRight) != 0 && (mask & SameBelow) != 0 && (mask & SameBelowRight) == 0) return true;
            if (x == 0 && y == last && (mask & SameLeft) != 0 && (mask & SameAbove) != 0 && (mask & SameAboveLeft) == 0) return true;
            if (x == last && y == last && (mask & SameRight) != 0 && (mask & SameAbove) != 0 && (mask & SameAboveRight) == 0) return true;

            return false;
        }

        /// <summary>Player body: a coloured rectangle with a head and an eye showing the facing direction.</summary>
        /// <param name="teamColor">Colour of the outer outline, which shows which team the player is on.</param>
        public static Sprite Player(int widthPx, int heightPx, Color teamColor)
        {
            string key = $"player_{widthPx}x{heightPx}_{ColorUtility.ToHtmlStringRGB(teamColor)}";
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
                    bool outer = x == 0 || y == 0 || x == widthPx - 1 || y == heightPx - 1;
                    bool inner = x == 1 || y == 1 || x == widthPx - 2 || y == heightPx - 2;
                    Color c = y >= heightPx - headHeight ? skin : y < legHeight ? legs : body;
                    texture.SetPixel(x, y, outer ? teamColor : inner ? outline : c);
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

        /// <summary>A full liquid tile; the surface one has a bright top edge for tiles open to the air.</summary>
        public static Sprite Liquid(LiquidType liquid, bool surface)
        {
            string key = $"liquid_{liquid}_{(surface ? "surface" : "body")}";
            if (s_Cache.TryGetValue(key, out Sprite cached)) return cached;

            bool nectar = liquid == LiquidType.Nectar;
            Color body = nectar ? new Color(0.95f, 0.65f, 0.1f) : new Color(0.95f, 0.32f, 0.05f);
            Color edge = nectar ? new Color(1f, 0.9f, 0.45f) : new Color(1f, 0.78f, 0.2f);
            float alpha = nectar ? 0.7f : 0.8f;

            const int size = SimUnits.PixelsPerTile;
            Texture2D texture = NewTexture(size, size);
            var random = new System.Random((surface ? 11 : 7) + (nectar ? 100 : 0));
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float shade = 0.85f + (float)random.NextDouble() * 0.3f;
                    Color c = new Color(body.r * shade, body.g * shade, body.b * shade, alpha);
                    if (surface && y >= size - 3)
                    {
                        float k = y == size - 1 ? 1f : 0.7f;
                        c = new Color(edge.r, edge.g * k, edge.b, 0.95f);
                    }

                    texture.SetPixel(x, y, c);
                }
            }

            return Finish(key, texture, Vector2.zero);
        }

        /// <summary>A see-through orb tile: faint fill with a brighter ring.</summary>
        public static Sprite Ward()
        {
            const string key = "tile_ward";
            if (s_Cache.TryGetValue(key, out Sprite cached)) return cached;

            const int size = SimUnits.PixelsPerTile;
            Texture2D texture = NewTexture(size, size);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - 7.5f, dy = y - 7.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    Color c = new Color(0.45f, 0.85f, 1f, 0.22f);
                    if (d > 6.2f && d <= 7.6f) c = new Color(0.8f, 0.97f, 1f, 0.85f);
                    else if (d > 7.6f) c = new Color(0.45f, 0.85f, 1f, 0.1f);
                    if (x >= 3 && x <= 5 && y >= 9 && y <= 11 && d < 6.2f) c = new Color(1f, 1f, 1f, 0.6f);
                    texture.SetPixel(x, y, c);
                }
            }

            return Finish(key, texture, Vector2.zero);
        }

        /// <summary>A 16×16 potion bottle: cork, neck and a round red body with a highlight.</summary>
        public static Sprite Potion()
        {
            const string key = "potion_icon";
            if (s_Cache.TryGetValue(key, out Sprite cached)) return cached;

            const int size = 16;
            Texture2D texture = NewTexture(size, size);
            Color outline = new Color32(30, 20, 30, 255);
            Color glass = new Color32(200, 40, 55, 255);
            Color shine = new Color32(255, 150, 160, 255);
            Color cork = new Color32(170, 120, 70, 255);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Color c = Color.clear;
                    float dx = x - 7.5f, dy = y - 5.5f;
                    bool body = dx * dx + dy * dy <= 28f;
                    bool neck = x >= 6 && x <= 9 && y >= 9 && y <= 12;
                    bool stopper = x >= 5 && x <= 10 && y >= 13 && y <= 14;
                    if (body || neck) c = glass;
                    if (stopper) c = cork;
                    if (body && x <= 6 && y >= 6 && y <= 9 && x >= 4) c = shine;
                    texture.SetPixel(x, y, c);
                }
            }

            // Outline: any empty pixel touching a filled one.
            Color[] pixels = texture.GetPixels();
            Color[] result = (Color[])pixels.Clone();
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (pixels[y * size + x].a > 0f) continue;
                    bool near = (x > 0 && pixels[y * size + x - 1].a > 0f) || (x < size - 1 && pixels[y * size + x + 1].a > 0f) ||
                                (y > 0 && pixels[(y - 1) * size + x].a > 0f) || (y < size - 1 && pixels[(y + 1) * size + x].a > 0f);
                    if (near) result[y * size + x] = outline;
                }
            }

            texture.SetPixels(result);
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

        /// <summary>A solid rectangle centred on its pivot (hooks and chain links).</summary>
        public static Sprite Solid(string key, Color color, int widthPx, int heightPx)
        {
            if (s_Cache.TryGetValue(key, out Sprite cached)) return cached;

            Texture2D texture = NewTexture(widthPx, heightPx);
            for (int y = 0; y < heightPx; y++)
            {
                for (int x = 0; x < widthPx; x++)
                {
                    texture.SetPixel(x, y, color);
                }
            }

            return Finish(key, texture, new Vector2(0.5f, 0.5f));
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
