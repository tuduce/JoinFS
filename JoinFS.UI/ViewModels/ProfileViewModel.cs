using CommunityToolkit.Mvvm.ComponentModel;
using JoinFS.UI.Services;

namespace JoinFS.UI.ViewModels;

/// <summary>
/// What the user entered once and keeps: nickname, SimBrief username, model overrides, height adjustments.
/// Every change is written straight back to the <see cref="ISettingsStore"/>, so "ask once, remember" holds across runs.
/// </summary>
public sealed partial class ProfileViewModel : ObservableObject
{
    private readonly ISettingsStore _store;
    private readonly UserSettings _settings;

    public ProfileViewModel(ISettingsStore store)
    {
        _store = store;
        _settings = store.Load();
        _nickname = _settings.Nickname;
        _simbriefUsername = _settings.SimbriefUsername ?? "";
        _broadcastTacpack = _settings.BroadcastTacpack;
        _broadcastEverything = _settings.BroadcastEverything;
        _modelScanOnConnect = _settings.ModelScanOnConnect;
        _generateCsl = _settings.GenerateCsl;
        _skipCsl = _settings.SkipCsl;
    }

    /// <summary>Raised when an override is added, changed or removed.</summary>
    public event EventHandler? OverridesChanged;

    public bool Onboarded => _settings.Onboarded;

    [ObservableProperty]
    private string _nickname;

    [ObservableProperty]
    private string _simbriefUsername;

    /// <summary>Broadcast the VRS TacPack. Shared by Settings → Simulator and the Objects tab.</summary>
    [ObservableProperty]
    private bool _broadcastTacpack;

    /// <summary>Broadcast every object, without asking per object or model.</summary>
    [ObservableProperty]
    private bool _broadcastEverything;

    partial void OnBroadcastTacpackChanged(bool value)
    {
        _settings.BroadcastTacpack = value;
        Save();
    }

    partial void OnBroadcastEverythingChanged(bool value)
    {
        _settings.BroadcastEverything = value;
        Save();
    }

    /// <summary>Scan for models when the simulator connects. "Scan at launch" in the X-Plane dialog is the same setting.</summary>
    [ObservableProperty]
    private bool _modelScanOnConnect;

    /// <summary>X-Plane: generate CSL objects for the installed aircraft.</summary>
    [ObservableProperty]
    private bool _generateCsl;

    /// <summary>X-Plane: leave out CSL objects that were already generated.</summary>
    [ObservableProperty]
    private bool _skipCsl;

    partial void OnModelScanOnConnectChanged(bool value)
    {
        _settings.ModelScanOnConnect = value;
        Save();
    }

    partial void OnGenerateCslChanged(bool value)
    {
        _settings.GenerateCsl = value;
        Save();
    }

    partial void OnSkipCslChanged(bool value)
    {
        _settings.SkipCsl = value;
        Save();
    }

    public bool HasSimbriefUsername => !string.IsNullOrWhiteSpace(SimbriefUsername);

    partial void OnNicknameChanged(string value)
    {
        _settings.Nickname = value;
        Save();
    }

    partial void OnSimbriefUsernameChanged(string value)
    {
        _settings.SimbriefUsername = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        OnPropertyChanged(nameof(HasSimbriefUsername));
        Save();
    }

    public void CompleteOnboarding(string nickname, string? simbriefUsername)
    {
        Nickname = nickname.Trim();
        if (!string.IsNullOrWhiteSpace(simbriefUsername))
            SimbriefUsername = simbriefUsername.Trim();
        _settings.Onboarded = true;
        Save();
        OnPropertyChanged(nameof(Onboarded));
    }

    /// <summary>The substitute chosen for <paramref name="original"/>, or null when none was saved.</summary>
    public string? GetOverride(string original) => _settings.ModelOverrides.GetValueOrDefault(original);

    public IReadOnlyDictionary<string, string> Overrides => _settings.ModelOverrides;

    public void SetOverride(string original, string substitute)
    {
        _settings.ModelOverrides[original] = substitute;
        Save();
        OverridesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveOverride(string original)
    {
        if (_settings.ModelOverrides.Remove(original))
        {
            Save();
            OverridesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Per-model height offset in centimetres. Zero means off.</summary>
    public int GetHeightAdjustmentCm(string model) => _settings.HeightAdjustmentsCm.GetValueOrDefault(model);

    public void SetHeightAdjustmentCm(string model, int centimetres)
    {
        if (centimetres == 0)
            _settings.HeightAdjustmentsCm.Remove(model);
        else
            _settings.HeightAdjustmentsCm[model] = centimetres;
        Save();
    }

    private void Save() => _store.Save(_settings);
}
