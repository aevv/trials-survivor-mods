[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('status', 'ensure-closed', 'stop', 'start', 'logs', 'errors', 'runs')]
    [string]$Action,
    [string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Trials Survivors',
    [int]$Lines = 40
)

$ErrorActionPreference = 'Stop'

$processName = 'Trials Survivors'
$steamAppId = 3762660
$bepinexLog = Join-Path $GamePath 'BepInEx\LogOutput.log'
$playerLog = Join-Path $env:USERPROFILE 'AppData\LocalLow\Angry Wisp\Trials Survivors\Player.log'
$runHistory = Join-Path $GamePath 'BepInEx\config\runhistory'

function Get-Game { Get-Process -Name $processName -ErrorAction SilentlyContinue }

switch ($Action) {
    'status' {
        $game = Get-Game
        if ($game) { "running (pid $($game.Id), started $($game.StartTime))" } else { 'not running' }
    }

    'ensure-closed' {
        if (Get-Game) {
            Write-Error "Trials Survivors is running and locks the plugin DLLs. Close it (or run 'task game:stop') and try again."
        }
    }

    'stop' {
        $game = Get-Game
        if (-not $game) { 'not running'; return }
        $game | Stop-Process
        $game | Wait-Process -Timeout 30
        'stopped'
    }

    'start' {
        if (Get-Game) { 'already running'; return }
        Start-Process "steam://rungameid/$steamAppId"
        'launching via Steam'
    }

    'logs' {
        if (-not (Test-Path $bepinexLog)) { "no BepInEx log at $bepinexLog"; return }
        Get-Content $bepinexLog |
            Where-Object { $_ -match 'Trials Survivors:|Exception|\[Error|\[Warning' -and $_ -notmatch 'unsupported (return type|parameter)' } |
            Select-Object -Last $Lines
    }

    'errors' {
        if (-not (Test-Path $playerLog)) { "no Unity log at $playerLog"; return }
        $content = Get-Content $playerLog
        $found = $false
        for ($i = 0; $i -lt $content.Count; $i++) {
            if ($content[$i] -match 'Exception') {
                $found = $true
                $content[$i..([Math]::Min($i + 6, $content.Count - 1))] |
                    Where-Object { $_ -notmatch 'Sentry|UnityEngine\.Logger:|UnityEngine\.Debug:' }
                ''
            }
        }
        if (-not $found) { 'no exceptions in Player.log' }
    }

    'runs' {
        if (-not (Test-Path $runHistory)) { 'no runs recorded yet'; return }
        Get-ChildItem $runHistory -Filter *.json | Sort-Object Name -Descending | ForEach-Object {
            $run = Get-Content $_.FullName -Raw | ConvertFrom-Json
            '{0}  {1,-11} {2,-16} {3,-10} {4,6} kills  {5,2} skills' -f `
                $run.EndedAtUtc.ToLocalTime().ToString('yyyy-MM-dd HH:mm'), $run.Result, $run.ClassName, $run.Difficulty, $run.Kills, @($run.Skills).Count
        }
    }
}
