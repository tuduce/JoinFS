using System.Collections.Generic;
using Model = JoinFS.Substitution.Model;

namespace JoinFS.Matching
{
    /// <summary>Specs for one aircraft plus a human-readable note on how each came about (shown in the report).</summary>
    public sealed class ResolvedSpecs
    {
        public AircraftSpecs Specs = new();
        /// <summary>The ICAO type the reference data was looked up under (after alias/title inference); "" when none</summary>
        public string EffectiveIcao = "";
        public List<string> Notes = [];
    }

    /// <summary>
    /// Decides which physical data to use for an aircraft, best source first: measured values from the model's own cfg
    /// files/SimConnect, then the public reference for its ICAO type (a wrong tag corrected through the alias table, or a
    /// type inferred from the title), then coarse values derived from the Doc8643 class code / typerole.
    /// </summary>
    public sealed class SpecResolver
    {
        readonly ReferenceSpecs reference;
        readonly Doc8643 doc8643;

        public SpecResolver(ReferenceSpecs reference, Doc8643 doc8643)
        {
            this.reference = reference;
            this.doc8643 = doc8643;
        }

        public ResolvedSpecs ForRemote(MatchRequest request)
        {
            ResolvedSpecs resolved = new();
            string icao = reference.ResolveAlias(request.IcaoType);
            if (icao != request.IcaoType) resolved.Notes.Add($"ICAO type '{request.IcaoType}' is an alias of '{icao}'.");

            AircraftSpecs specs = reference.Find(icao);
            if (specs != null)
            {
                resolved.EffectiveIcao = icao;
                resolved.Notes.Add($"Public reference data for ICAO type '{icao}'.");
            }
            else
            {
                string inferred = reference.InferIcaoFromText(request.Title);
                specs = inferred != null ? reference.Find(inferred) : null;
                if (specs != null)
                {
                    resolved.EffectiveIcao = inferred;
                    resolved.Notes.Add($"ICAO type '{request.IcaoType}' has no reference data; type '{inferred}' inferred from the title '{request.Title}'.");
                }
            }

            if (specs == null)
            {
                specs = Coarse(request.ClassCode.Length == 3 ? request.ClassCode : ClassCodeOf(request.IcaoType), request.Typerole);
                resolved.Notes.Add(specs.KnownFeatureCount > 0
                    ? "No reference data: only coarse values derived from the class code / typerole."
                    : "No reference data and no class code: nothing is known about this aircraft's size or speed.");
            }

            FillManufacturer(specs, resolved.EffectiveIcao.Length > 0 ? resolved.EffectiveIcao : request.IcaoType);
            resolved.Specs = specs;
            return resolved;
        }

        /// <summary>
        /// The manufacturer from Doc8643 for the type. Doc8643 outranks the cfg's icao_manufacturer, which add-on authors often
        /// get wrong (seen in practice: "B748", the type, in the manufacturer field); the cfg value is only kept when the type
        /// is not a Doc8643 designator.
        /// </summary>
        void FillManufacturer(AircraftSpecs specs, string icaoType)
        {
            // curated reference first, then the majority maker in Doc8643, and only then whatever the cfg claims
            string key = ManufacturerKey.From(reference.ManufacturerOf(icaoType));
            if (key.Length == 0) key = ManufacturerKey.From(doc8643.MajorityManufacturer(icaoType));
            if (key.Length > 0)
            {
                specs.Manufacturer = key;
                specs.Set("Manufacturer", SpecSource.Reference);
            }
        }

        public ResolvedSpecs ForModel(Model model)
        {
            ResolvedSpecs resolved = new();
            string icao = reference.ResolveAlias(model.icaoType);
            AircraftSpecs fallback = reference.Find(icao);
            if (fallback != null)
            {
                resolved.EffectiveIcao = icao;
                resolved.Notes.Add($"Public reference data for ICAO type '{icao}'.");
            }
            else
            {
                string inferred = reference.InferIcaoFromText(model.title + " " + model.variation);
                fallback = inferred != null ? reference.Find(inferred) : null;
                if (fallback != null)
                {
                    resolved.EffectiveIcao = inferred;
                    resolved.Notes.Add($"No reference data for tag '{model.icaoType}'; type '{inferred}' inferred from the title.");
                }
            }
            if (fallback == null)
            {
                fallback = Coarse(model.classCode, model.typerole);
                resolved.Notes.Add("No reference data: coarse values from class code / typerole.");
            }

            if (model.specs != null && model.specs.KnownFeatureCount > 0)
            {
                resolved.Specs = Overlay(model.specs, fallback);
                resolved.Notes.Insert(0, "Measured values from the model's own configuration (" + SourceName(model.specs) + ").");
            }
            else
            {
                resolved.Specs = fallback;
            }
            FillManufacturer(resolved.Specs, resolved.EffectiveIcao.Length > 0 ? resolved.EffectiveIcao : model.icaoType);
            return resolved;
        }

        static string SourceName(AircraftSpecs specs) =>
            specs.Sources.ContainsValue(SpecSource.SimConnect) ? "SimConnect" : "aircraft.cfg / flight_model.cfg";

        string ClassCodeOf(string icaoType) => doc8643.TryGet(icaoType, out var row) ? row.ClassCode : "";

        /// <summary>Coarse specs from a Doc8643 class code ("L2J" = landplane, 2 engines, jet) and typerole - low-confidence last resort.</summary>
        static AircraftSpecs Coarse(string classCode, int typerole)
        {
            AircraftSpecs specs = new();
            bool rotor = typerole == TypeRole.Rotorcraft;
            if (typerole == TypeRole.Glider && classCode.Length != 3)
            {
                // a model that says only "glider": no engine, not a rotorcraft
                specs.Engine = EngineKind.None;
                specs.EngineCount = 0;
                specs.Rotor = false;
                specs.Set("EngineType", SpecSource.Inferred);
                specs.Set("Engines", SpecSource.Inferred);
                specs.Set("Rotor", SpecSource.Inferred);
                return specs;
            }
            if (classCode.Length == 3)
            {
                rotor = rotor || classCode[0] is 'H' or 'G';
                if (int.TryParse(classCode[1].ToString(), out int engines) && engines > 0)
                {
                    specs.EngineCount = engines;
                    specs.Set("Engines", SpecSource.Inferred);
                }
                EngineKind kind = classCode[2] switch
                {
                    'P' => EngineKind.Piston,
                    'T' => rotor ? EngineKind.Turboshaft : EngineKind.Turboprop,
                    'J' => EngineKind.Jet,
                    'E' => EngineKind.Electric,
                    _ => EngineKind.Unknown
                };
                if (kind != EngineKind.Unknown)
                {
                    specs.Engine = kind;
                    specs.Set("EngineType", SpecSource.Inferred);
                }
            }
            if (rotor)
            {
                specs.Rotor = true;
                specs.Set("Rotor", SpecSource.Inferred);
            }
            return specs;
        }

        /// <summary>Every feature from <paramref name="primary"/> when it has one, else from <paramref name="fallback"/> - with sources.</summary>
        public static AircraftSpecs Overlay(AircraftSpecs primary, AircraftSpecs fallback)
        {
            AircraftSpecs result = new() { Name = fallback.Name.Length > 0 ? fallback.Name : primary.Name };

            void Pick<T>(string feature, T primaryValue, T fallbackValue, System.Action<T> assign, bool primaryHas, bool fallbackHas)
            {
                if (primaryHas) { assign(primaryValue); result.Set(feature, primary.SourceOf(feature) == SpecSource.None ? SpecSource.Config : primary.SourceOf(feature)); }
                else if (fallbackHas) { assign(fallbackValue); result.Set(feature, fallback.SourceOf(feature)); }
            }

            Pick("Rotor", primary.Rotor, fallback.Rotor, v => result.Rotor = v, primary.Rotor.HasValue, fallback.Rotor.HasValue);
            Pick("Wing", primary.Wing, fallback.Wing, v => result.Wing = v, primary.Wing != WingConfig.Unknown, fallback.Wing != WingConfig.Unknown);
            Pick("Span", primary.SpanM, fallback.SpanM, v => result.SpanM = v, primary.SpanM.HasValue, fallback.SpanM.HasValue);
            Pick("Length", primary.LengthM, fallback.LengthM, v => result.LengthM = v, primary.LengthM.HasValue, fallback.LengthM.HasValue);
            Pick("Mtow", primary.MtowKg, fallback.MtowKg, v => result.MtowKg = v, primary.MtowKg.HasValue, fallback.MtowKg.HasValue);
            Pick("Engines", primary.EngineCount, fallback.EngineCount, v => result.EngineCount = v, primary.EngineCount.HasValue, fallback.EngineCount.HasValue);
            Pick("EngineType", primary.Engine, fallback.Engine, v => result.Engine = v, primary.Engine != EngineKind.Unknown, fallback.Engine != EngineKind.Unknown);
            Pick("Cruise", primary.CruiseKt, fallback.CruiseKt, v => result.CruiseKt = v, primary.CruiseKt.HasValue, fallback.CruiseKt.HasValue);
            Pick("Gear", primary.Gear, fallback.Gear, v => result.Gear = v, primary.Gear != GearKind.Unknown, fallback.Gear != GearKind.Unknown);
            Pick("Manufacturer", primary.Manufacturer, fallback.Manufacturer, v => result.Manufacturer = v, primary.Manufacturer.Length > 0, fallback.Manufacturer.Length > 0);
            return result;
        }
    }
}
