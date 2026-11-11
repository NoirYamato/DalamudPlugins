using System.Buffers;
using System.Collections;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using MitCue.Core.Data;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.EventEmitters;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization.ObjectGraphVisitors;

namespace MitCue.Core.Plans;

/// <summary>Plan (de)serialization: YAML (human-editable), JSON (storage), share codes (compressed, base64url).</summary>
/// <remarks>
/// <para>
/// YAML (docs/DESIGN.md §3.2) uses snake_case keys and leaves out nulls, <c>false</c>, empty lists (except an empty
/// either/or group, <c>any: []</c>) and an empty <c>who</c>; items are written as <c>{action, name}</c>,
/// <c>{any: [...]}</c> or <c>{note}</c>. JSON uses camelCase and keeps every value. A share code is <c>MITCUE1:</c> +
/// base64url (no padding) of the raw-deflated UTF-8 JSON.
/// </para>
/// <para>
/// Readers ignore unknown fields, accept a missing <c>format</c> (treated as the current one) and reject any other
/// format with a <see cref="FormatException"/> that says so. They reject a repeated key (instead of keeping its last
/// value), YAML aliases (<c>*name</c>, which would share one object between places, or make a plan contain itself),
/// and jobs that are not combat jobs; every format reads jobs with the same rules, so a plan read from one format
/// can be written as another and read back. Every reader error is a <see cref="FormatException"/>.
/// </para>
/// </remarks>
public static class PlanSerializer
{
    /// <summary>Prefix of share codes in the current format.</summary>
    public const string ShareCodePrefix = "MITCUE1:";

    /// <summary>Upper bound for a decompressed share code (protects against decompression bombs).</summary>
    private const int MaxShareCodeBytes = 16 * 1024 * 1024;

    private static readonly ISerializer YamlWriter = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults)
        .WithQuotingNecessaryStrings(true)
        .WithEventEmitter(next => new CompactStyleEmitter(next))
        .WithEmissionPhaseObjectGraphVisitor(args => new OmitEmptyVisitor(args.InnerVisitor))
        .DisableAliases()
        .Build();

    /// <summary>The YAML reader. Read through <see cref="NoAliasParser"/>: see there why aliases are rejected.</summary>
    private static readonly IDeserializer YamlReader = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .WithDuplicateKeyChecking()
        .WithTypeConverter(new JobYamlConverter())
        .WithNodeDeserializer(new ScalarItemDeserializer(), where => where.OnTop())
        .Build();

    private static readonly EmitterSettings YamlEmitterSettings = EmitterSettings.Default
        .WithBestWidth(int.MaxValue)
        .WithIndentedSequences()
        .WithNewLine("\n");

    /// <summary>Storage JSON: indented, every non-null value.</summary>
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions(indented: true, JsonIgnoreCondition.WhenWritingNull);

    /// <summary>Share-code JSON: compact, defaults (false, null) left out.</summary>
    private static readonly JsonSerializerOptions CompactJsonOptions = CreateJsonOptions(indented: false, JsonIgnoreCondition.WhenWritingDefault);

    public static string ToYaml(MitPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var writer = new StringWriter { NewLine = "\n" };
        YamlWriter.Serialize(new Emitter(writer, YamlEmitterSettings), plan);
        return writer.ToString();
    }

    /// <summary>Reads a plan from YAML. Text that is JSON (our camelCase storage form) is read as JSON.</summary>
    /// <exception cref="FormatException">Not a plan, invalid YAML, a value of the wrong type, or another format.</exception>
    public static MitPlan FromYaml(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        var text = YamlNodes.StripBom(yaml);
        var start = text.TrimStart();
        if (LooksLikeShareCode(start))
            throw new FormatException("This is a MitCue share code, not plan YAML: import it as a share code.");
        if (start.StartsWith('{') && IsJson(text))
            return FromJson(text);

        MitPlan? plan;
        try
        {
            plan = YamlReader.Deserialize<MitPlan>(new NoAliasParser(new Parser(new StringReader(text))));
        }
        catch (YamlException ex)
        {
            throw new FormatException($"Invalid plan YAML{At(ex.Start)}: {Describe(ex)}", ex);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or InvalidCastException)
        {
            throw new FormatException($"Invalid plan YAML (check brackets, quotes and indentation): {ex.Message}", ex);
        }

        return Finish(plan ?? throw new FormatException("The plan YAML is empty."));
    }

    public static string ToJson(MitPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return JsonSerializer.Serialize(plan, JsonOptions);
    }

    /// <exception cref="FormatException">Not a plan, invalid JSON, a value of the wrong type, or another format.</exception>
    public static MitPlan FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var text = YamlNodes.StripBom(json);
        if (LooksLikeShareCode(text.TrimStart()))
            throw new FormatException("This is a MitCue share code, not plan JSON: import it as a share code.");
        return Finish(ReadJson(text, "plan JSON"));
    }

    /// <summary>"MITCUE1:" + base64url(deflate(json)).</summary>
    public static string ToShareCode(MitPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var json = JsonSerializer.SerializeToUtf8Bytes(plan, CompactJsonOptions);
        using var packed = new MemoryStream();
        using (var deflate = new DeflateStream(packed, CompressionLevel.SmallestSize, leaveOpen: true))
            deflate.Write(json);
        return ShareCodePrefix + Base64UrlEncode(packed.ToArray());
    }

    /// <summary>
    /// Reads a share code. Surrounding whitespace, quotes and backticks and line breaks inside the code (from chat
    /// wrapping) are ignored; the prefix is case-insensitive.
    /// </summary>
    /// <exception cref="FormatException">Not a share code, a code from a newer MitCue, or a damaged/incomplete code.</exception>
    public static MitPlan FromShareCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var compact = new string(code.Where(c => !char.IsWhiteSpace(c)).ToArray()).Trim('`', '"', '\'');
        var colon = compact.IndexOf(':');
        var prefix = colon > 0 ? compact[..colon] : string.Empty;
        if (!prefix.Equals(ShareCodePrefix[..^1], StringComparison.OrdinalIgnoreCase))
        {
            if (prefix.StartsWith("MITCUE", StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException(
                    $"This share code has the format {prefix.ToUpperInvariant()}, which this version of MitCue cannot read " +
                    $"(it reads {ShareCodePrefix[..^1]}). Update MitCue and try again.");
            }

            var hint = compact.StartsWith('{') ? " This looks like plan JSON." : compact.Contains("format:", StringComparison.Ordinal) ? " This looks like plan YAML." : string.Empty;
            throw new FormatException($"Not a MitCue share code: a share code starts with \"{ShareCodePrefix}\".{hint}");
        }

        byte[] packed;
        try
        {
            packed = Base64UrlDecode(compact[(colon + 1)..]);
        }
        catch (FormatException ex)
        {
            throw new FormatException("The share code is damaged: it contains characters that do not belong to a share code.", ex);
        }
        if (packed.Length == 0)
            throw new FormatException($"The share code is empty after \"{ShareCodePrefix}\".");

        string json;
        try
        {
            json = Inflate(packed);
        }
        catch (InvalidDataException ex)
        {
            throw new FormatException("The share code is damaged or incomplete (it could not be unpacked). Copy the whole code and try again.", ex);
        }

        return Finish(ReadJson(json, "share code"));
    }

    /// <summary>
    /// Reads a plan in whichever form <paramref name="text"/> is: a share code, JSON or YAML. A Markdown code fence
    /// around the text (as LLMs write it) is ignored.
    /// </summary>
    /// <exception cref="FormatException">The text is none of the supported forms, or the plan is invalid.</exception>
    public static MitPlan Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var body = StripCodeFence(YamlNodes.StripBom(text).Trim());
        if (body.Length == 0)
            throw new FormatException("There is no plan text.");
        if (LooksLikeShareCode(body))
            return FromShareCode(body);
        return FromYaml(body);
    }

    private static JsonSerializerOptions CreateJsonOptions(bool indented, JsonIgnoreCondition ignore)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = ignore,
            IgnoreReadOnlyProperties = true,
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            AllowDuplicateProperties = false,
            WriteIndented = indented,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.Converters.Add(new JobJsonConverter()); // before the enum converter, so jobs use the YAML rules
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly();
        return options;
    }

    private static MitPlan ReadJson(string json, string what)
    {
        MitPlan? plan;
        try
        {
            plan = JsonSerializer.Deserialize<MitPlan>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            var where = ex.LineNumber is { } line ? $" at line {line + 1}" + (ex.Path is { Length: > 0 } path ? $" ({path})" : string.Empty) : string.Empty;
            var message = what == "share code"
                ? $"The share code is damaged: its content is not a valid plan{where}. {ex.Message}"
                : $"Invalid plan JSON{where}: {ex.Message}";
            throw new FormatException(message, ex);
        }
        return plan ?? throw new FormatException($"The {what} contains no plan.");
    }

    /// <summary>
    /// Checks the format, repairs nulls so callers can rely on non-null collections, and writes slot synonyms as the
    /// standard slots (<see cref="PlanNormalizer.NormalizeSlots"/>).
    /// </summary>
    private static MitPlan Finish(MitPlan plan)
    {
        var format = plan.Format?.Trim();
        if (!string.IsNullOrEmpty(format) && !string.Equals(format, MitPlan.CurrentFormat, StringComparison.OrdinalIgnoreCase))
        {
            var why = format.StartsWith("mitcue-plan/", StringComparison.OrdinalIgnoreCase)
                ? "The plan was made by a newer version of MitCue; update the plugin to read it."
                : "This does not look like a MitCue plan.";
            throw new FormatException($"Unsupported plan format '{format}': this version of MitCue reads '{MitPlan.CurrentFormat}'. {why}");
        }
        plan.Format = MitPlan.CurrentFormat;

        if (string.IsNullOrWhiteSpace(plan.Id))
            plan.Id = Guid.NewGuid().ToString("D");
        plan.Name ??= string.Empty;
        plan.Fight ??= string.Empty;
        if (plan.Source != null)
        {
            plan.Source.Kind ??= string.Empty;
            plan.Source.Tabs ??= [];
            plan.Source.Tabs.RemoveAll(t => t == null);
        }

        plan.Assignees ??= [];
        plan.Assignees.RemoveAll(a => a == null);
        foreach (var a in plan.Assignees)
            a.Key ??= string.Empty;

        plan.Entries ??= [];
        plan.Entries.RemoveAll(e => e == null);
        foreach (var e in plan.Entries)
        {
            e.Id ??= string.Empty;
            e.Mechanic ??= string.Empty;
            e.Who ??= new WhoSpec();
            e.Do ??= [];
            RepairItems(e.Do);
        }

        // Every reader (the plugin's store and AI route, the CLI) then selects calls from the same slots.
        PlanNormalizer.NormalizeSlots(plan);
        return plan;
    }

    private static void RepairItems(List<PlanItem> items)
    {
        items.RemoveAll(i => i == null);
        foreach (var item in items)
        {
            if (item.Any != null)
                RepairItems(item.Any);
        }
    }

    /// <summary>
    /// True when the text starts like a share code, also behind the whitespace, quotes and backticks that chat puts
    /// around it (Discord inline code), which <see cref="FromShareCode"/> ignores.
    /// </summary>
    private static bool LooksLikeShareCode(string text)
    {
        var start = 0;
        while (start < text.Length && (char.IsWhiteSpace(text[start]) || text[start] is '`' or '"' or '\''))
            start++;
        var code = text.AsSpan(start);
        return code.StartsWith("MITCUE", StringComparison.OrdinalIgnoreCase) && code.IndexOf(':') is > 6 and < 16;
    }

    /// <summary>
    /// The job spellings every plan reader accepts: an abbreviation ("WHM", "whm"), an English job name ("White Mage"),
    /// a ClassJob id ("24"), or "None". Shared by the YAML and JSON readers, so a plan read from any format can be
    /// written as any other and read back.
    /// </summary>
    private static bool TryReadJob(string? text, out Job job)
    {
        if (Jobs.TryParse(text, out job))
            return true;
        job = Job.None;
        return string.Equals(text?.Trim(), nameof(Job.None), StringComparison.OrdinalIgnoreCase);
    }

    private static string UnknownJob(string? text) => $"Unknown job '{text}' (use an abbreviation such as WHM or SCH).";

    private static bool IsJson(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string StripCodeFence(string text)
    {
        if (!text.StartsWith("```", StringComparison.Ordinal))
            return text;
        var firstLineEnd = text.IndexOf('\n');
        if (firstLineEnd < 0)
            return text.Trim('`').Trim();
        var body = text[(firstLineEnd + 1)..].TrimEnd();
        if (body.EndsWith("```", StringComparison.Ordinal))
            body = body[..^3];
        return body.Trim();
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Decodes base64url (also accepts standard base64 characters and padding).</summary>
    private static byte[] Base64UrlDecode(string text)
    {
        var s = text.TrimEnd('=').Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 1:
                throw new FormatException("Invalid base64 length.");
            case 2:
                s += "==";
                break;
            case 3:
                s += "=";
                break;
        }
        return Convert.FromBase64String(s);
    }

    private static string Inflate(byte[] packed)
    {
        using var input = new MemoryStream(packed);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + read > MaxShareCodeBytes)
                throw new FormatException("The share code is too large to be a plan.");
            output.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length);
    }

    private static string At(Mark mark) => mark.Line > 0 ? $" at {YamlNodes.Where(mark)}" : string.Empty;

    /// <summary>YamlDotNet wraps type errors as "Exception during deserialization"; the inner message says what was wrong.</summary>
    private static string Describe(YamlException ex)
    {
        var inner = ex.InnerException;
        while (inner is YamlException { InnerException: { } deeper })
            inner = deeper;
        return inner?.Message ?? ex.Message;
    }

    /// <summary>
    /// Flow style for the small mappings and string lists, as in docs/DESIGN.md §3.2; double quotes for multi-line
    /// text (block scalars would normalize "\r\n" and read worse in plan files).
    /// </summary>
    private sealed class CompactStyleEmitter(IEventEmitter next) : ChainedEventEmitter(next)
    {
        /// <summary>CR, LF, NEL, LINE SEPARATOR, PARAGRAPH SEPARATOR.</summary>
        private static readonly SearchValues<char> LineBreaks = SearchValues.Create([(char)0x0D, (char)0x0A, (char)0x85, (char)0x2028, (char)0x2029]);

        public override void Emit(ScalarEventInfo eventInfo, IEmitter emitter)
        {
            if (eventInfo.Source.Value is string text && text.AsSpan().IndexOfAny(LineBreaks) >= 0)
                eventInfo.Style = ScalarStyle.DoubleQuoted;
            base.Emit(eventInfo, emitter);
        }

        public override void Emit(MappingStartEventInfo eventInfo, IEmitter emitter)
        {
            var type = eventInfo.Source.Type;
            if (type == typeof(PlanItem) || type == typeof(WhoSpec) || type == typeof(Assignee) || type == typeof(PlanSource))
                eventInfo.Style = MappingStyle.Flow;
            base.Emit(eventInfo, emitter);
        }

        public override void Emit(SequenceStartEventInfo eventInfo, IEmitter emitter)
        {
            if (eventInfo.Source.Type == typeof(List<string>))
                eventInfo.Style = SequenceStyle.Flow;
            base.Emit(eventInfo, emitter);
        }
    }

    /// <summary>
    /// Leaves out computed (read-only) properties, empty lists that cannot be null (the readers turn a missing one into
    /// an empty one) and an all-empty <c>who</c>. An empty list that can be null is kept, because there empty and
    /// missing differ: <c>any: []</c> is an (empty) either/or group, no <c>any</c> means the item is not a group.
    /// </summary>
    private sealed class OmitEmptyVisitor(IObjectGraphVisitor<IEmitter> next) : ChainedObjectGraphVisitor(next)
    {
        public override bool EnterMapping(IPropertyDescriptor key, IObjectDescriptor value, IEmitter context, ObjectSerializer serializer)
        {
            if (!key.CanWrite)
                return false;
            if (value.Value is ICollection { Count: 0 } && !key.AllowNulls) // AllowNulls: declared nullable (List<PlanItem>? Any)
                return false; // strings are not ICollection, so "" is still written
            if (value.Value is WhoSpec { Slot: null, Job: null, Assignee: null })
                return false;
            return base.EnterMapping(key, value, context, serializer);
        }
    }

    /// <summary>Reads jobs leniently with <see cref="TryReadJob"/> ("WHM", "whm", "White Mage", "24"); "None" stays <see cref="Job.None"/>.</summary>
    private sealed class JobYamlConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type) => type == typeof(Job) || type == typeof(Job?);

        public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
        {
            var scalar = parser.Consume<Scalar>();
            if (scalar.Style is ScalarStyle.Plain or ScalarStyle.Any && YamlNodes.IsNullText(scalar.Value))
                return type == typeof(Job?) ? null : Job.None;
            if (TryReadJob(scalar.Value, out var job))
                return job;
            throw new YamlException(scalar.Start, scalar.End, UnknownJob(scalar.Value));
        }

        public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer) =>
            emitter.Emit(new Scalar(value?.ToString() ?? string.Empty));
    }

    /// <summary>
    /// Reads JSON jobs with the YAML rules (<see cref="TryReadJob"/>), from a string or a number. The stock enum
    /// converter also took any number (99) and flag combinations ("WHM, SCH" as SCH), which YAML rejects. Writes
    /// what that converter wrote: the abbreviation (a value outside the enum as its number, which no reader accepts).
    /// </summary>
    private sealed class JobJsonConverter : JsonConverter<Job>
    {
        public override Job Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number => Encoding.UTF8.GetString(reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan),
                JsonTokenType.Null => null, // only for a non-nullable Job: a Job? null never reaches the converter
                _ => throw new JsonException($"A job must be a text such as \"WHM\", not {reader.TokenType}."),
            };
            if (text == null)
                return Job.None; // as the YAML reader does
            return TryReadJob(text, out var job) ? job : throw new JsonException(UnknownJob(text));
        }

        public override void Write(Utf8JsonWriter writer, Job value, JsonSerializerOptions options)
        {
            if (Enum.IsDefined(value))
                writer.WriteStringValue(value.ToString());
            else
                writer.WriteNumberValue((byte)value);
        }
    }

    /// <summary>
    /// Rejects YAML aliases (<c>*name</c>) before the deserializer resolves them. A resolved alias is the very object
    /// its anchor (<c>&amp;name</c>) made, so <c>&amp;x {any: [*x]}</c> builds an item that contains itself (repairing
    /// it overflowed the stack, which ends the process), and a few lines that each refer twice to the line before
    /// expand to millions of items. Plans never need aliases; an anchor alone is harmless and still accepted.
    /// </summary>
    private sealed class NoAliasParser(IParser inner) : IParser
    {
        public ParsingEvent? Current => inner.Current;

        public bool MoveNext()
        {
            var more = inner.MoveNext();
            if (inner.Current is AnchorAlias alias)
                throw new YamlException(alias.Start, alias.End, $"YAML aliases such as '*{alias.Value}' are not allowed in plans: write the item out in full.");
            return more;
        }
    }

    /// <summary>A plain text item in <c>do</c> (e.g. <c>- Temperance</c>) is read as a note, so nothing typed is lost.</summary>
    private sealed class ScalarItemDeserializer : INodeDeserializer
    {
        public bool Deserialize(IParser reader, Type expectedType, Func<IParser, Type, object?> nestedObjectDeserializer, out object? value, ObjectDeserializer rootDeserializer)
        {
            value = null;
            if (expectedType != typeof(PlanItem) || !reader.Accept<Scalar>(out var scalar))
                return false;
            if (scalar.Style is ScalarStyle.Plain or ScalarStyle.Any && YamlNodes.IsNullText(scalar.Value))
                return false; // null item: handled (and later dropped) by the default deserializers
            reader.MoveNext();
            value = PlanItem.ForNote(scalar.Value);
            return true;
        }
    }
}
