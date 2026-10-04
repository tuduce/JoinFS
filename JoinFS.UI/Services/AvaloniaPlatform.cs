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

    public async Task OpenFileAsync(string path)
    {
        if (topLevel() is not { } top || !File.Exists(path))
            return;
        if (await top.StorageProvider.TryGetFileFromPathAsync(new Uri(path)) is { } file)
            await top.Launcher.LaunchFileAsync(file);
    }

    private static FilePickerFileType[]? KindOf(string? extension) =>
        extension is null ? null : [new FilePickerFileType("JoinFS files") { Patterns = ["*." + extension] }];

    public async Task<string?> PickOpenFileAsync(string title, string? startFolder = null, string? extension = null)
    {
        if (topLevel()?.StorageProvider is not { } storage)
            return null;

        FilePickerOpenOptions options = new() { Title = title, AllowMultiple = false, FileTypeFilter = KindOf(extension) };
        if (startFolder is not null && Directory.Exists(startFolder))
            options.SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(new Uri(startFolder));

        var files = await storage.OpenFilePickerAsync(options);
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(string title, string suggestedName, string? startFolder = null, string? extension = null)
    {
        if (topLevel()?.StorageProvider is not { } storage)
            return null;

        FilePickerSaveOptions options = new()
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            FileTypeChoices = KindOf(extension),
        };
        if (startFolder is not null && Directory.Exists(startFolder))
            options.SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(new Uri(startFolder));

        var file = await storage.SaveFilePickerAsync(options);
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
