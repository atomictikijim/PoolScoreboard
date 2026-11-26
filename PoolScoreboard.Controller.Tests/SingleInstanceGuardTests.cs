using PoolScoreboard.Controller;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// One Controller per desktop session: a second launch must be refused and must wake the first, because two
/// copies share %AppData% and fight over 127.0.0.1:51234. Names are per-test so runs cannot collide with each
/// other or with a Controller the developer has open.
/// </summary>
public sealed class SingleInstanceGuardTests
{
    private readonly string _id = Guid.NewGuid().ToString("n");

    private string MutexName => @"Local\poolscoreboard-guard-tests-" + _id;

    private string ActivationName => @"Local\poolscoreboard-guard-tests-activate-" + _id;

    [Fact]
    public void ASecondLaunch_IsRefusedAndWakesTheRunningInstance()
    {
        using var activated = new ManualResetEventSlim();
        using var primary = SingleInstanceGuard.TryAcquire(MutexName, ActivationName, activated.Set);
        Assert.NotNull(primary);

        var duplicate = SingleInstanceGuard.TryAcquire(MutexName, ActivationName, activated.Set);

        Assert.Null(duplicate);
        Assert.True(
            activated.Wait(TimeSpan.FromSeconds(5)),
            "the running instance was not asked to come forward");
    }

    [Fact]
    public void AfterTheRunningInstanceReleases_TheNextLaunchBecomesPrimary()
    {
        var primary = SingleInstanceGuard.TryAcquire(MutexName, ActivationName, () => { });
        Assert.NotNull(primary);
        primary!.Dispose();

        var next = SingleInstanceGuard.TryAcquire(MutexName, ActivationName, () => { });
        Assert.NotNull(next);
        next!.Dispose();
    }

    [Fact]
    public void ASecondLaunch_WithoutTheActivationEvent_StillExitsInsteadOfThrowing()
    {
        // Stands in for a running instance that holds the mutex but has not created its event yet.
        using var holder = new Mutex(initiallyOwned: false, MutexName);

        var started = DateTime.UtcNow;
        var duplicate = SingleInstanceGuard.TryAcquire(MutexName, ActivationName, () => { });

        Assert.Null(duplicate);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }
}
