#requires -Version 5.1
<#
.SYNOPSIS
  Fold an in-game lesson-editor session into the shipped tutorial copy
  (Assets\Scripts\StationeersUIMod\UI\Menu\Tutorial\TutorialCopy.g.cs).

.DESCRIPTION
  The flow (Documentation\0.9.8.0\Tutorial-Build-Contract.md s7):
    1. In game: uiadev, then the lesson editor (F8, or uiatutorial edit). Edit; edits save to
       BepInEx\config\StationeersUIMod\Tutorial\TutorialText.xml as you go.
    2. Press Export in the editor. It writes
       BepInEx\config\StationeersUIMod\Tutorial\Export\TutorialCopy.g.cs - the WHOLE copy file
       (shipped text + your overrides) in exactly the repo file's format.
    3. Run this script. It validates the export, prints what changed against the repo file, and
       copies it over the repo file byte for byte.
    4. Rebuild (dotnet build Dev\StationeersUIMod.Dev.csproj -c Debug), F6 in game, check
       uiatutorial lint, review the diff, commit.

  Validation (any failure aborts before anything is written):
    - ASCII only (printable 0x20-0x7E plus TAB, CR, LF) - the game's TMP font draws nothing else.
    - Braces balanced in the C# structure (string literals and comments excluded).
    - The Pairs initializer exists, closes, and holds an EVEN number of string literals.
    - No empty and no duplicate keys.
    - No interpolated / unterminated string literals.
  Escapes other than \\ \" \n, verbatim strings and unbalanced {tokens} inside a text are
  reported as warnings (the exporter never writes them; a hand edit might).

  File handling: .NET file APIs with explicit UTF-8 only - no Get-Content / Set-Content round
  trips and no regex edits of source (CLAUDE.md trap). The copy writes the exact bytes that were
  validated.

  The game install is found the same way tools\package.ps1 / dotnet build find it: the GameDir
  property of Dev\StationeersUIMod.Dev.csproj (override with -GameDir).

.PARAMETER GameDir
  The Stationeers install folder. Default: the csproj's GameDir.

.PARAMETER ExportPath
  The exported TutorialCopy.g.cs to bake. Default:
  <GameDir>\BepInEx\config\StationeersUIMod\Tutorial\Export\TutorialCopy.g.cs

.PARAMETER ClearOverrides
  After a successful bake, back up TutorialText.xml (to Tutorial\Backups\) and delete it, so the
  game shows the baked copy as shipped text again instead of as "edited" overrides. Refuses when
  the file holds an override the export does not contain (you edited after exporting) unless
  -Force.

.PARAMETER Force
  With -ClearOverrides: clear even if TutorialText.xml has edits the export does not contain
  (they are still backed up).

.EXAMPLE
  .\tools\bake-tutorial.ps1 -WhatIf
  Validate and show the diff; write nothing.

.EXAMPLE
  .\tools\bake-tutorial.ps1 -ClearOverrides
  Bake, then back up and clear the in-game overrides.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$GameDir,
    [string]$ExportPath,
    [switch]$ClearOverrides,
    [switch]$Force
)
$ErrorActionPreference = 'Stop'

# ---- paths ----
$RepoRoot = Split-Path -Parent $PSScriptRoot
$RepoCopy = Join-Path $RepoRoot 'Assets\Scripts\StationeersUIMod\UI\Menu\Tutorial\TutorialCopy.g.cs'
$Csproj   = Join-Path $RepoRoot 'Dev\StationeersUIMod.Dev.csproj'
$Utf8     = New-Object System.Text.UTF8Encoding($false)
$Utf8Strict = New-Object System.Text.UTF8Encoding($false, $true)   # throws on invalid bytes

function Resolve-GameDir {
    if ($GameDir) { return $GameDir }
    # Same source as tools\package.ps1 (dotnet build): the csproj's GameDir default.
    if ([System.IO.File]::Exists($Csproj)) {
        try {
            $doc = New-Object System.Xml.XmlDocument
            $doc.LoadXml([System.IO.File]::ReadAllText($Csproj, $Utf8))
            $node = $doc.SelectSingleNode("//*[local-name()='GameDir']")
            if ($node -and $node.InnerText.Trim()) { return $node.InnerText.Trim() }
        } catch {
            Write-Warning "Could not read GameDir from $Csproj ($($_.Exception.Message)) - using the Steam default."
        }
    }
    return 'C:\Program Files (x86)\Steam\steamapps\common\Stationeers'
}

$GameRoot    = Resolve-GameDir
$TutorialDir = Join-Path $GameRoot 'BepInEx\config\StationeersUIMod\Tutorial'
if (-not $ExportPath) { $ExportPath = Join-Path $TutorialDir 'Export\TutorialCopy.g.cs' }
$OverrideXml = Join-Path $TutorialDir 'TutorialText.xml'

Write-Host "==> Baking lesson copy" -ForegroundColor Cyan
Write-Host "    export: $ExportPath"
Write-Host "    repo:   $RepoCopy"

if (-not [System.IO.File]::Exists($ExportPath)) {
    throw "No export at $ExportPath. In game: uiadev, open the lesson editor (F8 or 'uiatutorial edit'), press Export - or pass -ExportPath / -GameDir."
}

# ---------------------------------------------------------------- the copy-file reader

function Show-Text([string]$s, [int]$max = 100) {
    if ($null -eq $s) { return '' }
    $one = $s.Replace("`r", '').Replace("`n", '\n')
    if ($one.Length -gt $max) { $one = $one.Substring(0, $max) + '...' }
    return $one
}

# A small C# lexer: enough to find the Pairs initializer's string literals and to count braces
# OUTSIDE strings and comments. Returns problems (fatal), warnings, and the pairs in file order.
function Read-CopySource([string]$Text) {
    $problems = New-Object 'System.Collections.Generic.List[string]'
    $warnings = New-Object 'System.Collections.Generic.List[string]'
    $literals = New-Object 'System.Collections.Generic.List[object]'

    # ASCII: printable 0x20-0x7E plus TAB / LF / CR.
    $line = 1; $col = 0; $bad = 0
    for ($k = 0; $k -lt $Text.Length; $k++) {
        $ch = [int]$Text[$k]
        if ($ch -eq 10) { $line++; $col = 0; continue }
        $col++
        if ($ch -eq 9 -or $ch -eq 13 -or ($ch -ge 0x20 -and $ch -le 0x7E)) { continue }
        $bad++
        if ($bad -le 5) { $problems.Add(('non-ASCII character U+{0:X4} at line {1}, column {2}' -f $ch, $line, $col)) }
    }
    if ($bad -gt 5) { $problems.Add("... and $($bad - 5) more non-ASCII character(s)") }

    $n = $Text.Length
    $i = 0; $line = 1
    $depth = 0
    $sawPairs = $false; $inPairs = $false; $pairsDepth = -1; $pairsClosed = $false
    $word = New-Object System.Text.StringBuilder

    while ($i -lt $n) {
        $c = $Text[$i]
        $next = if ($i + 1 -lt $n) { $Text[$i + 1] } else { [char]0 }

        # identifiers (to spot the word "Pairs")
        if ([char]::IsLetterOrDigit($c) -or $c -eq '_') { [void]$word.Append($c); $i++; continue }
        if ($word.Length -gt 0) {
            if ($word.ToString() -ceq 'Pairs') { $sawPairs = $true }
            [void]$word.Clear()
        }

        if ($c -eq "`n") { $line++; $i++; continue }

        if ($c -eq '/' -and $next -eq '/') {                  # line comment
            while ($i -lt $n -and $Text[$i] -ne "`n") { $i++ }
            continue
        }
        if ($c -eq '/' -and $next -eq '*') {                  # block comment
            $i += 2
            while ($i -lt $n -and -not ($Text[$i] -eq '*' -and $i + 1 -lt $n -and $Text[$i + 1] -eq '/')) {
                if ($Text[$i] -eq "`n") { $line++ }
                $i++
            }
            $i += 2
            continue
        }
        if ($c -eq "'") {                                      # char literal
            $i++
            while ($i -lt $n -and $Text[$i] -ne "'" -and $Text[$i] -ne "`n") { if ($Text[$i] -eq '\') { $i++ }; $i++ }
            $i++
            continue
        }
        if ($c -eq '$' -and ($next -eq '"' -or $next -eq '@')) {
            $problems.Add("interpolated string at line $line (the copy file holds plain literals only)")
            $i++
            continue
        }
        if ($c -eq '@' -and $next -eq '"') {                  # verbatim string
            $startLine = $line
            $sb = New-Object System.Text.StringBuilder
            $i += 2
            $closed = $false
            while ($i -lt $n) {
                $d = $Text[$i]
                if ($d -eq '"') {
                    if ($i + 1 -lt $n -and $Text[$i + 1] -eq '"') { [void]$sb.Append('"'); $i += 2; continue }
                    $i++; $closed = $true; break
                }
                if ($d -eq "`n") { $line++ }
                if ($d -ne "`r") { [void]$sb.Append($d) }
                $i++
            }
            if (-not $closed) { $problems.Add("unterminated verbatim string starting at line $startLine"); break }
            $warnings.Add("verbatim string at line $startLine (the exporter writes plain literals)")
            if ($inPairs) { $literals.Add([pscustomobject]@{ Value = $sb.ToString(); Line = $startLine }) }
            continue
        }
        if ($c -eq '"') {                                      # regular string
            $startLine = $line
            $sb = New-Object System.Text.StringBuilder
            $i++
            $closed = $false
            while ($i -lt $n) {
                $d = $Text[$i]
                if ($d -eq '"') { $i++; $closed = $true; break }
                if ($d -eq "`r" -or $d -eq "`n") { break }
                if ($d -eq '\' -and $i + 1 -lt $n) {
                    $e = $Text[$i + 1]
                    switch -CaseSensitive ($e) {
                        '\' { [void]$sb.Append('\'); $i += 2 }
                        '"' { [void]$sb.Append('"'); $i += 2 }
                        'n' { [void]$sb.Append("`n"); $i += 2 }
                        't' { [void]$sb.Append("`t"); $i += 2; $warnings.Add("escape \t at line $line (only \\ \"" \n are canonical)") }
                        'r' { [void]$sb.Append("`r"); $i += 2; $warnings.Add("escape \r at line $line (only \\ \"" \n are canonical)") }
                        "'" { [void]$sb.Append("'"); $i += 2; $warnings.Add("escape \' at line $line (only \\ \"" \n are canonical)") }
                        '0' { [void]$sb.Append([char]0); $i += 2; $warnings.Add("escape \0 at line $line (only \\ \"" \n are canonical)") }
                        'u' {
                            $hex = if ($i + 6 -le $n) { $Text.Substring($i + 2, 4) } else { '' }
                            $v = 0
                            if ($hex.Length -eq 4 -and [int]::TryParse($hex, [System.Globalization.NumberStyles]::HexNumber, $null, [ref]$v)) {
                                [void]$sb.Append([char]$v); $i += 6
                            } else { [void]$sb.Append('\u'); $i += 2 }
                            $warnings.Add("escape \u at line $line (only \\ \"" \n are canonical)")
                        }
                        default { [void]$sb.Append($e); $i += 2; $warnings.Add("escape \$e at line $line (only \\ \"" \n are canonical)") }
                    }
                    continue
                }
                [void]$sb.Append($d)
                $i++
            }
            if (-not $closed) { $problems.Add("unterminated string at line $startLine"); break }
            if ($inPairs) { $literals.Add([pscustomobject]@{ Value = $sb.ToString(); Line = $startLine }) }
            elseif ($sawPairs -or $pairsClosed) { $warnings.Add("string literal outside the Pairs initializer at line $startLine") }
            continue
        }
        if ($c -eq '{') {
            if ($sawPairs -and -not $inPairs -and -not $pairsClosed) { $inPairs = $true; $pairsDepth = $depth }
            $depth++
            $i++
            continue
        }
        if ($c -eq '}') {
            $depth--
            if ($depth -lt 0) { $problems.Add("unbalanced '}' at line $line"); $depth = 0 }
            if ($inPairs -and $depth -eq $pairsDepth) { $inPairs = $false; $pairsClosed = $true }
            $i++
            continue
        }
        $i++
    }
    if ($depth -ne 0) { $problems.Add("unbalanced braces: $depth '{' never closed") }
    if (-not $sawPairs) { $problems.Add("no 'Pairs' array found") }
    elseif (-not $pairsClosed) { $problems.Add("the Pairs initializer is never closed") }
    if (($literals.Count % 2) -ne 0) { $problems.Add("odd number of strings in Pairs ($($literals.Count)) - a key or a text is missing") }

    foreach ($need in @('namespace StationeersUIMod.UI.Menu.Tutorial', 'static class TutorialCopy', 'static readonly string[] Pairs')) {
        if ($Text.IndexOf($need, [System.StringComparison]::Ordinal) -lt 0) { $problems.Add("missing '$need'") }
    }

    # Pairs -> ordered keys + map; duplicates / empty keys are fatal.
    $keys = New-Object 'System.Collections.Generic.List[string]'
    $map  = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([System.StringComparer]::Ordinal)
    $dups = New-Object 'System.Collections.Generic.List[string]'
    # NB: PowerShell variable names are case-insensitive - never name a local $text here, it
    # would overwrite the $Text parameter that the line-ending scan below still reads.
    for ($p = 0; $p + 1 -lt $literals.Count; $p += 2) {
        $key = [string]$literals[$p].Value
        $val = [string]$literals[$p + 1].Value
        if ($key.Length -eq 0) { $problems.Add("empty key at line $($literals[$p].Line)"); continue }
        if ($map.ContainsKey($key)) { $dups.Add("$key (line $($literals[$p].Line))"); continue }
        $map[$key] = $val
        $keys.Add($key)
        # A broken {token} inside a text is lint's job in game; flag it here too.
        $open = 0
        foreach ($tc in $val.ToCharArray()) {
            if ($tc -eq '{') { $open++ } elseif ($tc -eq '}') { $open-- }
            if ($open -lt 0 -or $open -gt 1) { break }
        }
        if ($open -ne 0) { $warnings.Add("unbalanced {token} braces in the text of '$key'") }
    }
    foreach ($d in ($dups | Select-Object -First 10)) { $problems.Add("duplicate key $d") }
    if ($dups.Count -gt 10) { $problems.Add("... and $($dups.Count - 10) more duplicate key(s)") }

    $crlf = 0; $lf = 0
    for ($k = 0; $k -lt $Text.Length; $k++) {
        if ($Text[$k] -eq "`n") { if ($k -gt 0 -and $Text[$k - 1] -eq "`r") { $crlf++ } else { $lf++ } }
    }
    $eol = if ($crlf -gt 0 -and $lf -eq 0) { 'CRLF' } elseif ($lf -gt 0 -and $crlf -eq 0) { 'LF' } elseif ($crlf -eq 0) { 'none' } else { 'mixed' }

    return [pscustomobject]@{
        Problems = $problems; Warnings = $warnings; Keys = $keys; Map = $map; Eol = $eol
    }
}

function Read-Ascii([string]$Path, [string]$What) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    try { $text = $Utf8Strict.GetString($bytes) }
    catch { throw "$What is not valid UTF-8 (so not ASCII): $Path" }
    return [pscustomobject]@{ Bytes = $bytes; Text = $text }
}

# ---------------------------------------------------------------- validate the export

Write-Host "==> Validating the export" -ForegroundColor Cyan
$export = Read-Ascii $ExportPath 'The export'
$new = Read-CopySource $export.Text
foreach ($w in ($new.Warnings | Select-Object -First 15)) { Write-Warning $w }
if ($new.Warnings.Count -gt 15) { Write-Warning "... and $($new.Warnings.Count - 15) more warning(s)" }
if ($new.Problems.Count -gt 0) {
    Write-Host ""
    Write-Host "!! EXPORT REJECTED - nothing was written:" -ForegroundColor Red
    foreach ($p in $new.Problems) { Write-Host "!!   $p" -ForegroundColor Red }
    throw "Export validation failed ($($new.Problems.Count) problem(s)). Fix the text in game (the lesson editor's lint line names the problem) and export again."
}
Write-Host ("    OK: {0} pairs, ASCII, braces balanced, no duplicate keys, line endings {1}" -f $new.Keys.Count, $new.Eol) -ForegroundColor Green
if ($new.Eol -ne 'CRLF') { Write-Warning "The export's line endings are $($new.Eol); the contract format is CRLF (git normalizes .cs files, so this is cosmetic)." }

# ---------------------------------------------------------------- diff against the repo file

Write-Host "==> Changes against the repo file" -ForegroundColor Cyan
$identical = $false
$changed = New-Object 'System.Collections.Generic.List[string]'
$added   = New-Object 'System.Collections.Generic.List[string]'
$removed = New-Object 'System.Collections.Generic.List[string]'
$old = $null
if ([System.IO.File]::Exists($RepoCopy)) {
    $repo = Read-Ascii $RepoCopy 'The repo copy file'
    $identical = ($repo.Bytes.Length -eq $export.Bytes.Length) -and
        ([System.Convert]::ToBase64String($repo.Bytes) -ceq [System.Convert]::ToBase64String($export.Bytes))
    $old = Read-CopySource $repo.Text
    if ($old.Problems.Count -gt 0) {
        Write-Warning "The CURRENT repo file has problems of its own (the bake replaces it):"
        foreach ($p in $old.Problems) { Write-Warning "  $p" }
    }
    foreach ($k in $new.Keys) {
        if (-not $old.Map.ContainsKey($k)) { $added.Add($k) }
        elseif (-not [string]::Equals($old.Map[$k], $new.Map[$k], [System.StringComparison]::Ordinal)) { $changed.Add($k) }
    }
    foreach ($k in $old.Keys) { if (-not $new.Map.ContainsKey($k)) { $removed.Add($k) } }

    $orderMoved = $false
    if ($added.Count -eq 0 -and $removed.Count -eq 0 -and $old.Keys.Count -eq $new.Keys.Count) {
        for ($k = 0; $k -lt $new.Keys.Count; $k++) {
            if (-not [string]::Equals($old.Keys[$k], $new.Keys[$k], [System.StringComparison]::Ordinal)) { $orderMoved = $true; break }
        }
    }

    Write-Host ("    {0} changed, {1} added, {2} removed (of {3} pairs){4}" -f $changed.Count, $added.Count, $removed.Count, $new.Keys.Count, $(if ($orderMoved) { ', ORDER changed' } else { '' }))
    foreach ($k in ($changed | Select-Object -First 25)) {
        Write-Host "    ~ $k" -ForegroundColor Yellow
        Write-Host ("        was: " + (Show-Text $old.Map[$k]))
        Write-Host ("        now: " + (Show-Text $new.Map[$k]))
    }
    if ($changed.Count -gt 25) { Write-Host "    ... and $($changed.Count - 25) more changed key(s)" }
    foreach ($k in ($added | Select-Object -First 10)) { Write-Host "    + $k" -ForegroundColor Green }
    if ($added.Count -gt 10) { Write-Host "    ... and $($added.Count - 10) more added key(s)" }
    foreach ($k in ($removed | Select-Object -First 10)) { Write-Host "    - $k" -ForegroundColor Red }
    if ($removed.Count -gt 10) { Write-Host "    ... and $($removed.Count - 10) more removed key(s)" }
    if ($added.Count -gt 0 -or $removed.Count -gt 0) {
        Write-Warning "Keys were added/removed: the export follows TutorialChapters of the BUILD that exported it. Make sure that build matches the repo's TutorialChapters.cs."
    }
    if ($old.Eol -ne $new.Eol) { Write-Host "    line endings: repo $($old.Eol) -> export $($new.Eol) (git normalizes .cs, not a content change)" }
    if ($identical) { Write-Host "    byte-identical: the repo file already holds exactly this copy." -ForegroundColor Green }
} else {
    Write-Warning "No repo copy file at $RepoCopy - the export will create it."
}

# ---------------------------------------------------------------- write

if ($identical) {
    Write-Host "==> Nothing to copy." -ForegroundColor Green
} elseif ($PSCmdlet.ShouldProcess($RepoCopy, "Write the baked lesson copy ($($changed.Count) changed, $($added.Count) added, $($removed.Count) removed)")) {
    [System.IO.File]::WriteAllBytes($RepoCopy, $export.Bytes)   # exactly the validated bytes
    Write-Host "==> Baked into $RepoCopy" -ForegroundColor Green
}

# ---------------------------------------------------------------- -ClearOverrides

if ($ClearOverrides) {
    Write-Host "==> Clearing in-game overrides" -ForegroundColor Cyan
    if (-not [System.IO.File]::Exists($OverrideXml)) {
        Write-Host "    no TutorialText.xml at $OverrideXml - nothing to clear."
    } else {
        # Refuse to drop an edit the export does not carry (you typed after pressing Export).
        $missing = New-Object 'System.Collections.Generic.List[string]'
        $orphans = 0; $legacy = 0
        try {
            $doc = New-Object System.Xml.XmlDocument
            $doc.LoadXml([System.IO.File]::ReadAllText($OverrideXml, $Utf8))
            foreach ($node in $doc.SelectNodes('/TutorialText/Text')) {
                $key = $node.GetAttribute('key')
                if (-not $key) { continue }
                $val = $node.InnerText.Replace("`r", '')
                if (-not $new.Map.ContainsKey($key)) { $orphans++; continue }
                if (-not [string]::Equals($new.Map[$key], $val, [System.StringComparison]::Ordinal)) { $missing.Add($key) }
            }
            $legacy = $doc.SelectNodes('/TutorialText/Step').Count
        } catch {
            if (-not $Force) { throw "Could not read $OverrideXml ($($_.Exception.Message)). Pass -Force to back it up and clear it anyway." }
            Write-Warning "Could not read $OverrideXml ($($_.Exception.Message)) - clearing anyway (-Force)."
        }
        if ($missing.Count -gt 0) {
            Write-Host "    TutorialText.xml has $($missing.Count) edit(s) the export does not contain (edited after Export?):" -ForegroundColor Yellow
            foreach ($k in ($missing | Select-Object -First 10)) { Write-Host "      $k" -ForegroundColor Yellow }
            if (-not $Force) { throw "Refusing to clear overrides that were never exported. Export again in game, or pass -Force (they are still backed up)." }
        }
        if ($orphans -gt 0) { Write-Host "    ($orphans override(s) for keys the script no longer has - dropped with the file, kept in the backup)" }
        if ($legacy -gt 0)  { Write-Host "    ($legacy retired v1 step entr$(if ($legacy -eq 1) { 'y' } else { 'ies' }) - dropped with the file, kept in the backup)" }

        $backupDir = Join-Path $TutorialDir 'Backups'
        $backup = Join-Path $backupDir ('TutorialText.' + (Get-Date).ToString('yyyyMMdd-HHmmss') + '.xml')
        if ($PSCmdlet.ShouldProcess($OverrideXml, "Back up to $backup, then delete it (clear every override)")) {
            [void][System.IO.Directory]::CreateDirectory($backupDir)
            [System.IO.File]::Copy($OverrideXml, $backup, $false)
            [System.IO.File]::Delete($OverrideXml)
            Write-Host "    backed up to $backup" -ForegroundColor Green
            Write-Host "    cleared $OverrideXml" -ForegroundColor Green
        }
        if (Get-Process -Name 'rocketstation' -ErrorAction SilentlyContinue) {
            Write-Warning "Stationeers is running: its lesson editor still holds the overrides in memory, and its next save writes them back. Rebuild + F6 (or restart) before editing again."
        }
    }
}

# ---------------------------------------------------------------- next steps

Write-Host ""
Write-Host "==> Next" -ForegroundColor Cyan
Write-Host "    1. dotnet build Dev\StationeersUIMod.Dev.csproj -c Debug   (then F6 in game)"
Write-Host "    2. in game: uiatutorial lint   (expect 0 problems)"
Write-Host "    3. review the diff of TutorialCopy.g.cs and commit it"
if (-not $ClearOverrides -and [System.IO.File]::Exists($OverrideXml)) {
    Write-Host "    note: TutorialText.xml still holds your overrides; once the new build is loaded they equal the"
    Write-Host "          shipped text. Re-run with -ClearOverrides (or 'uiatutorial reset' in game) to drop them."
}
