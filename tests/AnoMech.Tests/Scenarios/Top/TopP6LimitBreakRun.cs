using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Top;

namespace AnoMech.Tests;

// A timeline with Alpha Omega spawned at 0 and the run's limit breaks (TopP6LimitBreaks), ticked once a frame as a
// scenario ticks them; `betweenFrames` runs after that tick, where the game hears the human's client before the next
// frame's events. The run ends once its events run dry, so a test that waits on a press keeps one due.
internal static class TopP6LimitBreakRun
{
    public static TimelineRunResult Execute(Action<SimWorld, TopP6AlphaOmega, TopP6LimitBreaks> timeline, TimelineRunOptions? options = null,
        Action<SimWorld, TopP6AlphaOmega>? betweenFrames = null)
    {
        TopP6LimitBreaks? limitBreaks = null;
        SimWorld? world = null;
        TopP6AlphaOmega? alphaOmega = null;
        var run = TimelineRun.Execute(w =>
        {
            world = w;
            limitBreaks = new TopP6LimitBreaks(w);
            alphaOmega = new TopP6AlphaOmega(w);
            w.Events.Add(0f, alphaOmega.Spawn);
            timeline(w, alphaOmega, limitBreaks);
        }, (options ?? new TimelineRunOptions()) with
        {
            Tick = () =>
            {
                limitBreaks?.Tick();
                if (world is not null && alphaOmega is not null) betweenFrames?.Invoke(world, alphaOmega);
            },
        });
        limitBreaks?.Dispose();
        return run;
    }
}
