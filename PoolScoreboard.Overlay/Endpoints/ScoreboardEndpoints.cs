using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PoolScoreboard.Core;
using PoolScoreboard.Overlay.Models;

namespace PoolScoreboard.Overlay.Endpoints;

internal static class ScoreboardEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Map(WebApplication app, GameManager gameManager)
    {
        app.MapGet("/overlay/scoreboard", () =>
            Results.Content(ReadEmbeddedAsset("scoreboard.html"), "text/html"));

        app.MapGet("/overlay/scoreboard/style.css", () =>
            Results.Content(ReadEmbeddedAsset("scoreboard.css"), "text/css"));

        app.MapGet("/overlay/scoreboard/app.js", () =>
            Results.Content(ReadEmbeddedAsset("scoreboard.js"), "application/javascript"));

        app.MapGet("/overlay/api/scoreboard/state", () =>
            Results.Json(ScoreboardStateMapper.ToDto(gameManager.GetCurrentGameState()), JsonOptions));

        // End-cap icons are served here instead of riding inside the state payload: the SSE
        // snapshot goes out on every operator action, so an embedded base64 logo meant re-sending
        // it on every score change. The `v` query is cache-busting only — the bytes always come
        // from the player's current icon, so an old `v` still returns current artwork.
        app.MapGet("/overlay/api/players/{id}/icon", (string id) =>
        {
            var state = gameManager.GetCurrentGameState();
            var player = state.Player1.Id == id ? state.Player1
                : state.Player2.Id == id ? state.Player2
                : null;

            if (player == null || !EndCapIconDataUri.TryDecode(player.EndCapIcon, out var bytes, out var contentType))
                return Results.NotFound();

            return Results.Bytes(bytes, contentType);
        });

        app.MapGet("/overlay/api/scoreboard/stream", async (HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.ContentType = "text/event-stream";

            var channel = System.Threading.Channels.Channel.CreateUnbounded<string>();

            void OnGameStateChanged(object? sender, GameStateChangedEventArgs e) =>
                channel.Writer.TryWrite(Serialize(e.GameState));

            gameManager.GameStateChanged += OnGameStateChanged;
            try
            {
                await WriteEvent(context.Response, Serialize(gameManager.GetCurrentGameState()), ct);

                await foreach (var json in channel.Reader.ReadAllAsync(ct))
                {
                    await WriteEvent(context.Response, json, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected (OBS scene switch, browser source reload, etc.) — expected.
            }
            finally
            {
                gameManager.GameStateChanged -= OnGameStateChanged;
            }
        });
    }

    private static string Serialize(PoolScoreboard.Core.Models.GameState state) =>
        JsonSerializer.Serialize(ScoreboardStateMapper.ToDto(state), JsonOptions);

    private static async Task WriteEvent(HttpResponse response, string json, CancellationToken ct)
    {
        await response.WriteAsync($"data: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    private static string ReadEmbeddedAsset(string fileName)
    {
        var assembly = typeof(ScoreboardEndpoints).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
