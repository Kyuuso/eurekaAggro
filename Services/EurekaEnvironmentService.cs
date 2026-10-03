using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace EurekaAggro.Services;

/// <summary>
/// Status and trigger conditions for monster mutations and adaptations in Eureka.
/// </summary>
public record struct MutationInfo(
    bool CanMutate,
    string TriggerName,
    bool IsActiveNow,
    string HintMessage
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
    /// Checks if a monster can mutate or adapt, and evaluates whether current environmental conditions trigger it.
    /// </summary>
    public MutationInfo CheckMutation(string mobName)
    {
        if (string.IsNullOrWhiteSpace(mobName))
        {
            return default;
        }

        foreach (var (key, (trigger, isTimeBased)) in MutationTriggers)
        {
            if (mobName.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                bool isActive;
                string hint;

                if (isTimeBased)
                {
                    bool night = IsNight();
                    isActive = trigger.Equals("Night", StringComparison.OrdinalIgnoreCase) ? night : !night;
                    hint = isActive ? "Night (Active)" : "Requires Night (18:00 ET)";
                }
                else
                {
                    string currentWeather = GetCurrentWeatherName();
                    // Some mobs mutate in either Fog or Blizzards
                    if (trigger.Contains("/"))
                    {
                        var parts = trigger.Split('/');
                        isActive = false;
                        foreach (var p in parts)
                        {
                            if (currentWeather.Contains(p.Trim(), StringComparison.OrdinalIgnoreCase))
                            {
                                isActive = true;
                                break;
                            }
                        }
                    }
                    else
                    {
                        isActive = currentWeather.Contains(trigger, StringComparison.OrdinalIgnoreCase);
                    }

                    hint = isActive ? $"{trigger} (Active)" : $"Requires {trigger}";
                }

                return new MutationInfo(
                    CanMutate: true,
                    TriggerName: trigger,
                    IsActiveNow: isActive,
                    HintMessage: hint
                );
            }
        }

        return default;
    }
}
