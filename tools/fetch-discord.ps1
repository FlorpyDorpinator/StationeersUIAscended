#requires -Version 5.1
<#
.SYNOPSIS
  Mirror the UIA Discord server - every channel and thread the bot can read, plus
  attachments - into Discord\ at the repo root, for the /triage command.

.DESCRIPTION
  READ-ONLY by construction: a bot token and GET requests against the Discord REST API (v10)
  only. The bot never posts, reacts, or edits anything.

  Incremental: Discord\state.json remembers the newest message id per channel/thread, so each
  run downloads only what is new, appends it to the per-channel transcripts, and writes the
  same new messages to one Discord\inbox\<timestamp>.md file. The inbox is what /triage
  consumes. The FIRST run backfills the whole server history.

  Discord\ is git-ignored: it holds other people's messages and logs and must never be
  committed or shipped.

  Layout (paths relative to Discord\, which is also how the transcripts reference files):
    state.json                                    server + newest-seen message id per channel/thread
    channels\<category>\<channel>.md                full transcript, oldest first
    channels\<category>\<channel>\<thread> (<id>).md  one file per thread / forum post
    attachments\<channel id>\<message id>-<file>    logs, screenshots (size-capped)
    raw\<channel id>\<first id>-<last id>.json      verbatim API pages (lossless backup)
    inbox\<yyyy-MM-dd_HHmmss>.md                   new messages from one run (for /triage)
    triage\                                        /triage reports + the running issue list

  One-time setup: see "Changes Reports\2026-09-25 - Discord server mirror + triage command.md".

.PARAMETER SetToken
  Prompt for the bot token (hidden input), store it encrypted for this Windows user (DPAPI) at
  %USERPROFILE%\.uia-discord\bot-token.dat, check it against Discord, and exit. The
  UIA_DISCORD_BOT_TOKEN environment variable, if set, overrides the stored token.

.PARAMETER Check
  Verify the stored token, the bot's servers and the Message Content intent, then exit.

.PARAMETER GuildId
  The server to mirror (default: the UIA server).

.PARAMETER MaxAttachmentMB
  Attachments larger than this are listed but not downloaded (default 25).

.PARAMETER NoAttachments
  List attachments without downloading any.
#>
[CmdletBinding()]
param(
    [switch]$SetToken,
    [switch]$Check,
    [string]$GuildId = '1455816765345894507',
    [string]$OutDir,
    [int]$MaxAttachmentMB = 25,
    [switch]$NoAttachments
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # the 5.1 progress bar makes every web call crawl
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$ApiBase   = 'https://discord.com/api/v10'
$UserAgent = 'DiscordBot (https://github.com/FlorpyDorpinator/StationeersUIAscended, 1.0)'
$TokenFile = [IO.Path]::Combine($env:USERPROFILE, '.uia-discord', 'bot-token.dat')
$Utf8      = New-Object System.Text.UTF8Encoding($false)
$NL        = "`n"

# Channel types (Discord API): messages live in text/voice/announcement/stage channels and in
# threads; forum/media channels hold only threads.
$MessageChannelTypes = @(0, 2, 5, 13)
$ThreadParentTypes   = @(0, 5, 15, 16)
$ThreadTypes         = @(10, 11, 12)
# Message types rendered as ordinary chat (default, reply, slash command, thread starter, context menu).
$ChatMessageTypes    = @(0, 19, 20, 21, 23)
# Application flags GATEWAY_MESSAGE_CONTENT (1<<18) | GATEWAY_MESSAGE_CONTENT_LIMITED (1<<19).
$MessageContentFlags = 0xC0000

# ---------------------------------------------------------------- token

function Get-BotToken {
    if ($env:UIA_DISCORD_BOT_TOKEN) { return ($env:UIA_DISCORD_BOT_TOKEN.Trim() -replace '^Bot\s+', '') }
    if (-not (Test-Path -LiteralPath $TokenFile)) {
        throw "No bot token stored. In a terminal, run:  powershell -ExecutionPolicy Bypass -File tools\fetch-discord.ps1 -SetToken"
    }
    $ss = ([IO.File]::ReadAllText($TokenFile, $Utf8)).Trim() | ConvertTo-SecureString
    $b = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($ss)
    try { ([Runtime.InteropServices.Marshal]::PtrToStringBSTR($b)).Trim() -replace '^Bot\s+', '' }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b) }
}

function Set-BotToken {
    $ss = Read-Host -AsSecureString 'Paste the Discord bot token (input is hidden), then press Enter'
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($TokenFile)) | Out-Null
    [IO.File]::WriteAllText($TokenFile, (ConvertFrom-SecureString $ss), $Utf8)
    Write-Host "Saved, encrypted for Windows user $env:USERNAME, to $TokenFile"
    Test-Bot
}

# ---------------------------------------------------------------- HTTP

function Get-Header($headers, [string]$name) {
    if (-not $headers) { return $null }
    foreach ($k in $headers.Keys) { if ($k -ieq $name) { return [string]$headers[$k] } }
    return $null
}

# GET one API path. Returns Status/Text/Json; non-2xx statuses come back as a Status (callers
# decide whether a 403 is fatal) unless -Required, which throws a readable error instead.
# Handles 429 and the per-route X-RateLimit headers so a full-server backfill never trips a ban.
function Invoke-DiscordApi([string]$Path, [switch]$Required) {
    for ($attempt = 1; $attempt -le 8; $attempt++) {
        try {
            $r = Invoke-WebRequest -Uri ($ApiBase + $Path) -Headers $script:Headers -UserAgent $UserAgent -UseBasicParsing -TimeoutSec 60
            # Discord sends no charset, so 5.1 would decode .Content as Latin-1; decode the bytes.
            $text = [Text.Encoding]::UTF8.GetString($r.RawContentStream.ToArray())
            if ((Get-Header $r.Headers 'X-RateLimit-Remaining') -eq '0') {
                $resetAfter = Get-Header $r.Headers 'X-RateLimit-Reset-After'
                if ($resetAfter) { Start-Sleep -Milliseconds ([int]([double]$resetAfter * 1000) + 50) }
            }
            return [pscustomobject]@{ Status = 200; Text = $text; Json = ($text | ConvertFrom-Json) }
        } catch [System.Net.WebException] {
            $resp = $_.Exception.Response
            if (-not $resp) { Start-Sleep -Seconds 3; continue }          # timeout / connection drop
            $code = [int]$resp.StatusCode
            if ($code -eq 429) {
                $wait = Get-Header $resp.Headers 'Retry-After'
                $sec = if ($wait) { [double]$wait } else { 5 }
                Write-Host ("  rate limited - waiting {0:N1}s" -f $sec)
                Start-Sleep -Milliseconds ([int]($sec * 1000) + 100)
                continue
            }
            if ($code -ge 500) { Start-Sleep -Seconds 3; continue }
            if ($Required) {
                $why = switch ($code) {
                    401 { 'the bot token was rejected - run -SetToken again with a fresh token from the Bot page' }
                    403 { 'the bot lacks access - check it was invited with View Channels + Read Message History' }
                    404 { 'not found - is the bot in that server, and is the server id right?' }
                    default { "HTTP $code" }
                }
                throw "Discord API $Path failed: $why"
            }
            return [pscustomobject]@{ Status = $code; Text = $null; Json = $null }
        }
    }
    throw "Discord API $Path kept failing after 8 attempts"
}

function Test-Bot {
    $script:Headers = @{ Authorization = 'Bot ' + (Get-BotToken) }
    $me = Invoke-DiscordApi '/users/@me'
    if ($me.Status -ne 200) { throw "Discord rejected the token (HTTP $($me.Status)). Copy a fresh one from the Bot page and run -SetToken again." }
    Write-Host "Token OK - bot account: $($me.Json.username)"
    $app = Invoke-DiscordApi '/applications/@me'
    if ($app.Status -eq 200) {
        if (([int64]$app.Json.flags -band $MessageContentFlags) -eq 0) {
            Write-Warning 'Message Content Intent is OFF - every message will come back EMPTY. Turn it on: Developer Portal -> your app -> Bot -> Privileged Gateway Intents -> Message Content Intent.'
        } else { Write-Host 'Message Content Intent: on' }
    }
    $gs = @((Invoke-DiscordApi '/users/@me/guilds' -Required).Json | Where-Object { $null -ne $_ })
    if ($gs.Count -eq 0) { Write-Warning 'The bot is not in any server yet - open the invite link from the setup steps.' }
    foreach ($g in $gs) {
        $mark = if ($g.id -eq $GuildId) { '  <- mirrored' } else { '' }
        Write-Host "In server: $($g.name) ($($g.id))$mark"
    }
    if ($gs.Count -gt 0 -and -not ($gs | Where-Object { $_.id -eq $GuildId })) {
        Write-Warning "The bot is not in server $GuildId (the one this script mirrors)."
    }
}

# ---------------------------------------------------------------- files & state

function ConvertTo-SafeName([string]$s) {
    if (-not $s) { return '_' }
    $bad = [IO.Path]::GetInvalidFileNameChars()
    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $s.ToCharArray()) {
        if ($bad -contains $ch) { [void]$sb.Append('_') }
        elseif ($ch -eq '[') { [void]$sb.Append('(') }          # brackets are wildcards to many cmdlets
        elseif ($ch -eq ']') { [void]$sb.Append(')') }
        else { [void]$sb.Append($ch) }
    }
    $r = $sb.ToString().Trim().TrimEnd('.')
    if ($r.Length -gt 80) {
        $cut = 80; if ([char]::IsHighSurrogate($r[79])) { $cut = 79 }
        $r = $r.Substring(0, $cut).TrimEnd('.', ' ')
    }
    if ($r -match '^(CON|PRN|AUX|NUL|COM\d|LPT\d)$') { $r = "_$r" }
    if (-not $r) { $r = '_' }
    return $r
}

function Get-FullPath([string]$rel) { [IO.Path]::Combine($script:Root, ($rel -replace '/', '\')) }

function Add-Text([string]$rel, [string]$text) {
    $full = Get-FullPath $rel
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($full)) | Out-Null
    [IO.File]::AppendAllText($full, $text, $Utf8)
}

function Read-State {
    $st = @{ guildId = $null; guildName = $null; lastRun = $null; channels = @{} }
    $p = Get-FullPath 'state.json'
    if (Test-Path -LiteralPath $p) {
        $o = [IO.File]::ReadAllText($p, $Utf8) | ConvertFrom-Json
        $st.guildId = $o.guildId; $st.guildName = $o.guildName; $st.lastRun = $o.lastRun
        if ($o.channels) {
            foreach ($pr in $o.channels.PSObject.Properties) {
                $st.channels[$pr.Name] = @{ name = $pr.Value.name; file = $pr.Value.file; lastId = $pr.Value.lastId }
            }
        }
    }
    return $st
}

function Write-State($st) {
    $p = Get-FullPath 'state.json'
    $tmp = "$p.tmp"
    [IO.File]::WriteAllText($tmp, ($st | ConvertTo-Json -Depth 6), $Utf8)
    # [NullString]: PowerShell would pass a plain $null to .NET as "" (an illegal backup path).
    if (Test-Path -LiteralPath $p) { [IO.File]::Replace($tmp, $p, [NullString]::Value) } else { [IO.File]::Move($tmp, $p) }
}

function Save-Attachment($att, [string]$channelId, [string]$messageId) {
    if ($NoAttachments) { return 'not downloaded (-NoAttachments)' }
    if ([int64]$att.size -gt [int64]$MaxAttachmentMB * 1MB) { return "not downloaded (over $MaxAttachmentMB MB)" }
    $rel = "attachments/$channelId/$messageId-" + (ConvertTo-SafeName $att.filename)
    $full = Get-FullPath $rel
    if (Test-Path -LiteralPath $full) { return $rel }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($full)) | Out-Null
    $wc = New-Object System.Net.WebClient
    try {
        $wc.Headers.Add('User-Agent', $UserAgent)
        $wc.DownloadFile([string]$att.url, $full)   # signed CDN url - no token sent
        return $rel
    } catch {
        Write-Warning "  attachment $($att.filename) failed: $($_.Exception.Message)"
        return "not downloaded ($($_.Exception.Message))"
    } finally { $wc.Dispose() }
}

# ---------------------------------------------------------------- rendering

function Get-AuthorName($a) {
    if (-not $a) { return '(unknown)' }
    $n = if ($a.global_name) { "$($a.global_name) (@$($a.username))" } else { "@$($a.username)" }
    if ($a.bot) { $n += ' [bot]' }
    return $n
}

function Resolve-Content([string]$text, $m) {
    if (-not $text) { return '' }
    foreach ($u in @($m.mentions)) {
        if (-not $u) { continue }
        $n = if ($u.global_name) { $u.global_name } else { $u.username }
        $text = $text.Replace("<@$($u.id)>", "@$n").Replace("<@!$($u.id)>", "@$n")
    }
    foreach ($mm in [regex]::Matches($text, '<#(\d+)>')) {
        $cid = $mm.Groups[1].Value
        if ($script:ChanNames.ContainsKey($cid)) { $text = $text.Replace($mm.Value, '#' + $script:ChanNames[$cid]) }
    }
    return [regex]::Replace($text, '<a?:(\w+):\d+>', ':$1:')
}

function Get-Snippet([string]$s, [int]$max) {
    $s = ($s -replace '\s+', ' ').Trim()
    if ($s.Length -gt $max) { $s = $s.Substring(0, $max) + '...' }
    return $s
}

function Format-Message($m, [string]$channelId, [hashtable]$attPaths) {
    # Joins, boosts, pins etc. carry no text - nothing to triage.
    if ($ChatMessageTypes -notcontains [int]$m.type -and -not $m.content -and -not @($m.attachments | Where-Object { $_ }).Count) { return '' }
    $when = ([DateTimeOffset]::Parse($m.timestamp, [Globalization.CultureInfo]::InvariantCulture)).ToLocalTime().ToString('yyyy-MM-dd HH:mm')
    $edited = if ($m.edited_timestamp) { ' (edited)' } else { '' }
    $link = "https://discord.com/channels/$($script:Guild)/$channelId/$($m.id)"
    $out = New-Object System.Collections.Generic.List[string]
    $out.Add("**$(Get-AuthorName $m.author)** - $when$edited - [link]($link)")
    if ($ChatMessageTypes -notcontains [int]$m.type) { $out.Add("(system message, type $($m.type))") }

    $ref = $m.referenced_message
    if ($ref) {
        $out.Add("> replying to $(Get-AuthorName $ref.author): $(Get-Snippet (Resolve-Content $ref.content $ref) 160)")
    }
    $body = Resolve-Content $m.content $m
    if ($body) { foreach ($line in ($body -split "`r?`n")) { $out.Add($line) } }

    foreach ($snap in @($m.message_snapshots)) {
        if ($snap -and $snap.message -and $snap.message.content) {
            $out.Add("> forwarded: $(Get-Snippet $snap.message.content 400)")
        }
    }
    foreach ($att in @($m.attachments)) {
        if (-not $att) { continue }
        $kb = [math]::Ceiling([double]$att.size / 1KB)
        $local = $attPaths[[string]$att.id]
        if ($local -and -not $local.StartsWith('not downloaded')) { $out.Add("- attachment: $($att.filename) ($kb KB) -> $local") }
        else { $out.Add("- attachment: $($att.filename) ($kb KB) - $local") }
    }
    foreach ($e in @($m.embeds)) {
        if (-not $e) { continue }
        $parts = @()
        if ($e.title) { $parts += (Get-Snippet $e.title 200) }
        if ($e.url) { $parts += $e.url }
        if ($e.description) { $parts += (Get-Snippet $e.description 300) }
        if ($parts.Count) { $out.Add('- embed: ' + ($parts -join ' | ')) }
    }
    foreach ($s in @($m.sticker_items)) { if ($s) { $out.Add("- sticker: $($s.name)") } }
    $out.Add('')
    return ($out -join $NL) + $NL
}

# ---------------------------------------------------------------- fetch

# Every message after $afterId (oldest first). Raw pages are kept verbatim under raw\.
function Get-NewMessages([string]$channelId, [string]$afterId) {
    $all = New-Object System.Collections.Generic.List[object]
    $cursor = if ($afterId) { $afterId } else { '0' }   # not the channel id: a forum post's first message shares the thread's id
    $status = 200
    while ($true) {
        $r = Invoke-DiscordApi "/channels/$channelId/messages?limit=100&after=$cursor"
        if ($r.Status -ne 200) { $status = $r.Status; break }
        $page = @($r.Json | Where-Object { $null -ne $_ } | Sort-Object { [UInt64]$_.id })
        if ($page.Count -eq 0) { break }
        Add-Text "raw/$channelId/$($page[0].id)-$($page[-1].id).json" $r.Text
        $all.AddRange([object[]]$page)
        $cursor = [string]$page[-1].id
        if ($page.Count -lt 100) { break }
    }
    return [pscustomobject]@{ Status = $status; Messages = $all }
}

function Get-Label($c) {
    if ($ThreadTypes -contains [int]$c.type) {
        $parent = $script:ById[[string]$c.parent_id]
        $pn = if ($parent) { $parent.name } else { '?' }
        return "#$pn > thread: $($c.name)"
    }
    $cat = $script:ById[[string]$c.parent_id]
    if ($cat) { return "#$($c.name) (in $($cat.name))" }
    return "#$($c.name)"
}

# Transcript path for a channel/thread. Remembered in state.json on first sight, so renames
# keep appending to the same file.
function Get-TranscriptRel($c) {
    $known = $script:State.channels[[string]$c.id]
    if ($known -and $known.file) { return $known.file }
    if ($ThreadTypes -contains [int]$c.type) {
        $parent = $script:ById[[string]$c.parent_id]
        $base = if ($parent) { (Get-TranscriptRel $parent) -replace '\.md$', '' } else { 'channels/_orphan-threads' }
        return "$base/$(ConvertTo-SafeName $c.name) ($($c.id)).md"
    }
    $cat = $script:ById[[string]$c.parent_id]
    if ($cat) { return "channels/$(ConvertTo-SafeName $cat.name)/$(ConvertTo-SafeName $c.name).md" }
    return "channels/$(ConvertTo-SafeName $c.name).md"
}

function Invoke-Mirror {
    $script:Headers = @{ Authorization = 'Bot ' + (Get-BotToken) }
    $script:Root = if ($OutDir) { [IO.Path]::GetFullPath($OutDir) } else { [IO.Path]::GetFullPath([IO.Path]::Combine($PSScriptRoot, '..', 'Discord')) }
    [IO.Directory]::CreateDirectory($script:Root) | Out-Null
    $script:State = Read-State
    $script:Guild = $GuildId

    $guild = (Invoke-DiscordApi "/guilds/$GuildId" -Required).Json
    $script:State.guildId = $GuildId
    $script:State.guildName = $guild.name
    Write-Host "Mirroring '$($guild.name)' into $($script:Root)"

    $app = Invoke-DiscordApi '/applications/@me'
    if ($app.Status -eq 200 -and (([int64]$app.Json.flags -band $MessageContentFlags) -eq 0)) {
        throw 'Message Content Intent is OFF - Discord would return every message EMPTY. Turn it on (Developer Portal -> Bot -> Privileged Gateway Intents -> Message Content Intent) and run again.'
    }

    $chs = @((Invoke-DiscordApi "/guilds/$GuildId/channels" -Required).Json | Where-Object { $null -ne $_ })
    $script:ById = @{}; $script:ChanNames = @{}
    foreach ($c in $chs) { $script:ById[[string]$c.id] = $c; $script:ChanNames[[string]$c.id] = $c.name }

    # Threads: every active one guild-wide, plus archived public/private ones per parent
    # (private archived needs Manage Threads - a 403 there is skipped quietly).
    $threads = @{}
    foreach ($t in @((Invoke-DiscordApi "/guilds/$GuildId/threads/active" -Required).Json.threads)) {
        if ($t) { $threads[[string]$t.id] = $t }
    }
    foreach ($c in @($chs | Where-Object { $ThreadParentTypes -contains [int]$_.type })) {
        foreach ($kind in 'public', 'private') {
            $before = $null
            while ($true) {
                $q = "/channels/$($c.id)/threads/archived/$($kind)?limit=100"
                if ($before) { $q += '&before=' + [uri]::EscapeDataString($before) }
                $r = Invoke-DiscordApi $q
                if ($r.Status -ne 200) { break }
                $batch = @($r.Json.threads | Where-Object { $null -ne $_ })
                foreach ($t in $batch) { $threads[[string]$t.id] = $t }
                if (-not $r.Json.has_more -or $batch.Count -eq 0) { break }
                $before = [string]$batch[-1].thread_metadata.archive_timestamp
            }
        }
    }
    foreach ($t in $threads.Values) { $script:ById[[string]$t.id] = $t; $script:ChanNames[[string]$t.id] = $t.name }

    # Source order: channels as they appear in Discord's sidebar, each followed by its threads.
    $catPos = { param($c) $cat = $script:ById[[string]$c.parent_id]; if ($cat) { [int]$cat.position } else { -1 } }
    $sources = New-Object System.Collections.Generic.List[object]
    $parents = @($chs | Where-Object { ($MessageChannelTypes + $ThreadParentTypes) -contains [int]$_.type } |
        Sort-Object @{ Expression = { & $catPos $_ } }, @{ Expression = { [int]$_.position } })
    foreach ($c in $parents) {
        if ($MessageChannelTypes -contains [int]$c.type) { $sources.Add($c) }
        foreach ($t in @($threads.Values | Where-Object { [string]$_.parent_id -eq [string]$c.id } | Sort-Object { [UInt64]$_.id })) { $sources.Add($t) }
    }

    $inboxRel = 'inbox/' + (Get-Date -Format 'yyyy-MM-dd_HHmmss') + '.md'
    $inboxCounts = New-Object System.Collections.Generic.List[string]
    $tz = 'UTC' + (Get-Date).ToString('zzz')
    $newTotal = 0
    $noAccess = New-Object System.Collections.Generic.List[string]

    foreach ($src in $sources) {
        $id = [string]$src.id
        $entry = $script:State.channels[$id]
        $last = if ($entry) { [string]$entry.lastId } else { $null }
        # Cheap skip: Discord already tells us the newest message id per channel/thread.
        if ($last -and $src.last_message_id -and ([UInt64][string]$src.last_message_id -le [UInt64]$last)) { continue }

        $label = Get-Label $src
        $res = Get-NewMessages $id $last
        if ($res.Status -eq 403 -or $res.Status -eq 404) { $noAccess.Add($label) }
        $msgs = $res.Messages
        if ($msgs.Count -eq 0) { continue }
        Write-Host ("  {0,5} new  {1}" -f $msgs.Count, $label)

        $rel = Get-TranscriptRel $src
        $sb = New-Object System.Text.StringBuilder
        foreach ($m in $msgs) {
            $attPaths = @{}
            foreach ($att in @($m.attachments)) {
                if ($att) { $attPaths[[string]$att.id] = Save-Attachment $att $id ([string]$m.id) }
            }
            [void]$sb.Append((Format-Message $m $id $attPaths))
        }
        $block = $sb.ToString()

        if (-not (Test-Path -LiteralPath (Get-FullPath $rel))) {
            Add-Text $rel ("# $label$NL${NL}Server: $($guild.name) | channel id $id | times are local ($tz) | " +
                "written by tools/fetch-discord.ps1 - do not edit, it is appended to on every run.$NL$NL")
        }
        Add-Text $rel $block
        if ($inboxCounts.Count -eq 0) {
            Add-Text $inboxRel ("# Discord inbox - $(Get-Date -Format 'yyyy-MM-dd HH:mm')$NL${NL}New messages since the previous " +
                "fetch, grouped by channel. Times are local ($tz). Paths are relative to the Discord folder.$NL$NL")
        }
        Add-Text $inboxRel ("## $label$NL${NL}Transcript: $rel$NL$NL" + $block)
        $inboxCounts.Add("- $label - $($msgs.Count)")
        $newTotal += $msgs.Count

        $script:State.channels[$id] = @{ name = $label; file = $rel; lastId = [string]$msgs[$msgs.Count - 1].id }
        Write-State $script:State     # after every channel, so an interrupted backfill resumes
    }

    $script:State.lastRun = (Get-Date).ToString('o')
    Write-State $script:State

    if ($inboxCounts.Count -gt 0) {
        $full = Get-FullPath $inboxRel
        $body = [IO.File]::ReadAllText($full, $Utf8)
        $cut = $body.IndexOf("$NL## ")
        $toc = "**$newTotal new messages in $($inboxCounts.Count) channels/threads:**$NL" + ($inboxCounts -join $NL) + $NL
        [IO.File]::WriteAllText($full, $body.Substring(0, $cut + 1) + $toc + $body.Substring($cut), $Utf8)
        Write-Host "$newTotal new messages -> Discord\$($inboxRel -replace '/', '\')"
    } else {
        Write-Host 'No new messages.'
    }
    if ($noAccess.Count -gt 0) {
        Write-Host "No access (give the bot View Channel + Read Message History there to include them):"
        foreach ($n in $noAccess) { Write-Host "  $n" }
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    if ($SetToken) { Set-BotToken }
    elseif ($Check) { Test-Bot }
    else { Invoke-Mirror }
}
