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
/// Vista principal de la automatización de conejos de Eureka.
/// Reproduce con fidelidad el diseño oscuro con tarjetas de área,
/// monitoreo de tareas, verificación de dependencias y botón de acción destacado.
/// </summary>
internal class StartBunnies
{
    public static bool IsRunning = false;

    // Colores de la paleta estética de BFE
    private static readonly Vector4 ColorFondoTarjeta = new(0.106f, 0.110f, 0.133f, 1f);      // #1B1C22
    private static readonly Vector4 ColorFondoCampo = new(0.086f, 0.090f, 0.110f, 1f);        // #16171C
    private static readonly Vector4 ColorBordeNormal = new(0.157f, 0.165f, 0.212f, 1f);       // #282A36
    private static readonly Vector4 ColorDoradoAcento = new(0.961f, 0.729f, 0.259f, 1f);      // #F5BA42
    private static readonly Vector4 ColorDoradoOscuro = new(0.12f, 0.10f, 0.05f, 1f);
    private static readonly Vector4 ColorVerdeDisponible = new(0.239f, 0.839f, 0.467f, 1f);   // #3DD677
    private static readonly Vector4 ColorTextoSecundario = new(0.608f, 0.620f, 0.663f, 1f);   // #9B9EA9
    private static readonly Vector4 ColorRojoDetener = new(0.851f, 0.255f, 0.255f, 1f);       // #D94141

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

        // 1. SELECCIÓN DE ÁREA (Area Selection)
        ImGui.TextColored(Vector4.One, UiText.T("Area Selection"));
        ImGui.Spacing();

        DrawAreaCards(scale);

        ImGui.Spacing();
        ImGui.Spacing();

        // 2. TAREA ACTUAL (Task)
        ImGui.TextColored(Vector4.One, UiText.T("Task"));
        ImGui.Spacing();
        DrawTextField("TaskField", icurrentTask == "idle" ? "Idle. Select an area and press Start to begin." : icurrentTask, scale);

        ImGui.Spacing();
        ImGui.Spacing();

        // 3. TIEMPO TRANSCURRIDO (Time elapsed)
        ImGui.TextColored(Vector4.One, UiText.T("Time elapsed"));
        ImGui.Spacing();
        DrawTextField("TimeField", P.stopwatch.Elapsed.ToString(@"mm\:ss\.fff"), scale);

        ImGui.Spacing();
        ImGui.Spacing();

        // 4. DEPENDENCIAS (Dependencies)
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

        // 5. BOTÓN DE ACCIÓN PRINCIPAL (Start Pyros / Stop)
        DrawMainActionButton(scale);
    }

    /// <summary>
    /// Dibuja las tres tarjetas de zona (Pagos, Pyros con badge WIP, Hydatos).
    /// </summary>
    private static void DrawAreaCards(float scale)
    {
        var anchoTotal = ImGui.GetContentRegionAvail().X;
        var espacio = 12f * scale;
        var anchoTarjeta = (anchoTotal - (espacio * 2f)) / 3f;
        var altoTarjeta = 64f * scale;

        // Tarjeta 1: Pagos (No disponible)
        DrawCard(0, "Pagos", "Not available", false, false, anchoTarjeta, altoTarjeta, scale);
        ImGui.SameLine(0, espacio);

        // Tarjeta 2: Pyros (Activo y seleccionado con badge WIP)
        DrawCard(1, "Pyros", "Select to start bunnies", true, true, anchoTarjeta, altoTarjeta, scale);
        ImGui.SameLine(0, espacio);

        // Tarjeta 3: Hydatos (No disponible)
        DrawCard(2, "Hydatos", "Not available", false, false, anchoTarjeta, altoTarjeta, scale);
    }

    private static void DrawCard(int index, string nombre, string descripcion, bool isSelected, bool isWip, float ancho, float alto, float scale)
    {
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();

        // Color de fondo y borde según estado
        var colorFondo = isSelected ? ColorFondoTarjeta : new Vector4(0.09f, 0.095f, 0.11f, 1f);
        var colorBorde = isSelected ? ColorDoradoAcento : ColorBordeNormal;

        dl.AddRectFilled(pos, pos + new Vector2(ancho, alto), ImGui.ColorConvertFloat4ToU32(colorFondo), 6f * scale);
        dl.AddRect(pos, pos + new Vector2(ancho, alto), ImGui.ColorConvertFloat4ToU32(colorBorde), 6f * scale, ImDrawFlags.None, isSelected ? 1.5f : 1.0f);

        // Selector circular (Radio button visual)
        var centroRadio = pos + new Vector2(24f * scale, alto * 0.5f);
        var radioExterior = 8f * scale;
        dl.AddCircle(centroRadio, radioExterior, ImGui.ColorConvertFloat4ToU32(isSelected ? ColorDoradoAcento : new Vector4(0.4f, 0.42f, 0.5f, 1f)), 24, 1.5f);
        if (isSelected)
        {
            dl.AddCircleFilled(centroRadio, 4f * scale, ImGui.ColorConvertFloat4ToU32(ColorDoradoAcento));
        }

        // Título del área y Badge WIP
        var posTexto = pos + new Vector2(42f * scale, 14f * scale);
        dl.AddText(posTexto, ImGui.ColorConvertFloat4ToU32(isSelected ? Vector4.One : ColorTextoSecundario), nombre);

        if (isWip)
        {
            var anchoTextoNombre = ImGui.CalcTextSize(nombre).X;
            var posBadgeMin = posTexto + new Vector2(anchoTextoNombre + 8f * scale, -1f * scale);
            var posBadgeMax = posBadgeMin + new Vector2(34f * scale, 16f * scale);
            dl.AddRectFilled(posBadgeMin, posBadgeMax, ImGui.ColorConvertFloat4ToU32(ColorDoradoAcento), 3f * scale);
            dl.AddText(posBadgeMin + new Vector2(5f * scale, 1f * scale), 0xFF111111, "WIP");
        }

        // Subtítulo descriptivo
        var posDesc = pos + new Vector2(42f * scale, 34f * scale);
        dl.AddText(posDesc, ImGui.ColorConvertFloat4ToU32(ColorTextoSecundario), UiText.T(descripcion));

        // Botón invisible para interacción táctil/ratón
        ImGui.SetCursorScreenPos(pos);
        if (ImGui.InvisibleButton($"##Card_{nombre}", new Vector2(ancho, alto)))
        {
            if (index == 1) // Solo Pyros está activo actualmente
            {
                C.zoneSelected = (sbyte)index;
                C.Save();
            }
        }
    }

    /// <summary>
    /// Dibuja una caja de campo oscura con esquinas redondeadas.
    /// </summary>
    private static void DrawTextField(string id, string texto, float scale)
    {
        var pos = ImGui.GetCursorScreenPos();
        var ancho = ImGui.GetContentRegionAvail().X;
        var alto = 36f * scale;
        var dl = ImGui.GetWindowDrawList();

        dl.AddRectFilled(pos, pos + new Vector2(ancho, alto), ImGui.ColorConvertFloat4ToU32(ColorFondoCampo), 6f * scale);
        dl.AddRect(pos, pos + new Vector2(ancho, alto), ImGui.ColorConvertFloat4ToU32(ColorBordeNormal), 6f * scale);

        var textoLocalizado = UiText.T(texto);
        var tamTexto = ImGui.CalcTextSize(textoLocalizado);
        var posTexto = pos + new Vector2(14f * scale, (alto - tamTexto.Y) * 0.5f);
        dl.AddText(posTexto, ImGui.ColorConvertFloat4ToU32(ColorTextoSecundario), textoLocalizado);

        ImGui.Dummy(new Vector2(ancho, alto));
    }

    /// <summary>
    /// Dibuja la lista de dependencias en una tabla estructurada con columnas independientes
    /// para evitar cualquier solapamiento o desalineación visual.
    /// </summary>
    private static void DrawDependenciesList(float scale)
    {
        var dependencias = P.pluginDependencies.RequiredStatuses;

        if (ImGui.BeginTable("##DepsTable", 3, ImGuiTableFlags.BordersOuter | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Dependency", ImGuiTableColumnFlags.WidthStretch, 0.50f);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch, 0.25f);
            ImGui.TableSetupColumn("Repository", ImGuiTableColumnFlags.WidthFixed, 110f * scale);
            ImGui.TableHeadersRow();

            foreach (var dep in dependencias)
            {
                ImGui.TableNextRow(ImGuiTableRowFlags.None, 28f * scale);

                // Columna 1: Icono + Nombre de la dependencia
                ImGui.TableNextColumn();
                var estaCargado = dep.State == PluginDependencyState.Loaded;
                var colorCheck = estaCargado ? ColorVerdeDisponible : (dep.State == PluginDependencyState.InstalledNotLoaded ? ImGuiColors.DalamudYellow : ImGuiColors.DalamudRed);

                var cellPos = ImGui.GetCursorScreenPos();
                var dl = ImGui.GetWindowDrawList();
                var center = cellPos + new Vector2(8f * scale, ImGui.GetTextLineHeight() * 0.5f + 3f * scale);
                dl.AddCircleFilled(center, 5f * scale, ImGui.ColorConvertFloat4ToU32(colorCheck));
                if (estaCargado)
                {
                    dl.AddLine(center + new Vector2(-2.5f * scale, 0), center + new Vector2(-0.5f * scale, 2f * scale), 0xFF111111, 1.5f * scale);
                    dl.AddLine(center + new Vector2(-0.5f * scale, 2f * scale), center + new Vector2(3f * scale, -2f * scale), 0xFF111111, 1.5f * scale);
                }

                ImGui.SetCursorScreenPos(cellPos + new Vector2(20f * scale, 2f * scale));
                ImGui.TextUnformatted(dep.DisplayName);

                // Columna 2: Estado (Available / Missing)
                ImGui.TableNextColumn();
                var textoEstado = estaCargado ? "Available" : dep.StateText;
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 2f * scale);
                ImGui.TextColored(colorCheck, UiText.T(textoEstado));

                // Columna 3: Botón de copiado de URL del repositorio
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
    /// Dibuja el botón destacado grande de inicio/parada al pie de la ventana.
    /// </summary>
    private static void DrawMainActionButton(float scale)
    {
        var ready = P.pluginDependencies.RequiredDependenciesLoaded;
        var btnColor = IsRunning ? ColorRojoDetener : ColorDoradoAcento;
        var hoverColor = IsRunning ? new Vector4(0.95f, 0.35f, 0.35f, 1f) : new Vector4(0.98f, 0.78f, 0.32f, 1f);
        var textColor = IsRunning ? Vector4.One : new Vector4(0.07f, 0.07f, 0.07f, 1f);

        ImGui.BeginDisabled(!ready && !IsRunning);
        ImGui.PushStyleColor(ImGuiCol.Button, btnColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoverColor);
        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f * scale);

        var etiqueta = ActionLabel;

        if (ImGui.Button(etiqueta, new Vector2(ImGui.GetContentRegionAvail().X, 42f * scale)))
        {
            RunActionFromUi();
        }

        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
        ImGui.EndDisabled();
    }
}
