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
// caster's limit break 3 resolving while they show kills all six comets; a physical ranged one kills each meteor whose
// hitbox its line reaches (30 long past its user's hitbox, 8 wide, toward its target). Comets still alive at 22.2 enrage,
// and meteors at 32.3, and everyone dies. Godmode, so the run plays on past the deaths it reports.
public class TopP6CosmoMeteorLimitBreakTests
{
    private const float CometsEnrage = 22.2f;
    private const float MeteorsEnrage = 32.3f;

    private static readonly TopP6CosmoMeteorRolls Rolls = new(
        [[MainTank, OffTank, RegenHealer, ShieldHealer], [MainTank, OffTank, RegenHealer, ShieldHealer]],
        [CasterDps, MeleeDpsB, MainTank], ShieldHealer);

    private static uint Job(PartyRole seat) => PartyPresets.Standard[(int)seat].ClassJob;

    [SetUp]
    public void InstallGame() => FakeGame.Install();

    [Test]
    public void ACasterLimitBreak3KillsEveryComet()
    {
        var alive = new Dictionary<float, (int Comets, int Meteors)>();
        var run = CosmoMeteor((world, alphaOmega, limitBreaks, cosmoMeteor) =>
        {
            world.Events.Add(11f, () => limitBreaks.Press(CasterDps, Job(CasterDps), alphaOmega.Enemy));
            foreach (var at in new[] { 15.4f, 15.6f })
                world.Events.Add(at, () => alive[at] = Alive(cosmoMeteor));
        });

        Assert.That(alive[15.4f], Is.EqualTo((6, 2)));
        Assert.That(alive[15.6f], Is.EqualTo((0, 2)));
        Assert.That(Reported(run, TopP6CosmoMeteor.CometsEnraged), Is.Empty);
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
                world.Events.Add(enragesAt - castSeconds, () => Act(PlayerActionStep.CastStarted, limitBreak3, Target(), castSeconds));
                world.Events.Add(enragesAt, () => Act(PlayerActionStep.Resolved, limitBreak3, Target(), 0f));
            });

        Assert.That(Reported(run, comets ? TopP6CosmoMeteor.CometsEnraged : TopP6CosmoMeteor.MeteorsEnraged), Is.Empty, run.ToString);
        Assert.That(comets ? alive.Comets : alive.Meteors, Is.Zero);
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
