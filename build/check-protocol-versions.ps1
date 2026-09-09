#!/usr/bin/env pwsh
# Fails if the JoinFS <-> X-Plane-plugin link constants have drifted between the
# C# app (JoinFS/) and the native plugin (JoinFS-XP/). These pairs are hand-kept
# in sync - there is no shared header - so CI guards them.
#
# Run locally from the repo root:  ./build/check-protocol-versions.ps1

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$errors = @()

function Get-Int([string]$relPath, [string]$pattern) {
    $full = Join-Path $repo $relPath
    $m = Select-String -Path $full -Pattern $pattern -AllMatches | Select-Object -First 1
    if (-not $m) { throw "pattern '$pattern' not found in $relPath" }
    $raw = $m.Matches[0].Groups[1].Value.Trim()
    if ($raw -match '^0x') { return [Convert]::ToInt32($raw, 16) }
    return [int]$raw
}

# --- DATA_VERSION: JoinFS/XPlane.cs  vs  JoinFS-XP/Link.cpp -------------------
$csData  = Get-Int 'JoinFS/XPlane.cs'      'DATA_VERSION\s*=\s*(\d+)'
$cppData = Get-Int 'JoinFS-XP/Link.cpp'    'DATA_VERSION\s*=\s*(\d+)'
if ($csData -ne $cppData) {
    $errors += "DATA_VERSION mismatch: XPlane.cs=$csData  Link.cpp=$cppData"
}

# --- NODE_VERSION low byte: JoinFS/Node.cs  vs  JoinFS-XP/Link.h --------------
$csNode  = Get-Int 'JoinFS/Node.cs'        'VERSION\s*=\s*(0x[0-9a-fA-F]+)'
$cppNode = Get-Int 'JoinFS-XP/Link.h'      'NODE_VERSION\s*=\s*(0x[0-9a-fA-F]+)'
if (($csNode -band 0xff) -ne ($cppNode -band 0xff)) {
    $errors += ("NODE_VERSION low byte mismatch: Node.cs=0x{0:x}  Link.h=0x{1:x}" -f ($csNode -band 0xff), ($cppNode -band 0xff))
}

# --- MAX_* buffer sizes ------------------------------------------------------
$maxConsts = @{
    'MAX_CALLSIGN_LENGTH'  = 'JoinFS/XPlane.cs'
    'MAX_MODEL_LENGTH'     = 'JoinFS/XPlane.cs'
    'MAX_ICAOTYPE_LENGTH'  = 'JoinFS/XPlane.cs'
    'MAX_DATAREF_LENGTH'   = 'JoinFS/XPlane.cs'
    'MAX_NICKNAME_LENGTH'  = 'JoinFS/Network.cs'
}
foreach ($name in $maxConsts.Keys) {
    $cs  = Get-Int $maxConsts[$name]       "$name\s*=\s*(\d+)"
    $cpp = Get-Int 'JoinFS-XP/Link.h'      "$name\s*=\s*(\d+)"
    if ($cs -ne $cpp) {
        $errors += "$name mismatch: C#=$cs  Link.h=$cpp"
    }
}

if ($errors.Count -gt 0) {
    Write-Host "X-Plane link protocol parity check FAILED:" -ForegroundColor Red
    $errors | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    Write-Host "Update both sides together (see JoinFS/XPlane.cs DATA_VERSION doc comment)." -ForegroundColor Yellow
    exit 1
}

$lowByte = '0x{0:x}' -f ($csNode -band 0xff)
Write-Host "X-Plane link protocol parity check OK (DATA_VERSION=$csData, NODE_VERSION low byte=$lowByte)."
