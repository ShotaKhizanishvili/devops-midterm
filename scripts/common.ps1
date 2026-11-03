<#
    Shared helpers for the blue-green deployment scripts.

    Layout created under the deploy root:

        deploy/
          blue/                 <- published app (slot A)
          green/                <- published app (slot B)
          state.json            <- which slot is live, which one to roll back to
          logs/deploy.log       <- deployment / rollback audit trail
          logs/health-check.log <- monitoring log (read by the /status dashboard)
          logs/app.out.log      <- stdout of the live process
          logs/app.err.log      <- stderr of the live process

    Only one slot serves traffic at a time (on $Port); the other one holds the
    previous release, ready for an instant rollback.

    Compatible with Windows PowerShell 5.1 and PowerShell 7+ (pwsh) so the same
    scripts run locally on Windows and on the GitHub Actions runner.
#>

$script:Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Get-DeployPaths {
    param([Parameter(Mandatory = $true)][string]$DeployRoot)

    $root = [System.IO.Path]::GetFullPath($DeployRoot)
    $logs = Join-Path $root 'logs'

    [pscustomobject]@{
        Root      = $root
        Logs      = $logs
        StateFile = Join-Path $root 'state.json'
        DeployLog = Join-Path $logs 'deploy.log'
        HealthLog = Join-Path $logs 'health-check.log'
        StdOut    = Join-Path $logs 'app.out.log'
        StdErr    = Join-Path $logs 'app.err.log'
    }
}

function Get-SlotPath {
    param([Parameter(Mandatory = $true)]$Paths, [Parameter(Mandatory = $true)][string]$Slot)
    Join-Path $Paths.Root $Slot
}

function Get-IdleSlot {
    param([string]$ActiveSlot)
    if ($ActiveSlot -eq 'blue') { 'green' } else { 'blue' }
}

function Initialize-DeployRoot {
    param([Parameter(Mandatory = $true)]$Paths)

    $dirs = @($Paths.Root, $Paths.Logs, (Get-SlotPath $Paths 'blue'), (Get-SlotPath $Paths 'green'))
    foreach ($dir in $dirs) {
        if (-not (Test-Path $dir)) {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
        }
    }
}

function New-DeployState {
    [pscustomobject]@{
        activeSlot      = ''
        activeRelease   = ''
        previousSlot    = ''
        previousRelease = ''
        port            = 0
        processId       = 0
        deployedAt      = ''
        lastAction      = 'none'
    }
}

function Get-DeployState {
    param([Parameter(Mandatory = $true)]$Paths)

    if (Test-Path $Paths.StateFile) {
        try {
            return [System.IO.File]::ReadAllText($Paths.StateFile) | ConvertFrom-Json
        }
        catch {
            Write-Warning "state.json is unreadable ($($_.Exception.Message)); starting from a clean state."
        }
    }

    New-DeployState
}

function Save-DeployState {
    param([Parameter(Mandatory = $true)]$Paths, [Parameter(Mandatory = $true)]$State)

    $json = $State | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText($Paths.StateFile, $json, $script:Utf8NoBom)
}

function Get-UtcStamp { (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ') }

function Add-Utf8Line {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Line)

    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    # Written through .NET so the file never gets a UTF-8 BOM: Windows PowerShell 5.1
    # would otherwise prepend one and corrupt the first parsed log line.
    [System.IO.File]::AppendAllText($Path, $Line + [Environment]::NewLine, $script:Utf8NoBom)
}

function Write-DeployLog {
    param(
        [Parameter(Mandatory = $true)]$Paths,
        [ValidateSet('INFO', 'OK', 'WARN', 'ERROR')][string]$Level = 'INFO',
        [Parameter(Mandatory = $true)][string]$Message
    )

    $line = '{0} | {1,-5} | {2}' -f (Get-UtcStamp), $Level, $Message
    Add-Utf8Line -Path $Paths.DeployLog -Line $line

    $colour = 'Gray'
    if ($Level -eq 'OK') { $colour = 'Green' }
    elseif ($Level -eq 'WARN') { $colour = 'Yellow' }
    elseif ($Level -eq 'ERROR') { $colour = 'Red' }

    Write-Host $line -ForegroundColor $colour
}

function Write-HealthLog {
    param(
        [Parameter(Mandatory = $true)][string]$LogPath,
        [Parameter(Mandatory = $true)][ValidateSet('UP', 'DOWN')][string]$Status,
        [int]$HttpStatus = 0,
        [int]$LatencyMs = 0,
        [Parameter(Mandatory = $true)][string]$Url,
        [string]$Note = ''
    )

    # Pipe-delimited so HealthLogParser (and the /status dashboard) can read it back.
    $line = '{0} | {1} | {2} | {3}ms | {4} | {5}' -f (Get-UtcStamp), $Status, $HttpStatus, $LatencyMs, $Url, $Note
    Add-Utf8Line -Path $LogPath -Line $line
    $line
}

function Invoke-HealthProbe {
    param([Parameter(Mandatory = $true)][string]$Url, [int]$TimeoutSeconds = 10)

    $watch = [System.Diagnostics.Stopwatch]::StartNew()

    try {
        $response = Invoke-WebRequest -Uri $Url -TimeoutSec $TimeoutSeconds -UseBasicParsing -ErrorAction Stop
        $watch.Stop()

        $note = 'ok'
        try {
            $body = $response.Content | ConvertFrom-Json
            $note = 'slot={0} release={1} status={2}' -f $body.slot, $body.release, $body.status
        }
        catch {
            $note = 'non-json body'
        }

        return [pscustomobject]@{
            Up         = ([int]$response.StatusCode -eq 200)
            HttpStatus = [int]$response.StatusCode
            LatencyMs  = [int]$watch.ElapsedMilliseconds
            Note       = $note
        }
    }
    catch {
        $watch.Stop()

        $status = 0
        try {
            if ($null -ne $_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
        }
        catch {
            $status = 0
        }

        $message = ($_.Exception.Message -replace '\r?\n', ' ') -replace '\|', '/'

        return [pscustomobject]@{
            Up         = $false
            HttpStatus = $status
            LatencyMs  = [int]$watch.ElapsedMilliseconds
            Note       = $message.Trim()
        }
    }
}

function Wait-ForHealth {
    param([Parameter(Mandatory = $true)][string]$Url, [int]$TimeoutSeconds = 45)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $last = $null

    do {
        $last = Invoke-HealthProbe -Url $Url -TimeoutSeconds 5
        if ($last.Up) { return $last }
        Start-Sleep -Milliseconds 1200
    } while ((Get-Date) -lt $deadline)

    return $last
}

function Stop-AppProcess {
    param([int]$ProcessId)

    if (-not $ProcessId -or $ProcessId -le 0) { return $false }

    $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if (-not $process) { return $false }

    try {
        $process | Stop-Process -Force -ErrorAction Stop
        Start-Sleep -Milliseconds 700
    }
    catch {
        Write-Warning "Could not stop process ${ProcessId}: $($_.Exception.Message)"
        return $false
    }

    return $true
}

function Start-AppProcess {
    param(
        [Parameter(Mandatory = $true)]$Paths,
        [Parameter(Mandatory = $true)][string]$Slot,
        [Parameter(Mandatory = $true)][int]$Port,
        [Parameter(Mandatory = $true)][string]$Release,
        [string]$StdOutFile,
        [string]$StdErrFile
    )

    $dll = Join-Path (Get-SlotPath $Paths $Slot) 'devops-midterm.dll'
    if (-not (Test-Path $dll)) {
        throw "Slot '$Slot' holds no published application ($dll is missing)."
    }

    if (-not $StdOutFile) { $StdOutFile = $Paths.StdOut }
    if (-not $StdErrFile) { $StdErrFile = $Paths.StdErr }

    # The app reads these: /health reports slot + release (so a rollback is visible),
    # and Monitoring__LogPath points the dashboard at the shared health-check log
    # instead of a per-slot copy that would disappear on the next switch.
    $env:ASPNETCORE_ENVIRONMENT = 'Production'
    $env:ASPNETCORE_URLS = "http://localhost:$Port"
    $env:DEPLOY_SLOT = $Slot
    $env:RELEASE_VERSION = $Release
    $env:Monitoring__LogPath = $Paths.HealthLog

    Start-Process -FilePath 'dotnet' -ArgumentList @($dll) -PassThru -RedirectStandardOutput $StdOutFile -RedirectStandardError $StdErrFile
}
