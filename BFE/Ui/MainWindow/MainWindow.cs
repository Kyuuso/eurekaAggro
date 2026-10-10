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
/// Permite el control del ciclo de conejos, visualización de estadísticas y créditos de autoría.
/// </summary>
internal class MainWindow : PositionedWindow
{
    private bool showAllStats = true;
    private bool showPagosStats = false;
    private bool showPyrosStats = false;
    private bool showHydatosStats = false;

    public MainWindow() : base($"{PluginInfo.DisplayName} ###BFEMainWindow")
    {
        SizeConstraints = new()
        {
            MinimumSize = new(680, 500),
            MaximumSize = new(1800, 1400)
        };
        Size = new(800, 600);
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
        DrawHeader();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.BeginTabBar("BunniesBarPrincipal", ImGuiTabBarFlags.FittingPolicyScroll))
        {
            if (ImGui.BeginTabItem($"{FontAwesomeIcon.Play.ToIconString()}  {UiText.T("Start Bunnies")}"))
            {
                ImGui.Spacing();
                StartBunnies.Draw();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem($"{FontAwesomeIcon.ChartBar.ToIconString()}  {UiText.T("Stats")}"))
            {
                ImGui.Spacing();
                DrawStatsTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem($"{FontAwesomeIcon.InfoCircle.ToIconString()}  {UiText.T("About")}"))
            {
                ImGui.Spacing();
                About.Draw();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        FinalizePendingWindowPlacement();
    }

    private void DrawHeader()
    {
        // Título del subsistema y estado de ejecución
        ImGui.TextColored(ImGuiColors.ParsedGold, $"{PluginInfo.DisplayName} - Bunny Fate Engine");
        ImGui.SameLine();

        var isRunning = SchedulerMain.DoWeTick;
        var statusColor = isRunning ? ImGuiColors.HealerGreen : ImGuiColors.DalamudGrey;
        var statusText = isRunning ? "En Ejecución" : "En Reposo";
        ImGui.TextColored(statusColor, $"[{statusText}]");

        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 220f * ImGuiHelpers.GlobalScale);
        if (ImGui.SmallButton($"{FontAwesomeIcon.Cog.ToIconString()} Ajustes"))
        {
            P.settingsWindow.IsOpen = !P.settingsWindow.IsOpen;
        }

        ImGui.SameLine();
        if (ImGui.SmallButton($"{FontAwesomeIcon.Heart.ToIconString()} Ko-fi"))
        {
            Process.Start(new ProcessStartInfo { FileName = PluginInfo.SupportUrl, UseShellExecute = true });
        }

        ImGui.SameLine();
        if (ImGui.SmallButton($"{FontAwesomeIcon.User.ToIconString()} Autor"))
        {
            Process.Start(new ProcessStartInfo { FileName = PluginInfo.OriginalAuthorUrl, UseShellExecute = true });
        }
    }

    private void DrawStatsTab()
    {
        if (ImGui.BeginTabBar("StatsTabs"))
        {
            if (ImGui.BeginTabItem(UiText.T("Lifetime")))
            {
                DrawStatsSection(C.stats, out bool reset, C.pyrosStats, C.pagosStats, C.hydatosStats);
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
                DrawStatsSection(C.sessionStats, out bool reset, C.pyrosSessionStats, C.pagosSessionStats, C.hydatosSessionStats);
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

    private void DrawStatsSection(Stats stat, out bool reset, PyrosStats pyrosStat, PagosStats pagosStat, HydatosStats hydatosStat)
    {
        var buttonHeight = Math.Max(28f * ImGuiHelpers.GlobalScale, ImGui.GetFrameHeight());
        var availableHeight = Math.Max(1, ImGui.GetContentRegionAvail().Y - buttonHeight - ImGui.GetStyle().ItemSpacing.Y);

        ImGui.BeginChild("StatsRegionScroll", new Vector2(0, availableHeight), true);

        if (ImGui.CollapsingHeader("Estadísticas Totales", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawDictionaryStats(new Dictionary<string, int>
            {
                { "Gil Obtenido", stat.gilEarned },
                { "Cofres de Oro", stat.goldCoffer },
                { "Cofres de Plata", stat.silverCoffer },
                { "Cofres de Bronce", stat.bronzeCoffer },
                { "Montura Eldthurs", stat.eldthursCounter },
                { "Peinados de Pyros", stat.pyrosHairStyleCounter },
                { "Minion Copycat Bulb", stat.bulbMinion },
                { "Montura Petrel", stat.petrelCounter }
            });
        }

        if (ImGui.CollapsingHeader("Estadísticas de Pyros", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawDictionaryStats(new Dictionary<string, int>
            {
                { "Gil Obtenido", pyrosStat.gilEarned },
                { "Cofres de Oro", pyrosStat.goldCoffer },
                { "Cofres de Plata", pyrosStat.silverCoffer },
                { "Cofres de Bronce", pyrosStat.bronzeCoffer },
                { "Montura Eldthurs", pyrosStat.eldthursCounter },
                { "Peinados de Pyros", pyrosStat.pyrosHairStyleCounter }
            });
        }

        if (ImGui.CollapsingHeader("Estadísticas de Pagos"))
        {
            DrawDictionaryStats(new Dictionary<string, int>
            {
                { "Gil Obtenido", pagosStat.gilEarned },
                { "Cofres de Oro", pagosStat.goldCoffer },
                { "Cofres de Plata", pagosStat.silverCoffer },
                { "Cofres de Bronce", pagosStat.bronzeCoffer },
                { "Minion Bulb", pagosStat.bulbMinion },
                { "Ojo Hakutaku", pagosStat.hakutakuEye }
            });
        }

        ImGui.EndChild();

        var isCtrlHeld = ImGui.GetIO().KeyCtrl;
        using (var _ = ImRaii.PushStyle(ImGuiStyleVar.Alpha, 0.5f, !isCtrlHeld))
        {
            reset = ImGui.Button("RESTABLECER ESTADÍSTICAS (MANTÉN CTRL)", new Vector2(ImGui.GetContentRegionAvail().X, buttonHeight)) && isCtrlHeld;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(isCtrlHeld ? "Pulsa para reiniciar las estadísticas." : "Mantén pulsada la tecla Ctrl para habilitar el botón.");
        }
    }

    private static void DrawDictionaryStats(Dictionary<string, int> items)
    {
        if (ImGui.BeginTable("##StatsGrid", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Concepto", ImGuiTableColumnFlags.WidthStretch, 0.6f);
            ImGui.TableSetupColumn("Cantidad", ImGuiTableColumnFlags.WidthStretch, 0.4f);

            foreach (var (label, val) in items)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(label);

                ImGui.TableNextColumn();
                ImGui.TextColored(ImGuiColors.ParsedGold, val.ToString("N0"));
            }

            ImGui.EndTable();
        }
    }
}
