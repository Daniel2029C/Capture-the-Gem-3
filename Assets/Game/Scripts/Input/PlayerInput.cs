using UnityEngine;
using UnityEngine.InputSystem;

namespace CTG.Input
{
    /// <summary>One tick's worth of player controls.</summary>
    public struct InputFrame
    {
        public bool Left;
        public bool Right;
        public bool Jump;
        /// <summary>Down held (S / down arrow): drop through glass platforms.</summary>
        public bool Down;
        public bool UseItem;
        /// <summary>Secondary use (right click), e.g. a bow's second arrow.</summary>
        public bool UseAlt;
        /// <summary>Fire the grappling hook (right mouse press for now).</summary>
        public bool HookFire;
        /// <summary>Ability keys pressed this tick: bit 0 = ability 1 (Left Shift), bit 1 = ability 2 (E), bit 2 = ability 3 (C).</summary>
        public int AbilityPressed;
        /// <summary>Ability keys held this tick, same bits.</summary>
        public int AbilityHeld;
        /// <summary>Hotbar slot chosen by number key this tick (0-9), or -1.</summary>
        public int HotbarKey;
        /// <summary>Net scroll notches since the last tick; positive = scroll up.</summary>
        public int HotbarScroll;
        /// <summary>Cursor position in world units.</summary>
        public Vector2 CursorWorld;
        /// <summary>Drink the healing potion (H).</summary>
        public bool UsePotion;
        /// <summary>Testing: end the match immediately (K).</summary>
        public bool DevEndMatch;
    }

    /// <summary>
    /// Reads keyboard and mouse every rendered frame and hands one <see cref="InputFrame"/> to each
    /// simulation tick. Presses are latched between ticks so a tap shorter than a tick is never lost.
    /// This is the seam a network layer would replace: ticks consume frames, not devices.
    /// </summary>
    public class PlayerInput : MonoBehaviour
    {
        [SerializeField] Camera worldCamera;
        [Tooltip("Enables testing keys: K = end the match now.")]
        [SerializeField] bool devKeys = true;

        bool m_JumpLatched;
        bool m_UseLatched;
        bool m_AltLatched;
        bool m_HookLatched;
        int m_AbilityLatched;
        int m_HotbarKeyLatched = -1;
        bool m_PotionLatched;
        bool m_DevEndLatched;
        float m_ScrollAccumulated;

        public void SetCamera(Camera cam) => worldCamera = cam;

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;

            if (keyboard != null)
            {
                if (keyboard.spaceKey.wasPressedThisFrame) m_JumpLatched = true;
                if (keyboard.hKey.wasPressedThisFrame) m_PotionLatched = true;
                if (keyboard.leftShiftKey.wasPressedThisFrame) m_AbilityLatched |= 1;
                if (keyboard.eKey.wasPressedThisFrame) m_AbilityLatched |= 2;
                if (keyboard.cKey.wasPressedThisFrame) m_AbilityLatched |= 4;

                if (devKeys)
                {
                    if (keyboard.kKey.wasPressedThisFrame) m_DevEndLatched = true;
                }

                for (int i = 0; i < 10; i++)
                {
                    // Keys 1..9 then 0 map to slots 0..9.
                    Key key = i == 9 ? Key.Digit0 : Key.Digit1 + i;
                    if (keyboard[key].wasPressedThisFrame) m_HotbarKeyLatched = i;
                }
            }

            if (mouse != null)
            {
                if (mouse.leftButton.wasPressedThisFrame) m_UseLatched = true;
                if (mouse.rightButton.wasPressedThisFrame)
                {
                    m_AltLatched = true;
                    m_HookLatched = true;
                }
                m_ScrollAccumulated += mouse.scroll.ReadValue().y;
            }
        }

        public InputFrame Consume()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;

            var frame = new InputFrame
            {
                HookFire = m_HookLatched, AbilityPressed = m_AbilityLatched, AbilityHeld = m_AbilityLatched, HotbarKey = m_HotbarKeyLatched, UsePotion = m_PotionLatched, DevEndMatch = m_DevEndLatched,
            };

            if (keyboard != null)
            {
                frame.Left = keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed;
                frame.Right = keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed;
                frame.Jump = keyboard.spaceKey.isPressed || m_JumpLatched;
                frame.Down = keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed;
                if (keyboard.leftShiftKey.isPressed) frame.AbilityHeld |= 1;
                if (keyboard.eKey.isPressed) frame.AbilityHeld |= 2;
                if (keyboard.cKey.isPressed) frame.AbilityHeld |= 4;
            }

            if (mouse != null)
            {
                frame.UseItem = mouse.leftButton.isPressed || m_UseLatched;
                frame.UseAlt = mouse.rightButton.isPressed || m_AltLatched;

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
            m_AltLatched = false;
            m_HookLatched = false;
            m_AbilityLatched = 0;
            m_HotbarKeyLatched = -1;
            m_PotionLatched = false;
            m_DevEndLatched = false;
            m_ScrollAccumulated = 0f;
            return frame;
        }
    }
}
