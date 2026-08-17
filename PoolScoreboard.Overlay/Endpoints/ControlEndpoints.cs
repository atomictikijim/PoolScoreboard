using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using PoolScoreboard.Core;
using PoolScoreboard.Core.Enums;

namespace PoolScoreboard.Overlay.Endpoints;

/// <summary>
/// Local HTTP endpoints for Stream Deck buttons, hit directly via Stream Deck's built-in
/// "Website" action (background mode) rather than a custom Elgato SDK plugin — see
/// CLAUDE.md's Stream Deck Integration section. Mapped as GET routes since that built-in
/// action has no way to configure a request body or a non-GET verb.
/// </summary>
internal static class ControlEndpoints
{
    public static void Map(WebApplication app, GameManager gameManager)
    {
        app.MapGet("/api/control/score/home/add", () =>
        {
            gameManager.AddPoint(isPlayer1: true);
            return Results.Ok();
        });

        app.MapGet("/api/control/score/away/add", () =>
        {
            gameManager.AddPoint(isPlayer1: false);
            return Results.Ok();
        });

        app.MapGet("/api/control/score/home/subtract", () =>
        {
            gameManager.SubtractPoint(isPlayer1: true);
            return Results.Ok();
        });

        app.MapGet("/api/control/score/away/subtract", () =>
        {
            gameManager.SubtractPoint(isPlayer1: false);
            return Results.Ok();
        });

        app.MapGet("/api/control/score/undo", () =>
        {
            gameManager.UndoPoint();
            return Results.Ok();
        });

        app.MapGet("/api/control/shooter/toggle", () =>
        {
            var state = gameManager.GetCurrentGameState();
            var next = ReferenceEquals(state.CurrentShooter, state.Player1) ? state.Player2 : state.Player1;
            gameManager.SetCurrentShooter(next);
            return Results.Ok();
        });

        app.MapGet("/api/control/balls/home/solids", () =>
        {
            AssignGroup(gameManager, isHome: true, BallGroup.Solids);
            return Results.Ok();
        });

        app.MapGet("/api/control/balls/home/stripes", () =>
        {
            AssignGroup(gameManager, isHome: true, BallGroup.Stripes);
            return Results.Ok();
        });

        app.MapGet("/api/control/balls/away/solids", () =>
        {
            AssignGroup(gameManager, isHome: false, BallGroup.Solids);
            return Results.Ok();
        });

        app.MapGet("/api/control/balls/away/stripes", () =>
        {
            AssignGroup(gameManager, isHome: false, BallGroup.Stripes);
            return Results.Ok();
        });

        app.MapGet("/api/control/balls/{number:int}/toggle", (int number) =>
        {
            if (number is < 1 or > 15)
                return Results.BadRequest("Ball number must be between 1 and 15.");

            var state = gameManager.GetCurrentGameState();
            if (state.PocketedBalls.Contains(number))
                gameManager.UnpocketBall(number);
            else
                gameManager.PocketBall(number);
            return Results.Ok();
        });

        app.MapGet("/api/control/rack/new", () =>
        {
            gameManager.ResetBalls();
            return Results.Ok();
        });

        app.MapGet("/api/control/match/reset", () =>
        {
            gameManager.ResetGame();
            return Results.Ok();
        });
    }

    private static void AssignGroup(GameManager gameManager, bool isHome, BallGroup group)
    {
        var state = gameManager.GetCurrentGameState();
        var player = isHome ? state.Player1 : state.Player2;
        gameManager.AssignBallGroup(player, group);
    }
}
