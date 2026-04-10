param(
    [string]$Version = "",
    [string]$Runtime = "win-x64",
    [string]$Channel = "win",
    [string]$PackId = "Mongo.Profiler.Viewer",
    [string]$MainExe = "Mongo.Profiler.Viewer.exe",
    [string]$FeedDir = "C:\mongo-profiler",
    [bool]$CreateInstaller = $false
)

$ErrorActionPreference = "Stop"

function Assert-LastExitCode([string]$OperationName) {
    if ($LASTEXITCODE -ne 0) {
        throw "$OperationName failed with exit code $LASTEXITCODE."
    }
}

function Resolve-VersionFromNbgv([string]$ProjectDirectory) {
    if (-not (Get-Command nbgv -ErrorAction SilentlyContinue)) {
        return ""
    }

    Push-Location $ProjectDirectory
    try {
        $resolved = & nbgv get-version -v NuGetPackageVersion 2>$null
        if ($LASTEXITCODE -eq 0) {
            return ($resolved | Out-String).Trim()
        }

        return ""
    }
    finally {
        Pop-Location
    }
}

function Resolve-VersionFromGeneratedMetadata([string]$ProjectPath, [string]$ProjectDirectory) {
    & dotnet build $ProjectPath -c Release -p:RestoreIgnoreFailedSources=true 1>$null
    Assert-LastExitCode "dotnet build"

    $versionFile = Join-Path $ProjectDirectory "obj\Release\net10.0\Mongo.Profiler.Viewer.Version.cs"
    if (-not (Test-Path $versionFile)) {
        return ""
    }

    $match = Select-String -Path $versionFile -Pattern 'internal const string NuGetPackageVersion = "([^"]+)"' | Select-Object -First 1
    if ($match -and $match.Matches.Count -gt 0) {
        return $match.Matches[0].Groups[1].Value
    }

    $fallbackMatch = Select-String -Path $versionFile -Pattern 'internal const string AssemblyFileVersion = "([^"]+)"' | Select-Object -First 1
    if ($fallbackMatch -and $fallbackMatch.Matches.Count -gt 0) {
        return $fallbackMatch.Matches[0].Groups[1].Value
    }

    return ""
}

$viewerDir = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $viewerDir "Mongo.Profiler.Viewer.csproj"
$artifactsRoot = Join-Path $viewerDir "artifacts"
$publishDir = Join-Path $artifactsRoot "publish\$Runtime\$Version"
$releaseDir = Join-Path $artifactsRoot "releases\$Runtime\$Version"

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Resolve-VersionFromNbgv -ProjectDirectory $viewerDir
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Resolve-VersionFromGeneratedMetadata -ProjectPath $projectPath -ProjectDirectory $viewerDir
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = "0.0.$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())"
}

$publishDir = Join-Path $artifactsRoot "publish\$Runtime\$Version"
$releaseDir = Join-Path $artifactsRoot "releases\$Runtime\$Version"

Write-Host "Preparing local viewer update package..."
Write-Host "Version : $Version"
Write-Host "Runtime : $Runtime"
Write-Host "Channel : $Channel"
Write-Host "Feed    : $FeedDir"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet command not found."
}

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw "vpk command not found. Install it with: dotnet tool install -g vpk"
}

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
New-Item -ItemType Directory -Force -Path $FeedDir | Out-Null

dotnet publish $projectPath -c Release --self-contained -r $Runtime -p:RestoreIgnoreFailedSources=true -o $publishDir
Assert-LastExitCode "dotnet publish"

$mainExePath = Join-Path $publishDir $MainExe
if (-not (Test-Path $mainExePath)) {
    $exeCandidates = Get-ChildItem -Path $publishDir -Filter "*.exe" -File -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty Name

    $candidateText = if ($exeCandidates) { ($exeCandidates -join ", ") } else { "<none>" }
    throw "Publish completed but main exe '$MainExe' was not found in '$publishDir'. Found executables: $candidateText"
}

vpk pack `
    --packId $PackId `
    --packVersion $Version `
    --packDir $publishDir `
    --mainExe $MainExe `
    --channel $Channel `
    --outputDir $releaseDir `
    $(if (-not $CreateInstaller) { "--noInst" })
Assert-LastExitCode "vpk pack"

$releaseFiles = Get-ChildItem -Path $releaseDir -File -ErrorAction SilentlyContinue
if (-not $releaseFiles) {
    throw "No release files were generated in '$releaseDir'."
}

vpk upload local `
    --outputDir $releaseDir `
    --channel $Channel `
    --path $FeedDir `
    --regenerate
Assert-LastExitCode "vpk upload local"

$setupExe = Get-ChildItem -Path $releaseDir -Filter "*Setup.exe" -File -ErrorAction SilentlyContinue | Select-Object -First 1
if ($setupExe) {
    Write-Host ""
    Write-Host "Installer:"
    Write-Host "  $($setupExe.FullName)"
}

Write-Host ""
Write-Host "Local feed ready:"
Write-Host "  $FeedDir"
Write-Host ""
Write-Host "Set these variables before launching installed viewer:"
Write-Host "  `$env:MONGO_PROFILER_VIEWER_UPDATE_FEED_URL = '$FeedDir'"
Write-Host "  `$env:MONGO_PROFILER_VIEWER_UPDATE_CHANNEL = '$Channel'"
