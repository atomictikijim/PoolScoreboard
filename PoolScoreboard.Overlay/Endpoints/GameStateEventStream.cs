using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using PoolScoreboard.Core;
using PoolScoreboard.Core.Models;

namespace PoolScoreboard.Overlay.Endpoints;

/// <summary>
/// The Server-Sent-Events plumbing the overlay streams share: hold the connection open, replay the
/// current snapshot once, then push a payload per <see cref="GameManager.GameStateChanged"/>. The
/// endpoint supplies only its own serializer, because the two pages map the same state differently.
/// A per-connection channel keeps the writer — the WPF thread, inside GameManager's lock — from
/// touching the response stream, and the snapshot keeps it from reading live state off that lock.
/// </summary>
internal static class GameStateEventStream
{
    public static async Task WriteAsync(HttpContext context, GameManager gameManager, Func<GameState, string> serialize)
    {
        var ct = context.RequestAborted;

        context.Response.Headers.CacheControl = "no-cache";
        context.Response.ContentType = "text/event-stream";

        var channel = Channel.CreateUnbounded<string>();

        void OnGameStateChanged(object? sender, GameStateChangedEventArgs e) =>
            channel.Writer.TryWrite(serialize(e.GameState));

        gameManager.GameStateChanged += OnGameStateChanged;
        try
        {
            await WriteEvent(context.Response, serialize(gameManager.GetStateSnapshot()), ct);

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
    }

    private static async Task WriteEvent(HttpResponse response, string json, CancellationToken ct)
    {
        await response.WriteAsync($"data: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}
