using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using PoolScoreboard.Controller.ViewModels;

namespace PoolScoreboard.Controller.Hotkeys;

/// <summary>What a global hotkey does. Named rather than pre-bound to a command, so the mapping can be tested.</summary>
public enum GlobalHotkeyAction
{
    HomePoint,
    AwayPoint,
    Undo,
    ToggleShooter,
    NewRack,
    StartShotClock,
    ExtendShotClock
}

/// <summary>One system-wide shortcut: the id Windows reports back, its chord, and the action it runs.</summary>
public sealed record GlobalHotkey(int Id, Key Key, ModifierKeys Modifiers, GlobalHotkeyAction Action);

/// <summary>
/// The shortcuts that work while OBS has focus, which is where the operator's hands are not: the window's own keys
/// need the Controller focused, and during a stream it never is.
/// </summary>
public static class GlobalHotkeys
{
    /// <summary>
    /// One chord per action, all Ctrl+Alt+letter. Deliberately not the bare Q/P/A/Space/N/T/E the window uses: those
    /// are ours only while the Controller has focus, and registering them system-wide would take the letters and the
    /// spacebar from every other program for the length of a match — OBS's own text boxes included. Ctrl+Alt is the
    /// combination almost nothing else claims, and Windows reports a chord it cannot take rather than stealing it.
    /// </summary>
    public static IReadOnlyList<GlobalHotkey> Default { get; } = new GlobalHotkey[]
    {
        new(1, Key.Q, ModifierKeys.Control | ModifierKeys.Alt, GlobalHotkeyAction.HomePoint),
        new(2, Key.P, ModifierKeys.Control | ModifierKeys.Alt, GlobalHotkeyAction.AwayPoint),
        new(3, Key.A, ModifierKeys.Control | ModifierKeys.Alt, GlobalHotkeyAction.Undo),
        new(4, Key.Space, ModifierKeys.Control | ModifierKeys.Alt, GlobalHotkeyAction.ToggleShooter),
        new(5, Key.N, ModifierKeys.Control | ModifierKeys.Alt, GlobalHotkeyAction.NewRack),
        new(6, Key.T, ModifierKeys.Control | ModifierKeys.Alt, GlobalHotkeyAction.StartShotClock),
        new(7, Key.E, ModifierKeys.Control | ModifierKeys.Alt, GlobalHotkeyAction.ExtendShotClock)
    };

    /// <summary>
    /// The command a hotkey runs — the same one its key and its button run, so a chord can never do something the
    /// window cannot.
    /// </summary>
    public static IRelayCommand CommandFor(GameViewModel viewModel, GlobalHotkeyAction action) => action switch
    {
        GlobalHotkeyAction.HomePoint => viewModel.AddHomePointCommand,
        GlobalHotkeyAction.AwayPoint => viewModel.AddAwayPointCommand,
        GlobalHotkeyAction.Undo => viewModel.UndoLastActionCommand,
        GlobalHotkeyAction.ToggleShooter => viewModel.ToggleCurrentShooterCommand,
        GlobalHotkeyAction.NewRack => viewModel.NewRackCommand,
        GlobalHotkeyAction.StartShotClock => viewModel.StartShotClockCommand,
        GlobalHotkeyAction.ExtendShotClock => viewModel.ExtendShotClockCommand,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "No command is mapped to this hotkey.")
    };
}
