using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P6Gauntlet;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Tests.TopP6GauntletCosmoMemoryLpduAiTests;
using static AnoMech.Tests.TopP6HumanWalk;

namespace AnoMech.Tests;

// The sunny-day soak drives the player's seat with the bots' own AI. Here the seat is a human who plays the Gauntlet's
// Cosmo Memory part by LPDU from what a player can see: Cosmo Memory's cast bar (the plan's Cosmo Memory tank, the
// warrior main tank, presses its limit break 3 halfway through it, through its client), Cosmo Arrow's cast bar, the first
// set's lines once they show (two casts, the cross: in first; four, the outer lines: out first), each wave of lines going
// off, Cosmo Dive's bar ending and its hits. At each cue it walks to its next LPDU spot a fixed reaction time late,
// straight, curving or sprinting, and never steps round anyone. Cosmo Memory kills everyone unless a tank's limit break 3
// has taken hold: a human main tank's own, any other seat the bot main tank's. A human tank presses its own LPDU set for
// its buster as Cosmo Dive's bar ends, the same reaction time late (the warrior main tank Rampart, Thrill of Battle and
// Bloodwhetting, the paladin off-tank Rampart, Bulwark and Holy Sheltron). Whatever its seat, pattern, walk and reaction
// time, nobody may die, and the human keeps the slack the margin tests ask of the bots from every hit it dodges.
public class TopP6GauntletCosmoMemoryLpduHumanTests
{
    // Three fixed seeds per seat, for the bots' jitter; HumanSeeds asks for more, as SunnySeeds does for the soak.
    private static IEnumerable<int> Seeds => Enumerable.Range(1, TestContext.Parameters.Get("HumanSeeds", 3));
    private static readonly float[] ReactionTimes = [0f, 0.5f, 1f];
    private const float JitterSlack = 0.5f;

    // Halfway through Cosmo Memory's 6 s cast.
    private const float LimitBreakIntoTheCast = 3f;

    // The lines' omens show this long into their casts.
    private const float LinesShowAfter = 2f;

    private static IEnumerable<TestCaseData> Matrix()
        => from inFirst in new[] { true, false }
           from walk in Enum.GetValues<Walk>()
           from delay in ReactionTimes
           select new TestCaseData(inFirst, walk, delay).SetName($"{{m}}({(inFirst ? "in" : "out")} first, {walk}, {delay:F1}s late)");

    [TestCaseSource(nameof(Matrix))]
    public void AHumanPlayingLpduIsNeverCaught(bool inFirst, Walk walk, float delay)
    {
        var tightest = new Clearances();
        var runs = 0;
        var problems = new List<string>();
        foreach (var seat in PerRole.All)
        foreach (var seed in Seeds)
        {
            runs++;
            problems.AddRange(Play(seat, inFirst, walk, delay, 7919 * seed + (int)seat, tightest));
        }

        TestContext.Out.WriteLine($"{TestContext.CurrentContext.Test.Name}: {runs} runs; the human's tightest clearances:{Environment.NewLine}{tightest}");
        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(30)));
            Assert.That(tightest.Within(JitterSlack), Is.Empty, "the human's clearances");
        });
    }

    private static List<string> Play(PartyRole seat, bool inFirst, Walk walk, float delay, int seed, Clearances tightest)
    {
        var guide = new Guide(seat, delay, walk, seed);
        var measure = tightest.For(seat, $"seed {seed}, {seat}");
        ScenarioRun run;
        AoeQuery.Evaluated += measure;
        try
        {
            run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, seed, new ScenarioRunOptions
            {
                PlayerRole = seat,
                UserActionsEnabled = true,
                Overrides = o => Pin((TopP6GauntletStateOverrides)o, inFirst),
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
        if (guide.ReadInFirst != inFirst)
            problems.Add($"the human read {(guide.ReadInFirst is { } read ? read ? "in first" : "out first" : "nothing")} off the first set's lines");
        if (run.Passed && guide.Walks < guide.Legs)
            problems.Add($"the human set off on {guide.Walks} of its {guide.Legs} walks");
        if (seat == MainTank && !guide.Pressed)
            problems.Add("the human main tank never pressed its limit break 3");
        if (seat.IsTank() && !guide.Mitigated)
            problems.Add("the human tank never pressed its own set for its buster");
        return problems.Select(problem => $"seed {seed}, {seat} ({(inFirst ? "in" : "out")} first, {walk}, {delay:F1}s late): {problem}").ToList();
    }

    // Each cue queues a walk that sets off a reaction time later, aimed when it sets off; a walk that sets off before
    // the last one arrived turns from where the human stands.
    private sealed class Guide(PartyRole seat, float late, Walk walk, int seed)
    {
        public readonly List<string> Problems = [];
        public int Legs { get; private set; }
        public int Walks { get; private set; }
        public bool? ReadInFirst { get; private set; }
        public bool Pressed { get; private set; }
        public bool Mitigated { get; private set; }

        private readonly Random rng = new(seed);
        private readonly Queue<(float Depart, Func<Vector2> To)> legs = new();
        private (float Depart, Vector2[] Points, float Speed)? walking;
        private int rowsRead, waves, firstSetCasts;
        private float? firstSetCastAt, pressAt, mitigateAt;
        private float lastWave = float.NegativeInfinity;
        private bool started, diveCastEnded, diveHit;

        public void Tick(ScenarioProbe p)
        {
            var me = p.Member(seat)!;
            if (!me.IsAlive()) return;
            if (!started)
            {
                started = true;
                Queue(p.Time, () => AutoSpot(seat));
            }
            ReadTheTimeline(p);
            if (ReadInFirst is null && firstSetCastAt is { } cast && p.Time >= cast + LinesShowAfter)
                ReadInFirst = firstSetCasts == 2;
            if (pressAt is { } press && p.Time >= press)
            {
                pressAt = null;
                Pressed = true;
                TopP6GauntletCosmoMemoryScenarioTests.PressLimitBreak3(p.World, seat);
            }
            if (mitigateAt is { } mitigate && p.Time >= mitigate)
            {
                mitigateAt = null;
                Mitigated = true;
                TopP6HumanMitigation.PressTheSeatsLpduSetNow(p.World, seat, TopP6TankHit.CosmoDive1);
            }
            Advance(p, me);
        }

        private void ReadTheTimeline(ScenarioProbe p)
        {
            var rows = p.Fake.Timeline.Rows;
            for (; rowsRead < rows.Count; rowsRead++)
            {
                var row = rows[rowsRead];
                var cast = row["kind"] == "cast";
                if (!cast && row["kind"] != "ability") continue;
                if (!uint.TryParse(row["id"], CultureInfo.InvariantCulture, out var id)) continue;
                switch (id)
                {
                    case ActionId.CosmoMemory when cast && TopP6GauntletRun.TheHumanPresses(p, TopP6LimitBreakDuty.CosmoMemory):
                        pressAt = p.Time + LimitBreakIntoTheCast + late;
                        break;
                    case ActionId.CosmoArrow when cast:
                        Queue(p.Time, () => WaymarkTwoStack);
                        break;
                    case ActionId.CosmoArrowOmen when cast:
                        firstSetCastAt ??= p.Time;
                        if (p.Time < firstSetCastAt + 1f) firstSetCasts++;
                        break;
                    case ActionId.CosmoArrowOmen or ActionId.CosmoArrowDamage when !cast && p.Time - lastWave > 1f:
                        lastWave = p.Time;
                        OnTheWave(p.Time, waves++);
                        break;
                    case ActionId.CosmoDive when !cast && !diveCastEnded:
                        diveCastEnded = true;
                        if (!seat.IsTank()) Queue(p.Time, () => WaymarkTwoStack);
                        else mitigateAt = p.Time + late;
                        break;
                    case ActionId.CosmoDive_7BA7 when !cast && !diveHit:
                        diveHit = true;
                        Queue(p.Time, () => AutoSpot(seat));
                        break;
                }
            }
        }

        // Wave 0 is the first set (T), wave k the lines going off 2k s after it.
        private void OnTheWave(float now, int wave)
        {
            if (ReadInFirst is not { } inFirst)
            {
                Problems.Add($"wave {wave} went off at {now:F2} before the human could read the pattern");
                return;
            }
            if (wave == 0) Queue(now, () => inFirst ? CentreSquare : Corner);
            if (wave == 2) Queue(now, () => WaymarkTwoStack);
            if (wave == (inFirst ? 4 : 3)) Queue(now, () => SideStep);
            if (wave == (inFirst ? 5 : 4)) Queue(now, () => seat.IsTank() ? DiveSpot(seat) : InStep);
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
                walking = (p.Time, [from, .. TopP6HumanWalk.Points(walk, from, next.To(), rng)], TopP6HumanWalk.Speed(walk));
            }
            if (walking is not { } w) return;
            var (at, heading, arrived) = TopP6HumanWalk.Along(w.Points, (p.Time - w.Depart) * w.Speed);
            me.SetPosition(new Placement(new Vector3(at.X, 0f, at.Y), MathF.Atan2(heading.X, heading.Y)));
            if (arrived) walking = null;
        }
    }

    // How close the human came to each hit it dodged, or how deep it stood in the stack it had to share, as each
    // resolved, over every run of a case: the scenario's own areas, read off AoeQuery.
    private sealed class Clearances
    {
        private readonly Dictionary<string, (float Clearance, string Where)> tightest = new();
        private readonly HashSet<(float, uint)> ranked = [];
        private ScenarioProbe? probe;
        private string playing = "";

        public void Watch(ScenarioProbe p) => probe = p;

        public Action<AoeQuery> For(PartyRole seat, string run)
        {
            probe = null;
            ranked.Clear();
            playing = run;
            return hit =>
            {
                if (probe is not { } p || p.Member(seat) is not { } me || !me.IsAlive()) return;
                var on = Vector2.Distance(Flat(hit.Source.Position), Flat(me.Position)) < 0.01f;
                var clearance = hit.SignedDistance(me.Position) ?? float.NaN;
                float From(PartyRole role) => Flat(p.Member(role)!.Position).Length();
                var others = PerRole.All.Where(r => r != seat && p.Member(r).IsAlive()).ToList();
                switch (hit.ActionId)
                {
                    case ActionId.CosmoArrowOmen or ActionId.CosmoArrowDamage:
                        Note("the exasquare lines", clearance);
                        break;
                    case ActionId.Unknown7ddf when !on:
                        Note("another's Flash Gale", clearance);
                        break;
                    case ActionId.Unknown7ddf when seat == OffTank && ranked.Add((p.Time, hit.ActionId)):
                        Note("the off-tank the farthest", From(seat) - others.Max(From));
                        break;
                    case ActionId.CosmoDive_7BA7 when !on:
                        Note("another's buster", clearance);
                        if (!seat.IsTank() && ranked.Add((p.Time, hit.ActionId)))
                            Note("a non-tank farther from Alpha Omega than both tanks", From(seat) - others.Where(r => r.IsTank()).Max(From));
                        break;
                    case ActionId.CosmoDive_7BA7 when ranked.Add((p.Time, hit.ActionId)):
                        Note("a tank closer to Alpha Omega than every non-tank", others.Where(r => !r.IsTank()).Min(From) - From(seat));
                        break;
                    case ActionId.CosmoDive_7BA8 when seat.IsTank():
                        Note("outside the stack", clearance);
                        break;
                    case ActionId.CosmoDive_7BA8:
                        Note("inside the stack", -clearance);
                        break;
                }
            };
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
