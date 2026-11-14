using System.Numerics;
using AnoMech.Scenarios.Tea;
using static AnoMech.Scenarios.Tea.TeaP1Constants;

namespace AnoMech.Tests;

// Ids the pulls show that the sim has no use for yet: the opening's, and what no part plays.
internal static class TeaLogIds
{
    public const uint JagdDoll = 11338;
    public const uint BruteJustice = 11340;
    public const uint LiquidAutoAttack = 18808;
    public const uint HandAutoAttack = 18809;
    public const uint DollAutoAttack = 19278;
    public const uint Exhaust = 18462;
    public const uint ExhaustFromAPlayer = 18463;
    public const uint ReducibleComplexityFed = 18464;
    public const uint ReducibleComplexityKilled = 18465;
    public const uint Provoke = 7533;
    public const uint Shirk = 7537;
    public const uint Dropsy = 289;
    public const uint LuminousAetheroplasm = 706;
    public const uint DollTether = 0x11;
    public const uint FeedTether = 0x29;
    public const uint EntranceBarrier = 2000182;
}

// Three Liquid Rages as they appeared, with the quarter of the arena they leave open (degrees clockwise from north).
internal sealed record TeaTornadoSet(DateTimeOffset Appeared, IReadOnlyList<(uint Actor, Vector2 Position)> Tornadoes, int? OpenBearing)
{
    // West, Middle or East of the open quarter, as the sim names them.
    public TeaP1Tornado? Which(Vector2 position)
    {
        if (OpenBearing is not { } open) return null;
        var relative = ((int)Math.Round(PullLog.Bearing(position, TeaPull.Centre)) - open + 360) % 360;
        return relative switch { 270 => TeaP1Tornado.West, 180 => TeaP1Tornado.Middle, 90 => TeaP1Tornado.East, _ => null };
    }

    public string Layout => OpenBearing is { } open ? $"open {TeaPull.Compass(open)}" : "no open quarter";
}

// One TEA pull.
internal sealed class TeaPull : PullLog
{
    public static readonly Vector2 Centre = new(TeaZone.Instance.Origin.X, TeaZone.Instance.Origin.Z);

    // A player just raised is beyond harm until Transcendent runs out, as one under a tank's invulnerability is.
    private static readonly Dictionary<ushort, string> Unharmed = new()
    {
        [82] = "Hallowed Ground", [409] = "Holmgang", [810] = "Living Dead", [811] = "Walking Dead", [1836] = "Superbolide", [3255] = "Undead Rebirth",
        [418] = "Transcendent",
    };

    protected override IReadOnlyDictionary<ushort, string> Invulnerabilities => Unharmed;

    private static readonly uint[] AutoAttacks = [TeaLogIds.LiquidAutoAttack, TeaLogIds.HandAutoAttack, TeaLogIds.DollAutoAttack];

    private static readonly uint[] FightBases =
    [
        P1BNpcBaseId.LivingLiquid, P1BNpcBaseId.LiquidHand, P1BNpcBaseId.LiquidRage, P1BNpcBaseId.Embolus, P1BNpcBaseId.CruiseChaser,
        P1BNpcBaseId.Helper, TeaLogIds.JagdDoll, TeaLogIds.BruteJustice,
    ];

    // The mechanics that show how far a pull got, in fight order.
    public static readonly IReadOnlyList<(string Name, Func<TeaPull, bool> Reached)> Milestones =
    [
        ("the pull", _ => true),
        ("Cascade", p => p.CastsOf(P1ActionId.Cascade).Count >= 1),
        ("the Hand's first slam", p => p.Slams.Count >= 1),
        ("the dolls' Exhaust", p => p.Hits.Any(h => h.Action == TeaLogIds.Exhaust)),
        ("Protean 1", p => p.CastsOf(P1ActionId.ProteanWaveFromTheLiquid).Count >= 1),
        ("the Splashes", p => p.Hits.Any(h => h.Action == P1ActionId.Splash)),
        ("Drainage", p => p.Hits.Any(h => h.Action == P1ActionId.Drainage)),
        ("Cascade 2", p => p.CastsOf(P1ActionId.Cascade).Count >= 2),
        ("Liquid Gaol", p => p.Hits.Any(h => h.Action == P1ActionId.LiquidGaol)),
        ("Protean 2", p => p.CastsOf(P1ActionId.ProteanWaveFromTheLiquid).Count >= 2),
        ("the Hand's second slam", p => p.Slams.Count >= 2),
        ("Limit Cut", p => p.Hits.Any(h => h.Action == P1ActionId.HawkBlaster)),
    ];

    // The combatants, objects and map effects the pull found standing as it opened.
    public IReadOnlyList<ActCombatant> Standing { get; }
    public IReadOnlyList<ActObject> StandingObjects { get; }
    public IReadOnlyList<ActMapEffect> MapEffectsAtReset { get; }
    public IReadOnlyList<ActMapEffect> MapEffects { get; }

    // The changes of music the duty sent since the last pull closed, and those within the pull.
    public IReadOnlyList<ActMusic> MusicAtReset { get; }
    public IReadOnlyList<ActMusic> Music { get; }
    public IReadOnlyList<TeaTornadoSet> TornadoSets { get; }

    private readonly Dictionary<uint, uint> bases = [];
    private readonly Dictionary<uint, List<(DateTimeOffset Time, int Hp)>> hitPoints = [];
    private readonly Dictionary<uint, int> maxHitPoints = [];
    private readonly DateTimeOffset lastLine;
    private readonly Dictionary<uint, List<(DateTimeOffset Time, Vector2 Position, bool Walked)>> serverTracks = [];

    public TeaPull(ActPull pull) : base(pull, standing: true, alignMemoryLines: true)
    {
        lastLine = Lines.Count == 0 ? Start : Lines[^1].Time;
        Standing = Read(StandingLines, ActCombatant.From);
        StandingObjects = Read(StandingLines, ActObject.From);
        MapEffectsAtReset = Read(StandingLines, ActMapEffect.From);
        MapEffects = Read(Lines, ActMapEffect.From);
        MusicAtReset = Read(StandingLines, ActMusic.From);
        Music = Read(Lines, ActMusic.From);
        foreach (var combatant in Standing.Concat(Combatants).Where(c => c.Added && ActorIds.IsEnemy(c.Actor)))
            bases[combatant.Actor] = combatant.BNpcBase;
        var health = Read(StandingLines.Concat(Lines), ActHealth.From);
        foreach (var sample in health.Where(h => h.MaxHp > 0))
            maxHitPoints.TryAdd(sample.Actor, sample.MaxHp);
        foreach (var (time, actor, hp) in health.Select(h => (h.Time, h.Actor, h.Hp)).Concat(HitPoints.Select(h => (h.Time, h.Actor, h.Hp))).OrderBy(s => s.Time))
        {
            if (!hitPoints.TryGetValue(actor, out var samples)) hitPoints[actor] = samples = [];
            samples.Add((time, hp));
        }
        var sent = Standing.Concat(Combatants).Where(c => c.Added && c.Position is not null)
                           .Select(c => (c.Time, c.Actor, Position: c.Position!.Value, Walked: false))
                           .Concat(Moves.Select(m => (m.Time, m.Actor, m.Position, Walked: !m.Placed)))
                           .Concat(CastsAt.Select(c => (c.Time, Actor: c.Source, c.Position, Walked: false)));
        foreach (var sample in sent.Where(s => ActorIds.IsEnemy(s.Actor)).OrderBy(s => s.Time))
        {
            if (!serverTracks.TryGetValue(sample.Actor, out var track)) serverTracks[sample.Actor] = track = [];
            track.Add((sample.Time, sample.Position, sample.Walked));
        }
        TornadoSets = Clusters(Combatants.Where(c => c.Added && c.BNpcBase == P1BNpcBaseId.LiquidRage && c.Position is not null), c => c.Time, 1)
            .Select(set => new TeaTornadoSet(set[0].Time, set.Select(c => (c.Actor, c.Position!.Value)).ToList(),
                                             OpenQuarter(set.Select(c => c.Position!.Value))))
            .ToList();
        Slams = Clusters(Hits.Where(h => h.Action is P1ActionId.HandOfPrayer or P1ActionId.HandOfParting), h => h.Time, 1).Select(c => c[0]).ToList();
    }

    // The pull's own Living Liquid: the last to join before it or in it, since a combatant of the pull before can linger
    // among those standing. The Liquid Hand is the first to join in the pull.
    public uint? Liquid => Standing.Concat(Combatants).Where(c => c.Added && c.BNpcBase == P1BNpcBaseId.LivingLiquid)
                                   .OrderBy(c => c.Time).Select(c => (uint?)c.Actor).LastOrDefault();

    public uint? Hand => Combatants.Where(c => c.Added && c.BNpcBase == P1BNpcBaseId.LiquidHand).Select(c => (uint?)c.Actor).FirstOrDefault();

    // The Hand's slams, Hand of Prayer or Hand of Parting, one hit of each.
    public IReadOnlyList<ActHit> Slams { get; }

    // A cast or an ability of the fight's own crossed the network. The plugin's own runs have none: their enemies act
    // only in the client.
    public bool TheFightActed
        => Casts.Any(c => IsOfTheFight(c.Source)) || Hits.Any(h => IsOfTheFight(h.Source));

    // The log holds the pull from its first second: the Living Liquid stood at full health as it opened.
    public bool LoggedFromItsStart => Liquid is { } liquid && HpAt(liquid, Start - MemoryLag) is { } hp && hp == MaxHp(liquid);

    // What stood as the pull opened and joined with its Living Liquid, as the reset before it set the arena up.
    public IEnumerable<ActCombatant> SetUpWithTheLiquid(uint bnpcBase)
        => Standing.Where(c => c.Added && c.BNpcBase == P1BNpcBaseId.LivingLiquid).Select(c => (DateTimeOffset?)c.Time).LastOrDefault() is { } reset
            ? Standing.Where(c => c.Added && c.BNpcBase == bnpcBase && Math.Abs(Seconds(reset, c.Time)) < 2)
            : [];

    // An earlier hit has already left the player at 0 HP: what lands on it now kills nobody.
    public bool IsDying(uint player, DateTimeOffset at)
        => Hits.Any(h => h.Target == player && h.Time < at && Seconds(h.Time, at) < DeathAfterHit && HpAfter(h) == 0);

    public string Reached => Milestones.Last(m => m.Reached(this)).Name;

    public double Clock(DateTimeOffset at) => Seconds(Start, at);

    // NaN while the pull still runs as the log is read.
    public double Length => End is { } end ? Seconds(Start, end) : double.NaN;

    public string Lasted => End is null ? "still running as the log was read" : $"{Spread.F(Length, "0.0")} s";

    // What a hit at the time did has had time to show: the pull ended, or the log runs on for a hit's damage to land.
    public bool Settled(DateTimeOffset at) => End is not null || Seconds(at, lastLine) >= DeathAfterHit;

    public uint? BaseOf(uint actor) => bases.TryGetValue(actor, out var found) ? found : null;

    public bool IsOfTheFight(uint actor) => BaseOf(actor) is { } found && FightBases.Contains(found);

    public IEnumerable<uint> Actors(uint bnpcBase) => bases.Where(b => b.Value == bnpcBase).Select(b => b.Key).Order();

    public IReadOnlyList<ActCast> CastsOf(uint action) => Casts.Where(c => c.Action == action).ToList();

    // The action's hits, grouped into the times it went off.
    public List<List<ActHit>> Volleys(uint action, double window = 0.5)
        => Clusters(Hits.Where(h => h.Action == action), h => h.Time, window);

    public int? MaxHp(uint actor) => maxHitPoints.TryGetValue(actor, out var max) ? max : null;

    // The actor's hit points by the last line at or before the time that carries them.
    public int? HpAt(uint actor, DateTimeOffset at)
        => hitPoints.TryGetValue(actor, out var samples)
            ? samples.Where(s => s.Time <= at).Select(s => (int?)s.Hp).LastOrDefault()
            : null;

    public IEnumerable<uint> Living(DateTimeOffset at) => Players.Where(p => IsAlive(p, at)).Order();

    // Where an actor stood. An enemy is read from what the server sent of it where it can be (ServerAt): the positions
    // the other lines carry are the client's copy, which trails a moving enemy by up to half a second.
    public Vector2? Where(uint actor, DateTimeOffset at)
        => ActorIds.IsEnemy(actor) && ServerAt(actor, at) is { } server ? server : Positions.At(actor, at)?.Position;

    // Where the server last placed the actor (271), moved it (270) or had it cast from (263), or where it joined; between
    // two move lines under 0.6 s apart, the straight line between them.
    public Vector2? ServerAt(uint actor, DateTimeOffset at)
    {
        if (!serverTracks.TryGetValue(actor, out var track)) return null;
        var after = track.FindIndex(s => s.Time > at);
        if (after == 0) return null;
        if (after < 0) return track[^1].Position;
        var (last, next) = (track[after - 1], track[after]);
        var span = Seconds(last.Time, next.Time);
        return next.Walked && span < 0.6 ? Vector2.Lerp(last.Position, next.Position, (float)(Seconds(last.Time, at) / span)) : last.Position;
    }

    // Whom the enemy was turned to at the time: by its last target change or its last auto-attack, whichever is later. A
    // change logged in the instant the pull opens can fall just outside it, and the auto-attacks then say.
    public uint? TargetOf(uint enemy, DateTimeOffset at)
    {
        var turned = TargetChanges.Where(t => t.Actor == enemy && t.Time <= at).Select(t => ((DateTimeOffset Time, uint Target)?)(t.Time, t.Target)).LastOrDefault();
        var struck = Hits.Where(h => h.Source == enemy && h.Time <= at && AutoAttacks.Contains(h.Action) && h.HitSomeone)
                         .Select(h => ((DateTimeOffset Time, uint Target)?)(h.Time, h.Target)).LastOrDefault();
        var last = turned is null || struck?.Time > turned.Value.Time ? struck : turned;
        return last is { } found && ActorIds.IsPlayer(found.Target) ? found.Target : null;
    }

    public override string ActionName(uint action) => AutoAttacks.Contains(action) ? "auto-attack" : base.ActionName(action);

    public static bool IsAnAutoAttack(uint action) => AutoAttacks.Contains(action);

    public TeaTornadoSet? TornadoesAt(DateTimeOffset at) => TornadoSets.LastOrDefault(s => s.Appeared <= at);

    public static Vector2 Local(Vector2 world) => world - Centre;

    // "(x, z)" from the arena's centre, x east and z south, as the scenarios write a spot.
    public static string Spot(Vector2 world) => $"({Spread.F(world.X - Centre.X)}, {Spread.F(world.Y - Centre.Y)})";

    public static double Bearing(Vector2 position) => Bearing(position, Centre);

    public static string Compass(int bearing) => (((bearing % 360) + 360) % 360) switch
    {
        0 => "north", 45 => "north-east", 90 => "east", 135 => "south-east", 180 => "south", 225 => "south-west", 270 => "west", 315 => "north-west",
        var other => $"{other} degrees",
    };

    public static double Degrees(double radians) => radians * 180 / Math.PI;

    // The quarter three tornadoes 90 degrees apart leave open: opposite the one the other two flank.
    private static int? OpenQuarter(IEnumerable<Vector2> tornadoes)
    {
        var bearings = tornadoes.Select(t => (int)Math.Round(Bearing(t) / 45) * 45 % 360).ToList();
        if (bearings.Count != 3) return null;
        foreach (var middle in bearings)
            if (bearings.Contains((middle + 90) % 360) && bearings.Contains((middle + 270) % 360))
                return (middle + 180) % 360;
        return null;
    }
}

// Where the plugin keeps TEA's first phase.
internal static class TeaSource
{
    public const string Zone = "AnoMech/Scenarios/Tea/TeaZone.cs";
    public const string Constants = "AnoMech/Scenarios/Tea/TeaP1Constants.cs";
    public const string Actions = "AnoMech/Scenarios/Tea/TeaP1Actions.cs";
    public const string Mechanics = "AnoMech/Scenarios/Tea/TeaP1Mechanics.cs";
    public const string Scenario = "AnoMech/Scenarios/Tea/P1LivingLiquid/TeaP1LivingLiquidScenario.cs";
    public const string State = "AnoMech/Scenarios/Tea/P1LivingLiquid/TeaP1LivingLiquidState.cs";
    public const string LimitCut = "AnoMech/Scenarios/Tea/P1LimitCut/TeaP1LimitCutScenario.cs";
    public const string LimitCutState = "AnoMech/Scenarios/Tea/P1LimitCut/TeaP1LimitCutState.cs";

    // The scenario's own note that the opening is not played. What only the opening has points here, so building the
    // opening, which rewords the note, names every finding that then has a value to check.
    public static string OpeningNotPlayed => SimSource.Line(Scenario, "the pull before them isn't played");

    public static string NoHealthPlayed => SimSource.Line(Scenario, "which the timeline brings: no health is");
}
