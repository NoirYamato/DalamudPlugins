using AnoMech.Core.Game.Party;
using AnoMech.Multiplayer;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P5Delta;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Tests;

// Whether a seat forced the inner or outer tether of a colour ("Close" = blue, "Far" = green in the settings),
// which hands that colour's line-up to the roll instead of the strat's healer rule. It is read from the band a
// seat's other choices leave, as SeatTethers seats it: Beyond Defence is close inner, and a monitor or a Hello
// World tether turns any request into Close any.
public class TopP5DeltaStateTests
{
    private static readonly IEnumerable<int> Seeds = Enumerable.Range(1, 50);

    // Multiplayer fixtures leave their hosting session installed, which would make every seat real here too.
    private MultiplayerManager? leftInstalled;

    [SetUp]
    public void Solo()
    {
        leftInstalled = Plugin.Multiplayer;
        DalamudServices.Install(nameof(Plugin.Multiplayer), null!);
    }

    [TearDown]
    public void PutBackWhatWasInstalled() => DalamudServices.Install(nameof(Plugin.Multiplayer), leftInstalled!);

    [Test]
    public void AutoForcesNeitherColour()
    {
        Assert.Multiple(() =>
        {
            foreach (var seed in Seeds)
            {
                var state = new TopP5DeltaState(new Rng(seed), new TopP5DeltaStateOverrides(), MainTank);
                Assert.That(state.CloseInnerOuterForced, Is.False, $"seed {seed}");
                Assert.That(state.FarInnerOuterForced, Is.False, $"seed {seed}");
            }
        });
    }

    [TestCase(PlayerTetherAssignment.CloseInner, true, false)]
    [TestCase(PlayerTetherAssignment.CloseOuter, true, false)]
    [TestCase(PlayerTetherAssignment.CloseAny, false, false)]
    [TestCase(PlayerTetherAssignment.FarInner, false, true)]
    [TestCase(PlayerTetherAssignment.FarOuter, false, true)]
    [TestCase(PlayerTetherAssignment.FarAny, false, false)]
    public void SoloTheOwnTetherRowForcesItsColour(PlayerTetherAssignment mine, bool close, bool far)
    {
        var overrides = new TopP5DeltaStateOverrides();
        overrides.Tether.Mine = mine;
        Assert.Multiple(() =>
        {
            foreach (var seed in Seeds)
            {
                var state = new TopP5DeltaState(new Rng(seed), overrides, ShieldHealer);
                Assert.That(state.CloseInnerOuterForced, Is.EqualTo(close), $"seed {seed}");
                Assert.That(state.FarInnerOuterForced, Is.EqualTo(far), $"seed {seed}");
            }
        });
    }

    [Test]
    public void BeyondDefenceYesForcesTheBlues([Values] bool hosting)
    {
        var overrides = new TopP5DeltaStateOverrides();
        if (hosting) overrides.BeyondDefence[MeleeDpsB] = true;
        else overrides.BeyondDefence.Mine = true;
        var state = Make(hosting, overrides, MeleeDpsB);
        Assert.That(state.CloseInnerOuterForced, Is.True);
        Assert.That(state.FarInnerOuterForced, Is.False);
        Assert.That(Array.IndexOf(state.TetherOrder.ToArray(), MeleeDpsB), Is.LessThan(2), "seated close inner");
    }

    [Test]
    public void BeyondDefenceNoForcesNothing()
    {
        var overrides = new TopP5DeltaStateOverrides();
        overrides.BeyondDefence.Mine = false;
        var state = new TopP5DeltaState(new Rng(3), overrides, OffTank);
        Assert.That(state.CloseInnerOuterForced, Is.False);
    }

    // The monitor and both Hello World tethers are only taken from the blues, so a green request beside one of
    // them is resolved to Close any, which forces nothing.
    [TestCase(true, HelloWorldOption.Auto)]
    [TestCase(false, HelloWorldOption.Near)]
    [TestCase(false, HelloWorldOption.Far)]
    public void AGreenRequestTurnedCloseAnyForcesNothing(bool monitor, HelloWorldOption hello)
    {
        var overrides = new TopP5DeltaStateOverrides();
        overrides.Tether[CasterDps] = PlayerTetherAssignment.FarInner;
        if (monitor) overrides.Monitor[CasterDps] = true;
        overrides.HelloWorld[CasterDps] = hello;
        var state = Make(true, overrides, MainTank);
        Assert.That(state.FarInnerOuterForced, Is.False);
        Assert.That(state.CloseInnerOuterForced, Is.False);
    }

    // Hosting, any one seat's band is enough, and a colour nobody asked about stays free.
    [Test]
    public void HostingAnySeatForcesItsColourOnly([Values] PartyRole seat, [Values] bool blue)
    {
        var overrides = new TopP5DeltaStateOverrides();
        overrides.Tether[seat] = blue ? PlayerTetherAssignment.CloseOuter : PlayerTetherAssignment.FarInner;
        var state = Make(true, overrides, MainTank);
        Assert.That(state.CloseInnerOuterForced, Is.EqualTo(blue));
        Assert.That(state.FarInnerOuterForced, Is.EqualTo(!blue));
    }

    // Inner and outer requests are seated before Any ones, so two Any seats ahead in seat order can't take both
    // slots of the band a later seat forced, and every Any seat still lands in its colour.
    [TestCase(PlayerTetherAssignment.CloseInner, PlayerTetherAssignment.CloseAny, 0)]
    [TestCase(PlayerTetherAssignment.CloseOuter, PlayerTetherAssignment.CloseAny, 2)]
    [TestCase(PlayerTetherAssignment.FarInner, PlayerTetherAssignment.FarAny, 4)]
    [TestCase(PlayerTetherAssignment.FarOuter, PlayerTetherAssignment.FarAny, 6)]
    public void AForcedBandIsSeatedBehindTwoAnySeatsOfItsColour(PlayerTetherAssignment band, PlayerTetherAssignment any, int firstSlot)
    {
        var overrides = new TopP5DeltaStateOverrides();
        overrides.Tether[MainTank] = any;
        overrides.Tether[OffTank] = any;
        overrides.Tether[CasterDps] = band;
        AssertSeatedBehindTheAnySeats(overrides, firstSlot, firstSlot < 4 ? 0 : 4);
    }

    [Test]
    public void BeyondDefenceYesIsSeatedCloseInnerBehindTwoCloseAnySeats()
    {
        var overrides = new TopP5DeltaStateOverrides();
        overrides.Tether[MainTank] = PlayerTetherAssignment.CloseAny;
        overrides.Tether[OffTank] = PlayerTetherAssignment.CloseAny;
        overrides.BeyondDefence[CasterDps] = true;
        AssertSeatedBehindTheAnySeats(overrides, 0, 0);
    }

    private static void AssertSeatedBehindTheAnySeats(TopP5DeltaStateOverrides overrides, int forcedFirstSlot, int anyFirstSlot)
    {
        Assert.Multiple(() =>
        {
            foreach (var seed in Enumerable.Range(1, 1000))
            {
                var order = Make(true, overrides, MainTank, seed).TetherOrder.ToList();
                var why = $"seed {seed}: {string.Join(" ", order)}";
                Assert.That(order.IndexOf(CasterDps), Is.InRange(forcedFirstSlot, forcedFirstSlot + 1), why);
                Assert.That(order.IndexOf(MainTank), Is.InRange(anyFirstSlot, anyFirstSlot + 3), why);
                Assert.That(order.IndexOf(OffTank), Is.InRange(anyFirstSlot, anyFirstSlot + 3), why);
            }
        });
    }

    [Test]
    public void APinnedTetherOrderForcesNothing()
    {
        var overrides = new TopP5DeltaStateOverrides
        {
            TetherOrder = [MainTank, OffTank, RegenHealer, ShieldHealer, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps],
        };
        var state = Make(true, overrides, MainTank);
        Assert.That(state.CloseInnerOuterForced, Is.False);
        Assert.That(state.FarInnerOuterForced, Is.False);
    }

    // A party-setup choice, carried as set; it draws nothing, so every roll is the seed's either way.
    [Test]
    public void AutomarkersIsCarriedAsSetAndRollsNothing()
    {
        Assert.That(new TopP5DeltaStateOverrides().Automarkers, Is.EqualTo(Automarkers.On));
        Assert.Multiple(() =>
        {
            foreach (var seed in Seeds)
            {
                var on = new TopP5DeltaState(new Rng(seed), new TopP5DeltaStateOverrides(), MainTank);
                var off = new TopP5DeltaState(new Rng(seed), new TopP5DeltaStateOverrides { Automarkers = Automarkers.Off }, MainTank);
                Assert.That(on.Automarkers, Is.EqualTo(Automarkers.On));
                Assert.That(off.Automarkers, Is.EqualTo(Automarkers.Off));
                Assert.That(off.TetherOrder, Is.EqualTo(on.TetherOrder), $"seed {seed}");
                Assert.That(off.FistColors, Is.EqualTo(on.FistColors), $"seed {seed}");
                Assert.That(off.NearWorldRole, Is.EqualTo(on.NearWorldRole), $"seed {seed}");
            }
        });
    }

    private static TopP5DeltaState Make(bool hosting, TopP5DeltaStateOverrides overrides, PartyRole player, int seed = 7)
    {
        PerRole.ForceSeats = hosting;
        try
        {
            return new TopP5DeltaState(new Rng(seed), overrides, player);
        }
        finally
        {
            PerRole.ForceSeats = false;
        }
    }
}
