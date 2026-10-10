using System;
using System.Collections.Generic;

namespace EurekaAggro.Tracker.Model;

/// <summary>
/// Interface implemented by each Eureka zone (Anemos, Pagos, Pyros, Hydatos)
/// supplying its FATE/NM tables, weather predictions, and pop times.
/// </summary>
public interface IEurekaZoneTracker
{
    ushort ZoneId { get; }
    string ZoneName { get; }
    uint TerritoryId { get; }

    List<EurekaFate> GetFates();

    (EurekaWeather Weather, TimeSpan Timeleft) GetCurrentWeatherInfo();

    List<(EurekaWeather Weather, TimeSpan Time)> GetAllNextWeatherTime();

    void SetPopTimes(Dictionary<ushort, long> keyValuePairs);
}
