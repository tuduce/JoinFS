using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RecordingXRay.Services;

namespace RecordingXRay.ViewModels;

/// <param name="Degrees">The value converted to degrees, for angular fields only (empty otherwise).</param>
public sealed record FieldRow(string Key, string Degrees, string Value);

public sealed record FieldCard(string Title, IReadOnlyList<FieldRow> Rows);

/// <param name="Fraction">-1..1 for centred controls (rudder, elevator, aileron), 0..1 for brakes.</param>
public sealed record ControlRow(string Key, string RawText, double Fraction, bool IsCentered);

public sealed record FlagChip(string Text, bool IsOn);

/// <summary>The right-hand pane: the selected frame as cards (Fields) or as plain text (Raw).</summary>
public sealed partial class InspectorViewModel : ObservableObject
{
    private readonly Func<uint, string> resolveName;

    public InspectorViewModel(Func<uint, string> resolveName)
    {
        this.resolveName = resolveName;
    }

    /// <summary>Puts text on the clipboard. Set by the window.</summary>
    public Func<string, Task>? CopyText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFields), nameof(ShowRaw), nameof(ShowCards), nameof(ShowVariables), nameof(IsEmpty))]
    private bool hasFrame;

    [ObservableProperty]
    private string typeName = string.Empty;

    [ObservableProperty]
    private FrameKind kind = FrameKind.Other;

    [ObservableProperty]
    private string subtitle = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFlags))]
    private IReadOnlyList<FlagChip> flags = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFields), nameof(ShowFields), nameof(ShowRaw), nameof(ShowCards), nameof(ShowVariables))]
    private bool isRaw;

    [ObservableProperty]
    private string rawText = string.Empty;

    [ObservableProperty]
    private bool hasInstruments;

    [ObservableProperty]
    private double pitchRadians;

    [ObservableProperty]
    private double bankRadians;

    [ObservableProperty]
    private double headingRadians;

    [ObservableProperty]
    private string attitudeText = string.Empty;

    [ObservableProperty]
    private string headingText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<FieldCard> cards = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasControls))]
    private IReadOnlyList<ControlRow> controls = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVariables), nameof(ShowCards), nameof(ShowVariables))]
    private VariablesViewModel? variables;

    public bool IsEmpty => !HasFrame;

    public bool HasFlags => Flags.Count > 0;

    public bool IsFields => !IsRaw;

    public bool HasControls => Controls.Count > 0;

    public bool HasVariables => Variables is not null;

    public bool ShowFields => HasFrame && !IsRaw;

    public bool ShowRaw => HasFrame && IsRaw;

    /// <summary>Instruments and cards, in a scrolling column.</summary>
    public bool ShowCards => ShowFields && !HasVariables;

    /// <summary>The variables table fills the pane instead.</summary>
    public bool ShowVariables => ShowFields && HasVariables;

    [RelayCommand]
    private void ShowFieldsMode() => IsRaw = false;

    [RelayCommand]
    private void ShowRawMode() => IsRaw = true;

    [RelayCommand]
    private async Task CopyAsync()
    {
        if (HasFrame && CopyText is not null)
        {
            await CopyText(RawText);
        }
    }

    public void Clear()
    {
        HasFrame = false;
        TypeName = string.Empty;
        Subtitle = string.Empty;
        Flags = [];
        RawText = string.Empty;
        HasInstruments = false;
        Cards = [];
        Controls = [];
        Variables = null;
    }

    public void Show(LaneViewModel lane, FrameRow row)
    {
        RecordedFrame frame = row.Frame;
        TypeName = row.TypeName;
        Kind = row.Kind;
        Subtitle = $"{lane.Name} · frame {row.IndexText} · {row.TimeText}";
        RawText = FrameFormatter.FormatFrame(frame, resolveName);

        Flags = [];
        HasInstruments = false;
        Cards = [];
        Controls = [];
        Variables = null;

        switch (frame)
        {
            case AircraftPositionFrame ap:
                ShowInstruments(ap.Pitch, ap.Bank, ap.Heading);
                Flags = FlagChips(ap.Ground, ap.ElevationCorrection);
                Cards = PositionCards(
                    ap.Latitude, ap.Longitude, ap.Altitude, ap.Pitch, ap.Bank, ap.Heading,
                    ap.VelocityX, ap.VelocityY, ap.VelocityZ,
                    ap.AngularVelocityX, ap.AngularVelocityY, ap.AngularVelocityZ,
                    ap.AccelerationX, ap.AccelerationY, ap.AccelerationZ,
                    new FieldRow("Elevation", string.Empty, NumberFormat.Raw(ap.Elevation)));
                Controls =
                [
                    Control("Rudder", ap.RudderRaw, centered: true),
                    Control("Elevator", ap.ElevatorRaw, centered: true),
                    Control("Aileron", ap.AileronRaw, centered: true),
                    Control("Brake left", ap.BrakeLeftRaw, centered: false),
                    Control("Brake right", ap.BrakeRightRaw, centered: false),
                ];
                break;
            case ObjectPositionFrame op:
                ShowInstruments(op.Pitch, op.Bank, op.Heading);
                Flags = FlagChips(op.Ground, op.ElevationCorrection);
                Cards = PositionCards(
                    op.Latitude, op.Longitude, op.Altitude, op.Pitch, op.Bank, op.Heading,
                    op.VelocityX, op.VelocityY, op.VelocityZ,
                    op.AngularVelocityX, op.AngularVelocityY, op.AngularVelocityZ,
                    op.AccelerationX, op.AccelerationY, op.AccelerationZ,
                    new FieldRow("Height", string.Empty, NumberFormat.Raw(op.Height)));
                break;
            case SimEventFrame simEvent:
                Cards =
                [
                    new FieldCard("Event",
                    [
                        new FieldRow("Event id", string.Empty, simEvent.EventId.ToString(CultureInfo.InvariantCulture)),
                        new FieldRow("Data", string.Empty, simEvent.Data.ToString(CultureInfo.InvariantCulture)),
                    ]),
                ];
                break;
            case IntegerVariablesFrame ints:
                Variables = new VariablesViewModel(ints.Variables
                    .Select(v => new VariableRow(v.Key, resolveName(v.Key), v.Value.ToString(CultureInfo.InvariantCulture), v.Value)).ToList());
                break;
            case FloatVariablesFrame floats:
                Variables = new VariablesViewModel(floats.Variables
                    .Select(v => new VariableRow(v.Key, resolveName(v.Key), NumberFormat.Raw(v.Value), v.Value)).ToList());
                break;
            case String8VariablesFrame strings:
                Variables = new VariablesViewModel(strings.Variables
                    .Select(v => new VariableRow(v.Key, resolveName(v.Key), v.Value, null)).ToList());
                break;
        }

        HasFrame = true;
    }

    private void ShowInstruments(float pitch, float bank, float heading)
    {
        HasInstruments = true;
        PitchRadians = pitch;
        BankRadians = bank;
        HeadingRadians = heading;
        AttitudeText = $"P {NumberFormat.Degrees(pitch, 2)} · B {NumberFormat.Degrees(bank, 2)}";
        HeadingText = NumberFormat.Raw(heading) + " rad";
    }

    private static IReadOnlyList<FlagChip> FlagChips(bool ground, bool elevationCorrection) =>
    [
        new FlagChip("Ground", ground),
        new FlagChip("Elevation correction", elevationCorrection),
    ];

    private static ControlRow Control(string key, short raw, bool centered) =>
        new(key, raw.ToString(CultureInfo.InvariantCulture), NumberFormat.ControlFraction(raw, centered), centered);

    private static FieldRow Angle(string key, double radians, int decimals) =>
        new(key, NumberFormat.Degrees(radians, decimals), NumberFormat.Raw(radians));

    // Single-precision fields keep their short float text (0.19611156), not the widened double's.
    private static FieldRow Angle(string key, float radians, int decimals) =>
        new(key, NumberFormat.Degrees(radians, decimals), NumberFormat.Raw(radians));

    private static FieldRow Plain(string key, double value) => new(key, string.Empty, NumberFormat.Raw(value));

    private static FieldRow Plain(string key, float value) => new(key, string.Empty, NumberFormat.Raw(value));

    private static List<FieldCard> PositionCards(
        double latitude, double longitude, double altitude,
        float pitch, float bank, float heading,
        float velocityX, float velocityY, float velocityZ,
        float angularX, float angularY, float angularZ,
        float accelerationX, float accelerationY, float accelerationZ,
        FieldRow lastPositionRow) =>
    [
        new FieldCard("Position",
        [
            Angle("Latitude", latitude, 4),
            Angle("Longitude", longitude, 4),
            Plain("Altitude", altitude),
            lastPositionRow,
        ]),
        new FieldCard("Attitude",
        [
            Angle("Pitch", pitch, 2),
            Angle("Bank", bank, 2),
            new FieldRow("Heading", NumberFormat.Heading(heading), NumberFormat.Raw(heading)),
        ]),
        new FieldCard("Velocity", [Plain("X", velocityX), Plain("Y", velocityY), Plain("Z", velocityZ)]),
        new FieldCard("Angular velocity", [Plain("X", angularX), Plain("Y", angularY), Plain("Z", angularZ)]),
        new FieldCard("Acceleration", [Plain("X", accelerationX), Plain("Y", accelerationY), Plain("Z", accelerationZ)]),
    ];
}
