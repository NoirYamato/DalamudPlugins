using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top;
using AnoMech.Scenarios.Top.P6CosmoDive1;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Tests.NegativeRun;
using static AnoMech.Tests.TopP6CosmoDive1LpduAiTests;

namespace AnoMech.Tests;

// The bots play LPDU (TopP6CosmoDive1LpduAiTests): the autos with the MT at (0, -5), the OT at (14.5, 0) and the rest
// at (0, 6); Cosmo Arrow from (7.5, 7.5); the dive with the tanks at (-+4.6, -4.6) and the six at (7.5, 7.5).
//
//    1.9   Cosmo Memory (6 s): its bar ends at 7.6, when a human needs 80% mitigation, a tank's limit break 3, while
//          tank mitigation is required. The main tank's goes off at 4.9 and covers everyone.
//   12.9 / 16.1, 47.1 / 50.3   Flash Gale (r 5) on the MT and on the player farthest from Alpha Omega.
//   16.3   Cosmo Arrow. 24.0 the first set (in first the cross |c| < 5, out first the outer lines 10 < |c| < 20);
//          26.0 the other set; from 26.3 every 2 s the rolling lines |c - line| < 2.5:
//            In first  ±7.5 / ±7.5 ±12.5 / ±2.5 ±17.5 / ±2.5 / ±7.5 / ±12.5 / ±17.5
//            Out first ±7.5 / ±2.5 ±7.5 / ±2.5 ±12.5 / ±7.5 ±17.5 / ±12.5 / ±17.5
//   31.4   Cosmo Dive (5.6 s); 39.4 a buster (r 8) on each of the two players closest to Alpha Omega and a stack (r 6,
//          six needed) on the farthest of the rest.
public class TopP6CosmoDive1ScenarioTests
{
    private static NegativeRun<TopP6CosmoDive1Scenario> Dive1(PartyRole player, bool inFirst = true)
        => Negative<TopP6CosmoDive1Scenario>(player)
            .Overrides<TopP6CosmoDive1StateOverrides>(o => Pin(o, inFirst));

    // ---- Cosmo Memory

    [Test]
    public void AMainTankWhoForgetsItsLimitBreakDiesToCosmoMemory()
        => Dive1(MainTank)
            .WithMitigationChecks()
            .TeleportAt(0.5f, to: MainTankAutoSpot)
            .ShouldKill(ActionId.CosmoMemory, MainTank);

    [Test]
    public void AMainTankWhoPressesItsLimitBreakLives()
    {
        var run = Run(MainTank, stopAt: 10f, mitigationChecks: true,
                      probe: p => { if (p.Crossed(5.5f)) PressLimitBreak3(p.World, MainTank); },
                      takeovers: [new Takeover(0.5f, MainTankAutoSpot)]);

        Assert.That(run.Passed, run.ToString);
    }

    [TestCase(OffTank)]
    [TestCase(RegenHealer)]
    [TestCase(ShieldHealer)]
    [TestCase(MeleeDpsA)]
    [TestCase(MeleeDpsB)]
    [TestCase(PhysRangedDps)]
    [TestCase(CasterDps)]
    public void ABotMainTanksLimitBreakCarriesAHumanInAnyOtherSeat(PartyRole seat)
    {
        var run = Run(seat, stopAt: 10f, mitigationChecks: true, takeovers: [new Takeover(0.5f, AutoSpot(seat))]);

        Assert.That(run.Passed, run.ToString);
    }

    // The main tank walks out of the arena before its limit break, so nothing covers the healer.
    [Test]
    public void WithoutTheMainTanksLimitBreakCosmoMemoryKillsAHumanInAnotherSeat()
        => Dive1(RegenHealer)
            .WithMitigationChecks()
            .TeleportAt(0.5f, to: PartyAutoSpot)
            .MoveBotAt(4f, MainTank, to: new(0f, 21f))
            .ShouldKill(TheEnvironment, MainTank)
            .ShouldKill(ActionId.CosmoMemory, RegenHealer);

    [Test]
    public void NothingIsCheckedWithoutTankMitigation()
    {
        var run = Run(MainTank, stopAt: 10f, mitigationChecks: true, configure: c => c.EnableTankMitigation = false,
                      takeovers: [new Takeover(0.5f, MainTankAutoSpot)]);

        Assert.That(run.Passed, run.ToString);
    }

    // ---- The exasquares, as upstream's Exasquares/WC2 has them

    [TestCase(true)]
    [TestCase(false)]
    public void StandingInTheFirstSetKills(bool inFirst)
        => Dive1(RegenHealer, inFirst)
            .TeleportAt(20f, to: inFirst ? CentreSquare : Corner)
            .ShouldKill(ActionId.CosmoArrowOmen, RegenHealer);

    // In the second set either way, clear of the rolling lines that land with it.
    [TestCase(true)]
    [TestCase(false)]
    public void StandingInTheSecondSetKills(bool inFirst)
        => Dive1(RegenHealer, inFirst)
            .TeleportAt(24.5f, to: new(3f, 13f))
            .ShouldKill(ActionId.CosmoArrowOmen, RegenHealer);

    // Staying in front of waymark 2 instead of dodging.
    [TestCase(true)]
    [TestCase(false)]
    public void StandingInTheFirstRollingLinesKills(bool inFirst)
        => Dive1(RegenHealer, inFirst)
            .TeleportAt(24.5f, to: WaymarkTwoStack)
            .ShouldKill(ActionId.CosmoArrowDamage, RegenHealer);

    [TestCase(true)]
    [TestCase(false)]
    public void StandingInARollingLineComingBackInKills(bool inFirst)
        => Dive1(RegenHealer, inFirst)
            .TeleportAt(28.6f, to: new(2f, 8f))
            .ShouldKill(ActionId.CosmoArrowDamage, RegenHealer);

    [TestCase(true)]
    [TestCase(false)]
    public void StandingInTheLastRollingLineAtTheEdgeKills(bool inFirst)
        => Dive1(RegenHealer, inFirst)
            .TeleportAt(34.5f, to: new(17.5f, 0f))
            .ShouldKill(ActionId.CosmoArrowDamage, RegenHealer);

    // ---- The autos

    [Test]
    public void ANonTankFarthestFromAlphaOmegaDiesToFlashGale()
        => Dive1(PhysRangedDps)
            .TeleportAt(12.5f, to: new(0f, 18f))
            .ShouldKill(ActionId.Unknown7ddf, PhysRangedDps);

    [Test]
    public void StandingInTheMainTanksFlashGaleKills()
        => Dive1(MeleeDpsA)
            .TeleportAt(12.5f, to: new(2f, -5f))
            .ShouldKill(ActionId.Unknown7ddf, MeleeDpsA);

    [Test]
    public void StandingInTheOffTanksFlashGaleKills()
        => Dive1(ShieldHealer)
            .TeleportAt(15.5f, to: new(12f, 0f))
            .ShouldKill(ActionId.Unknown7ddf, ShieldHealer);

    // The off-tank, farthest at r 9, stands 4 y from the main tank: each tank is in the other's.
    [Test]
    public void TanksStandingTogetherAtTheAutosBothDie()
        => Dive1(OffTank)
            .TeleportAt(12.5f, to: new(0f, -9f))
            .ShouldKill(ActionId.Unknown7ddf, MainTank, OffTank);

    [Test]
    public void TheAutosAfterTheDiveKillToo()
        => Dive1(PhysRangedDps)
            .TeleportAt(46.5f, to: new(0f, 18f))
            .ShouldKill(ActionId.Unknown7ddf, PhysRangedDps);

    // ---- Cosmo Dive

    // Clear of both tanks' busters and of the stack, which then holds five.
    [TestCase(true)]
    [TestCase(false)]
    public void ANonTankClosestToAlphaOmegaDiesToABuster(bool inFirst)
        => Dive1(RegenHealer, inFirst)
            .TeleportAt(38.5f, to: new(-1f, 3.5f))
            .ShouldKill(ActionId.CosmoDive_7BA7, RegenHealer);

    // The off-tank closes in to 3.6 y of the main tank.
    [Test]
    public void TanksTooCloseTogetherBothDieToTheBusters()
        => Dive1(OffTank)
            .TeleportAt(38.5f, to: new(-1f, -4.6f))
            .ShouldKill(ActionId.CosmoDive_7BA7, MainTank, OffTank);

    // The off-tank, still second closest, stands 3.5 y from the stack: in it, and the stack in its buster.
    [Test]
    public void ABusterTargetInTheStackDies()
        => Dive1(OffTank)
            .TeleportAt(38.5f, to: new(7.5f, 4f))
            .ShouldKill(ActionId.CosmoDive_7BA8, OffTank);

    // The caster stands apart, neither closest nor farthest.
    [Test]
    public void AStackOfFiveKillsAllFive()
        => Dive1(CasterDps)
            .TeleportAt(38.5f, to: new(-6f, 6f))
            .ShouldKill(ActionId.CosmoDive_7BA8, RegenHealer, ShieldHealer, MeleeDpsA, MeleeDpsB, PhysRangedDps);

    // ---- The arena

    [Test]
    public void LeavingTheArenaKills()
        => Dive1(MainTank)
            .TeleportAt(2f, to: new(0f, 21f))
            .ShouldKill(TheEnvironment, MainTank);

    [TestCase(0.5f, 0f, -5f, MainTank, "limit break 3")]
    [TestCase(12.5f, 0f, 18f, PhysRangedDps, "a tank must be the farthest")]
    [TestCase(12.5f, 2f, -5f, MeleeDpsA, "aimed at someone else")]
    [TestCase(38.5f, -1f, 3.5f, RegenHealer, "two players closest to Alpha Omega")]
    [TestCase(38.5f, 7.5f, 4f, OffTank, "must stay out of the farthest one's stack")]
    [TestCase(38.5f, -6f, 6f, CasterDps, "had 5 in it")]
    public void EveryDeathSaysWhy(float at, float x, float z, PartyRole player, string because)
    {
        var run = Run(player, stopAt: null, mitigationChecks: true, takeovers: [new Takeover(at, new Vector2(x, z))]);

        Assert.That(run.Deaths, Is.Not.Empty, run.ToString);
        Assert.That(run.Deaths.Select(d => d.Cause), Has.Some.Contains(because), run.ToString);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void EveryoneOnLpdusSpotsSurvives(bool inFirst)
    {
        var run = Run(MainTank, inFirst, stopAt: null);

        Assert.That(run.Passed, run.ToString);
    }

    // ---- The fight's timeline

    [TestCase(true)]
    [TestCase(false)]
    public void FollowsTheFightsTimeline(bool inFirst)
    {
        IReadOnlyList<Dictionary<string, string>> rows = [];
        var run = Run(MainTank, inFirst, stopAt: null, probe: p => rows = p.Fake.Timeline.Rows);

        Assert.That(run.Passed, run.ToString);
        var waves = inFirst ? 7 : 6;
        Assert.Multiple(() =>
        {
            Assert.That(Times(rows, "cast", ActionId.CosmoMemory), Is.EqualTo(new[] { 1.9f }).Within(0.05f), "Cosmo Memory's cast");
            Assert.That(Times(rows, "ability", ActionId.CosmoMemory), Is.EqualTo(new[] { 7.9f }).Within(0.05f), "Cosmo Memory's hit");
            Assert.That(Times(rows, "ability", ActionId.AlphaOmegaAutoAttack), Is.EqualTo(new[] { 12.0f, 15.2f, 46.2f, 49.4f }).Within(0.05f), "the autos");
            Assert.That(Times(rows, "ability", ActionId.Unknown7ddf), Is.EqualTo(Twice(12.9f, 16.1f, 47.1f, 50.3f)).Within(0.05f), "Flash Gale");
            Assert.That(Times(rows, "cast", ActionId.CosmoArrow), Is.EqualTo(new[] { 16.3f }).Within(0.05f), "Cosmo Arrow's cast");
            Assert.That(Times(rows, "cast", ActionId.CosmoArrowOmen).Distinct(), Is.EqualTo(new[] { 16.3f, 18.31f }).Within(0.05f), "the two sets' casts");
            Assert.That(Times(rows, "ability", ActionId.CosmoArrowOmen).Distinct(), Is.EqualTo(new[] { 24.3f, 26.31f }).Within(0.05f), "the two sets");
            Assert.That(Times(rows, "ability", ActionId.CosmoArrowDamage).Distinct(),
                        Is.EqualTo(Enumerable.Range(0, waves).Select(k => 26.31f + 2f * k)).Within(0.05f), "the rolling lines");
            Assert.That(FirstSetLines(rows), Is.EqualTo(inFirst ? 2 : 4), "the first set: the cross is two casts, the outer lines four");
            Assert.That(Times(rows, "cast", ActionId.CosmoDive), Is.EqualTo(new[] { 31.4f }).Within(0.05f), "Cosmo Dive's cast");
            Assert.That(Times(rows, "ability", ActionId.CosmoDive_7BA7), Is.EqualTo(new[] { 39.4f, 39.4f }).Within(0.05f), "the busters");
            Assert.That(Times(rows, "ability", ActionId.CosmoDive_7BA8), Is.EqualTo(new[] { 39.4f }).Within(0.05f), "the stack");
            Assert.That(rows.Where(r => r["kind"] is "ability" or "cast").Select(Time).Max(), Is.EqualTo(50.3f).Within(0.05f),
                        "nothing is cast after the last auto");
            Assert.That(run.Warnings, Is.Empty, "nothing warned, so no helper cast was dropped");
        });
    }

    // The human main tank's own button, as the client's press applies it.
    internal static void PressLimitBreak3(SimWorld world, PartyRole role)
    {
        var member = world.Party.Get(role)!;
        var limitBreak3 = LimitBreakHandler.ActionId(PartyPresets.Standard[(int)role].ClassJob, 3);
        JobActions.ApplyEffects(member, limitBreak3, (ulong)member.GameObjectId, new Random(1));
    }

    private static ScenarioRun Run(PartyRole player, bool inFirst = true, float? stopAt = null, bool mitigationChecks = false,
        Action<Configuration>? configure = null, Action<ScenarioProbe>? probe = null, Takeover[]? takeovers = null)
        => ScenarioRun.Execute(typeof(TopP6CosmoDive1Scenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = player,
            StopAt = stopAt,
            WriteArtifactsOnFailure = false,
            UserActionsEnabled = mitigationChecks,
            Configure = configure,
            Overrides = o => Pin((TopP6CosmoDive1StateOverrides)o, inFirst),
            Probe = probe,
            Takeovers = takeovers ?? [],
        });

    private static int FirstSetLines(IEnumerable<Dictionary<string, string>> rows)
    {
        var casts = rows.Where(r => r["kind"] == "cast" && r["id"] == $"{ActionId.CosmoArrowOmen}").ToList();
        var first = casts.Min(Time);
        return casts.Count(r => Time(r) < first + 1f);
    }

    private static float[] Times(IEnumerable<Dictionary<string, string>> rows, string kind, uint actionId)
        => rows.Where(r => r["kind"] == kind && r["id"] == $"{actionId}").Select(Time).Order().ToArray();

    private static float[] Twice(params float[] times) => times.SelectMany(t => new[] { t, t }).ToArray();

    private static float Time(Dictionary<string, string> row) => float.Parse(row["t"], CultureInfo.InvariantCulture);
}
