using System;
using System.Collections.Generic;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using EurekaSuite.Configuration;
using EurekaSuite.Data;
using EurekaSuite.Tracker.Model;
using EurekaSuite.Tracker.Network;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace EurekaSuite.Tracker.Services;

/// <summary>
/// Monitors in-game FATEs in Eureka to detect spawned Notorious Monsters (NMs),
/// dispatching audio/visual pop notifications and auto-reporting kills to the live tracker.
/// </summary>
public class FateMonitorService
{
    // After a zone change the FATE table fills in over several frames, so every FATE seen during
    // this window is treated as already active instead of freshly spawned.
    private const long SeedGracePeriodMs = 3000;

    private readonly IFateTable fateTable;
    private readonly IToastGui toastGui;
    private readonly IChatGui chatGui;
    private readonly IClientState clientState;
    private readonly PluginConfiguration config;
    private readonly EurekaTrackerClient trackerClient;
    private readonly InstanceTrackerService instanceService;

    private HashSet<ushort> previousFates = new();
    private HashSet<ushort> currentFates = new();

    // Environment.TickCount64 of the first update after a reset (0 = not started yet).
    private long seedStartTick;

    public FateMonitorService(
        IFateTable fateTable,
        IToastGui toastGui,
        IChatGui chatGui,
        IClientState clientState,
        PluginConfiguration config,
        EurekaTrackerClient trackerClient,
        InstanceTrackerService instanceService)
    {
        this.fateTable = fateTable;
        this.toastGui = toastGui;
        this.chatGui = chatGui;
        this.clientState = clientState;
        this.config = config;
        this.trackerClient = trackerClient;
        this.instanceService = instanceService;
    }

    /// <summary>
    /// Diffs the FATE table against the previous tick. Must be called from the framework thread
    /// once the local player is loaded.
    /// </summary>
    /// <param name="zoneTracker">Static NM data for the zone the player is in.</param>
    /// <param name="liveTracker">Live tracker state, or null if not connected to a tracker for this zone.</param>
    public void Update(IEurekaZoneTracker zoneTracker, IEurekaZoneTracker? liveTracker)
    {
        currentFates.Clear();
        for (int i = 0; i < fateTable.Length; i++)
        {
            var fate = fateTable[i];
            if (fate == null) continue;
            currentFates.Add(fate.FateId);
        }

        if (seedStartTick == 0)
        {
            seedStartTick = Environment.TickCount64;
        }

        bool seeding = Environment.TickCount64 - seedStartTick < SeedGracePeriodMs;
        if (!seeding)
        {
            List<EurekaFate>? zoneFates = null;
            foreach (var fateId in currentFates)
            {
                if (previousFates.Contains(fateId)) continue;

                zoneFates ??= zoneTracker.GetFates();
                var match = FindFate(zoneFates, fateId);
                if (match != null && !match.IsBunnyFate)
                {
                    OnFatePopped(match, zoneTracker, liveTracker);
                }
            }
        }

        (previousFates, currentFates) = (currentFates, previousFates);
    }

    private static EurekaFate? FindFate(List<EurekaFate>? fates, ushort fateId)
    {
        if (fates == null) return null;

        for (int i = 0; i < fates.Count; i++)
        {
            if (fates[i].FateId == fateId) return fates[i];
        }

        return null;
    }

    private unsafe void OnFatePopped(EurekaFate fate, IEurekaZoneTracker zoneTracker, IEurekaZoneTracker? liveTracker)
    {
        // 1. Toast Notification
        if (config.TrackerDisplayToastPop)
        {
            toastGui.ShowQuest($"NM Pop: {fate.BossName}", new Dalamud.Game.Gui.Toast.QuestToastOptions
            {
                PlaySound = false,
                DisplayCheckmark = true,
            });
        }

        // 2. Sound Effect
        if (config.TrackerPlayPopSound)
        {
            try
            {
                UIGlobals.PlaySoundEffect(0x29); // Standard notification jingle
            }
            catch { }
        }

        // 3. Chat Notification
        if (config.TrackerDisplayFatePop)
        {
            var mapPayload = new MapLinkPayload(fate.TerritoryId, fate.MapId, fate.FatePosition.X, fate.FatePosition.Y);
            string placeName = !string.IsNullOrEmpty(mapPayload.PlaceName) ? mapPayload.PlaceName : ValidZones.GetZoneName(fate.TerritoryId);
            string coordStr = !string.IsNullOrEmpty(mapPayload.CoordinateString) ? mapPayload.CoordinateString : $"( {fate.FatePosition.X:0.0} , {fate.FatePosition.Y:0.0} )";

            var seString = new SeStringBuilder()
                .AddUiForeground(45)
                .AddText("[Eureka Suite] ")
                .AddUiForegroundOff()
                .AddText("NM Popped: ")
                .AddUiForeground(58)
                .AddText(fate.BossName)
                .AddUiForegroundOff()
                .AddText(" at ")
                .Add(mapPayload)
                .AddUiForeground(518)
                .AddText($"\uE0BB {placeName} {coordStr}")
                .AddUiForegroundOff()
                .Add(RawPayload.LinkTerminator)
                .BuiltString;

            chatGui.Print(seString);
        }

        // 4. Auto Pop to live Eureka Tracker
        if (config.TrackerAutoPopFate && CanReportPop(fate, zoneTracker, liveTracker))
        {
            long nowMs = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            _ = trackerClient.SetPopTimeAsync(fate.TrackerId, nowMs);
        }
    }

    /// <summary>
    /// Only reports pops while the player stands in the tracker's zone and instance,
    /// and never overwrites a pop time the tracker already holds.
    /// </summary>
    private bool CanReportPop(EurekaFate fate, IEurekaZoneTracker zoneTracker, IEurekaZoneTracker? liveTracker)
    {
        if (liveTracker == null || !trackerClient.IsConnected || !trackerClient.CanModify)
            return false;

        if (clientState.TerritoryType != zoneTracker.TerritoryId || fate.TerritoryId != zoneTracker.TerritoryId)
            return false;

        string trackerInstance = trackerClient.InstanceId;
        if (!string.IsNullOrEmpty(trackerInstance))
        {
            string detectedInstance = instanceService.GetBestDetectedInstanceId();
            if (!string.IsNullOrEmpty(detectedInstance) &&
                !string.Equals(trackerInstance, detectedInstance, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        var liveFate = FindFate(liveTracker.GetFates(), fate.FateId);
        return liveFate != null && !liveFate.IsPopped();
    }

    /// <summary>
    /// Clears the FATE snapshot. The next updates re-seed it silently for the grace period.
    /// </summary>
    public void Reset()
    {
        previousFates.Clear();
        currentFates.Clear();
        seedStartTick = 0;
    }
}
