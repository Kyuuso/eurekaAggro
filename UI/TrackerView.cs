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
using EurekaAggro.Configuration;
using EurekaAggro.Tracker;
using EurekaAggro.Tracker.Model;
using EurekaAggro.Tracker.Network;

namespace EurekaAggro.UI;

/// <summary>
/// Renders the complete Eureka Tracker GUI inspired by EurekaHelper,
/// featuring full Phoenix WebSocket live sync, auto/manual Instance ID detection and updates,
/// NM respawn timers, and weather forecasting.
/// </summary>
public class TrackerView
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
        ImGui.TextColored(GoldAccent, "Tracker:");
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
                ImGui.SetTooltip("Create a new Eureka Tracker on ffxiv-eureka.com");
            }

            ImGui.SameLine();

            // Code Input
            ImGui.SetNextItemWidth(110 * scale);
            ImGui.InputTextWithHint("##TrackerCode", "6-char Code", ref inputTrackerCode, 6);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Enter the 6-character code of an existing tracker");
            }

            ImGui.SameLine();

            // Password Input
            ImGui.SetNextItemWidth(140 * scale);
            ImGui.InputTextWithHint("##TrackerPassword", "Password (optional)", ref inputTrackerPassword, 50);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Optional password. Required to modify pop timers if the tracker is protected.");
            }

            ImGui.SameLine();

            // Connect Button
            if (ImGui.Button("Connect"))
            {
                if (!string.IsNullOrWhiteSpace(inputTrackerCode))
                {
                    config.TrackerLastCode = inputTrackerCode;
                    config.TrackerLastPassword = inputTrackerPassword;
                    config.Save();

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

            // Copy Password
            if (client.CanModify)
            {
                ImGui.SameLine();
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Key))
                {
                    ImGui.SetClipboardText(client.TrackerPassword);
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"Copy tracker password to clipboard: {client.TrackerPassword}");
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
                inputTrackerCode = newTrackerId;
                inputTrackerPassword = password;
                config.TrackerLastCode = newTrackerId;
                config.TrackerLastPassword = password;
                config.Save();

                await trackerManager.Client.JoinTrackerAsync(newTrackerId, password);

                // Auto-sync detected Server ID if present
                string detectedId = trackerManager.InstanceService.GetBestDetectedInstanceId();
                if (!string.IsNullOrEmpty(detectedId))
                {
                    int? dcId = trackerManager.GetCurrentDataCenterId();
                    await trackerManager.Client.SetInstanceInformationAsync(detectedId, dcId);
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
            ImGui.Text("Instance ID:");
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
            if (!string.IsNullOrEmpty(detectedId))
            {
                ImGui.TextColored(ImGuiColors.ParsedGreen, $"[Detected from memory: {detectedId}]");
            }
            else
            {
                ImGui.TextDisabled("[No Server ID detected yet - enter a Eureka zone]");
            }

            if (client.IsConnected && !string.IsNullOrEmpty(client.InstanceId))
            {
                ImGui.SameLine();
                ImGui.TextColored(GoldAccent, $"| Active on Tracker: {client.InstanceId}");
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
                ImGui.TableSetupColumn("Lv", ImGuiTableColumnFlags.WidthFixed, 36 * scale);
            }
            ImGui.TableSetupColumn("NM Boss", ImGuiTableColumnFlags.WidthStretch, 140 * scale);
            ImGui.TableSetupColumn("Spawn Mob", ImGuiTableColumnFlags.WidthStretch, 130 * scale);
            ImGui.TableSetupColumn("Popped At", ImGuiTableColumnFlags.WidthFixed, 90 * scale);
            ImGui.TableSetupColumn("Respawn In", ImGuiTableColumnFlags.WidthFixed, 100 * scale);
            ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 80 * scale);

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

                    if (ImGui.Button("Apply"))
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
                    if (ImGui.Button("Cancel"))
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
        if (!string.IsNullOrEmpty(detectedId))
        {
            ImGui.TextColored(GreenColorText, $"Active Eureka Server ID detected: {detectedId}");
        }

        if (trackerManager.CurrentZoneTracker != null)
        {
            ImGui.TextDisabled($"Current Expedition Zone: {trackerManager.CurrentZoneTracker.ZoneName}");
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
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Automatically generates a new tracker on ffxiv-eureka.com if entering Eureka without an active connection.");
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
            EurekaAggroPlugin.PluginLog.Error(ex, "Failed to open map with map link.");
        }
    }
}
