using System.Text.RegularExpressions;
using MitCue.Core.Actions;
using YamlDotNet.RepresentationModel;

namespace MitCue.Core.Tests.Data;

/// <summary>data/actions/aliases.yaml through <see cref="AliasDictionary"/> (reference: docs/research/actions.md §11–12).</summary>
public class AliasDictionaryTests
{
    private static readonly int[] SyncLevels = [80, 90, 100];

    private static AliasDictionary Aliases => RepoFiles.Aliases;

    private static ActionCatalog Catalog => RepoFiles.Actions;

    private static IEnumerable<string> Names(IEnumerable<AliasExpansion> expansions) => expansions.SelectMany(e => e.Actions);

    // ---- normalization ---------------------------------------------------------------------------

    [Theory]
    [InlineData("Nature's Minne", "natures minne")]
    [InlineData("Nature’s Minne", "natures minne")]
    [InlineData("Co-Tank", "co tank")]
    [InlineData("30%", "30%")]
    [InlineData("Rampart + 30% + Short CD", "rampart 30% short cd")]
    [InlineData("FeyIllum", "feyillum")]
    [InlineData("  Short   CD  ", "short cd")]
    [InlineData("Party Mit        Dismantle", "party mit dismantle")]
    [InlineData("Lilybell (pop after 1st Hit)", "lilybell pop after 1st hit")]
    [InlineData("Zoe → Eprog", "zoe eprog")]
    [InlineData("~1:22", "1 22")]
    [InlineData("ÉLAN", "élan")]
    [InlineData("", "")]
    [InlineData("--", "")]
    public void Normalize_FollowsTheDocumentedRules(string text, string expected) =>
        Assert.Equal(expected, AliasDictionary.Normalize(text));

    [Fact]
    public void KeysInTheFile_AreAlreadyNormalized()
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(RepoFiles.Data.ReadText(RepoFiles.AliasesPath)));
        var aliases = (YamlMappingNode)((YamlMappingNode)stream.Documents[0].RootNode).Children[new YamlScalarNode("aliases")];
        var rawKeys = aliases.Children.Keys.Cast<YamlScalarNode>().Select(k => k.Value!).ToList();

        Assert.True(rawKeys.Count >= 341, $"expected at least the 341 documented keys, found {rawKeys.Count}");
        Assert.All(rawKeys, k => Assert.Equal(AliasDictionary.Normalize(k), k));
        Assert.Equal(rawKeys.Order(StringComparer.Ordinal), Aliases.Keys);
    }

    // ---- data validation ---------------------------------------------------------------------------

    [Fact]
    public void EveryAlias_ResolvesToActionsItsJobCanUse()
    {
        Assert.Empty(Aliases.Validate(Catalog));
    }

    /// <summary>
    /// Every alias, for every job it applies to and every ultimate sync level, resolves (with upgrades applied) to an
    /// action of that job which is either usable at that level or simply not learned yet (a diagnostic for the
    /// importer, e.g. Expedient in TEA) — never to an action already replaced by its upgrade. At level 100 all resolve.
    /// </summary>
    [Fact]
    public void EveryAlias_ResolvesAtEverySyncLevel()
    {
        var problems = new List<string>();
        foreach (var key in Aliases.Keys)
        {
            foreach (var job in Jobs.All)
            {
                foreach (var expansion in Aliases.Lookup(key, job).Where(e => !e.NoteOnly))
                {
                    foreach (var name in expansion.Actions.Concat(expansion.Targeted))
                    {
                        foreach (var level in SyncLevels)
                        {
                            var action = Catalog.FindAtLevel(name, level, job);
                            if (action == null)
                                problems.Add($"{key} ({job}): '{name}' is unknown");
                            else if (!action.UsableBy(job))
                                problems.Add($"{key} ({job}): {action} is not a {job} action");
                            else if (!action.AvailableAt(level) && (level == 100 || action.Level <= level))
                                problems.Add($"{key} ({job}) at {level}: {action} is not usable");
                        }
                    }
                }
            }
        }
        Assert.Empty(problems);
    }

    [Fact]
    public void EveryAlias_AppliesToAtLeastOneJob()
    {
        var unused = Aliases.Keys.Where(k => !Jobs.All.Any(j => Aliases.Lookup(k, j).Count > 0)).ToList();
        Assert.Empty(unused);
    }

    [Fact]
    public void EmbeddedCopy_HasTheSameKeys()
    {
        var embedded = AliasDictionary.Load(new MitCue.Core.Data.EmbeddedDataSource().ReadText(RepoFiles.AliasesPath));
        Assert.Equal(Aliases.Keys, embedded.Keys);
    }

    // ---- shapes: plain, per job, sequences, note-only, targeted ------------------------------------

    [Fact]
    public void PlainAlias_IsOneActionForAnyJobThatHasIt()
    {
        var kera = Assert.Single(Aliases.Lookup("kera"));
        Assert.Equal(["Kerachole"], kera.Actions);
        Assert.Null(kera.Job);
        Assert.False(kera.Sequence);
        Assert.False(kera.IsUser);
        Assert.Single(Aliases.Lookup("Kera", Job.SGE));
        Assert.Empty(Aliases.Lookup("kera", Job.WHM));
        Assert.Equal(4, Jobs.All.Count(j => Aliases.Lookup("rep", j).Count > 0)); // the four tanks
    }

    [Fact]
    public void PerJobAlias_ExpandsForTheAssigneesJob()
    {
        Assert.Equal(2, Aliases.Lookup("wings").Count);
        Assert.Equal(["Temperance"], Names(Aliases.Lookup("wings", Job.WHM)));
        Assert.Equal(["Passage of Arms"], Names(Aliases.Lookup("Wings", Job.PLD)));
        Assert.Empty(Aliases.Lookup("wings", Job.SCH));

        Assert.Equal(["Angel Feathers"], Names(Aliases.Lookup("healer lb3", Job.SCH)));
        Assert.Equal(["Pulse of Life"], Names(Aliases.Lookup("Healer LB3", Job.WHM)));
        Assert.Equal(["Land Waker"], Names(Aliases.Lookup("lb3", Job.WAR)));
        Assert.Equal(["Astral Stasis"], Names(Aliases.Lookup("lb3", Job.AST)));
        Assert.Empty(Aliases.Lookup("lb3", Job.BRD));
    }

    [Fact]
    public void Sequences_KeepTheirOrder()
    {
        var spreadlo = Assert.Single(Aliases.Lookup("Spreadlo", Job.SCH));
        Assert.True(spreadlo.Sequence);
        Assert.Equal(["Adloquium", "Deployment Tactics"], spreadlo.Actions);
        Assert.NotNull(spreadlo.Note);
        Assert.Empty(Aliases.Lookup("spreadlo", Job.WHM));

        var zoe = Assert.Single(Aliases.Lookup("zoe eprog", Job.SGE));
        Assert.True(zoe.Sequence);
        Assert.Equal(["Zoe", "Eukrasian Prognosis"], zoe.Actions);

        var tempera = Assert.Single(Aliases.Lookup("party mit", Job.PCT));
        Assert.True(tempera.Sequence);
        Assert.Equal(["Tempera Coat", "Tempera Grassa"], tempera.Actions);
    }

    [Theory]
    [InlineData("everything")]
    [InlineData("Kitchen Sink")]
    [InlineData("personals")]
    [InlineData("melee lb3")]
    public void NoteOnlyPhrases_ApplyToEveryJobAndCarryANote(string token)
    {
        foreach (var job in Jobs.All)
        {
            var e = Assert.Single(Aliases.Lookup(token, job));
            Assert.True(e.NoteOnly);
            Assert.Empty(e.Actions);
            Assert.False(string.IsNullOrWhiteSpace(e.Note));
        }
    }

    [Theory]
    [InlineData(Job.PLD, "Holy Sheltron", "Intervention")]
    [InlineData(Job.WAR, "Bloodwhetting", "Nascent Flash")]
    [InlineData(Job.DRK, "The Blackest Night", "The Blackest Night")]
    [InlineData(Job.GNB, "Heart of Corundum", "Heart of Corundum")]
    public void ShortCd_HasATargetedForm(Job job, string self, string targeted)
    {
        var e = Assert.Single(Aliases.Lookup("Short CD", job));
        Assert.Equal([self], e.Actions);
        Assert.Equal([targeted], e.Targeted);
        Assert.Equal(job, e.Job);
        Assert.Empty(Aliases.Lookup("short cd", Job.WHM));
    }

    // ---- level resolution (actions.md §11) ---------------------------------------------------------

    [Theory]
    [InlineData("succor", Job.SCH, 90, "Succor")]
    [InlineData("succor", Job.SCH, 100, "Concitation")]
    [InlineData("eprog", Job.SGE, 90, "Eukrasian Prognosis")]
    [InlineData("eprog", Job.SGE, 100, "Eukrasian Prognosis II")]
    [InlineData("physis", Job.SGE, 80, "Physis II")]
    [InlineData("hoc", Job.GNB, 80, "Heart of Stone")]
    [InlineData("hs", Job.PLD, 80, "Sheltron")]
    [InlineData("bw", Job.WAR, 80, "Raw Intuition")]
    [InlineData("30%", Job.PLD, 90, "Sentinel")]
    [InlineData("30%", Job.PLD, 100, "Guardian")]
    [InlineData("30%", Job.WAR, 100, "Damnation")]
    [InlineData("30%", Job.DRK, 100, "Shadowed Vigil")]
    [InlineData("30%", Job.GNB, 100, "Great Nebula")]
    [InlineData("40%", Job.GNB, 90, "Nebula")]
    [InlineData("medica 2", Job.WHM, 100, "Medica III")]
    public void Aliases_ResolveAtTheSyncLevel(string token, Job job, int level, string expected)
    {
        var expansion = Assert.Single(Aliases.Lookup(token, job));
        var action = Catalog.FindAtLevel(expansion.Actions[0], level, job);
        Assert.NotNull(action);
        Assert.Equal(expected, action.Name);
        Assert.True(action.AvailableAt(level));
    }

    [Theory]
    [InlineData("expedient", Job.SCH)]
    [InlineData("aquaveil", Job.WHM)]
    [InlineData("bell", Job.WHM)]
    [InlineData("macrocosmos", Job.AST)]
    [InlineData("magick barrier", Job.RDM)]
    [InlineData("protraction", Job.SCH)]
    public void ActionsNotLearnedAtTheSyncLevel_StillResolveSoTheImporterCanReportThem(string token, Job job)
    {
        var action = Catalog.FindAtLevel(Assert.Single(Aliases.Lookup(token, job)).Actions[0], 80, job);
        Assert.NotNull(action);
        Assert.False(action.AvailableAt(80));
        Assert.True(action.Level > 80);
    }

    // ---- MatchLongest --------------------------------------------------------------------------------

    [Theory]
    [InlineData("Short CD to R1", "short cd", "to r1")]
    [InlineData("Lilybell pop after 1st Hit", "lilybell", "pop after 1st hit")]
    [InlineData("Kera after 1st hit", "kera", "after 1st hit")]
    [InlineData("Soil 57", "soil", "57")]
    [InlineData("Zoe Eprog", "zoe eprog", "")]
    [InlineData("Zoe", "zoe", "")]
    [InlineData("2nd Seraph Charge", "2nd seraph charge", "")]
    [InlineData("Hoc Co-Tank", "hoc", "co tank")]
    [InlineData("Party Mit if MCH", "party mit", "if mch")]
    [InlineData("Healer LB3", "healer lb3", "")]
    [InlineData("Medica II", "medica ii", "")]
    [InlineData("Nature's Minne", "natures minne", "")]
    public void MatchLongest_TakesTheLongestKeyAtTheStart(string item, string key, string rest)
    {
        var match = Aliases.MatchLongest(item);
        Assert.NotNull(match);
        Assert.Equal(key, match.Key);
        Assert.Equal(rest, match.Rest);
        Assert.NotEmpty(match.Expansions);
    }

    [Fact]
    public void MatchLongest_FiltersExpansionsByJobButKeepsTheKey()
    {
        var pld = Aliases.MatchLongest("PLD Wings", Job.PLD)!;
        Assert.Equal("pld wings", pld.Key);
        Assert.Equal(["Passage of Arms"], Names(pld.Expansions));

        var whm = Aliases.MatchLongest("PLD Wings", Job.WHM)!;
        Assert.Equal("pld wings", whm.Key);
        Assert.Empty(whm.Expansions);
    }

    [Theory]
    [InlineData("")]
    [InlineData("MT")]
    [InlineData("Kerachole2")]
    [InlineData("soi")]
    public void MatchLongest_ReturnsNullWithoutAKeyAtTheStart(string item) => Assert.Null(Aliases.MatchLongest(item));

    // ---- token coverage (actions.md §12) --------------------------------------------------------------

    /// <summary>
    /// Every spelling in the coverage table of docs/research/actions.md §12 matches its alias key with the
    /// longest-prefix rule, and the key resolves to what the table's last column says.
    /// </summary>
    [Fact]
    public void EveryTokenOfTheSheetCoverageTable_Resolves()
    {
        var doc = RepoFiles.ReadDoc("research", "actions.md");
        var start = doc.IndexOf("## 12.", StringComparison.Ordinal);
        var end = doc.IndexOf("Items that resolve to no alias", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "the coverage section of actions.md was not found");

        var rows = doc[start..end].Split('\n').Where(l => l.StartsWith("| `", StringComparison.Ordinal)).ToList();
        Assert.True(rows.Count >= 80, $"expected the ~85 coverage rows, found {rows.Count}");

        var problems = new List<string>();
        var spellings = 0;
        foreach (var row in rows)
        {
            var cells = row.Split('|').Select(c => c.Trim()).ToArray();
            var key = cells[1].Trim('`');
            var resolvesTo = cells[5];

            foreach (var spelling in cells[3].Split("; "))
            {
                spellings++;
                var match = Aliases.MatchLongest(spelling);
                if (match?.Key != key)
                    problems.Add($"'{spelling}' matched '{match?.Key}', expected '{key}'");
            }

            problems.AddRange(CheckResolution(key, resolvesTo));
        }

        Assert.True(spellings >= rows.Count);
        Assert.Empty(problems);
    }

    private static IEnumerable<string> CheckResolution(string key, string resolvesTo)
    {
        if (resolvesTo == "note only")
        {
            if (Aliases.Lookup(key) is not { Count: > 0 } notes || !notes.All(e => e.NoteOnly))
                yield return $"'{key}' should be note-only";
            yield break;
        }

        if (Regex.IsMatch(resolvesTo, "^[A-Z]{3}: "))
        {
            foreach (var part in resolvesTo.Split("; "))
            {
                var m = Regex.Match(part, @"^([A-Z]{3}): (.*?)(?: \(targeted: (.*)\))?$");
                if (!m.Success || !Jobs.TryParse(m.Groups[1].Value, out var job))
                {
                    yield return $"'{key}': cannot read '{part}'";
                    continue;
                }

                var expansion = Aliases.Lookup(key, job).FirstOrDefault();
                var expected = m.Groups[2].Value.Split(" → ");
                if (expansion == null || !expansion.Actions.SequenceEqual(expected))
                    yield return $"'{key}' for {job}: expected {string.Join(" → ", expected)}, got {expansion}";
                else if (m.Groups[3].Success && !expansion.Targeted.SequenceEqual([m.Groups[3].Value]))
                    yield return $"'{key}' for {job}: expected targeted {m.Groups[3].Value}, got {expansion}";
            }
            yield break;
        }

        var actions = resolvesTo.Split(" → ");
        if (!Aliases.Lookup(key).Any(e => e.Job == null && e.Actions.SequenceEqual(actions)))
            yield return $"'{key}': expected {resolvesTo}, got {string.Join(" | ", Aliases.Lookup(key))}";
    }

    [Theory]
    [InlineData("Rapture")]
    [InlineData("Medica II")]
    [InlineData("Cure III")]
    [InlineData("Consolation")]
    [InlineData("Emergency Tactics")]
    [InlineData("Succor")]
    [InlineData("Swiftcast")]
    [InlineData("Sprint")]
    [InlineData("Dissipate")]
    [InlineData("Dissipates")]
    [InlineData("Lucid")]
    [InlineData("Presence of Mind")]
    [InlineData("Assize")]
    [InlineData("Lustrate")]
    [InlineData("Indomitability")]
    [InlineData("Shake")]
    [InlineData("Tank LB3")]
    [InlineData("healer LB3")]
    public void ActionWordsInTheCarryTheClearNotes_AreKeys(string word)
    {
        Assert.True(Aliases.ContainsKey(word), $"'{word}' is not an alias");
        Assert.NotEmpty(Aliases.Lookup(word));
    }

    [Theory]
    [InlineData("MT")]
    [InlineData("OT")]
    [InlineData("H1")]
    [InlineData("H2 SCH")]
    [InlineData("H1 WHM")]
    [InlineData("M1")]
    [InlineData("R2")]
    [InlineData("CM LB Tank")]
    [InlineData("CM Rep Tank")]
    [InlineData("WAR")]
    [InlineData("PLD")]
    [InlineData("White Mage")]
    [InlineData("Scholar")]
    [InlineData("Top WHMs")]
    [InlineData("your 3rd/4th-in-line player")]
    [InlineData("after the hit")]
    [InlineData("right after the 1st stack")]
    [InlineData("just before P3 ends")]
    [InlineData("Debuffs at 13 secs")]
    [InlineData("when rots explode")]
    public void HeadersTargetsAndHints_AreNotAliases(string text) => Assert.Null(Aliases.MatchLongest(text));

    // ---- user aliases ----------------------------------------------------------------------------------

    [Fact]
    public void UserAliases_ComeFirstAndReplaceOnlyTheSameJob()
    {
        var aliases = RepoFiles.FreshAliases();
        aliases.AddUser("Bell", new AliasExpansion { Actions = ["Asylum"] });

        var bell = aliases.Lookup("bell", Job.WHM);
        Assert.Equal(2, bell.Count);
        Assert.True(bell[0].IsUser);
        Assert.Equal(["Asylum"], bell[0].Actions);
        Assert.Equal(["Liturgy of the Bell"], bell[1].Actions);
        Assert.False(bell[1].IsUser);

        aliases.AddUser("BELL", new AliasExpansion { Actions = ["Benediction"] });              // replaces (same token, no job)
        aliases.AddUser("bell", new AliasExpansion { Actions = ["Sacred Soil"], Job = Job.SCH }); // adds (other job)
        Assert.Equal(["Benediction", "Liturgy of the Bell"], Names(aliases.Lookup("bell", Job.WHM)));
        Assert.Equal(["Sacred Soil"], Names(aliases.Lookup("bell", Job.SCH)));
        Assert.Equal(3, aliases.Lookup("bell").Count);

        Assert.True(aliases.RemoveUser("bell"));
        Assert.False(aliases.RemoveUser("bell"));
        Assert.Equal(["Liturgy of the Bell"], Names(aliases.Lookup("bell")));
    }

    [Fact]
    public void UserAliases_AreNormalizedAndListedAsKeys()
    {
        var aliases = RepoFiles.FreshAliases();
        var before = aliases.Keys.Count();
        aliases.AddUser("  Big-Heal!! ", new AliasExpansion { Actions = ["Benediction"], Job = Job.WHM });

        Assert.Contains("big heal", aliases.Keys);
        Assert.Equal(before + 1, aliases.Keys.Count());
        Assert.True(aliases.ContainsKey("BIG HEAL"));
        var e = Assert.Single(aliases.Lookup("big heal", Job.WHM));
        Assert.True(e.IsUser);
        Assert.Empty(aliases.Lookup("big heal", Job.SCH));
        Assert.Equal("big heal", aliases.MatchLongest("Big Heal on MT", Job.WHM)!.Key);

        aliases.ClearUser();
        Assert.Equal(before, aliases.Keys.Count());
        Assert.Throws<ArgumentException>(() => aliases.AddUser("!!", new AliasExpansion { Actions = ["Benediction"] }));
    }

    [Fact]
    public void UserAliases_WithUnknownActions_AreReportedNotHidden()
    {
        var aliases = RepoFiles.FreshAliases();
        aliases.AddUser("oops", new AliasExpansion { Actions = ["Temperence"] });
        Assert.Single(aliases.Lookup("oops", Job.WHM)); // the importer reports the unknown name
        Assert.Contains(aliases.Validate(Catalog), p => p.Contains("Temperence", StringComparison.Ordinal));
    }

    [Fact]
    public void WithoutACatalog_AliasesWithoutAJobApplyToEveryJob()
    {
        var aliases = RepoFiles.FreshAliases(withCatalog: false);
        Assert.Null(aliases.Actions);
        Assert.Single(aliases.Lookup("rep", Job.WHM));
        Assert.Equal(["Temperance"], Names(aliases.Lookup("wings", Job.WHM))); // job-scoped expansions still filter
    }

    // ---- errors ----------------------------------------------------------------------------------------

    private const string MinimalFile = """
        schema: mitcue-aliases/1
        aliases:
          kera: Kerachole
          wings:
            - {job: WHM, actions: [Temperance]}
          everything: {note_only: true, note: "All of it."}
        """;

    [Fact]
    public void Validate_FindsBadAliases()
    {
        const string yaml = """
            schema: mitcue-aliases/1
            aliases:
              a: Temperence
              b: temperance
              c: {job: SCH, actions: [Temperance]}
              d: {actions: [Temperance, Sacred Soil]}
              e: {job: WAR, actions: [Bloodwhetting], targeted: [Intervention]}
              ok: Temperance
            """;
        var problems = AliasDictionary.Load(yaml, Catalog).Validate(Catalog);
        Assert.Equal(5, problems.Count);
        Assert.Contains(problems, p => p.StartsWith("alias 'a'", StringComparison.Ordinal) && p.Contains("unknown action 'Temperence'", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("alias 'b'", StringComparison.Ordinal) && p.Contains("spelled 'Temperance'", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("alias 'c' (SCH)", StringComparison.Ordinal) && p.Contains("not a SCH action", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("alias 'd'", StringComparison.Ordinal) && p.Contains("no job can use all", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("alias 'e' (WAR)", StringComparison.Ordinal) && p.Contains("Intervention is not a WAR action", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_MinimalFile()
    {
        var aliases = AliasDictionary.Load(MinimalFile);
        Assert.Equal(["everything", "kera", "wings"], aliases.Keys);
        Assert.Equal(Job.WHM, Assert.Single(aliases.Lookup("wings")).Job);
    }

    [Theory]
    [InlineData("kera: Kerachole", "kera: Kerachole\n  Kera: Kerachole", "duplicate")]
    [InlineData("job: WHM", "job: XYZ", "XYZ")]
    [InlineData("job: WHM", "job: WHM, level: 90", "level")]
    [InlineData("{note_only: true, note: \"All of it.\"}", "{note: \"All of it.\"}", "actions")]
    [InlineData("schema: mitcue-aliases/1", "schema: mitcue-aliases/9", "mitcue-aliases/9")]
    [InlineData("kera: Kerachole", "kera: [Kerachole]", "kera")]
    [InlineData("aliases:", "alias:", "aliases")]
    public void Load_ReportsWhatIsWrong(string find, string replace, string expectedInMessage)
    {
        var yaml = MinimalFile.Replace(find, replace, StringComparison.Ordinal);
        var ex = Assert.Throws<FormatException>(() => AliasDictionary.Load(yaml));
        Assert.Contains(expectedInMessage, ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
