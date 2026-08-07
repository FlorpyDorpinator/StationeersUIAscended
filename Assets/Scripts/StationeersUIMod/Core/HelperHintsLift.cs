using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// Keeps vanilla's F2 helper-hints panel ABOVE every mod canvas — but BELOW vanilla's own
    /// full-attention menus (FlorpyDorp, 2026-08-06: "always on top of any of our mod's UI…
    /// make sure it still sits behind the esc menu").
    ///
    /// WHY: the hints panel lives on a normal-order vanilla canvas (~100s), while the mod's
    /// canvas family sits at 3800–5600 (HUD 3800, radials ~5000–5010, Grid 5020/5030, popup 5150,
    /// Control Center 5200, drag ghost 5250, tutorial coach 5600) — so mod UI drew OVER the tips.
    ///
    /// HOW: the proven VanillaTooltip lift pattern, scoped tight — a NESTED overrideSorting
    /// Canvas on the controller's own GameObject (never a shared root, the 2026-07-24 trap), at
    /// <see cref="LiftOrder"/> above the whole mod family, PLUS its own GraphicRaycaster so the
    /// panel's links/buttons stay clickable (a nested override canvas without one goes dead —
    /// the exit-menu investigation's Dropdown lesson, 2026-08-04). While a vanilla full-attention
    /// menu is front (<see cref="Guards.VanillaMenuWantsFront"/> — pause/start, Stationpedia, IC
    /// editor, console, creative) the override is switched OFF, restoring vanilla's natural
    /// layering, so the Esc menu draws over the tips exactly as shipped.
    ///
    /// SAFETY: the controller type is resolved BY NAME (no compile-time reference — a future
    /// branch that reshapes the class degrades this feature, never the mod), everything is
    /// fail-soft, writes are dirty-guarded, and <see cref="Shutdown"/> restores/destroys what we
    /// added (called from the teardown FINALLY — a lifted panel left at 5800 after unload would
    /// cover the Esc menu forever).
    /// </summary>
    internal static class HelperHintsLift
    {
        /// <summary>Above the tutorial coach (5600), the mod's own ceiling.</summary>
        private const int LiftOrder = 5800;

        private static bool _typeResolved;
        private static FieldInfo _instanceField;   // HelperHintsTextController._instance (static)

        private static Component _liftedFor;       // the controller instance our canvas belongs to
        private static Canvas _canvas;             // the nested canvas we manage
        private static GraphicRaycaster _ray;
        private static bool _weAddedCanvas;
        private static bool _weAddedRay;
        private static bool _origOverride;         // pre-lift state of a PRE-EXISTING canvas
        private static int _origOrder;

        public static void Tick()
        {
            try
            {
                if (!_typeResolved)
                {
                    _typeResolved = true;   // one attempt per load; a miss = feature off, mod fine
                    var t = System.Type.GetType(
                        "Assets.Scripts.UI.HelperHints.HelperHintsTextController, Assembly-CSharp");
                    if (t != null)
                        _instanceField = t.GetField("_instance",
                            BindingFlags.NonPublic | BindingFlags.Static);
                }
                if (_instanceField == null) return;

                var ctrl = _instanceField.GetValue(null) as Component;
                if (ctrl == null)   // Unity fake-null too: world unloaded, panel gone
                {
                    _liftedFor = null; _canvas = null; _ray = null;
                    return;
                }

                if (!ReferenceEquals(ctrl, _liftedFor) || _canvas == null)
                {
                    // New world / new instance: (re)acquire the nested canvas on the controller's
                    // OWN GameObject — the subtree holds exactly the panel + its collapse tab.
                    var go = ctrl.gameObject;
                    _canvas = go.GetComponent<Canvas>();
                    _weAddedCanvas = _canvas == null;
                    if (_weAddedCanvas) _canvas = go.AddComponent<Canvas>();
                    else { _origOverride = _canvas.overrideSorting; _origOrder = _canvas.sortingOrder; }
                    _ray = go.GetComponent<GraphicRaycaster>();
                    _weAddedRay = _ray == null;
                    if (_weAddedRay) _ray = go.AddComponent<GraphicRaycaster>();
                    _liftedFor = ctrl;
                }

                // Lift while the mod's UI could overlap; stand down to vanilla's natural layering
                // whenever a vanilla full-attention menu is front, so Esc & co. draw over the tips.
                bool lift = !Guards.VanillaMenuWantsFront();
                if (lift)
                {
                    if (!_canvas.overrideSorting) _canvas.overrideSorting = true;
                    if (_canvas.sortingOrder != LiftOrder) _canvas.sortingOrder = LiftOrder;
                }
                else
                {
                    bool wantOverride = !_weAddedCanvas && _origOverride;
                    if (_canvas.overrideSorting != wantOverride) _canvas.overrideSorting = wantOverride;
                    if (!_weAddedCanvas && _canvas.sortingOrder != _origOrder) _canvas.sortingOrder = _origOrder;
                }
            }
            catch { /* fail-soft: hints layering is never worth a broken frame */ }
        }

        /// <summary>Teardown / hot-reload: put vanilla back exactly as found. Destroy order
        /// matters — the raycaster requires the canvas, so it goes first.</summary>
        public static void Shutdown()
        {
            try
            {
                if (_canvas != null)
                {
                    if (_weAddedRay && _ray != null) Object.Destroy(_ray);
                    if (_weAddedCanvas) Object.Destroy(_canvas);
                    else { _canvas.overrideSorting = _origOverride; _canvas.sortingOrder = _origOrder; }
                }
            }
            catch { }
            _canvas = null; _ray = null; _liftedFor = null;
            _weAddedCanvas = false; _weAddedRay = false;
        }
    }
}
