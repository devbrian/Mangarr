#Requires -Version 5.1
<#
.SYNOPSIS
  Start the local Mangarr Console build in the background and wait until it is ready.

.DESCRIPTION
  Launches _output/net10.0/Mangarr.Console.exe (Debug build under net10.0),
  waits up to 60s for GET /login to respond 200, and records the REAL process PID
  + log paths under _output/.mangarr-run/ so stop-mangarr.ps1 can find them.

  Runs natively on the Windows host, so the readiness probe always reaches the
  app's loopback listener. (The previous bash script silently timed out when run
  from WSL2, whose NAT'd network namespace can't reach the Windows-side listener.)

.PARAMETER Foreground
  Run in the foreground (don't background; useful for tailing output).

.PARAMETER Port
  Override the readiness probe port (default 8989, read from config.xml when present).

.PARAMETER Timeout
  Override the readiness timeout in seconds (default 60).

.EXAMPLE
  pwsh scripts/start-mangarr.ps1

.EXAMPLE
  pwsh scripts/start-mangarr.ps1 -Foreground

.EXAMPLE
  pwsh scripts/start-mangarr.ps1 -Port 8990 -Timeout 120
#>
[CmdletBinding()]
param(
    [switch]$Foreground,
    [int]$Port = 0,
    [int]$Timeout = 60
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$RunDir   = Join-Path $RepoRoot '_output\.mangarr-run'
$PidFile  = Join-Path $RunDir 'mangarr.pid'
$LogFile  = Join-Path $RunDir 'mangarr.log'
$ErrFile  = Join-Path $RunDir 'mangarr.err.log'
$Exe      = Join-Path $RepoRoot '_output\net10.0\Mangarr.Console.exe'

function Resolve-MangarrPort {
    param([int]$Override)
    if ($Override -gt 0) { return $Override }
    $cfgPaths = @(
        (Join-Path $env:ProgramData 'Mangarr\config.xml'),
        (Join-Path $env:USERPROFILE '.config\Mangarr\config.xml')
    )
    foreach ($cfg in $cfgPaths) {
        if (Test-Path -LiteralPath $cfg) {
            try {
                $xml = [xml](Get-Content -LiteralPath $cfg -Raw)
                if ($xml.Config.Port) { return [int]$xml.Config.Port }
            } catch { }
        }
    }
    return 8989
}

function Get-MangarrStatusCode {
    param([int]$ProbePort, [int]$TimeoutSec = 6)
    try {
        $resp = Invoke-WebRequest -Uri "http://localhost:$ProbePort/login" `
            -TimeoutSec $TimeoutSec -UseBasicParsing -ErrorAction Stop
        return [int]$resp.StatusCode
    } catch {
        $r = $_.Exception.Response
        if ($r -and $r.StatusCode) { return [int]$r.StatusCode }
        return 0
    }
}

$Port = Resolve-MangarrPort -Override $Port

if (-not (Test-Path -LiteralPath $Exe)) {
    Write-Error "Mangarr.Console.exe not found at $Exe`nRun 'dotnet build src/Mangarr.sln --configuration Debug' first."
    exit 1
}

# Refuse to start a second instance on the same port.
if ((Get-MangarrStatusCode -ProbePort $Port) -eq 200) {
    $existing = ''
    if (Test-Path -LiteralPath $PidFile) { $existing = (Get-Content -LiteralPath $PidFile -Raw).Trim() }
    $suffix = if ($existing) { " (pid $existing)" } else { '' }
    Write-Host "Mangarr is already listening on port $Port$suffix"
    exit 0
}

New-Item -ItemType Directory -Force -Path $RunDir | Out-Null

if ($Foreground) {
    Write-Host "[start-mangarr] Launching in foreground from $Exe"
    & $Exe
    exit $LASTEXITCODE
}

# Background launch. -PassThru returns the REAL Mangarr.Console.exe process, so the
# recorded PID is always correct (no bash wrapper-PID guesswork).
Write-Host "[start-mangarr] Launching $Exe in background (stdout: $LogFile, stderr: $ErrFile)"
$proc = Start-Process -FilePath $Exe -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput $LogFile -RedirectStandardError $ErrFile
Set-Content -LiteralPath $PidFile -Value $proc.Id

Write-Host -NoNewline "[start-mangarr] Waiting up to ${Timeout}s for http://localhost:$Port to be ready"
$deadline = (Get-Date).AddSeconds($Timeout)
while ((Get-Date) -lt $deadline) {
    if ((Get-MangarrStatusCode -ProbePort $Port -TimeoutSec 3) -eq 200) {
        Write-Host ''
        Write-Host "[start-mangarr] Ready on http://localhost:$Port (pid $($proc.Id))"
        exit 0
    }
    if ($proc.HasExited) {
        Write-Host ''
        Write-Warning "[start-mangarr] Process $($proc.Id) exited (code $($proc.ExitCode)) before becoming ready. Tail of logs:"
        if (Test-Path -LiteralPath $LogFile) { Get-Content -LiteralPath $LogFile -Tail 30 }
        if (Test-Path -LiteralPath $ErrFile) { Get-Content -LiteralPath $ErrFile -Tail 30 }
        Remove-Item -LiteralPath $PidFile -ErrorAction SilentlyContinue
        exit 1
    }
    Write-Host -NoNewline '.'
    Start-Sleep -Seconds 1
}

Write-Host ''
Write-Warning "[start-mangarr] Timed out after ${Timeout}s waiting for http://localhost:$Port (pid $($proc.Id) still alive). Tail of logs:"
if (Test-Path -LiteralPath $LogFile) { Get-Content -LiteralPath $LogFile -Tail 30 }
if (Test-Path -LiteralPath $ErrFile) { Get-Content -LiteralPath $ErrFile -Tail 30 }
exit 1
