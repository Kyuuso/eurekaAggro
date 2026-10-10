using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using EurekaAggro.Configuration;
using EurekaAggro.Tracker.Model;
using EurekaAggro.Tracker.Network;
using EurekaAggro.Tracker.Services;
using EurekaAggro.Tracker.Zones;

namespace EurekaAggro.Tracker;

/// <summary>
/// Central manager orchestrating the Eureka Tracker subsystem:
/// network client, instance/server ID hooking, FATE pop monitoring, and zone transitions.
/// </summary>
public class TrackerManager : IDisposable
{
    private readonly PluginConfiguration config;
    private readonly IClientState clientState;
    private readonly IObjectTable objectTable;
    private readonly IDataManager dataManager;
    private readonly IChatGui chatGui;
    private readonly IFramework framework;

    private bool isAutoCreatingTracker;

    public EurekaTrackerClient Client { get; }
    public InstanceTrackerService InstanceService { get; }
    public FateMonitorService FateMonitor { get; }

    public IEurekaZoneTracker? CurrentZoneTracker { get; private set; }

    public static readonly Dictionary<string, int> DatacenterToEurekaId = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Elemental", 1 },
        { "Gaia", 2 },
        { "Mana", 3 },
        { "Aether", 4 },
        { "Primal", 5 },
        { "Chaos", 6 },
        { "Crystal", 7 },
        { "Light", 8 },
        { "Materia", 9 },
        { "Dynamis", 10 },
        { "Meteor", 11 },
    };

    public TrackerManager(
        PluginConfiguration config,
        IGameInteropProvider interopProvider,
        IClientState clientState,
        IObjectTable objectTable,
        IDataManager dataManager,
        IChatGui chatGui,
        IToastGui toastGui,
        IFateTable fateTable,
        IFramework framework)
    {
        this.config = config;
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.dataManager = dataManager;
        this.chatGui = chatGui;
        this.framework = framework;

        Client = new EurekaTrackerClient();
        InstanceService = new InstanceTrackerService(interopProvider, clientState, chatGui, config);
        FateMonitor = new FateMonitorService(fateTable, toastGui, chatGui, config, Client);

        clientState.TerritoryChanged += OnTerritoryChanged;
        framework.Update += OnFrameworkUpdate;
        InstanceService.OnEurekaZoneEntered += OnEurekaZoneEntered;

        UpdateCurrentZone(clientState.TerritoryType);
    }

    private void OnTerritoryChanged(uint territoryId)
    {
        UpdateCurrentZone(territoryId);
    }

    private void UpdateCurrentZone(uint territoryId)
    {
        CurrentZoneTracker = territoryId switch
        {
            732 => new AnemosTracker(),
            763 => new PagosTracker(),
            795 => new PyrosTracker(),
            827 => new HydatosTracker(),
            _ => null,
        };

        if (CurrentZoneTracker == null)
        {
            InstanceService.ResetServerId();
        }
        else
        {
            TryAutoCreateTracker();
        }

        FateMonitor.Reset();
    }

    private void OnEurekaZoneEntered(uint serverId, ushort territoryId)
    {
        UpdateCurrentZone(territoryId);
        TryAutoCreateTracker();
    }

    /// <summary>
    /// Attempts to auto-create a new tracker on ffxiv-eureka.com if enabled,
    /// directly associating the detected instance/server ID and datacenter.
    /// </summary>
    public void TryAutoCreateTracker()
    {
        if (!config.TrackerAutoCreate || Client.IsConnected || isAutoCreatingTracker || CurrentZoneTracker == null)
            return;

        isAutoCreatingTracker = true;
        int zoneId = CurrentZoneTracker.ZoneId;
        string zoneName = CurrentZoneTracker.ZoneName;

        _ = Task.Run(async () =>
        {
            try
            {
                var (newTrackerId, password, _) = await EurekaTrackerClient.CreateTrackerAsync(zoneId);
                if (!string.IsNullOrEmpty(newTrackerId))
                {
                    config.TrackerLastCode = newTrackerId;
                    config.TrackerLastPassword = password;
                    config.Save();

                    bool joined = await Client.JoinTrackerAsync(newTrackerId, password);
                    if (joined)
                    {
                        // Add detected Instance ID directly to the newly created tracker
                        string detectedId = InstanceService.GetBestDetectedInstanceId();
                        int? dcId = GetCurrentDataCenterId();
                        if (!string.IsNullOrEmpty(detectedId))
                        {
                            await Client.SetInstanceInformationAsync(detectedId, dcId);
                        }

                        chatGui.Print(new SeStringBuilder()
                            .AddUiForeground(45)
                            .AddText("[EurekaAggro] ")
                            .AddUiForegroundOff()
                            .AddText($"Auto-created {zoneName} Tracker: ")
                            .AddUiForeground(58)
                            .AddText($"https://ffxiv-eureka.com/{newTrackerId}")
                            .AddUiForegroundOff()
                            .AddText(string.IsNullOrEmpty(detectedId) ? string.Empty : $" (Instance ID: {detectedId})")
                            .BuiltString);
                    }
                }
            }
            catch (Exception ex)
            {
                EurekaAggroPlugin.PluginLog.Error(ex, "Failed to auto-create and join Eureka tracker.");
            }
            finally
            {
                isAutoCreatingTracker = false;
            }
        });
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (CurrentZoneTracker != null)
        {
            FateMonitor.Update(Client.ActiveTracker ?? CurrentZoneTracker);
        }
    }

    /// <summary>
    /// Resolves the player's current data center numeric ID for ffxiv-eureka.com.
    /// </summary>
    public int? GetCurrentDataCenterId()
    {
        try
        {
            var world = objectTable.LocalPlayer?.CurrentWorld.Value;
            if (world != null)
            {
                var dcName = world.Value.DataCenter.Value.Name.ToString();
                if (!string.IsNullOrEmpty(dcName) && DatacenterToEurekaId.TryGetValue(dcName, out int id))
                {
                    return id;
                }
            }
        }
        catch { }

        return null;
    }

    public void Dispose()
    {
        clientState.TerritoryChanged -= OnTerritoryChanged;
        framework.Update -= OnFrameworkUpdate;
        InstanceService.OnEurekaZoneEntered -= OnEurekaZoneEntered;

        Client.Dispose();
        InstanceService.Dispose();
    }
}
