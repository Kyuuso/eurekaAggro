using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Text.RegularExpressions;

namespace BFE.Ui;

/// <summary>
/// Typography roles for the user interface.
/// </summary>
internal enum UiFontRole
{
    Body,
    BodyStrong,
    Title,
    PluginName,
    Counter,
    Action,
    CompactTitle
}

/// <summary>
/// Native localization and string formatting system for the bunny automation UI.
/// Loads translated strings directly from embedded assembly resources.
/// </summary>
internal sealed class UiText : IDisposable
{
    [ThreadStatic] private static UiText? current;
    internal static UiText Current => current ?? FallbackInstance;

    private static string activeLanguage = "en";
    internal static string ActiveLanguage => activeLanguage;

    private static UiText? fallbackInstance;
    private static UiText FallbackInstance => fallbackInstance ??= new UiText(activeLanguage, null);

    internal static void SetActiveLanguage(string language)
    {
        activeLanguage = Languages.Any(l => l.Code == language) ? language : "en";
        fallbackInstance?.Dispose();
        fallbackInstance = new UiText(activeLanguage, null);
    }

    internal static readonly (string Code, string Name)[] Languages =
    [
        ("en", "English"),
        ("es", "Español"),
        ("de", "Deutsch"),
        ("fr", "Français"),
        ("it", "Italiano"),
        ("ja", "日本語"),
        ("ko", "한국어"),
        ("zh-Hans", "简体中文"),
        ("pt-BR", "Português (Brasil)"),
        ("ru", "Русский"),
        ("pl", "Polski"),
        ("tr", "Türkçe"),
        ("id", "Bahasa Indonesia"),
        ("vi", "Tiếng Việt"),
        ("hi", "हिन्दी")
    ];

    private readonly ResourceManager manager;
    internal ResourceSet? Resources { get; }
    internal CultureInfo Culture { get; }
    internal string Language { get; }
    private readonly (Regex Pattern, string Key, int ArgumentCount)[] messageTemplates;

    public UiText(string language, Func<UiFontRole, IDisposable>? pushFont = null)
    {
        Language = Languages.Any(l => l.Code == language) ? language : "en";
        Culture = CultureInfo.GetCultureInfo(Language);

        // Load resource manager for selected language
        manager = new ResourceManager("BFE.Localization.Strings_" + Language.Replace('-', '_'), typeof(UiText).Assembly);
        try
        {
            Resources = manager.GetResourceSet(CultureInfo.InvariantCulture, true, false);
        }
        catch
        {
            Resources = null;
        }

        // Compile regex templates for dynamic string formatting substitutions
        var parameter = new Regex(@"\{(\d+)(?::([^}]+))?\}");
        if (Resources != null)
        {
            messageTemplates = Resources.Cast<DictionaryEntry>()
                .Select(entry => (string)entry.Key)
                .Where(key => parameter.IsMatch(key))
                .Select(key =>
                {
                    var pattern = "^";
                    var offset = 0;
                    var holes = parameter.Matches(key);
                    foreach (Match hole in holes)
                    {
                        pattern += Regex.Escape(key[offset..hole.Index].Replace("\r\n", "\n")).Replace(@"\n", @"\r?\n") + $"(?<arg{hole.Groups[1].Value}>.*?)";
                        offset = hole.Index + hole.Length;
                    }
                    pattern += Regex.Escape(key[offset..].Replace("\r\n", "\n")).Replace(@"\n", @"\r?\n") + "$";
                    return (new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(20)), key,
                        holes.Cast<Match>().Max(hole => int.Parse(hole.Groups[1].Value, CultureInfo.InvariantCulture)) + 1);
                }).ToArray();
        }
        else
        {
            messageTemplates = Array.Empty<(Regex, string, int)>();
        }
    }

    /// <summary>
    /// Translates an English string into the currently active UI language.
    /// If no translation exists, returns the original string.
    /// </summary>
    internal static string T(string english)
    {
        if (string.IsNullOrEmpty(english)) return string.Empty;
        if (Current.Language == "en") return english;

        // Normalization of common casing differences
        english = english switch
        {
            "idle" => "Idle",
            "Going to Fc" => "Going to FC",
            _ => english
        };

        if (Current.Resources?.GetString(english, false) is { } exact) return exact;

        var trimmed = english.TrimEnd();
        if (trimmed.Length != english.Length && Current.Resources?.GetString(trimmed, false) is { } label)
            return label + english[trimmed.Length..];

        foreach (var template in Current.messageTemplates)
        {
            var match = template.Pattern.Match(english);
            if (!match.Success) continue;

            var args = Enumerable.Range(0, template.ArgumentCount).Select(index =>
            {
                var value = match.Groups[$"arg{index}"].Value;
                return (object)(template.Key == "Start {0}" && value == "Normal Raid" ? T(value) : value);
            }).ToArray();

            var formatString = Current.Resources?.GetString(template.Key, false);
            if (!string.IsNullOrEmpty(formatString))
                return string.Format(Current.Culture, formatString, args);
        }

        return english;
    }

    /// <summary>
    /// Translates and formats a string with positional arguments.
    /// </summary>
    internal static string F(string english, params object?[] args)
    {
        try
        {
            return string.Format(Current.Culture, T(english), args);
        }
        catch
        {
            return english;
        }
    }

    /// <summary>
    /// Translates and formats an interpolated string with localized parameter values.
    /// </summary>
    internal static string F(FormattableString text)
    {
        try
        {
            return string.Format(Current.Culture, T(text.Format),
                text.GetArguments().Select(value => value is string s && s is "Yes" or "No" or "Y" or "N" or "On" or "Off" or "Forced / On" or "Override Off" or "Interactive" or "Passive" ? T(s) : value).ToArray());
        }
        catch
        {
            return text.ToString();
        }
    }

    internal string Format(string key, params object?[] args)
    {
        try
        {
            return string.Format(Culture, Resources?.GetString(key, false) ?? key, args);
        }
        catch
        {
            return key;
        }
    }

    internal string Label(string key) => Resources?.GetString(key, false) ?? key;

    /// <summary>
    /// No-op scope token for font stacking compatibility.
    /// </summary>
    private sealed class EmptyScope : IDisposable
    {
        public static readonly EmptyScope Instance = new();
        public void Dispose() { }
    }

    internal static IDisposable Font(UiFontRole role) => EmptyScope.Instance;

    internal Scope Enter() => new(this);

    internal readonly struct Scope : IDisposable
    {
        private readonly UiText? previous;
        internal Scope(UiText value) { previous = current; current = value; }
        public void Dispose() => current = previous;
    }

    internal static string Date(DateTimeOffset? date) => date?.ToLocalTime().ToString("g", Current.Culture) ?? T("Never");

    public void Dispose()
    {
        try
        {
            manager.ReleaseAllResources();
        }
        catch { }
    }
}
