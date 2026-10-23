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

namespace AnoMech.Scenarios.Top.P3Intermission;

// From the end of P2 until Omega turns targetable. A mistake kills whoever made it on the spot, with
// the reason; in game the void zone applies a lethal bleed instead.
public sealed class TopP3IntermissionScenario : IScenario
{
    public string Name => "Intermission";
    public IPhase Phase => TopZone.P3;

    public void DrawSettings() => settingsWindow.Draw();
    public bool HasPerPlayerSettings => true;
    public void DrawPerPlayerSettings() => settingsWindow.DrawPerPlayer();
    public object SettingsOverrides => settingsWindow.Overrides;
    public IReadOnlyList<string> SettingsConflicts => settingsWindow.Overrides.Validate().Problems;
    private readonly TopP3IntermissionSettingsWindow settingsWindow = new();

    public IReadOnlyList<IScenarioAi> AiStrats => [new TopP3IntermissionLpduAi()];

    private const string TwoCannons = "Hit by two Sniper Cannons";
    private const string StackAlone = "Took the High-powered Sniper Cannon alone";
    private const string StoodInTheVoidZone = "Stood in the void zone in the middle";

    // UNVERIFIED which kind stands where; both cast the same Colossal Blow. Each set gets both kinds.
    private static readonly float[] LeftArmBearings = [0f, 240f, 300f];

    private SimWorld world = null!;
    private SimParty party = null!;
    private TopP3IntermissionState state = null!;

    private SimEnemy? omega;
    private SimEnemy? firstWaveHelper;
    private SimEnemy? secondWaveHelper;
    private readonly List<(SimEnemy? Unit, float Bearing)> arms = [];
    private readonly Dictionary<SimCharacter, SimEnemy?> memberHelpers = [];
    private readonly List<SimCharacter> spreadHolders = [];
    private readonly List<SimCharacter> stackHolders = [];
    private SimEventObject? voidZone;
    private bool voidZoneLive;

    private sealed record Cannon(SimCharacter Holder, EnemyAction Action, ushort StatusId, IReadOnlyList<SimCharacter> Hits)
    {
        public bool IsStack => Action == Actions.HighPoweredSniperCannon;
    }

    public void Run(SimWorld worldParam, int? selectedAi)
    {
        world = worldParam;
        party = worldParam.Party;
        state = new TopP3IntermissionState(world.Rng, party, settingsWindow.Overrides);
        omega = firstWaveHelper = secondWaveHelper = null;
        arms.Clear();
        memberHelpers.Clear();
        spreadHolders.Clear();
        stackHolders.Clear();
        voidZone = null;
        voidZoneLive = false;
        if (selectedAi is { } idx && idx < AiStrats.Count)
            ((IScenarioAi<TopP3IntermissionState>)AiStrats[idx]).Run(state, world);

        world.Events.Add(0f, SpawnOmegaArmsAndHelpers);
        world.Events.Add(9.2f, ApplyFodder);
        world.Events.Add(9.3f, () => firstWaveHelper?.Cast(Actions.WaveRepeater, Vector3.Zero));
        // UNVERIFIED: both warp-in times.
        world.Events.Add(12.3f, () => WarpIn(state.FirstSetBearings));
        world.Events.Add(15.3f, SpawnVoidZone);
        world.Events.Add(15.4f, () => WarpIn(state.SecondSetBearings));
        // UNVERIFIED: whether the instant rings show anything when an invisible helper casts them.
        world.Events.Add(16.4f, () => firstWaveHelper?.Cast(Actions.WaveRepeaterRing2));
        world.Events.Add(17.4f, () => secondWaveHelper?.Cast(Actions.WaveRepeater, Vector3.Zero));
        world.Events.Add(18.4f, () => firstWaveHelper?.Cast(Actions.WaveRepeaterRing3));
        world.Events.Add(20.4f, () => firstWaveHelper?.Cast(Actions.WaveRepeaterRing4));
        world.Events.Add(24.4f, () => ColossalBlows(state.FirstSetBearings));
        world.Events.Add(24.5f, () => secondWaveHelper?.Cast(Actions.WaveRepeaterRing2));
        world.Events.Add(26.6f, () => secondWaveHelper?.Cast(Actions.WaveRepeaterRing3));
        world.Events.Add(26.9f, () => ColossalBlows(state.SecondSetBearings));
        world.Events.Add(28.3f, FireSniperCannons);
        world.Events.Add(28.7f, () => secondWaveHelper?.Cast(Actions.WaveRepeaterRing4));
        world.Events.Add(30.7f, DespawnVoidZone);
        world.Events.Add(32.3f, () => omega?.SetTargetable(true));
    }

    public void Tick(float delta, float elapsed)
    {
        if (!voidZoneLive) return;
        foreach (var member in party.Find.InsideCircle(Vector3.Zero, P3Geometry.VoidZoneRadius).Where(m => m.IsAlive()).ToList())
            member.Die(SimCharacterDeathExtensions.Environment, StoodInTheVoidZone);
    }

    // Casts on an actor spawned the same frame play no animation, so everything that casts exists
    // from the start. UNVERIFIED: Omega facing south and visible from the start.
    private void SpawnOmegaArmsAndHelpers()
    {
        omega = world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: OmegaP3,
            NameId: BNpcNameId.OmegaFinal,
            Level: 90,
            Targetable: false,
            EnemyList: EnemyListMode.Always,
            Visibility: SpawnVisibility.Visible,
            Placement: new Placement(Vector3.Zero, 0f)));
        firstWaveHelper = SpawnHelper();
        secondWaveHelper = SpawnHelper();
        foreach (var member in party.AllMembers())
            memberHelpers[member] = SpawnHelper();
        foreach (var bearing in new[] { 0f, 60f, 120f, 180f, 240f, 300f })
            arms.Add((SpawnArm(bearing), bearing));
    }

    private SimEnemy? SpawnHelper()
        => world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: BNpcBaseId.OmegaHelper,
            Targetable: false,
            EnemyList: EnemyListMode.Never,
            Visibility: SpawnVisibility.InvisibleHelper,
            Placement: new Placement(Vector3.Zero, 0f)));

    // UNVERIFIED: the arms face the centre, as P5's do.
    private SimEnemy? SpawnArm(float bearing)
    {
        var left = IsLeftArm(bearing);
        var position = OnRing(bearing, P3Geometry.ArmRadius);
        return world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: left ? BNpcBaseId.LeftArmUnit : BNpcBaseId.RightArmUnit,
            NameId: left ? BNpcNameId.LeftArmUnit : BNpcNameId.RightArmUnit,
            Level: 90,
            Targetable: false,
            EnemyList: EnemyListMode.Manual,
            Visibility: SpawnVisibility.HiddenUntilShown,
            LayoutId: left ? BNpcLayoutId.LeftArmUnit : BNpcLayoutId.RightArmUnit,
            Placement: new Placement(position, MathF.Atan2(-position.X, -position.Z))));
    }

    private static bool IsLeftArm(float bearing) => LeftArmBearings.Contains(bearing);

    private void ApplyFodder()
    {
        spreadHolders.AddRange(Members(state.With(SniperDebuff.Spread)));
        stackHolders.AddRange(Members(state.With(SniperDebuff.Stack)));
        foreach (var holder in spreadHolders)
            holder.AddStatus(P3StatusId.SniperCannonFodder, P3Duration.SniperCannonFodder);
        foreach (var holder in stackHolders)
            holder.AddStatus(P3StatusId.HighPoweredSniperCannonFodder, P3Duration.HighPoweredSniperCannonFodder);
    }

    private void WarpIn(IReadOnlyList<float> bearings)
    {
        foreach (var (unit, bearing) in Arms(bearings))
            unit.PlayActionTimeline(IsLeftArm(bearing) ? ActionTimelineId.WarpEnd2 : ActionTimelineId.WarpEnd);
    }

    private void ColossalBlows(IReadOnlyList<float> bearings)
    {
        foreach (var (unit, _) in Arms(bearings))
            unit.Cast(Actions.ColossalBlow);
    }

    private IEnumerable<(SimEnemy Unit, float Bearing)> Arms(IReadOnlyList<float> bearings)
        => arms.Where(a => a.Unit is not null && bearings.Contains(a.Bearing)).Select(a => (a.Unit!, a.Bearing));

    // UNVERIFIED: the spawn args, the radius and the times.
    private void SpawnVoidZone()
    {
        voidZone = world.SpawnEventObject(new EventObjectSpawnConfig
        {
            EObjId = P3EObjId.VoidZone,
            Placement = new Placement(Vector3.Zero, 0f),
            EventId = P3EObjId.VoidZoneEventId,
            TargetableStatus = 5,
            Arg2 = P3EObjId.VoidZoneArg2,
        });
        voidZoneLive = true;
    }

    private void DespawnVoidZone()
    {
        voidZoneLive = false;
        voidZone?.Despawn();
        voidZone = null;
    }

    // Kept on the holders the scenario recorded: a status that runs out removes itself before the
    // scenario can see that it expired. Every circle is taken before any death, and a holder who
    // already died fires nothing (in game it retargets, but that death already failed the run).
    // UNVERIFIED: the stacks going off with the spreads, and every rule below; the game's damage is unknown.
    private void FireSniperCannons()
    {
        var cannons = Alive(spreadHolders)
            .Select(h => new Cannon(h, Actions.SniperCannon, P3StatusId.SniperCannonFodder,
                                    party.Find.InsideActionAoe(ActionId.SniperCannon, h.Placement())))
            .Concat(Alive(stackHolders)
                .Select(h => new Cannon(h, Actions.HighPoweredSniperCannon, P3StatusId.HighPoweredSniperCannonFodder,
                                        party.Find.InsideActionAoe(ActionId.HighPoweredSniperCannon, h.Placement()))))
            .ToList();

        foreach (var cannon in cannons)
        {
            cannon.Holder.RemoveStatus(cannon.StatusId);
            CastFromMemberHelper(cannon.Holder, cannon.Action);
        }

        foreach (var cannon in cannons.Where(c => c.IsStack && c.Hits.Count < 2))
            cannon.Holder.Die(ActionId.HighPoweredSniperCannon, StackAlone);
        // A holder's own spread counts, so a spread holder in anyone's circle takes two. A stack of three is allowed.
        foreach (var hits in cannons.SelectMany(c => c.Hits.Select(member => (Member: member, Cannon: c)))
                                    .GroupBy(h => h.Member)
                                    .Where(g => g.Count() >= 2)
                                    .ToList())
            hits.Key.Die(hits.Any(h => !h.Cannon.IsStack) ? ActionId.SniperCannon : ActionId.HighPoweredSniperCannon, TwoCannons);
    }

    private void CastFromMemberHelper(SimCharacter member, EnemyAction action)
    {
        if (memberHelpers.GetValueOrDefault(member) is not { } helper) return;
        helper.SetPosition(member.Position);
        helper.Cast(action, member);
    }

    private static IEnumerable<SimCharacter> Alive(IEnumerable<SimCharacter> members) => members.Where(m => m.IsAlive()).ToList();

    private IEnumerable<SimCharacter> Members(IEnumerable<PartyRole> roles)
        => roles.Select(party.Get).OfType<SimCharacter>().Where(m => m.IsAlive());

    private static Vector3 OnRing(float bearingDegrees, float radius)
    {
        var radians = bearingDegrees * MathF.PI / 180f;
        return new Vector3(radius * MathF.Sin(radians), 0f, -radius * MathF.Cos(radians));
    }
}
