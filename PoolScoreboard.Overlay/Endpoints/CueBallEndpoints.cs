using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PoolScoreboard.Core;
using PoolScoreboard.Overlay.Models;

namespace PoolScoreboard.Overlay.Endpoints;

internal static class CueBallEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Map(WebApplication app, GameManager gameManager)
    {
        app.MapGet("/overlay/cueball", () =>
            Results.Content(ReadEmbeddedAsset("cueball.html"), "text/html"));

        app.MapGet("/overlay/cueball/style.css", () =>
            Results.Content(ReadEmbeddedAsset("cueball.css"), "text/css"));

        app.MapGet("/overlay/cueball/app.js", () =>
            Results.Content(ReadEmbeddedAsset("cueball.js"), "application/javascript"));

        app.MapGet("/overlay/api/cueball/state", () =>
            Results.Json(CueBallStateMapper.ToDto(gameManager.GetCurrentGameState()), JsonOptions));

        app.MapPost("/overlay/api/cueball/contact", (CueBallContactRequest request) =>
        {
            gameManager.SetCueBallSpin(request.X, request.Y);
            return Results.Ok();
        });

        app.MapPost("/overlay/api/cueball/clear", () =>
        {
            gameManager.ClearCueBallSpin();
            return Results.Ok();
        });

        app.MapGet("/overlay/api/cueball/stream", async (HttpContext context, CancellationToken ct) =>
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
        JsonSerializer.Serialize(CueBallStateMapper.ToDto(state), JsonOptions);

    private static async Task WriteEvent(HttpResponse response, string json, CancellationToken ct)
    {
        await response.WriteAsync($"data: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    private static string ReadEmbeddedAsset(string fileName)
    {
        var assembly = typeof(CueBallEndpoints).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

internal record CueBallContactRequest(double X, double Y);
