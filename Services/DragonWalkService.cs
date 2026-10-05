using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using EurekaAggro.Configuration;
using EurekaAggro.Data;
using EurekaAggro.Models;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.System.Input;

namespace EurekaAggro.Services;

/// <summary>
/// Proximity-based monitoring service for lethal Sleeping Dragons in Eureka zones.
/// Safely manages Walk mode to prevent accidental player wipes caused by running near sleeping mobs.
/// Outputs structured Dalamud logs for debugging and player verification.
/// </summary>
public unsafe class DragonWalkService
{
    private readonly IPluginLog log;
    private readonly IClientState clientState;
    private readonly IObjectTable objectTable;
    private readonly IChatGui chatGui;
    private readonly MobDatabase mobDatabase;
    private readonly PluginConfiguration config;

    private bool wasAutoWalkForced = false;
    private bool playerWasAlreadyWalking = false;
    private DateTime lastToggleTime = DateTime.MinValue;
    private string lastDragonName = string.Empty;
    private float lastDistance = 0f;

    /// <summary>
    /// True when Auto-Walk has taken control of the character's movement mode.
    /// </summary>
    public bool IsAutoWalkEngaged => wasAutoWalkForced;

    /// <summary>
    /// Name of the nearest sleeping dragon triggering auto-walk.
    /// </summary>
    public string CurrentDragonName => lastDragonName;

    /// <summary>
    /// Distance in meters to the nearest sleeping dragon triggering auto-walk.
    /// </summary>
    public float CurrentDistance => lastDistance;

    // Windows API input simulation
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyA(uint uCode, uint uMapType);

    public DragonWalkService(
        IPluginLog log,
        IClientState clientState,
        IObjectTable objectTable,
        IChatGui chatGui,
        MobDatabase mobDatabase,
        PluginConfiguration config)
    {
        this.log = log;
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.chatGui = chatGui;
        this.mobDatabase = mobDatabase;
        this.config = config;
    }

    /// <summary>
    /// Evaluates dragon proximity and synchronizes walk/run states on every game tick.
    /// Called directly from IFramework.Update.
    /// </summary>
    public void Update()
    {
        if (!config.Enabled || !config.AutoWalkNearDragons)
        {
            if (wasAutoWalkForced)
            {
                RestoreRunMode("Auto-Walk setting toggled off");
            }
            return;
        }

        var player = objectTable.LocalPlayer;
        if (player == null)
        {
            if (wasAutoWalkForced)
            {
                wasAutoWalkForced = false;
                playerWasAlreadyWalking = false;
            }
            return;
        }

        var playerPos = player.Position;
        bool anyDragonInWalkRange = false;
        string nearestDragonName = string.Empty;
        float nearestDist = float.MaxValue;

        // Scan nearby GameObjects for Sleeping Dragons
        foreach (var obj in objectTable)
        {
            if (obj is not IBattleNpc mob) continue;
            if (mob.IsDead) continue;

            var name = mob.Name.TextValue;
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (MobDatabase.IsPlayerPetOrCompanion(name)) continue;

            // Strict dragon identification:
            // 1. Name explicitly includes "dragon", "slumbering", "sleeping", or "voidragon"
            // 2. OR classified as Sound aggro dragon in mob database
            bool isSleepingDragon =
                name.Contains("sleeping", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("slumbering", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("voidragon", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("void dragon", StringComparison.OrdinalIgnoreCase);

            if (!isSleepingDragon && mobDatabase.Mobs.TryGetValue(mob.BaseId, out var mobData))
            {
                if (mobData.AggroType == AggroType.Sound &&
                    (mobData.Name.Contains("dragon", StringComparison.OrdinalIgnoreCase) ||
                     mobData.Name.Contains("wyrm", StringComparison.OrdinalIgnoreCase)))
                {
                    isSleepingDragon = true;
                }
            }

            if (!isSleepingDragon) continue;

            // In combat mobs are already awake and actively engaged
            var inCombat = (mob.StatusFlags & StatusFlags.InCombat) != 0;
            if (inCombat) continue;

            // Elevation check for Eureka cliffs & caves (Pagos/Pyros)
            if (config.EnableVerticalFilter)
            {
                var verticalDiff = Math.Abs(playerPos.Y - mob.Position.Y);
                if (verticalDiff > config.VerticalTolerance) continue;
            }

            var dist = Vector3.Distance(playerPos, mob.Position);
            if (dist <= config.AutoWalkDistance)
            {
                anyDragonInWalkRange = true;
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearestDragonName = name;
                }
            }
        }

        var ctrl = Control.Instance();
        if (ctrl == null) return;

        if (anyDragonInWalkRange)
        {
            lastDragonName = nearestDragonName;
            lastDistance = nearestDist;

            // Keep memory walk flags locked every frame while near dragon
            ctrl->IsWalking = true;
            ctrl->IsWalkingDuringAutorun = true;

            if (!wasAutoWalkForced)
            {
                // Capture if player was already walking before auto-walk took control
                playerWasAlreadyWalking = ctrl->IsWalking;
                wasAutoWalkForced = true;

                log.Information($"[EurekaAggro - AutoWalk] Approaching Sleeping Dragon '{nearestDragonName}' at {nearestDist:F1}m (Trigger limit: {config.AutoWalkDistance:F1}m). Engaging WALK mode!");

                if (config.LogAutoWalkToChat)
                {
                    chatGui.Print($"[EurekaAggro] ✔ Auto-Walk engaged near '{nearestDragonName}' ({nearestDist:F1}m).");
                }

                // If player was running, send walk keybind to toggle the game client into walk mode
                if (!playerWasAlreadyWalking)
                {
                    ToggleGameWalkMode(true);
                }
                else
                {
                    log.Information("[EurekaAggro - AutoWalk] Player was already walking manually. Preserving walking state without simulated keypress.");
                }
            }
        }
        else if (wasAutoWalkForced)
        {
            RestoreRunMode($"Safely cleared sleeping dragon range (nearest was '{lastDragonName}')");
        }
    }

    /// <summary>
    /// Restores standard running mode once safely out of dragon range.
    /// </summary>
    private void RestoreRunMode(string reason)
    {
        var ctrl = Control.Instance();
        if (ctrl != null)
        {
            ctrl->IsWalking = false;
            ctrl->IsWalkingDuringAutorun = false;
        }

        log.Information($"[EurekaAggro - AutoWalk] Restoring RUN mode. Reason: {reason}");

        if (config.LogAutoWalkToChat)
        {
            chatGui.Print("[EurekaAggro] ✔ Safely left dragon zone. Run mode restored.");
        }

        if (!playerWasAlreadyWalking)
        {
            // Toggle back to running
            ToggleGameWalkMode(false);
        }
        else
        {
            log.Information("[EurekaAggro - AutoWalk] Leaving dragon area. Player had walking enabled manually before, keeping walking state.");
        }

        wasAutoWalkForced = false;
        playerWasAlreadyWalking = false;
        lastDragonName = string.Empty;
        lastDistance = 0f;
    }

    /// <summary>
    /// Sends a virtual key event to toggle FFXIV's internal Walk/Run mode.
    /// </summary>
    public void ToggleGameWalkMode(bool wantWalking)
    {
        // Enforce cooldown (minimum 600ms) to prevent key flickering
        if ((DateTime.UtcNow - lastToggleTime).TotalMilliseconds < 600)
            return;

        byte vkCode = ResolveWalkKeybind();

        uint scanCode = MapVirtualKeyA(vkCode, 0);
        uint extended = (vkCode == 0x6F) ? KEYEVENTF_EXTENDEDKEY : 0;

        // Key Down
        keybd_event(vkCode, (byte)scanCode, extended, 0);
        // Key Up
        keybd_event(vkCode, (byte)scanCode, extended | KEYEVENTF_KEYUP, 0);

        lastToggleTime = DateTime.UtcNow;
        log.Information($"[EurekaAggro - AutoWalk] Sent walk keybind to client (VirtualKey: 0x{vkCode:X2}, TargetState: {(wantWalking ? "Walk" : "Run")})");
    }

    /// <summary>
    /// Resolves player's active Walk keybind from InputData, defaulting to Keypad / (VK_DIVIDE = 0x6F).
    /// </summary>
    private byte ResolveWalkKeybind()
    {
        try
        {
            var uiInputData = FFXIVClientStructs.FFXIV.Client.UI.UIInputData.Instance();
            if (uiInputData != null)
            {
                var kb = uiInputData->GetKeybind(InputId.WALK);
                if (kb != null)
                {
                    var seKey = kb->KeySettings[0].Key;
                    if (seKey != SeVirtualKey.NO_KEY)
                    {
                        return (byte)seKey;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log.Debug(ex, "Could not resolve dynamic walk keybind from UIInputData. Using standard default 0x6F (Keypad /).");
        }

        return 0x6F; // Standard FFXIV default: Keypad / (VK_DIVIDE)
    }

    /// <summary>
    /// Manually triggers a toggle test (used by configuration window button).
    /// </summary>
    public void ManualTestToggle()
    {
        var ctrl = Control.Instance();
        bool current = ctrl != null && ctrl->IsWalking;
        log.Information($"[EurekaAggro - AutoWalk] Manual test toggle requested from settings. Current IsWalking: {current}");
        ToggleGameWalkMode(!current);
    }
}
