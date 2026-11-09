using System;
using System.Collections.Generic;
using UnityEngine;

namespace CTG.World
{
    // ---- File format (JSON, under Assets/Resources/Maps) --------------------------------------------------
    // width / height : size of the arena in tiles.
    // border         : tiles of solid brick added around the arena (blue left half, red right half), so the
    //                  camera never sees past the edge. 0 = none.
    // legend         : glyph -> tile type name ("Air", "BlueDirt", "RedDirt", "BlueBrick", "RedBrick", "Ward").
    // tiles          : `height` strings of `width` glyphs, top row first.
    // liquids        : same layout, the liquids: '~' lava, 'n' nectar, '.' none. May be empty.
    // zones          : optional rectangles (x, y from the top row, w, h): SpawnBlue / SpawnRed rooms, BlueGem / RedGem hitboxes, NoBuild areas.
    // shapes         : sparse list of non-full tiles: x, y (y counted from the top row) and a TileShape name.

    [Serializable]
    public class MapLegendEntry
    {
        public string glyph;
        public string tile;
    }

    [Serializable]
    public class MapShapeEntry
    {
        public int x;
        public int y;
        public string shape;
    }

    /// <summary>A rectangle of the arena (x, y from the top-left, in tiles) with a purpose: "SpawnBlue", "SpawnRed", "BlueGem", "RedGem" or "NoBuild".</summary>
    [Serializable]
    public class MapZone
    {
        public string kind;
        public int x;
        public int y;
        public int w;
        public int h;
    }

    [Serializable]
    public class MapFile
    {
        public string name;
        public int width;
        public int height;
        public int border;
        public MapLegendEntry[] legend;
        public string[] tiles;
        public string[] liquids;
        public MapShapeEntry[] shapes;
        public MapZone[] zones;
    }

    /// <summary>A loaded map: the world plus where the arena sits inside it.</summary>
    public class LoadedMap
    {
        public string Name;
        public TileWorld World;
        /// <summary>Tiles of border around the arena.</summary>
        public int Border;
        public int ArenaWidth;
        public int ArenaHeight;
        /// <summary>Interior of each team's spawn room in world tiles (x, y = bottom-left), or null if the map has none.</summary>
        public RectInt? BlueSpawn;
        public RectInt? RedSpawn;
        /// <summary>Each team's gem hitbox (6×9 tiles) in world tiles (x, y = bottom-left), or null.</summary>
        public RectInt? BlueGem;
        public RectInt? RedGem;

        /// <summary>Tile x of the arena's centre column.</summary>
        public int CenterX => Border + ArenaWidth / 2;
    }

    public static class MapLoader
    {
        public const string ResourceFolder = "Maps";

        public static LoadedMap Load(string mapName)
        {
            var asset = Resources.Load<TextAsset>($"{ResourceFolder}/{mapName}");
            if (asset == null) throw new InvalidOperationException($"Map '{mapName}' not found in Resources/{ResourceFolder}.");

            return Build(JsonUtility.FromJson<MapFile>(asset.text));
        }

        public static LoadedMap Build(MapFile file)
        {
            if (file.tiles == null || file.tiles.Length != file.height)
                throw new InvalidOperationException($"Map '{file.name}': expected {file.height} tile rows.");

            var glyphs = new Dictionary<char, TileId>();
            foreach (MapLegendEntry entry in file.legend)
            {
                if (string.IsNullOrEmpty(entry.glyph)) continue;
                glyphs[entry.glyph[0]] = ParseTile(entry.tile);
            }

            int border = Mathf.Max(0, file.border);
            var world = new TileWorld(file.width + border * 2, file.height + border * 2);

            // Brick border: blue on the left half of the world, red on the right.
            if (border > 0)
            {
                for (int y = 0; y < world.Height; y++)
                {
                    for (int x = 0; x < world.Width; x++)
                    {
                        world.SetRaw(x, y, x < world.Width / 2 ? TileId.BlueBrick : TileId.RedBrick);
                    }
                }
            }

            // Rows are listed top first; the world is y-up.
            for (int row = 0; row < file.height; row++)
            {
                string tiles = file.tiles[row];
                if (tiles.Length != file.width)
                    throw new InvalidOperationException($"Map '{file.name}': tile row {row} has {tiles.Length} glyphs, expected {file.width}.");

                string liquids = file.liquids != null && row < file.liquids.Length ? file.liquids[row] : null;
                int y = border + (file.height - 1 - row);

                for (int col = 0; col < file.width; col++)
                {
                    char glyph = tiles[col];
                    if (!glyphs.TryGetValue(glyph, out TileId id))
                        throw new InvalidOperationException($"Map '{file.name}': glyph '{glyph}' is not in the legend.");

                    int x = border + col;
                    world.SetRaw(x, y, id);
                    if (id == TileId.Air && liquids != null && col < liquids.Length)
                    {
                        if (liquids[col] == '~') world.SetLiquid(x, y, LiquidType.Lava);
                        else if (liquids[col] == 'n') world.SetLiquid(x, y, LiquidType.Nectar);
                    }
                }
            }

            if (file.shapes != null)
            {
                foreach (MapShapeEntry entry in file.shapes)
                {
                    int x = border + entry.x;
                    int y = border + (file.height - 1 - entry.y);
                    if (!Enum.TryParse(entry.shape, out TileShape shape))
                        throw new InvalidOperationException($"Map '{file.name}': unknown shape '{entry.shape}'.");

                    world.SetTile(x, y, world.Get(x, y), shape);
                }
            }

            var map = new LoadedMap
            {
                Name = file.name, World = world, Border = border, ArenaWidth = file.width, ArenaHeight = file.height,
            };

            if (file.zones != null)
            {
                foreach (MapZone zone in file.zones)
                {
                    // Convert from arena top-left tiles to world bottom-left tiles.
                    var rect = new RectInt(border + zone.x, border + (file.height - zone.y - zone.h), zone.w, zone.h);
                    switch (zone.kind)
                    {
                        case "SpawnBlue": map.BlueSpawn = rect; break;
                        case "SpawnRed": map.RedSpawn = rect; break;
                        case "BlueGem": map.BlueGem = rect; break;
                        case "RedGem": map.RedGem = rect; break;
                        case "NoBuild": world.SetNoBuild(rect.x, rect.y, rect.width, rect.height); break;
                        default: throw new InvalidOperationException($"Map '{file.name}': unknown zone '{zone.kind}'.");
                    }
                }
            }

            return map;
        }

        static TileId ParseTile(string name)
        {
            if (!Enum.TryParse(name, out TileId id)) throw new InvalidOperationException($"Unknown tile type '{name}'.");
            return id;
        }

        /// <summary>
        /// Finds a place to stand near a column: scanning down from the top of the arena for the first solid
        /// tile with three clear, liquid-free tiles above it (checked a column either side too, so the player
        /// isn't wedged). Searches outward from <paramref name="tileX"/> if that column has none.
        /// Returns the tile row to stand on, or -1.
        /// </summary>
        public static int FindStandRow(LoadedMap map, int tileX, out int foundX)
        {
            TileWorld world = map.World;
            int top = map.Border + map.ArenaHeight - 1;
            for (int offset = 0; offset < world.Width; offset++)
            {
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    int x = tileX + offset * sign;
                    if (offset == 0 && sign == 1) continue;

                    for (int y = top; y >= 0; y--)
                    {
                        if (CanStandOn(world, x, y))
                        {
                            foundX = x;
                            return y;
                        }
                    }
                }
            }

            foundX = tileX;
            return -1;
        }

        static bool CanStandOn(TileWorld world, int x, int y)
        {
            if (!world.InBounds(x, y) || !world.IsSolid(x, y)) return false;

            for (int dy = 1; dy <= 3; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (world.IsSolid(x + dx, y + dy) || world.GetLiquid(x + dx, y + dy) != LiquidType.None) return false;
                }
            }

            return true;
        }
    }
}
