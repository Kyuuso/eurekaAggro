namespace EurekaAggro.Models;

/// <summary>
/// Contains aggro parameters and threat data for a monster in Eureka.
/// </summary>
public class MobData
{
    /// <summary>
    /// Display name of the monster.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Aggro type (Sight, Sound, Proximity, Blood, Magic).
    /// </summary>
    public AggroType AggroType { get; set; } = AggroType.Unknown;

    /// <summary>
    /// Danger level rating.
    /// </summary>
    public DangerLevel DangerLevel { get; set; } = DangerLevel.Unknown;

    /// <summary>
    /// Eureka Elemental Level of the monster (e.g. 52 for Hydatos Ziz).
    /// </summary>
    public byte ElementalLevel { get; set; } = 0;

    /// <summary>
    /// Hitbox collision radius in meters.
    /// </summary>
    public float HitboxRadius { get; set; } = 1.0f;

    /// <summary>
    /// Additional aggro distance in meters (defaults to ~10.2m).
    /// </summary>
    public float AggroDistance { get; set; } = 10.2f;

    /// <summary>
    /// Field of view cone angle in radians (defaults to ~100 degrees or 1.745 rad).
    /// </summary>
    public float SightRadian { get; set; } = 1.74533f;

    /// <summary>
    /// Total detection radius combining hitbox and aggro distance.
    /// </summary>
    public float TotalRadius => HitboxRadius + AggroDistance;
}
