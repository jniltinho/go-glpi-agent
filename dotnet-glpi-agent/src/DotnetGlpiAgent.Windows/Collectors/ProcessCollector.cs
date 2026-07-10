using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Diagnostics;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Windows.Bcl;

namespace DotnetGlpiAgent.Windows.Collectors;

public sealed class ProcessCollector : WindowsCollectorBase
{
    public const int MaximumProcesses = 2048;
    public const int MaximumCommandLength = 4096;

    private readonly IWindowsProcessDataAdapter _processes;
    private readonly SecretRedactor _redactor;

    public ProcessCollector(
        IWindowsProcessDataAdapter processes,
        SecretRedactor redactor,
        IWindowsPlatform? platform = null)
        : base(platform)
    {
        _processes = processes;
        _redactor = redactor;
    }

    public override string Name => "windows-processes";

    public override InventoryCategory Category => InventoryCategory.Process;

    public override async ValueTask<InventoryContribution> CollectAsync(
        CollectorContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Options.ScanProcesses)
        {
            return new InventoryContribution { Source = Name };
        }

        IReadOnlyList<WindowsProcessDataSnapshot> rows = await _processes.GetAsync(
            MaximumProcesses,
            MaximumCommandLength,
            cancellationToken).ConfigureAwait(false);
        return new InventoryContribution
        {
            Source = Name,
            Processes = rows.Select(Map)
                .DistinctBy(static process => process.ProcessId)
                .OrderBy(static process => process.ProcessId)
                .ToArray(),
        };
    }

    public ProcessInfo Map(WindowsProcessDataSnapshot process)
    {
        ArgumentNullException.ThrowIfNull(process);
        string? command = process.CommandLine;
        if (command?.Length > MaximumCommandLength)
        {
            command = command[..MaximumCommandLength];
        }

        return new ProcessInfo(
            process.ProcessId,
            process.Name,
            process.Owner,
            command is null ? null : _redactor.Redact(command),
            process.StartedAt,
            process.WorkingSetBytes);
    }
}
