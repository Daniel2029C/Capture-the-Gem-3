using CTG.Combat;
using CTG.Core;
using CTG.Gameplay;
using UnityEngine;

namespace CTG.Rendering
{
    /// <summary>Draws each gem's barrier as a translucent shield over the gem with its remaining life above it.</summary>
    public sealed class BarrierView : MonoBehaviour
    {
        GemMatch m_Match;
        SpriteRenderer m_Blue, m_Red;
        GUIStyle m_Label;

        public static BarrierView Create(Transform parent, GemMatch match)
        {
            var go = new GameObject("Barrier View");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<BarrierView>();
            view.m_Match = match;
            view.m_Blue = view.Make(match.BlueGem);
            view.m_Red = view.Make(match.RedGem);
            return view;
        }

        SpriteRenderer Make(Gem gem)
        {
            var go = new GameObject($"{gem.Team} Barrier");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderSprites.Solid("barrier_fill", Color.white, 16, 16);
            sr.sortingOrder = 3;
            Rect box = gem.HitboxPx;
            go.transform.position = box.center / SimUnits.PixelsPerTile;
            go.transform.localScale = new Vector3(box.width, box.height, 1f) / SimUnits.PixelsPerTile;
            return sr;
        }

        void LateUpdate()
        {
            Refresh(m_Blue, m_Match.BlueGem);
            Refresh(m_Red, m_Match.RedGem);
        }

        static void Refresh(SpriteRenderer sr, Gem gem)
        {
            bool up = gem.Barrier.IsUp;
            sr.enabled = up;
            if (!up) return;

            Color team = TeamColors.For(gem.Team);
            float health = gem.Barrier.Health / (float)GemBarrier.MaxHealth;
            float shimmer = 0.5f + 0.5f * Mathf.Sin(Time.time * 2.5f);
            sr.color = new Color(Mathf.Lerp(team.r, 0.6f, 0.5f), Mathf.Lerp(team.g, 0.9f, 0.5f), 1f, 0.12f + 0.1f * shimmer + 0.08f * health);
        }

        void OnGUI()
        {
            if (m_Match == null) return;

            Camera cam = Camera.main;
            if (cam == null) return;

            if (m_Label == null)
            {
                m_Label = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(0.75f, 0.95f, 1f) },
                };
            }

            DrawLabel(cam, m_Match.BlueGem);
            DrawLabel(cam, m_Match.RedGem);
        }

        void DrawLabel(Camera cam, Gem gem)
        {
            if (!gem.Barrier.IsUp) return;

            Vector3 screen = cam.WorldToScreenPoint(new Vector3(gem.HitboxPx.center.x, gem.HitboxPx.yMax, 0f) / SimUnits.PixelsPerTile + new Vector3(0f, 0.8f, 0f));
            if (screen.z < 0f) return;

            GUI.Label(new Rect(screen.x - 90f, Screen.height - screen.y - 12f, 180f, 24f), $"Barrier {gem.Barrier.Health}/{GemBarrier.MaxHealth}", m_Label);
        }
    }
}
