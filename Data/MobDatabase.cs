using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using Dalamud.Plugin.Services;
using EurekaSuite.Models;
using Newtonsoft.Json.Linq;

namespace EurekaSuite.Data;

/// <summary>
/// Manages the Eureka mob database, aggro detection types, and custom user overrides.
/// Includes pre-configured aggro definitions for Eureka's iconic monsters
/// (Sleeping Dragons, Ashkin/Undead, and Elementals/Sprites).
/// </summary>
public class MobDatabase
{
    private readonly IPluginLog log;
    private readonly string userStoragePath;
    private bool isDirty = false;

    /// <summary>
    /// Thread-safe dictionary of mobs indexed by their BaseId.
    /// </summary>
    public ConcurrentDictionary<uint, MobData> Mobs { get; } = new();

    public MobDatabase(IPluginLog log, string configDirectory)
    {
        this.log = log;
        userStoragePath = Path.Combine(configDirectory, "eurekasuite_mobs.json");
        var legacyPath = Path.Combine(configDirectory, "EurekaSuite_mobs.json");
        if (!File.Exists(userStoragePath) && File.Exists(legacyPath))
        {
            try { File.Copy(legacyPath, userStoragePath); } catch { }
        }

        LoadEmbeddedDatabase();
        LoadUserOverrides();
    }

    private void LoadEmbeddedDatabase()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("EurekaSuite.Recursos.DB.json");
            if (stream == null)
            {
                log.Warning("Embedded DB.json resource was not found.");
                return;
            }

            using var reader = new StreamReader(stream);
            var jsonText = reader.ReadToEnd();
            var root = JObject.Parse(jsonText);

            if (root["mobs"] is JObject mobsObj)
            {
                foreach (var prop in mobsObj.Properties())
                {
                    if (uint.TryParse(prop.Name, out var id) && prop.Value is JObject data)
                    {
                        var mob = ParseMob(data);
                        Mobs[id] = mob;
                    }
                }
            }

            log.Info($"Loaded {Mobs.Count} mobs from embedded database.");
        }
        catch (Exception ex)
        {
            log.Error(ex, "Failed to load embedded mob database.");
        }
    }

    private static MobData ParseMob(JObject data)
    {
        var name = data["name"]?.ToString() ?? string.Empty;
        var aggroStr = data["aggroType"]?.ToString() ?? string.Empty;
        var dangerStr = data["dangerLevel"]?.ToString() ?? string.Empty;
        var hitbox = data["hitRadius"]?.Value<float>() ?? 1.0f;
        var distance = data["aggroDistance"]?.Value<float>() ?? 0f;
        var sight = data["sightRadian"]?.Value<float>() ?? 1.74533f;
        var elemLevel = data["elementalLevel"]?.Value<byte>() ?? 0;

        var (inferredType, defaultDistance) = ClassifyEurekaMob(name);

        var aggroType = aggroStr.ToLowerInvariant() switch
        {
            "sight" => AggroType.Sight,
            "sound" => AggroType.Sound,
            "proxi" => AggroType.Proximity,
            "blood" => AggroType.Blood,
            "magic" => AggroType.Magic,
            _ => inferredType
        };

        if (distance <= 0f)
        {
            distance = defaultDistance;
        }

        var dangerLevel = dangerStr.ToLowerInvariant() switch
        {
            "easy" => DangerLevel.Easy,
            "caution" => DangerLevel.Caution,
            "danger" => DangerLevel.Danger,
            _ => (aggroType == AggroType.Sound) ? DangerLevel.Danger : DangerLevel.Unknown
        };

        return new MobData
        {
            Name = name,
            AggroType = aggroType,
            DangerLevel = dangerLevel,
            HitboxRadius = hitbox,
            AggroDistance = distance,
            SightRadian = sight,
            ElementalLevel = elemLevel
        };
    }

    /// <summary>
    /// Accurately classifies Eureka monsters across Anemos, Pagos, Pyros, Hydatos, and Baldesion Arsenal
    /// into their true canonical aggro types (Sound, Magic, Blood, Proximity, Sight) with default distances.
    /// </summary>
    public static (AggroType Type, float DefaultDistance) ClassifyEurekaMob(string name)
    {
        var lower = name.ToLowerInvariant();

        // 1. Explicit Sight exceptions (creatures with eyes that would otherwise match a Sound/Magic keyword)
        // Dragonflies & Darners have compound eyes (Sight), not Sound like true dragons
        if (lower.Contains("dragonfly") || lower.Contains("darner"))
            return (AggroType.Sight, 10.2f);

        // Tortoises/Chelonians have eyes (Sight), not Magic despite words like "emberflash"
        if (lower.Contains("matamata") || lower.Contains("chelone"))
            return (AggroType.Sight, 10.2f);

        // 2. Specific Proximity exceptions (Constructs/Bombs that might contain fish/animal words like bombfish)
        if (lower.Contains("bomb") || lower.Contains("grenade"))
            return (AggroType.Proximity, 10.2f);

        // 3. Sprites & Elementals: Magic aggro (18.0m radius triggered by casting spells)
        if (lower.Contains("sprite") || lower.Contains("elemental") ||
            lower.Contains("ember") || lower.Contains("spark") || lower.Contains("flatus"))
        {
            return (AggroType.Magic, 18.0f);
        }

        // 4. Ashkin / Undead: Blood aggro (25.0m radius triggered when player HP < 80%)
        if (lower.Contains("corpse") || lower.Contains("ghost") || lower.Contains("wraith") ||
            lower.Contains("bhoot") || lower.Contains("specter") || lower.Contains("revenant") ||
            lower.Contains("skatene") || lower.Contains("skeleton") || lower.Contains("zombie") ||
            lower.Contains("ankou") || lower.Contains("wight") || lower.Contains("shadow") ||
            lower.Contains("mummy") || lower.Contains("vampire") || lower.Contains("damned") ||
            lower.Contains("dullahan") || lower.Contains("gravekeeper") || lower.Contains("anubys") ||
            lower.Contains("lich") || lower.Contains("phantom") || lower.Contains("apparition") ||
            lower.Contains("geshunpest") || lower.Contains("haunt") || lower.Contains("ashkin"))
        {
            return (AggroType.Blood, 25.0f);
        }

        // 5. Proximity aggro (360-degree always, walking does NOT prevent aggro)
        // Amorphous / Slimes / Puddings / Gelatos / Hecteyes
        if (lower.Contains("slime") || lower.Contains("pudding") || lower.Contains("flan") ||
            lower.Contains("squib") || lower.Contains("hecteyes") || lower.Contains("amoeba") ||
            lower.Contains("gelato") || lower.Contains("blubber eyes") || lower.Contains("red eye"))
        {
            return (AggroType.Proximity, 10.2f);
        }

        // Plantoids, Fungi & Saplings
        if (lower.Contains("treant") || lower.Contains("leshy") || lower.Contains("ochu") ||
            lower.Contains("nullchu") || lower.Contains("valbol") || lower.Contains("roselet") ||
            lower.Contains("icetrap") || lower.Contains("plant") || lower.Contains("morbol") ||
            lower.Contains("malboro") || lower.Contains("henbane") || lower.Contains("biloko") ||
            lower.Contains("mushroom") || lower.Contains("funguar") || lower.Contains("sapling") ||
            lower.Contains("cassie"))
        {
            return (AggroType.Proximity, 10.2f);
        }

        // Cactuars & Mandragoras (including Canal varieties and Korpokkurs)
        if (lower.Contains("cactuar") || lower.Contains("sabotender") ||
            lower.Contains("mandragora") || lower.Contains("korrigan") ||
            lower.Contains("korpokkur") || lower.Contains("gorpokkur") ||
            lower.Contains("canal onion") || lower.Contains("canal egg") ||
            lower.Contains("canal garlic") || lower.Contains("canal tomato") ||
            lower.Contains("canal queen"))
        {
            return (AggroType.Proximity, 10.2f);
        }

        // Constructs / Golems / Mimics / Magitek / Mammets
        if (lower.Contains("golem") || lower.Contains("colossus") || lower.Contains("talos") ||
            lower.Contains("monolith") || lower.Contains("guardian") || lower.Contains("statue") ||
            lower.Contains("gargoyle") || lower.Contains("mimic") || lower.Contains("dreadnaught") ||
            lower.Contains("mammet") || lower.Contains("calca") || lower.Contains("brina"))
        {
            return (AggroType.Proximity, 10.2f);
        }

        // Sleeping Dragons (Sound aggro: running triggers, walking is 100% safe)
        // Awake patrolling dragons (Biasts, Vouivres, Wyverns, Drakes) have eyes and use standard Sight aggro!
        if (lower.Contains("void dragon") || lower.Contains("voidragon") ||
            lower.Contains("slumbering") || lower.Contains("sleeping"))
        {
            return (AggroType.Sound, 10.5f);
        }

        // Fish / Ogrebons / Piranus / Anglers
        if (lower.Contains("piranu") || lower.Contains("ogrebon") || lower.Contains("angler") ||
            lower.Contains("pugil") || lower.Contains("gurnard") || lower.Contains("bishop") ||
            lower.Contains("fish"))
        {
            return (AggroType.Sound, 10.5f);
        }

        // Amphibians (Efts, Salamanders, Frogs, Toads, Nanka, Nix)
        if (lower.Contains("eft") || lower.Contains("salamander") || lower.Contains("frog") ||
            lower.Contains("toad") || lower.Contains("nix") || lower.Contains("nanka"))
        {
            return (AggroType.Sound, 10.5f);
        }

        // Arthropods, Crustaceans, Spiders & Scorpions
        if (lower.Contains("yarzon") || lower.Contains("crab") || lower.Contains("snipper") ||
            lower.Contains("spider") || lower.Contains("crawler") || lower.Contains("karlabos") ||
            lower.Contains("clipper") || lower.Contains("banemite") || lower.Contains("diremite") ||
            lower.Contains("mite") || lower.Contains("centipede") || lower.Contains("tarantula") ||
            lower.Contains("scorpion") || lower.Contains("bird eater") || lower.Contains("big claw") ||
            lower.Contains("yabby") || lower.Contains("arthro"))
        {
            return (AggroType.Sound, 10.5f);
        }

        // Burrowers & Blind cave creatures
        if (lower.Contains("bat") || lower.Contains("worm") || lower.Contains("sandworm") ||
            lower.Contains("leech") || lower.Contains("mole") || lower.Contains("barbmole"))
        {
            return (AggroType.Sound, 10.5f);
        }

        // Snails & Shell-dwellers (blind, detect ground vibration)
        if (lower.Contains("uragnite") || lower.Contains("ymir"))
        {
            return (AggroType.Sound, 10.5f);
        }

        // 7. Default: Sight aggro (Frontal cone, walking or running in front triggers it)
        return (AggroType.Sight, 10.2f);
    }


    /// <summary>
    /// Checks whether a name corresponds to a player pet, summon, or companion.
    /// </summary>
    public static bool IsPlayerPetOrCompanion(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var lower = name.ToLowerInvariant();
        return lower.Contains("carbuncle") || lower.Contains("eos") || lower.Contains("selene") ||
               lower.Contains("titan-egi") || lower.Contains("ifrit-egi") || lower.Contains("garuda-egi") ||
               lower.Contains("bahamut") || lower.Contains("phoenix") || lower.Contains("automaton") ||
               lower.Contains("chocobo") || lower.Contains("seraph") || lower.Contains("living shadow") ||
               lower.Contains("esteem");
    }

    private void LoadUserOverrides()
    {
        try
        {
            if (!File.Exists(userStoragePath)) return;

            var jsonText = File.ReadAllText(userStoragePath);
            var root = JObject.Parse(jsonText);
            foreach (var prop in root.Properties())
            {
                if (uint.TryParse(prop.Name, out var id) && prop.Value is JObject data)
                {
                    var mob = ParseMob(data);

                    // Purge any player pets or summons that may have been erroneously recorded previously
                    if (IsPlayerPetOrCompanion(mob.Name))
                    {
                        isDirty = true;
                        continue;
                    }

                    if (mob.AggroType == AggroType.Sight)
                    {
                        var (inferred, defaultD) = ClassifyEurekaMob(mob.Name);
                        if (inferred != AggroType.Sight)
                        {
                            mob.AggroType = inferred;
                            mob.AggroDistance = defaultD;
                            if (inferred == AggroType.Sound) mob.DangerLevel = DangerLevel.Danger;
                            isDirty = true;
                        }
                    }
                    Mobs[id] = mob;
                }
            }

        }
        catch (Exception ex)
        {
            log.Error(ex, "Failed to load user mob overrides.");
        }
    }

    /// <summary>
    /// Saves user customized mob configurations to disk.
    /// </summary>
    public void SaveUserOverrides()
    {
        try
        {
            var root = new JObject();
            foreach (var (id, mob) in Mobs)
            {
                var mobObj = new JObject
                {
                    ["name"] = mob.Name,
                    ["aggroType"] = mob.AggroType switch
                    {
                        AggroType.Sight => "Sight",
                        AggroType.Sound => "Sound",
                        AggroType.Proximity => "Proxi",
                        AggroType.Blood => "Blood",
                        AggroType.Magic => "Magic",
                        _ => ""
                    },
                    ["dangerLevel"] = mob.DangerLevel switch
                    {
                        DangerLevel.Easy => "Easy",
                        DangerLevel.Caution => "Caution",
                        DangerLevel.Danger => "Danger",
                        _ => ""
                    },
                    ["hitRadius"] = mob.HitboxRadius,
                    ["aggroDistance"] = mob.AggroDistance,
                    ["sightRadian"] = mob.SightRadian,
                    ["elementalLevel"] = mob.ElementalLevel
                };
                root[id.ToString()] = mobObj;
            }

            var dir = Path.GetDirectoryName(userStoragePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(userStoragePath, root.ToString());
            isDirty = false;
            log.Info("Eureka mob database saved successfully.");
        }
        catch (Exception ex)
        {
            log.Error(ex, "Failed to save mob database overrides.");
        }
    }

    public void SaveIfDirty()
    {
        if (isDirty)
        {
            SaveUserOverrides();
        }
    }

    public void UpdateElementalLevel(uint baseId, byte level)
    {
        if (level == 0) return;
        if (Mobs.TryGetValue(baseId, out var mob))
        {
            if (mob.ElementalLevel != level)
            {
                mob.ElementalLevel = level;
                isDirty = true;
            }
        }
    }

    public MobData GetOrRegister(uint baseId, string name, float hitboxRadius)
    {
        if (IsPlayerPetOrCompanion(name))
        {
            return new MobData
            {
                Name = name,
                AggroType = AggroType.Sight,
                DangerLevel = DangerLevel.Easy,
                AggroDistance = 0f
            };
        }

        if (Mobs.TryGetValue(baseId, out var existing))
        {
            if (existing.HitboxRadius <= 0.05f && hitboxRadius > 0.05f)
            {
                existing.HitboxRadius = hitboxRadius;
            }

            // Upgrade previously unclassified Sight mobs to their true Eureka aggro type (e.g. Piranu -> Sound)
            if (existing.AggroType == AggroType.Sight)
            {
                var (inferred, defaultD) = ClassifyEurekaMob(name);
                if (inferred != AggroType.Sight)
                {
                    existing.AggroType = inferred;
                    existing.AggroDistance = defaultD;
                    if (inferred == AggroType.Sound) existing.DangerLevel = DangerLevel.Danger;
                    isDirty = true;
                }
            }

            return existing;
        }

        var (inferredType, defaultDist) = ClassifyEurekaMob(name);
        var danger = (inferredType == AggroType.Sound) ? DangerLevel.Danger : DangerLevel.Unknown;

        var newMob = new MobData
        {
            Name = name,
            AggroType = inferredType,
            DangerLevel = danger,
            HitboxRadius = hitboxRadius > 0.05f ? hitboxRadius : 1.0f,
            AggroDistance = defaultDist,
            SightRadian = 1.74533f // ~100 degrees
        };

        Mobs[baseId] = newMob;
        isDirty = true;
        return newMob;
    }
}
