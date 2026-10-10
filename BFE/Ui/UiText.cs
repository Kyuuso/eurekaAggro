using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Resources;
using System.Text.RegularExpressions;
using AethertekUI;
using Dalamud.Bindings.ImGui;

namespace BFE.Ui;

internal sealed class UiText : IDisposable
{
    [ThreadStatic] private static UiText? current;
    internal static UiText Current => current ?? throw new InvalidOperationException("Enter the BFE UI frame before drawing.");
    internal static readonly (string Code,string Name)[] Languages=[("en","English"),("de","Deutsch"),("fr","Français"),
        ("es","Español"),("it","Italiano"),("ru","Русский"),("ja","日本語"),("ko","한국어"),("zh-Hans","简体中文"),
        ("vi","Tiếng Việt"),("pt-BR","Português (Brasil)"),("id","Bahasa Indonesia"),("pl","Polski"),("tr","Türkçe"),("hi","हिन्दी")];
    internal static IEnumerable<string> CjkLanguages(string selected) => new[]{"ja","ko","zh-Hans"}.OrderBy(code=>code==selected?0:1);
    private readonly ResourceManager manager;
    internal ResourceSet Resources { get; }
    internal IReadOnlyList<string> RequiredText { get; }
    internal CultureInfo Culture { get; }
    internal string Language { get; }
    private readonly Func<UiFontRole,IDisposable> pushFont;
    private readonly (Regex Pattern, string Key, int ArgumentCount)[] messageTemplates;
    internal UiText(string language, Func<UiFontRole,IDisposable> pushFont)
    {
        Language=Languages.Any(l=>l.Code==language)?language:"en";
        Culture=CultureInfo.GetCultureInfo(Language);
        manager=new ResourceManager("BFE.Localization.Strings_"+Language.Replace('-','_'),typeof(UiText).Assembly);
        Resources=manager.GetResourceSet(CultureInfo.InvariantCulture,true,false) ?? throw new MissingManifestResourceException(Language);
        this.pushFont=pushFont;
        var english=new ResourceManager("BFE.Localization.Strings_en",typeof(UiText).Assembly);
        try
        {
            var fallback=english.GetResourceSet(CultureInfo.InvariantCulture,true,false) ?? throw new MissingManifestResourceException("en");
            RequiredText=Values(Resources).Concat(Values(fallback)).Concat(Languages.Where(l => l.Code != "hi").Select(l=>l.Name)).Append("\u2661").Distinct().ToArray();
        }
        finally { english.ReleaseAllResources(); }
        // Service messages remain English in logs; only their UI copies are localized.
        var parameter = new Regex(@"\{(\d+)(?::([^}]+))?\}");
        messageTemplates = Resources.Cast<DictionaryEntry>().Select(entry => (string)entry.Key)
            .Where(key => parameter.IsMatch(key)).Select(key =>
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
    internal static string T(string english)
    {
        if (Current.Language == "en") return english;
        english = english switch { "idle" => "Idle", "Going to Fc" => "Going to FC", _ => english };
        if (Current.Resources.GetString(english, false) is { } exact) return exact;
        var trimmed = english.TrimEnd();
        if (trimmed.Length != english.Length && Current.Resources.GetString(trimmed, false) is { } label)
            return label + english[trimmed.Length..];
        foreach (var template in Current.messageTemplates)
        {
            var match = template.Pattern.Match(english);
            if (!match.Success) continue;
            // Service captures are already formatted; preserve IDs and leading zeroes.
            var args = Enumerable.Range(0, template.ArgumentCount).Select(index =>
            {
                var value = match.Groups[$"arg{index}"].Value;
                return (object)(template.Key == "Start {0}" && value == "Normal Raid" ? T(value) : value);
            }).ToArray();
            return string.Format(Current.Culture, Current.Resources.GetString(template.Key, false)!, args);
        }
        return english; // Names, game data and raw diagnostic values are consumer data.
    }
    internal static string F(string english,params object?[] args) => string.Format(Current.Culture,T(english),args);
    internal static string F(FormattableString text) => string.Format(Current.Culture, T(text.Format),
        text.GetArguments().Select(value => value is string s && s is "Yes" or "No" or "Y" or "N" or "On" or "Off" or "Forced / On" or "Override Off" or "Interactive" or "Passive" ? T(s) : value).ToArray());
    internal string Format(string key, params object?[] args) => string.Format(Culture, Resources.GetString(key, false) ?? key, args);
    internal string Label(string key) => Resources.GetString(key, false) ?? key;
    internal static IDisposable Font(UiFontRole role) => Current.pushFont(role);
    internal Scope Enter() => new(this);
    internal readonly struct Scope : IDisposable
    {
        private readonly UiText? previous;
        internal Scope(UiText value) { previous=current; current=value; }
        public void Dispose() => current=previous;
    }
    internal static string Date(DateTimeOffset? date) => date?.ToLocalTime().ToString("g",Current.Culture) ?? T("Never");
    internal ushort[] GlyphRanges()
    {
        var chars=RequiredText.SelectMany(MaterialText.NativeGlyphText).Where(c=>!char.IsControl(c))
            .Concat(Enumerable.Range(0x20,0x024F-0x20+1).Select(i=>(char)i))
            .Concat(Enumerable.Range(0x0400,0x052F-0x0400+1).Select(i=>(char)i)).Concat("—").Distinct().Order().ToArray();
        var result=new List<ushort>();
        for(var index=0;index<chars.Length;index++)
        {
            var first=chars[index]; var last=first;
            while(index+1<chars.Length && chars[index+1]==last+1) last=chars[++index];
            result.Add(first); result.Add(last);
        }
        result.Add(0); return result.ToArray();
    }
    internal static IEnumerable<string> Values(ResourceSet set) => set.Cast<DictionaryEntry>().Select(e=>(string)e.Value!);
    public void Dispose() => manager.ReleaseAllResources();
}
