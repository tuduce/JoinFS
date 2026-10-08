namespace JoinFS.UI.ViewModels.Overlays;

/// <summary>A message for the user to read, with an OK. Escape and the cross close it too.</summary>
public sealed class MessageViewModel(string title, string message) : OverlayViewModel
{
    public override string Title => title;
    public string Message => message;
}
