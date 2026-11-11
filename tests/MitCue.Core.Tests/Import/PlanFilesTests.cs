using MitCue.Core.Data;
using MitCue.Core.Import;
using MitCue.Core.Plans;
using MitCue.Core.Tests.Data;

namespace MitCue.Core.Tests.Import;

/// <summary>
/// The plans shipped in plans/ (embedded in MitCue.Core and bundled with the plugin) are the importer's output for the
/// sheet fixtures, they load, and they are valid for TOP at level 90.
/// </summary>
/// <remarks>
/// After a change to the importer, the knowledge base or the fixtures, regenerate them with
/// <c>MITCUE_UPDATE_PLANS=1 dotnet test --filter FullyQualifiedName~PlanFilesTests</c> and review the diff.
/// </remarks>
public class PlanFilesTests
{
    private static readonly string[] FileNames = ["top-noirmit-whm.yaml", "top-lpdu.yaml"];

    public static TheoryData<string> Files
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var file in FileNames)
                data.Add(file);
            return data;
        }
    }

    private static string PlansDir => Path.Combine(RepoFiles.Root, "plans");

    private static bool Update => Environment.GetEnvironmentVariable("MITCUE_UPDATE_PLANS") is "1" or "true";

    [Theory]
    [MemberData(nameof(Files))]
    public void PlanFile_IsTheImportOfItsFixture(string file)
    {
        var bundled = PlanImports.Bundled.Single(b => b.File == file);
        var yaml = PlanSerializer.ToYaml(PlanImports.Import(bundled).Plan);
        var path = Path.Combine(PlansDir, file);
        if (Update)
        {
            Directory.CreateDirectory(PlansDir);
            File.WriteAllText(path, yaml);
        }

        Assert.True(File.Exists(path), $"{path} is missing; generate it with MITCUE_UPDATE_PLANS=1.");
        Assert.True(yaml == File.ReadAllText(path), $"plans/{file} is not the importer's output for its fixture; regenerate it with MITCUE_UPDATE_PLANS=1 and review the diff.");
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void PlanFile_LoadsAndIsValid(string file)
    {
        var plan = PlanSerializer.FromYaml(File.ReadAllText(Path.Combine(PlansDir, file)));
        Assert.Equal("top", plan.Fight);
        Assert.NotEmpty(plan.Entries);
        Assert.Empty(PlanValidator.Validate(plan, PlanImports.Top, RepoFiles.Actions));

        // The validation as spelled out: every mechanic exists, every action exists and the entry's job can use it at 90.
        foreach (var entry in plan.Entries)
        {
            Assert.NotNull(PlanImports.Top.GetMechanic(entry.Mechanic));
            foreach (var id in entry.Do.SelectMany(i => i.ActionIds()))
            {
                var action = RepoFiles.Actions.Get(id);
                Assert.NotNull(action);
                Assert.True(action.AvailableAt(90), $"{entry.Id}: {action.Name} at 90");
                if (entry.Who.Job is { } job)
                    Assert.True(action.UsableBy(job), $"{entry.Id}: {action.Name} is not a {job} action");
            }
        }
    }

    [Fact]
    public void PlanFiles_AreBundled()
    {
        // Exactly these: a plan file left in plans/ ships with the plugin (Carry the Clear's combined tab no longer does).
        var bundled = EmbeddedDataSource.Plans().List(string.Empty).ToList();
        Assert.Equal(FileNames.Order(StringComparer.Ordinal), bundled.Order(StringComparer.Ordinal));
        Assert.Equal(FileNames.Order(StringComparer.Ordinal), Directory.GetFiles(PlansDir).Select(f => Path.GetFileName(f)!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void PlanFiles_HaveDistinctStableIds()
    {
        var ids = FileNames.Select(f => PlanSerializer.FromYaml(File.ReadAllText(Path.Combine(PlansDir, f))).Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(ids, PlanImports.Bundled.Select(b => PlanImports.Import(b).Plan.Id));

        // The White Mage plan kept its 0.1.0.1 id through its rename, so saved choices and PlanStore.BundledDefaults find it.
        Assert.Equal(PlanImports.NoirMitWhmId, ids[0]);
    }
}
