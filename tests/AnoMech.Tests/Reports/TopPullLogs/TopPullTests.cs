using System.Numerics;
using AnoMech.Core.UserActions;
using static AnoMech.Scenarios.Top.TopP3Constants;
using static AnoMech.Tests.ActLogLines;

namespace AnoMech.Tests;

public class TopPullTests
{
    private const uint Player1 = 0x10000001, Boss = 0x40000010;

    private const ushort Holmgang = 409;

    [Test]
    public void APlayerIsDeadFromTheirDeathUntilALaterLineShowsHitPointsAgain()
    {
        var pull = Pull(StatusList(1, Player1, 5000, Vector2.Zero), Death(10, Player1, Boss), StatusList(10.5, Player1, 5000, Vector2.Zero),
                        StatusList(14, Player1, 0, Vector2.Zero), StatusList(20, Player1, 5000, Vector2.Zero));

        Assert.That(new[] { 5, 12, 15, 25 }.Select(s => pull.IsAlive(Player1, Origin.AddSeconds(s))), Is.EqualTo(new[] { true, false, false, true }));
    }

    // The death line comes about 2 s after the hit points reach 0.
    [Test]
    public void APlayerIsDeadFromTheirFirstZeroHitPointsBeforeTheirDeathLine()
    {
        var pull = Pull(StatusList(1, Player1, 5000, Vector2.Zero), EffectResult(10, Player1, 0x20, 0, Vector2.Zero), Death(12, Player1, Boss));

        Assert.That(new[] { 9.9, 10.5, 13 }.Select(s => pull.IsAlive(Player1, Origin.AddSeconds(s))), Is.EqualTo(new[] { true, false, false }));
    }

    [Test]
    public void ZeroHitPointsThePullEndsBeforeTheirDeathLineAreADeath()
        => Assert.That(Pull(EffectResult(10, Player1, 0x20, 0, Vector2.Zero)).IsAlive(Player1, Origin.AddSeconds(15)), Is.False);

    [Test]
    public void AnUpdateShowingHitPointsAgainIsARaise()
    {
        var pull = Pull(Death(10, Player1, Boss), UpdateHp(10.5, Player1, 5000, Vector2.Zero), UpdateHp(30, Player1, 5000, Vector2.Zero));

        Assert.That(new[] { 20, 31 }.Select(s => pull.IsAlive(Player1, Origin.AddSeconds(s))), Is.EqualTo(new[] { false, true }));
    }

    [TestCase(2.5, true)]
    [TestCase(3.5, false)]
    public void ADeathWithinTheSecondsAfterATimeIsFound(double diedAfter, bool found)
        => Assert.That(Pull(EffectResult(5 + diedAfter, Player1, 0x20, 0, Vector2.Zero)).DiedWithin(Player1, Origin.AddSeconds(5), 3),
                       found ? Is.EqualTo(Origin.AddSeconds(5 + diedAfter)) : Is.Null);

    // The second effect result, from another hit, kills; it says nothing of the first hit.
    [TestCase(0, true)]
    [TestCase(1200, false)]
    public void AHitsOwnEffectResultSaysWhetherItKilled(int hpAfter, bool killed)
    {
        var pull = Pull(Hit(5, Boss, 0x7B4E, Player1, sequence: 0x20), EffectResult(5.6, Player1, 0x20, hpAfter, Vector2.Zero),
                        EffectResult(6, Player1, 0x21, 0, Vector2.Zero));

        Assert.That(pull.Outcome(pull.Hits[0]), Is.EqualTo(killed ? HitOutcome.Killed : HitOutcome.Survived));
    }

    [TestCase(2.5, true)]
    [TestCase(3.5, false)]
    public void AHitWithNoEffectResultKilledWhenItsTargetDiedWithinThreeSeconds(double diedAfter, bool killed)
    {
        var pull = Pull(Hit(5, Boss, 0x7B4E, Player1, sequence: 0x20), Death(5 + diedAfter, Player1, Boss));

        Assert.That(pull.Outcome(pull.Hits[0]), Is.EqualTo(killed ? HitOutcome.Killed : HitOutcome.Survived));
    }

    [Test]
    public void AHitOnAPlayerUnderAnInvulnerabilityShowsNothingOfItsDamage()
    {
        var pull = Pull(Gains(1, Holmgang, 10f, Player1, Player1),
                        Hit(5, Boss, 0x7B7F, Player1, sequence: 0x20), EffectResult(5.6, Player1, 0x20, 1, Vector2.Zero),
                        Loses(11, Holmgang, Player1, Player1),
                        Hit(12, Boss, 0x7B7F, Player1, sequence: 0x21), EffectResult(12.6, Player1, 0x21, 0, Vector2.Zero),
                        Hit(13, Boss, 0x7B7F, Player1, sequence: 0x22));

        Assert.Multiple(() =>
        {
            Assert.That(pull.Hits.Select(pull.Outcome), Is.EqualTo(new[] { HitOutcome.Invulnerable, HitOutcome.Killed, HitOutcome.AlreadyDead }));
            Assert.That(pull.Describe(pull.Hits[0]), Is.EqualTo("under Holmgang"));
        });
    }

    [Test]
    public void AHoldingRunsFromItsGainToItsLossOrItsHoldersDeath()
    {
        var pull = Pull(Gains(1, P3StatusId.RedRot, 27f, ActorIds.None, Player1), Loses(5, P3StatusId.RedRot, ActorIds.None, Player1),
                        Gains(6, P3StatusId.RedRot, 27f, ActorIds.None, Player1), UpdateHp(8, Player1, 0, Vector2.Zero), Death(10, Player1, Boss));

        Assert.That(pull.Holdings(P3StatusId.RedRot).Select(h => (h.From, h.To)), Is.EqualTo(new[]
        {
            (Origin.AddSeconds(1), Origin.AddSeconds(5)), (Origin.AddSeconds(6), Origin.AddSeconds(8)),
        }));
    }

    [Test]
    public void AGroupTakesWhatComesWithinTheWindowOfItsFirst()
        => Assert.That(TopPull.Clusters(new[] { 0.0, 0.4, 0.9, 1.2, 5.0 }, s => Origin.AddSeconds(s), 1),
                       Is.EqualTo(new[] { new[] { 0.0, 0.4, 0.9 }, new[] { 1.2 }, new[] { 5.0 } }));

    [TestCase(100f, 86f, 0)]
    [TestCase(114f, 100f, 90)]
    [TestCase(100f, 114f, 180)]
    [TestCase(90.1f, 90.1f, 315)]
    public void ABearingIsDegreesClockwiseFromNorth(float x, float y, double degrees)
        => Assert.That(TopPull.Bearing(new Vector2(x, y)), Is.EqualTo(degrees).Within(0.01));

    [Test]
    public void APullReachesItsFurthestMechanic()
    {
        var pull = Pull(Cast(1, Boss, ActionIdOf("P2"), 2.7f), Cast(60, Boss, P3ActionId.WaveRepeater, 4.7f), Cast(90, Boss, P3ActionId.HelloWorld, 4.7f));

        Assert.That(pull.Reached, Is.EqualTo("P3 Hello World"));
    }

    [Test]
    public void APlayerIsNamedByTheirJob()
        => Assert.That((Pull().Who(Player1), Pull().Who(0x10000009)), Is.EqualTo((JobId.WhiteMage.ToString(), "an unknown player")));

    private static uint ActionIdOf(string milestone) => TopPull.Milestones.Single(m => m.Name == milestone).Casts[0];

    private static TopPull Pull(params string[] lines)
    {
        var pull = new ActPull { File = "Network_test.log", Number = 1, Start = Origin, Jobs = new() { [Player1] = (uint)JobId.WhiteMage } };
        pull.Lines.AddRange(lines.Select(l => ActLine.Parse(l)!));
        return new TopPull(pull);
    }
}
