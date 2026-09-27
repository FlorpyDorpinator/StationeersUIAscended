using System.Net;

namespace FeedbackRelay;

/// <summary>
/// Jackson's hard rule for his public APIs: a client that asks for HTML - a person in a browser -
/// gets a human page saying where they landed, never raw JSON or a bare 404. API clients are
/// untouched: the mod sends Accept: application/json, curl sends */*, and a tie between HTML and
/// JSON goes to JSON. Only GET/HEAD are answered with the page; a POST always reaches the API, so
/// content negotiation can never swallow a report.
/// </summary>
public static class LandingPage
{
    private const int MaxEchoedPathChars = 80;

    public static bool Applies(HttpRequest request) =>
        (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)) && WantsHtml(request);

    /// <summary>True when the client ranks HTML strictly above JSON. A wildcard (*/*) is not a
    /// request for HTML.</summary>
    public static bool WantsHtml(HttpRequest request)
    {
        double html = 0, json = 0;
        try
        {
            foreach (var m in request.GetTypedHeaders().Accept)
            {
                var q = m.Quality ?? 1.0;
                if (m.MediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase)
                    || m.MediaType.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase))
                    html = Math.Max(html, q);
                else if (m.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
                    json = Math.Max(json, q);
            }
        }
        catch (Exception)
        {
            return false; // unparseable Accept header: treat as an API client
        }
        return html > 0 && html > json;
    }

    /// <summary>The API's own routes answer 200 with the page; anything else is an honest 404.</summary>
    public static bool IsApiPath(PathString path) =>
        !path.HasValue || path.Value == "/"
        || path.Equals("/v1/ping", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/v1/report", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/v1/status", StringComparison.OrdinalIgnoreCase);

    public static async Task Write(HttpContext ctx)
    {
        var known = IsApiPath(ctx.Request.Path);
        var res = ctx.Response;
        res.StatusCode = known ? StatusCodes.Status200OK : StatusCodes.Status404NotFound;
        res.ContentType = "text/html; charset=utf-8";
        res.Headers.CacheControl = "no-cache";
        // No scripts, no external loads: the page needs inline CSS and a data: favicon, nothing else.
        res.Headers["Content-Security-Policy"] =
            "default-src 'none'; style-src 'unsafe-inline'; img-src data:; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
        res.Headers["X-Content-Type-Options"] = "nosniff";
        res.Headers["X-Frame-Options"] = "DENY";
        res.Headers["Referrer-Policy"] = "no-referrer";
        if (HttpMethods.IsHead(ctx.Request.Method)) return;
        await res.WriteAsync(Render(ctx.Request.Path.Value, known));
    }

    private static string Render(string? rawPath, bool known)
    {
        var path = rawPath ?? "/";
        if (path.Length > MaxEchoedPathChars) path = path[..MaxEchoedPathChars] + "...";
        var shown = WebUtility.HtmlEncode(path); // the path comes from the internet

        var note = "";
        if (!known)
            note = $"""<p class="note">There's nothing at <code>{shown}</code> (404) &mdash; and nothing meant for people anywhere else here, either.</p>""";
        else if (path != "/" && path.Length > 0)
            note = $"""<p class="note">You opened <code>{shown}</code> in a browser. That's one of the API's endpoints &mdash; it answers the mod, not people.</p>""";

        return $$"""
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex">
<title>UI Ascended &middot; Feedback Relay</title>
<link rel="icon" href="data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 32 32'%3E%3Ccircle cx='16' cy='16' r='11' fill='none' stroke='%235fb4ff' stroke-width='4' stroke-dasharray='8.5 3'/%3E%3Ccircle cx='16' cy='16' r='3' fill='%23ff9a3c'/%3E%3C/svg%3E">
<style>
  :root {
    color-scheme: dark;
    --bg: #060b14;
    --panel: rgba(13, 25, 43, 0.82);
    --line: rgba(95, 175, 255, 0.26);
    --line-soft: rgba(95, 175, 255, 0.12);
    --accent: #5fb4ff;
    --warm: #ff9a3c;
    --text: #dbe7f5;
    --mute: #8ea3bb;
    --ok: #46d68c;
    --mono: ui-monospace, "Cascadia Mono", "Segoe UI Mono", Consolas, monospace;
  }
  * { box-sizing: border-box; }
  html, body { margin: 0; }
  body {
    min-height: 100vh;
    display: grid;
    grid-template-columns: minmax(0, 680px);
    justify-content: center;
    align-content: center;
    padding: 32px 16px;
    color: var(--text);
    font: 16px/1.6 system-ui, -apple-system, "Segoe UI", Roboto, sans-serif;
    background:
      radial-gradient(1100px 560px at 50% -12%, #12305a 0%, transparent 62%),
      repeating-linear-gradient(0deg, rgba(95,175,255,.035) 0 1px, transparent 1px 32px),
      repeating-linear-gradient(90deg, rgba(95,175,255,.035) 0 1px, transparent 1px 32px),
      var(--bg);
  }
  .card {
    position: relative;
    width: 100%;
    min-width: 0;
    padding: clamp(22px, 5vw, 40px);
    background: var(--panel);
    border: 1px solid var(--line);
    border-radius: 14px;
    box-shadow: 0 24px 70px rgba(0,0,0,.55), 0 0 60px rgba(95,180,255,.10);
  }
  .card::before {
    content: "";
    position: absolute;
    inset: -1px -1px auto -1px;
    height: 2px;
    border-radius: 14px 14px 0 0;
    background: linear-gradient(90deg, transparent, var(--accent) 30%, var(--warm) 70%, transparent);
    opacity: .85;
  }
  .eyebrow {
    display: flex;
    align-items: center;
    gap: 10px;
    color: var(--accent);
    font: 600 12px/1 var(--mono);
    letter-spacing: .16em;
    text-transform: uppercase;
  }
  .eyebrow svg { flex: none; }
  h1 {
    margin: 20px 0 12px;
    font-size: clamp(27px, 5.5vw, 38px);
    line-height: 1.12;
    letter-spacing: -.01em;
  }
  h1 .warm { color: var(--warm); }
  .lead { margin: 0 0 16px; font-size: 17px; }
  h2 {
    margin: 30px 0 8px;
    color: var(--mute);
    font: 600 12px/1.2 var(--mono);
    letter-spacing: .14em;
    text-transform: uppercase;
  }
  p { margin: 0 0 12px; }
  strong { color: #fff; }
  a { color: var(--warm); text-underline-offset: 3px; }
  a:hover { color: #ffc083; }
  code {
    font: .9em var(--mono);
    padding: 1px 6px;
    border-radius: 5px;
    color: #c4e2ff;
    background: rgba(95,175,255,.10);
    border: 1px solid rgba(95,175,255,.18);
    overflow-wrap: anywhere;
  }
  .pill {
    display: inline-flex;
    align-items: center;
    gap: 8px;
    padding: 4px 12px;
    border-radius: 999px;
    font-size: 13px;
    color: var(--ok);
    background: rgba(70,214,140,.08);
    border: 1px solid rgba(70,214,140,.32);
  }
  .dot { width: 8px; height: 8px; border-radius: 50%; background: var(--ok); box-shadow: 0 0 8px var(--ok); }
  .note {
    padding: 10px 14px;
    border-radius: 8px;
    font-size: 15px;
    color: #ffd6b0;
    background: rgba(255,154,60,.08);
    border: 1px solid rgba(255,154,60,.26);
  }
  table { width: 100%; border-collapse: collapse; font-size: 14px; }
  td { padding: 9px 12px 9px 0; border-top: 1px solid var(--line-soft); vertical-align: top; }
  td.m { width: 1%; white-space: nowrap; color: var(--warm); font: 600 12px/1.9 var(--mono); }
  td.p { white-space: nowrap; }
  td.p code { overflow-wrap: normal; }
  .small { margin-top: 12px; color: var(--mute); font-size: 14px; }
  footer {
    margin-top: 30px;
    padding-top: 16px;
    border-top: 1px solid var(--line-soft);
    color: var(--mute);
    font-size: 13px;
  }
  @media (max-width: 520px) {
    td.p { white-space: normal; }
    td { padding-right: 8px; }
  }
</style>
</head>
<body>
<main class="card">
  <div class="eyebrow">
    <svg width="22" height="22" viewBox="0 0 32 32" aria-hidden="true">
      <circle cx="16" cy="16" r="11" fill="none" stroke="currentColor" stroke-width="4" stroke-dasharray="8.5 3"/>
      <circle cx="16" cy="16" r="3" fill="#ff9a3c"/>
    </svg>
    UI Ascended &middot; Feedback Relay
  </div>

  <h1>Huh <span class="warm">&mdash;</span> how did you get here?</h1>
  <p class="lead">This address is an <strong>API</strong>, not a website. It's the feedback relay for
    <strong>Stationeers UI Ascended</strong>, a UI-replacement mod for the game <em>Stationeers</em>.
    It exists to be talked to by the mod, so there's nothing here to click.</p>
  {{note}}
  <p><span class="pill"><span class="dot"></span>Relay online</span></p>

  <h2>What it does</h2>
  <p>When a player sends a bug report or a suggestion from inside the game, the mod posts it here.
    The relay checks it, strips anything that shouldn't be in it, and files it for the developers.
    No accounts and no tracking: reports are anonymous unless the player adds a contact name.</p>

  <h2>Want to report something?</h2>
  <p>Do it from the game: open the console and type <code>uiafeedback</code> to see how. Or find
    UI Ascended on the
    <a href="https://steamcommunity.com/sharedfiles/filedetails/?id=3776545141" rel="noopener noreferrer">Steam Workshop</a>.</p>

  <h2>For developers</h2>
  <table>
    <tr><td class="m">GET</td><td class="p"><code>/v1/ping</code></td><td>Health check.</td></tr>
    <tr><td class="m">POST</td><td class="p"><code>/v1/report</code></td><td>Submit a report. Sent by the mod; JSON; rate-limited.</td></tr>
    <tr><td class="m">GET</td><td class="p"><code>/v1/status/<wbr>{number}</code></td><td>What happened to a filed report.</td></tr>
  </table>
  <p class="small">Responses are JSON. You're seeing this page because your browser asked for HTML;
    send <code>Accept: application/json</code> to get the API instead.</p>

  <footer>Stationeers UI Ascended, by FlorpyDorp. This subdomain of <code>ssui.dev</code> is lent by
    JacksonTheMaster &mdash; UI Ascended and SSUI are separate projects.</footer>
</main>
</body>
</html>
""";
    }
}
