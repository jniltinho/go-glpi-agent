using System.Xml.Linq;
using DotnetGlpiAgent.Core.Configuration;

namespace DotnetGlpiAgent.Core.Tests;

public sealed class MsiAuthoringTests
{
    private static string FindRepoFile(params string[] relativeParts)
    {
        string dir = AppContext.BaseDirectory;
        for (var i = 0; i < 12; i++)
        {
            string candidate = Path.Combine([dir, .. relativeParts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            DirectoryInfo? parent = Directory.GetParent(dir);
            if (parent is null)
            {
                break;
            }

            dir = parent.FullName;
        }

        // Walk up from the test project itself when BaseDirectory is under bin/.
        dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine([dir, .. relativeParts]);
    }

    private static readonly XNamespace Wxs = "http://wixtoolset.org/schemas/v4/wxs";
    private static readonly XNamespace Util = "http://wixtoolset.org/schemas/v4/wxs/util";

    [Fact]
    public void Package_wxs_declares_stable_identity_and_service_tables()
    {
        string path = FindRepoFile("packaging", "wix", "Package.wxs");
        Assert.True(File.Exists(path), $"Missing {path}");
        XDocument document = XDocument.Load(path);

        XElement package = Assert.Single(document.Descendants(Wxs + "Package"));
        Assert.Equal("{474BE2D1-1A58-47E6-B9FE-700411A9E1B3}", (string?)package.Attribute("UpgradeCode"));
        Assert.Equal("perMachine", (string?)package.Attribute("Scope"));

        XElement serviceInstall = Assert.Single(document.Descendants(Wxs + "ServiceInstall"));
        Assert.Equal("DotnetGlpiAgent", (string?)serviceInstall.Attribute("Name"));
        Assert.Equal("LocalSystem", (string?)serviceInstall.Attribute("Account"));
        XElement recovery = Assert.Single(serviceInstall.Descendants(Util + "ServiceConfig"));
        Assert.Equal("restart", (string?)recovery.Attribute("FirstFailureActionType"));

        Assert.Contains(
            document.Descendants(Wxs + "ServiceControl"),
            element => (string?)element.Attribute("Remove") == "uninstall"
                && (string?)element.Attribute("Stop") == "both");
        Assert.Contains(
            document.Descendants(Wxs + "RegistryValue"),
            element => (string?)element.Attribute("Name") == "DelayedAutostart");

        XElement majorUpgrade = Assert.Single(document.Descendants(Wxs + "MajorUpgrade"));
        Assert.False(string.IsNullOrWhiteSpace((string?)majorUpgrade.Attribute("DowngradeErrorMessage")));
        Assert.Contains(
            document.Descendants(Wxs + "Launch"),
            element => (string?)element.Attribute("Condition") == "VersionNT64");
    }

    [Fact]
    public void Package_wxs_restores_server_and_tag_on_repair_and_upgrade()
    {
        string path = FindRepoFile("packaging", "wix", "Package.wxs");
        XDocument document = XDocument.Load(path);

        // Remember-property pattern: without it, MSI repair rewrites the ini
        // values with the empty SERVER/TAG of the maintenance session.
        foreach ((string property, string saved, string registryName) in new[]
        {
            ("SERVER", "SAVEDSERVER", "ServerValue"),
            ("TAG", "SAVEDTAG", "TagValue"),
        })
        {
            XElement search = Assert.Single(
                document.Descendants(Wxs + "Property"),
                element => (string?)element.Attribute("Id") == saved);
            Assert.Equal(registryName, (string?)search.Descendants(Wxs + "RegistrySearch").Single().Attribute("Name"));

            XElement restore = Assert.Single(
                document.Descendants(Wxs + "SetProperty"),
                element => (string?)element.Attribute("Id") == property);
            Assert.Equal($"[{saved}]", (string?)restore.Attribute("Value"));

            Assert.Contains(
                document.Descendants(Wxs + "RegistryValue"),
                element => (string?)element.Attribute("Name") == registryName
                    && (string?)element.Attribute("Value") == $"[{property}]");
        }
    }

    [Fact]
    public void Package_wxs_exposes_only_documented_public_properties()
    {
        string path = FindRepoFile("packaging", "wix", "Package.wxs");
        XDocument document = XDocument.Load(path);
        XNamespace ns = "http://wixtoolset.org/schemas/v4/wxs";
        var propertyIds = document.Descendants(ns + "Property")
            .Select(element => (string?)element.Attribute("Id"))
            .Where(id => id is not null)
            .Select(id => id!)
            .ToHashSet(StringComparer.Ordinal);

        string[] required =
        [
            "SERVER",
            "TAG",
            "STARTSERVICE",
            "RUNNOW",
            "PURGE",
            "INSTALLDIR",
            "PURGEDATADIR",
            "WIXUI_EXITDIALOGOPTIONALTEXT",
        ];
        foreach (string id in required)
        {
            Assert.Contains(id, propertyIds);
        }

        string[] forbiddenSecrets =
        [
            "PASSWORD",
            "PROXYPASSWORD",
            "PROXY_PASSWORD",
            "CLIENTCERTPASSWORD",
            "CLIENT_CERT_PASSWORD",
        ];
        foreach (string id in forbiddenSecrets)
        {
            Assert.DoesNotContain(id, propertyIds);
        }
    }

    [Fact]
    public void Package_wxs_preserves_config_and_supports_purge()
    {
        string path = FindRepoFile("packaging", "wix", "Package.wxs");
        XDocument document = XDocument.Load(path);

        XElement configuration = Assert.Single(
            document.Descendants(Wxs + "Component"),
            element => (string?)element.Attribute("Id") == "DefaultConfiguration");
        Assert.Equal("yes", (string?)configuration.Attribute("Permanent"));
        Assert.Equal("yes", (string?)configuration.Attribute("NeverOverwrite"));

        // PURGE=1 removes both the ProgramData root and the retained agent.cfg
        // in the install folder.
        var purgeTargets = document.Descendants(Util + "RemoveFolderEx").ToList();
        Assert.Equal(2, purgeTargets.Count);
        foreach (XElement purge in purgeTargets)
        {
            Assert.Equal("uninstall", (string?)purge.Attribute("On"));
            Assert.Equal("PURGE = 1", (string?)purge.Attribute("Condition"));
        }

        Assert.Equal(
            new[] { "PURGEDATADIR", "PURGEINSTALLDIR" },
            purgeTargets.Select(element => (string?)element.Attribute("Property")).Order().ToArray());

        XElement runNow = Assert.Single(
            document.Descendants(Wxs + "CustomAction"),
            element => (string?)element.Attribute("Id") == "RunAgentAfterInstall");
        Assert.Equal("AgentExecutable", (string?)runNow.Attribute("FileRef"));
        Assert.Equal("ignore", (string?)runNow.Attribute("Return"));
        Assert.Equal("commit", (string?)runNow.Attribute("Execute"));
    }

    [Fact]
    public void Default_agent_cfg_is_flat_key_value_and_loads_without_server()
    {
        string path = FindRepoFile("packaging", "wix", "agent.cfg");
        Assert.True(File.Exists(path), $"Missing {path}");
        var loader = new AgentConfigLoader();
        var entries = loader.Load(path);
        var map = entries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);

        Assert.True(map.ContainsKey("local") || map.ContainsKey("server"), "defaults must allow service targets");
        Assert.Contains("delaytime", map.Keys);
        Assert.Contains("logfile", map.Keys);
        Assert.Contains("vardir", map.Keys);
        Assert.DoesNotContain(map.Keys, key => key.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Wixproj_pins_approved_sdk_and_signing_hooks()
    {
        string path = FindRepoFile("packaging", "wix", "DotnetGlpiAgent.Package.wixproj");
        string text = File.ReadAllText(path);

        Assert.Contains("WixToolset.Sdk/4.0.6", text, StringComparison.Ordinal);
        Assert.Contains("WixToolset.Util.wixext", text, StringComparison.Ordinal);
        Assert.Contains("SignMsi", text, StringComparison.Ordinal);
        Assert.Contains("SigningCertificateThumbprint", text, StringComparison.Ordinal);
        Assert.Contains("WriteUnsignedMarker", text, StringComparison.Ordinal);
        Assert.Contains("UNSIGNED", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Secret_provisioning_script_requires_elevation_and_hardens_acl()
    {
        string path = FindRepoFile("packaging", "scripts", "Set-AgentSecret.ps1");
        string text = File.ReadAllText(path);

        Assert.Contains("Administrator", text, StringComparison.Ordinal);
        Assert.Contains("icacls.exe", text, StringComparison.Ordinal);
        Assert.Contains("S-1-5-18", text, StringComparison.Ordinal);
        Assert.Contains("S-1-5-32-544", text, StringComparison.Ordinal);
        Assert.Contains("password", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConvertTo-SecureString", text, StringComparison.OrdinalIgnoreCase);
    }
}
