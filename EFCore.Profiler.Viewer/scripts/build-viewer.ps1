param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = "",
    [switch]$SelfContained = $true
)

$ErrorActionPreference = "Stop"

function Assert-LastExitCode([string]$OperationName) {
    if ($LASTEXITCODE -ne 0) {
        throw "$OperationName failed with exit code $LASTEXITCODE."
    }
}

function Resolve-VersionFromGeneratedMetadata([string]$ProjectPath, [string]$ProjectDirectory, [string]$ConfigurationName) {
    & dotnet build $ProjectPath -c $ConfigurationName -p:RestoreIgnoreFailedSources=true 1>$null
    Assert-LastExitCode "dotnet build"

    $versionFile = Join-Path $ProjectDirectory "obj\$ConfigurationName\net10.0\EFCore.Profiler.Viewer.Version.cs"
    if (-not (Test-Path $versionFile)) {
        return ""
    }

    $match = Select-String -Path $versionFile -Pattern 'internal const string NuGetPackageVersion = "([^"]+)"' | Select-Object -First 1
    if ($match -and $match.Matches.Count -gt 0) {
        return $match.Matches[0].Groups[1].Value
    }

    return ""
}

$viewerDir = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $viewerDir "EFCore.Profiler.Viewer.csproj"
$artifactsRoot = Join-Path $viewerDir "artifacts"
$version = Resolve-VersionFromGeneratedMetadata -ProjectPath $projectPath -ProjectDirectory $viewerDir -ConfigurationName $Configuration

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $versionSegment = if ([string]::IsNullOrWhiteSpace($version)) { "unknown-version" } else { $version }
    $OutputDir = Join-Path $artifactsRoot "publish\$Runtime\$versionSegment"
}

Write-Host "Building EF Core viewer..."
Write-Host "Configuration : $Configuration"
Write-Host "Runtime       : $Runtime"
Write-Host "Version       : $version"
Write-Host "Output        : $OutputDir"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet command not found."
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$publishArgs = @(
    "publish",
    $projectPath,
    "-c", $Configuration,
    "-r", $Runtime,
    "-p:RestoreIgnoreFailedSources=true",
    "-o", $OutputDir
)

if ($SelfContained) {
    $publishArgs += "--self-contained"
}
else {
    $publishArgs += "--no-self-contained"
}

& dotnet @publishArgs
Assert-LastExitCode "dotnet publish"

Write-Host ""
Write-Host "EF Core viewer build ready:"
Write-Host "  $OutputDir"
