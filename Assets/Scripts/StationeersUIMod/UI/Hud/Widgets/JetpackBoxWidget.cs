using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud.Widgets
{
    /// <summary>
    /// Vanilla's jetpack info box, restyled: a "JETPACK" title over a "THRUST NNN" line,
    /// with the propellant delta ("NNNN kPa") on its own row preceded by vanilla's green
    /// canister icon. Every value comes from the frame snapshot; the kPa value is coloured
    /// by the same low/crit bands the vanilla panel alarms on, so it reads identically.
    ///
    /// Electric rigs (or a gas rig with no canister mounted) show "--" for kPa — there is
    /// no canister-vs-world delta to report. When no jetpack is worn the whole box renders
    /// dim/blank; guards keep it from throwing so tier/Def hiding stays the real off-switch.
    /// </summary>
    internal sealed class JetpackBoxWidget : HudElementView
    {
        private PanelGraphic _box;
        private Image _canister;
        private RectTransform _canisterRt;
        private TextMeshProUGUI _title, _thrust, _prop;
        private RectTransform _titleRt, _thrustRt, _propRt;

        // Display-string caches: rebuilt only when the rounded int / validity changes, so an
        // always-on box never allocates a string per frame.
        private int _shownThrust = int.MinValue;
        private bool _shownThrustOn;
        private string _thrustText = "THRUST --";
        private int _shownProp = int.MinValue;
        private bool _shownPropOn;
        private string _propText = "--";

        protected override void BuildContent(RectTransform root)
        {
            _box = MakePanel(root, "Box");

            // Vanilla's green canister sprite. Native colour (white multiply), and it
            // late-resolves — the game singleton exists only in-world — so UpdatePanel
            // retries the fetch each frame until it lands.
            _canister = MakeIcon(root, "Canister");
            _canister.enabled = false;
            _canisterRt = _canister.rectTransform;

            _title = HudText.Make(root, "Title", 12f, TextAlignmentOptions.Center);
            _thrust = HudText.Make(root, "Thrust", 14f, TextAlignmentOptions.Center);
            _prop = HudText.Make(root, "Prop", 14f, TextAlignmentOptions.MidlineLeft);
            _titleRt = _title.rectTransform;
            _thrustRt = _thrust.rectTransform;
            _propRt = _prop.rectTransform;
        }

        public override void Layout(float scale)
        {
            Root.anchoredPosition = Vector2.zero;
            var c = CenterFor(scale);
            var s = SizeFor(scale);

            ((RectTransform)_box.transform).anchoredPosition = c;
            _box.SetShape(s.x, s.y,
                Radius(Def.RTL), Radius(Def.RTR), Radius(Def.RBR), Radius(Def.RBL));

            float pad = 6f * scale;
            float left = c.x - s.x * 0.5f + pad;
            float right = c.x + s.x * 0.5f - pad;
            float topEdge = c.y + s.y * 0.5f - pad;
            float botEdge = c.y - s.y * 0.5f + pad;
            float cx = (left + right) * 0.5f;
            float cw = right - left;

            // Three stacked rows: title / thrust / propellant+icon.
            float titleY = Mathf.Lerp(botEdge, topEdge, 0.82f);
            float thrustY = Mathf.Lerp(botEdge, topEdge, 0.50f);
            float propY = Mathf.Lerp(botEdge, topEdge, 0.16f);

            _titleRt.anchoredPosition = new Vector2(cx, titleY);
            _titleRt.sizeDelta = new Vector2(cw, 16f * scale);
            _thrustRt.anchoredPosition = new Vector2(cx, thrustY);
            _thrustRt.sizeDelta = new Vector2(cw, 20f * scale);

            // Propellant row: canister glyph pinned to the left, value text filling the rest.
            float isz = Mathf.Clamp((topEdge - botEdge) * 0.28f, 10f * scale, 24f * scale);
            _canisterRt.anchoredPosition = new Vector2(left + isz * 0.5f, propY);
            _canisterRt.sizeDelta = new Vector2(isz, isz);

            float textLeft = left + isz + 4f * scale;
            float tw = Mathf.Max(20f, right - textLeft);
            _propRt.anchoredPosition = new Vector2(textLeft + tw * 0.5f, propY);
            _propRt.sizeDelta = new Vector2(tw, 20f * scale);
        }

        public override void UpdatePanel(HudSnapshot s, float scale)
        {
            // --- chrome ---
            bool showBox = Def.GetB("box", true);
            _box.enabled = showBox;
            if (showBox)
            {
                _box.color = FillColor();
                _box.BorderColor = BorderColor();
                _box.BorderWidth = BorderWidthFor();
                ApplyGlass(_box);
            }

            var accent = TextColor();
            bool present = false;
            bool gas = false;
            int thrust = 0;
            int kpa = 0;
            bool crit = false, low = false;
            try
            {
                present = s.JetpackPresent;
                thrust = (int)s.JetpackThrustPct;
                gas = present && s.JetpackIsGas && s.JetpackPropellantDeltaKPa >= 0f;
                kpa = (int)s.JetpackPropellantDeltaKPa;
                crit = s.JetpackCrit;
                low = s.JetpackLow;
            }
            catch { present = false; gas = false; }

            // --- canister icon (late-resolve, native colour) ---
            if (present)
            {
                if (_canister.sprite == null)
                {
                    var late = Core.VanillaIcons.JetpackCanister();
                    _canister.sprite = late;
                }
                _canister.enabled = _canister.sprite != null && gas;
                _canister.color = Color.white;
            }
            else
            {
                _canister.enabled = false;
            }

            // --- thrust line (rebuild only when the rounded value / presence changes) ---
            if (present != _shownThrustOn || (present && thrust != _shownThrust))
            {
                _shownThrustOn = present; _shownThrust = thrust;
                _thrustText = "THRUST " + (present
                    ? thrust.ToString("0", CultureInfo.InvariantCulture)
                    : "--");
            }

            // --- propellant value ---
            if (gas != _shownPropOn || (gas && kpa != _shownProp))
            {
                _shownPropOn = gas; _shownProp = kpa;
                _propText = gas
                    ? kpa.ToString("0", CultureInfo.InvariantCulture) + Small(" kPa")
                    : "--";
            }

            Color propCol = !present ? HudPalette.TextDim.Value
                : crit ? HudPalette.Critical.Value
                : low ? HudPalette.Warn.Value
                : HudPalette.TextValue.Value;

            float vs = Def.GetF("valueSize", 14f) * Def.FontScale;
            HudText.Sync(_title); HudText.Sync(_thrust); HudText.Sync(_prop);
            _title.fontSize = HudText.Size(vs * 0.82f) * scale;
            _thrust.fontSize = HudText.Size(vs) * scale;
            _prop.fontSize = HudText.Size(vs) * scale;
            _title.color = HudPalette.TextLabel.Value;
            _thrust.color = present ? accent : HudPalette.TextDim.Value;
            _prop.color = propCol;
            HudText.Set(_title, "JETPACK");
            HudText.Set(_thrust, _thrustText);
            HudText.Set(_prop, _propText);
        }

        private static string Small(string unit) => "<size=62%>" + unit + "</size>";

        public override void DescribeProps(List<HudProp> into)
        {
            base.DescribeProps(into);
            var d = Def;
            into.Add(HudProp.Bool("Background box", () => d.GetB("box", true), v => d.SetB("box", v)));
            into.Add(HudProp.F("Value size", () => d.GetF("valueSize", 14f), v => d.SetF("valueSize", v), 8f, 48f));
        }
    }
}
