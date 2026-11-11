using Dalamud.Plugin.Services;

namespace MitCue.Util;

/// <summary>
/// Collects problems (exceptions from MitCue.Core calls, failed hooks, unreadable files) so the plugin
/// never crashes a frame: each distinct problem is logged once and listed in the config window and
/// the Debug tab. Thread-safe (hook detours may report too).
/// </summary>
internal sealed class ErrorReporter
{
    private readonly IPluginLog log;
    private readonly object gate = new();
    private readonly List<Problem> problems = [];

    /// <summary>Creates a reporter that logs through <paramref name="log"/>.</summary>
    public ErrorReporter(IPluginLog log)
    {
        this.log = log;
    }

    /// <summary>Incremented whenever the problem list changes.</summary>
    public int Version { get; private set; }

    /// <summary>Number of problems currently listed.</summary>
    public int Count
    {
        get
        {
            lock (this.gate)
                return this.problems.Count;
        }
    }

    /// <summary>Records an exception under <paramref name="key"/>; the first occurrence is logged.</summary>
    /// <param name="key">Stable identity of the problem (e.g. "data.fights").</param>
    /// <param name="context">What MitCue was doing (shown to the user).</param>
    /// <param name="ex">The exception.</param>
    public void Report(string key, string context, Exception ex)
    {
        var message = Describe(ex);
        bool first;
        lock (this.gate)
        {
            first = this.Upsert(key, context, message, ex);
        }

        if (!first)
            return;

        if (ex is NotImplementedException)
            this.log.Warning("{Context}: {Message}", context, message);
        else
            this.log.Error(ex, "{Context}: {Message}", context, message);
    }

    /// <summary>Records a problem that is not an exception; the first occurrence is logged as a warning.</summary>
    public void Warn(string key, string context, string message)
    {
        bool first;
        lock (this.gate)
        {
            first = this.Upsert(key, context, message, null);
        }

        if (first)
            this.log.Warning("{Context}: {Message}", context, message);
    }

    /// <summary>Removes the problem with this key (the operation succeeded later).</summary>
    public void Clear(string key)
    {
        lock (this.gate)
        {
            if (this.problems.RemoveAll(p => p.Key == key) > 0)
                this.Version++;
        }
    }

    /// <summary>Removes every problem whose key starts with <paramref name="prefix"/>.</summary>
    public void ClearPrefix(string prefix)
    {
        lock (this.gate)
        {
            if (this.problems.RemoveAll(p => p.Key.StartsWith(prefix, StringComparison.Ordinal)) > 0)
                this.Version++;
        }
    }

    /// <summary>True when a problem with this key is listed.</summary>
    public bool Has(string key)
    {
        lock (this.gate)
            return this.problems.Exists(p => p.Key == key);
    }

    /// <summary>A copy of the current problems, oldest first.</summary>
    public Problem[] Snapshot()
    {
        lock (this.gate)
            return this.problems.ToArray();
    }

    /// <summary>A short, user-facing description of an exception.</summary>
    public static string Describe(Exception ex)
    {
        if (ex is NotImplementedException)
        {
            var site = ex.TargetSite;
            var where = site == null ? "a MitCue.Core method" : $"{site.DeclaringType?.Name}.{site.Name}";
            return $"Not implemented yet: {where} (MitCue.Core is still being built).";
        }

        if (ex is AggregateException { InnerException: { } inner })
            return Describe(inner);

        return $"{ex.GetType().Name}: {ex.Message}";
    }

    private bool Upsert(string key, string context, string message, Exception? ex)
    {
        var index = this.problems.FindIndex(p => p.Key == key);
        var now = DateTime.Now;
        if (index >= 0)
        {
            var existing = this.problems[index];
            if (existing.Message != message)
            {
                // A different failure under the same key: keep the newest description.
                this.problems[index] = existing with { Message = message, Detail = ex?.ToString(), LastSeen = now, Count = existing.Count + 1 };
                this.Version++;
            }
            else
            {
                existing.LastSeen = now;
                existing.Count++;
            }

            return false;
        }

        if (this.problems.Count >= 100)
            this.problems.RemoveAt(0);
        this.problems.Add(new Problem(key, context, message, ex?.ToString(), now) { LastSeen = now, Count = 1 });
        this.Version++;
        return true;
    }

    /// <summary>One listed problem.</summary>
    /// <param name="Key">Stable identity.</param>
    /// <param name="Context">What MitCue was doing.</param>
    /// <param name="Message">Short description.</param>
    /// <param name="Detail">Full exception text, if any.</param>
    /// <param name="FirstSeen">First occurrence (local time).</param>
    internal sealed record Problem(string Key, string Context, string Message, string? Detail, DateTime FirstSeen)
    {
        /// <summary>Latest occurrence (local time).</summary>
        public DateTime LastSeen { get; set; }

        /// <summary>Number of occurrences.</summary>
        public int Count { get; set; }
    }
}
