using JoinFS.Net;
using JoinFS.Net.Jfp2.Codecs;
using JoinFS.Recording;
using JoinFS.Recording.Format;

namespace JoinFS.Tests.Recording;

/// <summary>
/// One time base per recorded object (docs/recording/jfs2/README.md, limit 6): variables and events go with the positions, so the capture
/// never holds a record back.
/// </summary>
public sealed class TrackTimeBaseTests : IDisposable
{
    readonly string folder = Directory.CreateTempSubdirectory("jfs2-timebase-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(folder, true); } catch (IOException) { }
    }

    [Fact]
    public void BeforeTheFirstPosition_RecordsTakeTheRecordersTime()
    {
        var timeBase = new TrackTimeBase();
        Assert.Equal(3.0, timeBase.Other(3.0));
        Assert.Equal(3.5, timeBase.Other(3.5));
    }

    [Fact]
    public void TheFirstPosition_TakesTheRecordersTime_AndLaterOnesKeepTheirSampleSpacing()
    {
        var timeBase = new TrackTimeBase();
        Assert.Equal(10.0, timeBase.Position(10.0, 5000.0));
        // arrives 70 ms late: still 50 ms after the first
        Assert.Equal(10.05, timeBase.Position(10.12, 5000.05), 9);
        Assert.Equal(10.10, timeBase.Position(10.13, 5000.10), 9);
    }

    [Fact]
    public void Records_GoWithTheLatestPosition()
    {
        var timeBase = new TrackTimeBase();
        timeBase.Position(10.0, 5000.0);
        timeBase.Position(10.12, 5000.05);
        // arrives after the late position, before the next one
        Assert.Equal(10.05, timeBase.Other(10.13), 9);
        Assert.Equal(10.05, timeBase.Other(10.12 + TrackTimeBase.OverdueSeconds), 9);
    }

    [Fact]
    public void WhenThePositionsStop_RecordsMoveOnBehindTheRecordersTime()
    {
        var timeBase = new TrackTimeBase();
        timeBase.Position(10.0, 5000.0);
        Assert.Equal(11.5, timeBase.Other(12.0), 9);
        Assert.Equal(12.5, timeBase.Other(13.0), 9);
    }

    /// <summary>
    /// A network aircraft whose positions arrive with jitter and whose variables and events arrive between them: on one time base
    /// nothing is held, and the variables sit at the position they arrived after. On the recorder's time (before this slice) the
    /// positions after a late one were held.
    /// </summary>
    [Fact]
    public void AJitteryNetworkStream_IsWrittenWithNothingHeld()
    {
        Assert.Equal(0, Capture("on-base.jfs2", onTimeBase: true));
        Assert.True(Capture("arrival.jfs2", onTimeBase: false) > 0);

        using var reader = RecordingReader.Open(Path.Combine(folder, "on-base.jfs2"));
        uint position = reader.FindKind(Jfs2Kinds.Position, 1)!.Value;
        uint variables = reader.FindKind(Jfs2Kinds.Variables, 1)!.Value;
        uint simEvent = reader.FindKind(Jfs2Kinds.Event, 1)!.Value;
        long lastPosition = -1;
        int checkedRecords = 0;
        for (int i = 0; i < reader.Chunks.Count; i++)
        {
            Assert.True(reader.TryReadChunk(i, out RecordChunk? chunk));
            foreach (RecordEntry record in chunk!.Records.Where(r => !r.IsSnapshot))
            {
                if (record.Kind == position)
                {
                    lastPosition = record.Time;
                }
                else if (record.Kind == variables || record.Kind == simEvent)
                {
                    Assert.Equal(lastPosition, record.Time);
                    checkedRecords++;
                }
            }
        }
        Assert.Equal(200 + 20, checkedRecords);
    }

    int Capture(string name, bool onTimeBase)
    {
        using var capture = Jfs2Capture.Create(Path.Combine(folder, name), "JoinFS test", "", new DateTime(2026, 10, 15, 18, 0, 0, DateTimeKind.Utc), _ => { });
        var timeBase = new TrackTimeBase();
        capture.OpenTrack(1, 0.0, TrackOwner.Network, "pilot", "JoinFS test", "", new IdentityUpdate { IsAircraft = true, Callsign = "DLH1", Model = "M" });
        for (int i = 0; i < 200; i++)
        {
            double sample = 7000.0 + i * 0.05;
            // the sender's clock runs 7000 s ahead; latency 100 ms plus up to 80 ms of jitter, more than the spacing of positions
            double arrival = 0.1 + i * 0.05 + (i * 7 % 9) * 0.01;
            PositionUpdate position = new() { Latitude = 47, Longitude = 8, StaticCgToGround = float.NaN };
            capture.Position(1, timeBase.Position(arrival, sample), ref position);
            double now = arrival + 0.001;
            double time = onTimeBase ? timeBase.Other(now) : now;
            capture.Variables(1, time, new Dictionary<uint, int> { [42] = i }, null, null);
            if (i % 10 == 0)
            {
                capture.Event(1, time, 7, (uint)i);
            }
        }
        capture.Finish();
        return capture.HeldCount;
    }
}
