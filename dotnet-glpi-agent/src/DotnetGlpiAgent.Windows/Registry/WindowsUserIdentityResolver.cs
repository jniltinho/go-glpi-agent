using System.Security.Principal;

namespace DotnetGlpiAgent.Windows.Registry;

public interface IWindowsUserIdentityResolver
{
    string? ResolveAccountName(string sid);
}

public sealed class WindowsUserIdentityResolver : IWindowsUserIdentityResolver
{
    public string? ResolveAccountName(string sid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sid);
        try
        {
            var identifier = new SecurityIdentifier(sid);
            return identifier.Translate(typeof(NTAccount)).Value;
        }
        catch (Exception exception) when (exception is IdentityNotMappedException or ArgumentException or SystemException)
        {
            return null;
        }
    }
}
