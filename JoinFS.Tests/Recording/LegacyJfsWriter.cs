using System.Text;

namespace JoinFS.Tests.Recording;

/// <summary>
/// Writes recorded objects in the old <c>.jfs</c> format, the way an FS2024 build did (livery, ICAO type and airline after the frames of
/// each object). The application no longer writes this format; the importer tests need files to read, and this is the one place that
/// still knows how they were made. It is a test-side copy of the writer that used to be in Recorder.cs, which is why it calls
/// <see cref="JfsFrames"/> for the frame bodies as that did.
/// </summary>
internal static class LegacyJfsWriter
{
    /// <summary>The newest version of the old format.</summary>
    public const short Version = 21008;

    public static byte[] Write(List<Recorder.Obj> objects, short version = Version)
    {
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(version);
            writer.Write(objects.Count(o => o is Recorder.Aircraft));
            foreach (Recorder.Obj obj in objects.Where(o => o is Recorder.Aircraft))
            {
                var aircraft = (Recorder.Aircraft)obj;
                writer.Write(aircraft.plane);
                writer.Write(aircraft.callsign);
                writer.Write(aircraft.nickname);
                WriteObject(writer, obj);
            }
            writer.Write(objects.Count(o => o is not Recorder.Aircraft));
            foreach (Recorder.Obj obj in objects.Where(o => o is not Recorder.Aircraft))
            {
                WriteObject(writer, obj);
            }
        }
        return stream.ToArray();
    }

    static void WriteObject(BinaryWriter writer, Recorder.Obj obj)
    {
        writer.Write(obj.model);
        writer.Write((byte)obj.typerole);
        writer.Write(obj.frames.Count);
        foreach (Recorder.Frame frame in obj.frames)
        {
            writer.Write((byte)frame.type);
            writer.Write(frame.time);
            switch (frame)
            {
                case Recorder.ObjectPositionFrame position:
                    WriteObjectPosition(writer, position.data);
                    break;
                case Recorder.AircraftPositionFrame position:
                    JfsFrames.Write(writer, ref position.data);
                    break;
                case Recorder.SimEventFrame sim:
                    writer.Write(sim.eventId);
                    writer.Write(sim.data);
                    break;
                case Recorder.IntegerVariablesFrame integers:
                    WriteVariables(writer, integers.variables, (w, value) => w.Write(value));
                    break;
                case Recorder.FloatVariablesFrame floats:
                    WriteVariables(writer, floats.variables, (w, value) => w.Write(value));
                    break;
                case Recorder.String8VariablesFrame strings:
                    WriteVariables(writer, strings.variables, (w, value) => w.Write(value));
                    break;
            }
        }
        writer.Write(obj.livery);
        writer.Write(obj.icaoType);
        writer.Write(obj.icaoAirline);
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
