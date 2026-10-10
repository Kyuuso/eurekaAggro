using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using BFE.Scheduler;
using BFE.Windows;

namespace BFE.Ui.MainWindow;

/// <summary>
/// Ventana principal de automatización de conejos de Eureka (BFE).
/// Recrea la estética de alto impacto con cabecera estilizada,
/// pestañas elegantes y presentación fiel al diseño original.
/// </summary>
internal class MainWindow : PositionedWindow
{
    private static readonly Vector4 ColorDoradoAcento = new(0.961f, 0.729f, 0.259f, 1f);      // #F5BA42
    private static readonly Vector4 ColorFondoVentana = new(0.075f, 0.078f, 0.090f, 1f);      // #131417
    private static readonly Vector4 ColorTextoSecundario = new(0.608f, 0.620f, 0.663f, 1f);

    public MainWindow() : base($"{PluginInfo.DisplayName} ###BFEMainWindow")
    {
        SizeConstraints = new()
        {
            MinimumSize = new(720, 560),
            MaximumSize = new(1800, 1400)
        };
        Size = new(840, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
        Flags |= ImGuiWindowFlags.HorizontalScrollbar;

        TitleBarButtons.Add(new()
        {
            Click = (m) => { if (m == ImGuiMouseButton.Left) P.settingsWindow.IsOpen = !P.settingsWindow.IsOpen; },
            Icon = FontAwesomeIcon.Cog,
            IconOffset = new(2, 2),
            ShowTooltip = () => ImGui.SetTooltip(UiText.T("Open settings window"))
        });

        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.Play,
            Priority = -10,
            IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left) StartBunnies.RunActionFromUi(); },
            ShowTooltip = () => ImGui.SetTooltip(StartBunnies.ActionTitleTooltip),
        });

        P.windowSystem.AddWindow(this);
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        if (TitleBarButtons.Count > 1)
        {
            TitleBarButtons[1].Icon = StartBunnies.IsRunning ? FontAwesomeIcon.Stop : FontAwesomeIcon.Play;
        }
        base.PreDraw();
    }

    public override void Draw()
    {
        var scale = ImGuiHelpers.GlobalScale;

        // 1. Cabecera principal (Header con marca BFE y controles)
        DrawHeader(scale);

        ImGui.Spacing();

        // 2. Indicador de estado (State: Idle / Running)
        DrawStateIndicator(scale);

        ImGui.Spacing();

        // 3. Barra de navegación por pestañas
        DrawTabs(scale);

        FinalizePendingWindowPlacement();
    }

    private void DrawHeader(float scale)
    {
        var pos = ImGui.GetCursorScreenPos();
        var ancho = ImGui.GetContentRegionAvail().X;
        var dl = ImGui.GetWindowDrawList();

        // Título BFE y subtítulo Bunny Fate Engine
        var posTexto = pos + new Vector2(10f * scale, 0);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 1.5f, posTexto, ImGui.ColorConvertFloat4ToU32(ColorDoradoAcento), "BFE");
        dl.AddText(posTexto + new Vector2(0, 22f * scale), 0xFFCCCCCC, "Bunny Fate Engine");

        // Botones de acción rápida a la derecha
        ImGui.SetCursorScreenPos(pos + new Vector2(ancho - 370f * scale, 8f * scale));

        if (ImGui.SmallButton("Settings"))
        {
            P.settingsWindow.IsOpen = !P.settingsWindow.IsOpen;
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Ko-fi"))
        {
            Process.Start(new ProcessStartInfo { FileName = PluginInfo.SupportUrl, UseShellExecute = true });
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Discord"))
        {
            Process.Start(new ProcessStartInfo { FileName = PluginInfo.DiscordUrl, UseShellExecute = true });
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("OG Author"))
        {
            Process.Start(new ProcessStartInfo { FileName = PluginInfo.OriginalAuthorUrl, UseShellExecute = true });
        }

        ImGui.SameLine();
        P.appearance.DrawSelector(true);

        ImGui.SetCursorScreenPos(pos + new Vector2(0, 48f * scale));
    }

    private void DrawStateIndicator(float scale)
    {
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();

        var isRunning = SchedulerMain.DoWeTick;
        var colorCirculo = isRunning ? new Vector4(0.239f, 0.839f, 0.467f, 1f) : new Vector4(0.42f, 0.43f, 0.47f, 1f);

        var centroCirculo = pos + new Vector2(6f * scale, 8f * scale);
        dl.AddCircleFilled(centroCirculo, 5f * scale, ImGui.ColorConvertFloat4ToU32(colorCirculo));

        var textoEstado = isRunning ? "Running" : "Idle";
        dl.AddText(pos + new Vector2(18f * scale, 0), 0xFFFFFFFF, $"State:  {textoEstado}");

        ImGui.Dummy(new Vector2(100f * scale, 18f * scale));
    }

    private void DrawTabs(float scale)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(14f * scale, 8f * scale));
        if (ImGui.BeginTabBar("BunniesTabs", ImGuiTabBarFlags.FittingPolicyScroll))
        {
            if (ImGui.BeginTabItem(UiText.T("Start Bunnies")))
            {
                ImGui.Spacing();
                StartBunnies.Draw();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem(UiText.T("Stats")))
            {
                ImGui.Spacing();
                DrawStatsTab(scale);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem(UiText.T("About")))
            {
                ImGui.Spacing();
                About.Draw();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
        ImGui.PopStyleVar();
    }

    private void DrawStatsTab(float scale)
    {
        if (ImGui.BeginTabBar("StatsCategoryTabs"))
        {
            if (ImGui.BeginTabItem(UiText.T("Lifetime")))
            {
                DrawStatsSection(C.stats, out bool reset, C.pyrosStats, C.pagosStats, C.hydatosStats, scale);
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

            if (ImGui.BeginTabItem(UiText.T("Session")))
            {
                DrawStatsSection(C.sessionStats, out bool reset, C.pyrosSessionStats, C.pagosSessionStats, C.hydatosSessionStats, scale);
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

    private void DrawStatsSection(Stats stat, out bool reset, PyrosStats pyrosStat, PagosStats pagosStat, HydatosStats hydatosStat, float scale)
    {
        var buttonHeight = Math.Max(30f * scale, ImGui.GetFrameHeight());
        var availableHeight = Math.Max(1, ImGui.GetContentRegionAvail().Y - buttonHeight - ImGui.GetStyle().ItemSpacing.Y);

        ImGui.BeginChild("StatsRegionScroll", new Vector2(0, availableHeight), true);

        if (ImGui.CollapsingHeader("Lifetime Overall Statistics", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawDictionaryStats(new Dictionary<string, int>
            {
                { "Gil Earned", stat.gilEarned },
                { "Gold Coffers", stat.goldCoffer },
                { "Silver Coffers", stat.silverCoffer },
                { "Bronze Coffers", stat.bronzeCoffer },
                { "Eldthurs Horns", stat.eldthursCounter },
                { "Pyros Hairstyles", stat.pyrosHairStyleCounter },
                { "Copycat Bulb Minion", stat.bulbMinion },
                { "Petrel Mount", stat.petrelCounter }
            });
        }

        if (ImGui.CollapsingHeader("Pyros Statistics", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawDictionaryStats(new Dictionary<string, int>
            {
                { "Gil Earned", pyrosStat.gilEarned },
                { "Gold Coffers", pyrosStat.goldCoffer },
                { "Silver Coffers", pyrosStat.silverCoffer },
                { "Bronze Coffers", pyrosStat.bronzeCoffer },
                { "Eldthurs Horns", pyrosStat.eldthursCounter },
                { "Pyros Hairstyles", pyrosStat.pyrosHairStyleCounter }
            });
        }

        ImGui.EndChild();

        var isCtrlHeld = ImGui.GetIO().KeyCtrl;
        using (var _ = ImRaii.PushStyle(ImGuiStyleVar.Alpha, 0.5f, !isCtrlHeld))
        {
            reset = ImGui.Button("RESET STATS (HOLD CTRL)", new Vector2(ImGui.GetContentRegionAvail().X, buttonHeight)) && isCtrlHeld;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(isCtrlHeld ? "Click to reset statistics." : "Hold Ctrl to enable button.");
        }
    }

    private static void DrawDictionaryStats(Dictionary<string, int> items)
    {
        if (ImGui.BeginTable("##StatsGrid", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Item / Metric", ImGuiTableColumnFlags.WidthStretch, 0.6f);
            ImGui.TableSetupColumn("Count", ImGuiTableColumnFlags.WidthStretch, 0.4f);

            foreach (var (label, val) in items)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(label);

                ImGui.TableNextColumn();
                ImGui.TextColored(ColorDoradoAcento, val.ToString("N0"));
            }

            ImGui.EndTable();
        }
    }
}
