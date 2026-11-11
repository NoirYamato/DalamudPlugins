using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace MitCue.PluginTests.Support;

/// <summary>
/// Catches task exceptions that nothing read: the finalizer reports them through
/// <see cref="TaskScheduler.UnobservedTaskException"/>, and Dalamud logs each one as an error ("Unobserved exception in Task").
/// </summary>
internal static class UnobservedTasks
{
    /// <summary>Runs <paramref name="scenario"/>, collects garbage and returns the unobserved exceptions that <paramref name="match"/> accepts.</summary>
    public static List<Exception> After(Action scenario, Func<Exception, bool> match)
    {
        var seen = new ConcurrentQueue<Exception>();
        void Handler(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            foreach (var ex in e.Exception.InnerExceptions)
            {
                if (match(ex))
                    seen.Enqueue(ex);
            }
        }

        TaskScheduler.UnobservedTaskException += Handler;
        try
        {
            Run(scenario);

            // The task becomes garbage once its continuation has returned; give the finalizer a few chances.
            for (var i = 0; i < 10 && seen.IsEmpty; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(20);
            }
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Handler;
        }

        return seen.ToList();
    }

    // Not inlined, so no local of the scenario keeps the task alive while the garbage is collected.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(Action scenario) => scenario();
}
