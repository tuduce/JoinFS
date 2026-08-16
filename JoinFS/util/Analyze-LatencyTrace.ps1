<#
.SYNOPSIS
	Analyzes JoinFS latency trace JSON files and identifies performance bottlenecks.

.DESCRIPTION
	Parses latency trace JSON output, highlights critical delays, and provides
	actionable recommendations for optimization. Color-codes results based on
	performance thresholds for aerobatic formation flying.

.PARAMETER JsonPath
	Path to the latency trace JSON file to analyze.

.PARAMETER ShowDetails
	Show detailed statistics for all trace points.

.PARAMETER Threshold
	Custom threshold preset: "Aerobatic" (strict), "Casual" (relaxed), or "Custom"

.EXAMPLE
	.\Analyze-LatencyTrace.ps1 -JsonPath "latency_trace_manual_20260816_143936.json"

.EXAMPLE
	.\Analyze-LatencyTrace.ps1 -JsonPath "latency_trace_auto_*.json" -ShowDetails

.EXAMPLE
	Get-ChildItem latency_trace_*.json | Sort-Object LastWriteTime | Select-Object -Last 1 | %{ .\Analyze-LatencyTrace.ps1 -JsonPath $_.FullName }
#>

param(
	[Parameter(Mandatory=$true, Position=0, ValueFromPipeline=$true)]
	[string]$JsonPath,

	[Parameter()]
	[switch]$ShowDetails,

	[Parameter()]
	[ValidateSet("Aerobatic", "Casual", "Custom")]
	[string]$Threshold = "Aerobatic"
)

# Performance thresholds (in microseconds)
$Thresholds = @{
	Aerobatic = @{
		MainLoop_P50 = 200
		MainLoop_P95 = 500
		MainLoop_P99 = 1000
		SimConnect_P50 = 1000
		SimConnect_P95 = 3000
		SimConnect_P99 = 5000
		UdpSend_P50 = 50
		UdpSend_P95 = 150
		UdpSend_P99 = 300
		PacketProcess_P50 = 50
		PacketProcess_P95 = 150
		PacketProcess_P99 = 300
		HubRelay_P50 = 50
		HubRelay_P95 = 200
		HubRelay_P99 = 500
	}
	Casual = @{
		MainLoop_P50 = 500
		MainLoop_P95 = 2000
		MainLoop_P99 = 5000
		SimConnect_P50 = 5000
		SimConnect_P95 = 15000
		SimConnect_P99 = 30000
		UdpSend_P50 = 200
		UdpSend_P95 = 500
		UdpSend_P99 = 1000
		PacketProcess_P50 = 200
		PacketProcess_P95 = 500
		PacketProcess_P99 = 1000
		HubRelay_P50 = 200
		HubRelay_P95 = 500
		HubRelay_P99 = 1000
	}
}

$ActiveThresholds = $Thresholds[$Threshold]

# Color helper functions
function Write-ColorLine {
	param([string]$Text, [string]$Color = "White")
	Write-Host $Text -ForegroundColor $Color
}

function Write-Header {
	param([string]$Text)
	Write-Host ""
	Write-Host ("=" * 80) -ForegroundColor Cyan
	Write-Host " $Text" -ForegroundColor Cyan -NoNewline
	Write-Host ""
	Write-Host ("=" * 80) -ForegroundColor Cyan
}

function Write-SubHeader {
	param([string]$Text)
	Write-Host ""
	Write-Host ("-" * 80) -ForegroundColor DarkCyan
	Write-Host " $Text" -ForegroundColor Yellow
	Write-Host ("-" * 80) -ForegroundColor DarkCyan
}

function Get-StatusColor {
	param([double]$Value, [double]$Good, [double]$Warning)
	if ($Value -le $Good) { return "Green" }
	elseif ($Value -le $Warning) { return "Yellow" }
	else { return "Red" }
}

function Format-Microseconds {
	param([double]$Us)
	if ($Us -lt 1000) {
		return "{0:N1} us" -f $Us
	} elseif ($Us -lt 1000000) {
		return "{0:N2} ms" -f ($Us / 1000)
	} else {
		return "{0:N2} s" -f ($Us / 1000000)
	}
}

function Get-SeverityIcon {
	param([string]$Level)
	switch ($Level) {
		"Critical" { return "[!]" }
		"Warning"  { return "[*]" }
		"Good"     { return "[+]" }
		"Info"     { return "[i]" }
		default    { return "[ ]" }
	}
}

# Main analysis
try {
	# Validate file exists
	if (-not (Test-Path $JsonPath)) {
		Write-ColorLine "ERROR: File not found: $JsonPath" "Red"
		exit 1
	}

	# Read and parse JSON
	Write-ColorLine "Loading trace file: $JsonPath" "Cyan"
	$traceData = Get-Content $JsonPath -Raw | ConvertFrom-Json

	# Display metadata
	Write-Header "TRACE METADATA"
	Write-Host "  Generated At    : " -NoNewline; Write-ColorLine $traceData.metadata.generatedAt "White"
	Write-Host "  Total Entries   : " -NoNewline; Write-ColorLine $traceData.metadata.totalEntries.ToString("N0") "White"
	Write-Host "  Analyzed Entries: " -NoNewline; Write-ColorLine $traceData.metadata.analyzedEntries.ToString("N0") "White"
	Write-Host "  Stopwatch Freq  : " -NoNewline; Write-ColorLine "$($traceData.metadata.stopwatchFrequency.ToString('N0')) Hz" "White"

	if ($traceData.metadata.totalEntries -gt $traceData.metadata.analyzedEntries) {
		$percentAnalyzed = [math]::Round(($traceData.metadata.analyzedEntries / $traceData.metadata.totalEntries) * 100, 1)
		Write-Host "  Coverage        : " -NoNewline
		Write-ColorLine "$percentAnalyzed% (buffer wrapped, recent data only)" "Yellow"
	}

	# Analyze per-hop latencies
	Write-Header "BOTTLENECK ANALYSIS"

	$bottlenecks = @()

	foreach ($hop in $traceData.perHopLatencyUs.PSObject.Properties) {
		$hopName = $hop.Name
		$stats = $hop.Value

		# Determine severity based on hop type
		$severity = "Good"
		$reason = ""
		$thresholdKey = ""

		if ($hopName -like "*TickStart*TickEnd*") {
			$thresholdKey = "MainLoop"
			if ($stats.p95 -gt $ActiveThresholds.MainLoop_P95) {
				$severity = "Critical"
				$reason = "Main loop blocking detected - impacts all subsystems"
			} elseif ($stats.p50 -gt $ActiveThresholds.MainLoop_P50) {
				$severity = "Warning"
				$reason = "Main loop slower than expected"
			}
		}
		elseif ($hopName -like "*SimConnect*") {
			$thresholdKey = "SimConnect"
			if ($stats.p50 -gt $ActiveThresholds.SimConnect_P95) {
				$severity = "Critical"
				$reason = "SimConnect IPC latency dominates - use event-driven updates"
			} elseif ($stats.p50 -gt $ActiveThresholds.SimConnect_P50) {
				$severity = "Warning"
				$reason = "SimConnect polling overhead detected"
			}
		}
		elseif ($hopName -like "*UdpSend*") {
			$thresholdKey = "UdpSend"
			if ($stats.p95 -gt $ActiveThresholds.UdpSend_P99) {
				$severity = "Warning"
				$reason = "UDP send blocking occasionally - check socket buffers"
			}
		}
		elseif ($hopName -like "*PacketProcessed*") {
			$thresholdKey = "PacketProcess"
			if ($stats.p95 -gt $ActiveThresholds.PacketProcess_P99) {
				$severity = "Warning"
				$reason = "Packet deserialization/processing slow"
			}
		}
		elseif ($hopName -like "*HubRelay*") {
			$thresholdKey = "HubRelay"
			if ($stats.p95 -gt $ActiveThresholds.HubRelay_P99) {
				$severity = "Warning"
				$reason = "Hub relay adding significant overhead"
			}
		}

		$bottlenecks += [PSCustomObject]@{
			Hop = $hopName
			Count = $stats.count
			P50 = $stats.p50
			P95 = $stats.p95
			P99 = $stats.p99
			Max = $stats.max
			Mean = $stats.mean
			Severity = $severity
			Reason = $reason
			ThresholdKey = $thresholdKey
		}
	}

	# Sort by severity and p95 latency
	$bottlenecks = $bottlenecks | Sort-Object @{Expression={
		switch($_.Severity) {
			"Critical" {0}
			"Warning" {1}
			"Good" {2}
		}
	}}, @{Expression={$_.P95}; Descending=$true}

	# Display ranked bottlenecks
	$rank = 1
	foreach ($item in $bottlenecks) {
		$icon = Get-SeverityIcon $item.Severity
		$color = switch ($item.Severity) {
			"Critical" { "Red" }
			"Warning" { "Yellow" }
			"Good" { "Green" }
		}

		Write-Host ""
		Write-Host "[$rank] $icon " -NoNewline -ForegroundColor $color
		Write-Host $item.Hop -ForegroundColor White
		Write-Host "    Status    : " -NoNewline
		Write-ColorLine $item.Severity.ToUpper() $color
		Write-Host "    Count     : " -NoNewline
		Write-ColorLine $item.Count.ToString("N0") "White"
		Write-Host "    p50       : " -NoNewline
		Write-ColorLine (Format-Microseconds $item.P50) (Get-StatusColor $item.P50 ($ActiveThresholds."$($item.ThresholdKey)_P50") ($ActiveThresholds."$($item.ThresholdKey)_P95"))
		Write-Host "    p95       : " -NoNewline
		Write-ColorLine (Format-Microseconds $item.P95) (Get-StatusColor $item.P95 ($ActiveThresholds."$($item.ThresholdKey)_P95") ($ActiveThresholds."$($item.ThresholdKey)_P99"))
		Write-Host "    p99       : " -NoNewline
		Write-ColorLine (Format-Microseconds $item.P99) "White"
		Write-Host "    max       : " -NoNewline
		Write-ColorLine (Format-Microseconds $item.Max) "White"

		if ($item.Reason) {
			Write-Host "    Impact    : " -NoNewline
			Write-ColorLine $item.Reason $color
		}

		$rank++
	}

	# Top delays analysis
	Write-Header "TOP DELAY SPIKES"

	if ($traceData.topDelays -and $traceData.topDelays.Count -gt 0) {
		Write-Host "  Largest gaps between consecutive trace points (typically between loop iterations):"
		Write-Host ""

		for ($i = 0; $i -lt [Math]::Min(5, $traceData.topDelays.Count); $i++) {
			$delay = $traceData.topDelays[$i]
			$delayMs = [math]::Round($delay.delayUs / 1000, 2)

			$color = if ($delayMs -gt 10) { "Red" } 
					elseif ($delayMs -gt 5) { "Yellow" }
					else { "White" }

			Write-Host "  [$($i+1)] " -NoNewline
			Write-Host (Format-Microseconds $delay.delayUs) -ForegroundColor $color -NoNewline
			Write-Host " gap between " -NoNewline
			Write-Host $delay.fromPoint -ForegroundColor Cyan -NoNewline
			Write-Host " -> " -NoNewline
			Write-Host $delay.toPoint -ForegroundColor Cyan
			Write-Host "       @ " -NoNewline
			Write-Host "$($delay.timestampMs.ToString('N3')) ms" -ForegroundColor DarkGray -NoNewline
			Write-Host " into trace"
		}

		$maxDelay = $traceData.topDelays[0]
		if ($maxDelay.delayUs -gt 50000) {
			Write-Host ""
			Write-ColorLine "  [*] WARNING: Detected abnormally large gap of $(Format-Microseconds $maxDelay.delayUs)" "Yellow"
			Write-ColorLine "      This may indicate a debugger pause, system sleep, or hang." "Yellow"
		}
	} else {
		Write-ColorLine "  No significant delay spikes detected." "Green"
	}

	# Recommendations
	Write-Header "RECOMMENDATIONS"

	$critical = $bottlenecks | Where-Object {$_.Severity -eq "Critical"}
	$warnings = $bottlenecks | Where-Object {$_.Severity -eq "Warning"}

	if ($critical.Count -eq 0 -and $warnings.Count -eq 0) {
		Write-ColorLine "  [+] Excellent! No major bottlenecks detected." "Green"
		Write-ColorLine "     Your latency profile is well-optimized for aerobatic formation flying." "Green"
	} else {
		Write-Host ""
		$priority = 1

		foreach ($item in $critical) {
			Write-Host "  [$priority] " -NoNewline -ForegroundColor Red
			Write-Host "CRITICAL: " -NoNewline -ForegroundColor Red
			Write-Host $item.Hop -ForegroundColor White
			Write-ColorLine "      $($item.Reason)" "Red"

			# Specific recommendations
			if ($item.Hop -like "*SimConnect*") {
				Write-ColorLine "      -> Switch from RequestData() polling to RequestDataOnSimObject() with SIM_FRAME period" "Yellow"
				Write-ColorLine "      -> This will reduce latency from ~$(Format-Microseconds $item.P50) to <500us" "Yellow"
				Write-ColorLine "      -> Expected improvement: ~50x faster for position updates" "Yellow"
			}
			elseif ($item.Hop -like "*TickStart*TickEnd*") {
				Write-ColorLine "      -> Profile which subsystem in DoWork() is blocking" "Yellow"
				Write-ColorLine "      -> Consider splitting work across multiple ticks or async operations" "Yellow"
				Write-ColorLine "      -> Check for I/O or synchronous network calls inside lock(conch)" "Yellow"
			}

			Write-Host ""
			$priority++
		}

		foreach ($item in $warnings) {
			Write-Host "  [$priority] " -NoNewline -ForegroundColor Yellow
			Write-Host "Warning: " -NoNewline -ForegroundColor Yellow
			Write-Host $item.Hop -ForegroundColor White
			Write-ColorLine "      $($item.Reason)" "Yellow"
			Write-Host ""
			$priority++
		}
	}

	# Trace point coverage
	if ($ShowDetails) {
		Write-Header "TRACE POINT COVERAGE"

		$counts = $traceData.tracePointCounts.PSObject.Properties | 
				  Sort-Object {$_.Value} -Descending

		foreach ($point in $counts) {
			Write-Host "  " -NoNewline
			Write-Host ("{0,-40}" -f $point.Name) -NoNewline -ForegroundColor Cyan
			Write-Host " : " -NoNewline
			Write-ColorLine $point.Value.ToString("N0") "White"
		}
	}

	# Summary score
	Write-Header "OVERALL LATENCY SCORE"

	$criticalCount = $critical.Count
	$warningCount = $warnings.Count

	if ($criticalCount -eq 0 -and $warningCount -eq 0) {
		Write-Host "  Grade: " -NoNewline
		Write-ColorLine "A+ (EXCELLENT)" "Green"
		Write-ColorLine "  Your system is optimized for low-latency multiplayer flying." "Green"
	}
	elseif ($criticalCount -eq 0 -and $warningCount -le 2) {
		Write-Host "  Grade: " -NoNewline
		Write-ColorLine "B+ (GOOD)" "Yellow"
		Write-ColorLine "  Minor optimizations available but performance is acceptable." "Yellow"
	}
	elseif ($criticalCount -le 1) {
		Write-Host "  Grade: " -NoNewline
		Write-ColorLine "C (NEEDS IMPROVEMENT)" "Yellow"
		Write-ColorLine "  Significant optimization opportunity exists." "Yellow"
	}
	else {
		Write-Host "  Grade: " -NoNewline
		Write-ColorLine "D (CRITICAL ISSUES)" "Red"
		Write-ColorLine "  Major bottlenecks detected - immediate action recommended." "Red"
	}

	Write-Host ""
	Write-Host ("=" * 80) -ForegroundColor Cyan
	Write-Host ""

	# Export summary if requested
	if ($PSBoundParameters.ContainsKey('OutFile')) {
		$summary = @{
			AnalyzedFile = $JsonPath
			Timestamp = Get-Date
			CriticalIssues = $criticalCount
			Warnings = $warningCount
			Bottlenecks = $bottlenecks
			Threshold = $Threshold
		}
		$summary | ConvertTo-Json -Depth 5 | Out-File $OutFile
		Write-ColorLine "Summary exported to: $OutFile" "Cyan"
	}

} catch {
	Write-ColorLine "ERROR: Failed to analyze trace file" "Red"
	Write-ColorLine "  $($_.Exception.Message)" "Red"
	Write-ColorLine "  $($_.ScriptStackTrace)" "DarkRed"
	exit 1
}
