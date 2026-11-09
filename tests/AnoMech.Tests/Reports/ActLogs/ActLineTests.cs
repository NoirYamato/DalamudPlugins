using System.Numerics;
using static AnoMech.Tests.ActLogLines;

namespace AnoMech.Tests;

public class ActLineTests
{
    [Test]
    public void ALineReadsItsTypeAndTime()
    {
        var line = ActLine.Parse(Cast(1.5, 0x40000001, 0x7B55, 5f))!;

        Assert.That((line.Type, line.Time), Is.EqualTo((ActLineType.StartsCasting, Origin.AddSeconds(1.5))));
    }

    [Test]
    public void ATimeKeepsItsSevenFractionDigits()
    {
        var line = ActLine.Parse("21|2026-01-01T20:00:00.1234567+03:00|0")!;

        Assert.That(line.Time.Ticks % TimeSpan.TicksPerSecond, Is.EqualTo(1234567));
    }

    [TestCase("")]
    [TestCase("21")]
    [TestCase("ab|2026-01-01T20:00:00.0000000+03:00|0")]
    [TestCase("21|not a time|0")]
    public void AMalformedLineReadsAsNothing(string text)
        => Assert.That(ActLine.Parse(text), Is.Null);

    [Test]
    public void EveryNameFieldIsBlanked()
    {
        var lines = new[]
        {
            Hit(1, 0x40000001, 0x7B55, 0x10000001), Cast(1, 0x40000001, 0x7B55, 5f), Gains(1, 0xDC7, 27f, ActorIds.None, 0x10000001),
            Loses(1, 0xDC7, ActorIds.None, 0x10000001), Death(1, 0x10000001, 0x40000001), Player(1, 0x10000001, 24, Vector2.Zero),
            StatusList(1, 0x10000001, 1000, Vector2.Zero), Primary(1, 0x10000001), Memory(1, "Add", 0x10000001, ("Name", Name), ("PosX", "1.0")),
        };

        Assert.That(lines.Select(ActLine.Parse).SelectMany(l => Enumerable.Range(0, l!.FieldCount).Select(l.Text)), Has.None.EqualTo(Name));
    }

    [Test]
    public void FieldsReadAsHexDecimalFloatOrAPoint()
    {
        var line = ActLine.Parse(Line(21, 0, 34, (4, "7B55"), (24, "42"), (33, "-1.25"), (30, "100.50"), (31, "99.25")))!;

        Assert.Multiple(() =>
        {
            Assert.That(line.Hex(4), Is.EqualTo(0x7B55u));
            Assert.That(line.Int(24), Is.EqualTo(42));
            Assert.That(line.Float(33), Is.EqualTo(-1.25f));
            Assert.That(line.Point(30), Is.EqualTo(new Vector2(100.5f, 99.25f)));
        });
    }

    [Test]
    public void ABlankOrMissingFieldReadsAsNothing()
    {
        var line = ActLine.Parse(Line(21, 0, 4))!;

        Assert.Multiple(() =>
        {
            Assert.That(line.Hex(2), Is.Null);
            Assert.That(line.Float(40), Is.Null);
            Assert.That(line.Text(40), Is.Empty);
            Assert.That(line.Point(2), Is.Null);
        });
    }

    [Test]
    public void AMemoryLineReadsItsPairs()
    {
        var line = ActLine.Parse(Memory(0, "Change", 0x10000001, ("Heading", "1.5000"), ("PosX", "101.2500"), ("BNpcID", "1EB821")))!;

        Assert.Multiple(() =>
        {
            Assert.That(line.Pairs(), Is.EqualTo(new[] { ("Heading", "1.5000"), ("PosX", "101.2500"), ("BNpcID", "1EB821") }));
            Assert.That(line.PairFloat("PosX"), Is.EqualTo(101.25f));
            Assert.That(line.PairHex("BNpcID"), Is.EqualTo(2013217u));
            Assert.That(line.PairFloat("PosY"), Is.Null);
        });
    }
}
