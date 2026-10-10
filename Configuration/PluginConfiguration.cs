using System;
using System.Numerics;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace EurekaAggro.Configuration;

/// <summary>
/// Persistent plugin configuration for EurekaAggro.
/// Stores visual settings, detection ranges, safety margins, and color palettes.
/// </summary>
[Serializable]
public class PluginConfiguration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // --- GENERAL ACTIVATION ---

    /// <summary>
    /// Master toggle for radar processing and drawing.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When true, radar only activates inside Eureka expedition zones (Anemos, Pagos, Pyros, Hydatos).
    /// </summary>
    public bool OnlyInEureka { get; set; } = true;

    /// <summary>
    /// Maximum detection distance in meters to scan and draw monsters.
    /// </summary>
    public float DetectionRange { get; set; } = 60.0f;

    /// <summary>
    /// Latency / ping safety margin added to aggro radii (defaults to +0.3m for server tick safety).
    /// </summary>
    public float SafetyMargin { get; set; } = 0.3f;

    // --- EUREKA AGGRO MECHANICS ---

    /// <summary>
    /// Running sound aggro distance for Sleeping Dragons in Eureka (defaults to 10.5m).
    /// </summary>
    public float DragonRunAggroDistance { get; set; } = 10.5f;

    /// <summary>
    /// Blood aggro detection distance for Undead / Ashkin when player HP < 80% (defaults to 25.0m).
    /// </summary>
    public float BloodAggroDistance { get; set; } = 25.0f;

    /// <summary>
    /// Magic aggro detection distance for Sprites / Elementals when casting spells (defaults to 18.0m).
    /// </summary>
    public float MagicAggroDistance { get; set; } = 18.0f;

    /// <summary>
    /// Base aggro distance for standard sight/proximity mobs (defaults to 10.2m).
    /// </summary>
    public float DefaultAggroDistance { get; set; } = 10.2f;

    /// <summary>
    /// Field of view cone angle in degrees (defaults to 100 degrees / 1.745 rad).
    /// </summary>
    public float SightAngleDegrees { get; set; } = 100.0f;

    /// <summary>
    /// When true, monsters on cliffs, caves, or different elevations (|delta Y| > VerticalTolerance) are filtered out.
    /// Critical for Eureka Pagos and Pyros multi-tier topography.
    /// </summary>
    public bool EnableVerticalFilter { get; set; } = true;

    /// <summary>
    /// Vertical elevation difference threshold in meters (defaults to 5.5m).
    /// In FFXIV Eureka, monsters cannot aggro across vertical ledges or caves beyond this height.
    /// </summary>
    public float VerticalTolerance { get; set; } = 5.5f;

    /// <summary>
    /// Movement speed threshold in m/s below which the player is considered walking (defaults to 2.8 m/s).
    /// Normal walking is ~2.4 m/s; running is ~6.0 m/s.
    /// </summary>
    public float DragonWalkSpeedThreshold { get; set; } = 2.8f;

    // --- LEVEL FILTERING ---

    /// <summary>
    /// When true, filters out lower-level monsters that will not aggro you.
    /// Defaults to false so radars show all monsters regardless of player level.
    /// </summary>
    public bool FilterSafeMobs { get; set; } = false;

    /// <summary>
    /// When true, automatically detects and synchronizes your character's Elemental Level in real time
    /// using game memory and expedition zone caps (Anemos 20, Pagos 35, Pyros 50, Hydatos 60).
    /// </summary>
    public bool AutoDetectElementalLevel { get; set; } = true;

    /// <summary>
    /// Your character's current Eureka Elemental Level (1 to 60).
    /// Defaults to 60 (Hydatos cap).
    /// </summary>
    public int PlayerElementalLevel { get; set; } = 60;

    /// <summary>
    /// Safe level difference threshold in levels (defaults to 2).
    /// In FFXIV Eureka, standard sight/proximity mobs do not aggro if player level - mob level >= 2.
    /// </summary>
    public int SafeLevelDifference { get; set; } = 2;

    /// <summary>
    /// When level filtering is enabled, always keep showing Sleeping Dragons (Sound aggro).
    /// </summary>
    public bool AlwaysShowDragons { get; set; } = true;

    /// <summary>
    /// When enabled, automatically engages Walk mode (IsWalking) when running near Sleeping Dragons.
    /// Restores running automatically once safely out of range.
    /// </summary>
    public bool AutoWalkNearDragons { get; set; } = true;

    /// <summary>
    /// Proximity distance in meters to automatically trigger Walk mode near Sleeping Dragons (defaults to 16.0m).
    /// </summary>
    public float AutoWalkDistance { get; set; } = 16.0f;

    /// <summary>
    /// When true, prints notifications to the Dalamud in-game chat when auto-walk engages/disengages.
    /// </summary>
    public bool LogAutoWalkToChat { get; set; } = false;

    /// <summary>
    /// Displays real-time mutation and adaptation availability indicators above eligible monsters based on active weather and Eorzea time.
    /// </summary>
    public bool ShowMutationStatus { get; set; } = true;

    /// <summary>
    /// When level filtering is enabled, always keep showing Undead / Ashkin (Blood aggro).
    /// </summary>
    public bool AlwaysShowUndead { get; set; } = true;

    /// <summary>
    /// When level filtering is enabled, always keep showing Sprites / Elementals (Magic aggro).
    /// </summary>
    public bool AlwaysShowSprites { get; set; } = true;

    // --- TOGGLES ---

    /// <summary>
    /// Draw frontal vision cone for Sight aggro monsters.
    /// </summary>
    public bool ShowVisionCones { get; set; } = true;

    /// <summary>
    /// Draw running vs walking sound circles for Sleeping Dragons in Eureka.
    /// </summary>
    public bool ShowSoundCircles { get; set; } = true;

    /// <summary>
    /// Draw 360-degree proximity aggro circles.
    /// </summary>
    public bool ShowProximityCircles { get; set; } = true;

    /// <summary>
    /// Draw blood detection radius for Undead / Ashkin (turns red when player HP < 80%).
    /// </summary>
    public bool ShowBloodCircles { get; set; } = true;

    /// <summary>
    /// Draw magic detection warning for Sprites / Elementals.
    /// </summary>
    public bool ShowMagicWarnings { get; set; } = true;

    /// <summary>
    /// Fill 3D ground circles and cones with semi-transparent colors (false = clean hollow outlines).
    /// </summary>
    public bool FillShapes { get; set; } = false;

    /// <summary>
    /// Fill opacity for shapes (0.05 to 0.8).
    /// </summary>
    public float FillOpacity { get; set; } = 0.25f;

    /// <summary>
    /// Border line thickness.
    /// </summary>
    public float BorderThickness { get; set; } = 2.0f;

    /// <summary>
    /// Distance guide line from player to near monsters (< 10m red, >= 10m green).
    /// </summary>
    public bool ShowDistanceLines { get; set; } = true;

    /// <summary>
    /// Floating text labels above monsters indicating name, distance, and aggro type.
    /// </summary>
    public bool ShowMobLabels { get; set; } = true;

    /// <summary>
    /// Display floating HUD banner alert when a dangerous action is being cast.
    /// </summary>
    public bool ShowCastAlerts { get; set; } = true;

    /// <summary>
    /// When true, only shows cast alerts for your current target instead of all nearby casting enemies.
    /// </summary>
    public bool CastAlertsTargetOnly { get; set; } = false;

    /// <summary>
    /// When true, locks the cast alert HUD window in place so it cannot be accidentally moved with the mouse.
    /// </summary>
    public bool LockCastAlertPosition { get; set; } = false;

    /// <summary>
    /// User-customized screen position for the cast alert window (or -1, -1 for default).
    /// </summary>
    public Vector2 CastAlertPosition { get; set; } = new(-1, -1);

    /// <summary>
    /// Print a notification in in-game chat when a dangerous cast is detected.
    /// </summary>
    public bool NotifyCastInChat { get; set; } = false;

    // --- COLOR PALETTE (RGBA) ---

    public Vector4 ColorEasy { get; set; } = new(0.1f, 0.9f, 0.2f, 1.0f);
    public Vector4 ColorCaution { get; set; } = new(1.0f, 0.55f, 0.0f, 1.0f);
    public Vector4 ColorDanger { get; set; } = new(1.0f, 0.15f, 0.15f, 1.0f);
    public Vector4 ColorUnknown { get; set; } = new(0.2f, 0.7f, 1.0f, 1.0f);
    public Vector4 ColorDragonSound { get; set; } = new(1.0f, 0.85f, 0.1f, 1.0f);
    public Vector4 ColorBlood { get; set; } = new(0.9f, 0.1f, 0.25f, 1.0f);
    public Vector4 ColorMagic { get; set; } = new(0.7f, 0.2f, 0.95f, 1.0f);

    public Vector4 ColorDragonSafeText { get; set; } = new(0.2f, 1.0f, 0.4f, 1.0f);
    public Vector4 ColorDragonWarningText { get; set; } = new(1.0f, 0.2f, 0.2f, 1.0f);
    public Vector4 ColorMutationActiveText { get; set; } = new(0.2f, 1.0f, 0.9f, 1.0f);
    public Vector4 ColorMutationInactiveText { get; set; } = new(0.65f, 0.65f, 0.65f, 0.75f);
    public Vector4 ColorDistanceNear { get; set; } = new(1.0f, 0.15f, 0.15f, 0.95f);
    public Vector4 ColorDistanceFar { get; set; } = new(0.2f, 1.0f, 0.2f, 0.95f);

    public bool UseCustomMobTextColor { get; set; } = false;
    public Vector4 ColorCustomMobText { get; set; } = new(1.0f, 1.0f, 1.0f, 1.0f);

    public void ResetColorsToDefault()
    {
        ColorEasy = new(0.1f, 0.9f, 0.2f, 1.0f);
        ColorCaution = new(1.0f, 0.55f, 0.0f, 1.0f);
        ColorDanger = new(1.0f, 0.15f, 0.15f, 1.0f);
        ColorUnknown = new(0.2f, 0.7f, 1.0f, 1.0f);
        ColorDragonSound = new(1.0f, 0.85f, 0.1f, 1.0f);
        ColorBlood = new(0.9f, 0.1f, 0.25f, 1.0f);
        ColorMagic = new(0.7f, 0.2f, 0.95f, 1.0f);
        ColorDragonSafeText = new(0.2f, 1.0f, 0.4f, 1.0f);
        ColorDragonWarningText = new(1.0f, 0.2f, 0.2f, 1.0f);
        ColorMutationActiveText = new(0.2f, 1.0f, 0.9f, 1.0f);
        ColorMutationInactiveText = new(0.65f, 0.65f, 0.65f, 0.75f);
        ColorDistanceNear = new(1.0f, 0.15f, 0.15f, 0.95f);
        ColorDistanceFar = new(0.2f, 1.0f, 0.2f, 0.95f);
        UseCustomMobTextColor = false;
        ColorCustomMobText = new(1.0f, 1.0f, 1.0f, 1.0f);
        Save();
    }

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pi)
    {
        pluginInterface = pi;

        // Migrate to version 2: enforce clean hollow outlines (no fills) by default
        if (Version < 2)
        {
            FillShapes = false;
            Version = 2;
            Save();
        }

        // Migrate to version 3: disable level filtering by default so mobs in lower zones are always visible
        if (Version < 3)
        {
            FilterSafeMobs = false;
            Version = 3;
            Save();
        }
    }

    // --- EUREKA TRACKER SETTINGS ---
    public bool TrackerAutoCreate { get; set; } = false;
    public bool TrackerCreatePublic { get; set; } = true;
    public bool TrackerAutoJoinExisting { get; set; } = true;
    public bool TrackerAutoPopFate { get; set; } = true;
    public bool TrackerDisplayFatePop { get; set; } = true;
    public bool TrackerDisplayToastPop { get; set; } = true;
    public bool TrackerPlayPopSound { get; set; } = true;
    public bool TrackerDisplayServerIdInChat { get; set; } = true;
    public bool TrackerShowLevelInTable { get; set; } = true;
    public string TrackerLastCode { get; set; } = string.Empty;
    public string TrackerLastPassword { get; set; } = string.Empty;
    public string TrackerCustomInstanceId { get; set; } = string.Empty;

    // --- TRACKER PASSWORD & HISTORY MEMORY ---
    public Dictionary<string, string> SavedTrackerPasswords { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<TrackerHistoryEntry> TrackerHistory { get; set; } = new();

    /// <summary>
    /// Remembers a tracker, its password, instance ID, and updates last used timestamp.
    /// </summary>
    public void RememberTracker(string trackerId, string? password = null, string? instanceId = null, int zoneId = 0)
    {
        if (string.IsNullOrWhiteSpace(trackerId)) return;
        string cleanId = trackerId.Trim();

        TrackerLastCode = cleanId;

        if (!string.IsNullOrWhiteSpace(password))
        {
            string cleanPwd = password.Trim();
            TrackerLastPassword = cleanPwd;
            SavedTrackerPasswords[cleanId] = cleanPwd;
        }
        else if (SavedTrackerPasswords.TryGetValue(cleanId, out var existingPwd))
        {
            TrackerLastPassword = existingPwd;
        }

        var existing = TrackerHistory.Find(e => string.Equals(e.TrackerId, cleanId, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.LastUsedAt = DateTimeOffset.UtcNow;
            if (!string.IsNullOrWhiteSpace(password)) existing.Password = password.Trim();
            if (!string.IsNullOrWhiteSpace(instanceId)) existing.InstanceId = instanceId.Trim();
            if (zoneId > 0) existing.ZoneId = zoneId;
        }
        else
        {
            TrackerHistory.Insert(0, new TrackerHistoryEntry
            {
                TrackerId = cleanId,
                Password = !string.IsNullOrWhiteSpace(password) ? password.Trim() : (SavedTrackerPasswords.TryGetValue(cleanId, out var p) ? p : string.Empty),
                InstanceId = instanceId?.Trim() ?? string.Empty,
                ZoneId = zoneId,
                CreatedAt = DateTimeOffset.UtcNow,
                LastUsedAt = DateTimeOffset.UtcNow,
            });
        }

        // Keep last 30 entries
        if (TrackerHistory.Count > 30)
        {
            TrackerHistory.RemoveRange(30, TrackerHistory.Count - 30);
        }

        Save();
    }

    /// <summary>
    /// Retrieves a saved edit password for a tracker ID if known.
    /// </summary>
    public string? GetSavedPassword(string? trackerId)
    {
        if (string.IsNullOrWhiteSpace(trackerId)) return null;
        if (SavedTrackerPasswords.TryGetValue(trackerId.Trim(), out var pwd) && !string.IsNullOrWhiteSpace(pwd))
        {
            return pwd;
        }
        return null;
    }

    /// <summary>
    /// Forgets a tracker and its password from history.
    /// </summary>
    public void ForgetTracker(string trackerId)
    {
        if (string.IsNullOrWhiteSpace(trackerId)) return;
        string cleanId = trackerId.Trim();
        SavedTrackerPasswords.Remove(cleanId);
        TrackerHistory.RemoveAll(e => string.Equals(e.TrackerId, cleanId, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(TrackerLastCode, cleanId, StringComparison.OrdinalIgnoreCase))
        {
            TrackerLastCode = string.Empty;
            TrackerLastPassword = string.Empty;
        }
        Save();
    }

    // --- AGGRO LINES STATISTICS ---
    public int LifetimeDragonsBypassed { get; set; } = 0;
    public int LifetimeAutoWalkActivations { get; set; } = 0;
    public int LifetimeCastAlertsTriggered { get; set; } = 0;
    public int LifetimeCloseCallsAvoided { get; set; } = 0;

    [NonSerialized]
    public int SessionDragonsBypassed = 0;
    [NonSerialized]
    public int SessionAutoWalkActivations = 0;
    [NonSerialized]
    public int SessionCastAlertsTriggered = 0;
    [NonSerialized]
    public int SessionCloseCallsAvoided = 0;

    /// <summary>
    /// Resets all session and lifetime statistics for the Aggro Lines radar.
    /// </summary>
    public void ResetAggroStats()
    {
        LifetimeDragonsBypassed = 0;
        LifetimeAutoWalkActivations = 0;
        LifetimeCastAlertsTriggered = 0;
        LifetimeCloseCallsAvoided = 0;
        SessionDragonsBypassed = 0;
        SessionAutoWalkActivations = 0;
        SessionCastAlertsTriggered = 0;
        SessionCloseCallsAvoided = 0;
        Save();
    }

    public void Save()
    {
        pluginInterface?.SavePluginConfig(this);
    }
}

/// <summary>
/// Persisted history entry for a previously visited or created Eureka Tracker.
/// Stores edit password, instance ID, zone, and visit timestamps.
/// </summary>
public class TrackerHistoryEntry
{
    public string TrackerId { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string InstanceId { get; set; } = string.Empty;
    public int ZoneId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastUsedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool HasPassword => !string.IsNullOrWhiteSpace(Password);

    public string GetAgeString()
    {
        var elapsed = DateTimeOffset.UtcNow - LastUsedAt.ToUniversalTime();
        if (elapsed.TotalMinutes < 1) return "just now";
        if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours}h ago";
        return $"{(int)elapsed.TotalDays}d ago";
    }
}
