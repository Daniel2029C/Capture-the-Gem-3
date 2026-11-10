using CTG.Core;
using UnityEngine;

namespace CTG.Rendering
{
    /// <summary>
    /// Cave backdrop behind the world: two horizontally scrolling silhouette layers (far and near) that move at
    /// a fraction of the camera's speed, and a soft dark vignette over the whole view. Purely visual.
    /// </summary>
    [DefaultExecutionOrder(200)] // after the camera has moved
    public sealed class BackdropView : MonoBehaviour
    {
        sealed class Layer
        {
            public Transform Transform;
            public SpriteRenderer Renderer;
            public float Follow;      // 0 = fixed to the world, 1 = fixed to the camera
            public float TileWidth;   // world units
        }

        Camera m_Camera;
        readonly System.Collections.Generic.List<Layer> m_Layers = new();
        Transform m_Vignette;

        public static BackdropView Create(Camera camera)
        {
            var view = camera.gameObject.AddComponent<BackdropView>();
            view.m_Camera = camera;
            view.Build();
            return view;
        }

        void Build()
        {
            AddLayer("Backdrop Far", 0.82f, 11, new Color32(24, 31, 50, 255), 0.55f, 17, -60);
            AddLayer("Backdrop Near", 0.6f, 29, new Color32(19, 25, 41, 255), 0.4f, 31, -50);

            var vignette = new GameObject("Vignette");
            vignette.transform.SetParent(transform, false);
            vignette.transform.localPosition = new Vector3(0f, 0f, 5f);
            var renderer = vignette.AddComponent<SpriteRenderer>();
            renderer.sprite = BackdropArt.Vignette();
            renderer.sortingOrder = 1000;
            m_Vignette = vignette.transform;
        }

        void AddLayer(string name, float follow, int seed, Color32 color, float coverage, int featureSeed, int sortingOrder)
        {
            Sprite sprite = BackdropArt.Silhouette(name, seed, color, coverage, featureSeed);
            var go = new GameObject(name);
            go.transform.SetParent(null, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.drawMode = SpriteDrawMode.Tiled;
            renderer.sortingOrder = sortingOrder;
            m_Layers.Add(new Layer
            {
                Transform = go.transform,
                Renderer = renderer,
                Follow = follow,
                TileWidth = sprite.rect.width / SimUnits.PixelsPerTile,
            });
        }

        void OnDestroy()
        {
            foreach (Layer layer in m_Layers)
            {
                if (layer.Transform != null) Destroy(layer.Transform.gameObject);
            }
        }

        void LateUpdate()
        {
            if (m_Camera == null) return;

            float halfHeight = m_Camera.orthographicSize;
            float halfWidth = halfHeight * m_Camera.aspect;
            Vector3 cam = m_Camera.transform.position;

            foreach (Layer layer in m_Layers)
            {
                float tile = layer.TileWidth;
                float width = (Mathf.Ceil(halfWidth * 2f / tile) + 2f) * tile;
                float shift = Mathf.Round(cam.x * (1f - layer.Follow) / tile) * tile; // keep a whole number of tiles under the view
                float x = cam.x * layer.Follow + shift;
                float texHeight = layer.Renderer.sprite.rect.height / SimUnits.PixelsPerTile;
                float height = Mathf.Ceil(halfHeight * 2f / texHeight) * texHeight;
                layer.Renderer.size = new Vector2(width, height);
                layer.Transform.position = new Vector3(x, cam.y, 10f);
            }

            m_Vignette.localScale = new Vector3(halfWidth * 2f * 1.02f, halfHeight * 2f * 1.02f, 1f);
        }
    }
}
