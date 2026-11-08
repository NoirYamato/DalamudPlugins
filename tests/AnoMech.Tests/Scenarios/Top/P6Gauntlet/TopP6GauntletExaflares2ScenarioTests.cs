using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top;
using AnoMech.Scenarios.Top.P6Gauntlet;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Tests.NegativeRun;
using static AnoMech.Tests.TopP6GauntletExaflares2LpduAiTests;

namespace AnoMech.Tests;

// The Gauntlet's Exaflares 2 part on its own, from t 0 (Gauntlet time - 124.9). Unless a test says otherwise the first
// exaflare is pinned north, the others following clockwise, a 1.1 s step: the party starts beside it at NW, follows round
// N, NE and E (all r 14) and holds SE (9.9, 9.9) for the dive; the main tank's arm of the Y is W (-6.5, 0) and the
// off-tank's N (0, -6.5).
//
//    1.9  Unlimited Wave Cannon (5 s); the exaflares' 12 s casts at 1.9, 2.9, 3.9 and 4.9 from r 24, each going off on
//         the rim (decided 13.6 + k), then every 1.1 s 8 y further on through the centre (r 8 circles).
//   11.9 / 13.7 / 15.6 / 17.5 / 19.4 / 21.4  puddles (r 6) under everyone, each deciding 2.7 s later.
//   19.6  Cosmo Dive (5.6 s; its bar ends 24.9); 27.6 busters (r 8) on the two closest, a stack (r 6, 6 needed) on the
//         farthest.
//   34.4 / 37.6  the autos' visuals; Flash Gale (r 5) 0.9 s after each on the main tank and the farthest.
// A human melee is held to its own melee limit break 3: the first started from the bar's end to 36.1, the second once the
// first has resolved, to 40.6, each on Alpha Omega and resolved, or it dies as the window closes.
public class TopP6GauntletExaflares2ScenarioTests
{
    private static NegativeRun<TopP6GauntletScenario> ExaflaresDive(PartyRole player)
        => Negative<TopP6GauntletScenario>(player)
            .Overrides<TopP6GauntletStateOverrides>(o => Pin(o, 0, true, 1.1f));

    private static readonly Vector2 Hold = new(9.9f, 9.9f);

    // The first exaflare's first explosion, r 8 round (0, -24), reaches 6 y in from the rim.
    [Test]
    public void StandingWhereAnExaflareGoesOffOnTheRimKills()
        => ExaflaresDive(RegenHealer)
            .TeleportAt(11f, to: new(0, -18))
            .ShouldKill(ActionId.WaveCannon_7BAD, RegenHealer);

    // θ0's own point: missed by the rim explosion (10 y), hit by the next one at r 16 (15.0); its own second puddle
    // there decides only at 16.4.
    [Test]
    public void StandingWhereALaterExplosionGoesOffKills()
        => ExaflaresDive(RegenHealer)
            .TeleportAt(12f, to: new(0, -14))
            .ShouldKill(ActionId.WaveCannon_7BAE, RegenHealer);

    [Test]
    public void StayingOnTheFirstPuddlesKills()
        => ExaflaresDive(RegenHealer)
            .TeleportAt(12f, to: Vector2.Zero)
            .ShouldKill(ActionId.WaveCannon_7BAF, RegenHealer);

    // The healer 3 y from the boss toward the hold: closer than the tanks, 8.9 y from either.
    [Test]
    public void ANonTankClosestToAlphaOmegaTakesABusterAndDies()
        => ExaflaresDive(RegenHealer)
            .TeleportAt(26f, to: new(2.12f, 2.12f))
            .ShouldKill(ActionId.CosmoDive_7BA7, RegenHealer);

    // 4 y beyond the main tank on its own bearing: neither closest nor farthest, but inside the main tank's buster.
    [Test]
    public void StandingInATanksBusterKills()
        => ExaflaresDive(MeleeDpsA)
            .TeleportAt(26f, to: new(-10.5f, 0))
            .ShouldKill(ActionId.CosmoDive_7BA7, MeleeDpsA);

    // The off-tank 3 y in front of the hold is still the second closest: in the stack, and the stack in its buster.
    [Test]
    public void ABusterTargetInTheStackDiesAndTakesTheStackWithIt()
        => ExaflaresDive(OffTank)
            .TeleportAt(26f, to: new(7.78f, 7.78f))
            .ShouldKill(ActionId.CosmoDive_7BA8, OffTank)
            .ShouldKill(ActionId.CosmoDive_7BA7, TheSix);

    // The caster stands apart, neither closest nor farthest, so the stack holds five.
    [Test]
    public void AStackOfFiveKillsAllFive()
        => ExaflaresDive(CasterDps)
            .TeleportAt(26f, to: new(0, 10))
            .ShouldKill(ActionId.CosmoDive_7BA8, TheSix.Where(r => r != CasterDps).ToArray());

    [Test]
    public void ANonTankFarthestFromAlphaOmegaTakesFlashGaleAndDies()
        => ExaflaresDive(RegenHealer)
            .TeleportAt(33f, to: new(0, 17.5f))
            .ShouldKill(ActionId.Unknown7ddf, RegenHealer);

    [TestCase(MeleeDpsA, 2f, -5f)]
    [TestCase(PhysRangedDps, 12f, 0f)]
    public void StandingInATanksFlashGaleKills(PartyRole player, float x, float z)
        => ExaflaresDive(player)
            .TeleportAt(33f, to: new(x, z))
            .ShouldKill(ActionId.Unknown7ddf, player);

    // M2, the second melee, presses its limit break from the middle and stays for the autos; a main tank 4 y from it
    // catches it in its own Flash Gale.
    [Test]
    public void AMainTankThatMakesNoRoomCatchesTheSecondMeleeInItsFlashGale()
        => ExaflaresDive(MainTank)
            .TeleportAt(31f, to: new(0, -4))
            .ShouldKill(ActionId.Unknown7ddf, MeleeDpsB);

    // ---- The human's melee limit breaks 3

    private const float FirstMeleeWindowCloses = 36.1f, SecondMeleeWindowCloses = 40.6f;

    // M1's dragoon is the first melee, M2's monk the second. A human who presses nothing dies as its window closes.
    [TestCase(MeleeDpsA, FirstMeleeWindowCloses, TopP6LimitBreaks.FirstDiveMeleeRule)]
    [TestCase(MeleeDpsB, SecondMeleeWindowCloses, TopP6LimitBreaks.SecondDiveMeleeRule)]
    public void AHumanMeleeWhoNeverPressesDiesAsItsWindowCloses(PartyRole seat, float closes, string rule)
    {
        var run = HumanMelee(seat, _ => { });

        Assert.That(run.Deaths.Select(d => (d.Role, d.Cause)), Is.EqualTo(new[] { (seat, rule) }), run.ToString);
        Assert.That(run.Deaths[0].Time, Is.EqualTo(closes).Within(0.05f), run.ToString);
    }

    // Pressed on Alpha Omega at its cue, the first right after the bar, the second once the first has resolved.
    [TestCase(MeleeDpsA, 25.5f)]
    [TestCase(MeleeDpsB, 31f)]
    public void AHumanMeleeWhoPressesInItsWindowLives(PartyRole seat, float pressAt)
    {
        var run = HumanMelee(seat, p => TopP6HumanLimitBreak.Press(p.World, pressAt, TopP6GauntletRun.LimitBreak3(seat),
            () => TopP6GauntletRun.Scenario(p).AlphaOmega?.Enemy));

        Assert.That(run.Passed, run.ToString);
    }

    // The second melee may not go before the first has resolved: started at 25.0, ahead of the first's (25.3), which then
    // waits for it, it doesn't count.
    [Test]
    public void TheSecondMeleeStartingBeforeTheFirstHasResolvedDies()
    {
        var run = HumanMelee(MeleeDpsB, p => TopP6HumanLimitBreak.Press(p.World, 25f, TopP6GauntletRun.LimitBreak3(MeleeDpsB),
            () => TopP6GauntletRun.Scenario(p).AlphaOmega?.Enemy));

        Assert.That(run.Deaths.Select(d => (d.Role, d.Cause)),
                    Is.EqualTo(new[] { (MeleeDpsB, TopP6LimitBreaks.SecondDiveMeleeRule + TopP6LimitBreaks.YoursStartedTooEarly) }), run.ToString);
    }

    // The human melee stays on the AI, pressing only what the test scripts, scheduled at the run's start.
    private static ScenarioRun HumanMelee(PartyRole seat, Action<ScenarioProbe> press)
        => ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = seat,
            StopAt = SecondMeleeWindowCloses + 0.5f,
            WriteArtifactsOnFailure = false,
            Overrides = o =>
            {
                var overrides = (TopP6GauntletStateOverrides)o;
                Pin(overrides, 0, true, 1.1f);
                overrides.PlayerPressesItsOwnLimitBreaks = true;
            },
            Probe = p => { if (p.Crossed(0.1f)) press(p); },
        });

    [Test]
    public void LeavingTheArenaKills()
        => ExaflaresDive(MainTank)
            .TeleportAt(2f, to: new(0, 21))
            .ShouldKill(TheEnvironment, MainTank);

    [TestCase(11f, 0f, -18f, RegenHealer, "exaflare")]
    [TestCase(12f, 0f, 0f, RegenHealer, "Wave Cannon puddle")]
    [TestCase(26f, 2.12f, 2.12f, RegenHealer, "Cosmo Dive buster as a non-tank")]
    [TestCase(26f, -10.5f, 0f, MeleeDpsA, "Cosmo Dive buster aimed at someone else")]
    [TestCase(26f, 7.78f, 7.78f, OffTank, "Cosmo Dive buster and its stack")]
    [TestCase(26f, 0f, 10f, CasterDps, "had 5 in it")]
    [TestCase(33f, 0f, 17.5f, RegenHealer, "Flash Gale as a non-tank")]
    [TestCase(33f, 2f, -5f, MeleeDpsA, "Flash Gale aimed at someone else")]
    public void EveryDeathSaysWhy(float at, float x, float z, PartyRole player, string because)
    {
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = player,
            WriteArtifactsOnFailure = false,
            Overrides = o => Pin((TopP6GauntletStateOverrides)o, 0, true, 1.1f),
            Takeovers = [new Takeover(at, new Vector2(x, z))],
        });

        Assert.That(run.Deaths, Is.Not.Empty, run.ToString);
        Assert.That(run.Deaths.Select(d => d.Cause), Has.Some.Contains(because), run.ToString);
    }

    [TestCase(1.1f)]
    [TestCase(1.0f)]
    public void FollowsTheFightsTimeline(float step)
    {
        SimTimeline? timeline = null;
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            Overrides = o => Pin((TopP6GauntletStateOverrides)o, 5, false, step),
            Probe = p => timeline ??= p.Fake.Timeline,
        });

        Assert.That(run.Passed, run.ToString);
        var rows = timeline!.Rows;
        var wave = Wave(5, false, step);
        Assert.Multiple(() =>
        {
            Assert.That(run.Warnings, Has.None.Contains("dropped"), "every helper cast went out");
            Assert.That(Times(rows, "cast", ActionId.UnlimitedWaveCannon), Is.EqualTo(new[] { 1.9f }).Within(0.05f), "Unlimited Wave Cannon");
            Assert.That(Times(rows, "cast", ActionId.WaveCannon_7BAD), Is.EqualTo(new[] { 1.9f, 2.9f, 3.9f, 4.9f }).Within(0.05f), "the exaflares' casts");
            Assert.That(Times(rows, "ability", ActionId.WaveCannon_7BAD, ActionId.WaveCannon_7BAE),
                        Is.EqualTo(wave.Explosions.Select(e => e.Step == 0 ? e.HitAt + 0.3f : e.HitAt).Order()).Within(0.05f), "the explosions");
            Assert.That(Times(rows, "cast", ActionId.WaveCannon_7BAF),
                        Is.EqualTo(new[] { 11.9f, 13.7f, 15.6f, 17.5f, 19.4f, 21.4f }.SelectMany(t => Enumerable.Repeat(t, 8))).Within(0.05f), "puddles");
            Assert.That(Times(rows, "cast", ActionId.CosmoDive), Is.EqualTo(new[] { 19.6f }).Within(0.05f), "Cosmo Dive");
            Assert.That(Times(rows, "ability", ActionId.CosmoDive_7BA7, ActionId.CosmoDive_7BA8), Is.EqualTo(new[] { 27.6f, 27.6f, 27.6f }).Within(0.05f), "the dive's hits");
            Assert.That(Times(rows, "ability", ActionId.AlphaOmegaAutoAttack), Is.EqualTo(new[] { 34.4f, 37.6f }).Within(0.05f), "the autos");
            Assert.That(Times(rows, "ability", ActionId.Unknown7ddf), Is.EqualTo(new[] { 35.3f, 35.3f, 38.5f, 38.5f }).Within(0.05f), "Flash Gale");
            Assert.That(rows.Where(r => r["kind"] is "ability" or "cast").Select(Time).Max(), Is.EqualTo(38.5f).Within(0.05f),
                        "nothing is cast after the last Flash Gale");
        });
    }

    // The exaflares start on r 24 at the rolled point and the next three the way they turn, each facing the centre.
    [Test]
    public void TheExaflaresStartWhereTheRollSays()
    {
        var problems = new List<string>();
        foreach (var (first, clockwise) in Patterns)
        {
            SimTimeline? timeline = null;
            var run = Run(MainTank, first, clockwise, 1.1f, p => timeline ??= p.Fake.Timeline, stopAt: 5.5f);
            if (!run.Passed) problems.Add(run.ToString());
            var starts = timeline!.Rows.Where(r => r["kind"] == "cast" && r["id"] == $"{ActionId.WaveCannon_7BAD}")
                                       .Select(r => new Vector2(Float(r["x"]) - 100f, Float(r["z"]) - 100f))
                                       .ToList();
            var expected = Enumerable.Range(0, 4).Select(k => Flat(TopP6AlphaOmega.AtBearing(45f * (first + (clockwise ? k : -k)), 24f))).ToList();
            if (starts.Count != 4 || starts.Zip(expected).Any(s => Distance(s.First, s.Second) > 0.01f))
                problems.Add($"first {first}, clockwise {clockwise}: the exaflares started at {string.Join(", ", starts)}");
        }

        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems));
    }

    [TestCase(1.1f)]
    [TestCase(1.0f)]
    public void EveryoneOnTheStratsSpotsSurvives(float step)
    {
        var problems = new List<string>();
        foreach (var (first, clockwise) in new[] { (0, true), (3, false), (6, true) })
        {
            var run = Run(PerRole.All[first], first, clockwise, step, _ => { });
            if (!run.Passed) problems.Add(run.ToString());
        }

        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems));
    }

    private static float[] Times(IEnumerable<Dictionary<string, string>> rows, string kind, params uint[] actionIds)
        => rows.Where(r => r["kind"] == kind && actionIds.Any(id => r["id"] == $"{id}"))
               .Select(Time)
               .Order()
               .ToArray();

    private static float Time(Dictionary<string, string> row) => Float(row["t"]);

    private static float Float(string value) => float.Parse(value, CultureInfo.InvariantCulture);
}
