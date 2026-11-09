using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AnoMech.Core;
using AnoMech.Scenarios;

namespace AnoMech;

// The main window's setup across game restarts, in its own file in the plugin's config folder rather than in upstream's
// Configuration: the Role pick, and per scenario its region, its strat by name (AiStrats indices move between builds) and
// its settings overrides as ScenarioSettingsSync carries them to peers, which leaves out the internal test pins.
internal sealed class SetupMemory
{
    public const string FileName = "setup.json";
    public const string AutoRole = "Auto";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    // One of the main window's Role labels.
    public string Role { get; set; } = AutoRole;

    // By IScenario.FullName.
    public Dictionary<string, ScenarioSetup> Scenarios { get; set; } = [];

    // The defaults when the file is missing or unreadable.
    public static SetupMemory Load(string? path)
    {
        if (path is null || !File.Exists(path)) return new SetupMemory();
        try
        {
            var loaded = JsonSerializer.Deserialize<SetupMemory>(File.ReadAllText(path), Options) ?? new SetupMemory();
            loaded.Role ??= AutoRole;
            loaded.Scenarios ??= [];
            return loaded;
        }
        catch (Exception e)
        {
            DiagnosticLog.Warn($"[SetupMemory] Could not read {FileName}, starting from the defaults: {e.Message}");
            return new SetupMemory();
        }
    }

    // Through a temporary file, so a crash mid-write leaves the last good one.
    public void Save(string? path)
    {
        if (path is null) return;
        try
        {
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, Options));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception e)
        {
            DiagnosticLog.Warn($"[SetupMemory] Could not save {FileName}: {e.Message}");
        }
    }

    public ScenarioSetup? Find(IScenario scenario) => Scenarios.GetValueOrDefault(scenario.FullName());

    // True when this changed what is kept. Reading the scenario's settings costs a serialization, so a caller that
    // remembers every frame passes `checkSettings` now and then.
    public bool Remember(string role, IScenario? scenario, string? region, string? strat, bool checkSettings)
    {
        var changed = Role != role;
        Role = role;
        if (scenario is null) return changed;
        var key = scenario.FullName();
        var kept = Scenarios.GetValueOrDefault(key);
        var settings = checkSettings ? ScenarioSettingsSync.Serialize(scenario.SettingsOverrides) : kept?.Settings;
        if (kept is not null && kept.Region == region && kept.Strat == strat && kept.Settings == settings) return changed;
        Scenarios[key] = new ScenarioSetup { Region = region, Strat = strat, Settings = settings };
        return true;
    }
}

internal sealed class ScenarioSetup
{
    // The strat picker's region; null for a scenario whose strats have none.
    public string? Region { get; set; }
    public string? Strat { get; set; }
    public string? Settings { get; set; }

    // The strat named here in `region`, or null when the scenario has none by that name there.
    public int? StratIndex(IScenario scenario, string? region)
    {
        var strats = scenario.AiStrats;
        for (var i = 0; i < strats.Count; i++)
            if (strats[i].Name == Strat && strats[i].Group == region) return i;
        return null;
    }

    public void RestoreSettings(IScenario scenario)
    {
        if (Settings is not { } json || scenario.SettingsOverrides is not { } overrides) return;
        DiagnosticLog.Info($"[SetupMemory] Putting back {scenario.Name}'s settings from {SetupMemory.FileName}.");
        ScenarioSettingsSync.Apply(overrides, json);
    }
}
