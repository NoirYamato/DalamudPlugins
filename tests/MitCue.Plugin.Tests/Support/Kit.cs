using Dalamud.Plugin.Services;
using MitCue.Core;
using MitCue.Core.Events;
using MitCue.Core.Fights;
using MitCue.Core.Plans;
using MitCue.Core.Reminders;
using MitCue.Core.Timelines;
using MitCue.Data;
using MitCue.Runtime;
using MitCue.Util;

namespace MitCue.PluginTests.Support;

/// <summary>Builders for fights, plans and sessions used by the plugin tests.</summary>
internal static class Kit
{
    public const uint Temperance = 16536;
    public const uint DivineBenison = 7432;
    public const uint PlenaryIndulgence = 7433;
    public const uint Consolation = 16546;
    public const uint SacredSoil = 188;
    public const uint Reprisal = 7535;

    /// <summary>This test run's temporary folders (plan stores, recordings), deleted when the run ends.</summary>
    private static readonly string TempRoot = Path.Combine(Path.GetTempPath(), "mitcue-plugin-tests", Guid.NewGuid().ToString("N"));

    static Kit()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                if (Directory.Exists(TempRoot))
                    Directory.Delete(TempRoot, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        };
    }

    /// <summary>A timeline with no entries (sessions in these tests run on a <see cref="ScriptedSyncEngine"/>).</summary>
    public static Timeline EmptyTimeline { get; } = new() { Name = "empty", Entries = [], Labels = new Dictionary<string, double>() };

    /// <summary>A two-phase fight: P1 from 0 (pull, raidwides at 30 and 60), P2 from 200 (raidwides at 230 and 460).</summary>
    public static FightDefinition Fight() => new()
    {
        Key = "test",
        Name = "Test Fight",
        Territory = 1,
        SyncLevel = 90,
        TimelineFile = "test.txt",
        Phases =
        [
            new Phase { Id = "p1", Name = "P1", Start = 0 },
            new Phase { Id = "p2", Name = "P2", Start = 200 },
        ],
        Mechanics =
        [
            Mechanic("p1.pull", "Pull", "p1", 0, MechanicKind.Marker),
            Mechanic("p1.raid", "Raidwide 1", "p1", 30),
            Mechanic("p1.raid2", "Raidwide 2", "p1", 60),
            Mechanic("p2.start", "P2 start", "p2", 200, MechanicKind.Marker),
            Mechanic("p2.raid", "P2 raidwide", "p2", 230),
            Mechanic("p2.late", "Late raidwide", "p2", 460),
        ],
    };

    /// <summary>One plan entry pressing <paramref name="action"/> at hit + <paramref name="offset"/>.</summary>
    public static PlanEntry Entry(string mechanic, double offset, uint action, string name, Job? job = Job.WHM, string? slot = null, string? assignee = null) => new()
    {
        Id = $"{mechanic}/{action}/{offset}",
        Mechanic = mechanic,
        Offset = offset,
        Who = new WhoSpec { Job = job, Slot = slot, Assignee = assignee },
        Do = [PlanItem.ForAction(action, name)],
    };

    /// <summary>A plan holding <paramref name="entries"/>.</summary>
    public static MitPlan Plan(IEnumerable<PlanEntry> entries, params Assignee[] assignees) => new()
    {
        Id = "plan-" + Guid.NewGuid().ToString("N"),
        Name = "Test plan",
        Fight = "test",
        Assignees = assignees.ToList(),
        Entries = entries.ToList(),
    };

    /// <summary>A session over <paramref name="own"/> entries, driven by <paramref name="engine"/>.</summary>
    public static EncounterSession Session(
        FightDefinition fight,
        IReadOnlyList<PlanEntry> own,
        ISyncEngine engine)
    {
        return new EncounterSession(new SessionSetup
        {
            Fight = fight,
            Timeline = EmptyTimeline,
            Plan = Plan(own),
            Me = new PlayerContext(Job.WHM, 90),
            OwnEntries = own,
            Actions = EmptyActionCatalog.Instance,
            Options = new ReminderOptions(),
            SyncOptions = SyncMatchOptions.Default,
            Engine = (_, _) => engine,
        });
    }

    /// <summary>An error reporter that logs nowhere.</summary>
    public static ErrorReporter Errors() => new(ServiceFake.Of<IPluginLog>());

    /// <summary>Game data with no game behind it (names and icons are unknown).</summary>
    public static GameData GameData() => new(ServiceFake.Of<IDataManager>());

    /// <summary>A fresh temporary folder, deleted when the test run ends.</summary>
    public static DirectoryInfo TempDirectory() => Directory.CreateDirectory(Path.Combine(TempRoot, Guid.NewGuid().ToString("N")));

    private static Mechanic Mechanic(string id, string name, string phase, double hit, MechanicKind kind = MechanicKind.Raidwide) => new()
    {
        Id = id,
        Name = name,
        Phase = phase,
        Hit = hit,
        Hits = [hit],
        Kind = kind,
    };
}

/// <summary>
/// Drives an <see cref="EncounterSession"/> the way SessionManager does each frame: this frame's events first, then
/// <see cref="EncounterSession.Update"/>. Records pull ends and "became due" notifications with their clock.
/// </summary>
internal sealed class SessionDriver
{
    private readonly EncounterSession session;
    private double? castEvery;
    private double nextCast;

    public SessionDriver(EncounterSession session, double clock)
    {
        this.session = session;
        this.Clock = clock;
        session.PullEnded += (_, summary, reason) => this.Ended.Add((this.Clock, reason, summary));
        session.BecameDue += (_, reminder) => this.Due.Add((this.Clock, reminder));
    }

    /// <summary>Current clock (seconds).</summary>
    public double Clock { get; private set; }

    /// <summary>The InCombat condition passed to each update.</summary>
    public bool InCombat { get; private set; }

    /// <summary>Seconds between simulated boss casts, starting at the next frame (null = the boss does nothing).</summary>
    public double? BossCastEvery
    {
        get => this.castEvery;
        set
        {
            this.castEvery = value;
            this.nextCast = this.Clock;
        }
    }

    public List<(double Clock, string Reason, PullSummary Summary)> Ended { get; } = [];

    public List<(double Clock, Reminder Reminder)> Due { get; } = [];

    /// <summary>The InCombat flag changes now (the event is fed like GameEventSource does, then a frame runs).</summary>
    public void CombatEdge(bool inCombat)
    {
        this.InCombat = inCombat;
        this.session.Process(new CombatStateChanged(this.Clock, inCombat));
        this.session.Update(this.Clock, null, this.InCombat, null);
    }

    /// <summary>Feeds one event at the current clock, then runs a frame.</summary>
    public void Event(GameEvent e)
    {
        this.session.Process(e);
        this.session.Update(this.Clock, null, this.InCombat, null);
    }

    /// <summary>Runs frames every <paramref name="step"/> seconds up to <paramref name="clock"/>.</summary>
    public void RunTo(double clock, double step = 0.1)
    {
        while (this.Clock + step <= clock + 1e-9)
        {
            this.Clock += step;
            if (this.castEvery is { } every && this.Clock >= this.nextCast)
            {
                this.session.Process(new CastStarted(this.Clock, 0x7B03, 0x40001234, "Omega", 5f, 0));
                this.nextCast = this.Clock + every;
            }

            this.session.Update(this.Clock, null, this.InCombat, null);
        }
    }
}
