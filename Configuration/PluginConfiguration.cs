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
    /// </summary>
    public bool FilterSafeMobs { get; set; } = true;

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
    }

    public void Save()
    {
        pluginInterface?.SavePluginConfig(this);
    }
}
