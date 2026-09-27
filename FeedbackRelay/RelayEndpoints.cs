using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FeedbackRelay;

/// <summary>
/// The three endpoints (plan §2.3). Each handler writes its own response and records a short
/// "outcome" (and optional detail) in HttpContext.Items for the one-line request log in
/// Program.cs - which is the ONLY place anything about a request is logged. Report text,
/// profile XML, log excerpts, the token and raw IPs never reach a log line.
/// </summary>
public sealed class RelayEndpoints
{
    public const string OutcomeItem = "relay.outcome";
    public const string DetailItem = "relay.detail";
    public const string ClientKeyItem = "relay.client";
    public const string ClientHeader = "X-UIA-Client";

    public const int StatusLookupsPerHour = 120;

    private readonly RelayConfig _config;
    private readonly GitHubIssues _github;
    private readonly byte[]? _clientKeyHash;

    private readonly SlidingWindowLimiter _reportLimiter;
    private readonly SlidingWindowLimiter _statusLimiter = new(StatusLookupsPerHour, TimeSpan.FromHours(1));
    private readonly DedupeWindow _dedupe = new(TimeSpan.FromHours(1));
    private readonly StatusCache _statusCache = new(TimeSpan.FromSeconds(60));

    public RelayEndpoints(RelayConfig config, GitHubIssues github)
    {
        _config = config;
        _github = github;
        _reportLimiter = new SlidingWindowLimiter(config.ReportsPerHour, TimeSpan.FromHours(1));
        _clientKeyHash = config.ClientKeyRequired
            ? SHA256.HashData(Encoding.UTF8.GetBytes(config.ClientKey!))
            : null;
    }

    // --- GET /v1/ping --------------------------------------------------------------------

    public Task Ping(HttpContext ctx)
    {
        Outcome(ctx, "ok");
        return Write(ctx, StatusCodes.Status200OK, new PingResponse(true, _config.DryRun, _config.ReportsPerHour));
    }

    // --- POST /v1/report -----------------------------------------------------------------

    public async Task Report(HttpContext ctx)
    {
        // 1. The static client header - obscurity, not security (plan §2.4): it filters
        //    drive-by scanners, not a determined abuser. Refused requests don't cost quota.
        if (_clientKeyHash != null && !ClientHeaderMatches(ctx))
        {
            Outcome(ctx, "forbidden");
            await Write(ctx, StatusCodes.Status403Forbidden, new ErrorResponse("Missing or wrong X-UIA-Client header."));
            return;
        }

        // 2. Our misconfiguration is not the player's problem: answer before charging quota.
        if (!_config.DryRun && !_config.GitHubConfigured)
        {
            Outcome(ctx, "not-configured");
            await Write(ctx, StatusCodes.Status503ServiceUnavailable,
                new ErrorResponse("The relay is not configured to file issues yet."));
            return;
        }

        // 3. 3 POSTs per hour per client, counted before parsing so a flood is cheap to refuse.
        if (!_reportLimiter.TryAcquire(ClientKey(ctx), out var retryAfter))
        {
            Outcome(ctx, "rate-limited");
            SetRetryAfter(ctx, retryAfter);
            await Write(ctx, StatusCodes.Status429TooManyRequests,
                new ErrorResponse($"Too many reports from this address - the limit is {_config.ReportsPerHour} per hour."));
            return;
        }

        // 4. Parse.
        ReportRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<ReportRequest>(
                ctx.Request.Body, RelayJson.Options, ctx.RequestAborted);
        }
        catch (JsonException)
        {
            Outcome(ctx, "bad-json");
            await Write(ctx, StatusCodes.Status400BadRequest, new ErrorResponse("Request body is not valid report JSON."));
            return;
        }
        catch (BadHttpRequestException ex)
        {
            // Kestrel's body-size ceiling (413) or a malformed body.
            Outcome(ctx, "bad-body");
            await Write(ctx, ex.StatusCode, new ErrorResponse(ex.StatusCode == StatusCodes.Status413PayloadTooLarge
                ? "Request body is too large."
                : "Request body could not be read."));
            return;
        }

        // 5. Validate, cap, defang.
        var report = ValidatedReport.From(request, out var error);
        if (report == null)
        {
            Outcome(ctx, "invalid");
            await Write(ctx, StatusCodes.Status400BadRequest, new ErrorResponse(error ?? "Invalid report."));
            return;
        }

        // 6. Same title + description in the last hour -> refused (any sender).
        var dedupeKey = DedupeKey(report);
        if (!_dedupe.TryReserve(dedupeKey))
        {
            Outcome(ctx, "duplicate");
            await Write(ctx, StatusCodes.Status429TooManyRequests,
                new ErrorResponse("An identical report was already received in the last hour.", Duplicate: true));
            return;
        }

        try
        {
            // 7. Build. The marker id is relay-generated: the relay is stateless and GitHub
            //    assigns the issue number only after the body exists.
            var issue = IssueBuilder.Build(report, Guid.NewGuid(), DateTimeOffset.UtcNow);

            if (_config.DryRun)
            {
                Outcome(ctx, "dry-run");
                await Write(ctx, StatusCodes.Status201Created, new DryRunResponse(0, true, issue));
                return;
            }

            // 8. File it. One attempt; the game's outbox is the retry loop.
            var created = await _github.CreateIssueAsync(issue);
            if (!created.Ok)
            {
                _dedupe.Release(dedupeKey); // never landed - let the outbox retry through
                Outcome(ctx, created.GitHubStatus is int code ? $"github-{code}" : "github-unreachable");
                Detail(ctx, created.LogDetail);
                await Write(ctx, StatusCodes.Status502BadGateway,
                    new ErrorResponse(created.Error ?? "GitHub call failed.", GithubStatus: created.GitHubStatus));
                return;
            }

            Outcome(ctx, $"filed#{created.Number.ToString(CultureInfo.InvariantCulture)}");
            await Write(ctx, StatusCodes.Status201Created, new FiledResponse(created.Number, created.Url));
        }
        catch
        {
            _dedupe.Release(dedupeKey);
            throw;
        }
    }

    // --- GET /v1/status/{number} ---------------------------------------------------------

    public async Task Status(HttpContext ctx)
    {
        var raw = ctx.Request.RouteValues["number"] as string;
        if (!TryParseIssueNumber(raw, out var number))
        {
            Outcome(ctx, "invalid-number");
            await Write(ctx, StatusCodes.Status404NotFound, new ErrorResponse("Unknown report number."));
            return;
        }

        if (_config.DryRun)
        {
            Outcome(ctx, "dry-run");
            await Write(ctx, StatusCodes.Status503ServiceUnavailable,
                new ErrorResponse("Dry-run mode: status lookups are disabled."));
            return;
        }
        if (!_config.GitHubConfigured)
        {
            Outcome(ctx, "not-configured");
            await Write(ctx, StatusCodes.Status503ServiceUnavailable,
                new ErrorResponse("The relay is not configured to read issues yet."));
            return;
        }

        // Not in the plan's list: a per-client ceiling so a number-enumerating scanner can't
        // spend the PAT's GitHub API budget (which issue filing shares).
        if (!_statusLimiter.TryAcquire(ClientKey(ctx), out var retryAfter))
        {
            Outcome(ctx, "rate-limited");
            SetRetryAfter(ctx, retryAfter);
            await Write(ctx, StatusCodes.Status429TooManyRequests, new ErrorResponse("Too many status lookups from this address."));
            return;
        }

        if (_statusCache.TryGet(number, out var cached))
        {
            Outcome(ctx, cached.Found ? "cached" : "cached-unknown");
            await WriteStatus(ctx, cached);
            return;
        }

        var result = await _github.GetStatusAsync(number);
        switch (result.Kind)
        {
            case GitHubIssues.StatusKind.Found:
                var found = new StatusCache.Entry(true, result.Status);
                _statusCache.Set(number, found);
                Outcome(ctx, "found");
                await WriteStatus(ctx, found);
                return;

            case GitHubIssues.StatusKind.NotFound:
                var missing = new StatusCache.Entry(false, null);
                _statusCache.Set(number, missing);
                Outcome(ctx, "unknown");
                await WriteStatus(ctx, missing);
                return;

            default:
                // Transient: not cached.
                Outcome(ctx, result.GitHubStatus is int code ? $"github-{code}" : "github-unreachable");
                Detail(ctx, result.LogDetail);
                await Write(ctx, StatusCodes.Status502BadGateway,
                    new ErrorResponse(result.Error ?? "GitHub call failed.", GithubStatus: result.GitHubStatus));
                return;
        }
    }

    private static Task WriteStatus(HttpContext ctx, StatusCache.Entry entry) =>
        entry.Found && entry.Status != null
            ? Write(ctx, StatusCodes.Status200OK, entry.Status)
            : Write(ctx, StatusCodes.Status404NotFound, new ErrorResponse("Unknown report number."));

    // --- Helpers -------------------------------------------------------------------------

    private static Task Write<T>(HttpContext ctx, int status, T body)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.Headers.CacheControl = "no-store";
        return ctx.Response.WriteAsJsonAsync(body, RelayJson.Options, ctx.RequestAborted);
    }

    private static void Outcome(HttpContext ctx, string outcome) => ctx.Items[OutcomeItem] = outcome;

    private static void Detail(HttpContext ctx, string? detail)
    {
        if (!string.IsNullOrEmpty(detail)) ctx.Items[DetailItem] = detail;
    }

    private static string ClientKey(HttpContext ctx) =>
        ctx.Items.TryGetValue(ClientKeyItem, out var k) && k is string s ? s : "unknown";

    private static void SetRetryAfter(HttpContext ctx, TimeSpan retryAfter) =>
        ctx.Response.Headers.RetryAfter =
            ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

    /// <summary>Constant-time: both sides are hashed to a fixed length first, so neither the
    /// content nor the length of the expected value leaks through timing.</summary>
    private bool ClientHeaderMatches(HttpContext ctx)
    {
        var presented = ctx.Request.Headers[ClientHeader].ToString();
        var presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        return CryptographicOperations.FixedTimeEquals(presentedHash, _clientKeyHash!);
    }

    private static string DedupeKey(ValidatedReport report)
    {
        var bytes = Encoding.UTF8.GetBytes(report.Title + "\u0000" + report.Description);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    /// <summary>Digits only (no sign, no whitespace), 1..int.MaxValue.</summary>
    private static bool TryParseIssueNumber(string? raw, out int number)
    {
        number = 0;
        return raw != null && raw.Length is > 0 and <= 10 &&
               int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out number) &&
               number > 0;
    }
}
