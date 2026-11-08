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
// both Blaster tethers are on their takers just before Blaster, and the human keeps more than 0.5 y from every hit it
// dodges.
//
// A Blaster tether can also start on a human who has none to take. Its line toward the beetle passes the takers waiting
// 3 y toward it only if the human stands within about 0.6 y of the beetle's axis behind them, so the takers fetch it.
// Those cases give the human a tether and a second Hello World job other than the tethers, and at its second mark send
// it loosely into the middle instead: to a random point 0.5-3 y from it, in any direction. A human who stays on its
// first Hello World spot until 53, or walks straight on to its second, isn't covered: its own walk still fails about one
// roll in 30 and one in 300 (it reaches the second Hello World late, crosses a Blaster or a taker's line, or a curving
// walk along the rim leaves the arena).
public class TopP5OmegaLpduHumanTests
{
    // Staff and Sword against Staff and Shield puts the two stacks 17 y and 10 y out on cardinals a quarter apart, 19.7 y
    // of walking, and the second pair goes off 3.9 s after the first set's telegraphs vanish. A walker who sets off more
    // than about 0.9 s late is still on its way as it goes off and from about 1.1 s late can be caught, so the walkers
    // here react up to 0.9 s late and only the sprinter 1.2 s.
    private static readonly float[] ReactionTimes = [0f, 0.6f, 0.9f, 1.2f];
    private const float LatestWalker = 0.9f;
    private const float JitterSlack = 0.5f;

    private const float SecondHelloWorldFrom = 45f, SecondHelloWorldCall = 53f;
    private const float FirstPairHits = 24.15f, SecondPairHits = 28.15f, StackTolerance = 0.75f;
    private const float TethersChecked = 57.5f;
    private const float LooseMiddleFrom = 0.5f, LooseMiddleTo = 3f;

    // Where the human goes at its second mark: the strat's own spot, or loosely into the middle.
    private enum Gather { Lpdu, LooseMiddle }

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
            Assert.That(tightest.Within(JitterSlack), Is.Empty, "the human's clearances");
        });
    }

    // Every seat holds a tether under both monitor halves and all four beetles, and each seat every second Hello World
    // job but the tethers. The other tether starts on a seat the roll picks, a taker's or not. The human still holds its
    // tether as the takers start fetching in about two runs in three; the rest it passed on as it waited or walked in.
    [TestCaseSource(nameof(Matrix))]
    public void TheTakersFetchABlasterTetherFromAHumanGatheringLoosely(Walk walk, float delay)
    {
        var reaction = Array.IndexOf(ReactionTimes, delay);
        var tightest = new Clearances();
        var problems = new List<string>();
        var (runs, held) = (0, 0);
        for (var round = 0; round < TetherRounds; round++)
        foreach (var seat in PerRole.All)
        for (var halfAndBeetle = 0; halfAndBeetle < 8; halfAndBeetle++, runs++)
        {
            problems.AddRange(Play(Roll.HoldingATether(seat, halfAndBeetle, round, walk, reaction), delay, tightest, Gather.LooseMiddle, out var heldIt));
            if (heldIt) held++;
        }

        TestContext.Out.WriteLine($"{TestContext.CurrentContext.Test.Name}: {runs} runs, the human still holding its tether as the takers start "
                                  + $"fetching in {held}; its tightest clearances:{Environment.NewLine}{tightest}");
        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(30)));
            Assert.That(tightest.Within(JitterSlack), Is.Empty, "the human's clearances");
            Assert.That(held, Is.GreaterThan(runs / 3), "runs where the human still held its tether as the takers started fetching");
        });
    }

    private static List<string> Play(Roll roll, float delay, Clearances tightest, Gather gather, out bool heldAsTheFetchBegan)
    {
        var guide = new Guide(roll, delay, gather);
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
                    guide.Tick(p);
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
        heldAsTheFetchBegan = guide.HeldAsTheFetchBegan;
        if (problems.Count > 0) problems.Add("the Blaster tethers went " + string.Join(", ", guide.TetherPasses));
        return problems.Select(problem => $"{roll} ({delay:F1}s late, {gather}): {problem}").ToList();
    }

    // The test rolls everything and pins it; the human reads the fight, its marks and the beetle as a player would, and the
    // bots read the same roll from the scenario. The rest of the party takes the other rows of the jobs at random.
    internal sealed record Roll(PartyRole Seat, OmegaRoll Omega, Walk Walk, int Seed)
    {
        public int FirstJob => Array.IndexOf(Omega.FirstOrder, Seat);

        public int SecondJob => Array.IndexOf(Omega.SecondOrder, Seat);

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
        private (float Depart, Vector2[] Points, float Speed)? walking;
        private string lastHolders = "";
        private string? myId;
        private int rowsRead, cannonCasts, helloWorldHits;
        private int? firstJob, secondJob;
        private bool? firstCannonsFireNorthAndSouth, monitorsWest;
        private bool started;

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
            if (p.Crossed(SecondHelloWorldCall)) Queue(p.Time, SecondHelloWorldSpot);
            if (p.Crossed(FirstPairHits)) CheckTheStack(p, 0);
            if (p.Crossed(SecondPairHits)) CheckTheStack(p, 1);
            if (p.Crossed(TopP5OmegaLpduAi.FirstFetch)) HeldAsTheFetchBegan = BlasterTetherHolders(p).Contains(roll.Seat);
            if (p.Crossed(TethersChecked)) CheckTheTethersAreOnTheirTakers(p);
            NoteWhereTheTethersGo(p);
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

        private static Vector2 LooseMiddle(Random random)
        {
            var radius = LooseMiddleFrom + (LooseMiddleTo - LooseMiddleFrom) * (float)random.NextDouble();
            var (sin, cos) = MathF.SinCos(2f * MathF.PI * (float)random.NextDouble());
            return new Vector2(radius * sin, radius * cos);
        }

        private void CheckTheTethersAreOnTheirTakers(ScenarioProbe p)
        {
            var holders = BlasterTetherHolders(p);
            if (!holders.Order().SequenceEqual(roll.Omega.SecondOrder[2..4].Order()))
                Problems.Add($"the Blaster tethers were on {string.Join(", ", holders)} at t={p.Time:F2}");
        }

        private static List<PartyRole> BlasterTetherHolders(ScenarioProbe p)
            => p.World.Children.OfType<SimTether>()
                .Where(t => t.IsActive && t.TetherId == TetherId.PassableTether)
                .Select(t => t.A)
                .OfType<ISimPartyMember>()
                .Select(holder => holder.Role)
                .ToList();

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
            Queue(p.Time, gather == Gather.LooseMiddle ? _ => looseMiddle : LpduGatherSpot);
        }

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
            if (MathF.Abs((bearing - roll.Omega.Beetle.RadiansFromNorth * 180f / MathF.PI + 540f) % 360f - 180f) > 1f)
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

    // How close the human came to each hit it dodged, and the bots to each hit centred on the human, as each decided, over
    // every run of a case: the scenario's own areas, read off AoeQuery. Each Hello World jump's pick is held to the same
    // slack, and the human's distance from the arena's edge is read every frame.
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
                Note("the arena's edge", Geometry.ArenaRadius - Flat(me.Position).Length());
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
                        blasted.Add(TargetOf(p, source));
                        if (mine) NoteTheBots(p, hit, "every bot outside the human's Blaster");
                        else Note("another's Blaster", clearance);
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

        private void NoteTheBots(ScenarioProbe p, AoeQuery hit, string name)
        {
            foreach (var bot in PerRole.All.Where(r => r != playing!.Seat && p.Member(r).IsAlive()))
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

        public IEnumerable<string> Within(float slack)
            => tightest.Where(t => !(t.Value.Clearance > slack))
                       .Select(t => $"{t.Key} {t.Value.Clearance.ToString("F2", CultureInfo.InvariantCulture)} ({t.Value.Where})");

        public override string ToString()
            => string.Join(Environment.NewLine, tightest.OrderBy(t => t.Value.Clearance)
                                                        .Select(t => $"  {t.Key} {t.Value.Clearance.ToString("F2", CultureInfo.InvariantCulture)} ({t.Value.Where})"));
    }
}
