using System.Text.RegularExpressions;

using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Core.Normalization;
using DotnetGlpiAgent.Windows.Management;

namespace DotnetGlpiAgent.Windows.Collectors;

public sealed partial class UserSessionCollector : WindowsCollectorBase
{
    private readonly IWmiQueryAdapter _wmi;

    public UserSessionCollector(IWmiQueryAdapter wmi, IWindowsPlatform? platform = null)
        : base(platform)
    {
        _wmi = wmi;
    }

    public override string Name => "windows-users-sessions";

    public override InventoryCategory Category => InventoryCategory.User;

    public override async ValueTask<InventoryContribution> CollectAsync(
        CollectorContext context,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WmiRow> users = await QueryAsync(
            "Win32_UserAccount",
            ["Disabled", "Domain", "FullName", "LocalAccount", "Name", "SID"],
            "LocalAccount = TRUE",
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<WmiRow> groups = await QueryAsync(
            "Win32_Group",
            ["Domain", "LocalAccount", "Name", "SID"],
            "LocalAccount = TRUE",
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<WmiRow> memberships = await QueryAsync(
            "Win32_GroupUser",
            ["GroupComponent", "PartComponent"],
            null,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<WmiRow> logonSessions = await QueryAsync(
            "Win32_LogonSession",
            ["LogonId", "LogonType", "StartTime"],
            null,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<WmiRow> loggedOnUsers = await QueryAsync(
            "Win32_LoggedOnUser",
            ["Antecedent", "Dependent"],
            null,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<WmiRow> computerSystems = await QueryAsync(
            "Win32_ComputerSystem",
            ["UserName"],
            null,
            cancellationToken).ConfigureAwait(false);
        string? activeUser = computerSystems.Count > 0
            ? InventoryNormalizer.CleanString(computerSystems[0].GetString("UserName"))
            : null;
        (UserInfo[] mappedUsers, GroupInfo[] mappedGroups, SessionInfo[] mappedSessions) = Map(
            users,
            groups,
            memberships,
            logonSessions,
            loggedOnUsers,
            activeUser);

        return new InventoryContribution
        {
            Source = Name,
            Users = mappedUsers,
            Groups = mappedGroups,
            Sessions = mappedSessions,
        };
    }

    public static (UserInfo[] Users, GroupInfo[] Groups, SessionInfo[] Sessions) Map(
        IEnumerable<WmiRow> userRows,
        IEnumerable<WmiRow> groupRows,
        IEnumerable<WmiRow> membershipRows,
        IEnumerable<WmiRow> sessionRows,
        IEnumerable<WmiRow> loggedOnRows,
        string? activeUser)
    {
        ArgumentNullException.ThrowIfNull(userRows);
        ArgumentNullException.ThrowIfNull(groupRows);
        ArgumentNullException.ThrowIfNull(membershipRows);
        ArgumentNullException.ThrowIfNull(sessionRows);
        ArgumentNullException.ThrowIfNull(loggedOnRows);

        UserInfo[] users = userRows.Select(MapUser)
            .DistinctBy(static user => user.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static user => user.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var userIds = users.ToDictionary(
            static user => AccountKey(user.Domain, user.Name),
            static user => user.Id,
            StringComparer.OrdinalIgnoreCase);
        var groupMembers = BuildGroupMembers(membershipRows, userIds);
        GroupInfo[] groups = groupRows.Select(row => MapGroup(row, groupMembers))
            .DistinctBy(static group => group.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        SessionInfo[] sessions = MapSessions(sessionRows, loggedOnRows, activeUser);
        return (users, groups, sessions);
    }

    private ValueTask<IReadOnlyList<WmiRow>> QueryAsync(
        string className,
        IReadOnlyList<string> properties,
        string? whereClause,
        CancellationToken cancellationToken)
    {
        return _wmi.QueryAsync(
            new WmiQuery(@"\\.\root\cimv2", className, properties, whereClause, Timeout),
            cancellationToken);
    }

    private static UserInfo MapUser(WmiRow row)
    {
        string name = InventoryNormalizer.CleanString(row.GetString("Name")) ?? "unknown";
        string? domain = InventoryNormalizer.CleanString(row.GetString("Domain"));
        string id = InventoryNormalizer.CleanString(row.GetString("SID")) ?? AccountKey(domain, name);
        return new UserInfo(
            id,
            name,
            domain,
            InventoryNormalizer.CleanString(row.GetString("FullName")),
            row.GetBoolean("LocalAccount") ?? true,
            row.GetBoolean("Disabled") ?? false);
    }

    private static GroupInfo MapGroup(
        WmiRow row,
        Dictionary<string, IReadOnlyList<string>> groupMembers)
    {
        string name = InventoryNormalizer.CleanString(row.GetString("Name")) ?? "unknown";
        string? domain = InventoryNormalizer.CleanString(row.GetString("Domain"));
        string id = InventoryNormalizer.CleanString(row.GetString("SID")) ?? AccountKey(domain, name);
        string key = AccountKey(domain, name);
        return new GroupInfo(
            id,
            name,
            domain,
            row.GetBoolean("LocalAccount") ?? true,
            groupMembers.TryGetValue(key, out IReadOnlyList<string>? members) ? members : []);
    }

    private static Dictionary<string, IReadOnlyList<string>> BuildGroupMembers(
        IEnumerable<WmiRow> rows,
        Dictionary<string, string> userIds)
    {
        var members = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (WmiRow row in rows)
        {
            AccountReference? group = ParseAccountReference(row.GetString("GroupComponent"));
            AccountReference? user = ParseAccountReference(row.GetString("PartComponent"));
            if (group is null || user is null
                || !userIds.TryGetValue(AccountKey(user.Domain, user.Name), out string? userId))
            {
                continue;
            }

            string groupKey = AccountKey(group.Domain, group.Name);
            if (!members.TryGetValue(groupKey, out HashSet<string>? groupSet))
            {
                groupSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                members[groupKey] = groupSet;
            }

            groupSet.Add(userId);
        }

        return members.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<string>)pair.Value.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static SessionInfo[] MapSessions(
        IEnumerable<WmiRow> sessionRows,
        IEnumerable<WmiRow> loggedOnRows,
        string? activeUser)
    {
        var sessionById = sessionRows
            .Where(static row => IsInteractive(row.GetUInt32("LogonType")))
            .ToDictionary(
                static row => row.GetString("LogonId") ?? string.Empty,
                static row => row,
                StringComparer.OrdinalIgnoreCase);
        var sessions = new List<SessionInfo>();
        foreach (WmiRow association in loggedOnRows)
        {
            AccountReference? account = ParseAccountReference(association.GetString("Antecedent"));
            string? sessionId = ParseSessionId(association.GetString("Dependent"));
            if (account is null || sessionId is null || !sessionById.TryGetValue(sessionId, out WmiRow? session))
            {
                continue;
            }

            string qualifiedName = AccountKey(account.Domain, account.Name);
            uint? logonType = session.GetUInt32("LogonType");
            sessions.Add(new SessionInfo(
                sessionId,
                account.Name,
                account.Domain,
                MapLogonType(logonType),
                session.GetDateTime("StartTime"),
                string.Equals(qualifiedName, activeUser, StringComparison.OrdinalIgnoreCase)
                    || logonType is 2 or 10));
        }

        if (sessions.Count == 0 && ParseQualifiedAccount(activeUser) is { } active)
        {
            sessions.Add(new SessionInfo(
                InventoryNormalizer.StableKey(active.Domain, active.Name, "active"),
                active.Name,
                active.Domain,
                "Active",
                null,
                true));
        }

        return sessions
            .DistinctBy(static session => session.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static session => session.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static AccountReference? ParseAccountReference(string? value)
    {
        if (value is null)
        {
            return null;
        }

        Match match = AccountReferenceRegex().Match(value);
        return match.Success
            ? new AccountReference(Unescape(match.Groups["domain"].Value), Unescape(match.Groups["name"].Value))
            : null;
    }

    private static AccountReference? ParseQualifiedAccount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string[] parts = value.Split('\\', 2);
        return parts.Length == 2
            ? new AccountReference(parts[0], parts[1])
            : new AccountReference(null, parts[0]);
    }

    private static string? ParseSessionId(string? value)
    {
        if (value is null)
        {
            return null;
        }

        Match match = SessionReferenceRegex().Match(value);
        return match.Success ? match.Groups["id"].Value : null;
    }

    private static string AccountKey(string? domain, string name)
    {
        return string.IsNullOrWhiteSpace(domain) ? name : $"{domain}\\{name}";
    }

    private static string Unescape(string value) => value.Replace("\\\\", "\\", StringComparison.Ordinal).Replace("\\\"", "\"", StringComparison.Ordinal);

    private static bool IsInteractive(uint? logonType) => logonType is 2 or 7 or 10 or 11;

    private static string? MapLogonType(uint? logonType)
    {
        return logonType switch
        {
            2 => "Interactive",
            7 => "Unlock",
            10 => "RemoteInteractive",
            11 => "CachedInteractive",
            null => null,
            _ => $"LogonType {logonType}",
        };
    }

    private sealed record AccountReference(string? Domain, string Name);

    // Win32_LoggedOnUser.Antecedent references the Win32_Account base class on real systems.
    [GeneratedRegex("Win32_(?:Account|UserAccount|SystemAccount|Group)\\.Domain=\\\"(?<domain>(?:\\\\.|[^\"])*)\\\",Name=\\\"(?<name>(?:\\\\.|[^\"])*)\\\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex AccountReferenceRegex();

    [GeneratedRegex("Win32_LogonSession\\.LogonId=\\\"(?<id>[^\"]+)\\\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex SessionReferenceRegex();
}
