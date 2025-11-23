# Auto-run watcher for FamidashEditor
# Watches the native-windows project files and restarts the editor when they change.
# Usage: .\scripts\auto-run.ps1 (run this in workspace root)

param(
    [string]$ProjectDir = "native-windows",
    [int]$DebounceMs = 500
)

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot '..')
$projectPath = Join-Path $repoRoot $ProjectDir

Set-Location -Path $repoRoot > $null

Write-Host "[auto-run] starting watcher (project: $ProjectDir) at $projectPath"

$proc = $null
$restartPending = $false
$lastEvent = Get-Date

function Start-Editor {
    if ($proc -ne $null -and -not $proc.HasExited) {
        try { Write-Host "[auto-run] stopping previous process (Id=$($proc.Id))"; $proc.Kill() } catch {}
        Start-Sleep -Milliseconds 200
    }
    Write-Host "[auto-run] launching dotnet run for $projectPath"
    $proc = Start-Process -FilePath dotnet -ArgumentList 'run','-c','Debug' -WorkingDirectory $projectPath -PassThru
}

function Schedule-Restart {
    $restartPending = $true
    $lastEvent = Get-Date
}

# initial launch
Start-Editor

$watcher = New-Object System.IO.FileSystemWatcher
$watcher.Path = (Resolve-Path $projectPath).Path
$watcher.IncludeSubdirectories = $true
$watcher.Filter = "*.*"
$watcher.NotifyFilter = [System.IO.NotifyFilters]'FileName, LastWrite, LastAccess, LastWrite, Size'

$onChange = Register-ObjectEvent $watcher Changed -Action { Schedule-Restart } -SourceIdentifier 'AutoRunChanged'
$onCreate = Register-ObjectEvent $watcher Created -Action { Schedule-Restart } -SourceIdentifier 'AutoRunCreated'
$onDelete = Register-ObjectEvent $watcher Deleted -Action { Schedule-Restart } -SourceIdentifier 'AutoRunDeleted'
$onRename = Register-ObjectEvent $watcher Renamed -Action { Schedule-Restart } -SourceIdentifier 'AutoRunRenamed'

$watcher.EnableRaisingEvents = $true

try {
    while ($true) {
        Start-Sleep -Milliseconds 200
        if ($restartPending) {
            # debounce
            $since = (Get-Date) - $lastEvent
            if ($since.TotalMilliseconds -ge $DebounceMs) {
                Write-Host "[auto-run] change detected; restarting editor"
                Start-Editor
                $restartPending = $false
            }
        }
    }
} finally {
    Unregister-Event -SourceIdentifier 'AutoRunChanged' -ErrorAction SilentlyContinue
    Unregister-Event -SourceIdentifier 'AutoRunCreated' -ErrorAction SilentlyContinue
    Unregister-Event -SourceIdentifier 'AutoRunDeleted' -ErrorAction SilentlyContinue
    Unregister-Event -SourceIdentifier 'AutoRunRenamed' -ErrorAction SilentlyContinue
    $watcher.Dispose()
}
