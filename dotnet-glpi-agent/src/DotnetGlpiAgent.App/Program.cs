using DotnetGlpiAgent.App.Cli;
using DotnetGlpiAgent.App.Runtime;
using DotnetGlpiAgent.App.Service;
using Microsoft.Extensions.Hosting.WindowsServices;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

if (args.FirstOrDefault()?.Equals("service", StringComparison.OrdinalIgnoreCase) == true
    || WindowsServiceHelpers.IsWindowsService())
{
    return await WindowsServiceProgram.RunAsync(args, cancellation.Token);
}

var app = new CommandApp(new AppRuntimeFactory(), Console.Out, Console.Error);
return await app.RunAsync(args, cancellation.Token);
