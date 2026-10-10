using System;
using System.Diagnostics;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.Automation.NeoTaskManager;
using ECommons.Configuration;
using ECommons.DalamudServices;
using AutoRetainerAPI;
using BFE;
using BFE.IPC;
using BFE.IPC.Lifestream;
using BFE.Scheduler;
using BFE.Ui;
using BFE.Ui.DebugWindow;
using BFE.Ui.MainWindow;
using BFE.Ui.SettingWindow;

namespace EurekaSuite.Services;

/// <summary>
/// Central automation coordinator for Eureka bunny FATEs and treasure hunting.
/// Integrates bunny acquisition, treasure hunting, navigation via vnavmesh,
/// combat via WrathCombo, avoidance via BossMod, teleports, and AutoRetainer.
/// </summary>
public sealed class BunnyAutomationService : IDisposable
{
    public static BunnyAutomationService P { get; private set; } = null!;
    public static Config C => P.config;

    private readonly IDalamudPluginInterface pluginInterface;
    public IChatGui ChatGui { get; }
    public IToastGui ToastGui { get; }
    public IPlayerState PlayerState { get; }
    public ITextureProvider TextureProvider { get; }

    internal Config config = null!;
    internal BfeAppearance appearance = null!;

    // Bunny user interface windows
    internal WindowSystem windowSystem = null!;
    internal MainWindow mainWindow = null!;
    internal SettingsWindow settingsWindow = null!;
    internal DebugWindow debugWindow = null!;

    // Item filtering and inventory handling
    public Filter filter { get; private set; } = null!;

    // IPC integrations and automation services
    internal AutoRetainerApi autoRetainerApi = null!;
    internal LifestreamIPC lifestream = null!;
    internal TaskManager taskManager = null!;
    internal AutoRetainerIPC autoRetainer = null!;
    internal PandoraIPC pandora = null!;
    internal NavmeshIPC navmesh = null!;
    internal BossModIPC bossmod = null!;
    internal WrathIPC wrath = null!;
    internal BunniesIPC bunniesIPC = null!;
    internal PluginDependencyService pluginDependencies = null!;

    // Session runtime tracking
    internal Stopwatch stopwatch = null!;
    internal TimeSpan totalRunTime;

    public BunnyAutomationService(
        IDalamudPluginInterface pluginInterface,
        IChatGui chatGui,
        IToastGui toastGui,
        IPlayerState playerState,
        ITextureProvider textureProvider)
    {
        P = this;
        this.pluginInterface = pluginInterface;
        ChatGui = chatGui;
        ToastGui = toastGui;
        PlayerState = playerState;
        TextureProvider = textureProvider;

        filter = new Filter();

        // Configuration and visual styling initialization
        EzConfig.Migrate<Config>();
        config = EzConfig.Init<Config>();
        appearance = new BfeAppearance(textureProvider);

        // IPC subsystems and dependency initialization
        pluginDependencies = new PluginDependencyService();
        pluginDependencies.Refresh(true);
        taskManager = new TaskManager();
        autoRetainer = new AutoRetainerIPC();
        autoRetainerApi = new AutoRetainerApi();
        lifestream = new LifestreamIPC();
        navmesh = new NavmeshIPC();
        pandora = new PandoraIPC();
        bossmod = new BossModIPC();
        wrath = new WrathIPC();
        bunniesIPC = new BunniesIPC();

        // Window registration
        windowSystem = new WindowSystem("Eureka Suite - Bunnies");
        mainWindow = new MainWindow();
        debugWindow = new DebugWindow();
        settingsWindow = new SettingsWindow();

        // Activity stopwatch
        stopwatch = new Stopwatch();

        ResetSessionStats();
    }

    /// <summary>
    /// Update loop executed every Dalamud framework tick.
    /// Refreshes dependency status and executes bunny scheduler ticks when enabled.
    /// </summary>
    public void Update()
    {
        pluginDependencies.RefreshIfDue();

        if (SchedulerMain.DoWeTick && Svc.Objects.LocalPlayer != null)
        {
            SchedulerMain.Tick();
        }
    }

    /// <summary>
    /// Draws active bunny windows in the UI builder pipeline.
    /// </summary>
    public void DrawUi()
    {
        appearance.Draw(windowSystem);
    }

    /// <summary>
    /// Processes command-line instructions for bunny automation (/bfe or /bunnies).
    /// </summary>
    public void ProcessCommand(string command, string args)
    {
        var cleanArgs = args.Trim();

        if (cleanArgs.EqualsIgnoreCaseAny("ws"))
        {
            ResetWindowPositions();
        }
        else if (cleanArgs.EqualsIgnoreCaseAny("j"))
        {
            JumpWindowsToRandomVisibleLocations();
        }
        else if (cleanArgs.EqualsIgnoreCaseAny("d", "debug"))
        {
            debugWindow.IsOpen = !debugWindow.IsOpen;
        }
        else if (cleanArgs.EqualsIgnoreCaseAny("config", "s", "settings", "setting"))
        {
            settingsWindow.IsOpen = !settingsWindow.IsOpen;
        }
        else if (cleanArgs.EqualsIgnoreCaseAny("stop"))
        {
            SchedulerMain.DisablePlugin();
            ChatGui.Print("[Eureka Suite] Bunny automation stopped.");
        }
        else if (cleanArgs.EqualsIgnoreCaseAny("pyros"))
        {
            pluginDependencies.Refresh(true);
            if (pluginDependencies.RequiredDependenciesLoaded)
            {
                C.zoneSelected = 1;
                SchedulerMain.EnablePlugin();
                ChatGui.Print("[Eureka Suite] Starting Pyros bunny automation cycle.");
            }
            else
            {
                Helpers.NotifyPlugins();
                SchedulerMain.DisablePlugin();
            }
        }
    }

    public void ToggleMainWindow()
    {
        mainWindow.IsOpen = !mainWindow.IsOpen;
    }

    public void ToggleSettingsWindow()
    {
        settingsWindow.IsOpen = !settingsWindow.IsOpen;
    }

    public void ToggleDebugWindow()
    {
        debugWindow.IsOpen = !debugWindow.IsOpen;
    }

    private void ResetSessionStats()
    {
        C.sessionStats.Reset();
        C.pagosSessionStats.Reset();
        C.pyrosSessionStats.Reset();
        C.hydatosSessionStats.Reset();
    }

    internal void ResetWindowPositions()
    {
        mainWindow.QueueResetToOrigin();
        settingsWindow.QueueResetToOrigin();
        debugWindow.QueueResetToOrigin();
        mainWindow.IsOpen = true;
        settingsWindow.IsOpen = true;
    }

    internal void JumpWindowsToRandomVisibleLocations()
    {
        mainWindow.QueueRandomVisibleJump();
        settingsWindow.QueueRandomVisibleJump();
        debugWindow.QueueRandomVisibleJump();
        mainWindow.IsOpen = true;
        settingsWindow.IsOpen = true;
    }

    public void Dispose()
    {
        windowSystem.RemoveAllWindows();
        appearance?.Dispose();
        filter?.Dispose();
        autoRetainerApi?.Dispose();
    }
}
