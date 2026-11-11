using MitCue.Core.Import;
using MitCue.Core.Plans;

namespace MitCue.Core.Tests.Import;

/// <summary>The same plan from every reader (fixture JSON, xlsx), and plans that survive YAML and JSON.</summary>
public class ImportRoundTripTests
{
    public static TheoryData<int> Plans => new() { 0, 1, 2 };

    [Theory]
    [MemberData(nameof(Plans))]
    public void XlsxAndFixtureJson_GiveIdenticalPlans(int index)
    {
        var bundled = PlanImports.Bundled[index];
        var json = PlanImports.Import(bundled);

        // Excel cannot store '/' in tab names ("P1/P2" → "P1P2"): pass the original names, as the plugin does.
        var title = Fixtures.Doc(bundled.Sheet).Title;
        var names = Fixtures.Doc(bundled.Sheet).Tabs.OrderBy(t => t.Index).Select(t => t.Name).ToList();
        using var stream = File.OpenRead(Fixtures.Xlsx(bundled.Sheet));
        var xlsx = new PlanImporter().Import(SheetReaders.FromXlsx(stream, title, new XlsxReadOptions { TabNames = names }), bundled.Options);

        Assert.Equal(PlanSerializer.ToYaml(json.Plan), PlanSerializer.ToYaml(xlsx.Plan));
        Assert.Equal(json.Diagnostics.Select(d => d.ToString()), xlsx.Diagnostics.Select(d => d.ToString()));
    }

    [Theory]
    [MemberData(nameof(Plans))]
    public void Yaml_RoundTrips(int index)
    {
        var plan = PlanImports.Import(PlanImports.Bundled[index]).Plan;
        var yaml = PlanSerializer.ToYaml(plan);
        var back = PlanSerializer.FromYaml(yaml);
        Assert.Equal(yaml, PlanSerializer.ToYaml(back));
        Assert.Equal(PlanSerializer.ToJson(plan), PlanSerializer.ToJson(back));
        Assert.Equal(yaml, PlanSerializer.ToYaml(PlanSerializer.FromShareCode(PlanSerializer.ToShareCode(plan))));
    }

    [Fact]
    public void Csv_GivesTheSameEntriesAsTheFixture_ExceptFormatting()
    {
        // A pasted/CSV tab has no colours: the same entries, but nothing is optional (grey) any more.
        var csv = SheetReaders.FromDelimited(File.ReadAllText(Fixtures.Csv("carry-the-clear", "top-white-mage")), ',', "TOP – White Mage");
        var doc = new SheetDocument { Title = "Carry the Clear: TOP Healing Plan", Tabs = [csv] };
        var fromCsv = new PlanImporter().Import(doc, PlanImports.Bundled[0].Options).Plan;
        var fromJson = PlanImports.Whm.Plan;
        Assert.Equal(fromJson.Entries.Count, fromCsv.Entries.Count);
        foreach (var (a, b) in fromJson.Entries.Zip(fromCsv.Entries))
        {
            Assert.Equal((a.Mechanic, a.HitIndex, a.Offset, string.Join("+", a.Names())), (b.Mechanic, b.HitIndex, b.Offset, string.Join("+", b.Names())));
            Assert.False(b.Optional);
        }
    }
}
