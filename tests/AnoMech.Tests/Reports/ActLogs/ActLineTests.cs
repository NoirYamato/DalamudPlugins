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

    private const uint Player1 = 0x10000001, Boss = 0x40000001;

    // A line of each type that carries a name, with the name where the network log puts it.
    private static IEnumerable<TestCaseData> LinesWithNames()
    {
        TestCaseData Case(string type, string line) => new TestCaseData(line).SetArgDisplayNames(type);
        yield return Case("02", Primary(1, Player1));
        yield return Case("03", Player(1, Player1, 24, Vector2.Zero));
        yield return Case("04", Removed(1, Player1, 24));
        yield return Case("20", Cast(1, Boss, 0x7B55, 5f));
        yield return Case("21", Hit(1, Boss, 0x7B55, Player1));
        yield return Case("22", Hit(1, Boss, 0x7B55, Player1, aoe: true));
        yield return Case("23", Line(23, 1, 8, (2, Hex(Player1)), (3, Name), (4, "DF"), (5, "Action"), (6, "Cancelled")));
        yield return Case("24", Line(24, 1, 31, (2, Hex(Player1)), (3, Name), (4, "DoT"), (5, "0"), (6, "1234"), (17, Hex(Boss)), (18, Name)));
        yield return Case("25", Death(1, Player1, Boss));
        yield return Case("26", Gains(1, 0xDC7, 27f, ActorIds.None, Player1));
        yield return Case("27", Line(27, 1, 11, (2, Hex(Player1)), (3, Name), (6, "017C")));
        yield return Case("30", Loses(1, 0xDC7, ActorIds.None, Player1));
        yield return Case("34", NameToggle(1, Boss, true));
        yield return Case("35", Line(35, 1, 13, (2, Hex(Player1)), (3, Name), (4, Hex(Boss)), (5, Name), (8, "0054")));
        yield return Case("37", EffectResult(1, Player1, 0x1A2B, 1000, Vector2.Zero));
        yield return Case("38", StatusList(1, Player1, 1000, Vector2.Zero));
        yield return Case("39", UpdateHp(1, Player1, 1000, Vector2.Zero));
        yield return Case("261", Memory(1, "Add", Player1, ("Name", Name), ("PosX", "1.0")));
    }

    [TestCaseSource(nameof(LinesWithNames))]
    public void EveryNameFieldIsBlanked(string text)
    {
        var line = ActLine.Parse(text)!;

        Assert.Multiple(() =>
        {
            Assert.That(text.Split('|'), Has.Some.EqualTo(Name));
            Assert.That(Enumerable.Range(0, line.FieldCount).Select(line.Text), Has.None.EqualTo(Name));
        });
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
