#Requires -Version 7
<#
.SYNOPSIS
Publishes one mod as a GitHub release that the TSMods loader can install.

.DESCRIPTION
Builds the mod in Release, reads its plugin version and game build stamp with the
tsmods CLI, then creates a release tagged <mod>-v<version> with the DLL attached.
The loader's "Get mods" view lists these releases. Refuses if the tag exists, so
bump <Version> in the mod's csproj first.

.EXAMPLE
./release.ps1 -Mod RunHud
.EXAMPLE
./release.ps1 -Mod RunHud -DryRun
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Mod,
    [string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Trials Survivors',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root "mods\TrialsSurvivors.$Mod\TrialsSurvivors.$Mod.csproj"
if (-not (Test-Path $project)) { throw "No mod project at $project" }

dotnet build $project -c Release --verbosity quiet -nowarn:NU1603 -p:GamePath="$GamePath"
if ($LASTEXITCODE -ne 0) { throw 'build failed' }

$dll = Join-Path $root "mods\TrialsSurvivors.$Mod\bin\Release\net6.0\TrialsSurvivors.$Mod.dll"
$info = dotnet run --project (Join-Path $root 'loader\TSMods.Cli') --verbosity quiet -- inspect $dll --json | ConvertFrom-Json
if (-not $info.plugin) { throw "$dll isn't a BepInEx plugin" }

$version = $info.plugin.version
$tag = "$($Mod.ToLowerInvariant())-v$version"
$notes = @"
$($info.plugin.name) $version

Built for Trials Survivors build $($info.stamp.gameBuildId) with BepInEx $($info.stamp.bepInExVersion).
Install with the TSMods loader (Get mods), or drop the DLL into BepInEx\plugins.
"@

gh release view $tag --repo aevv/trials-survivor-mods *> $null
if ($LASTEXITCODE -eq 0) { throw "Release $tag already exists. Bump <Version> in $project first." }

Write-Host "tag    $tag"
Write-Host "file   $dll"
Write-Host "sha256 $($info.sha256)"
Write-Host $notes
if ($DryRun) { Write-Host 'dry run: nothing published' -ForegroundColor Yellow; return }

gh release create $tag $dll --repo aevv/trials-survivor-mods --title "$($info.plugin.name) $version" --notes $notes
