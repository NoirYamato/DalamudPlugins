using System.Text.RegularExpressions;
using MitCue.Cli.Import;
using MitCue.Core.Import;
using MitCue.Core.Plans;

namespace MitCue.Cli.Commands;

/// <summary><c>import &lt;source&gt; --fight &lt;key&gt; ...</c>: a sheet to plan YAML through <see cref="PlanImporter"/>.</summary>
internal static partial class ImportCommand
{
    public static CommandSpec Spec { get; } = new()
    {
        Name = "import",
        Synopsis = "<source> --fight <key> [--tabs a,b] [--assignees a,b] [--calibration <key>] [--name <text>] [--url <text>] [-o <plan.yaml>]",
        Summary = "Import a plan from a sheet (.xlsx, fixture folder, .csv/.tsv, or a Google Sheets link) into plan YAML.",
        Details = """
            The source is an .xlsx workbook, a folder of JSON fixture tabs (tests/fixtures/sheets/<sheet>), a .csv or .tsv
            export of one tab, a fixture .json tab, or a public Google Sheets link (its xlsx export is downloaded; the sheet
            must be shared as "Anyone with the link"). Tab names are matched ignoring case and punctuation, so "P1/P2"
            finds the exported "P1P2".

            Prints the importer's diagnostics grouped by severity and, for timestamped sheets, each phase's fitted zero
            point (timeline seconds of sheet 0:00) and spread. The plan YAML goes to -o, or to standard output (the report
            then goes to standard error). Exit code 1 when a diagnostic is an error (the plan is still written).
            """,
        Options =
        [
            new("fight", "key", "Fight key (top, tea, dmu). Required."),
            new("tabs", "a,b", "Only these tabs (comma-separated); default: every tab that looks like a plan table."),
            new("assignees", "a,b", "Only these columns (key or header), e.g. \"White Mage\" or \"H2 SCH\"; default: all. A name that matches no column is an error."),
            new("calibration", "key", "Calibration preset of the fight file (e.g. carry_the_clear) used when a phase cannot be fitted."),
            new("name", "text", "Plan name (default: chosen by the importer from the sheet)."),
            new("url", "text", "Source link stored in the plan (default: the link imported, if any)."),
            new("date", "yyyy-mm-dd", "Import date stamped on the plan (default: today)."),
            new("id", "guid", "Plan id to use (default: the importer's); keeps re-imports of a sheet the same plan."),
            new("save", "file.xlsx", "Also save the downloaded workbook (Google Sheets links only)."),
            new("output", "plan.yaml", "Write the plan here (\"-\" = standard output, the default).", "o"),
        ],
        MinArgs = 1,
        MaxArgs = 1,
        Examples =
        [
            "import tests/fixtures/sheets/carry-the-clear --fight top --tabs \"TOP – White Mage\" --calibration carry_the_clear -o ctc-whm.yaml",
            "import tests/fixtures/sheets/lpdu/export/lpdu.xlsx --fight top --assignees \"H2 SCH\" -o lpdu-sch.yaml",
            "import \"https://docs.google.com/spreadsheets/d/<id>/edit#gid=0\" --fight top -o plan.yaml",
        ],
        Run = Run,
    };

    private static int Run(CliContext ctx, ParsedArgs args)
    {
        var fight = ctx.Fight(args.Require("fight"));
        var calibration = args.Get("calibration");
        if (calibration != null && !fight.Calibration.ContainsKey(calibration))
        {
            var keys = fight.Calibration.Keys.Order(StringComparer.Ordinal).ToList();
            throw new UsageException($"import: {fight.Key} has no calibration preset '{calibration}'. Presets: {(keys.Count > 0 ? string.Join(", ", keys) : "none")}.");
        }

        var date = args.Get("date") ?? ctx.Environment.Today();
        if (!IsoDate().IsMatch(date))
            throw new UsageException($"import: --date needs an ISO date like 2026-11-11, not '{date}'.");
        Guid? id = null;
        if (args.Get("id") is { } idText)
        {
            if (!Guid.TryParse(idText, out var parsed))
                throw new UsageException($"import: --id needs a GUID like 6f1c2b9e-0000-4000-8000-000000000001, not '{idText}'.");
            id = parsed;
        }

        var source = args.Positionals[0];
        if (args.Has("save") && !GoogleSheetLink.TryParse(source, out _))
            throw new UsageException("import: --save only applies to Google Sheets links.");

        // Data first: a broken data file is reported before a download.
        var actions = ctx.Actions;
        var aliases = ctx.Aliases;
        var loaded = SheetSource.Load(ctx, source);
        if (args.Get("save") is { } save && loaded.Bytes != null)
        {
            var full = ctx.ResolvePath(save);
            File.WriteAllBytes(full, loaded.Bytes);
            ctx.Err.WriteLine($"Saved the workbook to {full}");
        }

        var document = loaded.Document;
        var tabs = SheetSource.MatchTabs(document, args.GetList("tabs"));
        ImportOptions Options(IReadOnlyList<string>? assignees) => new()
        {
            Fight = fight,
            Actions = actions,
            Aliases = aliases,
            PlanName = args.Get("name"),
            SourceUrl = args.Get("url") ?? loaded.Url,
            CalibrationKey = calibration,
            Tabs = tabs,
            Assignees = assignees,
            ImportedDate = date,
        };

        var importer = new PlanImporter();
        var wanted = args.GetList("assignees");
        var result = importer.Import(document, Options(wanted));
        if (wanted != null)
            CheckAssignees(importer, document, wanted, result.Plan, Options);
        if (id is { } planId)
            result.Plan.Id = planId.ToString("D");

        var output = args.Get("output") ?? "-";
        var report = output == "-" ? ctx.Err : ctx.Out;
        WriteReport(report, loaded, tabs, fight, calibration, result);

        var yaml = PlanSerializer.ToYaml(result.Plan);
        ctx.WriteText(output, yaml);
        if (output != "-")
            report.WriteLine($"Wrote {ctx.ResolvePath(output)}");

        return result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) ? ExitCodes.Problems : ExitCodes.Ok;
    }

    /// <summary>
    /// Every --assignees name must select a column, by its key or its header as the importer matches them (a name that
    /// selects nothing would write an empty plan): a usage error listing the columns otherwise, before anything is written.
    /// </summary>
    private static void CheckAssignees(PlanImporter importer, SheetDocument document, IReadOnlyList<string> wanted, MitPlan plan, Func<IReadOnlyList<string>?, ImportOptions> options)
    {
        // A name equal to an imported column's key matched it; any other is tried alone (it may be a header, "WAR" for
        // the "WAR/DRK WAR" column).
        var unknown = wanted
            .Where(w => !plan.Assignees.Any(a => string.Equals(a.Key, w, StringComparison.OrdinalIgnoreCase)))
            .Where(w => importer.Import(document, options([w])).Plan.Assignees.Count == 0)
            .ToList();
        if (unknown.Count == 0)
            return;

        var columns = importer.Import(document, options(null)).Plan.Assignees.Select(a => "\"" + a.Key + "\"").ToList();
        throw new UsageException(
            $"import: --assignees: {(unknown.Count == 1 ? "no column is" : "no columns are")} named {string.Join(", ", unknown.Select(u => "'" + u + "'"))}. " +
            $"Columns: {(columns.Count > 0 ? string.Join(", ", columns) : "none")}.");
    }

    private static void WriteReport(TextWriter report, LoadedSheet loaded, IReadOnlyList<string>? tabs, Core.Fights.FightDefinition fight, string? calibration, ImportResult result)
    {
        var document = loaded.Document;
        report.WriteLine($"Source: \"{document.Title}\" ({loaded.Kind}), {Text.Count(document.Tabs.Count, "tab")}: {string.Join(", ", document.Tabs.Select(t => "\"" + t.Name + "\""))}");
        if (tabs != null)
            report.WriteLine($"Tabs imported: {string.Join(", ", tabs.Select(t => "\"" + t + "\""))}");
        if (loaded.Url != null && GoogleSheetLink.TryParse(loaded.Url, out var link) && link.Gid is { } gid)
            report.WriteLine($"The link points at tab gid {gid}; the whole workbook was downloaded (pick tabs with --tabs).");
        report.WriteLine($"Fight: {fight.Key} ({fight.Name}, level {fight.SyncLevel})");

        // Diagnostics, most severe first, in the importer's order within a severity.
        var diagnostics = result.Diagnostics;
        report.WriteLine();
        var errors = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        var warnings = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
        var infos = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Info);
        report.WriteLine($"Diagnostics: {Text.Count(errors, "error")}, {Text.Count(warnings, "warning")}, {infos} info");
        foreach (var severity in new[] { DiagnosticSeverity.Error, DiagnosticSeverity.Warning, DiagnosticSeverity.Info })
        {
            var group = diagnostics.Where(d => d.Severity == severity).ToList();
            if (group.Count == 0)
                continue;
            report.WriteLine($"  {severity switch { DiagnosticSeverity.Error => "Errors", DiagnosticSeverity.Warning => "Warnings", _ => "Info" }} ({group.Count}):");
            var table = new Table("src", "code", "message");
            foreach (var d in group)
                table.Add(d.Src ?? "-", d.Code, Text.OneLine(d.Message));
            table.Write(report, "    ", header: false);
        }

        // Calibration: the fitted zero and spread per phase, next to the preset.
        report.WriteLine();
        if (result.PhaseZeros.Count == 0 && result.PhaseSpread.Count == 0)
        {
            report.WriteLine("Calibration: none (no timestamped rows were fitted).");
        }
        else
        {
            var preset = calibration != null && fight.Calibration.TryGetValue(calibration, out var p) ? p : null;
            report.WriteLine("Calibration (timeline seconds of each phase's sheet 0:00; spread = largest residual of the fit):");
            var table = new Table("phase", ">start", ">zero", ">spread", preset != null ? ">preset " + calibration : ">");
            foreach (var phase in fight.Phases)
            {
                var zero = Lookup(result.PhaseZeros, phase.Id);
                var spread = Lookup(result.PhaseSpread, phase.Id);
                var presetZero = preset != null ? Lookup(preset, phase.Id) : null;
                table.Add(
                    phase.Id,
                    Text.Sec(phase.Start),
                    zero is { } z ? Text.Sec(z) : "-",
                    spread is { } s ? Text.Sec(s) + " s" : "-",
                    preset != null ? presetZero is { } pz ? Text.Sec(pz) : "-" : string.Empty);
            }

            table.Write(report, "  ");
        }

        // The plan.
        var plan = result.Plan;
        report.WriteLine();
        report.WriteLine($"Plan \"{plan.Name}\" (fight {plan.Fight}): {Text.Count(plan.Entries.Count, "entry", "entries")}, {Text.Count(plan.Assignees.Count, "column")}");
        foreach (var assignee in plan.Assignees)
        {
            var count = plan.Entries.Count(e => string.Equals(e.Who?.Assignee, assignee.Key, StringComparison.OrdinalIgnoreCase));
            var who = string.Join(" ", new[] { assignee.Slot, assignee.Job?.ToString() }.Where(x => !string.IsNullOrEmpty(x)));
            report.WriteLine($"  \"{assignee.Key}\"{(who.Length > 0 ? " (" + who + ")" : string.Empty)}: {Text.Count(count, "entry", "entries")}");
        }
    }

    private static double? Lookup(IReadOnlyDictionary<string, double> map, string phase)
    {
        if (map.TryGetValue(phase, out var exact))
            return exact;
        foreach (var (key, value) in map)
        {
            if (string.Equals(key, phase, StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return null;
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    private static partial Regex IsoDate();
}
