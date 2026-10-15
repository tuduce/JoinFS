using System.Text;
using JoinFS.Recording;

namespace JoinFS.Tests.Recording;

/// <summary>
/// Writes recorded tracks in the old <c>.jfs</c> format, the way an FS2024 build did (livery, ICAO type and airline after the frames of
/// each object). The application no longer writes this format; the importer tests need files to read, and this is the one place that
/// still knows how they were made. It is a test-side copy of the writer that used to be in Recorder.cs, which is why it calls
/// <see cref="JfsFrames"/> for the frame bodies as that did. A variables frame becomes one old frame per value kind, as the old recorder kept them.
/// </summary>
internal static class LegacyJfsWriter
{
    /// <summary>The newest version of the old format.</summary>
    public const short Version = 21008;

    /// <summary>The old format's frame types (the old <c>Recorder.FrameType</c>) that the writer used.</summary>
    const byte ObjectPosition = 0, AircraftPosition = 1, SimEvent = 10, IntegerVariables = 11, FloatVariables = 12, String8Variables = 13;

    public static byte[] Write(List<SceneTrack> tracks, short version = Version)
    {
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(version);
            writer.Write(tracks.Count(t => t.Identity.IsAircraft));
            foreach (SceneTrack track in tracks.Where(t => t.Identity.IsAircraft))
            {
                writer.Write(track.Identity.IsPlane);
                writer.Write(track.Identity.Callsign ?? "");
                writer.Write(track.Nickname ?? "");
                WriteObject(writer, track);
            }
            writer.Write(tracks.Count(t => !t.Identity.IsAircraft));
            foreach (SceneTrack track in tracks.Where(t => !t.Identity.IsAircraft))
            {
                WriteObject(writer, track);
            }
        }
        return stream.ToArray();
    }

    static void WriteObject(BinaryWriter writer, SceneTrack track)
    {
        List<(byte Type, double Time, Action<BinaryWriter> Body)> frames = [];
        foreach (SceneFrame frame in track.Frames)
        {
            switch (frame)
            {
                case SceneObjectPositionFrame position:
                {
                    Sim.ObjectPositionVelocity data = SimMessageMapper.ToPositionVelocity(position.Position);
                    frames.Add((ObjectPosition, frame.Time, w => WriteObjectPosition(w, data)));
                    break;
                }
                case ScenePositionFrame position:
                {
                    Sim.AircraftPosition data = SimMessageMapper.ToAircraftPosition(position.Position);
                    frames.Add((AircraftPosition, frame.Time, w => JfsFrames.Write(w, ref data)));
                    break;
                }
                case SceneEventFrame sim:
                    frames.Add((SimEvent, frame.Time, w => { w.Write(sim.EventId); w.Write(sim.Data); }));
                    break;
                case SceneVariablesFrame variables:
                {
                    var (integers, floats, strings) = variables.ByKind();
                    if (integers != null) frames.Add((IntegerVariables, frame.Time, w => WriteVariables(w, integers, (x, value) => x.Write(value))));
                    if (floats != null) frames.Add((FloatVariables, frame.Time, w => WriteVariables(w, floats, (x, value) => x.Write(value))));
                    if (strings != null) frames.Add((String8Variables, frame.Time, w => WriteVariables(w, strings, (x, value) => x.Write(value))));
                    break;
                }
            }
        }

        writer.Write(track.Identity.Model ?? "");
        writer.Write(track.Identity.TypeRole);
        writer.Write(frames.Count);
        foreach (var (type, time, body) in frames)
        {
            writer.Write(type);
            writer.Write(time);
            body(writer);
        }
        writer.Write(track.Identity.Livery ?? "");
        writer.Write(track.Identity.IcaoType ?? "");
        writer.Write(track.Identity.IcaoAirline ?? "");
    }

    static void WriteObjectPosition(BinaryWriter writer, Sim.ObjectPositionVelocity p)
    {
        writer.Write(p.latitude);
        writer.Write(p.longitude);
        writer.Write(p.altitude);
        writer.Write(p.pitch);
        writer.Write(p.bank);
        writer.Write(p.heading);
        writer.Write(p.velocityX);
        writer.Write(p.velocityY);
        writer.Write(p.velocityZ);
        writer.Write(p.angularVelocityX);
        writer.Write(p.angularVelocityY);
        writer.Write(p.angularVelocityZ);
        writer.Write(p.accelerationX);
        writer.Write(p.accelerationY);
        writer.Write(p.accelerationZ);
        writer.Write(p.height);
        writer.Write((byte)(p.ground != 0 ? 0x01 : 0)); // the elevation-correction bit (0x02) was a setting of the writer's machine
    }

    static void WriteVariables<T>(BinaryWriter writer, Dictionary<uint, T> variables, Action<BinaryWriter, T> writeValue)
    {
        writer.Write((ushort)variables.Count);
        foreach (var variable in variables)
        {
            writer.Write(variable.Key);
            writeValue(writer, variable.Value);
        }
    }
}
