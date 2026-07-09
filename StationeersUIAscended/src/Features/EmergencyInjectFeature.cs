using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Entities;
using StationeersUIAscended.Core;
using StationeersUIAscended.Overlay;
using UnityEngine;

namespace StationeersUIAscended.Features
{
    /// <summary>
    /// Testing self-preservation: a hotkey that finds a health / stim auto-injector you OWN
    /// and uses it on yourself through the game's OWN secondary-use funnel
    /// (NetworkClient.UseItemSecondary → server → Injector.OnUseItem → Human.Apply). This is
    /// exactly the message the vanilla client sends when you hold-use a consumable, so the
    /// SERVER performs the heal and validates it — the mod forges nothing.
    ///
    /// Multiplayer honesty: the funnel operates on the ACTIVE HAND slot of your human, so the
    /// injector must be in your active hand. If it is elsewhere on you (worn / bag), the mod
    /// first moves it to your active hand via the item funnel and fires the use once it lands
    /// (surviving the client→server round-trip). It works while incapacitated because it is
    /// deliberately not gated on gameplay-input focus. It cannot heal you if you own no
    /// injector — no server message consumes an item that isn't there.
    /// </summary>
    public sealed class EmergencyInjectFeature
    {
        private DynamicThing _pendingInjector;
        private float _pendingDeadline;

        public void Update()
        {
            if (!UIAConfig.EmergencyInjectEnabled.Value) return;
            if (GameManager.IsBatchMode) return;

            // Raw keypress detection FIRST (before any human/state gate), so pressing the
            // hotkey always produces visible feedback — proves the key is registering and
            // tells you WHY nothing happened. Accepts both top-row and numpad digits.
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool healPressed = shift && (Input.GetKeyDown(UIAConfig.EmergencyHealKey.Value)
                                         || Input.GetKeyDown(KeyCode.Keypad9));
            bool stimPressed = shift && (Input.GetKeyDown(UIAConfig.EmergencyStimKey.Value)
                                         || Input.GetKeyDown(KeyCode.Keypad8));

            var human = InventoryManager.ParentHuman;
            if (healPressed || stimPressed)
                UIALog.Info($"[EmergencyInject] hotkey detected (heal={healPressed} stim={stimPressed}, human={(human != null)})");

            if (human == null)
            {
                if (healPressed || stimPressed)
                    Toast.Show("No living body — can't inject", Theme.Critical, 3f);
                _pendingInjector = null;
                return;
            }

            // Deferred fire: the injector was moved to the active hand; use it once it arrives.
            if (_pendingInjector != null)
            {
                if (Time.unscaledTime > _pendingDeadline)
                {
                    Announce("[UI Ascended] Injector didn't reach your hand in time — try again.");
                    _pendingInjector = null;
                }
                else if (InventoryManager.ActiveHandSlot?.Get() == _pendingInjector)
                {
                    FireUse();
                    _pendingInjector = null;
                }
                return;
            }

            if (healPressed)
                UseInjector("HealthInjector", "health");
            else if (stimPressed)
                UseInjector("EpiInjector", "stim");
        }

        private void UseInjector(string className, string label)
        {
            var human = InventoryManager.ParentHuman;
            if (human == null) return;

            ScannedSlot found = FindInjector(human, className);
            if (found == null)
            {
                Announce($"[UI Ascended] No {label} auto-injector found on you.");
                Toast.Show($"No {label} injector on you", Theme.Critical, 3f);
                UIAudioManager.Play(UIAudioManager.ActionFailHash);
                return;
            }
            Toast.Show($"Using {found.Occupant.DisplayName}...", Theme.Accent, 3f);

            Slot hand = InventoryManager.ActiveHandSlot;
            if (hand != null && hand.Get() == found.Occupant)
            {
                FireUse();
                return;
            }

            if (hand == null || hand.Get() != null)
            {
                Announce($"[UI Ascended] Clear your active hand, then Shift+{KeyName(className)} again " +
                         $"({found.Occupant.DisplayName} is in your {found.Location}).");
                UIAudioManager.Play(UIAudioManager.ActionFailHash);
                return;
            }

            // Active hand is free and the injector is elsewhere on you: move it in via the
            // funnel, then fire the use when it lands (handles the server round-trip).
            var injector = found.Occupant;
            if (!Slot.AllowMove(injector, hand))
            {
                Announce($"[UI Ascended] Couldn't ready the {label} injector.");
                return;
            }
            OnServer.MoveToSlot(injector, hand);
            _pendingInjector = injector;
            _pendingDeadline = Time.unscaledTime + 3f;
            Announce($"[UI Ascended] Readying {injector.DisplayName}…");
        }

        /// <summary>Send the vanilla secondary-use for the active hand — the server runs the heal.</summary>
        private static void FireUse()
        {
            try
            {
                var entity = InventoryManager.Parent;
                var activeHand = InventoryManager.Instance != null ? InventoryManager.Instance.ActiveHand : null;
                if (entity == null || activeHand == null) return;
                int slotId = activeHand.SlotId;
                if (GameManager.RunSimulation)
                    OnServer.UseItemSecondary(entity, slotId, 1f);
                else
                    NetworkClient.UseItemSecondary(entity, slotId, 1f);
                UIAudioManager.Play(UIAudioManager.ObjectIntoHandHash);
                Announce("[UI Ascended] Injector used on yourself (server applies the effect).");
                Toast.Show("Injector used - server applying effect", Theme.Good, 3f);
            }
            catch (System.Exception e)
            {
                UIALog.Error("Emergency inject fire failed: " + e);
                Announce("[UI Ascended] Inject failed: " + e.Message);
            }
        }

        private static ScannedSlot FindInjector(Human human, string className)
        {
            foreach (var scanned in InventoryScanner.Scan(3, includeToolSlots: true))
            {
                var occ = scanned.Occupant;
                if (occ != null && IsClass(occ, className))
                    return scanned.Pin();
            }
            return null;
        }

        private static bool IsClass(object o, string className)
        {
            var t = o?.GetType();
            while (t != null)
            {
                if (t.Name == className) return true;
                t = t.BaseType;
            }
            return false;
        }

        private static string KeyName(string className) =>
            className == "HealthInjector" ? UIAConfig.EmergencyHealKey.Value.ToString()
                                          : UIAConfig.EmergencyStimKey.Value.ToString();

        private static void Announce(string msg)
        {
            UIALog.Info(msg);
            try { ConsoleWindow.Print(msg, System.ConsoleColor.Cyan); } catch { }
        }
    }
}
