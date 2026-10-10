using System;
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
/// Controlador y vista principal para el inicio y monitoreo de la automatización de conejos.
/// Renderizado nativo con ImGui de Dalamud sin dependencias externas.
/// </summary>
internal class StartBunnies
{
    public static bool IsRunning = false;

    internal static string ActionLabel => IsRunning
        ? "Detener Conejos"
        : $"Iniciar {C.zoneSelected switch { 0 => "Pagos", 1 => "Pyros", 2 => "Hydatos", _ => "Eureka" }}";

    internal static string ActionTitleTooltip => UiText.T(ActionLabel) + "\n" + UiText.T(
        !IsRunning && !P.pluginDependencies.RequiredDependenciesLoaded
            ? "Carga todos los plugins requeridos para iniciar la automatización."
            : icurrentTask == "idle" ? "En reposo. Selecciona una zona y pulsa Iniciar." : icurrentTask);

    internal static void RunActionFromUi()
    {
        if (IsRunning)
        {
            SchedulerMain.DisablePlugin();
            RunCommand("e [Eureka Conejos] Automatización detenida.");
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

        // 1. Área de selección de zona de Eureka
        ImGui.TextColored(ImGuiColors.ParsedGold, $"{FontAwesomeIcon.MapMarkerAlt.ToIconString()}  Zona de Eureka");
        ImGui.Separator();

        DrawAreas();

        ImGui.Spacing();
        ImGui.Spacing();

        // 2. Información del estado y tiempo transcurrido
        ImGui.TextColored(ImGuiColors.ParsedGold, $"{FontAwesomeIcon.Tasks.ToIconString()}  Estado de la Misión");
        ImGui.Separator();

        var estadoActual = icurrentTask == "idle" ? "En reposo. Pulsa Iniciar para comenzar el ciclo." : icurrentTask;
        ImGui.TextColored(ImGuiColors.DalamudWhite, $"Tarea actual:  {UiText.T(estadoActual)}");
        ImGui.TextColored(ImGuiColors.DalamudGrey, $"Tiempo activo: {P.stopwatch.Elapsed:mm\\:ss\\.fff}");

        ImGui.Spacing();
        ImGui.Spacing();

        // 3. Tabla de verificación de dependencias
        DrawDependencyStatus();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // 4. Botón de acción principal (Iniciar / Detener)
        var ready = P.pluginDependencies.RequiredDependenciesLoaded;
        var btnColor = IsRunning ? new Vector4(0.85f, 0.25f, 0.25f, 1f) : new Vector4(0.18f, 0.65f, 0.35f, 1f);
        var hoverColor = IsRunning ? new Vector4(0.95f, 0.35f, 0.35f, 1f) : new Vector4(0.25f, 0.75f, 0.45f, 1f);

        ImGui.BeginDisabled(!ready && !IsRunning);
        ImGui.PushStyleColor(ImGuiCol.Button, btnColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoverColor);
        ImGui.PushStyleColor(ImGuiCol.Text, Vector4.One);

        var icon = IsRunning ? FontAwesomeIcon.Stop.ToIconString() : FontAwesomeIcon.Play.ToIconString();
        if (ImGui.Button($"{icon}   {ActionLabel}", new Vector2(ImGui.GetContentRegionAvail().X, 42f * scale)))
        {
            RunActionFromUi();
        }

        ImGui.PopStyleColor(3);
        ImGui.EndDisabled();

        if (!ready && !IsRunning)
        {
            ImGui.TextColored(ImGuiColors.DalamudYellow, "Instala y activa todos los plugins requeridos para poder iniciar.");
        }
    }

    private static void DrawAreas()
    {
        var currentZone = (int)C.zoneSelected;

        ImGui.BeginDisabled(true);
        if (ImGui.RadioButton("Eureka Pagos (Próximamente)", currentZone == 0))
        {
            C.zoneSelected = 0;
            C.Save();
        }
        ImGui.EndDisabled();

        ImGui.SameLine(220f * ImGuiHelpers.GlobalScale);
        if (ImGui.RadioButton("Eureka Pyros [Activo]", currentZone == 1))
        {
            C.zoneSelected = 1;
            C.Save();
        }

        ImGui.SameLine(440f * ImGuiHelpers.GlobalScale);
        ImGui.BeginDisabled(true);
        if (ImGui.RadioButton("Eureka Hydatos (Próximamente)", currentZone == 2))
        {
            C.zoneSelected = 2;
            C.Save();
        }
        ImGui.EndDisabled();
    }

    private static void DrawDependencyStatus()
    {
        ImGui.TextColored(ImGuiColors.ParsedGold, $"{FontAwesomeIcon.Plug.ToIconString()}  Dependencias del Sistema");
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 100f * ImGuiHelpers.GlobalScale);
        if (ImGui.SmallButton($"{FontAwesomeIcon.Sync.ToIconString()} Actualizar"))
        {
            P.pluginDependencies.Refresh(true);
        }
        ImGui.Separator();

        var statuses = P.pluginDependencies.RequiredStatuses;
        if (ImGui.BeginTable("##DependenciasConejos", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Plugin", ImGuiTableColumnFlags.WidthStretch, 0.45f);
            ImGui.TableSetupColumn("Estado", ImGuiTableColumnFlags.WidthStretch, 0.35f);
            ImGui.TableSetupColumn("Repositorio", ImGuiTableColumnFlags.WidthFixed, 130f * ImGuiHelpers.GlobalScale);
            ImGui.TableHeadersRow();

            foreach (var dep in statuses)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(dep.DisplayName);

                ImGui.TableNextColumn();
                var color = GetDependencyColor(dep.State);
                ImGui.TextColored(color, UiText.T(dep.StateText));

                ImGui.TableNextColumn();
                if (ImGui.SmallButton($"Copiar Repo##{dep.InternalName}"))
                {
                    ImGui.SetClipboardText(dep.RepoUrl);
                    DuoLog.Information("URL del repositorio copiada al portapapeles.");
                    Notify.Info(UiText.T("Repo URL Copied"));
                }
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
