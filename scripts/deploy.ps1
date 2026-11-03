<#
.SYNOPSIS
    Blue-green deployment of the GradeBook app into a local production folder.

.DESCRIPTION
    One stable URL (http://localhost:<Port>) is served by exactly one of two
    slot folders - "blue" or "green". A deployment never touches the slot that
    is currently live:

        1. publish (or copy) the new release into the IDLE slot
        2. smoke-test that slot on a throwaway port
        3. only then stop the live process and start the new slot on <Port>
        4. verify /health; if it fails, roll straight back to the old slot

    Because the old release stays on disk in the other slot, a rollback is a
    process restart rather than a rebuild.

.EXAMPLE
    # local: build from source and deploy
    powershell -ExecutionPolicy Bypass -File ./scripts/deploy.ps1

.EXAMPLE
    # CI: deploy an artifact that the build stage already published
    pwsh ./scripts/deploy.ps1 -ArtifactPath ./artifact -Release $env:GITHUB_SHA
#>
[CmdletBinding()]
param(
    # Pre-published application folder (from the CI build stage). When empty the
    # script runs "dotnet publish" itself.
    [string]$ArtifactPath = '',

    [string]$ProjectPath = 'devops-midterm/devops-midterm.csproj',

    [string]$DeployRoot = 'deploy',

    # The stable production port that the monitoring script polls.
    [int]$Port = 5080,

    # Throwaway port used to verify the new release before it goes live.
    [int]$SmokePort = 5090,

    [string]$Release = '',

    [int]$HealthTimeoutSeconds = 60,

    # Keep a broken release live instead of rolling back (for demos).
    [switch]$NoRollback
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

if (-not $Release) {
    $Release = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')
}

$paths = Get-DeployPaths -DeployRoot $DeployRoot
Initialize-DeployRoot -Paths $paths

$state = Get-DeployState -Paths $paths
$activeSlot = [string]$state.activeSlot
$targetSlot = Get-IdleSlot -ActiveSlot $activeSlot
$targetPath = Get-SlotPath $paths $targetSlot

$healthUrl = "http://localhost:$Port/health"
$smokeUrl = "http://localhost:$SmokePort/health"

Write-DeployLog -Paths $paths -Level INFO -Message "=== deploy start | release=$Release | active=$(if ($activeSlot) { $activeSlot } else { '(none)' }) -> target=$targetSlot ==="

# Staging overwrites the idle slot, which is also the recorded rollback target.
# If the deployment then aborts, that target no longer exists, so the state file
# must stop advertising it - otherwise rollback.ps1 would start the broken build.
function Invoke-AbortedDeploy {
    param([string]$Message)

    Write-DeployLog -Paths $paths -Level ERROR -Message $Message

    if ($state.previousSlot -eq $targetSlot -and $state.previousRelease) {
        Write-DeployLog -Paths $paths -Level WARN -Message "slot '$targetSlot' no longer holds release '$($state.previousRelease)' - rollback target cleared"
    }

    $state.previousSlot = ''
    $state.previousRelease = ''
    $state.lastAction = 'deploy-aborted'
    Save-DeployState -Paths $paths -State $state

    exit 1
}

# ---------------------------------------------------------------------------
# 1. Put the new release into the idle slot.
# ---------------------------------------------------------------------------

# Checked before anything is deleted, so a bad path costs nothing.
if ($ArtifactPath -and -not (Test-Path (Join-Path $ArtifactPath 'devops-midterm.dll'))) {
    Write-DeployLog -Paths $paths -Level ERROR -Message "artifact at '$ArtifactPath' contains no devops-midterm.dll - nothing was changed"
    exit 1
}

if (Test-Path $targetPath) {
    Get-ChildItem -Path $targetPath -Force | Remove-Item -Recurse -Force
}
else {
    New-Item -ItemType Directory -Path $targetPath -Force | Out-Null
}

if ($ArtifactPath) {
    Write-DeployLog -Paths $paths -Level INFO -Message "copying artifact '$ArtifactPath' into slot '$targetSlot'"
    Copy-Item -Path (Join-Path $ArtifactPath '*') -Destination $targetPath -Recurse -Force
}
else {
    Write-DeployLog -Paths $paths -Level INFO -Message "publishing '$ProjectPath' into slot '$targetSlot'"
    dotnet publish $ProjectPath -c Release -o $targetPath --nologo | Out-Host

    if ($LASTEXITCODE -ne 0) {
        Invoke-AbortedDeploy -Message "dotnet publish failed with exit code $LASTEXITCODE - live slot untouched"
    }
}

# ---------------------------------------------------------------------------
# 2. Smoke-test the new slot on a throwaway port, before it serves traffic.
# ---------------------------------------------------------------------------
Write-DeployLog -Paths $paths -Level INFO -Message "smoke testing slot '$targetSlot' on port $SmokePort"

$smokeProcess = Start-AppProcess -Paths $paths -Slot $targetSlot -Port $SmokePort -Release $Release `
    -StdOutFile (Join-Path $paths.Logs 'smoke.out.log') -StdErrFile (Join-Path $paths.Logs 'smoke.err.log')

$smokeResult = Wait-ForHealth -Url $smokeUrl -TimeoutSeconds $HealthTimeoutSeconds
Stop-AppProcess -ProcessId $smokeProcess.Id | Out-Null

if (-not $smokeResult.Up) {
    Invoke-AbortedDeploy -Message "smoke test FAILED (http=$($smokeResult.HttpStatus), $($smokeResult.Note)) - aborting, '$activeSlot' stays live"
}

Write-DeployLog -Paths $paths -Level OK -Message "smoke test passed in $($smokeResult.LatencyMs)ms"

# ---------------------------------------------------------------------------
# 3. Switch traffic: stop the live process, start the new slot on $Port.
# ---------------------------------------------------------------------------
if (Stop-AppProcess -ProcessId ([int]$state.processId)) {
    Write-DeployLog -Paths $paths -Level INFO -Message "stopped previous process $($state.processId) (slot '$activeSlot')"
}

$process = Start-AppProcess -Paths $paths -Slot $targetSlot -Port $Port -Release $Release
Write-DeployLog -Paths $paths -Level INFO -Message "started slot '$targetSlot' as process $($process.Id) on port $Port"

$health = Wait-ForHealth -Url $healthUrl -TimeoutSeconds $HealthTimeoutSeconds
Write-HealthLog -LogPath $paths.HealthLog -Status $(if ($health.Up) { 'UP' } else { 'DOWN' }) `
    -HttpStatus $health.HttpStatus -LatencyMs $health.LatencyMs -Url $healthUrl `
    -Note "post-deploy check release=$Release slot=$targetSlot" | Out-Null

if (-not $health.Up) {
    Write-DeployLog -Paths $paths -Level ERROR -Message "new release is unhealthy (http=$($health.HttpStatus), $($health.Note))"

    $canRollback = $activeSlot -and (Test-Path (Join-Path (Get-SlotPath $paths $activeSlot) 'devops-midterm.dll'))

    if ($NoRollback) {
        Write-DeployLog -Paths $paths -Level WARN -Message 'rollback suppressed by -NoRollback'
    }
    elseif (-not $canRollback) {
        Write-DeployLog -Paths $paths -Level WARN -Message 'no previous release on disk - nothing to roll back to'
    }
    else {
        # Record the failed release so rollback.ps1 knows what to step back from.
        $state.previousSlot = $activeSlot
        $state.previousRelease = [string]$state.activeRelease
        $state.activeSlot = $targetSlot
        $state.activeRelease = $Release
        $state.processId = $process.Id
        $state.port = $Port
        $state.deployedAt = Get-UtcStamp
        $state.lastAction = 'deploy-failed'
        Save-DeployState -Paths $paths -State $state

        Write-DeployLog -Paths $paths -Level WARN -Message 'triggering automatic rollback'
        & (Join-Path $PSScriptRoot 'rollback.ps1') -DeployRoot $DeployRoot -Port $Port `
            -HealthTimeoutSeconds $HealthTimeoutSeconds -Reason "failed health check of release $Release"
    }

    exit 1
}

# ---------------------------------------------------------------------------
# 4. Commit the switch.
# ---------------------------------------------------------------------------
$state.previousSlot = $activeSlot
$state.previousRelease = [string]$state.activeRelease
$state.activeSlot = $targetSlot
$state.activeRelease = $Release
$state.processId = $process.Id
$state.port = $Port
$state.deployedAt = Get-UtcStamp
$state.lastAction = 'deploy'
Save-DeployState -Paths $paths -State $state

Write-DeployLog -Paths $paths -Level OK -Message "release $Release is live on slot '$targetSlot' (http://localhost:$Port) in $($health.LatencyMs)ms"
Write-DeployLog -Paths $paths -Level INFO -Message "rollback target: slot '$(if ($state.previousSlot) { $state.previousSlot } else { '(none)' })' release '$(if ($state.previousRelease) { $state.previousRelease } else { '(none)' })'"

Write-Host ''
Write-Host "  Application : http://localhost:$Port"       -ForegroundColor Cyan
Write-Host "  Dashboard   : http://localhost:$Port/Home/Status" -ForegroundColor Cyan
Write-Host "  Health      : $healthUrl"                   -ForegroundColor Cyan
Write-Host "  Live slot   : $targetSlot ($Release)"        -ForegroundColor Cyan
Write-Host ''

exit 0
