using System.Reflection;
using Dalamud.Plugin.Services;

namespace AnoMech.Tests;

// Dalamud queues Framework.Run work and drains it at the start of the next frame, before Update.
// RunOnTick work runs on the first frame its delay has passed, by the frames' own clock. Only
// Run(Action) and RunOnTick(Action, delay) are modelled; any other member throws.
public class FakeFramework : DispatchProxy
{
    private readonly Queue<Action> pending = new();
    private readonly List<(double Due, Action Action)> delayed = [];
    private double clock;

    internal static FakeFramework Create() => (FakeFramework)(object)Create<IFramework, FakeFramework>();

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method is { Name: nameof(IFramework.Run) } && args is [Action action, ..])
        {
            pending.Enqueue(action);
            return Task.CompletedTask;
        }
        if (method is { Name: nameof(IFramework.RunOnTick) } && args is [Action later, TimeSpan delay, ..])
        {
            delayed.Add((clock + delay.TotalSeconds, later));
            return Task.CompletedTask;
        }
        throw new NotSupportedException($"FakeFramework does not model IFramework.{method?.Name}");
    }

    internal void RunPending(float deltaSeconds = 0f)
    {
        clock += deltaSeconds;
        for (var count = pending.Count; count > 0; count--)
            pending.Dequeue()();
        foreach (var due in delayed.Where(d => d.Due <= clock).ToList())
        {
            delayed.Remove(due);
            due.Action();
        }
    }
}
