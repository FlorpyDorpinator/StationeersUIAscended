using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Kit
{
    /// <summary>
    /// Decorates an interior menu surface (a button, a panel) with a HUD glass BORDER — edge light
    /// + ripple + sheen following the F9 globals — but deliberately NO glow halo (the halo is
    /// reserved for the outer window; FlorpyDorp: "I don't want all the interior buttons to have
    /// halo glow though ... but interior buttons can follow the edge light ripple etc").
    ///
    /// Additive by design: it adds a child <see cref="UI.Hud.PanelGraphic"/> that draws only the
    /// (transparent-fill) glass border over the host's existing Image, so the host's fill/hover
    /// recolour logic is never touched. raycastTarget is off, so clicks pass straight through to
    /// the button. Hot-reload safe (no statics); the child dies with the host.
    /// </summary>
    internal sealed class UiaGlassSkin : MonoBehaviour
    {
        private UI.Hud.PanelGraphic _panel;
        private RectTransform _rt;
        private static readonly Color Transparent = new Color(0f, 0f, 0f, 0f);

        private void EnsurePanel()
        {
            if (_panel != null) return;
            _rt = transform as RectTransform;
            if (_rt == null) return;
            var go = new GameObject("GlassBorder", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var prt = (RectTransform)go.transform;
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;
            _panel = go.AddComponent<UI.Hud.PanelGraphic>();
            _panel.raycastTarget = false;   // clicks fall through to the host button
            prt.SetAsLastSibling();          // border draws OVER the host fill + its label
        }

        // LateUpdate: the layout system has resolved this frame's rect size by now.
        private void LateUpdate()
        {
            EnsurePanel();
            if (_panel == null || _rt == null) return;
            var size = _rt.rect.size;
            if (size.x < 2f || size.y < 2f) return;
            float corner = UiaTheme.Corner;
            float bw = UI.Hud.HudConfig.BorderWidth != null ? UI.Hud.HudConfig.BorderWidth.Value : 1.4f;
            _panel.color = Transparent;              // no fill — the host keeps its own
            _panel.BorderColor = UiaTheme.Accent;    // themed edge line
            _panel.BorderWidth = bw;
            _panel.SetShape(size.x, size.y, corner);
            // Edge light + ripple + sheen from the globals; NO glow halo, no frost (interiors).
            UI.Hud.HudGlobalGlass.Apply(_panel, includeGlow: false, wantFrost: false, wantTierB: false);
        }
    }
}
