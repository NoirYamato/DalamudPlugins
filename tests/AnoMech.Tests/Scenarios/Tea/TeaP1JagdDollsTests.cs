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
