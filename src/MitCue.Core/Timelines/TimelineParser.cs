using System.Globalization;
using System.Text.RegularExpressions;

namespace MitCue.Core.Timelines;

/// <summary>
/// Parses cactbot timeline text (format: docs/research/cactbot-timelines.md, reference
/// implementation cactbot ui/raidboss/timeline_parser.ts).
/// </summary>
/// <remarks>
/// <para>
/// Every line is handled like cactbot handles it, with the same statement regexes applied in the same
/// order: comments and empty lines are skipped; <c>hideall</c>, <c>label</c> and entry lines are
/// parsed; the legacy alert commands (<c>alertall</c>, <c>infotext</c>/<c>alerttext</c>/<c>alarmtext</c>,
/// <c>define soundalert</c>, <c>define speaker</c>) are accepted and ignored. On an entry line the comment
/// is cut first, then <c>duration</c>, an old-style <c>sync /regex/</c>, a network sync
/// <c>Type { json5 }</c> and, after the sync, <c>window</c> and <c>jump</c>/<c>forcejump</c> are taken
/// out; any text left over drops the entry.
/// </para>
/// <para>
/// Differences from cactbot, all reported in <see cref="Timeline.Warnings"/>: an entry rejected for
/// leftover text loses its sync as well (cactbot keeps the sync active, quirk Q4); old-style syncs are
/// kept but never match (MitCue sees game events, not ACT log lines); a <c>forcejump</c> to a missing
/// label loses the forcejump (cactbot throws at runtime); an invalid regex in a sync value drops the
/// sync. Values with leading or trailing spaces in name fields are reported (quirk Q7) but kept.
/// </para>
/// </remarks>
public static class TimelineParser
{
    private const RegexOptions Opts = RegexOptions.CultureInvariant;
    private const string Number = @"[0-9]+(?:\.[0-9]+)?";

    // The statement regexes of cactbot's timeline_parser.ts, verbatim apart from the regex dialect.
    private static readonly Regex IgnoreRegex = new(@"^hideall\s+""(?<id>[^""]+)""(?:\s*#.*)?$", Opts);
    private static readonly Regex TtsRegex = new(@"^alertall\s+""(?<id>[^""]*)""\s+before\s+(?<beforeSeconds>-?" + Number + @")\s+(?<command>sound|speak\s+""[^""]*"")\s+""(?<text>[^""]*)""$", Opts);
    private static readonly Regex SoundAlertRegex = new(@"^define\s+soundalert\s+""[^""]*""\s+""[^""]*""$", Opts);
    private static readonly Regex SpeakerRegex = new(@"define speaker ""[^""]*""(\s+""[^""]*"")?\s+(-?" + Number + @")\s+(-?" + Number + ")", Opts);
    private static readonly Regex PopupTextRegex = new(@"^(?<type>info|alert|alarm)text\s+""(?<id>[^""]+)""\s+before\s+(?<beforeSeconds>-?" + Number + @")(?:\s+""(?<text>[^""]+)"")?$", Opts);
    private static readonly Regex LabelRegex = new(@"^(?<time>" + Number + @")\s+(?<text>label\s+""(?<label>[^""]*)"")\s*$", Opts);
    private static readonly Regex LineRegex = new(@"^(?<text>(?<time>" + Number + @")\s+""(?<name>.*?)"")(\s+(.*))?", Opts);
    private static readonly Regex DurationRegex = new(@"(?:[^#]*?\s)?(?<text>duration\s+(?<seconds>" + Number + @"))(\s.*)?$", Opts);
    private static readonly Regex SyncRegexCommand = new(@"(?:[^#]*?\s)?(?<text>sync\s*/(?<regex>.*)/)(?<args>\s.*)?$", Opts);
    private static readonly Regex SyncNetRegex = new(@"(?:[^#]*?\s)?(?<netRegexType>" + string.Join("|", NetLogDefinitions.Names) + @")\s*(?<netRegex>\{.*\})(?<args>\s.*)?$", Opts);
    private static readonly Regex WindowRegex = new(@"(?:[^#]*?\s)?(?<text>window\s+(?:(?<start>" + Number + "),)?(?<end>" + Number + @"))(?:\s.*)?$", Opts);
    private static readonly Regex JumpRegex = new(@"(?:[^#]*?\s)?(?<text>(?<command>(?:force|)jump)\s+(?:""(?<label>[^""]*)""|(?<seconds>" + Number + @")))(?:\s.*)?$", Opts);

    // make_timeline.ts arguments recorded in header comments, e.g. "# -p 7B03:15 7B40:206.4".
    private static readonly Regex PhaseHintRegex = new(@"^#\s*-p\s+(?<list>[^-]*)", Opts);
    private static readonly Regex ZoneIdHintRegex = new(@"^#\s*ZoneId:\s*(?<id>[0-9]+)\s*$", Opts | RegexOptions.IgnoreCase);

    /// <summary>Keys CombatantMemory's repeating "pair" field may use (cactbot netlog_defs.ts).</summary>
    private static readonly HashSet<string> CombatantMemoryKeys = new(StringComparer.Ordinal)
    {
        "CurrentWorldID", "WorldID", "WorldName", "BNpcID", "BNpcNameID", "PartyType", "ID", "OwnerID", "WeaponId", "Type",
        "Job", "Level", "Name", "CurrentHP", "MaxHP", "CurrentMP", "MaxMP", "PosX", "PosY", "PosZ", "Heading", "MonsterType",
        "Status", "ModelStatus", "AggressionStatus", "TargetID", "IsTargetable", "Radius", "Distance", "EffectiveDistance",
        "NPCTargetID", "CurrentGP", "MaxGP", "CurrentCP", "MaxCP", "PCTargetID", "IsCasting1", "IsCasting2", "CastBuffID",
        "CastTargetID", "CastGroundTargetX", "CastGroundTargetY", "CastGroundTargetZ", "CastDurationCurrent", "CastDurationMax",
        "TransformationId",
    };

    /// <summary>
    /// Parses a timeline. Never throws for unknown syntax: unsupported lines are skipped and
    /// reported in <see cref="Timeline.Warnings"/>. Throws <see cref="FormatException"/> only
    /// for input that is not a timeline at all (e.g. no entries).
    /// </summary>
    public static Timeline Parse(string text, string name = "timeline")
    {
        ArgumentNullException.ThrowIfNull(text);
        var state = new ParseState();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
            ParseLine(state, i + 1, lines[i]);

        ResolveLabels(state);

        if (state.Drafts.Count == 0)
        {
            var first = state.Warnings.Count > 0 ? $" (first problem: line {state.Warnings[0].Line}: {state.Warnings[0].Message})" : string.Empty;
            throw new FormatException($"{name} is not a cactbot timeline: it has no entries{first}.");
        }

        var entries = new List<TimelineEntry>(state.Drafts.Count);
        foreach (var d in state.Drafts)
        {
            entries.Add(new TimelineEntry
            {
                Index = entries.Count,
                Line = d.Line,
                Time = d.Time,
                Name = d.Name,
                Sync = d.Sync,
                Window = d.Window,
                JumpTime = d.JumpTime,
                JumpLabel = d.JumpLabel,
                ForceJump = d.ForceJump,
                Duration = d.Duration,
                IsHidden = state.Hidden.Contains(d.Name) || d.Name is "--sync--" or "--Reset--",
            });
        }

        return new Timeline
        {
            Name = name,
            Entries = entries.AsReadOnly(),
            Labels = state.Labels,
            HiddenNames = state.Hidden,
            Warnings = state.Warnings.OrderBy(w => w.Line).Select(w => $"line {w.Line}: {w.Message}").ToList().AsReadOnly(),
            PhaseAnchors = state.PhaseAnchors.AsReadOnly(),
            ZoneId = state.ZoneId,
        };
    }

    private static void ParseLine(ParseState state, int lineNumber, string raw)
    {
        var line = JsTrim(raw);
        if (line.Length == 0)
            return;
        if (line[0] == '#')
        {
            ReadHeaderHint(state, line);
            return;
        }

        Match m;
        if ((m = IgnoreRegex.Match(line)).Success)
        {
            state.Hidden.Add(m.Groups["id"].Value);
            return;
        }

        if (TtsRegex.IsMatch(line))
        {
            state.Warn(lineNumber, "\"alertall\" is a legacy alert command; it is ignored");
            return;
        }

        if (SoundAlertRegex.IsMatch(line) || SpeakerRegex.IsMatch(line))
        {
            state.Warn(lineNumber, "\"define\" is a legacy alert command; it is ignored");
            return;
        }

        if ((m = PopupTextRegex.Match(line)).Success)
        {
            state.Warn(lineNumber, $"\"{m.Groups["type"].Value}text\" is a legacy alert command; it is ignored");
            return;
        }

        if ((m = LabelRegex.Match(line)).Success)
        {
            var label = m.Groups["label"].Value;
            var seconds = ParseNumber(m.Groups["time"].Value);
            if (state.Labels.TryGetValue(label, out var previous))
            {
                state.Warn(lineNumber, $"duplicate label \"{label}\" (already at {Format(previous)} on line {state.LabelLines[label]}); this definition ({Format(seconds)}) is used");
            }

            state.Labels[label] = seconds;
            state.LabelLines[label] = lineNumber;
            return;
        }

        m = LineRegex.Match(line);
        if (!m.Success)
        {
            state.Warn(lineNumber, $"not a timeline statement, ignored: {line}");
            return;
        }

        var draft = new EntryDraft
        {
            Line = lineNumber,
            Time = ParseNumber(m.Groups["time"].Value),
            Name = m.Groups["name"].Value,
        };

        // The name is taken first, so it may contain '#'; then the comment goes.
        var rest = JsTrim(ReplaceFirst(line, m.Groups["text"].Value, string.Empty));
        var hash = rest.IndexOf('#');
        if (hash >= 0)
            rest = JsTrim(rest[..hash]);

        var messages = new List<string>();
        if (rest.Length > 0)
        {
            rest = MatchDuration(rest, draft);
            rest = MatchLegacySync(rest, draft, messages);
            rest = MatchNetSync(rest, draft, messages);
        }

        // The sync's own problems come first: they are often why text is left over (cactbot reports both).
        foreach (var message in messages)
            state.Warn(lineNumber, message);
        if (rest.Length > 0)
        {
            state.Warn(lineNumber, $"unexpected text \"{rest}\"; the entry is ignored: {line}");
            return;
        }
        if (draft.Sync != null && draft.Window.IsEmpty)
            state.Warn(lineNumber, $"the sync window is empty (window {Format(draft.Window.Before)},{Format(draft.Window.After)}); this sync never matches");
        state.Drafts.Add(draft);
    }

    private static string MatchDuration(string rest, EntryDraft draft)
    {
        var m = DurationRegex.Match(rest);
        if (!m.Success)
            return rest;
        draft.Duration = ParseNumber(m.Groups["seconds"].Value);
        return JsTrim(ReplaceFirst(rest, m.Groups["text"].Value, string.Empty));
    }

    private static string MatchLegacySync(string rest, EntryDraft draft, List<string> messages)
    {
        var m = SyncRegexCommand.Match(rest);
        if (!m.Success)
            return rest;
        rest = JsTrim(ReplaceFirst(rest, m.Groups["text"].Value, string.Empty));
        draft.Sync = new SyncSpec(SyncSpec.LegacyType, new Dictionary<string, FieldMatcher>(), m.Groups["regex"].Value);
        messages.Add("old-style \"sync /.../\" syncs match ACT log lines, which MitCue does not see; this entry never syncs");
        return ApplySyncArguments(rest, m.Groups["args"], draft);
    }

    private static string MatchNetSync(string rest, EntryDraft draft, List<string> messages)
    {
        var m = SyncNetRegex.Match(rest);
        if (!m.Success)
            return rest;

        var type = m.Groups["netRegexType"].Value;
        var json = m.Groups["netRegex"].Value;
        rest = JsTrim(ReplaceFirst(rest, type, string.Empty));
        rest = JsTrim(ReplaceFirst(rest, json, string.Empty));

        object? parsed;
        try
        {
            parsed = Json5.Parse(json);
        }
        catch (FormatException ex)
        {
            messages.Add($"invalid sync parameters {json} ({ex.Message}); the entry has no sync");
            return rest;
        }

        if (parsed is not Json5Object obj)
        {
            messages.Add($"invalid sync parameters for {type}: {json} is not an object; the entry has no sync");
            return rest;
        }

        if (!TryBuildFields(type, obj, out var fields, out var error))
        {
            messages.Add($"invalid sync parameters for {type}: {error}; the entry has no sync");
            return rest;
        }

        foreach (var (key, matcher) in fields)
        {
            if (SyncSpec.NameFields.Contains(key) && matcher.Values.Any(v => v.Length > 0 && (char.IsWhiteSpace(v[0]) || char.IsWhiteSpace(v[^1]))))
                messages.Add($"value {matcher.Display} of field \"{key}\" has leading or trailing spaces, so it only matches names with those spaces");
        }

        draft.Sync = new SyncSpec(type, fields);
        draft.Window = SyncWindow.Default;
        draft.JumpTime = null;
        draft.JumpLabel = null;
        draft.ForceJump = false;
        return ApplySyncArguments(rest, m.Groups["args"], draft);
    }

    /// <summary>Takes <c>window</c> and <c>jump</c>/<c>forcejump</c> from the text after the sync (cactbot's buildRegexSync).</summary>
    private static string ApplySyncArguments(string rest, Group args, EntryDraft draft)
    {
        draft.Window = SyncWindow.Default;
        if (!args.Success)
            return rest;

        var window = WindowRegex.Match(args.Value);
        if (window.Success)
        {
            rest = JsTrim(ReplaceFirst(rest, window.Groups["text"].Value, string.Empty));
            var end = ParseNumber(window.Groups["end"].Value);
            draft.Window = window.Groups["start"].Success
                ? new SyncWindow(ParseNumber(window.Groups["start"].Value), end)
                : new SyncWindow(end / 2, end / 2);
        }

        var jump = JumpRegex.Match(args.Value);
        if (jump.Success)
        {
            rest = JsTrim(ReplaceFirst(rest, jump.Groups["text"].Value, string.Empty));
            if (jump.Groups["seconds"].Success)
                draft.JumpTime = ParseNumber(jump.Groups["seconds"].Value);
            else
                draft.JumpLabel = jump.Groups["label"].Value;
            draft.ForceJump = jump.Groups["command"].Value == "forcejump";
        }

        return rest;
    }

    private static bool TryBuildFields(string type, Json5Object obj, out Dictionary<string, FieldMatcher> fields, out string error)
    {
        fields = new Dictionary<string, FieldMatcher>(StringComparer.Ordinal);
        error = string.Empty;
        if (!NetLogDefinitions.TryGet(type, out var definition))
        {
            error = $"unknown sync type {type}";
            return false;
        }

        foreach (var (key, value) in obj.Members)
        {
            if (!definition.Fields.Contains(key))
            {
                if (definition.HasRepeatingPairs && key == NetLogDefinitions.RepeatingPairField)
                {
                    if (!TryAddPairs(value, fields, out error))
                        return false;
                    continue;
                }

                error = $"unknown field \"{key}\"";
                return false;
            }

            if (key == "timestamp")
            {
                error = "the field \"timestamp\" cannot be used in a sync";
                return false;
            }

            if (!TryMatcher(key, value, out var matcher, out error))
                return false;
            fields[key] = matcher;
        }

        return true;
    }

    /// <summary>CombatantMemory's <c>pair: [{ key: "BNpcID", value: "1234" }]</c> becomes the field "pair.BNpcID".</summary>
    private static bool TryAddPairs(object? value, Dictionary<string, FieldMatcher> fields, out string error)
    {
        error = string.Empty;
        if (value is not List<object?> list)
        {
            error = "the field \"pair\" must be an array of { key, value } objects";
            return false;
        }

        foreach (var item in list)
        {
            if (item is not Json5Object pair || !pair.TryGet("key", out var keyValue) || keyValue is not string key
                || !CombatantMemoryKeys.Contains(key) || pair.Members.Any(p => p.Key is not ("key" or "value")))
            {
                error = "each \"pair\" entry must be { key: \"<CombatantMemory key>\", value: ... } (array keys are not supported)";
                return false;
            }

            if (pair.TryGet("value", out var v))
            {
                if (!TryMatcher("pair." + key, v, out var matcher, out error))
                    return false;
                fields["pair." + key] = matcher;
            }
        }

        return true;
    }

    private static bool TryMatcher(string key, object? value, out FieldMatcher matcher, out string error)
    {
        matcher = null!;
        error = string.Empty;
        List<string> values;
        if (value is string s)
        {
            values = [s];
        }
        else if (value is List<object?> list && list.All(v => v is string))
        {
            values = list.Cast<string>().ToList();
        }
        else
        {
            error = $"the value of \"{key}\" must be a string or an array of strings";
            return false;
        }

        try
        {
            matcher = FieldMatcher.AnyOf(values);
            return true;
        }
        catch (ArgumentException ex)
        {
            error = $"the value of \"{key}\" is not a valid regular expression ({ex.Message})";
            return false;
        }
    }

    private static void ResolveLabels(ParseState state)
    {
        foreach (var d in state.Drafts)
        {
            if (d.JumpLabel == null)
                continue;
            if (state.Labels.TryGetValue(d.JumpLabel, out var time))
            {
                d.JumpTime = time;
                continue;
            }

            if (d.ForceJump)
            {
                state.Warn(d.Line, $"forcejump to unknown label \"{d.JumpLabel}\"; the entry keeps its sync without the forcejump");
                d.ForceJump = false;
            }
            else
            {
                state.Warn(d.Line, $"jump to unknown label \"{d.JumpLabel}\"; the sync does not jump");
            }
        }
    }

    private static void ReadHeaderHint(ParseState state, string comment)
    {
        var p = PhaseHintRegex.Match(comment);
        if (p.Success)
        {
            foreach (var token in p.Groups["list"].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var colon = token.IndexOf(':');
                if (colon > 0 && double.TryParse(token[(colon + 1)..], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var t))
                    state.PhaseAnchors.Add(new PhaseAnchor(token[..colon], t));
            }

            return;
        }

        var z = ZoneIdHintRegex.Match(comment);
        if (z.Success && int.TryParse(z.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var zone))
            state.ZoneId ??= zone;
    }

    /// <summary>JavaScript's String.prototype.trim: whitespace, line terminators and the BOM.</summary>
    private static string JsTrim(string s)
    {
        int start = 0, end = s.Length;
        while (start < end && IsJsSpace(s[start]))
            start++;
        while (end > start && IsJsSpace(s[end - 1]))
            end--;
        return start == 0 && end == s.Length ? s : s[start..end];
    }

    private static bool IsJsSpace(char c) => char.IsWhiteSpace(c) || c == '\uFEFF';

    /// <summary>JavaScript's String.prototype.replace with a string pattern: the first occurrence only.</summary>
    private static string ReplaceFirst(string s, string find, string replacement)
    {
        var i = s.IndexOf(find, StringComparison.Ordinal);
        return i < 0 ? s : string.Concat(s.AsSpan(0, i), replacement, s.AsSpan(i + find.Length));
    }

    private static double ParseNumber(string s) => double.Parse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    private static string Format(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);

    private sealed class ParseState
    {
        public List<EntryDraft> Drafts { get; } = [];

        public Dictionary<string, double> Labels { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, int> LabelLines { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Hidden { get; } = new(StringComparer.Ordinal);

        public List<(int Line, string Message)> Warnings { get; } = [];

        public List<PhaseAnchor> PhaseAnchors { get; } = [];

        public int? ZoneId { get; set; }

        public void Warn(int line, string message) => this.Warnings.Add((line, message));
    }

    private sealed class EntryDraft
    {
        public int Line { get; init; }

        public double Time { get; init; }

        public string Name { get; init; } = string.Empty;

        public SyncSpec? Sync { get; set; }

        public SyncWindow Window { get; set; } = SyncWindow.Default;

        public double? JumpTime { get; set; }

        public string? JumpLabel { get; set; }

        public bool ForceJump { get; set; }

        public double? Duration { get; set; }
    }
}
