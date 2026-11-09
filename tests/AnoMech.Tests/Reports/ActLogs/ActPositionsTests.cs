using System.Numerics;
using static AnoMech.Tests.ActLogLines;

namespace AnoMech.Tests;

public class ActPositionsTests
{
    private const uint Player1 = 0x10000001, Player2 = 0x10000002, Helper = 0x40000020;

    [Test]
    public void ATimeBetweenTwoSamplesReadsTheLineBetweenThem()
    {
        var pose = Track(StatusList(1, Player1, 1000, new Vector2(100f, 100f)), StatusList(2, Player1, 1000, new Vector2(110f, 100f)))
            .At(Player1, Origin.AddSeconds(1.25))!.Value;

        Assert.Multiple(() =>
        {
            Assert.That(pose.Position.X, Is.EqualTo(102.5f).Within(1e-3f));
            Assert.That(pose.Gap.TotalSeconds, Is.EqualTo(0.25).Within(1e-6));
        });
    }

    [Test]
    public void BeforeTheFirstSampleAndAfterTheLastTheNearestOneStands()
    {
        var positions = Track(StatusList(1, Player1, 1000, new Vector2(100f, 100f)), StatusList(2, Player1, 1000, new Vector2(110f, 100f)));

        Assert.That(new[] { 0.5, 3.0 }.Select(s => positions.At(Player1, Origin.AddSeconds(s))!.Value).Select(p => (p.Position, p.Gap)),
                    Is.EqualTo(new[] { (new Vector2(100f, 100f), TimeSpan.FromSeconds(0.5)), (new Vector2(110f, 100f), TimeSpan.FromSeconds(1)) }));
    }

    // A 261 line carries only the coordinates that changed.
    [Test]
    public void AMemoryLineKeepsTheCoordinateItLeftOut()
    {
        var positions = Track(Memory(1, "Change", Player1, ("PosX", "100.0000"), ("PosY", "90.0000")),
                              Memory(2, "Change", Player1, ("PosX", "104.0000")));

        Assert.That(positions.At(Player1, Origin.AddSeconds(2))!.Value.Position, Is.EqualTo(new Vector2(104f, 90f)));
    }

    [Test]
    public void AMemoryLineWithHalfAPositionAndNothingBeforeItPlacesNobody()
        => Assert.That(Track(Memory(1, "Change", Player1, ("PosX", "100.0000"))).Knows(Player1), Is.False);

    [Test]
    public void AHitPlacesItsTargetAndItsSource()
    {
        var positions = Track(Hit(1, Player2, 0x5EF8, Player1, targetAt: new Vector2(90f, 95f), sourceAt: new Vector2(80f, 85f)));

        Assert.That((positions.At(Player1, Origin.AddSeconds(1))!.Value.Position, positions.At(Player2, Origin.AddSeconds(1))!.Value.Position),
                    Is.EqualTo((new Vector2(90f, 95f), new Vector2(80f, 85f))));
    }

    [Test]
    public void AnEffectResultAndAnUpdatePlaceTheirActor()
    {
        var positions = Track(EffectResult(1, Player1, 0x1A2B, 1000, new Vector2(90f, 95f)), UpdateHp(2, Player1, 1000, new Vector2(100f, 95f)));

        Assert.That(new[] { 1.0, 2.0 }.Select(s => positions.At(Player1, Origin.AddSeconds(s))!.Value.Position),
                    Is.EqualTo(new[] { new Vector2(90f, 95f), new Vector2(100f, 95f) }));
    }

    [Test]
    public void AHitOnNobodyPlacesNobody()
        => Assert.That(Track(Hit(1, Helper, 0x7B80, ActorIds.None, targetAt: new Vector2(90f, 95f))).Knows(ActorIds.None), Is.False);

    [Test]
    public void TheHeadingIsTheLastOneLoggedAtOrBeforeTheTime()
    {
        var positions = Track(Placed(1, Helper, new Vector2(100f, 100f), 0.5f), Placed(2, Helper, new Vector2(100f, 110f), 1.5f));

        Assert.That(new[] { 1.9, 2.0 }.Select(s => positions.At(Helper, Origin.AddSeconds(s))!.Value.Heading), Is.EqualTo(new float?[] { 0.5f, 1.5f }));
    }

    [Test]
    public void SamplesLoggedOutOfOrderAreSortedByTime()
    {
        var positions = Track(StatusList(2, Player1, 1000, new Vector2(110f, 100f)), StatusList(1, Player1, 1000, new Vector2(100f, 100f)));

        Assert.That(positions.At(Player1, Origin.AddSeconds(1.5))!.Value.Position.X, Is.EqualTo(105f).Within(1e-3f));
    }

    [Test]
    public void AnActorNeverLoggedHasNoPosition()
        => Assert.That(Track(StatusList(1, Player1, 1000, Vector2.Zero)).At(Player2, Origin), Is.Null);

    private static ActPositions Track(params string[] lines) => new(lines.Select(l => ActLine.Parse(l)!));
}
