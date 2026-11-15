namespace JoinFS.Tests;

/// <summary>
/// XPlanePluginAutoInstall decides whether JoinFS installs the X-Plane plugin by itself or offers the install card.
/// </summary>
public class XPlanePluginAutoInstallTests
{
    const string Folder = @"D:\X-Plane 12";

    [Fact]
    public void Without_a_known_folder_the_card_is_offered()
    {
        XPlanePluginAutoInstall auto = new();
        Assert.Equal(XPlanePluginAutoInstall.Action.Ask, auto.Decide(null));
        Assert.Equal(XPlanePluginAutoInstall.Action.Ask, auto.Decide(" "));
    }

    [Fact]
    public void A_known_folder_is_installed_into_without_asking()
    {
        XPlanePluginAutoInstall auto = new();
        Assert.Equal(XPlanePluginAutoInstall.Action.Install, auto.Decide(Folder));
    }

    [Fact]
    public void A_folder_is_installed_into_once_while_running_and_after_success()
    {
        XPlanePluginAutoInstall auto = new();
        auto.Decide(Folder);
        // an outdated plugin keeps answering while the install runs, and after it until X-Plane restarts
        Assert.Equal(XPlanePluginAutoInstall.Action.Nothing, auto.Decide(Folder));
        auto.Done(Folder, installed: true);
        Assert.Equal(XPlanePluginAutoInstall.Action.Nothing, auto.Decide(@"d:\x-plane 12"));
    }

    [Fact]
    public void After_a_failed_install_the_card_is_offered()
    {
        XPlanePluginAutoInstall auto = new();
        auto.Decide(Folder);
        auto.Done(Folder, installed: false);
        Assert.Equal(XPlanePluginAutoInstall.Action.Ask, auto.Decide(Folder));
    }

    [Fact]
    public void Another_folder_is_installed_into_by_itself_too()
    {
        XPlanePluginAutoInstall auto = new();
        auto.Decide(Folder);
        auto.Done(Folder, installed: true);
        Assert.Equal(XPlanePluginAutoInstall.Action.Install, auto.Decide(@"D:\X-Plane 11"));
    }
}
