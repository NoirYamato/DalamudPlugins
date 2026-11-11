using MitCue.Core;
using MitCue.Core.Events;
using MitCue.Game;

namespace MitCue.Runtime;

/// <summary>Game state sampled once per framework tick and handed to the runtime (reused every frame).</summary>
internal sealed class FrameContext
{
    /// <summary>Monotonic clock of this frame.</summary>
    public double Now { get; set; }

    /// <summary>A character is logged in.</summary>
    public bool LoggedIn { get; set; }

    /// <summary>Current territory id.</summary>
    public uint Territory { get; set; }

    /// <summary>Current ClassJob row id.</summary>
    public uint ClassJobId { get; set; }

    /// <summary>Current job (None for classes/crafters).</summary>
    public Job Job { get; set; }

    /// <summary>Effective (synced) level.</summary>
    public int Level { get; set; }

    /// <summary>Local player's entity id (0 when unknown).</summary>
    public uint LocalEntityId { get; set; }

    /// <summary>InCombat condition.</summary>
    public bool InCombat { get; set; }

    /// <summary>Bound by duty (any duty).</summary>
    public bool BoundByDuty { get; set; }

    /// <summary>Remaining /countdown seconds, or null.</summary>
    public float? Countdown { get; set; }

    /// <summary>Client language is English (timeline source names can be matched).</summary>
    public bool ClientIsEnglish { get; set; } = true;

    /// <summary>Client language name.</summary>
    public string ClientLanguage { get; set; } = "English";

    /// <summary>This frame's game events, in time order.</summary>
    public IReadOnlyList<GameEvent> Events { get; set; } = [];

    /// <summary>This frame's de-duplicated own action uses (all detection paths).</summary>
    public List<OwnUse> OwnUses { get; } = new(8);

    /// <summary>This frame's action uses by other players.</summary>
    public IReadOnlyList<OwnUse> PartyUses { get; set; } = [];

    /// <summary>
    /// The jobs in the player's party as a <see cref="PartyJobs"/> mask (0 when not in a party, or when nothing needs
    /// it: it is only read while the co-healer's calls are shown).
    /// </summary>
    public ulong PartyJobMask { get; set; }
}
