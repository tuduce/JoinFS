using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace JoinFS.UI.Services;

/// <summary>The window-bound services: clipboard, file and folder pickers, the default browser.</summary>
public sealed class AvaloniaPlatform(Func<TopLevel?> topLevel) : IPlatform
{
    public async Task CopyTextAsync(string text)
    {
        if (topLevel()?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    public void OpenUrl(string url)
    {
        // Only web links: a URL from a hub's own data must not launch anything else.
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https")
            _ = topLevel()?.Launcher.LaunchUriAsync(uri);
    }

    public async Task<string?> PickOpenFileAsync(string title)
    {
        if (topLevel()?.StorageProvider is not { } storage)
            return null;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(string title, string suggestedName)
    {
        if (topLevel()?.StorageProvider is not { } storage)
            return null;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions { Title = title, SuggestedFileName = suggestedName });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        if (topLevel()?.StorageProvider is not { } storage)
            return null;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
