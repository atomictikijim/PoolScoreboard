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
    /// One chord per action, all Ctrl+Shift+letter. Deliberately not the bare Q/P/A/Space/N/T/E the window uses: those
    /// are ours only while the Controller has focus, and registering them system-wide would take the letters and the
    /// spacebar from every other program for the length of a match — OBS's own text boxes included.
    /// <para>
    /// And deliberately not Ctrl+Alt either, which is what this started as: on any non-US layout AltGr *is*
    /// Ctrl+Alt, so a Ctrl+Alt+Q chord would swallow the @ a German keyboard types with AltGr+Q (and the € on AltGr+E)
    /// for every program on the machine. Ctrl+Shift types nothing on its own, so no character is taken from anyone.
    /// The cost is the other direction: Ctrl+Shift+letter is a real app shortcut in some programs (Ctrl+Shift+P in an
    /// editor, Ctrl+Shift+T for a browser's reopened tab), and those would fire here while a match is running.
    /// </para>
    /// </summary>
    public static IReadOnlyList<GlobalHotkey> Default { get; } = new GlobalHotkey[]
    {
        new(1, Key.Q, ChordModifiers, GlobalHotkeyAction.HomePoint),
        new(2, Key.P, ChordModifiers, GlobalHotkeyAction.AwayPoint),
        new(3, Key.A, ChordModifiers, GlobalHotkeyAction.Undo),
        new(4, Key.Space, ChordModifiers, GlobalHotkeyAction.ToggleShooter),
        new(5, Key.N, ChordModifiers, GlobalHotkeyAction.NewRack),
        new(6, Key.T, ChordModifiers, GlobalHotkeyAction.StartShotClock),
        new(7, Key.E, ChordModifiers, GlobalHotkeyAction.ExtendShotClock)
    };

    /// <summary>The one modifier set every chord uses, named once so the rule is stated in one place.</summary>
    public const ModifierKeys ChordModifiers = ModifierKeys.Control | ModifierKeys.Shift;

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
