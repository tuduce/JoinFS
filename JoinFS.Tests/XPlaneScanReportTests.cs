namespace JoinFS.Tests;

/// <summary>
/// XPlaneScanReport explains a scan result: counts from each step of the X-Plane scan and, when no
/// model came out, which step lost them - so "No models found" is never a bare message.
/// </summary>
public class XPlaneScanReportTests
{
    static XPlaneScanReport Report(bool cslFolder = true, int aircraft = 5, int xsb = 5, int entries = 5, int banned = 0, int errors = 0, int models = 5)
        => new(cslFolder, aircraft, xsb, entries, banned, errors, models);

    [Fact]
    public void Outcome_ModelsPublished_IsModels()
    {
        Assert.Equal(XPlaneScanOutcome.Models, Report().Outcome);
    }

    [Fact]
    public void Outcome_NoCslFolder_IsNoCslFolder()
    {
        Assert.Equal(XPlaneScanOutcome.NoCslFolder, Report(cslFolder: false, xsb: 0, entries: 0, models: 0).Outcome);
    }

    [Fact]
    public void Outcome_CslFolderWithoutXsbFiles_IsNoXsbFiles()
    {
        Assert.Equal(XPlaneScanOutcome.NoXsbFiles, Report(xsb: 0, entries: 0, models: 0).Outcome);
    }

    [Fact]
    public void Outcome_XsbFilesButNoEntries_IsNoEntries()
    {
        Assert.Equal(XPlaneScanOutcome.NoEntries, Report(entries: 0, models: 0).Outcome);
    }

    [Fact]
    public void Outcome_XsbFilesUnreadable_IsReadErrors()
    {
        Assert.Equal(XPlaneScanOutcome.ReadErrors, Report(entries: 0, errors: 3, models: 0).Outcome);
    }

    [Fact]
    public void Outcome_EveryEntryBanned_IsAllBanned()
    {
        Assert.Equal(XPlaneScanOutcome.AllBanned, Report(entries: 5, banned: 5, models: 0).Outcome);
    }

    [Fact]
    public void Outcome_SomeModelsDespiteBansAndErrors_IsStillModels()
    {
        Assert.Equal(XPlaneScanOutcome.Models, Report(entries: 5, banned: 2, errors: 1, models: 3).Outcome);
    }

    [Fact]
    public void Summary_ListsEveryCountAndTheOutcome()
    {
        string summary = Report(aircraft: 22, xsb: 23, entries: 23, banned: 1, errors: 0, models: 22).Summary();

        Assert.Contains("22 aircraft file(s)", summary);
        Assert.Contains("23 CSL file(s)", summary);
        Assert.Contains("23 entries", summary);
        Assert.Contains("1 banned", summary);
        Assert.Contains("0 unreadable", summary);
        Assert.Contains("22 model(s)", summary);
        Assert.Contains("Models", summary);
    }

    [Fact]
    public void Summary_ShowsLinkedPacks_AndDefaultsToNone()
    {
        Assert.Contains("0 linked pack(s)", Report().Summary());
        Assert.Contains("3 linked pack(s)", new XPlaneScanReport(true, 0, 10, 10, 0, 0, 10, 3).Summary());
    }

    [Theory]
    [InlineData(XPlaneScanOutcome.NoCslFolder, true)]
    [InlineData(XPlaneScanOutcome.NoXsbFiles, true)]
    [InlineData(XPlaneScanOutcome.NoEntries, false)]
    [InlineData(XPlaneScanOutcome.AllBanned, false)]
    [InlineData(XPlaneScanOutcome.ReadErrors, false)]
    public void NothingGenerated_IsTrueOnlyWhenThereAreNoCslModelsAtAll(XPlaneScanOutcome outcome, bool expected)
    {
        Assert.Equal(expected, XPlaneScanReport.NothingGenerated(outcome));
    }
}
