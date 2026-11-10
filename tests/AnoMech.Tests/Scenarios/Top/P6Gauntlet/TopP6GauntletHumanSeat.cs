using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top;
using AnoMech.Scenarios.Top.P6Gauntlet;
using static AnoMech.Scenarios.Top.P6Gauntlet.TopP6LimitBreakDuty;

namespace AnoMech.Tests;

// One limit break the human presses: its job's at Level, at Gauntlet time At, aimed at Alpha Omega or at nothing, cut off
// InterruptedAfter into its cast, and, if ground-targeted, put down at GroundPoint or else on its target.
internal sealed record TopP6GauntletHumanPress(float At, int Level = 3, bool OnAlphaOmega = false, float? InterruptedAfter = null,
    Vector3? GroundPoint = null)
{
    // Where the strat's bots press each duty, the plan's "aim for" times.
    public static float AimFor(TopP6LimitBreakDuty duty) => duty switch
    {
        CosmoMemory => 4.9f,
        FirstDiveMelee => 150.2f,
        SecondDiveMelee => 156.9f,
        Comets => 178.3f,
        Meteors => 188.8f,
        MagicNumber1Tank => 202.5f,
        MagicNumber1Healer => 208.8f,
        MagicNumber2Tank => 218.6f,
        MagicNumber2Healer => 224.9f,
        _ => 233.0f,
    };

    public static bool AimedAtAlphaOmega(TopP6LimitBreakDuty duty) => duty is FirstDiveMelee or SecondDiveMelee or Comets or Meteors or Enrage;

    // The duty's limit break 3 at its time, or `at`.
    public static TopP6GauntletHumanPress For(TopP6LimitBreakDuty duty, float? at = null, float? interruptedAfter = null)
        => new(at ?? AimFor(duty), OnAlphaOmega: AimedAtAlphaOmega(duty), InterruptedAfter: interruptedAfter);
}

// The human in one seat of the Gauntlet with its own job: the debug driver walks it with the bots, and it presses only
// the limit breaks a test gives it, each as its client plays it (TopP6HumanLimitBreak). Times are on the Gauntlet's clock,
// G = 0 1.9 s before Cosmo Memory's cast; the run plays First to Last.
internal sealed record TopP6GauntletHumanSeat(PartyRole Seat, JobId Job)
{
    public TopP6GauntletPart First { get; init; } = TopP6GauntletPart.CosmoMemory;
    public TopP6GauntletPart Last { get; init; } = TopP6GauntletPart.MagicNumber;
    public TopP6TankLimitBreaks? Tank { get; init; }
    public TopP6MeleeLimitBreaks? Melee { get; init; }
    public TopP6HealerLimitBreaks? Healer { get; init; }
    public bool GodMode { get; init; }
    public int Seed { get; init; } = 1;

    public TopP6GauntletHumanSeat From(TopP6GauntletPart first, TopP6GauntletPart? last = null)
        => this with { First = first, Last = last ?? first };

    // Each duty's limit break 3 at the time the strat's bots press theirs.
    public TopP6GauntletHumanSeatRun Pressing(IEnumerable<TopP6LimitBreakDuty> duties)
        => Play(duties.Select(duty => TopP6GauntletHumanPress.For(duty)).ToArray());

    public TopP6GauntletHumanSeatRun Play(params TopP6GauntletHumanPress[] presses)
    {
        TopP6GauntletLimitBreakPlan? plan = null;
        IReadOnlyList<TopP6LimitBreakPress> log = [];
        var chat = new List<(float G, string Line)>();
        var seen = 0;
        var start = TopP6GauntletParts.GauntletStart(First);
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), 0, Seed, new ScenarioRunOptions
        {
            PlayerRole = Seat,
            GodMode = GodMode,
            UserActionsEnabled = true,
            WriteArtifactsOnFailure = false,
            Overrides = o =>
            {
                ((FakeBattleCharas)Natives.BattleCharas).Player.ClassJob = (byte)Job;
                var overrides = (TopP6GauntletStateOverrides)o;
                overrides.Parts = (First, Last);
                overrides.TankLimitBreaks = Tank;
                overrides.MeleeLimitBreaks = Melee;
                overrides.HealerLimitBreaks = Healer;
                overrides.PlayerPressesItsOwnLimitBreaks = true;
            },
            Probe = p =>
            {
                if (plan is null)
                {
                    plan = TopP6GauntletRun.State(p).LimitBreakPlan;
                    foreach (var press in presses) Schedule(p, press, start);
                }
                log = TopP6GauntletRun.LimitBreaks(p).Presses;
                for (; seen < p.Chat.Count; seen++) chat.Add((p.Time + start, p.Chat[seen]));
            },
        });
        return new TopP6GauntletHumanSeatRun(this, run, plan!, log, chat);
    }

    private void Schedule(ScenarioProbe p, TopP6GauntletHumanPress press, float start)
    {
        var actionId = LimitBreakHandler.ActionId((uint)Job, press.Level);
        Func<SimCharacter?>? target = press.OnAlphaOmega ? () => TopP6GauntletRun.Scenario(p).AlphaOmega?.Enemy : null;
        p.World.Events.FromRunStart(() =>
        {
            TopP6HumanLimitBreak.Press(p.World, press.At - start, actionId, target, press.InterruptedAfter, press.GroundPoint);
            return true;
        });
    }
}

internal sealed record TopP6GauntletHumanSeatRun(
    TopP6GauntletHumanSeat Seat, ScenarioRun Run, TopP6GauntletLimitBreakPlan Plan, IReadOnlyList<TopP6LimitBreakPress> Presses,
    IReadOnlyList<(float G, string Line)> Chat)
{
    private const float Within = 0.05f;

    public float G(float runTime) => runTime + TopP6GauntletParts.GauntletStart(Seat.First);

    public IEnumerable<TopP6LimitBreakPress> TheHumans => Presses.Where(p => p.ByHuman);

    // Who the chat reported dying, each report with its time: in godmode, the only record of a death.
    public IReadOnlyList<(float G, string Who, string Cause)> Reported
        => Chat.Where(c => c.Line.Contains(" died: "))
               .Select(c => (c.G, Who: c.Line[..c.Line.IndexOf(" died: ")], Cause: c.Line[(c.Line.IndexOf(" died: ") + 7)..]))
               .ToList();

    public void AssertClears()
        => Assert.That(Run.Passed, () => $"{Run}{Environment.NewLine}presses: {Describe(Presses)}");

    // The plan gives the human `duties`, and the run clears with every duty it plays pressed once, in order, by the plan's
    // seat: the human's own by the human, as its job's limit break 3, the rest by the run.
    public void AssertEveryDutyPressedOnceByItsSeat(IEnumerable<TopP6LimitBreakDuty> duties)
    {
        AssertClears();
        var played = Enum.GetValues<TopP6LimitBreakDuty>().Where(d => new TopP6GauntletParts(Seat.First, Seat.Last).Plays(PartOf(d))).ToList();
        var limitBreak3 = LimitBreakHandler.ActionId((uint)Seat.Job, 3);
        Assert.Multiple(() =>
        {
            Assert.That(Plan.DutiesOf(Seat.Seat), Is.EqualTo(duties), "the human's duties");
            Assert.That(Presses.Select(p => (p.Seat, p.ByHuman)), Is.EqualTo(played.Select(d => (Plan.SeatOf(d), Plan.HumanPresses(d)))),
                        () => $"who pressed, in order: {Describe(Presses)}");
            Assert.That(Presses.Select(p => (p.Level, p.ResolvedAt is not null)), Has.All.EqualTo((3, true)), () => Describe(Presses));
            Assert.That(TheHumans.Select(p => p.ActionId), Has.All.EqualTo(limitBreak3), "the human's own limit break 3");
        });
    }

    // Each hit reports `players` deaths for `cause`, each player once, and nothing else is reported.
    public void AssertReportedOncePerHit(params (float At, int Players, string Cause)[] hits)
    {
        var reported = Reported;
        var why = () => string.Join(Environment.NewLine, reported.Select(r => $"{r.G:F2} {r.Who}: {r.Cause}"));
        Assert.Multiple(() =>
        {
            foreach (var (at, players, cause) in hits)
            {
                var atHit = reported.Where(r => MathF.Abs(r.G - at) < Within).ToList();
                Assert.That(atHit.Select(r => r.Cause).ToList(), Has.Count.EqualTo(players).And.All.EqualTo(cause), why);
                Assert.That(atHit.Select(r => r.Who).Distinct().Count(), Is.EqualTo(players), why);
            }
            Assert.That(reported, Has.Count.EqualTo(hits.Sum(h => h.Players)), why);
        });
    }

    public static TopP6GauntletPart PartOf(TopP6LimitBreakDuty duty) => duty switch
    {
        CosmoMemory => TopP6GauntletPart.CosmoMemory,
        FirstDiveMelee or SecondDiveMelee => TopP6GauntletPart.Exaflares2,
        Comets or Meteors => TopP6GauntletPart.CosmoMeteor,
        _ => TopP6GauntletPart.MagicNumber,
    };

    // Everyone, the human included, killed at G `at` by `actionId` for `cause`, and nobody earlier.
    public void AssertEveryoneDied(float at, uint actionId, string cause)
    {
        var why = () => $"{Run}{Environment.NewLine}presses: {Describe(Presses)}";
        Assert.Multiple(() =>
        {
            Assert.That(Run.Deaths.Select(d => d.Role), Is.EquivalentTo(PerRole.All), why);
            Assert.That(Run.Deaths.Select(d => d.ActionId), Has.All.EqualTo(actionId), why);
            Assert.That(Run.Deaths.Select(d => d.Cause), Has.All.EqualTo(cause), why);
            Assert.That(Run.Deaths.Select(d => G(d.Time)), Has.All.EqualTo(at).Within(Within), why);
        });
    }

    // Only the human, at G `at`, for `cause`: the Gauntlet's own rule for its melee limit breaks.
    public void AssertOnlyTheHumanDied(float at, string cause)
    {
        var why = () => $"{Run}{Environment.NewLine}presses: {Describe(Presses)}";
        Assert.That(Run.Deaths.Select(d => (d.Role, d.Cause)), Is.EqualTo(new[] { (Seat.Seat, cause) }), why);
        Assert.That(G(Run.Deaths[0].Time), Is.EqualTo(at).Within(Within), why);
    }

    // The human first, at G `at`, for `cause`, by the enrage's own rule; then, Alpha Omega still standing, everyone else
    // as Run: ****mi*'s damage lands at 262.0, inside the 5 s before a death freezes the run.
    public void AssertTheHumanDiedThenRunMiKilledTheRest(float at, string cause)
    {
        var why = () => $"{Run}{Environment.NewLine}presses: {Describe(Presses)}";
        var runMi = $"Run: ****mi* ({TopP6AlphaOmega.RunMiLanded})";
        Assert.That(Run.Deaths.Select(d => (d.Role, d.Cause)),
                    Is.EqualTo(new[] { (Seat.Seat, cause) }.Concat(PerRole.All.Where(r => r != Seat.Seat).Select(r => (r, runMi)))), why);
        Assert.That(Run.Deaths.Select(d => G(d.Time)), Is.EqualTo(new[] { at }.Concat(Enumerable.Repeat(262.0f, 7))).Within(Within), why);
    }

    public static string Describe(IEnumerable<TopP6LimitBreakPress> presses)
        => string.Join("; ", presses.Select(p =>
            $"{p.Seat}{(p.ByHuman ? " (human)" : "")} {p.ActionId} LB{p.Level} from {p.CastStartedAt:F2}"
            + (p.ResolvedAt is { } resolved ? $" to {resolved:F2}" : p.InterruptedAt is { } cut ? $", cut at {cut:F2}" : ", casting")));
}
