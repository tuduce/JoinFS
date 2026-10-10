using JoinFS.Matching;

namespace JoinFS.Tests;

/// <summary>
/// XPlaneLiveryPlanner turns an aircraft folder's liveries into CSL model blocks. It only ever
/// reads the aircraft folder; these tests build fixtures that look like an X-Plane aircraft.
/// </summary>
public class XPlaneLiveryPlannerTests : IDisposable
{
    readonly string aircraft;

    public XPlaneLiveryPlannerTests()
    {
        aircraft = Path.Combine(Path.GetTempPath(), "JoinFSTests_" + Guid.NewGuid(), "Aircraft", "Vendor", "A320");
        Directory.CreateDirectory(Path.Combine(aircraft, "objects"));
    }

    public void Dispose()
    {
        string root = Path.GetFullPath(Path.Combine(aircraft, "..", "..", ".."));
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }

    void Write(string relativePath, string content = "x")
    {
        string path = Path.Combine(aircraft, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    static AirlineResolver Airlines() => AirlineResolver.FromLines(
    [
        "DLH\tLH\tLufthansa",
        "KLM\tKL\tKLM Royal Dutch Airlines",
        "RED\t\tRed Wings",
        "AAA\t\tAlpha Air",
        "AAB\t\tAlpha Airways",
    ]);

    // ---- enumeration -------------------------------------------------------------------------

    [Fact]
    public void EnumerateLiveries_NoLiveriesFolder_ReturnsEmpty()
    {
        Assert.Empty(XPlaneLiveryPlanner.EnumerateLiveries(aircraft));
    }

    [Fact]
    public void EnumerateLiveries_ReturnsFolderNamesSorted_IgnoresFiles()
    {
        Write(@"liveries\Lufthansa\a.txt");
        Write(@"liveries\Delta\a.txt");
        Write(@"liveries\readme.txt");

        Assert.Equal(["Delta", "Lufthansa"], XPlaneLiveryPlanner.EnumerateLiveries(aircraft));
    }

    // ---- ids ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("Delta Air Lines 2019", "Delta_Air_Lines_2019")]
    [InlineData("KLM (PH-BXA)", "KLM_PHBXA")]
    [InlineData("!!!", "livery")]
    public void UniqueId_SanitizesLikeCslNames(string name, string expected)
    {
        Assert.Equal(expected, XPlaneLiveryPlanner.UniqueId(name, new HashSet<string>()));
    }

    [Fact]
    public void UniqueId_CollidingNames_GetNumericSuffix_AndAreRemembered()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // all three sanitize to "KLM1" (case-insensitively): the CSL id must still be unique
        string first = XPlaneLiveryPlanner.UniqueId("KLM-1", used);
        string second = XPlaneLiveryPlanner.UniqueId("klm1", used);
        string third = XPlaneLiveryPlanner.UniqueId("KLM1", used);

        Assert.Equal("KLM1", first);
        Assert.Equal("klm1_2", second);
        Assert.Equal("KLM1_3", third);
    }

    // ---- .obj texture parsing ----------------------------------------------------------------

    [Fact]
    public void ReadTextures_ReturnsTextureAndLitTexture()
    {
        Write(@"objects\fuselage.obj", "I\n800\nOBJ\n\nTEXTURE tex_a.png\nTEXTURE_LIT tex_a_LIT.png\nPOINT_COUNTS 1 0 0 0\n");

        var (texture, lit) = XPlaneLiveryPlanner.ReadTextures(Path.Combine(aircraft, "objects", "fuselage.obj"));

        Assert.Equal("tex_a.png", texture);
        Assert.Equal("tex_a_LIT.png", lit);
    }

    [Fact]
    public void ReadTextures_NoneOrMissing_ReturnsNull()
    {
        Write(@"objects\x.obj", "I\n800\nOBJ\nTEXTURE_LIT none\n");

        var (texture, lit) = XPlaneLiveryPlanner.ReadTextures(Path.Combine(aircraft, "objects", "x.obj"));

        Assert.Null(texture);
        Assert.Null(lit);
    }

    // ---- plan --------------------------------------------------------------------------------

    [Fact]
    public void Plan_LiveryOverridesTexture_MapsSourceAndUniqueTarget()
    {
        Write(@"objects\fuselage.obj", "TEXTURE tex_a.png\nTEXTURE_LIT tex_a_LIT.png\n");
        Write(@"liveries\Lufthansa\objects\tex_a.png", "livery");

        var plan = XPlaneLiveryPlanner.Plan(aircraft, "Lufthansa", "Lufthansa");

        var obj = Assert.Single(plan.Objects);
        Assert.Equal("fuselage.obj", obj.RelativePath);
        Assert.Equal("Lufthansa_tex_a.png", obj.Texture);
        Assert.Equal(Path.Combine(aircraft, "liveries", "Lufthansa", "objects", "tex_a.png"), obj.TextureSource);
        Assert.Null(obj.Lit);
    }

    [Fact]
    public void Plan_OnlyLitOverridden_KeepsBaseTextureAs4thParameter()
    {
        Write(@"objects\fuselage.obj", "TEXTURE tex_a.png\nTEXTURE_LIT tex_a_LIT.png\n");
        Write(@"liveries\Night\objects\tex_a_LIT.png");

        var obj = Assert.Single(XPlaneLiveryPlanner.Plan(aircraft, "Night", "Night").Objects);

        Assert.Equal("tex_a.png", obj.Texture);
        Assert.Null(obj.TextureSource);
        Assert.Equal("Night_tex_a_LIT.png", obj.Lit);
    }

    [Fact]
    public void Plan_NoOverride_ObjectIsListedWithoutTextureParameters()
    {
        Write(@"objects\tailfin.obj", "TEXTURE tex_b.png\n");
        Write(@"liveries\Plain\placeholder.txt");

        var obj = Assert.Single(XPlaneLiveryPlanner.Plan(aircraft, "Plain", "Plain").Objects);

        Assert.Null(obj.Texture);
        Assert.Null(obj.Lit);
    }

    [Fact]
    public void Plan_TextureInSubFolder_KeepsSubFolderForTarget()
    {
        Write(@"objects\fuselage.obj", "TEXTURE textures/tex_a.png\n");
        Write(@"liveries\KLM\objects\textures\tex_a.png");

        var obj = Assert.Single(XPlaneLiveryPlanner.Plan(aircraft, "KLM", "KLM").Objects);

        Assert.Equal("textures/KLM_tex_a.png", obj.Texture);
    }

    [Fact]
    public void Plan_PilotGlassAndGearObjects_AreSkippedLikeTheDefaultModel()
    {
        Write(@"objects\fuselage.obj", "TEXTURE a.png\n");
        Write(@"objects\pilot.obj", "TEXTURE a.png\n");
        Write(@"objects\cockpit_glass.obj", "TEXTURE a.png\n");
        Write(@"objects\gear_main.obj", "TEXTURE a.png\n");

        var plan = XPlaneLiveryPlanner.Plan(aircraft, "Any", "Any");

        Assert.Equal(["fuselage.obj"], plan.Objects.Select(o => o.RelativePath));
    }

    [Fact]
    public void Plan_NeverWritesIntoTheAircraftFolder()
    {
        Write(@"objects\fuselage.obj", "TEXTURE a.png\n");
        Write(@"liveries\L\objects\a.png");
        string Snapshot() => string.Join("|", Directory.EnumerateFileSystemEntries(aircraft, "*", SearchOption.AllDirectories)
            .Order().Select(p => p + ":" + (File.Exists(p) ? File.GetLastWriteTimeUtc(p).Ticks : 0)));
        string before = Snapshot();

        XPlaneLiveryPlanner.EnumerateLiveries(aircraft);
        XPlaneLiveryPlanner.Plan(aircraft, "L", "L");

        Assert.Equal(before, Snapshot());
    }

    // ---- airline -----------------------------------------------------------------------------

    [Theory]
    [InlineData("Lufthansa", "DLH")]
    [InlineData("DLH", "DLH")]
    [InlineData("DLH_D-AIZZ", "DLH")]
    [InlineData("KLM Royal Dutch Airlines", "KLM")]
    [InlineData("Lufthansa 2019 D-AIZZ", "DLH")]
    public void ResolveAirline_KnownAirline_ReturnsIcao(string liveryName, string expected)
    {
        Assert.Equal(expected, XPlaneLiveryPlanner.ResolveAirline(liveryName, Airlines()));
    }

    [Theory]
    [InlineData("Red")]          // three letters that happen to equal an ICAO code
    [InlineData("Default")]
    [InlineData("Sunset")]
    [InlineData("")]
    public void ResolveAirline_NotAnAirline_ReturnsNull(string liveryName)
    {
        Assert.Null(XPlaneLiveryPlanner.ResolveAirline(liveryName, Airlines()));
    }

    [Fact]
    public void ResolveAirline_AmbiguousName_ReturnsNull()
    {
        // "Alpha" is the name of two airlines: no certain answer
        Assert.Null(XPlaneLiveryPlanner.ResolveAirline("Alpha", Airlines()));
    }

    // ---- xsb block ---------------------------------------------------------------------------

    [Fact]
    public void BuildBlock_WithAirline_WritesOperatorAndTypeQualifiedLivery()
    {
        Write(@"objects\fuselage.obj", "TEXTURE tex_a.png\n");
        Write(@"liveries\Lufthansa\objects\tex_a.png");
        var plan = XPlaneLiveryPlanner.Plan(aircraft, "Lufthansa", "Lufthansa");

        var lines = XPlaneLiveryPlanner.BuildBlock("A320", "default", "A320", "DLH", plan);

        Assert.Equal(
        [
            "",
            "OBJ8_AIRCRAFT A320_Lufthansa",
            "OBJ8 SOLID YES A320/default/fuselage.obj Lufthansa_tex_a.png",
            "LIVERY A320 DLH A320_Lufthansa",
        ], lines);
    }

    [Fact]
    public void BuildBlock_WithoutAirline_UsesTheJoinFsMarker()
    {
        Write(@"objects\fuselage.obj", "TEXTURE tex_a.png\n");
        var plan = XPlaneLiveryPlanner.Plan(aircraft, "Sunset", "Sunset");

        var lines = XPlaneLiveryPlanner.BuildBlock("A320", "default", "A320", null, plan);

        Assert.Equal("LIVERY A320 JFS A320_Sunset", lines[^1]);
    }

    [Fact]
    public void BuildBlock_JfsMarkerYieldsNoAirline_InTheExistingXsbParser()
    {
        // pins the contract the "no airline" choice relies on
        var (type, airline) = XsbEntry.Identity("LIVERY", ["LIVERY", "A320", "JFS", "A320_Sunset"]);

        Assert.Equal("A320", type);
        Assert.Equal("", airline);
    }

    [Fact]
    public void BuildBlock_LitOnlyOverride_WritesBaseTextureThenLit()
    {
        Write(@"objects\fuselage.obj", "TEXTURE tex_a.png\nTEXTURE_LIT tex_a_LIT.png\n");
        Write(@"liveries\Night\objects\tex_a_LIT.png");
        var plan = XPlaneLiveryPlanner.Plan(aircraft, "Night", "Night");

        var lines = XPlaneLiveryPlanner.BuildBlock("A320", "default", "A320", null, plan);

        Assert.Contains("OBJ8 SOLID YES A320/default/fuselage.obj tex_a.png Night_tex_a_LIT.png", lines);
    }

    [Fact]
    public void ReadIcaoType_FromAcf_CutsToFourCharacters_DefaultsToC172()
    {
        Write("a.acf", "P acf/_ICAO A320NEO\n");
        Write("b.acf", "P acf/_name Foo\n");

        Assert.Equal("A320", XPlaneLiveryPlanner.ReadIcaoType(Path.Combine(aircraft, "a.acf")));
        Assert.Equal("C172", XPlaneLiveryPlanner.ReadIcaoType(Path.Combine(aircraft, "b.acf")));
    }
}
