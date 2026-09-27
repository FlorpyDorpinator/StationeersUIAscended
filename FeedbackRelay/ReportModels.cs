using System.Text.Json;
using System.Text.Json.Serialization;

namespace FeedbackRelay;

// --- Wire shapes ------------------------------------------------------------------------
//
// Request JSON is camelCase (System.Text.Json web defaults, case-insensitive on read).
// Unknown fields are ignored; a wrong type (e.g. profileModified: "yes") is a 400.

public sealed class ReportRequest
{
    public string? Kind { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Contact { get; set; }
    public ReportContext? Context { get; set; }
    public string? ProfileXml { get; set; }
    public string? LogExcerpt { get; set; }
}

public sealed class ReportContext
{
    public string? ModVersion { get; set; }
    public string? DeployRoute { get; set; }
    public string? GameBuild { get; set; }
    public string? ProfileName { get; set; }
    public bool? ProfileModified { get; set; }
    public string? PlayerContext { get; set; }
    public string? Display { get; set; }
    public string? Os { get; set; }
}

public enum FeedbackKind
{
    Bug,
    Suggestion,
}

// --- Response shapes --------------------------------------------------------------------

public sealed record ErrorResponse(
    string Error,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Duplicate = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? GithubStatus = null);

public sealed record FiledResponse(int Number, string Url);

public sealed record DryRunResponse(int Number, bool DryRun, IssuePayload Issue);

public sealed record IssuePayload(string Title, string Body, string[] Labels);

public sealed record StatusResponse(string State, string[] Labels);

public sealed record PingResponse(bool Ok, bool DryRun, int ReportsPerHour);

public static class RelayJson
{
    /// <summary>camelCase out, case-insensitive in.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

// --- Limits (relay-side enforcement; the mod's own caps are a courtesy) -----------------

public static class RelayLimits
{
    public const int TitleMax = 120;
    public const int DescriptionMax = 4000;
    public const int ContactMax = 60;

    /// <summary>Per context field (mod version, OS string, ...). Not in the plan's list; a
    /// guard so a hostile client can't use context fields to blow the issue-body cap.</summary>
    public const int ContextFieldMax = 200;

    /// <summary>100 KB, measured in characters (profiles are ASCII XML in practice).</summary>
    public const int ProfileXmlMax = 100 * 1024;

    /// <summary>30 KB, measured in characters.</summary>
    public const int LogExcerptMax = 30 * 1024;

    /// <summary>GitHub's issue body cap is 65,536; we stay well under it.</summary>
    public const int IssueBodyMax = 60_000;

    /// <summary>Kestrel request-body ceiling. Comfortably above the largest legitimate report
    /// (~140K chars, even fully \u-escaped), far below Kestrel's 30 MB default.</summary>
    public const long MaxRequestBodyBytes = 1024 * 1024;
}

/// <summary>A report that passed validation: every string trimmed, capped and defanged.</summary>
public sealed class ValidatedReport
{
    public required FeedbackKind Kind { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public string? Contact { get; init; }

    public string? ModVersion { get; init; }
    public string? DeployRoute { get; init; }
    public string? GameBuild { get; init; }
    public string? ProfileName { get; init; }
    public bool? ProfileModified { get; init; }
    public string? PlayerContext { get; init; }
    public string? Display { get; init; }
    public string? Os { get; init; }

    public string? ProfileXml { get; init; }
    public bool ProfileXmlTruncated { get; init; }
    public string? LogExcerpt { get; init; }
    public bool LogExcerptTruncated { get; init; }

    /// <summary>
    /// Validates and normalizes a request. Returns null and sets <paramref name="error"/> when
    /// the request can't be filed.
    ///
    /// The user-typed fields (title, description, contact) are REJECTED when over their cap -
    /// the in-game form enforces the same caps, so only a broken or hostile client gets here.
    /// The attachments (profile XML, log excerpt) and machine-filled context fields are
    /// TRUNCATED instead: an over-long excerpt should never cost a player the whole report.
    /// </summary>
    public static ValidatedReport? From(ReportRequest? req, out string? error)
    {
        error = null;
        if (req == null)
        {
            error = "Request body must be a JSON object.";
            return null;
        }

        FeedbackKind kind;
        switch ((req.Kind ?? "").Trim().ToLowerInvariant())
        {
            case "bug": kind = FeedbackKind.Bug; break;
            case "suggestion": kind = FeedbackKind.Suggestion; break;
            default:
                error = "kind must be \"bug\" or \"suggestion\".";
                return null;
        }

        var title = Sanitizer.SingleLine(req.Title);
        if (title.Length == 0) { error = "title is required."; return null; }
        if (title.Length > RelayLimits.TitleMax)
        {
            error = $"title is longer than {RelayLimits.TitleMax} characters.";
            return null;
        }

        var description = Sanitizer.NormalizeNewlines(req.Description).Trim();
        if (description.Length == 0) { error = "description is required."; return null; }
        if (description.Length > RelayLimits.DescriptionMax)
        {
            error = $"description is longer than {RelayLimits.DescriptionMax} characters.";
            return null;
        }

        var contact = Sanitizer.SingleLine(req.Contact);
        if (contact.Length > RelayLimits.ContactMax)
        {
            error = $"contact is longer than {RelayLimits.ContactMax} characters.";
            return null;
        }

        var ctx = req.Context ?? new ReportContext();

        string? Ctx(string? value)
        {
            var v = Sanitizer.SingleLine(value);
            if (v.Length > RelayLimits.ContextFieldMax) v = v.Substring(0, RelayLimits.ContextFieldMax);
            return v.Length == 0 ? null : Sanitizer.Neutralize(v);
        }

        string? profile = Sanitizer.NormalizeNewlines(req.ProfileXml);
        var profileTruncated = false;
        if (profile.Trim().Length == 0) profile = null;
        else if (profile.Length > RelayLimits.ProfileXmlMax)
        {
            profile = Sanitizer.KeepHead(profile, RelayLimits.ProfileXmlMax);
            profileTruncated = true;
        }

        string? log = Sanitizer.NormalizeNewlines(req.LogExcerpt);
        var logTruncated = false;
        if (log.Trim().Length == 0) log = null;
        else if (log.Length > RelayLimits.LogExcerptMax)
        {
            // The most recent lines are where the error is: keep the tail.
            log = Sanitizer.KeepTail(log, RelayLimits.LogExcerptMax);
            logTruncated = true;
        }

        return new ValidatedReport
        {
            Kind = kind,
            Title = Sanitizer.Neutralize(title),
            Description = Sanitizer.Neutralize(description),
            Contact = contact.Length == 0 ? null : Sanitizer.Neutralize(contact),
            ModVersion = Ctx(ctx.ModVersion),
            DeployRoute = Ctx(ctx.DeployRoute),
            GameBuild = Ctx(ctx.GameBuild),
            ProfileName = Ctx(ctx.ProfileName),
            ProfileModified = ctx.ProfileModified,
            PlayerContext = Ctx(ctx.PlayerContext),
            Display = Ctx(ctx.Display),
            Os = Ctx(ctx.Os),
            ProfileXml = profile == null ? null : Sanitizer.Neutralize(profile),
            ProfileXmlTruncated = profileTruncated,
            LogExcerpt = log == null ? null : Sanitizer.Neutralize(log),
            LogExcerptTruncated = logTruncated,
        };
    }
}
