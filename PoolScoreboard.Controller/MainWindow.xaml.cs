using System.Windows;
using PoolScoreboard.Controller.ViewModels;
using PoolScoreboard.Core.Enums;

namespace PoolScoreboard.Controller;

public partial class MainWindow : Window
{
    private readonly GameViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new GameViewModel();
        _viewModel.SetupPlayers(League.APA, GameType.NineBall);

        DataContext = _viewModel;

        UpdateAtTableButton();
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(GameViewModel.HomeAtTable))
                UpdateAtTableButton();
        };
    }

    private void UpdateAtTableButton()
    {
        AtTableToggleButton.Content = _viewModel.HomeAtTable ? "HOME at Table" : "AWAY at Table";
    }
}