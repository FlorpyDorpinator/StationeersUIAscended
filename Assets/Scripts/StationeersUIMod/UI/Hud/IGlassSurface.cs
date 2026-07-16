using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// The subset of a procedural glass graphic's vertex-colour "look" knobs that
    /// <see cref="HudElementView.ApplyGlass"/> / <c>ApplyMeshFx</c> / <c>ApplyFx</c> drive from
    /// config. Both <see cref="PanelGraphic"/> (rectangles/trapezoids) and
    /// <see cref="PolygonPanelGraphic"/> (freeform pen shapes) implement it, so the ONE
    /// glass-styling code path lights a drawn Shape exactly like a Box — no duplicated wiring.
    /// (Fill <c>color</c>, <c>BorderColor</c> and <c>BorderWidth</c> are set by callers directly
    /// on the concrete graphic, so they stay off this interface.)
    /// </summary>
    public interface IGlassSurface : IHudFxGraphic
    {
        float Sheen { get; set; }
        float Spec { get; set; }
        float BorderFade { get; set; }
        float SoftEdge { get; set; }
        float Glow { get; set; }
        float GlowInner { get; set; }
        float GlowWidth { get; set; }
        float GlowDiffuse { get; set; }
        float EdgeRipple { get; set; }
        float EdgeRippleFreq { get; set; }

        /// <summary>0..1 — dials the ripple waveform from the layered incommensurate noise (0,
        /// choppy light/dark breakup) toward a single clean sine (1, a smooth light→dark
        /// gradient). 0 (default) is the classic look.</summary>
        float RippleSmooth { get; set; }

        /// <summary>Per-element AA-ramp width in px; -1 = use the global HudConfig.EdgeFeather.</summary>
        float FeatherOverride { get; set; }

        /// <summary>The graphic itself, for the FX-material assignment path (which needs a
        /// <see cref="MaskableGraphic"/>). Both implementers return <c>this</c>.</summary>
        MaskableGraphic AsGraphic { get; }
    }
}
