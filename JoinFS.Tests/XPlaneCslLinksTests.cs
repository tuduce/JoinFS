namespace JoinFS.Tests;

/// <summary>
/// XPlaneCslLinks keeps junctions to installed CSL packs inside JoinFS's own CSL folder in step with
/// what is enabled. Real junctions in a temp tree; the target contents must never be touched.
/// </summary>
public class XPlaneCslLinksTests : IDisposable
{
    readonly string root;

    public XPlaneCslLinksTests()
    {
        root = Path.Combine(Path.GetTempPath(), "JoinFSTests_" + Guid.NewGuid());
        Directory.CreateDirectory(CslFolder);
    }

    string CslFolder => Path.Combine(root, "Resources", "plugins", "JoinFS", "Resources", "CSL");

    public void Dispose()
    {
        // links first, so deleting the tree cannot reach into a link target
        if (Directory.Exists(CslFolder))
        {
            foreach (string dir in Directory.GetDirectories(CslFolder))
            {
                if (new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    Directory.Delete(dir, false);
                }
            }
        }
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }

    XPlaneCslSource MakePack(string name, string packageFolder = "A320")
    {
        string path = Path.Combine(root, "Resources", "plugins", name);
        string package = Path.Combine(path, packageFolder);
        Directory.CreateDirectory(package);
        File.WriteAllText(Path.Combine(package, "xsb_aircraft.txt"), "EXPORT_NAME " + name);
        return new XPlaneCslSource(name, path, 1);
    }

    string LinkPath(XPlaneCslSource source) => Path.Combine(CslFolder, XPlaneCslLinks.LinkName(source.Name));

    static bool IsLink(string path) => new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint);

    [Fact]
    public void Sync_CreatesAJunctionThePackagesCanBeReadThrough()
    {
        var pack = MakePack("X-CSL");

        int linked = XPlaneCslLinks.Sync(root, [pack], _ => { });

        Assert.Equal(1, linked);
        Assert.True(IsLink(LinkPath(pack)));
        Assert.True(File.Exists(Path.Combine(LinkPath(pack), "A320", "xsb_aircraft.txt")));
    }

    [Fact]
    public void Sync_Twice_KeepsTheSameLink()
    {
        var pack = MakePack("X-CSL");
        XPlaneCslLinks.Sync(root, [pack], _ => { });
        DateTime created = Directory.GetCreationTimeUtc(LinkPath(pack));

        int linked = XPlaneCslLinks.Sync(root, [pack], _ => { });

        Assert.Equal(1, linked);
        Assert.Equal(created, Directory.GetCreationTimeUtc(LinkPath(pack)));
    }

    [Fact]
    public void Sync_DisabledPack_RemovesOnlyTheLink_NotTheTargetContents()
    {
        var pack = MakePack("X-CSL");
        XPlaneCslLinks.Sync(root, [pack], _ => { });

        int linked = XPlaneCslLinks.Sync(root, [], _ => { });

        Assert.Equal(0, linked);
        Assert.False(Directory.Exists(LinkPath(pack)));
        Assert.True(File.Exists(Path.Combine(pack.Path, "A320", "xsb_aircraft.txt")));
    }

    [Fact]
    public void Sync_LeavesGeneratedPackagesAndForeignLinksAlone()
    {
        var pack = MakePack("X-CSL");
        string generated = Path.Combine(CslFolder, "Cessna_172_SP");
        Directory.CreateDirectory(generated);
        File.WriteAllText(Path.Combine(generated, "xsb_aircraft.txt"), "x");
        // a real folder that merely looks like one of ours
        string lookalike = Path.Combine(CslFolder, "__linked_manual");
        Directory.CreateDirectory(lookalike);

        XPlaneCslLinks.Sync(root, [pack], _ => { });
        XPlaneCslLinks.Sync(root, [], _ => { });

        Assert.True(File.Exists(Path.Combine(generated, "xsb_aircraft.txt")));
        Assert.True(Directory.Exists(lookalike));
    }

    [Fact]
    public void Sync_PluginNotInstalled_DoesNothing()
    {
        string other = Path.Combine(Path.GetTempPath(), "JoinFSTests_" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(other, "Resources", "plugins"));
        try
        {
            var pack = new XPlaneCslSource("X-CSL", Path.Combine(other, "Resources", "plugins", "X-CSL"), 1);

            Assert.Equal(0, XPlaneCslLinks.Sync(other, [pack], _ => { }));
            Assert.False(Directory.Exists(Path.Combine(other, "Resources", "plugins", "JoinFS")));
        }
        finally
        {
            Directory.Delete(other, true);
        }
    }

    [Fact]
    public void Sync_RemovesTheLinkOfAPackThatNoLongerExists()
    {
        var pack = MakePack("Old-CSL");
        XPlaneCslLinks.Sync(root, [pack], _ => { });
        Directory.Delete(pack.Path, true);

        int linked = XPlaneCslLinks.Sync(root, [], _ => { });

        Assert.Equal(0, linked);
        Assert.False(Directory.Exists(LinkPath(pack)));
    }

    [Fact]
    public void IsLink_RealFolderIsFalse_JunctionIsTrue()
    {
        var pack = MakePack("X-CSL");
        XPlaneCslLinks.Sync(root, [pack], _ => { });

        Assert.True(XPlaneCslLinks.IsLink(LinkPath(pack)));
        Assert.False(XPlaneCslLinks.IsLink(pack.Path));
    }
}
