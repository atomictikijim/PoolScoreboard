using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using PoolScoreboard.Controller.Hotkeys;
using PoolScoreboard.Controller.Persistence;
using PoolScoreboard.Controller.ViewModels;

namespace PoolScoreboard.Controller;

public partial class MainWindow : Window
{
    private readonly GameViewModel _viewModel;

    /// <summary>
    /// The chords that work while OBS has focus. Registered only while a match is running, because at setup they have
    /// nothing to act on and would be taken from every other program on the machine for no reason.
    /// </summary>
    private readonly GlobalHotkeySession _hotkeys = new(new Win32HotkeyRegistration());

    private IntPtr _window;

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

        // Windows reports a chord to a window, so the handle is what the registration needs — and it does not exist
        // until the window is created.
        SourceInitialized += (_, _) => AttachGlobalHotkeys();

        // A match starting registers the chords, a reset takes them back.
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Remember both sides on the way out, so a match that was set up but never started still
        // leaves its players in the roster.
        Closed += (_, _) =>
        {
            _viewModel.RememberPlayers();
            HwndSource.FromHwnd(_window)?.RemoveHook(OnWindowMessage);
            _hotkeys.Dispose();
        };
    }

    /// <summary>
    /// Registers the global chords against this window and starts listening for them. Attaching after the view model
    /// already knows about a match is the normal case — the autosave restores one before the window exists — so the
    /// session registers whatever is active the moment it is given a handle.
    /// </summary>
    private void AttachGlobalHotkeys()
    {
        _window = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_window)?.AddHook(OnWindowMessage);

        _hotkeys.Attach(_window);
        _hotkeys.SetActive(_viewModel.GameInitialized);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameViewModel.GameInitialized))
            _hotkeys.SetActive(_viewModel.GameInitialized);
    }

    /// <summary>
    /// A global chord arrived, whatever had focus. It runs the command its key runs, and only while a match is on: the
    /// registration is the permission, so an id that is not in the session reaches nothing.
    /// </summary>
    private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != Win32HotkeyRegistration.WmHotkey)
            return IntPtr.Zero;

        var hotkey = _hotkeys.HotkeyFor(wParam.ToInt32());
        if (hotkey is null || !_viewModel.GameInitialized)
            return IntPtr.Zero;

        var command = GlobalHotkeys.CommandFor(_viewModel, hotkey.Action);
        if (command.CanExecute(null))
            command.Execute(null);

        handled = true;
        return IntPtr.Zero;
    }

    // Live-view shortcuts: Q/P score the home/away player, A undoes the last change,
    // Space toggles the current shooter, N starts a new rack, T starts or restarts the
    // shot clock, E calls the current shooter's extension. Only active once a match is
    // running, and ignored while a setup TextBox has focus. The same actions are also
    // registered as Ctrl+Alt+Q/P/A/Space/N/T/E system-wide (Hotkeys/GlobalHotkeys.cs)
    // while a match runs, because during a stream OBS has the focus, not this window.
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
                // One undo for every command the operator can take back — a point or a minus, the shooter, a ball or
                // a group — so a wrong toggle mid-stream is corrected the way a wrong point is.
                _viewModel.UndoLastActionCommand.Execute(null);
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
