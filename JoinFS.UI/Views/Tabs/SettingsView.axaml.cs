using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JoinFS.UI.ViewModels.Tabs;

namespace JoinFS.UI.Views.Tabs;

public partial class SettingsView : UserControl
{
    private SettingsViewModel? _settings;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_settings is not null)
                _settings.PropertyChanged -= OnSettingsChanged;
            _settings = DataContext as SettingsViewModel;
            if (_settings is not null)
                _settings.PropertyChanged += OnSettingsChanged;
        };
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        // A card that opens below the fold, or taller than what is left of the page, would open out of sight: bring it into view.
        if (e.PropertyName == nameof(SettingsViewModel.OpenSection) && _settings?.OpenSection is { } section)
            Dispatcher.UIThread.Post(() => ScrollTo(section), DispatcherPriority.Background);
    }

    private void ScrollTo(SettingsSectionViewModel section)
    {
        Border? card = this.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => ReferenceEquals(b.Tag, section));
        if (card is null || Page.Content is not Visual content || card.TranslatePoint(default, content) is not { } top)
            return;

        double bottom = top.Y + card.Bounds.Height;
        if (top.Y < Page.Offset.Y || bottom > Page.Offset.Y + Page.Viewport.Height)
            Page.Offset = new Vector(Page.Offset.X, Math.Max(0, top.Y - 8));
    }
}
