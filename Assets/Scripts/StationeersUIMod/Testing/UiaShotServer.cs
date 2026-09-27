using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Assets.Scripts;                 // GameManager
using Assets.Scripts.GridSystem;      // GameState
using BepInEx;                        // Paths
using StationeersUIMod.Core;
using UnityEngine;

namespace StationeersUIMod.Testing
{
    /// <summary>
    /// FILE-TRIGGERED SCREENSHOT SERVER — the "let Claude see what it is testing" loop
    /// (FlorpyDorp's request, 2026-09-25). An outside process drops
    /// <c>BepInEx/uia-shot-request.txt</c>; this server picks it up, (optionally) loads a
    /// single-player save from the main menu, opens F10 on requested tabs, captures the back
    /// buffer to <c>BepInEx/uia-shots/*.png</c>, and writes <c>uia-shot-done.txt</c> when
    /// finished — so an orchestrating session can build, let ScriptEngine's file-watcher
    /// hot-reload, request shots, and READ the images, with nobody at the keyboard.
    ///
    /// <para><b>Player-inert by construction.</b> Without the request file this is one
    /// <c>File.Exists</c> per second. Serving a request mutates NO game state: it opens the F10
    /// menu (config-only UI) and, at most, loads a LOCAL single-player save the request names —
    /// the game's own <c>LoadHelper</c> extracts every load to a temp copy, and the orchestrator
    /// additionally requests a copied save, so a real save is never written. No gates beyond the
    /// file itself (a player machine never has the file); one log line per served request.</para>
    ///
    /// <para><b>Request format</b> (line-based, ASCII, '#' comments):
    /// <c>world=&lt;saveDirName|latest&gt;</c> (used only when not already in a world);
    /// <c>waitms=&lt;int&gt;</c> settle per shot (default 700);
    /// <c>shot=&lt;file.png&gt;:&lt;TabTitle|none&gt;[:&lt;Simple|Complex&gt;]</c> repeatable —
    /// TabTitle selects an F10 tab ("none" closes F10 for a bare-HUD shot; "same" captures the
    /// screen as-is, for motion bursts); the optional mode flips Smart Stow before the shot; an
    /// optional 4th field "dump" also writes a graphics report (<c>&lt;file&gt;.panels.txt</c>).</para>
    ///
    /// <para>Hot-reload: <see cref="Reset"/> (plugin OnDestroy) destroys the runner and drops
    /// every static. A request mid-reload is simply re-served by the fresh instance.</para>
    /// </summary>
    internal static class UiaShotServer
    {
        private const string RequestName = "uia-shot-request.txt";
        private const string WorkingName = "uia-shot-request.working";
        private const string DoneName = "uia-shot-done.txt";
        private const string ShotsDirName = "uia-shots";
        private const float PollSeconds = 1f;
        private const float WorldLoadTimeout = 300f;
        private const float WorldSettleSeconds = 6f;

        private static float _nextPoll;
        private static bool _busy;
        private static GameObject _runnerGo;
        private static ShotRunner _runner;

        private static string RequestPath { get { return Path.Combine(Paths.BepInExRootPath, RequestName); } }
        private static string WorkingPath { get { return Path.Combine(Paths.BepInExRootPath, WorkingName); } }
        private static string DonePath { get { return Path.Combine(Paths.BepInExRootPath, DoneName); } }
        private static string ShotsDir { get { return Path.Combine(Paths.BepInExRootPath, ShotsDirName); } }

        /// <summary>Per-frame pump (plugin Update). One time compare per frame; one
        /// File.Exists per second; everything else only while serving a request.</summary>
        public static void Tick()
        {
            if (_busy) return;
            float now = Time.unscaledTime;
            if (now < _nextPoll) return;
            _nextPoll = now + PollSeconds;
#if !DEBUG
            // Dev tooling only: a request can load saves, switch themes and run console
            // commands, so a Release (shipped) build never looks for the trigger file.
            return;
#else
            try
            {
                if (!File.Exists(RequestPath)) return;
                // Claim the request by MOVE, so a hot-reload mid-serve re-serves rather than
                // double-serves, and a malformed file cannot re-trigger every second.
                try
                {
                    if (File.Exists(WorkingPath)) File.Delete(WorkingPath);
                    File.Move(RequestPath, WorkingPath);
                }
                catch (Exception e) { UIALog.Warn("Shot server could not claim the request: " + e.Message); return; }
                var runner = EnsureRunner();
                if (runner == null) { WriteDone(new List<string> { "FAIL: no runner (plugin not loaded?)" }); return; }
                _busy = true;
                runner.StartCoroutine(Serve());
            }
            catch (Exception e)
            {
                UIALog.Warn("Shot server poll failed: " + e.Message);
            }
#endif
        }

        /// <summary>Hot-reload teardown (plugin OnDestroy): drop every static, destroy the runner.</summary>
        public static void Reset()
        {
            _busy = false;
            _nextPoll = 0f;
            _pointerOn = false;
            _radialOpenedHere = false;
            if (_pointerHarmony != null)
            {
                try { _pointerHarmony.UnpatchSelf(); } catch { }
                _pointerHarmony = null;
            }
            _runner = null;
            if (_runnerGo != null)
            {
                try { UnityEngine.Object.Destroy(_runnerGo); } catch { }
            }
            _runnerGo = null;
        }

        // ------------------------------------------------------------------ serving ----

        private sealed class ShotRunner : MonoBehaviour { }

        private static ShotRunner EnsureRunner()
        {
            if (_runner != null) return _runner;
            try
            {
                _runnerGo = new GameObject("UIAscended_ShotServer");
                UnityEngine.Object.DontDestroyOnLoad(_runnerGo);
                _runnerGo.hideFlags = HideFlags.HideAndDontSave;
                _runner = _runnerGo.AddComponent<ShotRunner>();
                return _runner;
            }
            catch (Exception e)
            {
                UIALog.Warn("Shot server runner failed: " + e.Message);
                return null;
            }
        }

        /// <summary>One request line in order: a shot, or a state change applied before the
        /// next shot (theme, radial, hover, burst, sub-tab, console command).</summary>
        private sealed class Step
        {
            public string Kind;        // "shot" | "theme" | "radial" | "hover" | "burst" | "sub" | "cmd"
            public ShotSpec Shot;
            public string Arg;         // the raw value for the non-shot kinds
        }

        // ---- the capture pointer (radial GIFs): an ImGui MousePos override applied by a
        // Harmony postfix on the game's own ImGui input step, so the radial's hover follows a
        // scripted path with nobody at the mouse. Patched only while a request needs it. ----
        private static HarmonyLib.Harmony _pointerHarmony;
        private static bool _pointerOn;
        private static Vector2 _pointer;   // ImGui space (pixels, y DOWN), like io.MousePos

        private static void SetPointer(Vector2 imguiPos)
        {
            EnsurePointerPatch();
            _pointer = imguiPos;
            _pointerOn = true;
        }

        private static void ClearPointer() { _pointerOn = false; }

        private static void EnsurePointerPatch()
        {
            if (_pointerHarmony != null) return;
            try
            {
                var h = new HarmonyLib.Harmony("com.stationeersuimod.shotserver.pointer");
                var target = HarmonyLib.AccessTools.Method(typeof(ImGuiNET.Unity.ImGuiPlatformInputManager),
                    "PrepareFrame");
                var post = new HarmonyLib.HarmonyMethod(typeof(UiaShotServer), nameof(PointerPostfix));
                h.Patch(target, postfix: post);
                _pointerHarmony = h;
            }
            catch (Exception e) { UIALog.Warn("Shot server pointer patch failed: " + e.Message); }
        }

        /// <summary>Runs right after the game writes io.MousePos from the real mouse and before
        /// ImGui.NewFrame, so this frame's ImGui (and the radial's hover test) sees the override.</summary>
        private static void PointerPostfix(ImGuiNET.ImGuiIOPtr io)
        {
            if (_pointerOn) io.MousePos = _pointer;
        }

        private sealed class ShotSpec
        {
            public string File;
            public string Tab;    // F10 tab title, or "none" for a bare-HUD shot
            public string Mode;   // "", "Simple", "Complex"
            public bool Dump;     // 4th field "dump": also write <file>.panels.txt
        }

        /// <summary>Read-only diagnostic: every active graphic under the F10 root with its
        /// screen rect, material, and the PanelGraphic edge-light state (SDF mode, Spec,
        /// BorderFade, EdgeRipple, border) — the ground truth behind a motion map.</summary>
        private static void DumpPanels(string file, List<string> report)
        {
            try
            {
                var root = GameObject.Find("UIAscended_ControlCenter");
                if (root == null) { report.Add("WARN dump: no F10 root"); return; }
                var sb = new System.Text.StringBuilder();
                var graphics = root.GetComponentsInChildren<UnityEngine.UI.Graphic>(false);
                var corners = new Vector3[4];
                for (int i = 0; i < graphics.Length; i++)
                {
                    var g = graphics[i];
                    if (g == null || !g.isActiveAndEnabled || g is TMPro.TMP_Text) continue;
                    g.rectTransform.GetWorldCorners(corners);
                    var mat = g.materialForRendering;
                    sb.Append(PathOf(g.transform, root.transform)).Append(" | ").Append(g.GetType().Name)
                      .Append(" | rect=").Append((int)corners[0].x).Append(',').Append((int)corners[0].y)
                      .Append(' ').Append((int)(corners[2].x - corners[0].x)).Append('x')
                      .Append((int)(corners[2].y - corners[0].y))
                      .Append(" | mat=").Append(mat != null && mat.shader != null ? mat.shader.name : "-")
                      .Append(" | col=").Append(ColorUtility.ToHtmlStringRGBA(g.color));
                    var p = g as UI.Hud.PanelGraphic;
                    if (p != null)
                        sb.Append(" | sdf=").Append(p.SdfMode ? 1 : 0)
                          .Append(" spec=").Append(p.Spec.ToString("0.00"))
                          .Append(" fade=").Append(p.BorderFade.ToString("0.00"))
                          .Append(" ripple=").Append(p.EdgeRipple.ToString("0.00"))
                          .Append(" bw=").Append(p.BorderWidth.ToString("0.0"))
                          .Append(" bcol=").Append(ColorUtility.ToHtmlStringRGBA(p.BorderColor))
                          .Append(" sides=").Append(p.BorderSides);
                    sb.Append('\n');
                }
                Directory.CreateDirectory(ShotsDir);
                string path = Path.Combine(ShotsDir, file);
                File.WriteAllText(path, sb.ToString());
                report.Add("OK dump " + graphics.Length + " graphics -> " + path);
            }
            catch (Exception e) { report.Add("FAIL dump: " + e.Message); }
        }

        // ------------------------------------------------------------ capture steps ----

        private static bool _radialOpenedHere;
        private const float BurstFps = 30f;

        private static string ActiveThemeName()
        {
            try { return UI.Hud.HudConfig.HudActiveProfile != null ? UI.Hud.HudConfig.HudActiveProfile.Value : null; }
            catch { return null; }
        }

        /// <summary>theme=&lt;name&gt; — the UI Themes card's own Apply recipe (ProfilesTab.Apply).
        /// The request's first theme step remembers the player's theme; the end restores it.</summary>
        private static void ApplyTheme(string name, List<string> report)
        {
            if (string.IsNullOrEmpty(name)) return;
            try
            {
                Windows.HudEditorWindow.FlushPendingElementEdit();
                if (UI.Hud.HudConfig.HudActiveProfile != null) UI.Hud.HudConfig.HudActiveProfile.Value = name;
                var keep = Features.HudProfileStore.Active;
                Features.HudProfileStore.LoadActive(name,
                    () => keep != null ? keep.Clone() : new UI.Hud.HudDocument { Name = name });
                UI.Hud.HudDocumentHistory.Clear();
                Windows.HudEditorMode.ClearElementSelection();
                if (UI.Menu.UiaControlCenter.IsOpen) UI.Menu.UiaControlCenter.Refresh();
                report.Add("OK theme: " + name);
            }
            catch (Exception e) { report.Add("FAIL theme '" + name + "': " + e.Message); }
        }

        /// <summary>radial=toolbelt | close — the real MMB toolbelt ring, opened sticky through
        /// RadialController.SwitchToFeature (the uiatest harness's entry point). F10 closes first:
        /// a radial cannot open over the menu.</summary>
        private static void RadialStep(string arg, List<string> report)
        {
            var ctl = Features.RadialController.Active;
            if (ctl == null) { report.Add("FAIL radial: no controller"); return; }
            try
            {
                if (string.Equals(arg, "close", StringComparison.OrdinalIgnoreCase))
                {
                    ctl.CloseAll("shot server");
                    _radialOpenedHere = false;
                    ClearPointer();
                    report.Add("OK radial closed");
                    return;
                }
                try { UI.Menu.UiaControlCenter.Close(); } catch { }
                ctl.SwitchToFeature(new Features.ToolbeltRadialFeature());
                _radialOpenedHere = ctl._menu.IsOpen;
                report.Add(_radialOpenedHere ? "OK radial open (toolbelt)" : "FAIL radial: ring did not open");
            }
            catch (Exception e) { report.Add("FAIL radial: " + e.Message); }
        }

        /// <summary>The ring's centre and a radius through the middle of the wedges, in ImGui
        /// space (the radial's own Draw: DrawUtil.ScreenCenter + config radii, hub floor 104).</summary>
        private static void RingGeometry(out Vector2 centre, out float midR)
        {
            centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float outerR = UIAConfig.RadialOuterRadius != null ? UIAConfig.RadialOuterRadius.Value : 240f;
            float innerR = UIAConfig.RadialInnerRadius != null ? UIAConfig.RadialInnerRadius.Value : 120f;
            innerR = Mathf.Clamp(innerR, 104f, Mathf.Max(104f, outerR - 30f));
            midR = (innerR + outerR) * 0.5f;
        }

        /// <summary>The pointer on wedge <paramref name="k"/> of <paramref name="count"/>: wedge i's
        /// mid angle is -PI/2 + i * 2PI/count (ImGui y-down; wedge 0 at 12 o'clock, clockwise).</summary>
        private static Vector2 WedgePoint(float k, int count)
        {
            Vector2 c; float r;
            RingGeometry(out c, out r);
            float a = -Mathf.PI * 0.5f + k * (Mathf.PI * 2f / Mathf.Max(1, count));
            return c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }

        /// <summary>hover=&lt;wedge&gt;/&lt;count&gt; | off — park the pointer on one wedge.</summary>
        private static void HoverStep(string arg, List<string> report)
        {
            if (string.IsNullOrEmpty(arg) || arg.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                ClearPointer();
                return;
            }
            string[] p = arg.Split('/');
            float k; int count;
            if (p.Length != 2 || !float.TryParse(p[0], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out k) || !int.TryParse(p[1], out count))
            {
                report.Add("FAIL hover: expected <wedge>/<count>, got '" + arg + "'");
                return;
            }
            SetPointer(WedgePoint(k, count));
            report.Add("OK hover " + arg);
        }

        /// <summary>sub=&lt;index&gt; — the SmartStow sub-tab for the CURRENT mode (Simple: 0 Return
        /// Home, 1 Universal Inventory; Complex: 0 Organizer, 1 Routing, 2 Universal Inventory).</summary>
        private static void SubTabStep(string arg, List<string> report)
        {
            int idx;
            if (!int.TryParse(arg, out idx)) { report.Add("FAIL sub: '" + arg + "'"); return; }
            try
            {
                var st = UI.Menu.Tabs.Storage.StowShared.State;
                Features.StowModeConfig.EnsureBound();
                bool simple = Features.StowModeConfig.Available
                    && Features.StowModeConfig.Mode == Features.StowMode.Simple;
                if (simple) st.SubTabSimple = idx; else st.SubTabComplex = idx;
                if (UI.Menu.UiaControlCenter.IsOpen) UI.Menu.UiaControlCenter.Refresh();
                report.Add("OK sub " + idx + (simple ? " (simple)" : " (complex)"));
            }
            catch (Exception e) { report.Add("FAIL sub: " + e.Message); }
        }

        /// <summary>burst=&lt;name&gt;:&lt;seconds&gt;:&lt;x&gt;,&lt;y&gt;,&lt;w&gt;,&lt;h&gt;[:sweep=&lt;wedges&gt;]
        /// — every rendered frame for the duration, cropped to a TOP-LEFT-origin screen rect,
        /// written as raw RGB24 (bottom-up rows) to uia-shots/&lt;name&gt;/NNNN.raw with a
        /// manifest of per-frame times (tools/uia-gif.py turns it into a GIF). sweep=N walks the
        /// capture pointer around an open radial: dwell on each of N wedges, then glide (N=0 =
        /// one continuous revolution). Raw files, not PNG: encoding per frame would stall.</summary>
        private static IEnumerator Burst(string arg, List<string> report)
        {
            string[] p = arg.Split(':');
            float seconds;
            string[] r = p.Length >= 3 ? p[2].Split(',') : null;
            int x, y, w, h;
            if (p.Length < 3 || r.Length != 4
                || !float.TryParse(p[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out seconds)
                || !int.TryParse(r[0], out x) || !int.TryParse(r[1], out y)
                || !int.TryParse(r[2], out w) || !int.TryParse(r[3], out h))
            {
                report.Add("FAIL burst: expected <name>:<seconds>:<x>,<y>,<w>,<h>[:sweep=N], got '" + arg + "'");
                yield break;
            }
            x = Mathf.Clamp(x, 0, Screen.width - 1);
            y = Mathf.Clamp(y, 0, Screen.height - 1);
            w = Mathf.Clamp(w, 1, Screen.width - x);
            h = Mathf.Clamp(h, 1, Screen.height - y);
            bool sweep = false;
            int wedges = 0;
            if (p.Length >= 4 && p[3].StartsWith("sweep", StringComparison.OrdinalIgnoreCase))
            {
                sweep = true;
                int eqi = p[3].IndexOf('=');
                if (eqi > 0) int.TryParse(p[3].Substring(eqi + 1), out wedges);
            }

            string name = Path.GetFileNameWithoutExtension(SafeFileName(p[0]));   // a folder, not a .png
            string dir = Path.Combine(ShotsDir, name);
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
            }
            catch (Exception e) { report.Add("FAIL burst dir: " + e.Message); yield break; }

            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            var manifest = new System.Text.StringBuilder();
            manifest.Append(w).Append(' ').Append(h).Append('\n');
            float t0 = Time.unscaledTime;
            float nextCapture = 0f;
            int frame = 0;
            while (true)
            {
                float t = Time.unscaledTime - t0;
                if (t > seconds) break;
                if (sweep) SetPointer(SweepPoint(t / Mathf.Max(0.01f, seconds), wedges));
                // Capture at most BurstFps; the pointer still moves every rendered frame.
                if (t < nextCapture) { yield return null; continue; }
                nextCapture = t + 1f / BurstFps;
                yield return new WaitForEndOfFrame();
                try
                {
                    tex.ReadPixels(new Rect(x, Screen.height - y - h, w, h), 0, 0, false);
                    File.WriteAllBytes(Path.Combine(dir, frame.ToString("D4") + ".raw"),
                        tex.GetRawTextureData());
                    manifest.Append(frame).Append(' ')
                        .Append((Time.unscaledTime - t0).ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture))
                        .Append('\n');
                    frame++;
                }
                catch (Exception e) { report.Add("FAIL burst frame " + frame + ": " + e.Message); break; }
            }
            UnityEngine.Object.Destroy(tex);
            if (sweep) ClearPointer();
            try { File.WriteAllText(Path.Combine(dir, "manifest.txt"), manifest.ToString()); } catch { }
            report.Add("OK burst " + name + ": " + frame + " frames, " + w + "x" + h);
        }

        /// <summary>Progress u in [0,1] around the ring: with N wedges, dwell 65% of each slot on the
        /// wedge centre, then an eased glide to the next; N=0 sweeps continuously.</summary>
        private static Vector2 SweepPoint(float u, int wedges)
        {
            u = Mathf.Clamp01(u);
            if (wedges <= 0) return WedgePoint(u * 12f, 12);
            float slot = u * wedges;
            int k = Mathf.Min(wedges - 1, Mathf.FloorToInt(slot));
            float local = slot - k;
            float g = local < 0.65f ? 0f : Mathf.SmoothStep(0f, 1f, (local - 0.65f) / 0.35f);
            return WedgePoint(k + g, wedges);
        }

        private static string PathOf(Transform t, Transform stop)
        {
            string s = t.name;
            for (var p = t.parent; p != null && p != stop; p = p.parent) s = p.name + "/" + s;
            return s;
        }

        private static IEnumerator Serve()
        {
            var report = new List<string>();
            var steps = new List<Step>();
            int shotCount = 0;
            string world = null;
            int waitMs = 700;
            string editProfile = null;
            int editGroup = 0;

            try
            {
                string[] lines = File.ReadAllLines(WorkingPath);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = (lines[i] ?? "").Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1).Trim();
                    if (key == "world") world = val;
                    else if (key == "waitms") int.TryParse(val, out waitMs);
                    else if (key == "edit")
                    {
                        // edit=<bag profile>[:<group 0-3>] — see the EDITING-band block below.
                        string[] ep = val.Split(':');
                        editProfile = ep[0].Trim();
                        if (ep.Length >= 2) int.TryParse(ep[1].Trim(), out editGroup);
                    }
                    else if (key == "shot")
                    {
                        string[] p = val.Split(':');
                        if (p.Length >= 2)
                        {
                            steps.Add(new Step
                            {
                                Kind = "shot",
                                Shot = new ShotSpec
                                {
                                    File = SafeFileName(p[0]),
                                    Tab = p[1].Trim(),
                                    Mode = p.Length >= 3 ? p[2].Trim() : "",
                                    Dump = p.Length >= 4 && p[3].Trim().Equals("dump", StringComparison.OrdinalIgnoreCase),
                                },
                            });
                            shotCount++;
                        }
                    }
                    else if (key == "theme" || key == "radial" || key == "hover" || key == "burst"
                             || key == "sub" || key == "cmd" || key == "grid")
                        steps.Add(new Step { Kind = key, Arg = val });
                }
            }
            catch (Exception e)
            {
                report.Add("FAIL: request unreadable: " + e.Message);
                Finish(report);
                yield break;
            }
            if (waitMs < 100) waitMs = 100;
            if (waitMs > 10000) waitMs = 10000;
            UIALog.Info("Shot server: serving " + shotCount + " shot(s), " + steps.Count + " step(s)"
                + (world != null ? " (world=" + world + ")" : "") + ".");

            // ---- ensure a world ----
            if (!WorldIsReady())
            {
                if (string.IsNullOrEmpty(world))
                {
                    report.Add("FAIL: no world is loaded and the request named none (add world=latest).");
                    Finish(report);
                    yield break;
                }
                string path, name, why;
                if (!ResolveSave(world, out path, out name, out why))
                {
                    report.Add("FAIL: " + why);
                    Finish(report);
                    yield break;
                }
                bool kicked = false;
                try
                {
                    // 27798 Assets.Scripts.Serialization/LoadHelper.cs:19: fire-and-forget async
                    // load; it extracts the save to a temp copy itself, so the source directory
                    // is never written. Verified public static void LoadGame(string, string).
                    Assets.Scripts.Serialization.LoadHelper.LoadGame(path, name);
                    kicked = true;
                    report.Add("OK: load kicked for '" + name + "'");
                }
                catch (Exception e) { report.Add("FAIL: LoadGame threw: " + e.Message); }
                if (!kicked) { Finish(report); yield break; }

                float deadline = Time.unscaledTime + WorldLoadTimeout;
                while (!WorldIsReady() && Time.unscaledTime < deadline)
                    yield return null;
                if (!WorldIsReady())
                {
                    report.Add("FAIL: world not ready after " + (int)WorldLoadTimeout + "s.");
                    Finish(report);
                    yield break;
                }
                yield return new WaitForSecondsRealtime(WorldSettleSeconds);
            }

            // ---- optional: open a bag profile's rule group in the Organizer's EDITING band
            // (UI state only; the player's own selection is restored after the shots) ----
            string prevEditProfile = null;
            int prevEditGroup = -1;
            bool editSet = false;
            if (!string.IsNullOrEmpty(editProfile))
            {
                try
                {
                    var st = UI.Menu.Tabs.Storage.StowShared.State;
                    prevEditProfile = st.SelectedProfile;
                    prevEditGroup = st.OpenRuleGroup;
                    st.SelectedProfile = editProfile;
                    st.OpenRuleGroup = editGroup;
                    editSet = true;
                }
                catch (Exception e) { report.Add("WARN edit: " + e.Message); }
            }

            // ---- run the steps, in request order ----
            string originalTheme = null;
            for (int i = 0; i < steps.Count; i++)
            {
                Step step = steps[i];
                if (step.Kind == "shot")
                {
                    ShotSpec s = step.Shot;
                    bool staged = false;
                    try
                    {
                        // "same" = capture the screen as-is (a motion burst shares one state).
                        staged = string.Equals(s.Tab, "same", StringComparison.OrdinalIgnoreCase)
                            || Stage(s, report);
                    }
                    catch (Exception e) { report.Add("FAIL " + s.File + ": staging threw: " + e.Message); }
                    if (!staged) continue;
                    yield return new WaitForSecondsRealtime(waitMs / 1000f);
                    yield return new WaitForEndOfFrame();
                    CaptureNow(s.File, report);
                    if (s.Dump) DumpPanels(s.File + ".panels.txt", report);
                }
                else if (step.Kind == "theme")
                {
                    if (originalTheme == null) originalTheme = ActiveThemeName();
                    ApplyTheme(step.Arg, report);
                    yield return new WaitForSecondsRealtime(waitMs / 1000f);
                }
                else if (step.Kind == "radial")
                {
                    RadialStep(step.Arg, report);
                    yield return new WaitForSecondsRealtime(waitMs / 1000f);
                }
                else if (step.Kind == "hover")
                {
                    HoverStep(step.Arg, report);
                    yield return new WaitForSecondsRealtime(waitMs / 1000f);
                }
                else if (step.Kind == "sub")
                {
                    SubTabStep(step.Arg, report);
                }
                else if (step.Kind == "grid")
                {
                    // grid=open | close — the Universal Inventory through its own Toggle (F10 first
                    // closes: the grid is a gameplay panel, not a menu overlay).
                    try
                    {
                        bool want = !string.Equals(step.Arg, "close", StringComparison.OrdinalIgnoreCase);
                        if (want) { try { UI.Menu.UiaControlCenter.Close(); } catch { } }
                        if (UI.Grid.TheGridPanel.IsOpen != want) UI.Grid.TheGridPanel.Toggle();
                        report.Add("OK grid " + (UI.Grid.TheGridPanel.IsOpen ? "open" : "closed"));
                    }
                    catch (Exception e) { report.Add("FAIL grid: " + e.Message); }
                    yield return new WaitForSecondsRealtime(waitMs / 1000f);
                }
                else if (step.Kind == "cmd")
                {
                    try { global::Util.Commands.CommandLine.Process(step.Arg); report.Add("OK cmd: " + step.Arg); }
                    catch (Exception e) { report.Add("FAIL cmd '" + step.Arg + "': " + e.Message); }
                }
                else if (step.Kind == "burst")
                {
                    var burst = Burst(step.Arg, report);
                    while (burst.MoveNext()) yield return burst.Current;
                }
            }

            ClearPointer();
            if (_radialOpenedHere)
            {
                try { if (Features.RadialController.Active != null) Features.RadialController.Active.CloseAll("shot server"); }
                catch { }
                _radialOpenedHere = false;
            }
            if (originalTheme != null) ApplyTheme(originalTheme, report);

            if (editSet)
            {
                try
                {
                    var st = UI.Menu.Tabs.Storage.StowShared.State;
                    st.SelectedProfile = prevEditProfile;
                    st.OpenRuleGroup = prevEditGroup;
                }
                catch { }
            }
            try { UI.Menu.UiaControlCenter.Close(); } catch { }
            Finish(report);
        }

        /// <summary>Put the screen into the requested state (synchronous part). True = capture.</summary>
        private static bool Stage(ShotSpec s, List<string> report)
        {
            if (string.Equals(s.Tab, "none", StringComparison.OrdinalIgnoreCase))
            {
                try { UI.Menu.UiaControlCenter.Close(); } catch { }
                return true;
            }
            if (!string.IsNullOrEmpty(s.Mode))
            {
                try
                {
                    Features.StowModeConfig.EnsureBound();
                    if (Features.StowModeConfig.Available)
                    {
                        Features.StowModeConfig.Mode = s.Mode.Equals("Simple", StringComparison.OrdinalIgnoreCase)
                            ? Features.StowMode.Simple : Features.StowMode.Complex;
                        try { UI.Grid.GridProfileMode.BumpVersion(); } catch { }
                    }
                }
                catch (Exception e) { report.Add("WARN " + s.File + ": mode set failed: " + e.Message); }
            }
            try
            {
                if (!UI.Menu.UiaControlCenter.IsOpen) UI.Menu.UiaControlCenter.Open();
                if (!UI.Menu.UiaControlCenter.IsOpen) { report.Add("FAIL " + s.File + ": F10 did not open."); return false; }
                UI.Menu.UiaControlCenter.SelectTabByTitle(s.Tab);   // void; an unknown title keeps the active tab
                return true;
            }
            catch (Exception e)
            {
                report.Add("FAIL " + s.File + ": " + e.Message);
                return false;
            }
        }

        /// <summary>Back-buffer capture (the UiaTestShots technique, verbatim): must run right
        /// after WaitForEndOfFrame on the same frame.</summary>
        private static void CaptureNow(string file, List<string> report)
        {
            Texture2D tex = null;
            RenderTexture prev = RenderTexture.active;
            try
            {
                Directory.CreateDirectory(ShotsDir);
                string path = Path.Combine(ShotsDir, file);
                RenderTexture.active = null;
                int w = Screen.width, h = Screen.height;
                tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0f, 0f, w, h), 0, 0, false);
                tex.Apply(false);
                byte[] png = tex.EncodeToPNG();
                if (png == null || png.Length == 0) report.Add("FAIL " + file + ": empty encode.");
                else
                {
                    File.WriteAllBytes(path, png);
                    report.Add("OK " + file + " " + w + "x" + h + " " + png.Length + "b -> " + path);
                }
            }
            catch (Exception e) { report.Add("FAIL " + file + ": " + e.Message); }
            finally
            {
                RenderTexture.active = prev;
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
        }

        private static void Finish(List<string> report)
        {
            _busy = false;
            try
            {
                report.Add("READY");
                File.WriteAllLines(DonePath, report.ToArray());
            }
            catch (Exception e) { UIALog.Warn("Shot server done-file write failed: " + e.Message); }
            try { if (File.Exists(WorkingPath)) File.Delete(WorkingPath); } catch { }
            UIALog.Info("Shot server: done (" + report.Count + " line(s)).");
        }

        private static void WriteDone(List<string> report)
        {
            Finish(report);
        }

        // ------------------------------------------------------------------ helpers ----

        private static bool WorldIsReady()
        {
            try { return GameManager.GameState == GameState.Running && Guards.LocalHuman != null; }
            catch { return false; }
        }

        /// <summary>Resolve a request's world name against the game's own saves folder
        /// (27798 StationSaveUtils.cs:21 — Documents/My Games/Stationeers/saves).</summary>
        private static bool ResolveSave(string world, out string path, out string name, out string why)
        {
            path = null; name = null; why = null;
            try
            {
                string savesRoot = Path.Combine(Path.Combine(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games"), "Stationeers"), "saves");
                if (!Directory.Exists(savesRoot)) { why = "saves folder not found: " + savesRoot; return false; }
                if (!string.Equals(world, "latest", StringComparison.OrdinalIgnoreCase))
                {
                    string dir = Path.Combine(savesRoot, world);
                    if (!Directory.Exists(dir)) { why = "save '" + world + "' not found."; return false; }
                    path = dir; name = world; return true;
                }
                string bestDir = null;
                DateTime bestTime = DateTime.MinValue;
                string[] dirs = Directory.GetDirectories(savesRoot);
                for (int i = 0; i < dirs.Length; i++)
                {
                    DateTime t = Directory.GetLastWriteTimeUtc(dirs[i]);
                    if (t > bestTime) { bestTime = t; bestDir = dirs[i]; }
                }
                if (bestDir == null) { why = "no saves found in " + savesRoot; return false; }
                path = bestDir;
                name = Path.GetFileName(bestDir);
                return true;
            }
            catch (Exception e) { why = "save resolution failed: " + e.Message; return false; }
        }

        private static string SafeFileName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "shot.png";
            char[] bad = Path.GetInvalidFileNameChars();
            string s = raw.Trim();
            for (int i = 0; i < bad.Length; i++) s = s.Replace(bad[i], '_');
            if (!s.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) s += ".png";
            return s;
        }
    }
}
