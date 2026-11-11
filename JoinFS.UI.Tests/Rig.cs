using JoinFS.UI.Models;
using JoinFS.UI.Services;
using JoinFS.UI.Services.Fake;
using JoinFS.UI.ViewModels;

namespace JoinFS.UI.Tests;

/// <summary>A shell on fake services with zero latency, so a connect finishes the moment it is awaited.</summary>
public sealed class Rig
{
    /// <param name="simulatorFound">False for a simulator whose folder JoinFS did not find by itself.</param>
    public Rig(bool onboarded = true, UserSettings? settings = null, bool xplaneBuild = false, bool simulatorFound = true)
    {
        Settings = settings ?? new UserSettings { Onboarded = onboarded, Nickname = onboarded ? "HB-TDX" : "" };
        Platform = new NullPlatform();
        Services = FakeServices.Create(TimeSpan.Zero, Settings, Platform, xplaneBuild);
        if (!simulatorFound)
        {
            ((FakeModelScanSource)Services.ModelScan).SimFolder = "";
            ((FakeXPlaneScanSource)Services.XPlaneScan).SimFolder = "";
        }
        Main = new MainViewModel(Services);
    }

    public UserSettings Settings { get; }
    public NullPlatform Platform { get; }
    public AppServices Services { get; }
    public MainViewModel Main { get; }

    /// <summary>A rig whose hub directory has been loaded.</summary>
    public static Task<Rig> WithHubsAsync() => Task.FromResult(new Rig());
}
