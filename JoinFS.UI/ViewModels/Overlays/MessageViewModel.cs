namespace JoinFS.UI.ViewModels.Overlays;

/// <summary>A message for the user to read, with an OK. Escape and the cross close it too. Without a title it is just the text.</summary>
public sealed class MessageViewModel(string? title, string message) : OverlayViewModel
{
    public override string Title => title ?? "";
    public bool HasTitle => !string.IsNullOrEmpty(title);
    public string Message => message;
}
