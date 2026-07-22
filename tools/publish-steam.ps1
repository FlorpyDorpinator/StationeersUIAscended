#requires -Version 5.1
<#
.SYNOPSIS
  Publish the staged Stationeers UI Ascended mod to the Steam Workshop via steamcmd.

.DESCRIPTION
  Uploads tools\workshop_update.vdf's contentfolder (the repo dist\StationeersUIMod folder that
  package.ps1 stages) as a Workshop item. Run package.ps1 first so dist\ is fresh.

  PLACEHOLDER SAFETY: the mod has no Workshop id yet, so workshop_update.vdf ships with
  publishedfileid "0". Publishing with 0 would CREATE a brand-new Workshop item, so this script
  REFUSES to run while the id is 0 unless you pass -Force (an intentional first-time create).
  After the first create, steamcmd prints the new id - paste it into workshop_update.vdf so every
  later publish updates that item instead of making duplicates.

.PARAMETER SteamUser
  Steam account to log in as (defaults to the same account used for StationpediaAscended).
  steamcmd will prompt for the password / Steam Guard code on first use and then cache it.

.PARAMETER Force
  Publish even though publishedfileid is still 0 - i.e. intentionally CREATE the Workshop item.
#>
[CmdletBinding()]
param(
    [string]$SteamUser = '<steam-user>',
    [string]$SteamCmd  = 'C:\steamcmd\steamcmd.exe',
    [switch]$Force
)
$ErrorActionPreference = 'Stop'

$Vdf = Join-Path $PSScriptRoot 'workshop_update.vdf'
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

& $SteamCmd +login $SteamUser +workshop_build_item $Vdf +quit
if ($LASTEXITCODE -ne 0) { throw "steamcmd failed (exit $LASTEXITCODE)." }
Write-Host "==> Steam Workshop publish complete." -ForegroundColor Green
