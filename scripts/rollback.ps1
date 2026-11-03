<#
.SYNOPSIS
    Rolls the local blue-green deployment back to the previous slot.

.DESCRIPTION
    The previous release is still sitting in the idle slot folder, so a rollback
    is just "stop the current process, start the other slot on the same port,
    verify /health". It is called automatically by deploy.ps1 when a new release
    fails its health check, by health-check.ps1 -AutoRollback when a live
    deployment starts failing, and can be run by hand at any time.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File ./scripts/rollback.ps1
#>
[CmdletBinding()]
param(
    [string]$DeployRoot = 'deploy',
    [int]$Port = 5080,
    [int]$HealthTimeoutSeconds = 60,
    [string]$Reason = 'manual rollback'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$paths = Get-DeployPaths -DeployRoot $DeployRoot
Initialize-DeployRoot -Paths $paths

$state = Get-DeployState -Paths $paths
$failedSlot = [string]$state.activeSlot
$failedRelease = [string]$state.activeRelease
$targetSlot = [string]$state.previousSlot
$targetRelease = [string]$state.previousRelease
$healthUrl = "http://localhost:$Port/health"

Write-DeployLog -Paths $paths -Level WARN -Message "=== rollback start | reason: $Reason ==="

if (-not $targetSlot) {
    Write-DeployLog -Paths $paths -Level ERROR -Message 'no previous slot recorded in state.json - cannot roll back'
    exit 1
}

if (-not (Test-Path (Join-Path (Get-SlotPath $paths $targetSlot) 'devops-midterm.dll'))) {
    Write-DeployLog -Paths $paths -Level ERROR -Message "slot '$targetSlot' holds no published application - cannot roll back"
    exit 1
}

Write-DeployLog -Paths $paths -Level INFO -Message "rolling back '$failedSlot' ($(if ($failedRelease) { $failedRelease } else { 'unknown' })) -> '$targetSlot' ($(if ($targetRelease) { $targetRelease } else { 'unknown' }))"

if (Stop-AppProcess -ProcessId ([int]$state.processId)) {
    Write-DeployLog -Paths $paths -Level INFO -Message "stopped failing process $($state.processId)"
}

$release = if ($targetRelease) { $targetRelease } else { 'rollback' }
$process = Start-AppProcess -Paths $paths -Slot $targetSlot -Port $Port -Release $release
Write-DeployLog -Paths $paths -Level INFO -Message "started slot '$targetSlot' as process $($process.Id) on port $Port"

$health = Wait-ForHealth -Url $healthUrl -TimeoutSeconds $HealthTimeoutSeconds
Write-HealthLog -LogPath $paths.HealthLog -Status $(if ($health.Up) { 'UP' } else { 'DOWN' }) `
    -HttpStatus $health.HttpStatus -LatencyMs $health.LatencyMs -Url $healthUrl `
    -Note "post-rollback check slot=$targetSlot release=$release" | Out-Null

if (-not $health.Up) {
    Write-DeployLog -Paths $paths -Level ERROR -Message "rollback target is ALSO unhealthy (http=$($health.HttpStatus), $($health.Note)) - manual intervention required"
    exit 1
}

$state.activeSlot = $targetSlot
$state.activeRelease = $release
$state.previousSlot = $failedSlot
$state.previousRelease = $failedRelease
$state.processId = $process.Id
$state.port = $Port
$state.deployedAt = Get-UtcStamp
$state.lastAction = 'rollback'
Save-DeployState -Paths $paths -State $state

Write-DeployLog -Paths $paths -Level OK -Message "rollback complete - slot '$targetSlot' ($release) is live on http://localhost:$Port in $($health.LatencyMs)ms"
exit 0
