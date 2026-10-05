#Requires -Version 7
<#
.SYNOPSIS
Installs (or removes) BepInEx 6 bleeding-edge IL2CPP into the Trials Survivors folder.

.DESCRIPTION
Trials Survivors is a Unity 6 / IL2CPP build, so it needs the IL2CPP line of
BepInEx 6 rather than any BepInEx 5 release. This extracts the downloaded
bleeding-edge zip over the game folder and leaves every game file untouched —
uninstalling is just deleting what we added, which -Uninstall does.

Run the game once after installing: BepInEx generates interop assemblies into
BepInEx\interop on first launch, which is what mods compile against.

.EXAMPLE
./install-bepinex.ps1
.EXAMPLE
./install-bepinex.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Trials Survivors',
    [string]$Zip,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'

# Everything BepInEx adds to the game folder, so we can cleanly reverse it.
$added = @('BepInEx', 'dotnet', 'doorstop_config.ini', '.doorstop_version', 'changelog.txt', 'winhttp.dll')

if (-not (Test-Path (Join-Path $GamePath 'GameAssembly.dll'))) {
    throw "'$GamePath' does not look like the Trials Survivors install (no GameAssembly.dll)."
}

if ($Uninstall) {
    foreach ($item in $added) {
        $p = Join-Path $GamePath $item
        if (Test-Path $p) { Remove-Item $p -Recurse -Force; Write-Host "removed $item" }
    }
    Write-Host 'BepInEx removed; game files untouched.' -ForegroundColor Green
    return
}

if (-not $Zip) {
    $Zip = Get-ChildItem (Join-Path $PSScriptRoot 'BepInEx-Unity.IL2CPP-win-x64-*.zip'), (Join-Path $PSScriptRoot 'bepinex-il2cpp-*.zip') -EA SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $Zip -or -not (Test-Path $Zip)) {
    throw 'No BepInEx IL2CPP zip found in tools\. Download one from https://builds.bepinex.dev/projects/bepinex_be (BepInEx-Unity.IL2CPP-win-x64).'
}

# Fail early and loudly rather than half-extracting, since the default Steam
# library lives under Program Files and may need an elevated shell.
$probe = Join-Path $GamePath '.tmods-write-probe'
try { New-Item $probe -ItemType File -Force | Out-Null; Remove-Item $probe -Force }
catch { throw "Cannot write to '$GamePath'. Re-run this script from an elevated terminal." }

Write-Host "installing $(Split-Path $Zip -Leaf)" -ForegroundColor Cyan
Expand-Archive -Path $Zip -DestinationPath $GamePath -Force

Write-Host "`ninstalled. next: launch the game once so BepInEx writes BepInEx\interop," -ForegroundColor Green
Write-Host 'then build a mod against those assemblies.'
