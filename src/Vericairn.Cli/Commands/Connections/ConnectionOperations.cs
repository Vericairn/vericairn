// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Json = System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Sdk;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class ConnectionOperations(CommandContext context)
{
    public int ConnectionsAdd(string address, string code, string name, string? custody, bool? presence, bool json)
    {
        try
        {
            var connections = new RuntimeConnections();
            IReadOnlyList<RuntimeConnection> before = connections.List();
            RuntimeConnection connection = connections.Add(address, code, new PairingOptions(name) { Custody = custody, Presence = presence });
            List<RuntimeConnection> replaced = [.. before.Where(earlier => connections.Find(earlier.Id) is null)];
            ConnectionStatus status = connections.Probe(connection);
            if (json)
            {
                Json.JsonObject added = Json.JsonNode.Parse(JsonSerializer.Serialize(ConnectionJson(status), ProtocolJson.Options))!.AsObject();
                added["replaced"] = Json.JsonNode.Parse(JsonSerializer.Serialize(replaced.Select(r => new { id = r.Id, device = r.Device, principal = r.Principal }), ProtocolJson.Options));
                context.Output.WriteLine(added.ToJsonString());
                return Clean;
            }

            context.Output.WriteLine($"paired as device {connection.Device} of {connection.PrincipalName ?? connection.Principal} ({connection.Principal}) with {connection.EstateName ?? connection.Estate} at {connection.Address}, permitted {string.Join(", ", connection.Profiles)}");
            context.Output.WriteLine($"  runtime identity {RuntimeIdentityScheme.Fingerprint(connection.Identity)}, pinned on this machine");
            foreach (RuntimeConnection earlier in replaced)
            {
                context.Output.WriteLine($"  replaced connection {earlier.Id}: device {earlier.Device} stays admitted until an administrator revokes it");
            }

            context.Output.WriteLine($"connection {connection.Id}  {status.State}: {status.Message}");
            return Clean;
        }
        catch (PairingException exception)
        {
            return context.Fail(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return context.Fail(exception.ParamName is { } parameter ? exception.Message.Replace($" (Parameter '{parameter}')", string.Empty, StringComparison.Ordinal) : exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.Cryptography.CryptographicException)
        {
            return context.Fail(exception.Message);
        }
    }

    public int ConnectionsList(bool json)
    {
        try
        {
            var connections = new RuntimeConnections();
            List<ConnectionStatus> statuses = connections.List().Select(c => connections.Probe(c)).ToList();
            if (json)
            {
                context.Output.WriteLine(JsonSerializer.Serialize(statuses.Select(ConnectionJson), ProtocolJson.Options));
                return Clean;
            }

            if (statuses.Count == 0)
            {
                context.Output.WriteLine("no connections on this machine; add one with 'vericairn connections add <address> --code <code> --name <device name>'");
            }

            foreach (ConnectionStatus status in statuses)
            {
                RuntimeConnection connection = status.Connection;
                context.Output.WriteLine($"{connection.Id}  {connection.EstateName ?? connection.Estate}  {connection.Address}  {status.State}  device {connection.Device} ({connection.PrincipalName ?? connection.Principal})");
                context.Output.WriteLine($"  {status.Message}");
            }

            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return context.Fail(exception.Message);
        }
    }

    public int ConnectionsRemove(string id, bool json)
    {
        try
        {
            RuntimeConnection? removed = new RuntimeConnections().Remove(id);
            if (removed is null)
            {
                return context.Fail($"No connection {id} on this machine; 'vericairn connections list' shows them.");
            }

            if (json)
            {
                context.Output.WriteLine(JsonSerializer.Serialize(new { removed = removed.Id, address = removed.Address, device = removed.Device, principal = removed.Principal }, ProtocolJson.Options));
                return Clean;
            }

            context.Output.WriteLine($"removed connection {removed.Id} to {removed.Address}");
            context.Output.WriteLine($"  device {removed.Device} stays admitted until an administrator revokes it ('vericairn devices revoke {removed.Device} --by <administrator> --reason <why>'): disconnection is not revocation; its key stays in this machine's custody");
            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return context.Fail(exception.Message);
        }
    }

    private static object ConnectionJson(ConnectionStatus status) => new
    {
        id = status.Connection.Id,
        address = status.Connection.Address,
        estate = status.Connection.Estate,
        estateName = status.Connection.EstateName,
        identity = RuntimeIdentityScheme.Fingerprint(status.Connection.Identity),
        device = status.Connection.Device,
        principal = status.Connection.Principal,
        principalName = status.Connection.PrincipalName,
        profiles = status.Connection.Profiles,
        pairedAt = status.Connection.PairedAt,
        state = status.State,
        message = status.Message,
        lastCurrent = status.Connection.LastCurrent?.At,
    };
}
