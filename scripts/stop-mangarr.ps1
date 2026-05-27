#Requires -Version 5.1
<#
.SYNOPSIS
  Stop the local Mangarr Console build.

.DESCRIPTION
  1. POST /api/v5/system/shutdown with the API key from config.xml (graceful).
  2. Fall back to Stop-Process on the recorded PID (verified against Mangarr.Console).
  3. Image-name fallback: stop any lingering Mangarr.Console process.
  4. Wait until the app stops responding on the port.

  Runs natively on the Windows host, so process lookup uses Get-Process /
  Stop-Process directly - no tasklist/taskkill PID guesswork.

.PARAMETER Port
  Override the port (default 8989, read from config.xml).

.PARAMETER Timeout
  Wait up to N seconds for shutdown (default 15).

.PARAMETER Force
  Skip the graceful shutdown attempt; kill immediately.

.EXAMPLE
  pwsh scripts/stop-mangarr.ps1

.EXAMPLE
  pwsh scripts/stop-mangarr.ps1 -Force
#>
[CmdletBinding()]
param(
    [int]$Port = 0,
    [int]$Timeout = 15,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$RunDir   = Join-Path $RepoRoot '_output\.mangarr-run'
$PidFile  = Join-Path $RunDir 'mangarr.pid'
$ProcName = 'Mangarr.Console'

function Get-MangarrConfig {
    $cfgPaths = @(
        (Join-Path $env:ProgramData 'Mangarr\config.xml'),
        (Join-Path $env:USERPROFILE '.config\Mangarr\config.xml')
    )
    foreach ($cfg in $cfgPaths) {
        if (Test-Path -LiteralPath $cfg) {
            try { return [xml](Get-Content -LiteralPath $cfg -Raw) } catch { }
        }
    }
    return $null
}

function Test-MangarrUp {
    param([int]$ProbePort)
    try {
        $resp = Invoke-WebRequest -Uri "http://localhost:$ProbePort/login" `
            -TimeoutSec 6 -UseBasicParsing -MaximumRedirection 0 -ErrorAction Stop
        $code = [int]$resp.StatusCode
        return ($code -ge 200 -and $code -lt 400)
    } catch {
        $r = $_.Exception.Response
        if ($r -and $r.StatusCode) {
            $code = [int]$r.StatusCode
            return ($code -ge 200 -and $code -lt 400)
        }
        return $false
    }
}

$cfg = Get-MangarrConfig
if ($Port -le 0 -and $cfg -and $cfg.Config.Port) { try { $Port = [int]$cfg.Config.Port } catch { } }
if ($Port -le 0) { $Port = 8989 }

$ApiKey = ''
if ($cfg -and $cfg.Config.ApiKey) { try { $ApiKey = [string]$cfg.Config.ApiKey } catch { } }

if (-not (Test-MangarrUp -ProbePort $Port)) {
    Write-Host "[stop-mangarr] Mangarr is not responding on http://localhost:$Port - nothing to stop."
    Remove-Item -LiteralPath $PidFile -ErrorAction SilentlyContinue
    exit 0
}

# Step 1: graceful shutdown via API.
if (-not $Force -and $ApiKey) {
    Write-Host "[stop-mangarr] Sending POST /api/v5/system/shutdown"
    try {
        Invoke-WebRequest -Uri "http://localhost:$Port/api/v5/system/shutdown" -Method Post `
            -Headers @{ 'X-Api-Key' = $ApiKey } -TimeoutSec 6 -UseBasicParsing -ErrorAction Stop | Out-Null
    } catch {
        # The app routinely drops the connection mid-shutdown; that's expected, so
        # we proceed to the confirmation wait regardless of the request outcome.
    }
    Write-Host "[stop-mangarr] Shutdown request sent; waiting up to ${Timeout}s for Mangarr to stop."
    $deadline = (Get-Date).AddSeconds($Timeout)
    while ((Get-Date) -lt $deadline) {
        if (-not (Test-MangarrUp -ProbePort $Port)) {
            Write-Host "[stop-mangarr] Stopped cleanly."
            Remove-Item -LiteralPath $PidFile -ErrorAction SilentlyContinue
            exit 0
        }
        Start-Sleep -Seconds 1
    }
    Write-Warning "[stop-mangarr] Graceful shutdown timed out after ${Timeout}s; falling back to kill."
}

# Step 2: kill the recorded PID, verified against the process name.
$killed = $false
if (Test-Path -LiteralPath $PidFile) {
    $recorded = (Get-Content -LiteralPath $PidFile -Raw).Trim()
    $recordedPid = 0
    if ([int]::TryParse($recorded, [ref]$recordedPid) -and $recordedPid -gt 0) {
        $p = Get-Process -Id $recordedPid -ErrorAction SilentlyContinue
        if ($p -and $p.ProcessName -eq $ProcName) {
            Write-Host "[stop-mangarr] Killing recorded pid $recordedPid"
            Stop-Process -Id $recordedPid -Force -ErrorAction SilentlyContinue
            $killed = $true
        } elseif ($recorded) {
            Write-Host "[stop-mangarr] Recorded pid $recorded does not match $ProcName (stale); falling through to image-name kill."
        }
    }
}

# Step 3: image-name fallback - runs when no/stale PID, or the PID kill left the app up.
if (-not $killed -or (Test-MangarrUp -ProbePort $Port)) {
    $procs = Get-Process -Name $ProcName -ErrorAction SilentlyContinue
    if ($procs) {
        Write-Host "[stop-mangarr] Stopping all $ProcName processes"
        $procs | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}

# Step 4: confirm.
$deadline = (Get-Date).AddSeconds($Timeout)
while ((Get-Date) -lt $deadline) {
    if (-not (Test-MangarrUp -ProbePort $Port)) {
        Write-Host "[stop-mangarr] Stopped."
        Remove-Item -LiteralPath $PidFile -ErrorAction SilentlyContinue
        exit 0
    }
    Start-Sleep -Seconds 1
}

Write-Warning "[stop-mangarr] Mangarr is still responding on port $Port after ${Timeout}s."
exit 1
