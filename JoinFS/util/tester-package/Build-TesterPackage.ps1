<#
.SYNOPSIS
Builds a zip for testers: JoinFS builds that start with the position estimation log on, plus
scripts to start them and to collect the logs afterwards (docs/position-estimation-plan.md 6.1).

.EXAMPLE
.\JoinFS\util\tester-package\Build-TesterPackage.ps1 -UploadTarget user@host.example:/upload/ -UploadPort 2224

.EXAMPLE
.\JoinFS\util\tester-package\Build-TesterPackage.ps1 -UploadInfo "upload the zip to https://drive.example/folder"

.EXAMPLE
.\JoinFS\util\tester-package\Build-TesterPackage.ps1 -Builds FS2024 -SelfContained
#>
param(
    # which simulator builds to include (X-Plane needs its plugin installed, so it is left out)
    [ValidateSet('FS2024', 'FS2020', 'FSX', 'P3D')]
    [string[]]$Builds = @('FS2024', 'FS2020'),
    # carry the .NET runtime in the package (about 60 MB more per build), so testers need not install it
    [switch]$SelfContained,
    # where testers send the collected logs, when the collect script does not upload them
    [string]$UploadInfo = 'send the zip to the test organiser.',
    # scp target the collect script uploads to, e.g. user@host:/path/ - passed here rather than
    # written in the repository; the password is given to the testers separately
    [string]$UploadTarget,
    [int]$UploadPort = 22,
    # where the package goes (default: artifacts\ in the repository)
    [string]$OutputFolder
)

$ErrorActionPreference = 'Stop'

<#
The upload server's host keys, as known_hosts lines, so that testers are never asked to trust
it and an impostor is refused. Taken by connecting once per key type without authenticating
(BatchMode): Windows' ssh-keyscan fails against current OpenSSH servers (it offers a key
exchange it does not support), while ssh itself negotiates fine.
#>
function Get-HostKeys([string]$userHost, [int]$port) {
    # ssh reports the refused login on stderr, which must not stop the script
    $ErrorActionPreference = 'Continue'
    $ssh =Join-Path $env:SystemRoot 'System32\OpenSSH\ssh.exe'
    if (-not (Test-Path $ssh)) { $ssh = 'ssh' }
    $work = Join-Path ([IO.Path]::GetTempPath()) ("joinfs-hostkeys-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work | Out-Null
    # a relative file name: ssh would split a path with spaces into several files
    Push-Location $work
    try {
        $keys = @()
        foreach ($algorithm in 'ssh-ed25519', 'ecdsa-sha2-nistp256', 'rsa-sha2-512') {
            # a file per type: accept-new adds no key of another type once the host has one.
            # Fails at authentication by design; only the host key is wanted.
            $file = "known_hosts_$algorithm"
            & $ssh -p $port -o "HostKeyAlgorithms=$algorithm" -o StrictHostKeyChecking=accept-new -o "UserKnownHostsFile=$file" -o BatchMode=yes -o ConnectTimeout=15 $userHost exit 2>&1 | Out-Null
            if (Test-Path $file) { $keys += @(Get-Content $file | Where-Object { $_.Trim() }) }
        }
        return $keys
    }
    finally {
        Pop-Location
        Remove-Item $work -Recurse -Force
    }
}

$repo =(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$project = Join-Path $repo 'JoinFS\JoinFS.csproj'
$template = Join-Path $PSScriptRoot 'package'
if (-not $OutputFolder) { $OutputFolder = Join-Path $repo 'artifacts' }

# the package is named after the commit it was built from
$commit = (git -C $repo rev-parse --short HEAD).Trim()
$branch = (git -C $repo rev-parse --abbrev-ref HEAD).Trim()
if (git -C $repo status --porcelain) {
    Write-Warning "There are uncommitted changes: the package will not match commit $commit."
    $commit += '-dirty'
}
$name = "JoinFS-test-$commit"
$stage = Join-Path $OutputFolder $name
$zip = "$stage.zip"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }
New-Item -ItemType Directory -Path $stage | Out-Null

$displayNames = @{ FS2024 = 'MSFS 2024'; FS2020 = 'MSFS 2020'; FSX = 'FSX'; P3D = 'Prepar3D' }
# FSX and P3D are x86 because of their SimConnect DLL
$runtimes = @{ FS2024 = 'win-x64'; FS2020 = 'win-x64'; FSX = 'win-x86'; P3D = 'win-x86' }

foreach ($build in $Builds) {
    Write-Host "Publishing $build..."
    $publishArgs = @('publish', $project, '-c', $build, '-o', (Join-Path $stage $build), '-nologo', '-v', 'quiet')
    if ($SelfContained) { $publishArgs += @('-r', $runtimes[$build], '--self-contained', 'true') }
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $build" }
}

# scripts and README
Copy-Item (Join-Path $template '*') -Destination $stage -Recurse

$startFiles = @()
foreach ($build in $Builds) {
    $bat = "Start JoinFS - " + $displayNames[$build] + ".bat"
    $startFiles += "     - " + $bat
    @(
        '@echo off',
        "powershell -NoProfile -ExecutionPolicy Bypass -File `"%~dp0tools\Start-JoinFSTest.ps1`" -Build $build",
        'if errorlevel 1 pause'
    ) | Set-Content -Path (Join-Path $stage $bat) -Encoding ASCII
}
@(
    '@echo off',
    'powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Collect-TestLogs.ps1"',
    'if errorlevel 1 pause'
) | Set-Content -Path (Join-Path $stage 'Collect test logs.bat') -Encoding ASCII

# the upload: target, port and the server's pinned host keys
$sendLogs = $UploadInfo
if ($UploadTarget) {
    $userHost = $UploadTarget.Split(':')[0]
    Write-Host "Fetching the host keys of $userHost (port $UploadPort)..."
    $hostKeys = Get-HostKeys $userHost $UploadPort
    if ($hostKeys.Count -eq 0) { throw "Could not get the host keys of $userHost on port $UploadPort" }
    $hostKeys | Set-Content -Path (Join-Path $stage 'tools\known_hosts') -Encoding ASCII
    @(
        "target $UploadTarget",
        "port $UploadPort"
    ) | Set-Content -Path (Join-Path $stage 'tools\upload.txt') -Encoding ASCII
    $sendLogs = "the collect script uploads the zip for you. When it asks for a password, type the upload password you were given (nothing shows while you type) and press Enter. If the upload fails, $UploadInfo"
}

$readme = Join-Path $stage 'README.txt'
(Get-Content $readme -Raw).Replace('{{VERSION}}', "$commit ($branch)").Replace('{{UPLOAD}}', $sendLogs).Replace('{{STARTFILES}}', ($startFiles -join "`r`n")) |
    Set-Content -Path $readme -Encoding ASCII -NoNewline

@(
    "commit $commit",
    "branch $branch",
    ("built " + (Get-Date).ToUniversalTime().ToString('o')),
    ("builds " + ($Builds -join ' ')),
    "selfContained $([bool]$SelfContained)"
) | Set-Content -Path (Join-Path $stage 'tools\package-info.txt') -Encoding ASCII

Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host ""
Write-Host "Package: $zip ($size MB)" -ForegroundColor Green
