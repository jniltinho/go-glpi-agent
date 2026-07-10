using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace DotnetGlpiAgent.Windows.Bcl;

public sealed record HostDataSnapshot(
    string MachineName,
    string? DomainName,
    string OperatingSystemDescription,
    string OperatingSystemArchitecture,
    string ProcessArchitecture,
    DateTimeOffset BootTimeUtc,
    string TimeZoneId);

public interface IHostDataAdapter
{
    ValueTask<HostDataSnapshot> GetAsync(CancellationToken cancellationToken);
}

public sealed class HostDataAdapter(TimeProvider? timeProvider = null) : IHostDataAdapter
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public ValueTask<HostDataSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? domain = IPGlobalProperties.GetIPGlobalProperties().DomainName;
        DateTimeOffset bootTime = _timeProvider.GetUtcNow().Subtract(TimeSpan.FromMilliseconds(Environment.TickCount64));
        return ValueTask.FromResult(new HostDataSnapshot(
            Environment.MachineName,
            string.IsNullOrWhiteSpace(domain) ? null : domain,
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            RuntimeInformation.ProcessArchitecture.ToString(),
            bootTime,
            TimeZoneInfo.Local.Id));
    }
}
