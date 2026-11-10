using System.Numerics;
using AnoMech.Core.UserActions;
using static AnoMech.Tests.ActLogLines;

namespace AnoMech.Tests;

// A boss's helper hits two players with one ability at 5 s.
public class LethalHitsTests
{
    private const uint Helper = 0x40000020, Player1 = 0x10000001, Player2 = 0x10000002;
    private const ushort Holmgang = 409;

    [Test]
    public void HitsThatKilledEveryoneTheyHitMatch()
    {
        var finding = Finding(EffectResult(5.6, Player1, 0x20, 0, Vector2.Zero), EffectResult(5.6, Player2, 0x20, 0, Vector2.Zero));

        Assert.That((finding.Measured, finding.Verdict), Is.EqualTo(("2 of 2 hits killed their target", Verdict.Matches)));
    }

    [Test]
    public void AHitSomeoneLivedThroughDiffers()
    {
        var finding = Finding(EffectResult(5.6, Player1, 0x20, 0, Vector2.Zero), EffectResult(5.6, Player2, 0x20, 15000, Vector2.Zero));

        Assert.Multiple(() =>
        {
            Assert.That((finding.Measured, finding.Verdict), Is.EqualTo(("1 of 2 hits killed their target", Verdict.Differs)));
            Assert.That(finding.Details, Has.Some.EndsWith("on Warrior: survived at 15000 HP"));
        });
    }

    [Test]
    public void AHitOnAPlayerUnderAnInvulnerabilityIsListedButLeftOut()
    {
        var finding = Finding(Gains(1, Holmgang, 10f, Player2, Player2), EffectResult(5.6, Player1, 0x20, 0, Vector2.Zero),
                              EffectResult(5.6, Player2, 0x20, 1, Vector2.Zero));

        Assert.Multiple(() =>
        {
            Assert.That((finding.Measured, finding.Verdict),
                        Is.EqualTo(("1 of 1 hits killed their target; 1 on a player under an invulnerability left out", Verdict.Matches)));
            Assert.That(finding.Details, Has.Some.EndsWith("on Warrior: under Holmgang"));
        });
    }

    private static LogFinding Finding(params string[] lines)
    {
        var pull = new ActPull
        {
            File = "Network_test.log", Number = 1, Start = Origin,
            Jobs = new() { [Player1] = (uint)JobId.WhiteMage, [Player2] = (uint)JobId.Warrior },
        };
        pull.Lines.AddRange(lines.Append(Hit(5, Helper, 0x7B50, Player1, sequence: 0x20, aoe: true))
                                 .Append(Hit(5, Helper, 0x7B50, Player2, sequence: 0x20, aoe: true))
                                 .Select(l => ActLine.Parse(l)!));
        var top = new TopPull(pull);
        var hits = new LethalHits();
        hits.Observe(top, top.Hits, _ => "a ring");
        return hits.Finding("Section", "Rings kill", "File.cs:1", "every hit kills");
    }
}
