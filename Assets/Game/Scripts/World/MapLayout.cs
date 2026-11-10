using CTG.Combat;
using UnityEngine;

namespace CTG.World
{
    /// <summary>
    /// Where the objective pieces sit on a map: each side's mob spawn area and base altar, and the mid altar. A map
    /// can state them with zones; otherwise they are derived from the gem positions and the world so every map has
    /// a sensible default until it is adapted.
    /// </summary>
    public static class MapLayout
    {
        /// <summary>How far from the gem hitbox, in tiles, the base altar stands (toward the team's own base).</summary>
        const int k_AltarOffsetTiles = 8;
        /// <summary>The default mob area: this many tiles beyond the gem's outer edge, this wide.</summary>
        const int k_MobAreaStart = 22, k_MobAreaWidth = 30;

        /// <summary>Area (world tiles) in which a side's mob spawns: the open ground between its gem area and mid.</summary>
        public static RectInt MobSpawnArea(LoadedMap map, Team side)
        {
            RectInt? zone = side == Team.Blue ? map.BlueMobSpawn : map.RedMobSpawn;
            if (zone.HasValue) return zone.Value;

            // Blue: to the right of its gem, over the open cave rows. Red mirrors it across the world centre.
            int bottom = map.Border + 8;
            int height = map.ArenaHeight - 16;
            RectInt? gem = map.BlueGem;
            int blueX = gem.HasValue ? gem.Value.xMax + k_MobAreaStart : map.Border + k_MobAreaStart;
            var blue = new RectInt(blueX, bottom, k_MobAreaWidth, height);
            if (side == Team.Blue) return blue;

            int mirroredX = map.World.Width - blue.xMax;
            return new RectInt(mirroredX, bottom, blue.width, blue.height);
        }

        /// <summary>Tiles either side of the centre column the mid mobs keep clear of, and how wide each side's area is.</summary>
        const int k_MidGap = 2, k_MidAreaWidth = 26;
        /// <summary>Rows above and below the mid altar that the default mid area covers.</summary>
        const int k_MidRowsBelow = 20, k_MidRowsAbove = 25;

        /// <summary>Area (world tiles) within mid where a side's mid Drakon spawns: that side's half of the centre.</summary>
        public static RectInt MidMobSpawnArea(LoadedMap map, Team side)
        {
            RectInt? zone = side == Team.Blue ? map.BlueMidMobSpawn : map.RedMidMobSpawn;
            if (zone.HasValue) return zone.Value;

            int altarY = MidAltarTile(map).y;
            int bottom = Mathf.Max(map.Border + 4, altarY - k_MidRowsBelow);
            int top = Mathf.Min(map.Border + map.ArenaHeight - 4, altarY + k_MidRowsAbove);
            int x = side == Team.Blue ? map.CenterX - k_MidGap - k_MidAreaWidth : map.CenterX + k_MidGap;
            return new RectInt(x, bottom, k_MidAreaWidth, top - bottom);
        }

        /// <summary>The altar's standing spot (world tile x, and the row of the floor tile it stands on).</summary>
        public static Vector2Int BaseAltarTile(LoadedMap map, Team team)
        {
            RectInt? zone = team == Team.Blue ? map.BlueAltar : map.RedAltar;
            if (zone.HasValue) return new Vector2Int(zone.Value.x + zone.Value.width / 2, zone.Value.y - 1);

            RectInt? gem = team == Team.Blue ? map.BlueGem : map.RedGem;
            if (!gem.HasValue) return CenterFallback(map, team == Team.Blue ? map.Border + 12 : map.World.Width - map.Border - 12);

            // Beside the pedestal on its base side, on the pedestal's floor.
            int x = team == Team.Blue ? gem.Value.x - k_AltarOffsetTiles : gem.Value.xMax + k_AltarOffsetTiles - 1;
            return new Vector2Int(x, FloorBelow(map.World, x, gem.Value.y));
        }

        /// <summary>The mid altar: under the first honey (nectar) tile, else on the ground at the map centre.</summary>
        public static Vector2Int MidAltarTile(LoadedMap map)
        {
            if (map.MidAltar.HasValue) return new Vector2Int(map.MidAltar.Value.x + map.MidAltar.Value.width / 2, map.MidAltar.Value.y - 1);

            TileWorld world = map.World;
            for (int y = world.Height - 1; y >= 0; y--)
            {
                for (int x = 0; x < world.Width; x++)
                {
                    if (world.GetLiquid(x, y) != LiquidType.Nectar) continue;

                    // Straight down from the honey to the first solid tile.
                    return new Vector2Int(x, FloorBelow(world, x, y));
                }
            }

            return CenterFallback(map, map.CenterX);
        }

        /// <summary>
        /// The mid-boss's floor tile: the platform below the mid altar (the first solid ground straight down from it,
        /// which on a map with a centre spire is the platform under the spire).
        /// </summary>
        public static Vector2Int BossFloorTile(LoadedMap map)
        {
            if (map.BossSpot.HasValue) return new Vector2Int(map.BossSpot.Value.x + map.BossSpot.Value.width / 2, map.BossSpot.Value.y - 1);

            Vector2Int altar = MidAltarTile(map);
            return new Vector2Int(altar.x, FloorBelow(map.World, altar.x, altar.y - 1));
        }

        static Vector2Int CenterFallback(LoadedMap map, int x)
        {
            int row = MapLoader.FindStandRow(map, x, out int foundX);
            return new Vector2Int(foundX, row);
        }

        /// <summary>The first solid tile at or below (x, startY).</summary>
        static int FloorBelow(TileWorld world, int x, int startY)
        {
            for (int y = Mathf.Min(startY, world.Height - 1); y >= 0; y--)
            {
                if (world.IsSolid(x, y)) return y;
            }

            return 0;
        }
    }
}
