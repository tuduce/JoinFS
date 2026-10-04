using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Models;
using JoinFS.UI.Services;
using JoinFS.UI.ViewModels.Overlays;

namespace JoinFS.UI.ViewModels.Tabs;

public sealed partial class ModelRuleRowViewModel : ObservableObject
{
    private readonly ModelMatchingViewModel _owner;

    internal ModelRuleRowViewModel(string original, string substitute, bool isRemovable, ModelMatchingViewModel owner)
    {
        Original = original;
        Substitute = substitute;
        IsRemovable = isRemovable;
        _owner = owner;
    }

    public string Original { get; }
    public string Substitute { get; }

    /// <summary>Only rules the user added can be removed; the built-in defaults can only be edited.</summary>
    public bool IsRemovable { get; }

    [RelayCommand]
    private void Edit() => _owner.Edit(this);

    [RelayCommand]
    private void Remove() => _owner.Remove(this);
}

/// <summary>Model Matching tab: which model stands in for which. Edits go through the shared Substitute overlay.</summary>
public sealed class ModelMatchingViewModel : ObservableObject
{
    private readonly IModelCatalog _catalog;
    private readonly ProfileViewModel _profile;
    private readonly IShell _shell;

    public ModelMatchingViewModel(IModelCatalog catalog, ProfileViewModel profile, IShell shell)
    {
        _catalog = catalog;
        _profile = profile;
        _shell = shell;
        _profile.OverridesChanged += (_, _) => Rebuild();
        Rebuild();
    }

    public ObservableCollection<ModelRuleRowViewModel> Rows { get; } = [];

    internal void Edit(ModelRuleRowViewModel row) =>
        _shell.ShowOverlay(new SubstituteViewModel(row.Original, row.Substitute, _catalog, _profile));

    internal void Remove(ModelRuleRowViewModel row) => _profile.RemoveOverride(row.Original);

    private void Rebuild()
    {
        IReadOnlyList<ModelRule> defaults = _catalog.GetDefaultRules();

        Rows.Clear();
        foreach (ModelRule rule in defaults)
            Rows.Add(new ModelRuleRowViewModel(rule.Original, _profile.GetOverride(rule.Original) ?? rule.Substitute, isRemovable: false, this));

        // Overrides the user saved for models that are not defaults are rules of their own.
        foreach ((string original, string substitute) in _profile.Overrides)
            if (defaults.All(d => d.Original != original))
                Rows.Add(new ModelRuleRowViewModel(original, substitute, isRemovable: true, this));
    }
}
