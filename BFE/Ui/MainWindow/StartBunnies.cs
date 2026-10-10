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
        var drawList = ImGui.GetWindowDrawList();

        // Marco contenedor principal
        var posMarco = ImGui.GetCursorScreenPos();
        var anchoDisponible = ImGui.GetContentRegionAvail().X;
        var paddingMarco = 16f * scale;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(paddingMarco, paddingMarco));
        ImGui.BeginChild("##MarcoContenedorBFE", new Vector2(anchoDisponible, ImGui.GetContentRegionAvail().Y - 54f * scale), true, ImGuiWindowFlags.None);
        {
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
            var posDep = ImGui.GetCursorScreenPos();
            ImGui.TextColored(Vector4.One, UiText.T("Dependencies"));

            ImGui.SameLine(ImGui.GetContentRegionAvail().X - 85f * scale);
            if (ImGui.SmallButton(UiText.T("Refresh")))
            {
                P.pluginDependencies.Refresh(true);
            }

            ImGui.Spacing();
            DrawDependenciesList(scale);
        }
        ImGui.EndChild();
        ImGui.PopStyleVar();

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
    /// Dibuja la lista de dependencias con iconos de verificación circular y enlaces de repositorio.
    /// </summary>
    private static void DrawDependenciesList(float scale)
    {
        var dl = ImGui.GetWindowDrawList();
        var dependencias = P.pluginDependencies.RequiredStatuses;

        foreach (var dep in dependencias)
        {
            var pos = ImGui.GetCursorScreenPos();
            var ancho = ImGui.GetContentRegionAvail().X;
            var alto = 26f * scale;

            // Icono circular con check verde
            var centroCheck = pos + new Vector2(10f * scale, alto * 0.5f);
            var estaCargado = dep.State == PluginDependencyState.Loaded;
            var colorCheck = estaCargado ? ColorVerdeDisponible : (dep.State == PluginDependencyState.InstalledNotLoaded ? ImGuiColors.DalamudYellow : ImGuiColors.DalamudRed);

            dl.AddCircleFilled(centroCheck, 8f * scale, ImGui.ColorConvertFloat4ToU32(colorCheck));
            if (estaCargado)
            {
                var p1 = centroCheck + new Vector2(-4f * scale, 0f * scale);
                var p2 = centroCheck + new Vector2(-1f * scale, 3.5f * scale);
                var p3 = centroCheck + new Vector2(4.5f * scale, -3.5f * scale);
                dl.AddLine(p1, p2, 0xFF111111, 2f * scale);
                dl.AddLine(p2, p3, 0xFF111111, 2f * scale);
            }
            else
            {
                var tamIcono = ImGui.CalcTextSize("!");
                dl.AddText(centroCheck - (tamIcono * 0.5f), 0xFF111111, "!");
            }

            // Nombre del plugin
            dl.AddText(pos + new Vector2(26f * scale, 3f * scale), 0xFFFFFFFF, dep.DisplayName);

            // Estado (Available / Missing)
            var textoEstado = estaCargado ? "Available" : dep.StateText;
            dl.AddText(pos + new Vector2(160f * scale, 3f * scale), ImGui.ColorConvertFloat4ToU32(colorCheck), UiText.T(textoEstado));

            // Enlace de copiado del repositorio a la derecha
            var textoEnlace = "Get Repo Url";
            var tamEnlace = ImGui.CalcTextSize(textoEnlace);
            var xEnlace = ancho - tamEnlace.X - 8f * scale;

            ImGui.SetCursorScreenPos(pos + new Vector2(xEnlace, 0));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.42f, 0.60f, 0.97f, 1f));
            if (ImGui.SmallButton($"##Link_{dep.InternalName}"))
            {
                ImGui.SetClipboardText(dep.RepoUrl);
                DuoLog.Information("Repo URL copied to clipboard.");
                Notify.Info(UiText.T("Repo URL Copied"));
            }
            ImGui.PopStyleColor();

            // Dibujamos el texto encima del botón invisible
            dl.AddText(pos + new Vector2(xEnlace, 3f * scale), 0xFFF89A6C, textoEnlace);

            ImGui.SetCursorScreenPos(pos + new Vector2(0, alto + 4f * scale));
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
