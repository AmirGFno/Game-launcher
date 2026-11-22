using System.Windows;
using GameLauncher.ViewModels;

namespace GameLauncher.Views;

public partial class GameEditorWindow : Window
{
    private readonly GameEditorViewModel _viewModel;

    public GameEditorWindow(GameEditorViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
        _viewModel.RequestClose += OnRequestClose;

        Closed += OnClosed;
    }

    private void OnRequestClose(object? sender, bool dialogResult)
    {
        DialogResult = dialogResult;
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.RequestClose -= OnRequestClose;
        Closed -= OnClosed;
    }
}
