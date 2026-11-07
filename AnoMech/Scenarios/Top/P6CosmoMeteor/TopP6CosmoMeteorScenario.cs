using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Scenarios.Top.TopP6Constants;
using Actions = AnoMech.Scenarios.Top.TopP6Actions;

namespace AnoMech.Scenarios.Top.P6CosmoMeteor;

// From 1.9 s before Alpha Omega's Cosmo Meteor cast to the flares and the stack; Magic Number isn't played. The adds have
// no HP: the comets die as the caster's limit break 3 lands and the meteors as the physical ranged's, whoever presses
// them, so their enrage never comes. A mistake kills whoever made it on the spot, with the reason.
public sealed class TopP6CosmoMeteorScenario : IScenario
{
    public string Name => "Cosmo Meteor";
    public IPhase Phase => TopZone.P6;

    public void DrawSettings() => settingsWindow.Draw();
    public bool HasPerPlayerSettings => true;
    public void DrawPerPlayerSettings() => settingsWindow.DrawPerPlayer();
    public object SettingsOverrides => settingsWindow.Overrides;
    public IReadOnlyList<string> SettingsConflicts => settingsWindow.Overrides.Validate().Problems;
    private readonly TopP6CosmoMeteorSettingsWindow settingsWindow = new();

    public IReadOnlyList<IScenarioAi> AiStrats => [new TopP6CosmoMeteorLpduAi()];

    internal const string InSomeoneElsesSpread =
        "Stood in another player's Cosmo Meteor spread: four land at once and the other four a second later, twice, each on everyone within 5 y of its target";
    internal const string FlareInTheStack =
        "Took Cosmo Meteor's stack holding a flare: the stack lands on a player without one, and every flare must stay out of it";
    internal static readonly string TooCloseToAFlare =
        $"Stood within {P6Geometry.FlareLethalWithin:F0} y of a flare: each lands on its marked player and hits everyone, harder the nearer they stand";

    internal static string TooFewInTheStack(int count)
        => $"Cosmo Meteor's unmarked stack had {count} in it: it needs {P6Rules.CosmoMeteorStack}";

    private static readonly float[] MeteorBearings = [0f, 180f];
    private static readonly float[] CometBearings = [45f, 90f, 135f, 225f, 270f, 315f];

    private SimWorld world = null!;
    private SimParty party = null!;
    private TopP6CosmoMeteorState state = null!;
    private TopP6AlphaOmega alphaOmega = null!;
    private readonly List<SimEnemy?> meteors = [];
    private readonly List<SimEnemy?> comets = [];

    // Brilliant Dynamis gives back every bar a limit break spends, so the gauge is full all phase.
    public void RunInstanceEvents(SimWorld instanceWorld) => instanceWorld.Party.LimitBreak.Set(3f);

    public void Run(SimWorld worldParam, int? selectedAi)
    {
        world = worldParam;
        party = worldParam.Party;
        state = new TopP6CosmoMeteorState(world.Rng, party, settingsWindow.Overrides);
        alphaOmega = new TopP6AlphaOmega(world);
        meteors.Clear();
        comets.Clear();
        if (selectedAi is { } idx && idx < AiStrats.Count)
            ((IScenarioAi<TopP6CosmoMeteorState>)AiStrats[idx]).Run(state, world);

        world.Events.Add(0f, alphaOmega.Spawn);
        world.Events.Add(1.0f, alphaOmega.ApplyCosmoMemoryStatuses);
        world.Events.Add(1.9f, () => alphaOmega.Cast(Actions.CosmoMeteor));
        world.Events.Add(6.9f, () => BaitedPuddles.Drop(party, Actions.CosmoMeteorBait, alphaOmega.CastFromHelper));
        // UNVERIFIED: when the adds show, targetable.
        world.Events.Add(9.3f, SpawnTheAdds);
        world.Events.Add(10.1f, ShowTheAdds);
        world.Events.Add(12.0f, () => LandTheSpreads(state.SpreadWave(0, 0)));
        world.Events.Add(13.0f, () => LandTheSpreads(state.SpreadWave(0, 1)));
        world.Events.Add(13.5f, () => Defeat(comets));
        world.Events.Add(18.0f, () => LandTheSpreads(state.SpreadWave(1, 0)));
        world.Events.Add(19.0f, () => LandTheSpreads(state.SpreadWave(1, 1)));
        world.Events.Add(21.9f, MarkTheFlares);
        world.Events.Add(24.0f, () => Defeat(meteors));
        world.Events.Add(30.0f, LandTheFlaresAndTheStack);
        world.Events.Add(31.0f, () => alphaOmega.Cast(Actions.CosmoMeteorFinish));
    }

    // The meteors north and south. UNVERIFIED: the comets on the other six compass points.
    private void SpawnTheAdds()
    {
        meteors.AddRange(MeteorBearings.Select(bearing => SpawnAdd(P6BNpcBaseId.CosmoMeteor, P6BNpcNameId.CosmoMeteor, bearing, P6Geometry.CosmoMeteorDistance)));
        comets.AddRange(CometBearings.Select(bearing => SpawnAdd(P6BNpcBaseId.CosmoComet, P6BNpcNameId.CosmoComet, bearing, P6Geometry.CosmoCometDistance)));
    }

    private SimEnemy? SpawnAdd(uint bnpcBaseId, uint nameId, float bearing, float distance)
        => world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: bnpcBaseId,
            NameId: nameId,
            Level: Level,
            Targetable: false,
            EnemyList: EnemyListMode.Manual,
            Visibility: SpawnVisibility.HiddenUntilPopIn,
            Placement: new Placement(TopP6AlphaOmega.AtBearing(bearing, distance), 0f).Face(Vector3.Zero)));

    private void ShowTheAdds()
    {
        foreach (var add in meteors.Concat(comets).OfType<SimEnemy>())
        {
            add.ActorControl.PopIn();
            add.SetTargetable(true);
            add.SetVisibleInEnemyList(true);
        }
    }

    private static void Defeat(IEnumerable<SimEnemy?> adds)
    {
        foreach (var add in adds.OfType<SimEnemy>().Where(add => add.IsAlive()))
            add.Defeat();
    }

    // UNVERIFIED: that a spread kills anyone but its target at once. In the game each hit leaves a vulnerability that
    // makes the next one lethal (TopP6Actions.CosmoMeteorSpread), and with every player hit once a set, a second apart,
    // a player in someone else's spread always takes a second.
    private void LandTheSpreads(PartyRole[] targets)
    {
        var hit = targets.Select(role => party.Get(role)).OfType<SimCharacter>().Where(member => member.IsAlive()).ToList();
        var inSomeoneElses = hit.SelectMany(target => party.Find.InsideActionAoe(P6ActionId.CosmoMeteorSpread, target.Placement())
                                                          .Where(member => member != target))
                                .Distinct()
                                .ToList();
        foreach (var member in inSomeoneElses)
            member.Die(P6ActionId.CosmoMeteorSpread, InSomeoneElsesSpread);
        foreach (var target in hit)
            alphaOmega.CastFromHelper(Actions.CosmoMeteorSpread, target);
    }

    private void MarkTheFlares()
    {
        foreach (var member in state.Flares.List.Select(role => party.Get(role)).OfType<SimCharacter>().Where(member => member.IsAlive()))
            member.AttachLockonVfx(P6LockonId.Flare);
    }

    // Decided from where everyone stands as they land, then shown. A player dead since the markers fires no flare, and a
    // dead stack target takes no stack. UNVERIFIED: every rule; the game's damage is unknown.
    private void LandTheFlaresAndTheStack()
    {
        var flares = state.Flares.List.Select(role => party.Get(role)).OfType<SimCharacter>().Where(member => member.IsAlive()).ToList();
        var stack = party.Get(state.StackTarget) is { } target && target.IsAlive() ? target : null;
        IReadOnlyList<SimCharacter> soakers = stack is null ? [] : party.Find.InsideActionAoe(P6ActionId.CosmoMeteorStack, stack.Placement());

        var verdicts = new Verdicts();
        foreach (var flare in flares.Where(soakers.Contains))
            verdicts.Kill(flare, P6ActionId.CosmoMeteorStack, FlareInTheStack);
        if (stack is not null && soakers.Count < P6Rules.CosmoMeteorStack)
            foreach (var soaker in soakers)
                verdicts.Kill(soaker, P6ActionId.CosmoMeteorStack, TooFewInTheStack(soakers.Count));
        foreach (var flare in flares)
            foreach (var member in party.Find.InsideCircle(flare.Position, P6Geometry.FlareLethalWithin).Where(member => member != flare))
                verdicts.Kill(member, P6ActionId.CosmoMeteorFlare, TooCloseToAFlare);
        verdicts.Deal();

        foreach (var flare in flares)
            alphaOmega.CastFromHelper(Actions.CosmoMeteorFlare, flare);
        if (stack is not null)
            alphaOmega.CastFromHelper(Actions.CosmoMeteorStack, stack);
    }
}
