using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using StationeersUIMod.Features;
using StationeersUIMod.Overlay;
using StationeersUIMod.UI.Hud;
using UnityEngine;

namespace StationeersUIMod.Testing
{
    /// <summary>
    /// The VISUAL half of <c>uiatest</c> (suite <c>shots</c>): posed screenshots written to
    /// <c>BepInEx/config/StationeersUIMod/TestResults/shots/&lt;timestamp&gt;/</c> for a human (or the
    /// orchestrating agent) to judge afterwards — the radial chrome under each of the four shipped
    /// themes (wheel open / a hovered branch wedge with its action word / The Hub with a recent item),
    /// the F10 HUD tab in Simple mode, and a Rangefinder element in the suited tier.
    ///
    /// <para>VERDICT. A screenshot is reported <c>CAPTURED &lt;path&gt;</c> — its own state in the totals,
    /// NEVER a pass: that a PNG was written proves nothing about what it shows (safety review). Only the
    /// real assertions these tests make about the posed state (the action word, the recent-item wedge,
    /// the active tab, the view being built) count as checks.</para>
    ///
    /// <para>THEMES are shown as in-memory clones under a throwaway name
    /// (<c>UiaTestHarness.ApplyShippedTheme</c>) - the profile setting is never moved and no shipped
    /// theme file can receive an autosave while the cycle runs.</para>
    ///
    /// <para>CAPTURE. <c>ScreenCapture.CaptureScreenshotAsTexture</c> exists in the game's
    /// UnityEngine.ScreenCaptureModule.dll (Unity 2022.3.62), but the dev build does not reference that
    /// module (Dev/StationeersUIMod.Dev.csproj), so the harness uses the same primitives the game's own
    /// screenshot code uses — <c>Texture2D.ReadPixels</c> + <c>EncodeToPNG</c> (27758 GameManager.cs:1035-1036,
    /// CinematicCamera.cs:535-537) — read from the BACK BUFFER at <c>WaitForEndOfFrame</c> (the game's own
    /// idiom, Label.cs:154), i.e. after every camera, every screen-space-overlay canvas and the ImGui layer
    /// have drawn. Both modules (CoreModule, ImageConversionModule) are already referenced.</para>
    ///
    /// <para>HOVER. The radial computes its hover from the live pointer inside <c>RadialMenu.Draw</c>
    /// (ImGui's <c>io.MousePos</c>, refreshed by ImGuiManager.PrepareImGuiFrame inside the same LateUpdate
    /// that then calls ImGuiWindowManager.Draw — 27758 ImGuiManager.cs:151-216, Draw at :192 — so no
    /// coroutine can interleave between the pointer read and the radial's Draw). To pose a hover without moving the
    /// OS cursor, a Harmony prefix on the radial's own <c>RadialMenu.HoverTick</c> — the call Draw makes
    /// right after the hover is final — overwrites the resolved hover index; everything downstream (the
    /// wedge highlight, the hub readout, the D-022 action word, the published hint context) is then the
    /// real renderer's output. Inert unless a shot sets an index; removed at the end of every run.</para>
    /// </summary>
    internal static partial class UiaTestHarness
    {
        private static bool _f10Touched, _f10WasOpen;
        private static int _f10WasTab = -1;

        private static void ResetShotStatics()
        {
            _f10Touched = false;
            _f10WasOpen = false;
            _f10WasTab = -1;
        }

        private static IEnumerator SuiteShots()
        {
            yield return Test("shots.themes", "Radial chrome under each shipped theme: wheel open / a hovered branch / The Hub with a recent item", ShotsThemes);
            yield return Test("shots.f10", "The F10 HUD tab in Simple mode", ShotsF10);
            yield return Test("shots.rangefinder", "A Rangefinder element on screen in the suited tier", ShotsRangefinder);
        }

        // ------------------------------------------------------------------ hover driver

        private static bool HookHover(TestCase t)
        {
            if (_hoverHooked) return true;
            try
            {
                EnsureHarmony();
                var target = AccessTools.Method(typeof(RadialMenu), "HoverTick");
                var pre = AccessTools.Method(typeof(UiaTestHarness), "HoverTickPrefix");
                if (target == null || pre == null)
                {
                    t.Info("hover driver unavailable (RadialMenu.HoverTick not found) - hover shots show whatever the real pointer hovers");
                    return false;
                }
                _harmony.Patch(target, prefix: new HarmonyMethod(pre));
                _hoverHooked = true;
                return true;
            }
            catch (Exception e)
            {
                t.Info("hover driver unavailable: " + e.Message);
                return false;
            }
        }

        /// <summary>Runs at the head of <c>RadialMenu.HoverTick</c>, i.e. inside Draw after the main-ring
        /// hover has been resolved from the pointer. While a shot poses a hover it replaces the resolved
        /// index, parks the pointer distance mid-ring (so the sticky action word and the readout treat the
        /// pointer as ON the ring, and no slide-out dwell starts) and clears the CLOSE-band hover. Field
        /// injection by name (Harmony <c>___field</c>): a rename fails the patch, never the build.</summary>
        private static void HoverTickPrefix(ref int ____hovered, ref float ____mainDist, ref bool ____closeHovered,
            float ____lastOuterR, float ____lastInnerR)
        {
            if (_hoverIndex == HoverOff) return;
            ____closeHovered = false;
            ____hovered = _hoverIndex;
            ____mainDist = (____lastInnerR + ____lastOuterR) * 0.5f;
        }

        // ------------------------------------------------------------------ capture

        /// <summary>Wait for the end of the frame, then read the back buffer into a PNG. Reported
        /// CAPTURED (never a check - see the class remarks); only a capture that produced NO file is a
        /// failure (the test's deliverable is missing).</summary>
        private static IEnumerator Capture(string file, TestCase t)
        {
            yield return new WaitForEndOfFrame();
            Texture2D tex = null;
            RenderTexture prev = RenderTexture.active;
            try
            {
                Directory.CreateDirectory(_shotDir);
                string path = Path.Combine(_shotDir, file);
                RenderTexture.active = null;   // the back buffer: the finished frame, overlays included
                int w = Screen.width, h = Screen.height;
                tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0f, 0f, w, h), 0, 0, false);
                tex.Apply(false);
                byte[] png = tex.EncodeToPNG();
                if (png == null || png.Length == 0) t.Fail("screenshot " + file + ": nothing was encoded - no file written");
                else
                {
                    File.WriteAllBytes(path, png);
                    _shotCount++;
                    t.Captured(path, w + "x" + h + ", " + png.Length + " bytes"
                        + (png.Length <= 1024 ? " - suspiciously small, probably a blank frame" : "") + " - judge by eye");
                }
            }
            catch (Exception e) { t.Fail("screenshot " + file + " could not be captured: " + e.Message); }
            finally
            {
                RenderTexture.active = prev;
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
        }

        // ------------------------------------------------------------------ shots

        private static IEnumerator ShotsThemes(TestCase t)
        {
            var ctl = RadialController.Active;
            if (ctl == null) { t.Skip("the radial controller is not running (radial half disabled?)"); yield break; }
            bool drive = HookHover(t);
            BeginProfileGuard();   // the player's own profile + global look come back after the suite

            // A recent item for The Hub: a bag in a hand holding an iron ingot (depth 1 - where the
            // recent-item wedge looks, BagRadialFeature.FindByPrefab), recorded as the last retrieval.
            yield return ClearTestItems();
            bool recent = false;
            if (HandsAreEmpty())
            {
                string bagName = FirstExistingPrefab(null, BagPrefabs);
                var bag = bagName != null ? SpawnIntoSlot(bagName, OtherHand(ActiveHand()), t) : null;
                if (bag != null)
                {
                    yield return Frames(3);
                    var free = FreeStorageSlots(bag);
                    var ingot = free.Count > 0 ? SpawnIntoSlot("ItemIronIngot", free[0], t) : null;
                    if (ingot != null)
                    {
                        SaveRecent();   // RestoreAll puts the player's own "recent item" back
                        RetrievalMemory.Record(ingot);
                        recent = true;
                    }
                }
            }
            if (!recent) t.Info("no recent-item fixture (hands not empty, or no bag prefab) - the Hub shots show no recent-item wedge");

            var feature = new ToolbeltRadialFeature();
            var names = ShippedNames();
            for (int i = 0; i < names.Length; i++)
            {
                string theme = names[i];
                string slug = Slug(theme);
                CloseRadialsQuietly();
                if (!ApplyShippedTheme(theme, t)) { t.Fail("could not show the theme '" + theme + "'"); continue; }
                yield return Seconds(0.5f);   // the HUD rebuilds its views and the theme restyles settle

                // 1) the belt wheel, tap-opened (sticky), nothing hovered
                ctl.SwitchToFeature(feature);
                _hoverIndex = -1;
                yield return Seconds(0.6f);   // open animation + the hint strip fading in
                if (!ctl._menu.IsOpen) { t.Fail(slug + ": the belt wheel did not open"); continue; }
                yield return Capture("theme-" + slug + "_wheel-open.png", t);

                // 2) a hovered BRANCH wedge (The Hub): the D-022 action word over the ring
                int hubIdx = IndexOfTag(ctl._menu, RadialMenu.HubTag);
                _hoverIndex = hubIdx >= 0 ? hubIdx : 0;
                yield return Seconds(0.45f);
                if (drive)
                    t.Check(RadialHintContext.Verb == "Open",
                        slug + ": the hovered branch (The Hub) publishes the action word 'Open' (" + (RadialHintContext.Verb ?? "none") + ")");
                yield return Capture("theme-" + slug + "_hover-branch.png", t);

                // 3) The Hub opened (what a click on its wedge runs: SelectSticky -> PushBranch), the
                //    recent-item wedge hovered so the hub shows its bold name line + "Recent item"
                var hub = ctl._menu.MainEntry(_hoverIndex);
                if (hub != null && hub.IsBranch) ctl._menu.PushBranch(hub);
                else t.Info(slug + ": no Hub wedge to open");
                int rec = IndexOfCornerTag(ctl._menu, "Recent item");
                _hoverIndex = rec >= 0 ? rec : 0;
                yield return Seconds(0.5f);
                if (recent) t.Check(rec >= 0, slug + ": The Hub shows the recent-item wedge (tagged 'Recent item')");
                yield return Capture("theme-" + slug + "_hub-recent.png", t);

                _hoverIndex = HoverOff;
                CloseRadialsQuietly();
                yield return Frames(3);
            }
            // Back to the player's own profile + look before the next shot builds a scratch copy of it.
            RestoreProfileNow();
            yield return Seconds(0.3f);
        }

        private static IEnumerator ShotsF10(TestCase t)
        {
            bool wasOpen = UI.Menu.UiaControlCenter.IsOpen;
            UI.Menu.UiaControlCenter.Open();
            if (!UI.Menu.UiaControlCenter.IsOpen) { t.Fail("F10 did not open"); yield break; }
            if (!_f10Touched)
            {
                _f10Touched = true;
                _f10WasOpen = wasOpen;
                _f10WasTab = CcActiveTab();
            }
            // REFLECTION (see F10ScrollCapture): UiaControlCenter.cs is mid-refactor in another session.
            // 0.9.8.0: the global Simple/Advanced density flag is RETIRED (per-section disclosures
            // replaced it), so the old SetAdvanced choreography is gone — the capture is simply
            // the HUD tab as it opens.
            int hud = CcTabIndex("HudTab");
            if (hud < 0) t.Info("the HUD tab was not found by type name - capturing whatever tab is active");
            else if (!CcInvoke("SelectTab", hud)) t.Info("could not select the HUD tab (UiaControlCenter.SelectTab)");
            yield return Seconds(0.7f);
            if (hud >= 0) t.Check(CcActiveTab() == hud, "the HUD tab is the active tab");
            yield return Capture("f10_hud-simple.png", t);
            RestoreF10();
            yield return Frames(2);
        }

        private static IEnumerator ShotsRangefinder(TestCase t)
        {
            if (!AddRangefinder(t)) yield break;   // scratch profile + the F9 add path
            var tier = HudSystem.LastSnapshot != null ? HudSystem.LastSnapshot.Tier : HudTier.Bare;
            if (tier == HudTier.Bare)
            {
                ForceTier(HudTier.Suited);
                t.Info("you are in the BARE tier - the shot forces the Suited tier (restored after)");
            }
            yield return Seconds(1.0f);
            var view = FindView(_rfElementId);
            t.Check(view != null && view.Root != null && view.Root.gameObject.activeInHierarchy,
                "the Rangefinder view is built and active (screen centre, over the crosshair)");
            yield return Capture("rangefinder_suited.png", t);
        }

        // ------------------------------------------------------------------ helpers

        private static int IndexOfTag(RadialMenu menu, object tag)
        {
            for (int i = 0; i < 64; i++)
            {
                var e = menu.MainEntry(i);
                if (e == null) break;
                if (ReferenceEquals(e.Tag, tag)) return i;
            }
            return -1;
        }

        private static int IndexOfCornerTag(RadialMenu menu, string cornerTag)
        {
            for (int i = 0; i < 64; i++)
            {
                var e = menu.MainEntry(i);
                if (e == null) break;
                if (e.CornerTag == cornerTag) return i;
            }
            return -1;
        }

        /// <summary>"Stationeers Blue Minimalist" -> "blue-minimalist" (ASCII file-name slug).</summary>
        private static string Slug(string name)
        {
            var sb = new StringBuilder();
            bool dash = false;
            string s = (name ?? "").ToLowerInvariant();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) { sb.Append(c); dash = false; }
                else if (!dash && sb.Length > 0) { sb.Append('-'); dash = true; }
            }
            string slug = sb.ToString().TrimEnd('-');
            if (slug.StartsWith("stationeers-", StringComparison.Ordinal)) slug = slug.Substring("stationeers-".Length);
            return slug.Length > 0 ? slug : "theme";
        }

        // --- UiaControlCenter by reflection (fail-soft: -1 / false on any mismatch) ---

        private static int CcActiveTab()
        {
            try
            {
                var f = typeof(UI.Menu.UiaControlCenter).GetField("_active", AnyStatic);
                return f != null ? (int)f.GetValue(null) : -1;
            }
            catch { return -1; }
        }

        private static int CcTabIndex(string typeName)
        {
            try
            {
                var f = typeof(UI.Menu.UiaControlCenter).GetField("_tabs", AnyStatic);
                var list = f != null ? f.GetValue(null) as IList : null;
                if (list == null) return -1;
                for (int i = 0; i < list.Count; i++)
                    if (list[i] != null && list[i].GetType().Name == typeName) return i;
            }
            catch { }
            return -1;
        }

        private static bool CcInvoke(string method, object arg)
        {
            try
            {
                // Resolve by the argument's type: the tutorial added SelectTab(string) beside
                // SelectTab(int), so a name-only lookup is ambiguous (AmbiguousMatchException).
                var t = typeof(UI.Menu.UiaControlCenter);
                var m = arg != null ? t.GetMethod(method, AnyStatic, null, new[] { arg.GetType() }, null) : null;
                if (m == null) m = t.GetMethod(method, AnyStatic);
                if (m == null) return false;
                m.Invoke(null, new[] { arg });
                return true;
            }
            catch (Exception e)
            {
                LogLine("   F10 reflection '" + method + "' failed: " + e.Message);
                return false;
            }
        }

        /// <summary>Put F10 back the way the shot found it: density, tab, open/closed.</summary>
        private static void RestoreF10()
        {
            if (!_f10Touched) return;
            _f10Touched = false;
            try
            {
                if (UI.Menu.UiaControlCenter.IsOpen)
                {
                    // 0.9.8.0: no density flag to restore any more (retired with the title-bar buttons).
                    if (_f10WasTab >= 0 && CcActiveTab() != _f10WasTab) CcInvoke("SelectTab", _f10WasTab);
                    if (!_f10WasOpen) UI.Menu.UiaControlCenter.Close();
                }
            }
            catch (Exception e) { LogLine("   F10 restore failed: " + e.Message); }
            _f10WasTab = -1;
        }
    }
}
