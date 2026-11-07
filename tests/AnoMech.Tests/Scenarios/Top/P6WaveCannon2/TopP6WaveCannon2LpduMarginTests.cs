using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Multiplayer;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P6WaveCannon2;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Tests.TopP6WaveCannon2LpduAiTests;

namespace AnoMech.Tests;

// The sunny-day soak only proves nobody dies. These are the clearances the LPDU spots keep, measured on every bot
// against the scenario's own hit areas (AoeQuery) as each one resolves, jitter included. Each pattern plays all 70
// ways laser set 1 can take four of the eight, twice, with the player in two different seats, so every role is a bot
// under every split; the Wild Charge goes on each role in turn. The player's seat is left out of the measures: under
// the debug driver it walks by movement input and can stop a frame's step past its spot, and in play it is a human,
// whom TopP6WaveCannon2LpduHumanTests covers. For the same reason a laser aimed at the player is held only to the
// slack below. The Wild Charge counts everyone.
public class TopP6WaveCannon2LpduMarginTests
{
    // Each split plays twice, the player in another seat and on another seed each time; MarginSeeds asks for more.
    private static int RunsPerSplit => Math.Max(2, TestContext.Parameters.Get("MarginSeeds", 2));

    // The least each row keeps with every bot jittered.
    private static readonly (string Check, float Required)[] Table =
    [
        ("every bot outside every exasquare band", 1f),
        ("every bot but its target outside every laser aimed at a bot", 2f),
        ("the tanks lead the Wild Charge line by", 3f),
    ];

    // Off the table, with the slack the P4 and P5 margin tests ask for.
    private const float JitterSlack = 0.5f;

    private static readonly string[] Beyond = ["every bot outside the player's laser", "everyone inside the Wild Charge line"];

    // Seconds to spare, which must be positive.
    private static readonly string[] Timing = ["every bot standing still at each wave and laser it doesn't walk through"];

    // The stack walk crosses the last wave, which fires where nobody walks (rings 0-2 stay clear).
    private const float InFirstLastWave = 23.91f, OutFirstLastWave = 21.91f;
    private const float RunSpeed = 6.5f;

    private static readonly PartyRole[][] Splits =
        Combinations(PerRole.All, 4).Select(set => set.ToArray()).ToArray();

    private static IEnumerable<IEnumerable<PartyRole>> Combinations(IReadOnlyList<PartyRole> from, int count)
        => count == 0 ? [[]]
            : from.SelectMany((role, i) => Combinations(from.Skip(i + 1).ToList(), count - 1).Select(rest => rest.Prepend(role)));

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
    public void TheSplitsAreTheSeventyWaysToPickFour()
        => Assert.That(Splits.Select(s => string.Join(",", s.Order())).Distinct().Count(), Is.EqualTo(70));

    [TestCase(true)]
    [TestCase(false)]
    public void KeepsTheLpduClearancesOnEverySplit(bool inFirst)
    {
        var margins = new Dictionary<string, (float Clearance, string Where)>();
        var problems = new List<string>();
        var measuredAsBots = new HashSet<(int Split, PartyRole Role)>();
        for (var split = 0; split < Splits.Length; split++)
        for (var repeat = 0; repeat < RunsPerSplit; repeat++)
        {
            var player = PerRole.All[(split + 3 * repeat) % 8];
            var setOne = Splits[split];
            PartyRole[] order = [.. setOne, .. PerRole.All.Except(setOne)];
            var target = PerRole.All[(split + repeat) % 8];
            var seed = 7919 * split + 31 * repeat + (inFirst ? 1 : 2);
            var where = $"{(inFirst ? "in" : "out")} first, set 1 {string.Join("+", setOne)}, Wild Charge on {target}, player {player}, seed {seed}";
            var measurer = new Measurer(inFirst, player, where, margins);
            ScenarioRun run;
            AoeQuery.Evaluated += measurer.OnHit;
            try
            {
                run = ScenarioRun.Execute(typeof(TopP6WaveCannon2Scenario), LpduStrat, seed, new ScenarioRunOptions
                {
                    PlayerRole = player,
                    Overrides = o =>
                    {
                        var overrides = (TopP6WaveCannon2StateOverrides)o;
                        overrides.InFirst = inFirst;
                        overrides.ProteanOrder = order;
                        overrides.WildChargeTarget = target;
                    },
                    Probe = measurer.Tick,
                });
            }
            finally
            {
                AoeQuery.Evaluated -= measurer.OnHit;
            }
            if (!run.Passed) problems.Add($"{where}: {run}");
            problems.AddRange(measurer.Problems.Select(problem => $"{where}: {problem}"));
            foreach (var role in PerRole.All.Where(r => r != player)) measuredAsBots.Add((split, role));
        }

        TestContext.Out.WriteLine(string.Join(Environment.NewLine,
            margins.OrderBy(m => m.Key).Select(m => $"{m.Key}: {m.Value.Clearance.ToString("F2", CultureInfo.InvariantCulture)} ({m.Value.Where})")));
        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(30)));
            Assert.That(measuredAsBots, Has.Count.EqualTo(8 * Splits.Length), "every role was a bot under every split");
            foreach (var (check, required) in Table)
                Assert.That(Margin(margins, check), Is.GreaterThanOrEqualTo(required), check);
            foreach (var check in Beyond)
                Assert.That(Margin(margins, check), Is.GreaterThan(JitterSlack), check);
            foreach (var check in Timing)
                Assert.That(Margin(margins, check), Is.GreaterThan(0f), check);
        });
    }

    private static float Margin(Dictionary<string, (float Clearance, string Where)> margins, string check)
        => margins.TryGetValue(check, out var margin) ? margin.Clearance : float.NaN;

    private sealed class Measurer(bool inFirst, PartyRole player, string where, Dictionary<string, (float Clearance, string Where)> margins)
    {
        public readonly List<string> Problems = [];

        private SimWorld? world;
        private readonly Dictionary<PartyRole, float> stoppedAt = new();
        private readonly HashSet<PartyRole> walking = [];
        private readonly HashSet<(float, uint, Placement)> seen = [];

        public void Tick(ScenarioProbe p)
        {
            world ??= p.World;
            foreach (var bot in Bots())
            {
                if (world.Party.Get(bot)!.MoveDestination is not null) walking.Add(bot);
                else if (walking.Remove(bot)) stoppedAt[bot] = p.Time;
            }
        }

        private IEnumerable<PartyRole> Bots() => PerRole.All.Where(r => r != player && world!.Party.Get(r).IsAlive());

        private Vector2 At(PartyRole role) => Flat(world!.Party.Get(role)!.Position);

        private void Note(string check, float clearance)
        {
            if (margins.TryGetValue(check, out var before) && before.Clearance <= clearance) return;
            margins[check] = (clearance, $"t={world!.Events.Elapsed.ToString("F2", CultureInfo.InvariantCulture)}, {where}");
        }

        // Every area check raises Evaluated, and a resolve checks the party and then the NPCs with the same query.
        public void OnHit(AoeQuery hit)
        {
            if (world is null) return;
            if (!seen.Add((world.Events.Elapsed, hit.ActionId, hit.Source))) return;
            switch (hit.ActionId)
            {
                case ActionId.CosmoArrowOmen or ActionId.CosmoArrowDamage:
                    foreach (var bot in Bots()) Note("every bot outside every exasquare band", Clearance(hit, At(bot)));
                    StandingStill();
                    break;
                case ActionId.WaveCannonProtean:
                    var target = TargetOf(hit);
                    foreach (var bot in Bots().Where(b => b != target))
                        Note(target == player ? "every bot outside the player's laser" : "every bot but its target outside every laser aimed at a bot",
                             Clearance(hit, At(bot)));
                    StandingStill();
                    break;
                case ActionId.WaveCannonWildCharge:
                    MeasureTheLine(hit);
                    StandingStill();
                    break;
            }
        }

        // A bot still walking counts the time it needs to get there, as a negative.
        private void StandingStill()
        {
            var now = world!.Events.Elapsed;
            if (MathF.Abs(now - (inFirst ? InFirstLastWave : OutFirstLastWave)) < 0.05f) return;
            foreach (var bot in Bots())
            {
                var member = world.Party.Get(bot)!;
                var spare = member.MoveDestination is { } to
                    ? -Vector2.Distance(At(bot), Flat(to)) / RunSpeed
                    : now - stoppedAt.GetValueOrDefault(bot, 0f);
                Note("every bot standing still at each wave and laser it doesn't walk through", spare);
            }
        }

        // The line runs from Omega's centre to the target as it fires; the front is the end nearest Omega.
        private void MeasureTheLine(AoeQuery hit)
        {
            var members = PerRole.All.Where(r => world!.Party.Get(r).IsAlive()).ToList();
            if (members.Count < 8) Problems.Add($"only {members.Count} alive for the Wild Charge");
            foreach (var role in members) Note("everyone inside the Wild Charge line", -Clearance(hit, At(role)));
            var direction = new Vector2(MathF.Sin(hit.Source.Rotation), MathF.Cos(hit.Source.Rotation));
            var along = members.OrderBy(r => Vector2.Dot(At(r) - Flat(hit.Source.Position), direction)).ToList();
            if (!along.Take(2).All(r => r.IsTank()))
                Problems.Add($"the Wild Charge's front two are {along[0]} and {along[1]}");
            Note("the tanks lead the Wild Charge line by",
                 Vector2.Dot(At(along[2]) - At(along[1]), direction));
        }

        private PartyRole TargetOf(AoeQuery hit)
        {
            var direction = new Vector2(MathF.Sin(hit.Source.Rotation), MathF.Cos(hit.Source.Rotation));
            return PerRole.All.Where(r => world!.Party.Get(r).IsAlive())
                              .Where(r => Vector2.Dot(At(r) - Flat(hit.Source.Position), direction) > 0f)
                              .MinBy(r => Across(At(r) - Flat(hit.Source.Position), direction));
        }

        private static float Across(Vector2 offset, Vector2 direction) => MathF.Abs(offset.X * direction.Y - offset.Y * direction.X);

        private static float Clearance(AoeQuery hit, Vector2 at)
            => hit.SignedDistance(new Vector3(at.X, 0f, at.Y)) ?? throw new InvalidOperationException($"no area for action {hit.ActionId}");
    }
}
