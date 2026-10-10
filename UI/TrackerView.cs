using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Plugin.Services;
using EurekaSuite.Configuration;
using EurekaSuite.Data;
using EurekaSuite.Tracker;
using EurekaSuite.Tracker.Model;
using EurekaSuite.Tracker.Network;
using EurekaSuite.Localization;

namespace EurekaSuite.UI;

/// <summary>
/// Renders the complete Eureka Tracker GUI inspired by EurekaHelper,
/// featuring full Phoenix WebSocket live sync, auto/manual Instance ID detection and updates,
/// NM respawn timers, and weather forecasting.
/// </summary>
public class TrackerView : IDisposable
{
    private readonly TrackerManager trackerManager;
    private readonly PluginConfiguration config;
    private readonly IClientState clientState;
    private readonly IGameGui gameGui;

    // Local input buffers
    private string inputTrackerCode = string.Empty;
    private string inputTrackerPassword = string.Empty;
    private string inputInstanceId = string.Empty;

    // Pop time editor state
    private string timeAgoHours = "0";
    private string timeAgoMinutes = "0";
    private ushort editingFateTrackerId;
    private bool isEditingPopTime;

    // Visual palette
    private static readonly Vector4 RedButtonColor = new(0.89f, 0.40f, 0.40f, 1f);
    private static readonly Vector4 BlueButtonColor = new(0.26f, 0.44f, 0.74f, 1f);
    private static readonly Vector4 GreenColorText = new(0.33f, 0.85f, 0.67f, 1f);
    private static readonly Vector4 RedColorText = new(0.92f, 0.45f, 0.45f, 1f);
    private static readonly Vector4 OrangeColorText = new(0.95f, 0.60f, 0.20f, 1f);
    private static readonly Vector4 PurpleColorText = new(0.85f, 0.45f, 0.95f, 1f);
    private static readonly Vector4 GoldAccent = new(0.961f, 0.729f, 0.259f, 1f);

    public TrackerView(
        TrackerManager trackerManager,
        PluginConfiguration config,
        IClientState clientState,
        IGameGui gameGui)
    {
        this.trackerManager = trackerManager;
        this.config = config;
        this.clientState = clientState;
        this.gameGui = gameGui;

        inputTrackerCode = config.TrackerLastCode ?? string.Empty;
        inputTrackerPassword = config.TrackerLastPassword ?? string.Empty;

        trackerManager.Client.OnTrackerUpdated += OnTrackerUpdated;
        trackerManager.Client.OnConnectionStatusChanged += OnTrackerUpdated;
    }

    private void OnTrackerUpdated()
    {
        var client = trackerManager.Client;
        if (!string.IsNullOrEmpty(client.TrackerId)) inputTrackerCode = client.TrackerId;
        if (!string.IsNullOrEmpty(client.TrackerPassword)) inputTrackerPassword = client.TrackerPassword;
        if (!string.IsNullOrEmpty(client.InstanceId)) inputInstanceId = client.InstanceId;
    }

    public void Dispose()
    {
        trackerManager.Client.OnTrackerUpdated -= OnTrackerUpdated;
        trackerManager.Client.OnConnectionStatusChanged -= OnTrackerUpdated;
    }

    public void Draw(MainWindow.SubView subView, float scale)
    {
        if (subView == MainWindow.SubView.Configuration)
        {
            DrawConfiguration(scale);
            return;
        }

        DrawMain(scale);
    }

    private void DrawMain(float scale)
    {
        var client = trackerManager.Client;

        // 1. TOP HEADER & CONNECTION CONTROLS
        DrawConnectionHeader(client, scale);

        ImGui.Separator();
        ImGui.Spacing();

        // 2. INSTANCE ID & SERVER ID BAR
        DrawInstanceIdBar(client, scale);

        ImGui.Spacing();

        // 3. NM TABLE OR PLACEHOLDER
        if (client.IsConnected && client.ActiveTracker != null)
        {
            DrawNmTable(client, scale);
        }
        else
        {
            DrawDisconnectedPlaceholder(client, scale);
        }
    }

    private void DrawConnectionHeader(EurekaTrackerClient client, float scale)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(GoldAccent, Loc.T("Tracker:"));
        ImGui.SameLine();

        if (!client.IsConnected)
        {
            // Create Tracker [+] Button
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
            {
                ImGui.OpenPopup("##CreateTrackerPopup");
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Loc.T("Create a new Eureka Tracker on ffxiv-eureka.com"));
            }

            ImGui.SameLine();

            // Code Input
            ImGui.SetNextItemWidth(110 * scale);
            if (ImGui.InputTextWithHint("##TrackerCode", Loc.T("6-char Code"), ref inputTrackerCode, 6))
            {
                if (inputTrackerCode.Length == 6 && string.IsNullOrEmpty(inputTrackerPassword))
                {
                    string? saved = config.GetSavedPassword(inputTrackerCode);
                    if (!string.IsNullOrEmpty(saved))
                    {
                        inputTrackerPassword = saved;
                    }
                }
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Loc.T("Enter the 6-character code of an existing tracker"));
            }

            ImGui.SameLine();

            // Password Input
            ImGui.SetNextItemWidth(140 * scale);
            ImGui.InputTextWithHint("##TrackerPassword", Loc.T("Password (optional)"), ref inputTrackerPassword, 50);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Loc.T("Optional password. Required to modify pop timers if the tracker is protected."));
            }

            bool hasSavedPwd = !string.IsNullOrEmpty(config.GetSavedPassword(inputTrackerCode));
            if (hasSavedPwd)
            {
                ImGui.SameLine();
                if (ImGuiComponents.IconButton("##SavedPwdBtn", FontAwesomeIcon.Key))
                {
                    string pwd = config.GetSavedPassword(inputTrackerCode) ?? string.Empty;
                    ImGui.SetClipboardText(pwd);
                }
                if (ImGui.IsItemHovered())
                {
                    string pwd = config.GetSavedPassword(inputTrackerCode) ?? string.Empty;
                    ImGui.SetTooltip($"Saved password: '{pwd}' (Click to copy)");
                }
            }

            if (!string.IsNullOrWhiteSpace(inputTrackerPassword))
            {
                ImGui.SameLine();
                if (ImGuiComponents.IconButton("##ShareInputBtn", FontAwesomeIcon.ShareAlt))
                {
                    string shareMsg = !string.IsNullOrWhiteSpace(inputTrackerCode)
                        ? $"https://ffxiv-eureka.com/{inputTrackerCode} | Password: {inputTrackerPassword}"
                        : inputTrackerPassword;
                    ImGui.SetClipboardText(shareMsg);
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"Copy share message (URL + Password: '{inputTrackerPassword}') to clipboard");
                }
            }

            ImGui.SameLine();

            // Connect Button
            if (ImGui.Button(Loc.T("Connect")))
            {
                if (!string.IsNullOrWhiteSpace(inputTrackerCode))
                {
                    string detectedId = trackerManager.InstanceService.GetBestDetectedInstanceId();
                    int zoneId = trackerManager.CurrentZoneTracker?.ZoneId ?? 0;
                    config.RememberTracker(inputTrackerCode, inputTrackerPassword, detectedId, zoneId);

                    _ = Task.Run(async () =>
                    {
                        await client.JoinTrackerAsync(inputTrackerCode, inputTrackerPassword);
                    });
                }
            }
        }
        else
        {
            // Connected controls
            ImGui.TextColored(ImGuiColors.HealerGreen, $"● ID: {client.TrackerId}");
            ImGui.SameLine();
            ImGui.TextDisabled($"| Viewers: {client.Viewers}");
            ImGui.SameLine();

            // Copy Link
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Link))
            {
                ImGui.SetClipboardText($"https://ffxiv-eureka.com/{client.TrackerId}");
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Copy tracker URL to clipboard (https://ffxiv-eureka.com/...)");
            }

            ImGui.SameLine();

            // Share (URL + Password)
            if (ImGuiComponents.IconButton(FontAwesomeIcon.ShareAlt))
            {
                string shareMsg = client.CanModify
                    ? $"https://ffxiv-eureka.com/{client.TrackerId} | Password: {client.TrackerPassword}"
                    : $"https://ffxiv-eureka.com/{client.TrackerId}";
                ImGui.SetClipboardText(shareMsg);
            }
            if (ImGui.IsItemHovered())
            {
                string pwdText = client.CanModify ? $" (includes Password: {client.TrackerPassword})" : "";
                ImGui.SetTooltip($"Copy share message to clipboard{pwdText}\n'https://ffxiv-eureka.com/{client.TrackerId} | Password: ...'");
            }

            // Copy Password & Display
            if (client.CanModify)
            {
                ImGui.SameLine();
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Key))
                {
                    ImGui.SetClipboardText(client.TrackerPassword);
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"Click to copy password '{client.TrackerPassword}' to clipboard");
                }

                ImGui.SameLine();
                ImGui.TextColored(GoldAccent, $"| Password: {client.TrackerPassword}");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"Admin password: '{client.TrackerPassword}'. Share this so others can update NM pop times.");
                }

                ImGui.SameLine();
                // Toggle Public / Private
                if (client.IsPublic)
                {
                    if (ImGuiComponents.IconButton(FontAwesomeIcon.Lock))
                    {
                        _ = client.SetInstanceInformationAsync(client.InstanceId, null);
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Tracker is PUBLIC on ffxiv-eureka.com. Click to set to PRIVATE.");
                    }
                }
                else
                {
                    if (ImGuiComponents.IconButton(FontAwesomeIcon.LockOpen))
                    {
                        int? dcId = trackerManager.GetCurrentDataCenterId();
                        _ = client.SetInstanceInformationAsync(client.InstanceId, dcId);
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Tracker is PRIVATE. Click to set to PUBLIC on your Data Center.");
                    }
                }
            }

            ImGui.SameLine();

            // Open in browser
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Globe))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = $"https://ffxiv-eureka.com/{client.TrackerId}",
                        UseShellExecute = true,
                    });
                }
                catch { }
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Open tracker in external web browser");
            }

            ImGui.SameLine();

            // Export / Clone Tracker
            if (ImGuiComponents.IconButton(FontAwesomeIcon.FileExport))
            {
                _ = Task.Run(async () =>
                {
                    var (newId, pwd, _) = await EurekaTrackerClient.ExportTrackerAsync(client.TrackerId);
                    if (!string.IsNullOrEmpty(newId))
                    {
                        await client.JoinTrackerAsync(newId, pwd);
                    }
                });
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Clone and export this tracker into a fresh instance");
            }

            ImGui.SameLine();

            // Disconnect
            if (ImGuiComponents.IconButton(FontAwesomeIcon.SignOutAlt))
            {
                _ = client.DisconnectAsync();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Disconnect from this tracker");
            }

            // Weather & Eorzea Time forecast button
            ImGui.SameLine();
            DrawWeatherForecastButton(client);
        }

        // Popup: Create Tracker
        if (ImGui.BeginPopup("##CreateTrackerPopup"))
        {
            ImGui.TextColored(GoldAccent, "Select Eureka Zone:");
            ImGui.Separator();

            if (trackerManager.CurrentZoneTracker != null)
            {
                var curZone = trackerManager.CurrentZoneTracker;
                if (ImGui.Selectable($"Auto-detect: {curZone.ZoneName}"))
                {
                    CreateAndJoinTracker(curZone.ZoneId);
                    ImGui.CloseCurrentPopup();
                }
                ImGui.Separator();
            }

            if (ImGui.Selectable("Anemos (Lv. 1 - 20)"))
            {
                CreateAndJoinTracker(1);
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.Selectable("Pagos (Lv. 20 - 35)"))
            {
                CreateAndJoinTracker(2);
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.Selectable("Pyros (Lv. 35 - 50)"))
            {
                CreateAndJoinTracker(3);
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.Selectable("Hydatos (Lv. 50 - 60)"))
            {
                CreateAndJoinTracker(4);
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void CreateAndJoinTracker(int zoneId)
    {
        _ = Task.Run(async () =>
        {
            var (newTrackerId, password, err) = await EurekaTrackerClient.CreateTrackerAsync(zoneId);
            if (!string.IsNullOrEmpty(newTrackerId))
            {
                string detectedId = trackerManager.InstanceService.GetBestDetectedInstanceId();
                config.RememberTracker(newTrackerId, password, detectedId, zoneId);

                inputTrackerCode = newTrackerId;
                inputTrackerPassword = password;

                bool joined = await trackerManager.Client.JoinTrackerAsync(newTrackerId, password);
                if (joined)
                {
                    // Auto-sync detected Server ID if present
                    inputInstanceId = detectedId;
                    int? dcId = config.TrackerCreatePublic ? trackerManager.GetCurrentDataCenterId() : null;
                    if (!string.IsNullOrEmpty(detectedId) || dcId.HasValue)
                    {
                        await trackerManager.Client.SetInstanceInformationAsync(detectedId, dcId);
                    }
                }
            }
        });
    }

    /// <summary>
    /// Renders the Server ID / Instance ID management controls.
    /// Addresses the exact user requirement to view, edit, and push instance IDs to ffxiv-eureka.com.
    /// </summary>
    private void DrawInstanceIdBar(EurekaTrackerClient client, float scale)
    {
        string detectedId = trackerManager.InstanceService.GetBestDetectedInstanceId();

        // Auto-fill buffer if empty and detected
        if (string.IsNullOrEmpty(inputInstanceId))
        {
            if (!string.IsNullOrEmpty(client.InstanceId))
            {
                inputInstanceId = client.InstanceId;
            }
            else if (!string.IsNullOrEmpty(detectedId))
            {
                inputInstanceId = detectedId;
            }
        }

        ImGui.BeginGroup();
        {
            ImGui.AlignTextToFramePadding();
            ImGui.Text(Loc.T("Instance ID:"));
            ImGui.SameLine();

            ImGui.SetNextItemWidth(90 * scale);
            ImGui.InputTextWithHint("##InstanceIdInput", "e.g. 60", ref inputInstanceId, 16);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Server ID / Instance identifier. Used to identify specific Eureka instances for instance hopping.");
            }

            ImGui.SameLine();

            // [ Update on Tracker ] Button
            bool canPush = client.IsConnected && client.CanModify;
            if (!canPush) ImGui.BeginDisabled();

            if (ImGui.Button("Update on Tracker"))
            {
                int? dcId = trackerManager.GetCurrentDataCenterId();
                _ = client.SetInstanceInformationAsync(inputInstanceId, dcId);
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip("Sends this Instance ID to ffxiv-eureka.com via set_instance_information so other players see it.");
            }

            if (!canPush) ImGui.EndDisabled();

            ImGui.SameLine();

            // [ Use Detected ] Button
            if (!string.IsNullOrEmpty(detectedId) && detectedId != inputInstanceId)
            {
                if (ImGui.SmallButton($"Use Detected ({detectedId})"))
                {
                    inputInstanceId = detectedId;
                }
                ImGui.SameLine();
            }

            // Status label
            bool inEureka = ValidZones.IsEureka(clientState.TerritoryType);
            if (inEureka && !string.IsNullOrEmpty(detectedId))
            {
                ImGui.TextColored(ImGuiColors.ParsedGreen, $"[Detected from memory: {detectedId}]");
            }
            else if (inEureka)
            {
                ImGui.TextDisabled("[No Server ID detected yet - enter a Eureka zone]");
            }
            else
            {
                ImGui.TextDisabled("[Outside Eureka - Standby]");
            }

            if (client.IsConnected && !string.IsNullOrEmpty(client.InstanceId))
            {
                ImGui.SameLine();
                ImGui.TextColored(GoldAccent, $"| Active on Tracker: {client.InstanceId}");

                // Check if multiple public trackers exist for this same instance ID
                var altTrackers = trackerManager.AvailablePublicTrackers
                    .Where(t => string.Equals(t.InstanceId, client.InstanceId, StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(t.TrackerId, client.TrackerId, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(t => t.UpdatedAt ?? t.CreatedAt ?? DateTimeOffset.MinValue)
                    .ToList();

                if (altTrackers.Count > 0)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(OrangeColorText, $"({altTrackers.Count} alt online)");
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Switch...##SwitchAltTrackerBtn"))
                    {
                        ImGui.OpenPopup("##SwitchAltTrackerPopup");
                    }

                    if (ImGui.BeginPopup("##SwitchAltTrackerPopup"))
                    {
                        ImGui.TextColored(GoldAccent, $"Alternative Trackers for Instance {client.InstanceId}:");
                        ImGui.Separator();
                        foreach (var alt in altTrackers)
                        {
                            ImGui.Text($"{alt.TrackerId}  |  {alt.GetAgeString()}  |  {alt.PoppedCount} NMs");
                            ImGui.SameLine();
                            if (ImGui.Button($"Connect##Alt_{alt.TrackerId}"))
                            {
                                string altPwd = config.GetSavedPassword(alt.TrackerId) ?? string.Empty;
                                inputTrackerCode = alt.TrackerId;
                                inputTrackerPassword = altPwd;
                                config.RememberTracker(alt.TrackerId, altPwd, alt.InstanceId, alt.ZoneId);
                                _ = client.JoinTrackerAsync(alt.TrackerId, altPwd);
                                ImGui.CloseCurrentPopup();
                            }
                        }
                        ImGui.EndPopup();
                    }
                }
            }
        }
        ImGui.EndGroup();
    }

    private void DrawWeatherForecastButton(EurekaTrackerClient client)
    {
        if (client.ActiveTracker == null) return;

        ImGuiComponents.IconButton(FontAwesomeIcon.CloudSun);
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();

            var tracker = client.ActiveTracker;
            var currentWeather = tracker.GetCurrentWeatherInfo();
            var etNow = EorzeaTime.Now;

            ImGui.TextColored(GoldAccent, $"Eorzea Time: {etNow.EorzeaDateTime:HH:mm} ET");
            bool isNight = etNow.EorzeaDateTime.Hour < 6 || etNow.EorzeaDateTime.Hour >= 19;
            if (isNight)
            {
                ImGui.TextColored(ImGuiColors.DalamudViolet, $"(Night) - Daylight in {etNow.TimeUntilDay():mm'm 'ss's'}");
            }
            else
            {
                ImGui.TextColored(ImGuiColors.ParsedGold, $"(Day) - Night in {etNow.TimeUntilNight():mm'm 'ss's'}");
            }

            ImGui.Separator();

            ImGui.Text($"Current Weather: ");
            ImGui.SameLine();
            ImGui.TextColored(GreenColorText, currentWeather.Weather.ToFriendlyString());
            ImGui.Text($"Weather changes in: {currentWeather.Timeleft:mm'm 'ss's'}");

            ImGui.Separator();
            ImGui.TextColored(GoldAccent, "Upcoming Weather Forecast:");
            var forecasts = tracker.GetAllNextWeatherTime();
            foreach (var (weather, time) in forecasts)
            {
                ImGui.TextColored(PurpleColorText, weather.ToFriendlyString());
                ImGui.SameLine();
                ImGui.TextDisabled($"in: {(time.Hours > 0 ? time.ToString(@"hh\h\ mm\m\ ss\s") : time.ToString(@"mm\m\ ss\s"))}");
            }

            ImGui.EndTooltip();
        }
    }

    private void DrawNmTable(EurekaTrackerClient client, float scale)
    {
        var tracker = client.ActiveTracker;
        if (tracker == null) return;

        var fates = tracker.GetFates();
        int columnCount = config.TrackerShowLevelInTable ? 6 : 5;

        var flags = ImGuiTableFlags.Resizable
            | ImGuiTableFlags.BordersInnerH
            | ImGuiTableFlags.BordersV
            | ImGuiTableFlags.ScrollY
            | ImGuiTableFlags.Sortable
            | ImGuiTableFlags.RowBg;

        if (ImGui.BeginTable("##EurekaNmTrackerTable", columnCount, flags, new Vector2(0, 0)))
        {
            if (config.TrackerShowLevelInTable)
            {
                ImGui.TableSetupColumn(Loc.T("Level"), ImGuiTableColumnFlags.WidthFixed, 36 * scale);
            }
            ImGui.TableSetupColumn(Loc.T("Name"), ImGuiTableColumnFlags.WidthStretch, 140 * scale);
            ImGui.TableSetupColumn(Loc.T("Trigger"), ImGuiTableColumnFlags.WidthStretch, 130 * scale);
            ImGui.TableSetupColumn(Loc.T("Pop Time"), ImGuiTableColumnFlags.WidthFixed, 90 * scale);
            ImGui.TableSetupColumn(Loc.T("Respawn"), ImGuiTableColumnFlags.WidthFixed, 100 * scale);
            ImGui.TableSetupColumn(Loc.T("Status"), ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 80 * scale);

            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();

            // Handle Sorting
            var sortSpecs = ImGui.TableGetSortSpecs();
            var sortedFates = fates.AsEnumerable();

            if (sortSpecs.SpecsDirty && sortSpecs.SpecsCount > 0)
            {
                var spec = sortSpecs.Specs;
                int colIdx = spec.ColumnIndex;
                if (!config.TrackerShowLevelInTable) colIdx++;

                bool asc = spec.SortDirection == ImGuiSortDirection.Ascending;
                sortedFates = colIdx switch
                {
                    0 => asc ? sortedFates.OrderBy(f => f.FateLevel) : sortedFates.OrderByDescending(f => f.FateLevel),
                    1 => asc ? sortedFates.OrderBy(f => f.BossName) : sortedFates.OrderByDescending(f => f.BossName),
                    2 => asc ? sortedFates.OrderBy(f => f.SpawnedBy) : sortedFates.OrderByDescending(f => f.SpawnedBy),
                    3 => asc ? sortedFates.OrderBy(f => f.KilledAt) : sortedFates.OrderByDescending(f => f.KilledAt),
                    4 => asc ? sortedFates.OrderBy(f => f.IsPopped()).ThenBy(f => f.GetRespawnTimeleft()) : sortedFates.OrderByDescending(f => f.IsPopped()).ThenByDescending(f => f.GetRespawnTimeleft()),
                    _ => sortedFates,
                };
            }

            foreach (var fate in sortedFates)
            {
                ImGui.TableNextRow();

                // Column: Level
                if (config.TrackerShowLevelInTable)
                {
                    ImGui.TableNextColumn();
                    ImGui.TextDisabled($"{fate.FateLevel}");
                }

                // Column: NM Boss
                ImGui.TableNextColumn();
                ImGui.Text(fate.BossName);
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextColored(GoldAccent, $"FATE: {fate.FateName} (Lv. {fate.FateLevel})");
                    ImGui.Text($"Boss Element: {fate.BossElement.ToFriendlyString()}");
                    if (fate.SpawnRequiredWeather != EurekaWeather.None)
                    {
                        ImGui.TextColored(PurpleColorText, $"Required Weather: {fate.SpawnRequiredWeather.ToFriendlyString()}");
                    }
                    ImGui.TextDisabled("Click to place Map Flag marker.");
                    ImGui.EndTooltip();
                }
                if (ImGui.IsItemClicked())
                {
                    SetMapFlag(fate.TerritoryId, fate.MapId, fate.FatePosition);
                }

                // Column: Spawn Mob
                ImGui.TableNextColumn();
                ImGui.Text(fate.SpawnedBy);
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text($"Mob Element: {fate.SpawnByElement.ToFriendlyString()}");
                    if (fate.SpawnByRequiredNight)
                    {
                        ImGui.TextColored(ImGuiColors.DalamudViolet, "Requires Night");
                    }
                    if (fate.SpawnByRequiredWeather != EurekaWeather.None)
                    {
                        ImGui.TextColored(PurpleColorText, $"Requires Weather: {fate.SpawnByRequiredWeather.ToFriendlyString()}");
                    }
                    ImGui.TextDisabled("Click to place Map Flag marker at spawn location.");
                    ImGui.EndTooltip();
                }
                if (ImGui.IsItemClicked())
                {
                    SetMapFlag(fate.TerritoryId, fate.MapId, fate.SpawnByPosition);
                }

                // Column: Popped At
                ImGui.TableNextColumn();
                if (fate.IsPopped())
                {
                    string popStr = fate.GetPoppedTime().ToString("HH:mm");
                    ImGui.TextColored(RedColorText, popStr);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"Popped at {fate.GetPoppedTime():g} local time.\nClick to edit elapsed time.");
                    }
                    if (client.CanModify && ImGui.IsItemClicked())
                    {
                        editingFateTrackerId = fate.TrackerId;
                        var diff = DateTime.Now - fate.GetPoppedTime();
                        timeAgoHours = Math.Max(0, diff.Hours).ToString();
                        timeAgoMinutes = Math.Max(0, diff.Minutes).ToString();
                        isEditingPopTime = true;
                        ImGui.OpenPopup($"##EditPopTime_{fate.TrackerId}");
                    }
                }
                else
                {
                    ImGui.TextDisabled("--:--");
                }

                // Pop Time Edit Modal
                if (ImGui.BeginPopup($"##EditPopTime_{fate.TrackerId}"))
                {
                    ImGui.TextColored(GoldAccent, $"Edit Pop Time: {fate.BossName}");
                    ImGui.Separator();

                    ImGui.SetNextItemWidth(45 * scale);
                    ImGui.InputText("h##EditH", ref timeAgoHours, 2, ImGuiInputTextFlags.CharsDecimal);
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(45 * scale);
                    ImGui.InputText("m##EditM", ref timeAgoMinutes, 2, ImGuiInputTextFlags.CharsDecimal);
                    ImGui.SameLine();
                    ImGui.Text("ago");

                    if (ImGui.Button(Loc.T("Apply")))
                    {
                        if (int.TryParse(timeAgoHours, out int h) && int.TryParse(timeAgoMinutes, out int m))
                        {
                            var editedTime = DateTime.Now - new TimeSpan(h, m, 0);
                            long ms = new DateTimeOffset(editedTime).ToUnixTimeMilliseconds();
                            _ = client.SetPopTimeAsync(fate.TrackerId, ms);
                        }
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button(Loc.T("Cancel")))
                    {
                        ImGui.CloseCurrentPopup();
                    }

                    ImGui.EndPopup();
                }

                // Column: Respawn In
                ImGui.TableNextColumn();
                var reqs = fate.GetRespawnRequirements(tracker);
                if (reqs.Count == 0)
                {
                    ImGui.TextColored(GreenColorText, "Ready");
                }
                else
                {
                    var first = reqs[0];
                    if (first.Action == "Respawn")
                    {
                        string timeFmt = first.Time.Hours > 0 ? first.Time.ToString(@"hh\h\ mm\m") : first.Time.ToString(@"mm\m\ ss\s");
                        ImGui.TextColored(RedColorText, timeFmt);
                    }
                    else
                    {
                        ImGui.TextColored(OrangeColorText, $"{first.Action} in {first.Time:mm'm'}");
                    }
                }

                // Column: Actions (POP / RESET)
                ImGui.TableNextColumn();
                if (client.CanModify)
                {
                    if (fate.IsPopped())
                    {
                        ImGui.PushStyleColor(ImGuiCol.Button, RedButtonColor);
                        if (ImGui.SmallButton($"RESET##{fate.TrackerId}"))
                        {
                            _ = client.ResetPopAsync(fate.TrackerId);
                        }
                        ImGui.PopStyleColor();
                    }
                    else
                    {
                        ImGui.PushStyleColor(ImGuiCol.Button, BlueButtonColor);
                        if (ImGui.SmallButton($"POP##{fate.TrackerId}"))
                        {
                            long nowMs = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                            _ = client.SetPopTimeAsync(fate.TrackerId, nowMs);
                        }
                        ImGui.PopStyleColor();
                    }
                }
                else
                {
                    ImGui.TextDisabled(fate.IsPopped() ? "Popped" : "Ready");
                }
            }

            ImGui.EndTable();
        }
    }

    private void DrawDisconnectedPlaceholder(EurekaTrackerClient client, float scale)
    {
        ImGui.Dummy(new Vector2(0, 30 * scale));
        ImGui.Separator();
        ImGui.Spacing();

        if (client.IsInvalid)
        {
            ImGui.TextColored(RedColorText, "Invalid Tracker ID: The specified tracker does not exist on ffxiv-eureka.com.");
        }
        else if (!string.IsNullOrEmpty(client.ErrorMessage))
        {
            ImGui.TextColored(RedColorText, $"Tracker Error: {client.ErrorMessage}");
        }
        else
        {
            ImGui.TextColored(GoldAccent, "Not connected to a live Eureka Tracker.");
            ImGui.TextDisabled("To track NM pop timers, enter an existing 6-character tracker code above or click [+] to create a new one.");
        }

        ImGui.Spacing();

        string detectedId = trackerManager.InstanceService.GetBestDetectedInstanceId();
        if (trackerManager.CurrentZoneTracker != null)
        {
            if (!string.IsNullOrEmpty(detectedId))
            {
                ImGui.TextColored(GreenColorText, $"Active Eureka Server ID detected: {detectedId}");
            }
            ImGui.TextDisabled($"Current Expedition Zone: {trackerManager.CurrentZoneTracker.ZoneName}");
        }
        else
        {
            ImGui.TextDisabled("Location: Outside Eureka (Expedition tracker features on standby).");
        }

        ImGui.Spacing();
        DrawPublicTrackersSection(detectedId, scale);

        ImGui.Spacing();
        DrawRecentTrackersSection(scale);
    }

    /// <summary>
    /// Displays live public trackers from ffxiv-eureka.com for the player's current data center.
    /// Highlights trackers matching the player's detected Server ID and provides 1-click connection.
    /// </summary>
    private void DrawPublicTrackersSection(string detectedId, float scale)
    {
        int? dcId = trackerManager.GetCurrentDataCenterId();
        string dcName = trackerManager.GetCurrentDataCenterName() ?? "Data Center";

        // Auto-refresh when opening or when stale (> 60s)
        if (dcId.HasValue && !trackerManager.IsFetchingPublicTrackers &&
            (trackerManager.AvailablePublicTrackers.Count == 0 || !trackerManager.LastPublicTrackersFetch.HasValue || (DateTimeOffset.UtcNow - trackerManager.LastPublicTrackersFetch.Value).TotalSeconds > 60))
        {
            _ = trackerManager.RefreshPublicTrackersAsync();
        }

        ImGui.Separator();
        ImGui.Spacing();

        // Section header with title and refresh button
        int currentZoneId = trackerManager.CurrentZoneTracker?.ZoneId ?? 0;
        string zoneHeader = trackerManager.CurrentZoneTracker != null
            ? $"Public Trackers on {dcName} ({trackerManager.CurrentZoneTracker.ZoneName}):"
            : $"Public Trackers on {dcName} (All Zones):";

        ImGui.TextColored(GoldAccent, zoneHeader);
        ImGui.SameLine();

        if (trackerManager.IsFetchingPublicTrackers)
        {
            ImGui.TextDisabled("(Scanning...)");
        }
        else
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Sync))
            {
                _ = trackerManager.RefreshPublicTrackersAsync();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Refresh public tracker list from ffxiv-eureka.com");
            }
        }

        // Filter and sort trackers
        var list = trackerManager.AvailablePublicTrackers;
        if (currentZoneId > 0)
        {
            list = list.Where(t => t.ZoneId == currentZoneId).ToList();
        }

        var sorted = list
            .OrderByDescending(t => !string.IsNullOrEmpty(detectedId) && string.Equals(t.InstanceId, detectedId, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(t => !string.IsNullOrEmpty(t.InstanceId))
            .ThenByDescending(t => t.UpdatedAt ?? t.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(t => t.PoppedCount)
            .ToList();

        if (sorted.Count == 0)
        {
            if (trackerManager.IsFetchingPublicTrackers)
            {
                ImGui.TextDisabled("Scanning public directory on ffxiv-eureka.com...");
            }
            else
            {
                string noMsg = currentZoneId > 0
                    ? $"No active public trackers registered for {trackerManager.CurrentZoneTracker?.ZoneName} on {dcName}."
                    : $"No active public trackers registered on {dcName}.";
                ImGui.TextDisabled(noMsg);
                ImGui.TextDisabled("Click [+] above to create a new tracker and publish it to the instance.");
            }
            return;
        }

        ImGui.Spacing();

        int numColumns = currentZoneId > 0 ? 6 : 7;
        if (ImGui.BeginTable("##PublicTrackersTable", numColumns, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Code", ImGuiTableColumnFlags.WidthFixed, 90 * scale);
            if (currentZoneId == 0)
            {
                ImGui.TableSetupColumn("Zone", ImGuiTableColumnFlags.WidthFixed, 80 * scale);
            }
            ImGui.TableSetupColumn("Instance ID", ImGuiTableColumnFlags.WidthFixed, 115 * scale);
            ImGui.TableSetupColumn("Last Updated", ImGuiTableColumnFlags.WidthFixed, 90 * scale);
            ImGui.TableSetupColumn("Active", ImGuiTableColumnFlags.WidthFixed, 65 * scale);
            ImGui.TableSetupColumn("NMs Popped", ImGuiTableColumnFlags.WidthStretch, 85 * scale);
            ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 85 * scale);
            ImGui.TableHeadersRow();

            foreach (var pt in sorted)
            {
                bool isMatch = !string.IsNullOrEmpty(detectedId) && string.Equals(pt.InstanceId, detectedId, StringComparison.OrdinalIgnoreCase);

                ImGui.TableNextRow();

                // 1. Tracker 6-char Code + Copy button + Password Memory Key icon
                ImGui.TableNextColumn();
                ImGui.TextColored(GoldAccent, pt.TrackerId);

                string savedPwd = config.GetSavedPassword(pt.TrackerId) ?? string.Empty;
                if (!string.IsNullOrEmpty(savedPwd))
                {
                    ImGui.SameLine();
                    if (ImGuiComponents.IconButton($"##KeyPublic_{pt.TrackerId}", FontAwesomeIcon.Key))
                    {
                        ImGui.SetClipboardText(savedPwd);
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"Password remembered: '{savedPwd}'\nClick to copy password to clipboard.");
                    }

                    ImGui.SameLine();
                    if (ImGuiComponents.IconButton($"##SharePublic_{pt.TrackerId}", FontAwesomeIcon.ShareAlt))
                    {
                        ImGui.SetClipboardText($"https://ffxiv-eureka.com/{pt.TrackerId} | Password: {savedPwd}");
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"Copy share message (URL + Password: '{savedPwd}') to clipboard.");
                    }
                }

                ImGui.SameLine();
                if (ImGuiComponents.IconButton($"##CopyCode_{pt.TrackerId}", FontAwesomeIcon.Copy))
                {
                    ImGui.SetClipboardText(pt.TrackerId);
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"Copy code '{pt.TrackerId}' to clipboard");
                }

                // Optional Zone column if viewing all zones
                if (currentZoneId == 0)
                {
                    ImGui.TableNextColumn();
                    ImGui.Text(GetZoneName(pt.ZoneId));
                }

                // 2. Server ID / Instance ID
                ImGui.TableNextColumn();
                if (isMatch)
                {
                    ImGui.TextColored(GreenColorText, $"{pt.InstanceId} [MATCH!]");
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"This tracker matches your current active Eureka Server ID ({detectedId})!");
                    }
                }
                else if (!string.IsNullOrEmpty(pt.InstanceId))
                {
                    ImGui.Text(pt.InstanceId);
                }
                else
                {
                    ImGui.TextDisabled("-");
                }

                // 3. Last Updated
                ImGui.TableNextColumn();
                string age = pt.GetAgeString();
                ImGui.TextDisabled(string.IsNullOrEmpty(age) ? "-" : age);

                // 4. Time Active
                ImGui.TableNextColumn();
                string active = pt.GetTimeActiveString();
                ImGui.TextDisabled(active);

                // 5. NMs Popped
                ImGui.TableNextColumn();
                if (pt.PoppedCount > 0)
                {
                    ImGui.TextColored(GreenColorText, $"{pt.PoppedCount} NMs");
                }
                else
                {
                    ImGui.TextDisabled("0 NMs");
                }

                // 6. One-click Connect Button (with remembered password)
                ImGui.TableNextColumn();
                if (isMatch)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.20f, 0.55f, 0.30f, 1f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.70f, 0.38f, 1f));
                }

                if (ImGui.Button($"Connect##{pt.TrackerId}"))
                {
                    inputTrackerCode = pt.TrackerId;
                    inputTrackerPassword = savedPwd;
                    config.RememberTracker(pt.TrackerId, savedPwd, pt.InstanceId, pt.ZoneId);
                    _ = trackerManager.Client.JoinTrackerAsync(pt.TrackerId, savedPwd);
                }

                if (isMatch)
                {
                    ImGui.PopStyleColor(2);
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"Connect instantly to tracker '{pt.TrackerId}' (ffxiv-eureka.com/{pt.TrackerId}) with 1 click.");
                }
            }

            ImGui.EndTable();
        }
    }

    private static string GetZoneName(int zoneId) => zoneId switch
    {
        1 => "Anemos",
        2 => "Pagos",
        3 => "Pyros",
        4 => "Hydatos",
        _ => "Eureka",
    };

    /// <summary>
    /// Displays previously visited or created trackers with remembered passwords.
    /// Allows 1-click reconnection with preserved admin/edit permissions.
    /// </summary>
    private void DrawRecentTrackersSection(float scale)
    {
        if (config.TrackerHistory.Count == 0) return;

        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.CollapsingHeader($"Recent Trackers & Saved Passwords ({config.TrackerHistory.Count})###RecentTrackersHeader"))
        {
            ImGui.Spacing();
            if (ImGui.BeginTable("##RecentTrackersTable", 6, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Code", ImGuiTableColumnFlags.WidthFixed, 90 * scale);
                ImGui.TableSetupColumn("Zone", ImGuiTableColumnFlags.WidthFixed, 80 * scale);
                ImGui.TableSetupColumn("Instance ID", ImGuiTableColumnFlags.WidthFixed, 95 * scale);
                ImGui.TableSetupColumn("Password", ImGuiTableColumnFlags.WidthFixed, 140 * scale);
                ImGui.TableSetupColumn("Last Visited", ImGuiTableColumnFlags.WidthStretch, 80 * scale);
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 135 * scale);
                ImGui.TableHeadersRow();

                for (int i = 0; i < config.TrackerHistory.Count; i++)
                {
                    var entry = config.TrackerHistory[i];
                    ImGui.TableNextRow();

                    // 1. Code
                    ImGui.TableNextColumn();
                    ImGui.TextColored(GoldAccent, entry.TrackerId);
                    ImGui.SameLine();
                    if (ImGuiComponents.IconButton($"##CopyHist_{entry.TrackerId}", FontAwesomeIcon.Copy))
                    {
                        ImGui.SetClipboardText(entry.TrackerId);
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"Copy code '{entry.TrackerId}' to clipboard");
                    }

                    // 2. Zone
                    ImGui.TableNextColumn();
                    ImGui.Text(GetZoneName(entry.ZoneId));

                    // 3. Instance ID
                    ImGui.TableNextColumn();
                    if (!string.IsNullOrEmpty(entry.InstanceId))
                    {
                        ImGui.Text(entry.InstanceId);
                    }
                    else
                    {
                        ImGui.TextDisabled("-");
                    }

                    // 4. Password (with Copy button)
                    ImGui.TableNextColumn();
                    if (entry.HasPassword)
                    {
                        ImGui.TextColored(GoldAccent, entry.Password);
                        ImGui.SameLine();
                        if (ImGuiComponents.IconButton($"##CopyPwdHist_{entry.TrackerId}", FontAwesomeIcon.Key))
                        {
                            ImGui.SetClipboardText(entry.Password);
                        }
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip($"Click to copy password '{entry.Password}' to clipboard");
                        }
                    }
                    else
                    {
                        ImGui.TextDisabled("None (Read-only)");
                    }

                    // 5. Last Visited
                    ImGui.TableNextColumn();
                    ImGui.TextDisabled(entry.GetAgeString());

                    // 6. Actions: Connect, Share, Forget
                    ImGui.TableNextColumn();
                    if (ImGui.Button($"Connect##Hist_{entry.TrackerId}"))
                    {
                        inputTrackerCode = entry.TrackerId;
                        inputTrackerPassword = entry.Password;
                        config.RememberTracker(entry.TrackerId, entry.Password, entry.InstanceId, entry.ZoneId);
                        _ = trackerManager.Client.JoinTrackerAsync(entry.TrackerId, entry.Password);
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"Reconnect to {entry.TrackerId} with saved credentials and admin rights");
                    }

                    if (entry.HasPassword)
                    {
                        ImGui.SameLine();
                        if (ImGuiComponents.IconButton($"##ShareHist_{entry.TrackerId}", FontAwesomeIcon.ShareAlt))
                        {
                            ImGui.SetClipboardText($"https://ffxiv-eureka.com/{entry.TrackerId} | Password: {entry.Password}");
                        }
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip($"Copy share message (URL + Password: '{entry.Password}') to clipboard");
                        }
                    }

                    ImGui.SameLine();
                    if (ImGuiComponents.IconButton($"##DelHist_{entry.TrackerId}", FontAwesomeIcon.Trash))
                    {
                        config.ForgetTracker(entry.TrackerId);
                        break;
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Remove this tracker and its password from history");
                    }
                }

                ImGui.EndTable();
            }
        }
    }

    private void DrawConfiguration(float scale)
    {
        ImGui.TextColored(GoldAccent, "Eureka Tracker Settings");
        ImGui.Separator();
        ImGui.Spacing();

        bool autoCreate = config.TrackerAutoCreate;
        if (ImGui.Checkbox("Auto-create Tracker upon entering Eureka", ref autoCreate))
        {
            config.TrackerAutoCreate = autoCreate;
            config.Save();
            if (autoCreate)
            {
                trackerManager.TryAutoJoinOrCreateTracker();
            }
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Automatically generates a new tracker on ffxiv-eureka.com upon entering Eureka (or enabling), setting the detected Instance ID directly.");
        }

        // Indented sub-option for public tracker creation
        ImGui.Indent(20f);
        bool createPublic = config.TrackerCreatePublic;
        if (ImGui.Checkbox("Publish auto-created tracker to Data Center publicly", ref createPublic))
        {
            config.TrackerCreatePublic = createPublic;
            config.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("When creating a tracker automatically or manually, registers your Data Center on ffxiv-eureka.com so it appears on the public list for other players.");
        }
        ImGui.Unindent(20f);

        bool autoJoin = config.TrackerAutoJoinExisting;
        if (ImGui.Checkbox("Auto-connect to public tracker if Server ID matches", ref autoJoin))
        {
            config.TrackerAutoJoinExisting = autoJoin;
            config.Save();
            if (autoJoin)
            {
                trackerManager.TryAutoJoinOrCreateTracker();
            }
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Upon entering Eureka, scans public trackers for your Data Center and zone. If an existing tracker already has your matching Server ID, connects automatically.");
        }

        bool autoPop = config.TrackerAutoPopFate;
        if (ImGui.Checkbox("Auto-pop NMs when FATE spawns in game", ref autoPop))
        {
            config.TrackerAutoPopFate = autoPop;
            config.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Automatically marks the NM as killed/popped on the live tracker when its FATE appears.");
        }

        bool showServerIdInChat = config.TrackerDisplayServerIdInChat;
        if (ImGui.Checkbox("Print Instance / Server ID in chat upon zone entry", ref showServerIdInChat))
        {
            config.TrackerDisplayServerIdInChat = showServerIdInChat;
            config.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Prints the detected Server ID in chat when entering a Eureka zone. Useful for identifying unique instances.");
        }

        bool displayFatePop = config.TrackerDisplayFatePop;
        if (ImGui.Checkbox("Display NM pop alert in chat with map link", ref displayFatePop))
        {
            config.TrackerDisplayFatePop = displayFatePop;
            config.Save();
        }

        bool displayToast = config.TrackerDisplayToastPop;
        if (ImGui.Checkbox("Display on-screen quest toast when NM pops", ref displayToast))
        {
            config.TrackerDisplayToastPop = displayToast;
            config.Save();
        }

        bool playSound = config.TrackerPlayPopSound;
        if (ImGui.Checkbox("Play sound chime when NM pops", ref playSound))
        {
            config.TrackerPlayPopSound = playSound;
            config.Save();
        }

        bool showLevel = config.TrackerShowLevelInTable;
        if (ImGui.Checkbox("Show Level column in NM table", ref showLevel))
        {
            config.TrackerShowLevelInTable = showLevel;
            config.Save();
        }
    }

    private void SetMapFlag(ushort territoryId, ushort mapId, Vector2 position)
    {
        try
        {
            var mapPayload = new MapLinkPayload(territoryId, mapId, position.X, position.Y);
            gameGui.OpenMapWithMapLink(mapPayload);
        }
        catch (Exception ex)
        {
            EurekaSuitePlugin.PluginLog.Error(ex, "Failed to open map with map link.");
        }
    }
}
