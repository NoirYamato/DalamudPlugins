using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Scenarios.Top.TopP4Constants;
using Actions = AnoMech.Scenarios.Top.TopP4Actions;

namespace AnoMech.Scenarios.Top.P4BlueScreen;

// From 3 s before Omega's Wave Cannon cast to the last Wave Repeater ring; the Blue Screen DPS check isn't
// played. A mistake kills whoever made it on the spot, with the reason.
public sealed class TopP4BlueScreenScenario : IScenario
{
    public string Name => "Blue Screen";
    public IPhase Phase => TopZone.P4;

    public void DrawSettings() => settingsWindow.Draw();
    public bool HasPerPlayerSettings => true;
    public void DrawPerPlayerSettings() => settingsWindow.DrawPerPlayer();
    public object SettingsOverrides => settingsWindow.Overrides;
    public IReadOnlyList<string> SettingsConflicts => settingsWindow.Overrides.Validate().Problems;
    private readonly TopP4BlueScreenSettingsWindow settingsWindow = new();

    public IReadOnlyList<IScenarioAi> AiStrats => [new TopP4BlueScreenLpduAi()];

    private const string TwoBeams = "Hit by two Wave Cannon beams: each player's beam hits everyone in its line";
    private const string BothLineStacks = "Hit by both line stacks";

    // UNVERIFIED: Omega faces north for the whole phase; he has no auto-attacks here.
    private static readonly Placement OmegaSpot = new(Vector3.Zero, MathF.PI);

    private SimWorld world = null!;
    private SimParty party = null!;
    private TopP4BlueScreenState state = null!;

    private SimEnemy? omega;
    private SimEnemy? ringHelper;
    private readonly Dictionary<PartyRole, SimEnemy?> repeatHelpers = [];
    private readonly Dictionary<PartyRole, SimEnemy?> beamHelpers = [];
    private readonly List<SimEnemy?> stackHelpers = [];
    private readonly Dictionary<PartyRole, Placement> lines = [];

    public void Run(SimWorld worldParam, int? selectedAi)
    {
        world = worldParam;
        party = worldParam.Party;
        state = new TopP4BlueScreenState(world.Rng, party, settingsWindow.Overrides);
        omega = ringHelper = null;
        repeatHelpers.Clear();
        beamHelpers.Clear();
        stackHelpers.Clear();
        lines.Clear();
        if (selectedAi is { } idx && idx < AiStrats.Count)
            ((IScenarioAi<TopP4BlueScreenState>)AiStrats[idx]).Run(state, world);

        world.Events.Add(0f, SpawnOmegaAndHelpers);
        // Omega's teleport just before the cast (31558) would only move him onto the spot he already holds.
        world.Events.Add(3f, () => omega?.Cast(P4ActionId.WaveCannonCast));
        world.Events.Add(5.6f, () => MarkStackTargets(0));
        world.Events.Add(8f, AimEveryonesLine);
        world.Events.Add(8.6f, FireBeams);
        world.Events.Add(11f, () => ringHelper?.Cast(Actions.WaveRepeater, Vector3.Zero));
        world.Events.Add(13.3f, () => omega?.Cast(P4ActionId.WaveCannonVisual));
        world.Events.Add(13.6f, () => FireLineStacks(0));
        world.Events.Add(15.7f, () => MarkStackTargets(1));
        // UNVERIFIED: no log times the later aims and beams; each aim is 5.3 s before its repeats resolve and its
        // beams 0.6 s after it, as in round 1.
        world.Events.Add(17.9f, AimEveryonesLine);
        // UNVERIFIED: whether the instant rings, beams and line stacks show anything when an invisible helper
        // casts them.
        world.Events.Add(18.1f, () => ringHelper?.Cast(Actions.WaveRepeaterRing2));
        world.Events.Add(18.1f, () => omega?.Cast(P4ActionId.WaveCannonVisual));
        world.Events.Add(18.5f, FireBeams);
        world.Events.Add(20.1f, () => ringHelper?.Cast(Actions.WaveRepeaterRing3));
        world.Events.Add(22.1f, () => ringHelper?.Cast(Actions.WaveRepeaterRing4));
        world.Events.Add(23.2f, () => omega?.Cast(P4ActionId.WaveCannonVisual2));
        world.Events.Add(23.4f, () => FireLineStacks(1));
        world.Events.Add(25.6f, () => MarkStackTargets(2));
        world.Events.Add(28f, AimEveryonesLine);
        world.Events.Add(28.2f, () => ringHelper?.Cast(Actions.WaveRepeater, Vector3.Zero));
        world.Events.Add(28.2f, () => omega?.Cast(P4ActionId.WaveCannonVisual3));
        world.Events.Add(28.6f, FireBeams);
        world.Events.Add(33.3f, () => omega?.Cast(P4ActionId.WaveCannonVisual4));
        world.Events.Add(33.5f, () => FireLineStacks(2));
        world.Events.Add(35.2f, () => ringHelper?.Cast(Actions.WaveRepeaterRing2));
        world.Events.Add(37.2f, () => ringHelper?.Cast(Actions.WaveRepeaterRing3));
        world.Events.Add(39.2f, () => ringHelper?.Cast(Actions.WaveRepeaterRing4));
    }

    // Casts on an actor spawned the same frame play no animation, so everything that casts exists from the
    // start. A beam and a repeat each get their own helper: an instant cast from a helper whose own bar is
    // running is untested.
    private void SpawnOmegaAndHelpers()
    {
        omega = world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: TopP3Constants.OmegaP3,
            NameId: BNpcNameId.OmegaFinal,
            Level: 90,
            Targetable: true,
            Placement: OmegaSpot));
        ringHelper = SpawnHelper();
        foreach (var role in PerRole.All)
        {
            repeatHelpers[role] = SpawnHelper();
            beamHelpers[role] = SpawnHelper();
        }
        for (var i = 0; i < TopP4BlueScreenState.TargetsPerSet; i++)
            stackHelpers.Add(SpawnHelper());
    }

    private SimEnemy? SpawnHelper()
        => world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: BNpcBaseId.OmegaHelper,
            Targetable: false,
            EnemyList: EnemyListMode.Never,
            Visibility: SpawnVisibility.InvisibleHelper,
            Placement: new Placement(Vector3.Zero, 0f)));

    // UNVERIFIED: whether the marker shows when an invisible helper casts it.
    private void MarkStackTargets(int set)
    {
        var targets = state.StackTargets(set).List;
        for (var i = 0; i < targets.Length; i++)
            if (party.Get(targets[i]) is { } target && target.IsAlive())
                stackHelpers[i]?.Cast(P4ActionId.StackMarker, target);
    }

    // A directional cast at a character re-aims at it when it resolves, so each helper is turned to its
    // player and casts at nothing. UNVERIFIED: the lines start at the centre.
    private void AimEveryonesLine()
    {
        lines.Clear();
        foreach (var role in PerRole.All)
        {
            if (party.Get(role) is not { } member || !member.IsAlive()) continue;
            var line = LineFromCentreTo(member.Position);
            lines[role] = line;
            beamHelpers[role]?.SetPosition(line);
            repeatHelpers[role]?.SetPosition(line);
            repeatHelpers[role]?.Cast(Actions.WaveCannonRepeat);
        }
    }

    // UNVERIFIED: the beams fire down the lines fixed at the aim, not toward where the players stand now.
    // Every line is taken before any death, and a player who died since the aim fires nothing (in game that
    // line lands on someone else, but the death already failed the run).
    private void FireBeams()
    {
        var firing = lines.Where(l => party.Get(l.Key).IsAlive()).ToList();
        var hitTwice = firing.SelectMany(l => party.Find.InsideActionAoe(P4ActionId.WaveCannonBeam, l.Value))
                             .GroupBy(m => m)
                             .Where(g => g.Count() >= 2)
                             .Select(g => g.Key)
                             .ToList();
        foreach (var member in hitTwice)
            member.Die(P4ActionId.WaveCannonBeam, TwoBeams);
        foreach (var (role, _) in firing)
            beamHelpers[role]?.Cast(Actions.WaveCannonBeam);
    }

    // Each line runs from the centre toward its target where it stands now. UNVERIFIED: every rule below; the
    // game's damage is unknown.
    private void FireLineStacks(int set)
    {
        var targets = state.StackTargets(set).List;
        var stacks = Enumerable.Range(0, targets.Length)
            .Where(i => party.Get(targets[i]).IsAlive())
            .Select(i => (Helper: stackHelpers[i], Line: LineFromCentreTo(party.Get(targets[i])!.Position)))
            .Select(s => (s.Helper, s.Line, Hits: party.Find.InsideActionAoe(P4ActionId.LineStack, s.Line)))
            .ToList();

        foreach (var member in stacks.SelectMany(s => s.Hits).GroupBy(m => m).Where(g => g.Count() >= 2).Select(g => g.Key).ToList())
            member.Die(P4ActionId.LineStack, BothLineStacks);
        foreach (var (_, _, hits) in stacks.Where(s => s.Hits.Count < P4Rules.LineStackMinimum && !s.Hits.All(IsTank)))
            foreach (var member in hits)
                member.Die(P4ActionId.LineStack, TooFewInTheLine(hits.Count));

        foreach (var (helper, line, _) in stacks)
        {
            helper?.SetPosition(line);
            helper?.Cast(Actions.LineStack);
        }
    }

    private static string TooFewInTheLine(int count)
        => $"A line stack hit only {count} {(count == 1 ? "player" : "players")}: it needs {P4Rules.LineStackMinimum} to share it, or tanks alone";

    private static bool IsTank(SimCharacter member) => member is ISimPartyMember { Role: var role } && role.IsTank();

    private static Placement LineFromCentreTo(Vector3 position) => new Placement(Vector3.Zero, 0f).Face(position);
}
