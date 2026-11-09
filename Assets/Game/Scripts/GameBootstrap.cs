using CTG.Classes;
using CTG.Combat;
using CTG.Core;
using CTG.Gameplay;
using CTG.Items;
using CTG.Rendering;
using CTG.UI;
using CTG.World;
using UnityEngine;
using UnityEngine.Tilemaps;
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

        [SerializeField] Camera worldCamera;

        public TileWorld World { get; private set; }
        public LoadedMap Map { get; private set; }
        public PlayerCharacter Player { get; private set; }
        public TargetDummy Dummy { get; private set; }

        void Awake()
        {
            Application.targetFrameRate = -1;

            var clock = new GameObject("Simulation Clock").AddComponent<SimulationClock>();
            clock.transform.SetParent(transform, false);

            Map = MapLoader.Load(mapName);
            World = Map.World;

            var editor = new TileEditor(World);
            clock.Register(new TileEditorTicker(editor));

            CreateWorldRenderer();

            if (worldCamera == null) worldCamera = Camera.main;

            var inputGo = new GameObject("Player Input");
            inputGo.transform.SetParent(transform, false);
            var input = inputGo.AddComponent<PlayerInput>();
            input.SetCamera(worldCamera);

            var playerGo = new GameObject("Player");
            Player = playerGo.AddComponent<PlayerCharacter>();
            // Spawn in the middle of the map, on the first surface found from the top.
            int standRow = MapLoader.FindStandRow(Map, Map.CenterX, out int spawnColumn);
            if (standRow < 0) throw new System.InvalidOperationException($"Map '{Map.Name}' has nowhere to spawn.");

            Vector2 spawn = StandPosition(spawnColumn, standRow, PlayerCharacter.HitboxWidthPx);
            Player.Initialize(World, editor, input, spawn);

            // Registered after the player so projectiles fired this tick also move this tick.
            var projectiles = new GameObject("Projectiles").AddComponent<ProjectileSystem>();
            projectiles.transform.SetParent(transform, false);
            projectiles.Initialize(World);


            worldCamera.backgroundColor = new Color32(100, 150, 230, 255);
            worldCamera.clearFlags = CameraClearFlags.SolidColor;
            CameraView cameraView = worldCamera.GetComponent<CameraView>();
            if (cameraView == null) cameraView = worldCamera.gameObject.AddComponent<CameraView>();
            cameraView.Bind(Player, new Vector2(World.Width, World.Height));

            var cursor = new GameObject("Tile Cursor View").AddComponent<TileCursorView>();
            cursor.transform.SetParent(transform, false);
            cursor.Bind(Player, World, editor);

            var combatText = new GameObject("Combat Text").AddComponent<CombatText>();
            combatText.transform.SetParent(transform, false);

            // Testing target a few tiles right of spawn, standing on the nearest surface there.
            var dummyGo = new GameObject("Target Dummy");
            dummyGo.transform.SetParent(transform, false);
            Dummy = dummyGo.AddComponent<TargetDummy>();
            int dummyRow = MapLoader.FindStandRow(Map, spawnColumn + dummyOffsetTiles, out int dummyColumn);
            Dummy.Initialize(dummyRow >= 0 ? StandPosition(dummyColumn, dummyRow, 22) : spawn + new Vector2(dummyOffsetTiles * SimUnits.PixelsPerTile, 0f));

            var hud = new GameObject("HUD").AddComponent<HotbarHud>();
            hud.transform.SetParent(transform, false);
            hud.Bind(Player, Dummy);

            // Testing: pick a class before the simulation starts.
            clock.enabled = false;
            var select = new GameObject("Class Select").AddComponent<ClassSelectUi>();
            select.transform.SetParent(transform, false);
            select.Bind(chosen =>
            {
                Player.SetClass(chosen);
                Player.Inventory.Set(1, ItemId.DirtBlock, startingDirt);
                if (startingWardBlocks > 0) Player.Inventory.Set(3, ItemId.WardBlock, startingWardBlocks);
                input.Consume(); // drop the click that chose the class
                clock.enabled = true;
            });
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
