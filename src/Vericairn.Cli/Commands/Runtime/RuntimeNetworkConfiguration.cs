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
    public int RuntimeConfigure(string root, string? localNetwork, bool allowPublic, bool json, string? mcp = null, string[]? origins = null)
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

            bool? serveMcp = mcp?.ToLowerInvariant() switch
            {
                null => null,
                "on" => true,
                "off" => false,
                _ => throw new ArgumentException($"--mcp is on or off, not '{mcp}'."),
            };
            List<string>? allowed = null;
            if (origins is { Length: > 0 })
            {
                allowed = [];
                foreach (string origin in origins)
                {
                    if (origin.Equals("none", StringComparison.OrdinalIgnoreCase))
                    {
                        if (origins.Length > 1)
                        {
                            return context.Fail("--mcp-origin none empties the list; give it alone.");
                        }

                        continue;
                    }

                    if (!RuntimeSettings.TryNormalizeOrigin(origin, out string? normalized, out string? problem))
                    {
                        return context.Fail(problem! + " Nothing was changed.");
                    }

                    if (!allowed.Contains(normalized!, StringComparer.Ordinal))
                    {
                        allowed.Add(normalized!);
                    }
                }
            }

            string directory = Path.GetDirectoryName(DaemonHost.EndpointFilePath(root))!;
            RuntimeSettings settings = RuntimeSettings.Load(directory);
            string? exposure = null;
            if (localNetwork is not null)
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

                settings = settings with { LocalNetwork = address, AllowPublic = exposure is not null };
            }

            if (serveMcp is bool on)
            {
                settings = settings with { Mcp = on };
            }

            if (allowed is not null)
            {
                settings = settings with { AllowedOrigins = allowed };
            }

            bool changed = localNetwork is not null || serveMcp is not null || allowed is not null;
            if (changed)
            {
                settings.Save(directory);
            }

            DaemonEndpoint? running = DaemonHost.Discover(root);
            bool alive = running is not null && context.IsAlive(running);
            string? served = running?.LocalNetwork is LocalNetworkEndpoint lan ? new System.Net.IPEndPoint(System.Net.IPAddress.Parse(lan.Host), lan.Port).ToString() : null;
            if (json)
            {
                context.Output.WriteLine(JsonSerializer.Serialize(new
                {
                    localNetwork = settings.LocalNetwork, allowPublic = settings.AllowPublic, mcp = settings.Mcp, allowedOrigins = settings.AllowedOrigins,
                    served = alive ? served : null, mcpServed = alive ? running!.Mcp is not null : (bool?)null, restart = alive && changed,
                }, ProtocolJson.Options));
                return Clean;
            }

            context.Output.WriteLine(settings.LocalNetwork is null
                ? "the local-network profile is off: the runtime listens on its local socket and loopback only"
                : $"the runtime serves the local-network profile at {settings.LocalNetwork}, to devices that pair with it, from its next start");
            if (settings.AllowPublic)
            {
                context.Output.WriteLine("  allowed with --allow-public: a wildcard or public address, where every device that reaches it may try to pair");
            }
            context.Output.WriteLine(settings.Mcp
                ? "network MCP is on: /mcp on the loopback and local-network listeners, from the runtime's next start, for agents' and services' devices that sign every request"
                : "network MCP is off: /mcp is not served");
            if (settings.Mcp || settings.AllowedOrigins.Count > 0)
            {
                context.Output.WriteLine(settings.AllowedOrigins.Count == 0
                    ? "  origins allowed for /mcp: none, so a request naming an origin is refused"
                    : $"  origins allowed for /mcp: {string.Join(", ", settings.AllowedOrigins)}");
            }
            if (alive)
            {
                context.Output.WriteLine(served is null ? $"  daemon {running!.ProcessId} serves no local-network profile now" : $"  daemon {running!.ProcessId} serves it now at {served}");
                context.Output.WriteLine(running!.Mcp is null ? $"  daemon {running.ProcessId} serves no network MCP now" : $"  daemon {running.ProcessId} serves network MCP now at {running.Mcp}");
                if (changed)
                {
                    context.Output.WriteLine("  the running daemon keeps its listeners until it restarts: 'vericairn runtime stop', then 'vericairn runtime start'");
                }
            }

            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
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
            if (endpoint is not null && context.IsAlive(endpoint))
            {
                using var client = new VericairnClient(endpoint);
                (CertificateRotated? rotated, Problem? problem) = client.RotateCertificateAsync(new CertificateRotateRequest(compromised, reason, identity), context.CancellationToken).GetAwaiter().GetResult();
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
        catch (Exception exception) when ((exception is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException or System.Security.Cryptography.CryptographicException) && exception is not IClientOperationFailure)
        {
            return context.Fail(exception.Message);
        }
    }
}
