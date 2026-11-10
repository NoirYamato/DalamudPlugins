using System.Numerics;
using AnoMech.Core.UserActions;
using static AnoMech.Scenarios.Top.TopP4Constants;
using static AnoMech.Tests.ActLogLines;

namespace AnoMech.Tests;

// Omega's Wave Cannon cast starts at 0 s, the markers land at 2.5 s and the two line stacks go off at 10.5 s. A Warrior under
// Holmgang takes one line alone.
public class TopP4BlueScreenPullLogsTests
{
    private const uint Boss = 0x40000010, Marker1 = 0x40000021, Marker2 = 0x40000022, Line1 = 0x40000031, Line2 = 0x40000032;
    private const uint Warrior = 0x10000001;
    private const ushort Holmgang = 409;

    [Test]
    public void ALineOnAPlayerUnderAnInvulnerabilityIsLeftOutOfTheMinimum()
    {
        var finding = LineStackMinimum(secondLineHits: 1, secondLineKills: 1);

        Assert.Multiple(() =>
        {
            Assert.That(finding.Measured, Does.Contain("the largest line with a non-tank in it that killed hit 1, none was lived through, so the "
                                                       + "game's minimum lies at 2 or more"));
            Assert.That(finding.Measured, Does.Contain("1 target under an invulnerability left out"));
            Assert.That(finding.Verdict, Is.EqualTo(Verdict.Inconclusive));
        });
    }

    [Test]
    public void ALineOfFourThatKilledDiffers()
        => Assert.That(LineStackMinimum(secondLineHits: 4, secondLineKills: 1).Verdict, Is.EqualTo(Verdict.Differs));

    private static LogFinding LineStackMinimum(int secondLineHits, int secondLineKills)
    {
        uint Player(int i) => 0x10000001u + (uint)i;
        var lines = new List<string>
        {
            Cast(0, Boss, P4ActionId.WaveCannonCast, 4.7f), Gains(1, Holmgang, 10f, Warrior, Warrior),
            Hit(2.5, Marker1, P4ActionId.StackMarker, Warrior), Hit(2.5, Marker2, P4ActionId.StackMarker, Player(1)),
            Hit(10.5, Line1, P4ActionId.LineStack, Warrior, sequence: 0x31, aoe: true), EffectResult(11, Warrior, 0x31, 1, Vector2.Zero),
        };
        for (var i = 1; i <= secondLineHits; i++)
            lines.AddRange([
                Hit(10.5, Line2, P4ActionId.LineStack, Player(i), sequence: 0x32, aoe: true),
                EffectResult(11, Player(i), 0x32, i <= secondLineKills ? 0 : 20000, Vector2.Zero),
            ]);
        var pull = new ActPull
        {
            File = "Network_test.log", Number = 1, Start = Origin,
            Jobs = Enumerable.Range(1, 4).ToDictionary(Player, _ => (uint)JobId.Ninja),
        };
        pull.Jobs[Warrior] = (uint)JobId.Warrior;
        pull.Lines.AddRange(lines.Select(l => ActLine.Parse(l)!));
        var measures = new TopP4BlueScreenPullLogs();
        measures.Observe(new TopPull(pull));
        return measures.Findings().Single(f => f.Name.StartsWith("A line stack needs four", StringComparison.Ordinal));
    }
}
