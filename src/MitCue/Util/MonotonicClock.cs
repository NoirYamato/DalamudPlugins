using System.Diagnostics;

namespace MitCue.Util;

/// <summary>
/// Monotonic clock in seconds since the plugin loaded (Stopwatch ticks). Thread-safe; used for every
/// <see cref="MitCue.Core.Events.GameEvent.Time"/> the plugin produces, including from hook detours.
/// </summary>
internal sealed class MonotonicClock
{
    private readonly long origin = Stopwatch.GetTimestamp();

    /// <summary>Seconds since the clock was created.</summary>
    public double Now => (Stopwatch.GetTimestamp() - this.origin) / (double)Stopwatch.Frequency;
}
