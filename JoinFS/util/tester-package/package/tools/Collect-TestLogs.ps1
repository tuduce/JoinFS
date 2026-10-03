# Collects the JoinFS test logs of the last days into one zip on the Desktop, ready to send.
# Called by "Collect test logs.bat" in the package root.
# Windows PowerShell 5.1 compatible, ASCII only.

param(
    [int]$Days = 7
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
# the builds in this package: folders holding JoinFS-<build>.exe
$builds = Get-ChildItem $root -Directory | Where-Object { Test-Path (Join-Path $_.FullName ("JoinFS-" + $_.Name + ".exe")) } | ForEach-Object { $_.Name }

# JoinFS keeps its log files open while it runs
foreach ($build in $builds) {
    if (Get-Process -Name "JoinFS-$build" -ErrorAction SilentlyContinue) {
        Stop-WithMessage "JoinFS is still running. Close it first, then collect the logs again."
    }
}

$name = Read-Host "Your name or callsign (goes in the file name)"
if ([string]::IsNullOrWhiteSpace($name)) { $name = $env:USERNAME }
$name = $name -replace '[^A-Za-z0-9_-]', '_'

$stamp = Get-Date -Format 'yyyyMMdd-HHmm'
$since = (Get-Date).AddDays(-$Days)
$work = Join-Path $env:TEMP "joinfs-test-logs-$stamp"
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Path $work | Out-Null

$estimationFiles = 0
foreach ($build in $builds) {
    $storage = Join-Path $env:LOCALAPPDATA "JoinFS-$build"
    if (-not (Test-Path $storage)) { continue }
    $files = Get-ChildItem $storage -File | Where-Object {
        ($_.Name -like 'estimation-*.csv' -or $_.Name -like 'log-*.txt' -or $_.Name -like 'clock-*.txt') -and $_.LastWriteTime -ge $since
    }
    if ($files) {
        $destination = Join-Path $work $build
        New-Item -ItemType Directory -Path $destination | Out-Null
        $files | Copy-Item -Destination $destination
        $estimationFiles += @($files | Where-Object { $_.Name -like 'estimation-*.csv' }).Count
    }
}

if ($estimationFiles -eq 0) {
    Remove-Item $work -Recurse -Force
    Stop-WithMessage "No position logs from the last $Days days. Was JoinFS started with the 'Start JoinFS' batch file of this package?"
}

# which package this is, and a last clock check to see how the clock drifted during the tests
$info = Join-Path $PSScriptRoot 'package-info.txt'
if (Test-Path $info) { Copy-Item $info -Destination $work }
Write-Host "Checking this PC's clock once more (takes a few seconds)..."
$lines = @(
    ("utc " + (Get-Date).ToUniversalTime().ToString('o')),
    ("computer " + $env:COMPUTERNAME),
    "w32tm /stripchart /computer:time.windows.com /samples:5 /dataonly"
)
try {
    $lines += & w32tm /stripchart /computer:time.windows.com /samples:5 /dataonly 2>&1 | ForEach-Object { "$_" }
}
catch {
    $lines += "failed: " + $_.Exception.Message
}
$lines | Set-Content -Path (Join-Path $work 'clock-at-collection.txt') -Encoding ASCII

$zip = Join-Path ([Environment]::GetFolderPath('Desktop')) "JoinFS-test-logs-$name-$stamp.zip"
Write-Host "Packing $estimationFiles position log(s)..."
Compress-Archive -Path (Join-Path $work '*') -DestinationPath $zip -CompressionLevel Optimal -Force
Remove-Item $work -Recurse -Force

$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host ""
Write-Host "Logs packed: $zip ($size MB)" -ForegroundColor Green

# upload with Windows' own sftp and the package's login key, when the package says where to
# (tools\upload.txt, tools\upload_key). sftp rather than scp: Windows 10's scp speaks the old
# SCP protocol, which an SFTP-only server refuses.
$uploaded = $false
$uploadFile = Join-Path $PSScriptRoot 'upload.txt'
$packageKey = Join-Path $PSScriptRoot 'upload_key'
$canUpload = (Test-Path $uploadFile) -and (Test-Path $packageKey)
if ($canUpload) {
    $upload = @{}
    Get-Content $uploadFile | ForEach-Object { $key, $value = $_ -split ' ', 2; $upload[$key] = $value }
    $userHost, $remoteFolder = $upload['target'] -split ':', 2
    $sftp = Join-Path $env:SystemRoot 'System32\OpenSSH\sftp.exe'
    if (-not (Test-Path $sftp)) {
        Write-Host ""
        Write-Host "Cannot upload: the Windows OpenSSH client is missing (Settings > System > Optional features > OpenSSH Client)." -ForegroundColor Yellow
    }
    else {
        $temp = Join-Path ([IO.Path]::GetTempPath()) ("joinfs-upload-" + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $temp | Out-Null
        try {
            # ssh refuses a private key others can read: use a copy only this user can access
            # (full control, so it can be deleted again)
            $key = Join-Path $temp 'upload_key'
            Copy-Item $packageKey $key
            $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
            & icacls.exe $key /inheritance:r /grant:r "*${sid}:(F)" | Out-Null
            # forward slashes: sftp reads a backslash in a batch file as an escape
            $batch = Join-Path $temp 'upload.sftp'
            ('put "' + $zip.Replace('\', '/') + '" ' + $remoteFolder) | Set-Content -Path $batch -Encoding ASCII
            # the server's keys come with the package: never ask the tester to trust it, refuse an
            # impostor. A relative file name, as ssh splits a path with spaces into several files.
            Push-Location $PSScriptRoot
            try {
                for ($attempt = 1; $attempt -le 2 -and -not $uploaded; $attempt++) {
                    Write-Host ""
                    Write-Host "Uploading..."
                    & $sftp -b $batch -i $key -P $upload['port'] -o StrictHostKeyChecking=yes -o UserKnownHostsFile=known_hosts -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=20 $userHost
                    if ($LASTEXITCODE -eq 0) { $uploaded = $true }
                }
            }
            finally {
                Pop-Location
            }
        }
        finally {
            Remove-Item $temp -Recurse -Force
        }
    }
}

Write-Host ""
if ($uploaded) {
    Write-Host "Uploaded - thank you! The zip stays on your Desktop; you can delete it." -ForegroundColor Green
}
else {
    explorer.exe "/select,`"$zip`""
    if ($canUpload) {
        Write-Host "The logs were not uploaded." -ForegroundColor Yellow
    }
    Get-Content (Join-Path $root 'README.txt') | Where-Object { $_ -like '*Send the logs:*' } | ForEach-Object { Write-Host $_.Trim().TrimStart('3', '.', ' ') }
}
Write-Host ""
Read-Host "Press Enter to close"
