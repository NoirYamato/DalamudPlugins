using System.Collections.Concurrent;
using MitCue.Core.Data;
using MitCue.Core.Events;
using MitCue.Core.Fights;
using MitCue.Core.Simulation;
using MitCue.Core.Timelines;

namespace MitCue.Core.Tests.Timelines;

/// <summary>
/// Helpers for the timeline tests: the bundled cactbot timelines (read from the core assembly, as the
/// plugin does), phase lists for TOP/TEA/DMU built in code (fight files are loaded elsewhere), event
/// builders and a driver that plays simulated events through a <see cref="TimelinePlayer"/>.
/// </summary>
internal static class TimelineKit
{
    public const string Top = "the_omega_protocol.txt";
    public const string Tea = "the_epic_of_alexander.txt";
    public const string Dmu = "dancing_mad.txt";
    public const string Dsr = "dragonsongs_reprise_ultimate.txt";
    public const string Fru = "futures_rewritten.txt";

    public const uint BossId = 0x40001234;
    public const uint PlayerId = 0x10000001;

    private static readonly EmbeddedDataSource Data = new();
    private static readonly ConcurrentDictionary<string, Timeline> Cache = new();

    public static string Text(string file) => Data.ReadText("timelines/" + file);

    public static Timeline Load(string file) => Cache.GetOrAdd(file, f => TimelineParser.Parse(Text(f), f));

    /// <summary>Parses a timeline written inline, one string per line.</summary>
    public static Timeline Parse(params string[] lines) => TimelineParser.Parse(string.Join("\n", lines), "test");

    public static TimelineEntry AtLine(this Timeline timeline, int line) => timeline.Entries.Single(e => e.Line == line);

    public static TimelineEntry Entry(this Timeline timeline, string name) => timeline.Entries.Single(e => e.Name == name);

    // Phase starts are the phases' sync points (docs/research/cactbot-timelines.md §5).
    public static FightDefinition TopFight() => Fight("top", Top, 1122, 90, ("p1", 0), ("p2", 200), ("p3", 400), ("p4", 600), ("p5", 700), ("p6", 1100));

    public static FightDefinition TeaFight() => Fight("tea", Tea, 887, 80, ("p1", 0), ("p2", 200), ("p3", 492), ("p4", 900));

    public static FightDefinition DmuFight(double p3Start = 600) => Fight("dmu", Dmu, 1363, 100, ("p1", 0), ("p2", 297.4), ("p3", p3Start), ("p4", 1087.7), ("p5", 1355.6));

    public static FightDefinition Fight(string key, string file, ushort territory, int level, params (string Id, double Start)[] phases) => new()
    {
        Key = key,
        Name = key.ToUpperInvariant(),
        Territory = territory,
        SyncLevel = level,
        TimelineFile = file,
        Phases = phases.Select(p => new Phase { Id = p.Id, Name = p.Id.ToUpperInvariant(), Start = p.Start }).ToList(),
    };

    public static AbilityResolved Ability(double clock, uint id, string source = "Boss") => new(clock, id, BossId, source, false, [PlayerId]);

    public static CastStarted Cast(double clock, uint id, string source = "Boss") => new(clock, id, BossId, source, 3f, PlayerId);

    public static CombatStateChanged Combat(double clock, bool inCombat = true) => new(clock, inCombat);

    /// <summary>One processed simulated event: the engine's time just before and just after it.</summary>
    public sealed record Step(SimulatedEvent Sim, double Before, double After, SyncResult? Result)
    {
        public double Clock => this.Sim.Event.Time;
    }

    /// <summary>
    /// Plays the events through a new player like the plugin does: a tick every 50 ms of clock (each frame)
    /// and the frame's events in order. Records the engine time before and after each event.
    /// </summary>
    public static (TimelinePlayer Player, List<Step> Steps) Play(Timeline timeline, IReadOnlyList<SimulatedEvent> events, SyncMatchOptions? options = null, double tail = 5)
    {
        var player = new TimelinePlayer(timeline, options);
        var steps = new List<Step>(events.Count);
        if (events.Count == 0)
            return (player, steps);

        var clock = events[0].Event.Time - 1;
        foreach (var sim in events)
        {
            while (clock + 0.05 < sim.Event.Time)
            {
                clock += 0.05;
                player.Tick(clock);
            }

            player.Tick(sim.Event.Time);
            var before = player.TimeAt(sim.Event.Time);
            var result = player.Process(sim.Event);
            steps.Add(new Step(sim, before, player.TimeAt(sim.Event.Time), result));
            clock = Math.Max(clock, sim.Event.Time);
        }

        player.Tick(clock + tail);
        return (player, steps);
    }
}
