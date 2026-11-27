// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Sdk;

namespace Vericairn.Remote;

public sealed partial class LocalNetworkConnections
{
    private static readonly object InterventionStorageGate = new();

    /// <inheritdoc />
    public IReadOnlyList<RemoteInterventionReceipt> LoadInterventions(RuntimeConnection connection)
    {
        lock (InterventionStorageGate) return ReadInterventions(connection);
    }

    /// <inheritdoc />
    public void SaveIntervention(RuntimeConnection connection, RemoteInterventionReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        lock (InterventionStorageGate)
        {
            var receipts = ReadInterventions(connection).ToDictionary(r => r.Request.Key, StringComparer.Ordinal);
            if (receipts.TryGetValue(receipt.Request.Key, out RemoteInterventionReceipt? previous) &&
                (previous.Id != receipt.Id || previous.Request != receipt.Request))
                throw new InvalidOperationException("This intervention was already attempted. Reconcile the original request.");

            receipts[receipt.Request.Key] = receipt;
            string path = InterventionPath(connection);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new InterventionStorage(Binding(connection), receipts.Values.ToArray()), ProtocolJson.Options);
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    file.Write(bytes);
                    file.Flush(flushToDisk: true);
                }
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    private IReadOnlyList<RemoteInterventionReceipt> ReadInterventions(RuntimeConnection connection)
    {
        try
        {
            InterventionStorage? saved = JsonSerializer.Deserialize<InterventionStorage>(File.ReadAllBytes(InterventionPath(connection)), ProtocolJson.Options);
            if (saved is null || saved.Binding != Binding(connection) || saved.Receipts is null ||
                saved.Receipts.Any(r => r is not { Request: { Target: not null, Revision: not null, KnownState: not null, Effect: not null }, Id: not null, State: not null, Observation: not null } ||
                    r.Request.Operation is not ("Pause goal" or "Cancel run" or "Contain run") || !Guid.TryParseExact(r.Id, "N", out _)) ||
                saved.Receipts.Select(r => r.Request.Key).Distinct(StringComparer.Ordinal).Count() != saved.Receipts.Count)
                throw new InvalidDataException("The intervention records are incomplete. Recover them before submitting another request.");
            return saved.Receipts;
        }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
    }

    private string InterventionPath(RuntimeConnection connection) =>
        Path.Combine(Path.GetDirectoryName(observations)!, "remote-interventions", Binding(connection) + ".json");

    private sealed record InterventionStorage(string Binding, IReadOnlyList<RemoteInterventionReceipt> Receipts);
}
