using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace EurekaSuite.Services;

/// <summary>
/// Status and trigger conditions for monster mutations and adaptations in Eureka.
/// </summary>
public record struct MutationInfo(
    bool CanMutate,
    string TriggerName,
    bool IsActiveNow,
    string HintMessage,
    string OverlayText
);

/// <summary>
/// Provides environmental tracking for Eureka (real-time weather, Eorzea time, and monster mutation status).
/// </summary>
public class EurekaEnvironmentService
{
    private readonly IDataManager dataManager;
    private readonly IClientState clientState;

    // Cache of weather names by WeatherId to avoid lookups
    private readonly Dictionary<byte, string> weatherNameCache = new();

    // English weather names, used to match mutation triggers regardless of the client language
    private readonly Dictionary<byte, string> englishWeatherNameCache = new();

    // Resolved mutation rule per monster name (null when the monster never mutates)
    private readonly Dictionary<string, MutationRule?> ruleByMobName = new(StringComparer.Ordinal);

    private sealed class MutationRule
    {
        public string Trigger = string.Empty;
        public bool IsTimeBased;
        public string[] WeatherOptions = Array.Empty<string>();
        public string ActiveHint = string.Empty;
        public string InactiveHint = string.Empty;
        public string ActiveOverlay = string.Empty;
        public string InactiveOverlay = string.Empty;
    }

    // Known Eureka mutation and adaptation triggers by monster keyword
    // Key: Lowercase keyword/species; Value: (Required Weather or "Night" / "Day")
    private static readonly Dictionary<string, (string Trigger, bool IsTimeBased)> MutationTriggers = new(StringComparer.OrdinalIgnoreCase)
    {
        // --- PAGOS MUTATIONS ---
        { "pagos chimera", ("Night", true) },
        { "pagos anala", ("Fog", false) },
        { "pagos billygoat", ("Fog", false) },
        { "pagos garm", ("Night", true) },
        { "pagos morbol", ("Fog / Blizzards", false) },
        { "pagos minotaur", ("Night", true) },
        { "pagos harpuia", ("Fog", false) },
        { "pagos worm", ("Fair Skies", false) },
        { "pagos griffin", ("Fair Skies", false) },
        { "pagos wolf", ("Night", true) },
        { "pagos gazelle", ("Fair Skies", false) },
        { "pagos ogrebon", ("Fog", false) },
        { "pagos gouger", ("Fog", false) },
        { "pagos manticore", ("Night", true) },
        { "pagos taurus", ("Night", true) },
        { "pagos ameretat", ("Fog", false) },

        // --- PYROS MUTATIONS ---
        { "pyros crab", ("Fog", false) },
        { "pyros billygoat", ("Heat Waves", false) },
        { "pyros slime", ("Night", true) },
        { "pyros dhalmel", ("Heat Waves", false) },
        { "pyros chimera", ("Night", true) },
        { "pyros tomahawk", ("Heat Waves", false) },
        { "pyros vulture", ("Fair Skies", false) },
        { "thunder drake", ("Thunder", false) },
        { "val boar", ("Fair Skies", false) },
        { "val hecteyes", ("Night", true) },
        { "pyros centaur", ("Blizzards", false) },
        { "pyros skatene", ("Fog", false) },
        { "pyros piranu", ("Fog", false) },
        { "pyros flutur", ("Night", true) },
        { "pyros treant", ("Fog", false) },
        { "pyros wolf", ("Night", true) },
        { "pyros manticore", ("Night", true) },
        { "hakutaku", ("Heat Waves", false) },
        { "pyros defoliator", ("Fog", false) },

        // --- HYDATOS MUTATIONS ---
        { "hydatos morbol", ("Night", true) },
        { "hydatos ziz", ("Showers", false) },
        { "hydatos tiger", ("Night", true) },
        { "hydatos peiste", ("Gloom", false) },
        { "hydatos wraith", ("Night", true) },
        { "hydatos crawler", ("Showers", false) },
        { "hydatos golem", ("Thunderstorms", false) },
        { "hydatos dullahan", ("Night", true) }
    };

    public EurekaEnvironmentService(IDataManager dataManager, IClientState clientState)
    {
        this.dataManager = dataManager;
        this.clientState = clientState;
    }

    /// <summary>
    /// Computes the current Eorzea Time hour (0 to 23).
    /// </summary>
    public static int GetEorzeaHour()
    {
        long eorzeaSeconds = (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds * 144 / 7;
        return (int)((eorzeaSeconds / 3600) % 24);
    }

    /// <summary>
    /// Checks whether it is currently nighttime in Eorzea (18:00 - 06:00 ET).
    /// </summary>
    public static bool IsNight()
    {
        int hour = GetEorzeaHour();
        return hour < 6 || hour >= 18;
    }

    /// <summary>
    /// Reads the active Weather ID directly from game memory.
    /// </summary>
    public unsafe byte GetCurrentWeatherId()
    {
        try
        {
            var wm = WeatherManager.Instance();
            if (wm != null)
            {
                return wm->GetCurrentWeather();
            }
        }
        catch
        {
            // Ignore during zone transitions
        }

        return 0;
    }

    /// <summary>
    /// Resolves the human-readable localized name of the active weather.
    /// </summary>
    public string GetCurrentWeatherName()
    {
        byte id = GetCurrentWeatherId();
        if (id == 0) return "Unknown";

        if (weatherNameCache.TryGetValue(id, out var cachedName))
        {
            return cachedName;
        }

        try
        {
            var sheet = dataManager.GetExcelSheet<Weather>();
            if (sheet != null && sheet.TryGetRow(id, out var row))
            {
                var name = row.Name.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    weatherNameCache[id] = name;
                    return name;
                }
            }
        }
        catch
        {
            // Fallback
        }

        return "Unknown";
    }

    /// <summary>
    /// Resolves the English name of the active weather. Mutation triggers are defined in English,
    /// so comparing against the localized name would never match on non-English clients.
    /// </summary>
    private string GetCurrentWeatherEnglishName()
    {
        byte id = GetCurrentWeatherId();
        if (id == 0) return string.Empty;

        if (englishWeatherNameCache.TryGetValue(id, out var cachedName))
        {
            return cachedName;
        }

        var name = string.Empty;
        try
        {
            var sheet = dataManager.GetExcelSheet<Weather>(Dalamud.Game.ClientLanguage.English);
            if (sheet != null && sheet.TryGetRow(id, out var row))
            {
                name = row.Name.ToString();
            }
        }
        catch
        {
            // Fallback
        }

        englishWeatherNameCache[id] = name;
        return name;
    }

    /// <summary>
    /// Checks if a monster can mutate or adapt, and evaluates whether current environmental conditions trigger it.
    /// Called per monster per frame: rule lookup and hint strings are cached so this does not allocate.
    /// </summary>
    public MutationInfo CheckMutation(string mobName)
    {
        if (string.IsNullOrWhiteSpace(mobName))
        {
            return default;
        }

        if (!ruleByMobName.TryGetValue(mobName, out var rule))
        {
            rule = FindMutationRule(mobName);
            ruleByMobName[mobName] = rule;
        }

        if (rule == null)
        {
            return default;
        }

        bool isActive;
        if (rule.IsTimeBased)
        {
            bool night = IsNight();
            isActive = rule.Trigger.Equals("Night", StringComparison.OrdinalIgnoreCase) ? night : !night;
        }
        else
        {
            // Some mobs mutate in either of several weathers (e.g. Fog or Blizzards)
            string currentWeather = GetCurrentWeatherEnglishName();
            isActive = false;
            if (currentWeather.Length > 0)
            {
                foreach (var option in rule.WeatherOptions)
                {
                    if (currentWeather.Contains(option, StringComparison.OrdinalIgnoreCase))
                    {
                        isActive = true;
                        break;
                    }
                }
            }
        }

        return new MutationInfo(
            CanMutate: true,
            TriggerName: rule.Trigger,
            IsActiveNow: isActive,
            HintMessage: isActive ? rule.ActiveHint : rule.InactiveHint,
            OverlayText: isActive ? rule.ActiveOverlay : rule.InactiveOverlay
        );
    }

    private static MutationRule? FindMutationRule(string mobName)
    {
        foreach (var (key, (trigger, isTimeBased)) in MutationTriggers)
        {
            if (!mobName.Contains(key, StringComparison.OrdinalIgnoreCase)) continue;

            var activeHint = isTimeBased ? "Night (Active)" : $"{trigger} (Active)";
            var inactiveHint = isTimeBased ? "Requires Night (18:00 ET)" : $"Requires {trigger}";
            var options = trigger.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return new MutationRule
            {
                Trigger = trigger,
                IsTimeBased = isTimeBased,
                WeatherOptions = options,
                ActiveHint = activeHint,
                InactiveHint = inactiveHint,
                ActiveOverlay = $"[CAN MUTATE NOW]: {activeHint}",
                InactiveOverlay = $"[Mutates: {inactiveHint}]",
            };
        }

        return null;
    }
}
