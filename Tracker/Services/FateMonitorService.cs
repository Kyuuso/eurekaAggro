using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using EurekaAggro.Configuration;
using EurekaAggro.Tracker.Model;
using EurekaAggro.Tracker.Network;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace EurekaAggro.Tracker.Services;

/// <summary>
/// Monitors in-game FATEs in Eureka to detect spawned Notorious Monsters (NMs),
/// dispatching audio/visual pop notifications and auto-reporting kills to the live tracker.
/// </summary>
public class FateMonitorService
{
    private readonly IFateTable fateTable;
    private readonly IToastGui toastGui;
    private readonly IChatGui chatGui;
    private readonly PluginConfiguration config;
    private readonly EurekaTrackerClient trackerClient;

    private readonly HashSet<ushort> previousFates = new();

    public FateMonitorService(
        IFateTable fateTable,
        IToastGui toastGui,
        IChatGui chatGui,
        PluginConfiguration config,
        EurekaTrackerClient trackerClient)
    {
        this.fateTable = fateTable;
        this.toastGui = toastGui;
        this.chatGui = chatGui;
        this.config = config;
        this.trackerClient = trackerClient;
    }

    public void Update(IEurekaZoneTracker? currentZoneTracker)
    {
        if (currentZoneTracker == null) return;

        var currentFates = fateTable.Select(f => f.FateId).ToHashSet();

        // Detect newly spawned FATEs
        var newlySpawned = currentFates.Except(previousFates).ToList();
        if (newlySpawned.Count > 0)
        {
            var zoneFates = currentZoneTracker.GetFates();
            foreach (var fateId in newlySpawned)
            {
                var match = zoneFates.FirstOrDefault(f => f.FateId == fateId);
                if (match != null && !match.IsBunnyFate)
                {
                    OnFatePopped(match);
                }
            }
        }

        previousFates.Clear();
        foreach (var id in currentFates)
        {
            previousFates.Add(id);
        }
    }

    private unsafe void OnFatePopped(EurekaFate fate)
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
            var seString = new SeStringBuilder()
                .AddUiForeground(45)
                .AddText("[EurekaAggro] ")
                .AddUiForegroundOff()
                .AddText("NM Popped: ")
                .AddUiForeground(58)
                .AddText(fate.BossName)
                .AddUiForegroundOff()
                .AddText(" at ")
                .Add(mapPayload)
                .BuiltString;

            chatGui.Print(seString);
        }

        // 4. Auto Pop to live Eureka Tracker
        if (config.TrackerAutoPopFate && trackerClient.IsConnected && trackerClient.CanModify)
        {
            long nowMs = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            _ = trackerClient.SetPopTimeAsync(fate.TrackerId, nowMs);
        }
    }

    public void Reset()
    {
        previousFates.Clear();
    }
}
