using System.Security.AccessControl;
using System.Security.Principal;
using DotnetGlpiAgent.Core.Configuration;
using DotnetGlpiAgent.Core.Diagnostics;

namespace DotnetGlpiAgent.Windows.Security;

public interface IFileAccessControlInspector
{
    bool IsUnprivilegedWritable(string path);
}

public sealed class WindowsPathSecurityPolicy
{
    private readonly IFileAccessControlInspector _inspector;

    public WindowsPathSecurityPolicy(IFileAccessControlInspector inspector)
    {
        _inspector = inspector;
    }

    public string ValidateTrustedPath(string path, string trustedRoot, string description)
    {
        string fullPath = AgentPathPolicy.EnsureWithinRoot(path, trustedRoot, description);
        string current = ExistingPathOrParent(fullPath);
        while (current.Length > 0)
        {
            if (_inspector.IsUnprivilegedWritable(current))
            {
                throw new AgentConfigurationException(
                    $"The {description} path is writable by an unprivileged Windows principal.");
            }

            if (string.Equals(current, Path.GetFullPath(trustedRoot).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                return fullPath;
            }

            current = Path.GetDirectoryName(current)?.TrimEnd('\\', '/') ?? string.Empty;
        }

        throw new AgentConfigurationException($"The {description} path does not have a trusted existing parent.");
    }

    private static string ExistingPathOrParent(string path)
    {
        string current = path.TrimEnd('\\', '/');
        while (current.Length > 0 && !File.Exists(current) && !Directory.Exists(current))
        {
            current = Path.GetDirectoryName(current)?.TrimEnd('\\', '/') ?? string.Empty;
        }

        return current;
    }
}

public sealed class WindowsFileAccessControlInspector : IFileAccessControlInspector
{
    private const FileSystemRights WriteRights = FileSystemRights.Write
        | FileSystemRights.Modify
        | FileSystemRights.FullControl
        | FileSystemRights.CreateFiles
        | FileSystemRights.CreateDirectories
        | FileSystemRights.Delete;

    private static readonly HashSet<string> UnprivilegedSids =
    [
        new SecurityIdentifier(WellKnownSidType.WorldSid, null).Value,
        new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null).Value,
        new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null).Value,
    ];

    public bool IsUnprivilegedWritable(string path)
    {
        FileSystemSecurity security = Directory.Exists(path)
            ? new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access)
            : new FileInfo(path).GetAccessControl(AccessControlSections.Access);
        AuthorizationRuleCollection rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));
        return rules.OfType<FileSystemAccessRule>().Any(static rule =>
            rule.AccessControlType == AccessControlType.Allow
            && (rule.FileSystemRights & WriteRights) != 0
            && rule.IdentityReference is SecurityIdentifier sid
            && UnprivilegedSids.Contains(sid.Value));
    }
}

public sealed class WindowsFilePermissionHardener : IFilePermissionHardener
{
    public void HardenDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(path);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(DirectoryRule(WellKnownSidType.LocalSystemSid));
        security.AddAccessRule(DirectoryRule(WellKnownSidType.BuiltinAdministratorsSid));
        new DirectoryInfo(path).SetAccessControl(security);
    }

    public void HardenFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var security = new FileSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(FileRule(WellKnownSidType.LocalSystemSid));
        security.AddAccessRule(FileRule(WellKnownSidType.BuiltinAdministratorsSid));
        new FileInfo(path).SetAccessControl(security);
    }

    private static FileSystemAccessRule DirectoryRule(WellKnownSidType type)
    {
        return new FileSystemAccessRule(
            new SecurityIdentifier(type, null),
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow);
    }

    private static FileSystemAccessRule FileRule(WellKnownSidType type)
    {
        return new FileSystemAccessRule(
            new SecurityIdentifier(type, null),
            FileSystemRights.FullControl,
            AccessControlType.Allow);
    }
}
