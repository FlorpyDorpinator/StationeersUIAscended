#requires -Version 5.1
<#
.SYNOPSIS
  Update the Steam Workshop PAGE (description and, optionally, visibility) of Stationeers UI
  Ascended WITHOUT uploading any mod content.

.DESCRIPTION
  tools\publish-steam.ps1 uploads content + a changenote and never sends a description, so the
  store-page text only changes through this script. It builds a metadata-only workshop_build_item
  file (appid + publishedfileid + description [+ visibility]) - steamcmd leaves every field it is
  not given untouched, so the mod files players download are not re-uploaded or changed.

  The description source is Documentation\Launch\workshop-description.bbcode (Steam BBCode).

  VDF QUOTE TRAP (same as publish-steam.ps1): steamcmd's KeyValues parser does not process
  escapes, so a literal double quote ends the string. Double quotes are turned into single
  quotes and backslashes into forward slashes; keep the source free of both anyway.

  Keep this file ASCII-only: Windows PowerShell 5.1 misreads non-ASCII characters in scripts.

.PARAMETER Visibility
  Optional: Public, FriendsOnly, Private or Unlisted. Omit to leave visibility as it is.

.PARAMETER DryRun
  Build and check the metadata file and print what would be sent; do not contact Steam.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\update-workshop-page.ps1 -DryRun
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\update-workshop-page.ps1 -Visibility Public
#>
[CmdletBinding()]
param(
    [ValidateSet('Public', 'FriendsOnly', 'Private', 'Unlisted')]
    [string]$Visibility,
    [string]$DescriptionFile,
    [string]$SteamUser = '<steam-user>',
    [string]$SteamCmd  = 'C:\steamcmd\steamcmd.exe',
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
if (-not $DescriptionFile) { $DescriptionFile = Join-Path $RepoRoot 'Documentation\Launch\workshop-description.bbcode' }
$Vdf = Join-Path $PSScriptRoot 'workshop_update.vdf'
if (-not (Test-Path -LiteralPath $DescriptionFile)) { throw "Description file not found: $DescriptionFile" }
if (-not (Test-Path -LiteralPath $Vdf)) { throw "Missing $Vdf (needed for the Workshop id)." }

$vdfText = Get-Content -LiteralPath $Vdf -Raw
if ($vdfText -notmatch '"publishedfileid"\s+"(\d+)"') { throw "No publishedfileid in $Vdf." }
$id = $Matches[1]
if ($id -eq '0') { throw "publishedfileid is 0 - there is no Workshop item to update." }
$appId = '544550'
if ($vdfText -match '"appid"\s+"(\d+)"') { $appId = $Matches[1] }

$desc = [IO.File]::ReadAllText($DescriptionFile, [Text.Encoding]::UTF8).Trim() -replace "`r`n", "`n"
if (-not $desc) { throw "The description file is empty - refusing to blank the Workshop page." }
$quotes = ([regex]::Matches($desc, '"')).Count
$slashes = ([regex]::Matches($desc, '\\')).Count
$desc = $desc -replace '"', "'" -replace '\\', '/'
$nonAscii = @($desc.ToCharArray() | Where-Object { [int]$_ -gt 127 }).Count
if ($desc.Length -gt 8000) { throw "Description is $($desc.Length) characters; the Steam Workshop limit is 8000." }
$placeholders = @([regex]::Matches($desc, '<[A-Z][A-Z ]*LINK>') | ForEach-Object { $_.Value } | Select-Object -Unique)
if ($placeholders.Count -gt 0 -and -not $DryRun) {
    throw "The description still contains placeholder(s): $($placeholders -join ', '). Replace them with real links (or delete those lines) first."
}

$visCode = $null
switch ($Visibility) {
    'Public'      { $visCode = '0' }
    'FriendsOnly' { $visCode = '1' }
    'Private'     { $visCode = '2' }
    'Unlisted'    { $visCode = '3' }
}

$lines = @(
    '"workshopitem"',
    '{',
    "`t`"appid`"`t`t`"$appId`"",
    "`t`"publishedfileid`"`t`t`"$id`""
)
if ($visCode) { $lines += "`t`"visibility`"`t`t`"$visCode`"" }
$lines += "`t`"description`"`t`t`"$desc`""
$lines += '}'

$DistDir = Join-Path $RepoRoot 'dist'
if (-not (Test-Path $DistDir)) { New-Item -ItemType Directory -Path $DistDir -Force | Out-Null }
$PageVdf = Join-Path $DistDir 'workshop_page.effective.vdf'
[IO.File]::WriteAllText($PageVdf, ($lines -join "`n") + "`n", (New-Object Text.UTF8Encoding($false)))

Write-Host "==> Workshop item $id (app $appId)" -ForegroundColor Cyan
Write-Host "    description: $($desc.Length) / 8000 characters from $DescriptionFile"
if ($quotes -gt 0 -or $slashes -gt 0) { Write-Warning "Replaced $quotes double quote(s) and $slashes backslash(es) (VDF quote trap)." }
if ($nonAscii -gt 0) { Write-Warning "$nonAscii non-ASCII character(s) in the description - Steam accepts UTF-8, but check how they render." }
if ($placeholders.Count -gt 0) { Write-Warning "Placeholder(s) still to fill before a real run: $($placeholders -join ', ')" }
if ($visCode) { Write-Host "    visibility:  $Visibility" } else { Write-Host "    visibility:  unchanged" }
Write-Host "    content:     NOT uploaded (metadata-only update)"
Write-Host "    metadata file: $PageVdf"

if ($DryRun) {
    Write-Host '==> Dry run - Steam was not contacted.' -ForegroundColor Yellow
    return
}
if (-not (Test-Path $SteamCmd)) { throw "steamcmd not found at $SteamCmd." }
& $SteamCmd +login $SteamUser +workshop_build_item $PageVdf +quit
if ($LASTEXITCODE -ne 0) { throw "steamcmd failed (exit $LASTEXITCODE)." }
Write-Host '==> Workshop page updated.' -ForegroundColor Green
