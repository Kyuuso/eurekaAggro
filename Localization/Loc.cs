using System;
using BFE.Ui;

namespace EurekaSuite.Localization;

/// <summary>
/// Suite-wide localization helper bridging the native multi-language translation engine.
/// Translates UI strings, HUD alerts, and notifications into the active user language.
/// </summary>
public static class Loc
{
    public static (string Code, string Name)[] Languages => UiText.Languages;

    public static string CurrentLanguage => UiText.ActiveLanguage;

    public static void SetLanguage(string language)
    {
        UiText.SetActiveLanguage(language);
    }

    public static string T(string english) => UiText.T(english);

    public static string F(string english, params object?[] args) => UiText.F(english, args);

    public static string F(FormattableString text) => UiText.F(text);
}
