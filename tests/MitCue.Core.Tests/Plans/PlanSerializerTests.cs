using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using MitCue.Core.Plans;

namespace MitCue.Core.Tests.Plans;

/// <summary><see cref="PlanSerializer"/>: YAML, JSON and share codes (docs/DESIGN.md §3.2).</summary>
public class PlanSerializerTests
{
    private static MitPlan Sample() => new()
    {
        Id = "6f1c2b9e-0000-4000-8000-000000000001",
        Name = "Carry the Clear – WHM",
        Fight = "top",
        Source = new PlanSource
        {
            Kind = "google-sheet",
            Url = "https://docs.google.com/spreadsheets/d/1chKttd7/edit#gid=777100",
            Tabs = ["TOP – White Mage", "TOP – WHM + SCH"],
            Imported = "2026-11-11",
        },
        Assignees =
        [
            new Assignee { Key = "White Mage", Job = Job.WHM },
            new Assignee { Key = "H2 SCH", Slot = "H2", Job = Job.SCH },
            new Assignee { Key = "Notes" },
        ],
        Entries =
        [
            new PlanEntry
            {
                Id = "e012",
                Mechanic = "p1.pantokrator",
                Offset = -3.3,
                Who = new WhoSpec { Job = Job.WHM, Assignee = "White Mage" },
                Do =
                [
                    PlanItem.ForAction(16536, "Temperance"),
                    PlanItem.AnyOf([PlanItem.ForAction(3570, "Tetragrammaton"), PlanItem.ForAction(7432, "Divine Benison")]),
                    PlanItem.ForNote("Everything"),
                ],
                Severity = 3,
                Src = "'TOP – White Mage'!C18",
            },
            new PlanEntry
            {
                Id = "e013",
                Mechanic = "p3.hello_world",
                HitIndex = 1,
                Offset = 0,
                Who = new WhoSpec { Slot = "H2", Job = Job.SCH },
                Do = [PlanItem.ForAction(185, "Adloquium"), PlanItem.ForAction(3585, "Deployment Tactics")],
                Seq = true,
                Target = "your 3rd/4th-in-line player",
                Note = "after the 1st stack",
                Detail = "Line one\nLine two: \"quoted\" # not a comment",
                Optional = true,
                WhenReady = true,
                Severity = 0,
                Src = "'P3'!F5",
            },
            new PlanEntry { Id = "e014", Mechanic = "p6.cosmo_memory" },
        ],
    };

    private static void AssertSamePlan(MitPlan expected, MitPlan actual) =>
        Assert.Equal(PlanSerializer.ToJson(expected), PlanSerializer.ToJson(actual));

    // ---- round trips --------------------------------------------------------------------------------

    [Fact]
    public void Yaml_RoundTrips() => AssertSamePlan(Sample(), PlanSerializer.FromYaml(PlanSerializer.ToYaml(Sample())));

    [Fact]
    public void Json_RoundTrips() => AssertSamePlan(Sample(), PlanSerializer.FromJson(PlanSerializer.ToJson(Sample())));

    [Fact]
    public void ShareCode_RoundTrips() => AssertSamePlan(Sample(), PlanSerializer.FromShareCode(PlanSerializer.ToShareCode(Sample())));

    [Fact]
    public void ItemShapes_SurviveEveryFormat()
    {
        foreach (var plan in AllFormats(Sample()))
        {
            var items = plan.Entries[0].Do;
            Assert.Equal(16536u, items[0].Action);
            Assert.Equal("Temperance", items[0].Name);
            Assert.Null(items[0].Any);
            Assert.Null(items[0].Note);

            Assert.Null(items[1].Action);
            Assert.Equal([3570u, 7432u], items[1].ActionIds());
            Assert.Equal(["Tetragrammaton", "Divine Benison"], items[1].Any!.Select(i => i.Name));

            Assert.True(items[2].IsNote);
            Assert.Equal("Everything", items[2].Note);

            var second = plan.Entries[1];
            Assert.Equal(0, second.Offset);   // zero is a time, not "automatic"
            Assert.Equal(1, second.HitIndex);
            Assert.Equal(0, second.Severity);
            Assert.Null(plan.Entries[2].Offset);
            Assert.Equal(Job.SCH, plan.Assignees[1].Job);
            Assert.Null(plan.Assignees[2].Job);
        }
    }

    [Fact]
    public void SpecialStrings_SurviveEveryFormat()
    {
        string[] texts =
        [
            "", " ", "true", "false", "yes", "no", "null", "~", "123", "-3.5", "1e3", "0x1F", ".nan", "2026-11-11",
            "a: b", "# hash", "- dash", "[x]", "{y}", "*star", "&amp", "!tag", "%pct", "@at", "`tick`", "'single'",
            "\"double\"", "multi\nline\n", "tab\there", " lead", "trail ", "★★★", "⏱ on CD", "Kera/Soil → Deploy",
            "日本語", "😀", "\\backslash", "line\r\nbreak", "cr\ronly", "\nleading break", "trailing\n\n", "ctrl\u0001char",
            "nbsp\u00A0x", "bom\uFEFFx", "nel\u0085x", "ls\u2028x",
        ];
        var plan = new MitPlan
        {
            Name = "special",
            Fight = "top",
            Entries = texts.Select((t, i) => new PlanEntry { Id = "e" + i, Mechanic = "m", Note = t, Do = [PlanItem.ForNote(t)] }).ToList(),
        };

        foreach (var back in AllFormats(plan))
        {
            Assert.Equal(texts, back.Entries.Select(e => e.Note));
            Assert.Equal(texts, back.Entries.Select(e => e.Do[0].Note));
        }
    }

    [Fact]
    public void SpecialNumbers_SurviveEveryFormat()
    {
        double[] offsets = [0, -0.1, 1e-7, -12345.678, 0.1 + 0.2, double.MaxValue, double.NaN];
        var plan = new MitPlan
        {
            Fight = "top",
            Entries = offsets.Select((o, i) => new PlanEntry { Id = "e" + i, Mechanic = "m", Offset = o, Do = [PlanItem.ForAction(uint.MaxValue - (uint)i, "x")] }).ToList(),
        };

        foreach (var back in AllFormats(plan))
        {
            Assert.Equal(offsets, back.Entries.Select(e => e.Offset!.Value));
            Assert.Equal(uint.MaxValue, back.Entries[0].Do[0].Action);
        }
    }

    /// <summary>
    /// Every settable property of every plan type gets a non-default value; all of them must survive every format.
    /// Guards against a new model property that one of the formats silently drops.
    /// </summary>
    [Fact]
    public void EveryProperty_SurvivesEveryFormat()
    {
        var counter = 0;
        var plan = (MitPlan)Fill(typeof(MitPlan), ref counter, depth: 0);
        var expected = PlanSerializer.ToJson(plan);
        foreach (var json in new[] { "\"seq\": true", "\"whenReady\": true", "\"hitIndex\"", "\"any\"", "\"assignee\"", "\"imported\"" })
            Assert.Contains(json, expected, StringComparison.Ordinal);

        Assert.Equal(expected, PlanSerializer.ToJson(PlanSerializer.FromYaml(PlanSerializer.ToYaml(plan))));
        Assert.Equal(expected, PlanSerializer.ToJson(PlanSerializer.FromJson(expected)));
        Assert.Equal(expected, PlanSerializer.ToJson(PlanSerializer.FromShareCode(PlanSerializer.ToShareCode(plan))));
    }

    private static object Fill(Type type, ref int n, int depth)
    {
        var obj = Activator.CreateInstance(type)!;
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite))
        {
            n++;
            var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            object? value =
                p.Name == nameof(MitPlan.Format) ? MitPlan.CurrentFormat :
                t == typeof(string) ? $"text {n}" :
                t == typeof(bool) ? true :
                t == typeof(int) ? n :
                t == typeof(uint) ? (uint)(1000 + n) :
                t == typeof(double) ? n + 0.25 :
                t == typeof(Job) ? Job.SGE :
                t == typeof(List<string>) ? new List<string> { $"item {n}" } :
                t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>) ? FillList(t, ref n, depth) :
                t.IsClass ? Fill(t, ref n, depth + 1) :
                throw new InvalidOperationException($"{type.Name}.{p.Name}: add a value for {t.Name} to this test");
            p.SetValue(obj, value);
        }
        return obj;
    }

    /// <summary>A one-element list, or null below depth 3 (nested <c>any</c> groups end there).</summary>
    private static IList? FillList(Type listType, ref int n, int depth)
    {
        if (depth >= 3)
            return null;
        var list = (IList)Activator.CreateInstance(listType)!;
        list.Add(Fill(listType.GetGenericArguments()[0], ref n, depth + 1));
        return list;
    }

    private static IEnumerable<MitPlan> AllFormats(MitPlan plan)
    {
        yield return PlanSerializer.FromYaml(PlanSerializer.ToYaml(plan));
        yield return PlanSerializer.FromJson(PlanSerializer.ToJson(plan));
        yield return PlanSerializer.FromShareCode(PlanSerializer.ToShareCode(plan));
    }

    // ---- YAML shape ------------------------------------------------------------------------------------

    [Fact]
    public void Yaml_UsesTheDesignShape()
    {
        var yaml = PlanSerializer.ToYaml(Sample());

        Assert.StartsWith("format: mitcue-plan/1\nid: 6f1c2b9e-0000-4000-8000-000000000001\nname: Carry the Clear – WHM\nfight: top\n", yaml, StringComparison.Ordinal);
        Assert.Contains("- {action: 16536, name: Temperance}", yaml, StringComparison.Ordinal);
        Assert.Contains("- {any: [{action: 3570, name: Tetragrammaton}, {action: 7432, name: Divine Benison}]}", yaml, StringComparison.Ordinal);
        Assert.Contains("- {note: Everything}", yaml, StringComparison.Ordinal);
        Assert.Contains("who: {job: WHM, assignee: White Mage}", yaml, StringComparison.Ordinal);
        Assert.Contains("- {key: H2 SCH, slot: H2, job: SCH}", yaml, StringComparison.Ordinal);
        Assert.Contains("tabs: [TOP – White Mage, TOP – WHM + SCH]", yaml, StringComparison.Ordinal);
        Assert.Contains("hit_index: 1", yaml, StringComparison.Ordinal);
        Assert.Contains("when_ready: true", yaml, StringComparison.Ordinal);
        Assert.Contains("offset: -3.3", yaml, StringComparison.Ordinal);
        Assert.Contains("offset: 0\n", yaml, StringComparison.Ordinal);
        Assert.Contains("severity: 0\n", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Yaml_LeavesOutNullsDefaultsAndEmptyValues()
    {
        var yaml = PlanSerializer.ToYaml(Sample());
        var last = yaml[yaml.IndexOf("- id: e014", StringComparison.Ordinal)..];
        Assert.Equal("- id: e014\n    mechanic: p6.cosmo_memory\n", last);

        foreach (var absent in new[] { "null", "is_note", "isNote", "seq: false", "optional: false", "when_ready: false", "who: {}", "do: []", "~" })
            Assert.DoesNotContain(absent, yaml, StringComparison.Ordinal);
        Assert.Equal(2, yaml.Split("seq: true").Length); // only e013 has seq
    }

    [Fact]
    public void Yaml_ReadsTheDesignExample()
    {
        // docs/DESIGN.md §3.2 (with the elided values filled in)
        const string yaml = """
            format: mitcue-plan/1
            id: 6f1c0000-aaaa-bbbb-cccc-000000000000
            name: Carry the Clear – WHM
            fight: top
            source: { kind: google-sheet, url: "https://docs.google.com/x#gid=777100", tabs: ["TOP – White Mage"], imported: 2026-11-11 }
            assignees:                                 # one per source column; a player picks theirs
              - { key: "White Mage", slot: null, job: WHM }
            entries:
              - id: e012
                mechanic: p1.pantokrator               # anchor (always a mechanic id)
                offset: -3.3                           # seconds relative to the mechanic's hit
                who: { job: WHM }
                do:
                  - { action: 16536, name: Temperance }
                  - { any: [ { action: 3570, name: Tetragrammaton }, { action: 7432, name: Divine Benison } ] }   # either/or
                  - { note: "Everything" }             # unresolved text, shown but not tracked
                seq: false
                target: null
                note: null
                detail: null
                optional: false
                when_ready: false
                severity: 3
                src: "'TOP – White Mage'!C18"
            """;

        var plan = PlanSerializer.FromYaml(yaml);
        Assert.Equal("6f1c0000-aaaa-bbbb-cccc-000000000000", plan.Id);
        Assert.Equal("2026-11-11", plan.Source!.Imported);
        Assert.Equal(["TOP – White Mage"], plan.Source.Tabs);
        var assignee = Assert.Single(plan.Assignees);
        Assert.Null(assignee.Slot);
        Assert.Equal(Job.WHM, assignee.Job);
        var entry = Assert.Single(plan.Entries);
        Assert.Equal(-3.3, entry.Offset);
        Assert.Equal(Job.WHM, entry.Who.Job);
        Assert.Equal(3, entry.Do.Count);
        Assert.Equal(2, entry.Do[1].Any!.Count);
        Assert.Equal("Everything", entry.Do[2].Note);
        Assert.Null(entry.Target);
        Assert.False(entry.Seq);
        Assert.Equal(3, entry.Severity);
        Assert.Equal("'TOP – White Mage'!C18", entry.Src);
    }

    // ---- tolerance ---------------------------------------------------------------------------------------

    [Fact]
    public void Yaml_ToleratesUnknownFieldsAndLenientValues()
    {
        const string yaml = """
            format: MITCUE-PLAN/1
            name: Lenient
            fight: top
            author: someone          # unknown
            assignees:
              - { key: A, job: White Mage, colour: blue }
              - { key: B, job: sch }
              - { key: C, job: 28 }
            entries:
              - id: e1
                mechanic: p1.x
                importance: high     # unknown
                who: { job: AST, team: 1 }
                do:
                  - Temperance
                  - { action: 3570, name: Tetragrammaton, icon: 2633 }
                  - ~
            """;

        var plan = PlanSerializer.FromYaml(yaml);
        Assert.Equal(MitPlan.CurrentFormat, plan.Format);
        Assert.Equal([Job.WHM, Job.SCH, Job.SCH], plan.Assignees.Select(a => a.Job!.Value));
        var entry = Assert.Single(plan.Entries);
        Assert.Equal(Job.AST, entry.Who.Job);
        Assert.Equal(2, entry.Do.Count);                  // the null item is dropped
        Assert.Equal("Temperance", entry.Do[0].Note);     // plain text becomes a note
        Assert.Equal(3570u, entry.Do[1].Action);
    }

    [Fact]
    public void Json_ToleratesUnknownFields()
    {
        const string json = """
            {
              "format": "mitcue-plan/1",
              "name": "Lenient",
              "fight": "top",
              "futureField": { "nested": [1, 2, 3] },
              "entries": [ { "id": "e1", "mechanic": "m", "whenReady": true, "extra": 1, "do": [ { "action": 1, "flavour": "x" } ] } ],
            }
            """;
        var plan = PlanSerializer.FromJson(json);
        Assert.True(plan.Entries[0].WhenReady);
        Assert.Equal(1u, plan.Entries[0].Do[0].Action);
    }

    [Fact]
    public void MissingFormat_IsTheCurrentFormat()
    {
        Assert.Equal(MitPlan.CurrentFormat, PlanSerializer.FromYaml("name: x\nfight: top\n").Format);
        Assert.Equal(MitPlan.CurrentFormat, PlanSerializer.FromJson("""{"name":"x","fight":"top","format":null}""").Format);
    }

    [Fact]
    public void Readers_RepairNullCollections()
    {
        var plan = PlanSerializer.FromYaml("fight: top\nassignees: ~\nentries:\n  - { id: a, mechanic: m, who: ~, do: ~ }\n  - ~\n");
        Assert.Empty(plan.Assignees);
        var entry = Assert.Single(plan.Entries);
        Assert.NotNull(entry.Who);
        Assert.Empty(entry.Do);

        var json = PlanSerializer.FromJson("""{"fight":"top","entries":[{"id":"a","who":null,"do":null},null],"assignees":null}""");
        Assert.NotNull(Assert.Single(json.Entries).Who);
        Assert.False(string.IsNullOrEmpty(json.Id));
    }

    [Fact]
    public void FromYaml_AcceptsOurJson()
    {
        var plan = PlanSerializer.FromYaml(PlanSerializer.ToJson(Sample()));
        AssertSamePlan(Sample(), plan);
        Assert.True(plan.Entries[1].WhenReady); // camelCase keys are not lost
    }

    [Fact]
    public void Parse_DetectsTheFormat()
    {
        var sample = Sample();
        AssertSamePlan(sample, PlanSerializer.Parse(PlanSerializer.ToShareCode(sample)));
        AssertSamePlan(sample, PlanSerializer.Parse(PlanSerializer.ToJson(sample)));
        AssertSamePlan(sample, PlanSerializer.Parse(PlanSerializer.ToYaml(sample)));
        AssertSamePlan(sample, PlanSerializer.Parse("```yaml\n" + PlanSerializer.ToYaml(sample) + "```\n"));
        AssertSamePlan(sample, PlanSerializer.Parse("  \n" + PlanSerializer.ToShareCode(sample) + "\n"));
        Assert.Throws<FormatException>(() => PlanSerializer.Parse("   "));
    }

    // ---- share codes ---------------------------------------------------------------------------------------

    [Fact]
    public void ShareCode_IsPrefixedBase64UrlOfDeflatedJson()
    {
        var code = PlanSerializer.ToShareCode(Sample());
        Assert.StartsWith("MITCUE1:", code, StringComparison.Ordinal);
        var payload = code["MITCUE1:".Length..];
        Assert.Matches("^[A-Za-z0-9_-]+$", payload);

        var b64 = payload.Replace('-', '+').Replace('_', '/');
        b64 += new string('=', (4 - b64.Length % 4) % 4);
        using var inflate = new DeflateStream(new MemoryStream(Convert.FromBase64String(b64)), CompressionMode.Decompress);
        using var reader = new StreamReader(inflate, Encoding.UTF8);
        var json = reader.ReadToEnd();
        Assert.StartsWith("{\"format\":\"mitcue-plan/1\"", json, StringComparison.Ordinal);
        AssertSamePlan(Sample(), PlanSerializer.FromJson(json));
        Assert.True(code.Length < PlanSerializer.ToJson(Sample()).Length / 2, "the share code should be much smaller than the JSON");
    }

    [Fact]
    public void ShareCode_ToleratesChatFormatting()
    {
        var code = PlanSerializer.ToShareCode(Sample());
        var wrapped = string.Join("\n", code.Chunk(40).Select(c => new string(c)));
        AssertSamePlan(Sample(), PlanSerializer.FromShareCode("  `" + wrapped + "`  "));
        AssertSamePlan(Sample(), PlanSerializer.FromShareCode("\"mitcue1:" + code[8..] + "\""));
        AssertSamePlan(Sample(), PlanSerializer.FromShareCode(code + "=="));
    }

    [Theory]
    [InlineData("hello", "not a mitcue share code")]
    [InlineData("", "not a mitcue share code")]
    [InlineData("{\"format\":\"mitcue-plan/1\"}", "looks like plan json")]
    [InlineData("format: mitcue-plan/1", "looks like plan yaml")]
    [InlineData("MITCUE2:AAAA", "MITCUE2")]
    [InlineData("MITCUE2:AAAA", "update")]
    [InlineData("MITCUE1:", "empty")]
    [InlineData("MITCUE1:$$$$", "damaged")]
    [InlineData("MITCUE1:q1ZKyy_NS1GyUlA", "damaged")]
    public void ShareCode_RejectsOtherTextWithAClearMessage(string code, string expected)
    {
        var ex = Assert.Throws<FormatException>(() => PlanSerializer.FromShareCode(code));
        Assert.Contains(expected, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShareCode_RejectsTruncatedAndCorruptedCodes()
    {
        var code = PlanSerializer.ToShareCode(Sample());
        Assert.Throws<FormatException>(() => PlanSerializer.FromShareCode(code[..(code.Length / 2)]));

        var chars = code.ToCharArray();
        for (var i = 12; i < chars.Length; i += 7)
            chars[i] = chars[i] == 'A' ? 'B' : 'A';
        Assert.Throws<FormatException>(() => PlanSerializer.FromShareCode(new string(chars)));
    }

    [Fact]
    public void ShareCode_RejectsADecompressionBomb()
    {
        using var packed = new MemoryStream();
        using (var deflate = new DeflateStream(packed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var zeros = new byte[1024 * 1024];
            for (var i = 0; i < 20; i++)
                deflate.Write(zeros);
        }
        var code = "MITCUE1:" + Convert.ToBase64String(packed.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var ex = Assert.Throws<FormatException>(() => PlanSerializer.FromShareCode(code));
        Assert.Contains("too large", ex.Message, StringComparison.Ordinal);
    }

    // ---- other formats are rejected ------------------------------------------------------------------------

    [Theory]
    [InlineData("mitcue-plan/2", "newer")]
    [InlineData("mitcue-fight/1", "does not look like")]
    [InlineData("xivplan", "does not look like")]
    public void OtherFormats_AreRejected(string format, string why)
    {
        var plan = Sample();
        plan.Format = format;

        var fromYaml = Assert.Throws<FormatException>(() => PlanSerializer.FromYaml(PlanSerializer.ToYaml(plan)));
        var fromJson = Assert.Throws<FormatException>(() => PlanSerializer.FromJson(PlanSerializer.ToJson(plan)));
        var fromCode = Assert.Throws<FormatException>(() => PlanSerializer.FromShareCode(PlanSerializer.ToShareCode(plan)));
        foreach (var ex in new[] { fromYaml, fromJson, fromCode })
        {
            Assert.Contains(format, ex.Message, StringComparison.Ordinal);
            Assert.Contains(MitPlan.CurrentFormat, ex.Message, StringComparison.Ordinal);
            Assert.Contains(why, ex.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ReadersReportTheProblemAndWhere()
    {
        var yaml = Assert.Throws<FormatException>(() => PlanSerializer.FromYaml("fight: top\nentries:\n  - id: a\n    offset: soon\n"));
        Assert.Contains("line 4", yaml.Message, StringComparison.Ordinal);
        Assert.Contains("soon", yaml.Message, StringComparison.Ordinal);

        var job = Assert.Throws<FormatException>(() => PlanSerializer.FromYaml("assignees:\n  - { key: x, job: Wizard }\n"));
        Assert.Contains("Wizard", job.Message, StringComparison.Ordinal);

        var syntax = Assert.Throws<FormatException>(() => PlanSerializer.FromYaml("fight: top\nentries: [ {id: a\n"));
        Assert.Contains("YAML", syntax.Message, StringComparison.Ordinal);

        var json = Assert.Throws<FormatException>(() => PlanSerializer.FromJson("""{"entries":[{"offset":"soon"}]}"""));
        Assert.Contains("$.entries[0].offset", json.Message, StringComparison.Ordinal);

        Assert.Throws<FormatException>(() => PlanSerializer.FromJson("{ not json"));
        Assert.Throws<FormatException>(() => PlanSerializer.FromJson("null"));
        Assert.Throws<FormatException>(() => PlanSerializer.FromJson(string.Empty));
        Assert.Throws<FormatException>(() => PlanSerializer.FromYaml(string.Empty));
        Assert.Throws<FormatException>(() => PlanSerializer.FromYaml("- a\n- b\n"));
    }

    [Fact]
    public void ShareCodes_AreNotReadAsYamlOrJson()
    {
        var code = PlanSerializer.ToShareCode(Sample());
        Assert.Contains("share code", Assert.Throws<FormatException>(() => PlanSerializer.FromYaml(code)).Message, StringComparison.Ordinal);
        Assert.Contains("share code", Assert.Throws<FormatException>(() => PlanSerializer.FromJson(code)).Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Chat wraps codes in backticks (Discord inline code) or quotes; <see cref="PlanSerializer.FromShareCode"/> ignores
    /// them, and <see cref="PlanSerializer.Parse"/> used to send such a code to the YAML reader ("Invalid plan YAML").
    /// </summary>
    [Theory]
    [InlineData("`{0}`")]
    [InlineData("``{0}``")]
    [InlineData("\"{0}\"")]
    [InlineData("'{0}'")]
    [InlineData("  ` {0} `  ")]
    [InlineData("```\n`{0}`\n```")]
    public void Parse_ReadsShareCodesWrappedInBackticksOrQuotes(string wrapper)
    {
        var text = string.Format(System.Globalization.CultureInfo.InvariantCulture, wrapper, PlanSerializer.ToShareCode(Sample()));
        AssertSamePlan(Sample(), PlanSerializer.Parse(text));

        // The other readers recognize the wrapped code too, and say what it is.
        Assert.Contains("share code", Assert.Throws<FormatException>(() => PlanSerializer.FromYaml(text)).Message, StringComparison.Ordinal);
        Assert.Contains("share code", Assert.Throws<FormatException>(() => PlanSerializer.FromJson(text)).Message, StringComparison.Ordinal);
    }

    // ---- hostile or sloppy input -----------------------------------------------------------------------------

    /// <summary>
    /// A resolved YAML alias is the same object as its anchor, so this item contains itself. Repairing it overflowed the
    /// stack, which cannot be caught: it ended the test host (and, in the plugin, the game).
    /// </summary>
    [Fact]
    public void Yaml_SelfReferencingAlias_IsRejected()
    {
        const string yaml = "format: mitcue-plan/1\nfight: top\nentries:\n  - id: e1\n    mechanic: p1.pull\n    do:\n      - &x {any: [*x]}\n";
        var ex = Assert.Throws<FormatException>(() => PlanSerializer.FromYaml(yaml));
        Assert.Contains("alias", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("line 7", ex.Message, StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => PlanSerializer.Parse(yaml));
    }

    /// <summary>
    /// Each line refers twice to the line before, so the plan doubles per line: these 0.5 KB expanded to 4096 actions
    /// (3.7 MB of JSON); 30 lines took 16 s to read, and saving ran out of memory.
    /// </summary>
    [Fact]
    public void Yaml_DoublingAliases_AreRejectedNotExpanded()
    {
        var yaml = new StringBuilder("format: mitcue-plan/1\nfight: top\nentries:\n  - id: e0\n    mechanic: p1.pull\n    do:\n      - &a0 {action: 16536, name: Temperance}\n");
        for (var i = 1; i <= 12; i++)
            yaml.Append("      - &a").Append(i).Append(" {any: [*a").Append(i - 1).Append(", *a").Append(i - 1).Append("]}\n");

        var ex = Assert.Throws<FormatException>(() => PlanSerializer.FromYaml(yaml.ToString()));
        Assert.Contains("*a0", ex.Message, StringComparison.Ordinal);
        Assert.Contains("line 8", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>Every alias is rejected, wherever it is: even a harmless-looking one would make two entries share one item object.</summary>
    [Theory]
    [InlineData("entries:\n  - id: e1\n    mechanic: m\n    do:\n      - &t {action: 16536, name: Temperance}\n  - id: e2\n    mechanic: n\n    do:\n      - *t\n")]
    [InlineData("base: &b {id: e1, mechanic: m}\nentries:\n  - *b\n")]
    [InlineData("base: &b {mechanic: m}\nentries:\n  - <<: *b\n    id: e1\n")]
    [InlineData("author: &a someone\nreviewer: *a\nentries: []\n")]
    public void Yaml_Aliases_AreRejectedAnywhere(string body)
    {
        var ex = Assert.Throws<FormatException>(() => PlanSerializer.FromYaml("fight: top\n" + body));
        Assert.Contains("alias", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Yaml_AnchorsWithoutAliases_AreHarmlessAndAccepted()
    {
        var plan = PlanSerializer.FromYaml("fight: top\nentries:\n  - &e {id: e1, mechanic: m, do: [&t {action: 16536, name: Temperance}]}\n");
        Assert.Equal(16536u, Assert.Single(Assert.Single(plan.Entries).Do).Action);
    }

    /// <summary>A repeated key used to keep only its last value: here e1 and e2 were dropped without a word.</summary>
    [Theory]
    [InlineData("format: mitcue-plan/1\nfight: top\nentries:\n  - { id: e1, mechanic: p1.a }\n  - { id: e2, mechanic: p1.b }\nentries:\n  - { id: e3, mechanic: p1.c }\n", "entries", "line 6")]
    [InlineData("fight: top\nentries:\n  - { id: e1, mechanic: p1.a, id: e9 }\n", "id", "line 3")]
    [InlineData("fight: top\nentries:\n  - id: e1\n    mechanic: p1.a\n    who: { job: WHM }\n    who: { job: SCH }\n", "who", "line 6")]
    [InlineData("fight: top\nunknown: 1\nunknown: 2\n", "unknown", "line 3")]
    public void Yaml_DuplicateKeys_AreRejected(string yaml, string key, string where)
    {
        var ex = Assert.Throws<FormatException>(() => PlanSerializer.FromYaml(yaml));
        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(key, ex.Message, StringComparison.Ordinal);
        Assert.Contains(where, ex.Message, StringComparison.Ordinal);
    }

    /// <summary>The same for JSON, which <see cref="PlanSerializer.FromYaml"/> and <see cref="PlanSerializer.Parse"/> also accept.</summary>
    [Fact]
    public void Json_DuplicateKeys_AreRejected()
    {
        const string json = """{"fight":"top","entries":[{"id":"e1","mechanic":"a"},{"id":"e2","mechanic":"b"}],"entries":[{"id":"e3","mechanic":"c"}]}""";
        foreach (var read in new Func<string, MitPlan>[] { PlanSerializer.FromJson, PlanSerializer.FromYaml, PlanSerializer.Parse, t => PlanSerializer.FromShareCode(ShareCodeOf(t)) })
            Assert.Contains("duplicate", Assert.Throws<FormatException>(() => read(json)).Message, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<FormatException>(() => PlanSerializer.FromJson("""{"fight":"top","entries":[{"id":"e1","who":{"job":"WHM","Job":"SCH"}}]}"""));
    }

    /// <summary>
    /// Every reader accepts the same job spellings. JSON (and so share codes) used to take any number (99) and flag
    /// combinations ("WHM, SCH" read as SCH), so a plan imported from a share code could be saved as YAML that MitCue
    /// could not read back ("Unknown job '99'").
    /// </summary>
    [Theory]
    [InlineData("\"WHM\"", "WHM", Job.WHM)]
    [InlineData("\"whm\"", "whm", Job.WHM)]
    [InlineData("\"White Mage\"", "White Mage", Job.WHM)]
    [InlineData("24", "24", Job.WHM)]
    [InlineData("\"28\"", "\"28\"", Job.SCH)]
    [InlineData("\"None\"", "None", Job.None)]
    public void Readers_AcceptTheSameJobs(string json, string yaml, Job expected)
    {
        foreach (var plan in JobPlans(json, yaml))
        {
            Assert.Equal(expected, plan.Assignees[0].Job);
            Assert.Equal(expected, plan.Entries[0].Who.Job);
            foreach (var back in AllFormats(plan))
                Assert.Equal(expected, back.Entries[0].Who.Job);
        }
    }

    [Theory]
    [InlineData("99", "99")]
    [InlineData("\"99\"", "\"99\"")]
    [InlineData("1", "1")]
    [InlineData("0", "0")]
    [InlineData("\"WHM, SCH\"", "WHM, SCH")]
    [InlineData("\"Wizard\"", "Wizard")]
    public void Readers_RejectTheSameJobs(string json, string yaml)
    {
        var (fromJson, fromYaml) = JobDocuments(json, yaml);
        var errors = new[]
        {
            Assert.Throws<FormatException>(() => PlanSerializer.FromJson(fromJson)),
            Assert.Throws<FormatException>(() => PlanSerializer.FromShareCode(ShareCodeOf(fromJson))),
            Assert.Throws<FormatException>(() => PlanSerializer.FromYaml(fromYaml)),
        };
        foreach (var ex in errors)
            Assert.Contains($"Unknown job '{yaml.Trim('"')}'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("$.assignees[0].job", errors[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UndefinedJobs_AreNotReadBackFromAnyFormat()
    {
        var plan = Sample();
        plan.Entries[0].Who.Job = (Job)99;
        Assert.Throws<FormatException>(() => PlanSerializer.FromYaml(PlanSerializer.ToYaml(plan)));
        Assert.Throws<FormatException>(() => PlanSerializer.FromJson(PlanSerializer.ToJson(plan)));
        Assert.Throws<FormatException>(() => PlanSerializer.FromShareCode(PlanSerializer.ToShareCode(plan)));
    }

    private static (string Json, string Yaml) JobDocuments(string json, string yaml) =>
    (
        $$$"""{"fight":"top","assignees":[{"key":"a","job":{{{json}}}}],"entries":[{"id":"e1","mechanic":"m","who":{"job":{{{json}}}}}]}""",
        $"fight: top\nassignees:\n  - key: a\n    job: {yaml}\nentries:\n  - id: e1\n    mechanic: m\n    who:\n      job: {yaml}\n"
    );

    private static IEnumerable<MitPlan> JobPlans(string json, string yaml)
    {
        var (fromJson, fromYaml) = JobDocuments(json, yaml);
        yield return PlanSerializer.FromJson(fromJson);
        yield return PlanSerializer.FromShareCode(ShareCodeOf(fromJson));
        yield return PlanSerializer.FromYaml(fromYaml);
    }

    /// <summary>A share code of exactly this JSON, as a hand-made code or one from another MitCue version could hold.</summary>
    private static string ShareCodeOf(string json)
    {
        using var packed = new MemoryStream();
        using (var deflate = new DeflateStream(packed, CompressionLevel.SmallestSize, leaveOpen: true))
            deflate.Write(Encoding.UTF8.GetBytes(json));
        return "MITCUE1:" + Convert.ToBase64String(packed.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>YAML used to leave out <c>any: []</c> like any empty list, and read the item back as nothing at all.</summary>
    [Fact]
    public void EmptyAnyGroup_SurvivesEveryFormat()
    {
        var plan = Sample();
        plan.Entries[0].Do.Add(PlanItem.AnyOf([]));
        plan.Entries[0].Do.Add(PlanItem.AnyOf([PlanItem.AnyOf([])]));

        Assert.Contains("- {any: []}\n", PlanSerializer.ToYaml(plan), StringComparison.Ordinal);
        Assert.Contains("- {any: [{any: []}]}\n", PlanSerializer.ToYaml(plan), StringComparison.Ordinal);
        foreach (var back in AllFormats(plan))
        {
            Assert.Empty(back.Entries[0].Do[3].Any!);
            Assert.Empty(Assert.Single(back.Entries[0].Do[4].Any!).Any!);
            AssertSamePlan(plan, back);
        }
    }
}
