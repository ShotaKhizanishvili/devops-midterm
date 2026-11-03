<#
.SYNOPSIS
    Monitoring / health check: polls the deployed app and appends the result to a .log file.

.DESCRIPTION
    Each probe writes one pipe-delimited line that both a human and the app's
    /Home/Status dashboard can read:

        2026-11-03T20:15:04Z | UP | 200 | 37ms | http://localhost:5080/health | slot=blue release=... status=healthy

    With -AutoRollback the script also reacts to what it measures: after
    -FailureThreshold consecutive failures it calls rollback.ps1, which puts the
    previous release back on the same port.

.EXAMPLE
    # one probe against the currently deployed slot
    powershell -ExecutionPolicy Bypass -File ./scripts/health-check.ps1

.EXAMPLE
    # watch for a minute and self-heal if it breaks
    pwsh ./scripts/health-check.ps1 -Attempts 12 -IntervalSeconds 5 -AutoRollback
#>
[CmdletBinding()]
param(
    # Defaults to the live deployment recorded in deploy/state.json.
    [string]$Url = '',

    [string]$DeployRoot = 'deploy',

    # Defaults to <DeployRoot>/logs/health-check.log.
    [string]$LogPath = '',

    [int]$Attempts = 1,

    [int]$IntervalSeconds = 5,

    [int]$TimeoutSeconds = 10,

    # Consecutive failures that trigger -AutoRollback.
    [int]$FailureThreshold = 3,

    [switch]$AutoRollback
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$paths = Get-DeployPaths -DeployRoot $DeployRoot
$state = Get-DeployState -Paths $paths

if (-not $LogPath) { $LogPath = $paths.HealthLog }

if (-not $Url) {
    $port = [int]$state.port
    if ($port -le 0) { $port = 5080 }
    $Url = "http://localhost:$port/health"
}

$slotLabel = if ($state.activeSlot) { "$($state.activeSlot)/$($state.activeRelease)" } else { 'unknown slot' }
Write-Host "health check -> $Url ($slotLabel), $Attempts attempt(s), log: $LogPath" -ForegroundColor Cyan

$upCount = 0
$downCount = 0
$consecutiveFailures = 0
$rolledBack = $false
$last = $null

for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
    $last = Invoke-HealthProbe -Url $Url -TimeoutSeconds $TimeoutSeconds
    $status = if ($last.Up) { 'UP' } else { 'DOWN' }

    $line = Write-HealthLog -LogPath $LogPath -Status $status -HttpStatus $last.HttpStatus `
        -LatencyMs $last.LatencyMs -Url $Url -Note $last.Note

    Write-Host "  [$attempt/$Attempts] $line" -ForegroundColor $(if ($last.Up) { 'Green' } else { 'Red' })

    if ($last.Up) {
        $upCount++
        $consecutiveFailures = 0
    }
    else {
        $downCount++
        $consecutiveFailures++

        if ($AutoRollback -and $consecutiveFailures -ge $FailureThreshold -and -not $rolledBack) {
            Write-Host "  $consecutiveFailures consecutive failures -> rolling back" -ForegroundColor Yellow

            & (Join-Path $PSScriptRoot 'rollback.ps1') -DeployRoot $DeployRoot -Port ([int]$state.port) `
                -Reason "$consecutiveFailures consecutive failed health checks"

            $rolledBack = $true
            $consecutiveFailures = 0

            # Re-read the state so the next probes report the restored release.
            $state = Get-DeployState -Paths $paths
        }
    }

    if ($attempt -lt $Attempts) {
        Start-Sleep -Seconds $IntervalSeconds
    }
}

$total = $upCount + $downCount
$uptime = if ($total -gt 0) { [math]::Round($upCount * 100 / $total, 2) } else { 0 }

Write-Host ''
Write-Host "  checks: $total   up: $upCount   down: $downCount   uptime: $uptime%" -ForegroundColor Cyan
if ($rolledBack) {
    Write-Host '  a rollback was performed during this run - see deploy/logs/deploy.log' -ForegroundColor Yellow
}

if ($last -and $last.Up) { exit 0 } else { exit 1 }
