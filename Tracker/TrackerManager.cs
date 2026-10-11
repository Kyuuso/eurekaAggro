using System;
using System.Collections.Generic;
using System.Linq;
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
    private const long PublicTrackersRefreshIntervalMs = 60_000;
    private const long PublicTrackersMaxBackoffMs = 600_000;

    private readonly PluginConfiguration config;
    private readonly IClientState clientState;
    private readonly IObjectTable objectTable;
    private readonly IDataManager dataManager;
    private readonly IChatGui chatGui;
    private readonly IFramework framework;

    private volatile bool isAutoCreatingTracker;

    // Auto-join is deferred to the framework tick so it runs once the local player (and data center) is available.
    private bool autoJoinPending;
    private uint? pendingServerId;

    // Environment.TickCount64 of the last public directory fetch attempt (0 = never attempted).
    private long lastPublicTrackersAttemptTick;
    private int publicTrackersFailureCount;

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

    /// <summary>
    /// True when the public tracker directory has never been requested, or when the last attempt is older
    /// than the refresh interval. Failed attempts back off exponentially (60s, 120s, 240s, up to 10 minutes).
    /// </summary>
    public bool IsPublicTrackersRefreshDue
    {
        get
        {
            if (IsFetchingPublicTrackers) return false;

            long lastAttempt = Interlocked.Read(ref lastPublicTrackersAttemptTick);
            if (lastAttempt == 0) return true;

            long intervalMs = PublicTrackersRefreshIntervalMs;
            int failures = publicTrackersFailureCount;
            if (failures > 1)
            {
                intervalMs = Math.Min(PublicTrackersMaxBackoffMs, PublicTrackersRefreshIntervalMs << Math.Min(failures - 1, 4));
            }

            return Environment.TickCount64 - lastAttempt >= intervalMs;
        }
    }

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
        FateMonitor = new FateMonitorService(fateTable, toastGui, chatGui, clientState, config, Client, InstanceService);

        clientState.TerritoryChanged += OnTerritoryChanged;
        framework.Update += OnFrameworkUpdate;
        InstanceService.OnEurekaZoneEntered += OnEurekaZoneEntered;
        Client.OnRequestRejected += OnTrackerRequestRejected;

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
            autoJoinPending = false;
            pendingServerId = null;
            AvailablePublicTrackers = new();
            OnPublicTrackersUpdated?.Invoke();

            // Let the UI fetch the all-zones list once, unless the directory is currently backing off.
            if (publicTrackersFailureCount == 0)
                Interlocked.Exchange(ref lastPublicTrackersAttemptTick, 0);
        }
        else
        {
            autoJoinPending = true;
        }

        FateMonitor.Reset();
    }

    private void OnEurekaZoneEntered(uint serverId, ushort territoryId)
    {
        // Also covers instance changes within the same territory, which must reset the FATE snapshot.
        UpdateCurrentZone(territoryId);
        pendingServerId = serverId;
    }

    /// <summary>
    /// Fetches all active public trackers for the player's current data center.
    /// Must be called from the framework thread because it reads the local player.
    /// </summary>
    public async Task RefreshPublicTrackersAsync(CancellationToken cancellationToken = default)
    {
        int? dcId = GetCurrentDataCenterId();
        if (!dcId.HasValue || IsFetchingPublicTrackers) return;

        IsFetchingPublicTrackers = true;
        try
        {
            await FetchAndStorePublicTrackersAsync(dcId.Value, cancellationToken);
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
    /// Fetches the public directory, records the attempt for backoff purposes, and stores the result on success.
    /// Returns null if the directory could not be reached.
    /// </summary>
    private async Task<List<PublicTrackerInfo>?> FetchAndStorePublicTrackersAsync(int dcId, CancellationToken cancellationToken = default)
    {
        Interlocked.Exchange(ref lastPublicTrackersAttemptTick, Environment.TickCount64);

        var trackers = await EurekaTrackerClient.FetchPublicTrackersAsync(dcId, cancellationToken);
        if (trackers == null)
        {
            Interlocked.Increment(ref publicTrackersFailureCount);
            return null;
        }

        Interlocked.Exchange(ref publicTrackersFailureCount, 0);
        AvailablePublicTrackers = trackers;
        LastPublicTrackersFetch = DateTimeOffset.UtcNow;
        OnPublicTrackersUpdated?.Invoke();
        return trackers;
    }

    /// <summary>
    /// Saves a tracker to the configuration history on the framework thread, so the UI never observes
    /// the history list while it is being modified.
    /// </summary>
    public Task RememberTrackerAsync(string trackerId, string? password, string? instanceId, int zoneId)
    {
        return framework.RunOnFrameworkThread(() => config.RememberTracker(trackerId, password, instanceId, zoneId));
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
    /// Must be called from the framework thread.
    /// </summary>
    public void TryAutoJoinOrCreateTracker(uint? specificServerId = null)
    {
        if (Client.IsConnected || isAutoCreatingTracker || CurrentZoneTracker == null)
            return;

        string detectedId = specificServerId.HasValue && specificServerId.Value > 0
            ? specificServerId.Value.ToString()
            : InstanceService.GetBestDetectedInstanceId();
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
                    var publicTrackers = await FetchAndStorePublicTrackersAsync(dcId.Value) ?? new List<PublicTrackerInfo>();

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
                            await RememberTrackerAsync(matching.TrackerId, savedPwd, detectedId, zoneId);

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
                        await RememberTrackerAsync(newTrackerId, password, detectedId, zoneId);

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
                else if (dcId.HasValue && AvailablePublicTrackers.Count == 0 && IsPublicTrackersRefreshDue)
                {
                    // Fetch public trackers so they are immediately visible in the UI
                    await FetchAndStorePublicTrackersAsync(dcId.Value);
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

    private void OnTrackerRequestRejected(string requestName, string reason)
    {
        _ = framework.RunOnFrameworkThread(() =>
        {
            chatGui.PrintError(new SeStringBuilder()
                .AddText("[Eureka Suite] ")
                .AddText($"Tracker rejected '{requestName}': {reason}")
                .BuiltString);
        });
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        var zoneTracker = CurrentZoneTracker;
        if (zoneTracker == null || !InstanceTrackerService.IsEurekaTerritory(clientState.TerritoryType))
            return;

        // Wait for the local player so the data center and FATE table are valid after a zone change.
        if (objectTable.LocalPlayer == null)
            return;

        if (autoJoinPending && !isAutoCreatingTracker)
        {
            autoJoinPending = false;
            uint? serverId = pendingServerId;
            pendingServerId = null;
            TryAutoJoinOrCreateTracker(serverId);
        }

        // Only hand the live tracker to the monitor when it belongs to the zone the player is in.
        var liveTracker = Client.ActiveTracker;
        if (liveTracker != null && liveTracker.ZoneId != zoneTracker.ZoneId)
            liveTracker = null;

        FateMonitor.Update(zoneTracker, liveTracker);
    }

    /// <summary>
    /// Resolves the player's current data center numeric ID for ffxiv-eureka.com.
    /// Must be called from the framework thread.
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
    /// Must be called from the framework thread.
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
        Client.OnRequestRejected -= OnTrackerRequestRejected;

        Client.Dispose();
        InstanceService.Dispose();
    }
}
