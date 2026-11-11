using MitCue.Core;
using MitCue.Core.Actions;
using MitCue.Import;

namespace MitCue.PluginTests;

/// <summary>Review fixes remembered as personal aliases (DESIGN §3.3, §6.8).</summary>
public class PersonalAliasesTests
{
    [Fact]
    public void MakesAnAliasFromShorthand()
    {
        var alias = PersonalAliases.From(" Spreadlo ", "Deployment Tactics", Job.SCH);

        Assert.NotNull(alias);
        Assert.Equal("Spreadlo", alias.Token);
        Assert.Equal(new[] { "Deployment Tactics" }, alias.Actions);
        Assert.Equal("SCH", alias.Job);
        Assert.Null(PersonalAliases.From(null, "Temperance", Job.WHM));
        Assert.Null(PersonalAliases.From("  ", "Temperance", Job.WHM));
        Assert.Null(PersonalAliases.From("!!", "Temperance", Job.WHM));
        Assert.Null(PersonalAliases.From("Use everything you have before the first stack, then shields", "Temperance", Job.WHM));
        Assert.Null(PersonalAliases.From("Temp", " ", Job.WHM));
        Assert.Null(PersonalAliases.From("Temp", "Temperance", Job.None)!.Job);
    }

    [Fact]
    public void ReplacesTheSameShorthandForTheSameJob()
    {
        var aliases = new List<UserAliasEntry>();
        Assert.True(PersonalAliases.Upsert(aliases, PersonalAliases.From("Tempy", "Temperance", Job.WHM)!));
        Assert.False(PersonalAliases.Upsert(aliases, PersonalAliases.From("tempy!", "Temperance", Job.WHM)!));
        Assert.True(PersonalAliases.Upsert(aliases, PersonalAliases.From("TEMPY", "Divine Benison", Job.WHM)!));
        Assert.True(PersonalAliases.Upsert(aliases, PersonalAliases.From("Tempy", "Sacred Soil", Job.SCH)!));

        Assert.Equal(2, aliases.Count);
        Assert.Equal(new[] { "Divine Benison" }, aliases.Single(a => a.Job == "WHM").Actions);

        Assert.True(PersonalAliases.Remove(aliases, PersonalAliases.From("tempy", "x", Job.WHM)!));
        Assert.Equal("SCH", Assert.Single(aliases).Job);
        Assert.False(PersonalAliases.Remove(aliases, PersonalAliases.From("tempy", "x", Job.WHM)!));
    }

    [Fact]
    public void AnAliasIsReadByTheAliasDictionary()
    {
        var dictionary = AliasDictionary.Load("schema: mitcue-aliases/1\naliases:\n  temp: Temperance\n");
        var alias = PersonalAliases.From("Spreadlo", "Deployment Tactics", Job.SCH)!;

        // As CoreData.ApplyUserAliases adds the configured aliases.
        dictionary.AddUser(alias.Token, new AliasExpansion { Actions = alias.Actions.ToList(), Job = Job.SCH, IsUser = true });

        var expansion = Assert.Single(dictionary.Lookup("spreadlo", Job.SCH));
        Assert.Equal(new[] { "Deployment Tactics" }, expansion.Actions);
        Assert.Empty(dictionary.Lookup("spreadlo", Job.WHM));
    }
}
