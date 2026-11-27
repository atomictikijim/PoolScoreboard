using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PoolScoreboard.Core;
using PoolScoreboard.Core.Persistence;
using PoolScoreboard.Overlay.Diagnostics;
using PoolScoreboard.Overlay.Endpoints;

namespace PoolScoreboard.Overlay;

/// <summary>
/// Builds and starts the in-process Kestrel server for the OBS overlay pages and the
/// Stream Deck control API. Bound to 127.0.0.1 only — never reachable off the local machine.
/// </summary>
public static class OverlayHost
{
    /// <summary>The port used when a caller does not name one; the app's own choice lives in its settings.</summary>
    public const int Port = OperatorProfile.DefaultOverlayPort;

    /// <summary>
    /// The host with every page and endpoint mapped but not started, so the real routes can be driven in a test
    /// through <c>Microsoft.AspNetCore.TestHost</c>: no socket, no port, nothing to leave behind on a runner. It also
    /// keeps the mapping in one place, so a route added here is served by both <see cref="Start"/> and the tests.
    /// </summary>
    /// <param name="configureWebHost">
    /// Runs against the web host builder before the app is built: <see cref="Start"/> uses it for the address, the
    /// tests for <c>UseTestServer</c>.
    /// </param>
    public static WebApplication Build(GameManager gameManager, Action<IWebHostBuilder>? configureWebHost = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        // The default providers write to a console this WPF process does not have; this one puts the host's
        // warnings and errors somewhere an operator can read after the fact.
        builder.Logging.AddProvider(new TraceLoggerProvider());

        configureWebHost?.Invoke(builder.WebHost);

        var app = builder.Build();

        ScoreboardEndpoints.Map(app, gameManager);
        CueBallEndpoints.Map(app, gameManager);
        ControlEndpoints.Map(app, gameManager);

        return app;
    }

    public static IHost Start(GameManager gameManager, int port = Port)
    {
        var app = Build(gameManager, webHost => webHost.UseUrls($"http://127.0.0.1:{port}"));

        // Throws when the port is taken; OverlayHostRunner turns that into a logged warning rather than a
        // startup crash. A host that failed to bind has no listener to clean up, so it is simply dropped.
        app.Start();
        return app;
    }
}
