using CTG.Core;
using CTG.Gameplay;
using UnityEngine;

namespace CTG.Rendering
{
    /// <summary>
    /// Draws the player's hooks and the chain of links from each hook back toward the player, interpolated
    /// between ticks. Links are laid every <see cref="k_LinkSpacingPx"/> from the hook until within
    /// <see cref="k_StopWithinPx"/> of the player.
    /// </summary>
    public class GrappleHookView : MonoBehaviour
    {
        const float k_LinkSpacingPx = 12f;
        const float k_StopWithinPx = 32f;
        const int k_LinkWidthPx = 4;
        const int k_HookSizePx = 12;

        static readonly Color s_LinkColor = new Color32(78, 120, 52, 255);
        static readonly Color s_HookColor = new Color32(120, 190, 70, 255);

        PlayerCharacter m_Player;
        readonly System.Collections.Generic.List<SpriteRenderer> m_Links = new();
        readonly System.Collections.Generic.List<SpriteRenderer> m_HookViews = new();

        public void Bind(PlayerCharacter player) => m_Player = player;

        void LateUpdate()
        {
            GrapplingHooks hooks = m_Player != null && m_Player.Motor != null ? m_Player.Hooks : null;
            int hookCount = hooks != null ? hooks.Count : 0;

            float alpha = SimulationClock.Instance != null ? SimulationClock.Instance.Alpha : 1f;
            PlayerMotor motor = m_Player != null ? m_Player.Motor : null;
            Vector2 playerCenter = motor != null
                ? motor.GetVisualPosition(alpha) + new Vector2(motor.Width * 0.5f, motor.Height * 0.5f)
                : Vector2.zero;

            int linkIndex = 0;
            for (int i = 0; i < hookCount; i++)
            {
                GrappleHook hook = hooks.Hooks[i];
                Vector2 center = Vector2.Lerp(hook.PreviousCenter, hook.Center, alpha);

                SpriteRenderer hookView = GetView(m_HookViews, i, 14);
                hookView.enabled = true;
                hookView.sprite = PlaceholderSprites.Solid("hook_head", s_HookColor, k_HookSizePx, k_HookSizePx);
                hookView.transform.position = center / SimUnits.PixelsPerTile;

                Vector2 toPlayer = playerCenter - center;
                float distance = toPlayer.magnitude;
                float angle = Mathf.Atan2(toPlayer.y, toPlayer.x) * Mathf.Rad2Deg - 90f;
                while (distance > k_StopWithinPx && distance < 5000f)
                {
                    center += toPlayer / distance * k_LinkSpacingPx;
                    toPlayer = playerCenter - center;
                    distance = toPlayer.magnitude;

                    SpriteRenderer link = GetView(m_Links, linkIndex++, 9);
                    link.enabled = true;
                    link.sprite = PlaceholderSprites.Solid("hook_link", s_LinkColor, k_LinkWidthPx, (int)k_LinkSpacingPx);
                    link.transform.position = center / SimUnits.PixelsPerTile;
                    link.transform.rotation = Quaternion.Euler(0f, 0f, angle);
                }
            }

            for (int i = hookCount; i < m_HookViews.Count; i++) m_HookViews[i].enabled = false;
            for (int i = linkIndex; i < m_Links.Count; i++) m_Links[i].enabled = false;
        }

        SpriteRenderer GetView(System.Collections.Generic.List<SpriteRenderer> views, int index, int sortingOrder)
        {
            while (views.Count <= index)
            {
                var go = new GameObject("Hook Part");
                go.transform.SetParent(transform, false);
                var view = go.AddComponent<SpriteRenderer>();
                view.sortingOrder = sortingOrder;
                views.Add(view);
            }

            return views[index];
        }
    }
}
