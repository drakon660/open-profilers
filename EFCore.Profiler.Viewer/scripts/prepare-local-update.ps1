param(
    [string]$Version = "",
    [string]$Runtime = "win-x64",
    [string]$Channel = "win",
    [string]$PackId = "EFCore.Profiler.Viewer",
    [string]$MainExe = "EFCore.Profiler.Viewer.exe",
    [string]$FeedDir = "C:\efcore-profiler",
    [bool]$CreateInstaller = $false
)

$ErrorActionPreference = "Stop"

function Assert-LastExitCode([string]$OperationName) {
    if ($LASTEXITCODE -ne 0) {
        throw "$OperationName failed with exit code $LASTEXITCODE."
    }
}

function Resolve-VersionFromNbgv([string]$ProjectDirectory) {
    $commands = @(
        @{ File = "nbgv"; Args = @("get-version", "-v", "NuGetPackageVersion") },
        @{ File = "dotnet"; Args = @("nbgv", "get-version", "-v", "NuGetPackageVersion") },
        @{ File = "dotnet"; Args = @("tool", "run", "nbgv", "--", "get-version", "-v", "NuGetPackageVersion") }
    )

    foreach ($command in $commands) {
        try {
            Push-Location $ProjectDirectory
            $result = & $command.File @($command.Args) 2>$null
            if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($result)) {
                return ($result | Select-Object -First 1).ToString().Trim()
            }
        }
        catch {
        }
        finally {
            Pop-Location
        }
    }

    return ""
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

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Resolve-VersionFromNbgv -ProjectDirectory $viewerDir
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Resolve-VersionFromGeneratedMetadata -ProjectPath $projectPath -ProjectDirectory $viewerDir -ConfigurationName "Release"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = "0.0.$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())"
}

$publishDir = Join-Path $artifactsRoot "publish\$Runtime\$Version"
$releaseDir = Join-Path $artifactsRoot "releases\$Runtime\$Version"

Write-Host "Preparing EF Core viewer local update..."
Write-Host "Version       : $Version"
Write-Host "Runtime       : $Runtime"
Write-Host "Channel       : $Channel"
Write-Host "Feed          : $FeedDir"
Write-Host "Publish Dir   : $publishDir"
Write-Host "Release Dir   : $releaseDir"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet command not found."
}

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw "vpk command not found."
}

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
New-Item -ItemType Directory -Force -Path $FeedDir | Out-Null

& dotnet publish $projectPath -c Release --self-contained -r $Runtime -p:RestoreIgnoreFailedSources=true -o $publishDir
Assert-LastExitCode "dotnet publish"

$mainExePath = Join-Path $publishDir $MainExe
if (-not (Test-Path $mainExePath)) {
    throw "Main executable not found at $mainExePath"
}

& vpk pack `
    --packId $PackId `
    --packVersion $Version `
    --packDir $publishDir `
    --mainExe $MainExe `
    --channel $Channel `
    --outputDir $releaseDir `
    $(if (-not $CreateInstaller) { "--noInst" })
Assert-LastExitCode "vpk pack"

$releaseFiles = Get-ChildItem -Path $releaseDir -File
if ($releaseFiles.Count -eq 0) {
    throw "No release artifacts were generated in $releaseDir"
}

& vpk upload local `
    --outputDir $releaseDir `
    --channel $Channel `
    --path $FeedDir `
    --regenerate
Assert-LastExitCode "vpk upload local"

Write-Host ""
Write-Host "EF Core viewer update feed ready:"
Write-Host "  $FeedDir"
