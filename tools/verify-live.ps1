<#
.SYNOPSIS
    Repeats the three live checks that unit tests cannot cover, against the real app.

.DESCRIPTION
    Everything here was, until now, done by hand with throwaway scripts in %TEMP%, which meant nobody
    could repeat or trust it. Each scenario prints an "ok" line per assertion and the script exits
    non-zero if any of them fails.

      crash-resume   start a match, score 2-1, let the debounce write match.json, kill the process with
                     no OnExit (that is the crash), relaunch and check the overlay serves 2-1 again,
                     then RESET MATCH and check the snapshot is deleted.

      banner-retry   hold 127.0.0.1:51234 with a program that is not this app, launch the Controller and
                     check the window says the overlay is off and offers Retry, then free the port,
                     press Retry and check the overlay serves and the banner is gone.

      leftover       run a Controller built from before the single-instance guard, which keeps the port
                     with a window showing. The current build must leave it alone and tell the operator
                     to close that window; hiding the window and pressing Retry must then close the
                     leftover and take the port back.

    The operator's %APPDATA%\PoolScoreboard is backed up first and restored afterwards, every process
    this script started is stopped, and the pre-guard worktree is removed.

.PARAMETER Scenario
    all (default), or one of the three names above.

.PARAMETER Exe
    Controller to test. Defaults to the Debug build.

.PARAMETER PreGuardCommit
    Commit used for the leftover scenario's old build. It must predate the single-instance guard
    (4cceaff), otherwise the second launch exits instead of holding the port.

.EXAMPLE
    dotnet build
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\verify-live.ps1

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\verify-live.ps1 -Scenario crash-resume
#>
[CmdletBinding()]
param(
    [ValidateSet('all', 'crash-resume', 'banner-retry', 'leftover')]
    [string]$Scenario = 'all',

    [string]$Exe,

    [int]$Port = 51234,

    [string]$PreGuardCommit = '1acddbd'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class VerifyWin32
{
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr w, IntPtr l);
}
"@

$root = Split-Path $PSScriptRoot -Parent
if (-not $Exe) { $Exe = Join-Path $root 'PoolScoreboard.Controller\bin\Debug\net8.0-windows\PoolScoreboard.Controller.exe' }
$AppData = Join-Path $env:APPDATA 'PoolScoreboard'
$MatchPath = Join-Path $AppData 'match.json'
$BaseUrl = "http://127.0.0.1:$Port"

$script:Started = New-Object System.Collections.ArrayList
$script:Failures = New-Object System.Collections.ArrayList
$script:Worktree = $null
$script:AppDataBackup = $null
$script:BlockerScript = $null

function Write-Step([string]$Text) {
    Write-Host ''
    Write-Host "== $Text ==" -ForegroundColor Cyan
}

function Assert-That($Condition, [string]$Message) {
    if (-not $Condition) { throw "CHECK FAILED: $Message" }
    Write-Host "  ok  $Message" -ForegroundColor Green
}

function Get-ControllerWindow([int]$TargetPid) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $TargetPid)
    [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Children, $condition)
}

function Wait-ControllerWindow([int]$TargetPid, [int]$TimeoutSeconds = 25) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $window = Get-ControllerWindow $TargetPid
        if ($window) { return $window }
        Start-Sleep -Milliseconds 250
    }
    throw "no window appeared for pid $TargetPid within $TimeoutSeconds s"
}

function Get-WindowText([int]$TargetPid) {
    $window = Get-ControllerWindow $TargetPid
    if (-not $window) { return '' }
    $all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    $names = foreach ($element in $all) { if ($element.Current.Name) { $element.Current.Name } }
    return ($names -join ' | ')
}

function Wait-WindowText([int]$TargetPid, [string]$Pattern, [int]$TimeoutSeconds = 20) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $text = Get-WindowText $TargetPid
        if ($text -match $Pattern) { return $text }
        Start-Sleep -Milliseconds 300
    }
    return $null
}

function Test-RetryButton([int]$TargetPid) {
    $window = Get-ControllerWindow $TargetPid
    if (-not $window) { return $false }
    $byName = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty, 'Retry')
    return $null -ne $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $byName)
}

function Invoke-WindowButton([int]$TargetPid, [string]$Label, [int]$TimeoutSeconds = 20) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $window = Get-ControllerWindow $TargetPid
        if ($window) {
            $byType = [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Button)
            foreach ($button in $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $byType)) {
                if ($button.Current.Name -eq $Label) {
                    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
                    return $true
                }
            }
        }
        Start-Sleep -Milliseconds 300
    }
    return $false
}

function Hide-ProcessWindow([System.Diagnostics.Process]$Process) {
    $Process.Refresh()
    $handle = $Process.MainWindowHandle
    if ($handle -eq [IntPtr]::Zero) { return }

    [VerifyWin32]::ShowWindow($handle, 0) | Out-Null
    for ($i = 0; $i -lt 25; $i++) {
        Start-Sleep -Milliseconds 200
        $Process.Refresh()
        if ($Process.MainWindowHandle -eq [IntPtr]::Zero) { return }
    }
    throw "could not hide the window of pid $($Process.Id)"
}

function Get-PortOwnerPid {
    $connection = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($connection) { return [int]$connection.OwningProcess }
    return 0
}

function Wait-PortOwner([int]$ExpectedPid, [int]$TimeoutSeconds = 25) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ((Get-PortOwnerPid) -eq $ExpectedPid) { return $true }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

function Wait-PortFree([int]$TimeoutSeconds = 20) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ((Get-PortOwnerPid) -eq 0) { return $true }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

function Start-Controller([string]$Path) {
    $process = Start-Process -FilePath $Path -PassThru
    $script:Started.Add($process) | Out-Null
    return $process
}

function Wait-State([int]$TimeoutSeconds = 25) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            return Invoke-RestMethod -Uri "$BaseUrl/overlay/api/scoreboard/state" -TimeoutSec 3
        } catch {
            Start-Sleep -Milliseconds 300
        }
    }
    return $null
}

function Wait-OverlayDown([int]$TimeoutSeconds = 10) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            Invoke-RestMethod -Uri "$BaseUrl/overlay/api/scoreboard/state" -TimeoutSec 2 | Out-Null
            Start-Sleep -Milliseconds 300
        } catch {
            return $true
        }
    }
    return $false
}

function Invoke-Control([string]$Path) {
    $response = Invoke-WebRequest -Uri "$BaseUrl/api/control/$Path" -UseBasicParsing -TimeoutSec 5
    if ($response.StatusCode -ne 200) { throw "control $Path returned $($response.StatusCode)" }
}

# git writes its progress to stderr, and with $ErrorActionPreference = 'Stop' a redirected stderr line from a
# native command becomes a terminating error. This runs one, collects both streams and reports the exit code
# instead, so a failed build can be reported as a failed build.
function Invoke-Native {
    param([string]$FilePath, [string[]]$Arguments)

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $lines = & $FilePath @Arguments 2>&1 | ForEach-Object { "$_" }
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($lines -join [Environment]::NewLine) }
    } finally {
        $ErrorActionPreference = $previous
    }
}

function Stop-ProcessQuietly([System.Diagnostics.Process]$Process) {
    try {
        if (-not $Process.HasExited) { Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue }
    } catch {
        # Already gone; nothing to stop.
    }
}

function Stop-AllStarted {
    foreach ($process in $script:Started) { Stop-ProcessQuietly $process }
    $script:Started.Clear()
    Wait-PortFree 15 | Out-Null
}

function Invoke-CrashResume {
    Write-Step 'crash-resume'
    Remove-Item $MatchPath -ErrorAction SilentlyContinue

    $controller = Start-Controller $Exe
    $state = Wait-State
    Assert-That ($null -ne $state) 'the overlay answers on 127.0.0.1'
    Assert-That (-not $state.isGameActive) 'a launch with no saved match opens at setup'

    Assert-That (Invoke-WindowButton $controller.Id 'START MATCH') 'START MATCH is on the setup screen'
    Start-Sleep -Milliseconds 500
    Invoke-Control 'score/home/add'
    Invoke-Control 'score/home/add'
    Invoke-Control 'score/away/add'
    Start-Sleep -Milliseconds 1500

    Assert-That (Test-Path $MatchPath) 'the debounced save wrote match.json'
    $saved = Get-Content $MatchPath -Raw | ConvertFrom-Json
    Assert-That ($saved.homeScore -eq 2 -and $saved.awayScore -eq 1) "the snapshot holds 2-1 (got $($saved.homeScore)-$($saved.awayScore))"

    # A forced kill, not a close: this is the crash, and it skips OnExit entirely.
    Stop-ProcessQuietly $controller
    Assert-That (Wait-PortFree 20) 'the killed process released the port'
    Assert-That (Test-Path $MatchPath) 'match.json survived the crash'

    $relaunched = Start-Controller $Exe
    $resumed = Wait-State
    Assert-That ($null -ne $resumed) 'the relaunched Controller serves the overlay'
    Assert-That ($resumed.isGameActive) 'the relaunch is mid-match, not at setup'
    Assert-That ($resumed.home.score -eq 2 -and $resumed.away.score -eq 1) "the scores came back (got $($resumed.home.score)-$($resumed.away.score))"
    Assert-That (Test-Path $MatchPath) 'the resume did not delete the file it was reading'

    Invoke-Control 'match/reset'
    Start-Sleep -Milliseconds 1500
    Assert-That (-not (Test-Path $MatchPath)) 'RESET MATCH deletes the snapshot'
}

function Invoke-BannerRetry {
    Write-Step 'banner-retry'
    Assert-That ((Get-PortOwnerPid) -eq 0) "port $Port is free before the scenario starts"

    # A listener in another process: not this app, so the Controller can only report it.
    $script:BlockerScript = Join-Path ([IO.Path]::GetTempPath()) ("pool-block-$([guid]::NewGuid().ToString('n')).ps1")
    Set-Content -Path $script:BlockerScript -Encoding ASCII -Value @"
`$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
`$listener.Start()
try { Start-Sleep -Seconds 300 } finally { `$listener.Stop() }
"@
    $blocker = Start-Process -FilePath 'powershell.exe' `
        -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $script:BlockerScript) `
        -WindowStyle Hidden -PassThru
    $script:Started.Add($blocker) | Out-Null
    Assert-That (Wait-PortOwner $blocker.Id 20) "a foreign process ($($blocker.Id)) took port $Port"

    $controller = Start-Controller $Exe
    Wait-ControllerWindow $controller.Id | Out-Null
    $banner = Wait-WindowText $controller.Id 'overlay is off|could not start'
    Assert-That ($null -ne $banner) 'the window says the overlay is off'
    Assert-That ($banner -match "$Port") 'the banner names the port'
    Assert-That (Test-RetryButton $controller.Id) 'the banner offers Retry'
    Assert-That ($banner -notmatch 'Close that Controller window') 'it does not tell the operator to close a window that is not ours'
    Assert-That (Wait-OverlayDown) 'nothing is serving the overlay while the port is taken'

    Stop-ProcessQuietly $blocker
    Assert-That (Wait-PortFree 20) 'the port was released'

    Assert-That (Invoke-WindowButton $controller.Id 'Retry') 'Retry was pressed'
    $state = Wait-State
    Assert-That ($null -ne $state) 'the overlay serves after Retry'

    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline -and (Test-RetryButton $controller.Id)) { Start-Sleep -Milliseconds 300 }
    Assert-That (-not (Test-RetryButton $controller.Id)) 'the banner and its Retry are gone'
}

function Invoke-Leftover {
    Write-Step 'leftover'
    Assert-That ((Get-PortOwnerPid) -eq 0) "port $Port is free before the scenario starts"

    $script:Worktree = Join-Path ([IO.Path]::GetTempPath()) ("pool-pre-guard-$([guid]::NewGuid().ToString('n'))")
    $added = Invoke-Native 'git' @('-C', $root, 'worktree', 'add', '--detach', $script:Worktree, $PreGuardCommit)
    if ($added.ExitCode -ne 0) { throw "could not create a worktree at ${PreGuardCommit}: $($added.Output)" }

    $oldProject = Join-Path $script:Worktree 'PoolScoreboard.Controller\PoolScoreboard.Controller.csproj'
    Write-Host "  ..  building the pre-guard Controller ($PreGuardCommit)"
    $built = Invoke-Native 'dotnet' @('build', $oldProject, '--configuration', 'Debug', '--nologo', '-v', 'quiet')
    if ($built.ExitCode -ne 0) { throw "the pre-guard build failed: $($built.Output)" }
    $oldExe = Join-Path $script:Worktree 'PoolScoreboard.Controller\bin\Debug\net8.0-windows\PoolScoreboard.Controller.exe'
    Assert-That (Test-Path $oldExe) 'the pre-guard build produced a Controller'

    $old = Start-Controller $oldExe
    Assert-That (Wait-PortOwner $old.Id 30) "the old Controller ($($old.Id)) holds port $Port"
    Wait-ControllerWindow $old.Id | Out-Null

    $controller = Start-Controller $Exe
    Wait-ControllerWindow $controller.Id | Out-Null
    $banner = Wait-WindowText $controller.Id 'overlay is off|could not start'
    Assert-That ($null -ne $banner) 'the new Controller reports that it could not start'
    Assert-That ($banner -match "PID $($old.Id)") 'the banner names the process holding the port'
    Assert-That ($banner -match 'Close that Controller window') 'a windowed Controller is left alone, with the advice that closes it'
    $old.Refresh()
    Assert-That (-not $old.HasExited) 'the windowed leftover is still running - somebody could be using it'

    Hide-ProcessWindow $old
    $old.Refresh()
    Assert-That ($old.MainWindowHandle -eq [IntPtr]::Zero) "the leftover's window is gone, so it is now a windowless holder"
    Assert-That (Invoke-WindowButton $controller.Id 'Retry') 'Retry was pressed'
    $state = Wait-State
    Assert-That ($null -ne $state) 'the overlay serves on the port the leftover gave up'
    Assert-That (Wait-PortOwner $controller.Id 15) 'the new Controller owns the port now'

    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline -and -not $old.HasExited) { Start-Sleep -Milliseconds 200; $old.Refresh() }
    Assert-That ($old.HasExited) 'the windowless leftover was closed'
}

# Checked before resolving, so a missing build says what to do instead of throwing from Resolve-Path.
if (-not (Test-Path $Exe)) {
    Write-Host "Controller not found at $Exe. Run 'dotnet build' first, or pass -Exe." -ForegroundColor Red
    exit 1
}
$Exe = (Resolve-Path $Exe).Path

Write-Step "verify-live: $Scenario"
Write-Host "  exe:      $Exe"
Write-Host "  port:     $Port"
Write-Host "  app data: $AppData"

$portOwner = Get-PortOwnerPid
if ($portOwner -ne 0) {
    Write-Host "Port $Port is already in use by pid $portOwner. Close it before running this." -ForegroundColor Red
    exit 1
}

if (Test-Path $AppData) {
    $script:AppDataBackup = Join-Path ([IO.Path]::GetTempPath()) ("pool-appdata-$([guid]::NewGuid().ToString('n'))")
    Copy-Item -Recurse -Force $AppData $script:AppDataBackup
    Write-Host "  backed up %AppData%\PoolScoreboard"
}

$scenarios = if ($Scenario -eq 'all') { @('crash-resume', 'banner-retry', 'leftover') } else { @($Scenario) }

try {
    foreach ($name in $scenarios) {
        try {
            switch ($name) {
                'crash-resume' { Invoke-CrashResume }
                'banner-retry' { Invoke-BannerRetry }
                'leftover' { Invoke-Leftover }
            }
        } catch {
            Write-Host "  FAIL  ${name}: $($_.Exception.Message)" -ForegroundColor Red
            $script:Failures.Add("$name - $($_.Exception.Message)") | Out-Null
        } finally {
            Stop-AllStarted
        }
    }
} finally {
    # Every scenario stops what it started in its own finally, so there is nothing left to stop here.
    if ($script:Worktree) {
        # A worktree whose registration is half made (an interrupted add) cannot be removed by git, so fall back to
        # deleting the folder and letting prune drop the stale entry.
        $removed = Invoke-Native 'git' @('-C', $root, 'worktree', 'remove', '--force', $script:Worktree)
        if ($removed.ExitCode -ne 0) {
            Invoke-Native 'git' @('-C', $root, 'worktree', 'unlock', $script:Worktree) | Out-Null
            $removed = Invoke-Native 'git' @('-C', $root, 'worktree', 'remove', '--force', $script:Worktree)
        }
        Remove-Item -Recurse -Force $script:Worktree -ErrorAction SilentlyContinue
        if ($removed.ExitCode -ne 0) { Invoke-Native 'git' @('-C', $root, 'worktree', 'prune') | Out-Null }
    }

    if ($script:BlockerScript) { Remove-Item $script:BlockerScript -ErrorAction SilentlyContinue }

    if ($script:AppDataBackup) {
        if (Test-Path $AppData) { Remove-Item -Recurse -Force $AppData }
        Copy-Item -Recurse -Force $script:AppDataBackup $AppData
        Remove-Item -Recurse -Force $script:AppDataBackup -ErrorAction SilentlyContinue
        Write-Host '  restored %AppData%\PoolScoreboard'
    }
}

Write-Step 'summary'
if ($script:Failures.Count -eq 0) {
    Write-Host '  all live checks passed' -ForegroundColor Green
    exit 0
}

foreach ($failure in $script:Failures) { Write-Host "  FAILED: $failure" -ForegroundColor Red }
exit 1
