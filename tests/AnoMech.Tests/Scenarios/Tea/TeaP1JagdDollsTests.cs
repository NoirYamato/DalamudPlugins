using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Tea;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Tea.TeaP1Constants;

namespace AnoMech.Tests;

// The Jagd Dolls on their own: the boss piece with both bosses parked at the rim, north and south, and everyone far
// east unless a test puts them elsewhere. The dolls appear at 1.0 and turn targetable at 1.1 unless a test says
// otherwise.
public class TeaP1JagdDollsTests
{
    [Test]
    public void EachQuarterOfEveryLayoutsFrameHoldsOneDoll([Values] TeaP1OpenQuarter open)
    {
        var frame = new TeaP1TornadoLayout(open);
        List<(TeaP1Quarter Quarter, Vector2 Relative)> dolls = [];
        var run = Dolls((world, fight, _) =>
        {
            fight.JagdDollsAppear(1f);
            world.Events.Add(1.5f, () => dolls = Enum.GetValues<TeaP1Quarter>()
                .Select(quarter => (quarter, frame.ToRelative(fight.Dolls.In(frame, quarter)!.Spot))).ToList());
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(dolls.Select(doll => doll.Relative).Distinct().Count(), Is.EqualTo(4));
        Assert.Multiple(() =>
        {
            foreach (var (quarter, relative) in dolls)
            {
                Assert.That(relative.Length(), Is.EqualTo(P1Geometry.JagdDollRing).Within(0.01f), $"{quarter}");
                Assert.That((relative.X > 0f, relative.Y < 0f), Is.EqualTo((quarter is TeaP1Quarter.Northeast or TeaP1Quarter.Southeast,
                    quarter is TeaP1Quarter.Northeast or TeaP1Quarter.Northwest)), $"{quarter} at {relative}");
            }
        });
    }

    // The client has a spawned actor only some frames after its spawn, 15 here, and drops what is written to it before:
    // the dolls turn targetable at 1.1, six frames after they were spawned.
    [Test]
    public void ADollTheClientDoesNotHaveYetTurnsTargetableOnceItIsThereAndCannotBeHitBefore()
    {
        var up = new List<bool>();
        var hit = new List<bool>();
        var shown = new List<bool>();
        var run = Dolls((world, fight, presses) =>
        {
            world.Events.Add(0.5f, () => ((FakeBattleCharas)Natives.BattleCharas).PacketSpawnFrames = (15, 15));
            fight.JagdDollsAppear(1f);
            fight.JagdDollsTurnTargetable(1.1f);
            foreach (var at in new[] { 1.15f, 1.35f })
                world.Events.Add(at, () =>
                {
                    var doll = fight.Dolls.All[0];
                    up.Add(doll.IsUp);
                    shown.Add((doll.Enemy.Proxy!.TargetableStatus & 0x02) != 0);
                    presses.Strike(world.Party.Get(MeleeDpsA)!, doll.Enemy);
                    hit.Add(doll.HasBeenHit);
                });
            world.Events.Add(2f, () => { });
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(up, Is.EqualTo(new[] { false, true }));
        Assert.That(shown, Is.EqualTo(new[] { false, true }), "the client was told once it had the actor");
        Assert.That(hit, Is.EqualTo(new[] { false, true }));
    }

    // H1 stands 5 y from one doll and 8 y or more from the rest. The timeline's last line is at 25.0.
    [Test]
    public void OneDollsExhaustLeavesLuminousAetheroplasmAndTheExhaustsStopWithTheTimeline()
    {
        var carries = new List<bool>();
        var run = Dolls((world, fight, _) =>
        {
            fight.JagdDollsAppear(1f);
            fight.JagdDollsTurnTargetable(1.1f);
            world.Events.Add(1.5f, () => TimelineRun.Place(world, RegenHealer, 11.2f, -4.6f));
            fight.Exhausts(2f);
            fight.HandOfPain(25f);
            foreach (var at in new[] { 1.9f, 2.1f, 6.9f, 7.1f })
                world.Events.Add(at, () => carries.Add(world.Party.Get(RegenHealer)!.HasStatus(P1StatusId.LuminousAetheroplasm)));
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(carries, Is.EqualTo(new[] { false, true, true, false }), "4.96 s from 2.0");
        var exhausts = run.Sent("ability", P1ActionId.Exhaust);
        Assert.That(exhausts.Select(sent => sent.Time), Is.EqualTo(Enumerable.Range(0, 3).SelectMany(nth => Enumerable.Repeat(2f + nth * P1Duration.ExhaustEvery, 4)))
            .Within(TimelineRun.FrameLate), "from all four at 2.0, 12.64 and 23.28, and not at 33.92");
        Assert.That(exhausts.Count(sent => sent.Hits.Count == 1), Is.EqualTo(3), "one doll reaches H1 each time, and nobody else");
        Assert.That(run.Sent("ability", P1ActionId.ExhaustFromAPlayer), Is.Empty);
        Assert.That(run.Elapsed, Is.InRange(25f, 30.5f), "the run ends by itself after its last line");
    }

    // The main tank stands on the middle, 7 y from all four dolls, as they Exhaust at 2.0, under a tank's
    // invulnerability: it explodes all the same and lives through it. M1 and the off tank stand 8 y from it, each beside
    // one doll and out of the others' reach: the explosion kills M1, and the off tank, invulnerable too, lives.
    [TestCase(P1StatusId.HallowedGround, P1StatusId.Holmgang)]
    [TestCase(P1StatusId.Holmgang, P1StatusId.LivingDead)]
    [TestCase(P1StatusId.LivingDead, P1StatusId.Superbolide)]
    [TestCase(P1StatusId.Superbolide, P1StatusId.HallowedGround)]
    public void ATanksInvulnerabilityTakesItThroughAnExplosionItsOwnIncluded(int theExplodingTanks, int theOtherTanks)
    {
        var marked = new List<bool>();
        var run = Dolls((world, fight, _) =>
        {
            fight.JagdDollsAppear(1f);
            fight.JagdDollsTurnTargetable(1.1f);
            world.Events.Add(1.5f, () =>
            {
                TimelineRun.Place(world, MainTank, 0f, 0f);
                TimelineRun.Place(world, MeleeDpsA, OnTheBearing(67.5f, 8f).X, OnTheBearing(67.5f, 8f).Y);
                TimelineRun.Place(world, OffTank, OnTheBearing(247.5f, 8f).X, OnTheBearing(247.5f, 8f).Y);
                world.Party.Get(MainTank)!.AddStatus((ushort)theExplodingTanks, 10f);
                world.Party.Get(OffTank)!.AddStatus((ushort)theOtherTanks, 10f);
            });
            fight.Exhausts(2f);
            world.Events.Add(2.2f, () => marked.AddRange(new[] { MainTank, OffTank }.Select(tank => world.Party.Get(tank)!.HasStatus(P1StatusId.LuminousAetheroplasm))));
            world.Events.Add(3f, () => { });
        });

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(run.Deaths.Select(death => (death.Role, death.ActionId)), Is.EqualTo(new[] { (MeleeDpsA, (uint?)P1ActionId.ExhaustFromAPlayer) }), run.ToString);
        Assert.That(run.Deaths[0].Cause, Does.Contain(TeaP1JagdDolls.LuminousAetheroplasmBurst).And.Contain("short of a tank's invulnerability"));
        Assert.That(run.Sent("ability", P1ActionId.ExhaustFromAPlayer), Has.Count.EqualTo(1), "the main tank explodes all the same");
        Assert.That(marked, Is.EqualTo(new[] { true, true }), "the Exhausts land on both tanks");
    }

    private static Vector2 OnTheBearing(float degrees, float fromTheMiddle)
        => new Vector2(MathF.Sin(degrees * MathF.PI / 180f), -MathF.Cos(degrees * MathF.PI / 180f)) * fromTheMiddle;

    // M1, 4.0 y from the doll it hits at 1.5, is inside the 4.3 y a doll lets its holder stand: the doll stays. At 3.1
    // M1 steps to 4.6 y, and the doll walks until it is 3.3 y from it.
    [Test]
    public void ADollSetsOffOnlyOnceItsHolderIsAYalmPastWhereItStops()
    {
        Vector3 spot = default, stayed = default;
        var standsOff = float.NaN;
        var run = Dolls((world, fight, presses) =>
        {
            fight.JagdDollsAppear(1f);
            fight.JagdDollsTurnTargetable(1.1f);
            world.Events.Add(1.3f, () =>
            {
                spot = fight.Dolls.All[0].Spot;
                TimelineRun.Place(world, MeleeDpsA, spot.X + 4.0f, spot.Z);
            });
            world.Events.Add(1.5f, () => presses.Strike(world.Party.Get(MeleeDpsA)!, fight.Dolls.All[0].Enemy));
            world.Events.Add(3.0f, () => stayed = fight.Dolls.All[0].Enemy.Position);
            world.Events.Add(3.1f, () => TimelineRun.Place(world, MeleeDpsA, spot.X + 4.6f, spot.Z));
            world.Events.Add(4.5f, () => standsOff = Vector3.Distance(fight.Dolls.All[0].Enemy.Position, world.Party.Get(MeleeDpsA)!.Position));
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(Vector3.Distance(stayed, spot), Is.LessThan(0.01f), "tethered since 2.1, with its holder 4.0 y off");
        Assert.That(standsOff, Is.EqualTo(3.3f).Within(0.1f));
    }

    // M1 hits a doll 11.8 y from it at 1.5 and dies at 2.6, with the doll, tethered at 2.1, on its way. The doll stops
    // there and its tether goes. It still Exhausts, at 4.0, and a boss put beside it at 4.5 is still fed it.
    [Test]
    public void ADollWhoseHolderDiesStopsWhereItIsLosesItsTetherAndIsStillADoll()
    {
        var stands = new List<Vector3>();
        var tethered = new List<bool>();
        TeaP1Boss? fedTo = null;
        Vector3 spot = default;
        var run = Dolls((world, fight, presses) =>
        {
            fight.JagdDollsAppear(1f);
            fight.JagdDollsTurnTargetable(1.1f);
            world.Events.Add(1.5f, () => presses.Strike(world.Party.Get(MeleeDpsA)!, fight.Dolls.All[0].Enemy));
            world.Events.Add(2.5f, () => tethered.Add(fight.Dolls.All[0].Tether is not null));
            world.Events.Add(2.6f, () => world.Party.Get(MeleeDpsA)!.Die(P1ActionId.Splash));
            foreach (var at in new[] { 2.8f, 4.4f })
                world.Events.Add(at, () =>
                {
                    spot = fight.Dolls.All[0].Spot;
                    stands.Add(fight.Dolls.All[0].Enemy.Position);
                    tethered.Add(fight.Dolls.All[0].Tether is not null);
                });
            fight.Exhausts(4f);
            world.Events.Add(4.5f, () => fight.Liquid!.SetPosition(fight.Dolls.All[0].Enemy.Position + new Vector3(0f, 0f, -3f)));
            world.Events.Add(4.7f, () => fedTo = fight.Dolls.All[0].FedTo);
        });

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(tethered, Is.EqualTo(new[] { true, false, false }), "at 2.5, 2.8 and 4.4");
        Assert.That(Vector3.Distance(stands[0], spot), Is.InRange(2f, 7f), "it had set off");
        Assert.That(stands[1], Is.EqualTo(stands[0]), "and stands where its holder died on it");
        Assert.That(run.Sent("ability", P1ActionId.Exhaust).Count(sent => MathF.Abs(sent.Time - 4f) < 0.1f), Is.EqualTo(4), "all four Exhaust at 4.0");
        Assert.That(fedTo, Is.EqualTo(TeaP1Boss.LivingLiquid));
    }

    // M1 dies 0.3 s after its hit, before the tether would show 0.6 s after it.
    [Test]
    public void ADollWhoseFirstHitterDiesBeforeItsTetherShowsTethersNobody()
    {
        float? tetheredAt = 0f;
        var hit = false;
        var run = Dolls((world, fight, presses) =>
        {
            fight.JagdDollsAppear(1f);
            fight.JagdDollsTurnTargetable(1.1f);
            world.Events.Add(1.5f, () => presses.Strike(world.Party.Get(MeleeDpsA)!, fight.Dolls.All[0].Enemy));
            world.Events.Add(1.8f, () => world.Party.Get(MeleeDpsA)!.Die(P1ActionId.Splash));
            world.Events.Add(3f, () => (tetheredAt, hit) = (fight.Dolls.All[0].TetheredAt, fight.Dolls.All[0].HasBeenHit));
        });

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That((tetheredAt, hit), Is.EqualTo(((float?)null, true)));
    }

    [Test]
    public void ExhaustFromAPlayerAndReducibleComplexityOnlyShowTheirHits()
    {
        var run = Dolls((world, fight, _) =>
        {
            world.Events.Add(1f, () => TimelineRun.Place(world, PerRole.All.ToDictionary(role => role, _ => new Vector2(2f, 0f))));
            world.Events.Add(2f, () => fight.CastFromHelper(TeaP1Actions.ExhaustFromAPlayer, at: new Placement(Vector3.Zero, 0f)));
            world.Events.Add(3f, () => fight.Liquid!.Cast(TeaP1Actions.ReducibleComplexity));
            world.Events.Add(5f, () => { });
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(run.Sent("ability", P1ActionId.ExhaustFromAPlayer).Single().Hits, Has.Count.EqualTo(8));
        Assert.That(run.Sent("ability", P1ActionId.ReducibleComplexity).Single().Hits, Has.Count.EqualTo(8));
    }

    [TestCase(P1ActionId.Exhaust, "Exhaust")]
    [TestCase(P1ActionId.ExhaustFromAPlayer, "Exhaust")]
    [TestCase(P1ActionId.ReducibleComplexity, "Reducible Complexity")]
    public void EachActionIsTheOneItIsNamedFor(uint actionId, string name)
    {
        FakeGame.Install();

        Assert.That(Natives.Data.Action(actionId)?.Name, Is.EqualTo(name));
        Assert.That(Natives.Data.Status(P1StatusId.LuminousAetheroplasm)?.Name, Is.EqualTo("Luminous Aetheroplasm"));
        Assert.That(Natives.Data.BNpcName(P1BNpcNameId.JagdDoll), Is.EqualTo("jagd doll").IgnoreCase);
    }

    private static TimelineRunResult Dolls(Action<SimWorld, TeaP1LivingLiquid, TeaP1Presses> schedule)
    {
        TeaP1LivingLiquid? piece = null;
        TeaP1Presses? presses = null;
        var run = TimelineRun.Execute(world =>
        {
            presses = new TeaP1Presses(world);
            var fight = piece = new TeaP1LivingLiquid(world, presses);
            fight.Spawn(new Placement(new Vector3(0f, 0f, -18f), 0f), new Placement(new Vector3(0f, 0f, 18f), 0f));
            world.Events.Add(0.1f, () => TimelineRun.Place(world, PerRole.All.ToDictionary(role => role, _ => new Vector2(18f, 0f))));
            schedule(world, fight, presses);
        }, new TimelineRunOptions { Phase = TeaZone.P1, Tick = () => piece?.Tick() });
        presses?.Dispose();
        return run;
    }
}
