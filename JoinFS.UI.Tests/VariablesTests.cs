using JoinFS.UI.Models;
using JoinFS.UI.Services;
using JoinFS.UI.Services.Fake;
using JoinFS.UI.ViewModels;
using JoinFS.UI.ViewModels.Overlays;
using JoinFS.UI.ViewModels.Tabs;

namespace JoinFS.UI.Tests;

public class VariablesTests
{
    private const string Model = "PMDG 777-200ER GE PMDG House"; // has two files of its own in the fake catalog

    private static FakeVariablesCatalog Variables(Rig rig) => (FakeVariablesCatalog)rig.Services.Variables;

    private static VariablesOverlayViewModel Open(Rig rig, string? model = Model) =>
        new(model, rig.Services.Variables, rig.Services.Models, rig.Platform);

    [Fact]
    public void It_starts_on_the_model_given_with_its_files()
    {
        Rig rig = new();

        VariablesOverlayViewModel overlay = Open(rig);

        Assert.Equal(Model, overlay.Model);
        Assert.Equal(Model, overlay.Picker.SelectedType);
        Assert.Equal(["Custom_FMC_Vars", "ListBox_Sets"], overlay.Files);
    }

    [Fact]
    public void A_model_with_no_list_of_its_own_shows_the_default_of_its_kind()
    {
        Rig rig = new();

        VariablesOverlayViewModel overlay = Open(rig, "Latecoere 631");

        Assert.Equal(["Plane.txt", "SingleProp.txt"], overlay.Files);
    }

    [Fact]
    public void A_model_the_simulator_does_not_have_stays_the_model_until_another_is_picked()
    {
        Rig rig = new();

        VariablesOverlayViewModel overlay = Open(rig, "Not Installed");

        Assert.Equal("Not Installed", overlay.Model);

        overlay.Picker.SelectedType = "Latecoere 631";
        Assert.Equal("Latecoere 631", overlay.Model);
    }

    [Fact]
    public void Picking_another_model_moves_to_its_files()
    {
        Rig rig = new();
        VariablesOverlayViewModel overlay = Open(rig);

        overlay.Picker.SelectedType = "CH-1 N-1981";

        Assert.Equal("CH-1 N-1981", overlay.Model);
        Assert.Equal(["Rotor_Vars"], overlay.Files);
    }

    [Fact]
    public async Task Add_keeps_the_file_by_its_name_under_the_variables_folder()
    {
        Rig rig = new();
        VariablesOverlayViewModel overlay = Open(rig);
        rig.Platform.PickedFile = Path.Combine(Variables(rig).FilesFolder, "Mine", "Extra.txt");

        await overlay.AddCommand.ExecuteAsync(null);

        Assert.Equal(Variables(rig).FilesFolder, rig.Platform.LastStartFolder);
        Assert.Equal(Path.Combine("Mine", "Extra.txt"), overlay.Files[^1]);
        Assert.Equal("", overlay.Status);
    }

    [Fact]
    public async Task A_file_from_outside_the_folder_is_refused_and_says_why()
    {
        Rig rig = new();
        VariablesOverlayViewModel overlay = Open(rig);
        rig.Platform.PickedFile = Path.Combine(Path.GetTempPath(), "Elsewhere", "Extra.txt");

        await overlay.AddCommand.ExecuteAsync(null);

        Assert.Equal(["Custom_FMC_Vars", "ListBox_Sets"], overlay.Files);
        Assert.Contains(Variables(rig).FilesFolder, overlay.Status);
    }

    [Fact]
    public async Task A_folder_with_the_same_start_is_not_taken_for_the_variables_folder()
    {
        Rig rig = new();
        VariablesOverlayViewModel overlay = Open(rig);
        rig.Platform.PickedFile = Variables(rig).FilesFolder + "-other" + Path.DirectorySeparatorChar + "Extra.txt";

        await overlay.AddCommand.ExecuteAsync(null);

        Assert.Equal(2, overlay.Files.Count);
    }

    [Fact]
    public async Task Cancelling_the_file_dialog_adds_nothing()
    {
        Rig rig = new();
        VariablesOverlayViewModel overlay = Open(rig);
        rig.Platform.PickedFile = null;

        await overlay.AddCommand.ExecuteAsync(null);

        Assert.Equal(2, overlay.Files.Count);
    }

    [Fact]
    public void Remove_takes_the_selected_file_off_the_list_and_needs_a_selection()
    {
        Rig rig = new();
        VariablesOverlayViewModel overlay = Open(rig);
        Assert.False(overlay.RemoveCommand.CanExecute(null));

        overlay.SelectedFile = "Custom_FMC_Vars";
        Assert.True(overlay.RemoveCommand.CanExecute(null));
        overlay.RemoveCommand.Execute(null);

        Assert.Equal(["ListBox_Sets"], overlay.Files);
        Assert.Equal(["ListBox_Sets"], rig.Services.Variables.GetFiles(Model));
    }

    [Fact]
    public void The_default_files_cannot_be_edited_the_others_open_from_the_folder()
    {
        Rig rig = new();
        VariablesOverlayViewModel overlay = Open(rig, "Latecoere 631");

        overlay.SelectedFile = "Plane.txt";
        Assert.False(overlay.EditCommand.CanExecute(null));

        overlay.Picker.SelectedType = Model;
        overlay.SelectedFile = "Custom_FMC_Vars";
        Assert.True(overlay.EditCommand.CanExecute(null));
        overlay.EditCommand.Execute(null);

        Assert.Equal([Path.Combine(Variables(rig).FilesFolder, "Custom_FMC_Vars")], rig.Platform.OpenedFiles);
    }

    [Fact]
    public void Ok_applies_the_changes_and_closing_any_other_way_does_not()
    {
        Rig rig = new();

        Open(rig).CloseCommand.Execute(null);
        Assert.Equal(0, Variables(rig).Applied);

        Open(rig).OkCommand.Execute(null);
        Assert.Equal(1, Variables(rig).Applied);
    }

    [Fact]
    public void Without_a_picker_the_model_stays_the_one_given()
    {
        Rig rig = new();
        StubVariables variables = new(rig.Services.Variables) { PickModel = false };

        VariablesOverlayViewModel overlay = new(Model, variables, rig.Services.Models, rig.Platform);
        overlay.Picker.SelectedType = "Latecoere 631";

        Assert.False(overlay.CanPickModel);
        Assert.Equal(Model, overlay.Model);
    }

    // ---- where it is opened from

    [Fact]
    public void The_aircraft_list_opens_the_variables_of_the_model_that_aircraft_shows()
    {
        ScriptedTraffic traffic = new() { VariablesModel = "CH-1 N-1981" };
        AppServices services = FakeServices.Create(TimeSpan.Zero, new UserSettings { Onboarded = true, Nickname = "Me" }) with { Traffic = traffic };
        MainViewModel main = new(services);

        main.Aircraft.Rows[0].Actions.Single(a => a.Label.StartsWith("Assign Variables")).Command.Execute(null);

        VariablesOverlayViewModel overlay = Assert.IsType<VariablesOverlayViewModel>(main.Overlay);
        Assert.Equal("CH-1 N-1981", overlay.Model);
    }

    [Fact]
    public void An_aircraft_that_is_gone_opens_nothing()
    {
        ScriptedTraffic traffic = new() { VariablesModel = null };
        AppServices services = FakeServices.Create(TimeSpan.Zero, new UserSettings { Onboarded = true, Nickname = "Me" }) with { Traffic = traffic };
        MainViewModel main = new(services);

        main.Aircraft.Rows[0].Actions.Single(a => a.Label.StartsWith("Assign Variables")).Command.Execute(null);

        Assert.Null(main.Overlay);
    }

    [Fact]
    public void The_settings_list_follows_what_the_overlay_changed_once_it_is_closed()
    {
        Rig rig = new();
        VariableAssignmentViewModel row = rig.Main.Settings.Variables.Assignments.Single(a => a.Model == "CH-1 N-1981");
        row.EditCommand.Execute(null);
        VariablesOverlayViewModel overlay = (VariablesOverlayViewModel)rig.Main.Overlay!;
        overlay.SelectedFile = "Rotor_Vars";
        overlay.RemoveCommand.Execute(null);

        overlay.OkCommand.Execute(null);

        Assert.Same(row, rig.Main.Settings.Variables.Assignments.Single(a => a.Model == "CH-1 N-1981"));
        Assert.Equal("", row.FilesText);
    }

    [Fact]
    public void A_model_that_got_a_list_of_its_own_appears_in_the_settings_list_when_the_card_opens()
    {
        Rig rig = new();
        rig.Services.Variables.AddFiles("Latecoere 631", ["New.txt"]);
        Assert.DoesNotContain(rig.Main.Settings.Variables.Assignments, a => a.Model == "Latecoere 631");

        rig.Main.Settings.Variables.ToggleCommand.Execute(null);

        Assert.Contains(rig.Main.Settings.Variables.Assignments, a => a.Model == "Latecoere 631");
    }
}

/// <summary>A variables catalog whose one setting a test decides, on top of another.</summary>
internal sealed class StubVariables(IVariablesCatalog inner) : IVariablesCatalog
{
    public bool PickModel { get; set; } = true;

    public IReadOnlyList<VariableAssignment> GetAssignments() => inner.GetAssignments();
    public bool CanPickModel => PickModel;
    public string FilesFolder => inner.FilesFolder;
    public IReadOnlyList<string> GetFiles(string model) => inner.GetFiles(model);
    public void AddFiles(string model, IReadOnlyList<string> files) => inner.AddFiles(model, files);
    public void RemoveFile(string model, int index) => inner.RemoveFile(model, index);
    public bool IsBuiltIn(string file) => inner.IsBuiltIn(file);
    public void Apply() => inner.Apply();
}
