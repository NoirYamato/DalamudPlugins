namespace MitCue.Cli.Tests;

/// <summary>Help, dispatch, argument errors and exit codes.</summary>
public class CommandLineTests : IDisposable
{
    private readonly CliKit kit = new();

    public static TheoryData<string> CommandNames() => [.. Cli.Commands.Select(c => c.Name)];

    public void Dispose() => this.kit.Dispose();

    [Fact]
    public void GlobalHelp_ListsEveryCommand()
    {
        var r = this.kit.Run("--help");
        Assert.Equal(0, r.ExitCode);
        foreach (var c in Cli.Commands)
            Assert.Contains("  " + c.Name + " ", r.Out);
        Assert.Contains("Exit codes", r.Out);
        Assert.Equal(r.Out, this.kit.Run("help").Out);
    }

    [Fact]
    public void NoCommand_PrintsHelpAndIsAUsageError()
    {
        var r = this.kit.Run();
        Assert.Equal(2, r.ExitCode);
        Assert.Contains("Usage: mitcue <command>", r.Out);
    }

    [Theory]
    [MemberData(nameof(CommandNames))]
    public void EveryCommand_HasHelp(string name)
    {
        var command = Cli.Commands.Single(c => c.Name == name);
        foreach (var args in new[] { new[] { name, "--help" }, [name, "-h"], ["help", name], ["--help", name] })
        {
            var r = this.kit.Run(args);
            Assert.True(r.ExitCode == 0, r.ToString());
            Assert.StartsWith($"Usage: mitcue {name}", r.Out);
            Assert.Contains(command.Summary, r.Out);
            foreach (var option in command.Options)
                Assert.Contains("--" + option.Name, r.Out);
            Assert.Contains("--data <dir>", r.Out);
        }
    }

    [Fact]
    public void UnknownCommand_IsAUsageError()
    {
        var r = this.kit.Run("frobnicate");
        Assert.Equal(2, r.ExitCode);
        Assert.Contains("unknown command 'frobnicate'", r.Err);
        Assert.Contains("fights, mechanics", r.Err);
    }

    [Theory]
    [InlineData(new[] { "fights", "--bogus" }, "unknown option '--bogus'")]
    [InlineData(new[] { "fights", "extra" }, "unexpected argument 'extra'")]
    [InlineData(new[] { "mechanics" }, "missing argument")]
    [InlineData(new[] { "mechanics", "top", "--phase" }, "--phase needs a value")]
    [InlineData(new[] { "mechanics", "top", "--phase", "p1", "--phase", "p2" }, "--phase is given more than once")]
    [InlineData(new[] { "mechanics", "nope" }, "unknown fight 'nope'")]
    [InlineData(new[] { "mechanics", "top", "--phase", "p9" }, "has no phase 'p9'")]
    [InlineData(new[] { "mechanics", "top", "--kind", "boom" }, "unknown kind 'boom'")]
    [InlineData(new[] { "timeline", "neither-a-fight-nor-a-file" }, "is neither a fight")]
    [InlineData(new[] { "import", "x.xlsx" }, "--fight is required")]
    [InlineData(new[] { "import", "https://example.com/sheet", "--fight", "top" }, "is not a Google Sheets link")]
    [InlineData(new[] { "import", "x.xlsx", "--fight", "top", "--calibration", "nope" }, "no calibration preset 'nope'")]
    [InlineData(new[] { "import", "x.xlsx", "--fight", "top", "--date", "11/11/2026" }, "ISO date")]
    [InlineData(new[] { "validate", "--data" }, "--data needs a value")]
    public void BadArguments_AreUsageErrors(string[] args, string message)
    {
        var r = this.kit.Run(args);
        Assert.True(r.ExitCode == 2, r.ToString());
        Assert.Contains(message, r.Err);
        Assert.StartsWith("error: ", r.Err);
    }

    [Fact]
    public void MissingFiles_AreFailures()
    {
        var r = this.kit.Run("plan", "missing.yaml", "--job", "WHM");
        Assert.Equal(3, r.ExitCode);
        Assert.Contains("plan not found", r.Err);

        var data = this.kit.Run("--data", "no-such-dir", "fights");
        Assert.Equal(3, data.ExitCode);
        Assert.Contains("no such directory", data.Err);
    }

    [Fact]
    public void UnreadablePlan_IsAFailure()
    {
        var path = this.kit.Write("bad.yaml", "format: mitcue-plan/9\nname: x\n");
        var r = this.kit.Run("plan", path, "--job", "WHM");
        Assert.Equal(3, r.ExitCode);
        Assert.Contains("is not a readable plan", r.Err);
        Assert.Contains("mitcue-plan/9", r.Err);
    }

    [Fact]
    public void DataOption_PointsAtAnotherDataFolder()
    {
        var r = this.kit.Run("fights", "--data", Path.Combine(CliKit.RepoRoot, "data"));
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("top", r.Out);
        Assert.Equal(r.Out, this.kit.Run("--data=" + Path.Combine(CliKit.RepoRoot, "data"), "fights").Out);
    }
}
