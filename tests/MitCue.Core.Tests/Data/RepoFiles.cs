using MitCue.Core.Actions;
using MitCue.Core.Data;

namespace MitCue.Core.Tests.Data;

/// <summary>The repository's data/ and docs/ folders, and the action knowledge base loaded from them (shared, loaded once).</summary>
internal static class RepoFiles
{
    public const string ActionsPath = "actions/actions.yaml";
    public const string AliasesPath = "actions/aliases.yaml";

    private static readonly Lazy<DirectoryDataSource> LazyData = new(() => DirectoryDataSource.FindRepoData());
    private static readonly Lazy<ActionCatalog> LazyActions = new(() => ActionCatalog.Load(Data.ReadText(ActionsPath)));
    private static readonly Lazy<AliasDictionary> LazyAliases = new(() => AliasDictionary.Load(Data.ReadText(AliasesPath), Actions));

    /// <summary>data/ of the repository.</summary>
    public static DirectoryDataSource Data => LazyData.Value;

    /// <summary>The repository root.</summary>
    public static string Root => Path.GetDirectoryName(Data.Root)!;

    /// <summary>The catalog from data/actions/actions.yaml.</summary>
    public static ActionCatalog Actions => LazyActions.Value;

    /// <summary>The aliases from data/actions/aliases.yaml, filtered with <see cref="Actions"/>. Tests must not add user aliases to it.</summary>
    public static AliasDictionary Aliases => LazyAliases.Value;

    /// <summary>A fresh alias dictionary (for tests that add user aliases).</summary>
    public static AliasDictionary FreshAliases(bool withCatalog = true) =>
        AliasDictionary.Load(Data.ReadText(AliasesPath), withCatalog ? Actions : null);

    public static string ReadDoc(params string[] parts) => File.ReadAllText(Path.Combine([Root, "docs", .. parts]));

    /// <summary>The catalog entry with this exact name that is not a follow-up row.</summary>
    public static ActionInfo Action(string name) =>
        Actions.All.SingleOrDefault(a => a.Name == name && !a.IsFollowUp)
        ?? Actions.All.Single(a => a.Name == name);
}
