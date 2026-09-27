using System;
using StationeersUIMod.Core;
using StationeersUIMod.Features;
using StationeersUIMod.UI.Menu.Kit;
using UnityEngine;

namespace StationeersUIMod.UI.Menu.Tabs.Storage
{
    /// <summary>
    /// The Simple-mode "RETURN HOME" sub-tab (Simple plan §10.1, built in mockup 3's style —
    /// ANSWER #26): the home count, the in-hand readout (the Simple tester's tool), the tips
    /// switch, and the two maintenance buttons. No capture, no profile UI, ever — that whole
    /// surface belongs to Complex mode.
    ///
    /// <para>Everything here READS <see cref="StowHomeStore"/> (client-safe, config-XML-backed)
    /// and writes only config; the two ConfirmButtons call the store's own maintenance API
    /// (<see cref="StowHomeStore.ForgetThisWorld"/> / <see cref="StowHomeStore.RelearnFromCarried"/>)
    /// and surface the returned counts.</para>
    /// </summary>
    internal static class SimpleStowPage
    {
        private const string TabTitle = "SmartStow";

        public static void Build(Transform col)
        {
            // Borderless-page contract (manila seam): no page-level panel/border here — the
            // FolderTabs Card is the one bordered frame; this page is kit Sections + notes.
            var st = StowShared.State;
            var sec = UiaComposite.Section(col, "smartstow.simple", "Return Home",
                "Every item goes back to the bag and slot you last deliberately put it in. Move "
                + "it by hand and its home moves. A brand-new item is routed once by the smart "
                + "chain, and wherever it lands becomes its home. Taking an item out never "
                + "changes its home.");

            int homes;
            string handLine, homeLine;
            StowShared.SimpleReadout(out homes, out handLine, out homeLine);

            bool worldReady = false;
            try { worldReady = StowHomeStore.WorldReady; } catch { }

            var countT = UiaControls.Note(sec.Body, worldReady || homes > 0
                ? "Homes remembered in this world: " + homes
                : "Homes remembered in this world: " + homes + " (world still settling - the count fills in a few seconds after loading)");
            countT.color = UiaTheme.Text;
            UiaSearch.RegisterRow(TabTitle, "smartstow.simple", "Homes remembered in this world",
                UiaSearch.MakeJump(countT.gameObject));

            // ---- the in-hand readout ----
            if (handLine == null)
            {
                UiaControls.Note(sec.Body, "No character - load into a world to see what your held item calls home.");
            }
            else
            {
                var hand = UiaControls.Note(sec.Body, handLine);
                hand.color = UiaTheme.Text;
                if (homeLine != null) StowShared.SubNote(sec.Body, homeLine);
            }

            var notes = UiaControls.ToggleRow(sec.Body,
                "Tell me when an item has no home yet, or its home is full",
                StowModeConfig.SimpleNotes, v => StowModeConfig.SimpleNotes = v);
            UiaSearch.RegisterRow(TabTitle, "smartstow.simple", "Tell me when an item has no home yet",
                UiaSearch.MakeJump(notes.transform.parent.gameObject));

            // ---- maintenance: the two confirmed, destructive-ish buttons ----
            var forget = UiaComposite.ConfirmButton(sec.Body, "Forget homes in this world", () =>
            {
                int dropped = -1;
                try { dropped = StowHomeStore.ForgetThisWorld(); }
                catch (Exception e) { UIALog.Warn("Forget homes failed: " + e.Message); }
                SimpleStatus(dropped >= 0
                        ? "Forgot " + dropped + " home(s) in this world. Items get new homes as you place them."
                        : "Could not forget the homes (see the log).",
                    dropped >= 0 ? UiaComposite.StatusKind.Good : UiaComposite.StatusKind.Error);
                StowShared.InvalidateSimple();
                UiaControlCenter.Refresh();
            }, -1f, UiaTheme.RowH);
            UiaSearch.RegisterRow(TabTitle, "smartstow.simple", "Forget homes in this world",
                UiaSearch.MakeJump(forget.gameObject));

            var relearn = UiaComposite.ConfirmButton(sec.Body, "Re-learn homes from what I carry now", () =>
            {
                int now = -1;
                try { now = StowHomeStore.RelearnFromCarried(); }
                catch (Exception e) { UIALog.Warn("Re-learn homes failed: " + e.Message); }
                SimpleStatus(now >= 0
                        ? "Re-learned from your current inventory - " + now + " home(s) remembered."
                        : "Could not re-learn the homes (see the log).",
                    now >= 0 ? UiaComposite.StatusKind.Good : UiaComposite.StatusKind.Error);
                StowShared.InvalidateSimple();
                UiaControlCenter.Refresh();
            }, -1f, UiaTheme.RowH);
            UiaSearch.RegisterRow(TabTitle, "smartstow.simple", "Re-learn homes from what I carry now",
                UiaSearch.MakeJump(relearn.gameObject));

            UiaComposite.InlineStatus(sec.Body, st.SimpleMsg, st.SimpleKind);

            // ---- exclusions: readable here, changed in Complex (S-10 / §10.1) ----
            int excluded = 0;
            try { excluded = BagProfileStore.ExcludedCount; } catch { }
            if (excluded > 0)
                StowShared.SubNote(col, excluded
                    + (excluded == 1 ? " container is" : " containers are")
                    + " set to never stow - change this in Complex mode's Bags column.");
        }

        private static void SimpleStatus(string msg, UiaComposite.StatusKind kind)
        {
            var st = StowShared.State; st.SimpleMsg = msg; st.SimpleKind = kind;
        }
    }
}
