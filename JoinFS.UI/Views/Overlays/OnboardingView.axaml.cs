using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using JoinFS.UI.ViewModels.Overlays;

namespace JoinFS.UI.Views.Overlays;

public partial class OnboardingView : UserControl
{
    private OnboardingViewModel? _viewModel;

    public OnboardingView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = DataContext as OnboardingViewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        FocusInput();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OnboardingViewModel.Step) or nameof(OnboardingViewModel.UsesSimbrief))
            FocusInput();
    }

    /// <summary>Puts the cursor in the box the step asks for, so the user can type at once. The step's controls show a moment later.</summary>
    private void FocusInput()
    {
        Dispatcher.UIThread.Post(() =>
        {
            TextBox? box = _viewModel?.Step switch
            {
                OnboardingStep.Welcome => NicknameBox,
                OnboardingStep.SimBrief when _viewModel.UsesSimbrief => SimbriefBox,
                OnboardingStep.Simulator => FolderBox,
                _ => null,
            };
            box?.Focus();
        }, DispatcherPriority.Input);
    }
}
