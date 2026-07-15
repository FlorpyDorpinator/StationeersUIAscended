namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// Marks a UIA-OWNED procedural HUD graphic that effect systems (FX materials, uv0
    /// restyling, mesh-effect walks) are allowed to touch. This is a HARD gate: anything
    /// NOT carrying this interface — borrowed vanilla graphics, <c>Image</c> icons,
    /// <c>RawImage</c>s — must never be restyled by an effect walk, or we corrupt objects
    /// we do not own and cannot hand back cleanly (adversarial review 2026-07-13). Presence
    /// of the interface IS the whole contract; effect code type-checks for this and nothing
    /// finer, so only the two hand-authored procedural graphics (Panel/Polyline) qualify.
    /// </summary>
    public interface IHudFxGraphic
    {
        /// <summary>Per-element effect strength, 0..1, baked into uv0.x on every vertex at
        /// mesh-rebuild time (NOT read per-frame). 0 (default) is inert: the stock UI
        /// material samples a white texture and never reads uv0, so a graphic renders
        /// byte-identically until an FX material that reads this channel is assigned.</summary>
        float FxStrength { get; set; }
    }
}
