using System;
using System.Collections.Generic;
using System.Linq;
using AnoMech.Core;
using AnoMech.Core.Game;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Multiplayer;
using Dalamud.Plugin;

namespace AnoMech;

// Dalamud's installed plugins, read for AnoMech at load and on every change. A zone load of AnoMech's could trip the guard
// of a NoirMech stay already up, which stops the game, so such a stay ends as AnoMech is found.
internal sealed class AnoMechWatch(Game game, MultiplayerManager multiplayer, RunController runs) : IDisposable
{
    public static AnoMechWatch Start(Game game, MultiplayerManager multiplayer, RunController runs)
    {
        var watch = new AnoMechWatch(game, multiplayer, runs);
        Plugin.PluginInterface.ActivePluginsChanged += watch.OnActivePluginsChanged;
        watch.CheckInstalled();
        return watch;
    }

    public void Dispose() => Plugin.PluginInterface.ActivePluginsChanged -= OnActivePluginsChanged;

    // Dalamud may raise this off the framework thread.
    private void OnActivePluginsChanged(IActivePluginsChangedEventArgs args) => Plugin.Framework.Run(CheckInstalled);

    private void CheckInstalled()
    {
        List<InstalledPlugin> installed;
        try
        {
            installed = Plugin.PluginInterface.InstalledPlugins.Select(p => new InstalledPlugin(p.InternalName, p.IsLoaded)).ToList();
        }
        catch (Exception e)
        {
            DiagnosticLog.Warn($"[AnoMechWatch] Could not list the installed plugins: {e.Message}");
            return;
        }
        Check(installed);
    }

    // One chat line each time AnoMech is found newly loaded.
    internal void Check(IEnumerable<InstalledPlugin> installed)
    {
        if (!Plugin.AnoMechPresence.Update(installed)) return;
        var inStay = game.Zone is not null;
        Game.Print(inStay ? $"{AnoMechPresence.LoadedRefusal} Leaving the sim zone." : AnoMechPresence.LoadedRefusal);
        if (inStay) LeaveStay();
        Natives.Zone.HoldSendFirewall(false);
    }

    // A peer leaves its session as Disconnect does, so its own stay comes down at once and the host ends the run it lost
    // a player from. Anyone else leaves as Leave does: a host's party goes back to the inn with it, since its sim is theirs.
    private void LeaveStay()
    {
        if (game.Mode != SessionMode.Peer)
        {
            runs.Leave();
            return;
        }
        multiplayer.LeaveSession();
        if (game.World.Map.IsInInstance) game.Leave();
    }
}
