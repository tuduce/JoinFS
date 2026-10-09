using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;

namespace JoinFS.Matching
{
    /// <summary>
    /// Reads measured aircraft data from a simulator model's own configuration files. For MSFS (2020/2024):
    /// aircraft.cfg [GENERAL] (Category, icao_engine_type/count), flight_model.cfg ([AIRPLANE_GEOMETRY] wing_span in ft,
    /// [WEIGHT_AND_BALANCE] max_gross_weight in lb, [REFERENCE SPEEDS] cruise_speed in kt) and
    /// engines.cfg ([GENERALENGINEDATA] engine_type 0..5, Count). Values are converted to metres/kilograms/knots.
    /// </summary>
    public static class CfgSpecExtractor
    {
        const double FeetToMetres = 0.3048;
        const double PoundsToKilograms = 0.45359237;

        public static AircraftSpecs FromMsfsCfg(string aircraftCfg, string flightModelCfg, string enginesCfg)
        {
            AircraftSpecs specs = new();
            IniFile aircraft = IniFile.Parse(aircraftCfg ?? "");
            IniFile flightModel = IniFile.Parse(flightModelCfg ?? "");
            IniFile engines = IniFile.Parse(enginesCfg ?? "");

            specs.Manufacturer = ManufacturerKey.From(aircraft.Get("GENERAL", "icao_manufacturer"));
            if (specs.Manufacturer.Length > 0) specs.Set("Manufacturer", SpecSource.Config);
            ReadCategory(specs, aircraft);
            ReadEngines(specs, aircraft, engines);
            ReadFlightModel(specs, flightModel);
            ReadGear(specs, flightModel);
            return specs;
        }

        static void ReadCategory(AircraftSpecs specs, IniFile aircraft)
        {
            string category = aircraft.Get("GENERAL", "Category");
            if (category.Equals("Helicopter", StringComparison.OrdinalIgnoreCase)) specs.Rotor = true;
            else if (category.Equals("Airplane", StringComparison.OrdinalIgnoreCase)) specs.Rotor = false;
            else return;

            specs.Set("Rotor", SpecSource.Config);
        }

        static void ReadEngines(AircraftSpecs specs, IniFile aircraft, IniFile engines)
        {
            string typeText = aircraft.Get("GENERAL", "icao_engine_type");
            EngineKind kind = EngineKindFromText(typeText, specs.Rotor == true);
            if (kind == EngineKind.Unknown && int.TryParse(engines.Get("GENERALENGINEDATA", "engine_type"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
            {
                kind = EngineKindFromCode(code);
            }
            if (kind != EngineKind.Unknown)
            {
                specs.Engine = kind;
                specs.Set("EngineType", SpecSource.Config);
            }

            string countText = aircraft.Get("GENERAL", "icao_engine_count");
            if (countText.Length == 0) countText = engines.Get("GENERALENGINEDATA", "Count");
            if (int.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) && count > 0 && count <= 9)
            {
                specs.EngineCount = count;
                specs.Set("Engines", SpecSource.Config);
            }
        }

        static void ReadFlightModel(AircraftSpecs specs, IniFile flightModel)
        {
            if (TryNumber(flightModel.Get("AIRPLANE_GEOMETRY", "wing_span"), out double spanFeet) && spanFeet > 0)
            {
                specs.SpanM = spanFeet * FeetToMetres;
                specs.Set("Span", SpecSource.Config);
            }
            if (TryNumber(flightModel.Get("WEIGHT_AND_BALANCE", "max_gross_weight"), out double pounds) && pounds > 0)
            {
                specs.MtowKg = pounds * PoundsToKilograms;
                specs.Set("Mtow", SpecSource.Config);
            }
            if (TryNumber(flightModel.Get("REFERENCE SPEEDS", "cruise_speed"), out double cruise) && cruise > 0)
            {
                specs.CruiseKt = cruise;
                specs.Set("Cruise", SpecSource.Config);
            }
        }

        /// <summary>
        /// Gear layout from [CONTACT_POINTS] "point.N = type, longitudinal(ft, + = forward), lateral(ft), vertical, ...".
        /// Type 1 wheel, 3 skid, 4 float, 16 ski (2 = scrape point is ignored). Wheels: a centreline wheel forward of every
        /// other wheel is a nose wheel (tricycle), one behind all of them is a tail wheel (taildragger).
        /// </summary>
        static void ReadGear(AircraftSpecs specs, IniFile flightModel)
        {
            List<(double longitudinal, double lateral)> wheels = [];
            int skids = 0, floats = 0, skis = 0;

            foreach (string value in flightModel.GetAll("CONTACT_POINTS", "point."))
            {
                string[] parts = value.Split(',');
                if (parts.Length < 3 || !TryNumber(parts[0], out double type)) continue;
                TryNumber(parts[1], out double longitudinal);
                TryNumber(parts[2], out double lateral);

                switch ((int)type)
                {
                    case 1: wheels.Add((longitudinal, lateral)); break;
                    case 3: skids++; break;
                    case 4: floats++; break;
                    case 16: skis++; break;
                }
            }

            GearKind gear = ClassifyGear(wheels, skids, floats, skis);
            if (gear != GearKind.Unknown)
            {
                specs.Gear = gear;
                specs.Set("Gear", SpecSource.Config);
            }
        }

        static GearKind ClassifyGear(List<(double longitudinal, double lateral)> wheels, int skids, int floats, int skis)
        {
            if (floats > 0) return GearKind.Floats;
            if (skis > 0) return GearKind.Skis;
            if (wheels.Count == 0) return skids > 0 ? GearKind.Skids : GearKind.Unknown;
            if (wheels.Count < 3) return GearKind.Unknown;

            const double CentrelineToleranceFeet = 0.5;
            var centreline = wheels.Where(w => Math.Abs(w.lateral) <= CentrelineToleranceFeet).ToList();
            var sides = wheels.Where(w => Math.Abs(w.lateral) > CentrelineToleranceFeet).ToList();
            if (centreline.Count != 1 || sides.Count < 2) return GearKind.Unknown;

            double centre = centreline[0].longitudinal;
            if (centre > sides.Max(w => w.longitudinal)) return GearKind.Tricycle;
            if (centre < sides.Min(w => w.longitudinal)) return GearKind.Taildragger;
            return GearKind.Unknown;
        }

        static bool TryNumber(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>icao_engine_type per the MSFS SDK: Piston, Jet, Turboprop/Turboshaft, Electric (also "Rocket", "None").</summary>
        public static EngineKind EngineKindFromText(string text, bool rotor)
        {
            string t = (text ?? "").Trim().ToLowerInvariant();
            if (t.Length == 0) return EngineKind.Unknown;
            if (t.Contains("turboprop") || t.Contains("turboshaft")) return rotor ? EngineKind.Turboshaft : EngineKind.Turboprop;
            if (t.Contains("jet")) return EngineKind.Jet;
            if (t.Contains("piston")) return EngineKind.Piston;
            if (t.Contains("electric")) return EngineKind.Electric;
            return EngineKind.Unknown;
        }

        /// <summary>engines.cfg engine_type / SimConnect "ENGINE TYPE": 0 piston, 1 jet, 3 helo turbine, 5 turboprop.</summary>
        public static EngineKind EngineKindFromCode(int code) => code switch
        {
            0 => EngineKind.Piston,
            1 => EngineKind.Jet,
            3 => EngineKind.Turboshaft,
            5 => EngineKind.Turboprop,
            _ => EngineKind.Unknown
        };
    }
}
