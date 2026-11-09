using CTG.Core;
using CTG.Gameplay;
using UnityEngine;

namespace CTG.Rendering
{
    public enum ViewScaling
    {
        /// <summary>
        /// Always show the same world height as the reference resolution, whatever the real resolution.
        /// Fair for competitive play: nobody sees further by running a bigger monitor.
        /// </summary>
        FixedReferenceHeight,
        /// <summary>
        /// Terraria's own behaviour: one world pixel per screen pixel at zoom 1, so higher resolutions see more.
        /// </summary>
        PixelPerfect,
    }

    /// <summary>
    /// Terraria-style camera: locked to the player's centre (no smoothing) and sized so that at 1920×1080
    /// and zoom 1 it shows 1080 / 16 = 67.5 tiles of height, like Terraria at 100% zoom. Resolution and
    /// zoom can change at runtime through <see cref="DisplaySettings"/>; the size is recomputed whenever the
    /// screen size or settings change.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(100)] // after the player has written its interpolated position
    public class CameraView : MonoBehaviour
    {
        Camera m_Camera;
        PlayerCharacter m_Target;
        int m_LastScreenHeight;
        int m_LastSettingsVersion = -1;

        Vector2 m_WorldSizeTiles;

        /// <param name="worldSizeTiles">World size; the view is kept inside it, as Terraria does at map edges.</param>
        public void Bind(PlayerCharacter target, Vector2 worldSizeTiles)
        {
            m_Camera = GetComponent<Camera>();
            m_Camera.orthographic = true;
            m_Target = target;
            m_WorldSizeTiles = worldSizeTiles;
            Refresh();
        }

        void Refresh()
        {
            DisplaySettings settings = DisplaySettings.Current;
            float zoom = Mathf.Max(0.1f, settings.Zoom);
            float pixelHeight = settings.Scaling == ViewScaling.PixelPerfect
                ? Screen.height
                : settings.ReferenceHeight;

            float visibleTiles = pixelHeight / TerrariaUnits.PixelsPerTile / zoom;
            m_Camera.orthographicSize = visibleTiles * 0.5f;

            m_LastScreenHeight = Screen.height;
            m_LastSettingsVersion = DisplaySettings.Version;
        }

        void LateUpdate()
        {
            if (m_Camera == null) return;

            if (Screen.height != m_LastScreenHeight || DisplaySettings.Version != m_LastSettingsVersion)
            {
                Refresh();
            }

            if (m_Target == null || m_Target.Motor == null) return;

            // The player's interpolated transform sits at the hitbox's bottom-centre.
            Vector3 feet = m_Target.transform.position;
            float halfHeight = m_Target.Motor.Height * 0.5f / TerrariaUnits.PixelsPerTile;
            Vector2 focus = new Vector2(feet.x, feet.y + halfHeight);
            transform.position = new Vector3(ClampAxis(focus.x, m_Camera.orthographicSize * m_Camera.aspect, m_WorldSizeTiles.x),
                                             ClampAxis(focus.y, m_Camera.orthographicSize, m_WorldSizeTiles.y),
                                             transform.position.z);
        }

        static float ClampAxis(float value, float halfExtent, float worldSize)
        {
            // A world smaller than the view on this axis is centred instead.
            if (worldSize <= halfExtent * 2f) return worldSize * 0.5f;
            return Mathf.Clamp(value, halfExtent, worldSize - halfExtent);
        }
    }

    /// <summary>
    /// Display options: resolution, window mode, zoom and view scaling. A settings menu calls
    /// <see cref="Apply"/>; the camera picks the change up on its next frame.
    /// </summary>
    public class DisplaySettings
    {
        public int Width = 1920;
        public int Height = 1080;
        public FullScreenMode WindowMode = FullScreenMode.FullScreenWindow;
        /// <summary>1 = Terraria's 100% zoom. Terraria allows 100%-200%.</summary>
        public float Zoom = 1f;
        public ViewScaling Scaling = ViewScaling.FixedReferenceHeight;
        /// <summary>Screen height whose view FixedReferenceHeight reproduces at any resolution.</summary>
        public int ReferenceHeight = 1080;

        public static DisplaySettings Current { get; private set; } = new();
        public static int Version { get; private set; }

        public static void Apply(DisplaySettings settings, bool changeResolution)
        {
            Current = settings;
            Version++;

            if (changeResolution && !Application.isEditor)
            {
                Screen.SetResolution(settings.Width, settings.Height, settings.WindowMode);
            }
        }
    }
}
