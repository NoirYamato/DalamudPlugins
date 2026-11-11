using System.Text.RegularExpressions;
using MitCue.Core.Import;

namespace MitCue.Core.Tests.Import;

public class CellParserTests
{
    private static readonly CellParser Parser = new();

    /// <summary>
    /// Compact form of a parse: items " / ", steps " → ", tokens " + "; a token is its head (alternatives joined
    /// with "|"), then ‹trailing›, [target], (hints), @time, "*" footnote, "⏱" when ready, "opt" optional, colour,
    /// "bold", "job=" prefix, "struck".
    /// </summary>
    internal static string Render(ParsedCell cell) =>
        string.Join(" / ", cell.Items.Select(i => string.Join(" → ", i.Steps.Select(s => string.Join(" + ", s.Tokens.Select(Render))))));

    private static string Render(ParsedToken t)
    {
        var s = t.Alternatives.Count > 1 ? string.Join("|", t.Alternatives) : t.Head;
        if (t.Trailing is not null)
            s += " ‹" + t.Trailing + "›";
        if (t.Target is not null)
            s += " [" + t.Target.Value + "]";
        foreach (var h in t.Hints)
            s += " (" + h + ")";
        if (t.Time is not null)
            s += " @" + t.Time;
        if (t.Footnote)
            s += " *";
        if (t.WhenReady)
            s += " ⏱";
        if (t.Optional)
            s += " opt";
        if (t.Fg is not null)
            s += " " + t.Fg;
        if (t.Bold)
            s += " bold";
        if (t.JobPrefix is not null)
            s += " job=" + t.JobPrefix;
        if (t.Struck)
            s += " struck";
        return s.Trim();
    }

    // The real cells, with their formatting, from the fixtures.
    public static TheoryData<string, string, string, string> RealCells => new()
    {
        // LPDU: either/or, targets, trailing hints, wide whitespace, footnotes
        { "lpdu", "P1/P2", "E2", "Kera|Soil" },
        { "lpdu", "P1/P2", "E10", "Soil|Kera" },
        { "lpdu", "P1/P2", "B3", "Short CD ‹to 4› [4]" },
        { "lpdu", "P1/P2", "E11", "Everything" },
        { "lpdu", "P3", "G3", "Kera + Zoe Eprog" },
        { "lpdu", "P3", "E4", "CU + Neutralsect" },
        { "lpdu", "P3", "F5", "FeyIllum + 2nd Seraph Charge" },
        { "lpdu", "P3", "F6", "Soil (Debuffs at 13 secs/when rots explode)" },
        { "lpdu", "P3", "G6", "Kera (Debuffs at 13 secs/when rots explode)" },
        { "lpdu", "P4", "B2", "Rep + Short CD ‹to R1› [R1]" },
        { "lpdu", "P4", "J5", "Party Mit ‹if MCH›" },
        { "lpdu", "P4", "G7", "Holos ‹befor stack Hits›" },
        { "lpdu", "P5", "B2", "Rep + Party Mit ‹after Solar Ray›" },
        { "lpdu", "P5", "C2", "Party Mit ‹after Solar Ray›" },
        { "lpdu", "P5", "J2", "Party Mit ‹after Solar Ray›" },
        { "lpdu", "P5", "C3", "Rep + PLD Wings job=PLD" },
        { "lpdu", "P5", "D4", "Lilybell + Debuff ‹at ~3 secs›" },
        { "lpdu", "P5", "E4", "Macrocosmos + Neutralsect ‹if P4 skip›" },
        { "lpdu", "P5", "E5", "Neutralsect ‹no skip›" },
        { "lpdu", "P5", "B7", "Party Mit ‹on CD› ⏱" },
        { "lpdu", "P5", "C7", "Rep + Party Mit ‹on CD› ⏱" },
        { "lpdu", "P5", "F7", "Soil + Expedient + FeyIllum + Seraph" },
        { "lpdu", "P5", "I9", "Feint ‹Autos Omega uptime›" },
        { "lpdu", "P5", "J9", "Party Mit ‹on CD› ⏱ + Dismantle" },
        { "lpdu", "P6", "B4", "Party Mit ‹after 6th puddle› *" },
        { "lpdu", "P6", "C4", "Party Mit ‹after 6th puddle› + Rep *" },
        { "lpdu", "P6", "F4", "Succor ‹Proteans› + Soil + Spreadlo ‹Stack›" },
        { "lpdu", "P6", "B5", "Rep *" },
        { "lpdu", "P6", "C5", "PLD Wings job=PLD" },
        { "lpdu", "P6", "E5", "CU + Neutralsect ‹shields for proteans›" },
        { "lpdu", "P6", "I5", "Feint *" },
        { "lpdu", "P6", "B7", "Party Mit + Rep + Short CD ‹to R1/2› [R1/2]" },
        { "lpdu", "P6", "C7", "Party Mit + Short CD ‹to R1/2› [R1/2]" },
        { "lpdu", "P6", "D7", "Lilybell ‹pop after 1st Hit›" },
        { "lpdu", "P6", "G7", "Eprog + Panhaima + Kera ‹after 1st Hit›" },
        { "lpdu", "P6", "J7", "Party Mit + Dismantle" },
        { "lpdu", "P6", "D9", "Healer LB3" },
        { "lpdu", "P6", "B9", "Tank LB3" },
        { "lpdu", "General Tank Mit til P5", "B2", "Invuln Diffuse Wave Cannon" },
        { "lpdu", "General Tank Mit til P5", "B3", "Rampart ‹as P1 dies›" },
        { "lpdu", "General Tank Mit til P5", "B6", "Rampart + 30% + Short CD" },
        { "lpdu", "General Tank Mit til P5", "B8", "Rampart ‹use during HW cast›" },
        { "lpdu", "General Tank Mit til P5", "B11", "Rampart ‹when ever ready› ⏱" },
        { "lpdu", "General Tank Mit til P5", "B13", "Short CD ‹on R1› [R1]" },
        { "lpdu", "WAR/DRK", "B11", "Holmgang + Nascant ‹Co-Tank› [Co-Tank]" },
        { "lpdu", "WAR/DRK", "C12", "Living Dead + TBN|Oblation ‹Co-Tank› [Co-Tank]" },
        { "lpdu", "DRK/PLD", "B13", "Living Dead + TBN|Oblation ‹Co-Tank› [Co-Tank]" },
        { "lpdu", "GNB/PLD", "B12", "Superbolide + Hoc ‹Co-Tank› [Co-Tank]" },
        { "lpdu", "GNB/PLD", "C6", "Rampart + Bullwark + HS + Intervention ‹Co-Tank› [Co-Tank]" },

        // Carry the Clear: items with times, sequences, targets, approximate times, colours, markers
        { "carry-the-clear", "TOP – White Mage", "C17", "Asylum @~0:40 / Solace + Benison [your 3rd/4th-in-line player]" },
        { "carry-the-clear", "TOP – White Mage", "C18", "Temp @1:18 / Bell (right after the 1st stack) @~1:22 / Plenary @1:31 * #1154cc bold" },
        { "carry-the-clear", "TOP – White Mage", "C24", "Plenary @0:55 * #1154cc bold / Asylum @0:59" },
        { "carry-the-clear", "TOP – White Mage", "C25", "Benison @1:29 opt #808080 / Aquaveil @1:30 opt #808080" },
        { "carry-the-clear", "TOP – White Mage", "C31", "Plenary @0:39 * ⏱ #1154cc bold" },
        { "carry-the-clear", "TOP – White Mage", "C40", "Temperance @0:19 ⏱ / Asylum @0:26 ⏱ / Plenary @0:28 * #1154cc bold" },
        { "carry-the-clear", "TOP – White Mage", "C43", "Benison @0:07 / Aquaveil @0:07 opt #808080" },
        { "carry-the-clear", "TOP – White Mage", "C53", "Temperance @4:09 opt #808080" },
        { "carry-the-clear", "TOP – White Mage", "C66", "Healer LB3 (after the hit) @~4:32" },
        { "carry-the-clear", "TOP – Scholar", "C18", "Protraction + Recit → Adlo → Deploy @1:07 / Illumination @1:15 / Expedient @1:17 / Seraph @1:19 / Soil @1:22" },
        { "carry-the-clear", "TOP – Scholar", "C25", "Protraction + Recit → Adlo → Deploy @1:23 / Seraph @1:33" },
        { "carry-the-clear", "TOP – Scholar", "C38", "Protraction + Recit → Adlo → Deploy (just before P3 ends) @~3:04" },
        { "carry-the-clear", "TOP – Scholar", "C40", "Seraph @0:12 ⏱ / Soil @0:12 / Expedient @0:19 ⏱ / Illumination @0:21 ⏱ / Whispering Dawn @0:25 / Blessing @0:37" },
        { "carry-the-clear", "TOP – Scholar", "C44", "Soil @0:13 / Recit → Adlo → Deploy @0:17" },
        { "carry-the-clear", "TOP – Scholar", "C51", "Protraction + Recit @2:44-2:46 → Adlo @2:50" },
        { "carry-the-clear", "TOP – Scholar", "C52", "Illumination @3:04 opt #808080 / Deploy @3:07 / Seraph @3:07 opt #808080 / Soil @3:08" },
        { "carry-the-clear", "TOP – Scholar", "C67", "Healer LB3 (after the hit) @~4:48" },
        { "carry-the-clear", "TOP – WHM + SCH", "D17", "Asylum @~0:40 / Benison @0:41 / Tetragrammaton @0:49" },
        { "carry-the-clear", "TOP – WHM + SCH", "D18", "Temperance @1:18 / Bell (after the 1st stack) @~1:22 / Plenary @1:31 * #1154cc bold" },
    };

    [Theory]
    [MemberData(nameof(RealCells))]
    public void Parse_RealCells(string key, string tab, string a1, string expected)
    {
        var cell = Fixtures.Cell(key, tab, a1);
        var parsed = Parser.Parse(cell, $"'{tab}'!{a1}");

        Assert.Equal(expected, Render(parsed));
        Assert.All(parsed.Items, i => Assert.Equal($"'{tab}'!{a1}", i.Src));
        Assert.All(parsed.Items.SelectMany(i => i.Steps), s => Assert.Equal($"'{tab}'!{a1}", s.Src));
        Assert.All(parsed.Tokens, t => Assert.Equal($"'{tab}'!{a1}", t.Src));
    }

    [Fact]
    public void RealCells_CoverAtLeastFortyCells() => Assert.True(RealCells.Count() >= 40);

    [Theory]
    [InlineData("Adlo -> Deploy", "Adlo → Deploy")]
    [InlineData("Recit => Adlo ⇒ Deploy", "Recit → Adlo → Deploy")]
    [InlineData("Soil, Succor", "Soil + Succor")]
    [InlineData("Soil & Succor", "Soil + Succor")]
    [InlineData("Kera\nSoil", "Kera + Soil")]
    [InlineData("Kera after\n1st Hit", "Kera ‹after 1st Hit›")]
    [InlineData("Nascent        Co-Tank", "Nascent ‹Co-Tank› [Co-Tank]")]
    [InlineData("Soil        (0:12)", "Soil @0:12")]
    [InlineData("Kera 0:28 / 0:37", "Kera @0:28,0:37")]
    [InlineData("Deploy 0:49", "Deploy @0:49")]
    [InlineData("Temp at 1:18", "Temp @1:18")]
    [InlineData("1:18 Temp", "Temp @1:18")]
    [InlineData("Recit 0:12 → Deploy 0:17", "Recit @0:12 → Deploy @0:17")]
    [InlineData("Temp (1:18)/Bell (1:22)", "Temp @1:18 / Bell @1:22")]
    [InlineData("Kera; Soil", "Kera / Soil")]
    [InlineData("Bene (MT)", "Bene [MT]")]
    [InlineData("Bene on MT", "Bene ‹on MT› [MT]")]
    [InlineData("Bene (on the Bladedance tank)", "Bene [the Bladedance tank]")]
    [InlineData("Short CD to Co Tank", "Short CD ‹to Co Tank› [Co Tank]")]
    [InlineData("Kera or Soil", "Kera|Soil")]
    [InlineData("Party Mit/Short CD", "Party Mit|Short CD")]
    [InlineData("Kera (optional)", "Kera (optional) opt")]
    [InlineData("Kera asap", "Kera ‹asap› ⏱")]
    [InlineData("Kera ⏱\uFE0F", "Kera ⏱")]
    [InlineData("Soil w/ Succor", "Soil ‹w/ Succor›")]
    [InlineData("Consolation ×2", "Consolation ‹×2›")]
    [InlineData("Soil (Debuffs (13s))", "Soil (Debuffs (13s))")]
    [InlineData("Soil (unclosed", "Soil (unclosed)")]
    [InlineData("Kera (?)", "Kera (?)")]
    [InlineData("(1:18)", "@1:18")]
    [InlineData("\"Kera\" on MT", "\"Kera\" ‹on MT› [MT]")]
    [InlineData("Soil (2:20) + Kera (2:22)", "Soil @2:20 + Kera @2:22")]
    [InlineData("Kera (0:28 / 0:37)", "Kera @0:28,0:37")]
    [InlineData("SCH Soil", "SCH Soil job=SCH")]
    [InlineData("3rd/4th Kera", "3rd/4th Kera")]
    [InlineData("Asylum middle", "Asylum ‹middle›")]
    [InlineData("Soil under boss + Kera", "Soil ‹under boss› + Kera")]
    public void Parse_SyntheticCells(string text, string expected) => Assert.Equal(expected, Render(Parser.Parse(text)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" \n ")]
    public void Parse_BlankCells_HaveNoItems(string? text)
    {
        var parsed = Parser.Parse(text, "'X'!A1");
        Assert.True(parsed.IsEmpty);
        Assert.Equal("'X'!A1", parsed.Src);
    }

    [Fact]
    public void Parse_Sequence_StepsAndTimes()
    {
        var parsed = Parser.Parse(Fixtures.Cell("carry-the-clear", "TOP – Scholar", "C51"));

        var item = Assert.Single(parsed.Items);
        Assert.True(item.IsSequence);
        Assert.Equal(2, item.Steps.Count);
        Assert.Equal([0, 1], item.Steps.Select(s => s.Index));
        Assert.Equal(["Protraction", "Recit"], item.Steps[0].Tokens.Select(t => t.Head));
        Assert.Null(item.Steps[0].Tokens[0].Time); // the range is written after Recit…
        Assert.Equal((164.0, 166.0), (item.Steps[0].Time!.Seconds, item.Steps[0].Time!.End!.Value)); // …and applies to the step
        Assert.Equal("2:44–2:46", item.Steps[0].Time!.Text);
        Assert.Equal(170, item.Steps[1].Time!.Seconds);
        Assert.Equal(170, item.Time!.Seconds); // the item's time is its last step's
        Assert.Equal("Protraction + Recit (2:44–2:46)", item.Steps[0].Text);
        Assert.Equal("Protraction + Recit (2:44–2:46) → Adlo (2:50)", item.Text);
    }

    [Fact]
    public void Parse_SequenceWithOneTime_TheItemTakesIt()
    {
        var parsed = Parser.Parse(Fixtures.Cell("carry-the-clear", "TOP – Scholar", "C18"));

        Assert.Equal(5, parsed.Items.Count);
        var deploy = parsed.Items[0];
        Assert.Equal(["Protraction + Recit", "Adlo", "Deploy (1:07)"], deploy.Steps.Select(s => s.Text));
        Assert.Null(deploy.Steps[0].Time);
        Assert.Equal(67, deploy.Time!.Seconds);
        Assert.False(deploy.Time.Approx);
        Assert.Equal("1:07", deploy.Time.Text);
        Assert.Equal([0, 1, 2, 3, 4], parsed.Items.Select(i => i.Index));
        Assert.Equal(["Illumination", "Expedient", "Seraph", "Soil"], parsed.Items.Skip(1).Select(i => i.Tokens.Single().Head));
    }

    [Fact]
    public void Parse_TimeWithHint_KeepsBothAndTheOriginalText()
    {
        var token = Parser.Parse(Fixtures.Cell("carry-the-clear", "TOP – White Mage", "C18")).Items[1].Tokens.Single();

        Assert.Equal("Bell", token.Head);
        Assert.Equal(82, token.Time!.Seconds);
        Assert.True(token.Time.Approx);
        Assert.Equal("~1:22", token.Time.Text);
        Assert.Equal(["right after the 1st stack"], token.Hints);
        Assert.Equal("Bell (right after the 1st stack, ~1:22)", token.Text);
    }

    [Fact]
    public void Parse_TargetsAggregateToTheStepAndItem()
    {
        var item = Parser.Parse(Fixtures.Cell("carry-the-clear", "TOP – White Mage", "C17")).Items[1];

        Assert.Equal(["Solace", "Benison"], item.Tokens.Select(t => t.Head));
        Assert.Null(item.Tokens.First().Target);
        Assert.Equal("your 3rd/4th-in-line player", item.Steps[0].Target!.Value);
        Assert.Equal("your 3rd/4th-in-line player", item.Target!.Text);
        Assert.Null(item.Time);
    }

    [Fact]
    public void Parse_TrailingTarget_KeepsTheTrailingTextToo()
    {
        var token = Parser.Parse("Short CD to R1/2").Tokens.Single();

        Assert.Equal("Short CD", token.Head);
        Assert.Equal("to R1/2", token.Trailing);
        Assert.Equal(new ParsedTarget("R1/2", "to R1/2"), token.Target);
        Assert.Equal(["Short CD"], token.Alternatives); // "R1/2" is not an either/or
        Assert.Equal("Short CD to R1/2", token.WithTrailing(token.Alternatives[0]));
    }

    [Fact]
    public void Parse_EitherOrWithTrailing_SharesTheTrailingText()
    {
        var token = Parser.Parse(Fixtures.Cell("lpdu", "WAR/DRK", "C12")).Items.Single().Tokens.Last();

        Assert.Equal("TBN/Oblation Co-Tank", token.Phrase);
        Assert.Equal(["TBN", "Oblation"], token.Alternatives);
        Assert.Equal(["TBN Co-Tank", "Oblation Co-Tank"], token.Alternatives.Select(token.WithTrailing));
    }

    [Fact]
    public void Parse_OptionalGreyRuns_ArePerItem()
    {
        var parsed = Parser.Parse(Fixtures.Cell("carry-the-clear", "TOP – White Mage", "C43"));

        Assert.Equal([false, true], parsed.Items.Select(i => i.Optional));
        var aquaveil = parsed.Items[1].Tokens.Single();
        Assert.Equal("#808080", aquaveil.Fg);
        Assert.Equal([new TextRun(0, "#808080", false)], aquaveil.Runs);
    }

    [Fact]
    public void Parse_ColouredRunsAreKeptPerToken()
    {
        var parsed = Parser.Parse(Fixtures.Cell("carry-the-clear", "TOP – White Mage", "C18"));

        Assert.Equal([new TextRun(0, null, false)], parsed.Items[0].Tokens.Single().Runs);
        var plenary = parsed.Items[2].Tokens.Single();
        Assert.Equal("Plenary* (1:31)", plenary.Text);
        Assert.Equal(56, plenary.Start);
        Assert.Equal([new TextRun(0, "#1154cc", true)], plenary.Runs);
        Assert.True(plenary.Footnote);
        Assert.True(parsed.Items[2].Footnote);
        Assert.False(parsed.Items[0].Footnote);
    }

    [Fact]
    public void Parse_FootnoteMarkerColour_IsInTheRunsButNotTheHeadColour()
    {
        var rep = Parser.Parse(Fixtures.Cell("lpdu", "P6", "C4")).Tokens.Last();

        Assert.Equal("Rep*", rep.Text);
        Assert.Null(rep.Fg);
        Assert.Equal([new TextRun(0, null, false), new TextRun(3, "#ff0000", false)], rep.Runs);
    }

    [Fact]
    public void Parse_CellColourAndStrike_ApplyWithoutRuns()
    {
        var grey = Parser.Parse(new SheetCell { Text = "Soil + Kera", Fg = "#999999" });
        Assert.All(grey.Tokens, t => Assert.True(t.Optional));

        var struck = Parser.Parse(new SheetCell { Text = "Soil + Kera", Runs = [new TextRun(0, null, false) { Strike = true }, new TextRun(4, null, false)] });
        Assert.Equal([true, false], struck.Tokens.Select(t => t.Struck));

        var dim = Parser.Parse(new SheetCell { Text = "Soil", Fg = "#545454" });
        Assert.False(dim.Tokens.Single().Optional); // a dark grey is not "grey = optional"

        var custom = new CellParser(new CellParserOptions { IsOptionalColor = c => c == "#ff0000" });
        Assert.True(custom.Parse(new SheetCell { Text = "Soil", Fg = "#FF0000" }).Tokens.Single().Optional);
    }

    [Fact]
    public void Parse_TokensPointBackIntoTheCellText()
    {
        foreach (var (_, doc) in Fixtures.Both)
        {
            foreach (var tab in doc.Tabs)
            {
                foreach (var row in tab.Rows)
                {
                    foreach (var cell in row.Where(c => !c.IsEmpty))
                    {
                        var parsed = Parser.Parse(cell);
                        foreach (var item in parsed.Items)
                        {
                            Assert.Equal(item.Text, cell.Text!.Substring(item.Start, item.Text.Length));
                            foreach (var step in item.Steps)
                                Assert.Equal(step.Text, cell.Text!.Substring(step.Start, step.Text.Length));
                            foreach (var t in item.Tokens)
                            {
                                Assert.Equal(t.Text, cell.Text!.Substring(t.Start, t.Text.Length));
                                Assert.Equal(t.Text.Trim(), t.Text);
                                Assert.True(t.Runs.Count > 0 && t.Runs[0].Start == 0);
                                Assert.StartsWith(t.Head, t.Phrase, StringComparison.Ordinal);
                                if (t.Head.Length > 0)
                                    Assert.All(t.Alternatives, a => Assert.Contains(a, t.Head, StringComparison.Ordinal));
                            }
                        }
                    }
                }
            }
        }
    }

    // ---- properties over every cell of both sheets ------------------------------------------------

    private static readonly Regex WordRegex = new(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant);

    private static IEnumerable<string> Words(string? text) => text is null ? [] : WordRegex.Matches(text).Select(m => m.Value);

    /// <summary>Every piece of text the parse kept, read from the result's fields (not from a helper of the parser).</summary>
    private static IEnumerable<string> KeptTexts(ParsedCell parsed)
    {
        foreach (var t in parsed.Tokens)
        {
            yield return t.Phrase; // head + trailing words
            foreach (var h in t.Hints)
                yield return h;
            if (t.Target is not null)
                yield return t.Target.Text;
            if (t.Time is not null)
                yield return t.Time.Text;
        }
    }

    [Fact]
    public void EveryAssigneeCell_ParsesWithoutLosingAWord()
    {
        var cells = Fixtures.Both.SelectMany(d => new LayoutDetector().Detect(d.Doc).PlanTabs)
            .SelectMany(t => t.DataRows)
            .SelectMany(r => r.Assignments)
            .ToList();
        Assert.True(cells.Count > 300, $"only {cells.Count} assignee cells");

        var failures = new List<string>();
        foreach (var a in cells)
        {
            ParsedCell parsed;
            try
            {
                parsed = Parser.Parse(a.Cell, a.Src);
            }
            catch (Exception e)
            {
                failures.Add($"{a.Src}: threw {e.GetType().Name}: {e.Message}");
                continue;
            }

            if (parsed.IsEmpty)
                failures.Add($"{a.Src}: no items for '{a.Cell.Text}'");
            var kept = KeptTexts(parsed).SelectMany(Words).GroupBy(w => w).ToDictionary(g => g.Key, g => g.Count());
            foreach (var g in Words(a.Cell.Text).GroupBy(w => w))
            {
                if (kept.GetValueOrDefault(g.Key) < g.Count())
                    failures.Add($"{a.Src}: lost '{g.Key}' in '{a.Cell.Text}' → {Render(parsed)}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void EveryCellOfEveryTab_ParsesWithoutThrowingOrLosingAWord()
    {
        var failures = new List<string>();
        var count = 0;
        foreach (var (_, doc) in Fixtures.Both)
        {
            foreach (var tab in doc.Tabs)
            {
                for (var r = 0; r < tab.Rows.Count; r++)
                {
                    for (var c = 0; c < tab.Rows[r].Count; c++)
                    {
                        var cell = tab.Cell(r, c);
                        if (cell.IsEmpty)
                            continue;
                        count++;
                        try
                        {
                            var parsed = Parser.Parse(cell, tab.Ref(r, c));
                            var kept = KeptTexts(parsed).SelectMany(Words).GroupBy(w => w).ToDictionary(g => g.Key, g => g.Count());
                            foreach (var g in Words(cell.Text).GroupBy(w => w))
                            {
                                if (kept.GetValueOrDefault(g.Key) < g.Count())
                                    failures.Add($"{tab.Ref(r, c)}: lost '{g.Key}' in '{cell.Text}'");
                            }
                        }
                        catch (Exception e)
                        {
                            failures.Add($"{tab.Ref(r, c)}: threw {e.GetType().Name}: {e.Message}");
                        }
                    }
                }
            }
        }

        Assert.True(count > 1000, $"only {count} cells");
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Theory]
    [InlineData("((((")]
    [InlineData("))))")]
    [InlineData("/ / /")]
    [InlineData("+ + ,")]
    [InlineData("→ → ->")]
    [InlineData("* ⏱ *")]
    [InlineData("-")]
    [InlineData("--->")]
    [InlineData("Kera /Soil")]
    [InlineData("a/")]
    [InlineData("/a")]
    [InlineData("w/")]
    [InlineData("(1:18) / (1:20)")]
    [InlineData("Soil (1:18) / (1:20) / Kera")]
    [InlineData("0:28 / Kera")]
    [InlineData("~ ≈ @ at")]
    [InlineData("[Kera] [0:20]")]
    [InlineData("Kera\r\n\r\nSoil")]
    [InlineData("\t")]
    [InlineData("1:2:3:4:5")]
    public void Parse_OddInput_NeverThrowsAndKeepsEveryWord(string text)
    {
        var parsed = Parser.Parse(text);

        var kept = KeptTexts(parsed).SelectMany(Words).GroupBy(w => w).ToDictionary(g => g.Key, g => g.Count());
        foreach (var g in Words(text).GroupBy(w => w))
            Assert.True(kept.GetValueOrDefault(g.Key) >= g.Count(), $"lost '{g.Key}' → {Render(parsed)}");
    }
}
