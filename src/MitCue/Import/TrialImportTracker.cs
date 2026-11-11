namespace MitCue.Import;

/// <summary>
/// When the Options step runs its trial import (the <see cref="ImportAnalysis"/> that picks the default tabs and
/// columns), keyed by source and fight: once per key; again after it was cancelled by closing the window (so the
/// defaults are applied when the window opens again); and, after the player cancelled it with the Cancel button,
/// only when they ask for it (a Cancel that restarts at once would do nothing). Main thread only.
/// </summary>
internal sealed class TrialImportTracker
{
    private string? started;
    private string? cancelled;
    private bool running;

    /// <summary>True while the trial import runs.</summary>
    public bool Running => this.running;

    /// <summary>True when the player cancelled the trial import for <paramref name="key"/> and has not asked for it again.</summary>
    public bool CancelledByPlayer(string key) => this.cancelled == key;

    /// <summary>True when the trial import for <paramref name="key"/> should start now.</summary>
    /// <param name="key">Source and fight.</param>
    /// <param name="busy">Another background job runs.</param>
    public bool ShouldStart(string key, bool busy) => !busy && this.started != key && this.cancelled != key;

    /// <summary>The trial import for <paramref name="key"/> starts.</summary>
    public void Start(string key)
    {
        this.started = key;
        this.running = true;
    }

    /// <summary>True when a result for <paramref name="key"/> is still wanted (nothing newer started).</summary>
    public bool IsCurrent(string key) => this.started == key;

    /// <summary>The trial import delivered its result (or failed).</summary>
    public void Finished(string key)
    {
        if (this.started == key)
            this.running = false;
    }

    /// <summary>
    /// The running background job was cancelled. When it was the trial import, it runs again: by itself when the
    /// window closed (<paramref name="byPlayer"/> = false), after <see cref="Retry"/> when the player cancelled it.
    /// </summary>
    public void JobCancelled(bool byPlayer)
    {
        if (!this.running)
            return;
        this.running = false;
        if (byPlayer)
            this.cancelled = this.started;
        this.started = null;
    }

    /// <summary>The player asks for the trial import again (or something it depends on changed).</summary>
    public void Retry()
    {
        this.started = null;
        this.cancelled = null;
        this.running = false;
    }
}
