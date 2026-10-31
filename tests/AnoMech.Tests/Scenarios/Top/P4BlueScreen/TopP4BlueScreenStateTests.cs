using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P4BlueScreen;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Tests;

// The fight-wide "markers in one light party" row per set: Yes puts both markers in one of the party list's
// light parties (MT H1 M1 R1 / OT H2 M2 R2), which forces the strat's flex, No one in each. The seat rows
// still pick who within that.
public class TopP4BlueScreenStateTests
{
    private static readonly PartyRole[] LightParty1 = [MainTank, RegenHealer, MeleeDpsA, PhysRangedDps];
    private static readonly PartyRole[] LightParty2 = [OffTank, ShieldHealer, MeleeDpsB, CasterDps];
    private static readonly IEnumerable<int> Seeds = Enumerable.Range(1, 200);

    // Multiplayer fixtures leave their hosting session installed, which would make every seat real here too.
    private AnoMech.Multiplayer.MultiplayerManager? leftInstalled;

    [SetUp]
    public void Solo()
    {
        leftInstalled = Plugin.Multiplayer;
        DalamudServices.Install(nameof(Plugin.Multiplayer), null!);
    }

    [TearDown]
    public void PutBackWhatWasInstalled() => DalamudServices.Install(nameof(Plugin.Multiplayer), leftInstalled!);

    [Test]
    public void TheLightPartiesAreThePartyLists()
        => Assert.That(TopP4BlueScreenState.LightParties, Is.EqualTo(new[] { LightParty1, LightParty2 }));

    // Solo, the player's own row decides whether they are marked and the light-party row the pair's shape.
    [Test]
    public void SoloTheOwnRowAndTheLightPartyRowAreBothHonoured(
        [Values(null, true, false)] bool? mine, [Values(true, false)] bool oneLightParty)
    {
        Assert.Multiple(() =>
        {
            foreach (var seat in PerRole.All)
            {
                var party = PartyWithPlayerIn(seat);
                var overrides = new TopP4BlueScreenStateOverrides();
                for (var set = 0; set < TopP4BlueScreenState.Sets; set++)
                {
                    overrides.MarkerInSet(set).Mine = mine;
                    overrides.SetOneLightPartyInSet(set, oneLightParty);
                }
                foreach (var seed in Seeds)
                {
                    var state = new TopP4BlueScreenState(new Rng(seed), party, overrides);
                    for (var set = 0; set < TopP4BlueScreenState.Sets; set++)
                    {
                        var targets = state.StackTargets(set).List;
                        var why = $"{seat}, seed {seed}, set {set + 1}: {string.Join(" ", targets)}";
                        Assert.That(targets, Has.Length.EqualTo(2).And.Unique, why);
                        Assert.That(SameLightParty(targets), Is.EqualTo(oneLightParty), why);
                        if (mine is { } marked)
                            Assert.That(targets.Contains(seat), Is.EqualTo(marked), why);
                    }
                }
            }
        });
    }

    // Set 1: MT in and H1 out, so MT and M1 or R1. Set 2: MT and OT in, one in each. Set 3: MT, H1 and M1 out,
    // which leaves LP1 a single seat, so both go to LP2.
    [Test]
    public void HostingConsistentSeatPicksAndTheRowsAreHonoured()
        => Hosting(() =>
        {
            var overrides = new TopP4BlueScreenStateOverrides
            {
                OneLightPartyInSet1 = true,
                OneLightPartyInSet2 = false,
                OneLightPartyInSet3 = true,
            };
            overrides.MarkerInSet1[MainTank] = true;
            overrides.MarkerInSet1[RegenHealer] = false;
            overrides.MarkerInSet2[MainTank] = true;
            overrides.MarkerInSet2[OffTank] = true;
            foreach (var role in new[] { MainTank, RegenHealer, MeleeDpsA })
                overrides.MarkerInSet3[role] = false;

            Assert.That(overrides.Validate().Problems, Is.Empty);
            Assert.Multiple(() =>
            {
                foreach (var seed in Seeds)
                {
                    var state = new TopP4BlueScreenState(new Rng(seed), new SimParty(), overrides);
                    Assert.That(state.StackTargets(0).List, Has.Member(MainTank).And.Some.Matches<PartyRole>(r => r is MeleeDpsA or PhysRangedDps), $"seed {seed} set 1");
                    Assert.That(state.StackTargets(1).List, Is.EquivalentTo(new[] { MainTank, OffTank }), $"seed {seed} set 2");
                    Assert.That(state.StackTargets(2).List, Is.SubsetOf(LightParty2).And.Unique.And.Length.EqualTo(2), $"seed {seed} set 3");
                }
            });
        });

    // Set 1, Yes: MT and OT in; the earlier seat's light party wins and OT is dropped.
    // Set 2, No: MT and H1 in, both LP1; MT keeps it, and the other marker goes to LP2.
    // Set 3, No: all of LP2 out; one of them still takes LP2's marker.
    [Test]
    public void HostingConflictingSeatPicksResolveTowardTheEarlierSeat()
        => Hosting(() =>
        {
            var overrides = new TopP4BlueScreenStateOverrides
            {
                OneLightPartyInSet1 = true,
                OneLightPartyInSet2 = false,
                OneLightPartyInSet3 = false,
            };
            overrides.MarkerInSet1[MainTank] = true;
            overrides.MarkerInSet1[OffTank] = true;
            overrides.MarkerInSet2[MainTank] = true;
            overrides.MarkerInSet2[RegenHealer] = true;
            foreach (var role in LightParty2)
                overrides.MarkerInSet3[role] = false;

            Assert.Multiple(() =>
            {
                foreach (var seed in Seeds)
                {
                    var state = new TopP4BlueScreenState(new Rng(seed), new SimParty(), overrides);
                    var set1 = state.StackTargets(0).List;
                    var set2 = state.StackTargets(1).List;
                    var set3 = state.StackTargets(2).List;
                    Assert.That(set1, Has.Member(MainTank).And.Not.Member(OffTank).And.SubsetOf(LightParty1).And.Unique, $"seed {seed} set 1");
                    Assert.That(set2, Has.Member(MainTank).And.Not.Member(RegenHealer).And.Exactly(1).Matches<PartyRole>(LightParty2.Contains), $"seed {seed} set 2");
                    Assert.That(set3.Count(LightParty1.Contains), Is.EqualTo(1), $"seed {seed} set 3");
                    Assert.That(set3.Count(LightParty2.Contains), Is.EqualTo(1), $"seed {seed} set 3");
                }
            });
        });

    // Yes with three refusals in each light party: the one free seat of the light party picked is marked, with a
    // refuser beside it.
    [Test]
    public void HostingYesWithTooFewFreeSeatsStillMarksOneLightParty()
        => Hosting(() =>
        {
            var overrides = new TopP4BlueScreenStateOverrides { OneLightPartyInSet1 = true };
            foreach (var role in new[] { MainTank, RegenHealer, MeleeDpsA, OffTank, ShieldHealer, MeleeDpsB })
                overrides.MarkerInSet1[role] = false;

            Assert.Multiple(() =>
            {
                foreach (var seed in Seeds)
                {
                    var targets = new TopP4BlueScreenState(new Rng(seed), new SimParty(), overrides).StackTargets(0).List;
                    Assert.That(SameLightParty(targets), $"seed {seed}: {string.Join(" ", targets)}");
                    Assert.That(targets, Has.Some.Matches<PartyRole>(r => r is PhysRangedDps or CasterDps), $"seed {seed}");
                }
            });
        });

    [Test]
    public void AutoRowsRollAsThePlainBuilderDoes()
    {
        var party = PartyWithPlayerIn(MeleeDpsB);
        var overrides = new TopP4BlueScreenStateOverrides();
        overrides.MarkerInSet2.Mine = true;
        Assert.Multiple(() =>
        {
            foreach (var seed in Seeds)
            {
                var state = new TopP4BlueScreenState(new Rng(seed), party, overrides);
                var rng = new Rng(seed);
                for (var set = 0; set < TopP4BlueScreenState.Sets; set++)
                {
                    var plain = new RoleListBuilder
                    {
                        Size = TopP4BlueScreenState.TargetsPerSet,
                        Membership = set == 1 ? new Dictionary<PartyRole, bool> { [MeleeDpsB] = true } : null,
                    }.Build(rng, party);
                    Assert.That(state.StackTargets(set).List, Is.EqualTo(plain.List), $"seed {seed} set {set + 1}");
                }
            }
        });
    }

    [Test]
    public void SettingOneSetsRowLeavesTheOtherSetsAsTheSeedHadThem([Values(true, false)] bool oneLightParty)
    {
        var party = PartyWithPlayerIn(MainTank);
        Assert.Multiple(() =>
        {
            foreach (var seed in Seeds)
            {
                var auto = new TopP4BlueScreenState(new Rng(seed), party, new TopP4BlueScreenStateOverrides());
                var pinned = new TopP4BlueScreenState(new Rng(seed), party, new TopP4BlueScreenStateOverrides { OneLightPartyInSet2 = oneLightParty });
                Assert.That(pinned.StackTargets(0).List, Is.EqualTo(auto.StackTargets(0).List), $"seed {seed} set 1");
                Assert.That(pinned.StackTargets(2).List, Is.EqualTo(auto.StackTargets(2).List), $"seed {seed} set 3");
            }
        });
    }

    [TestCase(true, new[] { MainTank, OffTank }, new PartyRole[0], "set 1's markers both go to one light party")]
    [TestCase(true, new[] { MainTank }, new[] { RegenHealer, MeleeDpsA, PhysRangedDps }, "all refuse a marker in set 1, but set 1's markers both go to one light party")]
    [TestCase(true, new PartyRole[0], new[] { MainTank, RegenHealer, MeleeDpsA, OffTank, ShieldHealer, MeleeDpsB }, "all refuse a marker in set 1, but set 1's markers both go to one light party")]
    [TestCase(false, new[] { MainTank, RegenHealer }, new PartyRole[0], "set 1's markers go one to each light party")]
    [TestCase(false, new PartyRole[0], new[] { OffTank, ShieldHealer, MeleeDpsB, CasterDps }, "all refuse a marker in set 1, but set 1's markers go one to each light party")]
    public void ShapesTheSeatPicksCannotMeetConflict(bool oneLightParty, PartyRole[] wanted, PartyRole[] refused, string problem)
    {
        var overrides = new TopP4BlueScreenStateOverrides { OneLightPartyInSet1 = oneLightParty };
        foreach (var role in wanted) overrides.MarkerInSet1[role] = true;
        foreach (var role in refused) overrides.MarkerInSet1[role] = false;

        Hosting(() => Assert.That(overrides.Validate().Problems, Has.Some.Contains(problem)));
        Assert.That(overrides.Validate().Problems, Is.Empty, "solo");
    }

    [TestCase(true, new[] { OffTank }, new[] { MainTank, RegenHealer, MeleeDpsA })]
    [TestCase(true, new PartyRole[0], new[] { MainTank, RegenHealer, MeleeDpsA, OffTank, ShieldHealer })]
    [TestCase(false, new[] { MainTank, OffTank }, new[] { RegenHealer, MeleeDpsA, PhysRangedDps })]
    [TestCase(false, new[] { CasterDps }, new[] { MainTank, OffTank, ShieldHealer })]
    public void ShapesTheSeatPicksCanMeetDoNotConflict(bool oneLightParty, PartyRole[] wanted, PartyRole[] refused)
    {
        var overrides = new TopP4BlueScreenStateOverrides { OneLightPartyInSet2 = oneLightParty };
        foreach (var role in wanted) overrides.MarkerInSet2[role] = true;
        foreach (var role in refused) overrides.MarkerInSet2[role] = false;

        Hosting(() => Assert.That(overrides.Validate().Problems, Is.Empty));
    }

    // The bots clear the fight with every set flexing, and with none.
    [Test]
    public void TheBotsClearEverySetShape([Values(true, false)] bool oneLightParty, [Values(11, 23, 37, 41)] int seed)
    {
        var run = ScenarioRun.Execute(typeof(TopP4BlueScreenScenario), 0, seed, new ScenarioRunOptions
        {
            Overrides = o =>
            {
                var overrides = (TopP4BlueScreenStateOverrides)o;
                for (var set = 0; set < TopP4BlueScreenState.Sets; set++)
                    overrides.SetOneLightPartyInSet(set, oneLightParty);
            },
        });

        Assert.That(run.Passed, run.ToString);
    }

    private static bool SameLightParty(PartyRole[] targets)
        => targets.All(LightParty1.Contains) || targets.All(LightParty2.Contains);

    private static void Hosting(Action check)
    {
        PerRole.ForceSeats = true;
        try
        {
            check();
        }
        finally
        {
            PerRole.ForceSeats = false;
        }
    }

    // A party whose player sits in `seat`.
    private static SimParty PartyWithPlayerIn(PartyRole seat)
    {
        SimParty? party = null;
        var run = ScenarioRun.Execute(typeof(TopP4BlueScreenScenario), 0, 1, new ScenarioRunOptions
        {
            PlayerRole = seat,
            StopAt = 0.1f,
            Probe = p => party ??= p.World.Party,
        });
        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(party!.PlayerRole, Is.EqualTo(seat));
        return party;
    }
}
