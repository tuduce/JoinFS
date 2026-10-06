namespace JoinFS.Tests;

/// <summary>
/// The websocket feed used to publish an aircraft on every work-loop pass (~200/s) because it compared raw
/// doubles while the wire carries rounded values, and never re-sent an unchanged aircraft (so a map purging
/// stale markers dropped parked aircraft). The filter publishes on a change of what is actually on the wire,
/// at most once per flush interval for the whole feed (one batched message per tick), and keeps every aircraft
/// alive with a periodic re-send.
/// </summary>
public class AircraftFeedFilterTests
{
    const double FlushInterval = 0.5;
    const double KeepAlive = 15.0;

    double now;
    readonly AircraftFeedFilter filter;
    readonly Guid key = Guid.NewGuid();

    public AircraftFeedFilterTests()
    {
        filter = new AircraftFeedFilter(() => now, FlushInterval, KeepAlive);
    }

    static AircraftSnapshot Parked() => new()
    {
        guid = "g", callsign = "D-EULE", nickname = "Pilot", trafficType = "pilot", icaoType = "MD11F",
        latitude = 33.823917, longitude = -116.507003, altitude = 431.0, speed = 0.0, heading = 169,
        squawk = "2102", com1 = "124.850", onGround = true,
    };

    [Fact]
    public void FirstSightingIsSent()
    {
        Assert.True(filter.ShouldSend(key, Parked()));
    }

    [Fact]
    public void UnchangedAircraftIsSilentUntilKeepAlive_ThenSentOnce()
    {
        filter.ShouldSend(key, Parked());

        now = KeepAlive - 0.1;
        Assert.False(filter.ShouldSend(key, Parked()));

        now = KeepAlive;
        Assert.True(filter.ShouldSend(key, Parked()));
        Assert.False(filter.ShouldSend(key, Parked()));   // timer restarts after the keep-alive
    }

    [Fact]
    public void JitterBelowWireRoundingIsNotAChange()
    {
        filter.ShouldSend(key, Parked());
        var jitter = Parked();
        jitter.latitude += 1e-9;
        jitter.longitude -= 1e-9;
        jitter.altitude += 0.2;       // wire carries whole feet
        jitter.speed += 0.01;         // wire carries 0.1 kt

        now = FlushInterval + 1;
        Assert.False(filter.ShouldSend(key, jitter));
    }

    [Fact]
    public void AnyVisibleChangeIsDueOnTheNextEvaluation_ExactlyOnce()
    {
        filter.ShouldSend(key, Parked());
        var moved = Parked();
        moved.latitude += 0.001;

        now = FlushInterval;
        Assert.True(filter.ShouldSend(key, moved));
        Assert.False(filter.ShouldSend(key, moved));      // the same state is not sent twice
    }

    [Fact]
    public void ChangeOfASlowFieldIsDueToo()
    {
        filter.ShouldSend(key, Parked());
        var retuned = Parked();
        retuned.squawk = "7000";

        now = FlushInterval;
        Assert.True(filter.ShouldSend(key, retuned));
    }

    [Fact]
    public void FlushIsDueAtMostOncePerInterval()
    {
        Assert.True(filter.FlushDue());                    // the very first pass
        now = FlushInterval - 0.01;
        Assert.False(filter.FlushDue());
        now = FlushInterval;
        Assert.True(filter.FlushDue());
        now = FlushInterval + 0.1;
        Assert.False(filter.FlushDue());                   // the interval restarts at the last flush
    }

    [Fact]
    public void MovingAircraftProduceAtMostOneMessagePerInterval_EvenWhenOutOfPhase()
    {
        // 5 aircraft that change at different moments: with per-aircraft timers they would drift out of
        // phase and trigger a message nearly every pass. The feed must flush as one batch per tick.
        var keys = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        var snaps = keys.Select(_ => Parked()).ToArray();
        int messages = 0, aircraftSent = 0;

        for (now = 0.0; now < 5.0; now += 0.005)           // 5 s of 5 ms work-loop passes
        {
            for (int i = 0; i < keys.Length; i++)
                if ((int)(now / 0.005) % (i + 2) == 0) snaps[i].latitude += 1e-5;   // staggered movement

            if (!filter.FlushDue()) continue;
            int due = 0;
            for (int i = 0; i < keys.Length; i++)
                if (filter.ShouldSend(keys[i], snaps[i])) due++;
            if (due > 0) { messages++; aircraftSent += due; }
        }

        Assert.InRange(messages, 9, 10);                   // 2 Hz for 5 s
        Assert.InRange(aircraftSent, 9 * 5, 10 * 5);       // every moving aircraft rides in every message
    }

    [Fact]
    public void RoundedSlowFieldsDoNotTrigger()
    {
        var first = Parked();
        first.flaps = 0.5;
        first.rotorRpm = 100.0;
        filter.ShouldSend(key, first);

        var noise = first;
        noise.flaps += 0.0001;     // wire carries 3 decimals
        noise.rotorRpm += 0.01;    // wire carries 1 decimal
        now = FlushInterval;
        Assert.False(filter.ShouldSend(key, noise));
    }

    [Fact]
    public void RetainDropsStateOfAircraftThatDisappeared_SoTheyReappearAsNew()
    {
        filter.ShouldSend(key, Parked());

        filter.Retain([]);

        now = FlushInterval;
        Assert.True(filter.ShouldSend(key, Parked()));
    }

    [Fact]
    public void RetainKeepsStateOfAircraftStillSeen()
    {
        filter.ShouldSend(key, Parked());

        filter.Retain([key]);

        now = FlushInterval;
        Assert.False(filter.ShouldSend(key, Parked()));
    }

    [Fact]
    public void KnownReturnsTheLatestSnapshotOfEveryAircraft_EvenWhenNotDueToBeSent()
    {
        var other = Guid.NewGuid();
        filter.ShouldSend(key, Parked());
        var moved = Parked();
        moved.latitude += 0.001;
        filter.ShouldSend(key, moved);                     // held back, but it is the latest state
        var second = Parked();
        second.callsign = "N123";
        filter.ShouldSend(other, second);

        var known = filter.Known().ToList();

        Assert.Equal(2, known.Count);
        Assert.Contains(known, s => s.callsign == "D-EULE" && s.latitude == moved.latitude);
        Assert.Contains(known, s => s.callsign == "N123");
    }

    [Fact]
    public void Contains_IsTrueOnlyForAircraftTheFilterTracks()
    {
        Assert.False(filter.Contains(key));

        filter.ShouldSend(key, Parked());
        Assert.True(filter.Contains(key));

        filter.Retain([]);
        Assert.False(filter.Contains(key));
    }

    [Fact]
    public void Rounded_MatchesTheWirePrecision()
    {
        var s = Parked();
        s.latitude = 33.8239174999;
        s.longitude = -116.5070031;
        s.altitude = 431.4;
        s.speed = 12.34;
        s.flaps = 0.12345;
        s.rotorRpm = 99.96;

        var r = s.Rounded();

        Assert.Equal(33.823917, r.latitude);
        Assert.Equal(-116.507003, r.longitude);
        Assert.Equal(431.0, r.altitude);
        Assert.Equal(12.3, r.speed);
        Assert.Equal(0.123, r.flaps);
        Assert.Equal(100.0, r.rotorRpm);
    }
}
