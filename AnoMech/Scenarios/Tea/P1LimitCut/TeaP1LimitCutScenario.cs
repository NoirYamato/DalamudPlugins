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
    private SimCharacter? aimedAt;

    private static readonly Vector3 BruteJusticeLandsAt = new(-6f, 0f, 0f);

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
        landedBehind = aimedAt = null;
        DiagnosticLog.Info($"[TeaP1LimitCut] Roll: first blasters {state.FirstBlasters}, numbers "
                           + string.Join(" ", state.Numbers.Select((role, k) => $"{k + 1}={role}")) + ".");
        if (selectedAi is { } idx && idx < AiStrats.Count)
            ((IScenarioAi<TeaP1LimitCutState>)AiStrats[idx]).Run(state, world);

        SpawnActors();

        for (var k = 0; k < state.Numbers.Count; k++)
        {
            var number = k + 1;
            world.Events.Add(5.95f + k * P1Duration.NumberSpacing, () => ShowNumber(number));
        }
        world.Events.Add(8.0f, () => HawkBlasterPair(0));
        world.Events.Add(10.2f, () => HawkBlasterPair(1));
        world.Events.Add(12.4f, () => HawkBlasterPair(2));
        world.Events.Add(15.4f - P1Duration.CruiseChaserLandsBeforeAlphaSword, () => LandBehind(1));
        world.Events.Add(14.6f, () => HawkBlasterPair(3));
        // The release plays along the model's facing, which a turn only reaches a flush later, so each hit is turned
        // 0.1 s ahead for the model and again as it goes off for its area.
        world.Events.Add(15.3f, FaceWhomHeLandedBehind);
        world.Events.Add(15.4f, AlphaSword);
        world.Events.Add(16.8f, HawkBlasterInTheMiddle);
        world.Events.Add(16.8f, () => AimAt(2));
        world.Events.Add(16.9f, SuperBlasstyCharge);
        world.Events.Add(20.05f - P1Duration.CruiseChaserLandsBeforeAlphaSword, () => LandBehind(3));
        world.Events.Add(19.0f, () => HawkBlasterPair(4));
        world.Events.Add(19.95f, FaceWhomHeLandedBehind);
        world.Events.Add(20.05f, AlphaSword);
        world.Events.Add(21.2f, () => HawkBlasterPair(5));
        world.Events.Add(21.45f, () => AimAt(4));
        world.Events.Add(21.55f, SuperBlasstyCharge);
        world.Events.Add(23.45f, () => HawkBlasterPair(6));
        world.Events.Add(24.7f - P1Duration.CruiseChaserLandsBeforeAlphaSword, () => LandBehind(5));
        world.Events.Add(24.6f, FaceWhomHeLandedBehind);
        world.Events.Add(24.7f, AlphaSword);
        world.Events.Add(25.6f, () => HawkBlasterPair(7));
        world.Events.Add(26.1f, () => AimAt(6));
        world.Events.Add(26.2f, SuperBlasstyCharge);
        world.Events.Add(27.8f, HawkBlasterInTheMiddle);
        world.Events.Add(29.35f - P1Duration.CruiseChaserLandsBeforeAlphaSword, () => LandBehind(7));
        world.Events.Add(29.25f, FaceWhomHeLandedBehind);
        world.Events.Add(29.35f, AlphaSword);
        world.Events.Add(30.75f, () => AimAt(8));
        world.Events.Add(30.85f, SuperBlasstyCharge);
        world.Events.Add(34.05f, () => bruteJustice?.Cast(TeaP1Actions.JKick));
    }

    // Casts on an actor spawned the same frame play no animation, so everything that casts exists from the start.
    // Brute Justice casts J Kick himself in the fight, from where he lands. He isn't drawn here, so an unseen helper
    // under his name casts it from there.
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
        bruteJustice = world.SpawnEnemy(Helper(P1BNpcNameId.BruteJustice) with { Placement = new Placement(BruteJusticeLandsAt, 0f) });
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

    private void HawkBlasterAt(Vector3 centre) => helpers.Cast(TeaP1Actions.HawkBlaster, centre, new Placement(centre, 0f));

    // A dead number's hit goes to another living player: UNVERIFIED that each is as likely. Dead means fallen: a
    // number a Hawk Blaster killed less than its damage delay ago still draws its own hit.
    private SimCharacter? Target(int number)
    {
        if (party.Get(state.Numbers[number - 1]) is { } numbered && numbered.IsAlive()) return numbered;
        var alive = party.ActiveMembers().Where(m => m.IsAlive()).ToArray();
        return alive.Length == 0 ? null : state.PickRandom(alive);
    }

    // Behind is the reverse of the way the player faces as he lands. The fight plays the same timeline on him at
    // every landing; with a warp-in at the first landing alone the client left him unseen at the later ones.
    private void LandBehind(int number)
    {
        landedBehind = null;
        if (cruiseChaser is null || Target(number) is not { } target) return;
        cruiseChaser.SetPosition(target.Placement().MoveForward(-state.CruiseChaserLandsBehind));
        landedBehind = target;
        cruiseChaser.PlayActionTimeline(ActionTimelineId.WarpEnd);
    }

    // UNVERIFIED: he cleaves through that player wherever it has stepped since he landed, not on along the facing
    // he landed with, so a number can't walk out of its own cleave.
    private void FaceWhomHeLandedBehind() => Face(landedBehind);

    private void AlphaSword()
    {
        if (landedBehind is null) return;
        Face(landedBehind);
        cruiseChaser?.Cast(alphaSword);
    }

    // He charges from where he cleaved and stays there.
    private void AimAt(int number)
    {
        aimedAt = landedBehind is null ? null : Target(number);
        Face(aimedAt);
    }

    private void SuperBlasstyCharge()
    {
        if (aimedAt is null) return;
        Face(aimedAt);
        cruiseChaser?.Cast(superBlasstyCharge);
    }

    private void Face(SimCharacter? target)
    {
        if (cruiseChaser is not null && target is not null && target.IsAlive())
            cruiseChaser.SetPosition(cruiseChaser.Placement().Face(target.Position));
    }
}
