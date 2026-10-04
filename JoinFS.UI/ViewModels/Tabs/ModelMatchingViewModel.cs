using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Models;
using JoinFS.UI.Services;
using JoinFS.UI.ViewModels.Overlays;

namespace JoinFS.UI.ViewModels.Tabs;

/// <summary>
/// One rule of the Model Matching table. The row lives as long as the rule does and is updated in place, so a refresh never
/// rebuilds what the user is looking at.
/// </summary>
public sealed partial class ModelRuleRowViewModel : ObservableObject
{
    private readonly ModelMatchingViewModel _owner;

    internal ModelRuleRowViewModel(ModelRule rule, ModelMatchingViewModel owner)
    {
        Original = rule.Original;
        _substitute = rule.Substitute;
        IsRemovable = !rule.IsDefault;
        _owner = owner;
    }

    public string Original { get; }

    [ObservableProperty]
    private string _substitute;

    /// <summary>Only rules the user added can be removed; the built-in defaults can only be edited.</summary>
    public bool IsRemovable { get; }

    internal void Update(ModelRule rule) => Substitute = rule.Substitute;

    [RelayCommand]
    private Task Edit() => _owner.EditAsync(this);

    [RelayCommand]
    private void Remove() => _owner.Remove(this);
}

/// <summary>Model Matching tab: which model stands in for which. Edits go through the shared Substitute overlay.</summary>
public sealed partial class ModelMatchingViewModel : ObservableObject
{
    private readonly IModelCatalog _catalog;
    private readonly IShell _shell;
    private readonly Dictionary<string, ModelRuleRowViewModel> _rowsByOriginal = [];

    public ModelMatchingViewModel(IModelCatalog catalog, IShell shell)
    {
        _catalog = catalog;
        _shell = shell;
        Refresh();
    }

    public ObservableCollection<ModelRuleRowViewModel> Rows { get; } = [];

    /// <summary>How the models stand: how many are known, or that a scan is running. Empty when there is nothing to say.</summary>
    [ObservableProperty]
    private string _scanStatus = "";

    /// <summary>Reads the table again. Rules still there are updated in place; new ones get a row, ones gone lose theirs.</summary>
    public void Refresh()
    {
        List<ModelRuleRowViewModel> wanted = [];
        HashSet<string> seen = [];
        foreach (ModelRule rule in _catalog.GetRules())
        {
            if (!seen.Add(rule.Original))
                continue; // a model has one match; a repeat is a fault in the source
            if (_rowsByOriginal.TryGetValue(rule.Original, out ModelRuleRowViewModel? row) && row.IsRemovable == !rule.IsDefault)
                row.Update(rule);
            else
                _rowsByOriginal[rule.Original] = row = new ModelRuleRowViewModel(rule, this);
            wanted.Add(row);
        }

        foreach (string gone in _rowsByOriginal.Keys.Except(seen).ToList())
            _rowsByOriginal.Remove(gone);

        CollectionSync.Reconcile(Rows, wanted);
        ScanStatus = _catalog.ScanStatus;
    }

    internal async Task EditAsync(ModelRuleRowViewModel row)
    {
        ModelTarget target = new(row.Original);
        ModelChoice? current = await _catalog.GetCurrentAsync(target);

        SubstituteViewModel overlay = new(target, current, _catalog);
        // What the overlay changes shows in the table as soon as it is closed.
        overlay.CloseRequested += (_, _) => Refresh();
        _shell.ShowOverlay(overlay);
    }

    internal void Remove(ModelRuleRowViewModel row)
    {
        _catalog.ClearSubstitute(new ModelTarget(row.Original));
        Refresh();
    }
}
