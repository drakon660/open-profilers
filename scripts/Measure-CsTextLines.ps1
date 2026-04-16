[CmdletBinding()]
param(
    [string]$Path,
    [string]$OutputPath,
    [string[]]$ExcludeDirectories = @(".git", "bin", "obj")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

if ([string]::IsNullOrWhiteSpace($Path)) {
    $Path = Join-Path $scriptRoot ".."
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $scriptRoot "..\reports\cs-line-count-report.html"
}

function Get-ResolvedPath {
    param(
        [Parameter(Mandatory)]
        [string]$TargetPath
    )

    return (Resolve-Path -LiteralPath $TargetPath).Path
}

function Get-RelativePath {
    param(
        [Parameter(Mandatory)]
        [string]$BasePath,
        [Parameter(Mandatory)]
        [string]$FullPath
    )

    $normalizedBasePath = [System.IO.Path]::GetFullPath($BasePath)
    if (-not $normalizedBasePath.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $normalizedBasePath += [System.IO.Path]::DirectorySeparatorChar
    }

    $baseUri = [System.Uri]$normalizedBasePath
    $fileUri = [System.Uri]([System.IO.Path]::GetFullPath($FullPath))
    $relativeUri = $baseUri.MakeRelativeUri($fileUri)

    return [System.Uri]::UnescapeDataString($relativeUri.ToString()).Replace("/", [System.IO.Path]::DirectorySeparatorChar)
}

function Test-IsExcluded {
    param(
        [Parameter(Mandatory)]
        [string]$RelativePath,
        [Parameter(Mandatory)]
        [string[]]$ExcludedNames
    )

    $segments = $RelativePath -split '[\\/]'
    return $segments | Where-Object { $ExcludedNames -contains $_ } | Select-Object -First 1
}

function Get-TextLineCount {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath
    )

    $count = 0
    foreach ($line in [System.IO.File]::ReadLines($FilePath)) {
        if (-not [string]::IsNullOrWhiteSpace($line)) {
            $count++
        }
    }

    return $count
}

$resolvedBasePath = Get-ResolvedPath -TargetPath $Path
$resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [System.IO.Path]::GetDirectoryName($resolvedOutputPath)

if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
}

$items = foreach ($file in Get-ChildItem -LiteralPath $resolvedBasePath -Recurse -File -Filter "*.cs") {
    $relativePath = Get-RelativePath -BasePath $resolvedBasePath -FullPath $file.FullName
    if (Test-IsExcluded -RelativePath $relativePath -ExcludedNames $ExcludeDirectories) {
        continue
    }

    $textLineCount = Get-TextLineCount -FilePath $file.FullName
    if ($textLineCount -eq 0) {
        continue
    }

    [pscustomobject]@{
        RelativePath = $relativePath.Replace("\", "/")
        TextLineCount = $textLineCount
        SizeKb = [math]::Round($file.Length / 1KB, 2)
    }
}

$sortedItems = $items | Sort-Object -Property @(
    @{ Expression = "TextLineCount"; Descending = $true },
    @{ Expression = "RelativePath"; Descending = $false }
)
$totalFiles = @($sortedItems).Count
$totalTextLines = ($sortedItems | Measure-Object -Property TextLineCount -Sum).Sum
$generatedAt = Get-Date -Format "yyyy-MM-dd HH:mm:ss zzz"
$maxLineCount = if ($totalFiles -gt 0) { ($sortedItems | Select-Object -First 1).TextLineCount } else { 0 }

$rows = foreach ($item in $sortedItems) {
    $width = if ($maxLineCount -gt 0) {
        [math]::Round(($item.TextLineCount / $maxLineCount) * 100, 2)
    }
    else {
        0
    }

    $encodedPath = [System.Net.WebUtility]::HtmlEncode($item.RelativePath)

    @"
<tr>
    <td class="path">$encodedPath</td>
    <td class="count">$($item.TextLineCount)</td>
    <td class="size">$($item.SizeKb)</td>
    <td class="bar-cell">
        <div class="bar-track">
            <div class="bar-fill" style="width: $width%;"></div>
        </div>
    </td>
</tr>
"@
}

$html = @"
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <title>C# Text Line Count Report</title>
    <style>
        :root {
            color-scheme: light;
            --bg: #f5f7fb;
            --panel: #ffffff;
            --ink: #1b2430;
            --muted: #5b6678;
            --accent: #0f766e;
            --accent-soft: #dff6f2;
            --border: #d8e0ec;
            --shadow: 0 20px 45px rgba(15, 23, 42, 0.08);
        }

        * {
            box-sizing: border-box;
        }

        body {
            margin: 0;
            padding: 32px;
            font-family: "Segoe UI", Tahoma, sans-serif;
            background:
                radial-gradient(circle at top right, rgba(15, 118, 110, 0.18), transparent 24%),
                linear-gradient(180deg, #eef4fb 0%, var(--bg) 100%);
            color: var(--ink);
        }

        .page {
            max-width: 1200px;
            margin: 0 auto;
        }

        .hero {
            background: linear-gradient(135deg, #12344d 0%, #0f766e 100%);
            color: white;
            border-radius: 24px;
            padding: 28px 32px;
            box-shadow: var(--shadow);
        }

        .hero h1 {
            margin: 0 0 10px;
            font-size: 32px;
            line-height: 1.1;
        }

        .hero p {
            margin: 0;
            color: rgba(255, 255, 255, 0.84);
        }

        .stats {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
            gap: 16px;
            margin: 20px 0 28px;
        }

        .card {
            background: var(--panel);
            border: 1px solid var(--border);
            border-radius: 20px;
            padding: 18px 20px;
            box-shadow: var(--shadow);
        }

        .card .label {
            display: block;
            font-size: 12px;
            font-weight: 700;
            text-transform: uppercase;
            letter-spacing: 0.08em;
            color: var(--muted);
            margin-bottom: 8px;
        }

        .card .value {
            font-size: 30px;
            font-weight: 700;
            color: var(--ink);
        }

        .card .hint {
            margin-top: 6px;
            font-size: 13px;
            color: var(--muted);
        }

        .table-wrap {
            background: var(--panel);
            border: 1px solid var(--border);
            border-radius: 24px;
            box-shadow: var(--shadow);
            overflow: hidden;
        }

        table {
            width: 100%;
            border-collapse: collapse;
        }

        thead {
            background: #f2f7fb;
        }

        th,
        td {
            padding: 16px 18px;
            border-bottom: 1px solid var(--border);
            vertical-align: middle;
        }

        th {
            text-align: left;
            font-size: 12px;
            text-transform: uppercase;
            letter-spacing: 0.08em;
            color: var(--muted);
        }

        tbody tr:hover {
            background: #f9fbfd;
        }

        tbody tr:last-child td {
            border-bottom: none;
        }

        .path {
            width: 48%;
            font-family: Consolas, "Cascadia Mono", monospace;
            font-size: 13px;
        }

        .count,
        .size {
            white-space: nowrap;
            font-variant-numeric: tabular-nums;
        }

        .bar-cell {
            width: 28%;
        }

        .bar-track {
            width: 100%;
            height: 12px;
            background: #e7eef7;
            border-radius: 999px;
            overflow: hidden;
        }

        .bar-fill {
            height: 100%;
            border-radius: 999px;
            background: linear-gradient(90deg, #14b8a6 0%, #0f766e 100%);
        }

        .footer {
            margin-top: 14px;
            color: var(--muted);
            font-size: 13px;
        }

        @media (max-width: 900px) {
            body {
                padding: 18px;
            }

            .hero,
            .table-wrap,
            .card {
                border-radius: 18px;
            }

            th,
            td {
                padding: 12px 14px;
            }
        }
    </style>
</head>
<body>
    <div class="page">
        <section class="hero">
            <h1>C# Text Line Count Report</h1>
            <p>Counts only lines that contain non-whitespace text in <code>*.cs</code> files.</p>
        </section>

        <section class="stats">
            <article class="card">
                <span class="label">Scanned Root</span>
                <div class="value">$([System.Net.WebUtility]::HtmlEncode($resolvedBasePath))</div>
                <div class="hint">Directories excluded: $([string]::Join(", ", $ExcludeDirectories))</div>
            </article>
            <article class="card">
                <span class="label">Files</span>
                <div class="value">$totalFiles</div>
                <div class="hint">Only files with at least one text line are listed.</div>
            </article>
            <article class="card">
                <span class="label">Total Text Lines</span>
                <div class="value">$totalTextLines</div>
                <div class="hint">Sorted descending by non-empty line count.</div>
            </article>
            <article class="card">
                <span class="label">Generated</span>
                <div class="value">$generatedAt</div>
                <div class="hint">Local time on this machine.</div>
            </article>
        </section>

        <section class="table-wrap">
            <table>
                <thead>
                    <tr>
                        <th>File</th>
                        <th>Text Lines</th>
                        <th>Size KB</th>
                        <th>Visual</th>
                    </tr>
                </thead>
                <tbody>
                    $($rows -join [Environment]::NewLine)
                </tbody>
            </table>
        </section>

        <div class="footer">
            Report written to $([System.Net.WebUtility]::HtmlEncode($resolvedOutputPath))
        </div>
    </div>
</body>
</html>
"@

[System.IO.File]::WriteAllText($resolvedOutputPath, $html, [System.Text.Encoding]::UTF8)

Write-Host "HTML report created: $resolvedOutputPath"
Write-Host "Files listed: $totalFiles"
Write-Host "Total text lines: $totalTextLines"
