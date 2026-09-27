using System.Text;

namespace FeedbackRelay;

/// <summary>
/// Pure text helpers. Invariant 2 of the plan (§5): user-typed text is DATA, never markup.
/// Ported from the sister app's api/feedback/github.py (_neutralize, _fenced).
/// </summary>
public static class Sanitizer
{
    /// <summary>U+2011 NON-BREAKING HYPHEN - looks like '-' to a human, isn't one to a parser.</summary>
    private const char NbHyphen = '‑';

    /// <summary>
    /// Defang HTML comments. Without this, anyone could forge a
    /// <c>&lt;!-- uia-feedback:... --&gt;</c> or <c>&lt;!-- uia-triage-plan:... --&gt;</c> marker
    /// and break the triage bot's dedupe. Same replacements, in the same order, as the sister app's
    /// _neutralize: "&lt;!--" becomes "&lt;!" + U+2011 + "-", "--&gt;" becomes "-" + U+2011 + "&gt;".
    /// Length-preserving (4 chars in, 4 chars out; 3 in, 3 out).
    /// </summary>
    public static string Neutralize(string? text) =>
        (text ?? "")
            .Replace("<!--", "<!" + NbHyphen + "-", StringComparison.Ordinal)
            .Replace("-->", "-" + NbHyphen + ">", StringComparison.Ordinal);

    /// <summary>CRLF and lone CR become LF.</summary>
    public static string NormalizeNewlines(string? text) =>
        (text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    /// <summary>One line: every control character (newlines, tabs, ...) becomes a space; trimmed.</summary>
    public static string SingleLine(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
            sb.Append(char.IsControl(c) ? ' ' : c);
        return sb.ToString().Trim();
    }

    /// <summary>
    /// A value for a markdown table cell: a pipe would end the cell and a newline would end
    /// the row, so both are removed (the sister app strips rather than escapes). Neutralized AFTER
    /// stripping, because removing a character can join "&lt;!-|-" into "&lt;!--".
    /// </summary>
    public static string Cell(string value) =>
        Neutralize(SingleLine(value).Replace("|", "", StringComparison.Ordinal).Trim());

    /// <summary>
    /// A user-chosen name (profile name, contact) inside a code span, table-safe. Backtick,
    /// pipe and newline are removed first: a backtick would close the span and let the name
    /// smuggle rendered markdown into the issue; GitHub renders nothing inside a code span -
    /// no links, no @mentions, no images. Neutralized after stripping, like <see cref="Cell"/>.
    /// </summary>
    public static string Fenced(string name)
    {
        var cleaned = Neutralize(SingleLine(name)
            .Replace("|", "", StringComparison.Ordinal)
            .Replace("`", "", StringComparison.Ordinal)
            .Trim());
        return cleaned.Length == 0 ? "—" : "`" + cleaned + "`";
    }

    /// <summary>
    /// A code fence that the content can't close: CommonMark closes a backtick fence only with
    /// a run at least as long as the opener, so use one longer than any run in the content.
    /// </summary>
    public static string FenceFor(string content)
    {
        int longest = 0, run = 0;
        foreach (var c in content)
        {
            run = c == '`' ? run + 1 : 0;
            if (run > longest) longest = run;
        }
        return new string('`', Math.Max(3, longest + 1));
    }

    /// <summary>The first <paramref name="max"/> chars, ending on a whole line when that costs
    /// little, never splitting a surrogate pair.</summary>
    public static string KeepHead(string text, int max)
    {
        if (max <= 0) return "";
        if (text.Length <= max) return text;
        var cut = max;
        if (char.IsHighSurrogate(text[cut - 1])) cut--;
        var head = text.Substring(0, cut);
        var lastNl = head.LastIndexOf('\n');
        if (lastNl >= 0 && head.Length - lastNl <= 400) head = head.Substring(0, lastNl);
        return head;
    }

    /// <summary>The last <paramref name="max"/> chars, starting on a whole line when that costs
    /// little, never splitting a surrogate pair.</summary>
    public static string KeepTail(string text, int max)
    {
        if (max <= 0) return "";
        if (text.Length <= max) return text;
        var start = text.Length - max;
        if (char.IsLowSurrogate(text[start])) start++;
        var tail = text.Substring(start);
        var firstNl = tail.IndexOf('\n');
        if (firstNl >= 0 && firstNl < 400) tail = tail.Substring(firstNl + 1);
        return tail;
    }
}
