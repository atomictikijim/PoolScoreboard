using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PoolScoreboard.Controller.Persistence;
using PoolScoreboard.Controller.ViewModels;

namespace PoolScoreboard.Controller;

public partial class MainWindow : Window
{
    private readonly GameViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        var app = (App)Application.Current;
        // The window supplies itself and its dispatcher, so the view model never reads Application.Current.
        _viewModel = new GameViewModel(
            app.SharedGameManager,
            app.RequestProfileSave,
            app.OverlaySession,
            new PlayerRosterStore(),
            Dispatcher,
            this);

        DataContext = _viewModel;

        PreviewKeyDown += MainWindow_PreviewKeyDown;

        // Remember both sides on the way out, so a match that was set up but never started still
        // leaves its players in the roster.
        Closed += (_, _) => _viewModel.RememberPlayers();
    }

    // Live-view shortcuts: Q/P score the home/away player, A undoes the last point,
    // Space toggles the current shooter, N starts a new rack, T starts or restarts the
    // shot clock, E calls the current shooter's extension. Only active once a match is
    // running, and ignored while a setup TextBox has focus.
    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_viewModel.GameInitialized || Keyboard.FocusedElement is TextBox)
            return;

        switch (e.Key)
        {
            case Key.Q:
                _viewModel.AddHomePointCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.P:
                _viewModel.AddAwayPointCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.A:
                _viewModel.UndoLastPointCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Space:
                _viewModel.ToggleCurrentShooterCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.N:
                _viewModel.NewRackCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.T:
                // One press per shot: the clock restarts from the full time, and a point or a newly pocketed ball
                // restarts it again for the shot those set up.
                _viewModel.StartShotClockCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.E:
                // The player's extension, called on their behalf: it adds time to the clock already running, once
                // per player per rack. Refused the way the greyed button is — Execute would run it regardless, and
                // the key must not do what the button refuses to.
                if (_viewModel.ExtendShotClockCommand.CanExecute(null))
                    _viewModel.ExtendShotClockCommand.Execute(null);

                e.Handled = true;
                break;
        }
    }
}
