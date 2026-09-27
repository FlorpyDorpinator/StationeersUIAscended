// UIA feedback relay - Documentation/0.9.8.0/Feedback-Pipeline-Plan.md §2.
//
// Accepts in-game bug reports / suggestions from Stationeers UI Ascended, sanitizes and
// rate-limits them, and files them as GitHub issues with a token that never leaves this
// machine. Listens on loopback only; the public face is a Cloudflare Tunnel (README.md).
//
// Invariants (plan §5) this process is responsible for:
//   - user-typed text is data, never markup (Sanitizer.Neutralize on every user string);
//   - the token lives in this process's environment only - never logged, never returned;
//   - logs go to the console only and carry method/path/outcome/ip-hash, never report text;
//   - the process reads and writes no files outside its own directory.

using System.Diagnostics;
using System.Net;
using System.Text;
using FeedbackRelay;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

var config = RelayConfig.FromEnvironment();

var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
{
    Args = args,
    // Its own directory, whether started by `dotnet run`, by hand, or by the service manager
    // (whose working directory is System32).
    ContentRootPath = AppContext.BaseDirectory,
});

// Lets the same exe run as a Windows service; a no-op when started from a console.
if (OperatingSystem.IsWindows())
    builder.Services.AddWindowsService(o => o.ServiceName = "UIAFeedbackRelay");

// Console only (AddWindowsService registers an Event Log provider when running as a service;
// ClearProviders removes it, by design - see README "Logs").
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.IncludeScopes = false;
    o.UseUtcTimestamp = true;
    o.TimestampFormat = "yyyy-MM-dd HH:mm:ss'Z' ";
});
builder.Logging.SetMinimumLevel(LogLevel.Information);
// Framework chatter off: exactly one log line per request, written by the middleware below.
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

builder.WebHost.ConfigureKestrel(kestrel =>
{
    // 127.0.0.1 ONLY - never a LAN or WAN interface. cloudflared reaches it over loopback.
    kestrel.Listen(IPAddress.Loopback, config.Port);
    kestrel.Limits.MaxRequestBodySize = RelayLimits.MaxRequestBodyBytes;
    kestrel.AddServerHeader = false;
});

var app = builder.Build();
var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("relay");

if (config.FatalErrors.Count > 0)
{
    foreach (var e in config.FatalErrors) log.LogCritical("config error: {Error}", e);
    return 2;
}
foreach (var w in config.Warnings) log.LogWarning("config: {Warning}", w);
log.LogInformation("UIA feedback relay starting: {Summary}", config.Describe());

var identity = new ClientIdentity();
var endpoints = new RelayEndpoints(config, new GitHubIssues(config));

// One structured line per request: method, path, status, outcome, ip-hash, duration.
app.Use(async (HttpContext ctx, RequestDelegate next) =>
{
    var started = Stopwatch.GetTimestamp();
    var clientKey = identity.KeyFor(ctx);
    ctx.Items[RelayEndpoints.ClientKeyItem] = clientKey;
    try
    {
        await next(ctx);
    }
    catch (Exception) when (ctx.RequestAborted.IsCancellationRequested)
    {
        ctx.Items[RelayEndpoints.OutcomeItem] = "client-aborted";
    }
    catch (Exception ex)
    {
        // A bug in here must never take the service down or leak a stack trace to a caller.
        ctx.Items[RelayEndpoints.OutcomeItem] = "error";
        ctx.Items[RelayEndpoints.DetailItem] = ex.GetType().Name;
        if (!ctx.Response.HasStarted)
        {
            ctx.Response.Clear();
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await ctx.Response.WriteAsJsonAsync(new ErrorResponse("Internal error."), RelayJson.Options);
        }
    }
    finally
    {
        var ms = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var outcome = ctx.Items.TryGetValue(RelayEndpoints.OutcomeItem, out var o) && o is string os
            ? os
            : ctx.Response.StatusCode == 404 ? "no-route" : "-";
        var path = LogSafe(ctx.Request.Path.Value, 100);
        if (ctx.Items.TryGetValue(RelayEndpoints.DetailItem, out var d) && d is string detail)
        {
            log.LogInformation("{Method} {Path} status={Status} outcome={Outcome} ip={IpHash} ms={Ms} detail=\"{Detail}\"",
                ctx.Request.Method, path, ctx.Response.StatusCode, outcome, ClientIdentity.Short(clientKey), ms,
                LogSafe(detail, 160));
        }
        else
        {
            log.LogInformation("{Method} {Path} status={Status} outcome={Outcome} ip={IpHash} ms={Ms}",
                ctx.Request.Method, path, ctx.Response.StatusCode, outcome, ClientIdentity.Short(clientKey), ms);
        }
    }
});

// A browser gets a human "where did you land" page instead of JSON (Jackson's rule for his APIs;
// LandingPage.cs). Vary: Accept on everything, so no cache ever hands one kind to the other.
app.Use(async (HttpContext ctx, RequestDelegate next) =>
{
    ctx.Response.Headers.Append("Vary", "Accept");
    if (LandingPage.Applies(ctx.Request))
    {
        ctx.Items[RelayEndpoints.OutcomeItem] = LandingPage.IsApiPath(ctx.Request.Path) ? "landing-page" : "landing-page-404";
        await LandingPage.Write(ctx);
        return;
    }
    await next(ctx);
});

app.MapGet("/v1/ping", endpoints.Ping);
app.MapPost("/v1/report", endpoints.Report);
app.MapGet("/v1/status/{number}", endpoints.Status);

// Belt and braces for the loopback-only invariant: if configuration (e.g. a stray
// Kestrel__Endpoints__* environment variable) ever adds a non-loopback address, refuse to run.
app.Lifetime.ApplicationStarted.Register(() =>
{
    var addresses = app.Services.GetRequiredService<IServer>()
        .Features.Get<IServerAddressesFeature>()?.Addresses ?? Array.Empty<string>();
    foreach (var address in addresses)
    {
        if (!IsLoopbackAddress(address))
        {
            log.LogCritical("refusing to serve on non-loopback address {Address}; the relay listens on 127.0.0.1 only", address);
            app.Lifetime.StopApplication();
            return;
        }
    }
    log.LogInformation("listening on {Addresses}", string.Join(", ", addresses));
});

app.Run();
return 0;

static bool IsLoopbackAddress(string address)
{
    if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) return false;
    var host = uri.Host.Trim('[', ']');
    if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
    return IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip);
}

// Request paths come from the internet: no control characters (log-line forgery), capped.
static string LogSafe(string? text, int max)
{
    if (string.IsNullOrEmpty(text)) return "";
    var sb = new StringBuilder(Math.Min(text.Length, max));
    foreach (var c in text)
    {
        if (sb.Length >= max) { sb.Append("..."); break; }
        sb.Append(char.IsControl(c) ? '?' : c == '"' ? '\'' : c);
    }
    return sb.ToString();
}
