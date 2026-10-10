using System;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using EurekaSuite.Configuration;
using EurekaSuite.Data;
using EurekaSuite.Rendering;
using EurekaSuite.Services;
using EurekaSuite.Tracker;
using EurekaSuite.UI;

namespace EurekaSuite;

/// <summary>
/// Main plugin class for Eureka Suite.
/// Integrates:
/// - Tactical aggro detection and monster radar in Eureka (Sleeping dragons by sound, Ashkin by blood, Sprites by magic, vision cones).
/// - Combat cast alerts for interrupts and stuns.
/// - Bunny Fate Engine (BFE) for automated bunny fate routing & treasure hunting with vnavmesh and auto-combat.
/// - Live ffxiv-eureka.com Phoenix WebSocket tracker sync with automatic instance discovery.
/// </summary>
public sealed class EurekaSuitePlugin : IDalamudPlugin
{
    public string Name => "Eureka Suite";

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
    [PluginService] internal static IToastGui ToastGui { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IPluginLog PluginLog { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] internal static IFateTable FateTable { get; private set; } = null!;

    // Core Eureka detection components
    private readonly PluginConfiguration configuration;
    private readonly MobDatabase mobDatabase;
    private readonly ActionDatabase actionDatabase;
    private readonly EurekaEnvironmentService environmentService;
    private readonly CastMonitor castMonitor;
    private readonly DragonWalkService dragonWalkService;
    private readonly OverlayRenderer overlayRenderer;
    private readonly MainWindow mainWindow;
    private readonly CastAlertWindow castAlertWindow;

    // Bunny Fate Engine (BFE) native subsystem
    private readonly BunnyAutomationService bunnyAutomationService;

    // Eureka Tracker native subsystem
    private readonly TrackerManager trackerManager;

    // Console commands
    private const string MainCommand = "/eurekasuite";
    private const string SuiteShortCommand = "/es";
    private const string EurekaCommand = "/eureka";
    private const string LegacyMainCommand = "/EurekaSuite";
    private const string LegacyShortCommand = "/ea";
    private const string AggroCommand = "/aggro";
    private const string BfeCommand = "/bfe";
    private const string BunniesCommand = "/bunnies";
    private const string TrackerCommand = "/etracker";

    public EurekaSuitePlugin(IDalamudPluginInterface pluginInterface)
    {
        configuration = pluginInterface.GetPluginConfig() as PluginConfiguration ?? new PluginConfiguration();
        configuration.Initialize(pluginInterface);

        // Initialize ECommons directly for the primary plugin
        ECommonsMain.Init(pluginInterface, this, ECommons.Module.DalamudReflector, ECommons.Module.ObjectFunctions);

        var configDir = pluginInterface.GetPluginConfigDirectory();

        mobDatabase = new MobDatabase(PluginLog, configDir);
        actionDatabase = new ActionDatabase(PluginLog, configDir);
        environmentService = new EurekaEnvironmentService(DataManager, ClientState);

        castMonitor = new CastMonitor(DataManager, ClientState, TargetManager, ObjectTable, ChatGui, actionDatabase, configuration);
        dragonWalkService = new DragonWalkService(PluginLog, ClientState, ObjectTable, ChatGui, mobDatabase, configuration);
        overlayRenderer = new OverlayRenderer(GameGui, ClientState, ObjectTable, mobDatabase, environmentService, dragonWalkService, configuration);

        // Initialize Eureka Bunny automation service
        bunnyAutomationService = new BunnyAutomationService(pluginInterface, ChatGui, ToastGui, PlayerState, TextureProvider);

        // Initialize Eureka Tracker manager
        trackerManager = new TrackerManager(configuration, GameInteropProvider, ClientState, ObjectTable, DataManager, ChatGui, ToastGui, FateTable, Framework);

        castAlertWindow = new CastAlertWindow(castMonitor, configuration);
        mainWindow = new MainWindow(configuration, mobDatabase, actionDatabase, environmentService, ClientState, dragonWalkService, castAlertWindow, TextureProvider, PluginInterface, bunnyAutomationService, ObjectTable, trackerManager, GameGui);

        // Registered commands for Eureka Suite
        CommandManager.AddHandler(MainCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Eureka Suite main window."
        });

        CommandManager.AddHandler(SuiteShortCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Short alias to open Eureka Suite."
        });

        CommandManager.AddHandler(EurekaCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Eureka Suite main overview."
        });

        CommandManager.AddHandler(LegacyMainCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Legacy alias for Eureka Suite."
        });

        CommandManager.AddHandler(LegacyShortCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Legacy short alias (/ea) for Eureka Suite."
        });

        CommandManager.AddHandler(AggroCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Eureka Suite aggro radar tab."
        });

        // Registered commands for Fate Engine (Bunnies)
        CommandManager.AddHandler(BfeCommand, new CommandInfo(OnBfeCommand)
        {
            HelpMessage = "Eureka bunny fate automation commands (/bfe, /bfe pyros, /bfe stop, /bfe settings)."
        });

        CommandManager.AddHandler(BunniesCommand, new CommandInfo(OnBfeCommand)
        {
            HelpMessage = "Legacy alias for Eureka bunny automation."
        });

        // Registered commands for Eureka Tracker
        CommandManager.AddHandler(TrackerCommand, new CommandInfo(OnTrackerCommand)
        {
            HelpMessage = "Open the Eureka Tracker live NM window and instance manager (/etracker, /etracker config)."
        });

        PluginInterface.UiBuilder.Draw += OnDrawUi;
        PluginInterface.UiBuilder.OpenConfigUi += OnOpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += OnOpenMainUi;
        Framework.Update += OnFrameworkUpdate;

        PluginLog.Info("Eureka Suite initialized successfully.");
    }

    private void OnCommand(string command, string args)
    {
        var clean = args.Trim();
        if (clean.EqualsIgnoreCaseAny("config", "s", "settings", "setting"))
        {
            mainWindow.OpenAggroRadar(MainWindow.SubView.Configuration);
        }
        else
        {
            if (mainWindow.IsOpen && string.IsNullOrEmpty(clean))
            {
                mainWindow.IsOpen = false;
            }
            else
            {
                mainWindow.OpenAggroRadar(MainWindow.SubView.Main);
            }
        }
    }

    private void OnBfeCommand(string command, string args)
    {
        var clean = args.Trim();
        if (clean.EqualsIgnoreCaseAny("config", "s", "settings", "setting"))
        {
            mainWindow.OpenFateEngine(MainWindow.SubView.Configuration);
        }
        else if (clean.EqualsIgnoreCaseAny("pyros"))
        {
            bunnyAutomationService.ProcessCommand(command, args);
            mainWindow.OpenFateEngine(MainWindow.SubView.Main);
        }
        else if (clean.EqualsIgnoreCaseAny("stop"))
        {
            bunnyAutomationService.ProcessCommand(command, args);
        }
        else if (clean.EqualsIgnoreCaseAny("ws", "j", "d", "debug"))
        {
            bunnyAutomationService.ProcessCommand(command, args);
        }
        else
        {
            if (mainWindow.IsOpen && mainWindow.ActiveMainTab == 1 && string.IsNullOrEmpty(clean))
            {
                mainWindow.IsOpen = false;
            }
            else
            {
                mainWindow.OpenFateEngine(MainWindow.SubView.Main);
            }
        }
    }

    private void OnTrackerCommand(string command, string args)
    {
        var clean = args.Trim();
        if (clean.EqualsIgnoreCaseAny("config", "s", "settings", "setting"))
        {
            mainWindow.OpenTracker(MainWindow.SubView.Configuration);
        }
        else
        {
            if (mainWindow.IsOpen && mainWindow.ActiveMainTab == 2 && string.IsNullOrEmpty(clean))
            {
                mainWindow.IsOpen = false;
            }
            else
            {
                mainWindow.OpenTracker(MainWindow.SubView.Main);
            }
        }
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (configuration.Enabled)
        {
            castMonitor.Update();
            dragonWalkService.Update();
        }

        // The bunny engine processes its tick loops and dependency status
        bunnyAutomationService.Update();
    }

    private void OnDrawUi()
    {
        // 1. 3D screen overlay
        overlayRenderer.Draw();

        // 2. Tactical cast alert window
        castAlertWindow.Draw();

        // 3. Main radar and configuration window
        mainWindow.Draw();

        // 4. Bunny windows if open
        bunnyAutomationService.DrawUi();
    }

    private void OnOpenConfigUi()
    {
        mainWindow.OpenAggroRadar(MainWindow.SubView.Configuration);
    }

    private void OnOpenMainUi()
    {
        mainWindow.OpenAggroRadar(MainWindow.SubView.Main);
    }

    public void Dispose()
    {
        CommandManager.RemoveHandler(MainCommand);
        CommandManager.RemoveHandler(SuiteShortCommand);
        CommandManager.RemoveHandler(EurekaCommand);
        CommandManager.RemoveHandler(LegacyMainCommand);
        CommandManager.RemoveHandler(LegacyShortCommand);
        CommandManager.RemoveHandler(AggroCommand);
        CommandManager.RemoveHandler(BfeCommand);
        CommandManager.RemoveHandler(BunniesCommand);
        CommandManager.RemoveHandler(TrackerCommand);

        PluginInterface.UiBuilder.Draw -= OnDrawUi;
        PluginInterface.UiBuilder.OpenConfigUi -= OnOpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= OnOpenMainUi;
        Framework.Update -= OnFrameworkUpdate;

        // Dispose main window resources
        mainWindow?.Dispose();

        // Dispose Eureka tracker resources
        trackerManager?.Dispose();

        // Dispose bunny subsystem resources
        bunnyAutomationService?.Dispose();

        // Dispose ECommons resources
        ECommonsMain.Dispose();

        configuration.Save();
        mobDatabase.SaveIfDirty();
        actionDatabase.SaveIfDirty();

        PluginLog.Info("Eureka Suite unloaded successfully.");
    }
}
