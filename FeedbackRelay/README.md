# UIA Feedback Relay

The small service that turns an in-game bug report or suggestion from Stationeers UI Ascended into
a GitHub issue. It holds the GitHub token on your Dell so the mod never ships one. It cleans up
and rate-limits every report, then files the issue that the triage bot picks up.

**Where the issues go (since 2026-09-27):** the PRIVATE repo
`FlorpyDorpinator/StationeersUIAscended-Feedback`, so players' reports stay private while this code
repo is public. The triage workflow (`.github/workflows/feedback-triage.yml`) lives there too and
checks this repo out read-only to plan against the code.

Spec: `Documentation/0.9.8.0/Feedback-Pipeline-Plan.md` §2 (relay) and §5 (invariants). It is
ported from the sister app's `api/feedback/github.py`.

```
game (F10 -> Feedback) --HTTPS--> Cloudflare edge --tunnel--> cloudflared (Dell) --> relay 127.0.0.1:8080 --> GitHub Issues API
```

- **Listens on `127.0.0.1` only.** The relay never binds a LAN or WAN interface. `cloudflared`
  reaches it over loopback. On startup the relay checks its own addresses and refuses to run if
  anything non-loopback slipped in.
- **Stateless.** GitHub is the database. Rate-limit and dedupe state live in memory, and a
  restart forgets them, which is fine for a backstop.
- **Logs go to the console only.** Each request gets one line: method, path, status, outcome, a
  keyed hash of the IP, and duration. Report text, profile XML, log excerpts, raw IPs, the client
  key and the token are never logged.
- **Touches no files** outside its own folder. It only reads configuration from environment
  variables.

---

## API

| Endpoint | Success | Errors |
|---|---|---|
| `GET /v1/ping` | `200 {ok: true, dryRun}` | none |
| `POST /v1/report` | `201 {number, url}`. In DRY_RUN: `201 {number: 0, dryRun: true, issue: {title, body, labels}}` | `400 {error}` validation · `403` missing/wrong `X-UIA-Client` (only when a key is configured) · `413` body over 1 MB · `429` rate-limited (has `Retry-After`) or duplicate (`{duplicate: true}`) · `502 {error, githubStatus?}` GitHub call failed · `503` token/repo not configured |
| `GET /v1/status/{number}` | `200 {state, labels: [...]}` | `404` unknown number, or an issue the relay didn't file · `429` more than 120 lookups/hour/IP · `502` GitHub failed · `503` DRY_RUN or not configured |

**Browsers get a landing page (Jackson's rule for his APIs).** A GET/HEAD whose `Accept`
header ranks `text/html` strictly above `application/json` - i.e. a person in a browser - gets
a styled "huh, how did you get here? this is an API" page (`LandingPage.cs`): 200 on the API's
own paths, 404 elsewhere, no scripts, strict CSP, echoed path HTML-escaped. The mod
(`Accept: application/json`), curl (`*/*`) and any HTML/JSON tie still get JSON, and a POST
always reaches the API. Every response carries `Vary: Accept`. Logged as `outcome=landing-page`.

Request body for `POST /v1/report` (camelCase; everything except kind/title/description is optional):

```json
{
  "kind": "bug",
  "title": "Radial closes when swapping hands",
  "description": "What I did...\nWhat I expected...\nWhat happened...",
  "contact": "JacksonEnjoyer42",
  "context": {
    "modVersion": "0.9.7.4 Experimental", "deployRoute": "ScriptEngine", "gameBuild": "27701",
    "profileName": "Stationeers Blue", "profileModified": true,
    "playerContext": "MP client - Suited tier", "display": "2560x1440 @ 1.0", "os": "Windows 11 ..."
  },
  "profileXml": "<HudDocument ...>...</HudDocument>",
  "logExcerpt": "[Info   :UIALog] ..."
}
```

What the relay enforces (the mod's own caps are only a courtesy):

- **Hard caps (rejected with 400 if over):** title 120, description 4,000, contact 60 characters.
  The in-game form uses the same caps, so only a broken or hostile client ever hits these.
- **Soft caps (truncated, never rejected):** profile XML 100 KB, where the head is kept. Log
  excerpt 30 KB, where the tail (most recent lines) is kept. Each context field is capped at 200
  characters.
- **Issue body cap: 60,000 characters.** When a report is too big, the log excerpt is trimmed
  first, then the profile XML. The description is never trimmed. Each trimmed section gets a
  `<sub>(truncated)</sub>` note.
- **Defanging:** every user string has `<!--` / `-->` defanged with a non-breaking hyphen
  (U+2011), so nobody can forge the `uia-feedback` or `uia-triage-plan` markers. One side
  effect: XML comments inside an attached profile come through defanged as well. The shipped
  profiles contain none.
- **Rate limit:** 3 `POST`s per hour per client IP. The IP comes from `CF-Connecting-IP`
  (cloudflared forwards it), or from the socket when that header is absent. IPv6 addresses are
  grouped by `/64`. The same title and description within an hour gets a 429 no matter who
  sends it. If a report fails to reach GitHub (502), it is not counted as a duplicate, so the
  mod's outbox can retry it.
- **Labels:** `feedback`, `bug` or `enhancement`, `triage:pending`.

---

## Configuration (environment variables)

| Variable | Meaning |
|---|---|
| `UIA_FEEDBACK_GITHUB_TOKEN` | Fine-grained PAT (see §3). **Secret.** |
| `UIA_FEEDBACK_REPO` | `owner/name`: `FlorpyDorpinator/StationeersUIAscended-Feedback` (private; never the public code repo) |
| `UIA_RELAY_PORT` | Loopback port, default `8080` |
| `UIA_RELAY_CLIENT_KEY` | Optional. When set, `POST /v1/report` requires a matching `X-UIA-Client` header (compared in constant time). This only keeps drive-by scanners out; it is not real security, because the value ships inside the mod. |
| `UIA_RELAY_REPORTS_PER_HOUR` | Optional. Reports per hour per client address, 1-100 (default 6). A bad value falls back to 6 with a startup warning. `setup-dell.ps1 -ReportsPerHour N` sets it. `/v1/ping` reports the value in force. |
| `UIA_RELAY_DRY_RUN` | `1` means never call GitHub. The report endpoint returns the issue it *would* file, and status lookups return 503. |

The relay reads these once at startup. After changing one, restart the process or service.

---

## 1. Run it locally (DRY_RUN) and smoke-test it

Needs the .NET 10 SDK. The project targets `net10.0`; see ".NET version" below.

```powershell
cd "C:\Dev\Stationeers UI Ascended\FeedbackRelay"
$env:UIA_RELAY_DRY_RUN = "1"
dotnet run -c Release
```

In a second PowerShell window:

```powershell
cd "C:\Dev\Stationeers UI Ascended\FeedbackRelay"
powershell -ExecutionPolicy Bypass -File .\smoke-test.ps1
```

The smoke test runs in Windows PowerShell 5.1 and makes about 40 requests. It checks:
- ping
- a full bug report with profile XML: marker, header, quoted description, details table,
  attachment block, labels
- a forged `<!-- forged-marker -->` and forged markers coming back defanged
- every cap
- the truncation order
- dedupe and the per-hour limit (read from `/v1/ping`, so it follows whatever is configured)
- the status endpoint
- the `X-UIA-Client` gate, when you start the relay with `$env:UIA_RELAY_CLIENT_KEY = "..."` and
  pass the same value as `-ClientKey "..."`

It **refuses to run unless the relay says `dryRun: true`**, so it can never file real issues. It
is safe to re-run against the same relay process.

To eyeball one issue by hand, save a request body (like the JSON above) as `test-report.json`,
then run:

```powershell
curl.exe -s -X POST http://127.0.0.1:8080/v1/report -H "Content-Type: application/json" --data-binary "@test-report.json"
```

(Use a file for the body. Windows PowerShell 5.1 strips the inner `"` quotes from an inline
JSON argument to `curl.exe`.)

## 2. Publish for the Dell

```powershell
cd "C:\Dev\Stationeers UI Ascended\FeedbackRelay"
dotnet publish -c Release -r win-x64 --self-contained
```

Output: `FeedbackRelay\bin\Release\net10.0\win-x64\publish\`, about 106 MB. The exe is
`FeedbackRelay.exe`. The build is self-contained, so the Dell does **not** need .NET installed.
Copy the whole `publish` folder to the Dell, e.g. to `C:\Services\UIAFeedbackRelay\`. The rest
of this README assumes that path. `bin/` and `obj/` are git-ignored.

## 3. Mint the GitHub token (fine-grained PAT)

1. Sign in to GitHub as **FlorpyDorpinator**, then go to **Settings -> Developer settings ->
   Personal access tokens -> Fine-grained tokens -> Generate new token**.
2. Fill in:
   - Name: `UIA feedback relay`
   - Resource owner: `FlorpyDorpinator`
   - Expiration: pick one and put a rotation reminder in your calendar
3. **Repository access:** *Only select repositories* -> `StationeersUIAscended-Feedback`. Just that
   one repo. (An existing token can be edited to add it; its value does not change.)
4. **Repository permissions:** set **Issues = Read and write**. Nothing else. GitHub adds
   *Metadata: Read-only* on its own, which is expected.
5. Generate the token, copy it once, and save it as `C:\Dev\.secrets\UIA Feedback Token.txt`.
   That follows the house convention next to `the sister app Feedback Token.txt`. **Never put it in
   this repo, the mod, an issue, or a chat.**

Create the labels the relay applies, so GitHub never answers 422 "label missing":

```powershell
$repo = "FlorpyDorpinator/StationeersUIAscended-Feedback"
gh label create feedback         --repo $repo --color 0E8A16 --description "Filed from in-game feedback" --force
gh label create "triage:pending" --repo $repo --color FBCA04 --description "Waiting for the triage bot" --force
gh label create "plan:ready"     --repo $repo --color 1D76DB --description "Triage bot posted a plan"   --force
gh label list --repo $repo       # confirm bug and enhancement exist too (GitHub creates them by default)
```

To rotate the token:
1. Mint a new token.
2. Overwrite the secrets file.
3. Re-run the registry step in §4b.
4. `Restart-Service UIAFeedbackRelay`.
5. Delete the old token on GitHub.

## 4. Put the settings on the Dell

The service gets its own environment, which is §4b. That is the production setup. §4a is only
for running the exe by hand in a console.

### 4a. For console runs (your user account)

```powershell
setx UIA_FEEDBACK_REPO "FlorpyDorpinator/StationeersUIAscended-Feedback"
# Read the token from the secrets file so it never appears on a command line or in PSReadLine history:
[Environment]::SetEnvironmentVariable("UIA_FEEDBACK_GITHUB_TOKEN", (Get-Content "C:\Dev\.secrets\UIA Feedback Token.txt" -Raw).Trim(), "User")
```

These only apply to **new** windows. A Windows service does **not** see user variables, because
it runs as a different account. It only sees machine variables after a reboot. That is why the
service gets its own environment below.

### 4b. For the Windows service (per-service environment)

Run this in an **elevated** Windows PowerShell *after* creating the service in §5. The service
control manager reads the service's `Environment` registry value (REG_MULTI_SZ) at every start.
That scopes the token to this one service, and it never touches the machine-wide environment:

```powershell
$tok = (Get-Content "C:\Dev\.secrets\UIA Feedback Token.txt" -Raw).Trim()
New-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\UIAFeedbackRelay" -Name Environment -PropertyType MultiString -Force -Value @(
    "UIA_FEEDBACK_GITHUB_TOKEN=$tok",
    "UIA_FEEDBACK_REPO=FlorpyDorpinator/StationeersUIAscended-Feedback",
    "UIA_RELAY_CLIENT_KEY=<the same value the mod sends as X-UIA-Client>"
) | Out-Null
Remove-Variable tok
Restart-Service UIAFeedbackRelay
```

If you don't use a client key, leave out the `UIA_RELAY_CLIENT_KEY` line. Add
`"UIA_RELAY_DRY_RUN=1"` to the list for a dry-run service. Local accounts on the Dell can read
service registry values, which is fine on a single-user box.

## 5. Run the relay as a Windows service

**Shortcut:** `setup-dell.ps1` (also copied into the `publish` folder) does §4b, §5 and the
tunnel install from §6 in one pass, checking each step and stopping with a clear message on
failure. From an elevated PowerShell on the Dell:
`powershell -ExecutionPolicy Bypass -File C:\Services\UIAFeedbackRelay\setup-dell.ps1`
(starts in DRY RUN; re-run with `-GoLive` to add the GitHub token). The manual steps below
remain the reference.

The exe has Windows-service support built in (`Microsoft.Extensions.Hosting.WindowsServices`).
A service is the simplest reliable option:
- it starts at boot without anyone logging in
- the service manager restarts it if it crashes
- it runs under the low-privilege **LocalService** account, not SYSTEM, which matters because
  it faces the internet
- its environment is scoped to the service

Run this in an **elevated** Windows PowerShell. Use `sc.exe`, not `sc`, which is an alias for
`Set-Content` in PowerShell:

```powershell
icacls "C:\Services\UIAFeedbackRelay" /grant "NT AUTHORITY\LocalService:(OI)(CI)RX"
sc.exe create UIAFeedbackRelay binPath= "C:\Services\UIAFeedbackRelay\FeedbackRelay.exe" start= delayed-auto obj= "NT AUTHORITY\LocalService" DisplayName= "UIA Feedback Relay"
sc.exe description UIAFeedbackRelay "Stationeers UI Ascended in-game feedback to GitHub issues (loopback only, fronted by cloudflared)"
sc.exe failure UIAFeedbackRelay reset= 86400 actions= restart/5000/restart/5000/restart/60000
sc.exe qc UIAFeedbackRelay        # expect SERVICE_START_NAME : NT AUTHORITY\LocalService
```

Next, do §4b, which sets the environment and restarts the service. Then check it:

```powershell
Get-Service UIAFeedbackRelay
curl.exe -s http://127.0.0.1:8080/v1/ping      # {"ok":true,"dryRun":false}
```

**Updating to a new build** (after re-running §2 on the dev box and copying `publish` over as
`C:\Temp\publish`, for example):

```powershell
Stop-Service UIAFeedbackRelay
robocopy "C:\Temp\publish" "C:\Services\UIAFeedbackRelay" /MIR
Start-Service UIAFeedbackRelay
```

**Removing it:** `Stop-Service UIAFeedbackRelay; sc.exe delete UIAFeedbackRelay`

### Logs

By design, the relay only logs to the console. A service has no console, so while it runs as a
service its log lines go nowhere. Two ways to see what's happening:

- **Every error response explains itself.** A 502 from `POST /v1/report` names GitHub's status
  code and a hint, e.g. `GitHub said 401 - the token was rejected and may need rotating.` A curl
  through the tunnel is usually enough to diagnose.
- **Watch it live.** Stop the service and run the same exe with the same environment in an
  elevated console:

  ```powershell
  Stop-Service UIAFeedbackRelay
  (Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\UIAFeedbackRelay").Environment |
      ForEach-Object { $k, $v = $_ -split "=", 2; Set-Item "Env:$k" $v }
  & "C:\Services\UIAFeedbackRelay\FeedbackRelay.exe"     # Ctrl+C when done
  Start-Service UIAFeedbackRelay
  ```

A log line looks like this:
`2026-09-25 21:14:03Z info: relay[0] POST /v1/report status=201 outcome=filed#104 ip=3fa9c1d2e0b4 ms=532`.
The `ip=` value is an HMAC of the address under a random key that exists only in memory. It
lets you correlate requests within one run, can't be reversed, and changes on every restart.

## 6. Cloudflare Tunnel (the public face)

These facts were checked against developers.cloudflare.com/tunnel on 2026-09-25:

- Tunnel is **free on all plans**. A public hostname needs a **domain on Cloudflare**.
- `cloudflared` only makes **outbound** connections, on outbound port **7844**. You open **no
  inbound router ports**, and the home IP is never published.

Install `cloudflared` on the Dell, either from Cloudflare's downloads page or with
`winget install --id Cloudflare.cloudflared`.

### Quick Tunnel (testing only)

```powershell
cloudflared tunnel --url http://localhost:8080
```

This prints an ephemeral `https://<random>.trycloudflare.com` URL. It needs no account and no
domain, is for testing only, and is limited to 200 concurrent requests. Point a curl, or the
mod's `Feedback.RelayUrl` setting, at it. The relay listens on IPv4 loopback only, so if
cloudflared reports "connection refused" on `[::1]`, use `--url http://127.0.0.1:8080` instead.

### Named tunnel (production)

1. In the Cloudflare **Zero Trust dashboard**, create a tunnel of type *Cloudflared* and name it
   e.g. `uia-feedback`. The dashboard shows an install command that contains the tunnel token.
2. Copy the token into `C:\Dev\.secrets\UIA Cloudflare Tunnel Token.txt`. It is a secret too.
3. In an **elevated** prompt on the Dell, run the following. It installs cloudflared as a
   Windows service that reconnects across reboots:

   ```powershell
   cloudflared.exe service install <TUNNEL_TOKEN>
   ```

4. Add a **public hostname** on the tunnel mapped to service `http://127.0.0.1:8080` (not
   `localhost`, for the IPv4-loopback reason above; and **no trailing slash** - the dashboard
   rejects `http://127.0.0.1:8080/` as "proxying to a different path"). Leave Path empty.
5. Test it: `curl.exe -s https://uiascended.ssui.dev/v1/ping`
6. Nothing to set in the mod: an empty `[Feedback] RelayUrl` means the built-in relay.

**Production (decided 2026-09-26):** the tunnel lives in a separate Cloudflare account
(a tunnel can only route hostnames in its own account); he routed `uiascended.ssui.dev` to it
and handed over only the tunnel token. That hostname is the mod's built-in default
(`FeedbackService.DefaultRelayUrl`). Do NOT use `uiascended.stationeers-wiki.com`: that zone
puts a managed challenge in front of every request (verified with game, browser and curl user
agents - all 403 `cf-mitigated: challenge`), which the game can never pass.

### Edge rate limiting (defense in depth)

Add a Cloudflare **rate limiting rule** on the report path (`/v1/report`, method POST, counted
per IP), so floods stop at the edge before they reach the Dell. **Plan caveat**, checked against
developers.cloudflare.com/waf/rate-limiting-rules on 2026-09-25: the **Free** plan allows 1 rule
with a counting period of **10 seconds only**, a 10 s mitigation timeout, and IP-only counting.
Pro allows periods up to 1 minute and timeouts up to 1 hour. A literal "3 per hour" rule at the
edge therefore isn't possible on Free.

- **On Free**, use the one rule as a burst filter. For example, more than 2 POSTs to
  `/v1/report` in 10 s from one IP gets a block for 10 s.
- **On any plan**, the relay's own per-IP limit (6 per hour by default) is the real hourly limit. It runs
  whatever the edge rule says.

## 7. Phase 0 end-to-end (real issue, not dry run)

Before starting, the service should be running with the token and repo set, the labels should
exist, and a tunnel should be up. Save a test body as `test-report.json` (see the API section)
and run:

```powershell
curl.exe -s -X POST "https://uiascended.ssui.dev/v1/report" -H "Content-Type: application/json" -H "X-UIA-Client: <key, if configured>" --data-binary "@test-report.json"
```

Expected: `{"number":<n>,"url":"https://github.com/FlorpyDorpinator/StationeersUIAscended-Feedback/issues/<n>"}`.
The issue appears with `feedback`, `bug`, and `triage:pending`. Then
`curl.exe -s https://uiascended.ssui.dev/v1/status/<n>` returns
`{"state":"open","labels":[...]}`.

Remember the dedupe: sending the same title and description again within an hour gets a 429.

## Troubleshooting

| You see | Meaning |
|---|---|
| `503` "not configured" | `UIA_FEEDBACK_GITHUB_TOKEN` or `UIA_FEEDBACK_REPO` is missing or malformed in the process's environment, and DRY_RUN is off. For the service, check §4b. |
| `502` "GitHub said 401" | The token was rejected: expired, revoked, or mistyped. Rotate it (§3). |
| `502` "GitHub said 403" | The token lacks **Issues: Read and write** on this repo, or GitHub is rate-limiting the relay. |
| `502` "GitHub said 404" | Wrong `UIA_FEEDBACK_REPO`, or the token isn't scoped to that repo. |
| `502` "GitHub said 422" | GitHub rejected the issue, most likely because a label is missing. Create the labels (§3). |
| `502` "did not answer within 8 s" / "Couldn't reach GitHub" | Network trouble on the Dell. The mod's outbox will retry. |
| `429` with `Retry-After` | Per-IP limit (6/hour by default; `UIA_RELAY_REPORTS_PER_HOUR`). |
| `429` with `duplicate: true` | The same title and description arrived within the hour. |
| Edge `530` / `1033` | The tunnel or `cloudflared` is down, or the relay isn't listening. The mod's outbox keeps the report until it's back. |

## .NET version

The relay targets **.NET 10 (LTS, supported to November 2028)** - moved from .NET 9 on
2026-09-26, ahead of .NET 9's end of support (2026-11-10). The dev box has the .NET 10 SDK
(10.0.401) installed alongside 9. `Microsoft.Extensions.Hosting.WindowsServices` is on 10.0.12.

Because the publish is self-contained, **a runtime move means replacing the WHOLE folder on
the Dell** (the runtime's DLLs all change), not just `FeedbackRelay.dll`:

```powershell
Stop-Service UIAFeedbackRelay
Rename-Item C:\Services\UIAFeedbackRelay UIAFeedbackRelay-backup   # rollback = swap back
New-Item -ItemType Directory C:\Services\UIAFeedbackRelay | Out-Null
# paste EVERYTHING from bin\Release\net10.0\win-x64\publish\ into C:\Services\UIAFeedbackRelay\
powershell -ExecutionPolicy Bypass -File C:\Services\UIAFeedbackRelay\setup-dell.ps1
```

Re-running `setup-dell.ps1` re-grants the service account's read access on the new folder,
keeps the existing settings (token, repo, limit - they live in the service's registry entry,
not the folder), restarts the relay and pings it.

## Files

| File | What it is |
|---|---|
| `Program.cs` | Host setup: loopback binding, console logging, the one-line request log, the loopback self-check |
| `RelayConfig.cs` | Environment variables, validation, a startup summary that never prints the token |
| `RelayEndpoints.cs` | The three endpoints: client-key gate, rate limit, validation, dedupe, DRY_RUN |
| `ReportModels.cs` | Request/response shapes, the caps, validation/normalization |
| `Sanitizer.cs` | `Neutralize` (comment defanging), table-cell and code-span cleaning, safe code fences, truncation |
| `IssueBuilder.cs` | The issue title/body/labels (plan §2.5) and the truncation order |
| `GitHubIssues.cs` | The only GitHub client: 8 s timeout, one attempt, status-code hints |
| `RateLimiter.cs` | Sliding-window limiter, dedupe window, 60 s status cache |
| `ClientIdentity.cs` | Resolves `CF-Connecting-IP` into a keyed hash (rate-limit key and log token) |
| `LandingPage.cs` | The browser-only "this is an API" page and its Accept-header negotiation |
| `setup-dell.ps1` | One-shot, re-runnable installer for the Dell (service + tunnel; `-GoLive` adds the token) |
| `smoke-test.ps1` | The DRY_RUN smoke test (PowerShell 5.1) |
