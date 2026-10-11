using System;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using EurekaSuite.Configuration;
using FFXIVClientStructs.FFXIV.Client.Game.Network;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Network;

namespace EurekaSuite.Tracker.Services;

/// <summary>
/// Intercepts zone initialization network packets to automatically resolve the zone's Server ID,
/// with real-time memory fallback from UIState PublicInstance.
/// </summary>
public unsafe class InstanceTrackerService : IDisposable
{
    private readonly IGameInteropProvider interopProvider;
    private readonly IClientState clientState;
    private readonly IChatGui chatGui;
    private readonly PluginConfiguration config;

    private Hook<PacketDispatcher.Delegates.HandleZoneInitPacket>? zoneInitHook;

    public uint? CurrentServerId { get; private set; }
    public ushort? CurrentTerritoryId { get; private set; }
    public ushort? CurrentInstanceNumber { get; private set; }

    public event Action<uint, ushort>? OnEurekaZoneEntered;

    public InstanceTrackerService(
        IGameInteropProvider interopProvider,
        IClientState clientState,
        IChatGui chatGui,
        PluginConfiguration config)
    {
        this.interopProvider = interopProvider;
        this.clientState = clientState;
        this.chatGui = chatGui;
        this.config = config;

        try
        {
            zoneInitHook = interopProvider.HookFromAddress<PacketDispatcher.Delegates.HandleZoneInitPacket>(
                (nint)PacketDispatcher.MemberFunctionPointers.HandleZoneInitPacket,
                ZoneInitDetour);
            zoneInitHook.Enable();
            EurekaSuitePlugin.PluginLog.Info("ZoneInitPacket hook enabled successfully.");
        }
        catch (Exception ex)
        {
            EurekaSuitePlugin.PluginLog.Error(ex, "Failed to hook HandleZoneInitPacket. Using memory fallbacks.");
        }
    }

    private void ZoneInitDetour(uint entityId, ZoneInitPacket* packet, byte a3)
    {
        try
        {
            zoneInitHook?.Original(entityId, packet, a3);

            if (packet != null)
            {
                CurrentTerritoryId = packet->TerritoryTypeId;
                CurrentInstanceNumber = packet->Instance;

                // Check if this is an expedition zone (Anemos 732, Pagos 763, Pyros 795, Hydatos 827)
                if (IsEurekaTerritory(packet->TerritoryTypeId))
                {
                    CurrentServerId = packet->ServerId;
                    EurekaSuitePlugin.PluginLog.Info($"Entered Eureka zone {packet->TerritoryTypeId} with Server ID {packet->ServerId}");

                    if (config.TrackerDisplayServerIdInChat)
                    {
                        string zoneName = GetZoneName(packet->TerritoryTypeId);
                        chatGui.Print(new SeStringBuilder()
                            .AddUiForeground(45)
                            .AddText("[Eureka Suite] ")
                            .AddUiForegroundOff()
                            .AddText($"{zoneName} Instance detected - ")
                            .AddUiForeground(58)
                            .AddText($"Server ID: {packet->ServerId}")
                            .AddUiForegroundOff()
                            .BuiltString);
                    }

                    OnEurekaZoneEntered?.Invoke(packet->ServerId, packet->TerritoryTypeId);
                }
                else
                {
                    // Player is outside Eureka - do not track overworld server IDs as Eureka instance IDs
                    CurrentServerId = null;
                }
            }
        }
        catch (Exception ex)
        {
            EurekaSuitePlugin.PluginLog.Error(ex, "Error processing ZoneInitDetour.");
        }
    }

    /// <summary>
    /// Resets any active detected server ID when transitioning outside Eureka.
    /// </summary>
    public void ResetServerId()
    {
        CurrentServerId = null;
    }

    /// <summary>
    /// Reads the public instance ID from UIState if already inside a Eureka zone.
    /// </summary>
    public static uint GetLivePublicInstanceId()
    {
        try
        {
            var uiState = UIState.Instance();
            if (uiState != null && IsEurekaTerritory((ushort)uiState->PublicInstance.TerritoryTypeId))
            {
                return uiState->PublicInstance.InstanceId;
            }
        }
        catch { }

        return 0;
    }

    /// <summary>
    /// Returns the best available instance identifier string (Server ID from packet, or PublicInstanceId).
    /// Returns string.Empty if the player is currently outside Eureka.
    /// </summary>
    public string GetBestDetectedInstanceId()
    {
        // Must be in Eureka to provide an instance ID
        if (!IsEurekaTerritory(clientState.TerritoryType))
        {
            return string.Empty;
        }

        if (CurrentServerId.HasValue && CurrentServerId.Value > 0)
        {
            return CurrentServerId.Value.ToString();
        }

        uint live = GetLivePublicInstanceId();
        if (live > 0)
        {
            return live.ToString();
        }

        return string.Empty;
    }

    public static bool IsEurekaTerritory(uint territoryId) =>
        territoryId is 732 or 763 or 795 or 827;

    private static string GetZoneName(uint territoryId) => territoryId switch
    {
        732 => "Eureka Anemos",
        763 => "Eureka Pagos",
        795 => "Eureka Pyros",
        827 => "Eureka Hydatos",
        _ => "Eureka",
    };

    public void Dispose()
    {
        try
        {
            zoneInitHook?.Dispose();
        }
        catch (Exception ex)
        {
            EurekaSuitePlugin.PluginLog.Error(ex, "Failed to dispose ZoneInitHook.");
        }
    }
}
