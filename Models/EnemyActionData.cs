namespace EurekaAggro.Models;

/// <summary>
/// Data regarding an enemy action or spell cast, indicating required defensive counters.
/// </summary>
public class EnemyActionData
{
    /// <summary>
    /// Action / spell identifier.
    /// </summary>
    public uint ActionId { get; set; }

    /// <summary>
    /// Name of the monster casting this action.
    /// </summary>
    public string MobName { get; set; } = string.Empty;

    /// <summary>
    /// Name of the action or spell.
    /// </summary>
    public string ActionName { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether this cast should/can be Stunned (Low Blow / Leg Sweep).
    /// </summary>
    public bool RequiresStun { get; set; }

    /// <summary>
    /// Indicates whether this cast should be Silenced / Interrupted (Interject / Head Graze).
    /// </summary>
    public bool RequiresSilence { get; set; }

    /// <summary>
    /// Indicates whether this action inflicts heavy sustained damage requiring regeneration or healing.
    /// </summary>
    public bool RequiresRegen { get; set; }

    /// <summary>
    /// Indicates whether the player must break line of sight (LOS) behind an obstacle or pillar.
    /// </summary>
    public bool RequiresLineOfSight { get; set; }

    /// <summary>
    /// Indicates whether the cast is natively interruptible according to game data.
    /// </summary>
    public bool IsInterruptible { get; set; }

    /// <summary>
    /// Custom alert message displayed on the HUD when this action is cast.
    /// </summary>
    public string AlertMessage { get; set; } = string.Empty;
}
