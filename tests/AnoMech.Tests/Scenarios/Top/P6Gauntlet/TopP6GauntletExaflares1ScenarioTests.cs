using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top;
using AnoMech.Scenarios.Top.P6Gauntlet;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Tests.NegativeRun;
using static AnoMech.Tests.TopP6GauntletExaflares1LpduAiTests;

namespace AnoMech.Tests;

// The Gauntlet's Exaflares 1 part on its own, from t 0 (Gauntlet time - 48.6). Unless a test says otherwise the first
// exaflare starts east and the others follow clockwise (SE, S, SW), so the party starts NE at (9.9, -9.9) and follows E,
// SE and S; the lasers go to MT OT H1 H2, then M1 M2 R1 R2; the Wild Charge goes toward the MT; the exaflares step every
// 1.1 s (x east, z south).
//
//    1.9  Unlimited Wave Cannon (5 s); the exaflares' 12 s casts at 1.9 / 2.9 / 3.9 / 4.9, their arrows shown 4.5 s in.
//   11.9 / 13.7 / 15.6 / 17.5 / 19.4 / 21.4   a puddle (r 6) under everyone, each hitting 2.7 s later.
//   13.6 + k   exaflare k's first explosion (r 8) on the rim, then one 8 y further every 1.1 s.
//   24.5  Wave Cannon (10.9 s). 27.5 / 29.5: lasers (8 y wide) from Alpha Omega at four players, then at the other four.
//   35.8  Wild Charge (8 y wide) toward one player; a human tank among the two at its front needs its big cooldowns while
//         "Require tank mitigation" is on. 39.9 / 43.1: the autos, Flash Gale 0.9 s after each.
public class TopP6GauntletExaflares1ScenarioTests
{
    private const int East = 2;
    private static readonly PartyRole[] SupportsFirst = [MainTank, OffTank, RegenHealer, ShieldHealer, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps];

    private static NegativeRun<TopP6GauntletScenario> Exaflares1(PartyRole player, PartyRole wildChargeTarget = MainTank)
        => Negative<TopP6GauntletScenario>(player)
            .Overrides<TopP6GauntletStateOverrides>(o => PinAll(o, wildChargeTarget));

    // ---- Unlimited Wave Cannon.

    // The east rim, where the first exaflare goes off on its 24 y point.
    [Test]
    public void StandingWhereTheFirstExaflareGoesOffKills()
        => Exaflares1(MainTank)
            .TeleportAt(12f, to: new(18f, 0f))
            .ShouldKill(ActionId.WaveCannon_7BAD, MainTank);

    // East at r 14, 2 y from where the first exaflare's next explosion (16 y out) lands at 15.0. MT's own puddles
    // dropped far from it.
    [Test]
    public void StandingWhereALaterExplosionGoesOffKills()
        => Exaflares1(MainTank)
            .TeleportAt(14.5f, to: new(14f, 0f))
            .ShouldKill(ActionId.WaveCannon_7BAE, MainTank);

    [Test]
    public void StayingOnTheFirstPuddleKills()
        => Exaflares1(MainTank)
            .FreezeAt(12f)
            .ShouldKill(ActionId.WaveCannon_7BAF, MainTank);

    // The third set drops at the start as the party arrives; MT stays there when the others move on.
    [Test]
    public void StayingOnTheStartAfterTheThirdPuddleKills()
        => Exaflares1(MainTank)
            .FreezeAt(16.1f)
            .ShouldKill(ActionId.WaveCannon_7BAF, MainTank);

    // Waiting for the spread on the last puddles' spot, as the party stands when the sixth set drops.
    [Test]
    public void StayingOnTheLastPuddleKills()
        => Exaflares1(MainTank)
            .FreezeAt(21.5f)
            .ShouldKill(ActionId.WaveCannon_7BAF, MainTank);

    // ---- Wave Cannon.

    // MT stands in OT's line: each laser of the first set hits both, so the second kills both.
    [Test]
    public void TwoLasersOfOneSetKill()
        => Exaflares1(MainTank)
            .TeleportAt(27f, to: new(5f, 0f))
            .ShouldKill(ActionId.WaveCannonProtean, MainTank, OffTank);

    // MT stands in R2's line 8 y out, nearer Alpha Omega and clear of OT's: MT's own laser hits R2 too, and R2's, two
    // seconds later, hits MT again; both still carry the first hit's vulnerability.
    [Test]
    public void ALaserTargetInTheNextSetsLineDies()
        => Exaflares1(MainTank)
            .TeleportAt(27f, to: new(5.66f, -5.66f))
            .ShouldKill(ActionId.WaveCannonProtean, MainTank, CasterDps);

    [Test]
    public void LeavingTheWildChargeLineKillsEveryoneInIt()
        => Exaflares1(RegenHealer)
            .TeleportAt(34f, to: new(-10.5f, 0f))
            .ShouldKill(ActionId.WaveCannonWildCharge, AllBut(RegenHealer));

    [Test]
    public void ANonTankInFrontOfTheTanksDies()
        => Exaflares1(RegenHealer)
            .TeleportAt(34f, to: new(0f, 4f))
            .ShouldKill(ActionId.WaveCannonWildCharge, RegenHealer);

    // The line goes toward its target wherever it stands: MT alone in the east takes it alone.
    [Test]
    public void TheWildChargeFollowsItsTarget()
        => Exaflares1(MainTank)
            .TeleportAt(34f, to: new(10.5f, 0f))
            .ShouldKill(ActionId.WaveCannonWildCharge, MainTank);

    // At the front of the line but under no cooldown of its own: a human tank there needs its big cooldowns.
    [TestCase(MainTank)]
    [TestCase(OffTank)]
    public void AHumanTankWithoutItsBigCooldownsDiesAtTheFront(PartyRole tank)
        => Exaflares1(tank)
            .WithMitigationChecks()
            .TeleportAt(34f, to: new(0f, 6.5f))
            .ShouldKill(ActionId.WaveCannonWildCharge, tank);

    // OT drops behind the party, so a non-tank is second along the line.
    [Test]
    public void ATankBehindThePartyLeavesANonTankInFront()
    {
        var run = Run(OffTank, MainTank, stopAt: 38f, new Takeover(34f, new Vector2(0f, 13f)));

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(run.Deaths, Has.Count.EqualTo(1), run.ToString);
        Assert.That(run.Deaths[0].ActionId, Is.EqualTo(ActionId.WaveCannonWildCharge), run.ToString);
        Assert.That(run.Deaths[0].Role.IsTank(), Is.False, run.ToString);
    }

    // ---- The autos.

    [Test]
    public void ANonTankFarthestFromAlphaOmegaTakesFlashGale()
        => Exaflares1(RegenHealer)
            .TeleportAt(40f, to: new(0f, 16f))
            .ShouldKill(ActionId.Unknown7ddf, RegenHealer);

    [Test]
    public void StandingBesideTheMainTankForTheAutosKills()
        => Exaflares1(RegenHealer)
            .TeleportAt(40f, to: new(1f, -5f))
            .ShouldKill(ActionId.Unknown7ddf, RegenHealer);

    [Test]
    public void LeavingTheArenaKills()
        => Exaflares1(MainTank)
            .TeleportAt(3f, to: new(0f, 21f))
            .ShouldKill(TheEnvironment, MainTank);

    [TestCase(12f, 18f, 0f, MainTank, "exaflare")]
    [TestCase(11.95f, 0f, 0f, MainTank, "puddle")]
    [TestCase(27f, 5f, 0f, MainTank, "two Wave Cannon lasers")]
    [TestCase(34f, -10.5f, 0f, RegenHealer, "needs all eight players")]
    [TestCase(34f, 0f, 4f, RegenHealer, "tank buster")]
    [TestCase(40f, 0f, 16f, RegenHealer, "non-tank")]
    public void EveryDeathSaysWhy(float at, float x, float z, PartyRole player, string because)
    {
        var run = Run(player, MainTank, stopAt: null, new Takeover(at, new Vector2(x, z)));

        Assert.That(run.Deaths, Is.Not.Empty, run.ToString);
        Assert.That(run.Deaths[0].Cause, Does.Contain(because), run.ToString);
    }

    [Test]
    public void FollowsTheFightsTimeline()
    {
        SimTimeline? timeline = null;
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            Overrides = o => PinAll((TopP6GauntletStateOverrides)o, MainTank),
            Probe = p => timeline ??= p.Fake.Timeline,
        });

        Assert.That(run.Passed, run.ToString);
        var rows = timeline!.Rows;
        Assert.Multiple(() =>
        {
            Assert.That(run.Warnings, Has.None.Contains("dropped"), "every helper cast found a free helper");
            Assert.That(Times(rows, "cast", ActionId.UnlimitedWaveCannon), Is.EqualTo(new[] { 1.9f }).Within(0.05f), "Unlimited Wave Cannon");
            Assert.That(Times(rows, "cast", ActionId.WaveCannon_7BAD), Is.EqualTo(new[] { 1.9f, 2.9f, 3.9f, 4.9f }).Within(0.05f), "the exaflares' casts");
            Assert.That(Times(rows, "ability", ActionId.WaveCannon_7BAD), Is.EqualTo(new[] { 13.9f, 14.9f, 15.9f, 16.9f }).Within(0.05f), "their first explosions");
            Assert.That(Times(rows, "ability", ActionId.WaveCannon_7BAE).Max(), Is.EqualTo(16.9f + 6 * 1.1f).Within(0.05f), "the last explosion");
            Assert.That(Times(rows, "cast", ActionId.WaveCannon_7BAF),
                        Is.EqualTo(Eight(11.9f, 13.7f, 15.6f, 17.5f, 19.4f, 21.4f)).Within(0.05f), "the puddles");
            Assert.That(Times(rows, "ability", ActionId.WaveCannon_7BAF),
                        Is.EqualTo(Eight(14.9f, 16.7f, 18.6f, 20.5f, 22.4f, 24.4f)).Within(0.05f), "the puddles going off");
            Assert.That(Times(rows, "cast", ActionId.WaveCannon_7BA9), Is.EqualTo(new[] { 24.5f }).Within(0.05f), "Wave Cannon");
            Assert.That(Times(rows, "ability", ActionId.WaveCannonProtean), Is.EqualTo(Four(27.5f, 29.5f)).Within(0.05f), "the lasers");
            Assert.That(Times(rows, "ability", ActionId.WaveCannonWildCharge), Is.EqualTo(new[] { 35.8f }).Within(0.05f), "Wild Charge");
            Assert.That(Times(rows, "ability", ActionId.AlphaOmegaAutoAttack), Is.EqualTo(new[] { 39.9f, 43.1f }).Within(0.05f), "the autos");
            Assert.That(Times(rows, "ability", ActionId.Unknown7ddf), Is.EqualTo(new[] { 40.8f, 40.8f, 44.0f, 44.0f }).Within(0.05f), "Flash Gale");
            Assert.That(rows.Where(r => r["kind"] is "ability" or "cast").Select(Time).Max(), Is.EqualTo(44.0f).Within(0.05f),
                        "nothing is cast after the second auto");
        });
    }

    // Each exaflare's cast stands on its rolled compass point, 24 y out, facing the centre.
    [Test]
    public void TheExaflaresStartWhereTheRollSays([Range(0, 7)] int first, [Values] bool clockwise)
    {
        SimTimeline? timeline = null;
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            StopAt = 6f,
            Overrides = o => Pin((TopP6GauntletStateOverrides)o, first, clockwise),
            Probe = p => timeline ??= p.Fake.Timeline,
        });

        Assert.That(run.Failure, Is.Null, run.ToString);
        var casts = timeline!.Rows.Where(r => r["kind"] == "cast" && r["id"] == $"{ActionId.WaveCannon_7BAD}").ToList();
        Assert.That(casts.Select(Local), Is.EqualTo(Enumerable.Range(0, 4).Select(k => OnThePath(first, clockwise, k) * (24f / 14f)))
                                           .Using<Vector2>((a, b) => Vector2.Distance(a, b) < 0.05f));
    }

    // Lasers 1 go to the first four of the roll, lasers 2 to the other four, and Wild Charge toward its target.
    [Test]
    public void TheLasersAndTheWildChargeFollowTheRoll()
    {
        PartyRole[] order = [CasterDps, MeleeDpsA, RegenHealer, ShieldHealer, MainTank, PhysRangedDps, OffTank, MeleeDpsB];
        SimTimeline? timeline = null;
        Dictionary<string, PartyRole>? seats = null;
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            StopAt = 37f,
            Overrides = o =>
            {
                var overrides = (TopP6GauntletStateOverrides)o;
                PinAll(overrides, PhysRangedDps);
                overrides.Lasers1 = order;
            },
            Probe = p =>
            {
                timeline ??= p.Fake.Timeline;
                seats ??= PerRole.All.ToDictionary(r => p.Member(r)!.GameObjectId.ObjectId.ToString("X8"), r => r);
            },
        });

        Assert.That(run.Passed, run.ToString);
        var lasers = timeline!.Rows.Where(r => r["kind"] == "ability" && r["id"] == $"{ActionId.WaveCannonProtean}").ToList();
        var wildCharge = timeline.Rows.Where(r => r["kind"] == "ability" && r["id"] == $"{ActionId.WaveCannonWildCharge}").ToList();
        Assert.Multiple(() =>
        {
            Assert.That(lasers.Where(r => Time(r) < 28.5f).Select(r => seats![r["target"]]), Is.EquivalentTo(order[..4]), "the first lasers");
            Assert.That(lasers.Where(r => Time(r) > 28.5f).Select(r => seats![r["target"]]), Is.EquivalentTo(order[4..]), "the second lasers");
            Assert.That(wildCharge.Select(r => seats![r["target"]]), Is.EqualTo(new[] { PhysRangedDps }), "Wild Charge");
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void EveryoneOnTheStratsSpotsSurvives(int roll)
    {
        (int First, bool Clockwise, PartyRole Target)[] rolls = [(East, true, MainTank), (7, false, PhysRangedDps), (4, true, OffTank)];
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            Overrides = o =>
            {
                var overrides = (TopP6GauntletStateOverrides)o;
                Pin(overrides, rolls[roll].First, rolls[roll].Clockwise);
                overrides.WildCharge1Target = rolls[roll].Target;
            },
        });

        Assert.That(run.Passed, run.ToString);
    }

    private static ScenarioRun Run(PartyRole player, PartyRole wildChargeTarget, float? stopAt, params Takeover[] takeovers)
        => ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = player,
            StopAt = stopAt,
            WriteArtifactsOnFailure = false,
            Overrides = o => PinAll((TopP6GauntletStateOverrides)o, wildChargeTarget),
            Takeovers = takeovers,
        });

    private static void PinAll(TopP6GauntletStateOverrides o, PartyRole wildChargeTarget)
    {
        Pin(o, East, clockwise: true);
        o.Lasers1 = SupportsFirst;
        o.WildCharge1Target = wildChargeTarget;
    }

    private static float[] Times(IEnumerable<Dictionary<string, string>> rows, string kind, uint actionId)
        => rows.Where(r => r["kind"] == kind && r["id"] == $"{actionId}").Select(Time).Order().ToArray();

    private static float[] Four(params float[] times) => times.SelectMany(t => Enumerable.Repeat(t, 4)).ToArray();

    private static float[] Eight(params float[] times) => times.SelectMany(t => Enumerable.Repeat(t, 8)).ToArray();

    private static float Time(Dictionary<string, string> row) => Float(row["t"]);

    private static Vector2 Local(Dictionary<string, string> row)
        => new Vector2(Float(row["x"]), Float(row["z"])) - new Vector2(TopZone.Instance.Origin.X, TopZone.Instance.Origin.Z);

    private static float Float(string value) => float.Parse(value, CultureInfo.InvariantCulture);
}
