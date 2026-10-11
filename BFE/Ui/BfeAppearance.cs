using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace BFE.Ui;

/// <summary>
/// Lightweight appearance and styling manager for bunny automation windows.
/// Text follows the suite-wide language set through Loc.SetLanguage (UiText.ActiveLanguage).
/// </summary>
internal sealed class BfeAppearance
{
    internal void Draw(WindowSystem windows)
    {
        windows.Draw();
    }

    internal string Label(string key) => UiText.T(key);

    internal string Format(string pattern, params object[] args)
    {
        try
        {
            return string.Format(UiText.Current.Culture, UiText.T(pattern), args);
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

    internal void DrawWindowAppearanceSettings()
    {
        DrawTransparencyToggle();
    }
}
