using MitCue.Core.Import;

namespace MitCue.Core.Tests.Import;

public class ParsedTimeTests
{
    [Theory]
    [InlineData("1:18", 78, null, false, "")]
    [InlineData("0:07", 7, null, false, "")]
    [InlineData("(1:18)", 78, null, false, "")]
    [InlineData("~1:22", 82, null, true, "")]
    [InlineData("≈3:04", 184, null, true, "")]
    [InlineData("~ 0:40", 40, null, true, "")]
    [InlineData("about 0:40", 40, null, true, "")]
    [InlineData("ca. 0:40", 40, null, true, "")]
    [InlineData("at 1:18", 78, null, false, "")]
    [InlineData("@1:18", 78, null, false, "")]
    [InlineData("at ~1:18", 78, null, true, "")]
    [InlineData("2:44–2:46", 164, 166.0, false, "")]
    [InlineData("1:47 – 1:56", 107, 116.0, false, "")]
    [InlineData("2:44-2:46", 164, 166.0, false, "")]
    [InlineData("2:44 to 2:46", 164, 166.0, false, "")]
    [InlineData("3:16–~3:18", 196, 198.0, true, "")]
    [InlineData("0:28 / 0:37 / 0:46 / 0:55", 28, null, false, "37,46,55")]
    [InlineData("0:08 / 0:11", 8, null, false, "11")]
    [InlineData("0:28, 0:37", 28, null, false, "37")]
    [InlineData("-0:05", -5, null, false, "")]
    [InlineData("1:02:03", 3723, null, false, "")]
    [InlineData("1:18.5", 78.5, null, false, "")]
    [InlineData("01:18", 78, null, false, "")]
    [InlineData("0:40:", 40, null, false, "")] // "0:40: Rep"
    [InlineData("1:21:00", 4860, null, false, "")] // three parts are h:mm:ss (the layout detector handles typed m:ss)
    public void TryParse_ReadsSheetTimes(string text, double seconds, double? end, bool approx, string more)
    {
        Assert.True(ParsedTime.TryParse(text, out var t));
        Assert.Equal(seconds, t.Seconds, 3);
        Assert.Equal(end, t.End);
        Assert.Equal(approx, t.Approx);
        Assert.Equal(more, string.Join(",", t.More));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Damage Events")]
    [InlineData("1:18 Temp")]
    [InlineData("~3 secs")]
    [InlineData("13s")]
    [InlineData("1:7")]
    [InlineData("P1 — Omega")]
    [InlineData("1:18 (pull) / Kera 0:20")]
    public void TryParse_RejectsTextThatIsNotOnlyATime(string? text) => Assert.False(ParsedTime.TryParse(text, out _));

    [Fact]
    public void FindAll_ReturnsPositionsAndText()
    {
        const string text = "Recit 0:12 → Deploy 0:17. 17 of 58 Deploy after the raidwide instead (0:31).";
        var all = ParsedTime.FindAll(text);

        Assert.Equal([12.0, 17, 31], all.Select(m => m.Time.Seconds));
        Assert.Equal(["0:12", "0:17", "0:31"], all.Select(m => m.Time.Text));
        Assert.All(all, m => Assert.Equal(m.Time.Text, text.Substring(m.Index, m.Length)));
    }

    [Fact]
    public void FindAll_KeepsTheApproximationAndRestOfAParenthetical()
    {
        var all = ParsedTime.FindAll("right after the 1st stack, ~1:22");

        var m = Assert.Single(all);
        Assert.Equal("~1:22", m.Time.Text);
        Assert.True(m.Time.Approx);
        Assert.Equal(27, m.Index);
    }

    [Fact]
    public void FindAll_TimeFollowedByAColon()
    {
        var m = Assert.Single(ParsedTime.FindAll("0:40: Rep"));
        Assert.Equal(("0:40", 0, 4), (m.Time.Text, m.Index, m.Length));
        Assert.Empty(ParsedTime.FindAll("1:2:3:4:5"));
    }

    [Fact]
    public void FindAll_DoesNotMergeTimesSeparatedByWords()
    {
        var all = ParsedTime.FindAll("Back at 1:25 if P2 lasted 2:14 or longer");

        Assert.Equal(2, all.Count);
        Assert.Equal("at 1:25", all[0].Time.Text);
        Assert.Equal("2:14", all[1].Time.Text);
    }

    [Fact]
    public void ToStringAndAll_DescribeTheTime()
    {
        Assert.True(ParsedTime.TryParse("~2:44–2:46", out var range));
        Assert.Equal("~2:44-2:46", range.ToString());
        Assert.Equal([164.0, 166], range.All);
        Assert.True(range.IsRange);

        Assert.True(ParsedTime.TryParse("0:28 / 0:37", out var list));
        Assert.Equal("0:28,0:37", list.ToString());
        Assert.Equal("0:28 / 0:37", list.Text);
        Assert.Equal([28.0, 37], list.All);
        Assert.Equal("1:05", ParsedTime.Format(65));
    }
}
