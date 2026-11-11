using System.Globalization;
using System.Text.Json;
using MitCue.Core.Events;
using MitCue.Util;

namespace MitCue.Game;

/// <summary>
/// Optional recorder (setting "Record pulls"): appends every live game event as one JSON object per line to
/// ConfigDirectory/recordings/&lt;territory&gt;-&lt;yyyyMMdd-HHmmss&gt;.jsonl, one file per zone visit. Player names
/// are never written; entity ids, action ids, NPC (boss) names and clock times are. These files are the
/// real-world data used to tune timeline sync. Framework thread only.
/// </summary>
internal sealed class PullRecorder : IDisposable
{
    private const int FormatVersion = 1;

    private readonly string directory;
    private readonly ErrorReporter errors;
    private readonly string pluginVersion;
    private FileStream? stream;
    private Utf8JsonWriter? writer;
    private uint territory;
    private double lastFlush;
    private float? lastCountdown;
    private bool failed;

    /// <summary>Creates a recorder writing under <paramref name="configDirectory"/>/recordings.</summary>
    public PullRecorder(DirectoryInfo configDirectory, ErrorReporter errors, string pluginVersion)
    {
        this.directory = Path.Combine(configDirectory.FullName, "recordings");
        this.errors = errors;
        this.pluginVersion = pluginVersion;
    }

    /// <summary>True while a recording file is open.</summary>
    public bool Active => this.writer != null;

    /// <summary>Path of the current recording, or null.</summary>
    public string? CurrentFile { get; private set; }

    /// <summary>Folder holding the recordings.</summary>
    public string Directory => this.directory;

    /// <summary>Lines written to the current file.</summary>
    public int LinesWritten { get; private set; }

    /// <summary>
    /// Whether to record this frame: the setting is on and the player is in a supported fight's zone or bound by a
    /// duty. Towns, housing and the open world are never recorded (other players' action uses would be logged).
    /// </summary>
    public static bool ShouldRecord(bool enabled, bool boundByDuty, bool fightZone) => enabled && (boundByDuty || fightZone);

    /// <summary>Opens or closes the recording for this frame.</summary>
    /// <param name="shouldRecord">Recording enabled and in a fight zone or duty.</param>
    /// <param name="currentTerritory">Current territory id (a new file starts on each zone change).</param>
    /// <param name="now">Current monotonic clock.</param>
    /// <param name="clientLanguage">Client language name, written to the header.</param>
    public void Update(bool shouldRecord, uint currentTerritory, double now, string clientLanguage)
    {
        if (currentTerritory != this.territory || !shouldRecord)
        {
            // New zone (or recording switched off): finish the current file and allow a retry after errors.
            if (this.writer != null)
                this.Close();
            this.territory = currentTerritory;
            this.failed = false;
        }

        if (!shouldRecord || this.writer != null || this.failed || currentTerritory == 0)
            return;

        try
        {
            System.IO.Directory.CreateDirectory(this.directory);
            var path = Path.Combine(
                this.directory,
                $"{currentTerritory.ToString(CultureInfo.InvariantCulture)}-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.jsonl");
            this.stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 16 * 1024);
            this.writer = new Utf8JsonWriter(this.stream);
            this.CurrentFile = path;
            this.LinesWritten = 0;
            this.lastFlush = now;
            this.lastCountdown = null;

            this.writer.WriteStartObject();
            this.writer.WriteString("type", "Header");
            this.writer.WriteNumber("format", FormatVersion);
            this.writer.WriteNumber("territory", currentTerritory);
            this.writer.WriteString("started", DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture));
            this.writer.WriteNumber("clock", Math.Round(now, 3));
            this.writer.WriteString("clientLanguage", clientLanguage);
            this.writer.WriteString("plugin", this.pluginVersion);
            this.writer.WriteEndObject();
            this.EndLine();
        }
        catch (Exception ex)
        {
            this.failed = true;
            this.errors.Report("recorder.open", "Starting a pull recording", ex);
            this.Close();
        }
    }

    /// <summary>Appends this frame's events, own uses and countdown changes.</summary>
    public void Write(double now, IReadOnlyList<GameEvent> events, IReadOnlyList<OwnUse> ownUses, float? countdown)
    {
        var w = this.writer;
        if (w == null)
            return;

        try
        {
            if (countdown.HasValue != this.lastCountdown.HasValue)
            {
                w.WriteStartObject();
                w.WriteString("type", countdown.HasValue ? "CountdownStarted" : "CountdownEnded");
                w.WriteNumber("t", Math.Round(now, 3));
                w.WriteNumber("remaining", Math.Round(countdown ?? this.lastCountdown ?? 0, 2));
                w.WriteEndObject();
                this.EndLine();
            }

            this.lastCountdown = countdown;

            for (var i = 0; i < events.Count; i++)
                this.WriteEvent(w, events[i]);

            for (var i = 0; i < ownUses.Count; i++)
            {
                var use = ownUses[i];
                w.WriteStartObject();
                w.WriteString("type", "OwnUse");
                w.WriteNumber("t", Math.Round(use.Clock, 3));
                w.WriteNumber("id", use.ActionId);
                w.WriteString("source", use.Source.ToString());
                w.WriteEndObject();
                this.EndLine();
            }

            if (now - this.lastFlush > 2)
            {
                this.lastFlush = now;
                this.stream?.Flush();
            }
        }
        catch (Exception ex)
        {
            this.failed = true;
            this.errors.Report("recorder.write", "Writing the pull recording", ex);
            this.Close();
        }
    }

    /// <summary>Closes the current file.</summary>
    public void Close()
    {
        try
        {
            this.writer?.Flush();
            this.writer?.Dispose();
            this.stream?.Flush();
            this.stream?.Dispose();
        }
        catch (Exception ex)
        {
            this.errors.Report("recorder.close", "Closing the pull recording", ex);
        }

        this.writer = null;
        this.stream = null;
        this.CurrentFile = null;
    }

    /// <inheritdoc/>
    public void Dispose() => this.Close();

    private void WriteEvent(Utf8JsonWriter w, GameEvent e)
    {
        w.WriteStartObject();
        w.WriteString("type", e.GetType().Name);
        w.WriteNumber("t", Math.Round(e.Time, 3));
        switch (e)
        {
            case CombatStateChanged c:
                w.WriteBoolean("inCombat", c.InCombat);
                break;
            case CastStarted c:
                w.WriteString("id", GameEvent.Hex(c.ActionId));
                w.WriteString("src", GameEvent.Hex(c.SourceId, 8));
                w.WriteString("srcName", c.SourceName); // non-player casters only (bosses/helpers)
                w.WriteNumber("castTime", Math.Round(c.CastTime, 2));
                w.WriteString("tgt", GameEvent.Hex(c.TargetId, 8));
                break;
            case AbilityResolved a:
                w.WriteString("id", GameEvent.Hex(a.ActionId));
                w.WriteString("src", GameEvent.Hex(a.SourceId, 8));
                w.WriteBoolean("player", a.SourceIsPlayer);
                w.WriteBoolean("local", a.SourceIsLocalPlayer);
                if (!a.SourceIsPlayer)
                    w.WriteString("srcName", a.SourceName);
                w.WriteStartArray("tgt");
                foreach (var target in a.TargetIds)
                    w.WriteStringValue(GameEvent.Hex(target, 8));
                w.WriteEndArray();
                break;
            case DutyReset r:
                w.WriteString("kind", r.Kind.ToString());
                break;
            case MapEffectOccurred m:
                w.WriteNumber("location", m.Location);
                w.WriteNumber("flags", m.Flags);
                break;
            case DirectorCommand d:
                w.WriteString("command", GameEvent.Hex(d.Command, 8));
                break;
        }

        w.WriteEndObject();
        this.EndLine();
    }

    private void EndLine()
    {
        this.writer!.Flush();
        this.stream!.WriteByte((byte)'\n');
        this.writer.Reset(this.stream);
        this.LinesWritten++;
    }
}
