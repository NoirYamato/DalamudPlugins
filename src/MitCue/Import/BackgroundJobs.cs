namespace MitCue.Import;

/// <summary>
/// Runs one piece of slow work at a time (download, workbook parsing, import) on a background task and hands its
/// result back to the game's main thread through <c>post</c> (the plugin passes <c>IFramework.RunOnFrameworkThread</c>),
/// so callers never block the framework thread and never touch their state from another thread.
/// Starting a new job cancels the previous one; results of cancelled or superseded jobs are dropped (their exceptions
/// are still observed, so nothing is logged as an unobserved task exception).
/// </summary>
/// <remarks>
/// <see cref="Run{T}"/>, <see cref="Cancel"/> and <see cref="Dispose"/> are called on the main thread; the callbacks
/// run on the main thread (via <c>post</c>). The work itself runs on the thread pool and must not touch UI state.
/// </remarks>
internal sealed class BackgroundJobs : IDisposable
{
    private readonly Action<Action> post;
    private readonly Action<Exception> callbackFailed;
    private CancellationTokenSource? cancel;
    private int generation;
    private bool disposed;

    /// <summary>Creates the runner.</summary>
    /// <param name="post">Queues an action on the main thread.</param>
    /// <param name="callbackFailed">Receives exceptions thrown by result callbacks (they never escape).</param>
    public BackgroundJobs(Action<Action> post, Action<Exception> callbackFailed)
    {
        this.post = post;
        this.callbackFailed = callbackFailed;
    }

    /// <summary>Label of the running job ("Downloading the sheet..."), or null when idle.</summary>
    public string? Current { get; private set; }

    /// <summary>True while a job runs.</summary>
    public bool Busy => this.Current != null;

    /// <summary>When the running job started (UTC).</summary>
    public DateTime StartedUtc { get; private set; }

    /// <summary>Starts <paramref name="work"/> on the thread pool, cancelling the running job.</summary>
    /// <typeparam name="T">Result type.</typeparam>
    /// <param name="label">What is happening, for the UI.</param>
    /// <param name="work">The work; it should honour the token where it can.</param>
    /// <param name="done">Main thread: the result.</param>
    /// <param name="failed">Main thread: the exception the work threw.</param>
    public void Run<T>(string label, Func<CancellationToken, Task<T>> work, Action<T> done, Action<Exception> failed)
    {
        if (this.disposed)
            return;

        this.Cancel();
        var gen = ++this.generation;
        var source = new CancellationTokenSource();
        this.cancel = source;
        this.Current = label;
        this.StartedUtc = DateTime.UtcNow;
        var token = source.Token;

        Task<T> task;
        try
        {
            task = Task.Run(() => work(token), token);
        }
        catch (Exception ex)
        {
            this.Finish(gen, () => failed(ex));
            return;
        }

        task.ContinueWith(
            t =>
            {
                // Always read the exception, also of a job whose result is dropped: an unread task exception is
                // reported by the finalizer as "Unobserved exception in Task" (an error in dalamud.log).
                var error = t.Exception;
                Action deliver;
                if (t.IsCanceled || token.IsCancellationRequested)
                    deliver = () => { };
                else if (error != null)
                    deliver = () => failed(error.InnerExceptions.Count == 1 ? error.InnerExceptions[0] : error);
                else
                    deliver = () => done(t.Result);

                try
                {
                    this.post(() => this.Finish(gen, deliver));
                }
                catch (Exception)
                {
                    // The main-thread queue is gone (plugin unloading): nothing left to deliver to.
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>Runs synchronous work on the thread pool (see <see cref="Run{T}"/>).</summary>
    public void RunSync<T>(string label, Func<CancellationToken, T> work, Action<T> done, Action<Exception> failed) =>
        this.Run(label, ct => Task.FromResult(work(ct)), done, failed);

    /// <summary>Cancels the running job (its result is dropped). Main thread.</summary>
    public void Cancel()
    {
        this.generation++;
        this.Current = null;
        var source = this.cancel;
        this.cancel = null;
        if (source == null)
            return;
        try
        {
            source.Cancel();
        }
        catch (Exception)
        {
            // A callback registered on the token threw; the job is abandoned anyway.
        }
        finally
        {
            source.Dispose();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        this.Cancel();
        this.disposed = true;
    }

    private void Finish(int gen, Action deliver)
    {
        if (this.disposed || gen != this.generation)
            return;

        this.Current = null;
        var source = this.cancel;
        this.cancel = null;
        source?.Dispose();
        try
        {
            deliver();
        }
        catch (Exception ex)
        {
            this.callbackFailed(ex);
        }
    }
}
