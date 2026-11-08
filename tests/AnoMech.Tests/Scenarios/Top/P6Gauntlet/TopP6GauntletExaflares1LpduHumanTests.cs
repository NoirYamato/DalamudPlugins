using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Multiplayer;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top;
using AnoMech.Scenarios.Top.P6Gauntlet;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Tests.TopP6GauntletExaflares1LpduAiTests;
using static AnoMech.Tests.TopP6HumanWalk;

namespace AnoMech.Tests;

// The sunny-day soak drives the player's seat with the bots' own AI. Here the seat is a human who plays LPDU from what a
// player can see: Alpha Omega's cast bars, the exaflares' arrows, the puddles as they drop and go off, the exaflares'
// explosions, the lasers and the Wild Charge. It reads the first exaflare's point and the way the others turn from the
// first two arrows, and at each cue walks to its next LPDU spot a fixed reaction time late, straight, curving (never
// toward a puddle's circle it can see) or sprinting, never stepping round anyone:
//   Unlimited Wave Cannon's cast: out of the hitbox (the MT north, the rest south; the OT is out); the second arrow:
//   the middle;
//   a second after the first puddles drop: the start, beside the first exaflare away from the second;
//   the first exaflare's explosion 8 y out: on to its point; the fourth and the fifth puddles: on to the next point,
//   once at the last one; the sixth puddles: in to r 4 on its waymark; their blast: out to its Wave Cannon spot;
//   the second lasers: its place in the Wild Charge's line; the Wild Charge: its spot for the autos.
// Whatever its seat, the exaflares, their step, its walk and its reaction time, nobody may die, and the human keeps the
// slack the margin tests ask of the bots from every hit it dodges.
public class TopP6GauntletExaflares1LpduHumanTests
{
    private static readonly float[] Steps = [1.1f, 1.0f];
    private static readonly float[] ReactionTimes = [0f, 0.3f, 0.6f];
    private const float JitterSlack = 0.5f;

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
        => from seat in PerRole.All
           from step in Steps
           from delay in ReactionTimes
           select new TestCaseData(seat, step, delay).SetName($"{{m}}({seat}, step {step:F1}, {delay:F1}s late)");

    // Each case plays every first point once; the turn alternates with the point, the seat and the step, so every seat
    // plays every point both ways round, and the walk with the point and the seat.
    [TestCaseSource(nameof(Matrix))]
    public void AHumanPlayingLpduIsNeverCaught(PartyRole seat, float step, float delay)
    {
        var tightest = new Clearances();
        var problems = new List<string>();
        var stepIndex = Array.IndexOf(Steps, step);
        for (var first = 0; first < TopP6UnlimitedWaveCannon.CompassPoints; first++)
        {
            var clockwise = (first + (int)seat + stepIndex) % 2 == 0;
            var walk = Enum.GetValues<Walk>()[(first + (int)seat) % 3];
            var seed = 7919 * first + 97 * (int)seat + 11 * stepIndex + (int)MathF.Round(delay * 10f);
            problems.AddRange(Play(new Roll(seat, first, clockwise, step, walk, seed), delay, tightest));
        }

        TestContext.Out.WriteLine($"{TestContext.CurrentContext.Test.Name}: the human's tightest clearances:{Environment.NewLine}{tightest}");
        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(30)));
            Assert.That(tightest.Within(JitterSlack), Is.Empty, "the human's clearances");
        });
    }

    internal sealed record Roll(PartyRole Seat, int First, bool Clockwise, float Step, Walk Walk, int Seed)
    {
        public override string ToString()
            => $"seed {Seed}, {Seat} (first {First}, {(Clockwise ? "clockwise" : "counter-clockwise")}, step {Step:F1}, {Walk})";
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
            run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, roll.Seed, new ScenarioRunOptions
            {
                PlayerRole = roll.Seat,
                Overrides = o => Pin((TopP6GauntletStateOverrides)o, roll.First, roll.Clockwise, roll.Step),
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

        if (!run.Passed) problems.Add(run.ToString());
        problems.AddRange(guide.Problems);
        if (run.Passed && guide.Walks < guide.Legs)
            problems.Add($"the human set off on {guide.Walks} of its {guide.Legs} walks");
        return problems.Select(problem => $"{roll} ({delay:F1}s late): {problem}").ToList();
    }

    // Each cue, seen in what the sim sent, queues a walk that sets off a reaction time after it, aimed when it sets off;
    // a walk that waits for the last one sets off no sooner than that one arrives.
    private sealed class Guide(Roll roll, float late)
    {
        private const float HoldOnTheFirstPuddles = 1f;

        public readonly List<string> Problems = [];
        public int Legs { get; private set; }
        public int Walks { get; private set; }

        private readonly Random rng = new(roll.Seed);
        private readonly Queue<(float Depart, Func<Vector2> To, bool AfterTheLast)> legs = new();
        private (float Depart, Vector2[] Points, float Speed)? walking;
        private readonly List<(float Shown, Vector2 At)> arrows = [];
        private readonly List<(float Dropped, Vector2 At)> puddles = [];
        private int rowsRead, puddleSets, puddleBlasts, laserSets;
        private float lastPuddleSet = float.NegativeInfinity, lastPuddleBlast = float.NegativeInfinity, lastLasers = float.NegativeInfinity;
        private bool started, inTheMiddle, followedTheFirst;

        private float ArrowTurn => MathF.Sign(Cross(arrows[0].At, arrows[1].At));

        // r 14 at the first arrow's bearing turned 45° the way the arrows turn, `points` times.
        private Vector2 OnThePath(int points)
        {
            var first = MathF.Atan2(arrows[0].At.X, -arrows[0].At.Y) + ArrowTurn * points * MathF.PI / 4f;
            return new Vector2(14f * MathF.Sin(first), -14f * MathF.Cos(first));
        }

        public void Tick(ScenarioProbe p)
        {
            var me = p.Member(roll.Seat)!;
            if (!me.IsAlive()) return;
            if (!started)
            {
                started = true;
                Queue(0f, () => AutoSpots[roll.Seat]);
            }
            ReadWhatTheSimSent(p);
            if (!inTheMiddle && arrows.Count >= 2 && p.Time >= arrows[1].Shown)
            {
                inTheMiddle = true;
                Queue(arrows[1].Shown, () => Vector2.Zero);
            }
            Advance(p, me);
        }

        private void ReadWhatTheSimSent(ScenarioProbe p)
        {
            var rows = p.Fake.Timeline.Rows;
            for (; rowsRead < rows.Count; rowsRead++)
            {
                var row = rows[rowsRead];
                var id = uint.TryParse(row["id"], out var parsed) ? parsed : 0u;
                var t = Float(row["t"]);
                switch (row["kind"], id)
                {
                    case ("cast", ActionId.UnlimitedWaveCannon):
                        Queue(t, () => OutsideTheHitbox[roll.Seat]);
                        break;
                    case ("cast", ActionId.WaveCannon_7BAD):
                        arrows.Add((t + Float(row["omen_delay"]), Local(row)));
                        break;
                    case ("cast", ActionId.WaveCannon_7BAF):
                        puddles.Add((t, Local(row)));
                        if (FirstOfARound(ref lastPuddleSet, t)) OnAPuddleSet(t, ++puddleSets);
                        break;
                    case ("ability", ActionId.WaveCannon_7BAF) when FirstOfARound(ref lastPuddleBlast, t) && ++puddleBlasts == 6:
                        Queue(t, () => SpreadSpots[roll.Seat]);
                        break;
                    case ("ability", ActionId.WaveCannon_7BAE) when !followedTheFirst && IsTheFirstExaflareEightOut(row):
                        followedTheFirst = true;
                        Queue(t, () => OnThePath(0), afterTheLast: true);
                        break;
                    case ("ability", ActionId.WaveCannonProtean) when FirstOfARound(ref lastLasers, t) && ++laserSets == 2:
                        Queue(t, () => WildChargeLine[roll.Seat]);
                        break;
                    case ("ability", ActionId.WaveCannonWildCharge):
                        Queue(t, () => AutoSpots[roll.Seat]);
                        break;
                }
            }
        }

        private void OnAPuddleSet(float t, int set)
        {
            switch (set)
            {
                case 1:
                    Queue(t + HoldOnTheFirstPuddles, () => OnThePath(-1));
                    break;
                case 4:
                    Queue(t, () => OnThePath(1), afterTheLast: true);
                    break;
                case 5:
                    Queue(t, () => OnThePath(2), afterTheLast: true);
                    break;
                case 6:
                    Queue(t, () => InsideTheLastPuddles(roll.Seat), afterTheLast: true);
                    break;
            }
        }

        private bool IsTheFirstExaflareEightOut(Dictionary<string, string> row)
            => arrows.Count > 0 && Vector2.Distance(Local(row), Vector2.Normalize(arrows[0].At) * 8f) < 0.5f;

        private static bool FirstOfARound(ref float last, float now)
        {
            var first = now - last > 0.5f;
            last = now;
            return first;
        }

        private void Queue(float cue, Func<Vector2> to, bool afterTheLast = false)
        {
            legs.Enqueue((cue + late, to, afterTheLast));
            Legs++;
        }

        private void Advance(ScenarioProbe p, SimCharacter me)
        {
            if (legs.TryPeek(out var next) && p.Time >= next.Depart && (!next.AfterTheLast || walking is null))
            {
                legs.Dequeue();
                Walks++;
                var from = Flat(me.Position);
                var to = next.To();
                walking = (p.Time, [from, .. TopP6HumanWalk.Points(roll.Walk, from, to, rng, p.Time, puddles)], TopP6HumanWalk.Speed(roll.Walk));
            }
            if (walking is not { } w) return;
            var (at, heading, arrived) = TopP6HumanWalk.Along(w.Points, (p.Time - w.Depart) * w.Speed);
            me.SetPosition(new Placement(new Vector3(at.X, 0f, at.Y), MathF.Atan2(heading.X, heading.Y)));
            if (arrived) walking = null;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
    }

    // How close the human came to each hit it dodged (or how deep it stood in the one it had to share), as each took
    // its targets, over every run of a case: the scenario's own areas, read off AoeQuery.
    private sealed class Clearances
    {
        private readonly Dictionary<string, (float Clearance, string Where)> tightest = new();
        private ScenarioProbe? probe;
        private Roll? playing;

        public void Watch(ScenarioProbe p) => probe = p;

        public Action<AoeQuery> For(Roll roll)
        {
            probe = null;
            playing = roll;
            var seat = roll.Seat;
            return hit =>
            {
                if (probe is not { } p || p.Member(seat) is not { } me || !me.IsAlive()) return;
                var clearance = hit.SignedDistance(me.Position) ?? float.NaN;
                var on = Vector2.Distance(Flat(hit.Source.Position), Flat(me.Position)) < 0.01f;
                var where = $"{Describe(Flat(me.Position))} from {Describe(Flat(hit.Source.Position))}";
                switch (hit.ActionId)
                {
                    case ActionId.WaveCannon_7BAD or ActionId.WaveCannon_7BAE: Note("an exaflare", clearance, where); break;
                    case ActionId.WaveCannon_7BAF: Note("a puddle", clearance, where); break;
                    case ActionId.WaveCannonProtean when !IsAimedAt(hit, me, p): Note("another player's laser", clearance, where); break;
                    case ActionId.WaveCannonWildCharge: Note("inside the Wild Charge's line", -clearance, where); break;
                    case ActionId.Unknown7ddf when !on: Note("another player's Flash Gale", clearance, where); break;
                }
            };
        }

        private static string Describe(Vector2 at)
            => $"({at.X.ToString("F1", CultureInfo.InvariantCulture)}, {at.Y.ToString("F1", CultureInfo.InvariantCulture)})";

        // A laser's target stands nearest its line's axis, ahead of Alpha Omega.
        private static bool IsAimedAt(AoeQuery hit, SimCharacter me, ScenarioProbe p)
            => PerRole.All.Select(r => p.Member(r)).Where(m => m.IsAlive())
                          .MinBy(m => Across(hit.Source, m!.Position) + (Along(hit.Source, m.Position) < 0f ? 100f : 0f)) == me;

        private void Note(string name, float clearance, string where)
        {
            if (tightest.TryGetValue(name, out var before) && before.Clearance <= clearance) return;
            tightest[name] = (clearance, $"t={probe!.Time.ToString("F2", CultureInfo.InvariantCulture)} at {where}, {playing}");
        }

        public IEnumerable<string> Within(float slack)
            => tightest.Where(t => !(t.Value.Clearance > slack))
                       .Select(t => $"{t.Key} {t.Value.Clearance.ToString("F2", CultureInfo.InvariantCulture)} ({t.Value.Where})");

        public override string ToString()
            => string.Join(Environment.NewLine, tightest.OrderBy(t => t.Value.Clearance)
                                                        .Select(t => $"  {t.Key} {t.Value.Clearance.ToString("F2", CultureInfo.InvariantCulture)} ({t.Value.Where})"));
    }

    private static float Along(Placement line, Vector3 at)
        => (at.X - line.Position.X) * MathF.Sin(line.Rotation) + (at.Z - line.Position.Z) * MathF.Cos(line.Rotation);

    private static float Across(Placement line, Vector3 at)
        => MathF.Abs((at.X - line.Position.X) * MathF.Cos(line.Rotation) - (at.Z - line.Position.Z) * MathF.Sin(line.Rotation));

    private static Vector2 Local(Dictionary<string, string> row)
        => new Vector2(Float(row["x"]), Float(row["z"])) - new Vector2(TopZone.Instance.Origin.X, TopZone.Instance.Origin.Z);

    private static float Float(string value) => float.Parse(value, CultureInfo.InvariantCulture);
}
