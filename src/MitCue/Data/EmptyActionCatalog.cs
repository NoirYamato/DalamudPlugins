using MitCue.Core;
using MitCue.Core.Actions;

namespace MitCue.Data;

/// <summary>
/// Stand-in <see cref="IActionCatalog"/> used while the real catalog (data/actions/actions.yaml) is missing or
/// failed to load, so the runtime can still schedule reminders with default timing.
/// </summary>
internal sealed class EmptyActionCatalog : IActionCatalog
{
    /// <summary>The shared instance.</summary>
    public static readonly EmptyActionCatalog Instance = new();

    private EmptyActionCatalog()
    {
    }

    /// <inheritdoc/>
    public IEnumerable<ActionInfo> All => [];

    /// <inheritdoc/>
    public ActionInfo? Get(uint id) => null;

    /// <inheritdoc/>
    public ActionInfo? FindByName(string name, Job? job = null) => null;

    /// <inheritdoc/>
    public IEnumerable<ActionInfo> ForJob(Job job, int syncLevel) => [];
}
