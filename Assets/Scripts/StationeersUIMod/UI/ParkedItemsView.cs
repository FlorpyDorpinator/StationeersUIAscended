using System.Collections.Generic;
using StationeersUIMod.Overlay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI
{
    /// <summary>
    /// Renders the Option A parking layer: items dragged off a radial sit on the screen in
    /// little shaded circles until they are dragged into a slot or dumped by the closing
    /// right-click. Also draws the ghost icon following the cursor during a drag.
    /// Purely visual — all state lives in ParkingState, all mutations in ItemActions.
    /// </summary>
    public static class ParkedItemsView
    {
        private static Canvas _canvas;
        private static readonly List<ChipVisual> _pool = new List<ChipVisual>();
        private static ChipVisual _ghost;

        private sealed class ChipVisual
        {
            public RectTransform Root;
            public CircleGraphic Circle;
            public Image Icon;
            public TextMeshProUGUI Name;
        }

        public static void Render(ParkingState parking, Vector2 mouseImGui)
        {
            if (parking == null || !parking.Active)
            {
                Hide();
                return;
            }
            EnsureCanvas();
            _canvas.gameObject.SetActive(true);

            while (_pool.Count < parking.Chips.Count)
                _pool.Add(MakeChip("Chip" + _pool.Count));

            for (int i = 0; i < _pool.Count; i++)
            {
                bool used = i < parking.Chips.Count;
                _pool[i].Root.gameObject.SetActive(used);
                if (!used) continue;
                var chip = parking.Chips[i];
                DressChip(_pool[i], chip, UnityRadialView.CanvasAnchoredPos(chip.Pos), ghost: false);
            }

            if (_ghost == null) _ghost = MakeChip("DragGhost");
            bool dragging = parking.Dragging != null;
            _ghost.Root.gameObject.SetActive(dragging);
            if (dragging)
            {
                // The ghost follows the cursor and must draw over every parked chip,
                // including ones pooled AFTER it was created.
                _ghost.Root.SetAsLastSibling();
                DressChip(_ghost, parking.Dragging, UnityRadialView.CanvasAnchoredPos(mouseImGui), ghost: true);
            }
        }

        public static void Hide()
        {
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }

        public static void Shutdown()
        {
            if (_canvas != null) Object.Destroy(_canvas.gameObject);
            _canvas = null;
            _pool.Clear();
            _ghost = null;
        }

        private static void DressChip(ChipVisual v, ParkingState.Chip chip, Vector2 pos, bool ghost)
        {
            v.Root.anchoredPosition = pos;

            // Blue shaded circle behind the icon (per the design ask), hub-style rim.
            Color fill = RadialPalette.WedgeHover.Value;
            fill.a = ghost ? 0.35f : 0.72f;
            v.Circle.color = fill;
            Color rim = RadialPalette.WedgeBorder.Value;
            rim.a *= ghost ? 0.6f : 1f;
            v.Circle.BorderColor = rim;
            v.Circle.BorderWidth = 2.2f;
            v.Circle.SetRadius(ParkingState.ChipRadius);
            v.Circle.Refresh();

            v.Icon.sprite = chip.Icon;
            v.Icon.enabled = chip.Icon != null;
            v.Icon.color = new Color(1f, 1f, 1f, ghost ? 0.85f : 1f);
            v.Icon.rectTransform.sizeDelta = Vector2.one * (ParkingState.ChipRadius * 1.5f);

            v.Name.gameObject.SetActive(!ghost && chip.Icon == null);
            if (v.Name.gameObject.activeSelf)
            {
                UnityRadialView.SyncFont(v.Name);
                v.Name.text = UnityRadialView.WedgeText(chip.Name);
            }
        }

        private static void EnsureCanvas()
        {
            if (_canvas != null) return;
            var go = new GameObject("UIAscended_ParkingCanvas");
            Object.DontDestroyOnLoad(go);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 5010; // above the radial rings; still under the ImGui modal layer
            var group = go.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
        }

        private static ChipVisual MakeChip(string name)
        {
            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(_canvas.transform, false);
            var rt = (RectTransform)root.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;

            var cgo = new GameObject("Circle", typeof(RectTransform));
            cgo.transform.SetParent(rt, false);
            var circle = cgo.AddComponent<CircleGraphic>();
            circle.raycastTarget = false;

            var igo = new GameObject("Icon", typeof(RectTransform));
            igo.transform.SetParent(rt, false);
            var icon = igo.AddComponent<Image>();
            icon.raycastTarget = false;
            icon.preserveAspect = true;

            var ngo = new GameObject("Name", typeof(RectTransform));
            ngo.transform.SetParent(rt, false);
            var tmp = ngo.AddComponent<TextMeshProUGUI>();
            tmp.font = UnityRadialView.Font();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 8f;
            tmp.fontSizeMax = 12f;
            tmp.raycastTarget = false;
            tmp.rectTransform.sizeDelta = new Vector2(ParkingState.ChipRadius * 1.8f, 30f);

            return new ChipVisual { Root = rt, Circle = circle, Icon = icon, Name = tmp };
        }
    }
}
