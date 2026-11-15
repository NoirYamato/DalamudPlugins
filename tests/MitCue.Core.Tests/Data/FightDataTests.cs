using System.Globalization;
using System.Text.RegularExpressions;
using MitCue.Core.Data;
using MitCue.Core.Fights;
using Xunit.Abstractions;

namespace MitCue.Core.Tests.Data;

/// <summary>
/// Validates the bundled fight catalogs (data/fights/*.yaml) against their cactbot timelines (docs/DESIGN.md §3.1, §8).
/// Every check passes trivially while no fight file exists yet.
/// </summary>
/// <remarks>
/// Timelines are scanned with a simple regex for <c>time "name"</c> lines rather than through the timeline parser,
/// so these checks only depend on the fight files themselves.
/// </remarks>
public partial class FightDataTests(ITestOutputHelper output)
{
    /// <summary>How far (seconds) a mechanic's hit may be from its timeline entry.</summary>
    private const double EntryTolerance = 0.05;

    private static readonly Lazy<IReadOnlyList<(string Path, FightDefinition? Fight, string? Error)>> Files = new(LoadAll);

    private static IEnumerable<(string Path, FightDefinition Fight)> Fights =>
        Files.Value.Where(f => f.Fight != null).Select(f => (f.Path, f.Fight!));

    [GeneratedRegex("""^\s*(\d+(?:\.\d+)?)\s+"([^"]*)"(?:\s|$)""")]
    private static partial Regex EntryLine();

    private static IReadOnlyList<(string Path, FightDefinition? Fight, string? Error)> LoadAll()
    {
        var data = RepoFiles.Data;
        var result = new List<(string, FightDefinition?, string?)>();
        foreach (var path in data.List("fights").Where(p => p.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                result.Add((path, FightLoader.Load(data.ReadText(path), path), null));
            }
            catch (FormatException ex)
            {
                result.Add((path, null, ex.Message));
            }
        }
        return result;
    }

    /// <summary>(time, name) of every entry line of a timeline.</summary>
    private static List<(double Time, string Name)> TimelineEntries(string text)
    {
        var entries = new List<(double, string)>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var m = EntryLine().Match(line);
            if (m.Success)
                entries.Add((double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), m.Groups[2].Value));
        }
        return entries;
    }

    [Fact]
    public void EveryFightFile_Loads()
    {
        output.WriteLine($"{Files.Value.Count} fight file(s): {string.Join(", ", Files.Value.Select(f => f.Path))}");
        var errors = Files.Value.Where(f => f.Error != null).Select(f => f.Error!).ToList();
        Assert.Empty(errors);
    }

    [Fact]
    public void Fights_HaveUniqueKeysAndTerritories_AndUltimateSyncLevels()
    {
        var fights = Fights.Select(f => f.Fight).ToList();
        Assert.Equal(fights.Count, fights.Select(f => f.Key.ToLowerInvariant()).Distinct().Count());
        Assert.Equal(fights.Count, fights.Select(f => f.Territory).Distinct().Count());
        foreach (var (path, fight) in Fights)
        {
            Assert.True(fight.SyncLevel is 80 or 90 or 100, $"{path}: sync level {fight.SyncLevel}");
            Assert.Equal(Path.GetFileNameWithoutExtension(path), fight.Key, ignoreCase: true);
        }
    }

    [Fact]
    public void EveryFight_HasItsTimeline()
    {
        foreach (var (path, fight) in Fights)
            Assert.True(RepoFiles.Data.Exists("timelines/" + fight.TimelineFile), $"{path}: timeline {fight.TimelineFile} is missing");
    }

    [Fact]
    public void EveryMechanicEntry_IsInTheTimelineAtItsHit()
    {
        var problems = new List<string>();
        var checkedCount = 0;
        foreach (var (path, fight) in Fights)
        {
            if (!RepoFiles.Data.Exists("timelines/" + fight.TimelineFile))
                continue; // reported by EveryFight_HasItsTimeline
            var entries = TimelineEntries(TimelineText(fight));
            foreach (var m in fight.Mechanics.Where(m => m.Entry != null))
            {
                checkedCount++;
                if (entries.Any(e => e.Name == m.Entry && Math.Abs(e.Time - m.Hit) <= EntryTolerance))
                    continue;
                var near = entries.Where(e => e.Name == m.Entry).Select(e => e.Time.ToString(CultureInfo.InvariantCulture)).ToList();
                problems.Add($"{path}: {m.Id} expects \"{m.Entry}\" at {m.Hit.ToString(CultureInfo.InvariantCulture)}" +
                             (near.Count > 0 ? $"; the timeline has it at {string.Join(", ", near)}" : "; the timeline has no such entry"));
            }
        }
        output.WriteLine($"{checkedCount} mechanic entries checked");
        Assert.Empty(problems);
    }

    /// <summary>The fight's cactbot timeline with its <c>timeline_additions</c> appended, as <see cref="FightLibrary.LoadTimeline"/> loads it.</summary>
    private static string TimelineText(FightDefinition fight)
    {
        var text = RepoFiles.Data.ReadText("timelines/" + fight.TimelineFile);
        return string.IsNullOrWhiteSpace(fight.TimelineAdditions) ? text : text.TrimEnd() + "\n" + fight.TimelineAdditions;
    }

    [Fact]
    public void EveryHit_IsTheTimeOfATimelineEntry()
    {
        var problems = new List<string>();
        foreach (var (path, fight) in Fights)
        {
            if (!RepoFiles.Data.Exists("timelines/" + fight.TimelineFile))
                continue;
            var times = TimelineEntries(TimelineText(fight)).Select(e => e.Time).ToList();
            foreach (var m in fight.Mechanics)
            {
                foreach (var h in m.Hits.Where(h => !times.Any(t => Math.Abs(t - h) <= EntryTolerance)))
                    problems.Add($"{path}: {m.Id} hit {h.ToString(CultureInfo.InvariantCulture)} is not the time of a timeline entry");
            }
        }
        Assert.Empty(problems);
    }

    [Fact]
    public void Hits_AreSortedAndStartWithHit()
    {
        foreach (var (path, fight) in Fights)
        {
            foreach (var m in fight.Mechanics)
            {
                Assert.True(m.Hits.Count > 0, $"{path}: {m.Id} has no hits");
                Assert.Equal(m.Hit, m.Hits[0]);
                Assert.True(m.Hits.Zip(m.Hits.Skip(1)).All(p => p.First <= p.Second), $"{path}: {m.Id} hits are not in time order");
            }
            Assert.True(fight.Mechanics.Zip(fight.Mechanics.Skip(1)).All(p => p.First.Hit <= p.Second.Hit), $"{path}: mechanics are not sorted by hit");
        }
    }

    /// <summary>
    /// Sub hits are damage times after the mechanic's first hit that are no timeline entries (only coverage counts them): in
    /// time order, none of them a hit.
    /// </summary>
    [Fact]
    public void SubHits_AreAfterTheFirstHit_InTimeOrder_AndNoHits()
    {
        var withSubHits = 0;
        foreach (var (path, fight) in Fights)
        {
            foreach (var m in fight.Mechanics.Where(m => m.SubHits.Count > 0))
            {
                withSubHits++;
                Assert.True(m.SubHits[0] > m.Hit, $"{path}: {m.Id} sub hit {m.SubHits[0]} is not after its hit {m.Hit}");
                Assert.True(m.SubHits.Zip(m.SubHits.Skip(1)).All(p => p.First < p.Second), $"{path}: {m.Id} sub hits are not in time order");
                Assert.DoesNotContain(m.SubHits, m.Hits.Contains);
            }
        }

        Assert.True(withSubHits >= 2, "TOP's Diffuse Wave Cannon Kyrios and TEA's first Splash have sub hits");
    }

    /// <summary>The timing fields (press, heal_lead, series, sub_hits) time the calls planned on a mechanic against its damage: never on a marker.</summary>
    [Fact]
    public void TimingFields_AreOnlyOnDamage()
    {
        foreach (var (path, fight) in Fights)
        {
            foreach (var m in fight.Mechanics.Where(m => m.Kind == MechanicKind.Marker))
            {
                Assert.False(m.PressAfter, $"{path}: {m.Id} is a marker with press: after");
                Assert.Null(m.HealLead);
                Assert.False(m.Series, $"{path}: {m.Id} is a marker with series: true");
                Assert.Empty(m.SubHits);
            }
        }
    }

    [Fact]
    public void Ids_AreUnique()
    {
        foreach (var (path, fight) in Fights)
        {
            var mechanicDuplicates = fight.Mechanics.GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key);
            Assert.True(!mechanicDuplicates.Any(), $"{path}: duplicate mechanic ids {string.Join(", ", mechanicDuplicates)}");
            var phaseDuplicates = fight.Phases.GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key);
            Assert.True(!phaseDuplicates.Any(), $"{path}: duplicate phase ids {string.Join(", ", phaseDuplicates)}");
        }
    }

    [Fact]
    public void Phases_AreSortedByStart_AndMechanicsUseThem()
    {
        foreach (var (path, fight) in Fights)
        {
            Assert.True(fight.Phases.Count > 0, $"{path}: no phases");
            Assert.True(fight.Phases.Zip(fight.Phases.Skip(1)).All(p => p.First.Start < p.Second.Start), $"{path}: phases are not sorted by start");
            foreach (var m in fight.Mechanics)
                Assert.True(fight.GetPhase(m.Phase) != null, $"{path}: {m.Id} is in unknown phase {m.Phase}");
        }
    }

    [Fact]
    public void CalibrationPhases_Exist()
    {
        foreach (var (path, fight) in Fights)
        {
            foreach (var (sheet, zeros) in fight.Calibration)
            {
                foreach (var phase in zeros.Keys)
                    Assert.True(fight.GetPhase(phase) != null, $"{path}: calibration '{sheet}' names unknown phase '{phase}'");
            }
        }
    }

    [Fact]
    public void MechanicIds_ArePrefixedWithTheirPhase()
    {
        foreach (var (path, fight) in Fights)
        {
            foreach (var m in fight.Mechanics)
                Assert.True(m.Id.StartsWith(m.Phase + ".", StringComparison.OrdinalIgnoreCase), $"{path}: {m.Id} is not prefixed with its phase {m.Phase}");
        }
    }

    [Fact]
    public void EmbeddedFightLibrary_HasTheSameFights()
    {
        var library = new FightLibrary(new EmbeddedDataSource());
        Assert.Equal(Fights.Select(f => f.Fight.Key).Order(), library.Fights.Select(f => f.Key).Order());
    }
}
