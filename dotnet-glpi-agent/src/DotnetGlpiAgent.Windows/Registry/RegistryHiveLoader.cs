using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Inventory;
using Microsoft.Win32.SafeHandles;

namespace DotnetGlpiAgent.Windows.Registry;

public interface IRegistryHiveLoader
{
    ValueTask<IDisposable> LoadAsync(
        string mountName,
        string hiveFile,
        CancellationToken cancellationToken);
}

public sealed partial class RegistryHiveLoader : IRegistryHiveLoader
{
    public ValueTask<IDisposable> LoadAsync(
        string mountName,
        string hiveFile,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateMountName(mountName);
        string fullPath = Path.GetFullPath(hiveFile);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The offline registry hive was not found.", fullPath);
        }

        if (!TryEnablePrivilege("SeBackupPrivilege") || !TryEnablePrivilege("SeRestorePrivilege"))
        {
            throw new CollectorFailureException(
                CollectionState.AccessDenied,
                "registry-hive-privilege-unavailable",
                "The process cannot enable the privileges required to load an offline registry hive.");
        }

        int error = NativeMethods.RegLoadKey(HKeyUsers, mountName, fullPath);
        if (error != 0)
        {
            throw new CollectorFailureException(
                error is 5 or 1314 ? CollectionState.AccessDenied : CollectionState.Failed,
                "registry-hive-load-failed",
                new Win32Exception(error).Message);
        }

        return ValueTask.FromResult<IDisposable>(new RegistryHiveLease(mountName));
    }

    private static nint HKeyUsers => new(unchecked((int)0x80000003));

    private static void ValidateMountName(string mountName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mountName);
        if (mountName.Length > 64
            || mountName.Any(static character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')))
        {
            throw new ArgumentException("The registry mount name contains unsupported characters.", nameof(mountName));
        }
    }

    private static bool TryEnablePrivilege(string privilegeName)
    {
        using Process process = Process.GetCurrentProcess();
        if (!NativeMethods.OpenProcessToken(
                process.Handle,
                TokenAdjustPrivileges | TokenQuery,
                out SafeAccessTokenHandle token))
        {
            return false;
        }

        using (token)
        {
            if (!NativeMethods.LookupPrivilegeValue(null, privilegeName, out Luid luid))
            {
                return false;
            }

            var privileges = new TokenPrivileges
            {
                PrivilegeCount = 1,
                Privileges = new LuidAndAttributes
                {
                    Luid = luid,
                    Attributes = PrivilegeEnabled,
                },
            };
            if (!NativeMethods.AdjustTokenPrivileges(
                    token,
                    false,
                    ref privileges,
                    0,
                    0,
                    0))
            {
                return false;
            }

            return Marshal.GetLastPInvokeError() != ErrorNotAllAssigned;
        }
    }

    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const uint PrivilegeEnabled = 0x00000002;
    private const int ErrorNotAllAssigned = 1300;

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LuidAndAttributes
    {
        public Luid Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public LuidAndAttributes Privileges;
    }

    private sealed class RegistryHiveLease(string mountName) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            GC.SuppressFinalize(this);
            int error = NativeMethods.RegUnLoadKey(HKeyUsers, mountName);
            if (error != 0)
            {
                // Best-effort retry after letting lingering key handles finalize.
                // Never throw from Dispose: a stuck hive must degrade the profile
                // scan, not destroy the whole software category during unwind.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                _ = NativeMethods.RegUnLoadKey(HKeyUsers, mountName);
            }
        }
    }

    private static partial class NativeMethods
    {
        [LibraryImport("advapi32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool OpenProcessToken(
            nint processHandle,
            uint desiredAccess,
            out SafeAccessTokenHandle tokenHandle);

        [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool LookupPrivilegeValue(
            string? systemName,
            string name,
            out Luid luid);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AdjustTokenPrivileges(
            SafeAccessTokenHandle tokenHandle,
            [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
            ref TokenPrivileges newState,
            uint bufferLength,
            nint previousState,
            nint returnLength);

        [LibraryImport("advapi32.dll", EntryPoint = "RegLoadKeyW", StringMarshalling = StringMarshalling.Utf16)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int RegLoadKey(nint key, string subKey, string file);

        [LibraryImport("advapi32.dll", EntryPoint = "RegUnLoadKeyW", StringMarshalling = StringMarshalling.Utf16)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int RegUnLoadKey(nint key, string subKey);
    }
}
