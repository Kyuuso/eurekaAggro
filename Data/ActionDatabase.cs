using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using Dalamud.Plugin.Services;
using EurekaAggro.Models;
using Newtonsoft.Json.Linq;

namespace EurekaAggro.Data;

/// <summary>
/// Manages enemy channeling actions and counters (Stun, Silence, Line of Sight, Regen).
/// </summary>
public class ActionDatabase
{
    private readonly IPluginLog log;
    private readonly string userStoragePath;

    /// <summary>
    /// Dictionary of enemy actions indexed by ActionId.
    /// </summary>
    public ConcurrentDictionary<uint, EnemyActionData> Actions { get; } = new();

    public ActionDatabase(IPluginLog log, string configDirectory)
    {
        this.log = log;
        userStoragePath = Path.Combine(configDirectory, "eurekaaggro_actions.json");
        LoadEmbeddedDatabase();
        LoadUserOverrides();
    }

    private void LoadEmbeddedDatabase()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("EurekaAggro.Recursos.DDCheckDB.json");
            if (stream == null)
            {
                log.Warning("Embedded DDCheckDB.json resource was not found.");
                return;
            }

            using var reader = new StreamReader(stream);
            var jsonText = reader.ReadToEnd();
            var root = JObject.Parse(jsonText);

            if (root["channeling"] is JObject channeling)
            {
                foreach (var prop in channeling.Properties())
                {
                    if (uint.TryParse(prop.Name, out var id) && prop.Value is JObject data)
                    {
                        var action = ParseAction(id, data);
                        Actions[id] = action;
                    }
                }
            }

            log.Info($"Loaded {Actions.Count} actions from embedded database.");
        }
        catch (Exception ex)
        {
            log.Error(ex, "Failed to load embedded action database.");
        }
    }

    private static EnemyActionData ParseAction(uint id, JObject data)
    {
        return new EnemyActionData
        {
            ActionId = id,
            MobName = data["mob"]?.ToString() ?? string.Empty,
            ActionName = data["name"]?.ToString() ?? string.Empty,
            RequiresStun = data["stun"]?.Value<bool>() ?? false,
            RequiresSilence = data["silence"]?.Value<bool>() ?? false,
            RequiresRegen = data["regen"]?.Value<bool>() ?? false,
            RequiresLineOfSight = data["los"]?.Value<bool>() ?? false,
            IsInterruptible = data["interruptable"]?.Value<bool>() ?? false,
            AlertMessage = data["alertMessage"]?.ToString() ?? string.Empty
        };
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
                    Actions[id] = ParseAction(id, data);
                }
            }
        }
        catch (Exception ex)
        {
            log.Error(ex, "Failed to load user action overrides.");
        }
    }

    private bool isDirty = false;

    public void SaveUserOverrides()
    {
        try
        {
            var root = new JObject();
            foreach (var (id, ac) in Actions)
            {
                var actionObj = new JObject
                {
                    ["mob"] = ac.MobName,
                    ["name"] = ac.ActionName,
                    ["stun"] = ac.RequiresStun,
                    ["silence"] = ac.RequiresSilence,
                    ["regen"] = ac.RequiresRegen,
                    ["los"] = ac.RequiresLineOfSight,
                    ["interruptable"] = ac.IsInterruptible,
                    ["alertMessage"] = ac.AlertMessage
                };
                root[id.ToString()] = actionObj;
            }

            var dir = Path.GetDirectoryName(userStoragePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(userStoragePath, root.ToString());
            isDirty = false;
            log.Info("Action database saved successfully.");
        }
        catch (Exception ex)
        {
            log.Error(ex, "Failed to save action database overrides.");
        }
    }

    public void SaveIfDirty()
    {
        if (isDirty)
        {
            SaveUserOverrides();
        }
    }

    public EnemyActionData GetOrRegister(uint actionId, string mobName, string actionName, bool isInterruptible)
    {
        if (Actions.TryGetValue(actionId, out var existing))
        {
            return existing;
        }

        var newAction = new EnemyActionData
        {
            ActionId = actionId,
            MobName = mobName,
            ActionName = actionName,
            IsInterruptible = isInterruptible,
            RequiresSilence = isInterruptible,
            RequiresStun = false,
            RequiresRegen = false,
            RequiresLineOfSight = false,
            AlertMessage = isInterruptible ? "INTERRUPT AVAILABLE!" : string.Empty
        };

        Actions[actionId] = newAction;
        isDirty = true;
        return newAction;
    }
}
