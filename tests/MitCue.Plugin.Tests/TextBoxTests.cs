using MitCue.UI;

namespace MitCue.PluginTests;

public class TextBoxTests
{
    [Fact]
    public void KnowsWhetherItIsBlank()
    {
        var box = new TextBox(64);
        Assert.True(box.IsBlank);

        box.Set(" \n\t  \n");
        Assert.True(box.IsBlank);
        Assert.Equal(3, box.Lines);

        box.Set("\nformat: mitcue-plan/1\n");
        Assert.False(box.IsBlank);

        box.Set(string.Empty);
        Assert.True(box.IsBlank);
        Assert.Equal(0, box.Lines);
    }

    [Fact]
    public void ReadingTheBlankStateEveryFrameAllocatesNothing()
    {
        // Review finding: the Source step called Text.Trim() every frame to enable its button, copying the whole pasted
        // text each time (105 KB per frame for a 400-entry plan, on the Large Object Heap above 42.5K characters).
        var box = new TextBox(1024 * 1024);
        box.Set("\n" + string.Concat(Enumerable.Repeat("  - { id: e001, mechanic: p1.pantokrator }\n", 1500)) + "\n\n");
        _ = box.IsBlank;

        var before = GC.GetAllocatedBytesForCurrentThread();
        var blank = false;
        for (var frame = 0; frame < 600; frame++)
            blank |= box.IsBlank;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.False(blank);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void CutsTooLongTextAtACharacterBoundary()
    {
        var box = new TextBox(8);
        box.Set("abcdefé"); // 8 bytes: the "é" (2 bytes) does not fit after 6 ASCII bytes + NUL

        Assert.True(box.Truncated);
        Assert.Equal("abcdef", box.Text);
        Assert.Equal(6, box.Bytes);
    }
}
