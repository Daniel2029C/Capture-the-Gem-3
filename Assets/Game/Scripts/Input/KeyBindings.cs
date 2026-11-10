using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CTG.Input
{
    /// <summary>Every control the player can rebind.</summary>
    public enum BindAction
    {
        MoveLeft,
        MoveRight,
        Jump,
        Down,
        UseItem,
        UseAlt,
        Hook,
        Ability1,
        Ability2,
        Ability3,
        Spell1,
        Spell2,
        Shop,
        Hotbar1, Hotbar2, Hotbar3, Hotbar4, Hotbar5, Hotbar6, Hotbar7, Hotbar8, Hotbar9, Hotbar10,
        DevEndMatch,
    }

    /// <summary>A keyboard key or a mouse button.</summary>
    public readonly struct Binding : IEquatable<Binding>
    {
        public readonly bool IsMouse;
        /// <summary>A <see cref="Key"/> value for the keyboard; 0 left, 1 right, 2 middle for the mouse.</summary>
        public readonly int Code;

        public Binding(bool isMouse, int code)
        {
            IsMouse = isMouse;
            Code = code;
        }

        public static Binding OfKey(Key key) => new(false, (int)key);
        public static Binding OfMouse(int button) => new(true, button);

        public bool Equals(Binding other) => IsMouse == other.IsMouse && Code == other.Code;
        public override bool Equals(object obj) => obj is Binding other && Equals(other);
        public override int GetHashCode() => IsMouse ? 1000 + Code : Code;

        public string Serialize() => (IsMouse ? "m:" : "k:") + Code;

        public static bool TryParse(string text, out Binding binding)
        {
            binding = default;
            if (string.IsNullOrEmpty(text) || text.Length < 3) return false;
            if (!int.TryParse(text.Substring(2), out int code)) return false;

            if (text.StartsWith("m:")) binding = OfMouse(code);
            else if (text.StartsWith("k:")) binding = OfKey((Key)code);
            else return false;
            return true;
        }

        public string Label
        {
            get
            {
                if (IsMouse) return Code switch { 0 => "Mouse 1 (left)", 1 => "Mouse 2 (right)", 2 => "Mouse 3 (middle)", _ => $"Mouse {Code + 1}" };

                var key = (Key)Code;
                return key switch
                {
                    Key.LeftShift => "Left Shift",
                    Key.RightShift => "Right Shift",
                    Key.LeftCtrl => "Left Ctrl",
                    Key.RightCtrl => "Right Ctrl",
                    Key.LeftAlt => "Left Alt",
                    Key.RightAlt => "Right Alt",
                    Key.Space => "Space",
                    Key.Digit0 => "0", Key.Digit1 => "1", Key.Digit2 => "2", Key.Digit3 => "3", Key.Digit4 => "4",
                    Key.Digit5 => "5", Key.Digit6 => "6", Key.Digit7 => "7", Key.Digit8 => "8", Key.Digit9 => "9",
                    _ => key.ToString(),
                };
            }
        }
    }

    /// <summary>
    /// The player's key bindings: defaults, saved choices (PlayerPrefs) and queries. Left, Right and Down also
    /// respond to the arrow keys as fixed alternates. Rebinding a control to a key another control already uses
    /// swaps the two, so no key ever ends up doing two things.
    /// </summary>
    public static class KeyBindings
    {
        const string k_PrefPrefix = "ctg.bind.";

        static readonly Dictionary<BindAction, Binding> s_Defaults = new()
        {
            { BindAction.MoveLeft, Binding.OfKey(Key.A) },
            { BindAction.MoveRight, Binding.OfKey(Key.D) },
            { BindAction.Jump, Binding.OfKey(Key.Space) },
            { BindAction.Down, Binding.OfKey(Key.S) },
            { BindAction.UseItem, Binding.OfMouse(0) },
            { BindAction.UseAlt, Binding.OfMouse(1) },
            { BindAction.Hook, Binding.OfMouse(1) },
            { BindAction.Ability1, Binding.OfKey(Key.LeftShift) },
            { BindAction.Ability2, Binding.OfKey(Key.E) },
            { BindAction.Ability3, Binding.OfKey(Key.C) },
            { BindAction.Spell1, Binding.OfKey(Key.LeftCtrl) },
            { BindAction.Spell2, Binding.OfKey(Key.V) },
            { BindAction.Shop, Binding.OfKey(Key.X) },
            { BindAction.Hotbar1, Binding.OfKey(Key.Digit1) },
            { BindAction.Hotbar2, Binding.OfKey(Key.Digit2) },
            { BindAction.Hotbar3, Binding.OfKey(Key.Digit3) },
            { BindAction.Hotbar4, Binding.OfKey(Key.Digit4) },
            { BindAction.Hotbar5, Binding.OfKey(Key.Digit5) },
            { BindAction.Hotbar6, Binding.OfKey(Key.Digit6) },
            { BindAction.Hotbar7, Binding.OfKey(Key.Digit7) },
            { BindAction.Hotbar8, Binding.OfKey(Key.Digit8) },
            { BindAction.Hotbar9, Binding.OfKey(Key.Digit9) },
            { BindAction.Hotbar10, Binding.OfKey(Key.Digit0) },
            { BindAction.DevEndMatch, Binding.OfKey(Key.K) },
        };

        static readonly Dictionary<BindAction, Binding> s_Current = new();
        static bool s_Loaded;

        public static event Action Changed;

        public static IEnumerable<BindAction> Actions => s_Defaults.Keys;

        public static string DisplayName(BindAction action) => action switch
        {
            BindAction.MoveLeft => "Move left",
            BindAction.MoveRight => "Move right",
            BindAction.Jump => "Jump / unhook",
            BindAction.Down => "Drop through platform",
            BindAction.UseItem => "Use item",
            BindAction.UseAlt => "Alternate use",
            BindAction.Hook => "Fire hook",
            BindAction.Ability1 => "Ability 1",
            BindAction.Ability2 => "Ability 2",
            BindAction.Ability3 => "Ability 3",
            BindAction.Spell1 => "Spell 1",
            BindAction.Spell2 => "Spell 2",
            BindAction.Shop => "Shop",
            BindAction.DevEndMatch => "End match (dev)",
            _ => $"Hotbar {(int)action - (int)BindAction.Hotbar1 + 1}",
        };

        static void EnsureLoaded()
        {
            if (s_Loaded) return;

            s_Loaded = true;
            s_Current.Clear();
            foreach (KeyValuePair<BindAction, Binding> entry in s_Defaults)
            {
                Binding binding = entry.Value;
                try
                {
                    if (Binding.TryParse(PlayerPrefs.GetString(k_PrefPrefix + entry.Key, ""), out Binding saved)) binding = saved;
                }
                catch (Exception)
                {
                    // Preferences unavailable: use the default.
                }

                s_Current[entry.Key] = binding;
            }
        }

        public static Binding Get(BindAction action)
        {
            EnsureLoaded();
            return s_Current[action];
        }

        public static string Label(BindAction action) => Get(action).Label;

        /// <summary>Binds a control; another control using the same input takes this control's old input.</summary>
        public static void Set(BindAction action, Binding binding)
        {
            EnsureLoaded();
            Binding old = s_Current[action];
            foreach (BindAction other in new List<BindAction>(s_Current.Keys))
            {
                // Fire hook and alternate use may share a button; anything else swaps.
                if (other == action || !s_Current[other].Equals(binding)) continue;
                if (SharesAllowed(action, other)) continue;

                s_Current[other] = old;
            }

            s_Current[action] = binding;
            Save();
            Changed?.Invoke();
        }

        static bool SharesAllowed(BindAction a, BindAction b) =>
            (a == BindAction.Hook && b == BindAction.UseAlt) || (a == BindAction.UseAlt && b == BindAction.Hook);

        public static void ResetToDefaults()
        {
            EnsureLoaded();
            foreach (KeyValuePair<BindAction, Binding> entry in s_Defaults) s_Current[entry.Key] = entry.Value;
            Save();
            Changed?.Invoke();
        }

        static void Save()
        {
            try
            {
                foreach (KeyValuePair<BindAction, Binding> entry in s_Current) PlayerPrefs.SetString(k_PrefPrefix + entry.Key, entry.Value.Serialize());
                PlayerPrefs.Save();
            }
            catch (Exception)
            {
                // Preferences unavailable: bindings last for this session only.
            }
        }

        // ---- Queries ----------------------------------------------------------------------------------

        public static bool WasPressedThisFrame(BindAction action) => Query(action, true);
        public static bool IsPressed(BindAction action) => Query(action, false);

        static bool Query(BindAction action, bool pressedEdge)
        {
            Binding binding = Get(action);
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;

            if (binding.IsMouse)
            {
                if (mouse == null) return false;
                var button = binding.Code switch { 0 => mouse.leftButton, 1 => mouse.rightButton, 2 => mouse.middleButton, _ => null };
                if (button == null) return false;
                return pressedEdge ? button.wasPressedThisFrame : button.isPressed;
            }

            if (keyboard == null) return false;
            var key = keyboard[(Key)binding.Code];
            bool result = pressedEdge ? key.wasPressedThisFrame : key.isPressed;

            // Arrow keys always work as alternates for movement.
            Key alternate = action switch
            {
                BindAction.MoveLeft => Key.LeftArrow,
                BindAction.MoveRight => Key.RightArrow,
                BindAction.Down => Key.DownArrow,
                _ => Key.None,
            };
            if (!result && alternate != Key.None)
            {
                var alt = keyboard[alternate];
                result = pressedEdge ? alt.wasPressedThisFrame : alt.isPressed;
            }

            return result;
        }
    }

    /// <summary>Menus ask the game to ignore the controls while they are open.</summary>
    public static class InputBlock
    {
        /// <summary>No controls at all (the main menu is open); movement stops too.</summary>
        public static bool All;
        /// <summary>No actions (item use, abilities, hook, spells), but the player can still move: the shop is open.</summary>
        public static bool Actions;
    }
}
