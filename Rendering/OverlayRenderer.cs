using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using EurekaAggro.Configuration;
using EurekaAggro.Data;
using EurekaAggro.Models;
using EurekaAggro.Services;

namespace EurekaAggro.Rendering;

/// <summary>
/// Real-time 3D in-game overlay renderer for Eureka.
/// Projects vision cones, sound circles for Sleeping Dragons, blood aggro warnings,
/// sprite magic alerts, distance guide lines, and pack link indicators.
/// </summary>
public class OverlayRenderer
{
    private readonly IGameGui gameGui;
    private readonly IClientState clientState;
    private readonly IObjectTable objectTable;
    private readonly MobDatabase mobDatabase;
    private readonly PluginConfiguration config;

    // Pre-allocated static screen point buffer to guarantee zero GC heap allocations during 3D rendering
    private readonly Vector2[] screenPointsBuffer = new Vector2[128];

    // Player speed tracking for walk vs run detection
    private Vector3 lastPlayerPosition = Vector3.Zero;
    private DateTime lastPositionTime = DateTime.UtcNow;
    private float currentSpeed = 0.0f;

    public OverlayRenderer(
        IGameGui gameGui,
        IClientState clientState,
        IObjectTable objectTable,
        MobDatabase mobDatabase,
        PluginConfiguration config)
    {
        this.gameGui = gameGui;
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.mobDatabase = mobDatabase;
        this.config = config;
    }

    /// <summary>
    /// Frame-by-frame rendering loop called from Dalamud's UI draw callback.
    /// </summary>
    public void Draw()
    {
        if (!config.Enabled || !clientState.IsLoggedIn) return;

        if (!ValidZones.IsValidZone(clientState.TerritoryType, config.OnlyInEureka))
        {
            return;
        }

        var player = objectTable.LocalPlayer;
        if (player == null) return;

        // Scan active in-game Nameplates to read exact Eureka Elemental Levels in real time
        EurekaLevelService.ScanNameplates();

        // Calculate player movement speed in yards/meters per second
        var now = DateTime.UtcNow;
        var dt = (float)(now - lastPositionTime).TotalSeconds;
        var playerPos = player.Position;

        if (dt > 0.05f)
        {
            if (lastPlayerPosition != Vector3.Zero)
            {
                currentSpeed = Vector3.Distance(new Vector3(playerPos.X, 0, playerPos.Z), new Vector3(lastPlayerPosition.X, 0, lastPlayerPosition.Z)) / dt;
            }
            lastPlayerPosition = playerPos;
            lastPositionTime = now;
        }

        bool isRunning = currentSpeed > config.DragonWalkSpeedThreshold;
        float playerHpPercent = player.MaxHp > 0 ? ((float)player.CurrentHp / player.MaxHp * 100f) : 100f;
        var drawList = ImGui.GetBackgroundDrawList();

        // Calculate active Elemental Level (auto-detected via memory / zone cap, or manual override)
        int effectivePlayerLevel = EurekaLevelService.GetEffectiveElementalLevel(
            clientState.TerritoryType,
            config.AutoDetectElementalLevel,
            config.PlayerElementalLevel);

        foreach (var obj in objectTable)
        {
            if (obj is not IBattleChara mob || mob.GameObjectId == player.GameObjectId) continue;
            if (mob.CurrentHp <= 0 || !mob.IsTargetable) continue;

            // Only process battle NPCs (never players, minions, or event objects)
            if (mob is IPlayerCharacter || mob.ObjectKind != ObjectKind.BattleNpc) continue;

            // Never process player summons (Carbuncle, fairies, Egi, Bahamut, etc.) or companion chocobos
            // In FFXIV, unowned wild monsters have OwnerId 0 or 0xE000_0000 / 0xFFFF_FFFF.
            if (mob.OwnerId != 0 && mob.OwnerId != 0xE000_0000 && mob.OwnerId != 0xFFFF_FFFF) continue;
            if (obj is IBattleNpc bNpc && (bNpc.BattleNpcKind == BattleNpcSubKind.Pet ||
                                           bNpc.BattleNpcKind == BattleNpcSubKind.Buddy ||
                                           bNpc.BattleNpcKind == BattleNpcSubKind.LovmMinion ||
                                           bNpc.BattleNpcKind == BattleNpcSubKind.NpcPartyMember))
            {
                continue;
            }

            // Name-based safety guard against pets and summons
            var mobName = mob.Name.TextValue;
            if (MobDatabase.IsPlayerPetOrCompanion(mobName)) continue;

            var distance = Vector3.Distance(playerPos, mob.Position);
            if (distance > config.DetectionRange) continue;

            // In Eureka (especially Pagos & Pyros cliffs and caves), mobs on different elevations cannot aggro
            if (config.EnableVerticalFilter)
            {
                var verticalDiff = Math.Abs(playerPos.Y - mob.Position.Y);
                if (verticalDiff > config.VerticalTolerance) continue;
            }

            var data = mobDatabase.GetOrRegister(mob.BaseId, mob.Name.TextValue, mob.HitboxRadius);

            // Resolve true Eureka Elemental Level (extracts from NamePlate to avoid Stormblood dummy sync Lv.70)
            byte mobLevel = EurekaLevelService.GetMobElementalLevel(
                mob.GameObjectId,
                mob.BaseId,
                mob.Name.TextValue,
                mob.Level,
                data.ElementalLevel);

            if (mobLevel > 0 && data.ElementalLevel != mobLevel)
            {
                mobDatabase.UpdateElementalLevel(mob.BaseId, mobLevel);
            }

            var inCombat = (mob.StatusFlags & StatusFlags.InCombat) != 0;
            if (inCombat) continue;

            // Filter out mobs that are too low level to aggro the player (unless exempted)
            if (config.FilterSafeMobs)
            {
                // Only actual lethal Sleeping Dragons (Voidragons, Slumbering Dragons) are exempted regardless of level
                bool isDragon = data.Name.Contains("dragon", StringComparison.OrdinalIgnoreCase) ||
                                data.Name.Contains("wyrm", StringComparison.OrdinalIgnoreCase) ||
                                data.Name.Contains("slumbering", StringComparison.OrdinalIgnoreCase);

                bool isExempt = (config.AlwaysShowDragons && isDragon) ||
                                (config.AlwaysShowUndead && data.AggroType == AggroType.Blood) ||
                                (config.AlwaysShowSprites && data.AggroType == AggroType.Magic);

                if (!isExempt && mobLevel > 0)
                {
                    // In FFXIV Eureka: mobs with level <= playerLevel - SafeLevelDifference will NEVER aggro
                    if (effectivePlayerLevel - mobLevel >= config.SafeLevelDifference)
                    {
                        continue;
                    }
                }
            }

            // Apply user-configured latency/safety margin buffer
            var totalRadius = data.TotalRadius + config.SafetyMargin;

            switch (data.AggroType)
            {
                case AggroType.Sound:
                    // Sound aggro in Eureka: Running wakes/triggers them, walking is 100% safe!
                    if (config.ShowSoundCircles)
                    {
                        var colDragon = config.ColorDragonSound;
                        var borderColor = ImGui.ColorConvertFloat4ToU32(colDragon);
                        var fillColor = ImGui.ColorConvertFloat4ToU32(new Vector4(colDragon.X, colDragon.Y, colDragon.Z, config.FillOpacity));

                        // Single clean sound aggro circle
                        DrawGroundCircle3D(drawList, mob.Position, totalRadius, borderColor, fillColor, config.FillShapes, 36);

                        // Check if mob is actually a sleeping dragon (e.g. slumbering dragon, voidragon)
                        bool isDragon = data.Name.Contains("dragon", StringComparison.OrdinalIgnoreCase) ||
                                        data.Name.Contains("wyrm", StringComparison.OrdinalIgnoreCase) ||
                                        data.Name.Contains("slumbering", StringComparison.OrdinalIgnoreCase);

                        // Alert when running near sound-detecting monsters
                        if (gameGui.WorldToScreen(mob.Position + new Vector3(0, mob.HitboxRadius + 1.4f, 0), out var pWarn))
                        {
                            if (isDragon)
                            {
                                if (distance <= totalRadius + 4.0f && isRunning)
                                {
                                    drawList.AddText(pWarn - new Vector2(75, 0), ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.2f, 0.2f, 1f)), "⚠ RUNNING NEAR DRAGON! WALK NOW (KEYPAD /)");
                                }
                                else if (distance <= totalRadius + 1.5f)
                                {
                                    drawList.AddText(pWarn - new Vector2(50, 0), borderColor, "✔ SLEEPING DRAGON (SAFE)");
                                }
                            }
                            else
                            {
                                // Non-dragon sound monsters (Clipper, Karlabos, Piranu, Crabs, etc.)
                                if (distance <= totalRadius + 3.0f && isRunning)
                                {
                                    drawList.AddText(pWarn - new Vector2(65, 0), ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.35f, 0.2f, 1f)), "⚠ SOUND AGGRO! WALK TO AVOID (KEYPAD /)");
                                }
                            }
                        }
                    }
                    break;

                case AggroType.Blood:
                    // UNDEAD / ASHKIN: Detect low HP players (< 80%) from up to 25m
                    if (config.ShowBloodCircles)
                    {
                        bool isLowHp = playerHpPercent < 80f;
                        float bloodRadius = isLowHp ? (config.BloodAggroDistance + config.SafetyMargin) : totalRadius;

                        var colBlood = isLowHp ? new Vector4(1.0f, 0.1f, 0.1f, 1.0f) : config.ColorBlood;
                        var borderColor = ImGui.ColorConvertFloat4ToU32(colBlood);
                        var fillColor = ImGui.ColorConvertFloat4ToU32(new Vector4(colBlood.X, colBlood.Y, colBlood.Z, isLowHp ? 0.35f : config.FillOpacity));

                        DrawGroundCircle3D(drawList, mob.Position, bloodRadius, borderColor, fillColor, config.FillShapes, 36);

                        if (isLowHp && gameGui.WorldToScreen(mob.Position + new Vector3(0, mob.HitboxRadius + 1.4f, 0), out var pAlert))
                        {
                            drawList.AddText(pAlert - new Vector2(75, 0), borderColor, "☠ HP < 80%: BLOOD AGGRO ACTIVE (HEAL TO SAFE)");
                        }
                    }
                    break;

                case AggroType.Magic:
                    // SPRITES / ELEMENTALS: Aggro on casting spells nearby (18m)
                    if (config.ShowMagicWarnings)
                    {
                        float magicRadius = config.MagicAggroDistance + config.SafetyMargin;
                        var colMagic = config.ColorMagic;
                        var borderColor = ImGui.ColorConvertFloat4ToU32(colMagic);
                        var fillColor = ImGui.ColorConvertFloat4ToU32(new Vector4(colMagic.X, colMagic.Y, colMagic.Z, config.FillOpacity));

                        DrawGroundCircle3D(drawList, mob.Position, magicRadius, borderColor, fillColor, config.FillShapes, 32);

                        if (gameGui.WorldToScreen(mob.Position + new Vector3(0, mob.HitboxRadius + 1.4f, 0), out var pSprite))
                        {
                            if (player.IsCasting && distance <= magicRadius)
                            {
                                drawList.AddText(pSprite - new Vector2(85, 0), ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.1f, 0.1f, 1f)), "⚡ CASTING DETECTED! SPRITE WILL AGGRO!");
                            }
                            else
                            {
                                drawList.AddText(pSprite - new Vector2(60, 0), borderColor, "⚡ Sprite [DO NOT CAST SPELLS]");
                            }
                        }
                    }
                    break;

                case AggroType.Proximity:
                    if (config.ShowProximityCircles)
                    {
                        var colProxi = GetDangerColor(data.DangerLevel);
                        var borderColor = ImGui.ColorConvertFloat4ToU32(colProxi);
                        var fillColor = ImGui.ColorConvertFloat4ToU32(new Vector4(colProxi.X, colProxi.Y, colProxi.Z, config.FillOpacity));

                        DrawGroundCircle3D(drawList, mob.Position, totalRadius, borderColor, fillColor, config.FillShapes, 32);
                    }
                    break;

                case AggroType.Sight:
                default:
                    if (config.ShowVisionCones)
                    {
                        var colVision = GetDangerColor(data.DangerLevel);
                        var borderColor = ImGui.ColorConvertFloat4ToU32(colVision);
                        var fillColor = ImGui.ColorConvertFloat4ToU32(new Vector4(colVision.X, colVision.Y, colVision.Z, config.FillOpacity));

                        DrawVisionCone3D(drawList, mob.Position, mob.Rotation, totalRadius, data.SightRadian, borderColor, fillColor, config.FillShapes, 20);
                    }

                    // In FFXIV Eureka, sight monsters also trigger aggro if touched within close proximity from behind
                    if (config.ShowProximityCircles)
                    {
                        float touchRadius = mob.HitboxRadius + 1.8f + config.SafetyMargin;
                        var colProxi = GetDangerColor(data.DangerLevel);
                        var borderColor = ImGui.ColorConvertFloat4ToU32(colProxi);
                        var fillColor = ImGui.ColorConvertFloat4ToU32(new Vector4(colProxi.X, colProxi.Y, colProxi.Z, config.FillOpacity));

                        DrawGroundCircle3D(drawList, mob.Position, touchRadius, borderColor, fillColor, config.FillShapes, 24);
                    }
                    break;
            }

            // High precision distance guide line (< 10m red, >= 10m green)
            if (config.ShowDistanceLines && distance < 12.0f)
            {
                var lineCol = distance < 10.0f
                    ? ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.15f, 0.15f, 0.95f))
                    : ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 1f, 0.2f, 0.95f));

                if (gameGui.WorldToScreen(playerPos, out var pPlayer) &&
                    gameGui.WorldToScreen(mob.Position, out var pMob))
                {
                    drawList.AddLine(pPlayer, pMob, lineCol, 1.8f);
                }
            }

            // Floating mob labels
            if (config.ShowMobLabels)
            {
                var textHeight = mob.Position + new Vector3(0, mob.HitboxRadius + 0.8f, 0);
                if (gameGui.WorldToScreen(textHeight, out var pText))
                {
                    var typeLabel = data.AggroType switch
                    {
                        AggroType.Sound => "[Sound - Walk!]",
                        AggroType.Blood => "[Blood / Undead]",
                        AggroType.Magic => "[Magic / Sprite]",
                        AggroType.Proximity => "[Proximity]",
                        AggroType.Sight => "[Sight]",
                        _ => "[?]"
                    };

                    var textCol = ImGui.ColorConvertFloat4ToU32(GetDangerColor(data.DangerLevel));
                    string levelPrefix = mobLevel > 0 ? $"Lv.{mobLevel} " : string.Empty;
                    string fullText = $"{levelPrefix}{data.Name} {typeLabel} ({distance:F1}m)";
                    drawList.AddText(pText - new Vector2(30, 0), textCol, fullText);
                }
            }
        }
    }

    private Vector4 GetDangerColor(DangerLevel danger)
    {
        return danger switch
        {
            DangerLevel.Easy => config.ColorEasy,
            DangerLevel.Caution => config.ColorCaution,
            DangerLevel.Danger => config.ColorDanger,
            _ => config.ColorUnknown
        };
    }

    private void DrawGroundCircle3D(
        ImDrawListPtr drawList,
        Vector3 center,
        float radius,
        uint borderColor,
        uint fillColor,
        bool fill,
        int segments = 36)
    {
        segments = Math.Clamp(segments, 16, 120);
        int count = 0;
        float step = (float)(2 * Math.PI / segments);

        bool hasCenter = false;
        Vector2 center2D = Vector2.Zero;
        if (fill && fillColor != 0)
        {
            hasCenter = gameGui.WorldToScreen(center, out center2D);
        }

        for (int i = 0; i < segments; i++)
        {
            float ang = i * step;
            var pos3D = new Vector3(
                center.X + radius * (float)Math.Sin(ang),
                center.Y,
                center.Z + radius * (float)Math.Cos(ang)
            );

            if (gameGui.WorldToScreen(pos3D, out var pos2D))
            {
                screenPointsBuffer[count++] = pos2D;
            }
        }

        if (count < 3) return;

        // Only draw fill triangles if explicitly requested and center is on screen
        if (fill && fillColor != 0 && hasCenter)
        {
            for (int i = 0; i < count; i++)
            {
                drawList.AddTriangleFilled(center2D, screenPointsBuffer[i], screenPointsBuffer[(i + 1) % count], fillColor);
            }
        }

        // Clean hollow boundary loop
        for (int i = 0; i < count; i++)
        {
            var p1 = screenPointsBuffer[i];
            var p2 = screenPointsBuffer[(i + 1) % count];
            drawList.AddLine(p1, p2, borderColor, config.BorderThickness);
        }
    }

    private void DrawVisionCone3D(
        ImDrawListPtr drawList,
        Vector3 center,
        float rotation,
        float radius,
        float fovAngle,
        uint borderColor,
        uint fillColor,
        bool fill,
        int segments = 24)
    {
        if (!gameGui.WorldToScreen(center, out var center2D)) return;

        segments = Math.Clamp(segments, 8, 100);
        int count = 0;

        float halfAngle = fovAngle / 2.0f;
        float startAngle = rotation - halfAngle;
        float step = fovAngle / segments;

        for (int i = 0; i <= segments; i++)
        {
            float ang = startAngle + i * step;
            var pos3D = new Vector3(
                center.X + radius * (float)Math.Sin(ang),
                center.Y,
                center.Z + radius * (float)Math.Cos(ang)
            );

            if (gameGui.WorldToScreen(pos3D, out var p2D))
            {
                screenPointsBuffer[count++] = p2D;
            }
        }

        if (count < 2) return;

        // Fill only if explicitly requested
        if (fill && fillColor != 0)
        {
            for (int i = 0; i < count - 1; i++)
            {
                drawList.AddTriangleFilled(center2D, screenPointsBuffer[i], screenPointsBuffer[i + 1], fillColor);
            }
        }

        // Clean hollow outline: Left ray from mob, arc perimeter, and Right ray back to mob
        drawList.AddLine(center2D, screenPointsBuffer[0], borderColor, config.BorderThickness);
        for (int i = 0; i < count - 1; i++)
        {
            drawList.AddLine(screenPointsBuffer[i], screenPointsBuffer[i + 1], borderColor, config.BorderThickness);
        }
        drawList.AddLine(screenPointsBuffer[count - 1], center2D, borderColor, config.BorderThickness);
    }
}
