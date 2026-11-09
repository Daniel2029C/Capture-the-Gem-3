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
    /// Builds the single-player sandbox at runtime: a flat dirt map, the local player with CTG2's
    /// pickaxe and 300 dirt, the camera, cursor and HUD. The scene only needs this component and a camera.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        [Header("Map")]
        [SerializeField] int worldWidth = 300;
        [SerializeField] int worldHeight = 120;
        [Tooltip("Rows of dirt from the bottom of the world.")]
        [SerializeField] int floorDepth = 30;

        [Header("Loadout (CTG2 defaults)")]
        [SerializeField] ItemId pickaxe = ItemId.ShardstonePickaxe;
        [SerializeField] int startingDirt = 300;

        [Header("Testing")]
        [Tooltip("Tiles right of the player spawn where the target dummy stands.")]
        [SerializeField] int dummyOffsetTiles = 6;

        [SerializeField] Camera worldCamera;

        public TileWorld World { get; private set; }
        public PlayerCharacter Player { get; private set; }
        public TargetDummy Dummy { get; private set; }

        void Awake()
        {
            Application.targetFrameRate = -1;

            var clock = new GameObject("Simulation Clock").AddComponent<SimulationClock>();
            clock.transform.SetParent(transform, false);

            World = new TileWorld(worldWidth, worldHeight);
            GenerateFlatFloor(World, floorDepth);

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
            float spawnX = worldWidth * 0.5f * SimUnits.PixelsPerTile - PlayerCharacter.HitboxWidthPx * 0.5f;
            float spawnY = floorDepth * SimUnits.PixelsPerTile;
            Player.Initialize(World, editor, input, new Vector2(spawnX, spawnY));

            Player.Inventory.Set(0, pickaxe, 1);
            Player.Inventory.Set(1, ItemId.DirtBlock, startingDirt);

            worldCamera.backgroundColor = new Color32(100, 150, 230, 255);
            worldCamera.clearFlags = CameraClearFlags.SolidColor;
            CameraView cameraView = worldCamera.GetComponent<CameraView>();
            if (cameraView == null) cameraView = worldCamera.gameObject.AddComponent<CameraView>();
            cameraView.Bind(Player, new Vector2(worldWidth, worldHeight));

            var cursor = new GameObject("Tile Cursor View").AddComponent<TileCursorView>();
            cursor.transform.SetParent(transform, false);
            cursor.Bind(Player, World, editor);

            var combatText = new GameObject("Combat Text").AddComponent<CombatText>();
            combatText.transform.SetParent(transform, false);

            // Testing target a few tiles right of spawn, standing on the floor.
            var dummyGo = new GameObject("Target Dummy");
            dummyGo.transform.SetParent(transform, false);
            Dummy = dummyGo.AddComponent<TargetDummy>();
            float dummyX = (worldWidth * 0.5f + dummyOffsetTiles) * SimUnits.PixelsPerTile;
            Dummy.Initialize(new Vector2(dummyX, floorDepth * SimUnits.PixelsPerTile));

            var hud = new GameObject("HUD").AddComponent<HotbarHud>();
            hud.transform.SetParent(transform, false);
            hud.Bind(Player, Dummy);
        }

        static void GenerateFlatFloor(TileWorld world, int depth)
        {
            for (int y = 0; y < depth && y < world.Height; y++)
            {
                for (int x = 0; x < world.Width; x++)
                {
                    world.SetRaw(x, y, TileId.Dirt);
                }
            }
        }

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
