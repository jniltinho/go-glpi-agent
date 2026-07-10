namespace DotnetGlpiAgent.Core.Diagnostics;

public static class CorrelationContext
{
    private static readonly AsyncLocal<string?> CurrentValue = new();

    public static string? Current => CurrentValue.Value;

    public static IDisposable BeginScope(string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        string? previous = CurrentValue.Value;
        CurrentValue.Value = correlationId;
        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            CurrentValue.Value = previous;
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
