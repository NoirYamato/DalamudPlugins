using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top;
using AnoMech.Scenarios.Top.P6Gauntlet;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Tests.TopP6GauntletExaflares2LpduAiTests;
using static AnoMech.Tests.TopP6HumanWalk;

namespace AnoMech.Tests;

// The sunny-day soak drives the player's seat with the bots' own AI. Here the seat is a human who plays the Gauntlet's
// Exaflares 2 part by LPDU from what a player can see: Unlimited Wave Cannon's cast bar, the first two arrows (each shows
// 4.5 s into its exaflare's cast), the puddles dropping under everyone, the first exaflare going off, Cosmo Dive's bar
// ending, the dive's hits and the other melee's limit break 3 landing. At each cue it walks to its next spot a fixed
// reaction time late, straight, curving (never toward a puddle's circle it can see) or sprinting, and never steps round
// anyone:
//   the run's start: to its auto spot, where it already stands; the cast bar: out of the hitbox (the MT north, the rest
//   south; the OT is out); the second arrow: into the middle; the first puddles, then a beat: beside the first exaflare,
//   away from the second; the first exaflare going off at r 8: onto its point; puddles 4 and 5: on round to the second
//   and the third exaflare's points; puddle 6: the six onto the last exaflare's point, a tank from there through the boss
//   into its arm of the Y; the dive's hits: back to its auto spot (the OT out at r 16, west when the hold is east), the
//   second melee to the middle and the MT back a step to make room for it.
// A melee presses its own limit break 3 on Alpha Omega as its client does: the first melee as Cosmo Dive's bar ends, on
// the hold; the second once in the middle and the first's has landed. It stands still through the 4.5 s cast; a walk
// cued meanwhile sets off as the cast ends. A tank presses its own LPDU set for its buster as Cosmo Dive's bar ends, as
// late (the warrior main tank Rampart, Thrill of Battle and Bloodwhetting, the paladin off-tank Rampart, Bulwark and Holy
// Sheltron), with "Resolve your own actions" on, so its mitigation is checked. Whatever its seat, the roll, the
// exaflares' step, its reaction time and its walk, nobody may die, the bots hold their spots at the dive, and the human
// keeps the slack the margin tests ask of the bots from every hit it dodges.
public class TopP6GauntletExaflares2LpduHumanTests
{
    // The way out of the middle is the tight one: leave before about 12.5 and the second puddles drop where the start
    // point is, after about 13.7 and the first ones still catch you. A second late there is too late whatever the strat,
    // so the latest human here reacts 0.6 s late, where P3's and P4's react up to a second late.
    private static readonly float[] ReactionTimes = [0f, 0.3f, 0.6f];
    private const float JitterSlack = 0.5f;

    private static IEnumerable<TestCaseData> Matrix()
        => from step in Steps
           from walk in Enum.GetValues<Walk>()
           from delay in ReactionTimes
           select new TestCaseData(step, walk, delay).SetName($"{{m}}({walk}, {step:F1}s step, {delay:F1}s late)");

    [TestCaseSource(nameof(Matrix))]
    public void AHumanPlayingLpduIsNeverCaught(float step, Walk walk, float delay)
    {
        var tightest = new Clearances();
        var problems = new List<string>();
        var runs = 0;
        foreach (var seat in PerRole.All)
        foreach (var (first, clockwise) in Patterns)
        {
            runs++;
            problems.AddRange(Play(seat, first, clockwise, step, walk, delay, tightest));
        }

        TestContext.Out.WriteLine($"{runs} runs; the human's tightest clearances:{Environment.NewLine}{tightest}");
        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(30)));
            Assert.That(tightest.Within(JitterSlack), Is.Empty, "the human's clearances");
        });
    }

    private static List<string> Play(PartyRole seat, int first, bool clockwise, float step, Walk walk, float delay, Clearances tightest)
    {
        var seed = 1009 * (int)seat + 31 * first + (clockwise ? 7 : 0) + (int)walk;
        var guide = new Guide(seat, delay, walk, seed);
        var problems = new List<string>();
        var measure = tightest.For(seat, $"{seat}, first {first}, clockwise {clockwise}, {walk}, {step:F1}s step, {delay:F1}s late");
        ScenarioRun run;
        AoeQuery.Evaluated += measure;
        try
        {
            run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, seed, new ScenarioRunOptions
            {
                PlayerRole = seat,
                UserActionsEnabled = true,
                Overrides = o => Pin((TopP6GauntletStateOverrides)o, first, clockwise, step),
                Takeovers = [new Takeover(0f, null)],
                Probe = p =>
                {
                    tightest.Watch(p);
                    guide.Tick(p);
                    problems.AddRange(BotsOffTheirDiveSpots(p, seat, first, clockwise));
                },
            });
        }
        finally
        {
            AoeQuery.Evaluated -= measure;
        }

        if (!run.Passed) problems.Add(run.ToString());
        if (guide.FirstRead != first || guide.ClockwiseRead != clockwise)
            problems.Add($"the human read the first exaflare on {guide.FirstRead} turning {(guide.ClockwiseRead == true ? "clockwise" : "counter-clockwise")}");
        if (run.Passed && guide.Walks < guide.Legs)
            problems.Add($"the human set off on {guide.Walks} of its {guide.Legs} walks");
        if (guide.Duty is { } duty && guide.PressedAt is null)
            problems.Add($"the human never pressed its {duty} limit break 3");
        if (seat.IsTank() && !guide.Mitigated)
            problems.Add("the human tank never pressed its own set for its buster");
        return problems.Select(problem => $"{seat}, first {first}, clockwise {clockwise} ({walk}, {step:F1}s step, {delay:F1}s late, seed {seed}): {problem}")
                       .ToList();
    }

    private const float JustBeforeTheDive = 27.5f, OffSpotTolerance = 0.75f;

    private static IEnumerable<string> BotsOffTheirDiveSpots(ScenarioProbe p, PartyRole seat, int first, bool clockwise)
    {
        if (!p.Crossed(JustBeforeTheDive)) return [];
        var spots = Expected(21.5f, first, clockwise);
        return PerRole.All.Where(r => r != seat && p.Member(r).IsAlive())
                      .Select(r => (Role: r, Off: Distance(Flat(p.Member(r)!.Position), spots[r])))
                      .Where(b => b.Off > OffSpotTolerance)
                      .Select(b => $"{b.Role} stood {b.Off:F2} y off its dive spot")
                      .ToList();
    }

    // Each cue, once seen, sends the human off a reaction time later, aimed where it reads the next spot to be then. A
    // new walk replaces the one before.
    private sealed class Guide(PartyRole seat, float late, Walk walk, int seed)
    {
        private const float CentreBeat = 1f;
        private const float MeleeLimitBreak3Cast = 4.5f;
        private static readonly Vector3 Origin = new(100f, 0f, 100f);

        public int? FirstRead { get; private set; }
        public bool? ClockwiseRead { get; private set; }
        public int Legs => Route.Length;
        public int Walks { get; private set; }
        public TopP6LimitBreakDuty? Duty { get; private set; }
        public float? PressedAt { get; private set; }
        public bool Mitigated { get; private set; }

        private readonly Random rng = new(seed);
        private (Func<float?> Cue, Func<Vector2> To)[]? route;
        private int next;
        private (float Depart, Vector2[] Points, float Speed)? walking;
        private readonly List<(float At, Vector2 Spot)> arrows = [];
        private readonly List<float> puddleDrops = [];
        private readonly List<(float Dropped, Vector2 At)> puddles = [];
        private float? waveCannonCast, firstExaflarePast, diveBarEnded, diveHits, firstMeleeLanded;
        private int rowsRead;
        private bool secondMelee, planRead;
        private uint otherMeleesLimitBreak3;

        private (Func<float?> Cue, Func<Vector2> To)[] Route => route ??= Plan().ToArray();

        private bool Casting(float now) => PressedAt is { } pressed && now < pressed + MeleeLimitBreak3Cast;

        public void Tick(ScenarioProbe p)
        {
            var me = p.Member(seat)!;
            if (!me.IsAlive()) return;
            ReadThePlan(p);
            ReadTheTimeline(p);
            PressOnTheCue(p);
            MitigateOnTheCue(p);
            Advance(p, me);
        }

        // A tank's own set for its buster, as the dive's bar ends.
        private void MitigateOnTheCue(ScenarioProbe p)
        {
            if (!seat.IsTank() || Mitigated || diveBarEnded is not { } ended || p.Time < ended + late) return;
            Mitigated = true;
            TopP6HumanMitigation.PressTheSeatsLpduSetNow(p.World, seat, TopP6TankHit.CosmoDive2);
        }

        private void ReadThePlan(ScenarioProbe p)
        {
            if (planRead) return;
            planRead = true;
            var plan = TopP6GauntletRun.State(p).LimitBreakPlan;
            secondMelee = plan.SeatOf(TopP6LimitBreakDuty.SecondDiveMelee) == seat;
            otherMeleesLimitBreak3 = TopP6GauntletRun.LimitBreak3(seat == MeleeDpsA ? MeleeDpsB : MeleeDpsA);
            if (plan.HumanPresses(TopP6LimitBreakDuty.FirstDiveMelee)) Duty = TopP6LimitBreakDuty.FirstDiveMelee;
            if (plan.HumanPresses(TopP6LimitBreakDuty.SecondDiveMelee)) Duty = TopP6LimitBreakDuty.SecondDiveMelee;
        }

        // The first melee on the hold as the bar ends; the second in the middle once the first's has landed.
        private void PressOnTheCue(ScenarioProbe p)
        {
            if (Duty is not { } duty || PressedAt is not null || walking is not null) return;
            var cue = duty == TopP6LimitBreakDuty.FirstDiveMelee ? diveBarEnded : next == Route.Length ? firstMeleeLanded : null;
            if (cue is not { } seen || p.Time < seen + late) return;
            PressedAt = p.Time;
            TopP6HumanLimitBreak.Press(p.World, 0f, TopP6GauntletRun.LimitBreak3(seat), () => TopP6GauntletRun.Scenario(p).AlphaOmega?.Enemy);
        }

        private void ReadTheTimeline(ScenarioProbe p)
        {
            var rows = p.Fake.Timeline.Rows;
            for (; rowsRead < rows.Count; rowsRead++)
            {
                var row = rows[rowsRead];
                var t = Float(row["t"]);
                switch (row["kind"], uint.TryParse(row["id"], out var id) ? id : 0u)
                {
                    case ("cast", ActionId.UnlimitedWaveCannon):
                        waveCannonCast ??= t;
                        break;
                    case ("cast", ActionId.WaveCannon_7BAD):
                        arrows.Add((t + Float(row["omen_delay"]), Local(row)));
                        break;
                    case ("cast", ActionId.WaveCannon_7BAF):
                        puddles.Add((t, Local(row)));
                        if (puddleDrops.Count == 0 || t - puddleDrops[^1] > 0.5f) puddleDrops.Add(t);
                        break;
                    case ("ability", ActionId.WaveCannon_7BAE) when FirstRead is { } firstPoint
                                                                    && Vector2.Distance(Local(row), OnRing(45f * firstPoint, 8f)) < 1f:
                        firstExaflarePast ??= t;
                        break;
                    case ("ability", ActionId.CosmoDive):
                        diveBarEnded ??= t;
                        break;
                    case ("ability", ActionId.CosmoDive_7BA7):
                        diveHits ??= t;
                        break;
                    case ("ability", var limitBreak) when limitBreak == otherMeleesLimitBreak3:
                        firstMeleeLanded ??= t;
                        break;
                }
            }
            ReadTheArrows(p.Time);
        }

        private void ReadTheArrows(float now)
        {
            var shown = arrows.Where(a => a.At <= now).Take(2).ToList();
            if (shown.Count >= 1) FirstRead ??= (int)MathF.Round(BearingOf(shown[0].Spot) / 45f) % 8;
            if (shown.Count < 2 || ClockwiseRead is not null) return;
            var turn = BearingOf(shown[1].Spot) - BearingOf(shown[0].Spot);
            ClockwiseRead = (turn + 360f) % 360f < 180f;
        }

        // ---- The legs, each with its cue and its spot.

        private IEnumerable<(Func<float?> Cue, Func<Vector2> To)> Plan()
        {
            yield return (() => 0f, () => AutoSpots[seat]);
            yield return (() => waveCannonCast, () => SteppedOut[seat]);
            yield return (() => arrows.Count >= 2 ? arrows[1].At : null, () => Vector2.Zero);
            yield return (() => PuddleDrop(0) + CentreBeat, () => OnRing(Bearing(-1), FollowRadius));
            yield return (() => firstExaflarePast, () => OnRing(Bearing(0), FollowRadius));
            yield return (() => PuddleDrop(3), () => OnRing(Bearing(1), FollowRadius));
            yield return (() => PuddleDrop(4), () => OnRing(Bearing(2), FollowRadius));
            yield return (() => PuddleDrop(5), DiveSpot);
            yield return (() => diveHits, AutoSpotAfterTheDive);
        }

        private Vector2 AutoSpotAfterTheDive() => seat switch
        {
            MainTank => new Vector2(0f, -7f),
            OffTank => (Bearing(3) % 360f + 360f) % 360f == 90f ? OffTankWestAfterTheDive : OffTankEastAfterTheDive,
            _ => secondMelee ? Vector2.Zero : AutoSpots[seat],
        };

        private float? PuddleDrop(int set) => puddleDrops.Count > set ? puddleDrops[set] : null;

        private float Bearing(int exaflare) => 45f * (FirstRead!.Value + (ClockwiseRead!.Value ? exaflare : -exaflare));

        private Vector2 DiveSpot() => seat switch
        {
            MainTank => OnRing(Bearing(3) + 135f, TankRadius),
            OffTank => OnRing(Bearing(3) + 225f, TankRadius),
            _ => OnRing(Bearing(3), FollowRadius),
        };

        // ---- Walking.

        private void Advance(ScenarioProbe p, SimCharacter me)
        {
            if (next < Route.Length && Route[next].Cue() is { } cue && p.Time >= cue + late && !Casting(p.Time))
            {
                var to = Route[next++].To();
                Walks++;
                var from = Flat(me.Position);
                walking = (p.Time, [from, .. TopP6HumanWalk.Points(walk, from, to, rng, p.Time, puddles)], TopP6HumanWalk.Speed(walk));
            }
            if (walking is not { } w) return;
            var (at, heading, arrived) = TopP6HumanWalk.Along(w.Points, (p.Time - w.Depart) * w.Speed);
            me.SetPosition(new Placement(new Vector3(at.X, 0f, at.Y), MathF.Atan2(heading.X, heading.Y)));
            if (arrived) walking = null;
        }

        private static Vector2 Local(Dictionary<string, string> row) => new(Float(row["x"]) - Origin.X, Float(row["z"]) - Origin.Z);

        private static float Float(string value) => float.Parse(value, CultureInfo.InvariantCulture);

        private static float BearingOf(Vector2 v) => (MathF.Atan2(v.X, -v.Y) * 180f / MathF.PI + 360f) % 360f;

        private static Vector2 OnRing(float bearing, float radius) => Flat(TopP6AlphaOmega.AtBearing(bearing, radius));
    }

    // How close the human came to each hit it dodged (or how deep it stood in the stack it had to share), as each
    // resolved, over every run of a case: the scenario's own areas, read off AoeQuery.
    private sealed class Clearances
    {
        private readonly Dictionary<string, (float Clearance, string Where)> tightest = new();
        private ScenarioProbe? probe;
        private readonly HashSet<(float, uint, Vector3)> seen = [];

        public void Watch(ScenarioProbe p) => probe = p;

        public Action<AoeQuery> For(PartyRole seat, string playing)
        {
            seen.Clear();
            probe = null;
            return hit =>
            {
                if (probe is not { } p || p.Member(seat) is not { } me || !me.IsAlive()) return;
                if (!seen.Add((p.Time, hit.ActionId, hit.Source.Position))) return;
                var on = Vector2.Distance(Flat(hit.Source.Position), Flat(me.Position)) < 0.01f;
                var clearance = hit.SignedDistance(me.Position) ?? float.NaN;
                var where = $"t={p.Time.ToString("F2", CultureInfo.InvariantCulture)} at {Describe(Flat(me.Position))} from {Describe(Flat(hit.Source.Position))}, {playing}";
                switch (hit.ActionId)
                {
                    case ActionId.WaveCannon_7BAD or ActionId.WaveCannon_7BAE: Note("an exaflare", clearance, where); break;
                    case ActionId.WaveCannon_7BAF: Note("a puddle", clearance, where); break;
                    case ActionId.CosmoDive_7BA7 when !on: Note("another's Cosmo Dive buster", clearance, where); break;
                    case ActionId.CosmoDive_7BA8 when seat.IsTank(): Note("the Cosmo Dive stack", clearance, where); break;
                    case ActionId.CosmoDive_7BA8: Note("inside the Cosmo Dive stack", -clearance, where); break;
                    case ActionId.Unknown7ddf when !on: Note("another's Flash Gale", clearance, where); break;
                }
            };
        }

        private void Note(string name, float clearance, string where)
        {
            if (tightest.TryGetValue(name, out var before) && before.Clearance <= clearance) return;
            tightest[name] = (clearance, where);
        }

        private static string Describe(Vector2 at)
            => $"({at.X.ToString("F1", CultureInfo.InvariantCulture)}, {at.Y.ToString("F1", CultureInfo.InvariantCulture)})";

        public IEnumerable<string> Within(float slack)
            => tightest.Where(t => !(t.Value.Clearance > slack))
                       .Select(t => $"{t.Key} {t.Value.Clearance.ToString("F2", CultureInfo.InvariantCulture)} ({t.Value.Where})");

        public override string ToString()
            => string.Join(Environment.NewLine, tightest.OrderBy(t => t.Value.Clearance)
                                                        .Select(t => $"  {t.Key} {t.Value.Clearance.ToString("F2", CultureInfo.InvariantCulture)} ({t.Value.Where})"));
    }
}
