using Dalamud.Plugin;
using ECommons.DalamudServices;
using ECommons.Logging;

namespace BFE;

internal enum PluginDependencyState
{
    Missing,
    InstalledNotLoaded,
    Loaded,
}

internal sealed class PluginDependencyDefinition
{
    public PluginDependencyDefinition(string internalName, string displayName, string repoUrl, params string[] displayNameFragments)
    {
        InternalName = internalName;
        DisplayName = displayName;
        RepoUrl = repoUrl;
        DisplayNameFragments = displayNameFragments.Length > 0 ? displayNameFragments : [displayName];
    }

    public string InternalName { get; }
    public string DisplayName { get; }
    public string RepoUrl { get; }
    public string[] DisplayNameFragments { get; }
}

internal sealed class PluginDependencyStatus
{
    public PluginDependencyStatus(PluginDependencyDefinition definition, PluginDependencyState state, string matchedInternalName = "", string matchedName = "")
    {
        Definition = definition;
        State = state;
        MatchedInternalName = matchedInternalName;
        MatchedName = matchedName;
    }

    public PluginDependencyDefinition Definition { get; }
    public PluginDependencyState State { get; }
    public string MatchedInternalName { get; }
    public string MatchedName { get; }
    public string InternalName => Definition.InternalName;
    public string DisplayName => Definition.DisplayName;
    public string RepoUrl => Definition.RepoUrl;
    public bool IsInstalled => State != PluginDependencyState.Missing;
    public bool IsLoaded => State == PluginDependencyState.Loaded;

    public string StateText => State switch
    {
        PluginDependencyState.Loaded => "Loaded",
        PluginDependencyState.InstalledNotLoaded => "Installed, not loaded",
        _ => "Missing",
    };
}

internal sealed class PluginDependencyService
{
    public const string VnavmeshInternalName = "vnavmesh";
    public const string RotationSolverInternalName = "RotationSolver";
    public const string BossModRebornInternalName = "BossModReborn";
    public const string BossModInternalName = "BossMod";

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    private static readonly PluginDependencyDefinition[] RequiredDefinitions =
    [
        new(VnavmeshInternalName, "vnavmesh", IPC.NavmeshIPC.Repo, "vnavmesh"),
        new(RotationSolverInternalName, "Rotation Solver Reborn", RSR, "Rotation Solver Reborn", "RotationSolver"),
        new(BossModRebornInternalName, "BossMod Reborn", BMR, "BossMod Reborn", "BossModReborn", "Boss Mod Reborn", "BMR"),
    ];

    private readonly Dictionary<string, PluginDependencyStatus> allStatuses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PluginDependencyStatus> requiredStatuses = new(StringComparer.OrdinalIgnoreCase);
    private DateTime lastRefreshUtc = DateTime.MinValue;

    public PluginDependencyService()
    {
        foreach (var definition in RequiredDefinitions)
            requiredStatuses[definition.InternalName] = MissingStatus(definition);
    }

    public IReadOnlyList<PluginDependencyStatus> RequiredStatuses
        => RequiredDefinitions.Select(definition => GetRequiredStatus(definition.InternalName)).ToList();

    public bool RequiredDependenciesLoaded
        => RequiredStatuses.All(status => status.IsLoaded);

    public IReadOnlyList<PluginDependencyStatus> UnloadedRequiredStatuses
        => RequiredStatuses.Where(status => !status.IsLoaded).ToList();

    public bool IsBossModFamilyLoaded
        => IsLoaded(BossModRebornInternalName) || IsLoaded(BossModInternalName);

    public bool IsLoaded(string internalName)
        => GetStatus(internalName).IsLoaded;

    public bool IsInstalled(string internalName)
        => GetStatus(internalName).IsInstalled;

    public PluginDependencyStatus GetRequiredStatus(string internalName)
    {
        RefreshIfDue();
        return requiredStatuses.TryGetValue(internalName, out var status)
            ? status
            : MissingStatus(new PluginDependencyDefinition(internalName, internalName, string.Empty, internalName));
    }

    public PluginDependencyStatus GetStatus(string internalName)
    {
        RefreshIfDue();

        if (allStatuses.TryGetValue(internalName, out var status))
            return status;

        status = allStatuses.Values.FirstOrDefault(candidate => Matches(candidate, internalName));
        return status ?? MissingStatus(new PluginDependencyDefinition(internalName, internalName, string.Empty, internalName));
    }

    public void RefreshIfDue()
    {
        if (DateTime.UtcNow - lastRefreshUtc >= RefreshInterval)
            Refresh(true);
    }

    public void Refresh(bool force)
    {
        if (!force && DateTime.UtcNow - lastRefreshUtc < RefreshInterval)
            return;

        try
        {
            var installedPlugins = Svc.PluginInterface.InstalledPlugins.ToList();
            allStatuses.Clear();

            foreach (var plugin in installedPlugins)
                AddPluginStatus(plugin);

            foreach (var definition in RequiredDefinitions)
                requiredStatuses[definition.InternalName] = ResolveRequiredStatus(definition, installedPlugins);

            lastRefreshUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            PluginLog.Warning($"Failed to refresh plugin dependency status: {ex.Message}");
            lastRefreshUtc = DateTime.UtcNow;
        }
    }

    private static bool Matches(PluginDependencyStatus status, string name)
    {
        return string.Equals(status.InternalName, name, StringComparison.OrdinalIgnoreCase)
               || string.Equals(status.MatchedInternalName, name, StringComparison.OrdinalIgnoreCase)
               || string.Equals(status.MatchedName, name, StringComparison.OrdinalIgnoreCase);
    }

    private void AddPluginStatus(IExposedPlugin plugin)
    {
        var internalName = plugin.InternalName ?? string.Empty;
        var name = plugin.Name ?? internalName;
        var definition = new PluginDependencyDefinition(internalName, name, string.Empty, name);
        var state = plugin.IsLoaded ? PluginDependencyState.Loaded : PluginDependencyState.InstalledNotLoaded;
        var status = new PluginDependencyStatus(definition, state, internalName, name);

        if (!string.IsNullOrEmpty(internalName))
            allStatuses[internalName] = status;

        if (!string.IsNullOrEmpty(name))
            allStatuses[name] = status;
    }

    private static PluginDependencyStatus ResolveRequiredStatus(PluginDependencyDefinition definition, IReadOnlyList<IExposedPlugin> installedPlugins)
    {
        var plugin = installedPlugins.FirstOrDefault(candidate =>
            string.Equals(candidate.InternalName, definition.InternalName, StringComparison.OrdinalIgnoreCase));

        plugin ??= installedPlugins.FirstOrDefault(candidate =>
            definition.DisplayNameFragments.Any(fragment =>
                (candidate.Name ?? string.Empty).Contains(fragment, StringComparison.OrdinalIgnoreCase)));

        if (plugin == null)
            return MissingStatus(definition);

        return new PluginDependencyStatus(
            definition,
            plugin.IsLoaded ? PluginDependencyState.Loaded : PluginDependencyState.InstalledNotLoaded,
            plugin.InternalName ?? string.Empty,
            plugin.Name ?? string.Empty);
    }

    private static PluginDependencyStatus MissingStatus(PluginDependencyDefinition definition)
        => new(definition, PluginDependencyState.Missing);
}
