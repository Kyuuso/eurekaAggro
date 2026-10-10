using System;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using EurekaAggro.Configuration;
using EurekaAggro.Data;
using EurekaAggro.Rendering;
using EurekaAggro.Services;
using EurekaAggro.UI;

namespace EurekaAggro;

/// <summary>
/// Clase principal del plugin EurekaAggro.
/// Integra de forma 100% nativa:
/// - Detección táctica de aggro y radar de monstruos en Eureka (Dragones dormidos por sonido, Ashkin por sangre, Sprites por magia, conos de visión).
/// - Alertas de casteo en combate para interrupciones y aturdimientos.
/// - Takeover completo de BFE (Bunnies for Eureka) para automatización inteligente de conejos de Pyros/Pagos/Hydatos con vnavmesh y combate integrado.
/// </summary>
public sealed class EurekaAggroPlugin : IDalamudPlugin
{
    public string Name => "Eureka Aggro";

    // Servicios inyectados de Dalamud
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IToastGui ToastGui { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IPluginLog PluginLog { get; private set; } = null!;

    // Componentes del núcleo de detección de Eureka
    private readonly PluginConfiguration configuration;
    private readonly MobDatabase mobDatabase;
    private readonly ActionDatabase actionDatabase;
    private readonly EurekaEnvironmentService environmentService;
    private readonly CastMonitor castMonitor;
    private readonly DragonWalkService dragonWalkService;
    private readonly OverlayRenderer overlayRenderer;
    private readonly MainWindow mainWindow;
    private readonly CastAlertWindow castAlertWindow;

    // Subsistema nativo de automatización de conejos (BFE)
    private readonly BunnyAutomationService bunnyAutomationService;

    // Comandos de consola
    private const string MainCommand = "/eurekaaggro";
    private const string ShortCommand = "/ea";
    private const string BfeCommand = "/bfe";
    private const string BunniesCommand = "/bunnies";

    public EurekaAggroPlugin(IDalamudPluginInterface pluginInterface)
    {
        configuration = pluginInterface.GetPluginConfig() as PluginConfiguration ?? new PluginConfiguration();
        configuration.Initialize(pluginInterface);

        // Inicializamos ECommons directamente para el plugin principal
        ECommonsMain.Init(pluginInterface, this, ECommons.Module.DalamudReflector, ECommons.Module.ObjectFunctions);

        var configDir = pluginInterface.GetPluginConfigDirectory();

        mobDatabase = new MobDatabase(PluginLog, configDir);
        actionDatabase = new ActionDatabase(PluginLog, configDir);
        environmentService = new EurekaEnvironmentService(DataManager, ClientState);

        castMonitor = new CastMonitor(DataManager, ClientState, TargetManager, ObjectTable, ChatGui, actionDatabase, configuration);
        dragonWalkService = new DragonWalkService(PluginLog, ClientState, ObjectTable, ChatGui, mobDatabase, configuration);
        overlayRenderer = new OverlayRenderer(GameGui, ClientState, ObjectTable, mobDatabase, environmentService, dragonWalkService, configuration);

        // Inicializamos el servicio nativo de conejos de Eureka
        bunnyAutomationService = new BunnyAutomationService(pluginInterface, ChatGui, ToastGui, PlayerState, TextureProvider);

        castAlertWindow = new CastAlertWindow(castMonitor, configuration);
        mainWindow = new MainWindow(configuration, mobDatabase, actionDatabase, environmentService, ClientState, dragonWalkService, castAlertWindow, TextureProvider, pluginInterface, bunnyAutomationService);

        // Registro de comandos del radar de EurekaAggro
        CommandManager.AddHandler(MainCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Abre la ventana principal del radar y configuración de Eureka Aggro."
        });

        CommandManager.AddHandler(ShortCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Comando abreviado para abrir el radar de Eureka Aggro."
        });

        // Registro de comandos de automatización de conejos
        CommandManager.AddHandler(BfeCommand, new CommandInfo(OnBfeCommand)
        {
            HelpMessage = "Comandos de automatización de conejos (/bfe, /bfe pyros, /bfe stop, /bfe settings)."
        });

        CommandManager.AddHandler(BunniesCommand, new CommandInfo(OnBfeCommand)
        {
            HelpMessage = "Alias heredado para el control de conejos de Eureka."
        });

        PluginInterface.UiBuilder.Draw += OnDrawUi;
        PluginInterface.UiBuilder.OpenConfigUi += OnOpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += OnOpenConfigUi;
        Framework.Update += OnFrameworkUpdate;

        PluginLog.Info("EurekaAggro y subsistema de conejos inicializados correctamente.");
    }

    private void OnCommand(string command, string args)
    {
        mainWindow.IsOpen = !mainWindow.IsOpen;
    }

    private void OnBfeCommand(string command, string args)
    {
        bunnyAutomationService.ProcessCommand(command, args);
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (configuration.Enabled)
        {
            castMonitor.Update();
            dragonWalkService.Update();
        }

        // El motor de conejos procesa sus ticks y chequeo de dependencias
        bunnyAutomationService.Update();
    }

    private void OnDrawUi()
    {
        // 1. Overlay 3D en pantalla
        overlayRenderer.Draw();

        // 2. Ventana de alerta de casteos tácticos
        castAlertWindow.Draw();

        // 3. Ventana principal de radar y configuración
        mainWindow.Draw();

        // 4. Ventanas de conejos si están abiertas
        bunnyAutomationService.DrawUi();
    }

    private void OnOpenConfigUi()
    {
        mainWindow.IsOpen = true;
    }

    public void Dispose()
    {
        CommandManager.RemoveHandler(MainCommand);
        CommandManager.RemoveHandler(ShortCommand);
        CommandManager.RemoveHandler(BfeCommand);
        CommandManager.RemoveHandler(BunniesCommand);

        PluginInterface.UiBuilder.Draw -= OnDrawUi;
        PluginInterface.UiBuilder.OpenConfigUi -= OnOpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= OnOpenConfigUi;
        Framework.Update -= OnFrameworkUpdate;

        // Liberación de recursos del subsistema de conejos
        bunnyAutomationService?.Dispose();

        // Liberación de recursos de ECommons
        ECommonsMain.Dispose();

        configuration.Save();
        mobDatabase.SaveIfDirty();
        actionDatabase.SaveIfDirty();

        PluginLog.Info("EurekaAggro descargado correctamente.");
    }
}
