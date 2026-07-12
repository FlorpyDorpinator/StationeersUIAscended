using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// Draws a single <see cref="HudGlyphs"/> glyph as a baked-AA vector icon that fills its
    /// RectTransform's short side. Same dirty-on-change discipline as PanelGraphic: this can
    /// live on an always-on-screen HUD element, so only real changes may trigger a rebuild.
    ///
    /// The creator attaches a VisorWarp alongside if the icon must bend with the visor — this
    /// class never adds one itself.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class HudIconGraphic : MaskableGraphic
    {
        private HudIconKind _kind = HudIconKind.None;
        private float _strokeScale = 1f;

        public HudIconKind Kind
        {
            get => _kind;
            set { if (_kind != value) { _kind = value; SetVerticesDirty(); } }
        }

        /// <summary>Multiplier on the derived stroke width (px = size * 0.09 * this, floored
        /// at 1.1px so a glyph never thins into invisibility on a small icon).</summary>
        public float StrokeScale
        {
            get => _strokeScale;
            set { if (!Mathf.Approximately(_strokeScale, value)) { _strokeScale = value; SetVerticesDirty(); } }
        }

        public void Refresh() => SetVerticesDirty();

        private static float Feather => HudConfig.EdgeFeather != null
            ? HudConfig.EdgeFeather.Value : 1.25f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_kind == HudIconKind.None) return;

            Rect r = rectTransform.rect;
            float size = Mathf.Min(r.width, r.height);
            if (size < 1f) return;

            float stroke = Mathf.Max(1.1f, size * 0.09f * _strokeScale);
            HudGlyphs.Emit(vh, _kind, r.center, size, stroke, color, Feather);
        }
    }
}
