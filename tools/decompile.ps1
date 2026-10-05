#Requires -Version 7
<#
.SYNOPSIS
Decompiles the dumped assemblies to a greppable C# source tree.

.DESCRIPTION
Produces dumps\cs\<assembly>\ with one .cs per type, so finding what to patch is
a grep rather than another dump-and-inspect cycle.

Two sources, because they answer different questions:

  game    - the Cpp2IL dump of the shipped assembly. This is the game as written:
            real field names, offsets, signatures, inheritance. Method bodies are
            mostly 'throw null' (they're native), so read this for structure.

  interop - the Il2CppInterop assemblies BepInEx generates. This is the game as a
            mod sees it: which members are public, what the property wrappers are
            called, what you can actually reference from a plugin. Check a member
            here before writing a patch against it.

Requires tools\dump.ps1 to have run (for 'game'), and BepInEx installed plus the
game launched once (for 'interop').

.EXAMPLE
./decompile.ps1
.EXAMPLE
./decompile.ps1 -Source interop -Assemblies Assembly-CSharp
#>
[CmdletBinding()]
param(
    [string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Trials Survivors',
    [ValidateSet('game', 'interop', 'both')]
    [string]$Source = 'both',
    [string[]]$Assemblies = @('Assembly-CSharp', 'Assembly-CSharp-firstpass'),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

$ilspy = Get-Command ilspycmd -EA SilentlyContinue
if (-not $ilspy) {
    $local = Join-Path $env:USERPROFILE '.dotnet\tools\ilspycmd.exe'
    if (Test-Path $local) { $ilspy = $local }
    else { throw 'ilspycmd not found. Run: dotnet tool install -g ilspycmd' }
}
else { $ilspy = $ilspy.Source }

$sources = [ordered]@{}
if ($Source -in 'game', 'both') { $sources['game'] = Join-Path $root 'dumps\dll_il_recovery' }
if ($Source -in 'interop', 'both') { $sources['interop'] = Join-Path $GamePath 'BepInEx\interop' }

foreach ($kind in $sources.Keys) {
    $from = $sources[$kind]
    if (-not (Test-Path $from)) {
        Write-Warning "skipping '$kind': '$from' does not exist."
        continue
    }

    foreach ($name in $Assemblies) {
        $dll = Join-Path $from "$name.dll"
        if (-not (Test-Path $dll)) { Write-Warning "skipping '$kind\$name': not found."; continue }

        $outDir = Join-Path $root "dumps\cs\$kind\$name"
        if (Test-Path $outDir) {
            if (-not $Force) { Write-Host "exists, skipping: dumps\cs\$kind\$name (pass -Force to redo)" -ForegroundColor DarkGray; continue }
            Remove-Item $outDir -Recurse -Force
        }
        New-Item $outDir -ItemType Directory -Force | Out-Null

        Write-Host "decompiling $kind\$name ..." -ForegroundColor Cyan
        # -p writes a project with one file per type; -r points at the sibling
        # assemblies so cross-assembly types resolve to names, not error nodes.
        & $ilspy -p -o $outDir -r $from $dll
        if ($LASTEXITCODE -ne 0) { throw "ilspycmd failed on $dll with $LASTEXITCODE." }

        $files = (Get-ChildItem $outDir -Recurse -Filter *.cs).Count
        Write-Host "  $files files -> dumps\cs\$kind\$name" -ForegroundColor Green
    }
}

Write-Host @'

grep the result, e.g.:
  rg -n "_limitDetectionCount" dumps/cs/game
  rg -n "class SS_Effect" dumps/cs/game --files-with-matches
'@ -ForegroundColor DarkGray
