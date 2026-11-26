using System.IO;
using Microsoft.Extensions.Logging;
using PoolScoreboard.Controller.Diagnostics;
using PoolScoreboard.Overlay.Diagnostics;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// The bridge's whole job is the file the operator reads afterwards, so these assert against a real
/// <see cref="TraceLogFile"/> in a temp folder rather than a mock.
/// </summary>
public sealed class TraceLoggerProviderTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "poolscoreboard-host-log-tests", Guid.NewGuid().ToString("n"));

    private string LogPath => Path.Combine(_root, "controller.log");

    [Fact]
    public void HostWarningsReachTheLogFileWithTheirCategory()
    {
        using (var log = new TraceLogFile(LogPath))
        {
            log.Attach();

            using var provider = new TraceLoggerProvider();
            var logger = provider.CreateLogger("Microsoft.AspNetCore.Server.Kestrel");
            logger.LogWarning("Failed to bind to address http://127.0.0.1:51234: address already in use.");
        }

        var text = File.ReadAllText(LogPath);
        Assert.Contains("Failed to bind to address", text);
        Assert.Contains("Microsoft.AspNetCore.Server.Kestrel", text);
    }

    [Fact]
    public void HostErrorsReachTheLogFileWithTheException()
    {
        using (var log = new TraceLogFile(LogPath))
        {
            log.Attach();

            using var provider = new TraceLoggerProvider();
            var logger = provider.CreateLogger("Microsoft.AspNetCore.Diagnostics");
            logger.LogError(new InvalidOperationException("boom"), "control endpoint failed");
        }

        var text = File.ReadAllText(LogPath);
        Assert.Contains("control endpoint failed", text);
        Assert.Contains("InvalidOperationException", text);
        Assert.Contains("Error", text);
    }

    [Fact]
    public void InformationAndDebugAreDropped()
    {
        using (var log = new TraceLogFile(LogPath))
        {
            log.Attach();

            using var provider = new TraceLoggerProvider();
            var logger = provider.CreateLogger("Microsoft.AspNetCore.Hosting");

            Assert.False(logger.IsEnabled(LogLevel.Information));
            logger.LogInformation("request starting noise");
            logger.LogDebug("connection noise");
        }

        var text = File.ReadAllText(LogPath);
        Assert.DoesNotContain("request starting noise", text);
        Assert.DoesNotContain("connection noise", text);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
