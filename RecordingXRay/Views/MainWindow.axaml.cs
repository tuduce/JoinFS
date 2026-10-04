using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using RecordingXRay.ViewModels;

namespace RecordingXRay.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        TitleBar.PointerPressed += OnTitleBarPointerPressed;
    }

    // The client area extends under the title bar, so moving and maximising the window is our job.
    // Buttons handle their own presses, so only the empty parts of the bar get here.
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2 && CanResize)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else
        {
            BeginMoveDrag(e);
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.PickFile = PickFileAsync;
            viewModel.CopyText = CopyTextAsync;
        }
    }

    private Task CopyTextAsync(string text) => Clipboard is { } clipboard ? clipboard.SetTextAsync(text) : Task.CompletedTask;

    private async Task<string?> PickFileAsync()
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open JoinFS Recording",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("JoinFS recordings") { Patterns = ["*.jfs"] },
                FilePickerFileTypes.All,
            ],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private static void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.IsDragOver = e.DataTransfer.Contains(DataFormat.File);
        }
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.IsDragOver = false;
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is MainViewModel dragging)
        {
            dragging.IsDragOver = false;
        }

        string? path = e.DataTransfer.TryGetFiles()?.Select(file => file.TryGetLocalPath()).FirstOrDefault(p => p is not null);
        if (path is not null && DataContext is MainViewModel viewModel)
        {
            await viewModel.LoadAsync(path);
        }
    }
}
