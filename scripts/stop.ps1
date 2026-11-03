<#
.SYNOPSIS
    Stops the locally deployed application (the process recorded in deploy/state.json).
#>
[CmdletBinding()]
param([string]$DeployRoot = 'deploy')

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$paths = Get-DeployPaths -DeployRoot $DeployRoot
$state = Get-DeployState -Paths $paths

if (Stop-AppProcess -ProcessId ([int]$state.processId)) {
    Write-DeployLog -Paths $paths -Level INFO -Message "stopped process $($state.processId) (slot '$($state.activeSlot)')"
    $state.processId = 0
    $state.lastAction = 'stopped'
    Save-DeployState -Paths $paths -State $state
}
else {
    Write-Host 'nothing to stop - no live process recorded.' -ForegroundColor Yellow
}
