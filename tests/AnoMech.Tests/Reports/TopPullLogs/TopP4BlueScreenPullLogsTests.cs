using System.Numerics;
using AnoMech.Core.UserActions;
using static AnoMech.Scenarios.Top.TopP4Constants;
using static AnoMech.Tests.ActLogLines;

namespace AnoMech.Tests;

// Omega's Wave Cannon cast starts at 0 s.
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

    [Test]
    public void ABeamThatFollowedItsWalkingPlayerMatches()
    {
        var beams = Beams(walked: 20, dancerBeamBearing: 110);

        Assert.Multiple(() =>
        {
            Assert.That(beams.Measured, Does.StartWith("1 of 2 beams went off within 0.5 degrees of one of their round's aimed lines; 1 left"));
            Assert.That(beams.Measured, Does.Contain(": 1 passed closer to their target where it stood as they hit than where it stood at the "
                                                     + "aim, 0 farther and 0 about as close"));
            Assert.That(beams.Verdict, Is.EqualTo(Verdict.Matches));
        });
    }

    [Test]
    public void ABeamThatLeftItsLineAwayFromItsPlayerDiffers()
        => Assert.That(Beams(walked: 10, dancerBeamBearing: 80).Verdict, Is.EqualTo(Verdict.Differs));

    [Test]
    public void BeamsOfPlayersStandingStillAreInconclusive()
    {
        var beams = Beams(walked: 0, dancerBeamBearing: 90);

        Assert.That((beams.Measured, beams.Verdict),
                    Is.EqualTo(("2 of 2 beams went off within 0.5 degrees of one of their round's aimed lines. 0 beams hit more than one player",
                                Verdict.Inconclusive)));
    }

    [Test]
    public void ABeamThatLeftItsLineWhileTheLogsShowItsPlayerStillIsInconclusive()
        => Assert.That(Beams(walked: 0, dancerBeamBearing: 100).Verdict, Is.EqualTo(Verdict.Inconclusive));

    // The round aims at 5 s and its beams go off at 5.54 s. A Ninja holds still 12 y north; a Dancer stands 12 y east at the
    // aim and walks the given degrees clockwise round the centre before its beam goes off toward the given bearing.
    private static LogFinding Beams(double walked, double dancerBeamBearing)
    {
        const uint Ninja = 0x10000002, Dancer = 0x10000003, RepeatN = 0x40000041, RepeatD = 0x40000042, BeamN = 0x40000051, BeamD = 0x40000052;
        static Vector2 At(double bearing)
            => new(100f + 12f * (float)Math.Sin(bearing * Math.PI / 180), 100f - 12f * (float)Math.Cos(bearing * Math.PI / 180));
        static float Heading(double bearing) => (float)Math.Atan2(Math.Sin(bearing * Math.PI / 180), -Math.Cos(bearing * Math.PI / 180));
        static ushort Raw(double bearing) => (ushort)Math.Round((Heading(bearing) + Math.PI) / (2 * Math.PI) * 65535);

        var centre = new Vector2(100f, 100f);
        var lines = new List<string>
        {
            Cast(0, Boss, P4ActionId.WaveCannonCast, 4.7f),
            StatusList(5, Ninja, 50000, At(0)), StatusList(5, Dancer, 50000, At(90)),
            Cast(5, RepeatN, P4ActionId.WaveCannonRepeat, 5.3f), CastAt(5, RepeatN, P4ActionId.WaveCannonRepeat, centre, Heading(0)),
            Cast(5, RepeatD, P4ActionId.WaveCannonRepeat, 5.3f), CastAt(5, RepeatD, P4ActionId.WaveCannonRepeat, centre, Heading(90)),
            Hit(5.54, BeamN, P4ActionId.WaveCannonBeam, Ninja, targetAt: At(0), rotation: Raw(0), aoe: true),
            Hit(5.54, BeamD, P4ActionId.WaveCannonBeam, Dancer, targetAt: At(90 + walked), rotation: Raw(dancerBeamBearing), aoe: true),
        };
        var pull = new ActPull
        {
            File = "Network_test.log", Number = 1, Start = Origin,
            Jobs = new() { [Ninja] = (uint)JobId.Ninja, [Dancer] = (uint)JobId.Dancer },
        };
        pull.Lines.AddRange(lines.Select(l => ActLine.Parse(l)!));
        var measures = new TopP4BlueScreenPullLogs();
        measures.Observe(new TopPull(pull));
        return measures.Findings().Single(f => f.Name.StartsWith("Each beam fires toward its player", StringComparison.Ordinal));
    }

    // The markers land at 2.5 s and the two line stacks go off at 10.5 s. A Warrior under Holmgang takes one line alone.
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
