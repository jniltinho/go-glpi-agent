using DotnetGlpiAgent.Core.Configuration;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Protocol.Transport;

namespace DotnetGlpiAgent.App.Runtime;

public interface IAppRuntime
{
    ValueTask<InventorySnapshot> CollectAsync(AgentOptions options, CancellationToken cancellationToken);

    ValueTask<GlpiSubmissionResult> SubmitAsync(
        InventorySnapshot snapshot,
        AgentOptions options,
        CancellationToken cancellationToken);
}

public interface IAppRuntimeFactory
{
    IAppRuntime Create(AgentOptions options);
}
