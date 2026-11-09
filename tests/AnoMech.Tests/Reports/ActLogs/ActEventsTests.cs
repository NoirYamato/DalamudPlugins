using System.Numerics;
using static AnoMech.Tests.ActLogLines;

namespace AnoMech.Tests;

public class ActEventsTests
{
    private const uint Boss = 0x40000010, Helper = 0x40000020, Player1 = 0x10000001;

    [Test]
    public void ACastReadsItsCasterActionTargetAndCastTime()
        => Assert.That(ActCast.From(Parse(Cast(2, Boss, 0x7B55, 4.7f))),
                       Is.EqualTo(new ActCast(Origin.AddSeconds(2), Boss, 0x7B55, Boss, 4.7f)));

    [Test]
    public void ACastsLocationReadsItsPointAndHeading()
        => Assert.That(ActCastAt.From(Parse(CastAt(2, Helper, 0x7B5F, new Vector2(114f, 100f), -1.57f))),
                       Is.EqualTo(new ActCastAt(Origin.AddSeconds(2), Helper, 0x7B5F, new Vector2(114f, 100f), -1.57f)));

    [Test]
    public void AHitReadsItsTargetWhereItStoodAndItsPlace()
    {
        var hit = ActHit.From(Parse(Hit(3, Helper, 0x7B63, Player1, targetAt: new Vector2(90f, 105f), index: 2, count: 8)))!.Value;

        Assert.That((hit.Source, hit.Action, hit.Target, hit.TargetPosition, hit.TargetIndex, hit.TargetCount, hit.HitSomeone),
                    Is.EqualTo((Helper, 0x7B63u, Player1, (Vector2?)new Vector2(90f, 105f), 2, 8, true)));
    }

    [Test]
    public void AHitOnNobodyHitsNobody()
        => Assert.That(ActHit.From(Parse(Hit(3, Helper, 0x7B80, ActorIds.None)))!.Value.HitSomeone, Is.False);

    [TestCase((ushort)0x7FFF, 0f)]
    [TestCase((ushort)0xFFFF, MathF.PI)]
    [TestCase((ushort)0x0000, -MathF.PI)]
    [TestCase((ushort)0xBFFF, MathF.PI / 2)]
    public void AHitsRotationSpreadsMinusPiToPiOverItsSixteenBits(ushort raw, float heading)
        => Assert.That(ActHit.From(Parse(Hit(3, Helper, 0x7B7E, Player1, rotation: raw)))!.Value.Rotation, Is.EqualTo(heading).Within(1e-3f));

    [Test]
    public void AnOlderPluginsHitHasNoRotation()
    {
        var shortLine = string.Join('|', Hit(3, Helper, 0x7B7E, Player1).Split('|').Take(53).Append("hash"));

        Assert.That(ActHit.From(Parse(shortLine))!.Value.Rotation, Is.Null);
    }

    [Test]
    public void AStatusReadsWhoGainedOrLostItAndForHowLong()
        => Assert.That(new[] { Gains(4, 0xDC8, 10f, Helper, Player1), Loses(9, 0xDC8, Helper, Player1) }.Select(l => ActStatus.From(Parse(l))),
                       Is.EqualTo(new ActStatus?[]
                       {
                           new ActStatus(Origin.AddSeconds(4), true, 0xDC8, 10f, Helper, Player1),
                           new ActStatus(Origin.AddSeconds(9), false, 0xDC8, 0f, Helper, Player1),
                       }));

    [Test]
    public void APlacementReadsWhereTheServerPutTheActor()
        => Assert.That(ActMove.From(Parse(Placed(5, Helper, new Vector2(88.89f, 105.76f), 0.15f))),
                       Is.EqualTo(new ActMove(Origin.AddSeconds(5), Helper, new Vector2(88.89f, 105.76f), 0.15f, true)));

    [Test]
    public void ACombatantReadsItsJobOrItsBNpcBase()
    {
        var player = ActCombatant.From(Parse(Player(0, Player1, 24, new Vector2(100f, 110f))))!.Value;
        var enemy = ActCombatant.From(Parse(Enemy(0, Boss, 15717, new Vector2(100f, 100f), 0f)))!.Value;

        Assert.That((player.Job, enemy.BNpcBase, enemy.Position), Is.EqualTo((24u, 15717u, (Vector2?)new Vector2(100f, 100f))));
    }

    [Test]
    public void APlayedTimelineReadsFromItsActorControl()
        => Assert.That(ActTimeline.From(Parse(Line(273, 6, 10, (2, Hex(Boss)), (3, "0197"), (4, "1E43"), (5, "0")))),
                       Is.EqualTo(new ActTimeline(Origin.AddSeconds(6), Boss, 7747)));

    [Test]
    public void AnotherActorControlIsNoTimeline()
        => Assert.That(ActTimeline.From(Parse(Line(273, 6, 10, (2, Hex(Boss)), (3, "003F"), (4, "5")))), Is.Null);

    [Test]
    public void AnObjectComesAndGoesThroughTheMemoryLines()
    {
        var added = ActObject.From(Parse(Memory(7, "Add", Helper, ("BNpcID", "1EB821"), ("Type", "7"))));
        var removed = ActObject.From(Parse(Memory(26, "Remove", Helper)));
        var changed = ActObject.From(Parse(Memory(8, "Change", Helper, ("PosX", "1.0"))));

        Assert.That(new[] { added, removed, changed }, Is.EqualTo(new ActObject?[]
        {
            new ActObject(Origin.AddSeconds(7), true, Helper, 2013217), new ActObject(Origin.AddSeconds(26), false, Helper, null), null,
        }));
    }

    [Test]
    public void ALineOfAnotherTypeReadsAsNoEvent()
    {
        var cast = Parse(Cast(2, Boss, 0x7B55, 4.7f));

        Assert.That((ActHit.From(cast), ActStatus.From(cast), ActMove.From(cast), ActDeath.From(cast)), Is.EqualTo(((ActHit?)null, (ActStatus?)null, (ActMove?)null, (ActDeath?)null)));
    }

    private static ActLine Parse(string text) => ActLine.Parse(text)!;
}
