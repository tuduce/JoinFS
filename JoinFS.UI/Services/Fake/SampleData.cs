using System.Globalization;
using JoinFS.UI.Models;

namespace JoinFS.UI.Services.Fake;

/// <summary>The prototype's sample rows, shared by the fake services and the tests.</summary>
public static class SampleData
{
    public static IReadOnlyList<HubInfo> Hubs { get; } =
    [
        new("Planet FsHub", HubStatus.Online, 24, 24, "26.4.0", "The official FsHub community server for JoinFS pilots — casual flying, weekly group flights, and a friendly ATC crew.", "discord.gg/planetfshub", "Sat Oct 4, 18:00 UTC — Group flight KJFK→KBOS", "fshub.io:24192"),
        new("AirSherpa", HubStatus.Online, 6, 6, "3.2.17", "Small mountain-flying focused hub.", "", "", "airsherpa.net:24192"),
        new("DigitalThemePark", HubStatus.Online, 5, 5, "3.2.17", "", "teamspeak.dtp-network.com", "", "dtp-network.com:24192"),
        new("swiss and europe", HubStatus.Global, 4, 4, "3.2.17", "Regional hub covering Swiss and European routes.", "", "Sun Oct 12, 17:00 UTC — Alps VFR tour", "85.195.0.14:24192"),
        new("Flight Unlimited Network", HubStatus.Online, 0, 0, "26.4.0", "", "", "", "flightunlimited.net:24192"),
        new("Aidan's Hub", HubStatus.Password, 0, 0, "26.5.0", "Private hub, invite only.", "", "", "aidan-hub.net:24192"),
        new("Retro Flight Club", HubStatus.Offline, 0, 0, "26.3.1", "Vintage aircraft community, currently offline.", "", "", "retroflight.club:24192"),
        new("NoiseAbatement Hub", HubStatus.Online, 3, 3, "26.4.0", "Community hub focused on noise-abatement procedures around busy airports.", "", "", "noiseabatement.net:24192"),
    ];

    public static IReadOnlyList<AddressBookEntry> AddressBook { get; } =
    [
        new("Global", "Global hubs mesh", BuiltIn: true),
        new("Planet FsHub", "fshub.io:24192"),
        new("AirSherpa", "airsherpa.net:24192"),
        new("Aidan's Hub", "aidan-hub.net:24192", RequiresPassword: true),
        new("swiss and europe", "85.195.0.14:24192"),
    ];

    public static IReadOnlyList<PeerInfo> Peers { get; } =
    [
        Peer("6Knotts", "ASXGS", 36, "Microsoft Flight Simulator 2024", legacy: true),
        Peer("ADF320", "A320", 162, "Prepar3D v5", legacy: true),
        Peer("azizba213", "", 123, "Microsoft Flight Simulator 2024", legacy: true),
        Peer("Breizh Punisher", "F-SLCD", 38, "X-Plane", legacy: true),
        Peer("CarGuy86", "G-HUGE", 108, "Microsoft Flight Simulator 2024", legacy: true),
        Peer("David18", "ASXGS", 22, "Microsoft Flight Simulator 2020", legacy: false),
        Peer("DiegoCuervo", "LV-OPA", 255, "Microsoft Flight Simulator 2024", legacy: true),
        Peer("HB-TDX", "HB-TDX", 0, "Microsoft Flight Simulator 2024", legacy: false, isMe: true),
    ];

    // The prototype derives the version from the protocol and the port from the nickname, until real data is wired in.
    private static PeerInfo Peer(string nick, string callsign, int latency, string simulator, bool legacy, bool isMe = false) =>
        new(nick, nick, callsign, "Yes", latency, 1, 0, simulator, legacy ? "18.2.4" : "26.4.0", isMe ? "" : legacy ? "Legacy" : "JFP2", 6809 + nick.Length % 40, isMe);

    public static IReadOnlyList<AircraftInfo> Aircraft { get; } = BuildAircraft();

    /// <summary>Shown when "Include All Hub Aircraft" is on: the aircraft of the other public hubs.</summary>
    public static IReadOnlyList<AircraftInfo> HubAircraft { get; } =
    [
        Aircraft1("HUB-001", "Retro Flight Club", 1204.5, 90, 8500, 180, "Douglas DC-3", AircraftActions.None),
        Aircraft1("HUB-002", "AirSherpa", 612.2, 270, 11500, 140, "Pilatus PC-6", AircraftActions.None),
    ];

    /// <summary>Shown when "Include All Simulator Aircraft" is on: the local simulator's own AI aircraft.</summary>
    public static IReadOnlyList<AircraftInfo> SimulatorAircraft { get; } =
    [
        Aircraft1("AI-0001", "Sim (A)", 12.4, 45, 4000, 210, "Airbus A320neo (AI)", AircraftActions.Ignore | AircraftActions.Record),
        Aircraft1("AI-0002", "Sim (A)", 30.1, 225, 12000, 330, "Boeing 737-800 (AI)", AircraftActions.Ignore | AircraftActions.Record),
    ];


    public static IReadOnlyList<ObjectInfo> Objects { get; } =
    [
        Obj("Pastou", "Airbus H145 Red Carpet (A)", 3, 45, 390.3, true, false, false),
        Obj("Jcfoster", "PC-12 D-FCAH (A)", 1, 96, 4549.4, true, false, false),
        Obj("David18", "Black Square B36TP Bonanza", 2, 32, 8813.0, false, false, false),
        Obj("Jeka28", "GC1a Swift (Factory) (D)", 1, 44, 8818.2, true, true, false),
        Obj("Nacho", "GC1a Swift (Factory) (D)", 4, 22, 6306.6, true, false, true),
        Obj("Nicksrun75", "C208B Cargo (Cargo 01)", 1, 285, 458.6, false, false, false),
    ];

    private static ObjectInfo Obj(string owner, string model, int count, int bearing, double distance, bool broadcast, bool ignoreOwner, bool ignoreModel) =>
        new($"{owner}/{model}", owner, model, ModelNames.StripVariantSuffix(model), count, bearing, distance,
            broadcast, ignoreOwner, ignoreModel, ModelBroadcast: false, CanBroadcast: true, CanIgnore: true, CanSubstitute: true);

    public static IReadOnlyList<ModelRule> DefaultRules { get; } =
    [
        new("Default SingleProp", "GC1a Swift - factory", true),
        new("Default TwinProp", "OV-10 Bronco Montelimar", true),
        new("Default Airliner", "PMDG 777-200ER GE PMDG House", true),
        new("Default Rotorcraft", "CH-1 N-1981", true),
        new("Default Glider", "K21 D-1258", true),
        new("Default Fighter", "MiG-15Bis BARE METAL", true),
        new("Default Bomber", "Latecoere 631", true),
        new("Default FourProp", "Junkers Ju52/3m Modern", true),
    ];

    public static IReadOnlyList<string> ModelTypes { get; } =
        ["GC1a Swift - factory", "OV-10 Bronco Montelimar", "PMDG 777-200ER GE PMDG House", "CH-1 N-1981", "K21 D-1258", "MiG-15Bis BARE METAL", "Latecoere 631", "Junkers Ju52/3m Modern"];

    public static IReadOnlyList<string> ModelVariations { get; } = ["Factory", "Livery A", "Livery B"];

    public static IReadOnlyList<VariableAssignment> Variables { get; } =
    [
        new("GC1a Swift (Factory)", ["ListBox_Sets"]),
        new("PMDG 777-200ER GE PMDG House", ["Custom_FMC_Vars", "ListBox_Sets"]),
        new("CH-1 N-1981", ["Rotor_Vars"]),
    ];

    public static IReadOnlyList<ChatMessage> Chat { get; } =
    [
        new("6Knotts", "anyone else at KJFK right now?"),
        new("ADF320", "just departed, heading your way"),
        new("Breizh Punisher", "weather looks rough over the Atlantic tonight"),
    ];

    public static IReadOnlyList<RecordedAircraft> LoadedRecordList { get; } =
    [
        new("6Knotts", "Bonanza"), new("ADF320", "A320"), new("Breizh Punisher", "F-SLCD"),
        new("CarGuy86", "A380-800"), new("David18", "Cessna 172"), new("DiegoCuervo", "737-800"),
    ];

    public static IReadOnlyList<string> LogLines { get; } =
    [
        "16:05:30.866 - Delisting aircraft 'F-SLCD' - Model 'Robin DR401'",
        "16:05:30.880 - Listing aircraft 'F-MAGK' from '05776-64988-6112/10' - Model 'Tecnam P2012 Traveller Combi'",
        "16:05:30.901 - Listing aircraft 'ASXGS' from '53713-39253-6112/7' - Model 'FFX P180 Medevac'",
        "16:05:30.919 - Listing aircraft 'C-SR88' from '04579-46071-6112/3' - Model 'TFDi Design MD-11 GE CF6-80c'",
        "16:05:32.340 - Delisting aircraft 'TOM738' from '00153-57060-6112/66' - Model 'PMDG 737-800 TUI Airways'",
        "16:06:25.027 - ERROR Unable to get weather observation",
        "16:07:08.856 - Removed node '24943-43180-6112/200'",
    ];

    private static AircraftInfo Aircraft1(string callsign, string owner, double distance, int heading, int altitude, double speed, string model, AircraftActions can) =>
        new(callsign, callsign, owner, distance, heading, altitude, speed, model,
            Bearing: heading, Squawk: "1200", Com1: "118.000", Com2: "121.500", Simulator: "Microsoft Flight Simulator 2024",
            OriginalModel: ModelNames.StripVariantSuffix(model), FlightPlan: "No flight plan filed", Remarks: "None",
            AircraftLinkState.Created, Recording: false, Ignored: false, Tracked: false, can);

    private static IReadOnlyList<AircraftInfo> BuildAircraft()
    {
        (string Callsign, string Owner, double Distance, int Heading, int Altitude, int Gs, string Model)[] raw =
        [
            ("LV-ALB", "", 4528.6, 357, 1607, 53, "GC1a Swift (Factory)"),
            ("9H-WDR", "Pastou", 390.3, 311, 34461, 445, "Airbus H145 Red Carpet (A)"),
            ("A320", "ADF320", 3411.4, 63, 24, 0, "GC1a Swift (Factory) (D)"),
            ("AAL2693", "Jcfoster", 4549.4, 96, 597, 0, "PC-12 D-FCAH (A)"),
            ("ASXGS", "David18", 8813.0, 32, 4992, 263, "Black Square B36TP Bonanza"),
            ("AUI5501", "Jeka28", 8818.2, 44, 3150, 217, "GC1a Swift (Factory) (D)"),
            ("C-GTLX", "Nacho", 6306.6, 22, 39164, 525, "GC1a Swift (Factory) (D)"),
            ("CTO75", "Nicksrun75", 458.6, 285, 1898, 172, "C208B Cargo (Cargo 01)"),
        ];

        // The prototype starts with these four recorded; squawk and radio frequencies are derived from the names until real data is wired in.
        HashSet<string> recorded = ["LV-ALB", "9H-WDR", "AAL2693", "ASXGS"];
        return raw.Select(a => new AircraftInfo(
            a.Callsign, a.Callsign, a.Owner, a.Distance, a.Heading, a.Altitude, a.Gs, a.Model,
            Bearing: a.Heading,
            Squawk: (1200 + a.Callsign.Length * 37 % 6600).ToString("0000", CultureInfo.InvariantCulture),
            Com1: (118 + a.Callsign.Length % 17 / 10.0).ToString("0.000", CultureInfo.InvariantCulture),
            Com2: (121 + a.Owner.Length % 9 / 10.0).ToString("0.000", CultureInfo.InvariantCulture),
            Simulator: "Microsoft Flight Simulator 2024",
            OriginalModel: ModelNames.StripVariantSuffix(a.Model),
            FlightPlan: a.Owner.Length > 0 ? $"{a.Callsign} — VFR, {Math.Round(a.Distance / 50)} nm route" : "No flight plan filed",
            Remarks: "None",
            AircraftLinkState.Created, recorded.Contains(a.Callsign), Ignored: false, Tracked: false, AircraftActions.All)).ToArray();
    }
}
