using System.Globalization;
using System.Text;
using MitCue.Core.Events;
using MitCue.Core.Reminders;
using MitCue.Game;
using MitCue.Runtime;
using MitCue.Util;

namespace MitCue.UI;

/// <summary>Builds the Debug tab text (also copied to the clipboard for bug reports).</summary>
internal static class DebugReport
{
    /// <summary>Builds the full report. Never throws; failing sections show their error.</summary>
    public static string Build(Plugin plugin)
    {
        var sb = new StringBuilder(8 * 1024);
        var ci = CultureInfo.InvariantCulture;
        sb.Append(ci, $"MitCue {plugin.Version} debug report, {DateTime.Now:yyyy-MM-dd HH:mm:ss}").AppendLine();

        Section(sb, "Game", () => AppendGame(sb, plugin));
        Section(sb, "Data", () => AppendData(sb, plugin));
        Section(sb, "Live session", () => AppendSession(sb, plugin, plugin.Sessions.Session, plugin.Sessions.Status));
        Section(sb, "Preview", () =>
        {
            if (!plugin.Preview.IsActive)
            {
                sb.AppendLine("not running");
                return;
            }

            sb.Append(ci, $"{plugin.Preview.Description}; sim clock {plugin.Preview.SimNow:0.00}; events {plugin.Preview.EventsPlayed}/{plugin.Preview.EventCount}").AppendLine();
            AppendSession(sb, plugin, plugin.Preview.Session, "preview");
        });
        Section(sb, "Recorder", () =>
        {
            sb.Append(ci, $"enabled {plugin.Config.RecordPulls}, active {plugin.Recorder.Active}, lines {plugin.Recorder.LinesWritten}").AppendLine();
            sb.Append("file: ").AppendLine(plugin.Recorder.CurrentFile ?? "(none)");
            sb.Append("folder: ").AppendLine(plugin.Recorder.Directory);
        });
        Section(sb, "Last 50 events (other players' actions omitted)", () =>
        {
            var events = plugin.Events.RecentEvents;
            for (var i = 0; i < events.Count; i++)
                sb.AppendLine(FormatEvent(events[i], plugin));
        });
        Section(sb, "Recent own uses", () =>
        {
            var uses = plugin.RecentOwnUses;
            for (var i = 0; i < uses.Count; i++)
            {
                var u = uses[i];
                sb.Append(ci, $"{u.Clock,10:0.000}  {GameEvent.Hex(u.ActionId)} {plugin.GameData.ActionName(u.ActionId) ?? "?"} via {u.Source}").AppendLine();
            }
        });
        Section(sb, "Problems", () =>
        {
            var problems = plugin.Errors.Snapshot();
            if (problems.Length == 0)
                sb.AppendLine("none");
            foreach (var p in problems)
                sb.Append(ci, $"[{p.LastSeen:HH:mm:ss} x{p.Count}] {p.Context}: {p.Message}").AppendLine();
        });

        return sb.ToString();
    }

    private static void Section(StringBuilder sb, string title, Action body)
    {
        sb.AppendLine().Append("== ").Append(title).AppendLine(" ==");
        try
        {
            body();
        }
        catch (Exception ex)
        {
            sb.Append("(unavailable: ").Append(ErrorReporter.Describe(ex)).AppendLine(")");
        }
    }

    private static void AppendGame(StringBuilder sb, Plugin plugin)
    {
        var ci = CultureInfo.InvariantCulture;
        var ctx = plugin.Frame;
        sb.Append(ci, $"logged in {ctx.LoggedIn}; territory {ctx.Territory} ({plugin.GameData.TerritoryName(ctx.Territory)}); bound by duty {ctx.BoundByDuty}").AppendLine();
        sb.Append(ci, $"job {ctx.Job} (ClassJob {ctx.ClassJobId}, {plugin.GameData.JobAbbreviation(ctx.ClassJobId)}); effective level {ctx.Level}; entity 0x{ctx.LocalEntityId:X8}").AppendLine();
        sb.Append(ci, $"in combat {ctx.InCombat}; countdown {(ctx.Countdown is { } cd ? cd.ToString("0.0", ci) : "-")}; client {ctx.ClientLanguage}").AppendLine();
        sb.Append(ci, $"ActionEffect hook: {plugin.Events.Hook.Status}; capture {plugin.Events.Capture}; observed {plugin.Events.Hook.Observed}").AppendLine();
        sb.Append(ci, $"cast polling {plugin.Events.PollCasts}; casts seen {plugin.Events.CastsSeen}; tracked cooldown actions {plugin.TrackedActionIds.Count}").AppendLine();
    }

    private static void AppendData(StringBuilder sb, Plugin plugin)
    {
        var ci = CultureInfo.InvariantCulture;
        var data = plugin.Data;
        sb.Append(ci, $"fights: {(data.Fights == null ? "not loaded" : string.Join(", ", data.FightList.Select(f => $"{f.Key} (territory {f.Territory}, level {f.SyncLevel}, {f.Phases.Count} phases, {f.Mechanics.Count} mechanics)")))}").AppendLine();
        sb.Append(ci, $"action catalog: {(data.ActionsLoaded ? "loaded" : "missing")}; aliases: {(data.Aliases != null ? "loaded" : "missing")}").AppendLine();
        sb.Append(ci, $"plans: {plugin.Plans.All.Count} ({plugin.Plans.All.Count(p => p.Origin == Plans.PlanOrigin.User)} user) in {plugin.Plans.PlansDirectory}").AppendLine();
    }

    private static void AppendSession(StringBuilder sb, Plugin plugin, EncounterSession? session, string status)
    {
        var ci = CultureInfo.InvariantCulture;
        sb.Append("status: ").AppendLine(status);
        if (session == null)
            return;

        var me = session.Me;
        sb.Append(ci, $"fight {session.Fight.Key} ({session.Fight.Name}); plan \"{session.Plan.Name}\" ({session.Plan.Id})").AppendLine();
        sb.Append(ci, $"player: {me.Job} level {me.Level}, slot {me.Slot ?? "-"}, column {me.AssigneeKey ?? "(any)"}; entries {session.OwnEntryCount} own, {session.OtherEntryCount} others").AppendLine();
        if (!session.IsPreview)
            sb.Append(ci, $"faulted {plugin.Sessions.Faulted}; rebuild pending {plugin.Sessions.RebuildPending}; plan automatic {plugin.Sessions.PlanIsAutomatic}").AppendLine();

        string running;
        try
        {
            running = session.Player.IsRunning ? "running" : "stopped";
        }
        catch (Exception ex)
        {
            running = ErrorReporter.Describe(ex);
        }

        var phase = session.PhaseAt(session.TimelineTime);
        sb.Append(ci, $"mode {session.Mode}; timeline {running}; time {session.TimelineTime:0.00}; phase {(phase != null ? phase.Id + " (" + phase.Name + ")" : "-")}; pull active {session.PullActive}").AppendLine();
        sb.Append(ci, $"jumps {session.Jumps}; own uses applied {session.OwnUses}; options lookahead {session.Options.Lookahead} due lead {session.Options.DueLead}").AppendLine();

        sb.AppendLine("last syncs:");
        for (var i = 0; i < session.Syncs.Count; i++)
        {
            var s = session.Syncs[i];
            sb.Append(ci, $"  [{s.Clock,10:0.000}] {s.FromTime,8:0.0} -> {s.ToTime,8:0.0}  line {s.Entry.Line} \"{s.Entry.Name}\" {s.Entry.Sync}{(s.Started ? " (start)" : string.Empty)}{(s.IsJump ? " (jump)" : string.Empty)}{(s.Stopped ? " (stop)" : string.Empty)}").AppendLine();
        }

        sb.AppendLine("reminders:");
        IReadOnlyList<Reminder> all;
        try
        {
            all = session.Scheduler.All;
        }
        catch (Exception ex)
        {
            sb.Append("  (unavailable: ").Append(ErrorReporter.Describe(ex)).AppendLine(")");
            return;
        }

        foreach (var r in all)
        {
            var name = ReminderText.ItemName(r.Item, plugin.GameData);
            sb.Append(ci, $"  {r.State,-8} press {r.PressAt,8:0.0} hit {r.Hit,8:0.0} [{r.Earliest:0.0}, {r.Latest:0.0}] {name} @ {r.Mechanic.Id}");
            if (r.UsedAt is { } used)
                sb.Append(ci, $" used {used:0.0}{(r.UsedEarly ? " (early)" : string.Empty)}");
            if (r.ReadyIn is { } ready)
                sb.Append(ci, $" readyIn {ready:0.0}");
            if (r.MissingResources)
                sb.Append(" no-resource");
            sb.AppendLine();
        }

        // Plan entries the scheduler dropped (e.g. a misspelt or stale mechanic id): their calls never show.
        var problems = session.Scheduler.Problems;
        sb.Append(ci, $"entries not scheduled: {problems.Count}").AppendLine();
        foreach (var problem in problems)
            sb.Append("  ").AppendLine(problem);

        // Which reminder each own action use counted for ("why didn't my press count?").
        var uses = session.Scheduler.RecentUses;
        sb.Append(ci, $"own uses seen by the scheduler (last {uses.Count}):").AppendLine();
        foreach (var use in uses)
        {
            var action = plugin.GameData.ActionName(use.ActionId) ?? "?";
            var matched = use.Matched is { } m
                ? string.Create(ci, $"done: {ReminderText.ItemName(m.Item, plugin.GameData)} @ {m.Mechanic.Id} (press {m.PressAt:0.0})")
                : "no reminder in its window";
            sb.Append(ci, $"  t {use.Time,8:0.0}  {GameEvent.Hex(use.ActionId)} {action} -> {matched}").AppendLine();
        }
    }

    private static string FormatEvent(GameEvent e, Plugin plugin)
    {
        var ci = CultureInfo.InvariantCulture;
        return e switch
        {
            CombatStateChanged c => string.Format(ci, "{0,10:0.000}  InCombat {1}", c.Time, c.InCombat ? 1 : 0),
            CastStarted c => string.Format(ci, "{0,10:0.000}  StartsUsing {1} {2} by \"{3}\" {4:0.0}s", c.Time, GameEvent.Hex(c.ActionId), plugin.GameData.ActionName(c.ActionId) ?? "?", c.SourceName, c.CastTime),
            AbilityResolved a => string.Format(ci, "{0,10:0.000}  Ability {1} {2} by {3} ({4} targets){5}", a.Time, GameEvent.Hex(a.ActionId), plugin.GameData.ActionName(a.ActionId) ?? "?", a.SourceIsLocalPlayer ? "you" : a.SourceIsPlayer ? "your pet" : "\"" + a.SourceName + "\"", a.TargetIds.Count, string.Empty),
            DutyReset r => string.Format(ci, "{0,10:0.000}  Reset {1}", r.Time, r.Kind),
            _ => string.Format(ci, "{0,10:0.000}  {1}", e.Time, e.GetType().Name),
        };
    }
}
