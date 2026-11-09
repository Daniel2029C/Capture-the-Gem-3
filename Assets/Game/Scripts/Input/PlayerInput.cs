using UnityEngine;
using UnityEngine.InputSystem;

namespace CTG.Input
{
    /// <summary>One tick's worth of player controls, mirroring Terraria's control* flags.</summary>
    public struct InputFrame
    {
        public bool Left;
        public bool Right;
        public bool Jump;
        public bool UseItem;
        /// <summary>Hotbar slot chosen by number key this tick (0-9), or -1.</summary>
        public int HotbarKey;
        /// <summary>Net scroll notches since the last tick; positive = scroll up.</summary>
        public int HotbarScroll;
        /// <summary>Cursor position in world units.</summary>
        public Vector2 CursorWorld;
    }

    /// <summary>
    /// Reads keyboard and mouse every rendered frame and hands one <see cref="InputFrame"/> to each
    /// simulation tick. Presses are latched between ticks so a tap shorter than a tick is never lost.
    /// This is the seam a network layer would replace: ticks consume frames, not devices.
    /// </summary>
    public class PlayerInput : MonoBehaviour
    {
        [SerializeField] Camera worldCamera;

        bool m_JumpLatched;
        bool m_UseLatched;
        int m_HotbarKeyLatched = -1;
        float m_ScrollAccumulated;

        public void SetCamera(Camera cam) => worldCamera = cam;

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;

            if (keyboard != null)
            {
                if (keyboard.spaceKey.wasPressedThisFrame) m_JumpLatched = true;

                for (int i = 0; i < 10; i++)
                {
                    // Terraria layout: keys 1..9 then 0 map to slots 0..9.
                    Key key = i == 9 ? Key.Digit0 : Key.Digit1 + i;
                    if (keyboard[key].wasPressedThisFrame) m_HotbarKeyLatched = i;
                }
            }

            if (mouse != null)
            {
                if (mouse.leftButton.wasPressedThisFrame) m_UseLatched = true;
                m_ScrollAccumulated += mouse.scroll.ReadValue().y;
            }
        }

        public InputFrame Consume()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;

            var frame = new InputFrame { HotbarKey = m_HotbarKeyLatched };

            if (keyboard != null)
            {
                frame.Left = keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed;
                frame.Right = keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed;
                frame.Jump = keyboard.spaceKey.isPressed || m_JumpLatched;
            }

            if (mouse != null)
            {
                frame.UseItem = mouse.leftButton.isPressed || m_UseLatched;

                if (worldCamera != null)
                {
                    Vector2 screen = mouse.position.ReadValue();
                    frame.CursorWorld = worldCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));
                }
            }

            // Platforms report scroll in different scales (often 120 per notch on Windows, 1 on macOS);
            // only the direction matters for the hotbar.
            if (Mathf.Abs(m_ScrollAccumulated) > 0.01f)
            {
                frame.HotbarScroll = m_ScrollAccumulated > 0f ? 1 : -1;
            }

            m_JumpLatched = false;
            m_UseLatched = false;
            m_HotbarKeyLatched = -1;
            m_ScrollAccumulated = 0f;
            return frame;
        }
    }
}
