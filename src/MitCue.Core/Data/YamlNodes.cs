using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace MitCue.Core.Data;

/// <summary>
/// Helpers for reading YAML through YamlDotNet's representation model, so loaders can name the offending
/// item, field and line in their <see cref="FormatException"/> messages.
/// </summary>
internal static class YamlNodes
{
    /// <summary>Parses the first document of <paramref name="yaml"/>; its root must be a mapping.</summary>
    /// <param name="yaml">Document text (a leading byte-order mark is ignored).</param>
    /// <param name="what">Name of the document for messages, e.g. "fights/top.yaml".</param>
    public static YamlMappingNode LoadMapping(string yaml, string what)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(StripBom(yaml)));
        }
        catch (YamlException ex)
        {
            throw new FormatException($"{what}: invalid YAML at {Where(ex.Start)}: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // YamlDotNet reports some malformed documents (e.g. an unterminated flow sequence) this way.
            throw new FormatException($"{what}: invalid YAML (check brackets, quotes and indentation): {ex.Message}", ex);
        }

        if (stream.Documents.Count == 0 || IsNull(stream.Documents[0].RootNode))
            throw new FormatException($"{what}: the document is empty.");
        if (stream.Documents[0].RootNode is not YamlMappingNode root)
            throw new FormatException($"{what}: expected a mapping of keys at the top level ({Where(stream.Documents[0].RootNode)}).");
        return root;
    }

    /// <summary>Removes a leading byte-order mark (YamlDotNet would make it part of the first key).</summary>
    public static string StripBom(string text) => text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;

    /// <summary>"line 12, column 5" (YamlDotNet marks are 1-based).</summary>
    public static string Where(Mark mark) => $"line {mark.Line}, column {mark.Column}";

    /// <summary>"line 12".</summary>
    public static string Where(YamlNode node) => $"line {node.Start.Line}";

    /// <summary>True for a missing value or a plain null scalar (empty, <c>~</c>, <c>null</c>); quoted strings are never null.</summary>
    public static bool IsNull(YamlNode? node) =>
        node is null ||
        (node is YamlScalarNode { Style: ScalarStyle.Plain or ScalarStyle.Any } scalar && IsNullText(scalar.Value));

    /// <summary>The YAML core-schema spellings of null.</summary>
    public static bool IsNullText(string? text) => text is null or "" or "~" or "null" or "Null" or "NULL";

    /// <summary>The scalar keys of a mapping, in document order.</summary>
    public static IEnumerable<(string Key, YamlNode KeyNode, YamlNode Value)> Entries(YamlMappingNode map)
    {
        foreach (var (k, v) in map.Children)
        {
            if (k is YamlScalarNode { Value: { } key })
                yield return (key, k, v);
        }
    }

    /// <summary>The value of <paramref name="key"/>, or null when the key is missing or its value is null.</summary>
    public static YamlNode? Get(YamlMappingNode map, string key)
    {
        foreach (var (k, v) in map.Children)
        {
            if (k is YamlScalarNode { Value: { } name } && name == key)
                return IsNull(v) ? null : v;
        }
        return null;
    }

    /// <summary>A text value, or null when missing.</summary>
    public static string? GetString(YamlMappingNode map, string key, string context)
    {
        var node = Get(map, key);
        if (node == null)
            return null;
        if (node is not YamlScalarNode scalar)
            throw new FormatException($"{context}: '{key}' must be a single value, not a list or mapping ({Where(node)}).");
        return scalar.Value;
    }

    /// <summary>A non-empty text value; throws when missing or blank.</summary>
    public static string RequireString(YamlMappingNode map, string key, string context)
    {
        var value = GetString(map, key, context)?.Trim();
        if (string.IsNullOrEmpty(value))
            throw new FormatException($"{context}: '{key}' is missing ({Where(map)}).");
        return value;
    }

    /// <summary>A finite number, or null when missing.</summary>
    public static double? GetDouble(YamlMappingNode map, string key, string context)
    {
        var node = Get(map, key);
        return node == null ? null : ToDouble(node, $"'{key}'", context);
    }

    /// <summary>A finite number; throws when missing.</summary>
    public static double RequireDouble(YamlMappingNode map, string key, string context) =>
        GetDouble(map, key, context) ?? throw new FormatException($"{context}: '{key}' is missing ({Where(map)}).");

    /// <summary>A whole number, or null when missing.</summary>
    public static long? GetInteger(YamlMappingNode map, string key, string context)
    {
        var node = Get(map, key);
        if (node == null)
            return null;
        if (node is YamlScalarNode { Value: { } text } &&
            long.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
            return value;
        throw new FormatException($"{context}: '{key}' must be a whole number, not '{Describe(node)}' ({Where(node)}).");
    }

    /// <summary>A whole number; throws when missing.</summary>
    public static long RequireInteger(YamlMappingNode map, string key, string context) =>
        GetInteger(map, key, context) ?? throw new FormatException($"{context}: '{key}' is missing ({Where(map)}).");

    /// <summary>A boolean (true/false, yes/no, on/off), or null when missing.</summary>
    public static bool? GetBool(YamlMappingNode map, string key, string context)
    {
        var node = Get(map, key);
        if (node == null)
            return null;
        if (node is YamlScalarNode { Value: { } text })
        {
            switch (text.Trim().ToLowerInvariant())
            {
                case "true" or "yes" or "on":
                    return true;
                case "false" or "no" or "off":
                    return false;
            }
        }
        throw new FormatException($"{context}: '{key}' must be true or false, not '{Describe(node)}' ({Where(node)}).");
    }

    /// <summary>A list value; a single scalar counts as a one-element list. Empty when missing.</summary>
    public static IReadOnlyList<YamlNode> GetList(YamlMappingNode map, string key, string context)
    {
        var node = Get(map, key);
        return node switch
        {
            null => [],
            YamlSequenceNode seq => seq.Children.Where(n => !IsNull(n)).ToList(),
            YamlScalarNode scalar => [scalar],
            _ => throw new FormatException($"{context}: '{key}' must be a list ({Where(node)})."),
        };
    }

    /// <summary>A list of non-empty texts (a single text counts as a one-element list). Empty when missing.</summary>
    public static IReadOnlyList<string> GetStringList(YamlMappingNode map, string key, string context)
    {
        var result = new List<string>();
        foreach (var item in GetList(map, key, context))
        {
            if (item is not YamlScalarNode { Value: { } text } || string.IsNullOrWhiteSpace(text))
                throw new FormatException($"{context}: every item of '{key}' must be a non-empty text ({Where(item)}).");
            result.Add(text.Trim());
        }
        return result;
    }

    /// <summary>A list of finite numbers (a single number counts as a one-element list). Empty when missing.</summary>
    public static IReadOnlyList<double> GetDoubleList(YamlMappingNode map, string key, string context) =>
        GetList(map, key, context).Select(n => ToDouble(n, $"every item of '{key}'", context)).ToList();

    /// <summary>Parses a finite number from a scalar node.</summary>
    public static double ToDouble(YamlNode node, string what, string context)
    {
        if (node is YamlScalarNode { Value: { } text } && TryParseDouble(text, out var value))
            return value;
        throw new FormatException($"{context}: {what} must be a number, not '{Describe(node)}' ({Where(node)}).");
    }

    /// <summary>Invariant-culture number parsing that rejects NaN and infinities.</summary>
    public static bool TryParseDouble(string? text, out double value) =>
        double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    /// <summary>Short description of a node for messages.</summary>
    public static string Describe(YamlNode node) => node switch
    {
        YamlScalarNode scalar => scalar.Value ?? string.Empty,
        YamlSequenceNode => "(a list)",
        YamlMappingNode => "(a mapping)",
        _ => node.ToString(),
    };
}
