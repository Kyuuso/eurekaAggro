using System;
using System.Diagnostics;
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

    // Movement speed tracking
    private Vector3 lastPlayerPos = Vector3.Zero;
    private DateTime lastPosTime = DateTime.UtcNow;
    private float currentSpeed = 0f;

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
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyA(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

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
                RestoreRunMode("Auto-Walk setting toggled off", false);
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

        // If player is already in combat, NEVER force walk mode!
        // If we previously engaged auto-walk, immediately restore run mode so the player can flee/kite.
        var playerInCombat = (player.StatusFlags & StatusFlags.InCombat) != 0;
        if (playerInCombat)
        {
            if (wasAutoWalkForced)
            {
                RestoreRunMode("Player entered combat / got aggroed! Immediately releasing Walk mode so player can flee!", true);
            }
            return;
        }

        var playerPos = player.Position;

        // Calculate player speed in real time
        var now = DateTime.UtcNow;
        var dt = (float)(now - lastPosTime).TotalSeconds;
        if (dt > 0.05f)
        {
            if (lastPlayerPos != Vector3.Zero)
            {
                var horizDist = Vector2.Distance(new Vector2(playerPos.X, playerPos.Z), new Vector2(lastPlayerPos.X, lastPlayerPos.Z));
                currentSpeed = horizDist / dt;
            }
            lastPlayerPos = playerPos;
            lastPosTime = now;
        }

        var chara = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)player.Address;
        bool isMounted = chara != null && chara->Mount.MountId > 0;
        float walkSpeedLimit = isMounted ? 3.8f : 3.2f;

        // When mounted, running speed is ~9.5 m/s vs ~6.0 m/s on foot.
        // Add an automatic +4.0m buffer when mounted so deceleration starts well ahead of the 10.5m sound circle!
        float effectiveWalkDistance = config.AutoWalkDistance + (isMounted ? 4.0f : 0f);

        // Hysteresis threshold: trigger walk at effectiveWalkDistance, clear walk at effectiveWalkDistance + 1.5m
        float triggerDistance = wasAutoWalkForced ? (effectiveWalkDistance + 1.5f) : effectiveWalkDistance;

        bool anyDragonInWalkRange = false;
        string nearestDragonName = string.Empty;
        float nearestDist = float.MaxValue;

        // Scan nearby GameObjects for Sleeping Dragons
        foreach (var obj in objectTable)
        {
            if (obj is not IBattleNpc mob) continue;
            if (mob.IsDead || mob.CurrentHp <= 0) continue;

            // CRITICAL: If the dragon is already awake / aggroed / in combat, walking provides ZERO protection!
            // Furthermore, forcing walk while an awake dragon is chasing someone traps them and causes fatal wipes.
            var mobInCombat = (mob.StatusFlags & StatusFlags.InCombat) != 0;
            var hasTarget = mob.TargetObjectId != 0 && mob.TargetObjectId != 0xE000_0000 && mob.TargetObjectId != 0xFFFF_FFFF;
            var isDamaged = mob.MaxHp > 0 && mob.CurrentHp < mob.MaxHp;
            var isCasting = mob.IsCasting;

            if (mobInCombat || hasTarget || isDamaged || isCasting)
            {
                continue;
            }

            var name = mob.Name.TextValue;
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (MobDatabase.IsPlayerPetOrCompanion(name)) continue;

            // Strict dragon identification
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

            // Elevation check for Eureka cliffs & caves (Pagos/Pyros)
            if (config.EnableVerticalFilter)
            {
                var verticalDiff = Math.Abs(playerPos.Y - mob.Position.Y);
                if (verticalDiff > config.VerticalTolerance) continue;
            }

            var dist = Vector3.Distance(playerPos, mob.Position);
            if (dist <= triggerDistance)
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

            if (!wasAutoWalkForced)
            {
                // CRITICAL: Inspect player walk state BEFORE writing to ctrl->IsWalking!
                // Walking on foot or on a mount is ~2.4 m/s. Running on foot is ~6.0 m/s; galloping on mount is ~9.0 m/s.
                // If moving above the walking limit or if memory flag is false, the player is actively running/galloping.
                bool playerWasRunning = currentSpeed > walkSpeedLimit || !ctrl->IsWalking;
                playerWasAlreadyWalking = !playerWasRunning;
                wasAutoWalkForced = true;

                string modeStr = isMounted ? "Mounted" : "On Foot";
                log.Information($"[EurekaAggro - AutoWalk] Approaching Sleeping Dragon '{nearestDragonName}' at {nearestDist:F1}m ({modeStr}, Speed: {currentSpeed:F1} m/s, Limit: {walkSpeedLimit:F1} m/s, Initial IsWalking: {ctrl->IsWalking}). Engaging WALK mode!");

                if (config.LogAutoWalkToChat)
                {
                    chatGui.Print($"[EurekaAggro] ✔ Auto-Walk engaged near '{nearestDragonName}' ({nearestDist:F1}m, {modeStr}).");
                }

                // If player was running, trigger the game client walk toggle
                if (playerWasRunning)
                {
                    ToggleGameWalkMode(true);
                }
                else
                {
                    log.Information($"[EurekaAggro - AutoWalk] Player was already walking slowly ({modeStr}). Preserving walk state without toggle.");
                }
            }

            // Keep memory walk flags locked every frame while near sleeping dragon
            ctrl->IsWalking = true;
            ctrl->IsWalkingDuringAutorun = true;
        }
        else if (wasAutoWalkForced)
        {
            RestoreRunMode($"Safely cleared sleeping dragon range (nearest was '{lastDragonName}')", false);
        }
    }

    /// <summary>
    /// Restores standard running mode once safely out of dragon range or when entering combat.
    /// </summary>
    private void RestoreRunMode(string reason, bool isCombatEmergency = false)
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
            if (isCombatEmergency)
            {
                chatGui.Print("[EurekaAggro] ⚠ In combat / Aggroed! Auto-Walk disengaged, Run mode restored!");
            }
            else
            {
                chatGui.Print("[EurekaAggro] ✔ Safely left dragon zone. Run mode restored.");
            }
        }

        if (!playerWasAlreadyWalking || isCombatEmergency)
        {
            // Toggle back to running (force bypass cooldown in combat emergencies)
            ToggleGameWalkMode(false, force: isCombatEmergency);
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
    public void ToggleGameWalkMode(bool wantWalking, bool force = false)
    {
        // Enforce cooldown (minimum 400ms) to prevent key flickering, unless forced (e.g. emergency combat release)
        if (!force && (DateTime.UtcNow - lastToggleTime).TotalMilliseconds < 400)
            return;

        byte vkCode = ResolveWalkKeybind();
        uint scanCode = MapVirtualKeyA(vkCode, 0);
        uint extended = (vkCode == 0x6F) ? KEYEVENTF_EXTENDEDKEY : 0;

        // 1. Send via keybd_event (system level input)
        keybd_event(vkCode, (byte)scanCode, extended, 0);
        keybd_event(vkCode, (byte)scanCode, extended | KEYEVENTF_KEYUP, 0);

        // 2. Also post directly to FFXIV main window handle to guarantee processing
        try
        {
            var proc = Process.GetCurrentProcess();
            if (proc.MainWindowHandle != IntPtr.Zero)
            {
                PostMessage(proc.MainWindowHandle, WM_KEYDOWN, (IntPtr)vkCode, (IntPtr)(scanCode << 16));
                PostMessage(proc.MainWindowHandle, WM_KEYUP, (IntPtr)vkCode, (IntPtr)((scanCode << 16) | 0xC0000001));
            }
        }
        catch {}

        lastToggleTime = DateTime.UtcNow;
        log.Information($"[EurekaAggro - AutoWalk] Sent walk keybind to client (VirtualKey: 0x{vkCode:X2}, ScanCode: 0x{scanCode:X2}, TargetState: {(wantWalking ? "Walk" : "Run")}, Force: {force})");
    }

    /// <summary>
    /// Resolves player's active Walk keybind from UIInputData, defaulting to Keypad / (VK_DIVIDE = 0x6F).
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
        ToggleGameWalkMode(!current, force: true);
    }
}
