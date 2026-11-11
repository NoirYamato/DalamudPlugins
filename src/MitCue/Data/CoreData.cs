using Dalamud.Plugin.Services;
using MitCue.Core;
using MitCue.Core.Actions;
using MitCue.Core.Data;
using MitCue.Core.Fights;
using MitCue.Core.Timelines;
using MitCue.Util;

namespace MitCue.Data;

/// <summary>
/// MitCue.Core data shared by the runtime and the UI: fight library (fights + timelines), action catalog
/// and alias dictionary. Loaded lazily on the framework thread on first use and cached. Every core call
/// is guarded: a failure (missing data file, parse error, or a NotImplementedException while the core is
/// still being written) is reported through <see cref="ErrorReporter"/> instead of escaping.
/// </summary>
internal sealed class CoreData
{
    /// <summary>Embedded path of the action knowledge base.</summary>
    public const string ActionsPath = "actions/actions.yaml";

    /// <summary>Embedded path of the shorthand alias dictionary.</summary>
    public const string AliasesPath = "actions/aliases.yaml";

    private readonly ErrorReporter errors;
    private readonly Func<IEnumerable<UserAliasEntry>> userAliases;
    private readonly Dictionary<string, Timeline?> timelines = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the (not yet loaded) data holder.</summary>
    /// <param name="errors">Problem sink.</param>
    /// <param name="userAliases">Personal aliases to add to the alias dictionary after loading.</param>
    public CoreData(ErrorReporter errors, Func<IEnumerable<UserAliasEntry>> userAliases)
    {
        this.errors = errors;
        this.userAliases = userAliases;
    }

    /// <summary>True after the first load attempt (successful or not).</summary>
    public bool Loaded { get; private set; }

    /// <summary>Incremented on every (re)load.</summary>
    public int Revision { get; private set; }

    /// <summary>The fight library, or null if it failed to load.</summary>
    public FightLibrary? Fights { get; private set; }

    /// <summary>The action catalog; an empty catalog when the real one failed to load.</summary>
    public IActionCatalog Actions { get; private set; } = EmptyActionCatalog.Instance;

    /// <summary>True when <see cref="Actions"/> is the real catalog.</summary>
    public bool ActionsLoaded { get; private set; }

    /// <summary>The alias dictionary (with user aliases), or null if it failed to load.</summary>
    public AliasDictionary? Aliases { get; private set; }

    /// <summary>All loaded fights (empty when the library failed to load).</summary>
    public IReadOnlyList<FightDefinition> FightList => this.Fights?.Fights ?? [];

    /// <summary>Loads everything on first call. Framework thread.</summary>
    public void EnsureLoaded()
    {
        if (this.Loaded)
            return;
        this.Loaded = true;
        this.Revision++;
        this.timelines.Clear();

        try
        {
            this.Fights = new FightLibrary(new EmbeddedDataSource());
            this.errors.Clear("data.fights");
        }
        catch (Exception ex)
        {
            this.Fights = null;
            this.errors.Report("data.fights", "Loading fight definitions (data/fights)", ex);
        }

        try
        {
            this.Actions = ActionCatalog.Load(new EmbeddedDataSource().ReadText(ActionsPath));
            this.ActionsLoaded = true;
            this.errors.Clear("data.actions");
        }
        catch (Exception ex)
        {
            this.Actions = EmptyActionCatalog.Instance;
            this.ActionsLoaded = false;
            this.errors.Report("data.actions", "Loading the action catalog (data/" + ActionsPath + ")", ex);
        }

        try
        {
            // With the catalog, aliases without a job only apply to jobs that can use their actions ("rep" is not a healer action).
            this.Aliases = AliasDictionary.Load(new EmbeddedDataSource().ReadText(AliasesPath), this.ActionsLoaded ? this.Actions : null);
            this.errors.Clear("data.aliases");
            this.ApplyUserAliases();
        }
        catch (Exception ex)
        {
            this.Aliases = null;
            this.errors.Report("data.aliases", "Loading the alias dictionary (data/" + AliasesPath + ")", ex);
        }
    }

    /// <summary>Drops everything so the next <see cref="EnsureLoaded"/> reloads (Debug tab button).</summary>
    public void Reload()
    {
        this.Loaded = false;
        this.Fights = null;
        this.Actions = EmptyActionCatalog.Instance;
        this.ActionsLoaded = false;
        this.Aliases = null;
        this.errors.ClearPrefix("data.");
    }

    /// <summary>Adds the configured personal aliases to <see cref="Aliases"/>.</summary>
    public void ApplyUserAliases()
    {
        if (this.Aliases == null)
            return;

        foreach (var alias in this.userAliases())
        {
            try
            {
                Job? job = Jobs.TryParse(alias.Job, out var j) ? j : null;
                this.Aliases.AddUser(alias.Token, new AliasExpansion
                {
                    Actions = alias.Actions.ToList(),
                    Job = job,
                    Sequence = alias.Sequence,
                    NoteOnly = alias.NoteOnly,
                    Note = alias.Note,
                    IsUser = true,
                });
            }
            catch (Exception ex)
            {
                this.errors.Report("data.useralias", "Applying personal aliases", ex);
                return;
            }
        }
    }

    /// <summary>The fight whose territory is <paramref name="territory"/>, or null.</summary>
    public FightDefinition? FightForTerritory(uint territory)
    {
        if (this.Fights == null || territory == 0 || territory > ushort.MaxValue)
            return null;
        try
        {
            return this.Fights.ForTerritory((ushort)territory);
        }
        catch (Exception ex)
        {
            this.errors.Report("data.fightlookup", "Looking up the fight for this zone", ex);
            return null;
        }
    }

    /// <summary>The fight with this key (e.g. "top"), or null.</summary>
    public FightDefinition? GetFight(string key)
    {
        if (this.Fights == null)
            return null;
        try
        {
            return this.Fights.Get(key);
        }
        catch (Exception ex)
        {
            this.errors.Report("data.fightlookup", "Looking up a fight", ex);
            return null;
        }
    }

    /// <summary>The fight's parsed timeline (cached), or null when it failed to parse (reported once).</summary>
    public Timeline? TryLoadTimeline(FightDefinition fight)
    {
        if (this.timelines.TryGetValue(fight.Key, out var cached))
            return cached;

        Timeline? timeline = null;
        var key = "data.timeline." + fight.Key;
        try
        {
            if (this.Fights != null)
            {
                timeline = this.Fights.LoadTimeline(fight);
                this.errors.Clear(key);
            }
        }
        catch (Exception ex)
        {
            this.errors.Report(key, $"Loading the {fight.Key.ToUpperInvariant()} timeline ({fight.TimelineFile})", ex);
        }

        this.timelines[fight.Key] = timeline;
        return timeline;
    }

    /// <summary>The catalog entry for an action, or null (never throws).</summary>
    public ActionInfo? GetAction(uint id)
    {
        if (!this.ActionsLoaded)
            return null;
        try
        {
            return this.Actions.Get(id);
        }
        catch (Exception ex)
        {
            this.errors.Report("data.actionlookup", "Looking up an action in the catalog", ex);
            return null;
        }
    }
}
