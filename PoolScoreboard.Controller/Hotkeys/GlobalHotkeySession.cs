using System.Diagnostics;

namespace PoolScoreboard.Controller.Hotkeys;

/// <summary>
/// Hands chords to Windows and takes them back. Behind an interface so the "only while a match is running" rule can be
/// tested without a real registration — a test that took Ctrl+Alt+Q from the machine running it would be a bug of its
/// own.
/// </summary>
internal interface IHotkeyRegistration
{
    /// <summary>Asks Windows to report the chord to that window. False when it is already taken.</summary>
    bool TryRegister(IntPtr window, GlobalHotkey hotkey);

    void Unregister(IntPtr window, GlobalHotkey hotkey);
}

/// <summary>
/// Which global hotkeys are registered right now. Only while a match is running: at setup a chord would score a match
/// nobody is playing, and an operator typing in another program would not expect it.
/// </summary>
internal sealed class GlobalHotkeySession : IDisposable
{
    private readonly IHotkeyRegistration _registration;
    private readonly List<GlobalHotkey> _registered = new();

    private IntPtr _window;
    private bool _active;

    public GlobalHotkeySession(IHotkeyRegistration registration) => _registration = registration;

    /// <summary>What Windows will report to this window at the moment, so what a key can reach is never a guess.</summary>
    public IReadOnlyList<GlobalHotkey> Registered => _registered;

    /// <summary>The hotkey one of those id numbers stands for, or null when it is not ours.</summary>
    public GlobalHotkey? HotkeyFor(int id)
    {
        foreach (var hotkey in _registered)
        {
            if (hotkey.Id == id)
                return hotkey;
        }

        return null;
    }

    /// <summary>Tells the session which window Windows should report to. Registers at once if a match is already on.</summary>
    public void Attach(IntPtr window)
    {
        _window = window;
        Sync();
    }

    public void SetActive(bool active)
    {
        _active = active;
        Sync();
    }

    public void Dispose()
    {
        _active = false;
        Sync();
    }

    private void Sync()
    {
        if (_active && _window != IntPtr.Zero)
        {
            foreach (var hotkey in GlobalHotkeys.Default)
            {
                if (!_registered.Contains(hotkey) && _registration.TryRegister(_window, hotkey))
                    _registered.Add(hotkey);
            }

            return;
        }

        foreach (var hotkey in _registered.ToArray())
        {
            _registration.Unregister(_window, hotkey);
            _registered.Remove(hotkey);
        }
    }
}
