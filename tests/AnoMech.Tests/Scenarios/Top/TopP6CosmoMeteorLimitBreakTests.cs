using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Top;
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

    private static TimelineRunResult CosmoMeteor(Action<SimWorld, TopP6AlphaOmega, TopP6LimitBreaks, TopP6CosmoMeteor> timeline)
        => TopP6LimitBreakRun.Execute((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(0.5f, () =>
            {
                limitBreaks.SetGauge(3f);
                alphaOmega.ApplyCosmoMemoryStatuses();
            });
            var cosmoMeteor = alphaOmega.CosmoMeteor(1.9f, Rolls, limitBreaks);
            timeline(world, alphaOmega, limitBreaks, cosmoMeteor);
        }, new TimelineRunOptions { GodMode = true, PlayerRole = OffTank });
}
