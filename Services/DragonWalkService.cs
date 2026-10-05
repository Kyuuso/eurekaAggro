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
/// Instantly restores Run mode if player enters combat, takes damage, or if the dragon wakes up.
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

    // Dragon tracking to prevent re-trapping after emergency release
    private ulong activeDragonId = 0;
    private ulong ignoredDragonId = 0;
    private uint lastPlayerHp = 0;

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
                activeDragonId = 0;
            }
            return;
        }

        var chara = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)player.Address;
        bool playerInCombat = (player.StatusFlags & StatusFlags.InCombat) != 0 || (chara != null && chara->InCombat);
        bool playerTookDamage = lastPlayerHp > 0 && player.CurrentHp < lastPlayerHp;
        lastPlayerHp = player.CurrentHp;

        // EMERGENCY COMBAT CHECK: If player is in combat or took ANY damage, IMMEDIATELY drop walk lock!
        if (playerInCombat || playerTookDamage)
        {
            if (wasAutoWalkForced)
            {
                if (activeDragonId != 0)
                {
                    ignoredDragonId = activeDragonId;
                }
                RestoreRunMode(playerTookDamage ? "Player took damage from enemy attack!" : "Player entered combat / got aggroed!", true);
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

        bool isMounted = chara != null && chara->Mount.MountId > 0;
        float walkSpeedLimit = isMounted ? 3.8f : 3.2f;

        // When mounted, running speed is ~9.5 m/s vs ~6.0 m/s on foot.
        // Add an automatic +4.0m buffer when mounted so deceleration starts well ahead of the 10.5m sound circle!
        float effectiveWalkDistance = config.AutoWalkDistance + (isMounted ? 4.0f : 0f);

        // Hysteresis threshold: trigger walk at effectiveWalkDistance, clear walk at effectiveWalkDistance + 1.5m
        float triggerDistance = wasAutoWalkForced ? (effectiveWalkDistance + 1.5f) : effectiveWalkDistance;

        IBattleNpc? targetSleepingDragon = null;
        float nearestDist = float.MaxValue;

        // Scan nearby GameObjects for Sleeping Dragons
        foreach (var obj in objectTable)
        {
            if (obj is not IBattleNpc mob) continue;
            if (mob.IsDead || mob.CurrentHp <= 0) continue;

            var mobChara = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)mob.Address;
            bool mobInCombat = (mob.StatusFlags & StatusFlags.InCombat) != 0 || (mobChara != null && mobChara->InCombat);
            bool mobTargeting = (mob.TargetObjectId != 0 && mob.TargetObjectId != 0xE000_0000 && mob.TargetObjectId != 0xFFFF_FFFF) ||
                                (mobChara != null && mobChara->TargetId.ObjectId != 0 && mobChara->TargetId.ObjectId != 0xE000_0000);
            bool mobDamaged = mob.MaxHp > 0 && mob.CurrentHp < mob.MaxHp;
            bool mobCasting = mob.IsCasting;

            bool isAwakeOrAggroed = mobInCombat || mobTargeting || mobDamaged || mobCasting;

            // If the dragon we were walking for has just woken up or started attacking:
            if (wasAutoWalkForced && mob.GameObjectId == activeDragonId && isAwakeOrAggroed)
            {
                ignoredDragonId = activeDragonId;
                RestoreRunMode($"Dragon '{mob.Name.TextValue}' woke up / aggroed! Run mode restored immediately!", true);
                return;
            }

            // Skip awake, engaged, damaged, or casting dragons (walking provides zero protection against awake mobs!)
            if (isAwakeOrAggroed) continue;

            // If this dragon previously triggered an emergency release, do not re-trap the player while still near it
            if (mob.GameObjectId == ignoredDragonId) continue;

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
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    targetSleepingDragon = mob;
                }
            }
        }

        var ctrl = Control.Instance();
        if (ctrl == null) return;

        if (targetSleepingDragon != null)
        {
            lastDragonName = targetSleepingDragon.Name.TextValue;
            lastDistance = nearestDist;
            activeDragonId = targetSleepingDragon.GameObjectId;

            if (!wasAutoWalkForced)
            {
                // CRITICAL: Inspect player walk state BEFORE toggling
                bool playerWasRunning = currentSpeed > walkSpeedLimit || !ctrl->IsWalking;
                playerWasAlreadyWalking = !playerWasRunning;
                wasAutoWalkForced = true;

                string modeStr = isMounted ? "Mounted" : "On Foot";
                log.Information($"[EurekaAggro - AutoWalk] Approaching Sleeping Dragon '{lastDragonName}' at {lastDistance:F1}m ({modeStr}, Speed: {currentSpeed:F1} m/s). Engaging WALK mode!");

                if (config.LogAutoWalkToChat)
                {
                    chatGui.Print($"[EurekaAggro] ✔ Auto-Walk engaged near '{lastDragonName}' ({lastDistance:F1}m, {modeStr}).");
                }

                if (playerWasRunning)
                {
                    SetDesiredWalkState(true);
                }
            }
            // CRITICAL: We do NOT lock ctrl->IsWalking = true on every frame!
            // FFXIV maintains its walk toggle state naturally.
            // If the player chooses to manually run, autorun, or flee, we do NOT fight or override their inputs!
        }
        else if (wasAutoWalkForced)
        {
            RestoreRunMode($"Safely cleared sleeping dragon range (nearest was '{lastDragonName}')", false);
        }
        else if (ignoredDragonId != 0 && nearestDist > triggerDistance + 5.0f)
        {
            // Safely far enough away from previously ignored dragon; reset ignore flag
            ignoredDragonId = 0;
        }
    }

    /// <summary>
    /// Restores standard running mode once safely out of dragon range or when entering combat.
    /// </summary>
    private void RestoreRunMode(string reason, bool isCombatEmergency = false)
    {
        log.Information($"[EurekaAggro - AutoWalk] Restoring RUN mode. Reason: {reason} (Emergency: {isCombatEmergency})");

        if (config.LogAutoWalkToChat)
        {
            if (isCombatEmergency)
            {
                chatGui.Print("[EurekaAggro] ⚠ In combat / Aggroed! Auto-Walk disengaged, RUN mode restored!");
            }
            else
            {
                chatGui.Print("[EurekaAggro] ✔ Safely left dragon zone. Run mode restored.");
            }
        }

        if (!playerWasAlreadyWalking || isCombatEmergency)
        {
            // Toggle back to running (bypasses cooldown in emergencies)
            SetDesiredWalkState(false, force: isCombatEmergency);
        }
        else
        {
            log.Information("[EurekaAggro - AutoWalk] Leaving dragon area. Player had walking enabled manually before, keeping walking state.");
        }

        wasAutoWalkForced = false;
        playerWasAlreadyWalking = false;
        activeDragonId = 0;
        lastDragonName = string.Empty;
        lastDistance = 0f;
    }

    /// <summary>
    /// Synchronizes the game client's Walk/Run state to the desired mode.
    /// Only sends the keypress IF the client is not already in the desired state!
    /// </summary>
    public void SetDesiredWalkState(bool wantWalking, bool force = false)
    {
        var ctrl = Control.Instance();
        if (ctrl == null) return;

        bool currentWalking = ctrl->IsWalking;

        // If the game is ALREADY in the desired state, do nothing!
        if (currentWalking == wantWalking)
        {
            ctrl->IsWalkingDuringAutorun = wantWalking;
            return;
        }

        // Cooldown check (minimum 350ms to prevent rapid key flapping, unless emergency force)
        if (!force && (DateTime.UtcNow - lastToggleTime).TotalMilliseconds < 350)
            return;

        byte vkCode = ResolveWalkKeybind();
        uint scanCode = MapVirtualKeyA(vkCode, 0);
        uint extended = (vkCode == 0x6F) ? KEYEVENTF_EXTENDEDKEY : 0;

        // Send a single key down and key up via keybd_event (DirectInput / raw input in FFXIV processes this once).
        // NEVER send PostMessage to MainWindowHandle at the same time, because that produces a duplicate keystroke
        // which immediately inverts the toggle back to where it started!
        keybd_event(vkCode, (byte)scanCode, extended, 0);
        keybd_event(vkCode, (byte)scanCode, extended | KEYEVENTF_KEYUP, 0);

        // Update memory flags to match the new state
        ctrl->IsWalking = wantWalking;
        ctrl->IsWalkingDuringAutorun = wantWalking;

        lastToggleTime = DateTime.UtcNow;
        log.Information($"[EurekaAggro - AutoWalk] SetDesiredWalkState (Target: {(wantWalking ? "Walk" : "Run")}, Prior: {(currentWalking ? "Walk" : "Run")}, VK: 0x{vkCode:X2}, Force: {force})");
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
        SetDesiredWalkState(!current, force: true);
    }
}
