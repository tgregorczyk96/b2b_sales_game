<#
.SYNOPSIS
    Builds the Sales Engine (netstandard2.1) from its own repository and copies the result into this Unity project.

.DESCRIPTION
    The engine stays a separate, Unity-independent repository. This script is the only bridge:
      - SalesEngine.dll, SalesEngine.Content.dll, SalesEngine.AI.dll  -> Assets/Plugins/SalesEngine/
      - content/**/*.json (curated scenario content)                  -> Assets/StreamingAssets/SalesEngine/content/
      - the engine commit the files were built from                   -> Assets/Plugins/SalesEngine/ENGINE_VERSION.txt
    No NuGet dependencies are copied: the only one (System.Text.Json 8) is part of Unity's BCL extensions.
    The script fails if a built assembly references anything outside that allowed set.

.PARAMETER EnginePath
    Root of the sales_engine repository. Defaults to a sibling checkout: <Projekte>\sales_engine.
#>
[CmdletBinding()]
param(
    [string]$EnginePath = (Join-Path $PSScriptRoot '..\..\..\sales_engine'),
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$projectRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$EnginePath = Resolve-Path $EnginePath
$pluginDir = Join-Path $projectRoot 'Assets\Plugins\SalesEngine'
$contentDir = Join-Path $projectRoot 'Assets\StreamingAssets\SalesEngine\content'
$assemblies = 'SalesEngine', 'SalesEngine.Content', 'SalesEngine.AI'

# Assemblies Unity (Mono, .NET Standard 2.1 + BCL extensions) provides itself.
$allowedReferences = @(
    'netstandard', 'System.Text.Json', 'System.Text.Encodings.Web', 'System.Memory',
    'Microsoft.Bcl.AsyncInterfaces', 'System.Runtime.CompilerServices.Unsafe'
) + $assemblies

foreach ($project in 'SalesEngine.Content', 'SalesEngine.AI') {
    $csproj = Join-Path $EnginePath "src\$project\$project.csproj"
    & dotnet build $csproj -f netstandard2.1 -c $Configuration --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed for $project" }
}

New-Item -ItemType Directory -Force $pluginDir | Out-Null
foreach ($name in $assemblies) {
    $built = Join-Path $EnginePath "src\$name\bin\$Configuration\netstandard2.1\$name.dll"
    $references = [Reflection.Assembly]::ReflectionOnlyLoadFrom($built).GetReferencedAssemblies() | ForEach-Object Name
    $unexpected = $references | Where-Object { $allowedReferences -notcontains $_ }
    if ($unexpected) { throw "$name.dll references assemblies Unity does not provide: $($unexpected -join ', ')" }

    Copy-Item $built $pluginDir -Force
    Copy-Item ([IO.Path]::ChangeExtension($built, '.pdb')) $pluginDir -Force
}

# Overwrite in place (keeps Unity's .meta files and GUIDs stable); remove only JSON files the engine no longer has.
$engineContent = Join-Path $EnginePath 'content'
$synced = @{}
Get-ChildItem $engineContent -Recurse -Filter *.json | ForEach-Object {
    $relative = $_.FullName.Substring($engineContent.Length + 1)
    $target = Join-Path $contentDir $relative
    New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
    Copy-Item $_.FullName $target -Force
    $synced[$relative] = $true
}
Get-ChildItem $contentDir -Recurse -Filter *.json | Where-Object { -not $synced[$_.FullName.Substring($contentDir.Length + 1)] } | ForEach-Object {
    Remove-Item $_.FullName, "$($_.FullName).meta" -Force -ErrorAction SilentlyContinue
}

$commit = (& git -C $EnginePath rev-parse HEAD).Trim()
$branch = (& git -C $EnginePath rev-parse --abbrev-ref HEAD).Trim()
$dirty = if (& git -C $EnginePath status --porcelain -- src content) { ' (uncommitted changes)' } else { '' }
@(
    "Sales Engine build copied by Tools/Sync-SalesEngine.ps1 - do not edit the DLLs or content by hand."
    "repository: $((& git -C $EnginePath remote get-url origin).Trim())"
    "branch:     $branch"
    "commit:     $commit$dirty"
    "target:     netstandard2.1 ($Configuration)"
) | Set-Content (Join-Path $pluginDir 'ENGINE_VERSION.txt') -Encoding utf8

Write-Host "Sales Engine $branch@$($commit.Substring(0, 7))$dirty synced to $pluginDir"
