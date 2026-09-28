<#
  setup-dell.ps1 - one-shot installer for the UIA feedback relay + Cloudflare tunnel.

  Run in an ADMINISTRATOR PowerShell on the Dell, from the folder that holds FeedbackRelay.exe:

      powershell -ExecutionPolicy Bypass -File C:\Services\UIAFeedbackRelay\setup-dell.ps1

  What it does (every step checks first, so it is safe to re-run):
    1. checks this is an admin window and that ALL relay files are present
    2. checks the relay's port is free
    3. creates the UIAFeedbackRelay Windows service (LocalService account, auto-restart)
    4. starts it in DRY RUN (never contacts GitHub) and pings it
    5. installs cloudflared (winget, or a signature-checked download) and connects the tunnel
       using the token you paste (input is hidden; it is never printed or saved by this script)

  Later, to go live (real GitHub issues), run it again with -GoLive; it asks for the GitHub
  token the same hidden way.

  To change the per-player report limit (default 6 per hour) without a rebuild:
      powershell -ExecutionPolicy Bypass -File C:\Services\UIAFeedbackRelay\setup-dell.ps1 -ReportsPerHour 10
  A limit set this way is kept by later runs of this script until changed again.

  To point a LIVE relay at another repo, keeping its token:
      powershell -ExecutionPolicy Bypass -File C:\Services\UIAFeedbackRelay\setup-dell.ps1 -Repo owner/name

  Keep this file ASCII-only: Windows PowerShell 5.1 misreads non-ASCII characters in scripts.
#>
param(
    [switch]$GoLive,
    [int]$Port = 8080,
    [string]$Repo = 'FlorpyDorpinator/StationeersUIAscended',
    [int]$ReportsPerHour = 0     # 0 = keep the current setting (or the relay's default of 6)
)

$ErrorActionPreference = 'Stop'
$ServiceName = 'UIAFeedbackRelay'
$Dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Exe = Join-Path $Dir 'FeedbackRelay.exe'
$RegPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"

function Step([string]$n, [string]$text) { Write-Host ''; Write-Host "[$n] $text" -ForegroundColor Cyan }
function Ok([string]$text)   { Write-Host "    OK    $text" -ForegroundColor Green }
function Note([string]$text) { Write-Host "    NOTE  $text" -ForegroundColor Yellow }
function Fail([string]$text) {
    Write-Host "    FAIL  $text" -ForegroundColor Red
    Write-Host ''
    Write-Host 'Stopped here. Copy everything above and send it to Claude.' -ForegroundColor Red
    exit 1
}

function Read-Secret([string]$prompt) {
    $sec = Read-Host -Prompt $prompt -AsSecureString
    $b = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
    try { return ([Runtime.InteropServices.Marshal]::PtrToStringBSTR($b)).Trim() }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b) }
}

function Wait-Ping([int]$seconds) {
    for ($i = 0; $i -lt $seconds; $i++) {
        $r = $null
        try { $r = & curl.exe -s -m 2 "http://127.0.0.1:$Port/v1/ping" } catch { }
        if ($r -match '"ok":true') { return $r }
        Start-Sleep -Seconds 1
    }
    return $null
}

function Show-DirectRun([string[]]$envList) {
    Note 'The service did not answer. Running the relay directly for 6 seconds to capture its error...'
    try { Stop-Service $ServiceName -Force -ErrorAction SilentlyContinue } catch { }
    $names = @()
    foreach ($kv in $envList) { $parts = $kv -split '=', 2; Set-Item -Path "Env:$($parts[0])" -Value $parts[1]; $names += $parts[0] }
    $o = Join-Path $env:TEMP 'uia-relay-diag.out.txt'
    $e = Join-Path $env:TEMP 'uia-relay-diag.err.txt'
    $p = Start-Process -FilePath $Exe -WorkingDirectory $Dir -RedirectStandardOutput $o -RedirectStandardError $e -NoNewWindow -PassThru
    Start-Sleep -Seconds 6
    $answered = Wait-Ping 1
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
    foreach ($n in $names) { Remove-Item -Path "Env:$n" -ErrorAction SilentlyContinue }
    Write-Host '    --- relay output ---'
    Get-Content $o, $e -ErrorAction SilentlyContinue | Select-Object -Last 30 | ForEach-Object { Write-Host "      $_" }
    if ($answered) {
        Note 'Run directly (as you) it WORKS, so the service account cannot read the folder or the port is blocked for it.'
    }
}

Write-Host 'UIA feedback relay - Dell setup' -ForegroundColor White
Write-Host "Folder: $Dir"

# ---------------------------------------------------------------- 1. preconditions
Step 1 'Checking this window and the relay files'
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { Fail 'This window is not elevated. Close it, right-click PowerShell, choose "Run as administrator", and run the same command again.' }
Ok 'Administrator window'
if (-not (Test-Path -LiteralPath $Exe)) { Fail "FeedbackRelay.exe is not in $Dir. Copy ALL files from the publish folder into this folder (not the publish folder itself)." }
$dllCount = @(Get-ChildItem -LiteralPath $Dir -Filter *.dll -File).Count
if ($dllCount -lt 100) { Fail "Only $dllCount .dll files here; a complete copy has about 330 files in total. Copy ALL files from the publish folder, then re-run." }
Ok "FeedbackRelay.exe plus $dllCount libraries present"
if ($Dir -match ' ') { Fail "The folder path contains a space ($Dir). Use a path without spaces, e.g. C:\Services\UIAFeedbackRelay." }
if (-not (Get-Command curl.exe -ErrorAction SilentlyContinue)) { Fail 'curl.exe is missing (it ships with Windows 10 1803+ / Windows 11).' }

# ---------------------------------------------------------------- 2. port
Step 2 "Checking port $Port is free for the relay"
$listen = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
if ($listen.Count -gt 0) {
    $ownerName = ''
    try { $ownerName = (Get-Process -Id $listen[0].OwningProcess).ProcessName } catch { }
    if ($ownerName -eq 'FeedbackRelay') { Ok 'The relay itself already holds the port (re-run)' }
    else { Fail "Port $Port is already used by '$ownerName'. Re-run with a different port, e.g.  -Port 8090 , and tell the tunnel owner topoint the tunnel at 127.0.0.1:8090." }
} else { Ok "Port $Port is free" }

# ---------------------------------------------------------------- 3. service
Step 3 'Creating the Windows service'
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Ok 'Service already exists; making sure it points at this folder'
    $out = & sc.exe config $ServiceName binPath= $Exe start= delayed-auto obj= 'NT AUTHORITY\LocalService'
    if ($LASTEXITCODE -ne 0) { Fail ("sc.exe config failed ($LASTEXITCODE): " + ($out -join ' ')) }
} else {
    $out = & sc.exe create $ServiceName binPath= $Exe start= delayed-auto obj= 'NT AUTHORITY\LocalService' DisplayName= 'UIA Feedback Relay'
    if ($LASTEXITCODE -ne 0) { Fail ("sc.exe create failed ($LASTEXITCODE): " + ($out -join ' ')) }
    Ok 'Service created (runs as the low-privilege LocalService account)'
}
$null = & sc.exe description $ServiceName 'Stationeers UI Ascended in-game feedback to GitHub issues (loopback only, fronted by cloudflared)'
$null = & sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/60000
Ok 'Auto-restart on crash configured'
$out = & icacls $Dir /grant 'NT AUTHORITY\LocalService:(OI)(CI)RX' /Q
if ($LASTEXITCODE -ne 0) { Fail ("icacls failed ($LASTEXITCODE): " + ($out -join ' ')) }
Ok 'LocalService can read the folder'

# ---------------------------------------------------------------- 4. settings + start
Step 4 'Relay settings and start'
$existing = @()
try { $existing = @((Get-ItemProperty -Path $RegPath -Name Environment -ErrorAction Stop).Environment) } catch { }
$alreadyLive = @($existing | Where-Object { $_ -like 'UIA_FEEDBACK_GITHUB_TOKEN=*' }).Count -gt 0

if ($GoLive) {
    Write-Host ''
    Write-Host '    Paste the GitHub fine-grained token (Issues: Read and write on the one repo).' -ForegroundColor White
    Write-Host '    Input is hidden. Right-click pastes in this window.' -ForegroundColor White
    $gh = Read-Secret '    GitHub token'
    if (-not $gh) { Fail 'No token entered.' }
    if ($gh -notmatch '^(github_pat_|ghp_)') { Note 'That does not look like a GitHub token (expected github_pat_...). Continuing anyway.' }
    $envList = @("UIA_FEEDBACK_GITHUB_TOKEN=$gh", "UIA_FEEDBACK_REPO=$Repo", "UIA_RELAY_PORT=$Port")
    $gh = $null
    $expectDry = $false
} elseif ($alreadyLive) {
    Note 'The relay is already LIVE (has a GitHub token); keeping those settings. Re-run with -GoLive to replace the token.'
    $envList = @($existing | Where-Object { $_ -notlike 'UIA_RELAY_PORT=*' }) + "UIA_RELAY_PORT=$Port"
    if ($PSBoundParameters.ContainsKey('Repo')) {
        # An explicit -Repo re-points a live relay without asking for the token again.
        $envList = @($envList | Where-Object { $_ -notlike 'UIA_FEEDBACK_REPO=*' }) + "UIA_FEEDBACK_REPO=$Repo"
    }
    $expectDry = $false
} else {
    $envList = @('UIA_RELAY_DRY_RUN=1', "UIA_RELAY_PORT=$Port")
    $expectDry = $true
}
if ($ReportsPerHour -lt 0 -or $ReportsPerHour -gt 100) { Fail '-ReportsPerHour must be from 1 to 100.' }
$keptLimit = @($existing | Where-Object { $_ -like 'UIA_RELAY_REPORTS_PER_HOUR=*' })
$envList = @($envList | Where-Object { $_ -notlike 'UIA_RELAY_REPORTS_PER_HOUR=*' })
if ($ReportsPerHour -gt 0) { $envList += "UIA_RELAY_REPORTS_PER_HOUR=$ReportsPerHour" }
elseif ($keptLimit.Count -gt 0) { $envList += $keptLimit[0] }
New-ItemProperty -Path $RegPath -Name Environment -PropertyType MultiString -Value $envList -Force | Out-Null
# Report the repo the relay will really use: a live re-run without -Repo keeps the stored one.
$liveRepo = @($envList | Where-Object { $_ -like 'UIA_FEEDBACK_REPO=*' } | ForEach-Object { $_.Substring(18) }) | Select-Object -First 1
if ($expectDry) { Ok 'Mode: DRY RUN (never contacts GitHub)' } else { Ok "Mode: LIVE, filing into $liveRepo" }

try {
    if ((Get-Service $ServiceName).Status -eq 'Running') { Restart-Service $ServiceName -Force } else { Start-Service $ServiceName }
} catch { Note ("Service start reported: " + $_.Exception.Message) }
$ping = Wait-Ping 20
if (-not $ping) { Show-DirectRun $envList; Fail 'The relay did not answer on 127.0.0.1.' }
Ok "Relay answers: $ping"
if ($expectDry -and $ping -notmatch '"dryRun":true') { Note 'Expected dry run but the relay says it is live - check the settings.' }
if ((-not $expectDry) -and $ping -notmatch '"dryRun":false') { Note 'Expected live but the relay still reports dry run.' }

# ---------------------------------------------------------------- 5. tunnel
Step 5 'Cloudflare tunnel'
$cfSvc = Get-Service -Name 'cloudflared' -ErrorAction SilentlyContinue
if ($cfSvc) {
    Ok "Tunnel service already installed (status: $($cfSvc.Status))"
    if ($cfSvc.Status -ne 'Running') { try { Start-Service cloudflared; Ok 'Tunnel service started' } catch { Note ('Could not start it: ' + $_.Exception.Message) } }
} else {
    function Find-Cloudflared {
        $cands = @("${env:ProgramFiles(x86)}\cloudflared\cloudflared.exe", "$env:ProgramFiles\cloudflared\cloudflared.exe")
        foreach ($c in $cands) { if ($c -and (Test-Path -LiteralPath $c)) { return $c } }
        $cmd = Get-Command cloudflared.exe -ErrorAction SilentlyContinue
        if ($cmd) { return $cmd.Source }
        return $null
    }
    $cf = Find-Cloudflared
    if (-not $cf) {
        Note 'Installing cloudflared with winget...'
        try { & winget install --id Cloudflare.cloudflared -e --silent --accept-source-agreements --accept-package-agreements | Out-Null } catch { Note 'winget is not available here.' }
        $cf = Find-Cloudflared
    }
    if (-not $cf) {
        Note 'Downloading cloudflared directly from Cloudflare''s GitHub releases...'
        $target = Join-Path $env:ProgramFiles 'cloudflared'
        New-Item -ItemType Directory -Force -Path $target | Out-Null
        $cf = Join-Path $target 'cloudflared.exe'
        & curl.exe -sSL -o $cf 'https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe'
        $sig = Get-AuthenticodeSignature -FilePath $cf
        if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'Cloudflare, Inc\.') {
            Remove-Item -LiteralPath $cf -Force -ErrorAction SilentlyContinue
            Fail 'The downloaded cloudflared.exe is not validly signed by Cloudflare; deleted it.'
        }
    }
    $ErrorActionPreference = 'Continue'
    $ver = (& $cf --version 2>&1 | ForEach-Object { "$_" }) -join ' '
    $ErrorActionPreference = 'Stop'
    Ok "cloudflared: $cf ($ver)"

    Write-Host ''
    Write-Host '    Paste the tunnel token (the long text starting with eyJ).' -ForegroundColor White
    Write-Host '    Pasting his whole "cloudflared service install ..." line also works.' -ForegroundColor White
    Write-Host '    Input is hidden. Right-click pastes in this window.' -ForegroundColor White
    $raw = Read-Secret '    Tunnel token'
    $m = [regex]::Match($raw, 'eyJ[A-Za-z0-9+/=_-]{20,}')
    $raw = $null
    if (-not $m.Success) { Fail 'That did not contain a tunnel token (it should start with eyJ).' }
    $token = $m.Value
    try {
        $p = $token.Replace('-', '+').Replace('_', '/'); while ($p.Length % 4) { $p += '=' }
        $obj = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($p)) | ConvertFrom-Json
        if (-not ($obj.a -and $obj.t -and $obj.s)) { throw 'missing fields' }
        Ok "Token is a valid tunnel token (tunnel id $($obj.t))"
        $obj = $null
    } catch { $token = $null; Fail 'That text is not a valid tunnel token - copy it again from the file.' }

    # cloudflared logs its normal progress to stderr; under 'Stop', PowerShell 5.1 turns the first
    # redirected stderr line into a terminating error, so relax it for this one call.
    $ErrorActionPreference = 'Continue'
    $out = & $cf service install $token 2>&1
    $code = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    $token = $null
    if ($code -ne 0) { Fail ("cloudflared service install failed ($code): " + (($out | ForEach-Object { "$_" }) -join ' ')) }
    $running = $false
    for ($i = 0; $i -lt 20; $i++) {
        $s = Get-Service -Name 'cloudflared' -ErrorAction SilentlyContinue
        if ($s -and $s.Status -eq 'Running') { $running = $true; break }
        Start-Sleep -Seconds 1
    }
    if (-not $running) { Fail 'The cloudflared service was installed but is not running.' }
    Ok 'Tunnel service installed and running (starts automatically after reboots)'
}

# ---------------------------------------------------------------- done
Write-Host ''
Write-Host 'ALL DONE' -ForegroundColor Green
Write-Host "  Relay:  $((Get-Service $ServiceName).Status), $(if ($expectDry) { 'DRY RUN' } else { 'LIVE' }), http://127.0.0.1:$Port"
Write-Host "  Tunnel: $((Get-Service cloudflared).Status)"
Write-Host ''
Write-Host 'Next: tell Claude it finished. The Cloudflare dashboard should now show this machine as a'
Write-Host "connector; his public hostname must point at http://127.0.0.1:$Port ."
if ($expectDry) { Write-Host 'To go live later (real GitHub issues), run this script again with -GoLive.' }
