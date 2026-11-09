using CTG.Combat;
using CTG.Core;
using CTG.Gameplay;
using UnityEngine;

namespace CTG.Rendering
{
    /// <summary>
    /// Draws each team's gem in its frame at the base, and a small copy above the head of whoever carries one.
    /// Placeholder art: a faceted diamond in the owner's colour.
    /// </summary>
    public class GemView : MonoBehaviour
    {
        GemMatch m_Match;
        Transform m_BlueBase, m_RedBase;
        Transform m_BlueCarried, m_RedCarried;
        Aura m_BlueAura, m_RedAura;

        /// <summary>A soft halo and a few drifting motes around a gem carrier.</summary>
        sealed class Aura
        {
            public Transform Root;
            public Transform Halo;
            public Transform[] Motes;
            public SpriteRenderer[] MoteRenderers;
        }

        public static GemView Create(Transform parent, GemMatch match)
        {
            var view = new GameObject("Gem View").AddComponent<GemView>();
            view.transform.SetParent(parent, false);
            view.m_Match = match;
            view.m_BlueBase = view.Make(match.BlueGem, 1, 1f);
            view.m_RedBase = view.Make(match.RedGem, 1, 1f);
            view.m_BlueCarried = view.Make(match.BlueGem, 30, 0.28f);
            view.m_RedCarried = view.Make(match.RedGem, 30, 0.28f);
            view.m_BlueAura = view.MakeAura(match.BlueGem.Team);
            view.m_RedAura = view.MakeAura(match.RedGem.Team);
            return view;
        }

        Transform Make(Gem gem, int order, float scale)
        {
            var go = new GameObject(gem.Team == Team.Blue ? "Blue Gem" : "Red Gem");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Diamond(gem.Team);
            sr.sortingOrder = order;
            go.transform.localScale = Vector3.one * scale;
            Vector2 centre = gem.HitboxPx.center / SimUnits.PixelsPerTile;
            go.transform.position = centre;
            return go.transform;
        }

        Aura MakeAura(Team team)
        {
            var aura = new Aura { Root = new GameObject(team == Team.Blue ? "Blue Gem Aura" : "Red Gem Aura").transform };
            aura.Root.SetParent(transform, false);

            var halo = new GameObject("Halo");
            halo.transform.SetParent(aura.Root, false);
            var haloRenderer = halo.AddComponent<SpriteRenderer>();
            haloRenderer.sprite = Halo(team);
            haloRenderer.sortingOrder = 29;
            aura.Halo = halo.transform;

            aura.Motes = new Transform[5];
            aura.MoteRenderers = new SpriteRenderer[5];
            for (int i = 0; i < aura.Motes.Length; i++)
            {
                var mote = new GameObject("Mote");
                mote.transform.SetParent(aura.Root, false);
                var sr = mote.AddComponent<SpriteRenderer>();
                sr.sprite = Mote(team);
                sr.sortingOrder = 29;
                aura.Motes[i] = mote.transform;
                aura.MoteRenderers[i] = sr;
            }

            aura.Root.gameObject.SetActive(false);
            return aura;
        }

        void LateUpdate()
        {
            Place(m_Match.BlueGem, m_BlueCarried, m_BlueAura);
            Place(m_Match.RedGem, m_RedCarried, m_RedAura);
            // The pedestal is empty while its gem is being carried.
            m_BlueBase.gameObject.SetActive(!m_Match.BlueGem.IsHeld);
            m_RedBase.gameObject.SetActive(!m_Match.RedGem.IsHeld);
        }

        static void Place(Gem gem, Transform carried, Aura aura)
        {
            bool show = gem.IsHeld && gem.Holder != null;
            carried.gameObject.SetActive(show);
            aura.Root.gameObject.SetActive(show);
            if (!show) return;

            float alpha = SimulationClock.Instance != null ? SimulationClock.Instance.Alpha : 1f;
            Vector2 px = gem.Holder.Motor.GetVisualPosition(alpha);
            carried.position = new Vector3((px.x + gem.Holder.Motor.Width * 0.5f) / SimUnits.PixelsPerTile,
                                           (px.y + gem.Holder.Motor.Height + 22f) / SimUnits.PixelsPerTile, 0f);

            // Aura: a gently pulsing halo on the player's body, with motes drifting around it.
            aura.Root.position = new Vector3((px.x + gem.Holder.Motor.Width * 0.5f) / SimUnits.PixelsPerTile,
                                             (px.y + gem.Holder.Motor.Height * 0.5f) / SimUnits.PixelsPerTile, 0f);
            float time = Time.time;
            aura.Halo.localScale = Vector3.one * (0.9f + 0.06f * Mathf.Sin(time * 3f));
            for (int i = 0; i < aura.Motes.Length; i++)
            {
                float angle = time * 1.4f + i * Mathf.PI * 2f / aura.Motes.Length;
                float wobble = Mathf.Sin(time * 2.2f + i * 1.7f);
                aura.Motes[i].localPosition = new Vector3(Mathf.Cos(angle) * (1.35f + 0.2f * wobble), Mathf.Sin(angle) * (1.9f + 0.2f * wobble), 0f);
                Color c = aura.MoteRenderers[i].color;
                c.a = 0.35f + 0.25f * wobble;
                aura.MoteRenderers[i].color = c;
            }
        }

        static readonly Sprite[] s_Halos = new Sprite[4];
        static readonly Sprite[] s_Motes = new Sprite[4];

        static Color AuraColor(Team team) => team == Team.Blue ? new Color(0.45f, 0.65f, 1f) : new Color(1f, 0.45f, 0.4f);

        /// <summary>A faint ring that fades out both inward and outward, 4 tiles across.</summary>
        static Sprite Halo(Team team)
        {
            int slot = (int)team;
            if (s_Halos[slot] != null) return s_Halos[slot];

            const int size = 64;
            Color colour = AuraColor(team);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = Mathf.Sqrt((x + 0.5f - size * 0.5f) * (x + 0.5f - size * 0.5f) + (y + 0.5f - size * 0.5f) * (y + 0.5f - size * 0.5f)) / (size * 0.5f);
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(r - 0.78f) / 0.22f);
                    float fill = Mathf.Clamp01(1f - r) * 0.18f;
                    Color c = colour;
                    c.a = (ring * ring * 0.28f + fill) * (r < 1f ? 1f : 0f);
                    tex.SetPixel(x, y, c);
                }
            }

            tex.Apply();
            s_Halos[slot] = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), SimUnits.PixelsPerTile);
            return s_Halos[slot];
        }

        /// <summary>A tiny soft diamond mote.</summary>
        static Sprite Mote(Team team)
        {
            int slot = (int)team;
            if (s_Motes[slot] != null) return s_Motes[slot];

            const int size = 7;
            Color colour = AuraColor(team);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = (Mathf.Abs(x - 3) + Mathf.Abs(y - 3)) / 3f;
                    Color c = Color.Lerp(Color.white, colour, d);
                    c.a = Mathf.Clamp01(1f - d);
                    tex.SetPixel(x, y, c);
                }
            }

            tex.Apply();
            s_Motes[slot] = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), SimUnits.PixelsPerTile);
            return s_Motes[slot];
        }

        static Sprite s_Blue, s_Red;

        /// <summary>A 5×7 tile faceted diamond with a darker outline.</summary>
        static Sprite Diamond(Team team)
        {
            ref Sprite cached = ref (team == Team.Blue ? ref s_Blue : ref s_Red);
            if (cached != null) return cached;

            const int w = 80, h = 112;
            Color baseColor = team == Team.Blue ? new Color(0.15f, 0.3f, 1f) : new Color(1f, 0.2f, 0.2f);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // Normalised distance in a diamond: 1 at the edge.
                    float dx = Mathf.Abs(x + 0.5f - w * 0.5f) / (w * 0.5f);
                    float dy = Mathf.Abs(y + 0.5f - h * 0.5f) / (h * 0.5f);
                    float d = dx + dy;
                    if (d > 1f) { tex.SetPixel(x, y, Color.clear); continue; }

                    float facet = (x + 0.5f < w * 0.5f ? 0.85f : 1.1f) * (y + 0.5f > h * 0.5f ? 1.1f : 0.9f);
                    Color c = baseColor * facet;
                    if (d > 0.88f) c = baseColor * 0.45f;                     // outline
                    else if (d < 0.18f) c = Color.Lerp(c, Color.white, 0.6f); // core highlight
                    c.a = 1f;
                    tex.SetPixel(x, y, c);
                }
            }

            tex.Apply();
            cached = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), SimUnits.PixelsPerTile);
            cached.name = $"gem_{team}";
            return cached;
        }
    }
}
