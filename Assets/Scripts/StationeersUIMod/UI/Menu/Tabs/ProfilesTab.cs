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
        public string Title => "HUD Themes";

        // The default UIs we want front-and-center, in order. Only the ones that actually exist
        // on disk become cards; the rest of the list falls through to the dropdown. Curated to the
        // shipped set (2026-08-01) — Stationeers Blue is the default, its Minimalist cut trims the
        // chrome, Zirillian Red is the red alternate and Pure HUD the green one. Four names, three
        // columns: the grid wraps to a second row, and PickFeatured's cap (6) still has headroom.
        private static readonly string[] Featured =
            { "Stationeers Blue", "Stationeers Blue Minimalist", "Zirillian Red", "Pure HUD" };

        // Manage-a-profile state. INSTANCE fields on purpose: the tab object lives exactly as long
        // as the Control Center's built UI (UiaControlCenter.Shutdown drops the tab list and
        // EnsureBuilt makes fresh tabs), so a menu teardown or a hot reload resets these for free —
        // there is no static to unwind. Each destructive action is a two-step: the first click sets
        // a confirm flag and rebuilds the tab, the second click does the work.
        private string _manageName;
        private bool _confirmDelete;
        private bool _confirmRestore;

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
            // Taller card so the preview area is ~16:9 (all shipped previews are 800x450 = 1.778):
            // at this width the image fills the box edge-to-edge with preserveAspect, instead of
            // being letterboxed into a thin strip. The name + subtitle sit centered underneath.
            grid.cellSize = new Vector2(298f, 218f);
            grid.spacing = new Vector2(UiaTheme.Gap, UiaTheme.Gap);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            var gridFit = gridGo.AddComponent<ContentSizeFitter>();
            gridFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            foreach (var name in featured)
                Card(gridGo.transform, name, string.Equals(name, active, System.StringComparison.OrdinalIgnoreCase));

            UiaUi.Go("spacer1", col).AddComponent<LayoutElement>().preferredHeight = 6f;
            UiaControls.Header(col, "All HUD Themes");

            int activeIdx = all.IndexOf(active ?? "");
            UiaControls.DropdownRow(col, "Active HUD Theme", all, activeIdx < 0 ? 0 : activeIdx,
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
            UiaControls.DropdownRow(col, "Font (this HUD Theme)", fonts, fontIdx, i =>
            {
                var d = HudProfileStore.Active;
                if (d == null) return;
                d.Font = i <= 0 ? null : fonts[i];
                HudProfileStore.MarkChanged();
            });

            // A HUD Theme WE ship is read-only for players (the store refuses every player-edit
            // write — see HudProfileStore.Save), and that font picker is the ONLY control on this tab
            // that edits the ACTIVE theme, so it is the one place here where a change can look like
            // it stuck when the store quietly dropped it. The centre-screen toast says so at edit
            // time; this says so before the click. Asking the store keeps one source of truth for
            // "is this one of ours" — there is no name list here to drift as the shipped set grows.
            if (!string.IsNullOrEmpty(active) && !UiaDevMode.Active && HudProfileStore.IsShippedName(active))
                UiaControls.Note(col, "'" + active + "' is a HUD Theme we ship, so it is read-only - a font picked here is not saved. Duplicate it and change the copy instead.");

            if (advanced)
            {
                UiaUi.Go("spacer2", col).AddComponent<LayoutElement>().preferredHeight = 6f;
                UiaControls.Header(col, "Author (advanced)");
                var rowGo = UiaUi.Go("adv-row", col);
                UiaUi.Size(rowGo, UiaTheme.RowH);
                UiaUi.HLayout((RectTransform)rowGo.transform, UiaTheme.Gap);
                UiaControls.Button(rowGo.transform, "New blank HUD Theme", NewBlank, 170f, UiaTheme.RowH);
                UiaControls.Button(rowGo.transform, "Duplicate active", DuplicateActive, 160f, UiaTheme.RowH);
                var rowGo2 = UiaUi.Go("adv-row2", col);
                UiaUi.Size(rowGo2, UiaTheme.RowH);
                UiaUi.HLayout((RectTransform)rowGo2.transform, UiaTheme.Gap);
                UiaControls.Button(rowGo2.transform, "Open HUD Designer (F9)", OpenDesigner, 200f, UiaTheme.RowH, UiaControls.ButtonStyle.Primary);
                UiaControls.Button(rowGo2.transform, "Open HUD Themes folder", OpenFolder, 180f, UiaTheme.RowH);
                UiaControls.Note(col, "New blank HUD Theme starts an empty slate (your screen size, one hand-boxes element, no saved theme) and switches to it. The HUD Designer (F9) is where you build it out - add, move, resize and restyle every element, and rename it there.");

                // Dev-side "ship it": copy the profile you are building straight into the repo's
                // HudProfiles folder (the folder SyncShipped reads and package.ps1 zips). No-op with
                // an honest toast on a non-dev install (the repo folder is not there to write to).
                var rowGo3 = UiaUi.Go("adv-row3", col);
                UiaUi.Size(rowGo3, UiaTheme.RowH);
                UiaUi.HLayout((RectTransform)rowGo3.transform, UiaTheme.Gap);
                UiaControls.Button(rowGo3.transform, "Export active to mod (ship it)", ExportActiveToMod, 240f, UiaTheme.RowH);
                UiaControls.Note(col, "Export active to mod copies the HUD Theme you're using (its layout, saved theme and preview .png) into the mod's HudProfiles folder in the repo - the same folder that ships and that the launch-time sync seeds to every player. Dev machines only.");
                BuildManageBlock(col, all, active);
            }
        }

        /// <summary>The destructive half of profile CRUD, advanced-only: pick a profile, then delete
        /// it or put our shipped version back. Deliberately simpler than the F9 designer's row — the
        /// card layout has no text input, so RENAME and the naming of a new profile stay in F9 and
        /// this side auto-names. Every call goes through the same <see cref="HudProfileStore"/> API
        /// F9 uses, so the shipped-theme rules hold identically here.</summary>
        private void BuildManageBlock(RectTransform col, List<string> all, string active)
        {
            UiaUi.Go("spacer3", col).AddComponent<LayoutElement>().preferredHeight = 6f;
            UiaControls.Header(col, "Manage a HUD Theme");
            if (all == null || all.Count == 0)
            {
                UiaControls.Note(col, "No HUD Themes on disk yet.");
                return;
            }

            // Case-insensitive throughout: profile names are FILE names, and the active one comes
            // from config, which the player may have typed with different casing.
            if (IndexOfName(all, _manageName) < 0)
            {
                int a = IndexOfName(all, active);
                _manageName = a >= 0 ? all[a] : all[0];
            }
            int idx = IndexOfName(all, _manageName);
            UiaControls.DropdownRow(col, "HUD Theme", all, idx < 0 ? 0 : idx, i =>
            {
                if (i < 0 || i >= all.Count) return;
                _manageName = all[i];
                _confirmDelete = false;      // a new target invalidates any armed confirm
                _confirmRestore = false;
                UiaControlCenter.Refresh();
            });

            string target = _manageName;
            bool isActive = string.Equals(target, active, System.StringComparison.OrdinalIgnoreCase);
            bool shipped = HudProfileStore.IsShippedName(target);

            var row = UiaUi.Go("manage-row", col);
            UiaUi.Size(row, UiaTheme.RowH);
            UiaUi.HLayout((RectTransform)row.transform, UiaTheme.Gap);

            if (_confirmDelete)
            {
                UiaControls.Button(row.transform, "Yes, delete it", () => DoDelete(target), 170f,
                    UiaTheme.RowH, UiaControls.ButtonStyle.Danger);
                UiaControls.Button(row.transform, "Cancel", CancelConfirm, 120f, UiaTheme.RowH);
                UiaControls.Note(col, "Delete '" + target + "' from disk? This cannot be undone."
                    + (shipped ? " It is one of ours, so a pristine copy is re-seeded on the next launch - that is the restore path." : ""));
                return;
            }
            if (_confirmRestore)
            {
                UiaControls.Button(row.transform, "Yes, restore it", () => DoRestore(target), 170f,
                    UiaTheme.RowH, UiaControls.ButtonStyle.Danger);
                UiaControls.Button(row.transform, "Cancel", CancelConfirm, 120f, UiaTheme.RowH);
                UiaControls.Note(col, "Replace '" + target + "' with the version we ship? Your changes to that HUD Theme are overwritten. No other HUD Theme is touched.");
                return;
            }

            var del = UiaControls.Button(row.transform, "Delete HUD Theme",
                () => { _confirmDelete = true; _confirmRestore = false; UiaControlCenter.Refresh(); },
                170f, UiaTheme.RowH, UiaControls.ButtonStyle.Danger);
            if (isActive) del.SetEnabled(false);   // never delete the profile the HUD is drawing
            // Restore copies FROM the mod's installed folder, which the F6 ScriptEngine dev flow
            // does not have — offer the button dimmed rather than armed-and-doomed, and say why in
            // the note below. Asking the store (not StationeersUIMod.ModDirectory) keeps one source
            // of truth for "is the shipped set reachable".
            bool canRestore = HudProfileStore.ShippedFolderAvailable;
            if (shipped)
            {
                var res = UiaControls.Button(row.transform, "Restore shipped",
                    () => { _confirmRestore = true; _confirmDelete = false; UiaControlCenter.Refresh(); },
                    170f, UiaTheme.RowH);
                if (!canRestore) res.SetEnabled(false);
            }

            if (isActive)
                UiaControls.Note(col, "'" + target + "' is the HUD Theme you are using - pick another one above (or switch HUD Themes) before deleting it.");
            else if (shipped && !canRestore)
                UiaControls.Note(col, "'" + target + "' is a HUD Theme we ship, but the mod's installed folder isn't available right now (the F6 dev flow has none), so there is nothing to restore from. Deleting it still works: a pristine copy comes back on the next launch.");
            else if (shipped)
                UiaControls.Note(col, "'" + target + "' is a HUD Theme we ship. Restore puts our version back over your edits; deleting it also brings a pristine copy back on the next launch.");
            else
                UiaControls.Note(col, "'" + target + "' is yours - deleting it is permanent.");
        }

        private static int IndexOfName(List<string> all, string name)
        {
            if (all == null || string.IsNullOrEmpty(name)) return -1;
            for (int i = 0; i < all.Count; i++)
                if (string.Equals(all[i], name, System.StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        private void CancelConfirm()
        {
            _confirmDelete = false;
            _confirmRestore = false;
            UiaControlCenter.Refresh();
        }

        /// <summary>Delete a profile that is NOT the active one. The guard is re-checked here (not
        /// just at build time) because the active profile can change between arming the confirm and
        /// clicking it.</summary>
        private void DoDelete(string name)
        {
            _confirmDelete = false;
            global::StationeersUIMod.Windows.HudEditorWindow.FlushPendingElementEdit();
            string active = HudConfig.HudActiveProfile != null ? HudConfig.HudActiveProfile.Value : null;
            if (!string.IsNullOrEmpty(name)
                && !string.Equals(name, active, System.StringComparison.OrdinalIgnoreCase))
            {
                HudProfileStore.Delete(name);
                _manageName = null;   // re-seeds to the active profile on the rebuild below
            }
            UiaControlCenter.Refresh();
        }

        /// <summary>Re-copy our pristine shipped file over the player's edited copy, then reload it
        /// if it happens to be the live one. The pending autosave is forced out FIRST: a debounced
        /// write landing after the copy would put the player's edits straight back.</summary>
        private void DoRestore(string name)
        {
            _confirmRestore = false;
            if (string.IsNullOrEmpty(name)) { UiaControlCenter.Refresh(); return; }
            string active = HudConfig.HudActiveProfile != null ? HudConfig.HudActiveProfile.Value : null;
            bool isActive = string.Equals(name, active, System.StringComparison.OrdinalIgnoreCase);
            try
            {
                global::StationeersUIMod.Windows.HudEditorWindow.FlushPendingElementEdit();
                if (isActive) HudProfileStore.FlushNow();
                bool ok = HudProfileStore.RestoreShipped(name);
                if (ok && isActive) { Apply(name); return; }   // Apply refreshes the tab itself
                // A silent no-op is the worst outcome of a confirmed destructive action: the player
                // cannot tell "restored" from "nothing happened". Say which it was.
                global::StationeersUIMod.Overlay.Toast.Show(ok
                        ? "'" + name + "' restored to the version we ship."
                        : "Could not restore '" + name + "' - the mod's installed folder isn't available.",
                    ok ? global::StationeersUIMod.Overlay.Theme.TextPrimary : global::StationeersUIMod.Overlay.Theme.Critical,
                    ok ? 3f : 4f);
            }
            catch (System.Exception e) { UIALog.Warn("Restore shipped HUD Theme failed: " + e.Message); }
            UiaControlCenter.Refresh();
        }

        /// <summary>FlorpyDorp's "New Theme button that opens a blank slate". The document is built
        /// by the store (current screen size, ONE follow-global hand-boxes element, no theme
        /// snapshot, so it keeps the look you have now until you change a global). Auto-named,
        /// because this side of the UI has no text input; rename it in F9.</summary>
        private void NewBlank()
        {
            const string BaseName = "New HUD Theme";
            string name = BaseName;
            var existing = HudProfileStore.ListProfiles();
            int n = 2;
            while (IndexOfName(existing, name) >= 0 && n < 500) name = BaseName + " " + (n++);
            if (HudProfileStore.CreateBlank(name))
            {
                _manageName = name;
                _confirmDelete = false;
                _confirmRestore = false;
                Apply(name);
            }
            else UIALog.Warn("Could not create blank HUD Theme '" + name + "'.");
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
            UiaUi.VLayout((RectTransform)card.transform, 3f, 6, 6, 6, 6);

            // Preview image (or a labelled placeholder). flexibleHeight lets it absorb all the
            // space above the name/subtitle rows so the screenshot dominates the card.
            var preGo = UiaUi.Go("preview", card.transform);
            UiaUi.Size(preGo, 84f, -1f, -1f, 1f);
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
            nameT.color = active ? UiaTheme.Selected : UiaTheme.Text; nameT.alignment = TextAlignmentOptions.Center;
            nameT.text = name; nameT.overflowMode = TextOverflowModes.Ellipsis; nameT.enableWordWrapping = false;

            string sub = active ? "ACTIVE" : (doc != null && !string.IsNullOrEmpty(doc.Description) ? doc.Description : "Click to apply");
            var subGo = UiaUi.Go("sub", card.transform);
            UiaUi.Size(subGo, 18f);
            var subT = subGo.AddComponent<TextMeshProUGUI>();
            subT.font = UiaTheme.Font(); subT.fontSize = 12f; subT.raycastTarget = false;
            subT.color = active ? UiaTheme.On : UiaTheme.TextMute; subT.alignment = TextAlignmentOptions.Center;
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
                // The F9 designer can be open behind this menu (it is a click-to-edit surface while
                // the editor runs), so an in-flight property gesture must be committed to the
                // OUTGOING document before the swap — same contract as the F9 profile combo.
                global::StationeersUIMod.Windows.HudEditorWindow.FlushPendingElementEdit();
                if (HudConfig.HudActiveProfile != null) HudConfig.HudActiveProfile.Value = name;
                var keep = HudProfileStore.Active;
                HudProfileStore.LoadActive(name, () => keep != null ? keep.Clone() : new HudDocument { Name = name });
                HudDocumentHistory.Clear();
                // Element ids do not survive a document swap, so a selection held by the F9 editor
                // (which can be open behind this menu) would point at nothing. Same reset the F9
                // switch path does — parity, so a switch is a switch wherever it is driven from.
                global::StationeersUIMod.Windows.HudEditorMode.ClearElementSelection();
            }
            catch (System.Exception e) { UIALog.Warn("Apply HUD Theme failed: " + e.Message); }
            UiaControlCenter.Refresh();
        }

        private void DuplicateActive()
        {
            var doc = HudProfileStore.Active;
            if (doc == null) return;
            string baseName = (doc.Name ?? "HUD Theme") + " copy";
            string name = baseName;
            var existing = HudProfileStore.ListProfiles();
            int n = 2;
            // Case-INsensitive: profile names are Windows file names, so "Blue copy" and "blue copy"
            // are the same file — an ordinal List.Contains would have let a duplicate silently
            // overwrite an existing profile through Save (which has no collision guard, by design:
            // it is also the autosave path).
            while (IndexOfName(existing, name) >= 0 && n < 500) name = baseName + " " + (n++);
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

        /// <summary>FlorpyDorp's "add the UI I'm building to the build folder" button: exports the
        /// ACTIVE profile into the repo's HudProfiles folder via <see cref="HudProfileStore
        /// .ExportToShippedFolder"/>. Any in-flight F9 edit and the debounced autosave are flushed
        /// FIRST, because Export re-reads the profile file from the config folder — an un-flushed
        /// change would ship a stale copy. A clear toast reports success (with the repo path) or the
        /// dev-only failure, so a confirmed action never looks like a silent no-op.</summary>
        private void ExportActiveToMod()
        {
            string name = HudConfig.HudActiveProfile != null ? HudConfig.HudActiveProfile.Value : null;
            if (string.IsNullOrEmpty(name))
            {
                global::StationeersUIMod.Overlay.Toast.Show("No active HUD Theme to export.",
                    global::StationeersUIMod.Overlay.Theme.Critical, 3f);
                return;
            }
            try
            {
                global::StationeersUIMod.Windows.HudEditorWindow.FlushPendingElementEdit();
                HudProfileStore.FlushNow();
                string dst = HudProfileStore.ExportToShippedFolder(name);
                if (!string.IsNullOrEmpty(dst))
                    global::StationeersUIMod.Overlay.Toast.Show(
                        "Exported '" + name + "' to the mod's HudProfiles folder - it ships on the next package.",
                        global::StationeersUIMod.Overlay.Theme.TextPrimary, 4f);
                else
                    global::StationeersUIMod.Overlay.Toast.Show(
                        "Export failed: the mod's repo HudProfiles folder wasn't found (this works on a dev machine only).",
                        global::StationeersUIMod.Overlay.Theme.Critical, 4.5f);
            }
            catch (System.Exception e) { UIALog.Warn("Export active HUD Theme to mod failed: " + e.Message); }
        }
    }
}
