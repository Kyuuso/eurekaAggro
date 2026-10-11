using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace EurekaSuite.Tracker.Model;

/// <summary>
/// Data definition for a Notorious Monster (NM) / FATE in Eureka.
/// Handles pop times, cooldown calculations, and weather/night condition requirements.
/// </summary>
public class EurekaFate
{
    public const long CooldownMilliseconds = 7200000; // 2 hours (120 minutes)

    public ushort FateId { get; private set; }
    public ushort TrackerId { get; private set; }
    public ushort TerritoryId { get; private set; }
    public ushort MapId { get; private set; }
    public string FateName { get; private set; }
    public string BossName { get; private set; }
    public string BossShortName { get; private set; }
    public Vector2 FatePosition { get; set; }
    public string SpawnedBy { get; private set; }
    public Vector2 SpawnByPosition { get; private set; }
    public EurekaWeather SpawnRequiredWeather { get; private set; }
    public EurekaWeather SpawnByRequiredWeather { get; private set; }
    public EurekaElement BossElement { get; private set; }
    public EurekaElement SpawnByElement { get; private set; }
    public bool SpawnByRequiredNight { get; private set; }
    public long KilledAt { get; private set; } = -1;
    public byte FateProgress { get; set; }
    public bool IncludeInTracker { get; private set; }
    public bool IsBunnyFate { get; private set; }
    public int FateLevel { get; private set; }

    public EurekaFate(
        ushort fateId,
        ushort trackerId,
        ushort territoryId,
        ushort mapId,
        string fateName,
        string bossName,
        string bossShortName,
        Vector2 fatePosition,
        string spawnedBy,
        Vector2 spawnedByPosition,
        EurekaWeather spawnRequiredWeather,
        EurekaWeather spawnByRequiredWeather,
        EurekaElement bossElement,
        EurekaElement spawnByElement,
        bool spawnByRequiredNight,
        int fateLevel,
        bool includeInTracker = true,
        bool isBunnyFate = false)
    {
        FateId = fateId;
        TrackerId = trackerId;
        TerritoryId = territoryId;
        MapId = mapId;
        FateName = fateName;
        BossName = bossName;
        BossShortName = bossShortName;
        FatePosition = fatePosition;
        SpawnedBy = spawnedBy;
        SpawnByPosition = spawnedByPosition;
        SpawnRequiredWeather = spawnRequiredWeather;
        SpawnByRequiredWeather = spawnByRequiredWeather;
        BossElement = bossElement;
        SpawnByElement = spawnByElement;
        SpawnByRequiredNight = spawnByRequiredNight;
        FateLevel = fateLevel;
        IncludeInTracker = includeInTracker;
        IsBunnyFate = isBunnyFate;
        KilledAt = -1;
    }

    /// <summary>
    /// Returns true if the NM has been popped and is still within its 2-hour respawn cooldown.
    /// </summary>
    public bool IsPopped() =>
        KilledAt != -1 && (KilledAt + CooldownMilliseconds) > DateTimeOffset.Now.ToUnixTimeMilliseconds();

    /// <summary>
    /// Returns remaining time until this NM's 2-hour cooldown finishes.
    /// </summary>
    public TimeSpan GetRespawnTimeleft()
    {
        long remainingMs = (KilledAt + CooldownMilliseconds) - DateTimeOffset.Now.ToUnixTimeMilliseconds();
        return remainingMs > 0 ? TimeSpan.FromMilliseconds(remainingMs) : TimeSpan.Zero;
    }

    /// <summary>
    /// Evaluates current respawn and spawn condition blockers (cooldown, weather, night).
    /// </summary>
    public List<(string Action, TimeSpan Time)> GetRespawnRequirements(IEurekaZoneTracker tracker)
    {
        List<(string Action, TimeSpan Time)> requirements = new();

        if (IsPopped())
        {
            requirements.Add(("Respawn", GetRespawnTimeleft()));
        }

        var currentWeather = tracker.GetCurrentWeatherInfo().Weather;

        if (SpawnByRequiredWeather != EurekaWeather.None && SpawnByRequiredWeather != currentWeather)
        {
            var nextWeather = tracker.GetAllNextWeatherTime().FirstOrDefault(x => x.Weather == SpawnByRequiredWeather);
            if (nextWeather.Weather != EurekaWeather.None)
            {
                requirements.Add((nextWeather.Weather.ToFriendlyString(), nextWeather.Time));
            }
        }
        else if (SpawnRequiredWeather != EurekaWeather.None && SpawnRequiredWeather != currentWeather)
        {
            var nextWeather = tracker.GetAllNextWeatherTime().FirstOrDefault(x => x.Weather == SpawnRequiredWeather);
            if (nextWeather.Weather != EurekaWeather.None)
            {
                requirements.Add((nextWeather.Weather.ToFriendlyString(), nextWeather.Time));
            }
        }

        var etNow = EorzeaTime.Now;
        if (SpawnByRequiredNight && etNow.EorzeaDateTime.Hour >= 6 && etNow.EorzeaDateTime.Hour < 18)
        {
            requirements.Add(("Night", etNow.TimeUntilNight()));
        }

        return requirements;
    }

    public DateTime GetPoppedTime() => EorzeaTime.Zero.AddMilliseconds(KilledAt).ToLocalTime();

    public void ResetKill() => KilledAt = -1;

    public void SetKill(long time) => KilledAt = time;
}
