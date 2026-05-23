<#
.SYNOPSIS
    Identify (and optionally terminate) leaked PuppeteerSharp Chromium processes,
    leaving user Chrome browser sessions untouched.

.DESCRIPTION
    Test runs that exercise ComixPuppeteerSigner spawn headless Chromium via
    PuppeteerSharp. When VSTest is canceled / crashes / hits the per-fixture
    timeout, the Chromium child can be orphaned (parent dies; Chromium keeps
    running). Over time these accumulate, eat RAM, hold file locks on
    _temp/mangarr-chromium/, and starve the next test run for resources.

    The naive cleanup `taskkill /F /IM chrome.exe` also kills the user's
    real Chrome browser. This script discriminates by THREE orthogonal
    signals that PuppeteerSharp's launched Chromium carries but user Chrome
    never does:

      1. ExecutablePath is NOT under "Program Files\Google\Chrome\Application\"
         (Puppeteer downloads its own Chromium under _temp/mangarr-chromium/,
         ~/.cache/puppeteer/, or $RUNNER_TEMP).

      2. CommandLine contains "--remote-debugging-port=" — Puppeteer ALWAYS
         opens a DevTools port to talk to Chromium; user Chrome never does.

      3. CommandLine contains "--enable-automation" — Puppeteer sets the
         WebDriver automation flag; user Chrome never does.

    Any single signal is sufficient evidence to classify as Puppeteer-leaked.
    The script requires at LEAST ONE signal before flagging a process for kill,
    so a Chromium-based browser (Edge, Brave, Vivaldi) is also safe.

.PARAMETER Kill
    Actually terminate the matched processes. Without this flag, the script
    is dry-run (lists what would be killed but takes no action).

.PARAMETER Verbose
    Print per-process detection signals for every chrome.exe (matched and
    skipped) for debugging.

.EXAMPLE
    .\scripts\kill-orphan-chromium.ps1
    Dry-run: lists Puppeteer Chromium processes that would be terminated.

.EXAMPLE
    .\scripts\kill-orphan-chromium.ps1 -Kill
    Actually terminate the identified leaked processes.

.EXAMPLE
    .\scripts\kill-orphan-chromium.ps1 -Verbose
    Show detection signals for every chrome.exe (including user Chrome that
    was correctly skipped).
#>

[CmdletBinding()]
param(
    [switch]$Kill
)

$ErrorActionPreference = 'Stop'

# Pull CommandLine + ExecutablePath for every chrome.exe process. Tasklist alone
# can't see these — Get-CimInstance Win32_Process is the canonical Windows API.
$chromes = Get-CimInstance Win32_Process -Filter "Name = 'chrome.exe'"

if (-not $chromes) {
    Write-Host "[kill-orphan-chromium] No chrome.exe processes running."
    exit 0
}

$leaked = @()
$preserved = @()

foreach ($p in $chromes) {
    $cmdLine = if ($p.CommandLine) { $p.CommandLine } else { "" }
    $exePath = if ($p.ExecutablePath) { $p.ExecutablePath } else { "" }

    # Signal 1: NOT under user-installed Chrome.
    $isUserChromePath = $exePath -match '\\Program Files( \(x86\))?\\Google\\Chrome\\Application\\chrome\.exe$'

    # Signal 2: Puppeteer's DevTools port flag.
    $hasDebugPort = $cmdLine -match '--remote-debugging-port='

    # Signal 3: WebDriver automation flag.
    $hasAutomation = $cmdLine -match '--enable-automation'

    # A process is Puppeteer-leaked if ANY of:
    #   (a) it's NOT the user-installed Chrome binary, OR
    #   (b) it carries the remote-debugging port flag, OR
    #   (c) it carries the enable-automation flag.
    # Signal (a) alone catches Chromium installed outside Program Files (Puppeteer's
    # _temp cache); signals (b)+(c) catch the case where someone WERE somehow
    # launching the user-installed Chrome via Puppeteer (edge case, but safe).
    $isLeaked = (-not $isUserChromePath) -or $hasDebugPort -or $hasAutomation

    $verbose = [PSCustomObject]@{
        PID         = $p.ProcessId
        ParentPID   = $p.ParentProcessId
        Memory_MB   = [math]::Round($p.WorkingSetSize / 1MB, 1)
        UserChrome  = $isUserChromePath
        DebugPort   = $hasDebugPort
        Automation  = $hasAutomation
        Verdict     = if ($isLeaked) { 'LEAKED' } else { 'KEEP' }
        ExePath     = $exePath
    }

    if ($isLeaked) {
        $leaked += $verbose
    } else {
        $preserved += $verbose
    }

    Write-Verbose ("PID {0} verdict={1} userChrome={2} debugPort={3} automation={4} exe={5}" -f `
        $p.ProcessId, $verbose.Verdict, $isUserChromePath, $hasDebugPort, $hasAutomation, $exePath)
}

Write-Host ""
Write-Host "[kill-orphan-chromium] Survey complete:"
Write-Host ("  Total chrome.exe:     {0}" -f $chromes.Count)
Write-Host ("  Puppeteer-leaked:     {0}" -f $leaked.Count)
Write-Host ("  User Chrome (keep):   {0}" -f $preserved.Count)
Write-Host ""

if ($leaked.Count -gt 0) {
    Write-Host "Leaked Chromium candidates:"
    $leaked | Format-Table PID, ParentPID, Memory_MB, DebugPort, Automation, ExePath -AutoSize | Out-String | Write-Host
}

if ($preserved.Count -gt 0 -and $VerbosePreference -eq 'Continue') {
    Write-Host "Preserved (user Chrome):"
    $preserved | Format-Table PID, ParentPID, Memory_MB, ExePath -AutoSize | Out-String | Write-Host
}

if ($leaked.Count -eq 0) {
    Write-Host "[kill-orphan-chromium] Nothing to clean up."
    exit 0
}

if (-not $Kill) {
    Write-Host "[kill-orphan-chromium] DRY-RUN. Re-run with -Kill to actually terminate."
    exit 0
}

Write-Host "[kill-orphan-chromium] Terminating $($leaked.Count) leaked process(es)..."
foreach ($entry in $leaked) {
    try {
        Stop-Process -Id $entry.PID -Force -ErrorAction Stop
        Write-Host ("  killed PID {0} ({1} MB)" -f $entry.PID, $entry.Memory_MB)
    } catch {
        Write-Warning ("  could not kill PID {0}: {1}" -f $entry.PID, $_.Exception.Message)
    }
}
Write-Host "[kill-orphan-chromium] Done."
