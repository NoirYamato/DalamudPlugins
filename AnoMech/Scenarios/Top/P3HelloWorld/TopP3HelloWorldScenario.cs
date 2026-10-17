using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Scenarios.Top.TopP3Constants;
using Actions = AnoMech.Scenarios.Top.TopP3Actions;

namespace AnoMech.Scenarios.Top.P3HelloWorld;

// A mistake kills whoever made it on the spot, with the reason, instead of the game's delayed
// wipes; only an unsoaked tower and an unbroken regression still take everyone.
public sealed class TopP3HelloWorldScenario : IScenario
{
    public string Name => "Hello World";
    public IPhase Phase => TopZone.P3;

    public void DrawSettings() => settingsWindow.Draw();
    public bool HasPerPlayerSettings => true;
    public void DrawPerPlayerSettings() => settingsWindow.DrawPerPlayer();
    public object SettingsOverrides => settingsWindow.Overrides;
    public IReadOnlyList<string> SettingsConflicts => settingsWindow.Overrides.Validate().Problems;
    private readonly TopP3HelloWorldSettingsWindow settingsWindow = new();

    public IReadOnlyList<IScenarioAi> AiStrats => [new TopP3HelloWorldLpduAi()];

    private const string TowerUnsoaked = "A tower was left unsoaked";
    private const string StackAlone = "Took the stack alone";
    private const string TwoStacks = "Hit by two stacks";
    private const string TwoDefamations = "Hit by two defamations";
    private const string OtherRotExplosion = "Caught in someone else's rot explosion";
    private const string TowerDebuffExpired =
        "Your tower debuff expired: soak only the tower of your rot's colour, and let your rot explode after";

    private SimWorld world = null!;
    private SimParty party = null!;
    private TopP3HelloWorldState state = null!;

    private SimEnemy? omega;
    private readonly List<(SimEnemy? Helper, HelloWorldColour Colour)> towers = [];
    private readonly Dictionary<SimCharacter, SimEnemy?> memberHelpers = [];
    private readonly Dictionary<SimCharacter, Debuffs> debuffs = [];
    private readonly List<SimTether> ghostTethers = [];
    private readonly List<Regression> regressions = [];
    private bool rotsLive;

    // What each member carries. Kept here rather than read off the statuses: a status that runs
    // out removes itself before the scenario can see that it expired.
    private sealed class Debuffs
    {
        public bool StackBug, DefamationBug, LatentSynchronizationBug, LatentDefect;
        public bool SynchronizationDebugger, OverflowDebugger;
        public readonly float?[] RotExpiry = new float?[2];
        public readonly float?[] TowerDebuffExpiry = new float?[2];
        public readonly bool[] RotDebugger = new bool[2];

        public bool HoldsAnyRot => RotExpiry.Any(e => e is not null);
        public bool HoldsAnyTowerDebuff => TowerDebuffExpiry.Any(e => e is not null);
    }

    private sealed record Regression(SimTether Tether, bool Local, float Deadline);

    public void Run(SimWorld worldParam, int? selectedAi)
    {
        world = worldParam;
        party = worldParam.Party;
        state = new TopP3HelloWorldState(world.Rng, party, settingsWindow.Overrides);
        omega = null;
        towers.Clear();
        memberHelpers.Clear();
        debuffs.Clear();
        ghostTethers.Clear();
        regressions.Clear();
        rotsLive = false;
        if (selectedAi is { } idx && idx < AiStrats.Count)
            ((IScenarioAi<TopP3HelloWorldState>)AiStrats[idx]).Run(state, world);

        world.Events.Add(0f, SpawnOmegaAndHelpers);
        world.Events.Add(0f, () => omega?.Cast(Actions.HelloWorld));
        world.Events.Add(5f, ApplyHelloWorldDebuffs);
        world.Events.Add(8f, () => TurnSmellsIntoBugs(firstResolve: 29.1f));
        world.Events.Add(19.1f, () => StartSet(1));
        world.Events.Add(28f, () => StartRegressions(1));
        world.Events.Add(29.1f, () => ResolveSet(nextResolve: 50.1f));
        world.Events.Add(31f, KillLatentDefectHolders);
        world.Events.Add(40.1f, () => StartSet(2));
        world.Events.Add(49f, () => StartRegressions(2));
        world.Events.Add(50.1f, () => ResolveSet(nextResolve: 71.2f));
        world.Events.Add(61.2f, () => StartSet(3));
        world.Events.Add(70f, () => StartRegressions(3));
        world.Events.Add(71.2f, () => ResolveSet(nextResolve: 92.5f));
        world.Events.Add(74f, KillLatentSynchronizationBugHolders);
        world.Events.Add(82.5f, () => StartSet(4));
        world.Events.Add(91f, () => StartRegressions(4));
        world.Events.Add(92.5f, () => ResolveSet(nextResolve: 113.8f));
        world.Events.Add(105.8f, () => omega?.Cast(Actions.CriticalError));
        world.Events.Add(113.8f, ResolveCriticalError);
    }

    public void Tick(float delta, float elapsed)
    {
        if (!rotsLive) return;
        var now = world.Events.Elapsed;
        ExplodeExpiredRots(now);
        KillExpiredTowerDebuffs(now);
        SpreadRotsByContact(now);
        TickRegressions(now);
    }

    private void SpawnOmegaAndHelpers()
    {
        omega = world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: OmegaP3,
            NameId: BNpcNameId.OmegaFinal,
            Level: 90,
            Targetable: true,
            EnemyList: EnemyListMode.Always,
            Visibility: SpawnVisibility.Visible,
            Placement: new Placement(Vector3.Zero, MathF.PI)));
        for (var i = 0; i < 4; i++)
            towers.Add((SpawnHelper(), HelloWorldColour.Red));
        foreach (var member in party.AllMembers())
            memberHelpers[member] = SpawnHelper();
    }

    private SimEnemy? SpawnHelper()
        => world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: BNpcBaseId.OmegaHelper,
            Targetable: false,
            EnemyList: EnemyListMode.Never,
            Visibility: SpawnVisibility.InvisibleHelper,
            Placement: new Placement(Vector3.Zero, 0f)));

    // Each pair's code smells run out as its regressions start, two sets apart (28 / 49 / 70 / 91).
    private static (float Remote, float Local) SmellSeconds(HelloWorldKit kit) => kit switch
    {
        HelloWorldKit.Defamation => (44f, 86f),
        HelloWorldKit.Stack => (86f, 44f),
        HelloWorldKit.RemoteFirst => (23f, 65f),
        _ => (65f, 23f),
    };

    private void ApplyHelloWorldDebuffs()
    {
        foreach (var kit in Enum.GetValues<HelloWorldKit>())
        foreach (var member in Members(state.WithKit(kit)))
        {
            var held = debuffs[member] = new Debuffs();
            var (remote, local) = SmellSeconds(kit);
            member.AddStatus(P3StatusId.RemoteCodeSmell, remote);
            member.AddStatus(P3StatusId.LocalCodeSmell, local);
            member.AddStatus(P3StatusId.LatentSynchronizationBug, P3Duration.LatentSynchronizationBug);
            held.LatentSynchronizationBug = true;
            switch (kit)
            {
                case HelloWorldKit.Defamation:
                    member.AddStatus(P3StatusId.OverflowCodeSmell, P3Duration.CodeSmell);
                    member.AddStatus(state.DefamationColour.RotSmellStatusId, P3Duration.CodeSmell);
                    break;
                case HelloWorldKit.Stack:
                    member.AddStatus(P3StatusId.SynchronizationCodeSmell, P3Duration.CodeSmell);
                    member.AddStatus(state.StackColour.RotSmellStatusId, P3Duration.CodeSmell);
                    break;
                case HelloWorldKit.LocalFirst:
                    member.AddStatus(P3StatusId.LatentDefect, P3Duration.LatentDefect);
                    held.LatentDefect = true;
                    break;
            }
        }
    }

    private void TurnSmellsIntoBugs(float firstResolve)
    {
        rotsLive = true;
        var untilResolve = firstResolve - world.Events.Elapsed;
        foreach (var member in Members(state.WithKit(HelloWorldKit.Defamation)))
        {
            member.RemoveStatus(P3StatusId.OverflowCodeSmell);
            member.RemoveStatus(state.DefamationColour.RotSmellStatusId);
            GiveDefamationBug(member, untilResolve);
            GiveRot(member, state.DefamationColour);
        }
        foreach (var member in Members(state.WithKit(HelloWorldKit.Stack)))
        {
            member.RemoveStatus(P3StatusId.SynchronizationCodeSmell);
            member.RemoveStatus(state.StackColour.RotSmellStatusId);
            GiveStackBug(member, untilResolve);
            GiveRot(member, state.StackColour);
        }
    }

    // Two adjacent towers of the defamation colour either side of the set's layout, two of the
    // stack colour opposite, all on the cardinals.
    private void StartSet(int set)
    {
        omega?.Cast(P3ActionId.LatentDefect);
        var layout = state.Layout(set);
        (float Degrees, HelloWorldColour Colour)[] spots =
        [
            (-45f, state.DefamationColour), (45f, state.DefamationColour),
            (135f, state.StackColour), (-135f, state.StackColour),
        ];
        for (var i = 0; i < towers.Count; i++)
        {
            var (helper, _) = towers[i];
            var (degrees, colour) = spots[i];
            towers[i] = (helper, colour);
            helper?.SetPosition(OnRing(layout, degrees, P3Geometry.TowerRingRadius));
            helper?.Cast(colour.TowerActionId);
        }
        TetherPair(state.WithRole(set, HelloWorldRole.Local), TetherId.HWPrepLocal, ghostTethers);
        TetherPair(state.WithRole(set, HelloWorldRole.Remote), TetherId.HWPrepRemote, ghostTethers);
    }

    private void TetherPair(IReadOnlyList<PartyRole> pair, ushort tetherId, List<SimTether> into)
    {
        if (party.Get(pair[0]) is not { } a || party.Get(pair[1]) is not { } b || !a.IsAlive() || !b.IsAlive()) return;
        into.Add(world.Tether(a, b, tetherId));
    }

    // The tethers carry no duration: the scenario owns the deadline and removes them itself.
    private void StartRegressions(int set)
    {
        ghostTethers.ForEach(tether => tether.Despawn());
        ghostTethers.Clear();
        StartRegression(state.WithRole(set, HelloWorldRole.Local), local: true);
        StartRegression(state.WithRole(set, HelloWorldRole.Remote), local: false);
    }

    private void StartRegression(IReadOnlyList<PartyRole> pair, bool local)
    {
        var tethers = new List<SimTether>();
        TetherPair(pair, local ? TetherId.HWLocal : TetherId.HWRemote, tethers);
        if (tethers.Count == 0) return;
        var tether = tethers[0];
        foreach (var member in new[] { tether.A, tether.B }.OfType<SimCharacter>())
        {
            member.RemoveStatus(local ? P3StatusId.LocalCodeSmell : P3StatusId.RemoteCodeSmell);
            member.AddStatus(RegressionStatus(local), P3Duration.Regression);
        }
        regressions.Add(new Regression(tether, local, world.Events.Elapsed + P3Duration.Regression));
    }

    private static ushort RegressionStatus(bool local) => local ? P3StatusId.LocalRegression : P3StatusId.RemoteRegression;

    // Every hit is taken from what was held just before the resolve, then applied.
    private void ResolveSet(float nextResolve)
    {
        var untilNext = nextResolve - world.Events.Elapsed;
        var soaks = towers.Where(t => t.Helper is not null)
                          .Select(t => (t.Helper, t.Colour, Soakers: party.Find.InsideActionAoe(t.Colour.TowerActionId, t.Helper!.Placement())))
                          .ToList();
        var stacks = Alive().Where(m => Held(m).StackBug)
                            .Select(m => (Holder: m, Hits: party.Find.InsideActionAoe(P3ActionId.Stack, m.Placement())))
                            .ToList();
        var defamations = Alive().Where(m => Held(m).DefamationBug)
                                 .Select(m => (Holder: m, Hits: party.Find.InsideActionAoe(P3ActionId.Defamation, m.Placement())))
                                 .ToList();
        var syncDebuggedBefore = Alive().Where(m => Held(m).SynchronizationDebugger).ToHashSet();
        var overflowDebuggedBefore = Alive().Where(m => Held(m).OverflowDebugger).ToHashSet();

        foreach (var (holder, _) in stacks) CastFromMemberHelper(holder, Actions.Stack, holder);
        foreach (var (holder, _) in defamations) CastFromMemberHelper(holder, Actions.Defamation, holder);

        if (soaks.FirstOrDefault(s => s.Soakers.Count == 0) is { Helper: var emptyTower, Colour: { } emptyColour })
        {
            emptyTower?.Cast(emptyColour.TowerUnsoakedActionId);
            party.WipeAllPlayers(emptyColour.TowerUnsoakedActionId, TowerUnsoaked);
            return;
        }

        foreach (var (holder, hits) in stacks.Where(s => s.Hits.Count < 2))
            holder.Die(P3ActionId.Stack, StackAlone);
        foreach (var member in HitAtLeastTwice(stacks.Select(s => s.Hits)))
            member.Die(P3ActionId.Stack, TwoStacks);
        foreach (var member in HitAtLeastTwice(defamations.Select(s => s.Hits)))
            member.Die(P3ActionId.Defamation, TwoDefamations);

        foreach (var (_, colour, soakers) in soaks)
        foreach (var member in soakers.Where(m => m.IsAlive()))
        {
            Held(member).TowerDebuffExpiry[colour.Index] = world.Events.Elapsed + P3Duration.TowerDebuff;
            member.AddStatus(colour.TowerDebuffStatusId, P3Duration.TowerDebuff);
        }

        var stackHolders = stacks.Select(s => s.Holder).ToHashSet();
        foreach (var holder in stackHolders.Where(m => m.IsAlive()))
        {
            Held(holder).StackBug = false;
            holder.RemoveStatus(P3StatusId.CriticalSynchronizationBug);
            Held(holder).SynchronizationDebugger = true;
            holder.AddStatus(P3StatusId.SynchronizationDebugger);
        }
        foreach (var member in stacks.SelectMany(s => s.Hits).Distinct().Where(m => m.IsAlive()))
        {
            Held(member).LatentSynchronizationBug = false;
            member.RemoveStatus(P3StatusId.LatentSynchronizationBug);
            if (!stackHolders.Contains(member) && !syncDebuggedBefore.Contains(member))
                GiveStackBug(member, untilNext);
        }

        var defamationHolders = defamations.Select(s => s.Holder).ToHashSet();
        foreach (var holder in defamationHolders.Where(m => m.IsAlive()))
        {
            Held(holder).DefamationBug = false;
            holder.RemoveStatus(P3StatusId.CriticalOverflowBug);
            Held(holder).OverflowDebugger = true;
            holder.AddStatus(P3StatusId.OverflowDebugger);
        }
        foreach (var member in defamations.SelectMany(s => s.Hits).Distinct().Where(m => m.IsAlive()))
        {
            Held(member).LatentDefect = false;
            member.RemoveStatus(P3StatusId.LatentDefect);
            if (!defamationHolders.Contains(member) && !overflowDebuggedBefore.Contains(member))
                GiveDefamationBug(member, untilNext);
        }
    }

    private static IEnumerable<SimCharacter> HitAtLeastTwice(IEnumerable<IReadOnlyList<SimCharacter>> hits)
        => hits.SelectMany(h => h).GroupBy(m => m).Where(g => g.Count() >= 2).Select(g => g.Key).ToList();

    private void KillLatentDefectHolders()
    {
        foreach (var member in Alive().Where(m => Held(m).LatentDefect).ToList())
        {
            Held(member).LatentDefect = false;
            CastFromMemberHelper(member, P3ActionId.LatentDefectExpired, member);
            member.Die(P3ActionId.LatentDefectExpired, "Latent Defect expired: a defamation has to hit you first");
        }
    }

    private void KillLatentSynchronizationBugHolders()
    {
        foreach (var member in Alive().Where(m => Held(m).LatentSynchronizationBug).ToList())
        {
            Held(member).LatentSynchronizationBug = false;
            CastFromMemberHelper(member, P3ActionId.LatentSynchronizationBugExpired, member);
            member.Die(P3ActionId.LatentSynchronizationBugExpired, "Latent Synchronization Bug expired: a stack has to hit you first");
        }
    }

    private void ResolveCriticalError()
    {
        foreach (var member in Alive().ToList())
        {
            var held = Held(member);
            var reason = !held.OverflowDebugger ? "no Overflow Debugger: a defamation of your own has to resolve first"
                : held.StackBug || held.DefamationBug ? "still holding a stack or a defamation"
                : held.HoldsAnyRot ? "still holding a rot"
                : held.HoldsAnyTowerDebuff ? "still holding a tower debuff"
                : null;
            if (reason is not null) member.Die(P3ActionId.CriticalError, reason);
        }
    }

    private void ExplodeExpiredRots(float now)
    {
        var explosions = new List<(SimCharacter Holder, HelloWorldColour Colour, IReadOnlyList<SimCharacter> Caught)>();
        foreach (var holder in Alive())
        foreach (var colour in HelloWorldColour.Both)
            if (Held(holder).RotExpiry[colour.Index] is { } expiry && now >= expiry)
                explosions.Add((holder, colour, party.Find.InsideActionAoe(RotExplosionActionId(colour), holder.Placement())
                                                     .Where(m => m != holder).ToList()));
        foreach (var (holder, colour, _) in explosions)
        {
            CastFromMemberHelper(holder, RotExplosion(colour), holder);
            var held = Held(holder);
            held.RotExpiry[colour.Index] = null;
            holder.RemoveStatus(colour.RotStatusId);
            held.RotDebugger[colour.Index] = true;
            holder.AddStatus(colour.DebuggerStatusId);
            held.TowerDebuffExpiry[colour.Index] = null;
            holder.RemoveStatus(colour.TowerDebuffStatusId);
        }
        foreach (var (_, colour, caught) in explosions)
        foreach (var member in caught)
            member.Die(RotExplosionActionId(colour), OtherRotExplosion);
    }

    private void KillExpiredTowerDebuffs(float now)
    {
        foreach (var member in Alive().ToList())
        foreach (var colour in HelloWorldColour.Both)
        {
            if (Held(member).TowerDebuffExpiry[colour.Index] is not { } expiry || now < expiry) continue;
            Held(member).TowerDebuffExpiry[colour.Index] = null;
            CastFromMemberHelper(member, colour.TowerDebuffExpiredActionId, member);
            member.Die(colour.TowerDebuffExpiredActionId, TowerDebuffExpired);
        }
    }

    // A copy, not a hand-over: the passer keeps theirs. Debuggers make a player immune to that colour.
    private void SpreadRotsByContact(float now)
    {
        var caught = new List<(SimCharacter Member, HelloWorldColour Colour, SimCharacter From)>();
        foreach (var holder in Alive())
        foreach (var colour in HelloWorldColour.Both)
        {
            if (Held(holder).RotExpiry[colour.Index] is null) continue;
            foreach (var other in party.Find.InsideCircle(holder.Position, P3Geometry.RotContactRadius))
                if (other != holder && Held(other) is var held && held.RotExpiry[colour.Index] is null && !held.RotDebugger[colour.Index])
                    caught.Add((other, colour, holder));
        }
        foreach (var (member, colour, from) in caught)
        {
            if (Held(member).RotExpiry[colour.Index] is not null) continue;
            Plugin.Log.Info($"[P3 Hello World] {Describe(member)} caught the {(colour == HelloWorldColour.Red ? "red" : "blue")} rot from {Describe(from)} at t={now:F2}.");
            GiveRot(member, colour);
        }
    }

    // A dead end leaves nothing to break; that death already failed the run.
    private void TickRegressions(float now)
    {
        foreach (var regression in regressions.ToList())
        {
            var tether = regression.Tether;
            if (SimTether.IsAnyDead(tether))
                EndRegression(regression);
            else if (regression.Local
                         ? tether.StretchLt(P3Geometry.LocalRegressionBreakDistance)
                         : tether.StretchGt(P3Geometry.RemoteRegressionBreakDistance))
            {
                Plugin.Log.Info($"[P3 Hello World] {(regression.Local ? "Local" : "Remote")} regression broken at t={now:F2}.");
                EndRegression(regression);
                foreach (var end in new[] { tether.A, tether.B }.OfType<SimCharacter>())
                    CastFromMemberHelper(end, TopActions.HwTetherBreak, null);
            }
            else if (now >= regression.Deadline)
            {
                EndRegression(regression);
                if (tether.A is { } a) CastFromMemberHelper(a, Actions.RegressionUnbroken, null);
            }
        }
    }

    private void EndRegression(Regression regression)
    {
        regressions.Remove(regression);
        regression.Tether.Despawn();
        foreach (var end in new[] { regression.Tether.A, regression.Tether.B }.OfType<SimCharacter>())
            end.RemoveStatus(RegressionStatus(regression.Local));
    }

    private void GiveRot(SimCharacter member, HelloWorldColour colour)
    {
        Held(member).RotExpiry[colour.Index] = world.Events.Elapsed + P3Duration.Rot;
        member.AddStatus(colour.RotStatusId, P3Duration.Rot);
    }

    private void GiveStackBug(SimCharacter member, float untilResolve)
    {
        Held(member).StackBug = true;
        member.AddStatus(P3StatusId.CriticalSynchronizationBug, untilResolve);
    }

    private void GiveDefamationBug(SimCharacter member, float untilResolve)
    {
        Held(member).DefamationBug = true;
        member.AddStatus(P3StatusId.CriticalOverflowBug, untilResolve);
    }

    private static EnemyAction RotExplosion(HelloWorldColour colour)
        => colour == HelloWorldColour.Red ? Actions.RedRotExplosion : Actions.BlueRotExplosion;

    private static uint RotExplosionActionId(HelloWorldColour colour) => RotExplosion(colour).ActionId;

    private void CastFromMemberHelper(SimCharacter member, EnemyAction action, SimCharacter? target)
    {
        if (memberHelpers.GetValueOrDefault(member) is not { } helper) return;
        helper.SetPosition(member.Position);
        helper.Cast(action, target);
    }

    private void CastFromMemberHelper(SimCharacter member, uint actionId, SimCharacter target)
    {
        if (memberHelpers.GetValueOrDefault(member) is not { } helper) return;
        helper.SetPosition(member.Position);
        helper.Cast(actionId, target);
    }

    private Debuffs Held(SimCharacter member)
    {
        if (!debuffs.TryGetValue(member, out var held)) debuffs[member] = held = new Debuffs();
        return held;
    }

    private IEnumerable<SimCharacter> Alive() => party.ActiveMembers();

    private IEnumerable<SimCharacter> Members(IEnumerable<PartyRole> roles)
        => roles.Select(party.Get).OfType<SimCharacter>().Where(m => m.IsAlive());

    private static string Describe(SimCharacter member) => (member as ISimPartyMember)?.Role.ToString() ?? "?";

    // Bearing `degrees` clockwise from the layout's direction.
    private static Vector3 OnRing(Direction layout, float degrees, float radius)
        => layout.RotateRad(degrees * MathF.PI / 180f).Apply(new Vector3(0f, 0f, -radius));
}
