using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using AnoMech.Core.Game.Ai;
using AnoMech.Scenarios;

namespace AnoMech.Tests;

// A Move with an arrivalTime plans its bot's departure from where the bot stands as the Move fires, and that deferred
// MoveTo can't be cancelled. So a bot has to leave before its next Move is issued, or it walks back toward the older
// spot; and it has to stand still when a deferred Move is planned, or it leaves from somewhere else and arrives late.
// Every catalog strat on fixed seeds (LintSeeds, default 3), read from each run's trace, where a Move's AiManager lines
// follow the scheduler line of the Move call that logged them.
public partial class StratLintTests
{
    // AiManager's walking speed, which it keeps private.
    private const float RunSpeed = 6.5f;

    // The trace rounds times to 0.01s and distances to 0.1y; a tenth of a second's walk fits in AiManager's 0.25s
    // deadline margin.
    private const float WalkTolerance = 0.1f;

    private static IEnumerable<int> Seeds => Enumerable.Range(1, Math.Max(1, TestContext.Parameters.Get("LintSeeds", 3)));

    // Pairs a strat already has, left as they are, by strat. Fixing one fails the test until its entry goes.
    private static readonly Dictionary<string, string[]> Known = new()
    {
        ["Futures Rewritten — P3 Apocalypse [NA priority / static spreads / OT bait]"] =
            ["planned while walking: FruP3ApocalypseAi.cs Move@36.0 -> FruP3ApocalypseAi.cs Move@37.0"],
    };

    private static IEnumerable<TestCaseData> ScenarioStrats()
        => from scenario in ScenarioCatalog.Create().Scenarios
           from strat in scenario.AiStrats.Select((ai, index) => (ai, index))
           select new TestCaseData(scenario.GetType(), strat.index, $"{scenario.FullName()} [{strat.ai.Name}]")
               .SetName($"{scenario.FullName()} [{strat.ai.Name}] bot moves");

    [TestCaseSource(nameof(ScenarioStrats))]
    public void EachBotLeavesBeforeItsNextMoveAndStandsWhileOneIsPlanned(Type scenarioType, int strat, string name)
    {
        var found = new List<Finding>();
        foreach (var seed in Seeds)
        {
            TraceLog? trace = null;
            var run = ScenarioRun.Execute(scenarioType, strat, seed, new ScenarioRunOptions { Probe = _ => trace ??= (TraceLog)(object)Plugin.Log });
            Assert.That(run.Failure, Is.Null, run.ToString);
            var moves = Moves(trace!.Lines).ToList();
            TestContext.Out.WriteLine($"seed {seed}: {moves.Count} bot moves to t={run.Elapsed:F1}");
            found.AddRange(Findings(moves, seed));
        }
        foreach (var finding in found) TestContext.Out.WriteLine(finding);

        var known = Known.GetValueOrDefault(name, []);
        var keys = found.Select(f => f.Key).Distinct().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(keys.Except(known), Is.Empty, () => string.Join(Environment.NewLine, found.Where(f => !known.Contains(f.Key))));
            Assert.That(known.Except(keys), Is.Empty, "no longer found: take these out of Known");
        });
    }

    // A walk timed to arrive at 10s leaves long after the same bots' next move at 3s.
    [Test]
    public void FindsABotLeavingAfterItsNextMove()
    {
        var found = Lint(ai =>
        {
            ai.Move(1f, () => new AllTo(new Vector2(0f, 10f)), arrivalTime: 10f);
            ai.Move(3f, () => new AllTo(new Vector2(0f, -10f)));
        });
        Assert.That(found.Select(f => f.Key).Distinct(), Is.EqualTo(new[] { $"leaves after its next move: {File} Move@1.0 -> {File} Move@3.0" }));
        Assert.That(found.Select(f => f.Move.Role).Distinct().Count(), Is.EqualTo(8));
    }

    // The lost bot of a 10s flex: a 16y walk from 1s still going when the deadline move at 2s plans its departure.
    [Test]
    public void FindsAMovePlannedWhileItsBotStillWalks()
    {
        var found = Lint(ai =>
        {
            ai.Move(1f, () => new AllTo(new Vector2(0f, 16f)));
            ai.Move(2f, () => new AllTo(new Vector2(0f, -16f)), arrivalTime: 14f);
            ai.Move(16f, () => new AllTo(new Vector2(0f, 0f)), arrivalTime: 19.5f);
        });
        Assert.That(found.Select(f => f.Key).Distinct(), Is.EqualTo(new[] { $"planned while walking: {File} Move@1.0 -> {File} Move@2.0" }));
    }

    private const string File = nameof(StratLintTests) + ".cs";

    private static List<Finding> Lint(Action<AiManager> moves)
    {
        TraceLog? trace = null;
        var run = TimelineRun.Execute(world =>
        {
            trace = (TraceLog)(object)Plugin.Log;
            moves(new AiManager(world));
        });
        Assert.That(run.Failure, Is.Null, run.ToString);
        return Findings(Moves(trace!.Lines).ToList(), 1).ToList();
    }

    private sealed class AllTo(Vector2 spot) : IAiMove
    {
        public Vector2? this[int role] => spot;
    }

    private sealed record MoveLine(string Role, string Site, string At, float Due, float Stamp, float? LeavesAt, float Distance, bool Sprinting)
    {
        public float ArrivesAt => (LeavesAt ?? Stamp) + Distance / (Sprinting ? AiManager.SprintSpeed : RunSpeed);
    }

    private sealed record Finding(string Rule, int Seed, MoveLine Move, MoveLine Next)
    {
        public string Key => $"{Rule}: {SiteFile(Move.Site)} Move@{Move.At} -> {SiteFile(Next.Site)} Move@{Next.At}";

        public override string ToString()
            => $"{Rule}: {Move.Role} (seed {Seed}) moved at {Move.Site} (Move@{Move.At}), leaving at {Move.LeavesAt ?? Move.Stamp:F2} "
               + $"and arriving at {Move.ArrivesAt:F2}, then at {Next.Site} (Move@{Next.At}) at {Next.Due:F2}"
               + (Next.LeavesAt is { } leaves ? $", to leave at {leaves:F2}" : "");

        private static string SiteFile(string site) => site[..site.LastIndexOf(':')];
    }

    private static IEnumerable<Finding> Findings(IReadOnlyList<MoveLine> moves, int seed)
    {
        foreach (var bot in moves.GroupBy(m => m.Role))
        foreach (var (move, next) in bot.Zip(bot.Skip(1)))
        {
            if (move.LeavesAt >= next.Due)
                yield return new Finding("leaves after its next move", seed, move, next);
            else if (next.LeavesAt is not null && move.ArrivesAt > next.Due + WalkTolerance)
                yield return new Finding("planned while walking", seed, move, next);
        }
    }

    // Each bot's moves in the order they were issued.
    private static IEnumerable<MoveLine> Moves(IReadOnlyList<string> lines)
    {
        string? site = null;
        var due = 0f;
        foreach (var line in lines)
        {
            if (Fired().Match(line) is { Success: true } fired)
            {
                site = fired.Groups["site"].Value;
                due = Number(fired.Groups["due"]);
                continue;
            }
            if (!line.Contains("[AiManager] Move@", StringComparison.Ordinal)) continue;
            var move = AiMove().Match(line);
            Assert.That(move.Success, $"an AiManager line this lint can't read: {line}");
            Assert.That(site, Is.Not.Null, $"an AiManager line before any scheduler line: {line}");
            var stamp = Number(move.Groups["t"]);
            yield return new MoveLine(move.Groups["role"].Value, site!, move.Groups["at"].Value, due, stamp,
                move.Groups["delay"].Success ? stamp + Number(move.Groups["delay"]) : null,
                Number(move.Groups["distance"]), move.Groups["sprint"].Success);
        }
    }

    private static float Number(Group group) => float.Parse(group.Value, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\[EventScheduler\] fired (?<site>\S+:\d+) \(due (?<due>-?[\d.]+)\)")]
    private static partial Regex Fired();

    [GeneratedRegex(@"^t=\s*(?<t>-?[\d.]+) \S+\s+\[AiManager\] Move@(?<at>-?[\d.]+): (?<role>\w+) \([-\d.]+,[-\d.]+\) -> \([-\d.]+,[-\d.]+\) for \([-\d.]+,[-\d.]+\) (?<distance>[\d.]+)y(?<sprint> sprinting)?(?:, leaving in (?<delay>[\d.]+)s)?\.$")]
    private static partial Regex AiMove();
}
