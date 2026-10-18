// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Daemon;
using Vericairn.Protocol;
using Vericairn.Sdk;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class RuntimeNetworkConfiguration(CommandContext context)
{
    public int RuntimeConfigure(string root, string? localNetwork, bool allowPublic, bool json)
    {
        try
        {
            if (!IsEstate(root))
            {
                return context.Fail(NotAnEstate(root));
            }

            if (allowPublic && localNetwork is null)
            {
                return context.Fail("--allow-public allows the address --local-network gives; give both.");
            }

            string directory = Path.GetDirectoryName(DaemonHost.EndpointFilePath(root))!;
            RuntimeSettings settings;
            string? exposure = null;
            if (localNetwork is null)
            {
                settings = RuntimeSettings.Load(directory);
            }
            else
            {
                string? address = null;
                if (!localNetwork.Equals("off", StringComparison.OrdinalIgnoreCase))
                {
                    if (!RuntimeSettings.TryParseAddress(localNetwork, out System.Net.IPEndPoint? endpoint, out string? problem))
                    {
                        return context.Fail(problem!);
                    }

                    // The profile is for a private network: a wildcard or public address is the operator's explicit choice.
                    exposure = RuntimeSettings.Exposure(endpoint!.Address);
                    if (exposure is not null && !allowPublic)
                    {
                        return context.Fail($"{endpoint} is {exposure}, and the local-network profile is for a private network: give this machine's address on it (in 10/8, 172.16/12, 192.168/16, link-local or fc00::/7), or add --allow-public to listen there anyway. Nothing was changed.");
                    }

                    address = endpoint.ToString();
                }

                settings = new RuntimeSettings(address, AllowPublic: exposure is not null);
                settings.Save(directory);
            }

            DaemonEndpoint? running = DaemonHost.Discover(root);
            bool alive = running is not null && VericairnClient.IsAlive(running);
            string? served = running?.LocalNetwork is LocalNetworkEndpoint lan ? new System.Net.IPEndPoint(System.Net.IPAddress.Parse(lan.Host), lan.Port).ToString() : null;
            if (json)
            {
                context.Output.WriteLine(JsonSerializer.Serialize(new { localNetwork = settings.LocalNetwork, allowPublic = settings.AllowPublic, served = alive ? served : null, restart = alive && localNetwork is not null }, ProtocolJson.Options));
                return Clean;
            }

            context.Output.WriteLine(settings.LocalNetwork is null
                ? "the local-network profile is off: the runtime listens on its local socket and loopback only"
                : $"the runtime serves the local-network profile at {settings.LocalNetwork}, to devices that pair with it, from its next start");
            if (settings.AllowPublic)
            {
                context.Output.WriteLine("  allowed with --allow-public: a wildcard or public address, where every device that reaches it may try to pair");
            }
            if (alive)
            {
                context.Output.WriteLine(served is null ? $"  daemon {running!.ProcessId} serves no local-network profile now" : $"  daemon {running!.ProcessId} serves it now at {served}");
                if (localNetwork is not null)
                {
                    context.Output.WriteLine("  the running daemon keeps its listeners until it restarts: 'vericairn runtime stop', then 'vericairn runtime start'");
                }
            }

            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return context.Fail(exception.Message);
        }
    }

    public int RuntimeRotate(string root, bool compromised, bool identity, string? reason, bool json)
    {
        try
        {
            if (!IsEstate(root))
            {
                return context.Fail(NotAnEstate(root));
            }

            DaemonEndpoint? endpoint = DaemonHost.Discover(root);
            if (endpoint is not null && VericairnClient.IsAlive(endpoint))
            {
                using var client = new VericairnClient(endpoint);
                (CertificateRotated? rotated, Problem? problem) = client.RotateCertificate(new CertificateRotateRequest(compromised, reason, identity));
                if (rotated is null)
                {
                    return context.Fail(problem?.Message ?? "The certificate was not rotated.");
                }

                if (json)
                {
                    context.Output.WriteLine(JsonSerializer.Serialize(rotated, ProtocolJson.Options));
                    return Clean;
                }

                context.Output.WriteLine($"runtime certificate {rotated.Certificate}, until {rotated.NotAfter}, replaced {rotated.Previous}");
                context.Output.WriteLine(rotated.Identity is null
                    ? "  paired devices keep working: they pin the runtime identity, which signs the certificate in use"
                    : $"  runtime identity replaced by {rotated.Identity}: every paired device pairs again");
                if (rotated.Compromised)
                {
                    context.Output.WriteLine($"  after a compromise: {rotated.Closed} open connections were closed, and the old certificate is refused by every client");
                }

                return Clean;
            }

            // No daemon: the files are replaced now and the next start serves them; nothing is open to close.
            string directory = Path.GetDirectoryName(DaemonHost.EndpointFilePath(root))!;
            string? replacedIdentity = null;
            if (identity)
            {
                if (!File.Exists(Path.Combine(directory, RuntimeIdentity.FileName)))
                {
                    return context.Fail("This runtime has no identity to replace: it is made when the local-network profile is first served.");
                }

                replacedIdentity = RuntimeIdentity.LoadOrCreate(directory).Replace(directory).Fingerprint;
            }

            using System.Security.Cryptography.X509Certificates.X509Certificate2 next = RuntimeCertificate.Replace(directory);
            string fingerprint = RuntimeCertificate.Fingerprint(next);
            if (json)
            {
                context.Output.WriteLine(JsonSerializer.Serialize(new { certificate = fingerprint, identity = replacedIdentity, served = false }, ProtocolJson.Options));
                return Clean;
            }

            context.Output.WriteLine($"no daemon is serving this estate: the runtime certificate is now {fingerprint}, served from the next start");
            if (replacedIdentity is not null)
            {
                context.Output.WriteLine($"  runtime identity replaced by {replacedIdentity}: every paired device pairs again");
            }

            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException or System.Security.Cryptography.CryptographicException)
        {
            return context.Fail(exception.Message);
        }
    }
}
