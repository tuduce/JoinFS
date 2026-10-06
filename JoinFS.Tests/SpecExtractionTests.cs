using JoinFS.Matching;

namespace JoinFS.Tests;

public class SpecExtractionTests
{
    // Shaped like the real FlyByWire A320neo files in an MSFS 2024 Community folder
    const string AircraftCfg = """
        [VERSION]
        major = 1
        [GENERAL]
        atc_type = "TT:ATCCOM.ATC_NAME AIRBUS.0.text"
        Category = "airplane"
        icao_type_designator = "A20N"
        icao_manufacturer = "AIRBUS"
        icao_model = "A-320neo"
        icao_engine_type = "Jet"
        icao_engine_count = 2
        icao_WTC = "M"
        [FLTSIM.0]
        title = "Airbus A320 Neo FlyByWire" ; Variation name
        ui_manufacturer = "FlyByWire Simulations" ; e.g. Boeing
        ui_type = "A320neo (LEAP)"
        ui_variation = "Airbus House"
        atc_id = "FBW320" ; tail number
        atc_airline = "Fly By Wire"
        """;

    const string FlightModelCfg = """
        [WEIGHT_AND_BALANCE]
        max_gross_weight = 174165 ; Maximum takeoff weight, (LBS)
        empty_weight = 93697 ; Empty weight, (LBS)
        [AIRPLANE_GEOMETRY]
        wing_area = 1317.47 ; Wing area S (SQUARE FEET)
        wing_span = 117.454 ; Wing span b (FEET)
        [REFERENCE SPEEDS]
        full_flaps_stall_speed = 115 ; Knots True (KTAS)
        flaps_up_stall_speed = 171 ; Knots True (KTAS)
        cruise_speed = 455 ; Knots True (KTAS)
        """;

    [Fact]
    public void Ini_parser_strips_comments_and_quotes_and_keeps_sections_apart()
    {
        var ini = IniFile.Parse(AircraftCfg);
        Assert.Equal("A20N", ini.Get("GENERAL", "icao_type_designator"));
        Assert.Equal("Airbus A320 Neo FlyByWire", ini.Get("FLTSIM.0", "title"));
        Assert.Equal("FBW320", ini.Get("FLTSIM.0", "atc_id"));
        Assert.Equal("", ini.Get("GENERAL", "does_not_exist"));
    }

    [Fact]
    public void Ini_parser_is_case_insensitive_for_sections_and_keys()
    {
        var ini = IniFile.Parse("[General]\nICAO_WTC = \"H\"");
        Assert.Equal("H", ini.Get("GENERAL", "icao_wtc"));
    }

    [Fact]
    public void Extracts_measured_specs_from_MSFS_cfg_files_with_unit_conversion()
    {
        var specs = CfgSpecExtractor.FromMsfsCfg(AircraftCfg, FlightModelCfg, null);

        Assert.Equal(117.454 * 0.3048, specs.SpanM.Value, 3);
        Assert.Equal(174165 * 0.45359237, specs.MtowKg.Value, 1);
        Assert.Equal(455, specs.CruiseKt.Value);
        Assert.Equal(2, specs.EngineCount.Value);
        Assert.Equal(EngineKind.Jet, specs.Engine);
        Assert.False(specs.Rotor.Value);
        Assert.Equal(SpecSource.Config, specs.SourceOf("Span"));
        Assert.Equal(SpecSource.Config, specs.SourceOf("Mtow"));
    }

    [Fact]
    public void Helicopter_category_sets_rotor()
    {
        var specs = CfgSpecExtractor.FromMsfsCfg("[GENERAL]\nCategory = \"Helicopter\"\nicao_engine_type = \"Turboprop/Turboshaft\"\nicao_engine_count = 2", "", null);
        Assert.True(specs.Rotor.Value);
        Assert.Equal(EngineKind.Turboshaft, specs.Engine);
    }

    [Fact]
    public void Numeric_engine_type_from_engines_cfg_fills_in_when_aircraft_cfg_has_none()
    {
        string engines = "[GENERALENGINEDATA]\nengine_type = 5 ; 0=Piston, 1=Jet, 2=None, 3=Helo-Turbine, 4=Rocket, 5=Turboprop\nCount = 2";
        var specs = CfgSpecExtractor.FromMsfsCfg("[GENERAL]\nCategory = \"airplane\"", "", engines);
        Assert.Equal(EngineKind.Turboprop, specs.Engine);
    }

    [Fact]
    public void Missing_files_yield_empty_specs_without_throwing()
    {
        var specs = CfgSpecExtractor.FromMsfsCfg("", "", null);
        Assert.Equal(0, specs.KnownFeatureCount);
    }

    static string ContactPoints(params string[] points) =>
        "[CONTACT_POINTS]\nmax_number_of_points = " + points.Length + "\n" + string.Join("\n", points.Select((p, i) => $"point.{i} = {p}"));

    [Fact]
    public void Nose_wheel_forward_of_the_mains_is_a_tricycle_gear()
    {
        // the real FlyByWire A320neo points: nose wheel at +27.44 ft on the centreline, mains at -14.03 ft
        string flightModel = ContactPoints(
            "1, 27.44, 0, -9.55, 800, 0, 1.25, 95, 1.0, 1.88, 1.05, 10.6, 9.4, 0, 0, 0, 2.05",
            "1, -14.03, -14, -9.83, 1200, 1, 1.9167, 0, 1.235, 1.39, 0.45, 11.1, 9.9, 2, 0, 0, 1.05",
            "1, -14.03, 14, -9.83, 1200, 2, 1.9167, 0, 1.235, 1.39, 0.45, 11.1, 9.9, 3, 0, 0, 1.05",
            "2, -26.00, -57, 6, 100, 0, 0, 0, 0, 0, 0, 0, 0, 5, 0, 0, 1");
        var specs = CfgSpecExtractor.FromMsfsCfg("", flightModel, null);
        Assert.Equal(GearKind.Tricycle, specs.Gear);
        Assert.Equal(SpecSource.Config, specs.SourceOf("Gear"));
    }

    [Fact]
    public void Tail_wheel_behind_the_mains_is_a_taildragger()
    {
        string flightModel = ContactPoints(
            "1, 1.5, -3.5, -5, 800, 1, 1.2, 0, 1, 1, 1, 1, 1, 2, 0, 0, 1",
            "1, 1.5, 3.5, -5, 800, 2, 1.2, 0, 1, 1, 1, 1, 1, 3, 0, 0, 1",
            "1, -17.0, 0, -3, 400, 0, 0.6, 30, 1, 1, 1, 1, 1, 0, 0, 0, 1");
        Assert.Equal(GearKind.Taildragger, CfgSpecExtractor.FromMsfsCfg("", flightModel, null).Gear);
    }

    [Fact]
    public void Skid_contact_points_without_wheels_are_skids()
    {
        string flightModel = ContactPoints(
            "3, 1.0, -2.5, -4, 300, 0, 0, 0, 1, 1, 1, 0, 0, 2, 0, 0, 1",
            "3, 1.0, 2.5, -4, 300, 0, 0, 0, 1, 1, 1, 0, 0, 3, 0, 0, 1");
        Assert.Equal(GearKind.Skids, CfgSpecExtractor.FromMsfsCfg("", flightModel, null).Gear);
    }

    [Fact]
    public void Float_contact_points_are_floats()
    {
        string flightModel = ContactPoints(
            "4, 1.0, -4, -4, 300, 0, 0, 0, 1, 1, 1, 0, 0, 2, 0, 0, 1",
            "4, 1.0, 4, -4, 300, 0, 0, 0, 1, 1, 1, 0, 0, 3, 0, 0, 1");
        Assert.Equal(GearKind.Floats, CfgSpecExtractor.FromMsfsCfg("", flightModel, null).Gear);
    }

    [Fact]
    public void Scrape_points_and_unclear_layouts_leave_gear_unknown()
    {
        string flightModel = ContactPoints("2, -26.00, -57, 6, 100", "2, 44.07, 0, 2, 720");
        Assert.Equal(GearKind.Unknown, CfgSpecExtractor.FromMsfsCfg("", flightModel, null).Gear);
    }

    [Fact]
    public void Garbage_numbers_are_ignored()
    {
        var specs = CfgSpecExtractor.FromMsfsCfg("", "[AIRPLANE_GEOMETRY]\nwing_span = abc", null);
        Assert.Null(specs.SpanM);
    }
}
