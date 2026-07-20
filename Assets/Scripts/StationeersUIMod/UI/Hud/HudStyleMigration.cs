using UnityEngine;
using StationeersUIMod.Core;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// The 2026-07-16 legacy-style REGRESSION. Pre-standardisation profiles carried a third,
    /// implicit style state ("legacy mixed", styleSource 0/absent): the followGlobal colour
    /// flag, "-1 = follow global" sentinels, and fx* keys that MULTIPLIED the globals
    /// (fxFrostAmt et al. — the source of every "this element won't react to the global
    /// slider" bug). This pass, run from <see cref="HudDocument.Sanitize"/> on every load,
    /// rewrites each legacy element into the coherent two-state contract:
    ///
    ///  • An element whose effects/glass/sizing carried NO real override maps to GLOBAL —
    ///    it renders identically and every F9 effect slider now drives it 1:1.
    ///  • Anything with a real override maps to CUSTOM via the same snapshot path the F9
    ///    inspector uses (<see cref="HudElementView.SetUnifiedStyleSourceWithoutView"/>,
    ///    which honours full legacy semantics for a stored 0) — pixel-identical, but now a
    ///    complete, self-contained design instead of a mixture of sentinels and multipliers.
    ///
    /// COLOURS migrate by ref content, not by state: a legacy element that followed the
    /// global palette gets its refs rewritten to the palette NAMES (so it keeps tracking the
    /// F9 palette live); an element with its own refs keeps them verbatim. Colour identity is
    /// therefore never destroyed by the migration OR by later "follow global" flips.
    ///
    /// Idempotent by construction: it only touches elements whose stored styleSource is
    /// absent/0, and it always writes 1 or 2. Fail-soft: a throwing element is left as-is
    /// (StyleSourceOf reads 0 as Global at runtime, so nothing crashes — the element simply
    /// follows the globals until the next successful pass).
    /// </summary>
    internal static class HudStyleMigration
    {
        /// <summary>Keys whose presence (as a real value, not the -1 sentinel) marks the
        /// element as carrying its own effect/glass design → must map to Custom.
        /// sheen/spec are checked separately: under legacy followGlobal=true they were
        /// DORMANT (the flag forced the globals over them), so they must not block a
        /// follower's clean mapping to Global.</summary>
        private static readonly string[] SentinelKeys =
        {
            "feather", "bfade", "softEdge", "glow", "glowIn", "glowWidth",
            "glowDiffuse", "glowExtraDiffuse", "glowHaze", "glowBreath", "glowUneven",
            "glowOrganicScale", "glowFlowAura", "ripple", "rippleFreq", "edgeFlow",
            "frostDepth", "edgeLight",
        };

        /// <summary>The extinct legacy multiplier/opt-out pairs, folded into the Custom
        /// snapshot by the migration and then removed everywhere.</summary>
        private static readonly string[] LegacyFxKeys =
        {
            "fxShine", "fxShineAmt", "fxIrid", "fxIridAmt", "fxChroma", "fxChromaAmt",
            "fxFrost", "fxFrostAmt", "fxDissolve",
        };

        /// <summary>Everything a clean GLOBAL element must not carry: dormant style values
        /// would silently reactivate on the next separation snapshot as if they were the
        /// author's design. (Participation keys like edgeFade, insets, width, fadeEnds and
        /// content keys are NOT style state and are never touched.)</summary>
        private static readonly string[] GlobalStripKeys =
        {
            "sheen", "spec", "feather", "bfade", "softEdge", "glow", "glowIn", "glowWidth",
            "glowDiffuse", "glowExtraDiffuse", "glowHaze", "glowBreath", "glowUneven",
            "glowOrganicScale", "glowFlowAura", "ripple", "rippleFreq", "rippleSmooth",
            "edgeFlow", "frostDepth", "edgeLight", "squircle", "gaussianHalo",
            "fxCollapse", "fxCollapseAmt", "fxGlitch", "fxGlitchAmt", "fxWarp", "fxWarpAmt",
            "fxPulse", "fxPulseAmt", "customStyleReady",
        };

        /// <summary>Regress every legacy-styled element in <paramref name="doc"/> to the
        /// two-state contract. Returns how many elements were rewritten.</summary>
        internal static int Migrate(HudDocument doc)
        {
            if (doc == null || doc.Elements == null) return 0;
            int migrated = 0;
            for (int i = 0; i < doc.Elements.Count; i++)
            {
                var el = doc.Elements[i];
                if (el == null) continue;
                try
                {
                    if (MigrateElement(el)) migrated++;
                }
                catch (System.Exception e)
                {
                    UIALog.Warn("HudStyleMigration: element '" + el.Id + "' failed ("
                        + e.Message + ") — left for the next pass.");
                }
            }
            return migrated;
        }

        private static bool MigrateElement(HudElementDef el)
        {
            int raw = el.GetI("styleSource", HudElementView.StyleLegacy);
            if (raw == HudElementView.StyleGlobal || raw == HudElementView.StyleCustom)
            {
                // Already coherent — just clear extinct legacy residue so it can never
                // resurface (cheap no-ops when the keys are absent).
                bool touched = el.GetS("followGlobal", null) != null;
                el.Set("followGlobal", null);
                for (int i = 0; i < LegacyFxKeys.Length; i++)
                {
                    // Custom keeps its motion/pulse keys; only the extinct multiplier family goes.
                    touched |= el.GetS(LegacyFxKeys[i], null) != null;
                    el.Set(LegacyFxKeys[i], null);
                }
                return touched;
            }

            bool followedColours = el.GetB("followGlobal", false);

            if (IsEffectivelyGlobal(el, followedColours))
            {
                // Renders identically as a pure follower; strip the sediment so nothing
                // dormant can reactivate later.
                el.SetI("styleSource", HudElementView.StyleGlobal);
                for (int i = 0; i < GlobalStripKeys.Length; i++) el.Set(GlobalStripKeys[i], null);
                el.BorderWidth = -1f;
                el.RTL = -1f; el.RTR = -1f; el.RBR = -1f; el.RBL = -1f;
            }
            else
            {
                // Real overrides: freeze the CURRENT legacy render into a complete Custom
                // snapshot (the WithoutView path honours the stored 0's full legacy
                // semantics — followGlobal glass gate, -1 sentinels, fx* multipliers,
                // the spec==0 edge-light opt-out).
                HudElementView.SetUnifiedStyleSourceWithoutView(el, false, true);
            }

            // Colour continuity. Legacy followGlobal=true painted the PALETTE over the
            // element's (dormant) refs; refs now always resolve, so hand the element the
            // palette NAMES — it keeps showing and tracking exactly what it showed.
            // followGlobal=false refs were already live: keep them verbatim.
            if (followedColours)
            {
                el.Fill = "HudPanelFill";
                el.Border = "HudPanelBorder";
                el.TextColor = "HudTextValue";
                if (el.Type == HudElementType.Readout)
                {
                    // Empty bar refs already mean "the palette slot" (Good/Warn/Critical/
                    // PanelBorder/TextValue) — exactly what legacy-following rendered.
                    el.Set("barFill", null);
                    el.Set("barWarn", null);
                    el.Set("barCrit", null);
                    el.Set("barTrack", null);
                    el.Set("barTarget", null);
                }
            }

            el.Set("followGlobal", null);
            for (int i = 0; i < LegacyFxKeys.Length; i++) el.Set(LegacyFxKeys[i], null);
            return true;
        }

        /// <summary>True when the legacy element's effects/glass/sizing resolve to the pure
        /// globals — i.e. every style key is at its "-1 = follow global" sentinel (or the
        /// equivalent neutral default), every fx* multiplier is neutral, and motion/pulse sit
        /// at the defaults Global enforces. Colour refs never matter here: they resolve
        /// identically in the Global state.</summary>
        private static bool IsEffectivelyGlobal(HudElementDef el, bool followedColours)
        {
            if (el.BorderWidth >= 0f) return false;
            if (el.RTL >= 0f || el.RTR >= 0f || el.RBR >= 0f || el.RBL >= 0f) return false;

            // Under legacy followGlobal=true, baked sheen/spec were dormant (the flag forced
            // the global glass) — the element rendered as a follower regardless, so they do
            // not block Global. Without the flag they were live.
            if (!followedColours
                && (el.GetF("sheen", -1f) >= 0f || el.GetF("spec", -1f) >= 0f)) return false;

            for (int i = 0; i < SentinelKeys.Length; i++)
                if (el.GetF(SentinelKeys[i], -1f) >= 0f) return false;

            // squircle's sentinel is "< 2", not "< 0".
            if (el.GetF("squircle", -1f) >= 2f) return false;
            // rippleSmooth has no global; Global renders it 0.
            if (el.GetF("rippleSmooth", 0f) > 0.0001f) return false;

            // Legacy multipliers/opt-outs at anything but neutral change the render.
            if (!el.GetB("fxShine", true) || !el.GetB("fxIrid", true)
                || !el.GetB("fxChroma", true) || !el.GetB("fxFrost", true)
                || !el.GetB("fxDissolve", true)) return false;
            if (!NearOne(el.GetF("fxShineAmt", 1f)) || !NearOne(el.GetF("fxIridAmt", 1f))
                || !NearOne(el.GetF("fxChromaAmt", 1f)) || !NearOne(el.GetF("fxFrostAmt", 1f)))
                return false;

            // Global forces collapse/glitch/warp ON at 1x and pulse OFF.
            if (!el.GetB("fxCollapse", true) || !el.GetB("fxGlitch", true)
                || !el.GetB("fxWarp", true) || el.GetB("fxPulse", false)) return false;
            if (!NearOne(el.GetF("fxCollapseAmt", 1f)) || !NearOne(el.GetF("fxGlitchAmt", 1f))
                || !NearOne(el.GetF("fxWarpAmt", 1f))) return false;

            return true;
        }

        private static bool NearOne(float v) => Mathf.Abs(v - 1f) < 0.0001f;
    }
}
