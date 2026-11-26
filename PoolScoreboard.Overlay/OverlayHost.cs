using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PoolScoreboard.Core;
using PoolScoreboard.Overlay.Diagnostics;
using PoolScoreboard.Overlay.Endpoints;

namespace PoolScoreboard.Overlay;

/// <summary>
/// Builds and starts the in-process Kestrel server for the OBS overlay pages and the
/// Stream Deck control API. Bound to 127.0.0.1 only — never reachable off the local machine.
/// </summary>
public static class OverlayHost
{
    public const int Port = 51234;

    public static IHost Start(GameManager gameManager, int port = Port)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        // The default providers write to a console this WPF process does not have; this one puts the host's
        // warnings and errors somewhere an operator can read after the fact.
        builder.Logging.AddProvider(new TraceLoggerProvider());

        var app = builder.Build();

        ScoreboardEndpoints.Map(app, gameManager);
        CueBallEndpoints.Map(app, gameManager);
        ControlEndpoints.Map(app, gameManager);

        // Throws when the port is taken; OverlayHostRunner turns that into a logged warning rather than a
        // startup crash. A host that failed to bind has no listener to clean up, so it is simply dropped.
        app.Start();
        return app;
    }
}
