using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Models;
using JoinFS.UI.Services;

namespace JoinFS.UI.ViewModels.Overlays;

/// <summary>
/// "Substitution": picks the model that stands in for <see cref="Original"/>. The replacement is shown as
/// <c>type [+] variation</c>. Shared by the Aircraft, Objects and Model Matching tabs.
/// </summary>
public sealed partial class SubstituteViewModel : OverlayViewModel
{
    public const string VariationSeparator = " [+] ";

    private readonly ProfileViewModel _profile;
    private readonly List<string> _allTypes;

    public SubstituteViewModel(string original, string currentSubstitute, IModelCatalog catalog, ProfileViewModel profile)
    {
        Original = original;
        _profile = profile;

        (string currentType, string currentVariation) = ParseSubstitute(currentSubstitute);

        // The current type may not be in the catalogue (the original model itself, say); keep it selectable.
        _allTypes = [.. catalog.GetTypes()];
        if (!_allTypes.Contains(currentType))
            _allTypes.Insert(0, currentType);

        List<string> variations = [.. catalog.GetVariations()];
        if (!variations.Contains(currentVariation))
            variations.Insert(0, currentVariation);
        foreach (string variation in variations)
            Variations.Add(variation);

        RebuildTypes(currentType);
        SelectedVariation = currentVariation;
    }

    public override string Title => "Substitution";

    public string Original { get; }

    /// <summary>Optional. Narrows the type list to the entries containing every word.</summary>
    [ObservableProperty]
    private string _filterWords = "";

    public ObservableCollection<string> Types { get; } = [];
    public ObservableCollection<string> Variations { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    private string? _selectedType;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    private string? _selectedVariation;

    /// <summary>The replacement as it will be saved.</summary>
    public string Preview => $"{SelectedType}{VariationSeparator}{SelectedVariation}";

    partial void OnFilterWordsChanged(string value) => RebuildTypes(SelectedType);

    [RelayCommand]
    private void Save()
    {
        _profile.SetOverride(Original, Preview);
        Close();
    }

    /// <summary>"No substitution": the model stands in for itself.</summary>
    [RelayCommand]
    private void UseOriginal()
    {
        _profile.SetOverride(Original, Original);
        Close();
    }

    public static (string Type, string Variation) ParseSubstitute(string substitute)
    {
        int split = substitute.IndexOf(VariationSeparator, StringComparison.Ordinal);
        return split < 0
            ? (substitute, "Factory")
            : (substitute[..split], substitute[(split + VariationSeparator.Length)..]);
    }

    private void RebuildTypes(string? keep)
    {
        string[] words = FilterWords.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Types.Clear();
        foreach (string type in _allTypes.Where(t => words.All(w => t.Contains(w, StringComparison.OrdinalIgnoreCase))))
            Types.Add(type);
        SelectedType = keep is not null && Types.Contains(keep) ? keep : Types.FirstOrDefault();
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
    private readonly IPlatform _platform;

    public ExplainMatchViewModel(string model, IModelCatalog catalog, IPlatform platform)
    {
        Model = model;
        _platform = platform;
        Rows = catalog.Explain(model);
        Steps = catalog.ExplainSteps(model);
    }

    public override string Title => "Explain Match";

    public string Model { get; }
    public IReadOnlyList<ExplainRow> Rows { get; }
    public IReadOnlyList<string> Steps { get; }

    /// <summary>The text "Copy to clipboard" puts on the clipboard.</summary>
    public string ClipboardText =>
        string.Join(Environment.NewLine,
            [$"Model: {Model}", "", "Attribute\tRequested\tMatched Model",
             .. Rows.Select(r => $"{r.Attribute}\t{r.Requested}\t{r.Matched}"),
             "", "Matching steps (in the order they were tried):", .. Steps]);

    [RelayCommand]
    private Task CopyToClipboardAsync() => _platform.CopyTextAsync(ClipboardText);

    // The README lists the two buttons below as placeholders; they need the real matcher and debug-bundle writer.
    [RelayCommand]
    private void ExportDebugBundle() { }

    [RelayCommand]
    private void OpenKnownModelsList() { }
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
