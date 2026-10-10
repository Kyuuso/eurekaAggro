using System.Numerics;
using AethertekUI;
using AethertekUI.Dalamud;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;

namespace BFE.Ui;

internal sealed class BfeAppearance : IDisposable
{
    private readonly MaterialTextHost shapedText;
    private UiText text;
    private BfeFonts fonts;
    private MaterialTheme theme;
    private readonly MaterialWindowFold fontStatusMotion = new();
    private readonly MaterialWindowDecorations fontStatusDecorations = new();
    private readonly Dictionary<string, MaterialWindowOpacity> windowOpacities = new();
    private readonly MaterialOptions<string> languages = new(UiText.Languages.Select(l => new MaterialOption<string>(l.Code, l.Code, l.Name)).ToArray());
    private string appliedLanguage = "";
    private uint appliedAccent;
    private Vector3 accentDraft;
    private int checkedGeneration = -1;
    private bool fontIssueLogged;

    internal BfeAppearance(Dalamud.Plugin.Services.ITextureProvider textures) => shapedText = new(textures);

    private void Apply()
    {
        var language = UiText.Languages.Any(l => l.Code == C.UiLanguage) ? C.UiLanguage : "en";
        if (language != appliedLanguage)
        {
            fonts?.Dispose();
            text?.Dispose();
            text = new(language, role => fonts.Push(role));
            fonts = new(Svc.PluginInterface.UiBuilder.FontAtlas, text.GlyphRanges(), language);
            appliedLanguage = language;
            checkedGeneration = -1;
            fontIssueLogged = false;
        }
        if (theme is null || appliedAccent != (C.UiAccentRgb & 0xFFFFFF))
        {
            appliedAccent = C.UiAccentRgb & 0xFFFFFF;
            theme = BfePresentation.Theme(appliedAccent);
            var rgb = BfePresentation.Rgb(appliedAccent);
            accentDraft = new(rgb.X, rgb.Y, rgb.Z);
        }
        theme.Density = C.UiCompact ? MaterialDensity.Compact : MaterialDensity.Standard;
    }

    internal void Draw(WindowSystem windows)
    {
        Apply();
        if (!windows.Windows.Any(window => window.IsOpen)) return;
        using var resources = text.Enter();
        using var shaping = shapedText.Push();
        if (fonts.Ready && checkedGeneration != fonts.Generation)
        {
            try
            {
                var generation = fonts.Generation;
                foreach (var size in BfePresentation.FontSizes.Distinct())
                    shapedText.Renderer.CheckGlyphs(text.RequiredText, size * ImGuiHelpers.GlobalScale);
                fonts.CheckGlyphs(text.RequiredText);
                var hindiLabel = UiText.Languages.Single(l => l.Code == "hi").Name;
                var hindiAvailable = true;
                foreach (var size in BfePresentation.FontSizes.Distinct())
                    hindiAvailable &= shapedText.Renderer.TryCheckGlyphs([hindiLabel], size * ImGuiHelpers.GlobalScale, out _);
                languages.Replace(UiText.Languages.Select(l => new MaterialOption<string>(l.Code, l.Code,
                    l.Code == "hi" && !hindiAvailable ? "Hindi (unavailable)" : l.Name,
                    l.Code == "hi" && !hindiAvailable)).ToArray());
                checkedGeneration = generation;
            }
            catch (Exception ex)
            {
                if (!fontIssueLogged) { Svc.Log.Error(ex, "[BFE] Required UI glyph coverage failed."); fontIssueLogged = true; }
            }
        }
        using var palette = MaterialTheme.Push(theme, ImGuiHelpers.GlobalScale, MaterialStyleMode.ColorsOnly);
        using var chrome = MaterialWindowChrome.Push();
        if (!fonts.Ready || checkedGeneration != fonts.Generation)
        {
            if (!fontIssueLogged && fonts.LoadException is { } error) { Svc.Log.Error(error, "[BFE] Required UI fonts failed to load."); fontIssueLogged = true; }
            ImGui.SetNextWindowSize(new Vector2(460 * ImGuiHelpers.GlobalScale, 0));
            fontStatusMotion.PreDraw("BFE##FontStatus", null, null, reducedMotion: false, prepareDecorations: fontStatusDecorations.Prepare);
            if (ImGui.Begin("BFE##FontStatus", ImGuiWindowFlags.AlwaysAutoResize))
            {
                fontStatusDecorations.Paint();
                var failed = fonts.LoadException is not null || fontIssueLogged;
                ImGui.TextWrapped(appliedLanguage == "hi" && failed ? "Hindi UI fonts are unavailable. Use English to continue."
                    : failed ? "UI fonts failed to load. See the plugin log." : "Loading UI fonts...");
                if (appliedLanguage == "hi" && failed && ImGui.Button("Use English"))
                {
                    C.UiLanguage = "en";
                    C.Save();
                }
            }
            ImGui.End();
            fontStatusDecorations.Paint();
            fontStatusMotion.PostDraw();
            ApplyWindowOpacity("BFE##FontStatus");
            return;
        }
        using var style = new MaterialStyleScope();
        var s = ImGuiHelpers.GlobalScale;
        style.Style(ImGuiStyleVar.WindowPadding, new Vector2(C.UiCompact ? 12 : 20) * s);
        style.Style(ImGuiStyleVar.ItemSpacing, new Vector2(C.UiCompact ? 8 : 12, C.UiCompact ? 5 : 10) * s);
        style.Style(ImGuiStyleVar.FramePadding, new Vector2(C.UiCompact ? 10 : 14, C.UiCompact ? 4 : 7) * s);
        style.Style(ImGuiStyleVar.CellPadding, new Vector2(C.UiCompact ? 6 : 10, C.UiCompact ? 4 : 8) * s);
        style.Style(ImGuiStyleVar.FrameRounding, 4 * s);
        style.Style(ImGuiStyleVar.ChildRounding, 4 * s);
        using var body = fonts.Push(UiFontRole.Body);
        windows.Draw();
        foreach (var window in windows.Windows)
            if (window.IsOpen) ApplyWindowOpacity(window.WindowName);
    }

    internal float SelectorWidth
    {
        get
        {
            var metrics = BfePresentation.Controls(BfePresentation.Compact ? 44 : 52);
            return Math.Max((BfePresentation.Compact ? 186 : 202) * MaterialTheme.Metrics.Scale, MathF.Ceiling(MaterialText.Measure(languages.LabelFor(appliedLanguage, "Select...")).X
                    + metrics.Height + 3 * metrics.Gap + Math.Min(metrics.IconSize, metrics.Height)));
        }
    }

    internal void DrawSelector(bool includeAccent = true)
    {
        var language = appliedLanguage;
        using var controls = MaterialControls.Push(BfePresentation.Controls(BfePresentation.Compact ? 44 : 52));
        var accentChanged = false;
        if (includeAccent)
        {
            accentChanged = MaterialAppearanceSelector.DrawAccent("appearance", ref accentDraft,
                new(UiText.T("Color"), UiText.T("Language"), UiText.T("Teal"), UiText.T("Blue"), UiText.T("Pink"), UiText.T("Custom RGB")), BfePresentation.Compact ? 35 : 37);
            ImGui.SameLine();
        }
        var languageChanged = MaterialAppearanceSelector.DrawLanguage("appearance", ref language, languages, BfePresentation.Compact ? 186 : 202);
        var changed = new MaterialAppearanceChange(accentChanged, languageChanged);
        if (changed.AccentChanged)
            C.UiAccentRgb = ((uint)Math.Clamp((int)MathF.Round(accentDraft.X * 255), 0, 255) << 16)
                | ((uint)Math.Clamp((int)MathF.Round(accentDraft.Y * 255), 0, 255) << 8) | (uint)Math.Clamp((int)MathF.Round(accentDraft.Z * 255), 0, 255);
        if (changed.LanguageChanged) C.UiLanguage = language;
        if (changed.AccentChanged || changed.LanguageChanged) C.Save();
    }

    internal BfeAppearance() => Apply();
    internal string Label(string key) => text.Label(key);
    internal string Format(string key, params object?[] arguments) => text.Format(key, arguments);

    public void Dispose() { fonts?.Dispose(); text?.Dispose(); shapedText.Dispose(); }

    private void ApplyWindowOpacity(string windowName)
    {
        if (!windowOpacities.TryGetValue(windowName, out var opacity))
            windowOpacities.Add(windowName, opacity = new MaterialWindowOpacity());
        opacity.Apply(windowName, C.UiWindowOpacityPercent / 100f,
            C.UiTransparencyEnabled, C.UiAutoFade,
            C.UiFadedOpacityPercent / 100f, C.UiUnfocusedDelaySeconds);
    }

    internal void DrawTransparencyToggle()
    {
        var enabled = C.UiTransparencyEnabled;
        if (UiGui.Checkbox("Transparency##MainWindow", ref enabled))
        { C.UiTransparencyEnabled = enabled; C.Save(); }
    }

    internal void DrawWindowAppearanceSettings()
    {
        if (!UiGui.CollapsingHeader("Window appearance###UiWindowAppearance")) return;
        var compact = C.UiCompact;
        if (UiGui.Checkbox("Compact mode", ref compact))
        { C.UiCompact = compact; C.Save(); }
        DrawSelector();
        var compactVisible = C.UiCompactVisibleOnMainWindow;
        if (UiGui.Checkbox("Compact visible on main window", ref compactVisible))
        { C.UiCompactVisibleOnMainWindow = compactVisible; C.Save(); }
        var languageVisible = C.UiLanguageVisibleOnMainWindow;
        if (UiGui.Checkbox("Language visible on main window", ref languageVisible))
        { C.UiLanguageVisibleOnMainWindow = languageVisible; C.Save(); }
        var enabled = C.UiTransparencyEnabled;
        if (UiGui.Checkbox("Transparency", ref enabled))
        { C.UiTransparencyEnabled = enabled; C.Save(); }
        MaterialText.Text(UiText.T("Opacity (%)"));
        ImGui.SetNextItemWidth(MaterialLayout.FitNextItemWidth(160 * MaterialTheme.Metrics.Scale, 80 * MaterialTheme.Metrics.Scale));
        var normalOpacity = C.UiWindowOpacityPercent;
        if (ImGui.InputInt("##UiWindowOpacityPercent", ref normalOpacity))
        { C.UiWindowOpacityPercent = normalOpacity; C.Save(); }
        var autoFade = C.UiAutoFade;
        if (UiGui.Checkbox("Auto-fade when unfocused", ref autoFade))
        { C.UiAutoFade = autoFade; C.Save(); }
        ImGui.BeginDisabled(!autoFade);
        MaterialText.Text(UiText.T("Unfocused opacity (%)"));
        ImGui.SetNextItemWidth(MaterialLayout.FitNextItemWidth(160 * MaterialTheme.Metrics.Scale, 80 * MaterialTheme.Metrics.Scale));
        var fadedOpacity = C.UiFadedOpacityPercent;
        if (ImGui.InputInt("##UiFadedOpacityPercent", ref fadedOpacity))
        { C.UiFadedOpacityPercent = fadedOpacity; C.Save(); }
        MaterialText.Text(UiText.T("Unfocused delay (seconds)"));
        ImGui.SetNextItemWidth(MaterialLayout.FitNextItemWidth(160 * MaterialTheme.Metrics.Scale, 80 * MaterialTheme.Metrics.Scale));
        var delay = C.UiUnfocusedDelaySeconds;
        if (ImGui.InputInt("##UiUnfocusedDelaySeconds", ref delay))
        { C.UiUnfocusedDelaySeconds = delay; C.Save(); }
        ImGui.EndDisabled();
    }
}
