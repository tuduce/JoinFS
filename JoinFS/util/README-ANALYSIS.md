# JoinFS Latency Trace Analysis Tools

PowerShell scripts for analyzing JoinFS multiplayer latency and identifying performance bottlenecks.

## 📁 Files

- **`Analyze-LatencyTrace.ps1`** — Core analysis engine with color-coded output and recommendations
- **`Quick-Analysis.ps1`** — Convenience wrapper for quick analysis workflows

## 🚀 Quick Start

### Analyze the Most Recent Trace

```powershell
cd JoinFS\util
.\Quick-Analysis.ps1 -Latest
```

### Analyze a Specific File

```powershell
.\Analyze-LatencyTrace.ps1 -JsonPath "..\latency_trace_manual_20260816_143936.json"
```

### Compare Multiple Traces

```powershell
.\Quick-Analysis.ps1 -Compare
# Then select files by number (e.g., 1,2,3)
```

### Verbose Mode (Show All Trace Points)

```powershell
.\Quick-Analysis.ps1 -Latest -Verbose
```

## 📊 Output Sections

The analyzer provides:

### 1. **Trace Metadata**
- When the trace was captured
- How many events were recorded
- Buffer coverage (warns if buffer wrapped)

### 2. **Bottleneck Analysis**
Ranked list of performance issues with color coding:
- 🔴 **Critical** — Immediate action required (p95 > strict threshold)
- ⚠️ **Warning** — Optimization opportunity (p95 > relaxed threshold)
- ✅ **Good** — No issues detected

For each bottleneck:
- **p50** (median), **p95** (95th percentile), **p99** (99th percentile), **max**
- Frequency (count)
- Impact assessment
- Root cause hypothesis

### 3. **Top Delay Spikes**
Largest gaps between events, useful for identifying:
- OS scheduler preemptions
- Debugger pauses
- System sleep/hibernation
- Lock contention

### 4. **Recommendations**
Prioritized action items with specific fixes:
- Code changes
- Configuration tweaks
- Architecture improvements

### 5. **Overall Latency Score**
Letter grade (A+ to D) based on bottleneck severity.

## 🎯 Performance Thresholds

### Aerobatic Mode (Default, Strict)
Optimized for tight formation flying where every millisecond counts:

| Component | p50 Target | p95 Target | p99 Target |
|-----------|-----------|-----------|-----------|
| Main Loop | < 200 µs | < 500 µs | < 1000 µs |
| SimConnect | < 1 ms | < 3 ms | < 5 ms |
| UDP Send | < 50 µs | < 150 µs | < 300 µs |
| Packet Process | < 50 µs | < 150 µs | < 300 µs |
| Hub Relay | < 50 µs | < 200 µs | < 500 µs |

### Casual Mode (Relaxed)
For general multiplayer flying:

```powershell
.\Analyze-LatencyTrace.ps1 -JsonPath "trace.json" -Threshold Casual
```

Thresholds are 5-10× more relaxed.

## 📈 Interpreting Results

### Example: SimConnect Bottleneck

```
[1] 🔴 SimConnectRequestSent → SimConnectDataReceived
	Status    : CRITICAL
	Count     : 570
	p50       : 11.6 ms
	p95       : 25.9 ms
	p99       : 48.0 ms
	max       : 55.5 ms
	Impact    : SimConnect IPC latency dominates - use event-driven updates

	→ Switch from RequestData() polling to RequestDataOnSimObject() with SIM_FRAME period
	→ This will reduce latency from ~11.6 ms to <500µs
	→ Expected improvement: ~50× faster for position updates
```

**What this means:**
- Every time JoinFS asks the simulator for aircraft position, it waits **11.6ms on average**
- For a 4-aircraft formation, that's **~46ms of total lag per update cycle**
- **Fix:** Use event-driven SimConnect subscriptions instead of polling (see delay-elimination-plan.md)

### Example: Excellent Performance

```
[1] ✅ UdpSendCalled → UdpSendCompleted
	Status    : GOOD
	p50       : 11.5 µs
	p95       : 64.4 µs

[2] ✅ UdpPacketReceived → PacketProcessed
	Status    : GOOD
	p50       : 20.8 µs
	p95       : 63.1 µs
```

**What this means:**
- UDP network layer is **near-optimal**
- No optimization needed here

## 🔧 Advanced Usage

### Analyze All Auto-Dumps

```powershell
Get-ChildItem ..\latency_trace_auto_*.json | ForEach-Object {
	Write-Host "=== $($_.Name) ===" -ForegroundColor Cyan
	.\Analyze-LatencyTrace.ps1 -JsonPath $_.FullName
}
```

### Filter for Critical Issues Only

```powershell
$result = .\Analyze-LatencyTrace.ps1 -JsonPath "trace.json" | Out-String
if ($result -match "CRITICAL") {
	Write-Host "Bottlenecks detected!" -ForegroundColor Red
	# Send alert, log to file, etc.
}
```

### Generate Report for Multiple Flights

```powershell
# Run before optimization
.\Quick-Analysis.ps1 -Latest > before.txt

# ... apply fixes ...

# Run after optimization
.\Quick-Analysis.ps1 -Latest > after.txt

# Compare
Compare-Object (Get-Content before.txt) (Get-Content after.txt)
```

## 📝 Typical Workflow

1. **Capture Baseline Trace**
   - Build Debug configuration
   - Fly a typical scenario (e.g., 4-ship formation)
   - Let auto-dump trigger or press Ctrl+Shift+T

2. **Analyze**
   ```powershell
   .\Quick-Analysis.ps1 -Latest
   ```

3. **Identify Top Issue**
   - Look at bottleneck #1 (highest severity + latency)
   - Read the recommendation

4. **Apply Fix**
   - Implement suggested code change
   - Rebuild

5. **Capture New Trace**
   - Fly same scenario
   - Dump trace again

6. **Compare Results**
   ```powershell
   .\Quick-Analysis.ps1 -Compare
   # Select before and after traces
   ```

7. **Repeat** for next bottleneck

## 🐛 Troubleshooting

### "No trace files found"
- Make sure you're building **Debug** configuration (LATENCY_TRACE is only enabled in Debug)
- Trace files are created in the workspace root, not the build output directory
- Check if JoinFS ran long enough to trigger auto-dump or press Ctrl+Shift+T manually

### "ERROR: Failed to analyze trace file"
- Check that the JSON file is valid (open in text editor)
- Verify the file isn't locked by another process
- Ensure PowerShell execution policy allows scripts:
  ```powershell
  Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned
  ```

### High Latency But No Critical Issues
- Try `-Threshold Aerobatic` for stricter checks
- Use `-Verbose` to see all trace point counts
- Check "Top Delay Spikes" section for intermittent issues

## 🎓 Understanding the Metrics

### Percentiles (p50, p95, p99)
- **p50 (median)** — Typical case, 50% of events are faster
- **p95** — 95% of events are faster (captures "normal bad" cases)
- **p99** — 99% of events are faster (captures rare spikes)
- **max** — Worst-case outlier

**Why p95 matters:** In aerobatic formation flying, the **slowest 5%** of updates cause visible decoherence. We optimize for consistency, not just average performance.

### Hop Latencies
A "hop" is the time between two consecutive trace points:
- **TickStart → TickEnd** — How long the main loop takes
- **SimConnectRequestSent → DataReceived** — SimConnect roundtrip time
- **UdpSendCalled → UdpSendCompleted** — How long UDP socket operations take
- **UdpPacketReceived → PacketProcessed** — Deserialization + processing time
- **HubRelayIn → HubRelayOut** — CONSOLE hub forwarding overhead

### Top Delays
These are **gaps** between events, not hop durations. Large gaps indicate:
- OS scheduler moved JoinFS to another CPU core
- System was busy with another process
- Debugger was attached and paused
- Network packet loss caused a long wait

## 📚 Related Documentation

- **[delay-elimination-plan.md](../docs/delay-elimination-plan.md)** — Full latency analysis and optimization roadmap
- **[LatencyTracer.cs](../JoinFS/Diagnostics/LatencyTracer.cs)** — Ring buffer tracing implementation
- **[LatencyReport.cs](../JoinFS/Diagnostics/LatencyReport.cs)** — JSON/CSV report generation

## 🤝 Contributing

Found a bottleneck pattern not covered? Add it to `Analyze-LatencyTrace.ps1`:

1. Define threshold in `$Thresholds` hash table
2. Add detection logic in the `foreach ($hop in $traceData.perHopLatencyUs...)` loop
3. Add recommendation in the `RECOMMENDATIONS` section

## 📄 License

Same license as JoinFS (see workspace root LICENSE file).
