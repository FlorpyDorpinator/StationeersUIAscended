using System.Globalization;
using System.Text.RegularExpressions;

namespace FeedbackRelay;

/// <summary>
/// Everything the relay reads from its environment, read once at startup. Changing a value
/// means restarting the process (or the Windows service).
///
/// The token is held here and handed only to <see cref="GitHubIssues"/>. It is never logged,
/// never echoed in a response, and <see cref="Describe"/> reports only whether it is set.
/// </summary>
public sealed class RelayConfig
{
    public const int DefaultPort = 8080;

    /// <summary>Reports per hour per client address (FlorpyDorp raised it from 3 to 6,
    /// 2026-09-26). Overridable with UIA_RELAY_REPORTS_PER_HOUR; every report also starts a
    /// Claude triage run, so a high value lets one spammer burn Actions minutes.</summary>
    public const int DefaultReportsPerHour = 6;
    public const int MaxReportsPerHour = 100;

    public string? Token { get; private init; }
    public string? Repo { get; private init; }
    public int Port { get; private init; } = DefaultPort;
    public int ReportsPerHour { get; private init; } = DefaultReportsPerHour;
    public string? ClientKey { get; private init; }
    public bool DryRun { get; private init; }

    /// <summary>Problems that should stop startup (e.g. an unparseable port).</summary>
    public List<string> FatalErrors { get; } = new();

    /// <summary>Problems worth a startup warning that still let the relay run.</summary>
    public List<string> Warnings { get; } = new();

    /// <summary>Both halves or nothing: a token with no repo can't file anywhere.</summary>
    public bool GitHubConfigured => !string.IsNullOrEmpty(Token) && !string.IsNullOrEmpty(Repo);

    public bool ClientKeyRequired => !string.IsNullOrEmpty(ClientKey);

    private static readonly Regex RepoPattern =
        new(@"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9._-]{1,100}$", RegexOptions.CultureInvariant);

    public static RelayConfig FromEnvironment()
    {
        string? Env(string name)
        {
            var v = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        }

        var token = Env("UIA_FEEDBACK_GITHUB_TOKEN");
        var repo = Env("UIA_FEEDBACK_REPO");
        var portText = Env("UIA_RELAY_PORT");
        var clientKey = Env("UIA_RELAY_CLIENT_KEY");
        var dryRunText = Env("UIA_RELAY_DRY_RUN");
        var perHourText = Env("UIA_RELAY_REPORTS_PER_HOUR");

        var fatal = new List<string>();
        var warnings = new List<string>();

        // A bad value falls back to the default rather than stopping the relay: a typo in a
        // tuning knob must never take report intake offline.
        var perHour = DefaultReportsPerHour;
        if (perHourText != null)
        {
            if (!int.TryParse(perHourText, NumberStyles.None, CultureInfo.InvariantCulture, out perHour)
                || perHour < 1 || perHour > MaxReportsPerHour)
            {
                warnings.Add($"UIA_RELAY_REPORTS_PER_HOUR must be a number from 1 to {MaxReportsPerHour} - using {DefaultReportsPerHour}.");
                perHour = DefaultReportsPerHour;
            }
        }

        var port = DefaultPort;
        if (portText != null)
        {
            if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port)
                || port < 1 || port > 65535)
            {
                fatal.Add("UIA_RELAY_PORT must be a number from 1 to 65535.");
                port = DefaultPort;
            }
        }

        if (repo != null && !RepoPattern.IsMatch(repo))
        {
            warnings.Add("UIA_FEEDBACK_REPO is not in \"owner/name\" form - treating GitHub as not configured.");
            repo = null;
        }

        var dryRun = dryRunText != null &&
                     (dryRunText == "1" ||
                      dryRunText.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                      dryRunText.Equals("yes", StringComparison.OrdinalIgnoreCase));

        if (!dryRun && (token == null || repo == null))
        {
            warnings.Add("GitHub is not configured (UIA_FEEDBACK_GITHUB_TOKEN and UIA_FEEDBACK_REPO) " +
                         "and DRY_RUN is off - POST /v1/report will answer 503.");
        }

        var config = new RelayConfig
        {
            Token = token,
            Repo = repo,
            Port = port,
            ClientKey = clientKey,
            DryRun = dryRun,
            ReportsPerHour = perHour,
        };
        config.FatalErrors.AddRange(fatal);
        config.Warnings.AddRange(warnings);
        return config;
    }

    /// <summary>A one-line startup summary. Never contains the token or the client key.</summary>
    public string Describe() =>
        $"port={Port} dryRun={(DryRun ? "on" : "off")} reportsPerHour={ReportsPerHour} repo={Repo ?? "(not set)"} " +
        $"token={(string.IsNullOrEmpty(Token) ? "(not set)" : "set")} " +
        $"clientKey={(ClientKeyRequired ? "required" : "not required")}";
}
