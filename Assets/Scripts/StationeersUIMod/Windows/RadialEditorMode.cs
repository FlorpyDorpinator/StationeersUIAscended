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

        /// <summary>Palette entry names painting whatever the cursor is on right now —
        /// the F10 colour list highlights these so "which colour is that?" answers itself.</summary>
        public static readonly HashSet<string> HotPalette = new HashSet<string>();

        /// <summary>Changes whenever the hovered element changes; the colour list scrolls
        /// to the highlighted rows on change.</summary>
        public static string HotSignature { get; private set; } = "";

        private static Canvas _blackCanvas;
        private static Image _black;
        private static float _demoTemp = 21.5f;
        private static List<Sprite> _demoIcons;
        private static ParkingState _demoParking;

        public static void Enter()
        {
            Active = true;
            _demoIcons = null; // re-grab thumbnails from whatever the player carries now
            _demoParking = null;
        }

        public static void Exit()
        {
            Active = false;
            if (_blackCanvas != null) _blackCanvas.gameObject.SetActive(false);
            UnityRadialView.Hide();
            ParkedItemsView.Hide();
            _demoParking = null;
        }

        public static void Shutdown()
        {
            Active = false;
            if (_blackCanvas != null) Object.Destroy(_blackCanvas.gameObject);
            _blackCanvas = null;
            _black = null;
            _demoIcons = null;
            _demoParking = null;
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

            bool closeHovered = RadialMenu.InCloseButton(mouse - center, innerR);
            UnityRadialView.Render(
                center, innerR, outerR, entries, hovered, "Radial Editor",
                satCenter, satInner, satOuter, satEntries, satHovered, "Child radial",
                readout, "hover wedges to preview states", sticky: true,
                dragging: null, closeHovered: closeHovered);

            // A floating parked-item bubble so the chip size/colour edits are visible live.
            if (_demoParking == null)
            {
                var icons = DemoIcons();
                _demoParking = new ParkingState();
                _demoParking.Chips.Add(new ParkingState.Chip
                {
                    Icon = icons.Count > 0 ? icons[0] : null,
                    Name = "Item",
                    Pos = new Vector2(Screen.width * 0.74f, Screen.height * 0.72f),
                });
            }
            ParkedItemsView.Render(_demoParking, mouse);

            UpdateHotPalette(mouse, center, innerR, readout, closeHovered);
        }

        /// <summary>Point at an element, learn its colours: fill HotPalette with the entry
        /// names that paint whatever is under the cursor. Wedges highlight BOTH their rest
        /// and selected entries (pointing at a wedge is what selects it, so its rest state
        /// is never hoverable on its own).</summary>
        private static void UpdateHotPalette(Vector2 mouse, Vector2 center, float innerR,
            RadialEntry hoveredEntry, bool closeHovered)
        {
            HotPalette.Clear();

            if (closeHovered)
            {
                HotPalette.Add("HubCloseButton");
                HotPalette.Add("HubCloseButtonHover");
                HotPalette.Add("HubCloseText");
                HotPalette.Add("HubBorder");
            }
            else if (_demoParking != null && _demoParking.Chips.Count > 0
                && (mouse - _demoParking.Chips[0].Pos).magnitude <= ParkingState.ChipRadius + 8f)
            {
                // Chips draw with the selected-wedge fill and the wedge border as their rim.
                HotPalette.Add("WedgeSelected");
                HotPalette.Add("WedgeBorder");
            }
            else if (hoveredEntry != null)
            {
                if (!hoveredEntry.Enabled)
                {
                    HotPalette.Add("WedgeDisabled");
                    HotPalette.Add("TextDisabled");
                    HotPalette.Add("WedgeBorder");
                }
                else if (hoveredEntry.StowStyle)
                {
                    HotPalette.Add("WedgeStowTarget");
                    HotPalette.Add("WedgeStowTargetSelected");
                    HotPalette.Add("WedgeBorder");
                    HotPalette.Add("WedgeBorderSelected");
                }
                else if (hoveredEntry.GroupStyle)
                {
                    HotPalette.Add("GroupWedgeFill");
                    HotPalette.Add("GroupWedgeBorder");
                    HotPalette.Add("WedgeSelected");
                }
                else if (hoveredEntry.DeviceSlotStyle)
                {
                    HotPalette.Add("DeviceSlotBorderColor");
                    HotPalette.Add("WedgeBackground");
                    HotPalette.Add("WedgeSelected");
                    HotPalette.Add("TextPrimary");
                }
                else
                {
                    HotPalette.Add("WedgeBackground");
                    HotPalette.Add("WedgeSelected");
                    HotPalette.Add("WedgeBorder");
                    HotPalette.Add("WedgeBorderSelected");
                    HotPalette.Add("RimShine");
                }
                if (hoveredEntry.Enabled) HotPalette.Add("TextPrimary");
            }
            else if ((mouse - center).magnitude < innerR - 6f)
            {
                HotPalette.Add("HubFill");
                HotPalette.Add("HubBorder");
                HotPalette.Add("TextPrimary");
                HotPalette.Add("TextDim");
                HotPalette.Add("TextAccent");
            }

            HotSignature = HotPalette.Count == 0 ? "" : string.Join("|", HotPalette);
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
                    Sublabel = "in a device slot (blue edge)",
                    StateText = "5300<size=75%>kPa</size>",
                    Icon = icons.Count > 2 ? icons[2] : null,
                    DeviceSlotStyle = true,
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
                    Label = "Power Cells",
                    ActionText = "Open",
                    Sublabel = "a sorting-class group wedge",
                    Icon = icons.Count > 1 ? icons[1] : null,
                    GroupStyle = true,
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
