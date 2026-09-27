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
    /// font), or any profile from the dropdown. Kit v2 conversion: authoring (Save as, Duplicate,
    /// the F9 Designer, the profiles folder, delete/restore) lives under Author's "More options" —
    /// the destructive actions are kit <see cref="UiaComposite.ConfirmButton"/>s, and action
    /// results report through an <see cref="UiaComposite.InlineStatus"/> line instead of a
    /// centre-screen toast. Player-facing wording says "UI Theme(s)" (FlorpyDorp's rename) —
    /// folder names, XML, class names, shipped theme names ("Stationeers Blue", …) and cfg keys
    /// are untouched; only labels/notes changed.</summary>
    public sealed class ProfilesTab : IUiaTab
    {
        public string Title => "UI Themes";

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
        // there is no static to unwind.
        private string _manageName;

        // Author feedback (the InlineStatus content). INSTANCE fields so the message survives the
        // Refresh() that follows most author actions (tab instances persist across a rebuild), and
        // dies with the tab object on a theme Restyle/teardown (transient feedback, not state).
        private string _authorMsg;
        private UiaComposite.StatusKind _authorKind;

        // ---------- gesture caches (restyle-path perf) ----------
        // An F9 colour drag rebuilds every open F10 tab ~7x/s (UiaControlCenter.IsRestyling), and
        // this tab's Build otherwise re-does a Directory.GetFiles scan (ListProfiles), an XML
        // deserialization PER featured card (Load, uncached), and a Resources.FindObjectsOfTypeAll
        // scan (AllFontNames) on every single one of those repaints — none of which the repaint
        // needs, since nothing about the on-disk profile set or the loaded fonts changes just
        // because a colour wheel moved. Same idiom as Tabs/Storage/StowShared.cs: reuse the last
        // gesture-built result while IsRestyling, fetch fresh on every real (non-restyle) build.
        // Statics, not instance fields, because EnsureBuilt/RestyleCore construct a brand new
        // ProfilesTab on every Restyle — an instance-field cache would be empty every time and
        // never help. All three hold plain data (strings / a POCO XML document, no Unity Object
        // refs), so nothing here can dangle across a hot reload; see ResetCaches for the one thing
        // that still wants an explicit reset hook.
        private static List<string> _profilesCache;
        private static readonly Dictionary<string, HudDocument> _docCache = new Dictionary<string, HudDocument>();
        private static List<string> _fontNamesCache;

        private static List<string> CachedProfiles()
        {
            if (_profilesCache != null && UiaControlCenter.IsRestyling) return _profilesCache;
            _profilesCache = HudProfileStore.ListProfiles();
            return _profilesCache;
        }

        private static HudDocument CachedLoad(string name)
        {
            HudDocument doc;
            bool have = _docCache.TryGetValue(name, out doc);
            if (have && UiaControlCenter.IsRestyling) return doc;
            doc = HudProfileStore.Load(name);
            _docCache[name] = doc;
            return doc;
        }

        private static List<string> CachedFontNames()
        {
            if (_fontNamesCache != null && UiaControlCenter.IsRestyling) return _fontNamesCache;
            _fontNamesCache = HudText.AllFontNames();
            return _fontNamesCache;
        }

        /// <summary>Hot-reload / shutdown teardown hook. NOT YET WIRED into
        /// <c>UiaControlCenter.Shutdown()</c> — that file is being edited concurrently this pass
        /// by another agent; wire in <c>Tabs.ProfilesTab.ResetCaches();</c> next to the existing
        /// <c>Tabs.RadialTab.ResetTransients()</c> call when it's next safe to touch. Safe even
        /// unwired in the meantime: none of these caches hold a Unity Object reference, and a hot
        /// reload replaces the type (fresh statics) regardless — this is belt-and-braces, not a
        /// correctness requirement.</summary>
        public static void ResetCaches()
        {
            _profilesCache = null;
            _docCache.Clear();
            _fontNamesCache = null;
        }

        public void Build(RectTransform content, bool advanced)
        {
            ScrollRect scroll;
            var col = UiaUi.ScrollView(content, out scroll, UiaTheme.Gap);
            UiaUi.Fill((RectTransform)scroll.gameObject.transform);

            var all = CachedProfiles();
            string active = HudConfig.HudActiveProfile != null ? HudConfig.HudActiveProfile.Value : null;

            BuildChoose(col, all, active);
            BuildActiveAndFont(col, all, active);
            BuildAuthor(col, all, active);
        }

        // ---------- Choose your UI ----------

        private void BuildChoose(Transform col, List<string> all, string active)
        {
            var sec = UiaComposite.Section(col, "profiles.choose", "Choose your UI");
            UiaControls.Note(sec.Body, "Pick a layout below, or use the two switches up top to run just the radial half or just the HUD half. Your choice applies to the visor HUD instantly.");

            var featured = PickFeatured(all);
            var gridGo = UiaUi.Go("card-grid", sec.Body);
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
        }

        // ---------- All UI Themes ----------

        private void BuildActiveAndFont(Transform col, List<string> all, string active)
        {
            var sec = UiaComposite.Section(col, "profiles.active", "All UI Themes");

            int activeIdx = all.IndexOf(active ?? "");
            var activeDd = UiaControls.DropdownRow(sec.Body, "Active UI Theme", all, activeIdx < 0 ? 0 : activeIdx,
                i => { if (i >= 0 && i < all.Count) Apply(all[i]); });
            UiaSearch.RegisterRow(Title, "profiles.active", "Active UI Theme", UiaSearch.MakeJump(activeDd.transform.parent.gameObject));

            // Per-profile font.
            var fonts = new List<string> { "(inherit global font)" };
            fonts.AddRange(CachedFontNames());
            var doc = HudProfileStore.Active;
            int fontIdx = 0;
            if (doc != null && !string.IsNullOrEmpty(doc.Font))
            {
                int f = fonts.IndexOf(doc.Font);
                fontIdx = f >= 0 ? f : 0;
            }
            var fontDd = UiaControls.DropdownRow(sec.Body, "Font (this UI Theme)", fonts, fontIdx, i =>
            {
                var d = HudProfileStore.Active;
                if (d == null) return;
                d.Font = i <= 0 ? null : fonts[i];
                HudProfileStore.MarkChanged();
            });
            UiaSearch.RegisterRow(Title, "profiles.active", "Font (this UI Theme)", UiaSearch.MakeJump(fontDd.transform.parent.gameObject));

            // A UI Theme WE ship is read-only for players (the store refuses every player-edit
            // write — see HudProfileStore.Save), and that font picker is the ONLY control on this
            // tab that edits the ACTIVE theme, so it is the one place here where a change can look
            // like it stuck when the store quietly dropped it. Asking the store keeps one source of
            // truth for "is this one of ours" — there is no name list here to drift as the shipped
            // set grows.
            if (!string.IsNullOrEmpty(active) && !UiaDevMode.Active && HudProfileStore.IsShippedName(active))
                UiaControls.Note(sec.Body, "'" + active + "' is a UI Theme we ship, so it is read-only - a font picked here is not saved. Duplicate it and change the copy instead.");
        }

        // ---------- Author ----------

        private void BuildAuthor(Transform col, List<string> all, string active)
        {
            var sec = UiaComposite.Section(col, "profiles.author", "Author",
                "Save-as, Duplicate, the HUD Designer, the folder shortcut, shipping a theme to " +
                "the mod, and deleting/restoring a UI Theme all live under More options.", true);
            UiaControls.Note(sec.Body, "Authoring tools live under More options below.");
            var statusLine = UiaComposite.InlineStatus(sec.Body, _authorMsg, _authorKind);

            var ex = sec.Extras;
            if (ex == null) return;
            System.Action reveal = () => sec.SetExpanded(true);

            var rowGo = UiaUi.Go("adv-row", ex);
            // Hand-built row: explicit floor (Kit v2 visual fix wave) rather than relying solely
            // on whatever minimum UiaUi.Size/Button/Note get centrally.
            UiaUi.Size(rowGo, UiaTheme.RowH).minHeight = UiaTheme.RowH;
            UiaUi.HLayout((RectTransform)rowGo.transform, UiaTheme.Gap);
            var newBlankBtn = UiaControls.Button(rowGo.transform, "New blank UI Theme", () => NewBlank(statusLine), 170f, UiaTheme.RowH);
            var dupBtn = UiaControls.Button(rowGo.transform, "Duplicate active", DuplicateActive, 160f, UiaTheme.RowH);

            var rowGo2 = UiaUi.Go("adv-row2", ex);
            UiaUi.Size(rowGo2, UiaTheme.RowH).minHeight = UiaTheme.RowH;
            UiaUi.HLayout((RectTransform)rowGo2.transform, UiaTheme.Gap);
            var designerBtn = UiaControls.Button(rowGo2.transform, "Open HUD Designer (F9)", OpenDesigner, 200f, UiaTheme.RowH, UiaControls.ButtonStyle.Primary);
            var folderBtn = UiaControls.Button(rowGo2.transform, "Open UI Themes folder", OpenFolder, 180f, UiaTheme.RowH);
            UiaControls.Note(ex, "New blank UI Theme starts an empty slate (your screen size, one hand-boxes element, no saved theme) and switches to it. The HUD Designer (F9) is where you build it out - add, move, resize and restyle every element, and rename it there.");

            // Dev-side "ship it": copy the profile you are building straight into the repo's
            // HudProfiles folder (the folder SyncShipped reads and package.ps1 zips). No-op with
            // an honest status on a non-dev install (the repo folder is not there to write to).
            var exportBtn = UiaControls.Button(ex, "Export active to mod (ship it)", () => ExportActiveToMod(statusLine), 240f, UiaTheme.RowH);
            UiaControls.Note(ex, "Export active to mod copies the UI Theme you're using (its layout, saved theme and preview .png) into the mod's HudProfiles folder in the repo - the same folder that ships and that the launch-time sync seeds to every player. Dev machines only.");

            UiaSearch.RegisterRow(Title, "profiles.author", "New blank UI Theme", UiaSearch.MakeJump(newBlankBtn.gameObject, reveal));
            UiaSearch.RegisterRow(Title, "profiles.author", "Duplicate active", UiaSearch.MakeJump(dupBtn.gameObject, reveal));
            UiaSearch.RegisterRow(Title, "profiles.author", "Open HUD Designer (F9)", UiaSearch.MakeJump(designerBtn.gameObject, reveal));
            UiaSearch.RegisterRow(Title, "profiles.author", "Open UI Themes folder", UiaSearch.MakeJump(folderBtn.gameObject, reveal));
            UiaSearch.RegisterRow(Title, "profiles.author", "Export active to mod", UiaSearch.MakeJump(exportBtn.gameObject, reveal));

            BuildManageBlock(ex, all, active, statusLine, reveal);
        }

        /// <summary>The destructive half of profile CRUD: pick a UI Theme, then delete it or put
        /// our shipped version back — each a kit <see cref="UiaComposite.ConfirmButton"/> (the one
        /// confirm idiom, replacing the tab's old hand-rolled two-click state). Deliberately
        /// simpler than the F9 designer's row — the card layout has no text input, so RENAME and
        /// the naming of a new profile stay in F9 and this side auto-names. Every call goes through
        /// the same <see cref="HudProfileStore"/> API F9 uses, so the shipped-theme rules hold
        /// identically here.</summary>
        private void BuildManageBlock(RectTransform ex, List<string> all, string active,
            UiaComposite.InlineStatusHandle statusLine, System.Action reveal)
        {
            UiaUi.Go("spacer3", ex).AddComponent<LayoutElement>().preferredHeight = 6f;
            UiaControls.Header(ex, "Manage a UI Theme");
            if (all == null || all.Count == 0)
            {
                UiaControls.Note(ex, "No UI Themes on disk yet.");
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
            var manageDd = UiaControls.DropdownRow(ex, "UI Theme", all, idx < 0 ? 0 : idx, i =>
            {
                if (i < 0 || i >= all.Count) return;
                _manageName = all[i];
                UiaControlCenter.Refresh();   // a new target invalidates any armed ConfirmButton
            });
            UiaSearch.RegisterRow(Title, "profiles.author", "Manage a UI Theme", UiaSearch.MakeJump(manageDd.transform.parent.gameObject, reveal));

            string target = _manageName;
            bool isActive = string.Equals(target, active, System.StringComparison.OrdinalIgnoreCase);
            bool shipped = HudProfileStore.IsShippedName(target);
            // Restore copies FROM the mod's installed folder, which the F6 ScriptEngine dev flow
            // does not have — Asking the store (not StationeersUIMod.ModDirectory) keeps one source
            // of truth for "is the shipped set reachable".
            bool canRestore = HudProfileStore.ShippedFolderAvailable;

            var row = UiaUi.Go("manage-row", ex);
            UiaUi.Size(row, UiaTheme.RowH).minHeight = UiaTheme.RowH;
            UiaUi.HLayout((RectTransform)row.transform, UiaTheme.Gap);

            if (!isActive)   // never delete the UI Theme the HUD is drawing
            {
                var delCb = UiaComposite.ConfirmButton(row.transform, "Delete UI Theme", () => DoDelete(target), 190f, UiaTheme.RowH);
                UiaSearch.RegisterRow(Title, "profiles.author", "Delete UI Theme", UiaSearch.MakeJump(delCb.gameObject, reveal));
            }
            if (shipped)
            {
                if (canRestore)
                {
                    var resCb = UiaComposite.ConfirmButton(row.transform, "Restore shipped", () => DoRestore(target), 170f, UiaTheme.RowH);
                    UiaSearch.RegisterRow(Title, "profiles.author", "Restore shipped UI Theme", UiaSearch.MakeJump(resCb.gameObject, reveal));
                }
                else
                {
                    // No confirm step when there is nothing to destroy: the click just explains,
                    // same as the maintenance dev-offline path on the HUD tab.
                    var resBtn = UiaControls.Button(row.transform, "Restore shipped", () =>
                        SetAuthorStatus("Restore shipped needs the mod's installed folder - unavailable under the F6 dev flow.",
                            UiaComposite.StatusKind.Error, statusLine),
                        170f, UiaTheme.RowH, UiaControls.ButtonStyle.Danger);
                    UiaSearch.RegisterRow(Title, "profiles.author", "Restore shipped UI Theme", UiaSearch.MakeJump(resBtn.gameObject, reveal));
                }
            }

            if (isActive)
                UiaControls.Note(ex, "'" + target + "' is the UI Theme you are using - pick another one above (or switch UI Themes) before deleting it.");
            else if (shipped && !canRestore)
                UiaControls.Note(ex, "'" + target + "' is a UI Theme we ship, but the mod's installed folder isn't available right now (the F6 dev flow has none), so there is nothing to restore from. Deleting it still works: a pristine copy comes back on the next launch.");
            else if (shipped)
                UiaControls.Note(ex, "'" + target + "' is a UI Theme we ship. Restore puts our version back over your edits; deleting it also brings a pristine copy back on the next launch.");
            else
                UiaControls.Note(ex, "'" + target + "' is yours - deleting it is permanent.");
        }

        private static int IndexOfName(List<string> all, string name)
        {
            if (all == null || string.IsNullOrEmpty(name)) return -1;
            for (int i = 0; i < all.Count; i++)
                if (string.Equals(all[i], name, System.StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>Set the author feedback both LIVE (the built status line, for actions that do
        /// not rebuild the tab) and in the instance fields the next Build reads (for the ones that
        /// do).</summary>
        private void SetAuthorStatus(string msg, UiaComposite.StatusKind kind, UiaComposite.InlineStatusHandle live)
        {
            _authorMsg = msg;
            _authorKind = kind;
            if (live != null) live.Show(msg, kind);
        }

        /// <summary>Delete a profile that is NOT the active one (re-checked here, not just at
        /// build time, since the active profile can change between arming the ConfirmButton and
        /// its Yes click).</summary>
        private void DoDelete(string name)
        {
            global::StationeersUIMod.Windows.HudEditorWindow.FlushPendingElementEdit();
            string active = HudConfig.HudActiveProfile != null ? HudConfig.HudActiveProfile.Value : null;
            if (!string.IsNullOrEmpty(name)
                && !string.Equals(name, active, System.StringComparison.OrdinalIgnoreCase))
            {
                HudProfileStore.Delete(name);
                _manageName = null;   // re-seeds to the active profile on the rebuild below
                _authorMsg = "'" + name + "' deleted.";
                _authorKind = UiaComposite.StatusKind.Good;
            }
            UiaControlCenter.Refresh();
        }

        /// <summary>Re-copy our pristine shipped file over the player's edited copy, then reload it
        /// if it happens to be the live one. The pending autosave is forced out FIRST: a debounced
        /// write landing after the copy would put the player's edits straight back.</summary>
        private void DoRestore(string name)
        {
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
                _authorMsg = ok
                    ? "'" + name + "' restored to the version we ship."
                    : "Could not restore '" + name + "' - the mod's installed folder isn't available.";
                _authorKind = ok ? UiaComposite.StatusKind.Good : UiaComposite.StatusKind.Error;
            }
            catch (System.Exception e)
            {
                UIALog.Warn("Restore shipped UI Theme failed: " + e.Message);
                _authorMsg = "Restore shipped UI Theme failed - see the log.";
                _authorKind = UiaComposite.StatusKind.Error;
            }
            UiaControlCenter.Refresh();
        }

        /// <summary>FlorpyDorp's "New Theme button that opens a blank slate". The document is built
        /// by the store (current screen size, ONE follow-global hand-boxes element, no theme
        /// snapshot, so it keeps the look you have now until you change a global). Auto-named,
        /// because this side of the UI has no text input; rename it in F9.</summary>
        private void NewBlank(UiaComposite.InlineStatusHandle live)
        {
            const string BaseName = "New UI Theme";
            string name = BaseName;
            var existing = HudProfileStore.ListProfiles();
            int n = 2;
            while (IndexOfName(existing, name) >= 0 && n < 500) name = BaseName + " " + (n++);
            if (HudProfileStore.CreateBlank(name))
            {
                _manageName = name;
                Apply(name);
            }
            else
            {
                UIALog.Warn("Could not create blank UI Theme '" + name + "'.");
                SetAuthorStatus("Could not create a new blank UI Theme - see the log.", UiaComposite.StatusKind.Error, live);
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
            HudDocument doc = CachedLoad(name);

            // OPAQUE active face (a=1) — NOT UiaTheme.SelectedDim (alpha 0.30). Unity's Outline
            // component clones the whole 9-slice geometry 4x at FULL alpha directly under the
            // face, so a sub-1-alpha fill + an Outline composites to a solid, full-saturation
            // slab (the card read as an orange block with its name invisible). A themed opaque
            // tint keeps the same "this one is active" cue without that compositing trap. Kit
            // note (not a UiaUi change): never put an Outline under a sub-0.9-alpha fill.
            Color activeFace = Color.Lerp(UiaTheme.PanelRaised, UiaTheme.Selected, 0.18f);
            activeFace.a = 1f;

            var card = UiaUi.Go("card", parent);
            var cimg = card.AddComponent<Image>();
            cimg.color = active ? activeFace : UiaTheme.PanelRaised;
            UiaUi.OutlineOf(cimg, active ? UiaTheme.Selected : UiaTheme.Divider, active ? 2f : 1f);
            UiaUi.VLayout((RectTransform)card.transform, 3f, 6, 6, 6, 6);

            // Preview image (or a labelled placeholder). flexibleHeight lets it absorb all the
            // space above the name/subtitle rows so the screenshot dominates the card. The card
            // itself sits in a FIXED-size GridLayoutGroup cell (unlike a scroll view's
            // auto-growing content) — the one genuinely squeeze-prone container in this tab, so
            // every row here gets an explicit minHeight floor.
            var preGo = UiaUi.Go("preview", card.transform);
            UiaUi.Size(preGo, 84f, -1f, -1f, 1f).minHeight = 84f;
            var preImg = preGo.AddComponent<Image>();
            var sprite = UiaImages.Load(HudProfileStore.PreviewPath(name));
            if (sprite != null)
            {
                preImg.sprite = sprite; preImg.color = Color.white; preImg.preserveAspect = true; preImg.raycastTarget = false;
            }
            else
            {
                // Themed recessed placeholder (was a hard-coded dark navy) so a shipped theme
                // with a light surface doesn't get an oddly mismatched "coming soon" tile.
                preImg.color = UiaTheme.Window; preImg.raycastTarget = false;
                var ph = UiaUi.Text(preGo.transform, "PREVIEW\nComing soon", 11f, UiaTheme.TextMute, TextAlignmentOptions.Center, true);
                UiaUi.Fill((RectTransform)ph.transform);
            }

            // No ellipsis (FlorpyDorp: never cut anything off). The name and subtitle rows keep
            // their one-line minimums but DROP the fixed preferred height, so the card's
            // VLayout reads each TMP's own wrapped preferred height: a long name / description
            // wraps onto extra lines and the flexible preview above gives up the room (down to
            // its 84px floor — ~110px of text headroom in the 218px card cell).
            var nameGo = UiaUi.Go("name", card.transform);
            var nameLe = UiaUi.Size(nameGo, 22f);
            nameLe.minHeight = 22f; nameLe.preferredHeight = -1f;
            var nameT = nameGo.AddComponent<TextMeshProUGUI>();
            nameT.font = UiaTheme.Font(); nameT.fontSize = 16f; nameT.raycastTarget = false;
            nameT.color = active ? UiaTheme.Selected : UiaTheme.Text; nameT.alignment = TextAlignmentOptions.Center;
            nameT.text = name; nameT.overflowMode = TextOverflowModes.Overflow; nameT.enableWordWrapping = true;

            string sub = active ? "ACTIVE" : (doc != null && !string.IsNullOrEmpty(doc.Description) ? doc.Description : "Click to apply");
            var subGo = UiaUi.Go("sub", card.transform);
            var subLe = UiaUi.Size(subGo, 18f);
            subLe.minHeight = 18f; subLe.preferredHeight = -1f;
            var subT = subGo.AddComponent<TextMeshProUGUI>();
            subT.font = UiaTheme.Font(); subT.fontSize = 12f; subT.raycastTarget = false;
            subT.color = active ? UiaTheme.On : UiaTheme.TextMute; subT.alignment = TextAlignmentOptions.Center;
            subT.text = sub; subT.overflowMode = TextOverflowModes.Overflow; subT.enableWordWrapping = true;

            // Whole card applies on click (drag still scrolls — click handler only). Hover/selected
            // mirror the same opaque active face — never the translucent SelectedDim, for the
            // same reason the base fill above isn't it.
            card.AddComponent<UiaControls.UiaButton>()
                .Init(cimg, cimg.color, active ? activeFace : UiaTheme.PanelHover,
                    active ? activeFace : UiaTheme.PanelHover)
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
            catch (System.Exception e) { UIALog.Warn("Apply UI Theme failed: " + e.Message); }
            UiaControlCenter.Refresh();
        }

        private void DuplicateActive()
        {
            var doc = HudProfileStore.Active;
            if (doc == null) return;
            string baseName = (doc.Name ?? "UI Theme") + " copy";
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
        /// change would ship a stale copy. Reports through the shared inline status (no rebuild
        /// needed — nothing else on this tab changes shape), so a confirmed action never looks
        /// like a silent no-op.</summary>
        private void ExportActiveToMod(UiaComposite.InlineStatusHandle live)
        {
            string name = HudConfig.HudActiveProfile != null ? HudConfig.HudActiveProfile.Value : null;
            if (string.IsNullOrEmpty(name))
            {
                SetAuthorStatus("No active UI Theme to export.", UiaComposite.StatusKind.Error, live);
                return;
            }
            try
            {
                global::StationeersUIMod.Windows.HudEditorWindow.FlushPendingElementEdit();
                HudProfileStore.FlushNow();
                string dst = HudProfileStore.ExportToShippedFolder(name);
                if (!string.IsNullOrEmpty(dst))
                    SetAuthorStatus("Exported '" + name + "' to the mod's HudProfiles folder - it ships on the next package.",
                        UiaComposite.StatusKind.Good, live);
                else
                    SetAuthorStatus("Export failed: the mod's repo HudProfiles folder wasn't found (this works on a dev machine only).",
                        UiaComposite.StatusKind.Error, live);
            }
            catch (System.Exception e)
            {
                UIALog.Warn("Export active UI Theme to mod failed: " + e.Message);
                SetAuthorStatus("Export failed - see the log.", UiaComposite.StatusKind.Error, live);
            }
        }
    }
}
