using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace BFE.Ui;

// Native widgets receive their original English labels/IDs. Only their visible label is painted in the selected locale.
// This also preserves English-derived helper IDs and existing saved window identities.
internal static class UiGui
{
    internal static void TextUnformatted(string text) => MaterialText.Text(UiText.T(text));
    internal static void TextWrapped(string text) => MaterialText.TextWrapped(UiText.T(text));
    internal static void TextDisabled(string text)
    {
        ImGui.PushTextWrapPos(0);
        try { MaterialText.TextDisabled(UiText.T(text)); }
        finally { ImGui.PopTextWrapPos(); }
    }
    internal static void Text(string text) => MaterialText.Text(UiText.T(text));
    internal static void BulletText(string text) => MaterialText.BulletText(UiText.T(text));
    internal static void TextColored(Vector4 color, string text) => MaterialText.TextColored(color, UiText.T(text));

    internal static bool SliderFloat(string label, ref float value, float min, float max, string format)
    {
        BeginField(label);
        var changed = ImGui.SliderFloat("", ref value, min, max, format);
        ImGui.PopID();
        return changed;
    }
    internal static bool CollapsingHeader(string label)
    {
        var translated = UiText.T(label.Split("##", 2)[0]);
        using var height = MaterialText.PushLineHeight(translated);
        var position = ImGui.GetCursorScreenPos();
        ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
        var open = ImGui.CollapsingHeader(label);
        ImGui.PopStyleColor();
        var c = MaterialTheme.Current.Colors;
        var dl = ImGui.GetWindowDrawList();
        dl.PushClipRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), true);
        try
        {
        MaterialIcons.Draw(open ? MaterialIcon.ChevronDown : MaterialIcon.ArrowRight, position + ImGui.GetStyle().FramePadding, ImGui.GetFontSize(), c.OnSurface);
        MaterialText.AddText(dl, position + new Vector2(ImGui.GetFontSize() + ImGui.GetStyle().FramePadding.X * 2,
            (ImGui.GetItemRectMax().Y - position.Y - MaterialText.Measure(translated).Y) * .5f), MaterialCanvas.Color(c.OnSurface), translated);
        }
        finally { dl.PopClipRect(); }
        return open;
    }
    private static void Label(string original,Vector2 position,Vector4 background,Vector4 foreground,Vector2? clip=null,string? display=null)
    {
        var visible=original.Split("##",2)[0];
        var translated=display ?? UiText.T(visible);
        if(translated==visible) return;
        var dl=ImGui.GetWindowDrawList();
        var width=Math.Max(MaterialText.Measure(visible).X,MaterialText.Measure(translated).X);
        if(clip is { } max) dl.PushClipRect(position,max,true);
        try
        {
            dl.AddRectFilled(position,position+new Vector2(width,Math.Max(ImGui.GetTextLineHeight(),MaterialText.Measure(translated).Y)),ImGui.ColorConvertFloat4ToU32(background));
            foreground.W*=ImGui.GetStyle().Alpha;
            MaterialText.AddText(dl, position,ImGui.ColorConvertFloat4ToU32(foreground),translated);
        }
        finally { if(clip.HasValue) dl.PopClipRect(); }
    }
    internal static bool Button(string label,string? display=null)
    {
        var translated=display ?? UiText.T(label.Split("##",2)[0]);
        using var controls = ImGui.GetStyle().FramePadding.Y == 0 || MaterialControls.Context == MaterialControlContext.Dense
            ? default(MaterialControls.ControlScope) : MaterialControls.Push(MaterialControlContext.Toolbar);
        using var height = MaterialText.PushLineHeight(translated);
        var width=MaterialLayout.FitNextItemWidth(0,MaterialText.Measure(translated).X+2*ImGui.GetStyle().FramePadding.X);
        var foreground=ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);
        var clicked=ImGui.Button(label,new Vector2(width,0));
        ImGui.PopStyleColor();
        var min=ImGui.GetItemRectMin(); var max=ImGui.GetItemRectMax();
        foreground.W*=ImGui.GetStyle().Alpha;
        ImGui.GetWindowDrawList().PushClipRect(min,max,true);
        try
        {
        MaterialText.AddText(ImGui.GetWindowDrawList(), min+(max-min-MaterialText.Measure(translated))*.5f,ImGui.ColorConvertFloat4ToU32(foreground),translated);
        }
        finally { ImGui.GetWindowDrawList().PopClipRect(); }
        return clicked;
    }
    internal static bool Button(string label, Vector2 pixels)
    {
        var translated = UiText.T(label.Split("##", 2)[0]);
        using var controls = ImGui.GetStyle().FramePadding.Y == 0 || MaterialControls.Context == MaterialControlContext.Dense
            ? default(MaterialControls.ControlScope) : MaterialControls.Push(MaterialControlContext.Toolbar);
        using var height = MaterialText.PushLineHeight(translated);
        pixels.Y = Math.Max(pixels.Y, ImGui.GetFrameHeight());
        var color = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        pixels.X = MaterialLayout.FitNextItemWidth(pixels.X, MaterialText.Measure(translated).X + 2 * ImGui.GetStyle().FramePadding.X);
        ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
        var clicked = ImGui.Button(label, pixels);
        ImGui.PopStyleColor();
        var min = ImGui.GetItemRectMin(); var max = ImGui.GetItemRectMax();
        var dl = ImGui.GetWindowDrawList();
        dl.PushClipRect(min, max, true);
        try
        {
        MaterialText.AddText(dl, min + (max - min - MaterialText.Measure(translated)) * .5f, MaterialCanvas.Color(color), translated);
        }
        finally { dl.PopClipRect(); }
        return clicked;
    }
    internal static bool FilledAction(string original, MaterialIcon icon, bool disabled)
    {
        var s = MaterialTheme.Metrics.Scale;
        var c = MaterialTheme.Current.Colors;
        var label = UiText.T(original);
        using var controls = MaterialControls.Push(MaterialControlContext.Toolbar);
        using var lineHeight = MaterialText.PushLineHeight(label);
        var size = new Vector2(MaterialLayout.FitNextItemWidth((BfePresentation.Compact ? 272 : 280) * s, MaterialText.Measure(label).X + 72 * s), Math.Max(ImGui.GetFrameHeight(), (24 + 2 * (BfePresentation.Compact ? 2 : 4)) * s));
        var fill = BfePresentation.ActionFill;
        ImGui.BeginDisabled(disabled);
        try
        {
        ImGui.PushStyleColor(ImGuiCol.Button, fill);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, MaterialColor.Layer(fill, c.OnPrimary, .08f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, MaterialColor.Layer(fill, c.OnPrimary, .14f));
        ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
        var clicked = ImGui.Button(original, size);
        ImGui.PopStyleColor(4);
        var min = ImGui.GetItemRectMin(); var max = ImGui.GetItemRectMax();
        var ink = c.OnPrimary;
        ink.W *= ImGui.GetStyle().Alpha;
        var dl = ImGui.GetWindowDrawList();
        dl.PushClipRect(min, max, true);
        try
        {
        MaterialIcons.Draw(icon, min + new Vector2(16 * s, (size.Y - 24 * s) * .5f), 24 * s, ink);
        MaterialText.AddText(dl, min + new Vector2(52 * s, (size.Y - MaterialText.Measure(label).Y) * .5f), MaterialCanvas.Color(ink), label);
        }
        finally { dl.PopClipRect(); }
        return clicked;
        }
        finally { ImGui.EndDisabled(); }
    }
    internal static bool TabItem(string original)
    {
        var label = UiText.T(original);
        using var height = MaterialText.PushLineHeight(label);
        ImGui.SetNextItemWidth(MaterialText.Measure(label).X + ImGui.GetStyle().FramePadding.X * 2);
        var color = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
        var open = ImGui.BeginTabItem(original);
        ImGui.PopStyleColor();
        var min = ImGui.GetItemRectMin(); var max = ImGui.GetItemRectMax();
        var dl = ImGui.GetWindowDrawList();
        dl.PushClipRect(min, max, true);
        try
        {
        MaterialText.AddText(dl, min + (max - min - MaterialText.Measure(label)) * .5f, MaterialCanvas.Color(color), label);
        }
        finally { dl.PopClipRect(); }
        if (MaterialText.Measure(label).X > max.X - min.X - ImGui.GetStyle().FramePadding.X * 2 && ImGui.IsItemHovered()) MaterialText.SetTooltip(label);
        return open;
    }
    internal static void SameLineIfFits(string label)
    {
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        var width = MaterialText.Measure(UiText.T(label)).X + ImGui.GetStyle().FramePadding.X * 2;
        if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + width <= right) ImGui.SameLine();
    }
    internal static float IconButtonWidth(string label, bool header = false, bool link = false)
        => MaterialText.Measure(UiText.T(label.Split("##", 2)[0])).X + (link ? 32 : 44) * MaterialTheme.Metrics.Scale
            + 2 * MaterialControlMetrics.Measure(MaterialTheme.Metrics, ImGui.GetTextLineHeight(), MaterialControls.Context == MaterialControlContext.Dense ? MaterialControlContext.Dense : MaterialControlContext.Toolbar).NativePadding.X;
    internal static void SameLineIconIfFits(string label, bool header = false)
    {
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + IconButtonWidth(label, header) <= right) ImGui.SameLine();
    }
    internal static bool IconButton(string original, MaterialIcon icon, bool header = false, bool link = false)
    {
        var s = MaterialTheme.Metrics.Scale;
        var label = UiText.T(original.Split("##", 2)[0]);
        using var controls = MaterialControls.Context == MaterialControlContext.Dense
            ? default(MaterialControls.ControlScope) : MaterialControls.Push(MaterialControlContext.Toolbar);
        using var height = MaterialText.PushLineHeight(label);
        var iconSize = (header ? 32 : link ? 22 : 30) * s;
        var padding = ImGui.GetStyle().FramePadding.X;
        var textOffset = (link ? 32 : 44) * s;
        var vertical = MaterialControls.Context == MaterialControlContext.Dense ? BfePresentation.Compact ? 1 : 2 : BfePresentation.Compact ? 2 : 4;
        var size = new Vector2(MaterialLayout.FitNextItemWidth(0, IconButtonWidth(original, header, link)), Math.Max(ImGui.GetFrameHeight(), iconSize + 2 * vertical * s));
        var foreground = link ? MaterialTheme.Current.Colors.Tertiary : ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        if (header || link) ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
        var clicked = ImGui.Button(original, size);
        ImGui.PopStyleColor(header || link ? 2 : 1);
        foreground.W *= ImGui.GetStyle().Alpha;
        var min = ImGui.GetItemRectMin(); var max = ImGui.GetItemRectMax();
        var dl = ImGui.GetWindowDrawList(); dl.PushClipRect(min, max, true);
        if (!header && !link) dl.AddRect(min, max, MaterialCanvas.Color(MaterialTheme.Current.Colors.OutlineVariant), 4 * s);
        var position = min + new Vector2(padding, (size.Y - iconSize) * .5f);
        if (icon == MaterialIcon.None) BfePresentation.Discord(position, iconSize, foreground);
        else MaterialIcons.Draw(icon, position, iconSize, foreground);
        var textPosition = min + new Vector2(padding + textOffset, (size.Y - MaterialText.Measure(label).Y) * .5f);
        MaterialText.AddText(dl, textPosition, MaterialCanvas.Color(foreground), label);
        if (link) dl.AddLine(textPosition + new Vector2(0, MaterialText.Measure(label).Y - s), textPosition + new Vector2(MaterialText.Measure(label).X, MaterialText.Measure(label).Y - s), MaterialCanvas.Color(foreground), s);
        dl.PopClipRect();
        return clicked;
    }
    internal static bool TabItem(string original, MaterialIcon icon, float logicalWidth)
    {
        var s = MaterialTheme.Metrics.Scale;
        var label = UiText.T(original);
        var padding = new Vector2(16, BfePresentation.Compact ? 12 : 16) * s;
        if (MaterialText.RequiresShaping(label)) padding.Y += Math.Max(0, MaterialText.Measure(label).Y - ImGui.GetTextLineHeight()) * .5f;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, padding);
        ImGui.SetNextItemWidth(Math.Max(logicalWidth * s, MaterialText.Measure(label).X + 80 * s));
        ImGui.PushStyleColor(ImGuiCol.TabActive, MaterialTheme.Current.Colors.SurfaceContainer);
        var window = ImGuiP.GetCurrentWindow();
        var priorContentRight = window.DC.CursorMaxPos.X;
        var open = ImGui.BeginTabItem(original);
        // Native tab layout reports the whole scrolling strip as content width.
        // Keep its viewport contribution without discarding any prior body overflow.
        window.DC.CursorMaxPos.X = Math.Max(priorContentRight, Math.Min(window.DC.CursorMaxPos.X, window.WorkRect.Max.X));
        var nativeBackground = ImGui.GetStyle().Colors[(int)(ImGui.IsItemHovered() ? ImGuiCol.TabHovered : open ? ImGuiCol.TabActive : ImGuiCol.Tab)];
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var c = MaterialTheme.Current.Colors;
        var bar = ImGui.GetCurrentContext().CurrentTabBar;
        ImGui.GetWindowDrawList().PushClipRect(new Vector2(Math.Max(min.X, bar.ScrollingRectMinX), min.Y),
            new Vector2(Math.Min(max.X, bar.ScrollingRectMaxX), max.Y), true);
        try
        {
        // Paint over only the retained native caption. Hiding ImGuiCol.Text here
        // also hides the native scroll arrows emitted during the first tab's layout.
        ImGui.GetWindowDrawList().AddRectFilled(min + padding - Vector2.One,
            min + padding + MaterialText.Measure(original) + Vector2.One, MaterialCanvas.Color(nativeBackground));
        MaterialIcons.Draw(icon, min + new Vector2(16 * s, Math.Max(0, (max.Y - min.Y - 24 * s) * .5f)), 24 * s, open ? c.Primary : c.OnSurface);
        MaterialText.AddText(ImGui.GetWindowDrawList(), min + new Vector2(52 * s, Math.Max(0, (max.Y - min.Y - MaterialText.Measure(label).Y) * .5f)),
            MaterialCanvas.Color(open ? c.Primary : c.OnSurface), label);
        ImGui.GetWindowDrawList().AddRect(min, max, MaterialCanvas.Color(open ? c.Primary : c.OutlineVariant), 4 * s);
        }
        finally { ImGui.GetWindowDrawList().PopClipRect(); }
        return open;
    }
    internal static bool SmallButton(string label,string? display=null)
    {
        // Native small buttons use the same ID and behavior with zero vertical padding.
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,new Vector2(ImGui.GetStyle().FramePadding.X,0));
        var clicked=Button(label,display);
        ImGui.PopStyleVar();
        return clicked;
    }
    internal static bool Checkbox(string label,ref bool value)
    {
        var visible=label.Split("##",2)[0];
        var translated=UiText.T(visible);
        using var height = MaterialText.PushLineHeight(translated);
        var foreground=ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        var gap=ImGui.GetStyle().ItemInnerSpacing;
        MaterialLayout.FitNextItemWidth(0, ImGui.GetFrameHeight() + gap.X + Math.Max(MaterialText.Measure(visible).X, MaterialText.Measure(translated).X));
        // Native Checkbox sizes its hit area from the original label. Adjust that size for the
        // translated ink while keeping the native widget and its original ID.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing,new Vector2(Math.Max(0,gap.X+MaterialText.Measure(translated).X-MaterialText.Measure(visible).X),gap.Y));
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);
        var changed=ImGui.Checkbox(label,ref value);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        var p=ImGui.GetItemRectMin()+new Vector2(ImGui.GetFrameHeight()+gap.X,
            (ImGui.GetItemRectMax().Y-ImGui.GetItemRectMin().Y-MaterialText.Measure(translated).Y)*.5f);
        foreground.W*=ImGui.GetStyle().Alpha;
        MaterialText.AddText(ImGui.GetWindowDrawList(), p,ImGui.ColorConvertFloat4ToU32(foreground),translated);
        return changed;
    }
    internal static bool RadioButton(string label, bool selected)
    {
        var translated = UiText.T(label);
        using var height = MaterialText.PushLineHeight(translated);
        var color = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        var gap = ImGui.GetStyle().ItemInnerSpacing;
        MaterialLayout.FitNextItemWidth(0, ImGui.GetFrameHeight() + gap.X + Math.Max(MaterialText.Measure(label).X, MaterialText.Measure(translated).X));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing, new Vector2(Math.Max(0, gap.X + MaterialText.Measure(translated).X - MaterialText.Measure(label).X), gap.Y));
        ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
        var clicked = ImGui.RadioButton(label, selected);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        color.W *= ImGui.GetStyle().Alpha;
        MaterialText.AddText(ImGui.GetWindowDrawList(), ImGui.GetItemRectMin() + new Vector2(ImGui.GetFrameHeight() + gap.X,
            (ImGui.GetItemRectMax().Y-ImGui.GetItemRectMin().Y-MaterialText.Measure(translated).Y)*.5f), ImGui.ColorConvertFloat4ToU32(color), translated);
        return clicked;
    }
    internal static bool Selectable(string original,bool selected,string? display=null)
    {
        var translated=display ?? UiText.T(original.Split("##",2)[0]);
        var origin=ImGui.GetCursorScreenPos();
        var width=ImGui.GetContentRegionAvail().X;
        var foreground=ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);
        var rowHeight = Math.Max(ImGui.GetTextLineHeight(), MaterialText.Measure(translated).Y);
        var clicked=ImGui.Selectable(original,selected,ImGuiSelectableFlags.None,new Vector2(0,rowHeight));
        ImGui.PopStyleColor();
        foreground.W*=ImGui.GetStyle().Alpha;
        var dl=ImGui.GetWindowDrawList();
        dl.PushClipRect(origin,origin+new Vector2(width,rowHeight),true);
        try
        {
        MaterialText.AddText(dl, origin,ImGui.ColorConvertFloat4ToU32(foreground),translated);
        }
        finally { dl.PopClipRect(); }
        if(MaterialText.Measure(translated).X>width && ImGui.IsItemHovered()) MaterialText.SetTooltip(translated);
        return clicked;
    }
    private static void BeginField(string label, float minimum = 0)
    {
        var requested = ImGui.CalcItemWidth();
        minimum = Math.Max(minimum, Math.Max(80 * MaterialTheme.Metrics.Scale, MaterialText.Measure("00000000").X + 2 * ImGui.GetStyle().FramePadding.X));
        var visible = label.Split("##", 2)[0];
        if (visible.Length != 0) MaterialText.Text(UiText.T(visible));
        ImGui.SetNextItemWidth(MaterialLayout.FitNextItemWidth(requested, MathF.Ceiling(minimum)));
        // An empty label hashes to this native ID and contributes no hidden English width.
        ImGuiP.PushOverrideID(ImGui.GetID(label));
    }
    internal static bool InputText(string label,ref string value,int length)
    {
        BeginField(label);
        try
        {
            using var height = MaterialText.PushLineHeight(value);
            return MaterialShapedInput.SingleLine("", "", ref value, length);
        }
        finally { ImGui.PopID(); }
    }
    internal static bool InputInt(string label,ref int value) { BeginField(label);var changed=ImGui.InputInt("",ref value); ImGui.PopID(); return changed; }
    internal static bool Combo(string label,ref int value,string[] options,int count)
    {
        var minimum = options.Take(count).Max(option => MaterialText.Measure(UiText.T(option)).X) + ImGui.GetFrameHeight() + 2 * ImGui.GetStyle().FramePadding.X;
        BeginField(label, minimum);
        var changed=false;
        if(MaterialText.BeginCombo("",value>=0 && value<count?UiText.T(options[value]):""))
        {
            for(var index=0;index<count;index++)
            {
                ImGui.PushID(index);
                if(Selectable(options[index],value==index)) { changed=value!=index;value=index; }
                if(value==index) ImGui.SetItemDefaultFocus();
                ImGui.PopID();
            }
            ImGui.EndCombo();
        }
        ImGui.PopID(); return changed;
    }
    internal static void PaintTitleWithImage(Window owner, string display)
    {
        var window = ImGuiP.FindWindowByName(owner.WindowName);
        if (window.IsNull) return;
        var count = owner.TitleBarButtons.Count(button => !owner.IsClickthrough || button.AvailableClickthrough);
        if (owner.AllowPinning || owner.AllowClickthrough || owner.AllowBackgroundBlur) count++;
        var extraRightWidth = count * (ImGuiP.CalcFontSize(window) + ImGui.GetStyle().ItemInnerSpacing.X);
        var texture = BfePresentation.OriginalIcon;
        using var font = UiText.Font(UiFontRole.Body);
        MaterialWindowHeader.PaintTitle(window, display, texture?.Handle ?? default,
            texture is null ? Vector2.Zero : new Vector2(texture.Width, texture.Height), extraRightWidth, owner.ShowCloseButton);
    }

    internal static void ReserveTitleSpace(Window owner, string visible, float minimumWidth)
    {
        var style = ImGui.GetStyle();
        var fontSize = ImGui.GetFontSize();
        var collapse = (owner.Flags & (ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.Modal)) == 0
            && style.WindowMenuButtonPosition != ImGuiDir.None;
        var count = owner.TitleBarButtons.Count(button => !owner.IsClickthrough || button.AvailableClickthrough);
        if (owner.AllowPinning || owner.AllowClickthrough || owner.AllowBackgroundBlur) count++;
        var controls = (count + (owner.ShowCloseButton ? 1 : 0) + (collapse ? 1 : 0)) * (fontSize + style.ItemInnerSpacing.X);
        var required = (MaterialText.Measure(visible).X + fontSize + style.ItemInnerSpacing.X
            + controls + style.FramePadding.X * 2 + style.ItemInnerSpacing.X)
            / ImGui.GetIO().FontGlobalScale;
        var bounds = owner.SizeConstraints ?? new WindowSizeConstraints();
        bounds.MinimumSize = new(Math.Max(minimumWidth, required), bounds.MinimumSize.Y);
        owner.SizeConstraints = bounds;
    }

    internal static void Title(string original,string translated)
    {
        var s=ImGui.GetStyle(); var size=ImGui.GetFontSize();var height=ImGui.GetFrameHeight();
        var flags=ImGuiP.GetCurrentWindow().Flags;
        var collapseOnLeft=(flags & (ImGuiWindowFlags.NoCollapse|ImGuiWindowFlags.Modal))==0 && s.WindowMenuButtonPosition==ImGuiDir.Left;
        var position=ImGui.GetWindowPos()+new Vector2(s.FramePadding.X+(collapseOnLeft?size+s.ItemInnerSpacing.X:0),s.FramePadding.Y);
        var originalWidth=MaterialText.Measure(original).X;
        using var font=UiText.Font(UiFontRole.Body);
        var translatedWidth=MaterialText.Measure(translated).X*size/ImGui.GetFontSize();
        if (MaterialText.RequiresShaping(translated))
            position.Y = ImGui.GetWindowPos().Y + Math.Max(0, (height - MaterialText.Measure(translated).Y * size / ImGui.GetFontSize()) * .5f);
        var dl=ImGui.GetWindowDrawList();
        var rightButtons = size + s.FramePadding.X * 2;
        if ((flags & ImGuiWindowFlags.NoCollapse) == 0 && s.WindowMenuButtonPosition == ImGuiDir.Right) rightButtons += size + s.ItemInnerSpacing.X;
        dl.PushClipRect(position,ImGui.GetWindowPos()+new Vector2(Math.Max(0,ImGui.GetWindowSize().X-rightButtons),height),false);
        try
        {
        var bg=s.Colors[(int)(ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)?ImGuiCol.TitleBgActive:ImGuiCol.TitleBg)];
        dl.AddRectFilled(position,position+new Vector2(Math.Max(originalWidth,translatedWidth),height-s.FramePadding.Y),ImGui.ColorConvertFloat4ToU32(bg));
        MaterialText.AddText(dl, ImGui.GetFont(),size,position,ImGui.ColorConvertFloat4ToU32(s.Colors[(int)ImGuiCol.Text]),translated);
        }
        finally { dl.PopClipRect(); }
    }
    internal static void TableHeadersRow(float height=0)
    {
        height = Math.Max(height, Enumerable.Range(0, ImGui.TableGetColumnCount()).Select(index =>
            MaterialText.Measure(UiText.T(ImGui.TableGetColumnName(index))).Y).DefaultIfEmpty(0).Max());
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers,height);
        for(var index=0;index<ImGui.TableGetColumnCount();index++)
        {
            if(!ImGui.TableSetColumnIndex(index)) continue;
            var original=ImGui.TableGetColumnName(index);
            var position=ImGui.GetCursorScreenPos();
            var available=ImGui.GetContentRegionAvail().X;
            ImGui.TableHeader(original);
            Label(original,position,ImGui.GetStyle().Colors[(int)ImGuiCol.TableHeaderBg],ImGui.GetStyle().Colors[(int)ImGuiCol.Text], position + new Vector2(Math.Max(1, available), height));
            var translated=UiText.T(original);
            if(translated!=original && MaterialText.Measure(translated).X>available-16*AethertekUI.MaterialTheme.Metrics.Scale && ImGui.IsItemHovered())
                MaterialText.SetTooltip(translated);
        }
    }
    internal static void CenterColumnText(string text, bool underlined = false)
    {
        if (!MaterialText.RequiresShaping(text)) { ECommons.ImGuiMethods.ImGuiEx.CenterColumnText(text, underlined); return; }
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (ImGui.GetColumnWidth() - MaterialText.Measure(text).X) * .5f);
        MaterialText.Text(text);
        if (underlined) ImGui.GetWindowDrawList().AddLine(
            new Vector2(ImGui.GetItemRectMin().X, ImGui.GetItemRectMax().Y), ImGui.GetItemRectMax(),
            ImGui.GetColorU32(ImGuiCol.Text));
    }
    internal static void CenterColumnText(Vector4 color, string text, bool underlined = false)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        try { CenterColumnText(text, underlined); }
        finally { ImGui.PopStyleColor(); }
    }
}
