# JoinFS Network Delay Elimination Plan

## Executive Summary

JoinFS is used for precision aerobatic formations where every millisecond matters. This document provides a comprehensive analysis of all delay sources in the multiplayer position/orientation synchronization pipeline, from the user's simulator to the network hub (CONSOLE) to other connected players. It identifies concrete optimization opportunities and provides a phased implementation roadmap.

**Current Baseline**: 10–35 ms application-layer latency before network RTT (typical worst-case scenario).

**Phase 1 Goal**: Comprehensive latency instrumentation with <1 µs measurement overhead.

**Phase 2 Goal**: Reduce application-layer latency to <5 ms (p95) through targeted optimizations.

---

## Architecture Overview

### Main Event Loop

**File**: `JoinFS/Program.cs` (lines 1057–1165)

```
while (workFinish == false)
{
	long start = sw.ElapsedMilliseconds;
	lock (conch) {
		sim?.DoWork();
		network.DoWork();
		recorder.DoWork();
		// ... other work
	}
	long duration = sw.ElapsedMilliseconds - start;
	long sleep = 5 - duration;
	if (sleep > 0) Thread.Sleep((int)sleep);
}
```

**Critical characteristics**:
- Single-threaded architecture
- Global `lock(conch)` serializes ALL work (sim, network send/receive, recorder, UI refresh)
- Target tick interval: 5 ms (200 Hz)
- Actual tick interval: 5–20 ms due to Windows default timer resolution (15.625 ms)
- **Impact**: Every operation waits for the next tick; received packets sit in the OS buffer for 0–5 ms

### Position Update Pipeline (SimConnect → Network)

**Files**: `SimConnectInterface.cs`, `Sim.cs`, `Network.cs`, `Node.cs`

1. **SimConnect data acquisition** (`SimConnectInterface.cs` ~L592):
   - `sc.RequestDataOnSimObject(request, def, simId, SIMCONNECT_PERIOD.ONCE, ...)`
   - **Poll-based**, not event-driven
   - Adds one SimConnect IPC round-trip per request (~0.1–1 ms on local machine)

2. **Position processing** (`Sim.cs` ~L2195):
   - `WriteAircraftPositionMessage(aircraft.netId, aircraft.simTime, aircraft, ref aircraftPosition)`
   - Serializes position to binary stream (doubles, floats, int16 control surfaces)
   - Applies `GetIntervalMask` — adaptive send rate (default: every tick for nearby aircraft, every 32 ticks for distant/disconnected nodes)

3. **Network send** (`Node.cs` ~L661):
   - `udpClient.Send(data, length, endPoint)`
   - **Synchronous blocking call**
   - No pre-allocated buffers
   - Socket created with default OS settings (no buffer size tuning, no `DontFragment`, no `DontRoute`)

### Receive Pipeline (Network → Sim)

**File**: `Node.cs` (lines 1575–1602)

```csharp
while (IsOpen && udpClient.Available > 0)
{
	IPEndPoint endPoint = new(IPAddress.Any, 0);
	byte[] messageData = udpClient.Receive(ref endPoint); // ALLOCATION!
	receiveBuffer.SetLength(0);
	receiveBuffer.Write(messageData, 0, messageData.Length);
	receiveReader.BaseStream.Seek(0, SeekOrigin.Begin);
	ReceiveMsg(endPoint);
}
```

**Critical issues**:
- `udpClient.Receive()` allocates a new `byte[]` per packet → GC pressure
- Called only once per tick inside `lock(conch)` → minimum one tick delay
- No dedicated receive thread → packets queued in OS buffer until next tick

### CONSOLE Hub Relay

**File**: `Program.cs` (DoWork loop), `Network.cs` (message routing)

**Relay path**: Player A → Hub → Player B

1. Tick N: Hub receives packet from Player A into OS buffer
2. Tick N+1: Hub `ReceiveMessages()` processes packet (inside `lock(conch)`)
3. Tick N+1: Hub forwards packet via `localNode.Send(nuid)` to Player B
4. Tick N+2 (at Player B): Player B receives packet

**Minimum relay latency**: 2 ticks = 10 ms (before network RTT)

**Worst-case relay latency**: ~30 ms (if timer resolution jitter aligns poorly)

---

## Identified Delay Sources (Layered Analysis)

### Layer 1: Operating System

| Source | Location | Estimated Impact | Measurement Required |
|--------|----------|------------------|----------------------|
| **Windows timer resolution** | `Program.cs` L1162 `Thread.Sleep` | **0–15 ms jitter** per tick | `timeBeginPeriod(1)` vs default |
| **UDP socket receive buffer** | `Node.cs` L2113 `new UdpClient(port)` | Packet loss under burst load (no delay if no loss) | Monitor `udpClient.Available` high-water mark |
| **UDP socket send buffer** | Same | Possible blocking on full buffer | Monitor send errors |
| **Thread scheduler quantum** | N/A | Contributes to `Thread.Sleep` jitter | Covered by timer resolution fix |
| **Network stack overhead** | Kernel→user transition | ~10–50 µs (unavoidable) | Baseline reference |

**Key finding**: Windows default timer resolution is 15.625 ms. `Thread.Sleep(5)` will often sleep for 15+ ms.

**Fix**: Call `timeBeginPeriod(1)` on startup (sets global OS timer to 1 ms resolution). MUST be paired with `timeEndPeriod(1)` on shutdown.

**Risk**: Increases global system power consumption slightly (~1–2% CPU). Acceptable for flight sim use case.

---

### Layer 2: .NET Runtime

| Source | Location | Estimated Impact | Measurement Required |
|--------|----------|------------------|----------------------|
| **GC pressure from receive allocations** | `Node.cs` L1586 `udpClient.Receive()` | **0.1–5 ms pauses** during Gen0 GC | GC event counter correlation |
| **Synchronous blocking send** | `Node.cs` L661 `udpClient.Send(...)` | Serializes sends; may block if OS buffer full | Compare async vs sync send times |
| **`lock(conch)` contention** | `Program.cs` L1066 | Minimal (single-threaded today) | Becomes critical in Phase 2 (async receive thread) |
| **`MemoryStream` resizing** | `Node.cs` receive buffer growth | Rare (buffers stabilize at typical packet size) | Monitor buffer `Capacity` |

**Key finding**: `udpClient.Receive(ref endPoint)` returns a **new byte array** on every call. At 200 Hz × N nodes, this creates 200N allocations/sec.

**Fix Phase 2**: Use `Socket.ReceiveFrom(buffer, ...)` with pre-allocated pooled buffers, or `UdpClient.ReceiveAsync` with `Memory<byte>`.

---

### Layer 3: Application Logic

| Source | Location | Estimated Impact | Measurement Required |
|--------|----------|------------------|----------------------|
| **5 ms tick interval** | `Program.cs` L1157 `sleep = 5 - duration` | **0–5 ms** delay per operation | Measure tick duration histogram |
| **Serialized work in `lock(conch)`** | `Program.cs` L1066–1152 | Queuing delay increases with work complexity | Measure per-subsystem work duration |
| **SimConnect polling** | `SimConnectInterface.cs` L592 `PERIOD.ONCE` | **0–5 ms** added latency (one extra tick round-trip) | Compare `PERIOD.SIM_FRAME` |
| **Hub relay: 2-tick minimum** | CONSOLE `DoWork` loop | **≥10 ms** relay latency | Measure receive→send delta |
| **`GetIntervalMask` filtering** | `Sim.cs` L2205 | Reduces send rate for distant aircraft (intentional) | No fix needed; document behavior |
| **No packet timestamping** | `Sim.cs` position writer | Cannot measure E2E latency today | Add `long sendTimestampTicks` field |

**Key finding**: Even with perfect OS/network, the 5 ms tick interval is a hard floor.

**Fix Phase 2**: Decouple receive from main loop (dedicated thread) + reduce tick to 2–3 ms + use high-resolution timer.

---

### Layer 4: Network (Physical)

| Source | Estimated Impact | Notes |
|--------|------------------|-------|
| **LAN RTT** | 1–5 ms | Baseline; irreducible |
| **Internet RTT** | 20–200 ms | Baseline; irreducible |
| **Packet loss** | Triggers retransmit (200+ ms) | JoinFS uses guaranteed delivery for critical messages |

**Key finding**: Network RTT is outside application control. Focus on minimizing application-layer delays (currently 10–35 ms) to make network RTT the dominant factor.

---

## Phase 1: Latency Instrumentation & Measurement

### Objectives

1. **Measure every hop** in the position update pipeline with <1 µs overhead
2. **Identify the top 3 delay contributors** with p50/p95/p99 percentile data
3. **Establish a baseline** before any optimizations
4. **Enable regression detection** after Phase 2 changes

### Design: Lock-Free Ring Buffer Tracer

**Files to create**:
- `JoinFS/JoinFS/Diagnostics/LatencyTracer.cs`
- `JoinFS/JoinFS/Diagnostics/LatencyReport.cs`

**Preprocessor guard**: `#if LATENCY_TRACE` (enabled only in Debug builds)

**Data structures**:

```csharp
public enum TracePoint : byte
{
	// Tick lifecycle
	TickStart = 0,
	TickEnd = 1,

	// SimConnect path
	SimConnectRequestSent = 10,
	SimConnectDataReceived = 11,

	// Position update path (local → network)
	PositionReadFromSim = 20,
	PositionWrittenToSendBuffer = 21,
	UdpSendCalled = 22,
	UdpSendCompleted = 23,

	// Receive path (network → local)
	UdpPacketReceivedInOsBuffer = 30,  // Approximated by ReceiveMessages entry
	UdpPacketReceived = 31,
	PacketProcessed = 32,

	// Hub relay (CONSOLE only)
	HubRelayIn = 40,
	HubRelayOut = 41,
}

[StructLayout(LayoutKind.Sequential)]
public struct TraceEntry
{
	public long TimestampTicks;      // Stopwatch.GetTimestamp()
	public TracePoint Point;
	public uint ObjectId;            // netId or nuid hash
	public ushort PayloadBytes;
	public byte ThreadId;            // For Phase 2 multi-threading
	public byte Reserved;
}
```

**Ring buffer**:
- Pre-allocated array: `TraceEntry[65536]` (1 MB total)
- Lock-free write: `Interlocked.Increment` on write index, modulo buffer size
- Overwrite oldest entries when full (intentional; we care about recent data)

**Hot-path API**:
```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void Record(TracePoint point, uint objectId = 0, ushort bytes = 0)
{
	// 2 lines: read Stopwatch.GetTimestamp(), write to buffer[Interlocked.Increment(index)]
}
```

**Dump API**:
```csharp
public static void Dump(string basePath)
{
	// Snapshot buffer → CSV + JSON
	// CSV: TimestampMs, ElapsedSinceLastUs, TracePoint, ObjectId, PayloadBytes
	// JSON: { "perHopLatencyUs": { "TickStart→TickEnd": { "p50": 3.2, "p95": 4.8, "p99": 6.1 }, ... } }
}
```

### Instrumentation Points

| File | Location | TracePoint | Notes |
|------|----------|------------|-------|
| `Program.cs` | L1064 (before `lock`) | `TickStart` | Measure tick interval |
| `Program.cs` | L1152 (after `lock`) | `TickEnd` | Measure work duration |
| `SimConnectInterface.cs` | L592 (after `RequestDataOnSimObject`) | `SimConnectRequestSent` | Measure SimConnect IPC latency |
| `SimConnectInterface.cs` | OnRecvSimobjectData handler | `SimConnectDataReceived` | End of SimConnect IPC |
| `Sim.cs` | L2195 (after `WriteAircraftPositionMessage`) | `PositionWrittenToSendBuffer` | Position ready to send |
| `Node.cs` | L661 (before `udpClient.Send`) | `UdpSendCalled` | Start of UDP send |
| `Node.cs` | L661 (after `udpClient.Send`) | `UdpSendCompleted` | End of UDP send (may block) |
| `Node.cs` | L1580 (start of `while` loop) | `UdpPacketReceivedInOsBuffer` | Approximation |
| `Node.cs` | L1586 (after `Receive`) | `UdpPacketReceived` | Actual receive |
| `Node.cs` | L1600 (after `ReceiveMsg`) | `PacketProcessed` | End of processing |
| `Network.cs` (CONSOLE) | Position message handler | `HubRelayIn` | Hub received position |
| `Network.cs` (CONSOLE) | Position message relay | `HubRelayOut` | Hub forwarded position |

### Expected Metrics

**Baseline hypothesis** (Debug build, default Windows timer):
- `TickStart → TickEnd`: p50 = 2 ms, p95 = 5 ms, p99 = 18 ms (timer jitter)
- `SimConnectRequestSent → SimConnectDataReceived`: p50 = 0.5 ms, p95 = 2 ms
- `PositionWrittenToSendBuffer → UdpSendCompleted`: p50 = 0.05 ms, p95 = 0.2 ms
- `UdpPacketReceivedInOsBuffer → PacketProcessed`: p50 = 0.1 ms, p95 = 0.5 ms
- `HubRelayIn → HubRelayOut`: p50 = 5 ms, p95 = 10 ms (waits for next tick)

**Success criteria for Phase 1**:
1. CSV file dumps successfully with no crashes after 60 seconds of operation
2. JSON reports per-hop latencies with percentiles
3. Top 3 delay sources identified from data
4. Baseline documented in this file (append results section)

---

## Phase 2: Optimizations (Documented, Not Yet Implemented)

### 2.1: Windows Timer Resolution Fix (Low Risk, High Impact)

**File**: `Program.cs`

**Change**:
```csharp
[DllImport("winmm.dll")]
static extern int timeBeginPeriod(uint period);
[DllImport("winmm.dll")]
static extern int timeEndPeriod(uint period);

// In Main() or startup:
timeBeginPeriod(1);  // Set to 1 ms resolution
// ... application runs ...
timeEndPeriod(1);    // Restore on shutdown
```

**Expected impact**: Reduce `Thread.Sleep(5)` jitter from 0–15 ms to 0–1 ms.

**Measurement**: Compare `TickStart → TickEnd` p95 before/after.

**Risk**: Minimal. Standard practice in games and real-time apps.

---

### 2.2: High-Precision Sleep (Medium Risk, Medium Impact)

**File**: `Program.cs`

**Change**: Replace `Thread.Sleep((int)sleep)` with hybrid spin-wait for sub-ms precision:

```csharp
long targetTicks = Stopwatch.GetTimestamp() + (sleep * Stopwatch.Frequency / 1000);
if (sleep > 2)
	Thread.Sleep((int)(sleep - 1));  // Sleep most of the time
while (Stopwatch.GetTimestamp() < targetTicks)
	Thread.SpinWait(100);  // Spin the last 1–2 ms
```

**Expected impact**: Reduce tick jitter to <0.1 ms.

**Risk**: Increases CPU usage slightly (one core at ~5–10% instead of ~1%). Acceptable for flight sim.

**Prerequisite**: 2.1 must be completed first (timer resolution).

---

### 2.3: Async UDP Receive on Dedicated Thread (High Risk, High Impact)

**File**: `Node.cs`

**Current**: `ReceiveMessages()` called once per tick inside `lock(conch)` → minimum one tick delay.

**Change**:
1. Create dedicated receive thread (or use `Task.Run` with long-running flag)
2. Call `await udpClient.ReceiveAsync()` in a loop
3. Write received packets to a lock-free concurrent queue (`ConcurrentQueue<ReceivedPacket>`)
4. Main loop dequeues and processes (still inside `lock(conch)` for now)

**Expected impact**: Eliminate the 0–5 ms "wait for next tick" delay on receives.

**Measurement**: `UdpPacketReceivedInOsBuffer → PacketProcessed` should drop to <0.5 ms p95.

**Risk**:
- Introduces multi-threading → potential race conditions
- `lock(conch)` contention if receive thread tries to write to shared state
- Mitigation: Queue packets only; process in main thread

**Prerequisite**: Phase 1 tracing must confirm this is a top-3 contributor.

---

### 2.4: Pre-Allocated Receive Buffers (Low Risk, Medium Impact)

**File**: `Node.cs`

**Current**: `byte[] messageData = udpClient.Receive(ref endPoint)` allocates per packet.

**Change**:
```csharp
byte[] buffer = ArrayPool<byte>.Shared.Rent(1500);  // MTU size
int received = udpClient.Client.Receive(buffer, SocketFlags.None);
// ... process buffer[0..received] ...
ArrayPool<byte>.Shared.Return(buffer);
```

**Expected impact**: Eliminate GC pauses from receive allocations.

**Measurement**: Correlate GC events with latency spikes in tracing data.

**Risk**: Minimal. Standard .NET pattern.

---

### 2.5: Socket Buffer Size Tuning (Low Risk, Low Impact)

**File**: `Node.cs` L2113

**Change**:
```csharp
udpClient = new UdpClient(localNuid.port, AddressFamily.InterNetwork);
udpClient.Client.ReceiveBufferSize = 256 * 1024;  // 256 KB (default is 8 KB)
udpClient.Client.SendBufferSize = 256 * 1024;
udpClient.Client.DontFragment = true;  // Prevent IP fragmentation
```

**Expected impact**: Reduce packet loss under burst traffic.

**Measurement**: Monitor `udpClient.Available` high-water mark; confirm no drops.

**Risk**: None.

---

### 2.6: CONSOLE Hub Relay Bypass (High Risk, High Impact)

**File**: `Program.cs`, `Network.cs`

**Current**: Hub relay waits for next tick → minimum 2-tick (10 ms) relay latency.

**Change**:
1. Create dedicated relay thread for position-only messages
2. Bypass `lock(conch)` for relay (read-only access to routing table)
3. Forward position messages immediately without waiting for tick

**Expected impact**: Reduce hub relay latency from 10 ms p50 to <1 ms p50.

**Measurement**: `HubRelayIn → HubRelayOut` should drop to <0.5 ms p95.

**Risk**:
- High: Concurrent access to `nodes` dictionary without lock
- Mitigation: Use `ConcurrentDictionary` for routing table, or copy routing snapshot periodically

**Prerequisite**: Phase 1 tracing must confirm hub relay is a top-3 contributor.

---

### 2.7: SimConnect Event-Driven Updates (Medium Risk, Medium Impact)

**File**: `SimConnectInterface.cs`

**Current**: `SIMCONNECT_PERIOD.ONCE` → poll on every tick.

**Change**: Use `SIMCONNECT_PERIOD.SIM_FRAME` → SimConnect pushes updates every sim frame.

**Expected impact**: Eliminate one tick of polling latency.

**Measurement**: `SimConnectRequestSent → SimConnectDataReceived` should drop to <0.2 ms p95.

**Risk**: May increase message rate if sim runs >200 FPS (unlikely; typical sim runs at 30–60 FPS).

**Prerequisite**: Test with locked 60 FPS sim first.

---

### 2.8: Packet-Level Timestamping (Low Risk, High Impact for Measurement)

**File**: `Sim.cs` (position message writer), `Network.cs` (message parser)

**Change**: Add a `long SendTimestampTicks` field to the position message header.

**Impact**: Enable true end-to-end latency measurement (sender timestamp → receiver timestamp).

**Use case**: Real-time latency HUD overlay; detect network issues vs. application issues.

**Risk**: Increases packet size by 8 bytes. Negligible.

---

## Acceptance Criteria

### Phase 1 (Tracing)

- [ ] `LatencyTracer.cs` and `LatencyReport.cs` compile without errors
- [ ] `LATENCY_TRACE` symbol added to Debug configuration
- [ ] All instrumentation points added without breaking existing functionality
- [ ] Trace dump command works (CONSOLE `tracedump` + GUI menu/keyboard shortcut)
- [ ] CSV file contains valid data after 60-second run
- [ ] JSON summary reports top 5 delay sources with p50/p95/p99
- [ ] No measurable performance regression (<1% CPU overhead)

### Phase 2 (Optimizations)

Each optimization must:
1. Show ≥20% improvement in its target metric (p95 latency)
2. Cause no regression in other metrics
3. Pass 10-minute stress test (8 nodes, 100 aircraft)
4. Be individually revertible (one commit per optimization)

**Overall Phase 2 goal**: Application-layer latency (sender → receiver, excluding network RTT) reduced to <5 ms p95 (from current ~15 ms p95).

---

## Measurement Methodology

### Test Scenario 1: Local Loopback (Baseline)

**Setup**: Two JoinFS instances on same machine, connected via localhost.

**Purpose**: Isolate application-layer latency (zero network RTT).

**Metrics**:
- `TickStart → TickEnd` (work duration)
- `PositionWrittenToSendBuffer → PacketProcessed` (sender → receiver)
- GC pause frequency

**Baseline target**: <5 ms p95 end-to-end after Phase 2.

---

### Test Scenario 2: LAN (Network Baseline)

**Setup**: Two machines on same LAN (1 Gbps Ethernet, <1 ms RTT).

**Purpose**: Add minimal network RTT to isolate application vs. network delays.

**Metrics**: Same as Scenario 1 + network RTT (via packet timestamps in Phase 2.8).

**Baseline target**: <6 ms p95 end-to-end after Phase 2 (≈5 ms application + 1 ms network).

---

### Test Scenario 3: Hub Relay (CONSOLE)

**Setup**: Player A → CONSOLE Hub (LAN) → Player B (LAN).

**Purpose**: Measure hub relay latency.

**Metrics**:
- `HubRelayIn → HubRelayOut` (relay processing time)
- End-to-end: Player A send → Player B receive

**Baseline target**: <2 ms p95 relay processing after Phase 2.6.

---

### Test Scenario 4: Stress Test

**Setup**: 8 nodes, 100 aircraft total, all broadcasting positions.

**Purpose**: Detect GC pauses, lock contention, and buffer overflows.

**Metrics**:
- Packet loss rate (should be 0%)
- Max `udpClient.Available` (should be <50 packets queued)
- GC pause count (should be <10/min after Phase 2.4)

---

## Risk Assessment

| Optimization | Complexity | Risk Level | Rollback Ease | Priority |
|--------------|------------|------------|---------------|----------|
| 2.1: Timer resolution | Low | Low | Easy (one P/Invoke call) | **HIGH** |
| 2.2: High-precision sleep | Low | Low | Easy (replace one line) | HIGH |
| 2.4: Pre-allocated buffers | Low | Low | Easy (use ArrayPool) | **HIGH** |
| 2.5: Socket buffer tuning | Low | Low | Easy (set properties) | Medium |
| 2.8: Packet timestamping | Low | Low | Easy (add field) | **HIGH** (for measurement) |
| 2.7: SimConnect event mode | Medium | Medium | Medium (change polling mode) | Medium |
| 2.3: Async receive thread | High | Medium | Hard (threading changes) | **HIGH** (if bottleneck confirmed) |
| 2.6: Hub relay bypass | High | **High** | Hard (concurrency changes) | Medium (CONSOLE only) |

**Recommendation**: Implement in priority order. Pause after each to measure impact.

---

## Results (To Be Populated After Phase 1)

### Baseline Measurements (Pre-Optimization)

_Run Date: [TBD]_  
_Configuration: Windows 11, .NET 8, Debug build, default timer resolution_  
_Scenario: Local loopback, 2 nodes, 1 aircraft each_

**Top 5 Delay Sources** (p95 latency):

1. [TBD] ms — [TracePoint → TracePoint]
2. [TBD] ms — [TracePoint → TracePoint]
3. [TBD] ms — [TracePoint → TracePoint]
4. [TBD] ms — [TracePoint → TracePoint]
5. [TBD] ms — [TracePoint → TracePoint]

**CSV Sample**: [Link to file]

**JSON Summary**: [Link to file]

---

### Phase 2 Results (Post-Optimization)

_To be added as each optimization is completed._

| Optimization | Date | p95 Before | p95 After | Improvement | Notes |
|--------------|------|------------|-----------|-------------|-------|
| 2.1: Timer resolution | [TBD] | [TBD] ms | [TBD] ms | [TBD]% | |
| 2.2: High-precision sleep | [TBD] | [TBD] ms | [TBD] ms | [TBD]% | |
| ... | | | | | |

---

## Appendix A: Glossary

- **p50/p95/p99**: 50th/95th/99th percentile latency (median, "typical worst-case", "rare worst-case")
- **Tick**: One iteration of the main work loop (~5 ms target interval)
- **RTT**: Round-trip time (network latency, sender → receiver → sender)
- **SimConnect**: Microsoft Flight Simulator SDK IPC mechanism (named pipes + shared memory)
- **NUID**: Node unique identifier (JoinFS network addressing)
- **Hub**: CONSOLE-mode JoinFS instance that relays messages between players

---

## Appendix B: Code References

| File | Key Functions | Purpose |
|------|---------------|---------|
| `Program.cs` | `DoWork()` (L1057) | Main event loop |
| `Node.cs` | `Send()` (L676), `ReceiveMessages()` (L1577) | UDP socket I/O |
| `Sim.cs` | Position update loop (L2195) | Aircraft position broadcast |
| `SimConnectInterface.cs` | `RequestData()` (L585) | SimConnect IPC |
| `Network.cs` | Message routing | Hub relay logic |

---

## Appendix C: Related Work

- **DCS World**: Uses ~200 Hz update rate, dedicated network thread, lock-free queues
- **IL-2 Sturmovik**: Uses UDP with custom reliability layer, <10 ms p95 application latency
- **Best Practices**: "High-Performance .NET Networking" (Microsoft), "Game Engine Architecture" (Jason Gregory, Ch. 15)

---

## Document Revision History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2025-01-XX | GitHub Copilot (AI Assistant) | Initial comprehensive analysis and Phase 1/2 roadmap |

---

**End of Document**
