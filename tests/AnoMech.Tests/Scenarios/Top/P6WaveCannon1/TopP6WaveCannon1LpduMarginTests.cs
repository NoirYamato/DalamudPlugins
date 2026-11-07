using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Multiplayer;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top;
using AnoMech.Scenarios.Top.P6WaveCannon1;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Tests.TopP6WaveCannon1LpduAiTests;

namespace AnoMech.Tests;

// The sunny-day soak only proves nobody dies. These are the clearances the design promises, measured on every bot
// against the scenario's own hit areas as each hit takes its targets: every first point both ways round, under both
// exaflare steps, with the lasers' sets and the Wild Charge's target as each seed rolls them, so the bots' walks
// between the spots are measured too. The player's seat is left out: under the debug driver it walks by movement
// input and can stop a frame's step past its spot, and in play it is a human, whom TopP6WaveCannon1LpduHumanTests
// covers. For the same reason the hits aimed at the player are kept out of the table; whoever stands where, everyone
// must still be in Wild Charge's line behind both tanks, and the off tank the farthest for the autos. The seat moves
// round, so every role is a bot under every pattern.
public class TopP6WaveCannon1LpduMarginTests
{
    // Two at least, so a role the player sits in on one seed is a bot on another.
    private static int SeedsPerPattern => Math.Max(2, TestContext.Parameters.Get("MarginSeeds", 3));
    private static readonly float[] Steps = [1.1f, 1.0f];

    // A bot lands anywhere within AiManager's default jitter of its spot. Each walk aims from one jittered spot to the
    // next, so a bot caught mid-walk can stray about one and a half jitters off its unjittered path. The design's table
    // is rounded to 0.01, and lines swing a little with their jittered targets.
    private const float Jitter = 0.3f, Slack = 0.05f;

    // The design's clearances, unjittered on the strat's timing as the sim plays it, less the jitter each check can lose:
    // the measured bot's own, and that of the bot a hit is centred on or aimed at. Puddles drop and go off while the bots
    // walk, each on the first frame after its time, so one lands up to a frame's walk past where the strat's timing alone
    // would drop it. The Wild Charge's line swings about Alpha Omega toward a jittered tank 6.5 y out, so its far end
    // moves 1.6 times as far.
    private static readonly (string Check, float Design, float Jitters)[] Table =
    [
        ("an exaflare", 2.08f, 1.5f),
        ("a puddle", 2.30f, 3f),
        ("another player's laser", 3.42f, 2f),
        ("inside the Wild Charge's line", 4f, 3f),
        ("the Wild Charge's nearest non-tank behind its tanks", 4f, 2f),
        ("another player's Flash Gale", 6f, 2f),
        ("the off tank's lead as the farthest from Alpha Omega", 8.5f, 2f),
    ];

    // Every exaflare and puddle at least this far from every bot, whatever the jitter.
    private const float HitFloor = 1f, LaserFloor = 2f;

    private static IEnumerable<(int First, bool Clockwise, float Step)> Patterns()
        => from first in Enumerable.Range(0, TopP6UnlimitedWaveCannon.CompassPoints)
           from clockwise in new[] { true, false }
           from step in Steps
           select (first, clockwise, step);

    // Multiplayer fixtures leave their hosting session installed, which would make every seat real here too.
    private MultiplayerManager? leftInstalled;

    [SetUp]
    public void Solo()
    {
        leftInstalled = Plugin.Multiplayer;
        DalamudServices.Install(nameof(Plugin.Multiplayer), null!);
    }

    [TearDown]
    public void PutBackWhatWasInstalled() => DalamudServices.Install(nameof(Plugin.Multiplayer), leftInstalled!);

    [Test]
    public void KeepsTheDesignsClearancesForEveryExaflarePattern()
    {
        var margins = new Margins();
        var held = new HashSet<(PartyRole, int, bool, float)>();
        var problems = new List<string>();
        var run = 0;
        foreach (var (first, clockwise, step) in Patterns())
        for (var i = 0; i < SeedsPerPattern; i++, run++)
        {
            var player = PerRole.All[run % 8];
            foreach (var role in PerRole.All.Where(r => r != player))
                held.Add((role, first, clockwise, step));
            var seed = Random.Shared.Next();
            var measure = margins.For(player, $"first {first}, {(clockwise ? "clockwise" : "counter-clockwise")}, step {step}, player {player}, seed {seed}");
            ScenarioRun result;
            AoeQuery.Evaluated += measure;
            try
            {
                result = ScenarioRun.Execute(typeof(TopP6WaveCannon1Scenario), LpduStrat, seed, new ScenarioRunOptions
                {
                    PlayerRole = player,
                    Overrides = o => Pin((TopP6WaveCannon1StateOverrides)o, first, clockwise, step),
                    Probe = margins.Watch,
                });
            }
            finally
            {
                AoeQuery.Evaluated -= measure;
            }
            if (!result.Passed) problems.Add(result.ToString());
            problems.AddRange(margins.TakeProblems().Select(p => $"seed {result.Seed}, {player} the player: {p}"));
        }

        TestContext.Out.WriteLine($"{run} runs; the bots' tightest clearances:{Environment.NewLine}{margins}");
        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(30)));
            Assert.That(held, Has.Count.EqualTo(8 * Patterns().Count()), "every role was a bot under every pattern");
            foreach (var (check, design, jitters) in Table)
                Assert.That(margins.Of(check), Is.GreaterThanOrEqualTo(design - jitters * Jitter - Slack), () => margins.Describe(check));
            Assert.That(margins.Of("an exaflare"), Is.GreaterThanOrEqualTo(HitFloor), () => margins.Describe("an exaflare"));
            Assert.That(margins.Of("a puddle"), Is.GreaterThanOrEqualTo(HitFloor), () => margins.Describe("a puddle"));
            Assert.That(margins.Of("another player's laser"), Is.GreaterThanOrEqualTo(LaserFloor), () => margins.Describe("another player's laser"));
        });
    }

    private sealed class Margins
    {
        private readonly Dictionary<string, (float Clearance, string Where)> tightest = new();
        private readonly List<string> problems = [];
        private ScenarioProbe? probe;
        private PartyRole player;
        private string playing = "";

        public void Watch(ScenarioProbe p) => probe = p;

        public float Of(string check) => tightest.TryGetValue(check, out var t) ? t.Clearance : float.NaN;

        public string Describe(string check) => tightest.TryGetValue(check, out var t) ? $"{check} ({t.Where})" : $"{check} was never measured";

        public List<string> TakeProblems()
        {
            var taken = problems.ToList();
            problems.Clear();
            return taken;
        }

        public Action<AoeQuery> For(PartyRole seat, string pattern)
        {
            probe = null;
            player = seat;
            playing = pattern;
            return Measure;
        }

        private void Measure(AoeQuery hit)
        {
            if (probe is not { } p) return;
            var members = PerRole.All.Where(r => p.Member(r).IsAlive()).ToDictionary(r => r, r => p.Member(r)!.Position);
            var bots = members.Where(m => m.Key != player).ToList();
            switch (hit.ActionId)
            {
                case ActionId.WaveCannon_7BAD or ActionId.WaveCannon_7BAE:
                    foreach (var (_, at) in bots) Note("an exaflare", hit.SignedDistance(at) ?? float.NaN);
                    break;
                case ActionId.WaveCannon_7BAF:
                    foreach (var (_, at) in bots) Note("a puddle", hit.SignedDistance(at) ?? float.NaN);
                    break;
                case ActionId.WaveCannonProtean:
                    var target = AimedAt(hit, members);
                    if (target != player)
                        foreach (var (_, at) in bots.Where(b => b.Key != target)) Note("another player's laser", hit.SignedDistance(at) ?? float.NaN);
                    break;
                case ActionId.WaveCannonWildCharge:
                    WildCharge(hit, members, bots);
                    break;
                case ActionId.Unknown7ddf:
                    if (Vector2.Distance(Flat(members.GetValueOrDefault(player)), Flat(hit.Source.Position)) > 0.01f)
                        foreach (var (_, at) in bots.Where(b => Vector2.Distance(Flat(b.Value), Flat(hit.Source.Position)) > 0.01f))
                            Note("another player's Flash Gale", hit.SignedDistance(at) ?? float.NaN);
                    var farthest = members.MaxBy(m => Flat(m.Value).Length()).Key;
                    if (farthest != OffTank) problems.Add($"t={p.Time:F2} {farthest} was the farthest from Alpha Omega for the autos");
                    else if (player != OffTank)
                        Note("the off tank's lead as the farthest from Alpha Omega",
                             Flat(members[OffTank]).Length() - bots.Where(b => b.Key != OffTank).Max(b => Flat(b.Value).Length()));
                    break;
            }
        }

        private void WildCharge(AoeQuery hit, Dictionary<PartyRole, Vector3> members, List<KeyValuePair<PartyRole, Vector3>> bots)
        {
            var outside = members.Where(m => !(hit.SignedDistance(m.Value) < 0f)).Select(m => m.Key).ToList();
            if (outside.Count > 0) problems.Add($"{string.Join(", ", outside)} stood outside Wild Charge's line");
            if (AimedAt(hit, members) != player)
                foreach (var (_, at) in bots) Note("inside the Wild Charge's line", -(hit.SignedDistance(at) ?? float.NaN));
            var front = members.OrderBy(m => Along(hit.Source, m.Value)).ToList();
            if (!front.Take(2).All(m => m.Key.IsTank()))
                problems.Add($"Wild Charge's front two were {front[0].Key} and {front[1].Key}");
            var tanks = bots.Where(b => b.Key.IsTank()).ToList();
            if (tanks.Count > 0)
                Note("the Wild Charge's nearest non-tank behind its tanks",
                     bots.Where(b => !b.Key.IsTank()).Min(b => Along(hit.Source, b.Value)) - tanks.Max(t => Along(hit.Source, t.Value)));
        }

        private void Note(string check, float clearance)
        {
            if (tightest.TryGetValue(check, out var before) && before.Clearance <= clearance) return;
            tightest[check] = (clearance, $"t={probe!.Time.ToString("F2", CultureInfo.InvariantCulture)}, {playing}");
        }

        public override string ToString()
            => string.Join(Environment.NewLine, tightest.OrderBy(t => t.Key)
                                                        .Select(t => $"  {t.Key}: {t.Value.Clearance.ToString("F2", CultureInfo.InvariantCulture)} ({t.Value.Where})"));
    }

    // A line is aimed at where its target stands as it fires, so its target stands on its axis, ahead of Alpha Omega.
    private static PartyRole AimedAt(AoeQuery line, Dictionary<PartyRole, Vector3> members)
        => members.MinBy(m => Across(line.Source, m.Value) + (Along(line.Source, m.Value) < 0f ? 100f : 0f)).Key;

    private static float Along(Placement line, Vector3 at)
        => (at.X - line.Position.X) * MathF.Sin(line.Rotation) + (at.Z - line.Position.Z) * MathF.Cos(line.Rotation);

    private static float Across(Placement line, Vector3 at)
        => MathF.Abs((at.X - line.Position.X) * MathF.Cos(line.Rotation) - (at.Z - line.Position.Z) * MathF.Sin(line.Rotation));
}
