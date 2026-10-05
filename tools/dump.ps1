#Requires -Version 7
<#
.SYNOPSIS
Dumps Trials Survivors' IL2CPP assemblies to decompilable .NET DLLs.

.DESCRIPTION
Runs Cpp2IL over GameAssembly.dll + global-metadata.dat and writes recovered
assemblies to dumps\<format>\. Use dll_il_recovery (default) for assemblies you
can read in ILSpy and compile a mod against; use isil when IL recovery gives up
on a method and you need the raw instruction stream.

.EXAMPLE
./dump.ps1
.EXAMPLE
./dump.ps1 -Format isil
#>
[CmdletBinding()]
param(
    [string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Trials Survivors',
    [ValidateSet('dll_il_recovery', 'dll_default', 'dummydll', 'isil', 'diffable-cs')]
    [string]$Format = 'dll_il_recovery',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$cpp2il = Join-Path $PSScriptRoot 'cpp2il.exe'
$outDir = Join-Path $root "dumps\$Format"

if (-not (Test-Path $cpp2il)) {
    throw "cpp2il.exe not found. Run: gh release download 2022.1.0-pre-release.21 --repo SamboyCoding/Cpp2IL --pattern 'Cpp2IL-*-Windows.exe' --output '$cpp2il'"
}
if (-not (Test-Path (Join-Path $GamePath 'GameAssembly.dll'))) {
    throw "GameAssembly.dll not found under '$GamePath'."
}

if (Test-Path $outDir) {
    if (-not $Force) { throw "'$outDir' already exists. Pass -Force to overwrite." }
    Remove-Item $outDir -Recurse -Force
}

$buildGuid = (Select-String -Path (Join-Path $GamePath '*_Data\boot.config') -Pattern 'build-guid=(.+)').Matches.Groups[1].Value
Write-Host "game build-guid: $buildGuid" -ForegroundColor Cyan

& $cpp2il `
    --game-path $GamePath `
    --output-as $Format `
    --output-to $outDir `
    --use-processor attributeinjector

if ($LASTEXITCODE -ne 0) { throw "Cpp2IL exited with $LASTEXITCODE." }

$manifest = [ordered]@{
    buildGuid    = $buildGuid
    unityVersion = (Get-Item (Join-Path $GamePath '*.exe') | Where-Object Name -notmatch 'CrashHandler|crashpad').VersionInfo.ProductVersion
    format       = $Format
    cpp2il       = (& $cpp2il --version 2>$null | Select-String -Pattern '^Version (.+)$').Matches.Groups[1].Value
    dumpedUtc    = (Get-Date).ToUniversalTime().ToString('o')
}
$manifest | ConvertTo-Json | Set-Content (Join-Path $outDir 'tmods-dump.json')

Write-Host "`ndumped to $outDir" -ForegroundColor Green
Get-ChildItem $outDir -Filter 'Assembly-CSharp*' | Format-Table Name, Length
