using System.Globalization;
using System.Text;

namespace MitCue.Core.Timelines;

/// <summary>
/// Minimal JSON5 reader for timeline sync parameters (cactbot parses them with the json5 package):
/// bare or quoted keys, single- or double-quoted strings with JSON5 escapes, trailing commas,
/// comments, and JSON5 numbers. Objects become <see cref="Json5Object"/>, arrays
/// <c>List&lt;object?&gt;</c>, strings <see cref="string"/>, numbers <see cref="double"/>,
/// <c>true</c>/<c>false</c> <see cref="bool"/> and <c>null</c> null.
/// </summary>
internal sealed class Json5
{
    private readonly string text;
    private int pos;

    private Json5(string text) => this.text = text;

    /// <summary>Parses one JSON5 value that must span the whole text.</summary>
    /// <exception cref="FormatException">The text is not valid JSON5.</exception>
    public static object? Parse(string text)
    {
        var reader = new Json5(text);
        try
        {
            reader.SkipSpace();
            var value = reader.ReadValue();
            reader.SkipSpace();
            if (reader.pos < text.Length)
                throw reader.Error($"unexpected '{text[reader.pos]}'");
            return value;
        }
        catch (OverflowException)
        {
            throw reader.Error("number out of range");
        }
    }

    private static bool IsSpace(char c) =>
        c is '\t' or '\v' or '\f' or ' ' or '\u00A0' or '\uFEFF' or '\n' or '\r' or '\u2028' or '\u2029'
        || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator;

    private static bool IsIdentifierStart(char c) => c is '$' or '_' || char.IsLetter(c);

    private static bool IsIdentifierPart(char c) =>
        IsIdentifierStart(c) || char.IsDigit(c) || c is '\u200C' or '\u200D'
        || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.ConnectorPunctuation;

    private FormatException Error(string message) => new($"{message} at position {this.pos + 1}");

    private char Peek(int ahead = 0) => this.pos + ahead < this.text.Length ? this.text[this.pos + ahead] : '\0';

    private bool AtEnd => this.pos >= this.text.Length;

    private void SkipSpace()
    {
        while (!this.AtEnd)
        {
            var c = this.text[this.pos];
            if (IsSpace(c))
            {
                this.pos++;
            }
            else if (c == '/' && this.Peek(1) == '/')
            {
                while (!this.AtEnd && this.text[this.pos] is not ('\n' or '\r' or '\u2028' or '\u2029'))
                    this.pos++;
            }
            else if (c == '/' && this.Peek(1) == '*')
            {
                var end = this.text.IndexOf("*/", this.pos + 2, StringComparison.Ordinal);
                if (end < 0)
                    throw this.Error("unterminated comment");
                this.pos = end + 2;
            }
            else
            {
                return;
            }
        }
    }

    private object? ReadValue()
    {
        if (this.AtEnd)
            throw this.Error("unexpected end of input");
        var c = this.text[this.pos];
        switch (c)
        {
            case '{':
                return this.ReadObject();
            case '[':
                return this.ReadArray();
            case '"' or '\'':
                return this.ReadString();
        }

        if (c is '+' or '-' or '.' or 'I' or 'N' || char.IsAsciiDigit(c))
            return this.ReadNumber();
        if (this.TryWord("true"))
            return true;
        if (this.TryWord("false"))
            return false;
        if (this.TryWord("null"))
            return null;
        throw this.Error($"unexpected '{c}'");
    }

    private bool TryWord(string word)
    {
        if (string.CompareOrdinal(this.text, this.pos, word, 0, word.Length) != 0)
            return false;
        if (IsIdentifierPart(this.Peek(word.Length)))
            return false;
        this.pos += word.Length;
        return true;
    }

    private Json5Object ReadObject()
    {
        this.pos++; // {
        var result = new Json5Object();
        this.SkipSpace();
        while (true)
        {
            if (this.Peek() == '}')
            {
                this.pos++;
                return result;
            }

            var key = this.Peek() is '"' or '\'' ? this.ReadString() : this.ReadIdentifier();
            this.SkipSpace();
            if (this.Peek() != ':')
                throw this.Error("expected ':'");
            this.pos++;
            this.SkipSpace();
            result.Set(key, this.ReadValue());
            this.SkipSpace();
            if (this.Peek() == ',')
            {
                this.pos++;
                this.SkipSpace();
                continue;
            }

            if (this.Peek() == '}')
            {
                this.pos++;
                return result;
            }

            throw this.AtEnd ? this.Error("unterminated object") : this.Error($"unexpected '{this.Peek()}' in object");
        }
    }

    private List<object?> ReadArray()
    {
        this.pos++; // [
        var result = new List<object?>();
        this.SkipSpace();
        while (true)
        {
            if (this.Peek() == ']')
            {
                this.pos++;
                return result;
            }

            result.Add(this.ReadValue());
            this.SkipSpace();
            if (this.Peek() == ',')
            {
                this.pos++;
                this.SkipSpace();
                continue;
            }

            if (this.Peek() == ']')
            {
                this.pos++;
                return result;
            }

            throw this.AtEnd ? this.Error("unterminated array") : this.Error($"unexpected '{this.Peek()}' in array");
        }
    }

    private string ReadIdentifier()
    {
        var sb = new StringBuilder();
        while (!this.AtEnd)
        {
            var c = this.text[this.pos];
            if (c == '\\' && this.Peek(1) == 'u')
            {
                this.pos += 2;
                c = this.ReadHex(4);
                if (!(sb.Length == 0 ? IsIdentifierStart(c) : IsIdentifierPart(c)))
                    throw this.Error("invalid identifier escape");
                sb.Append(c);
                continue;
            }

            if (!(sb.Length == 0 ? IsIdentifierStart(c) : IsIdentifierPart(c)))
                break;
            sb.Append(c);
            this.pos++;
        }

        if (sb.Length == 0)
            throw this.AtEnd ? this.Error("unexpected end of input") : this.Error($"unexpected '{this.Peek()}' (expected a key)");
        return sb.ToString();
    }

    private string ReadString()
    {
        var quote = this.text[this.pos++];
        var sb = new StringBuilder();
        while (true)
        {
            if (this.AtEnd)
                throw this.Error("unterminated string");
            var c = this.text[this.pos++];
            if (c == quote)
                return sb.ToString();
            if (c is '\n' or '\r')
                throw this.Error("line break in string");
            if (c != '\\')
            {
                sb.Append(c);
                continue;
            }

            if (this.AtEnd)
                throw this.Error("unterminated string");
            var e = this.text[this.pos++];
            switch (e)
            {
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'v': sb.Append('\v'); break;
                case '0':
                    if (char.IsAsciiDigit(this.Peek()))
                        throw this.Error("invalid escape");
                    sb.Append('\0');
                    break;
                case 'x': sb.Append(this.ReadHex(2)); break;
                case 'u': sb.Append(this.ReadHex(4)); break;
                case '\r':
                    if (this.Peek() == '\n')
                        this.pos++;
                    break; // line continuation
                case '\n' or '\u2028' or '\u2029':
                    break; // line continuation
                default:
                    if (char.IsAsciiDigit(e))
                        throw this.Error("invalid escape");
                    sb.Append(e); // JSON5 drops the backslash before any other character: "\." is "."
                    break;
            }
        }
    }

    private char ReadHex(int digits)
    {
        if (this.pos + digits > this.text.Length)
            throw this.Error("invalid escape");
        var hex = this.text.Substring(this.pos, digits);
        if (!hex.All(Uri.IsHexDigit))
            throw this.Error("invalid escape");
        this.pos += digits;
        return (char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private double ReadNumber()
    {
        var start = this.pos;
        var sign = 1.0;
        if (this.Peek() is '+' or '-')
        {
            sign = this.Peek() == '-' ? -1 : 1;
            this.pos++;
        }

        if (this.TryWord("Infinity"))
            return sign * double.PositiveInfinity;
        if (this.TryWord("NaN"))
            return double.NaN;
        if (this.Peek() == '0' && this.Peek(1) is 'x' or 'X')
        {
            this.pos += 2;
            var hexStart = this.pos;
            while (Uri.IsHexDigit(this.Peek()))
                this.pos++;
            if (this.pos == hexStart)
                throw this.Error("invalid number");
            return sign * (double)ulong.Parse(this.text[hexStart..this.pos], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        var digitsStart = this.pos;
        while (char.IsAsciiDigit(this.Peek()))
            this.pos++;
        var intDigits = this.pos - digitsStart;
        if (intDigits > 1 && this.text[digitsStart] == '0')
        {
            // JSON5 integer parts are 0 or [1-9][0-9]*: json5 ends the number at the 0 and rejects the next digit.
            this.pos = digitsStart + 1;
            throw this.Error("invalid number (leading zero)");
        }
        var fracDigits = 0;
        if (this.Peek() == '.')
        {
            this.pos++;
            var fracStart = this.pos;
            while (char.IsAsciiDigit(this.Peek()))
                this.pos++;
            fracDigits = this.pos - fracStart;
        }

        if (intDigits == 0 && fracDigits == 0)
            throw this.Error("invalid number");
        if (this.Peek() is 'e' or 'E')
        {
            this.pos++;
            if (this.Peek() is '+' or '-')
                this.pos++;
            var expStart = this.pos;
            while (char.IsAsciiDigit(this.Peek()))
                this.pos++;
            if (this.pos == expStart)
                throw this.Error("invalid number");
        }

        if (IsIdentifierPart(this.Peek()))
            throw this.Error("invalid number");
        var body = this.text[start..this.pos].TrimStart('+');
        return double.Parse(body, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}

/// <summary>A JSON5 object: keys in order of appearance (a repeated key keeps its first position, last value).</summary>
internal sealed class Json5Object
{
    private readonly List<KeyValuePair<string, object?>> members = [];

    public IReadOnlyList<KeyValuePair<string, object?>> Members => this.members;

    public void Set(string key, object? value)
    {
        var i = this.members.FindIndex(m => m.Key == key);
        if (i >= 0)
            this.members[i] = new(key, value);
        else
            this.members.Add(new(key, value));
    }

    public bool TryGet(string key, out object? value)
    {
        foreach (var m in this.members)
        {
            if (m.Key == key)
            {
                value = m.Value;
                return true;
            }
        }

        value = null;
        return false;
    }
}
