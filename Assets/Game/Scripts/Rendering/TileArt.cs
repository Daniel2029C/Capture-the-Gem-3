using System.Collections.Generic;
using CTG.Core;
using CTG.World;
using UnityEngine;

namespace CTG.Rendering
{
    /// <summary>
    /// Placeholder tile art made in code. Terrain is dark and desaturated so players and projectiles are the
    /// brightest things on screen. Earth (dirt) is a muted team-tinted fill with a pale rim light on exposed
    /// sides; stone (the team bricks) is near-black with a glowing team-colour rim, so team colour reads as
    /// light rather than as a fill. Tiles that border their own kind blend into one surface: only exposed
    /// sides get a rim, exposed convex corners are rounded and exposed edges wobble by a pixel.
    /// </summary>
    public static class TileArt
    {
        /// <summary>Bit set in a tile's neighbour mask when the neighbour on that side is the same kind of tile.</summary>
        public const int SameLeft = 1, SameRight = 2, SameBelow = 4, SameAbove = 8;
        /// <summary>Same, for the four diagonal neighbours.</summary>
        public const int SameBelowLeft = 16, SameBelowRight = 32, SameAboveLeft = 64, SameAboveRight = 128;

        const int k_Size = SimUnits.PixelsPerTile;
        const int k_CornerRadius = 3;

        static readonly Dictionary<string, Sprite> s_Cache = new();

        static bool IsStone(TileId id) => id == TileId.BlueBrick || id == TileId.RedBrick;

        /// <summary>A thin pale-glass strip along the top of the tile; the ends are capped where the platform stops.</summary>
        public static Sprite Platform(int mask)
        {
            mask &= SameLeft | SameRight;
            string key = $"art_Platform_{mask}";
            if (s_Cache.TryGetValue(key, out Sprite cached)) return cached;

            var texture = new Texture2D(k_Size, k_Size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color[k_Size * k_Size];
            for (int x = 0; x < k_Size; x++)
            {
                bool capLeft = (mask & SameLeft) == 0 && x == 0;
                bool capRight = (mask & SameRight) == 0 && x == k_Size - 1;
                int top = k_Size - 1;
                pixels[top * k_Size + x] = capLeft || capRight ? new Color(0.9f, 0.97f, 1f, 1f) : new Color(0.75f, 0.92f, 1f, 0.95f);
                pixels[(top - 1) * k_Size + x] = new Color(0.45f, 0.72f, 0.9f, 0.7f);
                pixels[(top - 2) * k_Size + x] = new Color(0.3f, 0.55f, 0.8f, 0.3f);
            }

            texture.SetPixels(pixels);
            texture.Apply();
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, k_Size, k_Size), Vector2.zero, SimUnits.PixelsPerTile);
            sprite.name = key;
            s_Cache[key] = sprite;
            return sprite;
        }

        public static Sprite Tile(TileId id, Color accent, TileShape shape, int mask, int variant)
        {
            string key = $"art_{id}_{shape}_{mask}_{variant}";
            if (s_Cache.TryGetValue(key, out Sprite cached)) return cached;

            bool stone = IsStone(id);
            var covered = new bool[k_Size, k_Size];
            for (int y = 0; y < k_Size; y++)
            {
                for (int x = 0; x < k_Size; x++)
                {
                    covered[x, y] = TileShapes.Covers(shape, x, y);
                }
            }

            ShapeEdges(covered, mask, variant, (int)id);

            // Palette.
            Color.RGBToHSV(accent, out float hue, out float sat, out float val);
            Color earth = Color.HSVToRGB(hue, sat * 0.3f, val * 0.45f);
            Color rimLight = Color.HSVToRGB(hue, sat * 0.35f, Mathf.Min(1f, val * 1.05f));
            Color slate = new Color(0.12f, 0.14f, 0.19f);
            Color body = stone ? slate : earth;

            var texture = new Texture2D(k_Size, k_Size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var random = new System.Random(unchecked(variant * 7919 + (int)id * 104729 + (int)shape * 31 + 17));
            float noiseOffset = variant * 13.7f + (int)id * 3.1f;

            for (int y = 0; y < k_Size; y++)
            {
                for (int x = 0; x < k_Size; x++)
                {
                    if (!covered[x, y])
                    {
                        texture.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    // Body: slow blotches, a little grain, and the odd dark pebble.
                    float blotch = Mathf.PerlinNoise(x * 0.22f + noiseOffset, y * 0.22f + noiseOffset) - 0.5f;
                    float grain = (float)random.NextDouble() - 0.5f;
                    float shade = 1f + blotch * 0.35f + grain * 0.12f;
                    if (random.NextDouble() < 0.035) shade *= 0.72f;
                    Color c = body * shade;

                    // Exposure to open space, measured in pixels from the nearest uncovered pixel on each side.
                    int up = Distance(covered, mask, x, y, 0, 1);
                    int down = Distance(covered, mask, x, y, 0, -1);
                    int left = Distance(covered, mask, x, y, -1, 0);
                    int right = Distance(covered, mask, x, y, 1, 0);

                    if (stone)
                    {
                        // Glowing team-colour rim, fading over three pixels, strongest on exposed corners.
                        int nearest = Mathf.Min(Mathf.Min(up, down), Mathf.Min(left, right));
                        float glow = nearest == 1 ? 1f : nearest == 2 ? 0.45f : nearest == 3 ? 0.16f : 0f;
                        c += accent * glow;
                    }
                    else
                    {
                        float light = 0f;
                        if (up == 1) light = 0.7f; else if (up == 2) light = 0.28f;
                        if (left == 1 || right == 1) light = Mathf.Max(light, 0.3f);
                        if (down == 1) light = -0.4f; else if (down == 2 && light == 0f) light = -0.15f;
                        c = light >= 0f ? Color.Lerp(c, rimLight, light) : c * (1f + light);
                    }

                    c.a = 1f;
                    texture.SetPixel(x, y, c);
                }
            }

            texture.Apply();
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, k_Size, k_Size), Vector2.zero, SimUnits.PixelsPerTile);
            sprite.name = key;
            s_Cache[key] = sprite;
            return sprite;
        }

        /// <summary>Wobbles exposed edges by up to two pixels and rounds exposed convex corners.</summary>
        static void ShapeEdges(bool[,] covered, int mask, int variant, int salt)
        {
            bool openLeft = (mask & SameLeft) == 0, openRight = (mask & SameRight) == 0;
            bool openBelow = (mask & SameBelow) == 0, openAbove = (mask & SameAbove) == 0;

            for (int i = 0; i < k_Size; i++)
            {
                if (openLeft) Erode(covered, i, true, false, Wobble(variant, salt, i, 1));
                if (openRight) Erode(covered, i, true, true, Wobble(variant, salt, i, 2));
                if (openBelow) Erode(covered, i, false, false, Wobble(variant, salt, i, 3));
                if (openAbove) Erode(covered, i, false, true, Wobble(variant, salt, i, 4));
            }

            Round(covered, openLeft && openBelow, 0, 0, 1, 1);
            Round(covered, openRight && openBelow, k_Size - 1, 0, -1, 1);
            Round(covered, openLeft && openAbove, 0, k_Size - 1, 1, -1);
            Round(covered, openRight && openAbove, k_Size - 1, k_Size - 1, -1, -1);
        }

        static int Wobble(int variant, int salt, int along, int side)
        {
            float n = Mathf.PerlinNoise(along * 0.4f + variant * 5.3f + salt * 0.7f, side * 11.1f + salt * 0.13f);
            return n > 0.74f ? 2 : n > 0.4f ? 1 : 0;
        }

        /// <summary>Clears <paramref name="depth"/> pixels from one edge at one position along it.</summary>
        static void Erode(bool[,] covered, int along, bool vertical, bool farSide, int depth)
        {
            for (int d = 0; d < depth; d++)
            {
                int across = farSide ? k_Size - 1 - d : d;
                if (vertical) covered[across, along] = false;   // a left/right edge, running along y
                else covered[along, across] = false;            // a bottom/top edge, running along x
            }
        }

        static void Round(bool[,] covered, bool enabled, int cornerX, int cornerY, int dirX, int dirY)
        {
            if (!enabled) return;

            float r = k_CornerRadius;
            for (int a = 0; a < k_CornerRadius; a++)
            {
                for (int b = 0; b < k_CornerRadius; b++)
                {
                    float px = a + 0.5f, py = b + 0.5f;
                    if ((px - r) * (px - r) + (py - r) * (py - r) > r * r)
                    {
                        covered[cornerX + dirX * a, cornerY + dirY * b] = false;
                    }
                }
            }
        }

        /// <summary>Steps from a pixel in a direction until an uncovered pixel; 4 if none within three steps.</summary>
        static int Distance(bool[,] covered, int mask, int x, int y, int dx, int dy)
        {
            for (int k = 1; k <= 3; k++)
            {
                if (!IsCovered(covered, mask, x + dx * k, y + dy * k)) return k;
            }

            return 4;
        }

        /// <summary>Is this pixel solid? Pixels outside the tile count as solid when that neighbour is the same kind.</summary>
        static bool IsCovered(bool[,] covered, int mask, int x, int y)
        {
            bool inX = x >= 0 && x < k_Size, inY = y >= 0 && y < k_Size;
            if (inX && inY) return covered[x, y];

            if (!inX && !inY)
            {
                int bit = x < 0 ? (y < 0 ? SameBelowLeft : SameAboveLeft) : (y < 0 ? SameBelowRight : SameAboveRight);
                return (mask & bit) != 0;
            }

            if (!inX) return (mask & (x < 0 ? SameLeft : SameRight)) != 0;
            return (mask & (y < 0 ? SameBelow : SameAbove)) != 0;
        }
    }
}
