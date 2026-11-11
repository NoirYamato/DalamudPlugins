using Dalamud.Plugin.Services;
using ClassJobRow = Lumina.Excel.Sheets.ClassJob;
using LuminaAction = Lumina.Excel.Sheets.Action;
using TerritoryRow = Lumina.Excel.Sheets.TerritoryType;

namespace MitCue.Data;

/// <summary>
/// Lumina (game data) helpers: action names in the client language, icons, GCD flag, job abbreviations.
/// Results are cached per id. Safe to call from Framework.Update and Draw (both run on the main thread).
/// </summary>
internal sealed class GameData
{
    /// <summary>Action sheet CooldownGroup value of the global cooldown (index 57 in ActionManager).</summary>
    private const byte GcdCooldownGroup = 58;

    private readonly IDataManager data;
    private readonly object gate = new();
    private readonly Dictionary<uint, ActionRow> actions = [];
    private readonly Dictionary<uint, string> jobs = [];
    private readonly Dictionary<uint, string> territories = [];

    /// <summary>Creates the helper over Dalamud's data manager.</summary>
    public GameData(IDataManager data)
    {
        this.data = data;
    }

    /// <summary>Action name in the client language, or null when the id is unknown.</summary>
    public string? ActionName(uint actionId) => this.GetAction(actionId).Name;

    /// <summary>Icon id of the action (0 when unknown).</summary>
    public uint ActionIcon(uint actionId) => this.GetAction(actionId).Icon;

    /// <summary>True when the action shares the global cooldown (its recast says nothing about the action itself).</summary>
    public bool IsGcd(uint actionId) => this.GetAction(actionId).CooldownGroup == GcdCooldownGroup;

    /// <summary>True when the Action sheet has this row.</summary>
    public bool ActionExists(uint actionId) => this.GetAction(actionId).Exists;

    /// <summary>ClassJob abbreviation in the client language (e.g. "WHM"), or the id when unknown.</summary>
    public string JobAbbreviation(uint classJobId)
    {
        lock (this.gate)
        {
            if (this.jobs.TryGetValue(classJobId, out var cached))
                return cached;
        }

        var text = classJobId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            if (this.data.GetExcelSheet<ClassJobRow>().TryGetRow(classJobId, out var row))
            {
                var abbreviation = row.Abbreviation.ExtractText();
                if (!string.IsNullOrWhiteSpace(abbreviation))
                    text = abbreviation;
            }
        }
        catch (Exception)
        {
            // Sheet unavailable: fall back to the id.
        }

        lock (this.gate)
            this.jobs[classJobId] = text;
        return text;
    }

    /// <summary>Place name of a territory (client language), or its id.</summary>
    public string TerritoryName(uint territoryId)
    {
        lock (this.gate)
        {
            if (this.territories.TryGetValue(territoryId, out var cached))
                return cached;
        }

        var text = territoryId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            if (this.data.GetExcelSheet<TerritoryRow>().TryGetRow(territoryId, out var row))
            {
                var name = row.PlaceName.ValueNullable?.Name.ExtractText();
                if (!string.IsNullOrWhiteSpace(name))
                    text = name;
            }
        }
        catch (Exception)
        {
            // Sheet unavailable: fall back to the id.
        }

        lock (this.gate)
            this.territories[territoryId] = text;
        return text;
    }

    private ActionRow GetAction(uint actionId)
    {
        if (actionId == 0)
            return default;

        lock (this.gate)
        {
            if (this.actions.TryGetValue(actionId, out var cached))
                return cached;
        }

        var info = default(ActionRow);
        try
        {
            if (this.data.GetExcelSheet<LuminaAction>().TryGetRow(actionId, out var row))
            {
                var name = row.Name.ExtractText();
                info = new ActionRow(true, string.IsNullOrWhiteSpace(name) ? null : name, row.Icon, row.CooldownGroup);
            }
        }
        catch (Exception)
        {
            // Sheet unavailable: unknown action.
        }

        lock (this.gate)
            this.actions[actionId] = info;
        return info;
    }

    private readonly record struct ActionRow(bool Exists, string? Name, uint Icon, byte CooldownGroup);
}
