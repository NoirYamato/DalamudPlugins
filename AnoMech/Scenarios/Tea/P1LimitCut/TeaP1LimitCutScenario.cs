using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Tea.TeaP1Constants;

namespace AnoMech.Scenarios.Tea.P1LimitCut;

// The intermission after Living Liquid, from 8 s before the first Hawk Blaster, with the party in the middle and
// no boss up, until J Kick; Brute Justice and Cruise Chaser turning targetable isn't played. A mistake kills
// whoever made it, with the reason.
public sealed class TeaP1LimitCutScenario : IScenario
{
    public string Name => "Limit Cut";
    public IPhase Phase => TeaZone.P1;
    public float BgmSecondsAtStart => P1Duration.LimitCutIntoTheTrack;

    public void DrawSettings() => settingsWindow.Draw();
    public bool HasPerPlayerSettings => true;
    public void DrawPerPlayerSettings() => settingsWindow.DrawPerPlayer();
    public object SettingsOverrides => settingsWindow.Overrides;
    public IReadOnlyList<string> SettingsConflicts => settingsWindow.Overrides.Validate().Problems;
    private readonly TeaP1LimitCutSettingsWindow settingsWindow = new();

    public IReadOnlyList<IScenarioAi> AiStrats => [new TeaP1LimitCutLpduAi()];

    private SimWorld world = null!;
    private SimParty party = null!;
    private TeaP1LimitCutState state = null!;
    private TeaP1Presses? presses;
    private SpawnedHelperPool helpers = null!;
    private EnemyAction alphaSword = null!;
    private EnemyAction superBlasstyCharge = null!;

    private SimEnemy? cruiseChaser;
    private SimEnemy? bruteJustice;
    private SimCharacter? landedBehind;
    private bool cruiseChaserShown;
    private bool aimed;

    internal int BusyHelperPeak => helpers.PeakBusy;

    public void Run(SimWorld worldParam, int? selectedAi)
    {
        world = worldParam;
        party = worldParam.Party;
        state = new TeaP1LimitCutState(world.Rng, party, settingsWindow.Overrides);
        presses?.Dispose();
        presses = new TeaP1Presses(world);
        alphaSword = TeaP1Actions.AlphaSword(presses);
        superBlasstyCharge = TeaP1Actions.SuperBlasstyCharge(presses);
        landedBehind = null;
        cruiseChaserShown = aimed = false;
        DiagnosticLog.Info($"[TeaP1LimitCut] Roll: first blasters {state.FirstBlasters}, numbers "
                           + string.Join(" ", state.Numbers.Select((role, k) => $"{k + 1}={role}")) + ".");
        if (selectedAi is { } idx && idx < AiStrats.Count)
            ((IScenarioAi<TeaP1LimitCutState>)AiStrats[idx]).Run(state, world);

        SpawnActors();

        for (var k = 0; k < state.Numbers.Count; k++)
        {
            var number = k + 1;
            world.Events.Add(5.9f + k * P1Duration.NumberSpacing, () => ShowNumber(number));
        }
        world.Events.Add(8.0f, () => HawkBlasterPair(0));
        world.Events.Add(10.2f, () => HawkBlasterPair(1));
        world.Events.Add(12.4f, () => HawkBlasterPair(2));
        world.Events.Add(14.6f, () => HawkBlasterPair(3));
        world.Events.Add(15.4f - P1Duration.CruiseChaserLandsBeforeAlphaSword, () => LandBehind(1));
        // The release plays along the model's facing, which a turn only reaches a flush later.
        world.Events.Add(15.3f, FaceWhomHeLandedBehind);
        world.Events.Add(15.4f, AlphaSword);
        world.Events.Add(16.8f, HawkBlasterInTheMiddle);
        world.Events.Add(16.8f, () => AimAt(2));
        world.Events.Add(16.9f, SuperBlasstyCharge);
        world.Events.Add(19.0f, () => HawkBlasterPair(4));
        world.Events.Add(20.1f - P1Duration.CruiseChaserLandsBeforeAlphaSword, () => LandBehind(3));
        world.Events.Add(20.0f, FaceWhomHeLandedBehind);
        world.Events.Add(20.1f, AlphaSword);
        world.Events.Add(21.2f, () => HawkBlasterPair(5));
        world.Events.Add(21.5f, () => AimAt(4));
        world.Events.Add(21.6f, SuperBlasstyCharge);
        world.Events.Add(23.5f, () => HawkBlasterPair(6));
        world.Events.Add(24.8f - P1Duration.CruiseChaserLandsBeforeAlphaSword, () => LandBehind(5));
        world.Events.Add(24.7f, FaceWhomHeLandedBehind);
        world.Events.Add(24.8f, AlphaSword);
        world.Events.Add(25.7f, () => HawkBlasterPair(7));
        world.Events.Add(26.2f, () => AimAt(6));
        world.Events.Add(26.3f, SuperBlasstyCharge);
        world.Events.Add(27.9f, HawkBlasterInTheMiddle);
        world.Events.Add(29.4f - P1Duration.CruiseChaserLandsBeforeAlphaSword, () => LandBehind(7));
        world.Events.Add(29.3f, FaceWhomHeLandedBehind);
        world.Events.Add(29.4f, AlphaSword);
        world.Events.Add(30.8f, () => AimAt(8));
        world.Events.Add(30.9f, SuperBlasstyCharge);
        world.Events.Add(34.1f, () => bruteJustice?.Cast(TeaP1Actions.JKick));
    }

    // Casts on an actor spawned the same frame play no animation, so everything that casts exists from the start.
    // UNVERIFIED: J Kick comes from an unseen helper under Brute Justice's name; he is not drawn.
    private void SpawnActors()
    {
        cruiseChaser = world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: P1BNpcBaseId.CruiseChaser,
            NameId: P1BNpcNameId.CruiseChaser,
            Level: Level,
            Targetable: false,
            EnemyList: EnemyListMode.Never,
            Visibility: SpawnVisibility.HiddenUntilShown,
            Placement: new Placement(Vector3.Zero, 0f)));
        bruteJustice = world.SpawnEnemy(Helper(P1BNpcNameId.BruteJustice));
        helpers = new SpawnedHelperPool(world, Helper(P1BNpcNameId.CruiseChaser), P1Rules.LimitCutHelpers);
        helpers.Spawn();
    }

    private static EnemySpawnConfig Helper(uint nameId) => new(
        BNpcBaseId: P1BNpcBaseId.Helper,
        NameId: nameId,
        Level: Level,
        Targetable: false,
        EnemyList: EnemyListMode.Never,
        Visibility: SpawnVisibility.InvisibleHelper,
        Placement: new Placement(Vector3.Zero, 0f));

    private void CastFromHelper(EnemyAction action, CastTarget target = default, Placement? at = null, byte animationVariation = 0)
        => helpers.Cast(action, target, at ?? new Placement(Vector3.Zero, 0f), animationVariation);

    private void ShowNumber(int number)
    {
        if (party.Get(state.Numbers[number - 1]) is { } member && member.IsAlive())
            member.AttachLockonVfx(P1LockonId.LimitCutNumbers[number - 1]);
    }

    private void HawkBlasterPair(int index)
    {
        var (a, b) = state.HawkBlasterPair(index);
        HawkBlasterAt(a);
        HawkBlasterAt(b);
    }

    private void HawkBlasterInTheMiddle() => HawkBlasterAt(Vector3.Zero);

    private void HawkBlasterAt(Vector3 centre) => CastFromHelper(TeaP1Actions.HawkBlaster, centre, new Placement(centre, 0f));

    // UNVERIFIED: a dead number's hit goes to any living player, each as likely.
    private SimCharacter? Target(int number)
    {
        if (party.Get(state.Numbers[number - 1]) is { } numbered && numbered.IsAlive()) return numbered;
        var alive = party.ActiveMembers().Where(m => m.IsAlive()).ToArray();
        return alive.Length == 0 ? null : state.PickRandom(alive);
    }

    // Behind is the reverse of the way the player faces as he lands. UNVERIFIED: that his model has the warp-in
    // this shows him with.
    private void LandBehind(int number)
    {
        landedBehind = null;
        if (cruiseChaser is null || Target(number) is not { } target) return;
        cruiseChaser.SetPosition(target.Placement().MoveForward(-state.CruiseChaserLandsBehind));
        landedBehind = target;
        if (cruiseChaserShown) return;
        cruiseChaser.PlayActionTimeline(ActionTimelineId.WarpEnd);
        cruiseChaserShown = true;
    }

    // UNVERIFIED: he cleaves through that player wherever it has stepped since he landed, not on along the facing
    // he landed with, so a number can't walk out of its own cleave.
    private void FaceWhomHeLandedBehind()
    {
        if (cruiseChaser is not null && landedBehind is { } target && target.IsAlive())
            cruiseChaser.SetPosition(cruiseChaser.Placement().Face(target.Position));
    }

    private void AlphaSword()
    {
        if (landedBehind is not null) cruiseChaser?.Cast(alphaSword);
    }

    // UNVERIFIED: he charges from where he cleaved and stays there.
    private void AimAt(int number)
    {
        aimed = false;
        if (landedBehind is null || cruiseChaser is null || Target(number) is not { } target) return;
        cruiseChaser.SetPosition(cruiseChaser.Placement().Face(target.Position));
        aimed = true;
    }

    private void SuperBlasstyCharge()
    {
        if (aimed) cruiseChaser?.Cast(superBlasstyCharge);
    }
}
