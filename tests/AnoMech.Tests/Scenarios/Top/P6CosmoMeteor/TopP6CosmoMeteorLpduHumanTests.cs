using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Multiplayer;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P6CosmoMeteor;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopP6Constants;
using static AnoMech.Tests.TopP6CosmoMeteorLpduAiTests;

namespace AnoMech.Tests;

// The sunny-day soak drives the player's seat with the bots' own AI. Here the seat is a human who plays LPDU from what a
// player can see: Cosmo Meteor's cast bar, the bait circles dropping under everyone as it ends, the spreads going off
// and the flare markers. It starts on its spot for the autos the block opens on, and at each cue walks to its next spot
// a fixed reaction time late, straight, curving or sprinting, never stepping round anyone:
//   the cast bar: under Alpha Omega;
//   the baits: out to its meteor clock spot;
//   the eighth spread (the shield healer only): into the middle;
//   the flare markers: R1 holds A; the rest to the stack (north on R1, south when R1 has a flare) or, with a flare, to
//   the cardinal the clock-order rule gives it.
// Its part is pinned (no flare with the stack north or south, a flare with R1 free or marked too); the other flares, the
// stack target and the spread waves come from the seed. Whatever its seat, part, walk and reaction time, nobody may die,
// and the human keeps the slack the margin tests ask of the bots from every hit.
public class TopP6CosmoMeteorLpduHumanTests
{
    // Six fixed rolls per seat, two per walking style; HumanSeeds asks for more, as SunnySeeds does for the soak.
    private static IEnumerable<int> Seeds => Enumerable.Range(1, TestContext.Parameters.Get("HumanSeeds", 6));
    private static readonly float[] ReactionTimes = [0f, 0.5f, 1f];
    private const float JitterSlack = 0.5f;

    public enum Part { NoFlareStackNorth, NoFlareStackSouth, Flare, FlareWithThePhysRanged }

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
        => from part in Enum.GetValues<Part>()
           from delay in ReactionTimes
           select new TestCaseData(part, delay).SetName($"{{m}}({part}, {delay:F1}s late)");

    [TestCaseSource(nameof(Matrix))]
    public void AHumanPlayingLpduIsNeverCaught(Part part, float delay)
    {
        var tightest = new Clearances();
        var runs = 0;
        var problems = new List<string>();
        foreach (var seat in PerRole.All)
        foreach (var seed in Seeds)
        {
            if (Roll.For(seat, part, seed) is not { } roll) continue;
            runs++;
            problems.AddRange(Play(roll, delay, tightest));
        }

        TestContext.Out.WriteLine($"{TestContext.CurrentContext.Test.Name}: {runs} runs; the human's tightest clearances:{Environment.NewLine}{tightest}");
        Assert.Multiple(() =>
        {
            Assert.That(runs, Is.GreaterThan(0), "a seat could play it");
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
            run = ScenarioRun.Execute(typeof(TopP6CosmoMeteorScenario), LpduStrat, roll.Seed, new ScenarioRunOptions
            {
                PlayerRole = roll.Seat,
                Overrides = o => Pin((TopP6CosmoMeteorStateOverrides)o, roll.Flares, roll.Stack),
                Takeovers = [new Takeover(0f, AutoSpots[roll.Seat])],
                Probe = p =>
                {
                    tightest.Watch(p);
                    problems.AddRange(BotsOffTheirSpotsAsTheFlaresLand(p, roll));
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

    // The test rolls what the human could see and pins it; the bots read the same roll from the scenario.
    internal sealed record Roll(PartyRole Seat, Part Part, int Seed, Walk Walk, PartyRole[] Flares, PartyRole Stack)
    {
        public static Roll? For(PartyRole seat, Part part, int seed)
        {
            var physRanged = seat == PhysRangedDps;
            if (physRanged && part is Part.NoFlareStackSouth or Part.Flare) return null;
            var rng = new Random(seed * 1009 + (int)seat * 31 + (int)part * 7);
            PartyRole[] marked = part switch
            {
                Part.NoFlareStackNorth => [],
                Part.NoFlareStackSouth => [PhysRangedDps],
                Part.Flare => [seat],
                _ => physRanged ? [seat] : [seat, PhysRangedDps],
            };
            var others = PerRole.All.Where(r => r != seat && r != PhysRangedDps).OrderBy(_ => rng.Next()).ToList();
            var flares = marked.Concat(others).Take(TopP6CosmoMeteorState.FlareTargets).OrderBy(r => Array.IndexOf(Clockwise, r)).ToArray();
            var withoutAFlare = PerRole.All.Where(r => !flares.Contains(r)).ToList();
            var walk = Enum.GetValues<Walk>()[(seed + (int)seat) % 3];
            return new Roll(seat, part, seed, walk, flares, withoutAFlare[rng.Next(withoutAFlare.Count)]);
        }

        public override string ToString()
            => $"seed {Seed}, {Seat} ({Part}, {Walk}, flares {Describe(Flares)}, stack {Stack})";
    }

    // Each cue queues a walk that sets off a reaction time later and is aimed as it sets off.
    private sealed class Guide(Roll roll, float late)
    {
        private const float RunSpeed = 6.5f, LeastCurve = 1f, MostCurve = 2f;

        public readonly List<string> Problems = [];
        public int Legs { get; private set; }
        public int Walks { get; private set; }

        private readonly Random rng = new(roll.Seed);
        private readonly Queue<(float Depart, Func<Vector2> To)> legs = new();
        private (float Depart, Vector2[] Points, float Speed)? walking;
        private Dictionary<string, PartyRole>? roleById;
        private readonly List<PartyRole> marked = [];
        private int rowsRead, spreadsSeen;
        private bool castSeen, baitsSeen;

        public void Tick(ScenarioProbe p)
        {
            var me = p.Member(roll.Seat)!;
            if (!me.IsAlive()) return;
            roleById ??= PerRole.All.ToDictionary(r => p.Member(r)!.EntityId.ToString("X8"), r => r);
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
                if (row["kind"] == "cast" && id == $"{P6ActionId.CosmoMeteor}" && !castSeen)
                {
                    castSeen = true;
                    Queue(p.Time, () => Vector2.Zero);
                }
                else if (row["kind"] == "cast" && id == $"{P6ActionId.CosmoMeteorBait}" && !baitsSeen)
                {
                    baitsSeen = true;
                    Queue(p.Time, () => Clock[roll.Seat]);
                }
                else if (row["kind"] == "ability" && id == $"{P6ActionId.CosmoMeteorSpread}" && ++spreadsSeen == 8 && roll.Seat == ShieldHealer)
                    Queue(p.Time, () => Vector2.Zero);
                else if (row["kind"] is "ac" or "ac_self" && id == "0x22" && row["p"].StartsWith($"0x{P6LockonId.Flare:X},")
                         && roleById!.TryGetValue(row["actor"], out var target))
                    ReadAFlareMarker(p.Time, target);
            }
        }

        private void ReadAFlareMarker(float now, PartyRole target)
        {
            marked.Add(target);
            if (marked.Count < TopP6CosmoMeteorState.FlareTargets) return;
            if (!marked.Order().SequenceEqual(roll.Flares.Order()))
                Problems.Add($"the human saw flares on {Describe(marked)}, the roll put them on {Describe(roll.Flares)}");
            if (roll.Seat != PhysRangedDps)
                Queue(now, () => ExpectedFinalSpots([.. marked])[roll.Seat]);
        }

        private void Queue(float cue, Func<Vector2> to)
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
                walking = (p.Time, [from, .. Curve(from, next.To())], roll.Walk == Walk.Sprinting ? AiManager.SprintSpeed : RunSpeed);
            }
            if (walking is not { } w) return;
            var (at, heading, arrived) = Along(w.Points, (p.Time - w.Depart) * w.Speed);
            me.SetPosition(new Placement(new Vector3(at.X, 0f, at.Y), MathF.Atan2(heading.X, heading.Y)));
            if (arrived) walking = null;
        }

        // A curving walk bulges one to two yalms off the straight line.
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

    // However the human walked, every bot stands on its spot just before the flares and the stack land.
    private const float FlaresLandAt = 30f, JustBefore = 0.1f, OffSpotTolerance = 0.75f;

    private static IEnumerable<string> BotsOffTheirSpotsAsTheFlaresLand(ScenarioProbe p, Roll roll)
    {
        if (!p.Crossed(FlaresLandAt - JustBefore)) return [];
        var spots = ExpectedFinalSpots(roll.Flares);
        return PerRole.All.Where(r => r != roll.Seat && p.Member(r).IsAlive())
                      .Select(r => (Role: r, Off: Vector2.Distance(Flat(p.Member(r)!.Position), spots[r])))
                      .Where(b => b.Off > OffSpotTolerance)
                      .Select(b => $"{b.Role} stood {b.Off:F2} y off its spot as the flares landed")
                      .ToList();
    }

    // How close the human came to each hit it had to dodge, or how deep it stood in the stack it had to share, as each
    // decided, over every run of a case: the scenario's own areas, read off AoeQuery, and the flares' kill radius.
    private sealed class Clearances
    {
        private readonly Dictionary<string, (float Clearance, string Where)> tightest = new();
        private readonly HashSet<(float, uint, Placement)> seen = [];
        private ScenarioProbe? probe;
        private Roll? playing;

        public void Watch(ScenarioProbe p) => probe = p;

        public Action<AoeQuery> For(Roll roll)
        {
            seen.Clear();
            probe = null;
            playing = roll;
            return hit =>
            {
                if (probe is not { } p || p.Member(roll.Seat) is not { } me || !me.IsAlive()) return;
                if (!seen.Add((p.World.Events.Elapsed, hit.ActionId, hit.Source))) return;
                var at = Flat(me.Position);
                var source = Flat(hit.Source.Position);
                var mine = Vector2.Distance(source, at) < 0.01f;
                var clearance = hit.SignedDistance(me.Position) ?? float.NaN;
                switch (hit.ActionId)
                {
                    case P6ActionId.CosmoMeteorBait:
                        Note("the baits", clearance);
                        break;
                    case P6ActionId.CosmoMeteorSpread when !mine:
                        Note("other players' spreads", clearance);
                        break;
                    case P6ActionId.CosmoMeteorSpread:
                        foreach (var other in Others(p, roll.Seat))
                            Note("others in the human's spread", Vector2.Distance(other, at) - 5f);
                        break;
                    case P6ActionId.CosmoMeteorStack when roll.Flares.Contains(roll.Seat):
                        Note("outside the stack with a flare", clearance);
                        break;
                    case P6ActionId.CosmoMeteorStack:
                        Note("inside the stack", -clearance);
                        break;
                    case P6ActionId.CosmoMeteorFlare when mine:
                        foreach (var other in Others(p, roll.Seat))
                            Note("others within the human's flare", Vector2.Distance(other, at) - P6Geometry.FlareLethalWithin);
                        break;
                    case P6ActionId.CosmoMeteorFlare:
                        Note("other players' flares", Vector2.Distance(source, at) - P6Geometry.FlareLethalWithin);
                        break;
                }
            };
        }

        private static IEnumerable<Vector2> Others(ScenarioProbe p, PartyRole seat)
            => PerRole.All.Where(r => r != seat && p.Member(r).IsAlive()).Select(r => Flat(p.Member(r)!.Position));

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
