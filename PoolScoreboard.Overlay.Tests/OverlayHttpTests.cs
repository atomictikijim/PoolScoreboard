using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using PoolScoreboard.Core;
using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Models;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Overlay.Tests;

/// <summary>
/// The overlay over HTTP, against the endpoints the app really serves. The guards in
/// <c>PoolScoreboard.Controller.Tests</c> read source text and prove the strings agree; this drives the routes, so a
/// changed status code, content type or payload field fails here instead of being found in OBS. Nothing binds a
/// socket: <see cref="OverlayHost.Build"/> is handed <c>UseTestServer</c>.
/// <para>
/// The payload assertions are written against the pages, not against a hand-kept list: every <c>state.…</c> path the
/// asset reads has to resolve in the JSON, and the asset is read out of this assembly — the same bytes
/// <c>EmbeddedAsset</c> serves — so renaming a DTO property fails with the path the page wanted.
/// </para>
/// </summary>
public sealed class OverlayHttpTests : IAsyncLifetime
{
    /// <summary>A page reading <c>state.home.score</c> needs a <c>home</c> object with a <c>score</c>.</summary>
    private const string ScoreboardState = "/overlay/api/scoreboard/state";

    private static readonly Regex StatePath = new(@"\bstate\.([A-Za-z0-9_][A-Za-z0-9_.]*)", RegexOptions.Compiled);

    private GameManager _gameManager = null!;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _gameManager = new GameManager();
        _gameManager.InitializeGame(GameType.NineBall, RaceToMode.Single);

        _app = OverlayHost.Build(_gameManager, webHost => webHost.UseTestServer());
        await _app.StartAsync();

        _client = _app.GetTestClient();

        // A hang in CI is worse than a failed assertion: bound every request.
        _client.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Theory]
    [InlineData("/overlay/scoreboard", "text/html")]
    [InlineData("/overlay/scoreboard/style.css", "text/css")]
    [InlineData("/overlay/scoreboard/app.js", "application/javascript")]
    [InlineData("/overlay/cueball", "text/html")]
    [InlineData("/overlay/cueball/style.css", "text/css")]
    [InlineData("/overlay/cueball/app.js", "application/javascript")]
    public async Task EveryPageAndAssetIsServedWithItsContentType(string path, string contentType)
    {
        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task TheStatePayloadCarriesEveryPathTheScoreboardPageReads()
    {
        var payload = await GetJson(ScoreboardState);
        var read = PathsReadBy("scoreboard.js");

        Assert.NotEmpty(read);

        var missing = read.Where(path => !TryResolve(payload, path)).ToArray();

        Assert.True(
            missing.Length == 0,
            $"the scoreboard page reads these, but the payload no longer carries them: {string.Join(", ", missing)}");
    }

    [Fact]
    public async Task TheCueBallPayloadCarriesEveryPathTheCueBallPageReads()
    {
        var payload = await GetJson("/overlay/api/cueball/state");
        var read = PathsReadBy("cueball.js");

        Assert.NotEmpty(read);

        var missing = read.Where(path => !TryResolve(payload, path)).ToArray();

        Assert.True(
            missing.Length == 0,
            $"the cue-ball page reads these, but the payload no longer carries them: {string.Join(", ", missing)}");
    }

    [Theory]
    [InlineData("/overlay/api/scoreboard/stream", "scoreboard.js")]
    [InlineData("/overlay/api/cueball/stream", "cueball.js")]
    public async Task TheFirstFrameOfTheStreamCarriesThePayloadThePageReads(string streamPath, string asset)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var response = await _client.GetAsync(
            streamPath,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        // The page gets its first picture from this frame, not from the snapshot route: EventSource only ever sees
        // what the stream sends, so a payload that resolves on /state and not here is still a blank overlay.
        var payload = FramePayload(await ReadFirstFrame(response, timeout.Token));

        var read = PathsReadBy(asset);
        Assert.NotEmpty(read);

        var missing = read.Where(path => !TryResolve(payload, path)).ToArray();

        Assert.True(
            missing.Length == 0,
            $"the first frame of {streamPath} does not carry what the page reads: {string.Join(", ", missing)}");
    }

    [Fact]
    public async Task AControlCallPushesTheNextStateOnTheOpenStream()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var response = await _client.GetAsync(
            "/overlay/api/scoreboard/stream",
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
        var frames = new EventFrames(body);

        var before = HomeScore(FramePayload(await frames.ReadFrameAsync(timeout.Token)));

        (await _client.GetAsync("/api/control/score/home/add")).EnsureSuccessStatusCode();

        // Without this the page shows the score from connect time until something forces a reload: the route that
        // changes state and the frame that reaches OBS are wired together only by GameStateChanged.
        var after = HomeScore(FramePayload(await frames.ReadFrameAsync(timeout.Token)));

        Assert.Equal(before + 1, after);
    }

    [Fact]
    public async Task TheControlApiMovesTheStateThePagesRender()
    {
        var before = HomeScore(await GetJson(ScoreboardState));

        (await _client.GetAsync("/api/control/score/home/add")).EnsureSuccessStatusCode();

        Assert.Equal(before + 1, HomeScore(await GetJson(ScoreboardState)));
    }

    [Fact]
    public async Task TheShotClockRoutesStartAndClearTheClockThePageCountsDown()
    {
        (await _client.GetAsync("/api/control/shotclock/start")).EnsureSuccessStatusCode();

        var started = await GetJson(ScoreboardState);
        var endsAt = started.GetProperty("shotClockEndsAt");

        // The page counts down from this instant on its own, so a deadline in the past would show 0:00 for the
        // whole shot, and a value it cannot parse would leave the clock stuck at whatever it last read.
        Assert.Equal(JsonValueKind.String, endsAt.ValueKind);
        Assert.True(
            DateTimeOffset.TryParse(
                endsAt.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var deadline),
            $"the page cannot parse {endsAt.GetString()} as a deadline");
        Assert.True(deadline > DateTimeOffset.UtcNow, "the deadline is already past");
        Assert.Equal(GameState.DefaultShotClockSeconds, started.GetProperty("shotClockSeconds").GetInt32());

        (await _client.GetAsync("/api/control/shotclock/clear")).EnsureSuccessStatusCode();

        var cleared = await GetJson(ScoreboardState);
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("shotClockEndsAt").ValueKind);
    }

    [Fact]
    public async Task TheShotClockExtensionMovesTheDeadlineOnceForEachShooter()
    {
        (await _client.GetAsync("/api/control/shotclock/start")).EnsureSuccessStatusCode();
        var started = ShotClockDeadline(await GetJson(ScoreboardState));

        (await _client.GetAsync("/api/control/shotclock/extension")).EnsureSuccessStatusCode();
        var extended = ShotClockDeadline(await GetJson(ScoreboardState));

        // The page counts down from the deadline, so an extension has to move that instant forward by exactly the
        // extension length — not start the shot over, which would hand back the time already spent.
        Assert.Equal(GameState.DefaultShotClockExtensionSeconds, (extended - started).TotalSeconds);

        (await _client.GetAsync("/api/control/shotclock/extension")).EnsureSuccessStatusCode();
        Assert.Equal(extended, ShotClockDeadline(await GetJson(ScoreboardState)));

        // The other player still has theirs: the shooter change restarts the clock, and this extension adds to the
        // fresh deadline.
        (await _client.GetAsync("/api/control/shooter/toggle")).EnsureSuccessStatusCode();
        var restarted = ShotClockDeadline(await GetJson(ScoreboardState));

        (await _client.GetAsync("/api/control/shotclock/extension")).EnsureSuccessStatusCode();
        Assert.Equal(
            GameState.DefaultShotClockExtensionSeconds,
            (ShotClockDeadline(await GetJson(ScoreboardState)) - restarted).TotalSeconds);
    }

    [Fact]
    public async Task TheShotClockDisplayModeReachesThePayloadThePageReads()
    {
        Assert.Equal("Time", (await GetJson(ScoreboardState)).GetProperty("shotClockDisplay").GetString());

        _gameManager.SetShotClockDisplay(ShotClockDisplay.Line);

        // The page picks which of its two drawings to show from this, so a mode that never reached the payload would
        // leave the operator's toggle doing nothing on the overlay.
        Assert.Equal("Line", (await GetJson(ScoreboardState)).GetProperty("shotClockDisplay").GetString());
    }

    [Fact]
    public async Task TheStaleCueReachesThePayloadThePageReads()
    {
        Assert.False((await GetJson(ScoreboardState)).GetProperty("staleCueEnabled").GetBoolean());

        _gameManager.SetStaleCue(true);

        // The page only marks itself when this arrives, and only learns it from a frame: a cue that never reached the
        // payload would leave a dead overlay looking live forever.
        Assert.True((await GetJson(ScoreboardState)).GetProperty("staleCueEnabled").GetBoolean());
    }

    private static DateTimeOffset ShotClockDeadline(JsonElement state) =>
        DateTimeOffset.Parse(
            state.GetProperty("shotClockEndsAt").GetString()!,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    [Fact]
    public async Task TheCueBallPageCanPlaceTheContactPoint()
    {
        (await _client.PostAsJsonAsync("/overlay/api/cueball/contact", new { x = 0.25, y = 0.75 }))
            .EnsureSuccessStatusCode();

        var payload = await GetJson("/overlay/api/cueball/state");

        // The property names in this request and this payload are what cueball.js sends and reads.
        Assert.Equal(0.25, payload.GetProperty("x").GetDouble(), 3);
        Assert.Equal(0.75, payload.GetProperty("y").GetDouble(), 3);
    }

    // Every /api/control route, read back through the payload the pages render. A Stream Deck button has no screen
    // to check itself against, so an effect that is absent, on the wrong side or the wrong sign has to fail here.

    [Theory]
    [InlineData("/api/control/score/home/add", true, 1)]
    [InlineData("/api/control/score/away/add", false, 1)]
    [InlineData("/api/control/score/home/subtract", true, -1)]
    [InlineData("/api/control/score/away/subtract", false, -1)]
    public async Task EachScoreControlMovesOnlyTheSideItNames(string path, bool home, int delta)
    {
        // Both sides start on one, so a no-op, a flipped sign and the wrong side are all distinguishable.
        _gameManager.AddPoint(isPlayer1: true);
        _gameManager.AddPoint(isPlayer1: false);

        (await _client.GetAsync(path)).EnsureSuccessStatusCode();

        var state = await GetJson(ScoreboardState);
        Assert.Equal(1 + (home ? delta : 0), PlayerScore(state, isHome: true));
        Assert.Equal(1 + (home ? 0 : delta), PlayerScore(state, isHome: false));
    }

    [Fact]
    public async Task UndoRemovesThePointTheScoreboardShowsLast()
    {
        _gameManager.AddPoint(isPlayer1: true);
        _gameManager.AddPoint(isPlayer1: true);
        _gameManager.AddPoint(isPlayer1: false);

        (await _client.GetAsync("/api/control/score/undo")).EnsureSuccessStatusCode();

        var afterOne = await GetJson(ScoreboardState);
        Assert.Equal(2, PlayerScore(afterOne, isHome: true));
        Assert.Equal(0, PlayerScore(afterOne, isHome: false)); // the away point was last, not a home one

        (await _client.GetAsync("/api/control/score/undo")).EnsureSuccessStatusCode();

        Assert.Equal(1, PlayerScore(await GetJson(ScoreboardState), isHome: true));
    }

    [Fact]
    public async Task TheShooterControlFlipsTheSideThePageHighlights()
    {
        var before = (await GetJson(ScoreboardState)).GetProperty("homeIsCurrentShooter").GetBoolean();

        (await _client.GetAsync("/api/control/shooter/toggle")).EnsureSuccessStatusCode();
        Assert.Equal(!before, (await GetJson(ScoreboardState)).GetProperty("homeIsCurrentShooter").GetBoolean());

        (await _client.GetAsync("/api/control/shooter/toggle")).EnsureSuccessStatusCode();
        Assert.Equal(before, (await GetJson(ScoreboardState)).GetProperty("homeIsCurrentShooter").GetBoolean());
    }

    [Theory]
    [InlineData("/api/control/balls/home/solids", true, "Solids")]
    [InlineData("/api/control/balls/home/stripes", true, "Stripes")]
    [InlineData("/api/control/balls/away/solids", false, "Solids")]
    [InlineData("/api/control/balls/away/stripes", false, "Stripes")]
    public async Task EachBallGroupControlAssignsTheGroupAndItsComplement(string path, bool home, string group)
    {
        // Groups exist only in 8-ball, and the app opens on 9-ball.
        _gameManager.InitializeGame(GameType.EightBall, RaceToMode.Single);

        (await _client.GetAsync(path)).EnsureSuccessStatusCode();

        var state = await GetJson(ScoreboardState);

        Assert.Equal(group, PlayerGroup(state, isHome: home));
        Assert.Equal(group == "Solids" ? "Stripes" : "Solids", PlayerGroup(state, isHome: !home));
    }

    [Fact]
    public async Task TheBallRouteStopsTheClockWhenItPocketsTheMoneyBall()
    {
        // The fixture's match is 9-ball, where the 9 is the money ball.
        (await _client.GetAsync("/api/control/shotclock/start")).EnsureSuccessStatusCode();
        Assert.NotEqual(JsonValueKind.Null, (await GetJson(ScoreboardState)).GetProperty("shotClockEndsAt").ValueKind);

        (await _client.GetAsync("/api/control/balls/9/toggle")).EnsureSuccessStatusCode();

        // The page draws the clock only while a deadline arrives, so a stop it can see is a null deadline: a track
        // left counting down would be timing a rack that is already over.
        Assert.Equal(JsonValueKind.Null, (await GetJson(ScoreboardState)).GetProperty("shotClockEndsAt").ValueKind);
    }

    [Fact]
    public async Task TheBallTogglePocketsTheBallAndUnpocketsItAgain()
    {
        (await _client.GetAsync("/api/control/balls/5/toggle")).EnsureSuccessStatusCode();

        Assert.Equal(new[] { 5 }, PocketedBalls(await GetJson(ScoreboardState)));

        (await _client.GetAsync("/api/control/balls/5/toggle")).EnsureSuccessStatusCode();

        Assert.Empty(PocketedBalls(await GetJson(ScoreboardState)));
    }

    [Fact]
    public async Task ABallNumberOutsideTheRackIsRejected()
    {
        var response = await _client.GetAsync("/api/control/balls/16/toggle");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(PocketedBalls(await GetJson(ScoreboardState)));
    }

    [Fact]
    public async Task ANewRackLeavesTheClockIdleAtFullLengthWithoutRunningIt()
    {
        (await _client.GetAsync("/api/control/shotclock/start")).EnsureSuccessStatusCode();
        Assert.NotEqual(JsonValueKind.Null, (await GetJson(ScoreboardState)).GetProperty("shotClockEndsAt").ValueKind);

        (await _client.GetAsync("/api/control/rack/new")).EnsureSuccessStatusCode();

        // Not counting, but still on screen: the page draws the clock from these two fields together, so a rack that
        // left both empty would blank it rather than show the new rack's allowance.
        var rack = await GetJson(ScoreboardState);
        Assert.Equal(JsonValueKind.Null, rack.GetProperty("shotClockEndsAt").ValueKind);
        Assert.True(rack.GetProperty("shotClockArmed").GetBoolean());

        (await _client.GetAsync("/api/control/shotclock/start")).EnsureSuccessStatusCode();

        var started = await GetJson(ScoreboardState);
        Assert.False(started.GetProperty("shotClockArmed").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, started.GetProperty("shotClockEndsAt").ValueKind);
    }

    [Fact]
    public async Task ANewRackClearsTheBallTrackerButKeepsTheScore()
    {
        _gameManager.AddPoint(isPlayer1: true);
        (await _client.GetAsync("/api/control/balls/3/toggle")).EnsureSuccessStatusCode();
        (await _client.GetAsync("/api/control/balls/7/toggle")).EnsureSuccessStatusCode();

        (await _client.GetAsync("/api/control/rack/new")).EnsureSuccessStatusCode();

        var state = await GetJson(ScoreboardState);
        Assert.Empty(PocketedBalls(state));
        Assert.Equal(1, PlayerScore(state, isHome: true)); // a new rack is not a new match
    }

    [Fact]
    public async Task ResetMatchClearsTheScoreboardThePageShows()
    {
        _gameManager.InitializeGame(GameType.EightBall, RaceToMode.Single);
        _gameManager.AddPoint(isPlayer1: true);
        _gameManager.AddPoint(isPlayer1: false);
        _gameManager.AssignBallGroup(isHome: true, BallGroup.Solids);
        _gameManager.PocketBall(9);

        (await _client.GetAsync("/api/control/match/reset")).EnsureSuccessStatusCode();

        var state = await GetJson(ScoreboardState);

        Assert.Equal(0, PlayerScore(state, isHome: true));
        Assert.Equal(0, PlayerScore(state, isHome: false));
        Assert.Equal("Unassigned", PlayerGroup(state, isHome: true));
        Assert.Equal("Unassigned", PlayerGroup(state, isHome: false));
        Assert.Empty(PocketedBalls(state));
        Assert.False(state.GetProperty("isGameActive").GetBoolean());
        Assert.Equal(JsonValueKind.Null, state.GetProperty("winnerName").ValueKind);

        // A reset ends the match, it does not restyle the show: the overlay keeps previewing what was chosen.
        Assert.Equal("EightBall", state.GetProperty("gameType").GetString());
    }

    [Fact]
    public async Task APlayerIconIsServedAsItsOwnBytes()
    {
        byte[] bytes = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03 };
        _gameManager.SetPlayerDetails(
            isHome: true,
            name: "Home",
            teamName: string.Empty,
            raceToTarget: 5,
            endCapIcon: EndCapIconData.ToDataUri("image/png", bytes));

        var playerId = _gameManager.GetStateSnapshot().Player1.Id;
        var response = await _client.GetAsync($"/overlay/api/players/{playerId}/icon");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(bytes, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AnUnknownPlayerHasNoIcon()
    {
        var response = await _client.GetAsync("/overlay/api/players/nobody/icon");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<JsonElement> GetJson(string path)
    {
        var response = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static int HomeScore(JsonElement payload) => PlayerScore(payload, isHome: true);

    private static int PlayerScore(JsonElement state, bool isHome) =>
        state.GetProperty(isHome ? "home" : "away").GetProperty("score").GetInt32();

    private static string PlayerGroup(JsonElement state, bool isHome) =>
        state.GetProperty(isHome ? "home" : "away").GetProperty("ballGroup").GetString()!;

    private static int[] PocketedBalls(JsonElement state) =>
        state.GetProperty("pocketedBalls").EnumerateArray().Select(ball => ball.GetInt32()).ToArray();

    private static async Task<string> ReadFirstFrame(HttpResponseMessage response, CancellationToken ct)
    {
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        return await new EventFrames(body).ReadFrameAsync(ct);
    }

    /// <summary>The JSON an SSE frame carries, which is what the page's <c>JSON.parse(event.data)</c> sees.</summary>
    private static JsonElement FramePayload(string frame)
    {
        Assert.StartsWith("data: ", frame);

        using var document = JsonDocument.Parse(frame["data: ".Length..]);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Reads SSE frames off a live response body, one at a time. It keeps whatever arrived past the frame it returned,
    /// because a single read can span two frames and dropping the tail would lose the next event — the thing the
    /// push-on-change test is looking for. Bytes, not text, so a character is never decoded across a read boundary.
    /// </summary>
    private sealed class EventFrames
    {
        private readonly Stream _body;
        private readonly byte[] _chunk = new byte[1024];
        private MemoryStream _pending = new();

        internal EventFrames(Stream body) => _body = body;

        internal async Task<string> ReadFrameAsync(CancellationToken ct)
        {
            while (true)
            {
                var buffered = _pending.ToArray();
                var end = IndexOfBlankLine(buffered);
                if (end >= 0)
                {
                    var frame = Encoding.UTF8.GetString(buffered, 0, end);

                    _pending = new MemoryStream();
                    _pending.Write(buffered, end + 2, buffered.Length - end - 2);
                    return frame;
                }

                var read = await _body.ReadAsync(_chunk, ct);
                Assert.True(read > 0, "the stream ended before it sent the frame under test");

                _pending.Write(_chunk, 0, read);
            }
        }

        private static int IndexOfBlankLine(byte[] bytes)
        {
            for (var i = 0; i + 1 < bytes.Length; i++)
            {
                if (bytes[i] == '\n' && bytes[i + 1] == '\n')
                    return i;
            }

            return -1;
        }
    }

    /// <summary>The <c>state.…</c> paths a page reads, from the asset this assembly embeds and serves.</summary>
    private static string[] PathsReadBy(string asset)
    {
        var assembly = typeof(OverlayHost).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(asset, StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);

        return StatePath.Matches(reader.ReadToEnd())
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool TryResolve(JsonElement element, string path)
    {
        foreach (var segment in path.Split('.'))
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out var next))
                return false;

            element = next;
        }

        return true;
    }
}
