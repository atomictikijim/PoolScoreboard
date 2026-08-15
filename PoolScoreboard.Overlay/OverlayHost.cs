using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PoolScoreboard.Core;
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

        var app = builder.Build();

        ScoreboardEndpoints.Map(app, gameManager);
        CueBallEndpoints.Map(app, gameManager);

        app.Start();
        return app;
    }
}
