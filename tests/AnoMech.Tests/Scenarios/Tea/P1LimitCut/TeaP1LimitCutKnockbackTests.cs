using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Scenarios.Tea;
using FFXIVClientStructs.FFXIV.Client.Game;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Tea.TeaP1Constants;
using static AnoMech.Tests.TeaP1LimitCutScenarioTests;
using Stage = AnoMech.Tests.TeaP1LimitCutStage;

namespace AnoMech.Tests;

// Cruise Chaser's knockbacks on the strat-free stage (TeaP1LimitCutStage), first blasters west and east. The player
// is a human from the start, one that presses its own buttons, and still stands where the stage puts it until just
// before its hit: M1 is #1, cleaved at 15.4 on the north spot, and R1 is #2, charged there at 16.9. Cruise Chaser
// stands 3 y counter-clockwise of #1, so the cleave pushes on clockwise and the charge throws back counter-clockwise.
public class TeaP1LimitCutKnockbackTests
{
    private const float Rim = P1Geometry.ArenaRadius;

    private static Stage HumanNumberOne => new() { HumanUntil = 15.0f };
    private static Stage HumanNumberTwo => new() { HumanUntil = 16.5f };

    private static Vector2 CruiseChaserSpot(Stage stage)
        => stage.HitSpot(1) - 3f * Stage.Facing(Stage.ClockwiseAlongTheRim(stage.HitSpot(1)));

    // From #2's own spot the rim is 13.5 y on.
    [Test]
    public void TheChargeThrowsAHumanAwayFromCruiseChaserAsFarAsTheRim()
    {
        var stage = HumanNumberTwo;
        var from = stage.HitSpot(2);
        var away = Vector2.Normalize(from - CruiseChaserSpot(stage));

        var landed = WhereThePlayerIs(stage, PhysRangedDps, at: 17.6f, stopAt: 17.8f);

        Assert.That(ToTheRim(from, away), Is.EqualTo(13.5f).Within(0.3f));
        Assert.That(Vector2.Distance(landed, from + ToTheRim(from, away) * away), Is.LessThan(0.05f), $"landed at {landed}");
        Assert.That(landed.Length(), Is.EqualTo(Rim).Within(0.05f));
    }

    // Charged toward the south-east from just inside the north spot, #2 has 25 y of floor ahead.
    [Test]
    public void AThrowWithRoomGoesTwentyYalms()
    {
        var stage = HumanNumberTwo;
        var from = new Vector2(0f, -11f);
        var away = Vector2.Normalize(from - CruiseChaserSpot(stage));

        var landed = WhereThePlayerIs(stage, PhysRangedDps, at: 17.6f, stopAt: 17.8f, new Takeover(16.5f, from));

        Assert.That(ToTheRim(from, away), Is.GreaterThan(20f));
        Assert.That(Vector2.Distance(landed, from + 20f * away), Is.LessThan(0.05f), $"landed at {landed}");
    }

    // Half a yalm inside the rim, with the rest of group A behind Cruise Chaser. It stays against the rim, alive,
    // until the run stops just before the next blast.
    [Test]
    public void AThrowStopsAtTheRim()
    {
        var stage = HumanNumberTwo;
        var from = Stage.OnRing(300f, Rim - 0.5f);
        var away = Vector2.Normalize(from - CruiseChaserSpot(stage));

        var landed = WhereThePlayerIs(stage, PhysRangedDps, at: 17.6f, stopAt: 18.8f, new Takeover(16.5f, from));

        Assert.That(ToTheRim(from, away), Is.InRange(0.5f, 1f));
        Assert.That(Vector2.Distance(landed, from + ToTheRim(from, away) * away), Is.LessThan(0.05f), $"landed at {landed}");
        Assert.That(landed.Length(), Is.EqualTo(Rim).Within(0.05f));
    }

    // On the rim already, there is nowhere to be thrown to.
    [Test]
    public void AHumanOnTheRimIsNotThrownThroughIt()
    {
        var from = Stage.OnRing(300f, Rim);

        var landed = WhereThePlayerIs(HumanNumberTwo, PhysRangedDps, at: 17.6f, stopAt: 17.8f, new Takeover(16.5f, from));

        Assert.That(Vector2.Distance(landed, from), Is.LessThan(0.05f), $"landed at {landed}");
    }

    [Test]
    public void TheCleavePushesAHumanFiveYalmsOnAlongItsFacing()
    {
        var stage = HumanNumberOne;
        var from = stage.HitSpot(1);

        var landed = WhereThePlayerIs(stage, MeleeDpsA, at: 16.2f, stopAt: 16.5f);

        Assert.That(Vector2.Distance(landed, from + 5f * Stage.Facing(Stage.ClockwiseAlongTheRim(from))), Is.LessThan(0.05f), $"landed at {landed}");
    }

    [TestCase(P1PlayerActionId.ArmsLength)]
    [TestCase(P1PlayerActionId.Surecast)]
    [TestCase(P1PlayerActionId.InnerRelease)]
    [TestCase(P1PlayerActionId.Berserk)]
    public void AnAntiKnockbackPressedBeforeTheChargeHoldsTheHumanInPlace(uint press)
    {
        var stage = HumanNumberTwo;

        var landed = WhereThePlayerIs(stage, PhysRangedDps, at: 17.6f, stopAt: 17.8f, pressAt: 14.0f, press: press);

        Assert.That(Vector2.Distance(landed, stage.HitSpot(2)), Is.LessThan(0.05f), $"landed at {landed}");
    }

    [TestCase(P1PlayerActionId.ArmsLength)]
    [TestCase(P1PlayerActionId.Surecast)]
    [TestCase(P1PlayerActionId.InnerRelease)]
    [TestCase(P1PlayerActionId.Berserk)]
    public void AnAntiKnockbackPressedBeforeTheCleaveHoldsTheHumanInPlace(uint press)
    {
        var stage = HumanNumberOne;

        var landed = WhereThePlayerIs(stage, MeleeDpsA, at: 16.2f, stopAt: 16.5f, pressAt: 14.0f, press: press);

        Assert.That(Vector2.Distance(landed, stage.HitSpot(1)), Is.LessThan(0.05f), $"landed at {landed}");
    }

    // Arm's Length and Surecast last 6 s and a warrior's Inner Strength 15, whether its button reports Inner Release
    // or the Berserk that became it; the charge lands at 16.9.
    [TestCase(P1PlayerActionId.ArmsLength, 11.1f, false)]
    [TestCase(P1PlayerActionId.ArmsLength, 10.7f, true)]
    [TestCase(P1PlayerActionId.Surecast, 11.1f, false)]
    [TestCase(P1PlayerActionId.Surecast, 10.7f, true)]
    [TestCase(P1PlayerActionId.InnerRelease, 2.1f, false)]
    [TestCase(P1PlayerActionId.InnerRelease, 1.7f, true)]
    [TestCase(P1PlayerActionId.Berserk, 2.1f, false)]
    [TestCase(P1PlayerActionId.Berserk, 1.7f, true)]
    public void AnAntiKnockbackHoldsForAsLongAsItsEffectLasts(uint press, float pressAt, bool thrown)
    {
        var stage = HumanNumberTwo;

        var landed = WhereThePlayerIs(stage, PhysRangedDps, at: 17.6f, stopAt: 17.8f, pressAt: pressAt, press: press);

        Assert.That(Vector2.Distance(landed, stage.HitSpot(2)) > 1f, Is.EqualTo(thrown), $"landed at {landed}");
    }

    [Test]
    public void AnAntiKnockbackPressedAfterTheHitIsTooLate()
    {
        var stage = HumanNumberTwo;

        var landed = WhereThePlayerIs(stage, PhysRangedDps, at: 17.6f, stopAt: 17.8f, pressAt: 17.0f, press: P1PlayerActionId.Surecast);

        Assert.That(landed.Length(), Is.EqualTo(Rim).Within(0.05f), $"landed at {landed}");
    }

    // The stage moves #1 on at 15.5 and #2 at 17.0.
    [Test]
    public void BotsHoldTheirSpotsThroughBothHits()
    {
        var stage = new Stage();
        Vector2? cleaved = null;
        Vector2? charged = null;
        var run = stage.Run(MainTank, stopAt: 17.5f, probe: p =>
        {
            if (p.Crossed(15.45f)) cleaved = Flat(p.Member(MeleeDpsA)!.Position);
            if (p.Crossed(16.95f)) charged = Flat(p.Member(PhysRangedDps)!.Position);
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(Vector2.Distance(cleaved!.Value, stage.HitSpot(1)), Is.LessThan(0.05f), $"#1 at {cleaved}");
        Assert.That(Vector2.Distance(charged!.Value, stage.HitSpot(2)), Is.LessThan(0.05f), $"#2 at {charged}");
    }

    // A headless run drives the player's seat as a bot until a test takes it over: it presses nothing and is not thrown.
    [TestCase(MeleeDpsA, 1, 15.45f)]
    [TestCase(PhysRangedDps, 2, 16.95f)]
    public void ThePlayersSeatDrivenAsABotIsNotThrown(PartyRole player, int number, float justAfterItsHit)
    {
        var stage = new Stage();
        Vector2? stands = null;
        var run = stage.Run(player, probe: p =>
        {
            if (p.Crossed(justAfterItsHit)) stands = Flat(p.Member(player)!.Position);
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(Vector2.Distance(stands!.Value, stage.HitSpot(number)), Is.LessThan(0.05f), $"#{number} at {stands}");
    }

    // Thrown to the rim 18 degrees past the west spot, 7.9 y from where the fifth pair's west blaster goes off at
    // 19.0. Dead at 20.0, it is not there for #3's cleave at 20.1.
    [Test]
    public void AThrownHumanWhoStaysWhereItLandedDiesToTheSecondRoundsFirstBlast()
    {
        var run = HumanNumberTwo.Run(PhysRangedDps);

        AssertFirstDeath(run, PhysRangedDps, P1ActionId.HawkBlaster, 20.0f, "Hawk Blasters go off unmarked");
    }

    // 11.2 y from the west blaster, and 25 y ahead of Cruise Chaser as he cleaves #3 on the south-west spot.
    [Test]
    public void AThrownHumanThatClearsTheBlastButNotTheCleaveDiesToTheSecondHit()
    {
        var run = HumanNumberTwo.Run(PhysRangedDps, takeovers: new Takeover(18.0f, new Vector2(-16f, -11f)));

        AssertFirstDeath(run, PhysRangedDps, P1ActionId.AlphaSword, 21.1f, TeaP1Actions.SecondCruiseChaserHit);
    }

    private static Vector2 WhereThePlayerIs(Stage stage, PartyRole player, float at, float stopAt, params Takeover[] takeovers)
        => WhereThePlayerIs(stage, player, at, stopAt, null, 0, takeovers);

    // Where the human stands at `at`, having pressed `press` at `pressAt` if given. Nobody may have died by `stopAt`.
    private static Vector2 WhereThePlayerIs(Stage stage, PartyRole player, float at, float stopAt, float? pressAt, uint press, params Takeover[] takeovers)
    {
        Vector2? stands = null;
        var run = stage.Run(player, stopAt, takeovers: takeovers, probe: p =>
        {
            if (pressAt is { } time && p.Crossed(time))
                p.Fake.UserActions.Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, press, 0));
            if (p.Crossed(at)) stands = Flat(p.Member(player)!.Position);
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(stands, Is.Not.Null, "the run reached the reading");
        return stands!.Value;
    }

    // How far `from` can go along `direction` before it reaches the rim.
    private static float ToTheRim(Vector2 from, Vector2 direction)
    {
        var along = Vector2.Dot(from, direction);
        return MathF.Sqrt(along * along - from.LengthSquared() + Rim * Rim) - along;
    }

    private static Vector2 Flat(Vector3 position) => new(position.X, position.Z);
}
