using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace JoinFS.UI.ViewModels.Overlays;

/// <summary>First run only. The nickname is required; the SimBrief username is optional.</summary>
public sealed partial class OnboardingViewModel : OverlayViewModel
{
    private readonly ProfileViewModel _profile;

    public OnboardingViewModel(ProfileViewModel profile) => _profile = profile;

    public override string Title => "Welcome to JoinFS";
    public override bool IsDismissable => false;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private string _nickname = "";

    [ObservableProperty]
    private bool _showSimbriefInput;

    [ObservableProperty]
    private string _simbriefUsername = "";

    private bool CanContinue => !string.IsNullOrWhiteSpace(Nickname);

    [RelayCommand]
    private void ToggleSimbriefInput() => ShowSimbriefInput = !ShowSimbriefInput;

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private void Continue()
    {
        // A username typed and then hidden again is not wanted.
        _profile.CompleteOnboarding(Nickname, ShowSimbriefInput ? SimbriefUsername : null);
        Close();
    }
}

/// <summary>Asks for a hub's password; <c>onConfirm</c> continues the join.</summary>
public sealed partial class PasswordPromptViewModel : OverlayViewModel
{
    private readonly Func<string, Task> _onConfirm;

    public PasswordPromptViewModel(string hubName, Func<string, Task> onConfirm)
    {
        HubName = hubName;
        _onConfirm = onConfirm;
    }

    public override string Title => "Password Required";

    public string HubName { get; }
    public string Message => $"{HubName} requires a password to join.";

    [ObservableProperty]
    private string _password = "";

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        Close();
        await _onConfirm(Password);
    }
}

/// <summary>Asks for the SimBrief username once, from Flight Plan. The shell stores it, then runs the import.</summary>
public sealed partial class SimbriefPromptViewModel : OverlayViewModel
{
    private readonly Func<string, Task> _onConfirm;

    public SimbriefPromptViewModel(Func<string, Task> onConfirm) => _onConfirm = onConfirm;

    public override string Title => "Import from SimBrief";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private string _username = "";

    private bool CanImport => !string.IsNullOrWhiteSpace(Username);

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        Close();
        await _onConfirm(Username.Trim());
    }
}
