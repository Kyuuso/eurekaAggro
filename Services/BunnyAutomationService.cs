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

namespace EurekaAggro.Services;

/// <summary>
/// Servicio central de automatización de conejos de Eureka (BFE Takeover).
/// Integra de forma nativa e integral en EurekaAggro toda la lógica de obtención de conejos,
/// cofres de tesoro, navegación con vnavmesh, rotación con WrathCombo, evasión con BossMod,
/// teletransportes e interacción con AutoRetainer.
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

    // Ventanas de la interfaz de conejos
    internal WindowSystem windowSystem = null!;
    internal MainWindow mainWindow = null!;
    internal SettingsWindow settingsWindow = null!;
    internal DebugWindow debugWindow = null!;

    // Filtro de objetos e inventario
    public Filter filter { get; private set; } = null!;

    // IPCs y servicios de automatización
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

    // Temporizadores de sesión
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

        // Inicialización de la configuración y apariencia
        EzConfig.Migrate<Config>();
        config = EzConfig.Init<Config>();
        appearance = new BfeAppearance(textureProvider);

        // Inicialización de subsistemas de IPC y dependencias
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

        // Inicialización de ventanas
        windowSystem = new WindowSystem("EurekaAggro - Bunnies");
        mainWindow = new MainWindow();
        debugWindow = new DebugWindow();
        settingsWindow = new SettingsWindow();

        // Cronómetro de actividad
        stopwatch = new Stopwatch();

        ResetSessionStats();
    }

    /// <summary>
    /// Ciclo de actualización ejecutado en cada frame de Dalamud.
    /// Actualiza el estado de dependencias y el planificador de conejos si está activo.
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
    /// Dibuja las ventanas activas de conejos en la interfaz.
    /// </summary>
    public void DrawUi()
    {
        appearance.Draw(windowSystem);
    }

    /// <summary>
    /// Procesa comandos de consola para controlar los conejos (/bfe o /bunnies).
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
            ChatGui.Print("[EurekaAggro] Bunny automation stopped.");
        }
        else if (cleanArgs.EqualsIgnoreCaseAny("pyros"))
        {
            pluginDependencies.Refresh(true);
            if (pluginDependencies.RequiredDependenciesLoaded)
            {
                C.zoneSelected = 1;
                SchedulerMain.EnablePlugin();
                ChatGui.Print("[EurekaAggro] Starting Pyros bunny automation cycle.");
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
