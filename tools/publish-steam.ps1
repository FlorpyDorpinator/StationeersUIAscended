#requires -Version 5.1
<#
.SYNOPSIS
  Publish the staged Stationeers UI Ascended mod to the Steam Workshop via steamcmd.

.DESCRIPTION
  Uploads tools\workshop_update.vdf's contentfolder (the repo "dist\Stationeers UI Ascended"
  folder that package.ps1 stages) as a Workshop item. Run package.ps1 first so dist\ is fresh.

  PLACEHOLDER SAFETY: the mod has no Workshop id yet, so workshop_update.vdf ships with
  publishedfileid "0". Publishing with 0 would CREATE a brand-new Workshop item, so this script
  REFUSES to run while the id is 0 unless you pass -Force (an intentional first-time create).
  After the first create, steamcmd prints the new id - paste it into workshop_update.vdf so every
  later publish updates that item instead of making duplicates.

.PARAMETER SteamUser
  Steam account to log in as. The login is NOT stored in this (public) repo: pass -SteamUser,
  or set the UIA_STEAM_USER environment variable, or put the name alone in tools\steam-user.txt
  (git-ignored). steamcmd will prompt for the password / Steam Guard code on first use and then
  cache it.

.PARAMETER Force
  Publish even though publishedfileid is still 0 - i.e. intentionally CREATE the Workshop item.
#>
[CmdletBinding()]
param(
    [string]$SteamUser = '',
    [string]$SteamCmd  = 'C:\steamcmd\steamcmd.exe',
    [switch]$Force
)
$ErrorActionPreference = 'Stop'

# The Steam login never lives in the repo (it is public): -SteamUser, else $env:UIA_STEAM_USER,
# else tools\steam-user.txt (git-ignored, one line).
if (-not $SteamUser) { $SteamUser = $env:UIA_STEAM_USER }
if (-not $SteamUser) {
    $userFile = Join-Path $PSScriptRoot 'steam-user.txt'
    if (Test-Path -LiteralPath $userFile) { $SteamUser = (Get-Content -LiteralPath $userFile -Raw).Trim() }
}
if (-not $SteamUser) {
    throw "No Steam login. Pass -SteamUser <name>, set UIA_STEAM_USER, or create tools\steam-user.txt (git-ignored)."
}

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Vdf      = Join-Path $PSScriptRoot 'workshop_update.vdf'
$AboutXml = Join-Path $RepoRoot 'Assets\About\About.xml'
if (-not (Test-Path $Vdf))      { throw "Missing $Vdf." }
if (-not (Test-Path $SteamCmd)) {
    throw "steamcmd not found at $SteamCmd. Install SteamCMD (https://developer.valvesoftware.com/wiki/SteamCMD) or pass -SteamCmd <path>."
}

$vdfText = Get-Content -LiteralPath $Vdf -Raw

# Verify the content folder the vdf points at actually exists (i.e. package.ps1 has run).
if ($vdfText -match '"contentfolder"\s+"([^"]+)"') {
    $content = $Matches[1] -replace '\\\\', '\'
    if (-not (Test-Path $content)) {
        throw "contentfolder does not exist: $content `n Run tools\package.ps1 first to stage the mod."
    }
}

# Placeholder guard.
if ($vdfText -match '"publishedfileid"\s+"(\d+)"') {
    $id = $Matches[1]
    if ($id -eq '0' -and -not $Force) {
        Write-Warning "workshop_update.vdf still has publishedfileid 0 - no Workshop item exists for this mod yet."
        Write-Warning "Publishing now would CREATE a new Workshop item. When you are ready:"
        Write-Warning "  * re-run with -Force to create it, then paste the id steamcmd returns into workshop_update.vdf, OR"
        Write-Warning "  * set publishedfileid to your existing Workshop id first."
        throw "Refusing to publish with the placeholder Workshop id (0). Pass -Force to create the item."
    }
    Write-Host "==> Publishing Workshop item $id as '$SteamUser'" -ForegroundColor Cyan
}

# ---- changenote injection: pull the TOPMOST [h3]...[/h3] block straight out of
# About.xml's <ChangeLog> at run time, instead of relying on a hand-edited vdf line that
# reliably goes stale (it did - the checked-in vdf still says 0.9.1.0). Steam's Workshop
# changenote field accepts BBCode same as the description, so the raw block is passed through
# unmodified rather than stripped. ----
if (-not (Test-Path $AboutXml)) { throw "About.xml not found at $AboutXml (needed for the changenote)." }
$aboutRaw = Get-Content -LiteralPath $AboutXml -Raw
$clMatch = [regex]::Match($aboutRaw, '<ChangeLog>([\s\S]*?)</ChangeLog>')
if (-not $clMatch.Success) { throw "Could not find <ChangeLog> in $AboutXml." }
$changelogBody = $clMatch.Groups[1].Value

$firstH3 = $changelogBody.IndexOf('[h3]')
if ($firstH3 -lt 0) { throw "No [h3] block found in About.xml <ChangeLog> - cannot derive a changenote." }
$secondH3 = $changelogBody.IndexOf('[h3]', $firstH3 + 4)
if ($secondH3 -ge 0) {
    $topBlock = $changelogBody.Substring($firstH3, $secondH3 - $firstH3)
} else {
    $topBlock = $changelogBody.Substring($firstH3)
}
$changenote = $topBlock.Trim() -replace "`r`n", "`n"
if (-not $changenote) { throw "Derived an empty changenote from About.xml <ChangeLog> - aborting rather than publishing blank." }

Write-Host "==> Derived changenote from About.xml (topmost [h3] block, $($changenote.Length) chars):" -ForegroundColor Cyan
Write-Host $changenote

# SANITIZE for VDF rather than escape: steamcmd's KeyValues parser does NOT process escape
# sequences in the workshop config (a \" still terminates the quoted string), so a changenote
# containing a real double quote broke the whole file ("got } in key", first hit 2026-08-04 —
# the 0.9.7.1 note quoted a playtester report). Double quotes become typographic singles and
# backslashes become slashes; both read fine in the published note. Newlines stay — KeyValues
# quoted tokens span lines and earlier multiline notes published cleanly.
$vdfEscaped = $changenote -replace '"', "'" -replace '\\', '/'

if ($vdfText -notmatch '(?m)^\s*"changenote"\s+"[^"]*"\s*$') {
    throw "Could not find a `"changenote`" `"...`" line in $Vdf to replace."
}
$effectiveVdfText = [regex]::Replace($vdfText, '(?m)^(\s*"changenote"\s+)"[^"]*"(\s*)$', { param($m) "$($m.Groups[1].Value)`"$vdfEscaped`"$($m.Groups[2].Value)" })

# Write the effective vdf (with the live changenote) to dist\ (gitignored) rather than
# overwriting the checked-in tools\workshop_update.vdf - that file keeps only the static
# fields (appid/contentfolder/previewfile) plus a placeholder comment for changenote.
$DistDir      = Join-Path $RepoRoot 'dist'
if (-not (Test-Path $DistDir)) { New-Item -ItemType Directory -Path $DistDir -Force | Out-Null }
$EffectiveVdf = Join-Path $DistDir 'workshop_update.effective.vdf'
Set-Content -LiteralPath $EffectiveVdf -Value $effectiveVdfText -Encoding UTF8
Write-Host "==> Wrote effective vdf (live changenote): $EffectiveVdf" -ForegroundColor Cyan

& $SteamCmd +login $SteamUser +workshop_build_item $EffectiveVdf +quit
if ($LASTEXITCODE -ne 0) { throw "steamcmd failed (exit $LASTEXITCODE)." }
Write-Host "==> Steam Workshop publish complete." -ForegroundColor Green
if ($vdfText -match '"publishedfileid"\s+"0"') {
    Write-Warning "REMINDER: this was a first-time publish (publishedfileid was 0). Copy the new id steamcmd printed above into tools\workshop_update.vdf's `"publishedfileid`" field so future publishes update this item instead of creating duplicates."
}
