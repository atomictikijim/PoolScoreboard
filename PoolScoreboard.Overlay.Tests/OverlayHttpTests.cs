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
        var payload = await GetJson("/overlay/api/scoreboard/state");
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
        var before = HomeScore(await GetJson("/overlay/api/scoreboard/state"));

        (await _client.GetAsync("/api/control/score/home/add")).EnsureSuccessStatusCode();

        Assert.Equal(before + 1, HomeScore(await GetJson("/overlay/api/scoreboard/state")));
    }

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

    private static int HomeScore(JsonElement payload) =>
        payload.GetProperty("home").GetProperty("score").GetInt32();

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
