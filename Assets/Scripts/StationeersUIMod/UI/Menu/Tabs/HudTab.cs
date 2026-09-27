using StationeersUIMod.UI.Hud;
using StationeersUIMod.UI.Menu.Kit;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Tabs
{
    /// <summary>HUD-half comfort settings — the Kit v2 PILOT tab: content lives in Section
    /// cards, the old Advanced-only extras live behind per-section "More options" disclosures
    /// (remembered per section), every row registers with the settings search, and feedback is
    /// an inline status line instead of a centre-screen toast. Behaviour is unchanged from the
    /// pre-pilot tab: Power &amp; glitch sits directly under Diegetic tiers while diegetics is on
    /// (D-020) and is reachable through Show's More options while it is off; Maintenance's
    /// destructive reset uses the kit ConfirmButton (same two-step, same 5s window).</summary>
    public sealed class HudTab : IUiaTab
    {
        public string Title => "HUD";

        // Maintenance feedback (the InlineStatus content). INSTANCE fields on purpose: they
        // survive the Refresh() that follows each maintenance action (tab instances persist),
        // die with the tab object on a theme Restyle/teardown (transient feedback, not state),
        // and need no Shutdown reset.
        private string _maintMsg;
        private UiaComposite.StatusKind _maintKind;

        public void Build(RectTransform content, bool advanced)
        {
            ScrollRect scroll;
            var col = UiaUi.ScrollView(content, out scroll, UiaTheme.Gap);
            UiaUi.Fill((RectTransform)scroll.gameObject.transform);

            BuildLook(col);
            BuildShow(col);
            BuildHideVanilla(col);
            BuildMaintenance(col);
        }

        // ---------- Look ----------

        private void BuildLook(Transform col)
        {
            var sec = UiaComposite.Section(col, "hud.look", "Look",
                "Layout, scale, font and curvature are all authored in the F9 HUD Designer - " +
                "this tab only points there, so the same knob is never edited in two places.");
            UiaControls.Note(sec.Body, "Scale, font and curvature are built in the HUD Designer, where you build your layout.");
            var f9 = UiaControls.Button(sec.Body, "Open the HUD Designer (F9)", OpenDesigner,
                -1f, UiaTheme.RowH, UiaControls.ButtonStyle.Primary);
            UiaSearch.RegisterRow(Title, "hud.look", "Open the HUD Designer (F9)",
                UiaSearch.MakeJump(f9.gameObject));
        }

        // ---------- Show ----------

        private void BuildShow(Transform col)
        {
            bool diegetics = HudConfig.DiegeticTiers.Value;
            // More options only exists while diegetics is OFF: that is where Power & glitch
            // parks so it stays reachable (the old Advanced-always behaviour); with diegetics
            // ON the same rows sit in the body, directly under the toggle (D-020), and a
            // disclosure would have nothing left to hold.
            var sec = UiaComposite.Section(col, "hud.show", "Show", null, !diegetics);

            // Diegetics drives whether low-battery flicker/glitch is even visible to a Bare-tier
            // player, so Power & glitch sits directly beneath it (FlorpyDorp, D-020). Refresh()
            // on toggle so the section reshapes immediately, without reopening the tab.
            //
            // B5: every HudConfig key on this tab (bar HudTheme.Exclude's per-machine/perf knobs)
            // TRAVELS WITH THE PROFILE THEME, so each write must MarkThemeChanged — exactly what F9's
            // own Toggle/FloatSlider do. Without it the edit only lives in the .cfg and the
            // profile's stored theme puts the old value back on the next relaunch / profile
            // switch. A SHIPPED (read-only) theme needs nothing extra here: the store's save gate
            // (HudProfileStore.RefuseActiveSave) drops that write and toasts once, F9's own path.
            var diegeticsToggle = UiaControls.ToggleRow(sec.Body, "Diegetic tiers (words when unpowered)",
                HudConfig.DiegeticTiers.Value,
                v => { HudConfig.DiegeticTiers.Value = v; Features.HudProfileStore.MarkThemeChanged(); UiaControlCenter.Refresh(); });
            UiaSearch.RegisterRow(Title, "hud.show", "Diegetic tiers",
                UiaSearch.MakeJump(diegeticsToggle.transform.parent.gameObject));

            if (diegetics)
            {
                PowerGlitchRows(sec.Body, null);
            }

            // Same B5 fix: FlickerAnimations travels with the theme too (the shipped profiles
            // carry cfg:FlickerAnimations), and this row had the identical silent revert once.
            var flicker = UiaControls.ToggleRow(sec.Body, "Flicker / boot animations", HudConfig.FlickerAnimations.Value,
                v => { HudConfig.FlickerAnimations.Value = v; Features.HudProfileStore.MarkThemeChanged(); });
            UiaSearch.RegisterRow(Title, "hud.show", "Flicker / boot animations",
                UiaSearch.MakeJump(flicker.transform.parent.gameObject));

            var vitals = UiaControls.ToggleRow(sec.Body, "Detailed vitals tooltips (mood + hygiene)",
                HudConfig.DetailedVitalsTooltips.Value,
                v => { HudConfig.DetailedVitalsTooltips.Value = v; Features.HudProfileStore.MarkThemeChanged(); });
            UiaSearch.RegisterRow(Title, "hud.show", "Detailed vitals tooltips",
                UiaSearch.MakeJump(vitals.transform.parent.gameObject));

            if (!diegetics && sec.Extras != null)
            {
                UiaControls.Note(sec.Extras, "Power & glitch tuning. These only show anything while Diegetic tiers is on.");
                PowerGlitchRows(sec.Extras, sec);
            }
        }

        /// <summary>The Power &amp; glitch block — ONE builder for both homes (the Show body
        /// while diegetics is on, Show's More options while it is off), so the rows can never
        /// drift apart. <paramref name="revealSection"/> non-null = the rows live inside that
        /// section's collapsed disclosure and a search jump must open it first.</summary>
        private void PowerGlitchRows(Transform parent, UiaComposite.SectionHandle revealSection)
        {
            UiaControls.Header(parent, "Power & glitch");
            var dropouts = UiaControls.ToggleRow(parent, "Low-power dropouts", HudConfig.LowPowerDropouts.Value,
                v => { HudConfig.LowPowerDropouts.Value = v; Features.HudProfileStore.MarkThemeChanged(); });
            var threshold = UiaControls.SliderRow(parent, "Low-power threshold (%)", 0f, 40f,
                HudConfig.LowPowerThreshold.Value,
                v => { HudConfig.LowPowerThreshold.Value = v; Features.HudProfileStore.MarkThemeChanged(); }, "0");
            var glitch = UiaControls.ToggleRow(parent, "Power-transition glitch", HudConfig.FxGlitchOn.Value,
                v => { HudConfig.FxGlitchOn.Value = v; Features.HudProfileStore.MarkThemeChanged(); });

            System.Action reveal = null;
            if (revealSection != null)
            {
                var s = revealSection;
                reveal = () => s.SetExpanded(true);
            }
            UiaSearch.RegisterRow(Title, "hud.show", "Low-power dropouts",
                UiaSearch.MakeJump(dropouts.transform.parent.gameObject, reveal));
            UiaSearch.RegisterRow(Title, "hud.show", "Low-power threshold",
                UiaSearch.MakeJump(threshold.transform.parent.gameObject, reveal));
            UiaSearch.RegisterRow(Title, "hud.show", "Power-transition glitch",
                UiaSearch.MakeJump(glitch.transform.parent.gameObject, reveal));
        }

        // ---------- Hide vanilla panels ----------

        private void BuildHideVanilla(Transform col)
        {
            var sec = UiaComposite.Section(col, "hud.hide", "Hide vanilla panels",
                "The vanilla HUD objects stay alive - only their visibility changes, through the " +
                "game's own panel-visibility path, so switching back is always safe.");
            var hands = UiaControls.ToggleRow(sec.Body, "Hands panel", UIAConfig.HideVanillaHands.Value,
                v => UIAConfig.HideVanillaHands.Value = v);
            var clothing = UiaControls.ToggleRow(sec.Body, "Clothing panel", UIAConfig.HideVanillaClothing.Value,
                v => UIAConfig.HideVanillaClothing.Value = v);
            var status = UiaControls.ToggleRow(sec.Body, "Status / moodlet panel", UIAConfig.HideVanillaStatus.Value,
                v => UIAConfig.HideVanillaStatus.Value = v);
            var cluster = UiaControls.ToggleRow(sec.Body, "Instrument cluster (bottom-right)", UIAConfig.HideVanillaPlayerState.Value,
                v => UIAConfig.HideVanillaPlayerState.Value = v);
            UiaSearch.RegisterRow(Title, "hud.hide", "Hands panel",
                UiaSearch.MakeJump(hands.transform.parent.gameObject));
            UiaSearch.RegisterRow(Title, "hud.hide", "Clothing panel",
                UiaSearch.MakeJump(clothing.transform.parent.gameObject));
            UiaSearch.RegisterRow(Title, "hud.hide", "Status / moodlet panel",
                UiaSearch.MakeJump(status.transform.parent.gameObject));
            UiaSearch.RegisterRow(Title, "hud.hide", "Instrument cluster",
                UiaSearch.MakeJump(cluster.transform.parent.gameObject));
        }

        // ---------- Maintenance ----------

        private void BuildMaintenance(Transform col)
        {
            // The whole toolbox sits behind More options (it replaced the old Advanced-only
            // gating): a player in trouble finds it from the section title or the search, and
            // nobody trips over a destructive button while browsing.
            var sec = UiaComposite.Section(col, "hud.maintenance", "Maintenance",
                "Repair and reset tools for the mod's config folders and the shipped themes.",
                true);
            UiaControls.Note(sec.Body, "Repair and reset tools live under More options below.");
            // Inline feedback, next to the buttons that caused it (Kit v2's toast replacement).
            // Built from the instance fields so the message survives the Refresh() each action
            // triggers to repaint the tab.
            var statusLine = UiaComposite.InlineStatus(sec.Body, _maintMsg, _maintKind);

            var ex = sec.Extras;
            if (ex == null) return;

            // The NON-destructive repair goes first, above the two-click nuke: it is the one a
            // player in trouble should reach for, and it can never make anything worse.
            UiaControls.Note(ex, "Recreates every folder the mod keeps settings in (UI Themes, Stow Profiles, bag assignments, per-world layouts) and re-seeds the shipped UI Themes and Stow Profiles. It only ever ADDS what is missing - nothing you made is touched, so there is no confirm. Use it if you deleted part of the config folder while the game was running.");
            var repairBtn = UiaControls.Button(ex, "Repair config folders", RepairConfigFolders, -1f, UiaTheme.RowH);
            // The persistent half of the report (the status line is the at-a-glance half).
            // Rendered only once the button has been pressed this session; Refresh() rebuilds
            // the tab, which is how the result appears without the player reopening F10.
            string lastRepair = Core.ConfigTreeRepair.LastResult;
            if (!string.IsNullOrEmpty(lastRepair)) UiaControls.Note(ex, lastRepair);

            UiaUi.Go("sp", ex).AddComponent<LayoutElement>().preferredHeight = 6f;
            // Ask the STORE, not StationeersUIMod.ModDirectory: a mod folder that exists but has no
            // HudProfiles subfolder is just as unrestorable as no mod folder at all, and only the
            // store knows the shape it needs. (The store refuses the wipe in that case too.)
            bool devOffline = !Features.HudProfileStore.ShippedFolderAvailable;
            UiaControls.Note(ex, devOffline
                ? "Restore shipped themes needs the mod's installed folder, which isn't available right now (the F6 dev flow has none) - the button below will just explain that if you click it."
                : "Puts all four shipped themes back exactly as shipped, undoing any edits you made to them. Your own UI Themes are never touched. The button asks once before it acts.");

            GameObject restoreRow;
            var reveal = sec;
            if (devOffline)
            {
                // No confirm step when there is nothing to destroy: the click just explains,
                // exactly as the old single-click toast did — inline now.
                var b = UiaControls.Button(ex, "Restore shipped themes", () =>
                {
                    SetMaintStatus("Restore shipped themes needs the mod's installed folder - unavailable under the F6 dev flow.",
                        UiaComposite.StatusKind.Error, statusLine);
                }, -1f, UiaTheme.RowH, UiaControls.ButtonStyle.Danger);
                restoreRow = b.gameObject;
            }
            else
            {
                // The kit ConfirmButton replaces the hand-rolled 5-second arm toast: same two
                // clicks, same window, but the "Sure?" happens where the button is.
                var cb = UiaComposite.ConfirmButton(ex, "Restore shipped themes", RestoreShippedThemes,
                    -1f, UiaTheme.RowH);
                restoreRow = cb.gameObject;
            }

            UiaSearch.RegisterRow(Title, "hud.maintenance", "Repair config folders",
                UiaSearch.MakeJump(repairBtn.gameObject, () => reveal.SetExpanded(true)));
            UiaSearch.RegisterRow(Title, "hud.maintenance", "Restore shipped themes",
                UiaSearch.MakeJump(restoreRow, () => reveal.SetExpanded(true)));
        }

        /// <summary>Set the maintenance feedback both LIVE (the built status line, for actions
        /// that do not rebuild the tab) and in the instance fields the next Build reads (for
        /// the ones that do).</summary>
        private void SetMaintStatus(string msg, UiaComposite.StatusKind kind,
            UiaComposite.InlineStatusHandle live)
        {
            _maintMsg = msg;
            _maintKind = kind;
            if (live != null) live.Show(msg, kind);
        }

        private static void OpenDesigner()
        {
            try
            {
                var inst = global::StationeersUIMod.StationeersUIMod.Instance;
                if (inst != null) { UiaControlCenter.Close(); inst.ToggleHudEditor(); }
            }
            catch { }
        }

        /// <summary>SINGLE click, no confirm — <see cref="Core.ConfigTreeRepair.Run"/> is strictly
        /// additive (folders created only when absent, shipped content seeded only when missing), so
        /// there is nothing to arm against. Reports twice, deliberately: the inline status line for
        /// the at-a-glance "it worked", and the persistent note rebuilt into this tab by
        /// <see cref="UiaControlCenter.Refresh"/> for the detail a player can still read a minute
        /// later. Both read the SAME string off the store, so they can never disagree. Run() is
        /// already fail-soft per step; the catch here is the same belt-and-braces the restore button
        /// carries, so a surprise from the UI half still lands as a message rather than a dead
        /// button.</summary>
        private void RepairConfigFolders()
        {
            try
            {
                int created = Core.ConfigTreeRepair.Run();
                _maintMsg = Core.ConfigTreeRepair.LastResult;
                _maintKind = created > 0 ? UiaComposite.StatusKind.Good : UiaComposite.StatusKind.Info;
                UiaControlCenter.Refresh();
            }
            catch (System.Exception e)
            {
                Core.UIALog.Warn("Repair config folders failed: " + e.Message);
                _maintMsg = "Repair config folders failed - see the log.";
                _maintKind = UiaComposite.StatusKind.Error;
                UiaControlCenter.Refresh();
            }
        }

        /// <summary>The F10 half of the shipped-theme reset affordance (the console has its own
        /// general-purpose `uiareset` nuke — see Core/FinderCommands.cs — this is the scoped,
        /// discoverable, in-menu version that touches ONLY the shipped themes). The kit
        /// ConfirmButton owns the two-step arm; this runs on the confirmed click: wipe the
        /// shipped set and immediately re-seed it fresh from the mod folder
        /// (<see cref="Features.HudProfileStore.ResetShippedProfiles"/>). Reloads the live
        /// document afterward if the active profile was one of the wiped names, so the change is
        /// visible immediately rather than after a restart.</summary>
        private void RestoreShippedThemes()
        {
            string modDir = global::StationeersUIMod.StationeersUIMod.ModDirectory;
            if (!Features.HudProfileStore.ShippedFolderAvailable)
            {
                _maintMsg = "Restore shipped themes needs the mod's installed folder - unavailable under the F6 dev flow.";
                _maintKind = UiaComposite.StatusKind.Error;
                UiaControlCenter.Refresh();
                return;
            }

            try
            {
                string activeBefore = HudConfig.HudActiveProfile != null ? HudConfig.HudActiveProfile.Value : null;
                Features.HudProfileStore.ResetShippedProfiles(modDir);
                string activeAfter = HudConfig.HudActiveProfile != null ? HudConfig.HudActiveProfile.Value : null;
                if (!string.IsNullOrEmpty(activeAfter)
                    && !string.Equals(activeBefore, activeAfter, System.StringComparison.OrdinalIgnoreCase))
                {
                    var keep = Features.HudProfileStore.Active;
                    Features.HudProfileStore.LoadActive(activeAfter, () => keep != null ? keep.Clone() : new HudDocument { Name = activeAfter });
                    HudDocumentHistory.Clear();
                }
                _maintMsg = "Shipped themes restored.";
                _maintKind = UiaComposite.StatusKind.Good;
                UiaControlCenter.Refresh();
            }
            catch (System.Exception e)
            {
                Core.UIALog.Warn("Restore shipped themes failed: " + e.Message);
                _maintMsg = "Restore shipped themes failed - see the log.";
                _maintKind = UiaComposite.StatusKind.Error;
                UiaControlCenter.Refresh();
            }
        }
    }
}
