param(
    [string]$Configuration = "Release",
    [string]$Destination = "C:\nugets",
    [ValidateSet("none", "patch", "minor", "major")]
    [string]$Bump = "patch",
    [string]$Version = "",
    [switch]$PackInternalPackages
)

$ErrorActionPreference = "Stop"

$clientProjectDir = $PSScriptRoot
$rootDir = Split-Path -Parent $clientProjectDir
$projectFile = Join-Path $clientProjectDir "Mongo.Profiler.Client.csproj"
$projectsToPack = @($projectFile)
$internalProjects = @(
    (Join-Path $rootDir "Mongo.Profiler\Mongo.Profiler.csproj"),
    (Join-Path $rootDir "Mongo.Profiler.Grpc\Mongo.Profiler.Grpc.csproj")
)
if ($PackInternalPackages) {
    $projectsToPack = $internalProjects + $projectsToPack
}
$artifactsDir = Join-Path $clientProjectDir "artifacts"
$packageOutDir = Join-Path $artifactsDir "nuget"

if (!(Test-Path -LiteralPath $projectFile)) {
    throw "Project file not found: $projectFile"
}

foreach ($project in ($internalProjects + $projectFile)) {
    if (!(Test-Path -LiteralPath $project)) {
        throw "Project file not found: $project"
    }
}

function Get-ChildTextNode {
    param(
        [System.Xml.XmlNode]$Parent,
        [string]$Name
    )

    foreach ($node in $Parent.ChildNodes) {
        if ($node.NodeType -eq [System.Xml.XmlNodeType]::Element -and $node.Name -eq $Name) {
            return $node
        }
    }

    return $null
}

function Resolve-Version {
    param(
        [string]$ProjectPath,
        [string]$RequestedVersion,
        [string]$BumpStrategy
    )

    [xml]$xml = Get-Content -LiteralPath $ProjectPath
    $projectNode = $xml.Project
    if ($null -eq $projectNode) {
        throw "Invalid project XML: missing <Project> root."
    }

    $propertyGroupNode = $null
    foreach ($child in $projectNode.ChildNodes) {
        if ($child.NodeType -eq [System.Xml.XmlNodeType]::Element -and $child.Name -eq "PropertyGroup") {
            $propertyGroupNode = $child
            break
        }
    }
    if ($null -eq $propertyGroupNode) {
        $propertyGroupNode = $xml.CreateElement("PropertyGroup")
        [void]$projectNode.AppendChild($propertyGroupNode)
    }

    $versionNode = Get-ChildTextNode -Parent $propertyGroupNode -Name "Version"
    if ($null -eq $versionNode) {
        $versionNode = $xml.CreateElement("Version")
        [void]$propertyGroupNode.AppendChild($versionNode)
    }

    $currentVersionText = $versionNode.InnerText
    if ([string]::IsNullOrWhiteSpace($currentVersionText)) {
        $currentVersionText = "1.0.0"
    }

    $targetVersionText = ""
    if (-not [string]::IsNullOrWhiteSpace($RequestedVersion)) {
        $targetVersionText = $RequestedVersion.Trim()
    }
    else {
        $currentVersion = $null
        if (-not [version]::TryParse($currentVersionText, [ref]$currentVersion)) {
            throw "Current project version '$currentVersionText' is not a valid numeric version."
        }

        switch ($BumpStrategy) {
            "none" { $targetVersionText = $currentVersion.ToString(3) }
            "patch" { $targetVersionText = [version]::new($currentVersion.Major, $currentVersion.Minor, $currentVersion.Build + 1).ToString(3) }
            "minor" { $targetVersionText = [version]::new($currentVersion.Major, $currentVersion.Minor + 1, 0).ToString(3) }
            "major" { $targetVersionText = [version]::new($currentVersion.Major + 1, 0, 0).ToString(3) }
            default { throw "Unknown bump strategy: $BumpStrategy" }
        }
    }

    $targetVersionParsed = $null
    if (-not [version]::TryParse($targetVersionText, [ref]$targetVersionParsed)) {
        throw "Target version '$targetVersionText' is not a valid numeric version."
    }

    $versionNode.InnerText = $targetVersionParsed.ToString(3)
    $xml.Save($ProjectPath)
    return $targetVersionParsed.ToString(3)
}

function Set-ProjectVersion {
    param(
        [string]$ProjectPath,
        [string]$TargetVersion
    )

    [xml]$xml = Get-Content -LiteralPath $ProjectPath
    $projectNode = $xml.Project
    if ($null -eq $projectNode) {
        throw "Invalid project XML in ${ProjectPath}: missing <Project> root."
    }

    $propertyGroupNode = $null
    foreach ($child in $projectNode.ChildNodes) {
        if ($child.NodeType -eq [System.Xml.XmlNodeType]::Element -and $child.Name -eq "PropertyGroup") {
            $propertyGroupNode = $child
            break
        }
    }
    if ($null -eq $propertyGroupNode) {
        $propertyGroupNode = $xml.CreateElement("PropertyGroup")
        [void]$projectNode.AppendChild($propertyGroupNode)
    }

    $versionNode = Get-ChildTextNode -Parent $propertyGroupNode -Name "Version"
    if ($null -eq $versionNode) {
        $versionNode = $xml.CreateElement("Version")
        [void]$propertyGroupNode.AppendChild($versionNode)
    }

    $versionNode.InnerText = $TargetVersion
    $xml.Save($ProjectPath)
}

$resolvedVersion = Resolve-Version -ProjectPath $projectFile -RequestedVersion $Version -BumpStrategy $Bump
Write-Host "Using package version: $resolvedVersion"

foreach ($project in ($internalProjects + $projectFile)) {
    Set-ProjectVersion -ProjectPath $project -TargetVersion $resolvedVersion
}

if (!(Test-Path -LiteralPath $Destination)) {
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
}

if (Test-Path -LiteralPath $packageOutDir) {
    Remove-Item -Path $packageOutDir -Recurse -Force
}
New-Item -ItemType Directory -Path $packageOutDir -Force | Out-Null

foreach ($project in $projectsToPack) {
    Write-Host "Packing $(Split-Path -Leaf $project)..."
    dotnet pack $project `
        --configuration $Configuration `
        --output $packageOutDir `
        /p:PackageVersion=$resolvedVersion `
        /p:ContinuousIntegrationBuild=true

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet pack failed for $project with exit code $LASTEXITCODE"
    }
}

$packages = Get-ChildItem -Path $packageOutDir -Filter "*.nupkg" -File |
    Where-Object { $_.Name -notlike "*.snupkg" }

if (-not $packages) {
    throw "No .nupkg package found in $packageOutDir"
}

foreach ($pkg in $packages) {
    $destinationFile = Join-Path $Destination $pkg.Name
    Copy-Item -Path $pkg.FullName -Destination $destinationFile -Force
    Write-Host "Copied: $($pkg.Name) -> $Destination"
}

Write-Host "Done."
