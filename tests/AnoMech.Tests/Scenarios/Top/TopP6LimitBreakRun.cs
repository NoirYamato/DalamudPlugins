using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Top;

namespace AnoMech.Tests;

// A timeline with Alpha Omega spawned at 0 and the run's limit breaks (TopP6LimitBreaks), ticked once a frame as a
// scenario ticks them. The run ends once its events run dry, so a test that waits on a press keeps one due.
internal static class TopP6LimitBreakRun
{
    public static TimelineRunResult Execute(Action<SimWorld, TopP6AlphaOmega, TopP6LimitBreaks> timeline, TimelineRunOptions? options = null)
    {
        TopP6LimitBreaks? limitBreaks = null;
        var run = TimelineRun.Execute(world =>
        {
            limitBreaks = new TopP6LimitBreaks(world);
            var alphaOmega = new TopP6AlphaOmega(world);
            world.Events.Add(0f, alphaOmega.Spawn);
            timeline(world, alphaOmega, limitBreaks);
        }, (options ?? new TimelineRunOptions()) with { Tick = () => limitBreaks?.Tick() });
        limitBreaks?.Dispose();
        return run;
    }
}
