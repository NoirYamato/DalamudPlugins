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
    public void AHitReadsTheSequenceItsEffectResultNames()
        => Assert.That(ActHit.From(Parse(Hit(3, Helper, 0x7B63, Player1, sequence: 0x1A2B, aoe: true)))!.Value.Sequence, Is.EqualTo(0x1A2Bu));

    // The second effect is the damage; under the 0x40 flag a third byte rides above the first two, added onto the second.
    [TestCase("750003", "CB670000", 52071)]
    [TestCase("750003", "424E400F", 999999)]
    [TestCase("650605", "069F0000", 1695)]
    public void AHitReadsTheDamageOfItsDamageEffect(string kind, string value, int damage)
    {
        var line = Line(21, 3, 55, (2, Hex(Helper)), (4, "4825"), (6, Hex(Player1)), (8, "A10"), (9, "4D00000"), (10, kind), (11, value));

        Assert.That(ActHit.From(Parse(line))!.Value.Damage, Is.EqualTo(damage));
    }

    [Test]
    public void AHitWithNoDamageEffectReadsNoDamage()
        => Assert.That((ActHit.From(Parse(Hit(3, Helper, 0x7B63, Player1)))!.Value.Damage, ActHit.From(Parse(Hit(3, Helper, 0x7B63, Player1, damage: 1695)))!.Value.Damage),
                       Is.EqualTo(((int?)null, (int?)1695)));

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
    public void AMoveReadsWhereTheServerMovedTheActor()
        => Assert.That(ActMove.From(Parse(Moved(5, Boss, new Vector2(100f, 93.5f), -1.57f))),
                       Is.EqualTo(new ActMove(Origin.AddSeconds(5), Boss, new Vector2(100f, 93.5f), -1.57f, false)));

    [TestCase(true)]
    [TestCase(false)]
    public void ATargetabilityToggleReadsWhetherTheActorCanBeTargeted(bool shown)
        => Assert.That(ActTargetable.From(Parse(NameToggle(6, Boss, shown))), Is.EqualTo(new ActTargetable(Origin.AddSeconds(6), Boss, shown)));

    [Test]
    public void ACombatantReadsItsJobOrItsBNpcBase()
    {
        var player = ActCombatant.From(Parse(Player(0, Player1, 24, new Vector2(100f, 110f))))!.Value;
        var enemy = ActCombatant.From(Parse(Enemy(0, Boss, 15717, new Vector2(100f, 100f), 1.25f)))!.Value;

        Assert.That((player.Job, enemy.BNpcBase, enemy.Position, enemy.Heading),
                    Is.EqualTo((24u, 15717u, (Vector2?)new Vector2(100f, 100f), (float?)1.25f)));
    }

    [Test]
    public void HitPointsReadFromAnEffectResultAStatusListOrAnUpdate()
        => Assert.That(new[]
                       {
                           EffectResult(1, Player1, 0x1A2B, 0, Vector2.Zero), StatusList(2, Player1, 5000, Vector2.Zero),
                           UpdateHp(3, Player1, 6000, Vector2.Zero),
                       }.Select(l => ActHp.From(Parse(l))),
                       Is.EqualTo(new ActHp?[]
                       {
                           new ActHp(Origin.AddSeconds(1), Player1, 0, 0x1A2B), new ActHp(Origin.AddSeconds(2), Player1, 5000, null),
                           new ActHp(Origin.AddSeconds(3), Player1, 6000, null),
                       }));

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
    public void ATetherReadsItsEndsAndItsId()
        => Assert.That(ActTether.From(Parse(Tether(7, Helper, Player1, 0x11))), Is.EqualTo(new ActTether(Origin.AddSeconds(7), Helper, Player1, 0x11)));

    [Test]
    public void AModelStateReadsFromItsActorControl()
        => Assert.That(ActModelState.From(Parse(ModelState(6, Boss, 20))), Is.EqualTo(new ActModelState(Origin.AddSeconds(6), Boss, 20)));

    [Test]
    public void APlayedTimelineIsNoModelState()
        => Assert.That(ActModelState.From(Parse(Line(273, 6, 10, (2, Hex(Boss)), (3, "0197"), (4, "1E43"), (5, "0")))), Is.Null);

    // A line that names no maximum, as a boss's effect result, gives none.
    [Test]
    public void HitPointsBesideTheirMaximumReadFromACombatantJoiningAStatusListOrAnUpdate()
        => Assert.That(new[]
                       {
                           Enemy(1, Boss, 11335, Vector2.Zero, 0f, 3300000, 3356051), StatusList(2, Player1, 5000, Vector2.Zero),
                           UpdateHp(3, Player1, 6000, Vector2.Zero), Line(37, 4, 16, (2, Hex(Boss)), (4, "00001A2B"), (5, "900")),
                       }.Select(l => ActHealth.From(Parse(l))),
                       Is.EqualTo(new ActHealth?[]
                       {
                           new ActHealth(Origin.AddSeconds(1), Boss, 3300000, 3356051), new ActHealth(Origin.AddSeconds(2), Player1, 5000, 100000),
                           new ActHealth(Origin.AddSeconds(3), Player1, 6000, 100000), null,
                       }));

    [Test]
    public void ATargetChangeReadsWhomAnEnemyTurnsTo()
    {
        var turned = ActTargetChange.From(Parse(Memory(5, "Change", Boss, ("NPCTargetID", Hex(Player1)), ("PCTargetID", Hex(Player1)))));
        var moved = ActTargetChange.From(Parse(Memory(6, "Change", Boss, ("PosX", "1.0"))));

        Assert.That((turned, moved), Is.EqualTo(((ActTargetChange?)new ActTargetChange(Origin.AddSeconds(5), Boss, Player1), (ActTargetChange?)null)));
    }

    [Test]
    public void AHeadMarkerReadsItsTargetAndId()
        => Assert.That(ActHeadMarker.From(Parse(HeadMarker(8, Player1, 79))), Is.EqualTo(new ActHeadMarker(Origin.AddSeconds(8), Player1, 79)));

    [Test]
    public void AnAbilityAimedAtTheGroundReadsWhereItWentOff()
        => Assert.That(ActAbilityAt.From(Parse(AbilityAt(9, Helper, 0x4830, new Vector2(109.88f, 90.07f), -2.35f))),
                       Is.EqualTo(new ActAbilityAt(Origin.AddSeconds(9), Helper, 0x4830, new Vector2(109.88f, 90.07f), -2.35f)));

    [Test]
    public void AnAbilityOnATargetCarriesNoLocation()
        => Assert.That(ActAbilityAt.From(Parse(Line(264, 9, 12, (2, Hex(Helper)), (3, "4825"), (4, "00001A2B"), (5, "0"), (9, "0.12")))), Is.Null);

    [Test]
    public void AMapEffectReadsItsSlotAndFlags()
        => Assert.That(ActMapEffect.From(Parse(MapEffect(10, 0x00020004, 7))), Is.EqualTo(new ActMapEffect(Origin.AddSeconds(10), 0x00020004, 7)));

    [Test]
    public void ALineOfAnotherTypeReadsAsNoEvent()
    {
        var cast = Parse(Cast(2, Boss, 0x7B55, 4.7f));

        Assert.That((ActHit.From(cast), ActStatus.From(cast), ActMove.From(cast), ActDeath.From(cast)), Is.EqualTo(((ActHit?)null, (ActStatus?)null, (ActMove?)null, (ActDeath?)null)));
    }

    [Test]
    public void AHitNamesTheStatusItPutsOnItsTargetAndTheKnockbackRowItThrowsItBy()
    {
        var hit = ActHit.From(Parse(Hit(10, Boss, 0x4834, Player1, applies: 695, knockbackRow: 2)))!.Value;
        var plain = ActHit.From(Parse(Hit(10, Boss, 0x4834, Player1, damage: 500)))!.Value;

        Assert.Multiple(() =>
        {
            Assert.That(hit.Applies, Is.EqualTo(new uint[] { 695 }));
            Assert.That(hit.KnockbackRow, Is.EqualTo(2));
            Assert.That(plain.Applies, Is.Empty);
            Assert.That(plain.KnockbackRow, Is.Null);
        });
    }

    [Test]
    public void AChangeOfMusicReadsItsTrack()
        => Assert.That((ActMusic.From(Parse(Director(10, ActMusic.ChangeMusic, 723))), ActMusic.From(Parse(Director(10, ActLog.DirectorWipe)))),
                       Is.EqualTo(((ActMusic?)new ActMusic(Origin.AddSeconds(10), 723), (ActMusic?)null)));

    private static ActLine Parse(string text) => ActLine.Parse(text)!;
}
