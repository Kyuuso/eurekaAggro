using System;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using EurekaAggro.Configuration;
using EurekaAggro.Data;

namespace EurekaAggro.Services;

/// <summary>
/// Active cast alert data shown on screen.
/// </summary>
public class ActiveCastAlert
{
    public string MobName { get; set; } = string.Empty;
    public string ActionName { get; set; } = string.Empty;
    public string MainMessage { get; set; } = string.Empty;
    public bool RequiresStun { get; set; }
    public bool RequiresSilence { get; set; }
    public bool RequiresLineOfSight { get; set; }
    public bool RequiresRegen { get; set; }
    public float CastProgress { get; set; }
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public float DurationSeconds { get; set; } = 3.0f;

    public bool IsExpired => (DateTime.UtcNow - StartTime).TotalSeconds > DurationSeconds;
}

/// <summary>
/// Monitors casting actions of targets and nearby enemies to trigger tactical alerts (Stun, Silence, LOS).
/// </summary>
public class CastMonitor
{
    private readonly IDataManager dataManager;
    private readonly IClientState clientState;
    private readonly ITargetManager targetManager;
    private readonly IObjectTable objectTable;
    private readonly IChatGui chat;
    private readonly ActionDatabase actionDatabase;
    private readonly PluginConfiguration config;

    // Track active casting entity to prevent repetitive allocations and chat spam
    private ulong currentCastingMobId = 0;
    private uint currentCastActionId = 0;

    public ActiveCastAlert? ActiveAlert { get; private set; }

    public CastMonitor(
        IDataManager dataManager,
        IClientState clientState,
        ITargetManager targetManager,
        IObjectTable objectTable,
        IChatGui chat,
        ActionDatabase actionDatabase,
        PluginConfiguration config)
    {
        this.dataManager = dataManager;
        this.clientState = clientState;
        this.targetManager = targetManager;
        this.objectTable = objectTable;
        this.chat = chat;
        this.actionDatabase = actionDatabase;
        this.config = config;
    }

    public void Update()
    {
        if (!config.Enabled || !config.ShowCastAlerts)
        {
            ResetAlert();
            return;
        }

        if (ActiveAlert != null && ActiveAlert.IsExpired)
        {
            ResetAlert();
        }

        // Priority 1: Current target
        var target = targetManager.Target;
        if (target is IBattleChara targetChara &&
            targetChara.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc &&
            !(targetChara is Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter) &&
            targetChara.IsCasting &&
            targetChara.CurrentHp > 0)
        {
            ProcessEnemyCast(targetChara);
            return;
        }

        // Priority 2: Nearby enemies
        var player = objectTable.LocalPlayer;
        if (player == null)
        {
            ResetAlert();
            return;
        }

        IBattleChara? activeCaster = null;
        foreach (var obj in objectTable)
        {
            if (obj is IBattleChara enemy &&
                enemy.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc &&
                !(enemy is Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter) &&
                enemy.GameObjectId != player.GameObjectId)
            {
                if (enemy.IsCasting && enemy.CurrentHp > 0)
                {
                    activeCaster = enemy;
                    break;
                }
            }
        }

        if (activeCaster != null)
        {
            ProcessEnemyCast(activeCaster);
        }
        else if (currentCastingMobId != 0)
        {
            ResetAlert();
        }
    }

    private void ResetAlert()
    {
        ActiveAlert = null;
        currentCastingMobId = 0;
        currentCastActionId = 0;
    }

    private void ProcessEnemyCast(IBattleChara enemy)
    {
        var actionId = enemy.CastActionId;
        if (actionId == 0)
        {
            ResetAlert();
            return;
        }

        float progress = 0f;
        if (enemy.TotalCastTime > 0)
        {
            progress = Math.Clamp(enemy.CurrentCastTime / enemy.TotalCastTime, 0f, 1f);
        }

        // If it is the same ongoing cast, just update progress without creating new objects or spamming chat
        if (currentCastingMobId == enemy.GameObjectId && currentCastActionId == actionId && ActiveAlert != null)
        {
            ActiveAlert.CastProgress = progress;
            return;
        }

        var mobName = enemy.Name.TextValue;
        var isInterruptible = enemy.IsCastInterruptible;

        string actionName = string.Empty;
        try
        {
            var sheet = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
            if (sheet != null && sheet.TryGetRow(actionId, out var row))
            {
                actionName = row.Name.ExtractText();
            }
        }
        catch
        {
            // Fallback if sheet resolution fails
        }

        if (string.IsNullOrWhiteSpace(actionName))
        {
            actionName = $"Action #{actionId}";
        }

        var data = actionDatabase.GetOrRegister(actionId, mobName, actionName, isInterruptible);

        string message = string.Empty;
        if (!string.IsNullOrWhiteSpace(data.AlertMessage))
        {
            message = data.AlertMessage;
        }
        else if (data.RequiresSilence || data.IsInterruptible)
        {
            message = "SILENCE / INTERRUPT NOW!";
        }
        else if (data.RequiresStun)
        {
            message = "STUN NOW!";
        }
        else if (data.RequiresLineOfSight)
        {
            message = "BREAK LINE OF SIGHT (LOS)!";
        }
        else if (data.RequiresRegen)
        {
            message = "HEALING / REGEN NEEDED!";
        }

        if (!string.IsNullOrEmpty(message))
        {
            currentCastingMobId = enemy.GameObjectId;
            currentCastActionId = actionId;

            ActiveAlert = new ActiveCastAlert
            {
                MobName = mobName,
                ActionName = data.ActionName,
                MainMessage = message,
                RequiresStun = data.RequiresStun,
                RequiresSilence = data.RequiresSilence || data.IsInterruptible,
                RequiresLineOfSight = data.RequiresLineOfSight,
                RequiresRegen = data.RequiresRegen,
                CastProgress = progress,
                DurationSeconds = Math.Max(enemy.TotalCastTime - enemy.CurrentCastTime, 1.5f),
                StartTime = DateTime.UtcNow
            };

            // Only print once when cast starts
            if (config.NotifyCastInChat)
            {
                chat.Print($"[EurekaAggro] Alert: {mobName} is casting {data.ActionName} -> {message}");
            }
        }
    }
}
