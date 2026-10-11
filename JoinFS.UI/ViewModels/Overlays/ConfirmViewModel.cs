using CommunityToolkit.Mvvm.Input;

namespace JoinFS.UI.ViewModels.Overlays;

public enum ConfirmChoice { Cancel, Yes, No }

/// <summary>
/// Asks a question with two answers and a way out. <see cref="Result"/> is the answer; closing the card any other way (the cross,
/// Escape) is <see cref="ConfirmChoice.Cancel"/>.
/// </summary>
public sealed partial class ConfirmViewModel(string title, string message, string yesText, string noText) : OverlayViewModel
{
    private readonly TaskCompletionSource<ConfirmChoice> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override string Title => title;
    public string Message => message;
    public string YesText => yesText;
    public string NoText => noText;

    public Task<ConfirmChoice> Result => _result.Task;

    protected override void OnClosing() => _result.TrySetResult(ConfirmChoice.Cancel);

    [RelayCommand]
    private void Yes()
    {
        _result.TrySetResult(ConfirmChoice.Yes);
        Close();
    }

    [RelayCommand]
    private void No()
    {
        _result.TrySetResult(ConfirmChoice.No);
        Close();
    }
}
