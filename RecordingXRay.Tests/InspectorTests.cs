using RecordingXRay.ViewModels;

namespace RecordingXRay.Tests;

public class InspectorViewModelTests
{
    private static readonly Func<uint, string> Names = id => id switch
    {
        1 => "gear position",
        2 => "flaps",
        3 => "airspeed",
        _ => "unknown",
    };

    private static InspectorViewModel Shown(RecordedFrame frame, int index = 0)
    {
        LaneViewModel lane = new(TestFrames.Aircraft("YR-SCD", frame));
        InspectorViewModel inspector = new(Names);
        inspector.Show(lane, new FrameRow(index, frame, null));
        return inspector;
    }

    [Fact]
    public void Starts_empty()
    {
        InspectorViewModel inspector = new(Names);

        Assert.True(inspector.IsEmpty);
        Assert.False(inspector.ShowFields);
    }

    [Fact]
    public void Aircraft_position_shows_instruments_flags_cards_and_controls()
    {
        InspectorViewModel inspector = Shown(TestFrames.Position(0.026));

        Assert.True(inspector.HasFrame);
        Assert.Equal("AircraftPosition", inspector.TypeName);
        Assert.Equal(FrameKind.Position, inspector.Kind);
        Assert.Equal("YR-SCD · frame [0] · 0.026 s", inspector.Subtitle);
        Assert.True(inspector.HasInstruments);
        Assert.Equal(-0.19611156f, inspector.PitchRadians, precision: 6);
        Assert.Equal("P -11.24° · B -0.45°", inspector.AttitudeText);
        Assert.Equal("2.874476 rad", inspector.HeadingText);
        Assert.Equal([new FlagChip("Ground", true), new FlagChip("Elevation correction", false)], inspector.Flags);
        Assert.Equal(["Position", "Attitude", "Velocity", "Angular velocity", "Acceleration"], inspector.Cards.Select(card => card.Title));

        FieldCard position = inspector.Cards[0];
        Assert.Equal(new FieldRow("Latitude", "55.4293°", "0.9674238236590125"), position.Rows[0]);
        Assert.Equal(new FieldRow("Elevation", string.Empty, "44.945507"), position.Rows[3]);
        Assert.Equal(new FieldRow("Pitch", "-11.24°", "-0.19611156"), inspector.Cards[1].Rows[0]);
        Assert.Equal(new FieldRow("Heading", "164.70°", "2.874476"), inspector.Cards[1].Rows[2]);
        Assert.Equal(new FieldRow("Y", string.Empty, "-0.015327258"), inspector.Cards[2].Rows[1]);

        Assert.True(inspector.HasControls);
        Assert.Equal(["Rudder", "Elevator", "Aileron", "Brake left", "Brake right"], inspector.Controls.Select(c => c.Key));
        Assert.Equal("-1471", inspector.Controls[2].RawText);
        Assert.Equal(-0.0898, inspector.Controls[2].Fraction, precision: 3);
        Assert.True(inspector.Controls[2].IsCentered);
        Assert.Equal(1.0, inspector.Controls[4].Fraction);
        Assert.False(inspector.Controls[4].IsCentered);

        Assert.True(inspector.ShowCards);
        Assert.False(inspector.ShowVariables);
        Assert.Contains("Latitude: 0.9674238236590125", inspector.RawText);
    }

    [Fact]
    public void Object_position_has_height_and_no_controls()
    {
        InspectorViewModel inspector = Shown(TestFrames.ObjectPosition(2));

        Assert.Equal("ObjectPosition", inspector.TypeName);
        Assert.False(inspector.HasControls);
        Assert.Equal("Height", inspector.Cards[0].Rows[3].Key);
        Assert.Equal("3.5", inspector.Cards[0].Rows[3].Value);
        Assert.Equal([new FlagChip("Ground", false), new FlagChip("Elevation correction", true)], inspector.Flags);
    }

    [Fact]
    public void Sim_event_shows_one_card()
    {
        InspectorViewModel inspector = Shown(TestFrames.Event(1));

        FieldCard card = Assert.Single(inspector.Cards);
        Assert.Equal("Event", card.Title);
        Assert.Equal(["7", "9"], card.Rows.Select(row => row.Value));
        Assert.False(inspector.HasInstruments);
        Assert.False(inspector.HasFlags);
    }

    [Fact]
    public void Variables_frame_shows_a_named_table_instead_of_cards()
    {
        InspectorViewModel inspector = Shown(TestFrames.Integers(0.154, (2, 7), (1, -3), (99, 0)));

        Assert.False(inspector.ShowCards);
        Assert.True(inspector.ShowVariables);
        Assert.Empty(inspector.Cards);
        VariablesViewModel table = inspector.Variables!;
        Assert.Equal([1u, 2u, 99u], table.View.Select(row => row.Id));
        Assert.Equal("gear position", table.View[0].Name);
        Assert.Equal("unknown", table.View[2].Name);
        Assert.Equal("3 variables", table.CountText);
    }

    [Fact]
    public void Variables_table_filters_and_sorts()
    {
        VariablesViewModel table = Shown(TestFrames.Floats(1, (1, 2.5f), (2, -10f), (3, 0.25f))).Variables!;

        table.SortByCommand.Execute(VariableSort.Value);
        Assert.Equal(["-10", "0.25", "2.5"], table.View.Select(row => row.Value));
        Assert.Equal("VALUE ▲", table.ValueHeader);

        table.SortByCommand.Execute(VariableSort.Value);
        Assert.Equal(["2.5", "0.25", "-10"], table.View.Select(row => row.Value));
        Assert.Equal("VALUE ▼", table.ValueHeader);

        table.SortByCommand.Execute(VariableSort.Name);
        Assert.Equal(["airspeed", "flaps", "gear position"], table.View.Select(row => row.Name));

        table.FilterText = "FLAP";
        Assert.Equal(["flaps"], table.View.Select(row => row.Name));
        Assert.Equal("1 of 3 variables", table.CountText);

        table.FilterText = string.Empty;
        table.SortByCommand.Execute(VariableSort.Id);
        Assert.Equal([1u, 2u, 3u], table.View.Select(row => row.Id));
    }

    [Fact]
    public void String_variables_sort_as_text()
    {
        VariablesViewModel table = Shown(TestFrames.Strings(1, (1, "beta"), (2, "Alpha"))).Variables!;

        table.SortByCommand.Execute(VariableSort.Value);

        Assert.Equal(["Alpha", "beta"], table.View.Select(row => row.Value));
    }

    [Fact]
    public void Raw_mode_and_copy()
    {
        InspectorViewModel inspector = Shown(TestFrames.Position(0.026));
        string? copied = null;
        inspector.CopyText = text =>
        {
            copied = text;
            return Task.CompletedTask;
        };

        Assert.True(inspector.IsFields);
        inspector.ShowRawModeCommand.Execute(null);
        Assert.True(inspector.ShowRaw);
        Assert.False(inspector.ShowCards);

        inspector.CopyCommand.Execute(null);
        Assert.Equal(inspector.RawText, copied);

        inspector.ShowFieldsModeCommand.Execute(null);
        Assert.True(inspector.ShowCards);
    }

    [Fact]
    public void Clear_returns_to_the_empty_state()
    {
        InspectorViewModel inspector = Shown(TestFrames.Position(0.026));

        inspector.Clear();

        Assert.True(inspector.IsEmpty);
        Assert.Empty(inspector.Cards);
        Assert.Empty(inspector.Controls);
        Assert.False(inspector.HasInstruments);
    }
}
