using CTG.Classes;
using CTG.Combat;
using CTG.Core;
using CTG.Economy;
using CTG.Gameplay;
using CTG.Items;
using CTG.Mobs;
using CTG.Rendering;
using CTG.UI;
using CTG.World;
using UnityEngine;
using UnityEngine.Tilemaps;
using IdleInput = CTG.Input.IdleInput;
using IInputSource = CTG.Input.IInputSource;
using PlayerInput = CTG.Input.PlayerInput;

namespace CTG
{
    /// <summary>
    /// Builds the single-player sandbox at runtime: the chosen map, the local player with the class
    /// chosen at start, the camera, cursor and HUD. The scene only needs this component and a camera.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        [Header("Map")]
        [Tooltip("Name of a map file in Assets/Resources/Maps: Stalactite or Test.")]
        [SerializeField] string mapName = "stalactite";

        [Header("Loadout")]
        [Tooltip("Dirt every class starts with, in slot 2.")]
        [SerializeField] int startingDirt = 300;
        [Tooltip("Testing: Ward Blocks every class starts with, in slot 4. 0 = none.")]
        [SerializeField] int startingWardBlocks;

        [Header("Testing")]
        [Tooltip("Tiles right of the player spawn where the target dummy stands.")]
        [SerializeField] int dummyOffsetTiles = 6;
        [Tooltip("Testing: uncontrolled players added to each team (the local player is extra, on blue). 0 = none.")]
        [SerializeField] int botsPerTeam;

        [SerializeField] Camera worldCamera;

        public TileWorld World { get; private set; }
        public LoadedMap Map { get; private set; }
        public PlayerCharacter Player { get; private set; }
        public TargetDummy Dummy { get; private set; }

        MatchContext m_Context;
        SimulationClock m_Clock;
        TileEditor m_Editor;
        PlayerInput m_Input;
        GemMatch m_GemMatch;
        MatchFlow m_MatchFlow;
        Vector2 m_PlayerSpawn;
        int m_PlayerSpawnColumn;

        /// <summary>
        /// Builds the match in order. The order matters: things tick in the order they register with the clock, so
        /// the world editor ticks first, then every player, then the objective (so it sees this tick's player
        /// positions), then the match flow, then projectiles (so a shot fired this tick also moves this tick).
        /// Views and UI do not tick and can go anywhere after what they show exists.
        /// </summary>
        void Awake()
        {
            Application.targetFrameRate = -1;

            BeginMatch();
            BuildWorld();
            SpawnPlayers();
            BuildObjective();
            BuildPlayerUi();
            BuildMatchFlow();
            BuildProjectiles();
            BuildCameraAndCursor();
            BuildTestingTools();
            BeginClassSelect();
        }

        void OnDestroy() => m_Context?.End();

        /// <summary>Creates the match's services and its clock.</summary>
        void BeginMatch()
        {
            m_Context = MatchContext.Begin();
            m_Clock = new GameObject("Simulation Clock").AddComponent<SimulationClock>();
            m_Clock.transform.SetParent(transform, false);
            m_Context.Clock = m_Clock;
        }

        /// <summary>The map, its tiles and their renderer, and the tile editor with its dirt particles.</summary>
        void BuildWorld()
        {
            Map = MapLoader.Load(mapName);
            World = Map.World;

            m_Editor = new TileEditor(World);
            m_Clock.Register(new TileEditorTicker(m_Editor));

            // Dirt chips fly when dirt blocks are mined away (and, in BuildProjectiles, when projectiles strike dirt).
            ParticleView.Create(transform);
            m_Editor.TileBroken += (tile, id) =>
                ParticleView.DirtBurst((new Vector2(tile.x, tile.y) + new Vector2(0.5f, 0.5f)) * SimUnits.PixelsPerTile, id, 10, Vector2.up, 120f);

            CreateWorldRenderer();
        }

        /// <summary>The local player (blue) and, for testing, idle players filling out each team.</summary>
        void SpawnPlayers()
        {
            if (worldCamera == null) worldCamera = Camera.main;

            var inputGo = new GameObject("Player Input");
            inputGo.transform.SetParent(transform, false);
            m_Input = inputGo.AddComponent<PlayerInput>();
            m_Input.SetCamera(worldCamera);

            Player = SpawnPlayer(Team.Blue, m_Input, m_Editor, true, "You", 0);
            for (int i = 0; i < botsPerTeam; i++)
            {
                SpawnPlayer(Team.Blue, new IdleInput(), m_Editor, false, $"Blue bot {i + 1}", i + 1);
                SpawnPlayer(Team.Red, new IdleInput(), m_Editor, false, $"Red bot {i + 1}", i);
            }

            m_PlayerSpawn = Player.Motor.Position;
            m_PlayerSpawnColumn = Mathf.RoundToInt((m_PlayerSpawn.x + PlayerCharacter.HitboxWidthPx * 0.5f) / SimUnits.PixelsPerTile - 0.5f);
        }

        /// <summary>The gem objective with its views, and the altars and mob spawns placed from the map layout.</summary>
        void BuildObjective()
        {
            m_GemMatch = GemMatch.Create(transform, Map);
            if (m_GemMatch == null) return;

            GemView.Create(transform, m_GemMatch);
            MatchHud.Create(transform, m_GemMatch);
            BarrierView.Create(transform, m_GemMatch);
            CreateObjectivePieces(m_GemMatch);
        }

        void BuildPlayerUi()
        {
            ShopUi.Create(transform, Player);
            KeybindMenuUi.Create(transform);
            ScoreboardUi.Create(transform);
        }

        /// <summary>The stages of the match (class select, countdown, playing, results) and the testing shortcuts.</summary>
        void BuildMatchFlow()
        {
            m_MatchFlow = MatchFlow.Create(transform, m_Clock, m_GemMatch);
            if (m_GemMatch != null)
            {
                Player.DevEndMatchRequested += m_GemMatch.DevEnd;
                Player.DevSkipOvertimeRequested += m_GemMatch.DevSkipToOvertime;
            }

            MatchFlowHud.Create(transform, m_MatchFlow);
        }

        void BuildProjectiles()
        {
            var projectiles = new GameObject("Projectiles").AddComponent<ProjectileSystem>();
            projectiles.transform.SetParent(transform, false);
            m_Context.Projectiles = projectiles;
            projectiles.Initialize(World);
            projectiles.TileStruck += (point, id) => ParticleView.DirtBurst(point, id, 6, Vector2.up, 100f);
        }

        void BuildCameraAndCursor()
        {
            worldCamera.backgroundColor = new Color32(14, 18, 28, 255);
            worldCamera.clearFlags = CameraClearFlags.SolidColor;
            CameraView cameraView = worldCamera.GetComponent<CameraView>();
            if (cameraView == null) cameraView = worldCamera.gameObject.AddComponent<CameraView>();
            cameraView.Bind(Player, new Vector2(World.Width, World.Height));
            if (worldCamera.GetComponent<BackdropView>() == null) BackdropView.Create(worldCamera);

            var cursor = new GameObject("Tile Cursor View").AddComponent<TileCursorView>();
            cursor.transform.SetParent(transform, false);
            cursor.Bind(Player, World, m_Editor);

            var combatText = new GameObject("Combat Text").AddComponent<CombatText>();
            combatText.transform.SetParent(transform, false);
        }

        /// <summary>The target dummy a few tiles right of spawn, standing on the nearest surface there, and the HUD.</summary>
        void BuildTestingTools()
        {
            var dummyGo = new GameObject("Target Dummy");
            dummyGo.transform.SetParent(transform, false);
            Dummy = dummyGo.AddComponent<TargetDummy>();
            int dummyRow = MapLoader.FindStandRow(Map, m_PlayerSpawnColumn + dummyOffsetTiles, out int dummyColumn);
            Dummy.Initialize(dummyRow >= 0 ? StandPosition(dummyColumn, dummyRow, 22) : m_PlayerSpawn + new Vector2(dummyOffsetTiles * SimUnits.PixelsPerTile, 0f));

            var hud = new GameObject("HUD").AddComponent<HotbarHud>();
            hud.transform.SetParent(transform, false);
            hud.Bind(Player, Dummy);
        }

        /// <summary>Testing: the simulation waits for a class to be picked, then every player takes it.</summary>
        void BeginClassSelect()
        {
            m_Clock.enabled = false;
            var select = new GameObject("Class Select").AddComponent<ClassSelectUi>();
            select.transform.SetParent(transform, false);
            select.Bind(chosen =>
            {
                foreach (PlayerCharacter p in m_Context.Players.All)
                {
                    p.SetClass(chosen);
                    p.Inventory.Set(1, ItemId.DirtBlock, startingDirt);
                    if (startingWardBlocks > 0) p.Inventory.Set(3, ItemId.WardBlock, startingWardBlocks);
                }

                m_Input.Consume(); // drop the click that chose the class
                m_MatchFlow.ClassChosen();
            });
        }

        /// <summary>
        /// Creates a player on the given team, standing in that team's spawn room (or mid-map if the map has none).
        /// <paramref name="slot"/> spreads players along the room so they don't stack.
        /// </summary>
        PlayerCharacter SpawnPlayer(Team team, IInputSource input, TileEditor editor, bool isLocal, string playerName, int slot)
        {
            var go = new GameObject($"Player {playerName}");
            var player = go.AddComponent<PlayerCharacter>();
            player.Team = team;
            player.PlayerName = playerName;

            RectInt? room = team == Team.Blue ? Map.BlueSpawn : Map.RedSpawn;
            int column, standRow;
            if (room.HasValue)
            {
                // On the floor of the room, a few tiles in from the wall (blue from its left, red from its right).
                RectInt r = room.Value;
                int offset = 3 + slot;
                column = team == Team.Blue ? r.x + offset : r.xMax - 1 - offset;
                column = Mathf.Clamp(column, r.x + 1, r.xMax - 2);
                standRow = r.y - 1;
            }
            else
            {
                standRow = MapLoader.FindStandRow(Map, Map.CenterX, out column);
            }

            if (standRow < 0) throw new System.InvalidOperationException($"Map '{Map.Name}' has nowhere to spawn.");

            player.Initialize(World, editor, input, StandPosition(column, standRow, PlayerCharacter.HitboxWidthPx), isLocal);
            if (room.HasValue)
            {
                RectInt r = room.Value;
                player.HomeRoomPx = new Rect(r.x * SimUnits.PixelsPerTile, r.y * SimUnits.PixelsPerTile,
                                             r.width * SimUnits.PixelsPerTile, r.height * SimUnits.PixelsPerTile);
            }

            return player;
        }

        /// <summary>Altars (base and mid) and the Drakon spawns, placed from the map layout.</summary>
        void CreateObjectivePieces(GemMatch gemMatch)
        {
            AltarAt(AltarKind.Base, Team.Blue, MapLayout.BaseAltarTile(Map, Team.Blue));
            AltarAt(AltarKind.Base, Team.Red, MapLayout.BaseAltarTile(Map, Team.Red));
            AltarAt(AltarKind.Mid, Team.Neutral, MapLayout.MidAltarTile(Map));
            MobDirector.Create(transform, Map, gemMatch);
        }

        void AltarAt(AltarKind kind, Team owner, Vector2Int floorTile)
        {
            // Stands on top of the floor tile, centred on it.
            Altar.Create(transform, kind, owner, new Vector2((floorTile.x + 0.5f) * SimUnits.PixelsPerTile, (floorTile.y + 1) * SimUnits.PixelsPerTile));
        }

        /// <summary>Hitbox bottom-left, centred on a tile column and standing on top of the given tile row.</summary>
        static Vector2 StandPosition(int column, int standRow, float hitboxWidthPx) => new(
            (column + 0.5f) * SimUnits.PixelsPerTile - hitboxWidthPx * 0.5f,
            (standRow + 1) * SimUnits.PixelsPerTile);

        void CreateWorldRenderer()
        {
            var grid = new GameObject("World Grid").AddComponent<Grid>();
            grid.cellSize = Vector3.one;
            grid.transform.SetParent(transform, false);

            var tilemapGo = new GameObject("Tiles");
            tilemapGo.transform.SetParent(grid.transform, false);
            tilemapGo.AddComponent<Tilemap>();
            tilemapGo.AddComponent<TilemapRenderer>();
            tilemapGo.AddComponent<TileWorldRenderer>().Bind(World);
        }

        sealed class TileEditorTicker : ITickable
        {
            readonly TileEditor m_Editor;
            public TileEditorTicker(TileEditor editor) => m_Editor = editor;
            public void Tick(int tick) => m_Editor.Tick(tick);
        }
    }
}
