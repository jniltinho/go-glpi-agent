using System.Security.Cryptography;
using System.Text;
using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Configuration;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Windows.Registry;
using Microsoft.Win32;

namespace DotnetGlpiAgent.Windows.Collectors;

public sealed record OfflineSoftwareScanResult(
    IReadOnlyList<SoftwareInfo> Software,
    IReadOnlyList<SourceDiagnostic> Diagnostics);

public interface IOfflineSoftwareScanner
{
    ValueTask<OfflineSoftwareScanResult> ScanAsync(CancellationToken cancellationToken);
}

public sealed class OfflineSoftwareScanner : IOfflineSoftwareScanner
{
    private const string ProfileListPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";
    private const string NativeUninstallPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string Wow64UninstallPath = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
    private static readonly string[] ProfileValueNames = ["ProfileImagePath"];
    private static readonly string[] SoftwareValueNames =
    [
        "DisplayName",
        "DisplayVersion",
        "EstimatedSize",
        "InstallDate",
        "Publisher",
        "ReleaseType",
        "SystemComponent",
        "UninstallString",
        "URLInfoAbout",
        "WindowsInstaller",
    ];

    private readonly IRegistryQueryAdapter _registry;
    private readonly IRegistryHiveLoader _loader;
    private readonly IWindowsUserIdentityResolver _users;
    private readonly string _profilesRoot;

    public OfflineSoftwareScanner(
        IRegistryQueryAdapter registry,
        IRegistryHiveLoader loader,
        IWindowsUserIdentityResolver users,
        string profilesRoot)
    {
        _registry = registry;
        _loader = loader;
        _users = users;
        _profilesRoot = Path.GetFullPath(profilesRoot);
    }

    public async ValueTask<OfflineSoftwareScanResult> ScanAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<RegistryKeySnapshot> profiles = await _registry.EnumerateSubKeysAsync(
            RegistryHive.LocalMachine,
            RegistryView.Registry64,
            ProfileListPath,
            ProfileValueNames,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<RegistryKeySnapshot> loaded = await _registry.EnumerateSubKeysAsync(
            RegistryHive.Users,
            RegistryView.Default,
            string.Empty,
            [],
            cancellationToken).ConfigureAwait(false);
        var loadedSids = loaded.Select(static hive => LastSegment(hive.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var software = new List<SoftwareInfo>();
        var diagnostics = new List<SourceDiagnostic>();

        foreach (RegistryKeySnapshot profile in profiles.Take(256))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string sid = LastSegment(profile.Path);
            if (loadedSids.Contains(sid))
            {
                continue;
            }

            try
            {
                string profilePath = Environment.ExpandEnvironmentVariables(
                    profile.GetString("ProfileImagePath")
                        ?? throw new InvalidDataException("ProfileImagePath is missing."));
                string trustedProfile = AgentPathPolicy.EnsureWithinRoot(profilePath, _profilesRoot, "offline profile");
                string hiveFile = AgentPathPolicy.EnsureWithinRoot(
                    Path.Combine(trustedProfile, "NTUSER.DAT"),
                    trustedProfile,
                    "offline registry hive");
                string mountName = CreateMountName(sid);
                using IDisposable lease = await _loader.LoadAsync(
                    mountName,
                    hiveFile,
                    cancellationToken).ConfigureAwait(false);
                string? account = _users.ResolveAccountName(sid);
                software.AddRange(SoftwareCollector.MapRows(
                    await _registry.EnumerateSubKeysAsync(
                        RegistryHive.Users,
                        RegistryView.Default,
                        $"{mountName}\\{NativeUninstallPath}",
                        SoftwareValueNames,
                        cancellationToken).ConfigureAwait(false),
                    "x86_64",
                    "offline-user-registry",
                    sid,
                    account));
                software.AddRange(SoftwareCollector.MapRows(
                    await _registry.EnumerateSubKeysAsync(
                        RegistryHive.Users,
                        RegistryView.Default,
                        $"{mountName}\\{Wow64UninstallPath}",
                        SoftwareValueNames,
                        cancellationToken).ConfigureAwait(false),
                    "x86",
                    "offline-user-registry",
                    sid,
                    account));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is CollectorFailureException or IOException or UnauthorizedAccessException or AgentConfigurationException)
            {
                CollectionState state = exception is CollectorFailureException failure
                    ? failure.State
                    : exception is UnauthorizedAccessException ? CollectionState.AccessDenied : CollectionState.Failed;
                diagnostics.Add(new SourceDiagnostic(
                    $"offline-profile:{sid}",
                    state,
                    "offline-profile-scan-failed",
                    exception.GetType().Name));
            }
        }

        return new OfflineSoftwareScanResult(SoftwareCollector.Deduplicate(software), diagnostics);
    }

    private static string CreateMountName(string sid)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sid));
        return $"DotnetGlpiAgent_{Convert.ToHexString(hash)[..16]}";
    }

    private static string LastSegment(string path)
    {
        return path.TrimEnd('\\', '/').Split('\\', '/').LastOrDefault() ?? string.Empty;
    }
}
