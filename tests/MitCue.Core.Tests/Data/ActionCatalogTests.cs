using System.Text.RegularExpressions;
using MitCue.Core.Actions;
using MitCue.Core.Data;

namespace MitCue.Core.Tests.Data;

/// <summary>data/actions/actions.yaml through <see cref="ActionCatalog"/> (reference: docs/research/actions.md §6–9).</summary>
public class ActionCatalogTests
{
    private static readonly int[] SyncLevels = [80, 90, 100];

    private static ActionCatalog Catalog => RepoFiles.Actions;

    private static ActionInfo A(string name) => RepoFiles.Action(name);

    /// <summary>(id, name) pairs read straight from the YAML text, independent of the loader.</summary>
    private static List<(uint Id, string Name)> RawEntries()
    {
        var text = RepoFiles.Data.ReadText(RepoFiles.ActionsPath);
        return Regex.Matches(text, @"^\s*- id: (\d+)\s*\r?\n\s*name: ""([^""]*)""", RegexOptions.Multiline)
            .Select(m => (uint.Parse(m.Groups[1].Value), m.Groups[2].Value))
            .ToList();
    }

    // ---- loading -------------------------------------------------------------------------------

    [Fact]
    public void Load_ReadsEveryActionInTheFile()
    {
        var raw = RawEntries();
        Assert.True(raw.Count >= 178, $"expected at least the 178 documented actions, the file has {raw.Count}");
        Assert.Equal(raw.Count, Catalog.All.Count());
        Assert.Equal(raw.Select(r => r.Id), Catalog.All.Select(a => a.Id));
        Assert.Equal("7.58", Catalog.GameVersion);
    }

    [Fact]
    public void Ids_AreUnique()
    {
        var duplicates = Catalog.All.GroupBy(a => a.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(duplicates);
    }

    [Fact]
    public void Names_MatchTheYaml()
    {
        foreach (var (id, name) in RawEntries())
        {
            var action = Catalog.Get(id);
            Assert.NotNull(action);
            Assert.Equal(name, action.Name);
        }
    }

    [Fact]
    public void Names_AreUniqueAmongNonFollowUpRows()
    {
        var duplicates = Catalog.All.Where(a => !a.IsFollowUp).GroupBy(a => AliasDictionary.Normalize(a.Name))
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(duplicates);
    }

    [Fact]
    public void EmbeddedCopy_IsTheSameKnowledgeBase()
    {
        var embedded = ActionCatalog.Load(new EmbeddedDataSource().ReadText(RepoFiles.ActionsPath));
        Assert.Equal(Catalog.All.Select(a => (a.Id, a.Name)), embedded.All.Select(a => (a.Id, a.Name)));
    }

    [Fact]
    public void EveryAction_HasJobsKindTargetAndIcon()
    {
        foreach (var a in Catalog.All)
        {
            Assert.True(a.Jobs.Count > 0, $"{a}: no jobs");
            Assert.DoesNotContain(Job.None, a.Jobs);
            Assert.NotEqual(ActionTarget.Unknown, a.Target);
            Assert.True(a.Icon > 0, $"{a}: no icon");
            Assert.True(a.Level >= 0, $"{a}: negative level");
            Assert.True(a.Recast >= 0, $"{a}: negative recast");
            Assert.Equal(a.Kind == ActionKind.LimitBreak, a.LimitBreakTier != null);
        }
    }

    /// <summary>
    /// The YAML's <c>sync</c> map (main duration at 80/90/100, null = not usable) must agree with what the catalog
    /// derives from <c>level</c>, <c>replaced_by</c> and the <c>duration</c> thresholds.
    /// </summary>
    [Fact]
    public void SyncMap_AgreesWithLevelsUpgradesAndDurations()
    {
        var file = ActionsFile.Parse(RepoFiles.Data.ReadText(RepoFiles.ActionsPath));
        var problems = new List<string>();
        foreach (var dto in file.Actions)
        {
            var action = Catalog.Get(dto.Id)!;
            foreach (var level in SyncLevels)
            {
                Assert.True(dto.Sync.ContainsKey(level), $"{action}: sync has no {level}");
                var expected = dto.Sync[level];
                if (expected == null)
                {
                    if (action.AvailableAt(level))
                        problems.Add($"{action} is available at {level} but sync says null");
                }
                else if (!action.AvailableAt(level))
                {
                    problems.Add($"{action} is not available at {level} but sync says {expected}");
                }
                else if (Math.Abs((action.DurationAt(level) ?? 0) - expected.Value) > 1e-9)
                {
                    problems.Add($"{action} lasts {action.DurationAt(level)} s at {level} but sync says {expected}");
                }
            }
        }
        Assert.Empty(problems);
    }

    [Fact]
    public void Upgrades_PointBothWays()
    {
        foreach (var a in Catalog.All)
        {
            if (a.ReplacedBy is { } up)
            {
                var upgrade = Catalog.Get(up);
                Assert.True(upgrade != null, $"{a}: replaced_by {up} is not in the catalog");
                Assert.Equal(a.Id, upgrade.UpgradeOf);
                Assert.Equal(upgrade.Level, a.ReplacedAtLevel);
                Assert.Equal(a.Jobs, upgrade.Jobs);
            }

            if (a.UpgradeOf is { } down)
            {
                var baseAction = Catalog.Get(down);
                Assert.True(baseAction != null, $"{a}: upgrade_of {down} is not in the catalog");
                Assert.Equal(a.Id, baseAction.ReplacedBy);
            }
        }
    }

    [Fact]
    public void FollowUpReferences_Exist()
    {
        foreach (var a in Catalog.All)
        {
            if (a.FollowUpOf is { } trigger)
                Assert.True(Catalog.Get(trigger) != null, $"{a}: follow_up_of {trigger} is not in the catalog");
            foreach (var f in a.FollowUps)
            {
                var target = Catalog.Get(f.Id);
                Assert.True(target != null, $"{a}: follow-up {f.Id} {f.Name} is not in the catalog");
                Assert.Equal(f.Name, target.Name);
            }
        }
    }

    // ---- field mapping -------------------------------------------------------------------------

    [Fact]
    public void Fields_AreMappedFromTheYaml()
    {
        var bell = A("Liturgy of the Bell");
        Assert.Equal(25862u, bell.Id);
        Assert.Equal(ActionKind.Heal, bell.Kind);
        Assert.Equal(ActionTarget.Ground, bell.Target);
        Assert.Equal(UseTiming.Before, bell.When);
        Assert.Equal(90, bell.Level);
        Assert.Equal(180, bell.Recast);
        Assert.Equal(23, bell.CooldownGroup);
        Assert.Equal(2649u, bell.Icon);
        Assert.Contains(bell.Related, r => r.Id == 28509 && r.Name == "Liturgy of the Bell" && r.Detail != null);
        Assert.Contains("Reactive heal", bell.Notes);

        var star = A("Earthly Star");
        Assert.Equal(10, star.Lead);
        Assert.Contains(star.FollowUps, f => f.Id == 8324 && f.Name == "Stellar Detonation");

        var caress = A("Divine Caress");
        Assert.Equal(3881u, caress.RequiresStatus);
        Assert.Equal(16536u, caress.FollowUpOf);
        Assert.True(caress.IsFollowUp);
        Assert.Contains("Divine Grace", caress.Requires);

        var solace = A("Afflatus Solace");
        Assert.Equal(new ResourceCost("Lily", 1), solace.Cost);
        Assert.True(solace.IsGcd);

        Assert.Equal(900, A("Medica").MpCost);
        Assert.Equal(2, A("Medica").CastTime);
        Assert.Equal(71, A("Thin Air").AdditionalCooldownGroup);
        Assert.Null(A("Temperance").AdditionalCooldownGroup);

        var pulse = A("Pulse of Life");
        Assert.Equal(ActionKind.LimitBreak, pulse.Kind);
        Assert.Equal(3, pulse.LimitBreakTier);
        Assert.Equal(UseTiming.After, pulse.When);
        Assert.Equal(0, pulse.Level);

        Assert.Equal(ActionKind.PartyMit, A("Reprisal").Kind);
        Assert.Equal(ActionTarget.Enemy, A("Reprisal").Target);
        Assert.Equal(ActionKind.TankMit, A("Rampart").Kind);
        Assert.Equal(ActionTarget.Self, A("Rampart").Target);
        Assert.Equal(ActionKind.Invuln, A("Holmgang").Kind);
        Assert.Equal(ActionKind.Shield, A("Divine Benison").Kind);
        Assert.Equal(ActionKind.Regen, A("Asylum").Kind);
        Assert.Equal(ActionKind.Buff, A("Recitation").Kind);
        Assert.Equal(ActionKind.Movement, A("Sprint").Kind);
        Assert.Equal(ActionKind.Other, A("Aetherflow").Kind);

        Assert.Equal(Jobs.All.Count(j => j is not (Job.BLU or Job.BST)), A("Sprint").Jobs.Count);
        Assert.Equal([Job.PLD, Job.WAR, Job.DRK, Job.GNB], A("Reprisal").Jobs);
    }

    [Fact]
    public void Effects_KeepEveryDurationByLevel()
    {
        var temperance = A("Temperance");
        Assert.Equal(2, temperance.Effects.Count);
        var grace = temperance.Effects.Single(e => e.Name.StartsWith("Divine Grace", StringComparison.Ordinal));
        Assert.Equal(0, grace.DurationAt(90));
        Assert.Equal(30, grace.DurationAt(100));

        var physis = A("Physis II").Effects.Single(e => e.Name.Contains("healing received", StringComparison.Ordinal));
        Assert.Equal(10, physis.DurationAt(90));
        Assert.Equal(15, physis.DurationAt(100));

        var resolve = A("Intervention").Effects.Single(e => e.Name.StartsWith("Knight's Resolve", StringComparison.Ordinal));
        Assert.Equal(0, resolve.DurationAt(80));
        Assert.Equal(4, resolve.DurationAt(90));
    }

    // ---- level-dependent values (actions.md §7, §8, §13.7, §15) --------------------------------

    [Theory]
    [InlineData("Reprisal", 80, 10)]
    [InlineData("Reprisal", 90, 10)]
    [InlineData("Reprisal", 100, 15)]
    [InlineData("Feint", 90, 10)]
    [InlineData("Feint", 100, 15)]
    [InlineData("Addle", 90, 10)]
    [InlineData("Addle", 100, 15)]
    [InlineData("Intervention", 80, 6)]
    [InlineData("Intervention", 90, 8)]
    [InlineData("Nascent Flash", 80, 6)]
    [InlineData("Nascent Flash", 90, 8)]
    [InlineData("Sheltron", 80, 6)]
    [InlineData("Equilibrium", 80, 0)]
    [InlineData("Equilibrium", 90, 15)]
    [InlineData("Temperance", 90, 20)]
    [InlineData("Plenary Indulgence", 80, 10)]
    [InlineData("Plenary Indulgence", 100, 10)]
    [InlineData("Kerachole", 90, 15)]
    [InlineData("Last Bastion", 90, 12)]
    [InlineData("Sprint", 90, 10)]
    public void DurationAt_FollowsTraits(string name, int level, double seconds) =>
        Assert.Equal(seconds, A(name).DurationAt(level));

    [Theory]
    [InlineData("Medica")]
    [InlineData("Benediction")]
    [InlineData("Healing Wind")]
    public void DurationAt_IsNullWithoutALastingEffect(string name) => Assert.Null(A(name).DurationAt(90));

    [Theory]
    [InlineData("Divine Benison", 80, 1)]
    [InlineData("Divine Benison", 90, 2)]
    [InlineData("Tetragrammaton", 90, 1)]
    [InlineData("Tetragrammaton", 100, 2)]
    [InlineData("Essential Dignity", 80, 2)]
    [InlineData("Essential Dignity", 100, 3)]
    [InlineData("Celestial Intersection", 80, 1)]
    [InlineData("Celestial Intersection", 90, 2)]
    [InlineData("Aurora", 80, 1)]
    [InlineData("Aurora", 90, 2)]
    [InlineData("Radiant Aegis", 90, 2)]
    [InlineData("Oblation", 90, 2)]
    [InlineData("Consolation", 90, 2)]
    [InlineData("Thin Air", 80, 2)]
    [InlineData("Temperance", 100, 1)]
    public void ChargesAt_FollowsTraits(string name, int level, int charges) =>
        Assert.Equal(charges, A(name).ChargesAt(level));

    [Theory]
    [InlineData("Troubadour", 80, 120)]
    [InlineData("Troubadour", 90, 90)]
    [InlineData("Tactician", 80, 120)]
    [InlineData("Shield Samba", 100, 90)]
    [InlineData("Deployment Tactics", 80, 120)]
    [InlineData("Deployment Tactics", 90, 90)]
    [InlineData("Recitation", 90, 90)]
    [InlineData("Recitation", 100, 60)]
    [InlineData("Zoe", 80, 120)]
    [InlineData("Zoe", 90, 90)]
    [InlineData("Soteria", 90, 90)]
    [InlineData("Soteria", 100, 60)]
    [InlineData("Swiftcast", 90, 60)]
    [InlineData("Swiftcast", 100, 40)]
    [InlineData("Temperance", 90, 120)]
    public void RecastAt_FollowsTraits(string name, int level, double seconds) =>
        Assert.Equal(seconds, A(name).RecastAt(level));

    // ---- ForJob ----------------------------------------------------------------------------------

    [Theory]
    [InlineData(Job.SGE, 80, "Physis II", "Physis")]
    [InlineData(Job.SGE, 90, "Physis II", "Physis")]
    [InlineData(Job.SGE, 100, "Physis II", "Physis")]
    [InlineData(Job.SGE, 90, "Eukrasian Prognosis", "Eukrasian Prognosis II")]
    [InlineData(Job.SGE, 100, "Eukrasian Prognosis II", "Eukrasian Prognosis")]
    [InlineData(Job.SCH, 90, "Succor", "Concitation")]
    [InlineData(Job.SCH, 100, "Concitation", "Succor")]
    [InlineData(Job.SCH, 80, "Summon Seraph", "Expedient")]
    [InlineData(Job.SCH, 90, "Expedient", "Seraphism")]
    [InlineData(Job.WHM, 80, "Temperance", "Aquaveil")]
    [InlineData(Job.WHM, 90, "Liturgy of the Bell", "Divine Caress")]
    [InlineData(Job.WHM, 100, "Medica III", "Medica II")]
    [InlineData(Job.AST, 90, "Aspected Helios", "Helios Conjunction")]
    [InlineData(Job.AST, 100, "Sun Sign", "Aspected Helios")]
    [InlineData(Job.PLD, 80, "Sheltron", "Holy Sheltron")]
    [InlineData(Job.PLD, 90, "Holy Sheltron", "Sheltron")]
    [InlineData(Job.PLD, 90, "Sentinel", "Guardian")]
    [InlineData(Job.PLD, 100, "Guardian", "Sentinel")]
    [InlineData(Job.WAR, 80, "Raw Intuition", "Bloodwhetting")]
    [InlineData(Job.WAR, 90, "Bloodwhetting", "Raw Intuition")]
    [InlineData(Job.DRK, 80, "The Blackest Night", "Oblation")]
    [InlineData(Job.DRK, 100, "Shadowed Vigil", "Shadow Wall")]
    [InlineData(Job.GNB, 80, "Heart of Stone", "Heart of Corundum")]
    [InlineData(Job.GNB, 90, "Heart of Corundum", "Heart of Stone")]
    [InlineData(Job.SAM, 90, "Tengentsu", "Third Eye")]
    [InlineData(Job.WHM, 90, "Swiftcast", "Reprisal")]
    [InlineData(Job.WAR, 90, "Reprisal", "Feint")]
    [InlineData(Job.RDM, 80, "Addle", "Magick Barrier")]
    public void ForJob_RespectsLevelsAndUpgrades(Job job, int level, string included, string excluded)
    {
        var names = Catalog.ForJob(job, level).Select(a => a.Name).ToList();
        Assert.Contains(included, names);
        Assert.DoesNotContain(excluded, names);
    }

    [Fact]
    public void ForJob_ReturnsExactlyTheUsableAndAvailableActions()
    {
        foreach (var job in Jobs.All)
        {
            foreach (var level in SyncLevels)
            {
                var expected = Catalog.All.Where(a => a.UsableBy(job) && a.AvailableAt(level)).Select(a => a.Id);
                Assert.Equal(expected, Catalog.ForJob(job, level).Select(a => a.Id));
            }
        }

        Assert.Contains(Catalog.ForJob(Job.WHM, 90), a => a.Name == "Sprint");
        Assert.Contains(Catalog.ForJob(Job.SCH, 90), a => a.Name == "Angel Feathers");
        Assert.DoesNotContain(Catalog.ForJob(Job.SCH, 90), a => a.Name == "Pulse of Life");
    }

    // ---- FindByName ------------------------------------------------------------------------------

    [Theory]
    [InlineData("Nature's Minne", "Nature's Minne")]
    [InlineData("natures minne", "Nature's Minne")]
    [InlineData("NATURE’S MINNE", "Nature's Minne")]
    [InlineData("arms length", "Arm's Length")]
    [InlineData("the bole", "the Bole")]
    [InlineData("The Bole", "the Bole")]
    [InlineData("heart-of-corundum", "Heart of Corundum")]
    [InlineData("  Liturgy   of the Bell ", "Liturgy of the Bell")]
    public void FindByName_IgnoresCaseAndPunctuation(string query, string expected)
    {
        var action = Catalog.FindByName(query);
        Assert.NotNull(action);
        Assert.Equal(expected, action.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Not An Action")]
    [InlineData("Fey Illum")]
    public void FindByName_UnknownNames_ReturnNull(string query) => Assert.Null(Catalog.FindByName(query));

    [Fact]
    public void FindByName_PrefersThePlayerRowOverItsFollowUp()
    {
        Assert.Equal(2, Catalog.All.Count(a => a.Name == "Horoscope"));
        Assert.Equal(16557u, Catalog.FindByName("Horoscope")!.Id);
        Assert.Equal(16557u, Catalog.FindByName("horoscope", Job.AST)!.Id);
    }

    [Fact]
    public void FindByName_PrefersAnActionOfTheJob()
    {
        var catalog = new ActionCatalog(
        [
            new ActionInfo { Id = 1, Name = "Shared", Jobs = [Job.WHM] },
            new ActionInfo { Id = 2, Name = "Shared", Jobs = [Job.SCH] },
        ]);
        Assert.Equal(1u, catalog.FindByName("shared")!.Id);
        Assert.Equal(2u, catalog.FindByName("shared", Job.SCH)!.Id);
        Assert.Equal(1u, catalog.FindByName("shared", Job.AST)!.Id); // preference, not a filter
    }

    [Fact]
    public void Constructor_RejectsDuplicateIds()
    {
        var ex = Assert.Throws<ArgumentException>(() => new ActionCatalog(
        [
            new ActionInfo { Id = 7, Name = "A" },
            new ActionInfo { Id = 7, Name = "B" },
        ]));
        Assert.Contains("7", ex.Message);
    }

    // ---- level resolution (actions.md §6 "Upgrades at a sync level") ------------------------------

    [Theory]
    [InlineData("Succor", 90, "Succor")]
    [InlineData("Succor", 100, "Concitation")]
    [InlineData("Concitation", 90, "Succor")]
    [InlineData("Physis", 80, "Physis II")]
    [InlineData("Physis", 90, "Physis II")]
    [InlineData("Heart of Corundum", 80, "Heart of Stone")]
    [InlineData("Heart of Stone", 90, "Heart of Corundum")]
    [InlineData("Holy Sheltron", 80, "Sheltron")]
    [InlineData("Bloodwhetting", 80, "Raw Intuition")]
    [InlineData("Sentinel", 90, "Sentinel")]
    [InlineData("Sentinel", 100, "Guardian")]
    [InlineData("Guardian", 90, "Sentinel")]
    [InlineData("Vengeance", 100, "Damnation")]
    [InlineData("Eukrasian Prognosis", 100, "Eukrasian Prognosis II")]
    [InlineData("Medica II", 100, "Medica III")]
    [InlineData("Aspected Helios", 100, "Helios Conjunction")]
    [InlineData("Third Eye", 90, "Tengentsu")]
    [InlineData("Reprisal", 80, "Reprisal")]
    [InlineData("Expedient", 80, "Expedient")]
    public void ResolveAt_AppliesUpgradesBothWays(string name, int level, string expected)
    {
        var resolved = Catalog.ResolveAt(A(name), level);
        Assert.Equal(expected, resolved.Name);
        Assert.Equal(expected, Catalog.FindAtLevel(name.ToLowerInvariant(), level)!.Name);
    }

    [Fact]
    public void ResolveAt_StopsOnInconsistentUpgradeData()
    {
        // A says it becomes B at 10, but B claims to be learned at 20: at 15 the two rules disagree forever.
        var a = new ActionInfo { Id = 1, Name = "A", Level = 1, ReplacedBy = 2, ReplacedAtLevel = 10 };
        var b = new ActionInfo { Id = 2, Name = "B", Level = 20, UpgradeOf = 1 };
        var catalog = new ActionCatalog([a, b]);
        Assert.Contains(catalog.ResolveAt(a, 15), new[] { a, b });
        Assert.Same(b, catalog.ResolveAt(a, 20));
        Assert.Same(a, catalog.ResolveAt(b, 5));

        var orphan = new ActionInfo { Id = 3, Name = "Orphan", Level = 50, UpgradeOf = 99, ReplacedBy = 98, ReplacedAtLevel = 60 };
        Assert.Same(orphan, catalog.ResolveAt(orphan, 40)); // referenced rows missing: unchanged
        Assert.Same(orphan, catalog.ResolveAt(orphan, 70));
    }

    [Fact]
    public void ResolveAt_NeverReturnsAReplacedAction()
    {
        foreach (var a in Catalog.All)
        {
            foreach (var level in SyncLevels)
            {
                var r = Catalog.ResolveAt(a, level);
                Assert.True(r.AvailableAt(level) || r.Level > level, $"{a} at {level} resolved to {r}, which is replaced at that level");
                Assert.True(r.Jobs.SequenceEqual(a.Jobs), $"{a} at {level} resolved to another job's action {r}");
            }
        }
    }

    // ---- recast groups -----------------------------------------------------------------------------

    [Theory]
    [InlineData("Raw Intuition", "Nascent Flash", true)]
    [InlineData("Bloodwhetting", "Nascent Flash", true)]
    [InlineData("Play II", "the Bole", true)]
    [InlineData("Holy Sheltron", "Sheltron", true)]
    [InlineData("Temperance", "Temperance", true)]
    [InlineData("Adloquium", "Succor", false)]          // both GCDs: group 58 is not a shared recast
    [InlineData("Temperance", "Fey Illumination", false)] // same group number, different jobs
    [InlineData("Divine Benison", "Tetragrammaton", false)]
    public void SharesRecastWith_UsesTheJobsRecastGroups(string a, string b, bool expected)
    {
        Assert.Equal(expected, A(a).SharesRecastWith(A(b)));
        Assert.Equal(expected, A(b).SharesRecastWith(A(a)));
    }

    // ---- errors ------------------------------------------------------------------------------------

    private const string MinimalFile = """
        schema: mitcue-actions/1
        game_version: "7.58"
        actions:
          - id: 7535
            name: "Reprisal"
            jobs: [PLD, WAR]
            level: 22
            kind: party_mit
            target: enemy
            gcd: false
            cast: 0
            recast: 60
            cooldown_group: 45
            duration: {22: 10, 98: 15}
            sync: {80: 10, 90: 10, 100: 15}
            icon: 806
        """;

    [Fact]
    public void Load_MinimalFile()
    {
        var catalog = ActionCatalog.Load(MinimalFile);
        var reprisal = Assert.Single(catalog.All);
        Assert.Equal(15, reprisal.DurationAt(100));
        Assert.Same(reprisal, catalog.Get(7535));
        Assert.Null(catalog.Get(1));
    }

    [Theory]
    [InlineData("kind: party_mit", "kind: party_mitigation", "party_mitigation")]
    [InlineData("target: enemy", "target: boss", "boss")]
    [InlineData("jobs: [PLD, WAR]", "jobs: [PLD, XYZ]", "XYZ")]
    [InlineData("icon: 806", "icon: 806\n    colour: red", "colour")]
    [InlineData("recast: 60", "recast: sixty", "sixty")]
    [InlineData("schema: mitcue-actions/1", "schema: mitcue-actions/2", "mitcue-actions/2")]
    [InlineData("jobs: [PLD, WAR]", "jobs: [PLD, WAR", "YAML")]
    public void Load_ReportsWhatIsWrong(string find, string replace, string expectedInMessage)
    {
        var yaml = MinimalFile.Replace(find, replace, StringComparison.Ordinal);
        var ex = Assert.Throws<FormatException>(() => ActionCatalog.Load(yaml));
        Assert.Contains(expectedInMessage, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A repeated key used to keep its last value without a word (here Reprisal's duration at 100 became 99).</summary>
    [Theory]
    [InlineData("    icon: 806", "    duration: {22: 99}\n    icon: 806", "duration", "line 16")]
    [InlineData("duration: {22: 10, 98: 15}", "duration: {22: 10, 98: 15, 98: 99}", "98", "line 14")]
    [InlineData("game_version: \"7.58\"", "game_version: \"7.58\"\ngame_version: \"7.59\"", "game_version", "line 3")]
    public void Load_RejectsDuplicateKeys(string find, string replace, string key, string where)
    {
        var yaml = MinimalFile.Replace(find, replace, StringComparison.Ordinal);
        var ex = Assert.Throws<FormatException>(() => ActionCatalog.Load(yaml));
        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(key, ex.Message, StringComparison.Ordinal);
        Assert.Contains(where, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsDuplicateIds()
    {
        var entry = MinimalFile[MinimalFile.IndexOf("  - id:", StringComparison.Ordinal)..];
        var ex = Assert.Throws<FormatException>(() => ActionCatalog.Load(MinimalFile + "\n" + entry));
        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("7535", ex.Message);
    }
}
