using CommunityToolkit.Mvvm.ComponentModel;
using JoinFS.UI.Services;

namespace JoinFS.UI.ViewModels;

/// <summary>
/// What the user entered once and keeps: nickname, SimBrief username.
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

    private void Save() => _store.Save(_settings);
}
