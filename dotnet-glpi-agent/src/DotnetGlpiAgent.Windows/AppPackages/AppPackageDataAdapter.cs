using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Windows.Registry;
using Microsoft.Win32;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace DotnetGlpiAgent.Windows.AppPackages;

public sealed record AppPackageDataSnapshot(
    string Id,
    string Name,
    string? Version,
    string? Publisher,
    string? Architecture,
    string UserSid,
    bool IsFramework);

public sealed record AppPackageEnumerationResult(
    IReadOnlyList<AppPackageDataSnapshot> Packages,
    IReadOnlyList<SourceDiagnostic> Diagnostics);

public interface IAppPackageDataAdapter
{
    ValueTask<AppPackageEnumerationResult> EnumerateAsync(CancellationToken cancellationToken);
}

public sealed partial class AppPackageDataAdapter : IAppPackageDataAdapter
{
    private const string ProfileListPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";
    private readonly IRegistryQueryAdapter _registry;

    public AppPackageDataAdapter(IRegistryQueryAdapter registry)
    {
        _registry = registry;
    }

    public async ValueTask<AppPackageEnumerationResult> EnumerateAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<RegistryKeySnapshot> profiles = await _registry.EnumerateSubKeysAsync(
            RegistryHive.LocalMachine,
            RegistryView.Registry64,
            ProfileListPath,
            [],
            cancellationToken).ConfigureAwait(false);
        string[] userSids = profiles
            .Select(static profile => LastSegment(profile.Path))
            .Where(static sid => UserSidRegex().IsMatch(sid))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return await Task.Run(
            () => EnumeratePackages(userSids, cancellationToken),
            CancellationToken.None).ConfigureAwait(false);
    }

    private static AppPackageEnumerationResult EnumeratePackages(
        IReadOnlyList<string> userSids,
        CancellationToken cancellationToken)
    {
        var packages = new List<AppPackageDataSnapshot>();
        var diagnostics = new List<SourceDiagnostic>();
        var manager = new PackageManager();
        foreach (string sid in userSids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (Package package in manager.FindPackagesForUser(sid))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    PackageId id = package.Id;
                    packages.Add(new AppPackageDataSnapshot(
                        id.FullName,
                        id.Name,
                        FormatVersion(id.Version),
                        id.Publisher,
                        id.Architecture.ToString(),
                        sid,
                        package.IsFramework));
                }
            }
            catch (UnauthorizedAccessException)
            {
                diagnostics.Add(new SourceDiagnostic(
                    $"appx:{sid}",
                    CollectionState.AccessDenied,
                    "appx-access-denied",
                    "App package enumeration was denied for this profile."));
            }
            catch (COMException)
            {
                diagnostics.Add(new SourceDiagnostic(
                    $"appx:{sid}",
                    CollectionState.Partial,
                    "appx-enumeration-failed",
                    "Windows could not enumerate app packages for this profile."));
            }
        }

        return new AppPackageEnumerationResult(packages, diagnostics);
    }

    private static string FormatVersion(PackageVersion version)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}");
    }

    private static string LastSegment(string path)
    {
        return path.TrimEnd('\\', '/').Split('\\', '/').LastOrDefault() ?? string.Empty;
    }

    [GeneratedRegex("^S-1-(?:5-21|12-1)-[0-9-]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex UserSidRegex();
}
