using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Multiplayer;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P6WaveCannon2;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Tests.TopP6WaveCannon2LpduAiTests;

namespace AnoMech.Tests;

// The sunny-day soak drives the player's seat with the bots' own AI. Here the seat is a human who plays LPDU from what a
// player can see: Omega's Cosmo Arrow cast, the first telegraph (a cross is in first, four bands out first), the first
// set going off, each rolling wave and the laser sets. At each cue it walks to its LPDU spot a fixed reaction time late,
// straight, curving or sprinting, and never steps round anyone:
//   Cosmo Arrow's cast: home;   the first set: in (in first) or out to the corner (out first);
//   in first:  wave 2 back out past home, wave 4 the side step, wave 5 the laser spot, wave 6 the stack;
//   out first: wave 2 back home, wave 3 the side step, wave 4 the laser spot, laser set 2 the stack.
// The waves count from the first rolling lines (11.91), every 2 s.
// Whatever its seat, pattern, laser split, Wild Charge target and reaction time, nobody may die, and the human keeps the
// slack the margin tests ask of the bots from every hit it dodges: surviving a line by a few hundredths is a death in
// the game.
public class TopP6WaveCannon2LpduHumanTests
{
    // Three fixed rolls per seat, one per walking style; HumanSeeds asks for more, as SunnySeeds does for the soak.
    private static IEnumerable<int> Seeds => Enumerable.Range(1, TestContext.Parameters.Get("HumanSeeds", 3));
    private static readonly float[] ReactionTimes = [0f, 0.3f, 0.6f];
    private const float JitterSlack = 0.5f;

    public enum Walk { Straight, Curving, Sprinting }

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
        => from inFirst in new[] { true, false }
           from delay in ReactionTimes
           select new TestCaseData(inFirst, delay).SetName($"{{m}}({(inFirst ? "in" : "out")} first, {delay:F1}s late)");

    [TestCaseSource(nameof(Matrix))]
    public void AHumanPlayingLpduIsNeverCaught(bool inFirst, float delay)
    {
        var tightest = new Clearances();
        var runs = 0;
        var problems = new List<string>();
        foreach (var seat in PerRole.All)
        foreach (var seed in Seeds)
        {
            runs++;
            problems.AddRange(Play(Roll.For(seat, inFirst, seed), delay, tightest));
        }

        TestContext.Out.WriteLine($"{TestContext.CurrentContext.Test.Name}: {runs} runs; the human's tightest clearances:{Environment.NewLine}{tightest}");
        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(30)));
            Assert.That(tightest.Within(JitterSlack), Is.Empty, "the human's clearances");
        });
    }

    private static List<string> Play(Roll roll, float delay, Clearances tightest)
    {
        var guide = new Guide(roll, delay);
        var problems = new List<string>();
        var measure = tightest.For(roll);
        ScenarioRun run;
        AoeQuery.Evaluated += measure;
        try
        {
            run = ScenarioRun.Execute(typeof(TopP6WaveCannon2Scenario), LpduStrat, roll.Seed, new ScenarioRunOptions
            {
                PlayerRole = roll.Seat,
                Overrides = o => roll.Pin((TopP6WaveCannon2StateOverrides)o),
                Takeovers = [new Takeover(0f, null)],
                Probe = p =>
                {
                    guide.Tick(p);
                    tightest.Watch(p);
                },
            });
        }
        finally
        {
            AoeQuery.Evaluated -= measure;
        }

        if (!run.Passed) problems.Add(run.ToString());
        problems.AddRange(tightest.Problems);
        if (guide.InFirstRead != roll.InFirst)
            problems.Add($"the human read the first telegraph as {Pattern(guide.InFirstRead)}");
        if (run.Passed && guide.Walks < guide.Legs)
            problems.Add($"the human set off on {guide.Walks} of its {guide.Legs} walks");
        if (run.Passed && guide.Legs != 6)
            problems.Add($"the human saw {guide.Legs} of its 6 cues");
        return problems.Select(problem => $"{roll} ({delay:F1}s late): {problem}").ToList();
    }

    private static string Pattern(bool? inFirst) => inFirst switch { true => "in first", false => "out first", null => "unread" };

    // The test rolls the lasers and the Wild Charge and pins them; the human reads only what the fight shows.
    internal sealed record Roll(PartyRole Seat, bool InFirst, int Seed, Walk Walk, PartyRole[] ProteanOrder, PartyRole WildChargeTarget)
    {
        public static Roll For(PartyRole seat, bool inFirst, int seed)
        {
            var rng = new Random(seed * 1009 + (int)seat * 31 + (inFirst ? 7 : 0));
            PartyRole[] order = [.. PerRole.All.OrderBy(_ => rng.Next())];
            var target = PerRole.All[rng.Next(8)];
            return new Roll(seat, inFirst, seed * 7919 + (int)seat * 2 + (inFirst ? 1 : 0), Enum.GetValues<Walk>()[(seed + (int)seat) % 3],
                            order, target);
        }

        public void Pin(TopP6WaveCannon2StateOverrides o)
        {
            o.InFirst = InFirst;
            o.ProteanOrder = ProteanOrder;
            o.WildChargeTarget = WildChargeTarget;
        }

        public override string ToString()
            => $"seed {Seed}, {Seat} ({Pattern(InFirst)}, {Walk}, set 1 {string.Join("+", ProteanOrder.Take(4))}, Wild Charge on {WildChargeTarget})";
    }

    // Each cue queues a walk that sets off a reaction time later, aimed when it sets off; a walk that sets off while
    // another is under way starts from where the human stands.
    private sealed class Guide(Roll roll, float late)
    {
        private const float RunSpeed = 6.5f;
        private const float LeastCurve = 1f, MostCurve = 2f;

        public bool? InFirstRead { get; private set; }
        public int Legs { get; private set; }
        public int Walks { get; private set; }

        private readonly Random rng = new(roll.Seed);
        private readonly Queue<(float Depart, Stage To)> legs = new();
        private (float Depart, Vector2[] Points, float Speed)? walking;
        private int rowsRead, firstTelegraphOmens, firstSets, waves, laserSets;
        private string? firstTelegraphAt;
        private float lastFirstSet = float.NegativeInfinity, lastWave = float.NegativeInfinity, lastLaserSet = float.NegativeInfinity;

        public void Tick(ScenarioProbe p)
        {
            var me = p.Member(roll.Seat)!;
            if (!me.IsAlive()) return;
            ReadTheTimeline(p);
            Advance(p, me);
        }

        private void ReadTheTimeline(ScenarioProbe p)
        {
            var rows = p.Fake.Timeline.Rows;
            for (; rowsRead < rows.Count; rowsRead++)
            {
                var row = rows[rowsRead];
                var id = row["id"];
                if (row["kind"] == "cast" && id == $"{ActionId.CosmoArrow}")
                    Queue(p.Time, Stage.Home);
                else if (row["kind"] == "cast" && id == $"{ActionId.CosmoArrowOmen}" && (firstTelegraphAt ??= row["t"]) == row["t"])
                    firstTelegraphOmens++;
                if (row["kind"] != "ability") continue;
                if (id == $"{ActionId.CosmoArrowOmen}" && FirstOfARound(ref lastFirstSet, p.Time) && firstSets++ == 0)
                    OnTheFirstSet(p.Time);
                else if (id == $"{ActionId.CosmoArrowDamage}" && FirstOfARound(ref lastWave, p.Time))
                    OnTheWave(p.Time, ++waves);
                else if (id == $"{ActionId.WaveCannonProtean}" && FirstOfARound(ref lastLaserSet, p.Time))
                    OnTheLaserSet(p.Time, ++laserSets);
            }
        }

        private static bool FirstOfARound(ref float last, float now)
        {
            var first = now - last > 1f;
            last = now;
            return first;
        }

        // The cross through the centre is two lines, the four bands four.
        private void OnTheFirstSet(float now)
        {
            InFirstRead = firstTelegraphOmens == 2;
            Queue(now, InFirstRead == true ? Stage.CentreSquare : Stage.Corner);
        }

        private void OnTheWave(float now, int wave)
        {
            var next = (InFirstRead, wave) switch
            {
                (true, 2) => Stage.OutPastHome,
                (true, 4) => Stage.Side,
                (true, 5) => Stage.Laser,
                (true, 6) => Stage.Stack,
                (false, 2) => Stage.BackHome,
                (false, 3) => Stage.Side,
                (false, 4) => Stage.Laser,
                _ => (Stage?)null,
            };
            if (next is { } stage) Queue(now, stage);
        }

        private void OnTheLaserSet(float now, int set)
        {
            if (InFirstRead == false && set == 2) Queue(now, Stage.Stack);
        }

        private void Queue(float cue, Stage to)
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
                walking = (p.Time, [from, .. Curve(from, Spot(roll.Seat, next.To))], roll.Walk == Walk.Sprinting ? AiManager.SprintSpeed : RunSpeed);
            }
            if (walking is not { } w) return;
            var (at, heading, arrived) = Along(w.Points, (p.Time - w.Depart) * w.Speed);
            me.SetPosition(new Placement(new Vector3(at.X, 0f, at.Y), MathF.Atan2(heading.X, heading.Y)));
            if (arrived) walking = null;
        }

        // A curving walk bulges one to two yalms off the straight line, a quarter of its length at most.
        private Vector2[] Curve(Vector2 from, Vector2 to)
        {
            var length = Vector2.Distance(from, to);
            if (roll.Walk != Walk.Curving || length <= 1f) return [to];
            var bulge = Math.Min(LeastCurve + (MostCurve - LeastCurve) * (float)rng.NextDouble(), length / 4f);
            var normal = new Vector2(-(to - from).Y, (to - from).X) / length * (rng.Next(2) == 0 ? -1f : 1f);
            return [.. Enumerable.Range(1, 16).Select(i => i / 16f).Select(u => from + (to - from) * u + normal * (bulge * 4f * u * (1f - u)))];
        }

        private static (Vector2 At, Vector2 Heading, bool Arrived) Along(Vector2[] points, float walked)
        {
            var heading = new Vector2(0f, 1f);
            for (var i = 1; i < points.Length; i++)
            {
                var leg = Vector2.Distance(points[i - 1], points[i]);
                if (leg > 1e-4f) heading = (points[i] - points[i - 1]) / leg;
                if (walked <= leg) return (leg < 1e-4f ? points[i] : Vector2.Lerp(points[i - 1], points[i], walked / leg), heading, false);
                walked -= leg;
            }
            return (points[^1], heading, true);
        }
    }

    // How close the human came to each hit it dodged (or how deep it stood in the one it had to take), as each resolved,
    // over every run of a case: the scenario's own areas, read off AoeQuery.
    private sealed class Clearances
    {
        public readonly List<string> Problems = [];

        private readonly Dictionary<string, (float Clearance, string Where)> tightest = new();
        private ScenarioProbe? probe;
        private Roll? playing;
        private readonly HashSet<(float, uint, Placement)> seen = [];

        public void Watch(ScenarioProbe p) => probe = p;

        public Action<AoeQuery> For(Roll roll)
        {
            var seat = roll.Seat;
            seen.Clear();
            Problems.Clear();
            probe = null;
            playing = roll;
            return hit =>
            {
                if (probe is not { } p || p.Member(seat) is not { } me || !me.IsAlive()) return;
                if (!seen.Add((p.World.Events.Elapsed, hit.ActionId, hit.Source))) return;
                var at = Flat(me.Position);
                switch (hit.ActionId)
                {
                    case ActionId.CosmoArrowOmen or ActionId.CosmoArrowDamage:
                        Note("the exasquares", Clearance(hit, at));
                        break;
                    case ActionId.WaveCannonProtean when TargetOf(p, hit) == seat:
                        foreach (var bot in PerRole.All.Where(r => r != seat && p.Member(r).IsAlive()))
                            Note("every bot outside the human's laser", Clearance(hit, Flat(p.Member(bot)!.Position)));
                        break;
                    case ActionId.WaveCannonProtean:
                        Note("the others' lasers", Clearance(hit, at));
                        break;
                    case ActionId.WaveCannonWildCharge:
                        Note("inside the Wild Charge", -Clearance(hit, at));
                        MeasureTheLine(p, hit);
                        break;
                }
            };
        }

        // The front of the line is the end nearest Omega: the two tanks, then the rest.
        private void MeasureTheLine(ScenarioProbe p, AoeQuery hit)
        {
            var direction = new Vector2(MathF.Sin(hit.Source.Rotation), MathF.Cos(hit.Source.Rotation));
            var along = PerRole.All.Where(r => p.Member(r).IsAlive())
                                   .Select(r => (Role: r, Along: Vector2.Dot(Flat(p.Member(r)!.Position) - Flat(hit.Source.Position), direction)))
                                   .OrderBy(m => m.Along).ToList();
            if (along.Count < 8 || !along.Take(2).All(m => m.Role.IsTank()))
            {
                Problems.Add($"the Wild Charge line runs {string.Join(", ", along.Select(m => m.Role))}");
                return;
            }
            Note("the tanks ahead of the rest of the Wild Charge line", along[2].Along - along[1].Along);
        }

        private static PartyRole TargetOf(ScenarioProbe p, AoeQuery hit)
        {
            var direction = new Vector2(MathF.Sin(hit.Source.Rotation), MathF.Cos(hit.Source.Rotation));
            return PerRole.All.Where(r => p.Member(r).IsAlive())
                              .Select(r => (Role: r, Offset: Flat(p.Member(r)!.Position) - Flat(hit.Source.Position)))
                              .Where(m => Vector2.Dot(m.Offset, direction) > 0f)
                              .MinBy(m => MathF.Abs(m.Offset.X * direction.Y - m.Offset.Y * direction.X)).Role;
        }

        private static float Clearance(AoeQuery hit, Vector2 at)
            => hit.SignedDistance(new Vector3(at.X, 0f, at.Y)) ?? throw new InvalidOperationException($"no area for action {hit.ActionId}");

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
