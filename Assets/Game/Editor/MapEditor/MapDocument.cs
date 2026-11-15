using System;
using System.Collections.Generic;
using CTG.World;
using UnityEngine;

namespace CTG.EditorTools
{
    /// <summary>
    /// A map being edited: tiles, shapes and liquids per cell (x from the left, y = row from the top, as in the map
    /// file) plus the zones. A ScriptableObject so edits go through the editor's undo system.
    /// </summary>
    public sealed class MapDocument : ScriptableObject
    {
        public string MapName = "New Map";
        public int Width;
        public int Height;
        public int Border;
        [SerializeField] int[] m_Tiles;
        [SerializeField] byte[] m_Shapes;
        [SerializeField] byte[] m_Liquids;
        public List<MapZone> Zones = new();

        /// <summary>Glyphs used when saving. Tile types not listed get a free glyph.</summary>
        static readonly Dictionary<TileId, char> s_Glyphs = new()
        {
            { TileId.Air, '.' }, { TileId.BlueDirt, 'b' }, { TileId.RedDirt, 'r' }, { TileId.BlueBrick, 'B' },
            { TileId.RedBrick, 'R' }, { TileId.Ward, 'w' }, { TileId.Platform, 'p' }, { TileId.Stone, 's' },
        };

        public static MapDocument CreateEmpty(string name, int width, int height, int border)
        {
            var doc = CreateInstance<MapDocument>();
            doc.hideFlags = HideFlags.HideAndDontSave;
            doc.MapName = name;
            doc.Allocate(width, height, border);
            return doc;
        }

        void Allocate(int width, int height, int border)
        {
            Width = Mathf.Max(1, width);
            Height = Mathf.Max(1, height);
            Border = Mathf.Max(0, border);
            m_Tiles = new int[Width * Height];
            m_Shapes = new byte[Width * Height];
            m_Liquids = new byte[Width * Height];
            for (int i = 0; i < m_Tiles.Length; i++) m_Tiles[i] = (int)TileId.Air;
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
        int Index(int x, int y) => y * Width + x;

        public TileId GetTile(int x, int y) => InBounds(x, y) ? (TileId)m_Tiles[Index(x, y)] : TileId.Air;
        public TileShape GetShape(int x, int y) => InBounds(x, y) ? (TileShape)m_Shapes[Index(x, y)] : TileShape.Full;
        public LiquidType GetLiquid(int x, int y) => InBounds(x, y) ? (LiquidType)m_Liquids[Index(x, y)] : LiquidType.None;

        /// <summary>Sets a cell. A block clears any liquid (the game only keeps liquid in empty cells); air keeps no shape.</summary>
        public void Set(int x, int y, TileId tile, TileShape shape, LiquidType liquid)
        {
            if (!InBounds(x, y)) return;

            int i = Index(x, y);
            m_Tiles[i] = (int)tile;
            m_Shapes[i] = (byte)(tile == TileId.Air ? TileShape.Full : shape);
            m_Liquids[i] = (byte)(tile == TileId.Air ? liquid : LiquidType.None);
        }

        public void SetTile(int x, int y, TileId tile) => Set(x, y, tile, tile == TileId.Air ? TileShape.Full : GetShape(x, y), LiquidType.None);

        public void SetShape(int x, int y, TileShape shape)
        {
            if (InBounds(x, y) && GetTile(x, y) != TileId.Air) m_Shapes[Index(x, y)] = (byte)shape;
        }

        public void SetLiquid(int x, int y, LiquidType liquid) => Set(x, y, TileId.Air, TileShape.Full, liquid);

        public void Clear(int x, int y) => Set(x, y, TileId.Air, TileShape.Full, LiquidType.None);

        // ---- mirroring (the arena is a left/right mirror with team colours swapped) ---------------------------------

        public int MirrorX(int x) => Width - 1 - x;

        public static TileId SwapTeam(TileId id) => id switch
        {
            TileId.BlueDirt => TileId.RedDirt,
            TileId.RedDirt => TileId.BlueDirt,
            TileId.BlueBrick => TileId.RedBrick,
            TileId.RedBrick => TileId.BlueBrick,
            _ => id,
        };

        public static TileShape MirrorShape(TileShape shape) => shape switch
        {
            TileShape.SlopeDownLeft => TileShape.SlopeDownRight,
            TileShape.SlopeDownRight => TileShape.SlopeDownLeft,
            TileShape.SlopeUpLeft => TileShape.SlopeUpRight,
            TileShape.SlopeUpRight => TileShape.SlopeUpLeft,
            _ => shape,
        };

        /// <summary>The zone kind on the other team's side (SpawnBlue -> SpawnRed), or null for shared ones.</summary>
        public static string MirrorZoneKind(string kind)
        {
            if (kind.Contains("Blue")) return kind.Replace("Blue", "Red");
            if (kind.Contains("Red")) return kind.Replace("Red", "Blue");
            return null;
        }

        /// <summary>
        /// Makes the map symmetric: copies one half onto the other, mirrored left-right with team colours and slopes
        /// swapped. With an odd width the centre column is left alone. With <paramref name="zones"/>, team zones and
        /// no-build areas on the source half replace their counterparts on the other half (mid altar and boss spot stay).
        /// </summary>
        public void MirrorHalf(bool leftToRight, bool zones)
        {
            int half = Width / 2;
            for (int y = 0; y < Height; y++)
            {
                for (int i = 0; i < half; i++)
                {
                    int src = leftToRight ? i : MirrorX(i);
                    int dst = MirrorX(src);
                    Set(dst, y, SwapTeam(GetTile(src, y)), MirrorShape(GetShape(src, y)), GetLiquid(src, y));
                }
            }

            if (!zones) return;

            string source = leftToRight ? "Blue" : "Red";
            string target = leftToRight ? "Red" : "Blue";
            bool OnTargetSide(MapZone z) => leftToRight ? z.x >= Width - half : z.x + z.w <= half;
            bool OnSourceSide(MapZone z) => leftToRight ? z.x + z.w <= half : z.x >= Width - half;

            var sources = Zones.FindAll(z => z.kind.Contains(source) || (z.kind == "NoBuild" && OnSourceSide(z)));
            Zones.RemoveAll(z => z.kind.Contains(target) || (z.kind == "NoBuild" && OnTargetSide(z)));
            foreach (MapZone z in sources)
            {
                string kind = z.kind == "NoBuild" ? "NoBuild" : MirrorZoneKind(z.kind);
                Zones.Add(new MapZone { kind = kind, x = Width - z.x - z.w, y = z.y, w = z.w, h = z.h });
            }
        }

        // ---- regions --------------------------------------------------------------------------------------------

        public MapClip Copy(RectInt r)
        {
            var clip = new MapClip(r.width, r.height);
            for (int y = 0; y < r.height; y++)
            {
                for (int x = 0; x < r.width; x++)
                {
                    int sx = r.x + x, sy = r.y + y;
                    clip.Set(x, y, GetTile(sx, sy), GetShape(sx, sy), GetLiquid(sx, sy));
                }
            }

            return clip;
        }

        /// <param name="includeAir">Empty cells in the clip overwrite what is there; otherwise they are skipped.</param>
        public void Paste(MapClip clip, int left, int top, bool includeAir)
        {
            for (int y = 0; y < clip.Height; y++)
            {
                for (int x = 0; x < clip.Width; x++)
                {
                    clip.Get(x, y, out TileId tile, out TileShape shape, out LiquidType liquid);
                    if (!includeAir && tile == TileId.Air && liquid == LiquidType.None) continue;
                    Set(left + x, top + y, tile, shape, liquid);
                }
            }
        }

        /// <summary>Grows or shrinks the arena. Existing content moves right by <paramref name="shiftX"/> and down by <paramref name="shiftY"/>.</summary>
        public void Resize(int width, int height, int shiftX, int shiftY)
        {
            MapClip old = Copy(new RectInt(0, 0, Width, Height));
            Allocate(width, height, Border);
            Paste(old, shiftX, shiftY, true);
            foreach (MapZone z in Zones)
            {
                z.x += shiftX;
                z.y += shiftY;
            }
        }

        // ---- zones ----------------------------------------------------------------------------------------------

        /// <summary>Zone kinds a map can have, in the order the editor lists them. Only NoBuild may appear more than once.</summary>
        public static readonly string[] ZoneKinds =
        {
            "SpawnBlue", "SpawnRed", "MobSpawnBlue", "MobSpawnRed", "MobMidBlue", "MobMidRed", "AltarMid", "BossMid",
            "AltarBlue", "AltarRed", "BlueGem", "RedGem", "NoBuild",
        };

        public MapZone FindZone(string kind) => Zones.Find(z => z.kind == kind);

        /// <summary>Sets a zone; every kind but NoBuild replaces the existing one.</summary>
        public void SetZone(string kind, RectInt r)
        {
            if (kind != "NoBuild") Zones.RemoveAll(z => z.kind == kind);
            Zones.Add(new MapZone { kind = kind, x = r.x, y = r.y, w = r.width, h = r.height });
        }

        // ---- file conversion ------------------------------------------------------------------------------------

        public static MapDocument FromFile(MapFile file)
        {
            var doc = CreateEmpty(string.IsNullOrEmpty(file.name) ? "Map" : file.name, file.width, file.height, file.border);
            var glyphs = new Dictionary<char, TileId>();
            if (file.legend != null)
            {
                foreach (MapLegendEntry e in file.legend)
                {
                    if (string.IsNullOrEmpty(e.glyph)) continue;
                    if (!Enum.TryParse(e.tile, out TileId id)) throw new InvalidOperationException($"Unknown tile type '{e.tile}' in the legend.");
                    glyphs[e.glyph[0]] = id;
                }
            }

            for (int y = 0; y < doc.Height; y++)
            {
                string row = file.tiles != null && y < file.tiles.Length ? file.tiles[y] : "";
                string liquids = file.liquids != null && y < file.liquids.Length ? file.liquids[y] : "";
                for (int x = 0; x < doc.Width; x++)
                {
                    TileId tile = x < row.Length && glyphs.TryGetValue(row[x], out TileId t) ? t : TileId.Air;
                    LiquidType liquid = LiquidType.None;
                    if (x < liquids.Length)
                    {
                        if (liquids[x] == '~') liquid = LiquidType.Lava;
                        else if (liquids[x] == 'n') liquid = LiquidType.Nectar;
                    }

                    doc.Set(x, y, tile, TileShape.Full, liquid);
                }
            }

            if (file.shapes != null)
            {
                foreach (MapShapeEntry s in file.shapes)
                {
                    if (Enum.TryParse(s.shape, out TileShape shape)) doc.SetShape(s.x, s.y, shape);
                }
            }

            if (file.zones != null)
            {
                foreach (MapZone z in file.zones) doc.Zones.Add(new MapZone { kind = z.kind, x = z.x, y = z.y, w = z.w, h = z.h });
            }

            return doc;
        }

        public MapFile ToFile()
        {
            // Legend: only the tile types used, with stable glyphs.
            var used = new HashSet<TileId> { TileId.Air };
            foreach (int t in m_Tiles) used.Add((TileId)t);

            var glyphFor = new Dictionary<TileId, char>();
            var taken = new HashSet<char>();
            foreach (TileId id in used)
            {
                if (s_Glyphs.TryGetValue(id, out char g))
                {
                    glyphFor[id] = g;
                    taken.Add(g);
                }
            }

            const string spare = "acdefghijklmnoqtuvxyzACDEFGHIJKLMNOPQSTUVWXYZ0123456789";
            int next = 0;
            foreach (TileId id in used)
            {
                if (glyphFor.ContainsKey(id)) continue;
                while (taken.Contains(spare[next])) next++;
                glyphFor[id] = spare[next];
                taken.Add(spare[next]);
            }

            var legend = new List<MapLegendEntry>();
            foreach (KeyValuePair<TileId, char> e in glyphFor) legend.Add(new MapLegendEntry { glyph = e.Value.ToString(), tile = e.Key.ToString() });
            legend.Sort((a, b) => a.glyph == "." ? -1 : b.glyph == "." ? 1 : string.CompareOrdinal(a.glyph, b.glyph));

            var tiles = new string[Height];
            var liquids = new string[Height];
            var shapes = new List<MapShapeEntry>();
            bool anyLiquid = false;
            var tileRow = new char[Width];
            var liquidRow = new char[Width];
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    TileId tile = GetTile(x, y);
                    tileRow[x] = glyphFor[tile];
                    LiquidType liquid = GetLiquid(x, y);
                    liquidRow[x] = liquid == LiquidType.Lava ? '~' : liquid == LiquidType.Nectar ? 'n' : '.';
                    if (liquid != LiquidType.None) anyLiquid = true;
                    TileShape shape = GetShape(x, y);
                    if (tile != TileId.Air && shape != TileShape.Full) shapes.Add(new MapShapeEntry { x = x, y = y, shape = shape.ToString() });
                }

                tiles[y] = new string(tileRow);
                liquids[y] = new string(liquidRow);
            }

            return new MapFile
            {
                name = MapName, width = Width, height = Height, border = Border, legend = legend.ToArray(), tiles = tiles,
                liquids = anyLiquid ? liquids : Array.Empty<string>(), shapes = shapes.ToArray(), zones = Zones.ToArray(),
            };
        }
    }

    /// <summary>A copied rectangle of cells.</summary>
    public sealed class MapClip
    {
        public readonly int Width;
        public readonly int Height;
        readonly TileId[] m_Tiles;
        readonly TileShape[] m_Shapes;
        readonly LiquidType[] m_Liquids;

        public MapClip(int width, int height)
        {
            Width = width;
            Height = height;
            m_Tiles = new TileId[width * height];
            m_Shapes = new TileShape[width * height];
            m_Liquids = new LiquidType[width * height];
        }

        public void Set(int x, int y, TileId tile, TileShape shape, LiquidType liquid)
        {
            int i = y * Width + x;
            m_Tiles[i] = tile;
            m_Shapes[i] = shape;
            m_Liquids[i] = liquid;
        }

        public void Get(int x, int y, out TileId tile, out TileShape shape, out LiquidType liquid)
        {
            int i = y * Width + x;
            tile = m_Tiles[i];
            shape = m_Shapes[i];
            liquid = m_Liquids[i];
        }

        /// <summary>Left-right mirror with team colours swapped, for building the other team's side.</summary>
        public MapClip Flipped()
        {
            var clip = new MapClip(Width, Height);
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    Get(x, y, out TileId t, out TileShape s, out LiquidType l);
                    clip.Set(Width - 1 - x, y, MapDocument.SwapTeam(t), MapDocument.MirrorShape(s), l);
                }
            }

            return clip;
        }
    }
}
