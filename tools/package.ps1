#requires -Version 5.1
<#
.SYNOPSIS
  Package the shippable Stationeers UI Ascended SLP mod.

.DESCRIPTION
  Builds the RELEASE DLL (the ScriptEngine dev shim is #if-gated out of Release in the
  .csproj - Debug only), stages the mod folder exactly as it ships, and writes a
  version-named zip at the repo root. Mirrors the StationpediaAscended packaging flow.

  Ship layout (top-level folder wrapped inside the zip):
    StationeersUIMod\
      StationeersUIMod.dll        <- Dev\bin\Release
      uia_effects.bundle          <- Dev\UiaEffectsBundle\Build (Tier B/C shaders; fail-soft)
      About\About.xml             <- Assets\About
      About\Preview.png
      About\thumb.png
      HudProfiles\*.xml + README  <- HudProfiles\

  The same staged folder (dist\StationeersUIMod) is what tools\workshop_update.vdf points
  steamcmd at, so 'Release & Publish' uploads exactly what the zip contains.

.PARAMETER SkipBuild
  Reuse the existing Release DLL instead of rebuilding.

.PARAMETER Install
  Also copy the staged mod into the local Stationeers mods folder so it can be tested as an
  INSTALLED SLP mod. OFF by default: running it alongside the dev DLL in BepInEx\scripts would
  load the mod twice (see CLAUDE.md "never install two at once").
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [switch]$Install,
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'

# ---- paths ----
$RepoRoot   = Split-Path -Parent $PSScriptRoot
$Csproj     = Join-Path $RepoRoot 'Dev\StationeersUIMod.Dev.csproj'
$DllPath    = Join-Path $RepoRoot "Dev\bin\$Configuration\StationeersUIMod.dll"
$Bundle     = Join-Path $RepoRoot 'Dev\UiaEffectsBundle\Build\uia_effects.bundle'
$AboutSrc   = Join-Path $RepoRoot 'Assets\About'
$ProfSrc    = Join-Path $RepoRoot 'HudProfiles'
$AboutXml   = Join-Path $AboutSrc 'About.xml'
$ModSrcFile = Join-Path $RepoRoot 'Assets\Scripts\StationeersUIMod\StationeersUIMod.cs'

$ModName  = 'StationeersUIMod'
$Stage    = Join-Path $RepoRoot "dist\$ModName"   # steamcmd contentfolder + zip source

# ---- clean-tree guard (warn, not abort: a dirty tree still packages, but the zip may not
# match any single commit, which makes a shipped bug hard to bisect later). ----
try {
    $gitStatus = & git -C $RepoRoot status --porcelain 2>$null
} catch { $gitStatus = $null }
if ($gitStatus) {
    Write-Warning "Packaging from a DIRTY tree (uncommitted changes present) - the zip may not match any commit."
    Write-Warning "Uncommitted paths:"
    foreach ($line in ($gitStatus -split "`r?`n" | Where-Object { $_ })) { Write-Warning "    $line" }
}

# ---- version (single source of truth: About.xml <Version> - but it MUST agree with the
# ModVersion const compiled into the DLL, or the zip name and the mod's own self-report
# diverge. This happened once: the zip was named 0.9.2 while the tree/DLL said 0.9.2.0. ----
if (-not (Test-Path $AboutXml)) { throw "About.xml not found at $AboutXml" }
[xml]$about = Get-Content -LiteralPath $AboutXml -Raw
$Version = "$($about.ModMetadata.Version)".Trim()
if (-not $Version) { throw "Could not read <Version> from $AboutXml" }

if (-not (Test-Path $ModSrcFile)) { throw "StationeersUIMod.cs not found at $ModSrcFile (cannot verify ModVersion)." }
$modSrcText = Get-Content -LiteralPath $ModSrcFile -Raw
$modVerMatch = [regex]::Match($modSrcText, 'ModVersion\s*=\s*"([^"]+)"')
if (-not $modVerMatch.Success) { throw "Could not find 'ModVersion = ""...""' const in $ModSrcFile." }
$CodeVersion = $modVerMatch.Groups[1].Value.Trim()

if ($CodeVersion -ne $Version) {
    Write-Host ""
    Write-Host "!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!" -ForegroundColor Red
    Write-Host "!! VERSION MISMATCH - REFUSING TO PACKAGE                              !!" -ForegroundColor Red
    Write-Host "!!   Assets\About\About.xml <Version>                 = $Version" -ForegroundColor Red
    Write-Host "!!   StationeersUIMod.cs ModVersion const             = $CodeVersion" -ForegroundColor Red
    Write-Host "!!   Bump BOTH together (see CLAUDE.md 'Version' section) and re-run.  !!" -ForegroundColor Red
    Write-Host "!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!" -ForegroundColor Red
    throw "Version mismatch: About.xml=$Version vs ModVersion const=$CodeVersion."
}

Write-Host "==> Packaging $ModName v$Version ($Configuration)" -ForegroundColor Cyan

# ---- build Release ----
if (-not $SkipBuild) {
    Write-Host "==> dotnet build -c $Configuration" -ForegroundColor Cyan
    dotnet build $Csproj -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Release build failed (exit $LASTEXITCODE)." }
}
if (-not (Test-Path $DllPath)) { throw "DLL not found: $DllPath  (build first, or drop -SkipBuild)." }

# ---- SAFETY: a ship build must NOT contain the dev ScriptEngine shim.
# The 0.8.0 release zip shipped the Dev DLL with ScriptEngineLoader compiled in, which self-
# initialised the mod under SLP and swallowed SLP's own OnLoaded. The .csproj now gates the shim
# to Debug; this is the belt-and-suspenders check so a ship build can never regress that. ----
$latin1  = [System.Text.Encoding]::GetEncoding('ISO-8859-1')
$dllText = [System.IO.File]::ReadAllText($DllPath, $latin1)
if ($dllText.Contains('ScriptEngineLoader')) {
    throw "Release DLL contains ScriptEngineLoader - the dev shim leaked into a ship build. Aborting. (Build -c Release; the shim is Debug-only in the .csproj.)"
}

# ---- stage the mod folder ----
if (Test-Path $Stage) { Remove-Item $Stage -Recurse -Force }
New-Item -ItemType Directory -Path $Stage -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $Stage 'About') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $Stage 'HudProfiles') -Force | Out-Null

Copy-Item $DllPath (Join-Path $Stage 'StationeersUIMod.dll') -Force

# Effects bundle: fail-soft. Missing = Tier B/C shader effects unavailable (HudShaderStore
# degrades to Tier A), so warn loudly rather than shipping silently without it.
if (Test-Path $Bundle) {
    Copy-Item $Bundle (Join-Path $Stage 'uia_effects.bundle') -Force
} else {
    Write-Warning "uia_effects.bundle NOT found at $Bundle."
    Write-Warning "The package will ship WITHOUT Tier B/C shader effects. Build it via Dev\UiaEffectsBundle\build-bundle.bat first if you want them."
}

Copy-Item (Join-Path $AboutSrc 'About.xml') (Join-Path $Stage 'About') -Force
foreach ($img in @('Preview.png','thumb.png')) {
    $src = Join-Path $AboutSrc $img
    if (Test-Path $src) { Copy-Item $src (Join-Path $Stage 'About') -Force } else { Write-Warning "About\$img missing." }
}

# HudProfiles: ship every .xml + the README (matches the existing releases).
Copy-Item (Join-Path $ProfSrc '*.xml') (Join-Path $Stage 'HudProfiles') -Force
$readme = Join-Path $ProfSrc 'README.md'
if (Test-Path $readme) { Copy-Item $readme (Join-Path $Stage 'HudProfiles') -Force }

# ---- content audit (post-stage) ----
# Hard failures: things that would ship a broken or mis-versioned mod.
# Soft warnings: things that are nice-to-have but not release blockers yet.
Write-Host "==> Content audit" -ForegroundColor Cyan
$auditErrors = New-Object System.Collections.Generic.List[string]

# Both shipped themes must be present (CLAUDE.md: shipped set = Stationeers Blue + Pure HUD).
foreach ($theme in @('Stationeers Blue', 'Pure HUD')) {
    $themeFile = Join-Path $Stage "HudProfiles\$theme.xml"
    if (-not (Test-Path $themeFile)) {
        $auditErrors.Add("Missing shipped theme: HudProfiles\$theme.xml")
    }
}
# At least one HudProfiles\*.xml must exist at all (belt-and-suspenders on the copy step above).
$stagedProfiles = @(Get-ChildItem (Join-Path $Stage 'HudProfiles') -Filter '*.xml' -ErrorAction SilentlyContinue)
if ($stagedProfiles.Count -eq 0) {
    $auditErrors.Add("No HudProfiles\*.xml staged at all.")
}

# Preview PNGs: soft warn only until FlorpyDorp adds them.
$stagedPreviews = @(Get-ChildItem (Join-Path $Stage 'HudProfiles') -Filter '*.png' -ErrorAction SilentlyContinue)
if ($stagedPreviews.Count -eq 0) {
    Write-Warning "No HudProfiles\*.png theme previews staged (cosmetic only - not a release blocker yet)."
}

# No loose .pdb files anywhere in the staged tree (an embedded pdb inside the dll is fine and
# expected; a separate .pdb alongside it would leak debug symbols/paths into the ship zip).
$loosePdbs = @(Get-ChildItem $Stage -Recurse -Filter '*.pdb' -ErrorAction SilentlyContinue)
if ($loosePdbs.Count -gt 0) {
    foreach ($pdb in $loosePdbs) { $auditErrors.Add("Loose .pdb in staged tree: $($pdb.FullName)") }
}

# About.xml must be present in the stage (belt-and-suspenders on the copy step above).
if (-not (Test-Path (Join-Path $Stage 'About\About.xml'))) {
    $auditErrors.Add("Missing About\About.xml in staged tree.")
}

if ($auditErrors.Count -gt 0) {
    Write-Host ""
    Write-Host "!! CONTENT AUDIT FAILED:" -ForegroundColor Red
    foreach ($e in $auditErrors) { Write-Host "!!   $e" -ForegroundColor Red }
    throw "Content audit failed ($($auditErrors.Count) error(s)). See above."
}
Write-Host "    content audit passed" -ForegroundColor Green

# ---- zip (version-named, at repo root; archive wraps the StationeersUIMod\ folder) ----
# Zip name derives from the SAME agreed version checked above (About.xml == ModVersion const) -
# never a separately hand-typed string, so a name/tree mismatch like the 0.9.2 vs 0.9.2.0
# incident above cannot recur.
$ZipPath = Join-Path $RepoRoot "$ModName-$Version.zip"
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
Compress-Archive -Path $Stage -DestinationPath $ZipPath -Force
$zipMb = '{0:N2}' -f ((Get-Item $ZipPath).Length / 1MB)
if (-not ((Split-Path -Leaf $ZipPath) -eq "$ModName-$Version.zip")) {
    throw "Internal error: zip name does not match the agreed version ($Version)."
}

# ---- final manifest listing ----
Write-Host ""
Write-Host "==> Staged manifest ($Stage):" -ForegroundColor Cyan
Get-ChildItem $Stage -Recurse -File | Sort-Object FullName | ForEach-Object {
    $rel = $_.FullName.Substring($Stage.Length + 1)
    $sizeKb = '{0:N1}' -f ($_.Length / 1KB)
    Write-Host ("    {0,10} KB  {1}" -f $sizeKb, $rel)
}

# ---- optional: install into the local mods folder for SLP testing ----
if ($Install) {
    $ModsFolder = Join-Path $env:USERPROFILE "Documents\My Games\Stationeers\mods\$ModName"
    if (Test-Path $ModsFolder) { Remove-Item $ModsFolder -Recurse -Force }
    New-Item -ItemType Directory -Path $ModsFolder -Force | Out-Null
    Copy-Item (Join-Path $Stage '*') $ModsFolder -Recurse -Force
    Write-Host "==> Installed to $ModsFolder" -ForegroundColor Green
    Write-Warning "A dev DLL in BepInEx\scripts + this installed mod = the mod loads TWICE. Clear one before playing."
}

Write-Host ""
Write-Host "==> Packaged $ModName v$Version" -ForegroundColor Green
Write-Host "    zip:   $ZipPath ($zipMb MB)"
Write-Host "    stage: $Stage  (steamcmd contentfolder)"
