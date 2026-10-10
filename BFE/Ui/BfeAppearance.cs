using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace BFE.Ui;

/// <summary>
/// Gestor visual ligero para la apariencia de las ventanas de conejos.
/// Soporta cambio de idioma y renderizado directo mediante Dalamud WindowSystem.
/// </summary>
internal sealed class BfeAppearance : IDisposable
{
    private UiText text;
    private string appliedLanguage = "";

    internal float SelectorWidth => 120f;

    internal BfeAppearance(ITextureProvider textures)
    {
        text = new UiText("es", null);
        appliedLanguage = "es";
    }

    private void Apply()
    {
        var lang = C.UiLanguage ?? "es";
        if (lang != appliedLanguage)
        {
            text.Dispose();
            text = new UiText(lang, null);
            appliedLanguage = lang;
        }
    }

    internal void Draw(WindowSystem windows)
    {
        Apply();
        using var scope = text.Enter();
        windows.Draw();
    }

    internal string Label(string key) => UiText.T(key);

    internal string Format(string pattern, params object[] args)
    {
        try
        {
            return string.Format(text.Culture, UiText.T(pattern), args);
        }
        catch
        {
            return pattern;
        }
    }

    internal void DrawTransparencyToggle()
    {
        var transparent = C.UiTransparencyEnabled;
        if (ImGui.Checkbox(UiText.T("Transparent Window") + "###UiTransparent", ref transparent))
        {
            C.UiTransparencyEnabled = transparent;
            C.Save();
        }
    }

    internal void DrawSelector(bool compact)
    {
        var currentLang = C.UiLanguage ?? "es";
        var langs = UiText.Languages;
        var currentIndex = Array.FindIndex(langs, l => l.Code == currentLang);
        if (currentIndex < 0) currentIndex = 0;

        ImGui.SetNextItemWidth(120f);
        if (ImGui.Combo("###UiLanguageSelector", ref currentIndex, langs.Select(l => l.Name).ToArray(), langs.Length))
        {
            C.UiLanguage = langs[currentIndex].Code;
            C.Save();
        }
    }

    internal void DrawWindowAppearanceSettings()
    {
        ImGui.TextUnformatted(UiText.T("Language Selection:"));
        DrawSelector(false);

        ImGui.Spacing();
        DrawTransparencyToggle();
    }

    public void Dispose()
    {
        text?.Dispose();
    }
}
