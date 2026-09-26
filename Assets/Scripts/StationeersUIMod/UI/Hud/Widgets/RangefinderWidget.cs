using System;
using System.Collections.Generic;
using System.Globalization;
using Assets.Scripts;          // CameraController (namespace Assets.Scripts — verified 27758 decompile)
using Assets.Scripts.Objects;  // Layers (namespace Assets.Scripts.Objects — verified 27758 decompile)
using TMPro;
using UnityEngine;

namespace StationeersUIMod.UI.Hud.Widgets
{
    /// <summary>
    /// D-019 (FlorpyDorp, origin request "a laser distance measuring tool"): a small instrument
    /// box reading the straight-line distance from the player camera's CROSSHAIR — dead-centre of
    /// the view, not the OS mouse cursor, which vanilla only frees while a menu/inventory is open
    /// (CursorManager.cs:510-515 locks/hides it during normal play) — to the first solid surface
    /// in front of the player. Pure LOCAL raycast: no networked read, no game-state mutation, so
    /// it is client-safe by construction and needs none of rule 3's MP funnel.
    ///
    /// RAYCAST CONVENTION (verified against the "Stationeers 8-1-26 V27758" decompile, the newest
    /// snapshot in Reference/StationeersGameVersions/):
    ///  - Origin/direction mirror vanilla's OWN camera-forward "what's in front of me" ray, used
    ///    for foot/hand IK targeting: <c>Human.GetIkTarget</c>
    ///    (Assembly-CSharp/Assets/Scripts/Objects/Entities/Human.cs:3329) casts from
    ///    <see cref="CameraController.CameraOrigin"/> (CameraController.cs:133 — in third person
    ///    this projects forward to roughly the character's own eye, so the ray never starts behind
    ///    the player's back and looks through their own model) along
    ///    <c>CurrentCamera.transform.forward</c> (CameraController.cs:148). That is exactly the
    ///    "crosshair ray" this element reads, and is deliberately NOT <c>Input.mousePosition</c> —
    ///    mouse-based rays (InputMouse.cs:291,381; CursorManager.cs:250; used by this mod's own
    ///    <c>WorldSlotCue</c>/<c>RadialMenu</c> world-hover raycasts) answer "what is my OS cursor
    ///    over", a different question while the cursor is unlocked for a UI.
    ///  - Layer mask is deliberately NOT vanilla's <c>CursorManager.CursorHitMask</c>
    ///    (CursorManager.cs:589) even though that is what this mod's own interaction raycasts
    ///    reuse elsewhere: that mask is curated for HAND INTERACTION range (3 m,
    ///    <c>CursorManager.MaxInteractDistance</c>, CursorManager.cs:567) and is proven to miss
    ///    plain terrain/rock by default — the mining-voxel collider it ORs in while a mining tool
    ///    is held (HumanHandsBehaviour.cs:99, MiningDrill.cs:101/199) is a TRIGGER-only synthetic
    ///    3x3x3 targeting helper (CursorVoxel.cs:64-84), not a real surface, and is absent the rest
    ///    of the time. A rangefinder must read ANY solid surface at ANY range, so it builds its own
    ///    mask: everything except "Ignore Raycast", "Player" and "PlayerImmune"
    ///    (Assembly-CSharp/Assets/Scripts/Objects/Layers.cs:16,19,28). "Player" is the layer every
    ///    <see cref="Assets.Scripts.Objects.Entities.Human"/>'s own colliders sit on (Human.cs:187's
    ///    <c>LayerPlayer</c>), so excluding it satisfies "exclude the player's own colliders" BY
    ///    CONSTRUCTION — for every human, not just the local one — rather than an identity check
    ///    after the fact. "PlayerImmune" is where the LOCAL movement body lives
    ///    (MovementController.cs:113); vanilla's own camera-forward ray excludes it too
    ///    (Human.GetIkTarget's AllButPlayerImmune, Human.cs:3329), so a third-person / looking-down
    ///    reading never measures your own body. This is a
    ///    deliberate simplification (see the Changes Report) over the
    ///    alternative of only excluding the LOCAL human and letting the laser range OTHER players;
    ///    for a first pass it is simpler, cheaper, and errs on the side never reading a bogus
    ///    near-zero number off your own hitbox.
    ///  - <see cref="QueryTriggerInteraction.Ignore"/> is passed explicitly, matching vanilla's own
    ///    surface-only queries (<c>Human.CheckCapsule</c>, Human.cs:3323; the audio occlusion ray in
    ///    GameAudioSource.cs:1016) — otherwise gas/interaction trigger volumes could register as
    ///    "surfaces".
    ///
    /// PERFORMANCE. The raycast itself is throttled to <see cref="SampleInterval"/> (~10 Hz) using
    /// the same "Time.unscaledTime >= next check" idiom <c>HudSampler</c> uses for its world-name
    /// refresh (HudSampler.cs:636-639) — cheap, but there is no reason to pay for it 60x/second.
    /// The formatted display string is separately dirty-guarded (rebuilt only when the rounded,
    /// unit-converted value actually changes), matching <c>ReadoutWidget</c>'s value-string cache.
    /// Neither path allocates on a frame where nothing changed.
    /// </summary>
    internal sealed class RangefinderWidget : HudElementView
    {
        private const float SampleInterval = 0.1f; // ~10 Hz, per the assignment's throttle spec
        private const float DefaultMaxRange = 200f;
        private const float MinMaxRange = 10f;
        private const float MaxMaxRange = 500f;
        private const float MetersToFeet = 3.28084f;

        private static readonly string[] UnitNames = { "Meters", "Feet" };

        private PanelGraphic _box;
        private TextMeshProUGUI _label;
        private TextMeshProUGUI _value;

        // Raycast throttle + cache: the sample only runs at SampleInterval; every other frame
        // just re-renders whatever it last found.
        private float _nextSampleAt;
        private bool _hasReading;
        private float _lastMeters;

        // Display-string cache: rebuilt only when the rounded/unit-converted shown value,
        // validity, or unit actually changes — never a per-frame string allocation.
        private bool _shownValid;
        private float _shownRounded;
        private bool _shownFeet;
        private string _valueText = "--";

        // A small boxed instrument, same family as Readout/Compass: the backing panel takes the
        // trapezoid insets and the full glass/border/corner treatment.
        protected override bool SupportsTrapezoid => true;
        protected override bool SupportsPanelAppearance => true;

        // The box is optional ("box" key, default ON) — surface the same "effects are saved but
        // invisible" hint Readout/Compass/VitalsPanel give when their background is toggled off.
        protected override bool OptionalPanelBackgroundIsOff => !Def.GetBFor(EditBare(Def), "box", true);

        protected override void BuildContent(RectTransform root)
        {
            _box = MakePanel(root, "Box");
            _label = HudText.Make(root, "Label", 11f, TextAlignmentOptions.Center);
            _value = HudText.Make(root, "Value", 17f, TextAlignmentOptions.Center);
        }

        public override void Layout(float scale)
        {
            Root.anchoredPosition = Vector2.zero;
            var c = CenterFor(scale);
            var s = SizeFor(scale);

            ((RectTransform)_box.transform).anchoredPosition = c;
            _box.SetShape(s.x, s.y, RadiusTL(), RadiusTR(), RadiusBR(), RadiusBL(),
                InsetTop(scale), InsetBottom(scale));

            // Small label near the top, big value below it — the same two-line stack Readout's
            // "stack" mode uses, minus the bar/target rows this instrument has no use for.
            float pad = 4f * scale;
            _label.rectTransform.anchoredPosition = new Vector2(c.x, c.y + s.y * 0.22f);
            _label.rectTransform.sizeDelta = new Vector2(Mathf.Max(10f, s.x - pad * 2f), s.y * 0.4f);
            _value.rectTransform.anchoredPosition = new Vector2(c.x, c.y - s.y * 0.12f);
            _value.rectTransform.sizeDelta = new Vector2(Mathf.Max(10f, s.x - pad * 2f), s.y * 0.52f);
        }

        public override void UpdatePanel(HudSnapshot snap, float scale)
        {
            bool showBox = Def.GetBFor(LayoutBare, "box", true);
            _box.enabled = showBox;
            if (showBox)
            {
                _box.color = FillColor();
                _box.BorderColor = BorderColor();
                _box.BorderWidth = BorderWidthFor();
                ApplyGlass(_box);
            }

            if (Time.unscaledTime >= _nextSampleAt)
            {
                _nextSampleAt = Time.unscaledTime + SampleInterval;
                float maxRange = Mathf.Clamp(Def.GetF("maxRange", DefaultMaxRange), MinMaxRange, MaxMaxRange);
                float meters;
                _hasReading = TryMeasure(maxRange, out meters);
                _lastMeters = meters;
            }

            bool feet = IsFeet(Def);
            float shown = _hasReading ? (feet ? _lastMeters * MetersToFeet : _lastMeters) : 0f;
            // Quantized to one decimal (matches the "12.4 m" / "40.7 ft" format), rounded not
            // truncated, and the string only rebuilds when this rounded value actually moves.
            float rounded = _hasReading ? Mathf.Round(shown * 10f) * 0.1f : 0f;
            if (_hasReading != _shownValid || !Mathf.Approximately(rounded, _shownRounded) || feet != _shownFeet)
            {
                _shownValid = _hasReading; _shownRounded = rounded; _shownFeet = feet;
                _valueText = _hasReading
                    ? rounded.ToString("0.0", CultureInfo.InvariantCulture) + (feet ? " ft" : " m")
                    : "--";
            }

            string lbl = Def.GetS("label", "");
            if (string.IsNullOrEmpty(lbl)) lbl = "RANGE";

            HudText.Sync(_label);
            HudText.Sync(_value);
            float valueSize = HudText.Size(17f * Def.FontScaleFor(LayoutBare)) * scale;
            _value.fontSize = valueSize;
            _label.fontSize = HudText.Size(11f * Def.FontScaleFor(LayoutBare)) * scale;
            _label.color = GlobalOr(Def.GetSFor(LayoutBare, "labelColor", ""), HudPalette.TextLabel.Value);
            _value.color = TextColor();
            HudText.Set(_label, lbl);
            HudText.Set(_value, _valueText);
        }

        /// <summary>Vanilla's own camera-forward look-ray (see the class doc for full decompile
        /// citations), capped at <paramref name="maxRange"/> metres and restricted to real,
        /// non-trigger surfaces on any layer except "Ignore Raycast", "Player" and "PlayerImmune". Wrapped in
        /// try/catch and returns false on any failure (camera/physics can be momentarily absent
        /// during a scene transition) — this must degrade to "--", never throw into HudSystem's
        /// per-panel update loop (rule 5, fail soft).</summary>
        private static bool TryMeasure(float maxRange, out float meters)
        {
            meters = 0f;
            try
            {
                var cam = CameraController.CurrentCamera;
                if (cam == null) return false;

                int mask = ~0;
                int ignoreRaycast = Layers.IgnoreRaycast;
                int player = Layers.Player;
                // B7: the LOCAL movement body sits on "PlayerImmune", not "Player" —
                // MovementController.Start puts its own GameObject there (27758
                // MovementController.cs:113; the layer id is Layers.PlayerImmune =
                // LayerMask.NameToLayer("PlayerImmune"), Layers.cs:19, resolved at runtime like the
                // other two — -1 if a build ever drops the layer, which the >= 0 guard skips).
                // Vanilla's own camera-forward look-ray, the one this widget mirrors, excludes it too
                // (Human.GetIkTarget casts with AllButPlayerImmune, Human.cs:3329). Without it a
                // third-person or looking-down reading could measure your own body.
                int playerImmune = Layers.PlayerImmune;
                if (ignoreRaycast >= 0) mask &= ~(1 << ignoreRaycast);
                if (player >= 0) mask &= ~(1 << player);
                if (playerImmune >= 0) mask &= ~(1 << playerImmune);

                RaycastHit hit;
                bool didHit = Physics.Raycast(CameraController.CameraOrigin, cam.transform.forward,
                    out hit, maxRange, mask, QueryTriggerInteraction.Ignore);
                if (!didHit) return false;
                meters = hit.distance;
                return true;
            }
            catch { return false; }
        }

        private static bool IsFeet(HudElementDef d)
            => string.Equals(d.GetS("unit", ""), "ft", StringComparison.OrdinalIgnoreCase);

        public override void DescribeProps(List<HudProp> into)
        {
            base.DescribeProps(into);
            var d = Def;

            // Content/identity params — shared across tiers, like Readout's "src": a bare/suited/
            // robot HUD showing the SAME instrument in different units per tier would just be
            // confusing, so these deliberately do NOT go through the *For per-tier accessors.
            into.Add(HudProp.Enum("Units", () => IsFeet(d) ? 1 : 0,
                v => d.Set("unit", v == 1 ? "ft" : null), UnitNames));
            into.Add(HudProp.F("Max range (m)", () => d.GetF("maxRange", DefaultMaxRange),
                v => d.SetF("maxRange", Mathf.Clamp(v, MinMaxRange, MaxMaxRange)), MinMaxRange, MaxMaxRange));
            into.Add(HudProp.Text("Label override", () => d.GetS("label", ""), v => d.Set("label", Empty(v))));

            // Appearance — per-tier forkable (rule 8), matching Readout/Compass's own box+colour rows.
            int appearanceStart = into.Count;
            into.Add(HudProp.Bool("Background box", () => d.GetBFor(EditBare(d), "box", true),
                v => d.SetBFor(EditBare(d), "box", v)));
            into.Add(HudProp.Color("Label colour", () => d.GetSFor(EditBare(d), "labelColor", ""),
                v => d.SetSFor(EditBare(d), "labelColor", Empty(v)), () => HudPalette.TextLabel.Value));
            for (int i = appearanceStart; i < into.Count; i++) into[i].Group = HudPropGroup.Appearance;
        }

        private static string Empty(string v) => string.IsNullOrEmpty(v) ? null : v;
    }
}
