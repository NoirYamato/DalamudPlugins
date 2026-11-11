using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;

namespace MitCue.UI;

/// <summary>
/// A multi-line text box over a persistent UTF-8 buffer, for pasting whole sheets and AI answers: nothing about the
/// whole text is re-encoded, copied or scanned per frame. <see cref="Text"/>, <see cref="Lines"/>, <see cref="Bytes"/>
/// and <see cref="IsBlank"/> are worked out when the text changes, so drawing code can read them every frame without
/// allocating (a <c>Text.Trim()</c> per frame copies the whole text, onto the Large Object Heap above 42.5K characters).
/// Main thread only.
/// </summary>
internal sealed class TextBox
{
    private readonly int capacity;
    private byte[]? buffer;

    /// <summary>Creates an empty box.</summary>
    /// <param name="capacity">Buffer size in bytes, including the terminating NUL.</param>
    public TextBox(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 2);
        this.capacity = capacity;
    }

    /// <summary>The text.</summary>
    public string Text { get; private set; } = string.Empty;

    /// <summary>True when the text is empty or only white space.</summary>
    public bool IsBlank { get; private set; } = true;

    /// <summary>True when the last <see cref="Set"/> had to cut the text to fit the buffer.</summary>
    public bool Truncated { get; private set; }

    /// <summary>Lines of <see cref="Text"/>.</summary>
    public int Lines { get; private set; }

    /// <summary>UTF-8 size of <see cref="Text"/>.</summary>
    public int Bytes { get; private set; }

    /// <summary>Replaces the text (cut at a character boundary when it does not fit).</summary>
    public void Set(string value)
    {
        var buf = this.Buffer();
        var bytes = Encoding.UTF8.GetBytes(value);
        var length = bytes.Length;
        this.Truncated = length >= this.capacity;
        if (this.Truncated)
        {
            length = this.capacity - 1;
            while (length > 0 && (bytes[length] & 0xC0) == 0x80)
                length--; // do not cut a UTF-8 sequence
        }

        Array.Copy(bytes, buf, length);
        Array.Clear(buf, length, buf.Length - length);
        this.SetText(Encoding.UTF8.GetString(buf, 0, length), length);
    }

    /// <summary>Draws the box; returns true when the user edited the text.</summary>
    public bool Draw(string id, Vector2 size, ImGuiInputTextFlags flags)
    {
        var buf = this.Buffer();
        if (!ImGui.InputTextMultiline(id, buf, size, flags))
            return false;
        var end = Array.IndexOf(buf, (byte)0);
        var length = end < 0 ? buf.Length : end;
        this.SetText(Encoding.UTF8.GetString(buf, 0, length), length);
        this.Truncated = false;
        return true;
    }

    private void SetText(string text, int bytes)
    {
        this.Text = text;
        this.Bytes = bytes;
        this.IsBlank = string.IsNullOrWhiteSpace(text);
        this.Lines = text.Length == 0 ? 0 : text.Count(c => c == '\n') + 1;
    }

    private byte[] Buffer() => this.buffer ??= new byte[this.capacity];
}
