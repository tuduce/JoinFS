using JoinFS.Matching;

namespace JoinFS.Tests
{
    /// <summary>
    /// Measured data per model folder: wing span, weight, speeds, engines and gear from the model's own aircraft.cfg / flight_model.cfg /
    /// engines.cfg (MSFS) or from the single aircraft.cfg (FSX/P3D style). A livery package inherits what its base_container defines.
    /// </summary>
    public sealed class MeasuredSpecsReaderTests : IDisposable
    {
        readonly string root = Path.Combine(Path.GetTempPath(), "measured-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        string Write(string relative, string content)
        {
            string path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return Path.GetDirectoryName(path)!;
        }

        const string BaseAircraft = """
            [GENERAL]
            Category = "airplane"
            icao_type_designator = "A20N"
            icao_manufacturer = "AIRBUS"
            icao_engine_type = "Jet"
            icao_engine_count = 2
            [FLTSIM.0]
            title = "Airbus A320 Neo FlyByWire"
            """;

        const string FlightModel = """
            [WEIGHT_AND_BALANCE]
            max_gross_weight = 174165
            [AIRPLANE_GEOMETRY]
            wing_span = 117.454
            [REFERENCE SPEEDS]
            cruise_speed = 455
            [CONTACT_POINTS]
            point.0 = 1, 27.44, 0, -9.55
            point.1 = 1, -14.03, -14, -9.83
            point.2 = 1, -14.03, 14, -9.83
            """;

        string CreateBase() => Write(@"pkg\SimObjects\AirPlanes\FlyByWire_A320_NEO\aircraft.cfg", BaseAircraft);

        [Fact]
        public void Reads_span_weight_speed_engines_and_gear_of_an_MSFS_aircraft_folder()
        {
            string folder = CreateBase();
            Write(@"pkg\SimObjects\AirPlanes\FlyByWire_A320_NEO\flight_model.cfg", FlightModel);

            var specs = new MeasuredSpecsReader().ReadFolder(folder)!;

            Assert.Equal(117.454 * 0.3048, specs.SpanM!.Value, 3);
            Assert.Equal(174165 * 0.45359237, specs.MtowKg!.Value, 1);
            Assert.Equal(455, specs.CruiseKt);
            Assert.Equal(2, specs.EngineCount);
            Assert.Equal(EngineKind.Jet, specs.Engine);
            Assert.Equal(GearKind.Tricycle, specs.Gear);
            Assert.Equal(SpecSource.Config, specs.SourceOf("Span"));
        }

        [Fact]
        public void A_livery_package_inherits_the_flight_model_and_engines_of_its_base_container()
        {
            CreateBase();
            Write(@"pkg\SimObjects\AirPlanes\FlyByWire_A320_NEO\flight_model.cfg", FlightModel);
            string livery = Write(@"liv\SimObjects\AirPlanes\_Livery\aircraft.cfg", "[VARIATION]\nbase_container = \"..\\FlyByWire_A320_NEO\"\n[FLTSIM.0]\ntitle = \"Livery\"");

            // the livery package sits in another package: the base container is found by its folder name
            var specs = new MeasuredSpecsReader(name => name == "FlyByWire_A320_NEO" ? Path.Combine(root, @"pkg\SimObjects\AirPlanes\FlyByWire_A320_NEO") : null).ReadFolder(livery)!;

            Assert.Equal(455, specs.CruiseKt);
            Assert.Equal(2, specs.EngineCount);
            Assert.Equal(EngineKind.Jet, specs.Engine);
        }

        [Fact]
        public void A_relative_base_container_next_to_the_livery_is_followed_without_any_lookup()
        {
            CreateBase();
            Write(@"pkg\SimObjects\AirPlanes\FlyByWire_A320_NEO\flight_model.cfg", FlightModel);
            string livery = Write(@"pkg\SimObjects\AirPlanes\_Livery\aircraft.cfg", "[VARIATION]\nbase_container = \"..\\FlyByWire_A320_NEO\"\n[FLTSIM.0]\ntitle = \"Livery\"");

            var specs = new MeasuredSpecsReader().ReadFolder(livery)!;
            Assert.Equal(455, specs.CruiseKt);
        }

        [Fact]
        public void An_FSX_style_single_aircraft_cfg_is_its_own_flight_model()
        {
            string folder = Write(@"fsx\SimObjects\Airplanes\Cessna\aircraft.cfg", """
                [GENERAL]
                category = airplane
                [WEIGHT_AND_BALANCE]
                max_gross_weight = 2550
                [AIRPLANE_GEOMETRY]
                wing_span = 36.1
                [REFERENCE SPEEDS]
                cruise_speed = 122
                """);

            var specs = new MeasuredSpecsReader().ReadFolder(folder)!;

            Assert.Equal(36.1 * 0.3048, specs.SpanM!.Value, 3);
            Assert.Equal(122, specs.CruiseKt);
        }

        [Fact]
        public void A_folder_without_anything_measurable_gives_null()
        {
            string folder = Write(@"x\aircraft.cfg", "[FLTSIM.0]\ntitle = \"Nothing\"");
            Assert.Null(new MeasuredSpecsReader().ReadFolder(folder));
        }

        [Fact]
        public void A_missing_or_blank_folder_gives_null_and_never_throws()
        {
            var reader = new MeasuredSpecsReader();
            Assert.Null(reader.ReadFolder(Path.Combine(root, "nope")));
            Assert.Null(reader.ReadFolder(""));
            Assert.Null(reader.ReadFolder(null!));
        }

        [Fact]
        public void The_same_folder_is_read_only_once()
        {
            string folder = CreateBase();
            Write(@"pkg\SimObjects\AirPlanes\FlyByWire_A320_NEO\flight_model.cfg", FlightModel);
            var reader = new MeasuredSpecsReader();

            var first = reader.ReadFolder(folder);
            File.Delete(Path.Combine(folder, "flight_model.cfg"));
            var second = reader.ReadFolder(folder);

            Assert.Same(first, second);
        }

        [Fact]
        public void Garbage_in_the_cfg_files_is_survived()
        {
            string folder = Write(@"bad\aircraft.cfg", "\0\0 not an ini file [[[");
            Write(@"bad\flight_model.cfg", "wing_span = abc\n[x");
            Assert.Null(new MeasuredSpecsReader().ReadFolder(folder));
        }
    }
}
