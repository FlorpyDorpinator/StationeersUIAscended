using System.Collections.Generic;

namespace StationeersUIMod.UI.Menu.Kit
{
    /// <summary>
    /// A registry of per-page state objects that survive BOTH rebuild paths the Control Center
    /// has — <c>Refresh</c> (a user gesture rebuilds the active tab) and <c>Restyle</c> (a live
    /// theme change rebuilds the whole window INCLUDING new tab instances). Tab instance fields
    /// survive only the first; statics survive both but each one is a hand-written hot-reload
    /// hazard (F10 plan §4 B1: StorageTab grew ~20 of them). This puts them all behind one
    /// registry with ONE reset in <c>UiaControlCenter.Shutdown</c>.
    ///
    /// <para>Usage: give each page a plain state class and fetch it by a stable key at the top of
    /// Build — <c>var st = UiaPageState.Get&lt;OrganizerState&gt;("storage.organizer");</c>. The
    /// same key always returns the same instance until <see cref="Reset"/>. State classes should
    /// hold plain data (selections, drafts, armed flags); anything holding Unity objects or
    /// <c>Thing</c> references must be clearable, because Reset only DROPS the dictionary — it
    /// cannot know how to unwind a live reference beyond letting it go.</para>
    /// </summary>
    public static class UiaPageState
    {
        private static readonly Dictionary<string, object> _states =
            new Dictionary<string, object>(8);

        /// <summary>The state object for <paramref name="pageKey"/>, created on first use. A key
        /// requested with a DIFFERENT type than it was created with gets a fresh instance of the
        /// requested type (programmer error, but fail-soft: never a cast throw).</summary>
        public static T Get<T>(string pageKey) where T : class, new()
        {
            if (string.IsNullOrEmpty(pageKey)) return new T();
            object o;
            if (_states.TryGetValue(pageKey, out o))
            {
                T typed = o as T;
                if (typed != null) return typed;
            }
            T fresh = new T();
            _states[pageKey] = fresh;
            return fresh;
        }

        /// <summary>Drop one page's state (e.g. an explicit "start over" gesture).</summary>
        public static void Clear(string pageKey)
        {
            if (!string.IsNullOrEmpty(pageKey)) _states.Remove(pageKey);
        }

        /// <summary>The single teardown, called from <c>UiaControlCenter.Shutdown</c> — every
        /// page state dies here, so no page ever needs its own static reset again.</summary>
        public static void Reset()
        {
            _states.Clear();
        }
    }
}
