using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FeedbackRelay;

/// <summary>
/// The only code that talks to GitHub. Two calls: create an issue, read one issue's state.
///
/// Conventions carried over from the sister app's github.py: a module-level timeout (8 s - a player
/// is waiting on the Send button), ONE attempt with no retry or backoff (the game-side outbox
/// is the retry loop), and a status-code-plus-hint error that is actionable ("GitHub said 401
/// - the token was rejected") rather than an exception name. Nothing in here throws for a
/// GitHub or network failure; every outcome comes back as a value.
///
/// The token is attached per request and never logged. GitHub's own error text is kept only
/// for the relay's log line (trimmed), never sent back to the caller.
/// </summary>
public sealed class GitHubIssues
{
    public const string ApiVersion = "2022-11-28";
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
    private const int MaxErrorDetailChars = 160;

    /// <summary>What each status code means for whoever reads the error. The code itself is
    /// always in the message - it's the thing worth searching for.</summary>
    private static readonly Dictionary<int, string> StatusHints = new()
    {
        [401] = "the token was rejected and may need rotating",
        [403] = "access was refused - the token may lack Issues write, or GitHub is rate-limiting the relay",
        [404] = "the repository wasn't found - check UIA_FEEDBACK_REPO and the token's repository access",
        [422] = "the issue was rejected - a label may not exist in the repo yet",
        [429] = "GitHub is rate-limiting the relay",
    };

    private readonly RelayConfig _config;
    private readonly HttpClient _http;

    public GitHubIssues(RelayConfig config)
    {
        _config = config;
        _http = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        })
        {
            BaseAddress = new Uri("https://api.github.com/"),
            Timeout = RequestTimeout,
            MaxResponseContentBufferSize = 1024 * 1024,
        };
        // GitHub rejects requests without a User-Agent.
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("UIA-FeedbackRelay", "1.0"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", ApiVersion);
    }

    // --- Outcomes ----------------------------------------------------------------------

    /// <param name="Error">Safe to return to the caller: status code + hint, no GitHub text.</param>
    /// <param name="LogDetail">GitHub's own words, trimmed - for the relay's log line only.</param>
    public sealed record CreateOutcome(
        bool Ok, int Number, string Url, int? GitHubStatus, string? Error, string? LogDetail);

    public enum StatusKind { Found, NotFound, Failed }

    public sealed record StatusOutcome(
        StatusKind Kind, StatusResponse? Status, int? GitHubStatus, string? Error, string? LogDetail);

    // --- POST /repos/{repo}/issues ------------------------------------------------------

    public async Task<CreateOutcome> CreateIssueAsync(IssuePayload issue)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"repos/{_config.Repo}/issues")
        {
            Content = JsonContent.Create(issue, options: RelayJson.Options),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.Token);

        HttpResponseMessage response;
        try
        {
            // Deliberately NOT linked to the caller's RequestAborted: if the game gives up
            // waiting, finishing the call (and keeping the dedupe reservation on success) is
            // what stops the outbox retry from filing the same report twice.
            response = await _http.SendAsync(request);
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            return new CreateOutcome(false, 0, "", null, DescribeTransport(ex), ex.GetType().Name);
        }

        using (response)
        {
            var code = (int)response.StatusCode;
            var text = await ReadTextAsync(response);
            if (code < 200 || code >= 300)
            {
                var hint = StatusHints.GetValueOrDefault(code, "the issue wasn't filed");
                return new CreateOutcome(false, 0, "", code, $"GitHub said {code} - {hint}.", Trimmed(text));
            }

            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (root.TryGetProperty("number", out var n) && n.TryGetInt32(out var number) && number > 0)
                {
                    var url = root.TryGetProperty("html_url", out var u) && u.ValueKind == JsonValueKind.String
                        ? u.GetString() ?? ""
                        : "";
                    if (url.Length > 200) url = url.Substring(0, 200);
                    return new CreateOutcome(true, number, url, code, null, null);
                }
                return new CreateOutcome(false, 0, "", code, $"GitHub said {code} but named no issue number.", null);
            }
            catch (JsonException)
            {
                return new CreateOutcome(false, 0, "", code, $"GitHub said {code} but sent no readable JSON.", null);
            }
        }
    }

    // --- GET /repos/{repo}/issues/{number} ----------------------------------------------

    /// <summary>
    /// State + label names only. An issue counts as "found" only if it is a real issue (not a
    /// pull request) that the relay filed - its body starts with the uia-feedback marker - so
    /// the endpoint can't be used to read the state or labels of anything else in the repo.
    /// </summary>
    public async Task<StatusOutcome> GetStatusAsync(int number)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"repos/{_config.Repo}/issues/{number}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.Token);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request);
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            return new StatusOutcome(StatusKind.Failed, null, null, DescribeTransport(ex), ex.GetType().Name);
        }

        using (response)
        {
            var code = (int)response.StatusCode;
            // 404 unknown, 410 deleted, 301 transferred to another repo: all "not ours / not here".
            if (code == 404 || code == 410 || code == 301)
                return new StatusOutcome(StatusKind.NotFound, null, code, null, null);

            var text = await ReadTextAsync(response);
            if (code < 200 || code >= 300)
            {
                var hint = StatusHints.GetValueOrDefault(code, "the lookup failed");
                return new StatusOutcome(StatusKind.Failed, null, code, $"GitHub said {code} - {hint}.", Trimmed(text));
            }

            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;

                if (root.TryGetProperty("pull_request", out var pr) && pr.ValueKind != JsonValueKind.Null)
                    return new StatusOutcome(StatusKind.NotFound, null, code, null, null);

                var body = root.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String
                    ? b.GetString() ?? ""
                    : "";
                if (!body.TrimStart().StartsWith(IssueBuilder.MarkerPrefix, StringComparison.Ordinal))
                    return new StatusOutcome(StatusKind.NotFound, null, code, null, null);

                var state = root.TryGetProperty("state", out var s) && s.ValueKind == JsonValueKind.String
                    ? s.GetString() ?? "unknown"
                    : "unknown";

                var labels = new List<string>();
                if (root.TryGetProperty("labels", out var ls) && ls.ValueKind == JsonValueKind.Array)
                {
                    foreach (var label in ls.EnumerateArray())
                    {
                        if (label.ValueKind == JsonValueKind.String)
                            labels.Add(label.GetString() ?? "");
                        else if (label.ValueKind == JsonValueKind.Object &&
                                 label.TryGetProperty("name", out var name) &&
                                 name.ValueKind == JsonValueKind.String)
                            labels.Add(name.GetString() ?? "");
                    }
                }

                return new StatusOutcome(StatusKind.Found, new StatusResponse(state, labels.ToArray()), code, null, null);
            }
            catch (JsonException)
            {
                return new StatusOutcome(StatusKind.Failed, null, code, $"GitHub said {code} but sent no readable JSON.", null);
            }
        }
    }

    // --- Helpers -------------------------------------------------------------------------

    private static bool IsTransportFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or OperationCanceledException;

    /// <summary>Timeout, DNS, TLS, connection reset. The category is the useful part; the
    /// exception text can carry the URL and adds nothing but noise.</summary>
    private static string DescribeTransport(Exception ex) => ex switch
    {
        TaskCanceledException or OperationCanceledException =>
            $"GitHub did not answer within {RequestTimeout.TotalSeconds:0} s.",
        HttpRequestException hre => $"Couldn't reach GitHub ({hre.HttpRequestError}).",
        _ => $"Couldn't reach GitHub ({ex.GetType().Name}).",
    };

    private static async Task<string> ReadTextAsync(HttpResponseMessage response)
    {
        try { return await response.Content.ReadAsStringAsync(); }
        catch { return ""; }
    }

    /// <summary>GitHub's response squeezed onto one line and cut short: a 40 KB HTML error page
    /// from a proxy must not flood the log.</summary>
    private static string Trimmed(string text)
    {
        var detail = string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return detail.Length > MaxErrorDetailChars ? detail.Substring(0, MaxErrorDetailChars).TrimEnd() + "..." : detail;
    }
}
