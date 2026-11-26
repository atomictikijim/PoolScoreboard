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
            Results.Content(EmbeddedAsset.Read("cueball.html"), "text/html"));

        app.MapGet("/overlay/cueball/style.css", () =>
            Results.Content(EmbeddedAsset.Read("cueball.css"), "text/css"));

        app.MapGet("/overlay/cueball/app.js", () =>
            Results.Content(EmbeddedAsset.Read("cueball.js"), "application/javascript"));

        app.MapGet("/overlay/api/cueball/state", () =>
            Results.Json(CueBallStateMapper.ToDto(gameManager.GetStateSnapshot()), JsonOptions));

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

        // One snapshot on connect, then a payload per mutation — shared with the scoreboard stream.
        app.MapGet("/overlay/api/cueball/stream", (HttpContext context) =>
            GameStateEventStream.WriteAsync(context, gameManager, Serialize));
    }

    private static string Serialize(PoolScoreboard.Core.Models.GameState state) =>
        JsonSerializer.Serialize(CueBallStateMapper.ToDto(state), JsonOptions);
}

internal record CueBallContactRequest(double X, double Y);
