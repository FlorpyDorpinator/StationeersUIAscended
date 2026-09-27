using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using StationeersUIMod.Core;

namespace StationeersUIMod.UI.Menu.Kit
{
    /// <summary>
    /// Kit-internal persisted preferences for the F10 Control Center: the last-open tab, the
    /// per-section "More options" disclosure states, and whatever small kit prefs come later.
    /// These are MACHINE state, not settings a player tunes — they live in their own
    /// "0. Internal" section so the real settings sections stay readable.
    ///
    /// <para>Bound LAZILY against whatever ConfigFile <c>UIAConfig</c> was bound to (the
    /// <see cref="Features.StowRenameConfig"/> idiom: any already-bound entry knows its file, and
    /// <c>ConfigFile.Bind</c> is idempotent), so this class never touches <c>UIAConfig.cs</c> and
    /// never cares whether it runs before or after the main Bind. Everything is fail-soft: with no
    /// config available every getter returns its default and every setter is a no-op — the menu
    /// simply forgets across launches, which is the correct degraded behaviour.</para>
    ///
    /// <para>Hot reload: <see cref="Reset"/> (called from <c>UiaControlCenter.Shutdown</c>) drops
    /// the entry references so a reloaded assembly re-binds against the live ConfigFile.</para>
    /// </summary>
    public static class UiaMenuPrefs
    {
        private const string Section = "0. Internal";

        private static ConfigEntry<string> _lastTab;
        private static ConfigEntry<string> _openSections;

        // The parsed disclosure set, kept in lock-step with _openSections' comma-joined value.
        private static readonly HashSet<string> _open = new HashSet<string>();
        private static bool _openParsed;

        private static bool EnsureBound()
        {
            if (_lastTab != null && _openSections != null) return true;
            try
            {
                var host = UIAConfig.MasterEnable;      // any already-bound entry knows its file
                if (host == null) return false;
                ConfigFile cfg = host.ConfigFile;
                if (cfg == null) return false;
                if (_lastTab == null)
                    _lastTab = cfg.Bind(Section, "LastTab", "",
                        "Managed by the mod: the F10 tab that was open last, restored on the next " +
                        "open. Clearing it just means F10 opens on the first tab again.");
                if (_openSections == null)
                    _openSections = cfg.Bind(Section, "OpenSections", "",
                        "Managed by the mod: which 'More options' section disclosures are open, " +
                        "as a comma-separated id list. Safe to clear.");
                return _lastTab != null && _openSections != null;
            }
            catch (System.Exception e)
            {
                UIALog.Warn("UiaMenuPrefs bind failed: " + e.Message);
                return false;
            }
        }

        /// <summary>The title of the tab F10 last had open ("" = never saved). Set on every tab
        /// switch; read once when the window is (re)built.</summary>
        public static string LastTab
        {
            get
            {
                if (!EnsureBound()) return "";
                return _lastTab.Value ?? "";
            }
            set
            {
                if (!EnsureBound()) return;
                _lastTab.Value = value ?? "";
            }
        }

        private static void EnsureOpenParsed()
        {
            if (_openParsed) return;
            _openParsed = true;
            _open.Clear();
            if (!EnsureBound()) return;
            string raw = _openSections.Value;
            if (string.IsNullOrEmpty(raw)) return;
            string[] parts = raw.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i] != null ? parts[i].Trim() : null;
                if (!string.IsNullOrEmpty(p)) _open.Add(p);
            }
        }

        /// <summary>Is section <paramref name="sectionId"/>'s "More options" disclosure open?
        /// The stored set only holds OPEN sections, so an id that was never opened (or was closed
        /// again) reads false — sections default collapsed, per the disclosure model.</summary>
        public static bool GetDisclosure(string sectionId)
        {
            if (string.IsNullOrEmpty(sectionId)) return false;
            EnsureOpenParsed();
            return _open.Contains(sectionId);
        }

        /// <summary>Remember a section disclosure state. Ids are code-authored ASCII tokens
        /// (e.g. "hud.show"); commas are stripped so one id can never corrupt the joined list.</summary>
        public static void SetDisclosure(string sectionId, bool open)
        {
            if (string.IsNullOrEmpty(sectionId)) return;
            sectionId = sectionId.Replace(",", "");
            EnsureOpenParsed();
            bool changed = open ? _open.Add(sectionId) : _open.Remove(sectionId);
            if (!changed) return;
            if (!EnsureBound()) return;   // in-memory only this session — still correct behaviour
            var sb = new StringBuilder(64);
            foreach (string id in _open)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(id);
            }
            _openSections.Value = sb.ToString();
        }

        /// <summary>Teardown hook (hot reload / shutdown): drop the entry references and the
        /// parsed cache so a reloaded assembly re-binds and re-parses against the live file.</summary>
        public static void Reset()
        {
            _lastTab = null;
            _openSections = null;
            _open.Clear();
            _openParsed = false;
        }
    }
}
