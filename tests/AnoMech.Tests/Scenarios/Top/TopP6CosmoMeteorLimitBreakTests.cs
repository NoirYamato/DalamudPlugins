using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Top;
using FFXIVClientStructs.FFXIV.Client.Game;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopP6Constants;

namespace AnoMech.Tests;

// Cosmo Meteor cast at 1.9 with the limit breaks counted: the adds spawn at 9.3 and show, targetable, at 10.1. A
// caster's limit break 3 resolving while they show kills each comet whose hitbox its circle reaches (15 round its ground
// point); a physical ranged one each meteor whose hitbox its line reaches (30 long past its user's hitbox, 8 wide, toward
// its target). Comets still alive at 22.2 enrage, and meteors at 32.3, and everyone dies. Godmode, so the run plays on
// past the deaths it reports.
public class TopP6CosmoMeteorLimitBreakTests
{
    private const float CometsEnrage = 22.2f;
    private const float MeteorsEnrage = 32.3f;

    private static readonly TopP6CosmoMeteorRolls Rolls = new(
        [[MainTank, OffTank, RegenHealer, ShieldHealer], [MainTank, OffTank, RegenHealer, ShieldHealer]],
        [CasterDps, MeleeDpsB, MainTank], ShieldHealer);

    private static uint Job(PartyRole seat) => PartyPresets.Standard[(int)seat].ClassJob;

    private static uint LimitBreak3(PartyRole seat) => LimitBreakHandler.ActionId(Job(seat), 3);

    private static string YoursCaught(int adds) => string.Format(TopP6CosmoMeteor.YoursCaught, adds);

    [SetUp]
    public void InstallGame() => FakeGame.Install();

    // The run's own goes down on its target, Alpha Omega, and its circle holds all six.
    [Test]
    public void ACasterLimitBreak3KillsEveryComet()
    {
        var alive = new Dictionary<float, (int Comets, int Meteors)>();
        (Vector3? GroundPoint, Vector3 AlphaOmega) placed = default;
        var run = CosmoMeteor((world, alphaOmega, limitBreaks, cosmoMeteor) =>
        {
            world.Events.Add(11f, () => limitBreaks.Press(CasterDps, Job(CasterDps), alphaOmega.Enemy));
            world.Events.Add(12f, () => placed = (limitBreaks.Presses.Single().GroundPoint, alphaOmega.Position));
            foreach (var at in new[] { 15.4f, 15.6f })
                world.Events.Add(at, () => alive[at] = Alive(cosmoMeteor));
        });

        Assert.That(placed.GroundPoint, Is.EqualTo(placed.AlphaOmega));
        Assert.That(alive[15.4f], Is.EqualTo((6, 2)));
        Assert.That(alive[15.6f], Is.EqualTo((0, 2)));
        Assert.That(Reported(run, TopP6CosmoMeteor.CometsEnraged), Is.Empty);
    }

    // R2's client puts the circle 8 y north of Alpha Omega: it holds the comets at 45, 90, 270 and 315; the two at 135 and
    // 225, 19.5 y from its centre, live, enrage and kill everyone, the reason saying how many it caught.
    [Test]
    public void AHumansCasterLimitBreak3KillsOnlyTheCometsItsCircleHolds()
    {
        var alive = (Comets: -1, Meteors: -1);
        Vector3? groundPoint = null;
        var run = CosmoMeteor((world, alphaOmega, limitBreaks, cosmoMeteor) =>
        {
            world.Events.Add(10.5f, () => TimelineRun.Place(world, CasterDps, 0f, -13f));
            TopP6HumanLimitBreak.Press(world, 11f, LimitBreak3(CasterDps), () => alphaOmega.Enemy, groundPoint: new Vector3(0f, 0f, -8f));
            world.Events.Add(15.6f, () =>
            {
                alive = Alive(cosmoMeteor);
                groundPoint = limitBreaks.Presses.Single().GroundPoint;
            });
        }, CasterDps);

        Assert.That(groundPoint, Is.EqualTo(new Vector3(0f, 0f, -8f)));
        Assert.That(alive, Is.EqualTo((2, 2)));
        Assert.That(Reported(run, TopP6CosmoMeteor.CometsEnraged + YoursCaught(4)), Has.Count.EqualTo(8), run.ToString);
    }

    // The circle's centre east of Alpha Omega puts the west comet (270) as far from it as the circle's radius plus the
    // comet's hitbox, give or take half a yalm: inside, the circle holds all six; outside, that one lives.
    [TestCase(-0.5f, 0)]
    [TestCase(0.5f, 1)]
    public void TheCircleCountsTheCometsHitbox(float pastTheHitbox, int cometsLeft)
    {
        var alive = (Comets: -1, Meteors: -1);
        var hitbox = -1f;
        CosmoMeteor((world, alphaOmega, _, cosmoMeteor) =>
        {
            world.Events.Add(10.5f, () =>
            {
                TimelineRun.Place(world, CasterDps, 0f, -13f);
                hitbox = cosmoMeteor.Comets[0].HitboxRadius;
                var east = Natives.Data.Action(LimitBreak3(CasterDps))!.EffectRange + hitbox - P6Geometry.CosmoCometDistance + pastTheHitbox;
                TopP6HumanLimitBreak.Press(world, 0.5f, LimitBreak3(CasterDps), () => alphaOmega.Enemy, groundPoint: new Vector3(east, 0f, 0f));
            });
            world.Events.Add(15.6f, () => alive = Alive(cosmoMeteor));
        }, CasterDps);

        Assert.That(hitbox, Is.GreaterThan(0f));
        Assert.That(alive.Comets, Is.EqualTo(cometsLeft));
    }

    // Its first circle, 8 y north of Alpha Omega, holds four comets, and Brilliant Dynamis gives the gauge back; the second,
    // 5 y east, holds the one at 135 but not the one at 225, 16.9 y from its centre. That comet enrages, and the reason
    // counts what both caught.
    [Test]
    public void TheEnragesReasonCountsWhatAllTheHumansLimitBreaksCaught()
    {
        var alive = (Comets: -1, Meteors: -1);
        var run = CosmoMeteor((world, alphaOmega, _, cosmoMeteor) =>
        {
            world.Events.Add(10.5f, () => TimelineRun.Place(world, CasterDps, 0f, -13f));
            TopP6HumanLimitBreak.Press(world, 11f, LimitBreak3(CasterDps), () => alphaOmega.Enemy, groundPoint: new Vector3(0f, 0f, -8f));
            TopP6HumanLimitBreak.Press(world, 16f, LimitBreak3(CasterDps), () => alphaOmega.Enemy, groundPoint: new Vector3(5f, 0f, 0f));
            world.Events.Add(20.6f, () => alive = Alive(cosmoMeteor));
        }, CasterDps);

        Assert.That(alive.Comets, Is.EqualTo(1));
        Assert.That(Reported(run, TopP6CosmoMeteor.CometsEnraged + YoursCaught(5)), Has.Count.EqualTo(8), run.ToString);
    }

    // The player on (0, -13) puts the circle on (0, 12.5), 25.5 y away, or on (0, 13.5), 26.5 y away. The first counts, and
    // holds only the comets at 135 and 225; the second is farther than the limit break reaches, so the client placed it
    // for no cast of its, and the circle holds every comet.
    [TestCase(12.5f, 4)]
    [TestCase(13.5f, 0)]
    public void APointBeyondTheLimitBreaksReachIsNoneTheClientPlaced(float z, int cometsLeft)
    {
        var alive = (Comets: -1, Meteors: -1);
        Vector3? groundPoint = null;
        CosmoMeteor((world, alphaOmega, limitBreaks, cosmoMeteor) =>
        {
            world.Events.Add(10.5f, () => TimelineRun.Place(world, CasterDps, 0f, -13f));
            TopP6HumanLimitBreak.Press(world, 11f, LimitBreak3(CasterDps), () => alphaOmega.Enemy, groundPoint: new Vector3(0f, 0f, z));
            world.Events.Add(15.6f, () =>
            {
                alive = Alive(cosmoMeteor);
                groundPoint = limitBreaks.Presses.Single().GroundPoint;
            });
        }, CasterDps);

        Assert.That(groundPoint, cometsLeft > 0 ? Is.EqualTo(new Vector3(0f, 0f, z)) : Is.Null);
        Assert.That(alive.Comets, Is.EqualTo(cometsLeft));
    }

    // A client that reports no ground point, or one farther from the player than the limit break reaches, leaves the circle
    // unplaced, and it holds every comet.
    [TestCase(false)]
    [TestCase(true)]
    public void ACircleItsClientDidntPlaceHoldsEveryComet(bool beyondReach)
    {
        var alive = (Comets: -1, Meteors: -1);
        Vector3? groundPoint = Vector3.One;
        var run = CosmoMeteor((world, _, limitBreaks, cosmoMeteor) =>
        {
            world.Events.Add(10.5f, () => TimelineRun.Place(world, CasterDps, 0f, -13f));
            TopP6HumanLimitBreak.Press(world, 11f, LimitBreak3(CasterDps), groundPoint: beyondReach ? new Vector3(0f, 0f, 40f) : null);
            world.Events.Add(15.6f, () =>
            {
                alive = Alive(cosmoMeteor);
                groundPoint = limitBreaks.Presses.Single().GroundPoint;
            });
        }, CasterDps);

        Assert.That(groundPoint, Is.Null);
        Assert.That(alive.Comets, Is.Zero);
        Assert.That(Reported(run, TopP6CosmoMeteor.CometsEnraged), Is.Empty, run.ToString);
    }

    // Pressed at 4.0, it resolves at 8.5, before the adds are there to hit.
    [Test]
    public void OneResolvingBeforeTheAddsShowKillsNothing()
    {
        var run = CosmoMeteor((world, alphaOmega, limitBreaks, _) => world.Events.Add(4f, () => limitBreaks.Press(CasterDps, Job(CasterDps), alphaOmega.Enemy)));

        Assert.That(Reported(run, TopP6CosmoMeteor.CometsEnraged), Has.Count.EqualTo(8), run.ToString);
    }

    [Test]
    public void LeftAliveTheAddsEnrageAndKillEveryone()
    {
        var run = CosmoMeteor((_, _, _, _) => { });
        var enrages = run.Sent("ability", P6ActionId.CosmoMeteorEnrage);

        Assert.That(Reported(run, TopP6CosmoMeteor.CometsEnraged), Has.Count.EqualTo(8), run.ToString);
        Assert.That(Reported(run, TopP6CosmoMeteor.MeteorsEnraged), Has.Count.EqualTo(8), run.ToString);
        Assert.That(enrages.Select(e => e.Time), Is.EqualTo(Enumerable.Repeat(CometsEnrage, 6).Concat(Enumerable.Repeat(MeteorsEnrage, 2))).Within(TimelineRun.FrameLate));
    }

    // From A, 13 y north, at Alpha Omega: the line runs north to south through both meteors.
    [Test]
    public void ALineThroughBothMeteorsKillsBoth()
    {
        var alive = (Comets: -1, Meteors: -1);
        var run = CosmoMeteor((world, alphaOmega, limitBreaks, cosmoMeteor) =>
        {
            world.Events.Add(10.5f, () => TimelineRun.Place(world, PhysRangedDps, 0f, -13f));
            world.Events.Add(11f, () => limitBreaks.Press(PhysRangedDps, Job(PhysRangedDps), alphaOmega.Enemy));
            world.Events.Add(15.6f, () => alive = Alive(cosmoMeteor));
        });

        Assert.That(alive, Is.EqualTo((6, 0)));
        Assert.That(Reported(run, TopP6CosmoMeteor.MeteorsEnraged), Is.Empty, run.ToString);
    }

    // From (0, 5), between the meteors, the line toward Alpha Omega runs north through the north meteor only: the south
    // one, behind R1, lives and enrages.
    [Test]
    public void ALineThroughOneMeteorKillsOnlyThatOne()
    {
        var alive = (Comets: -1, Meteors: -1);
        var run = CosmoMeteor((world, alphaOmega, limitBreaks, cosmoMeteor) =>
        {
            world.Events.Add(10.5f, () => TimelineRun.Place(world, PhysRangedDps, 0f, 5f));
            world.Events.Add(11f, () => limitBreaks.Press(PhysRangedDps, Job(PhysRangedDps), alphaOmega.Enemy));
            world.Events.Add(15.6f, () => alive = Alive(cosmoMeteor));
        });

        Assert.That(alive, Is.EqualTo((6, 1)));
        Assert.That(Reported(run, TopP6CosmoMeteor.MeteorsEnraged), Has.Count.EqualTo(8), run.ToString);
    }

    // The same line, the human's: the south meteor's enrage says how many it caught.
    [Test]
    public void AHumansLineThroughOneMeteorSaysHowManyItCaught()
    {
        var alive = (Comets: -1, Meteors: -1);
        var run = CosmoMeteor((world, alphaOmega, _, cosmoMeteor) =>
        {
            world.Events.Add(10.5f, () => TimelineRun.Place(world, PhysRangedDps, 0f, 5f));
            TopP6HumanLimitBreak.Press(world, 11f, LimitBreak3(PhysRangedDps), () => alphaOmega.Enemy);
            world.Events.Add(15.6f, () => alive = Alive(cosmoMeteor));
        }, PhysRangedDps);

        Assert.That(alive, Is.EqualTo((6, 1)));
        Assert.That(Reported(run, TopP6CosmoMeteor.MeteorsEnraged + YoursCaught(1)), Has.Count.EqualTo(8), run.ToString);
    }

    // Aimed at Alpha Omega from east of A, the line passes both meteors' centres as far off as the line is half wide (4 y)
    // plus a meteor's hitbox, give or take half a yalm: inside, it reaches both hitboxes; outside, neither.
    [TestCase(-0.5f, 0)]
    [TestCase(0.5f, 2)]
    public void TheLineCountsTheMeteorsHitbox(float pastTheHitbox, int meteorsLeft)
    {
        var alive = (Comets: -1, Meteors: -1);
        var hitbox = -1f;
        CosmoMeteor((world, alphaOmega, limitBreaks, cosmoMeteor) =>
        {
            world.Events.Add(10.5f, () =>
            {
                hitbox = cosmoMeteor.Meteors[0].HitboxRadius;
                var offset = 4f + hitbox + pastTheHitbox;
                const float meteor = P6Geometry.CosmoMeteorDistance;
                var x = 13f * offset / MathF.Sqrt(meteor * meteor - offset * offset);
                TimelineRun.Place(world, PhysRangedDps, x, -13f);
            });
            world.Events.Add(11f, () => limitBreaks.Press(PhysRangedDps, Job(PhysRangedDps), alphaOmega.Enemy));
            world.Events.Add(15.6f, () => alive = Alive(cosmoMeteor));
        });

        Assert.That(hitbox, Is.GreaterThan(1f));
        Assert.That(alive.Meteors, Is.EqualTo(meteorsLeft));
    }

    // The human's client is heard between the sim's frames: a limit break 3 whose resolve is heard in the frame its adds
    // enrage, before their enrage's own event, landed while they lived, and kills them before they can enrage. R1 presses
    // from A, at Alpha Omega.
    [TestCase(CasterDps)]
    [TestCase(PhysRangedDps)]
    public void AHumansLimitBreak3HeardInTheEnragesOwnFrameKillsTheAddsFirst(PartyRole seat)
    {
        const float CastAt = 1.9f;
        var comets = seat == CasterDps;
        var enragesAt = CastAt + (comets ? P6Duration.CometsEnrage : P6Duration.MeteorsEnrage);
        var alive = (Comets: -1, Meteors: -1);
        var run = CosmoMeteor((world, _, _, cosmoMeteor) => world.Events.Add(enragesAt + 0.1f, () => alive = Alive(cosmoMeteor)),
            seat, heardFirst: (world, alphaOmega) =>
            {
                var limitBreak3 = LimitBreakHandler.ActionId(Job(seat), 3);
                var castSeconds = Natives.Data.Action(limitBreak3)!.CastSeconds;
                ulong Target() => (ulong)alphaOmega.Enemy!.GameObjectId;
                world.Events.Add(enragesAt - castSeconds - 0.5f, () => TimelineRun.Place(world, PhysRangedDps, 0f, -13f));
                world.Events.Add(enragesAt - castSeconds, () =>
                {
                    TopP6HumanLimitBreak.PlaceOnTheGround(world, limitBreak3, alphaOmega.Position);
                    Act(PlayerActionStep.CastStarted, limitBreak3, Target(), castSeconds);
                });
                world.Events.Add(enragesAt, () => Act(PlayerActionStep.Resolved, limitBreak3, Target(), 0f));
            });

        Assert.That(Reported(run, comets ? TopP6CosmoMeteor.CometsEnraged : TopP6CosmoMeteor.MeteorsEnraged), Is.Empty, run.ToString);
        Assert.That(comets ? alive.Comets : alive.Meteors, Is.Zero);
    }

    // The same, aimed so it leaves some alive: R2's circle 8 y north of Alpha Omega holds four comets, and R1's line from
    // (0, 5) only the north meteor. Those left enrage, and the reason, read once the resolve has settled, counts what the
    // human's caught.
    [TestCase(CasterDps, 2, 4)]
    [TestCase(PhysRangedDps, 1, 1)]
    public void AHumansLimitBreak3HeardInTheEnragesOwnFrameCountsInItsReason(PartyRole seat, int left, int caught)
    {
        const float CastAt = 1.9f;
        var comets = seat == CasterDps;
        var enragesAt = CastAt + (comets ? P6Duration.CometsEnrage : P6Duration.MeteorsEnrage);
        var alive = (Comets: -1, Meteors: -1);
        var run = CosmoMeteor((world, _, _, cosmoMeteor) => world.Events.Add(enragesAt + 0.1f, () => alive = Alive(cosmoMeteor)),
            seat, heardFirst: (world, alphaOmega) =>
            {
                var limitBreak3 = LimitBreakHandler.ActionId(Job(seat), 3);
                var castSeconds = Natives.Data.Action(limitBreak3)!.CastSeconds;
                ulong Target() => (ulong)alphaOmega.Enemy!.GameObjectId;
                world.Events.Add(enragesAt - castSeconds - 0.5f, () => TimelineRun.Place(world, seat, 0f, comets ? -13f : 5f));
                world.Events.Add(enragesAt - castSeconds, () =>
                {
                    TopP6HumanLimitBreak.PlaceOnTheGround(world, limitBreak3, new Vector3(0f, 0f, -8f));
                    Act(PlayerActionStep.CastStarted, limitBreak3, Target(), castSeconds);
                });
                world.Events.Add(enragesAt, () => Act(PlayerActionStep.Resolved, limitBreak3, Target(), 0f));
            });

        Assert.That(comets ? alive.Comets : alive.Meteors, Is.EqualTo(left));
        Assert.That(Reported(run, (comets ? TopP6CosmoMeteor.CometsEnraged : TopP6CosmoMeteor.MeteorsEnraged) + YoursCaught(caught)),
                    Has.Count.EqualTo(8), run.ToString);
    }

    // The caster's press waits for a gauge left at one bar, and the enrage says so.
    [Test]
    public void AShortGaugeSaysSo()
    {
        var run = CosmoMeteor((world, alphaOmega, limitBreaks, _) =>
        {
            world.Events.Add(10.5f, () => limitBreaks.SetGauge(1f));
            world.Events.Add(11f, () => limitBreaks.Press(CasterDps, Job(CasterDps), alphaOmega.Enemy));
        });

        Assert.That(Reported(run, TopP6CosmoMeteor.CometsEnraged + TopP6LimitBreaks.GaugeWasShort), Has.Count.EqualTo(8), run.ToString);
    }

    private static (int Comets, int Meteors) Alive(TopP6CosmoMeteor cosmoMeteor)
        => (cosmoMeteor.Comets.Count(c => c.IsAlive()), cosmoMeteor.Meteors.Count(m => m.IsAlive()));

    private static List<string> Reported(TimelineRunResult run, string reason)
        => run.Chat.Where(line => line.Contains($" died: Cosmo Meteor ({reason})")).ToList();

    private static void Act(PlayerActionStep step, uint actionId, ulong targetId, float castSeconds)
        => ((FakeUserActions)Natives.UserActions).Act(new PlayerAction(step, ActionType.Action, actionId, targetId, castSeconds));

    // `heardFirst` schedules before Cosmo Meteor does, so its events at the same time go first; with `human` that seat is
    // the human's, pressing through its client.
    private static TimelineRunResult CosmoMeteor(Action<SimWorld, TopP6AlphaOmega, TopP6LimitBreaks, TopP6CosmoMeteor> timeline,
        PartyRole? human = null, Action<SimWorld, TopP6AlphaOmega>? heardFirst = null)
        => TopP6LimitBreakRun.Execute((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(0.5f, () =>
            {
                limitBreaks.SetGauge(3f);
                alphaOmega.ApplyCosmoMemoryStatuses();
            });
            heardFirst?.Invoke(world, alphaOmega);
            var cosmoMeteor = alphaOmega.CosmoMeteor(1.9f, Rolls, limitBreaks);
            timeline(world, alphaOmega, limitBreaks, cosmoMeteor);
        }, human is { } seat
            ? new TimelineRunOptions { GodMode = true, PlayerRole = seat, HumanPlayer = true, UserActionsEnabled = true }
            : new TimelineRunOptions { GodMode = true, PlayerRole = OffTank });
}
