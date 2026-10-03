using System;
using System.Collections.Concurrent;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace EurekaAggro.Services;

/// <summary>
/// Provides real-time Eureka Elemental Level detection and zone cap synchronizations.
/// </summary>
public static class EurekaLevelService
{
    // Caches for known mob levels detected from in-game Nameplates
    private static readonly ConcurrentDictionary<ulong, byte> MobObjectLevels = new();
    private static readonly ConcurrentDictionary<uint, byte> MobBaseIdLevels = new();
    private static readonly ConcurrentDictionary<string, byte> MobNameLevels = new();

    private static DateTime lastScanTime = DateTime.MinValue;

    /// <summary>
    /// Scans the 50 visible nameplates in real time from RaptureAtkModule to extract exact Eureka Elemental Levels.
    /// Optimized with a 500ms throttle and zero-allocation byte parsing to have virtually 0% CPU impact.
    /// </summary>
    public static unsafe void ScanNameplates()
    {
        // Throttle: only scan twice a second (every 500ms) since mob levels never change dynamically
        var now = DateTime.UtcNow;
        if ((now - lastScanTime).TotalMilliseconds < 500)
        {
            return;
        }
        lastScanTime = now;

        try
        {
            var ram = RaptureAtkModule.Instance();
            if (ram == null) return;

            var entries = ram->NamePlateInfoEntries;
            for (int i = 0; i < entries.Length; i++)
            {
                ref readonly var entry = ref entries[i];
                ulong id = entry.ObjectId.Id;
                if (id == 0) continue;

                byte parsedLvl = 0;

                // 1. Zero-allocation byte parsing directly from raw memory pointer (no strings allocated)
                if (!entry.LevelText.IsEmpty && entry.LevelText.StringLength > 0 && entry.LevelText.StringPtr.Value != null)
                {
                    byte* ptr = entry.LevelText.StringPtr.Value;
                    int len = (int)entry.LevelText.StringLength;
                    int val = 0;
                    bool hasDigit = false;

                    for (int b = 0; b < len; b++)
                    {
                        byte ch = ptr[b];
                        if (ch >= (byte)'0' && ch <= (byte)'9')
                        {
                            val = val * 10 + (ch - '0');
                            hasDigit = true;
                        }
                        else if (hasDigit)
                        {
                            break;
                        }
                    }

                    if (hasDigit && val > 0 && val <= 100)
                    {
                        parsedLvl = (byte)val;
                    }
                }

                // 2. Fallback to entry.Level if it is within reasonable Eureka range (1-65)
                if (parsedLvl == 0 && entry.Level > 0 && entry.Level <= 65)
                {
                    parsedLvl = (byte)entry.Level;
                }

                if (parsedLvl > 0)
                {
                    if (!MobObjectLevels.ContainsKey(id))
                    {
                        MobObjectLevels[id] = parsedLvl;

                        // Only allocate string once per new unknown mob name
                        var name = entry.Name.ToString();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            MobNameLevels[name] = parsedLvl;
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore during loading screens or area transitions
        }
    }

    /// <summary>
    /// Resolves the true Eureka Elemental Level of a monster using real-time NamePlate data and cached records.
    /// Avoids the Stormblood dummy sync Lv.70 bug.
    /// </summary>
    public static byte GetMobElementalLevel(ulong gameObjectId, uint baseId, string mobName, byte rawMobLevel, byte dbLevel = 0)
    {
        // 1. Live NamePlate object level
        if (MobObjectLevels.TryGetValue(gameObjectId, out var objLvl) && objLvl > 0)
        {
            MobBaseIdLevels[baseId] = objLvl;
            return objLvl;
        }

        // 2. BaseId cache (same mob species)
        if (MobBaseIdLevels.TryGetValue(baseId, out var baseLvl) && baseLvl > 0)
        {
            return baseLvl;
        }

        // 3. Name-based cache (e.g. "Hydatos Ziz" -> 52)
        if (!string.IsNullOrEmpty(mobName) && MobNameLevels.TryGetValue(mobName, out var nameLvl) && nameLvl > 0)
        {
            MobBaseIdLevels[baseId] = nameLvl;
            return nameLvl;
        }

        // 4. Saved database level
        if (dbLevel > 0 && dbLevel <= 65)
        {
            return dbLevel;
        }

        // 5. In Eureka, if raw level is 70 (Stormblood dummy sync), it is NOT the elemental level!
        if (rawMobLevel == 70)
        {
            return 0; // Return 0 until nameplate is scanned rather than falsely reporting Lv.70
        }

        return rawMobLevel;
    }

    /// <summary>
    /// Gets the maximum allowed Elemental Level for the specified Eureka territory.
    /// </summary>
    public static int GetZoneMaxLevel(uint territoryId)
    {
        return territoryId switch
        {
            732 => 20, // Eureka Anemos
            763 => 35, // Eureka Pagos
            795 => 50, // Eureka Pyros
            827 => 60, // Eureka Hydatos / Baldesion Arsenal
            _ => 60
        };
    }

    /// <summary>
    /// Attempts to read the player's active Elemental Level directly from the game's Eureka director memory.
    /// Returns 0 if outside Eureka or if memory is not yet available.
    /// </summary>
    public static unsafe int GetActiveMemoryLevel()
    {
        try
        {
            var ef = EventFramework.Instance();
            if (ef == null) return 0;

            var director = ef->GetPublicContentDirector();
            if (director != null && director->Type == PublicContentDirectorType.Eureka)
            {
                var eureka = (PublicContentEureka*)director;
                var lvl = (int)eureka->GetCurrentLevel();
                if (lvl > 0 && lvl <= 60)
                {
                    return lvl;
                }
            }
        }
        catch
        {
            // Ignore memory read errors gracefully during loading screens
        }

        return 0;
    }

    /// <summary>
    /// Computes the effective Elemental Level taking into account auto-detection, memory reads, zone caps, and manual override.
    /// </summary>
    public static int GetEffectiveElementalLevel(uint territoryId, bool autoDetect, int configuredLevel)
    {
        if (!autoDetect)
        {
            return Math.Clamp(configuredLevel, 1, 60);
        }

        // 1. Try real-time live memory read from game client
        int memLvl = GetActiveMemoryLevel();
        if (memLvl > 0)
        {
            return memLvl;
        }

        // 2. Zone-based auto sync: Cap configured level by the zone max (e.g. 60 -> 20 in Anemos)
        int zoneMax = GetZoneMaxLevel(territoryId);
        return Math.Min(configuredLevel, zoneMax);
    }
}
