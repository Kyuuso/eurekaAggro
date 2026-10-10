using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace BFE.Ui;

/// <summary>
/// Iconos estándar mapeados para la interfaz gráfica.
/// </summary>
internal enum MaterialIcon
{
    None,
    ChevronDown,
    ArrowRight,
    MapPin,
    Document,
    Clock,
    Refresh,
    Play,
    Stop
}

/// <summary>
/// Utilidades y componentes visuales para renderizar la interfaz de usuario
/// mediante ImGui nativo de Dalamud, sin dependencias de librerías externas propietarias.
/// </summary>
internal static class UiGui
{
    internal static void TextUnformatted(string text) => ImGui.TextUnformatted(UiText.T(text));
    internal static void TextWrapped(string text) => ImGui.TextWrapped(UiText.T(text));
    internal static void TextDisabled(string text) => ImGui.TextDisabled(UiText.T(text));
    internal static void Text(string text) => ImGui.TextUnformatted(UiText.T(text));
    internal static void BulletText(string text) => ImGui.BulletText(UiText.T(text));
    internal static void TextColored(Vector4 color, string text) => ImGui.TextColored(color, UiText.T(text));

    internal static bool SliderFloat(string label, ref float value, float min, float max, string format)
    {
        var cleanLabel = UiText.T(label.Split("##", 2)[0]) + "###" + label;
        return ImGui.SliderFloat(cleanLabel, ref value, min, max, format);
    }

    internal static bool SliderInt(string label, ref int value, int min, int max)
    {
        var cleanLabel = UiText.T(label.Split("##", 2)[0]) + "###" + label;
        return ImGui.SliderInt(cleanLabel, ref value, min, max);
    }

    internal static bool InputText(string label, ref string value, int length)
    {
        var cleanLabel = UiText.T(label.Split("##", 2)[0]) + "###" + label;
        return ImGui.InputText(cleanLabel, ref value);
    }

    internal static bool InputInt(string label, ref int value)
    {
        var cleanLabel = UiText.T(label.Split("##", 2)[0]) + "###" + label;
        return ImGui.InputInt(cleanLabel, ref value);
    }

    internal static bool Combo(string label, ref int value, string[] options, int count)
    {
        var cleanLabel = UiText.T(label.Split("##", 2)[0]) + "###" + label;
        var translated = options.Take(count).Select(o => UiText.T(o)).ToArray();
        return ImGui.Combo(cleanLabel, ref value, translated, count);
    }

    internal static bool CollapsingHeader(string label)
    {
        return ImGui.CollapsingHeader(UiText.T(label.Split("##", 2)[0]) + "###" + label);
    }

    internal static bool Button(string label, string? display = null)
    {
        var text = display ?? UiText.T(label.Split("##", 2)[0]);
        return ImGui.Button(text + "###" + label);
    }

    internal static bool Button(string label, Vector2 pixels)
    {
        var text = UiText.T(label.Split("##", 2)[0]);
        return ImGui.Button(text + "###" + label, pixels);
    }

    internal static bool SmallButton(string label, string? display = null)
    {
        var text = display ?? UiText.T(label.Split("##", 2)[0]);
        return ImGui.SmallButton(text + "###" + label);
    }

    internal static bool Checkbox(string label, ref bool value)
    {
        var text = UiText.T(label.Split("##", 2)[0]);
        return ImGui.Checkbox(text + "###" + label, ref value);
    }

    internal static bool RadioButton(string label, bool selected)
    {
        return ImGui.RadioButton(UiText.T(label), selected);
    }

    internal static bool Selectable(string original, bool selected, string? display = null)
    {
        var text = display ?? UiText.T(original.Split("##", 2)[0]);
        return ImGui.Selectable(text + "###" + original, selected);
    }

    internal static bool TabItem(string original)
    {
        return ImGui.BeginTabItem(UiText.T(original.Split("##", 2)[0]) + "###" + original);
    }

    internal static bool TabItem(string original, MaterialIcon icon, float logicalWidth)
    {
        var iconFa = ToFontAwesome(icon);
        var label = iconFa != FontAwesomeIcon.None
            ? $"{iconFa.ToIconString()}  {UiText.T(original.Split("##", 2)[0])}"
            : UiText.T(original.Split("##", 2)[0]);
        return ImGui.BeginTabItem(label + "###" + original);
    }

    /// <summary>
    /// Botón de acción principal destacado con colores llamativos según su estado de ejecución.
    /// </summary>
    internal static bool FilledAction(string original, MaterialIcon icon, bool disabled)
    {
        var isStop = original.Contains("Stop", StringComparison.OrdinalIgnoreCase);
        var btnColor = isStop ? new Vector4(0.85f, 0.25f, 0.25f, 1f) : new Vector4(0.18f, 0.65f, 0.35f, 1f);
        var hoverColor = isStop ? new Vector4(0.95f, 0.35f, 0.35f, 1f) : new Vector4(0.25f, 0.75f, 0.45f, 1f);
        var activeColor = isStop ? new Vector4(0.70f, 0.20f, 0.20f, 1f) : new Vector4(0.15f, 0.55f, 0.30f, 1f);

        ImGui.BeginDisabled(disabled);
        ImGui.PushStyleColor(ImGuiCol.Button, btnColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoverColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, activeColor);
        ImGui.PushStyleColor(ImGuiCol.Text, Vector4.One);

        var fa = ToFontAwesome(icon);
        var text = fa != FontAwesomeIcon.None
            ? $"{fa.ToIconString()}  {UiText.T(original)}"
            : UiText.T(original);

        var clicked = ImGui.Button(text, new Vector2(ImGui.GetContentRegionAvail().X, 36f * ImGuiHelpers.GlobalScale));

        ImGui.PopStyleColor(4);
        ImGui.EndDisabled();
        return clicked;
    }

    internal static bool IconButton(string original, MaterialIcon icon, bool header = false, bool link = false)
    {
        var fa = ToFontAwesome(icon);
        var text = fa != FontAwesomeIcon.None
            ? $"{fa.ToIconString()}  {UiText.T(original.Split("##", 2)[0])}"
            : UiText.T(original.Split("##", 2)[0]);

        if (link)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.ParsedPurple);
            var clicked = ImGui.SmallButton(text + "###" + original);
            ImGui.PopStyleColor();
            return clicked;
        }

        return ImGui.Button(text + "###" + original);
    }

    internal static void SameLineIfFits(string label)
    {
        var width = ImGui.CalcTextSize(UiText.T(label)).X + ImGui.GetStyle().FramePadding.X * 2;
        if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + width <= ImGui.GetWindowPos().X + ImGui.GetWindowSize().X)
        {
            ImGui.SameLine();
        }
    }

    internal static void SameLineIconIfFits(string label, bool header = false)
    {
        SameLineIfFits(label);
    }

    internal static float IconButtonWidth(string label, bool header = false, bool link = false)
    {
        return ImGui.CalcTextSize(UiText.T(label.Split("##", 2)[0])).X + 40f * ImGuiHelpers.GlobalScale;
    }

    internal static void Title(string original, string translated)
    {
        // Título decorativo en ventana
        ImGui.TextColored(ImGuiColors.DalamudViolet, translated);
        ImGui.Separator();
    }

    internal static void ReserveTitleSpace(Window owner, string visible, float minimumWidth)
    {
        var bounds = owner.SizeConstraints ?? new WindowSizeConstraints();
        bounds.MinimumSize = new Vector2(Math.Max(minimumWidth, 300f), bounds.MinimumSize.Y);
        owner.SizeConstraints = bounds;
    }

    internal static void PaintTitleWithImage(Window owner, string display)
    {
        // Dalamud dibuja el título nativamente en la barra de la ventana
    }

    internal static void TableHeadersRow(float height = 0)
    {
        ImGui.TableHeadersRow();
    }

    internal static void CenterColumnText(string text, bool underlined = false)
    {
        var localized = UiText.T(text);
        var textWidth = ImGui.CalcTextSize(localized).X;
        var colWidth = ImGui.GetColumnWidth();
        if (colWidth > textWidth)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (colWidth - textWidth) * 0.5f);
        }
        ImGui.TextUnformatted(localized);
        if (underlined)
        {
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            ImGui.GetWindowDrawList().AddLine(new Vector2(min.X, max.Y), max, ImGui.GetColorU32(ImGuiCol.Text));
        }
    }

    internal static void CenterColumnText(Vector4 color, string text, bool underlined = false)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        try
        {
            CenterColumnText(text, underlined);
        }
        finally
        {
            ImGui.PopStyleColor();
        }
    }

    /// <summary>
    /// Mapea los iconos de diseño a iconos estándar FontAwesome de Dalamud.
    /// </summary>
    internal static FontAwesomeIcon ToFontAwesome(MaterialIcon icon) => icon switch
    {
        MaterialIcon.ChevronDown => FontAwesomeIcon.ChevronDown,
        MaterialIcon.ArrowRight => FontAwesomeIcon.ArrowRight,
        MaterialIcon.MapPin => FontAwesomeIcon.MapMarkerAlt,
        MaterialIcon.Document => FontAwesomeIcon.FileAlt,
        MaterialIcon.Clock => FontAwesomeIcon.Clock,
        MaterialIcon.Refresh => FontAwesomeIcon.Sync,
        MaterialIcon.Play => FontAwesomeIcon.Play,
        MaterialIcon.Stop => FontAwesomeIcon.Stop,
        _ => FontAwesomeIcon.None
    };
}
