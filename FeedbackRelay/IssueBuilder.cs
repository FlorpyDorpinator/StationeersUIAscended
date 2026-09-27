using System.Globalization;
using System.Text;

namespace FeedbackRelay;

/// <summary>
/// Builds the GitHub issue (title, body, labels) for a validated report - the format in
/// Feedback-Pipeline-Plan.md §2.5. Pure: no network, no clock of its own, no state - which is
/// what lets DRY_RUN return the exact issue that would be filed ("preview before faith",
/// the sister app's build_issue()).
/// </summary>
public static class IssueBuilder
{
    /// <summary>First line of every issue the relay files: the "filed by the pipeline" join
    /// key for the triage bot's dedupe and for GET /v1/status. User text can never contain
    /// "&lt;!--" (see <see cref="Sanitizer.Neutralize"/>), so this can't be forged.</summary>
    public const string MarkerPrefix = "<!-- uia-feedback:v1:id=";

    public const string TruncatedNote = "<sub>(truncated)</sub>";

    private const string Absent = "—"; // em-dash

    /// <summary>Headroom when shrinking an attachment, so one re-render usually suffices.</summary>
    private const int ShrinkSlack = 128;

    public static IssuePayload Build(ValidatedReport r, Guid reportId, DateTimeOffset nowUtc)
    {
        var kindLabel = r.Kind == FeedbackKind.Bug ? "Bug" : "Suggestion";
        var title = $"[{kindLabel}] {r.Title}";
        var labels = new[]
        {
            "feedback",
            r.Kind == FeedbackKind.Bug ? "bug" : "enhancement",
            "triage:pending",
        };

        var profile = r.ProfileXml;
        var profileTruncated = r.ProfileXmlTruncated;
        var log = r.LogExcerpt;
        var logTruncated = r.LogExcerptTruncated;

        var body = Render(r, kindLabel, reportId, nowUtc, profile, profileTruncated, log, logTruncated);

        // Truncation order (plan §2.4): the log excerpt first, then the profile XML, and the
        // description never. The log keeps its TAIL (most recent lines), the profile its HEAD.
        for (var pass = 0; pass < 8 && body.Length > RelayLimits.IssueBodyMax; pass++)
        {
            var over = body.Length - RelayLimits.IssueBodyMax + ShrinkSlack;
            if (!string.IsNullOrEmpty(log))
            {
                log = Sanitizer.KeepTail(log, log.Length - over);
                logTruncated = true;
            }
            else if (!string.IsNullOrEmpty(profile))
            {
                profile = Sanitizer.KeepHead(profile, profile.Length - over);
                profileTruncated = true;
            }
            else
            {
                break;
            }
            body = Render(r, kindLabel, reportId, nowUtc, profile, profileTruncated, log, logTruncated);
        }

        if (body.Length > RelayLimits.IssueBodyMax)
        {
            // Unreachable with the intake caps (description 4,000 + capped context fields is
            // ~20K chars worst case). Kept so no input can ever produce a body GitHub rejects.
            body = Sanitizer.KeepHead(body, RelayLimits.IssueBodyMax - 64) + "\n\n" + TruncatedNote;
        }

        return new IssuePayload(title, body, labels);
    }

    private static string Render(
        ValidatedReport r, string kindLabel, Guid reportId, DateTimeOffset nowUtc,
        string? profile, bool profileTruncated, string? log, bool logTruncated)
    {
        var sb = new StringBuilder(8192);
        void Line(string s = "") => sb.Append(s).Append('\n');
        void Row(string label, string value) => Line($"| {label} | {value} |");

        Line(MarkerPrefix + reportId.ToString("D") + " -->");
        Line($"**{kindLabel} reported from in game** - {When(nowUtc)}");
        Line();
        Line("### What they said");
        foreach (var descLine in r.Description.Split('\n'))
            Line("> " + descLine);
        Line();
        Line("### Details");
        Line("| | |");
        Line("|---|---|");
        Row("Mod version", ModVersionCell(r));
        Row("Game build", Plain(r.GameBuild));
        Row("HUD profile", ProfileCell(r));
        Row("Context", Plain(r.PlayerContext));
        Row("Display", Plain(r.Display));
        Row("OS", Plain(r.Os));
        Row("Contact", r.Contact == null ? "(none given)" : Sanitizer.Fenced(r.Contact));
        Line();

        if (profile != null)
            Attachment(sb, "Profile XML (player opted in)", "xml", profile, profileTruncated);
        if (log != null)
            Attachment(sb, "Mod log excerpt (player opted in)", "text", log, logTruncated);

        Line("<sub>Filed automatically from in-game feedback. The reporter is anonymous " +
             "unless a contact is listed.</sub>");
        return sb.ToString();
    }

    private static void Attachment(StringBuilder sb, string summary, string lang, string content, bool truncated)
    {
        sb.Append("<details><summary>").Append(summary).Append("</summary>\n\n");
        content = content.TrimEnd('\n');
        if (content.Length == 0)
        {
            sb.Append("_Omitted: the issue reached GitHub's size limit._\n");
        }
        else
        {
            var fence = Sanitizer.FenceFor(content);
            sb.Append(fence).Append(lang).Append('\n')
              .Append(content).Append('\n')
              .Append(fence).Append('\n');
        }
        if (truncated) sb.Append(TruncatedNote).Append('\n');
        sb.Append("\n</details>\n\n");
    }

    /// <summary>"25 Sep 2026, 9:14 PM UTC".</summary>
    private static string When(DateTimeOffset nowUtc)
    {
        var t = nowUtc.ToUniversalTime();
        var inv = CultureInfo.InvariantCulture;
        return $"{t.Day.ToString(inv)} {t.ToString("MMM", inv)} {t.Year.ToString(inv)}, " +
               $"{t.ToString("h:mm tt", inv)} UTC";
    }

    private static string Plain(string? value)
    {
        if (value == null) return Absent;
        var cell = Sanitizer.Cell(value);
        return cell.Length == 0 ? Absent : cell;
    }

    private static string ModVersionCell(ValidatedReport r)
    {
        if (r.ModVersion == null && r.DeployRoute == null) return Absent;
        var route = r.DeployRoute == null ? "" : $" ({Plain(r.DeployRoute)})";
        return Plain(r.ModVersion) + route;
    }

    private static string ProfileCell(ValidatedReport r)
    {
        var flag = r.ProfileModified switch
        {
            true => " (modified from shipped)",
            false => " (not modified from shipped)",
            null => "",
        };
        var name = r.ProfileName == null ? Absent : Sanitizer.Fenced(r.ProfileName);
        return name + flag;
    }
}
