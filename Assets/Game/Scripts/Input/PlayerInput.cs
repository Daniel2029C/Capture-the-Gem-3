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
        /// <summary>Down held: drop through glass platforms.</summary>
        public bool Down;
        public bool UseItem;
        /// <summary>Secondary use, e.g. a bow's second arrow.</summary>
        public bool UseAlt;
        /// <summary>Fire the grappling hook.</summary>
        public bool HookFire;
        /// <summary>Ability keys pressed this tick: bit 0 = ability 1, bit 1 = ability 2, bit 2 = ability 3.</summary>
        public int AbilityPressed;
        /// <summary>Ability keys held this tick, same bits.</summary>
        public int AbilityHeld;
        /// <summary>Spell keys pressed this tick: bit 0 = spell 1, bit 1 = spell 2.</summary>
        public int SpellPressed;
        /// <summary>The shop key was pressed this tick.</summary>
        public bool ShopPressed;
        /// <summary>Hotbar slot chosen by key this tick (0-9), or -1.</summary>
        public int HotbarKey;
        /// <summary>Net scroll notches since the last tick; positive = scroll up.</summary>
        public int HotbarScroll;
        /// <summary>Cursor position in world units.</summary>
        public Vector2 CursorWorld;
        /// <summary>Testing: end the match immediately.</summary>
        public bool DevEndMatch;
        /// <summary>Testing: jump the match clock to the 10:00 shift.</summary>
        public bool DevSkipOvertime;
    }

    /// <summary>
    /// Reads the keyboard and mouse every rendered frame through <see cref="KeyBindings"/> and hands one
    /// <see cref="InputFrame"/> to each simulation tick. Presses are latched between ticks so a tap shorter than a
    /// tick is never lost. This is the seam a network layer would replace: ticks consume frames, not devices.
    /// </summary>
    public class PlayerInput : MonoBehaviour
    {
        [SerializeField] Camera worldCamera;
        [Tooltip("Enables testing keys (end the match now, skip to overtime).")]
        [SerializeField] bool devKeys = true;

        uint m_Latched;
        float m_ScrollAccumulated;

        public void SetCamera(Camera cam) => worldCamera = cam;

        static uint Bit(BindAction action) => 1u << (int)action;

        void Update()
        {
            foreach (BindAction action in KeyBindings.Actions)
            {
                if ((action == BindAction.DevEndMatch || action == BindAction.DevSkipOvertime) && !devKeys) continue;
                if (KeyBindings.WasPressedThisFrame(action)) m_Latched |= Bit(action);
            }

            Mouse mouse = Mouse.current;
            if (mouse != null) m_ScrollAccumulated += mouse.scroll.ReadValue().y;
        }

        bool Pressed(BindAction action) => (m_Latched & Bit(action)) != 0;

        /// <summary>Held, or tapped since the last tick (so a short tap still counts for a whole tick).</summary>
        bool Held(BindAction action) => KeyBindings.IsPressed(action) || Pressed(action);

        public InputFrame Consume()
        {
            var frame = new InputFrame
            {
                Left = KeyBindings.IsPressed(BindAction.MoveLeft),
                Right = KeyBindings.IsPressed(BindAction.MoveRight),
                Down = KeyBindings.IsPressed(BindAction.Down),
                Jump = Held(BindAction.Jump),
                UseItem = Held(BindAction.UseItem),
                UseAlt = Held(BindAction.UseAlt),
                HookFire = Pressed(BindAction.Hook),
                ShopPressed = Pressed(BindAction.Shop),
                DevEndMatch = Pressed(BindAction.DevEndMatch),
                DevSkipOvertime = Pressed(BindAction.DevSkipOvertime),
                HotbarKey = -1,
            };

            for (int i = 0; i < 3; i++)
            {
                BindAction action = BindAction.Ability1 + i;
                if (Pressed(action)) frame.AbilityPressed |= 1 << i;
                if (Held(action)) frame.AbilityHeld |= 1 << i;
            }

            for (int i = 0; i < 2; i++)
            {
                if (Pressed(BindAction.Spell1 + i)) frame.SpellPressed |= 1 << i;
            }

            for (int i = 0; i < 10; i++)
            {
                if (Pressed(BindAction.Hotbar1 + i)) frame.HotbarKey = i;
            }

            Mouse mouse = Mouse.current;
            if (mouse != null && worldCamera != null)
            {
                Vector2 screen = mouse.position.ReadValue();
                frame.CursorWorld = worldCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));
            }

            // Platforms report scroll in different scales (often 120 per notch on Windows, 1 on macOS);
            // only the direction matters for the hotbar.
            if (Mathf.Abs(m_ScrollAccumulated) > 0.01f)
            {
                frame.HotbarScroll = m_ScrollAccumulated > 0f ? 1 : -1;
            }

            m_Latched = 0;
            m_ScrollAccumulated = 0f;
            return frame;
        }
    }
}
