using StationeersUIMod.Overlay;
using StationeersUIMod.UI.Hud;
using StationeersUIMod.UI.Menu.Kit;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Tabs
{
    /// <summary>HUD-half comfort settings. Simple = a pointer into the F9 HUD Designer (which now
    /// owns scale/font/curvature) + show/hide toggles + the hide-vanilla group, PLUS Power & glitch
    /// whenever Diegetic tiers is on (it is meaningless with diegetics off - nothing to see);
    /// Advanced always shows Power & glitch and adds another shortcut into the F9 Designer near
    /// Maintenance.</summary>
    public sealed class HudTab : IUiaTab
    {
        public string Title => "HUD";

        // Two-click confirm for the destructive "Restore shipped themes" button below — an
        // INSTANCE field (not static) so it dies with this tab object rather than needing its own
        // Shutdown reset; HudTab is rebuilt fresh whenever the Control Center's tab list is.
        private float _restoreShippedArmUntil;

        public void Build(RectTransform content, bool advanced)
        {
            ScrollRect scroll;
            var col = UiaUi.ScrollView(content, out scroll, UiaTheme.Gap);
            UiaUi.Fill((RectTransform)scroll.gameObject.transform);

            UiaControls.Header(col, "Look");
            // Layout, scale, font and curvature are all authored in the F9 HUD Designer now -
            // this tab only points there so the same knob is never edited in two places at once.
            UiaControls.Note(col, "Scale, font and curvature are built in the HUD Designer, where you build your layout.");
            UiaControls.Button(col, "Open the HUD Designer (F9)", OpenDesigner, -1f, UiaTheme.RowH, UiaControls.ButtonStyle.Primary);

            UiaControls.Header(col, "Show");
            // Diegetics drives whether low-battery flicker/glitch is even visible to a Bare-tier
            // player, so Power & glitch sits directly beneath it (FlorpyDorp, D-020) instead of
            // being buried further down under an Advanced-only header that used to separate the
            // two. Refresh() on toggle so a Simple-mode player sees the section appear/disappear
            // immediately, without needing to reopen the tab.
            //
            // B5: every HudConfig key on this tab (bar HudTheme.Exclude's per-machine/perf knobs)
            // TRAVELS WITH THE PROFILE THEME, so each write must MarkThemeChanged — exactly what F9's
            // own Toggle/FloatSlider do (HudEditorWindow.cs:2554-2564). Without it the edit only lives
            // in the .cfg and the profile's stored theme puts the old value back on the next
            // relaunch / profile switch. A SHIPPED (read-only) theme needs nothing extra here: the
            // store's save gate (HudProfileStore.RefuseActiveSave) drops that write and toasts
            // "Shipped theme - read-only" once, the same path F9 relies on.
            UiaControls.ToggleRow(col, "Diegetic tiers (words when unpowered)", HudConfig.DiegeticTiers.Value,
                v => { HudConfig.DiegeticTiers.Value = v; Features.HudProfileStore.MarkThemeChanged(); UiaControlCenter.Refresh(); });

            // SIMPLE mode: only worth showing while Diegetics is actually on (there is nothing to
            // tune otherwise). ADVANCED mode always shows it, like every other Advanced knob here.
            if (advanced || HudConfig.DiegeticTiers.Value)
            {
                UiaControls.Header(col, "Power & glitch");
                UiaControls.ToggleRow(col, "Low-power dropouts", HudConfig.LowPowerDropouts.Value,
                    v => { HudConfig.LowPowerDropouts.Value = v; Features.HudProfileStore.MarkThemeChanged(); });
                UiaControls.SliderRow(col, "Low-power threshold (%)", 0f, 40f, HudConfig.LowPowerThreshold.Value,
                    v => { HudConfig.LowPowerThreshold.Value = v; Features.HudProfileStore.MarkThemeChanged(); }, "0");
                UiaControls.ToggleRow(col, "Power-transition glitch", HudConfig.FxGlitchOn.Value,
                    v => { HudConfig.FxGlitchOn.Value = v; Features.HudProfileStore.MarkThemeChanged(); });
            }

            // Same B5 fix: FlickerAnimations travels with the theme too (the shipped profiles carry
            // cfg:FlickerAnimations), and this pre-existing row had the identical silent revert.
            UiaControls.ToggleRow(col, "Flicker / boot animations", HudConfig.FlickerAnimations.Value,
                v => { HudConfig.FlickerAnimations.Value = v; Features.HudProfileStore.MarkThemeChanged(); });
            UiaControls.ToggleRow(col, "Detailed vitals tooltips (mood + hygiene)", HudConfig.DetailedVitalsTooltips.Value,
                v => { HudConfig.DetailedVitalsTooltips.Value = v; Features.HudProfileStore.MarkThemeChanged(); });

            UiaControls.Header(col, "Hide vanilla panels");
            UiaControls.ToggleRow(col, "Hands panel", UIAConfig.HideVanillaHands.Value, v => UIAConfig.HideVanillaHands.Value = v);
            UiaControls.ToggleRow(col, "Clothing panel", UIAConfig.HideVanillaClothing.Value, v => UIAConfig.HideVanillaClothing.Value = v);
            UiaControls.ToggleRow(col, "Status / moodlet panel", UIAConfig.HideVanillaStatus.Value, v => UIAConfig.HideVanillaStatus.Value = v);
            UiaControls.ToggleRow(col, "Instrument cluster (bottom-right)", UIAConfig.HideVanillaPlayerState.Value, v => UIAConfig.HideVanillaPlayerState.Value = v);

            if (!advanced) return;

            UiaUi.Go("sp", col).AddComponent<LayoutElement>().preferredHeight = 6f;
            UiaControls.Note(col, "Build your own layout and tune the glass effects in the HUD Designer.");
            UiaControls.Button(col, "Open the HUD Designer (F9)", OpenDesigner, -1f, UiaTheme.RowH, UiaControls.ButtonStyle.Primary);

            // ---- Maintenance: the one home for the shipped-theme reset affordance (audit 06 P1 +
            // the playtester incident — "my shipped theme is a mess and I don't know how to fix it").
            UiaUi.Go("sp2", col).AddComponent<LayoutElement>().preferredHeight = 6f;
            UiaControls.Header(col, "Maintenance (advanced)");

            // The NON-destructive repair goes first, above the two-click nuke: it is the one a
            // player in trouble should reach for, and it can never make anything worse.
            UiaControls.Note(col, "Recreates every folder the mod keeps settings in (HUD Themes, Stow Profiles, bag assignments, per-world layouts) and re-seeds the shipped HUD Themes and Stow Profiles. It only ever ADDS what is missing - nothing you made is touched, so there is no confirm. Use it if you deleted part of the config folder while the game was running.");
            UiaControls.Button(col, "Repair config folders", RepairConfigFolders, -1f, UiaTheme.RowH);
            // The persistent half of the report (the toast is the at-a-glance half). Rendered only
            // once the button has been pressed this session; Refresh() rebuilds the tab, which is
            // how the result appears without the player reopening F10.
            string lastRepair = Core.ConfigTreeRepair.LastResult;
            if (!string.IsNullOrEmpty(lastRepair)) UiaControls.Note(col, lastRepair);

            UiaUi.Go("sp3", col).AddComponent<LayoutElement>().preferredHeight = 6f;
            // Ask the STORE, not StationeersUIMod.ModDirectory: a mod folder that exists but has no
            // HudProfiles subfolder is just as unrestorable as no mod folder at all, and only the
            // store knows the shape it needs. (The store refuses the wipe in that case too.)
            bool devOffline = !Features.HudProfileStore.ShippedFolderAvailable;
            UiaControls.Note(col, devOffline
                ? "Restore shipped themes needs the mod's installed folder, which isn't available right now (the F6 dev flow has none) - the button below will just explain that if you click it."
                : "Puts all four shipped themes back exactly as shipped, undoing any edits you made to them. Your own HUD Themes are never touched. Click twice to confirm.");
            UiaControls.Button(col, "Restore shipped themes", RestoreShippedThemes, -1f, UiaTheme.RowH, UiaControls.ButtonStyle.Danger);
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
        /// there is nothing to arm against. Reports twice, deliberately: a transient centre-screen
        /// toast for the at-a-glance "it worked", and the persistent note rebuilt into this tab by
        /// <see cref="UiaControlCenter.Refresh"/> for the detail a player can still read a minute
        /// later. Both read the SAME string off the store, so they can never disagree. Run() is
        /// already fail-soft per step; the catch here is the same belt-and-braces the restore button
        /// carries, so a surprise from the UI half still lands as a toast rather than a dead
        /// button.</summary>
        private void RepairConfigFolders()
        {
            try
            {
                int created = Core.ConfigTreeRepair.Run();
                Toast.Show(Core.ConfigTreeRepair.LastResult,
                    created > 0 ? Theme.TextPrimary : Theme.TextDim, 3f);
                UiaControlCenter.Refresh();
            }
            catch (System.Exception e)
            {
                Core.UIALog.Warn("Repair config folders failed: " + e.Message);
                Toast.Show("Repair config folders failed - see the log.", Theme.Critical, 3.5f);
            }
        }

        /// <summary>The F10 half of the shipped-theme reset affordance (the console has its own
        /// general-purpose `uiareset` nuke — see Core/FinderCommands.cs — this is the scoped,
        /// discoverable, in-menu version that touches ONLY the two shipped themes). Two clicks: the
        /// first arms a 5-second confirm window and toasts what is about to happen; the second,
        /// within that window, actually wipes the shipped set and immediately re-seeds it fresh from
        /// the mod folder (<see cref="Features.HudProfileStore.ResetShippedProfiles"/>). Reloads the
        /// live document afterward if the active profile was one of the wiped names, so the change is
        /// visible immediately rather than after a restart.</summary>
        private void RestoreShippedThemes()
        {
            string modDir = global::StationeersUIMod.StationeersUIMod.ModDirectory;
            if (!Features.HudProfileStore.ShippedFolderAvailable)
            {
                Toast.Show("Restore shipped themes needs the mod's installed folder - unavailable under the F6 dev flow.", Theme.Critical, 3.5f);
                return;
            }

            float now = Time.unscaledTime;
            if (now > _restoreShippedArmUntil)
            {
                _restoreShippedArmUntil = now + 5f;
                Toast.Show("Click 'Restore shipped themes' again within 5s to confirm - this reverts any edits to the shipped themes.", Theme.Critical, 5f);
                return;
            }
            _restoreShippedArmUntil = 0f;

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
                Toast.Show("Shipped themes restored.", Theme.TextPrimary, 3f);
                UiaControlCenter.Refresh();
            }
            catch (System.Exception e)
            {
                Core.UIALog.Warn("Restore shipped themes failed: " + e.Message);
                Toast.Show("Restore shipped themes failed - see the log.", Theme.Critical, 3.5f);
            }
        }
    }
}
