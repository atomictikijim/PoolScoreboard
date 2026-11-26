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
            Results.Content(EmbeddedAsset.Read("scoreboard.html"), "text/html"));

        app.MapGet("/overlay/scoreboard/style.css", () =>
            Results.Content(EmbeddedAsset.Read("scoreboard.css"), "text/css"));

        app.MapGet("/overlay/scoreboard/app.js", () =>
            Results.Content(EmbeddedAsset.Read("scoreboard.js"), "application/javascript"));

        // A snapshot, not the live instance: the mapper copies the pocketed-ball set, and the UI
        // thread (or a concurrent control-API call) can be mutating that set at the same time.
        app.MapGet("/overlay/api/scoreboard/state", () =>
            Results.Json(ScoreboardStateMapper.ToDto(gameManager.GetStateSnapshot()), JsonOptions));

        // End-cap icons are served here instead of riding inside the state payload: the SSE
        // snapshot goes out on every operator action, so an embedded base64 logo meant re-sending
        // it on every score change. The `v` query is cache-busting only — the bytes always come
        // from the player's current icon, so an old `v` still returns current artwork.
        app.MapGet("/overlay/api/players/{id}/icon", (string id) =>
        {
            // Snapshot: this runs on a Kestrel thread while the UI can be assigning a new icon, and
            // reading the live player could pair one field with another player's/version's.
            var state = gameManager.GetStateSnapshot();
            var player = state.Player1.Id == id ? state.Player1
                : state.Player2.Id == id ? state.Player2
                : null;

            if (player == null || !EndCapIconDataUri.TryDecode(player.EndCapIcon, out var bytes, out var contentType))
                return Results.NotFound();

            return Results.Bytes(bytes, contentType);
        });

        // One snapshot on connect, then a payload per mutation — shared with the cue-ball stream.
        app.MapGet("/overlay/api/scoreboard/stream", (HttpContext context) =>
            GameStateEventStream.WriteAsync(context, gameManager, Serialize));
    }

    private static string Serialize(PoolScoreboard.Core.Models.GameState state) =>
        JsonSerializer.Serialize(ScoreboardStateMapper.ToDto(state), JsonOptions);
}
