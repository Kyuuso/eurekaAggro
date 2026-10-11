using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using EurekaSuite.Configuration;
using EurekaSuite.Data;
using EurekaSuite.Models;
using EurekaSuite.Services;
using EurekaSuite.Localization;

namespace EurekaSuite.Rendering;

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
    private readonly EurekaEnvironmentService environmentService;
    private readonly PluginConfiguration config;

    // Player speed tracking for walk vs run detection
    private Vector3 lastPlayerPosition = Vector3.Zero;
    private DateTime lastPositionTime = DateTime.UtcNow;
    private float currentSpeed = 0.0f;

    private readonly DragonWalkService dragonWalkService;

    // Game defaults that the "Base monster aggro radius" and "Sight cone arc angle" sliders are relative to
    private const float BuiltInAggroDistance = 10.2f;
    private const float BuiltInSightDegrees = 100.0f;

    /// <summary>
    /// Per-object data that only changes when the object, its level, or the UI language changes.
    /// Avoids reading the SeString name and rebuilding label text on every frame.
    /// </summary>
    private sealed class MobRenderCache
    {
        public uint BaseId;
        // Localized name as shown on nameplates, used to look up nameplate levels
        public string Name = string.Empty;
        // English name, used for classification keywords on any client language
        public string EnglishName = string.Empty;
        public bool IsPet;
        public string LabelPrefix = string.Empty;
        public string LabelDataName = string.Empty;
        public string LabelTypeText = string.Empty;
        public byte LabelLevel;
        public float LabelPrefixWidth;
        public float LabelFontSize;
        public long LastSeenFrame;
    }

    private readonly Dictionary<ulong, MobRenderCache> mobCache = new();
    private readonly List<ulong> staleCacheKeys = new();
    private long frameCounter;
    private uint lastTerritory;
    private const int CachePruneIntervalFrames = 600;

    // " (12.3m)" for every 0.1m step up to the maximum detection range, built once
    private static readonly string[] DistanceTexts = BuildDistanceTexts(1000);

    private string cachedBadgeName = string.Empty;
    private int cachedBadgeDistance = -1;
    private string cachedBadgeText = string.Empty;

    private static string[] BuildDistanceTexts(int steps)
    {
        var texts = new string[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            texts[i] = $" ({i / 10f:F1}m)";
        }
        return texts;
    }

    private static string GetDistanceText(float distance)
    {
        int index = (int)MathF.Round(distance * 10f);
        return DistanceTexts[Math.Clamp(index, 0, DistanceTexts.Length - 1)];
    }

    // Cached localized overlay strings (zero allocations per rendering frame)
    private static string cachedLanguage = string.Empty;
    private static string textAutoWalkSafe = "[OK] AUTO-WALK ENGAGED (SAFE)";
    private static string textRunningNearDragon = "[WARN] RUNNING NEAR DRAGON! WALK NOW (KEYPAD /)";
    private static string textSleepingDragonSafe = "[SAFE] SLEEPING DRAGON";
    private static string textSoundAggroWarn = "[WARN] SOUND AGGRO! WALK TO AVOID (KEYPAD /)";
    private static string textBloodAggroAlert = "[ALERT] HP < 80%: BLOOD AGGRO ACTIVE (HEAL TO SAFE)";
    private static string textCastingDetected = "[ALERT] CASTING DETECTED! SPRITE WILL AGGRO!";
    private static string textDoNotCastSpells = "[MAGIC] Sprite: DO NOT CAST SPELLS";
    private static string labelSound = "[Sound - Walk!]";
    private static string labelBlood = "[Blood / Undead]";
    private static string labelMagic = "[Magic / Sprite]";
    private static string labelProximity = "[Proximity]";
    private static string labelSight = "[Sight]";
    private static string labelUnknown = "[?]";

    private static void EnsureLocalizedStrings()
    {
        var currentLang = Loc.CurrentLanguage;
        if (cachedLanguage == currentLang) return;
        cachedLanguage = currentLang;

        textAutoWalkSafe = Loc.T("[OK] AUTO-WALK ENGAGED (SAFE)");
        textRunningNearDragon = Loc.T("[WARN] RUNNING NEAR DRAGON! WALK NOW (KEYPAD /)");
        textSleepingDragonSafe = Loc.T("[SAFE] SLEEPING DRAGON");
        textSoundAggroWarn = Loc.T("[WARN] SOUND AGGRO! WALK TO AVOID (KEYPAD /)");
        textBloodAggroAlert = Loc.T("[ALERT] HP < 80%: BLOOD AGGRO ACTIVE (HEAL TO SAFE)");
        textCastingDetected = Loc.T("[ALERT] CASTING DETECTED! SPRITE WILL AGGRO!");
        textDoNotCastSpells = Loc.T("[MAGIC] Sprite: DO NOT CAST SPELLS");
        labelSound = Loc.T("[Sound - Walk!]");
        labelBlood = Loc.T("[Blood / Undead]");
        labelMagic = Loc.T("[Magic / Sprite]");
        labelProximity = Loc.T("[Proximity]");
        labelSight = Loc.T("[Sight]");
        labelUnknown = Loc.T("[?]");
    }

    public OverlayRenderer(
        IGameGui gameGui,
        IClientState clientState,
        IObjectTable objectTable,
        MobDatabase mobDatabase,
        EurekaEnvironmentService environmentService,
        DragonWalkService dragonWalkService,
        PluginConfiguration config)
    {
        this.gameGui = gameGui;
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.mobDatabase = mobDatabase;
        this.environmentService = environmentService;
        this.dragonWalkService = dragonWalkService;
        this.config = config;
    }

    /// <summary>
    /// Frame-by-frame rendering loop called from Dalamud's UI draw callback.
    /// </summary>
    public void Draw()
    {
        if (!config.Enabled || !clientState.IsLoggedIn) return;

        EnsureLocalizedStrings();

        if (!ValidZones.IsValidZone(clientState.TerritoryType, config.OnlyInEureka))
        {
            return;
        }

        var player = objectTable.LocalPlayer;
        if (player == null) return;

        frameCounter++;
        var territory = clientState.TerritoryType;
        if (territory != lastTerritory)
        {
            // Object IDs are reused between instances
            lastTerritory = territory;
            mobCache.Clear();
            EurekaLevelService.ClearObjectLevels();
        }
        else if (frameCounter % CachePruneIntervalFrames == 0)
        {
            PruneMobCache();
        }

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

            var distance = Vector3.Distance(playerPos, mob.Position);
            if (distance > config.DetectionRange) continue;

            // Name-based safety guard against pets and summons
            var cache = GetMobCache(mob);
            if (cache.IsPet) continue;
            var mobName = cache.EnglishName;

            var data = mobDatabase.GetOrRegister(mob.BaseId, mobName, mob.HitboxRadius);

            // In Eureka (especially Pagos & Pyros cliffs and caves), standard mobs cannot aggro across vertical ledges.
            // However, Sleeping Dragons have 3D spherical sound detection and ALWAYS aggro regardless of height!
            bool isDragonCheck = data.AggroType == AggroType.Sound ||
                                 data.Name.Contains("dragon", StringComparison.OrdinalIgnoreCase) ||
                                 data.Name.Contains("wyrm", StringComparison.OrdinalIgnoreCase) ||
                                 data.Name.Contains("slumbering", StringComparison.OrdinalIgnoreCase) ||
                                 mobName.Contains("sleeping", StringComparison.OrdinalIgnoreCase) ||
                                 mobName.Contains("voidragon", StringComparison.OrdinalIgnoreCase);

            if (config.EnableVerticalFilter && !isDragonCheck)
            {
                var verticalDiff = Math.Abs(playerPos.Y - mob.Position.Y);
                if (verticalDiff > config.VerticalTolerance) continue;
            }

            // Resolve true Eureka Elemental Level (extracts from NamePlate to avoid Stormblood dummy sync Lv.70)
            byte mobLevel = EurekaLevelService.GetMobElementalLevel(
                mob.GameObjectId,
                mob.BaseId,
                cache.Name,
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
                bool isExempt = (config.AlwaysShowDragons && isDragonCheck) ||
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

            // Sound radius comes from the dragon slider; sight and proximity ranges are shifted by the base radius slider
            float aggroDistance = data.AggroType switch
            {
                AggroType.Sound => config.DragonRunAggroDistance,
                AggroType.Blood or AggroType.Magic => data.AggroDistance,
                _ => data.AggroDistance + (config.DefaultAggroDistance - BuiltInAggroDistance)
            };

            // Apply user-configured latency/safety margin buffer
            var totalRadius = data.HitboxRadius + MathF.Max(0f, aggroDistance) + config.SafetyMargin;
            float sightAngle = data.SightRadian * (config.SightAngleDegrees / BuiltInSightDegrees);

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
                                        data.Name.Contains("slumbering", StringComparison.OrdinalIgnoreCase) ||
                                        data.Name.Contains("sleeping", StringComparison.OrdinalIgnoreCase);

                        // Alert when running near sound-detecting monsters
                        if (gameGui.WorldToScreen(mob.Position + new Vector3(0, mob.HitboxRadius + 1.4f, 0), out var pWarn))
                        {
                            if (isDragon)
                            {
                                if (dragonWalkService.IsAutoWalkEngaged && distance <= config.AutoWalkDistance)
                                {
                                    drawList.AddText(pWarn - new Vector2(75, 0), ImGui.ColorConvertFloat4ToU32(config.ColorDragonSafeText), textAutoWalkSafe);
                                }
                                else if (distance <= totalRadius + 4.0f && isRunning)
                                {
                                    drawList.AddText(pWarn - new Vector2(75, 0), ImGui.ColorConvertFloat4ToU32(config.ColorDragonWarningText), textRunningNearDragon);
                                }
                                else if (distance <= totalRadius + 1.5f)
                                {
                                    drawList.AddText(pWarn - new Vector2(50, 0), borderColor, textSleepingDragonSafe);
                                }
                            }
                            else
                            {
                                // Non-dragon sound monsters (Clipper, Karlabos, Piranu, Crabs, etc.)
                                if (distance <= totalRadius + 3.0f && isRunning)
                                {
                                    drawList.AddText(pWarn - new Vector2(65, 0), ImGui.ColorConvertFloat4ToU32(config.ColorDragonWarningText), textSoundAggroWarn);
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
                            drawList.AddText(pAlert - new Vector2(75, 0), borderColor, textBloodAggroAlert);
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
                                drawList.AddText(pSprite - new Vector2(85, 0), ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.1f, 0.1f, 1f)), textCastingDetected);
                            }
                            else
                            {
                                drawList.AddText(pSprite - new Vector2(60, 0), borderColor, textDoNotCastSpells);
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

                        DrawVisionCone3D(drawList, mob.Position, mob.Rotation, totalRadius, sightAngle, borderColor, fillColor, config.FillShapes, 20);
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
                    ? ImGui.ColorConvertFloat4ToU32(config.ColorDistanceNear)
                    : ImGui.ColorConvertFloat4ToU32(config.ColorDistanceFar);

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
                        AggroType.Sound => labelSound,
                        AggroType.Blood => labelBlood,
                        AggroType.Magic => labelMagic,
                        AggroType.Proximity => labelProximity,
                        AggroType.Sight => labelSight,
                        _ => labelUnknown
                    };

                    var textCol = ImGui.ColorConvertFloat4ToU32(
                        config.UseCustomMobTextColor
                            ? config.ColorCustomMobText
                            : GetDangerColor(data.DangerLevel));

                    // The prefix is rebuilt only when level, name or language changes; the distance comes from a prebuilt table
                    var labelPos = pText - new Vector2(30, 0);
                    var prefix = GetLabelPrefix(cache, mobLevel, data.Name, typeLabel);
                    drawList.AddText(labelPos, textCol, prefix);
                    drawList.AddText(labelPos + new Vector2(cache.LabelPrefixWidth, 0), textCol, GetDistanceText(distance));

                    // Real-time Mutation & Adaptation triggers
                    if (config.ShowMutationStatus)
                    {
                        var mut = environmentService.CheckMutation(data.Name);
                        if (mut.CanMutate)
                        {
                            var mutCol = ImGui.ColorConvertFloat4ToU32(mut.IsActiveNow
                                ? config.ColorMutationActiveText
                                : config.ColorMutationInactiveText);
                            drawList.AddText(pText - new Vector2(30, -14), mutCol, mut.OverlayText);
                        }
                    }
                }
            }
        }

        // On-screen notification badge when Auto-Walk is active near a Sleeping Dragon
        if (dragonWalkService.IsAutoWalkEngaged)
        {
            var viewport = ImGui.GetMainViewport();
            var screenCenter = new Vector2(viewport.Size.X / 2f, viewport.Size.Y * 0.82f);
            var safeCol = ImGui.ColorConvertFloat4ToU32(config.ColorDragonSafeText);
            var bgCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.04f, 0.04f, 0.06f, 0.85f));
            string badgeText = GetBadgeText(dragonWalkService.CurrentDragonName, dragonWalkService.CurrentDistance);
            var textSize = ImGui.CalcTextSize(badgeText);
            var pMin = screenCenter - (textSize / 2f) - new Vector2(14, 6);
            var pMax = screenCenter + (textSize / 2f) + new Vector2(14, 6);
            drawList.AddRectFilled(pMin, pMax, bgCol, 6f);
            drawList.AddRect(pMin, pMax, safeCol, 6f, ImDrawFlags.None, 1.5f);
            drawList.AddText(screenCenter - (textSize / 2f), safeCol, badgeText);
        }
    }

    private MobRenderCache GetMobCache(IBattleChara mob)
    {
        if (!mobCache.TryGetValue(mob.GameObjectId, out var cache) || cache.BaseId != mob.BaseId)
        {
            var englishName = MobNameResolver.GetEnglishName(mob);
            cache = new MobRenderCache
            {
                BaseId = mob.BaseId,
                Name = mob.Name.TextValue,
                EnglishName = englishName,
                IsPet = MobDatabase.IsPlayerPetOrCompanion(englishName),
            };
            mobCache[mob.GameObjectId] = cache;
        }

        cache.LastSeenFrame = frameCounter;
        return cache;
    }

    private static string GetLabelPrefix(MobRenderCache cache, byte level, string dataName, string typeLabel)
    {
        float fontSize = ImGui.GetFontSize();
        bool textChanged = cache.LabelLevel != level ||
                           !ReferenceEquals(cache.LabelDataName, dataName) ||
                           !ReferenceEquals(cache.LabelTypeText, typeLabel) ||
                           cache.LabelPrefix.Length == 0;

        if (textChanged)
        {
            cache.LabelLevel = level;
            cache.LabelDataName = dataName;
            cache.LabelTypeText = typeLabel;
            cache.LabelPrefix = level > 0 ? $"Lv.{level} {dataName} {typeLabel}" : $"{dataName} {typeLabel}";
        }

        if (textChanged || cache.LabelFontSize != fontSize)
        {
            cache.LabelFontSize = fontSize;
            cache.LabelPrefixWidth = ImGui.CalcTextSize(cache.LabelPrefix).X;
        }

        return cache.LabelPrefix;
    }

    private string GetBadgeText(string dragonName, float distance)
    {
        int bucket = (int)MathF.Round(distance * 10f);
        if (bucket != cachedBadgeDistance || !ReferenceEquals(dragonName, cachedBadgeName))
        {
            cachedBadgeDistance = bucket;
            cachedBadgeName = dragonName;
            cachedBadgeText = $"[AUTO-WALK] ENGAGED ({dragonName} - {distance:F1}m)";
        }
        return cachedBadgeText;
    }

    private void PruneMobCache()
    {
        staleCacheKeys.Clear();
        foreach (var (id, entry) in mobCache)
        {
            if (frameCounter - entry.LastSeenFrame > CachePruneIntervalFrames)
            {
                staleCacheKeys.Add(id);
            }
        }
        foreach (var id in staleCacheKeys)
        {
            mobCache.Remove(id);
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
        float step = (float)(2 * Math.PI / segments);

        bool hasCenter = false;
        Vector2 center2D = Vector2.Zero;
        if (fill && fillColor != 0)
        {
            hasCenter = gameGui.WorldToScreen(center, out center2D);
        }

        for (int i = 0; i < segments; i++)
        {
            float ang1 = i * step;
            float ang2 = (i + 1) * step;

            var p1_3D = new Vector3(
                center.X + radius * (float)Math.Sin(ang1),
                center.Y,
                center.Z + radius * (float)Math.Cos(ang1)
            );
            var p2_3D = new Vector3(
                center.X + radius * (float)Math.Sin(ang2),
                center.Y,
                center.Z + radius * (float)Math.Cos(ang2)
            );

            if (gameGui.WorldToScreen(p1_3D, out var s1) && gameGui.WorldToScreen(p2_3D, out var s2))
            {
                if (fill && fillColor != 0 && hasCenter)
                {
                    drawList.AddTriangleFilled(center2D, s1, s2, fillColor);
                }
                drawList.AddLine(s1, s2, borderColor, config.BorderThickness);
            }
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
        segments = Math.Clamp(segments, 8, 100);

        float halfAngle = fovAngle / 2.0f;
        float startAngle = rotation - halfAngle;
        float endAngle = rotation + halfAngle;

        bool hasCenter = gameGui.WorldToScreen(center, out var center2D);

        // 1. Left bounding ray from center to left arc limit
        var left3D = new Vector3(
            center.X + radius * (float)Math.Sin(startAngle),
            center.Y,
            center.Z + radius * (float)Math.Cos(startAngle)
        );
        if (hasCenter && gameGui.WorldToScreen(left3D, out var left2D))
        {
            drawList.AddLine(center2D, left2D, borderColor, config.BorderThickness);
        }

        // 2. Right bounding ray from center to right arc limit
        var right3D = new Vector3(
            center.X + radius * (float)Math.Sin(endAngle),
            center.Y,
            center.Z + radius * (float)Math.Cos(endAngle)
        );
        if (hasCenter && gameGui.WorldToScreen(right3D, out var right2D))
        {
            drawList.AddLine(center2D, right2D, borderColor, config.BorderThickness);
        }

        // 3. Frontal arc segments
        float step = fovAngle / segments;
        for (int i = 0; i < segments; i++)
        {
            float ang1 = startAngle + i * step;
            float ang2 = startAngle + (i + 1) * step;

            var p1_3D = new Vector3(
                center.X + radius * (float)Math.Sin(ang1),
                center.Y,
                center.Z + radius * (float)Math.Cos(ang1)
            );
            var p2_3D = new Vector3(
                center.X + radius * (float)Math.Sin(ang2),
                center.Y,
                center.Z + radius * (float)Math.Cos(ang2)
            );

            if (gameGui.WorldToScreen(p1_3D, out var s1) && gameGui.WorldToScreen(p2_3D, out var s2))
            {
                if (fill && fillColor != 0 && hasCenter)
                {
                    drawList.AddTriangleFilled(center2D, s1, s2, fillColor);
                }
                drawList.AddLine(s1, s2, borderColor, config.BorderThickness);
            }
        }
    }
}
