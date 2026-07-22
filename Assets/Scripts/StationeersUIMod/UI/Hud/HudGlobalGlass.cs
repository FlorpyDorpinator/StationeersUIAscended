using BepInEx.Configuration;
using UnityEngine;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// Applies the F9 GLOBAL effect stack (the "follow global" resolution a HUD element in Global
    /// style renders) to any <see cref="PanelGraphic"/> that is NOT a document element — currently
    /// the F10 Control Center. This lets a non-HUD surface look exactly like a HUD glass box:
    /// sheen, edge light + ripple, border fade / soft edge, the glow halo, and Tier B/C
    /// (shine/iridescence via the shared edgefx material, frost via the glass material + backdrop).
    ///
    /// Mirrors the GLOBAL branch of <see cref="HudElementView"/>'s resolvers (ApplyGlass /
    /// ApplyMeshFx / ApplyFx) — kept in one place so a menu surface and a HUD box read identically.
    /// Every read is null-guarded (config may be unbound early / on F6) and every setter is
    /// dirty-guarded in the graphic, so this is cheap to call per frame.
    /// </summary>
    internal static class HudGlobalGlass
    {
        /// <summary>Set true each frame a non-HUD surface wants Tier C frost, so
        /// <see cref="HudSystem"/> keeps the backdrop capture alive for it (the same role
        /// <c>UnityRadialView.RadialFrostWanted</c> plays for the radial). The writer is
        /// responsible for clearing it when it stops wanting frost (the menu clears it on close).</summary>
        public static bool FrostDemand;

        private static float Cfg(ConfigEntry<float> e, float d) => e != null ? e.Value : d;
        private static bool On(ConfigEntry<bool> e) => e != null && e.Value;

        /// <param name="includeGlow">false = every other effect but NO glow halo (the interior
        /// menu surfaces follow edge light + ripple, but the halo is reserved for the outer window).</param>
        /// <param name="wantFrost">opt into the Tier C frosted backdrop when the HUD master is on.</param>
        /// <param name="wantTierB">opt into shine / iridescence (the shared edgefx material).</param>
        /// <param name="externalMaterial">true = the caller has already bound its own shared
        /// effect material (the analytic sdfglass path) and owns <see cref="IGlassSurface.FxStrength"/>;
        /// Apply then styles the mesh FIELDS only and leaves the material slot untouched. Without
        /// this, Apply's glass/edgefx selection and the caller's assignment would reassign the
        /// slot against each other every frame — a per-frame canvas rebatch.</param>
        public static void Apply(PanelGraphic g, bool includeGlow, bool wantFrost, bool wantTierB,
            bool externalMaterial = false)
        {
            if (g == null) return;
            bool tierA = On(HudConfig.FxTierA);

            // Glass sheen is Tier-A-independent; edge-light 'spec' takes the Tier-A boost when the
            // global edge light is on (the same +FxEdgeLight*0.45 a following HUD box gets).
            g.Sheen = Cfg(HudConfig.GlassSheen, 0f);
            float spec = Cfg(HudConfig.GlassEdge, 0f);
            if (tierA && On(HudConfig.FxEdgeLightOn) && Cfg(HudConfig.FxEdgeLight, 0f) > 0f)
                spec = Mathf.Clamp01(spec + HudConfig.FxEdgeLight.Value * 0.45f);
            g.Spec = spec;
            g.FeatherOverride = -1f; // -1 = the global EdgeFeather

            g.BorderFade = tierA && On(HudConfig.FxBorderFadeOn) ? Cfg(HudConfig.FxBorderFade, 0f) : 0f;
            g.SoftEdge = tierA && On(HudConfig.FxSoftEdgeOn) ? Cfg(HudConfig.FxSoftEdge, 0f) : 0f;

            bool glow = includeGlow && tierA && On(HudConfig.FxGlowOn);
            g.Glow = glow ? Cfg(HudConfig.FxGlow, 0f) : 0f;
            g.GlowInner = glow ? Cfg(HudConfig.FxGlowInner, 0f) : 0f;
            g.GlowWidth = Cfg(HudConfig.FxGlowWidth, 14f);
            g.GlowDiffuse = Cfg(HudConfig.FxGlowDiffuse, 0.5f);
            g.GlowExtraDiffuse = Cfg(HudConfig.FxGlowExtraDiffuse, 0f);

            bool edge = tierA && On(HudConfig.FxEdgeLightOn);
            g.EdgeRipple = edge ? Cfg(HudConfig.FxEdgeRipple, 0f) : 0f;
            g.EdgeRippleFreq = Cfg(HudConfig.FxEdgeRippleFreq, 2f);
            g.RippleSmooth = 0f; // global forces the un-smoothed ripple

            // The analytic SDF caller has already bound sdfglass and will zero FxStrength itself
            // (the SDF ABI carries independent strengths; uv0.x is unused there).
            if (externalMaterial) return;

            // Shared-material selection (frost wins over edge-fx), mirroring ApplyFx's global path.
            // FxStrength is the uv0.x volume: 1 = full (a global-following element resolves to 1).
            bool frostActive = wantFrost && On(HudConfig.FxTierC) && HudBackdrop.Active
                && Core.HudShaderStore.TierBAvailable && HudFxMaterials.Available;
            bool tierB = wantTierB && On(HudConfig.FxTierB) && Core.HudShaderStore.TierBAvailable;
            bool shineOn = tierB && On(HudConfig.FxShineOn) && Cfg(HudConfig.FxShine, 0f) > 0.001f;
            bool iridOn = tierB && On(HudConfig.FxIridOn) && Cfg(HudConfig.FxIridescence, 0f) > 0.001f;
            bool edgeFx = !frostActive && (shineOn || iridOn);

            if (frostActive)
            {
                g.FxStrength = 1f;
                if (!HudFxMaterials.Assign(g, "glass")) HudFxMaterials.Unassign(g);
            }
            else if (edgeFx)
            {
                g.FxStrength = 1f;
                if (!HudFxMaterials.Assign(g, "edgefx")) HudFxMaterials.Unassign(g);
            }
            else
            {
                g.FxStrength = 0f;
                HudFxMaterials.Unassign(g);
            }
        }
    }
}
