using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Configuration;
using DotnetGlpiAgent.Core.Diagnostics;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Windows.Bcl;
using DotnetGlpiAgent.Windows.Collectors;
using DotnetGlpiAgent.Windows.Management;

namespace DotnetGlpiAgent.Windows.Tests;

public sealed class UserSessionProcessTests
{
    [Fact]
    public void UserSessionMap_UsesSidMembershipAndInteractiveSessionIdentity()
    {
        WmiRow[] users =
        [
            Row(("SID", "S-1-5-21-1000"), ("Name", "alice"), ("Domain", "HOST"), ("FullName", "Alice Example"), ("LocalAccount", true), ("Disabled", false)),
            Row(("SID", "S-1-5-21-1001"), ("Name", "bob"), ("Domain", "HOST"), ("LocalAccount", true), ("Disabled", true)),
        ];
        WmiRow[] groups =
        [
            Row(("SID", "S-1-5-32-544"), ("Name", "Administrators"), ("Domain", "HOST"), ("LocalAccount", true)),
        ];
        WmiRow[] memberships =
        [
            Row(
                ("GroupComponent", "\\\\HOST\\root\\cimv2:Win32_Group.Domain=\"HOST\",Name=\"Administrators\""),
                ("PartComponent", "\\\\HOST\\root\\cimv2:Win32_UserAccount.Domain=\"HOST\",Name=\"alice\"")),
        ];
        WmiRow[] sessions =
        [
            Row(("LogonId", "42"), ("LogonType", 2), ("StartTime", "20260710010000.000000-180")),
            Row(("LogonId", "43"), ("LogonType", 5), ("StartTime", "20260710010000.000000-180")),
        ];
        WmiRow[] loggedOn =
        [
            Row(
                ("Antecedent", "\\\\HOST\\root\\cimv2:Win32_UserAccount.Domain=\"HOST\",Name=\"alice\""),
                ("Dependent", "\\\\HOST\\root\\cimv2:Win32_LogonSession.LogonId=\"42\"")),
        ];

        (UserInfo[] mappedUsers, GroupInfo[] mappedGroups, SessionInfo[] mappedSessions) = UserSessionCollector.Map(
            users,
            groups,
            memberships,
            sessions,
            loggedOn,
            @"HOST\alice");

        Assert.Equal(2, mappedUsers.Length);
        Assert.True(mappedUsers.Single(static user => user.Name == "bob").Disabled);
        Assert.Equal(["S-1-5-21-1000"], Assert.Single(mappedGroups).MemberIds);
        SessionInfo session = Assert.Single(mappedSessions);
        Assert.Equal("42", session.Id);
        Assert.Equal("Interactive", session.SessionType);
        Assert.True(session.Active);
    }

    [Fact]
    public void UserSessionMap_FallsBackToActiveComputerSystemUser()
    {
        (UserInfo[] _, GroupInfo[] _, SessionInfo[] sessions) = UserSessionCollector.Map(
            [],
            [],
            [],
            [],
            [],
            @"DOMAIN\active-user");

        SessionInfo session = Assert.Single(sessions);
        Assert.Equal("active-user", session.UserName);
        Assert.Equal("DOMAIN", session.Domain);
        Assert.True(session.Active);
    }

    [Fact]
    public async Task ProcessCollector_RedactsAndCapsOptInProcessDetails()
    {
        string longCommand = "app.exe --password=super-secret " + new string('x', 5000);
        var adapter = new FakeProcessDataAdapter(
        [
            new WindowsProcessDataSnapshot(20, "second", "DOMAIN\\user", longCommand, DateTimeOffset.UtcNow, 2048),
            new WindowsProcessDataSnapshot(10, "first", null, "first.exe token=token-value", null, 1024),
        ]);
        var collector = new ProcessCollector(
            adapter,
            new SecretRedactor(["super-secret", "token-value"]),
            SupportedPlatform.Instance);

        InventoryContribution result = await collector.CollectAsync(
            Context(scanProcesses: true),
            CancellationToken.None);

        Assert.Collection(
            result.Processes,
            process =>
            {
                Assert.Equal(10, process.ProcessId);
                Assert.DoesNotContain("token-value", process.CommandLine!, StringComparison.Ordinal);
            },
            process =>
            {
                Assert.Equal(20, process.ProcessId);
                Assert.InRange(process.CommandLine!.Length, 1, ProcessCollector.MaximumCommandLength);
                Assert.DoesNotContain("super-secret", process.CommandLine, StringComparison.Ordinal);
                Assert.Equal(@"DOMAIN\user", process.Owner);
            });
        Assert.Equal(ProcessCollector.MaximumProcesses, adapter.MaximumProcesses);
        Assert.Equal(ProcessCollector.MaximumCommandLength, adapter.MaximumCommandLength);
    }

    [Fact]
    public async Task ProcessCollector_DoesNotEnumerateWhenOptInIsDisabled()
    {
        var adapter = new FakeProcessDataAdapter([]);
        var collector = new ProcessCollector(adapter, new SecretRedactor(), SupportedPlatform.Instance);

        InventoryContribution result = await collector.CollectAsync(
            Context(scanProcesses: false),
            CancellationToken.None);

        Assert.Empty(result.Processes);
        Assert.False(adapter.WasCalled);
    }

    [Fact]
    public async Task ProcessCollector_AccessDeniedDegradesOnlyProcessCategory()
    {
        var collector = new ProcessCollector(
            new ThrowingProcessDataAdapter(),
            new SecretRedactor(),
            SupportedPlatform.Instance);

        CollectionRunResult result = await new InventoryCollectorOrchestrator(1).CollectAsync(
            [collector],
            new AgentOptions { ScanProcesses = true },
            "process-access-denied");

        Assert.Equal(CollectionState.AccessDenied, Assert.Single(result.Results).State);
    }

    private static CollectorContext Context(bool scanProcesses)
    {
        return new CollectorContext(
            new AgentOptions { ScanProcesses = scanProcesses },
            DateTimeOffset.UtcNow.AddMinutes(1),
            "process-fixture");
    }

    private static WmiRow Row(params (string Key, object? Value)[] values)
    {
        return new WmiRow(values.ToDictionary(static pair => pair.Key, static pair => pair.Value));
    }

    private sealed class FakeProcessDataAdapter(
        IReadOnlyList<WindowsProcessDataSnapshot> processes) : IWindowsProcessDataAdapter
    {
        public bool WasCalled { get; private set; }

        public int MaximumProcesses { get; private set; }

        public int MaximumCommandLength { get; private set; }

        public ValueTask<IReadOnlyList<WindowsProcessDataSnapshot>> GetAsync(
            int maximumProcesses,
            int maximumCommandLength,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            MaximumProcesses = maximumProcesses;
            MaximumCommandLength = maximumCommandLength;
            return ValueTask.FromResult(processes);
        }
    }

    private sealed class ThrowingProcessDataAdapter : IWindowsProcessDataAdapter
    {
        public ValueTask<IReadOnlyList<WindowsProcessDataSnapshot>> GetAsync(
            int maximumProcesses,
            int maximumCommandLength,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromException<IReadOnlyList<WindowsProcessDataSnapshot>>(
                new CollectorFailureException(CollectionState.AccessDenied, "process-access-denied", "Access denied."));
        }
    }

    private sealed class SupportedPlatform : IWindowsPlatform
    {
        public static SupportedPlatform Instance { get; } = new();

        public bool IsWindows => true;
    }
}
