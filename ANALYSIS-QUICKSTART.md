# Quick Start: Analyzing Your JoinFS Latency Traces

## 📝 Your Analysis Results

Based on your trace data, here's what the analyzer found:

### 🔴 CRITICAL ISSUE #1: SimConnect IPC Latency
- **p50:** 11.6 ms (median)
- **p95:** 25.9 ms (95th percentile)
- **p99:** 48.0 ms (worst 1%)

**What this means:** Every time JoinFS asks your simulator for aircraft position, it waits an average of **11.6 milliseconds**. For a 4-aircraft formation, that's ~46ms of lag per update cycle.

**Impact:** At 200 knots (~337 ft/s), a 25ms delay = **8.4 feet of position error**

**Fix:** Use event-driven SimConnect subscriptions instead of polling (GitHub Copilot has prepared an implementation plan for this)

### ⚠️ ISSUE #2: Main Loop (Less Critical)
- **p50:** 119 µs (excellent!)
- **p95:** 509 µs (acceptable)
- **p99:** 847 µs (occasional spikes)

**What this means:** Your main loop is actually **very fast** 95% of the time. The analyzer flagged this as "critical" but it's really **secondary** compared to SimConnect.

### ✅ GOOD NEWS: Network Is Optimized
- **UDP Send:** 11.5 µs median, 64.4 µs p95
- **Packet Processing:** 20.8 µs median, 63.1 µs p95

Your UDP network layer is **near-optimal**. No changes needed here!

---

## 🚀 How to Use the Analysis Scripts

### Option 1: Analyze Your Most Recent Trace (Easiest)

```powershell
cd C:\Users\crist\Documents\sandbox\JoinFS\JoinFS\util
.\Quick-Analysis.ps1 -Latest
```

This automatically finds and analyzes your newest trace file.

### Option 2: Analyze a Specific File

```powershell
cd C:\Users\crist\Documents\sandbox\JoinFS\JoinFS\util
.\Analyze-LatencyTrace.ps1 -JsonPath "C:\path\to\your\latency_trace_manual_20260816_143936.json"
```

### Option 3: Compare Multiple Traces (Before/After Optimization)

```powershell
cd C:\Users\crist\Documents\sandbox\JoinFS\JoinFS\util
.\Quick-Analysis.ps1 -Compare
# Then type: 1,2,3 (to compare traces 1, 2, and 3)
```

### Option 4: Show All Trace Points (Verbose)

```powershell
.\Quick-Analysis.ps1 -Latest -ShowDetails
```

---

## 📊 Understanding Your Results

### Severity Levels

| Icon | Meaning | Action |
|------|---------|--------|
| **[!]** | **CRITICAL** | Fix immediately |
| **[*]** | **WARNING** | Optimization opportunity |
| **[+]** | **GOOD** | No action needed |

### What the Numbers Mean

- **p50** = Median (typical case)
- **p95** = 95th percentile (5% of events are slower)
- **p99** = 99th percentile (1% of events are slower)
- **max** = Worst case ever recorded

**Why p95 matters:** In aerobatic formation, the **slowest 5%** cause visible decoherence. We optimize for consistency, not just averages.

### Performance Targets (Aerobatic Mode)

| Component | Target p95 | Your p95 | Status |
|-----------|-----------|----------|--------|
| SimConnect | < 3 ms | **25.9 ms** | 🔴 **8.6× too slow** |
| Main Loop | < 500 µs | 509 µs | ⚠️ Just over limit |
| UDP Send | < 150 µs | 64 µs | ✅ Excellent |
| Packet Process | < 150 µs | 63 µs | ✅ Excellent |

---

## 🎯 Recommended Next Steps

### Step 1: Fix SimConnect (Highest Impact)

GitHub Copilot has already created a plan for this. Tell Copilot:

```
"Implement the SimConnect Event-Driven Optimization plan"
```

**Expected improvement:** 11.6ms → <500µs = **~23× faster**

### Step 2: Capture a New Trace After the Fix

1. Build the optimized code in Debug mode
2. Fly the same scenario (e.g., 4-ship formation for 2 minutes)
3. Press **Ctrl+Shift+T** (GUI) or **T** key (CONSOLE) to dump trace

### Step 3: Compare Before and After

```powershell
cd C:\Users\crist\Documents\sandbox\JoinFS\JoinFS\util
.\Quick-Analysis.ps1 -Compare
# Select your old trace and new trace
```

You should see:
- SimConnect p95: **25.9ms → <500µs** ✅
- Overall Grade: **D → A+** ✅

### Step 4: Investigate Main Loop Spikes (If Still Present)

If p95 is still >500µs after fixing SimConnect:

```powershell
# Open the CSV file
notepad "C:\Users\crist\Documents\sandbox\JoinFS\latency_trace_manual_[timestamp].csv"

# Filter for TickStart → TickEnd gaps > 500µs
# Identify which subsystem (sim, network, recorder) is between them
```

---

## 🔧 Common Scenarios

### "I can't find my trace files"

Trace files are created in the **workspace root**, not the build output:

```powershell
Get-ChildItem C:\Users\crist\Documents\sandbox\JoinFS\latency_trace_*.json | Sort-Object LastWriteTime | Select-Object -Last 5
```

### "The analyzer says CRITICAL but everything feels fine"

The thresholds are **strict** for aerobatic formation flying. If you're flying casually:

```powershell
.\Analyze-LatencyTrace.ps1 -JsonPath "your_trace.json" -Threshold Casual
```

### "I want to track progress over time"

Export results to text files:

```powershell
# Before optimization
.\Quick-Analysis.ps1 -Latest > baseline.txt

# After each fix
.\Quick-Analysis.ps1 -Latest > after_simconnect_fix.txt
.\Quick-Analysis.ps1 -Latest > after_loop_fix.txt

# Compare
notepad baseline.txt
notepad after_simconnect_fix.txt
```

---

## 📚 More Documentation

- **Full README:** `JoinFS\util\README-ANALYSIS.md`
- **Optimization Plan:** `JoinFS\docs\delay-elimination-plan.md`
- **Trace Implementation:** `JoinFS\JoinFS\Diagnostics\LatencyTracer.cs`

---

## ✅ Action Items for You Right Now

1. ✅ **DONE:** You've already captured trace data
2. ✅ **DONE:** You've analyzed it and found SimConnect is the bottleneck
3. ⏭️ **NEXT:** Tell GitHub Copilot to implement the SimConnect fix:
   ```
   "Implement the SimConnect Event-Driven Optimization plan"
   ```
4. ⏭️ **AFTER THAT:** Capture a new trace and compare results

---

## 💡 Pro Tip: Automated Testing

Create a PowerShell script to run before/after every optimization:

```powershell
# test-performance.ps1
$baseline = .\Quick-Analysis.ps1 -Latest | Out-String
if ($baseline -match "SimConnectRequestSent.*p95.*: (\d+\.\d+) ms") {
	$p95 = [double]$matches[1]
	if ($p95 -lt 5) {
		Write-Host "PASS: SimConnect latency < 5ms" -ForegroundColor Green
	} else {
		Write-Host "FAIL: SimConnect latency $p95 ms" -ForegroundColor Red
	}
}
```

Happy optimizing! 🚀
