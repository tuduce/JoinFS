<#
.SYNOPSIS
	Quick launcher for latency trace analysis.

.DESCRIPTION
	Convenience wrapper that finds the most recent trace file and analyzes it.
	Can also compare multiple traces or analyze a specific file.

.PARAMETER Latest
	Analyze the most recent trace file in the current directory.

.PARAMETER Compare
	Compare multiple trace files side-by-side.

.PARAMETER Path
	Specific path to a trace file or wildcard pattern.

.EXAMPLE
	.\Quick-Analysis.ps1 -Latest

.EXAMPLE
	.\Quick-Analysis.ps1 -Path "latency_trace_manual_*.json"

.EXAMPLE
	.\Quick-Analysis.ps1 -Compare
#>

[CmdletBinding(DefaultParameterSetName='Latest')]
param(
	[Parameter(ParameterSetName='Latest')]
	[switch]$Latest,

	[Parameter(ParameterSetName='Compare')]
	[switch]$Compare,

	[Parameter(ParameterSetName='Path')]
	[string]$Path,

	[Parameter()]
	[switch]$ShowDetails
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$analyzerScript = Join-Path $scriptDir "Analyze-LatencyTrace.ps1"

# Find trace files in workspace root
$workspaceRoot = Split-Path -Parent $scriptDir
$traceFiles = Get-ChildItem -Path $workspaceRoot -Filter "latency_trace_*.json" -File | 
			  Sort-Object LastWriteTime -Descending

if ($traceFiles.Count -eq 0) {
	Write-Host "No trace files found in workspace root: $workspaceRoot" -ForegroundColor Yellow
	Write-Host ""
	Write-Host "To generate traces:" -ForegroundColor Cyan
	Write-Host "  1. Build JoinFS in Debug mode (any variant: FS2024-Debug, CONSOLE-Debug, etc.)" -ForegroundColor White
	Write-Host "  2. Run JoinFS and connect to a simulator or hub" -ForegroundColor White
	Write-Host "  3. Press Ctrl+Shift+T (GUI) or 'T' key (CONSOLE) to dump manually" -ForegroundColor White
	Write-Host "  4. Or wait for auto-dump at 90% buffer full" -ForegroundColor White
	Write-Host "  5. Or quit JoinFS to trigger shutdown dump" -ForegroundColor White
	Write-Host ""
	exit 1
}

switch ($PSCmdlet.ParameterSetName) {
	'Latest' {
		$targetFile = $traceFiles[0]
		Write-Host "Analyzing most recent trace file:" -ForegroundColor Cyan
		Write-Host "  $($targetFile.Name)" -ForegroundColor White
		Write-Host "  Generated: $($targetFile.LastWriteTime)" -ForegroundColor Gray
		Write-Host ""

		$params = @{
			JsonPath = $targetFile.FullName
		}
		if ($ShowDetails) { $params['ShowDetails'] = $true }

		& $analyzerScript @params
	}

	'Compare' {
		Write-Host "Available trace files:" -ForegroundColor Cyan
		Write-Host ""

		for ($i = 0; $i -lt [Math]::Min(10, $traceFiles.Count); $i++) {
			$file = $traceFiles[$i]
			$age = (Get-Date) - $file.LastWriteTime
			$ageStr = if ($age.TotalHours -lt 1) { 
				"{0:N0} minutes ago" -f $age.TotalMinutes 
			} elseif ($age.TotalDays -lt 1) {
				"{0:N1} hours ago" -f $age.TotalHours
			} else {
				"{0:N1} days ago" -f $age.TotalDays
			}

			Write-Host "  [$($i+1)] " -NoNewline -ForegroundColor Yellow
			Write-Host $file.Name -NoNewline -ForegroundColor White
			Write-Host " ($ageStr)" -ForegroundColor Gray
		}

		Write-Host ""
		Write-Host "Select files to compare (comma-separated numbers, e.g., 1,2,3): " -NoNewline -ForegroundColor Cyan
		$selection = Read-Host

		$indices = $selection -split ',' | ForEach-Object { [int]$_.Trim() - 1 }

		Write-Host ""
		Write-Host ("=" * 80) -ForegroundColor Cyan

		foreach ($idx in $indices) {
			if ($idx -ge 0 -and $idx -lt $traceFiles.Count) {
				$file = $traceFiles[$idx]
				Write-Host ""
				Write-Host "ANALYZING: $($file.Name)" -ForegroundColor Yellow
				Write-Host ("=" * 80) -ForegroundColor Cyan

				$params = @{
					JsonPath = $file.FullName
				}
				if ($ShowDetails) { $params['ShowDetails'] = $true }

				& $analyzerScript @params
			}
		}
	}

	'Path' {
		$matchedFiles = Get-ChildItem -Path $workspaceRoot -Filter $Path -File

		if ($matchedFiles.Count -eq 0) {
			Write-Host "No files matched pattern: $Path" -ForegroundColor Red
			exit 1
		}

		foreach ($file in $matchedFiles) {
			Write-Host ""
			Write-Host "ANALYZING: $($file.Name)" -ForegroundColor Yellow
			Write-Host ("=" * 80) -ForegroundColor Cyan

			$params = @{
				JsonPath = $file.FullName
			}
			if ($ShowDetails) { $params['ShowDetails'] = $true }

			& $analyzerScript @params
		}
	}
}
