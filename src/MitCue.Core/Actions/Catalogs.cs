namespace MitCue.Core.Actions;

/// <summary>Action catalog loaded from data/actions/actions.yaml.</summary>
public sealed class ActionCatalog : IActionCatalog
{
    private readonly List<ActionInfo> actions;

    public ActionCatalog(IEnumerable<ActionInfo> actions)
    {
        this.actions = actions.ToList();
    }

    public IEnumerable<ActionInfo> All => this.actions;

    /// <summary>Parses the actions YAML (schema in docs/research/actions.md).</summary>
    public static ActionCatalog Load(string yaml) => throw new NotImplementedException();

    public ActionInfo? Get(uint id) => throw new NotImplementedException();

    public ActionInfo? FindByName(string name, Job? job = null) => throw new NotImplementedException();

    public IEnumerable<ActionInfo> ForJob(Job job, int syncLevel) => throw new NotImplementedException();
}

/// <summary>Shorthand → action(s) dictionary loaded from data/actions/aliases.yaml plus user aliases.</summary>
public sealed class AliasDictionary
{
    /// <summary>Parses the aliases YAML (schema in docs/research/actions.md).</summary>
    public static AliasDictionary Load(string yaml) => throw new NotImplementedException();

    /// <summary>All normalized alias keys.</summary>
    public IEnumerable<string> Keys => throw new NotImplementedException();

    /// <summary>
    /// Expansions for a token (normalized: lowercase, punctuation and repeated spaces removed),
    /// filtered to those applicable to <paramref name="job"/> when given. User aliases first.
    /// </summary>
    public IReadOnlyList<AliasExpansion> Lookup(string token, Job? job = null) => throw new NotImplementedException();

    /// <summary>Adds or replaces a user alias.</summary>
    public void AddUser(string token, AliasExpansion expansion) => throw new NotImplementedException();

    /// <summary>Normalizes text the same way keys are normalized.</summary>
    public static string Normalize(string text) => throw new NotImplementedException();
}
