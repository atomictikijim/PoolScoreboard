using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using PoolScoreboard.Controller;
using PoolScoreboard.Controller.Diagnostics;
using PoolScoreboard.Core;
using PoolScoreboard.Overlay;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// The regression these cover: the overlay host was stopped on the WPF UI thread, ASP.NET Core captured
/// that thread's <see cref="SynchronizationContext"/>, and the shutdown continuations were queued to a
/// Dispatcher that the same thread was blocking on. The stop never completed — not even its timeout —
/// and the process lingered after its window closed.
/// </summary>
public class OverlayHostRunnerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(150);

    [Fact]
    public void StopAndDispose_StopsAHostThatCapturesTheCallersSynchronizationContext()
    {
        // Stands in for the WPF Dispatcher: posting a continuation queues it and never runs it, exactly
        // like a UI thread that is blocked inside OnExit.
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new StalledSynchronizationContext());
        try
        {
            var host = new FakeHost(async () =>
            {
                await Task.Yield();          // captures the ambient context
                await Task.Delay(10);        // then finishes, if that context ever resumes it
            });

            var stopped = OverlayHostRunner.StopAndDispose(host, Timeout);

            Assert.True(stopped);
            Assert.True(host.Disposed);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public void StopAndDispose_GivesUpInsteadOfBlockingForever_WhenTheHostNeverStops()
    {
        var host = new FakeHost(() => new TaskCompletionSource().Task);

        var started = DateTime.UtcNow;
        var stopped = OverlayHostRunner.StopAndDispose(host, Timeout);
        var elapsed = DateTime.UtcNow - started;

        Assert.False(stopped);
        Assert.False(host.Disposed);
        Assert.True(elapsed < TimeSpan.FromSeconds(3), $"gave up after {elapsed.TotalSeconds:0.0}s");
    }

    [Fact]
    public void StopAndDispose_ReturnsFalseWithoutThrowing_WhenTheStopFails()
    {
        var host = new FakeHost(() => throw new InvalidOperationException("host already gone"));

        Assert.False(OverlayHostRunner.StopAndDispose(host, Timeout));
    }

    [Fact]
    public void StopAndDispose_WritesTheGiveUpWarningIntoTheLogFileAndStillReturnsWithinItsBound()
    {
        var folder = Path.Combine(Path.GetTempPath(), "poolscoreboard-hostlog-tests", Guid.NewGuid().ToString("n"));
        var logPath = Path.Combine(folder, "controller.log");

        try
        {
            var stopped = true;
            var elapsed = TimeSpan.Zero;

            using (var log = new TraceLogFile(logPath))
            {
                log.Attach();

                var stopwatch = Stopwatch.StartNew();
                stopped = OverlayHostRunner.StopAndDispose(
                    new FakeHost(() => new TaskCompletionSource().Task),
                    TimeSpan.FromMilliseconds(100));
                elapsed = stopwatch.Elapsed;

                // Read while the listener is still attached, with the ordinary reader: the warning has to
                // be on disk already, because the process would be exiting at this point in a real
                // shutdown, so a line buffered anywhere would be lost.
                Assert.Contains("did not stop within", File.ReadAllText(logPath));
            }

            Assert.False(stopped);
            Assert.True(elapsed < TimeSpan.FromSeconds(2), $"stop took {elapsed.TotalMilliseconds:0}ms");
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Start_ReturnsNullAndTracesAnErrorWhenThePortIsAlreadyTaken()
    {
        var folder = Path.Combine(Path.GetTempPath(), "poolscoreboard-start-tests", Guid.NewGuid().ToString("n"));
        var logPath = Path.Combine(folder, "controller.log");

        // Hold the port the overlay wants. If something else already holds it the situation is the same, so
        // the assertions below still describe what should happen.
        TcpListener? blocker = null;
        try
        {
            blocker = new TcpListener(IPAddress.Loopback, OverlayHost.Port);
            blocker.Start();
        }
        catch (SocketException)
        {
            blocker = null;
        }

        try
        {
            IHost? host = null;

            using (var log = new TraceLogFile(logPath))
            {
                log.Attach();
                host = OverlayHostRunner.Start(new GameManager());

                var text = File.ReadAllText(logPath);
                Assert.Contains($"127.0.0.1:{OverlayHost.Port}", text);
                Assert.Contains("could not start", text);
            }

            Assert.Null(host);
        }
        finally
        {
            blocker?.Stop();
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void StopAndDispose_TracesAWarningWhenTheHostDoesNotStop()
    {
        var listener = new CapturingTraceListener();
        Trace.Listeners.Add(listener);
        try
        {
            OverlayHostRunner.StopAndDispose(
                new FakeHost(() => new TaskCompletionSource().Task),
                TimeSpan.FromMilliseconds(100));
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }

        Assert.Contains(listener.Messages, message => message.Contains("did not stop within"));
    }

    [Fact]
    public void StopAndDispose_TracesAWarningWhenTheStopThrows()
    {
        var listener = new CapturingTraceListener();
        Trace.Listeners.Add(listener);
        try
        {
            OverlayHostRunner.StopAndDispose(
                new FakeHost(() => throw new InvalidOperationException("already gone")),
                Timeout);
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }

        Assert.Contains(listener.Messages, message => message.Contains("failed to stop") && message.Contains("already gone"));
    }

    private sealed class CapturingTraceListener : TraceListener
    {
        public List<string> Messages { get; } = new();

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
            if (message != null)
                Messages.Add(message);
        }
    }

    private sealed class FakeHost : IHost
    {
        private readonly Func<Task> _stop;

        public FakeHost(Func<Task> stop) => _stop = stop;

        public bool Disposed { get; private set; }

        public IServiceProvider Services => null!;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => _stop();

        public void Dispose() => Disposed = true;
    }

    private sealed class StalledSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            // Deliberately dropped: the "UI thread" this stands in for never pumps.
        }

        public override void Send(SendOrPostCallback d, object? state) =>
            throw new NotSupportedException("A stalled context cannot run work inline.");
    }
}
