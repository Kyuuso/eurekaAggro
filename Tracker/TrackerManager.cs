using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using EurekaSuite.Configuration;
using EurekaSuite.Tracker.Model;
using EurekaSuite.Tracker.Network;
using EurekaSuite.Tracker.Services;
using EurekaSuite.Tracker.Zones;

namespace EurekaSuite.Tracker;

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

    public List<PublicTrackerInfo> AvailablePublicTrackers { get; private set; } = new();
    public bool IsFetchingPublicTrackers { get; private set; }
    public DateTimeOffset? LastPublicTrackersFetch { get; private set; }
    public event Action? OnPublicTrackersUpdated;

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
            AvailablePublicTrackers.Clear();
            OnPublicTrackersUpdated?.Invoke();
        }
        else
        {
            TryAutoJoinOrCreateTracker();
        }

        FateMonitor.Reset();
    }

    private void OnEurekaZoneEntered(uint serverId, ushort territoryId)
    {
        UpdateCurrentZone(territoryId);
        TryAutoJoinOrCreateTracker(serverId);
    }

    /// <summary>
    /// Fetches all active public trackers for the player's current data center.
    /// </summary>
    public async Task RefreshPublicTrackersAsync(CancellationToken cancellationToken = default)
    {
        int? dcId = GetCurrentDataCenterId();
        if (!dcId.HasValue || IsFetchingPublicTrackers) return;

        IsFetchingPublicTrackers = true;
        try
        {
            var trackers = await EurekaTrackerClient.FetchPublicTrackersAsync(dcId.Value, cancellationToken);
            AvailablePublicTrackers = trackers;
            LastPublicTrackersFetch = DateTimeOffset.UtcNow;
            OnPublicTrackersUpdated?.Invoke();
        }
        catch (Exception ex)
        {
            EurekaSuitePlugin.PluginLog.Debug($"Failed to refresh public trackers: {ex.Message}");
        }
        finally
        {
            IsFetchingPublicTrackers = false;
        }
    }

    /// <summary>
    /// Legacy alias for TryAutoJoinOrCreateTracker.
    /// </summary>
    public void TryAutoCreateTracker() => TryAutoJoinOrCreateTracker();

    /// <summary>
    /// Evaluates entering a Eureka zone:
    /// 1. If an active public tracker with the same Server ID is already open on this data center, auto-connects to it.
    /// 2. If no matching tracker exists and AutoCreate is enabled, creates a new one (public or private based on config) and injects Server ID.
    /// 3. Otherwise, refreshes the public tracker directory so the user can connect in 1 click.
    /// </summary>
    public void TryAutoJoinOrCreateTracker(uint? specificServerId = null)
    {
        if (Client.IsConnected || isAutoCreatingTracker || CurrentZoneTracker == null)
            return;

        string detectedId = specificServerId?.ToString() ?? InstanceService.GetBestDetectedInstanceId();
        int zoneId = CurrentZoneTracker.ZoneId;
        string zoneName = CurrentZoneTracker.ZoneName;
        int? dcId = GetCurrentDataCenterId();

        isAutoCreatingTracker = true;
        _ = Task.Run(async () =>
        {
            try
            {
                // 1. Check existing public trackers on this datacenter if enabled
                if (dcId.HasValue && config.TrackerAutoJoinExisting && !string.IsNullOrEmpty(detectedId))
                {
                    var publicTrackers = await EurekaTrackerClient.FetchPublicTrackersAsync(dcId.Value);
                    AvailablePublicTrackers = publicTrackers;
                    LastPublicTrackersFetch = DateTimeOffset.UtcNow;
                    OnPublicTrackersUpdated?.Invoke();

                    var matchingTrackers = publicTrackers
                        .Where(t => t.ZoneId == zoneId &&
                                    string.Equals(t.InstanceId, detectedId, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(t => t.UpdatedAt ?? t.CreatedAt ?? DateTimeOffset.MinValue)
                        .ThenByDescending(t => t.PoppedCount)
                        .ToList();

                    if (matchingTrackers.Count > 0)
                    {
                        var matching = matchingTrackers[0];
                        string savedPwd = config.GetSavedPassword(matching.TrackerId) ?? string.Empty;
                        bool joined = await Client.JoinTrackerAsync(matching.TrackerId, savedPwd);
                        if (joined)
                        {
                            config.RememberTracker(matching.TrackerId, savedPwd, detectedId, zoneId);

                            var sb = new SeStringBuilder()
                                .AddUiForeground(45)
                                .AddText("[Eureka Suite] ")
                                .AddUiForegroundOff()
                                .AddText($"Auto-connected to existing public {zoneName} Tracker: ")
                                .AddUiForeground(58)
                                .AddText($"https://ffxiv-eureka.com/{matching.TrackerId}")
                                .AddUiForegroundOff()
                                .AddText($" (Server ID: {detectedId}, Updated: {matching.GetAgeString()})");

                            if (!string.IsNullOrEmpty(savedPwd))
                            {
                                sb.AddUiForeground(57)
                                  .AddText(" [Admin password restored]")
                                  .AddUiForegroundOff();
                            }

                            if (matchingTrackers.Count > 1)
                            {
                                sb.AddUiForeground(43)
                                  .AddText($" [Note: {matchingTrackers.Count} trackers found for ID {detectedId}; joined the most recent one]")
                                  .AddUiForegroundOff();
                            }

                            chatGui.Print(sb.BuiltString);
                            return;
                        }
                    }
                }

                // 2. If no matching tracker exists, check if auto-create is enabled
                if (config.TrackerAutoCreate)
                {
                    var (newTrackerId, password, _) = await EurekaTrackerClient.CreateTrackerAsync(zoneId);
                    if (!string.IsNullOrEmpty(newTrackerId))
                    {
                        config.RememberTracker(newTrackerId, password, detectedId, zoneId);

                        bool joined = await Client.JoinTrackerAsync(newTrackerId, password);
                        if (joined)
                        {
                            int? dcToPush = (config.TrackerCreatePublic && dcId.HasValue) ? dcId.Value : null;
                            if (!string.IsNullOrEmpty(detectedId) || dcToPush.HasValue)
                            {
                                await Client.SetInstanceInformationAsync(detectedId, dcToPush);
                            }

                            string pubTag = (config.TrackerCreatePublic && dcId.HasValue) ? " [Public]" : " [Private]";
                            chatGui.Print(new SeStringBuilder()
                                .AddUiForeground(45)
                                .AddText("[Eureka Suite] ")
                                .AddUiForegroundOff()
                                .AddText($"Auto-created {zoneName} Tracker{pubTag}: ")
                                .AddUiForeground(58)
                                .AddText($"https://ffxiv-eureka.com/{newTrackerId}")
                                .AddUiForegroundOff()
                                .AddText(string.IsNullOrEmpty(detectedId) ? string.Empty : $" (Instance ID: {detectedId})")
                                .BuiltString);
                        }
                    }
                }
                else if (dcId.HasValue && AvailablePublicTrackers.Count == 0)
                {
                    // Fetch public trackers so they are immediately visible in the UI
                    _ = RefreshPublicTrackersAsync();
                }
            }
            catch (Exception ex)
            {
                EurekaSuitePlugin.PluginLog.Error(ex, "Failed to auto-join or auto-create Eureka tracker.");
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

    /// <summary>
    /// Resolves the player's current data center name string (e.g. "Chaos", "Light").
    /// </summary>
    public string? GetCurrentDataCenterName()
    {
        try
        {
            var world = objectTable.LocalPlayer?.CurrentWorld.Value;
            if (world != null)
            {
                return world.Value.DataCenter.Value.Name.ToString();
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
