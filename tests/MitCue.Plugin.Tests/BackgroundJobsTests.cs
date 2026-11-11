using System.Collections.Concurrent;
using MitCue.Import;

namespace MitCue.PluginTests;

public class BackgroundJobsTests
{
    private readonly BlockingCollection<Action> mainThread = new();
    private readonly List<Exception> callbackErrors = [];

    private BackgroundJobs Jobs() => new(this.mainThread.Add, this.callbackErrors.Add);

    /// <summary>Runs queued main-thread actions until <paramref name="done"/> holds (or fails after a timeout).</summary>
    private void Pump(Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!done())
        {
            Assert.True(DateTime.UtcNow < until, "timed out waiting for the job");
            if (this.mainThread.TryTake(out var action, 50))
                action();
        }
    }

    [Fact]
    public void DeliversTheResultOnTheMainThreadQueue()
    {
        using var jobs = this.Jobs();
        int? result = null;

        jobs.RunSync("Adding", _ => 20 + 22, r => result = r, ex => throw ex);
        Assert.True(jobs.Busy);
        Assert.Equal("Adding", jobs.Current);
        Assert.Null(result);

        this.Pump(() => result != null);
        Assert.Equal(42, result);
        Assert.False(jobs.Busy);
    }

    [Fact]
    public void DeliversFailures()
    {
        using var jobs = this.Jobs();
        Exception? error = null;

        jobs.RunSync<int>("Failing", _ => throw new FormatException("bad"), _ => { }, ex => error = ex);

        this.Pump(() => error != null);
        Assert.IsType<FormatException>(error);
        Assert.False(jobs.Busy);
    }

    [Fact]
    public async Task DropsCancelledAndSupersededResults()
    {
        using var jobs = this.Jobs();
        var results = new List<string>();
        var gate = new TaskCompletionSource();

        jobs.Run("First", async ct =>
        {
            await gate.Task;
            return "first";
        }, results.Add, ex => throw ex);
        jobs.Run("Second", _ => Task.FromResult("second"), results.Add, ex => throw ex);
        gate.SetResult();

        this.Pump(() => results.Count > 0);
        await Task.Delay(100);
        while (this.mainThread.TryTake(out var action))
            action();
        Assert.Equal(new[] { "second" }, results);

        jobs.Run("Third", async ct =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return "third";
        }, results.Add, ex => throw ex);
        jobs.Cancel();
        Assert.False(jobs.Busy);
        await Task.Delay(100);
        while (this.mainThread.TryTake(out var action))
            action();
        Assert.Equal(new[] { "second" }, results);
    }

    [Fact]
    public void ReportsCallbackExceptions()
    {
        using var jobs = this.Jobs();
        var ran = false;

        jobs.RunSync("Callback throws", _ => 1, _ =>
        {
            ran = true;
            throw new InvalidOperationException("ui broke");
        }, ex => throw ex);

        this.Pump(() => ran && this.callbackErrors.Count > 0);
        Assert.IsType<InvalidOperationException>(Assert.Single(this.callbackErrors));
    }

    [Fact]
    public void IgnoresWorkAfterDispose()
    {
        var jobs = this.Jobs();
        jobs.Dispose();
        jobs.RunSync("Late", _ => 1, _ => Assert.Fail("should not run"), ex => throw ex);
        Assert.False(jobs.Busy);
    }
}
