using System.Collections.Generic;
using System.Linq;
using StationeersUIMod.Core;
using StationeersUIMod.Overlay;
using StationeersUIMod.UI;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.Windows
{
    /// <summary>
    /// The radial EDITOR: the screen goes black, an example main radial and an example
    /// child radial appear with a wedge in every state (normal, hovered — live, follow the
    /// mouse — disabled, STOW, branch, scroll-value), and the F10 window turns into the
    /// full control panel (colors, font, sizes, thickness, gap, feather, shading).
    /// Everything renders through the REAL UnityRadialView, so what you see is exactly
    /// what the game shows.
    ///
    /// No modal of its own: the F10 settings window (a game ImGui window) already owns
    /// cursor unlock and key capture while it is open; closing it (Escape) exits the
    /// editor automatically.
    /// </summary>
    public static class RadialEditorMode
    {
        public static bool Active { get; private set; }

        private static Canvas _blackCanvas;
        private static Image _black;
        private static float _demoTemp = 21.5f;
        private static List<Sprite> _demoIcons;

        public static void Enter()
        {
            Active = true;
            _demoIcons = null; // re-grab thumbnails from whatever the player carries now
        }

        public static void Exit()
        {
            Active = false;
            if (_blackCanvas != null) _blackCanvas.gameObject.SetActive(false);
            UnityRadialView.Hide();
        }

        public static void Shutdown()
        {
            Active = false;
            if (_blackCanvas != null) Object.Destroy(_blackCanvas.gameObject);
            _blackCanvas = null;
            _black = null;
            _demoIcons = null;
        }

        /// <summary>Per-frame while active, called from the ImGui draw hook.</summary>
        public static void Draw()
        {
            if (!Active) return;
            EnsureBackdrop();
            _blackCanvas.gameObject.SetActive(true);

            float outerR = UIAConfig.RadialOuterRadius.Value;
            float innerR = Mathf.Clamp(UIAConfig.RadialInnerRadius.Value, 104f, Mathf.Max(104f, outerR - 30f));
            var center = new Vector2(Screen.width * 0.32f, Screen.height * 0.52f);

            float satScale = UIAConfig.RadialSatelliteScale.Value;
            float satOuter = Mathf.Clamp(outerR * 0.55f, 130f, 220f) * satScale;
            float satInner = satOuter * 0.34f;
            var satCenter = new Vector2(Screen.width * 0.32f + outerR + satOuter * 0.75f + 16f,
                                        Screen.height * 0.40f);

            var entries = BuildDemoEntries();
            var satEntries = BuildDemoSatellite();

            var mouse = DrawUtil.MousePos();
            int hovered = HoverIndex(mouse, center, innerR, outerR, entries.Count);
            int satHovered = HoverIndex(mouse, satCenter, satInner, satOuter, satEntries.Count);
            if (satHovered >= 0) hovered = -1;

            RadialEntry readout = satHovered >= 0 ? satEntries[satHovered]
                                : hovered >= 0 ? entries[hovered]
                                : null;

            // The scroll-wheel demo: the editor has no RadialMenu pumping OnScroll for it,
            // so route the wheel to the hovered wedge here (the F10 window keeps priority).
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f && readout?.OnScroll != null)
            {
                bool imguiOwnsMouse = false;
                try { imguiOwnsMouse = ImGuiNET.ImGui.GetIO().WantCaptureMouse; } catch { }
                if (!imguiOwnsMouse) readout.OnScroll(wheel > 0f ? 1 : -1);
            }

            UnityRadialView.Render(
                center, innerR, outerR, entries, hovered, "Radial Editor",
                satCenter, satInner, satOuter, satEntries, satHovered, "Child radial",
                readout, "hover wedges to preview states", sticky: true);
        }

        // ---------- demo data ----------

        private static List<RadialEntry> BuildDemoEntries()
        {
            var icons = DemoIcons();
            var list = new List<RadialEntry>
            {
                new RadialEntry
                {
                    Label = "Item",
                    ActionText = "Take to hand",
                    Sublabel = "a normal wedge",
                    Icon = icons.Count > 0 ? icons[0] : null,
                },
                new RadialEntry
                {
                    Label = "Battery",
                    ActionText = "Take to hand",
                    Sublabel = "state text under the icon",
                    StateText = "87<size=75%>%</size>",
                    Icon = icons.Count > 1 ? icons[1] : null,
                },
                new RadialEntry
                {
                    Label = "Canister",
                    ActionText = "Take to hand",
                    Sublabel = "pressure readout",
                    StateText = "5300<size=75%>kPa</size>",
                    Icon = icons.Count > 2 ? icons[2] : null,
                },
                new RadialEntry
                {
                    Label = "Disabled",
                    Enabled = false,
                    DisabledReason = "Held item doesn't fit",
                },
                new RadialEntry
                {
                    Label = "Stow",
                    ActionText = "Stow held item",
                    Sublabel = "orange on hover",
                    StowStyle = true,
                    HoverIcon = icons.Count > 0 ? icons[0] : null,
                },
                new RadialEntry
                {
                    Label = "Open",
                    ActionText = "Open",
                    Sublabel = "a branch wedge",
                    ChildProvider = () => new List<RadialEntry>(),
                },
                new RadialEntry
                {
                    Label = "Temperature",
                    ActionText = "Scroll to adjust",
                    Sublabel = "mouse wheel demo",
                    ValueText = () => _demoTemp.ToString("0.0") + "°C",
                    OnScroll = d => _demoTemp = Mathf.Clamp(_demoTemp + d * 0.5f, -20f, 60f),
                    OnSelect = () => { },
                },
            };
            return list;
        }

        private static List<RadialEntry> BuildDemoSatellite()
        {
            var icons = DemoIcons();
            return new List<RadialEntry>
            {
                new RadialEntry { Label = "Take", ActionText = "Take to hand", Icon = icons.Count > 0 ? icons[0] : null },
                new RadialEntry { Label = "Open", ActionText = "Open", ChildProvider = () => new List<RadialEntry>() },
                new RadialEntry { Label = "Stow", ActionText = "Stow held item", StowStyle = true },
            };
        }

        /// <summary>Real thumbnails from whatever the player carries, so the preview icons
        /// look like the game (falls back to text-only wedges when empty-handed).</summary>
        private static List<Sprite> DemoIcons()
        {
            if (_demoIcons != null) return _demoIcons;
            _demoIcons = new List<Sprite>();
            try
            {
                foreach (var s in InventoryScanner.Scan(2, true).Where(s => s.Occupant != null))
                {
                    Sprite icon = null;
                    try { icon = s.Occupant.GetThumbnail(); } catch { }
                    if (icon != null && !_demoIcons.Contains(icon)) _demoIcons.Add(icon);
                    if (_demoIcons.Count >= 3) break;
                }
            }
            catch { }
            return _demoIcons;
        }

        private static int HoverIndex(Vector2 mouse, Vector2 center, float innerR, float outerR, int count)
        {
            if (count <= 0) return -1;
            var delta = mouse - center;
            float dist = delta.magnitude;
            if (dist < innerR * 0.9f || dist > outerR + 40f) return -1;
            float angle = Mathf.Atan2(delta.y, delta.x);
            float sector = Mathf.PI * 2f / count;
            float rel = DrawUtil.NormalizeAngle(angle + Mathf.PI * 0.5f + sector * 0.5f);
            int idx = Mathf.FloorToInt(rel / sector);
            return idx >= count ? count - 1 : idx;
        }

        private static void EnsureBackdrop()
        {
            if (_blackCanvas != null) return;
            var go = new GameObject("UIAscended_EditorBackdrop");
            Object.DontDestroyOnLoad(go);
            _blackCanvas = go.AddComponent<Canvas>();
            _blackCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _blackCanvas.sortingOrder = 4900; // under the radial canvas (5000)

            var igo = new GameObject("Black", typeof(RectTransform));
            igo.transform.SetParent(go.transform, false);
            _black = igo.AddComponent<Image>();
            _black.color = Color.black;
            _black.raycastTarget = false;
            var rt = _black.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
