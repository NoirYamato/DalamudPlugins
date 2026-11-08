using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Multiplayer;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P5Omega;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Tests.TopP5OmegaLpduAiTests;
using static AnoMech.Tests.TopP6HumanWalk;

namespace AnoMech.Tests;

// The sunny-day soak drives the player's seat with the bots' own AI. Here the seat is a human who plays LPDU from what a
// player can see, and at each cue walks to its LPDU spot a fixed reaction time late, straight, curving or sprinting, never
// stepping round anyone:
//   the run's start: its place in the opening clump;
//   the Final's Diffuse Wave Cannon cast (the first Omega pair, its weapons and which way the first cannons fire are all
//   showing by then): the first pair's stack;
//   the first set's telegraphs vanishing (the cannons' casts end last): the second pair's stack;
//   the Final's Oversampled Wave Cannon cast, which shows the monitors' half: its first Hello World spot, by its mark;
//   its second mark: the middle, or 3 y toward the beetle with a Bind;
//   the strat's call at 53 (nothing on screen marks it): its second Hello World spot, turned with the beetle it can see;
//   the second Hello World's last jumps: the opening clump again.
// Whatever its seat, its job in each Hello World, the Omega pairs, the cannons, the monitors' half, the beetle, its walk
// and its reaction time, nobody may die, both Hello Worlds, the monitors and the Blasters go where the strat sends them,
// both Blaster tethers are on their takers standing on their rim spots just before Blaster, the human keeps more than
// 0.5 y from every hit it dodges, and the bots keep the margin suite's clearance from the takers' Blasters.
//
// A Blaster tether can also start on a human who has none to take. Its line toward the beetle passes the takers waiting
// 3 y toward it only if the human stands within about 0.6 y of the beetle's axis behind them, so the takers fetch it.
// Those cases give the human a tether and a second Hello World job other than the tethers, and at its second mark send
// it loosely into the middle instead: to a random point 0.5-3 y from it, in any direction. A human who stays on its
// first Hello World spot until 53, or walks straight on to its second, isn't covered in general: its own walk still
// fails about one roll in 30 and one in 300 (it reaches the second Hello World late, crosses a Blaster or a taker's
// line, or a curving walk along the rim leaves the arena), and now and then a taker bringing a tether back from the far
// rim after 53 loses it to a stack bot walking out and misses its rim spot at Blaster.
//
// Scripted rolls, each walked straight with no reaction time, cover the rest of the fetch: a human holding its tether
// right beside the beetle or against the wall near it until 55; a human standing with its tether in the line from a
// fetcher's rim spot to the beetle; and a human taking a taker's tether after the gather.
public class TopP5OmegaLpduHumanTests
{
    // Staff and Sword against Staff and Shield puts the two stacks 17 y and 10 y out on cardinals a quarter apart, 19.7 y
    // of walking, and the second pair goes off 3.9 s after the first set's telegraphs vanish. A walker who sets off more
    // than about 0.9 s late is still on its way as it goes off and from about 1.1 s late can be caught, so the walkers
    // here react up to 0.9 s late and only the sprinter 1.2 s.
    private static readonly float[] ReactionTimes = [0f, 0.6f, 0.9f, 1.2f];
    private const float LatestWalker = 0.9f;
    private const float JitterSlack = 0.5f;

    private const float SecondHelloWorldFrom = 45f, GatherCall = 48f, SecondHelloWorldCall = 53f;
    private const float FirstPairHits = 24.15f, SecondPairHits = 28.15f, StackTolerance = 0.75f;
    private const float TethersChecked = 57.5f;
    private const float LooseMiddleFrom = 0.5f, LooseMiddleTo = 3f;

    // A taker that kept upstream's walk-out lands within AiManager's 0.3 y jitter of its rim spot; a fetcher and a human
    // taker land on it.
    private const float OnItsRimSpot = 0.35f;

    // The margin suite's row: the Distant World 16.51 y from Bind 2, against Blaster's 15 y, less both bots' jitter and
    // the table's rounding.
    private const string BotsOutsideABlaster = "every bot but its target outside a Blaster";
    private const float BotsOutsideABlasterAtLeast = 1.51f - 2 * 0.3f - 0.02f;
    private const string TheArenasEdge = "the arena's edge";

    // The scripted rolls take the first set of jobs, where the second Hello World's jobs are these.
    private const int ScriptedJobs = 0;
    private const int NearWorld = 0, DistantWorld = 1, Bind1 = 2, Bind2 = 3, Attack1 = 4, Attack3 = 6;

    // How far the human stands from the line from a fetcher's rim spot to the beetle for the fetcher to wait.
    private const float NearlyInTheLine = 0.8f;

    // Where the human waits after its second mark: the strat's spot in the middle, loosely in the middle or its first
    // Hello World spot; or a spot of its own, written for the beetle north and turned with it, that it is set down on
    // first: beside the beetle, against the wall beside it, or 1 y off the takers' lines in the middle.
    private enum Gather { Lpdu, LooseMiddle, OnItsFirstSpot, BesideTheBeetle, AgainstTheWall, OffTheTakersLines }

    private static Vector2? SetDownOn(Gather gather) => gather switch
    {
        Gather.BesideTheBeetle => new Vector2(0.2f, -19.4f),
        Gather.AgainstTheWall => new Vector2(2f, -19.8f),
        Gather.OffTheTakersLines => new Vector2(1f, -6f),
        _ => null,
    };

    // Beside the beetle and against the wall it is set down before the tethers show.
    private const float SetDownByTheBeetleAt = 45f;

    private static float LeavesForItsSecondSpotAt(Gather gather) => gather switch
    {
        Gather.BesideTheBeetle or Gather.AgainstTheWall => 55f,
        Gather.OffTheTakersLines => 55.5f,
        _ => SecondHelloWorldCall,
    };

    // Rounds of every seat under both monitor halves and all four beetles for each tether case; TetherRounds asks for more.
    private static int TetherRounds => Math.Max(1, TestContext.Parameters.Get("TetherRounds", 1));

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

    private static IEnumerable<TestCaseData> Matrix()
        => from walk in Enum.GetValues<Walk>()
           from delay in ReactionTimes
           where delay <= LatestWalker || walk == Walk.Sprinting
           select new TestCaseData(walk, delay).SetName($"{{m}}({walk}, {delay:F1}s late)");

    // Every seat holds every job of both Hello Worlds in every case, under the set of jobs the case picks; each reaction
    // time the walkers share plays each set once and all 192 Omega pairs.
    [TestCaseSource(nameof(Matrix))]
    public void AHumanPlayingLpduIsNeverCaught(Walk walk, float delay)
    {
        var reaction = Array.IndexOf(ReactionTimes, delay);
        var jobs = ((int)walk + reaction) % HelloWorldJobs.Length;
        var tightest = new Clearances();
        var problems = new List<string>();
        var runs = 0;
        foreach (var seat in PerRole.All)
        for (var person = 0; person < 8; person++, runs++)
            problems.AddRange(Play(Roll.For(seat, person, jobs, walk, reaction), delay, tightest, Gather.Lpdu, out _));

        TestContext.Out.WriteLine($"{TestContext.CurrentContext.Test.Name}: {runs} runs; the human's tightest clearances:{Environment.NewLine}{tightest}");
        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(30)));
            AssertTheClearances(tightest);
        });
    }

    // Every seat holds a tether under both monitor halves and all four beetles, and each seat every second Hello World
    // job but the tethers. The other tether starts on a seat the roll picks, a taker's or not. The human still holds its
    // tether as the takers start fetching in about two runs in three; the rest it passed on as it waited or walked in.
    // A taker sent to fetch it takes it from the human before the walk-out in about half the runs.
    [TestCaseSource(nameof(Matrix))]
    public void TheTakersFetchABlasterTetherFromAHumanGatheringLoosely(Walk walk, float delay)
    {
        var reaction = Array.IndexOf(ReactionTimes, delay);
        var tightest = new Clearances();
        var problems = new List<string>();
        var (runs, held, fetched) = (0, 0, 0);
        for (var round = 0; round < TetherRounds; round++)
        foreach (var seat in PerRole.All)
        for (var halfAndBeetle = 0; halfAndBeetle < 8; halfAndBeetle++, runs++)
        {
            problems.AddRange(Play(Roll.HoldingATether(seat, halfAndBeetle, round, walk, reaction), delay, tightest, Gather.LooseMiddle, out var guide));
            if (guide.HeldAsTheFetchBegan) held++;
            if (guide.TakenFromTheHuman.Any(take => take.Fetching && take.At < TopP5OmegaLpduAi.LastFetchBeforeTheWalkOut)) fetched++;
        }

        TestContext.Out.WriteLine($"{TestContext.CurrentContext.Test.Name}: {runs} runs, the human still holding its tether as the takers start "
                                  + $"fetching in {held} and a taker sent to fetch it taking it from the human before the walk-out in {fetched}; "
                                  + $"its tightest clearances:{Environment.NewLine}{tightest}");
        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(30)));
            AssertTheClearances(tightest);
            Assert.That(held, Is.GreaterThan(runs / 3), "runs where the human still held its tether as the takers started fetching");
            Assert.That(fetched, Is.GreaterThan(runs / 6), "runs where a taker sent to fetch it took the human's tether before the walk-out");
        });
    }

    // Set down before the tethers show on a spot of its own by the beetle, written for the beetle north and turned with
    // it, the human holds its tether there until 55, then walks straight to the second Near World: beside the beetle at
    // (0.2, -19.4), or against the wall 2 y from it at (2, -19.8), 0.1 y from the edge, which its clearances leave out.
    // Its line to the beetle runs that close to the rim, so a taker must meet it out there, before it leaves, under every
    // beetle and seat, with the other tether on Bind 1, on Bind 2 or on Attack 1.
    [TestCase("beside the beetle")]
    [TestCase("against the wall")]
    public void ATakerFetchesTheTetherOfAHumanStandingByTheBeetle(string where)
    {
        var gather = where == "against the wall" ? Gather.AgainstTheWall : Gather.BesideTheBeetle;
        var leaves = LeavesForItsSecondSpotAt(gather);
        var tightest = new Clearances();
        var problems = new List<string>();
        var latest = 0f;
        var runs = 0;
        foreach (var seat in PerRole.All)
        for (var beetle = 0; beetle < 4; beetle++)
        foreach (var other in new[] { Bind1, Bind2, Attack1 })
        {
            var monitors = (beetle + (int)seat) % 2 == 0 ? MonitorSide.Left : MonitorSide.Right;
            var roll = Roll.Scripted(seat, NearWorld, monitors, Direction.Cardinal[beetle], [NearWorld, other], runs++);
            problems.AddRange(Play(roll, 0f, tightest, gather, out var guide));
            if (guide.TakenFromTheHuman.Count > 0 && guide.TakenFromTheHuman[0].At < leaves)
                latest = Math.Max(latest, guide.TakenFromTheHuman[0].At);
            else
                problems.Add($"{roll}: no taker took the tether from the human {where} before it left at {leaves}; "
                             + $"the tethers went {string.Join(", ", guide.TetherPasses)}");
        }

        TestContext.Out.WriteLine($"{TestContext.CurrentContext.Test.Name}: {runs} runs, the human's tether taken from it by t={latest:F2} at the latest; "
                                  + $"its tightest clearances:{Environment.NewLine}{tightest}");
        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(30)));
            AssertTheClearances(tightest, gather == Gather.AgainstTheWall ? TheArenasEdge : null);
        });
    }

    // The human stands with its tether on its first Hello World spot, jump 3's (19, 4), until 53, then walks straight to
    // Attack 3: no set of jobs gives jump 3 the second Near World. With the monitors west and the beetle east that spot is
    // 0.44 y off the line from Bind 1's rim spot to the beetle; the other tether starts on Bind 2, so Bind 1 fetches it.
    // Bind 1 must take it while the human still stands within 0.8 y of that line, hold it there for a second or more
    // without heading for its rim spot, and stand there with it at Blaster. The mirror, the monitors east and the beetle
    // west, puts the human in Bind 2's line.
    [Test]
    public void AFetcherHoldingTheTetherWaitsWhileTheHumanStandsInItsLine()
    {
        var tightest = new Clearances();
        var problems = new List<string>();
        var shortest = float.MaxValue;
        var runs = 0;
        foreach (var seat in PerRole.All)
        foreach (var (monitors, beetle, fetcher, other) in new[] { (MonitorSide.Left, Direction.E, Bind1, Bind2), (MonitorSide.Right, Direction.W, Bind2, Bind1) })
        {
            var roll = Roll.Scripted(seat, Attack3, monitors, beetle, [Attack3, other], runs++);
            var waiting = new FetcherWaiting(roll, roll.Omega.SecondOrder[fetcher]);
            problems.AddRange(Play(roll, 0f, tightest, Gather.OnItsFirstSpot, out _, waiting.Tick));
            problems.AddRange(waiting.Problems.Select(problem => $"{roll}: {problem}"));
            shortest = Math.Min(shortest, waiting.HeldWhileTheHumanStoodInTheLine);
        }

        TestContext.Out.WriteLine($"{TestContext.CurrentContext.Test.Name}: {runs} runs, the fetcher holding the tether while the human stood in its line "
                                  + $"for {shortest:F2} s at the least; the human's tightest clearances:{Environment.NewLine}{tightest}");
        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(30)));
            AssertTheClearances(tightest);
        });
    }

    // Both tethers start on their takers, and the human, without one, is set down at (1, -6), 1 y off their lines, once
    // both stand 3 y toward the beetle north. Bind 1's line sweeps over it as Bind 1 sets out after 53, so the human takes
    // Bind 1's tether after the gather. Bind 1, never sent before, must be sent back for it and stand on its rim spot with
    // a tether at Blaster. The human stays until 55.5, then walks straight to the second Near or Distant World.
    [Test]
    public void ATakerThatLosesItsTetherAfterTheGatherIsSentBackForIt()
    {
        var tightest = new Clearances();
        var problems = new List<string>();
        var (lost, sentBack) = (new List<float>(), new List<float>());
        var runs = 0;
        foreach (var seat in PerRole.All)
        foreach (var job in new[] { NearWorld, DistantWorld })
        {
            var monitors = ((int)seat + job) % 2 == 0 ? MonitorSide.Left : MonitorSide.Right;
            var roll = Roll.Scripted(seat, job, monitors, Direction.N, [Bind1, Bind2], runs++);
            var sent = new SentBackForItsTether(roll);
            problems.AddRange(Play(roll, 0f, tightest, Gather.OffTheTakersLines, out _, sent.Tick));
            problems.AddRange(sent.Problems.Select(problem => $"{roll}: {problem}"));
            if (sent.LostAt is { } lostAt) lost.Add(lostAt);
            if (sent.SentAt is { } sentAt) sentBack.Add(sentAt);
        }

        TestContext.Out.WriteLine($"{TestContext.CurrentContext.Test.Name}: {runs} runs, Bind 1 losing its tether at t={lost.DefaultIfEmpty().Min():F2}-{lost.DefaultIfEmpty().Max():F2} "
                                  + $"and sent back for it at t={sentBack.DefaultIfEmpty().Min():F2}-{sentBack.DefaultIfEmpty().Max():F2}; "
                                  + $"the human's tightest clearances:{Environment.NewLine}{tightest}");
        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(30)));
            AssertTheClearances(tightest);
        });
    }

    private static void AssertTheClearances(Clearances tightest, string? notHeld = null)
    {
        Assert.That(tightest.Within(JitterSlack, notHeld), Is.Empty, "the human's clearances");
        var (clearance, where) = tightest.Of(BotsOutsideABlaster);
        Assert.That(clearance, Is.GreaterThanOrEqualTo(BotsOutsideABlasterAtLeast), $"{BotsOutsideABlaster} ({where})");
    }

    private static List<string> Play(Roll roll, float delay, Clearances tightest, Gather gather, out Guide guide, Action<ScenarioProbe>? watch = null)
    {
        var guiding = guide = new Guide(roll, delay, gather);
        var measure = tightest.For(roll);
        ScenarioRun run;
        AoeQuery.Evaluated += measure;
        try
        {
            run = ScenarioRun.Execute(typeof(TopP5OmegaScenario), LpduStrat, roll.Seed, new ScenarioRunOptions
            {
                PlayerRole = roll.Seat,
                Overrides = o => roll.Omega.Pin((TopP5OmegaStateOverrides)o),
                Takeovers = [new Takeover(0f, null)],
                Probe = p =>
                {
                    tightest.Watch(p);
                    guiding.Tick(p);
                    watch?.Invoke(p);
                },
            });
        }
        finally
        {
            AoeQuery.Evaluated -= measure;
        }

        var problems = new List<string>();
        if (!run.Passed) problems.Add(run.ToString());
        problems.AddRange(guide.Problems);
        if (run.Passed)
        {
            problems.AddRange(tightest.Finish());
            if (guide.Walks < guide.Legs) problems.Add($"the human set off on {guide.Walks} of its {guide.Legs} walks");
            if (guide.Legs != 7) problems.Add($"the human saw {guide.Legs} of its 7 cues");
        }
        if (problems.Count > 0) problems.Add("the Blaster tethers went " + string.Join(", ", guide.TetherPasses));
        return problems.Select(problem => $"{roll} ({delay:F1}s late, {gather}): {problem}").ToList();
    }

    private static List<SimTether> PassableTethers(ScenarioProbe p)
        => [.. p.World.Children.OfType<SimTether>().Where(t => t.IsActive && t.TetherId == TetherId.PassableTether)];

    private static PartyRole? HolderOf(SimTether tether) => (tether.A as ISimPartyMember)?.Role;

    // On the line from `from` to the beetle, at least 0.1 y past `from`, and within `within` of it.
    private static bool InTheLine(Vector2 from, Vector2 beetle, Vector2 at, float within)
    {
        var length = Vector2.Distance(from, beetle);
        var toward = (beetle - from) / length;
        var offset = at - from;
        var along = Vector2.Dot(offset, toward);
        return along >= 0.1f && along <= length && MathF.Abs(toward.X * offset.Y - toward.Y * offset.X) <= within;
    }

    // The test rolls everything and pins it; the human reads the fight, its marks and the beetle as a player would, and the
    // bots read the same roll from the scenario. The rest of the party takes the other rows of the jobs at random.
    internal sealed record Roll(PartyRole Seat, OmegaRoll Omega, Walk Walk, int Seed)
    {
        public int FirstJob => Array.IndexOf(Omega.FirstOrder, Seat);

        public int SecondJob => Array.IndexOf(Omega.SecondOrder, Seat);

        public float Bearing => Omega.Beetle.RadiansFromNorth * 180f / MathF.PI;

        public Vector2 GatherSpotOf(PartyRole role)
            => Turned(TopP5OmegaLpduAi.GatherWithTheTetherTakersTowardTheBeetleWhenItIsNorth[Array.IndexOf(Omega.SecondOrder, role)]!.Value, Bearing);

        public Vector2 RimSpotOf(PartyRole taker)
            => Turned(TopP5OmegaLpduAi.SecondHelloWorldSpotsWhenTheBeetleIsNorth[Array.IndexOf(Omega.SecondOrder, taker)]!.Value, Bearing);

        public static Roll For(PartyRole seat, int person, int jobs, Walk walk, int reaction)
        {
            var s = (int)seat;
            var seed = 7919 * (64 * (3 * reaction + (int)walk) + 8 * s + person) + 3;
            var rng = new Random(seed);
            var others = new Queue<PartyRole>(PerRole.All.Where(r => r != seat).OrderBy(_ => rng.Next()));
            var byPerson = Enumerable.Range(0, 8).Select(i => i == person ? seat : others.Dequeue()).ToArray();
            var pairs = OmegaPairs.All[(8 * s + person + 64 * jobs + 24 * reaction) % OmegaPairs.All.Length];
            var monitors = (s + person + (int)walk) % 2 == 0 ? MonitorSide.Left : MonitorSide.Right;
            var beetle = Direction.Cardinal[(s / 2 + person + reaction) % 4];
            PartyRole[] tethers = [.. PerRole.All.OrderBy(_ => rng.Next()).Take(2)];
            return new Roll(seat, new OmegaRoll(pairs, monitors, beetle, jobs, byPerson, tethers), walk, seed);
        }

        private static readonly int[] JobsWithoutATether = [0, 1, 4, 5, 6, 7];

        // halfAndBeetle: the monitors' half (west first) and the beetle (N, E, S, W). A tether starts on the seat, whose
        // second Hello World job is never a tether's, and the other on another seat.
        public static Roll HoldingATether(PartyRole seat, int halfAndBeetle, int round, Walk walk, int reaction)
        {
            var s = (int)seat;
            var seed = 7919 * (64 * (3 * (4 * round + reaction) + (int)walk) + 8 * halfAndBeetle + s) + 5;
            var rng = new Random(seed);
            var jobs = (s + halfAndBeetle + reaction + round) % HelloWorldJobs.Length;
            var secondJob = JobsWithoutATether[(halfAndBeetle + s + (int)walk + round) % JobsWithoutATether.Length];
            var person = Array.FindIndex(HelloWorldJobs[jobs], job => job.Second == secondJob);
            var others = new Queue<PartyRole>(PerRole.All.Where(r => r != seat).OrderBy(_ => rng.Next()));
            var byPerson = Enumerable.Range(0, 8).Select(i => i == person ? seat : others.Dequeue()).ToArray();
            var pairs = OmegaPairs.All[rng.Next(OmegaPairs.All.Length)];
            var monitors = halfAndBeetle % 2 == 0 ? MonitorSide.Left : MonitorSide.Right;
            var beetle = Direction.Cardinal[halfAndBeetle / 2];
            var other = PerRole.All.Where(r => r != seat).OrderBy(_ => rng.Next()).First();
            return new Roll(seat, new OmegaRoll(pairs, monitors, beetle, jobs, byPerson, [seat, other]), walk, seed);
        }

        // A scripted roll under the first set of jobs: the seat holds `secondJob` in the second Hello World and the seats
        // after it take the other rows in turn; the tethers start on whoever holds `tetherJobs` in the second Hello World.
        public static Roll Scripted(PartyRole seat, int secondJob, MonitorSide monitors, Direction beetle, int[] tetherJobs, int n)
        {
            var person = Array.FindIndex(HelloWorldJobs[ScriptedJobs], job => job.Second == secondJob);
            PartyRole[] byPerson = [.. Enumerable.Range(0, 8).Select(i => PerRole.All[((int)seat + i - person + 8) % 8])];
            var omega = new OmegaRoll(OmegaPairs.All[7 * n % OmegaPairs.All.Length], monitors, beetle, ScriptedJobs, byPerson, []);
            return new Roll(seat, omega with { Tethers = [.. tetherJobs.Select(job => omega.SecondOrder[job])] }, Walk.Straight, 7919 * n + 7);
        }

        public override string ToString()
            => $"seed {Seed}, {Seat} ({Walk}, {HelloWorldSigns[FirstJob]} then {HelloWorldSigns[SecondJob]}, {Omega})";
    }

    // Each cue queues a walk that sets off a reaction time later and is aimed as it sets off.
    private sealed class Guide(Roll roll, float late, Gather gather)
    {
        public readonly List<string> Problems = [];
        public int Legs { get; private set; }
        public int Walks { get; private set; }
        public bool HeldAsTheFetchBegan { get; private set; }

        // Who held the tethers from each change on, and where they stood, for a failing run's report.
        public readonly List<string> TetherPasses = [];

        // Each time a taker took a tether from the human: when, which taker, and whether it was fetching rather than
        // walking in to its place 3 y toward the beetle, which it heads for exactly and stops on.
        public readonly List<(float At, PartyRole Taker, bool Fetching)> TakenFromTheHuman = [];

        private static readonly Dictionary<PartyRole, Vector2> Opening = new()
        {
            [MainTank] = new(-2.1f, -5.08f), [OffTank] = new(2.1f, -5.08f),
            [RegenHealer] = new(-0.7f, 5.7f), [ShieldHealer] = new(-0.7f, 6.5f), [MeleeDpsA] = new(-0.7f, 7.3f),
            [MeleeDpsB] = new(0.7f, 5.7f), [PhysRangedDps] = new(0.7f, 6.5f), [CasterDps] = new(0.7f, 7.3f),
        };

        private readonly Random rng = new(roll.Seed);
        private readonly Vector2 looseMiddle = LooseMiddle(new Random(roll.Seed + 1));
        private readonly Queue<(float Depart, Func<ScenarioProbe, Vector2> To)> legs = new();
        private readonly Vector2?[] stacks = new Vector2?[2];
        private readonly Dictionary<SimTether, PartyRole> heldLastFrame = new();
        private (float Depart, Vector2[] Points, float Speed)? walking;
        private string lastHolders = "";
        private string? myId;
        private int rowsRead, cannonCasts, helloWorldHits;
        private int? firstJob, secondJob;
        private bool? firstCannonsFireNorthAndSouth, monitorsWest;
        private bool started, setDown;

        public void Tick(ScenarioProbe p)
        {
            var me = p.Member(roll.Seat)!;
            if (!me.IsAlive()) return;
            myId ??= me.GameObjectId.ObjectId.ToString("X8");
            if (!started)
            {
                started = true;
                Queue(p.Time, _ => Opening[roll.Seat]);
            }
            ReadTheTimeline(p);
            if (p.Crossed(LeavesForItsSecondSpotAt(gather))) Queue(p.Time, SecondHelloWorldSpot);
            if (p.Crossed(FirstPairHits)) CheckTheStack(p, 0);
            if (p.Crossed(SecondPairHits)) CheckTheStack(p, 1);
            if (p.Crossed(TopP5OmegaLpduAi.FirstFetch)) HeldAsTheFetchBegan = BlasterTetherHolders(p).Contains(roll.Seat);
            if (p.Crossed(TethersChecked)) CheckTheTethersAreOnTheirTakers(p);
            NoteWhereTheTethersGo(p);
            NoteTakersTakingTheHumansTether(p);
            SetDownWhenItIsTime(p, me);
            Advance(p, me);
        }

        private void NoteWhereTheTethersGo(ScenarioProbe p)
        {
            var holders = BlasterTetherHolders(p);
            var who = string.Join("+", holders);
            if (who == lastHolders) return;
            lastHolders = who;
            TetherPasses.Add($"t={p.Time.ToString("F2", CultureInfo.InvariantCulture)} to "
                             + string.Join(" and ", holders.Select(h => $"{h} at {Flat(p.Member(h)!.Position)}")));
        }

        private void NoteTakersTakingTheHumansTether(ScenarioProbe p)
        {
            foreach (var tether in PassableTethers(p))
            {
                if (HolderOf(tether) is not { } holder) continue;
                if (heldLastFrame.TryGetValue(tether, out var before) && before == roll.Seat && Array.IndexOf(roll.Omega.SecondOrder, holder) is 2 or 3)
                    TakenFromTheHuman.Add((p.Time, holder, IsFetching(p, holder)));
                heldLastFrame[tether] = holder;
            }
        }

        private bool IsFetching(ScenarioProbe p, PartyRole taker)
        {
            var member = p.Member(taker)!;
            var place = roll.GatherSpotOf(taker);
            return member.MoveDestination is { } going
                ? Vector2.Distance(Flat(going), place) > 0.01f
                : Vector2.Distance(Flat(member.Position), place) > 0.5f;
        }

        private static Vector2 LooseMiddle(Random random)
        {
            var radius = LooseMiddleFrom + (LooseMiddleTo - LooseMiddleFrom) * (float)random.NextDouble();
            var (sin, cos) = MathF.SinCos(2f * MathF.PI * (float)random.NextDouble());
            return new Vector2(radius * sin, radius * cos);
        }

        private void SetDownWhenItIsTime(ScenarioProbe p, SimCharacter me)
        {
            if (setDown || SetDownOn(gather) is not { } spot) return;
            var due = gather == Gather.OffTheTakersLines
                ? p.Time >= GatherCall && roll.Omega.SecondOrder[2..4].All(taker => StandsOnItsPlace(p, taker))
                : p.Time >= SetDownByTheBeetleAt;
            if (!due) return;
            setDown = true;
            walking = null;
            var at = Turned(spot, BeetleBearing(p));
            me.SetPosition(new Placement(new Vector3(at.X, 0f, at.Y), me.Rotation));
        }

        private bool StandsOnItsPlace(ScenarioProbe p, PartyRole taker)
            => p.Member(taker) is { MoveDestination: null } member && Vector2.Distance(Flat(member.Position), roll.GatherSpotOf(taker)) <= 0.25f;

        private void CheckTheTethersAreOnTheirTakers(ScenarioProbe p)
        {
            var holders = BlasterTetherHolders(p);
            if (!holders.Order().SequenceEqual(roll.Omega.SecondOrder[2..4].Order()))
                Problems.Add($"the Blaster tethers were on {string.Join(", ", holders)} at t={p.Time:F2}");
            foreach (var taker in roll.Omega.SecondOrder[2..4])
            {
                if (p.Member(taker) is not { } member || !member.IsAlive()) continue;
                var (at, spot) = (Flat(member.Position), roll.RimSpotOf(taker));
                if (member.MoveDestination is { } going)
                    Problems.Add($"{taker} was still headed for {Flat(going)} from {at} at t={p.Time:F2}; its rim spot is {spot}");
                else if (Vector2.Distance(at, spot) > OnItsRimSpot)
                    Problems.Add($"{taker} stood at {at} at t={p.Time:F2}, {Vector2.Distance(at, spot):F2} y from its rim spot {spot}");
            }
        }

        private static List<PartyRole> BlasterTetherHolders(ScenarioProbe p)
            => [.. PassableTethers(p).Select(HolderOf).OfType<PartyRole>()];

        private void ReadTheTimeline(ScenarioProbe p)
        {
            var rows = p.Fake.Timeline.Rows;
            for (; rowsRead < rows.Count; rowsRead++)
            {
                var row = rows[rowsRead];
                var (kind, id) = (row["kind"], row["id"]);
                if (kind == "cast" && (id == Id(ActionId.OmegaDiffuseWaveCannonFront) || id == Id(ActionId.OmegaDiffuseWaveCannonSides)))
                    ReadTheFirstCannons(p, id == Id(ActionId.OmegaDiffuseWaveCannonFront));
                else if (kind == "cast" && id == Id(ActionId.OmegaDiffuseWaveCannonAOE) && cannonCasts++ == 0)
                    QueueTheStack(p.Time + CastSeconds(row), 1);
                else if (kind == "cast" && (id == Id(ActionId.OversampledWaveCannonLeft) || id == Id(ActionId.OversampledWaveCannonRight)))
                    ReadTheMonitors(p, id == Id(ActionId.OversampledWaveCannonLeft));
                else if (kind == "ac_target" && id == "0x1F6" && row["target"] == myId)
                    ReadMyMark(p, (Sign)Convert.ToUInt32(row["p"].Split(',')[0], 16));
                else if (kind == "ability" && IsHelloWorld(id) && ++helloWorldHits == 12)
                    Queue(p.Time, _ => Opening[roll.Seat]);
            }
        }

        private static string Id(uint actionId) => actionId.ToString(CultureInfo.InvariantCulture);

        private static bool IsHelloWorld(string id)
            => id == Id(ActionId.HelloNearWorld) || id == Id(ActionId.HelloNearWorldJump)
               || id == Id(ActionId.HelloDistantWorld) || id == Id(ActionId.HelloDistantWorldJump);

        private static float CastSeconds(Dictionary<string, string> row)
            => float.Parse(row["detail"].Split(' ').Single(d => d.StartsWith("cast=")).Replace("cast=", "").TrimEnd('s'), CultureInfo.InvariantCulture);

        private void ReadTheFirstCannons(ScenarioProbe p, bool northAndSouth)
        {
            firstCannonsFireNorthAndSouth = northAndSouth;
            if (northAndSouth != roll.Omega.Pairs.FirstCannonsFireNorthAndSouth)
                Problems.Add($"the human read the first cannons as {(northAndSouth ? "N/S" : "E/W")}");
            QueueTheStack(p.Time, 0);
        }

        private void QueueTheStack(float cue, int pair)
        {
            var read = roll.Omega.Pairs with { FirstCannonsFireNorthAndSouth = firstCannonsFireNorthAndSouth ?? false };
            var stack = read.Stack(pair);
            stacks[pair] = stack;
            Queue(cue, _ => stack);
        }

        // The bots stand within their jitter of the strat's stack; the human's own reading of the pair must put it there too.
        private void CheckTheStack(ScenarioProbe p, int pair)
        {
            var bots = PerRole.All.Where(r => r != roll.Seat && p.Member(r).IsAlive()).Select(r => Flat(p.Member(r)!.Position)).ToList();
            if (stacks[pair] is not { } mine || bots.Count == 0) return;
            var theirs = bots.Aggregate(Vector2.Zero, (sum, at) => sum + at) / bots.Count;
            if (Vector2.Distance(mine, theirs) > StackTolerance)
                Problems.Add($"the human read the {(pair == 0 ? "first" : "second")} Omega pair's stack as {mine}, the bots stacked at {theirs}");
        }

        private void ReadTheMonitors(ScenarioProbe p, bool west)
        {
            monitorsWest = west;
            if ((west ? MonitorSide.Left : MonitorSide.Right) != roll.Omega.Monitors)
                Problems.Add($"the human read the monitors as firing {(west ? "west" : "east")}");
            Queue(p.Time, FirstHelloWorldSpot);
        }

        private void ReadMyMark(ScenarioProbe p, Sign sign)
        {
            var job = Array.IndexOf(HelloWorldSigns, sign);
            if (p.Time < SecondHelloWorldFrom)
            {
                firstJob = job;
                if (job != roll.FirstJob) Problems.Add($"the human was marked {sign} for the first Hello World");
                return;
            }
            secondJob = job;
            if (job != roll.SecondJob) Problems.Add($"the human was marked {sign} for the second Hello World");
            Queue(p.Time, gather switch
            {
                Gather.Lpdu => LpduGatherSpot,
                Gather.LooseMiddle => _ => looseMiddle,
                _ => WhereItStands,
            });
        }

        private Vector2 WhereItStands(ScenarioProbe p) => Flat(p.Member(roll.Seat)!.Position);

        private Vector2 FirstHelloWorldSpot(ScenarioProbe p)
        {
            if (firstJob is not { } job || monitorsWest is not { } west) return Lost(p, "its first Hello World spot");
            return TopP5OmegaLpduAi.FirstHelloWorldSpotsWhenTheMonitorsFireWest[job]!.Value * new Vector2(west ? 1f : -1f, 1f);
        }

        private Vector2 LpduGatherSpot(ScenarioProbe p)
            => secondJob is { } job
                ? Turned(TopP5OmegaLpduAi.GatherWithTheTetherTakersTowardTheBeetleWhenItIsNorth[job]!.Value, BeetleBearing(p))
                : Lost(p, "its place in the middle");

        private Vector2 SecondHelloWorldSpot(ScenarioProbe p)
            => secondJob is { } job
                ? Turned(TopP5OmegaLpduAi.SecondHelloWorldSpotsWhenTheBeetleIsNorth[job]!.Value, BeetleBearing(p))
                : Lost(p, "its second Hello World spot");

        private float BeetleBearing(ScenarioProbe p)
        {
            var beetle = Flat(p.World.Children.OfType<SimEnemy>().Single(e => e.BNpcBaseId == BNpcBaseId.BeetleHelper).Position);
            var bearing = (MathF.Atan2(beetle.X, -beetle.Y) * 180f / MathF.PI + 360f) % 360f;
            if (MathF.Abs((bearing - roll.Bearing + 540f) % 360f - 180f) > 1f)
                Problems.Add($"the human saw the beetle at {beetle}");
            return MathF.Round(bearing / 90f) * 90f;
        }

        private Vector2 Lost(ScenarioProbe p, string what)
        {
            Problems.Add($"the human didn't know {what} at t={p.Time:F2}");
            return Flat(p.Member(roll.Seat)!.Position);
        }

        private void Queue(float cue, Func<ScenarioProbe, Vector2> to)
        {
            legs.Enqueue((cue + late, to));
            Legs++;
        }

        private void Advance(ScenarioProbe p, SimCharacter me)
        {
            if (legs.TryPeek(out var next) && p.Time >= next.Depart)
            {
                legs.Dequeue();
                Walks++;
                var from = Flat(me.Position);
                walking = (p.Time, [from, .. TopP6HumanWalk.Points(roll.Walk, from, next.To(p), rng)], TopP6HumanWalk.Speed(roll.Walk));
            }
            if (walking is not { } w) return;
            var (at, heading, arrived) = TopP6HumanWalk.Along(w.Points, (p.Time - w.Depart) * w.Speed);
            me.SetPosition(new Placement(new Vector3(at.X, 0f, at.Y), MathF.Atan2(heading.X, heading.Y)));
            if (arrived) walking = null;
        }
    }

    // The fetcher holding the human's tether while the human stands within 0.8 y of the line from the fetcher's rim spot
    // to the beetle: for how long, and whether it headed for its rim spot meanwhile; and that it holds the human's tether
    // at Blaster.
    private sealed class FetcherWaiting(Roll roll, PartyRole fetcher)
    {
        public readonly List<string> Problems = [];
        public float HeldWhileTheHumanStoodInTheLine { get; private set; }
        private SimTether? humans;
        private float? heldFrom;
        private bool over;

        public void Tick(ScenarioProbe p)
        {
            humans ??= PassableTethers(p).FirstOrDefault(t => HolderOf(t) == roll.Seat);
            if (humans is null) return;
            if (p.Crossed(TethersChecked))
            {
                if (heldFrom is null) Problems.Add($"{fetcher} never held the human's tether while the human stood in its line");
                else if (HeldWhileTheHumanStoodInTheLine < 1f)
                    Problems.Add($"{fetcher} held the human's tether while the human stood in its line for only {HeldWhileTheHumanStoodInTheLine:F2} s");
                if (HolderOf(humans) != fetcher) Problems.Add($"the human's tether was on {HolderOf(humans)} at t={p.Time:F2}, not {fetcher}");
            }
            if (over) return;
            var spot = roll.RimSpotOf(fetcher);
            var inTheLine = InTheLine(spot, Flat(humans.B!.Position), Flat(p.Member(roll.Seat)!.Position), NearlyInTheLine);
            if (heldFrom is null && HolderOf(humans) == fetcher && inTheLine) heldFrom = p.Time;
            if (heldFrom is not { } from) return;
            HeldWhileTheHumanStoodInTheLine = p.Time - from;
            if (!inTheLine)
            {
                over = true;
                return;
            }
            if (p.Member(fetcher)!.MoveDestination is { } going && Vector2.Distance(Flat(going), spot) <= 0.5f)
            {
                Problems.Add($"{fetcher} headed for its rim spot at t={p.Time:F2} with the human still in its line at {Flat(p.Member(roll.Seat)!.Position)}");
                over = true;
            }
        }
    }

    // Bind 1 from the gather on: until it loses its tether after 53 it heads only for its place in the middle or its rim
    // spot; then the human must hold that tether and Bind 1 head somewhere else, to fetch it.
    private sealed class SentBackForItsTether(Roll roll)
    {
        public readonly List<string> Problems = [];
        public float? LostAt { get; private set; }
        public float? SentAt { get; private set; }
        private readonly PartyRole taker = roll.Omega.SecondOrder[Bind1];
        private bool sentEarly;

        public void Tick(ScenarioProbe p)
        {
            if (p.Time < GatherCall) return;
            if (p.Crossed(TethersChecked))
            {
                if (LostAt is null) Problems.Add($"{taker} never lost its tether after {SecondHelloWorldCall}");
                else if (SentAt is null) Problems.Add($"{taker} lost its tether at t={LostAt:F2} and was never sent back for it");
            }
            if (SentAt is not null || p.Member(taker) is not { } member || !member.IsAlive()) return;
            var holds = PassableTethers(p).Any(t => HolderOf(t) == taker);
            var heading = member.MoveDestination is { } going ? Flat(going) : (Vector2?)null;
            var rim = roll.RimSpotOf(taker);
            if (LostAt is null)
            {
                if (p.Time >= SecondHelloWorldCall && !holds)
                {
                    LostAt = p.Time;
                    if (!PassableTethers(p).Any(t => HolderOf(t) == roll.Seat))
                        Problems.Add($"{taker} lost its tether at t={p.Time:F2}, not to the human: the tethers were on {string.Join(", ", PassableTethers(p).Select(HolderOf))}");
                    return;
                }
                if (!sentEarly && heading is { } to && Vector2.Distance(to, roll.GatherSpotOf(taker)) > 0.01f && Vector2.Distance(to, rim) > 0.5f)
                {
                    sentEarly = true;
                    Problems.Add($"{taker} headed for {to} at t={p.Time:F2}, before it lost its tether");
                }
                return;
            }
            if (heading is { } fetching && Vector2.Distance(fetching, rim) > 0.5f) SentAt = p.Time;
        }
    }

    // How close the human came to each hit it dodged, and the bots to each hit centred on the human or on another bot's
    // Blaster, as each decided, over every run of a case: the scenario's own areas, read off AoeQuery. Each Hello World
    // jump's pick is held to the same slack, and the human's distance from the arena's edge is read every frame.
    private sealed class Clearances
    {
        private readonly Dictionary<string, (float Clearance, string Where)> tightest = new();
        private readonly HashSet<(float, uint, Placement)> seen = [];
        private readonly Dictionary<(bool Second, bool Near), List<PartyRole>> chains = new();
        private readonly List<PartyRole> monitored = [], blasted = [];
        private ScenarioProbe? probe;
        private Roll? playing;

        public void Watch(ScenarioProbe p)
        {
            probe = p;
            if (playing is { } roll && p.Member(roll.Seat) is { } me && me.IsAlive())
                Note(TheArenasEdge, Geometry.ArenaRadius - Flat(me.Position).Length());
        }

        public Action<AoeQuery> For(Roll roll)
        {
            seen.Clear();
            chains.Clear();
            monitored.Clear();
            blasted.Clear();
            probe = null;
            playing = roll;
            return hit =>
            {
                if (probe is not { } p || p.Member(roll.Seat) is not { } me || !me.IsAlive()) return;
                if (!seen.Add((p.World.Events.Elapsed, hit.ActionId, hit.Source))) return;
                var at = Flat(me.Position);
                var source = Flat(hit.Source.Position);
                var mine = Vector2.Distance(source, at) < 0.01f;
                var clearance = Clearance(hit, at);
                switch (hit.ActionId)
                {
                    case ActionId.OptimizedBlizzardIII:
                        Note("Optimized Blizzard III", clearance);
                        break;
                    case ActionId.EfficientBladework:
                        Note("Efficient Bladework", clearance);
                        break;
                    case ActionId.BeyondStrength:
                        Note("inside Beyond Strength's safe circle", clearance);
                        break;
                    case ActionId.SuperliminalSteelOmenL or ActionId.SuperliminalSteelOmenR:
                        Note("Superliminal Steel", clearance);
                        break;
                    case ActionId.OmegaDiffuseWaveCannonAOE:
                        Note("the Diffuse Wave Cannons", clearance);
                        break;
                    case ActionId.OversampledWaveCannonAoe:
                        monitored.Add(TargetOf(p, source));
                        if (mine) NoteTheBots(p, hit, "every bot outside the human's monitor");
                        else Note("another's monitor", clearance);
                        break;
                    case ActionId.HelloNearWorld or ActionId.HelloNearWorldJump or ActionId.HelloDistantWorld or ActionId.HelloDistantWorldJump:
                        Chain(p, hit.ActionId is ActionId.HelloNearWorld or ActionId.HelloNearWorldJump, source);
                        if (mine) NoteTheBots(p, hit, "every bot outside the human's Hello World");
                        else Note("another's Hello World", clearance);
                        break;
                    case ActionId.BlasterAoe:
                        var blasterTarget = TargetOf(p, source);
                        blasted.Add(blasterTarget);
                        if (mine) NoteTheBots(p, hit, "every bot outside the human's Blaster");
                        else
                        {
                            Note("another's Blaster", clearance);
                            NoteTheBots(p, hit, BotsOutsideABlaster, blasterTarget);
                        }
                        break;
                }
            };
        }

        private void Chain(ScenarioProbe p, bool near, Vector2 source)
        {
            var second = p.Time > SecondHelloWorldFrom;
            if (!chains.TryGetValue((second, near), out var chain)) chains[(second, near)] = chain = [];
            if (chain.Count > 0)
            {
                var order = second ? playing!.Omega.SecondOrder : playing!.Omega.FirstOrder;
                var last = chain[^1];
                var candidates = PerRole.All.Where(r => r != last && p.Member(r).IsAlive()).Select(r => (r, Flat(p.Member(r)!.Position)));
                Note("each Hello World jump's pick, won by", JumpPick(near, Flat(p.Member(last)!.Position), candidates, JumpTargets(near, order, chain)).Margin);
            }
            chain.Add(TargetOf(p, source));
        }

        private void NoteTheBots(ScenarioProbe p, AoeQuery hit, string name, PartyRole? except = null)
        {
            foreach (var bot in PerRole.All.Where(r => r != playing!.Seat && r != except && p.Member(r).IsAlive()))
                Note(name, Clearance(hit, Flat(p.Member(bot)!.Position)));
        }

        private static PartyRole TargetOf(ScenarioProbe p, Vector2 source)
            => PerRole.All.Where(r => p.Member(r).IsAlive()).MinBy(r => Vector2.Distance(Flat(p.Member(r)!.Position), source));

        private static float Clearance(AoeQuery hit, Vector2 at)
            => hit.SignedDistance(new Vector3(at.X, 0f, at.Y)) ?? throw new InvalidOperationException($"no area for action {hit.ActionId}");

        // Each Hello World goes Near holder, Attacks 2 and 3 either way round, and Distant holder, Attack 1, Attack 4; the
        // monitors hit their two holders and the Blasters their two takers.
        public IEnumerable<string> Finish()
        {
            var omega = playing!.Omega;
            foreach (var (second, order) in new[] { (false, omega.FirstOrder), (true, omega.SecondOrder) })
            {
                var name = second ? "second" : "first";
                var near = chains.GetValueOrDefault((second, true), []);
                var distant = chains.GetValueOrDefault((second, false), []);
                if (near.Count != 3 || near[0] != order[0] || !near.Skip(1).Order().SequenceEqual(new[] { order[5], order[6] }.Order()))
                    yield return $"the {name} Hello Near World went {string.Join(", ", near)}";
                if (!distant.SequenceEqual([order[1], order[4], order[7]]))
                    yield return $"the {name} Hello Distant World went {string.Join(", ", distant)}";
            }
            if (!monitored.Order().SequenceEqual(omega.FirstOrder[2..4].Order()))
                yield return $"the monitors hit {string.Join(", ", monitored)}";
            if (!blasted.Order().SequenceEqual(omega.SecondOrder[2..4].Order()))
                yield return $"the Blasters hit {string.Join(", ", blasted)}";
        }

        private void Note(string name, float clearance)
        {
            if (tightest.TryGetValue(name, out var before) && before.Clearance <= clearance) return;
            tightest[name] = (clearance, $"t={probe!.Time.ToString("F2", CultureInfo.InvariantCulture)} {playing}");
        }

        public (float Clearance, string Where) Of(string name) => tightest.GetValueOrDefault(name, (float.NaN, "never measured"));

        // `notHeld`: a clearance a case stands inside the slack by design.
        public IEnumerable<string> Within(float slack, string? notHeld = null)
            => tightest.Where(t => t.Key != notHeld && !(t.Value.Clearance > slack))
                       .Select(t => $"{t.Key} {t.Value.Clearance.ToString("F2", CultureInfo.InvariantCulture)} ({t.Value.Where})");

        public override string ToString()
            => string.Join(Environment.NewLine, tightest.OrderBy(t => t.Value.Clearance)
                                                        .Select(t => $"  {t.Key} {t.Value.Clearance.ToString("F2", CultureInfo.InvariantCulture)} ({t.Value.Where})"));
    }
}
