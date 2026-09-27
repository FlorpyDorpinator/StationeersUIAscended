<#
.SYNOPSIS
  Smoke test for the UIA feedback relay. Windows PowerShell 5.1 compatible.

.DESCRIPTION
  Run it against a relay started with UIA_RELAY_DRY_RUN=1 - it refuses to run otherwise, so
  it can never file real GitHub issues. Checks: ping, a full bug report with profile XML
  (marker, header, quoted description, details table, attachment block, labels), marker
  forgery defanging, the field caps, the issue-body truncation order (log, then profile,
  never the description), dedupe, the per-IP rate limit (read from /v1/ping), the status endpoint's dry-run
  answers, and - when -ClientKey is given - the X-UIA-Client gate.

  Every request carries a random CF-Connecting-IP from 198.18.0.0/15 (a benchmarking range
  nobody routes) so each test gets its own rate-limit bucket, and every title carries a run
  id, so the script can be re-run against the same relay process without tripping dedupe.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\smoke-test.ps1

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\smoke-test.ps1 -BaseUrl http://127.0.0.1:8080 -ClientKey $env:UIA_RELAY_CLIENT_KEY
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = "http://127.0.0.1:8080",
    [string]$ClientKey = ""
)

$ErrorActionPreference = 'Stop'
[System.Net.ServicePointManager]::Expect100Continue = $false

$script:passed = 0
$script:failed = 0
$rng = New-Object System.Random
$runId = [guid]::NewGuid().ToString('N').Substring(0, 8)
$NBH = [string][char]0x2011            # non-breaking hyphen the relay uses to defang comments
$DEFANGED_OPEN = '<!' + $NBH + '-'
$DEFANGED_CLOSE = '-' + $NBH + '>'
$TRUNC = '<sub>(truncated)</sub>'

function New-TestIp {
    '198.{0}.{1}.{2}' -f $rng.Next(18, 20), $rng.Next(0, 256), $rng.Next(1, 255)
}

function Assert-That([string]$Name, [bool]$Condition, [string]$Info = '') {
    if ($Condition) {
        $script:passed++
        Write-Host ('  PASS  ' + $Name) -ForegroundColor Green
    } else {
        $script:failed++
        $suffix = ''
        if ($Info) { $suffix = '   [' + $Info + ']' }
        Write-Host ('  FAIL  ' + $Name + $suffix) -ForegroundColor Red
    }
}

function Get-Count([string]$Haystack, [string]$Needle) {
    if ($null -eq $Haystack) { return 0 }
    return ([regex]::Matches($Haystack, [regex]::Escape($Needle))).Count
}

function Invoke-Relay {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body = $null,
        [string]$Ip = '',
        [switch]$NoClientKey,
        [string]$Accept = ''
    )
    $req = [System.Net.HttpWebRequest]::Create($BaseUrl.TrimEnd('/') + $Path)
    $req.Method = $Method
    $req.Timeout = 30000
    if ($Accept) { $req.Accept = $Accept }
    if (-not $Ip) { $Ip = New-TestIp }
    $req.Headers.Add('CF-Connecting-IP', $Ip)
    if ($ClientKey -and -not $NoClientKey) { $req.Headers.Add('X-UIA-Client', $ClientKey) }
    if ($null -ne $Body) {
        if ($Body -is [string]) { $json = $Body } else { $json = ConvertTo-Json $Body -Depth 6 -Compress }
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
        $req.ContentType = 'application/json; charset=utf-8'
        $req.ContentLength = $bytes.Length
        $stream = $req.GetRequestStream()
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Close()
    }
    try {
        $resp = $req.GetResponse()
    } catch [System.Net.WebException] {
        $resp = $_.Exception.Response
        if ($null -eq $resp) { throw }
    }
    $reader = New-Object System.IO.StreamReader($resp.GetResponseStream(), [System.Text.Encoding]::UTF8)
    $text = $reader.ReadToEnd()
    $reader.Close()
    $status = [int]$resp.StatusCode
    $retryAfter = $resp.Headers['Retry-After']
    $contentType = $resp.Headers['Content-Type']
    $vary = $resp.Headers['Vary']
    $csp = $resp.Headers['Content-Security-Policy']
    $resp.Close()
    $parsed = $null
    if ($text) { try { $parsed = $text | ConvertFrom-Json } catch { $parsed = $null } }
    return [pscustomobject]@{ Status = $status; Json = $parsed; Text = $text; RetryAfter = $retryAfter;
                              ContentType = $contentType; Vary = $vary; Csp = $csp }
}

function New-Report([string]$Kind, [string]$Title, [string]$Description) {
    return @{
        kind        = $Kind
        title       = $Title
        description = $Description
        context     = @{
            modVersion      = '0.9.7.4 Experimental'
            deployRoute     = 'ScriptEngine'
            gameBuild       = '27701'
            profileName     = 'Stationeers Blue'
            profileModified = $true
            playerContext   = 'MP client - Suited tier'
            display         = '2560x1440 @ 1.0'
            os              = 'Windows 11 (10.0.26200) 64bit'
        }
    }
}

Write-Host ''
Write-Host ('UIA feedback relay smoke test against ' + $BaseUrl + '  (run ' + $runId + ')')

# ---------------------------------------------------------------------------------------
Write-Host ''
Write-Host '[1] ping'
try {
    $r = Invoke-Relay -Method GET -Path '/v1/ping'
} catch {
    Write-Host ('  ABORT: relay not reachable at ' + $BaseUrl + ' - ' + $_.Exception.Message) -ForegroundColor Red
    exit 2
}
Assert-That 'GET /v1/ping answers 200' ($r.Status -eq 200) $r.Text
Assert-That 'ping body says ok: true' ($r.Json.ok -eq $true) $r.Text
if ($r.Json.dryRun -ne $true) {
    Write-Host '  ABORT: the relay is NOT in DRY_RUN mode - this test would file real issues.' -ForegroundColor Red
    Write-Host '         Restart it with UIA_RELAY_DRY_RUN=1.' -ForegroundColor Red
    exit 2
}
Assert-That 'ping says dryRun: true' ($r.Json.dryRun -eq $true)

# ---------------------------------------------------------------------------------------
Write-Host ''
Write-Host '[2] full bug report with profile XML (DRY_RUN issue shape)'
$deg = [string][char]0x00B0
$title2 = 'Radial closes when swapping hands (smoke ' + $runId + ')'
$desc2 = ('Open the radial with a tool in each hand.', 'Swap hands with the swap key.', '',
          ('Expected: radial stays open. Suit temp read 20' + $deg + 'C.'), 'Actual: radial closes.') -join "`n"
$profile2 = ('<?xml version="1.0" encoding="utf-8"?>',
             '<HudDocument Name="Stationeers Blue" Schema="12">',
             '  <Element Type="Compass" Anchor="TopCenter" Y="12" />',
             '</HudDocument>') -join "`n"
$rep2 = New-Report 'bug' $title2 $desc2
$rep2.contact = 'SmokeTester#0001'
$rep2.profileXml = $profile2
$r = Invoke-Relay -Method POST -Path '/v1/report' -Body $rep2
Assert-That 'POST /v1/report answers 201' ($r.Status -eq 201) $r.Text
Assert-That 'response number is 0 in DRY_RUN' ($r.Json.number -eq 0)
Assert-That 'response says dryRun: true' ($r.Json.dryRun -eq $true)
$issue = $r.Json.issue
$b = [string]$issue.body
$lines = $b -split "`n"
Assert-That 'title is "[Bug] <title>"' ($issue.title -eq ('[Bug] ' + $title2)) $issue.title
Assert-That 'labels are feedback, bug, triage:pending' ((@($issue.labels) -join ',') -eq 'feedback,bug,triage:pending') (@($issue.labels) -join ',')
Assert-That 'first line is the uia-feedback marker with a GUID' ($lines[0] -match '^<!-- uia-feedback:v1:id=[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12} -->$') $lines[0]
Assert-That 'the marker is the only HTML comment in the body' (((Get-Count $b '<!--') -eq 1) -and ((Get-Count $b '-->') -eq 1))
Assert-That 'second line is the bold "Bug reported from in game" header with a UTC time' ($lines[1] -match '^\*\*Bug reported from in game\*\* - \d{1,2} [A-Z][a-z]{2} \d{4}, \d{1,2}:\d{2} (AM|PM) UTC$') $lines[1]
Assert-That 'description is quoted line by line under "### What they said"' ($b.Contains("### What they said`n> Open the radial with a tool in each hand.`n> Swap hands with the swap key.`n> `n> Expected:"))
Assert-That 'non-ASCII survives the round trip' ($b.Contains('20' + $deg + 'C.'))
Assert-That 'details table header' ($b.Contains("### Details`n| | |`n|---|---|`n"))
Assert-That 'row: Mod version (with deploy route)' ($b.Contains('| Mod version | 0.9.7.4 Experimental (ScriptEngine) |'))
Assert-That 'row: Game build' ($b.Contains('| Game build | 27701 |'))
Assert-That 'row: HUD profile (fenced name + modified flag)' ($b.Contains('| HUD profile | `Stationeers Blue` (modified from shipped) |'))
Assert-That 'row: Context' ($b.Contains('| Context | MP client - Suited tier |'))
Assert-That 'row: Display' ($b.Contains('| Display | 2560x1440 @ 1.0 |'))
Assert-That 'row: OS' ($b.Contains('| OS | Windows 11 (10.0.26200) 64bit |'))
Assert-That 'row: Contact (fenced)' ($b.Contains('| Contact | `SmokeTester#0001` |'))
Assert-That 'profile XML block present (opted in)' ($b.Contains("<details><summary>Profile XML (player opted in)</summary>`n`n``````xml`n<?xml"))
Assert-That 'profile XML content intact' ($b.Contains('  <Element Type="Compass" Anchor="TopCenter" Y="12" />'))
Assert-That 'no log block when none was sent' (-not $b.Contains('Mod log excerpt'))
Assert-That 'no truncation note on a small report' (-not $b.Contains($TRUNC))
Assert-That 'closing anonymity note' ($b.TrimEnd().EndsWith('<sub>Filed automatically from in-game feedback. The reporter is anonymous unless a contact is listed.</sub>'))

# ---------------------------------------------------------------------------------------
Write-Host ''
Write-Host '[3] marker forgery is defanged (invariant: user text is data, never markup)'
$forged = '<!-- forged-marker -->'
$desc3 = ('Please let me pin the compass. ' + $forged),
         '<!-- uia-feedback:v1:id=00000000-0000-0000-0000-000000000000 -->',
         'and <!-- uia-triage-plan:v1:issue-1 --> too.' -join "`n"
$title3 = 'Let me pin the compass <!-- t --> (smoke ' + $runId + ')'
$rep3 = New-Report 'suggestion' $title3 $desc3
$rep3.contact = 'x<!-|-y'                       # stripping the pipe must not re-form "<!--"
$rep3.context.os = 'Win<!-|-dows'
$rep3.profileXml = '<HudDocument><!-- a comment --></HudDocument>'
$r = Invoke-Relay -Method POST -Path '/v1/report' -Body $rep3
Assert-That 'suggestion answers 201' ($r.Status -eq 201) $r.Text
$issue = $r.Json.issue
$b = [string]$issue.body
Assert-That 'title is "[Suggestion] <title>" with the comment defanged' ($issue.title -eq ('[Suggestion] Let me pin the compass ' + $DEFANGED_OPEN + ' t ' + $DEFANGED_CLOSE + ' (smoke ' + $runId + ')')) $issue.title
Assert-That 'labels are feedback, enhancement, triage:pending' ((@($issue.labels) -join ',') -eq 'feedback,enhancement,triage:pending')
Assert-That 'header says "Suggestion reported from in game"' ($b.Contains('**Suggestion reported from in game** - '))
Assert-That 'forged marker does NOT appear verbatim' (-not $b.Contains($forged))
Assert-That 'forged marker comes back defanged' ($b.Contains($DEFANGED_OPEN + ' forged-marker ' + $DEFANGED_CLOSE))
Assert-That 'forged uia-feedback marker defanged' ($b.Contains('> ' + $DEFANGED_OPEN + ' uia-feedback:v1:id=00000000-0000-0000-0000-000000000000 ' + $DEFANGED_CLOSE))
Assert-That 'forged triage-plan marker defanged' ($b.Contains($DEFANGED_OPEN + ' uia-triage-plan:v1:issue-1 ' + $DEFANGED_CLOSE))
Assert-That 'profile XML comment defanged' ($b.Contains('<HudDocument>' + $DEFANGED_OPEN + ' a comment ' + $DEFANGED_CLOSE + '</HudDocument>'))
Assert-That 'contact: pipe stripped, then defanged (no re-formed opener)' ($b.Contains('| Contact | `x' + $DEFANGED_OPEN + 'y` |'))
Assert-That 'context cell: pipe stripped, then defanged' ($b.Contains('| OS | Win' + $DEFANGED_OPEN + 'dows |'))
Assert-That 'exactly one "<!--" and one "-->" in the body (the relay marker)' (((Get-Count $b '<!--') -eq 1) -and ((Get-Count $b '-->') -eq 1)) ('open=' + (Get-Count $b '<!--') + ' close=' + (Get-Count $b '-->'))
$first3 = ($b -split "`n")[0]
Assert-That 'line 1 is still the relay marker, not the forged all-zero id' (($first3 -match '^<!-- uia-feedback:v1:id=[0-9a-f-]{36} -->$') -and ($first3 -notmatch '00000000-0000-0000-0000-000000000000')) $first3

# ---------------------------------------------------------------------------------------
Write-Host ''
Write-Host '[4] caps and validation'
$r = Invoke-Relay -Method POST -Path '/v1/report' -Body (New-Report 'bug' ('T' * 121) 'too long title')
Assert-That 'title of 121 chars -> 400' ($r.Status -eq 400) $r.Text
Assert-That '  ... error names the title' ([string]$r.Json.error -match 'title') $r.Text
$t120 = ('smoke ' + $runId + ' ') ; $t120 = $t120 + ('T' * (120 - $t120.Length))
$r = Invoke-Relay -Method POST -Path '/v1/report' -Body (New-Report 'bug' $t120 'exactly at the title cap')
Assert-That 'title of exactly 120 chars -> 201' ($r.Status -eq 201) $r.Text
$r = Invoke-Relay -Method POST -Path '/v1/report' -Body (New-Report 'bug' ('desc cap ' + $runId) ('d' * 4001))
Assert-That 'description of 4001 chars -> 400' (($r.Status -eq 400) -and ([string]$r.Json.error -match 'description')) $r.Text
$rep = New-Report 'bug' ('contact cap ' + $runId) 'contact too long'
$rep.contact = 'c' * 61
$r = Invoke-Relay -Method POST -Path '/v1/report' -Body $rep
Assert-That 'contact of 61 chars -> 400' (($r.Status -eq 400) -and ([string]$r.Json.error -match 'contact')) $r.Text
$r = Invoke-Relay -Method POST -Path '/v1/report' -Body (New-Report 'feature' ('bad kind ' + $runId) 'x')
Assert-That 'kind "feature" -> 400' ($r.Status -eq 400) $r.Text
$r = Invoke-Relay -Method POST -Path '/v1/report' -Body (New-Report 'bug' ('no desc ' + $runId) '   ')
Assert-That 'blank description -> 400' ($r.Status -eq 400) $r.Text
$r = Invoke-Relay -Method POST -Path '/v1/report' -Body '{"kind": "bug", "title": '
Assert-That 'malformed JSON -> 400 with an error field' (($r.Status -eq 400) -and $r.Json.error) $r.Text
$r = Invoke-Relay -Method POST -Path '/v1/report' -Body '[]'
Assert-That 'JSON array instead of object -> 400' ($r.Status -eq 400) $r.Text

# ---------------------------------------------------------------------------------------
Write-Host ''
Write-Host '[5] profile over its 100 KB cap + log: log dropped first, profile head kept, description whole'
$descSentinel = 'DESC-END-SENTINEL'
$desc5 = ('d' * (4000 - $descSentinel.Length)) + $descSentinel
$xmlLine = '  <Element Type="Text" Anchor="TopLeft" X="0" Y="0" W="100" H="20" />' + "`n"
$profile5 = 'PROFILE-HEAD-SENTINEL' + "`n" + ($xmlLine * 2200) + 'PROFILE-TAIL-SENTINEL'
$logLine = '[Info   :UIALog] HudSystem: rebuilt 42 elements in 3.1 ms' + "`n"
$log5 = 'LOG-HEAD-SENTINEL' + "`n" + ($logLine * 700) + 'LOG-TAIL-SENTINEL'
$rep5 = New-Report 'bug' ('oversize ' + $runId) $desc5
$rep5.profileXml = $profile5
$rep5.logExcerpt = $log5
Write-Host ('      (sending profile ' + $profile5.Length + ' chars, log ' + $log5.Length + ' chars)')
$r = Invoke-Relay -Method POST -Path '/v1/report' -Body $rep5
Assert-That 'oversize report still answers 201' ($r.Status -eq 201) $r.Text
$b = [string]$r.Json.issue.body
Assert-That 'issue body is within the 60,000-char cap' ($b.Length -le 60000) ('length=' + $b.Length)
Assert-That 'description is complete (never truncated)' ($b.Contains('> ' + $desc5 + "`n"))
Assert-That 'truncation note present' ($b.Contains($TRUNC))
Assert-That 'profile HEAD kept' ($b.Contains('PROFILE-HEAD-SENTINEL'))
Assert-That 'profile TAIL cut' (-not $b.Contains('PROFILE-TAIL-SENTINEL'))
Assert-That 'log block present but omitted for size' ($b.Contains('Mod log excerpt (player opted in)') -and $b.Contains('_Omitted:'))
Assert-That 'no log content left' ((-not $b.Contains('LOG-HEAD-SENTINEL')) -and (-not $b.Contains('LOG-TAIL-SENTINEL')))

# ---------------------------------------------------------------------------------------
Write-Host ''
Write-Host '[6] total over 60,000 with both attachments under their caps: only the log is trimmed (tail kept)'
$profile6 = 'PROFILE-HEAD-SENTINEL' + "`n" + ($xmlLine * 560) + 'PROFILE-TAIL-SENTINEL'
$log6 = 'LOG-HEAD-SENTINEL' + "`n" + ($logLine * 500) + 'LOG-TAIL-SENTINEL'
$rep6 = New-Report 'bug' ('trim order ' + $runId) $desc5
$rep6.profileXml = $profile6
$rep6.logExcerpt = $log6
Write-Host ('      (sending profile ' + $profile6.Length + ' chars, log ' + $log6.Length + ' chars)')
$r = Invoke-Relay -Method POST -Path '/v1/report' -Body $rep6
Assert-That 'answers 201' ($r.Status -eq 201) $r.Text
$b = [string]$r.Json.issue.body
Assert-That 'issue body is within the 60,000-char cap' ($b.Length -le 60000) ('length=' + $b.Length)
Assert-That 'description is complete' ($b.Contains('> ' + $desc5 + "`n"))
Assert-That 'profile fully intact (head and tail)' ($b.Contains('PROFILE-HEAD-SENTINEL') -and $b.Contains('PROFILE-TAIL-SENTINEL'))
Assert-That 'log keeps its most recent lines (tail)' ($b.Contains('LOG-TAIL-SENTINEL'))
Assert-That 'log loses its oldest lines (head)' (-not $b.Contains('LOG-HEAD-SENTINEL'))
Assert-That 'exactly one truncation note (on the log)' ((Get-Count $b $TRUNC) -eq 1) ('notes=' + (Get-Count $b $TRUNC))

# ---------------------------------------------------------------------------------------
Write-Host ''
Write-Host '[7] dedupe: identical title + description within the hour'
$dupTitle = 'Duplicate check ' + $runId
$dupDesc = 'Same words, sent twice.'
$r = Invoke-Relay -Method POST -Path '/v1/report' -Body (New-Report 'bug' $dupTitle $dupDesc)
Assert-That 'first copy -> 201' ($r.Status -eq 201) $r.Text
$r = Invoke-Relay -Method POST -Path '/v1/report' -Body (New-Report 'bug' $dupTitle $dupDesc)
Assert-That 'second copy from another IP -> 429' ($r.Status -eq 429) $r.Text
Assert-That '  ... flagged duplicate: true' ($r.Json.duplicate -eq $true) $r.Text

# ---------------------------------------------------------------------------------------
Write-Host ''
$limit = [int](Invoke-Relay -Method GET -Path '/v1/ping').Json.reportsPerHour
Write-Host ('[8] rate limit: ' + $limit + ' reports per hour per IP (from /v1/ping)')
Assert-That 'ping reports the limit in force' ($limit -ge 1) ('reportsPerHour=' + $limit)
$ip8 = New-TestIp
for ($i = 1; $i -le $limit; $i++) {
    $r = Invoke-Relay -Method POST -Path '/v1/report' -Ip $ip8 -Body (New-Report 'bug' ('rate ' + $runId + ' #' + $i) ('report number ' + $i))
    Assert-That ('report ' + $i + ' of ' + $limit + ' from one IP -> 201') ($r.Status -eq 201) $r.Text
}
$over = $limit + 1
$r = Invoke-Relay -Method POST -Path '/v1/report' -Ip $ip8 -Body (New-Report 'bug' ('rate ' + $runId + ' #' + $over) ('report number ' + $over))
Assert-That ('report ' + $over + ' from the same IP -> 429') ($r.Status -eq 429) $r.Text
Assert-That '  ... with a Retry-After header' ([int]$r.RetryAfter -gt 0) ('Retry-After=' + $r.RetryAfter)
Assert-That '  ... and not flagged as a duplicate' ($r.Json.duplicate -ne $true)
$r = Invoke-Relay -Method POST -Path '/v1/report' -Body (New-Report 'bug' ('rate ' + $runId + ' other ip') 'a different sender')
Assert-That 'a different IP is unaffected -> 201' ($r.Status -eq 201) $r.Text

# ---------------------------------------------------------------------------------------
Write-Host ''
Write-Host '[9] status endpoint and routing'
$r = Invoke-Relay -Method GET -Path '/v1/status/abc'
Assert-That 'GET /v1/status/abc -> 404' ($r.Status -eq 404) $r.Text
$r = Invoke-Relay -Method GET -Path '/v1/status/0'
Assert-That 'GET /v1/status/0 -> 404' ($r.Status -eq 404) $r.Text
$r = Invoke-Relay -Method GET -Path '/v1/status/104'
Assert-That 'GET /v1/status/104 in DRY_RUN -> 503 (never calls GitHub)' ($r.Status -eq 503) $r.Text
$r = Invoke-Relay -Method GET -Path '/v1/nope'
Assert-That 'unknown route -> 404' ($r.Status -eq 404)

# ---------------------------------------------------------------------------------------
if ($ClientKey) {
    Write-Host ''
    Write-Host '[10] X-UIA-Client gate'
    $r = Invoke-Relay -Method POST -Path '/v1/report' -NoClientKey -Body (New-Report 'bug' ('no key ' + $runId) 'x')
    Assert-That 'report without X-UIA-Client -> 403' ($r.Status -eq 403) $r.Text
} else {
    Write-Host ''
    Write-Host '[10] X-UIA-Client gate: skipped (pass -ClientKey when the relay has UIA_RELAY_CLIENT_KEY set)'
}

# ---------------------------------------------------------------------------------------
Write-Host ''
Write-Host '[11] browser landing page (Accept: text/html) - Jackson''s rule'
$browser = 'text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8'
$r = Invoke-Relay -Method GET -Path '/' -Accept $browser
Assert-That 'browser GET / -> 200' ($r.Status -eq 200) ('' + $r.Status)
Assert-That '  ... served as text/html' ($r.ContentType -like 'text/html*') $r.ContentType
Assert-That '  ... says where they landed (UI Ascended, API)' (($r.Text -like '*Stationeers UI Ascended*') -and ($r.Text -like '*<strong>API</strong>*'))
Assert-That '  ... carries a no-script Content-Security-Policy' (($r.Csp -like "*default-src 'none'*") -and ($r.Text -notlike '*<script*'))
Assert-That '  ... Vary: Accept' ($r.Vary -like '*Accept*') $r.Vary
$r = Invoke-Relay -Method GET -Path '/v1/ping' -Accept $browser
Assert-That 'browser GET /v1/ping -> 200 HTML, not JSON' (($r.Status -eq 200) -and ($r.ContentType -like 'text/html*') -and ($null -eq $r.Json)) $r.ContentType
$r = Invoke-Relay -Method GET -Path '/v1/report' -Accept $browser
Assert-That 'browser GET /v1/report -> 200 HTML naming the endpoint' (($r.Status -eq 200) -and ($r.Text -like '*<code>/v1/report</code>*'))
$r = Invoke-Relay -Method GET -Path '/nothing-here' -Accept $browser
Assert-That 'browser GET unknown path -> 404, still HTML' (($r.Status -eq 404) -and ($r.ContentType -like 'text/html*')) ('' + $r.Status + ' ' + $r.ContentType)
$r = Invoke-Relay -Method GET -Path '/%3Cscript%3Ealert(1)%3C/script%3E' -Accept $browser
Assert-That 'echoed path is HTML-escaped (no injected tag)' (($r.Text -notlike '*<script*') -and ($r.Text -like '*&lt;script&gt;*'))
$r = Invoke-Relay -Method GET -Path '/v1/ping' -Accept 'application/json'
Assert-That 'Accept: application/json (the mod) -> JSON' (($r.Status -eq 200) -and ($r.Json.ok -eq $true)) $r.ContentType
$r = Invoke-Relay -Method GET -Path '/v1/ping' -Accept '*/*'
Assert-That 'Accept: */* (curl) -> JSON' ($r.Json.ok -eq $true) $r.ContentType
$r = Invoke-Relay -Method GET -Path '/v1/ping' -Accept 'application/json, text/html'
Assert-That 'HTML/JSON tie -> JSON' ($r.Json.ok -eq $true) $r.ContentType
$r = Invoke-Relay -Method GET -Path '/v1/ping' -Accept 'text/html;q=0.4, application/json;q=0.9'
Assert-That 'JSON ranked higher -> JSON' ($r.Json.ok -eq $true) $r.ContentType
$r = Invoke-Relay -Method POST -Path '/v1/report' -Accept $browser -Body (New-Report 'bug' ('html accept ' + $runId) 'a POST is never swallowed by the page')
Assert-That 'POST with Accept: text/html still reaches the API -> 201 JSON' (($r.Status -eq 201) -and ($r.Json.dryRun -eq $true)) ('' + $r.Status)
$r = Invoke-Relay -Method HEAD -Path '/' -Accept $browser
Assert-That 'browser HEAD / -> 200, no body' (($r.Status -eq 200) -and (-not $r.Text))

# ---------------------------------------------------------------------------------------
Write-Host ''
$color = 'Green'
if ($script:failed -gt 0) { $color = 'Red' }
Write-Host ('RESULT: ' + $script:passed + ' passed, ' + $script:failed + ' failed') -ForegroundColor $color
if ($script:failed -gt 0) { exit 1 }
exit 0
