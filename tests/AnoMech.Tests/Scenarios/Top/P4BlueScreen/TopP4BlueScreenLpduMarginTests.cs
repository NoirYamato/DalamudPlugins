using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P4BlueScreen;
using static AnoMech.Scenarios.Top.TopP4Constants;
using static AnoMech.Tests.TopP4BlueScreenLpduAiTests;
using static AnoMech.Tests.TopP4BlueScreenLpduHumanTests;

namespace AnoMech.Tests;

// The sunny-day soak only proves nobody dies. These are the clearances the design promises, measured on every
// bot at each hit's snapshot. Run k marks pair k in set 1, pair k + 9 in set 2 and pair k + 18 in set 3, so
// every one of the 28 pairs is played in every set, and each run is repeated with the player in other seats,
// so every role is a bot under every pair; the seeds vary the jitter. The player's seat is left out: under
// the debug driver it walks by movement input and can stop a frame's step past its spot, and in play it is a
// human, whom TopP4BlueScreenLpduHumanTests covers. For the same reason the lines aimed at the player are kept
// out of the table and only held to the slack below.
public class TopP4BlueScreenLpduMarginTests
{
    private const int LpduStrat = 0;
    // Two at least, so a role the player sits in on one seed is a bot on another.
    private static int SeedsPerRun => Math.Max(2, TestContext.Parameters.Get("MarginSeeds", 3));

    // A bot lands anywhere within AiManager's default jitter of its spot; the design's table is rounded to 0.01.
    private const float Jitter = 0.3f, Rounding = 0.02f;

    private static readonly float[] AimsAt = [8.0f, 17.9f, 28.0f];
    private static readonly float[] BeamsAt = [8.6f, 18.5f, 28.6f];
    private static readonly float[] RepeatsAt = [13.0f, 22.9f, 33.0f];
    private static readonly float[] StacksAt = [13.6f, 23.4f, 33.5f];
    private const float Ring1At = 15.7f, Ring2At = 18.1f, Ring3At = 20.1f, Ring4At = 22.1f, Ring5At = 32.9f, Ring6At = 35.2f,
                        Ring7At = 37.2f, Ring8At = 39.2f;

    // The design's clearance table, less the jitter each check can lose: one bot's against a fixed hit, two
    // against a line aimed at another bot. A clump's own stack line runs through its jittered marker, so the
    // 0.6 y both jitters allow is already in that row. The table's row for a neighbour's beam during a straight
    // step-in only matters to a human who steps in before beams 2; the bots step in after them.
    private static readonly (string Check, float Design, float Jitters)[] Table =
    [
        ("quickmarch spot to a neighbour's beam", 2.17f, 2f),
        ("set 1 and 3 stack spot to the nearest repeat line", 1.97f, 2f),
        ("set 2 stack spot to the nearest repeat line", 1.65f, 2f),
        ("set 1 and 3 clump to the other line stack", 6.19f, 2f),
        ("set 2 clump to the other line stack", 4.07f, 2f),
        ("clump inside its own line stack", 2.4f, 0f),
        ("quickmarch spot outside ring 2", 1.5f, 1f),
        ("step-in and set 2 stack spot inside ring 3", 1f, 1f),
        ("set 3 stack spot outside ring 6", 1f, 1f),
        ("set 3 step-in inside ring 7", 1.5f, 1f),
        ("walk back out outside ring 1", 1.36f, 1f),
    ];

    // Off the table: the rings no spot comes near and the lines aimed at the player, with the slack the other
    // P3 and P4 margin tests ask for.
    private const float JitterSlack = 0.5f;

    private static readonly string[] Beyond =
    [
        "inside ring 4", "outside ring 5", "inside ring 8",
        "a bot to the player's beam", "a bot to the player's repeat line", "a bot to the player's line stack",
        "a clump inside the player's line stack",
    ];

    private static readonly PartyRole[][] Pairs =
        PerRole.All.SelectMany((a, i) => PerRole.All.Skip(i + 1).Select(b => new[] { a, b })).ToArray();

    [Test]
    public void KeepsTheDesignsClearancesForEveryRoleAndPair()
    {
        var margins = new Dictionary<string, float>();
        var held = new HashSet<(PartyRole, int, int)>();
        var problems = new List<string>();
        for (var run = 0; run < Pairs.Length; run++)
        for (var i = 0; i < SeedsPerRun; i++)
        {
            var pairs = Enumerable.Range(0, TopP4BlueScreenState.Sets).Select(set => (run + 9 * set) % Pairs.Length).ToArray();
            PartyRole[][] sets = [.. pairs.Select(k => Pairs[k])];
            var player = PerRole.All[(run * SeedsPerRun + i) % 8];
            foreach (var role in PerRole.All.Where(r => r != player))
                for (var set = 0; set < sets.Length; set++)
                    held.Add((role, set, pairs[set]));
            var lines = new List<Line>();
            var result = ScenarioRun.Execute(typeof(TopP4BlueScreenScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
            {
                PlayerRole = player,
                Overrides = o => Pin((TopP4BlueScreenStateOverrides)o, sets),
                Probe = p => Measure(p, player, sets, lines, margins),
            });
            if (!result.Passed) problems.Add(result.ToString());
            if (result.Elapsed < Ring8At) problems.Add($"seed {result.Seed} ended at {result.Elapsed:F1}s, before the last ring");
        }

        TestContext.Out.WriteLine(string.Join(Environment.NewLine,
            margins.OrderBy(m => m.Key).Select(m => $"{m.Key}: {m.Value.ToString("F2", CultureInfo.InvariantCulture)}")));
        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty);
            Assert.That(held, Has.Count.EqualTo(8 * TopP4BlueScreenState.Sets * Pairs.Length), "every role was a bot under every pair in every set");
            foreach (var (check, design, jitters) in Table)
                Assert.That(margins.GetValueOrDefault(check, float.NaN), Is.GreaterThanOrEqualTo(design - jitters * Jitter - Rounding), check);
            foreach (var check in Beyond)
                Assert.That(margins.GetValueOrDefault(check, float.NaN), Is.GreaterThan(JitterSlack), check);
        });
    }

    private sealed record Line(float At, Vector2 Direction, bool Players);

    private static void Measure(ScenarioProbe p, PartyRole player, PartyRole[][] sets, List<Line> lines, Dictionary<string, float> margins)
    {
        void Note(string check, float clearance) => margins[check] = MathF.Min(margins.GetValueOrDefault(check, float.MaxValue), clearance);

        ReadTheLines(p, player, lines);
        var bots = PerRole.All.Where(r => r != player && p.Member(r).IsAlive()).Select(r => Flat(p.Member(r)!.Position)).ToList();
        for (var round = 0; round < AimsAt.Length; round++)
        {
            var aimed = lines.Where(l => l.At == AimsAt[round]).ToList();
            var stackSpot = round == 1 ? "set 2" : "set 1 and 3";
            foreach (var at in bots)
            {
                if (p.Crossed(BeamsAt[round]))
                {
                    var others = aimed.OrderBy(l => LineClearance(at, l.Direction)).Skip(1).ToList();
                    Note("quickmarch spot to a neighbour's beam", others.Where(l => !l.Players).Min(l => LineClearance(at, l.Direction)));
                    foreach (var line in others.Where(l => l.Players))
                        Note("a bot to the player's beam", LineClearance(at, line.Direction));
                }
                if (p.Crossed(RepeatsAt[round]))
                {
                    Note($"{stackSpot} stack spot to the nearest repeat line", aimed.Where(l => !l.Players).Min(l => LineClearance(at, l.Direction)));
                    foreach (var line in aimed.Where(l => l.Players))
                        Note("a bot to the player's repeat line", LineClearance(at, line.Direction));
                }
                if (p.Crossed(StacksAt[round]))
                {
                    var stacks = sets[round].Select(t => (Clearance: LineClearance(at, Vector2.Normalize(Flat(p.Member(t)!.Position))), Players: t == player))
                                            .OrderBy(s => s.Clearance).ToList();
                    Note(stacks[0].Players ? "a clump inside the player's line stack" : "clump inside its own line stack", -stacks[0].Clearance);
                    Note(stacks[1].Players ? "a bot to the player's line stack" : $"{stackSpot} clump to the other line stack", stacks[1].Clearance);
                }
            }
        }

        foreach (var r in bots.Select(at => at.Length()))
        {
            if (p.Crossed(Ring1At)) Note("walk back out outside ring 1", r - 6f);
            if (p.Crossed(Ring2At)) Note("quickmarch spot outside ring 2", r - 12f);
            if (p.Crossed(Ring3At)) Note("step-in and set 2 stack spot inside ring 3", 12f - r);
            if (p.Crossed(Ring4At)) Note("inside ring 4", 18f - r);
            if (p.Crossed(Ring5At)) Note("outside ring 5", r - 6f);
            if (p.Crossed(Ring6At)) Note("set 3 stack spot outside ring 6", r - 12f);
            if (p.Crossed(Ring7At)) Note("set 3 step-in inside ring 7", 12f - r);
            if (p.Crossed(Ring8At)) Note("inside ring 8", 18f - r);
        }
    }

    // The lines are the repeats' telegraphs, read as cast: one per player, from the centre. The player's is the
    // one pointing nearest it.
    private static void ReadTheLines(ScenarioProbe p, PartyRole player, List<Line> lines)
    {
        if (!AimsAt.Any(p.Crossed)) return;
        var aim = AimsAt.First(p.Crossed);
        var directions = p.Fake.Timeline.Rows
            .Where(r => r["kind"] == "cast" && r["id"] == $"{P4ActionId.WaveCannonRepeat}"
                        && MathF.Abs(float.Parse(r["t"], CultureInfo.InvariantCulture) - aim) < 0.5f)
            .Select(r => float.Parse(r["rot"], CultureInfo.InvariantCulture))
            .Select(rot => new Vector2(MathF.Sin(rot), MathF.Cos(rot)))
            .ToList();
        var towardsThePlayer = Vector2.Normalize(Flat(p.Member(player)!.Position));
        var players = directions.MaxBy(d => Vector2.Dot(d, towardsThePlayer));
        lines.AddRange(directions.Select(d => new Line(aim, d, d == players)));
    }
}
