// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Sdk;

namespace Vericairn.Remote;

public sealed partial class LocalNetworkConnections
{
    private static readonly object HandoffStorageGate = new();

    /// <inheritdoc />
    public IReadOnlyList<DesktopHandoffRequest> LoadPendingHandoffs(RuntimeConnection connection)
    {
        lock (HandoffStorageGate) return ReadPendingHandoffs(connection);
    }

    /// <inheritdoc />
    public void KeepPendingHandoff(RuntimeConnection connection, DesktopHandoffRequest request)
    {
        lock (HandoffStorageGate)
        {
            List<DesktopHandoffRequest> pending = ReadPendingHandoffs(connection);
            if (pending.Find(item => item.Subject.Type == request.Subject.Type && item.Subject.Id == request.Subject.Id) is { } original)
            {
                if (original with { Evidence = null } != request with { Evidence = null } ||
                    !(original.Evidence ?? []).SequenceEqual(request.Evidence ?? []))
                    throw new InvalidDataException("This subject already has an original preparation. Reopen it to check the outcome.");
                return;
            }

            pending.Add(request);
            WritePendingHandoffs(connection, pending);
        }
    }

    /// <inheritdoc />
    public void ForgetPendingHandoff(RuntimeConnection connection, string requestId)
    {
        lock (HandoffStorageGate)
        {
            List<DesktopHandoffRequest> pending = ReadPendingHandoffs(connection);
            if (pending.RemoveAll(request => request.RequestId == requestId) > 0)
                WritePendingHandoffs(connection, pending);
        }
    }

    private List<DesktopHandoffRequest> ReadPendingHandoffs(RuntimeConnection connection)
    {
        try
        {
            HandoffStorage? saved = JsonSerializer.Deserialize<HandoffStorage>(File.ReadAllBytes(PendingHandoffsPath(connection)), ProtocolJson.Options);
            if (saved is null || saved.Binding != Binding(connection) || saved.Requests is null ||
                saved.Requests.Any(request => request is not { Subject: { Type: not null, Id: not null, Hash: not null }, Task: not null, Reason: not null } ||
                    request.By != connection.Principal || !Guid.TryParseExact(request.RequestId, "N", out _)) ||
                saved.Requests.Select(request => (request.Subject.Type, request.Subject.Id)).Distinct().Count() != saved.Requests.Count)
                throw new InvalidDataException("The original handoff preparations could not be read.");
            return saved.Requests;
        }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
    }

    private void WritePendingHandoffs(RuntimeConnection connection, List<DesktopHandoffRequest> pending)
    {
        string path = PendingHandoffsPath(connection);
        if (pending.Count == 0)
        {
            File.Delete(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new HandoffStorage(Binding(connection), pending), ProtocolJson.Options);
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var file = new FileStream(temporary, options))
            {
                file.Write(bytes);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private string PendingHandoffsPath(RuntimeConnection connection) =>
        Path.Combine(Path.GetDirectoryName(observations)!, "remote-handoffs", Binding(connection) + ".json");

    private sealed record HandoffStorage(string Binding, List<DesktopHandoffRequest> Requests);
}
