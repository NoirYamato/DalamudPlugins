using System;
using System.Collections.Generic;
using System.Linq;

namespace AnoMech;

// Whether AnoMech is loaded beside NoirMech. Both install the same hooks and share ImGui window ids, so nothing zones in
// while it is: ZoneControl arms NoirMech's guard and firewall only in its own stays, and a zone load of AnoMech's could
// trip the guard of one.
internal sealed class AnoMechPresence
{
    public const string AnoMechInternalName = "AnoMech";

    public const string LoadedRefusal =
        "AnoMech is loaded -- disable it in /xlplugins to run " + Plugin.DisplayName + ": both install the same hooks.";

    // Worded as ZoneControl.StartBlockedReason's other reasons, which callers put in sentences of their own.
    public const string LoadedStartBlock = "AnoMech is loaded (disable it in /xlplugins: both install the same hooks)";

    public bool Loaded { get; private set; }

    // Null while AnoMech isn't loaded.
    public string? Refusal => Loaded ? LoadedRefusal : null;
    public string? StartBlock => Loaded ? LoadedStartBlock : null;

    // True when this finds AnoMech newly loaded.
    public bool Update(IEnumerable<InstalledPlugin>? plugins)
    {
        var loaded = plugins?.Any(p => p.IsLoaded && string.Equals(p.InternalName, AnoMechInternalName, StringComparison.OrdinalIgnoreCase)) ?? false;
        var detected = loaded && !Loaded;
        Loaded = loaded;
        return detected;
    }
}

// One entry of Dalamud's installed plugins, as far as AnoMechPresence reads it.
internal readonly record struct InstalledPlugin(string InternalName, bool IsLoaded);
