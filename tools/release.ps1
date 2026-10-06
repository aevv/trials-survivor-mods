#Requires -Version 7
<#
.SYNOPSIS
Publishes mods as GitHub releases that the TSMods loader can install.

.DESCRIPTION
Builds a mod in Release, reads its plugin version and game build stamp with the
tsmods CLI, then creates a release tagged <mod>-v<version> with the DLL attached.
The loader's "Get mods" view lists these releases.

With -Mod, refuses if the tag exists, so bump <Version> in the mod's csproj first.
Without -Mod, releases every mod whose <Version> has no tag yet and skips the rest.
Mods with uncommitted changes are refused, so a release always matches main.

Mods are built here rather than in CI because they compile against the game's
BepInEx\interop assemblies, which only exist on a machine with the game installed.

.EXAMPLE
./release.ps1 -Mod RunHud
.EXAMPLE
./release.ps1 -DryRun
#>
[CmdletBinding()]
param(
    [string]$Mod,
    [string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Trials Survivors',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$repo = 'aevv/trials-survivor-mods'
$root = Split-Path $PSScriptRoot -Parent

function Get-ProjectVersion([string]$project) {
    $node = Select-Xml -Path $project -XPath '//Version' | Select-Object -First 1
    if (-not $node) { throw "No <Version> in $project" }
    $node.Node.InnerText.Trim()
}

function Test-ReleaseExists([string]$tag) {
    gh release view $tag --repo $repo *> $null
    $LASTEXITCODE -eq 0
}

function Publish-Mod([string]$name) {
    $folder = Join-Path $root "mods\TrialsSurvivors.$name"
    $project = Join-Path $folder "TrialsSurvivors.$name.csproj"
    if (-not (Test-Path $project)) { throw "No mod project at $project" }

    $tag = "$($name.ToLowerInvariant())-v$(Get-ProjectVersion $project)"
    if (Test-ReleaseExists $tag) {
        if ($Mod) { throw "Release $tag already exists. Bump <Version> in $project first." }
        Write-Host "skip   $tag (already released)" -ForegroundColor DarkGray
        return
    }

    $dirty = git -C $root status --porcelain -- $folder
    if ($dirty) {
        $message = "$name has uncommitted changes, commit them before releasing:`n$($dirty -join "`n")"
        if (-not $DryRun) { throw $message }
        Write-Warning $message
    }

    dotnet build $project -c Release --verbosity quiet -nowarn:NU1603 -p:GamePath="$GamePath"
    if ($LASTEXITCODE -ne 0) { throw "build failed for $name" }

    $dll = Join-Path $folder "bin\Release\net6.0\TrialsSurvivors.$name.dll"
    $info = dotnet run --project (Join-Path $root 'loader\TSMods.Cli') --verbosity quiet -- inspect $dll --json | ConvertFrom-Json
    if (-not $info.plugin) { throw "$dll isn't a BepInEx plugin" }

    $version = $info.plugin.version
    $stampedTag = "$($name.ToLowerInvariant())-v$version"
    if ($stampedTag -ne $tag) { throw "$name's plugin reports $version but its csproj says otherwise ($tag)" }

    $notes = @"
$($info.plugin.name) $version

Built for Trials Survivors build $($info.stamp.gameBuildId) with BepInEx $($info.stamp.bepInExVersion).
Install with the TSMods loader (Get mods), or drop the DLL into BepInEx\plugins.
"@

    Write-Host "tag    $tag"
    Write-Host "file   $dll"
    Write-Host "sha256 $($info.sha256)"
    Write-Host $notes
    if ($DryRun) { Write-Host 'dry run: nothing published' -ForegroundColor Yellow; return }

    gh release create $tag $dll --repo $repo --title "$($info.plugin.name) $version" --notes $notes --latest=false
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed for $tag" }
}

if ($Mod) {
    Publish-Mod $Mod
    return
}

Get-ChildItem (Join-Path $root 'mods') -Directory -Filter 'TrialsSurvivors.*' |
    ForEach-Object { Publish-Mod ($_.Name -replace '^TrialsSurvivors\.', '') }
