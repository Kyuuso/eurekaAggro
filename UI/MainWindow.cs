using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using EurekaAggro.Configuration;
using EurekaAggro.Data;
using EurekaAggro.Models;
using EurekaAggro.Services;
using BFE;
using BFE.Ui.MainWindow;
using BFE.Ui.SettingsWindow;
using BFE.Scheduler;
using EurekaAggro.Tracker;

namespace EurekaAggro.UI;

/// <summary>
/// Unified master window for the Eureka Aggro Suite.
/// Matches the modular schematic:
/// - Top Header: Icon, Name, Credits summary, Live Zone/Player Status, Quick Links.
/// - Top Navigation Tabs: [ Aggro Lines ] | [ Fate Engine ] | [ About & Credits ].
/// - Middle Scrollable Content: Rendered based on the active module and its selected sub-view.
/// - Bottom Navigation Bar: [ Main ] | [ Configuration ] switching the active module's sub-view.
/// All user-facing strings are strictly in English.
/// </summary>
public class MainWindow
{
    public enum SubView
    {
        Main,
        Statistics,
        Configuration
    }

    private readonly PluginConfiguration config;
    private readonly MobDatabase mobDatabase;
    private readonly ActionDatabase actionDatabase;
    private readonly EurekaEnvironmentService environmentService;
    private readonly IClientState clientState;
    private readonly DragonWalkService dragonWalkService;
    private readonly CastAlertWindow castAlertWindow;
    private readonly ISharedImmediateTexture? iconTexture;
    private readonly BunnyAutomationService bunnyService;
    private readonly IObjectTable objectTable;
    private readonly TrackerManager trackerManager;
    private readonly TrackerView trackerView;

    public bool IsOpen = false;

    // Programmatic target tab request (-1 = no request, user-driven selection)
    private int targetMainTab = -1;
    public int ActiveMainTab { get; private set; } = 0;

    // Sub-view states for each module ("cada pestaña con su cosa, con su configuración")
    public SubView AggroSubView = SubView.Main;
    public SubView FateSubView = SubView.Main;
    public SubView TrackerSubView = SubView.Main;

    // Search filters
    private string mobSearchFilter = string.Empty;
    private string actionSearchFilter = string.Empty;

    // Visual palette
    private static readonly Vector4 GoldAccent = new(0.961f, 0.729f, 0.259f, 1f);      // #F5BA42
    private static readonly Vector4 CardBackground = new(0.106f, 0.110f, 0.133f, 1f);    // #1B1C22
    private static readonly Vector4 TextMuted = new(0.608f, 0.620f, 0.663f, 1f);

    public MainWindow(
        PluginConfiguration config,
        MobDatabase mobDatabase,
        ActionDatabase actionDatabase,
        EurekaEnvironmentService environmentService,
        IClientState clientState,
        DragonWalkService dragonWalkService,
        CastAlertWindow castAlertWindow,
        ITextureProvider textureProvider,
        IDalamudPluginInterface pluginInterface,
        BunnyAutomationService bunnyService,
        IObjectTable objectTable,
        TrackerManager trackerManager,
        IGameGui gameGui)
    {
        this.config = config;
        this.mobDatabase = mobDatabase;
        this.actionDatabase = actionDatabase;
        this.environmentService = environmentService;
        this.clientState = clientState;
        this.dragonWalkService = dragonWalkService;
        this.castAlertWindow = castAlertWindow;
        this.bunnyService = bunnyService;
        this.objectTable = objectTable;
        this.trackerManager = trackerManager;
        this.trackerView = new TrackerView(trackerManager, config, clientState, gameGui);

        if (!string.IsNullOrEmpty(pluginInterface.AssemblyLocation.DirectoryName))
        {
            var iconPath = Path.Combine(pluginInterface.AssemblyLocation.DirectoryName, "images", "icon.png");
            if (File.Exists(iconPath))
            {
                iconTexture = textureProvider.GetFromFileAbsolute(iconPath);
            }
            else
            {
                var rootIcon = Path.Combine(pluginInterface.AssemblyLocation.DirectoryName, "icon.png");
                if (File.Exists(rootIcon))
                {
                    iconTexture = textureProvider.GetFromFileAbsolute(rootIcon);
                }
            }
        }
    }

    public void OpenAggroRadar(SubView subView = SubView.Main)
    {
        targetMainTab = 0;
        AggroSubView = subView;
        IsOpen = true;
    }

    public void OpenFateEngine(SubView subView = SubView.Main)
    {
        targetMainTab = 1;
        FateSubView = subView;
        IsOpen = true;
    }

    public void OpenTracker(SubView subView = SubView.Main)
    {
        targetMainTab = 2;
        TrackerSubView = subView;
        IsOpen = true;
    }

    public void OpenAbout()
    {
        targetMainTab = 3;
        IsOpen = true;
    }

    public void Draw()
    {
        if (!IsOpen) return;

        var scale = ImGuiHelpers.GlobalScale;
        ImGui.SetNextWindowSize(new Vector2(880 * scale, 680 * scale), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Eureka Aggro Suite###EurekaAggroMainWindow", ref IsOpen))
        {
            // 1. Top Master Header: Icon, Name, Credits summary, State
            DrawMasterHeader(scale);

            ImGui.Separator();
            ImGui.Spacing();

            // 2. Top Module Tabs
            if (ImGui.BeginTabBar("##EurekaMasterTabs", ImGuiTabBarFlags.FittingPolicyScroll))
            {
                // TAB 1: AGGRO LINES
                var aggroFlags = (targetMainTab == 0) ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
                if (ImGui.BeginTabItem("Aggro Lines###TabAggro", aggroFlags))
                {
                    ActiveMainTab = 0;
                    DrawModuleBody(0, scale);
                    ImGui.EndTabItem();
                }

                // TAB 2: BUNNY FATE ENGINE
                var fateFlags = (targetMainTab == 1) ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
                if (ImGui.BeginTabItem("Bunny Fate Engine###TabFate", fateFlags))
                {
                    ActiveMainTab = 1;
                    DrawModuleBody(1, scale);
                    ImGui.EndTabItem();
                }

                // TAB 3: TRACKER
                var trackerFlags = (targetMainTab == 2) ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
                if (ImGui.BeginTabItem("Tracker###TabTracker", trackerFlags))
                {
                    ActiveMainTab = 2;
                    DrawModuleBody(2, scale);
                    ImGui.EndTabItem();
                }

                // TAB 4: ABOUT & CREDITS
                var aboutFlags = (targetMainTab == 3) ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
                if (ImGui.BeginTabItem("About & Credits###TabAbout", aboutFlags))
                {
                    ActiveMainTab = 3;
                    DrawAboutSection(scale);
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }

            // Consume programmatic navigation request so ImGui handles user clicks freely
            targetMainTab = -1;
        }
        ImGui.End();
    }

    /// <summary>
    /// Master header displayed at the top of the suite window:
    /// Plugin Icon, Title, Version, Active Expedition Status.
    /// </summary>
    private void DrawMasterHeader(float scale)
    {
        // Icon display
        if (iconTexture?.TryGetWrap(out var wrap, out _) == true && wrap != null)
        {
            ImGui.Image(wrap.Handle, new Vector2(38 * scale, 38 * scale));
            ImGui.SameLine();
        }

        // Title and zone status
        var territoryId = clientState.TerritoryType;
        bool isEureka = ValidZones.IsEureka(territoryId);
        string zoneName = ValidZones.GetZoneName(territoryId);

        ImGui.BeginGroup();
        {
            ImGui.TextColored(GoldAccent, "Eureka Aggro Suite");
            ImGui.SameLine();
            ImGui.TextDisabled($"v{GetType().Assembly.GetName().Version?.ToString(3) ?? "1.2.3"}");
            ImGui.SameLine();
            ImGui.TextDisabled("• by Kyuuso, DhogGPT & Joshua-XIV");

            if (isEureka)
            {
                int effectiveLvl = EurekaLevelService.GetEffectiveElementalLevel(territoryId, config.AutoDetectElementalLevel, config.PlayerElementalLevel);
                string mode = config.AutoDetectElementalLevel ? "Auto-synced" : "Manual";
                string weather = environmentService.GetCurrentWeatherName();
                int etHour = EurekaEnvironmentService.GetEorzeaHour();
                string timeStr = $"{etHour:D2}:00 ET ({(EurekaEnvironmentService.IsNight() ? "Night" : "Day")})";

                ImGui.TextColored(ImGuiColors.HealerGreen, $"● Location: {zoneName} (Elemental Lv. {effectiveLvl} [{mode}]) | {weather} | {timeStr}");
            }
            else
            {
                ImGui.TextColored(ImGuiColors.DalamudGrey, $"○ Location: {zoneName} [Outside Eureka - Expedition features on standby]");
            }
        }
        ImGui.EndGroup();
    }

    /// <summary>
    /// Renders the module content area along with the bottom navigation switcher:
    /// [ Main ] | [ Statistics ] | [ Configuration ]
    /// </summary>
    private void DrawModuleBody(int moduleIndex, float scale)
    {
        var bottomBarHeight = 38f * scale;

        // Middle Scrollable Content Child Area
        if (ImGui.BeginChild($"##ModuleContent_{moduleIndex}", new Vector2(0, -bottomBarHeight - (8f * scale)), true, ImGuiWindowFlags.None))
        {
            if (moduleIndex == 0) // AGGRO LINES
            {
                switch (AggroSubView)
                {
                    case SubView.Main:
                        DrawAggroRadarMain(scale);
                        break;
                    case SubView.Statistics:
                        DrawAggroRadarStatistics(scale);
                        break;
                    case SubView.Configuration:
                        DrawAggroRadarConfiguration();
                        break;
                }
            }
            else if (moduleIndex == 1) // BUNNY FATE ENGINE
            {
                switch (FateSubView)
                {
                    case SubView.Main:
                        DrawFateEngineMain(scale);
                        break;
                    case SubView.Statistics:
                        DrawFateStatsSection(scale);
                        break;
                    case SubView.Configuration:
                        DrawFateEngineConfiguration();
                        break;
                }
            }
            else if (moduleIndex == 2) // TRACKER
            {
                trackerView.Draw(TrackerSubView, scale);
            }
        }
        ImGui.EndChild();

        // Bottom Navigation Bar
        ImGui.Spacing();
        DrawBottomBar(moduleIndex, scale);
    }

    /// <summary>
    /// Bottom navigation bar:
    /// [ Main ] | [ Statistics ] | [ Configuration ]
    /// </summary>
    private void DrawBottomBar(int moduleIndex, float scale)
    {
        var availWidth = ImGui.GetContentRegionAvail().X;

        // Tracker module has 2 sub-views: [ Main ] | [ Configuration ]
        if (moduleIndex == 2)
        {
            var trackerBtnWidth = (availWidth - (10f * scale)) / 2f;
            var trackerBtnHeight = 32f * scale;

            // 1. MAIN
            bool isTrackerMainActive = (TrackerSubView == SubView.Main);
            if (isTrackerMainActive)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, GoldAccent);
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.08f, 0.08f, 0.08f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.98f, 0.78f, 0.32f, 1f));
            }

            if (ImGui.Button($"Main###BottomMainBtn_{moduleIndex}", new Vector2(trackerBtnWidth, trackerBtnHeight)))
            {
                TrackerSubView = SubView.Main;
            }

            if (isTrackerMainActive)
            {
                ImGui.PopStyleColor(3);
            }

            ImGui.SameLine(0, 10f * scale);

            // 2. CONFIGURATION
            bool isTrackerConfigActive = (TrackerSubView == SubView.Configuration);
            if (isTrackerConfigActive)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, GoldAccent);
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.08f, 0.08f, 0.08f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.98f, 0.78f, 0.32f, 1f));
            }

            if (ImGui.Button($"Configuration###BottomConfigBtn_{moduleIndex}", new Vector2(trackerBtnWidth, trackerBtnHeight)))
            {
                TrackerSubView = SubView.Configuration;
            }

            if (isTrackerConfigActive)
            {
                ImGui.PopStyleColor(3);
            }

            return;
        }

        var btnWidth = (availWidth - (20f * scale)) / 3f;
        var btnHeight = 32f * scale;

        var activeSubView = (moduleIndex == 0) ? AggroSubView : FateSubView;

        // 1. BUTTON: MAIN
        bool isMainActive = (activeSubView == SubView.Main);
        if (isMainActive)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, GoldAccent);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.08f, 0.08f, 0.08f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.98f, 0.78f, 0.32f, 1f));
        }

        if (ImGui.Button($"Main###BottomMainBtn_{moduleIndex}", new Vector2(btnWidth, btnHeight)))
        {
            if (moduleIndex == 0) AggroSubView = SubView.Main;
            else FateSubView = SubView.Main;
        }

        if (isMainActive)
        {
            ImGui.PopStyleColor(3);
        }

        ImGui.SameLine(0, 10f * scale);

        // 2. BUTTON: STATISTICS
        bool isStatsActive = (activeSubView == SubView.Statistics);
        if (isStatsActive)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, GoldAccent);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.08f, 0.08f, 0.08f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.98f, 0.78f, 0.32f, 1f));
        }

        if (ImGui.Button($"Statistics###BottomStatsBtn_{moduleIndex}", new Vector2(btnWidth, btnHeight)))
        {
            if (moduleIndex == 0) AggroSubView = SubView.Statistics;
            else FateSubView = SubView.Statistics;
        }

        if (isStatsActive)
        {
            ImGui.PopStyleColor(3);
        }

        ImGui.SameLine(0, 10f * scale);

        // 3. BUTTON: CONFIGURATION
        bool isConfigActive = (activeSubView == SubView.Configuration);
        if (isConfigActive)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, GoldAccent);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.08f, 0.08f, 0.08f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.98f, 0.78f, 0.32f, 1f));
        }

        if (ImGui.Button($"Configuration###BottomConfigBtn_{moduleIndex}", new Vector2(btnWidth, btnHeight)))
        {
            if (moduleIndex == 0) AggroSubView = SubView.Configuration;
            else FateSubView = SubView.Configuration;
        }

        if (isConfigActive)
        {
            ImGui.PopStyleColor(3);
        }
    }

    #region MODULE 1: AGGRO LINES - MAIN VIEW
    private void DrawAggroRadarMain(float scale)
    {
        // 1. Status Overview Card
        ImGui.BeginGroup();
        {
            var radarStatus = config.Enabled ? "Enabled" : "Disabled";
            var radarColor = config.Enabled ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed;

            ImGui.TextColored(GoldAccent, "Aggro Radar Status:");
            ImGui.SameLine();
            ImGui.TextColored(radarColor, $"● {radarStatus}");

            ImGui.SameLine(280 * scale);
            ImGui.TextColored(GoldAccent, "Dragon Auto-Walk:");
            ImGui.SameLine();
            var autoWalkStatus = config.AutoWalkNearDragons ? "Active" : "Disabled";
            ImGui.TextColored(config.AutoWalkNearDragons ? ImGuiColors.HealerGreen : TextMuted, $"● {autoWalkStatus}");

            ImGui.SameLine(540 * scale);
            ImGui.TextColored(GoldAccent, "Cast Alerts:");
            ImGui.SameLine();
            var alertStatus = config.ShowCastAlerts ? "Active" : "Disabled";
            ImGui.TextColored(config.ShowCastAlerts ? ImGuiColors.HealerGreen : TextMuted, $"● {alertStatus}");
        }
        ImGui.EndGroup();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // 2. Live Nearby Detected Threats
        if (ImGui.CollapsingHeader("Live Nearby Threats (Radar Detection Range)", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawLiveThreatsTable(scale);
        }

        ImGui.Spacing();

        // 3. Monsters Reference Database
        if (ImGui.CollapsingHeader("Eureka Monsters Reference Database"))
        {
            DrawMonstersReferenceTable();
        }

        ImGui.Spacing();

        // 4. Enemy Actions & Counters Database
        if (ImGui.CollapsingHeader("Enemy Actions & Tactical Counters Database"))
        {
            DrawActionsReferenceTable();
        }
    }

    private void DrawLiveThreatsTable(float scale)
    {
        var player = objectTable.LocalPlayer;
        if (player == null || !clientState.IsLoggedIn)
        {
            ImGui.TextDisabled("Player character not logged in or unavailable.");
            return;
        }

        var territoryId = clientState.TerritoryType;
        if (!ValidZones.IsEureka(territoryId))
        {
            ImGui.Spacing();
            ImGui.TextColored(ImGuiColors.DalamudGrey, "○ Expedition radar is on standby while outside Eureka.");
            ImGui.TextDisabled("Live threat detection and danger cones will automatically activate once you enter Anemos, Pagos, Pyros, or Hydatos.");
            ImGui.Spacing();
            return;
        }

        var playerPos = player.Position;
        var threats = new List<(string Name, int Level, float Distance, AggroType Aggro, DangerLevel Danger, bool IsDragon)>();

        foreach (var obj in objectTable)
        {
            if (obj is not IBattleChara mob || mob.GameObjectId == player.GameObjectId) continue;
            if (mob.CurrentHp <= 0 || !mob.IsTargetable) continue;
            if (mob is IPlayerCharacter || mob.ObjectKind != ObjectKind.BattleNpc) continue;
            if (mob.OwnerId != 0 && mob.OwnerId != 0xE000_0000 && mob.OwnerId != 0xFFFF_FFFF) continue;
            if (obj is IBattleNpc bNpc && (bNpc.BattleNpcKind == BattleNpcSubKind.Pet ||
                                           bNpc.BattleNpcKind == BattleNpcSubKind.Buddy)) continue;

            float dist = Vector3.Distance(playerPos, mob.Position);
            if (dist <= config.DetectionRange)
            {
                var data = mobDatabase.GetOrRegister(mob.BaseId, mob.Name.TextValue, mob.HitboxRadius);
                var aggro = data.AggroType;
                var danger = data.DangerLevel;
                var isDragon = aggro == AggroType.Sound || data.Name.Contains("dragon", StringComparison.OrdinalIgnoreCase);
                int lvl = data.ElementalLevel > 0 ? data.ElementalLevel : (int)mob.Level;

                threats.Add((data.Name, lvl, dist, aggro, danger, isDragon));
            }
        }

        if (threats.Count == 0)
        {
            ImGui.TextDisabled("No hostile enemies detected within your radar detection range.");
            return;
        }

        // Sort by distance
        threats.Sort((a, b) => a.Distance.CompareTo(b.Distance));

        if (ImGui.BeginTable("##LiveThreatsTable", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Target Name", ImGuiTableColumnFlags.WidthStretch, 2.2f);
            ImGui.TableSetupColumn("Level", ImGuiTableColumnFlags.WidthFixed, 45 * scale);
            ImGui.TableSetupColumn("Distance", ImGuiTableColumnFlags.WidthFixed, 75 * scale);
            ImGui.TableSetupColumn("Aggro Mechanic", ImGuiTableColumnFlags.WidthStretch, 1.6f);
            ImGui.TableSetupColumn("Danger Tier", ImGuiTableColumnFlags.WidthStretch, 1.2f);
            ImGui.TableSetupColumn("Safety Notice", ImGuiTableColumnFlags.WidthStretch, 2.0f);
            ImGui.TableHeadersRow();

            foreach (var threat in threats.Take(15))
            {
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(threat.Name);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(threat.Level > 0 ? threat.Level.ToString() : "-");

                ImGui.TableNextColumn();
                var distColor = threat.Distance < 10.5f ? ImGuiColors.DalamudRed : (threat.Distance < 20f ? ImGuiColors.DalamudYellow : ImGuiColors.DalamudGrey);
                ImGui.TextColored(distColor, $"{threat.Distance:F1} m");

                ImGui.TableNextColumn();
                var aggroColor = threat.Aggro switch
                {
                    AggroType.Sound => new Vector4(1.0f, 0.4f, 0.4f, 1f),
                    AggroType.Blood => new Vector4(0.9f, 0.1f, 0.3f, 1f),
                    AggroType.Magic => new Vector4(0.4f, 0.9f, 1.0f, 1f),
                    AggroType.Proximity => new Vector4(1.0f, 0.8f, 0.2f, 1f),
                    _ => new Vector4(0.4f, 1.0f, 0.4f, 1f)
                };
                ImGui.TextColored(aggroColor, threat.Aggro.ToString());

                ImGui.TableNextColumn();
                var dangerColor = threat.Danger switch
                {
                    DangerLevel.Danger => ImGuiColors.DalamudRed,
                    DangerLevel.Caution => new Vector4(1f, 0.6f, 0.2f, 1f),
                    DangerLevel.Easy => ImGuiColors.HealerGreen,
                    _ => ImGuiColors.DalamudGrey
                };
                ImGui.TextColored(dangerColor, threat.Danger.ToString());

                ImGui.TableNextColumn();
                if (threat.IsDragon)
                {
                    ImGui.TextColored(ImGuiColors.DalamudYellow, "Walk mode required (< 10.5m)");
                }
                else if (threat.Aggro == AggroType.Sight)
                {
                    ImGui.TextColored(TextMuted, "Avoid front vision cone");
                }
                else if (threat.Aggro == AggroType.Blood)
                {
                    ImGui.TextColored(new Vector4(0.9f, 0.3f, 0.3f, 1f), "Aggros low HP (< 80%)");
                }
                else
                {
                    ImGui.TextDisabled("Normal proximity");
                }
            }

            ImGui.EndTable();
        }
    }

    private void DrawMonstersReferenceTable()
    {
        ImGui.InputTextWithHint("##MobSearch", "Search monster by name...", ref mobSearchFilter, 100);

        ImGui.SameLine();
        if (ImGui.Button("Reset Filter"))
        {
            mobSearchFilter = string.Empty;
        }

        var mobs = mobDatabase.Mobs.Values.ToList();
        if (!string.IsNullOrWhiteSpace(mobSearchFilter))
        {
            mobs = mobs.Where(m => m.Name.Contains(mobSearchFilter, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        ImGui.TextDisabled($"Showing {mobs.Count} registered monsters in database");

        if (ImGui.BeginTable("##MobTable", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(0, 300)))
        {
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 2.5f);
            ImGui.TableSetupColumn("Elemental Lv.", ImGuiTableColumnFlags.WidthFixed, 85);
            ImGui.TableSetupColumn("Aggro Type", ImGuiTableColumnFlags.WidthStretch, 1.5f);
            ImGui.TableSetupColumn("Danger", ImGuiTableColumnFlags.WidthStretch, 1.2f);
            ImGui.TableSetupColumn("Detection Radius", ImGuiTableColumnFlags.WidthStretch, 1.5f);
            ImGui.TableHeadersRow();

            foreach (var mob in mobs)
            {
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(mob.Name);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(mob.ElementalLevel > 0 ? mob.ElementalLevel.ToString() : "-");

                ImGui.TableNextColumn();
                var aggroColor = mob.AggroType switch
                {
                    AggroType.Sound => new Vector4(1.0f, 0.4f, 0.4f, 1.0f),
                    AggroType.Blood => new Vector4(0.9f, 0.1f, 0.3f, 1.0f),
                    AggroType.Magic => new Vector4(0.4f, 0.9f, 1.0f, 1.0f),
                    AggroType.Proximity => new Vector4(1.0f, 0.8f, 0.2f, 1.0f),
                    AggroType.Sight => new Vector4(0.4f, 1.0f, 0.4f, 1.0f),
                    _ => Vector4.One
                };
                ImGui.TextColored(aggroColor, mob.AggroType.ToString());

                ImGui.TableNextColumn();
                var dangerColor = mob.DangerLevel switch
                {
                    DangerLevel.Danger => ImGuiColors.DalamudRed,
                    DangerLevel.Caution => new Vector4(1.0f, 0.6f, 0.2f, 1.0f),
                    DangerLevel.Easy => ImGuiColors.HealerGreen,
                    _ => ImGuiColors.DalamudGrey
                };
                ImGui.TextColored(dangerColor, mob.DangerLevel.ToString());

                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{mob.TotalRadius:F1} m");
            }

            ImGui.EndTable();
        }
    }

    private void DrawActionsReferenceTable()
    {
        ImGui.InputTextWithHint("##ActionSearch", "Search enemy action or mob name...", ref actionSearchFilter, 100);

        ImGui.SameLine();
        if (ImGui.Button("Reset Filter##Action"))
        {
            actionSearchFilter = string.Empty;
        }

        var actions = actionDatabase.Actions.Values.ToList();
        if (!string.IsNullOrWhiteSpace(actionSearchFilter))
        {
            actions = actions.Where(a =>
                a.ActionName.Contains(actionSearchFilter, StringComparison.OrdinalIgnoreCase) ||
                a.MobName.Contains(actionSearchFilter, StringComparison.OrdinalIgnoreCase) ||
                a.AlertMessage.Contains(actionSearchFilter, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        ImGui.TextDisabled($"Showing {actions.Count} dangerous enemy casts in database");

        if (ImGui.BeginTable("##ActionTable", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(0, 300)))
        {
            ImGui.TableSetupColumn("Action Name", ImGuiTableColumnFlags.WidthStretch, 2.0f);
            ImGui.TableSetupColumn("Mob Name", ImGuiTableColumnFlags.WidthStretch, 2.0f);
            ImGui.TableSetupColumn("Interruptible", ImGuiTableColumnFlags.WidthFixed, 85);
            ImGui.TableSetupColumn("Required Counter", ImGuiTableColumnFlags.WidthStretch, 1.8f);
            ImGui.TableSetupColumn("Alert Message", ImGuiTableColumnFlags.WidthStretch, 2.5f);
            ImGui.TableHeadersRow();

            foreach (var action in actions)
            {
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(action.ActionName);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(action.MobName);

                ImGui.TableNextColumn();
                if (action.IsInterruptible)
                {
                    ImGui.TextColored(new Vector4(0.2f, 1.0f, 0.4f, 1.0f), "YES");
                }
                else
                {
                    ImGui.TextDisabled("No");
                }

                ImGui.TableNextColumn();
                string counter = action.RequiresSilence ? "Silence / Interject" : (action.RequiresStun ? "Stun" : (action.RequiresLineOfSight ? "Break Line of Sight" : "Mitigation"));
                var counterColor = action.RequiresSilence ? new Vector4(0.2f, 1.0f, 0.4f, 1.0f) : (action.RequiresStun ? new Vector4(1.0f, 0.8f, 0.2f, 1.0f) : (action.RequiresLineOfSight ? new Vector4(1.0f, 0.4f, 0.4f, 1.0f) : Vector4.One));
                ImGui.TextColored(counterColor, counter);

                ImGui.TableNextColumn();
                string msg = action.AlertMessage;
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                if (ImGui.InputText($"##Msg_{action.ActionId}", ref msg, 120))
                {
                    action.AlertMessage = msg;
                }
            }

            ImGui.EndTable();
        }
    }
    #endregion

    #region MODULE 1: AGGRO LINES - STATISTICS VIEW
    private void DrawAggroRadarStatistics(float scale)
    {
        var buttonHeight = Math.Max(28f * scale, ImGui.GetFrameHeight());

        // Session Statistics Grid
        ImGui.TextColored(GoldAccent, "Aggro Radar - Current Session Statistics:");
        if (ImGui.BeginTable("##AggroSessionGrid", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Dragons Bypassed", ImGuiTableColumnFlags.WidthStretch, 0.25f);
            ImGui.TableSetupColumn("Auto-Walk Triggers", ImGuiTableColumnFlags.WidthStretch, 0.25f);
            ImGui.TableSetupColumn("Cast Alerts Handled", ImGuiTableColumnFlags.WidthStretch, 0.25f);
            ImGui.TableSetupColumn("Close Calls Avoided", ImGuiTableColumnFlags.WidthStretch, 0.25f);
            ImGui.TableHeadersRow();

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(ImGuiColors.HealerGreen, config.SessionDragonsBypassed.ToString());
            ImGui.TableNextColumn();
            ImGui.TextColored(GoldAccent, config.SessionAutoWalkActivations.ToString());
            ImGui.TableNextColumn();
            ImGui.TextColored(new Vector4(0.7f, 0.85f, 1f, 1f), config.SessionCastAlertsTriggered.ToString());
            ImGui.TableNextColumn();
            ImGui.TextColored(GoldAccent, config.SessionCloseCallsAvoided.ToString());

            ImGui.EndTable();
        }

        ImGui.Spacing();

        // Lifetime Historical Statistics Table
        ImGui.TextColored(GoldAccent, "Aggro Radar - Lifetime Historical Overall:");
        var dict = new Dictionary<string, int>
        {
            { "Sleeping Dragons Safely Bypassed", config.LifetimeDragonsBypassed },
            { "Auto-Walk Stealth Activations", config.LifetimeAutoWalkActivations },
            { "Dangerous Cast Alerts Triggered", config.LifetimeCastAlertsTriggered },
            { "Close-Range Threats Survived (< 6m)", config.LifetimeCloseCallsAvoided }
        };

        if (ImGui.BeginTable("##AggroLifetimeTable", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Metric / Safety Record", ImGuiTableColumnFlags.WidthStretch, 0.7f);
            ImGui.TableSetupColumn("Lifetime Count", ImGuiTableColumnFlags.WidthStretch, 0.3f);
            ImGui.TableHeadersRow();

            foreach (var (lbl, val) in dict)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(lbl);

                ImGui.TableNextColumn();
                ImGui.TextColored(GoldAccent, val.ToString("N0"));
            }
            ImGui.EndTable();
        }

        ImGui.Spacing();

        // Reset Button with Ctrl guard
        var isCtrlHeld = ImGui.GetIO().KeyCtrl;
        using (var _ = ImRaii.PushStyle(ImGuiStyleVar.Alpha, 0.5f, !isCtrlHeld))
        {
            if (ImGui.Button("RESET AGGRO STATS (HOLD CTRL)###ResetAggroStatsBtn", new Vector2(ImGui.GetContentRegionAvail().X, buttonHeight)) && isCtrlHeld)
            {
                config.ResetAggroStats();
            }
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(isCtrlHeld ? "Click to reset your expedition aggro radar statistics." : "Hold Ctrl to enable the reset button.");
        }
    }
    #endregion

    #region MODULE 1: AGGRO LINES - CONFIGURATION VIEW
    private void DrawAggroRadarConfiguration()
    {
        bool enabled = config.Enabled;
        if (ImGui.Checkbox("Enable Eureka Aggro Radar", ref enabled))
        {
            config.Enabled = enabled;
            config.Save();
        }

        bool onlyEureka = config.OnlyInEureka;
        if (ImGui.Checkbox("Activate only inside Eureka expedition zones", ref onlyEureka))
        {
            config.OnlyInEureka = onlyEureka;
            config.Save();
        }

        float range = config.DetectionRange;
        if (ImGui.SliderFloat("Detection range", ref range, 10.0f, 100.0f, "%.1f m"))
        {
            config.DetectionRange = range;
            config.Save();
        }

        float margin = config.SafetyMargin;
        if (ImGui.SliderFloat("Latency / Safety margin buffer", ref margin, 0.0f, 1.5f, "+%.2f m"))
        {
            config.SafetyMargin = margin;
            config.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Adds an extra safety buffer to aggro circles to account for network ping and server tick latency.");
        }

        bool vertFilter = config.EnableVerticalFilter;
        if (ImGui.Checkbox("Filter out monsters on cliffs / ledges / caves (different elevation)", ref vertFilter))
        {
            config.EnableVerticalFilter = vertFilter;
            config.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("In Eureka Pagos/Pyros, monsters cannot aggro across vertical drop-offs. Filters false alarms.");
        }

        if (config.EnableVerticalFilter)
        {
            float vertTol = config.VerticalTolerance;
            if (ImGui.SliderFloat("Vertical elevation tolerance", ref vertTol, 3.0f, 12.0f, "%.1f m"))
            {
                config.VerticalTolerance = vertTol;
                config.Save();
            }
        }

        ImGui.Separator();
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Elemental Level & Safe Mob Filtering (Anti-Clutter):");

        bool filterSafe = config.FilterSafeMobs;
        if (ImGui.Checkbox("Filter out lower-level monsters that cannot aggro you", ref filterSafe))
        {
            config.FilterSafeMobs = filterSafe;
            config.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Hides cones and circles for monsters whose level is too low to attack you. Perfect for avoiding clutter when hunting bunny chests or traveling through lower level areas.");
        }

        if (config.FilterSafeMobs)
        {
            bool autoDetect = config.AutoDetectElementalLevel;
            if (ImGui.Checkbox("Auto-detect & sync Elemental Level (Memory & Zone cap)", ref autoDetect))
            {
                config.AutoDetectElementalLevel = autoDetect;
                config.Save();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Reads your active Elemental Level in real time from game memory, automatically syncing to zone caps (e.g. 20 in Anemos, 35 in Pagos, 50 in Pyros, 60 in Hydatos).");
            }

            int activeLvl = EurekaLevelService.GetEffectiveElementalLevel(clientState.TerritoryType, config.AutoDetectElementalLevel, config.PlayerElementalLevel);
            ImGui.TextColored(new Vector4(0.2f, 1.0f, 0.4f, 1.0f), $"Active Elemental Level: {activeLvl} {(config.AutoDetectElementalLevel ? "[Auto-synced from Eureka]" : "[Manual override]")}");

            if (!config.AutoDetectElementalLevel)
            {
                int playerLvl = config.PlayerElementalLevel;
                if (ImGui.SliderInt("Manual Elemental Level", ref playerLvl, 1, 60))
                {
                    config.PlayerElementalLevel = playerLvl;
                    config.Save();
                }

                ImGui.TextDisabled("Quick Level Presets:");
                ImGui.SameLine();
                if (ImGui.SmallButton("Anemos (20)")) { config.PlayerElementalLevel = 20; config.Save(); }
                ImGui.SameLine();
                if (ImGui.SmallButton("Pagos (35)")) { config.PlayerElementalLevel = 35; config.Save(); }
                ImGui.SameLine();
                if (ImGui.SmallButton("Pyros (50)")) { config.PlayerElementalLevel = 50; config.Save(); }
                ImGui.SameLine();
                if (ImGui.SmallButton("Hydatos (60)")) { config.PlayerElementalLevel = 60; config.Save(); }
            }

            int safeDiff = config.SafeLevelDifference;
            if (ImGui.SliderInt("Safe level difference threshold", ref safeDiff, 1, 5, "%d levels below"))
            {
                config.SafeLevelDifference = safeDiff;
                config.Save();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("In FFXIV Eureka, standard sight/proximity monsters 2 or more levels below you will never aggro. (Default: 2)");
            }

            bool keepDragons = config.AlwaysShowDragons;
            if (ImGui.Checkbox("Always show Sleeping Dragons (regardless of level)", ref keepDragons))
            {
                config.AlwaysShowDragons = keepDragons;
                config.Save();
            }

            bool keepUndead = config.AlwaysShowUndead;
            if (ImGui.Checkbox("Always show Undead / Ashkin (Blood aggro)", ref keepUndead))
            {
                config.AlwaysShowUndead = keepUndead;
                config.Save();
            }

            bool keepSprites = config.AlwaysShowSprites;
            if (ImGui.Checkbox("Always show Sprites / Elementals (Magic aggro)", ref keepSprites))
            {
                config.AlwaysShowSprites = keepSprites;
                config.Save();
            }
        }

        ImGui.Separator();
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Eureka Aggro Mechanics & Distances:");

        bool sound = config.ShowSoundCircles;
        if (ImGui.Checkbox("Sound Aggro (Dragons, Crabs, Amphibians - Walk to avoid)", ref sound))
        {
            config.ShowSoundCircles = sound;
            config.Save();
        }

        if (config.ShowSoundCircles)
        {
            float soundDist = config.DragonRunAggroDistance;
            if (ImGui.SliderFloat("Sleeping Dragon sound aggro radius", ref soundDist, 8.0f, 15.0f, "%.1f m"))
            {
                config.DragonRunAggroDistance = soundDist;
                config.Save();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Sleeping Dragons have a running sound aggro radius of ~10.5m. Walking is 100% safe.");
            }
        }

        bool autoWalk = config.AutoWalkNearDragons;
        if (ImGui.Checkbox("Auto-Walk Safety Trigger for Sleeping Dragons", ref autoWalk))
        {
            config.AutoWalkNearDragons = autoWalk;
            config.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Automatically toggles Walk mode when within proximity of lethal Sleeping Dragons and restores Run mode once safely past.");
        }

        if (config.AutoWalkNearDragons)
        {
            float autoDist = config.AutoWalkDistance;
            if (ImGui.SliderFloat("Auto-Walk trigger distance", ref autoDist, 10.5f, 25.0f, "%.1f m"))
            {
                config.AutoWalkDistance = autoDist;
                config.Save();
            }

            bool chatAlert = config.LogAutoWalkToChat;
            if (ImGui.Checkbox("Log Auto-Walk activation in chat", ref chatAlert))
            {
                config.LogAutoWalkToChat = chatAlert;
                config.Save();
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Test Walk Mode Toggle"))
            {
                dragonWalkService.ManualTestToggle();
            }
        }

        bool prox = config.ShowProximityCircles;
        if (ImGui.Checkbox("Proximity Aggro Circles (360°)", ref prox))
        {
            config.ShowProximityCircles = prox;
            config.Save();
        }

        bool sight = config.ShowVisionCones;
        if (ImGui.Checkbox("Sight Aggro Cones (Frontal vision)", ref sight))
        {
            config.ShowVisionCones = sight;
            config.Save();
        }

        if (config.ShowVisionCones)
        {
            float coneAngle = config.SightAngleDegrees;
            if (ImGui.SliderFloat("Sight cone arc angle", ref coneAngle, 45.0f, 140.0f, "%.0f deg"))
            {
                config.SightAngleDegrees = coneAngle;
                config.Save();
            }
        }

        bool blood = config.ShowBloodCircles;
        if (ImGui.Checkbox("Blood Aggro Circles (Undead / Ashkin - Low HP < 80%)", ref blood))
        {
            config.ShowBloodCircles = blood;
            config.Save();
        }

        if (config.ShowBloodCircles)
        {
            float bloodDist = config.BloodAggroDistance;
            if (ImGui.SliderFloat("Blood detection radius", ref bloodDist, 15.0f, 35.0f, "%.1f m"))
            {
                config.BloodAggroDistance = bloodDist;
                config.Save();
            }
        }

        bool magic = config.ShowMagicWarnings;
        if (ImGui.Checkbox("Magic Aggro Warnings (Sprites / Elementals - Spellcasting)", ref magic))
        {
            config.ShowMagicWarnings = magic;
            config.Save();
        }

        if (config.ShowMagicWarnings)
        {
            float magicDist = config.MagicAggroDistance;
            if (ImGui.SliderFloat("Magic detection radius", ref magicDist, 10.0f, 25.0f, "%.1f m"))
            {
                config.MagicAggroDistance = magicDist;
                config.Save();
            }
        }

        float baseDist = config.DefaultAggroDistance;
        if (ImGui.SliderFloat("Base monster aggro radius", ref baseDist, 8.0f, 15.0f, "%.1f m"))
        {
            config.DefaultAggroDistance = baseDist;
            config.Save();
        }

        bool distLines = config.ShowDistanceLines;
        if (ImGui.Checkbox("Draw distance lines connecting to nearby threats", ref distLines))
        {
            config.ShowDistanceLines = distLines;
            config.Save();
        }

        bool mobLabels = config.ShowMobLabels;
        if (ImGui.Checkbox("Draw floating nametag labels above monsters", ref mobLabels))
        {
            config.ShowMobLabels = mobLabels;
            config.Save();
        }

        bool mutationStatus = config.ShowMutationStatus;
        if (ImGui.Checkbox("Show real-time mutation indicator (Weather & ET)", ref mutationStatus))
        {
            config.ShowMutationStatus = mutationStatus;
            config.Save();
        }

        ImGui.Separator();
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Enemy Action Alerts (HUD Cast Monitor):");

        bool enableAlerts = config.ShowCastAlerts;
        if (ImGui.Checkbox("Show tactical cast alert window when nearby enemies cast", ref enableAlerts))
        {
            config.ShowCastAlerts = enableAlerts;
            config.Save();
        }

        if (config.ShowCastAlerts)
        {
            bool lockAlerts = config.LockCastAlertPosition;
            if (ImGui.Checkbox("Lock cast alert window position (uncheck to drag)", ref lockAlerts))
            {
                config.LockCastAlertPosition = lockAlerts;
                config.Save();
            }

            ImGui.SameLine();
            bool isPreview = castAlertWindow.IsPreviewMode;
            if (ImGui.Checkbox("Preview Alert Window", ref isPreview))
            {
                castAlertWindow.IsPreviewMode = isPreview;
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Reset Position"))
            {
                castAlertWindow.ResetPosition();
            }
        }

        ImGui.Separator();
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Visual Colors & Customization:");

        var colDragon = config.ColorDragonSound;
        if (ImGui.ColorEdit4("Sound Aggro (Dragons)", ref colDragon, ImGuiColorEditFlags.AlphaBar))
        {
            config.ColorDragonSound = colDragon;
            config.Save();
        }

        var colDanger = config.ColorDanger;
        if (ImGui.ColorEdit4("Danger Cone / Circle", ref colDanger, ImGuiColorEditFlags.AlphaBar))
        {
            config.ColorDanger = colDanger;
            config.Save();
        }

        var colBlood = config.ColorBlood;
        if (ImGui.ColorEdit4("Blood Aggro (Undead)", ref colBlood, ImGuiColorEditFlags.AlphaBar))
        {
            config.ColorBlood = colBlood;
            config.Save();
        }

        var colMagic = config.ColorMagic;
        if (ImGui.ColorEdit4("Magic Aggro (Sprites)", ref colMagic, ImGuiColorEditFlags.AlphaBar))
        {
            config.ColorMagic = colMagic;
            config.Save();
        }

        var colEasy = config.ColorEasy;
        if (ImGui.ColorEdit4("Safe Level Mob", ref colEasy, ImGuiColorEditFlags.AlphaBar))
        {
            config.ColorEasy = colEasy;
            config.Save();
        }

        if (ImGui.Button("Reset All Colors to Default"))
        {
            config.ResetColorsToDefault();
        }
    }
    #endregion

    #region MODULE 2: BUNNY FATE ENGINE - MAIN VIEW
    private void DrawFateEngineMain(float scale)
    {
        // Primary DhogGPT Dark Dashboard
        StartBunnies.Draw();
    }

    private void DrawFateStatsSection(float scale)
    {
        var buttonHeight = Math.Max(28f * scale, ImGui.GetFrameHeight());

        // Pyros Session Table
        var stats = BFE.Plugin.C?.pyrosSessionStats;
        if (stats != null)
        {
            int totalCoffers = stats.bronzeCoffer + stats.silverCoffer + stats.goldCoffer;

            ImGui.TextColored(GoldAccent, "Pyros Current Session:");
            if (ImGui.BeginTable("##PyrosSessionGrid", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Total Coffers", ImGuiTableColumnFlags.WidthStretch, 0.25f);
                ImGui.TableSetupColumn("Gold Coffers", ImGuiTableColumnFlags.WidthStretch, 0.25f);
                ImGui.TableSetupColumn("Silver Coffers", ImGuiTableColumnFlags.WidthStretch, 0.25f);
                ImGui.TableSetupColumn("Gil Earned", ImGuiTableColumnFlags.WidthStretch, 0.25f);
                ImGui.TableHeadersRow();

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextColored(Vector4.One, totalCoffers.ToString());
                ImGui.TableNextColumn();
                ImGui.TextColored(GoldAccent, stats.goldCoffer.ToString());
                ImGui.TableNextColumn();
                ImGui.TextColored(new Vector4(0.7f, 0.85f, 1f, 1f), stats.silverCoffer.ToString());
                ImGui.TableNextColumn();
                ImGui.TextColored(GoldAccent, $"{stats.gilEarned:N0} gil");

                ImGui.EndTable();
            }

            if (stats.eldthursCounter > 0 || stats.pyrosHairStyleCounter > 0)
            {
                ImGui.Spacing();
                ImGui.Text($"• Eldthurs Horns: {stats.eldthursCounter}  |  • Pyros Hairstyles: {stats.pyrosHairStyleCounter}");
            }
        }

        ImGui.Spacing();

        // Lifetime Overall Table
        var stat = BFE.Plugin.C?.stats;
        if (stat != null)
        {
            ImGui.TextColored(GoldAccent, "Lifetime Overall:");
            var dict = new Dictionary<string, int>
            {
                { "Gil Earned", stat.gilEarned },
                { "Gold Coffers", stat.goldCoffer },
                { "Silver Coffers", stat.silverCoffer },
                { "Bronze Coffers", stat.bronzeCoffer },
                { "Eldthurs Mount", stat.eldthursCounter },
                { "Pyros Hairstyles", stat.pyrosHairStyleCounter },
                { "Copycat Bulb", stat.bulbMinion },
                { "Petrel Mount", stat.petrelCounter }
            };

            if (ImGui.BeginTable("##LifetimeStatsTable", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Item / Metric", ImGuiTableColumnFlags.WidthStretch, 0.6f);
                ImGui.TableSetupColumn("Count", ImGuiTableColumnFlags.WidthStretch, 0.4f);
                ImGui.TableHeadersRow();

                foreach (var (lbl, val) in dict)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(lbl);

                    ImGui.TableNextColumn();
                    ImGui.TextColored(GoldAccent, val.ToString("N0"));
                }
                ImGui.EndTable();
            }
        }

        ImGui.Spacing();

        // Reset Button with Ctrl guard
        var isCtrlHeld = ImGui.GetIO().KeyCtrl;
        using (var _ = ImRaii.PushStyle(ImGuiStyleVar.Alpha, 0.5f, !isCtrlHeld))
        {
            if (ImGui.Button("RESET STATS (HOLD CTRL)", new Vector2(ImGui.GetContentRegionAvail().X, buttonHeight)) && isCtrlHeld)
            {
                if (BFE.Plugin.C != null)
                {
                    BFE.Plugin.C.stats = new();
                    BFE.Plugin.C.pyrosStats = new();
                    BFE.Plugin.C.sessionStats = new();
                    BFE.Plugin.C.pyrosSessionStats = new();
                    BFE.Plugin.C.Save();
                }
            }
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(isCtrlHeld ? "Click to reset your bunny statistics." : "Hold Ctrl to enable the reset button.");
        }
    }
    #endregion

    #region MODULE 2: FATE ENGINE (BUNNIES) - CONFIGURATION VIEW
    private void DrawFateEngineConfiguration()
    {
        if (ImGui.CollapsingHeader("General Automation Settings", ImGuiTreeNodeFlags.DefaultOpen))
        {
            GeneralSettings.Draw();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.CollapsingHeader("AutoRetainer Integration Settings", ImGuiTreeNodeFlags.DefaultOpen))
        {
            AutoReatinerSettings.Draw();
        }
    }
    #endregion

    #region MODULE 3: ABOUT & CREDITS
    private void DrawAboutSection(float scale)
    {
        ImGui.Spacing();
        ImGui.TextColored(GoldAccent, "Eureka Aggro Suite - Project Attribution & Credits");
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.PushTextWrapPos(0);
        ImGui.TextUnformatted("Eureka Aggro combines specialized expedition threat radar detection with automated Fate and bunny treasure hunting under a unified, high-performance architecture.");
        ImGui.PopTextWrapPos();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Core Authors & Contributors:");
        ImGui.Spacing();

        // 1. Joshua-XIV
        ImGui.Bullet();
        ImGui.TextColored(GoldAccent, "Joshua-XIV");
        ImGui.SameLine();
        ImGui.TextColored(TextMuted, "- Original Author & Pioneer");
        ImGui.PushTextWrapPos(0);
        ImGui.TextUnformatted("Creator and author of the original Bunnies automation engine, developing the core state machine for Eureka bunny fate farming.");
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        // 2. DhogGPT / McVaxius
        ImGui.Bullet();
        ImGui.TextColored(GoldAccent, "DhogGPT / McVaxius");
        ImGui.SameLine();
        ImGui.TextColored(TextMuted, "- Architecture & Experience");
        ImGui.PushTextWrapPos(0);
        ImGui.TextUnformatted("Creator of BFE, modern navigation routing architecture, 15-language localization system, and premium dashboard UI design.");
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        // 3. KangasZ
        ImGui.Bullet();
        ImGui.TextColored(GoldAccent, "KangasZ");
        ImGui.SameLine();
        ImGui.TextColored(TextMuted, "- EurekaHelper & Tracker Foundations");
        ImGui.PushTextWrapPos(0);
        ImGui.TextUnformatted("Creator of EurekaHelper, developing the in-game Eureka Tracker Phoenix client architecture and comprehensive NM spawn definitions.");
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        // 4. Kyuuso
        ImGui.Bullet();
        ImGui.TextColored(GoldAccent, "Kyuuso");
        ImGui.SameLine();
        ImGui.TextColored(TextMuted, "- EurekaAggro & Unified Takeover");
        ImGui.PushTextWrapPos(0);
        ImGui.TextUnformatted("Creator of EurekaAggro, 3D threat radar visualizer, Sleeping Dragon proximity auto-walk, tactical cast monitor, and unified takeover suite integration.");
        ImGui.PopTextWrapPos();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(GoldAccent, "Community & External Links:");
        ImGui.Spacing();

        var availWidth = ImGui.GetContentRegionAvail().X;
        var btnWidth = (availWidth - (20f * scale)) / 3f;
        var btnHeight = 34f * scale;

        if (ImGui.Button($"Support McVaxius on Ko-fi###KofiBtn", new Vector2(btnWidth, btnHeight)))
        {
            Process.Start(new ProcessStartInfo { FileName = "https://ko-fi.com/mcvaxius", UseShellExecute = true });
        }
        ImGui.SameLine(0, 10f * scale);
        if (ImGui.Button($"Join Aethertek Discord###DiscordBtn", new Vector2(btnWidth, btnHeight)))
        {
            Process.Start(new ProcessStartInfo { FileName = "https://discord.gg/invite/aethertek", UseShellExecute = true });
        }
        ImGui.SameLine(0, 10f * scale);
        if (ImGui.Button($"EurekaAggro GitHub Source###GithubBtn", new Vector2(btnWidth, btnHeight)))
        {
            Process.Start(new ProcessStartInfo { FileName = "https://github.com/Kyuuso/eurekaAggro", UseShellExecute = true });
        }
    }
    #endregion
}
