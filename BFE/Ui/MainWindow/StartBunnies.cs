using System;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using ECommons.ImGuiMethods;
using ECommons.Logging;
using BFE.Scheduler;

namespace BFE.Ui.MainWindow;

/// <summary>
/// Main view for Eureka bunny automation (Bunny Fate Engine).
/// Renders zone selection cards, task tracking, dependency verification, and execution controls.
/// </summary>
internal class StartBunnies
{
    public static bool IsRunning = false;

    // BFE aesthetic color palette
    private static readonly Vector4 CardBackgroundColor = new(0.106f, 0.110f, 0.133f, 1f);      // #1B1C22
    private static readonly Vector4 FieldBackgroundColor = new(0.086f, 0.090f, 0.110f, 1f);     // #16171C
    private static readonly Vector4 NormalBorderColor = new(0.157f, 0.165f, 0.212f, 1f);        // #282A36
    private static readonly Vector4 GoldAccentColor = new(0.961f, 0.729f, 0.259f, 1f);          // #F5BA42
    private static readonly Vector4 DarkGoldColor = new(0.12f, 0.10f, 0.05f, 1f);
    private static readonly Vector4 AvailableGreenColor = new(0.239f, 0.839f, 0.467f, 1f);      // #3DD677
    private static readonly Vector4 SecondaryTextColor = new(0.608f, 0.620f, 0.663f, 1f);       // #9B9EA9
    private static readonly Vector4 StopRedColor = new(0.851f, 0.255f, 0.255f, 1f);              // #D94141

    internal static string ActionLabel => IsRunning ? "Stop Pyros" : "Start Pyros";

    internal static string ActionTitleTooltip => UiText.T(ActionLabel) + "\n" + UiText.T(
        !IsRunning && !P.pluginDependencies.RequiredDependenciesLoaded
            ? "Load all required plugins to start automation."
            : icurrentTask == "idle" ? "Idle. Select an area and press Start to begin." : icurrentTask);

    internal static void RunActionFromUi()
    {
        if (IsRunning)
        {
            SchedulerMain.DisablePlugin();
            RunCommand("e [Bunnies] Bunnies Stopped.");
        }
        else if (P.pluginDependencies.RequiredDependenciesLoaded)
        {
            ToggleRotationAIOff();
            SchedulerMain.EnablePlugin();
        }
    }

    public static void Draw()
    {
        var scale = ImGuiHelpers.GlobalScale;

        // 1. Area Selection
        ImGui.TextColored(Vector4.One, UiText.T("Area Selection"));
        ImGui.Spacing();

        DrawAreaCards(scale);

        ImGui.Spacing();
        ImGui.Spacing();

        // 2. Current Task
        ImGui.TextColored(Vector4.One, UiText.T("Task"));
        ImGui.Spacing();
        DrawTextField("TaskField", icurrentTask == "idle" ? "Idle. Select an area and press Start to begin." : icurrentTask, scale);

        ImGui.Spacing();
        ImGui.Spacing();

        // 3. Time elapsed
        ImGui.TextColored(Vector4.One, UiText.T("Time elapsed"));
        ImGui.Spacing();
        DrawTextField("TimeField", P.stopwatch.Elapsed.ToString(@"mm\:ss\.fff"), scale);

        ImGui.Spacing();
        ImGui.Spacing();

        // 4. Dependencies
        ImGui.TextColored(Vector4.One, UiText.T("Dependencies"));

        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 85f * scale);
        if (ImGui.SmallButton(UiText.T("Refresh") + "###RefreshDeps"))
        {
            P.pluginDependencies.Refresh(true);
        }

        ImGui.Spacing();
        DrawDependenciesList(scale);

        ImGui.Spacing();
        ImGui.Spacing();

        // 5. Main Action Button (Start Pyros / Stop)
        DrawMainActionButton(scale);
    }

    /// <summary>
    /// Renders the three zone cards (Pagos, Pyros with WIP badge, Hydatos).
    /// </summary>
    private static void DrawAreaCards(float scale)
    {
        var totalWidth = ImGui.GetContentRegionAvail().X;
        var spacing = 12f * scale;
        var cardWidth = (totalWidth - (spacing * 2f)) / 3f;
        var cardHeight = 64f * scale;

        // Card 1: Pagos (Unavailable)
        DrawCard(0, "Pagos", "Not available", false, false, cardWidth, cardHeight, scale);
        ImGui.SameLine(0, spacing);

        // Card 2: Pyros (Active and selected with WIP badge)
        DrawCard(1, "Pyros", "Select to start bunnies", true, true, cardWidth, cardHeight, scale);
        ImGui.SameLine(0, spacing);

        // Card 3: Hydatos (Unavailable)
        DrawCard(2, "Hydatos", "Not available", false, false, cardWidth, cardHeight, scale);
    }

    private static void DrawCard(int index, string name, string description, bool isSelected, bool isWip, float width, float height, float scale)
    {
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();

        // Background and border colors by selection state
        var backgroundColor = isSelected ? CardBackgroundColor : new Vector4(0.09f, 0.095f, 0.11f, 1f);
        var borderColor = isSelected ? GoldAccentColor : NormalBorderColor;

        dl.AddRectFilled(pos, pos + new Vector2(width, height), ImGui.ColorConvertFloat4ToU32(backgroundColor), 6f * scale);
        dl.AddRect(pos, pos + new Vector2(width, height), ImGui.ColorConvertFloat4ToU32(borderColor), 6f * scale, ImDrawFlags.None, isSelected ? 1.5f : 1.0f);

        // Circular radio indicator
        var radioCenter = pos + new Vector2(24f * scale, height * 0.5f);
        var radioRadius = 8f * scale;
        dl.AddCircle(radioCenter, radioRadius, ImGui.ColorConvertFloat4ToU32(isSelected ? GoldAccentColor : new Vector4(0.4f, 0.42f, 0.5f, 1f)), 24, 1.5f);
        if (isSelected)
        {
            dl.AddCircleFilled(radioCenter, 4f * scale, ImGui.ColorConvertFloat4ToU32(GoldAccentColor));
        }

        // Zone title and WIP badge
        var textPos = pos + new Vector2(42f * scale, 14f * scale);
        dl.AddText(textPos, ImGui.ColorConvertFloat4ToU32(isSelected ? Vector4.One : SecondaryTextColor), name);

        if (isWip)
        {
            var nameTextWidth = ImGui.CalcTextSize(name).X;
            var badgeMin = textPos + new Vector2(nameTextWidth + 8f * scale, -1f * scale);
            var badgeMax = badgeMin + new Vector2(34f * scale, 16f * scale);
            dl.AddRectFilled(badgeMin, badgeMax, ImGui.ColorConvertFloat4ToU32(GoldAccentColor), 3f * scale);
            dl.AddText(badgeMin + new Vector2(5f * scale, 1f * scale), 0xFF111111, "WIP");
        }

        // Descriptive subtitle
        var descPos = pos + new Vector2(42f * scale, 34f * scale);
        dl.AddText(descPos, ImGui.ColorConvertFloat4ToU32(SecondaryTextColor), UiText.T(description));

        // Invisible button for click interaction
        ImGui.SetCursorScreenPos(pos);
        if (ImGui.InvisibleButton($"##Card_{name}", new Vector2(width, height)))
        {
            if (index == 1) // Only Pyros is currently supported
            {
                C.zoneSelected = (sbyte)index;
                C.Save();
            }
        }
    }

    /// <summary>
    /// Renders a dark input container with rounded corners.
    /// </summary>
    private static void DrawTextField(string id, string text, float scale)
    {
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = 36f * scale;
        var dl = ImGui.GetWindowDrawList();

        dl.AddRectFilled(pos, pos + new Vector2(width, height), ImGui.ColorConvertFloat4ToU32(FieldBackgroundColor), 6f * scale);
        dl.AddRect(pos, pos + new Vector2(width, height), ImGui.ColorConvertFloat4ToU32(NormalBorderColor), 6f * scale);

        var localizedText = UiText.T(text);
        var textSize = ImGui.CalcTextSize(localizedText);
        var textPos = pos + new Vector2(14f * scale, (height - textSize.Y) * 0.5f);
        dl.AddText(textPos, ImGui.ColorConvertFloat4ToU32(SecondaryTextColor), localizedText);

        ImGui.Dummy(new Vector2(width, height));
    }

    /// <summary>
    /// Renders the dependency checklist in a structured table with fixed column sizing.
    /// </summary>
    private static void DrawDependenciesList(float scale)
    {
        var dependencies = P.pluginDependencies.RequiredStatuses;

        if (ImGui.BeginTable("##DepsTable", 3, ImGuiTableFlags.BordersOuter | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Dependency", ImGuiTableColumnFlags.WidthStretch, 0.50f);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch, 0.25f);
            ImGui.TableSetupColumn("Repository", ImGuiTableColumnFlags.WidthFixed, 110f * scale);
            ImGui.TableHeadersRow();

            foreach (var dep in dependencies)
            {
                ImGui.TableNextRow(ImGuiTableRowFlags.None, 28f * scale);

                // Column 1: Status indicator and dependency name
                ImGui.TableNextColumn();
                var isLoaded = dep.State == PluginDependencyState.Loaded;
                var checkColor = isLoaded ? AvailableGreenColor : (dep.State == PluginDependencyState.InstalledNotLoaded ? ImGuiColors.DalamudYellow : ImGuiColors.DalamudRed);

                var cellPos = ImGui.GetCursorScreenPos();
                var dl = ImGui.GetWindowDrawList();
                var center = cellPos + new Vector2(8f * scale, ImGui.GetTextLineHeight() * 0.5f + 3f * scale);
                dl.AddCircleFilled(center, 5f * scale, ImGui.ColorConvertFloat4ToU32(checkColor));
                if (isLoaded)
                {
                    dl.AddLine(center + new Vector2(-2.5f * scale, 0), center + new Vector2(-0.5f * scale, 2f * scale), 0xFF111111, 1.5f * scale);
                    dl.AddLine(center + new Vector2(-0.5f * scale, 2f * scale), center + new Vector2(3f * scale, -2f * scale), 0xFF111111, 1.5f * scale);
                }

                ImGui.SetCursorScreenPos(cellPos + new Vector2(20f * scale, 2f * scale));
                ImGui.TextUnformatted(dep.DisplayName);

                // Column 2: Status (Available / Missing)
                ImGui.TableNextColumn();
                var statusText = isLoaded ? "Available" : dep.StateText;
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 2f * scale);
                ImGui.TextColored(checkColor, UiText.T(statusText));

                // Column 3: Copy repository URL button
                ImGui.TableNextColumn();
                if (ImGui.SmallButton($"Get Repo Url###Link_{dep.InternalName}"))
                {
                    ImGui.SetClipboardText(dep.RepoUrl);
                    DuoLog.Information("Repo URL copied to clipboard.");
                    Notify.Info(UiText.T("Repo URL Copied"));
                }
            }
            ImGui.EndTable();
        }
    }

    /// <summary>
    /// Renders the large start/stop action button at the bottom of the window.
    /// </summary>
    private static void DrawMainActionButton(float scale)
    {
        var ready = P.pluginDependencies.RequiredDependenciesLoaded;
        var btnColor = IsRunning ? StopRedColor : GoldAccentColor;
        var hoverColor = IsRunning ? new Vector4(0.95f, 0.35f, 0.35f, 1f) : new Vector4(0.98f, 0.78f, 0.32f, 1f);
        var textColor = IsRunning ? Vector4.One : new Vector4(0.07f, 0.07f, 0.07f, 1f);

        ImGui.BeginDisabled(!ready && !IsRunning);
        ImGui.PushStyleColor(ImGuiCol.Button, btnColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoverColor);
        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f * scale);

        var label = ActionLabel;

        if (ImGui.Button(label, new Vector2(ImGui.GetContentRegionAvail().X, 42f * scale)))
        {
            RunActionFromUi();
        }

        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
        ImGui.EndDisabled();
    }
}
