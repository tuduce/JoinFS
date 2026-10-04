using System.Globalization;
using System.Text;

namespace RecordingXRay.Services;

/// <summary>The plain-text dump of an aircraft, object or frame: the inspector's Raw view and the Copy button.</summary>
public static class FrameFormatter
{
    public static string FormatAircraft(RecordedAircraft aircraft)
    {
        StringBuilder sb = new();
        sb.AppendLine("Type: Aircraft");
        sb.AppendLine(Line("Callsign", aircraft.Callsign));
        sb.AppendLine(Line("Nickname", aircraft.Nickname));
        sb.AppendLine(Line("Plane", aircraft.Plane));
        sb.AppendLine(Line("Model", aircraft.Model));
        sb.AppendLine(Line("TypeRole", $"{aircraft.TypeRole} ({TypeRoleToText(aircraft.TypeRole)})"));
        sb.AppendLine(Line("Frames", aircraft.Frames.Count));
        AppendIfSet(sb, "Livery", aircraft.Livery);
        AppendIfSet(sb, "IcaoType", aircraft.IcaoType);
        AppendIfSet(sb, "IcaoAirline", aircraft.IcaoAirline);
        return sb.ToString();
    }

    public static string FormatObject(RecordedObject obj)
    {
        StringBuilder sb = new();
        sb.AppendLine("Type: Object");
        sb.AppendLine(Line("Model", obj.Model));
        sb.AppendLine(Line("TypeRole", obj.TypeRole));
        sb.AppendLine(Line("Frames", obj.Frames.Count));
        AppendIfSet(sb, "Livery", obj.Livery);
        AppendIfSet(sb, "IcaoType", obj.IcaoType);
        AppendIfSet(sb, "IcaoAirline", obj.IcaoAirline);
        return sb.ToString();
    }

    /// <param name="resolveName">Maps a variable id to its name (<c>"unknown"</c> when not known).</param>
    public static string FormatFrame(RecordedFrame frame, Func<uint, string> resolveName)
    {
        StringBuilder sb = new();
        sb.AppendLine(Line("Type", frame.Type));
        sb.AppendLine(Line("Time", frame.Time.ToString("0.000", CultureInfo.InvariantCulture)));

        switch (frame)
        {
            case ObjectPositionFrame op:
                AppendPosition(sb, op.Latitude, op.Longitude, op.Altitude, op.Pitch, op.Bank, op.Heading,
                    op.VelocityX, op.VelocityY, op.VelocityZ,
                    op.AngularVelocityX, op.AngularVelocityY, op.AngularVelocityZ,
                    op.AccelerationX, op.AccelerationY, op.AccelerationZ);
                sb.AppendLine(Line("Height", op.Height));
                sb.AppendLine(Line("Ground", op.Ground));
                sb.AppendLine(Line("ElevationCorrection", op.ElevationCorrection));
                break;
            case AircraftPositionFrame ap:
                AppendPosition(sb, ap.Latitude, ap.Longitude, ap.Altitude, ap.Pitch, ap.Bank, ap.Heading,
                    ap.VelocityX, ap.VelocityY, ap.VelocityZ,
                    ap.AngularVelocityX, ap.AngularVelocityY, ap.AngularVelocityZ,
                    ap.AccelerationX, ap.AccelerationY, ap.AccelerationZ);
                sb.AppendLine(Line("RudderRaw", ap.RudderRaw));
                sb.AppendLine(Line("ElevatorRaw", ap.ElevatorRaw));
                sb.AppendLine(Line("AileronRaw", ap.AileronRaw));
                sb.AppendLine(Line("BrakeLeftRaw", ap.BrakeLeftRaw));
                sb.AppendLine(Line("BrakeRightRaw", ap.BrakeRightRaw));
                sb.AppendLine(Line("Elevation", ap.Elevation));
                sb.AppendLine(Line("Ground", ap.Ground));
                sb.AppendLine(Line("ElevationCorrection", ap.ElevationCorrection));
                break;
            case SimEventFrame simEvent:
                sb.AppendLine(Line("EventId", simEvent.EventId));
                sb.AppendLine(Line("Data", simEvent.Data));
                break;
            case IntegerVariablesFrame intVars:
                AppendVariables(sb, intVars.Variables.Select(v => (v.Key, Invariant(v.Value))), resolveName);
                break;
            case FloatVariablesFrame floatVars:
                AppendVariables(sb, floatVars.Variables.Select(v => (v.Key, NumberFormat.Raw(v.Value))), resolveName);
                break;
            case String8VariablesFrame stringVars:
                AppendVariables(sb, stringVars.Variables.Select(v => (v.Key, v.Value)), resolveName);
                break;
        }

        return sb.ToString();
    }

    public static string TypeRoleToText(int typeRole) => typeRole switch
    {
        1 => "SingleProp",
        2 => "TwinProp",
        3 => "Airliner",
        4 => "Rotorcraft",
        5 => "Glider",
        6 => "Fighter",
        7 => "Bomber",
        8 => "FourProp",
        _ => "Unknown",
    };

    private static void AppendPosition(
        StringBuilder sb,
        double latitude, double longitude, double altitude,
        float pitch, float bank, float heading,
        float velocityX, float velocityY, float velocityZ,
        float angularX, float angularY, float angularZ,
        float accelerationX, float accelerationY, float accelerationZ)
    {
        sb.AppendLine(Line("Latitude", latitude));
        sb.AppendLine(Line("Longitude", longitude));
        sb.AppendLine(Line("Altitude", altitude));
        sb.AppendLine(Line("Pitch", pitch));
        sb.AppendLine(Line("Bank", bank));
        sb.AppendLine(Line("Heading", heading));
        sb.AppendLine(Line("VelocityX", velocityX));
        sb.AppendLine(Line("VelocityY", velocityY));
        sb.AppendLine(Line("VelocityZ", velocityZ));
        sb.AppendLine(Line("AngularVelocityX", angularX));
        sb.AppendLine(Line("AngularVelocityY", angularY));
        sb.AppendLine(Line("AngularVelocityZ", angularZ));
        sb.AppendLine(Line("AccelerationX", accelerationX));
        sb.AppendLine(Line("AccelerationY", accelerationY));
        sb.AppendLine(Line("AccelerationZ", accelerationZ));
    }

    private static void AppendVariables(StringBuilder sb, IEnumerable<(uint Id, string Value)> variables, Func<uint, string> resolveName)
    {
        sb.AppendLine("Variables:");
        bool any = false;
        foreach ((uint id, string value) in variables)
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {id} ({resolveName(id)}) = {value}"));
            any = true;
        }

        if (!any)
        {
            sb.AppendLine("  (none)");
        }
    }

    private static void AppendIfSet(StringBuilder sb, string key, string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            sb.AppendLine(Line(key, value));
        }
    }

    private static string Line(string key, object value) =>
        string.Create(CultureInfo.InvariantCulture, $"{key}: {value}");

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);
}
