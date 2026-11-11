using System.Globalization;

namespace MitCue.Core;

/// <summary>Combat jobs. Numeric values equal the game's ClassJob row ids.</summary>
public enum Job : byte
{
    None = 0,
    PLD = 19,
    MNK = 20,
    WAR = 21,
    DRG = 22,
    BRD = 23,
    WHM = 24,
    BLM = 25,
    SMN = 27,
    SCH = 28,
    NIN = 30,
    MCH = 31,
    DRK = 32,
    AST = 33,
    SAM = 34,
    RDM = 35,
    BLU = 36,
    GNB = 37,
    DNC = 38,
    RPR = 39,
    SGE = 40,
    VPR = 41,
    PCT = 42,
    BST = 43,
}

public enum Role
{
    None,
    Tank,
    Healer,
    Melee,
    PhysicalRanged,
    Caster,
}

public static class Jobs
{
    private static readonly Dictionary<Job, string> FullNames = new()
    {
        [Job.PLD] = "Paladin", [Job.MNK] = "Monk", [Job.WAR] = "Warrior", [Job.DRG] = "Dragoon",
        [Job.BRD] = "Bard", [Job.WHM] = "White Mage", [Job.BLM] = "Black Mage", [Job.SMN] = "Summoner",
        [Job.SCH] = "Scholar", [Job.NIN] = "Ninja", [Job.MCH] = "Machinist", [Job.DRK] = "Dark Knight",
        [Job.AST] = "Astrologian", [Job.SAM] = "Samurai", [Job.RDM] = "Red Mage", [Job.BLU] = "Blue Mage",
        [Job.GNB] = "Gunbreaker", [Job.DNC] = "Dancer", [Job.RPR] = "Reaper", [Job.SGE] = "Sage",
        [Job.VPR] = "Viper", [Job.PCT] = "Pictomancer", [Job.BST] = "Beastmaster",
    };

    private static readonly Dictionary<string, Job> ByName = BuildNameIndex();

    /// <summary>All combat jobs (excluding <see cref="Job.None"/>).</summary>
    public static IReadOnlyList<Job> All { get; } = FullNames.Keys.ToArray();

    public static string FullName(Job job) => FullNames.TryGetValue(job, out var n) ? n : job.ToString();

    public static Role RoleOf(Job job) => job switch
    {
        Job.PLD or Job.WAR or Job.DRK or Job.GNB => Role.Tank,
        Job.WHM or Job.SCH or Job.AST or Job.SGE => Role.Healer,
        Job.MNK or Job.DRG or Job.NIN or Job.SAM or Job.RPR or Job.VPR or Job.BST => Role.Melee,
        Job.BRD or Job.MCH or Job.DNC => Role.PhysicalRanged,
        Job.BLM or Job.SMN or Job.RDM or Job.PCT or Job.BLU => Role.Caster,
        _ => Role.None,
    };

    /// <summary>
    /// Parses a job from an abbreviation ("WHM", "sge"), an English job name ("White Mage",
    /// "whitemage") or a numeric ClassJob id. Case- and whitespace-insensitive.
    /// </summary>
    public static bool TryParse(string? text, out Job job)
    {
        job = Job.None;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var key = Normalize(text);
        if (ByName.TryGetValue(key, out job))
            return true;
        if (byte.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && Enum.IsDefined(typeof(Job), id) && id != 0)
        {
            job = (Job)id;
            return true;
        }
        job = Job.None;
        return false;
    }

    private static string Normalize(string s) =>
        new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static Dictionary<string, Job> BuildNameIndex()
    {
        var d = new Dictionary<string, Job>();
        foreach (var (job, name) in FullNames)
        {
            d[Normalize(job.ToString())] = job;
            d[Normalize(name)] = job;
        }
        return d;
    }
}

/// <summary>Standard party slot names and their common synonyms.</summary>
public static class Slots
{
    public static readonly IReadOnlyList<string> Standard = ["MT", "OT", "H1", "H2", "M1", "M2", "R1", "R2"];

    private static readonly Dictionary<string, string> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MT"] = "MT", ["T1"] = "MT", ["OT"] = "OT", ["ST"] = "OT", ["T2"] = "OT",
        ["H1"] = "H1", ["H2"] = "H2",
        ["M1"] = "M1", ["D1"] = "M1", ["M2"] = "M2", ["D2"] = "M2",
        ["R1"] = "R1", ["D3"] = "R1", ["R2"] = "R2", ["D4"] = "R2",
    };

    /// <summary>Maps "MT"/"T1", "OT"/"ST"/"T2", "H1", "H2", "M1"/"D1", "M2"/"D2", "R1"/"D3", "R2"/"D4" to the standard name.</summary>
    public static bool TryNormalize(string? text, out string slot)
    {
        slot = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        if (Synonyms.TryGetValue(text.Trim(), out var s))
        {
            slot = s;
            return true;
        }
        return false;
    }

    public static Role RoleOf(string slot) => slot switch
    {
        "MT" or "OT" => Role.Tank,
        "H1" or "H2" => Role.Healer,
        "M1" or "M2" => Role.Melee,
        "R1" or "R2" => Role.Caster, // ranged slots hold physical ranged or casters
        _ => Role.None,
    };

    /// <summary>
    /// True when <paramref name="slot"/> (a standard slot or a synonym) is a slot of <paramref name="job"/>'s role: MT/OT
    /// for tanks, H1/H2 for healers, M1/M2 for melee, R1/R2 for physical ranged and casters.
    /// </summary>
    public static bool Fits(string? slot, Job job)
    {
        if (!TryNormalize(slot, out var s))
            return false;
        var role = Jobs.RoleOf(job);
        return RoleOf(s) == role || (RoleOf(s) == Role.Caster && role == Role.PhysicalRanged);
    }
}
