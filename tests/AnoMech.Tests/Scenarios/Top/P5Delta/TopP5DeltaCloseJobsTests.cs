using AnoMech.Core.Game.Party;
using AnoMech.Multiplayer;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P5Delta;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Tests;

// The monitor and both Hello World tethers go only to the four close (blue) seats. Seats that refuse them must not
// fill the close four while a seat that would take them sits far, and settings no seating can honour are a conflict.
public class TopP5DeltaCloseJobsTests
{
    private static readonly IEnumerable<int> Seeds = Enumerable.Range(1, 1000);

    // Multiplayer fixtures leave their hosting session installed, which would make every seat real here too.
    private MultiplayerManager? leftInstalled;

    [SetUp]
    public void HostWithEverySeatReal()
    {
        leftInstalled = Plugin.Multiplayer;
        DalamudServices.Install(nameof(Plugin.Multiplayer), null!);
        PerRole.ForceSeats = true;
    }

    [TearDown]
    public void PutBackWhatWasInstalled()
    {
        PerRole.ForceSeats = false;
        DalamudServices.Install(nameof(Plugin.Multiplayer), leftInstalled!);
    }

    [Test]
    public void ThreeSeatsRefusingHelloWorldAreNeverHandedATether()
    {
        var overrides = new TopP5DeltaStateOverrides();
        foreach (var role in new[] { MeleeDpsA, MeleeDpsB, PhysRangedDps })
            overrides.HelloWorld[role] = HelloWorldOption.No;
        AssertEverySeedHonours(overrides);
    }

    [Test]
    public void SevenSeatsRefusingTheMonitorLeaveItToTheEighth()
    {
        var overrides = new TopP5DeltaStateOverrides();
        foreach (var role in PerRole.All.Where(role => role != CasterDps))
            overrides.Monitor[role] = false;
        AssertEverySeedHonours(overrides);
    }

    // The forced close seats all refuse Near, so a Near handed to whoever is left could take the Far request's seat.
    [Test]
    public void AFarRequestIsKeptWhenTheOtherCloseSeatsRefuseHelloWorld()
    {
        var overrides = new TopP5DeltaStateOverrides();
        overrides.HelloWorld[CasterDps] = HelloWorldOption.Far;
        overrides.BeyondDefence[OffTank] = true;
        overrides.HelloWorld[OffTank] = HelloWorldOption.No;
        overrides.Tether[PhysRangedDps] = PlayerTetherAssignment.CloseOuter;
        overrides.HelloWorld[PhysRangedDps] = HelloWorldOption.No;
        overrides.Monitor[MeleeDpsA] = false;
        overrides.HelloWorld[MeleeDpsA] = HelloWorldOption.No;
        overrides.Tether[MainTank] = PlayerTetherAssignment.FarInner;
        overrides.Tether[MeleeDpsB] = PlayerTetherAssignment.FarInner;
        AssertEverySeedHonours(overrides);
    }

    // The close four already holds takers for every job, so nobody moves and the roll stays the seed's.
    [Test]
    public void ARefusalTheCloseFourAlreadyHonoursMovesNobody()
    {
        var refusal = new TopP5DeltaStateOverrides();
        refusal.Monitor[MeleeDpsA] = false;
        refusal.HelloWorld[MeleeDpsA] = HelloWorldOption.No;
        Assert.Multiple(() =>
        {
            foreach (var seed in Seeds)
            {
                var plain = new TopP5DeltaState(new Rng(seed), new TopP5DeltaStateOverrides(), MainTank);
                var refused = new TopP5DeltaState(new Rng(seed), refusal, MainTank);
                Assert.That(refused.TetherOrder, Is.EqualTo(plain.TetherOrder), $"seed {seed}");
                Assert.That(refused.FistColors, Is.EqualTo(plain.FistColors), $"seed {seed}");
            }
        });
    }

    // M1, M2 and R1 take three of the close four, which leaves one close seat for two Hello World tethers.
    [Test]
    public void CloseSeatsRefusingHelloWorldThatLeaveOneTakerConflict()
    {
        var overrides = new TopP5DeltaStateOverrides();
        foreach (var role in new[] { MeleeDpsA, MeleeDpsB, PhysRangedDps })
        {
            overrides.Tether[role] = PlayerTetherAssignment.CloseAny;
            overrides.HelloWorld[role] = HelloWorldOption.No;
        }
        Assert.That(overrides.Validate().Problems,
                    Has.Count.EqualTo(1).And.Some.Contains("M1, M2, R1 refuse Hello World"));
    }

    [Test]
    public void FourCloseSeatsRefusingTheMonitorConflict()
    {
        var overrides = new TopP5DeltaStateOverrides();
        foreach (var role in new[] { MainTank, OffTank, RegenHealer, ShieldHealer })
        {
            overrides.Tether[role] = PlayerTetherAssignment.CloseAny;
            overrides.Monitor[role] = false;
        }
        Assert.That(overrides.Validate().Problems,
                    Has.Count.EqualTo(1).And.Some.Contains("MT, OT, H1, H2 refuse the monitor"));
    }

    // Four far tethers make M1, M2, R1 and R2 the close four, and only R2 takes Hello World.
    [Test]
    public void FarTethersThatLeaveTheRefusersCloseConflict()
    {
        var overrides = new TopP5DeltaStateOverrides();
        foreach (var role in new[] { MainTank, OffTank, RegenHealer, ShieldHealer })
            overrides.Tether[role] = PlayerTetherAssignment.FarAny;
        foreach (var role in new[] { MeleeDpsA, MeleeDpsB, PhysRangedDps })
            overrides.HelloWorld[role] = HelloWorldOption.No;
        Assert.That(overrides.Validate().Problems,
                    Has.Count.EqualTo(1).And.Some.Contains("M1, M2, R1 refuse Hello World"));
    }

    // One close slot is open. H2, M1 and M2 would take the Far tether and R1 and R2 the monitor, but nobody takes both.
    [Test]
    public void OneOpenCloseSlotForTheMonitorAndAHelloTetherConflicts()
    {
        var overrides = new TopP5DeltaStateOverrides();
        foreach (var role in new[] { MainTank, OffTank, RegenHealer })
        {
            overrides.Tether[role] = PlayerTetherAssignment.CloseAny;
            overrides.Monitor[role] = false;
            overrides.HelloWorld[role] = HelloWorldOption.No;
        }
        overrides.HelloWorld[MainTank] = HelloWorldOption.Near;
        foreach (var role in new[] { ShieldHealer, MeleeDpsA, MeleeDpsB })
            overrides.Monitor[role] = false;
        foreach (var role in new[] { PhysRangedDps, CasterDps })
            overrides.HelloWorld[role] = HelloWorldOption.No;
        Assert.That(overrides.Validate().Problems,
                    Has.Count.EqualTo(1).And.Some.Contains("refuse the monitor or Hello World"));
    }

    // Random settings that pass Validate, with refusals common: every request is honoured, and nobody is handed a
    // job they refused.
    [Test]
    public void EveryValidSettingIsHonoured()
    {
        var random = new Random(20261104);
        var valid = 0;
        for (var attempt = 0; attempt < 200_000 && valid < 3000; attempt++)
        {
            var overrides = RandomSettings(random);
            if (overrides.Validate().Any) continue;
            valid++;
            foreach (var seed in Enumerable.Range(1, 3))
            {
                var problems = Dropped(overrides, new TopP5DeltaState(new Rng(attempt * 7 + seed), overrides, MainTank));
                if (problems.Count > 0) Assert.Fail($"seed {attempt * 7 + seed}, {Describe(overrides)}: {string.Join("; ", problems)}");
            }
        }
        Assert.That(valid, Is.EqualTo(3000));
    }

    private static void AssertEverySeedHonours(TopP5DeltaStateOverrides overrides)
    {
        Assert.That(overrides.Validate().Problems, Is.Empty);
        Assert.Multiple(() =>
        {
            foreach (var seed in Seeds)
                Assert.That(Dropped(overrides, new TopP5DeltaState(new Rng(seed), overrides, MainTank)), Is.Empty, $"seed {seed}");
        });
    }

    private static List<string> Dropped(TopP5DeltaStateOverrides o, TopP5DeltaState state)
    {
        var order = state.TetherOrder.ToList();
        var problems = PerRole.All.Where(role => Band(o, role) is { } band && !band.Contains(order.IndexOf(role)))
                              .Select(role => $"{role} was seated outside its band at {order.IndexOf(role)}")
                              .ToList();
        foreach (var role in PerRole.All)
        {
            if (o.Monitor[role] == true && state.PlayerMonitorRole != role) problems.Add($"{role}'s monitor went to {state.PlayerMonitorRole}");
            if (o.HelloWorld[role] == HelloWorldOption.Near && state.NearWorldRole != role) problems.Add($"{role}'s Near went to {state.NearWorldRole}");
            if (o.HelloWorld[role] == HelloWorldOption.Far && state.FarWorldRole != role) problems.Add($"{role}'s Far went to {state.FarWorldRole}");
        }
        if (o.Monitor[state.PlayerMonitorRole] == false) problems.Add($"{state.PlayerMonitorRole} refused the monitor and got it");
        if (o.HelloWorld[state.NearWorldRole] is HelloWorldOption.No or HelloWorldOption.Far) problems.Add($"{state.NearWorldRole} refused Near and got it");
        if (o.HelloWorld[state.FarWorldRole] is HelloWorldOption.No or HelloWorldOption.Near) problems.Add($"{state.FarWorldRole} refused Far and got it");
        return problems;
    }

    // The slots a seat's choices leave it, as the State seats them.
    private static int[]? Band(TopP5DeltaStateOverrides o, PartyRole role)
    {
        var tether = o.Tether[role] ?? PlayerTetherAssignment.Auto;
        if (o.BeyondDefence[role] == true) tether = PlayerTetherAssignment.CloseInner;
        else if ((o.Monitor[role] == true || o.HelloWorld[role] is HelloWorldOption.Near or HelloWorldOption.Far)
                 && tether is PlayerTetherAssignment.Auto or PlayerTetherAssignment.FarAny or PlayerTetherAssignment.FarInner or PlayerTetherAssignment.FarOuter)
            tether = PlayerTetherAssignment.CloseAny;
        return tether switch
        {
            PlayerTetherAssignment.CloseInner => [0, 1],
            PlayerTetherAssignment.CloseOuter => [2, 3],
            PlayerTetherAssignment.CloseAny => [0, 1, 2, 3],
            PlayerTetherAssignment.FarInner => [4, 5],
            PlayerTetherAssignment.FarOuter => [6, 7],
            PlayerTetherAssignment.FarAny => [4, 5, 6, 7],
            _ => null,
        };
    }

    private static TopP5DeltaStateOverrides RandomSettings(Random random)
    {
        var overrides = new TopP5DeltaStateOverrides();
        var tethers = Enum.GetValues<PlayerTetherAssignment>();
        foreach (var role in PerRole.All)
        {
            if (random.Next(2) == 0) overrides.Tether[role] = tethers[random.Next(tethers.Length)];
            overrides.Monitor[role] = random.Next(8) switch { 0 => true, < 4 => false, _ => null };
            overrides.HelloWorld[role] = random.Next(10) switch
            {
                0 => HelloWorldOption.Near, 1 => HelloWorldOption.Far, < 5 => HelloWorldOption.No, 5 => HelloWorldOption.Auto, _ => null,
            };
            overrides.BeyondDefence[role] = random.Next(10) switch { 0 => true, < 3 => false, _ => null };
        }
        return overrides;
    }

    private static string Describe(TopP5DeltaStateOverrides o)
        => string.Join(", ", PerRole.All.Select(role => $"{role} {o.Tether[role]}/{o.Monitor[role]}/{o.HelloWorld[role]}/{o.BeyondDefence[role]}"));
}
