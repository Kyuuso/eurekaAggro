using System;
using EurekaAggro.Services;

namespace BFE;

/// <summary>
/// Puente estático de compatibilidad para el subsistema de conejos de Eureka.
/// Expone la instancia activa del servicio nativo BunnyAutomationService hacia los schedulers y tareas internas.
/// </summary>
public static class Plugin
{
    public static BunnyAutomationService P => BunnyAutomationService.P;
    public static Config C => BunnyAutomationService.C;
}
