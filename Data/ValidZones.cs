using System.Collections.Generic;

namespace EurekaAggro.Data;

/// <summary>
/// Contains territory type IDs for all Eureka expedition zones in FFXIV.
/// </summary>
public static class ValidZones
{
    /// <summary>
    /// Territory type mappings for Eureka expedition zones.
    /// </summary>
    public static readonly Dictionary<uint, string> EurekaZones = new()
    {
        { 732, "Eureka Anemos" },
        { 763, "Eureka Pagos" },
        { 795, "Eureka Pyros" },
        { 827, "Eureka Hydatos / Baldesion Arsenal" }
    };

    /// <summary>
    /// Checks whether the specified territory ID is an active Eureka zone.
    /// </summary>
    public static bool IsEureka(uint territoryId)
    {
        return EurekaZones.ContainsKey(territoryId);
    }

    /// <summary>
    /// Gets the human-readable name of the Eureka zone.
    /// </summary>
    public static string GetZoneName(uint territoryId)
    {
        return EurekaZones.TryGetValue(territoryId, out var name) ? name : "Unknown Zone";
    }

    /// <summary>
    /// Checks whether radar drawing should be enabled given the current territory and configuration.
    /// </summary>
    public static bool IsValidZone(uint territoryId, bool onlyInEureka)
    {
        if (!onlyInEureka)
        {
            return true;
        }

        return IsEureka(territoryId);
    }
}
