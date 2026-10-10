using System;
using EurekaSuite.Services;

namespace BFE;

/// <summary>
/// Static compatibility bridge for the Eureka bunny automation subsystem.
/// Exposes the active BunnyAutomationService instance to schedulers and internal tasks.
/// </summary>
public static class Plugin
{
    public static BunnyAutomationService P => BunnyAutomationService.P;
    public static Config C => BunnyAutomationService.C;
}
