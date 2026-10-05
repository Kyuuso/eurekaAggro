using System;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using EurekaAggro.Configuration;
using EurekaAggro.Data;
using EurekaAggro.Rendering;
using EurekaAggro.Services;
using EurekaAggro.UI;

namespace EurekaAggro;

/// <summary>
/// Main plugin class for EurekaAggro.
/// Specialized 100% for Eureka expeditions (Anemos, Pagos, Pyros, Hydatos):
/// - Sleeping Dragons sound detection (running warning and safe walking radius).
/// - Undead (Ashkin) blood detection when player HP is below 80%.
/// - Sprites / Elementals magic aggro detection.
/// - Classic frontal sight cones and radial proximity circles with precision guide lines.
/// - In-game tactical cast alerts for interrupt/stun/LOS counters.
/// </summary>
public sealed class EurekaAggroPlugin : IDalamudPlugin
{
    public string Name => "Eureka Aggro";

    // Injected Dalamud services
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
    [PluginService] internal static IPluginLog PluginLog { get; private set; } = null!;

    // Internal components
    private readonly PluginConfiguration configuration;
    private readonly MobDatabase mobDatabase;
    private readonly ActionDatabase actionDatabase;
    private readonly EurekaEnvironmentService environmentService;
    private readonly CastMonitor castMonitor;
    private readonly DragonWalkService dragonWalkService;
    private readonly OverlayRenderer overlayRenderer;
    private readonly MainWindow mainWindow;
    private readonly CastAlertWindow castAlertWindow;

    private const string MainCommand = "/eurekaaggro";
    private const string ShortCommand = "/ea";

    public EurekaAggroPlugin(IDalamudPluginInterface pluginInterface)
    {
        configuration = pluginInterface.GetPluginConfig() as PluginConfiguration ?? new PluginConfiguration();
        configuration.Initialize(pluginInterface);

        var configDir = pluginInterface.GetPluginConfigDirectory();

        mobDatabase = new MobDatabase(PluginLog, configDir);
        actionDatabase = new ActionDatabase(PluginLog, configDir);
        environmentService = new EurekaEnvironmentService(DataManager, ClientState);

        castMonitor = new CastMonitor(ClientState, TargetManager, ObjectTable, ChatGui, actionDatabase, configuration);
        dragonWalkService = new DragonWalkService(PluginLog, ClientState, ObjectTable, ChatGui, mobDatabase, configuration);
        overlayRenderer = new OverlayRenderer(GameGui, ClientState, ObjectTable, mobDatabase, environmentService, dragonWalkService, configuration);

        mainWindow = new MainWindow(configuration, mobDatabase, actionDatabase, environmentService, ClientState, dragonWalkService, TextureProvider, pluginInterface);
        castAlertWindow = new CastAlertWindow(castMonitor, configuration);

        CommandManager.AddHandler(MainCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the Eureka Aggro configuration and radar window."
        });

        CommandManager.AddHandler(ShortCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Short command to open the Eureka Aggro radar."
        });

        PluginInterface.UiBuilder.Draw += OnDrawUi;
        PluginInterface.UiBuilder.OpenConfigUi += OnOpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += OnOpenConfigUi;
        Framework.Update += OnFrameworkUpdate;

        PluginLog.Info("EurekaAggro initialized successfully.");
    }

    private void OnCommand(string command, string args)
    {
        mainWindow.IsOpen = !mainWindow.IsOpen;
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (!configuration.Enabled) return;

        castMonitor.Update();
        dragonWalkService.Update();
    }

    private void OnDrawUi()
    {
        // 1. Draw 3D in-game overlay
        overlayRenderer.Draw();

        // 2. Draw HUD cast alert window if active
        castAlertWindow.Draw();

        // 3. Draw main configuration window if open
        mainWindow.Draw();
    }

    private void OnOpenConfigUi()
    {
        mainWindow.IsOpen = true;
    }

    public void Dispose()
    {
        CommandManager.RemoveHandler(MainCommand);
        CommandManager.RemoveHandler(ShortCommand);

        PluginInterface.UiBuilder.Draw -= OnDrawUi;
        PluginInterface.UiBuilder.OpenConfigUi -= OnOpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= OnOpenConfigUi;
        Framework.Update -= OnFrameworkUpdate;

        configuration.Save();
        mobDatabase.SaveIfDirty();
        actionDatabase.SaveIfDirty();

        PluginLog.Info("EurekaAggro unloaded.");
    }
}
