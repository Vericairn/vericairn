// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Daemon;

/// <summary>One write a paired device made over the local network, as the runtime that carried it out kept it.</summary>
/// <param name="At">When the runtime answered it.</param>
/// <param name="Device">The paired device that signed the request: its identity.</param>
/// <param name="Principal">The principal it acted for.</param>
/// <param name="Operation">The operation, as its method and route, for example POST /v1/records.</param>
/// <param name="Record">The record the write produced, when it produced one: its id.</param>
/// <param name="Hash">That record's content hash.</param>
/// <param name="Path">That record's repository-relative path.</param>
public sealed record LocalNetworkWrite(string At, string Device, string Principal, string Operation, string? Record = null, string? Hash = null, string? Path = null);

/// <summary>
/// The writes paired devices made over the local network, from 0.6.0: a record the runtime writes for a paired device is signed by
/// a device of the same principal whose key the runtime's machine holds, never by the paired device, so the runtime keeps which
/// device asked for it here. One JSON object per line, appended, in a file beside the live store, readable by its owner only; it
/// outlives the device's revocation. 'vericairn devices writes' lists it.
/// </summary>
public sealed class LocalNetworkLedger
{
    /// <summary>File name beside the live store.</summary>
    public const string FileName = "local-network-writes.jsonl";

    private readonly string path;
    private readonly object gate = new();

    /// <summary>The ledger kept in a directory beside a live store.</summary>
    public LocalNetworkLedger(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        path = System.IO.Path.Combine(directory, FileName);
    }

    /// <summary>Appends a write; the file is created readable by its owner only.</summary>
    public void Append(LocalNetworkWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);
        byte[] line = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(write, ProtocolJson.Options) + "\n");
        lock (gate)
        {
            var options = new FileStreamOptions { Mode = FileMode.Append, Access = FileAccess.Write, Share = FileShare.Read };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using var stream = new FileStream(path, options);
            stream.Write(line);
        }
    }

    /// <summary>Every write the ledger in a directory holds, oldest first; none when there is no ledger.</summary>
    public static IReadOnlyList<LocalNetworkWrite> Read(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        string file = System.IO.Path.Combine(directory, FileName);
        if (!File.Exists(file))
        {
            return [];
        }

        var writes = new List<LocalNetworkWrite>();
        foreach (string line in File.ReadLines(file).Where(l => l.Length > 0))
        {
            try
            {
                if (JsonSerializer.Deserialize<LocalNetworkWrite>(line, ProtocolJson.Options) is LocalNetworkWrite write)
                {
                    writes.Add(write);
                }
            }
            catch (JsonException)
            {
                // A line cut short by a crash is not a write.
            }
        }

        return writes;
    }
}
