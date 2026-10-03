namespace EurekaAggro.Models;

/// <summary>
/// Defines how a monster detects and engages the player in Eureka.
/// </summary>
public enum AggroType
{
    /// <summary>
    /// Unknown or unclassified aggro type.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Sight aggro: detects players entering its frontal vision cone.
    /// </summary>
    Sight = 1,

    /// <summary>
    /// Sound aggro: critical for Sleeping Dragons in Eureka.
    /// Running within their detection radius wakes them up.
    /// Walking (or riding mounts at walk speed) is 100% safe.
    /// </summary>
    Sound = 2,

    /// <summary>
    /// Proximity aggro: detects players within a 360-degree radius regardless of facing or movement.
    /// </summary>
    Proximity = 3,

    /// <summary>
    /// Blood aggro: common for Undead / Ashkin in Eureka.
    /// Detects players with less than 80% HP from very long distances (up to 30m).
    /// </summary>
    Blood = 4,

    /// <summary>
    /// Magic aggro: characteristic of Sprites / Elementals in Eureka.
    /// Attacks if a player casts a spell nearby.
    /// </summary>
    Magic = 5
}
