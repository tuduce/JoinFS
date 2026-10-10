using JoinFS.UI.Models;
using JoinFS.UI.Services;
using JoinFS.UI.Services.Fake;
using JoinFS.UI.ViewModels;
using JoinFS.UI.ViewModels.Overlays;

namespace JoinFS.UI.Tests;

public class ScanTests
{
    private static FakeModelScanSource Source(Rig rig) => (FakeModelScanSource)rig.Services.ModelScan;

    private static ScanModelsViewModel Open(Rig rig, bool connected = true) => new(rig.Services.ModelScan, connected, rig.Platform);

    [Fact]
    public void The_dialog_starts_on_where_the_last_scan_looked()
    {
        Rig rig = new();

        ScanModelsViewModel scan = Open(rig);

        Assert.Equal("Microsoft Flight Simulator 2020", scan.SimulatorName);
        Assert.Equal(@"C:\Flight Simulator Packages", scan.RootFolder);
        Assert.Equal(["Airplanes", "Boats", "Rotorcraft"], scan.Subfolders.Select(f => f.Name));
        Assert.Equal(["Airplanes"], scan.Subfolders.Where(f => f.IsChecked).Select(f => f.Name));
        Assert.Equal(["Aerosoft CRJ", "Fenix A320"], scan.AddOns.Select(a => a.Name));
        Assert.Equal(["Aerosoft CRJ"], scan.AddOns.Where(a => a.IsChecked).Select(a => a.Name));
        Assert.Equal(@"D:\Community", scan.OtherFoldersText);
        Assert.Contains("Packages", scan.FolderPrompt);
    }

    [Fact]
    public void Scan_hands_the_choices_to_the_scan_and_closes()
    {
        Rig rig = new();
        ScanModelsViewModel scan = Open(rig);
        scan.RootFolder = @"E:\Sim";
        scan.Subfolders.Single(f => f.Name == "Boats").IsChecked = true;
        scan.AddOns.Single(a => a.Name == "Fenix A320").IsChecked = true;
        scan.OtherFoldersText = "D:\\Community\r\n\r\n  F:\\More  \r\n";

        scan.ScanCommand.Execute(null);

        (string folder, IReadOnlyList<string> subfolders, IReadOnlyList<string> addOns, IReadOnlyList<string> others) = Assert.Single(Source(rig).Scans);
        Assert.Equal(@"E:\Sim", folder);
        Assert.Equal(["Airplanes", "Boats"], subfolders);
        Assert.Equal(["Aerosoft CRJ", "Fenix A320"], addOns);
        Assert.Equal([@"D:\Community", @"F:\More"], others);
    }

    [Fact]
    public void Scan_closes_the_overlay_it_was_opened_in()
    {
        Rig rig = new();
        ScanModelsViewModel scan = Open(rig);
        rig.Main.ShowOverlay(scan);

        scan.ScanCommand.Execute(null);

        Assert.Null(rig.Main.Overlay);
    }

    [Fact]
    public void Without_a_connected_simulator_there_is_nothing_to_scan()
    {
        Rig rig = new();
        ScanModelsViewModel scan = Open(rig, connected: false);

        Assert.False(scan.ScanCommand.CanExecute(null));
        Assert.Equal("Not connected", scan.SimulatorLabel);
    }

    [Fact]
    public void A_scan_that_is_already_running_is_said_so_and_the_dialog_stays()
    {
        Rig rig = new();
        Source(rig).Busy = true;
        ScanModelsViewModel scan = Open(rig);
        rig.Main.ShowOverlay(scan);

        scan.ScanCommand.Execute(null);

        Assert.Same(scan, rig.Main.Overlay);
        Assert.Equal("A scan is already running.", scan.Status);
    }

    [Fact]
    public void A_simulator_without_subfolders_scans_none_even_if_some_were_remembered()
    {
        Rig rig = new();
        Source(rig).ListsSubfolders = false;
        ScanModelsViewModel scan = Open(rig);

        scan.ScanCommand.Execute(null);

        Assert.False(scan.ShowSubfolders);
        Assert.Empty(scan.Subfolders);
        Assert.Empty(Source(rig).Scans.Single().Subfolders);
    }

    [Fact]
    public void Ticks_survive_a_change_of_folder()
    {
        Rig rig = new();
        ScanModelsViewModel scan = Open(rig);
        scan.Subfolders.Single(f => f.Name == "Rotorcraft").IsChecked = true;

        scan.RootFolder = @"D:\Other";

        Assert.Equal(["Airplanes", "Rotorcraft"], scan.Subfolders.Where(f => f.IsChecked).Select(f => f.Name));
    }

    [Fact]
    public void An_empty_folder_lists_no_subfolders()
    {
        Rig rig = new();
        ScanModelsViewModel scan = Open(rig);

        scan.RootFolder = "";

        Assert.Empty(scan.Subfolders);
        Assert.True(scan.NoSubfolders);
    }

    [Fact]
    public async Task Browse_takes_the_folder_that_was_picked()
    {
        Rig rig = new();
        ScanModelsViewModel scan = new(rig.Services.ModelScan, true, new FolderPlatform(@"G:\Picked"));

        await scan.BrowseCommand.ExecuteAsync(null);

        Assert.Equal(@"G:\Picked", scan.RootFolder);
    }

    // ---- X-Plane

    [Fact]
    public void The_x_plane_scan_hands_over_the_ticked_aircraft_folders()
    {
        Rig rig = new(xplaneBuild: true);
        rig.Main.Settings.Models.OpenModelScanningCommand.Execute(null);
        ScanXPlaneModelsViewModel scan = (ScanXPlaneModelsViewModel)rig.Main.Overlay!;
        scan.AircraftFolders.Single(f => f.Name == "FlyJSim").IsChecked = true;

        scan.ScanCommand.Execute(null);

        FakeXPlaneScanSource source = (FakeXPlaneScanSource)rig.Services.XPlaneScan;
        (string folder, IReadOnlyList<string> aircraft) = Assert.Single(source.Scans);
        Assert.Equal(@"C:\X-Plane 12", folder);
        Assert.Equal(["FlyJSim", "Laminar Research"], aircraft);
        Assert.Null(rig.Main.Overlay);
    }

    [Fact]
    public void An_x_plane_scan_that_is_already_running_is_said_so()
    {
        Rig rig = new(xplaneBuild: true);
        ((FakeXPlaneScanSource)rig.Services.XPlaneScan).Busy = true;
        rig.Main.Settings.Models.OpenModelScanningCommand.Execute(null);
        ScanXPlaneModelsViewModel scan = (ScanXPlaneModelsViewModel)rig.Main.Overlay!;

        scan.ScanCommand.Execute(null);

        Assert.Same(scan, rig.Main.Overlay);
        Assert.Equal("A scan is already running.", scan.Status);
    }

    // ---- the status on the Model Matching tab

    [Fact]
    public void The_model_matching_tab_shows_how_the_models_stand()
    {
        StatusCatalog catalog = new(new FakeModelCatalog()) { Status = "1,200 models known." };
        AppServices services = FakeServices.Create(TimeSpan.Zero, new UserSettings { Onboarded = true, Nickname = "Me" }) with { Models = catalog };
        MainViewModel main = new(services);
        Assert.Equal("1,200 models known.", main.ModelMatching.ScanStatus);

        catalog.Status = "Scanning for models...";
        main.ModelMatching.Refresh();

        Assert.Equal("Scanning for models...", main.ModelMatching.ScanStatus);
    }
}

/// <summary>A platform that answers the folder dialog.</summary>
internal sealed class FolderPlatform(string folder) : IPlatform
{
    public Task CopyTextAsync(string text) => Task.CompletedTask;
    public void OpenUrl(string url) { }
    public void PlayChime() { }
    public Task OpenFileAsync(string path) => Task.CompletedTask;
    public Task<string?> PickOpenFileAsync(string title, string? startFolder = null, string? extension = null) => Task.FromResult<string?>(null);
    public Task<string?> PickSaveFileAsync(string title, string suggestedName, string? startFolder = null, string? extension = null) => Task.FromResult<string?>(null);
    public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(folder);
}

/// <summary>A model catalog whose status line a test decides, on top of another.</summary>
internal sealed class StatusCatalog(IModelCatalog inner) : IModelCatalog
{
    public string Status { get; set; } = "";

    public string ScanStatus => Status;
    public IReadOnlyList<ModelRule> GetRules() => inner.GetRules();
    public bool HasModels => inner.HasModels;
    public IReadOnlyList<string> GetTypes(string filter) => inner.GetTypes(filter);
    public IReadOnlyList<string> GetVariations(string type) => inner.GetVariations(type);
    public string GetReplacement(string type, string variation) => inner.GetReplacement(type, variation);
    public string GetTitle(string type, string variation) => inner.GetTitle(type, variation);
    public ModelChoice? FindChoice(string title) => inner.FindChoice(title);
    public Task<ModelChoice?> GetCurrentAsync(ModelTarget target) => inner.GetCurrentAsync(target);
    public void SetSubstitute(ModelTarget target, string type, string variation) => inner.SetSubstitute(target, type, variation);
    public void ClearSubstitute(ModelTarget target) => inner.ClearSubstitute(target);
    public string? KnownModelsFile() => inner.KnownModelsFile();
    public void WriteDebugBundle(string zipPath, string report) => inner.WriteDebugBundle(zipPath, report);
}
