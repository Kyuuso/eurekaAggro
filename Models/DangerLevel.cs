namespace EurekaAggro.Models;

/// <summary>
/// Threat or danger level of an enemy.
/// </summary>
public enum DangerLevel
{
    /// <summary>
    /// Unknown threat level.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Easy (Green): standard monster without lethal mechanics.
    /// </summary>
    Easy = 1,

    /// <summary>
    /// Caution (Orange): monster with high damage or dangerous mechanics.
    /// </summary>
    Caution = 2,

    /// <summary>
    /// Danger (Red): lethal monster (e.g. Sleeping Dragon, high-level NM, one-shot mechanic).
    /// </summary>
    Danger = 3
}
