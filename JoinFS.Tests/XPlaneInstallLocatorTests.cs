namespace JoinFS.Tests;

/// <summary>
/// Exercises XPlaneInstallLocator (internal, exposed via InternalsVisibleTo) against temp
/// folders laid out like real X-Plane / Steam installs, so tests never touch the real
/// %LOCALAPPDATA% or Steam registry entries.
/// </summary>
public class XPlaneInstallLocatorTests : IDisposable
{
    readonly string tempDir;

    public XPlaneInstallLocatorTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "JoinFSTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, true);
        }
    }

    string CreateInstall(params string[] relativePath)
    {
        string root = Path.Combine([tempDir, .. relativePath]);
        Directory.CreateDirectory(Path.Combine(root, "Aircraft"));
        Directory.CreateDirectory(Path.Combine(root, "Resources", "plugins"));
        return root;
    }

    string WriteRegistry(string fileName, params string[] lines)
    {
        string registryDir = Path.Combine(tempDir, "registry");
        Directory.CreateDirectory(registryDir);
        File.WriteAllLines(Path.Combine(registryDir, fileName), lines);
        return registryDir;
    }

    [Fact]
    public void IsValidInstall_AircraftAndPluginsPresent_ReturnsTrue()
    {
        string root = CreateInstall("XP12");

        Assert.True(XPlaneInstallLocator.IsValidInstall(root));
    }

    [Fact]
    public void IsValidInstall_OnlyAircraftFolder_ReturnsFalse()
    {
        string root = Path.Combine(tempDir, "half");
        Directory.CreateDirectory(Path.Combine(root, "Aircraft"));

        Assert.False(XPlaneInstallLocator.IsValidInstall(root));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsValidInstall_BlankPath_ReturnsFalse(string? path)
    {
        Assert.False(XPlaneInstallLocator.IsValidInstall(path));
    }

    [Fact]
    public void ReadInstallRegistry_MissingFile_ReturnsEmpty()
    {
        var result = XPlaneInstallLocator.ReadInstallRegistry(Path.Combine(tempDir, "nope.txt"));

        Assert.Empty(result);
    }

    [Fact]
    public void ReadInstallRegistry_ReturnsAllNonBlankTrimmedLines()
    {
        string dir = WriteRegistry("x-plane_install_12.txt", @"  D:\XP12  ", "", @"E:\XP12-copy", "   ");

        var result = XPlaneInstallLocator.ReadInstallRegistry(Path.Combine(dir, "x-plane_install_12.txt"));

        Assert.Equal([@"D:\XP12", @"E:\XP12-copy"], result);
    }

    [Fact]
    public void ReadSteamLibraryFolders_ParsesPathEntriesAndUnescapesBackslashes()
    {
        string vdf = Path.Combine(tempDir, "libraryfolders.vdf");
        File.WriteAllText(vdf,
            "\"libraryfolders\"\r\n{\r\n" +
            "\t\"0\"\r\n\t{\r\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\r\n\t\t\"label\"\t\t\"\"\r\n\t}\r\n" +
            "\t\"1\"\r\n\t{\r\n\t\t\"path\"\t\t\"D:\\\\SteamLibrary\"\r\n\t}\r\n}\r\n");

        var result = XPlaneInstallLocator.ReadSteamLibraryFolders(vdf);

        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], result);
    }

    [Fact]
    public void ReadSteamLibraryFolders_MissingFile_ReturnsEmpty()
    {
        Assert.Empty(XPlaneInstallLocator.ReadSteamLibraryFolders(Path.Combine(tempDir, "none.vdf")));
    }

    [Fact]
    public void FindInstalls_NoRegistryNoSteam_ReturnsEmpty()
    {
        var result = XPlaneInstallLocator.FindInstalls(Path.Combine(tempDir, "empty"), null);

        Assert.Empty(result);
    }

    [Fact]
    public void FindInstalls_SkipsStalePaths()
    {
        string valid = CreateInstall("XP12");
        string registryDir = WriteRegistry("x-plane_install_12.txt", Path.Combine(tempDir, "deleted"), valid);

        var result = XPlaneInstallLocator.FindInstalls(registryDir, null);

        Assert.Single(result);
        Assert.Equal(valid, result[0].Path);
        Assert.Equal(12, result[0].Version);
    }

    [Fact]
    public void FindInstalls_ReturnsEveryValidLine_NotJustTheFirst()
    {
        string first = CreateInstall("XP12a");
        string second = CreateInstall("XP12b");
        string registryDir = WriteRegistry("x-plane_install_12.txt", first, second);

        var result = XPlaneInstallLocator.FindInstalls(registryDir, null);

        Assert.Equal([first, second], result.Select(i => i.Path));
    }

    [Fact]
    public void FindInstalls_OrdersXP12BeforeXP11()
    {
        string xp11 = CreateInstall("XP11");
        string xp12 = CreateInstall("XP12");
        WriteRegistry("x-plane_install_11.txt", xp11);
        string registryDir = WriteRegistry("x-plane_install_12.txt", xp12);

        var result = XPlaneInstallLocator.FindInstalls(registryDir, null);

        Assert.Equal([xp12, xp11], result.Select(i => i.Path));
        Assert.Equal([12, 11], result.Select(i => i.Version));
    }

    [Fact]
    public void FindInstalls_DeduplicatesSamePathDifferingInCaseAndTrailingSeparator()
    {
        string xp12 = CreateInstall("XP12");
        string registryDir = WriteRegistry("x-plane_install_12.txt",
            xp12, xp12.ToUpperInvariant() + Path.DirectorySeparatorChar);

        var result = XPlaneInstallLocator.FindInstalls(registryDir, null);

        Assert.Single(result);
    }

    [Fact]
    public void FindInstalls_FindsSteamInstallInSecondaryLibrary()
    {
        string steamRoot = Path.Combine(tempDir, "Steam");
        Directory.CreateDirectory(Path.Combine(steamRoot, "steamapps"));
        string library = Path.Combine(tempDir, "SteamLibrary");
        string xp12 = CreateInstall("SteamLibrary", "steamapps", "common", "X-Plane 12");
        File.WriteAllText(Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"),
            "\"libraryfolders\"\n{\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"" + library.Replace("\\", "\\\\") + "\"\n\t}\n}\n");

        var result = XPlaneInstallLocator.FindInstalls(Path.Combine(tempDir, "empty"), steamRoot);

        Assert.Single(result);
        Assert.Equal(xp12, result[0].Path);
        Assert.Equal(12, result[0].Version);
    }

    [Fact]
    public void FindInstalls_SteamAndRegistryPointToSameInstall_ListedOnce()
    {
        string steamRoot = Path.Combine(tempDir, "Steam");
        string xp12 = CreateInstall("Steam", "steamapps", "common", "X-Plane 12");
        string registryDir = WriteRegistry("x-plane_install_12.txt", xp12);

        var result = XPlaneInstallLocator.FindInstalls(registryDir, steamRoot);

        Assert.Single(result);
    }

    [Fact]
    public void FindInstalls_GarbageRegistryContent_DoesNotThrow()
    {
        string registryDir = Path.Combine(tempDir, "registry");
        Directory.CreateDirectory(registryDir);
        File.WriteAllBytes(Path.Combine(registryDir, "x-plane_install_12.txt"), [0xEF, 0xBB, 0xBF, 0x00, 0x01, 0x7C, 0x3F]);

        var result = XPlaneInstallLocator.FindInstalls(registryDir, null);

        Assert.Empty(result);
    }

    [Fact]
    public void FindInstalls_LineWithTrailingForwardSlash_ReturnsCleanPath()
    {
        // Laminar's installer writes e.g. "c:\X-Plane 12/" - mixed separators, trailing slash
        string xp12 = CreateInstall("X-Plane 12");
        string registryDir = WriteRegistry("x-plane_install_12.txt", xp12 + "/");

        var result = XPlaneInstallLocator.FindInstalls(registryDir, null);

        Assert.Single(result);
        Assert.Equal(xp12, result[0].Path);
    }

    [Fact]
    public void FindInstalls_LineWithForwardSlashesThroughout_UsesBackslashes()
    {
        string xp12 = CreateInstall("X-Plane 12");
        string registryDir = WriteRegistry("x-plane_install_12.txt", xp12.Replace('\\', '/') + "/");

        var result = XPlaneInstallLocator.FindInstalls(registryDir, null);

        Assert.Equal(xp12, result.Single().Path);
    }

    [Theory]
    [InlineData(@"C:\X-Plane 12\", @"C:\X-Plane 12")]
    [InlineData("C:\\X-Plane 12/", @"C:\X-Plane 12")]
    [InlineData(@"C:\X-Plane 12", @"C:\X-Plane 12")]
    [InlineData(@"C:\", @"C:\")]
    public void NormalizeFolder_RemovesTrailingSeparatorButKeepsRoot(string folder, string expected)
    {
        Assert.Equal(expected, XPlaneInstallLocator.NormalizeFolder(folder));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeFolder_Blank_ReturnsInputUnchanged(string? folder)
    {
        Assert.Equal(folder, XPlaneInstallLocator.NormalizeFolder(folder));
    }
}
