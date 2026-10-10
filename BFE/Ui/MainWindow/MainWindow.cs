using System.Numerics;
using System.Diagnostics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;
using BFE.Scheduler;
using BFE.Windows;
using AethertekUI;
namespace BFE.Ui.MainWindow;

internal class MainWindow : PositionedWindow
{
    public MainWindow() : base($"{PluginInfo.DisplayName} {P.GetType().Assembly.GetName().Version} ###BFEMainWindow")
    {
        SizeConstraints = new()
        {
            MinimumSize = new(720, 520),
            MaximumSize = new(1800, 1400)
        };
        Size = new(1452, 988);
        SizeCondition = ImGuiCond.FirstUseEver;
        Flags |= ImGuiWindowFlags.HorizontalScrollbar;

        TitleBarButtons.Add(new()
        {
            Click = (m) => { if (m == ImGuiMouseButton.Left) P.settingsWindow.IsOpen = !P.settingsWindow.IsOpen; },
            Icon = FontAwesomeIcon.Cog,
            IconOffset = new(2,2),
            ShowTooltip = () => MaterialText.SetTooltip(UiText.T("Open settings window"))
        });
        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.Play, Priority = -10, IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left) StartBunnies.RunActionFromUi(); },
            ShowTooltip = () => MaterialText.SetTooltip(StartBunnies.ActionTitleTooltip),
        });

        P.windowSystem.AddWindow(this);
    }
    public void Dispose() {}
    public override void PreDraw()
    {
        TitleBarButtons[1].Icon = StartBunnies.IsRunning ? FontAwesomeIcon.Stop : FontAwesomeIcon.Play;
        UiGui.ReserveTitleSpace(this, WindowName.Split("##", 2)[0], 720);
        base.PreDraw();
    }
    public override void PostDraw()
    {
        base.PostDraw();
        UiGui.PaintTitleWithImage(this, WindowName.Split("##", 2)[0]);
    }

    private void DrawStatsTab()
    {
        bool statsOpen;
        using (MaterialText.PushLineHeight(UiText.T("Lifetime"), UiText.T("Session")))
            statsOpen = ImGui.BeginTabBar("Stats");
        if (statsOpen)
        {
            if (UiGui.TabItem("Lifetime"))
            {
                this.DrawStatsTab(C.stats, out bool reset, C.pyrosStats, C.pagosStats, C.hydatosStats);
                
                if (reset)
                {
                    C.stats = new();
                    C.pagosStats = new();
                    C.pyrosStats = new();
                    C.hydatosStats = new();

                    C.Save();
                }
                ImGui.EndTabItem();
            }

            if (UiGui.TabItem("Session"))
            {
                this.DrawStatsTab(C.sessionStats, out bool reset, C.pyrosSessionStats, C.pagosSessionStats, C.hydatosSessionStats);

                if (reset)
                {
                    C.sessionStats = new();
                    C.pagosSessionStats = new();
                    C.pyrosSessionStats = new();
                    C.hydatosSessionStats = new();
                }
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }
    private bool showAllStats = true;
    private bool showSessionStats = true;
    private bool showPagosStats = false;
    private bool showSessionPagosStats = false;
    private bool showPyrosStats = false;
    private bool showSessionPyrosStats = false;
    private bool showHydatosStats = false;
    private bool showSessionHydatostats = false;
    private void DrawStatsTab(Stats stat, out bool reset, PyrosStats pyrosStat, PagosStats pagosStat, HydatosStats hydatosStat)
    {
        var buttonHeight = Math.Max(30 * MaterialTheme.Metrics.Scale, ImGui.GetFrameHeight());
        var availableHeight = Math.Max(1, ImGui.GetContentRegionAvail().Y - buttonHeight - ImGui.GetStyle().ItemSpacing.Y);
        var availableWidth = new Vector2(ImGui.GetContentRegionAvail().X, 0);
        string[] texts = { "Pagos Stats", "Pyros Stats", "Hydatos Stats" };
        float[] textSize = { MaterialText.Measure(UiText.T(texts[0])).X, MaterialText.Measure(UiText.T(texts[1])).X, MaterialText.Measure(UiText.T(texts[2])).X };
        float[] textStartX = { (availableWidth[0] - textSize[0]) * 0.5f, (availableWidth[0] - textSize[1]) * .5f, (availableWidth[0] - textSize[2]) * .5f };
        ImGui.BeginChild("StatsRegion", new Vector2(0, availableHeight), true, ImGuiWindowFlags.None);
        DrawMainSelectables("Total Stats", ref showAllStats, availableWidth, .5f * (availableWidth[0] - MaterialText.Measure("Total Stats").X));
        if (showAllStats)
            DrawStats(stat);

        ImGui.Separator();
        DrawMainSelectables(texts[0], ref showPagosStats, availableWidth, textStartX[0]);
        if (showPagosStats)
            DrawPagosStats(pagosStat);

        ImGui.Separator();
        DrawMainSelectables(texts[1], ref showPyrosStats, availableWidth, textStartX[1]);
        if (showPyrosStats)
            DrawPyrosStats(pyrosStat);

        ImGui.Separator();
        DrawMainSelectables(texts[2], ref showHydatosStats, availableWidth, textStartX[2]);
        if (showHydatosStats)
            DrawHydtosStats(hydatosStat);

        if (!showHydatosStats)
            ImGui.Separator();
        ImGui.EndChild();
        bool isCtrlHeld = ImGui.GetIO().KeyCtrl;

        using (var _ = ImRaii.PushStyle(ImGuiStyleVar.Alpha, 0.5f, !ImGui.GetIO().KeyCtrl))
        {
            reset = UiGui.Button("RESET STATS", new Vector2(ImGui.GetContentRegionAvail().X, buttonHeight)) && ImGui.GetIO().KeyCtrl;
        }
        if (ImGui.IsItemHovered()) MaterialText.SetTooltip(UiText.T(isCtrlHeld ? "Press to reset your stats." : "Hold Ctrl to enable the button."));
    }

    private void DrawStats(Stats stat)
    {
        if (!C.hasUpdatedStats)
        {
            C.hasUpdatedStats = true;
        }

        ImGui.Columns(3, "", false);

        // Top Middle
        ImGui.NextColumn();

        UiGui.CenterColumnText(MaterialTheme.Current.Colors.Primary, UiText.T("Bunnies"), true);
        ImGuiHelpers.ScaledDummy(10f);

        // Setting up columns for stats
        ImGui.Columns(2, "", false);

        // Dictionary for Iteration
        var totalStats = new Dictionary<string, int>
        {
            { "Gil Earned", stat.gilEarned },
            { "Gold Coffers", stat.goldCoffer },
            { "Silver Coffers", stat.silverCoffer },
            { "Bronze Coffers", stat.bronzeCoffer },
            { "Eldthurs Mount", stat.eldthursCounter },
            { "Pyros Hairstyles", stat.pyrosHairStyleCounter},
            { "Copycat Bulb", stat.bulbMinion },
            { "Petrel Mount", stat.petrelCounter }
        };

        foreach (var (label, value) in totalStats)
        {
            UiGui.CenterColumnText(UiText.T(label), true);
            UiGui.CenterColumnText(value.ToString("N0", UiText.Current.Culture));
            ImGui.NextColumn();
        }

        ImGui.Columns(1, "", false);
        ImGui.Dummy(new Vector2(0, 20));
    }

    private void DrawPagosStats(PagosStats stats)
    {
        if (!C.hasUpdatedStats)
        {
            C.hasUpdatedStats = true;
        }

        ImGui.Columns(3, "", false);

        // Top Middle
        ImGui.NextColumn();

        UiGui.CenterColumnText(MaterialTheme.Current.Colors.Primary, "Pagos", true);
        ImGuiHelpers.ScaledDummy(10f);

        // Setting up columns for stats
        ImGui.Columns(2, "", false);

        var PagosStats = new Dictionary<string, int>
        {
            { "Gil Earned", stats.gilEarned },
            { "Gold Coffers", stats.goldCoffer },
            { "Silver Coffers", stats.silverCoffer },
            { "Bronze Coffers", stats.bronzeCoffer },
            { "Eldthurs Mount", stats.bulbMinion },
            { "Pyros Hairstyles", stats.hakutakuEye}
        };

        foreach (var (label, value) in PagosStats)
        {
            UiGui.CenterColumnText(UiText.T(label), true);
            UiGui.CenterColumnText(value.ToString("N0", UiText.Current.Culture));
            ImGui.NextColumn();
        }

        ImGui.Columns(1, "", false);
    }

    private void DrawPyrosStats(PyrosStats stats)
    {
        if (!C.hasUpdatedStats)
        {
            C.hasUpdatedStats = true;
        }

        ImGui.Columns(3, "", false);

        // Top Middle
        ImGui.NextColumn();

        UiGui.CenterColumnText(MaterialTheme.Current.Colors.Primary, "Pyros", true);
        ImGuiHelpers.ScaledDummy(10f);

        // Setting up columns for stats
        ImGui.Columns(2, "", false);

        var PyrosStats = new Dictionary<string, int>
        {
            { "Gil Earned", stats.gilEarned },
            { "Gold Coffers", stats.goldCoffer },
            { "Silver Coffers", stats.silverCoffer },
            { "Bronze Coffers", stats.bronzeCoffer },
            { "Eldthurs Mount", stats.eldthursCounter },
            { "Pyros Hairstyles", stats.pyrosHairStyleCounter}
        };

        foreach (var (label, value) in PyrosStats)
        {
            UiGui.CenterColumnText(UiText.T(label), true);
            UiGui.CenterColumnText(value.ToString("N0", UiText.Current.Culture));
            ImGui.NextColumn();
        }

        ImGui.Columns(1, "", false);
    }

    private void DrawHydtosStats(HydatosStats stats)
    {
        if (!C.hasUpdatedStats)
        {
            C.hasUpdatedStats = true;
        }

        ImGui.Columns(3, "", false);

        // Top Middle
        ImGui.NextColumn();

        UiGui.CenterColumnText(MaterialTheme.Current.Colors.Primary, "Hydatos", true);
        ImGuiHelpers.ScaledDummy(10f);

        // Setting up columns for stats
        ImGui.Columns(2, "", false);

        var HydatosStats = new Dictionary<string, int>
        {
            { "Gil Earned", stats.gilEarned },
            { "Gold Coffers", stats.goldCoffer },
            { "Silver Coffers", stats.silverCoffer },
            { "Bronze Coffers", stats.bronzeCoffer },
            { "Petrel Mount", stats.petrelCounter },
        };

        foreach (var (label, value) in HydatosStats)
        {
            UiGui.CenterColumnText(UiText.T(label), true);
            UiGui.CenterColumnText(value.ToString("N0", UiText.Current.Culture));
            ImGui.NextColumn();
        }

        ImGui.Columns(1, "", false);
    }

    public override void Draw()
    {
        WindowMotion.DrawChrome();
        var window = ImGuiP.GetCurrentWindow();
        var previousWorkRect = window.WorkRect;
        var viewportWorkRect = previousWorkRect;
        viewportWorkRect.Max.X = Math.Max(viewportWorkRect.Min.X,
            Math.Min(viewportWorkRect.Max.X, window.Pos.X + ImGui.GetWindowContentRegionMax().X));
        // A prior horizontal extent must not widen the next frame's reflow viewport.
        window.WorkRect = viewportWorkRect;
        try
        {
            DrawHeader();
            ImGui.Separator();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (BfePresentation.Compact ? 10 : 5) * MaterialTheme.Metrics.Scale);
            using var tabFont = UiText.Font(UiFontRole.Action);
            var s = MaterialTheme.Metrics.Scale;
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(16, BfePresentation.Compact ? 12 : 16) * s);
            bool tabsOpen;
            using (MaterialText.PushLineHeight(UiText.T("Start Bunnies"), UiText.T("Stats"), UiText.T("About")))
                tabsOpen = ImGui.BeginTabBar("Bunnies Bar", ImGuiTabBarFlags.FittingPolicyScroll);
            ImGui.PopStyleVar();
            if (tabsOpen)
            {
                var width = ImGui.GetContentRegionAvail().X / s;
                if (UiGui.TabItem("Start Bunnies", MaterialIcon.Play, width * .325f))
                {
                    using (UiText.Font(UiFontRole.Body)) StartBunnies.Draw();
                    ImGui.EndTabItem();
                }
                if (UiGui.TabItem("Stats", MaterialIcon.Chart, width * .16f))
                {
                    using (UiText.Font(UiFontRole.Body)) DrawStatsTab();
                    ImGui.EndTabItem();
                }
                if (UiGui.TabItem("About", MaterialIcon.Info, width * .17f))
                {
                    using (UiText.Font(UiFontRole.Body)) About.Draw();
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
        }
        finally { window.WorkRect = previousWorkRect; }
        FinalizePendingWindowPlacement();
    }

    private void DrawHeader()
    {
        var s = MaterialTheme.Metrics.Scale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var compact = BfePresentation.Compact;
        BfePresentation.Rabbit(origin + new Vector2(compact ? 9 : 5, compact ? 5 : 6) * s, (compact ? 77 : 90) * s);
        var titleX = (compact ? 102 : 99) * s;
        ImGui.SetCursorScreenPos(origin + new Vector2(titleX, (compact ? -5 : -8) * s));
        using (UiText.Font(compact ? UiFontRole.CompactTitle : UiFontRole.Title)) UiGui.TextColored(MaterialTheme.Current.Colors.Primary, "BFE");
        var titleRight = ImGui.GetItemRectMax().X;
        ImGui.SetCursorScreenPos(origin + new Vector2(titleX, (compact ? 41 : 56) * s));
        UiGui.TextUnformatted("Bunny Fate Engine");
        var selectorWidth = C.UiLanguageVisibleOnMainWindow ? P.appearance.SelectorWidth : 0;
        var compactWidth = C.UiCompactVisibleOnMainWindow
            ? ImGui.GetTextLineHeight() + ImGui.GetStyle().ItemInnerSpacing.X + MaterialText.Measure("C").X + ImGui.GetStyle().ItemSpacing.X : 0;
        var opacityWidth = ImGui.GetTextLineHeight() + ImGui.GetStyle().ItemInnerSpacing.X + MaterialText.Measure(UiText.T("Transparency")).X;
        var controlsWidth = new[] { "Settings", "Ko-fi", "Discord", "OG Author" }.Sum(label => UiGui.IconButtonWidth(label, header: true))
            + compactWidth + opacityWidth + selectorWidth + ImGui.GetStyle().ItemSpacing.X * (C.UiLanguageVisibleOnMainWindow ? 5 : 4);
        var headingRight = Math.Max(titleRight, ImGui.GetItemRectMax().X);
        var wrapped = headingRight + 20 * s + controlsWidth > origin.X + width;
        ImGui.SetCursorScreenPos(origin + new Vector2(wrapped ? 0 : width - controlsWidth, (wrapped ? BfePresentation.HeaderHeight : compact ? 23 : 30) * s));
        var controlsOrigin = ImGui.GetCursorScreenPos();
        var controlsHeight = (compact ? 44 : 52) * s;
        if (C.UiCompactVisibleOnMainWindow)
        {
            var compactPreference = C.UiCompact;
            ImGui.BeginGroup();
            ImGui.SetCursorScreenPos(controlsOrigin + new Vector2(0, (controlsHeight - ImGui.GetTextLineHeight()) * .5f));
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(ImGui.GetStyle().FramePadding.X, 0));
            if (UiGui.Checkbox("C##CompactMode", ref compactPreference)) { C.UiCompact = compactPreference; C.Save(); }
            ImGui.PopStyleVar();
            if (ImGui.IsItemHovered()) MaterialText.SetTooltip(UiText.T("Compact mode"));
            var measuredCompactWidth = ImGui.GetItemRectSize().X;
            ImGui.SetCursorScreenPos(controlsOrigin);
            ImGui.Dummy(new Vector2(measuredCompactWidth, controlsHeight));
            ImGui.EndGroup();
            if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + opacityWidth <= origin.X + width) ImGui.SameLine();
        }
        var opacityOrigin = ImGui.GetCursorScreenPos();
        ImGui.BeginGroup();
        ImGui.SetCursorScreenPos(opacityOrigin + new Vector2(0, (controlsHeight - ImGui.GetTextLineHeight()) * .5f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(ImGui.GetStyle().FramePadding.X, 0));
        P.appearance.DrawTransparencyToggle();
        ImGui.PopStyleVar();
        var measuredOpacityWidth = ImGui.GetItemRectSize().X;
        ImGui.SetCursorScreenPos(opacityOrigin);
        ImGui.Dummy(new Vector2(measuredOpacityWidth, controlsHeight));
        ImGui.EndGroup();
        UiGui.SameLineIconIfFits("Settings", header: true);
        if (UiGui.IconButton("Settings", MaterialIcon.Settings, true))
            P.settingsWindow.IsOpen = !P.settingsWindow.IsOpen;
        UiGui.SameLineIconIfFits("Ko-fi", header: true);
        if (UiGui.IconButton("Ko-fi", MaterialIcon.HeartOutline, true))
            Process.Start(new ProcessStartInfo { FileName = PluginInfo.SupportUrl, UseShellExecute = true });
        UiGui.SameLineIconIfFits("Discord", header: true);
        if (UiGui.IconButton("Discord", MaterialIcon.None, true))
            Process.Start(new ProcessStartInfo { FileName = PluginInfo.DiscordUrl, UseShellExecute = true });
        if (ImGui.IsItemHovered())
            MaterialText.SetTooltip(UiText.T(PluginInfo.DiscordFeedbackNote));
        UiGui.SameLineIconIfFits("OG Author", header: true);
        if (UiGui.IconButton("OG Author", MaterialIcon.Person, true))
            Process.Start(new ProcessStartInfo { FileName = PluginInfo.OriginalAuthorUrl, UseShellExecute = true });
        if (C.UiLanguageVisibleOnMainWindow)
        {
            if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + selectorWidth <= origin.X + width) ImGui.SameLine();
            P.appearance.DrawSelector(false);
        }
        var controlsBottom = ImGui.GetItemRectMax().Y;
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, Math.Max(BfePresentation.HeaderHeight * s, controlsBottom - origin.Y + 10 * s)));
        ImGui.Separator();
        var statePosition = ImGui.GetCursorScreenPos();
        var stateHeight = (compact ? 36 : 37) * s;
        var stateFontSize = (compact ? 27 : 31) * s;
        var parts = UiText.T("State: {0}").Split("{0}", 2);
        var state = UiText.T(SchedulerMain.DoWeTick ? "Running" : "Idle");
        var prefixHeight = stateFontSize;
        var emphasizedSize = 31 * s;
        var emphasizedHeight = emphasizedSize;
        if (MaterialText.RequiresShaping(parts[0])) prefixHeight = MaterialText.Measure(parts[0]).Y * stateFontSize / ImGui.GetFontSize();
        using (UiText.Font(UiFontRole.BodyStrong))
            if (MaterialText.RequiresShaping(state)) emphasizedHeight = MaterialText.Measure(state).Y * emphasizedSize / ImGui.GetFontSize();
        if (MaterialText.RequiresShaping(parts[0]) || MaterialText.RequiresShaping(state))
            stateHeight = Math.Max(stateHeight, MathF.Ceiling(Math.Max(prefixHeight, emphasizedHeight)));
        var stateOrigin = statePosition + new Vector2(compact ? 14 : 10, 0) * s;
        var drawList = ImGui.GetWindowDrawList();
        var colors = MaterialTheme.Current.Colors;
        drawList.AddCircleFilled(stateOrigin + new Vector2(12 * s, stateHeight * .5f), 12 * s,
            MaterialCanvas.Color(SchedulerMain.DoWeTick ? ImGuiColors.HealerGreen : colors.OnSurfaceVariant), 24);
        var textPosition = stateOrigin + new Vector2((compact ? 48 : 50) * s, (stateHeight - prefixHeight) * .5f);
        MaterialText.AddText(drawList, ImGui.GetFont(), stateFontSize, textPosition, MaterialCanvas.Color(colors.OnSurface), parts[0]);
        textPosition.X += MaterialText.Measure(parts[0]).X * stateFontSize / ImGui.GetFontSize() + (compact ? 21 : 12) * s;
        using (UiText.Font(UiFontRole.BodyStrong))
        {
            var emphasizedPosition = new Vector2(textPosition.X, stateOrigin.Y + (stateHeight - emphasizedHeight) * .5f);
            MaterialText.AddText(drawList, ImGui.GetFont(), emphasizedSize, emphasizedPosition,
                MaterialCanvas.Color(SchedulerMain.DoWeTick ? ImGuiColors.HealerGreen : colors.OnSurface), state);
            textPosition.X += MaterialText.Measure(state).X * emphasizedSize / ImGui.GetFontSize();
        }
        MaterialText.AddText(drawList, ImGui.GetFont(), stateFontSize, textPosition, MaterialCanvas.Color(colors.OnSurface), parts[1]);
        ImGui.Dummy(new Vector2(width, stateHeight));
    }
}
