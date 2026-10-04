using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Models;
using JoinFS.UI.Services;

namespace JoinFS.UI.ViewModels.Overlays;

/// <summary>
/// Picks a model the way the old dialogs did: filter words narrow the types, a type has variations, and the two together name a model.
/// Shared by Substitute and Variables.
/// </summary>
public sealed partial class ModelPickerViewModel : ObservableObject
{
    private readonly IModelCatalog _catalog;
    private bool _loading = true;

    /// <param name="start">What to start on. It stays pickable even when the filter would leave it out.</param>
    public ModelPickerViewModel(IModelCatalog catalog, ModelChoice? start = null)
    {
        _catalog = catalog;

        List<string> types = [.. catalog.GetTypes("")];
        if (start is not null && !types.Contains(start.Type))
            types.Insert(0, start.Type);
        foreach (string type in types)
            Types.Add(type);

        _selectedType = start?.Type ?? types.FirstOrDefault();
        FillVariations(start?.Variation);
        _loading = false;
    }

    /// <summary>False until the simulator's models are known: there is nothing to pick from.</summary>
    public bool HasModels => _catalog.HasModels;

    /// <summary>Optional. Narrows the type list to the entries containing every word.</summary>
    [ObservableProperty]
    private string _filterWords = "";

    public ObservableCollection<string> Types { get; } = [];
    public ObservableCollection<string> Variations { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Replacement), nameof(Choice))]
    private string? _selectedType;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Replacement), nameof(Choice))]
    private string? _selectedVariation;

    /// <summary>The model chosen, as it is shown; empty when nothing is chosen.</summary>
    public string Replacement =>
        Choice is { } choice ? _catalog.GetReplacement(choice.Type, choice.Variation) : "";

    /// <summary>The type and variation picked, or null while one of them is missing.</summary>
    public ModelChoice? Choice =>
        SelectedType is { } type && SelectedVariation is { } variation ? new ModelChoice(type, variation) : null;

    partial void OnFilterWordsChanged(string value)
    {
        string? keep = SelectedType;
        Types.Clear();
        foreach (string type in _catalog.GetTypes(value))
            Types.Add(type);
        SelectedType = keep is not null && Types.Contains(keep) ? keep : Types.FirstOrDefault();
    }

    partial void OnSelectedTypeChanged(string? value)
    {
        if (!_loading)
            FillVariations(null);
    }

    private void FillVariations(string? keep)
    {
        Variations.Clear();
        if (SelectedType is { } type)
        {
            foreach (string variation in _catalog.GetVariations(type))
                Variations.Add(variation);
        }
        if (keep is not null && !Variations.Contains(keep))
            Variations.Insert(0, keep);
        SelectedVariation = keep ?? Variations.FirstOrDefault();
    }
}

/// <summary>
/// "Substitution": picks the model that stands in for <see cref="Original"/>. The replacement is shown as it will be saved.
/// Shared by the Aircraft, Objects and Model Matching tabs.
/// </summary>
public sealed partial class SubstituteViewModel : OverlayViewModel
{
    public const string VariationSeparator = " [+] ";

    private readonly ModelTarget _target;
    private readonly IModelCatalog _catalog;

    /// <param name="current">What stands in for the model now, to start the picker on.</param>
    public SubstituteViewModel(ModelTarget target, ModelChoice? current, IModelCatalog catalog)
    {
        _target = target;
        _catalog = catalog;
        Picker = new ModelPickerViewModel(catalog, current);
    }

    public override string Title => "Substitution";

    /// <summary>The model to be replaced, with its livery when it has one.</summary>
    public string Original => _target.Livery.Length > 0 ? _target.Model + VariationSeparator + _target.Livery : _target.Model;

    public ModelPickerViewModel Picker { get; }

    [RelayCommand]
    private void Save()
    {
        if (Picker.Choice is { } choice)
            _catalog.SetSubstitute(_target, choice.Type, choice.Variation);
        Close();
    }

    /// <summary>"No substitution": the model stands for itself.</summary>
    [RelayCommand]
    private void UseOriginal()
    {
        _catalog.ClearSubstitute(_target);
        Close();
    }
}

/// <summary>"Adjust Height": a per-model Y offset in steps of 5 or 50 cm. Cancel throws the edits away.</summary>
public sealed partial class AdjustHeightViewModel : OverlayViewModel
{
    private readonly ProfileViewModel _profile;

    public AdjustHeightViewModel(string model, ProfileViewModel profile)
    {
        Model = model;
        _profile = profile;
        _adjustmentCm = profile.GetHeightAdjustmentCm(model);
    }

    public override string Title => "Adjust Height";

    public string Model { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AdjustmentLabel))]
    private int _adjustmentCm;

    public string AdjustmentLabel => AdjustmentCm == 0 ? "Off" : $"{(AdjustmentCm > 0 ? "+" : "")}{AdjustmentCm} cm";

    [RelayCommand]
    private void Up50() => AdjustmentCm += 50;

    [RelayCommand]
    private void Up5() => AdjustmentCm += 5;

    [RelayCommand]
    private void Down5() => AdjustmentCm -= 5;

    [RelayCommand]
    private void Down50() => AdjustmentCm -= 50;

    [RelayCommand]
    private void Off() => AdjustmentCm = 0;

    [RelayCommand]
    private void Ok()
    {
        _profile.SetHeightAdjustmentCm(Model, AdjustmentCm);
        Close();
    }
}

/// <summary>"Explain Match": a read-only comparison of what was asked for and what was matched, and the steps tried.</summary>
public sealed partial class ExplainMatchViewModel : OverlayViewModel
{
    private readonly IModelCatalog _catalog;
    private readonly IPlatform _platform;
    private readonly MatchExplanation _explanation;

    public ExplainMatchViewModel(MatchExplanation explanation, IModelCatalog catalog, IPlatform platform)
    {
        _explanation = explanation;
        _catalog = catalog;
        _platform = platform;
    }

    public override string Title => "Explain Match: " + _explanation.Callsign;

    public string Outcome => _explanation.Outcome;

    /// <summary>A warning about the matched model, shown above the table. Null when there is none.</summary>
    public string? Note => _explanation.Note;

    public IReadOnlyList<ExplainRow> Rows => _explanation.Rows;
    public IReadOnlyList<string> Steps => _explanation.Steps;

    /// <summary>Where the list of known models comes from.</summary>
    public string Source => _explanation.Source;

    /// <summary>What the last of the buttons below did, when it did not work or needs saying. Empty otherwise.</summary>
    [ObservableProperty]
    private string _status = "";

    /// <summary>The text "Copy to clipboard" puts on the clipboard: the whole report.</summary>
    public string ClipboardText => _explanation.Report;

    [RelayCommand]
    private Task CopyToClipboardAsync() => _platform.CopyTextAsync(ClipboardText);

    [RelayCommand]
    private async Task ExportDebugBundleAsync()
    {
        string suggested = $"JoinFS-MatchDebug-{_explanation.Callsign}-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
        string? path = await _platform.PickSaveFileAsync("Export debug bundle", suggested);
        if (path is null)
            return;

        try
        {
            _catalog.WriteDebugBundle(path, _explanation.Report);
            Status = "Saved " + path;
        }
        catch (Exception ex)
        {
            Status = "Could not create the bundle: " + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenKnownModelsList()
    {
        string? file = _catalog.KnownModelsFile();
        if (file is null)
            Status = "There is no known-models list yet. Scan for models first.";
        else
            _platform.OpenUrl(file);
    }
}

/// <summary>"Variables": the variable files assigned to one model. Shared by the Aircraft tab and Settings → Variables.</summary>
public sealed partial class VariablesOverlayViewModel : OverlayViewModel
{
    private readonly IPlatform _platform;
    private readonly Action<IReadOnlyList<string>>? _onSave;

    public VariablesOverlayViewModel(string model, IEnumerable<string> files, IPlatform platform, Action<IReadOnlyList<string>>? onSave = null)
    {
        Model = model;
        _platform = platform;
        _onSave = onSave;
        foreach (string file in files)
            Files.Add(file);
    }

    public override string Title => "Variables";

    public string Model { get; }

    [ObservableProperty]
    private string _filterWords = "";

    // The design shows these two selects empty (a single "—"); they fill in once the variable catalogue is wired.
    public IReadOnlyList<string> Types { get; } = ["—"];
    public IReadOnlyList<string> Variations { get; } = ["—"];

    public ObservableCollection<string> Files { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand), nameof(EditCommand))]
    private string? _selectedFile;

    [RelayCommand]
    private async Task AddAsync()
    {
        string? path = await _platform.PickOpenFileAsync("Add variable file");
        if (path is null)
            return;
        string name = Path.GetFileNameWithoutExtension(path);
        if (!Files.Contains(name))
            Files.Add(name);
    }

    private bool HasSelection => SelectedFile is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Remove()
    {
        if (SelectedFile is not null)
            Files.Remove(SelectedFile);
    }

    // Editing a variable file opens its contents; that needs the real variable editor.
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Edit() { }

    [RelayCommand]
    private void Ok()
    {
        _onSave?.Invoke([.. Files]);
        Close();
    }
}
