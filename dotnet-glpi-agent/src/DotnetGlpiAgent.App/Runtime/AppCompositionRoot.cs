using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Configuration;
using DotnetGlpiAgent.Core.Diagnostics;
using DotnetGlpiAgent.Core.Identity;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Protocol.Transport;
using DotnetGlpiAgent.Windows.AppPackages;
using DotnetGlpiAgent.Windows.Bcl;
using DotnetGlpiAgent.Windows.Collectors;
using DotnetGlpiAgent.Windows.Management;
using DotnetGlpiAgent.Windows.Monitors;
using DotnetGlpiAgent.Windows.Registry;
using DotnetGlpiAgent.Windows.Security;

namespace DotnetGlpiAgent.App.Runtime;

public sealed class AppRuntimeFactory : IAppRuntimeFactory
{
    public IAppRuntime Create(AgentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return AppCompositionRoot.CreateRuntime(options);
    }
}

public static class AppCompositionRoot
{
    public static IAppRuntime CreateRuntime(AgentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var wmi = new WmiQueryAdapter();
        var registry = new RegistryQueryAdapter();
        var users = new WindowsUserIdentityResolver();
        string profilesRoot = ResolveProfilesRoot();
        var offlineScanner = new OfflineSoftwareScanner(
            registry,
            new RegistryHiveLoader(),
            users,
            profilesRoot);
        var redactor = new SecretRedactor(
        [
            options.Password,
            options.ProxyPassword,
            options.ClientCertificatePassword,
        ]);
        IInventoryCollector[] collectors =
        [
            new OperatingSystemCollector(wmi, registry, new HostDataAdapter()),
            new HardwareCollector(wmi),
            new BiosCollector(wmi),
            new CpuCollector(wmi),
            new MemoryCollector(wmi),
            new StorageCollector(wmi),
            new VolumeCollector(new FileSystemDataAdapter()),
            new NetworkCollector(new NetworkDataAdapter(), wmi),
            new DeviceCollector(wmi),
            new UserSessionCollector(wmi),
            new ProcessCollector(new WindowsProcessDataAdapter(), redactor),
            new SoftwareCollector(registry, users, offlineScanner),
            new AppPackageCollector(new AppPackageDataAdapter(registry)),
            new HotfixCollector(wmi),
            new PrinterCollector(wmi),
            new MonitorCollector(wmi, new EdidRegistryAdapter()),
            new PeripheralCollector(wmi),
            new BatteryCollector(wmi),
            new AntivirusCollector(wmi),
            new FirewallCollector(wmi),
        ];

        return new AppRuntime(
            collectors,
            new InventoryCollectorOrchestrator(options.MaxConcurrency),
            new IdentityStore(),
            new WindowsFilePermissionHardener());
    }

    private static string ResolveProfilesRoot()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.GetDirectoryName(profile)
            ?? Path.Combine(Environment.GetEnvironmentVariable("SystemDrive") ?? "C:", "Users");
    }
}

public sealed class AppRuntime : IAppRuntime
{
    private readonly IReadOnlyList<IInventoryCollector> _collectors;
    private readonly InventoryCollectorOrchestrator _orchestrator;
    private readonly IdentityStore _identityStore;
    private readonly IFilePermissionHardener _permissions;

    public AppRuntime(
        IEnumerable<IInventoryCollector> collectors,
        InventoryCollectorOrchestrator orchestrator,
        IdentityStore identityStore,
        IFilePermissionHardener permissions)
    {
        _collectors = collectors?.ToArray() ?? throw new ArgumentNullException(nameof(collectors));
        _orchestrator = orchestrator;
        _identityStore = identityStore;
        _permissions = permissions;
    }

    public async ValueTask<InventorySnapshot> CollectAsync(
        AgentOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        AgentPaths paths = AgentPaths.ForCurrentWindowsHost();
        string stateDirectory = Path.GetFullPath(options.StateDirectory ?? paths.StateDirectory);
        Directory.CreateDirectory(stateDirectory);
        _permissions.HardenDirectory(stateDirectory);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IdentityLoadResult identity = await _identityStore.LoadOrCreateAsync(
            stateDirectory,
            Environment.MachineName,
            now,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        string identityFile = Path.Combine(stateDirectory, IdentityStore.StateFileName);
        if (File.Exists(identityFile))
        {
            _permissions.HardenFile(identityFile);
        }

        string correlationId = Guid.NewGuid().ToString("N");
        IInventoryCollector[] enabled = _collectors
            .Where(collector => options.IsCategoryEnabled(collector.Category.ToString()))
            .ToArray();
        CollectionRunResult run = await _orchestrator.CollectAsync(
            enabled,
            options,
            correlationId,
            cancellationToken).ConfigureAwait(false);
        CategoryCollectionStatus[] statuses = BuildStatuses(run);
        bool complete = run.IsComplete && statuses.All(static status => status.State is
            CollectionState.Success or CollectionState.Unavailable or CollectionState.Disabled);
        var metadata = new CollectionMetadata(
            run.StartedAtUtc,
            run.CompletedAtUtc,
            complete,
            statuses,
            correlationId);
        var agentIdentity = new AgentIdentity(
            identity.Identity.AgentId,
            identity.Identity.DeviceId,
            Environment.MachineName,
            AgentVersion.UserAgent);
        return InventorySnapshotAssembler.Assemble(
            agentIdentity,
            new InventoryAccount(options.Tag),
            metadata,
            run.Contributions);
    }

    public async ValueTask<GlpiSubmissionResult> SubmitAsync(
        InventorySnapshot snapshot,
        AgentOptions options,
        CancellationToken cancellationToken)
    {
        using GlpiClient client = GlpiHttpClientFactory.Create(options);
        return await client.SubmitAsync(snapshot, cancellationToken).ConfigureAwait(false);
    }

    private static CategoryCollectionStatus[] BuildStatuses(CollectionRunResult run)
    {
        IEnumerable<CategoryCollectionStatus> collectorStatuses = run.Results.Select(static result => new CategoryCollectionStatus(
            result.Collector,
            result.Category.ToString(),
            result.State,
            result.Duration,
            result.DiagnosticCode,
            result.DiagnosticMessage));
        IEnumerable<CategoryCollectionStatus> sourceStatuses = run.Contributions
            .SelectMany(static contribution => contribution.Diagnostics)
            .Select(static diagnostic => new CategoryCollectionStatus(
                diagnostic.Source,
                "Source",
                diagnostic.State,
                TimeSpan.Zero,
                diagnostic.Code,
                diagnostic.Message));
        return collectorStatuses.Concat(sourceStatuses)
            .OrderBy(static status => status.Collector, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static status => status.Category, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public static class AgentVersion
{
    private static readonly Version AssemblyVersion = typeof(AgentVersion).Assembly.GetName().Version ?? new Version(0, 1, 0);

    public static string Value => $"{AssemblyVersion.Major}.{AssemblyVersion.Minor}.{AssemblyVersion.Build}";

    public static string UserAgent => $"Dotnet-GLPI-Agent/{Value}";
}
