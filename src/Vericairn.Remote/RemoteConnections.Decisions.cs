// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Sdk;

namespace Vericairn.Remote;

public sealed partial class LocalNetworkConnections
{
    // Serializes read/replace across connection stores in this app process. The platform hosts one app process.
    private static readonly object DecisionStorageGate = new();

    /// <inheritdoc />
    public IReadOnlyList<RemoteDecisionDraft> LoadDecisions(RuntimeConnection connection)
    {
        lock (DecisionStorageGate) return ReadDecisions(connection);
    }

    /// <inheritdoc />
    public void SaveDecision(RuntimeConnection connection, RemoteDecisionDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        lock (DecisionStorageGate)
        {
            var drafts = ReadDecisions(connection).ToDictionary(d => d.Key, StringComparer.Ordinal);
            if (drafts.TryGetValue(draft.Key, out RemoteDecisionDraft? previous) && previous.Attempted &&
                (draft.Prepared?.Id != previous.Prepared!.Id || draft.Prepared.Hash != previous.Prepared.Hash ||
                 previous.Receipt is not null && draft.Receipt != previous.Receipt ||
                 ImmutableSubmission(previous) != ImmutableSubmission(draft)))
                throw new InvalidOperationException("This original was already attempted. Reopen it and reconcile its retained identity; it cannot be replaced.");
            drafts[draft.Key] = draft;
            string path = DecisionPath(connection);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new DecisionStorage(1, Binding(connection), drafts.Values.ToArray()), ProtocolJson.Options);
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

    private IReadOnlyList<RemoteDecisionDraft> ReadDecisions(RuntimeConnection connection)
    {
        string path = DecisionPath(connection);
        if (!File.Exists(path)) return [];
        DecisionStorage? saved = JsonSerializer.Deserialize<DecisionStorage>(File.ReadAllText(path), ProtocolJson.Options);
        if (saved is not { Version: 1, Drafts: not null } || saved.Binding != Binding(connection) ||
            saved.Drafts.Any(d => d is not { Key: not null, Original.Subject.Envelope.ValueKind: JsonValueKind.Object,
                Review.Subject.Envelope.ValueKind: JsonValueKind.Object, Statement: not null, State: not null, Message: not null } ||
                !Complete(d.Original) || !Complete(d.Review) || d.Prepared is not null && d.Request is null ||
                d.Receipt is not null && d.Prepared is null ||
                d.State is not ("draft" or "rejected" or "uncertain" or "accepted for processing" or "completed") ||
                d.State != "draft" && d.Prepared is null && d.State != "rejected") ||
            saved.Drafts.Select(d => d.Key).Distinct(StringComparer.Ordinal).Count() != saved.Drafts.Count)
            throw new InvalidDataException("Decision storage is incomplete. Recover its original submissions before signing another record.");
        return saved.Drafts;
    }

    private static bool Complete(RemoteDecisionReview review) => review is { Root: not null, AsOf: not null, Material: not null, Evidence: not null, Basis: not null, Reasons: not null } &&
        review.Material.All(r => r is { Id: not null, Hash: not null, Envelope.ValueKind: JsonValueKind.Object }) &&
        review.Evidence.All(e => e is { Hash: not null, Text: not null });

    private string DecisionPath(RuntimeConnection connection) => Path.Combine(Path.GetDirectoryName(observations)!, "remote-decisions", Binding(connection) + ".json");
    private static string ImmutableSubmission(RemoteDecisionDraft draft) => CanonicalJson.Serialize(JsonSerializer.SerializeToElement(new
    {
        draft.Key, draft.Original, draft.Review, draft.Statement, draft.Stake, draft.Request, draft.Prepared,
    }, ProtocolJson.Options));
    private sealed record DecisionStorage(int Version, string Binding, IReadOnlyList<RemoteDecisionDraft> Drafts);
}
