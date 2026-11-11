using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using JoinFS.UI.ViewModels.Overlays;

namespace JoinFS.UI.Views.Overlays;

public partial class OnboardingView : UserControl
{
    private OnboardingViewModel? _viewModel;

    public OnboardingView()
    {
        InitializeComponent();
        HubList.DoubleTapped += (_, _) => Connect();
        HubList.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                Connect();
        };
    }

    // A double-click or Enter on a hub is the Connect button; it does nothing until a hub is picked.
    private void Connect()
    {
        if (_viewModel?.ConnectCommand.CanExecute(null) == true)
            _viewModel.ConnectCommand.Execute(null);
    }

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
                OnboardingStep.Welcome when _viewModel.UsesSimbrief => SimbriefBox,
                OnboardingStep.Welcome => NicknameBox,
                OnboardingStep.Simulator => FolderBox,
                _ => null,
            };
            box?.Focus();
        }, DispatcherPriority.Input);
    }
}
