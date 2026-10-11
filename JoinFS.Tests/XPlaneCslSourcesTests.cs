namespace JoinFS.Tests;

/// <summary>
/// XPlaneCslSources finds installed CSL packs below Resources\plugins (X-CSL, Bluebell, IVAO_CSL ...)
/// that JoinFS can link into its own CSL folder. Fixtures are temp trees laid out like an X-Plane install.
/// </summary>
public class XPlaneCslSourcesTests : IDisposable
{
    readonly string root;

    public XPlaneCslSourcesTests()
    {
        root = Path.Combine(Path.GetTempPath(), "JoinFSTests_" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(root, "Resources", "plugins"));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }

    void Package(params string[] relativeToPlugins)
    {
        string dir = Path.Combine([root, "Resources", "plugins", .. relativeToPlugins]);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "xsb_aircraft.txt"), "EXPORT_NAME x");
    }

    [Fact]
    public void Discover_NoPluginsFolder_ReturnsEmpty()
    {
        Assert.Empty(XPlaneCslSources.Discover(Path.Combine(root, "nowhere")));
    }

    [Fact]
    public void Discover_PackTreeWithSeveralPackages_IsOneSourceAtTheirCommonParent()
    {
        Package("X-CSL", "A320_Lufthansa");
        Package("X-CSL", "B738_Ryanair");

        var sources = XPlaneCslSources.Discover(root);

        var source = Assert.Single(sources);
        Assert.Equal("X-CSL", source.Name);
        Assert.Equal(Path.Combine(root, "Resources", "plugins", "X-CSL"), source.Path);
        Assert.Equal(2, source.Packages);
    }

    [Fact]
    public void Discover_PacksNestedInAContainer_UsesTheFolderThatHoldsThePackages()
    {
        // IVAO_CSL keeps data files next to a CSL sub-folder
        Package("IVAO_CSL", "CSL", "B738");
        Package("IVAO_CSL", "CSL", "A320");
        File.WriteAllText(Path.Combine(root, "Resources", "plugins", "IVAO_CSL", "Doc8643.txt"), "");

        var source = Assert.Single(XPlaneCslSources.Discover(root));

        Assert.Equal(@"IVAO_CSL\CSL", source.Name);
        Assert.Equal(Path.Combine(root, "Resources", "plugins", "IVAO_CSL", "CSL"), source.Path);
    }

    [Fact]
    public void Discover_SinglePackage_IsThePackageFolderItself()
    {
        Package("Bluebell", "BB_Heli");

        var source = Assert.Single(XPlaneCslSources.Discover(root));

        Assert.Equal(@"Bluebell\BB_Heli", source.Name);
        Assert.Equal(1, source.Packages);
    }

    [Fact]
    public void Discover_JoinFsOwnFolderIsExcluded()
    {
        Package("JoinFS", "Resources", "CSL", "Cessna_172_SP");
        Package("X-CSL", "A320");

        var sources = XPlaneCslSources.Discover(root);

        Assert.Equal(["X-CSL"], sources.Select(s => s.Name).Select(n => n.Split('\\')[0]));
    }

    [Fact]
    public void Discover_SeveralPacksAreListedSortedByName()
    {
        Package("X-CSL", "A");
        Package("X-CSL", "B");
        Package("IVAO_CSL", "CSL", "B738");
        Package("IVAO_CSL", "CSL", "A320");

        var names = XPlaneCslSources.Discover(root).Select(s => s.Name).ToList();

        Assert.Equal([@"IVAO_CSL\CSL", "X-CSL"], names);
    }

    [Fact]
    public void Discover_PackagesTooDeepForXpmp2_AreNotOffered()
    {
        // XPMP2 searches 5 levels below JoinFS\Resources: CSL / link / x / a / b is level 5, the package
        // folder itself at level 6 is not found. The common parent of both is "Deep".
        Package("Deep", "x", "a", "b", "p1");
        Package("Deep", "y", "a", "b", "p2");

        Assert.Empty(XPlaneCslSources.Discover(root));
    }

    [Fact]
    public void Discover_OnlyTheReachablePackagesAreCounted()
    {
        Package("Deep", "x", "a", "b", "too_deep");
        Package("Deep", "near");

        var source = Assert.Single(XPlaneCslSources.Discover(root));

        Assert.Equal(1, source.Packages);
    }

    [Fact]
    public void Discover_LongPathOfASinglePackage_IsFineBecauseTheLinkIsThePackage()
    {
        Package("Deep", "a", "b", "c", "pkg");

        Assert.Single(XPlaneCslSources.Discover(root));
    }

    [Theory]
    [InlineData(@"IVAO_CSL\CSL", "__linked_IVAO_CSL_CSL")]
    [InlineData("X-CSL", "__linked_X-CSL")]
    [InlineData("My Pack (2024)", "__linked_My_Pack_2024")]
    public void LinkName_IsPrefixedAndFileSystemSafe(string name, string expected)
    {
        Assert.Equal(expected, XPlaneCslLinks.LinkName(name));
    }
}
