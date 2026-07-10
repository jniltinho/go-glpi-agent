using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using DotnetGlpiAgent.Core.Configuration;

namespace DotnetGlpiAgent.Protocol.Transport;

public static class GlpiHttpClientFactory
{
    public static GlpiClient Create(AgentOptions agentOptions)
    {
        ArgumentNullException.ThrowIfNull(agentOptions);
        if (agentOptions.Server is null)
        {
            throw new GlpiProtocolException(
                GlpiFailureKind.Configuration,
                "server-required",
                "A GLPI server URI is required for server submission.");
        }

        var handler = new HttpClientHandler();
        ConfigureProxy(handler, agentOptions);
        ConfigureClientCertificate(handler, agentOptions);
        ConfigureServerCertificateValidation(handler, agentOptions);
        var client = new HttpClient(handler, true)
        {
            Timeout = agentOptions.HttpTimeout,
        };
        return new GlpiClient(
            client,
            new GlpiProtocolOptions
            {
                Server = agentOptions.Server,
                Compression = agentOptions.Compression,
                UserName = agentOptions.UserName,
                Password = agentOptions.Password,
                Lazy = agentOptions.Lazy,
                Force = agentOptions.Force,
            },
            true);
    }

    private static void ConfigureProxy(HttpClientHandler handler, AgentOptions options)
    {
        if (options.Proxy is null)
        {
            handler.UseProxy = true;
            handler.Proxy = WebRequest.DefaultWebProxy;
            return;
        }

        var proxy = new WebProxy(options.Proxy);
        if (!string.IsNullOrWhiteSpace(options.ProxyUserName))
        {
            proxy.Credentials = new NetworkCredential(options.ProxyUserName, options.ProxyPassword);
        }

        handler.Proxy = proxy;
        handler.UseProxy = true;
    }

    private static void ConfigureClientCertificate(HttpClientHandler handler, AgentOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ClientCertificateFile))
        {
            return;
        }

        X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12FromFile(
            options.ClientCertificateFile,
            options.ClientCertificatePassword);
        handler.ClientCertificates.Add(certificate);
    }

    private static void ConfigureServerCertificateValidation(HttpClientHandler handler, AgentOptions options)
    {
        if (options.AllowInsecureTls)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            return;
        }

        if (string.IsNullOrWhiteSpace(options.CaCertificateFile))
        {
            return;
        }

        var customRoots = new X509Certificate2Collection();
        customRoots.ImportFromPemFile(options.CaCertificateFile);
        handler.ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
            errors == SslPolicyErrors.None || ValidateWithCustomRoots(certificate, customRoots);
    }

    private static bool ValidateWithCustomRoots(
        X509Certificate2? certificate,
        X509Certificate2Collection customRoots)
    {
        if (certificate is null || customRoots.Count == 0)
        {
            return false;
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(customRoots);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return chain.Build(certificate);
    }
}
