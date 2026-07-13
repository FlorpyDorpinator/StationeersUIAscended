using System.Collections.Generic;
using Assets.Scripts.Objects;
using UnityEngine;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// Per-session hotkeys bound to device SETTING wedges (#4): hover a setting in a radial,
    /// press a letter, and that key now toggles/presses that setting from normal gameplay.
    /// The binding holds the live Interactable + its Thing (session references, guarded on
    /// execute), so pressing the key runs the SAME MP-safe path a click on the wedge would —
    /// <see cref="ItemActions.PressInteractable(DynamicThing, Interactable)"/>.
    ///
    /// Reserved keys (movement + the game's own inventory/hand keys) are never bindable:
    /// W A S D, Space, I J L, Q E — exactly the set FlorpyDorp called out.
    /// </summary>
    public static class WedgeHotkeys
    {
        private sealed class Binding
        {
            public Interactable Interactable;
            public DynamicThing Thing;
            public string Name;
        }

        private static readonly Dictionary<KeyCode, Binding> _map = new Dictionary<KeyCode, Binding>();

        private static readonly HashSet<KeyCode> Reserved = new HashSet<KeyCode>
        {
            KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.Space,
            KeyCode.I, KeyCode.J, KeyCode.L, KeyCode.Q, KeyCode.E,
        };

        /// <summary>A-Z minus the reserved gameplay keys — the only keys a wedge may claim.</summary>
        public static bool IsBindable(KeyCode k) => k >= KeyCode.A && k <= KeyCode.Z && !Reserved.Contains(k);

        /// <summary>The first bindable letter pressed THIS frame (KeyCode.None if none) — used
        /// by the radial to bind the hovered setting wedge.</summary>
        public static KeyCode PressedBindableLetter()
        {
            for (KeyCode k = KeyCode.A; k <= KeyCode.Z; k++)
                if (IsBindable(k) && Input.GetKeyDown(k)) return k;
            return KeyCode.None;
        }

        /// <summary>Bind (or re-bind) a key to a setting. Rebinding a key that already points
        /// elsewhere moves it; binding the SAME setting to a new key drops its old key first,
        /// so a setting never answers to two letters.</summary>
        public static void Bind(KeyCode k, Interactable interactable, DynamicThing thing, string name)
        {
            if (!IsBindable(k) || interactable == null || thing == null) return;
            // One letter per setting: clear any prior key pointing at this interactable.
            var stale = new List<KeyCode>();
            foreach (var kv in _map)
                if (ReferenceEquals(kv.Value.Interactable, interactable)) stale.Add(kv.Key);
            foreach (var s in stale) _map.Remove(s);
            _map[k] = new Binding { Interactable = interactable, Thing = thing, Name = name };
        }

        /// <summary>The letter currently bound to this interactable, or '\0' — for the wedge
        /// badge near the hub.</summary>
        public static char LetterFor(Interactable interactable)
        {
            if (interactable == null) return '\0';
            foreach (var kv in _map)
                if (ReferenceEquals(kv.Value.Interactable, interactable))
                    return (char)('A' + (kv.Key - KeyCode.A));
            return '\0';
        }

        /// <summary>Run any bound key pressed this frame. Called from the plugin's Update while
        /// in gameplay (the caller gates on cursor-hidden so a bind can't fire into a text
        /// field). Dead references (item destroyed) are pruned as they fail.</summary>
        public static void TickExecute()
        {
            if (_map.Count == 0) return;
            List<KeyCode> dead = null;
            foreach (var kv in _map)
            {
                if (!Input.GetKeyDown(kv.Key)) continue;
                var b = kv.Value;
                bool ok = false;
                try
                {
                    if (b.Interactable != null && b.Thing != null && b.Thing.gameObject != null)
                        ok = ItemActions.PressInteractable(b.Thing, b.Interactable);
                }
                catch { }
                if (!ok)
                {
                    if (dead == null) dead = new List<KeyCode>();
                    dead.Add(kv.Key);
                }
            }
            if (dead != null) foreach (var k in dead) _map.Remove(k);
        }

        /// <summary>Hot-reload / mod-off: drop every binding.</summary>
        public static void Clear() => _map.Clear();
    }
}
