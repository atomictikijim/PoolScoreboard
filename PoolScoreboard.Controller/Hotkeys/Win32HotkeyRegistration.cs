using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace PoolScoreboard.Controller.Hotkeys;

/// <summary>
/// <c>RegisterHotKey</c>, the only way to be told about a chord while another program has focus. A chord another
/// program already owns comes back as a failure rather than being stolen, which is why the session logs it: the key
/// would otherwise simply do nothing and there would be no way to tell why.
/// </summary>
internal sealed class Win32HotkeyRegistration : IHotkeyRegistration
{
    /// <summary>What Windows posts to the window when one of its registered chords is pressed.</summary>
    public const int WmHotkey = 0x0312;

    private const int ErrorHotkeyAlreadyRegistered = 1409;

    public bool TryRegister(IntPtr window, GlobalHotkey hotkey)
    {
        if (RegisterHotKey(
                window,
                hotkey.Id,
                (uint)hotkey.Modifiers,
                (uint)KeyInterop.VirtualKeyFromKey(hotkey.Key)))
        {
            return true;
        }

        // The interesting failure is somebody else's chord: MOD_NOREPEAT and a real error are not worth a line each,
        // but this one explains a key that silently does nothing.
        if (Marshal.GetLastWin32Error() == ErrorHotkeyAlreadyRegistered)
            Trace.WriteLine($"[GlobalHotkeys] Ctrl+Alt+{hotkey.Key} is already registered by another program.");

        return false;
    }

    public void Unregister(IntPtr window, GlobalHotkey hotkey) => UnregisterHotKey(window, hotkey.Id);

    // WPF's ModifierKeys values are the same numbers Win32's MOD_* flags use (Alt 1, Control 2, Shift 4, Win 8).
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
