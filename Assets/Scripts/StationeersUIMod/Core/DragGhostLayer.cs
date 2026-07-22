using Assets.Scripts.UI;           // InputMouse (world-drag CursorItem)
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.Core
{
    /// <summary>
    /// The ONE top-most drag-ghost host: a single non-raycast <see cref="RenderMode.ScreenSpaceOverlay"/>
    /// canvas at <see cref="SortingOrder"/> (5250) — above the ENTIRE Grid family (main window 5020,
    /// pinned windows 5030, tab drag-out 5100, profile popup 5150, Control Center 5200) — so every
    /// dragged visual renders ON TOP of the windows it might be dropped into.
    ///
    /// <para>WHY. Before this, each drag ghost lived on its SOURCE canvas: the HUD hand / worn-equipment
    /// ghost on the HUD canvas (3800), a bag-grid cell ghost on its own window canvas (5020 main / 5030
    /// pin). So a main-grid cell ghost flew UNDERNEATH an already-pinned window, and the HUD ghost
    /// underneath the whole Grid — the carried item vanished behind the very window you were dropping it
    /// into. Re-parenting every ghost here fixes the z-order in one place
    /// (<see cref="StationeersUIMod.UI.Hud.HudSlotDrag"/>'s hand/1-6 ghost and
    /// <see cref="StationeersUIMod.UI.Grid.BagGridCell"/>'s cell ghost both parent to <see cref="EnsureHost"/>).</para>
    ///
    /// <para>NON-RAYCAST. No <see cref="GraphicRaycaster"/> is added, so the layer can never intercept a
    /// pointer event nor occlude the EventSystem drop pick under the cursor (that pick walks
    /// GraphicRaycasters); the ghosts themselves are <c>raycastTarget = false</c>. Every ghost is a plain
    /// <see cref="Image"/>, so this canvas needs none of the analytic-panel <c>additionalShaderChannels</c>
    /// the HUD canvas opts into.</para>
    ///
    /// <para>WORLD-DRAG MIRROR. We do not own vanilla's world-drag rendering
    /// (<c>InputMouse.DragSlotDisplay</c>, drawn on the game canvas at ~order 0). While a vanilla world
    /// drag passes OVER the Grid or a pinned window it would disappear behind them, so
    /// <see cref="TickWorldMirror"/> paints a MIRROR of the carried thumbnail here — but ONLY over those
    /// windows, so vanilla carries the item everywhere else and there is never a double ghost. Read-only:
    /// it echoes the same sprite vanilla draws (<c>CursorItem.GetThumbnail()</c>) and never touches the
    /// drag machinery.</para>
    ///
    /// Static-reset rule: <see cref="Shutdown"/> destroys the canvas (and with it every child ghost) and
    /// nulls every field, so a double F6 reload leaves no stale canvas behind.
    /// </summary>
    internal static class DragGhostLayer
    {
        /// <summary>Above the whole Grid family incl. the Control Center (5200).</summary>
        private const int SortingOrder = 5250;

        private static Canvas _canvas;

        // World-drag mirror (vanilla carries the item; we only echo its thumbnail over our windows).
        private static GameObject _mirrorGo;
        private static RectTransform _mirrorRt;
        private static Image _mirror;

        /// <summary>The shared parent for every drag ghost. Lazily builds the top canvas on first use;
        /// null only if that build somehow failed. Callers parent their ghost here instead of their own
        /// source canvas so it outranks the whole Grid family.</summary>
        public static Transform EnsureHost()
        {
            EnsureCanvas();
            return _canvas != null ? _canvas.transform : null;
        }

        private static void EnsureCanvas()
        {
            if (_canvas != null) return;                   // Unity fake-null also catches a destroyed canvas
            var go = new GameObject("UIAscended_DragGhostLayer");
            Object.DontDestroyOnLoad(go);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = SortingOrder;
            // Deliberately NO GraphicRaycaster and NO additionalShaderChannels: this layer only ever
            // hosts non-raycast plain-Image ghosts, and must never occlude the drop pick under the
            // cursor (EventSystem.RaycastAll walks GraphicRaycasters).
        }

        /// <summary>Once per frame (from the plugin Update, after the Grid tick): while VANILLA carries a
        /// world-sourced item AND the cursor is over the Grid or a pinned window, mirror the carried
        /// thumbnail onto this top layer so it draws ABOVE those windows. Hidden the instant the drag
        /// ends or the cursor leaves those bounds — vanilla owns the ghost everywhere else, so there is
        /// never a double ghost. Cheap: one InputMouse read + up to two window hit tests, early-out when
        /// no world drag is live.</summary>
        public static void TickWorldMirror()
        {
            Sprite icon = null;
            try
            {
                if (DropResolver.VanillaWorldDragLive())
                {
                    Vector2 mp = Input.mousePosition;
                    if (UI.Grid.TheGridPanel.HitTestWindow(mp)
                        || UI.Grid.PinnedInventoryWindow.HitTestAny(mp))
                    {
                        var m = InputMouse.Instance;
                        var carried = m != null ? m.CursorItem : null;
                        if (carried != null) { try { icon = carried.GetThumbnail(); } catch { } }
                    }
                }
            }
            catch { icon = null; }

            if (icon == null) { HideMirror(); return; }

            EnsureCanvas();
            EnsureMirror();
            _mirror.sprite = icon;
            if (!_mirrorGo.activeSelf) _mirrorGo.SetActive(true);
            _mirrorRt.SetAsLastSibling();
            // ScreenSpaceOverlay at scaleFactor 1: world position IS the screen pixel (same math the
            // bag-grid cell ghost uses), so this tracks the raw cursor with no canvas-rect dependency.
            _mirrorRt.position = new Vector3(Input.mousePosition.x, Input.mousePosition.y, 0f);
        }

        private static void EnsureMirror()
        {
            if (_mirror != null) return;                   // Unity fake-null also catches a destroyed mirror
            _mirrorGo = new GameObject("WorldDragMirror", typeof(RectTransform));
            _mirrorGo.transform.SetParent(_canvas.transform, false);
            _mirrorRt = (RectTransform)_mirrorGo.transform;
            _mirrorRt.anchorMin = _mirrorRt.anchorMax = new Vector2(0.5f, 0.5f);
            _mirrorRt.pivot = new Vector2(0.5f, 0.5f);
            _mirrorRt.sizeDelta = new Vector2(48f, 48f);
            _mirror = _mirrorGo.AddComponent<Image>();
            _mirror.raycastTarget = false;                 // never occlude the drop under the cursor
            _mirror.preserveAspect = true;
            _mirror.color = new Color(1f, 1f, 1f, 0.85f);
        }

        private static void HideMirror()
        {
            if (_mirrorGo != null && _mirrorGo.activeSelf) _mirrorGo.SetActive(false);
        }

        /// <summary>Hot-reload / teardown: destroy the canvas (and with it every child ghost) and null
        /// every static, so a reloaded assembly starts clean (CLAUDE.md static-reset rule).</summary>
        public static void Shutdown()
        {
            if (_mirrorGo != null) Object.Destroy(_mirrorGo);
            _mirrorGo = null;
            _mirrorRt = null;
            _mirror = null;
            if (_canvas != null) Object.Destroy(_canvas.gameObject);
            _canvas = null;
        }
    }
}
