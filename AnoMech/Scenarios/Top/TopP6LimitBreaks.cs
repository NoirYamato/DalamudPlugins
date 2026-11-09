using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using FFXIVClientStructs.FFXIV.Client.Game;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Scenarios.Top.TopP6Constants;

namespace AnoMech.Scenarios.Top;

public enum TopP6LimitBreakKind
{
    Tank,
    Healer,
    Melee,
    PhysicalRanged,
    Caster,
}

public enum TopP6TankCover
{
    Holds,
    TooLate,
    Missing,
}

// One limit break pressed in a P6 run, by the human (heard through IUserActions.Acted) or by the run itself for a seat
// the human doesn't press for. Times are on the run's clock.
public sealed class TopP6LimitBreakPress
{
    public required PartyRole Seat { get; init; }
    public required bool ByHuman { get; init; }
    public required uint Job { get; init; }
    public required uint ActionId { get; init; }

    // 1-3 when it is the job's own limit break at that level, else 0.
    public required int Level { get; init; }
    public required TopP6LimitBreakKind Kind { get; init; }

    // The GameObjectId it was aimed at; 0 for none.
    public required ulong TargetId { get; init; }
    public required float CastStartedAt { get; init; }
    public required float CastSeconds { get; init; }
    public float? ResolvedAt { get; internal set; }
    public float? InterruptedAt { get; internal set; }

    // When its cast bar ends. The human's resolve reaches the run a frame or two after that, so a deadline its cast bar
    // meets is judged by this, not by ResolvedAt.
    public float LandsAt => CastStartedAt + CastSeconds;

    // Where its user stood as it resolved, facing its target, or the way it faced without one.
    public Placement? ResolvedFrom { get; internal set; }

    // Where a ground-targeted one goes down: where the human's client placed it, or on the target of one the run presses.
    // Null for any other, and for a human's whose client reported no point within its reach.
    public Vector3? GroundPoint { get; init; }

    public bool IsCasting => ResolvedAt is null && InterruptedAt is null;

    public bool Is(TopP6LimitBreakKind kind, int level = 3) => Kind == kind && Level == level;

    // A melee limit break 3 aimed at `targetId` whose cast bar ends from `from` until before `before`, whenever its resolve
    // is heard.
    public bool LandsAMeleeLimitBreak3On(ulong targetId, float from, float before)
        => Is(TopP6LimitBreakKind.Melee) && TargetId == targetId && LandsAt >= from && LandsAt < before;
}

// A slot the melee limit break 3 of the human's seat must fill, aimed at the window's target and resolved: started between
// OpensAt and LatestPress, or, with LandsBy, landing from OpensAt until before LandsBy however early it started
// (LandsAMeleeLimitBreak3On), LatestPress then being the last start that lands in time. With AfterAnotherMeleeLimitBreak3
// it opens instead once another seat's melee limit break 3 has resolved at or after OpensAt.
public sealed record TopP6MeleeWindow(PartyRole Seat, float OpensAt, float LatestPress, string Rule, bool AfterAnotherMeleeLimitBreak3 = false,
    float? LandsBy = null);

// The limit breaks of one P6 run: what everyone presses, the gauge they share, and the presses the run makes for the
// seats no human presses for. Construct one per run, as the run starts (it hears the IUserActions installed then),
// dispose of it with the run, and Tick it once a frame from the scenario's Tick. The human's presses arrive outside the
// sim's tick and before the client spends the gauge, so they are settled on the next Tick, or sooner if an event asks.
//
// The gauge is three bars. A limit break spends its level's bars as it resolves; one whose user holds Brilliant
// Dynamis gets them back at once, and the status becomes Spark of Dynamis, which gives nothing back. Death takes both.
// The party's own generation only runs where Rebuild says, so a wasted press leaves a later one short.
public sealed class TopP6LimitBreaks : IDisposable
{
    public const int Bars = 3;
    private const int UnitsPerBar = 10000;
    private const int FullUnits = Bars * UnitsPerBar;

    // The HUD gauge is resent once the gauge has moved this far, so a rebuild doesn't send one every frame.
    private const int HudStepUnits = 100;

    // A bot held still by its cast walks on to where it was sent at AiManager's run speed.
    private const float RunSpeed = 6.5f;

    // How long before a melee window opens a press still counts toward explaining a miss.
    private const float EarlyPressLookback = 10f;

    // How often an open melee window looks whether it has been filled.
    private const float WindowWatchStep = 1f;

    // The client's stand-in for "the player's current target". UNVERIFIED that a press reaches Acted with it rather than
    // its target's own id; one that does is read as aimed at the player's target as it is heard.
    private const uint CurrentTarget = 0xE0000000;

    // A caster's limit break goes down within 25 y of its user. UNVERIFIED: whether the client measures that from the
    // player's centre; the extra yalm covers its hitbox and a frame's walk since the sim last read where it stands.
    private const float GroundPointReach = 26f;

    public const string TakesHoldTooLate = "; one pressed within {0:0.#} s of the hit takes hold too late";
    public const string GaugeWasShort =
        "; the limit break gauge was short: one spent by a player without Brilliant Dynamis is never given back";

    public const string FirstDiveMeleeRule =
        "The first melee limit break 3 goes on Alpha Omega right after Cosmo Dive's cast bar: any later and the second can't finish before Cosmo Meteor's baits";
    public const string SecondDiveMeleeRule =
        "The second melee limit break 3 goes on Alpha Omega as soon as the first one's refill fills the gauge, early enough to be free before Cosmo Meteor's baits";
    public const string EnrageMeleeRule =
        "The Gauntlet's last melee limit break 3 must land on Alpha Omega after Run: ****mi*'s cast starts and before its damage lands";

    public const string YoursWasCutOff = "; yours was cut off";
    public const string YoursMissedAlphaOmega = "; yours wasn't aimed at Alpha Omega";
    public const string YoursStartedTooEarly = "; yours started before the window opened";
    public const string YoursLandedBeforeTheBar = "; yours landed before the bar went up";
    public const string YoursWasNotALimitBreak3 = "; yours was a limit break {0}";

    private readonly SimWorld world;
    private readonly IUserActions userActions;
    private readonly List<(PlayerAction Action, float At, Vector3? GroundPoint)> heard = [];
    private readonly List<TopP6LimitBreakPress> presses = [];
    private readonly List<Request> requests = [];
    private readonly List<Cast> casts = [];
    private readonly Dictionary<SimCharacter, float> tankLimitBreak3Since = [];
    private readonly List<HeldWindow> windows = [];

    private int units;
    private int sentUnits = -1;
    private (float From, float FullBy)? rebuild;
    private float generatedUntil;

    public TopP6LimitBreaks(SimWorld world)
    {
        this.world = world;
        userActions = Natives.UserActions;
        userActions.Acted += Hear;
        generatedUntil = Now;
    }

    public void Dispose() => userActions.Acted -= Hear;

    // Every limit break pressed this run, in the order they started.
    public IReadOnlyList<TopP6LimitBreakPress> Presses => presses;

    // Raised as each resolves, once the gauge has settled.
    public event Action<TopP6LimitBreakPress>? Resolved;

    public float Gauge
    {
        get
        {
            CatchUp();
            return units / (float)UnitsPerBar;
        }
    }

    // A press the run owes is waiting for a full gauge: a check failing now failed for want of one.
    public bool StarvedOfGauge
    {
        get
        {
            CatchUp();
            return requests.Count > 0 && units < FullUnits;
        }
    }

    private float Now => world.Events.Elapsed;

    private SimParty Party => world.Party;

    public void SetGauge(float bars)
    {
        CatchUp();
        units = (int)MathF.Round(Math.Clamp(bars, 0f, Bars) * UnitsPerBar);
        SendGauge(force: true);
    }

    // The party's own generation: evenly from `from` to `fullBy`, a full gauge's worth in all, and nothing outside it.
    // Times are on the run's clock and may lie before its start.
    public void Rebuild(float from, float fullBy)
    {
        CatchUp();
        rebuild = (from, fullBy);
        generatedUntil = Now;
    }

    // What a Rebuild over from..fullBy has added by `at`: the gauge a run starting later begins with.
    public static float RebuiltBars(float at, float from, float fullBy) => RebuiltUnits(at, from, fullBy) / (float)UnitsPerBar;

    public static TopP6LimitBreakKind KindOf(uint job) => PartyPresets.SkipRoleForJob(job) switch
    {
        PartyRole.MainTank or PartyRole.OffTank => TopP6LimitBreakKind.Tank,
        PartyRole.RegenHealer or PartyRole.ShieldHealer => TopP6LimitBreakKind.Healer,
        PartyRole.MeleeDpsA or PartyRole.MeleeDpsB => TopP6LimitBreakKind.Melee,
        PartyRole.PhysRangedDps => TopP6LimitBreakKind.PhysicalRanged,
        _ => TopP6LimitBreakKind.Caster,
    };

    public static TopP6LimitBreakKind KindOf(PartyRole seat) => KindOf(PartyPresets.Standard[(int)seat].ClassJob);

    // 1-3, or 0 when `actionId` is none of the job's limit breaks.
    public static int LevelOf(uint job, uint actionId)
    {
        for (var level = 1; level <= Bars; level++)
            if (actionId != 0 && LimitBreakHandler.ActionId(job, level) == actionId)
                return level;
        return 0;
    }

    // The seat presses `job`'s limit break 3 at `target` (null: itself) once no other limit break is casting and the
    // gauge is full, whatever "Require tank mitigation" says. Pressed for a bot, it casts as a player would; for the
    // local player's seat it only takes effect, since that client plays its own actions. One still waiting after
    // `until` (run time) is dropped at the end of that frame, so a check deciding then still counts it as owed.
    public void Press(PartyRole seat, uint job, SimCharacter? target = null, float until = float.PositiveInfinity)
    {
        CatchUp();
        requests.Add(new Request(seat, job, target, until));
        StartWaitingPresses();
    }

    // Whether `member` has a tank's limit break 3 up now, and up long enough before a hit deciding now.
    public TopP6TankCover TankLimitBreak3Cover(SimCharacter member)
    {
        CatchUp();
        if (!P6StatusId.TankLimitBreak3.Any(member.HasStatus)) return TopP6TankCover.Missing;
        return tankLimitBreak3Since.TryGetValue(member, out var since) && since <= Now - P6Duration.TankLimitBreak3TakesHold
            ? TopP6TankCover.Holds
            : TopP6TankCover.TooLate;
    }

    // The human in `window`'s seat dies unless a melee limit break 3 of that seat fills it: as it closes, or at its
    // interruption if that is later. A press counts whoever made it for the seat (the human, or the run for a bot-driven
    // player seat); only the seat's own presses count, and nothing holds a bot's seat to a window. An open window keeps
    // the run going only until it is filled, looking a second at a time, so a run that fills it early (the enrage's, which
    // stays open until 4.5 s before Run: ****mi*'s damage) ends as its other events do.
    public void HoldTheHumanTo(TopP6MeleeWindow window, Func<SimCharacter?> target)
    {
        var held = new HeldWindow(window, target);
        windows.Add(held);
        world.Events.FromRunStart(() =>
        {
            world.Events.Add(window.OpensAt, () => Watch(held));
            return true;
        });
    }

    public void Tick()
    {
        CatchUp();
        DropTheDeadsCasts();
        HoldCastersStill();
        requests.RemoveAll(request => Now > request.Until);
        StartWaitingPresses();
        foreach (var held in windows.Where(w => !w.Decided))
            held.LookAtTheTarget();
        foreach (var held in windows.Where(w => !w.Decided && Now >= w.Window.LatestPress).ToList())
            Judge(held);
        SendGauge(force: false);
    }

    // What happened since the last look: the human's presses heard meanwhile, the gauge's generation, and who has a
    // tank's limit break 3 up. Every entry point starts here, since an event can ask before the frame's Tick; a mechanic
    // that stops hearing Resolved, or takes stock, at a deadline calls it first, so a press heard just before still counts.
    internal void CatchUp()
    {
        SettleHeardPresses();
        Generate();
        WatchTankLimitBreak3s();
    }

    private void Hear(PlayerAction action)
    {
        if (action.ActionType != ActionType.Action || !LimitBreakHandler.IsLimitBreak(action.ActionId)) return;
        heard.Add(((uint)action.TargetId == CurrentTarget ? action with { TargetId = ClientsTarget() } : action, Now, PlacedOnTheGround(action.ActionId)));
    }

    private static ulong ClientsTarget() => Plugin.TargetManager?.Target?.GameObjectId ?? 0ul;

    // Where the player's client put its cast of a ground-targeted action, read as the cast is heard. A point farther from
    // the player than the action reaches is none the client placed for it. UNVERIFIED: that the client fills its own
    // cast's ground point while the sim runs.
    private Vector3? PlacedOnTheGround(uint actionId)
    {
        if (Natives.Data.Action(actionId) is not { TargetArea: true } || Party.Player is not { } player) return null;
        var point = world.Coordinates.ToLocal(Natives.BattleCharas.LocalPlayer.CastTargetLocation);
        return player.Placement().DistanceSq(point) <= GroundPointReach * GroundPointReach ? point : null;
    }

    private void SettleHeardPresses()
    {
        if (heard.Count == 0) return;
        var pending = heard.ToList();
        heard.Clear();
        foreach (var (action, at, groundPoint) in pending)
        {
            var human = HumansCast(action.ActionId);
            switch (action.Step)
            {
                case PlayerActionStep.CastStarted:
                    presses.Add(HumanPress(action, at, groundPoint));
                    break;
                case PlayerActionStep.Resolved:
                    if (human is null)
                    {
                        human = HumanPress(action, at, groundPoint);
                        presses.Add(human);
                    }
                    Settle(human, Party.Player, at);
                    break;
                case PlayerActionStep.Interrupted:
                    if (human is not null) human.InterruptedAt = at;
                    break;
            }
        }
    }

    private TopP6LimitBreakPress? HumansCast(uint actionId)
        => presses.LastOrDefault(p => p.ByHuman && p.IsCasting && p.ActionId == actionId);

    private TopP6LimitBreakPress HumanPress(PlayerAction action, float at, Vector3? groundPoint)
    {
        var job = (uint)Natives.BattleCharas.LocalPlayer.ClassJob;
        return new TopP6LimitBreakPress
        {
            Seat = Party.PlayerRole,
            ByHuman = true,
            Job = job,
            ActionId = action.ActionId,
            Level = LevelOf(job, action.ActionId),
            Kind = KindOf(job),
            TargetId = action.TargetId,
            CastStartedAt = at,
            CastSeconds = action.CastSeconds,
            GroundPoint = groundPoint,
        };
    }

    private void Settle(TopP6LimitBreakPress press, SimCharacter? user, float at)
    {
        Generate();
        var spent = Math.Min(press.Level * UnitsPerBar, units);
        units -= spent;
        if (user is not null && user.HasStatus(StatusId.BrilliantDynamis))
        {
            units = Math.Min(FullUnits, units + spent);
            user.RemoveStatus(StatusId.BrilliantDynamis);
            user.AddStatus(P6StatusId.SparkOfDynamis);
        }
        press.ResolvedAt = at;
        press.ResolvedFrom = user is null ? null : AimFrom(user, press.TargetId);
        SendGauge(force: true);
        DiagnosticLog.Info($"[TopP6LimitBreaks] {press.Seat} {(press.ByHuman ? "(human) " : "")}resolved {Natives.Data.ActionName(press.ActionId)} ({press.ActionId}); the gauge holds {units / (float)UnitsPerBar:F2} bars.");
        Resolved?.Invoke(press);
        StartWaitingPresses();
    }

    private Placement AimFrom(SimCharacter user, ulong targetId)
        => Find(targetId) is { } target && target != user ? user.Placement().Face(target.Position) : user.Placement();

    private SimCharacter? Find(ulong gameObjectId)
        => gameObjectId == 0
            ? null
            : world.Children.OfType<SimCharacter>().Concat(Party.AllMembers()).FirstOrDefault(c => (ulong)c.GameObjectId == gameObjectId);

    private void Generate()
    {
        var now = Now;
        if (rebuild is { } window && now > generatedUntil)
        {
            var gain = RebuiltUnits(now, window.From, window.FullBy) - RebuiltUnits(generatedUntil, window.From, window.FullBy);
            units = Math.Min(FullUnits, units + gain);
        }
        generatedUntil = MathF.Max(generatedUntil, now);
    }

    private static int RebuiltUnits(float at, float from, float fullBy)
        => fullBy <= from ? FullUnits : (int)(FullUnits * Math.Clamp((at - from) / (fullBy - from), 0f, 1f));

    private void SendGauge(bool force)
    {
        if (!force && Math.Abs(units - sentUnits) < HudStepUnits && !(units == FullUnits && sentUnits != FullUnits)) return;
        sentUnits = units;
        Party.LimitBreak.Set(units / (float)UnitsPerBar);
    }

    private void WatchTankLimitBreak3s()
    {
        foreach (var member in Party.AllMembers())
        {
            if (P6StatusId.TankLimitBreak3.Any(member.HasStatus)) tankLimitBreak3Since.TryAdd(member, Now);
            else tankLimitBreak3Since.Remove(member);
        }
    }

    private void StartWaitingPresses()
    {
        Generate();
        while (requests.Count > 0)
        {
            var request = requests[0];
            if (Party.Get(request.Seat) is not { } user || !user.IsAlive())
            {
                requests.RemoveAt(0);
                continue;
            }
            if (Now > request.Until || presses.Any(p => p.IsCasting) || units < FullUnits) return;
            requests.RemoveAt(0);
            Start(request, user);
        }
    }

    private void Start(Request request, SimCharacter user)
    {
        var actionId = LimitBreakHandler.ActionId(request.Job, Bars);
        var row = Natives.Data.Action(actionId);
        var castSeconds = row?.CastSeconds ?? 0f;
        var target = request.Target ?? user;
        var press = new TopP6LimitBreakPress
        {
            Seat = request.Seat,
            ByHuman = false,
            Job = request.Job,
            ActionId = actionId,
            Level = Bars,
            Kind = KindOf(request.Job),
            TargetId = (ulong)target.GameObjectId,
            CastStartedAt = Now,
            CastSeconds = castSeconds,
            GroundPoint = row?.TargetArea == true ? target.Position : null,
        };
        presses.Add(press);
        DiagnosticLog.Info($"[TopP6LimitBreaks] {request.Seat} presses {Natives.Data.ActionName(actionId)} ({actionId}), {castSeconds:F1} s.");
        if (castSeconds <= 0f)
        {
            ResolveRunsPress(press, user, target);
            return;
        }
        var cast = new Cast(press, user, user.MoveDestination);
        casts.Add(cast);
        user.StopMoving();
        PartyLimitBreakCast.Start(Party, user, actionId, target, castSeconds);
        world.Events.Add(castSeconds, () =>
        {
            if (!press.IsCasting) return;
            casts.Remove(cast);
            ResolveRunsPress(press, user, target);
            if (cast.HeldDestination is { } destination && user.IsAlive()) user.MoveTo(destination, RunSpeed);
        });
    }

    private void ResolveRunsPress(TopP6LimitBreakPress press, SimCharacter user, SimCharacter target)
    {
        if (!user.IsAlive())
        {
            press.InterruptedAt = Now;
            return;
        }
        SendGauge(force: true);
        PartyLimitBreakCast.Resolve(Party, user, press.ActionId, target);
        if (press.Kind == TopP6LimitBreakKind.Tank)
            foreach (var member in Party.AllMembers().Where(m => P6StatusId.TankLimitBreak3.Any(m.HasStatus)))
                tankLimitBreak3Since.TryAdd(member, Now);
        Settle(press, user, Now);
    }

    private void DropTheDeadsCasts()
    {
        foreach (var cast in casts.Where(c => !c.User.IsAlive()).ToList())
        {
            casts.Remove(cast);
            cast.Press.InterruptedAt = Now;
            PartyLimitBreakCast.Interrupt(cast.User);
        }
    }

    // A bot keeps still through its cast: a walk it is sent on meanwhile waits for the cast to end, and the step it took
    // on that walk before this tick is undone.
    private void HoldCastersStill()
    {
        foreach (var cast in casts)
        {
            if (cast.User.MoveDestination is not { } destination) continue;
            cast.HeldDestination = destination;
            cast.User.StopMoving();
            cast.User.SetPosition(cast.PinnedAt);
        }
    }

    private void Watch(HeldWindow held)
    {
        CatchUp();
        if (held.Decided) return;
        if (Now >= held.Window.LatestPress)
        {
            Judge(held);
            return;
        }
        if (Filling(held).Any(p => p.ResolvedAt is not null))
        {
            held.Decided = true;
            return;
        }
        world.Events.Add(MathF.Min(WindowWatchStep, held.Window.LatestPress - Now), () => Watch(held));
    }

    private void Judge(HeldWindow held)
    {
        CatchUp();
        if (held.Decided) return;
        var filling = Filling(held);
        if (filling.Any(p => p.ResolvedAt is not null))
        {
            held.Decided = true;
            return;
        }
        if (filling.Any(p => p.IsCasting)) return;
        held.Decided = true;
        var window = held.Window;
        var attempts = presses.Where(p => p.Seat == window.Seat && p.CastStartedAt >= window.OpensAt - EarlyPressLookback && p.CastStartedAt <= window.LatestPress)
                              .ToList();
        if (Party.Get(window.Seat) is SimPlayer human)
            human.Die(SimCharacterDeathExtensions.Environment, window.Rule + WhyYoursMissed(window, attempts, OpensAt(window), held.TargetId));
    }

    // The seat's presses that fill the window, casting or resolved.
    private List<TopP6LimitBreakPress> Filling(HeldWindow held)
    {
        var window = held.Window;
        held.LookAtTheTarget();
        return OpensAt(window) is { } opens
            ? presses.Where(p => p.Seat == window.Seat && Fills(window, p, opens, held.TargetId)).ToList()
            : [];
    }

    private static bool Fills(TopP6MeleeWindow window, TopP6LimitBreakPress press, float opens, ulong targetId)
        => window.LandsBy is { } landsBy
            ? press.LandsAMeleeLimitBreak3On(targetId, opens, landsBy)
            : press.Is(TopP6LimitBreakKind.Melee) && press.TargetId == targetId && press.CastStartedAt >= opens && press.CastStartedAt <= window.LatestPress;

    private float? OpensAt(TopP6MeleeWindow window)
    {
        if (!window.AfterAnotherMeleeLimitBreak3) return window.OpensAt;
        return presses.Where(p => p.Seat != window.Seat && p.Is(TopP6LimitBreakKind.Melee) && p.ResolvedAt >= window.OpensAt)
                      .Select(p => p.ResolvedAt)
                      .Min();
    }

    private static string WhyYoursMissed(TopP6MeleeWindow window, IReadOnlyList<TopP6LimitBreakPress> attempts, float? opens, ulong targetId)
    {
        if (attempts.Any(p => p.InterruptedAt is not null)) return YoursWasCutOff;
        if (attempts.FirstOrDefault(p => p.Level != Bars || p.Kind != TopP6LimitBreakKind.Melee) is { } wrong)
            return string.Format(YoursWasNotALimitBreak3, wrong.Level);
        if (attempts.Any(p => p.TargetId != targetId)) return YoursMissedAlphaOmega;
        if (window.LandsBy is not null) return attempts.Any(p => p.LandsAt < window.OpensAt) ? YoursLandedBeforeTheBar : "";
        if (attempts.Any(p => opens is not { } open || p.CastStartedAt < open)) return YoursStartedTooEarly;
        return "";
    }

    private sealed record Request(PartyRole Seat, uint Job, SimCharacter? Target, float Until);

    private sealed class Cast(TopP6LimitBreakPress press, SimCharacter user, Vector3? heldDestination)
    {
        public TopP6LimitBreakPress Press => press;
        public SimCharacter User => user;
        public Vector3? HeldDestination { get; set; } = heldDestination;
        public Placement PinnedAt { get; } = user.Placement();
    }

    private sealed class HeldWindow(TopP6MeleeWindow window, Func<SimCharacter?> target)
    {
        public TopP6MeleeWindow Window => window;
        public bool Decided { get; set; }

        // The target's GameObjectId as last seen, so a target that falls to the press and despawns before the window
        // closes still counts as aimed at.
        public ulong TargetId { get; private set; }

        public void LookAtTheTarget()
        {
            if (target() is { } aimedAt && (ulong)aimedAt.GameObjectId is var id and not 0) TargetId = id;
        }
    }
}
