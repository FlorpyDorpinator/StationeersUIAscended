using System;
using System.Collections.Generic;

namespace StationeersUIMod.UI.Menu.Tutorial
{
    /// <summary>
    /// Copy checks for the lesson script: the <c>uiatutorial lint</c> console command and the
    /// editor's per-field status line (Documentation/0.9.8.0/Tutorial-Build-Contract.md s6).
    ///
    /// <para>A field is checked for: characters TMP cannot draw (anything outside printable ASCII -
    /// the game's font renders Basic Latin only, CLAUDE.md "TMP glyphs = ASCII only"); unbalanced or
    /// unknown <c>{tokens}</c>; a key written as literal <c>[Key]</c> text instead of a token (it
    /// would lie after a rebind); empty text; and the RESOLVED length (live glyphs, what the player
    /// reads) against the field's budget.</para>
    ///
    /// <para><see cref="RunAll"/> also cross-checks the script against the shipped copy: every key
    /// the chapters declare needs a default in <see cref="TutorialCopy"/>, and every shipped pair
    /// needs a declared key (an orphan means a typo, or a step that was renamed). The only static is
    /// <see cref="LastChecked"/> (a plain int); <see cref="Shutdown"/> resets it (called from
    /// <c>TutorialDirector.Shutdown</c>).</para>
    /// </summary>
    internal static class TutorialLint
    {
        /// <summary>How many strings the last <see cref="RunAll"/> checked (for the console summary).</summary>
        internal static int LastChecked { get; private set; }

        /// <summary>Hot-reload rule: every static resets.</summary>
        internal static void Shutdown() { LastChecked = 0; }

        /// <summary>Null when the text is fine, else ONE short ASCII line naming the first problem.
        /// <paramref name="budget"/> &lt;= 0 skips the length check. Allocates (resolution) - call on
        /// change, not per frame.</summary>
        internal static string CheckField(string key, string text, int budget)
        {
            if (text == null) text = "";
            if (text.Length == 0) return "empty - the shipped copy will show instead";

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\n' || c == '\t') continue;
                if (c < 0x20) return "control character at " + (i + 1) + " (not drawable)";
                if (c > 0x7E) return "non-ASCII character at " + (i + 1) + " (draws as a box in game)";
            }

            // Tokens: balanced, known. {OPENER} is the reopen chrome line's placeholder; {N}, {TOTAL}
            // and {TITLE} are the first-run tour header's (tour mode) - the director swaps both kinds in
            // before TutorialTokens.Resolve, so anywhere else they would show as a bogus [key].
            int pos = 0;
            while (pos < text.Length)
            {
                int open = text.IndexOf('{', pos);
                int stray = text.IndexOf('}', pos);
                if (open < 0)
                {
                    if (stray >= 0) return "stray '}' at " + (stray + 1);
                    break;
                }
                if (stray >= 0 && stray < open) return "stray '}' at " + (stray + 1);
                int close = text.IndexOf('}', open + 1);
                if (close < 0) return "unclosed '{' at " + (open + 1);
                int nested = text.IndexOf('{', open + 1);
                if (nested >= 0 && nested < close) return "'{' inside a token at " + (nested + 1);
                string token = text.Substring(open + 1, close - open - 1);
                if (token == "OPENER")
                {
                    if (!string.Equals(key, ReopenKey, StringComparison.Ordinal))
                        return "{OPENER} only works in " + ReopenKey;
                }
                else if (IsTourHeaderToken(token))
                {
                    if (!string.Equals(key, TourHeaderKey, StringComparison.Ordinal))
                        return "{" + token + "} only works in " + TourHeaderKey;
                }
                else if (!TutorialTokens.IsKnown(token))
                    return "unknown token {" + token + "}";
                pos = close + 1;
            }

            // A bracketed key written as text: "[G]" / "[MMB]" - the stored form must be a token.
            int lb = text.IndexOf('[');
            if (lb >= 0)
            {
                int rb = text.IndexOf(']', lb + 1);
                if (rb > lb && rb - lb <= 12) return "'[" + text.Substring(lb + 1, rb - lb - 1) + "]' is text - use a {token} so rebinds show";
            }

            if (budget > 0)
            {
                // Measured as shown at its longest (ShownForBudget - the lesson editor's counter and
                // "on screen" line measure the very same string).
                int len = TutorialTokens.Resolve(ShownForBudget(key, text)).Length;
                if (len > budget) return "over budget: " + len + "/" + budget + " characters";
            }
            return null;
        }

        /// <summary>The text as the budget measures it: its longest on-screen form, still UNRESOLVED
        /// (pass the result to <see cref="TutorialTokens.Resolve"/>). The director swaps a few
        /// placeholders in before it resolves the tokens, so they are stood in for here the same way,
        /// at their longest: in chrome|reopen, <c>{OPENER}</c> becomes the belt-wheel key (the
        /// director's fallback opener, and the longer of the two the steps name - MMB vs R by
        /// default); in chrome|tourheader, <c>{N}</c> and <c>{TOTAL}</c> become the tour's length
        /// (<c>TutorialChapters.Tour.Length</c> - the largest lesson number) and <c>{TITLE}</c> a full
        /// 28-character lesson title. Any other key comes back unchanged: the director swaps nothing
        /// there, so a stray placeholder shows as a bogus [key] - which is what the player would read
        /// (<see cref="CheckField"/> flags it). THE one place for this: <see cref="CheckField"/> and
        /// the lesson editor's character counter and preview line all call it, so they always agree.
        /// Allocates - call on change, not per frame.</summary>
        internal static string ShownForBudget(string key, string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            if (string.Equals(key, ReopenKey, StringComparison.Ordinal))
                return text.Replace("{OPENER}", "{UIA_ToolbeltRadial}");
            if (string.Equals(key, TourHeaderKey, StringComparison.Ordinal))
            {
                string total = TutorialChapters.Tour.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
                // {TITLE} last, the director's order (TutorialDirector's tour-header builder).
                return text.Replace("{N}", total).Replace("{TOTAL}", total).Replace("{TITLE}", TitleSample);
            }
            return text;
        }

        /// <summary>One ASCII line for the editor's counter tooltip: what <see cref="ShownForBudget"/>
        /// stood in for (shown only on a field where it changed something).</summary>
        internal const string StandInsNote =
            "Placeholders count at their longest: {OPENER} as a key, {N} and {TOTAL} as the tour's length, {TITLE} as a 28-character title.";

        /// <summary>The one key <c>{OPENER}</c> may appear in.</summary>
        private const string ReopenKey = "chrome|reopen";

        /// <summary>The one key the first-run tour's placeholders may appear in.</summary>
        private const string TourHeaderKey = "chrome|tourheader";

        /// <summary>A lesson title at its 28-character budget (the {TITLE} stand-in for the length check).</summary>
        private const string TitleSample = "XXXXXXXXXXXXXXXXXXXXXXXXXXXX";

        private static bool IsTourHeaderToken(string token)
        {
            return token == "N" || token == "TOTAL" || token == "TITLE";
        }

        /// <summary>Check every string the lessons can show (current text = override or shipped) plus
        /// the script/copy cross-check. One line per problem, "key: problem". Sets
        /// <see cref="LastChecked"/>.</summary>
        internal static List<string> RunAll()
        {
            var problems = new List<string>();
            List<string> keys;
            try { keys = TutorialChapters.AllKeys(); }
            catch (Exception e)
            {
                problems.Add("script: could not list the lesson keys (" + e.Message + ")");
                LastChecked = 0;
                return problems;
            }

            var declared = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                if (!declared.Add(key)) { problems.Add(key + ": declared twice in TutorialChapters"); continue; }
                if (!TutorialTextStore.HasDefault(key)) problems.Add(key + ": no shipped copy in TutorialCopy.g.cs");
                string text = TutorialTextStore.Get(key);
                string p = CheckField(key, text, TutorialChapters.BudgetOf(key));
                if (p != null)
                    problems.Add(key + (TutorialTextStore.IsOverridden(key) ? " (edited)" : "") + ": " + p);
            }

            // Shipped pairs: even count, no duplicates, no orphans.
            try
            {
                var pairs = TutorialCopy.Pairs;
                if (pairs == null) problems.Add("TutorialCopy.Pairs is missing");
                else
                {
                    if ((pairs.Length & 1) != 0) problems.Add("TutorialCopy.Pairs has an odd count (" + pairs.Length + ")");
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    for (int i = 0; i + 1 < pairs.Length; i += 2)
                    {
                        string k = pairs[i];
                        if (string.IsNullOrEmpty(k)) { problems.Add("TutorialCopy.Pairs: empty key at pair " + (i / 2 + 1)); continue; }
                        if (!seen.Add(k)) problems.Add(k + ": shipped twice in TutorialCopy.g.cs");
                        if (!declared.Contains(k)) problems.Add(k + ": shipped copy with no key in TutorialChapters (typo or retired step?)");
                    }
                }
            }
            catch (Exception e) { problems.Add("TutorialCopy.Pairs could not be read (" + e.Message + ")"); }

            // Overrides on keys the script does not know (renamed steps, the retired 15-step ids).
            try
            {
                foreach (var k in TutorialTextStore.OverriddenKeys)
                    if (!declared.Contains(k)) problems.Add(k + ": saved override for a key no lesson uses (harmless; 'uiatutorial reset' clears it)");
            }
            catch { }

            LastChecked = keys.Count;
            return problems;
        }
    }
}
