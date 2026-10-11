namespace JoinFS.Tests;

/// <summary>
/// XPlaneCslFolder decides whether JoinFS still has to generate its CSL packages for an
/// install: the plugin is there, but nothing besides the shipped default C172 exists.
/// </summary>
public class XPlaneCslFolderTests : IDisposable
{
    readonly string simFolder;

    public XPlaneCslFolderTests()
    {
        simFolder = Path.Combine(Path.GetTempPath(), "JoinFSTests_" + Guid.NewGuid());
        Directory.CreateDirectory(simFolder);
    }

    public void Dispose()
    {
        if (Directory.Exists(simFolder))
        {
            Directory.Delete(simFolder, true);
        }
    }

    string PluginFolder => Path.Combine(simFolder, "Resources", "plugins", "JoinFS");

    string CslFolder => Path.Combine(PluginFolder, "Resources", "CSL");

    void AddPackage(string name, bool withModelFile = true)
    {
        string package = Path.Combine(CslFolder, name);
        Directory.CreateDirectory(package);
        if (withModelFile)
        {
            File.WriteAllText(Path.Combine(package, "xsb_aircraft.txt"), "EXPORT_NAME " + name);
        }
    }

    [Fact]
    public void IsPluginInstalled_NoPluginFolder_ReturnsFalse()
    {
        Assert.False(XPlaneCslFolder.IsPluginInstalled(simFolder));
    }

    [Fact]
    public void IsPluginInstalled_PluginBinariesPresent_ReturnsTrue()
    {
        Directory.CreateDirectory(Path.Combine(PluginFolder, "64"));

        Assert.True(XPlaneCslFolder.IsPluginInstalled(simFolder));
    }

    [Fact]
    public void NeedsGeneration_PluginNotInstalled_ReturnsFalse()
    {
        // installing the plugin comes first and triggers its own generating scan
        Assert.False(XPlaneCslFolder.NeedsGeneration(simFolder));
    }

    [Fact]
    public void NeedsGeneration_CslFolderMissing_ReturnsTrue()
    {
        Directory.CreateDirectory(Path.Combine(PluginFolder, "64"));

        Assert.True(XPlaneCslFolder.NeedsGeneration(simFolder));
    }

    [Fact]
    public void NeedsGeneration_OnlyShippedDefaultPackage_ReturnsTrue()
    {
        Directory.CreateDirectory(Path.Combine(PluginFolder, "64"));
        AddPackage("BB_GA");

        Assert.True(XPlaneCslFolder.NeedsGeneration(simFolder));
    }

    [Fact]
    public void NeedsGeneration_AnotherPackageWithModelFile_ReturnsFalse()
    {
        Directory.CreateDirectory(Path.Combine(PluginFolder, "64"));
        AddPackage("BB_GA");
        AddPackage("C172");

        Assert.False(XPlaneCslFolder.NeedsGeneration(simFolder));
    }

    [Fact]
    public void NeedsGeneration_OtherFolderWithoutModelFile_ReturnsTrue()
    {
        Directory.CreateDirectory(Path.Combine(PluginFolder, "64"));
        AddPackage("BB_GA");
        AddPackage("Leftover", withModelFile: false);

        Assert.True(XPlaneCslFolder.NeedsGeneration(simFolder));
    }

    [Theory]
    [InlineData(@"C:\X-Plane 12", @"C:\X-Plane 12\Aircraft\Laminar Research\C172\c172.acf")]
    [InlineData(@"C:\X-Plane 12\", @"C:\X-Plane 12\Aircraft\Laminar Research\C172\c172.acf")]
    [InlineData("C:\\X-Plane 12/", "C:\\X-Plane 12/Aircraft\\Laminar Research\\C172\\c172.acf")]
    [InlineData("c:\\x-plane 12", "C:\\X-Plane 12\\Aircraft\\Laminar Research\\C172\\c172.acf")]
    public void AircraftFolder_IsRelativeToTheInstallRegardlessOfSeparatorsAndCase(string install, string acf)
    {
        Assert.Equal(@"Aircraft\Laminar Research\C172", XPlaneCslFolder.AircraftFolder(install, acf));
    }

    [Fact]
    public void AircraftFolder_AcfOutsideTheInstall_ReturnsNull()
    {
        Assert.Null(XPlaneCslFolder.AircraftFolder(@"C:\X-Plane 12", @"D:\Other\Aircraft\C172\c172.acf"));
    }

    [Fact]
    public void AircraftFolder_AcfDirectlyInInstallRoot_ReturnsNull()
    {
        Assert.Null(XPlaneCslFolder.AircraftFolder(@"C:\X-Plane 12", @"C:\X-Plane 12\c172.acf"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NeedsGeneration_BlankFolder_ReturnsFalse(string? folder)
    {
        Assert.False(XPlaneCslFolder.NeedsGeneration(folder));
    }
}
