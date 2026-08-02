#requires -Version 5.1
<#
.SYNOPSIS
  Render the HUD Designer Handbook markdown into a paginated PDF and one PNG per page,
  entirely locally via headless Microsoft Edge - no downloads, no external toolchains.

.DESCRIPTION
  Documentation\HUD-Designer-Handbook.md (or any doc following the same strict subset) is
  authored in a small markdown subset with explicit page breaks:

      #, ##, ###            headings
      blank-line paragraphs
      **bold**, *italic*    inline emphasis
      `inline code`         inline code span
      ``` ... ```           fenced code block
      - item                bullet list
      1. item               numbered list
      > quote               blockquote
      ---                   horizontal rule
      <!-- page -->         explicit page break

  Everything else is HTML-encoded rather than interpreted, so the "renderer" never has to
  guess - a construct is either in the list above or it shows up as literal escaped text.

  Pipeline:
    1. Parse the source into one HTML fragment per <!-- page --> - delimited page.
    2. Wrap all pages into a single HTML document (print-oriented CSS: A4 @ 210mm x 297mm,
       18mm padding, one CSS page-break per .page) and print it to PDF via Edge headless.
    3. Wrap each page individually into its own HTML document (screen-oriented CSS: fixed
       1240x1754 px, i.e. A4 @ 150dpi) and screenshot it via Edge headless.

  Both outputs land in -OutDir (default: repo root Handbook\), which tools\package.ps1 stages
  into the shipped mod additively (see the "Handbook" block there).

  No network access, no Node/pandoc/wkhtmltopdf - just PowerShell + the Edge already on the box.

.PARAMETER Source
  Path to the handbook markdown source. Defaults to Documentation\HUD-Designer-Handbook.md
  under the repo root.

.PARAMETER OutDir
  Destination folder for "<name>.pdf" and "pages\page-NN.png". Defaults to the repo root's
  Handbook\ folder. Re-run wipes and rebuilds pages\ and the pdf so stale pages never linger.

.PARAMETER EdgePath
  Explicit path to msedge.exe. If omitted, tries the standard 64-bit and x86 Program Files
  locations, then falls back to PATH.

.EXAMPLE
  tools\render-handbook.ps1
  Renders the shipped handbook to Handbook\HUD-Designer-Handbook.pdf + Handbook\pages\*.png.

.EXAMPLE
  tools\render-handbook.ps1 -Source C:\tmp\sample.md -OutDir C:\tmp\out -EdgePath 'D:\Edge\msedge.exe'
  Renders an arbitrary doc elsewhere with an explicit Edge binary (smoke-testing the pipeline).
#>
[CmdletBinding()]
param(
    [string]$Source,
    [string]$OutDir,
    [string]$EdgePath
)
$ErrorActionPreference = 'Stop'

# ---- paths ----
$RepoRoot = Split-Path -Parent $PSScriptRoot
if (-not $Source) {
    $Source = Join-Path $RepoRoot 'Documentation\HUD-Designer-Handbook.md'
} elseif (-not [System.IO.Path]::IsPathRooted($Source)) {
    # A relative -Source must resolve against the CALLER's current location, not wherever the
    # script happens to live - GetFullPath() below resolves against the process's working
    # directory, which is not guaranteed to match PowerShell's own location ($PWD).
    $Source = Join-Path (Resolve-Path -LiteralPath '.').Path $Source
}
if (-not $OutDir)  { $OutDir  = Join-Path $RepoRoot 'Handbook' }
$Source = [System.IO.Path]::GetFullPath($Source)
$OutDir = [System.IO.Path]::GetFullPath($OutDir)

# =====================================================================================
# Inline span conversion: escape -> protect `code` -> **bold** -> *italic* -> restore code.
# Order matters (see .DESCRIPTION): code spans are protected before bold/italic so a literal
# "**" typed inside a code span (e.g. documenting a CLI flag) is never mistaken for emphasis.
# =====================================================================================
function ConvertFrom-InlineMarkdown {
    param([string]$Text)

    if ([string]::IsNullOrEmpty($Text)) { return '' }

    # 1) HTML-encode metacharacters first - order matters, & before < / > or we'd double-encode.
    $out = $Text.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;')

    # 2) Protect inline `code` spans behind a control-char sentinel (never appears in authored
    #    text) so the bold/italic regexes below cannot see inside them. Plain substring surgery
    #    (not a MatchEvaluator) to sidestep any $-in-replacement backreference ambiguity.
    $ctrl = [char]2
    $codeSpans = New-Object System.Collections.Generic.List[string]
    $codePattern = [regex]'`([^`\r\n]+?)`'
    $m = $codePattern.Match($out)
    while ($m.Success) {
        $token = "$ctrl$($codeSpans.Count)$ctrl"
        $codeSpans.Add($m.Groups[1].Value)
        $out = $out.Substring(0, $m.Index) + $token + $out.Substring($m.Index + $m.Length)
        $m = $codePattern.Match($out, $m.Index + $token.Length)
    }

    # 3) Bold before italic, so **x** isn't half-eaten by the *x* rule first.
    $out = [regex]::Replace($out, '\*\*(.+?)\*\*', '<strong>$1</strong>')

    # 4) Italic.
    $out = [regex]::Replace($out, '\*(.+?)\*', '<em>$1</em>')

    # 5) Restore the protected code spans as real <code> tags (literal .Replace - no regex
    #    backreference risk even if the span text itself contains "$").
    for ($k = 0; $k -lt $codeSpans.Count; $k++) {
        $token = "$ctrl$k$ctrl"
        $out = $out.Replace($token, "<code>$($codeSpans[$k])</code>")
    }

    return $out
}

# A hard-wrapped CONTINUATION of a list item: non-blank, and not the start of any other
# block (bullet, numbered item, heading, fence, blockquote, rule, page break). Authors wrap
# list items at ~90 columns like everything else; without this, the wrapped tail of an item
# would fall out of the <ul>/<ol> and render as its own paragraph.
function Test-ListContinuation([string]$l) {
    if ($null -eq $l) { return $false }
    $t = $l.Trim()
    if ($t -eq '') { return $false }
    if ($t -eq '<!-- page -->') { return $false }
    if ($l -match '^\s*-\s+') { return $false }
    if ($l -match '^\s*\d+\.\s+') { return $false }
    if ($l -match '^#{1,3}\s+') { return $false }
    if ($t.StartsWith('```')) { return $false }
    if ($l -match '^\s*>') { return $false }
    if ($t -match '^-{3,}$') { return $false }
    return $true
}

# =====================================================================================
# Block-level parser. Returns one HTML fragment (string) per <!-- page --> - delimited page.
# Supports exactly: #/##/### headings, paragraphs, "- " bullets, "N. " numbered lists,
# "> " blockquotes, "---" rules, ``` fenced code, and the <!-- page --> marker. Anything else
# falls through to a paragraph (and gets HTML-encoded there, never interpreted as markup).
# =====================================================================================
function ConvertFrom-HandbookMarkdown {
    param([string[]]$Lines)

    $pageBreakMark = [char]3
    $blocks = New-Object System.Collections.Generic.List[string]
    $paragraph = New-Object System.Collections.Generic.List[string]

    function Write-Paragraph {
        if ($paragraph.Count -gt 0) {
            $joined = ($paragraph -join ' ')
            $blocks.Add('<p>' + (ConvertFrom-InlineMarkdown $joined) + '</p>')
            $paragraph.Clear()
        }
    }

    $i = 0
    $inCode = $false
    $codeLines = New-Object System.Collections.Generic.List[string]

    while ($i -lt $Lines.Count) {
        $line = $Lines[$i]

        # Fenced code block delimiter (toggles regardless of what's inside).
        if ($line -match '^\s*```') {
            if ($inCode) {
                $encoded = ($codeLines -join "`n").Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;')
                $blocks.Add("<pre><code>$encoded</code></pre>")
                $codeLines.Clear()
                $inCode = $false
            } else {
                Write-Paragraph
                $inCode = $true
            }
            $i++
            continue
        }

        if ($inCode) {
            $codeLines.Add($line)
            $i++
            continue
        }

        # Explicit page break.
        if ($line.Trim() -eq '<!-- page -->') {
            Write-Paragraph
            $blocks.Add([string]$pageBreakMark)
            $i++
            continue
        }

        # Blank line: paragraph separator.
        if ($line.Trim() -eq '') {
            Write-Paragraph
            $i++
            continue
        }

        # Headings.
        if ($line -match '^(#{1,3})\s+(.+?)\s*$') {
            Write-Paragraph
            $level = $Matches[1].Length
            $text = $Matches[2]
            $blocks.Add("<h$level>" + (ConvertFrom-InlineMarkdown $text) + "</h$level>")
            $i++
            continue
        }

        # Horizontal rule (checked before bullets for clarity, though "---" could not match
        # the bullet pattern anyway since that requires a space right after the dash).
        if ($line.Trim() -match '^-{3,}$') {
            Write-Paragraph
            $blocks.Add('<hr />')
            $i++
            continue
        }

        # Bullet list - consumes consecutive "- " lines into one <ul>, folding hard-wrapped
        # continuation lines into the item they belong to. Items are kept RAW while collecting
        # and inline-converted once per whole item, so a **bold** span may cross a wrap.
        if ($line -match '^\s*-\s+(.+?)\s*$') {
            Write-Paragraph
            $items = New-Object System.Collections.Generic.List[string]
            while ($i -lt $Lines.Count) {
                if ($Lines[$i] -match '^\s*-\s+(.+?)\s*$') { $items.Add($Matches[1]); $i++; continue }
                if ($items.Count -gt 0 -and (Test-ListContinuation $Lines[$i])) {
                    $items[$items.Count - 1] = $items[$items.Count - 1] + ' ' + $Lines[$i].Trim()
                    $i++
                    continue
                }
                break
            }
            $li = ($items | ForEach-Object { '<li>' + (ConvertFrom-InlineMarkdown $_) + '</li>' }) -join "`n"
            $blocks.Add("<ul>`n$li`n</ul>")
            continue
        }

        # Numbered list - same shape as bullets, one <ol>, wrapped lines folded in.
        if ($line -match '^\s*\d+\.\s+(.+?)\s*$') {
            Write-Paragraph
            $items = New-Object System.Collections.Generic.List[string]
            while ($i -lt $Lines.Count) {
                if ($Lines[$i] -match '^\s*\d+\.\s+(.+?)\s*$') { $items.Add($Matches[1]); $i++; continue }
                if ($items.Count -gt 0 -and (Test-ListContinuation $Lines[$i])) {
                    $items[$items.Count - 1] = $items[$items.Count - 1] + ' ' + $Lines[$i].Trim()
                    $i++
                    continue
                }
                break
            }
            $li = ($items | ForEach-Object { '<li>' + (ConvertFrom-InlineMarkdown $_) + '</li>' }) -join "`n"
            $blocks.Add("<ol>`n$li`n</ol>")
            continue
        }

        # Blockquote - consumes consecutive "> " lines into one <blockquote><p>.
        if ($line -match '^\s*>\s?(.*)$') {
            Write-Paragraph
            $quote = New-Object System.Collections.Generic.List[string]
            while ($i -lt $Lines.Count -and $Lines[$i] -match '^\s*>\s?(.*)$') {
                $quote.Add($Matches[1])
                $i++
            }
            $joined = ($quote -join ' ').Trim()
            $blocks.Add('<blockquote><p>' + (ConvertFrom-InlineMarkdown $joined) + '</p></blockquote>')
            continue
        }

        # Default: accumulate into the current paragraph.
        $paragraph.Add($line.Trim())
        $i++
    }

    Write-Paragraph
    if ($inCode -and $codeLines.Count -gt 0) {
        # Unterminated fence at EOF: flush what we have rather than silently dropping it.
        $encoded = ($codeLines -join "`n").Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;')
        $blocks.Add("<pre><code>$encoded</code></pre>")
    }

    # Split the flat block list into pages around the page-break sentinel. A page with zero
    # blocks (a leading/trailing/doubled <!-- page --> marker) is skipped rather than emitted -
    # otherwise it renders as a blank page later, and a blank 1240x1754 PNG can still slip under
    # the min-size check and fail the run for a reason that isn't obvious from the output.
    $pages = New-Object System.Collections.Generic.List[string]
    $current = New-Object System.Collections.Generic.List[string]
    foreach ($b in $blocks) {
        if ($b -eq [string]$pageBreakMark) {
            if ($current.Count -gt 0) { $pages.Add(($current -join "`n")) }
            $current = New-Object System.Collections.Generic.List[string]
        } else {
            $current.Add($b)
        }
    }
    if ($current.Count -gt 0) { $pages.Add(($current -join "`n")) }

    return ,$pages
}

# =====================================================================================
# CSS. Base rules are shared by both render targets; print/screen each add their own page-
# sizing block (kept separate so we never rely on @media screen/print inside a single file -
# the PDF doc and each PNG doc are already distinct files with a single fixed purpose).
# =====================================================================================
function Get-HandbookBaseCss {
    return @'
* { box-sizing: border-box; }
html, body { margin: 0; padding: 0; }
body {
  font-family: "Segoe UI", Tahoma, Geneva, Verdana, Arial, sans-serif;
  color: #1c2b36;
  background: #ffffff;
  font-size: 14px;
  line-height: 1.5;
}
.page > *:first-child { margin-top: 0; }
h1, h2, h3 {
  color: #173a5e;
  font-family: "Segoe UI Semibold", "Segoe UI", Tahoma, Arial, sans-serif;
  margin: 0 0 0.5em 0;
  line-height: 1.25;
}
h1 { font-size: 26px; border-bottom: 2px solid #2f6db3; padding-bottom: 6px; }
h2 { font-size: 20px; color: #2f6db3; margin-top: 1.2em; }
h3 { font-size: 16px; margin-top: 1em; }
p { margin: 0 0 0.8em 0; }
ul, ol { margin: 0 0 0.8em 0; padding-left: 1.4em; }
li { margin-bottom: 0.3em; }
blockquote {
  margin: 0 0 0.8em 0;
  padding: 0.4em 1em;
  border-left: 4px solid #2f6db3;
  background: #f2f6fa;
  color: #37474f;
}
blockquote p { margin: 0; }
hr { border: none; border-top: 1px solid #b9c7d3; margin: 1.2em 0; }
code {
  font-family: Consolas, "Courier New", monospace;
  background: #eef2f6;
  border: 1px solid #d7e0e8;
  border-radius: 3px;
  padding: 0.1em 0.35em;
  font-size: 0.9em;
  color: #173a5e;
}
pre {
  background: #f4f7fa;
  border: 1px solid #d7e0e8;
  border-radius: 4px;
  padding: 0.8em 1em;
  margin: 0 0 0.8em 0;
  overflow: auto;
}
pre code { background: none; border: none; padding: 0; font-size: 0.85em; color: #1c2b36; }
strong { color: #12283d; }
em { color: #2f4a5e; }
'@
}

function Get-HandbookPrintCss {
    return @'
@page { size: A4; margin: 0; }
.page {
  width: 210mm;
  height: 297mm;
  padding: 18mm;
  overflow: hidden;
  background: #ffffff;
  page-break-after: always;
}
.page:last-child { page-break-after: auto; }
'@
}

function Get-HandbookScreenCss {
    return @'
html, body { width: 1240px; height: 1754px; }
.page {
  width: 1240px;
  height: 1754px;
  padding: 106px;
  overflow: hidden;
  background: #ffffff;
}
'@
}

function New-HandbookHtmlDocument {
    param(
        [string[]]$PageHtmlBlocks,
        [string]$ExtraCss,
        [string]$Title
    )
    $safeTitle = $Title.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;')
    $css = (Get-HandbookBaseCss) + "`n" + $ExtraCss
    $pageDivs = ($PageHtmlBlocks | ForEach-Object { "<div class=`"page`">`n$_`n</div>" }) -join "`n"
    $html = @"
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8" />
<title>$safeTitle</title>
<style>
$css
</style>
</head>
<body>
$pageDivs
</body>
</html>
"@
    return $html
}

# =====================================================================================
# Edge discovery + headless invocation with the --headless=new / --headless fallback.
# =====================================================================================
function Resolve-EdgePath {
    param([string]$Explicit)

    if ($Explicit) {
        if (Test-Path -LiteralPath $Explicit) { return (Resolve-Path -LiteralPath $Explicit).Path }
        throw "-EdgePath was given but not found: $Explicit"
    }

    $candidates = New-Object System.Collections.Generic.List[string]
    $pf86 = ${env:ProgramFiles(x86)}
    if ($pf86) { $candidates.Add((Join-Path $pf86 'Microsoft\Edge\Application\msedge.exe')) }
    if ($env:ProgramFiles) { $candidates.Add((Join-Path $env:ProgramFiles 'Microsoft\Edge\Application\msedge.exe')) }

    foreach ($c in $candidates) {
        if (Test-Path -LiteralPath $c) { return (Resolve-Path -LiteralPath $c).Path }
    }

    # Last resort: msedge.exe discoverable on PATH. Get-Command can return more than one match
    # (e.g. multiple installs on PATH) as an array, so take the first explicitly before reading
    # .Source - reading .Source off an array would fail rather than yield a single path string.
    $onPath = Get-Command 'msedge.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($onPath) { return $onPath.Source }

    throw "Microsoft Edge (msedge.exe) not found under Program Files (x86|64) or PATH. Pass -EdgePath to point at it explicitly."
}

# Cache which headless flag this machine's Edge accepts so we don't retry the losing one for
# every single page - a modern Edge always wins on --headless=new and we learn that once.
$script:EdgeHeadlessMode = $null

function Invoke-EdgeHeadless {
    param(
        [Parameter(Mandatory)] [string]$EdgeExe,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$ExpectedOutput,
        [int]$MinBytes = 1
    )

    $modes = New-Object System.Collections.Generic.List[string]
    if ($script:EdgeHeadlessMode) {
        $modes.Add($script:EdgeHeadlessMode)
    } else {
        $modes.Add('--headless=new')
        $modes.Add('--headless')
    }

    $lastExit = -1
    $lastStdErr = ''
    foreach ($mode in $modes) {
        if (Test-Path -LiteralPath $ExpectedOutput) {
            Remove-Item -LiteralPath $ExpectedOutput -Force -ErrorAction SilentlyContinue
        }
        $stdOutFile = [System.IO.Path]::GetTempFileName()
        $stdErrFile = [System.IO.Path]::GetTempFileName()
        $fullArgs = @($mode) + $Arguments
        try {
            $proc = Start-Process -FilePath $EdgeExe -ArgumentList $fullArgs -NoNewWindow -Wait -PassThru -RedirectStandardOutput $stdOutFile -RedirectStandardError $stdErrFile
            $lastExit = $proc.ExitCode
            $lastStdErr = Get-Content -LiteralPath $stdErrFile -Raw -ErrorAction SilentlyContinue
        } catch {
            $lastExit = -1
            $lastStdErr = $_.Exception.Message
        } finally {
            Remove-Item -LiteralPath $stdOutFile -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $stdErrFile -Force -ErrorAction SilentlyContinue
        }

        $producedOk = (Test-Path -LiteralPath $ExpectedOutput) -and ((Get-Item -LiteralPath $ExpectedOutput).Length -ge $MinBytes)
        if ($lastExit -eq 0 -and $producedOk) {
            $script:EdgeHeadlessMode = $mode
            return [pscustomobject]@{ Success = $true; Mode = $mode; ExitCode = $lastExit; StdErr = $lastStdErr }
        }
    }

    return [pscustomobject]@{ Success = $false; Mode = $modes[$modes.Count - 1]; ExitCode = $lastExit; StdErr = $lastStdErr }
}

# =====================================================================================
# Main
# =====================================================================================
Write-Host "==> Rendering Designer Handbook" -ForegroundColor Cyan
Write-Host "    source: $Source"
Write-Host "    outdir: $OutDir"

if (-not (Test-Path -LiteralPath $Source)) {
    throw "Source markdown not found: $Source"
}

$EdgeExe = Resolve-EdgePath -Explicit $EdgePath
Write-Host "    edge:   $EdgeExe"

$mdText = Get-Content -LiteralPath $Source -Raw -Encoding UTF8
if ($null -eq $mdText) { $mdText = '' }
$lines = $mdText -split '\r?\n'
$pages = ConvertFrom-HandbookMarkdown -Lines $lines
if ($pages.Count -eq 0) {
    throw "No renderable pages found in '$Source' - the file is empty or contains only <!-- page --> markers with no content between them."
}
$markerCount = @($lines | Where-Object { $_.Trim() -eq '<!-- page -->' }).Count
Write-Host "    pages:  $($pages.Count) (from $markerCount page marker(s))"

$BaseName = [System.IO.Path]::GetFileNameWithoutExtension($Source)
$PagesDir = Join-Path $OutDir 'pages'
# Pinned, not derived from $BaseName: HandbookViewer.cs hard-matches this exact filename
# (PdfFileName = "HUD-Designer-Handbook.pdf") to find the PDF to open, regardless of -Source.
$PdfPath  = Join-Path $OutDir 'HUD-Designer-Handbook.pdf'

if (-not (Test-Path -LiteralPath $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }
if (Test-Path -LiteralPath $PagesDir) { Remove-Item -LiteralPath $PagesDir -Recurse -Force }
New-Item -ItemType Directory -Path $PagesDir -Force | Out-Null
if (Test-Path -LiteralPath $PdfPath) { Remove-Item -LiteralPath $PdfPath -Force }

$TempDir = Join-Path $env:TEMP ('uia-handbook-' + [guid]::NewGuid().ToString('N'))
$TempProfileDir = Join-Path $TempDir 'profile'
New-Item -ItemType Directory -Path $TempProfileDir -Force | Out-Null

$failures = New-Object System.Collections.Generic.List[string]

try {
    # ---- PDF: one combined document, paginated by CSS page-break-after ----
    Write-Host ""
    Write-Host "==> Rendering PDF" -ForegroundColor Cyan
    $printCss  = (Get-HandbookBaseCss) + "`n" + (Get-HandbookPrintCss)
    $printHtml = New-HandbookHtmlDocument -PageHtmlBlocks $pages -ExtraCss $printCss -Title $BaseName
    $printHtmlPath = Join-Path $TempDir 'combined.html'
    Set-Content -LiteralPath $printHtmlPath -Value $printHtml -Encoding utf8
    $printUri = ([System.Uri]$printHtmlPath).AbsoluteUri

    # NOTE: --user-data-dir points at an isolated temp profile. Without it, a headless launch
    # can silently no-op by forwarding to an already-running normal Edge window on the same
    # default profile instead of actually rendering anything.
    #
    # NOTE: the repo path itself contains a space ("Stationeers UI Ascended"), and PowerShell's
    # own array-argument marshalling to a native process does NOT reliably quote an individual
    # "--flag=value with spaces" element - confirmed by hand: an unquoted value gets silently
    # split at the space and Edge fails with "Multiple targets are not supported in headless
    # mode." So every flag carrying a filesystem path is quoted AROUND THE VALUE ourselves
    # (--flag="value") - safe (and a no-op) even when the value has no spaces at all.
    $pdfArgs = @(
        '--disable-gpu',
        '--no-first-run',
        ('--user-data-dir="' + $TempProfileDir + '"'),
        ('--print-to-pdf="' + $PdfPath + '"'),
        '--no-pdf-header-footer',
        $printUri
    )
    $pdfResult = Invoke-EdgeHeadless -EdgeExe $EdgeExe -Arguments $pdfArgs -ExpectedOutput $PdfPath -MinBytes 1024

    if ($pdfResult.Success) {
        $pdfKb = '{0:N1}' -f ((Get-Item -LiteralPath $PdfPath).Length / 1KB)
        Write-Host "    OK  $PdfPath ($pdfKb KB, mode $($pdfResult.Mode))" -ForegroundColor Green
    } else {
        $failures.Add("PDF render failed (exit $($pdfResult.ExitCode)): $PdfPath")
        Write-Host "    FAILED to render PDF (exit $($pdfResult.ExitCode))" -ForegroundColor Red
        if ($pdfResult.StdErr) { Write-Host "    stderr: $($pdfResult.StdErr)" -ForegroundColor DarkYellow }
    }

    # ---- PNGs: one screenshot per page ----
    Write-Host ""
    Write-Host "==> Rendering $($pages.Count) page image(s)" -ForegroundColor Cyan
    $padWidth = "$($pages.Count)".Length
    if ($padWidth -lt 2) { $padWidth = 2 }
    $screenCss = (Get-HandbookBaseCss) + "`n" + (Get-HandbookScreenCss)

    for ($p = 0; $p -lt $pages.Count; $p++) {
        $pageNum  = $p + 1
        $pageTag  = 'page-' + $pageNum.ToString('D' + $padWidth)
        $pageHtml = New-HandbookHtmlDocument -PageHtmlBlocks @($pages[$p]) -ExtraCss $screenCss -Title "$BaseName - $pageTag"
        $pageHtmlPath = Join-Path $TempDir "$pageTag.html"
        Set-Content -LiteralPath $pageHtmlPath -Value $pageHtml -Encoding utf8
        $pageUri = ([System.Uri]$pageHtmlPath).AbsoluteUri

        $pngPath = Join-Path $PagesDir "$pageTag.png"
        $pngArgs = @(
            '--disable-gpu',
            '--no-first-run',
            ('--user-data-dir="' + $TempProfileDir + '"'),
            '--hide-scrollbars',
            ('--screenshot="' + $pngPath + '"'),
            '--window-size=1240,1754',
            $pageUri
        )
        $pngResult = Invoke-EdgeHeadless -EdgeExe $EdgeExe -Arguments $pngArgs -ExpectedOutput $pngPath -MinBytes 10KB

        if ($pngResult.Success) {
            $pngKb = '{0:N1}' -f ((Get-Item -LiteralPath $pngPath).Length / 1KB)
            Write-Host "    OK  $pageTag.png ($pngKb KB, mode $($pngResult.Mode))" -ForegroundColor Green
        } else {
            $failures.Add("PNG render failed for ${pageTag}: exit $($pngResult.ExitCode)")
            Write-Host "    FAILED $pageTag.png (exit $($pngResult.ExitCode))" -ForegroundColor Red
            if ($pngResult.StdErr) { Write-Host "    stderr: $($pngResult.StdErr)" -ForegroundColor DarkYellow }
        }
    }
} finally {
    if (Test-Path -LiteralPath $TempDir) { Remove-Item -LiteralPath $TempDir -Recurse -Force -ErrorAction SilentlyContinue }
}

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "==> Handbook render FINISHED WITH ERRORS:" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "    - $f" -ForegroundColor Red }
    exit 1
}

Write-Host "==> Handbook rendered (Edge headless mode: $script:EdgeHeadlessMode)" -ForegroundColor Green
Write-Host "    $PdfPath"
Write-Host "    $PagesDir\  ($($pages.Count) page image(s))"
exit 0
