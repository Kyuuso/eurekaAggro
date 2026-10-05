using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using EurekaAggro.Configuration;
using EurekaAggro.Data;
using EurekaAggro.Models;
using EurekaAggro.Services;

namespace EurekaAggro.UI;

/// <summary>
/// Main settings and management window for Eureka Aggro.
/// </summary>
public class MainWindow
{
    private readonly PluginConfiguration config;
    private readonly MobDatabase mobDatabase;
    private readonly ActionDatabase actionDatabase;
    private readonly EurekaEnvironmentService environmentService;
    private readonly IClientState clientState;
    private readonly DragonWalkService dragonWalkService;
    private readonly CastAlertWindow castAlertWindow;
    private readonly ISharedImmediateTexture? iconTexture;

    public bool IsOpen = false;

    private string mobSearchFilter = string.Empty;
    private string actionSearchFilter = string.Empty;

    public MainWindow(
        PluginConfiguration config,
        MobDatabase mobDatabase,
        ActionDatabase actionDatabase,
        EurekaEnvironmentService environmentService,
        IClientState clientState,
        DragonWalkService dragonWalkService,
        CastAlertWindow castAlertWindow,
        ITextureProvider textureProvider,
        IDalamudPluginInterface pluginInterface)
    {
        this.config = config;
        this.mobDatabase = mobDatabase;
        this.actionDatabase = actionDatabase;
        this.environmentService = environmentService;
        this.clientState = clientState;
        this.dragonWalkService = dragonWalkService;
        this.castAlertWindow = castAlertWindow;

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

    public void Draw()
    {
        if (!IsOpen) return;

        ImGui.SetNextWindowSize(new Vector2(760, 520), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Eureka Aggro Radar", ref IsOpen))
        {
            DrawStatusBar();

            ImGui.Separator();

            if (ImGui.BeginTabBar("##EurekaAggro_Tabs"))
            {
                if (ImGui.BeginTabItem("Configuration"))
                {
                    DrawConfigurationTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Eureka Monsters"))
                {
                    DrawMonstersTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Enemy Actions & Counters"))
                {
                    DrawActionsTab();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }
        ImGui.End();
    }

    private void DrawStatusBar()
    {
        if (iconTexture?.TryGetWrap(out var wrap, out _) == true && wrap != null)
        {
            ImGui.Image(wrap.Handle, new Vector2(34, 34));
            ImGui.SameLine();
        }

        var territoryId = clientState.TerritoryType;
        bool isEureka = ValidZones.IsEureka(territoryId);
        string zoneName = ValidZones.GetZoneName(territoryId);

        ImGui.Text($"Location: {zoneName} (ID: {territoryId})");
        ImGui.SameLine();
        if (isEureka)
        {
            int effectiveLvl = EurekaLevelService.GetEffectiveElementalLevel(territoryId, config.AutoDetectElementalLevel, config.PlayerElementalLevel);
            string mode = config.AutoDetectElementalLevel ? "Auto-synced" : "Manual";
            string weather = environmentService.GetCurrentWeatherName();
            int etHour = EurekaEnvironmentService.GetEorzeaHour();
            string timeStr = $"{etHour:D2}:00 ET ({(EurekaEnvironmentService.IsNight() ? "Night" : "Day")})";

            ImGui.TextColored(new Vector4(0.2f, 1.0f, 0.3f, 1.0f), $"[Active in Eureka | Elemental Lv. {effectiveLvl} ({mode})]");
            ImGui.SameLine();
            ImGui.TextDisabled($"| Weather: {weather} | {timeStr}");
        }
        else
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "[Outside Eureka]");
        }
    }

    private void DrawConfigurationTab()
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
            float dragonDist = config.DragonRunAggroDistance;
            if (ImGui.SliderFloat("Sound running aggro distance", ref dragonDist, 8.0f, 15.0f, "%.1f m"))
            {
                config.DragonRunAggroDistance = dragonDist;
                config.Save();
            }

            float walkSpeed = config.DragonWalkSpeedThreshold;
            if (ImGui.SliderFloat("Walk vs run speed threshold", ref walkSpeed, 1.5f, 4.0f, "%.1f m/s"))
            {
                config.DragonWalkSpeedThreshold = walkSpeed;
                config.Save();
            }

            bool autoWalk = config.AutoWalkNearDragons;
            if (ImGui.Checkbox("Auto-walk near Sleeping Dragons (prevents accidental running aggro)", ref autoWalk))
            {
                config.AutoWalkNearDragons = autoWalk;
                config.Save();
            }

            if (config.AutoWalkNearDragons)
            {
                float autoDist = config.AutoWalkDistance;
                if (ImGui.SliderFloat("Auto-walk activation range", ref autoDist, 10.0f, 25.0f, "%.1f m"))
                {
                    config.AutoWalkDistance = autoDist;
                    config.Save();
                }
                ImGui.TextDisabled("(Auto-adds +4.0m buffer when mounted. Auto-disengages instantly if combat begins.)");

                bool logChat = config.LogAutoWalkToChat;
                if (ImGui.Checkbox("Print Auto-Walk notifications to chat log", ref logChat))
                {
                    config.LogAutoWalkToChat = logChat;
                    config.Save();
                }

                if (ImGui.Button("Test Walk Mode Toggle Now"))
                {
                    dragonWalkService.ManualTestToggle();
                }
                ImGui.SameLine();
                ImGui.TextDisabled("(Toggles Keypad / in-game Walk state & writes to Dalamud log)");
            }
        }

        bool blood = config.ShowBloodCircles;
        if (ImGui.Checkbox("Undead / Ashkin: Blood detection (Flashes red if player HP < 80%)", ref blood))
        {
            config.ShowBloodCircles = blood;
            config.Save();
        }

        if (config.ShowBloodCircles)
        {
            float bloodDist = config.BloodAggroDistance;
            if (ImGui.SliderFloat("Undead blood aggro distance", ref bloodDist, 15.0f, 35.0f, "%.1f m"))
            {
                config.BloodAggroDistance = bloodDist;
                config.Save();
            }
        }

        bool magic = config.ShowMagicWarnings;
        if (ImGui.Checkbox("Sprites / Elementals: Magic aggro (Displays \"DO NOT CAST MAGIC\" warning)", ref magic))
        {
            config.ShowMagicWarnings = magic;
            config.Save();
        }

        if (config.ShowMagicWarnings)
        {
            float magicDist = config.MagicAggroDistance;
            if (ImGui.SliderFloat("Sprite magic detection distance", ref magicDist, 10.0f, 25.0f, "%.1f m"))
            {
                config.MagicAggroDistance = magicDist;
                config.Save();
            }
        }

        bool sight = config.ShowVisionCones;
        if (ImGui.Checkbox("Standard Monsters: Frontal vision cone", ref sight))
        {
            config.ShowVisionCones = sight;
            config.Save();
        }

        bool proxi = config.ShowProximityCircles;
        if (ImGui.Checkbox("Standard Monsters: 360-degree proximity aggro", ref proxi))
        {
            config.ShowProximityCircles = proxi;
            config.Save();
        }

        ImGui.Separator();
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Visual Guides & Overlays:");

        bool lines = config.ShowDistanceLines;
        if (ImGui.Checkbox("Distance guide line (< 10m red, >= 10m green for precision walking)", ref lines))
        {
            config.ShowDistanceLines = lines;
            config.Save();
        }

        bool labels = config.ShowMobLabels;
        if (ImGui.Checkbox("Show floating name, aggro type, and distance labels above monsters", ref labels))
        {
            config.ShowMobLabels = labels;
            config.Save();
        }

        bool mutation = config.ShowMutationStatus;
        if (ImGui.Checkbox("Show Mutation / Adaptation tracker above eligible mobs (weather/time based)", ref mutation))
        {
            config.ShowMutationStatus = mutation;
            config.Save();
        }

        bool fill = config.FillShapes;
        if (ImGui.Checkbox("Fill cones and circles (unchecked = clean hollow outlines)", ref fill))
        {
            config.FillShapes = fill;
            config.Save();
        }

        if (config.FillShapes)
        {
            float opacity = config.FillOpacity;
            if (ImGui.SliderFloat("Fill opacity", ref opacity, 0.05f, 0.8f, "%.2f"))
            {
                config.FillOpacity = opacity;
                config.Save();
            }
        }

        ImGui.Separator();
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Enemy Action & Cast Alerts (HUD):");

        bool alerts = config.ShowCastAlerts;
        if (ImGui.Checkbox("Enable floating HUD alert for dangerous casts (Interrupt, Stun, LOS)", ref alerts))
        {
            config.ShowCastAlerts = alerts;
            config.Save();
        }

        if (config.ShowCastAlerts)
        {
            ImGui.Indent();

            bool targetOnly = config.CastAlertsTargetOnly;
            if (ImGui.Checkbox("Only show cast alerts for current target (ignore background enemies)", ref targetOnly))
            {
                config.CastAlertsTargetOnly = targetOnly;
                config.Save();
            }

            bool lockPos = config.LockCastAlertPosition;
            if (ImGui.Checkbox("Lock alert window position (uncheck to drag anywhere with mouse)", ref lockPos))
            {
                config.LockCastAlertPosition = lockPos;
                config.Save();
            }

            bool preview = castAlertWindow.IsPreviewMode;
            if (ImGui.Checkbox("Preview & drag alert window position now", ref preview))
            {
                castAlertWindow.IsPreviewMode = preview;
            }

            ImGui.SameLine();
            if (ImGui.Button("Reset Window Position"))
            {
                castAlertWindow.ResetPosition();
            }

            bool notifyChat = config.NotifyCastInChat;
            if (ImGui.Checkbox("Print dangerous enemy casts to in-game chat log", ref notifyChat))
            {
                config.NotifyCastInChat = notifyChat;
                config.Save();
            }

            ImGui.Unindent();
        }

        ImGui.Separator();
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Color Palette & Customization:");

        if (ImGui.CollapsingHeader("Aggro Circles & Vision Cones", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var colDragon = config.ColorDragonSound;
            if (ImGui.ColorEdit4("Sleeping Dragons (Sound)##colDragon", ref colDragon))
            {
                config.ColorDragonSound = colDragon;
                config.Save();
            }

            var colBlood = config.ColorBlood;
            if (ImGui.ColorEdit4("Undead / Ashkin (Blood)##colBlood", ref colBlood))
            {
                config.ColorBlood = colBlood;
                config.Save();
            }

            var colMagic = config.ColorMagic;
            if (ImGui.ColorEdit4("Sprites / Elementals (Magic)##colMagic", ref colMagic))
            {
                config.ColorMagic = colMagic;
                config.Save();
            }

            var colDanger = config.ColorDanger;
            if (ImGui.ColorEdit4("High Danger Monsters##colDanger", ref colDanger))
            {
                config.ColorDanger = colDanger;
                config.Save();
            }

            var colCaution = config.ColorCaution;
            if (ImGui.ColorEdit4("Caution Monsters##colCaution", ref colCaution))
            {
                config.ColorCaution = colCaution;
                config.Save();
            }

            var colEasy = config.ColorEasy;
            if (ImGui.ColorEdit4("Easy / Safe Monsters##colEasy", ref colEasy))
            {
                config.ColorEasy = colEasy;
                config.Save();
            }

            var colUnknown = config.ColorUnknown;
            if (ImGui.ColorEdit4("Standard / Other Monsters##colUnknown", ref colUnknown))
            {
                config.ColorUnknown = colUnknown;
                config.Save();
            }
        }

        if (ImGui.CollapsingHeader("Text Labels & Warning Overlays", ImGuiTreeNodeFlags.DefaultOpen))
        {
            bool customTextCol = config.UseCustomMobTextColor;
            if (ImGui.Checkbox("Override floating monster name label color", ref customTextCol))
            {
                config.UseCustomMobTextColor = customTextCol;
                config.Save();
            }

            if (config.UseCustomMobTextColor)
            {
                var colText = config.ColorCustomMobText;
                if (ImGui.ColorEdit4("Custom Monster Label Color##colText", ref colText))
                {
                    config.ColorCustomMobText = colText;
                    config.Save();
                }
            }

            var colSafeText = config.ColorDragonSafeText;
            if (ImGui.ColorEdit4("Dragon Safe / Auto-Walk Engaged Text##colSafeText", ref colSafeText))
            {
                config.ColorDragonSafeText = colSafeText;
                config.Save();
            }

            var colWarnText = config.ColorDragonWarningText;
            if (ImGui.ColorEdit4("Dragon Running Danger Alert Text##colWarnText", ref colWarnText))
            {
                config.ColorDragonWarningText = colWarnText;
                config.Save();
            }

            var colMutActive = config.ColorMutationActiveText;
            if (ImGui.ColorEdit4("Mutation Available Now (Active)##colMutActive", ref colMutActive))
            {
                config.ColorMutationActiveText = colMutActive;
                config.Save();
            }

            var colMutInactive = config.ColorMutationInactiveText;
            if (ImGui.ColorEdit4("Mutation Upcoming (Inactive)##colMutInactive", ref colMutInactive))
            {
                config.ColorMutationInactiveText = colMutInactive;
                config.Save();
            }

            var colLineNear = config.ColorDistanceNear;
            if (ImGui.ColorEdit4("Distance Guide Line (< 10m Danger)##colLineNear", ref colLineNear))
            {
                config.ColorDistanceNear = colLineNear;
                config.Save();
            }

            var colLineFar = config.ColorDistanceFar;
            if (ImGui.ColorEdit4("Distance Guide Line (>= 10m Safe)##colLineFar", ref colLineFar))
            {
                config.ColorDistanceFar = colLineFar;
                config.Save();
            }
        }

        ImGui.Spacing();
        if (ImGui.Button("Reset All Colors to Default", new Vector2(210, 26)))
        {
            config.ResetColorsToDefault();
        }
    }

    private void DrawMonstersTab()
    {
        float saveBtnWidth = 110.0f;
        float searchWidth = Math.Max(150.0f, ImGui.GetContentRegionAvail().X - saveBtnWidth - ImGui.GetStyle().ItemSpacing.X);
        ImGui.SetNextItemWidth(searchWidth);
        ImGui.InputTextWithHint("##SearchMob", "Search monster by name...", ref mobSearchFilter, 64);
        ImGui.SameLine();
        if (ImGui.Button("Save Changes##Mobs", new Vector2(saveBtnWidth, 0)))
        {
            mobDatabase.SaveUserOverrides();
        }

        ImGui.Separator();

        var filteredMobs = mobDatabase.Mobs
            .Where(m => string.IsNullOrEmpty(mobSearchFilter) || m.Value.Name.Contains(mobSearchFilter, StringComparison.OrdinalIgnoreCase))
            .Take(150);

        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable;
        var tableSize = new Vector2(0, ImGui.GetContentRegionAvail().Y);

        if (ImGui.BeginTable("TableMobsEureka", 6, tableFlags, tableSize))
        {
            ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 2.0f);
            ImGui.TableSetupColumn("Danger", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Aggro Type", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableSetupColumn("Distance (m)", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn("Cone Angle (°)", ImGuiTableColumnFlags.WidthFixed, 95);
            ImGui.TableHeadersRow();

            foreach (var (id, mob) in filteredMobs)
            {
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.Text(id.ToString());

                ImGui.TableNextColumn();
                ImGui.Text(mob.Name);

                // Selector de Nivel de Peligro
                ImGui.TableNextColumn();
                int dangerIdx = (int)mob.DangerLevel;
                string[] dangerNames = { "Unknown", "Easy", "Caution", "Danger" };
                ImGui.SetNextItemWidth(-1);
                if (ImGui.Combo($"##Danger{id}", ref dangerIdx, dangerNames, dangerNames.Length))
                {
                    mob.DangerLevel = (DangerLevel)dangerIdx;
                }

                // Selector de Tipo de Aggro de Eureka
                ImGui.TableNextColumn();
                int typeIdx = (int)mob.AggroType;
                string[] typeNames = { "Unknown", "Sight", "Sound", "Proximity", "Blood", "Magic" };
                ImGui.SetNextItemWidth(-1);
                if (ImGui.Combo($"##Type{id}", ref typeIdx, typeNames, typeNames.Length))
                {
                    mob.AggroType = (AggroType)typeIdx;
                }

                // Distancia adicional de aggro
                ImGui.TableNextColumn();
                float dist = mob.AggroDistance;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.DragFloat($"##Dist{id}", ref dist, 0.1f, 1.0f, 35.0f, "%.1f"))
                {
                    mob.AggroDistance = dist;
                }

                // Ángulo de visión en grados
                ImGui.TableNextColumn();
                float degrees = (float)(mob.SightRadian * 180.0f / Math.PI);
                ImGui.SetNextItemWidth(-1);
                if (ImGui.DragFloat($"##Angle{id}", ref degrees, 1.0f, 10.0f, 360.0f, "%.0f°"))
                {
                    mob.SightRadian = (float)(degrees * Math.PI / 180.0f);
                }
            }

            ImGui.EndTable();
        }
    }

    private void DrawActionsTab()
    {
        float saveBtnWidth = 110.0f;
        float searchWidth = Math.Max(150.0f, ImGui.GetContentRegionAvail().X - saveBtnWidth - ImGui.GetStyle().ItemSpacing.X);
        ImGui.SetNextItemWidth(searchWidth);
        ImGui.InputTextWithHint("##SearchAction", "Search action or mob name...", ref actionSearchFilter, 64);
        ImGui.SameLine();
        if (ImGui.Button("Save Changes##Actions", new Vector2(saveBtnWidth, 0)))
        {
            actionDatabase.SaveUserOverrides();
        }

        ImGui.Separator();

        var filteredActions = actionDatabase.Actions
            .Where(a => string.IsNullOrEmpty(actionSearchFilter) ||
                        a.Value.ActionName.Contains(actionSearchFilter, StringComparison.OrdinalIgnoreCase) ||
                        a.Value.MobName.Contains(actionSearchFilter, StringComparison.OrdinalIgnoreCase))
            .Take(150);

        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable;
        var tableSize = new Vector2(0, ImGui.GetContentRegionAvail().Y);

        if (ImGui.BeginTable("TableActionsEureka", 7, tableFlags, tableSize))
        {
            ImGui.TableSetupColumn("Monster", ImGuiTableColumnFlags.WidthStretch, 1.3f);
            ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthStretch, 1.3f);
            ImGui.TableSetupColumn("Stun", ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableSetupColumn("Silence", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("LOS", ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableSetupColumn("Regen", ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableSetupColumn("Alert Message", ImGuiTableColumnFlags.WidthStretch, 2.0f);
            ImGui.TableHeadersRow();

            foreach (var (id, action) in filteredActions)
            {
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.Text(action.MobName);

                ImGui.TableNextColumn();
                ImGui.Text(action.ActionName);

                ImGui.TableNextColumn();
                bool stun = action.RequiresStun;
                if (ImGui.Checkbox($"##Stun{id}", ref stun)) action.RequiresStun = stun;

                ImGui.TableNextColumn();
                bool sil = action.RequiresSilence;
                if (ImGui.Checkbox($"##Sil{id}", ref sil)) action.RequiresSilence = sil;

                ImGui.TableNextColumn();
                bool los = action.RequiresLineOfSight;
                if (ImGui.Checkbox($"##Los{id}", ref los)) action.RequiresLineOfSight = los;

                ImGui.TableNextColumn();
                bool reg = action.RequiresRegen;
                if (ImGui.Checkbox($"##Reg{id}", ref reg)) action.RequiresRegen = reg;

                ImGui.TableNextColumn();
                string msg = action.AlertMessage;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.InputText($"##Msg{id}", ref msg, 128))
                {
                    action.AlertMessage = msg;
                }
            }

            ImGui.EndTable();
        }
    }
}
