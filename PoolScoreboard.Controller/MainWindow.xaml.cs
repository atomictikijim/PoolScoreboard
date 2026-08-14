using System.Windows;
using PoolScoreboard.Controller.ViewModels;

namespace PoolScoreboard.Controller;

public partial class MainWindow : Window
{
    private readonly GameViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new GameViewModel();

        DataContext = _viewModel;

        UpdateCurrentShooterButton();
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(GameViewModel.HomeIsCurrentShooter))
                UpdateCurrentShooterButton();
        };
    }

    private void UpdateCurrentShooterButton()
    {
        CurrentShooterToggleButton.Content = _viewModel.HomeIsCurrentShooter ? "HOME Shooting" : "AWAY Shooting";
    }
}
