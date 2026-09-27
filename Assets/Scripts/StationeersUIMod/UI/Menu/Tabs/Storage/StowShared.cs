using System;
using System.Collections.Generic;
using Assets.Scripts.Objects;
using StationeersUIMod.Core;
using StationeersUIMod.Features;
using StationeersUIMod.UI.Menu.Kit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Tabs.Storage
{
    /// <summary>Per-page state of the SmartStow tab, held in <see cref="UiaPageState"/> so it
    /// survives both Refresh (gesture rebuilds) and a theme Restyle. Cleared by
    /// <see cref="StowShared.ResetCaches"/> (via StorageTab.ResetCaches, the one teardown
    /// contract UiaControlCenter.Shutdown already calls).</summary>
    internal sealed class SmartStowState
    {
        // Sub-tab per mode (the two modes have different tab lists, so one shared index
        // would land Simple players on "Routing" after a mode flip).
        public int SubTabSimple;
        public int SubTabComplex;

        // Organizer selection.
        public string BrowsedSet;          // layout being BROWSED (never implies activation)
        public string SelectedProfile;     // bag profile selected into the EDITING band
        public int OpenRuleGroup = -1;     // 0=item 1=UIA 2=slot 3=category, -1 closed
        public string RuleSearch;          // By-item filter text (only shown past 15 rules)

        // Inline rename rows (open/closed per target; the typed text lives in the kit's
        // draft cache keyed by DraftId, so it survives rebuilds without us carrying it).
        public string RenameLayoutTarget;
        public string RenameProfileTarget;
        public long RenameBagId;

        // Bags column: the SELECTED bag card (comparison round: unselected bag cards are
        // compact two-liners; the profile/Capture/Edit controls open on the selected one).
        public long SelectedBagId;

        // Share bar.
        public string ShareCode;           // last exported UIAP1 code (kept for Copy again)
        public string ImportField;

        // Test box (transient diagnostic; cleared with the page state).
        public string TestPrefab;

        // Per-surface status lines (InlineStatus content re-shown after each rebuild).
        public string LayoutsMsg; public UiaComposite.StatusKind LayoutsKind;
        public string BagsMsg; public UiaComposite.StatusKind BagsKind;
        public string ProfsMsg; public UiaComposite.StatusKind ProfsKind;
        public string ShareMsg; public UiaComposite.StatusKind ShareKind;
        public string SimpleMsg; public UiaComposite.StatusKind SimpleKind;
    }

    /// <summary>
    /// Shared services for the SmartStow pages: the gesture-frequency caches (layout listing,
    /// worn-bag scan, thumbnails, the browsed layout's profile listing, the Simple page's
    /// counts), the small helpers every page uses, and the one ResetCaches funnel.
    ///
    /// <para>CACHING CONTRACT (the 2026-07-20 perf finding): a theme Restyle rebuilds the tab up
    /// to ~7x/s during an F9 colour drag, and NONE of that repaint needs fresh data — so every
    /// method here reuses its last gesture-built result while <see cref="UiaControlCenter.IsRestyling"/>
    /// is true, and does NO disk IO and NO inventory scan on that path. Non-restyle builds see
    /// fresh data (the layout listing additionally diffs <see cref="StowProfileStore.EditVersion"/>
    /// so an unchanged disk state costs no re-deserialization either).</para>
    /// </summary>
    internal static class StowShared
    {
        public const string PageKey = "smartstow";

        /// <summary>The capture dialog's sort order when hosted OVER the F10 window (F10's canvas
        /// is 5200; the Grid-hosted capture panel is also 5200 on its own canvas).</summary>
        public const int CaptureOverF10SortOrder = 5300;

        public static SmartStowState State { get { return UiaPageState.Get<SmartStowState>(PageKey); } }

        // Priority tri-state (design O6): the UI face of RuleTiers, shared read-only.
        public static readonly List<string> TierOptions = new List<string> { "High", "Normal", "Low" };

        // ---------- layout (Stow Profile set) listing ----------

        private static readonly List<StowProfileStore.StowSetInfo> _sets =
            new List<StowProfileStore.StowSetInfo>();
        private static bool _setsValid;
        private static int _setsVersion = -1;

        /// <summary>Every Storage Layout on disk, gesture-cached: re-read only when a write
        /// bumped <see cref="StowProfileStore.EditVersion"/> or a gesture invalidated the cache;
        /// a restyle build always reuses the last listing (no disk IO).</summary>
        public static List<StowProfileStore.StowSetInfo> Sets()
        {
            if (UiaControlCenter.IsRestyling && _setsValid) return _sets;
            if (!_setsValid || _setsVersion != StowProfileStore.EditVersion)
            {
                _sets.Clear();
                try { _sets.AddRange(StowProfileStore.ListSets()); }
                catch (Exception e) { UIALog.Warn("Could not list Storage Layouts: " + e.Message); }
                _setsVersion = StowProfileStore.EditVersion;
                _setsValid = true;
            }
            return _sets;
        }

        public static void InvalidateSets() { _setsValid = false; }

        /// <summary>Which layout the Organizer is BROWSING. Defaults to (and falls back to) the
        /// ACTIVE one, so a browsed layout that was just renamed or deleted can never leave the
        /// page pointing at nothing.</summary>
        public static string BrowsedSet(List<StowProfileStore.StowSetInfo> sets)
        {
            var st = State;
            for (int i = 0; i < sets.Count; i++)
                if (string.Equals(sets[i].Name, st.BrowsedSet, StringComparison.OrdinalIgnoreCase))
                    return sets[i].Name;
            for (int i = 0; i < sets.Count; i++)
                if (sets[i].IsActive) { st.BrowsedSet = sets[i].Name; return st.BrowsedSet; }
            st.BrowsedSet = sets.Count > 0 ? sets[0].Name : null;
            return st.BrowsedSet;
        }

        public static bool IsActiveSet(string name)
        {
            return StowProfileStore.Available && !string.IsNullOrEmpty(name)
                && string.Equals(name, StowProfileStore.ActiveName, StringComparison.OrdinalIgnoreCase);
        }

        // ---------- browsed layout's Bag Profile listing ----------

        private static readonly List<BagProfile> _browsedProfiles = new List<BagProfile>();
        private static string _browsedProfilesFor;
        private static int _browsedProfilesVersion = -1;
        private static bool _browsedProfilesValid;

        /// <summary>The browsed layout's Bag Profiles. ACTIVE layout = the live in-memory list
        /// (always fresh, no IO); an INACTIVE layout is read from its file, cached on
        /// (name, EditVersion) so a restyle or an unchanged rebuild costs nothing.</summary>
        public static List<BagProfile> ProfilesOfBrowsed(string setName)
        {
            if (string.IsNullOrEmpty(setName)) { _browsedProfiles.Clear(); return _browsedProfiles; }
            if (IsActiveSet(setName))
            {
                // Live list, no disk involved; safe (and correct) on every build.
                _browsedProfiles.Clear();
                _browsedProfiles.AddRange(BagProfileStore.Profiles);
                _browsedProfilesFor = setName;
                _browsedProfilesValid = true;
                _browsedProfilesVersion = StowProfileStore.EditVersion;
                return _browsedProfiles;
            }
            bool fresh = !_browsedProfilesValid
                || !string.Equals(_browsedProfilesFor, setName, StringComparison.OrdinalIgnoreCase)
                || _browsedProfilesVersion != StowProfileStore.EditVersion;
            if (fresh && UiaControlCenter.IsRestyling && _browsedProfilesValid
                && string.Equals(_browsedProfilesFor, setName, StringComparison.OrdinalIgnoreCase))
                fresh = false;   // restyle: reuse even a version-stale listing rather than hit disk
            if (fresh)
            {
                _browsedProfiles.Clear();
                try { _browsedProfiles.AddRange(StowProfileStore.ProfilesOf(setName)); }
                catch (Exception e) { UIALog.Warn("Could not read layout '" + setName + "': " + e.Message); }
                _browsedProfilesFor = setName;
                _browsedProfilesVersion = StowProfileStore.EditVersion;
                _browsedProfilesValid = true;
            }
            return _browsedProfiles;
        }

        public static void InvalidateBrowsedProfiles() { _browsedProfilesValid = false; }

        // ---------- worn bags + thumbnails ----------

        private static readonly List<DynamicThing> _bagScratch = new List<DynamicThing>();
        private static bool _bagScratchValid;
        private static readonly Dictionary<long, Sprite> _thumbs = new Dictionary<long, Sprite>();

        /// <summary>The on-player container list — still <see cref="LoadoutStore.CollectWornBags"/>,
        /// the ONE canonical gate-filtered enumeration every surface shares. A restyle build reuses
        /// the last gesture-built list instead of re-running the inventory scan.</summary>
        public static List<DynamicThing> WornBags()
        {
            if (!(UiaControlCenter.IsRestyling && _bagScratchValid))
            {
                LoadoutStore.CollectWornBags(_bagScratch);
                _bagScratchValid = true;
            }
            return _bagScratch;
        }

        public static void InvalidateBags() { _bagScratchValid = false; }

        /// <summary>Clear the per-bag thumbnail cache — called at the start of every NON-restyle
        /// Bags build so a repainted bag shows up on the next gesture, while an F9 colour drag
        /// keeps reusing the sprites it already has.</summary>
        public static void ClearThumbsIfGesture()
        {
            if (!UiaControlCenter.IsRestyling) _thumbs.Clear();
        }

        /// <summary>Session thumbnail cache keyed on ReferenceId (per BAG, not per prefab — two
        /// differently painted backpacks must never share one sprite). Only successful fetches
        /// are stored, so a not-yet-generated thumbnail retries on the next build.</summary>
        public static Sprite Thumb(DynamicThing bag)
        {
            long id = SafeRef(bag);
            Sprite sp;
            if (id != 0 && _thumbs.TryGetValue(id, out sp)) return sp;
            sp = null;
            try { sp = bag.GetThumbnail(); } catch { }
            if (sp != null && id != 0) _thumbs[id] = sp;
            return sp;
        }

        // ---------- Simple page caches (restyle-scoped, like the bag scan) ----------

        private static int _homesCount;
        private static string _handLine1, _handLine2;
        private static bool _simpleValid;

        /// <summary>Simple page data (home count + in-hand readout), computed once per gesture
        /// build and reused across restyles — TryDescribeEntry allocates and a colour drag
        /// rebuilds ~7x/s.</summary>
        public static void SimpleReadout(out int homes, out string handLine, out string homeLine)
        {
            if (!(UiaControlCenter.IsRestyling && _simpleValid))
            {
                _homesCount = 0;
                _handLine1 = null;
                _handLine2 = null;
                try { _homesCount = StowHomeStore.HomesCount; } catch { }
                try
                {
                    var human = Guards.LocalHuman;
                    if (human == null)
                    {
                        _handLine1 = null;   // page shows the fail-soft "no character" text
                    }
                    else
                    {
                        DynamicThing held = null;
                        try { held = Assets.Scripts.Inventory.InventoryManager.ActiveHandSlot != null ? Assets.Scripts.Inventory.InventoryManager.ActiveHandSlot.Get() : null; } catch { }
                        if (held == null && human.RightHandSlot != null) held = human.RightHandSlot.Get();
                        if (held == null && human.LeftHandSlot != null) held = human.LeftHandSlot.Get();
                        if (held == null)
                        {
                            _handLine1 = "In your hand: nothing";
                        }
                        else
                        {
                            _handLine1 = "In your hand: " + SafeName(held);
                            string prov;
                            _handLine2 = StowHomeStore.TryDescribeEntry(held, out prov)
                                ? "Home: " + prov
                                : "No home remembered for it yet - the next stow gives it one.";
                        }
                    }
                }
                catch (Exception e) { UIALog.Warn("Simple readout failed: " + e.Message); }
                _simpleValid = true;
            }
            homes = _homesCount;
            handLine = _handLine1;
            homeLine = _handLine2;
        }

        public static void InvalidateSimple() { _simpleValid = false; }

        // ---------- test box readout (fix wave finding 4) ----------

        /// <summary>The composed test-box lines. Strings only — nothing here retains a Thing
        /// or the router's pooled candidate list.</summary>
        public sealed class TestLines
        {
            public string Missing;   // non-null: the prefab is unavailable; show only this
            public string Head;      // "Testing: X"
            public string Blocker;   // non-null: no character; show after Head and stop
            public string Gate;      // optional "(switched off - this shows what it WOULD do)"
            public bool NoMatch;     // true: no candidate; show the dim vanilla line
            public string Win;       // winner line
            public string Next;      // optional runner-up line
        }

        private static TestLines _testLines;
        private static string _testFor;
        private static bool _testValid;

        /// <summary>The dry-run readout for the picked prefab, gesture-cached exactly like
        /// <see cref="SimpleReadout"/>: FindPrefab is a full prefab-list scan and
        /// <see cref="StowRouter.ResolveAll"/> walks the inventory, so a ~7x/s restyle reuses
        /// the last composed lines; every non-restyle build recomputes (a gesture may have
        /// changed profiles/assignments and therefore the answer). The pooled candidate list
        /// is read IMMEDIATELY into strings, as ever.</summary>
        public static TestLines TestReadout(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return null;
            if (UiaControlCenter.IsRestyling && _testValid && _testLines != null
                && string.Equals(_testFor, prefabName, StringComparison.Ordinal))
                return _testLines;

            var l = new TestLines();
            DynamicThing prefab = FindPrefab(prefabName);
            if (prefab == null)
            {
                l.Missing = "Item \"" + prefabName + "\" not available - load a world first.";
            }
            else
            {
                string disp;
                try { disp = prefab.DisplayName; } catch { disp = prefabName; }
                l.Head = "Testing: " + disp;
                if (Guards.LocalHuman == null)
                {
                    l.Blocker = "No character - load into a world to test.";
                }
                else
                {
                    // Mirror the execution-path gates the router leaves to its caller: report
                    // them rather than silently diverging from G.
                    if (!UIAConfig.MasterEnable.Value || !UIAConfig.SmartStowPlusEnabled.Value)
                        l.Gate = "(Smart Stow is currently switched off - this shows what it WOULD do)";

                    List<StowCandidate> results = null;
                    try { results = StowRouter.ResolveAll(prefab, null, StowRouter.ConfiguredDepth()); }
                    catch (Exception e) { UIALog.Warn("Test box dry-run failed: " + e.Message); }
                    if (results == null || results.Count == 0)
                    {
                        l.NoMatch = true;
                    }
                    else
                    {
                        // Read the pooled list NOW; it is invalid after the next resolve anywhere.
                        l.Win = "-> " + HolderName(results[0]) + "  (" + StageTag(results[0].Stage)
                            + ": " + TrimmedReason(results[0]) + ")";
                        if (results.Count > 1)
                            l.Next = "next: " + HolderName(results[1]) + "  (" + StageTag(results[1].Stage)
                                + ": " + TrimmedReason(results[1]) + ")";
                    }
                }
            }
            _testLines = l;
            _testFor = prefabName;
            _testValid = true;
            return l;
        }

        public static void InvalidateTest() { _testValid = false; }

        private static DynamicThing FindPrefab(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return null;
            try
            {
                var list = DynamicThing.DynamicThingPrefabs;
                if (list == null) return null;
                for (int i = 0; i < list.Count; i++)
                {
                    var it = list[i];
                    if (it != null && it.PrefabName == prefabName) return it;
                }
            }
            catch { }
            return null;
        }

        private static string HolderName(StowCandidate c)
        {
            Thing t = c.Holder;
            if (t == null) return "inventory";
            try { return t.DisplayName; } catch { }
            try { return t.PrefabName ?? "container"; } catch { return "container"; }
        }

        // ---------- rename-row draft ids (fix wave finding 6) ----------

        /// <summary>The kit draft-cache ids for the three inline rename rows. Centralized so a
        /// TARGET reset can always clear its matching draft — a half-typed name must never
        /// resurface on a different row later. The profile id carries the LAYOUT name too:
        /// "Ores" exists in several layouts and a name-only key collided across them.</summary>
        public static string LayoutRenameDraftId(string layoutName)
        {
            return "smartstow.renset." + layoutName;
        }

        public static string ProfileRenameDraftId(string layoutName, string profileName)
        {
            return "smartstow.renprof." + (layoutName ?? "") + "." + profileName;
        }

        public static string BagRenameDraftId(long refId)
        {
            return "smartstow.renbag." + refId;
        }

        /// <summary>Close every open inline rename row AND clear its draft (the two always move
        /// together — finding 6). Call BEFORE reassigning <c>BrowsedSet</c>: the profile draft
        /// id was built under the OLD browsed layout.</summary>
        public static void CloseRenameRows(SmartStowState st)
        {
            if (st == null) return;
            if (!string.IsNullOrEmpty(st.RenameLayoutTarget))
                UiaInputs.ClearDraft(LayoutRenameDraftId(st.RenameLayoutTarget));
            if (!string.IsNullOrEmpty(st.RenameProfileTarget))
                UiaInputs.ClearDraft(ProfileRenameDraftId(st.BrowsedSet, st.RenameProfileTarget));
            if (st.RenameBagId != 0)
                UiaInputs.ClearDraft(BagRenameDraftId(st.RenameBagId));
            st.RenameLayoutTarget = null;
            st.RenameProfileTarget = null;
            st.RenameBagId = 0;
        }

        // ---------- teardown ----------

        /// <summary>Hot-reload / menu teardown: drop every cache that holds Thing/Sprite refs
        /// into the live world, the drag-drop statics, and the whole page state.</summary>
        public static void ResetCaches()
        {
            _sets.Clear(); _setsValid = false; _setsVersion = -1;
            _browsedProfiles.Clear(); _browsedProfilesValid = false;
            _browsedProfilesFor = null; _browsedProfilesVersion = -1;
            _bagScratch.Clear(); _bagScratchValid = false;
            _thumbs.Clear();
            _simpleValid = false; _handLine1 = null; _handLine2 = null;
            _testLines = null; _testFor = null; _testValid = false;
            StowDragDrop.Reset();
            UiaPageState.Clear(PageKey);
        }

        // ---------- small shared helpers (ported from the pre-0.9.8.0 StorageTab) ----------

        public static string SafeName(DynamicThing t)
        {
            try { return t.DisplayName; } catch { return t != null ? t.PrefabName : "bag"; }
        }

        /// <summary>What this container IS, independent of any custom name — so a row still says
        /// "Mining Belt" after you (or a labeller) renamed it "Ores".</summary>
        public static string TypeName(DynamicThing t)
        {
            try
            {
                var prefab = t.SourcePrefab;
                if (prefab != null && !string.IsNullOrEmpty(prefab.DisplayName)) return prefab.DisplayName;
            }
            catch { }
            try { return t.PrefabName ?? "container"; } catch { return "container"; }
        }

        public static string SafePrefab(DynamicThing t)
        {
            try { return t.PrefabName ?? ""; } catch { return ""; }
        }

        public static long SafeRef(Thing t)
        {
            try { return t != null ? t.ReferenceId : 0L; } catch { return 0L; }
        }

        /// <summary>The SmartStow pages' NO-ELLIPSIS rule (FlorpyDorp, 2026-09-26: "We should
        /// never cut anything off by an ellipses"). Replaces every Ellipsis overflow and the
        /// old head+tail <c>Shorten()</c>: the text first steps its point size down from
        /// <paramref name="maxSize"/> toward a legible <paramref name="minSize"/> (floored at
        /// 9pt) to stay on one line, and only when even the floor cannot hold it on one line
        /// does it WORD-WRAP (<paramref name="wrap"/>). Nothing is ever truncated or clipped:
        /// overflow stays Overflow, and the host row must let its height grow (min-only
        /// LayoutElement heights — an explicit preferred height would mask the wrapped
        /// height, the EDITING-band clip-bug class). See <see cref="StowFitText"/>.</summary>
        public static TextMeshProUGUI Fit(TextMeshProUGUI t, float maxSize, float minSize = 9f,
            bool wrap = true)
        {
            if (t == null) return null;
            t.enableAutoSizing = false;
            t.fontSize = maxSize;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            var f = t.GetComponent<StowFitText>();
            if (f == null) f = t.gameObject.AddComponent<StowFitText>();
            f.MaxSize = maxSize;
            f.MinSize = Mathf.Min(maxSize, Mathf.Max(9f, minSize));
            f.AllowWrap = wrap;
            return t;
        }

        /// <summary>Unity's system clipboard, fail-soft (the export also wrote its .txt file and
        /// the status line says which of the two worked).</summary>
        public static bool CopyToClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            try { GUIUtility.systemCopyBuffer = text; return true; }
            catch (Exception e) { UIALog.Warn("Clipboard write failed: " + e.Message); return false; }
        }

        public static string ReadClipboard()
        {
            try { return GUIUtility.systemCopyBuffer; }
            catch (Exception e) { UIALog.Warn("Clipboard read failed: " + e.Message); return null; }
        }

        /// <summary>Nudge the Universal Inventory's profile chrome (chips, tab badges) after a
        /// profile/assignment change made from F10, so an open window repaints without waiting
        /// for a structural change. Fail-soft — a missing window never breaks the tab.</summary>
        public static void BumpGridChrome()
        {
            // global:: because the plugin CLASS shares the root namespace's name.
            try { global::StationeersUIMod.UI.Grid.GridProfileMode.BumpVersion(); } catch { }
        }

        /// <summary>The indented small muted footnote (the old SubNote), kept for the handful of
        /// contextual notes that are commentary rather than gesture feedback. Comparison round:
        /// secondary text on these pages is the light-cyan MutedCyan, not the kit's grey-slate.</summary>
        public static TextMeshProUGUI SubNote(Transform col, string text)
        {
            var t = UiaControls.Note(col, text);
            t.margin = new Vector4(18f, 3f, 2f, 3f);
            t.fontSize = 11f;
            t.color = StowSkin.MutedCyan;
            return t;
        }

        /// <summary>The DropdownRow widget without its labelled row shell, for inline use inside
        /// an existing HLayout row. 1:1-round field recipe (cards report): dark teal field
        /// fill, a 1px border on ALL sides (the bright top-only line left the skin), and a
        /// DRAWN cyan chevron on the LEFT of the label (the concept's field language; the
        /// ASCII "v" caret is gone). <paramref name="dim"/> mutes the label — a NO STOW bag's
        /// dropdown stays fully USABLE (an excluded container may still keep a profile).</summary>
        public static UiaControls.UiaDropdown InlineDropdown(Transform parent, List<string> options,
            int index, Action<int> onChanged, float width, bool dim = false, float height = 28f)
        {
            var ddGo = UiaUi.Go("dropdown", parent);
            var le = UiaUi.Size(ddGo, height, width, flexW: width < 0f ? 1f : 0f);
            if (width < 0f) le.minWidth = 90f;
            var bg = ddGo.AddComponent<Image>();
            Color fill = StowSkin.FieldFill;
            if (dim) fill.a *= 0.75f;
            bg.color = fill;
            UiaImages.Round(bg);
            UiaUi.OutlineOf(bg, StowSkin.FieldEdge, 1f);
            UiaIcons.AttachAt(ddGo.transform, UiaIcon.CaretDown, 12f,
                dim ? StowSkin.MutedCyan : Color.Lerp(StowSkin.MutedCyan, UiaTheme.Accent, 0.5f),
                0f, 0.5f, 8f, 0f);
            var lbl = UiaUi.Text(ddGo.transform, "", UiaTheme.SmallSize,
                dim ? StowSkin.MutedCyan : StowSkin.SoftText, TextAlignmentOptions.Left);
            var lrt = (RectTransform)lbl.transform;
            UiaUi.Fill(lrt); lrt.offsetMin = new Vector2(22f, 0f); lrt.offsetMax = new Vector2(-6f, 0f);
            // No-ellipsis rule: a long option (a layout-qualified profile name, the
            // "+ Add rule (UI Ascended class)..." sentinel) steps down to 9pt, then wraps to
            // a second line inside the fixed-height field — never "...".
            Fit(lbl, UiaTheme.SmallSize, 9f, true);
            var dd = ddGo.AddComponent<UiaControls.UiaDropdown>().Init(lbl, options, index);
            dd.OnChanged = onChanged;
            return dd;
        }

        // ---------- rule-tier mapping ----------

        public static int TierIndexOf(int priority)
        {
            RuleTier t = RuleTiers.TierOf(priority);
            if (t == RuleTier.High) return 0;
            if (t == RuleTier.Normal) return 1;
            return 2;
        }

        public static int TierValueAt(int index)
        {
            if (index == 0) return RuleTiers.HighValue;
            if (index == 1) return RuleTiers.NormalValue;
            return RuleTiers.LowValue;
        }

        /// <summary>Stage tags for the test box. Home is tagged defensively — the 0.9.8.0
        /// resolver wave adds the stage and the dry-run may surface it.</summary>
        public static string StageTag(StowStage s)
        {
            if (s == StowStage.Home) return "HOME";
            if (s == StowStage.BeltTool) return "BELT";
            if (s == StowStage.StackMerge) return "STACK";
            if (s == StowStage.FunctionalSocket) return "SOCKET";
            if (s == StowStage.Profile) return "PROFILE";
            if (s == StowStage.Affinity) return "AFFINITY";
            if (s == StowStage.BagDefault) return "DEFAULT";
            if (s == StowStage.Memory) return "MEMORY";
            if (s == StowStage.GenericFallback) return "GENERIC";
            if (s == StowStage.BeltFallback) return "BELT*";
            return "?";
        }

        /// <summary>The router's reason strings already name the profile stages ("profile: X");
        /// with the stage tag in front that prefix is redundant, so strip it.</summary>
        public static string TrimmedReason(StowCandidate c)
        {
            string reason = c.Reason ?? "";
            if (c.Stage == StowStage.Profile && reason.StartsWith("profile: ", StringComparison.Ordinal))
                return reason.Substring(9);
            if (c.Stage == StowStage.BagDefault && reason.StartsWith("bag default: ", StringComparison.Ordinal))
                return reason.Substring(13);
            return reason;
        }

        /// <summary>Deep clone of one Bag Profile (Duplicate inside the ACTIVE layout — the
        /// store's own clone is private and inactive-set-only).</summary>
        public static BagProfile CloneProfile(BagProfile src)
        {
            if (src == null) return null;
            var p = new BagProfile { Name = src.Name, Badge = src.Badge };
            for (int i = 0; i < src.Items.Count; i++)
            {
                var r = src.Items[i];
                if (r != null) p.Items.Add(new ItemRule { Prefab = r.Prefab, Priority = r.Priority });
            }
            for (int i = 0; i < src.Categories.Count; i++)
            {
                var r = src.Categories[i];
                if (r != null) p.Categories.Add(new CategoryRule { Name = r.Name, Priority = r.Priority });
            }
            for (int i = 0; i < src.SlotClasses.Count; i++)
            {
                var r = src.SlotClasses[i];
                if (r != null) p.SlotClasses.Add(new SlotClassRule { Name = r.Name, Priority = r.Priority });
            }
            for (int i = 0; i < src.UIAClasses.Count; i++)
            {
                var r = src.UIAClasses[i];
                if (r != null) p.UIAClasses.Add(new UIAClassRule { Name = r.Name, Priority = r.Priority });
            }
            return p;
        }
    }

    /// <summary>
    /// Shrink-then-wrap fitting for one TMP label (see <see cref="StowShared.Fit"/>). Runs
    /// INSIDE the UGUI layout pass rather than a frame later, so there is no one-frame flash
    /// of an overflowing label on each Refresh rebuild:
    /// <list type="bullet">
    /// <item>Horizontal input: reports the label's single-line width at MaxSize as its
    /// preferred width (priority 2, above the LayoutElement's flexibleWidth-only 1). The
    /// value never depends on the size we pick, so width allocation is stable and the
    /// fit converges in one step (no relayout ping-pong).</item>
    /// <item>Vertical input: the parent has already set our width, so pick the size — MaxSize
    /// if it fits, else the linear shrink (glyph advances and em-based character spacing both
    /// scale with point size), else MinSize + word-wrap — BEFORE the parent reads TMP's
    /// preferred height, which is then the honest (possibly two-line) height.</item>
    /// </list>
    /// Every other ILayoutElement value is -1, which LayoutUtility ignores. TMP's own
    /// setters early-out on an unchanged value, so a repeated pass is a no-op. No statics
    /// (hot-reload safe); the component dies with its label on every rebuild.
    /// </summary>
    internal sealed class StowFitText : MonoBehaviour, ILayoutElement
    {
        public float MaxSize = 13f;
        public float MinSize = 9f;
        public bool AllowWrap = true;

        private TextMeshProUGUI _t;
        private string _measuredText;
        private float _singleAtMax = -1f;
        private string _fitText;
        private float _fitWidth = -1f;

        private TextMeshProUGUI Label
        {
            get { if (_t == null) _t = GetComponent<TextMeshProUGUI>(); return _t; }
        }

        /// <summary>Single-line width of the current text at MaxSize (cached per text).</summary>
        private float SingleLineAtMax()
        {
            var t = Label;
            if (t == null) return -1f;
            string s = t.text ?? "";
            if (_singleAtMax >= 0f && string.Equals(s, _measuredText, StringComparison.Ordinal))
                return _singleAtMax;
            _measuredText = s;
            if (s.Length == 0) { _singleAtMax = 0f; return 0f; }
            float cur = t.fontSize > 0.01f ? t.fontSize : MaxSize;
            float w = 0f;
            try { w = t.GetPreferredValues(s, 32767f, 32767f).x; } catch { w = 0f; }
            // TMP's preferred width ALREADY includes the positive L/R margins
            // (TMP_Text.CalculatePreferredValues adds m_margin.x/z) — scale only the glyph
            // run to MaxSize and count the margins once, not twice.
            float m = Mathf.Max(0f, t.margin.x) + Mathf.Max(0f, t.margin.z);
            _singleAtMax = Mathf.Max(0f, w - m) * (MaxSize / cur) + m;
            return _singleAtMax;
        }

        public void CalculateLayoutInputHorizontal() { SingleLineAtMax(); }

        public void CalculateLayoutInputVertical() { FitNow(); }

        /// <summary>Catch-up for labels no layout pass revisits: a Fill-anchored label (the
        /// inline dropdown's, a teal button's) whose TEXT changes in place is its own layout
        /// root with no controller, so TMP's dirty mark never re-runs the vertical input.
        /// FitNow early-outs on unchanged text + width, so the steady state is two compares.</summary>
        private void LateUpdate() { FitNow(); }

        private void FitNow()
        {
            var t = Label;
            if (t == null) return;
            float avail = ((RectTransform)transform).rect.width;
            if (avail <= 1f) return;   // not laid out yet — the next pass fits
            string s = t.text ?? "";
            if (string.Equals(s, _fitText, StringComparison.Ordinal)
                && Mathf.Abs(avail - _fitWidth) < 0.5f)
                return;
            _fitText = s;
            _fitWidth = avail;

            float need = SingleLineAtMax();
            float size = MaxSize;
            bool wrap = false;
            if (need > avail && need > 0f)
            {
                // 0.97: a few percent of slack for kerning/rounding, so a shrunk single line
                // can never kiss (and paint past) the rect edge.
                // Margins do not scale with point size — ratio the glyph runs only.
                float m = Mathf.Max(0f, t.margin.x) + Mathf.Max(0f, t.margin.z);
                float shrunk = need - m > 0.5f
                    ? MaxSize * (Mathf.Max(0f, avail - m) / (need - m)) * 0.97f
                    : MaxSize;
                if (shrunk >= MinSize) size = Mathf.Floor(shrunk * 4f) / 4f;
                else { size = MinSize; wrap = AllowWrap; }
            }
            if (t.enableAutoSizing) t.enableAutoSizing = false;
            if (Mathf.Abs(t.fontSize - size) > 0.01f) t.fontSize = size;
            if (t.enableWordWrapping != wrap) t.enableWordWrapping = wrap;
        }

        public float minWidth { get { return -1f; } }
        public float preferredWidth { get { return SingleLineAtMax(); } }
        public float flexibleWidth { get { return -1f; } }
        public float minHeight { get { return -1f; } }
        public float preferredHeight { get { return -1f; } }
        public float flexibleHeight { get { return -1f; } }
        public int layoutPriority { get { return 2; } }
    }
}
