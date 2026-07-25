using System.Collections.Generic;
using StationeersUIMod.Core;
using StationeersUIMod.Features;
using StationeersUIMod.UI.Hud;
using StationeersUIMod.UI.Menu.Kit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Tabs
{
    /// <summary>The front door: pick a shipped "default UI" as a card (with preview art + its own
    /// font), or any profile from the dropdown. Advanced adds authoring shortcuts (Save as,
    /// Duplicate, the F9 Designer, the profiles folder).</summary>
    public sealed class ProfilesTab : IUiaTab
    {
        public string Title => "Profiles";

        // The default UIs we want front-and-center, in order. Only the ones that actually exist
        // on disk become cards; the rest of the list falls through to the dropdown. Curated to the
        // shipped set (2026-07-24) — Stationeers Blue is the default, Pure HUD the green alternate.
        private static readonly string[] Featured =
            { "Stationeers Blue", "Pure HUD" };

        public void Build(RectTransform content, bool advanced)
        {
            ScrollRect scroll;
            var col = UiaUi.ScrollView(content, out scroll, UiaTheme.Gap);
            UiaUi.Fill((RectTransform)scroll.gameObject.transform);

            UiaControls.Header(col, "Choose your UI");
            UiaControls.Note(col, "Pick a layout below, or use the two switches up top to run just the radial half or just the HUD half. Your choice applies to the visor HUD instantly.");

            var all = HudProfileStore.ListProfiles();
            string active = HudConfig.HudActiveProfile != null ? HudConfig.HudActiveProfile.Value : null;

            // Featured cards grid.
            var featured = PickFeatured(all);
            var gridGo = UiaUi.Go("card-grid", col);
            var grid = gridGo.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(298f, 158f);
            grid.spacing = new Vector2(UiaTheme.Gap, UiaTheme.Gap);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            var gridFit = gridGo.AddComponent<ContentSizeFitter>();
            gridFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            foreach (var name in featured)
                Card(gridGo.transform, name, string.Equals(name, active, System.StringComparison.OrdinalIgnoreCase));

            UiaUi.Go("spacer1", col).AddComponent<LayoutElement>().preferredHeight = 6f;
            UiaControls.Header(col, "All profiles");

            int activeIdx = all.IndexOf(active ?? "");
            UiaControls.DropdownRow(col, "Active profile", all, activeIdx < 0 ? 0 : activeIdx,
                i => { if (i >= 0 && i < all.Count) Apply(all[i]); });

            // Per-profile font.
            var fonts = new List<string> { "(inherit global font)" };
            fonts.AddRange(HudText.AllFontNames());
            var doc = HudProfileStore.Active;
            int fontIdx = 0;
            if (doc != null && !string.IsNullOrEmpty(doc.Font))
            {
                int f = fonts.IndexOf(doc.Font);
                fontIdx = f >= 0 ? f : 0;
            }
            UiaControls.DropdownRow(col, "Font (this profile)", fonts, fontIdx, i =>
            {
                var d = HudProfileStore.Active;
                if (d == null) return;
                d.Font = i <= 0 ? null : fonts[i];
                HudProfileStore.MarkChanged();
            });

            if (advanced)
            {
                UiaUi.Go("spacer2", col).AddComponent<LayoutElement>().preferredHeight = 6f;
                UiaControls.Header(col, "Author (advanced)");
                var rowGo = UiaUi.Go("adv-row", col);
                UiaUi.Size(rowGo, UiaTheme.RowH);
                UiaUi.HLayout((RectTransform)rowGo.transform, UiaTheme.Gap);
                UiaControls.Button(rowGo.transform, "Duplicate active", DuplicateActive, 160f, UiaTheme.RowH);
                UiaControls.Button(rowGo.transform, "Open HUD Designer (F9)", OpenDesigner, 200f, UiaTheme.RowH, UiaControls.ButtonStyle.Primary);
                UiaControls.Button(rowGo.transform, "Open profiles folder", OpenFolder, 180f, UiaTheme.RowH);
                UiaControls.Note(col, "The HUD Designer (F9) is where you build your own layout - add, move, resize and restyle every element. Save it there and it appears here as a profile.");
            }
        }

        private static List<string> PickFeatured(List<string> all)
        {
            var result = new List<string>();
            foreach (var f in Featured)
                foreach (var a in all)
                    if (string.Equals(a, f, System.StringComparison.OrdinalIgnoreCase) && !result.Contains(a))
                        result.Add(a);
            for (int i = 0; i < all.Count && result.Count < 6; i++)
                if (!result.Contains(all[i])) result.Add(all[i]);
            return result;
        }

        private void Card(Transform parent, string name, bool active)
        {
            HudDocument doc = HudProfileStore.Load(name);

            var card = UiaUi.Go("card", parent);
            var cimg = card.AddComponent<Image>();
            cimg.color = active ? UiaTheme.SelectedDim : UiaTheme.PanelRaised;
            UiaUi.OutlineOf(cimg, active ? UiaTheme.Selected : UiaTheme.Divider, active ? 2f : 1f);
            UiaUi.VLayout((RectTransform)card.transform, 3f, 8, 8, 8, 8);

            // Preview image (or a labelled placeholder).
            var preGo = UiaUi.Go("preview", card.transform);
            UiaUi.Size(preGo, 84f);
            var preImg = preGo.AddComponent<Image>();
            var sprite = UiaImages.Load(HudProfileStore.PreviewPath(name));
            if (sprite != null)
            {
                preImg.sprite = sprite; preImg.color = Color.white; preImg.preserveAspect = true; preImg.raycastTarget = false;
            }
            else
            {
                preImg.color = new Color(0.05f, 0.08f, 0.11f, 1f); preImg.raycastTarget = false;
                var ph = UiaUi.Text(preGo.transform, "PREVIEW\nComing soon", 11f, UiaTheme.TextMute, TextAlignmentOptions.Center, true);
                UiaUi.Fill((RectTransform)ph.transform);
            }

            var nameGo = UiaUi.Go("name", card.transform);
            UiaUi.Size(nameGo, 22f);
            var nameT = nameGo.AddComponent<TextMeshProUGUI>();
            nameT.font = UiaTheme.Font(); nameT.fontSize = 16f; nameT.raycastTarget = false;
            nameT.color = active ? UiaTheme.Selected : UiaTheme.Text; nameT.alignment = TextAlignmentOptions.Left;
            nameT.text = name; nameT.overflowMode = TextOverflowModes.Ellipsis; nameT.enableWordWrapping = false;

            string sub = active ? "ACTIVE" : (doc != null && !string.IsNullOrEmpty(doc.Description) ? doc.Description : "Click to apply");
            var subGo = UiaUi.Go("sub", card.transform);
            UiaUi.Size(subGo, 18f);
            var subT = subGo.AddComponent<TextMeshProUGUI>();
            subT.font = UiaTheme.Font(); subT.fontSize = 12f; subT.raycastTarget = false;
            subT.color = active ? UiaTheme.On : UiaTheme.TextMute; subT.alignment = TextAlignmentOptions.Left;
            subT.text = sub; subT.overflowMode = TextOverflowModes.Ellipsis; subT.enableWordWrapping = false;

            // Whole card applies on click (drag still scrolls — click handler only).
            card.AddComponent<UiaControls.UiaButton>()
                .Init(cimg, cimg.color, active ? UiaTheme.SelectedDim : UiaTheme.PanelHover,
                    active ? UiaTheme.SelectedDim : UiaTheme.PanelHover)
                .OnClick = () => { if (!active) Apply(name); };
        }

        private void Apply(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            try
            {
                if (HudConfig.HudActiveProfile != null) HudConfig.HudActiveProfile.Value = name;
                var keep = HudProfileStore.Active;
                HudProfileStore.LoadActive(name, () => keep != null ? keep.Clone() : new HudDocument { Name = name });
                HudDocumentHistory.Clear();
            }
            catch (System.Exception e) { UIALog.Warn("Apply profile failed: " + e.Message); }
            UiaControlCenter.Refresh();
        }

        private void DuplicateActive()
        {
            var doc = HudProfileStore.Active;
            if (doc == null) return;
            string baseName = (doc.Name ?? "Profile") + " copy";
            string name = baseName;
            var existing = HudProfileStore.ListProfiles();
            int n = 2;
            while (existing.Contains(name)) name = baseName + " " + (n++);
            if (HudProfileStore.Save(doc, name)) Apply(name);
        }

        private void OpenDesigner()
        {
            try
            {
                var inst = global::StationeersUIMod.StationeersUIMod.Instance;
                if (inst != null) { UiaControlCenter.Close(); inst.ToggleHudEditor(); }
            }
            catch (System.Exception e) { UIALog.Warn("Open Designer failed: " + e.Message); }
        }

        private void OpenFolder()
        {
            try { System.Diagnostics.Process.Start("explorer.exe", HudProfileStore.Dir); }
            catch { }
        }
    }
}
