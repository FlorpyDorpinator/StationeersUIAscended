using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Kit
{
    /// <summary>
    /// Decorates a menu surface (a button, a panel) with its BORDER line. Default: a plain, even
    /// line in the border colour (FlorpyDorp 2026-09-26: no glowy lines inside the tab pages).
    /// With <see cref="Flow"/> on (the title-bar row, the window edge): the HUD glass edge —
    /// edge light + moving ripple following the F9 globals — still with NO glow halo.
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

        /// <summary>Opt-in MOVING edge light. Off (the default) = the thin static mesh-path line
        /// every interior surface wears. FlorpyDorp (2026-09-26): only the manila tabs, the F10
        /// window's exterior and the title-bar row (master bars, search, pause, X) animate —
        /// "the interior boxes don't need to be animated".</summary>
        internal bool Flow;

        /// <summary>Per-host look overrides, read every LateUpdate (so a host may retint its
        /// line on hover/selection). Null / negative = the kit default: UiaTheme.Border, the
        /// global HudConfig.BorderWidth, UiaTheme.Corner.</summary>
        internal Color? BorderColorOverride;
        internal float WidthOverride = -1f;
        internal float CornerOverride = -1f;

        /// <summary>Give <paramref name="host"/> the kit's glass edge line (idempotent — an
        /// existing skin is reused and re-styled).</summary>
        internal static UiaGlassSkin Add(GameObject host, Color? border = null,
            float width = -1f, float corner = -1f, bool flow = false)
        {
            if (host == null) return null;
            var skin = host.GetComponent<UiaGlassSkin>();
            if (skin == null) skin = host.AddComponent<UiaGlassSkin>();
            skin.BorderColorOverride = border;
            skin.WidthOverride = width;
            skin.CornerOverride = corner;
            skin.Flow = flow;
            return skin;
        }

        /// <summary>Switch the moving edge light on for a host's EXISTING skin (a kit control
        /// that already carries one) without touching its look overrides.</summary>
        internal static void EnableFlow(GameObject host)
        {
            var skin = host != null ? host.GetComponent<UiaGlassSkin>() : null;
            if (skin != null) skin.Flow = true;
        }

        /// <summary>The mod's MOVING edge light (FlorpyDorp: "in the mod the UI elements
        /// that edge glow MOVES — make that enabled on our edge glow in this UI menu"):
        /// route the panel onto the analytic sdfglass path, whose shader animates the
        /// edge-energy sweep off _Time (HudConfig.EdgeFlowSpeed — the exact mechanism the
        /// visor HUD boxes and the Grid shell run; no hand-rolled tweens, the motion is
        /// entirely shader-side so the ~7Hz restyle costs nothing extra). Returns whether
        /// the analytic path is live so the caller can hand
        /// <see cref="UI.Hud.HudGlobalGlass.Apply"/> its <c>externalMaterial</c> flag; the
        /// fallback re-asserts mesh mode with HudElementView's exact neutral arguments (a
        /// stale SDF mode under the default material draws parameter-grid garbage).
        /// <paramref name="allowed"/> false keeps a surface on the mesh path. Masked surfaces
        /// are allowed: the shader implements the hard _ClipRect clip RectMask2D drives; only
        /// RectMask2D.softness (unused by the kit's viewports) has no analytic equivalent.</summary>
        internal static bool ApplySdfFlow(UI.Hud.PanelGraphic panel, bool allowed)
        {
            bool sdf = allowed
                && UI.Hud.HudSystem.FxClockLive
                && Core.HudShaderStore.SdfAvailable
                && UI.Hud.HudFxMaterials.Assign(panel, "sdfglass");
            if (sdf)
            {
                float squircle = Mathf.Clamp(UI.Hud.HudConfig.SdfSquircle != null
                    ? UI.Hud.HudConfig.SdfSquircle.Value : 2f, 2f, 8f);
                bool gaussian = UI.Hud.HudConfig.SdfGaussianHalo != null
                    && UI.Hud.HudConfig.SdfGaussianHalo.Value;
                float flow = Mathf.Clamp(UI.Hud.HudConfig.FxEdgeFlowSpeed != null
                    ? UI.Hud.HudConfig.FxEdgeFlowSpeed.Value : 0.22f, 0f, 4f);
                panel.SetSdfStyle(true, squircle, gaussian, flow,
                    0f, 1f, 0f, 0f, 0f, false, 0f, 0f, 0f, 0f, 0f, 0f, 1f);
                // The SDF ABI carries independent strengths; uv0.x's combined volume must
                // not force legacy mesh semantics (mirrors HudElementView.ApplyFx).
                panel.FxStrength = 0f;
            }
            else
            {
                panel.SetSdfStyle(false, 2f, false, 0f, 0f, 1f, 0f, 0f, 0f,
                    false, 0f, 0f, 0f, 0f, 0f, 0f, 1f);
            }
            return sdf;
        }

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
            // OPT OUT of the host's layout group (2026-09-26 visual fix wave): a host carrying
            // its own VLayout (a Section card, an Organizer column, the editing band) otherwise
            // captures this Fill-anchored child as one more zero-height LAST row and drags the
            // whole glass frame onto that displaced pivot — every border drawn ~half a card too
            // low, reading as "the next section renders inside this one's frame".
            var ble = go.AddComponent<LayoutElement>();
            ble.ignoreLayout = true;
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
            float corner = CornerOverride >= 0f ? CornerOverride : UiaTheme.Corner;
            _panel.color = Transparent;              // no fill — the host keeps its own
            // Themed edge line (the HUD box border, not the accent) unless the host tints it.
            _panel.BorderColor = BorderColorOverride ?? UiaTheme.Border;

            if (!Flow)
            {
                // The border band lies OUTSIDE its contour, so a box touching a scroll
                // viewport's edge lost that side of its line to the mask. Inset the contour by
                // the band's full reach so the whole stroke sits inside the host.
                float pw = WidthOverride >= 0f ? WidthOverride : PlainWidth;
                float inset = pw + PlainFeather;
                SetInset(inset);
                _panel.SetShape(Mathf.Max(1f, size.x - 2f * inset), Mathf.Max(1f, size.y - 2f * inset),
                    Mathf.Max(0f, corner - inset));
                // A NORMAL line (FlorpyDorp 2026-09-26: "get rid of the glowy lines around
                // boxes and replace with normal lines ... throughout the tabs"): one even stroke
                // in the border colour — no edge-light catch, ripple bake, fade or halo. The
                // glass recipe stays for the flowing surfaces only (title-bar row, window edge).
                ApplySdfFlow(_panel, false);
                UI.Hud.HudFxMaterials.Unassign(_panel);
                _panel.BorderWidth = pw;
                _panel.FeatherOverride = PlainFeather;
                _panel.Sheen = 0f;
                _panel.Spec = 0f;
                _panel.BorderFade = 0f;
                _panel.SoftEdge = 0f;
                _panel.EdgeRipple = 0f;
                _panel.Glow = 0f;
                _panel.GlowInner = 0f;
                return;
            }

            SetInset(0f);
            _panel.FeatherOverride = -1f;   // the global EdgeFeather, as the glass edge always had
            _panel.SetShape(size.x, size.y, corner);
            _panel.BorderWidth = WidthOverride >= 0f ? WidthOverride
                : (UI.Hud.HudConfig.BorderWidth != null ? UI.Hud.HudConfig.BorderWidth.Value : 1.4f);
            // The moving edge light first (it owns the material when live), then the
            // globals: edge light + ripple + sheen; NO glow halo, no frost.
            bool sdf = ApplySdfFlow(_panel, true);
            UI.Hud.HudGlobalGlass.Apply(_panel, includeGlow: false, wantFrost: false,
                wantTierB: false, externalMaterial: sdf);
        }

        private float _inset = -1f;

        private void SetInset(float inset)
        {
            if (Mathf.Approximately(_inset, inset)) return;
            _inset = inset;
            var prt = (RectTransform)_panel.transform;
            prt.offsetMin = new Vector2(inset, inset);
            prt.offsetMax = new Vector2(-inset, -inset);
        }

        // The plain line: a ~1px stroke once the anti-aliasing fringe is counted.
        private const float PlainWidth = 0.7f;
        private const float PlainFeather = 0.6f;

        private void OnDestroy()
        {
            // The analytic path assigns a shared material keyed by the graphic — forget it
            // BEFORE the graphic dies so restyle churn can't fill the registry with dead
            // keys (the window panel's own hygiene, applied to every skinned surface).
            if (_panel != null) UI.Hud.HudFxMaterials.Unassign(_panel);
        }
    }
}
