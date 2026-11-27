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
            // Atomic inside GameManager: reading the current shooter here and assigning it back
            // would let two overlapping button presses act on the same read.
            gameManager.ToggleCurrentShooter();
            return Results.Ok();
        });

        app.MapGet("/api/control/balls/home/solids", () =>
        {
            gameManager.AssignBallGroup(isHome: true, BallGroup.Solids);
            return Results.Ok();
        });

        app.MapGet("/api/control/balls/home/stripes", () =>
        {
            gameManager.AssignBallGroup(isHome: true, BallGroup.Stripes);
            return Results.Ok();
        });

        app.MapGet("/api/control/balls/away/solids", () =>
        {
            gameManager.AssignBallGroup(isHome: false, BallGroup.Solids);
            return Results.Ok();
        });

        app.MapGet("/api/control/balls/away/stripes", () =>
        {
            gameManager.AssignBallGroup(isHome: false, BallGroup.Stripes);
            return Results.Ok();
        });

        app.MapGet("/api/control/balls/{number:int}/toggle", (int number) =>
        {
            if (number is < 1 or > 15)
                return Results.BadRequest("Ball number must be between 1 and 15.");

            gameManager.ToggleBall(number);
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

        // Two routes rather than one toggle: a shot clock is started at the start of a shot and only ever
        // restarted, so a button that sometimes hides it would be a trap mid-match.
        app.MapGet("/api/control/shotclock/start", () =>
        {
            gameManager.StartShotClock();
            return Results.Ok();
        });

        app.MapGet("/api/control/shotclock/clear", () =>
        {
            gameManager.ClearShotClock();
            return Results.Ok();
        });
    }
}
