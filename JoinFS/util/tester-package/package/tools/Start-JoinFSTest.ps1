# Starts the JoinFS test build with the position estimation log on (-estimationlog) and the
# position estimator and clock model under test (-estimator, -clock), after recording how far this PC's clock is from a
# public time server. Called by the
# "Start JoinFS - ..." batch files in the package root.
# Windows PowerShell 5.1 compatible, ASCII only.

param(
    [Parameter(Mandatory = $true)]
    [string]$Build,
    # the estimator for the other aircraft (JoinFS/Estimation/EstimationRegistry.cs)
    [string]$Estimator = 'ClassicFixed',
    # the clock model that ages the other aircraft's samples (MinOffset; RttHalf is the old one)
    [string]$Clock = 'MinOffset'
)

$ErrorActionPreference = 'Stop'

function Stop-WithMessage([string]$message) {
    Write-Host ""
    Write-Host $message -ForegroundColor Yellow
    Write-Host ""
    Read-Host "Press Enter to close"
    exit 1
}

$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "$Build\JoinFS-$Build.exe"
if (-not (Test-Path $exe)) {
    Stop-WithMessage "Cannot find $exe. Extract the whole zip, and start JoinFS with the batch file in its folder."
}

# a downloaded zip marks every file as coming from the internet, which can make Windows block them
Get-ChildItem $root -Recurse -File | Unblock-File

# a framework-dependent build needs the .NET 8 Desktop Runtime (a self-contained one carries its own)
if (-not (Test-Path (Join-Path $root "$Build\hostfxr.dll"))) {
    $runtimes = Join-Path $env:ProgramFiles 'dotnet\shared\Microsoft.WindowsDesktop.App'
    $found = (Test-Path $runtimes) -and (Get-ChildItem $runtimes -Directory | Where-Object { $_.Name -like '8.*' })
    if (-not $found) {
        Start-Process 'https://dotnet.microsoft.com/download/dotnet/8.0'
        Stop-WithMessage "JoinFS needs the .NET 8 Desktop Runtime. The download page is opening: install 'Desktop Runtime' (Windows x64), then start JoinFS again."
    }
}

# another JoinFS of the same build may be the normal version, without the log
if (Get-Process -Name "JoinFS-$Build" -ErrorAction SilentlyContinue) {
    Stop-WithMessage "JoinFS is already running. Close it, then start the test version again with this batch file."
}

# JoinFS keeps its settings and logs here (shared with an installed JoinFS of the same build)
$storage = Join-Path $env:LOCALAPPDATA "JoinFS-$Build"
New-Item -ItemType Directory -Force -Path $storage | Out-Null

# how far this PC's clock is from a public time server: lets the logs of different PCs be put on
# one time line
Write-Host "Checking this PC's clock against time.windows.com (takes a few seconds)..."
$clockFile = Join-Path $storage ("clock-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + ".txt")
$lines = @(
    ("utc " + (Get-Date).ToUniversalTime().ToString('o')),
    ("computer " + $env:COMPUTERNAME),
    "build $Build",
    "w32tm /stripchart /computer:time.windows.com /samples:5 /dataonly"
)
try {
    $lines += & w32tm /stripchart /computer:time.windows.com /samples:5 /dataonly 2>&1 | ForEach-Object { "$_" }
}
catch {
    $lines += "failed: " + $_.Exception.Message
}
$lines | Set-Content -Path $clockFile -Encoding ASCII

Write-Host "Starting JoinFS ($Build) with the position log on, estimator $Estimator, clock $Clock..."
Start-Process -FilePath $exe -ArgumentList '-estimationlog', '-estimator', $Estimator, '-clock', $Clock -WorkingDirectory (Split-Path $exe)
Write-Host "Done. Fly as usual; afterwards close JoinFS and run 'Collect test logs'."
Start-Sleep -Seconds 4
