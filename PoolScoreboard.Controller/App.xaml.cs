using System.Windows;
using Microsoft.Extensions.Hosting;
using PoolScoreboard.Core;
using PoolScoreboard.Overlay;

namespace PoolScoreboard.Controller;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    /// <summary>
    /// The single <see cref="GameManager"/> instance for the app's lifetime, shared between the
    /// WPF UI and the in-process Overlay host so both always read/write the same game state.
    /// </summary>
    public GameManager SharedGameManager { get; } = new();

    private IHost? _overlayHost;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _overlayHost = OverlayHost.Start(SharedGameManager);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_overlayHost != null)
        {
            _overlayHost.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            _overlayHost.Dispose();
        }

        base.OnExit(e);
    }
}
