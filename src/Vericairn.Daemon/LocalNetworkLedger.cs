// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Identity;

namespace Vericairn.Daemon;

/// <summary>One write a paired device made over the local network, as the runtime that carried it out kept it.</summary>
/// <param name="At">When the runtime was asked for it.</param>
/// <param name="Device">The paired device that signed the request: its identity.</param>
/// <param name="Principal">The principal it acted for.</param>
/// <param name="Operation">The operation, as its method and route, for example POST /v1/records.</param>
/// <param name="Record">The record the write produced, when it produced one: its id; for a run's stop, the run's.</param>
/// <param name="Hash">That record's content hash.</param>
/// <param name="Path">That record's repository-relative path.</param>
/// <param name="Id">The write's id in the ledger, which its intent and its outcome share.</param>
/// <param name="State">
/// pending (the intent is kept and the outcome not yet), written (the operation did what it was asked), failed (it answered with a
/// refusal or an error, and wrote nothing it reports), or interrupted (the runtime stopped before the outcome was kept).
/// </param>
/// <param name="Candidates">For an interrupted write: the records its principal authored from its time until the runtime started again, one of which it may have produced.</param>
public sealed record LocalNetworkWrite(string At, string Device, string Principal, string Operation, string? Record = null, string? Hash = null, string? Path = null, string? Id = null, string? State = null, IReadOnlyList<string>? Candidates = null);

/// <summary>
/// The writes paired devices made over the local network, from 0.6.0: a record the runtime writes for a paired device is signed by
/// a device of the same principal whose key the runtime's machine holds, never by the paired device, so the runtime keeps which
/// device asked for it here. Before a write the runtime keeps its intent, flushed to disk, and refuses the write when it cannot;
/// after it, the outcome under the same id; and when it starts, it closes every intent left without an outcome as interrupted,
/// naming the records that write may have produced. One JSON object per line, appended, in a file beside the live store, readable
/// by its owner only; it outlives the device's revocation. 'vericairn devices writes' lists it.
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

    /// <summary>
    /// Keeps the intent of a write a paired device asks for, flushed to disk, and returns its id; throws <see cref="IOException"/> or
    /// <see cref="UnauthorizedAccessException"/> when it cannot be kept, and then the write must not happen.
    /// </summary>
    public string Intend(string at, string device, string principal, string operation)
    {
        string id = Ulid.NewUlid().Value;
        Append(new LocalNetworkWrite(at, device, principal, operation, Id: id, State: "pending"));
        return id;
    }

    /// <summary>Keeps the outcome of a write whose intent was kept, flushed to disk.</summary>
    public void Settle(LocalNetworkWrite outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        Append(outcome);
    }

    /// <summary>
    /// Closes every intent left without an outcome, as a runtime does when it starts: each is kept as interrupted, with the records its
    /// principal authored since, which <paramref name="since"/> gives. Returns how many it closed.
    /// </summary>
    public int Reconcile(string at, Func<string, string, IReadOnlyList<string>> since)
    {
        ArgumentNullException.ThrowIfNull(since);
        int closed = 0;
        foreach (LocalNetworkWrite open in ReadLines(path).Where(w => w.State == "pending" && w.Id is not null).GroupBy(w => w.Id).Select(g => g.First()))
        {
            if (ReadLines(path).Any(w => w.Id == open.Id && w.State != "pending"))
            {
                continue;
            }

            Append(open with { At = at, State = "interrupted", Candidates = since(open.Principal, open.At) });
            closed++;
        }

        return closed;
    }

    /// <summary>
    /// Every write the ledger in a directory holds, oldest first, each once: its intent's time and fields, with its outcome's state
    /// and record; none when there is no ledger.
    /// </summary>
    public static IReadOnlyList<LocalNetworkWrite> Read(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var writes = new List<LocalNetworkWrite>();
        var byId = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (LocalNetworkWrite line in ReadLines(System.IO.Path.Combine(directory, FileName)))
        {
            if (line.Id is null)
            {
                writes.Add(line with { State = line.State ?? "written" });
            }
            else if (byId.TryGetValue(line.Id, out int index))
            {
                LocalNetworkWrite intent = writes[index];
                writes[index] = intent with { State = line.State, Record = line.Record ?? intent.Record, Hash = line.Hash ?? intent.Hash, Path = line.Path ?? intent.Path, Candidates = line.Candidates ?? intent.Candidates };
            }
            else
            {
                byId[line.Id] = writes.Count;
                writes.Add(line);
            }
        }

        return writes;
    }

    private static IEnumerable<LocalNetworkWrite> ReadLines(string file)
    {
        if (!File.Exists(file))
        {
            yield break;
        }

        foreach (string line in File.ReadLines(file).Where(l => l.Length > 0))
        {
            LocalNetworkWrite? write;
            try
            {
                write = JsonSerializer.Deserialize<LocalNetworkWrite>(line, ProtocolJson.Options);
            }
            catch (JsonException)
            {
                // A line cut short by a crash is not a write.
                continue;
            }

            if (write is not null)
            {
                yield return write;
            }
        }
    }

    /// <summary>Appends one line, flushed to disk before it returns; the file is created readable by its owner only.</summary>
    private void Append(LocalNetworkWrite write)
    {
        byte[] line = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(write, ProtocolJson.Options) + "\n");
        lock (gate)
        {
            var options = new FileStreamOptions { Mode = FileMode.Append, Access = FileAccess.Write, Share = FileShare.Read, Options = FileOptions.WriteThrough };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using var stream = new FileStream(path, options);
            stream.Write(line);
            stream.Flush(flushToDisk: true);
        }
    }
}
