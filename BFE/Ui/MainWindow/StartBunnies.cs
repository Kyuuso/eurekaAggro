using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using ECommons.Logging;
using ECommons.ImGuiMethods;
using BFE.Scheduler;

namespace BFE.Ui.MainWindow;

internal class StartBunnies
{
    public static bool IsRunning = false;

    internal static string ActionLabel => IsRunning ? "Stop" : $"Start {C.zoneSelected switch { 0 => "Pagos", 1 => "Pyros", 2 => "Hydatos", _ => "Normal Raid" }}";
    internal static string ActionTitleTooltip => UiText.T(ActionLabel) + "\n" + UiText.T(
        !IsRunning && !P.pluginDependencies.RequiredDependenciesLoaded
            ? "Load all required plugins to start Bunnies automation."
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
        var s = MaterialTheme.Metrics.Scale;
        float footerHeight;
        using (UiText.Font(UiFontRole.Action)) footerHeight = Math.Max(BfePresentation.FooterHeight * s, ImGui.GetTextLineHeight() + 16 * s) + BfePresentation.Gap * s;
        var parentId = ImGui.GetID("");
        var origin = ImGui.GetCursorScreenPos();
        var window = ImGuiP.GetCurrentWindow();
        var previousContentRight = window.DC.CursorMaxPos.X;
        var horizontalInset = (BfePresentation.Compact ? 3 : -6) * s;
        // Join the retained panel to the native tab edge; native chrome stays visible.
        ImGui.SetCursorScreenPos(origin + new Vector2(horizontalInset, -ImGui.GetStyle().ItemSpacing.Y));
        var contentWidth = Math.Max(1, ImGui.GetContentRegionAvail().X - horizontalInset);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(BfePresentation.Compact ? 24 : 26, BfePresentation.Compact ? 14 : 20) * s);
        if (ImGui.BeginChild("##BunnyContent", new Vector2(contentWidth, Math.Max(1, ImGui.GetContentRegionAvail().Y - footerHeight)), true, ImGuiWindowFlags.HorizontalScrollbar))
        {
            ImGuiP.PushOverrideID(parentId);
            Section("Area Selection", MaterialIcon.MapPin, true);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (BfePresentation.Compact ? 4 : 5) * s);
            DrawAreas();
            Section("Task", MaterialIcon.Document);
            Field(icurrentTask == "idle" ? UiText.T("Idle. Select an area and press Start to begin.") : UiText.T(icurrentTask));
            Section("Time elapsed", MaterialIcon.Clock);
            using (UiText.Font(UiFontRole.Counter)) Field(P.stopwatch.Elapsed.ToString(@"mm\:ss\.fff", UiText.Current.Culture));
            DrawDependencyStatus();
            ImGui.PopID();
        }
        ImGui.EndChild();
        window.DC.CursorMaxPos.X = Math.Max(previousContentRight, Math.Min(window.DC.CursorMaxPos.X, window.WorkRect.Max.X));
        ImGui.PopStyleVar();
        ImGui.SetCursorScreenPos(new Vector2(origin.X + (BfePresentation.Compact ? 9 : 5) * s, ImGui.GetCursorScreenPos().Y + 14 * s));
        using var actionFont = UiText.Font(UiFontRole.Action);
        var ready = P.pluginDependencies.RequiredDependenciesLoaded;
        var original = ActionLabel;
        if (UiGui.FilledAction(original, IsRunning ? MaterialIcon.Stop : MaterialIcon.Play, !ready && !IsRunning))
            RunActionFromUi();
        if (!ready && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) MaterialText.SetTooltip(UiText.T("Load all required plugins to start Bunnies automation."));
    }

    private static void Section(string label, MaterialIcon icon, bool first = false)
    {
        var s = MaterialTheme.Metrics.Scale;
        if (!first) ImGui.Dummy(new Vector2(0, (label is "Task" or "Dependencies" ? BfePresentation.Compact ? 7 : 8 : 4) * s));
        var position = ImGui.GetCursorScreenPos();
        MaterialIcons.Draw(icon, position, 32 * s, MaterialTheme.Current.Colors.OnSurface);
        ImGui.Dummy(new Vector2(32 * s));
        ImGui.SameLine(0, (BfePresentation.Compact ? 14 : 20) * s);
        using var font = UiText.Font(UiFontRole.PluginName);
        UiGui.TextUnformatted(label);
    }

    private static void Field(string value)
    {
        var s = MaterialTheme.Metrics.Scale;
        var position = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var textPadding = (BfePresentation.Compact ? 23 : 28) * s;
        var textWidth = Math.Max(1, width - 2 * textPadding);
        var textHeight = MaterialText.Measure(value, false, textWidth).Y;
        var height = Math.Max((BfePresentation.Compact ? 43 : 54) * s, textHeight + (BfePresentation.Compact ? 12 : 16) * s);
        BfePresentation.Surface(position, position + new Vector2(width, height));
        ImGui.SetCursorScreenPos(position + new Vector2(textPadding, (height - textHeight) * .5f));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + textWidth);
        MaterialText.TextColored(MaterialTheme.Current.Colors.OnSurfaceVariant, value);
        ImGui.PopTextWrapPos();
        ImGui.SetCursorScreenPos(position);
        ImGui.Dummy(new Vector2(width, height));
    }

    private static void DrawAreas()
    {
        var s = MaterialTheme.Metrics.Scale;
        var available = ImGui.GetContentRegionAvail().X;
        float titleHeight;
        float pyrosWidth;
        float badgeWidth;
        using (UiText.Font(UiFontRole.PluginName))
        {
            titleHeight = Math.Max(ImGui.GetTextLineHeight(), MaterialText.Measure(UiText.T("WIP")).Y);
            pyrosWidth = MaterialText.Measure("Pyros").X;
            badgeWidth = MaterialText.Measure(UiText.T("WIP")).X;
        }
        var minimumWidth = Math.Max(220 * s, 112 * s + pyrosWidth + badgeWidth);
        var gap = (BfePresentation.Compact ? 16 : 20) * s;
        var columns = available >= 3 * minimumWidth + 2 * gap ? 3 : 1;
        var width = (available - (columns - 1) * gap) / columns;
        var labels = new[] { "Pagos", "Pyros", "Hydatos" };
        var descriptions = new[] { UiText.T("Not available"), UiText.T("Select to start bunnies"), UiText.T("Not available") };
        var textWidth = Math.Max(1, width - 96 * s);
        var descriptionHeight = descriptions.Max(text => MaterialText.Measure(text, false, textWidth).Y);
        var padding = (BfePresentation.Compact ? 6 : 10) * s;
        var descriptionY = padding + titleHeight + 4 * s;
        var height = Math.Max((BfePresentation.Compact ? 72 : 92) * s, descriptionY + descriptionHeight + padding);
        for (var index = 0; index < 3; index++)
        {
            if (index % columns != 0) ImGui.SameLine(0, gap);
            var position = ImGui.GetCursorScreenPos();
            var colors = MaterialTheme.Current.Colors;
            ImGui.BeginDisabled(index != 1);
            ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.Header, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, Vector4.Zero);
            if (ImGui.Selectable(labels[index], C.zoneSelected == index, ImGuiSelectableFlags.None, new Vector2(width, height))) { C.zoneSelected = (sbyte)index; C.Save(); }
            ImGui.PopStyleColor(4);
            var hovered = ImGui.IsItemHovered();
            var focused = ImGui.IsItemFocused();
            var active = ImGui.IsItemActive();
            ImGui.EndDisabled();
            var ink = index == 1 ? colors.OnSurface : colors.OnSurfaceVariant;
            BfePresentation.Surface(position, position + new Vector2(width, height));
            if (hovered || active)
                MaterialCanvas.Surface(position, position + new Vector2(width, height), MaterialColor.Layer(colors.SurfaceContainerHigh, colors.Primary, active ? .14f : .08f), 4 * s);
            if (C.zoneSelected == index || hovered || (focused && ImGui.GetIO().NavVisible))
                ImGui.GetWindowDrawList().AddRect(position, position + new Vector2(width, height), MaterialCanvas.Color(colors.Primary), 4 * s);
            ImGui.GetWindowDrawList().AddCircle(position + new Vector2(40 * s, height * .5f), 15 * s, MaterialCanvas.Color(index == 1 ? colors.Primary : colors.Outline), 24, 2 * s);
            if (C.zoneSelected == index) ImGui.GetWindowDrawList().AddCircleFilled(position + new Vector2(40 * s, height * .5f), 9 * s, MaterialCanvas.Color(index == 1 ? colors.Primary : colors.Outline), 24);
            var dl = ImGui.GetWindowDrawList();
            dl.PushClipRect(position, position + new Vector2(width, height), true);
            using (UiText.Font(UiFontRole.PluginName))
            {
                MaterialText.AddText(dl, position + new Vector2(80 * s, padding), MaterialCanvas.Color(ink), labels[index]);
                if (index == 1)
                {
                    var badgeMin = position + new Vector2(96 * s + pyrosWidth, padding - 2 * s);
                    var badgeMax = badgeMin + new Vector2(badgeWidth + 16 * s, titleHeight + 4 * s);
                    MaterialCanvas.Surface(badgeMin, badgeMax, colors.Primary, 4 * s);
                    MaterialText.AddText(dl, badgeMin + new Vector2(8 * s, 2 * s), MaterialCanvas.Color(colors.OnPrimary), UiText.T("WIP"));
                }
            }
            MaterialText.AddText(dl, ImGui.GetFont(), ImGui.GetFontSize(), position + new Vector2(80 * s, descriptionY), MaterialCanvas.Color(colors.OnSurfaceVariant), descriptions[index], textWidth);
            dl.PopClipRect();
        }
    }

    private static void DrawDependencyStatus()
    {
        Section("Dependencies", MaterialIcon.Link);
        var s = MaterialTheme.Metrics.Scale;
        using var rowsStyle = new MaterialStyleScope();
        if (!BfePresentation.Compact) rowsStyle.Style(ImGuiStyleVar.CellPadding, new Vector2(ImGui.GetStyle().CellPadding.X, 6 * s));
        using var tightRows = BfePresentation.Compact ? MaterialTable.PushTightRows() : default;
        var headingCenter = (ImGui.GetItemRectMin().Y + ImGui.GetItemRectMax().Y) * .5f;
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        var refreshWidth = UiGui.IconButtonWidth("Refresh");
        if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + refreshWidth <= right)
        {
            ImGui.SameLine(right - refreshWidth - ImGui.GetWindowPos().X);
            ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X,
                headingCenter - Math.Max(ImGui.GetFrameHeight(), BfePresentation.ControlHeight * s) * .5f));
        }
        if (UiGui.IconButton("Refresh##DependencyStatus", MaterialIcon.Refresh)) P.pluginDependencies.Refresh(true);
        var parentId = ImGui.GetID("");
        var statuses = P.pluginDependencies.RequiredStatuses;
        var minimum = statuses.Max(dependency => MaterialText.Measure(dependency.DisplayName).X) + 42 * s
            + statuses.Max(dependency => MaterialText.Measure(UiText.T(dependency.StateText)).X) + UiGui.IconButtonWidth("Get Repo Url ", link: true) + ImGui.GetStyle().CellPadding.X * 6;
        var rowHeight = Math.Max(BfePresentation.ControlHeight * s, ImGui.GetFrameHeight() + ImGui.GetStyle().CellPadding.Y * 2);
        var tableHeight = rowHeight * statuses.Count + ImGui.GetStyle().ScrollbarSize + 2 * s;
        if (ImGui.BeginTable("##Dependencies", 3, ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollX,
            new Vector2(0, tableHeight), Math.Max(ImGui.GetContentRegionAvail().X, minimum)))
        {
            ImGui.TableSetupColumn("Plugin", ImGuiTableColumnFlags.WidthStretch, 1);
            ImGui.TableSetupColumn("State", ImGuiTableColumnFlags.WidthStretch, 1);
            ImGui.TableSetupColumn("Repo", ImGuiTableColumnFlags.WidthFixed, Math.Max(140 * s, UiGui.IconButtonWidth("Get Repo Url ", link: true)));
            foreach (var dependency in statuses)
            {
                ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
                ImGui.TableNextColumn();
                var iconPosition = ImGui.GetCursorScreenPos();
                var statusInk = GetDependencyColor(dependency.State);
                ImGui.GetWindowDrawList().AddCircleFilled(iconPosition + new Vector2(12 * s), 12 * s, MaterialCanvas.Color(statusInk), 24);
                MaterialIcons.Draw(dependency.State == PluginDependencyState.Loaded ? MaterialIcon.Check : MaterialIcon.Close,
                    iconPosition + new Vector2(4 * s), 16 * s, MaterialTheme.Current.Colors.Background);
                ImGui.Dummy(new Vector2(24 * s, Math.Max(24 * s, ImGui.GetTextLineHeight())));
                ImGui.SameLine(0, 10 * s);
                MaterialText.Text(dependency.DisplayName);
                ImGui.TableNextColumn();
                MaterialText.TextColored(GetDependencyColor(dependency.State), UiText.T(dependency.StateText));
                ImGui.TableNextColumn();
                ImGuiP.PushOverrideID(parentId);
                if (UiGui.IconButton($"Get Repo Url ##{dependency.InternalName}", MaterialIcon.ExternalLink, link: true))
                {
                    ImGui.SetClipboardText(dependency.RepoUrl);
                    DuoLog.Information("Repo URL Copied");
                    Notify.Info(P.appearance.Label("Repo URL Copied"));
                }
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
    }

    private static Vector4 GetDependencyColor(PluginDependencyState state) => state switch
    {
        PluginDependencyState.Loaded => ImGuiColors.HealerGreen,
        PluginDependencyState.InstalledNotLoaded => ImGuiColors.DalamudYellow,
        _ => ImGuiColors.DalamudRed,
    };
}
