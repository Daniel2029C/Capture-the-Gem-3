using System;
using System.Collections.Generic;
using System.IO;
using CTG.World;
using UnityEditor;
using UnityEngine;

namespace CTG.EditorTools
{
    /// <summary>
    /// Map editor for the arena files in Assets/Resources/Maps: open, paint and erase tiles, liquids and shapes, select,
    /// copy and paste regions, place the layout zones (spawn rooms, mob areas, mid altar, boss spot...) and save.
    /// Open it from Tools > CTG > Map Editor.
    /// </summary>
    public sealed class MapEditorWindow : EditorWindow
    {
        const string MapsFolder = "Assets/Resources/Maps";
        const int TexPx = 4;            // texture pixels per tile, enough to show slopes and half blocks
        const float PanelWidth = 250f;

        enum Tool { Paint, Erase, Select, Zone, Pick }
        enum BrushKind { Tile, Liquid, Shape }

        MapDocument m_Doc;
        string m_Path;
        bool m_Dirty;

        // view
        float m_Zoom = 4f;              // screen pixels per tile
        Vector2 m_Pan = new(10f, 10f);
        Texture2D m_Tex;
        Color32[] m_Pixels;
        bool m_TexDirty = true;
        Rect m_Canvas;
        Vector2Int m_Hover = new(-1, -1);
        bool m_Panning;

        // held arrow keys scroll the view
        const float ArrowPanTilesPerSecond = 60f;
        bool m_HoldLeft, m_HoldRight, m_HoldUp, m_HoldDown, m_HoldFast;
        double m_LastUpdate;

        // tools
        Tool m_Tool = Tool.Paint;
        BrushKind m_BrushKind = BrushKind.Tile;
        TileId m_PaintTile = TileId.BlueDirt;
        LiquidType m_PaintLiquid = LiquidType.Lava;
        TileShape m_PaintShape = TileShape.SlopeDownLeft;
        int m_BrushSize = 1;
        bool m_Mirror;
        bool m_Stroking;

        // selection / clipboard
        RectInt? m_Selection;
        Vector2Int m_DragStart;
        bool m_Dragging;
        MapClip m_Clip;
        Texture2D m_ClipTex;
        bool m_Pasting;
        bool m_PasteAir = true;

        // moving the selection: its contents are lifted out, follow the cursor, and drop on release
        bool m_Moving;
        bool m_MoveZonesToo = true;
        Vector2Int m_MoveGrab;
        RectInt m_MoveFrom;
        MapClip m_MoveClip, m_MoveMirrorClip;
        Texture2D m_MoveTex;
        readonly List<MapZone> m_MoveZones = new();
        readonly List<MapZone> m_MoveMirrorZones = new();

        // zones
        int m_ZoneKind;
        bool m_MirrorZones = true;

        // file ui
        string[] m_MapFiles = Array.Empty<string>();
        int m_MapIndex;
        int m_NewWidth = 466, m_NewHeight = 57, m_NewBorder = 64;
        int m_ResizeWidth, m_ResizeHeight, m_ShiftX, m_ShiftY;
        bool m_ShowNew, m_ShowResize, m_ShowHelp;
        Vector2 m_PanelScroll;

        static readonly TileId[] s_Tiles = (TileId[])Enum.GetValues(typeof(TileId));
        static readonly LiquidType[] s_Liquids = (LiquidType[])Enum.GetValues(typeof(LiquidType));
        static readonly TileShape[] s_Shapes = (TileShape[])Enum.GetValues(typeof(TileShape));

        static readonly Color32 s_Background = new(17, 21, 33, 255);
        static readonly Color32 s_Lava = new(255, 120, 20, 255);
        static readonly Color32 s_Nectar = new(245, 196, 40, 255);

        [MenuItem("Tools/CTG/Map Editor")]
        public static void Open() => GetWindow<MapEditorWindow>("Map Editor");

        void OnEnable()
        {
            wantsMouseMove = true;
            RefreshMapList();
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.update += OnEditorUpdate;
            m_LastUpdate = EditorApplication.timeSinceStartup;
            if (m_Doc == null) m_Doc = MapDocument.CreateEmpty("New Map", m_NewWidth, m_NewHeight, m_NewBorder);
            m_TexDirty = true;
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.update -= OnEditorUpdate;
        }

        void OnLostFocus() => m_HoldLeft = m_HoldRight = m_HoldUp = m_HoldDown = false;

        /// <summary>Scrolls the view while arrow keys are held (Shift = 3× faster), at the same speed in tiles at any zoom.</summary>
        void OnEditorUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Min((float)(now - m_LastUpdate), 0.05f);
            m_LastUpdate = now;

            var dir = new Vector2((m_HoldRight ? 1 : 0) - (m_HoldLeft ? 1 : 0), (m_HoldDown ? 1 : 0) - (m_HoldUp ? 1 : 0));
            if (dir == Vector2.zero) return;

            float speed = ArrowPanTilesPerSecond * m_Zoom * (m_HoldFast ? 3f : 1f);
            m_Pan -= dir.normalized * speed * dt;
            Repaint();
        }

        void OnDestroy()
        {
            if (m_Tex != null) DestroyImmediate(m_Tex);
            if (m_ClipTex != null) DestroyImmediate(m_ClipTex);
            if (m_MoveTex != null) DestroyImmediate(m_MoveTex);
            if (m_Doc != null)
            {
                Undo.ClearUndo(m_Doc);
                DestroyImmediate(m_Doc);
            }
        }

        void OnUndoRedo()
        {
            m_TexDirty = true;
            m_Dirty = true;
            Repaint();
        }

        // =========================================================================================================
        // file
        // =========================================================================================================

        void RefreshMapList()
        {
            m_MapFiles = Directory.Exists(MapsFolder) ? Directory.GetFiles(MapsFolder, "*.json") : Array.Empty<string>();
            for (int i = 0; i < m_MapFiles.Length; i++) m_MapFiles[i] = m_MapFiles[i].Replace('\\', '/');
            Array.Sort(m_MapFiles, StringComparer.OrdinalIgnoreCase);
            m_MapIndex = Mathf.Clamp(Array.IndexOf(m_MapFiles, m_Path), 0, Mathf.Max(0, m_MapFiles.Length - 1));
        }

        bool ConfirmDiscard() => !m_Dirty || EditorUtility.DisplayDialog("Unsaved changes", "Discard the unsaved changes to this map?", "Discard", "Cancel");

        void LoadMap(string path)
        {
            if (!ConfirmDiscard()) return;

            try
            {
                MapFile file = JsonUtility.FromJson<MapFile>(File.ReadAllText(path));
                ReplaceDoc(MapDocument.FromFile(file));
                m_Path = path;
                m_Dirty = false;
                FitView();
                RefreshMapList();
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Could not open map", ex.Message, "OK");
            }
        }

        void ReplaceDoc(MapDocument doc)
        {
            if (m_Doc != null)
            {
                Undo.ClearUndo(m_Doc);
                DestroyImmediate(m_Doc);
            }

            m_Doc = doc;
            m_Selection = null;
            m_Pasting = false;
            m_TexDirty = true;
            m_ResizeWidth = doc.Width;
            m_ResizeHeight = doc.Height;
        }

        void Save(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            File.WriteAllText(path, JsonUtility.ToJson(m_Doc.ToFile(), true));
            AssetDatabase.ImportAsset(path);
            m_Path = path;
            m_Dirty = false;
            RefreshMapList();
            ShowNotification(new GUIContent($"Saved {Path.GetFileName(path)}"));
        }

        void SaveAs()
        {
            string name = string.IsNullOrEmpty(m_Doc.MapName) ? "map" : m_Doc.MapName.ToLowerInvariant().Replace(' ', '_');
            string path = EditorUtility.SaveFilePanelInProject("Save map", name, "json", "Save the map into the maps folder.", MapsFolder);
            if (!string.IsNullOrEmpty(path)) Save(path);
        }

        void FitView()
        {
            if (m_Doc == null || m_Canvas.width <= 0) return;

            m_Zoom = Mathf.Clamp(Mathf.Min((m_Canvas.width - 20f) / m_Doc.Width, (m_Canvas.height - 20f) / m_Doc.Height), 1f, 64f);
            m_Pan = new Vector2((m_Canvas.width - m_Doc.Width * m_Zoom) * 0.5f, (m_Canvas.height - m_Doc.Height * m_Zoom) * 0.5f);
        }

        // =========================================================================================================
        // gui
        // =========================================================================================================

        void OnGUI()
        {
            if (m_Doc == null) m_Doc = MapDocument.CreateEmpty("New Map", m_NewWidth, m_NewHeight, m_NewBorder);

            HandleCommands(Event.current);
            HandleShortcuts(Event.current);

            var panel = new Rect(0, 0, PanelWidth, position.height);
            bool firstLayout = m_Canvas.width <= 0;
            m_Canvas = new Rect(PanelWidth, 0, position.width - PanelWidth, position.height);
            if (firstLayout) FitView();

            DrawCanvas();
            GUILayout.BeginArea(panel, EditorStyles.helpBox);
            m_PanelScroll = GUILayout.BeginScrollView(m_PanelScroll);
            DrawPanel();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawPanel()
        {
            // ---- file ----
            GUILayout.Label("Map", EditorStyles.boldLabel);
            using (new GUILayout.HorizontalScope())
            {
                string[] names = Array.ConvertAll(m_MapFiles, Path.GetFileNameWithoutExtension);
                m_MapIndex = EditorGUILayout.Popup(m_MapIndex, names.Length > 0 ? names : new[] { "(no maps)" });
                if (GUILayout.Button("Open", GUILayout.Width(50)) && m_MapFiles.Length > 0) LoadMap(m_MapFiles[m_MapIndex]);
            }

            using (new GUILayout.HorizontalScope())
            {
                GUI.enabled = !string.IsNullOrEmpty(m_Path);
                if (GUILayout.Button("Save")) Save(m_Path);
                GUI.enabled = true;
                if (GUILayout.Button("Save As")) SaveAs();
                if (GUILayout.Button("Refresh", GUILayout.Width(60))) RefreshMapList();
            }

            EditorGUI.BeginChangeCheck();
            string newName = EditorGUILayout.TextField("Name", m_Doc.MapName);
            int newBorder = EditorGUILayout.IntField("Border", m_Doc.Border);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RegisterCompleteObjectUndo(m_Doc, "Map settings");
                m_Doc.MapName = newName;
                m_Doc.Border = Mathf.Max(0, newBorder);
                m_Dirty = true;
            }

            m_ShowNew = EditorGUILayout.Foldout(m_ShowNew, "New map", true);
            if (m_ShowNew)
            {
                m_NewWidth = Mathf.Max(8, EditorGUILayout.IntField("Width", m_NewWidth));
                m_NewHeight = Mathf.Max(8, EditorGUILayout.IntField("Height", m_NewHeight));
                m_NewBorder = Mathf.Max(0, EditorGUILayout.IntField("Border", m_NewBorder));
                if (GUILayout.Button("Create empty map") && ConfirmDiscard())
                {
                    ReplaceDoc(MapDocument.CreateEmpty("New Map", m_NewWidth, m_NewHeight, m_NewBorder));
                    m_Path = null;
                    m_Dirty = true;
                    FitView();
                }
            }

            m_ShowResize = EditorGUILayout.Foldout(m_ShowResize, "Resize", true);
            if (m_ShowResize)
            {
                m_ResizeWidth = Mathf.Max(8, EditorGUILayout.IntField("Width", m_ResizeWidth));
                m_ResizeHeight = Mathf.Max(8, EditorGUILayout.IntField("Height", m_ResizeHeight));
                m_ShiftX = EditorGUILayout.IntField(new GUIContent("Shift right", "Move existing content right by this many tiles (adds space on the left)."), m_ShiftX);
                m_ShiftY = EditorGUILayout.IntField(new GUIContent("Shift down", "Move existing content down by this many tiles (adds space on top)."), m_ShiftY);
                if (GUILayout.Button("Centre horizontally")) m_ShiftX = (m_ResizeWidth - m_Doc.Width) / 2;
                if (GUILayout.Button("Apply resize"))
                {
                    Undo.RegisterCompleteObjectUndo(m_Doc, "Resize map");
                    m_Doc.Resize(m_ResizeWidth, m_ResizeHeight, m_ShiftX, m_ShiftY);
                    m_Selection = null;
                    m_Dirty = true;
                    m_TexDirty = true;
                }
            }

            EditorGUILayout.Space();

            // ---- tools ----
            GUILayout.Label("Tool", EditorStyles.boldLabel);
            SetTool((Tool)GUILayout.SelectionGrid((int)m_Tool, new[] { "Paint (B)", "Erase (E)", "Select (S)", "Zone (Z)", "Pick (I)" }, 3));
            m_Mirror = EditorGUILayout.ToggleLeft(new GUIContent("Mirror to other side (M)", "Also apply every edit at the mirrored position, with team colours and slopes swapped."), m_Mirror);
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Mirror left → right", "Overwrite the right half with the left half, mirrored (red/blue and slopes swapped)."))) MirrorHalf(true);
                if (GUILayout.Button(new GUIContent("Mirror right → left", "Overwrite the left half with the right half, mirrored (red/blue and slopes swapped)."))) MirrorHalf(false);
            }
            m_MirrorZones = EditorGUILayout.ToggleLeft(new GUIContent("Mirror zones too", "Blue/red zones and no-build areas on the source half replace the other half's. Mid altar and boss spot are kept."), m_MirrorZones);

            EditorGUILayout.Space();

            // ---- brush ----
            GUILayout.Label("Brush", EditorStyles.boldLabel);
            m_BrushKind = (BrushKind)GUILayout.Toolbar((int)m_BrushKind, new[] { "Block", "Liquid", "Shape" });
            m_BrushSize = EditorGUILayout.IntSlider("Size ([ ])", m_BrushSize, 1, 16);
            switch (m_BrushKind)
            {
                case BrushKind.Tile:
                    foreach (TileId id in s_Tiles)
                    {
                        if (PaletteButton(TileName(id), TileColor(id), m_PaintTile == id)) m_PaintTile = id;
                    }
                    break;
                case BrushKind.Liquid:
                    foreach (LiquidType l in s_Liquids)
                    {
                        if (PaletteButton(l == LiquidType.None ? "None (drain)" : l.ToString(), LiquidColor(l), m_PaintLiquid == l)) m_PaintLiquid = l;
                    }
                    EditorGUILayout.HelpBox("Liquid only sits in empty cells: painting it clears the block there.", MessageType.None);
                    break;
                case BrushKind.Shape:
                    foreach (TileShape s in s_Shapes)
                    {
                        if (PaletteButton(s.ToString(), new Color(0.7f, 0.7f, 0.75f), m_PaintShape == s)) m_PaintShape = s;
                    }
                    EditorGUILayout.HelpBox("Reshapes existing blocks; empty cells are left alone.", MessageType.None);
                    break;
            }

            EditorGUILayout.Space();

            // ---- selection ----
            GUILayout.Label("Selection", EditorStyles.boldLabel);
            using (new GUILayout.HorizontalScope())
            {
                GUI.enabled = m_Selection.HasValue;
                if (GUILayout.Button("Copy")) CopySelection(false);
                if (GUILayout.Button("Cut")) CopySelection(true);
                GUI.enabled = m_Clip != null;
                if (GUILayout.Button("Paste")) BeginPaste();
                GUI.enabled = true;
            }

            using (new GUILayout.HorizontalScope())
            {
                GUI.enabled = m_Selection.HasValue;
                if (GUILayout.Button("Fill with brush")) FillSelection();
                if (GUILayout.Button("Clear")) ClearSelection();
                GUI.enabled = true;
            }

            using (new GUILayout.HorizontalScope())
            {
                GUI.enabled = m_Clip != null;
                if (GUILayout.Button("Flip clipboard (F)")) FlipClip();
                GUI.enabled = true;
                m_PasteAir = GUILayout.Toggle(m_PasteAir, new GUIContent("Paste air", "Empty cells in the clipboard (or a moved selection) overwrite what is there."), "Button");
            }

            m_MoveZonesToo = EditorGUILayout.ToggleLeft(new GUIContent("Move zones with selection", "Zones lying entirely inside the selection move with it."), m_MoveZonesToo);
            EditorGUILayout.HelpBox("Move: drag inside the selection, or nudge it with Alt+arrow keys (Shift = 10 tiles).", MessageType.None);

            EditorGUILayout.Space();

            // ---- zones ----
            GUILayout.Label("Zones", EditorStyles.boldLabel);
            m_ZoneKind = EditorGUILayout.Popup("Place", m_ZoneKind, MapDocument.ZoneKinds);
            EditorGUILayout.HelpBox(ZoneHelp(MapDocument.ZoneKinds[m_ZoneKind]), MessageType.None);
            GUI.enabled = m_Selection.HasValue;
            if (GUILayout.Button("Set zone from selection")) PlaceZone(MapDocument.ZoneKinds[m_ZoneKind], m_Selection.Value);
            GUI.enabled = true;

            for (int i = 0; i < m_Doc.Zones.Count; i++)
            {
                MapZone z = m_Doc.Zones[i];
                using (new GUILayout.HorizontalScope())
                {
                    Rect sw = GUILayoutUtility.GetRect(10, 16, GUILayout.Width(10));
                    EditorGUI.DrawRect(sw, ZoneColor(z.kind));
                    GUILayout.Label($"{z.kind}  {z.x},{z.y}  {z.w}×{z.h}", EditorStyles.miniLabel);
                    if (GUILayout.Button("Sel", EditorStyles.miniButtonLeft, GUILayout.Width(30))) m_Selection = new RectInt(z.x, z.y, z.w, z.h);
                    if (GUILayout.Button("×", EditorStyles.miniButtonRight, GUILayout.Width(20)))
                    {
                        Undo.RegisterCompleteObjectUndo(m_Doc, "Delete zone");
                        m_Doc.Zones.RemoveAt(i);
                        m_Dirty = true;
                        GUIUtility.ExitGUI();
                    }
                }
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Fit view (Home)")) FitView();
            m_ShowHelp = EditorGUILayout.Foldout(m_ShowHelp, "Controls", true);
            if (m_ShowHelp)
            {
                EditorGUILayout.HelpBox(
                    "Left drag: use tool. Right drag (Paint): erase.\n" +
                    "Hold arrow keys (Shift = faster), middle drag or Alt+drag: pan. Scroll: zoom.\n" +
                    "B/E/S/Z/I: tools. [ ]: brush size. M: mirror.\n" +
                    "Select: drag a box. Drag inside it to move it; Alt+arrows nudge (Shift = 10).\n" +
                    "Ctrl/Cmd+C/X/V copy, cut, paste; Delete clears; Esc cancels.\n" +
                    "Pasting: the clipboard follows the cursor, click to place, F flips it.\n" +
                    "Zone tool: drag a box to place the chosen zone.\n" +
                    "Ctrl/Cmd+Z / Shift+Z: undo / redo. Ctrl/Cmd+S: save.", MessageType.None);
            }
        }

        static bool PaletteButton(string label, Color color, bool selected)
        {
            Rect r = GUILayoutUtility.GetRect(new GUIContent(label), EditorStyles.toolbarButton, GUILayout.Height(20));
            bool pressed = GUI.Toggle(r, selected, "", EditorStyles.toolbarButton) && !selected;
            EditorGUI.DrawRect(new Rect(r.x + 4, r.y + 3, 14, r.height - 6), color);
            GUI.Label(new Rect(r.x + 24, r.y, r.width - 24, r.height), label, selected ? EditorStyles.boldLabel : EditorStyles.label);
            return pressed;
        }

        // =========================================================================================================
        // canvas
        // =========================================================================================================

        void DrawCanvas()
        {
            Event e = Event.current;
            EditorGUI.DrawRect(m_Canvas, new Color(0.06f, 0.07f, 0.1f));
            GUI.BeginGroup(m_Canvas);

            if (m_TexDirty) RebuildTexture();
            var mapRect = new Rect(m_Pan.x, m_Pan.y, m_Doc.Width * m_Zoom, m_Doc.Height * m_Zoom);
            GUI.DrawTexture(mapRect, m_Tex, ScaleMode.StretchToFill, false);
            DrawOutline(mapRect, new Color(0.35f, 0.38f, 0.48f), 2f);
            if (m_Zoom >= 10f) DrawGrid(mapRect);
            DrawCentreLine(mapRect);
            DrawZones();
            DrawSelectionAndCursor();

            HandleCanvasInput(e);
            GUI.EndGroup();

            DrawInfo();
        }

        Vector2Int ScreenToTile(Vector2 local) =>
            new(Mathf.FloorToInt((local.x - m_Pan.x) / m_Zoom), Mathf.FloorToInt((local.y - m_Pan.y) / m_Zoom));

        Rect TileRect(int x, int y, int w = 1, int h = 1) => new(m_Pan.x + x * m_Zoom, m_Pan.y + y * m_Zoom, w * m_Zoom, h * m_Zoom);

        void RebuildTexture()
        {
            int tw = m_Doc.Width * TexPx, th = m_Doc.Height * TexPx;
            if (m_Tex == null || m_Tex.width != tw || m_Tex.height != th)
            {
                if (m_Tex != null) DestroyImmediate(m_Tex);
                m_Tex = new Texture2D(tw, th, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                m_Pixels = new Color32[tw * th];
            }

            for (int y = 0; y < m_Doc.Height; y++)
            {
                for (int x = 0; x < m_Doc.Width; x++) WriteTilePixels(x, y);
            }

            m_Tex.SetPixels32(m_Pixels);
            m_Tex.Apply(false);
            m_TexDirty = false;
        }

        void WriteTilePixels(int x, int y)
        {
            TileId tile = m_Doc.GetTile(x, y);
            TileShape shape = m_Doc.GetShape(x, y);
            LiquidType liquid = m_Doc.GetLiquid(x, y);
            Color32 back = liquid == LiquidType.Lava ? s_Lava : liquid == LiquidType.Nectar ? s_Nectar : s_Background;
            Color32 fill = TileColor(tile);
            int tw = m_Doc.Width * TexPx;
            int baseY = (m_Doc.Height - 1 - y) * TexPx;   // texture rows run bottom-up

            for (int py = 0; py < TexPx; py++)
            {
                for (int px = 0; px < TexPx; px++)
                {
                    Color32 c = back;
                    if (tile == TileId.Platform)
                    {
                        if (py == TexPx - 1) c = fill;             // a thin top line, like the in-game platform
                    }
                    else if (tile != TileId.Air)
                    {
                        int sx = px * 16 / TexPx + 2, sy = py * 16 / TexPx + 2;
                        if (TileShapes.Covers(shape, sx, sy)) c = fill;
                    }

                    m_Pixels[(baseY + py) * tw + x * TexPx + px] = c;
                }
            }
        }

        static void DrawOutline(Rect r, Color c, float t)
        {
            EditorGUI.DrawRect(new Rect(r.x - t, r.y - t, r.width + 2 * t, t), c);
            EditorGUI.DrawRect(new Rect(r.x - t, r.yMax, r.width + 2 * t, t), c);
            EditorGUI.DrawRect(new Rect(r.x - t, r.y, t, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax, r.y, t, r.height), c);
        }

        void DrawGrid(Rect mapRect)
        {
            var c = new Color(1f, 1f, 1f, 0.06f);
            int x0 = Mathf.Max(0, Mathf.FloorToInt(-m_Pan.x / m_Zoom)), x1 = Mathf.Min(m_Doc.Width, Mathf.CeilToInt((m_Canvas.width - m_Pan.x) / m_Zoom));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(-m_Pan.y / m_Zoom)), y1 = Mathf.Min(m_Doc.Height, Mathf.CeilToInt((m_Canvas.height - m_Pan.y) / m_Zoom));
            for (int x = x0; x <= x1; x++) EditorGUI.DrawRect(new Rect(m_Pan.x + x * m_Zoom, mapRect.y, 1, mapRect.height), c);
            for (int y = y0; y <= y1; y++) EditorGUI.DrawRect(new Rect(mapRect.x, m_Pan.y + y * m_Zoom, mapRect.width, 1), c);
        }

        void DrawCentreLine(Rect mapRect)
        {
            float cx = m_Pan.x + m_Doc.Width * 0.5f * m_Zoom;
            EditorGUI.DrawRect(new Rect(cx, mapRect.y, 1, mapRect.height), new Color(1f, 1f, 1f, 0.18f));
        }

        void DrawZones()
        {
            var style = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = Color.white } };
            foreach (MapZone z in m_Doc.Zones)
            {
                Color c = ZoneColor(z.kind);
                Rect r = TileRect(z.x, z.y, z.w, z.h);
                EditorGUI.DrawRect(r, new Color(c.r, c.g, c.b, 0.12f));
                DrawOutline(r, c, 2f);
                var labelRect = new Rect(r.x + 3, r.y + 2, Mathf.Max(80, r.width), 16);
                EditorGUI.DrawRect(new Rect(labelRect.x - 2, labelRect.y, style.CalcSize(new GUIContent(z.kind)).x + 4, 14), new Color(0, 0, 0, 0.6f));
                GUI.Label(labelRect, z.kind, style);

                // altar / boss: mark the floor tile they stand on (the row under the zone's bottom centre)
                if (z.kind.StartsWith("Altar") || z.kind == "BossMid")
                {
                    Rect floor = TileRect(z.x + z.w / 2, z.y + z.h, 1, 1);
                    DrawOutline(floor, c, 2f);
                }
            }
        }

        void DrawSelectionAndCursor()
        {
            if (m_Moving)
            {
                Vector2Int at = m_Hover.x >= 0 ? m_Hover - m_MoveGrab : m_MoveFrom.position;
                Rect r = TileRect(at.x, at.y, m_MoveFrom.width, m_MoveFrom.height);
                GUI.DrawTexture(r, m_MoveTex, ScaleMode.StretchToFill, true);
                DrawOutline(r, new Color(0.3f, 0.85f, 1f), 2f);
                if (m_MoveMirrorClip != null)
                {
                    RectInt mf = MirrorRect(m_MoveFrom);
                    DrawOutline(TileRect(mf.x - (at.x - m_MoveFrom.x), at.y, mf.width, mf.height), new Color(0.3f, 0.85f, 1f, 0.5f), 1f);
                }

                return;
            }

            if (m_Selection.HasValue)
            {
                RectInt s = m_Selection.Value;
                if (m_Tool == Tool.Select && !m_Pasting) EditorGUIUtility.AddCursorRect(TileRect(s.x, s.y, s.width, s.height), MouseCursor.MoveArrow);
                Rect r = TileRect(s.x, s.y, s.width, s.height);
                EditorGUI.DrawRect(r, new Color(0.3f, 0.8f, 1f, 0.15f));
                DrawOutline(r, new Color(0.3f, 0.85f, 1f), 1.5f);
            }

            if (m_Hover.x < 0) return;

            if (m_Pasting && m_Clip != null)
            {
                Rect r = TileRect(m_Hover.x, m_Hover.y, m_Clip.Width, m_Clip.Height);
                GUI.color = new Color(1f, 1f, 1f, 0.7f);
                GUI.DrawTexture(r, m_ClipTex, ScaleMode.StretchToFill, true);
                GUI.color = Color.white;
                DrawOutline(r, new Color(1f, 0.85f, 0.3f), 1.5f);
                if (m_Mirror)
                {
                    Rect mr = TileRect(m_Doc.Width - m_Hover.x - m_Clip.Width, m_Hover.y, m_Clip.Width, m_Clip.Height);
                    DrawOutline(mr, new Color(1f, 0.85f, 0.3f, 0.5f), 1f);
                }

                return;
            }

            if (m_Tool == Tool.Paint || m_Tool == Tool.Erase)
            {
                RectInt b = BrushRect(m_Hover);
                DrawOutline(TileRect(b.x, b.y, b.width, b.height), Color.white, 1f);
                if (m_Mirror)
                {
                    DrawOutline(TileRect(m_Doc.Width - b.x - b.width, b.y, b.width, b.height), new Color(1f, 1f, 1f, 0.4f), 1f);
                }
            }
            else
            {
                DrawOutline(TileRect(m_Hover.x, m_Hover.y), Color.white, 1f);
            }

            if (m_Dragging && (m_Tool == Tool.Select || m_Tool == Tool.Zone))
            {
                RectInt d = DragRect(m_DragStart, m_Hover);
                Color c = m_Tool == Tool.Zone ? ZoneColor(MapDocument.ZoneKinds[m_ZoneKind]) : new Color(0.3f, 0.85f, 1f);
                DrawOutline(TileRect(d.x, d.y, d.width, d.height), c, 2f);
            }
        }

        void DrawInfo()
        {
            var lines = new List<string>
            {
                $"{m_Doc.MapName}{(m_Dirty ? "  *unsaved" : "")}",
                string.IsNullOrEmpty(m_Path) ? "(not saved yet)" : m_Path,
                $"Arena {m_Doc.Width} × {m_Doc.Height} tiles   ({m_Doc.Width * 16} × {m_Doc.Height * 16} px)",
                $"Border {m_Doc.Border}  →  world {m_Doc.Width + 2 * m_Doc.Border} × {m_Doc.Height + 2 * m_Doc.Border} tiles",
                $"Centre column {m_Doc.Width / 2}   Zoom {m_Zoom:0.#} px/tile",
                $"Tool {m_Tool}{(m_Mirror ? "  (mirror)" : "")}   Brush {BrushLabel()}  size {m_BrushSize}",
            };

            if (m_Doc.InBounds(m_Hover.x, m_Hover.y))
            {
                int wx = m_Doc.Border + m_Hover.x, wy = m_Doc.Border + (m_Doc.Height - 1 - m_Hover.y);
                TileId t = m_Doc.GetTile(m_Hover.x, m_Hover.y);
                TileShape s = m_Doc.GetShape(m_Hover.x, m_Hover.y);
                LiquidType l = m_Doc.GetLiquid(m_Hover.x, m_Hover.y);
                lines.Add($"Cursor {m_Hover.x}, {m_Hover.y} (from top-left)   world {wx}, {wy}");
                lines.Add($"  {TileName(t)}{(t != TileId.Air && s != TileShape.Full ? $", {s}" : "")}{(l != LiquidType.None ? $", {l}" : "")}");
                string inZones = "";
                foreach (MapZone z in m_Doc.Zones)
                {
                    if (m_Hover.x >= z.x && m_Hover.x < z.x + z.w && m_Hover.y >= z.y && m_Hover.y < z.y + z.h) inZones += z.kind + " ";
                }
                if (inZones.Length > 0) lines.Add($"  in {inZones}");
            }

            if (m_Selection.HasValue) lines.Add($"Selection {m_Selection.Value.x}, {m_Selection.Value.y}  {m_Selection.Value.width} × {m_Selection.Value.height}");
            if (m_Moving) lines.Add($"Moving {m_MoveFrom.width} × {m_MoveFrom.height}  offset {(m_Hover - m_MoveGrab).x - m_MoveFrom.x}, {(m_Hover - m_MoveGrab).y - m_MoveFrom.y}");
            if (m_Clip != null) lines.Add($"Clipboard {m_Clip.Width} × {m_Clip.Height}{(m_Pasting ? "  (click to paste)" : "")}");

            string missing = "";
            foreach (string k in new[] { "SpawnBlue", "SpawnRed", "MobSpawnBlue", "MobSpawnRed", "MobMidBlue", "MobMidRed", "AltarMid", "BossMid" })
            {
                if (m_Doc.FindZone(k) == null) missing += k + " ";
            }
            lines.Add(missing.Length == 0 ? "Layout zones: all set" : $"Not set (game derives them): {missing}");

            var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.9f, 0.92f, 0.96f) }, richText = false };
            float w = 0f;
            foreach (string s in lines) w = Mathf.Max(w, style.CalcSize(new GUIContent(s)).x);
            float h = lines.Count * 14f + 8f;
            var box = new Rect(m_Canvas.xMax - w - 20f, m_Canvas.y + 8f, w + 12f, h);
            EditorGUI.DrawRect(box, new Color(0f, 0f, 0f, 0.72f));
            for (int i = 0; i < lines.Count; i++) GUI.Label(new Rect(box.x + 6, box.y + 4 + i * 14f, w + 4, 16), lines[i], style);
        }

        string BrushLabel() => m_BrushKind switch
        {
            BrushKind.Tile => TileName(m_PaintTile),
            BrushKind.Liquid => m_PaintLiquid.ToString(),
            _ => m_PaintShape.ToString(),
        };

        // =========================================================================================================
        // input
        // =========================================================================================================

        void HandleCanvasInput(Event e)
        {
            var local = new Rect(0, 0, m_Canvas.width, m_Canvas.height);
            bool inside = local.Contains(e.mousePosition);

            if (m_Moving && e.rawType == EventType.MouseUp && e.button == 0)
            {
                FinishMove(m_Hover.x >= 0 ? m_Hover - m_MoveGrab : m_MoveFrom.position);
                e.Use();
                return;
            }

            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
            {
                Vector2Int t = inside ? ScreenToTile(e.mousePosition) : new Vector2Int(-1, -1);
                if (t != m_Hover)
                {
                    m_Hover = t;
                    Repaint();
                }
            }

            switch (e.type)
            {
                case EventType.ScrollWheel when inside:
                {
                    float old = m_Zoom;
                    m_Zoom = Mathf.Clamp(m_Zoom * (e.delta.y > 0 ? 1f / 1.15f : 1.15f), 0.5f, 64f);
                    // keep the point under the cursor fixed
                    m_Pan = e.mousePosition - (e.mousePosition - m_Pan) * (m_Zoom / old);
                    e.Use();
                    break;
                }
                case EventType.MouseDown when inside:
                    if (e.button == 2 || (e.button == 0 && e.alt))
                    {
                        m_Panning = true;
                        e.Use();
                        break;
                    }

                    GUI.FocusControl(null);
                    OnPointerDown(e);
                    e.Use();
                    break;
                case EventType.MouseDrag:
                    if (m_Panning)
                    {
                        m_Pan += e.delta;
                        e.Use();
                        Repaint();
                        break;
                    }

                    if (m_Stroking && inside) StrokeAt(ScreenToTile(e.mousePosition), e.button == 1);
                    if (m_Dragging) Repaint();
                    e.Use();
                    break;
                case EventType.MouseUp:
                    if (m_Panning)
                    {
                        m_Panning = false;
                        e.Use();
                        break;
                    }

                    OnPointerUp(e);
                    break;
            }
        }

        void OnPointerDown(Event e)
        {
            Vector2Int t = ScreenToTile(e.mousePosition);

            if (m_Pasting && e.button == 0)
            {
                Undo.RegisterCompleteObjectUndo(m_Doc, "Paste");
                m_Doc.Paste(m_Clip, t.x, t.y, m_PasteAir);
                if (m_Mirror) m_Doc.Paste(m_Clip.Flipped(), m_Doc.Width - t.x - m_Clip.Width, t.y, m_PasteAir);
                m_Dirty = true;
                m_TexDirty = true;
                if (!e.shift) m_Pasting = false;       // shift-click to keep stamping
                return;
            }

            switch (m_Tool)
            {
                case Tool.Paint:
                case Tool.Erase:
                    Undo.RegisterCompleteObjectUndo(m_Doc, m_Tool == Tool.Erase || e.button == 1 ? "Erase" : "Paint");
                    m_Stroking = true;
                    StrokeAt(t, e.button == 1);
                    break;
                case Tool.Select when e.button == 0 && m_Selection.HasValue && m_Selection.Value.Contains(t):
                    m_MoveGrab = t - m_Selection.Value.position;
                    Undo.RegisterCompleteObjectUndo(m_Doc, "Move selection");
                    LiftSelection();
                    break;
                case Tool.Select:
                case Tool.Zone:
                    m_DragStart = t;
                    m_Dragging = true;
                    break;
                case Tool.Pick:
                    Pick(t);
                    break;
            }
        }

        void OnPointerUp(Event e)
        {
            if (m_Stroking)
            {
                m_Stroking = false;
                Undo.IncrementCurrentGroup();
            }

            if (!m_Dragging) return;

            m_Dragging = false;
            RectInt r = DragRect(m_DragStart, ScreenToTile(e.mousePosition));
            if (r.width <= 0 || r.height <= 0) return;

            if (m_Tool == Tool.Select) m_Selection = r;
            else if (m_Tool == Tool.Zone) PlaceZone(MapDocument.ZoneKinds[m_ZoneKind], r);
            Repaint();
        }

        RectInt DragRect(Vector2Int a, Vector2Int b)
        {
            int x0 = Mathf.Clamp(Mathf.Min(a.x, b.x), 0, m_Doc.Width - 1), x1 = Mathf.Clamp(Mathf.Max(a.x, b.x), 0, m_Doc.Width - 1);
            int y0 = Mathf.Clamp(Mathf.Min(a.y, b.y), 0, m_Doc.Height - 1), y1 = Mathf.Clamp(Mathf.Max(a.y, b.y), 0, m_Doc.Height - 1);
            return new RectInt(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        }

        RectInt BrushRect(Vector2Int centre)
        {
            int half = (m_BrushSize - 1) / 2;
            return new RectInt(centre.x - half, centre.y - half, m_BrushSize, m_BrushSize);
        }

        void StrokeAt(Vector2Int t, bool erase)
        {
            erase |= m_Tool == Tool.Erase;
            RectInt b = BrushRect(t);
            bool changed = false;
            for (int y = b.y; y < b.yMax; y++)
            {
                for (int x = b.x; x < b.xMax; x++)
                {
                    changed |= ApplyBrush(x, y, erase, false);
                    if (m_Mirror) changed |= ApplyBrush(m_Doc.MirrorX(x), y, erase, true);
                }
            }

            if (!changed) return;

            m_Dirty = true;
            m_Tex.SetPixels32(m_Pixels);
            m_Tex.Apply(false);
            Repaint();
        }

        /// <summary>Applies the brush to one cell (team colours and slopes swapped when <paramref name="mirrored"/>). Returns true if it changed.</summary>
        bool ApplyBrush(int x, int y, bool erase, bool mirrored)
        {
            if (!m_Doc.InBounds(x, y)) return false;

            TileId oldT = m_Doc.GetTile(x, y);
            TileShape oldS = m_Doc.GetShape(x, y);
            LiquidType oldL = m_Doc.GetLiquid(x, y);

            if (erase) m_Doc.Clear(x, y);
            else switch (m_BrushKind)
            {
                case BrushKind.Tile:
                    m_Doc.SetTile(x, y, mirrored ? MapDocument.SwapTeam(m_PaintTile) : m_PaintTile);
                    break;
                case BrushKind.Liquid:
                    m_Doc.SetLiquid(x, y, m_PaintLiquid);
                    break;
                case BrushKind.Shape:
                    m_Doc.SetShape(x, y, mirrored ? MapDocument.MirrorShape(m_PaintShape) : m_PaintShape);
                    break;
            }

            if (oldT == m_Doc.GetTile(x, y) && oldS == m_Doc.GetShape(x, y) && oldL == m_Doc.GetLiquid(x, y)) return false;

            WriteTilePixels(x, y);
            return true;
        }

        void Pick(Vector2Int t)
        {
            if (!m_Doc.InBounds(t.x, t.y)) return;

            TileId tile = m_Doc.GetTile(t.x, t.y);
            LiquidType liquid = m_Doc.GetLiquid(t.x, t.y);
            if (tile == TileId.Air && liquid != LiquidType.None)
            {
                m_BrushKind = BrushKind.Liquid;
                m_PaintLiquid = liquid;
            }
            else
            {
                m_BrushKind = BrushKind.Tile;
                m_PaintTile = tile;
                if (m_Doc.GetShape(t.x, t.y) != TileShape.Full) m_PaintShape = m_Doc.GetShape(t.x, t.y);
            }

            SetTool(Tool.Paint);
        }

        void MirrorHalf(bool leftToRight)
        {
            if (m_Moving) return;

            Undo.RegisterCompleteObjectUndo(m_Doc, leftToRight ? "Mirror left to right" : "Mirror right to left");
            m_Doc.MirrorHalf(leftToRight, m_MirrorZones);
            Undo.IncrementCurrentGroup();
            m_Dirty = true;
            m_TexDirty = true;
            ShowNotification(new GUIContent(leftToRight ? "Mirrored left → right" : "Mirrored right → left"));
            Repaint();
        }

        /// <summary>Switches tool. Leaving the select tool drops the selection and any paste in progress.</summary>
        void SetTool(Tool tool)
        {
            if (tool == m_Tool || m_Moving) return;

            m_Tool = tool;
            if (tool != Tool.Select)
            {
                m_Selection = null;
                m_Pasting = false;
                m_Dragging = false;
            }

            Repaint();
        }

        void PlaceZone(string kind, RectInt r)
        {
            Undo.RegisterCompleteObjectUndo(m_Doc, "Place zone");
            m_Doc.SetZone(kind, r);
            string other = MapDocument.MirrorZoneKind(kind);
            if (m_Mirror && other != null)
            {
                m_Doc.SetZone(other, new RectInt(m_Doc.Width - r.x - r.width, r.y, r.width, r.height));
            }

            m_Dirty = true;
            Repaint();
        }

        // ---- selection ops ----

        void CopySelection(bool cut)
        {
            if (!m_Selection.HasValue) return;

            SetClip(m_Doc.Copy(m_Selection.Value));
            if (cut) ClearSelection();
            ShowNotification(new GUIContent(cut ? "Cut" : "Copied"));
        }

        void SetClip(MapClip clip)
        {
            m_Clip = clip;
            if (m_ClipTex != null) DestroyImmediate(m_ClipTex);
            m_ClipTex = BuildClipTexture(clip);
        }

        // ---- moving the selection ----

        RectInt MirrorRect(RectInt r) => new(m_Doc.Width - r.x - r.width, r.y, r.width, r.height);

        /// <summary>Takes the selection's cells (and, with mirror on, the mirrored region's) out of the map, plus the zones inside.</summary>
        void LiftSelection()
        {
            RectInt s = m_Selection.Value;
            m_MoveFrom = s;
            m_MoveClip = m_Doc.Copy(s);
            ClearRect(s);

            RectInt mr = MirrorRect(s);
            bool mirror = m_Mirror && !mr.Overlaps(s);
            m_MoveMirrorClip = mirror ? m_Doc.Copy(mr) : null;
            if (mirror) ClearRect(mr);

            m_MoveZones.Clear();
            m_MoveMirrorZones.Clear();
            if (m_MoveZonesToo)
            {
                foreach (MapZone z in m_Doc.Zones)
                {
                    var zr = new RectInt(z.x, z.y, z.w, z.h);
                    if (Inside(zr, s)) m_MoveZones.Add(z);
                    else if (mirror && Inside(zr, mr)) m_MoveMirrorZones.Add(z);
                }
            }

            if (m_MoveTex != null) DestroyImmediate(m_MoveTex);
            m_MoveTex = BuildClipTexture(m_MoveClip);
            m_Moving = true;
            m_TexDirty = true;
            Repaint();
        }

        /// <summary>Puts the lifted cells down with their top-left at <paramref name="topLeft"/> and moves the zones by the same offset.</summary>
        void FinishMove(Vector2Int topLeft)
        {
            if (!m_Moving) return;

            int dx = topLeft.x - m_MoveFrom.x, dy = topLeft.y - m_MoveFrom.y;
            m_Doc.Paste(m_MoveClip, topLeft.x, topLeft.y, m_PasteAir);
            if (m_MoveMirrorClip != null)
            {
                RectInt mr = MirrorRect(m_MoveFrom);
                m_Doc.Paste(m_MoveMirrorClip, mr.x - dx, mr.y + dy, m_PasteAir);
            }

            foreach (MapZone z in m_MoveZones)
            {
                z.x += dx;
                z.y += dy;
            }

            foreach (MapZone z in m_MoveMirrorZones)
            {
                z.x -= dx;
                z.y += dy;
            }

            m_Selection = new RectInt(topLeft.x, topLeft.y, m_MoveFrom.width, m_MoveFrom.height);
            m_Moving = false;
            m_MoveClip = m_MoveMirrorClip = null;
            m_MoveZones.Clear();
            m_MoveMirrorZones.Clear();
            if (dx != 0 || dy != 0) m_Dirty = true;
            m_TexDirty = true;
            Undo.IncrementCurrentGroup();
            Repaint();
        }

        void NudgeSelection(int dx, int dy)
        {
            if (m_Tool != Tool.Select || !m_Selection.HasValue || m_Moving || m_Pasting) return;

            Undo.RegisterCompleteObjectUndo(m_Doc, "Nudge selection");
            Vector2Int to = m_Selection.Value.position + new Vector2Int(dx, dy);
            LiftSelection();
            FinishMove(to);
        }

        static bool Inside(RectInt inner, RectInt outer) =>
            inner.xMin >= outer.xMin && inner.yMin >= outer.yMin && inner.xMax <= outer.xMax && inner.yMax <= outer.yMax;

        void ClearRect(RectInt r)
        {
            for (int y = r.y; y < r.yMax; y++)
            {
                for (int x = r.x; x < r.xMax; x++) m_Doc.Clear(x, y);
            }
        }

        static Texture2D BuildClipTexture(MapClip clip)
        {
            var tex = new Texture2D(clip.Width, clip.Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[clip.Width * clip.Height];
            for (int y = 0; y < clip.Height; y++)
            {
                for (int x = 0; x < clip.Width; x++)
                {
                    clip.Get(x, y, out TileId t, out _, out LiquidType l);
                    Color32 c = t != TileId.Air ? TileColor(t) : l == LiquidType.Lava ? s_Lava : l == LiquidType.Nectar ? s_Nectar : new Color32(0, 0, 0, 60);
                    px[(clip.Height - 1 - y) * clip.Width + x] = c;
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false);
            return tex;
        }

        void BeginPaste()
        {
            if (m_Clip == null) return;

            m_Tool = Tool.Select;
            m_Pasting = true;
        }

        void FlipClip()
        {
            if (m_Clip != null) SetClip(m_Clip.Flipped());
        }

        void ClearSelection()
        {
            if (!m_Selection.HasValue) return;

            Undo.RegisterCompleteObjectUndo(m_Doc, "Clear selection");
            RectInt s = m_Selection.Value;
            for (int y = s.y; y < s.yMax; y++)
            {
                for (int x = s.x; x < s.xMax; x++)
                {
                    m_Doc.Clear(x, y);
                    if (m_Mirror) m_Doc.Clear(m_Doc.MirrorX(x), y);
                }
            }

            m_Dirty = true;
            m_TexDirty = true;
        }

        void FillSelection()
        {
            if (!m_Selection.HasValue) return;

            Undo.RegisterCompleteObjectUndo(m_Doc, "Fill selection");
            RectInt s = m_Selection.Value;
            for (int y = s.y; y < s.yMax; y++)
            {
                for (int x = s.x; x < s.xMax; x++)
                {
                    ApplyBrush(x, y, false, false);
                    if (m_Mirror) ApplyBrush(m_Doc.MirrorX(x), y, false, true);
                }
            }

            m_Dirty = true;
            m_TexDirty = true;
        }

        // ---- keyboard ----

        void HandleCommands(Event e)
        {
            if (e.type != EventType.ValidateCommand && e.type != EventType.ExecuteCommand) return;
            if (EditorGUIUtility.editingTextField) return;

            bool run = e.type == EventType.ExecuteCommand;
            switch (e.commandName)
            {
                case "Copy":
                    if (run) CopySelection(false);
                    e.Use();
                    break;
                case "Cut":
                    if (run) CopySelection(true);
                    e.Use();
                    break;
                case "Paste":
                    if (run) BeginPaste();
                    e.Use();
                    break;
                case "Delete":
                case "SoftDelete":
                    if (run) ClearSelection();
                    e.Use();
                    break;
                case "SelectAll":
                    if (run) m_Selection = new RectInt(0, 0, m_Doc.Width, m_Doc.Height);
                    e.Use();
                    break;
            }

            if (run) Repaint();
        }

        void HandleShortcuts(Event e)
        {
            if (EditorGUIUtility.editingTextField) return;

            // arrows: held to scroll the view; Alt+arrow nudges the selection instead
            if ((e.type == EventType.KeyDown || e.type == EventType.KeyUp) && !e.alt && !e.control && !e.command && IsArrow(e.keyCode))
            {
                bool down = e.type == EventType.KeyDown;
                switch (e.keyCode)
                {
                    case KeyCode.LeftArrow: m_HoldLeft = down; break;
                    case KeyCode.RightArrow: m_HoldRight = down; break;
                    case KeyCode.UpArrow: m_HoldUp = down; break;
                    case KeyCode.DownArrow: m_HoldDown = down; break;
                }

                m_HoldFast = e.shift;
                m_LastUpdate = EditorApplication.timeSinceStartup;
                e.Use();
                return;
            }

            if (e.type != EventType.KeyDown) return;

            bool cmd = e.control || e.command;
            if (cmd && e.keyCode == KeyCode.S)
            {
                if (string.IsNullOrEmpty(m_Path)) SaveAs(); else Save(m_Path);
                e.Use();
                return;
            }

            if (cmd) return;   // leave other modified keys (undo, copy...) to the editor

            switch (e.keyCode)
            {
                case KeyCode.B: SetTool(Tool.Paint); break;
                case KeyCode.E: SetTool(Tool.Erase); break;
                case KeyCode.S: SetTool(Tool.Select); break;
                case KeyCode.Z: SetTool(Tool.Zone); break;
                case KeyCode.I: SetTool(Tool.Pick); break;
                case KeyCode.M: m_Mirror = !m_Mirror; break;
                case KeyCode.F: FlipClip(); break;
                case KeyCode.LeftBracket: m_BrushSize = Mathf.Max(1, m_BrushSize - 1); break;
                case KeyCode.RightBracket: m_BrushSize = Mathf.Min(16, m_BrushSize + 1); break;
                case KeyCode.Home: FitView(); break;
                case KeyCode.LeftArrow when e.alt: NudgeSelection(e.shift ? -10 : -1, 0); break;
                case KeyCode.RightArrow when e.alt: NudgeSelection(e.shift ? 10 : 1, 0); break;
                case KeyCode.UpArrow when e.alt: NudgeSelection(0, e.shift ? -10 : -1); break;
                case KeyCode.DownArrow when e.alt: NudgeSelection(0, e.shift ? 10 : 1); break;
                case KeyCode.Delete:
                case KeyCode.Backspace: ClearSelection(); break;
                case KeyCode.Escape:
                    if (m_Moving) FinishMove(m_MoveFrom.position);
                    else if (m_Pasting) m_Pasting = false;
                    else m_Selection = null;
                    break;
                default: return;
            }

            e.Use();
            Repaint();
        }

        static bool IsArrow(KeyCode k) => k is KeyCode.LeftArrow or KeyCode.RightArrow or KeyCode.UpArrow or KeyCode.DownArrow;

        // =========================================================================================================
        // colours and names
        // =========================================================================================================

        static Color32 TileColor(TileId id) => id switch
        {
            TileId.Air => s_Background,
            _ => TileDefs.Get(id).Color,
        };

        static string TileName(TileId id) => id == TileId.Air ? "Air" : TileDefs.Get(id).Name;

        static Color LiquidColor(LiquidType l) => l switch
        {
            LiquidType.Lava => s_Lava,
            LiquidType.Nectar => s_Nectar,
            _ => new Color(0.2f, 0.22f, 0.3f),
        };

        static Color ZoneColor(string kind)
        {
            if (kind.StartsWith("Spawn")) return kind.EndsWith("Blue") ? new Color(0.35f, 0.65f, 1f) : new Color(1f, 0.4f, 0.4f);
            if (kind.StartsWith("MobSpawn")) return kind.EndsWith("Blue") ? new Color(0.3f, 0.95f, 0.6f) : new Color(0.95f, 0.85f, 0.3f);
            if (kind.StartsWith("MobMid")) return kind.EndsWith("Blue") ? new Color(0.2f, 0.9f, 0.9f) : new Color(1f, 0.6f, 0.2f);
            if (kind == "AltarMid") return new Color(1f, 0.85f, 0.2f);
            if (kind == "BossMid") return new Color(0.85f, 0.4f, 1f);
            if (kind.StartsWith("Altar")) return new Color(1f, 0.95f, 0.6f);
            if (kind.EndsWith("Gem")) return new Color(0.7f, 0.9f, 1f);
            return new Color(0.6f, 0.6f, 0.65f);
        }

        static string ZoneHelp(string kind) => kind switch
        {
            "SpawnBlue" or "SpawnRed" => "Spawn room interior: players spawn on its floor, a few tiles in from the back wall, and regenerate faster inside.",
            "MobSpawnBlue" or "MobSpawnRed" => "Base mob area: the side Drakon spawns at a random open spot inside.",
            "MobMidBlue" or "MobMidRed" => "Mid mob area for this side: the mid Drakon spawns at a random open spot inside.",
            "AltarMid" => "Mid altar: stands on the tile under the zone's bottom centre (outlined).",
            "BossMid" => "Mid-boss spot: the Elder Drakon stands on the tile under the zone's bottom centre (outlined).",
            "AltarBlue" or "AltarRed" => "Base altar: stands on the tile under the zone's bottom centre.",
            "BlueGem" or "RedGem" => "Gem hitbox (6 × 9 tiles).",
            _ => "No-build area: players can't place blocks inside. Several are allowed.",
        };
    }
}
