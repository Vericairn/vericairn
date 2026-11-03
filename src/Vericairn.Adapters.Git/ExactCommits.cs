// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using Vericairn.Runtime.Policies;

namespace Vericairn.Adapters.Git;

/// <summary>
/// Complete exact-commit inventories already read from each clone, kept for later evaluations. A commit id names its tree
/// and every blob is named by its content, so a commit's files never change; only whether the clone still holds the commit
/// can, and the reader confirms that at every read. Blobs shared by several retained commits of a clone are held once. Past
/// the byte bound, the least recently used commits are released and read again when next needed.
/// </summary>
internal sealed class ExactCommits(long capacity)
{
    private readonly object gate = new();
    private readonly Dictionary<(string Clone, string Commit), LinkedListNode<Inventory>> inventories = [];
    private readonly LinkedList<Inventory> recent = new();
    private readonly Dictionary<(string Clone, string Id), Blob> blobs = [];
    private long bytes;

    /// <summary>One blob's SHA-256 identity and exact bytes, and how many retained commits hold it.</summary>
    internal sealed class Blob(string hash, ImmutableArray<byte> bytes)
    {
        internal string Hash { get; } = hash;
        internal ImmutableArray<byte> Bytes { get; } = bytes;
        internal int Uses { get; set; }
    }

    private sealed record Inventory(string Directory, string Commit, ImmutableArray<PolicyFile> Files, ImmutableArray<string> Ids);

    /// <summary>The bytes the retained blobs hold.</summary>
    internal long RetainedBytes
    {
        get { lock (gate) return bytes; }
    }

    /// <summary>A retained complete inventory of the commit in that clone, or null.</summary>
    internal ImmutableArray<PolicyFile>? Files(string clone, string commit)
    {
        lock (gate)
        {
            if (!inventories.TryGetValue((clone, commit), out LinkedListNode<Inventory>? node)) return null;
            recent.Remove(node);
            recent.AddFirst(node);
            return node.Value.Files;
        }
    }

    /// <summary>A blob a retained commit of that clone holds, or null.</summary>
    internal Blob? Held(string clone, string id)
    {
        lock (gate) return blobs.GetValueOrDefault((clone, id));
    }

    /// <summary>Retains a complete inventory and its blobs by object id, unless it alone exceeds the bound.</summary>
    internal void Keep(string clone, string commit, ImmutableArray<PolicyFile> files, IReadOnlyDictionary<string, Blob> read)
    {
        lock (gate)
        {
            if (inventories.ContainsKey((clone, commit))) return;
            long added = read.Where(b => !blobs.ContainsKey((clone, b.Key))).Sum(b => (long)b.Value.Bytes.Length);
            if (read.Values.Sum(b => (long)b.Bytes.Length) > capacity) return;
            foreach ((string id, Blob blob) in read)
            {
                if (!blobs.TryGetValue((clone, id), out Blob? held)) blobs[(clone, id)] = held = blob;
                held.Uses++;
            }
            bytes += added;
            inventories[(clone, commit)] = recent.AddFirst(new Inventory(clone, commit, files, [.. read.Keys]));
            while (bytes > capacity && recent.Last is { } oldest && oldest != recent.First) Release(oldest);
        }
    }

    private void Release(LinkedListNode<Inventory> node)
    {
        recent.Remove(node);
        inventories.Remove((node.Value.Directory, node.Value.Commit));
        foreach (string id in node.Value.Ids)
        {
            Blob blob = blobs[(node.Value.Directory, id)];
            if (--blob.Uses > 0) continue;
            blobs.Remove((node.Value.Directory, id));
            bytes -= blob.Bytes.Length;
        }
    }
}
